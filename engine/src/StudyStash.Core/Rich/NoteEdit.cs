using System.Text.RegularExpressions;

namespace StudyStash.Core.Rich;

/// <summary>
/// A lecture's notes as a student edits them by hand: their Markdown, with each diagram, plot and drawing folded to one
/// line of its own ("[Diagram 1]"), so its source is never in the way and a stray key can't break it. The line is the
/// diagram: moved, the diagram moves with it; deleted, the diagram goes. <see cref="End"/> puts each one back where
/// its line is. Everything else (headings, bullets, formulas, code) is the notes' own text, edited as it is.
/// </summary>
public sealed partial class NoteEdit
{
    [GeneratedRegex(@"^\s*\[(?:Diagram|Plot|Drawing) (\d+)\]\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex Held();

    /// <summary>Each folded block's own lines, fences and all, by its line's number.</summary>
    readonly Dictionary<int, string[]> held = [];

    /// <summary>The notes to edit, a line for each diagram.</summary>
    public string Text { get; private init; } = "";

    /// <summary>Whether any diagram is folded to a line (the editor then says what those lines are).</summary>
    public bool HasDiagrams => held.Count > 0;

    static string Word(NoteBlockKind kind) => kind switch
    {
        NoteBlockKind.Plot => "Plot",
        NoteBlockKind.Svg => "Drawing",
        _ => "Diagram",
    };

    public static NoteEdit Begin(string markdown)
    {
        string[] lines = NoteBlocks.Lines(markdown);
        var held = new Dictionary<int, string[]>();
        // A line the notes already have that reads like one of these keeps its number to itself.
        var taken = lines.Select(l => Held().Match(l)).Where(m => m.Success).Select(m => int.TryParse(m.Groups[1].Value, out int n) ? n : 0).ToHashSet();
        var text = new List<string>();
        int at = 0, number = 0;
        foreach (var block in NoteBlocks.Find(lines).Where(b => b.IsDiagram && b.Closed))
        {
            text.AddRange(lines[at..block.First]);
            do number++; while (taken.Contains(number));
            held[number] = lines[block.First..(block.Last + 1)];
            string first = lines[block.First];
            text.Add(first[..(first.Length - first.TrimStart().Length)] + $"[{Word(block.Kind)} {number}]");
            at = block.Last + 1;
        }
        text.AddRange(lines[at..]);
        var edit = new NoteEdit { Text = string.Join("\n", text) };
        foreach (var (n, block) in held) edit.held[n] = block;
        return edit;
    }

    /// <summary>The notes as edited, each diagram back where its line is now.</summary>
    public string End(string text)
    {
        var lines = new List<string>();
        foreach (string line in NoteBlocks.Lines(text))
        {
            if (Held().Match(line) is { Success: true } m && int.TryParse(m.Groups[1].Value, out int n) && held.TryGetValue(n, out var block))
                lines.AddRange(block);
            else lines.Add(line);
        }
        return string.Join("\n", lines);
    }
}
