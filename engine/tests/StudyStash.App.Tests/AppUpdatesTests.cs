using StudyStash.App.Services;
using StudyStash.Core;

namespace StudyStash.App.Tests;

/// <summary>AppUpdates.RoundAsync, tried through its injected pieces: no network, no process, no real clock.</summary>
public class AppUpdatesTests
{
    static Release Release(string version = "9.9.9") => new(
        $"v{version}", Updates.ParseVersion(version), $"https://example.com/{version}", "https://example.com");

    static AppUpdates Updates_(
        bool enabled = true,
        bool idle = true,
        string onDisk = "0.4.4",
        Func<Task<Release?>>? latest = null,
        Updates.ApplyFn? apply = null,
        Action? relaunchAndQuit = null,
        List<string>? log = null,
        Func<bool>? idleNow = null,
        Func<string>? onDiskNow = null,
        bool automatic = true,
        bool installerRelaunches = false,
        Action? quit = null,
        List<UpdateNews>? told = null)
    {
        log ??= [];
        return new AppUpdates
        {
            Latest = latest ?? (() => Task.FromResult<Release?>(null)),
            Idle = idleNow ?? (() => idle),
            Enabled = () => enabled,
            Automatic = () => automatic,
            OnDiskVersion = onDiskNow ?? (() => onDisk),
            Apply = apply ?? ((_, _, _, _) => Task.FromResult(true)),
            InstallerRelaunches = installerRelaunches,
            RelaunchAndQuit = relaunchAndQuit ?? (() => { }),
            Quit = quit ?? (() => { }),
            Tell = n => told?.Add(n),
            Log = s => log.Add(s),
            Home = "unused",
        };
    }

    [Fact]
    public async Task Disabled_never_checks_for_a_release()
    {
        bool asked = false;
        var updates = Updates_(enabled: false, latest: () => { asked = true; return Task.FromResult<Release?>(null); });

        Assert.Equal("off", await updates.RoundAsync());
        Assert.False(asked);
    }

    [Fact]
    public async Task Nothing_newer_leaves_it_alone()
    {
        var updates = Updates_(onDisk: "0.4.4", latest: () => Task.FromResult<Release?>(null));

        Assert.Equal("up to date", await updates.RoundAsync());
    }

    [Fact]
    public async Task A_newer_release_waits_for_the_student_to_be_idle_then_installs()
    {
        bool applied = false;
        bool quit = false;
        var updates = Updates_(
            idle: false,
            latest: () => Task.FromResult<Release?>(Release()),
            apply: (_, _, _, _) => { applied = true; return Task.FromResult(true); },
            relaunchAndQuit: () => quit = true);

        Assert.Equal("waiting until you're done recording", await updates.RoundAsync());
        Assert.False(applied);
        Assert.False(quit);
    }

    [Fact]
    public async Task Once_idle_the_release_installs_and_the_app_relaunches()
    {
        bool quit = false;
        var updates = Updates_(
            idle: true,
            latest: () => Task.FromResult<Release?>(Release()),
            apply: (_, _, _, _) => Task.FromResult(true),
            relaunchAndQuit: () => quit = true);

        Assert.Equal("relaunching", await updates.RoundAsync());
        Assert.True(quit);
    }

    [Fact]
    public async Task A_failed_install_says_so_once_and_does_not_quit()
    {
        bool quit = false;
        var told = new List<UpdateNews>();
        var updates = Updates_(
            idle: true,
            latest: () => Task.FromResult<Release?>(Release()),
            apply: (_, _, _, _) => Task.FromResult(false),
            relaunchAndQuit: () => quit = true,
            told: told);

        Assert.Equal("install failed", await updates.RoundAsync());
        Assert.Equal("install failed", await updates.RoundAsync()); // six hours later: tried again, not said again
        Assert.False(quit);
        Assert.Equal(UpdateNewsKind.Failed, Assert.Single(told).Kind);
    }

    /// <summary>The download takes a minute or more: a lecture started meanwhile isn't cut short by the app quitting
    /// for the new version. It relaunches into the new copy once the lecture's over.</summary>
    [Fact]
    public async Task A_lecture_started_while_it_downloaded_is_not_cut_short()
    {
        bool recording = false, swapped = false, quit = false;
        var updates = Updates_(
            idleNow: () => !recording,
            onDiskNow: () => swapped ? "9.9.9" : "0.4.4",
            latest: () => Task.FromResult<Release?>(Release()),
            apply: (_, _, _, _) =>
            {
                recording = true; // Record pressed while it downloaded; the swap went ahead
                swapped = true;
                return Task.FromResult(true);
            },
            relaunchAndQuit: () => quit = true);

        Assert.Equal("waiting until you're done recording", await updates.RoundAsync());
        Assert.False(quit);
        recording = false;
        Assert.Equal("relaunching", await updates.RoundAsync());
        Assert.True(quit);
    }

    /// <summary>Windows' Setup.exe closes the app and opens it again when it's done: the app only quits. Opening the
    /// old copy itself would hold the files Setup is replacing, and leave the install half done.</summary>
    [Fact]
    public async Task Windows_leaves_opening_the_app_again_to_setup()
    {
        bool relaunched = false, quit = false;
        var told = new List<UpdateNews>();
        var updates = Updates_(
            latest: () => Task.FromResult<Release?>(Release()),
            installerRelaunches: true,
            relaunchAndQuit: () => relaunched = true,
            quit: () => quit = true,
            told: told);

        Assert.Equal("handed off", await updates.RoundAsync());
        Assert.True(quit);
        Assert.False(relaunched);
        Assert.Equal(UpdateNewsKind.Restarting, Assert.Single(told).Kind); // remembered, so the next start can say if it took
    }

    /// <summary>auto_update off: a new version is said (once), never installed by itself, and installs when asked.</summary>
    [Fact]
    public async Task With_auto_update_off_a_new_version_is_said_and_installs_when_asked()
    {
        int applied = 0;
        var told = new List<UpdateNews>();
        var updates = Updates_(
            automatic: false,
            latest: () => Task.FromResult<Release?>(Release()),
            apply: (_, _, _, _) => { applied++; return Task.FromResult(true); },
            told: told);

        Assert.Equal("available", await updates.RoundAsync());
        Assert.Equal("available", await updates.RoundAsync());
        Assert.Equal(0, applied);
        Assert.Equal(UpdateNewsKind.Ready, Assert.Single(told).Kind);
        Assert.Equal("relaunching", await updates.NowAsync());
        Assert.Equal(1, applied);
    }

    [Fact]
    public async Task A_newer_copy_already_on_disk_relaunches_without_checking_for_a_release()
    {
        bool asked = false;
        bool quit = false;
        var updates = Updates_(
            idle: true,
            onDisk: "9.9.9",
            latest: () => { asked = true; return Task.FromResult<Release?>(null); },
            relaunchAndQuit: () => quit = true);

        Assert.Equal("relaunching", await updates.RoundAsync());
        Assert.False(asked);
        Assert.True(quit);
    }

    [Fact]
    public async Task A_newer_copy_on_disk_waits_for_idle_too()
    {
        bool quit = false;
        var updates = Updates_(idle: false, onDisk: "9.9.9", relaunchAndQuit: () => quit = true);

        Assert.Equal("waiting until you're done recording", await updates.RoundAsync());
        Assert.False(quit);
    }

    [Fact]
    public void Enabled_reads_auto_update_from_client_toml()
    {
        using var home = new TempHome();
        Configs.SaveClient(new ClientConfig(home.Path) { AutoUpdate = false });

        Assert.False(Configs.LoadClient(home.Path).AutoUpdate);
    }
}
