using System.Globalization;

namespace StudyStash.Core.Rich;

/// <summary>What a mark of a plot is coloured by: one of the series colours (in a fixed order), or a role the look
/// colours (text, quiet text, the grid, the axes, the paper behind a label, the lines over a heat map).</summary>
public enum PlotRole { Series, Ink, Ink2, Ink3, Grid, Axis, Surface, HeatLine }

public readonly record struct PlotPaint(PlotRole Role, int Index = 0)
{
    public static PlotPaint Series(int i) => new(PlotRole.Series, i);
    public static readonly PlotPaint Ink = new(PlotRole.Ink), Ink2 = new(PlotRole.Ink2), Ink3 = new(PlotRole.Ink3),
        Grid = new(PlotRole.Grid), Axis = new(PlotRole.Axis), Surface = new(PlotRole.Surface), HeatLine = new(PlotRole.HeatLine);
}

public readonly record struct PlotRect(double X, double Y, double W, double H)
{
    public double Right => X + W;
    public double Bottom => Y + H;
    public bool Overlaps(PlotRect o, double gap = 0) => X < o.Right + gap && o.X < Right + gap && Y < o.Bottom + gap && o.Y < Bottom + gap;
    public bool Within(PlotRect o) => X >= o.X - 0.5 && Y >= o.Y - 0.5 && Right <= o.Right + 0.5 && Bottom <= o.Bottom + 0.5;
    public bool Contains(PlotPt p) => p.X >= X && p.X <= Right && p.Y >= Y && p.Y <= Bottom;
}

/// <summary>A mark of a plot, in the scene's pixels; <see cref="Item"/> is what it draws (null for the axes).</summary>
public abstract record PlotMark(PlotItem? Item);

/// <summary>Lines through points (a curve's pieces, grid lines, a contour), <see cref="Width"/> px wide.</summary>
public sealed record PlotLines(IReadOnlyList<IReadOnlyList<PlotPt>> Lines, PlotPaint Paint, double Width, PlotDash Dash, double Opacity, PlotItem? Item) : PlotMark(Item);

/// <summary>Filled shapes (a shaded area, the unit square's image).</summary>
public sealed record PlotArea(IReadOnlyList<IReadOnlyList<PlotPt>> Polygons, PlotPaint Paint, double Opacity, PlotItem? Item) : PlotMark(Item);

/// <summary>A dot, ringed in the paper's colour so it reads where it crosses a line; hollow, it's a ring itself.</summary>
public sealed record PlotDot(PlotPt At, double Radius, PlotPaint Paint, PlotItem? Item, bool Hollow = false) : PlotMark(Item);

/// <summary>An arrow, its head at <see cref="To"/>.</summary>
public sealed record PlotArrow(PlotPt From, PlotPt To, PlotPaint Paint, double Width, PlotItem? Item) : PlotMark(Item);

/// <summary>A bar, rounded at its data end (its top, or its bottom when it hangs below zero).</summary>
public sealed record PlotBar(PlotRect Box, bool Up, PlotPaint Paint, PlotItem? Item) : PlotMark(Item);

/// <summary>Words in a box (its top left), on a patch of paper when <see cref="Backed"/>.</summary>
public sealed record PlotWords(PlotRect Box, string Text, double Size, bool Bold, PlotPaint Paint, bool Backed, PlotItem? Item) : PlotMark(Item);

/// <summary>A heat map: <see cref="Columns"/> × <see cref="Rows"/> cells over <see cref="Box"/>, row 0 at the top,
/// each a shade from 0 (low) to 1 (high), NaN where the surface is undefined.</summary>
public sealed record PlotHeatMap(PlotRect Box, int Columns, int Rows, float[] Shade, PlotItem? Item) : PlotMark(Item);

public enum PlotKeyShape { Line, Bar, Dot, Ramp }

/// <summary>An entry in the legend: what it shows, and its box (a click on it shows or hides it).</summary>
public sealed record PlotKey(PlotRect Box, PlotItem Item, PlotPaint Paint, PlotDash Dash, string Text, PlotKeyShape Shape, bool Off);

/// <summary>A point the student can drag: dragging moves the sliders it's made of.</summary>
public sealed record PlotHandle(PlotPt At, PlotParam X, PlotParam? Y, PlotItem Item);

/// <summary>A plot laid out at a width: what to draw (clipped to the plot's area, then on top of it), the legend, the
/// points that can be dragged, and where the plane lands.</summary>
public sealed class PlotScene
{
    public double Width { get; init; }
    public double Height { get; internal set; }
    public PlotMap Map { get; init; }
    public PlotRect Area => new(Map.Left, Map.Top, Map.Width, Map.Height);
    /// <summary>Drawn first, clipped to <see cref="Area"/>.</summary>
    public List<PlotMark> Data { get; } = [];
    /// <summary>Drawn over the data, unclipped: ticks, labels, the legend's words.</summary>
    public List<PlotMark> Front { get; } = [];
    public List<PlotKey> Keys { get; } = [];
    public List<PlotHandle> Handles { get; } = [];
    /// <summary>The colour each thing was given (for the hover's dots and keys).</summary>
    public Dictionary<PlotItem, int> Colours { get; } = [];
    /// <summary>Whether the plot has more to it than one frame could work out (shown coarser).</summary>
    public bool OutOfTime { get; internal set; }
}

/// <summary>How a plot is to be drawn now: its title shown or not, what's hidden, how finely (coarser while a slider
/// is dragged), and whether its curves are hidden for the student to predict them.</summary>
public sealed record PlotLook
{
    public bool Title { get; init; } = true;
    public IReadOnlySet<PlotItem> Hidden { get; init; } = new HashSet<PlotItem>();
    public double Detail { get; init; } = 1;
    /// <summary>The curves are hidden (the student is predicting them); their keys stay.</summary>
    public bool Predicting { get; init; }
}

/// <summary>
/// Lays a plot out at a width, in the notes' calm style: a title (unless the note's bold line above names it), a
/// legend for two or more series and a label beside a lone one, quiet grid lines at "nice" numbers (1, 2 or 5 times
/// a power of ten, or multiples of π), the axes' labels, then what the plot draws, sampled where it bends, in the
/// series colours in a fixed order, each label placed where it doesn't cover another. The same scene is drawn on
/// screen, on paper and as an SVG.
/// </summary>
public static class PlotLayout
{
    public const double TickSize = 11, LabelSize = 12, TitleSize = 13.5, KeySize = 12;
    const double Right = 14, LineGap = 1.35;

    /// <summary>The height a plot's area takes for its width (when its units aren't equal).</summary>
    public static double AreaHeight(double width) => Math.Clamp(Math.Round(width * 0.56), 200, 420);

    /// <summary>The size words are measured at: (text, size, bold) to width in px.</summary>
    public delegate double Measure(string text, double size, bool bold);

    public static PlotScene Build(PlotState state, double width, Measure measure, PlotLook? look = null)
    {
        look ??= new PlotLook();
        var plot = state.Plot;
        state.NewFrame();
        width = Math.Max(240, width);
        var colours = Colours(plot);
        var hidden = new HashSet<PlotItem>(look.Hidden);
        if (look.Predicting)
            foreach (var i in plot.Items.Where(i => i is PlotCurve { Flat: false } or PlotParametric or PlotSeries or PlotDescent or PlotTangent or PlotSecant or PlotShade)) hidden.Add(i);

        // Above the plot's area: its title, its legend, its y axis's label.
        double y = 0;
        var front = new List<PlotMark>();
        if (look.Title && plot.Title is { } title)
        {
            double h = TitleSize * LineGap;
            front.Add(new PlotWords(new PlotRect(0, y, Math.Min(width, measure(title, TitleSize, true)), h), title, TitleSize, true, PlotPaint.Ink, false, null));
            y += h + 6;
        }
        var keys = Legend(plot, state, colours, hidden, width, measure, ref y);
        string? yLabel = plot.Y?.Label ?? (plot.Items.OfType<PlotHeat>().FirstOrDefault() is { } heat ? heat.Variables[1] : null);
        if (yLabel is not null)
        {
            double h = LabelSize * LineGap;
            front.Add(new PlotWords(new PlotRect(0, y, measure(yLabel, LabelSize, false), h), yLabel, LabelSize, false, PlotPaint.Ink2, false, null));
            y += h + 4;
        }
        else y += 6;
        double top = y;

        // The area: as tall as its width says, or as its units do when they're equal.
        var view = state.View;
        double left = 40;
        double areaW = width - left - Right, areaH;
        List<(double V, string Label)> yTicks = [];
        for (int pass = 0; pass < 2; pass++)
        {
            areaW = width - left - Right;
            if (plot.Square)
            {
                areaH = Math.Round(areaW * view.Height / view.Width);
                double fitted = Math.Clamp(areaH, 200, Math.Max(200, Math.Min(560, areaW * 1.1)));
                if (Math.Abs(fitted - areaH) > 0.5)
                {
                    // Keep the units equal: the window grows on the axis that has room to spare.
                    areaH = fitted;
                    double unit = areaW / view.Width, wantH = areaH / unit;
                    if (wantH > view.Height)
                    {
                        double c = (view.Y0 + view.Y1) / 2;
                        view = view with { Y0 = c - wantH / 2, Y1 = c + wantH / 2 };
                    }
                    else
                    {
                        double unitY = areaH / view.Height, wantW = areaW / unitY, c = (view.X0 + view.X1) / 2;
                        view = view with { X0 = c - wantW / 2, X1 = c + wantW / 2 };
                    }
                }
            }
            else areaH = AreaHeight(areaW);
            yTicks = Ticks(view.Y0, view.Y1, areaH, 34, plot.Y?.PiTicks == true);
            double widest = yTicks.Count == 0 ? 0 : yTicks.Max(t => measure(t.Label, TickSize, false));
            double need = Math.Max(26, Math.Ceiling(widest) + 9);
            if (Math.Abs(need - left) < 1) break;
            left = need;
        }
        areaW = width - left - Right;
        areaH = plot.Square ? Math.Round(areaW * view.Height / view.Width) : AreaHeight(areaW);
        var map = new PlotMap(view, left, top, areaW, areaH);
        var xTicks = Ticks(view.X0, view.X1, areaW, 70, plot.X?.PiTicks == true);

        var scene = new PlotScene { Width = width, Map = map, Height = 0 };
        foreach (var (item, c) in colours) scene.Colours[item] = c;
        var data = scene.Data;
        var taken = new List<PlotRect>();
        var area = scene.Area;

        // The heat map first (the grid would only be noise over it), then the grid, the axes.
        var heatFill = plot.Items.OfType<PlotHeat>().FirstOrDefault(h => h.Fill && !hidden.Contains(h));
        foreach (var h in plot.Items.OfType<PlotHeat>().Where(h => !hidden.Contains(h)))
            Heat(state, h, map, data, colours, look.Detail);
        if (heatFill is null)
        {
            var grid = new List<IReadOnlyList<PlotPt>>();
            foreach (var (v, _) in xTicks) grid.Add([new(Snap(map.ScreenX(v)), map.Top), new(Snap(map.ScreenX(v)), map.Bottom)]);
            foreach (var (v, _) in yTicks) grid.Add([new(map.Left, Snap(map.ScreenY(v))), new(map.Right, Snap(map.ScreenY(v)))]);
            data.Add(new PlotLines(grid, PlotPaint.Grid, 1, PlotDash.Solid, 1, null));
        }
        var axes = new List<IReadOnlyList<PlotPt>>();
        if (view.Y0 <= 0 && view.Y1 >= 0) axes.Add([new(map.Left, Snap(map.ScreenY(0))), new(map.Right, Snap(map.ScreenY(0)))]);
        bool bars = plot.Items.Any(i => i is PlotSeries { Style: SeriesStyle.Bars or SeriesStyle.Stems });
        if (view.X0 <= 0 && view.X1 >= 0 && !bars) axes.Add([new(Snap(map.ScreenX(0)), map.Top), new(Snap(map.ScreenX(0)), map.Bottom)]);
        if (axes.Count > 0) data.Add(new PlotLines(axes, heatFill is null ? PlotPaint.Axis : PlotPaint.HeatLine, 1, PlotDash.Solid, heatFill is null ? 1 : 0.6, null));

        // The ticks' numbers, under and beside the area.
        foreach (var (v, label) in yTicks)
        {
            double w = measure(label, TickSize, false), h = TickSize * LineGap, sy = map.ScreenY(v);
            var box = new PlotRect(left - 7 - w, sy - h / 2, w, h);
            if (box.Y < top - h / 2 - 1 || box.Bottom > map.Bottom + h / 2 + 1) continue;
            front.Add(new PlotWords(box, label, TickSize, false, PlotPaint.Ink3, false, null));
        }
        double lastRight = double.NegativeInfinity;
        foreach (var (v, label) in xTicks)
        {
            double w = measure(label, TickSize, false), h = TickSize * LineGap;
            var box = new PlotRect(Math.Clamp(map.ScreenX(v) - w / 2, 0, width - w), map.Bottom + 5, w, h);
            if (box.X < lastRight + 6) continue;
            lastRight = box.Right;
            front.Add(new PlotWords(box, label, TickSize, false, PlotPaint.Ink3, false, null));
        }
        double bottom = map.Bottom + 5 + TickSize * LineGap;
        string? xLabel = plot.X?.Label ?? (plot.Items.OfType<PlotHeat>().FirstOrDefault() is { } hx ? hx.Variables[0] : null);
        if (xLabel is not null)
        {
            double w = measure(xLabel, LabelSize, false), h = LabelSize * LineGap;
            var box = new PlotRect(Math.Max(0, map.Right - w), bottom + 3, w, h);
            front.Add(new PlotWords(box, xLabel, LabelSize, false, PlotPaint.Ink2, false, null));
            bottom = box.Bottom;
        }
        foreach (var m in front) if (m is PlotWords pw) taken.Add(pw.Box);

        // What the plot draws, back to front.
        var labels = new List<Action>();
        foreach (var m in plot.Items.OfType<PlotMatrix>()) Matrix(state, m, map, data, front, taken, labels, measure);
        foreach (var f in plot.Items.OfType<PlotField>().Where(f => !hidden.Contains(f))) Field(state, f, map, data, colours);
        foreach (var s in plot.Items.OfType<PlotShade>().Where(s => !hidden.Contains(s) && !hidden.Contains(s.Under)))
            Shade(state, s, map, data, front, taken, labels, measure, colours);
        foreach (var item in plot.Items.Where(i => !hidden.Contains(i)))
            if (item is PlotCurve { Flat: true } or PlotVLine) Reference(state, item, map, data, front, taken, labels, measure, colours);
        foreach (var item in plot.Items.Where(i => !hidden.Contains(i)))
        {
            switch (item)
            {
                case PlotCurve { Flat: false } c:
                    Curve(state, c, map, data, colours, look.Detail);
                    break;
                case PlotParametric p:
                    data.Add(new PlotLines(PlotSampler.Parametric(t => Param(state, p, t), state.Eval(p.From), state.Eval(p.To), map, state.Env, look.Detail)
                        .Cast<IReadOnlyList<PlotPt>>().ToList(), PlotPaint.Series(colours[p]), 2, p.Dash, 1, p));
                    break;
                case PlotSeries s:
                    Series(state, s, map, data, colours);
                    break;
                case PlotPoints pts:
                    foreach (var (px, py) in pts.Points)
                    {
                        var at = map.Screen(state.Eval(px), state.Eval(py));
                        if (double.IsFinite(at.X) && double.IsFinite(at.Y)) data.Add(new PlotDot(at, 4, PlotPaint.Series(colours[pts]), pts));
                    }
                    break;
            }
        }
        foreach (var item in plot.Items.Where(i => !hidden.Contains(i)))
            if (item is PlotTangent or PlotSecant) Touching(state, item, map, data, front, taken, labels, measure, colours, scene.Handles);
        foreach (var d in plot.Items.OfType<PlotDescent>().Where(d => !hidden.Contains(d))) Descent(state, d, map, data, colours, scene.Handles);
        foreach (var v in plot.Items.OfType<PlotVector>().Where(v => !hidden.Contains(v))) Vector(state, v, map, data, front, taken, labels, measure, colours);
        foreach (var item in plot.Items.Where(i => !hidden.Contains(i)))
            if (item is PlotPoint or PlotText) Marked(state, item, map, data, front, taken, labels, measure, scene.Handles);

        // A lone curve is named beside itself; two or more series by the legend.
        var lone = plot.Items.Where(Legendable).ToList();
        if (keys.Count == 0 && lone is [var only and (PlotCurve or PlotParametric)] && only.Label is { } named && !hidden.Contains(only)
            && !Same(named.Written, plot.Y?.Label))
            labels.Add(() => Beside(state, only, data, map, front, taken, measure));
        obstacles = Obstacles(data);
        foreach (var place in labels) place();
        obstacles = [];

        foreach (var k in keys)
        {
            scene.Keys.Add(k);
            front.Add(new PlotWords(new PlotRect(k.Box.X + 24, k.Box.Y, k.Box.W - 24, k.Box.H), k.Text, KeySize, false, k.Off ? PlotPaint.Ink3 : PlotPaint.Ink2, false, k.Item));
        }
        scene.Front.AddRange(front);
        scene.Height = Math.Ceiling(bottom + 2);
        scene.OutOfTime = state.Env.FrameLeft <= 0;
        return scene;
    }

    static double Snap(double v) => Math.Round(v - 0.5) + 0.5;

    static bool Same(string a, string? b) => b is not null && string.Equals(a.Replace(" ", ""), b.Replace(" ", ""), StringComparison.OrdinalIgnoreCase);

    /// <summary>The points labels keep off: every few pixels along what's drawn (curves, their tangents, a descent's
    /// path, a shade's edges, the arrows), sorted left to right. Laying out is one thread at a time per scene.</summary>
    [ThreadStatic] static List<PlotPt>? obstacleList;

    static List<PlotPt> obstacles
    {
        get => obstacleList ??= [];
        set => obstacleList = value;
    }

    static List<PlotPt> Obstacles(List<PlotMark> data)
    {
        var pts = new List<PlotPt>();
        void Along(PlotPt a, PlotPt b)
        {
            double len = Math.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y));
            int n = (int)Math.Min(400, Math.Ceiling(len / 4));
            for (int i = 0; i <= n; i++) pts.Add(new PlotPt(a.X + (b.X - a.X) * i / Math.Max(1, n), a.Y + (b.Y - a.Y) * i / Math.Max(1, n)));
        }
        foreach (var m in data)
        {
            switch (m)
            {
                case PlotLines { Item: not null and not PlotMatrix and not PlotHeat } l:
                    foreach (var line in l.Lines)
                        for (int i = 0; i + 1 < line.Count; i++) Along(line[i], line[i + 1]);
                    break;
                case PlotArrow a:
                    Along(a.From, a.To);
                    break;
                case PlotDot d:
                    pts.Add(d.At);
                    break;
                case PlotBar b:
                    Along(new PlotPt(b.Box.X, b.Box.Y), new PlotPt(b.Box.Right, b.Box.Y));
                    break;
            }
        }
        pts.Sort((a, b) => a.X.CompareTo(b.X));
        return pts;
    }

    /// <summary>Whether a box (grown by a couple of pixels) covers any of the points labels keep off.</summary>
    static bool Covers(PlotRect r)
    {
        var pts = obstacles;
        if (pts.Count == 0) return false;
        double x0 = r.X - 2, x1 = r.Right + 2, y0 = r.Y - 2, y1 = r.Bottom + 2;
        int lo = 0, hi = pts.Count;
        while (lo < hi)
        {
            int mid = (lo + hi) / 2;
            if (pts[mid].X < x0) lo = mid + 1;
            else hi = mid;
        }
        for (int i = lo; i < pts.Count && pts[i].X <= x1; i++)
            if (pts[i].Y >= y0 && pts[i].Y <= y1) return true;
        return false;
    }

    /// <summary>Whether a thing goes in the legend: a curve, a sequence, data, a descent's path, a surface.</summary>
    public static bool Legendable(PlotItem i) => i is PlotCurve { Flat: false } or PlotParametric or PlotSeries or PlotPoints or PlotDescent
        || (i is PlotHeat { Fill: true } && i.Label is not null);

    /// <summary>The colour each thing is drawn in: the one it asks for, or the next series colour in turn (a flat
    /// reference line or a vertical one is grey; a shade takes its curve's colour; a point is ink).</summary>
    public static Dictionary<PlotItem, int> Colours(Plot plot)
    {
        var colours = new Dictionary<PlotItem, int>();
        // Over a heat map (blue, pale to deep), the series start at orange.
        int next = plot.Items.Any(i => i is PlotHeat { Fill: true }) ? 1 : 0;
        int Next() => next++ % 5;
        foreach (var i in plot.Items)
        {
            if (i.Colour is int c)
            {
                colours[i] = c;
                if (c == next % 5 && i is PlotCurve { Flat: false } or PlotParametric or PlotSeries or PlotPoints or PlotDescent or PlotVector) next++;
                continue;
            }
            colours[i] = i switch
            {
                PlotCurve { Flat: true } or PlotVLine => -2,
                PlotCurve or PlotParametric or PlotSeries or PlotPoints or PlotVector or PlotTangent or PlotSecant or PlotDescent => Next(),
                PlotPoint or PlotText => -1,
                _ => 0,
            };
        }
        foreach (var s in plot.Items.OfType<PlotShade>().Where(s => s.Colour is null)) colours[s] = colours[s.Under];
        foreach (var h in plot.Items.OfType<PlotHeat>().Where(h => !h.Fill && h.Colour is null)) colours[h] = Next();
        return colours;
    }

    /// <summary>A series colour, or a role for the greys.</summary>
    public static PlotPaint Paint(int colour) => colour switch
    {
        -1 => PlotPaint.Ink,
        -2 => PlotPaint.Ink3,
        _ => PlotPaint.Series(colour),
    };

    // --- the legend -----------------------------------------------------------------------------------------------------

    static List<PlotKey> Legend(Plot plot, PlotState state, Dictionary<PlotItem, int> colours, HashSet<PlotItem> hidden, double width, Measure measure, ref double y)
    {
        var keys = new List<PlotKey>();
        var items = plot.Items.Where(Legendable).ToList();
        if (items.Count < 2) return keys;
        double x = 0, h = KeySize * LineGap, rowY = y;
        foreach (var i in items)
        {
            string text = i.Label?.Text(state.Env) ?? i.Name;
            double w = 24 + measure(text, KeySize, false);
            if (x > 0 && x + w > width)
            {
                x = 0;
                rowY += h + 4;
            }
            var shape = i switch
            {
                PlotSeries { Style: SeriesStyle.Bars } => PlotKeyShape.Bar,
                PlotSeries { Style: SeriesStyle.Dots or SeriesStyle.Stems } or PlotPoints => PlotKeyShape.Dot,
                PlotHeat => PlotKeyShape.Ramp,
                _ => PlotKeyShape.Line,
            };
            keys.Add(new PlotKey(new PlotRect(x, rowY, w, h), i, Paint(colours[i]), i.Dash, text, shape, hidden.Contains(i) && !(i is PlotHeat)));
            x += w + 18;
        }
        y = rowY + h + 8;
        return keys;
    }

    // --- ticks ---------------------------------------------------------------------------------------------------------

    /// <summary>Tick values from <paramref name="lo"/> to <paramref name="hi"/> about <paramref name="spacing"/> px
    /// apart over <paramref name="pixels"/>: 1, 2 or 5 times a power of ten (or multiples of π), each written the same
    /// way (as many decimals as the step needs, a real minus sign, thousands grouped).</summary>
    public static List<(double V, string Label)> Ticks(double lo, double hi, double pixels, double spacing, bool pi = false)
    {
        var ticks = new List<(double, string)>();
        if (!(hi > lo) || pixels <= 0) return ticks;
        double target = Math.Max(2, pixels / spacing);
        if (pi)
        {
            double unit = Math.PI;
            double[] steps = [0.25, 0.5, 1, 2, 4, 8, 16];
            double step = steps.FirstOrDefault(s => (hi - lo) / (s * unit) <= target + 1, steps[^1]) * unit;
            for (double k = Math.Ceiling(lo / step - 1e-9); k * step <= hi + 1e-9 * step; k++)
                ticks.Add((k * step, PiLabel(k * step / unit)));
            return ticks;
        }
        double raw = (hi - lo) / target, mag = Math.Pow(10, Math.Floor(Math.Log10(raw))), r = raw / mag;
        double stepN = (r < 1.5 ? 1 : r < 3 ? 2 : r < 7 ? 5 : 10) * mag;
        int decimals = Math.Max(0, (int)-Math.Floor(Math.Log10(stepN) + 1e-9));
        bool big = Math.Max(Math.Abs(lo), Math.Abs(hi)) >= 1e7;
        for (double k = Math.Ceiling(lo / stepN - 1e-9); k * stepN <= hi + 1e-9 * stepN && ticks.Count < 60; k++)
        {
            double v = k * stepN;
            if (Math.Abs(v) < stepN * 1e-9) v = 0;
            ticks.Add((v, big ? PlotNumber.Format(v, 2) : Tick(v, decimals)));
        }
        return ticks;
    }

    static string Tick(double v, int decimals)
    {
        string s = Math.Abs(v) >= 10000 ? v.ToString("N" + decimals, CultureInfo.InvariantCulture) : v.ToString("F" + decimals, CultureInfo.InvariantCulture);
        return s.Replace('-', '−');
    }

    static string PiLabel(double k)
    {
        // k in quarters of π.
        int q = (int)Math.Round(k * 4);
        if (q == 0) return "0";
        int g = Gcd(Math.Abs(q), 4), num = q / g, den = 4 / g;
        string sign = num < 0 ? "−" : "";
        int n = Math.Abs(num);
        string top = n == 1 ? "π" : n + "π";
        return sign + (den == 1 ? top : top + "/" + den);
    }

    static int Gcd(int a, int b) => b == 0 ? a : Gcd(b, a % b);

    // --- each kind of thing -------------------------------------------------------------------------------------------

    static void Curve(PlotState state, PlotCurve c, PlotMap map, List<PlotMark> data, Dictionary<PlotItem, int> colours, double detail)
    {
        double a = c.From is { } f ? state.Eval(f) : map.View.X0, b = c.To is { } t ? state.Eval(t) : map.View.X1;
        var pieces = PlotSampler.Curve(x => state.Call(c.Function, c.Body, x), a, b, map, state.Env, detail);
        data.Add(new PlotLines(pieces.Cast<IReadOnlyList<PlotPt>>().ToList(), Paint(colours[c]), 2, c.Dash, 1, c));
    }

    static (double, double) Param(PlotState state, PlotParametric p, double t)
    {
        double saved = state.Env.Slots[p.Slot];
        state.Env.Slots[p.Slot] = t;
        double x = state.Eval(p.X), y = state.Eval(p.Y);
        state.Env.Slots[p.Slot] = saved;
        return (x, y);
    }

    static void Series(PlotState state, PlotSeries s, PlotMap map, List<PlotMark> data, Dictionary<PlotItem, int> colours)
    {
        double from = Math.Max(Math.Ceiling(map.View.X0 - 0.5), state.Eval(s.Function.From!));
        double to = Math.Min(Math.Floor(map.View.X1 + 0.5), state.Eval(s.Function.To!));
        if (!double.IsFinite(from) || !double.IsFinite(to)) return;
        from = Math.Ceiling(from - 1e-9);
        var paint = Paint(colours[s]);
        double unit = map.Sx, baseY = Math.Clamp(map.ScreenY(0), map.Top, map.Bottom);
        var steps = new List<PlotPt>();
        var stems = new List<IReadOnlyList<PlotPt>>();
        int count = 0;
        for (double k = from; k <= to + 1e-9 && count < 4000; k++, count++)
        {
            double v = state.Term(s, k);
            if (!double.IsFinite(v))
            {
                if (steps.Count > 0)
                {
                    data.Add(new PlotLines([steps.ToList()], paint, 2, s.Dash, 1, s));
                    steps.Clear();
                }
                continue;
            }
            double sx = map.ScreenX(k), sy = Math.Clamp(map.ScreenY(v), map.Top - 20, map.Bottom + 20);
            switch (s.Style)
            {
                case SeriesStyle.Bars:
                    double w = Math.Clamp(unit * 0.72, 1, 24);
                    double topY = Math.Min(sy, baseY), h = Math.Abs(sy - baseY);
                    if (h >= 0.5) data.Add(new PlotBar(new PlotRect(sx - w / 2, topY, w, h), v >= 0, paint, s));
                    break;
                case SeriesStyle.Stems:
                    stems.Add([new(sx, baseY), new(sx, sy)]);
                    data.Add(new PlotDot(new PlotPt(sx, sy), 3.5, paint, s));
                    break;
                case SeriesStyle.Dots:
                    data.Add(new PlotDot(new PlotPt(sx, sy), 4, paint, s));
                    break;
                case SeriesStyle.Steps:
                    if (steps.Count > 0) steps.Add(new PlotPt(sx, steps[^1].Y));
                    steps.Add(new PlotPt(sx, sy));
                    steps.Add(new PlotPt(map.ScreenX(k + 1), sy));
                    break;
            }
        }
        if (stems.Count > 0) data.Insert(data.Count - stems.Count, new PlotLines(stems, paint, 1.5, PlotDash.Solid, 1, s));
        if (steps.Count > 0) data.Add(new PlotLines([steps], paint, 2, s.Dash, 1, s));
    }

    static void Heat(PlotState state, PlotHeat h, PlotMap map, List<PlotMark> data, Dictionary<PlotItem, int> colours, double detail)
    {
        double cell = detail < 1 ? 7 : 3.5;
        int nx = (int)Math.Clamp(Math.Ceiling(map.Width / cell), 8, 260), ny = (int)Math.Clamp(Math.Ceiling(map.Height / cell), 8, 200);
        var values = new double[nx, ny];
        var finite = new List<double>(nx * ny);
        for (int j = 0; j < ny; j++)
        {
            double yv = map.View.Y1 - (j + 0.5) / ny * map.View.Height;
            for (int i = 0; i < nx; i++)
            {
                double xv = map.View.X0 + (i + 0.5) / nx * map.View.Width;
                double v = state.At(h, xv, yv);
                values[i, j] = v;
                if (double.IsFinite(v)) finite.Add(v);
            }
        }
        if (finite.Count < 4) return;
        finite.Sort();
        double lo = finite[0], hi = finite[^1], median = finite[finite.Count / 2];
        if (!(hi > lo)) return;
        // Shades spread by where the values lie (a bowl's many low values don't all look the same): the median is
        // half-way, the order is kept.
        double m = Math.Clamp((median - lo) / (hi - lo), 1e-6, 1);
        double gamma = Math.Clamp(Math.Log(0.5) / Math.Log(m), 0.2, 1);
        double T(double v) => Math.Pow(Math.Clamp((v - lo) / (hi - lo), 0, 1), gamma);
        if (h.Fill)
        {
            var shade = new float[nx * ny];
            for (int j = 0; j < ny; j++)
                for (int i = 0; i < nx; i++)
                    shade[j * nx + i] = double.IsFinite(values[i, j]) ? (float)T(values[i, j]) : float.NaN;
            data.Add(new PlotHeatMap(new PlotRect(map.Left, map.Top, map.Width, map.Height), nx, ny, shade, h));
        }
        // Contour lines, evenly spaced in shade.
        var lines = new List<IReadOnlyList<PlotPt>>();
        for (int k = 1; k <= 9; k++)
        {
            double level = lo + (hi - lo) * Math.Pow(k / 10.0, 1 / gamma);
            foreach (var (a, b) in PlotSampler.Contour(values, level))
                lines.Add([Grid(a, map, nx, ny), Grid(b, map, nx, ny)]);
        }
        data.Add(new PlotLines(lines, h.Fill ? PlotPaint.HeatLine : Paint(colours[h]), h.Fill ? 1 : 1.3, PlotDash.Solid, h.Fill ? 0.55 : 0.9, h));
    }

    static PlotPt Grid(PlotPt g, PlotMap map, int nx, int ny) => new(map.Left + (g.X + 0.5) / nx * map.Width, map.Top + (g.Y + 0.5) / ny * map.Height);

    static void Field(PlotState state, PlotField f, PlotMap map, List<PlotMark> data, Dictionary<PlotItem, int> colours)
    {
        const double spacing = 34;
        int nx = Math.Max(2, (int)(map.Width / spacing)), ny = Math.Max(2, (int)(map.Height / spacing));
        var arrows = new List<(PlotPt At, double Dx, double Dy)>();
        double most = 0;
        for (int i = 0; i < nx; i++)
            for (int j = 0; j < ny; j++)
            {
                double sx = map.Left + (i + 0.5) * map.Width / nx, sy = map.Top + (j + 0.5) * map.Height / ny;
                double x = map.DataX(sx), y = map.DataY(sy);
                state.Env.Slots[f.XSlot] = x;
                state.Env.Slots[f.YSlot] = y;
                double p = state.Eval(f.P), q = state.Eval(f.Q);
                if (!double.IsFinite(p) || !double.IsFinite(q)) continue;
                double dx = p * map.Sx, dy = -q * map.Sy;
                most = Math.Max(most, Math.Sqrt(dx * dx + dy * dy));
                arrows.Add((new PlotPt(sx, sy), dx, dy));
            }
        if (most <= 0) return;
        double k = 0.8 * Math.Min(map.Width / nx, map.Height / ny) / most;
        var paint = f.Colour is int c ? Paint(c) : PlotPaint.Ink3;
        foreach (var (at, dx, dy) in arrows)
        {
            double len = Math.Sqrt(dx * dx + dy * dy) * k;
            if (len < 3) continue;
            var from = new PlotPt(at.X - dx * k / 2, at.Y - dy * k / 2);
            var to = new PlotPt(at.X + dx * k / 2, at.Y + dy * k / 2);
            data.Add(new PlotArrow(from, to, paint, 1.2, f));
        }
    }

    /// <summary>A shaded area under a curve (or between two), its label inside it where it fits, else above it.</summary>
    static void Shade(PlotState state, PlotShade s, PlotMap map, List<PlotMark> data, List<PlotMark> front, List<PlotRect> taken, List<Action> labels, Measure measure, Dictionary<PlotItem, int> colours)
    {
        double a = state.Eval(s.From), b = state.Eval(s.To);
        if (!double.IsFinite(a) || !double.IsFinite(b)) return;
        if (a > b) (a, b) = (b, a);
        double F(double x) => state.At(s.Under, x);
        double G(double x) => s.Over is { } o ? state.At(o, x) : 0;
        double area = PlotSampler.Area(F, s.Over is null ? null : G, a, b);
        state.Env.Slots[s.AreaSlot] = area;
        double a1 = Math.Max(a, map.View.X0), b1 = Math.Min(b, map.View.X1);
        if (!(b1 > a1)) return;
        int n = (int)Math.Clamp((b1 - a1) * map.Sx / 2, 2, 600);
        double guard = map.Height * 4;
        double Sy(double v) => Math.Clamp(map.ScreenY(v), map.Top - guard, map.Bottom + guard);
        var polygons = new List<IReadOnlyList<PlotPt>>();
        var topEdge = new List<PlotPt>();
        var bottomEdge = new List<PlotPt>();
        void Flush()
        {
            if (topEdge.Count >= 2)
            {
                bottomEdge.Reverse();
                polygons.Add([.. topEdge, .. bottomEdge]);
            }
            topEdge.Clear();
            bottomEdge.Clear();
        }
        for (int i = 0; i <= n; i++)
        {
            double x = a1 + (b1 - a1) * i / n, f = F(x), g = G(x);
            if (!double.IsFinite(f) || !double.IsFinite(g))
            {
                Flush();
                continue;
            }
            topEdge.Add(new PlotPt(map.ScreenX(x), Sy(f)));
            bottomEdge.Add(new PlotPt(map.ScreenX(x), Sy(g)));
        }
        Flush();
        var paint = Paint(colours[s]);
        data.Add(new PlotArea(polygons, paint, 0.2, s));
        // Its edges, faintly: where it starts and stops.
        var edges = new List<IReadOnlyList<PlotPt>>();
        foreach (double x in new[] { a, b })
            if (x >= map.View.X0 && x <= map.View.X1 && double.IsFinite(F(x)))
                edges.Add([new(map.ScreenX(x), Sy(G(x))), new(map.ScreenX(x), Sy(F(x)))]);
        if (edges.Count > 0) data.Add(new PlotLines(edges, paint, 1.2, PlotDash.Solid, 0.7, s));
        if (s.Label is not { } label) return;
        labels.Add(() =>
        {
            string text = label.Text(state.Env);
            double w = measure(text, LabelSize, false), h = LabelSize * LineGap;
            double xc = (a1 + b1) / 2, fy = map.ScreenY(F(xc)), gy = map.ScreenY(G(xc)), sx = map.ScreenX(xc);
            double topY = Math.Max(map.Top, Math.Min(fy, gy)), botY = Math.Min(map.Bottom, Math.Max(fy, gy));
            var inside = new PlotRect(sx - w / 2, topY + (botY - topY) * 0.62 - h / 2, w, h);
            var candidates = new List<PlotRect>();
            if (botY - topY > h + 8 && (b1 - a1) * map.Sx > w * 0.8) candidates.Add(inside);
            candidates.Add(new PlotRect(sx - w / 2, topY - h - 8, w, h));
            candidates.Add(new PlotRect(sx + 8, topY - h - 8, w, h));
            candidates.Add(new PlotRect(sx - w - 8, topY - h - 8, w, h));
            Place(candidates, text, LabelSize, PlotPaint.Ink, s, map, front, taken);
        });
    }

    /// <summary>A flat line or a vertical one: quiet, dashed unless it says, its label at its end.</summary>
    static void Reference(PlotState state, PlotItem item, PlotMap map, List<PlotMark> data, List<PlotMark> front, List<PlotRect> taken, List<Action> labels, Measure measure, Dictionary<PlotItem, int> colours)
    {
        var paint = Paint(colours[item]);
        var dash = item.Dash == PlotDash.Solid && item.Colour is null ? PlotDash.Dashed : item.Dash;
        if (item is PlotVLine v)
        {
            double x = state.Eval(v.X);
            if (!double.IsFinite(x) || x < map.View.X0 || x > map.View.X1) return;
            double sx = Snap(map.ScreenX(x));
            data.Add(new PlotLines([[new(sx, map.Top), new(sx, map.Bottom)]], paint, 1.4, dash, 1, v));
            if (v.Label is { } label)
                labels.Add(() =>
                {
                    string text = label.Text(state.Env);
                    double w = measure(text, LabelSize, false), h = LabelSize * LineGap;
                    Place([new(sx + 6, map.Top + 4, w, h), new(sx - 6 - w, map.Top + 4, w, h), new(sx + 6, map.Top + 8 + h, w, h), new(sx - 6 - w, map.Top + 8 + h, w, h)],
                        text, LabelSize, PlotPaint.Ink2, v, map, front, taken);
                });
            return;
        }
        var c = (PlotCurve)item;
        double y = state.At(c, (map.View.X0 + map.View.X1) / 2);
        if (!double.IsFinite(y) || y < map.View.Y0 || y > map.View.Y1) return;
        double a = c.From is { } f ? Math.Max(map.View.X0, state.Eval(f)) : map.View.X0, b = c.To is { } t ? Math.Min(map.View.X1, state.Eval(t)) : map.View.X1;
        double sy = Snap(map.ScreenY(y));
        data.Add(new PlotLines([[new(map.ScreenX(a), sy), new(map.ScreenX(b), sy)]], paint, 1.4, dash, 1, c));
        if (c.Label is { } cl)
            labels.Add(() =>
            {
                string text = cl.Text(state.Env);
                double w = measure(text, LabelSize, false), h = LabelSize * LineGap, r = map.ScreenX(b);
                Place([new(r - 6 - w, sy - h - 3, w, h), new(r - 6 - w, sy + 3, w, h), new(map.ScreenX(a) + 6, sy - h - 3, w, h), new(map.ScreenX(a) + 6, sy + 3, w, h)],
                    text, LabelSize, PlotPaint.Ink2, c, map, front, taken);
            });
    }

    /// <summary>A tangent or a secant: its line across the view, its point (or points), and its label.</summary>
    static void Touching(PlotState state, PlotItem item, PlotMap map, List<PlotMark> data, List<PlotMark> front, List<PlotRect> taken, List<Action> labels, Measure measure, Dictionary<PlotItem, int> colours, List<PlotHandle> handles)
    {
        PlotCurve of;
        double x0, y0, slope;
        var at = new List<(double X, PlotExpression E)>();
        if (item is PlotTangent t)
        {
            of = t.Of;
            x0 = state.Eval(t.At);
            y0 = state.At(of, x0);
            slope = PlotSampler.Slope(x => state.At(of, x), x0);
            state.Env.Slots[t.SlopeSlot] = slope;
            at.Add((x0, t.At));
        }
        else
        {
            var s = (PlotSecant)item;
            of = s.Of;
            double a = state.Eval(s.A), b = state.Eval(s.B);
            double ya = state.At(of, a), yb = state.At(of, b);
            slope = (yb - ya) / (b - a);
            state.Env.Slots[s.SlopeSlot] = slope;
            (x0, y0) = (a, ya);
            at.Add((a, s.A));
            at.Add((b, s.B));
        }
        if (!double.IsFinite(x0) || !double.IsFinite(y0) || !double.IsFinite(slope)) return;
        var paint = Paint(colours[item]);
        var line = Clip(map.Screen(map.View.X0, y0 + slope * (map.View.X0 - x0)), map.Screen(map.View.X1, y0 + slope * (map.View.X1 - x0)), map);
        if (line is { } seg) data.Add(new PlotLines([[seg.A, seg.B]], paint, 1.6, item.Dash, 1, item));
        PlotPt? first = null;
        foreach (var (x, e) in at)
        {
            double y = state.At(of, x);
            if (!double.IsFinite(y)) continue;
            var p = map.Screen(x, y);
            first ??= p;
            data.Add(new PlotDot(p, 4.5, paint, item));
            if (e.SlotOnly is int slot && state.Plot.Params.FirstOrDefault(q => q.Slot == slot) is { } param)
                handles.Add(new PlotHandle(p, param, null, item));
        }
        if (item.Label is { } label && first is { } pt)
            labels.Add(() =>
            {
                string text = label.Text(state.Env);
                double w = measure(text, LabelSize, false), h = LabelSize * LineGap;
                // Above the line on the side it rises from, so it doesn't sit on the curve.
                double up = slope >= 0 ? -1 : 1;
                Place([new(pt.X + 10 * -up, pt.Y - h - 10, w, h), new(pt.X - w - 10 * up, pt.Y - h - 10, w, h), new(pt.X + 10, pt.Y + 10, w, h), new(pt.X - w - 10, pt.Y + 10, w, h)],
                    text, LabelSize, PlotPaint.Ink, item, map, front, taken);
            });
    }

    static void Descent(PlotState state, PlotDescent d, PlotMap map, List<PlotMark> data, Dictionary<PlotItem, int> colours, List<PlotHandle> handles)
    {
        bool two = d.On.Arity == 2;
        double x0 = state.Eval(d.X0), y0 = two ? state.Eval(d.Y0!) : 0;
        double rate = state.Eval(d.Rate), steps = state.Eval(d.Steps);
        if (!double.IsFinite(steps)) return;
        var path = PlotSampler.Descent((x, y) => state.At(d.On, x, y), two, x0, y0, rate, (int)Math.Round(steps));
        var points = path.Select(p => two ? map.Screen(p.X, p.Y) : map.Screen(p.X, state.At(d.On, p.X, 0)))
            .Where(p => double.IsFinite(p.X) && double.IsFinite(p.Y)).Select(p => new PlotPt(Math.Clamp(p.X, map.Left - 2000, map.Right + 2000), Math.Clamp(p.Y, map.Top - 2000, map.Bottom + 2000))).ToList();
        if (points.Count == 0) return;
        var paint = Paint(colours[d]);
        if (points.Count > 1) data.Add(new PlotLines([points], paint, 1.8, d.Dash, 1, d));
        for (int i = 1; i < points.Count; i++) data.Add(new PlotDot(points[i], i == points.Count - 1 ? 4.5 : 2.8, paint, d));
        data.Add(new PlotDot(points[0], 5, paint, d, Hollow: true));
        if (two && d.X0.SlotOnly is int sx && d.Y0!.SlotOnly is int sy
            && state.Plot.Params.FirstOrDefault(q => q.Slot == sx) is { } px && state.Plot.Params.FirstOrDefault(q => q.Slot == sy) is { } py)
            handles.Add(new PlotHandle(points[0], px, py, d));
    }

    static void Vector(PlotState state, PlotVector v, PlotMap map, List<PlotMark> data, List<PlotMark> front, List<PlotRect> taken, List<Action> labels, Measure measure, Dictionary<PlotItem, int> colours)
    {
        double x0 = v.X0 is { } a ? state.Eval(a) : 0, y0 = v.Y0 is { } b ? state.Eval(b) : 0, x = state.Eval(v.X), y = state.Eval(v.Y);
        if (!double.IsFinite(x0) || !double.IsFinite(y0) || !double.IsFinite(x) || !double.IsFinite(y)) return;
        var from = map.Screen(x0, y0);
        var to = map.Screen(x, y);
        var paint = Paint(colours[v]);
        data.Add(new PlotArrow(from, to, paint, 2.4, v));
        if (v.Label is { } label) labels.Add(() => AtTip(state.Env, label.Text(state.Env), from, to, v, map, front, taken, measure));
    }

    /// <summary>A label at an arrow's tip, carried on past it.</summary>
    static void AtTip(PlotEnv env, string text, PlotPt from, PlotPt to, PlotItem? item, PlotMap map, List<PlotMark> front, List<PlotRect> taken, Measure measure)
    {
        double w = measure(text, LabelSize, false), h = LabelSize * LineGap;
        double dx = to.X - from.X, dy = to.Y - from.Y, len = Math.Max(1e-9, Math.Sqrt(dx * dx + dy * dy));
        double ux = dx / len, uy = dy / len;
        var c = new PlotPt(to.X + ux * (8 + w / 2), to.Y + uy * (6 + h / 2));
        Place([new(c.X - w / 2, c.Y - h / 2, w, h), new(to.X + 8, to.Y - h - 4, w, h), new(to.X - w - 8, to.Y - h - 4, w, h), new(to.X + 8, to.Y + 4, w, h), new(to.X - w - 8, to.Y + 4, w, h)],
            text, LabelSize, PlotPaint.Ink, item, map, front, taken);
    }

    /// <summary>A point (with its label round it, wherever there's room) or words placed in the plane.</summary>
    static void Marked(PlotState state, PlotItem item, PlotMap map, List<PlotMark> data, List<PlotMark> front, List<PlotRect> taken, List<Action> labels, Measure measure, List<PlotHandle> handles)
    {
        var (xe, ye) = item is PlotPoint p ? (p.X, p.Y) : (((PlotText)item).X, ((PlotText)item).Y);
        double x = state.Eval(xe), y = state.Eval(ye);
        if (!double.IsFinite(x) || !double.IsFinite(y)) return;
        var at = map.Screen(x, y);
        if (!map.Inside(at, 2)) return;
        if (item is PlotPoint)
        {
            var paint = Paint(item.Colour ?? -1);
            data.Add(new PlotDot(at, 4.5, paint, item));
            var px = xe.SlotOnly is int sx ? state.Plot.Params.FirstOrDefault(q => q.Slot == sx) : null;
            var py = ye.SlotOnly is int sy ? state.Plot.Params.FirstOrDefault(q => q.Slot == sy) : null;
            if (px is not null) handles.Add(new PlotHandle(at, px, py, item));
            else if (py is not null) handles.Add(new PlotHandle(at, py, null, item));
        }
        if (item.Label is not { } label) return;
        labels.Add(() =>
        {
            string text = label.Text(state.Env);
            double w = measure(text, LabelSize, false), h = LabelSize * LineGap;
            if (item is PlotText)
            {
                Place([new(at.X - w / 2, at.Y - h / 2, w, h)], text, LabelSize, PlotPaint.Ink2, item, map, front, taken, force: true);
                return;
            }
            const double o = 8;
            Place([
                new(at.X + o, at.Y - h - o + 4, w, h), new(at.X - w - o, at.Y - h - o + 4, w, h), new(at.X + o, at.Y + o - 4, w, h), new(at.X - w - o, at.Y + o - 4, w, h),
                new(at.X - w / 2, at.Y - h - o - 2, w, h), new(at.X - w / 2, at.Y + o + 2, w, h), new(at.X + o + 2, at.Y - h / 2, w, h), new(at.X - w - o - 2, at.Y - h / 2, w, h),
            ], text, LabelSize, PlotPaint.Ink, item, map, front, taken);
        });
    }

    /// <summary>A lone series' label beside its curve's last stretch in view.</summary>
    static void Beside(PlotState state, PlotItem item, List<PlotMark> data, PlotMap map, List<PlotMark> front, List<PlotRect> taken, Measure measure)
    {
        string text = item.Label!.Text(state.Env);
        double w = measure(text, LabelSize, false), h = LabelSize * LineGap;
        var points = data.OfType<PlotLines>().Where(l => l.Item == item).SelectMany(l => l.Lines.SelectMany(x => x)).Where(p => map.Inside(p, 0.5)).ToList();
        if (points.Count == 0) return;
        // Near its end in view, above the curve where there's room, else below it.
        var near = points.OrderByDescending(p => p.X).Where((_, i) => i % 6 == 0).Take(60).ToList();
        var candidates = near.Select(p => new PlotRect(p.X - w - 4, p.Y - h - 6, w, h))
            .Concat(near.Select(p => new PlotRect(p.X - w - 4, p.Y + 6, w, h))).ToList();
        Place(candidates, text, LabelSize, PlotPaint.Ink, item, map, front, taken);
    }

    /// <summary>Puts words in the first of <paramref name="candidates"/> that's inside the plot's area and covers no
    /// other words (nor, when given, any of <paramref name="avoid"/>'s points); the first one when none is clear and
    /// <paramref name="force"/> says so; nowhere otherwise.</summary>
    static void Place(IEnumerable<PlotRect> candidates, string text, double size, PlotPaint paint, PlotItem? item, PlotMap map, List<PlotMark> front, List<PlotRect> taken,
        bool force = false, IReadOnlyList<PlotPt>? avoid = null)
    {
        var areaBox = new PlotRect(map.Left + 2, map.Top + 2, map.Width - 4, map.Height - 4);
        PlotRect? chosen = null, firstInside = null, firstFree = null;
        foreach (var c in candidates)
        {
            if (!c.Within(areaBox)) continue;
            firstInside ??= c;
            if (taken.Any(t => t.Overlaps(c, 3))) continue;
            if (avoid is not null && avoid.Any(c.Contains)) continue;
            firstFree ??= c;
            if (Covers(c)) continue;
            chosen = c;
            break;
        }
        // Nowhere clear of the lines: on its own patch of paper over them, where no other words are.
        chosen ??= firstFree ?? (force ? firstInside ?? candidates.FirstOrDefault() : null);
        if (chosen is not { } box) return;
        taken.Add(box);
        front.Add(new PlotWords(box, text, size, false, paint, true, item));
    }

    /// <summary>The part of the segment a–b inside the plot's area (Liang–Barsky), or null when none is.</summary>
    public static (PlotPt A, PlotPt B)? Clip(PlotPt a, PlotPt b, PlotMap map)
    {
        double t0 = 0, t1 = 1, dx = b.X - a.X, dy = b.Y - a.Y;
        bool Edge(double p, double q)
        {
            if (Math.Abs(p) < 1e-12) return q >= 0;
            double r = q / p;
            if (p < 0)
            {
                if (r > t1) return false;
                if (r > t0) t0 = r;
            }
            else
            {
                if (r < t0) return false;
                if (r < t1) t1 = r;
            }
            return true;
        }
        if (!double.IsFinite(dx) || !double.IsFinite(dy)) return null;
        if (Edge(-dx, a.X - map.Left) && Edge(dx, map.Right - a.X) && Edge(-dy, a.Y - map.Top) && Edge(dy, map.Bottom - a.Y))
            return (new PlotPt(a.X + t0 * dx, a.Y + t0 * dy), new PlotPt(a.X + t1 * dx, a.Y + t1 * dy));
        return null;
    }

    /// <summary>A matrix as the map it is: the plane's grid carried by it, the unit square and where it goes, where the
    /// basis vectors land, and its eigenvectors' lines.</summary>
    static void Matrix(PlotState state, PlotMatrix m, PlotMap map, List<PlotMark> data, List<PlotMark> front, List<PlotRect> taken, List<Action> labels, Measure measure)
    {
        var (a, b, c, d) = state.Matrix(m);
        if (!double.IsFinite(a + b + c + d)) return;
        double det = a * d - b * c;
        state.Env.Slots[m.DetSlot] = det;
        var view = map.View;
        // How many grid lines carry across the view: the view's corners taken back through the matrix.
        int n = 12;
        if (Math.Abs(det) > 1e-9)
        {
            double most = 0;
            foreach (var (x, y) in new[] { (view.X0, view.Y0), (view.X0, view.Y1), (view.X1, view.Y0), (view.X1, view.Y1) })
                most = Math.Max(most, Math.Max(Math.Abs((d * x - b * y) / det), Math.Abs((-c * x + a * y) / det)));
            n = (int)Math.Clamp(Math.Ceiling(most) + 1, 2, 40);
        }
        double far = n * 4 + 10;
        PlotPt S(double x, double y) => map.Screen(a * x + b * y, c * x + d * y);
        var grid = new List<IReadOnlyList<PlotPt>>();
        var axes = new List<IReadOnlyList<PlotPt>>();
        for (int i = -n; i <= n; i++)
        {
            foreach (var (p, q) in new[] { (S(i, -far), S(i, far)), (S(-far, i), S(far, i)) })
                if (Clip(p, q, map) is { } seg) (i == 0 ? axes : grid).Add([seg.A, seg.B]);
        }
        var blue = PlotPaint.Series(0);
        data.Add(new PlotLines(grid, blue, 1, PlotDash.Solid, 0.42, m));
        data.Add(new PlotLines(axes, blue, 1.6, PlotDash.Solid, 0.85, m));
        // The unit square, where it was (dashed) and where it goes (filled).
        var unit = new List<PlotPt> { map.Screen(0, 0), map.Screen(1, 0), map.Screen(1, 1), map.Screen(0, 1), map.Screen(0, 0) };
        data.Add(new PlotLines([unit], PlotPaint.Ink3, 1.2, PlotDash.Dashed, 1, m));
        var image = new List<PlotPt> { S(0, 0), S(1, 0), S(1, 1), S(0, 1) };
        data.Add(new PlotArea([image], blue, 0.16, m));
        data.Add(new PlotLines([[.. image, image[0]]], blue, 1.8, PlotDash.Solid, 1, m));
        // Its eigenvectors: lines through the origin it only stretches.
        var eigen = PlotSampler.Eigen(m.Entries[0].Eval(state.Env), m.Entries[1].Eval(state.Env), m.Entries[2].Eval(state.Env), m.Entries[3].Eval(state.Env));
        double t = m.MorphParam is { } morph ? state.Value(morph) : 1;
        var purple = PlotPaint.Series(3);
        foreach (var (lambda, vx, vy) in eigen)
        {
            if (Clip(map.Screen(-far * vx, -far * vy), map.Screen(far * vx, far * vy), map) is not { } seg) continue;
            data.Add(new PlotLines([[seg.A, seg.B]], purple, 1.5, PlotDash.Dashed, 0.95, m));
            double l = 1 + t * (lambda - 1);
            // The label near the end that's higher up, inside the area.
            var end = seg.A.Y < seg.B.Y ? seg.A : seg.B;
            var other = end.Equals(seg.A) ? seg.B : seg.A;
            var inward = new PlotPt(end.X + (other.X - end.X) * 0.12, end.Y + (other.Y - end.Y) * 0.12);
            string text = "λ = " + PlotNumber.Format(l);
            labels.Add(() =>
            {
                double w = measure(text, LabelSize, false), h = LabelSize * LineGap;
                Place([new(inward.X + 6, inward.Y - h / 2, w, h), new(inward.X - w - 6, inward.Y - h / 2, w, h), new(inward.X + 6, inward.Y + 4, w, h), new(inward.X - w - 6, inward.Y + 4, w, h)],
                    text, LabelSize, PlotPaint.Ink2, m, map, front, taken);
            });
        }
        // Where the basis vectors go.
        var o = map.Screen(0, 0);
        var e1 = S(1, 0);
        var e2 = S(0, 1);
        data.Add(new PlotArrow(o, e1, PlotPaint.Series(1), 2.6, m));
        data.Add(new PlotArrow(o, e2, PlotPaint.Series(2), 2.6, m));
        labels.Add(() => AtTip(state.Env, $"Ae₁ = ({PlotNumber.Format(a)}, {PlotNumber.Format(c)})", o, e1, m, map, front, taken, measure));
        labels.Add(() => AtTip(state.Env, $"Ae₂ = ({PlotNumber.Format(b)}, {PlotNumber.Format(d)})", o, e2, m, map, front, taken, measure));
        string caption = m.Label?.Text(state.Env) ?? "det = " + PlotNumber.Format(det);
        labels.Insert(0, () =>
        {
            double w = measure(caption, LabelSize, true), h = LabelSize * LineGap;
            var box = new PlotRect(map.Left + 8, map.Top + 6, w, h);
            taken.Add(box);
            front.Add(new PlotWords(box, caption, LabelSize, true, PlotPaint.Ink, true, m));
        });
    }

    /// <summary>What a screen reader says the plot is: its title, axes, sliders and what it draws.</summary>
    public static string Describe(PlotState state)
    {
        var p = state.Plot;
        var parts = new List<string> { "Plot" + (p.Title is { } t ? ": " + t : "") };
        var v = state.View;
        parts.Add($"{p.X?.Label ?? p.XName} from {PlotNumber.Format(v.X0)} to {PlotNumber.Format(v.X1)}");
        parts.Add($"{p.Y?.Label ?? "y"} from {PlotNumber.Format(v.Y0)} to {PlotNumber.Format(v.Y1)}");
        foreach (var i in p.Items.Where(Legendable)) parts.Add(i.Name);
        foreach (var (param, k) in p.Params.Select((q, k) => (q, k)).Where(x => !x.q.Name.StartsWith('\u0001')))
            parts.Add($"{param.Label ?? param.Name} = {PlotNumber.Format(state.Values[k])}");
        return string.Join(". ", parts) + ".";
    }
}
