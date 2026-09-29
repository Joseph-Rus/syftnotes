using System.Diagnostics;
using System.Text;
using StudyStash.App.Platform;
using StudyStash.Core;
using StudyStash.Core.Ai;

namespace StudyStash.App.Services;

/// <summary>An AI's command-line tool on this computer: where it is and the version it says it is. <see cref="Works"/>
/// only when its <c>--version</c> answered.</summary>
public sealed record AgentFound(string? Exe, Version? Version, string VersionText)
{
    public bool Works => Exe is not null && Version is not null;
    public static readonly AgentFound None = new(null, null, "");
}

/// <summary>How an install ended: whether it worked, why not in the student's words, and the version it put here.</summary>
public sealed record InstallOutcome(bool Ok, InstallFailure Failure, string Words, AgentFound Found)
{
    /// <summary>The installer's last line, for the "other" failure and for support.</summary>
    public string LastLine { get; init; } = "";
}

/// <summary>
/// Installs Claude Code or Codex for guided setup: only when the student presses Install, with the maker's own
/// installer (<see cref="AgentCli"/>), for this account alone (never as an administrator). It says where it's got to
/// as it goes, keeps everything the installer printed for Details and <c>logs/setup-install.log</c>, gives up after
/// ten minutes, and on cancel stops the installer and everything it started. A failure is sorted into the few kinds
/// setup words differently: offline, a country the maker doesn't serve, out of memory, or anything else.
/// </summary>
public sealed class AgentInstall
{
    readonly AgentCliInfo cli;
    readonly AgentInstaller installer;
    readonly Func<string, string?> which;
    readonly string? logPath;
    readonly bool official;
    readonly Lock gate = new();
    readonly List<string> printed = [];

    /// <summary><paramref name="installer"/> stands in for the maker's (a test's fake); <paramref name="which"/> finds
    /// the CLI (a test's own folder).</summary>
    public AgentInstall(AgentCliInfo cli, bool windows, string? logPath = null, AgentInstaller? installer = null, Func<string, string?>? which = null)
    {
        this.cli = cli;
        official = installer is null;
        this.installer = installer ?? cli.Installer(windows);
        this.which = which ?? AiProvider.Which;
        this.logPath = logPath;
        Device = windows ? "PC" : "Mac";
    }

    public string Device { get; }
    public TimeSpan Timeout { get; init; } = TimeSpan.FromMinutes(10);
    /// <summary>Whether this process runs as an administrator (Windows) or root: then it won't install.</summary>
    public Func<bool> Elevated { get; init; } = () => Environment.IsPrivilegedProcess;
    /// <summary>The line being run, as its maker's docs write it.</summary>
    public string Line => installer.Line;
    public string DocUrl => installer.DocUrl;

    /// <summary>Where it's got to, in setup's words ("Downloading Claude Code…", "Setting it up…", "Checking it works…").</summary>
    public event Action<string>? Phase;
    /// <summary>Another line of what the installer printed.</summary>
    public event Action<string>? Printed;

    /// <summary>Everything the installer printed so far.</summary>
    public string Output
    {
        get
        {
            lock (gate) return string.Join('\n', printed);
        }
    }

    /// <summary>The CLI as it is on this computer now: found, and whether its <c>--version</c> answers.</summary>
    public AgentFound Find() => Find(cli, which);

    public static AgentFound Find(AgentCliInfo cli, Func<string, string?>? which = null)
    {
        if ((which ?? AiProvider.Which)(cli.Binary) is not { } exe) return AgentFound.None;
        var r = Machine.Run(exe, ["--version"], TimeSpan.FromSeconds(20));
        string said = r is { ExitCode: 0 } ? Py.Strip(r.Stdout) : "";
        return new AgentFound(exe, said.Length > 0 ? AgentCli.ParseVersion(said) : null, Py.Head(said.Split('\n')[0], 80));
    }

    /// <summary>Runs the installer and checks the CLI works after. Never throws: every way it ends is an outcome.</summary>
    public async Task<InstallOutcome> RunAsync(CancellationToken ct = default)
    {
        if (Elevated())
            return Fail(InstallFailure.Elevated, $"Study Stash is running as an administrator, so {cli.Name} would install for the wrong account. Close Study Stash, open it normally, then try again.");
        // Tests and the self-test never run a maker's real installer, whatever they ask for.
        if (official && Desktop.SystemChangesOff)
            return Fail(InstallFailure.Other, "Installing is off here.");
        lock (gate) printed.Clear();
        Log($"--- {DateTime.Now:yyyy-MM-dd HH:mm:ss} installing {cli.Name}: {installer.Line}");
        Phase?.Invoke($"Downloading {cli.Name}…");

        var psi = new ProcessStartInfo(installer.Program)
        {
            RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true, UseShellExecute = false,
            CreateNoWindow = true, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (string a in installer.Args) psi.ArgumentList.Add(a);
        psi.Environment["PATH"] = AiProvider.SearchPath();
        psi.Environment.Remove("CLAUDECODE");
        foreach (var (k, v) in installer.Env) psi.Environment[k] = v;

        Process? p;
        try
        {
            p = Process.Start(psi);
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Log($"didn't start: {e.Message}");
            return Fail(InstallFailure.Other, $"The installer didn't start: {e.Message.TrimEnd('.')}.");
        }
        if (p is null) return Fail(InstallFailure.Other, "The installer didn't start.");
        using var proc = p;
        // No questions: an installer that wants an answer gets none and carries on (or stops, saying why).
        try { proc.StandardInput.Close(); } catch (IOException) { }
        var reading = Task.WhenAll(Read(proc.StandardOutput), Read(proc.StandardError));
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(Timeout);
        try
        {
            await proc.WaitForExitAsync(cts.Token);
            await reading.WaitAsync(TimeSpan.FromSeconds(10), CancellationToken.None);
        }
        catch (OperationCanceledException)
        {
            StopAll(proc);
            bool cancelled = ct.IsCancellationRequested;
            Log(cancelled ? "cancelled" : $"gave up after {Timeout.TotalMinutes:0} minutes");
            return cancelled ? Fail(InstallFailure.Cancelled, "Stopped.")
                : Fail(InstallFailure.TimedOut, $"The installer didn't finish within {Timeout.TotalMinutes:0} minutes. Check your internet connection, then try again.");
        }
        catch (TimeoutException)
        {
            // Something it started in the background still holds its output open: what it printed so far is enough.
        }
        int code = proc.ExitCode;
        Log($"exit {code}");
        List<string> lines;
        lock (gate) lines = [.. printed];
        if (code != 0)
        {
            var kind = AgentCli.Classify(code, lines);
            string last = AgentCli.LastWords(lines);
            return Fail(kind, Words(kind, last)) with { LastLine = last };
        }
        Phase?.Invoke("Checking it works…");
        var found = await Task.Run(Find, CancellationToken.None);
        if (!found.Works)
        {
            Log(found.Exe is null ? "not found after installing" : $"{found.Exe} --version didn't answer");
            return Fail(InstallFailure.WontStart, $"{cli.Name} installed but won't start.");
        }
        Log($"works: {found.VersionText}");
        return new InstallOutcome(true, InstallFailure.None, $"{cli.Name} {found.Version} is installed.", found);
    }

    /// <summary>Setup's words for each way an install fails.</summary>
    public string Words(InstallFailure kind, string last) => kind switch
    {
        InstallFailure.Offline => $"Study Stash couldn't reach {cli.Site}. Check your internet connection, then try again.",
        InstallFailure.Region => $"{cli.Name} isn't offered in your country yet.",
        InstallFailure.Memory => $"Your {Device} ran out of memory while installing. Close some apps, then try again.",
        InstallFailure.WontStart => $"{cli.Name} installed but won't start.",
        _ => last.Length > 0 ? $"The installer stopped: {last}." : "The installer stopped without saying why.",
    };

    static InstallOutcome Fail(InstallFailure kind, string words) => new(false, kind, words, AgentFound.None);

    async Task Read(StreamReader from)
    {
        try
        {
            while (await from.ReadLineAsync() is { } line)
            {
                string plain = AgentCli.Plain(line);
                if (plain.Length == 0) continue;
                lock (gate) printed.Add(plain);
                Log(plain);
                Printed?.Invoke(plain);
                if (AgentCli.PhaseOf(cli, plain) is { } phase) Phase?.Invoke(phase);
            }
        }
        catch (Exception e) when (e is IOException or ObjectDisposedException)
        {
        }
    }

    static void StopAll(Process proc)
    {
        try
        {
            if (!proc.HasExited) proc.Kill(entireProcessTree: true);
        }
        catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
        }
    }

    /// <summary>Installer output holds no secrets: it goes to logs/setup-install.log as it is.</summary>
    void Log(string line)
    {
        if (logPath is null) return;
        try
        {
            lock (gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
                File.AppendAllText(logPath, line + "\n");
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }
}

/// <summary>
/// Signing in to Claude Code or Codex for guided setup, with the provider's own sign-in: <c>claude auth login</c> or
/// <c>codex login</c> runs hidden and opens the browser itself; Study Stash never sees the password or the tokens.
/// Whether it's worked is read from the status command's exit code (<see cref="AgentCli.ReadStatus"/>), every three
/// seconds while setup waits and whenever the sign-in exits. A sign-in that stops at once without working (it wants a
/// real terminal) sets <see cref="NeedsTerminal"/>, and setup offers the Terminal instead.
/// </summary>
public sealed class AgentSignIn : IDisposable
{
    readonly AgentCliInfo cli;
    readonly string exe;
    Process? child;
    DateTime started;
    TaskCompletionSource exited = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public AgentSignIn(AgentCliInfo cli, string exe)
    {
        this.cli = cli;
        this.exe = exe;
    }

    /// <summary>How often setup asks whether it's signed in yet.</summary>
    public TimeSpan Every { get; init; } = TimeSpan.FromSeconds(3);
    /// <summary>A sign-in that fails within this long wants a terminal (it couldn't open the browser, or needs a code
    /// pasted).</summary>
    public TimeSpan QuickFail { get; init; } = TimeSpan.FromSeconds(20);
    /// <summary>The sign-in page the CLI printed, for "Open it again": kept in memory only, never logged.</summary>
    public string? Url { get; private set; }
    public bool NeedsTerminal { get; private set; }
    public bool Running => child is { HasExited: false };

    /// <summary>Asks the CLI whether it's signed in (its status command's exit code). Codex's words are never read
    /// (they can show part of a key); Claude Code's are only for how and on which plan.</summary>
    public AgentSignedIn Check() => AgentCli.ReadStatus(cli.Id, Machine.Run(exe, cli.StatusArgs, TimeSpan.FromSeconds(20)));

    /// <summary>Starts the sign-in, hidden (it opens the browser itself). Once at a time: again while it runs does
    /// nothing. False when it didn't start.</summary>
    public bool Start()
    {
        if (Running) return true;
        NeedsTerminal = false;
        exited = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        List<string> cmd = [exe, .. cli.SignInArgs];
        var launch = OperatingSystem.IsWindows() ? WindowsCommand.For(cmd) : new Launch(exe, [.. cli.SignInArgs]);
        var psi = new ProcessStartInfo(launch.FileName)
        {
            RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true, UseShellExecute = false, CreateNoWindow = true,
        };
        if (launch.CommandLine is { } whole) psi.Arguments = whole;
        else foreach (string a in launch.Arguments) psi.ArgumentList.Add(a);
        psi.Environment["PATH"] = AiProvider.SearchPath();
        psi.Environment.Remove("CLAUDECODE");
        try
        {
            child = Process.Start(psi);
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            child = null;
        }
        if (child is null)
        {
            NeedsTerminal = true;
            return false;
        }
        started = DateTime.UtcNow;
        var p = child;
        var at = started;
        var done = exited;
        _ = Task.Run(() => Watch(p.StandardOutput));
        _ = Task.Run(() => Watch(p.StandardError));
        _ = Task.Run(async () =>
        {
            try
            {
                await p.WaitForExitAsync();
                // Only the sign-in still current: one stopped to be run again isn't one that couldn't open the browser.
                if (p.ExitCode != 0 && DateTime.UtcNow - at < QuickFail && ReferenceEquals(child, p)) NeedsTerminal = true;
            }
            catch (InvalidOperationException)
            {
            }
            done.TrySetResult();
        });
        return true;
    }

    /// <summary>Runs the sign-in again, the one going stopped first: it asks the system to open the browser once more.
    /// (The page Claude Code prints ends on a code to paste, which setup has no field for; the page it opens itself
    /// finishes on its own.) False when it didn't start.</summary>
    public bool Restart()
    {
        var old = child;
        child = null;
        Url = null;
        try
        {
            if (old is { HasExited: false }) old.Kill(entireProcessTree: true);
            old?.WaitForExit(3000);
        }
        catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
        }
        return Start();
    }

    /// <summary>Waits until it's signed in (true), checking every <see cref="Every"/> and when the sign-in exits, or
    /// until <paramref name="ct"/> ends the wait (false). <paramref name="changed"/> hears each check's answer.</summary>
    public async Task<bool> WaitAsync(CancellationToken ct, Action<AgentSignedIn>? changed = null)
    {
        while (!ct.IsCancellationRequested)
        {
            var now = await Task.Run(Check, CancellationToken.None);
            changed?.Invoke(now);
            if (now.SignedIn) return true;
            try
            {
                await Task.WhenAny(Task.Delay(Every, ct), exited.Task.WaitAsync(ct));
            }
            catch (OperationCanceledException)
            {
                return false;
            }
            if (exited.Task.IsCompleted) exited = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }
        return false;
    }

    /// <summary>Reads what the sign-in prints, only to keep the first https address in it: nothing is logged.</summary>
    async Task Watch(StreamReader from)
    {
        try
        {
            while (await from.ReadLineAsync() is { } line)
            {
                if (Url is not null) continue;
                int at = line.IndexOf("https://", StringComparison.Ordinal);
                if (at < 0) continue;
                string url = new([.. line[at..].TakeWhile(c => !char.IsWhiteSpace(c))]);
                if (Uri.TryCreate(url, UriKind.Absolute, out _)) Url = url;
            }
        }
        catch (Exception e) when (e is IOException or ObjectDisposedException)
        {
        }
    }

    /// <summary>Stops the sign-in (and anything it started), if it's still going.</summary>
    public void Stop()
    {
        try
        {
            if (child is { HasExited: false } p) p.Kill(entireProcessTree: true);
        }
        catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
        }
    }

    public void Dispose()
    {
        Stop();
        child?.Dispose();
        child = null;
    }
}
