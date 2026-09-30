using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using StudyStash.App.ViewModels;

namespace StudyStash.App.Views;

public partial class WinLibrary : UserControl
{
    public WinLibrary()
    {
        InitializeComponent();
        // Files dropped on the lecture are attached to it; on the list of lectures, to the class.
        AttachFiles.AcceptDrops(NoteScroll, () => (DataContext as LibraryModel)?.LectureFiles);
        AttachFiles.AcceptDrops(LectureList, () => (DataContext as LibraryModel)?.ClassFiles);
        // Below 900 px the right column has no room: the list fills the window, and what's opened takes its place.
        SizeChanged += (_, e) =>
        {
            if (DataContext is LibraryModel m) m.Narrow = e.NewSize.Width < LibraryModel.NarrowBelow;
        };
        // The notes fade out under the floating ask bar (shorter while an answer is open), gone for the last quarter.
        Fades.MaskBottom(NoteScroll, Fade, 0.75);
    }

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
