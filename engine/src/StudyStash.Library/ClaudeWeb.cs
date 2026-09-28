using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using ModelContextProtocol.AspNetCore;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using StudyStash.Core;

namespace StudyStash.Library;

/// <summary>
/// The library's door for Claude, on a port of its own (the library's port + 1, on this computer only): the MCP
/// server, and the OAuth sign-in Claude uses to reach it. Tailscale Serve or Funnel puts this port, and nothing
/// else of the library, on https://&lt;library&gt;.ts.net. Claude signs in the way any app does: it registers, sends
/// you to the sign-in page, where the library's password allows it, and gets tokens that only read.
/// </summary>
public static class ClaudeWeb
{
    public const string McpPath = "/mcp";

    public static int PortFor(Config cfg) => cfg.WebPort + 1;

    public static WebApplication Build(WebApplicationBuilder builder, Config cfg, LibraryReader reader, ClaudeAccess access, StudyStash.Core.Canvas.CanvasSync? canvas = null, StudyStash.Core.Ai.FileIndex? files = null)
    {
        var source = new LocalLibrary(reader, canvas, cfg.Home, files);
        builder.Services.AddMcpServer(o =>
        {
            o.ServerInfo = new ModelContextProtocol.Protocol.Implementation { Name = ClaudeTools.ServerName, Title = "Study Stash", Version = Engine.Version };
            o.ServerInstructions = ClaudeTools.WebInstructions;
        })
            // No sessions: every request stands alone, so a library restart (or an update) loses Claude nothing.
            .WithHttpTransport(o => o.SessionMode = HttpServerSessionMode.Stateless)
            .WithTools(StudyStash.Core.Ai.ToolAccess.Guard(ClaudeTools.Tools(source, web: true), () => Task.FromResult((access.ToolsOn, access.Reading))))
            .WithPrompts(ClaudeTools.Prompts());
        var app = builder.Build();

        // In this order: a browser's preflight is answered (no sign-in needed to ask); a page from a site that isn't
        // Claude or this library is turned away (a browser always says where a page came from; Claude's own servers
        // send no Origin); only a signed-in Claude (or a token from Settings) gets to the MCP server; anyone else is told
        // where to sign in (RFC 9728), which is how Claude finds the sign-in on its own. AI tool access being off is
        // the tools' to say, in words, not a refused connection.
        app.Use(async (ctx, next) =>
        {
            ctx.Response.Headers["X-Content-Type-Options"] = "nosniff";
            var path = ctx.Request.Path;
            if (!path.StartsWithSegments(McpPath))
            {
                // Discovery, registration, tokens and signing out hold nothing a cookie could reach: any page may ask.
                if (Open.Any(p => path.StartsWithSegments(p)))
                {
                    ctx.Response.Headers.AccessControlAllowOrigin = "*";
                    ctx.Response.Headers.AccessControlExposeHeaders = "WWW-Authenticate";
                }
                if (HttpMethods.IsOptions(ctx.Request.Method))
                {
                    if (Open.Any(p => path.StartsWithSegments(p)))
                    {
                        ctx.Response.Headers.AccessControlAllowMethods = "GET, POST, OPTIONS";
                        ctx.Response.Headers.AccessControlAllowHeaders = "Authorization, Content-Type, MCP-Protocol-Version";
                        ctx.Response.Headers.AccessControlMaxAge = "600";
                    }
                    ctx.Response.StatusCode = StatusCodes.Status204NoContent;
                    return;
                }
                await next();
                return;
            }
            string origin = ctx.Request.Headers.Origin.ToString();
            bool allowed = origin.Length == 0 || AllowedOrigin(ctx, access, origin);
            ctx.Response.Headers.Vary = "Origin";
            if (origin.Length > 0 && allowed)
            {
                ctx.Response.Headers.AccessControlAllowOrigin = origin;
                ctx.Response.Headers.AccessControlExposeHeaders = "WWW-Authenticate, Mcp-Session-Id, MCP-Protocol-Version";
            }
            if (HttpMethods.IsOptions(ctx.Request.Method))
            {
                if (origin.Length > 0 && allowed)
                {
                    ctx.Response.Headers.AccessControlAllowMethods = "GET, POST, DELETE, OPTIONS";
                    ctx.Response.Headers.AccessControlAllowHeaders = McpHeaders;
                    ctx.Response.Headers.AccessControlMaxAge = "600";
                }
                ctx.Response.StatusCode = StatusCodes.Status204NoContent;
                return;
            }
            if (!allowed)
            {
                // Streamable HTTP's DNS-rebinding guard: a JSON-RPC error with no id, as the spec asks.
                ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
                await ctx.Response.WriteAsJsonAsync(new JsonObject
                {
                    ["jsonrpc"] = "2.0",
                    ["error"] = new JsonObject { ["code"] = -32600, ["message"] = "Study Stash doesn't take requests from pages on " + origin + "." },
                });
                return;
            }
            string auth = ctx.Request.Headers.Authorization.ToString();
            string bearer = auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? auth[7..].Trim() : "";
            // A token is for one address (RFC 8707): one given for this library on the internet doesn't read through
            // another name.
            if (access.Check(bearer, ResourceOf(ctx, access)) is null)
            {
                string metadata = Base(ctx, access) + "/.well-known/oauth-protected-resource" + McpPath;
                ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                ctx.Response.Headers.WWWAuthenticate = $"Bearer resource_metadata=\"{metadata}\", scope=\"{Scope}\""
                    + (bearer.Length > 0 ? ", error=\"invalid_token\", error_description=\"The token is unknown, expired, revoked or for another address\"" : "");
                await ctx.Response.WriteAsJsonAsync(new { error = "invalid_token", error_description = "Sign in to Study Stash again." });
                return;
            }
            // This door never hands out a session, so a session id can only be one a client kept from somewhere
            // else (an older build, a proxy): the SDK would refuse it with a 400, which Claude takes as the end.
            ctx.Request.Headers.Remove("Mcp-Session-Id");
            // No stream to open (GET) and no session to end (DELETE): Streamable HTTP asks for 405 here, where the
            // SDK's stateless mode would say 404, which a client reads as "your session is gone".
            if (!HttpMethods.IsPost(ctx.Request.Method))
            {
                ctx.Response.StatusCode = StatusCodes.Status405MethodNotAllowed;
                ctx.Response.Headers.Allow = "POST, OPTIONS";
                return;
            }
            await next();
        });

        IResult Resource(HttpContext ctx) => Http.Json(new JsonObject
        {
            ["resource"] = Base(ctx, access) + McpPath,
            ["authorization_servers"] = new JsonArray(Base(ctx, access)),
            ["bearer_methods_supported"] = new JsonArray("header"),
            ["scopes_supported"] = new JsonArray(Scope),
            ["resource_name"] = "Study Stash",
            ["resource_documentation"] = Docs,
        });
        app.MapGet("/.well-known/oauth-protected-resource", Resource);
        app.MapGet("/.well-known/oauth-protected-resource" + McpPath, Resource);
        IResult Server(HttpContext ctx)
        {
            string b = Base(ctx, access);
            return Http.Json(new JsonObject
            {
                ["issuer"] = b,
                ["authorization_endpoint"] = b + "/authorize",
                ["token_endpoint"] = b + "/token",
                ["registration_endpoint"] = b + "/register",
                ["revocation_endpoint"] = b + "/revoke",
                ["response_types_supported"] = new JsonArray("code"),
                ["grant_types_supported"] = new JsonArray("authorization_code", "refresh_token"),
                ["code_challenge_methods_supported"] = new JsonArray("S256"),
                ["token_endpoint_auth_methods_supported"] = new JsonArray("none"),
                ["revocation_endpoint_auth_methods_supported"] = new JsonArray("none"),
                ["scopes_supported"] = new JsonArray(Scope),
                ["authorization_response_iss_parameter_supported"] = true,
                ["client_id_metadata_document_supported"] = true,
                ["service_documentation"] = Docs,
            });
        }
        app.MapGet("/.well-known/oauth-authorization-server", Server);
        app.MapGet("/.well-known/oauth-authorization-server" + McpPath, Server);
        app.MapGet("/.well-known/openid-configuration", Server);

        // Dynamic client registration (RFC 7591): Claude's fallback when it doesn't use its published identity. Every
        // client is public (PKCE, no secret), whatever it asks for; fields it sends that don't matter here are fine.
        app.MapPost("/register", Http.Handle(async ctx =>
        {
            var body = await Http.JsonBodyAsync(ctx.Request);
            if (body is null) return Http.Json(new JsonObject { ["error"] = "invalid_client_metadata", ["error_description"] = "The body must be a JSON object." }, 400);
            var uris = (body["redirect_uris"] as JsonArray ?? []).Select(n => n is JsonValue v && v.TryGetValue(out string? s) ? s ?? "" : "").ToList();
            string name = body["client_name"] is JsonValue nv && nv.TryGetValue(out string? cn) ? cn ?? "" : "";
            ClaudeClient client;
            try
            {
                client = access.Register(name.Length > 0 ? Py.Head(name, 80) : "Claude", uris);
            }
            catch (ArgumentException e)
            {
                return Http.Json(new JsonObject { ["error"] = "invalid_redirect_uri", ["error_description"] = e.Message }, 400);
            }
            ctx.Response.Headers.CacheControl = "no-store";
            return Http.Json(new JsonObject
            {
                ["client_id"] = client.ClientId,
                ["client_id_issued_at"] = (long)client.Created,
                ["client_name"] = client.Name,
                ["redirect_uris"] = new JsonArray(client.RedirectUris.Select(u => (JsonNode?)u).ToArray()),
                ["grant_types"] = new JsonArray("authorization_code", "refresh_token"),
                ["response_types"] = new JsonArray("code"),
                ["token_endpoint_auth_method"] = "none",
                ["scope"] = Scope,
            }, 201);
        }));

        app.MapGet("/authorize", Http.Handle(ctx => Authorize(ctx, cfg, access, post: null)));
        app.MapPost("/authorize", Http.Handle(async ctx => await Authorize(ctx, cfg, access, await Http.FormAsync(ctx.Request))));

        app.MapPost("/token", Http.Handle(async ctx =>
        {
            var form = await Http.FormAsync(ctx.Request);
            ctx.Response.Headers.CacheControl = "no-store";
            ctx.Response.Headers.Pragma = "no-cache";
            // A public client sends client_id in the form; one that sends it as Basic auth is read the same way, and a
            // secret it sends along is ignored (none was ever issued).
            string clientId = form.Get("client_id");
            if (clientId.Length == 0 && BasicUser(ctx.Request) is { Length: > 0 } basic) clientId = basic;
            string grantType = form.Get("grant_type");
            if (grantType is "authorization_code" or "refresh_token" && (grantType == "authorization_code" || clientId.Length > 0) && !access.KnownClient(clientId))
                return Http.Json(new JsonObject { ["error"] = "invalid_client", ["error_description"] = "Study Stash doesn't know this app. Register again." }, 401);
            string? resource = null;
            if (form.Get("resource") is { Length: > 0 } asked && ((resource = Canonical(asked)) is null || !Ours(ctx, cfg, access, resource)))
                return Http.Json(new JsonObject { ["error"] = "invalid_target", ["error_description"] = "That isn't one of this library's addresses." }, 400);
            var (tokens, refusal) = grantType switch
            {
                "authorization_code" => access.Exchange(form.Get("code"), form.Get("code_verifier"), clientId, form.Get("redirect_uri"), resource),
                "refresh_token" => access.Refresh(form.Get("refresh_token"), clientId, resource, ResourceOf(ctx, access)),
                _ => (null, new ClaudeRefusal("unsupported_grant_type", "grant_type must be authorization_code or refresh_token.")),
            };
            if (tokens is null) return Http.Json(new JsonObject { ["error"] = refusal!.Error, ["error_description"] = refusal.Description }, 400);
            return Http.Json(new JsonObject
            {
                ["access_token"] = tokens.AccessToken, ["token_type"] = "Bearer", ["expires_in"] = tokens.ExpiresIn,
                ["refresh_token"] = tokens.RefreshToken, ["scope"] = Scope,
            });
        }));

        // An app signing out (RFC 7009). The answer is the same whether or not the token meant anything.
        app.MapPost("/revoke", Http.Handle(async ctx =>
        {
            var form = await Http.FormAsync(ctx.Request);
            ctx.Response.Headers.CacheControl = "no-store";
            access.RevokeToken(form.Get("token"), form.Get("client_id") is { Length: > 0 } id ? id : BasicUser(ctx.Request) ?? "");
            return Http.Json(new JsonObject());
        }));

        app.MapMcp(McpPath);
        app.MapGet("/", () => Results.Text(
            "Study Stash: the MCP server for your lecture library.\nAdd " + McpPath + " on this address to Claude as a connector.\n",
            "text/plain; charset=utf-8"));
        app.MapGet("/healthz", () => Http.Json(new JsonObject { ["ok"] = true, ["app"] = "study-stash-claude", ["version"] = Engine.Version }));
        Icons.Map(app);
        app.MapFallback(() => Http.Detail(404, "Not Found"));
        return app;
    }

    const string Scope = "library:read";

    /// <summary>The paths any web page may call: they hold no cookies and no one's data.</summary>
    static readonly string[] Open = ["/.well-known", "/register", "/token", "/revoke"];

    const string McpHeaders = "Authorization, Content-Type, Accept, MCP-Protocol-Version, Mcp-Session-Id, Mcp-Method, Mcp-Name, Last-Event-ID";

    /// <summary>Whether a page from <paramref name="origin"/> may call the MCP server: this library by any of its
    /// names, Claude on the web, or a program on this computer.</summary>
    public static bool AllowedOrigin(HttpContext ctx, ClaudeAccess access, string origin)
    {
        if (OriginOf(origin) is not { } o) return false;
        if (o.StartsWith("http://localhost:", StringComparison.Ordinal) || o == "http://localhost"
            || o.StartsWith("http://127.0.0.1:", StringComparison.Ordinal) || o == "http://127.0.0.1")
            return true;
        return new[] { Base(ctx, access), access.PublicUrl, access.TailnetUrl ?? "", "https://claude.ai", "https://claude.com" }
            .Any(known => known.Length > 0 && OriginOf(known) == o);
    }

    /// <summary>An address's origin the one way it's compared (scheme://host[:port], lowercased, no default port),
    /// or null when it isn't an http(s) address.</summary>
    static string? OriginOf(string uri) =>
        Uri.TryCreate(uri, UriKind.Absolute, out var u) && (u.Scheme == Uri.UriSchemeHttp || u.Scheme == Uri.UriSchemeHttps) && u.UserInfo.Length == 0
            ? $"{u.Scheme}://{u.IdnHost.ToLowerInvariant()}{(u.IsDefaultPort ? "" : ":" + u.Port)}" : null;
    const string Docs = "https://github.com/Joseph-Rus/study-stash/blob/main/docs/claude-connector.md";

    static readonly string[] Loopbacks = ["localhost", "127.0.0.1", "[::1]"];

    /// <summary>
    /// This server's address as Claude sees it, for everything it advertises. The first that fits:
    /// the public or tailnet address it was put on, when the request came to that name; https for any other
    /// *.ts.net name (Tailscale Serve and Funnel only answer on https, though they pass requests on over plain http);
    /// http for this computer (Claude Code here); else what a proxy on this computer says the request came to, and
    /// only then, since anyone else could send those headers; else the request's own scheme and host.
    /// </summary>
    public static string Base(HttpContext ctx, ClaudeAccess access)
    {
        var host = ctx.Request.Host;
        string name = (host.Host ?? "").ToLowerInvariant();
        if (name.Length == 0) return "http://localhost";
        foreach (string known in new[] { access.PublicUrl, access.TailnetUrl ?? "" })
            if (known.Length > 0 && Uri.TryCreate(known, UriKind.Absolute, out var u) && u.IdnHost.Equals(name, StringComparison.OrdinalIgnoreCase)
                && (host.Port is null || host.Port == u.Port))
                return known.TrimEnd('/');
        string port = host.Port is { } p ? ":" + p : "";
        if (name.EndsWith(".ts.net", StringComparison.Ordinal)) return "https://" + name + (host.Port is null or 443 ? "" : port);
        if (Loopbacks.Contains(name)) return "http://" + name + port;
        if (ctx.Connection.RemoteIpAddress is { } peer && System.Net.IPAddress.IsLoopback(peer)
            && ctx.Request.Headers["X-Forwarded-Host"].ToString() is { Length: > 0 } fh)
        {
            string proto = ctx.Request.Headers["X-Forwarded-Proto"].ToString().Split(',')[0].Trim().ToLowerInvariant();
            return (proto is "https" or "http" ? proto : ctx.Request.Scheme) + "://" + fh.Split(',')[0].Trim().ToLowerInvariant();
        }
        return ctx.Request.Scheme + "://" + name + port;
    }

    /// <summary>A URI the one way it's compared (RFC 8707 audiences): scheme and host lowercased, no default port,
    /// fragment or trailing slash. Null if it isn't an absolute http(s) address.</summary>
    public static string? Canonical(string uri)
    {
        if (!Uri.TryCreate(uri, UriKind.Absolute, out var u) || (u.Scheme != Uri.UriSchemeHttp && u.Scheme != Uri.UriSchemeHttps) || u.UserInfo.Length > 0)
            return null;
        string host = u.HostNameType == UriHostNameType.IPv6 && !u.IdnHost.StartsWith('[') ? $"[{u.IdnHost}]" : u.IdnHost.ToLowerInvariant();
        return $"{u.Scheme}://{host}{(u.IsDefaultPort ? "" : ":" + u.Port)}{u.AbsolutePath.TrimEnd('/')}{u.Query}";
    }

    /// <summary>The MCP server's own address, as the request reached it: what a token used here must be for.</summary>
    public static string ResourceOf(HttpContext ctx, ClaudeAccess access) => Canonical(Base(ctx, access) + McpPath)!;

    /// <summary>Whether a canonical address is this library's MCP server, by any name it answers to.</summary>
    static bool Ours(HttpContext ctx, Config cfg, ClaudeAccess access, string resource)
    {
        if (resource == ResourceOf(ctx, access)) return true;
        foreach (string known in new[] { access.PublicUrl, access.TailnetUrl ?? "", $"http://127.0.0.1:{PortFor(cfg)}", $"http://localhost:{PortFor(cfg)}" })
            if (known.Length > 0 && Canonical(known.TrimEnd('/') + McpPath) == resource) return true;
        return false;
    }

    /// <summary>The client_id of a Basic Authorization header, if one was sent.</summary>
    static string? BasicUser(HttpRequest request)
    {
        string auth = request.Headers.Authorization.ToString();
        if (!auth.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase)) return null;
        try
        {
            string pair = Encoding.UTF8.GetString(Convert.FromBase64String(auth[6..].Trim()));
            return Uri.UnescapeDataString(pair.Split(':')[0]);
        }
        catch (FormatException)
        {
            return null;
        }
    }

    static string Q(IFormCollection? post, HttpContext ctx, string key) =>
        post is not null ? post.Get(key) : ctx.Request.Query[key].ToString();

    /// <summary>The sign-in page: what's asking, and the library's password to allow it. Allowed, it goes back to
    /// Claude with a code; turned down, with access_denied. Every answer that goes back names this server (iss).</summary>
    static async Task<IResult> Authorize(HttpContext ctx, Config cfg, ClaudeAccess access, IFormCollection? post)
    {
        string clientId = Q(post, ctx, "client_id"), redirect = Q(post, ctx, "redirect_uri"), state = Q(post, ctx, "state");
        string challenge = Q(post, ctx, "code_challenge"), method = Q(post, ctx, "code_challenge_method"), resource = Q(post, ctx, "resource");
        var (client, problem) = await access.ClientForAsync(clientId, ctx.RequestAborted);
        // Nothing is sent back to a redirect we can't vouch for: the page says what's wrong instead.
        if (client is null) return Problem(problem!, 400);
        if (!client.RedirectUris.Any(r => ClaudeAccess.RedirectMatches(r, redirect)))
            return Problem("This sign-in link goes somewhere Study Stash doesn't know. Start again from Claude.", 400);
        string Back(string query) => redirect + (redirect.Contains('?') ? "&" : "?") + query
            + (state.Length > 0 ? "&state=" + Uri.EscapeDataString(state) : "") + "&iss=" + Uri.EscapeDataString(Base(ctx, access));
        if (Q(post, ctx, "response_type") != "code") return Results.Redirect(Back("error=unsupported_response_type"));
        if (challenge.Length < 43 || method != "S256") return Results.Redirect(Back("error=invalid_request&error_description=" + Uri.EscapeDataString("PKCE (S256) is required")));
        // The address the tokens will be for (RFC 8707): the one asked for, if it's this library's; else where
        // the sign-in came in.
        string? bound = resource.Length == 0 ? ResourceOf(ctx, access) : Canonical(resource);
        if (bound is null || !Ours(ctx, cfg, access, bound))
            return Results.Redirect(Back("error=invalid_target&error_description=" + Uri.EscapeDataString("resource isn't this library's MCP address")));
        if (cfg.PoolPassword.Length == 0)
            return Problem("Set a password for your library first (in its Settings), so only you can let Claude in.", 403);
        var fields = new[] { ("client_id", clientId), ("redirect_uri", redirect), ("state", state), ("code_challenge", challenge),
            ("code_challenge_method", method), ("resource", resource), ("response_type", "code") };
        string csp = $"default-src 'none'; style-src 'unsafe-inline'; img-src 'self'; form-action 'self' {new Uri(redirect).GetLeftPart(UriPartial.Authority)}; frame-ancestors 'none'; base-uri 'none'";
        if (post is null) return Form(cfg, client, fields, csp, error: null);
        if (post.Get("decision") == "deny") return Results.Redirect(Back("error=access_denied"));
        if (access.LockedOut()) return Form(cfg, client, fields, csp, "Too many wrong passwords. Try again in 15 minutes.", 429);
        string password = post.Get("password");
        bool right = CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(password), Encoding.UTF8.GetBytes(cfg.PoolPassword));
        if (!right)
        {
            access.Failed();
            return Form(cfg, client, fields, csp, "That password isn't right.", 401);
        }
        return Results.Redirect(Back("code=" + Uri.EscapeDataString(access.NewCode(client, redirect, challenge, bound))));
    }

    static IResult Form(Config cfg, ClaudeClient client, (string, string)[] fields, string csp, string? error, int status = 200)
    {
        string hidden = string.Concat(fields.Select(f => $"<input type=\"hidden\" name=\"{f.Item1}\" value=\"{Ui.Esc(f.Item2)}\">"));
        string err = error is null ? "" : $"<p class=\"bad\">{Ui.Esc(error)}</p>";
        // An app with a published identity is named by where it's published: its own name is only its say-so.
        string who = client.Host ?? (client.Name.Length > 0 ? client.Name : "Claude");
        string says = client.Host is not null && client.Name != client.Host ? $"<p>It calls itself {Ui.Esc(client.Name)}.</p>" : "";
        string body = $"""
            <main><form method="post" action="/authorize">
            <img src="/icon.png" alt="" width="56" height="56">
            <h1>Let {Ui.Esc(who)} read your lectures?</h1>
            {says}<p>It will see the notes and transcripts in {Ui.Esc(cfg.PoolName)}. It can't change or delete anything.</p>
            {err}{hidden}
            <input type="password" name="password" placeholder="Library password" aria-label="Library password" autocomplete="current-password" autofocus required>
            <button name="decision" value="allow">Allow</button>
            <button name="decision" value="deny" class="quiet" formnovalidate>Don't allow</button>
            </form></main>
            """;
        return Http.Html(Shell("Allow Claude · Study Stash", body), csp, status);
    }

    static IResult Problem(string text, int status) =>
        Http.Html(Shell("Study Stash", $"<main><form><img src=\"/icon.png\" alt=\"\" width=\"56\" height=\"56\"><h1>Can't sign in</h1><p>{Ui.Esc(text)}</p></form></main>"),
            "default-src 'none'; style-src 'unsafe-inline'; img-src 'self'; frame-ancestors 'none'; base-uri 'none'", status);

    /// <summary>A page in the Study Stash look, light or dark with the system, self-contained (no scripts).</summary>
    static string Shell(string title, string body) => $$$"""
        <!doctype html><html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1">
        <title>{{{Ui.Esc(title)}}}</title><link rel="icon" href="/favicon.ico">
        <style>
        :root{color-scheme:light dark;--bg:#F4F4F4;--card:#fff;--fg:#1D1D1F;--fg2:rgba(0,0,0,.56);--sep:rgba(0,0,0,.1);--accent:#E5484D;--bad:#C4383D}
        @media (prefers-color-scheme:dark){:root{--bg:#161616;--card:#1E1E1E;--fg:#F5F5F7;--fg2:rgba(255,255,255,.58);--sep:rgba(255,255,255,.1);--accent:#EC5D5E;--bad:#F4979A}}
        *{box-sizing:border-box}body{margin:0;min-height:100vh;display:grid;place-items:center;padding:24px;background:var(--bg);color:var(--fg);
        font:15px/1.5 -apple-system,BlinkMacSystemFont,"Segoe UI Variable Text","Segoe UI",system-ui,sans-serif;-webkit-font-smoothing:antialiased}
        main{width:min(380px,100%)}form{display:grid;gap:12px;padding:32px 28px;border-radius:16px;background:var(--card);box-shadow:0 12px 40px rgba(0,0,0,.12),0 0 0 .5px var(--sep);text-align:center}
        img{margin:0 auto 4px;border-radius:12px}h1{font-size:20px;line-height:1.25;margin:0;letter-spacing:-.01em}p{margin:0;color:var(--fg2);font-size:14px}
        p.bad{color:var(--bad)}input{height:40px;padding:0 12px;border-radius:8px;border:1px solid var(--sep);background:transparent;color:inherit;font:inherit}
        input:focus{outline:2px solid var(--accent);outline-offset:1px}button{height:40px;border:0;border-radius:8px;background:var(--accent);color:#fff;font:inherit;font-weight:600;cursor:pointer}
        button.quiet{background:transparent;color:var(--fg2);font-weight:500}button:focus-visible{outline:2px solid var(--accent);outline-offset:2px}
        </style></head><body>{{{body}}}</body></html>
        """;
}
