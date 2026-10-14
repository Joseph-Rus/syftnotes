using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StudyStash.App.Services;
using StudyStash.Core;

namespace StudyStash.App.ViewModels;

/// <summary>A folder in the sidebar: its swatch, name and how many notes (Home and Due have an icon instead).</summary>
public sealed partial class ClassItem : ObservableObject
{
    public string Name { get; init; } = "";
    public IBrush Dot { get; init; } = Brushes.Gray;
    /// <summary>Not a folder: what's due in every class, from Canvas.</summary>
    public bool IsDue { get; init; }
    /// <summary>Not a folder: Home, atop the sidebar.</summary>
    public bool IsHome { get; init; }
    [ObservableProperty] public partial int Count { get; set; }
    [ObservableProperty] public partial bool Selected { get; set; }
    /// <summary>The name as the sidebar shows it: without the registrar's code on the end.</summary>
    public string Display => IsDue || IsHome ? Name : ClassNames.Display(Name);
    /// <summary>The whole name, and its code: the tooltip of a name cut short.</summary>
    public string Tip => ClassNames.Code(Name) is { Length: > 0 } code ? $"{Display} · {code}" : Display;
    /// <summary>A count of nothing isn't shown.</summary>
    public bool ShowCount => Count > 0;
    /// <summary>What a screen reader says for its row: "Home", "Due, 18 to hand in", "BIO 110, 4 notes".</summary>
    public string Said => !ShowCount ? Display : IsDue ? $"{Display}, {Count} to hand in" : $"{Display}, {Count} note{(Count == 1 ? "" : "s")}";
    partial void OnCountChanged(int value)
    {
        OnPropertyChanged(nameof(ShowCount));
        OnPropertyChanged(nameof(Said));
    }
    /// <summary>The sidebar group it's in ("" for Classes; "Clubs", "Work", or one of the student's own).</summary>
    public string Group { get; init; } = "";
    /// <summary>What its recordings are called: lectures in a class, meetings in any other group.</summary>
    public string Noun => Nouns.For(Name, Group);
    /// <summary>The first of its group: the group's name heads it in the sidebar.</summary>
    public string Section { get; set; } = "";
    public bool HasSection => Section.Length > 0;
    /// <summary>Its context menu's Move to group: Classes, Clubs, Work, every other group it isn't in, and New group….</summary>
    public List<FolderChoice> MoveChoices { get; set; } = [];
}

/// <summary>One place a class can move to in the sidebar, as its context menu lists it.</summary>
public sealed class FolderChoice(string label, IRelayCommand pick)
{
    public string Label { get; } = label;
    public IRelayCommand Pick { get; } = pick;
}

/// <summary>"Put CS 101 in a new group": the name being typed, over the library window.</summary>
public sealed partial class NewFolderAsk(string className) : ObservableObject
{
    public string ClassName { get; } = className;
    public string Title => $"Put {ClassNames.Display(ClassName)} in a new group";
    [ObservableProperty] public partial string Name { get; set; } = "";
}

/// <summary>
/// A note's page: a lecture, a meeting or a recording, filling the pane. Over its title, where it is (its folder, a
/// link to that folder's page, and a way to move it) and what it is ("Lecture, Monday 12 October at 9:30 AM, 52
/// min"); the title is typed over to rename it; under it Notes and Transcript. While it's being recorded
/// (<see cref="Live"/>) the page has the recording's bar at its top, the notes are the student's own typing, and the
/// transcript is what's been heard so far.
/// </summary>
public sealed partial class NoteModel : ObservableObject
{
    public string Id { get; init; } = "";
    /// <summary>The folder it's in ("" or Unsorted for none).</summary>
    public string ClassName { get; init; } = "";
    public IBrush Dot { get; init; } = Brushes.Gray;
    /// <summary>Lecture, meeting or recording, by its folder.</summary>
    public string Noun { get; init; } = Nouns.Lecture;
    public bool IsUnsorted => ClassName.Length == 0 || ClassName == Configs.Unsorted;
    public bool HasClass => !IsUnsorted;
    /// <summary>The folder as the page's top line names it (no registrar's code).</summary>
    public string ClassShown => IsUnsorted ? "Unsorted" : ClassNames.Display(ClassName);
    /// <summary>When it was, and how long: "Monday 12 October at 9:30 AM, 52 min".</summary>
    public string When { get; init; } = "";
    /// <summary>The rest of the top line: "Lecture, Monday 12 October at 9:30 AM, 52 min".</summary>
    public string Crumb => When.Length > 0 ? $"{Nouns.Capital(Noun)}, {When}" : Nouns.Capital(Noun);
    /// <summary>What a screen reader says for the folder's link.</summary>
    public string FolderSaid => IsUnsorted ? "Not in a folder" : $"Open {ClassShown}";
    public string Title { get; init; } = "";
    public string Markdown { get; init; } = "";
    /// <summary>The notes shown are the student's own typing and nothing else yet: all of it reads as theirs.</summary>
    public bool Mine { get; init; }
    /// <summary>The notes aren't written yet (the library is on it), or couldn't be.</summary>
    public string? Pending { get; init; }
    /// <summary>The library was asked to write these notes again and couldn't: why, said over the notes it left as
    /// they were.</summary>
    public string? NotRewritten { get; init; }
    public bool WasNotRewritten => !string.IsNullOrEmpty(NotRewritten);
    public ObservableCollection<HeardLine> Transcript { get; } = [];
    [ObservableProperty] public partial bool ShowTranscript { get; set; }

    public bool ShowNotes => !ShowTranscript;
    public bool HasPending => !string.IsNullOrEmpty(Pending);
    public bool NoTranscript => Lines.Count == 0;
    /// <summary>What the page was last drawn from: a reload that finds the same leaves the page as it is.</summary>
    public string Stamp { get; init; } = "";
    /// <summary>The library doesn't have it yet: this computer recorded it and is still writing it down, or sending it.</summary>
    public bool Local { get; init; }

    /// <summary>Keep recording: what's said now goes on the end of its transcript (minutes or days later), and its
    /// notes are written again. Only for one this computer recorded, and not while it's the one recording.</summary>
    public Action? OnContinue { get; init; }
    public bool CanContinue => OnContinue is not null && !Live;
    [RelayCommand] void Continue() => OnContinue?.Invoke();

    // --- the title, typed over to rename ---------------------------------------------------------------------------

    /// <summary>What's in the title's field.</summary>
    [ObservableProperty] public partial string TitleText { get; set; } = "";
    /// <summary>Renames it (the library's, or while it records the recording's own); false leaves the old name.</summary>
    public Func<string, Task<bool>>? OnRename { get; set; }
    public bool CanRename => OnRename is not null;
    string named = "";

    /// <summary>The title as it stands (set once, when the page is made).</summary>
    public NoteModel Titled()
    {
        named = Title;
        TitleText = Title;
        return this;
    }

    /// <summary>Return, or the caret leaving the title: a new name is kept, an empty one goes back to what it was.</summary>
    [RelayCommand]
    async Task CommitTitle()
    {
        string typed = TitleText.Trim();
        if (typed.Length == 0 || typed == named || OnRename is null)
        {
            TitleText = named;
            return;
        }
        string before = named;
        named = typed;
        TitleText = typed;
        if (await OnRename(typed)) return;
        named = before;
        TitleText = before;
    }

    // --- while it records ------------------------------------------------------------------------------------------

    /// <summary>It's being recorded now.</summary>
    public bool Live { get; init; }
    public bool Saved => !Live && !Planned;
    /// <summary>Opened for a calendar event that hasn't started: nothing is recorded yet, notes can be typed, and
    /// recording starts on its own when the event does (or with Start now).</summary>
    public bool Planned { get; init; }
    /// <summary>The planned bar's words: "Starts at 10:30 AM. Recording starts on its own then."</summary>
    public string PlanWords { get; init; } = "";
    public Action? OnStartNow { get; init; }
    [RelayCommand] void StartNow() => OnStartNow?.Invoke();
    /// <summary>The student's own notes are typed here: while it records, or before, for a planned one.</summary>
    public bool Typing => Live || Planned;
    /// <summary>The recording: its level, its time, Pause and Stop; what's been heard so far is its lines.</summary>
    public RecorderModel? Recorder { get; init; }
    /// <summary>What the student types while it records: their own notes, which the write-up is built round.</summary>
    [ObservableProperty] public partial string Typed { get; set; } = "";
    public Action<string>? OnTyped { get; set; }
    partial void OnTypedChanged(string value) => OnTyped?.Invoke(value);
    public string TypedHint => Planned ? $"Jot down anything before it starts. When the recording stops, {Writer} writes up the {Noun} around your notes."
        : $"Write as much or as little as you like. When you stop recording, {Writer} writes up the {Noun} around your notes.";
    /// <summary>Who writes the notes when it stops, as the hint names them.</summary>
    public string Writer { get; init; } = "Claude";
    public string TranscriptTab => Live ? "Live transcript" : "Transcript";
    /// <summary>The transcript's lines: what's been heard so far while it records, the whole of it after.</summary>
    public ObservableCollection<HeardLine> Lines => Live && Recorder is not null ? Recorder.Lines : Transcript;
    public string NoTranscriptText => Live ? Recorder?.Waiting ?? RecorderModel.LiveWaiting
        : Planned ? "Nothing yet: the transcript starts when recording does." : $"This {Noun} has no transcript.";
    public string RecordingWord => Recorder?.IsPaused == true ? "Paused" : "Recording";
    /// <summary>The bar's dot breathes while it records: not while paused, nor for someone who asked for less motion.</summary>
    public bool Pulses => Live && Recorder?.IsPaused != true && !Platform.Motion.Reduced;
    /// <summary>It can be moved to another folder: once it's a note in the library (not while it records, or is on its way).</summary>
    public bool CanMove => !Live && !Local;
    public string PauseWord => Recorder?.IsPaused == true ? "Resume" : "Pause";
    /// <summary>What a screen reader hears of the bar: "Recording, 12:04".</summary>
    public string RecordingSaid => $"{RecordingWord}, {Recorder?.Elapsed}";

    /// <summary>Follows the recorder for the bar's words (Recording or Paused) and the live transcript's emptiness.</summary>
    public NoteModel Following()
    {
        if (Recorder is null) return this;
        Recorder.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(RecorderModel.IsPaused))
            {
                OnPropertyChanged(nameof(RecordingWord));
                OnPropertyChanged(nameof(PauseWord));
                OnPropertyChanged(nameof(Pulses));
            }
            if (e.PropertyName is nameof(RecorderModel.IsPaused) or nameof(RecorderModel.Elapsed)) OnPropertyChanged(nameof(RecordingSaid));
            if (e.PropertyName is nameof(RecorderModel.Waiting)) OnPropertyChanged(nameof(NoTranscriptText));
        };
        Recorder.Lines.CollectionChanged += (_, _) => OnPropertyChanged(nameof(NoTranscript));
        return this;
    }

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

/// <summary>A note the student is deleting: which, its title and folder and what it's called (a lecture, a meeting, a
/// recording), for the confirmation and the "Deleted · Undo" toast.</summary>
public sealed record LectureDeletion(string Id, string Title, string ClassName, string Noun = Nouns.Lecture)
{
    /// <summary>"Delete this lecture?"</summary>
    public string Ask => $"Delete this {Noun}?";
    /// <summary>"Deleted “Recursion and the call stack”".</summary>
    public string Said => $"Deleted “{Title}”";
    /// <summary>What the confirmation says goes: everywhere, notes and all, with a way back for a few minutes.</summary>
    public string Warning => $"“{Title}” is removed from your library on every computer: its notes, transcript and recording. You can undo it for a few minutes.";
}

/// <summary>A note just moved to another folder, while "Moved to Machine Learning. Undo" shows.</summary>
public sealed record NoteMove(string Id, string From, string To)
{
    public string Said => $"Moved to {(To.Length == 0 || To == Configs.Unsorted ? "Unsorted" : ClassNames.Display(To))}.";
}

/// <summary>A question over the window with two answers: "Stop recording Data Mining and start Capstone team sync?"
/// (Stop and start, or Cancel).</summary>
public sealed record ConfirmAsk(string Title, string Text, string Yes, Action Act);

/// <summary>
/// The full app: one sidebar (Home, Due, the folders under Classes, Clubs and Work, and what's coming up), and beside
/// it one page that fills the pane. The page is Home, Due's calendar or a folder's page (<see cref="Overview"/>); a
/// note's page (<see cref="Note"/>: its notes, which another AI can rewrite, its transcript, and an ask bar floating
/// over it); an assignment; or a class's lists on Canvas (<see cref="CanvasClass"/>), the one place with a list
/// beside what's opened from it.
/// </summary>
public sealed partial class LibraryModel : ObservableObject
{
    /// <summary>What's coming up on the student's calendars, at the foot of the sidebar: each a button that records it.</summary>
    public ComingUpModel ComingUp { get; } = new();

    /// <summary>Below this width a page draws its margins in, and a class's Canvas lists show one column, not two.</summary>
    public const double NarrowBelow = 900;

    public ObservableCollection<ClassItem> Classes { get; } = [];
    /// <summary>What's due, if Canvas has anything: the sidebar draws it under Home.</summary>
    public ClassItem? Due => Classes.FirstOrDefault(c => c.IsDue);
    public bool HasDue => Due is not null;
    /// <summary>How many notes are in no folder yet (Home's filter says so).</summary>
    [ObservableProperty] public partial int UnsortedCount { get; set; }
    /// <summary>Home, atop the sidebar.</summary>
    public ClassItem Home { get; } = new() { Name = "Home", IsHome = true };
    /// <summary>Home, Due or a folder's page; null shows what's open instead (a note, an assignment, Canvas's lists).</summary>
    [ObservableProperty] public partial OverviewModel? Overview { get; set; }
    public bool ShowOverview => Overview is not null;
    [ObservableProperty] public partial NoteModel? Note { get; set; }
    [ObservableProperty] public partial string Status { get; set; } = "";
    /// <summary>The status as the sidebar's foot shows it: a plain line for each thing it says ("Library on this
    /// Mac", "Canvas synced at 10:18").</summary>
    public string[] StatusLines => Status.Split(" · ", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    partial void OnStatusChanged(string value) => OnPropertyChanged(nameof(StatusLines));
    [ObservableProperty] public partial bool StatusGood { get; set; } = true;
    [ObservableProperty] public partial string SearchText { get; set; } = "";
    /// <summary>Draw the window's own buttons (screenshots; a real window has the system's).</summary>
    [ObservableProperty] public partial bool DrawChrome { get; set; }
    /// <summary>The library can't be read (not set up, away, an older one): what the page says instead.</summary>
    [ObservableProperty] public partial string? Empty { get; set; }

    /// <summary>A class's lists on Canvas (Assignments, Modules, Announcements), while they're open: the list on the
    /// left, what's picked from it beside it.</summary>
    [ObservableProperty] public partial CanvasClassModel? CanvasClass { get; set; }
    /// <summary>The class whose Canvas lists are open, for the line over them: its swatch and name, a link back to its page.</summary>
    [ObservableProperty] public partial string CanvasOf { get; set; } = "";
    public string CanvasOfShown => ClassNames.Display(CanvasOf);
    [ObservableProperty] public partial IBrush CanvasDot { get; set; } = Brushes.Gray;
    partial void OnCanvasOfChanged(string value) => OnPropertyChanged(nameof(CanvasOfShown));
    /// <summary>An assignment open (from Due's calendar, a class's rail, or its Canvas lists); null shows the note.</summary>
    [ObservableProperty] public partial AssignmentModel? Assignment { get; set; }
    /// <summary>A Canvas page or announcement open beside its list; null shows the note.</summary>
    [ObservableProperty] public partial CanvasReaderModel? Reader { get; set; }
    /// <summary>The open note's notes, with "Rewrite"; null while the notes aren't written yet.</summary>
    [ObservableProperty] public partial AiNotesModel? Notes { get; set; }
    /// <summary>The ask bar under the open note, with its engine picker.</summary>
    [ObservableProperty] public partial AiAskModel? Ask { get; set; }
    /// <summary>What a diagram in the open note's notes can do with the page: ask about a box, show the transcript or
    /// play the recording at a moment (the page's views hand it to their diagrams).</summary>
    public Controls.Rich.IDiagramHost? Diagrams { get; set; }
    /// <summary>What's attached to the open note (files dropped on it land here); null with no note open.</summary>
    [ObservableProperty] public partial AttachmentsModel? LectureFiles { get; set; }
    public bool HasLectureFiles => LectureFiles is not null;

    partial void OnLectureFilesChanged(AttachmentsModel? oldValue, AttachmentsModel? newValue)
    {
        oldValue?.Dispose();
        OnPropertyChanged(nameof(HasLectureFiles));
    }

    /// <summary>The window is narrow (below <see cref="NarrowBelow"/>).</summary>
    [ObservableProperty] public partial bool Narrow { get; set; }
    /// <summary>Narrow, on a class's Canvas lists, and showing what was opened in the list's place.</summary>
    [ObservableProperty] public partial bool NarrowDetail { get; set; }

    /// <summary>A class's Canvas lists are open: the one page with a list column.</summary>
    public bool ShowCanvasLists => Overview is null && CanvasClass is not null;
    public bool ShowAssignment => Overview is null && Assignment is not null;
    public bool ShowReader => Overview is null && Assignment is null && Reader is not null;
    /// <summary>The note's page, with its toolbar and ask bar.</summary>
    public bool ShowNotePage => Overview is null && Assignment is null && Reader is null && !ShowCanvasLists && Note is not null;
    /// <summary>Canvas's lists with nothing picked from them yet: the page beside them says to pick.</summary>
    public bool ShowCanvasHint => ShowCanvasLists && Assignment is null && Reader is null;
    /// <summary>Move, share and delete: over a note that's open and isn't still being recorded.</summary>
    public bool ShowLectureTools => ShowNotePage && Note is { CanMove: true };
    /// <summary>Open in Canvas, over an assignment.</summary>
    public bool ShowAssignmentTools => ShowAssignment;
    public bool HasNotes => Notes is not null;
    public bool HasAsk => Ask is not null && ShowNotePage;
    /// <summary>An answer to show above the ask bar: the page gives it its own room, and the notes end above it.</summary>
    public bool HasAnswer => HasAsk && Ask?.HasLatest == true;
    /// <summary>The library's own word on why there's nothing to show, when nothing else is.</summary>
    public bool ShowEmpty => Overview is null && Note is null && Assignment is null && Reader is null && !ShowCanvasLists && !string.IsNullOrEmpty(Empty);

    /// <summary>The Canvas lists' column: 360 (380 on Windows), and the whole pane when it's narrow.</summary>
    public double ListWidth => Narrow ? double.NaN : Skin.Current == SkinKind.Win ? 380 : 360;
    public bool ShowListColumn => ShowCanvasLists && (!Narrow || !NarrowDetail);
    public bool ShowDetailColumn => Overview is null && (!ShowCanvasLists || !Narrow || NarrowDetail);
    /// <summary>Where the page sits: beside the list, or in its place when there's none (or no room for both).</summary>
    public int DetailColumn => ShowCanvasLists && !Narrow ? 2 : 1;
    public int ColumnSpan => ShowCanvasLists && !Narrow ? 1 : 2;

    public LibraryModel()
    {
        Classes.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(Due));
            OnPropertyChanged(nameof(HasDue));
        };
    }

    /// <summary>The sidebar's counts as the library says them now, the list itself left as it is: what's selected stays
    /// selected. A note filed while the window is open changes a folder's count without anything else being reloaded.</summary>
    public void ShowCounts(IEnumerable<(string Name, int Lectures)> classes, int unsorted)
    {
        foreach (var (name, lectures) in classes)
            if (Classes.FirstOrDefault(c => c.Name == name && !c.IsDue && !c.IsHome) is { } item) item.Count = lectures;
        UnsortedCount = unsorted;
    }

    partial void OnAssignmentChanged(AssignmentModel? value) => PageChanged();
    partial void OnReaderChanged(CanvasReaderModel? value) => PageChanged();
    partial void OnEmptyChanged(string? value) => PageChanged();
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

    /// <summary>What the pane shows changed: every "is this showing" follows.</summary>
    void PageChanged()
    {
        OnPropertyChanged(nameof(ShowOverview));
        OnPropertyChanged(nameof(ShowCanvasLists));
        OnPropertyChanged(nameof(ShowCanvasHint));
        OnPropertyChanged(nameof(ShowAssignment));
        OnPropertyChanged(nameof(ShowAssignmentTools));
        OnPropertyChanged(nameof(ShowReader));
        OnPropertyChanged(nameof(ShowNotePage));
        OnPropertyChanged(nameof(ShowLectureTools));
        OnPropertyChanged(nameof(ShowEmpty));
        OnPropertyChanged(nameof(HasAsk));
        OnPropertyChanged(nameof(HasAnswer));
        OnPropertyChanged(nameof(ListWidth));
        OnPropertyChanged(nameof(ShowListColumn));
        OnPropertyChanged(nameof(ShowDetailColumn));
        OnPropertyChanged(nameof(DetailColumn));
        OnPropertyChanged(nameof(ColumnSpan));
    }

    partial void OnOverviewChanged(OverviewModel? oldValue, OverviewModel? newValue)
    {
        if (oldValue?.Files is { } files && !ReferenceEquals(files, newValue?.Files)) files.Dispose();
        if (newValue is not null)
        {
            newValue.Narrow = Narrow;
            // The ask for a tip shows on Home, in the page's own flow.
            newValue.Asking = newValue.IsHome ? Support : null;
        }
        PageChanged();
    }

    partial void OnNarrowChanged(bool value)
    {
        if (Overview is { } o) o.Narrow = value;
        if (!value) NarrowDetail = false;
        PageChanged();
    }

    partial void OnNarrowDetailChanged(bool value) => PageChanged();

    partial void OnCanvasClassChanged(CanvasClassModel? value)
    {
        if (value is not null)
        {
            // The library window draws the class's name and the switcher itself, over the list.
            value.Layout = ClassLayout.Tabs;
            value.Embedded = true;
        }
        PageChanged();
    }

    /// <summary>Shows what the student opened from a class's Canvas lists: in a narrow window it takes the list's
    /// place until they go back.</summary>
    public void Opened()
    {
        if (Narrow && ShowCanvasLists) NarrowDetail = true;
    }

    [RelayCommand] void Back() => NarrowDetail = false;

    public bool HasNote => Note is not null;
    public bool NoNote => Note is null;
    public bool HasEmpty => !string.IsNullOrEmpty(Empty);

    partial void OnNoteChanged(NoteModel? value)
    {
        if (LectureFiles is { } files && files.LectureId != value?.Id) LectureFiles = null; // another note's files don't stay
        OnPropertyChanged(nameof(HasNote));
        OnPropertyChanged(nameof(NoNote));
        PageChanged();
    }

    // --- recording, as the window shows it -------------------------------------------------------------------------

    /// <summary>Something is recording: the sidebar says so over Coming up, one click from its page.</summary>
    [ObservableProperty] public partial RecorderModel? Recording { get; set; }
    public bool IsRecording => Recording is not null;
    partial void OnRecordingChanged(RecorderModel? value) => OnPropertyChanged(nameof(IsRecording));
    /// <summary>Opens the page of what's recording.</summary>
    public Action? OnOpenRecording { get; set; }
    [RelayCommand] void OpenRecording() => OnOpenRecording?.Invoke();

    /// <summary>A question over the window, while it's asked ("Stop recording Data Mining and start Capstone team
    /// sync?"); null when nothing is.</summary>
    [ObservableProperty] public partial ConfirmAsk? Confirming { get; set; }
    public bool AskingConfirm => Confirming is not null;
    partial void OnConfirmingChanged(ConfirmAsk? value) => OnPropertyChanged(nameof(AskingConfirm));
    [RelayCommand] void CancelConfirm() => Confirming = null;
    [RelayCommand]
    void Confirm()
    {
        if (Confirming is not { } ask) return;
        Confirming = null;
        ask.Act();
    }

    // --- the ask for a tip ------------------------------------------------------------------------------------------

    /// <summary>The one gentle ask for a tip, on Home (<see cref="SupportAsk"/> says when); null when it isn't asking.</summary>
    [ObservableProperty] public partial SupportAskModel? Support { get; set; }
    public bool HasSupport => Support is not null;
    /// <summary>What the student answered: the host saves it, and opens the Ko-fi page for a tip.</summary>
    public Action<SupportAnswer>? OnSupportAnswer { get; set; }

    partial void OnSupportChanged(SupportAskModel? value)
    {
        OnPropertyChanged(nameof(HasSupport));
        if (Overview is { IsHome: true } home) home.Asking = value;
    }

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
    partial void OnDeletedChanged(LectureDeletion? value)
    {
        OnPropertyChanged(nameof(HasDeleted));
        ToastChanged();
    }

    /// <summary>Asks before deleting a note: a row's (its menu) or, with none, the open one (the toolbar).</summary>
    [RelayCommand]
    void Delete(NoteItem? row)
    {
        if (row is not null) Deleting = new LectureDeletion(row.Id, row.Title, row.Folder, row.Noun);
        else if (Note is { CanMove: true } n) Deleting = new LectureDeletion(n.Id, n.Title, n.ClassName, n.Noun);
    }

    [RelayCommand]
    void CancelDelete() => Deleting = null;

    /// <summary>Leaves a note's page for where the student came from (it was deleted): the host goes back.</summary>
    public Action? OnLeaveNote { get; set; }

    /// <summary>Delete, confirmed: the note leaves the page at once (its own page gives way to where the student came
    /// from), the library deletes it, and "Deleted · Undo" shows for a few seconds. If the library refuses, the host
    /// reads the page again.</summary>
    [RelayCommand(AllowConcurrentExecutions = true)]
    async Task ConfirmDelete()
    {
        if (Deleting is not { } d) return;
        Deleting = null;
        bool open = Remove(d);
        Moved = null;
        Deleted = d;
        if (open) OnLeaveNote?.Invoke();
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

    /// <summary>Takes a note out of the page's list and its folder's count, and closes it if it's open (true then).</summary>
    public bool Remove(LectureDeletion d)
    {
        Overview?.Remove(d.Id);
        if (d.ClassName.Length == 0 || d.ClassName == Configs.Unsorted)
        {
            if (UnsortedCount > 0) UnsortedCount--;
        }
        else if (Classes.FirstOrDefault(c => c.Name == d.ClassName && !c.IsDue) is { Count: > 0 } cls) cls.Count--;
        bool open = Note?.Id == d.Id;
        if (open) Note = null;
        return open;
    }

    // --- moving a note -----------------------------------------------------------------------------------------------

    /// <summary>The note just moved, while "Moved to Machine Learning. Undo" shows.</summary>
    [ObservableProperty] public partial NoteMove? Moved { get; set; }
    /// <summary>Moves it back (the host calls the library and reads the page again).</summary>
    public Func<NoteMove, Task>? OnUndoMove { get; set; }
    partial void OnMovedChanged(NoteMove? value) => ToastChanged();

    /// <summary>A note was moved: the toast says where, with Undo, for a few seconds.</summary>
    public void SayMoved(NoteMove move)
    {
        Deleted = null;
        Moved = move;
        _ = HideMovedAfterAWhile(move);
    }

    async Task HideMovedAfterAWhile(NoteMove move)
    {
        await Task.Delay(UndoShows);
        if (Moved == move) Moved = null;
    }

    /// <summary>The toast at the page's foot: what just happened, and Undo.</summary>
    public string ToastSaid => Moved?.Said ?? Deleted?.Said ?? "";
    public bool HasToast => Moved is not null || Deleted is not null;
    void ToastChanged()
    {
        OnPropertyChanged(nameof(ToastSaid));
        OnPropertyChanged(nameof(HasToast));
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    async Task UndoToast()
    {
        if (Moved is { } move)
        {
            Moved = null;
            if (OnUndoMove is not null) await OnUndoMove(move);
        }
        else await Undo();
    }

    /// <summary>The folder picker for the open note (the host builds it): the breadcrumb's chevron and the toolbar's
    /// Move open the same one.</summary>
    public Func<FolderPickerModel?>? PickerForNote { get; set; }

    public Action<ClassItem>? OnClass { get; set; }
    public Action? OnExport { get; set; }
    public Action? OnSearch { get; set; }
    public Action? OnSettings { get; set; }
    public Action? OnMore { get; set; }

    /// <summary>The sidebar is folded away, leaving the page the whole window.</summary>
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

    /// <summary>A folder's page, from wherever its name is a link: a note's breadcrumb, the line over its Canvas lists.</summary>
    public Action<string>? OnFolder { get; set; }
    [RelayCommand] void OpenFolder(string? name)
    {
        if (!string.IsNullOrEmpty(name) && name != Configs.Unsorted) OnFolder?.Invoke(name);
    }

    [RelayCommand] void ToggleSidebar() => SidebarHidden = !SidebarHidden;
    [RelayCommand] void GoBack() => OnGoBack?.Invoke();
    [RelayCommand] void GoForward() => OnGoForward?.Invoke();

    [RelayCommand] void PickClass(ClassItem c) => OnClass?.Invoke(c);
    [RelayCommand] void Export() => OnExport?.Invoke();
    [RelayCommand] void Search() => OnSearch?.Invoke();
    [RelayCommand] void Settings() => OnSettings?.Invoke();

    /// <summary>The "+" beside Classes in the sidebar: where a folder is added (Settings' Classes).</summary>
    public Action? OnAddClass { get; set; }
    public bool CanAddClass => OnAddClass is not null;
    [RelayCommand] void AddClass() => OnAddClass?.Invoke();
    [RelayCommand] void More() => OnMore?.Invoke();
}
