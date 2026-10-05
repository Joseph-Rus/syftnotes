namespace StudyStash.Core;

/// <summary>One word heard, with when it starts and ends (seconds) and how sure the model was of it (0 to 1).</summary>
public sealed record TimedWord(double Start, double End, string Text, double Probability = 1);

/// <summary>A model quick enough to hear the newest few seconds of a lecture again and again (Cactus Whistle): 16 kHz
/// mono, at most 30 seconds a pass, to words with their times in the pass.</summary>
public interface IWordHearer
{
    /// <summary><paramref name="language"/>: a language code, or "" to find it. <paramref name="keywords"/>: words and
    /// names to favour, one a line ("" for none).</summary>
    IReadOnlyList<TimedWord> Hear(float[] samples, string language, string keywords);
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
    public double From(double end) => Math.Max(Next, end - MostSeconds);

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
        // The pass began where the last settled word ended: a word it hears again right there is that same word.
        if (heard.Count > 0 && settled.Count > 0 && heard[0].Start < from + 0.3 && Same(heard[0].Text, settled[^1].Text))
            heard.RemoveAt(0);

        double settleBefore = end - SettleAfter;
        int k = 0;
        while (k < heard.Count && heard[k].End <= settleBefore) k++;
        if (k > 0)
        {
            settled.AddRange(heard.Take(k));
            Next = k < heard.Count ? (heard[k - 1].End + heard[k].Start) / 2 : heard[k - 1].End;
            Next = Math.Clamp(Next, from, end);
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
/// passes together. Only while a lecture records and is written down as it records: a lecture written down after
/// class (<see cref="Lecture.AfterClass"/>) only records, and a paused one has nothing new. These words are only for
/// the recorder to show and to ask about; the transcript the notes are made from is the <see cref="TranscriptionWorker"/>'s,
/// and <see cref="Merge"/> puts its lines first wherever it has got to.
/// </summary>
public sealed class LiveCaptioner(Func<Lecture?> recording, Func<double, RecentSound?> recent, Func<Lecture, (IWordHearer Hearer, string Language)?> hearer,
    Action<string>? log = null)
{
    /// <summary>Less new sound than this since the last pass waits for more.</summary>
    const double LeastNew = 0.5;
    readonly Action<string> log = log ?? (_ => { });
    readonly Lock gate = new();
    CaptionStitcher? stitcher;
    string? lectureId;
    double heardTo;
    bool told;

    /// <summary>How often it hears the newest sound.</summary>
    public TimeSpan Every { get; init; } = TimeSpan.FromSeconds(1);

    /// <summary>The live words changed: the lecture they're for.</summary>
    public event Action<Lecture>? Changed;

    /// <summary>When the first words of the lecture now recording were heard (seconds into the lecture, and the
    /// seconds the pass took), for the log; null until then.</summary>
    public (double At, double Took)? First { get; private set; }

    /// <summary>The live words of lecture <paramref name="id"/> from <paramref name="from"/> seconds on; empty for any other.</summary>
    public IReadOnlyList<TimedWord> Words(string id, double from = 0)
    {
        lock (gate)
            return stitcher is not null && lectureId == id ? [.. stitcher.Words.Where(w => w.Start >= from)] : [];
    }

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
        if (hearer(l) is not { } h) return false;
        var s = stitcher!;
        var sound = recent(s.Next);
        if (sound is null || sound.End - heardTo < LeastNew) return false;
        double from = s.From(sound.End);
        var samples = from > sound.Start ? sound.Samples[(int)Math.Round((from - sound.Start) * Sound.Rate)..] : sound.Samples;
        from = Math.Max(from, sound.Start);
        double end = sound.End;
        heardTo = end;
        if (new Chunk(0, samples).Silent())
        {
            lock (gate) s.Quiet(end);
            return true;
        }
        var took = System.Diagnostics.Stopwatch.StartNew();
        IReadOnlyList<TimedWord> words;
        try
        {
            words = h.Hearer.Hear(samples, h.Language, Keywords(l.ClassName));
        }
        catch (Exception e)
        {
            if (!told) log($"[live] {l.Id}: {e.Message}");
            told = true;
            return false;
        }
        lock (gate)
        {
            s.Heard(from, end, words);
            // What the transcript has written down is its to show: the live words before it aren't needed any more.
            s.Forget(l.TranscribedSeconds - 5);
        }
        if (First is null && words.Count > 0)
        {
            First = (from + words[0].Start, took.Elapsed.TotalSeconds);
            log(string.Create(System.Globalization.CultureInfo.InvariantCulture,
                $"[live] {l.Id}: first words at {from + words[0].Start:0.0} s into the lecture, heard {end - from - words[0].Start:0.0} s later in a {took.Elapsed.TotalMilliseconds:0} ms pass"));
        }
        Changed?.Invoke(l);
        return true;
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
        }
    }

    /// <summary>The class's name, as words to favour: a name or a subject the lecturer says.</summary>
    static string Keywords(string className) => className.Trim().Length > 2 && className.Any(char.IsLetter) ? className.Trim() : "";

    /// <summary>Hear about every <see cref="Every"/> until <paramref name="stop"/>: on its own thread, below the app's
    /// own work, so the recorder and the transcript never wait for it.</summary>
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
            var left = Every - (DateTime.UtcNow - started);
            // Nothing recording: look less often.
            if (recording() is not { State: LectureState.Recording, AfterClass: false }) left = TimeSpan.FromSeconds(2);
            if (left > TimeSpan.Zero) stop.WaitHandle.WaitOne(left);
        }
    }

    /// <summary>
    /// What's been said so far, as lines: the transcript's own (<paramref name="written"/>, the model the notes are made
    /// from) up to where it has got (<paramref name="writtenTo"/> seconds), then the live words after that.
    /// </summary>
    public static List<Spoken> Merge(IReadOnlyList<Spoken> written, double writtenTo, IEnumerable<TimedWord> live) =>
        [.. written, .. WordLines.Lines(live.Where(w => w.Start >= writtenTo - 0.05))];
}
