using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StudyStash.App.Services;

namespace StudyStash.App.ViewModels;

/// <summary>Something due, on its day in the calendar: its name (marked with its class's highlighter), a line under it
/// only when there's something to say ("Tonight 11:59 PM", a time that isn't the end of the day, "Missing"), and
/// whether it was handed in (then it's struck through).</summary>
public sealed record CalendarItem(string Class, string Id, string Title, string Sub, bool Missing, bool Done, IBrush Dot, string Tip, string Said, Action Open)
{
    public bool HasSub => Sub.Length > 0;
}

/// <summary>One day of the month's grid: its number ("12", or "1 Oct" on a month's first day), whether it's today or
/// outside the month showing, and what's due on it: two shown, the rest behind "2 more".</summary>
public sealed class CalendarCell
{
    public DateTime Date { get; init; }
    public string Number { get; init; } = "";
    public bool IsToday { get; init; }
    public bool IsOut { get; init; }
    /// <summary>The hairlines on its right and under it: every cell but the last column's and the last row's.</summary>
    public Avalonia.Thickness Rules { get; init; }
    public List<CalendarItem> Shown { get; } = [];
    public List<CalendarItem> More { get; } = [];
    public bool HasMore => More.Count > 0;
    public string MoreWords => $"{More.Count} more";
}

/// <summary>A class to see the calendar for, in its menu: every class, or one of them.</summary>
public sealed record CalendarFilter(string? Class, string Name, IBrush? Dot, bool Checked, IRelayCommand Pick)
{
    public bool HasDot => Dot is not null;
}

/// <summary>
/// Due as a calendar: the month showing (or one week of it), each day with what's due on it, for every class or one.
/// What's late and still to hand in says "Missing" in red on the day it was due; what was handed in is struck through
/// in its class's colour. Built from what the shell hands it (Canvas's Due list, and each class's whole term);
/// nothing here fetches.
/// </summary>
public sealed partial class DueCalendarModel : ObservableObject
{
    /// <summary>How many things a day shows before the rest fold into "2 more".</summary>
    public const int DayRows = 2;

    readonly OverviewModel.Sources sources;
    readonly Func<string, IBrush> dot;
    readonly List<(CanvasApi.Item Item, bool Done)> all;

    public DueCalendarModel(OverviewModel.Sources s)
    {
        sources = s;
        var dots = s.Classes.GroupBy(c => c.Name).ToDictionary(g => g.Key, g => g.First().Dot);
        dot = name => dots.TryGetValue(name, out var d) ? d : Brushes.Gray;
        all = [.. (s.Due?.Groups ?? []).SelectMany(g => g.Items.Select(i => (i, Done: g.Key == "handed_in" || !CanvasWords.ToHandIn(i))))
            .Where(x => x.i.DueAt is not null).GroupBy(x => (x.i.Class, x.i.Id)).Select(g => g.First())];
        var today = Today;
        month = new DateTime(today.Year, today.Month, 1);
        week = today.AddDays(-(((int)today.DayOfWeek + 6) % 7));
        Build();
    }

    DateTime Today => TimeZoneInfo.ConvertTime(sources.Now, sources.Zone).Date;
    DateTimeOffset Local(DateTimeOffset d) => TimeZoneInfo.ConvertTime(d, sources.Zone);

    DateTime month, week;

    /// <summary>The whole month (false) or one week of it, as the planner (true).</summary>
    [ObservableProperty] public partial bool WeekView { get; set; }
    public bool MonthView => !WeekView;
    /// <summary>The class the calendar is for; null for every class.</summary>
    [ObservableProperty] public partial string? Class { get; set; }

    public ObservableCollection<CalendarCell> Cells { get; } = [];
    public ObservableCollection<WeekDay> Week { get; } = [];
    public ObservableCollection<CalendarFilter> Filters { get; } = [];
    /// <summary>Mon to Sun: the grid's heading.</summary>
    public string[] Days { get; } = ["Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun"];
    /// <summary>The grid's rows this month: five, or six when the month runs over.</summary>
    public int Rows => Math.Max(1, Cells.Count / 7);

    /// <summary>"October 2026", or in the week view the week's days: "12 – 18 October".</summary>
    public string Heading => !WeekView ? month.ToString("MMMM yyyy", CultureInfo.InvariantCulture)
        : week.Month == week.AddDays(6).Month ? $"{week.Day} – {week.AddDays(6).Day} {week.ToString("MMMM", CultureInfo.InvariantCulture)}"
        : $"{week.ToString("d MMM", CultureInfo.InvariantCulture)} – {week.AddDays(6).ToString("d MMM", CultureInfo.InvariantCulture)}";
    public string PreviousTip => WeekView ? "Previous week" : "Previous month";
    public string NextTip => WeekView ? "Next week" : "Next month";
    public string FilterName => Class is null ? "All classes" : ClassNames.Display(Class);

    partial void OnWeekViewChanged(bool value)
    {
        OnPropertyChanged(nameof(MonthView));
        Build();
    }

    partial void OnClassChanged(string? value) => Build();

    [RelayCommand]
    void Previous()
    {
        if (WeekView) week = week.AddDays(-7);
        else month = month.AddMonths(-1);
        Build();
    }

    [RelayCommand]
    void Next()
    {
        if (WeekView) week = week.AddDays(7);
        else month = month.AddMonths(1);
        Build();
    }

    [RelayCommand]
    void GoToday()
    {
        var today = Today;
        month = new DateTime(today.Year, today.Month, 1);
        week = today.AddDays(-(((int)today.DayOfWeek + 6) % 7));
        Build();
    }

    /// <summary>Carries on where another calendar was (the page was only read again): its month, its week, and
    /// whether it showed the month or the week.</summary>
    public void Resume(DueCalendarModel before)
    {
        month = before.month;
        week = before.week;
        if (WeekView != before.WeekView) WeekView = before.WeekView;
        else Build();
    }

    [RelayCommand] void ShowMonth() => WeekView = false;
    [RelayCommand] void ShowWeek() => WeekView = true;
    [RelayCommand] static void Open(CalendarItem item) => item.Open();
    [RelayCommand] static void OpenDue(OverviewDue item) => item.Open();

    IEnumerable<(CanvasApi.Item Item, bool Done)> Showing => all.Where(x => Class is null || x.Item.Class == Class);

    /// <summary>Past its day and not handed in: it says so, in words, on the day it was due.</summary>
    bool IsMissing(CanvasApi.Item i, bool done) => !done && (i.Missing || i.Status is "missing" or "overdue" or "past due" || Local(i.DueAt!.Value).Date < Today);

    CalendarItem ItemFor(CanvasApi.Item i, bool done)
    {
        var at = Local(i.DueAt!.Value);
        bool missing = IsMissing(i, done);
        string time = at.ToString("h:mm tt", CultureInfo.InvariantCulture);
        // The end of the day is when most things are due, so it goes unsaid, except today: "Tonight 11:59 PM".
        string sub = missing ? "Missing" : done ? ""
            : at.Date == Today ? $"{(at.Hour >= 17 ? "Tonight" : "Today")} {time}"
            : at is { Hour: 23, Minute: 59 } ? "" : time;
        string cls = ClassNames.Display(i.Class);
        string state = done ? "Handed in" : missing ? "Missing" : "";
        string said = string.Join(", ", new[] { i.Name, cls, $"due {OverviewModel.Day(i.DueAt!.Value, sources.Now, sources.Zone)} at {time}", state }.Where(w => w.Length > 0));
        return new CalendarItem(i.Class, i.Id, i.Name, sub, missing, done, dot(i.Class), $"{i.Name}, {cls}", said, () => sources.OpenAssignment?.Invoke(i.Class, i.Id));
    }

    void Build()
    {
        var today = Today;
        Cells.Clear();
        Week.Clear();
        if (WeekView)
        {
            foreach (var day in OverviewModel.WeekOf(sources, dot, week, Class)) Week.Add(day);
        }
        else
        {
            // The grid starts on the Monday on or before the 1st and runs whole weeks to the month's end.
            var first = month.AddDays(-(((int)month.DayOfWeek + 6) % 7));
            var last = month.AddMonths(1).AddDays(-1);
            int rows = (int)Math.Ceiling(((last - first).Days + 1) / 7.0);
            var byDay = Showing.GroupBy(x => Local(x.Item.DueAt!.Value).Date).ToDictionary(g => g.Key, g => g.OrderBy(x => x.Item.DueAt).ToList());
            for (int n = 0; n < rows * 7; n++)
            {
                var date = first.AddDays(n);
                var cell = new CalendarCell
                {
                    Date = date, IsToday = date == today, IsOut = date.Month != month.Month,
                    Number = date.Day == 1 ? date.ToString("d MMM", CultureInfo.InvariantCulture) : date.Day.ToString(CultureInfo.InvariantCulture),
                    Rules = new Avalonia.Thickness(0, 0, n % 7 == 6 ? 0 : 1, n / 7 == rows - 1 ? 0 : 1),
                };
                if (byDay.TryGetValue(date, out var due))
                {
                    var items = due.Select(x => ItemFor(x.Item, x.Done)).ToList();
                    // Three on a day would be two and "1 more"; show them all only when they fit as two.
                    cell.Shown.AddRange(items.Take(DayRows));
                    cell.More.AddRange(items.Skip(DayRows));
                }
                Cells.Add(cell);
            }
        }
        Filters.Clear();
        Filters.Add(new CalendarFilter(null, "All classes", null, Class is null, new RelayCommand(() => Class = null)));
        foreach (string name in sources.Classes.Select(c => c.Name).Where(n => all.Any(x => x.Item.Class == n)))
        {
            string cls = name;
            Filters.Add(new CalendarFilter(cls, ClassNames.Display(cls), dot(cls), Class == cls, new RelayCommand(() => Class = cls)));
        }
        OnPropertyChanged(nameof(Rows));
        OnPropertyChanged(nameof(Heading));
        OnPropertyChanged(nameof(PreviousTip));
        OnPropertyChanged(nameof(NextTip));
        OnPropertyChanged(nameof(FilterName));
    }
}
