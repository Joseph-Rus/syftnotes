using System.Text;
using System.Text.RegularExpressions;
using StudyStash.Core.Rich;

namespace StudyStash.Core;

/// <summary>A piece of a lecture search and Ask can point at: a bullet or paragraph of its notes (under a heading),
/// or a stretch of its transcript (with the time it starts).</summary>
public sealed record Passage(string NoteId, string Kind, string Section, double? Start, string Text)
{
    public const string NotesKind = "notes", TranscriptKind = "transcript";
}

/// <summary>A passage that matched, with the lecture it's from.</summary>
public sealed record PassageHit(NoteRow Note, Passage Passage, double Rank);

/// <summary>Cutting a lecture into passages, and turning what someone typed into a full-text query.</summary>
public static partial class Passages
{
    const int Target = 420; // characters: a few sentences, enough to answer from

    [GeneratedRegex(@"^#{1,6}\s+(.+?)\s*#*\s*$")]
    private static partial Regex Heading();

    [GeneratedRegex(@"^\s*(?:[-*+]|\d+[.)])\s+")]
    private static partial Regex ListItem();

    /// <summary>A lecture's notes as passages: each bullet, each paragraph, and each block (code, a formula, a
    /// diagram as its words) one passage, under the heading it sits beneath.</summary>
    public static List<Passage> FromNotes(string noteId, string markdown)
    {
        var result = new List<Passage>();
        string section = "";
        var para = new StringBuilder();
        void Flush()
        {
            string t = para.ToString().Trim();
            para.Clear();
            if (t.Length > 0) result.Add(new Passage(noteId, Passage.NotesKind, section, null, t));
        }
        string[] lines = NoteBlocks.Lines(markdown);
        var blocks = NoteBlocks.Find(lines).ToDictionary(b => b.First);
        for (int k = 0; k < lines.Length; k++)
        {
            if (blocks.TryGetValue(k, out var block))
            {
                Flush();
                foreach (string piece in Pieces(Words(block)))
                    result.Add(new Passage(noteId, Passage.NotesKind, section, null, piece));
                k = block.Last;
                continue;
            }
            string line = lines[k].TrimEnd();
            var h = Heading().Match(line.Trim());
            if (h.Success)
            {
                Flush();
                section = h.Groups[1].Value.Trim();
                continue;
            }
            if (line.Trim().Length == 0)
            {
                Flush();
                continue;
            }
            if (ListItem().IsMatch(line))
            {
                Flush();
                para.Append(ListItem().Replace(line, "", 1).Trim());
                Flush();
                continue;
            }
            if (para.Length > 0) para.Append(' ');
            para.Append(line.Trim());
            if (para.Length > Target * 2) Flush();
        }
        Flush();
        return result;
    }

    static readonly HashSet<string> MermaidWords = new(StringComparer.OrdinalIgnoreCase)
        { "flowchart", "graph", "td", "tb", "bt", "lr", "rl", "subgraph", "end", "class", "classdef", "style", "direction" };

    /// <summary>What search and Ask read of a block: a diagram's words (never its markup), a formula's LaTeX, code
    /// as written.</summary>
    static string Words(NoteBlock block)
    {
        switch (block.Kind)
        {
            case NoteBlockKind.Mermaid:
                try
                {
                    var labels = Flowchart.Parse(block.Text).Labels();
                    return labels.Count == 0 ? "" : DiagramPrefix + string.Join(", ", labels);
                }
                catch (MermaidException)
                {
                    // Not a flowchart that can be drawn: its words still find it.
                    var words = Words(block.Text).Where(w => !MermaidWords.Contains(w) && w.Length > 1).ToList();
                    return words.Count == 0 ? "" : DiagramPrefix + string.Join(" ", words);
                }
            case NoteBlockKind.Svg:
                var drawing = SafeSvg.Clean(block.Text);
                var texts = drawing.Texts.Where(t => t.Trim().Length > 0).ToList();
                if (drawing.Title.Length > 0 && !texts.Contains(drawing.Title)) texts.Insert(0, drawing.Title);
                return texts.Count == 0 ? "" : DiagramPrefix + string.Join(", ", texts);
            case NoteBlockKind.Plot:
                try
                {
                    var words = Plot.Parse(block.Text).Words();
                    return words.Count == 0 ? "" : "Plot: " + string.Join(", ", words);
                }
                catch (PlotException)
                {
                    return block.Text.Trim();
                }
            case NoteBlockKind.Math:
                return block.Text.Trim();
            default:
                return block.Text.TrimEnd().TrimStart('\n'); // code keeps its indentation
        }
    }

    const string DiagramPrefix = "Diagram: ";

    /// <summary>A block's words as passages no longer than a long paragraph's: a long code block by whole lines, a
    /// big diagram's words by whole labels (each piece still saying it's a diagram), so no one passage swamps an
    /// answer's sources.</summary>
    static IEnumerable<string> Pieces(string words)
    {
        const int max = Target * 2;
        if (words.Length == 0) yield break;
        if (words.Length <= max)
        {
            yield return words;
            yield break;
        }
        bool diagram = words.StartsWith(DiagramPrefix, StringComparison.Ordinal);
        string sep = diagram ? ", " : "\n", prefix = diagram ? DiagramPrefix : "";
        var cur = new StringBuilder();
        foreach (string part in diagram ? words[DiagramPrefix.Length..].Split(sep) : words.Split('\n'))
            foreach (string piece in Summarize.Pieces(part, max))
            {
                if (cur.Length > 0 && cur.Length + sep.Length + piece.Length > max)
                {
                    yield return prefix + cur;
                    cur.Clear();
                }
                if (cur.Length > 0) cur.Append(sep);
                cur.Append(piece);
            }
        if (cur.ToString().Trim().Length > 0) yield return prefix + cur;
    }

    /// <summary>A transcript in stretches of about a minute; a timed one's stretches start where a line does.</summary>
    public static List<Passage> FromTranscript(string noteId, string transcript)
    {
        var result = new List<Passage>();
        if (TimedText.HasTimes(transcript))
        {
            var text = new StringBuilder();
            double? start = null;
            foreach (var s in TimedText.Parse(transcript))
            {
                start ??= s.Start;
                if (text.Length > 0) text.Append(' ');
                text.Append(s.Text);
                if (text.Length >= Target || s.End - start >= 75)
                {
                    result.Add(new Passage(noteId, Passage.TranscriptKind, "", start, text.ToString()));
                    text.Clear();
                    start = null;
                }
            }
            if (text.Length > 0) result.Add(new Passage(noteId, Passage.TranscriptKind, "", start, text.ToString()));
            return result;
        }
        string plain = Regex.Replace(transcript, @"\s+", " ").Trim();
        for (int at = 0; at < plain.Length;)
        {
            int end = Math.Min(plain.Length, at + Target);
            if (end < plain.Length)
            {
                int stop = plain.LastIndexOfAny(['.', '?', '!'], end - 1, end - at);
                if (stop > at + Target / 2) end = stop + 1;
            }
            result.Add(new Passage(noteId, Passage.TranscriptKind, "", null, plain[at..end].Trim()));
            at = end;
        }
        return result;
    }

    [GeneratedRegex(@"[\p{L}\p{N}]+")]
    private static partial Regex Word();

    static readonly HashSet<string> Stop = new(StringComparer.OrdinalIgnoreCase)
    {
        "a", "an", "the", "and", "or", "of", "to", "in", "on", "for", "is", "are", "was", "were", "be", "it", "this",
        "that", "what", "which", "who", "how", "why", "when", "where", "did", "do", "does", "she", "he", "they", "we",
        "you", "i", "me", "my", "our", "about", "with", "as", "at", "by", "from", "say", "said", "tell", "there",
    };

    public static List<string> Words(string text) => Word().Matches(text).Select(m => m.Value.ToLowerInvariant()).ToList();

    /// <summary>A search box's words for FTS5: every word must appear, the last may be half typed.</summary>
    public static string? AllWords(string query)
    {
        var words = Words(query);
        if (words.Count == 0) return null;
        return string.Join(" ", words.Select((w, i) => i == words.Count - 1 ? $"\"{w}\"*" : $"\"{w}\""));
    }

    /// <summary>A question's words for FTS5: any of the ones that mean something; the ranking does the rest.</summary>
    public static string? AnyWords(string question)
    {
        var words = Words(question).Where(w => !Stop.Contains(w) && w.Length > 1).Distinct().ToList();
        if (words.Count == 0) words = Words(question).Distinct().ToList();
        return words.Count == 0 ? null : string.Join(" OR ", words.Select(w => $"\"{w}\""));
    }
}
