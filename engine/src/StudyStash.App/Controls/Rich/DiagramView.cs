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
    public DiagramView(bool opensLarger) : this(opensLarger, fitWhole: false) { }

    /// <summary><paramref name="fitWhole"/>: on paper, where there's nothing to scroll, the whole picture fits the room
    /// it's given (the column, and the page's height), however small that makes it, rather than stopping at
    /// <see cref="MinScale"/>; and it doesn't open larger.</summary>
    public DiagramView(bool opensLarger, bool fitWhole)
    {
        canvas.FitWhole = fitWhole;
        scroller = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = canvas,
        };
        HorizontalAlignment = HorizontalAlignment.Center;
        canvas.Failed = ShowProblem;
        if (fitWhole)
        {
            scroller.Content = null;
            Child = canvas;
            return;
        }
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

    /// <summary>It couldn't be laid out, so it shows its source instead of a picture.</summary>
    public bool HasFailed => failed;

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
        AutomationProperties.SetHelpText(this, null);
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

    /// <summary>No floor on the scale: a diagram on paper (see <see cref="DiagramView(bool, bool)"/>).</summary>
    public bool FitWhole { get; set; }

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
        return FitWhole ? scale : Math.Max(DiagramView.MinScale, scale);
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
        // Boxes and words land on whole pixels of the screen it's drawn on, so their hairlines stay crisp: at 125%,
        // 150% or 175% a whole point isn't a whole pixel, so they snap to the screen's own pixels, not to points.
        double snap = Scale * Zoom * (TopLevel.GetTopLevel(this)?.RenderScaling ?? 1);
        Painter(scene).Draw(context, Scale, snap, Look);
    }

    DiagramPainter? painter;

    /// <summary>The painter for <paramref name="scene"/> in the look's colours now: the same one frame after frame
    /// until the scene, the colours or the font change.</summary>
    internal DiagramPainter Painter(DiagramScene scene)
    {
        bool dark = ActualThemeVariant == ThemeVariant.Dark;
        var ink = Solid(Ink, dark ? Colors.White : Color.Parse("#1D1D1F"));
        var line = Line ?? new SolidColorBrush(ink.Color, 0.6);
        var accent = Solid(Accent, Color.Parse("#0A84A0")).Color;
        Color? tint = AccentTint is ISolidColorBrush t ? t.Color : null;
        var quiet = Quiet ?? Line ?? Brushes.Gray;
        if (painter is { } p && ReferenceEquals(p.Scene, scene) && p.Palette.Dark == dark && ReferenceEquals(p.Palette.Ink, ink)
            && ReferenceEquals(p.Palette.Line, line) && ReferenceEquals(p.Palette.GroupFill, GroupFill) && p.Palette.Accent == accent
            && p.Palette.Tint == tint && ReferenceEquals(p.Palette.Quiet, quiet) && ReferenceEquals(p.Family, Family))
            return p;
        return painter = new DiagramPainter(scene, new DiagramPalette(dark, ink, line, GroupFill, accent, tint, quiet), Family);
    }

    /// <summary>What's shown over the picture while it's explored (null: the still picture).</summary>
    public DiagramLook? Look
    {
        get => look;
        set
        {
            look = value;
            InvalidateVisual();
        }
    }

    DiagramLook? look;

    /// <summary>How far the picture is zoomed on top of its scale (by its view's zoom), so its hairlines snap to
    /// the pixels they land on.</summary>
    public double Zoom
    {
        get => zoom;
        set
        {
            if (Math.Abs(zoom - value) < 1e-9) return;
            zoom = value;
            InvalidateVisual();
        }
    }

    double zoom = 1;

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

    FormattedText Text(string text, double size, FontWeight weight, IBrush brush) =>
        new(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface(Family, FontStyle.Normal, weight), size, brush);

    static SolidColorBrush Solid(IBrush? brush, Color fallback) => brush as SolidColorBrush ?? new SolidColorBrush(brush is ISolidColorBrush s ? s.Color : fallback);

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
