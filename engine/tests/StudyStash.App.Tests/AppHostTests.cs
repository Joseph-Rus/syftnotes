using System.Diagnostics;
using StudyStash.App.Services;
using StudyStash.App.ViewModels.Kit;
using StudyStash.Audio;
using StudyStash.Core;

namespace StudyStash.App.Tests;

[Collection(nameof(EnvironmentTests))]
public class AppHostTests
{
    static AppHost Host(string home, Func<IAudioSource>? mic = null) => new(home, mic, log: _ => { }, loginItems: new CountingLoginItems());

    static string Wav(TempHome home)
    {
        string path = home["speech.wav"];
        using var w = new WavWriter(path);
        w.Write(Enumerable.Range(0, Sound.Rate / 2).Select(i => (float)Math.Sin(i / 10.0) * 0.3f).ToArray());
        return path;
    }

    [Fact]
    public async Task A_sound_file_stands_in_for_the_microphone()
    {
        using var home = new TempHome();
        string before = Environment.GetEnvironmentVariable("STUDYSTASH_MIC_FILE") ?? "";
        Environment.SetEnvironmentVariable("STUDYSTASH_MIC_FILE", Wav(home));
        try
        {
            using var host = Host(home.Path);
            Assert.True(host.PretendMic);
            Assert.Equal(MicAccess.Allowed, host.MicAccess());
            Assert.Equal(MicAccess.Allowed, await host.AskMicAsync());
            using var mic = host.OpenMic();
            Assert.IsType<FileMicrophone>(mic);
        }
        finally
        {
            Environment.SetEnvironmentVariable("STUDYSTASH_MIC_FILE", before.Length > 0 ? before : null);
        }

        // Without one (and no file named), it's the real microphone: not opened here, only told apart.
        if (before.Length == 0)
        {
            using var real = Host(home.Path);
            Assert.False(real.PretendMic);
        }
    }

    [Fact]
    public void A_file_that_isnt_there_is_not_a_microphone()
    {
        using var home = new TempHome();
        string before = Environment.GetEnvironmentVariable("STUDYSTASH_MIC_FILE") ?? "";
        Environment.SetEnvironmentVariable("STUDYSTASH_MIC_FILE", home["nothing.wav"]);
        try
        {
            Assert.Null(AppHost.MicFromEnvironment());
        }
        finally
        {
            Environment.SetEnvironmentVariable("STUDYSTASH_MIC_FILE", before.Length > 0 ? before : null);
        }
    }

    [Fact]
    public void A_microphone_given_is_the_one_recorded_from()
    {
        using var home = new TempHome();
        var given = new FileMicrophone(Wav(home));
        using var host = Host(home.Path, () => given);
        Assert.True(host.PretendMic);
        Assert.Same(given, host.OpenMic());
        Assert.Equal(MicAccess.Allowed, host.MicAccess());
    }

    [Fact]
    public void Settings_that_cant_be_saved_are_said_not_thrown()
    {
        using var home = new TempHome();
        using var host = Host(home.Path);
        // app.json's temporary file can't be written where a folder is.
        Directory.CreateDirectory(home["app.json.tmp"]);
        List<string> said = [];
        host.Problem += (title, _) => said.Add(title);

        host.Save(s => s.Language = "fr");

        Assert.Equal("fr", host.Settings.Language);
        Assert.Equal(["Your settings couldn't be saved"], said);
    }

    [Fact]
    public void A_library_is_for_school_unless_it_says_otherwise()
    {
        Assert.True(AppHost.ForSchoolIn(null));
        Assert.True(AppHost.ForSchoolIn(new System.Text.Json.Nodes.JsonObject()));
        Assert.True(AppHost.ForSchoolIn(new System.Text.Json.Nodes.JsonObject { ["for_school"] = true }));
        Assert.False(AppHost.ForSchoolIn(new System.Text.Json.Nodes.JsonObject { ["for_school"] = false }));
        Assert.True(AppHost.ForSchoolIn(new System.Text.Json.Nodes.JsonObject { ["for_school"] = "no" }));
        Assert.True(AppHost.ForSchoolIn(new System.Text.Json.Nodes.JsonObject { ["for_school"] = null }));
    }

    [Fact]
    public void The_sound_file_plays_as_fast_as_asked()
    {
        Assert.Equal(1, AppHost.MicSpeed(null));
        Assert.Equal(1, AppHost.MicSpeed(""));
        Assert.Equal(4, AppHost.MicSpeed("4"));
        Assert.Equal(8, AppHost.MicSpeed("20"));
        Assert.Equal(1, AppHost.MicSpeed("0"));
        Assert.Equal(1, AppHost.MicSpeed("fast"));

        using var home = new TempHome();
        string file = Environment.GetEnvironmentVariable("STUDYSTASH_MIC_FILE") ?? "", speed = Environment.GetEnvironmentVariable("STUDYSTASH_MIC_SPEED") ?? "";
        Environment.SetEnvironmentVariable("STUDYSTASH_MIC_FILE", Wav(home));
        Environment.SetEnvironmentVariable("STUDYSTASH_MIC_SPEED", "4");
        try
        {
            using var host = Host(home.Path);
            using var mic = host.OpenMic();
            Assert.Equal(4, Assert.IsType<FileMicrophone>(mic).Speed);
        }
        finally
        {
            Environment.SetEnvironmentVariable("STUDYSTASH_MIC_FILE", file.Length > 0 ? file : null);
            Environment.SetEnvironmentVariable("STUDYSTASH_MIC_SPEED", speed.Length > 0 ? speed : null);
        }
    }

    /// <summary>Whisper that writes one line per piece.</summary>
    sealed class OneLine : ITranscriber
    {
        public Task<Transcription> TranscribeAsync(float[] samples, string prompt, string language, CancellationToken stop) =>
            Task.FromResult(new Transcription([new Spoken(0, 1, "The midterm is on recursion.")], "en"));

        public void Dispose()
        {
        }
    }

    [Fact]
    public async Task Whisper_that_wont_start_is_said_and_a_failed_lecture_goes_again()
    {
        using var home = new TempHome();
        bool broken = true;
        using var host = new AppHost(home.Path, () => new FileMicrophone(Wav(home)),
            () => broken ? throw new InvalidOperationException("the model file is damaged") : new OneLine(), log: _ => { },
            loginItems: new CountingLoginItems());
        int changes = 0;
        host.Changed += () => Interlocked.Increment(ref changes);
        File.Copy(Wav(home), host.Lectures.AudioPath("rec-1"));
        host.Lectures.Add(new Lecture { Id = "rec-1", Started = "2026-09-22T10:00:00-07:00", State = LectureState.Transcribing, Seconds = 0.5 });

        Assert.False(await host.Whisper.StepAsync(TestContext.Current.CancellationToken));
        Assert.Equal("Whisper couldn't start: the model file is damaged", host.WhisperProblem);
        Assert.True(changes > 0); // the windows hear of it
        Assert.Equal(LectureState.Transcribing, host.Lectures.Get("rec-1")!.State);

        broken = false;
        host.Whisper.Wake();
        Assert.True(await host.Whisper.StepAsync(TestContext.Current.CancellationToken));
        Assert.Null(host.WhisperProblem);
        Assert.Equal(LectureState.Sending, host.Lectures.Get("rec-1")!.State);

        // A lecture that failed goes again from the dropdown.
        host.Lectures.Update("rec-1", x =>
        {
            x.State = LectureState.Failed;
            x.Error = "Whisper couldn't write part of it down: out of memory";
        });
        host.Retry("rec-1");
        Assert.Equal(LectureState.Sending, host.Lectures.Get("rec-1")!.State);
        Assert.Equal("", host.Lectures.Get("rec-1")!.Error);
    }

    [Fact]
    public async Task The_watchdog_runs_every_second_and_pauses_a_microphone_that_gives_only_silence()
    {
        using var home = new TempHome();
        string silence = home["silence.wav"];
        using (var w = new WavWriter(silence)) w.Write(new float[Sound.Rate]);
        using var host = new AppHost(home.Path, () => new FileMicrophone(silence, 8), () => throw new InvalidOperationException("no model here"),
            log: _ => { }, loginItems: new CountingLoginItems());
        var said = new List<(string, string)>();
        host.Problem += (title, why) =>
        {
            lock (said) said.Add((title, why));
        };
        host.Start();
        host.Recorder.Start("CS 101");
        var took = Stopwatch.StartNew();
        await Wait.UntilAsync(() => host.Recorder.Current?.State == LectureState.Paused, "the recording to pause", TimeSpan.FromSeconds(15));
        Assert.Equal(LectureState.Paused, host.Recorder.Current?.State);
        Assert.InRange(took.Elapsed.TotalSeconds, 4, 15);
        lock (said) Assert.Contains(("Recording paused", RecordingWords.CantHear(true)), said);
        Assert.Equal(RecordingWords.CantHear(true), host.RecorderProblem);
    }

    [Fact]
    public void Stopping_waits_for_the_work_and_happens_once()
    {
        using var home = new TempHome();
        var host = Host(home.Path);
        host.Start();
        var took = Stopwatch.StartNew();
        host.Dispose();
        Assert.True(took.Elapsed < TimeSpan.FromSeconds(3.5), $"took {took.Elapsed}");
        host.Dispose();
    }

    // --- pressing Record: asking for the microphone ---------------------------------------------------------------

    /// <summary>The system's answers about the microphone, made up: what it says now, and what the student answers
    /// when asked.</summary>
    sealed class FakeMics(MicAccess now, MicAccess answer = MicAccess.Allowed) : IMicPermissions
    {
        public MicAccess Now = now;
        public int Asked;

        public MicAccess Access() => Now;

        public Task<MicAccess> AskAsync()
        {
            Asked++;
            if (Now == MicAccess.NotAsked) Now = answer;
            return Task.FromResult(Now);
        }
    }

    /// <summary>A microphone the system won't start.</summary>
    sealed class BrokenMic(MicTrouble trouble) : IAudioSource
    {
        public string Name => "Broken";
        public int SampleRate => Sound.Rate;
        public int Channels => 1;
        public event Action<float[]>? Samples { add { } remove { } }
        public event Action<string>? Failed { add { } remove { } }
        public void Start() => throw new MicrophoneException(trouble, MicTrouble.InvalidDevice);
        public void Stop() { }
        public void Dispose() { }
    }

    static AppHost Recording(TempHome home, IMicPermissions mics, Func<IAudioSource>? mic = null, List<string>? log = null)
    {
        string wav = Wav(home);
        return new AppHost(home.Path, mic ?? (() => new FileMicrophone(wav)), () => throw new InvalidOperationException("no model here"), log: s =>
            {
                if (log is not null) lock (log) log.Add(s);
            }, loginItems: new CountingLoginItems(), models: new ModelSetting(File: home["model.bin"]), micPermissions: mics);
    }

    [Fact]
    public async Task Not_asked_yet_asks_then_records_once_the_student_says_yes()
    {
        using var home = new TempHome();
        var mics = new FakeMics(MicAccess.NotAsked, answer: MicAccess.Allowed);
        using var host = Recording(home, mics);
        var start = await host.RecordAsync("CS 101");
        Assert.Equal(1, mics.Asked);
        Assert.Null(start.Trouble);
        Assert.NotNull(start.Lecture);
        Assert.Same(host.Recorder.Current, start.Lecture);
        Assert.Single(host.Lectures.All());
        host.StopRecording();
    }

    /// <summary>Record on a call's ask: both sides, and named for the call the way a typed title is, so the library
    /// keeps the name instead of naming it from what was said.</summary>
    [Fact]
    public async Task A_calls_recording_records_both_sides_and_keeps_the_calls_name()
    {
        using var home = new TempHome();
        using var host = Recording(home, new FakeMics(MicAccess.Allowed));
        var l = (await host.RecordAsync("", call: "Zoom call")).Lecture!;
        Assert.True(host.RecordsCall);
        Assert.Equal(("Zoom call", true), (l.Title, l.TitleTyped));
        Assert.Equal(Core.Meeting.ByHand, l.Payload()["raw"]?[Core.Meeting.TitleBy]?.GetValue<string>());
        host.StopRecording();
        Assert.False((await host.RecordAsync("")).Lecture!.TitleTyped);
        Assert.False(host.RecordsCall);
        host.StopRecording();
    }

    /// <summary>⌘Q while a lecture records asks first; pressed again while the question shows, it neither asks again
    /// nor quits but brings the question to the front; "Keep recording" leaves the lecture recording.</summary>
    [Fact]
    public async Task Quit_while_recording_asks_once_and_no_keeps_it_recording()
    {
        using var home = new TempHome();
        using var host = Recording(home, new FakeMics(MicAccess.Allowed));
        var start = await host.RecordAsync("CS 101");
        Assert.NotNull(start.Lecture);
        var answer = new TaskCompletionSource<bool>();
        var asked = new List<ConfirmAsk>();
        int shown = 0;
        var guard = new QuitWhileRecording(() => host.Recorder.Current is not null, ask =>
        {
            asked.Add(ask);
            return answer.Task;
        }, () => shown++);

        var first = guard.MayQuitAsync();
        // A second quit while the question is open is answered at once (no), never a second question to wait on.
        var again = guard.MayQuitAsync();
        Assert.True(again.IsCompleted);
        Assert.False(await again); // already done: no wait
        Assert.Equal(1, shown);
        answer.SetResult(false);
        Assert.False(await first);

        Assert.Equal("You're recording", Assert.Single(asked).Title);
        Assert.Same(start.Lecture, host.Recorder.Current);
        host.StopRecording();
    }

    /// <summary>Told to stop (SIGTERM: launchd, kill) while the quit question shows: the signal goes straight to Quit
    /// (Shell.cs), never through the question, and quitting saves the recording. Once it's saved, a Quit while the
    /// unanswered question still shows goes straight through: nothing is left recording to ask about.</summary>
    [Fact]
    public async Task Told_to_stop_while_the_quit_question_shows_saves_the_recording()
    {
        using var home = new TempHome();
        string wav = Wav(home);
        // Twenty times real time: past the shortest recording that's kept (Recorder.KeepAtLeastSeconds) in a moment.
        using var host = Recording(home, new FakeMics(MicAccess.Allowed), () => new FileMicrophone(wav, speed: 20));
        var start = await host.RecordAsync("CS 101");
        var lecture = Assert.IsType<Lecture>(start.Lecture);
        await Wait.UntilAsync(() => host.Recorder.Recent(0) is { } r && r.Start + r.Samples.Length / (double)Sound.Rate > Recorder.KeepAtLeastSeconds + 1,
            "more than the shortest kept recording", TimeSpan.FromSeconds(15));
        var never = new TaskCompletionSource<bool>();
        var guard = new QuitWhileRecording(() => host.Recorder.Current is not null, _ => never.Task, () => { });
        var asking = guard.MayQuitAsync();
        Assert.False(asking.IsCompleted);

        // What quitting does to the engine (Shell.Quit → CleanUp → the container's Dispose).
        var took = Stopwatch.StartNew();
        host.Dispose();
        Assert.True(took.Elapsed < TimeSpan.FromSeconds(3.5), $"took {took.Elapsed}");

        Assert.Null(host.Recorder.Current);
        var saved = host.Lectures.Get(lecture.Id)!;
        // Kept and handed on (to Whisper; with no model here it may stop there), not left recording or thrown away.
        Assert.DoesNotContain(saved.State, new[] { LectureState.Recording, LectureState.Paused });
        Assert.True(Sound.WavSeconds(host.Lectures.AudioPath(lecture.Id)) > Recorder.KeepAtLeastSeconds);
        var next = guard.MayQuitAsync();
        Assert.True(next.IsCompleted);
        Assert.True(await next);
        Assert.False(asking.IsCompleted);
    }

    [Fact]
    public async Task Not_asked_yet_and_refused_records_nothing_and_says_where_to_allow_it()
    {
        using var home = new TempHome();
        var mics = new FakeMics(MicAccess.NotAsked, answer: MicAccess.Denied);
        using var host = Recording(home, mics, () => throw new InvalidOperationException("the microphone was opened"));
        var start = await host.RecordAsync("CS 101");
        Assert.Equal(1, mics.Asked);
        Assert.Null(start.Lecture);
        Assert.Null(host.Recorder.Current);
        Assert.Equal(MicTroubleKind.Denied, start.Trouble!.Kind);
        Assert.Equal("Syft Notes can't use the microphone", start.Trouble.Title);
        Assert.Equal(OperatingSystem.IsWindows() ? "Open Settings" : "Open System Settings", start.Trouble.ActionLabel);
    }

    [Theory]
    [InlineData(MicAccess.Denied)]
    [InlineData(MicAccess.Restricted)]
    public async Task Denied_asks_nothing_and_opens_nothing(MicAccess access)
    {
        using var home = new TempHome();
        var mics = new FakeMics(access);
        using var host = Recording(home, mics, () => throw new InvalidOperationException("the microphone was opened"));
        var start = await host.RecordAsync("CS 101");
        Assert.Equal(0, mics.Asked);
        Assert.Null(start.Lecture);
        Assert.Equal(MicTroubleKind.Denied, start.Trouble!.Kind);
    }

    [Fact]
    public async Task Allowed_or_unknown_just_records_without_asking()
    {
        foreach (var access in new[] { MicAccess.Allowed, MicAccess.Unknown })
        {
            using var home = new TempHome();
            var mics = new FakeMics(access);
            using var host = Recording(home, mics);
            var start = await host.RecordAsync("");
            Assert.Equal(0, mics.Asked);
            Assert.NotNull(start.Lecture);
            host.StopRecording();
        }
    }

    [Fact]
    public async Task No_microphone_says_so_in_plain_words_and_logs_the_code()
    {
        using var home = new TempHome();
        var log = new List<string>();
        using var host = Recording(home, new FakeMics(MicAccess.Allowed), () => new BrokenMic(MicTrouble.NoDevice()), log);
        var start = await host.RecordAsync("CS 101");
        Assert.Null(start.Lecture);
        Assert.Null(host.Recorder.Current);
        Assert.Empty(host.Lectures.All());
        Assert.Equal("No microphone found", start.Trouble!.Title);
        Assert.Equal("Open Sound settings", start.Trouble.ActionLabel);
        Assert.DoesNotContain("66680", start.Trouble.Title + start.Trouble.Detail);
        lock (log) Assert.Contains(log, l => l.Contains("-66680"));
    }

    [Fact]
    public async Task A_pretend_microphone_never_asks_the_system()
    {
        using var home = new TempHome();
        using var host = Host(home.Path, () => new FileMicrophone(Wav(home)));
        Assert.Equal(MicAccess.Allowed, host.MicAccess());
        Assert.Equal(MicAccess.Allowed, await host.AskMicAsync());
    }

}
