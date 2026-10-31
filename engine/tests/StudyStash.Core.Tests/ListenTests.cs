using System.Text;
using StudyStash.Core.Study;

namespace StudyStash.Core.Tests;

/// <summary>Listen: a lecture as a short two-voice conversation, its script written by the library's AI from the notes
/// and exam flags, read by the computer's own voices and joined into one WAV with a breath between turns; made in the
/// background and fetched by the app. The voices are stand-ins here, except in the one test that uses this computer's.</summary>
public class ListenTests
{
    /// <summary>Voices that "say" each line as a tone a tenth of a second per word, A high and B low.</summary>
    public sealed class ToneVoices(bool ready = true) : ISpeech
    {
        public List<(string Who, string Text)> Said { get; } = [];
        public (bool Ok, string Why) Ready() => ready ? (true, "") : (false, "No voices here.");

        public Task SayAsync(string text, string who, string wav, CancellationToken ct)
        {
            lock (Said) Said.Add((who, text));
            int words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
            var samples = new short[Wav.Rate * words / 10];
            for (int i = 0; i < samples.Length; i++) samples[i] = (short)(8000 * Math.Sin(2 * Math.PI * (who == "A" ? 440 : 220) * i / Wav.Rate));
            Wav.Write(wav, samples);
            return Task.CompletedTask;
        }
    }

    public const string Script = """
        {"lines":[
          {"who":"A","text":"Today was blood vessels: arteries, capillaries and veins."},
          {"who":"B","text":"So arteries carry blood **to** the heart?"},
          {"who":"A","text":"[laughs] Away from it. Veins bring it back, with valves."},
          {"who":"C","text":"Remember the three layers of an artery wall."},
          {"who":"B","text":"   "}
        ]}
        """;

    [Fact]
    public void A_script_is_read_as_turns_said_aloud()
    {
        var lines = Listen.ReadScript(Script);
        Assert.Equal(["A", "B", "A", "A"], lines.Select(l => l.Who));
        Assert.Equal("So arteries carry blood to the heart?", lines[1].Text);
        Assert.Equal("Away from it. Veins bring it back, with valves.", lines[2].Text);
    }

    [Fact]
    public void The_scripts_prompt_asks_for_two_voices_to_be_heard_with_the_flags()
    {
        var lecture = new StudyLecture("v1", "Blood vessels", "BIO 110", "2026-10-08", "## Vessels\n- Arteries.", "",
            ExamHints.Find(StudyLibrary.BloodTranscript));
        string prompt = Listen.ScriptPrompt(lecture);
        Assert.Contains("Two people talk it through", prompt);
        Assert.Contains("[02:30] This will definitely be on the exam", prompt);
        Assert.Contains("- Arteries.", prompt);
    }

    [Fact]
    public void Wavs_of_any_rate_and_channels_are_joined_into_one_with_pauses()
    {
        using var dir = new TempDir();
        string a = Path.Combine(dir.Path, "a.wav"), b = Path.Combine(dir.Path, "b.wav"), joined = Path.Combine(dir.Path, "all.wav");
        Wav.Write(a, new short[Wav.Rate]); // one second
        // Two seconds of stereo at 44.1 kHz, with a JUNK chunk before its format, as say writes.
        WriteStereo44k(b, seconds: 2);

        double seconds = Wav.Join([a, b], joined, pauseSeconds: 0.5);

        Assert.Equal(3.5, seconds, 2);
        var (channels, rate, bits, data) = Wav.Read(joined);
        Assert.Equal((1, Wav.Rate, 16), (channels, rate, bits));
        Assert.InRange(data.Length, (int)(3.5 * Wav.Rate) * 2 - 8, (int)(3.5 * Wav.Rate) * 2 + 8);
        Assert.InRange(Wav.Mono(b)[1000], 11970, 12030); // the two channels' 8000 and 16000, mixed
    }

    static void WriteStereo44k(string path, int seconds)
    {
        using var w = new BinaryWriter(File.Create(path));
        int frames = 44100 * seconds, bytes = frames * 4;
        w.Write("RIFF"u8);
        w.Write(36 + 36 + bytes);
        w.Write("WAVE"u8);
        w.Write("JUNK"u8);
        w.Write(28);
        w.Write(new byte[28]);
        w.Write("fmt "u8);
        w.Write(16);
        w.Write((short)1);
        w.Write((short)2);
        w.Write(44100);
        w.Write(44100 * 4);
        w.Write((short)4);
        w.Write((short)16);
        w.Write("data"u8);
        w.Write(bytes);
        for (int i = 0; i < frames; i++)
        {
            w.Write((short)8000);
            w.Write((short)16000);
        }
    }

    [Fact]
    public void Something_that_isnt_a_wav_says_so()
    {
        using var dir = new TempDir();
        string bad = Path.Combine(dir.Path, "bad.wav");
        File.WriteAllText(bad, "not sound");
        Assert.Throws<InvalidDataException>(() => Wav.Read(bad));
    }

    [Fact]
    public async Task The_library_makes_a_lectures_listen_in_the_background_and_hands_over_its_sound()
    {
        var voices = new ToneVoices();
        await using var lib = await StudyLibrary.StartAsync(l => l.Speech = voices);
        lib.Answers.Add(Script);

        Assert.Equal(ListenInfo.None, (await lib.Study.ListenAsync("v1"))!.State);
        var started = (await lib.Study.MakeListenAsync("v1"))!;
        Assert.Equal(ListenInfo.Making, started.State);
        var info = await lib.ListenedAsync("v1");
        Assert.Equal(ListenInfo.Ready, info.State);
        Assert.Equal(4, info.Script.Count);
        Assert.Equal([("A", "Today was blood vessels: arteries, capillaries and veins."), ("B", "So arteries carry blood to the heart?")],
            voices.Said.Take(2));
        // 7 + 7 + 9 + 8 words at a tenth of a second each, and three pauses of 0.35 s.
        Assert.Equal(3.1 + 1.05, info.Seconds, 1);
        Assert.Contains("Two people talk it through", lib.Claude.Prompts.Single());

        using var dir = new TempDir();
        string saved = Path.Combine(dir.Path, "listen.wav");
        Assert.True(await lib.Study.ListenAudioAsync("v1", saved));
        Assert.Equal(info.Seconds, Wav.Mono(saved).Length / (double)Wav.Rate, 2);
        Assert.False(await lib.Study.ListenAudioAsync("m1", Path.Combine(dir.Path, "none.wav")));
    }

    [Fact]
    public async Task A_listen_that_couldnt_be_made_says_why_and_can_be_tried_again()
    {
        await using var lib = await StudyLibrary.StartAsync(l => l.Speech = new ToneVoices());
        lib.Answers.Add("not a script");
        await lib.Study.MakeListenAsync("v1");
        var failed = await lib.ListenedAsync("v1");
        Assert.Equal(ListenInfo.Failed, failed.State);
        Assert.NotEmpty(failed.Error);

        lib.Answers.Add(Script);
        await lib.Study.MakeListenAsync("v1");
        Assert.Equal(ListenInfo.Ready, (await lib.ListenedAsync("v1")).State);
    }

    [Fact]
    public async Task A_computer_with_no_voices_says_so_before_it_asks_the_AI()
    {
        await using var lib = await StudyLibrary.StartAsync(l => l.Speech = new ToneVoices(ready: false));
        Assert.False((await lib.Study.ListenAsync("v1"))!.CanSpeak);
        var e = await Assert.ThrowsAsync<LibraryRefusedException>(() => lib.Study.MakeListenAsync("v1"));
        Assert.Equal((409, "No voices here."), (e.Status, e.Message));
        Assert.Empty(lib.Claude.Prompts);
        Assert.Equal(404, (await Assert.ThrowsAsync<LibraryRefusedException>(() => lib.Study.MakeListenAsync("nope"))).Status);
    }

    /// <summary>This computer's own voices (say on a Mac, Windows' speech, espeak on Linux) speak a line into a WAV
    /// Listen can join. A Linux without espeak has none, and says so.</summary>
    [Fact]
    public async Task This_computers_voices_speak_a_line()
    {
        var voices = Voices.ForThisComputer();
        if (voices.Ready() is (false, var why))
        {
            Assert.True(OperatingSystem.IsLinux(), why);
            Assert.Contains("espeak", why);
            return;
        }
        using var dir = new TempDir();
        string a = Path.Combine(dir.Path, "a.wav"), b = Path.Combine(dir.Path, "b.wav"), all = Path.Combine(dir.Path, "all.wav");
        await voices.SayAsync("Arteries carry blood away from the heart.", "A", a, CancellationToken.None);
        await voices.SayAsync("And veins bring it back?", "B", b, CancellationToken.None);
        double seconds = Wav.Join([a, b], all, 0.35);
        Assert.InRange(seconds, 1.5, 15);
        Assert.Contains(Wav.Mono(all), s => Math.Abs((int)s) > 1000); // something was said, not quiet
    }
}
