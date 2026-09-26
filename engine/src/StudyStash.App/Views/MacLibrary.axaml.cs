using Avalonia.Controls;
using Avalonia.Input;
using StudyStash.App.ViewModels;

namespace StudyStash.App.Views;

public partial class MacLibrary : UserControl
{
    public MacLibrary()
    {
        InitializeComponent();
        // Below 900 px the right column has no room: the list fills the window, and what's opened takes its place.
        SizeChanged += (_, e) =>
        {
            if (DataContext is LibraryModel m) m.Narrow = e.NewSize.Width < LibraryModel.NarrowBelow;
        };
        Fades.Over(TopFade, "Win", 0.3);
        Fades.Under(Fade, "Win", 0.7);
    }

    void OnNotesTab(object? sender, PointerPressedEventArgs e)
    {
        if ((DataContext as LibraryModel)?.Note is { } n) n.ShowTranscript = false;
    }

    void OnTranscriptTab(object? sender, PointerPressedEventArgs e)
    {
        if ((DataContext as LibraryModel)?.Note is { } n) n.ShowTranscript = true;
    }
}
