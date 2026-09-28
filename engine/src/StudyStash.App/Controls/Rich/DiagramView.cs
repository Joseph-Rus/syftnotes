using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using StudyStash.Core.Rich;

namespace StudyStash.App.Controls.Rich;

/// <summary>
/// A flowchart drawn in the app's own type and colours, light or dark, in either look: laid out once (a ring, a
/// tree or a layered chart), then fitted to its column. A chart too wide for it turns (a left-to-right chart goes
/// top-down, a top-down tree left-to-right); then the picture scales down, never below <see cref="MinScale"/> (its
/// words stay at least 8 px), and past that it scrolls sideways. It never grows past <see cref="MaxScale"/> (1 in a
/// note).
/// </summary>
public sealed class DiagramView : Decorator
{
    public static readonly StyledProperty<Flowchart?> ChartProperty = AvaloniaProperty.Register<DiagramView, Flowchart?>(nameof(Chart));
    public static readonly StyledProperty<double> MaxScaleProperty = AvaloniaProperty.Register<DiagramView, double>(nameof(MaxScale), 1);

    /// <summary>The smallest the picture is drawn: 13 px box words stay at least 8 px.</summary>
    public const double MinScale = 0.6;

    readonly DiagramCanvas canvas = new();
    readonly ScrollViewer scroller;

    static DiagramView() => AffectsMeasure<DiagramView>(ChartProperty, MaxScaleProperty);

    public DiagramView()
    {
        scroller = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = canvas,
        };
        Child = scroller;
        HorizontalAlignment = HorizontalAlignment.Center;
        Cursor = new Cursor(StandardCursorType.Hand);
        ToolTip.SetTip(this, "Open larger");
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

    /// <summary>The diagram as laid out for its column, or null before it has been measured.</summary>
    public DiagramScene? Scene => canvas.Scene;

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
}

/// <summary>What <see cref="DiagramView"/> scrolls: the scene, drawn at its scale with the look's tokens.</summary>
sealed class DiagramCanvas : Control
{
    public static readonly StyledProperty<IBrush?> InkProperty = AvaloniaProperty.Register<DiagramCanvas, IBrush?>(nameof(Ink));
    public static readonly StyledProperty<IBrush?> LineProperty = AvaloniaProperty.Register<DiagramCanvas, IBrush?>(nameof(Line));
    public static readonly StyledProperty<IBrush?> GroupFillProperty = AvaloniaProperty.Register<DiagramCanvas, IBrush?>(nameof(GroupFill));
    public static readonly StyledProperty<IBrush?> AccentProperty = AvaloniaProperty.Register<DiagramCanvas, IBrush?>(nameof(Accent));
    public static readonly StyledProperty<IBrush?> AccentTintProperty = AvaloniaProperty.Register<DiagramCanvas, IBrush?>(nameof(AccentTint));
    public static readonly StyledProperty<FontFamily?> FontProperty = AvaloniaProperty.Register<DiagramCanvas, FontFamily?>(nameof(Font));

    static DiagramCanvas()
    {
        AffectsRender<DiagramCanvas>(InkProperty, LineProperty, GroupFillProperty, AccentProperty, AccentTintProperty);
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
        Bind(FontProperty, this.GetResourceObservable("TextFont"));
    }

    public IBrush? Ink { get => GetValue(InkProperty); set => SetValue(InkProperty, value); }
    public IBrush? Line { get => GetValue(LineProperty); set => SetValue(LineProperty, value); }
    public IBrush? GroupFill { get => GetValue(GroupFillProperty); set => SetValue(GroupFillProperty, value); }
    public IBrush? Accent { get => GetValue(AccentProperty); set => SetValue(AccentProperty, value); }
    public IBrush? AccentTint { get => GetValue(AccentTintProperty); set => SetValue(AccentTintProperty, value); }
    public FontFamily? Font { get => GetValue(FontProperty); set => SetValue(FontProperty, value); }

    Flowchart? chart;
    double maxScale = 1;
    Size room = new(double.PositiveInfinity, double.PositiveInfinity);

    public Flowchart? Chart
    {
        get => chart;
        set
        {
            chart = value;
            InvalidateMeasure();
        }
    }

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
        if (chart is null)
        {
            Scene = null;
            return default;
        }
        double width = double.IsFinite(room.Width) ? room.Width : double.PositiveInfinity;
        var scene = SceneCache.Get(chart, Family, null);
        if (scene.Width > width && DiagramLayout.Turned(chart, scene.Kind) is { } turned)
        {
            var other = SceneCache.Get(chart, Family, turned);
            if (other.Width < scene.Width) scene = other;
        }
        double scale = Math.Min(maxScale, width / scene.Width);
        if (double.IsFinite(room.Height) && room.Height > 0) scale = Math.Min(scale, room.Height / scene.Height);
        Scene = scene;
        Scale = Math.Max(DiagramView.MinScale, scale);
        return new Size(Math.Ceiling(scene.Width * Scale), Math.Ceiling(scene.Height * Scale));
    }

    public override void Render(DrawingContext context)
    {
        context.FillRectangle(Brushes.Transparent, new Rect(Bounds.Size));
        if (Scene is not { } scene) return;
        bool dark = ActualThemeVariant == ThemeVariant.Dark;
        var ink = Solid(Ink, dark ? Colors.White : Color.Parse("#1D1D1F"));
        var line = Line ?? new SolidColorBrush(ink.Color, 0.6);
        var accent = Solid(Accent, Color.Parse("#0A84A0")).Color;
        Color? tint = AccentTint is ISolidColorBrush t ? t.Color : null;
        var (fill, stroke) = DiagramColours.Neutral(dark, ink.Color);
        // At full size, boxes and words land on whole pixels so their hairlines stay crisp.
        bool snap = Math.Abs(Scale - 1) < 0.001;
        using var _ = context.PushTransform(Matrix.CreateScale(Scale, Scale));

        foreach (var g in scene.Groups)
        {
            context.DrawRectangle(GroupFill, null, new RoundedRect(Snap(ToRect(g.Box), snap, false), 12));
            DrawText(context, g.Title, DiagramLayout.TitleSize, FontWeight.SemiBold, line, g.TitleAt, snap);
        }

        var labelled = scene.Edges.Where(e => e.LabelLines.Count > 0).ToList();
        IDisposable? clip = null;
        if (labelled.Count > 0)
        {
            // The lines stop short of their words, so the words need no background of their own.
            var gaps = new GeometryGroup { FillRule = FillRule.NonZero };
            foreach (var e in labelled) gaps.Children.Add(new RectangleGeometry(ToRect(e.LabelBox.Inflate(2))));
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

    void DrawCentred(DrawingContext context, string text, double size, FontWeight weight, IBrush brush, double centreX, double top, double lineHeight, bool snap)
    {
        var t = Text(text, size, weight, brush);
        double x = centreX - t.WidthIncludingTrailingWhitespace / 2, y = top + (lineHeight - t.Height) / 2;
        context.DrawText(t, snap ? new Point(Math.Round(x), Math.Round(y)) : new Point(x, y));
    }

    void DrawText(DrawingContext context, string text, double size, FontWeight weight, IBrush brush, Pt at, bool snap)
    {
        var t = Text(text, size, weight, brush);
        context.DrawText(t, snap ? new Point(Math.Round(at.X), Math.Round(at.Y)) : P(at));
    }

    FormattedText Text(string text, double size, FontWeight weight, IBrush brush) =>
        new(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface(Family, FontStyle.Normal, weight), size, brush);

    static SolidColorBrush Solid(IBrush? brush, Color fallback) => brush as SolidColorBrush ?? new SolidColorBrush(brush is ISolidColorBrush s ? s.Color : fallback);

    static Point P(Pt p) => new(p.X, p.Y);

    static Rect ToRect(Box b) => new(b.X, b.Y, b.W, b.H);

    /// <summary>A rectangle on whole pixels; a 1 px outline's on the half pixel, so it covers one row exactly.</summary>
    static Rect Snap(Rect r, bool snap, bool hairline)
    {
        if (!snap) return r;
        double x = Math.Round(r.X), y = Math.Round(r.Y), right = Math.Round(r.Right), bottom = Math.Round(r.Bottom);
        return hairline ? new Rect(x + 0.5, y + 0.5, right - x - 1, bottom - y - 1) : new Rect(x, y, right - x, bottom - y);
    }
}

/// <summary>
/// Laid-out scenes, kept for the 64 diagrams drawn last (keyed by the chart, the font and the direction), so a note
/// shown again or a theme switched redraws without laying anything out. Scenes hold no colours.
/// </summary>
static class SceneCache
{
    const int Capacity = 64;
    static readonly Dictionary<string, LinkedListNode<(string Key, DiagramScene Scene)>> map = [];
    static readonly LinkedList<(string Key, DiagramScene Scene)> order = new();
    static readonly Lock gate = new();

    public static DiagramScene Get(Flowchart chart, FontFamily family, ChartDirection? direction)
    {
        string key = $"{family.Name}\u0001{direction}\u0001{chart.ToSource()}";
        lock (gate)
        {
            if (map.TryGetValue(key, out var hit))
            {
                order.Remove(hit);
                order.AddFirst(hit);
                return hit.Value.Scene;
            }
        }
        var scene = DiagramLayout.Lay(chart, Measurer(family), direction);
        lock (gate)
        {
            if (map.TryGetValue(key, out var raced)) return raced.Value.Scene;
            map[key] = order.AddFirst((key, scene));
            while (order.Count > Capacity)
            {
                map.Remove(order.Last!.Value.Key);
                order.RemoveLast();
            }
        }
        return scene;
    }

    /// <summary>
    /// Text widths in the look's own font: box words in its medium weight, group titles (12 px) in semibold, the
    /// words on arrows in regular.
    /// </summary>
    public static Func<string, double, bool, double> Measurer(FontFamily family) => (text, size, bold) =>
    {
        var weight = !bold ? FontWeight.Normal : size <= DiagramLayout.TitleSize ? FontWeight.SemiBold : FontWeight.Medium;
        return new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface(family, FontStyle.Normal, weight), size, null)
            .WidthIncludingTrailingWhitespace;
    };
}
