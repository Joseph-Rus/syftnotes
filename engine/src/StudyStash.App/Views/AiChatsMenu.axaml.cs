using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace StudyStash.App.Views;

public partial class AiChatsMenu : UserControl
{
    public AiChatsMenu() => InitializeComponent();

    /// <summary>Rename puts the caret in the name, all of it selected, ready to type over.</summary>
    void RenameClicked(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.Parent is Grid row && row.Children.OfType<TextBox>().FirstOrDefault() is { } name)
            Dispatcher.UIThread.Post(() =>
            {
                name.Focus();
                name.SelectAll();
            }, DispatcherPriority.Loaded);
    }
}
