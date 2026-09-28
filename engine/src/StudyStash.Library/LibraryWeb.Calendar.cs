using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using StudyStash.Core.Calendar;

namespace StudyStash.Library;

/// <summary>What's coming up on the student's calendars: the laptop that records sends its week here, and the app,
/// the web pages and the phone read it back with each event's class.</summary>
public sealed partial class LibraryWeb
{
    void MapCalendar(WebApplication app)
    {
        app.MapPost("/api/v2/calendar/upcoming", Http.Handle(ctx => ApiAsync(ctx, async () =>
        {
            var body = await Http.JsonBodyAsync(ctx.Request);
            if (body?["events"] is not JsonArray) return Http.Detail(400, "which events?");
            return Http.Json(new JsonObject { ["kept"] = LibraryCalendar.Save(cfg.Home, body, DateTimeOffset.UtcNow) });
        })));
        app.MapGet("/api/v2/calendar/upcoming", (HttpContext ctx) => Api(ctx, () =>
            Http.Json(LibraryCalendar.Read(cfg.Home, EventClass.Of(cfg), DateTimeOffset.UtcNow))));
    }

    /// <summary>"Coming up" on the home page: today and tomorrow's timed events, each with the class it's for.
    /// "" once there's nothing to show — no laptop sending events yet, or nothing left today or tomorrow.</summary>
    string ComingUpHtml()
    {
        var now = DateTimeOffset.Now;
        if (LibraryCalendar.Read(cfg.Home, EventClass.Of(cfg), now)["events"] is not JsonArray events || events.Count == 0) return "";
        var today = now.Date;
        var rows = events.OfType<JsonObject>()
            .Where(e => e["allDay"] is not JsonValue v || !v.TryGetValue(out bool allDay) || !allDay)
            .Select(e => (e, Start: DateTimeOffset.TryParse(e["start"]?.GetValue<string>(), out var t) ? t : (DateTimeOffset?)null))
            .Where(x => x.Start is { } s && s.Date <= today.AddDays(1))
            .Take(4)
            .Select(x =>
            {
                var start = x.Start!.Value;
                string title = x.e["title"]?.GetValue<string>() ?? "";
                string? cls = x.e["class"]?.GetValue<string>();
                string when = (start.Date == today ? "" : "Tomorrow ") + start.ToString("h:mm tt");
                string tag = cls is { Length: > 0 } ? $"<span class=\"tag\" style=\"{Ui.HueStyle(cls)}\">{Ui.Esc(cls)}</span>" : "";
                return $"<div class=\"row\"><span class=\"grow\">{Ui.Esc(title)}</span>{tag}<span class=\"value\">{Ui.Esc(when)}</span></div>";
            });
        string body = string.Concat(rows);
        return body.Length == 0 ? "" : $"<h2>Coming up</h2><div class=\"group\">{body}</div>";
    }
}
