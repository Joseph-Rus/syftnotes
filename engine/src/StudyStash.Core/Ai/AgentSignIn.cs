using System.Diagnostics;

namespace StudyStash.Core.Ai;

/// <summary>
/// Signing in to Claude Code, Codex or Antigravity CLI, for guided setup and for every Sign in button in Syft Notes, with
/// the provider's own sign-in:
/// <c>claude auth login</c>, <c>codex login</c> or <c>agy -p=/usage</c> runs hidden and opens the browser itself; Syft
/// Notes never sees the password or the tokens. Whether it's worked is read from the status command's exit code
/// (<see cref="AgentCli.ReadStatus"/>), every three seconds while setup waits and whenever the sign-in exits. A sign-in
/// that stops at once without working (it wants a real terminal) sets <see cref="NeedsTerminal"/>, and setup offers the
/// Terminal instead; one that waited for the browser and gave up by itself (agy's, after a minute) sets
/// <see cref="GaveUp"/>, and the wait ends.
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
    /// <summary>The sign-in waited for the browser as long as it does and stopped without being signed in.</summary>
    public bool GaveUp { get; private set; }
    /// <summary>The sign-in stopped because its maker doesn't offer it in this country (what it printed said so; the
    /// words themselves are never kept or logged).</summary>
    public bool Region { get; private set; }
    public bool Running => child is { HasExited: false };

    /// <summary>Asks the CLI whether it's signed in (its status command's exit code). Codex's words are never read
    /// (they can show part of a key); Claude Code's are only for how and on which plan.</summary>
    public AgentSignedIn Check() => AgentCli.ReadStatus(cli.Id, Machine.Run(exe, cli.StatusArgs, TimeSpan.FromSeconds(20), cli.Env()));

    /// <summary>Starts the sign-in, hidden (it opens the browser itself). Once at a time: again while it runs does
    /// nothing. False when it didn't start.</summary>
    public bool Start()
    {
        if (Running) return true;
        NeedsTerminal = false;
        GaveUp = false;
        Region = false;
        regionSaid = false;
        exited = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        List<string> cmd = [exe, .. cli.SignInArgs];
        var launch = OperatingSystem.IsWindows() ? WindowsCommand.For(cmd) : new Launch(exe, [.. cli.SignInArgs]);
        var psi = new ProcessStartInfo(launch.FileName)
        {
            RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true,
            // agy signs in with the browser only when its input isn't a pipe (piped, it stops at once, saying it isn't
            // signed in): it gets the app's own, which is nothing.
            RedirectStandardInput = cli.SignInGivesUp is null,
        };
        if (launch.CommandLine is { } whole) psi.Arguments = whole;
        else foreach (string a in launch.Arguments) psi.ArgumentList.Add(a);
        psi.Environment["PATH"] = AiProvider.SearchPath();
        psi.Environment.Remove("CLAUDECODE");
        foreach (var (k, v) in cli.Env()) psi.Environment[k] = v;
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
        var reading = Task.WhenAll(Task.Run(() => Watch(p.StandardOutput)), Task.Run(() => Watch(p.StandardError)));
        _ = Task.Run(async () =>
        {
            try
            {
                await p.WaitForExitAsync();
                // What it printed last, read before it's judged (a refusal for the country comes just before it stops).
                try
                {
                    await reading.WaitAsync(TimeSpan.FromSeconds(2));
                }
                catch (TimeoutException)
                {
                }
                // Only the sign-in still current: one stopped to be run again isn't one that couldn't open the browser.
                if (p.ExitCode != 0 && regionSaid && ReferenceEquals(child, p)) Region = true;
                else if (p.ExitCode != 0 && DateTime.UtcNow - at < QuickFail && ReferenceEquals(child, p)) NeedsTerminal = true;
                else if (p.ExitCode != 0 && cli.SignInGivesUp is not null && ReferenceEquals(child, p)) GaveUp = true;
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
    /// until <paramref name="ct"/> ends the wait, or the sign-in gave up (false). <paramref name="changed"/> hears each
    /// check's answer.</summary>
    public async Task<bool> WaitAsync(CancellationToken ct, Action<AgentSignedIn>? changed = null)
    {
        bool Report(AgentSignedIn now)
        {
            changed?.Invoke(now);
            return true;
        }
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
            // It gave up waiting for the browser, or its maker turned the country away: one last look, and the wait ends.
            if (GaveUp || Region) return await Task.Run(Check, CancellationToken.None) is { SignedIn: true } last && Report(last);
            if (exited.Task.IsCompleted) exited = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }
        return false;
    }

    /// <summary>What the sign-in printed said its maker doesn't offer it in this country.</summary>
    volatile bool regionSaid;

    /// <summary>Reads what the sign-in prints, only to keep the first https address in it and to see whether it says
    /// the country isn't served: nothing is logged.</summary>
    async Task Watch(StreamReader from)
    {
        try
        {
            while (await from.ReadLineAsync() is { } line)
            {
                if (AgentCli.LooksLikeRegion(line)) regionSaid = true;
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
