using System.Collections.ObjectModel;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using StudyStash.Core.Calendar;

namespace StudyStash.App.ViewModels;

/// <summary>One event in Coming up: when, what, where, and the class it's for with that class's dot. In the library
/// window's sidebar it's a button that records it, with a short line for when ("In 11 minutes", "Tomorrow 8:45 AM").</summary>
public sealed record ComingUpRow(string Id, string When, string Title, string? Location, string? ClassName, IBrush Dot, bool Now, string Short = "", bool Soon = false)
{
    public bool HasClass => ClassName is not null;
    public bool HasLocation => !string.IsNullOrEmpty(Location);
    /// <summary>"CS 101 · Engineering Hall 204", or whichever of the two there is.</summary>
    public string Detail => string.Join(" · ", new[] { ClassName, Location }.Where(s => !string.IsNullOrEmpty(s)));
    public bool HasDetail => Detail.Length > 0;
    /// <summary>The event by the name the sidebar uses: a class's own event without the registrar's code.</summary>
    public string Shown => ClassName is { } c && string.Equals(Title.Trim(), c, StringComparison.OrdinalIgnoreCase) ? ClassNames.Display(c) : Title;
    /// <summary>What its button says to a screen reader: "Record Capstone team sync, starts in 11 minutes".</summary>
    public string Said => $"Record {Shown}, {(Now ? "on now" : Soon ? "starts " + Short.ToLowerInvariant() : Spoken(Short))}";

    /// <summary>"Tomorrow 8:45 AM" as it's said: "tomorrow at 8:45 AM".</summary>
    static string Spoken(string brief) => brief.IndexOf(' ') is int sp and > 0 ? $"{brief[..sp].ToLowerInvariant()} at {brief[(sp + 1)..]}" : brief;
}

/// <summary>
/// "Coming up" in the menu bar dropdown (or tray panel) and the library window: the next few events today and
/// tomorrow from the student's calendars, each with the class it's for. Hidden while there are no calendars; with
/// calendars and nothing left today or tomorrow, it says so.
/// </summary>
public sealed partial class ComingUpModel : ObservableObject
{
    public ObservableCollection<ComingUpRow> Rows { get; } = [];
    /// <summary>The student has calendars: the list shows, even when it's empty.</summary>
    [ObservableProperty] public partial bool HasCalendars { get; set; }

    /// <summary>The sidebar's two: the next events, each a button that records it.</summary>
    public ObservableCollection<ComingUpRow> Next { get; } = [];
    /// <summary>How many the sidebar shows.</summary>
    public const int SidebarRows = 2;
    /// <summary>Starts recording an event (its id) into its folder.</summary>
    public Action<string>? OnRecord { get; set; }
    [CommunityToolkit.Mvvm.Input.RelayCommand] void Record(ComingUpRow row) => OnRecord?.Invoke(row.Id);

    public bool HasRows => Rows.Count > 0;
    public bool IsEmpty => HasCalendars && Rows.Count == 0;
    public string Heading => CalendarWords.ComingUp;
    public string EmptyText => CalendarWords.NothingComingUp;

    public ComingUpModel() => Rows.CollectionChanged += (_, _) =>
    {
        OnPropertyChanged(nameof(HasRows));
        OnPropertyChanged(nameof(IsEmpty));
    };

    partial void OnHasCalendarsChanged(bool value) => OnPropertyChanged(nameof(IsEmpty));

    /// <summary>The rows for these events: the soonest <paramref name="count"/> today and tomorrow. <paramref name="dot"/>
    /// is a class's dot (null for a class the library doesn't have, which shows no class).</summary>
    public static List<ComingUpRow> For(IEnumerable<CalendarEvent> events, DateTimeOffset now, TimeZoneInfo zone, IReadOnlyList<ClassHint> classes,
        Func<string, IBrush?> dot, int count = 4) =>
        [.. Upcoming.Coming(events, now, zone, count).Select(e =>
        {
            string? cls = EventClass.For(e.Title, classes);
            var brush = cls is null ? null : dot(cls);
            if (brush is null) cls = null;
            var at = TimeZoneInfo.ConvertTime(e.Start, zone);
            bool on = e.Start <= now, soon = !on && e.Start - now <= OverviewModel.Soon;
            int minutes = Math.Max(1, (int)Math.Ceiling((e.Start - now).TotalMinutes));
            string time = at.ToString("h:mm tt", System.Globalization.CultureInfo.InvariantCulture);
            string brief = on ? "Now" : soon ? minutes == 1 ? "In a minute" : $"In {minutes} minutes"
                : $"{(at.Date == TimeZoneInfo.ConvertTime(now, zone).Date ? "Today" : "Tomorrow")} {time}";
            return new ComingUpRow(e.Id, CalendarWords.When(e, now, zone), e.Title, e.Location, cls, brush ?? Brushes.Gray, on, brief, soon || on);
        })];

    /// <summary>Show these rows, changing the list only when they changed (so the dropdown doesn't flicker).</summary>
    public void Show(bool hasCalendars, IReadOnlyList<ComingUpRow> rows)
    {
        HasCalendars = hasCalendars;
        if (Rows.Count == rows.Count && Rows.Zip(rows).All(p => p.First.Id == p.Second.Id && p.First.When == p.Second.When && p.First.Title == p.Second.Title
            && p.First.ClassName == p.Second.ClassName && p.First.Location == p.Second.Location && p.First.Short == p.Second.Short)) return;
        Rows.Clear();
        foreach (var r in rows) Rows.Add(r);
        Next.Clear();
        foreach (var r in rows.Take(SidebarRows)) Next.Add(r);
    }
}
