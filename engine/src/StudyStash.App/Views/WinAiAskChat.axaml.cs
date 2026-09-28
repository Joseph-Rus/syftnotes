using Avalonia.Controls;
using StudyStash.App.ViewModels;

namespace StudyStash.App.Views;

public partial class WinAiAskChat : UserControl
{
    public WinAiAskChat()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (DataContext is AiAskModel m) m.CloseMenu = () => EngineChip.Flyout?.Hide();
        };
    }

    /// <summary>Inside another surface (the recorder's expanded card): no card of its own, just the thread and field.</summary>
    public void Embed() => Card.Classes.Add("embedded");
}
