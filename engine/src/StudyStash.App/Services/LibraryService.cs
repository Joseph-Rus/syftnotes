using System.ComponentModel;
using System.Diagnostics;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using StudyStash.Core;

namespace StudyStash.App.Services;

/// <summary>How the library's own process, run as this app's child, is doing right now.</summary>
public enum LibraryServiceState
{
    Stopped,
    Starting,
    /// <summary>Our child is up and the library answers.</summary>
    Running,
    /// <summary>Something else already answers as a Syft Notes library on this port: we didn't start anything. That's
    /// also the background library (<see cref="LibraryService.InBackground"/>), which the app uses and leaves running.</summary>
    Elsewhere,
    /// <summary>Another program holds the port.</summary>
    PortTaken,
    Failed,
}

/// <summary>The background library, as <see cref="LibraryService"/> uses it: the computer's own registration of it,
/// whether it's wanted now, and what it runs.</summary>
/// <param name="Wanted">On by default on just this computer once a phone is linked through Google, unless Settings →
/// Phone's switch is off (see <see cref="AppHost.BackgroundWanted"/>).</param>
/// <param name="Extra">The self-test's stand-in for Google Drive (--drive-folder); none for a student.</param>
public sealed record BackgroundPlan(IBackgroundLibrary System, Func<bool> Wanted, string Program, IReadOnlyList<string>? Extra = null);

/// <summary>
/// Runs `StudyStash --home H serve` as a child of this app: starts it, waits for it to answer, restarts it with a
/// growing pause if it dies (giving up after too many restarts in a row), and stops it cleanly on quit. It never
/// touches a library that's already answering on the port — it steps aside instead. The one exception to running a
/// child is the background library (<see cref="Background"/>): when the student wants the phone answered with the app
/// closed, the computer runs the library at login instead (a LaunchAgent, the Run key, a systemd --user unit), this
/// steps aside for it, and quitting the app leaves it running. What actually starts the process is swappable, so a
/// test runs a real, short-lived copy.
/// </summary>
public sealed class LibraryService : IDisposable
{
    const int MaxRestartsIn10Min = 5;
    static readonly int[] RestartDelaysSeconds = [2, 5, 15, 60];
    /// <summary>Bytes: the log starts over (keeping one old copy) past this, same as the app's own log.</summary>
    const long LogLimit = 5_000_000;

    readonly string home;
    readonly Config cfg;
    readonly IReadOnlyList<string> command;
    readonly Func<ProcessStartInfo, Process> spawn;
    readonly Func<Config, TimeSpan, Task<bool>> waitHealthy;
    readonly Func<int, Task<string>> portStatus;
    readonly Func<Config, Task<(string Version, bool Background)?>> health;
    readonly Action<string> log;
    readonly Lock gate = new();
    readonly List<DateTime> recentExits = [];
    readonly Queue<string> tail = new();
    Process? proc;
    bool stopping;
    bool disposed;

    /// <summary>The port and password it's running with (or would, once started).</summary>
    public Config Cfg => cfg;
    public LibraryServiceState State { get; private set; } = LibraryServiceState.Stopped;
    /// <summary>Why it's <see cref="LibraryServiceState.Failed"/> or <see cref="LibraryServiceState.PortTaken"/>, in
    /// words a student can be shown; null otherwise.</summary>
    public string? Failure { get; private set; }
    /// <summary>The last lines the library printed, for the app's own log beside a <see cref="Failure"/>: they're the
    /// engine talking to whoever reads logs, never words for a student.</summary>
    public string LastLines { get { lock (gate) return string.Join(" / ", tail); } }
    /// <summary>The child's process id while we're running one we started; null otherwise (a test kills it by this).</summary>
    public int? Pid { get { lock (gate) return proc?.Id; } }
    /// <summary>Called (on a worker thread) whenever <see cref="State"/> or <see cref="Failure"/> changes.</summary>
    public event Action? Changed;

    /// <summary>The background library, when the app can have one (just this computer); null keeps to a child.</summary>
    public BackgroundPlan? Background { get; set; }

    /// <summary>The library that answers is the background one: the app uses it and leaves it running when it quits.</summary>
    public bool InBackground { get; private set; }

    /// <summary>Why the background library couldn't be used just now, in the student's words (Settings → Phone shows
    /// it); null while it works or isn't wanted. The app's own library answers meanwhile, as before.</summary>
    public string? BackgroundProblem { get; private set; }

    public string LogDir => Path.Combine(home, "logs");
    public string LogPath => Path.Combine(LogDir, "library.log");
    public string PidPath => Path.Combine(home, "library.pid");

    public LibraryService(string home, Config cfg, IReadOnlyList<string>? command = null, Func<ProcessStartInfo, Process>? spawn = null,
        Func<Config, TimeSpan, Task<bool>>? waitHealthy = null, Func<int, Task<string>>? portStatus = null, Action<string>? log = null,
        Func<Config, Task<(string Version, bool Background)?>>? health = null)
    {
        this.home = home;
        this.cfg = cfg;
        this.command = command ?? [Platform.Desktop.Program];
        this.spawn = spawn ?? (info => Process.Start(info)!);
        this.waitHealthy = waitHealthy ?? ((c, t) => HostInfo.WaitForServerAsync(c, t));
        this.portStatus = portStatus ?? (p => HostInfo.PortStatusAsync(p));
        this.log = log ?? Console.WriteLine;
        this.health = health ?? HealthAsync;
    }

    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(5) };

    /// <summary>The library on the port, as it says itself: its version, and whether it's the background one.</summary>
    static async Task<(string Version, bool Background)?> HealthAsync(Config cfg)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1:{cfg.WebPort}/api/health");
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", cfg.PoolPassword);
            using var r = await Http.SendAsync(request);
            if (!r.IsSuccessStatusCode) return null;
            var o = System.Text.Json.Nodes.JsonNode.Parse(await r.Content.ReadAsStringAsync()) as System.Text.Json.Nodes.JsonObject;
            return ((string?)o?["version"] ?? "", o?["background"]?.GetValue<bool>() == true);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException or InvalidOperationException)
        {
            return null;
        }
    }

    void SetState(LibraryServiceState s, string? failure = null)
    {
        State = s;
        Failure = failure;
        Changed?.Invoke();
    }

    /// <summary>Starts the library, unless it's already starting or running. Returns once it answers, steps aside
    /// (<see cref="LibraryServiceState.Elsewhere"/>/<see cref="LibraryServiceState.PortTaken"/>) or fails to.</summary>
    public async Task StartAsync()
    {
        lock (gate)
        {
            if (State is LibraryServiceState.Starting or LibraryServiceState.Running) return;
            stopping = false;
        }
        if (!File.Exists(cfg.ConfigPath))
        {
            SetState(LibraryServiceState.Failed, "Not set up yet");
            return;
        }
        SetState(LibraryServiceState.Starting);
        InBackground = false;
        if (Background is { } plan)
        {
            if (plan.Wanted())
            {
                if (await UseBackgroundAsync(plan)) return;
            }
            else if (plan.System.IsRegistered(home))
            {
                // Not wanted any more (switched off, Google disconnected, no longer just this computer): it goes, and
                // the app's own library takes its place.
                log("[library] the background library isn't wanted any more: removing it");
                await Task.Run(() => plan.System.Unregister(home));
                await WaitPortFreeAsync();
                BackgroundProblem = null;
            }
        }
        string status;
        try { status = await portStatus(cfg.WebPort); }
        catch (Exception e) when (e is HttpRequestException or SocketException) { status = "free"; }
        if (status == "busy") { SetState(LibraryServiceState.PortTaken, $"Port {cfg.WebPort} is taken by another program."); return; }
        if (status == "ours") { SetState(LibraryServiceState.Elsewhere); return; }
        await SpawnAsync();
    }

    /// <summary>The library we stepped aside for (<see cref="LibraryServiceState.Elsewhere"/>) may have stopped since: a
    /// copy of the app that was ended, whose library was still shutting down as this one started. Then ours starts in
    /// its place; while another still answers, nothing changes.</summary>
    public async Task TakeOverIfGoneAsync()
    {
        if (State != LibraryServiceState.Elsewhere) return;
        await StartAsync();
    }

    /// <summary>
    /// The background library answers instead of a child of ours: registered with the computer (again, when it runs
    /// another copy's program or an older version than this app: after an update), started if it isn't running, and
    /// waited for. True once it answers as this version. Otherwise it's taken away again and false: the app's own
    /// library takes over, as before, and <see cref="BackgroundProblem"/> says why.
    /// </summary>
    async Task<bool> UseBackgroundAsync(BackgroundPlan plan)
    {
        var bg = plan.System;
        try
        {
            bool fresh = false;
            var registered = bg.Registered(home);
            if (registered is not { Count: > 0 } || !SameProgram(registered[0], plan.Program))
            {
                log("[library] registering the background library, so the phone is answered with the app closed");
                await Task.Run(() => bg.Register(home, plan.Program, plan.Extra));
                fresh = true;
            }
            else if (await portStatus(cfg.WebPort) == "free")
            {
                await Task.Run(() => bg.Start(home));
            }
            if (!fresh && await health(cfg) is { } said && (said.Version != Engine.Version || !said.Background))
            {
                // Running an older copy (updated since, by something that didn't restart it): onto this one.
                log($"[library] the background library runs {said.Version}; starting it again on {Engine.Version}");
                await Task.Run(() => bg.Register(home, plan.Program, plan.Extra));
            }
            if (await waitHealthy(cfg, TimeSpan.FromSeconds(30)) && await health(cfg) is { Background: true })
            {
                InBackground = true;
                BackgroundProblem = null;
                SetState(LibraryServiceState.Elsewhere);
                return true;
            }
            log("[library] the background library didn't answer in time: the app runs its own library instead");
            BackgroundProblem = "It didn't start, so for now your phone is answered only while Syft Notes is open.";
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException or PlatformNotSupportedException or Win32Exception)
        {
            log($"[library] couldn't register the background library: {e.Message}");
            BackgroundProblem = "This computer didn't let Syft Notes run in the background, so for now your phone is answered only while Syft Notes is open.";
        }
        try
        {
            await Task.Run(() => bg.Unregister(home));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException or PlatformNotSupportedException or Win32Exception)
        {
            log($"[library] couldn't remove the background library: {e.Message}");
        }
        await WaitPortFreeAsync();
        return false;
    }

    static bool SameProgram(string a, string b) =>
        string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    /// <summary>Up to 15 s for the port to be let go (a background library stopping): a child started sooner couldn't
    /// listen on it.</summary>
    async Task WaitPortFreeAsync()
    {
        var until = DateTime.UtcNow + TimeSpan.FromSeconds(15);
        while (DateTime.UtcNow < until && await portStatus(cfg.WebPort) != "free") await Task.Delay(100);
    }

    /// <summary>
    /// Settings → Phone's switch, Google connected or disconnected, or this computer no longer just this computer: the
    /// library moves to the background (our child stops, the computer runs it) or back (the background one goes, our
    /// child starts). Nothing changes when it's already where it should be.
    /// </summary>
    public async Task SwitchAsync()
    {
        bool wanted = Background?.Wanted() == true;
        if (wanted == InBackground && State is LibraryServiceState.Running or LibraryServiceState.Elsewhere or LibraryServiceState.Starting) return;
        if (State == LibraryServiceState.Running) await StopAsync();
        lock (gate) State = LibraryServiceState.Stopped;
        await StartAsync();
    }

    /// <summary>The library starts again to read new settings (a new password, laptops let in): the background one is
    /// restarted by the computer; otherwise our child stops and starts.</summary>
    public async Task RestartAsync()
    {
        if (InBackground && Background is { } plan && plan.Wanted())
        {
            SetState(LibraryServiceState.Starting);
            await Task.Run(() => plan.System.Restart(home));
            if (await waitHealthy(cfg, TimeSpan.FromSeconds(30)))
            {
                SetState(LibraryServiceState.Elsewhere);
                return;
            }
            lock (gate) State = LibraryServiceState.Stopped;
            await StartAsync();
            return;
        }
        await StopAsync();
        await StartAsync();
    }

    async Task SpawnAsync()
    {
        Directory.CreateDirectory(LogDir);
        RotateLogIfBig();
        var info = new ProcessStartInfo(command[0])
        {
            UseShellExecute = false,
            // No console window on Windows, even for an engine built as a console program.
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            // Its input stays open while we want it running: closing it (or this app ending) stops it.
            RedirectStandardInput = true,
            WorkingDirectory = home,
        };
        foreach (var a in command.Skip(1)) info.ArgumentList.Add(a);
        info.ArgumentList.Add("--home");
        info.ArgumentList.Add(home);
        info.ArgumentList.Add("serve");
        info.Environment.Remove("STUDYSTASH_SELFTEST");
        info.Environment[StudyStash.Library.Cli.StopWhenInputEndsEnv] = "1";

        Process p;
        try { p = spawn(info); }
        catch (Exception e) when (e is IOException or Win32Exception)
        {
            SetState(LibraryServiceState.Failed, e.Message);
            return;
        }
        p.EnableRaisingEvents = true;
        p.OutputDataReceived += (_, e) => LogLine(e.Data);
        p.ErrorDataReceived += (_, e) => LogLine(e.Data);
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();
        lock (gate) proc = p;
        try { File.WriteAllText(PidPath, p.Id.ToString()); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        p.Exited += (_, _) => OnExited();

        bool healthy;
        try { healthy = await waitHealthy(cfg, TimeSpan.FromSeconds(30)); }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException) { healthy = false; }
        if (p.HasExited) return; // OnExited already moved us past Starting
        if (!healthy)
        {
            log($"[library] didn't answer in time: {LastLines}");
            SetState(LibraryServiceState.Failed, "It didn't answer in time.");
            return;
        }
        SetState(LibraryServiceState.Running);
    }

    void LogLine(string? line)
    {
        if (line is null) return;
        lock (gate)
        {
            tail.Enqueue(line);
            while (tail.Count > 20) tail.Dequeue();
        }
        try { File.AppendAllText(LogPath, line + Environment.NewLine); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    void RotateLogIfBig()
    {
        try
        {
            var f = new FileInfo(LogPath);
            if (f.Exists && f.Length >= LogLimit) File.Move(LogPath, LogPath + ".1", overwrite: true);
        }
        catch (IOException) { }
    }

    void OnExited()
    {
        lock (gate)
        {
            proc = null;
            recentExits.Add(DateTime.UtcNow);
            recentExits.RemoveAll(t => DateTime.UtcNow - t > TimeSpan.FromMinutes(10));
        }
        try { File.Delete(PidPath); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        if (stopping || disposed) { SetState(LibraryServiceState.Stopped); return; }
        int count;
        lock (gate) count = recentExits.Count;
        if (count > MaxRestartsIn10Min)
        {
            log($"[library] kept stopping: {LastLines}");
            SetState(LibraryServiceState.Failed, "It kept stopping.");
            return;
        }
        int delay = RestartDelaysSeconds[Math.Min(count - 1, RestartDelaysSeconds.Length - 1)];
        log($"[library] stopped unexpectedly; trying again in {delay}s");
        SetState(LibraryServiceState.Starting);
        _ = Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(delay));
            if (!stopping && !disposed) await SpawnAsync();
        });
    }

    /// <summary>Stops the child we started or adopted; does nothing when the library is <see cref="LibraryServiceState.Elsewhere"/>
    /// or not running — we only ever stop a process we hold, never one just answering on the port. So quitting the app
    /// leaves the background library running: that's what it's for.</summary>
    public async Task StopAsync()
    {
        stopping = true;
        Process? p;
        lock (gate) p = proc;
        if (p is null)
        {
            SetState(LibraryServiceState.Stopped);
            return;
        }
        try
        {
            // A clean stop: its input ends (the only way to ask on Windows), and SIGTERM elsewhere. Then a moment to
            // shut its database down before it's ended for good.
            AskToStop(p);
            if (!OperatingSystem.IsWindows()) Native.Kill(p.Id, Native.SigTerm);
            using var soft = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            try { await p.WaitForExitAsync(soft.Token); }
            catch (OperationCanceledException) { if (!p.HasExited) p.Kill(entireProcessTree: true); }
            await p.WaitForExitAsync();
        }
        catch (InvalidOperationException) { } // already gone
        lock (gate) proc = null;
        try { File.Delete(PidPath); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        SetState(LibraryServiceState.Stopped);
    }

    /// <summary>Ends the child's input, which `serve` takes as "stop" (see <see cref="StudyStash.Library.Cli.StopWhenInputEndsEnv"/>).</summary>
    static void AskToStop(Process p)
    {
        try
        {
            p.StandardInput.Close();
        }
        catch (Exception e) when (e is IOException or InvalidOperationException or ObjectDisposedException)
        {
            // Started without its input to us (a test's own spawn), or already gone.
        }
    }

    public void Dispose()
    {
        disposed = true;
        stopping = true;
        Process? p;
        lock (gate) p = proc;
        if (p is null) return;
        try { if (!p.HasExited) p.Kill(entireProcessTree: true); }
        catch (InvalidOperationException) { }
    }
}

/// <summary>No background library: tests and the self-test (unless it's the self-test's own background run), so
/// nothing there ever registers anything with the computer.</summary>
public sealed class NoBackground : IBackgroundLibrary
{
    public static readonly NoBackground Instance = new();

    public bool IsRegistered(string home) => false;
    public IReadOnlyList<string>? Registered(string home) => null;
    public void Register(string home, string program, IReadOnlyList<string>? extra = null) =>
        throw new InvalidOperationException("Running in the background is off here.");
    public void Unregister(string home) { }
    public List<(string Home, IReadOnlyList<string> Args)> All() => [];
    public void Stop(string home) { }
    public void Start(string home) { }
    public void Restart(string home) { }
}

static class Native
{
    public const int SigTerm = 15;

    [DllImport("libc", SetLastError = true)]
    static extern int kill(int pid, int sig);

    /// <summary>Best-effort: a process that's already gone just means there's nothing left to ask.</summary>
    public static void Kill(int pid, int sig)
    {
        try { kill(pid, sig); } catch (DllNotFoundException) { }
    }
}
