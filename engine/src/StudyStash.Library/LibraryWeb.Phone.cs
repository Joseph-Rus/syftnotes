using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.StaticFiles;
using StudyStash.Core;

namespace StudyStash.Library;

/// <summary>
/// The phone app: its pages at /app/ (the web app in web/, which the phone adds to its Home Screen), whether a phone
/// is paired, and each lecture's notes drawn for it. Phones reach it at https://&lt;library&gt;.ts.net:8443 through
/// Tailscale Serve; see docs/phone.md.
/// </summary>
public sealed partial class LibraryWeb
{
    /// <summary>The phone app's own rules: its scripts and styles come from its own files, it talks only to this
    /// library, and no other page may frame it.</summary>
    public const string PhoneCsp =
        "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data: blob:; connect-src 'self'; worker-src 'self'; manifest-src 'self'; frame-ancestors 'none'";

    static readonly FileExtensionContentTypeProvider PhoneTypes = MakePhoneTypes();

    static FileExtensionContentTypeProvider MakePhoneTypes()
    {
        var types = new FileExtensionContentTypeProvider();
        types.Mappings[".webmanifest"] = "application/manifest+json";
        types.Mappings[".js"] = "text/javascript";
        types.Mappings[".mjs"] = "text/javascript";
        return types;
    }

    /// <summary>Where the phone app's files are (web/dist, built): the folder given, else STUDYSTASH_WEB_DIR, else web/
    /// beside the app. Null when there's no index.html there: this copy was built without the phone app.</summary>
    string? PhoneAppDir()
    {
        string? env = Environment.GetEnvironmentVariable("STUDYSTASH_WEB_DIR");
        string dir = options.PhoneApp ?? (string.IsNullOrWhiteSpace(env) ? Path.Combine(AppContext.BaseDirectory, "web") : env);
        return File.Exists(Path.Combine(dir, "index.html")) ? Path.TrimEndingDirectorySeparator(Path.GetFullPath(dir)) : null;
    }

    /// <summary>A file of the phone app. Any address that isn't a file is one of the app's own screens
    /// (/app/lectures/42), so it gets index.html and the app shows the screen; a missing file with an extension is a
    /// plain 404, so a stale script isn't answered with a page.</summary>
    IResult PhoneFile(HttpContext ctx, string path)
    {
        if (PhoneAppDir() is not { } root) return PhoneAppMissing();
        var headers = ctx.Response.Headers;
        headers["X-Content-Type-Options"] = "nosniff";
        headers["Referrer-Policy"] = "no-referrer";
        string[] parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        // Nothing outside the folder, and nothing hidden in it.
        if (parts.Any(p => p.StartsWith('.') || p.Contains('\\') || p.Contains(':')))
            return Http.Detail(404, "Not Found");
        string rel = string.Join('/', parts);
        string full = Path.GetFullPath(Path.Combine(root, Path.Combine(parts)));
        if (!full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal) || !File.Exists(full))
        {
            if (parts.Length > 0 && Path.HasExtension(parts[^1])) return Http.Detail(404, "Not Found");
            rel = "index.html";
            full = Path.Combine(root, rel);
        }
        // Vite names what it builds in assets/ by its contents, so those never change; everything else (the page, the
        // service worker, the manifest, the icons) is checked each time, so an update reaches the phone.
        headers.CacheControl = rel.StartsWith("assets/", StringComparison.Ordinal) ? "public, max-age=31536000, immutable" : "no-cache";
        if (rel == "sw.js") headers["Service-Worker-Allowed"] = "/app/";
        if (rel.EndsWith(".html", StringComparison.Ordinal))
        {
            headers.ContentSecurityPolicy = PhoneCsp;
            headers.XFrameOptions = "DENY";
        }
        string type = PhoneTypes.TryGetContentType(full, out var t) ? t : "application/octet-stream";
        if (type.StartsWith("text/", StringComparison.Ordinal) && !type.Contains("charset", StringComparison.Ordinal)) type += "; charset=utf-8";
        return Results.File(full, type, lastModified: File.GetLastWriteTimeUtc(full));
    }

    /// <summary>A library built without the phone app: a page in the library's own look that says so.</summary>
    IResult PhoneAppMissing()
    {
        string nonce = options.Nonce();
        string body = "<div class=\"login\"><form><div class=\"appicon\" aria-hidden=\"true\">" + Ui.Esc(Py.Head(cfg.PoolName.Length > 0 ? cfg.PoolName : "L", 1).ToUpperInvariant())
            + "</div><h1>The phone app isn't here yet</h1><p class=\"muted\">This copy of Study Stash was built without it. Update Study Stash "
            + "on the library's computer, then scan the code in Settings again.</p><a class=\"btn primary\" href=\"/\">Open the library</a></form></div>";
        return Respond(Ui.BarePage($"Phone app · {cfg.PoolName}", body, nonce), nonce, 404);
    }

    void MapPhone(WebApplication app)
    {
        MapDevices(app);
        MapPhoneSettings(app);
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
        // A lecture's notes drawn for the phone: its diagrams as pictures and its formulas ready for KaTeX.
        app.MapGet("/api/v2/lectures/{id}/rendered", (HttpContext ctx, string id) => Api(ctx, () =>
        {
            if (Reader.Lecture(id) is not { } l) return Http.Detail(404, "no such lecture");
            return Http.Json(new JsonObject
            {
                ["id"] = id, ["title"] = l["title"]?.DeepClone(), ["class"] = l["class"]?.DeepClone(), ["date"] = l["date"]?.DeepClone(),
                ["html"] = PhoneNotes.Render(l["notes"] is JsonValue v && v.TryGetValue(out string? notes) ? notes : ""),
            });
        }));
        // The app's pages need no sign-in (they're the same for everyone); what they show asks /api/v2, which does.
        // Routing takes "/app" and "/app/" as one: only the one without the slash moves, so the app's own addresses
        // (and its service worker's scope) are always under /app/.
        app.MapGet("/app", (HttpContext ctx) => ctx.Request.Path.Value!.EndsWith('/') ? PhoneFile(ctx, "") : Results.Redirect("/app/", permanent: true));
        app.MapGet("/app/{**path}", (HttpContext ctx, string? path) => PhoneFile(ctx, path ?? ""));
    }
}
