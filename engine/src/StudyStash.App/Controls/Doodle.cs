using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Styling;
using SkiaSharp;
using StudyStash.App.Controls.Rich;
using Svg.Skia;

namespace StudyStash.App.Controls;

/// <summary>One of the egghead's doodles (Eli's own drawing, and the ones drawn from it: assets/egghead, written by
/// tools/egghead/make_art.py), <see cref="Size"/> tall, in the light or dark drawing as the window is:
/// <c>&lt;c:Doodle Art="sleepy" Size="110"/&gt;</c>. <see cref="Busy"/> brings him to life while something's
/// happening (notes being written, a recording): he bobs and rocks a little, still for anyone whose computer asks for
/// less motion.</summary>
public sealed class Doodle : Control
{
    public static readonly StyledProperty<string> ArtProperty = AvaloniaProperty.Register<Doodle, string>(nameof(Art), "head");
    public static readonly StyledProperty<double> SizeProperty = AvaloniaProperty.Register<Doodle, double>(nameof(Size), 96);
    public static readonly StyledProperty<bool> BusyProperty = AvaloniaProperty.Register<Doodle, bool>(nameof(Busy));

    static readonly Dictionary<string, SKPicture?> Cache = [];
    static readonly Stopwatch Clock = Stopwatch.StartNew();

    static Doodle() => AffectsRender<Doodle>(ArtProperty);

    public Doodle() => ActualThemeVariantChanged += (_, _) => InvalidateVisual();

    public string Art
    {
        get => GetValue(ArtProperty);
        set => SetValue(ArtProperty, value);
    }

    public double Size
    {
        get => GetValue(SizeProperty);
        set => SetValue(SizeProperty, value);
    }

    public bool Busy
    {
        get => GetValue(BusyProperty);
        set => SetValue(BusyProperty, value);
    }

    /// <summary>The doodle's picture (its light or dark drawing), or null when there's no such doodle.</summary>
    public static SKPicture? Find(string art, bool dark)
    {
        string file = dark ? $"{art}-dark" : art;
        lock (Cache)
        {
            if (Cache.TryGetValue(file, out var held)) return held;
            var uri = new Uri($"avares://StudyStash/Assets/Egghead/{file}.svg");
            if (!AssetLoader.Exists(uri)) return Cache[file] = null;
            using var stream = AssetLoader.Open(uri);
            // Kept for the app's life: a dozen small pictures, drawn again and again.
            return Cache[file] = new SKSvg().Load(stream);
        }
    }

    SKPicture? Picture => Find(Art, ActualThemeVariant == ThemeVariant.Dark);

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == SizeProperty || change.Property == ArtProperty) InvalidateMeasure();
        if (change.Property == BusyProperty || change.Property == IsVisibleProperty) Tick();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Tick();
    }

    /// <summary>While busy and showing, the next frame is asked for after each one drawn.</summary>
    void Tick()
    {
        if (Busy && IsEffectivelyVisible && !Platform.Motion.Reduced && TopLevel.GetTopLevel(this) is { } top)
            top.RequestAnimationFrame(_ =>
            {
                InvalidateVisual();
                Tick();
            });
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var own = Picture?.CullRect ?? SKRect.Empty;
        return own.Height > 0 ? new Size(Size * own.Width / own.Height, Size) : new Size(Size, Size);
    }

    public override void Render(DrawingContext context)
    {
        // (Worked out here: the drawing runs on the renderer's own thread, which can't read this control.)
        if (Picture is not { } art) return;
        var box = new Rect(Bounds.Size);
        float bob = 0, tilt = 0;
        if (Busy && !Platform.Motion.Reduced)
        {
            double t = Clock.Elapsed.TotalSeconds;
            bob = (float)(-Math.Sin(t * 2.6) * Size * 0.025);
            tilt = (float)(Math.Sin(t * 1.3) * 2.2);
        }
        context.Custom(new SkiaOp(box, canvas =>
        {
            canvas.Translate(0, bob);
            canvas.RotateDegrees(tilt, (float)box.Width / 2, (float)box.Height);
            Brands.Draw(canvas, art, box);
        }));
    }
}
