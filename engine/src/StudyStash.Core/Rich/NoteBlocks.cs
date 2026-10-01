using System.Text.RegularExpressions;

namespace StudyStash.Core.Rich;

/// <summary>What a block of a note that isn't prose holds.</summary>
public enum NoteBlockKind { Code, Mermaid, Svg, Math }

/// <summary>
/// One block of a note that isn't prose: a fenced block (code, a Mermaid flowchart, an SVG drawing), a <c>$$</c>
/// formula on lines of its own, or an SVG written straight into the Markdown. <see cref="First"/> and
/// <see cref="Last"/> are its first and last line, fences included; <see cref="Text"/> is what's inside.
/// </summary>
public sealed record NoteBlock(NoteBlockKind Kind, string Info, string Text, int First, int Last, bool Closed)
{
    public bool IsDiagram => Kind is NoteBlockKind.Mermaid or NoteBlockKind.Svg;
}

/// <summary>
/// Finds a note's blocks line by line, the way the notes' own reader sees them: so the checks for a model stuck in
/// a loop, the search index and the diagram repair all read a note's code, formulas and diagrams as blocks, never
/// as lines of prose (a flowchart's repeated <c>end</c>, a comment line in code taken as a heading).
/// </summary>
public static partial class NoteBlocks
{
    [GeneratedRegex(@"^(flowchart|graph|sequenceDiagram|stateDiagram(-v2)?)\b", RegexOptions.IgnoreCase)]
    private static partial Regex FlowchartStart();

    /// <summary>Every kind of Mermaid diagram Study Stash draws, as its first word; timeline and mindmap only count
    /// where a diagram is expected (a repair's reply), not on a bare fence, where they'd be ordinary words.</summary>
    [GeneratedRegex(@"^(flowchart|graph|sequenceDiagram|stateDiagram(-v2)?|timeline|mindmap)\b", RegexOptions.IgnoreCase)]
    private static partial Regex DiagramStart();

    /// <summary>Whether a fenced block with this info holds a diagram, and which: ```mermaid (or ```mmd), ```svg,
    /// an ```xml or ```html one holding an SVG, and a bare fence that starts like a flowchart, a sequence or a state
    /// diagram.</summary>
    public static NoteBlockKind KindOf(string info, string text)
    {
        info = info.Trim().ToLowerInvariant();
        return info switch
        {
            "mermaid" or "mmd" => NoteBlockKind.Mermaid,
            "svg" => NoteBlockKind.Svg,
            "xml" or "html" when IsSvg(text) => NoteBlockKind.Svg,
            "" when FlowchartStart().IsMatch(text.TrimStart()) => NoteBlockKind.Mermaid,
            _ => NoteBlockKind.Code,
        };
    }

    /// <summary>Whether a text is an SVG drawing (after an optional XML declaration).</summary>
    public static bool IsSvg(string text)
    {
        text = text.TrimStart();
        if (text.StartsWith("<?xml", StringComparison.OrdinalIgnoreCase) && text.IndexOf("?>", StringComparison.Ordinal) is int end and >= 0)
            text = text[(end + 2)..].TrimStart();
        return text.StartsWith("<svg", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Whether a text starts like a flowchart (not another kind of Mermaid diagram), or says no kind at all.</summary>
    public static bool StartsAsFlowchart(string text) =>
        !DiagramStart().IsMatch(text.TrimStart()) || text.TrimStart().StartsWith("flowchart", StringComparison.OrdinalIgnoreCase) || text.TrimStart().StartsWith("graph", StringComparison.OrdinalIgnoreCase);

    /// <summary>Whether a text starts like a diagram of its own: any kind of Mermaid diagram Study Stash draws, or an SVG.</summary>
    public static bool StartsAsDiagram(string text) => DiagramStart().IsMatch(text.TrimStart()) || IsSvg(text);

    /// <summary>A fence line: its character (` or ~), how many, and the info after them; null when it isn't one.</summary>
    public static (char Char, int Count, string Info)? Fence(string line)
    {
        string t = line.TrimStart();
        if (t.Length < 3 || (t[0] != '`' && t[0] != '~')) return null;
        char c = t[0];
        int n = 0;
        while (n < t.Length && t[n] == c) n++;
        if (n < 3) return null;
        string info = t[n..].Trim();
        if (c == '`' && info.Contains('`')) return null; // an inline code span, not a fence
        return (c, n, info);
    }

    /// <summary>The note's blocks, in order. A fence still open at the end runs to the last line (not
    /// <see cref="NoteBlock.Closed"/>).</summary>
    public static List<NoteBlock> Find(IReadOnlyList<string> lines)
    {
        var blocks = new List<NoteBlock>();
        int i = 0;
        while (i < lines.Count)
        {
            string t = lines[i].Trim();
            if (Fence(lines[i]) is { } open)
            {
                int j = i + 1;
                while (j < lines.Count && !(Fence(lines[j]) is { } close && close.Char == open.Char && close.Count >= open.Count && close.Info.Length == 0)) j++;
                bool closed = j < lines.Count;
                string text = string.Join("\n", Inner(lines, i + 1, closed ? j : lines.Count, Indent(lines[i])));
                blocks.Add(new NoteBlock(KindOf(open.Info, text), open.Info, text, i, closed ? j : lines.Count - 1, closed));
                i = closed ? j + 1 : lines.Count;
                continue;
            }
            if (t.StartsWith("$$", StringComparison.Ordinal))
            {
                if (t.Length >= 4 && t.EndsWith("$$", StringComparison.Ordinal))
                {
                    blocks.Add(new NoteBlock(NoteBlockKind.Math, "", t[2..^2].Trim(), i, i, true));
                    i++;
                    continue;
                }
                int j = i + 1;
                while (j < lines.Count && !lines[j].TrimEnd().EndsWith("$$", StringComparison.Ordinal)) j++;
                bool closed = j < lines.Count;
                var body = new List<string> { t[2..] };
                for (int k = i + 1; k < Math.Min(j + 1, lines.Count); k++) body.Add(lines[k].Trim());
                if (closed) body[^1] = body[^1][..^2];
                blocks.Add(new NoteBlock(NoteBlockKind.Math, "", string.Join("\n", body).Trim(), i, closed ? j : lines.Count - 1, closed));
                i = closed ? j + 1 : lines.Count;
                continue;
            }
            if (t.StartsWith("<svg", StringComparison.OrdinalIgnoreCase))
            {
                int j = i;
                while (j < lines.Count && !lines[j].Contains("</svg>", StringComparison.OrdinalIgnoreCase)) j++;
                bool closed = j < lines.Count;
                int last = closed ? j : lines.Count - 1;
                string text = string.Join("\n", Inner(lines, i, last + 1, 0)).Trim();
                blocks.Add(new NoteBlock(NoteBlockKind.Svg, "", text, i, last, closed));
                i = last + 1;
                continue;
            }
            i++;
        }
        return blocks;
    }

    /// <summary>The note's blocks, from its Markdown.</summary>
    public static List<NoteBlock> Find(string markdown) => Find(Lines(markdown));

    /// <summary>A note's lines, however its line endings were written.</summary>
    public static string[] Lines(string markdown) => (markdown ?? "").ReplaceLineEndings("\n").Split('\n');

    static int Indent(string line) => line.Length - line.TrimStart().Length;

    /// <summary>A block's lines with the indentation its opening fence had taken off (a block inside a bullet).</summary>
    static IEnumerable<string> Inner(IReadOnlyList<string> lines, int from, int to, int indent)
    {
        for (int k = from; k < to; k++)
        {
            string l = lines[k];
            int cut = 0;
            while (cut < indent && cut < l.Length && l[cut] == ' ') cut++;
            yield return l[cut..];
        }
    }
}
