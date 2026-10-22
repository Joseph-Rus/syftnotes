using Avalonia.Controls;

namespace StudyStash.App.Views;

public partial class WinAiAnswer : UserControl
{
    public WinAiAnswer()
    {
        InitializeComponent();
        AnswerHeight.Follow(this, Thread);
        AskThread.Follow(this, Thread);
        AskThread.ClosesChats(this, ChatsButton);
    }
}
