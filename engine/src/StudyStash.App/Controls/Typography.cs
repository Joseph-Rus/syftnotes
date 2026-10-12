using Avalonia;
using Avalonia.Controls;

namespace StudyStash.App.Controls;

/// <summary>
/// The design's letter-spacing, where it has some (the page's date, a section's or a lecture's title, a day's number
/// are set a touch tighter): <c>c:Typography.Extra="-0.4"</c>, in pixels. Everything else is the font's own spacing.
/// </summary>
public static class Typography
{
    public static readonly AttachedProperty<double> ExtraProperty = AvaloniaProperty.RegisterAttached<TextBlock, double>("Extra", typeof(Typography));

    public static double GetExtra(TextBlock t) => t.GetValue(ExtraProperty);
    public static void SetExtra(TextBlock t, double v) => t.SetValue(ExtraProperty, v);

    static Typography() => ExtraProperty.Changed.AddClassHandler<TextBlock>((t, e) => t.LetterSpacing = e.NewValue is double extra ? extra : 0);
}
