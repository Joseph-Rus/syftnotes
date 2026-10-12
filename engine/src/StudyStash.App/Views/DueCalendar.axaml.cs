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
    public static void Close(Control? row)
    {
        if (row?.FindAncestorOfType<FlyoutPresenter>()?.Parent is Popup popup) popup.IsOpen = false;
    }
}
