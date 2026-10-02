using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using SkiaSharp;
using Svg.Skia;
using StudyStash.Core.Rich;

namespace StudyStash.App.Controls.Rich;

/// <summary>
/// A drawing an AI wrote as SVG, made safe (<see cref="SafeSvg"/>) and drawn in the look's fonts and colours, light or
/// dark. It shows at its own width (at most 720 px; a tiny one a little larger), scaled down to fit its column but
/// never below <see cref="MinScale"/> of that (past it, it scrolls sideways). In a note, a click opens it larger. An
/// illustration (a drawing whose parts are named) can be explored on screen like a flowchart
/// (<see cref="PartsExplorer"/>): its parts light up under the pointer, a click pins one with its name and line, its
/// labels turn off and on, a tour walks its parts and Test yourself hides its labels; on paper it's the still picture.
/// </summary>
public sealed class SvgView : Decorator
{
    public static readonly StyledProperty<string?> SourceProperty = AvaloniaProperty.Register<SvgView, string?>(nameof(Source));
    public static readonly StyledProperty<double> MaxScaleProperty = AvaloniaProperty.Register<SvgView, double>(nameof(MaxScale), 1);

    /// <summary>The smallest a drawing is shown: 13-unit words stay at least 8 px.</summary>
    public const double MinScale = 0.6;

    readonly SvgCanvas canvas = new();
    readonly bool opensLarger, fitWhole;
    PartsExplorer? explorer;
    PartsChrome? chrome;
    Panel? stage;
    Border? ring;
    bool keyboard;

    static SvgView() => AffectsMeasure<SvgView>(SourceProperty, MaxScaleProperty);

    public SvgView() : this(opensLarger: true) { }

    /// <summary>A drawing that opens larger on a click (in a note), or one that doesn't (the larger window's own).</summary>
    public SvgView(bool opensLarger) : this(opensLarger, fitWhole: false) { }

    /// <summary><paramref name="fitWhole"/>: on paper, where there's nothing to scroll, the whole drawing fits the room
    /// it's given (the column, and the page's height), however small that makes it, rather than stopping at
    /// <see cref="MinScale"/>; and it doesn't open larger.</summary>
    public SvgView(bool opensLarger, bool fitWhole)
    {
        this.opensLarger = opensLarger;
        this.fitWhole = fitWhole;
        canvas.FitWhole = fitWhole;
        HorizontalAlignment = HorizontalAlignment.Center;
        if (fitWhole)
        {
            Child = canvas;
            return;
        }
        var scroller = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = canvas,
        };
        if (!opensLarger)
        {
            Child = scroller;
            return;
        }
        var badge = Rich.OpenLarger.Badge();
        Child = new Panel { Children = { scroller, badge } };
        // An illustration opens larger from its own toolbar, or a click on its paper (see Explorable).
        Rich.OpenLarger.Wire(this, badge, () => explorer is null && Source is { } svg && Drawing?.Svg is not null ? new OpenDiagramEventArgs(this) { Title = Title, Svg = svg } : null);
    }

    /// <summary>The margin round an illustration in a window of its own.</summary>
    public const double WindowPad = 32;

    /// <summary>What a screen reader hears an illustration does.</summary>
    public const string Help = "Arrow keys move from part to part and Enter picks one. L turns the labels off and on, S tours the parts, H hides the labels to test yourself. Enter with no part picked opens it larger.";

    public const string WindowHelp = "Arrow keys move from part to part and Enter picks one. L turns the labels off and on, S tours the parts, H hides the labels to test yourself, plus and minus zoom.";

    /// <summary>What explores an illustration (null for a plain drawing, and on paper).</summary>
    internal PartsExplorer? Explorer => explorer;

    /// <summary>The controls round an illustration: a window of its own puts its toolbar and strip where it wants them.</summary>
    internal PartsChrome? Chrome => chrome;

    /// <summary>The drawing in the note this window's drawing was opened from: questions about it go to that note's lecture.</summary>
    public Control? Origin { get; set; }

    /// <summary>Whether the keyboard (not a click) brought it focus: its ring shows only then.</summary>
    internal bool KeyboardFocused => keyboard && IsKeyboardFocusWithin;

    /// <summary>An illustration on screen becomes explorable: in a note, its picture with a toolbar in the corner, a
    /// pinned part's card beside it and a strip under it while it's toured or tested; in a window of its own, the
    /// picture filling the window, its toolbar and strip placed by the window.</summary>
    void Explorable()
    {
        if (explorer is not null || fitWhole) return;
        Focusable = true;
        FocusAdorner = null;
        Cursor = null;
        bool windowed = !opensLarger;
        AutomationProperties.SetHelpText(this, windowed ? WindowHelp : Help);
        ring = new Border { BorderThickness = new Thickness(2), CornerRadius = new CornerRadius(8), Margin = new Thickness(-4), IsHitTestVisible = false, IsVisible = false };
        ring.Bind(Border.BorderBrushProperty, ring.GetResourceObservable("Accent"));
        if (Child is Panel old) old.Children.Clear();
        if (windowed)
        {
            if (Child is ScrollViewer sv) sv.Content = null;
            canvas.HorizontalAlignment = HorizontalAlignment.Center;
            canvas.VerticalAlignment = VerticalAlignment.Center;
            canvas.Margin = new Thickness(WindowPad);
            stage = new Panel { ClipToBounds = true, Background = Brushes.Transparent, Children = { canvas } };
            HorizontalAlignment = HorizontalAlignment.Stretch;
            VerticalAlignment = VerticalAlignment.Stretch;
            explorer = new PartsExplorer(this, canvas, stage, windowed: true);
        }
        else
        {
            var scroller = new ScrollViewer { HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled };
            if (Child is Panel { } p) p.Children.Clear();
            DetachCanvas();
            scroller.Content = canvas;
            stage = new Panel { HorizontalAlignment = HorizontalAlignment.Center, Children = { scroller, ring } };
            HorizontalAlignment = HorizontalAlignment.Stretch;
            explorer = new PartsExplorer(this, canvas, canvas, windowed: false);
        }
        canvas.Explorer = explorer;
        chrome = new PartsChrome(explorer, this, windowed);
        stage.Children.Add(chrome.Actions);
        stage.Children.Add(chrome.Announcer);
        if (windowed) Child = stage;
        else
        {
            var picture = new Panel { Children = { stage, chrome.Tools, chrome.Hint } };
            var rows = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto") };
            rows.Children.Add(picture);
            Grid.SetRow(chrome.Strip, 1);
            rows.Children.Add(chrome.Strip);
            Child = rows;
        }
        explorer.Changed += Place;
        PointerEntered += (_, _) => chrome.Update();
        PointerExited += (_, _) => chrome.Update();
        GotFocus += (_, e) =>
        {
            keyboard = e.NavigationMethod is NavigationMethod.Tab or NavigationMethod.Directional;
            Focused();
        };
        AddHandler(RequestBringIntoViewEvent, (_, e) =>
        {
            if (ReferenceEquals(e.TargetObject, this) && !keyboard) e.Handled = true;
        });
        LostFocus += (_, _) =>
        {
            keyboard = false;
            Focused();
        };
        // Ahead of the open-larger keys every drawing has: Enter and Space are the parts' here.
        AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Handled || explorer.Drawing is null) return;
            if (explorer.Key(e.Key, e.KeyModifiers))
            {
                keyboard = true;
                e.Handled = true;
                Focused();
            }
        }, RoutingStrategies.Tunnel);
    }

    void DetachCanvas()
    {
        switch (canvas.Parent)
        {
            case ScrollViewer sv:
                sv.Content = null;
                break;
            case Panel panel:
                panel.Children.Remove(canvas);
                break;
            case Decorator d:
                d.Child = null;
                break;
        }
    }

    void Focused()
    {
        if (ring is not null) ring.IsVisible = KeyboardFocused && explorer?.Ring is null;
        explorer?.FocusChanged();
        chrome?.Update();
    }

    /// <summary>Opens it larger, in a window of its own (unless something on the way up shows it another way).</summary>
    public void OpenLarger()
    {
        if (!opensLarger || fitWhole || Source is not { } svg || Drawing?.Svg is null) return;
        explorer?.HintSeen();
        Rich.OpenLarger.Raise(this, () => new OpenDiagramEventArgs(this) { Title = Title, Svg = svg });
    }

    /// <summary>Puts a pinned part's card just under it (or over it, where there's no room under), and keeps it there
    /// as the picture zooms and pans.</summary>
    void Place()
    {
        if (chrome is null || explorer is null || stage is null) return;
        chrome.Update();
        if (!chrome.Actions.IsVisible || explorer.Pinned is not { } id || explorer.Where(id) is not { } r) return;
        var top = canvas.TranslatePoint(r.TopLeft, stage);
        var bottom = canvas.TranslatePoint(r.BottomLeft, stage);
        if (top is not { } t || bottom is not { } b) return;
        chrome.Actions.Measure(Size.Infinity);
        var size = chrome.Actions.DesiredSize;
        double x = Math.Clamp(b.X, 4, Math.Max(4, stage.Bounds.Width - size.Width - 4));
        double y = b.Y + 6 + size.Height <= stage.Bounds.Height ? b.Y + 6 : t.Y - 6 - size.Height >= 0 ? t.Y - 6 - size.Height : Math.Max(0, stage.Bounds.Height - size.Height - 4);
        chrome.Actions.Margin = new Thickness(x, y, 0, 0);
    }

    /// <summary>The SVG as the AI wrote it.</summary>
    public string? Source
    {
        get => GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    public double MaxScale
    {
        get => GetValue(MaxScaleProperty);
        set => SetValue(MaxScaleProperty, value);
    }

    /// <summary>The drawing made safe, in its written colours: its size, title and words (or why it can't be shown).</summary>
    public SafeSvgResult? Drawing => canvas.Written;

    /// <summary>What the drawing is called: its title, or else its first words.</summary>
    public string Title => Drawing?.Title is { Length: > 0 } t ? t : "Diagram";

    /// <summary>How much the drawing is scaled from its own width to fit.</summary>
    public double Scale => canvas.Scale;


    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == SourceProperty)
        {
            canvas.Source = Source;
            var words = Drawing is { Svg: not null } d ? new[] { d.Title }.Concat(d.Texts).Where(w => w.Length > 0).Distinct() : [];
            AutomationProperties.SetName(this, Drawing?.Svg is null ? null
                : Drawing.Illustrated ? $"Illustration: {Title}. Parts: {string.Join(", ", Drawing.Parts.Select(p => p.Name))}"
                : "Diagram: " + string.Join(", ", words));
            if (Drawing is { Svg: not null, Illustrated: true } art && !fitWhole)
            {
                Explorable();
                explorer!.Load(art);
            }
        }
        else if (change.Property == MaxScaleProperty) canvas.MaxScale = MaxScale;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var room = availableSize;
        if (explorer is { Windowed: true }) room = new Size(Math.Max(0, room.Width - 2 * WindowPad), Math.Max(0, room.Height - 2 * WindowPad));
        else if (chrome is { Strip.IsVisible: true } c && double.IsFinite(room.Height))
        {
            c.Strip.Measure(new Size(room.Width, double.PositiveInfinity));
            room = room.WithHeight(Math.Max(0, room.Height - c.Strip.DesiredSize.Height));
        }
        canvas.Room = room;
        return base.MeasureOverride(availableSize);
    }

    public override void Render(DrawingContext context) => context.FillRectangle(Brushes.Transparent, new Rect(Bounds.Size));

    /// <summary>How wide a drawing <paramref name="width"/> units wide shows at full size: as wide as it is, up to
    /// 720 px; a small one grows a little (up to 1.25 times, toward 480 px) so its words aren't tiny.</summary>
    public static double Natural(double width) => Math.Min(720, Math.Max(width, Math.Min(width * 1.25, 480)));

    /// <summary>The font a look's drawings are set in: one Skia finds by name, with real weights, on each system.</summary>
    public static string Font => Skin.Current == SkinKind.Mac ? "Helvetica Neue, Arial, sans-serif" : "Segoe UI, Arial, sans-serif";
}

/// <summary>What <see cref="SvgView"/> scrolls: the picture, cleaned again in the look's colours whenever they change.</summary>
sealed class SvgCanvas : Control
{
    public static readonly StyledProperty<IBrush?> InkProperty = AvaloniaProperty.Register<SvgCanvas, IBrush?>(nameof(Ink));
    public static readonly StyledProperty<IBrush?> SecondaryProperty = AvaloniaProperty.Register<SvgCanvas, IBrush?>(nameof(Secondary));
    public static readonly StyledProperty<IBrush?> FaintProperty = AvaloniaProperty.Register<SvgCanvas, IBrush?>(nameof(Faint));
    public static readonly StyledProperty<IBrush?> AccentProperty = AvaloniaProperty.Register<SvgCanvas, IBrush?>(nameof(Accent));
    public static readonly StyledProperty<IBrush?> BlankProperty = AvaloniaProperty.Register<SvgCanvas, IBrush?>(nameof(Blank));

    string? source;
    double maxScale = 1;
    Size room = new(double.PositiveInfinity, double.PositiveInfinity);
    PictureHold? held;
    string? heldFor;
    string? themed;
    readonly Dictionary<string, PictureHold?> variants = [];
    PartsLook? look;

    /// <summary>What explores the drawing, when it's an illustration on screen.</summary>
    public PartsExplorer? Explorer { get; set; }

    /// <summary>What's shown over the picture while it's explored (null: the still picture).</summary>
    public PartsLook? Look
    {
        get => look;
        set
        {
            if (Equals(look, value)) return;
            look = value;
            InvalidateVisual();
        }
    }

    static SvgCanvas() => AffectsRender<SvgCanvas>(InkProperty, SecondaryProperty, FaintProperty, AccentProperty, BlankProperty);

    public SvgCanvas()
    {
        ActualThemeVariantChanged += (_, _) => InvalidateVisual();
        Bind(InkProperty, this.GetResourceObservable("Fg"));
        Bind(SecondaryProperty, this.GetResourceObservable("Fg2"));
        Bind(FaintProperty, this.GetResourceObservable("Fg3"));
        Bind(AccentProperty, this.GetResourceObservable("Accent"));
        Bind(BlankProperty, this.GetResourceObservable("Fill2"));
    }

    public IBrush? Ink { get => GetValue(InkProperty); set => SetValue(InkProperty, value); }
    public IBrush? Secondary { get => GetValue(SecondaryProperty); set => SetValue(SecondaryProperty, value); }
    public IBrush? Faint { get => GetValue(FaintProperty); set => SetValue(FaintProperty, value); }
    public IBrush? Accent { get => GetValue(AccentProperty); set => SetValue(AccentProperty, value); }
    public IBrush? Blank { get => GetValue(BlankProperty); set => SetValue(BlankProperty, value); }

    public SafeSvgResult? Written { get; private set; }

    public double Scale { get; private set; } = 1;

    public string? Source
    {
        get => source;
        set
        {
            source = value;
            heldFor = null;
            Written = value is null ? null : SafeSvg.Clean(value, new SafeSvgOptions { FontFamily = SvgView.Font });
            InvalidateMeasure();
            InvalidateVisual();
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

    /// <summary>No floor on the scale: a drawing on paper (see <see cref="SvgView(bool, bool)"/>).</summary>
    public bool FitWhole { get; set; }

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

    protected override Size MeasureOverride(Size availableSize)
    {
        if (Written is not { Svg: not null } drawing) return default;
        double natural = SvgView.Natural(drawing.Width), naturalHeight = drawing.Height * natural / drawing.Width;
        double scale = maxScale;
        if (double.IsFinite(room.Width)) scale = Math.Min(scale, room.Width / natural);
        if (double.IsFinite(room.Height) && room.Height > 0) scale = Math.Min(scale, room.Height / naturalHeight);
        Scale = FitWhole ? scale : Math.Max(SvgView.MinScale, scale);
        return new Size(Math.Ceiling(natural * Scale), Math.Ceiling(naturalHeight * Scale));
    }

    public override void Render(DrawingContext context)
    {
        context.FillRectangle(Brushes.Transparent, new Rect(Bounds.Size));
        if (source is null || Written is not { Svg: not null } drawing || Bounds.Width <= 0) return;
        bool dark = ActualThemeVariant == ThemeVariant.Dark;
        var palette = DiagramColours.Svg(dark, Colour(Ink, dark ? Colors.White : Color.Parse("#1D1D1F")), Colour(Secondary, Color.Parse("#6E6E73")), Colour(Faint, Color.Parse("#C7C7CC")));
        // The colours live in the SVG's text, so a new look (or light and dark) is a new clean, and a new picture.
        string key = $"{dark}\u0001{SvgView.Font}\u0001{palette.Ink}\u0001{palette.Secondary}\u0001{palette.LightLines}\u0001{source}";
        if (key != heldFor)
        {
            held?.Dispose();
            ForgetVariants();
            var cleaned = SafeSvg.Clean(source, new SafeSvgOptions { FontFamily = SvgView.Font, Palette = palette, Dark = dark }).Svg;
            held = cleaned is null ? null : SvgPictures.Hold(cleaned);
            heldFor = key;
            themed = cleaned;
        }
        float k = (float)(Bounds.Width / drawing.Width);
        var size = Bounds.Size;
        if (look is { } l && Explorer is { } x)
        {
            DrawExplored(context, l, x, k, size, dark);
            return;
        }
        if (held?.Again() is not { } forOp) return;
        context.Custom(new SkiaOp(new Rect(size), canvas =>
        {
            canvas.ClipRect(new SKRect(0, 0, (float)size.Width, (float)size.Height));
            canvas.Scale(k);
            canvas.DrawPicture(forOp.Picture);
        }, forOp));
    }

    /// <summary>A picture of one version of the drawing in the look's colours (without its labels, a part alone, a
    /// label alone...), made the first time it's wanted and kept until the colours change.</summary>
    PictureHold? Variant(string name, Func<string, string> make)
    {
        if (themed is null) return null;
        if (!variants.TryGetValue(name, out var hold))
        {
            try
            {
                hold = SvgPictures.Draw(make(themed));
            }
            catch (Exception)
            {
                hold = null;
            }
            variants[name] = hold;
        }
        return hold?.Again();
    }

    void ForgetVariants()
    {
        foreach (var v in variants.Values) v?.Dispose();
        variants.Clear();
    }

    /// <summary>
    /// The illustration as it's being explored: the drawing (with its labels, without them, or with only their lines
    /// while testing yourself), the rest dimmed while a part is lit, the lit part drawn again on top with a soft ring of
    /// the accent round it and its label (its name shows even with the labels off), each label shown again while
    /// testing yourself, a quiet blank where each hidden one goes, and the keyboard's ring round the part it's on.
    /// </summary>
    void DrawExplored(DrawingContext context, PartsLook l, PartsExplorer x, float k, Size size, bool dark)
    {
        var holds = new List<PictureHold>();
        PictureHold? Keep(PictureHold? h)
        {
            if (h is not null) holds.Add(h);
            return h;
        }
        var basePicture = Keep(l.Recall ? Variant("leaders", Illustration.Leaders) : l.Labels ? held?.Again() : Variant("bare", Illustration.Bare));
        string? lit = l.Lit is { } id && l.Dim > 0.001 ? id : null;
        var part = lit is null ? null : Keep(Variant("part:" + lit, s => Illustration.Part(s, lit)));
        bool nameShows = lit is not null && (!l.Recall || l.Shown.Contains(lit));
        var label = nameShows ? Keep(Variant("callout:" + lit, s => Illustration.CalloutOf(s, lit!))) : null;
        var shownAgain = l.Recall ? l.Shown.Where(s => s != lit).Select(s => Keep(Variant("callout:" + s, v => Illustration.CalloutOf(v, s)))).OfType<PictureHold>().ToList() : [];
        var accent = Colour(Accent, Color.Parse("#0A84A0"));
        var blank = Colour(Blank, dark ? Color.Parse("#3A3A3C") : Color.Parse("#EDEDF0"));
        var faint = Colour(Faint, Color.Parse("#C7C7CC"));
        var pills = l.Recall ? l.Blanks.Select(b => x.Callouts.TryGetValue(b, out var c) ? c.Words : (Bounds?)null).OfType<Bounds>().ToList() : [];
        Bounds? ringBox = l.Ring is { } r && x.Map?.Of(r) is { } rb ? rb : null;
        double dim = l.Dim;
        context.Custom(new SkiaOp(new Rect(size), canvas =>
        {
            canvas.ClipRect(new SKRect(0, 0, (float)size.Width, (float)size.Height));
            canvas.Scale(k);
            if (basePicture is not null)
            {
                if (lit is not null)
                {
                    using var fade = new SKPaint { Color = new SKColor(0, 0, 0, (byte)Math.Round(255 * (1 - 0.68 * dim))) };
                    canvas.SaveLayer(fade);
                    canvas.DrawPicture(basePicture.Picture);
                    canvas.Restore();
                }
                else canvas.DrawPicture(basePicture.Picture);
            }
            foreach (var again in shownAgain) canvas.DrawPicture(again.Picture);
            foreach (var pill in pills)
            {
                using var fill = new SKPaint { Color = new SKColor(blank.R, blank.G, blank.B, blank.A), IsAntialias = true };
                using var edge = new SKPaint { Color = new SKColor(faint.R, faint.G, faint.B, faint.A), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1 };
                var rect = new SKRect((float)pill.X - 3, (float)pill.Y - 2, (float)pill.Right + 3, (float)pill.Bottom + 2);
                canvas.DrawRoundRect(rect, 5, 5, fill);
                canvas.DrawRoundRect(rect, 5, 5, edge);
            }
            if (part is not null)
            {
                float halo = (float)(2.5 / Math.Max(0.2, k));
                using var glow = new SKPaint
                {
                    ImageFilter = SKImageFilter.CreateColorFilter(SKColorFilter.CreateBlendMode(new SKColor(accent.R, accent.G, accent.B, (byte)Math.Round(150 * dim)), SKBlendMode.SrcIn),
                        SKImageFilter.CreateDilate(halo, halo)),
                };
                canvas.SaveLayer(glow);
                canvas.DrawPicture(part.Picture);
                canvas.Restore();
                using var fadeIn = new SKPaint { Color = new SKColor(0, 0, 0, (byte)Math.Round(255 * Math.Min(1, 0.32 + dim))) };
                canvas.SaveLayer(fadeIn);
                canvas.DrawPicture(part.Picture);
                canvas.Restore();
            }
            if (label is not null) canvas.DrawPicture(label.Picture);
            if (ringBox is { } box)
            {
                using var ringPaint = new SKPaint { Color = new SKColor(accent.R, accent.G, accent.B), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = (float)(2 / Math.Max(0.2, k)) };
                float pad = (float)(4 / Math.Max(0.2, k));
                canvas.DrawRoundRect(new SKRect((float)box.X - pad, (float)box.Y - pad, (float)box.Right + pad, (float)box.Bottom + pad), pad * 1.5f, pad * 1.5f, ringPaint);
            }
        }, new Holds(holds)));
    }

    /// <summary>Every picture one frame draws, let go together once the renderer is done with it.</summary>
    sealed class Holds(List<PictureHold> holds) : IDisposable
    {
        public void Dispose()
        {
            foreach (var h in holds) h.Dispose();
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        held?.Dispose();
        held = null;
        heldFor = null;
        themed = null;
        ForgetVariants();
    }

    static Color Colour(IBrush? brush, Color fallback) => brush is ISolidColorBrush s
        ? Color.FromArgb((byte)Math.Round(s.Color.A * s.Opacity), s.Color.R, s.Color.G, s.Color.B)
        : fallback;
}

/// <summary>A hold on a cached picture: the picture stays alive until every hold on it (the cache's own, each
/// drawing's, each frame's) has been let go.</summary>
sealed class PictureHold : IDisposable
{
    readonly PictureEntry entry;
    int released;

    public PictureHold(PictureEntry entry) => this.entry = entry;

    public SKPicture Picture => entry.Picture;

    /// <summary>Another hold on the same picture (for a frame the renderer draws later).</summary>
    public PictureHold? Again() => entry.TryHold();

    public void Dispose()
    {
        if (Interlocked.Exchange(ref released, 1) == 0) entry.Release();
    }
}

/// <summary>One drawing's picture, counted: it's disposed when the last hold goes.</summary>
sealed class PictureEntry(SKSvg svg, SKPicture picture)
{
    int holds = 1;

    public SKPicture Picture { get; } = picture;

    public PictureHold? TryHold()
    {
        while (true)
        {
            int now = Volatile.Read(ref holds);
            if (now == 0) return null;
            if (Interlocked.CompareExchange(ref holds, now + 1, now) == now) return new PictureHold(this);
        }
    }

    public void Release()
    {
        if (Interlocked.Decrement(ref holds) == 0) svg.Dispose();
    }

    /// <summary>The cache letting go of its own hold.</summary>
    public void Evict() => Release();
}

/// <summary>
/// Pictures of the 32 drawings shown last, by their cleaned SVG, so a note shown again (or a list of them scrolled)
/// doesn't parse anything twice. Only cleaned SVG ever gets here, and Svg.Skia is locked down besides: no scripts,
/// no SVG fonts, no text pulled from elsewhere, nothing to navigate to.
/// </summary>
static class SvgPictures
{
    const int Capacity = 32;
    static readonly Dictionary<string, LinkedListNode<(string Key, PictureEntry Entry)>> map = [];
    static readonly LinkedList<(string Key, PictureEntry Entry)> order = new();
    static readonly HashSet<string> unreadable = [];
    static readonly Lock gate = new();

    /// <summary>A hold on the picture of <paramref name="cleaned"/>, or null when Svg.Skia can't draw it.</summary>
    public static PictureHold? Hold(string cleaned)
    {
        lock (gate)
        {
            if (unreadable.Contains(cleaned)) return null;
            if (map.TryGetValue(cleaned, out var hit))
            {
                order.Remove(hit);
                order.AddFirst(hit);
                return hit.Value.Entry.TryHold();
            }
        }
        var entry = Load(cleaned);
        lock (gate)
        {
            if (entry is null)
            {
                if (unreadable.Count > 256) unreadable.Clear();
                unreadable.Add(cleaned);
                return null;
            }
            if (map.TryGetValue(cleaned, out var raced))
            {
                entry.Evict();
                return raced.Value.Entry.TryHold();
            }
            map[cleaned] = order.AddFirst((cleaned, entry));
            var hold = entry.TryHold();
            while (order.Count > Capacity)
            {
                var last = order.Last!.Value;
                map.Remove(last.Key);
                order.RemoveLast();
                last.Entry.Evict();
            }
            return hold;
        }
    }

    /// <summary>A picture of its own (not kept with the others): an illustration's versions, and the map of its parts.
    /// Null when Svg.Skia can't draw it.</summary>
    public static PictureHold? Draw(string cleaned) => Load(cleaned) is { } entry ? new PictureHold(entry) : null;

    static PictureEntry? Load(string cleaned)
    {
        var svg = new SKSvg();
        try
        {
            var settings = svg.Settings;
            settings.EnableJavaScript = false;
            settings.EnableExternalJavaScript = false;
            settings.JavaScriptRuntimeFactory = null;
            settings.NavigationHandler = null;
            settings.EnableSvgFonts = false;
            settings.EnableTextReferences = false;
            settings.EnableBrokenImagePlaceholders = false;
            settings.EnableFilterBackgroundInputs = false;
            settings.EnableTextSelectionRendering = false;
            if (svg.FromSvg(cleaned) is { } picture && !picture.CullRect.IsEmpty) return new PictureEntry(svg, picture);
        }
        catch (Exception e) // Svg.Skia couldn't draw it: the note shows the source instead
        {
            Program.Log($"[diagram] couldn't draw an SVG: {e.GetType().Name}: {e.Message}");
        }
        svg.Dispose();
        return null;
    }
}
