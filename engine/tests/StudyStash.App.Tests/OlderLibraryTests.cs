using System.Text.Json.Nodes;
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
    public void Only_an_older_copy_of_ours_with_nothing_on_the_go_is_replaced()
    {
        static bool Ours(string p) => p.Contains(".app/", StringComparison.Ordinal);
        Assert.Equal(OlderVerdict.Replace, OlderLibrary.Judge(Old, Self, Ours, () => false));
        Assert.Equal(OlderVerdict.Busy, OlderLibrary.Judge(Old, Self, Ours, () => true));
        // This app's own program (its background library) and someone else's are left as before; so is one whose file
        // can't be read. Busy isn't even asked then.
        Assert.Equal(OlderVerdict.Self, OlderLibrary.Judge(Self, Self, Ours, () => throw new InvalidOperationException()));
        Assert.Equal(OlderVerdict.NotOurs, OlderLibrary.Judge("/usr/local/bin/python3", Self, Ours, () => throw new InvalidOperationException()));
        Assert.Equal(OlderVerdict.NotOurs, OlderLibrary.Judge(null, Self, Ours, () => throw new InvalidOperationException()));
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

    static Config Ready(TempHome home)
    {
        var cfg = new Config(home.Path, home["Notes"]) { PoolName = "Ada's library", PoolPassword = "this-copys-password", WebPort = 19787 };
        Configs.Save(cfg);
        return cfg;
    }

    /// <summary>A library service whose port is held by an older copy (process 4242, an old Study Stash.app) that turns
    /// it away, until <paramref name="free"/>.</summary>
    static LibraryService Service(TempHome home, Config cfg, List<string> asked, bool busy, Func<bool> free, Action? onQuit = null) =>
        new(home.Path, cfg, [Self],
            spawn: info =>
            {
                asked.Add($"started {info.ArgumentList[0]} {info.ArgumentList[1]}");
                throw new IOException("no child in this test");
            },
            waitHealthy: (_, _) => Task.FromResult(false),
            portStatus: _ => Task.FromResult(free() ? "free" : "ours"),
            health: _ => Task.FromResult<(string, bool)?>(null),
            older: new OlderLibrary
            {
                Holder = _ => new PortHolder(4242, Old),
                Ours = p => p == Old,
                Busy = h => h == home.Path && busy,
                StopOldRegistration = (_, _) => asked.Add("launchd lets go of the old job"),
                AskToQuit = pid =>
                {
                    asked.Add($"asked process {pid} to quit");
                    onQuit?.Invoke();
                    return true;
                },
            },
            log: _ => { });
}
