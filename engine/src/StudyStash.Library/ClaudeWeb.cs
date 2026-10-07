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
/// The library's door for Claude and ChatGPT, on a port of its own (the library's port + 1, on this computer only):
/// the MCP server, and the OAuth sign-in they use to reach it. Tailscale Serve or Funnel puts this port, and nothing
/// else of the library, on https://&lt;library&gt;.ts.net. Each signs in the way any app does: it says who it is (its
/// published identity, or by registering), sends you to the sign-in page, where the library's password allows it,
/// and gets tokens that only read.
/// </summary>
public static class ClaudeWeb
{
    public const string McpPath = "/mcp";

    public static int PortFor(Config cfg) => cfg.WebPort + 1;

    /// <summary>Where the door listens: on this computer only (loopback), never on the network, whatever the
    /// library's own pages do. An app on this computer reaches it at http://127.0.0.1:PORT/mcp with a sign-in or a
    /// token; Tailscale Serve or Funnel, when they're on, pass requests to it from here.</summary>
    public static void ListenHere(Microsoft.AspNetCore.Server.Kestrel.Core.KestrelServerOptions kestrel, int port) =>
        kestrel.Listen(System.Net.IPAddress.Loopback, port);

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
        // Claude, ChatGPT or this library is turned away (a browser always says where a page came from; their own
        // servers send no Origin); only a signed-in app (or a token from Settings) gets to the MCP server; anyone else
        // is told where to sign in (RFC 9728), which is how Claude and ChatGPT find the sign-in on their own. AI tool
        // access being off is the tools' to say, in words, not a refused connection.
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
            // secret it sends along is ignored (none was ever issued). One that signs who it is instead (ChatGPT can:
            // private_key_jwt) is taken at its word like the others: the code and its PKCE verifier are the proof.
            string clientId = form.Get("client_id");
            if (clientId.Length == 0 && BasicUser(ctx.Request) is { Length: > 0 } basic) clientId = basic;
            if (clientId.Length == 0 && AssertedClient(form.Get("client_assertion")) is { Length: > 0 } asserted) clientId = asserted;
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
            "Study Stash: the MCP server for your lecture library.\nAdd " + McpPath + " on this address to Claude or ChatGPT as a connector.\n",
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
    /// names, Claude or ChatGPT on the web, or a program on this computer.</summary>
    public static bool AllowedOrigin(HttpContext ctx, ClaudeAccess access, string origin)
    {
        if (OriginOf(origin) is not { } o) return false;
        if (o.StartsWith("http://localhost:", StringComparison.Ordinal) || o == "http://localhost"
            || o.StartsWith("http://127.0.0.1:", StringComparison.Ordinal) || o == "http://127.0.0.1")
            return true;
        return new[] { Base(ctx, access), access.PublicUrl, access.TailnetUrl ?? "", "https://claude.ai", "https://claude.com", "https://chatgpt.com" }
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

    /// <summary>Who a signed client assertion (RFC 7523) says it's from: its <c>sub</c>, read without checking the
    /// signature, which is all a client_id in the form would be worth too. Null when there's none to read.</summary>
    static string? AssertedClient(string jwt)
    {
        string[] parts = jwt.Split('.');
        if (parts.Length != 3) return null;
        try
        {
            string payload = parts[1].Replace('-', '+').Replace('_', '/');
            payload = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=');
            return JsonNode.Parse(Convert.FromBase64String(payload)) is JsonObject claims && claims["sub"] is JsonValue v && v.TryGetValue(out string? sub) ? sub : null;
        }
        catch (Exception e) when (e is FormatException or System.Text.Json.JsonException)
        {
            return null;
        }
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
    /// the app with a code; turned down, with access_denied. Every answer that goes back names this server (iss).</summary>
    static async Task<IResult> Authorize(HttpContext ctx, Config cfg, ClaudeAccess access, IFormCollection? post)
    {
        // The page is only ever fetched fresh, never framed, and never sent to another site as a referrer.
        ctx.Response.Headers.CacheControl = "no-store";
        ctx.Response.Headers["Referrer-Policy"] = "no-referrer";
        ctx.Response.Headers["X-Frame-Options"] = "DENY";
        string clientId = Q(post, ctx, "client_id"), redirect = Q(post, ctx, "redirect_uri"), state = Q(post, ctx, "state");
        string challenge = Q(post, ctx, "code_challenge"), method = Q(post, ctx, "code_challenge_method"), resource = Q(post, ctx, "resource");
        string scope = Q(post, ctx, "scope");
        var (client, problem) = await access.ClientForAsync(clientId, ctx.RequestAborted);
        // Nothing is sent back to a redirect we can't vouch for: the page says what's wrong instead.
        if (client is null) return Problem(problem!, 400);
        if (!client.RedirectUris.Any(r => ClaudeAccess.RedirectMatches(r, redirect)) || !Uri.TryCreate(redirect, UriKind.Absolute, out var redirectUri))
            return Problem(ClaudeAccess.UnknownLinkMessage, 400);
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
            return Problem("Your library has no password yet. Set one in Study Stash → Settings → Library, then connect again.", 403);
        var fields = new[] { ("client_id", clientId), ("redirect_uri", redirect), ("state", state), ("code_challenge", challenge),
            ("code_challenge_method", method), ("resource", resource), ("response_type", "code"), ("scope", scope) };
        string csp = $"default-src 'none'; style-src 'unsafe-inline'; img-src 'self'; form-action 'self' {redirectUri.GetLeftPart(UriPartial.Authority)}; frame-ancestors 'none'; base-uri 'none'";
        if (post is null) return Form(cfg, access, client, redirectUri, fields, csp, error: null);
        if (post.Get("decision") == "deny") return Results.Redirect(Back("error=access_denied"));
        // The same page either way, so the wording alone can't say whether the lockout or the password check found
        // the problem first.
        if (access.LockedOut()) return Form(cfg, access, client, redirectUri, fields, csp, $"Too many wrong tries. Wait 15 minutes, then connect from {NameOf(client)} again.", 429);
        string password = post.Get("password");
        bool right = CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(password), Encoding.UTF8.GetBytes(cfg.PoolPassword));
        if (!right)
        {
            access.Failed();
            return Form(cfg, access, client, redirectUri, fields, csp, "That isn't your library password. Try again.", 401);
        }
        return Results.Redirect(Back("code=" + Uri.EscapeDataString(access.NewCode(client, redirect, challenge, bound))));
    }

    /// <summary>What the app asking is called on the sign-in page: its own name, or "Claude" when it gave none.</summary>
    static string NameOf(ClaudeClient client) => client.Name.Length > 0 ? client.Name : "Claude";

    /// <summary>A redirect back to a program on this computer (Claude Code's loopback callback), not a browser
    /// somewhere else: the page warns, since anyone could have started that program.</summary>
    static bool IsLoopback(Uri u) => u.Scheme == Uri.UriSchemeHttp && (u.IsLoopback || u.IdnHost.Equals("localhost", StringComparison.OrdinalIgnoreCase));

    static string HostDisplay(Uri u) => u.HostNameType == UriHostNameType.IPv6 ? $"[{u.IdnHost}]" : u.IdnHost;

    static IResult Form(Config cfg, ClaudeAccess access, ClaudeClient client, Uri redirect, (string, string)[] fields, string csp, string? error, int status = 200)
    {
        string hidden = string.Concat(fields.Select(f => $"<input type=\"hidden\" name=\"{f.Item1}\" value=\"{Ui.Esc(f.Item2)}\">"));
        string err = error is null ? "" : $"<p class=\"bad\" role=\"alert\">{Ui.Esc(error)}</p>";
        // Anyone can claim any name at registration; a published identity (CIMD) is named by where it's published too.
        string name = NameOf(client);
        string identifiedBy = client.Host is not null ? $"<p class=\"who\">Identified by {Ui.Esc(client.Host)}</p>" : "";
        string warn = IsLoopback(redirect)
            ? "<p class=\"warn\">This goes back to a program on this computer (localhost). Allow it only if you just started it yourself.</p>" : "";
        var reading = access.Reading;
        var items = new List<string>();
        if (reading.Lectures) items.Add("Lectures and transcripts");
        if (reading.Notes) items.Add("Study notes");
        if (reading.Canvas) items.Add("Canvas assignments and files");
        string list = items.Count > 0 ? $"<ul class=\"reading\">{string.Concat(items.Select(i => $"<li>{Ui.Esc(i)}</li>"))}</ul>" : "";
        string toolsOff = access.ToolsOn ? "" : $"<p class=\"note\">Reading is switched off for AI apps in Study Stash (Settings → AI apps), so {Ui.Esc(name)} won't see anything until you turn it on.</p>";
        string body = $"""
            <main><form method="post" action="/authorize">
            <img src="/icon.png" alt="" width="56" height="56">
            <p class="lib">{Ui.Esc(cfg.PoolName)}</p>
            <h1>{Ui.Esc(name)} wants to read your lectures</h1>
            <p class="who">{Ui.Esc(name)} · will return to {Ui.Esc(HostDisplay(redirect))}</p>
            {identifiedBy}{warn}
            {list}
            <p class="cant">It can't change or delete anything.</p>
            {toolsOff}{hidden}
            <label for="pw">Library password</label>
            <input id="pw" type="password" name="password" autocomplete="current-password" autofocus required>
            {err}
            <div class="btns">
            <button name="decision" value="allow">Allow</button>
            <button name="decision" value="deny" class="quiet" formnovalidate>Deny</button>
            </div>
            </form></main>
            """;
        return Http.Html(Shell($"{name} · {cfg.PoolName}", body), csp, status);
    }

    static IResult Problem(string text, int status) =>
        Http.Html(Shell("Study Stash", $"<main><form><img src=\"/icon.png\" alt=\"\" width=\"56\" height=\"56\"><h1>Can't sign in</h1><p role=\"alert\">{Ui.Esc(text)}</p></form></main>"),
            "default-src 'none'; style-src 'unsafe-inline'; img-src 'self'; frame-ancestors 'none'; base-uri 'none'", status);

    /// <summary>A page in the Study Stash look (the Lagoon accent), light or dark with the system, self-contained (no
    /// scripts, so the CSP can hold to <c>default-src 'none'</c>).</summary>
    static string Shell(string title, string body) => $$$"""
        <!doctype html><html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1, viewport-fit=cover">
        <title>{{{Ui.Esc(title)}}}</title><link rel="icon" href="/favicon.ico">
        <style>
        :root{color-scheme:light dark;--bg:#F4F4F4;--card:#fff;--fg:#1D1D1F;--fg2:rgba(0,0,0,.56);--fg3:rgba(0,0,0,.4);--sep:rgba(0,0,0,.1);
        --accent:#008F90;--accentText:#007172;--bad:#C4383D;--warn:#8A5300}
        @media (prefers-color-scheme:dark){:root{--bg:#161616;--card:#1E1E1E;--fg:#F5F5F7;--fg2:rgba(255,255,255,.6);--fg3:rgba(255,255,255,.4);
        --sep:rgba(255,255,255,.12);--accent:#00A8A9;--accentText:#6AD1D1;--bad:#F4979A;--warn:#F2AF48}}
        *{box-sizing:border-box}html,body{width:100%}body{margin:0;min-height:100vh;display:flex;justify-content:center;align-items:center;
        padding:24px 16px;background:var(--bg);color:var(--fg);
        font:15px/1.5 -apple-system,BlinkMacSystemFont,"Segoe UI Variable Text","Segoe UI",system-ui,sans-serif;-webkit-font-smoothing:antialiased}
        main{width:100%;max-width:400px}form{width:100%;display:grid;gap:10px;padding:28px 24px;border-radius:16px;background:var(--card);
        box-shadow:0 12px 40px rgba(0,0,0,.12),0 0 0 .5px var(--sep);text-align:center}
        img{margin:0 auto 2px;border-radius:12px}p.lib{margin:0;color:var(--fg3);font-size:12px;font-weight:600;text-transform:uppercase;letter-spacing:.04em}
        h1{font-size:19px;line-height:1.3;margin:2px 0 0;letter-spacing:-.01em}p{margin:0;color:var(--fg2);font-size:13px}
        p.who{font-size:12.5px}p.warn{color:var(--warn)}p.note{color:var(--warn)}p.bad{color:var(--bad);font-weight:600}
        ul.reading{margin:6px 0 0;padding:0 0 0 18px;text-align:left;font-size:13px;color:var(--fg2)}ul.reading li{margin:2px 0}
        p.cant{font-size:12.5px}
        label{font-size:12.5px;color:var(--fg2);text-align:left;margin-top:6px}
        input{height:44px;padding:0 12px;border-radius:8px;border:1px solid var(--sep);background:transparent;color:inherit;font-size:16px;font-family:inherit}
        input:focus{outline:2px solid var(--accent);outline-offset:1px}
        .btns{display:flex;gap:10px}
        button{flex:1;height:44px;border:0;border-radius:8px;background:var(--accent);color:#fff;font:inherit;font-weight:600;cursor:pointer}
        button.quiet{background:transparent;color:var(--fg2);font-weight:500;box-shadow:inset 0 0 0 1px var(--sep)}
        button:focus-visible{outline:2px solid var(--accent);outline-offset:2px}
        @media (max-width:360px){.btns{flex-direction:column}}
        </style></head><body>{{{body}}}</body></html>
        """;
}
