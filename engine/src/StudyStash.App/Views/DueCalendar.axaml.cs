using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using StudyStash.App.Controls;
using StudyStash.App.Services;
using StudyStash.App.ViewModels;
using StudyStash.App.ViewModels.Kit;

namespace StudyStash.App.Views;

public partial class DueCalendar : UserControl
{
    OwnDueFormModel? form;

    public DueCalendar() => InitializeComponent();

    /// <summary>The form for the student's own items opens when its model says so (Add, or one of them clicked), in the window's
    /// dialog layer, and ends when the model does.</summary>
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (form is not null) form.PropertyChanged -= OnFormChanged;
        form = (DataContext as DueCalendarModel)?.Own;
        if (form is not null) form.PropertyChanged += OnFormChanged;
    }

    void OnFormChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(OwnDueFormModel.IsOpen) && form is { IsOpen: true } open) UiTasks.Run(() => ShowFormAsync(open), "show the Due form");
    }

    int shows;

    async Task ShowFormAsync(OwnDueFormModel open)
    {
        int mine = ++shows;
        if (TopLevel.GetTopLevel(this) is Window window && ConfirmLayer.For(window) is { } layer)
            await layer.ShowAsync(new OwnDueForm { DataContext = open });
        // A click outside ended it, or nowhere could show it: the model learns it is closed. (Not when the form has been opened
        // again meanwhile, a question over it answered no: a dialog layer lets go of the closed form a moment late.)
        if (mine == shows) open.Close();
    }

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
