namespace StudyStash.Core.Calendar;

/// <summary>One calendar a source has ("Classes", "Work", "Canvas"): its id (unique across every source), the source
/// it comes from, its name and colour ("#RRGGBB", or null when the source doesn't say), and whether the source
/// suggests showing it. Whether Study Stash shows it is the student's choice in Settings (<see cref="CalendarSettings"/>).</summary>
public sealed record CalendarInfo(string Id, string SourceId, string Name, string? Color, bool Enabled);

/// <summary>
/// One event on a calendar, as Study Stash sees it: a single occurrence (a class that meets every Tuesday is one of
/// these per Tuesday, each with its own <see cref="Id"/>), its start and end as moments in time, whether it's all day,
/// where it is, who's invited (names, or addresses when there's no name), a link (the meeting's, or the event's own
/// page), and whether it was called off. Cancelled events are dropped before the student sees them.
/// </summary>
public sealed record CalendarEvent(string Id, string CalendarId, string SourceId, string Title,
    DateTimeOffset Start, DateTimeOffset End, bool AllDay, string? Location, IReadOnlyList<string> Attendees,
    string? Url, bool Cancelled);

/// <summary>
/// Somewhere events come from: an iCalendar feed pasted in Settings (<see cref="Ics.IcsSource"/>), Apple Calendar,
/// a Google or Microsoft account. Each is made from its <see cref="CalendarSourceSettings"/> by its kind's
/// <see cref="CalendarKind"/>, and asked for its calendars and events by <see cref="Upcoming"/>.
/// </summary>
public interface ICalendarSource
{
    /// <summary>"ics:&lt;hash&gt;", "apple", "google:&lt;account&gt;", "microsoft:&lt;account&gt;".</summary>
    string Id { get; }
    /// <summary>"ics" | "apple" | "google" | "microsoft".</summary>
    string Kind { get; }
    /// <summary>Shown in Settings, e.g. the account's email.</summary>
    string Name { get; }
    Task<IReadOnlyList<CalendarInfo>> ListCalendarsAsync(CancellationToken ct);
    /// <summary>Every occurrence on these calendars that overlaps [<paramref name="from"/>, <paramref name="to"/>),
    /// recurring events expanded, cancelled ones included (marked <see cref="CalendarEvent.Cancelled"/>).</summary>
    Task<IReadOnlyList<CalendarEvent>> EventsAsync(IReadOnlyCollection<string> calendarIds,
        DateTimeOffset from, DateTimeOffset to, CancellationToken ct);
}
