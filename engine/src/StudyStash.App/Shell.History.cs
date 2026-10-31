using Avalonia.Input;
using StudyStash.App.ViewModels;

namespace StudyStash.App;

/// <summary>
/// Back and forward in the library window, as in a browser: each place the student leaves for another (Home, Due, a
/// folder's page, a note, an assignment, a class's Canvas lists) is kept, and Back (⌘[ or Alt+Left) goes to it again.
/// A place is what the window showed, so going back to it opens the same page.
/// </summary>
public static partial class Shell
{
    /// <summary>What the window showed: an overview (Home, Due and the class its calendar was for, or a folder's
    /// page), a note, an assignment on a page of its own, or a class's Canvas lists (the list it was on, and the
    /// assignment or page open beside it).</summary>
    sealed record Place(string? Overview = null, string? DueOnly = null, string? Lecture = null, (string Class, string Id)? Assignment = null,
        string? Canvas = null, ClassTab Tab = ClassTab.Assignments, CanvasReaderModel? Reader = null, bool Study = false);

    const int HistoryLimit = 50;
    static readonly List<Place> backPlaces = [], forwardPlaces = [];
    /// <summary>A place is being gone back (or forward) to, or read again: the navigation that does it isn't a new step.</summary>
    static bool restoring;
    /// <summary>The assignment open, if any (the class and its id): on a page of its own, or beside the Canvas list it
    /// was picked from.</summary>
    static (string Class, string Id)? openAssignment;

    /// <summary>Where the window is: Home, until something else has been opened.</summary>
    static Place Here()
    {
        if (overviewOf is { } over) return new Place(Overview: over, DueOnly: over == DueHome ? dueOnly : null);
        if (openCanvasClass is { } cls) return new Place(Canvas: cls, Tab: canvasTab, Assignment: library.Assignment is null ? null : openAssignment, Reader: library.Reader);
        if (openAssignment is { } assignment) return new Place(Assignment: assignment);
        if (openLecture is { } id) return new Place(Lecture: id);
        if (library.Study is not null) return new Place(Study: true);
        return new Place(Overview: "");
    }

    /// <summary>The student is about to leave this place for another: it's kept for Back, and Forward starts over.</summary>
    static void Remember()
    {
        if (restoring) return;
        var here = Here();
        if (backPlaces.Count > 0 && backPlaces[^1] == here) return;
        backPlaces.Add(here);
        if (backPlaces.Count > HistoryLimit) backPlaces.RemoveAt(0);
        forwardPlaces.Clear();
        UpdateHistory();
    }

    static void GoBack() => Step(backPlaces, forwardPlaces);
    static void GoForward() => Step(forwardPlaces, backPlaces);

    static void Step(List<Place> from, List<Place> to)
    {
        if (from.Count == 0) return;
        var place = from[^1];
        from.RemoveAt(from.Count - 1);
        to.Add(Here());
        UpdateHistory();
        _ = BackToAsync(place);
    }

    static async Task BackToAsync(Place place)
    {
        restoring = true;
        try
        {
            await GoToAsync(place);
        }
        finally
        {
            restoring = false;
        }
    }

    /// <summary>The page a note's own page gives way to when the note is deleted: where the student came from, or Home.</summary>
    static void LeaveNote()
    {
        LeavePage();
        if (backPlaces.Count == 0)
        {
            _ = ShowHomeAsync();
            return;
        }
        var place = backPlaces[^1];
        backPlaces.RemoveAt(backPlaces.Count - 1);
        UpdateHistory();
        _ = BackToAsync(place);
    }

    static async Task GoToAsync(Place place)
    {
        library.NarrowDetail = false;
        if (place.Study)
        {
            await ShowStudyAsync();
            return;
        }
        if (place.Overview is { } over)
        {
            if (over == DueHome) dueOnly = place.DueOnly;
            await ShowOverviewAsync(over);
        }
        else if (place.Canvas is { } cls)
        {
            await ShowCanvasListsAsync(cls, place.Tab);
            if (openCanvasClass != cls) return;
            if (place.Assignment is { } beside) await ShowAssignmentAsync(beside.Class, beside.Id);
            else if (place.Reader is { } reader)
            {
                openAssignment = null;
                library.Assignment = null;
                library.Reader = reader;
            }
            else
            {
                openAssignment = null;
                library.Assignment = null;
                library.Reader = null;
            }
        }
        else if (place.Assignment is { } assignment) await ShowAssignmentPageAsync(assignment.Class, assignment.Id);
        else if (place.Lecture is { } id) await ShowLectureAsync(id);
        else await ShowHomeAsync();
    }

    static void UpdateHistory()
    {
        library.CanGoBack = backPlaces.Count > 0;
        library.CanGoForward = forwardPlaces.Count > 0;
    }

    /// <summary>The library window's own keys: the sidebar (⌃⌘S, Ctrl+Shift+S), Back and Forward (⌘[ ⌘], or Alt+Left
    /// and Alt+Right, and a mouse's back and forward buttons), search (⌘K, Ctrl+K), the caret into the page's Ask
    /// bar (⌘J, Ctrl+J) and Record (⌘R, Ctrl+R).</summary>
    static void AddLibraryKeys(Avalonia.Controls.Window w)
    {
        bool mac = OperatingSystem.IsMacOS();
        void Bind(KeyGesture gesture, Action act) => w.KeyBindings.Add(new KeyBinding { Gesture = gesture, Command = new CommunityToolkit.Mvvm.Input.RelayCommand(act) });
        Bind(mac ? new KeyGesture(Key.S, KeyModifiers.Meta | KeyModifiers.Control) : new KeyGesture(Key.S, KeyModifiers.Control | KeyModifiers.Shift),
            () => library.SidebarHidden = !library.SidebarHidden);
        Bind(mac ? new KeyGesture(Key.OemOpenBrackets, KeyModifiers.Meta) : new KeyGesture(Key.Left, KeyModifiers.Alt), GoBack);
        Bind(mac ? new KeyGesture(Key.OemCloseBrackets, KeyModifiers.Meta) : new KeyGesture(Key.Right, KeyModifiers.Alt), GoForward);
        Bind(new KeyGesture(Key.K, mac ? KeyModifiers.Meta : KeyModifiers.Control), () => library.SearchCommand.Execute(null));
        Bind(new KeyGesture(Key.J, mac ? KeyModifiers.Meta : KeyModifiers.Control), () => Views.AskKeys.Focus(w));
        // Record, from the window (the shortcut in Settings works from any app; this one is the window's own).
        Bind(new KeyGesture(Key.R, mac ? KeyModifiers.Meta : KeyModifiers.Control), ToggleRecording);
        // Undo and Redo for a note's notes, when the page (not a field being typed in) has the keys.
        var command = mac ? KeyModifiers.Meta : KeyModifiers.Control;
        Bind(new KeyGesture(Key.Z, command), () => UndoNotes(w, redo: false));
        Bind(new KeyGesture(Key.Z, command | KeyModifiers.Shift), () => UndoNotes(w, redo: true));
        if (!mac) Bind(new KeyGesture(Key.Y, KeyModifiers.Control), () => UndoNotes(w, redo: true));
        w.AddHandler(Avalonia.Input.InputElement.PointerReleasedEvent, (_, e) =>
        {
            if (e.InitialPressMouseButton == MouseButton.XButton1) GoBack();
            else if (e.InitialPressMouseButton == MouseButton.XButton2) GoForward();
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
    }

    /// <summary>⌘Z / Ctrl+Z (and Redo) on a note's page: the notes' last change undone, or redone. A field being typed in
    /// keeps the keys for its own undo.</summary>
    static void UndoNotes(Avalonia.Controls.Window w, bool redo)
    {
        if (w.FocusManager?.GetFocusedElement() is Avalonia.Controls.TextBox) return;
        if (!library.ShowNotePage || library.Notes is not { Editing: false } notes) return;
        var command = redo ? notes.RedoCommand : notes.UndoCommand;
        if (command.CanExecute(null)) command.Execute(null);
    }
}
