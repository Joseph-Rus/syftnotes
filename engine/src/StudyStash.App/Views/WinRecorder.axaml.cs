using Avalonia.Controls;
using StudyStash.App.ViewModels;

namespace StudyStash.App.Views;

public partial class WinRecorder : UserControl
{
    public WinRecorder()
    {
        InitializeComponent();
        AskChat.Embed();
        // The pointer over the small pill puts Stop where the level meter was.
        Pill.PointerEntered += (_, _) => Hover(true);
        Pill.PointerExited += (_, _) => Hover(false);
    }

    void Hover(bool on)
    {
        if (DataContext is RecorderModel m) m.Hovered = on;
    }
}
