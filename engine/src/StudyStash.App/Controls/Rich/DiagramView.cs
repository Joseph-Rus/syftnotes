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
/// note). On screen it can be explored (<see cref="DiagramExplorer"/>): a box under the pointer lights its arrows, a
/// click pins it with its actions, it zooms and pans, steps through, folds its groups and hides its words to test
/// yourself; in a note a click on its paper (or its corner's button) opens it larger (<see cref="OpenLarger"/>), in a
/// window of its own it fills the window. On paper it's only ever the still picture. A chart that can't be laid out
/// becomes the calm card that says so, with its source.
/// </summary>
public sealed class DiagramView : Decorator
{
    public static readonly StyledProperty<Flowchart?> ChartProperty = AvaloniaProperty.Register<DiagramView, Flowchart?>(nameof(Chart));
    public static readonly StyledProperty<double> MaxScaleProperty = AvaloniaProperty.Register<DiagramView, double>(nameof(MaxScale), 1);

    /// <summary>The smallest the picture is drawn: 13 px box words stay at least 8 px.</summary>
    public const double MinScale = 0.6;

    /// <summary>What a screen reader hears it does.</summary>
    public const string Help = "Arrow keys move along the arrows and Enter picks a box. S steps through it, H hides its words to test yourself. Enter with no box picked opens it larger.";

    /// <summary>The same, in a window of its own.</summary>
    public const string WindowHelp = "Arrow keys move along the arrows and Enter picks a box. S steps through it, H hides its words to test yourself, plus and minus zoom.";

    readonly DiagramCanvas canvas = new();
    readonly ScrollViewer? scroller;
    readonly DiagramExplorer? explorer;
    readonly DiagramChrome? chrome;
    readonly Border? ring;
    readonly Panel? stage;
    readonly bool windowed;
    bool failed;

    static DiagramView() => AffectsMeasure<DiagramView>(ChartProperty, MaxScaleProperty);

    public DiagramView() : this(opensLarger: true) { }

    /// <summary>A diagram that opens larger on a click (in a note), or one that doesn't (the larger window's own).</summary>
    public DiagramView(bool opensLarger) : this(opensLarger, fitWhole: false) { }

    /// <summary><paramref name="fitWhole"/>: on paper, where there's nothing to scroll, the whole picture fits the room
    /// it's given (the column, and the page's height), however small that makes it, rather than stopping at
    /// <see cref="MinScale"/>; and it doesn't open larger, nor answer the pointer.</summary>
    public DiagramView(bool opensLarger, bool fitWhole)
    {
        canvas.FitWhole = fitWhole;
        canvas.Failed = ShowProblem;
        HorizontalAlignment = HorizontalAlignment.Center;
        if (fitWhole)
        {
            Child = canvas;
            return;
        }
        windowed = !opensLarger;
        Focusable = true;
        // Its own ring shows the keyboard is on it (round the picture, or round a box), not the theme's rectangle
        // round the whole column.
        FocusAdorner = null;
        AutomationProperties.SetHelpText(this, windowed ? WindowHelp : Help);
        ring = new Border { BorderThickness = new Thickness(2), CornerRadius = new CornerRadius(8), Margin = new Thickness(-4), IsHitTestVisible = false, IsVisible = false };
        ring.Bind(Border.BorderBrushProperty, ring.GetResourceObservable("Accent"));
        if (windowed)
        {
            canvas.HorizontalAlignment = HorizontalAlignment.Center;
            canvas.VerticalAlignment = VerticalAlignment.Center;
            canvas.Margin = new Thickness(WindowPad);
            stage = new Panel { ClipToBounds = true, Background = Brushes.Transparent, Children = { canvas } };
            HorizontalAlignment = HorizontalAlignment.Stretch;
            VerticalAlignment = VerticalAlignment.Stretch;
            explorer = new DiagramExplorer(this, canvas, stage, windowed: true);
        }
        else
        {
            scroller = new ScrollViewer
            {
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = canvas,
            };
            stage = new Panel { HorizontalAlignment = HorizontalAlignment.Center, Children = { scroller, ring } };
            Cursor = new Cursor(StandardCursorType.Hand);
            HorizontalAlignment = HorizontalAlignment.Stretch;
            explorer = new DiagramExplorer(this, canvas, canvas, windowed: false);
        }
        chrome = new DiagramChrome(explorer, this, windowed);
        stage.Children.Add(chrome.Actions);
        stage.Children.Add(chrome.Announcer);
        if (windowed) Child = stage; // its strip says how it works
        else
        {
            // The toolbar and the hint sit in the column's corners, clear of a picture narrower than the column.
            var picture = new Panel { Children = { stage, chrome.Tools, chrome.Hint } };
            var rows = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto") };
            rows.Children.Add(picture);
            Grid.SetRow(chrome.Strip, 1);
            rows.Children.Add(chrome.Strip);
            Child = rows;
        }
        explorer.Changed += Place;
        explorer.Zoomer.Changed += Place;
        PointerEntered += (_, _) => chrome.Update();
        PointerExited += (_, _) => chrome.Update();
        GotFocus += (_, e) =>
        {
            keyboard = e.NavigationMethod is NavigationMethod.Tab or NavigationMethod.Directional;
            Focused();
        };
        // A click gives it the keyboard without scrolling the page to show all of it (the box under the pointer would
        // move away mid-click); the keyboard's Tab still brings it into view.
        AddHandler(RequestBringIntoViewEvent, (_, e) =>
        {
            if (ReferenceEquals(e.TargetObject, this) && !keyboard) e.Handled = true;
        });
        LostFocus += (_, _) =>
        {
            keyboard = false;
            Focused();
        };
        KeyDown += (_, e) =>
        {
            if (e.Handled || explorer.Written is null) return;
            if (explorer.Key(e.Key, e.KeyModifiers))
            {
                keyboard = true;
                e.Handled = true;
                Focused();
            }
        };
    }

    /// <summary>The margin round the picture in a window of its own.</summary>
    public const double WindowPad = 32;

    bool keyboard;

    /// <summary>Whether the keyboard (not a click) brought it focus: its ring shows only then.</summary>
    internal bool KeyboardFocused => keyboard && IsKeyboardFocusWithin;

    void Focused()
    {
        if (ring is not null) ring.IsVisible = KeyboardFocused && explorer?.Ring is null;
        explorer?.FocusChanged();
        chrome?.Update();
    }

    /// <summary>What explores it (null on paper).</summary>
    internal DiagramExplorer? Explorer => explorer;

    /// <summary>The controls round it (null on paper): a window of its own puts its toolbar and strip where it wants them.</summary>
    internal DiagramChrome? Chrome => chrome;

    /// <summary>The diagram in the note this window's diagram was opened from: questions about it go to that note's
    /// lecture.</summary>
    public Control? Origin { get; set; }

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

    /// <summary>What the diagram is called: its own title, or the note's bold line just above it, or else its first
    /// words.</summary>
    public string Title => Named ?? Chart?.Labels().FirstOrDefault() ?? "Diagram";

    /// <summary>The diagram's own title, or the bold line the note gives it (the diagram pass writes one above each),
    /// or null when it has neither.</summary>
    public string? Named => Chart?.Title is { Length: > 0 } t ? t : Caption is { Length: > 0 } c ? c : null;

    /// <summary>The bold line just above it in the note, if there's one (its title, as the diagram pass writes it).</summary>
    public string? Caption { get; init; }

    /// <summary>The chart as the note wrote it, shown on the card if it can't be laid out (else its own
    /// canonical source is).</summary>
    public string? Source { get; init; }

    /// <summary>The groups to show folded when it opens (a window opened from a note shows them as the note did); null
    /// lets a big chart made of groups open as its overview.</summary>
    public IEnumerable<string>? Folded { get; init; }

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
            if (explorer is not null) explorer.Load(Chart, Folded);
            else canvas.Chart = Chart;
            AutomationProperties.SetName(this, Chart is { } chart ? "Diagram: " + string.Join(", ", chart.Labels()) : null);
        }
        else if (change.Property == MaxScaleProperty) canvas.MaxScale = MaxScale;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        // The scroller measures its content as if it had all the width in the world; the picture needs the column's
        // (less the strip under it, while one shows, where the column's height is limited).
        var room = availableSize;
        if (windowed) room = new Size(Math.Max(0, room.Width - 2 * WindowPad), Math.Max(0, room.Height - 2 * WindowPad));
        else if (chrome is { Strip.IsVisible: true } c && double.IsFinite(room.Height))
        {
            c.Strip.Measure(new Size(room.Width, double.PositiveInfinity));
            room = room.WithHeight(Math.Max(0, room.Height - c.Strip.DesiredSize.Height));
        }
        canvas.Room = room;
        return base.MeasureOverride(availableSize);
    }

    /// <summary>Opens it larger, in a window of its own (unless something on the way up shows it another way).</summary>
    public void OpenLarger()
    {
        if (failed || Chart is not { } chart || windowed) return;
        explorer?.HintSeen();
        Rich.OpenLarger.Raise(this, () => new OpenDiagramEventArgs(this)
        {
            // The window sizes itself to the chart laid out its own way (a left-to-right chart turned to fit a narrow
            // column reads left to right again in a window wide enough).
            Title = Title, Chart = chart, Scene = canvas.AsWritten() ?? Scene, Written = Source, Folded = explorer?.Folded.ToList(),
        });
    }

    /// <summary>Puts a pinned box's actions just under it (or over it, where there's no room under), and keeps them
    /// there as the picture zooms and pans.</summary>
    void Place()
    {
        if (chrome is null || explorer is null || stage is null) return;
        chrome.Update();
        if (!chrome.Actions.IsVisible || explorer.Pinned is not { } id || Scene?.Nodes.FirstOrDefault(n => n.Id == id) is not { } n) return;
        double s = canvas.Scale;
        var top = canvas.TranslatePoint(new Point(n.Box.X * s, n.Box.Y * s), stage);
        var bottom = canvas.TranslatePoint(new Point(n.Box.X * s, n.Box.Bottom * s), stage);
        if (top is not { } t || bottom is not { } b) return;
        chrome.Actions.Measure(Size.Infinity);
        var size = chrome.Actions.DesiredSize;
        double x = Math.Clamp(b.X, 4, Math.Max(4, stage.Bounds.Width - size.Width - 4));
        double y = b.Y + 6 + size.Height <= stage.Bounds.Height ? b.Y + 6 : t.Y - 6 - size.Height >= 0 ? t.Y - 6 - size.Height : Math.Max(0, stage.Bounds.Height - size.Height - 4);
        chrome.Actions.Margin = new Thickness(x, y, 0, 0);
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

    /// <summary>The chart laid out the way it's written (not turned to fit), if it has been.</summary>
    public DiagramScene? AsWritten() => chart is not null && source is not null ? SceneCache.Find(chart, source, Family, null).Scene ?? Scene : Scene;

    /// <summary>Whether a fold is gliding from one scene to the next.</summary>
    public bool IsMorphing => morph is not null;

    public double Scale { get; private set; } = 1;

    /// <summary>No floor on the scale: a diagram on paper (see <see cref="DiagramView(bool, bool)"/>).</summary>
    public bool FitWhole { get; set; }

    FontFamily Family => Font ?? FontFamily.Default;

    protected override Size MeasureOverride(Size availableSize)
    {
        Laying = false;
        if (chart is null || source is null)
        {
            SetScene(null);
            return default;
        }
        double width = double.IsFinite(room.Width) ? room.Width : double.PositiveInfinity;
        // Never lays anything out here: what isn't laid out yet is asked for, and a placeholder keeps its space.
        var written = SceneCache.Find(chart, source, Family, null);
        if (written.Failed)
        {
            SetScene(null);
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
            if (morph is { Run: null } waiting)
            {
                // The old picture stays as it was until the new one is ready to take its place.
                Laying = false;
                return waiting.FromSize;
            }
            SetScene(null);
            Laying = true;
            Scale = Fit(guess.Width, guess.Height, width);
            return new Size(Math.Ceiling(Math.Min(guess.Width * Scale, width)), Math.Ceiling(guess.Height * Scale));
        }
        var scene = written.Scene!;
        if (other?.Scene is { } otherScene && otherScene.Width < scene.Width) scene = otherScene;
        SetScene(scene);
        Scale = Fit(scene.Width, scene.Height, width);
        var size = new Size(Math.Ceiling(scene.Width * Scale), Math.Ceiling(scene.Height * Scale));
        if (morph is { } m)
        {
            // Folding or opening a group: the room it takes grows or shrinks with the picture, not in one jump.
            if (m.Run is null) StartMorph(m, size);
            return new Size(Math.Ceiling(Lerp(m.FromSize.Width, size.Width, m.T)), Math.Ceiling(Lerp(m.FromSize.Height, size.Height, m.T)));
        }
        return size;
    }

    static double Lerp(double a, double b, double t) => a + (b - a) * t;

    void SetScene(DiagramScene? scene)
    {
        if (ReferenceEquals(Scene, scene)) return;
        Scene = scene;
        if (SceneChanged is { } changed) Dispatcher.UIThread.Post(() => changed());
    }

    /// <summary>Called (after the measure) whenever the picture's scene changes: first laid out, folded, turned.</summary>
    public event Action? SceneChanged;

    // --- folding and opening groups, as a change the eye can follow ----------------------------------------------------

    sealed class Morphing(DiagramPainter from, double fromScale, Size fromSize, FoldedChart before, FoldedChart after)
    {
        public DiagramPainter From { get; } = from;
        public double FromScale { get; } = fromScale;
        public Size FromSize { get; } = fromSize;
        public FoldedChart Before { get; } = before;
        public FoldedChart After { get; } = after;
        public double T { get; set; }
        public IDisposable? Run { get; set; }
    }

    Morphing? morph;

    /// <summary>The next scene (the chart <paramref name="after"/> some groups were folded or opened) arrives as a change
    /// from this one (<paramref name="before"/>): each box glides from where it was to where it goes, a group's boxes
    /// into its folded box or out of it, and the lines fade across. Null: the next scene simply replaces this one.</summary>
    public void Morph(FoldedChart? before, FoldedChart? after)
    {
        morph?.Run?.Dispose();
        morph = null;
        if (before is null || after is null || Scene is not { } scene || Laying) return;
        morph = new Morphing(Painter(scene), Scale, Bounds.Size, before, after);
    }

    void StartMorph(Morphing m, Size to)
    {
        m.Run = Platform.Motion.Animate(this, TimeSpan.FromMilliseconds(320), t =>
        {
            m.T = t;
            InvalidateMeasure();
            InvalidateVisual();
        }, () =>
        {
            if (ReferenceEquals(morph, m)) morph = null;
            InvalidateMeasure();
            InvalidateVisual();
        });
    }

    /// <summary>A frame of the change from one scene to the next.</summary>
    void DrawMorph(DrawingContext context, Morphing m, DiagramPainter to)
    {
        double t = m.T, fs = m.FromScale, ts = Scale;
        static double Ease(double x) => Math.Clamp(x, 0, 1);
        using (context.PushOpacity(1 - Ease(t * 1.8))) m.From.Draw(context, fs, 0, null, nodes: false);
        using (context.PushOpacity(Ease((t - 0.35) / 0.65))) to.Draw(context, ts, 0, null, nodes: false);
        var fromBoxes = m.From.Scene.Nodes.ToDictionary(n => n.Id, n => Scaled(n.Box, fs));
        var toBoxes = to.Scene.Nodes.ToDictionary(n => n.Id, n => Scaled(n.Box, ts));
        var beforeLook = new DiagramLook { Folded = m.Before.Inside.Keys.ToHashSet() };
        var afterLook = new DiagramLook { Folded = m.After.Inside.Keys.ToHashSet() };
        // What's going: each box into the box that shows it now (its folded group), fading.
        foreach (var n in m.From.Scene.Nodes)
        {
            if (toBoxes.ContainsKey(n.Id)) continue;
            Rect end = m.After.Shown.TryGetValue(n.Id, out string? into) && toBoxes.TryGetValue(into, out var r) ? Shrink(r) : Shrink(fromBoxes[n.Id]);
            Node(context, m.From, n, Between(fromBoxes[n.Id], end, t), 1 - t, beforeLook);
        }
        // What stays, moving to its new place; what's new, coming out of the box that showed it (or gathering from the
        // boxes it now holds).
        foreach (var n in to.Scene.Nodes)
        {
            Rect start;
            double opacity = 1;
            if (fromBoxes.TryGetValue(n.Id, out var was)) start = was;
            else
            {
                opacity = t;
                var members = m.After.Shown.Where(s => s.Value == n.Id && fromBoxes.ContainsKey(s.Key)).Select(s => fromBoxes[s.Key]).ToList();
                start = m.Before.Shown.TryGetValue(n.Id, out string? from) && fromBoxes.TryGetValue(from, out var folded) ? Shrink(folded)
                    : members.Count > 0 ? members.Aggregate((a, b) => a.Union(b)) : Shrink(toBoxes[n.Id]);
            }
            Node(context, to, n, Between(start, toBoxes[n.Id], t), opacity, afterLook);
        }
    }

    static Rect Scaled(Box b, double s) => new(b.X * s, b.Y * s, b.W * s, b.H * s);

    static Rect Shrink(Rect r) => new(r.Center.X - r.Width * 0.2, r.Center.Y - r.Height * 0.2, r.Width * 0.4, r.Height * 0.4);

    static Rect Between(Rect a, Rect b, double t) => new(Lerp(a.X, b.X, t), Lerp(a.Y, b.Y, t), Lerp(a.Width, b.Width, t), Lerp(a.Height, b.Height, t));

    /// <summary>A box drawn into <paramref name="at"/> (the canvas's pixels), its words scaled with it.</summary>
    static void Node(DrawingContext context, DiagramPainter painter, SceneNode n, Rect at, double opacity, DiagramLook look)
    {
        if (opacity <= 0.01 || n.Box.W <= 0 || n.Box.H <= 0) return;
        double k = Math.Sqrt(at.Width * at.Height / (n.Box.W * n.Box.H));
        var c = n.Box.Center;
        using (context.PushOpacity(opacity))
        using (context.PushTransform(Matrix.CreateTranslation(-c.X, -c.Y) * Matrix.CreateScale(k, k) * Matrix.CreateTranslation(at.Center.X, at.Center.Y)))
            painter.DrawNode(context, n, 0, look);
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
        if (morph is { Run: null } waiting)
        {
            // The picture as it was (its groups as they were) until the new one is laid out.
            double was = waiting.FromScale * Zoom * (TopLevel.GetTopLevel(this)?.RenderScaling ?? 1);
            waiting.From.Draw(context, waiting.FromScale, was, new DiagramLook { Folded = waiting.Before.Inside.Keys.ToHashSet() });
            return;
        }
        if (Scene is not { } scene) return;
        if (morph is { } m)
        {
            DrawMorph(context, m, Painter(scene));
            return;
        }
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
        if (painter is { } p && ReferenceEquals(p.Scene, scene) && p.Palette.Dark == dark && p.Palette.Ink.Color == ink.Color
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
