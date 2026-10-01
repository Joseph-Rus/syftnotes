using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.VisualTree;
using StudyStash.Core.Rich;

namespace StudyStash.App.Controls.Rich;

/// <summary>A diagram asking to be shown larger: its title, and the chart and scene (a flowchart) or the SVG as
/// written (a drawing). Unless something on the way up handles it, a window opens with it.</summary>
public sealed class OpenDiagramEventArgs(object source) : RoutedEventArgs(OpenLarger.Event, source)
{
    public required string Title { get; init; }
    public Flowchart? Chart { get; init; }
    public DiagramScene? Scene { get; init; }
    public string? Svg { get; init; }
    /// <summary>The chart's source as the note wrote it (it may say the moment of the lecture it comes from).</summary>
    public string? Written { get; init; }
    /// <summary>The groups the note showed folded, so the window opens the same way.</summary>
    public IReadOnlyList<string>? Folded { get; init; }
}

/// <summary>
/// What makes a diagram in a note open larger: a click (or Enter or Space when it has focus) asks for it, the pointer
/// becomes a hand, and a small open-in-full badge shows in the corner while the pointer is over it. No tooltip: one
/// over a whole diagram would pop up every time the pointer crossed it while reading; screen readers hear it instead.
/// </summary>
public static class OpenLarger
{
    public static readonly RoutedEvent<OpenDiagramEventArgs> Event =
        RoutedEvent.Register<OpenDiagramEventArgs>("OpenDiagram", RoutingStrategies.Bubble, typeof(OpenLarger));

    public const string Help = "Open larger";

    /// <summary>The badge: the open-in-full icon on a small raised square, not hit-testable (the click is the
    /// diagram's).</summary>
    public static Control Badge()
    {
        var icon = new Icon { Glyph = "open_in_full", Size = 14 };
        icon.Bind(Icon.ForegroundProperty, icon.GetResourceObservable("Fg2"));
        var badge = new Border
        {
            Width = 26, Height = 26, CornerRadius = new CornerRadius(Skin.Current == SkinKind.Mac ? 7 : 4), Child = icon,
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 4, 4, 0),
            BorderThickness = new Thickness(1), IsHitTestVisible = false, IsVisible = false,
        };
        badge.Bind(Border.BackgroundProperty, badge.GetResourceObservable("PopupBg"));
        badge.Bind(Border.BorderBrushProperty, badge.GetResourceObservable("PopupStroke"));
        return badge;
    }

    /// <summary>Makes <paramref name="view"/> open larger (asking <paramref name="request"/> what to show), with
    /// <paramref name="badge"/> shown while the pointer is over it or it has the keyboard's focus.</summary>
    public static void Wire(Control view, Control badge, Func<OpenDiagramEventArgs?> request)
    {
        view.Focusable = true;
        view.Cursor = new Cursor(StandardCursorType.Hand);
        AutomationProperties.SetHelpText(view, Help);
        void Show() => badge.IsVisible = view.IsPointerOver || view.IsKeyboardFocusWithin;
        view.PointerEntered += (_, _) => Show();
        view.PointerExited += (_, _) => Show();
        view.GotFocus += (_, _) => Show();
        view.LostFocus += (_, _) => Show();
        view.Tapped += (_, e) =>
        {
            // The scroll bar of a wide diagram scrolls it; it doesn't open it.
            if (e.Source is Visual v && v.FindAncestorOfType<ScrollBar>(includeSelf: true) is not null) return;
            Raise(view, request);
        };
        view.KeyDown += (_, e) =>
        {
            if (e.Key is not (Key.Enter or Key.Space) || e.KeyModifiers != KeyModifiers.None) return;
            e.Handled = true;
            Raise(view, request);
        };
    }

    /// <summary>Asks for <paramref name="view"/>'s diagram larger; unless something on the way up shows it, a window
    /// opens with it.</summary>
    public static void Raise(Control view, Func<OpenDiagramEventArgs?> request)
    {
        if (request() is not { } e) return;
        view.RaiseEvent(e);
        if (!e.Handled) Windows.DiagramWindow.Open(e, TopLevel.GetTopLevel(view));
    }
}
