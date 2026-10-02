namespace StudyStash.Core.Rich;

/// <summary>
/// What would look wrong with a plot in the notes, checked by working it out at its sliders' starting values (and at
/// each slider's ends): a curve undefined almost everywhere, flat, or mostly off the plot; numbers too large to read;
/// a slider that changes nothing, or that takes a curve right off the plot; a point outside it; too many curves to tell
/// apart; labels too long. Each problem is a sentence the designer can act on, the way <see cref="DiagramLint"/>'s are.
/// None: it looks right.
/// </summary>
public static class PlotLint
{
    public const int MaxSeries = 6, MaxLegendLabel = 40, Samples = 240;

    public static IReadOnlyList<string> Problems(Plot plot)
    {
        var problems = new List<string>();
        var state = new PlotState(plot);
        var view = state.Home;
        if (plot.X is null && plot.Items.Any(i => i is PlotCurve or PlotHeat))
            problems.Add("it has no x range: give one (x 0 to 10 \"label\") over the values the lecture talks about");
        if (plot.Items.OfType<PlotHeat>().Any() && plot.Y is null)
            problems.Add("its surface has no y range: give both axes (x … to …, y … to …)");
        if (plot.Items.Count(PlotLayout.Legendable) > MaxSeries)
            problems.Add($"more than {MaxSeries} curves: too many to tell apart; keep the ones the lecture compares");
        foreach (var i in plot.Items.Where(PlotLayout.Legendable))
            if ((i.Label?.Written.Length ?? 0) > MaxLegendLabel)
                problems.Add($"the label “{i.Label!.Written}” is long for a legend: a few words, or the formula's own name");
        if (Math.Max(Math.Abs(view.Y0), Math.Abs(view.Y1)) >= 1e7 || (view.Height < 1e-6 && view.Height > 0))
            problems.Add($"its y axis runs from {PlotNumber.Format(view.Y0)} to {PlotNumber.Format(view.Y1)}: rescale it (other units, or a smaller x range) so its numbers are readable");

        foreach (var item in plot.Items) Check(state, item, view, problems);

        // Each slider must change something, and not take what it changes right off the plot at its ends.
        var reads = Reads(plot);
        foreach (var p in plot.Params.Where(p => !p.Name.StartsWith('\u0001')))
        {
            if (!reads.Contains(p.Slot))
            {
                problems.Add($"the slider {p.Name} changes nothing: use it in a formula, or leave it out");
                continue;
            }
            foreach (double end in new[] { p.Min, p.Max })
            {
                state.Set(p, end);
                foreach (var c in plot.Items.OfType<PlotCurve>().Where(c => !c.Flat && c.Body.Reads().Contains(p.Slot)))
                {
                    var (finite, inside, _, _) = Sample(state, c, view);
                    if (finite > Samples / 10 && inside == 0)
                        problems.Add($"with {p.Name} = {PlotNumber.Format(end)}, “{c.Name}” leaves the plot entirely: narrow {p.Name}'s range or widen y");
                }
            }
            state.Reset();
        }
        return problems.Distinct().ToList();
    }

    static void Check(PlotState state, PlotItem item, PlotRange view, List<string> problems)
    {
        state.NewFrame();
        switch (item)
        {
            case PlotCurve { Flat: false } c:
            {
                var (finite, inside, lo, hi) = Sample(state, c, view);
                if (finite < Samples / 20)
                    problems.Add($"“{c.Name}” is undefined almost everywhere from {PlotNumber.Format(view.X0)} to {PlotNumber.Format(view.X1)}: check its formula and the x range");
                else if (hi - lo <= 1e-9 * Math.Max(1, Math.Max(Math.Abs(lo), Math.Abs(hi))) || (hi - lo) < view.Height * 0.01)
                    problems.Add($"“{c.Name}” is flat over the whole x range, so it shows nothing: choose an x range where its shape shows");
                else if (inside < finite * 0.15)
                    problems.Add($"“{c.Name}” is mostly off the plot (from {PlotNumber.Format(lo)} to {PlotNumber.Format(hi)}, but y shows {PlotNumber.Format(view.Y0)} to {PlotNumber.Format(view.Y1)}): change the y range or the x range");
                break;
            }
            case PlotSeries s:
            {
                double from = state.Eval(s.Function.From!), to = state.Eval(s.Function.To!);
                if (to - from > 400) problems.Add($"“{s.Name}” has {to - from + 1:0} terms, too many to read as {s.Style.ToString().ToLowerInvariant()}: show fewer");
                int finite = 0;
                for (double k = Math.Ceiling(from); k <= to && k - from < 400; k++)
                    if (double.IsFinite(state.Term(s, k))) finite++;
                if (finite == 0) problems.Add($"“{s.Name}” is undefined at every whole number from {PlotNumber.Format(from)} to {PlotNumber.Format(to)}: check its formula");
                break;
            }
            case PlotHeat h:
            {
                var values = new List<double>();
                for (int i = 0; i < 24; i++)
                    for (int j = 0; j < 24; j++)
                    {
                        double v = state.At(h, view.X0 + view.Width * (i + 0.5) / 24, view.Y0 + view.Height * (j + 0.5) / 24);
                        if (double.IsFinite(v)) values.Add(v);
                    }
                if (values.Count < 24) problems.Add($"“{h.Name}” is undefined almost everywhere in view: check its formula and the axes");
                else if (values.Max() - values.Min() <= 1e-9 * Math.Max(1, Math.Abs(values.Max()))) problems.Add($"“{h.Name}” is flat across the view, so it shows nothing");
                break;
            }
            case PlotDescent d:
            {
                bool two = d.On.Arity == 2;
                var path = PlotSampler.Descent((x, y) => state.At(d.On, x, y), two, state.Eval(d.X0), two ? state.Eval(d.Y0!) : 0, state.Eval(d.Rate), (int)Math.Round(state.Eval(d.Steps)));
                if (path.Count < 3) problems.Add("the descent stops at once: check its start and its rate");
                else if (two && !path.Take(4).All(p => Inside(view, p.X, p.Y, 0.05)))
                    problems.Add("the descent leaves the plot at its starting rate: start it inside the axes, with a rate that converges (a slider can show divergence)");
                break;
            }
            case PlotPoint p:
            {
                double x = state.Eval(p.X), y = state.Eval(p.Y);
                if (!double.IsFinite(x) || !double.IsFinite(y)) problems.Add($"the point {(p.Label is { } l ? "“" + l.Written + "”" : "")} is undefined");
                else if (!Inside(view, x, y, 0)) problems.Add($"the point {(p.Label is { } l2 ? "“" + l2.Written + "” " : "")}at ({PlotNumber.Format(x)}, {PlotNumber.Format(y)}) is outside the plot");
                break;
            }
            case PlotVLine v:
            {
                double x = state.Eval(v.X);
                if (!double.IsFinite(x) || x < view.X0 || x > view.X1) problems.Add($"the line x = {v.X.Text} is outside the plot");
                break;
            }
            case PlotShade sh:
            {
                double a = state.Eval(sh.From), b = state.Eval(sh.To);
                if (!double.IsFinite(a) || !double.IsFinite(b) || Math.Max(a, b) < view.X0 || Math.Min(a, b) > view.X1)
                    problems.Add("the shaded area is outside the plot");
                break;
            }
            case PlotMatrix m:
            {
                var (a, b, c, d) = state.Matrix(m);
                if (!double.IsFinite(a + b + c + d)) problems.Add("the matrix has an undefined entry");
                else if (Math.Max(Math.Max(Math.Abs(a), Math.Abs(b)), Math.Max(Math.Abs(c), Math.Abs(d))) > 20)
                    problems.Add("the matrix's entries are too large to see its map (keep them under about 5)");
                break;
            }
        }
    }

    static bool Inside(PlotRange v, double x, double y, double slack) =>
        x >= v.X0 - v.Width * slack && x <= v.X1 + v.Width * slack && y >= v.Y0 - v.Height * slack && y <= v.Y1 + v.Height * slack;

    /// <summary>A curve over the view: how many samples are defined, how many fall inside it, and its range.</summary>
    static (int Finite, int Inside, double Lo, double Hi) Sample(PlotState state, PlotCurve c, PlotRange view)
    {
        int finite = 0, inside = 0;
        double lo = double.PositiveInfinity, hi = double.NegativeInfinity;
        state.NewFrame();
        for (int i = 0; i <= Samples; i++)
        {
            double x = view.X0 + view.Width * i / Samples, y = state.At(c, x);
            if (!double.IsFinite(y)) continue;
            finite++;
            lo = Math.Min(lo, y);
            hi = Math.Max(hi, y);
            if (y >= view.Y0 && y <= view.Y1) inside++;
        }
        return (finite, inside, lo, hi);
    }

    /// <summary>Every slot something in the plot reads (so a slider no expression reads is one that does nothing).</summary>
    static HashSet<int> Reads(Plot plot)
    {
        var slots = new HashSet<int>();
        void Add(PlotExpression? e)
        {
            if (e is not null) slots.UnionWith(e.Reads());
        }
        foreach (var i in plot.Items)
        {
            switch (i)
            {
                case PlotCurve c:
                    Add(c.Body);
                    Add(c.From);
                    Add(c.To);
                    break;
                case PlotVLine v:
                    Add(v.X);
                    break;
                case PlotParametric p:
                    Add(p.X);
                    Add(p.Y);
                    Add(p.From);
                    Add(p.To);
                    break;
                case PlotSeries s:
                    Add(s.Body);
                    Add(s.Function.From);
                    Add(s.Function.To);
                    break;
                case PlotPoints pts:
                    foreach (var (x, y) in pts.Points)
                    {
                        Add(x);
                        Add(y);
                    }
                    break;
                case PlotPoint pt:
                    Add(pt.X);
                    Add(pt.Y);
                    break;
                case PlotText t:
                    Add(t.X);
                    Add(t.Y);
                    break;
                case PlotShade sh:
                    Add(sh.From);
                    Add(sh.To);
                    break;
                case PlotTangent tg:
                    Add(tg.At);
                    break;
                case PlotSecant sc:
                    Add(sc.A);
                    Add(sc.B);
                    break;
                case PlotVector v:
                    Add(v.X0);
                    Add(v.Y0);
                    Add(v.X);
                    Add(v.Y);
                    break;
                case PlotField f:
                    Add(f.P);
                    Add(f.Q);
                    break;
                case PlotHeat h:
                    Add(h.Body);
                    break;
                case PlotDescent d:
                    Add(d.X0);
                    Add(d.Y0);
                    Add(d.Rate);
                    Add(d.Steps);
                    break;
                case PlotMatrix m:
                    foreach (var e in m.Entries) Add(e);
                    break;
            }
        }
        return slots;
    }
}
