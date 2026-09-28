using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using StudyStash.Core;

namespace StudyStash.Library;

/// <summary>
/// The phone app: its pages at /app/ (the web app in web/, which the phone adds to its Home Screen), whether a phone
/// is paired, and each lecture's notes drawn for it. Phones reach it at https://&lt;library&gt;.ts.net:8443 through
/// Tailscale Serve; see docs/phone.md.
/// </summary>
public sealed partial class LibraryWeb
{
    void MapPhone(WebApplication app)
    {
        MapDevices(app);
        // Whether this phone is paired, and which library it's talking to. No sign-in: the phone app asks it first.
        app.MapGet("/api/v2/me", (HttpContext ctx) =>
        {
            ctx.Response.Headers.CacheControl = "no-store";
            return Http.Json(new JsonObject
            {
                ["paired"] = PhoneOf(ctx) is not null,
                ["library"] = new JsonObject { ["name"] = cfg.PoolName, ["version"] = Engine.Version },
            });
        });
    }
}
