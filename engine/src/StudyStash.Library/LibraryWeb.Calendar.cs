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
}
