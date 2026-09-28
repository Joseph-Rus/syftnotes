using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using StudyStash.App.ViewModels;

namespace StudyStash.App.Views;

public partial class WinLibrary : UserControl
{
    public WinLibrary()
    {
        InitializeComponent();
        // Below 900 px the right column has no room: the list fills the window, and what's opened takes its place.
        SizeChanged += (_, e) =>
        {
            if (DataContext is LibraryModel m) m.Narrow = e.NewSize.Width < LibraryModel.NarrowBelow;
        };
        // The notes fade out under the floating ask bar. A mask rather than a colour laid over them: whatever is behind
        // the page (Windows 11's Mica, or the design's colour) shows through the fade, with no band where a guessed
        // colour would differ.
        NoteScroll.SizeChanged += (_, _) => FadeNotes();
        Fade.SizeChanged += (_, _) => FadeNotes();
    }

    /// <summary>The notes seen through a mask that is clear over the bottom of the page: <see cref="Fade"/>'s height
    /// (shorter while an answer is open), fully gone for its last quarter.</summary>
    void FadeNotes()
    {
        double h = NoteScroll.Bounds.Height, fade = Fade.Bounds.Height;
        NoteScroll.OpacityMask = h <= 0 || fade <= 0 ? null : new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, Math.Max(0, h - fade), RelativeUnit.Absolute),
            EndPoint = new RelativePoint(0, h, RelativeUnit.Absolute),
            GradientStops = { new GradientStop(Colors.Black, 0), new GradientStop(Colors.Transparent, 0.75) },
        };
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

    void OnNotesTab(object? sender, PointerPressedEventArgs e)
    {
        if ((DataContext as LibraryModel)?.Note is { } n) n.ShowTranscript = false;
    }

    void OnTranscriptTab(object? sender, PointerPressedEventArgs e)
    {
        if ((DataContext as LibraryModel)?.Note is { } n) n.ShowTranscript = true;
    }
}
