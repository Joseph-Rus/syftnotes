using System.Globalization;
using System.Text.Json.Nodes;

namespace StudyStash.Core.Calendar;

/// <summary>
/// The calendar event a lecture was recorded during, as the lecture keeps it and sends it to the library in its raw
/// payload ("event"): its id, title, times (ISO), place, who was invited, which calendar it's on, and its link.
/// </summary>
public sealed record RecordedEvent(string Id, string Title, string Start, string End, string? Location, List<string> Attendees, string Calendar, string? Url)
{
    public static RecordedEvent From(CalendarEvent e, string calendarName) => new(e.Id, e.Title,
        e.Start.ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture), e.End.ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture),
        e.Location, [.. e.Attendees], calendarName, e.Url);

    /// <summary>What goes in the lecture's raw payload.</summary>
    public JsonObject Json() => new()
    {
        ["id"] = Id, ["title"] = Title, ["start"] = Start, ["end"] = End, ["location"] = Location,
        ["attendees"] = new JsonArray([.. Attendees.Select(a => (JsonNode?)a)]), ["calendar"] = Calendar, ["url"] = Url,
    };
}

/// <summary>What a recording starts as: its class, its title (empty lets the library name it), and its event.</summary>
public sealed record RecordingStart(string ClassName, string Title, RecordedEvent? Event);

/// <summary>
/// A recording started during an event (or within ten minutes of one) takes its title from the event and its class
/// from the event's match, unless the student picked a class. A class picked by hand that isn't the event's (they're
/// in another lecture than their calendar says) wins, and the event is left out, so the lecture isn't misnamed.
/// </summary>
public static class RecordingEvents
{
    /// <summary>The start for a recording <paramref name="picked"/> for a class ("" when none was picked), while
    /// <paramref name="now"/> is on (null when nothing is). <paramref name="classes"/> are the library's; a matched
    /// class it doesn't have isn't used. <paramref name="calendarName"/> names the event's calendar.</summary>
    public static RecordingStart For(string picked, EventNow? now, IReadOnlyCollection<string> classes, Func<string, string> calendarName)
    {
        if (now is null) return new RecordingStart(picked, "", null);
        string? matched = now.Class is { } c && classes.Contains(c) ? c : null;
        if (picked.Length > 0 && matched is not null && matched != picked) return new RecordingStart(picked, "", null);
        return new RecordingStart(picked.Length > 0 ? picked : matched ?? "", now.Event.Title, RecordedEvent.From(now.Event, calendarName(now.Event.CalendarId)));
    }

    /// <summary>A calendar's name from calendars.json's list, or "" when it isn't there.</summary>
    public static Func<string, string> Names(CalendarSettings s) => id => s.Calendars.FirstOrDefault(c => c.Id == id)?.Name ?? "";
}
