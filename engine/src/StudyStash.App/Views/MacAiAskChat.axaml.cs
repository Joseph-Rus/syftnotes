using Avalonia.Controls;
using StudyStash.App.ViewModels;

namespace StudyStash.App.Views;

public partial class MacAiAskChat : UserControl
{
    public MacAiAskChat()
    {
        InitializeComponent();
        AskThread.Follow(this, Thread);
        DataContextChanged += (_, _) =>
        {
            if (DataContext is not AiAskModel m) return;
            m.CloseMenu = () => EngineChip.Flyout?.Hide();
            // A new chat's first question goes straight in.
            m.Started = () => ChatInput.Focus();
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
