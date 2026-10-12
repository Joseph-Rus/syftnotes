using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Styling;
using SkiaSharp;
using StudyStash.App.Controls.Rich;
using Svg.Skia;

namespace StudyStash.App.Controls;

/// <summary>One of the app's icons (<see cref="IconPaths"/>: Lucide's outlines), drawn as a 1.5-unit round stroke in
/// the text color at any size: <c>&lt;c:Icon Glyph="search" Size="18"/&gt;</c>, and <c>Filled="True"</c> for the
/// solid ones (pause, stop, play). A glyph named <c>brand:claude</c> is that product's own mark
/// (<see cref="Brands"/>), in its own colours.</summary>
public sealed class Icon : Control
{
    public static readonly StyledProperty<string> GlyphProperty = AvaloniaProperty.Register<Icon, string>(nameof(Glyph), "");
    public static readonly StyledProperty<bool> FilledProperty = AvaloniaProperty.Register<Icon, bool>(nameof(Filled));
    public static readonly StyledProperty<double> SizeProperty = AvaloniaProperty.Register<Icon, double>(nameof(Size), 18);
    public static readonly StyledProperty<IBrush?> ForegroundProperty = TextBlock.ForegroundProperty.AddOwner<Icon>();

    /// <summary>The stroke's width in the icons' 24-unit box: thinner than Lucide's own 2, as the design draws them.</summary>
    public const double Stroke = 1.5;

    static readonly Dictionary<string, Geometry> Cache = [];

    static Icon() => AffectsRender<Icon>(GlyphProperty, FilledProperty, ForegroundProperty);

    public Icon() => ActualThemeVariantChanged += (_, _) =>
    {
        // A brand's mark has a light and a dark drawing.
        if (Brands.Is(Glyph)) InvalidateVisual();
    };

    public string Glyph
    {
        get => GetValue(GlyphProperty);
        set => SetValue(GlyphProperty, value);
    }

    public bool Filled
    {
        get => GetValue(FilledProperty);
        set => SetValue(FilledProperty, value);
    }

    public double Size
    {
        get => GetValue(SizeProperty);
        set => SetValue(SizeProperty, value);
    }

    public IBrush? Foreground
    {
        get => GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == SizeProperty) InvalidateMeasure();
    }

    public static Geometry? Find(string name)
    {
        lock (Cache)
        {
            if (Cache.TryGetValue(name, out var g)) return g;
            if (!IconPaths.All.TryGetValue(name, out string? path)) return null;
            return Cache[name] = Geometry.Parse(path);
        }
    }

    protected override Size MeasureOverride(Size availableSize) => new(Size, Size);

    public override void Render(DrawingContext context)
    {
        double left = (Bounds.Width - Size) / 2, top = (Bounds.Height - Size) / 2;
        if (Brands.Is(Glyph))
        {
            if (Brands.Find(Glyph, ActualThemeVariant == ThemeVariant.Dark) is { } mark)
                context.Custom(new SkiaOp(new Rect(left, top, Size, Size), canvas => Brands.Draw(canvas, mark, new Rect(left, top, Size, Size))));
            return;
        }
        if (Find(Glyph) is not { } g) return;
        var ink = Foreground ?? Brushes.Black;
        double scale = Size / 24;
        var pen = new Pen(ink, Stroke, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
        using (context.PushTransform(Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(left, top)))
            context.DrawGeometry(Filled && IconPaths.Solid.Contains(Glyph) ? ink : null, pen, g);
    }
}

/// <summary>
/// The marks of the AI products the app names, so a student sees the one they know: Claude, ChatGPT, Codex, Gemini
/// and Ollama (Assets/Brands, from svgl.app; each is its owner's trademark, shown only to name its product). A view
/// asks for one as an icon: <c>Glyph="brand:claude"</c>. Those drawn in one ink have a light and a dark drawing.
/// </summary>
public static class Brands
{
    public const string Prefix = "brand:";

    /// <summary>The marks there are, and whether each has a drawing of its own for dark.</summary>
    static readonly Dictionary<string, bool> Marks = new()
    {
        ["claude"] = false, ["chatgpt"] = true, ["codex"] = true, ["gemini"] = false, ["ollama"] = true,
    };

    static readonly Dictionary<string, SKPicture?> Cache = [];

    public static bool Is(string? glyph) => glyph is not null && glyph.StartsWith(Prefix, StringComparison.Ordinal);

    /// <summary>The glyph that draws a product's mark ("claude" gives "brand:claude"), or null when there's none for it.</summary>
    public static string? Glyph(string name) => Marks.ContainsKey(name) ? Prefix + name : null;

    public static SKPicture? Find(string glyph, bool dark)
    {
        string name = glyph[Prefix.Length..];
        if (!Marks.TryGetValue(name, out bool themed)) return null;
        string file = themed ? $"{name}-{(dark ? "dark" : "light")}" : name;
        lock (Cache)
        {
            if (Cache.TryGetValue(file, out var held)) return held;
            using var stream = AssetLoader.Open(new Uri($"avares://StudyStash/Assets/Brands/{file}.svg"));
            // Kept for the app's life: a handful of small pictures, drawn again and again.
            return Cache[file] = new SKSvg().Load(stream);
        }
    }

    /// <summary>The mark, whole and centred in <paramref name="box"/>, whatever its own proportions.</summary>
    public static void Draw(SKCanvas canvas, SKPicture mark, Rect box)
    {
        var own = mark.CullRect;
        if (own.Width <= 0 || own.Height <= 0) return;
        float scale = (float)Math.Min(box.Width / own.Width, box.Height / own.Height);
        canvas.Translate((float)(box.X + (box.Width - own.Width * scale) / 2), (float)(box.Y + (box.Height - own.Height * scale) / 2));
        canvas.Scale(scale);
        canvas.Translate(-own.Left, -own.Top);
        canvas.DrawPicture(mark);
    }
}
