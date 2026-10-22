using Avalonia.Controls;

namespace StudyStash.App.Views;

public partial class MacAiAnswer : UserControl
{
    public MacAiAnswer()
    {
        InitializeComponent();
        AnswerHeight.Follow(this, Thread);
        AskThread.Follow(this, Thread);
        AskThread.ClosesChats(this, ChatsButton);
    }
}
