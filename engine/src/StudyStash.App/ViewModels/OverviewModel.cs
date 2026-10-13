using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StudyStash.App.Services;

namespace StudyStash.App.ViewModels;

/// <summary>
/// A note in a list (Home's, a folder's): when it was ("Today", "Wed 7 Oct"), its title, the folder it's in (on Home,
/// marked with that folder's highlighter and a button that moves it; an inbox and "Unsorted" when it's in none), the
/// note in a sentence or two, and how long it ran ("13 min").
/// </summary>
public sealed partial class NoteItem : ObservableObject
{
    public string Id { get; init; } = "";
    public string Title { get; init; } = "";
    public string When { get; init; } = "";
    public string Length { get; init; } = "";
    public string Summary { get; init; } = "";
    /// <summary>Its notes are still being written: the summary's place says so, quietly.</summary>
    public bool Pending { get; init; }
    /// <summary>The library's own date for it, as it came: what the next page of the list is asked for after.</summary>
    public string Date { get; init; } = "";
    /// <summary>The folder as the library knows it; "" (or Unsorted) for a note in none.</summary>
    [ObservableProperty] public partial string Folder { get; set; } = "";
    [ObservableProperty] public partial IBrush Dot { get; set; } = Brushes.Gray;
    /// <summary>Lecture, meeting or recording, by the folder it's in.</summary>
    [ObservableProperty] public partial string Noun { get; set; } = Nouns.Lecture;
    /// <summary>The list names each note's folder (Home's does; a folder's own page doesn't).</summary>
    public bool ShowFolder { get; init; }
    /// <summary>The first row under a heading: no hairline over it.</summary>
    public bool First { get; set; }
    /// <summary>Its folder picker is open: the label keeps its chevron and its fill.</summary>
    [ObservableProperty] public partial bool Picking { get; set; }
    public Action? Open { get; init; }

    public bool IsUnsorted => Folder.Length == 0 || Folder == Core.Configs.Unsorted;
    public string FolderShown => IsUnsorted ? "Unsorted" : ClassNames.Display(Folder);
    public bool ShowHighlight => ShowFolder && !IsUnsorted;
    public bool ShowUnsorted => ShowFolder && IsUnsorted;
    public bool HasSummary => Summary.Length > 0;
    public bool HasLength => Length.Length > 0;
    /// <summary>What a screen reader says for the row: the title alone doesn't say what it is or when.</summary>
    public string Said => string.Join(", ", new[] { Title, ShowFolder ? FolderShown : "", $"{Noun} from {When}".Trim(), Length }.Where(w => w.Length > 0));
    /// <summary>The folder label's own name: where it is, and what a press does.</summary>
    public string PickerSaid => $"In {FolderShown}. Change folder";

    partial void OnFolderChanged(string value)
    {
        OnPropertyChanged(nameof(IsUnsorted));
        OnPropertyChanged(nameof(FolderShown));
        OnPropertyChanged(nameof(ShowHighlight));
        OnPropertyChanged(nameof(ShowUnsorted));
        OnPropertyChanged(nameof(Said));
        OnPropertyChanged(nameof(PickerSaid));
    }

    partial void OnNounChanged(string value) => OnPropertyChanged(nameof(Said));
}

/// <summary>A folder's notes under a heading: "This week", "Last week", "September".</summary>
public sealed record NoteGroup(string Label, bool First, IReadOnlyList<NoteItem> Notes);

/// <summary>
/// An event in Home's Up next: when it starts ("1:45 PM" over "In 11 minutes", "Today" or "Tomorrow"), what it is,
/// and the folder a recording of it goes in. The whole row is a button that starts recording it. An event that's
/// just its class ("Data Mining") shows the name once, as the highlight.
/// </summary>
public sealed record UpNext(string Id, string Time, string Day, bool Soon, string Title, string Folder, IBrush Dot, string Said, Action Record)
{
    public bool HasFolder => Folder.Length > 0;
    public string FolderShown => ClassNames.Display(Folder);
    /// <summary>The event is named for its class and nothing more.</summary>
    public bool TitleIsFolder => HasFolder && (string.Equals(Title.Trim(), Folder, StringComparison.OrdinalIgnoreCase)
        || string.Equals(Title.Trim(), FolderShown, StringComparison.OrdinalIgnoreCase));
    public bool ShowTitle => !TitleIsFolder;
    public bool ShowFolder => HasFolder && !TitleIsFolder;
}

/// <summary>A row of Home's notes filter: All notes, Unsorted (how many), then every folder under its group's name.
/// All and Unsorted change the list where it is; a folder goes to its page.</summary>
public sealed record NotesFilter(string Kind, string Name, IBrush? Dot, int Count, bool Checked, IRelayCommand? Pick)
{
    public const string All = "all", Unsorted = "unsorted", Label = "label", Folder = "folder", Rule = "rule";
    public bool IsLabel => Kind == Label;
    public bool IsRule => Kind == Rule;
    public bool IsRow => Kind is All or Unsorted or Folder;
    public bool IsUnsorted => Kind == Unsorted;
    public bool IsFolder => Kind == Folder;
    public bool ShowCount => IsUnsorted && Count > 0;
}

/// <summary>Something due in a class, in its page's rail: its name, then "Sun 18 Oct, in 6 days" (heavier within a
/// week; "Was due Mon 5 Oct" in the late red once it's missing).</summary>
public sealed record RailDue(string Title, string When, bool Soon, bool Late, bool First, string Said, Action Open);

/// <summary>Something to hand in: its name (marked with its class's highlighter), when it's due ("11:59 PM", or "Was
/// due Mon 5" once it's late, or "Handed in"), and the class it's for.</summary>
public sealed record OverviewDue(string Class, string Id, string Title, string When, bool Late, bool Done, string ClassLabel, IBrush Dot, Action Open)
{
    /// <summary>What a screen reader says for it: the name alone doesn't say whose it is or when.</summary>
    public string Said => string.Join(", ", new[] { Title, ClassLabel, When }.Where(w => w.Length > 0));
    public bool HasClassLabel => ClassLabel.Length > 0;
    /// <summary>Opens it (where a row has no page's command in reach: a list in a menu).</summary>
    public IRelayCommand Pick => new RelayCommand(Open);
}

/// <summary>A class meeting in the week: when it starts ("9:00"), the class by its short name with its swatch, and
/// whether a lecture was recorded in it.</summary>
public sealed record WeekMeeting(string Time, string Class, IBrush Dot, bool Recorded);

/// <summary>One column of the week: a day (or, first, what's late), its class meetings and what's due on it.</summary>
public sealed class WeekDay : Controls.IWeighted
{
    /// <summary>"Mon", "Today", or "Late".</summary>
    public string Name { get; init; } = "";
    /// <summary>The day of the month; the Late column has none.</summary>
    public string Number { get; init; } = "";
    public bool IsToday { get; init; }
    public bool IsLate { get; init; }
    /// <summary>The column's share of the row: a weekday 1, Saturday and Sunday 0.8, Late 1.15.</summary>
    public double Weight { get; init; } = 1;
    /// <summary>The hairline on its left: every column but the first, today's and the one after today's.</summary>
    public bool Ruled { get; set; }
    public List<WeekMeeting> Meetings { get; } = [];
    public List<OverviewDue> Deadlines { get; } = [];
    /// <summary>"+2 more", when the day has more due than the column shows.</summary>
    public string More { get; set; } = "";
    /// <summary>The ones "+2 more" stands for: a click lists them.</summary>
    public List<OverviewDue> Rest { get; } = [];
    public bool HasMeetings => Meetings.Count > 0;
    public bool HasDeadlines => Deadlines.Count > 0;
    public bool HasMore => More.Length > 0;
    /// <summary>The hairline between the day's meetings and what's due.</summary>
    public bool Divided => HasMeetings && HasDeadlines;
}

/// <summary>A way further into a class from its page: one of its lists on Canvas.</summary>
public sealed record OverviewLink(string Label, string Detail, Action Open);

/// <summary>A folder linked to a class: its name, where it is, whether it's still there, and what its buttons do.</summary>
public sealed record LinkedFolder(string Name, string Path, bool Here, Action Open, Action Unlink)
{
    public string Detail => Here ? Path : $"{Path} (not found)";
}

/// <summary>What a folder's page is made from: the folder, the sidebar group it's in ("" for Classes), how many notes
/// it holds, and (when it's a class linked to Canvas) its course.</summary>
public sealed record ClassHomeFacts(string Name, IBrush Dot, int Lectures, string? Code, string? CourseTitle, string? Description,
    CanvasApi.Counts? Canvas, string Group = "");

/// <summary>
/// The pages that fill the library window beside the sidebar. Home: today's date, a sentence or two on what's most
/// pressing, Up next (the rest of today's and tomorrow's events, each a button that records it) and every note, newest
/// first. A folder's page: its name underlined in its highlighter, its lectures (or meetings) by week, and in a rail
/// what's due, its lists on Canvas and when it next meets. Due: a calendar of what's to hand in. Built from what the
/// shell already has; nothing here fetches.
/// </summary>
public sealed partial class OverviewModel : ObservableObject
{
    /// <summary>How many notes Home's list shows at a time; scrolling to its end brings the next page.</summary>
    public const int PageSize = 20;
    /// <summary>How many events Up next shows.</summary>
    public const int UpNextRows = 4;
    /// <summary>How many deadlines a folder's rail shows.</summary>
    public const int RailRows = 3;
    /// <summary>How many deadlines a day of the week's planner shows; one more than this and the rest fold into "+2 more".</summary>
    public const int DayRows = 4;
    /// <summary>An event this close to starting says "In 11 minutes" rather than its day.</summary>
    public static readonly TimeSpan Soon = TimeSpan.FromMinutes(15);

    public bool IsHome { get; init; }
    /// <summary>Due: the calendar of what's to hand in.</summary>
    public bool IsDuePage { get; init; }
    /// <summary>A folder's page (<see cref="ClassName"/>).</summary>
    public bool IsFolder => !IsHome && !IsDuePage;
    /// <summary>The folder as the library knows it (what its notes are filed under).</summary>
    public string ClassName { get; init; } = "";
    /// <summary>The page's title: today's date on Home ("Monday, October 12"), "Due", or the folder's name.</summary>
    public string Title { get; init; } = "";
    /// <summary>A sentence or two under the title, the most pressing thing first.</summary>
    public string Lede { get; init; } = "";
    public bool HasLede => Lede.Length > 0;
    public IBrush Dot { get; init; } = Brushes.Gray;
    /// <summary>What this folder's recordings are called: lectures in a class, meetings in a club or at work.</summary>
    public string Noun { get; init; } = Nouns.Lecture;

    // --- Home: Up next ----------------------------------------------------------------------------------------------

    public ObservableCollection<UpNext> UpNext { get; } = [];
    /// <summary>The student has calendars: Up next shows, even with nothing in it.</summary>
    public bool ShowUpNext { get; init; }
    public bool NoUpNext => ShowUpNext && UpNext.Count == 0;
    public string UpNextEmpty => "Nothing on your calendar for the rest of today or tomorrow.";
    [RelayCommand] static void RecordEvent(UpNext e) => e.Record();

    // --- the notes --------------------------------------------------------------------------------------------------

    /// <summary>Home's notes, newest first.</summary>
    public ObservableCollection<NoteItem> Notes { get; } = [];
    /// <summary>A folder's notes by week.</summary>
    public ObservableCollection<NoteGroup> Groups { get; } = [];
    public string NotesHeading => IsHome ? "Notes" : Nouns.Section(Noun);
    public string NotesEmpty { get; set; } = "";
    public bool NoNotes => IsHome ? Notes.Count == 0 : Groups.Count == 0;
    public bool ShowNotes => !IsDuePage;

    /// <summary>Home's filter: every note, or only the ones in no folder yet.</summary>
    public bool OnlyUnsorted { get; init; }
    public string FilterName => OnlyUnsorted ? "Unsorted" : "All notes";
    public ObservableCollection<NotesFilter> Filters { get; } = [];
    /// <summary>The filter was changed to every note (false) or the unsorted ones (true): the shell reads the list again.</summary>
    public Action<bool>? OnFilter { get; set; }

    [RelayCommand]
    void PickFilter(NotesFilter f)
    {
        if (f.Kind == NotesFilter.All) OnFilter?.Invoke(false);
        else if (f.Kind == NotesFilter.Unsorted) OnFilter?.Invoke(true);
        else f.Pick?.Execute(null);
    }

    /// <summary>The list could go on: the next page is a scroll (or a click) away.</summary>
    [ObservableProperty] public partial bool CanLoadMore { get; set; }
    [ObservableProperty] public partial bool LoadingMore { get; set; }
    /// <summary>Brings the next page of notes (the shell asks the library and adds them).</summary>
    public Func<Task>? OnMore { get; set; }

    [RelayCommand(AllowConcurrentExecutions = false)]
    async Task LoadMore()
    {
        if (!CanLoadMore || LoadingMore || OnMore is null) return;
        LoadingMore = true;
        try
        {
            await OnMore();
        }
        finally
        {
            LoadingMore = false;
        }
    }

    /// <summary>More of Home's notes, under the ones showing.</summary>
    public void Add(IEnumerable<NoteItem> more, bool canLoadMore)
    {
        foreach (var n in more)
        {
            n.First = Notes.Count == 0;
            Notes.Add(n);
        }
        CanLoadMore = canLoadMore;
        OnPropertyChanged(nameof(NoNotes));
    }

    /// <summary>Takes a note off the page (it was deleted, or moved out of what the page lists).</summary>
    public void Remove(string id)
    {
        if (Notes.FirstOrDefault(n => n.Id == id) is { } row)
        {
            Notes.Remove(row);
            if (Notes.Count > 0) Notes[0].First = true;
        }
        for (int i = Groups.Count - 1; i >= 0; i--)
        {
            if (Groups[i].Notes.All(n => n.Id != id)) continue;
            var left = Groups[i].Notes.Where(n => n.Id != id).ToList();
            if (left.Count == 0) Groups.RemoveAt(i);
            else
            {
                left[0].First = true;
                Groups[i] = Groups[i] with { Notes = left };
            }
        }
        if (Groups.Count > 0 && !Groups[0].First) Groups[0] = Groups[0] with { First = true };
        OnPropertyChanged(nameof(NoNotes));
    }

    /// <summary>The folder picker for a note in the list (the shell builds it: it knows the folders and does the move).</summary>
    public Func<NoteItem, FolderPickerModel?>? PickerFor { get; set; }
    /// <summary>Asks before deleting a note from its row's menu.</summary>
    public Action<NoteItem>? OnDelete { get; set; }
    [RelayCommand] static void OpenNote(NoteItem n) => n.Open?.Invoke();
    [RelayCommand] void DeleteNote(NoteItem n) => OnDelete?.Invoke(n);

    // --- a folder's rail --------------------------------------------------------------------------------------------

    public ObservableCollection<RailDue> Due { get; } = [];
    public bool HasDue => Due.Count > 0;
    /// <summary>"See these on the calendar": Due, for this class alone.</summary>
    public Action? OnCalendar { get; init; }
    [RelayCommand] void SeeCalendar() => OnCalendar?.Invoke();
    [RelayCommand] static void OpenRailDue(RailDue d) => d.Open();
    /// <summary>A Canvas class's lists: Assignments, Modules, Announcements.</summary>
    public ObservableCollection<OverviewLink> Links { get; } = [];
    public bool HasLinks => Links.Count > 0;
    [RelayCommand] static void OpenLink(OverviewLink l) => l.Open();
    /// <summary>"Next class" or "Next meeting", and when: "Wednesday at 9:30 AM".</summary>
    public string NextHeading => $"Next {(Noun == Nouns.Lecture ? "class" : Noun)}";
    public string NextText { get; init; } = "";
    public bool HasNext => NextText.Length > 0;
    /// <summary>The rail shows when there's something to put in it.</summary>
    public bool HasRail => IsFolder && (HasDue || HasLinks || HasNext || HasFiles || ShowFolders);

    /// <summary>Canvas needs something (signing in, its extension): said over the calendar, with what to do.</summary>
    [ObservableProperty] public partial CanvasStatusModel? CanvasStatus { get; set; }
    public bool HasCanvasStatus => CanvasStatus is not null;
    partial void OnCanvasStatusChanged(CanvasStatusModel? value) => OnPropertyChanged(nameof(HasCanvasStatus));
    /// <summary>Due's calendar.</summary>
    public DueCalendarModel? Calendar { get; init; }

    // --- Record -----------------------------------------------------------------------------------------------------

    /// <summary>Record from here: Home lets the library sort the recording (or files it with the event on now), a
    /// folder's page records into that folder. While something records the button shows it (which folder, the level,
    /// the time) and a click opens its page; only its stop square (<see cref="OnStop"/>) stops it.</summary>
    public Action? OnRecord { get; set; }
    public Action? OnStop { get; set; }
    [RelayCommand] void Stop() => OnStop?.Invoke();
    public string StopTip => $"Stop recording ({RecordKeys})";
    public bool CanRecord => OnRecord is not null;
    [ObservableProperty] public partial bool Recording { get; set; }
    /// <summary>The recording under way: the button reads its class, level, time and whether it's paused from it.</summary>
    [ObservableProperty] public partial RecorderModel? Recorder { get; set; }
    public bool Idle => !Recording;
    public bool Paused => Recording && Recorder?.IsPaused == true;
    public bool Running => Recording && !Paused;
    /// <summary>The button's word: "Record"; recording, the folder by its short name; paused, "Paused".</summary>
    public string RecordWords => !Recording ? "Record" : Paused ? "Paused" : ClassNames.Short(Recorder?.DisplayClass ?? "Recording");
    public string Elapsed => Recorder?.Elapsed ?? "";
    /// <summary>The keys that start and stop a recording from the library window: ⌘R, or Ctrl+R on Windows. (The
    /// shortcut in Settings does the same from any app.)</summary>
    public string RecordKeys { get; init; } = Skin.Current == SkinKind.Mac ? "⌘R" : "Ctrl+R";
    /// <summary>Where the recording goes, for the button's tip and for a screen reader (which hears the time too).</summary>
    public string RecordTip => Recording ? $"{(Paused ? "Paused" : "Recording")} {Elapsed}: open it" : IsHome ? $"Start recording ({RecordKeys})" : $"Record a {Noun} into {Title} ({RecordKeys})";
    partial void OnRecordingChanged(bool value) => RecordChanged();
    partial void OnRecorderChanged(RecorderModel? oldValue, RecorderModel? newValue)
    {
        if (oldValue is not null) oldValue.PropertyChanged -= RecorderSaid;
        if (newValue is not null) newValue.PropertyChanged += RecorderSaid;
        RecordChanged();
    }
    void RecorderSaid(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(RecorderModel.Elapsed))
        {
            OnPropertyChanged(nameof(Elapsed));
            OnPropertyChanged(nameof(RecordTip));
        }
        else if (e.PropertyName is nameof(RecorderModel.IsPaused) or nameof(RecorderModel.ClassName)) RecordChanged();
    }
    void RecordChanged()
    {
        OnPropertyChanged(nameof(Idle));
        OnPropertyChanged(nameof(Paused));
        OnPropertyChanged(nameof(Running));
        OnPropertyChanged(nameof(RecordWords));
        OnPropertyChanged(nameof(Elapsed));
        OnPropertyChanged(nameof(RecordTip));
    }
    [RelayCommand] void Record() => OnRecord?.Invoke();

    /// <summary>Home's "Buy us a coffee": Study Stash is free, and a tip for the two students who build it goes to
    /// their Ko-fi page (<see cref="Services.SupportAsk"/>). Only on Home, a quiet line at the foot of the page.</summary>
    public Action? OnSupport { get; set; }
    public bool CanSupport => IsHome && OnSupport is not null;
    [RelayCommand] void Support() => OnSupport?.Invoke();

    /// <summary>The one gentle ask for a tip, on Home when it's due (the library window sets it).</summary>
    [ObservableProperty] public partial SupportAskModel? Asking { get; set; }
    public bool HasAsking => Asking is not null;
    partial void OnAskingChanged(SupportAskModel? value) => OnPropertyChanged(nameof(HasAsking));

    /// <summary>Ask about every note (Home) or this folder's (its page), floating at the foot of the page as under a note.</summary>
    [ObservableProperty] public partial AiAskModel? Ask { get; set; }
    public bool HasAsk => Ask is not null;
    public bool HasAnswer => Ask?.HasLatest == true;
    partial void OnAskChanged(AiAskModel? oldValue, AiAskModel? newValue)
    {
        if (oldValue is not null) oldValue.PropertyChanged -= AskChanged;
        if (newValue is not null) newValue.PropertyChanged += AskChanged;
        OnPropertyChanged(nameof(HasAsk));
        OnPropertyChanged(nameof(HasAnswer));
    }
    void AskChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AiAskModel.HasLatest)) OnPropertyChanged(nameof(HasAnswer));
    }

    /// <summary>Folders on the computer that go with the class (a project's repo, say), linked rather than copied in.</summary>
    public ObservableCollection<LinkedFolder> Folders { get; } = [];
    public bool HasFolders => Folders.Count > 0;
    /// <summary>Link a folder: set on a folder's page when the library is on this computer (its folders are this one's).</summary>
    public Action? OnLinkFolder { get; set; }
    public bool CanLinkFolders => OnLinkFolder is not null;
    /// <summary>The rail's Linked folders: when there are some, or one can be linked.</summary>
    public bool ShowFolders => IsFolder && (HasFolders || CanLinkFolders);
    [RelayCommand] void LinkFolder() => OnLinkFolder?.Invoke();
    [RelayCommand] static void OpenFolder(LinkedFolder f) => f.Open();
    [RelayCommand] static void UnlinkFolder(LinkedFolder f) => f.Unlink();

    /// <summary>Shows the class's linked folders (and whether more can be linked here).</summary>
    public void SetFolders(IEnumerable<LinkedFolder> folders, Action? link)
    {
        Folders.Clear();
        foreach (var f in folders) Folders.Add(f);
        OnLinkFolder = link;
        OnPropertyChanged(nameof(HasFolders));
        OnPropertyChanged(nameof(CanLinkFolders));
        OnPropertyChanged(nameof(ShowFolders));
        OnPropertyChanged(nameof(HasRail));
    }

    /// <summary>What's attached to the folder (its page only): drop files on the page, or Attach.</summary>
    [ObservableProperty] public partial AttachmentsModel? Files { get; set; }
    /// <summary>Attach, beside Record: wherever files can be attached.</summary>
    public bool CanAttach => Files is not null;
    /// <summary>The rail lists what's attached once something is.</summary>
    public bool HasFiles => Files?.ShowHeader == true;
    partial void OnFilesChanged(AttachmentsModel? oldValue, AttachmentsModel? newValue)
    {
        if (oldValue is not null) oldValue.PropertyChanged -= FilesChanged;
        if (newValue is not null) newValue.PropertyChanged += FilesChanged;
        FilesChanged(null, new System.ComponentModel.PropertyChangedEventArgs(nameof(AttachmentsModel.ShowHeader)));
    }
    void FilesChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(AttachmentsModel.ShowHeader)) return;
        OnPropertyChanged(nameof(CanAttach));
        OnPropertyChanged(nameof(HasFiles));
        OnPropertyChanged(nameof(HasRail));
    }

    /// <summary>The window is narrow: the page's margins draw in, the buttons go under the title, and a folder's rail
    /// goes under its notes.</summary>
    [ObservableProperty] public partial bool Narrow { get; set; }
    /// <summary>The page's buttons (Record, Attach) sit beside the title (wide) or under it (narrow), never over it.</summary>
    public int ActionsColumn => Narrow ? 0 : 1;
    public int ActionsRow => Narrow ? 1 : 0;
    public int RailColumn => Narrow ? 0 : 1;
    public int RailRow => Narrow ? 1 : 0;
    partial void OnNarrowChanged(bool value)
    {
        OnPropertyChanged(nameof(ActionsColumn));
        OnPropertyChanged(nameof(ActionsRow));
        OnPropertyChanged(nameof(RailColumn));
        OnPropertyChanged(nameof(RailRow));
    }

    // --- building one -----------------------------------------------------------------------------------------------

    /// <summary>Everything the shell knows, for Home, Due or a folder's page.</summary>
    public sealed record Sources
    {
        public required DateTimeOffset Now { get; init; }
        public required TimeZoneInfo Zone { get; init; }
        /// <summary>Every folder, with its swatch and how many notes it holds.</summary>
        public IReadOnlyList<(string Name, IBrush Dot, int Lectures)> Classes { get; init; } = [];
        /// <summary>The sidebar group a folder is in: "" for Classes, else "Clubs", "Work" or one of the student's own.</summary>
        public Func<string, string> GroupOf { get; init; } = _ => "";
        /// <summary>Notes, newest first: every folder's for Home, the folder's own for its page.</summary>
        public IReadOnlyList<LectureFacts> Lectures { get; init; } = [];
        /// <summary>What's due (Canvas's Due list), or null without Canvas.</summary>
        public CanvasApi.DueResponse? Due { get; init; }
        /// <summary>The classes linked to Canvas.</summary>
        public IReadOnlyList<CanvasApi.ClassRow> Canvas { get; init; } = [];
        /// <summary>The events on the student's calendars, as far as the app keeps them (this week from Monday, and
        /// seven days ahead), each with the folder it's clearly for; null without calendars.</summary>
        public IReadOnlyList<Event>? Events { get; init; }
        public int Unsorted { get; init; }
        public int Writing { get; init; }
        public Action<string>? OpenClass { get; init; }
        /// <summary>Opens a note (its folder, its id).</summary>
        public Action<string, string>? OpenLecture { get; init; }
        public Action<string, string>? OpenAssignment { get; init; }
        /// <summary>Due's calendar, for every class (null) or one.</summary>
        public Action<string?>? OpenDue { get; init; }
        /// <summary>Starts recording an event (its id) into its folder.</summary>
        public Action<string>? RecordEvent { get; init; }
    }

    /// <summary>An event on a calendar: what, when, and the folder it's clearly for (null for none).</summary>
    public sealed record Event(string Id, string Title, DateTimeOffset Start, DateTimeOffset End, string? Class);

    /// <summary>One note as the library lists it.</summary>
    /// <param name="Status">Where a recording this computer has is on its way ("Recording now", "Writing notes…"), in
    /// the summary's place; null for a note the library has filed.</param>
    public sealed record LectureFacts(string Id, string Title, string Class, DateTimeOffset? Date, double? Seconds, string Summary, bool Writing, string DateText = "",
        string? Status = null);

    static DateTimeOffset Local(DateTimeOffset d, Sources s) => TimeZoneInfo.ConvertTime(d, s.Zone);

    static string Words(DateTimeOffset d, string format) => d.ToString(format, CultureInfo.InvariantCulture);

    /// <summary>A note's or a deadline's day: "Today", "Yesterday" (or "Tomorrow"), then "Wed 7 Oct" within the
    /// year, then "7 Oct 2025". Never "10/5/2026".</summary>
    public static string Day(DateTimeOffset when, DateTimeOffset now, TimeZoneInfo zone)
    {
        var d = TimeZoneInfo.ConvertTime(when, zone);
        var today = TimeZoneInfo.ConvertTime(now, zone).Date;
        int away = (d.Date - today).Days;
        return away switch
        {
            0 => "Today",
            -1 => "Yesterday",
            1 => "Tomorrow",
            _ => d.Year == today.Year ? Words(d, "ddd d MMM") : Words(d, "d MMM yyyy"),
        };
    }

    /// <summary>A day as its date, whatever today is: "Mon 12 Oct", and "12 Oct 2025" once it's another year.</summary>
    static string Date(DateTimeOffset when, Sources s)
    {
        var d = Local(when, s);
        return d.Year == Local(s.Now, s).Year ? Words(d, "ddd d MMM") : Words(d, "d MMM yyyy");
    }

    /// <summary>A time of day: "11:59 PM".</summary>
    public static string Time(DateTimeOffset when, TimeZoneInfo zone) => Words(TimeZoneInfo.ConvertTime(when, zone), "h:mm tt");

    /// <summary>The Monday of the week <paramref name="now"/> is in.</summary>
    static DateTime Monday(Sources s)
    {
        var today = Local(s.Now, s).Date;
        return today.AddDays(-(((int)today.DayOfWeek + 6) % 7));
    }

    static IEnumerable<CanvasApi.Item> StillToHandIn(CanvasApi.DueResponse? due) =>
        due is null ? [] : due.Groups.Where(g => g.Key != "handed_in").SelectMany(g => g.Items).Where(CanvasWords.ToHandIn);

    static IEnumerable<CanvasApi.Item> HandedIn(CanvasApi.DueResponse? due) =>
        due is null ? [] : due.Groups.Where(g => g.Key == "handed_in").SelectMany(g => g.Items);

    static bool Overdue(CanvasApi.Item i, DateTimeOffset now) => i.Missing || i.Status is "missing" or "overdue" or "past due" || i.DueAt < now;

    /// <summary>Late enough to be called missing: overdue, from a day before today (something due earlier today
    /// stays on today, with its time).</summary>
    static bool LateBefore(CanvasApi.Item i, Sources s) =>
        Overdue(i, s.Now) && (i.DueAt is not { } d || Local(d, s).Date < Local(s.Now, s).Date);

    /// <summary>A deadline in the week's planner: under its day it shows only the time; in the Late column, the day it was due.</summary>
    static OverviewDue DueRow(CanvasApi.Item i, Sources s, Func<string, IBrush> dot, bool classLabel, bool done = false)
    {
        bool late = !done && LateBefore(i, s);
        string when = done ? "Handed in"
            : i.DueAt is not { } d ? "No due date"
            : late ? $"Was due {Words(Local(d, s), Local(d, s).Year == Local(s.Now, s).Year ? "ddd d" : "d MMM yyyy")}"
            : Time(d, s.Zone);
        return new OverviewDue(i.Class, i.Id, i.Name, when, late, done, classLabel ? ClassNames.Display(i.Class) : "", dot(i.Class),
            () => s.OpenAssignment?.Invoke(i.Class, i.Id));
    }

    /// <summary>A note as a row of a list. Home's rows name their folder (and can be moved from there); a folder's don't.</summary>
    public static NoteItem Row(LectureFacts l, Sources s, Func<string, IBrush> dot, bool showFolder) => new()
    {
        // Home says "Today" and "Yesterday"; a folder's own list, under "This week", says the day itself.
        Id = l.Id, Title = l.Title, When = l.Date is not { } d ? "" : showFolder ? Day(d, s.Now, s.Zone) : Date(d, s), Length = l.Seconds is { } sec ? Core.TimedText.Length(sec) : "",
        Summary = l.Status ?? (l.Writing ? "Writing notes…" : PlainMath.Inline(l.Summary)), Pending = l.Writing || l.Status is not null, Date = l.DateText,
        Folder = l.Class == Core.Configs.Unsorted ? "" : l.Class, Dot = dot(l.Class), Noun = Nouns.For(l.Class, s.GroupOf(l.Class)), ShowFolder = showFolder,
        Open = () => s.OpenLecture?.Invoke(l.Class, l.Id),
    };

    public static Func<string, IBrush> DotsOf(Sources s)
    {
        var dots = s.Classes.GroupBy(c => c.Name).ToDictionary(g => g.Key, g => g.First().Dot);
        return name => dots.TryGetValue(name, out var d) ? d : Brushes.Gray;
    }

    static readonly string[] Counts = ["No", "One", "Two", "Three", "Four", "Five", "Six", "Seven", "Eight", "Nine", "Ten"];

    /// <summary>"Two", "Eleven" as "11": a count as a sentence starts with it.</summary>
    static string Count(int n) => n >= 0 && n < Counts.Length ? Counts[n] : n.ToString(CultureInfo.InvariantCulture);

    /// <summary>"assignment", or "quiz" when everything counted is one.</summary>
    static string Kind(IReadOnlyList<CanvasApi.Item> items, bool plural)
    {
        bool quizzes = items.Count > 0 && items.All(i => i.Kind == "quiz");
        return quizzes ? plural ? "quizzes" : "quiz" : plural ? "assignments" : "assignment";
    }

    /// <summary>The events still to come today and tomorrow (one that's on now counts), soonest first.</summary>
    static IEnumerable<Event> Coming(Sources s)
    {
        var today = Local(s.Now, s).Date;
        return (s.Events ?? []).Where(e => e.End > s.Now && Local(e.Start, s).Date <= today.AddDays(1)).OrderBy(e => e.Start);
    }

    /// <summary>An event by the name the page uses: its own, or (named for its class and nothing more) the class's
    /// without the registrar's code.</summary>
    static string Named(Event e) => e.Class is { } c && string.Equals(e.Title.Trim(), c, StringComparison.OrdinalIgnoreCase) ? ClassNames.Display(c) : e.Title.Trim();

    /// <summary>"in 11 minutes", "in a minute".</summary>
    static string InMinutes(TimeSpan until)
    {
        int minutes = Math.Max(1, (int)Math.Ceiling(until.TotalMinutes));
        return minutes == 1 ? "in a minute" : $"in {minutes} minutes";
    }

    /// <summary>What's on, or next today, in a sentence: "Capstone team sync starts in 11 minutes." (within the hour),
    /// "Data Mining is on now.", "Officer meeting is at 6:00 PM."; "" with nothing left today.</summary>
    public static string EventSentence(Sources s)
    {
        var today = Local(s.Now, s).Date;
        if (Coming(s).FirstOrDefault(e => Local(e.Start, s).Date <= today) is not { } next) return "";
        string name = Named(next);
        if (next.Start <= s.Now) return $"{name} is on now.";
        var until = next.Start - s.Now;
        return until <= TimeSpan.FromHours(1) ? $"{name} starts {InMinutes(until)}." : $"{name} is at {Time(next.Start, s.Zone)}.";
    }

    /// <summary>
    /// What's to hand in, in a sentence, the most pressing first: what's due next and when ("Reading quiz 6 is due
    /// tonight", "Two assignments due Wednesday"), then what's missing ("…, and Asynchronous Lecture 5 is still
    /// missing."); "" with nothing due this week and nothing missing.
    /// </summary>
    /// <param name="missing">Say what's missing after what's due (Due does; Home, with an event to say first, keeps to
    /// what's due).</param>
    public static string DueSentence(Sources s, bool missing = true)
    {
        var today = Local(s.Now, s).Date;
        var monday = Monday(s);
        var open = StillToHandIn(s.Due).ToList();
        var late = open.Where(i => LateBefore(i, s)).ToList();
        var week = open.Where(i => !LateBefore(i, s) && i.DueAt is { } d && Local(d, s).Date >= today && Local(d, s).Date < monday.AddDays(7))
            .OrderBy(i => i.DueAt).ToList();
        string first = "";
        if (week.Count > 0)
        {
            var day = Local(week[0].DueAt!.Value, s).Date;
            var that = week.Where(i => Local(i.DueAt!.Value, s).Date == day).ToList();
            string on = day == today ? "today" : day == today.AddDays(1) ? "tomorrow" : Words(day, "dddd");
            if (that.Count == 1)
            {
                var at = Local(that[0].DueAt!.Value, s);
                string day1 = day == today ? at.Hour >= 17 ? "tonight" : "today" : on;
                // The end of the day is when most things are due, so it goes unsaid.
                first = at is { Hour: 23, Minute: 59 } ? $"{that[0].Name} is due {day1}" : $"{that[0].Name} is due {day1} at {Words(at, "h:mm tt")}";
            }
            else first = $"{Count(that.Count)} {Kind(that, plural: true)} due {on}";
        }
        string then = !missing && first.Length > 0 ? "" : late.Count switch
        {
            0 => "",
            1 => first.Length > 0 ? $"and {late[0].Name} is still missing" : $"{late[0].Name} is still missing",
            _ => first.Length > 0 ? $"and {Count(late.Count).ToLowerInvariant()} are still missing" : $"{Count(late.Count)} {Kind(late, plural: true)} are still missing",
        };
        if (first.Length == 0 && then.Length == 0) return "";
        return first.Length > 0 && then.Length > 0 ? $"{first}, {then}." : $"{first}{then}.";
    }

    /// <summary>
    /// Home's sentence or two, the most pressing first: what's on or about to start ("Capstone team sync starts in 11
    /// minutes."), then what's due ("Reading quiz 6 is due tonight."). With neither it says nothing is due this week;
    /// without Canvas or calendars there's nothing to tell of, so it counts the library instead.
    /// </summary>
    public static string HomeLede(Sources s, bool canvas)
    {
        string soon = EventSentence(s);
        string due = canvas ? DueSentence(s, missing: soon.Length == 0) : "";
        if (soon.Length > 0 || due.Length > 0) return string.Join(" ", new[] { soon, due }.Where(x => x.Length > 0));
        if (canvas) return "Nothing due this week.";
        int notes = s.Classes.Sum(c => c.Lectures) + s.Unsorted;
        if (notes == 0) return "No notes yet. Record something and its notes land here.";
        string have = $"{Count(notes)} note{(notes == 1 ? "" : "s")} in {Count(s.Classes.Count).ToLowerInvariant()} folder{(s.Classes.Count == 1 ? "" : "s")}";
        return s.Writing > 0 ? $"{have}, and notes being written for {Count(s.Writing).ToLowerInvariant()}." : have + ".";
    }

    /// <summary>
    /// A week as the planner shows it: what's missing (a column of its own, only in the week that's on, and only when
    /// something is), then Monday to Sunday, each with its class meetings (from the student's calendars; none are made
    /// up without them) over what's due that day. A handed-in thing stays on its day, struck through. More than
    /// <see cref="DayRows"/> due on a day shows one fewer and "+2 more".
    /// </summary>
    public static List<WeekDay> WeekOf(Sources s, Func<string, IBrush> dot, DateTime monday, string? only = null)
    {
        var today = Local(s.Now, s).Date;
        bool Wanted(string cls) => only is null || cls == only;
        var open = StillToHandIn(s.Due).Where(i => Wanted(i.Class)).ToList();
        var done = HandedIn(s.Due).Where(i => Wanted(i.Class)).ToList();
        var days = new List<WeekDay>();
        var late = today >= monday && today < monday.AddDays(7)
            ? open.Where(i => LateBefore(i, s) && (i.DueAt is not { } d || Local(d, s).Date < monday)).OrderBy(i => i.DueAt ?? DateTimeOffset.MinValue).ToList()
            : [];
        if (late.Count > 0)
        {
            var column = new WeekDay { Name = "Late", IsLate = true, Weight = 1.15 };
            Fill(column, [.. late.Select(i => DueRow(i, s, dot, only is null))]);
            days.Add(column);
        }
        for (int n = 0; n < 7; n++)
        {
            var date = monday.AddDays(n);
            var day = new WeekDay
            {
                Name = date == today ? "Today" : Words(date, "ddd"), Number = date.Day.ToString(CultureInfo.InvariantCulture),
                IsToday = date == today, Weight = n >= 5 ? 0.8 : 1,
            };
            foreach (var m in (s.Events ?? []).Where(m => m.Class is { } c && Wanted(c) && Local(m.Start, s).Date == date).OrderBy(m => m.Start))
                day.Meetings.Add(new WeekMeeting(Words(Local(m.Start, s), "h:mm"), ClassNames.Short(m.Class!), dot(m.Class!),
                    s.Lectures.Any(l => l.Class == m.Class && l.Date is { } at && at >= m.Start.AddMinutes(-45) && at <= m.End.AddMinutes(45))));
            var due = open.Where(i => i.DueAt is { } d && Local(d, s).Date == date).Select(i => (Item: i, Done: false))
                .Concat(done.Where(i => i.DueAt is { } d && Local(d, s).Date == date).Select(i => (Item: i, Done: true)))
                .OrderBy(x => x.Item.DueAt).Select(x => DueRow(x.Item, s, dot, only is null, done: x.Done)).ToList();
            Fill(day, due);
            days.Add(day);
        }
        for (int i = 0; i < days.Count; i++) days[i].Ruled = i > 0 && !days[i].IsToday && !days[i - 1].IsToday;
        return days;

        static void Fill(WeekDay day, List<OverviewDue> due)
        {
            int show = due.Count > DayRows ? DayRows - 1 : due.Count;
            day.Deadlines.AddRange(due.Take(show));
            if (due.Count > show)
            {
                // The rest of a full day: the month, where every one of them shows.
                day.More = $"+{due.Count - show} more";
                day.Rest.AddRange(due.Skip(show));
            }
        }
    }

    /// <summary>Home: the date, what's most pressing, what's up next, and every note.</summary>
    public static OverviewModel Home(Sources s, bool onlyUnsorted = false)
    {
        var dot = DotsOf(s);
        bool canvas = s.Due is not null && s.Canvas.Any(c => c.Linked);
        var m = new OverviewModel
        {
            IsHome = true,
            Title = Words(Local(s.Now, s), "dddd, MMMM d"),
            Lede = HomeLede(s, canvas),
            ShowUpNext = s.Events is not null,
            OnlyUnsorted = onlyUnsorted,
            NotesEmpty = onlyUnsorted ? "Nothing to sort. Every note is in a folder."
                : $"No notes yet. Record something from here or the {(OperatingSystem.IsMacOS() ? "menu bar" : "tray")} and its notes land here.",
        };
        var today = Local(s.Now, s).Date;
        foreach (var e in Coming(s).Take(UpNextRows))
        {
            var at = Local(e.Start, s);
            bool on = e.Start <= s.Now, soon = !on && e.Start - s.Now <= Soon;
            string day = on ? "Now" : soon ? Nouns.Capital(InMinutes(e.Start - s.Now)) : at.Date == today ? "Today" : "Tomorrow";
            string folder = e.Class ?? "", noun = Nouns.For(folder, s.GroupOf(folder));
            string when = on ? "on now" : $"{(at.Date == today ? "today" : "tomorrow")} at {Words(at, "h:mm tt")}";
            string id = e.Id;
            m.UpNext.Add(new UpNext(id, Words(at, "h:mm tt"), day, soon || on, Named(e), folder, dot(folder), $"Record {Named(e)} as a {noun}, {when}",
                () => s.RecordEvent?.Invoke(id)));
        }
        m.Add(s.Lectures.Select(l => Row(l, s, dot, showFolder: true)), canLoadMore: false);

        // The filter: every note, the unsorted ones (in place), then each folder under its group (those go to its page).
        m.Filters.Add(new NotesFilter(NotesFilter.All, "All notes", null, 0, !onlyUnsorted, null));
        m.Filters.Add(new NotesFilter(NotesFilter.Unsorted, "Unsorted", null, s.Unsorted, onlyUnsorted, null));
        if (s.Classes.Count > 0) m.Filters.Add(new NotesFilter(NotesFilter.Rule, "", null, 0, false, null));
        foreach (var group in s.Classes.GroupBy(c => s.GroupOf(c.Name)).OrderBy(g => g.Key.Length > 0))
        {
            m.Filters.Add(new NotesFilter(NotesFilter.Label, group.Key.Length == 0 ? "Classes" : group.Key, null, 0, false, null));
            foreach (var (name, brush, count) in group)
                m.Filters.Add(new NotesFilter(NotesFilter.Folder, ClassNames.Display(name), brush, count, false, new RelayCommand(() => s.OpenClass?.Invoke(name))));
        }
        return m;
    }

    /// <summary>Due: what's most pressing in a sentence, and the calendar.</summary>
    public static OverviewModel ForDue(Sources s, string? only = null)
    {
        string due = DueSentence(s);
        return new OverviewModel
        {
            IsDuePage = true,
            Title = "Due",
            Lede = s.Due is null ? "" : due.Length > 0 ? due : StillToHandIn(s.Due).Any() ? "Nothing due this week." : "Nothing to hand in. You're all caught up.",
            Calendar = new DueCalendarModel(s) { Class = only },
        };
    }

    static readonly string[] Plurals = ["Sundays", "Mondays", "Tuesdays", "Wednesdays", "Thursdays", "Fridays", "Saturdays"];

    /// <summary>"Monday, Wednesday and Friday".</summary>
    static string List(IReadOnlyList<string> words) => words.Count switch
    {
        0 => "",
        1 => words[0],
        _ => string.Join(", ", words.Take(words.Count - 1)) + " and " + words[^1],
    };

    /// <summary>
    /// When a folder meets, from the week's events that are clearly its own: for a class "Meets Monday, Wednesday and
    /// Friday at 9:30 AM"; for a club or work "Thursdays at 6:00 PM". The time is said only when every meeting starts
    /// at the same one. "" with nothing on the calendar for it. (A week is all the app keeps of the calendar, so this
    /// is the week's pattern, not the term's.)
    /// </summary>
    public static string Meets(string folder, Sources s, bool asClass)
    {
        var monday = Monday(s);
        var own = (s.Events ?? []).Where(e => e.Class == folder).ToList();
        // The events named for the folder and nothing more are its regular meetings; one with a name of its own (a team
        // sync, an officers' meeting) is a one-off, and only counts when there are none of the regular kind.
        var regular = own.Where(e => string.Equals(Named(e), ClassNames.Display(folder), StringComparison.OrdinalIgnoreCase)).ToList();
        if (regular.Count > 0) own = regular;
        var week = own.Where(e => Local(e.Start, s).Date >= monday && Local(e.Start, s).Date < monday.AddDays(7)).ToList();
        if (week.Count == 0) week = own.Where(e => e.Start >= s.Now).ToList();
        if (week.Count == 0) return "";
        var days = week.Select(e => Local(e.Start, s).DayOfWeek).Distinct().OrderBy(d => ((int)d + 6) % 7).ToList();
        var times = week.Select(e => Time(e.Start, s.Zone)).Distinct().ToList();
        string at = times.Count == 1 ? $" at {times[0]}" : "";
        return asClass ? $"Meets {List([.. days.Select(d => d.ToString())])}{at}" : $"{List([.. days.Select(d => Plurals[(int)d])])}{at}";
    }

    /// <summary>When a folder next meets: "Wednesday at 9:30 AM", "Today at 6:00 PM, Officer meeting." (its name when
    /// it's more than the folder's own); "" with nothing ahead.</summary>
    public static string NextMeets(string folder, Sources s)
    {
        if ((s.Events ?? []).Where(e => e.Class == folder && e.End > s.Now).OrderBy(e => e.Start).FirstOrDefault() is not { } next) return "";
        var at = Local(next.Start, s);
        int away = (at.Date - Local(s.Now, s).Date).Days;
        string when = next.Start <= s.Now ? $"Now, until {Time(next.End, s.Zone)}"
            : $"{(away == 0 ? "Today" : away == 1 ? "Tomorrow" : away < 7 ? Words(at, "dddd") : Words(at, "ddd d MMM"))} at {Words(at, "h:mm tt")}";
        string name = Named(next);
        return string.Equals(name, ClassNames.Display(folder), StringComparison.OrdinalIgnoreCase) ? when : $"{when}, {name}.";
    }

    /// <summary>The heading a folder's note sits under: "This week", "Last week", then its month ("September", and
    /// with its year once that isn't this one).</summary>
    public static string GroupOf(DateTimeOffset? date, Sources s)
    {
        var today = Local(s.Now, s).Date;
        var d = date is { } at ? Local(at, s).Date : today;
        var monday = Monday(s);
        if (d >= monday) return "This week";
        if (d >= monday.AddDays(-7)) return "Last week";
        return d.Year == today.Year ? d.ToString("MMMM", CultureInfo.InvariantCulture) : d.ToString("MMMM yyyy", CultureInfo.InvariantCulture);
    }

    /// <summary>A folder's page: its notes by week, and (in the rail) what's due, its lists on Canvas and when it next meets.</summary>
    public static OverviewModel ForClass(ClassHomeFacts c, Sources s)
    {
        var dot = DotsOf(s);
        string noun = Nouns.For(c.Name, c.Group), shown = ClassNames.Display(c.Name);
        bool isClass = c.Group.Length == 0;
        // The lede: for a class its code (and that it's on Canvas) and when it meets; for a club or work, what its
        // meetings are and when. With no calendar to say when, how many there are.
        var parts = new List<string>();
        string code = c.Code ?? ClassNames.Code(c.Name);
        // (A class that goes by its code, "CS 101", needs no code under its name.)
        bool sayCode = isClass && code.Length > 0 && !string.Equals(code, shown, StringComparison.OrdinalIgnoreCase);
        if (sayCode) parts.Add(c.Canvas is not null ? $"{code} on Canvas." : $"{code}.");
        else if (isClass && c.Canvas is not null) parts.Add("On Canvas.");
        string meets = Meets(c.Name, s, isClass);
        string kind = c.Group switch { "Clubs" => "Club meetings", "Work" => "Work meetings", _ => "Meetings" };
        if (isClass && meets.Length > 0) parts.Add(meets + ".");
        else if (!isClass && meets.Length > 0) parts.Add($"{kind}, {meets}.");
        else if (parts.Count == 0)
            parts.Add(c.Lectures == 0 ? $"No {noun}s yet." : $"{Count(c.Lectures)} {noun}{(c.Lectures == 1 ? "" : "s")} so far.");
        var m = new OverviewModel
        {
            ClassName = c.Name,
            Title = shown,
            Dot = c.Dot,
            Noun = noun,
            Lede = string.Join(" ", parts),
            NextText = NextMeets(c.Name, s),
            NotesEmpty = $"No {noun}s yet. Record one here, or attach a recording.",
            OnCalendar = c.Canvas is null ? null : () => s.OpenDue?.Invoke(c.Name),
        };
        foreach (var g in s.Lectures.Where(l => l.Class == c.Name).GroupBy(l => GroupOf(l.Date, s)))
        {
            var rows = g.Select(l => Row(l, s, dot, showFolder: false)).ToList();
            rows[0].First = true;
            m.Groups.Add(new NoteGroup(g.Key, m.Groups.Count == 0, rows));
        }
        if (c.Canvas is not null)
        {
            // What's missing first, then what's next: three in all.
            var open = StillToHandIn(s.Due).Where(i => i.Class == c.Name).ToList();
            var late = open.Where(i => LateBefore(i, s)).OrderBy(i => i.DueAt ?? DateTimeOffset.MinValue);
            var ahead = open.Where(i => !LateBefore(i, s) && i.DueAt is not null).OrderBy(i => i.DueAt);
            foreach (var i in late.Concat(ahead).Take(RailRows)) m.Due.Add(Rail(i, s, m.Due.Count == 0));
        }
        return m;
    }

    /// <summary>A deadline in the rail: "Sun 18 Oct, in 6 days" (within two weeks; heavier within one), "Today, 11:59
    /// PM", or "Was due Mon 5 Oct" once it's missing.</summary>
    static RailDue Rail(CanvasApi.Item i, Sources s, bool first)
    {
        bool late = LateBefore(i, s);
        string when;
        bool soon = false;
        if (i.DueAt is not { } d) when = "No due date";
        else if (late) when = $"Was due {Words(Local(d, s), Local(d, s).Year == Local(s.Now, s).Year ? "ddd d MMM" : "d MMM yyyy")}";
        else
        {
            int away = (Local(d, s).Date - Local(s.Now, s).Date).Days;
            soon = away <= 7;
            when = away <= 1 ? $"{Day(d, s.Now, s.Zone)}, {Time(d, s.Zone)}" : away <= 14 ? $"{Day(d, s.Now, s.Zone)}, in {away} days" : Day(d, s.Now, s.Zone);
        }
        return new RailDue(i.Name, when, soon, late, first, $"{i.Name}, {(late ? "missing, " : "due ")}{when}", () => s.OpenAssignment?.Invoke(i.Class, i.Id));
    }

    /// <summary>A Canvas class's lists, in the rail: each opens that list.</summary>
    public void AddLinks(ClassHomeFacts c, Action<ClassTab>? tab)
    {
        if (c.Canvas is not { } k || tab is null) return;
        Links.Add(new OverviewLink("Assignments", $"{k.ToHandIn} to hand in, {k.Done} done", () => tab(ClassTab.Assignments)));
        Links.Add(new OverviewLink("Modules", k.Modules == 1 ? "1 module" : $"{k.Modules} modules", () => tab(ClassTab.Modules)));
        Links.Add(new OverviewLink("Announcements", k.AnnouncementsNew > 0 ? $"{k.Announcements}, {k.AnnouncementsNew} new" : $"{k.Announcements}",
            () => tab(ClassTab.Announcements)));
        OnPropertyChanged(nameof(HasLinks));
        OnPropertyChanged(nameof(HasRail));
    }
}
