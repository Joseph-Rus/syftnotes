using System.Globalization;
using System.Text;
using System.Xml.Linq;

namespace PartsImport;

/// <summary>
/// Makes a heavy traced drawing light (DBCLS's anatomy is traced with thousands of tiny curves): each path is followed
/// as points, points that a straight line between their neighbours already passes within <c>tolerance</c> of are
/// dropped (Ramer–Douglas–Peucker), and what's left is drawn again as smooth curves through the points, with corners
/// kept as corners. At the size a part is shown, the difference is under a pixel.
/// </summary>
static class Simplify
{
    public static void All(IEnumerable<XElement> leaves, double tolerance)
    {
        foreach (var path in leaves.SelectMany(l => l.DescendantsAndSelf()).Where(e => e.Name.LocalName == "path").ToList())
            if ((string?)path.Attribute("d") is { } d && d.Length > 200)
                path.SetAttributeValue("d", Path(d, tolerance));
    }

    public static string Path(string d, double tolerance)
    {
        var sb = new StringBuilder();
        foreach (var (points, closed) in Subpaths(d))
        {
            var kept = Rdp(points, tolerance);
            if (kept.Count < 2) continue;
            sb.Append('M').Append(F(kept[0].X)).Append(' ').Append(F(kept[0].Y));
            int n = kept.Count;
            for (int i = 0; i + 1 < n; i++)
            {
                var p0 = kept[Math.Max(0, i - 1)];
                var p1 = kept[i];
                var p2 = kept[i + 1];
                var p3 = kept[Math.Min(n - 1, i + 2)];
                if (closed && i == 0 && n > 2) p0 = kept[n - 2];
                if (closed && i + 2 >= n && n > 2) p3 = kept[1];
                if (Corner(p0, p1, p2) || Corner(p1, p2, p3))
                {
                    sb.Append('L').Append(F(p2.X)).Append(' ').Append(F(p2.Y));
                    continue;
                }
                double c1x = p1.X + (p2.X - p0.X) / 6, c1y = p1.Y + (p2.Y - p0.Y) / 6;
                double c2x = p2.X - (p3.X - p1.X) / 6, c2y = p2.Y - (p3.Y - p1.Y) / 6;
                sb.Append('C').Append(F(c1x)).Append(' ').Append(F(c1y)).Append(' ').Append(F(c2x)).Append(' ').Append(F(c2y)).Append(' ').Append(F(p2.X)).Append(' ').Append(F(p2.Y));
            }
            if (closed) sb.Append('Z');
        }
        return sb.ToString();
    }

    static bool Corner((double X, double Y) a, (double X, double Y) b, (double X, double Y) c)
    {
        double ux = b.X - a.X, uy = b.Y - a.Y, vx = c.X - b.X, vy = c.Y - b.Y;
        double lu = Math.Sqrt(ux * ux + uy * uy), lv = Math.Sqrt(vx * vx + vy * vy);
        if (lu == 0 || lv == 0) return false;
        return (ux * vx + uy * vy) / (lu * lv) < 0.35; // turning by more than about 70 degrees
    }

    static List<(double X, double Y)> Rdp(List<(double X, double Y)> pts, double tol)
    {
        if (pts.Count < 3) return pts;
        var keep = new bool[pts.Count];
        keep[0] = keep[^1] = true;
        var stack = new Stack<(int, int)>();
        stack.Push((0, pts.Count - 1));
        while (stack.Count > 0)
        {
            var (a, b) = stack.Pop();
            double far = 0;
            int at = -1;
            for (int i = a + 1; i < b; i++)
            {
                double d = Distance(pts[i], pts[a], pts[b]);
                if (d > far) { far = d; at = i; }
            }
            if (at >= 0 && far > tol)
            {
                keep[at] = true;
                stack.Push((a, at));
                stack.Push((at, b));
            }
        }
        return pts.Where((_, i) => keep[i]).ToList();
    }

    static double Distance((double X, double Y) p, (double X, double Y) a, (double X, double Y) b)
    {
        double dx = b.X - a.X, dy = b.Y - a.Y, len = dx * dx + dy * dy;
        if (len == 0) return Math.Sqrt((p.X - a.X) * (p.X - a.X) + (p.Y - a.Y) * (p.Y - a.Y));
        double t = Math.Clamp(((p.X - a.X) * dx + (p.Y - a.Y) * dy) / len, 0, 1);
        double x = a.X + t * dx - p.X, y = a.Y + t * dy - p.Y;
        return Math.Sqrt(x * x + y * y);
    }

    /// <summary>A path's subpaths as points along them (curves sampled), and whether each is closed.</summary>
    static IEnumerable<(List<(double X, double Y)> Points, bool Closed)> Subpaths(string d)
    {
        var tokens = Tokens(d).ToList();
        var pts = new List<(double X, double Y)>();
        double x = 0, y = 0, sx = 0, sy = 0, lcx = 0, lcy = 0;
        char cmd = ' ', prev = ' ';
        int i = 0;
        double Next() => tokens[i++].Num;
        while (i < tokens.Count)
        {
            if (tokens[i].Cmd is char c) { cmd = c; i++; if (c is 'Z' or 'z') { if (pts.Count > 1) yield return (pts, true); pts = []; x = sx; y = sy; prev = 'Z'; continue; } }
            else if (cmd == ' ') yield break;
            bool rel = char.IsLower(cmd);
            double ox = rel ? x : 0, oy = rel ? y : 0;
            switch (char.ToUpperInvariant(cmd))
            {
                case 'M':
                    if (pts.Count > 1) yield return (pts, false);
                    pts = [];
                    x = ox + Next(); y = oy + Next(); sx = x; sy = y; pts.Add((x, y));
                    cmd = rel ? 'l' : 'L';
                    break;
                case 'L': x = ox + Next(); y = oy + Next(); pts.Add((x, y)); break;
                case 'H': x = (rel ? x : 0) + Next(); pts.Add((x, y)); break;
                case 'V': y = (rel ? y : 0) + Next(); pts.Add((x, y)); break;
                case 'C':
                {
                    double x1 = ox + Next(), y1 = oy + Next(), x2 = ox + Next(), y2 = oy + Next(), ex = ox + Next(), ey = oy + Next();
                    Cubic(pts, x, y, x1, y1, x2, y2, ex, ey); lcx = x2; lcy = y2; x = ex; y = ey; break;
                }
                case 'S':
                {
                    double x1 = prev is 'C' or 'S' ? 2 * x - lcx : x, y1 = prev is 'C' or 'S' ? 2 * y - lcy : y;
                    double x2 = ox + Next(), y2 = oy + Next(), ex = ox + Next(), ey = oy + Next();
                    Cubic(pts, x, y, x1, y1, x2, y2, ex, ey); lcx = x2; lcy = y2; x = ex; y = ey; break;
                }
                case 'Q':
                {
                    double qx = ox + Next(), qy = oy + Next(), ex = ox + Next(), ey = oy + Next();
                    Cubic(pts, x, y, x + 2.0 / 3 * (qx - x), y + 2.0 / 3 * (qy - y), ex + 2.0 / 3 * (qx - ex), ey + 2.0 / 3 * (qy - ey), ex, ey);
                    lcx = qx; lcy = qy; x = ex; y = ey; break;
                }
                case 'T': x = ox + Next(); y = oy + Next(); pts.Add((x, y)); break;
                case 'A':
                    Next(); Next(); Next(); Next(); Next();
                    x = ox + Next(); y = oy + Next(); pts.Add((x, y)); break;
                default: yield break;
            }
            prev = char.ToUpperInvariant(cmd);
        }
        if (pts.Count > 1) yield return (pts, false);
    }

    static void Cubic(List<(double, double)> pts, double x0, double y0, double x1, double y1, double x2, double y2, double x3, double y3)
    {
        for (int k = 1; k <= 8; k++)
        {
            double t = k / 8.0, u = 1 - t;
            pts.Add((u * u * u * x0 + 3 * u * u * t * x1 + 3 * u * t * t * x2 + t * t * t * x3, u * u * u * y0 + 3 * u * u * t * y1 + 3 * u * t * t * y2 + t * t * t * y3));
        }
    }

    static IEnumerable<(char? Cmd, double Num)> Tokens(string d)
    {
        int i = 0;
        while (i < d.Length)
        {
            char c = d[i];
            if (char.IsLetter(c) && c is not 'e' and not 'E') { yield return (c, 0); i++; continue; }
            if (char.IsDigit(c) || c is '-' or '+' or '.')
            {
                int start = i;
                bool dot = c == '.';
                i++;
                while (i < d.Length)
                {
                    char n = d[i];
                    if (char.IsDigit(n)) { i++; continue; }
                    if (n == '.' && !dot) { dot = true; i++; continue; }
                    if (n is 'e' or 'E') { i++; if (i < d.Length && d[i] is '-' or '+') i++; continue; }
                    break;
                }
                yield return (null, double.Parse(d[start..i], NumberStyles.Float, CultureInfo.InvariantCulture));
                continue;
            }
            i++;
        }
    }

    static string F(double v) => Math.Round(v, 2).ToString("0.##", CultureInfo.InvariantCulture);
}
