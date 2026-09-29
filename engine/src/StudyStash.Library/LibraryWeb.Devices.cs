using System.Globalization;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using StudyStash.Core;

namespace StudyStash.Library;

/// <summary>
/// /api/v2/devices: adding a phone. A computer that's already in (the app's Settings, or the library's page) asks for
/// a 6-digit code and shows it with a QR code of the phone app's address; the phone opens that address, the student
/// types the code, and the phone gets a <c>device</c> cookie that reads the library the way the app does, until the
/// phone is removed here.
/// </summary>
public sealed partial class LibraryWeb
{
    /// <summary>The https port the phone app is on at https://&lt;library&gt;.ts.net: 443 is Claude's.</summary>
    public const int PhonePort = 8443;

    Devices? devices;

    /// <summary>The paired phones and the code that adds one.</summary>
    public Devices Phones => devices ??= options.Devices ?? new Devices(cfg.Home);

    /// <summary>The phone a request comes from, when it's for /api/v2 and carries a paired phone's cookie. A removed
    /// phone's cookie finds nothing, on its very next request.</summary>
    PairedDevice? PhoneOf(HttpContext ctx)
    {
        if (!ctx.Request.Path.StartsWithSegments("/api/v2")) return null;
        if (ctx.Items.TryGetValue(Devices.Cookie, out var known)) return known as PairedDevice;
        var phone = ctx.Request.Cookies[Devices.Cookie] is { Length: > 0 } token ? Phones.Check(token) : null;
        ctx.Items[Devices.Cookie] = phone;
        return phone;
    }

    static string Iso(DateTimeOffset at) => at.ToString("o", CultureInfo.InvariantCulture);

    static JsonObject DeviceJson(PairedDevice d, bool seen = true)
    {
        var o = new JsonObject { ["id"] = d.Id, ["name"] = d.Name, ["added"] = Iso(d.Added) };
        if (seen) o["lastSeen"] = Iso(d.LastSeen);
        return o;
    }

    JsonObject DevicesJson() => new()
    {
        ["devices"] = new JsonArray([.. Phones.List().Select(d => (JsonNode)DeviceJson(d))]),
    };

    /// <summary>{"error": "..."}: what the phone app shows when pairing is refused.</summary>
    static IResult PairError(int status, string error) => Http.Json(new JsonObject { ["error"] = error }, status);

    /// <summary>What a phone is called when it doesn't say: from its browser, "iPhone", "iPad" or "Android phone".</summary>
    public static string PhoneName(string? userAgent)
    {
        string ua = userAgent ?? "";
        if (ua.Contains("iPhone", StringComparison.Ordinal)) return "iPhone";
        if (ua.Contains("iPad", StringComparison.Ordinal)) return "iPad";
        if (ua.Contains("Android", StringComparison.Ordinal)) return ua.Contains("Mobile", StringComparison.Ordinal) ? "Android phone" : "Android tablet";
        return "Phone";
    }

    /// <summary>
    /// A new code, and where the phone opens the app. Any phone on the tailnet reaches the library at this computer's
    /// Tailscale IP, whatever its DNS settings, so that's the address the QR code gives (plain http: Tailscale encrypts
    /// the traffic itself). Tailscale Serve is turned on too (https on <see cref="PhonePort"/>), and when it works the
    /// address carries it as <c>?https=</c>: a phone that can look the name up (MagicDNS) moves there, where the app
    /// reads offline and installs as a real app. A library for this computer alone doesn't listen on the IP, so it
    /// gives the https address only. Why not, in words, when neither can be had.
    /// </summary>
    async Task<(JsonObject? Code, ReachProblem? Problem)> NewPhoneCodeAsync()
    {
        var (secure, problem) = await Task.Run(() => options.Reach.Set(cfg.WebPort, internet: false, on: true, httpsPort: PhonePort));
        string? secureApp = secure is null ? null : secure.TrimEnd('/') + "/app/";
        string? plainApp = PlainPhoneAddress();
        if (secureApp is null && plainApp is null)
        {
            var p = problem ?? new ReachProblem(ReachKind.Other, "Tailscale didn't say where the library is.");
            return (null, p with { Words = "Your phone reaches the library over Tailscale, so it needs Tailscale here first. " + p.Words });
        }
        string url = plainApp is null ? secureApp! : secureApp is null ? plainApp : plainApp + "?https=" + Uri.EscapeDataString(secureApp);
        var (code, expires) = Phones.NewCode();
        return (new JsonObject { ["code"] = code, ["url"] = url, ["secureUrl"] = secureApp, ["expires"] = Iso(expires) }, null);
    }

    /// <summary>http://&lt;Tailscale IP&gt;:&lt;port&gt;/app/, when Tailscale is up and the library listens beyond this
    /// computer.</summary>
    string? PlainPhoneAddress()
    {
        if (cfg.WebHost is "127.0.0.1" or "localhost" or "::1") return null;
        var ts = options.Reach.Tailscale();
        return ts.Running && ts.Ips.FirstOrDefault() is { Length: > 0 } ip ? $"http://{ip}:{cfg.WebPort}/app/" : null;
    }

    /// <summary>The pairing cookie is https-only wherever the phone's browser would keep it that way: over https
    /// (Tailscale Serve says so in X-Forwarded-Proto), and on this computer (browsers count localhost as secure). At
    /// the Tailscale IP, over plain http, a browser drops a Secure cookie, so there it isn't one.</summary>
    static bool SecureCookie(HttpRequest request) =>
        request.IsHttps || string.Equals(request.Headers["X-Forwarded-Proto"], "https", StringComparison.OrdinalIgnoreCase)
        || request.Host.Host is "localhost" or "127.0.0.1" or "[::1]" or "::1";

    void MapDevices(WebApplication app)
    {
        app.MapPost("/api/v2/devices/code", Http.Handle(ctx => ApiAsync(ctx, async () =>
        {
            var (code, problem) = await NewPhoneCodeAsync();
            ctx.Response.Headers.CacheControl = "no-store";
            if (code is null) return Http.Json(new JsonObject { ["detail"] = problem!.Words, ["fix"] = problem.FixUrl }, 409);
            return Http.Json(code);
        })));
        // Pairing needs no sign-in: the code is the sign-in.
        app.MapPost("/api/v2/devices/pair", Http.Handle(async ctx =>
        {
            ctx.Response.Headers.CacheControl = "no-store";
            var body = await Http.JsonBodyAsync(ctx.Request);
            string? typed = body?["code"] switch
            {
                JsonValue v when v.TryGetValue(out string? s) => s,
                JsonValue v when v.TryGetValue(out long n) && n is >= 0 and < 1_000_000 => n.ToString("D6", CultureInfo.InvariantCulture),
                _ => null,
            };
            var (device, token, why) = Phones.Pair(typed, Str(body, "name") ?? PhoneName(ctx.Request.Headers.UserAgent));
            if (why == Devices.Refusal.TooManyTries)
                return PairError(429, "Too many wrong codes. Wait ten minutes, then make a new code in Study Stash's Settings.");
            if (device is null)
                return PairError(401, "That code isn't right, or it's more than ten minutes old. Make a new one in Study Stash's Settings.");
            ctx.Response.Cookies.Append(Devices.Cookie, token!, new CookieOptions
            {
                HttpOnly = true, Secure = SecureCookie(ctx.Request), SameSite = SameSiteMode.Strict, Path = "/", MaxAge = TimeSpan.FromDays(400), IsEssential = true,
            });
            Console.WriteLine($"[phone] paired {device.Name}");
            return Http.Json(new JsonObject { ["device"] = DeviceJson(device, seen: false) });
        }));
        app.MapGet("/api/v2/devices", (HttpContext ctx) => Api(ctx, () => Http.Json(DevicesJson())));
        app.MapDelete("/api/v2/devices/{id}", (HttpContext ctx, string id) =>
            Api(ctx, () => Phones.Remove(id) ? Http.Json(DevicesJson()) : Http.Detail(404, "no such phone")));
    }
}
