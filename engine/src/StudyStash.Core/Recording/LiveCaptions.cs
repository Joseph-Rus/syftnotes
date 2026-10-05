namespace StudyStash.Core;

/// <summary>One word heard, with when it starts and ends (seconds) and how sure the model was of it (0 to 1).</summary>
public sealed record TimedWord(double Start, double End, string Text, double Probability = 1);

/// <summary>What a pass heard: the words with their times in it, the language it took them for ("" for none), and,
/// when the hearer timed it itself, the seconds the hearing took without its one-off setting up (a small Whisper sets up
/// for each length of pass and language once: that isn't this computer being slow).</summary>
public sealed record HeardWords(IReadOnlyList<TimedWord> Words, string Language, double? Took = null);

/// <summary>A model quick enough to hear the newest few seconds of a lecture again and again (Cactus Whistle): 16 kHz
/// mono, at most 30 seconds a pass, to words with their times in the pass.</summary>
public interface IWordHearer
{
    /// <summary><paramref name="language"/>: a language code, or "" to find it. <paramref name="keywords"/>: words and
    /// names to favour, one a line ("" for none).</summary>
    HeardWords Hear(float[] samples, string language, string keywords);
}

/// <summary>A stretch of the recording: its 16 kHz mono sound and where it starts in the lecture (seconds).</summary>
public sealed record RecentSound(float[] Samples, double Start)
{
    public double End => Start + Samples.Length / (double)Sound.Rate;
}

/// <summary>Words with times as a transcript's lines: a line ends at the end of a sentence (once it has a few words)
/// and at a long pause.</summary>
public static class WordLines
{
    /// <summary>A pause this long between two words starts a new line, punctuation or not.</summary>
    const double LongPause = 1.2;
    /// <summary>A sentence ends a line only after this many words, so "Dr." or "e.g." doesn't make a line of its own.</summary>
    const int FewestWords = 3;
    /// <summary>A line this long ends at the next word, sentence or not, so a long run-on sentence still reads in lines.</summary>
    const int MostWords = 40;

    public static List<Spoken> Lines(IEnumerable<TimedWord> words)
    {
        var lines = new List<Spoken>();
        var text = new System.Text.StringBuilder();
        double from = 0, to = 0;
        int count = 0;
        bool ended = false;
        foreach (var w in words)
        {
            string word = w.Text.Trim();
            if (word.Length == 0) continue;
            if (count > 0 && (ended || count >= MostWords || w.Start - to >= LongPause))
            {
                lines.Add(new Spoken(from, Math.Max(to, from), text.ToString()));
                text.Clear();
                count = 0;
            }
            if (count == 0) from = w.Start;
            if (text.Length > 0) text.Append(' ');
            text.Append(word);
            to = Math.Max(to, w.End);
            count++;
            ended = count >= FewestWords && word[^1] is '.' or '?' or '!';
        }
        if (count > 0) lines.Add(new Spoken(from, Math.Max(to, from), text.ToString()));
        return lines;
    }
}

/// <summary>
/// Puts the live words together from many short passes over the newest sound. Each pass hears from <see cref="Next"/>
/// to the newest sound; the words it hears that ended a few seconds before the newest sound are kept for good
/// (settled: they've had what came after them to go on), and the next pass starts between the last of them and the
/// word after, so no word is cut in two and none is heard twice. The words after them are only this pass's guess
/// (<see cref="Tentative"/>): the next pass, with more sound, hears them again.
/// </summary>
public sealed class CaptionStitcher
{
    /// <summary>A pass hears at most this much (Whistle takes 30 seconds at a time).</summary>
    public const double MostSeconds = 29.5;
    /// <summary>Words that ended this long before the newest sound are settled.</summary>
    public double SettleAfter { get; init; } = 2.5;
    /// <summary>A pass also hears this much of what's settled before <see cref="Next"/>, so the model has the start of
    /// the sentence to go on; the words it hears there again are dropped. 3 seconds of it took the live words on two
    /// 12-minute stretches of recorded lectures from 53% and 51% of words different from large-v3 to 34% and 29%
    /// (Whistle), and from 69% and 67% to 58% and 48% (Whisper tiny), for about 1.5 times the work.</summary>
    public double Context { get; init; } = 3;

    readonly List<TimedWord> settled = [];
    List<TimedWord> tentative = [];

    /// <summary>Where the next pass starts, in the lecture (seconds).</summary>
    public double Next { get; private set; }

    public IReadOnlyList<TimedWord> Settled => settled;
    public IReadOnlyList<TimedWord> Tentative => tentative;

    /// <summary>Every word so far: the settled ones, then this pass's guess at the rest.</summary>
    public IEnumerable<TimedWord> Words => settled.Concat(tentative);

    /// <summary>Starts at <paramref name="at"/> (where the recording is when it begins, or 0).</summary>
    public CaptionStitcher(double at = 0) => Next = at;

    /// <summary>Where a pass over the sound up to <paramref name="end"/> starts: <see cref="Next"/>, or, when that's
    /// further back than a pass takes (the computer fell behind), as far back as a pass takes. What's skipped isn't
    /// lost: the transcript's own model writes all of it down.</summary>
    public double From(double end) => Math.Max(Math.Max(0, Next - Context), end - MostSeconds);

    /// <summary>A pass over <paramref name="from"/> to <paramref name="end"/> (seconds in the lecture) heard
    /// <paramref name="words"/> (their times from the start of the pass).</summary>
    public void Heard(double from, double end, IReadOnlyList<TimedWord> words)
    {
        var heard = new List<TimedWord>(words.Count);
        foreach (var w in words)
        {
            string text = w.Text.Trim();
            if (text.Length == 0) continue;
            double start = from + Math.Max(0, w.Start), stop = from + Math.Max(w.Start, w.End);
            heard.Add(new TimedWord(start, Math.Min(stop, end), text, w.Probability));
        }
        // What it heard before Next is settled already (the context it was given).
        double next = Next;
        heard.RemoveAll(w => (w.Start + w.End) / 2 < next);
        // The pass's new sound began where the last settled word ended: a word it hears again right there is that same word.
        if (heard.Count > 0 && settled.Count > 0 && heard[0].Start < Math.Max(from, next) + 0.3 && Same(heard[0].Text, settled[^1].Text))
            heard.RemoveAt(0);

        double settleBefore = end - SettleAfter;
        int k = 0;
        while (k < heard.Count && heard[k].End <= settleBefore) k++;
        if (k > 0)
        {
            settled.AddRange(heard.Take(k));
            Next = k < heard.Count ? (heard[k - 1].End + heard[k].Start) / 2 : heard[k - 1].End;
            Next = Math.Clamp(Next, Math.Max(from, next), end);
        }
        else
        {
            // Nobody spoke before the first word heard (or at all): no need to hear that quiet again, bar its last
            // moments, where a word may be starting.
            double quietTo = heard.Count == 0 ? end - SettleAfter : Math.Min(heard[0].Start - 0.5, settleBefore);
            Next = Math.Max(Next, quietTo);
        }
        tentative = heard.Skip(k).ToList();
    }

    /// <summary>The sound from <see cref="Next"/> to <paramref name="end"/> is quiet: nothing to hear, nothing to settle.</summary>
    public void Quiet(double end)
    {
        tentative = [];
        Next = Math.Max(Next, end - SettleAfter);
    }

    /// <summary>Let go of settled words that ended before <paramref name="seconds"/> (the transcript has them now).</summary>
    public void Forget(double seconds)
    {
        int n = 0;
        while (n < settled.Count && settled[n].End < seconds) n++;
        if (n > 0) settled.RemoveRange(0, n);
    }

    static bool Same(string a, string b) =>
        string.Equals(a.Trim(".,?!;:\"'".ToCharArray()), b.Trim(".,?!;:\"'".ToCharArray()), StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// The words of the lecture being recorded, a second or two after they're said: a quick model (<see cref="IWordHearer"/>)
/// hears the newest sound about once a second, on a thread of its own, and <see cref="CaptionStitcher"/> puts the
/// passes together. Only while a lecture records and is written down as it records (a lecture written down after
/// class, <see cref="Lecture.AfterClass"/>, only records, and a paused one has nothing new), and only while someone
/// can see them (<see cref="Watched"/>: the recorder open, or the menu's panel): hearing every second costs about a
/// sixth of one core of an M3 Pro, and nobody reads the words in the closed pill. Opened, the first pass hears the last
/// half minute at once, so the words are there within a second or two. These words are only for
/// the recorder to show and to ask about; the transcript the notes are made from is the <see cref="TranscriptionWorker"/>'s,
/// and <see cref="Merge"/> puts its lines first wherever it has got to.
/// <para>Only where this computer is fast enough: every pass is timed against the sound it heard. A pass may take at
/// most <see cref="MostRatio"/> of it (3 seconds of sound in under a second), or the live words couldn't keep up
/// without a core to themselves. Until a pass has shown this computer is fast enough its words aren't shown, and a pass
/// hears 2 seconds at most; after that a pass hears no more than about <see cref="PassBudget"/> seconds of work. One
/// pass slower than the sound itself, or two slow passes in a row (three once it has kept up), and the live words
/// are off for this lecture (<see cref="TooSlow"/>): nothing hears them again until the next lecture, which is judged
/// afresh, and the recorder shows the transcript's own lines, as it did before there were live words. Whistle on a CI
/// Intel Mac took 25 s over 17 s of sound (1.5×) while Whisper wrote the transcript on the same processor, which is
/// why x64 computers hear the live words with a small Whisper instead; an M3 Pro takes under 0.04× even with every
/// core busy.</para>
/// </summary>
public sealed class LiveCaptioner(Func<Lecture?> recording, Func<double, RecentSound?> recent, Func<Lecture, (IWordHearer Hearer, string Language)?> hearer,
    Action<string>? log = null)
{
    /// <summary>Less new sound than this since the last pass waits for more.</summary>
    const double LeastNew = 0.5;
    /// <summary>The most a pass may take, as a share of the sound it hears, for the live words to be on.</summary>
    public const double MostRatio = 0.3;
    /// <summary>How much sound a pass hears until this computer's speed is known.</summary>
    public const double ProbeSeconds = 2;
    /// <summary>About the longest a pass should take, once the speed is known: it hears no more than that much work.</summary>
    public const double PassBudget = 1.5;
    // The speed: seconds a pass takes for each second it hears, leaning to the slowest lately; null until timed.
    double? ratio;
    bool fastEnough;
    int slowInARow;
    readonly Action<string> log = log ?? (_ => { });
    readonly Lock gate = new();
    CaptionStitcher? stitcher;
    string? lectureId;
    double heardTo;
    bool told;
    // The language the passes found, when the lecture's isn't known: a few seconds can sound like another language, so
    // a language is kept once three passes of a few seconds or more agree on it.
    readonly Dictionary<string, int> votes = [];
    string found = "";

    /// <summary>How often it hears the newest sound.</summary>
    public TimeSpan Every { get; init; } = TimeSpan.FromSeconds(1);

    /// <summary>Someone can see the live words (the recorder is open): only then is the sound heard.</summary>
    public Func<bool> Watched { get; init; } = () => true;

    readonly AutoResetEvent woken = new(false);

    /// <summary>Hear now, not at the next second (the recorder just opened).</summary>
    public void Wake() => woken.Set();

    /// <summary>The live words changed: the lecture they're for.</summary>
    public event Action<Lecture>? Changed;

    /// <summary>When the first words of the lecture now recording were heard (seconds into the lecture, and the
    /// seconds the pass took), for the log; null until then.</summary>
    public (double At, double Took)? First { get; private set; }

    /// <summary>Seconds, for timing passes (a test's own clock).</summary>
    public Func<double> Clock { get; init; } = () => System.Diagnostics.Stopwatch.GetTimestamp() / (double)System.Diagnostics.Stopwatch.Frequency;

    /// <summary>This computer is too slow for the live words in the lecture now recording: no pass is heard again
    /// until the next lecture.</summary>
    public bool TooSlow { get; private set; }

    /// <summary>The live words turned off for being too slow: how long a pass took for each second it heard.</summary>
    public event Action<double>? FoundTooSlow;

    /// <summary>How long a pass takes for each second of sound it hears on this computer (leaning to the slowest
    /// lately); null until a pass has been timed.</summary>
    public double? Ratio => ratio;

    /// <summary>Passes heard so far (the self-test checks none are once the live words are off).</summary>
    public int Passes { get; private set; }

    /// <summary>The live words of lecture <paramref name="id"/> from <paramref name="from"/> seconds on; empty for any other,
    /// and until this computer has shown it's fast enough.</summary>
    public IReadOnlyList<TimedWord> Words(string id, double from = 0)
    {
        lock (gate)
            return stitcher is not null && lectureId == id && fastEnough && !TooSlow ? [.. stitcher.Words.Where(w => w.Start >= from)] : [];
    }

    /// <summary>The most sound the next pass hears: a short one until the speed is known, then about
    /// <see cref="PassBudget"/> seconds of work.</summary>
    double MostToHear => ratio is not { } r ? ProbeSeconds : Math.Clamp(PassBudget / Math.Max(r, 1e-3), ProbeSeconds, CaptionStitcher.MostSeconds);

    /// <summary>One pass, if there's new sound to hear: true when it heard (or skipped quiet) some.</summary>
    public bool Step()
    {
        var l = recording();
        if (l is null || l.AfterClass || l.State != LectureState.Recording)
        {
            if (l is null) Reset(null);
            return false;
        }
        if (l.Id != lectureId) Reset(l);
        if (TooSlow || !Watched() || hearer(l) is not { } h) return false;
        var s = stitcher!;
        // What the transcript has written down already isn't heard again, bar a few seconds before it for context.
        var sound = recent(Math.Max(0, Math.Max(s.Next, l.TranscribedSeconds) - s.Context));
        if (sound is null || sound.End - heardTo < LeastNew) return false;
        double from = Math.Max(s.From(sound.End), sound.Start);
        // A pass hears from where the last left off, no more than this computer gets through in a moment: the rest
        // is the next pass's.
        double end = Math.Min(sound.End, from + MostToHear);
        int skip = (int)Math.Round((from - sound.Start) * Sound.Rate);
        var samples = sound.Samples[Math.Min(skip, sound.Samples.Length)..Math.Min(sound.Samples.Length, skip + (int)Math.Round((end - from) * Sound.Rate))];
        heardTo = end;
        if (new Chunk(0, samples).Silent())
        {
            lock (gate) s.Quiet(end);
            return true;
        }
        double started = Clock();
        IReadOnlyList<TimedWord> words;
        double? hearing;
        Passes++;
        try
        {
            var heard = h.Hearer.Hear(samples, h.Language.Length > 0 ? h.Language : found, Keywords(l.ClassName));
            words = heard.Words;
            hearing = heard.Took;
            if (h.Language.Length == 0 && found.Length == 0 && heard.Language.Length > 0 && end - from >= 3
                && (votes[heard.Language] = votes.GetValueOrDefault(heard.Language) + 1) >= 3)
                found = heard.Language;
        }
        catch (Exception e)
        {
            if (!told) log($"[live] {l.Id}: {e.Message}");
            told = true;
            return false;
        }
        double took = hearing ?? Clock() - started;
        lock (gate)
        {
            s.Heard(from, end, words);
            // What the transcript has written down is its to show: the live words before it aren't needed any more.
            s.Forget(l.TranscribedSeconds - 5);
        }
        if (!Timed(took, end - from))
        {
            log(string.Create(System.Globalization.CultureInfo.InvariantCulture,
                $"[live] {l.Id}: the live words took {ratio:0.00}× the sound they heard (at most {MostRatio}× keeps up): they're off for this lecture, and the recorder shows the transcript's own lines"));
            FoundTooSlow?.Invoke(ratio ?? 0);
            Changed?.Invoke(l);
            return true;
        }
        if (!fastEnough) return true;
        if (First is null && words.Count > 0)
        {
            First = (from + words[0].Start, took);
            log(string.Create(System.Globalization.CultureInfo.InvariantCulture,
                $"[live] {l.Id}: first words at {from + words[0].Start:0.0} s into the lecture, heard {sound.End - from - words[0].Start:0.0} s later in a {took * 1000:0} ms pass ({ratio:0.000}× the sound)"));
        }
        Changed?.Invoke(l);
        return true;
    }

    /// <summary>A pass took <paramref name="took"/> seconds over <paramref name="heard"/> seconds of sound: false once
    /// that makes this computer too slow for the live words. A pass under a second of sound says little (it's mostly
    /// the engine's own setup), so it isn't counted.</summary>
    bool Timed(double took, double heard)
    {
        if (heard < 1) return true;
        double r = took / heard;
        ratio = ratio is { } before ? Math.Max(r, before * 0.7 + r * 0.3) : r;
        if (r <= MostRatio)
        {
            slowInARow = 0;
            fastEnough = true;
            return true;
        }
        // Slower than the sound itself can never keep up: off at once. Slow but not that slow may be a moment's
        // hiccup (the transcript's model loading): off after two in a row, or three once it has kept up.
        if (r <= 1 && ++slowInARow < (fastEnough ? 3 : 2)) return true;
        ratio = Math.Max(r, ratio ?? r);
        TooSlow = true;
        return false;
    }

    void Reset(Lecture? l)
    {
        lock (gate)
        {
            if (lectureId == l?.Id) return;
            lectureId = l?.Id;
            // Hear from where the recording is now: a lecture picked up again after a restart isn't heard from the top.
            stitcher = l is null ? null : new CaptionStitcher(Math.Max(0, l.Seconds));
            heardTo = 0;
            told = false;
            First = null;
            votes.Clear();
            found = "";
            // Each lecture is judged afresh: a busy moment in one (the transcript's model loading, another app) doesn't
            // switch the next one's live words off.
            ratio = null;
            fastEnough = false;
            slowInARow = 0;
            TooSlow = false;
        }
    }

    /// <summary>The class's name, as words to favour: a name or a subject the lecturer says.</summary>
    static string Keywords(string className) => className.Trim().Length > 2 && className.Any(char.IsLetter) ? className.Trim() : "";

    /// <summary>Hear about every <see cref="Every"/> until <paramref name="stop"/>: on its own thread, below the app's
    /// own work, so the recorder and the transcript never wait for it. A computer that takes long over a pass hears less
    /// often (a pass's time at most a quarter of the time), so the live words never cost it much.</summary>
    public void Run(CancellationToken stop)
    {
        while (!stop.IsCancellationRequested)
        {
            var started = DateTime.UtcNow;
            try
            {
                Step();
            }
            catch (Exception e)
            {
                log($"[live] {e.Message}");
            }
            var took = DateTime.UtcNow - started;
            var left = (took * 4 > Every ? took * 4 : Every) - took;
            // Nothing recording: look less often.
            if (recording() is not { State: LectureState.Recording, AfterClass: false }) left = TimeSpan.FromSeconds(2);
            if (left > TimeSpan.Zero) WaitHandle.WaitAny([stop.WaitHandle, woken], left);
        }
    }

    /// <summary>
    /// What's been said so far, as lines: the transcript's own (<paramref name="written"/>, the model the notes are made
    /// from) up to where it has got (<paramref name="writtenTo"/> seconds), then the live words after that.
    /// </summary>
    public static List<Spoken> Merge(IReadOnlyList<Spoken> written, double writtenTo, IEnumerable<TimedWord> live) =>
        [.. written, .. WordLines.Lines(live.Where(w => w.Start >= writtenTo - 0.05))];
}
