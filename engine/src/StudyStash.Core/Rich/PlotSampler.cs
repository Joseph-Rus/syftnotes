namespace StudyStash.Core.Rich;

/// <summary>A point on the screen (or in the plane).</summary>
public readonly record struct PlotPt(double X, double Y);

/// <summary>Where the plane lands on the screen: the window in view, drawn into <see cref="Area"/>.</summary>
public readonly record struct PlotMap(PlotWindow View, double Left, double Top, double Width, double Height)
{
    public double Sx => Width / View.Width;
    public double Sy => Height / View.Height;
    public double ScreenX(double x) => Left + (x - View.X0) * Sx;
    public double ScreenY(double y) => Top + (View.Y1 - y) * Sy;
    public PlotPt Screen(double x, double y) => new(ScreenX(x), ScreenY(y));
    public double DataX(double sx) => View.X0 + (sx - Left) / Sx;
    public double DataY(double sy) => View.Y1 - (sy - Top) / Sy;
    public double Right => Left + Width;
    public double Bottom => Top + Height;
    public bool Inside(PlotPt p, double slop = 0) => p.X >= Left - slop && p.X <= Right + slop && p.Y >= Top - slop && p.Y <= Bottom + slop;
}

/// <summary>
/// Turns what a plot's expressions say into lines on the screen, exactly: a curve is sampled where it bends (more
/// samples there, fewer where it's straight), breaks where it's undefined or jumps (no vertical line at an
/// asymptote, or across a step), and leaves the screen where it does, without points thousands of screens away. A
/// frame stops refining once it has spent its budget (<see cref="PlotEnv.FrameLeft"/>), so no expression can make it
/// slow.
/// </summary>
public static class PlotSampler
{
    /// <summary>How far, in pixels, a straight line between samples may stray from the curve before the middle is
    /// sampled too; and how many times a stretch is halved at most.</summary>
    const double Tolerance = 0.2;
    const int MaxDepth = 14;

    /// <summary>
    /// The visible pieces of y = <paramref name="f"/>(x) for x from <paramref name="a"/> to <paramref name="b"/>
    /// (already cut to the view), in screen points: one list per unbroken piece. <paramref name="detail"/> below 1
    /// samples more coarsely (while a slider is being dragged on a slow plot).
    /// </summary>
    public static List<List<PlotPt>> Curve(Func<double, double> f, double a, double b, PlotMap map, PlotEnv env, double detail = 1)
    {
        var pieces = new List<List<PlotPt>>();
        if (!(b > a) || !double.IsFinite(a) || !double.IsFinite(b)) return pieces;
        a = Math.Max(a, map.View.X0);
        b = Math.Min(b, map.View.X1);
        if (!(b > a)) return pieces;
        double px = (b - a) * map.Sx;
        int n = (int)Math.Clamp(px / 3 * detail, 8, 600);
        var raw = new List<(double X, double Y, bool Break)>(n * 2);
        double guard = map.Height * 8;
        double Sy(double y) => Math.Clamp(map.ScreenY(y), map.Top - guard, map.Bottom + guard);
        bool Off(double y, int side) => double.IsFinite(y) && (side > 0 ? map.ScreenY(y) < map.Top - 2 : map.ScreenY(y) > map.Bottom + 2);
        int depthLimit = detail < 1 ? MaxDepth - 4 : MaxDepth;

        void Refine(double xl, double yl, double xr, double yr, int depth)
        {
            bool split = false;
            double xm = 0, ym = 0;
            if (depth < depthLimit && (xr - xl) * map.Sx > 1e-3 && env.FrameLeft > 0)
            {
                xm = 0.5 * (xl + xr);
                ym = f(xm);
                bool fl = double.IsFinite(yl), fm = double.IsFinite(ym), fr = double.IsFinite(yr);
                if (fl != fm || fm != fr) split = (xr - xl) * map.Sx > 0.05;
                else if (fl && fm && fr && !(Off(yl, 1) && Off(ym, 1) && Off(yr, 1)) && !(Off(yl, -1) && Off(ym, -1) && Off(yr, -1)))
                {
                    double syl = Sy(yl), sym = Sy(ym), syr = Sy(yr);
                    double bend = Math.Abs(sym - 0.5 * (syl + syr));
                    split = bend > Tolerance || Math.Abs(syl - syr) > map.Height * 0.5;
                }
            }
            if (split)
            {
                Refine(xl, yl, xm, ym, depth + 1);
                Refine(xm, ym, xr, yr, depth + 1);
                return;
            }
            // A stretch that still jumps most of the screen once it's as narrow as it gets is a jump, not a line.
            bool jump = double.IsFinite(yl) && double.IsFinite(yr) && depth >= depthLimit - 1 && Math.Abs(Sy(yl) - Sy(yr)) > Math.Max(4, map.Height * 0.25);
            raw.Add((xr, yr, jump || !double.IsFinite(yl)));
        }

        double x0 = a, y0 = f(a);
        raw.Add((x0, y0, true));
        for (int i = 1; i <= n; i++)
        {
            double x1 = a + (b - a) * i / n, y1 = f(x1);
            Refine(x0, y0, x1, y1, 0);
            (x0, y0) = (x1, y1);
        }

        // Into pieces, each on the screen; a run far above or below the view keeps only its ends.
        List<PlotPt>? piece = null;
        foreach (var (x, y, brk) in raw)
        {
            if (!double.IsFinite(y))
            {
                piece = null;
                continue;
            }
            if (brk || piece is null)
            {
                piece = [];
                pieces.Add(piece);
            }
            var p = new PlotPt(map.ScreenX(x), Sy(y));
            if (piece.Count >= 2)
            {
                var q = piece[^1];
                var r = piece[^2];
                bool outside = (q.Y < map.Top - 4 && r.Y < map.Top - 4 && p.Y < map.Top - 4) || (q.Y > map.Bottom + 4 && r.Y > map.Bottom + 4 && p.Y > map.Bottom + 4);
                if (outside)
                {
                    piece[^1] = p;
                    continue;
                }
            }
            piece.Add(p);
        }
        pieces.RemoveAll(pc => pc.Count < 2 || pc.All(p => p.Y < map.Top - 2) || pc.All(p => p.Y > map.Bottom + 2));
        return pieces;
    }

    /// <summary>A parametric curve's visible pieces: sampled where it bends on the screen, broken where it's
    /// undefined or leaps across it.</summary>
    public static List<List<PlotPt>> Parametric(Func<double, (double X, double Y)> f, double a, double b, PlotMap map, PlotEnv env, double detail = 1)
    {
        var pieces = new List<List<PlotPt>>();
        if (!(b > a) || !double.IsFinite(a) || !double.IsFinite(b)) return pieces;
        int n = (int)Math.Clamp(240 * detail, 24, 400);
        double guard = Math.Max(map.Width, map.Height) * 8;
        PlotPt S((double X, double Y) v) => new(Math.Clamp(map.ScreenX(v.X), map.Left - guard, map.Right + guard), Math.Clamp(map.ScreenY(v.Y), map.Top - guard, map.Bottom + guard));
        static bool Ok((double X, double Y) v) => double.IsFinite(v.X) && double.IsFinite(v.Y);
        var raw = new List<(PlotPt P, bool Ok, bool Break)>();
        int depthLimit = detail < 1 ? 8 : 12;

        void Refine(double tl, (double X, double Y) vl, double tr, (double X, double Y) vr, int depth)
        {
            if (depth < depthLimit && env.FrameLeft > 0)
            {
                double tm = 0.5 * (tl + tr);
                var vm = f(tm);
                bool split;
                if (Ok(vl) != Ok(vm) || Ok(vm) != Ok(vr)) split = true;
                else if (Ok(vl) && Ok(vm) && Ok(vr))
                {
                    PlotPt pl = S(vl), pm = S(vm), pr = S(vr);
                    double dx = pr.X - pl.X, dy = pr.Y - pl.Y, len = Math.Sqrt(dx * dx + dy * dy);
                    double dev = len < 1e-9 ? Math.Sqrt((pm.X - pl.X) * (pm.X - pl.X) + (pm.Y - pl.Y) * (pm.Y - pl.Y))
                        : Math.Abs((pm.X - pl.X) * dy - (pm.Y - pl.Y) * dx) / len;
                    split = dev > Tolerance || len > Math.Max(map.Width, map.Height) * 0.25;
                }
                else split = false;
                if (split)
                {
                    Refine(tl, vl, tm, vm, depth + 1);
                    Refine(tm, vm, tr, vr, depth + 1);
                    return;
                }
            }
            bool jump = Ok(vl) && Ok(vr) && depth >= depthLimit && Dist(S(vl), S(vr)) > Math.Max(map.Width, map.Height) * 0.25;
            raw.Add((Ok(vr) ? S(vr) : default, Ok(vr), jump || !Ok(vl)));
        }

        var v0 = f(a);
        raw.Add((Ok(v0) ? S(v0) : default, Ok(v0), true));
        double t0 = a;
        for (int i = 1; i <= n; i++)
        {
            double t1 = a + (b - a) * i / n;
            var v1 = f(t1);
            Refine(t0, v0, t1, v1, 0);
            (t0, v0) = (t1, v1);
        }
        List<PlotPt>? piece = null;
        foreach (var (p, ok, brk) in raw)
        {
            if (!ok)
            {
                piece = null;
                continue;
            }
            if (brk || piece is null)
            {
                piece = [];
                pieces.Add(piece);
            }
            piece.Add(p);
        }
        pieces.RemoveAll(pc => pc.Count < 2);
        return pieces;
    }

    static double Dist(PlotPt a, PlotPt b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

    /// <summary>The area between y = f and y = g (or the axis) from a to b: the signed integral of f − g by Simpson's
    /// rule on 400 strips (NaN where either is undefined).</summary>
    public static double Area(Func<double, double> f, Func<double, double>? g, double a, double b)
    {
        if (!double.IsFinite(a) || !double.IsFinite(b)) return double.NaN;
        if (a == b) return 0;
        const int n = 400;
        double h = (b - a) / n, sum = 0;
        for (int i = 0; i <= n; i++)
        {
            double x = a + h * i;
            double v = f(x) - (g?.Invoke(x) ?? 0);
            if (!double.IsFinite(v)) return double.NaN;
            sum += v * (i == 0 || i == n ? 1 : i % 2 == 1 ? 4 : 2);
        }
        return sum * h / 3;
    }

    /// <summary>The slope of f at x (a centred difference, small enough for a curve's own scale).</summary>
    public static double Slope(Func<double, double> f, double x)
    {
        double h = 1e-5 * Math.Max(1, Math.Abs(x));
        return (f(x + h) - f(x - h)) / (2 * h);
    }

    /// <summary>Gradient descent from (x, y) on f: each step goes rate × the gradient downhill; it stops early once a
    /// step comes out undefined or flies off past any scale worth drawing.</summary>
    public static List<(double X, double Y)> Descent(Func<double, double, double> f, bool two, double x, double y, double rate, int steps)
    {
        var path = new List<(double, double)> { (x, y) };
        if (!double.IsFinite(rate) || !double.IsFinite(x) || (two && !double.IsFinite(y))) return path;
        steps = Math.Clamp(steps, 0, 500);
        for (int i = 0; i < steps; i++)
        {
            double hx = 1e-5 * Math.Max(1, Math.Abs(x));
            double gx = (f(x + hx, y) - f(x - hx, y)) / (2 * hx);
            double gy = 0;
            if (two)
            {
                double hy = 1e-5 * Math.Max(1, Math.Abs(y));
                gy = (f(x, y + hy) - f(x, y - hy)) / (2 * hy);
            }
            if (!double.IsFinite(gx) || !double.IsFinite(gy)) break;
            x -= rate * gx;
            if (two) y -= rate * gy;
            if (!double.IsFinite(x) || !double.IsFinite(y) || Math.Abs(x) > 1e9 || Math.Abs(y) > 1e9) break;
            path.Add((x, y));
        }
        return path;
    }

    /// <summary>
    /// The contour lines of a grid of values at <paramref name="level"/> (marching squares): line segments in grid
    /// coordinates (column, row), each cell's crossing found by linear interpolation along its edges.
    /// </summary>
    public static List<(PlotPt A, PlotPt B)> Contour(double[,] v, double level)
    {
        int nx = v.GetLength(0), ny = v.GetLength(1);
        var segments = new List<(PlotPt, PlotPt)>();
        for (int i = 0; i < nx - 1; i++)
            for (int j = 0; j < ny - 1; j++)
            {
                double a = v[i, j], b = v[i + 1, j], c = v[i + 1, j + 1], d = v[i, j + 1];
                if (!double.IsFinite(a) || !double.IsFinite(b) || !double.IsFinite(c) || !double.IsFinite(d)) continue;
                int code = (a > level ? 1 : 0) | (b > level ? 2 : 0) | (c > level ? 4 : 0) | (d > level ? 8 : 0);
                if (code is 0 or 15) continue;
                PlotPt E(int edge) => edge switch
                {
                    0 => new PlotPt(i + T(a, b), j),
                    1 => new PlotPt(i + 1, j + T(b, c)),
                    2 => new PlotPt(i + T(d, c), j + 1),
                    _ => new PlotPt(i, j + T(a, d)),
                };
                double T(double p, double q) => Math.Abs(q - p) < 1e-300 ? 0.5 : Math.Clamp((level - p) / (q - p), 0, 1);
                switch (code)
                {
                    case 1 or 14: segments.Add((E(0), E(3))); break;
                    case 2 or 13: segments.Add((E(0), E(1))); break;
                    case 3 or 12: segments.Add((E(1), E(3))); break;
                    case 4 or 11: segments.Add((E(1), E(2))); break;
                    case 6 or 9: segments.Add((E(0), E(2))); break;
                    case 7 or 8: segments.Add((E(2), E(3))); break;
                    case 5:
                        segments.Add((E(0), E(1)));
                        segments.Add((E(2), E(3)));
                        break;
                    case 10:
                        segments.Add((E(0), E(3)));
                        segments.Add((E(1), E(2)));
                        break;
                }
            }
        return segments;
    }

    /// <summary>The real eigenvalues of [[a, b], [c, d]] with a unit eigenvector each (none when they're complex; one
    /// when they're equal and it's a multiple of the identity's direction isn't unique).</summary>
    public static List<(double Lambda, double X, double Y)> Eigen(double a, double b, double c, double d)
    {
        var result = new List<(double, double, double)>();
        double tr = a + d, det = a * d - b * c, disc = tr * tr / 4 - det;
        if (!double.IsFinite(disc) || disc < -1e-12) return result;
        double root = Math.Sqrt(Math.Max(0, disc));
        var lambdas = root < 1e-9 ? new[] { tr / 2 } : [tr / 2 + root, tr / 2 - root];
        double scale = Math.Max(1e-300, Math.Max(Math.Max(Math.Abs(a), Math.Abs(b)), Math.Max(Math.Abs(c), Math.Abs(d))));
        foreach (double l in lambdas)
        {
            // (A − λI)v = 0: v from the first row, or the second where the first is all zero.
            double vx = b, vy = l - a;
            if (Math.Abs(vx) < 1e-9 * scale && Math.Abs(vy) < 1e-9 * scale) (vx, vy) = (l - d, c);
            if (Math.Abs(vx) < 1e-9 * scale && Math.Abs(vy) < 1e-9 * scale)
            {
                // A multiple of the identity: every direction is one; the axes stand for them.
                result.Add((l, 1, 0));
                result.Add((l, 0, 1));
                continue;
            }
            double len = Math.Sqrt(vx * vx + vy * vy);
            result.Add((l, vx / len, vy / len));
        }
        return result;
    }
}
