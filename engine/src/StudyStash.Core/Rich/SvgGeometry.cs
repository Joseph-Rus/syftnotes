using System.Globalization;
using System.Xml.Linq;

namespace StudyStash.Core.Rich;

/// <summary>A box in a drawing's own units.</summary>
public readonly record struct Bounds(double X, double Y, double W, double H)
{
    public double Right => X + W;
    public double Bottom => Y + H;
    public double CenterX => X + W / 2;
    public double CenterY => Y + H / 2;

    public Bounds Union(Bounds o)
    {
        double x = Math.Min(X, o.X), y = Math.Min(Y, o.Y);
        return new Bounds(x, y, Math.Max(Right, o.Right) - x, Math.Max(Bottom, o.Bottom) - y);
    }

    public Bounds Inflate(double d) => new(X - d, Y - d, W + 2 * d, H + 2 * d);

    /// <summary>How much of this box and another overlap, in square units (0 when they don't).</summary>
    public double Overlap(Bounds o) => Math.Max(0, Math.Min(Right, o.Right) - Math.Max(X, o.X)) * Math.Max(0, Math.Min(Bottom, o.Bottom) - Math.Max(Y, o.Y));

    public bool Contains(double px, double py) => px >= X && px <= Right && py >= Y && py <= Bottom;

    /// <summary>How far a point is from the box (0 inside it).</summary>
    public double Distance(double px, double py)
    {
        double dx = Math.Max(0, Math.Max(X - px, px - Right)), dy = Math.Max(0, Math.Max(Y - py, py - Bottom));
        return Math.Sqrt(dx * dx + dy * dy);
    }

    static Bounds Of(IEnumerable<(double X, double Y)> points)
    {
        double x0 = double.MaxValue, y0 = double.MaxValue, x1 = double.MinValue, y1 = double.MinValue;
        foreach (var (x, y) in points)
        {
            x0 = Math.Min(x0, x);
            y0 = Math.Min(y0, y);
            x1 = Math.Max(x1, x);
            y1 = Math.Max(y1, y);
        }
        return x0 > x1 ? default : new Bounds(x0, y0, x1 - x0, y1 - y0);
    }

    public static Bounds? Around(IReadOnlyCollection<(double X, double Y)> points) => points.Count == 0 ? null : Of(points);
}

/// <summary>
/// Where things are in an SVG drawing, worked out from its markup alone (nothing is rendered): each shape's outline
/// points and box in the drawing's own units, through every transform and copy; and a piece of text's box, its
/// width estimated from its letters and size. Close enough to tell a label that runs off the edge, two labels on top of
/// each other, or a leader line that stops short of its part; not a renderer.
/// </summary>
public static partial class SvgGeometry
{
    /// <summary>An SVG transform: x' = A x + C y + E, y' = B x + D y + F.</summary>
    public readonly record struct Affine(double A, double B, double C, double D, double E, double F)
    {
        public static readonly Affine Identity = new(1, 0, 0, 1, 0, 0);

        /// <summary>This transform after <paramref name="inner"/> (inner applies first).</summary>
        public Affine Then(Affine inner) => new(
            A * inner.A + C * inner.B, B * inner.A + D * inner.B,
            A * inner.C + C * inner.D, B * inner.C + D * inner.D,
            A * inner.E + C * inner.F + E, B * inner.E + D * inner.F + F);

        public (double X, double Y) Apply(double x, double y) => (A * x + C * y + E, B * x + D * y + F);

        /// <summary>How much it scales lengths, on average.</summary>
        public double Scale => Math.Sqrt(Math.Abs(A * D - B * C));
    }

    static readonly HashSet<string> Hidden = ["defs", "symbol", "clipPath", "marker", "linearGradient", "radialGradient", "stop", "title", "desc"];

    /// <summary>What a drawing holds, measured: each drawn element's box (groups too), and each text's.</summary>
    public sealed class Measure
    {
        readonly Dictionary<string, XElement> byId = new(StringComparer.Ordinal);
        public Dictionary<XElement, Bounds> Boxes { get; } = [];
        /// <summary>Each text element and its box, in the order drawn.</summary>
        public List<(XElement Text, Bounds Box, double Size)> Texts { get; } = [];
        /// <summary>Each shape that's drawn (not a group, not text), with its outline points.</summary>
        public List<(XElement Shape, IReadOnlyList<(double X, double Y)> Points)> Shapes { get; } = [];

        public Measure(XElement root)
        {
            foreach (var e in root.DescendantsAndSelf())
                if ((string?)e.Attribute("id") is { Length: > 0 } id) byId.TryAdd(id, e);
            var vb = ((string?)root.Attribute("viewBox"))?.Split([' ', ','], StringSplitOptions.RemoveEmptyEntries);
            View = vb is { Length: 4 } && vb.All(v => double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
                ? new Bounds(N(vb[0]), N(vb[1]), N(vb[2]), N(vb[3]))
                : new Bounds(0, 0, 0, 0);
            Walk(root, Affine.Identity, 16, "start", 0);
        }

        /// <summary>The drawing's canvas (its viewBox).</summary>
        public Bounds View { get; }

        public XElement? ById(string id) => byId.GetValueOrDefault(id);

        Bounds? Walk(XElement e, Affine outer, double fontSize, string anchor, int depth)
        {
            string name = e.Name.LocalName;
            if (depth > 40 || Hidden.Contains(name) || (string?)e.Attribute("display") == "none") return null;
            var m = outer.Then(Transform((string?)e.Attribute("transform")));
            fontSize = FontSize((string?)e.Attribute("font-size"), fontSize);
            anchor = (string?)e.Attribute("text-anchor") ?? anchor;
            Bounds? box = null;
            switch (name)
            {
                case "svg" or "g":
                    foreach (var child in e.Elements())
                        if (Walk(child, m, fontSize, anchor, depth + 1) is { } b) box = box is { } u ? u.Union(b) : b;
                    break;
                case "use":
                    if (((string?)e.Attribute("href"))?.TrimStart('#') is { } href && byId.TryGetValue(href, out var target) && !ReferenceEquals(target, e))
                    {
                        var at = m.Then(new Affine(1, 0, 0, 1, Num(e, "x"), Num(e, "y")));
                        // A symbol or group in defs draws where it's used, as if it stood here.
                        if (target.Name.LocalName is "symbol" or "g")
                            foreach (var child in target.Elements())
                            {
                                if (Walk(child, at, fontSize, anchor, depth + 1) is { } b) box = box is { } u ? u.Union(b) : b;
                            }
                        else box = Walk(target, at, fontSize, anchor, depth + 1);
                    }
                    break;
                case "text":
                    box = TextBox(e, m, fontSize, anchor);
                    if (box is { } t) Texts.Add((e, t, fontSize * m.Scale));
                    break;
                default:
                    var points = Outline(e, m);
                    if (points.Count > 0)
                    {
                        Shapes.Add((e, points));
                        box = Bounds.Around(points);
                    }
                    break;
            }
            if (box is { } found) Boxes[e] = found;
            return box;
        }
    }

    /// <summary>Measures a drawing (cleaned, or as written).</summary>
    public static Measure Of(XElement root) => new(root);

    static double N(string s) => double.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);

    static double Num(XElement e, string attr, double fallback = 0)
    {
        string? v = (string?)e.Attribute(attr);
        if (v is null) return fallback;
        var first = Numbers(v).FirstOrDefault(double.NaN);
        return double.IsFinite(first) ? first : fallback;
    }

    static double FontSize(string? value, double inherited)
    {
        if (value is null) return inherited;
        string v = value.Trim().ToLowerInvariant();
        double k = 1;
        if (v.EndsWith("px", StringComparison.Ordinal)) v = v[..^2];
        else if (v.EndsWith("em", StringComparison.Ordinal))
        {
            v = v[..^2];
            k = inherited;
        }
        else if (v.EndsWith('%'))
        {
            v = v[..^1];
            k = inherited / 100;
        }
        return double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out double d) && d > 0 ? d * k : inherited;
    }

    /// <summary>The points that outline a shape (its corners, or points along its curves), transformed.</summary>
    public static IReadOnlyList<(double X, double Y)> Outline(XElement e, Affine m)
    {
        var local = new List<(double, double)>();
        switch (e.Name.LocalName)
        {
            case "rect":
                double x = Num(e, "x"), y = Num(e, "y"), w = Num(e, "width"), h = Num(e, "height");
                if (w > 0 && h > 0) local.AddRange([(x, y), (x + w, y), (x + w, y + h), (x, y + h)]);
                break;
            case "circle":
                Ellipse(local, Num(e, "cx"), Num(e, "cy"), Num(e, "r"), Num(e, "r"));
                break;
            case "ellipse":
                Ellipse(local, Num(e, "cx"), Num(e, "cy"), Num(e, "rx"), Num(e, "ry"));
                break;
            case "line":
                local.AddRange([(Num(e, "x1"), Num(e, "y1")), (Num(e, "x2"), Num(e, "y2"))]);
                break;
            case "polyline" or "polygon":
                var n = Numbers((string?)e.Attribute("points") ?? "").ToList();
                for (int i = 0; i + 1 < n.Count; i += 2) local.Add((n[i], n[i + 1]));
                break;
            case "path":
                local.AddRange(PathPoints((string?)e.Attribute("d") ?? ""));
                break;
        }
        return local.Select(p => m.Apply(p.Item1, p.Item2)).ToList();
    }

    static void Ellipse(List<(double, double)> into, double cx, double cy, double rx, double ry)
    {
        if (rx <= 0 || ry <= 0) return;
        for (int k = 0; k < 16; k++)
        {
            double t = k * Math.PI / 8;
            into.Add((cx + rx * Math.Cos(t), cy + ry * Math.Sin(t)));
        }
    }

    /// <summary>A text's box: from where it's anchored, as wide as its letters at its size (an average letter is a
    /// little over half the size wide), from a cap's height above the line to a descender below; each line of a
    /// text broken into tspans with their own x or dy measured on its own.</summary>
    static Bounds? TextBox(XElement text, Affine m, double size, string anchor)
    {
        double x = Num(text, "x"), y = Num(text, "y") + Num(text, "dy");
        var lines = new List<(double X, double Y, string Words, double Size, string Anchor)>();
        var spans = text.Elements().Where(s => s.Name.LocalName == "tspan").ToList();
        bool broken = spans.Any(s => s.Attribute("x") is not null || s.Attribute("y") is not null || s.Attribute("dy") is not null);
        if (!broken) lines.Add((x, y, Spaced(text.Value), size, anchor));
        else
        {
            string lead = string.Concat(text.Nodes().OfType<XText>().Select(t => t.Value));
            if (Spaced(lead).Length > 0) lines.Add((x, y, Spaced(lead), size, anchor));
            double lx = x, ly = y;
            foreach (var s in spans)
            {
                lx = s.Attribute("x") is not null ? Num(s, "x") : lx;
                ly = (s.Attribute("y") is not null ? Num(s, "y") : ly) + Num(s, "dy");
                lines.Add((lx, ly, Spaced(s.Value), FontSize((string?)s.Attribute("font-size"), size), (string?)s.Attribute("text-anchor") ?? anchor));
            }
        }
        string baseline = (string?)text.Attribute("dominant-baseline") ?? "";
        double shift = baseline is "middle" or "central" ? 0.35 : baseline is "hanging" or "text-before-edge" ? 0.8 : 0;
        bool bold = ((string?)text.Attribute("font-weight")) is "bold" or "600" or "700" or "800" or "900";
        var points = new List<(double X, double Y)>();
        foreach (var (lx, ly, words, s, a) in lines)
        {
            if (words.Length == 0) continue;
            double w = Width(words, s, bold);
            double left = a == "middle" ? lx - w / 2 : a == "end" ? lx - w : lx;
            double top = ly + (shift - 0.78) * s, bottom = ly + (shift + 0.24) * s;
            points.AddRange([m.Apply(left, top), m.Apply(left + w, top), m.Apply(left + w, bottom), m.Apply(left, bottom)]);
        }
        return Bounds.Around(points);
    }

    /// <summary>About how wide words are in the app's sans-serif fonts: narrow letters and spaces count for less,
    /// capitals and wide letters for more.</summary>
    public static double Width(string words, double size, bool bold = false)
    {
        double units = 0;
        foreach (char c in words)
            units += c switch
            {
                ' ' or 'i' or 'l' or 'j' or 'I' or '.' or ',' or ':' or ';' or '\'' or '!' or '|' => 0.28,
                'f' or 't' or 'r' or '(' or ')' or '-' => 0.36,
                'm' or 'w' or 'M' or 'W' => 0.84,
                >= 'A' and <= 'Z' => 0.66,
                >= '0' and <= '9' => 0.56,
                _ => 0.53,
            };
        return units * size * (bold ? 1.06 : 1);
    }

    static string Spaced(string s) => string.Join(' ', s.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    // --- transforms ------------------------------------------------------------------------------------------------

    public static Affine Transform(string? value)
    {
        var m = Affine.Identity;
        if (string.IsNullOrWhiteSpace(value)) return m;
        foreach (System.Text.RegularExpressions.Match t in TransformPart().Matches(value))
        {
            var a = Numbers(t.Groups[2].Value).ToArray();
            double A(int i, double d = 0) => i < a.Length ? a[i] : d;
            Affine step = t.Groups[1].Value switch
            {
                "matrix" when a.Length >= 6 => new Affine(a[0], a[1], a[2], a[3], a[4], a[5]),
                "translate" => new Affine(1, 0, 0, 1, A(0), A(1)),
                "scale" => new Affine(A(0, 1), 0, 0, A(1, A(0, 1)), 0, 0),
                "rotate" => Rotate(A(0), A(1), A(2)),
                "skewX" => new Affine(1, 0, Math.Tan(A(0) * Math.PI / 180), 1, 0, 0),
                "skewY" => new Affine(1, Math.Tan(A(0) * Math.PI / 180), 0, 1, 0, 0),
                _ => Affine.Identity,
            };
            m = m.Then(step);
        }
        return m;
    }

    static Affine Rotate(double degrees, double cx, double cy)
    {
        double r = degrees * Math.PI / 180, c = Math.Cos(r), s = Math.Sin(r);
        var turn = new Affine(c, s, -s, c, 0, 0);
        return new Affine(1, 0, 0, 1, cx, cy).Then(turn).Then(new Affine(1, 0, 0, 1, -cx, -cy));
    }

    [System.Text.RegularExpressions.GeneratedRegex(@"(matrix|translate|scale|rotate|skewX|skewY)\s*\(([^)]*)\)")]
    private static partial System.Text.RegularExpressions.Regex TransformPart();

    // --- numbers and paths -----------------------------------------------------------------------------------------

    /// <summary>The numbers in a list (spaces, commas, or nothing at all between them, as SVG allows: "1-2.5.5").</summary>
    public static IEnumerable<double> Numbers(string s)
    {
        int i = 0;
        while (i < s.Length)
        {
            if (Number(s, ref i) is double d) yield return d;
            else i++;
        }
    }

    /// <summary>The number at <paramref name="i"/> (skipping separators first), moving past it; null if none.</summary>
    static double? Number(string s, ref int i)
    {
        while (i < s.Length && (char.IsWhiteSpace(s[i]) || s[i] == ',')) i++;
        int start = i;
        if (i < s.Length && s[i] is '+' or '-') i++;
        bool digits = false, dot = false;
        while (i < s.Length && (char.IsAsciiDigit(s[i]) || (s[i] == '.' && !dot)))
        {
            if (s[i] == '.') dot = true;
            else digits = true;
            i++;
        }
        if (!digits)
        {
            i = start;
            return null;
        }
        if (i < s.Length && s[i] is 'e' or 'E')
        {
            int e = i + 1;
            if (e < s.Length && s[e] is '+' or '-') e++;
            if (e < s.Length && char.IsAsciiDigit(s[e]))
            {
                i = e;
                while (i < s.Length && char.IsAsciiDigit(s[i])) i++;
            }
        }
        return double.TryParse(s.AsSpan(start, i - start), NumberStyles.Float, CultureInfo.InvariantCulture, out double d) && double.IsFinite(d) ? d : 0;
    }

    /// <summary>A flag of an arc: a single 0 or 1, which may run straight into the next number.</summary>
    static double? Flag(string s, ref int i)
    {
        while (i < s.Length && (char.IsWhiteSpace(s[i]) || s[i] == ',')) i++;
        if (i < s.Length && s[i] is '0' or '1') return s[i++] - '0';
        return null;
    }

    /// <summary>Points along a path: its ends, and points along each curve and arc (enough for its box).</summary>
    public static List<(double X, double Y)> PathPoints(string d)
    {
        var pts = new List<(double, double)>();
        double cx = 0, cy = 0, sx = 0, sy = 0, lx = 0, ly = 0; // current, subpath start, last control
        char cmd = ' ', prev = ' ';
        int i = 0, guard = 0;
        while (i < d.Length && guard++ < 200_000)
        {
            while (i < d.Length && (char.IsWhiteSpace(d[i]) || d[i] == ',')) i++;
            if (i >= d.Length) break;
            if (char.IsLetter(d[i]) && d[i] is not ('e' or 'E'))
            {
                cmd = d[i++];
                if (cmd is 'Z' or 'z')
                {
                    cx = sx;
                    cy = sy;
                    prev = cmd;
                    continue;
                }
            }
            else if (cmd == ' ') break;
            bool rel = char.IsLower(cmd);
            double ox = rel ? cx : 0, oy = rel ? cy : 0;
            int before = i;
            switch (char.ToUpperInvariant(cmd))
            {
                case 'M':
                case 'L':
                case 'T':
                {
                    if (Number(d, ref i) is not double x || Number(d, ref i) is not double y) goto stop;
                    x += ox;
                    y += oy;
                    if (char.ToUpperInvariant(cmd) == 'T')
                    {
                        bool smooth = char.ToUpperInvariant(prev) is 'Q' or 'T';
                        double qx = smooth ? 2 * cx - lx : cx, qy = smooth ? 2 * cy - ly : cy;
                        Quad(pts, cx, cy, qx, qy, x, y);
                        lx = qx;
                        ly = qy;
                    }
                    else pts.Add((x, y));
                    if (char.ToUpperInvariant(cmd) == 'M')
                    {
                        sx = x;
                        sy = y;
                        cmd = rel ? 'l' : 'L'; // more pairs after a move are lines
                        prev = 'M';
                    }
                    else prev = cmd;
                    cx = x;
                    cy = y;
                    break;
                }
                case 'H':
                {
                    if (Number(d, ref i) is not double x) goto stop;
                    cx = x + ox;
                    pts.Add((cx, cy));
                    prev = cmd;
                    break;
                }
                case 'V':
                {
                    if (Number(d, ref i) is not double y) goto stop;
                    cy = y + oy;
                    pts.Add((cx, cy));
                    prev = cmd;
                    break;
                }
                case 'C':
                {
                    if (Number(d, ref i) is not double x1 || Number(d, ref i) is not double y1 || Number(d, ref i) is not double x2
                        || Number(d, ref i) is not double y2 || Number(d, ref i) is not double x || Number(d, ref i) is not double y) goto stop;
                    Cubic(pts, cx, cy, x1 + ox, y1 + oy, x2 + ox, y2 + oy, x + ox, y + oy);
                    lx = x2 + ox;
                    ly = y2 + oy;
                    cx = x + ox;
                    cy = y + oy;
                    prev = cmd;
                    break;
                }
                case 'S':
                {
                    if (Number(d, ref i) is not double x2 || Number(d, ref i) is not double y2 || Number(d, ref i) is not double x || Number(d, ref i) is not double y) goto stop;
                    bool smooth = char.ToUpperInvariant(prev) is 'C' or 'S';
                    double x1 = smooth ? 2 * cx - lx : cx, y1 = smooth ? 2 * cy - ly : cy;
                    Cubic(pts, cx, cy, x1, y1, x2 + ox, y2 + oy, x + ox, y + oy);
                    lx = x2 + ox;
                    ly = y2 + oy;
                    cx = x + ox;
                    cy = y + oy;
                    prev = cmd;
                    break;
                }
                case 'Q':
                {
                    if (Number(d, ref i) is not double x1 || Number(d, ref i) is not double y1 || Number(d, ref i) is not double x || Number(d, ref i) is not double y) goto stop;
                    Quad(pts, cx, cy, x1 + ox, y1 + oy, x + ox, y + oy);
                    lx = x1 + ox;
                    ly = y1 + oy;
                    cx = x + ox;
                    cy = y + oy;
                    prev = cmd;
                    break;
                }
                case 'A':
                {
                    if (Number(d, ref i) is not double rx || Number(d, ref i) is not double ry || Number(d, ref i) is not double rot
                        || Flag(d, ref i) is not double large || Flag(d, ref i) is not double sweep || Number(d, ref i) is not double x || Number(d, ref i) is not double y) goto stop;
                    Arc(pts, cx, cy, Math.Abs(rx), Math.Abs(ry), rot, large != 0, sweep != 0, x + ox, y + oy);
                    cx = x + ox;
                    cy = y + oy;
                    prev = cmd;
                    break;
                }
                default:
                    goto stop;
            }
            if (i == before) break;
        }
        stop:
        return pts;
    }

    static void Cubic(List<(double, double)> pts, double x0, double y0, double x1, double y1, double x2, double y2, double x3, double y3)
    {
        for (int k = 1; k <= 8; k++)
        {
            double t = k / 8.0, u = 1 - t;
            pts.Add((u * u * u * x0 + 3 * u * u * t * x1 + 3 * u * t * t * x2 + t * t * t * x3, u * u * u * y0 + 3 * u * u * t * y1 + 3 * u * t * t * y2 + t * t * t * y3));
        }
    }

    static void Quad(List<(double, double)> pts, double x0, double y0, double x1, double y1, double x2, double y2)
    {
        for (int k = 1; k <= 6; k++)
        {
            double t = k / 6.0, u = 1 - t;
            pts.Add((u * u * x0 + 2 * u * t * x1 + t * t * x2, u * u * y0 + 2 * u * t * y1 + t * t * y2));
        }
    }

    /// <summary>Points along an arc, from its endpoints to its centre and angles (the SVG spec's conversion).</summary>
    static void Arc(List<(double, double)> pts, double x1, double y1, double rx, double ry, double degrees, bool large, bool sweep, double x2, double y2)
    {
        if (rx == 0 || ry == 0 || (x1 == x2 && y1 == y2))
        {
            pts.Add((x2, y2));
            return;
        }
        double phi = degrees * Math.PI / 180, cos = Math.Cos(phi), sin = Math.Sin(phi);
        double dx = (x1 - x2) / 2, dy = (y1 - y2) / 2;
        double x1p = cos * dx + sin * dy, y1p = -sin * dx + cos * dy;
        double lambda = x1p * x1p / (rx * rx) + y1p * y1p / (ry * ry);
        if (lambda > 1)
        {
            rx *= Math.Sqrt(lambda);
            ry *= Math.Sqrt(lambda);
        }
        double num = rx * rx * ry * ry - rx * rx * y1p * y1p - ry * ry * x1p * x1p, den = rx * rx * y1p * y1p + ry * ry * x1p * x1p;
        double coef = (large == sweep ? -1 : 1) * Math.Sqrt(Math.Max(0, num / Math.Max(1e-12, den)));
        double cxp = coef * rx * y1p / ry, cyp = -coef * ry * x1p / rx;
        double ccx = cos * cxp - sin * cyp + (x1 + x2) / 2, ccy = sin * cxp + cos * cyp + (y1 + y2) / 2;
        static double Angle(double ux, double uy, double vx, double vy)
        {
            double a = Math.Atan2(ux * vy - uy * vx, ux * vx + uy * vy);
            return a;
        }
        double t1 = Angle(1, 0, (x1p - cxp) / rx, (y1p - cyp) / ry);
        double dt = Angle((x1p - cxp) / rx, (y1p - cyp) / ry, (-x1p - cxp) / rx, (-y1p - cyp) / ry);
        if (!sweep && dt > 0) dt -= 2 * Math.PI;
        else if (sweep && dt < 0) dt += 2 * Math.PI;
        for (int k = 1; k <= 12; k++)
        {
            double t = t1 + dt * k / 12;
            double ex = rx * Math.Cos(t), ey = ry * Math.Sin(t);
            pts.Add((cos * ex - sin * ey + ccx, sin * ex + cos * ey + ccy));
        }
    }
}
