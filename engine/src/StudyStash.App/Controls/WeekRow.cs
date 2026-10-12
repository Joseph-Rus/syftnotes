using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;

namespace StudyStash.App.Controls;

/// <summary>Something laid out in a <see cref="WeekRow"/> that says its own share of the row.</summary>
public interface IWeighted
{
    double Weight { get; }
}

/// <summary>
/// The week's columns side by side, each as wide as its <see cref="WeightProperty"/> says of the row (a weekday 1,
/// Saturday and Sunday 0.8, the Late column 1.15) and all as tall as the tallest, so the hairlines between days run
/// the grid's whole height. Each column starts on a whole pixel, to keep those hairlines sharp.
/// </summary>
public sealed class WeekRow : Panel
{
    public static readonly AttachedProperty<double> WeightProperty = AvaloniaProperty.RegisterAttached<WeekRow, Control, double>("Weight", 1);

    /// <summary>A column's share: what its own item says (a list's row holds the item as its content), or else what
    /// the control was given.</summary>
    public static double GetWeight(Control control) =>
        control is ContentPresenter { Content: IWeighted item } ? item.Weight : control.GetValue(WeightProperty);

    public static void SetWeight(Control control, double value) => control.SetValue(WeightProperty, value);

    static WeekRow() => AffectsParentMeasure<WeekRow>(WeightProperty);

    protected override Size MeasureOverride(Size availableSize)
    {
        double total = Children.Sum(GetWeight), height = 0;
        if (total <= 0) return default;
        bool open = double.IsInfinity(availableSize.Width);
        foreach (var child in Children)
        {
            child.Measure(new Size(open ? double.PositiveInfinity : availableSize.Width * GetWeight(child) / total, double.PositiveInfinity));
            height = Math.Max(height, child.DesiredSize.Height);
        }
        // Left to itself (no width given) the row is as wide as its widest column asks, at that column's share.
        double width = open ? Children.Max(c => c.DesiredSize.Width * total / Math.Max(GetWeight(c), 0.01)) : availableSize.Width;
        return new Size(width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        double total = Children.Sum(GetWeight), at = 0;
        if (total <= 0) return finalSize;
        foreach (var child in Children)
        {
            double left = Math.Round(finalSize.Width * at / total);
            at += GetWeight(child);
            double right = Math.Round(finalSize.Width * at / total);
            child.Arrange(new Rect(left, 0, right - left, finalSize.Height));
        }
        return finalSize;
    }
}
