using System.Globalization;
using System.Text.Json.Nodes;

namespace StudyStash.Core.Calendar;

/// <summary>
/// What's coming up, as the library knows it (calendar.json beside config.toml): the laptop that records sends its next
/// seven days of events (calendars run there, not here), and the library serves them to its web pages and the phone
/// with the class each is for, matched against the library's own classes and their Canvas codes. The laptop that
/// sent last is the one shown.
/// </summary>
public static class LibraryCalendar
{
    /// <summary>The most events one laptop's week can hold; more is a mistake, cut rather than kept.</summary>
    public const int MaxEvents = 500;

    public static string PathIn(string home) => Path.Combine(home, "calendar.json");

    static readonly Lock Gate = new();

    /// <summary>Keep a laptop's events ({events:[{id,title,start,end,allDay,location,class}]}): each with an id, a
    /// title and a start and end that read as times; anything else is left out. The number kept.</summary>
    public static int Save(string home, JsonObject? body, DateTimeOffset now)
    {
        var kept = new JsonArray();
        foreach (var e in (body?["events"] as JsonArray ?? []).OfType<JsonObject>().Take(MaxEvents))
        {
            string id = Str(e["id"]), title = Str(e["title"]).Trim();
            if (id.Length == 0 || Time(e["start"]) is not { } start || Time(e["end"]) is not { } end || end < start) continue;
            string location = Str(e["location"]).Trim();
            string cls = Str(e["class"]).Trim();
            kept.Add(new JsonObject
            {
                ["id"] = id, ["title"] = title.Length > 0 ? title : "(No title)",
                ["start"] = Iso(start), ["end"] = Iso(end), ["allDay"] = e["allDay"] is JsonValue v && v.TryGetValue(out bool b) && b,
                ["location"] = location.Length > 0 ? location : null, ["class"] = cls.Length > 0 ? cls : null,
            });
        }
        lock (Gate)
        {
            Directory.CreateDirectory(home);
            SharedFile.Write(PathIn(home), new JsonObject { ["updated"] = Iso(now), ["events"] = kept }.ToJsonString() + "\n");
        }
        return kept.Count;
    }

    /// <summary>
    /// {events:[{id,title,start,end,allDay,location,class}], updated}: the kept events not over yet, soonest first,
    /// each with its class (the library's match for its title, else the class the laptop matched when the library has
    /// it, else null). <c>updated</c> is null before any laptop has sent any.
    /// </summary>
    public static JsonObject Read(string home, IReadOnlyList<ClassHint> classes, DateTimeOffset now)
    {
        JsonObject? saved;
        lock (Gate) saved = SharedFile.Read(PathIn(home)) is { } text ? Parse(text) : null;
        var names = classes.Select(c => c.Name).ToHashSet();
        var events = (saved?["events"] as JsonArray ?? []).OfType<JsonObject>()
            .Select(e => (e, Start: Time(e["start"]), End: Time(e["end"])))
            .Where(x => x.Start is not null && x.End is not null && (x.End > now || (x.End == x.Start && x.Start >= now)))
            .OrderBy(x => x.Start)
            .Select(x =>
            {
                string title = Str(x.e["title"]);
                string sent = Str(x.e["class"]);
                string? cls = EventClass.For(title, classes) ?? (names.Contains(sent) ? sent : null);
                return (JsonNode)new JsonObject
                {
                    ["id"] = Str(x.e["id"]), ["title"] = title, ["start"] = Str(x.e["start"]), ["end"] = Str(x.e["end"]),
                    ["allDay"] = x.e["allDay"] is JsonValue v && v.TryGetValue(out bool b) && b,
                    ["location"] = Str(x.e["location"]) is { Length: > 0 } l ? l : null, ["class"] = cls,
                };
            }).ToArray();
        return new JsonObject { ["events"] = new JsonArray(events), ["updated"] = saved?["updated"]?.DeepClone() };
    }

    /// <summary>A laptop's week as it sends it: its events from now on, with the class it matched for each.</summary>
    public static JsonObject Body(IEnumerable<CalendarEvent> events, IReadOnlyList<ClassHint> classes) => new()
    {
        ["events"] = new JsonArray([.. events.Select(e => (JsonNode)new JsonObject
        {
            ["id"] = $"{e.SourceId}|{e.Id}", ["title"] = e.Title, ["start"] = Iso(e.Start), ["end"] = Iso(e.End), ["allDay"] = e.AllDay,
            ["location"] = e.Location, ["calendar"] = e.CalendarId, ["class"] = EventClass.For(e.Title, classes),
        })]),
    };

    static JsonObject? Parse(string text)
    {
        try
        {
            return JsonNode.Parse(text) as JsonObject;
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }

    static string Str(JsonNode? n) => n is JsonValue v && v.TryGetValue(out string? s) ? s : "";

    static DateTimeOffset? Time(JsonNode? n) =>
        DateTimeOffset.TryParse(Str(n), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var t) ? t : null;

    static string Iso(DateTimeOffset t) => t.ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture);
}
