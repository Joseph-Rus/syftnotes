using System.Threading.Channels;
using Microsoft.Extensions.Time.Testing;
using StudyStash.App.Services;
using StudyStash.Audio;
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
        List<UpdateNews>? told = null,
        Func<Release, bool>? moves = null,
        Func<Task<Release?>>? latestNow = null)
    {
        log ??= [];
        return new AppUpdates
        {
            Latest = latest ?? (() => Task.FromResult<Release?>(null)),
            LatestNow = latestNow,
            Idle = idleNow ?? (() => idle),
            Enabled = () => enabled,
            Automatic = () => automatic,
            OnDiskVersion = onDiskNow ?? (() => onDisk),
            Apply = apply ?? ((_, _, _, _) => Task.FromResult(true)),
            InstallerRelaunches = installerRelaunches,
            Moves = moves ?? (_ => false),
            RelaunchAndQuit = relaunchAndQuit ?? (() => { }),
            Quit = quit ?? (() => { }),
            Tell = n => told?.Add(n),
            Log = s => log.Add(s),
            Home = "unused",
        };
    }

    [Fact]
    public async Task A_copy_installed_the_old_way_moves_to_its_own_version_once_idle_and_the_move_reopens_it()
    {
        // The bridge release (Bridge.cs): an Inno-era or tarball copy moves over to Velopack's install even when the
        // release is the version it already runs, waits for the lecture like any update, and leaves reopening the app
        // to what does the move (relaunching it here too would open the old copy the move is uninstalling).
        bool idle = false, relaunched = false, quit = false;
        int applied = 0;
        var same = Release(Engine.Version);
        var updates = Updates_(
            onDisk: Engine.Version, idleNow: () => idle,
            latest: () => Task.FromResult<Release?>(same),
            moves: r => r == same,
            apply: (_, _, _, _) => { applied++; return Task.FromResult(true); },
            relaunchAndQuit: () => relaunched = true, quit: () => quit = true);

        Assert.Equal("waiting until you're done recording", await updates.RoundAsync());
        Assert.Equal(0, applied);
        idle = true;
        Assert.Equal("handed off", await updates.RoundAsync());
        Assert.Equal(1, applied);
        Assert.True(quit);
        Assert.False(relaunched);
        // Without the move, the same version is just up to date.
        Assert.Equal("up to date", await Updates_(onDisk: Engine.Version, latest: () => Task.FromResult<Release?>(same)).RoundAsync());
    }

    [Fact]
    public async Task With_auto_updates_off_a_move_at_the_same_version_says_finish_updating_not_a_new_version()
    {
        var same = Release(Engine.Version);
        var told = new List<UpdateNews>();
        var updates = Updates_(onDisk: Engine.Version, automatic: false, latest: () => Task.FromResult<Release?>(same), moves: _ => true, told: told);

        Assert.Equal("available", await updates.RoundAsync());
        Assert.True(updates.FoundIsMove);
        Assert.Equal(AppUpdates.MoveOnly, told.Single().Why);
        var check = await updates.CheckAsync();
        Assert.Equal(AppUpdates.MoveOnly, check.Why);
        var (line, canUpdate) = StudyStash.App.ViewModels.NoticeWords.UpdateChecked(check, Engine.Version);
        Assert.Contains("finish updating", line);
        Assert.DoesNotContain("is out", line);
        Assert.True(canUpdate);
        Assert.Equal("Finish updating", new StudyStash.App.ViewModels.PanelModel { UpdateIsMove = true, UpdateVersion = Engine.Version }.UpdateWords);
    }

    [Fact]
    public async Task A_failed_move_says_why_in_its_own_words()
    {
        var told = new List<UpdateNews>();
        var updates = new AppUpdates
        {
            Latest = () => Task.FromResult<Release?>(Release(Engine.Version)), Idle = () => true, Enabled = () => true,
            OnDiskVersion = () => Engine.Version, Apply = (_, _, _, _) => Task.FromResult(false), Moves = _ => true,
            FailedWhy = () => Bridge.NeedsFuse, RelaunchAndQuit = () => { }, Tell = told.Add, Log = _ => { }, Home = "unused",
        };

        Assert.Equal("install failed", await updates.RoundAsync());
        Assert.Equal(Bridge.NeedsFuse, told.Single(n => n.Kind == UpdateNewsKind.Failed).Why);
        var (title, text, action) = StudyStash.App.ViewModels.NoticeWords.MoveFailed(Bridge.NeedsFuse, Engine.Version);
        Assert.Contains("libfuse2", text);
        Assert.DoesNotContain("tries again later", text);
        Assert.Null(action);
        Assert.Contains("a week", StudyStash.App.ViewModels.NoticeWords.MoveFailed("", Engine.Version).Text);
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

    /// <summary>A first run's setup keeps its answers only in its window: the speech model finishing its download was
    /// when the move to Velopack's install quit the app, and it came back on the welcome page. Shell counts setup's
    /// window being open as busy, so a new version and the move both wait for it, like for a lecture. Closing it,
    /// finished or not, lifts the wait: a student who never finishes setup still gets every update.</summary>
    [Theory]
    [InlineData(false)] // a newer version
    [InlineData(true)] // the move to Velopack's install, at the version the copy already runs
    public async Task Setup_being_open_holds_an_update_and_the_move_until_it_is_done(bool move)
    {
        bool settingUp = true, left = false; // settingUp: setup's window is open
        int applied = 0;
        var release = Release(move ? Engine.Version : "9.9.9");
        var updates = Updates_(
            onDisk: Engine.Version, idleNow: () => !settingUp,
            latest: () => Task.FromResult<Release?>(release),
            moves: r => move && r == release,
            apply: (_, _, _, _) => { applied++; return Task.FromResult(true); },
            relaunchAndQuit: () => left = true, quit: () => left = true);

        Assert.Equal(AppUpdates.Waiting, await updates.RoundAsync());
        Assert.Equal(AppUpdates.Waiting, await updates.RoundAsync()); // a minute later (IdleRecheck): still setting up
        Assert.Equal(0, applied);
        Assert.False(left);
        settingUp = false; // the window closed (setup done or not)
        Assert.Equal(move ? AppUpdates.HandedOff : AppUpdates.Relaunching, await updates.RoundAsync());
        Assert.Equal(1, applied);
        Assert.True(left);
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
        Assert.Equal("install failed", await updates.RoundAsync()); // an hour later: tried again, not said again
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

    /// <summary>Update now asks GitHub afresh: the background rounds' hour-old answer may not know a release that
    /// came out since, and the student would be told they're up to date.</summary>
    [Fact]
    public async Task Update_now_asks_for_the_newest_release_afresh_not_the_hour_old_answer()
    {
        int applied = 0, fresh = 0;
        var updates = Updates_(
            latest: () => Task.FromResult<Release?>(null), // the cached answer from before the release
            latestNow: () => { fresh++; return Task.FromResult<Release?>(Release()); },
            apply: (_, _, _, _) => { applied++; return Task.FromResult(true); });

        Assert.Equal("up to date", await updates.RoundAsync());
        Assert.Equal(0, fresh);
        Assert.Equal("relaunching", await updates.NowAsync());
        Assert.Equal(1, fresh);
        Assert.Equal(1, applied);
    }

    /// <summary>Update now pressed during a lecture: one fresh GitHub call for the press, and the one-minute rounds
    /// that wait for the lecture keep that answer, so a long lecture can't run into GitHub's hourly limit.</summary>
    [Fact]
    public async Task Update_now_during_a_lecture_asks_GitHub_once_and_installs_when_the_lecture_ends()
    {
        bool idle = false;
        int applied = 0, fresh = 0;
        var updates = Updates_(
            idleNow: () => idle,
            latest: () => Task.FromResult<Release?>(null), // the cached answer from before the release
            latestNow: () => { fresh++; return Task.FromResult<Release?>(Release()); },
            apply: (_, _, _, _) => { applied++; return Task.FromResult(true); });

        Assert.Equal("waiting until you're done recording", await updates.NowAsync());
        for (int minute = 0; minute < 90; minute++) Assert.Equal("waiting until you're done recording", await updates.RoundAsync());
        idle = true;
        Assert.Equal("relaunching", await updates.RoundAsync());
        Assert.Equal(1, fresh);
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

    /// <summary>Task.Delay's clock stops while the computer sleeps: a laptop awake two hours a day would check every
    /// few days. The rounds go by the wall clock instead, a minute's nap at a time.</summary>
    [Fact]
    public async Task A_laptop_that_slept_past_the_hour_checks_on_the_next_minute_awake()
    {
        var ct = TestContext.Current.CancellationToken;
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 11, 4, 9, 0, 0, TimeSpan.Zero));
        var naps = Channel.CreateUnbounded<(TimeSpan Wait, TaskCompletionSource Done)>();
        int rounds = 0;
        var updates = new AppUpdates
        {
            Latest = () => { Interlocked.Increment(ref rounds); return Task.FromResult<Release?>(null); },
            Idle = () => true, Enabled = () => true, OnDiskVersion = () => Engine.Version,
            Apply = (_, _, _, _) => Task.FromResult(true), RelaunchAndQuit = () => { }, Log = _ => { }, Home = "unused",
            FirstCheck = TimeSpan.FromMinutes(2), Clock = clock,
            // Each nap ends when the test says, whatever it asked for, as a nap does when the computer wakes.
            Delay = (wait, stop) =>
            {
                var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                naps.Writer.TryWrite((wait, done));
                return done.Task.WaitAsync(stop);
            },
        };
        using var stop = new CancellationTokenSource();
        var run = updates.RunAsync(stop.Token);
        async Task<TaskCompletionSource> Nap()
        {
            var (wait, done) = await naps.Reader.ReadAsync(ct).AsTask().WaitAsync(TimeSpan.FromSeconds(10), ct);
            Assert.True(wait <= AppUpdates.Slice, $"a nap of {wait}");
            return done;
        }

        // The first round, two minutes after the app starts: a minute's nap is not yet two.
        (await Nap()).SetResult();
        var next = await Nap();
        Assert.Equal(0, rounds);
        clock.Advance(TimeSpan.FromMinutes(2));
        next.SetResult();
        next = await Nap();
        Assert.Equal(1, rounds);
        // A minute awake isn't the hour: no round.
        clock.Advance(TimeSpan.FromMinutes(1));
        next.SetResult();
        next = await Nap();
        Assert.Equal(1, rounds);
        // Asleep for three hours: the wall clock moved, the nap didn't end. It ends on waking, and the round comes then.
        clock.Advance(TimeSpan.FromHours(3));
        next.SetResult();
        next = await Nap();
        Assert.Equal(2, rounds);

        stop.Cancel();
        await run.WaitAsync(TimeSpan.FromSeconds(10), ct);
    }

    /// <summary>A lecture can be left Transcribing for good when the speech model is missing (a full disk) or won't
    /// load. An update must not wait on it for ever: the students it happens to are the ones who need the fix, and a
    /// restart picks the lecture up where it left off. One really being written down is never cut short.</summary>
    [Fact]
    public async Task A_lecture_stuck_transcribing_does_not_hold_an_update_off_but_one_being_written_down_does()
    {
        var ct = TestContext.Current.CancellationToken;
        using var home = new TempHome();
        string wav = home["speech.wav"];
        using (var w = new WavWriter(wav))
            w.Write(Enumerable.Range(0, Sound.Rate / 2).Select(i => (float)Math.Sin(i / 10.0) * 0.3f).ToArray());
        bool broken = true;
        using var host = new AppHost(home.Path, () => new FileMicrophone(wav),
            () => broken ? throw new InvalidOperationException("the model file is missing") : new OneLine(), log: _ => { },
            loginItems: new CountingLoginItems());
        foreach (var (id, started) in new[] { ("rec-a", "2026-11-04T10:00:00-07:00"), ("rec-b", "2026-11-04T11:00:00-07:00") })
        {
            File.Copy(wav, host.Lectures.AudioPath(id));
            host.Lectures.Add(new Lecture { Id = id, Started = started, State = LectureState.Transcribing, Seconds = 0.5 });
        }
        AppUpdates Round() => Updates_(latest: () => Task.FromResult<Release?>(Release()),
            idleNow: () => AppUpdates.NoLectureInHand(host.Lectures.All(), host.Whisper.Working));

        // The model won't load: both lectures wait, and the update goes ahead.
        Assert.False(await host.Whisper.StepAsync(ct));
        Assert.Equal("relaunching", await Round().RoundAsync());

        // The model loads and the first lecture is written down; the second is next: the update waits for it.
        broken = false;
        host.Whisper.Wake();
        Assert.True(await host.Whisper.StepAsync(ct));
        Assert.Equal(LectureState.Transcribing, host.Lectures.Get("rec-b")!.State);
        Assert.Equal("waiting until you're done recording", await Round().RoundAsync());
    }

    sealed class OneLine : ITranscriber
    {
        public Task<Transcription> TranscribeAsync(float[] samples, string prompt, string language, CancellationToken stop) =>
            Task.FromResult(new Transcription([new Spoken(0, 1, "The midterm is on recursion.")], "en"));

        public void Dispose()
        {
        }
    }

    [Fact]
    public void Enabled_reads_auto_update_from_client_toml()
    {
        using var home = new TempHome();
        Configs.SaveClient(new ClientConfig(home.Path) { AutoUpdate = false });

        Assert.False(Configs.LoadClient(home.Path).AutoUpdate);
    }

    [Fact]
    public void An_update_brings_the_library_window_back_only_when_it_was_open()
    {
        Assert.Equal(["--background"], AppUpdates.RelaunchWith(Configs.DefaultHome, windowOpen: false));
        Assert.Empty(AppUpdates.RelaunchWith(Configs.DefaultHome, windowOpen: true));
        Assert.Equal(["--home", "/notes"], AppUpdates.RelaunchWith("/notes", windowOpen: true));
    }

    [Fact]
    public void The_Windows_waiter_has_no_argument_list_when_the_app_comes_back_with_no_arguments()
    {
        // Start-Process refuses an empty -ArgumentList, which would leave the student with no app at all.
        var spawned = new List<IReadOnlyList<string>>();
        var host = new UpdateHost { System = "Windows", AppDir = @"C:\Apps\Syft Notes", SpawnDetached = spawned.Add };

        AppUpdates.SpawnWaiter(host, []);
        AppUpdates.SpawnWaiter(host, ["--background"]);

        Assert.DoesNotContain("-ArgumentList", spawned[0][^1]);
        Assert.Contains("-ArgumentList '--background'", spawned[1][^1]);
    }
}
