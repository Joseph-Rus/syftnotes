namespace StudyStash.Core.Ai;

/// <summary>
/// Signing in to Claude Code, Codex or Antigravity CLI, for guided setup and for every Sign in button in Syft Notes, with
/// the provider's own sign-in: <c>claude auth login</c>, <c>codex login</c> or <c>agy -p=/usage</c> runs in a terminal
/// nobody sees (<see cref="HiddenTerminal"/>), so a sign-in made for a terminal works without one ever showing, and it
/// opens the browser itself. Syft Notes never sees the password, a token or a code. What the sign-in shows is read the
/// way a person would read it (<see cref="SignInScreen"/>), its few simple questions answered the safe way; whether it's
/// worked is read from the status command's exit code (<see cref="AgentCli.ReadStatus"/>), every three seconds while
/// it waits and whenever the sign-in stops.
/// <para>
/// A sign-in that couldn't start, or stopped at once without working, sets <see cref="Stuck"/> ("Syft Notes couldn't
/// start Claude's sign-in"); one that waited for the browser and stopped by itself (agy's, after a minute) sets
/// <see cref="GaveUp"/>; one its maker refused in this country sets <see cref="Region"/>. Each ends the wait.
/// </para>
/// <para>
/// The sign-in page's address goes one place only: the system's browser opener, here (<see cref="OpenPage"/>), and only
/// for a page that finishes by itself (<see cref="SignInScreen.FinishesByItself"/>), when the sign-in said it couldn't
/// open the browser, or the student asked for the page again. It is never shown, logged, saved or put in the overview.
/// Claude Code's printed page ends on a code to paste, which Syft Notes never relays: its sign-in is run again instead.
/// </para>
/// </summary>
public sealed class AgentSignIn : IDisposable
{
    readonly AgentCliInfo cli;
    readonly string exe;
    readonly Lock gate = new();
    HiddenTerminal? child;
    SignInScreen? screen;
    DateTime started;
    int opened;
    TaskCompletionSource exited = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public AgentSignIn(AgentCliInfo cli, string exe)
    {
        this.cli = cli;
        this.exe = exe;
    }

    /// <summary>How often setup asks whether it's signed in yet.</summary>
    public TimeSpan Every { get; init; } = TimeSpan.FromSeconds(3);
    /// <summary>A sign-in that fails within this long couldn't start (it needs something the computer doesn't have, or
    /// it turned down how it was started).</summary>
    public TimeSpan QuickFail { get; init; } = TimeSpan.FromSeconds(20);
    /// <summary>Hands the sign-in page to the system's browser opener: the one place its address goes. A test (and CI,
    /// with the real CLIs) gives one that opens nothing.</summary>
    public Func<string, bool> OpenPage { get; init; } = page => Browser(page);

    /// <summary>The system's browser opener every sign-in starts with: the tests make it one that opens nothing, so no
    /// test can open a browser on the computer it runs on.</summary>
    internal static Func<string, bool> Browser { get; set; } = SystemBrowser;
    /// <summary>Each line the sign-in showed, plain: for CI's record of the real CLIs, which takes the addresses out
    /// before keeping anything. Nothing in the app listens.</summary>
    internal Action<string>? Shown { get; init; }

    /// <summary>The sign-in couldn't start, or stopped at once without working.</summary>
    public bool Stuck { get; private set; }
    /// <summary>The sign-in waited for the browser as long as it does and stopped without being signed in.</summary>
    public bool GaveUp { get; private set; }
    /// <summary>The sign-in stopped because its maker doesn't offer it in this country (what it showed said so; the
    /// words themselves are never kept or logged).</summary>
    public bool Region { get; private set; }
    /// <summary>It showed its sign-in page, or said it waits for the student in the browser.</summary>
    public bool Waiting => screen?.Waiting ?? false;
    /// <summary>It said it needed a real terminal (in the hidden one it should never say so).</summary>
    public bool WantedTerminal => screen?.WantsTerminal ?? false;
    /// <summary>The questions it asked that were answered for the student, in plain words.</summary>
    public IReadOnlyList<string> Answered
    {
        get
        {
            if (screen is not { } s) return [];
            lock (s) return [.. s.Answered];
        }
    }
    /// <summary>Whether its page finishes signing in by itself (so it's one Syft Notes may open).</summary>
    public bool PageFinishesByItself => screen?.PageFinishesByItself ?? false;
    /// <summary>How many times Syft Notes handed its page to the browser opener.</summary>
    public int Opened => Volatile.Read(ref opened);
    public bool Running => child is { HasExited: false };
    /// <summary>The sign-in's process, while it runs (CI looks for windows of its own).</summary>
    internal int? ProcessId => child?.Id;

    /// <summary>Asks the CLI whether it's signed in (its status command's exit code). Codex's words are never read
    /// (they can show part of a key); Claude Code's are only for how and on which plan.</summary>
    public AgentSignedIn Check() => AgentCli.ReadStatus(cli.Id, Machine.Run(exe, cli.StatusArgs, TimeSpan.FromSeconds(20), cli.Env()));

    /// <summary>Starts the sign-in in its hidden terminal (it opens the browser itself). Once at a time: again while it
    /// runs does nothing. False when it didn't start (<see cref="Stuck"/>).</summary>
    public bool Start()
    {
        if (Running) return true;
        Stuck = false;
        GaveUp = false;
        Region = false;
        // A fresh end to wait on, unless "again" made one already (so a wait never takes the old one's end for this one's).
        if (exited.Task.IsCompleted) exited = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var launch = OperatingSystem.IsWindows() ? WindowsCommand.For([exe, .. cli.SignInArgs]) : new Launch(exe, [.. cli.SignInArgs]);
        var env = HiddenTerminal.Environment([
            new("PATH", AiProvider.SearchPath()),
            new("CLAUDECODE", null), // started from inside Claude Code, claude would refuse to nest
            // Windows PowerShell, which a sign-in may run to open the browser, finds its own modules (PowerShell 7's
            // module path, handed down when Syft Notes was started from it, leaves it without Start-Process).
            .. OperatingSystem.IsWindows() ? [new KeyValuePair<string, string?>("PSModulePath", null)] : Array.Empty<KeyValuePair<string, string?>>(),
            .. cli.Env().Select(e => new KeyValuePair<string, string?>(e.Key, e.Value)),
        ]);
        var s = new SignInScreen();
        if (Shown is { } shown) s.Line += shown;
        HiddenTerminal t;
        try
        {
            t = HiddenTerminal.Start(launch, env);
        }
        catch (Exception e) when (e is IOException or DllNotFoundException or EntryPointNotFoundException or System.ComponentModel.Win32Exception)
        {
            child = null;
            Stuck = true;
            exited.TrySetResult();
            return false;
        }
        lock (gate)
        {
            child = t;
            screen = s;
        }
        started = DateTime.UtcNow;
        var at = started;
        var done = exited;
        t.Read(text => Showed(t, s, text));
        _ = Task.Run(async () =>
        {
            int code = await t.Exit;
            // What it showed last, read before it's judged (a refusal for the country comes just before it stops).
            await Task.WhenAny(t.Ended, Task.Delay(TimeSpan.FromSeconds(2)));
            // Only the sign-in still current, and not one Syft Notes stopped itself: one stopped to be run again, or
            // because the wait was over, isn't one that couldn't start.
            if (ReferenceEquals(child, t) && !ReferenceEquals(stopped, t) && code != 0)
            {
                if (s.Region) Region = true;
                else if (s.WantsTerminal || DateTime.UtcNow - at < QuickFail) Stuck = true;
                else GaveUp = true;
            }
            done.TrySetResult();
        });
        return true;
    }

    /// <summary>What the sign-in showed: the questions it asks answered, and its page opened when it said it couldn't open
    /// the browser itself (only a page that finishes by itself, once).</summary>
    void Showed(HiddenTerminal t, SignInScreen s, string text)
    {
        string reply;
        lock (s) reply = s.Read(text);
        if (reply.Length > 0) t.Type(reply);
        if (s.NoBrowser && s.PageFinishesByItself && s.Page is { } page && Interlocked.CompareExchange(ref opened, 1, 0) == 0)
            _ = Task.Run(() => OpenPage(page));
    }

    /// <summary>"Open the page again": a page that finishes by itself (Codex's) opens once more while its sign-in still
    /// waits; otherwise the sign-in is run again, the one going stopped first, and opens the browser itself (Claude
    /// Code's printed page ends on a code to paste, which setup has no field for). False when it didn't start.</summary>
    public bool OpenAgain()
    {
        if (Running && screen is { PageFinishesByItself: true, Page: { } page })
        {
            Interlocked.Increment(ref opened);
            _ = Task.Run(() => OpenPage(page));
            return true;
        }
        return Restart();
    }

    /// <summary>Runs the sign-in again, the one going stopped first. False when it didn't start.</summary>
    public bool Restart()
    {
        HiddenTerminal? old;
        lock (gate)
        {
            old = child;
            child = null;
            screen = null;
            exited = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }
        if (old is not null)
        {
            old.Dispose();
            old.Exit.Wait(TimeSpan.FromSeconds(3));
        }
        return Start();
    }

    /// <summary>Waits until it's signed in (true), checking every <see cref="Every"/> and when the sign-in stops, or
    /// until <paramref name="ct"/> ends the wait, or the sign-in stopped without it (false: <see cref="GaveUp"/>,
    /// <see cref="Region"/> or <see cref="Stuck"/>). <paramref name="changed"/> hears each check's answer.</summary>
    public async Task<bool> WaitAsync(CancellationToken ct, Action<AgentSignedIn>? changed = null)
    {
        while (!ct.IsCancellationRequested)
        {
            var now = await Task.Run(Check, CancellationToken.None);
            changed?.Invoke(now);
            if (now.SignedIn) return true;
            var waitingOn = exited;
            try
            {
                await Task.WhenAny(Task.Delay(Every, ct), waitingOn.Task.WaitAsync(ct));
            }
            catch (OperationCanceledException)
            {
                return false;
            }
            // Still going, or the one that stopped was replaced by "again": keep waiting.
            if (!waitingOn.Task.IsCompleted || !ReferenceEquals(waitingOn, exited)) continue;
            // It stopped: one last look, and the wait ends unless it's signed in (a sign-in that finished stops too).
            var last = await Task.Run(Check, CancellationToken.None);
            changed?.Invoke(last);
            if (last.SignedIn) return true;
            if (!Region && !Stuck) GaveUp = true;
            return false;
        }
        return false;
    }

    /// <summary>Stops the sign-in (and anything it started), if it's still going.</summary>
    public void Stop()
    {
        if (child is not { } t) return;
        stopped = t;
        t.Stop();
    }

    /// <summary>The sign-in Syft Notes stopped itself: its end says nothing about whether it could start.</summary>
    volatile HiddenTerminal? stopped;

    public void Dispose()
    {
        HiddenTerminal? t;
        lock (gate)
        {
            t = child;
            child = null;
            screen = null;
        }
        t?.Dispose();
    }

    /// <summary>The system's own browser opener, given the page as one argument (no shell reads it).</summary>
    static bool SystemBrowser(string page)
    {
        if (!Uri.TryCreate(page, UriKind.Absolute, out var u) || u.Scheme != "https") return false;
        Dialogs.OpenUrl(page);
        return true;
    }
}
