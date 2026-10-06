using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using StudyStash.App.Controls;
using StudyStash.App.ViewModels;

namespace StudyStash.App.Views;

public partial class MacLibrary : UserControl
{
    public MacLibrary()
    {
        InitializeComponent();
        // Files dropped on the lecture are attached to it; on the list of lectures, to the class.
        AttachFiles.AcceptDrops(NoteScroll, () => (DataContext as LibraryModel)?.LectureFiles);
        // The notes' diagrams ask about their boxes in this lecture's Ask bar, and find them in its transcript.
        LectureDiagramsView.Wire(this, NoteScroll, TranscriptLines);
        AttachFiles.AcceptDrops(LectureList, () => (DataContext as LibraryModel)?.ClassFiles);
        // Below 900 px the right column has no room: the list fills the window, and what's opened takes its place.
        SizeChanged += (_, e) =>
        {
            if (DataContext is LibraryModel m) m.Narrow = e.NewSize.Width < LibraryModel.NarrowBelow;
        };
        Fades.Over(TopFade, "Win", 0.3);
        Fades.Under(Fade, "Win", 0.7);
        AttachedToVisualTree += (_, _) =>
        {
            window = TopLevel.GetTopLevel(this) as Window;
            if (window is not null) window.PropertyChanged += OnWindowChanged;
            PlaceNav();
        };
        DetachedFromVisualTree += (_, _) =>
        {
            if (window is not null) window.PropertyChanged -= OnWindowChanged;
            window = null;
        };
    }

    /// <summary>How far in from the window's edge the title bar's buttons sit in full screen: as search and Settings do
    /// on the other side.</summary>
    public const double FullScreenInset = 12;

    Window? window;

    void OnWindowChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == Window.WindowStateProperty) PlaceNav();
    }

    /// <summary>The sidebar's button and Back/Forward start right after the lights; full screen shows no lights at
    /// rest, so there they move into the corner the lights leave (out of the room the header keeps for them).</summary>
    void PlaceNav() =>
        Nav.Margin = new Thickness(window?.WindowState == WindowState.FullScreen ? FullScreenInset - WindowHeader.LightsRoom : 0, 0, 0, 0);

    /// <summary>A lecture row's "Delete lecture…": asks first.</summary>
    void OnDeleteRow(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { DataContext: LectureCard card } && DataContext is LibraryModel m) m.DeleteCommand.Execute(card);
    }

    /// <summary>A click beside "Delete this lecture?" is Cancel.</summary>
    void OnDeleteAskPressed(object? sender, PointerPressedEventArgs e)
    {
        if (ReferenceEquals(e.Source, sender) && DataContext is LibraryModel m) m.CancelDeleteCommand.Execute(null);
    }
}
