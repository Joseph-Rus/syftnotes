using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Styling;
using StudyStash.Core.Rich;

namespace StudyStash.App.Controls.Rich;

/// <summary>What the pointer reads off a plot at a place: a heading (x = 1.2) and a row for each thing there.</summary>
sealed record PlotReading(string Heading, IReadOnlyList<(PlotPaint Paint, string Name, string Value, PlotPt? At)> Rows, double? X, PlotPt Where);

/// <summary>
/// A plot drawn in the app's type and colours, light or dark, at its column's width: its scene laid out again only when
/// the sliders, the view, what's hidden or the width change (each frame of a slider drag), drawn with the look's ink,
/// its grid and its paper, the series in their fixed colours. On screen it answers the pointer: a crosshair reads every
/// curve's value where it is, a click pins that, a drag moves a point that's a slider (or pans, zoomed in),
/// ⌘/Ctrl + scroll zooms, and a click on the legend shows or hides a curve. On paper it's the still picture.
/// </summary>
sealed class PlotCanvas : Control
{
    public static readonly StyledProperty<IBrush?> InkProperty = AvaloniaProperty.Register<PlotCanvas, IBrush?>(nameof(Ink));
    public static readonly StyledProperty<IBrush?> Ink2Property = AvaloniaProperty.Register<PlotCanvas, IBrush?>(nameof(Ink2));
    public static readonly StyledProperty<IBrush?> Ink3Property = AvaloniaProperty.Register<PlotCanvas, IBrush?>(nameof(Ink3));
    public static readonly StyledProperty<IBrush?> GridProperty = AvaloniaProperty.Register<PlotCanvas, IBrush?>(nameof(Grid));
    public static readonly StyledProperty<IBrush?> PaperProperty = AvaloniaProperty.Register<PlotCanvas, IBrush?>(nameof(Paper));
    public static readonly StyledProperty<IBrush?> LayerProperty = AvaloniaProperty.Register<PlotCanvas, IBrush?>(nameof(Layer));
    public static readonly StyledProperty<IBrush?> TipProperty = AvaloniaProperty.Register<PlotCanvas, IBrush?>(nameof(Tip));
    public static readonly StyledProperty<IBrush?> TipStrokeProperty = AvaloniaProperty.Register<PlotCanvas, IBrush?>(nameof(TipStroke));
    public static readonly StyledProperty<IBrush?> AccentProperty = AvaloniaProperty.Register<PlotCanvas, IBrush?>(nameof(Accent));
    public static readonly StyledProperty<FontFamily?> FontProperty = AvaloniaProperty.Register<PlotCanvas, FontFamily?>(nameof(Font));

    static PlotCanvas()
    {
        AffectsRender<PlotCanvas>(InkProperty, Ink2Property, Ink3Property, GridProperty, PaperProperty, LayerProperty, TipProperty, TipStrokeProperty, AccentProperty);
        AffectsMeasure<PlotCanvas>(FontProperty);
    }

    public PlotCanvas(PlotState state, bool still)
    {
        State = state;
        Still = still;
        ClipToBounds = false;
        ActualThemeVariantChanged += (_, _) =>
        {
            brushes.Clear();
            heatBitmap = null;
            InvalidateVisual();
        };
        Bind(InkProperty, this.GetResourceObservable("Fg"));
        Bind(Ink2Property, this.GetResourceObservable("Fg2"));
        Bind(Ink3Property, this.GetResourceObservable("Fg3"));
        Bind(GridProperty, this.GetResourceObservable("Sep"));
        Bind(PaperProperty, this.GetResourceObservable(Skin.Current == SkinKind.Mac ? "Win" : "Mica"));
        if (Skin.Current != SkinKind.Mac) Bind(LayerProperty, this.GetResourceObservable("Layer"));
        Bind(TipProperty, this.GetResourceObservable("PopupBg"));
        Bind(TipStrokeProperty, this.GetResourceObservable("PopupStroke"));
        Bind(AccentProperty, this.GetResourceObservable("Accent"));
        Bind(FontProperty, this.GetResourceObservable("TextFont"));
        if (!still) Wire();
    }

    public IBrush? Ink { get => GetValue(InkProperty); set => SetValue(InkProperty, value); }
    public IBrush? Ink2 { get => GetValue(Ink2Property); set => SetValue(Ink2Property, value); }
    public IBrush? Ink3 { get => GetValue(Ink3Property); set => SetValue(Ink3Property, value); }
    public IBrush? Grid { get => GetValue(GridProperty); set => SetValue(GridProperty, value); }
    public IBrush? Paper { get => GetValue(PaperProperty); set => SetValue(PaperProperty, value); }
    /// <summary>Windows' notes page: a see-through layer over the window's Mica, which a label's paper matches.</summary>
    public IBrush? Layer { get => GetValue(LayerProperty); set => SetValue(LayerProperty, value); }
    public IBrush? Tip { get => GetValue(TipProperty); set => SetValue(TipProperty, value); }
    public IBrush? TipStroke { get => GetValue(TipStrokeProperty); set => SetValue(TipStrokeProperty, value); }
    public IBrush? Accent { get => GetValue(AccentProperty); set => SetValue(AccentProperty, value); }
    public FontFamily? Font { get => GetValue(FontProperty); set => SetValue(FontProperty, value); }

    public PlotState State { get; }

    /// <summary>On paper: the picture, nothing more.</summary>
    public bool Still { get; }

    /// <summary>How it's drawn now (its title, what's hidden, how finely, predicting).</summary>
    public PlotLook Look
    {
        get => look;
        set
        {
            look = value;
            Refresh();
        }
    }

    PlotLook look = new();

    /// <summary>In a window of its own: the height it has (its area grows to fill it).</summary>
    public double? RoomHeight
    {
        get => roomHeight;
        set
        {
            if (roomHeight == value) return;
            roomHeight = value;
            InvalidateMeasure();
        }
    }

    double? roomHeight;

    public PlotScene? Scene { get; private set; }

    FontFamily Family => Font ?? FontFamily.Default;

    // --- laying out ----------------------------------------------------------------------------------------------------

    readonly Dictionary<(string, double, bool), double> widths = [];
    FontFamily? measuredIn;

    double Measure(string text, double size, bool bold)
    {
        if (!ReferenceEquals(measuredIn, Family))
        {
            widths.Clear();
            measuredIn = Family;
        }
        var key = (text, size, bold);
        if (widths.TryGetValue(key, out double w)) return w;
        if (widths.Count > 4000) widths.Clear();
        return widths[key] = Text(text, size, bold, Brushes.Black).WidthIncludingTrailingWhitespace;
    }

    FormattedText Text(string text, double size, bool bold, IBrush brush) =>
        new(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface(Family, FontStyle.Normal, bold ? FontWeight.SemiBold : FontWeight.Normal), size, brush);

    double laidWidth = double.NaN;

    /// <summary>Lays the plot out again at the width it has (the sliders or the view moved), and draws it.</summary>
    public void Refresh()
    {
        if (!double.IsFinite(laidWidth))
        {
            InvalidateMeasure();
            return;
        }
        double before = Scene?.Height ?? 0;
        Lay(laidWidth);
        if (Math.Abs((Scene?.Height ?? 0) - before) > 0.5) InvalidateMeasure();
        InvalidateVisual();
        Changed?.Invoke();
    }

    /// <summary>Called after the plot is laid out again (its sliders, its view).</summary>
    public event Action? Changed;

    void Lay(double width)
    {
        laidWidth = width;
        var scene = PlotLayout.Build(State, width, Measure, look);
        if (roomHeight is double room && double.IsFinite(room) && room > 0)
        {
            // A window: the area takes what's left of the window's height.
            double margins = scene.Height - scene.Map.Height;
            scene = PlotLayout.Build(State, width, Measure, look, Math.Max(160, room - margins));
        }
        Scene = scene;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        double width = double.IsFinite(availableSize.Width) && availableSize.Width > 0 ? availableSize.Width : 620;
        if (Scene is null || Math.Abs(width - laidWidth) > 0.5 || roomHeight is not null) Lay(width);
        return new Size(width, Scene!.Height);
    }

    // --- drawing -------------------------------------------------------------------------------------------------------

    readonly Dictionary<(PlotPaint, double), IBrush> brushes = [];

    bool Dark => ActualThemeVariant == ThemeVariant.Dark;

    static Color ColorOf(IBrush? b, Color fallback) => b is ISolidColorBrush s ? Color.FromArgb((byte)Math.Round(s.Color.A * s.Opacity), s.Color.R, s.Color.G, s.Color.B) : fallback;

    Color PaintColor(PlotPaint paint)
    {
        bool dark = Dark;
        return paint.Role switch
        {
            PlotRole.Series => Rgb(paint.Index is >= 0 and < 5 ? (dark ? PlotPalette.DarkSeries : PlotPalette.LightSeries)[paint.Index] : (dark ? PlotPalette.DarkPaper : PlotPalette.Light).Red),
            PlotRole.Ink => ColorOf(Ink, dark ? Colors.White : Color.Parse("#1D1D1F")),
            PlotRole.Ink2 => ColorOf(Ink2, dark ? Color.Parse("#B8B8BE") : Color.Parse("#5E5E63")),
            PlotRole.Ink3 => ColorOf(Ink3, dark ? Color.Parse("#8E8E94") : Color.Parse("#86868B")),
            PlotRole.Grid => ColorOf(Grid, dark ? Color.FromArgb(30, 255, 255, 255) : Color.FromArgb(20, 0, 0, 0)),
            PlotRole.Axis => WithAlpha(ColorOf(Ink3, Colors.Gray), 0.75),
            PlotRole.Surface => Over(ColorOf(Layer, Colors.Transparent), Opaque(ColorOf(Paper, dark ? Color.Parse("#1E1E20") : Colors.White), dark)),
            _ => dark ? Color.FromArgb(72, 255, 255, 255) : Color.FromArgb(80, 0, 0, 0),
        };
    }

    static Color Rgb(PlotRgb c) => Color.FromArgb(c.A, c.R, c.G, c.B);

    /// <summary>A see-through colour laid over a solid one.</summary>
    static Color Over(Color top, Color under)
    {
        double a = top.A / 255.0;
        byte Mix(byte t, byte u) => (byte)Math.Round(t * a + u * (1 - a));
        return Color.FromRgb(Mix(top.R, under.R), Mix(top.G, under.G), Mix(top.B, under.B));
    }

    static Color WithAlpha(Color c, double k) => Color.FromArgb((byte)Math.Round(c.A * k), c.R, c.G, c.B);

    /// <summary>The paper behind a label: the page's own colour, made solid (a see-through page ground over glass).</summary>
    static Color Opaque(Color c, bool dark)
    {
        if (c.A == 255) return c;
        double a = c.A / 255.0;
        byte under = dark ? (byte)30 : (byte)246;
        byte Mix(byte v) => (byte)Math.Round(v * a + under * (1 - a));
        return Color.FromRgb(Mix(c.R), Mix(c.G), Mix(c.B));
    }

    IBrush Brush(PlotPaint paint, double opacity = 1)
    {
        if (brushes.TryGetValue((paint, opacity), out var b)) return b;
        var c = PaintColor(paint);
        return brushes[(paint, opacity)] = new SolidColorBrush(WithAlpha(c, opacity));
    }

    public override void Render(DrawingContext context)
    {
        context.FillRectangle(Brushes.Transparent, new Rect(Bounds.Size));
        if (Scene is not { } scene) return;
        var area = new Rect(scene.Map.Left, scene.Map.Top, scene.Map.Width, scene.Map.Height);
        using (context.PushClip(area))
            foreach (var m in scene.Data) Draw(context, m);
        foreach (var m in scene.Front) Draw(context, m);
        foreach (var k in scene.Keys) Key(context, k);
        if (!Still) Overlay(context, scene);
    }

    static readonly DashStyle Dashed = new([3, 2.5], 0), Dotted = new([0.1, 2.4], 0);

    Pen Pen(PlotPaint paint, double width, PlotDash dash, double opacity = 1) => new(Brush(paint, opacity), width, dash switch
    {
        PlotDash.Dashed => Dashed,
        PlotDash.Dotted => Dotted,
        _ => null,
    }, PenLineCap.Round, PenLineJoin.Round);

    void Draw(DrawingContext context, PlotMark mark)
    {
        switch (mark)
        {
            case PlotHeatMap h:
                DrawHeat(context, h);
                break;
            case PlotLines l when l.Lines.Count > 0:
                var g = new StreamGeometry();
                using (var c = g.Open())
                    foreach (var line in l.Lines)
                    {
                        if (line.Count < 2) continue;
                        c.BeginFigure(P(line[0]), false);
                        for (int i = 1; i < line.Count; i++) c.LineTo(P(line[i]));
                        c.EndFigure(false);
                    }
                context.DrawGeometry(null, Pen(l.Paint, l.Width, l.Dash, l.Opacity), g);
                break;
            case PlotArea a:
                var fill = new StreamGeometry();
                using (var c = fill.Open())
                    foreach (var poly in a.Polygons)
                    {
                        if (poly.Count < 3) continue;
                        c.BeginFigure(P(poly[0]), true);
                        for (int i = 1; i < poly.Count; i++) c.LineTo(P(poly[i]));
                        c.EndFigure(true);
                    }
                context.DrawGeometry(Brush(a.Paint, a.Opacity), null, fill);
                break;
            case PlotDot d:
                if (d.Hollow) context.DrawEllipse(Brush(PlotPaint.Surface), Pen(d.Paint, 2, PlotDash.Solid), P(d.At), d.Radius, d.Radius);
                else
                {
                    context.DrawEllipse(Brush(PlotPaint.Surface), null, P(d.At), d.Radius + 2, d.Radius + 2);
                    context.DrawEllipse(Brush(d.Paint), null, P(d.At), d.Radius, d.Radius);
                }
                break;
            case PlotArrow ar:
                var (shaft, head) = PlotSvg.ArrowGeometry(ar.From, ar.To, ar.Width);
                context.DrawLine(Pen(ar.Paint, ar.Width, PlotDash.Solid), P(ar.From), P(shaft));
                var tri = new StreamGeometry();
                using (var c = tri.Open())
                {
                    c.BeginFigure(P(head[0]), true);
                    c.LineTo(P(head[1]));
                    c.LineTo(P(head[2]));
                    c.EndFigure(true);
                }
                context.DrawGeometry(Brush(ar.Paint), null, tri);
                break;
            case PlotBar b:
                context.DrawGeometry(Brush(b.Paint), null, Geometry.Parse(PlotSvg.BarPath(b)));
                break;
            case PlotWords w:
                var t = Text(w.Text, w.Size, w.Bold, Brush(w.Paint));
                if (w.Backed)
                    context.DrawRectangle(Brush(PlotPaint.Surface, 0.9), null, new RoundedRect(new Rect(w.Box.X - 3, w.Box.Y, w.Box.W + 6, w.Box.H), 3));
                context.DrawText(t, new Point(Math.Round(w.Box.X), Math.Round(w.Box.Y + (w.Box.H - t.Height) / 2)));
                break;
        }
    }

    static Point P(PlotPt p) => new(p.X, p.Y);

    WriteableBitmap? heatBitmap;
    PlotHeatMap? heatFor;

    void DrawHeat(DrawingContext context, PlotHeatMap h)
    {
        if (!ReferenceEquals(heatFor, h) || heatBitmap is null)
        {
            heatBitmap?.Dispose();
            heatBitmap = new WriteableBitmap(new PixelSize(h.Columns, h.Rows), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
            using (var fb = heatBitmap.Lock())
            {
                bool dark = Dark;
                var row = new byte[h.Columns * 4];
                for (int j = 0; j < h.Rows; j++)
                {
                    for (int i = 0; i < h.Columns; i++)
                    {
                        float v = h.Shade[j * h.Columns + i];
                        var c = float.IsNaN(v) ? default : PlotPalette.HeatRamp(v, dark);
                        byte a = float.IsNaN(v) ? (byte)0 : (byte)255;
                        row[i * 4] = c.B;
                        row[i * 4 + 1] = c.G;
                        row[i * 4 + 2] = c.R;
                        row[i * 4 + 3] = a;
                    }
                    System.Runtime.InteropServices.Marshal.Copy(row, 0, fb.Address + j * fb.RowBytes, row.Length);
                }
            }
            heatFor = h;
        }
        using (context.PushRenderOptions(new RenderOptions { BitmapInterpolationMode = BitmapInterpolationMode.HighQuality }))
            context.DrawImage(heatBitmap, new Rect(0, 0, h.Columns, h.Rows), new Rect(h.Box.X, h.Box.Y, h.Box.W, h.Box.H));
    }

    void Key(DrawingContext context, PlotKey k)
    {
        double cy = k.Box.Y + k.Box.H / 2, x = k.Box.X;
        double o = k.Off ? 0.35 : 1;
        switch (k.Shape)
        {
            case PlotKeyShape.Line:
                context.DrawLine(Pen(k.Paint, 2.5, k.Dash, o), new Point(x + 1, cy), new Point(x + 17, cy));
                break;
            case PlotKeyShape.Bar:
                context.DrawRectangle(Brush(k.Paint, o), null, new RoundedRect(new Rect(x + 3, cy - 6, 12, 12), 2));
                break;
            case PlotKeyShape.Dot:
                context.DrawEllipse(Brush(k.Paint, o), null, new Point(x + 9, cy), 4.5, 4.5);
                break;
            case PlotKeyShape.Ramp:
                for (int i = 0; i < 6; i++)
                {
                    var c = PlotPalette.HeatRamp(i / 5.0, Dark);
                    context.FillRectangle(new SolidColorBrush(Color.FromRgb(c.R, c.G, c.B)), new Rect(x + 1 + i * 3, cy - 5, 3, 10));
                }
                break;
        }
    }

    // --- reading values off it -----------------------------------------------------------------------------------------

    /// <summary>Where the pointer is over the plot (null: not over its area).</summary>
    public PlotPt? Pointer { get; private set; }

    /// <summary>A pinned reading's place on the canvas, while it's in view (a click pins it; another click elsewhere
    /// moves it).</summary>
    public PlotPt? Pinned => pinnedAt is { } pin && Scene is { } s && s.Map.Screen(pin.X, pin.Y) is var at && s.Map.Inside(at) ? at : null;

    /// <summary>The pinned place, in the plane (it stays put as the plot zooms and pans).</summary>
    (double X, double Y)? pinnedAt;

    /// <summary>Called when a reading is pinned or let go.</summary>
    public event Action? PinChanged;

    public void Pin(double x, double y)
    {
        pinnedAt = (x, y);
        PinChanged?.Invoke();
        InvalidateVisual();
    }

    public void Unpin()
    {
        if (pinnedAt is null) return;
        pinnedAt = null;
        PinChanged?.Invoke();
        InvalidateVisual();
    }

    public bool IsPinned => pinnedAt is not null;

    /// <summary>The pinned place in the plane.</summary>
    public (double X, double Y)? PinnedAt => pinnedAt;

    /// <summary>What's read off the plot at a point of it: every visible curve's value at its x (a crosshair), a
    /// sequence's term at the nearest whole number, a surface's value there, where a matrix takes it.</summary>
    public PlotReading? ReadAt(PlotPt at)
    {
        if (Scene is not { } scene || !scene.Map.Inside(at)) return null;
        var map = scene.Map;
        var p = State.Plot;
        double x = map.DataX(at.X), y = map.DataY(at.Y);
        var hidden = Hidden;
        var rows = new List<(PlotPaint, string, string, PlotPt?)>();
        State.NewFrame();
        string xName = p.XName;
        bool crosshair = false;
        foreach (var item in p.Items.Where(i => !hidden.Contains(i)))
        {
            switch (item)
            {
                case PlotCurve { Flat: false } c:
                    double v = State.At(c, x);
                    if (!double.IsFinite(v)) continue;
                    crosshair = true;
                    var pt = map.Screen(x, v);
                    rows.Add((PlotLayout.Paint(scene.Colours[c]), c.Label?.Text(State.Env) ?? PlotNumber.Pretty(c.Name), PlotNumber.Format(v), map.Inside(pt) ? pt : null));
                    break;
                case PlotSeries s:
                    double k = Math.Round(x);
                    double term = State.Term(s, k);
                    if (!double.IsFinite(term)) continue;
                    rows.Add((PlotLayout.Paint(scene.Colours[s]), $"{s.Function.Name}({PlotNumber.Format(k)})", PlotNumber.Format(term), map.Screen(k, term)));
                    xName = s.Variable;
                    x = k;
                    crosshair = true;
                    break;
                case PlotHeat h:
                    double hv = State.At(h, x, y);
                    if (double.IsFinite(hv)) rows.Add((PlotPaint.Ink3, h.Label?.Text(State.Env) ?? h.Name, PlotNumber.Format(hv), null));
                    break;
                case PlotMatrix m:
                    var (a, b, c2, d) = State.Matrix(m);
                    double ix = a * x + b * y, iy = c2 * x + d * y;
                    rows.Add((PlotPaint.Series(0), $"A({PlotNumber.Format(x)}, {PlotNumber.Format(y)})", $"({PlotNumber.Format(ix)}, {PlotNumber.Format(iy)})", map.Screen(ix, iy)));
                    break;
                case PlotPoints pts:
                    foreach (var (px, py) in pts.Points)
                    {
                        double dx = State.Eval(px), dy = State.Eval(py);
                        var sp = map.Screen(dx, dy);
                        if (Math.Abs(sp.X - at.X) < 10 && Math.Abs(sp.Y - at.Y) < 10)
                            rows.Add((PlotLayout.Paint(scene.Colours[pts]), pts.Label?.Text(State.Env) ?? "data", $"({PlotNumber.Format(dx)}, {PlotNumber.Format(dy)})", sp));
                    }
                    break;
            }
        }
        if (rows.Count == 0) return null;
        string heading = crosshair ? $"{PlotNumber.Pretty(xName)} = {PlotNumber.Format(x)}"
            : p.Items.OfType<PlotHeat>().FirstOrDefault() is { } heat ? $"{PlotNumber.Pretty(heat.Variables[0])} = {PlotNumber.Format(x)}, {PlotNumber.Pretty(heat.Variables[1])} = {PlotNumber.Format(y)}"
            : $"({PlotNumber.Format(x)}, {PlotNumber.Format(y)})";
        return new PlotReading(heading, rows, crosshair ? x : null, at);
    }

    /// <summary>What the student has hidden, and (predicting) the curves to be sketched.</summary>
    IReadOnlySet<PlotItem> Hidden
    {
        get
        {
            if (!look.Predicting) return look.Hidden;
            var all = new HashSet<PlotItem>(look.Hidden);
            foreach (var i in State.Plot.Items.Where(i => i is PlotCurve { Flat: false } or PlotParametric or PlotSeries)) all.Add(i);
            return all;
        }
    }

    void Overlay(DrawingContext context, PlotScene scene)
    {
        if (sketch.Count > 1) DrawSketch(context, scene);
        if (look.Predicting) return;
        if (Pinned is { } p && ReadAt(p) is { } pinned) Reading(context, scene, pinned, strong: true);
        if (Pointer is { } ptr && !panning && handle is null && (Pinned is null || Distance(ptr, Pinned.Value) > 12) && ReadAt(ptr) is { } r)
            Reading(context, scene, r, strong: Pinned is null);
        if (handle is { } h || HandleAt(Pointer) is not null)
        {
            var hp = (handle ?? HandleAt(Pointer))!;
            context.DrawEllipse(null, new Pen(Accent ?? Brushes.SteelBlue, 2), P(hp.At), 9, 9);
        }
    }

    static double Distance(PlotPt a, PlotPt b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

    /// <summary>A reading drawn: its crosshair, a dot on each curve where it's read, and a tip with the values.</summary>
    void Reading(DrawingContext context, PlotScene scene, PlotReading r, bool strong)
    {
        var map = scene.Map;
        if (r.X is double x)
        {
            double sx = Math.Round(map.ScreenX(x)) + 0.5;
            context.DrawLine(new Pen(Brush(PlotPaint.Ink3, strong ? 0.9 : 0.5), 1), new Point(sx, map.Top), new Point(sx, map.Bottom));
        }
        else
        {
            var w = r.Where;
            var pen = new Pen(Brush(PlotPaint.Ink, 0.7), 1);
            context.DrawLine(pen, new Point(w.X - 6, w.Y), new Point(w.X + 6, w.Y));
            context.DrawLine(pen, new Point(w.X, w.Y - 6), new Point(w.X, w.Y + 6));
        }
        foreach (var (paint, _, _, at) in r.Rows)
            if (at is { } p && map.Inside(p))
            {
                context.DrawEllipse(Brush(PlotPaint.Surface), null, P(p), 6, 6);
                context.DrawEllipse(Brush(paint), null, P(p), 4.5, 4.5);
            }
        if (!strong) return;
        // The tip: the heading, then a row a thing, its colour beside its name and its value right-aligned.
        const double size = 12, pad = 8, gap = 14, rowH = 18;
        var head = Text(r.Heading, size, false, Brush(PlotPaint.Ink2));
        var names = r.Rows.Select(row => Text(row.Name, size, false, Brush(PlotPaint.Ink))).ToList();
        var values = r.Rows.Select(row => Text(row.Value, size, true, Brush(PlotPaint.Ink))).ToList();
        double nameW = names.Count == 0 ? 0 : names.Max(n => n.WidthIncludingTrailingWhitespace);
        double valueW = values.Count == 0 ? 0 : values.Max(v => v.WidthIncludingTrailingWhitespace);
        double w0 = Math.Max(head.WidthIncludingTrailingWhitespace, 14 + nameW + gap + valueW) + 2 * pad;
        double h0 = rowH * (1 + r.Rows.Count) + 2 * pad - 4;
        double anchorX = r.X is double ax ? map.ScreenX(ax) : r.Where.X;
        double left = anchorX + 14;
        if (left + w0 > Bounds.Width - 2) left = anchorX - 14 - w0;
        left = Math.Clamp(left, 2, Math.Max(2, Bounds.Width - w0 - 2));
        double top = Math.Clamp(r.Where.Y - h0 / 2, map.Top, Math.Max(map.Top, map.Bottom - h0));
        var box = new Rect(left, top, w0, h0);
        context.DrawRectangle(Tip ?? Brushes.White, new Pen(TipStroke ?? Brushes.LightGray, 1), new RoundedRect(box, Skin.Current == SkinKind.Mac ? 7 : 5),
            new BoxShadows(new BoxShadow { OffsetY = 2, Blur = 8, Color = Color.FromArgb(Dark ? (byte)90 : (byte)36, 0, 0, 0) }));
        context.DrawText(head, new Point(left + pad, top + pad - 2));
        for (int i = 0; i < r.Rows.Count; i++)
        {
            double y = top + pad - 2 + rowH * (i + 1);
            context.DrawEllipse(Brush(r.Rows[i].Paint), null, new Point(left + pad + 4, y + names[i].Height / 2), 4, 4);
            context.DrawText(names[i], new Point(left + pad + 14, y));
            context.DrawText(values[i], new Point(left + w0 - pad - values[i].WidthIncludingTrailingWhitespace, y));
        }
    }

    // --- predict, then reveal ------------------------------------------------------------------------------------------

    readonly List<(double X, double Y)> sketch = [];
    bool sketching;

    /// <summary>The student's sketch, in the plane.</summary>
    public IReadOnlyList<(double X, double Y)> Sketch => sketch;

    public void ClearSketch()
    {
        sketch.Clear();
        InvalidateVisual();
    }

    /// <summary>Called as the student sketches.</summary>
    public event Action? Sketched;

    void DrawSketch(DrawingContext context, PlotScene scene)
    {
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(P(scene.Map.Screen(sketch[0].X, sketch[0].Y)), false);
            foreach (var (x, y) in sketch.Skip(1)) c.LineTo(P(scene.Map.Screen(x, y)));
            c.EndFigure(false);
        }
        var brush = look.Predicting ? Accent ?? Brushes.SteelBlue : Brush(PlotPaint.Ink2);
        using (context.PushClip(new Rect(scene.Map.Left, scene.Map.Top, scene.Map.Width, scene.Map.Height)))
            context.DrawGeometry(null, new Pen(brush, look.Predicting ? 2.5 : 2, look.Predicting ? null : new DashStyle([2, 2], 0), PenLineCap.Round, PenLineJoin.Round), g);
    }

    // --- the pointer ---------------------------------------------------------------------------------------------------

    Point? pressed;
    Point last;
    bool panning;
    PlotHandle? handle;
    PlotKey? keyDown;
    double pinchScale = 1;

    /// <summary>A drag of a point that's a slider started (true) or stopped (false).</summary>
    public event Action<bool>? HandleDrag;

    /// <summary>Called when a key in the legend is clicked.</summary>
    public event Action<PlotItem>? KeyClicked;

    /// <summary>In a window of its own: a plain scroll pans (in a note it scrolls the note on).</summary>
    public bool Windowed { get; set; }

    PlotHandle? HandleAt(PlotPt? p) => p is { } q && Scene is { } s
        ? s.Handles.Where(h => Distance(h.At, q) <= 12).OrderBy(h => Distance(h.At, q)).FirstOrDefault()
        : null;

    PlotKey? KeyAt(Point p) => Scene?.Keys.FirstOrDefault(k => p.X >= k.Box.X - 2 && p.X <= k.Box.Right + 2 && p.Y >= k.Box.Y - 2 && p.Y <= k.Box.Bottom + 2);

    void Wire()
    {
        PointerMoved += (_, e) =>
        {
            var p = e.GetPosition(this);
            var pt = new PlotPt(p.X, p.Y);
            if (handle is { } h && Scene is { } s)
            {
                State.Set(h.X, h.Y is null && h.Item is PlotPoint { X.SlotOnly: null } ? s.Map.DataY(p.Y) : s.Map.DataX(p.X));
                if (h.Y is { } yParam) State.Set(yParam, s.Map.DataY(p.Y));
                Refresh();
                e.Handled = true;
                return;
            }
            if (sketching && Scene is { } sk)
            {
                var m = sk.Map;
                double x = m.DataX(Math.Clamp(p.X, m.Left, m.Right)), y = m.DataY(Math.Clamp(p.Y, m.Top, m.Bottom));
                if (sketch.Count == 0 || Math.Abs(m.ScreenX(x) - m.ScreenX(sketch[^1].X)) >= 1.5) sketch.Add((x, y));
                Sketched?.Invoke();
                InvalidateVisual();
                e.Handled = true;
                return;
            }
            if (pressed is { } down && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed && Scene is { } sc)
            {
                if (!panning && Math.Abs(p.X - down.X) + Math.Abs(p.Y - down.Y) > 4 && (State.Zoomed || Windowed)) panning = true;
                if (panning)
                {
                    Cursor = new Cursor(StandardCursorType.SizeAll);
                    var d = p - last;
                    State.View = State.View.Pan(-d.X / sc.Map.Sx, d.Y / sc.Map.Sy);
                    last = p;
                    Refresh();
                    e.Handled = true;
                    return;
                }
            }
            Pointer = Scene is { } here && here.Map.Inside(pt) ? pt : null;
            Cursor = HandleAt(pt) is not null || KeyAt(p) is not null ? new Cursor(StandardCursorType.Hand)
                : look.Predicting && Pointer is not null ? new Cursor(StandardCursorType.Cross) : null;
            InvalidateVisual();
        };
        PointerExited += (_, _) =>
        {
            if (handle is not null || panning || sketching) return;
            Pointer = null;
            InvalidateVisual();
        };
        PointerPressed += (_, e) =>
        {
            var props = e.GetCurrentPoint(this).Properties;
            if (!props.IsLeftButtonPressed) return;
            var p = e.GetPosition(this);
            var pt = new PlotPt(p.X, p.Y);
            Focus(NavigationMethod.Pointer);
            if (KeyAt(p) is { } key)
            {
                keyDown = key;
                e.Handled = true;
                return;
            }
            if (look.Predicting && Scene is { } s0 && s0.Map.Inside(pt, 4))
            {
                sketching = true;
                sketch.Clear();
                e.Pointer.Capture(this);
                e.Handled = true;
                return;
            }
            if (HandleAt(pt) is { } h)
            {
                handle = h;
                e.Pointer.Capture(this);
                HandleDrag?.Invoke(true);
                e.Handled = true;
                return;
            }
            pressed = last = p;
            panning = false;
            e.Pointer.Capture(this);
        };
        PointerReleased += (_, e) =>
        {
            var p = e.GetPosition(this);
            if (keyDown is { } key)
            {
                keyDown = null;
                if (KeyAt(p) == key) KeyClicked?.Invoke(key.Item);
                e.Handled = true;
                return;
            }
            if (sketching)
            {
                sketching = false;
                e.Pointer.Capture(null);
                Sketched?.Invoke();
                e.Handled = true;
                return;
            }
            if (handle is not null)
            {
                handle = null;
                e.Pointer.Capture(null);
                HandleDrag?.Invoke(false);
                e.Handled = true;
                return;
            }
            if (pressed is null) return;
            bool click = !panning;
            pressed = null;
            panning = false;
            e.Pointer.Capture(null);
            Cursor = null;
            if (!click || e.InitialPressMouseButton != MouseButton.Left || Scene is not { } s) return;
            e.Handled = true;
            var pt = new PlotPt(p.X, p.Y);
            if (!s.Map.Inside(pt))
            {
                Unpin();
                return;
            }
            if (Pinned is { } pin && Distance(pin, pt) < 8) Unpin();
            else if (ReadAt(pt) is not null) Pin(s.Map.DataX(p.X), s.Map.DataY(p.Y));
            else Unpin();
        };
        PointerCaptureLost += (_, _) =>
        {
            if (handle is not null) HandleDrag?.Invoke(false);
            pressed = null;
            panning = false;
            handle = null;
            sketching = false;
        };
        PointerWheelChanged += (_, e) =>
        {
            if (Scene is not { } s) return;
            var p = e.GetPosition(this);
            bool zoomKey = e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta);
            if (zoomKey)
            {
                ZoomBy(Math.Pow(1.18, e.Delta.Y + e.Delta.X), p);
                e.Handled = true;
            }
            else if (Windowed || (State.Zoomed && e.Delta.X != 0 && Math.Abs(e.Delta.X) > Math.Abs(e.Delta.Y)))
            {
                State.View = State.View.Pan(-e.Delta.X * 40 / s.Map.Sx, e.Delta.Y * 40 / s.Map.Sy);
                Refresh();
                e.Handled = true;
            }
        };
        PointerTouchPadGestureMagnify += (_, e) =>
        {
            ZoomBy(1 + e.Delta.X, e.GetPosition(this));
            e.Handled = true;
        };
        GestureRecognizers.Add(new PinchGestureRecognizer());
        Pinch += (_, e) =>
        {
            ZoomBy(e.Scale / pinchScale, e.ScaleOrigin);
            pinchScale = e.Scale;
            e.Handled = true;
        };
        PinchEnded += (_, _) => pinchScale = 1;
    }

    /// <summary>Zooms the view by <paramref name="factor"/> about a point of the canvas (the middle of the area when
    /// none): the plot is worked out again for what's in view, so it stays exact however far in it goes.</summary>
    public void ZoomBy(double factor, Point? about = null)
    {
        if (Scene is not { } s || !double.IsFinite(factor) || factor <= 0) return;
        var m = s.Map;
        var at = about ?? new Point(m.Left + m.Width / 2, m.Top + m.Height / 2);
        double cx = m.DataX(Math.Clamp(at.X, m.Left, m.Right)), cy = m.DataY(Math.Clamp(at.Y, m.Top, m.Bottom));
        var next = State.View.Zoom(factor, cx, cy);
        var home = State.Home;
        // Never smaller than a millionth of where it opened, nor ten thousand times bigger.
        if (next.Width < home.Width * 1e-6 || next.Width > home.Width * 1e4) return;
        if (factor < 1 && next.Width > home.Width && next.Height > home.Height && !Windowed) next = home;
        State.View = next;
        Refresh();
    }

    /// <summary>Back to where it opened.</summary>
    public void Home()
    {
        State.View = State.Home;
        Refresh();
    }
}
