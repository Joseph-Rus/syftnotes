using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using StudyStash.Core;
using StudyStash.Core.Calendar;
using StudyStash.Core.Canvas;

namespace StudyStash.Library;

/// <summary>
/// What Ask is told besides the notes, with every question: today's date and time, the Due list (overdue, this week,
/// the next few later, as the Due tab has them) and today's and tomorrow's calendar, all in the student's time. A
/// lecture's notes rarely say when something is due, and the AI can't know what day it is, so "what's due this
/// Sunday?" was answered from whatever a lecture happened to say.
/// </summary>
public sealed partial class LibraryWeb
{
    const int AgendaLater = 8;

    /// <summary>The agenda, for one class when Ask is scoped to it.</summary>
    internal string AskAgenda(string? onlyClass)
    {
        var zone = Canvas.Zone;
        var now = TimeZoneInfo.ConvertTime(Canvas.Clock(), zone);
        var sb = new StringBuilder();
        sb.Append("Today is ").Append(now.ToString("dddd d MMMM yyyy, h:mm tt", CultureInfo.InvariantCulture))
            .Append(", ").Append(CanvasMarkdown.ZoneName(zone, now)).Append(". Every time below is ").Append(CanvasMarkdown.LocalTimes(zone)).Append(".\n");
        bool Mine(JsonNode? row) => onlyClass is null || Py.AsString(row?["class"]) == onlyClass;
        // due_at is Canvas's UTC time; the row's "due" is already the student's clock time, so converting it again
        // put every due time hours out (8 hours early in Pacific time, and so "overdue" before it was). A list saved
        // before due_at was kept has only "due": said as it is.
        string When(JsonNode? row) =>
            CanvasMarkdown.When(Py.AsString(row?["due_at"]), zone) is { Length: > 0 } at ? at
            : DateTime.TryParse(Py.AsString(row?["due"]), CultureInfo.InvariantCulture, DateTimeStyles.None, out var clock)
                ? clock.ToString("ddd d MMM yyyy, h:mm tt", CultureInfo.InvariantCulture) : "no due date";
        // The student's own items (kind "mine") say so, and one with no class has none to name.
        bool Own(JsonNode? row) => Py.AsString(row?["kind"]) == DueMine.Kind;
        string Line(JsonNode? row, string prefix)
        {
            string cls = Py.AsString(row?["class"]) is { Length: > 0 } c ? c + ": " : "";
            string tag = Own(row) ? $" (own {Py.AsString(row?["what"]) switch { "exam" => "exam", "study" => "study session", _ => "to-do" }})"
                : row?["missing"]?.GetValue<bool>() == true ? " (missing)" : "";
            return $"- {prefix}{When(row)}: {cls}{Py.AsString(row?["name"])}{tag}";
        }
        try
        {
            // Canvas not being linked says nothing about the student's own items, so the list is read either way.
            bool linked = Canvas.Settings.Url.Length > 0;
            if (!linked) sb.Append("Canvas isn't linked to Syft Notes, so its due dates aren't known.\n");
            var groups = DueList()["groups"] as JsonArray ?? [];
            JsonArray Rows(string key) => groups.FirstOrDefault(g => Py.AsString(g?["key"]) == key)?["items"] as JsonArray ?? [];
            var rows = new List<(JsonNode? Row, string Prefix)>();
            rows.AddRange(Rows("overdue").Where(Mine).Select(r => (r, "Overdue, was due ")));
            rows.AddRange(Rows("week").Where(Mine).Select(r => (r, "")));
            rows.AddRange(Rows("later").Where(Mine).Take(AgendaLater).Select(r => (r, "")));
            // A to-do of the student's own with no date is theirs to remember; Canvas's undated ones are left out, as before.
            rows.AddRange(Rows("undated").Where(r => Mine(r) && Own(r)).Select(r => (r, "")));
            if (rows.Count > 0)
                sb.Append(rows.Any(r => Own(r.Row)) ? "Due, soonest first (\"own\" is something the student added in Syft Notes, not from Canvas):\n" : "Due in Canvas, soonest first:\n")
                    .Append(string.Join('\n', rows.Select(r => Line(r.Row, r.Prefix)))).Append('\n');
            else if (linked) sb.Append("Nothing is due in Canvas right now.\n");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or InvalidOperationException)
        {
            // Asking still works without the Due list.
        }
        try
        {
            var events = (LibraryCalendar.Read(cfg.Home, EventClass.Of(cfg), DateTimeOffset.UtcNow)["events"] as JsonArray ?? [])
                .Where(Mine).Select(e =>
                {
                    string at = CanvasMarkdown.When(Py.AsString(e?["start"]), zone);
                    string cls = Py.AsString(e?["class"]) is { Length: > 0 } c ? $" ({c})" : "";
                    return $"- {at}: {Py.AsString(e?["title"])}{cls}";
                }).ToList();
            if (events.Count > 0) sb.Append("On the calendar today and tomorrow:\n").Append(string.Join('\n', events)).Append('\n');
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or InvalidOperationException)
        {
        }
        return sb.ToString();
    }
}
