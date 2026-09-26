using Avalonia.Controls;
using Avalonia.Interactivity;
using StudyStash.App.ViewModels;

namespace StudyStash.App.Views;

public partial class WinNextDue : UserControl
{
    public WinNextDue() => InitializeComponent();

    void OnClick(object? sender, RoutedEventArgs e) => (DataContext as NextDueModel)?.Open();
}
