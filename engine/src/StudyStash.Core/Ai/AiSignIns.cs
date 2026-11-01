namespace StudyStash.Core.Ai;

/// <summary>Where an AI's sign-in is, as the library says it to the app: <see cref="Waiting"/> for the student to finish
/// in the browser page that opened, <see cref="GaveUp"/> (it stopped waiting, or took too long), <see cref="Region"/> (its
/// maker doesn't offer it in this country) or <see cref="Stuck"/> (Syft Notes couldn't start it, or it stopped at once:
/// Try again, and Get help). "" when none is going.</summary>
public static class SignInPhase
{
    public const string Waiting = "waiting";
    public const string GaveUp = "gave_up";
    public const string Region = "region";
    public const string Stuck = "stuck";
}

/// <summary>
/// Every Sign in button in Syft Notes (Settings → Your AI, the AI problems card, setup's rows) starts the AI's own
/// sign-in here, hidden: <c>claude auth login</c>, <c>codex login</c> or Gemini's <c>agy</c> in Syft Notes' own home
/// run in a terminal nobody sees, and the provider opens the student's browser itself (<see cref="AgentSignIn"/>). Syft Notes never
/// sees the password, a token or the code the page ends on, and no field asks for it. One sign-in runs at a time per AI
/// (pressing again does nothing), it's looked at every few seconds with the AI's own status command, and the moment it
/// says signed in the AI is marked ready, so rows and cards turn ready by themselves. A sign-in that gives up or isn't
/// offered in the student's country stays said in <see cref="State"/> until they try again.
/// </summary>
public sealed class AiSignIns(EngineChecks checks, Action<string> signedIn) : IDisposable
{
    /// <summary>How long the library waits for a student to finish before it stops looking.</summary>
    public TimeSpan Patience { get; init; } = TimeSpan.FromMinutes(10);
    /// <summary>How often the AI's status command is asked while one waits.</summary>
    public TimeSpan Every { get; init; } = TimeSpan.FromSeconds(3);
    /// <summary>A sign-in that stops this soon, unsigned, couldn't open the browser (see <see cref="AgentSignIn.QuickFail"/>).</summary>
    public TimeSpan QuickFail { get; init; } = TimeSpan.FromSeconds(20);

    sealed class Entry(AgentSignIn signIn)
    {
        public AgentSignIn SignIn { get; } = signIn;
        public CancellationTokenSource Cts { get; } = new();
        public bool Over { get; set; }
        public Task Watcher { get; set; } = Task.CompletedTask;
    }

    readonly Lock gate = new();
    readonly Dictionary<string, Entry> entries = [];

    /// <summary>The AI's own sign-in for <paramref name="id"/> on this computer, or null when it isn't installed (or has no
    /// sign-in of its own, like Ollama).</summary>
    AgentSignIn? Make(string id)
    {
        if (id is not ("claude" or "codex" or "gemini")) return null;
        var cli = AgentCli.Get(id);
        return checks.Which(cli.Binary) is { } exe ? new AgentSignIn(cli, exe) { Every = Every, QuickFail = QuickFail } : null;
    }

    /// <summary>Whether this computer has a way to sign in to <paramref name="id"/> on its own.</summary>
    public bool Can(string id) => id is "claude" or "codex" or "gemini" && checks.Which(AgentCli.Get(id).Binary) is not null;

    /// <summary>The words for a sign-in just started: plain, and the same for every AI.</summary>
    public static string StartedWords(string id) => $"Finish signing in to {Engines.Name(id)} in the browser page that just opened.";

    /// <summary>Starts <paramref name="id"/>'s sign-in, hidden, unless one is going already (then nothing more starts).
    /// What to tell the student; throws <see cref="InvalidOperationException"/> when it isn't installed here.</summary>
    public string Start(string id) => Run(id, again: false);

    /// <summary>"Open the page again": a page that finishes by itself opened once more, or the sign-in stopped and run once
    /// more, which asks the system to open the browser again (the page Claude Code prints ends on a code to paste: nothing
    /// here shows or takes that code).</summary>
    public string Again(string id) => Run(id, again: true);

    string Run(string id, bool again)
    {
        lock (gate)
        {
            if (entries.TryGetValue(id, out var live) && !live.Over)
            {
                if (again) live.SignIn.OpenAgain();
                else if (!live.SignIn.Running) live.SignIn.Start();
                return StartedWords(id);
            }
            var signIn = Make(id) ?? throw new InvalidOperationException($"{Engines.Tool(id)} isn't installed on this computer.");
            if (entries.Remove(id, out var old))
            {
                old.Cts.Cancel();
                old.SignIn.Dispose();
            }
            var entry = new Entry(signIn);
            entries[id] = entry;
            signIn.Start();
            entry.Cts.CancelAfter(Patience);
            entry.Watcher = Task.Run(() => WatchAsync(id, entry));
            return StartedWords(id);
        }
    }

    async Task WatchAsync(string id, Entry entry)
    {
        bool done = false;
        try
        {
            done = await entry.SignIn.WaitAsync(entry.Cts.Token);
        }
        catch (Exception e) when (e is IOException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
        }
        lock (gate)
        {
            if (!ReferenceEquals(entries.GetValueOrDefault(id), entry)) return; // another sign-in took over
            if (done) entries.Remove(id);
            entry.Over = true;
        }
        entry.SignIn.Stop();
        if (done)
        {
            entry.SignIn.Dispose();
            signedIn(id);
        }
    }

    /// <summary>Where <paramref name="id"/>'s sign-in is now (<see cref="SignInPhase"/>), "" when none was started or it
    /// worked.</summary>
    public string State(string id)
    {
        lock (gate)
        {
            if (!entries.TryGetValue(id, out var e)) return "";
            if (!e.Over) return e.SignIn.Stuck ? SignInPhase.Stuck : SignInPhase.Waiting;
            return e.SignIn.Region ? SignInPhase.Region : e.SignIn.Stuck ? SignInPhase.Stuck : SignInPhase.GaveUp;
        }
    }

    /// <summary>The overview with each AI's sign-in state put on its row and on its "not signed in" card.</summary>
    public AiOverview Annotate(AiOverview overview) => overview with
    {
        Engines = [.. overview.Engines.Select(e => State(e.Id) is { Length: > 0 } s ? e with { SignIn = s } : e)],
        Problems = [.. overview.Problems.Select(p => p.Kind == "not_signed_in" && State(p.Engine) is { Length: > 0 } s ? p with { SignIn = s } : p)],
    };

    /// <summary>Stops every sign-in still waiting (the library is closing).</summary>
    public void Dispose()
    {
        List<Entry> all;
        lock (gate)
        {
            all = [.. entries.Values];
            entries.Clear();
        }
        foreach (var e in all)
        {
            e.Cts.Cancel();
            e.SignIn.Dispose();
        }
    }
}
