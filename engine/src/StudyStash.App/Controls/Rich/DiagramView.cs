using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using StudyStash.Core.Rich;

namespace StudyStash.App.Controls.Rich;

/// <summary>
/// A flowchart drawn in the app's own type and colours, light or dark, in either look: laid out once (a ring, a
/// tree or a layered chart) away from the window, with a quiet space about its size kept for it until it's ready,
/// then fitted to its column. A chart too wide for it turns (a left-to-right chart goes
/// top-down, a top-down tree left-to-right); then the picture scales down, never below <see cref="MinScale"/> (its
/// words stay at least 8 px), and past that it scrolls sideways. It never grows past <see cref="MaxScale"/> (1 in a
/// note). In a note, a click opens it larger (<see cref="OpenLarger"/>). A chart that can't be laid out becomes the
/// calm card that says so, with its source.
/// </summary>
public sealed class DiagramView : Decorator
{
    public static readonly StyledProperty<Flowchart?> ChartProperty = AvaloniaProperty.Register<DiagramView, Flowchart?>(nameof(Chart));
    public static readonly StyledProperty<double> MaxScaleProperty = AvaloniaProperty.Register<DiagramView, double>(nameof(MaxScale), 1);

    /// <summary>The smallest the picture is drawn: 13 px box words stay at least 8 px.</summary>
    public const double MinScale = 0.6;

    readonly DiagramCanvas canvas = new();
    readonly ScrollViewer scroller;
    bool failed;

    static DiagramView() => AffectsMeasure<DiagramView>(ChartProperty, MaxScaleProperty);

    public DiagramView() : this(opensLarger: true) { }

    /// <summary>A diagram that opens larger on a click (in a note), or one that doesn't (the larger window's own).</summary>
    public DiagramView(bool opensLarger)
    {
        scroller = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = canvas,
        };
        HorizontalAlignment = HorizontalAlignment.Center;
        canvas.Failed = ShowProblem;
        if (!opensLarger)
        {
            Child = scroller;
            return;
        }
        var badge = OpenLarger.Badge();
        Child = new Panel { Children = { scroller, badge } };
        OpenLarger.Wire(this, badge, () => !failed && Chart is { } chart ? new OpenDiagramEventArgs(this) { Title = Title, Chart = chart, Scene = Scene } : null);
    }

    public Flowchart? Chart
    {
        get => GetValue(ChartProperty);
        set => SetValue(ChartProperty, value);
    }

    public double MaxScale
    {
        get => GetValue(MaxScaleProperty);
        set => SetValue(MaxScaleProperty, value);
    }

    /// <summary>What the diagram is called: its own title, or else its first words.</summary>
    public string Title => Chart is not { } chart ? "Diagram" : chart.Title is { Length: > 0 } t ? t : chart.Labels().FirstOrDefault() ?? "Diagram";

    /// <summary>The chart as the note wrote it, shown on the card if it can't be laid out (else its own
    /// canonical source is).</summary>
    public string? Source { get; init; }

    /// <summary>The diagram as laid out for its column, or null until it has been.</summary>
    public DiagramScene? Scene => canvas.Scene;

    /// <summary>Whether it's still being laid out, its space kept by a quiet placeholder.</summary>
    public bool IsLaying => canvas.Laying;

    /// <summary>How much the picture is scaled to fit its column.</summary>
    public double Scale => canvas.Scale;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ChartProperty)
        {
            canvas.Chart = Chart;
            AutomationProperties.SetName(this, Chart is { } chart ? "Diagram: " + string.Join(", ", chart.Labels()) : null);
        }
        else if (change.Property == MaxScaleProperty) canvas.MaxScale = MaxScale;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        // The scroller measures its content as if it had all the width in the world; the picture needs the column's.
        canvas.Room = availableSize;
        return base.MeasureOverride(availableSize);
    }

    public override void Render(DrawingContext context)
    {
        // Clear, but there: the whole picture answers a click, not just its lines.
        context.FillRectangle(Brushes.Transparent, new Rect(Bounds.Size));
    }

    /// <summary>The chart couldn't be laid out: the calm card takes its place, across the column, and it no longer
    /// opens larger.</summary>
    void ShowProblem()
    {
        if (failed || Chart is not { } chart) return;
        failed = true;
        HorizontalAlignment = HorizontalAlignment.Stretch;
        Cursor = null;
        Focusable = false;
        ToolTip.SetTip(this, null);
        Child = new DiagramCard("Study Stash couldn't lay this flowchart out.", Source ?? chart.ToSource());
    }
}

/// <summary>What <see cref="DiagramView"/> scrolls: the scene, drawn at its scale with the look's tokens.</summary>
sealed class DiagramCanvas : Control
{
    public static readonly StyledProperty<IBrush?> InkProperty = AvaloniaProperty.Register<DiagramCanvas, IBrush?>(nameof(Ink));
    public static readonly StyledProperty<IBrush?> LineProperty = AvaloniaProperty.Register<DiagramCanvas, IBrush?>(nameof(Line));
    public static readonly StyledProperty<IBrush?> GroupFillProperty = AvaloniaProperty.Register<DiagramCanvas, IBrush?>(nameof(GroupFill));
    public static readonly StyledProperty<IBrush?> AccentProperty = AvaloniaProperty.Register<DiagramCanvas, IBrush?>(nameof(Accent));
    public static readonly StyledProperty<IBrush?> AccentTintProperty = AvaloniaProperty.Register<DiagramCanvas, IBrush?>(nameof(AccentTint));
    public static readonly StyledProperty<IBrush?> QuietProperty = AvaloniaProperty.Register<DiagramCanvas, IBrush?>(nameof(Quiet));
    public static readonly StyledProperty<FontFamily?> FontProperty = AvaloniaProperty.Register<DiagramCanvas, FontFamily?>(nameof(Font));

    static DiagramCanvas()
    {
        AffectsRender<DiagramCanvas>(InkProperty, LineProperty, GroupFillProperty, AccentProperty, AccentTintProperty, QuietProperty);
        AffectsMeasure<DiagramCanvas>(FontProperty);
    }

    public DiagramCanvas()
    {
        // Light and dark share the scene; only the colours change.
        ActualThemeVariantChanged += (_, _) => InvalidateVisual();
        Bind(InkProperty, this.GetResourceObservable("Fg"));
        Bind(LineProperty, this.GetResourceObservable("Fg2"));
        Bind(GroupFillProperty, this.GetResourceObservable("Fill2"));
        Bind(AccentProperty, this.GetResourceObservable("Accent"));
        Bind(AccentTintProperty, this.GetResourceObservable("AccentTint"));
        Bind(QuietProperty, this.GetResourceObservable("Fg3"));
        Bind(FontProperty, this.GetResourceObservable("TextFont"));
    }

    public IBrush? Ink { get => GetValue(InkProperty); set => SetValue(InkProperty, value); }
    public IBrush? Line { get => GetValue(LineProperty); set => SetValue(LineProperty, value); }
    public IBrush? GroupFill { get => GetValue(GroupFillProperty); set => SetValue(GroupFillProperty, value); }
    public IBrush? Accent { get => GetValue(AccentProperty); set => SetValue(AccentProperty, value); }
    public IBrush? AccentTint { get => GetValue(AccentTintProperty); set => SetValue(AccentTintProperty, value); }
    public IBrush? Quiet { get => GetValue(QuietProperty); set => SetValue(QuietProperty, value); }
    public FontFamily? Font { get => GetValue(FontProperty); set => SetValue(FontProperty, value); }

    Flowchart? chart;
    string? source;
    SceneKind kind;
    (double Width, double Height)? guessed, guessedTurned;
    Task? awaiting;
    bool failed;
    double maxScale = 1;
    Size room = new(double.PositiveInfinity, double.PositiveInfinity);

    public Flowchart? Chart
    {
        get => chart;
        set
        {
            chart = value;
            source = value?.ToSource();
            kind = value is null ? default : DiagramLayout.Kind(value);
            guessed = guessedTurned = null;
            failed = false;
            InvalidateMeasure();
        }
    }

    /// <summary>Called (once, on the UI thread) when the chart can't be laid out.</summary>
    public Action? Failed { get; set; }

    /// <summary>Whether the chart is still being laid out, a placeholder keeping its space.</summary>
    public bool Laying { get; private set; }

    public double MaxScale
    {
        get => maxScale;
        set
        {
            maxScale = value;
            InvalidateMeasure();
        }
    }

    /// <summary>The room the column gives the picture (set by the view before it measures).</summary>
    public Size Room
    {
        get => room;
        set
        {
            if (room == value) return;
            room = value;
            InvalidateMeasure();
        }
    }

    public DiagramScene? Scene { get; private set; }

    public double Scale { get; private set; } = 1;

    FontFamily Family => Font ?? FontFamily.Default;

    protected override Size MeasureOverride(Size availableSize)
    {
        Laying = false;
        if (chart is null || source is null)
        {
            Scene = null;
            return default;
        }
        double width = double.IsFinite(room.Width) ? room.Width : double.PositiveInfinity;
        // Never lays anything out here: what isn't laid out yet is asked for, and a placeholder keeps its space.
        var written = SceneCache.Find(chart, source, Family, null);
        if (written.Failed)
        {
            Scene = null;
            Fail();
            return default;
        }
        var guess = written.Scene is { } known ? (known.Width, known.Height) : guessed ??= DiagramLayout.Estimate(chart);
        SceneCache.Lookup? other = null;
        if (guess.Width > width && DiagramLayout.Turned(chart, written.Scene?.Kind ?? kind) is { } turned)
        {
            other = SceneCache.Find(chart, source, Family, turned);
            var turnedGuess = other.Value.Scene is { } t ? (t.Width, t.Height) : guessedTurned ??= DiagramLayout.Estimate(chart, turned);
            if (turnedGuess.Width < guess.Width) guess = turnedGuess;
        }
        if ((written.Laying ?? other?.Laying) is { } laying)
        {
            Wait(laying);
            Scene = null;
            Laying = true;
            Scale = Fit(guess.Width, guess.Height, width);
            return new Size(Math.Ceiling(Math.Min(guess.Width * Scale, width)), Math.Ceiling(guess.Height * Scale));
        }
        var scene = written.Scene!;
        if (other?.Scene is { } otherScene && otherScene.Width < scene.Width) scene = otherScene;
        Scene = scene;
        Scale = Fit(scene.Width, scene.Height, width);
        return new Size(Math.Ceiling(scene.Width * Scale), Math.Ceiling(scene.Height * Scale));
    }

    /// <summary>The scale a picture this size is drawn at in the room given: no larger than the most allowed, no
    /// smaller than the floor.</summary>
    double Fit(double sceneWidth, double sceneHeight, double width)
    {
        double scale = Math.Min(maxScale, width / sceneWidth);
        if (double.IsFinite(room.Height) && room.Height > 0) scale = Math.Min(scale, room.Height / sceneHeight);
        return Math.Max(DiagramView.MinScale, scale);
    }

    /// <summary>Measures again once <paramref name="task"/> (a layout this picture needs) is done.</summary>
    void Wait(Task task)
    {
        if (ReferenceEquals(task, awaiting)) return;
        awaiting = task;
        // This window's own dispatcher, found here on its thread: never looked up from the layout's thread, which
        // (once a test's app has gone) would make that thread the UI thread of whatever comes next.
        var ui = Dispatcher.UIThread;
        task.ContinueWith(_ => ui.Post(() =>
        {
            if (ReferenceEquals(awaiting, task)) awaiting = null;
            InvalidateMeasure();
        }), TaskScheduler.Default);
    }

    /// <summary>Says the chart couldn't be laid out — after this measure, since the view swaps what it shows.</summary>
    void Fail()
    {
        if (failed) return;
        failed = true;
        Dispatcher.UIThread.Post(() => Failed?.Invoke());
    }

    public override void Render(DrawingContext context)
    {
        context.FillRectangle(Brushes.Transparent, new Rect(Bounds.Size));
        if (Laying)
        {
            Placeholder(context);
            return;
        }
        if (Scene is not { } scene) return;
        bool dark = ActualThemeVariant == ThemeVariant.Dark;
        var ink = Solid(Ink, dark ? Colors.White : Color.Parse("#1D1D1F"));
        var line = Line ?? new SolidColorBrush(ink.Color, 0.6);
        var accent = Solid(Accent, Color.Parse("#0A84A0")).Color;
        Color? tint = AccentTint is ISolidColorBrush t ? t.Color : null;
        var (fill, stroke) = DiagramColours.Neutral(dark, ink.Color);
        // Boxes and words land on whole pixels of the screen it's drawn on, so their hairlines stay crisp: at 125%,
        // 150% or 175% a whole point isn't a whole pixel, so they snap to the screen's own pixels, not to points.
        double snap = Scale * (TopLevel.GetTopLevel(this)?.RenderScaling ?? 1);
        var scaled = context.PushTransform(Matrix.CreateScale(Scale, Scale));

        foreach (var g in scene.Groups)
        {
            context.DrawRectangle(GroupFill, null, new RoundedRect(Snap(ToRect(g.Box), snap, false), 12));
            DrawCentred(context, g.Title, DiagramLayout.TitleSize, FontWeight.SemiBold, line, g.TitleBox.Center.X, g.TitleBox.Y, g.TitleBox.H, snap);
        }

        var labelled = scene.Edges.Where(e => e.LabelLines.Count > 0).ToList();
        IDisposable? clip = null;
        if (labelled.Count > 0 || scene.Groups.Count > 0)
        {
            // The lines stop short of their words and of the groups' titles, so neither needs a background.
            var gaps = new GeometryGroup { FillRule = FillRule.NonZero };
            foreach (var e in labelled) gaps.Children.Add(new RectangleGeometry(ToRect(e.LabelBox.Inflate(2))));
            foreach (var g in scene.Groups) gaps.Children.Add(new RectangleGeometry(ToRect(g.TitleBox.Inflate(2))));
            clip = context.PushGeometryClip(new CombinedGeometry(GeometryCombineMode.Exclude, new RectangleGeometry(new Rect(-50, -50, scene.Width + 100, scene.Height + 100)), gaps));
        }
        foreach (var e in scene.Edges)
        {
            var pen = new Pen(line, SceneShapes.Thickness(e.Line), e.Line == EdgeLine.Dotted ? new DashStyle(SceneShapes.Dashes.Select(d => d / SceneShapes.Thickness(e.Line)), 0) : null, PenLineCap.Round, PenLineJoin.Round);
            var path = new StreamGeometry();
            using (var g = path.Open())
            {
                bool open = false;
                foreach (var step in e.Path)
                {
                    switch (step.Verb)
                    {
                        case PathVerb.Move:
                            if (open) g.EndFigure(false);
                            g.BeginFigure(P(step.A), false);
                            open = true;
                            break;
                        case PathVerb.Line:
                            g.LineTo(P(step.A));
                            break;
                        default:
                            g.CubicBezierTo(P(step.A), P(step.B), P(step.C));
                            break;
                    }
                }
                if (open) g.EndFigure(false);
            }
            context.DrawGeometry(null, pen, path);
            Marker(context, e.EndEnd, e.Tip, e.Base, line);
            Marker(context, e.StartEnd, e.StartTip, e.StartBase, line);
        }
        clip?.Dispose();
        // The scale again, afresh: drawn into a bitmap at 200% (a picture of the window, as the self-test takes), Avalonia
        // puts the words and boxes that follow a clip taken off inside the same transform twice as far out.
        scaled.Dispose();
        using var _ = context.PushTransform(Matrix.CreateScale(Scale, Scale));

        foreach (var e in labelled)
        {
            double top = e.LabelBox.Center.Y - e.LabelLines.Count * DiagramLayout.LabelLineHeight / 2;
            for (int i = 0; i < e.LabelLines.Count; i++)
                DrawCentred(context, e.LabelLines[i], DiagramLayout.LabelSize, FontWeight.Normal, line, e.LabelBox.Center.X, top + i * DiagramLayout.LabelLineHeight, DiagramLayout.LabelLineHeight, snap);
        }

        foreach (var n in scene.Nodes)
        {
            var (nodeFill, nodeStroke) = n.Tone == Tone.None ? (fill, stroke) : DiagramColours.Of(n.Tone, dark, accent, tint);
            IBrush brush = new SolidColorBrush(nodeFill);
            var pen = new Pen(new SolidColorBrush(nodeStroke), 1);
            var b = ToRect(n.Box);
            if (SceneShapes.Corners(n) is { } corners)
            {
                var poly = new StreamGeometry();
                using (var g = poly.Open())
                {
                    g.BeginFigure(P(corners[0]), true);
                    foreach (var c in corners.Skip(1)) g.LineTo(P(c));
                    g.EndFigure(true);
                }
                context.DrawGeometry(brush, pen, poly);
            }
            else if (n.Shape == NodeShape.Circle) context.DrawEllipse(brush, pen, b.Center, b.Width / 2, b.Height / 2);
            else if (n.Shape == NodeShape.Cylinder) Cylinder(context, Snap(b, snap, true), brush, pen);
            else
            {
                var r = Snap(b, snap, true);
                double radius = SceneShapes.Radius(n);
                context.DrawRectangle(brush, pen, new RoundedRect(r, radius));
                if (n.Shape == NodeShape.Subroutine)
                {
                    context.DrawLine(pen, new Point(r.X + 8, r.Y), new Point(r.X + 8, r.Bottom));
                    context.DrawLine(pen, new Point(r.Right - 8, r.Y), new Point(r.Right - 8, r.Bottom));
                }
            }
            double top = SceneShapes.TextTop(n);
            for (int i = 0; i < n.Lines.Count; i++)
                DrawCentred(context, n.Lines[i], DiagramLayout.TextSize, FontWeight.Medium, ink, n.Box.Center.X, top + i * DiagramLayout.LineHeight, DiagramLayout.LineHeight, snap);
        }
    }

    /// <summary>The space a diagram being laid out keeps: a soft rounded box, with a quiet word in the middle.</summary>
    void Placeholder(DrawingContext context)
    {
        var box = new Rect(Bounds.Size);
        if (box.Width < 1 || box.Height < 1) return;
        // The corners of the calm card that says a diagram can't be drawn.
        context.DrawRectangle(GroupFill, null, new RoundedRect(box, Skin.Current == SkinKind.Mac ? 8 : 4));
        if (box.Height < 24) return;
        var t = Text(NoteView.DrawingWords, DiagramLayout.LabelSize, FontWeight.Normal, Quiet ?? Line ?? Brushes.Gray);
        context.DrawText(t, new Point(Math.Round((box.Width - t.Width) / 2), Math.Round((box.Height - t.Height) / 2)));
    }

    static void Cylinder(DrawingContext context, Rect b, IBrush fill, Pen pen)
    {
        double cap = SceneShapes.CylinderCap, rx = b.Width / 2;
        var body = new StreamGeometry();
        using (var g = body.Open())
        {
            g.BeginFigure(new Point(b.X, b.Y + cap), true);
            g.LineTo(new Point(b.X, b.Bottom - cap));
            g.ArcTo(new Point(b.Right, b.Bottom - cap), new Size(rx, cap), 0, false, SweepDirection.CounterClockwise);
            g.LineTo(new Point(b.Right, b.Y + cap));
            g.ArcTo(new Point(b.X, b.Y + cap), new Size(rx, cap), 0, false, SweepDirection.CounterClockwise);
            g.EndFigure(true);
        }
        context.DrawGeometry(fill, pen, body);
        var rim = new StreamGeometry();
        using (var g = rim.Open())
        {
            g.BeginFigure(new Point(b.X, b.Y + cap), false);
            g.ArcTo(new Point(b.Right, b.Y + cap), new Size(rx, cap), 0, false, SweepDirection.CounterClockwise);
            g.EndFigure(false);
        }
        context.DrawGeometry(null, pen, rim);
    }

    static void Marker(DrawingContext context, EdgeEnd end, Pt tip, Pt @base, IBrush brush)
    {
        if (end == EdgeEnd.None || Pt.Distance(tip, @base) < 0.01) return;
        switch (end)
        {
            case EdgeEnd.Arrow:
                var head = SceneShapes.ArrowHead(tip, @base);
                var geometry = new StreamGeometry();
                using (var g = geometry.Open())
                {
                    g.BeginFigure(P(head[0]), true);
                    g.LineTo(P(head[1]));
                    g.LineTo(P(head[2]));
                    g.EndFigure(true);
                }
                context.DrawGeometry(brush, null, geometry);
                break;
            case EdgeEnd.Circle:
                context.DrawEllipse(brush, null, P(SceneShapes.Dot(tip, @base)), SceneShapes.DotRadius, SceneShapes.DotRadius);
                break;
            case EdgeEnd.Cross:
                var (a, b, c, d) = SceneShapes.Cross(tip, @base);
                var pen = new Pen(brush, 1.5, lineCap: PenLineCap.Round);
                context.DrawLine(pen, P(a), P(b));
                context.DrawLine(pen, P(c), P(d));
                break;
        }
    }

    void DrawCentred(DrawingContext context, string text, double size, FontWeight weight, IBrush brush, double centreX, double top, double lineHeight, double snap)
    {
        var t = Text(text, size, weight, brush);
        double x = centreX - t.WidthIncludingTrailingWhitespace / 2, y = top + (lineHeight - t.Height) / 2;
        context.DrawText(t, new Point(OnPixel(x, snap), OnPixel(y, snap)));
    }

    FormattedText Text(string text, double size, FontWeight weight, IBrush brush) =>
        new(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface(Family, FontStyle.Normal, weight), size, brush);

    static SolidColorBrush Solid(IBrush? brush, Color fallback) => brush as SolidColorBrush ?? new SolidColorBrush(brush is ISolidColorBrush s ? s.Color : fallback);

    static Point P(Pt p) => new(p.X, p.Y);

    static Rect ToRect(Box b) => new(b.X, b.Y, b.W, b.H);

    /// <summary>A rectangle on whole pixels of the screen (<paramref name="pixels"/> of them to a unit of the scene);
    /// a 1-unit outline's half a unit in, so its outer edge falls on a pixel's edge and it covers one row exactly
    /// when a unit is a pixel.</summary>
    internal static Rect Snap(Rect r, double pixels, bool hairline)
    {
        double x = OnPixel(r.X, pixels), y = OnPixel(r.Y, pixels), right = OnPixel(r.Right, pixels), bottom = OnPixel(r.Bottom, pixels);
        return hairline ? new Rect(x + 0.5, y + 0.5, right - x - 1, bottom - y - 1) : new Rect(x, y, right - x, bottom - y);
    }

    /// <summary>The nearest place to <paramref name="v"/> (in the scene's units) on a whole pixel of the screen.</summary>
    internal static double OnPixel(double v, double pixels) => pixels > 0 ? Math.Round(v * pixels) / pixels : v;
}
