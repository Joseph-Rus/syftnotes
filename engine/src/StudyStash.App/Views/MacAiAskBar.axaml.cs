using Avalonia.Controls;
using StudyStash.App.ViewModels;

namespace StudyStash.App.Views;

public partial class MacAiAskBar : UserControl
{
    public MacAiAskBar()
    {
        InitializeComponent();
        AskThread.ClosesChats(this, ChatsButton);
        DataContextChanged += (_, _) =>
        {
            if (DataContext is not AiAskModel m) return;
            m.CloseMenu = () => EngineChip.Flyout?.Hide();
            // A new chat's first question goes straight in.
            m.Started = () => AskInput.Focus();
        };
    }
}
