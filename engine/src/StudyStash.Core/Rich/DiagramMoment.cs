using System.Globalization;
using System.Text.RegularExpressions;

namespace StudyStash.Core.Rich;

/// <summary>A moment of a lecture a diagram's box was said at: the transcript's line and how well it matches.</summary>
public sealed record SaidAt(Spoken Line, double Score);

/// <summary>
/// Where in the lecture a diagram comes from: the moment the diagram pass marked it with ("%% Study Stash diagram, from
/// 12:34"), and the moments a box's words were said, found by matching its words against the transcript's lines (the
/// same lenient matching the diagram pass checks a diagram's words with: each word by its stem, common words left out).
/// </summary>
public static partial class DiagramMoment
{
    [GeneratedRegex(@"Study Stash diagram, from ((?:\d+:)?\d{1,2}:\d{2})\b")]
    private static partial Regex Mark();

    [GeneratedRegex(@"[^\p{L}\p{N}]+")]
    private static partial Regex NotWord();

    /// <summary>The words that say too little to find a moment by.</summary>
    static readonly HashSet<string> Common =
    [
        "with", "from", "that", "this", "then", "into", "onto", "over", "each", "when", "what", "where", "which", "their", "there",
        "they", "them", "more", "less", "than", "back", "also", "step", "steps", "start", "until", "after", "before", "next",
        "the", "and", "for", "are", "but", "not", "you", "all", "any", "can", "has", "have", "its", "was", "were", "will", "your",
        "box", "boxes", "inside", "yes",
    ];

    /// <summary>The moment (seconds into the lecture) a diagram's source says it comes from, or null.</summary>
    public static double? From(string? source)
    {
        if (source is null || Mark().Match(source) is not { Success: true } m) return null;
        var parts = m.Groups[1].Value.Split(':').Select(p => double.Parse(p, CultureInfo.InvariantCulture)).ToArray();
        return parts.Length == 3 ? parts[0] * 3600 + parts[1] * 60 + parts[2] : parts[0] * 60 + parts[1];
    }

    /// <summary>A label's words that can find it: lower case, at least three letters, not a common word, each cut to its
    /// stem (all but its last two letters, keeping at least four).</summary>
    public static IReadOnlyList<string> Stems(string label) =>
        [.. NotWord().Split(label.ToLowerInvariant()).Where(w => w.Length >= 3 && !Common.Contains(w)).Select(w => w.Length > 5 ? w[..Math.Max(4, w.Length - 2)] : w).Distinct()];

    /// <summary>
    /// Where <paramref name="label"/> was said: the transcript's lines (each read with the one after it, since a phrase
    /// can run across two) that hold most of its words, the rarer words counting for more, best first; at most
    /// <paramref name="max"/>, and only lines holding at least half of what the label says. A line near
    /// <paramref name="near"/> (the diagram's own moment) wins a tie. None when the label has no words to find.
    /// </summary>
    public static IReadOnlyList<SaidAt> Said(string label, IReadOnlyList<Spoken> lines, double? near = null, int max = 3)
    {
        var stems = Stems(label);
        if (stems.Count == 0 || lines.Count == 0) return [];
        var text = lines.Select(l => " " + string.Join(' ', NotWord().Split(l.Text.ToLowerInvariant())) + " ").ToList();
        // A word on every other line says little about where this one was said.
        var weight = stems.ToDictionary(s => s, s => 1 / Math.Log(2 + text.Count(t => t.Contains(" " + s, StringComparison.Ordinal))));
        double whole = weight.Values.Sum();
        string phrase = " " + string.Join(' ', NotWord().Split(label.ToLowerInvariant()).Where(w => w.Length > 0)) + " ";
        double Score(string said)
        {
            double score = stems.Where(s => said.Contains(" " + s, StringComparison.Ordinal)).Sum(s => weight[s]) / whole;
            // The words in a row, as the label has them, are surer still.
            return stems.Count > 1 && said.Contains(phrase, StringComparison.Ordinal) ? score + 0.25 : score;
        }
        var found = new List<SaidAt>();
        for (int i = 0; i < lines.Count; i++)
        {
            // A line on its own counts in full; read on into the next one, a little less (that line will be found too).
            double score = Math.Max(Score(text[i]), i + 1 < lines.Count ? 0.9 * Score(text[i] + text[i + 1]) : 0);
            if (score >= 0.5) found.Add(new SaidAt(lines[i], score));
        }
        // A phrase that runs across two lines is found from both: keep the earlier one.
        var best = found.OrderByDescending(f => Math.Round(f.Score, 6)).ThenBy(f => near is double t ? Math.Abs(f.Line.Start - t) : f.Line.Start).ToList();
        var kept = new List<SaidAt>();
        foreach (var f in best)
        {
            if (kept.Any(k => Math.Abs(k.Line.Start - f.Line.Start) < 20)) continue;
            kept.Add(f);
            if (kept.Count == max) break;
        }
        return kept;
    }
}
