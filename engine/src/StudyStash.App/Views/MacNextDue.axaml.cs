using Avalonia.Controls;
using Avalonia.Interactivity;
using StudyStash.App.ViewModels;

namespace StudyStash.App.Views;

public partial class MacNextDue : UserControl
{
    public MacNextDue() => InitializeComponent();

    void OnClick(object? sender, RoutedEventArgs e) => (DataContext as NextDueModel)?.Open();
}
