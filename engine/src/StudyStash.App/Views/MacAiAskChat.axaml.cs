using Avalonia.Controls;
using StudyStash.App.ViewModels;

namespace StudyStash.App.Views;

public partial class MacAiAskChat : UserControl
{
    public MacAiAskChat()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (DataContext is AiAskModel m) m.CloseMenu = () => EngineChip.Flyout?.Hide();
        };
    }

    /// <summary>Inside another surface (the recorder's expanded card): no card of its own, just the thread and field.</summary>
    public void Embed()
    {
        Card.Background = null;
        Card.BoxShadow = default;
        Card.Filter = null;
        Card.Padding = new Avalonia.Thickness(0);
        Card.Width = double.NaN;
    }
}
