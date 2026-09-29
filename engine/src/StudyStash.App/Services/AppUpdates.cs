using System.Globalization;
using Avalonia.Threading;
using StudyStash.App.Platform;
using StudyStash.Core;

namespace StudyStash.App.Services;

/// <summary>What the student is told about an update, each a notification of its own (<see cref="AppUpdates.Tell"/>).</summary>
public enum UpdateNewsKind
{
    /// <summary>A new version is out and updates aren't automatic here: Update installs it.</summary>
    Ready,
    /// <summary>A new version is out, but this copy can't replace itself (<see cref="UpdateNews.Why"/> says what to do).</summary>
    CantInstall,
    /// <summary>The student asked for it: it's downloading and installing.</summary>
    Installing,
    /// <summary>The student asked for it while recording: it installs once the lecture's over.</summary>
    Waiting,
    /// <summary>It didn't install: this version stays, and the next round tries again.</summary>
    Failed,
    /// <summary>The new version is in place (a Mac) or Setup.exe has it (Windows): the app quits for it now.</summary>
    Restarting,
}

public sealed record UpdateNews(UpdateNewsKind Kind, Release Release, string? Why = null)
{
    /// <summary>"0.10.1", without the tag's v.</summary>
    public string Version => string.Join('.', Release.Version);
}

/// <summary>
/// The running app checking for a new release on its own (D4): 10 minutes after it starts, then every 6 hours,
/// installing it once nothing is recording, paused or transcribing, then quitting so the new copy starts in its
/// place. It also notices when a newer copy has already been swapped onto disk by someone else - the CLI's
/// `studystash update`, or the library page's "Update now" - and relaunches into it once idle, with no download of
/// its own. With auto_update off it says a new version is out, and installs it when asked (<see cref="NowAsync"/>);
/// a copy that can't replace itself says what to do instead; one that fails says so. Every dependency is injectable,
/// so <see cref="RoundAsync"/> is tried with fakes: no network, no process, no real clock.
/// </summary>
public sealed class AppUpdates
{
    /// <summary>How often the idle-wait loop looks again while an update is ready but the student is mid-lecture,
    /// instead of waiting the full six hours for the next round.</summary>
    public static readonly TimeSpan IdleRecheck = TimeSpan.FromMinutes(1);

    // What a round comes to.
    public const string Off = "off";
    public const string UpToDate = "up to date";
    public const string CheckFailed = "check failed";
    public const string Available = "available";
    public const string CantHere = "can't update here";
    public const string Waiting = "waiting until you're done recording";
    public const string Failed = "install failed";
    public const string Relaunching = "relaunching";
    public const string HandedOff = "handed off";

    /// <summary>The app's own updater, once it has started: Settings' Update now and the notification's Update use it.</summary>
    public static AppUpdates? Current { get; private set; }

    public required Func<Task<Release?>> Latest { get; init; }
    /// <summary>Nothing recording, paused or transcribing right now.</summary>
    public required Func<bool> Idle { get; init; }
    /// <summary>Looks for new releases at all: an installed copy (or one that can't replace itself, to say so), and
    /// not the self-test.</summary>
    public required Func<bool> Enabled { get; init; }
    /// <summary>auto_update: a new release installs by itself. Off, it's said once and installs when asked.</summary>
    public Func<bool> Automatic { get; init; } = () => true;
    /// <summary>Why this copy can't install a new release itself, or null when it can.</summary>
    public Func<string?> CantInstall { get; init; } = () => null;
    /// <summary>The version actually on disk right now: someone else (the CLI, the library page) may have installed
    /// a newer one than the code this process is running.</summary>
    public required Func<string> OnDiskVersion { get; init; }
    public required Updates.ApplyFn Apply { get; init; }
    /// <summary>Windows: Setup.exe closes the app, installs over it and opens it again itself, so once it has the
    /// update the app only quits (<see cref="Quit"/>). A copy of its own reopened meanwhile would hold the files
    /// Setup is replacing, and the install would stop half done.</summary>
    public bool InstallerRelaunches { get; init; }
    /// <summary>Get the app running again on the version now on disk, then quit this process. Called instead of a
    /// plain quit because nothing else is watching to reopen the app: unlike a service under launchd or Setup.exe's
    /// own `/relaunch`, an ordinary quit here would just leave the student without a menu bar icon.</summary>
    public required Action RelaunchAndQuit { get; init; }
    /// <summary>Quit, and leave reopening to the installer (<see cref="InstallerRelaunches"/>).</summary>
    public Action Quit { get; init; } = () => { };
    /// <summary>Something to tell the student (from the updater's own thread).</summary>
    public Action<UpdateNews> Tell { get; init; } = _ => { };
    public required Action<string> Log { get; init; }
    /// <summary>Where lectures and settings live, for <see cref="Apply"/>.</summary>
    public required string Home { get; init; }
    /// <summary>How a round waits between checks; a test finishes at once instead of really sleeping.</summary>
    public Func<TimeSpan, CancellationToken, Task> Delay { get; init; } = Task.Delay;

    /// <summary>Downloading and installing right now.</summary>
    public bool Installing { get; private set; }

    readonly SemaphoreSlim oneAtATime = new(1, 1);
    readonly HashSet<string> told = [];
    volatile bool asked;
    CancellationTokenSource? napping;

    /// <summary>
    /// One check, never two at once: <see cref="Off"/>, <see cref="UpToDate"/>, <see cref="CheckFailed"/> (the next
    /// round tries again), <see cref="Available"/> (said, not installed: auto_update is off), <see cref="CantHere"/>,
    /// <see cref="Waiting"/> (an update is ready but the student isn't idle, or a lecture started while it
    /// downloaded), <see cref="Failed"/>, <see cref="HandedOff"/> (Setup.exe has it) or <see cref="Relaunching"/> (a
    /// fresh install, or a newer copy already on disk, just needed the student to be idle).
    /// </summary>
    public async Task<string> RoundAsync()
    {
        await oneAtATime.WaitAsync();
        try
        {
            return await OneRoundAsync();
        }
        finally
        {
            oneAtATime.Release();
        }
    }

    async Task<string> OneRoundAsync()
    {
        if (!Enabled()) return Off;

        int[] running = Updates.ParseVersion(Engine.Version);
        if (Updates.Compare(Updates.ParseVersion(OnDiskVersion()), running) > 0)
        {
            if (!Idle()) return Waiting;
            Log("[update] a newer copy is already installed here; relaunching into it");
            RelaunchAndQuit();
            return Relaunching;
        }

        Release? release;
        try
        {
            release = await Latest();
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            Log($"[update] check failed: {e.Message}");
            return CheckFailed;
        }
        if (release is null || !Updates.IsNewer(release)) return UpToDate;
        if (CantInstall() is { } why)
        {
            Once(new(UpdateNewsKind.CantInstall, release, why));
            return CantHere;
        }
        if (!asked && !Automatic())
        {
            Once(new(UpdateNewsKind.Ready, release));
            return Available;
        }
        if (!Idle())
        {
            if (asked) Once(new(UpdateNewsKind.Waiting, release));
            return Waiting;
        }

        Log($"[update] installing {release.Tag}...");
        Installing = true;
        if (asked) Tell(new(UpdateNewsKind.Installing, release));
        bool ok;
        try
        {
            ok = await Apply(release, Home, s => Log($"[update] {s}"), true);
        }
        catch (Exception e)
        {
            Log($"[update] {e.Message}");
            ok = false;
        }
        finally
        {
            Installing = false;
        }
        if (!ok)
        {
            // Apply looks once more right before the swap or the hand-off: a lecture that started while it downloaded
            // puts it off, and it goes ahead once that's over.
            if (!Idle()) return Waiting;
            Log("[update] install failed");
            asked = false;
            Once(new(UpdateNewsKind.Failed, release));
            return Failed;
        }
        asked = false;
        if (InstallerRelaunches)
        {
            Tell(new(UpdateNewsKind.Restarting, release));
            Quit();
            return HandedOff;
        }
        // The new copy is in place. A lecture started in the last moment isn't cut short: a round once it's over
        // finds the newer copy on disk and relaunches into it.
        if (!Idle()) return Waiting;
        Tell(new(UpdateNewsKind.Restarting, release));
        RelaunchAndQuit();
        return Relaunching;
    }

    /// <summary>Says <paramref name="news"/> once per version while this copy runs (a round every six hours, or
    /// every minute while it waits, never says it again).</summary>
    void Once(UpdateNews news)
    {
        lock (told)
            if (!told.Add($"{news.Kind} {news.Release.Tag}")) return;
        Tell(news);
    }

    /// <summary>Update now (Settings, or the notification's Update): the newest release installs now, auto_update or
    /// not, or as soon as the lecture being recorded is over. What happened, as <see cref="RoundAsync"/> says.</summary>
    public async Task<string> NowAsync()
    {
        asked = true;
        string result = await RoundAsync();
        if (result == Waiting)
        {
            // Look again in a minute, not in six hours.
            try
            {
                napping?.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }
        }
        else
        {
            asked = false;
        }
        return result;
    }

    /// <summary>Rounds forever: the first one 10 minutes after the app starts, then every 6 hours; while a round
    /// keeps saying to wait for the student, the next one comes a minute later instead.</summary>
    public async Task RunAsync(CancellationToken stop)
    {
        try
        {
            await NapAsync(TimeSpan.FromSeconds(Updates.FirstCheckAfter), stop);
            while (!stop.IsCancellationRequested)
            {
                string result;
                try
                {
                    result = await RoundAsync();
                }
                catch (Exception e)
                {
                    Log($"[update] {e.Message}");
                    result = UpToDate;
                }
                await NapAsync(result == Waiting ? IdleRecheck : TimeSpan.FromSeconds(Updates.CheckEvery), stop);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    /// <summary>The wait between rounds; <see cref="NowAsync"/> cuts it short when it has to wait for a lecture.</summary>
    async Task NapAsync(TimeSpan wait, CancellationToken stop)
    {
        using var nap = CancellationTokenSource.CreateLinkedTokenSource(stop);
        napping = nap;
        try
        {
            await Delay(wait, nap.Token);
        }
        catch (OperationCanceledException) when (!stop.IsCancellationRequested)
        {
        }
        finally
        {
            napping = null;
        }
    }

    /// <summary>A program that outlives this one, waiting for it to quit before opening the app again: the same
    /// idea as the Mac swap's own waiter (Updater.cs), needed here too because nothing else relaunches an already
    /// on-disk copy, and because relaunching after a fresh install this way (once, from here) avoids racing a
    /// second waiter spawned by <see cref="Updates.ApplyAsync"/> if that were also asked to relaunch. Never after
    /// handing off to Windows' Setup.exe, which opens the app itself once it's done (<see cref="InstallerRelaunches"/>).</summary>
    static void SpawnWaiter(UpdateHost host, IReadOnlyList<string> args)
    {
        string pid = Environment.ProcessId.ToString(CultureInfo.InvariantCulture);
        if (host.System == "Darwin")
        {
            const string script = "app=\"$1\"; shift; while kill -0 \"$0\" 2>/dev/null; do sleep 1; done; exec /usr/bin/open \"$app\" --args \"$@\"";
            host.SpawnDetached(["/bin/sh", "-c", script, pid, host.AppDir, .. args]);
        }
        else if (host.System == "Windows")
        {
            string exe = Path.Combine(host.AppDir, "StudyStash.exe");
            string command = $"Wait-Process -Id {pid} -ErrorAction SilentlyContinue; Start-Process -FilePath '{exe}' -ArgumentList '{string.Join(' ', args)}'";
            host.SpawnDetached(["powershell.exe", "-NoProfile", "-WindowStyle", "Hidden", "-Command", command]);
        }
    }

    /// <summary>auto_update, read fresh each round: client.toml's, and config.toml's too when the library is on this
    /// computer (Settings → Your library → Update automatically writes that one). Either off is off; a file that
    /// can't be read counts as off, so nothing installs on a guess.</summary>
    static bool AutoUpdateOn(AppHost host)
    {
        try
        {
            if (!Configs.LoadClient(host.Home).AutoUpdate) return false;
            if (!host.Settings.LibraryHere) return true;
            var library = Configs.Load(host.Home);
            return !File.Exists(library.ConfigPath) || library.AutoUpdate;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException or FormatException or Tomlyn.TomlException)
        {
            return false;
        }
    }

    /// <summary>Wires the real pieces to an app that's actually running, and starts checking in the background, off
    /// the UI thread (a Mac install copies and checks a whole app, which would otherwise freeze the menu bar for a
    /// minute). Off for a build folder and the self-test; auto_update is read fresh every round, so turning it off in
    /// Settings (or client.toml) takes effect on the next check.</summary>
    public static void Start(AppHost host, CancellationToken stop, Action<UpdateNews> tell)
    {
        var updateHost = UpdateHost.ThisComputer();
        List<string> relaunchArgs = ["--background"];
        if (!string.Equals(host.Home, Configs.DefaultHome, StringComparison.Ordinal)) relaunchArgs.AddRange(["--home", host.Home]);
        bool Idle() => host.Recorder.Current is null
                       && !host.Lectures.All().Any(l => l.State is LectureState.Recording or LectureState.Paused or LectureState.Transcribing);
        var updates = new AppUpdates
        {
            Latest = () => Updates.CachedLatestAsync(),
            Idle = Idle,
            Enabled = () => (updateHost.AppDir.Length > 0 || updateHost.CantReplace) && !Desktop.SystemChangesOff,
            Automatic = () => AutoUpdateOn(host),
            CantInstall = () => Updates.WhyNotUpdatable(updateHost),
            OnDiskVersion = () => updateHost.AppDir.Length > 0 ? Updates.InstalledVersion(updateHost.AppDir, updateHost.System) : Engine.Version,
            // Services running from the old bundle (an old-style library LaunchAgent) restart onto the new one rather
            // than run on from a deleted folder.
            Apply = (release, home, log, restart) => Updates.ApplyAsync(release, home, updateHost, log, restart, ready: Idle),
            InstallerRelaunches = updateHost.System == "Windows",
            RelaunchAndQuit = () =>
            {
                SpawnWaiter(updateHost, relaunchArgs);
                Dispatcher.UIThread.Post(() => Shell.Quit(0));
            },
            Quit = () => Dispatcher.UIThread.Post(() => Shell.Quit(0)),
            Tell = tell,
            Log = Program.Log,
            Home = host.Home,
        };
        Current = updates;
        _ = Task.Run(() => updates.RunAsync(stop));
    }
}
