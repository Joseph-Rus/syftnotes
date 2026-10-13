using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace StudyStash.App.Views;

public partial class DueCalendar : UserControl
{
    public DueCalendar() => InitializeComponent();

    /// <summary>A pick in one of the calendar's menus closes the menu it was in.</summary>
    void OnPicked(object? sender, RoutedEventArgs e) => Menus.Close(sender as Control);
}

/// <summary>The app's own menus are flyouts with rows in them: a pick closes the one it was made in.</summary>
public static class Menus
{
    /// <summary>Closes the menu <paramref name="row"/> is in, once its click is over. A row's Click comes before its
    /// command, and closing the menu there took the row out of the window: its command, bound through the window
    /// (a class in Due's calendar, a folder in Home's notes menu), came back empty and the pick did nothing.</summary>
    public static void Close(Control? row)
    {
        if (row?.FindAncestorOfType<FlyoutPresenter>()?.Parent is Popup popup)
            Avalonia.Threading.Dispatcher.UIThread.Post(() => popup.IsOpen = false);
    }
}
