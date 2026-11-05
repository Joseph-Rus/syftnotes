using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using StudyStash.App.ViewModels.Kit;

namespace StudyStash.App.Controls;

/// <summary>
/// The view's side of a page's <see cref="ScrollPlace"/>: it tells the place where the page is scrolled to, and when
/// asked puts the page back there. A page's rows and diagrams arrive after it opens, so it goes as far as the page
/// reaches and carries on as the page grows, until it's there. The student's own scroll (the wheel, a key, the
/// scrollbar) ends it: their place wins.
/// </summary>
public sealed class ScrollKeeper
{
    readonly ScrollViewer view;
    ScrollPlace? place;
    /// <summary>Where the page is being put back to, until it's there.</summary>
    double? back;

    public ScrollKeeper(ScrollViewer view)
    {
        this.view = view;
        view.ScrollChanged += (_, _) =>
        {
            if (back is not null) GoBack();
            else if (place is not null) place.Y = view.Offset.Y;
        };
        void Student(object? sender, RoutedEventArgs e) => back = null;
        view.AddHandler(InputElement.PointerWheelChangedEvent, Student, RoutingStrategies.Tunnel, handledEventsToo: true);
        view.AddHandler(InputElement.PointerPressedEvent, Student, RoutingStrategies.Tunnel, handledEventsToo: true);
        view.AddHandler(InputElement.KeyDownEvent, Student, RoutingStrategies.Tunnel, handledEventsToo: true);
    }

    /// <summary>Keeps the place of the page the view now shows (the view was given another page's model).</summary>
    public void Follow(ScrollPlace? next)
    {
        if (place is not null) place.Asked -= PutBack;
        place = next;
        back = null;
        if (place is null) return;
        place.Asked += PutBack;
        place.Y = view.Offset.Y;
    }

    void PutBack(double y)
    {
        back = y;
        GoBack();
    }

    void GoBack()
    {
        if (back is not { } y) return;
        double reach = Math.Max(0, view.Extent.Height - view.Viewport.Height);
        view.Offset = view.Offset.WithY(Math.Min(y, reach));
        if (reach >= y - 0.5) back = null;
    }
}
