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
        // The pointer on the small pill: Stop takes the S's place, and the card shows beside it.
        Pill.PointerEntered += (_, _) => { if (DataContext is RecorderModel m) m.Hovered = true; };
        Pill.PointerExited += (_, _) => { if (DataContext is RecorderModel m) m.Hovered = false; };
        Pill.PropertyChanged += (_, e) =>
        {
            if (e.Property == IsVisibleProperty && !Pill.IsVisible && DataContext is RecorderModel m) m.Hovered = false;
        };
    }
}
