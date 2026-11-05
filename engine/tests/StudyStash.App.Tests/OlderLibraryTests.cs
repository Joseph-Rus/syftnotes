using System.Text.Json.Nodes;
using StudyStash.App.Platform;
using StudyStash.App.Services;
using StudyStash.Core;

namespace StudyStash.App.Tests;

/// <summary>An older copy of Syft Notes holding the library's port with another password: replaced when it's ours and
/// idle, left alone (and said plainly, never "restart") when it's busy. Nothing real is asked: who holds the port, the
/// quit and launchd are stand-ins, the port is never 8787, and the home is a temp folder.</summary>
public sealed class OlderLibraryTests
{
    const string Self = "/Applications/Syft Notes.app/Contents/MacOS/StudyStash";
    const string Old = "/Applications/Study Stash.app/Contents/MacOS/StudyStash";

    [Fact]
    public void Only_an_older_copy_of_ours_for_this_folder_with_nothing_on_the_go_is_replaced()
    {
        const string home = "/Users/sam/.study-stash";
        static bool Idle(string _) => false;
        OurProcess Copy(string? version, string? folder = home, string program = Old, bool ours = true) => new(4242, program, ours, version, folder);

        Assert.Equal(OlderVerdict.Replace, OlderLibrary.Judge(Copy("0.22.1"), Self, "0.23.3", home, Idle));
        // A lecture on the go in this folder: left alone. Busy is asked of this folder's lectures.
        Assert.Equal(OlderVerdict.Busy, OlderLibrary.Judge(Copy("0.22.1"), Self, "0.23.3", home, h => h == home));
        // As new or newer (another path: ~/Applications, a "Keep Both" copy), or a version that can't be read: never.
        Assert.Equal(OlderVerdict.NotOlder, OlderLibrary.Judge(Copy("0.23.3"), Self, "0.23.3", home, Idle));
        Assert.Equal(OlderVerdict.NotOlder, OlderLibrary.Judge(Copy("0.24.0"), Self, "0.23.3", home, Idle));
        Assert.Equal(OlderVerdict.NotOlder, OlderLibrary.Judge(Copy(null), Self, "0.23.3", home, Idle));
        // Another settings folder's (or one whose folder can't be read), even older: never. Busy isn't asked.
        Assert.Equal(OlderVerdict.OtherHome, OlderLibrary.Judge(Copy("0.22.1", "/Users/sam/.study-stash-selftest"), Self, "0.23.3", home, _ => throw new InvalidOperationException()));
        Assert.Equal(OlderVerdict.OtherHome, OlderLibrary.Judge(Copy("0.22.1", null), Self, "0.23.3", home, _ => throw new InvalidOperationException()));
        // Someone else's program, or one that can't be told: never.
        Assert.Equal(OlderVerdict.NotOurs, OlderLibrary.Judge(Copy("0.22.1", program: "/usr/local/bin/python3", ours: false), Self, "0.23.3", home, Idle));
        Assert.Equal(OlderVerdict.NotOurs, OlderLibrary.Judge(null, Self, "0.23.3", home, Idle));
        // This app's own program (its background library) for this folder: its own to register again, unless busy.
        Assert.Equal(OlderVerdict.Own, OlderLibrary.Judge(Copy("0.23.3", program: Self), Self, "0.23.3", home, Idle));
        Assert.Equal(OlderVerdict.Busy, OlderLibrary.Judge(Copy("0.23.3", program: Self), Self, "0.23.3", home, _ => true));
    }

    [Fact]
    public void The_folder_a_copy_serves_is_read_from_its_command_line()
    {
        const string usual = "/Users/sam/.study-stash";
        Assert.Equal("/Users/sam/.study-stash", OurProcess.HomeIn("/Applications/Study Stash.app/Contents/MacOS/StudyStash --home /Users/sam/.study-stash helper", usual));
        Assert.Equal("/Users/sam/My Folder", OurProcess.HomeIn("/Applications/Study Stash.app/Contents/MacOS/StudyStash --home /Users/sam/My Folder serve", usual));
        Assert.Equal(@"C:\Users\Sam\.study-stash", OurProcess.HomeIn(@"""C:\Apps\StudyStash.exe"" --home ""C:\Users\Sam\.study-stash"" helper --keep-alive", usual));
        Assert.Equal("/tmp/other", OurProcess.HomeIn("/Applications/Syft Notes.app/Contents/MacOS/StudyStash --home /tmp/other", usual));
        Assert.Equal(usual, OurProcess.HomeIn("/Applications/Syft Notes.app/Contents/MacOS/StudyStash --background", usual));
    }

    [Fact]
    public void Busy_is_a_lecture_recording_or_being_written_down_now_never_one_waiting_to_be_sent()
    {
        using var home = new TempHome();
        var store = new LectureStore(home.Path);
        var now = DateTime.UtcNow;
        store.Add(new Lecture { Id = "rec-1", Started = "2026-11-04T09:00:00-08:00", State = LectureState.Filed });
        Assert.False(OlderLibrary.LecturesBusy(home.Path, now));

        foreach (var state in new[] { LectureState.Recording, LectureState.Paused, LectureState.Transcribing })
        {
            store.Update("rec-1", l => l.State = state);
            Assert.True(OlderLibrary.LecturesBusy(home.Path, now), state.ToString());
        }
        // Left Transcribing long ago (no model to write it down with): it never holds the library off.
        Assert.False(OlderLibrary.LecturesBusy(home.Path, now.AddHours(1)));

        // Recorded while the library was down, waiting to be sent: here, not in the old library, so not busy.
        File.WriteAllBytes(store.AudioPath("rec-1"), [0]);
        store.Update("rec-1", l => l.State = LectureState.Sending);
        Assert.False(OlderLibrary.LecturesBusy(home.Path, now));
        // Sent, and its notes being written there right now: busy.
        store.Update("rec-1", l => l.State = LectureState.Writing);
        Assert.True(OlderLibrary.LecturesBusy(home.Path, now));
    }

    [Fact]
    public async Task A_busy_older_copy_is_left_alone_and_the_student_is_told_without_restart()
    {
        using var home = new TempHome();
        var cfg = Ready(home);
        var asked = new List<string>();
        var svc = Service(home, cfg, asked, busy: true, free: () => false);

        await svc.StartAsync();

        Assert.Empty(asked); // not asked to quit, launchd not touched, nothing started
        Assert.Equal(LibraryServiceState.Elsewhere, svc.State);
        Assert.Equal(OlderLibrary.BusyWords, svc.Failure);
        string? why = await LibraryHere.ProblemAsync(svc, cfg);
        Assert.Equal(OlderLibrary.BusyWords, why);
        Assert.DoesNotContain("Restart", why!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task An_idle_older_copy_quits_and_this_library_starts_on_the_same_folder_and_sends_what_waited()
    {
        using var home = new TempHome();
        var cfg = Ready(home);
        // A lecture recorded while the old library turned the app away: waiting here to be sent.
        var store = new LectureStore(home.Path);
        store.Add(new Lecture { Id = "rec-20261104-090000-abcdef", Started = "2026-11-04T09:00:00-08:00", State = LectureState.Sending, Segments = [new(0, 5, "Hi.")] });
        File.WriteAllBytes(store.AudioPath("rec-20261104-090000-abcdef"), [0]);
        var asked = new List<string>();
        bool quit = false;
        var svc = Service(home, cfg, asked, busy: false, free: () => quit, onQuit: () => quit = true);

        await svc.StartAsync();

        Assert.Equal(["launchd lets go of the old job", "asked process 4242 to quit", "started --home " + home.Path], asked);

        // Still there, with its sound, and sent once the library answers.
        Assert.True(File.Exists(store.AudioPath("rec-20261104-090000-abcdef")));
        Assert.Equal(LectureState.Sending, new LectureStore(home.Path).Get("rec-20261104-090000-abcdef")!.State);
        var sent = new List<string>();
        var sender = new LectureSender(store, () => new ClientConfig(home.Path) { ServerUrl = $"http://127.0.0.1:{cfg.WebPort}", PoolKey = cfg.PoolPassword },
            new LaptopHost { Post = (url, body, key) => { sent.Add(url); return Task.FromResult(new JsonObject { ["status"] = "queued" }); } });
        Assert.Equal(1, await sender.StepAsync());
        Assert.Single(sent);
        Assert.Equal(LectureState.Writing, store.Get("rec-20261104-090000-abcdef")!.State);
    }

    [Fact]
    public async Task A_copy_as_new_or_newer_or_for_another_folder_is_never_touched_and_never_says_restart()
    {
        using var home = new TempHome();
        var cfg = Ready(home);
        foreach (var holder in new[]
                 {
                     new OurProcess(4242, "/Users/sam/Applications/Syft Notes.app/Contents/MacOS/StudyStash", true, Engine.Version, home.Path),
                     new OurProcess(4242, "/Users/sam/Downloads/Syft Notes.app/Contents/MacOS/StudyStash", true, "99.0.0", home.Path),
                     new OurProcess(4242, Old, true, "0.22.1", home["elsewhere"]),
                 })
        {
            var asked = new List<string>();
            var svc = Service(home, cfg, asked, busy: false, free: () => false, holder: holder);
            await svc.StartAsync();
            Assert.Empty(asked);
            Assert.Equal(LibraryServiceState.Elsewhere, svc.State);
            string? why = await LibraryHere.ProblemAsync(svc, cfg);
            Assert.Equal(OlderLibrary.OtherCopyWords, why);
            Assert.DoesNotContain("Restart", why!, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task This_apps_own_background_library_on_another_password_is_registered_again()
    {
        // Setup gave the library a new password; the background library still runs with the one it read at login.
        using var home = new TempHome();
        var cfg = Ready(home);
        var asked = new List<string>();
        var svc = Service(home, cfg, asked, busy: false, free: () => false,
            holder: new OurProcess(4242, Self, true, Engine.Version, home.Path),
            background: new BackgroundPlan(NoBackground.Instance, () => true, Self));

        await svc.StartAsync();

        Assert.Equal(["registered again"], asked); // never asked to quit by hand, nothing else started
        Assert.True(svc.InBackground);
        Assert.Equal(LibraryServiceState.Elsewhere, svc.State);
        Assert.Null(svc.Failure);
    }

    static Config Ready(TempHome home)
    {
        var cfg = new Config(home.Path, home["Notes"]) { PoolName = "Ada's library", PoolPassword = "this-copys-password", WebPort = 19787 };
        Configs.Save(cfg);
        return cfg;
    }

    /// <summary>A library service whose port is held by <paramref name="holder"/> (process 4242; an old Study Stash.app
    /// for this folder unless said), which turns it away until <paramref name="free"/> (or, with a background library,
    /// until that's registered again).</summary>
    static LibraryService Service(TempHome home, Config cfg, List<string> asked, bool busy, Func<bool> free, Action? onQuit = null,
        OurProcess? holder = null, BackgroundPlan? background = null)
    {
        bool registered = false;
        return new(home.Path, cfg, [Self],
            spawn: info =>
            {
                asked.Add($"started {info.ArgumentList[0]} {info.ArgumentList[1]}");
                throw new IOException("no child in this test");
            },
            waitHealthy: (_, _) => Task.FromResult(registered),
            portStatus: _ => Task.FromResult(free() ? "free" : "ours"),
            health: _ => Task.FromResult<(string, bool)?>(registered ? (Engine.Version, true) : null),
            older: new OlderLibrary
            {
                Holder = _ => holder ?? new OurProcess(4242, Old, true, "0.22.1", home.Path),
                Busy = h => h == home.Path && busy,
                StopRegistration = (_, _) => asked.Add("launchd lets go of the old job"),
                AskToQuit = pid =>
                {
                    asked.Add($"asked process {pid} to quit");
                    onQuit?.Invoke();
                    return true;
                },
            },
            log: _ => { })
        {
            Background = background is null ? null : background with
            {
                System = new Registrations(asked, () => registered = true, background.Program),
            },
        };
    }

    /// <summary>This app's background library, registered for this folder, running this program.</summary>
    sealed class Registrations(List<string> asked, Action registered, string runs) : IBackgroundLibrary
    {
        public bool IsRegistered(string home) => true;
        public IReadOnlyList<string>? Registered(string home) => [runs, "--home", home, "helper"];
        public void Register(string home, string program, IReadOnlyList<string>? extra = null)
        {
            asked.Add("registered again");
            registered();
        }
        public void Unregister(string home) => asked.Add("unregistered");
        public List<(string Home, IReadOnlyList<string> Args)> All() => [];
        public void Stop(string home) => asked.Add("stopped by its label");
        public void Start(string home) => asked.Add("started by its label");
        public void Restart(string home) => asked.Add("restarted");
    }
}
