using Avalonia.Controls;
using Avalonia.Input;
using StudyStash.App.ViewModels;

namespace StudyStash.App.Views;

public partial class StudyPage : UserControl
{
    public StudyPage()
    {
        InitializeComponent();
        // Reviewing by keys: Space shows the answer, 1 to 4 say how it went.
        AddHandler(KeyDownEvent, OnKey, Avalonia.Interactivity.RoutingStrategies.Tunnel);
    }

    void OnKey(object? sender, KeyEventArgs e)
    {
        if (DataContext is not StudyModel { Session: { HasCard: true } session } || e.Source is TextBox) return;
        if (e.Key == Key.Space && !session.ShowingBack)
        {
            session.ShowBackCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key is >= Key.D1 and <= Key.D4 && session.Key(e.Key - Key.D0)) e.Handled = true;
        else if (e.Key is >= Key.NumPad1 and <= Key.NumPad4 && session.Key(e.Key - Key.NumPad0)) e.Handled = true;
    }
}
