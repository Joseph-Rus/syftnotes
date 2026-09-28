using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
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
/// never below <see cref="MinScale"/> of that (past it, it scrolls sideways). In a note, a click opens it larger.
/// </summary>
public sealed class SvgView : Decorator
{
    public static readonly StyledProperty<string?> SourceProperty = AvaloniaProperty.Register<SvgView, string?>(nameof(Source));
    public static readonly StyledProperty<double> MaxScaleProperty = AvaloniaProperty.Register<SvgView, double>(nameof(MaxScale), 1);

    /// <summary>The smallest a drawing is shown: 13-unit words stay at least 8 px.</summary>
    public const double MinScale = 0.6;

    readonly SvgCanvas canvas = new();

    static SvgView() => AffectsMeasure<SvgView>(SourceProperty, MaxScaleProperty);

    public SvgView() : this(opensLarger: true) { }

    /// <summary>A drawing that opens larger on a click (in a note), or one that doesn't (the larger window's own).</summary>
    public SvgView(bool opensLarger)
    {
        var scroller = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = canvas,
        };
        HorizontalAlignment = HorizontalAlignment.Center;
        if (!opensLarger)
        {
            Child = scroller;
            return;
        }
        var badge = OpenLarger.Badge();
        Child = new Panel { Children = { scroller, badge } };
        OpenLarger.Wire(this, badge, () => Source is { } svg && Drawing?.Svg is not null ? new OpenDiagramEventArgs(this) { Title = Title, Svg = svg } : null);
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
            AutomationProperties.SetName(this, Drawing?.Svg is null ? null : "Diagram: " + string.Join(", ", words));
        }
        else if (change.Property == MaxScaleProperty) canvas.MaxScale = MaxScale;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        canvas.Room = availableSize;
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

    string? source;
    double maxScale = 1;
    Size room = new(double.PositiveInfinity, double.PositiveInfinity);
    PictureHold? held;
    string? heldFor;

    static SvgCanvas() => AffectsRender<SvgCanvas>(InkProperty, SecondaryProperty, FaintProperty);

    public SvgCanvas()
    {
        ActualThemeVariantChanged += (_, _) => InvalidateVisual();
        Bind(InkProperty, this.GetResourceObservable("Fg"));
        Bind(SecondaryProperty, this.GetResourceObservable("Fg2"));
        Bind(FaintProperty, this.GetResourceObservable("Fg3"));
    }

    public IBrush? Ink { get => GetValue(InkProperty); set => SetValue(InkProperty, value); }
    public IBrush? Secondary { get => GetValue(SecondaryProperty); set => SetValue(SecondaryProperty, value); }
    public IBrush? Faint { get => GetValue(FaintProperty); set => SetValue(FaintProperty, value); }

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
        Scale = Math.Max(SvgView.MinScale, scale);
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
            var cleaned = SafeSvg.Clean(source, new SafeSvgOptions { FontFamily = SvgView.Font, Palette = palette, Dark = dark }).Svg;
            held = cleaned is null ? null : SvgPictures.Hold(cleaned);
            heldFor = key;
        }
        if (held?.Again() is not { } forOp) return;
        float k = (float)(Bounds.Width / drawing.Width);
        var size = Bounds.Size;
        context.Custom(new SkiaOp(new Rect(size), canvas =>
        {
            canvas.ClipRect(new SKRect(0, 0, (float)size.Width, (float)size.Height));
            canvas.Scale(k);
            canvas.DrawPicture(forOp.Picture);
        }, forOp));
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        held?.Dispose();
        held = null;
        heldFor = null;
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
