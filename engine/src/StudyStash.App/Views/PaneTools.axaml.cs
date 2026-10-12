using Avalonia.Controls;
using Avalonia.Interactivity;
using StudyStash.App.ViewModels;

namespace StudyStash.App.Views;

public partial class PaneTools : UserControl
{
    public PaneTools() => InitializeComponent();

    /// <summary>Move to folder: the same folder picker as the breadcrumb's, under the button.</summary>
    void OnMove(object? sender, RoutedEventArgs e)
    {
        if ((DataContext as LibraryModel)?.PickerForNote?.Invoke() is { } picker) FolderPickerFlyout.Open(Move, picker);
    }
}
