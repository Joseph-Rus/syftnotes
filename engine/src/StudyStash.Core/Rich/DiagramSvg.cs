using System.Globalization;
using System.Text;

namespace StudyStash.Core.Rich;

/// <summary>The colours a diagram is drawn in outside the app: hex strings, a tone's stroke and fill.</summary>
public sealed record DiagramPalette(
    string Paper, string Ink, string Line, string Words, string NodeFill, string NodeStroke, string GroupFill, string GroupTitle,
    IReadOnlyDictionary<Tone, (string Stroke, string Fill)> Tones)
{
    /// <summary>
    /// The light palette, the one exported files use: the same hexes the notes' writers are given for SVG diagrams
    /// (ink, secondary, light lines, paper, and the red, blue, green, amber and purple pairs), and the default colour
    /// theme's teal for the accent.
    /// </summary>
    public static DiagramPalette Light { get; } = new(
        "#FFFFFF", "#1D1D1F", "#6E6E73", "#6E6E73", "#FFFFFF", "#C7C7CC", "#F2F2F7", "#6E6E73",
        new Dictionary<Tone, (string, string)>
        {
            [Tone.Red] = ("#D93025", "#FCE8E6"),
            [Tone.Blue] = ("#1A73E8", "#E8F0FE"),
            [Tone.Green] = ("#188038", "#E6F4EA"),
            [Tone.Amber] = ("#E37400", "#FEF7E0"),
            [Tone.Purple] = ("#8E24AA", "#F3E8FD"),
            [Tone.Accent] = (ClassColors.Hex((0.58, 0.12, 195)), ClassColors.Hex((0.95, 0.035, 195))),
        });
}

/// <summary>
/// A laid-out diagram as a standalone SVG file, drawn the way the app draws it (the same outlines, markers and
/// spacing), on its own paper so it reads on a dark page too. Every piece of text is escaped.
/// </summary>
public static class DiagramSvg
{
    public const string FontFamily = "\"Helvetica Neue\", \"Segoe UI\", Arial, sans-serif";

    public static string Render(DiagramScene scene, DiagramPalette? palette = null, string? title = null)
    {
        var p = palette ?? DiagramPalette.Light;
        var sb = new StringBuilder();
        string W = N(scene.Width), H = N(scene.Height);
        sb.Append($"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 {W} {H}\" width=\"{W}\" height=\"{H}\" font-family=\"{Esc(FontFamily)}\" role=\"img\">\n");
        title ??= string.Join(", ", scene.Nodes.Select(n => string.Join(" ", n.Lines)).Where(t => t.Length > 0));
        if (title.Length > 0) sb.Append("  <title>").Append(Esc(title)).Append("</title>\n");
        sb.Append($"  <rect width=\"{W}\" height=\"{H}\" fill=\"{p.Paper}\"/>\n");
        foreach (var g in scene.Groups)
        {
            sb.Append($"  <rect x=\"{N(g.Box.X)}\" y=\"{N(g.Box.Y)}\" width=\"{N(g.Box.W)}\" height=\"{N(g.Box.H)}\" rx=\"12\" fill=\"{p.GroupFill}\"/>\n");
            sb.Append($"  <text x=\"{N(g.TitleAt.X)}\" y=\"{N(g.TitleAt.Y + 8 + DiagramLayout.TitleSize * 0.35)}\" font-size=\"12\" font-weight=\"600\" fill=\"{p.GroupTitle}\">{Esc(g.Title)}</text>\n");
        }
        foreach (var e in scene.Edges)
        {
            sb.Append($"  <path d=\"{Path(e.Path)}\" fill=\"none\" stroke=\"{p.Line}\" stroke-width=\"{N(SceneShapes.Thickness(e.Line))}\" stroke-linecap=\"round\" stroke-linejoin=\"round\"");
            if (e.Line == EdgeLine.Dotted) sb.Append($" stroke-dasharray=\"{N(SceneShapes.Dashes[0])} {N(SceneShapes.Dashes[1])}\"");
            sb.Append("/>\n");
            Marker(sb, e.EndEnd, e.Tip, e.Base, p.Line);
            Marker(sb, e.StartEnd, e.StartTip, e.StartBase, p.Line);
        }
        foreach (var e in scene.Edges)
        {
            if (e.LabelLines.Count == 0) continue;
            var b = e.LabelBox;
            sb.Append($"  <rect x=\"{N(b.X)}\" y=\"{N(b.Y)}\" width=\"{N(b.W)}\" height=\"{N(b.H)}\" rx=\"3\" fill=\"{p.Paper}\"/>\n");
            double top = b.Center.Y - e.LabelLines.Count * DiagramLayout.LabelLineHeight / 2;
            for (int i = 0; i < e.LabelLines.Count; i++)
                sb.Append($"  <text x=\"{N(b.Center.X)}\" y=\"{N(top + (i + 0.5) * DiagramLayout.LabelLineHeight + DiagramLayout.LabelSize * 0.35)}\" font-size=\"12\" text-anchor=\"middle\" fill=\"{p.Words}\">{Esc(e.LabelLines[i])}</text>\n");
        }
        foreach (var n in scene.Nodes)
        {
            var (stroke, fill) = p.Tones.TryGetValue(n.Tone, out var tone) ? tone : (p.NodeStroke, p.NodeFill);
            string paint = $" fill=\"{fill}\" stroke=\"{stroke}\" stroke-width=\"1\"";
            var b = n.Box;
            if (SceneShapes.Corners(n) is { } corners)
                sb.Append($"  <polygon points=\"{string.Join(" ", corners.Select(c => N(c.X) + "," + N(c.Y)))}\"{paint}/>\n");
            else if (n.Shape == NodeShape.Circle)
                sb.Append($"  <circle cx=\"{N(b.Center.X)}\" cy=\"{N(b.Center.Y)}\" r=\"{N(b.W / 2)}\"{paint}/>\n");
            else if (n.Shape == NodeShape.Cylinder)
            {
                double cap = SceneShapes.CylinderCap, rx = b.W / 2;
                sb.Append($"  <path d=\"M{N(b.X)},{N(b.Y + cap)} L{N(b.X)},{N(b.Bottom - cap)} A{N(rx)},{N(cap)} 0 0 0 {N(b.Right)},{N(b.Bottom - cap)} L{N(b.Right)},{N(b.Y + cap)} A{N(rx)},{N(cap)} 0 0 0 {N(b.X)},{N(b.Y + cap)} Z\"{paint}/>\n");
                sb.Append($"  <path d=\"M{N(b.X)},{N(b.Y + cap)} A{N(rx)},{N(cap)} 0 0 0 {N(b.Right)},{N(b.Y + cap)}\" fill=\"none\" stroke=\"{stroke}\" stroke-width=\"1\"/>\n");
            }
            else
            {
                double r = SceneShapes.Radius(n);
                sb.Append($"  <rect x=\"{N(b.X)}\" y=\"{N(b.Y)}\" width=\"{N(b.W)}\" height=\"{N(b.H)}\" rx=\"{N(r)}\"{paint}/>\n");
                if (n.Shape == NodeShape.Subroutine)
                    sb.Append($"  <path d=\"M{N(b.X + 8)},{N(b.Y)} L{N(b.X + 8)},{N(b.Bottom)} M{N(b.Right - 8)},{N(b.Y)} L{N(b.Right - 8)},{N(b.Bottom)}\" stroke=\"{stroke}\" stroke-width=\"1\"/>\n");
            }
            double top = SceneShapes.TextTop(n);
            for (int i = 0; i < n.Lines.Count; i++)
                sb.Append($"  <text x=\"{N(b.Center.X)}\" y=\"{N(top + (i + 0.5) * DiagramLayout.LineHeight + DiagramLayout.TextSize * 0.35)}\" font-size=\"13\" font-weight=\"500\" text-anchor=\"middle\" fill=\"{p.Ink}\">{Esc(n.Lines[i])}</text>\n");
        }
        sb.Append("</svg>\n");
        return sb.ToString();
    }

    static void Marker(StringBuilder sb, EdgeEnd end, Pt tip, Pt @base, string colour)
    {
        if (end == EdgeEnd.None || Pt.Distance(tip, @base) < 0.01) return;
        switch (end)
        {
            case EdgeEnd.Arrow:
                var head = SceneShapes.ArrowHead(tip, @base);
                sb.Append($"  <polygon points=\"{string.Join(" ", head.Select(c => N(c.X) + "," + N(c.Y)))}\" fill=\"{colour}\"/>\n");
                break;
            case EdgeEnd.Circle:
                var dot = SceneShapes.Dot(tip, @base);
                sb.Append($"  <circle cx=\"{N(dot.X)}\" cy=\"{N(dot.Y)}\" r=\"{N(SceneShapes.DotRadius)}\" fill=\"{colour}\"/>\n");
                break;
            case EdgeEnd.Cross:
                var (a, b, c, d) = SceneShapes.Cross(tip, @base);
                sb.Append($"  <path d=\"M{N(a.X)},{N(a.Y)} L{N(b.X)},{N(b.Y)} M{N(c.X)},{N(c.Y)} L{N(d.X)},{N(d.Y)}\" stroke=\"{colour}\" stroke-width=\"1.5\" stroke-linecap=\"round\"/>\n");
                break;
        }
    }

    static string Path(IReadOnlyList<PathStep> steps)
    {
        var sb = new StringBuilder();
        foreach (var s in steps)
        {
            if (sb.Length > 0) sb.Append(' ');
            sb.Append(s.Verb switch
            {
                PathVerb.Move => $"M{N(s.A.X)},{N(s.A.Y)}",
                PathVerb.Line => $"L{N(s.A.X)},{N(s.A.Y)}",
                _ => $"C{N(s.A.X)},{N(s.A.Y)} {N(s.B.X)},{N(s.B.Y)} {N(s.C.X)},{N(s.C.Y)}",
            });
        }
        return sb.ToString();
    }

    static string N(double v) => Math.Round(v, 2).ToString("0.##", CultureInfo.InvariantCulture);

    static string Esc(string s) => s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;").Replace("'", "&#39;");
}
