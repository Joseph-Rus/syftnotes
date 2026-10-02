using System.Globalization;
using System.Text.RegularExpressions;
using StudyStash.Core.Rich;

namespace StudyStash.Library;

/// <summary>
/// A lecture's notes as the phone app shows them: safe HTML, with each Mermaid flowchart drawn as an SVG the way the
/// app draws it, each SVG drawing cleaned (<see cref="SafeSvg"/>), and each formula as KaTeX-ready markup that reads
/// on its own before KaTeX draws it. Raw HTML in the notes is never passed on, so nothing in them can run.
/// </summary>
public static partial class PhoneNotes
{
    /// <summary>How wide a phone's column is taken to be: a flowchart much wider is turned, when that's narrower.</summary>
    const double Column = 480;

    [GeneratedRegex("(?:<p>)?B(\\d+)(?:</p>)?")]
    private static partial Regex BlockMark();

    public static string Render(string? markdown)
    {
        string[] lines = NoteBlocks.Lines(markdown ?? "");
        var blocks = new List<string>();
        var output = new List<string>();
        int at = 0;
        foreach (var block in NoteBlocks.Find(lines))
        {
            for (; at < block.First; at++) output.Add(lines[at]);
            string indent = lines[block.First][..(lines[block.First].Length - lines[block.First].TrimStart().Length)];
            blocks.Add(Block(block));
            // A mark on a paragraph of its own, where the block was, which the block replaces once the prose is HTML.
            output.Add("");
            output.Add($"{indent}B{blocks.Count - 1}");
            output.Add("");
            at = block.Last + 1;
        }
        for (; at < lines.Length; at++) output.Add(lines[at]);
        string html = Ui.RenderMd(string.Join("\n", output), Formula);
        return BlockMark().Replace(html, m => blocks[int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture)]);
    }

    static string Block(NoteBlock block) => block.Kind switch
    {
        NoteBlockKind.Mermaid when block.Closed && Flowchart(block.Text) is { } svg => $"<figure class=\"diagram\">{svg}</figure>",
        NoteBlockKind.Svg when block.Closed => SafeSvg.Clean(block.Text) is { Svg: { } svg } drawing
            ? $"<figure class=\"diagram\">{svg}{Parts(drawing)}</figure>"
            : $"<p class=\"diagram-problem\"><em>{Ui.Esc(SafeSvg.Clean(block.Text).Problem)}</em></p>",
        NoteBlockKind.Math => Math(block.Text, "div", display: true),
        _ => Code(block.Info, block.Text),
    };

    /// <summary>An illustration's parts under it, folded away: on a phone there's no pointing at them, so each one's name
    /// and what the drawing says of it can be read here.</summary>
    static string Parts(SafeSvgResult drawing) => drawing.Parts.Count == 0 ? ""
        : "<details class=\"parts\"><summary>Parts</summary><dl>"
          + string.Concat(drawing.Parts.Select(p => $"<dt>{Ui.Esc(p.Name)}</dt>" + (p.Note.Length > 0 ? $"<dd>{Ui.Esc(p.Note)}</dd>" : "")))
          + "</dl></details>";

    static string Code(string info, string text)
    {
        string lang = info.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
        string cls = lang.Length > 0 && lang.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '+' or '#')
            ? $" class=\"language-{Ui.Esc(lang)}\"" : "";
        return $"<pre><code{cls}>{Ui.Esc(text)}\n</code></pre>";
    }

    /// <summary>A flowchart drawn as the app draws it, with words taken at an average width; null when it can't be
    /// read or laid out (it's shown as its source instead).</summary>
    static string? Flowchart(string source)
    {
        try
        {
            var chart = StudyStash.Core.Rich.Flowchart.Parse(source);
            return DiagramSvg.Render(DiagramLayout.Fit(chart, (text, size, bold) => text.Length * size * (bold ? 0.6 : 0.56), Column));
        }
        catch (Exception) // a chart that can't be drawn is shown as it was written; it never stops the notes
        {
            return null;
        }
    }

    /// <summary>An inline formula from the prose ($…$, or $$…$$ inside a line).</summary>
    static string Formula(string written)
    {
        bool display = written.StartsWith("$$", StringComparison.Ordinal) && written.Length >= 4;
        string tex = display ? written[2..^2] : written[1..^1];
        return Math(tex.Trim(), "span", display);
    }

    /// <summary>
    /// A formula for KaTeX: its TeX in data-tex (the app draws it with katex.render), and inside, the same formula in
    /// plain characters, so it reads before KaTeX has drawn it (or if it never does).
    /// </summary>
    static string Math(string tex, string tag, bool display) =>
        $"<{tag} class=\"math{(display ? " display" : "")}\" data-tex=\"{Ui.Esc(tex)}\">{Ui.Esc(MathText.Plain(tex))}</{tag}>";
}
