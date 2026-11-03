using StudyStash.App.Services.Kit;
using StudyStash.App.Services.Stores;
using StudyStash.Core;
using StudyStash.Core.Ai;

namespace StudyStash.App.Services;

/// <summary>The steps of Connect, as a card or a setup screen follows them.</summary>
public static class ConnectStep
{
    public const string Installing = "installing";
    /// <summary>Syft Notes is being added to the AI's apps on this computer.</summary>
    public const string Apps = "apps";
    public const string Checking = "checking";
    /// <summary>The AI's own sign-in, hidden: it has opened the student's browser and waits for them there.</summary>
    public const string SigningIn = "signing_in";
    public const string Done = "done";
}

/// <summary>Where Connect has got to: its step, and the line a student reads ("Installing Claude…").</summary>
public sealed record ConnectProgress(string Step, string Words);

/// <summary>How Connect ended: whether the AI is ready, what to say, the one thing the student still has to do (quit an
/// app and open it again), and the apps it connected, in the student's words.</summary>
public sealed record ConnectOutcome(bool Ok, string Words)
{
    /// <summary>The one thing left for the student, said once and plainly; "" when there's nothing.</summary>
    public string Next { get; init; } = "";
    /// <summary>The apps Syft Notes is in now ("Claude app", "Claude Code").</summary>
    public IReadOnlyList<string> Apps { get; init; } = [];
    /// <summary>Why an install stopped (none when it didn't run or worked).</summary>
    public InstallFailure Install { get; init; }
    /// <summary>Where the AI's sign-in stopped, when it did (<see cref="SignInPhase"/>).</summary>
    public string SignIn { get; init; } = "";
    /// <summary>The AI's state as the library last said it.</summary>
    public AiOverview? Overview { get; init; }
}

/// <summary>
/// One Connect per AI (Claude, ChatGPT, Gemini), which connects everything of that AI on this computer, and is safe to
/// press again (it puts right whatever is wrong and leaves what's right alone):
/// <list type="number">
/// <item>installs its command-line tool with its maker's installer when it isn't here (<see cref="AgentInstall"/>);</item>
/// <item>adds Syft Notes to every app of that AI on this computer, in the file each one really reads, and checks the
/// server they'll start answers (<see cref="ClaudeSetup"/>, <see cref="McpProbe"/>);</item>
/// <item>signs in, hidden, in the student's browser, and waits for them to finish there (the library's sign-in);</item>
/// <item>makes it the AI that writes the notes when none is chosen yet.</item>
/// </list>
/// Settings' cards and setup's AI step both call <see cref="ConnectAsync"/>; Ollama keeps its own Set up.
/// </summary>
public sealed class AiConnect(IAiLibrary library)
{
    /// <summary>The AI apps on this computer; null: none are touched (a library on another computer's AI, a test).</summary>
    public ClaudeSetup? Apps { get; init; }
    /// <summary>Installs an AI's tool on this computer, saying where it's got to; null where the library is on another
    /// computer (it's installed there).</summary>
    public Func<string, Action<string>, Task<InstallOutcome>>? Install { get; init; }
    /// <summary>Which computer's words to use for quitting an app (Windows: from the tray by the clock).</summary>
    public bool Windows { get; init; } = OperatingSystem.IsWindows();
    /// <summary>How often the library is asked while the student signs in, and how long that's waited for.</summary>
    public TimeSpan Every { get; init; } = TimeSpan.FromSeconds(2);
    public TimeSpan SignInWait { get; init; } = TimeSpan.FromMinutes(15);
    /// <summary>The clock those are measured on (a test's moves by hand).</summary>
    public TimeProvider Clock { get; init; } = TimeProvider.System;
    /// <summary>Whether Connect waits for the student to finish signing in, in their browser (setup's AI step does); Settings'
    /// card doesn't need to: its own watch turns it ready by itself, and the library makes the first AI signed in the one
    /// that writes the notes.</summary>
    public bool WaitForSignIn { get; init; } = true;

    /// <summary>AIs whose last sign-in happened where Syft Notes can't see it finish: checked before signing in again.</summary>
    readonly HashSet<string> checkFirst = [];

    /// <summary>The AI as students know it.</summary>
    public static string Brand(string id) => id switch { "codex" => "ChatGPT", "gemini" => "Gemini", "ollama" => "Ollama", _ => "Claude" };

    /// <summary>The words for quitting a desktop app that loads its settings only when it starts, on this computer.</summary>
    public static string QuitWords(string app, bool windows) =>
        $"Quit {app} fully and open it again." + (windows ? " Quit it from the tray icon by the clock." : "");

    /// <summary>
    /// Connects <paramref name="id"/> ("claude", "codex", "gemini"), saying each step to <paramref name="progress"/>.
    /// Never throws for what a student can meet (offline, refused, an install that stopped): every way it ends is an
    /// outcome with words for them.
    /// </summary>
    public async Task<ConnectOutcome> ConnectAsync(string id, Action<ConnectProgress>? progress = null, CancellationToken ct = default)
    {
        string brand = Brand(id);
        string said = "";
        void Say(string step, string words)
        {
            if (words == said) return; // an installer's own steps all read "Installing Claude…"
            said = words;
            progress?.Invoke(new ConnectProgress(step, words));
        }
        AiOverview? overview;
        try
        {
            overview = await library.EnginesAsync();
            var engine = overview?.Engines.FirstOrDefault(e => e.Id == id);

            // 1 · Its tool, with its maker's installer, when it isn't here.
            if (engine is { Installed: false } && Install is { } install)
            {
                Say(ConnectStep.Installing, $"Installing {brand}…");
                var installed = await install(id, _ => Say(ConnectStep.Installing, $"Installing {brand}…"));
                if (!installed.Ok) return new ConnectOutcome(false, installed.Words) { Install = installed.Failure, Overview = overview };
                overview = await library.EnginesAsync();
                engine = overview?.Engines.FirstOrDefault(e => e.Id == id);
                // Just installed: nothing to check, it can only be signed out (its sign-in comes straight after).
                if (engine is not null && Is(engine, AiEngineState.Unchecked)) engine = engine with { State = AiStatusStore.Raw(AiEngineState.NotSignedIn) };
            }
            if (engine is { Installed: false } && Install is null)
                return new ConnectOutcome(false, $"Install {brand} on your library's computer, then connect it here.") { Overview = overview };
            ct.ThrowIfCancellationRequested();

            // 2 · Every app of this AI on this computer, then a look that the server they'll start answers.
            var (apps, appsSaid) = await ConnectAppsAsync(id, Say);
            ct.ThrowIfCancellationRequested();

            // 3 · Signed in: checked when that isn't known yet (or after a sign-in somewhere Syft Notes can't see), and the
            // hidden sign-in when it isn't.
            if (Is(engine, AiEngineState.Unchecked, AiEngineState.Failed) || Is(engine, AiEngineState.NotSignedIn) && checkFirst.Remove(id))
            {
                Say(ConnectStep.Checking, $"Checking {brand}…");
                overview = (await library.CheckAsync(id))?.Overview ?? await library.EnginesAsync();
                engine = overview?.Engines.FirstOrDefault(e => e.Id == id);
            }
            if (Is(engine, AiEngineState.NotSignedIn))
            {
                Say(ConnectStep.SigningIn, $"Signing in to {brand}… finish in your browser.");
                var started = await library.SignInAsync(id);
                overview = started?.Overview ?? await library.EnginesAsync();
                engine = overview?.Engines.FirstOrDefault(e => e.Id == id);
                // A library from before sign-ins were hidden opens a terminal for it (or says where to, from a laptop):
                // nothing here learns when that's done, so the next Connect checks first.
                if (Is(engine, AiEngineState.NotSignedIn) && engine!.SignIn.Length == 0)
                {
                    checkFirst.Add(id);
                    return new ConnectOutcome(false, started?.Said is { Length: > 0 } words ? words : $"Sign in to {brand}, then press Fix.") { Apps = apps, Overview = overview };
                }
                if (!WaitForSignIn && Waiting(engine))
                    return new ConnectOutcome(true, AiSignIns.StartedWords(id)) { SignIn = SignInPhase.Waiting, Apps = apps, Overview = overview, Next = Apps is null ? "" : NextStep(id) };
                overview = await SignedInAsync(id, overview, ct);
                engine = overview?.Engines.FirstOrDefault(e => e.Id == id);
                if (Is(engine, AiEngineState.NotSignedIn))
                {
                    string phase = engine!.SignIn.Length > 0 ? engine.SignIn : SignInPhase.GaveUp;
                    return new ConnectOutcome(false, ViewModels.AiWords.SignInSaid(brand, phase) is { Length: > 0 } w ? w : $"{brand} isn't signed in yet. Try again.")
                    {
                        SignIn = phase, Apps = apps, Overview = overview,
                    };
                }
            }

            if (Is(engine, AiEngineState.Failed))
                return new ConnectOutcome(false, engine!.Why.Length > 0 ? $"{brand} didn't work: {engine.Why.TrimEnd('.')}." : $"{brand} didn't work. Try again.")
                {
                    Apps = apps, Overview = overview,
                };

            // 4 · The AI that writes the notes, when none is chosen yet.
            if (overview is { Notes: "" } && engine is { Installed: true } && Is(engine, AiEngineState.Ready, AiEngineState.Unchecked))
                overview = await library.DefaultsAsync(notes: id, ask: overview.Ask.Length == 0 ? id : null) ?? overview;

            string next = Apps is null ? "" : NextStep(id);
            Say(ConnectStep.Done, "Done");
            return new ConnectOutcome(appsSaid is null, appsSaid ?? $"{brand} is connected.") { Apps = apps, Next = next, Overview = overview };
        }
        catch (OperationCanceledException)
        {
            return new ConnectOutcome(false, "Stopped.");
        }
        catch (LibraryRefusedException e)
        {
            return new ConnectOutcome(false, e.Message);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or IOException)
        {
            return new ConnectOutcome(false, "Your library isn't answering. Check it's on, then try again.");
        }
    }

    /// <summary>Adds Syft Notes to each of the AI's apps here that doesn't have it just so, then checks the server they'll
    /// start answers. The apps it's in now, and what went wrong, if anything (null when nothing did).</summary>
    async Task<(IReadOnlyList<string> Apps, string? Problem)> ConnectAppsAsync(string id, Action<string, string> say)
    {
        if (Apps is not { } setup) return ([], null);
        string? problem = null;
        foreach (var app in await Task.Run(() => setup.StatesOf(id)))
        {
            if (!app.Installed || app.Good) continue;
            say(ConnectStep.Apps, $"Connecting {app.Words}…");
            var done = await Task.Run(() => setup.Connect(app.Id));
            if (!done.Ok) problem ??= $"Syft Notes couldn't add itself to {app.Words}. {FirstSentence(done.Say)}";
        }
        var now = await Task.Run(() => setup.StatesOf(id));
        var good = now.Where(a => a.Good).ToList();
        if (good.Count > 0 && problem is null)
        {
            say(ConnectStep.Apps, $"Checking {string.Join(" and ", good.Select(a => a.Words))}…");
            if (await setup.CheckAsync(id) is { Ok: false })
                problem = $"Syft Notes couldn't start its connection for {string.Join(" and ", good.Select(a => a.Words))}. Try again, or get help.";
        }
        return ([.. good.Select(a => a.Words)], problem);
    }

    static string FirstSentence(string words) => words.Split(". ")[0].TrimEnd('.') + ".";

    /// <summary>Waits for the student to finish signing in, in their browser: until the AI is signed in, its sign-in
    /// stopped (gave up, couldn't start, not offered here), or <see cref="SignInWait"/> is up.</summary>
    async Task<AiOverview?> SignedInAsync(string id, AiOverview? overview, CancellationToken ct)
    {
        bool first = true;
        await Poll.UntilAsync(async _ =>
        {
            if (!first) overview = await library.EnginesAsync() ?? overview;
            first = false;
            return !Waiting(overview?.Engines.FirstOrDefault(e => e.Id == id));
        }, Every, SignInWait, ct, Clock);
        return overview;
    }

    static bool Is(EngineInfo? engine, params AiEngineState[] states) => engine is not null && states.Contains(AiStatusStore.StateOf(engine));

    /// <summary>Its sign-in waits for the student in their browser.</summary>
    static bool Waiting(EngineInfo? engine) => Is(engine, AiEngineState.NotSignedIn) && engine!.SignIn == SignInPhase.Waiting;

    /// <summary>The one thing left for the student: a desktop app of this AI that is open, and hasn't loaded Syft Notes
    /// since it was added, reads its settings only when it starts.</summary>
    string NextStep(string id)
    {
        foreach (var app in Apps!.StatesOf(id))
            if (app is { Good: true, Running: true, Loaded: false } && ClaudeSetup.DesktopApp(app.Id) is { } desktop)
                return QuitWords(desktop.Name, Windows);
        return "";
    }
}
