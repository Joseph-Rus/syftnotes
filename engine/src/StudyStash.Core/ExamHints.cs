using System.Text.RegularExpressions;

namespace StudyStash.Core;

/// <summary>A moment in a lecture where the lecturer said something is (or isn't) on the exam: when (null for a
/// transcript without times), the sentence they said it in, and whether it was a "won't be on the exam".</summary>
public sealed record ExamHint(double? At, string Said, bool Not = false)
{
    /// <summary>"18:05", or "" without a time.</summary>
    public string Moment => At is { } s ? TimedText.Clock(s) : "";
}

/// <summary>
/// The moments a lecturer tells the room what to study: "this will be on the exam", "make sure you know", "write this
/// down", "I'll ask you this on the midterm", and the other way, "this won't be on the test". Found in the transcript
/// by what was said, on this computer, with no AI: a lecture's page lists them over its notes, and exam prep puts them
/// first.
/// </summary>
public static partial class ExamHints
{
    const string Exam = @"(?:exam|exams|test|tests|midterm|midterms|mid-term|final|finals|quiz|quizzes|assessment)";

    // A sentence that flags what's on the exam.
    [GeneratedRegex(
        $@"\b(?:on|in|for)\s+(?:the|your|an|a|our|this|next|every)\s+(?:\w+\s+)?{Exam}\b" +
        $@"|\b{Exam}\s+(?:question|questions|material|problem|problems)\b" +
        $@"|\b(?:will|would|could|might|may|going to|gonna)\s+(?:definitely\s+|probably\s+|likely\s+|certainly\s+|almost certainly\s+)?(?:be\s+)?(?:show|shows|showing|come|comes|turn)\s+up\b" +
        @"|\b(?:will|would|going to|gonna)\s+(?:definitely\s+|probably\s+)?be\s+(?:tested|examined|asked)\b" +
        @"|\b(?:you(?:'ll| will)?|you're going to|you are going to)\s+(?:definitely\s+|really\s+|absolutely\s+)?(?:need|have|want)\s+to\s+(?:know|remember|understand|memori[sz]e|be able to)\b" +
        @"|\bmake sure (?:that )?you (?:know|understand|remember|can|memori[sz]e|study|review|are comfortable)\b" +
        @"|\b(?:write|jot|copy|note) (?:this|that) down\b" +
        @"|\b(?:i|we)(?:'ll| will| am going to|'m going to| might| may| could| would)\s+(?:definitely\s+|probably\s+)?(?:ask|test|quiz) you\b" +
        @"|\b(?:i|we) (?:guarantee|promise)\b" +
        @"|\b(?:this|that|it) is (?:really |very |super |extremely )?(?:important|key|crucial|essential) (?:to know|to remember|for you)\b" +
        @"|\bknow this (?:cold|well|by heart|for)\b" +
        @"|\bfair game\b" +
        @"|\b(?:study|review) (?:this|that|these)\b" +
        @"|\bpay (?:close |special |particular )?attention to\b",
        RegexOptions.IgnoreCase)]
    private static partial Regex Flag();

    // The same, said the other way: it isn't on the exam.
    [GeneratedRegex(
        $@"\b(?:won't|will not|wouldn't|isn't|is not|aren't|are not|not going to|never)\s+(?:\w+\s+){{0,3}}?(?:on|in|for)\s+(?:the|your|an|a|our|any)\s+(?:\w+\s+)?{Exam}\b" +
        $@"|\bnot\s+(?:on|in)\s+(?:the|your|an|a|any)\s+(?:\w+\s+)?{Exam}\b" +
        $@"|\bnone of (?:this|that|these|it|them)\s+(?:\w+\s+){{0,4}}?(?:on|in)\s+(?:the|your|an|a|any)\s+(?:\w+\s+)?{Exam}\b" +
        @"|\b(?:won't|will not|wouldn't|not going to)\s+(?:be\s+)?(?:tested|examined|ask you|test you|quiz you)\b" +
        @"|\byou (?:don't|do not) (?:need|have) to (?:know|memori[sz]e|remember)\b" +
        @"|\bdon't (?:worry|stress) (?:too much )?about (?:memori[sz]ing|remembering)\b",
        RegexOptions.IgnoreCase)]
    private static partial Regex NotFlag();

    // "It'll be on it": on the exam only when the exam was just named.
    [GeneratedRegex(@"\b(?:will|'ll|is going to|are going to|gonna)\s+(?:definitely\s+|probably\s+|likely\s+|certainly\s+)?be\s+on\s+(?:it|there|that)\b", RegexOptions.IgnoreCase)]
    private static partial Regex OnIt();

    [GeneratedRegex($@"\b{Exam}\b", RegexOptions.IgnoreCase)]
    private static partial Regex NamesExam();

    [GeneratedRegex(@"(?<=[.!?])\s+(?=[\p{Lu}\d""'(])")]
    private static partial Regex SentenceBreak();

    /// <summary>The flagged moments of a transcript, in the order they were said. A timed transcript ("[02:30] …")
    /// gives each its moment; one without times gives none.</summary>
    public static List<ExamHint> Find(string transcript)
    {
        if (string.IsNullOrWhiteSpace(transcript)) return [];
        if (TimedText.HasTimes(transcript)) return Find(TimedText.Parse(transcript));
        return Find(transcript.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => ((double?)null, l)));
    }

    /// <summary>The flagged moments of timed lines.</summary>
    public static List<ExamHint> Find(IEnumerable<Spoken> lines) => Find(lines.Select(l => ((double?)l.Start, l.Text)));

    static List<ExamHint> Find(IEnumerable<(double? At, string Text)> lines)
    {
        var found = new List<ExamHint>();
        string before = "";
        foreach (var (at, text) in lines)
            foreach (string raw in SentenceBreak().Split(text))
            {
                string sentence = Regex.Replace(raw, @"\s+", " ").Trim();
                bool examNamed = NamesExam().IsMatch(before);
                before = sentence;
                if (sentence.Length < 8) continue;
                // A "won't be on the exam" is that, even though it names the exam.
                bool not = NotFlag().IsMatch(sentence);
                bool onIt = OnIt().IsMatch(sentence) && (examNamed || NamesExam().IsMatch(sentence));
                if (!not && !onIt && !Flag().IsMatch(sentence)) continue;
                var hint = new ExamHint(at, Shortened(sentence), not);
                // The same sentence said twice in a row (a caption repeated) is one moment.
                if (found.Count > 0 && found[^1].Said == hint.Said) continue;
                found.Add(hint);
            }
        return found;
    }

    /// <summary>A sentence that runs on (a transcript without full stops) is cut to about 40 words round the cue.</summary>
    static string Shortened(string sentence)
    {
        var words = sentence.Split(' ');
        if (words.Length <= 45) return sentence;
        var cue = NotFlag().Match(sentence) is { Success: true } n ? n : Flag().Match(sentence);
        int cueWord = sentence[..cue.Index].Count(c => c == ' ');
        int from = Math.Max(0, Math.Min(cueWord - 20, words.Length - 40));
        int to = Math.Min(words.Length, from + 40);
        return (from > 0 ? "… " : "") + string.Join(' ', words[from..to]) + (to < words.Length ? " …" : "");
    }
}
