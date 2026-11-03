using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using StudyStash.App.Platform;

namespace StudyStash.App.Controls;

/// <summary>
/// A progress bar in the Syft look: sky on the mesh's track (the <c>Progress</c> and <c>ProgressTrack</c> tokens, given
/// by Styles.axaml so a view's own style can change them), its ends the cell's corners. When <see cref="Value"/> moves
/// on, the sky glides there in a quarter of a second (eased out), so a sync or a download creeps rather than jumps; with
/// less motion asked for (or in the tests' pictures) it just goes there. <c>&lt;c:Progress Value="{Binding Progress}" Height="4" /&gt;</c>, 0 to 1.
/// </summary>
public sealed class Progress : Control
{
    public static readonly StyledProperty<double> ValueProperty = AvaloniaProperty.Register<Progress, double>(nameof(Value));
    public static readonly StyledProperty<IBrush?> FillProperty = AvaloniaProperty.Register<Progress, IBrush?>(nameof(Fill));
    public static readonly StyledProperty<IBrush?> TrackProperty = AvaloniaProperty.Register<Progress, IBrush?>(nameof(Track));

    /// <summary>How far, 0 to 1.</summary>
    public double Value
    {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public IBrush? Fill
    {
        get => GetValue(FillProperty);
        set => SetValue(FillProperty, value);
    }

    public IBrush? Track
    {
        get => GetValue(TrackProperty);
        set => SetValue(TrackProperty, value);
    }

    double shown = double.NaN;
    IDisposable? gliding;

    static Progress() => AffectsRender<Progress>(FillProperty, TrackProperty);

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != ValueProperty) return;
        double to = Math.Clamp(Value, 0, 1);
        gliding?.Dispose();
        if (double.IsNaN(shown) || Motion.Reduced)
        {
            shown = to;
            InvalidateVisual();
            return;
        }
        double from = shown;
        gliding = Motion.Animate(this, TimeSpan.FromMilliseconds(250), t =>
        {
            shown = from + (to - from) * t;
            InvalidateVisual();
        });
    }

    public override void Render(DrawingContext context)
    {
        var size = Bounds.Size;
        if (size.Width <= 0 || size.Height <= 0) return;
        var corner = new CornerRadius(Math.Max(1, size.Height * Skin.CellRound));
        context.DrawRectangle(Track, null, new RoundedRect(new Rect(size), corner));
        double fill = double.IsNaN(shown) ? Math.Clamp(Value, 0, 1) : shown;
        if (fill > 0)
            context.DrawRectangle(Fill, null, new RoundedRect(new Rect(0, 0, Math.Max(size.Height, size.Width * fill), size.Height), corner));
    }
}
