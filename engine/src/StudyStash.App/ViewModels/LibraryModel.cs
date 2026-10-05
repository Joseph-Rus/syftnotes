using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StudyStash.App.Services;
using StudyStash.Core;

namespace StudyStash.App.ViewModels;

/// <summary>A class in the sidebar: its dot, name and how many lectures (Unsorted has an inbox instead of a dot).</summary>
public sealed partial class ClassItem : ObservableObject
{
    public string Name { get; init; } = "";
    public IBrush Dot { get; init; } = Brushes.Gray;
    public bool IsUnsorted { get; init; }
    /// <summary>Not a class: what's due soon in every class, from Canvas.</summary>
    public bool IsDue { get; init; }
    /// <summary>Not a class: Home, the overview of every class, atop the sidebar.</summary>
    public bool IsHome { get; init; }
    [ObservableProperty] public partial int Count { get; set; }
    [ObservableProperty] public partial bool Selected { get; set; }
    public bool HasDot => !IsUnsorted;
    /// <summary>The sidebar folder it's in ("" for Classes).</summary>
    public string Group { get; init; } = "";
    /// <summary>The first of its folder: the folder's name heads it in the sidebar.</summary>
    public string Section { get; set; } = "";
    public bool HasSection => Section.Length > 0;
    /// <summary>Its context menu's Move to folder: Classes, every folder it isn't in, and New folder….</summary>
    public List<FolderChoice> MoveChoices { get; set; } = [];
}

/// <summary>One place a class can move to in the sidebar, as its context menu lists it.</summary>
public sealed class FolderChoice(string label, IRelayCommand pick)
{
    public string Label { get; } = label;
    public IRelayCommand Pick { get; } = pick;
}

/// <summary>"Move CS 101 to a new folder": the name being typed, over the library window.</summary>
public sealed partial class NewFolderAsk(string className) : ObservableObject
{
    public string ClassName { get; } = className;
    public string Title => $"Put {ClassName} in a new folder";
    [ObservableProperty] public partial string Name { get; set; } = "";
}

/// <summary>A lecture in the middle column: title, "Tue 23 Sep · 1 h 12 min", and the lecture in a sentence.</summary>
public sealed partial class LectureCard : ObservableObject
{
    public string Id { get; init; } = "";
    public string Title { get; init; } = "";
    public string Meta { get; init; } = "";
    public string Summary { get; init; } = "";
    /// <summary>The class it's filed under (the class showing, unless the list mixes classes).</summary>
    public string ClassName { get; init; } = "";
    public bool Last { get; init; }
    [ObservableProperty] public partial bool Selected { get; set; }
    public bool HasSummary => Summary.Length > 0;
}

/// <summary>Lectures under a heading: "This week", "Last week", "September".</summary>
public sealed class LectureGroup
{
    public string Label { get; init; } = "";
    public bool First { get; init; }
    public ObservableCollection<LectureCard> Items { get; } = [];
}

/// <summary>The lecture open on the right: its notes (Markdown) or its transcript.</summary>
public sealed partial class NoteModel : ObservableObject
{
    public string Id { get; init; } = "";
    public string ClassName { get; init; } = "";
    public IBrush Dot { get; init; } = Brushes.Gray;
    /// <summary>"CS 101 · Tuesday 23 September · 1 h 12 min".</summary>
    public string Meta { get; init; } = "";
    public string Title { get; init; } = "";
    public string Markdown { get; init; } = "";
    /// <summary>The notes aren't written yet (the library is on it), or couldn't be.</summary>
    public string? Pending { get; init; }
    public ObservableCollection<HeardLine> Transcript { get; } = [];
    [ObservableProperty] public partial bool ShowTranscript { get; set; }

    public bool ShowNotes => !ShowTranscript;
    public bool HasPending => !string.IsNullOrEmpty(Pending);
    public bool NoTranscript => Transcript.Count == 0;

    partial void OnShowTranscriptChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowNotes));
        if (!value) CanGoBack = false;
    }

    [RelayCommand] void Notes() => ShowTranscript = false;
    [RelayCommand] void Transcripts() => ShowTranscript = true;

    /// <summary>The transcript as timed lines, when it has times: what a diagram's box is looked for in.</summary>
    public List<Spoken> Spoken { get; } = [];

    /// <summary>The transcript's line to scroll to (its place in <see cref="Transcript"/>), once it's showing.</summary>
    public event Action<int>? Jumped;

    /// <summary>The transcript was opened from the notes at a moment a diagram pointed to: a button takes the student
    /// back to the notes (and the diagram) until they go there.</summary>
    [ObservableProperty] public partial bool CanGoBack { get; set; }

    [RelayCommand] void BackToNotes() => ShowTranscript = false;

    /// <summary>The transcript at a moment: its tab open, the line said then marked, and the page brought to it.</summary>
    public void ShowAt(double seconds)
    {
        if (!ShowTranscript) CanGoBack = true;
        if (Transcript.Count == 0) return;
        int at = 0;
        for (int i = 0; i < Transcript.Count; i++)
            if (Transcript[i].Start is double s && s <= seconds + 0.5) at = i;
        for (int i = 0; i < Transcript.Count; i++) Transcript[i].Here = i == at;
        ShowTranscript = true;
        Jumped?.Invoke(at);
    }
}

/// <summary>A lecture the student is deleting: which, and its title and class, for the confirmation and the "Deleted ·
/// Undo" toast.</summary>
public sealed record LectureDeletion(string Id, string Title, string ClassName)
{
    /// <summary>"Deleted “Recursion and the call stack”".</summary>
    public string Said => $"Deleted “{Title}”";
    /// <summary>What the confirmation says goes: everywhere, notes and all, with a way back for a few minutes.</summary>
    public string Warning => $"“{Title}” is removed from your library on every computer: its notes, transcript and recording. You can undo it for a few minutes.";
}

/// <summary>What the middle column lists: a class's lectures by week, Canvas's Due list, or a class linked to
/// Canvas (its tabs, or its sections in a narrow window).</summary>
public enum LibraryList { Lectures, Due, CanvasClass }

/// <summary>
/// The full app: classes (and Due) on the left, a class's lectures by week (or Canvas's lists) in the middle, and on
/// the right the lecture (its notes, which another engine can rewrite, and an ask bar floating over it), an
/// assignment, or a Canvas page. Below 900 px there's no room for the right column: the list fills the window, and
/// opening something shows it in the list's place, with a way back.
/// </summary>
public sealed partial class LibraryModel : ObservableObject
{
    /// <summary>What's coming up on the student's calendars, at the foot of the sidebar.</summary>
    public ComingUpModel ComingUp { get; } = new();

    /// <summary>Below this width the window shows one column after the sidebar, not two.</summary>
    public const double NarrowBelow = 900;

    public ObservableCollection<ClassItem> Classes { get; } = [];
    /// <summary>What's due soon, if Canvas has anything: the sidebar draws it above "Classes".</summary>
    public ClassItem? Due => Classes.FirstOrDefault(c => c.IsDue);
    public bool HasDue => Due is not null;
    /// <summary>Where lectures the library couldn't place wait: always in the sidebar under the classes, even
    /// empty, so they're one click away.</summary>
    [ObservableProperty] public partial ClassItem Unsorted { get; set; } = new() { Name = Configs.Unsorted, IsUnsorted = true };
    /// <summary>Home, atop the sidebar: every class at once.</summary>
    public ClassItem Home { get; } = new() { Name = "Home", IsHome = true };
    /// <summary>Home or a class's home, across the list and the page beside it; null shows those instead.</summary>
    [ObservableProperty] public partial OverviewModel? Overview { get; set; }
    public bool ShowOverview => Overview is not null;
    public ObservableCollection<LectureGroup> Groups { get; } = [];
    [ObservableProperty] public partial string ClassTitle { get; set; } = "";
    [ObservableProperty] public partial string ClassCount { get; set; } = "";
    [ObservableProperty] public partial NoteModel? Note { get; set; }
    [ObservableProperty] public partial string Status { get; set; } = "";
    [ObservableProperty] public partial bool StatusGood { get; set; } = true;
    [ObservableProperty] public partial string SearchText { get; set; } = "";
    /// <summary>Draw the window's own buttons (screenshots; a real window has the system's).</summary>
    [ObservableProperty] public partial bool DrawChrome { get; set; }
    /// <summary>The class has no lectures yet, or nothing is chosen: what the middle column says.</summary>
    [ObservableProperty] public partial string? Empty { get; set; }

    [ObservableProperty] public partial LibraryList List { get; set; } = LibraryList.Lectures;
    /// <summary>Canvas's Due list, while Due is open.</summary>
    [ObservableProperty] public partial CanvasDueModel? DueList { get; set; }
    /// <summary>What's wrong with Canvas (not set up, signed out, Chrome away…), atop the Due list; null while it's
    /// connected or syncing.</summary>
    [ObservableProperty] public partial CanvasStatusModel? DueStatus { get; set; }
    public bool HasDueStatus => DueStatus is not null;
    /// <summary>A class linked to Canvas, while it's open.</summary>
    [ObservableProperty] public partial CanvasClassModel? CanvasClass { get; set; }
    /// <summary>An assignment open on the right (from Due, or a class's own lists); null shows the lecture.</summary>
    [ObservableProperty] public partial AssignmentModel? Assignment { get; set; }
    /// <summary>A Canvas page or announcement open on the right; null shows the lecture.</summary>
    [ObservableProperty] public partial CanvasReaderModel? Reader { get; set; }
    /// <summary>The open lecture's notes, with "Rewrite notes"; null while the notes aren't written yet.</summary>
    [ObservableProperty] public partial AiNotesModel? Notes { get; set; }
    /// <summary>The ask bar under the open lecture, with its engine picker.</summary>
    [ObservableProperty] public partial AiAskModel? Ask { get; set; }
    /// <summary>What a diagram in the open lecture's notes can do with the page: ask about a box, show the transcript or
    /// play the recording at a moment (the page's views hand it to their diagrams).</summary>
    public Controls.Rich.IDiagramHost? Diagrams { get; set; }
    /// <summary>What's attached to the open lecture (files dropped on it land here); null with no lecture open.</summary>
    [ObservableProperty] public partial AttachmentsModel? LectureFiles { get; set; }
    /// <summary>What's attached in the class showing (files dropped on its list land here); null on Due.</summary>
    [ObservableProperty] public partial AttachmentsModel? ClassFiles { get; set; }
    public bool HasLectureFiles => LectureFiles is not null;
    public bool HasClassFiles => ClassFiles is not null;

    partial void OnLectureFilesChanged(AttachmentsModel? oldValue, AttachmentsModel? newValue)
    {
        oldValue?.Dispose();
        OnPropertyChanged(nameof(HasLectureFiles));
    }

    partial void OnClassFilesChanged(AttachmentsModel? oldValue, AttachmentsModel? newValue)
    {
        oldValue?.Dispose();
        OnPropertyChanged(nameof(HasClassFiles));
    }

    /// <summary>The window is too narrow for the right column (below <see cref="NarrowBelow"/>).</summary>
    [ObservableProperty] public partial bool Narrow { get; set; }
    /// <summary>Narrow, and showing what was opened in the list's place.</summary>
    [ObservableProperty] public partial bool NarrowDetail { get; set; }

    /// <summary>A class's column (its header, and the Canvas tabs when it's linked): every class but Due.</summary>
    public bool ShowLectures => List is LibraryList.Lectures or LibraryList.CanvasClass;
    public bool ShowDueList => List == LibraryList.Due;
    /// <summary>A Canvas-linked class: the Lectures / Assignments / Modules / Announcements switcher over its list.</summary>
    public bool HasClassTabs => List == LibraryList.CanvasClass && CanvasClass is not null;
    /// <summary>The lectures by week: a plain class, or a Canvas class on its Lectures tab.</summary>
    public bool ShowLectureGroups => !HasClassTabs || CanvasClass!.IsLecturesTab;
    /// <summary>One of a Canvas class's own lists, under the switcher.</summary>
    public bool ShowClassTabBody => HasClassTabs && !CanvasClass!.IsLecturesTab;

    public bool ShowAssignment => Assignment is not null;
    public bool ShowReader => Assignment is null && Reader is not null;
    /// <summary>The lecture's page, with its toolbar and ask bar: whenever no assignment or Canvas page is open.</summary>
    public bool ShowLecturePage => Assignment is null && Reader is null;
    /// <summary>Move, export and delete: only over a lecture that's open (not the empty "Choose a lecture" page).</summary>
    public bool ShowLectureTools => ShowLecturePage && HasNote && Overview is null;
    /// <summary>Open in Canvas, over an assignment on the right (not over an overview).</summary>
    public bool ShowAssignmentTools => ShowAssignment && Overview is null;
    public bool HasNotes => Notes is not null;
    public bool HasAsk => Ask is not null && HasNote && ShowLecturePage;
    /// <summary>An answer to show above the ask bar: the page gives it its own room, and the notes end above it.</summary>
    public bool HasAnswer => HasAsk && Ask?.HasLatest == true;

    /// <summary>The list column: the design's 312 (Mac) or 320 (Windows) for lectures, 340 for Canvas's lists (360
    /// for a Windows class page), and the whole window when it's narrow.</summary>
    public double ListWidth => Narrow ? double.NaN : List switch
    {
        LibraryList.Lectures => Skin.Current == SkinKind.Mac ? 312 : 320,
        LibraryList.CanvasClass when Skin.Current == SkinKind.Win => 380,
        LibraryList.CanvasClass => 360,
        _ => 340,
    };
    public bool ShowListColumn => Overview is null && (Narrow ? !NarrowDetail : !ListHidden);
    public bool ShowDetailColumn => Overview is null && (!Narrow || NarrowDetail);
    /// <summary>Where the right column sits: its own column, or the list's when it's narrow.</summary>
    public int DetailColumn => Narrow ? 1 : 2;
    public int ColumnSpan => Narrow ? 2 : 1;

    public LibraryModel()
    {
        Classes.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(Due));
            OnPropertyChanged(nameof(HasDue));
        };
    }

    /// <summary>The sidebar's counts as the library says them now, the list itself left as it is: what's selected stays
    /// selected. A lecture filed while the window is open changes a class's count without anything else being reloaded.</summary>
    public void ShowCounts(IEnumerable<(string Name, int Lectures)> classes, int unsorted)
    {
        foreach (var (name, lectures) in classes)
            if (Classes.FirstOrDefault(c => c.Name == name && !c.IsDue && !c.IsHome) is { } item) item.Count = lectures;
        Unsorted.Count = unsorted;
    }

    partial void OnListChanged(LibraryList value)
    {
        OnPropertyChanged(nameof(ShowLectures));
        OnPropertyChanged(nameof(ShowDueList));
        ClassTabsChanged();
        OnPropertyChanged(nameof(ListWidth));
    }

    partial void OnDueStatusChanged(CanvasStatusModel? value) => OnPropertyChanged(nameof(HasDueStatus));
    partial void OnAssignmentChanged(AssignmentModel? value) => DetailChanged();
    partial void OnReaderChanged(CanvasReaderModel? value) => DetailChanged();
    partial void OnNotesChanged(AiNotesModel? value) => OnPropertyChanged(nameof(HasNotes));
    partial void OnAskChanged(AiAskModel? oldValue, AiAskModel? newValue)
    {
        if (oldValue is not null) oldValue.PropertyChanged -= AskPropertyChanged;
        if (newValue is not null) newValue.PropertyChanged += AskPropertyChanged;
        OnPropertyChanged(nameof(HasAsk));
        OnPropertyChanged(nameof(HasAnswer));
    }

    void AskPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AiAskModel.HasLatest)) OnPropertyChanged(nameof(HasAnswer));
    }

    void DetailChanged()
    {
        OnPropertyChanged(nameof(ShowAssignment));
        OnPropertyChanged(nameof(ShowAssignmentTools));
        OnPropertyChanged(nameof(ShowReader));
        OnPropertyChanged(nameof(ShowLecturePage));
        OnPropertyChanged(nameof(ShowLectureTools));
        OnPropertyChanged(nameof(HasAsk));
        OnPropertyChanged(nameof(HasAnswer));
    }

    partial void OnOverviewChanged(OverviewModel? oldValue, OverviewModel? newValue)
    {
        if (oldValue?.Files is { } files && !ReferenceEquals(files, newValue?.Files) && !ReferenceEquals(files, ClassFiles)) files.Dispose();
        if (newValue is not null) newValue.Narrow = Narrow;
        OnPropertyChanged(nameof(ShowOverview));
        OnPropertyChanged(nameof(CanToggleList));
        OnPropertyChanged(nameof(ShowListColumn));
        OnPropertyChanged(nameof(ShowDetailColumn));
        OnPropertyChanged(nameof(ShowLectureTools));
        OnPropertyChanged(nameof(ShowAssignmentTools));
    }

    partial void OnNarrowChanged(bool value)
    {
        if (Overview is { } o) o.Narrow = value;
        if (!value) NarrowDetail = false;
        OnPropertyChanged(nameof(ListWidth));
        OnPropertyChanged(nameof(DetailColumn));
        OnPropertyChanged(nameof(CanToggleList));
        OnPropertyChanged(nameof(ColumnSpan));
        OnPropertyChanged(nameof(ShowListColumn));
        OnPropertyChanged(nameof(ShowDetailColumn));
    }

    partial void OnNarrowDetailChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowListColumn));
        OnPropertyChanged(nameof(ShowDetailColumn));
    }

    partial void OnCanvasClassChanged(CanvasClassModel? oldValue, CanvasClassModel? newValue)
    {
        if (oldValue is not null) oldValue.PropertyChanged -= CanvasClassPropertyChanged;
        if (newValue is not null)
        {
            // The library window draws the class's header and switcher itself, over its own lecture list.
            newValue.Layout = ClassLayout.Tabs;
            newValue.Embedded = true;
            newValue.PropertyChanged += CanvasClassPropertyChanged;
        }
        ClassTabsChanged();
    }

    void CanvasClassPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(CanvasClassModel.Tab)) ClassTabsChanged();
    }

    void ClassTabsChanged()
    {
        OnPropertyChanged(nameof(HasClassTabs));
        OnPropertyChanged(nameof(ShowLectureGroups));
        OnPropertyChanged(nameof(ShowClassTabBody));
    }

    /// <summary>Shows what the student opened (a lecture, an assignment, a page): in a narrow window it takes the
    /// list's place until they go back.</summary>
    public void Opened()
    {
        if (Narrow) NarrowDetail = true;
    }

    [RelayCommand] void Back() => NarrowDetail = false;

    public bool HasNote => Note is not null;
    public bool NoNote => Note is null;
    /// <summary>What the empty page beside the list says: pick a lecture, or that there are none to pick.</summary>
    [ObservableProperty] public partial string NoNoteText { get; set; } = "Choose a lecture to read its notes.";
    public bool HasEmpty => !string.IsNullOrEmpty(Empty);

    partial void OnNoteChanged(NoteModel? value)
    {
        if (LectureFiles is { } files && files.LectureId != value?.Id) LectureFiles = null; // another lecture's files don't stay
        OnPropertyChanged(nameof(HasNote));
        OnPropertyChanged(nameof(ShowLectureTools));
        OnPropertyChanged(nameof(NoNote));
        OnPropertyChanged(nameof(HasAsk));
        OnPropertyChanged(nameof(HasAnswer));
    }

    partial void OnEmptyChanged(string? value) => OnPropertyChanged(nameof(HasEmpty));

    // --- the ask for a tip ------------------------------------------------------------------------------------------

    /// <summary>The one gentle ask for a tip, atop the lecture list (<see cref="SupportAsk"/> says when); null when
    /// it isn't asking.</summary>
    [ObservableProperty] public partial SupportAskModel? Support { get; set; }
    public bool HasSupport => Support is not null;
    /// <summary>What the student answered: the host saves it, and opens the Ko-fi page for a tip.</summary>
    public Action<SupportAnswer>? OnSupportAnswer { get; set; }

    partial void OnSupportChanged(SupportAskModel? value) => OnPropertyChanged(nameof(HasSupport));

    /// <summary>Shows the ask (unless it's showing already); <paramref name="last"/> leaves out "Maybe later". Any
    /// answer puts it away.</summary>
    public void AskForSupport(bool last) => Support ??= new SupportAskModel(last, answer =>
    {
        Support = null;
        OnSupportAnswer?.Invoke(answer);
    });

    /// <summary>The window closes with the ask still showing: that counts as "Maybe later" (or, the last time, as
    /// the end of it), so an ask that's ignored still comes at most twice.</summary>
    public void WalkAwayFromSupport() => Support?.LaterCommand.Execute(null);

    // --- deleting a lecture ---------------------------------------------------------------------------------------

    /// <summary>The lecture being deleted, while the window asks "Delete this lecture?"; null when it isn't asking.</summary>
    /// <summary>A new sidebar folder being named for a class; null when nobody's asking.</summary>
    [ObservableProperty] public partial NewFolderAsk? NamingFolder { get; set; }
    public bool AskingFolder => NamingFolder is not null;
    partial void OnNamingFolderChanged(NewFolderAsk? value) => OnPropertyChanged(nameof(AskingFolder));
    /// <summary>Moves a class into a sidebar folder ("" for Classes).</summary>
    public Func<string, string, Task>? OnMoveToFolder { get; set; }
    [RelayCommand] void CancelFolder() => NamingFolder = null;
    [RelayCommand]
    async Task ConfirmFolder()
    {
        if (NamingFolder is not { } ask || ask.Name.Trim().Length == 0) return;
        NamingFolder = null;
        if (OnMoveToFolder is { } move) await move(ask.ClassName, ask.Name.Trim());
    }

    [ObservableProperty] public partial LectureDeletion? Deleting { get; set; }
    /// <summary>The lecture just deleted, while the "Deleted · Undo" toast shows.</summary>
    [ObservableProperty] public partial LectureDeletion? Deleted { get; set; }
    public bool AskingDelete => Deleting is not null;
    public bool HasDeleted => Deleted is not null;
    /// <summary>How long "Deleted · Undo" stays (the library keeps the lecture in its trash for longer).</summary>
    public TimeSpan UndoShows { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>Deletes the lecture on the library (the host calls its API); false if the library refused.</summary>
    public Func<LectureDeletion, Task<bool>>? OnDelete { get; set; }
    /// <summary>Brings the lecture back from the library's trash (the host calls its API and lists it again).</summary>
    public Func<LectureDeletion, Task>? OnUndo { get; set; }

    partial void OnDeletingChanged(LectureDeletion? value) => OnPropertyChanged(nameof(AskingDelete));
    partial void OnDeletedChanged(LectureDeletion? value) => OnPropertyChanged(nameof(HasDeleted));

    /// <summary>Asks before deleting a lecture: a row's (its context menu) or, with none, the open one (its header).</summary>
    [RelayCommand]
    void Delete(LectureCard? card)
    {
        if (card is not null) Deleting = new LectureDeletion(card.Id, card.Title, card.ClassName.Length > 0 ? card.ClassName : ClassTitle);
        else if (Note is { } n) Deleting = new LectureDeletion(n.Id, n.Title, n.ClassName);
    }

    [RelayCommand]
    void CancelDelete() => Deleting = null;

    /// <summary>Delete, confirmed: the lecture leaves the list at once (the next one opens in its place), the
    /// library deletes it, and "Deleted · Undo" shows for a few seconds. If the library refuses, the host lists
    /// the class again.</summary>
    [RelayCommand(AllowConcurrentExecutions = true)]
    async Task ConfirmDelete()
    {
        if (Deleting is not { } d) return;
        Deleting = null;
        var next = Remove(d);
        Deleted = d;
        if (next is not null && !Narrow) OnLecture?.Invoke(next);
        bool ok = OnDelete is null || await OnDelete(d);
        if (!ok)
        {
            if (Deleted == d) Deleted = null;
            return;
        }
        _ = HideDeletedAfterAWhile(d);
    }

    async Task HideDeletedAfterAWhile(LectureDeletion d)
    {
        await Task.Delay(UndoShows);
        if (Deleted == d) Deleted = null;
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    async Task Undo()
    {
        if (Deleted is not { } d) return;
        Deleted = null;
        if (OnUndo is not null) await OnUndo(d);
    }

    [RelayCommand]
    void DismissDeleted() => Deleted = null;

    /// <summary>Takes a lecture out of the list and the counts, and closes it if it's open. Hands back the lecture to
    /// open in its place: the one after it, or before it at the end.</summary>
    public LectureCard? Remove(LectureDeletion d)
    {
        var cards = Groups.SelectMany(g => g.Items).ToList();
        int at = cards.FindIndex(c => c.Id == d.Id);
        LectureCard? next = null;
        if (at >= 0)
        {
            next = at + 1 < cards.Count ? cards[at + 1] : at > 0 ? cards[at - 1] : null;
            foreach (var g in Groups.ToList())
            {
                if (g.Items.FirstOrDefault(c => c.Id == d.Id) is not { } card) continue;
                g.Items.Remove(card);
                if (g.Items.Count == 0) Groups.Remove(g);
            }
            int left = cards.Count - 1;
            ClassCount = $"{left} lecture{(left == 1 ? "" : "s")}";
            if (left == 0) Empty = $"No lectures in {ClassTitle} yet. Record one and it lands here.";
        }
        var cls = d.ClassName == Unsorted.Name ? Unsorted : Classes.FirstOrDefault(c => c.Name == d.ClassName && !c.IsDue);
        if (cls is { Count: > 0 }) cls.Count--;
        bool open = Note?.Id == d.Id;
        if (open)
        {
            Note = null;
            NarrowDetail = false;
        }
        return open ? next : null;
    }

    public Action<ClassItem>? OnClass { get; set; }
    public Action<LectureCard>? OnLecture { get; set; }
    public Action? OnMove { get; set; }
    public Action? OnExport { get; set; }
    public Action? OnSearch { get; set; }
    public Action? OnSettings { get; set; }
    public Action? OnMore { get; set; }

    /// <summary>The sidebar is folded away, leaving the list and the page the whole window.</summary>
    [ObservableProperty] public partial bool SidebarHidden { get; set; }
    public bool ShowSidebar => !SidebarHidden;
    partial void OnSidebarHiddenChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowSidebar));
        OnSidebarToggled?.Invoke(value);
    }

    /// <summary>There's somewhere to go back (or forward) to, as in a browser: the places the student picked.</summary>
    [ObservableProperty] public partial bool CanGoBack { get; set; }
    [ObservableProperty] public partial bool CanGoForward { get; set; }

    public Action<bool>? OnSidebarToggled { get; set; }
    public Action? OnGoBack { get; set; }
    public Action? OnGoForward { get; set; }

    /// <summary>A class's lectures by week: its home (what's due, what's coming up, its Canvas lists) is a click away.</summary>
    [ObservableProperty] public partial bool CanShowClassPage { get; set; }
    public Action? OnClassPage { get; set; }
    [RelayCommand] void ShowClassPage() => OnClassPage?.Invoke();

    [RelayCommand] void ToggleSidebar() => SidebarHidden = !SidebarHidden;

    /// <summary>The list (a class's lectures, Due) is folded away, leaving the page beside it the room: the toolbar's
    /// list button, ⌃⌘L or Ctrl+Shift+L. A narrow window, which shows one or the other, ignores it.</summary>
    [ObservableProperty] public partial bool ListHidden { get; set; }
    /// <summary>The list button: wherever there's a list beside a page (not on Home or a class's home, nor narrow).</summary>
    public bool CanToggleList => Overview is null && !Narrow;
    public Action<bool>? OnListToggled { get; set; }
    partial void OnListHiddenChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowListColumn));
        OnListToggled?.Invoke(value);
    }
    [RelayCommand] void ToggleList() => ListHidden = !ListHidden;
    [RelayCommand] void GoBack() => OnGoBack?.Invoke();
    [RelayCommand] void GoForward() => OnGoForward?.Invoke();

    [RelayCommand] void PickClass(ClassItem c) => OnClass?.Invoke(c);
    [RelayCommand] void PickLecture(LectureCard l) => OnLecture?.Invoke(l);
    [RelayCommand] void Move() => OnMove?.Invoke();
    [RelayCommand] void Export() => OnExport?.Invoke();
    [RelayCommand] void Search() => OnSearch?.Invoke();
    [RelayCommand] void Settings() => OnSettings?.Invoke();
    [RelayCommand] void More() => OnMore?.Invoke();
}
