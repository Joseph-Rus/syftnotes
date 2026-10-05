using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StudyStash.App.Services;

namespace StudyStash.App.ViewModels;

/// <summary>A number at the top of an overview: "12" over "Lectures". One with somewhere to go opens it.</summary>
public sealed record OverviewStat(string Value, string Label, bool Strong = false, Action? Open = null)
{
    public bool CanOpen => Open is not null;
}

/// <summary>A lecture in an overview's Recent lectures: its title, "Tue 23 Sep · 1 h 12 min", the lecture in a sentence
/// and, on Home, the class it's in with that class's dot.</summary>
public sealed record OverviewLecture(string Id, string Title, string Meta, string Summary, string ClassName, IBrush Dot, Action Open)
{
    public bool HasSummary => Summary.Length > 0;
}

/// <summary>Something to hand in: its title, how soon ("Tomorrow", "Missing"), and when and for which class.</summary>
public sealed record OverviewDue(string Class, string Id, string Title, string Right, bool Strong, string Sub, IBrush Dot, Action Open);

/// <summary>An event coming up: when, what, and where or for which class.</summary>
public sealed record OverviewEvent(string When, string Title, string Detail, IBrush Dot, bool Now)
{
    public bool HasDetail => Detail.Length > 0;
}

/// <summary>A class's card on Home: its dot and name, how many lectures and when the last was, and what's next.</summary>
public sealed record OverviewClass(string Name, IBrush Dot, string Lectures, string Next, bool NextStrong, Action Open)
{
    public bool HasNext => Next.Length > 0;
}

/// <summary>A way further into a class from its home: all its lectures, or one of its Canvas lists.</summary>
public sealed record OverviewLink(string Glyph, string Label, string Detail, Action Open);

/// <summary>What a class's home is made from: the class, its lectures, and (when linked) its Canvas course.</summary>
/// <summary>A folder linked to a class: its name, where it is, whether it's still there, and what its buttons do.</summary>
public sealed record LinkedFolder(string Name, string Path, bool Here, Action Open, Action Unlink)
{
    public string Detail => Here ? Path : $"{Path} (not found)";
}

public sealed record ClassHomeFacts(string Name, IBrush Dot, int Lectures, string? Code, string? CourseTitle, string? Description,
    CanvasApi.Counts? Canvas);

/// <summary>
/// The overview pages, across the whole of the library window beside the sidebar: Home (every class at once: what's due,
/// what's coming up, the newest lectures and a card for each class) and a class's home (the same, for that class alone,
/// with the ways further in: all its lectures, and its assignments, modules and announcements on Canvas). Built from
/// what the shell already has; nothing here fetches.
/// </summary>
public sealed partial class OverviewModel : ObservableObject
{
    /// <summary>How many rows a section shows before its "See all".</summary>
    public const int Rows = 5;

    /// <summary>Home, or a class's home (<see cref="ClassName"/>).</summary>
    public bool IsHome { get; init; }
    public bool IsClass => !IsHome;
    public string ClassName { get; init; } = "";
    public string Title { get; init; } = "";
    public string Subtitle { get; init; } = "";
    public IBrush Dot { get; init; } = Brushes.Gray;
    /// <summary>What the class covers, as the student described it in Settings.</summary>
    public string Description { get; init; } = "";
    public bool HasDescription => Description.Length > 0;

    public ObservableCollection<OverviewStat> Stats { get; } = [];
    public ObservableCollection<OverviewDue> Due { get; } = [];
    public ObservableCollection<OverviewEvent> Events { get; } = [];
    public ObservableCollection<OverviewLecture> Lectures { get; } = [];
    public ObservableCollection<OverviewClass> Classes { get; } = [];
    public ObservableCollection<OverviewLink> Links { get; } = [];

    /// <summary>Canvas is linked (to this class, or on Home to any): the Due section shows, even when it's empty.</summary>
    public bool HasCanvas { get; init; }
    /// <summary>The student has calendars: Coming up shows, even when it's empty.</summary>
    public bool HasCalendars { get; init; }
    public bool ShowDue => HasCanvas;
    public bool ShowEvents => HasCalendars;
    public bool NoDue => Due.Count == 0;
    public bool NoEvents => Events.Count == 0;
    public bool NoLectures => Lectures.Count == 0;
    public bool HasClasses => Classes.Count > 0;
    /// <summary>A Canvas class's lists (its lectures are Recent lectures' "See all").</summary>
    public bool HasLinks => Links.Count > 0;

    public string DueHeading => IsHome ? "Due soon" : "To hand in";
    public string DueEmpty => IsHome ? "Nothing to hand in. You're all caught up." : $"Nothing to hand in for {ClassName}.";
    public string EventsEmpty => IsHome ? CalendarWords.NothingComingUp : $"No {ClassName} on your calendar this week.";
    public string LecturesEmpty { get; init; } = "";
    /// <summary>"See all" beside Due soon on Home: the Due page.</summary>
    public Action? OnAllDue { get; init; }
    /// <summary>"See all" beside Recent lectures: the class's (or Unsorted's) lectures by week.</summary>
    public Action? OnAllLectures { get; init; }
    public bool CanSeeAllDue => OnAllDue is not null && Due.Count > 0;
    public bool CanSeeAllLectures => OnAllLectures is not null && Lectures.Count > 0;

    /// <summary>Folders on the computer that go with the class (a project's repo, say), linked rather than copied in.</summary>
    public ObservableCollection<LinkedFolder> Folders { get; } = [];
    public bool HasFolders => Folders.Count > 0;
    /// <summary>Link a folder: set on a class's home when the library is on this computer (its folders are this one's).</summary>
    public Action? OnLinkFolder { get; set; }
    public bool CanLinkFolders => OnLinkFolder is not null;
    /// <summary>The Linked folders section: when there are some, or one can be linked.</summary>
    public bool ShowFolders => IsClass && (HasFolders || CanLinkFolders);
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
    }

    /// <summary>What's attached to the class (its home only): drop files on the page, or Attach.</summary>
    [ObservableProperty] public partial AttachmentsModel? Files { get; set; }
    public bool HasFiles => Files is not null;
    partial void OnFilesChanged(AttachmentsModel? value) => OnPropertyChanged(nameof(HasFiles));

    /// <summary>The window is narrow: sections stack in one column, and the class cards go two across.</summary>
    [ObservableProperty] public partial bool Narrow { get; set; }
    public int CardColumns => Narrow ? 2 : 3;
    /// <summary>What's due and what's coming up sit beside the lectures (wide) or under them (narrow).</summary>
    public bool HasSide => ShowDue || ShowEvents;
    public int SideColumn => Narrow ? 0 : 2;
    public int SideSpan => Narrow ? 3 : 1;
    /// <summary>A class's ways in sit in one row when there's room.</summary>
    public int LinkColumns => Narrow ? 2 : Math.Max(1, Links.Count);
    public int SideRow => Narrow ? 1 : 0;
    /// <summary>The lectures take the whole width when nothing sits beside them.</summary>
    public int LecturesSpan => Narrow || !HasSide ? 3 : 1;
    partial void OnNarrowChanged(bool value)
    {
        OnPropertyChanged(nameof(CardColumns));
        OnPropertyChanged(nameof(SideColumn));
        OnPropertyChanged(nameof(SideSpan));
        OnPropertyChanged(nameof(LinkColumns));
        OnPropertyChanged(nameof(SideRow));
        OnPropertyChanged(nameof(LecturesSpan));
    }

    [RelayCommand] static void Open(Action? open) => open?.Invoke();
    [RelayCommand] static void OpenStat(OverviewStat stat) => stat.Open?.Invoke();
    [RelayCommand] static void OpenLecture(OverviewLecture l) => l.Open();
    [RelayCommand] static void OpenDue(OverviewDue d) => d.Open();
    [RelayCommand] static void OpenClass(OverviewClass c) => c.Open();
    [RelayCommand] static void OpenLink(OverviewLink l) => l.Open();
    [RelayCommand] void AllDue() => OnAllDue?.Invoke();
    [RelayCommand] void AllLectures() => OnAllLectures?.Invoke();

    // --- building one -----------------------------------------------------------------------------------------------

    /// <summary>Everything the shell knows, for Home or a class's home.</summary>
    public sealed record Sources
    {
        public required DateTimeOffset Now { get; init; }
        public required TimeZoneInfo Zone { get; init; }
        /// <summary>Every class, with its dot and how many lectures it holds.</summary>
        public IReadOnlyList<(string Name, IBrush Dot, int Lectures)> Classes { get; init; } = [];
        /// <summary>Lectures, newest first: every class's for Home, the class's own for its home.</summary>
        public IReadOnlyList<LectureFacts> Lectures { get; init; } = [];
        /// <summary>What's due (Canvas's Due list), or null without Canvas.</summary>
        public CanvasApi.DueResponse? Due { get; init; }
        /// <summary>The classes linked to Canvas.</summary>
        public IReadOnlyList<CanvasApi.ClassRow> Canvas { get; init; } = [];
        /// <summary>Events coming up, with the class each is for; null without calendars.</summary>
        public IReadOnlyList<ComingUpRow>? Events { get; init; }
        public int Unsorted { get; init; }
        public int Writing { get; init; }
        public Action<string>? OpenClass { get; init; }
        /// <summary>A class's lectures by week (its All lectures), rather than its home.</summary>
        public Action<string>? OpenAllLectures { get; init; }
        /// <summary>Opens a lecture (its class, its id) beside its class's list.</summary>
        public Action<string, string>? OpenLecture { get; init; }
        public Action<string, string>? OpenAssignment { get; init; }
        public Action? OpenDueList { get; init; }
        public Action? OpenUnsorted { get; init; }
    }

    /// <summary>One lecture as the library lists it.</summary>
    public sealed record LectureFacts(string Id, string Title, string Class, DateTimeOffset? Date, double? Seconds, string Summary, bool Writing);

    /// <summary>"Tue 23 Sep · 1 h 12 min".</summary>
    public static string LectureMeta(LectureFacts l, TimeZoneInfo zone)
    {
        string day = l.Date is { } d ? TimeZoneInfo.ConvertTime(d, zone).ToString("ddd d MMM", CultureInfo.InvariantCulture) : "";
        return l.Seconds is { } s ? $"{day} · {Core.TimedText.Length(s)}" : day;
    }

    /// <summary>"Good morning", "Good afternoon" or "Good evening".</summary>
    public static string Greeting(DateTimeOffset now, TimeZoneInfo zone) => TimeZoneInfo.ConvertTime(now, zone).Hour switch
    {
        < 5 => "Good evening",
        < 12 => "Good morning",
        < 18 => "Good afternoon",
        _ => "Good evening",
    };

    static IEnumerable<CanvasApi.Item> StillToHandIn(CanvasApi.DueResponse? due) =>
        due is null ? [] : due.Groups.Where(g => g.Key != "handed_in").SelectMany(g => g.Items).Where(CanvasWords.ToHandIn);

    static bool Overdue(CanvasApi.Item i, DateTimeOffset now) => i.Missing || i.Status is "missing" or "overdue" or "past due" || i.DueAt < now;

    OverviewDue DueRow(CanvasApi.Item i, Sources s, Func<string, IBrush> dot) => new(i.Class, i.Id, i.Name, CanvasWords.RightLabel(i, s.Zone, s.Now),
        i.Missing || Overdue(i, s.Now), IsHome ? CanvasWords.DueSub(i, s.Zone, s.Now) : CanvasWords.ClassTabRow(i, s.Zone, s.Now), dot(i.Class),
        () => s.OpenAssignment?.Invoke(i.Class, i.Id));

    OverviewLecture LectureRow(LectureFacts l, Sources s, Func<string, IBrush> dot) => new(l.Id, l.Title, LectureMeta(l, s.Zone),
        l.Writing ? "Writing notes…" : l.Summary, l.Class, dot(l.Class), () => s.OpenLecture?.Invoke(l.Class, l.Id));

    static Func<string, IBrush> DotsOf(Sources s)
    {
        var dots = s.Classes.GroupBy(c => c.Name).ToDictionary(g => g.Key, g => g.First().Dot);
        return name => dots.TryGetValue(name, out var d) ? d : Brushes.Gray;
    }

    /// <summary>Home: every class at once.</summary>
    public static OverviewModel Home(Sources s)
    {
        var dot = DotsOf(s);
        bool canvas = s.Due is not null && s.Canvas.Any(c => c.Linked);
        var local = TimeZoneInfo.ConvertTime(s.Now, s.Zone);
        int lectures = s.Classes.Sum(c => c.Lectures) + s.Unsorted;
        var m = new OverviewModel
        {
            IsHome = true,
            Title = Greeting(s.Now, s.Zone),
            Subtitle = local.ToString("dddd d MMMM", CultureInfo.InvariantCulture),
            HasCanvas = canvas,
            HasCalendars = s.Events is not null,
            OnAllDue = canvas ? s.OpenDueList : null,
            LecturesEmpty = "No lectures yet. Record one from the menu bar and its notes land here.",
        };
        var toHandIn = StillToHandIn(s.Due).ToList();
        m.Stats.Add(new OverviewStat(lectures.ToString(CultureInfo.InvariantCulture), lectures == 1 ? "Lecture" : "Lectures"));
        m.Stats.Add(new OverviewStat(s.Classes.Count.ToString(CultureInfo.InvariantCulture), s.Classes.Count == 1 ? "Class" : "Classes"));
        if (canvas)
        {
            m.Stats.Add(new OverviewStat(toHandIn.Count.ToString(CultureInfo.InvariantCulture), "To hand in", Open: s.OpenDueList));
            int late = toHandIn.Count(i => Overdue(i, s.Now));
            if (late > 0) m.Stats.Add(new OverviewStat(late.ToString(CultureInfo.InvariantCulture), "Overdue", Strong: true, Open: s.OpenDueList));
        }
        if (s.Writing > 0) m.Stats.Add(new OverviewStat(s.Writing.ToString(CultureInfo.InvariantCulture), "Writing notes"));
        if (s.Unsorted > 0) m.Stats.Add(new OverviewStat(s.Unsorted.ToString(CultureInfo.InvariantCulture), "Unsorted", Open: s.OpenUnsorted));

        foreach (var i in toHandIn.Take(Rows)) m.Due.Add(m.DueRow(i, s, dot));
        foreach (var e in (s.Events ?? []).Take(Rows)) m.Events.Add(new OverviewEvent(e.When, e.Title, e.Detail, e.Dot, e.Now));
        foreach (var l in s.Lectures.Take(Rows)) m.Lectures.Add(m.LectureRow(l, s, dot));

        foreach (var (name, classDot, count) in s.Classes)
        {
            var last = s.Lectures.FirstOrDefault(l => l.Class == name);
            string words = CanvasWords.LectureCountText(count);
            if (last?.Date is { } d) words += $" · last {TimeZoneInfo.ConvertTime(d, s.Zone).ToString("ddd d MMM", CultureInfo.InvariantCulture)}";
            var next = toHandIn.FirstOrDefault(i => i.Class == name);
            string when = next is null ? "" : CanvasWords.RightLabel(next, s.Zone, s.Now);
            string nextWords = next is null ? "" : when.Length > 0 ? $"{next.Name} · {when}" : next.Name;
            string cls = name;
            m.Classes.Add(new OverviewClass(name, classDot, words, nextWords, next is not null && Overdue(next, s.Now), () => s.OpenClass?.Invoke(cls)));
        }
        return m;
    }

    /// <summary>A class's home: that class alone, with the ways further in.</summary>
    public static OverviewModel ForClass(ClassHomeFacts c, Sources s)
    {
        var dot = DotsOf(s);
        bool canvas = c.Canvas is not null;
        var parts = new List<string> { CanvasWords.LectureCountText(c.Lectures) };
        if (canvas) parts.Add(CanvasWords.ClassCanvasLine(c.Code, c.CourseTitle ?? c.Name));
        var m = new OverviewModel
        {
            IsHome = false,
            ClassName = c.Name,
            Title = c.Name,
            Dot = c.Dot,
            Subtitle = string.Join(" · ", parts),
            Description = c.Description ?? "",
            HasCanvas = canvas,
            HasCalendars = s.Events is not null,
            OnAllLectures = () => (s.OpenAllLectures ?? s.OpenClass)?.Invoke(c.Name),
            LecturesEmpty = $"No lectures in {c.Name} yet. Record one and it lands here.",
        };
        var toHandIn = StillToHandIn(s.Due).Where(i => i.Class == c.Name).ToList();
        var events = (s.Events ?? []).Where(e => e.ClassName == c.Name).ToList();
        // The count opens the lectures, as To hand in opens what's due.
        m.Stats.Add(new OverviewStat(c.Lectures.ToString(CultureInfo.InvariantCulture), c.Lectures == 1 ? "Lecture" : "Lectures",
            Open: c.Lectures > 0 ? m.OnAllLectures : null));
        if (canvas)
        {
            m.Stats.Add(new OverviewStat(toHandIn.Count.ToString(CultureInfo.InvariantCulture), "To hand in"));
            int late = toHandIn.Count(i => Overdue(i, s.Now));
            if (late > 0) m.Stats.Add(new OverviewStat(late.ToString(CultureInfo.InvariantCulture), "Overdue", Strong: true));
            if (c.Canvas!.AnnouncementsNew > 0)
                m.Stats.Add(new OverviewStat(c.Canvas.AnnouncementsNew.ToString(CultureInfo.InvariantCulture), c.Canvas.AnnouncementsNew == 1 ? "New announcement" : "New announcements"));
        }
        if (s.Lectures.FirstOrDefault(l => l.Class == c.Name)?.Date is { } last)
            m.Stats.Add(new OverviewStat(TimeZoneInfo.ConvertTime(last, s.Zone).ToString("ddd d MMM", CultureInfo.InvariantCulture), "Last lecture"));
        if (events.FirstOrDefault() is { } next) m.Stats.Add(new OverviewStat(next.When, "Next on your calendar"));

        foreach (var i in toHandIn.Take(Rows)) m.Due.Add(m.DueRow(i, s, dot));
        foreach (var e in events.Take(Rows)) m.Events.Add(new OverviewEvent(e.When, e.Title, e.Location ?? "", e.Dot, e.Now));
        foreach (var l in s.Lectures.Where(l => l.Class == c.Name).Take(Rows)) m.Lectures.Add(m.LectureRow(l, s, dot));
        return m;
    }

    /// <summary>A class's ways further in: its lectures by week, and on Canvas its tabs.</summary>
    public void AddLinks(ClassHomeFacts c, Action allLectures, Action<ClassTab>? tab)
    {
        // Its lectures are under Recent lectures' "See all": the cards are Canvas's lists alone.
        if (c.Canvas is not { } k || tab is null) return;
        Links.Add(new OverviewLink("assignment", "Assignments", $"{k.ToHandIn} to hand in · {k.Done} done", () => tab(ClassTab.Assignments)));
        Links.Add(new OverviewLink("subject", "Modules", k.Modules == 1 ? "1 module" : $"{k.Modules} modules", () => tab(ClassTab.Modules)));
        Links.Add(new OverviewLink("campaign", "Announcements", k.AnnouncementsNew > 0 ? $"{k.Announcements} · {k.AnnouncementsNew} new" : $"{k.Announcements}",
            () => tab(ClassTab.Announcements)));
        OnPropertyChanged(nameof(HasLinks));
        OnPropertyChanged(nameof(LinkColumns));
    }
}
