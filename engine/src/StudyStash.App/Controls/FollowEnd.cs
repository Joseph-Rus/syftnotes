using Avalonia;
using Avalonia.Controls;

namespace StudyStash.App.Controls;

/// <summary>
/// A scroll view that keeps its newest words in view while what's in it grows (an answer being written), unless the
/// student has scrolled up to read something earlier: then it stays where they put it until they scroll back down
/// to the end. Content that shrinks (a new answer starting) follows again.
/// </summary>
public static class FollowEnd
{
    public static readonly AttachedProperty<bool> IsOnProperty = AvaloniaProperty.RegisterAttached<ScrollViewer, bool>("IsOn", typeof(FollowEnd));

    /// <summary>The student has scrolled away from the end.</summary>
    static readonly AttachedProperty<bool> AwayProperty = AvaloniaProperty.RegisterAttached<ScrollViewer, bool>("Away", typeof(FollowEnd));

    /// <summary>How close to the end still counts as at the end (a pixel or two of rounding, a nudge).</summary>
    const double Slack = 4;

    static FollowEnd() => IsOnProperty.Changed.AddClassHandler<ScrollViewer>((view, e) =>
    {
        view.ScrollChanged -= Changed;
        if (e.NewValue is true) view.ScrollChanged += Changed;
    });

    public static bool GetIsOn(ScrollViewer view) => view.GetValue(IsOnProperty);
    public static void SetIsOn(ScrollViewer view, bool on) => view.SetValue(IsOnProperty, on);

    static void Changed(object? sender, ScrollChangedEventArgs e)
    {
        if (sender is not ScrollViewer view) return;
        double end = Math.Max(0, view.Extent.Height - view.Viewport.Height);
        if (e.ExtentDelta.Y < 0) view.SetValue(AwayProperty, false);
        if (e.ExtentDelta.Y != 0 || e.ViewportDelta.Y != 0)
        {
            if (!view.GetValue(AwayProperty) && view.Offset.Y < end) view.Offset = new Vector(view.Offset.X, end);
        }
        else if (e.OffsetDelta.Y != 0) view.SetValue(AwayProperty, view.Offset.Y < end - Slack);
    }
}
