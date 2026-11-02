using System.Net;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using StudyStash.Core.PhoneDrive;

namespace StudyStash.Library;

/// <summary>
/// /api/v2/phone/google: the phone's Google Drive link, which runs here in the library (<see cref="PhoneLink"/>), for
/// the app on this same computer to show and steer from Settings → Phone. Only this computer's app may: the library's
/// password, from this computer, and never a phone (a phone's requests reach the library from this computer too, but
/// only the iPhone app's own calls, which these aren't: <see cref="PhoneCalls"/>).
/// </summary>
public sealed partial class LibraryWeb
{
    IResult? OnlyThisComputersApp(HttpContext ctx)
    {
        if (RequireKey(ctx) is { } refused) return refused;
        if (PhoneOf(ctx) is not null || ctx.Request.Headers.ContainsKey(LibraryCalls.WhoHeader)) return Http.Detail(403, "not for a phone");
        if (ctx.Connection.RemoteIpAddress is not { } ip || !IPAddress.IsLoopback(ip)) return Http.Detail(403, "only from this computer");
        return null;
    }

    IResult PhoneGoogleJson()
    {
        var o = options.Phone?.Json() ?? new JsonObject { ["account"] = null, ["running"] = false, ["phones"] = new JsonArray() };
        o["background"] = options.Background;
        return Http.Json(o);
    }

    void MapPhoneGoogle(WebApplication app)
    {
        // What the link is doing, its account and its phones; and whether this library runs in the background.
        app.MapGet("/api/v2/phone/google", (HttpContext ctx) => OnlyThisComputersApp(ctx) ?? PhoneGoogleJson());
        // The app connected an account (google.json is there now): the link starts with it.
        app.MapPost("/api/v2/phone/google/start", (HttpContext ctx) =>
        {
            if (OnlyThisComputersApp(ctx) is { } refused) return refused;
            options.Phone?.Start();
            return PhoneGoogleJson();
        });
        // Disconnect: the link stops, its files in the folder go, Google forgets this computer, google.json goes.
        app.MapPost("/api/v2/phone/google/disconnect", async (HttpContext ctx) =>
        {
            if (OnlyThisComputersApp(ctx) is { } refused) return refused;
            if (options.Phone is { } phone) await phone.DisconnectAsync();
            return PhoneGoogleJson();
        });
        // Remove a phone: this computer stops answering it.
        app.MapPost("/api/v2/phone/google/phones/{id}/remove", async (HttpContext ctx, string id) =>
        {
            if (OnlyThisComputersApp(ctx) is { } refused) return refused;
            try
            {
                if (options.Phone is { } phone) await phone.RemovePhoneAsync(id);
            }
            catch (Exception e) when (e is DriveException or GoogleException or HttpRequestException or TaskCanceledException)
            {
                return Http.Detail(502, "Can't reach Google Drive right now.");
            }
            return PhoneGoogleJson();
        });
        // A recording started or stopped in the app: the phone's copy looks again now.
        app.MapPost("/api/v2/phone/google/copy", (HttpContext ctx) =>
        {
            if (OnlyThisComputersApp(ctx) is { } refused) return refused;
            options.Phone?.CopyNow();
            return Http.Json(new JsonObject { ["ok"] = true });
        });
    }
}
