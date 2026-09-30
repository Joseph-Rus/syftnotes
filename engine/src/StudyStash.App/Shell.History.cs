using Avalonia.Input;
using StudyStash.App.ViewModels;

namespace StudyStash.App;

/// <summary>
/// Back and forward in the library window, as in a browser: each place the student leaves for another (a class, the
/// Due page, a lecture, an assignment, a Canvas page) is kept, and Back (⌘[ or Alt+Left) goes to it again. A place is
/// what the window showed, so going back to it opens the same class, lecture and page.
/// </summary>
public static partial class Shell
{
    /// <summary>What the window showed: Due (and the row picked there), or a class (its lecture, and an assignment or a
    /// Canvas page over it).</summary>
    sealed record Place(bool Due, (string Class, string Id)? DueRow, string? Class, string? Lecture, bool AllLectures,
        (string Class, string Id)? Assignment, CanvasReaderModel? Reader, ClassTab? Tab = null);

    const int HistoryLimit = 50;
    static readonly List<Place> backPlaces = [], forwardPlaces = [];
    /// <summary>A place is being gone back (or forward) to: the navigation that does it isn't a new step.</summary>
    static bool restoring;
    /// <summary>The assignment on the right, if any (the class and its id), for a place to come back to.</summary>
    static (string Class, string Id)? openAssignment;

    static Place? Here(ClassTab? tab = null)
    {
        if (dueOpen) return new Place(true, dueSelection, null, null, false, null, null);
        if (openClass is not { Length: > 0 } cls) return null;
        return new Place(false, null, cls, openLecture, allLectures, library.Assignment is null ? null : openAssignment, library.Reader,
            tab ?? library.CanvasClass?.Tab);
    }

    /// <summary>The student is about to leave this place for another: it's kept for Back, and Forward starts over.</summary>
    /// <param name="tab">The class page's tab this place was on, when it has just changed (<see cref="ClassTab"/>).</param>
    static void Remember(ClassTab? tab = null)
    {
        if (restoring || Here(tab) is not { } here) return;
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
        if (Here() is { } here) to.Add(here);
        UpdateHistory();
        _ = GoToAsync(place);
    }

    static async Task GoToAsync(Place place)
    {
        restoring = true;
        try
        {
            library.NarrowDetail = false;
            if (place.Due)
            {
                dueSelection = place.DueRow;
                await ShowDueAsync();
                return;
            }
            if (place.Class is not { } cls) return;
            openLecture = place.Lecture;
            allLectures = place.AllLectures;
            await ShowClassAsync(cls);
            // The tab it was on, before what was open over the lecture: going back to Lectures clears that.
            if (place.Tab is { } tab && library.CanvasClass is { } page) page.Tab = tab;
            if (place.Assignment is { } assignment) await ShowAssignmentAsync(assignment.Class, assignment.Id);
            else if (place.Reader is { } reader)
            {
                library.Assignment = null;
                library.Reader = reader;
            }
        }
        finally
        {
            restoring = false;
        }
    }

    static void UpdateHistory()
    {
        library.CanGoBack = backPlaces.Count > 0;
        library.CanGoForward = forwardPlaces.Count > 0;
    }

    /// <summary>The library window's own keys: the sidebar (⌃⌘S, Ctrl+Shift+S), Back and Forward (⌘[ ⌘], or Alt+Left
    /// and Alt+Right, and a mouse's back and forward buttons).</summary>
    static void AddLibraryKeys(Avalonia.Controls.Window w)
    {
        bool mac = OperatingSystem.IsMacOS();
        void Bind(KeyGesture gesture, Action act) => w.KeyBindings.Add(new KeyBinding { Gesture = gesture, Command = new CommunityToolkit.Mvvm.Input.RelayCommand(act) });
        Bind(mac ? new KeyGesture(Key.S, KeyModifiers.Meta | KeyModifiers.Control) : new KeyGesture(Key.S, KeyModifiers.Control | KeyModifiers.Shift),
            () => library.SidebarHidden = !library.SidebarHidden);
        Bind(mac ? new KeyGesture(Key.OemOpenBrackets, KeyModifiers.Meta) : new KeyGesture(Key.Left, KeyModifiers.Alt), GoBack);
        Bind(mac ? new KeyGesture(Key.OemCloseBrackets, KeyModifiers.Meta) : new KeyGesture(Key.Right, KeyModifiers.Alt), GoForward);
        w.AddHandler(Avalonia.Input.InputElement.PointerReleasedEvent, (_, e) =>
        {
            if (e.InitialPressMouseButton == MouseButton.XButton1) GoBack();
            else if (e.InitialPressMouseButton == MouseButton.XButton2) GoForward();
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
    }
}
