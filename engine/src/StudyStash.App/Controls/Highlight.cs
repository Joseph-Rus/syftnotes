using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace StudyStash.App.Controls;

/// <summary>
/// A class's colour beside its name: a small mark, a touch crooked, as if dabbed on with the class's highlighter.
/// <see cref="Fill"/> is the class's brush (<see cref="Skin.ClassDots"/>), drawn in the look's swatch shade of that
/// highlighter, light or dark; any other brush (a calendar's own colour, Unsorted's grey) is drawn as it is.
/// </summary>
public sealed class Swatch : Control
{
    public static readonly StyledProperty<IBrush?> FillProperty = AvaloniaProperty.Register<Swatch, IBrush?>(nameof(Fill));
    public static readonly StyledProperty<bool> SmallProperty = AvaloniaProperty.Register<Swatch, bool>(nameof(Small));

    readonly ClassShade shade;

    static Swatch()
    {
        AffectsRender<Swatch>(FillProperty);
        AffectsMeasure<Swatch>(SmallProperty);
    }

    public Swatch() => shade = new ClassShade(this, "Swatch");

    public IBrush? Fill
    {
        get => GetValue(FillProperty);
        set => SetValue(FillProperty, value);
    }

    /// <summary>The smaller mark beside a class in the week (9 × 7); the sidebar's is 14 × 10.</summary>
    public bool Small
    {
        get => GetValue(SmallProperty);
        set => SetValue(SmallProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == FillProperty) shade.Follow(Fill);
    }

    protected override Size MeasureOverride(Size availableSize) => Small ? new Size(9, 7) : new Size(14, 10);

    public override void Render(DrawingContext context)
    {
        if ((shade.Brush ?? Fill) is not { } brush) return;
        var size = Small ? new Size(9, 7) : new Size(14, 10);
        var rect = new Rect((Bounds.Width - size.Width) / 2, (Bounds.Height - size.Height) / 2, size.Width, size.Height);
        double k = Small ? 0.7 : 1;
        var shape = new RoundedRect(rect, new Vector(2 * k, 2 * k), new Vector(4 * k, 4 * k), new Vector(3 * k, 3 * k), new Vector(5 * k, 5 * k));
        var centre = rect.Center;
        using (context.PushTransform(Matrix.CreateTranslation(-centre.X, -centre.Y) * Matrix.CreateRotation(-8 * Math.PI / 180) * Matrix.CreateTranslation(centre.X, centre.Y)))
            context.DrawRectangle(brush, null, shape);
    }
}

/// <summary>
/// Words marked with a class's highlighter: a swipe behind ink text, its corners uneven so it reads as drawn by hand,
/// and when the words wrap each line gets a swipe of its own. An assignment's name in the week, the class under a
/// lecture's title. <see cref="Fill"/> is the class's brush (<see cref="Skin.ClassDots"/>), drawn in the look's
/// highlighter shade, light or dark (deep in dark, so the text stays light). <see cref="Struck"/> is something handed
/// in: no swipe, the words struck through in the class's colour. <see cref="Outlined"/> rings the swipe in ink (the
/// pointer over a deadline).
/// </summary>
public sealed class Highlight : TextBlock
{
    public static readonly StyledProperty<IBrush?> FillProperty = AvaloniaProperty.Register<Highlight, IBrush?>(nameof(Fill));
    public static readonly StyledProperty<bool> StruckProperty = AvaloniaProperty.Register<Highlight, bool>(nameof(Struck));
    public static readonly StyledProperty<bool> OutlinedProperty = AvaloniaProperty.Register<Highlight, bool>(nameof(Outlined));
    public static readonly StyledProperty<bool> UnderProperty = AvaloniaProperty.Register<Highlight, bool>(nameof(Under));

    /// <summary>How far the swipe runs past the words on each side, and how much of that the words are moved in by:
    /// the rest hangs outside, so a marked line starts level with a plain one beside it. In ems.</summary>
    const double Reach = 0.28, Inset = 0.18;

    readonly ClassShade marker, strike;

    static Highlight() => AffectsRender<Highlight>(FillProperty, StruckProperty, OutlinedProperty, UnderProperty);

    public Highlight()
    {
        // The swipe hangs a little past the words' own box.
        ClipToBounds = false;
        marker = new ClassShade(this, "Hl", "Hover");
        strike = new ClassShade(this, "Swatch", "InkSubtle", Strike);
        Padding = new Thickness(Inset * FontSize, 0);
    }

    public IBrush? Fill
    {
        get => GetValue(FillProperty);
        set => SetValue(FillProperty, value);
    }

    public bool Struck
    {
        get => GetValue(StruckProperty);
        set => SetValue(StruckProperty, value);
    }

    public bool Outlined
    {
        get => GetValue(OutlinedProperty);
        set => SetValue(OutlinedProperty, value);
    }

    /// <summary>A folder's name as its page's title: the marker runs under the words (the lower half of the line),
    /// square-ended, rather than behind the whole of them.</summary>
    public bool Under
    {
        get => GetValue(UnderProperty);
        set => SetValue(UnderProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == FillProperty)
        {
            marker.Follow(Fill);
            strike.Follow(Fill);
        }
        else if (change.Property == FontSizeProperty || change.Property == UnderProperty) Padding = new Thickness(Under ? 0 : Inset * FontSize, 0);
        else if (change.Property == StruckProperty) Strike();
    }

    /// <summary>Handed in: struck through, two pixels of the class's colour.</summary>
    void Strike() => TextDecorations = !Struck ? null :
    [
        new TextDecoration { Location = TextDecorationLocation.Strikethrough, Stroke = strike.Brush, StrokeThickness = 2, StrokeThicknessUnit = TextDecorationUnit.Pixel },
    ];

    protected override void RenderTextLayout(DrawingContext context, Point origin)
    {
        if (!Struck && marker.Brush is { } brush)
        {
            double em = FontSize;
            // A line's swipe is as tall as the type itself (its ascent and descent), not the line: lines that sit
            // close together touch, as a marker's passes do.
            double body = Body() * em;
            IPen? ring = Outlined ? new Pen(Foreground, 1.5) : null;
            double y = origin.Y;
            foreach (var line in TextLayout.TextLines)
            {
                if (line.Width > 0 && Under)
                {
                    // From the middle of the type's own box to just above its foot, a little past the words on each side.
                    context.DrawRectangle(brush, null, new Rect(origin.X + line.Start - 0.04 * em, y + (line.Height - body) / 2 + body * 0.5, line.Width + 0.08 * em, body * 0.4));
                }
                else if (line.Width > 0)
                {
                    double top = y + (line.Height - body) / 2 - 0.04 * em;
                    var rect = new Rect(origin.X + line.Start - Reach * em, top, line.Width + 2 * Reach * em, body + 0.06 * em);
                    context.DrawRectangle(brush, ring, new RoundedRect(rect, new Vector(0.28 * em, 0.5 * em), new Vector(0.55 * em, 0.3 * em),
                        new Vector(0.35 * em, 0.55 * em), new Vector(0.6 * em, 0.35 * em)));
                }
                y += line.Height;
            }
        }
        base.RenderTextLayout(context, origin);
    }

    /// <summary>The type's own height in ems: 1.3 for the app's face, and near enough for whatever stands in.</summary>
    double Body()
    {
        var metrics = new Typeface(FontFamily, FontStyle, FontWeight).GlyphTypeface.Metrics;
        return metrics.DesignEmHeight > 0 ? (double)(metrics.Descent - metrics.Ascent) / metrics.DesignEmHeight : 1.3;
    }
}

/// <summary>
/// The shade of a class's colour a control draws: the look's own token for that highlighter
/// (<paramref name="prefix"/> + "Yellow"), which changes with light and dark. For a brush that isn't a class's,
/// <paramref name="other"/> names the token to use instead, or (none named) there's no shade and the control draws
/// the brush itself.
/// </summary>
sealed class ClassShade(Control owner, string prefix, string? other = null, Action? changed = null)
{
    IDisposable? watching;

    public IBrush? Brush { get; private set; }

    public void Follow(IBrush? fill)
    {
        watching?.Dispose();
        watching = null;
        Brush = null;
        string? key = Skin.HueOf(fill) is { } hue ? prefix + hue : fill is null ? null : other;
        if (key is not null)
            watching = owner.GetResourceObservable(key).Subscribe(new Watcher(v =>
            {
                Brush = v as IBrush;
                changed?.Invoke();
                owner.InvalidateVisual();
            }));
        changed?.Invoke();
        owner.InvalidateVisual();
    }

    sealed class Watcher(Action<object?> next) : IObserver<object?>
    {
        public void OnCompleted() { }
        public void OnError(Exception error) { }
        public void OnNext(object? value) => next(value);
    }
}
