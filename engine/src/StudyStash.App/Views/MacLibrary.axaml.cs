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
        // The library window has no accent colour: what's inside that asks for one gets ink.
        Resources.MergedDictionaries.Add(Skin.NoAccent());
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
        Fades.Over(TopFade, "Surface", 0.3);
        Fades.Under(Fade, "Surface", 0.55);
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

    /// <summary>How far in from the window's edge the toolbars' first button sits in full screen: where the sidebar's
    /// own rows start.</summary>
    public const double FullScreenInset = 14;

    /// <summary>The room the traffic lights take in the window's top left corner, from the edge of the sidebar (or of
    /// the page, with the sidebar folded away), each of which starts 8 in from the window's.</summary>
    const double LightsRoom = WindowHeader.LightsRoom - 8;

    Window? window;

    void OnWindowChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == Window.WindowStateProperty) PlaceNav();
    }

    /// <summary>The sidebar's button starts right after the lights (and, with the sidebar folded away, so do it and
    /// Back and Forward at the page's top left); full screen shows no lights at rest, so there they take the corner.</summary>
    void PlaceNav()
    {
        bool full = window?.WindowState == WindowState.FullScreen;
        SideTools.Margin = new Thickness(full ? FullScreenInset - 8 : LightsRoom, 2, 6, 22);
        PaneNav.Margin = new Thickness(full ? 0 : LightsRoom - 12, 0, 0, 0);
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
