namespace StudyStash.Core.Rich;

/// <summary>A part of the plane a plot shows: x from <see cref="X0"/> to <see cref="X1"/>, y from <see cref="Y0"/> to
/// <see cref="Y1"/>.</summary>
public readonly record struct PlotRange(double X0, double X1, double Y0, double Y1)
{
    public double Width => X1 - X0;
    public double Height => Y1 - Y0;
    public bool Valid => double.IsFinite(X0) && double.IsFinite(X1) && double.IsFinite(Y0) && double.IsFinite(Y1) && X1 > X0 && Y1 > Y0;

    /// <summary>Zoomed by <paramref name="factor"/> (above 1 closer in) about the point (<paramref name="cx"/>,
    /// <paramref name="cy"/>), which stays where it is.</summary>
    public PlotRange Zoom(double factor, double cx, double cy, bool xOnly = false) => new(
        cx - (cx - X0) / factor, cx + (X1 - cx) / factor,
        xOnly ? Y0 : cy - (cy - Y0) / factor, xOnly ? Y1 : cy + (Y1 - cy) / factor);

    public PlotRange Pan(double dx, double dy) => new(X0 + dx, X1 + dx, Y0 + dy, Y1 + dy);
}

/// <summary>
/// A plot being looked at: where each slider is, which curves are hidden, and the part of the plane in view. Everything
/// about the plot that changes as the student plays with it lives here; the <see cref="Plot"/> itself never changes.
/// One thread at a time (the plot's sequences remember their terms).
/// </summary>
public sealed class PlotState
{
    public PlotState(Plot plot)
    {
        Plot = plot;
        Env = new PlotEnv(plot.Scope.SlotCount);
        Values = plot.Params.Select(p => p.Default).ToArray();
        for (int i = 0; i < plot.Params.Count; i++) Env.Slots[plot.Params[i].Slot] = Values[i];
        foreach (var f in plot.Scope.Functions.Values) f.Reset();
        Env.NewFrame();
        Home = HomeWindow();
        View = Home;
        Env.NewFrame();
    }

    /// <summary>A new frame's budget: what's evaluated from here (a drawing, a hover) may spend
    /// <see cref="PlotEnv.FrameSteps"/> steps.</summary>
    public void NewFrame() => Env.NewFrame();

    public Plot Plot { get; }
    public PlotEnv Env { get; }

    /// <summary>Each slider's value, in the order of <see cref="Plot.Params"/>.</summary>
    public double[] Values { get; }

    /// <summary>The things the student has hidden (by clicking their key in the legend).</summary>
    public HashSet<PlotItem> Hidden { get; } = [];

    /// <summary>What's in view now, and what the plot shows to begin with (its axes, or what it draws).</summary>
    public PlotRange View { get; set; }
    public PlotRange Home { get; private set; }

    /// <summary>Changes whenever a slider does (what's drawn for the old values is stale).</summary>
    public int Generation => Env.Generation;

    public bool AtDefaults => Plot.Params.Select((p, i) => Math.Abs(Values[i] - p.Default) < 1e-12).All(x => x);

    public bool Zoomed => !Same(View, Home);

    static bool Same(PlotRange a, PlotRange b) =>
        Math.Abs(a.X0 - b.X0) <= 1e-9 * Math.Max(1, a.Width) && Math.Abs(a.X1 - b.X1) <= 1e-9 * Math.Max(1, a.Width)
        && Math.Abs(a.Y0 - b.Y0) <= 1e-9 * Math.Max(1, a.Height) && Math.Abs(a.Y1 - b.Y1) <= 1e-9 * Math.Max(1, a.Height);

    /// <summary>Sets a slider (snapped to its range and step); true when it changed.</summary>
    public bool Set(int index, double value)
    {
        var p = Plot.Params[index];
        double v = p.Snap(value);
        if (Values[index] == v) return false;
        Values[index] = v;
        Env.Slots[p.Slot] = v;
        Env.Generation++;
        return true;
    }

    public bool Set(PlotParam p, double value) => Set(Plot.Params.IndexOf(p), value);

    /// <summary>Every slider back where it started.</summary>
    public bool Reset()
    {
        bool changed = false;
        for (int i = 0; i < Values.Length; i++) changed |= Set(i, Plot.Params[i].Default);
        return changed;
    }

    public double Value(PlotParam p) => Values[Plot.Params.IndexOf(p)];

    /// <summary>An expression's value with the sliders as they are now.</summary>
    public double Eval(PlotExpression e) => e.Eval(Env);

    /// <summary>A curve's value at <paramref name="x"/> (NaN off its domain).</summary>
    public double At(PlotCurve c, double x)
    {
        if (c.From is { } f && c.To is { } t && (x < Eval(f) - 1e-12 || x > Eval(t) + 1e-12)) return double.NaN;
        return Call(c.Function, c.Body, x);
    }

    /// <summary>A one-argument function's body at <paramref name="x"/>.</summary>
    internal double Call(PlotFunction f, PlotExpression body, double x)
    {
        int slot = f.Args[0];
        double saved = Env.Slots[slot];
        Env.Slots[slot] = x;
        double v = body.Eval(Env);
        Env.Slots[slot] = saved;
        return v;
    }

    /// <summary>A sequence's term at whole number <paramref name="k"/>.</summary>
    public double Term(PlotSeries s, double k)
    {
        Env.Budget = Math.Min(PlotEnv.MaxSteps, Env.FrameLeft);
        return s.Function.Term(Env, k);
    }

    /// <summary>A surface's value at (<paramref name="x"/>, <paramref name="y"/>).</summary>
    public double At(PlotHeat h, double x, double y)
    {
        int a = h.Function.Args[0], b = h.Function.Args[1];
        double sa = Env.Slots[a], sb = Env.Slots[b];
        Env.Slots[a] = x;
        Env.Slots[b] = y;
        double v = h.Body.Eval(Env);
        Env.Slots[a] = sa;
        Env.Slots[b] = sb;
        return v;
    }

    /// <summary>The value of the function a descent runs on.</summary>
    internal double At(PlotFunction f, double x, double y)
    {
        if (f.Body is null) return double.NaN;
        int a = f.Args[0];
        double sa = Env.Slots[a];
        Env.Slots[a] = x;
        double sb = 0;
        if (f.Arity == 2)
        {
            sb = Env.Slots[f.Args[1]];
            Env.Slots[f.Args[1]] = y;
        }
        long start = Math.Min(PlotEnv.MaxSteps, Env.FrameLeft);
        Env.Budget = start;
        double v = f.Body.Eval(Env);
        long used = 1 + start - Math.Max(0, Env.Budget);
        Env.Spent += used;
        Env.FrameLeft -= used;
        Env.Slots[a] = sa;
        if (f.Arity == 2) Env.Slots[f.Args[1]] = sb;
        return v;
    }

    /// <summary>The matrix the plot shows now (moved part of the way from the identity, while its morph slider
    /// says so).</summary>
    public (double A, double B, double C, double D) Matrix(PlotMatrix m)
    {
        double a = Eval(m.Entries[0]), b = Eval(m.Entries[1]), c = Eval(m.Entries[2]), d = Eval(m.Entries[3]);
        if (m.MorphParam is { } morph)
        {
            double t = Value(morph);
            a = 1 + t * (a - 1);
            b *= t;
            c *= t;
            d = 1 + t * (d - 1);
        }
        return (a, b, c, d);
    }

    // --- the part of the plane a plot opens on -------------------------------------------------------------------------

    /// <summary>
    /// What the plot shows to begin with: its axes where it gives them; otherwise what it draws, at the sliders'
    /// starting values: x over its curves' domains, its sequences, points and vectors (−10 to 10 when nothing says);
    /// y over what its curves and points reach there (most of it, so one spike doesn't flatten the rest), with zero
    /// when it's near, and a little room above and below. A matrix's plane is square round where the unit square goes.
    /// </summary>
    PlotRange HomeWindow()
    {
        var p = Plot;
        double x0, x1;
        if (p.X is { } ax) (x0, x1) = (ax.From, ax.To);
        else
        {
            var xs = new List<double>();
            foreach (var item in p.Items)
            {
                switch (item)
                {
                    case PlotCurve { From: { } f, To: { } t }:
                        xs.Add(Eval(f));
                        xs.Add(Eval(t));
                        break;
                    case PlotSeries s:
                        xs.Add(Eval(s.Function.From!) - 0.6);
                        xs.Add(Eval(s.Function.To!) + 0.6);
                        break;
                    case PlotPoints pts:
                        foreach (var (x, _) in pts.Points) xs.Add(Eval(x));
                        break;
                    case PlotParametric par:
                        foreach (var (x, _) in ParametricSamples(par, 200)) xs.Add(x);
                        break;
                    case PlotVector v:
                        xs.Add(v.X0 is { } vx0 ? Eval(vx0) : 0);
                        xs.Add(Eval(v.X));
                        break;
                    case PlotMatrix m:
                        return MatrixWindow(m);
                }
            }
            xs.RemoveAll(v => !double.IsFinite(v));
            if (xs.Count >= 2 && xs.Max() > xs.Min())
            {
                double lo = xs.Min(), hi = xs.Max(), pad = p.Items.Any(i => i is PlotPoints or PlotParametric or PlotVector) ? 0.08 * (hi - lo) : 0;
                (x0, x1) = (lo - pad, hi + pad);
                if (p.Items.Any(i => i is PlotVector) && x0 > 0) x0 = -0.08 * (hi - lo);
            }
            else (x0, x1) = (-10, 10);
        }
        if (p.Y is { } ay) return new PlotRange(x0, x1, ay.From, ay.To);
        var (y0, y1) = AutoY(x0, x1);
        return new PlotRange(x0, x1, y0, y1);
    }

    PlotRange MatrixWindow(PlotMatrix m)
    {
        var (a, b, c, d) = Matrix(m);
        double r = new[] { 1.0, Math.Abs(a), Math.Abs(b), Math.Abs(c), Math.Abs(d), Math.Abs(a + b), Math.Abs(c + d) }.Where(double.IsFinite).Max();
        r = Math.Min(Math.Max(2, Math.Ceiling(r * 1.3)), 50);
        var y = Plot.Y is { } ay ? (ay.From, ay.To) : (-r, r);
        var x = Plot.X is { } ax ? (ax.From, ax.To) : (-r, r);
        return new PlotRange(x.Item1, x.Item2, y.Item1, y.Item2);
    }

    (double, double) AutoY(double x0, double x1)
    {
        var ys = new List<double>();
        bool bars = false, zero = false;
        const int n = 400;
        foreach (var item in Plot.Items)
        {
            switch (item)
            {
                case PlotCurve c:
                    for (int i = 0; i <= n; i++)
                    {
                        double v = At(c, x0 + (x1 - x0) * i / n);
                        if (double.IsFinite(v)) ys.Add(v);
                    }
                    break;
                case PlotSeries s:
                    bars |= s.Style is SeriesStyle.Bars or SeriesStyle.Stems;
                    double from = Math.Max(Math.Ceiling(x0), Eval(s.Function.From!)), to = Math.Min(Math.Floor(x1), Eval(s.Function.To!));
                    for (double k = from; k <= to && k - from < 2000; k++)
                    {
                        double v = Term(s, k);
                        if (double.IsFinite(v)) ys.Add(v);
                    }
                    break;
                case PlotPoints pts:
                    foreach (var (_, y) in pts.Points) ys.Add(Eval(y));
                    break;
                case PlotPoint pt:
                    ys.Add(Eval(pt.Y));
                    break;
                case PlotParametric par:
                    foreach (var (_, y) in ParametricSamples(par, 200)) ys.Add(y);
                    break;
                case PlotVector v:
                    ys.Add(v.Y0 is { } vy0 ? Eval(vy0) : 0);
                    ys.Add(Eval(v.Y));
                    zero = true;
                    break;
                case PlotShade:
                    zero = true;
                    break;
            }
        }
        ys.RemoveAll(v => !double.IsFinite(v));
        if (ys.Count == 0) return (-10, 10);
        ys.Sort();
        // Most of what's drawn: a curve that shoots off (1/x near 0, tan at its asymptotes) doesn't flatten the rest.
        double lo = ys[(int)Math.Floor((ys.Count - 1) * 0.01)], hi = ys[(int)Math.Ceiling((ys.Count - 1) * 0.99)];
        double spread = ys[^1] - ys[0];
        if (spread > 0 && (hi - lo) > 0.5 * spread)
        {
            lo = ys[0];
            hi = ys[^1];
        }
        if (bars || zero || (lo > 0 && lo < 0.6 * (hi - lo))) lo = Math.Min(lo, 0);
        if (hi < 0 && -hi < 0.6 * (hi - lo)) hi = 0;
        if (hi - lo < 1e-12 * Math.Max(1, Math.Abs(hi)))
        {
            double c = lo, r = Math.Max(1, Math.Abs(c) * 0.5);
            return (c - r, c + r);
        }
        double pad = 0.07 * (hi - lo);
        return (lo == 0 && bars ? 0 : lo - pad, hi + pad);
    }

    /// <summary>A parametric curve at <paramref name="n"/> + 1 evenly spaced values of its variable.</summary>
    internal IEnumerable<(double X, double Y)> ParametricSamples(PlotParametric par, int n)
    {
        double a = Eval(par.From), b = Eval(par.To);
        if (!double.IsFinite(a) || !double.IsFinite(b) || b <= a) yield break;
        double saved = Env.Slots[par.Slot];
        try
        {
            for (int i = 0; i <= n; i++)
            {
                Env.Slots[par.Slot] = a + (b - a) * i / n;
                double x = par.X.Eval(Env), y = par.Y.Eval(Env);
                if (double.IsFinite(x) && double.IsFinite(y)) yield return (x, y);
            }
        }
        finally
        {
            Env.Slots[par.Slot] = saved;
        }
    }
}
