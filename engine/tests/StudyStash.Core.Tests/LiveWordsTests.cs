using StudyStash.Audio;

namespace StudyStash.Core.Tests;

/// <summary>The recorder's live words: short passes over the newest sound put together, only while a lecture is
/// written down as it records, and Cactus Whistle itself where this computer has its engine.</summary>
public class LiveWordsTests
{
    /// <summary>A lecturer who says a word every 0.4 s (each lasting 0.3 s) for 20 s, quiet from 8 to 11 s.</summary>
    static readonly List<TimedWord> Said =
    [
        .. Enumerable.Range(0, 50).Select(i => i * 0.4).Where(t => t is < 8 or >= 11)
            .Select((t, i) => new TimedWord(t, t + 0.3, i % 7 == 6 ? $"w{i}." : $"w{i}")),
    ];

    /// <summary>What a pass from <paramref name="from"/> to <paramref name="end"/> hears: the words said wholly inside
    /// it, timed from its start. A word cut off by the end of the sound isn't heard yet.</summary>
    static List<TimedWord> Hear(double from, double end) =>
        [.. Said.Where(w => w.Start >= from - 0.05 && w.End <= end).Select(w => w with { Start = w.Start - from, End = w.End - from })];

    [Fact]
    public void Passes_every_second_make_the_lecture_with_no_word_lost_or_twice()
    {
        var s = new CaptionStitcher();
        int passes = 0;
        for (double end = 1; end <= 24; end += 1)
        {
            double from = s.From(end);
            s.Heard(from, end, Hear(from, end));
            passes++;
            // The first words show a second in, and the newest is never more than about a second late.
            if (end == 1) Assert.Equal(["w0", "w1"], s.Words.Select(w => w.Text));
            var due = Said.Where(w => w.End <= end - 0.1).ToList();
            if (due.Count > 0) Assert.Contains(s.Words, w => w.Text == due[^1].Text);
            // A pass only hears what isn't settled: a few seconds, not the whole lecture.
            Assert.True(end - from <= 4.5, $"the pass at {end} s heard from {from} s");
        }
        Assert.Equal(Said.Select(w => w.Text), s.Words.Select(w => w.Text));
        Assert.Equal(Said.Select(w => Math.Round(w.Start, 2)), s.Words.Select(w => Math.Round(w.Start, 2)));
        Assert.Empty(s.Tentative);
        // Lines end at a sentence and at the long quiet.
        var lines = WordLines.Lines(s.Words);
        Assert.Equal("w0 w1 w2 w3 w4 w5 w6.", lines[0].Text);
        Assert.Contains(lines, l => Math.Abs(l.Start - 11.2) < 0.01);
    }

    [Fact]
    public void A_word_heard_again_where_the_last_pass_settled_is_kept_once_and_quiet_moves_on()
    {
        var s = new CaptionStitcher();
        s.Heard(0, 5, [new(0.2, 0.6, "Okay,"), new(0.7, 1.0, "today"), new(3.5, 3.9, "recursion")]);
        Assert.Equal(["Okay,", "today"], s.Settled.Select(w => w.Text));
        double next = s.Next;
        Assert.InRange(next, 1.0, 3.5);
        // The next pass starts just after "today" and hears its tail as "today" again.
        s.Heard(next, 7, [new(0.0, 0.1, "today"), new(3.5 - next, 3.9 - next, "recursion.")]);
        Assert.Equal(["Okay,", "today", "recursion."], s.Words.Select(w => w.Text));
        // Nothing said for a while: it doesn't keep hearing the same quiet.
        s.Quiet(30);
        Assert.Equal(30 - s.SettleAfter, s.Next, 3);
        // The transcript caught up: the live words it has are let go.
        s.Forget(2);
        Assert.Equal(["recursion."], s.Words.Select(w => w.Text));
    }

    [Fact]
    public void The_recorder_shows_the_transcript_where_it_has_got_and_the_live_words_after_it()
    {
        List<Spoken> written = [new(0, 9.5, "Okay, today recursion."), new(10, 29, "A function that calls itself.")];
        TimedWord[] live = [new(28.9, 29.2, "itself."), new(29.6, 30, "The"), new(30.1, 30.6, "base"), new(30.7, 31, "case.")];
        var lines = LiveCaptioner.Merge(written, 29.5, live);
        Assert.Equal(["Okay, today recursion.", "A function that calls itself.", "The base case."], lines.Select(l => l.Text));
        Assert.Equal(29.6, lines[^1].Start);
    }

    sealed class FakeHearer : IWordHearer
    {
        public int Calls;
        public string Keywords = "";

        public HeardWords Hear(float[] samples, string language, string keywords)
        {
            Calls++;
            Keywords = keywords;
            double seconds = samples.Length / (double)Sound.Rate;
            return new(seconds < 0.6 ? [] : [new TimedWord(0.1, 0.4, "Hello")], "en");
        }
    }

    static void WaitFor(Func<bool> done)
    {
        var until = DateTime.UtcNow.AddSeconds(5);
        while (!done() && DateTime.UtcNow < until) Thread.Sleep(10);
        Assert.True(done());
    }

    /// <summary>Words show a second in, for a lecture written down as it records, once the recorder is open (closed,
    /// nothing is heard); one written down after class only records (as Settings promises): nothing hears it, and
    /// nothing is written down, until it stops.</summary>
    [Fact]
    public async Task Live_words_come_a_second_in_but_never_for_a_lecture_written_down_after_class()
    {
        using var dir = new TempDir();
        var store = new LectureStore(dir.Path);
        FakeMic? mic = null;
        using var rec = new Recorder(store, () => mic = new FakeMic());
        var hearer = new FakeHearer();
        bool open = false;
        var captions = new LiveCaptioner(() => rec.Current, rec.Recent, _ => (hearer, "en")) { Watched = () => open };
        var heard = new List<string>();
        captions.Changed += l => heard.Add(l.Id);

        var live = rec.Start("Data Mining");
        Assert.False(captions.Step()); // nothing recorded yet
        mic!.Play(1.5);
        WaitFor(() => rec.Elapsed >= 1.45);
        Assert.False(captions.Step()); // the recorder is closed: nobody sees the words, so nothing hears them
        Assert.Equal(0, hearer.Calls);
        open = true;
        Assert.True(captions.Step());
        Assert.Equal(["Hello"], captions.Words(live.Id).Select(w => w.Text));
        Assert.Equal([live.Id], heard);
        Assert.Equal("Data Mining", hearer.Keywords);
        // Paused, there's nothing new to hear; resumed, it hears on in the lecture's own time.
        rec.Pause();
        Assert.False(captions.Step());
        rec.Resume();
        mic.Play(1);
        WaitFor(() => rec.Elapsed >= 2.45);
        Assert.Equal(rec.Elapsed, rec.Recent(0)!.End, 2);
        rec.Stop();
        Assert.False(captions.Step());
        Assert.Empty(captions.Words(live.Id));

        // After class: it records, and only records.
        int calls = hearer.Calls;
        var whisper = new FakeWhisper();
        var worker = new TranscriptionWorker(store, () => whisper, () => rec.Current);
        while (await worker.StepAsync(default)) { }
        whisper.Calls.Clear();
        var later = rec.Start("Data Mining", afterClass: true);
        mic.Play(35);
        WaitFor(() => rec.Elapsed >= 34.9);
        Assert.False(captions.Step());
        Assert.False(await worker.StepAsync(default));
        Assert.Equal(calls, hearer.Calls);
        Assert.Empty(whisper.Calls);
        Assert.Empty(captions.Words(later.Id));
        Assert.Equal(35, rec.Stop()!.Seconds, 1);
    }

    /// <summary>A hearer that takes <paramref name="ratio"/> seconds (on a pretend clock) for each second it hears, and
    /// hears a word a second.</summary>
    sealed class TimedHearer(double ratio) : IWordHearer
    {
        public double Now;
        public int Calls;
        public double Longest;

        public HeardWords Hear(float[] samples, string language, string keywords)
        {
            Calls++;
            double seconds = samples.Length / (double)Sound.Rate;
            Longest = Math.Max(Longest, seconds);
            Now += ratio * seconds;
            return new([.. Enumerable.Range(0, (int)seconds).Select(i => new TimedWord(i + 0.1, i + 0.5, $"w{i}"))], "en");
        }
    }

    /// <summary>The recorder opens on a lecture 12 s in. Where a pass takes 1.5× the sound it hears (a CI Intel Mac), one
    /// short pass says so, and at 0.5× two; then the live words are off: no word shown, never a long pass, nothing heard
    /// again. Where it takes 0.01×, a short first pass, then the rest in one, and the words show from the first.</summary>
    [Theory]
    [InlineData(1.5)]
    [InlineData(0.5)]
    [InlineData(0.01)]
    public void The_live_words_are_off_where_this_computer_is_too_slow_for_them(double ratio)
    {
        using var dir = new TempDir();
        var store = new LectureStore(dir.Path);
        FakeMic? mic = null;
        using var rec = new Recorder(store, () => mic = new FakeMic());
        var hearer = new TimedHearer(ratio);
        var captions = new LiveCaptioner(() => rec.Current, rec.Recent, _ => (hearer, "en")) { Clock = () => hearer.Now };
        double? slow = null;
        captions.FoundTooSlow += r => slow = r;
        int changes = 0;
        captions.Changed += _ => changes++;
        var l = rec.Start("Data Mining");
        mic!.Play(12);
        WaitFor(() => rec.Elapsed >= 11.95);

        Assert.True(captions.Step());
        Assert.InRange(hearer.Longest, LiveCaptioner.ProbeSeconds - 0.1, LiveCaptioner.ProbeSeconds + 0.01); // a short first pass, timed
        if (ratio > LiveCaptioner.MostRatio)
        {
            Assert.Empty(captions.Words(l.Id)); // not shown until this computer has shown it keeps up
            if (ratio <= 1)
            {
                Assert.Equal(0, changes);
                Assert.False(captions.TooSlow);
                Assert.True(captions.Step());
            }
            Assert.True(captions.TooSlow);
            Assert.Equal(ratio, slow!.Value, 2);
            Assert.Empty(captions.Words(l.Id));
            Assert.False(captions.Step());
            Assert.Equal(ratio <= 1 ? 2 : 1, hearer.Calls);
            // No pass ever took longer than the short first one, or about a second and a half once the speed was known.
            Assert.True(hearer.Longest * ratio <= Math.Max(LiveCaptioner.PassBudget, LiveCaptioner.ProbeSeconds * ratio) + 0.01, $"{hearer.Longest} s heard");
        }
        else
        {
            Assert.NotEmpty(captions.Words(l.Id));
            Assert.True(captions.Step()); // the rest, in one pass
            Assert.Equal(2, hearer.Calls);
            Assert.False(captions.TooSlow);
            Assert.Null(slow);
            Assert.Contains(captions.Words(l.Id), w => w.Start > 9);
        }
    }

    /// <summary>Real Cactus Whistle on the recording of speech (Fixtures/speech.wav), where the app carries its engine for
    /// this computer (Mac, Windows; not Linux): the words, their times, and nothing made up over silence.</summary>
    [Fact]
    public async Task Whistle_hears_the_words()
    {
        if (!WhistleTranscriber.Available) return;
        using var whistle = new WhistleTranscriber(WhistleTranscriber.BundledModel);
        var speech = Sound.ReadWav(Path.Combine(AppContext.BaseDirectory, "Fixtures", "speech.wav"));
        var t = await whistle.TranscribeAsync(speech, "", "", default);
        string said = string.Join(" ", t.Segments.Select(s => s.Text)).ToLowerInvariant();
        Assert.Contains("midterm", said);
        Assert.Contains("recursion", said);
        Assert.Equal("en", t.Language);
        var words = whistle.Hear(speech, "", "").Words;
        Assert.True(words.Count >= 8, string.Join(" ", words.Select(w => w.Text)));
        for (int i = 1; i < words.Count; i++) Assert.True(words[i].Start >= words[i - 1].Start && words[i].End >= words[i].Start);
        Assert.InRange(words[^1].End, 3, speech.Length / (double)Sound.Rate + 0.1);
        Assert.Equal("en", whistle.Hear(speech, "", "").Language);
        Assert.Empty(whistle.Hear(new float[Sound.Rate * 3], "en", "").Words);
    }
}
