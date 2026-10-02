using System.Globalization;
using System.Text;

namespace StudyStash.Core.Rich;

/// <summary>A colour as red, green, blue and opacity bytes.</summary>
public readonly record struct PlotRgb(byte R, byte G, byte B, byte A = 255)
{
    public string Hex => $"#{R:X2}{G:X2}{B:X2}";

    /// <summary>A colour from OKLCH (lightness 0–1, chroma, hue in degrees), clipped into sRGB.</summary>
    public static PlotRgb Oklch(double l, double c, double h)
    {
        double a = c * Math.Cos(h * Math.PI / 180), b = c * Math.Sin(h * Math.PI / 180);
        double l_ = l + 0.3963377774 * a + 0.2158037573 * b, m_ = l - 0.1055613458 * a - 0.0638541728 * b, s_ = l - 0.0894841775 * a - 1.2914855480 * b;
        double L = l_ * l_ * l_, M = m_ * m_ * m_, S = s_ * s_ * s_;
        double r = 4.0767416621 * L - 3.3077115913 * M + 0.2309699292 * S;
        double g = -1.2684380046 * L + 2.6097574011 * M - 0.3413193965 * S;
        double bb = -0.0041960863 * L - 0.7034186147 * M + 1.7076147010 * S;
        static byte F(double x)
        {
            x = Math.Clamp(x, 0, 1);
            return (byte)Math.Round(255 * (x <= 0.0031308 ? 12.92 * x : 1.055 * Math.Pow(x, 1 / 2.4) - 0.055));
        }
        return new PlotRgb(F(r), F(g), F(bb));
    }
}

/// <summary>
/// The colours a plot is drawn in. The series colours are one fixed order (blue, orange, green, purple, pink), chosen
/// in OKLCH and checked for colour-blind separation and contrast against the paper, light and dark each their own;
/// a heat map is one hue, pale to deep for low to high (on dark paper, dark to bright).
/// </summary>
public sealed record PlotPalette(bool Dark, PlotRgb[] Series, PlotRgb Red, PlotRgb Ink, PlotRgb Ink2, PlotRgb Ink3, PlotRgb Grid, PlotRgb Axis, PlotRgb Surface, PlotRgb HeatLine)
{
    /// <summary>The series colours on light paper and on dark.</summary>
    public static readonly PlotRgb[] LightSeries =
    [
        new(0x28, 0x77, 0xD3), new(0xE3, 0x69, 0x27), new(0x00, 0xA0, 0x71), new(0x6D, 0x47, 0xB8), new(0xD1, 0x52, 0x8B),
    ];

    public static readonly PlotRgb[] DarkSeries =
    [
        new(0x4C, 0x94, 0xEC), new(0xE0, 0x72, 0x27), new(0x35, 0xAA, 0x76), new(0x8F, 0x6C, 0xE0), new(0xDF, 0x5E, 0x97),
    ];

    public static PlotPalette Light { get; } = new(false, LightSeries, PlotRgb.Oklch(0.56, 0.17, 27),
        new(0x1D, 0x1D, 0x1F), new(0x5E, 0x5E, 0x63), new(0x86, 0x86, 0x8B), new(0xEC, 0xEC, 0xEF), new(0xB4, 0xB4, 0xBA), new(0xFF, 0xFF, 0xFF), new(0x1D, 0x1D, 0x1F, 0x50));

    public static PlotPalette DarkPaper { get; } = new(true, DarkSeries, PlotRgb.Oklch(0.66, 0.17, 27),
        new(0xF5, 0xF5, 0xF7), new(0xB8, 0xB8, 0xBE), new(0x8E, 0x8E, 0x94), new(0x33, 0x33, 0x37), new(0x5C, 0x5C, 0x62), new(0x1E, 0x1E, 0x20), new(0xFF, 0xFF, 0xFF, 0x48));

    /// <summary>A heat map's shade <paramref name="t"/> (0 low, 1 high) as a colour.</summary>
    public PlotRgb Ramp(double t) => HeatRamp(t, Dark);

    public static PlotRgb HeatRamp(double t, bool dark)
    {
        t = Math.Clamp(t, 0, 1);
        return dark
            ? PlotRgb.Oklch(0.24 + 0.62 * t, 0.035 + 0.075 * t, 258 - 22 * t)
            : PlotRgb.Oklch(0.975 - 0.56 * t, 0.018 + 0.122 * t, 245 + 18 * t);
    }

    public PlotRgb Of(PlotPaint paint) => paint.Role switch
    {
        PlotRole.Series => paint.Index is >= 0 and < 5 ? Series[paint.Index] : Red,
        PlotRole.Ink => Ink,
        PlotRole.Ink2 => Ink2,
        PlotRole.Ink3 => Ink3,
        PlotRole.Grid => Grid,
        PlotRole.Axis => Axis,
        PlotRole.Surface => Surface,
        _ => HeatLine,
    };
}

/// <summary>
/// A laid-out plot as a standalone SVG: what's saved beside a downloaded note, and what the phone shows. Light, on its
/// own paper (so it reads on a dark page too), at the sliders' starting values; every piece of text escaped.
/// </summary>
public static class PlotSvg
{
    public const string FontFamily = DiagramSvg.FontFamily;

    /// <summary>The plot in <paramref name="source"/> drawn at <paramref name="width"/>, its words measured at an
    /// average width (no font is to hand outside the app); null when it can't be read.</summary>
    public static string? Render(string source, double width = 640, bool title = true)
    {
        try
        {
            var plot = Plot.Parse(source);
            var state = new PlotState(plot);
            var scene = PlotLayout.Build(state, width, (text, size, bold) => text.Length * size * (bold ? 0.6 : 0.55), new PlotLook { Title = title });
            return Render(scene, plot.Title ?? "Plot");
        }
        catch (PlotException)
        {
            return null;
        }
    }

    public static string Render(PlotScene scene, string title, PlotPalette? palette = null)
    {
        var p = palette ?? PlotPalette.Light;
        var sb = new StringBuilder();
        string W = N(scene.Width), H = N(scene.Height);
        sb.Append($"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 {W} {H}\" width=\"{W}\" height=\"{H}\" font-family=\"{Esc(FontFamily)}\" role=\"img\">\n");
        sb.Append("  <title>").Append(Esc(title)).Append("</title>\n");
        sb.Append($"  <rect width=\"{W}\" height=\"{H}\" fill=\"{p.Surface.Hex}\"/>\n");
        var a = scene.Area;
        sb.Append($"  <defs><clipPath id=\"plot-area\"><rect x=\"{N(a.X)}\" y=\"{N(a.Y)}\" width=\"{N(a.W)}\" height=\"{N(a.H)}\"/></clipPath></defs>\n");
        sb.Append("  <g clip-path=\"url(#plot-area)\">\n");
        foreach (var m in scene.Data) Mark(sb, m, p, "    ");
        sb.Append("  </g>\n");
        foreach (var m in scene.Front) Mark(sb, m, p, "  ");
        foreach (var k in scene.Keys) Key(sb, k, p);
        sb.Append("</svg>\n");
        return sb.ToString();
    }

    static void Mark(StringBuilder sb, PlotMark m, PlotPalette p, string indent)
    {
        switch (m)
        {
            case PlotHeatMap h:
                Heat(sb, h, p, indent);
                break;
            case PlotLines l:
                if (l.Lines.Count == 0) break;
                sb.Append(indent).Append($"<path d=\"{string.Concat(l.Lines.Where(x => x.Count >= 2).Select(Path))}\" fill=\"none\" stroke=\"{Colour(p.Of(l.Paint))}\"")
                    .Append($" stroke-width=\"{N(l.Width)}\" stroke-linecap=\"round\" stroke-linejoin=\"round\"{Opacity("stroke-opacity", l.Opacity, p.Of(l.Paint))}{Dash(l.Dash, l.Width)}/>\n");
                break;
            case PlotArea ar:
                sb.Append(indent).Append($"<path d=\"{string.Concat(ar.Polygons.Where(x => x.Count >= 3).Select(x => Path(x) + "Z"))}\" fill=\"{Colour(p.Of(ar.Paint))}\"{Opacity("fill-opacity", ar.Opacity, p.Of(ar.Paint))}/>\n");
                break;
            case PlotDot d:
                var c = Colour(p.Of(d.Paint));
                if (d.Hollow) sb.Append(indent).Append($"<circle cx=\"{N(d.At.X)}\" cy=\"{N(d.At.Y)}\" r=\"{N(d.Radius)}\" fill=\"{p.Surface.Hex}\" stroke=\"{c}\" stroke-width=\"2\"/>\n");
                else sb.Append(indent).Append($"<circle cx=\"{N(d.At.X)}\" cy=\"{N(d.At.Y)}\" r=\"{N(d.Radius)}\" fill=\"{c}\" stroke=\"{p.Surface.Hex}\" stroke-width=\"2\" paint-order=\"stroke\"/>\n");
                break;
            case PlotArrow ar2:
                var (shaft, head) = ArrowGeometry(ar2.From, ar2.To, ar2.Width);
                string col = Colour(p.Of(ar2.Paint));
                sb.Append(indent).Append($"<path d=\"M{N(ar2.From.X)},{N(ar2.From.Y)}L{N(shaft.X)},{N(shaft.Y)}\" stroke=\"{col}\" stroke-width=\"{N(ar2.Width)}\" stroke-linecap=\"round\"{Opacity("stroke-opacity", 1, p.Of(ar2.Paint))}/>\n");
                sb.Append(indent).Append($"<path d=\"{Path(head)}Z\" fill=\"{col}\"{Opacity("fill-opacity", 1, p.Of(ar2.Paint))}/>\n");
                break;
            case PlotBar b:
                sb.Append(indent).Append($"<path d=\"{BarPath(b)}\" fill=\"{Colour(p.Of(b.Paint))}\"/>\n");
                break;
            case PlotWords w:
                if (w.Backed) sb.Append(indent).Append($"<rect x=\"{N(w.Box.X - 3)}\" y=\"{N(w.Box.Y)}\" width=\"{N(w.Box.W + 6)}\" height=\"{N(w.Box.H)}\" rx=\"3\" fill=\"{p.Surface.Hex}\" fill-opacity=\"0.88\"/>\n");
                sb.Append(indent).Append($"<text x=\"{N(w.Box.X)}\" y=\"{N(w.Box.Y + w.Box.H / 2 + w.Size * 0.35)}\" font-size=\"{N(w.Size)}\"{(w.Bold ? " font-weight=\"600\"" : "")} fill=\"{Colour(p.Of(w.Paint))}\">{Esc(w.Text)}</text>\n");
                break;
        }
    }

    static void Key(StringBuilder sb, PlotKey k, PlotPalette p)
    {
        double cy = k.Box.Y + k.Box.H / 2, x = k.Box.X;
        string c = Colour(p.Of(k.Paint));
        switch (k.Shape)
        {
            case PlotKeyShape.Line:
                sb.Append($"  <path d=\"M{N(x + 1)},{N(cy)}L{N(x + 17)},{N(cy)}\" stroke=\"{c}\" stroke-width=\"2.5\" stroke-linecap=\"round\"{Dash(k.Dash, 2.5)}/>\n");
                break;
            case PlotKeyShape.Bar:
                sb.Append($"  <rect x=\"{N(x + 3)}\" y=\"{N(cy - 6)}\" width=\"12\" height=\"12\" rx=\"2\" fill=\"{c}\"/>\n");
                break;
            case PlotKeyShape.Dot:
                sb.Append($"  <circle cx=\"{N(x + 9)}\" cy=\"{N(cy)}\" r=\"4.5\" fill=\"{c}\"/>\n");
                break;
            case PlotKeyShape.Ramp:
                for (int i = 0; i < 6; i++)
                    sb.Append($"  <rect x=\"{N(x + 1 + i * 3)}\" y=\"{N(cy - 5)}\" width=\"3\" height=\"10\" fill=\"{p.Ramp(i / 5.0).Hex}\"/>\n");
                break;
        }
    }

    /// <summary>A heat map as runs of cells of one shade (the shades rounded to 32 steps), so it stays a small file.</summary>
    static void Heat(StringBuilder sb, PlotHeatMap h, PlotPalette p, string indent)
    {
        double cw = h.Box.W / h.Columns, ch = h.Box.H / h.Rows;
        for (int j = 0; j < h.Rows; j++)
        {
            int i = 0;
            while (i < h.Columns)
            {
                float v = h.Shade[j * h.Columns + i];
                int q = float.IsNaN(v) ? -1 : (int)Math.Round(v * 31);
                int run = i + 1;
                while (run < h.Columns)
                {
                    float u = h.Shade[j * h.Columns + run];
                    int qu = float.IsNaN(u) ? -1 : (int)Math.Round(u * 31);
                    if (qu != q) break;
                    run++;
                }
                if (q >= 0)
                    sb.Append(indent).Append($"<rect x=\"{N(h.Box.X + i * cw)}\" y=\"{N(h.Box.Y + j * ch)}\" width=\"{N((run - i) * cw + 0.4)}\" height=\"{N(ch + 0.4)}\" fill=\"{p.Ramp(q / 31.0).Hex}\"/>\n");
                i = run;
            }
        }
    }

    /// <summary>Where an arrow's shaft stops, and its head's three corners.</summary>
    public static (PlotPt Shaft, IReadOnlyList<PlotPt> Head) ArrowGeometry(PlotPt from, PlotPt to, double width)
    {
        double dx = to.X - from.X, dy = to.Y - from.Y, len = Math.Sqrt(dx * dx + dy * dy);
        if (len < 1e-9) return (to, [to, to, to]);
        double ux = dx / len, uy = dy / len;
        double headLen = Math.Min(len * 0.6, 6 + width * 2.2), half = 2.6 + width * 1.1;
        var b = new PlotPt(to.X - ux * headLen, to.Y - uy * headLen);
        return (new PlotPt(to.X - ux * headLen * 0.8, to.Y - uy * headLen * 0.8), [to, new(b.X - uy * half, b.Y + ux * half), new(b.X + uy * half, b.Y - ux * half)]);
    }

    /// <summary>A bar's outline, its data end rounded.</summary>
    public static string BarPath(PlotBar b)
    {
        var r = b.Box;
        double rad = Math.Min(3, Math.Min(r.W / 2, r.H));
        if (b.Up)
            return $"M{N(r.X)},{N(r.Bottom)}L{N(r.X)},{N(r.Y + rad)}Q{N(r.X)},{N(r.Y)} {N(r.X + rad)},{N(r.Y)}L{N(r.Right - rad)},{N(r.Y)}Q{N(r.Right)},{N(r.Y)} {N(r.Right)},{N(r.Y + rad)}L{N(r.Right)},{N(r.Bottom)}Z";
        return $"M{N(r.X)},{N(r.Y)}L{N(r.X)},{N(r.Bottom - rad)}Q{N(r.X)},{N(r.Bottom)} {N(r.X + rad)},{N(r.Bottom)}L{N(r.Right - rad)},{N(r.Bottom)}Q{N(r.Right)},{N(r.Bottom)} {N(r.Right)},{N(r.Bottom - rad)}L{N(r.Right)},{N(r.Y)}Z";
    }

    static string Path(IReadOnlyList<PlotPt> pts) =>
        "M" + string.Join("L", pts.Select(q => N(q.X) + "," + N(q.Y)));

    static string Colour(PlotRgb c) => c.Hex;

    static string Opacity(string attribute, double opacity, PlotRgb c)
    {
        double o = opacity * c.A / 255.0;
        return o >= 0.999 ? "" : $" {attribute}=\"{N(o)}\"";
    }

    static string Dash(PlotDash dash, double width) => dash switch
    {
        PlotDash.Dashed => $" stroke-dasharray=\"{N(width * 3)} {N(width * 2.5)}\"",
        PlotDash.Dotted => $" stroke-dasharray=\"0.1 {N(width * 2.4)}\"",
        _ => "",
    };

    static string N(double v) => Math.Round(v, 2).ToString("0.##", CultureInfo.InvariantCulture);

    static string Esc(string s) => s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
}
