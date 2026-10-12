using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StudyStash.App.Services;

namespace StudyStash.App.ViewModels;

/// <summary>A lecture in an overview's lectures: when it was ("Today", "Wed 7 Oct"), its title, the class it's in (on
/// Home, marked with that class's highlighter), the lecture in a sentence or two, and how long it ran ("13 min").</summary>
public sealed record OverviewLecture(string Id, string Title, string When, string Length, string Summary, bool Pending, string ClassName, IBrush Dot, Action Open)
{
    public bool HasSummary => Summary.Length > 0;
}

/// <summary>Something to hand in: its name (marked with its class's highlighter), when it's due ("11:59 PM", or "Was
/// due Mon 5" once it's late, or "Handed in"), and the class it's for.</summary>
public sealed record OverviewDue(string Class, string Id, string Title, string When, bool Late, bool Done, string ClassLabel, IBrush Dot, Action Open)
{
    /// <summary>What a screen reader says for it: the name alone doesn't say whose it is or when.</summary>
    public string Said => string.Join(", ", new[] { Title, ClassLabel, When }.Where(w => w.Length > 0));
    public bool HasClassLabel => ClassLabel.Length > 0;
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
    public Action? OpenMore { get; set; }
    public bool HasMeetings => Meetings.Count > 0;
    public bool HasDeadlines => Deadlines.Count > 0;
    public bool HasMore => More.Length > 0;
    /// <summary>The hairline between the day's meetings and what's due.</summary>
    public bool Divided => HasMeetings && HasDeadlines;
}

/// <summary>Due's home: one stretch of time ("Late", "Today", "Wed 14 Oct") and what falls in it.</summary>
public sealed record OverviewDueGroup(string Heading, IReadOnlyList<OverviewDue> Items, bool Late);

/// <summary>A way further into a class from its home: one of its Canvas lists.</summary>
public sealed record OverviewLink(string Label, string Detail, Action Open);

/// <summary>What a class's home is made from: the class, its lectures, and (when linked) its Canvas course.</summary>
/// <summary>A folder linked to a class: its name, where it is, whether it's still there, and what its buttons do.</summary>
public sealed record LinkedFolder(string Name, string Path, bool Here, Action Open, Action Unlink)
{
    public string Detail => Here ? Path : $"{Path} (not found)";
}

public sealed record ClassHomeFacts(string Name, IBrush Dot, int Lectures, string? Code, string? CourseTitle, string? Description,
    CanvasApi.Counts? Canvas);

/// <summary>
/// The overview pages, across the whole of the library window beside the sidebar: Home (today's date, one sentence on
/// what's most pressing, the week with its class meetings and what's due, and the newest lectures), Due's home
/// (everything to hand in, day by day, late first) and a class's home (its deadlines, its lectures, and the ways
/// further in: its lists on Canvas, linked folders, files). Built from what the shell already has; nothing here
/// fetches.
/// </summary>
public sealed partial class OverviewModel : ObservableObject
{
    /// <summary>How many lectures an overview shows before its link to all of them.</summary>
    public const int Rows = 4;
    /// <summary>How many deadlines a day of the week shows; one more than this and the rest fold into "+2 more".</summary>
    public const int DayRows = 4;

    /// <summary>Home, or a class's home (<see cref="ClassName"/>).</summary>
    public bool IsHome { get; init; }
    /// <summary>Due's own home: what's to hand in, day by day, with the full list a click away.</summary>
    public bool IsDuePage { get; init; }
    public bool IsClass => !IsHome && !IsDuePage;
    /// <summary>The lectures: Home and a class's home, not Due's.</summary>
    public bool ShowMain => !IsDuePage;
    public ObservableCollection<OverviewDueGroup> DueGroups { get; } = [];
    /// <summary>The class as the library knows it (what its lectures are filed under).</summary>
    public string ClassName { get; init; } = "";
    /// <summary>The page's title: today's date on Home ("Monday, October 12"), the class's name on its home.</summary>
    public string Title { get; init; } = "";
    /// <summary>One plain sentence under the title: the most pressing thing first.</summary>
    public string Lede { get; init; } = "";
    public IBrush Dot { get; init; } = Brushes.Gray;
    /// <summary>What the class covers, as the student described it in Settings.</summary>
    public string Description { get; init; } = "";
    public bool HasDescription => Description.Length > 0;

    /// <summary>Home's week: an optional Late column, then Monday to Sunday.</summary>
    public ObservableCollection<WeekDay> Week { get; } = [];
    public bool ShowWeek => Week.Count > 0;
    /// <summary>Under the week when nothing in it is due: "Nothing due this week. Next up is …".</summary>
    public string WeekEmpty { get; init; } = "";
    public string WeekNext { get; init; } = "";
    public bool HasWeekEmpty => WeekEmpty.Length > 0;
    public ObservableCollection<OverviewDue> Due { get; } = [];
    public ObservableCollection<OverviewLecture> Lectures { get; } = [];
    public ObservableCollection<OverviewLink> Links { get; } = [];

    /// <summary>Canvas is linked (to this class, or on Home to any).</summary>
    public bool HasCanvas { get; init; }
    /// <summary>A class's home lists its deadlines when it's on Canvas, even when there are none.</summary>
    public bool ShowDue => IsClass && HasCanvas;
    public bool NoDue => Due.Count == 0;
    public bool NoLectures => Lectures.Count == 0;
    /// <summary>A Canvas class's lists (its lectures are under the lectures' own link).</summary>
    public bool HasLinks => Links.Count > 0;

    public string DueEmpty => $"Nothing to hand in for {Title}.";
    public string LecturesHeading => IsHome ? "Recent lectures" : "Lectures";
    public string LecturesEmpty { get; init; } = "";
    /// <summary>The link beside the lectures' heading, naming how many there are: "All 12 lectures".</summary>
    public string AllLecturesWords { get; init; } = "";
    /// <summary>The Due page (Home's "+2 more", and a class's deadlines' link).</summary>
    public Action? OnAllDue { get; init; }
    /// <summary>The link beside the lectures: the class's lectures by week.</summary>
    public Action? OnAllLectures { get; init; }
    public bool CanSeeAllDue => OnAllDue is not null && Due.Count > 0;
    public bool CanSeeAllLectures => OnAllLectures is not null && AllLecturesWords.Length > 0;
    /// <summary>Home has no list of every class's lectures to open, so there the count is words, not a link.</summary>
    public bool ShowLectureCount => !CanSeeAllLectures && AllLecturesWords.Length > 0;

    /// <summary>Record from here: Home lets the library sort the lecture, a class's home records into that class.
    /// While a lecture records the button shows it (which class, the level, the time) and a click stops it.</summary>
    public Action? OnRecord { get; set; }
    public bool CanRecord => OnRecord is not null;
    [ObservableProperty] public partial bool Recording { get; set; }
    /// <summary>The recording under way: the button reads its class, level, time and whether it's paused from it.</summary>
    [ObservableProperty] public partial RecorderModel? Recorder { get; set; }
    public bool Idle => !Recording;
    public bool Paused => Recording && Recorder?.IsPaused == true;
    public bool Running => Recording && !Paused;
    /// <summary>The button's word: "Record"; recording, the class by its short name; paused, "Paused".</summary>
    public string RecordWords => !Recording ? "Record" : Paused ? "Paused" : ClassNames.Short(Recorder?.DisplayClass ?? "Lecture");
    public string Elapsed => Recorder?.Elapsed ?? "";
    /// <summary>The shortcut that starts and stops a recording, as Settings has it.</summary>
    public string RecordKeys { get; init; } = Platform.Keybindings.Show(Platform.KeyAction.Record);
    /// <summary>Where the lecture goes, for the button's tip and for a screen reader (which hears the time too).</summary>
    public string RecordTip => Recording ? $"{(Paused ? "Paused" : "Recording")} {Elapsed}: stop" : IsHome ? "Start recording a lecture" : $"Record a lecture into {Title}";
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

    /// <summary>Ask about every class (Home) or this one (its home), floating at the foot of the page as under a lecture.</summary>
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
    /// <summary>Link a folder: set on a class's home when the library is on this computer (its folders are this one's).</summary>
    public Action? OnLinkFolder { get; set; }
    public bool CanLinkFolders => OnLinkFolder is not null;
    /// <summary>With folders already linked the button sits over them; with none it's in the card that says what one is for.</summary>
    public bool CanLinkAnother => CanLinkFolders && HasFolders;
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
        OnPropertyChanged(nameof(CanLinkAnother));
        OnPropertyChanged(nameof(ShowFolders));
    }

    /// <summary>What's attached to the class (its home only): drop files on the page, or Attach.</summary>
    [ObservableProperty] public partial AttachmentsModel? Files { get; set; }
    public bool HasFiles => Files is not null;
    partial void OnFilesChanged(AttachmentsModel? value) => OnPropertyChanged(nameof(HasFiles));

    /// <summary>The window is narrow: the page's margins draw in, Record goes under the title, and the week scrolls
    /// sideways rather than squeezing its days.</summary>
    [ObservableProperty] public partial bool Narrow { get; set; }
    /// <summary>The page's buttons (Record, Attach) sit beside the title (wide) or under it (narrow), never over it.</summary>
    public int ActionsColumn => Narrow ? 0 : 1;
    public int ActionsRow => Narrow ? 1 : 0;
    partial void OnNarrowChanged(bool value)
    {
        OnPropertyChanged(nameof(ActionsColumn));
        OnPropertyChanged(nameof(ActionsRow));
    }

    [RelayCommand] static void Open(Action? open) => open?.Invoke();
    [RelayCommand] static void OpenLecture(OverviewLecture l) => l.Open();
    [RelayCommand] static void OpenDue(OverviewDue d) => d.Open();
    [RelayCommand] static void OpenMore(WeekDay d) => d.OpenMore?.Invoke();
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
        /// <summary>Class meetings on the student's calendars (each an event that's clearly one class's), for the
        /// week on Home or the days ahead on a class's home; null without calendars, and then none are shown.</summary>
        public IReadOnlyList<Meeting>? Meetings { get; init; }
        public int Unsorted { get; init; }
        public int Writing { get; init; }
        public Action<string>? OpenClass { get; init; }
        /// <summary>A class's lectures by week (its All lectures), rather than its home.</summary>
        public Action<string>? OpenAllLectures { get; init; }
        /// <summary>Opens a lecture (its class, its id) beside its class's list.</summary>
        public Action<string, string>? OpenLecture { get; init; }
        public Action<string, string>? OpenAssignment { get; init; }
        public Action? OpenDueList { get; init; }
        /// <summary>Due's home (Home's See all and To hand in lead there).</summary>
        public Action? OpenDueHome { get; init; }
        public Action? OpenUnsorted { get; init; }
    }

    /// <summary>A class meeting on a calendar: when, and which class.</summary>
    public sealed record Meeting(DateTimeOffset Start, DateTimeOffset End, string Class);

    /// <summary>One lecture as the library lists it.</summary>
    public sealed record LectureFacts(string Id, string Title, string Class, DateTimeOffset? Date, double? Seconds, string Summary, bool Writing);

    static DateTimeOffset Local(DateTimeOffset d, Sources s) => TimeZoneInfo.ConvertTime(d, s.Zone);

    static string Words(DateTimeOffset d, string format) => d.ToString(format, CultureInfo.InvariantCulture);

    /// <summary>A lecture's or a deadline's day: "Today", "Yesterday" (or "Tomorrow"), then "Wed 7 Oct" within the
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

    /// <summary>A due time: "11:59 PM".</summary>
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

    /// <summary>Late enough for the week's Late column: overdue, from a day before today (something due earlier today
    /// stays on today, with its time).</summary>
    static bool LateBefore(CanvasApi.Item i, Sources s) =>
        Overdue(i, s.Now) && (i.DueAt is not { } d || Local(d, s).Date < Local(s.Now, s).Date);

    /// <summary>A deadline as a row: its day's column in the week shows only the time; a list says the day too.</summary>
    OverviewDue DueRow(CanvasApi.Item i, Sources s, Func<string, IBrush> dot, bool inWeek, bool done = false)
    {
        bool late = !done && LateBefore(i, s);
        string when = done ? "Handed in"
            : i.DueAt is not { } d ? "No due date"
            : late ? $"Was due {Words(Local(d, s), Local(d, s).Year == Local(s.Now, s).Year ? "ddd d" : "d MMM yyyy")}"
            : inWeek ? Time(d, s.Zone)
            : $"{Day(d, s.Now, s.Zone)}, {Time(d, s.Zone)}";
        return new OverviewDue(i.Class, i.Id, i.Name, when, late, done, IsClass ? "" : ClassNames.Display(i.Class), dot(i.Class),
            () => s.OpenAssignment?.Invoke(i.Class, i.Id));
    }

    OverviewLecture LectureRow(LectureFacts l, Sources s, Func<string, IBrush> dot) => new(l.Id, l.Title,
        l.Date is { } d ? Day(d, s.Now, s.Zone) : "", l.Seconds is { } sec ? Core.TimedText.Length(sec) : "",
        l.Writing ? "Writing summary…" : PlainMath.Inline(l.Summary), l.Writing, IsHome ? ClassNames.Display(l.Class) : "", dot(l.Class),
        () => s.OpenLecture?.Invoke(l.Class, l.Id));

    static Func<string, IBrush> DotsOf(Sources s)
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

    /// <summary>
    /// Home's one sentence, the most pressing thing first: what's due next and when ("CCHW2 is due tonight at 11:59
    /// PM.", "Two assignments due Wednesday"), then what's late ("…, and one is late."); with nothing due this week
    /// it says so. Without Canvas there's nothing to hand in to tell of, so it counts the library instead.
    /// </summary>
    public static string HomeLede(Sources s, bool canvas)
    {
        if (!canvas)
        {
            int lectures = s.Classes.Sum(c => c.Lectures) + s.Unsorted;
            if (lectures == 0) return "No lectures yet. Record one and its notes land here.";
            string have = $"{Count(lectures)} lecture{(lectures == 1 ? "" : "s")} in {Count(s.Classes.Count).ToLowerInvariant()} class{(s.Classes.Count == 1 ? "" : "es")}";
            return s.Writing > 0 ? $"{have}, and notes being written for {Count(s.Writing).ToLowerInvariant()}." : have + ".";
        }
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
                first = $"{that[0].Name} is due {day1} at {Words(at, "h:mm tt")}";
            }
            else first = $"{Count(that.Count)} {Kind(that, plural: true)} due {on}";
        }
        string then = late.Count switch
        {
            0 => "",
            1 => first.Length > 0 ? $"and {late[0].Name} is late" : $"{late[0].Name} is late",
            _ => first.Length > 0 ? $"and {Count(late.Count).ToLowerInvariant()} are late" : $"{Count(late.Count)} {Kind(late, plural: true)} are late",
        };
        if (first.Length == 0 && then.Length == 0) return "Nothing due this week.";
        return first.Length > 0 && then.Length > 0 ? $"{first}, {then}." : $"{first}{then}.";
    }

    /// <summary>
    /// Home's week: what's late (a column of its own, only when something is), then Monday to Sunday, each with its
    /// class meetings (from the student's calendars; none are made up without them) over what's due that day. A
    /// handed-in thing stays on its day, struck through. More than <see cref="DayRows"/> due on a day shows one fewer
    /// and "+2 more", which opens Due.
    /// </summary>
    void BuildWeek(Sources s, Func<string, IBrush> dot, bool canvas)
    {
        var today = Local(s.Now, s).Date;
        var monday = Monday(s);
        var open = canvas ? StillToHandIn(s.Due).ToList() : [];
        var done = canvas ? HandedIn(s.Due).ToList() : [];
        var late = open.Where(i => LateBefore(i, s)).OrderBy(i => i.DueAt ?? DateTimeOffset.MinValue).ToList();
        var days = new List<WeekDay>();
        if (late.Count > 0)
        {
            var column = new WeekDay { Name = "Late", IsLate = true, Weight = 1.15 };
            Fill(column, [.. late.Select(i => DueRow(i, s, dot, inWeek: true))]);
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
            foreach (var m in (s.Meetings ?? []).Where(m => Local(m.Start, s).Date == date).OrderBy(m => m.Start))
                day.Meetings.Add(new WeekMeeting(Words(Local(m.Start, s), "h:mm"), ClassNames.Short(m.Class), dot(m.Class),
                    s.Lectures.Any(l => l.Class == m.Class && l.Date is { } at && at >= m.Start.AddMinutes(-45) && at <= m.End.AddMinutes(45))));
            var due = open.Where(i => !LateBefore(i, s) && i.DueAt is { } d && Local(d, s).Date == date).Select(i => (Item: i, Done: false))
                .Concat(done.Where(i => i.DueAt is { } d && Local(d, s).Date == date).Select(i => (Item: i, Done: true)))
                .OrderBy(x => x.Item.DueAt).Select(x => DueRow(x.Item, s, dot, inWeek: true, done: x.Done)).ToList();
            Fill(day, due);
            days.Add(day);
        }
        for (int i = 0; i < days.Count; i++) days[i].Ruled = i > 0 && !days[i].IsToday && !days[i - 1].IsToday;
        foreach (var d in days) Week.Add(d);

        void Fill(WeekDay day, List<OverviewDue> due)
        {
            int show = due.Count > DayRows ? DayRows - 1 : due.Count;
            day.Deadlines.AddRange(due.Take(show));
            if (due.Count > show)
            {
                day.More = $"+{due.Count - show} more";
                day.OpenMore = s.OpenDueHome ?? s.OpenDueList;
            }
        }
    }

    /// <summary>Home: every class at once.</summary>
    public static OverviewModel Home(Sources s)
    {
        var dot = DotsOf(s);
        bool canvas = s.Due is not null && s.Canvas.Any(c => c.Linked);
        int lectures = s.Classes.Sum(c => c.Lectures) + s.Unsorted;
        // Nothing left to hand in this week: the week says so in words, and what comes next.
        var monday = Monday(s);
        var open = canvas ? StillToHandIn(s.Due).OrderBy(i => i.DueAt ?? DateTimeOffset.MaxValue).ToList() : [];
        bool quiet = canvas && !open.Any(i => LateBefore(i, s) || (i.DueAt is { } d && Local(d, s).Date < monday.AddDays(7)));
        var next = quiet ? open.FirstOrDefault(i => i.DueAt is not null) : null;
        var m = new OverviewModel
        {
            IsHome = true,
            Title = Words(Local(s.Now, s), "dddd, MMMM d"),
            Lede = HomeLede(s, canvas),
            HasCanvas = canvas,
            OnAllDue = canvas ? s.OpenDueHome ?? s.OpenDueList : null,
            AllLecturesWords = lectures > Rows ? $"All {lectures} lectures" : "",
            LecturesEmpty = $"No lectures yet. Record one from the {(OperatingSystem.IsMacOS() ? "menu bar" : "tray")} and its notes land here.",
            WeekEmpty = quiet ? "Nothing due this week." : "",
            WeekNext = next is null ? "" : $"Next up is {next.Name} for {ClassNames.Display(next.Class)}, due {Day(next.DueAt!.Value, s.Now, s.Zone)}.",
        };
        // The week needs something to show: what's due (Canvas) or when classes meet (calendars).
        if (canvas || s.Meetings is { Count: > 0 }) m.BuildWeek(s, dot, canvas);
        foreach (var l in s.Lectures.Take(Rows)) m.Lectures.Add(m.LectureRow(l, s, dot));
        return m;
    }

    /// <summary>
    /// Due's home: what's to hand in across every class, day by day, late first, then what has no date. The
    /// two-column list (each assignment beside its detail) is its Full list.
    /// </summary>
    public static OverviewModel ForDue(Sources s)
    {
        var dot = DotsOf(s);
        var toHandIn = StillToHandIn(s.Due).OrderBy(i => i.DueAt ?? DateTimeOffset.MaxValue).ToList();
        int late = toHandIn.Count(i => LateBefore(i, s));
        var m = new OverviewModel
        {
            IsDuePage = true,
            Title = "Due",
            Lede = toHandIn.Count == 0 ? "Nothing to hand in. You're all caught up."
                : late > 0 ? $"{Count(toHandIn.Count)} to hand in, {Count(late).ToLowerInvariant()} of them late." : $"{Count(toHandIn.Count)} to hand in.",
            HasCanvas = s.Due is not null,
            OnAllDue = s.OpenDueList,
        };
        void Group(string heading, IEnumerable<CanvasApi.Item> items, bool isLate = false)
        {
            var rows = items.Select(i => m.DueRow(i, s, dot, inWeek: !isLate && i.DueAt is not null)).ToList();
            if (rows.Count > 0) m.DueGroups.Add(new OverviewDueGroup(heading, rows, isLate));
        }
        Group("Late", toHandIn.Where(i => LateBefore(i, s)), isLate: true);
        foreach (var day in toHandIn.Where(i => !LateBefore(i, s) && i.DueAt is not null).GroupBy(i => Local(i.DueAt!.Value, s).Date))
            Group(Day(new DateTimeOffset(day.Key, s.Zone.GetUtcOffset(day.Key)), s.Now, s.Zone), day);
        Group("No due date", toHandIn.Where(i => !LateBefore(i, s) && i.DueAt is null));
        return m;
    }

    /// <summary>A class's home: that class alone, with the ways further in.</summary>
    public static OverviewModel ForClass(ClassHomeFacts c, Sources s)
    {
        var dot = DotsOf(s);
        bool canvas = c.Canvas is not null;
        // The lede: its code, how many lectures, and when it next meets.
        var parts = new List<string>();
        string code = c.Code ?? ClassNames.Code(c.Name);
        // (A class that goes by its code, "CS 101", needs no code under its name.)
        if (code.Length > 0 && !string.Equals(code, ClassNames.Display(c.Name), StringComparison.OrdinalIgnoreCase)) parts.Add(code);
        parts.Add(CanvasWords.LectureCountText(c.Lectures));
        if ((s.Meetings ?? []).Where(e => e.Class == c.Name && e.End > s.Now).OrderBy(e => e.Start).FirstOrDefault() is { } meets)
            parts.Add(meets.Start <= s.Now ? "meeting now" : $"next meets {Day(meets.Start, s.Now, s.Zone).ToLowerInvariant() switch { "today" => "today", "tomorrow" => "tomorrow", _ => Day(meets.Start, s.Now, s.Zone) }} at {Words(Local(meets.Start, s), "h:mm")}");
        var m = new OverviewModel
        {
            IsHome = false,
            ClassName = c.Name,
            Title = ClassNames.Display(c.Name),
            Dot = c.Dot,
            Lede = string.Join(" · ", parts),
            Description = c.Description ?? "",
            HasCanvas = canvas,
            OnAllDue = s.OpenDueHome ?? s.OpenDueList,
            OnAllLectures = () => (s.OpenAllLectures ?? s.OpenClass)?.Invoke(c.Name),
            AllLecturesWords = c.Lectures > 0 ? c.Lectures == 1 ? "The lecture by week" : $"All {c.Lectures} lectures" : "",
            LecturesEmpty = $"No lectures in {ClassNames.Display(c.Name)} yet. Record one and it lands here.",
        };
        foreach (var i in StillToHandIn(s.Due).Where(i => i.Class == c.Name).OrderBy(i => i.DueAt ?? DateTimeOffset.MaxValue).Take(6))
            m.Due.Add(m.DueRow(i, s, dot, inWeek: false));
        foreach (var l in s.Lectures.Where(l => l.Class == c.Name).Take(Rows)) m.Lectures.Add(m.LectureRow(l, s, dot));
        return m;
    }

    /// <summary>A class's ways further in: on Canvas, its tabs (its lectures are under the lectures' own link).</summary>
    public void AddLinks(ClassHomeFacts c, Action allLectures, Action<ClassTab>? tab)
    {
        if (c.Canvas is not { } k || tab is null) return;
        Links.Add(new OverviewLink("Assignments", $"{k.ToHandIn} to hand in · {k.Done} done", () => tab(ClassTab.Assignments)));
        Links.Add(new OverviewLink("Modules", k.Modules == 1 ? "1 module" : $"{k.Modules} modules", () => tab(ClassTab.Modules)));
        Links.Add(new OverviewLink("Announcements", k.AnnouncementsNew > 0 ? $"{k.Announcements} · {k.AnnouncementsNew} new" : $"{k.Announcements}",
            () => tab(ClassTab.Announcements)));
        OnPropertyChanged(nameof(HasLinks));
    }
}
