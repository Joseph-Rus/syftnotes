using Avalonia.Controls;
using StudyStash.App.ViewModels;

namespace StudyStash.App.Views;

public partial class MacRecorder : UserControl
{
    public MacRecorder()
    {
        InitializeComponent();
        AskChat.Embed();
        // The newest line in view as the live words come.
        NewestLine.Keep(this, Heard);
    }
}
