using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using CSharpMath.SkiaSharp;
using SkiaSharp;
using StudyStash.Core.Rich;
using AvaloniaSize = Avalonia.Size;

namespace StudyStash.App.Controls.Rich;

/// <summary>
/// A formula typeset with CSharpMath: inline, it sits on the surrounding text's baseline
/// (<see cref="TextBlock.SetBaselineOffset"/>, read by an <c>InlineUIContainer</c>); as a display formula it draws
/// larger, its own line. <see cref="Latex"/> is the source as the notes write it — <see cref="MathText.Prepare"/> is
/// applied here, once per measure, so callers never have to remember to. CSharpMath is a prerelease library: any
/// exception, or an <see cref="MathPainter.ErrorMessage"/>, is caught and leaves <see cref="ErrorMessage"/> set
/// instead of drawing anything, so a caller can drop back to the plain source; it never throws.
/// </summary>
public sealed class MathView : Control
{
    public static readonly StyledProperty<string?> LatexProperty = AvaloniaProperty.Register<MathView, string?>(nameof(Latex));
    public static readonly StyledProperty<bool> DisplayProperty = AvaloniaProperty.Register<MathView, bool>(nameof(Display));
    public static readonly StyledProperty<double> SizeProperty = AvaloniaProperty.Register<MathView, double>(nameof(Size), 16);
    public static readonly StyledProperty<IBrush?> ForegroundProperty = AvaloniaProperty.Register<MathView, IBrush?>(nameof(Foreground));
    public static readonly StyledProperty<double> ScaleProperty = AvaloniaProperty.Register<MathView, double>(nameof(Scale), 1.0);

    const double Pad = 1.5;

    static MathView() => AffectsMeasure<MathView>(LatexProperty, DisplayProperty, SizeProperty, ScaleProperty);

    MathPainter? painter;
    double ascent, descent, width;

    public string? Latex { get => GetValue(LatexProperty); set => SetValue(LatexProperty, value); }

    /// <summary>Display style (bigger operators, limits above/below): a formula on its own line, not sat in a
    /// sentence.</summary>
    public bool Display { get => GetValue(DisplayProperty); set => SetValue(DisplayProperty, value); }

    /// <summary>The font size CSharpMath lays the formula out at, before <see cref="Scale"/>.</summary>
    public double Size { get => GetValue(SizeProperty); set => SetValue(SizeProperty, value); }

    public IBrush? Foreground { get => GetValue(ForegroundProperty); set => SetValue(ForegroundProperty, value); }

    /// <summary>How much smaller than its natural size a display formula draws, to fit its column (never below
    /// <see cref="MathDisplay.MinScale"/>); always 1 inline.</summary>
    public double Scale { get => GetValue(ScaleProperty); set => SetValue(ScaleProperty, value); }

    /// <summary>Set after a measure that couldn't typeset <see cref="Latex"/> (bad syntax, or CSharpMath itself
    /// threw): the caller shows the plain source instead. Null once a measure succeeds.</summary>
    public string? ErrorMessage { get; private set; }

    /// <summary>The formula's natural size at <see cref="Size"/>, before <see cref="Scale"/> — what
    /// <see cref="MathDisplay"/> fits to the column.</summary>
    internal AvaloniaSize Natural => new(width + 2 * Pad, ascent + descent);

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        // A theme switch only recolours the drawing already laid out; it never re-measures.
        if (change.Property == ForegroundProperty && painter is not null)
        {
            painter.TextColor = ToSkColor(Foreground);
            InvalidateVisual();
        }
    }

    protected override AvaloniaSize MeasureOverride(AvaloniaSize availableSize)
    {
        ErrorMessage = null;
        painter = null;
        try
        {
            var p = new MathPainter
            {
                LaTeX = MathText.Prepare(Latex ?? ""),
                FontSize = (float)Math.Max(1, Size),
                TextColor = ToSkColor(Foreground),
                LineStyle = Display ? CSharpMath.Atom.LineStyle.Display : CSharpMath.Atom.LineStyle.Text,
            };
            p.Measure(0);
            if (p.ErrorMessage is { Length: > 0 } msg)
            {
                ErrorMessage = msg;
                return default;
            }
            if (p.Display is not { } d)
            {
                ErrorMessage = "Study Stash couldn't typeset this formula.";
                return default;
            }
            ascent = d.Ascent;
            descent = d.Descent;
            width = d.Width;
            painter = p;
            TextBlock.SetBaselineOffset(this, ascent * Scale);
            return new AvaloniaSize((width + 2 * Pad) * Scale, (ascent + descent) * Scale);
        }
        catch (Exception e)
        {
            ErrorMessage = e.Message;
            return default;
        }
    }

    public override void Render(DrawingContext context)
    {
        if (painter is not { } p || ErrorMessage is not null) return;
        double a = ascent, s = Scale;
        var bounds = new Rect(Bounds.Size).Inflate(8);
        context.Custom(new SkiaOp(bounds, c =>
        {
            c.Scale((float)s);
            p.Draw(c, (float)Pad, (float)a);
        }));
    }

    static SKColor ToSkColor(IBrush? brush) => brush is ISolidColorBrush s
        ? new SKColor(s.Color.R, s.Color.G, s.Color.B, s.Color.A)
        : SKColors.Black;
}

/// <summary>
/// A display formula, centred in the notes column: scaled down when it's wider than the column (never below
/// <see cref="MinScale"/> — its type stays legible), then, past that floor, scrollable sideways instead of shrinking
/// further. Fits like <c>DiagramView</c> does: the inner <see cref="MathView"/> is measured at its natural size
/// first, then <see cref="MathView.Scale"/> is set from the room actually given.
/// </summary>
sealed class MathDisplay : Decorator
{
    public const double MinScale = 0.7;

    readonly MathView math;
    readonly ScrollViewer scroller;

    public MathDisplay(MathView mv)
    {
        math = mv;
        math.HorizontalAlignment = HorizontalAlignment.Center;
        scroller = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = math,
        };
        Child = scroller;
        HorizontalAlignment = HorizontalAlignment.Center;
    }

    protected override AvaloniaSize MeasureOverride(AvaloniaSize availableSize)
    {
        math.Scale = 1;
        math.Measure(AvaloniaSize.Infinity);
        var natural = math.Natural;
        double scale = natural.Width <= 0 || !double.IsFinite(availableSize.Width)
            ? 1
            : Math.Clamp(availableSize.Width / natural.Width, MinScale, 1);
        if (Math.Abs(scale - math.Scale) > 0.0001)
        {
            math.Scale = scale;
            math.InvalidateMeasure();
        }
        return base.MeasureOverride(availableSize);
    }
}
