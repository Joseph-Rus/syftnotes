using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using StudyStash.Core;
using StudyStash.Core.Calendar;

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
        sb.Append("Today is ").Append(now.ToString("dddd d MMMM yyyy, h:mm tt", CultureInfo.InvariantCulture)).Append(".\n");
        bool Mine(JsonNode? row) => onlyClass is null || Py.AsString(row?["class"]) == onlyClass;
        string When(JsonNode? row)
        {
            string? iso = Py.AsString(row?["due"]) ?? Py.AsString(row?["due_at"]);
            return DateTimeOffset.TryParse(iso, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var d)
                ? TimeZoneInfo.ConvertTime(d, zone).ToString("ddd d MMM, h:mm tt", CultureInfo.InvariantCulture) : "no due date";
        }
        string Line(JsonNode? row, string prefix) =>
            $"- {prefix}{When(row)}: {Py.AsString(row?["class"])}: {Py.AsString(row?["name"])}{(row?["missing"]?.GetValue<bool>() == true ? " (missing)" : "")}";
        try
        {
            if (Canvas.Settings.Url.Length == 0) sb.Append("Canvas isn't linked to Syft Notes, so due dates aren't known.\n");
            else
            {
                var groups = DueList()["groups"] as JsonArray ?? [];
                JsonArray Rows(string key) => groups.FirstOrDefault(g => Py.AsString(g?["key"]) == key)?["items"] as JsonArray ?? [];
                var lines = new List<string>();
                lines.AddRange(Rows("overdue").Where(Mine).Select(r => Line(r, "Overdue, was due ")));
                lines.AddRange(Rows("week").Where(Mine).Select(r => Line(r, "")));
                lines.AddRange(Rows("later").Where(Mine).Take(AgendaLater).Select(r => Line(r, "")));
                sb.Append(lines.Count == 0 ? "Nothing is due in Canvas right now.\n" : "Due in Canvas, soonest first:\n" + string.Join('\n', lines) + "\n");
            }
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
                    string at = DateTimeOffset.TryParse(Py.AsString(e?["start"]), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var d)
                        ? TimeZoneInfo.ConvertTime(d, zone).ToString("ddd d MMM, h:mm tt", CultureInfo.InvariantCulture) : "";
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
