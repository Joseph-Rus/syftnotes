using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace StudyStash.App.Controls;

/// <summary>
/// Tooltips that wait to be wanted, the way the systems' own do: one shows only after the pointer has rested on
/// something for about a second, never while a menu or other popup is open, and not after a scroll until the pointer
/// moves again (the content sliding under a still pointer isn't asking for anything). A name that may be cut short
/// sets <see cref="WhenCutProperty"/> so its tooltip shows only when the name really is cut.
/// </summary>
public static class Tips
{
    /// <summary>How long the pointer rests on something before its tooltip shows: native apps wait about this long.</summary>
    public const int ShowDelay = 1000;

    /// <summary>On a TextBlock whose tooltip is its own text in full: show it only while the text is cut short.</summary>
    public static readonly AttachedProperty<bool> WhenCutProperty =
        AvaloniaProperty.RegisterAttached<TextBlock, bool>("WhenCut", typeof(Tips));

    static bool used;
    static bool scrolled;
    static Point scrolledAt;
    static readonly HashSet<Popup> open = [];

    public static bool GetWhenCut(TextBlock text) => text.GetValue(WhenCutProperty);

    public static void SetWhenCut(TextBlock text, bool value) => text.SetValue(WhenCutProperty, value);

    /// <summary>Turns it on for the whole app (once, at start, before any window).</summary>
    public static void Use()
    {
        if (used) return;
        used = true;
        // A style's strength, so a view that asks for its own delay still gets it.
        ToolTip.TipProperty.Changed.AddClassHandler<Control>((control, _) =>
        {
            if (!control.IsSet(ToolTip.ShowDelayProperty)) control.SetValue(ToolTip.ShowDelayProperty, ShowDelay, BindingPriority.Style);
        });
        ToolTip.ToolTipOpeningEvent.AddClassHandler<Control>((control, e) =>
        {
            if (Hold(control)) e.Cancel = true;
        });
        Popup.IsOpenProperty.Changed.AddClassHandler<Popup>((popup, _) =>
        {
            if (popup.IsOpen && popup.IsLightDismissEnabled && popup.Child is not ToolTip) open.Add(popup);
            else open.Remove(popup);
        });
        InputElement.PointerWheelChangedEvent.AddClassHandler<TopLevel>((top, e) =>
        {
            scrolled = true;
            scrolledAt = e.GetPosition(top);
        }, RoutingStrategies.Tunnel, handledEventsToo: true);
        InputElement.PointerMovedEvent.AddClassHandler<TopLevel>((top, e) =>
        {
            // Scrolling moves what's under the pointer, and the app may say the pointer moved: only a real move counts.
            if (scrolled && Distance(e.GetPosition(top), scrolledAt) > 3) scrolled = false;
        }, RoutingStrategies.Tunnel, handledEventsToo: true);
    }

    /// <summary>Whether <paramref name="control"/>'s tooltip should stay hidden right now.</summary>
    internal static bool Hold(Control control) =>
        scrolled || open.Any(p => p.IsOpen) || control is TextBlock text && GetWhenCut(text) && !IsCut(text);

    /// <summary>Whether <paramref name="text"/> is showing less than all of its words (an ellipsis, or lines cut off).</summary>
    internal static bool IsCut(TextBlock text) => text.TextLayout.TextLines.Any(l => l.HasCollapsed);

    /// <summary>For tests: forget any scroll or open popup another test left behind.</summary>
    internal static void Reset()
    {
        scrolled = false;
        open.Clear();
    }

    static double Distance(Point a, Point b) => Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y);
}
