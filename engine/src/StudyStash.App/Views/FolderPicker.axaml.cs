using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using StudyStash.App.ViewModels;

namespace StudyStash.App.Views;

public partial class FolderPicker : UserControl
{
    public FolderPicker()
    {
        InitializeComponent();
        // The keys are the list's while the caret is in the search field: arrows move, Return picks, Esc closes.
        Search.AddHandler(KeyDownEvent, OnKey, RoutingStrategies.Tunnel);
    }

    void OnKey(object? sender, KeyEventArgs e)
    {
        if (DataContext is not FolderPickerModel m) return;
        switch (e.Key)
        {
            case Key.Down: m.DownCommand.Execute(null); break;
            case Key.Up: m.UpCommand.Execute(null); break;
            case Key.Enter: m.EnterCommand.Execute(null); break;
            case Key.Escape: m.EscapeCommand.Execute(null); break;
            default: return;
        }
        e.Handled = true;
    }

    /// <summary>The caret goes in the search field as the menu opens.</summary>
    public void FocusSearch() => Dispatcher.UIThread.Post(() => Search.Focus(), DispatcherPriority.Input);
}

/// <summary>Opens the folder picker as a menu under a control: a Home row's folder label, a note page's breadcrumb
/// chevron, the toolbar's Move. It opens upward when there's no room under.</summary>
public static class FolderPickerFlyout
{
    public static void Open(Control anchor, FolderPickerModel model, Action? closed = null)
    {
        var view = new FolderPicker { DataContext = model };
        var flyout = new Flyout { Content = view, Placement = PlacementMode.BottomEdgeAlignedLeft, VerticalOffset = 6, ShowMode = FlyoutShowMode.Standard };
        flyout.FlyoutPresenterClasses.Add("bare");
        model.OnClose = flyout.Hide;
        flyout.Opened += (_, _) => view.FocusSearch();
        flyout.Closed += (_, _) => closed?.Invoke();
        flyout.ShowAt(anchor);
    }
}
