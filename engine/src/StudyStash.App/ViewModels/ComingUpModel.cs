using System.Collections.ObjectModel;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using StudyStash.Core.Calendar;

namespace StudyStash.App.ViewModels;

/// <summary>One event in Coming up: when, what, where, and the class it's for with that class's dot.</summary>
public sealed record ComingUpRow(string Id, string When, string Title, string? Location, string? ClassName, IBrush Dot, bool Now)
{
    public bool HasClass => ClassName is not null;
    public bool HasLocation => !string.IsNullOrEmpty(Location);
    /// <summary>"CS 101 · Engineering Hall 204", or whichever of the two there is.</summary>
    public string Detail => string.Join(" · ", new[] { ClassName, Location }.Where(s => !string.IsNullOrEmpty(s)));
    public bool HasDetail => Detail.Length > 0;
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
            return new ComingUpRow(e.Id, CalendarWords.When(e, now, zone), e.Title, e.Location, cls, brush ?? Brushes.Gray, e.Start <= now);
        })];

    /// <summary>Show these rows, changing the list only when they changed (so the dropdown doesn't flicker).</summary>
    public void Show(bool hasCalendars, IReadOnlyList<ComingUpRow> rows)
    {
        HasCalendars = hasCalendars;
        if (Rows.Count == rows.Count && Rows.Zip(rows).All(p => p.First.Id == p.Second.Id && p.First.When == p.Second.When && p.First.Title == p.Second.Title
            && p.First.ClassName == p.Second.ClassName && p.First.Location == p.Second.Location)) return;
        Rows.Clear();
        foreach (var r in rows) Rows.Add(r);
    }
}
