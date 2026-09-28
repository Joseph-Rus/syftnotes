using System.Net;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using ModelContextProtocol.Client;
using StudyStash.Library;

namespace StudyStash.Core.Tests;

/// <summary>Before any test runs: nothing may fetch a client document from the internet (ClaudeAccess's real
/// fetcher refuses while this is set).</summary>
static class NoInternetFetch
{
#pragma warning disable CA2255 // the tests are the application here: this has to run before any of them
    [ModuleInitializer]
    internal static void Set() => Environment.SetEnvironmentVariable("STUDYSTASH_TESTS", "1");
#pragma warning restore CA2255
}

/// <summary>Claude's custom connector, the way Claude on the web, the desktop app and Claude Code sign in: discovery,
/// its published identity (CIMD) or registration (DCR), PKCE, the address a token is for, refresh, and signing out.</summary>
public class ConnectorTests
{
    const string Ts = "https://mini.tail1234.ts.net";
    const string ClaudeCodeDoc = "https://claude.ai/oauth/claude-code-client-metadata";
    const string HostedDoc = "https://clients.example/claude.json";
    const string WebCallback = "https://claude.ai/api/mcp/auth_callback";

    static string Doc(string clientId, string name, params string[] redirects) => new JsonObject
    {
        ["client_id"] = clientId, ["client_name"] = name, ["token_endpoint_auth_method"] = "none",
        ["grant_types"] = new JsonArray("authorization_code", "refresh_token"), ["response_types"] = new JsonArray("code"),
        ["redirect_uris"] = new JsonArray(redirects.Select(r => (JsonNode?)r).ToArray()),
    }.ToJsonString();

    /// <summary>The Claude port of "Sam's library", with a clock the test moves and client documents the test
    /// hands out (the fetch is always faked).</summary>
    sealed class Door : IAsyncDisposable
    {
        public readonly TempDir Dir = new();
        public readonly Config Cfg;
        public readonly Store Store;
        public readonly ClaudeAccess Access;
        public readonly Dictionary<string, string> Docs = [];
        public readonly List<string> Fetched = [];
        public DateTimeOffset Now = DateTimeOffset.Parse("2026-09-23T10:00:00Z");
        public TestSite Site = null!;

        Door()
        {
            Cfg = new Config(Dir["home"], Dir["pool"])
            {
                PoolName = "Sam's library", PoolPassword = "pw", OllamaEnabled = false, Classes = [new ClassDef("CS 101", []), new ClassDef("BIO 110", [])],
            };
            Directory.CreateDirectory(Cfg.Home);
            Store = new Store(Cfg.DbPath, Cfg.PoolDir);
            Access = new ClaudeAccess(Cfg.Home, () => Now)
            {
                FetchClientDocument = (url, _) =>
                {
                    Fetched.Add(url.AbsoluteUri);
                    return Task.FromResult(Docs.GetValueOrDefault(url.AbsoluteUri));
                },
            };
        }

        /// <summary>The door open; <paramref name="canvas"/> links Canvas (so the Canvas tools are there), and
        /// <paramref name="files"/> gives it folders to read (read_file) under the door's own temp folder.</summary>
        public static async Task<Door> OpenAsync(bool canvas = false, IReadOnlyList<(string Name, string Path, bool Private)>? files = null)
        {
            var door = new Door();
            var sync = canvas ? new StudyStash.Core.Canvas.CanvasSync(door.Cfg.Home, c => door.Dir[c]) : null;
            StudyStash.Core.Ai.FileIndex? index = null;
            if (files is not null)
            {
                index = new StudyStash.Core.Ai.FileIndex(door.Cfg.Home, () => files, () => []);
                await index.UpdateAsync();
            }
            door.Site = await TestSite.StartAsync(b => ClaudeWeb.Build(b, door.Cfg, new LibraryReader(door.Cfg, door.Store), door.Access, sync, index));
            return door;
        }

        /// <summary>A browser that reaches the door as <paramref name="origin"/> (the Host it sends, and plain http
        /// or https as given): that's how Tailscale's proxy hands requests on.</summary>
        public HttpClient As(string origin) => new(Site.App.GetTestServer().CreateHandler()) { BaseAddress = new Uri(origin) };

        public async ValueTask DisposeAsync()
        {
            await Site.DisposeAsync();
            Store.Dispose();
            Dir.Dispose();
        }
    }

    static string Verifier() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(40)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    static string Challenge(string v) => Convert.ToBase64String(SHA256.HashData(Encoding.ASCII.GetBytes(v))).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    static FormUrlEncodedContent Form(params (string, string)[] f) => new(f.Select(x => KeyValuePair.Create(x.Item1, x.Item2)));

    static string Query((string, string)[] fields) => string.Join("&", fields.Select(f => $"{f.Item1}={Uri.EscapeDataString(f.Item2)}"));

    static (string, string)[] Ask(string clientId, string redirect, string verifier, string? resource = Ts + "/mcp", string state = "st8") =>
    [
        ("response_type", "code"), ("client_id", clientId), ("redirect_uri", redirect), ("state", state),
        ("code_challenge", Challenge(verifier)), ("code_challenge_method", "S256"), .. resource is null ? Array.Empty<(string, string)>() : [("resource", resource)],
    ];

    static async Task<JsonNode> Json(HttpResponseMessage r)
    {
        Assert.True(r.IsSuccessStatusCode, $"{(int)r.StatusCode}: {await r.Content.ReadAsStringAsync()}");
        return JsonNode.Parse(await r.Content.ReadAsStringAsync())!;
    }

    static async Task<(HttpStatusCode Status, string Error)> Refused(HttpResponseMessage r) =>
        (r.StatusCode, JsonNode.Parse(await r.Content.ReadAsStringAsync())!["error"]!.GetValue<string>());

    /// <summary>The password typed and Allow pressed: where the browser is sent back to.</summary>
    static async Task<Uri> Allow(HttpClient c, (string, string)[] fields)
    {
        var r = await c.PostAsync("/authorize", Form([.. fields, ("password", "pw"), ("decision", "allow")]));
        Assert.True(r.StatusCode == HttpStatusCode.Redirect, $"{(int)r.StatusCode}: {await r.Content.ReadAsStringAsync()}");
        return r.Headers.Location!;
    }

    static System.Collections.Specialized.NameValueCollection Params(Uri back) => System.Web.HttpUtility.ParseQueryString(back.Query);

    static Task<HttpResponseMessage> Swap(HttpClient c, string code, string verifier, string clientId, string redirect, string? resource = null) =>
        c.PostAsync("/token", Form([("grant_type", "authorization_code"), ("code", code), ("code_verifier", verifier), ("client_id", clientId),
            ("redirect_uri", redirect), .. resource is null ? Array.Empty<(string, string)>() : [("resource", resource)]]));

    static Task<HttpResponseMessage> Renew(HttpClient c, string refresh, string clientId, string? resource = null) =>
        c.PostAsync("/token", Form([("grant_type", "refresh_token"), ("refresh_token", refresh), ("client_id", clientId),
            .. resource is null ? Array.Empty<(string, string)>() : [("resource", resource)]]));

    static HttpRequestMessage Mcp(string? token)
    {
        var r = new HttpRequestMessage(HttpMethod.Post, "/mcp")
        {
            Headers = { Accept = { new("application/json"), new("text/event-stream") } },
            Content = JsonContent.Create(new { jsonrpc = "2.0", id = 1, method = "tools/list" }),
        };
        if (token is not null) r.Headers.Authorization = new("Bearer", token);
        return r;
    }

    /// <summary>A whole sign-in by claude.ai through the internet address, with its published identity: the tokens.</summary>
    static async Task<(string Access, string Refresh)> SignIn(Door door, HttpClient c)
    {
        door.Docs[HostedDoc] = Doc(HostedDoc, "Claude", WebCallback);
        string verifier = Verifier();
        var back = Params(await Allow(c, Ask(HostedDoc, WebCallback, verifier)));
        var tokens = await Json(await Swap(c, back["code"]!, verifier, HostedDoc, WebCallback));
        return (tokens["access_token"]!.GetValue<string>(), tokens["refresh_token"]!.GetValue<string>());
    }

    // --- discovery ---------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Discovery_advertises_every_field_on_the_ts_net_https_address()
    {
        await using var door = await Door.OpenAsync();
        // Tailscale's proxy passes requests on over plain http, and no public address was recorded (Funnel was turned
        // on outside the app): the name alone says https.
        var c = door.As("http://mini.tail1234.ts.net");
        var server = (JsonObject)await Json(await c.GetAsync("/.well-known/oauth-authorization-server"));
        Assert.Equal(Ts, server["issuer"]!.GetValue<string>());
        Assert.Equal(Ts + "/authorize", server["authorization_endpoint"]!.GetValue<string>());
        Assert.Equal(Ts + "/token", server["token_endpoint"]!.GetValue<string>());
        Assert.Equal(Ts + "/register", server["registration_endpoint"]!.GetValue<string>());
        Assert.Equal(Ts + "/revoke", server["revocation_endpoint"]!.GetValue<string>());
        Assert.Equal("[\"none\"]", server["token_endpoint_auth_methods_supported"]!.ToJsonString());
        Assert.Equal("[\"none\"]", server["revocation_endpoint_auth_methods_supported"]!.ToJsonString());
        Assert.Equal("[\"S256\"]", server["code_challenge_methods_supported"]!.ToJsonString());
        Assert.Equal("[\"authorization_code\",\"refresh_token\"]", server["grant_types_supported"]!.ToJsonString());
        Assert.Equal("[\"code\"]", server["response_types_supported"]!.ToJsonString());
        Assert.True(server["client_id_metadata_document_supported"]!.GetValue<bool>());
        Assert.True(server["authorization_response_iss_parameter_supported"]!.GetValue<bool>());
        Assert.Equal(server.ToJsonString(), (await Json(await c.GetAsync("/.well-known/openid-configuration"))).ToJsonString());
        Assert.Equal(server.ToJsonString(), (await Json(await c.GetAsync("/.well-known/oauth-authorization-server/mcp"))).ToJsonString());

        var resource = await Json(await c.GetAsync("/.well-known/oauth-protected-resource/mcp"));
        Assert.Equal(Ts + "/mcp", resource["resource"]!.GetValue<string>());
        Assert.Equal($"[\"{Ts}\"]", resource["authorization_servers"]!.ToJsonString());
        Assert.Equal("[\"library:read\"]", resource["scopes_supported"]!.ToJsonString());
        Assert.Equal("[\"header\"]", resource["bearer_methods_supported"]!.ToJsonString());
        Assert.Equal("Study Stash", resource["resource_name"]!.GetValue<string>());
        Assert.EndsWith("/docs/claude-connector.md", resource["resource_documentation"]!.GetValue<string>());

        // Every address in the 401 is the https one too; a token that doesn't work says so.
        var none = await c.SendAsync(Mcp(null));
        Assert.Equal(HttpStatusCode.Unauthorized, none.StatusCode);
        string challenge = none.Headers.WwwAuthenticate.ToString();
        Assert.Contains($"resource_metadata=\"{Ts}/.well-known/oauth-protected-resource/mcp\"", challenge);
        Assert.Contains("scope=\"library:read\"", challenge);
        Assert.DoesNotContain("error=", challenge);
        Assert.Equal((HttpStatusCode.Unauthorized, "invalid_token"), await Refused(none));
        var bad = await c.SendAsync(Mcp("ssa_made_up"));
        Assert.Equal(HttpStatusCode.Unauthorized, bad.StatusCode);
        Assert.Contains("error=\"invalid_token\"", bad.Headers.WwwAuthenticate.ToString());
        Assert.Contains("error_description=", bad.Headers.WwwAuthenticate.ToString());

        // The address the app recorded wins for its own name; this computer is plain http.
        door.Access.PublicUrl = Ts + "/";
        Assert.Equal(Ts, (await Json(await c.GetAsync("/.well-known/oauth-authorization-server")))["issuer"]!.GetValue<string>());
        Assert.Equal("http://127.0.0.1:8766", (await Json(await door.As("http://127.0.0.1:8766").GetAsync("/.well-known/oauth-authorization-server")))["issuer"]!.GetValue<string>());
    }

    static async Task<string> IssuerFrom(Door door, string host, IPAddress peer, params (string Name, string Value)[] headers)
    {
        var ctx = await door.Site.App.GetTestServer().SendAsync(x =>
        {
            x.Request.Method = "GET";
            x.Request.Scheme = "http";
            x.Request.Host = new HostString(host);
            x.Request.Path = "/.well-known/oauth-authorization-server";
            x.Connection.RemoteIpAddress = peer;
            foreach (var (name, value) in headers) x.Request.Headers[name] = value;
        });
        string body = await new StreamReader(ctx.Response.Body).ReadToEndAsync();
        return JsonNode.Parse(body)!["issuer"]!.GetValue<string>();
    }

    [Fact]
    public async Task Forwarded_headers_count_only_from_a_proxy_on_this_computer()
    {
        await using var door = await Door.OpenAsync();
        var stranger = IPAddress.Parse("203.0.113.9");
        (string, string)[] spoof = [("X-Forwarded-Host", "evil.example"), ("X-Forwarded-Proto", "https")];
        Assert.Equal("http://library.lan:8766", await IssuerFrom(door, "library.lan:8766", stranger, spoof));
        // Through Funnel the peer is this computer, but the name it came to still decides.
        Assert.Equal(Ts, await IssuerFrom(door, "mini.tail1234.ts.net", IPAddress.Loopback, spoof));
        Assert.Equal("http://localhost", await IssuerFrom(door, "localhost", IPAddress.Loopback, spoof));
        // A proxy of the student's own, on this computer, is believed.
        Assert.Equal("https://library.example", await IssuerFrom(door, "library.lan:8766", IPAddress.Loopback,
            ("X-Forwarded-Host", "library.example"), ("X-Forwarded-Proto", "https")));
    }

    [Fact]
    public void Addresses_compare_in_one_canonical_form()
    {
        Assert.Equal("https://mini.tail1234.ts.net/mcp", ClaudeWeb.Canonical("HTTPS://Mini.Tail1234.TS.net:443/mcp/"));
        Assert.Equal("http://localhost:8766/mcp", ClaudeWeb.Canonical("http://LOCALHOST:8766/mcp#top"));
        Assert.Equal("https://mini.tail1234.ts.net", ClaudeWeb.Canonical("https://mini.tail1234.ts.net/"));
        Assert.Null(ClaudeWeb.Canonical("ftp://mini.tail1234.ts.net/mcp"));
        Assert.Null(ClaudeWeb.Canonical("/mcp"));
    }

    // --- Claude's published identity (CIMD) ----------------------------------------------------------------------------

    [Fact]
    public async Task Claude_signs_in_with_its_published_identity()
    {
        await using var door = await Door.OpenAsync();
        var c = door.As("http://mini.tail1234.ts.net");
        door.Docs[HostedDoc] = Doc(HostedDoc, "Claude", WebCallback);
        string verifier = Verifier();
        var fields = Ask(HostedDoc, WebCallback, verifier);
        var page = await c.GetAsync("/authorize?" + Query(fields));
        string html = await page.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        // Named by where its identity is published; what it calls itself comes second.
        Assert.Contains("Let clients.example read your lectures?", html);
        Assert.Contains("It calls itself Claude.", html);

        var back = await Allow(c, fields);
        Assert.StartsWith(WebCallback + "?code=", back.ToString());
        var q = Params(back);
        Assert.Equal("st8", q["state"]);
        Assert.Equal(Ts, q["iss"]);
        var token = await c.PostAsync("/token", Form(("grant_type", "authorization_code"), ("code", q["code"]!), ("code_verifier", verifier),
            ("client_id", HostedDoc), ("redirect_uri", WebCallback), ("resource", Ts + "/mcp")));
        Assert.Equal("no-store", token.Headers.CacheControl!.ToString());
        Assert.Contains("no-cache", token.Headers.Pragma.ToString());
        var tokens = await Json(token);
        Assert.Equal("library:read", tokens["scope"]!.GetValue<string>());

        await using (var mcp = await McpClient.CreateAsync(new HttpClientTransport(new HttpClientTransportOptions
        {
            Endpoint = new Uri(Ts + "/mcp"), TransportMode = HttpTransportMode.StreamableHttp,
            AdditionalHeaders = new Dictionary<string, string> { ["Authorization"] = "Bearer " + tokens["access_token"]!.GetValue<string>() },
        }, door.As("http://mini.tail1234.ts.net"), ownsHttpClient: true)))
            Assert.Contains("list_classes", (await mcp.ListToolsAsync()).Select(t => t.Name));

        var grant = door.Access.Grants().Single();
        Assert.Equal(("Claude", "clients.example", Ts + "/mcp"), (grant.Name, grant.ClientHost, grant.Resource));
    }

    [Fact]
    public async Task A_document_that_doesnt_match_gets_a_problem_page_and_no_redirect()
    {
        await using var door = await Door.OpenAsync();
        var c = door.As("http://mini.tail1234.ts.net");
        async Task Problem(string clientId, string redirect, string words)
        {
            var r = await c.GetAsync("/authorize?" + Query(Ask(clientId, redirect, Verifier())));
            Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
            Assert.Null(r.Headers.Location);
            Assert.Contains(words, await r.Content.ReadAsStringAsync());
            var post = await c.PostAsync("/authorize", Form([.. Ask(clientId, redirect, Verifier()), ("password", "pw"), ("decision", "allow")]));
            Assert.Equal(HttpStatusCode.BadRequest, post.StatusCode);
            Assert.Null(post.Headers.Location);
        }

        door.Docs[HostedDoc] = Doc("https://clients.example/someone-else.json", "Claude", WebCallback);
        await Problem(HostedDoc, WebCallback, "aren&#x27;t right");
        door.Docs[HostedDoc] = Doc(HostedDoc, "Claude", WebCallback);
        await Problem(HostedDoc, "https://evil.example/cb", "goes somewhere Study Stash doesn");
        door.Docs[HostedDoc] = Doc(HostedDoc, "Claude", "http://evil.example/cb");
        await Problem(HostedDoc, "http://evil.example/cb", "aren&#x27;t right");
        door.Docs[HostedDoc] = new JsonObject { ["client_id"] = HostedDoc, ["redirect_uris"] = new JsonArray(WebCallback), ["token_endpoint_auth_method"] = "private_key_jwt" }.ToJsonString();
        await Problem(HostedDoc, WebCallback, "aren&#x27;t right");
        door.Docs[HostedDoc] = "<html>not json</html>";
        await Problem(HostedDoc, WebCallback, "aren&#x27;t right");
        door.Docs.Remove(HostedDoc);
        await Problem(HostedDoc, WebCallback, "couldn&#x27;t read the app&#x27;s details from clients.example");
        // Not a CIMD address (no path, or not https): just an unknown client.
        await Problem("https://clients.example", WebCallback, "isn&#x27;t one Study Stash knows");
        await Problem("http://clients.example/claude.json", WebCallback, "isn&#x27;t one Study Stash knows");
        Assert.DoesNotContain("https://clients.example/", door.Fetched);
    }

    [Fact]
    public async Task Claude_codes_loopback_callback_passes_on_any_port_and_look_alikes_fail()
    {
        await using var door = await Door.OpenAsync();
        var c = door.As("http://localhost");
        door.Docs[ClaudeCodeDoc] = Doc(ClaudeCodeDoc, "Claude Code", "http://localhost/callback", "http://127.0.0.1/callback");
        foreach (string ok in new[] { "http://localhost:51234/callback", "http://127.0.0.1:40000/callback", "http://localhost/callback" })
            Assert.Equal(HttpStatusCode.OK, (await c.GetAsync("/authorize?" + Query(Ask(ClaudeCodeDoc, ok, Verifier(), resource: null)))).StatusCode);
        foreach (string bad in new[] { "http://localhost.evil/callback", "http://localhost:51234/callback/x", "http://localhost:51234/Callback",
                     "https://claude.ai/api/mcp/auth_callback/x", "https://localhost:51234/callback", "http://[::1]:51234/callback",
                     "http://localhost:51234/callback?next=1", "http://user@localhost:51234/callback" })
        {
            var r = await c.GetAsync("/authorize?" + Query(Ask(ClaudeCodeDoc, bad, Verifier(), resource: null)));
            Assert.True(r.StatusCode == HttpStatusCode.BadRequest, bad);
            Assert.Null(r.Headers.Location);
        }

        // The whole sign-in on a port of the moment, bound to this computer's address; /token wants the redirect
        // exactly as it was asked for.
        string verifier = Verifier();
        var back = await Allow(c, Ask(ClaudeCodeDoc, "http://localhost:51234/callback", verifier, resource: null));
        Assert.Equal(51234, back.Port);
        string code = Params(back)["code"]!;
        Assert.Equal((HttpStatusCode.BadRequest, "invalid_grant"), await Refused(await Swap(c, code, verifier, ClaudeCodeDoc, "http://localhost/callback")));
        back = await Allow(c, Ask(ClaudeCodeDoc, "http://localhost:51234/callback", verifier, resource: null));
        var tokens = await Json(await Swap(c, Params(back)["code"]!, verifier, ClaudeCodeDoc, "http://localhost:51234/callback"));
        Assert.Equal(HttpStatusCode.OK, (await c.SendAsync(Mcp(tokens["access_token"]!.GetValue<string>()))).StatusCode);
        Assert.Equal("http://localhost/mcp", door.Access.Grants().Single().Resource);
        Assert.Equal("claude.ai", door.Access.Grants().Single().ClientHost); // named by its document's host
    }

    [Fact]
    public void Redirects_match_exactly_or_on_loopback_on_any_port()
    {
        Assert.True(ClaudeAccess.RedirectMatches(WebCallback, WebCallback));
        Assert.False(ClaudeAccess.RedirectMatches(WebCallback, WebCallback + "/"));
        Assert.False(ClaudeAccess.RedirectMatches("https://claude.ai/cb", "https://claude.ai:8443/cb"));
        Assert.True(ClaudeAccess.RedirectMatches("http://localhost/callback", "http://localhost:8080/callback"));
        Assert.True(ClaudeAccess.RedirectMatches("http://127.0.0.1:3000/callback", "http://127.0.0.1:59999/callback"));
        Assert.True(ClaudeAccess.RedirectMatches("http://[::1]/callback", "http://[::1]:4000/callback"));
        Assert.False(ClaudeAccess.RedirectMatches("http://localhost/callback", "http://127.0.0.1:8080/callback"));
        Assert.False(ClaudeAccess.RedirectMatches("http://localhost/callback", "http://localhost.evil:8080/callback"));
        Assert.False(ClaudeAccess.RedirectMatches("http://localhost/callback", "http://localhost:8080/callback2"));
        Assert.False(ClaudeAccess.RedirectMatches("http://localhost/callback", "http://localhost:8080/callback#x"));
        Assert.False(ClaudeAccess.RedirectMatches("http://example.com/callback", "http://example.com:8080/callback"));
    }

    // --- the guarded fetch ---------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("8.8.8.8", true)]
    [InlineData("1.1.1.1", true)]
    [InlineData("172.32.0.1", true)]
    [InlineData("2606:4700:4700::1111", true)]
    [InlineData("10.1.2.3", false)]
    [InlineData("172.16.0.1", false)]
    [InlineData("192.168.1.20", false)]
    [InlineData("100.100.100.100", false)]
    [InlineData("127.0.0.1", false)]
    [InlineData("169.254.169.254", false)]
    [InlineData("0.0.0.0", false)]
    [InlineData("224.0.0.1", false)]
    [InlineData("::1", false)]
    [InlineData("fe80::1", false)]
    [InlineData("fd7a:115c:a1e0::1", false)]
    [InlineData("::ffff:10.0.0.1", false)]
    [InlineData("2002:0a00:0001::1", false)]
    [InlineData("2001:db8::1", false)]
    public void Only_public_addresses_are_global(string ip, bool global) => Assert.Equal(global, PublicAddress.IsGlobal(IPAddress.Parse(ip)));

    /// <summary>Answers the fetch without a network, and notes which address it was pinned to.</summary>
    sealed class Canned(Func<HttpResponseMessage> answer) : HttpMessageHandler
    {
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(answer());
        }
    }

    [Fact]
    public async Task The_fetch_refuses_private_addresses_redirects_and_big_documents_and_keeps_what_it_read()
    {
        string body = Doc(HostedDoc, "Claude", WebCallback);
        var now = DateTimeOffset.Parse("2026-09-23T10:00:00Z");
        var pinned = new List<IPAddress>();
        HttpResponseMessage Answer() => new(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
            Headers = { CacheControl = new() { MaxAge = TimeSpan.FromSeconds(30) } },
        };
        var canned = new Canned(Answer);
        ClientDocuments Fetcher(params string[] ips) => new()
        {
            Resolve = (_, _) => Task.FromResult(ips.Select(IPAddress.Parse).ToArray()),
            Handler = ip =>
            {
                pinned.Add(ip);
                return canned;
            },
            Clock = () => now,
        };

        Assert.Null(await Fetcher("10.0.0.5").FetchAsync(new Uri(HostedDoc), default));
        Assert.Null(await Fetcher("8.8.8.8", "192.168.1.20").FetchAsync(new Uri(HostedDoc), default));
        Assert.Null(await Fetcher("100.101.102.103").FetchAsync(new Uri(HostedDoc), default));
        Assert.Null(await Fetcher("8.8.8.8").FetchAsync(new Uri("http://clients.example/claude.json"), default));
        Assert.Null(await Fetcher("8.8.8.8").FetchAsync(new Uri("https://clients.example:8443/claude.json"), default));
        Assert.Empty(pinned);

        var fetcher = Fetcher("8.8.8.8");
        Assert.Equal(body, await fetcher.FetchAsync(new Uri(HostedDoc), default));
        Assert.Equal([IPAddress.Parse("8.8.8.8")], pinned);
        // Kept for 5 minutes at least (it asked for 30 seconds), then read again.
        now += TimeSpan.FromMinutes(4);
        Assert.Equal(body, await fetcher.FetchAsync(new Uri(HostedDoc), default));
        Assert.Equal(1, canned.Calls);
        now += TimeSpan.FromMinutes(2);
        Assert.Equal(body, await fetcher.FetchAsync(new Uri(HostedDoc), default));
        Assert.Equal(2, canned.Calls);

        var moved = new ClientDocuments
        {
            Resolve = (_, _) => Task.FromResult(new[] { IPAddress.Parse("8.8.8.8") }),
            Handler = _ => new Canned(() => new HttpResponseMessage(HttpStatusCode.Found) { Headers = { Location = new Uri("https://169.254.169.254/") } }),
        };
        Assert.Null(await moved.FetchAsync(new Uri(HostedDoc), default));
        var big = new ClientDocuments
        {
            Resolve = (_, _) => Task.FromResult(new[] { IPAddress.Parse("8.8.8.8") }),
            Handler = _ => new Canned(() => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new MemoryStream(new byte[ClientDocuments.MaxBytes + 1])) }),
        };
        Assert.Null(await big.FetchAsync(new Uri(HostedDoc), default));
    }

    // --- DCR -----------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Registration_takes_what_clients_send_and_answers_as_a_public_client()
    {
        await using var door = await Door.OpenAsync();
        var c = door.As("http://mini.tail1234.ts.net");
        var reg = await Json(await c.PostAsync("/register", JsonContent.Create(new
        {
            client_name = "Claude", redirect_uris = new[] { WebCallback }, application_type = "web", scope = "library:read",
            grant_types = new[] { "authorization_code", "refresh_token", "client_credentials" }, token_endpoint_auth_method = "client_secret_basic",
        })));
        Assert.Equal("none", reg["token_endpoint_auth_method"]!.GetValue<string>());
        Assert.Equal("library:read", reg["scope"]!.GetValue<string>());
        Assert.Null(reg["client_secret"]);
        string clientId = reg["client_id"]!.GetValue<string>();

        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsync("/register", JsonContent.Create(new
        {
            redirect_uris = Enumerable.Range(0, 11).Select(i => $"https://claude.ai/cb{i}").ToArray(),
        }))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsync("/register", JsonContent.Create(new
        {
            redirect_uris = new[] { "https://claude.ai/" + new string('a', 2000) },
        }))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsync("/register", new StringContent("not json"))).StatusCode);

        // A client that sends its id as Basic auth, with a secret it was never given, is read the same way.
        string verifier = Verifier();
        var back = Params(await Allow(c, Ask(clientId, WebCallback, verifier)));
        var swap = new HttpRequestMessage(HttpMethod.Post, "/token")
        {
            Content = Form(("grant_type", "authorization_code"), ("code", back["code"]!), ("code_verifier", verifier), ("redirect_uri", WebCallback)),
        };
        swap.Headers.Authorization = new("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes(clientId + ":whatever")));
        Assert.NotNull((await Json(await c.SendAsync(swap)))["access_token"]);
    }

    // --- the address a token is for (RFC 8707) --------------------------------------------------------------------------

    [Fact]
    public async Task A_token_is_for_one_address()
    {
        await using var door = await Door.OpenAsync();
        var c = door.As("http://mini.tail1234.ts.net");
        var (access, _) = await SignIn(door, c);
        Assert.Equal(HttpStatusCode.OK, (await c.SendAsync(Mcp(access))).StatusCode);
        var elsewhere = await door.As("http://other.tail1234.ts.net").SendAsync(Mcp(access));
        Assert.Equal(HttpStatusCode.Unauthorized, elsewhere.StatusCode);
        Assert.Contains("error=\"invalid_token\"", elsewhere.Headers.WwwAuthenticate.ToString());
        Assert.Equal(HttpStatusCode.Unauthorized, (await door.As("http://localhost").SendAsync(Mcp(access))).StatusCode);

        // A token from Settings reads through any of them.
        var (token, _) = door.Access.CreateToken("Cursor");
        Assert.Equal(HttpStatusCode.OK, (await door.As("http://other.tail1234.ts.net").SendAsync(Mcp(token))).StatusCode);
    }

    [Fact]
    public async Task A_wrong_resource_gets_invalid_target()
    {
        await using var door = await Door.OpenAsync();
        var c = door.As("http://mini.tail1234.ts.net");
        door.Docs[HostedDoc] = Doc(HostedDoc, "Claude", WebCallback);
        string verifier = Verifier();

        var r = await c.GetAsync("/authorize?" + Query(Ask(HostedDoc, WebCallback, verifier, resource: "https://evil.example/mcp")));
        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
        var q = Params(r.Headers.Location!);
        Assert.Equal("invalid_target", q["error"]);
        Assert.Equal(Ts, q["iss"]);
        Assert.Equal("st8", q["state"]);

        // Its own address in another form is the same one.
        var back = Params(await Allow(c, Ask(HostedDoc, WebCallback, verifier, resource: "HTTPS://mini.tail1234.ts.net:443/mcp/")));
        Assert.Equal((HttpStatusCode.BadRequest, "invalid_target"), await Refused(await Swap(c, back["code"]!, verifier, HostedDoc, WebCallback, "https://evil.example/mcp")));
        back = Params(await Allow(c, Ask(HostedDoc, WebCallback, verifier)));
        Assert.Equal((HttpStatusCode.BadRequest, "invalid_target"), await Refused(await Swap(c, back["code"]!, verifier, HostedDoc, WebCallback, "http://localhost/mcp")));
        back = Params(await Allow(c, Ask(HostedDoc, WebCallback, verifier)));
        var tokens = await Json(await Swap(c, back["code"]!, verifier, HostedDoc, WebCallback, Ts + "/mcp"));
        Assert.Equal((HttpStatusCode.BadRequest, "invalid_target"), await Refused(await Renew(c, tokens["refresh_token"]!.GetValue<string>(), HostedDoc, "https://evil.example/mcp")));
        Assert.Equal((HttpStatusCode.BadRequest, "invalid_target"), await Refused(await Renew(c, tokens["refresh_token"]!.GetValue<string>(), HostedDoc, "http://localhost:8767/mcp")));
    }

    // --- /token --------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task An_unknown_client_gets_invalid_client_so_claude_registers_again()
    {
        await using var door = await Door.OpenAsync();
        var c = door.As("http://mini.tail1234.ts.net");
        Assert.Equal((HttpStatusCode.Unauthorized, "invalid_client"), await Refused(await Swap(c, "sscode_x", Verifier(), "ssc_gone", WebCallback)));
        Assert.Equal((HttpStatusCode.Unauthorized, "invalid_client"), await Refused(await Renew(c, "ssr_x", "ssc_gone")));
        Assert.Equal((HttpStatusCode.BadRequest, "invalid_grant"), await Refused(await Swap(c, "sscode_x", Verifier(), HostedDoc, WebCallback)));
        Assert.Equal((HttpStatusCode.BadRequest, "invalid_grant"), await Refused(await Swap(c, "sscode_x", "short", HostedDoc, WebCallback)));
        Assert.Equal((HttpStatusCode.BadRequest, "unsupported_grant_type"), await Refused(await c.PostAsync("/token", Form(("grant_type", "password")))));
    }

    [Fact]
    public async Task Refresh_rotates_forgives_a_retry_for_a_minute_and_ends_after_30_idle_days()
    {
        await using var door = await Door.OpenAsync();
        var c = door.As("http://mini.tail1234.ts.net");
        var (a1, r1) = await SignIn(door, c);
        async Task<(string Access, string Refresh)> Renewed(string refresh)
        {
            var t = await Json(await Renew(c, refresh, HostedDoc, Ts + "/mcp"));
            return (t["access_token"]!.GetValue<string>(), t["refresh_token"]!.GetValue<string>());
        }

        var (a2, r2) = await Renewed(r1);
        Assert.NotEqual(r1, r2);
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.SendAsync(Mcp(a1))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await c.SendAsync(Mcp(a2))).StatusCode);

        // The answer to that refresh was lost, so the old token comes again: it still works, for a minute.
        door.Now += TimeSpan.FromSeconds(30);
        var (a3, r3) = await Renewed(r1);
        Assert.Equal(HttpStatusCode.OK, (await c.SendAsync(Mcp(a3))).StatusCode);
        door.Now += TimeSpan.FromSeconds(31);
        Assert.Equal((HttpStatusCode.BadRequest, "invalid_grant"), await Refused(await Renew(c, r1, HostedDoc)));
        Assert.Equal((HttpStatusCode.BadRequest, "invalid_grant"), await Refused(await Renew(c, r2, HostedDoc)));
        // Refused, but the sign-in lives on: the current token still renews.
        var (_, r4) = await Renewed(r3);
        Assert.Equal((HttpStatusCode.BadRequest, "invalid_grant"), await Refused(await Renew(c, r4, "https://clients.example/other.json")));

        // Access lasts an hour; a refresh token 30 days unused.
        door.Now += TimeSpan.FromSeconds(3601);
        var (a5, r5) = await Renewed(r4);
        door.Now += TimeSpan.FromDays(29);
        var (_, r6) = await Renewed(r5);
        door.Now += TimeSpan.FromDays(30) + TimeSpan.FromSeconds(1);
        Assert.Equal((HttpStatusCode.BadRequest, "invalid_grant"), await Refused(await Renew(c, r6, HostedDoc)));
        Assert.Empty(door.Access.Grants());
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.SendAsync(Mcp(a5))).StatusCode);
    }

    [Fact]
    public async Task Revoking_ends_the_sign_in()
    {
        await using var door = await Door.OpenAsync();
        var c = door.As("http://mini.tail1234.ts.net");
        var (access, refresh) = await SignIn(door, c);
        Assert.Equal(HttpStatusCode.OK, (await c.SendAsync(Mcp(access))).StatusCode);

        // Someone else's client can't end it; an unknown token gets the same answer.
        Assert.Equal("{}", (await Json(await c.PostAsync("/revoke", Form(("token", access), ("client_id", "ssc_other"))))).ToJsonString());
        Assert.Equal(HttpStatusCode.OK, (await c.SendAsync(Mcp(access))).StatusCode);
        Assert.Equal("{}", (await Json(await c.PostAsync("/revoke", Form(("token", "ssa_nothing"))))).ToJsonString());

        var revoked = await c.PostAsync("/revoke", Form(("token", refresh), ("token_type_hint", "refresh_token"), ("client_id", HostedDoc)));
        Assert.Equal(HttpStatusCode.OK, revoked.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.SendAsync(Mcp(access))).StatusCode);
        Assert.Equal((HttpStatusCode.BadRequest, "invalid_grant"), await Refused(await Renew(c, refresh, HostedDoc)));

        (access, _) = await SignIn(door, c);
        await c.PostAsync("/revoke", Form(("token", access)));
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.SendAsync(Mcp(access))).StatusCode);
    }

    [Fact]
    public async Task Every_answer_back_names_the_server()
    {
        await using var door = await Door.OpenAsync();
        var c = door.As("http://mini.tail1234.ts.net");
        door.Docs[HostedDoc] = Doc(HostedDoc, "Claude", WebCallback);
        var fields = Ask(HostedDoc, WebCallback, Verifier());
        Assert.Equal(Ts, Params(await Allow(c, fields))["iss"]);
        var denied = await c.PostAsync("/authorize", Form([.. fields, ("decision", "deny")]));
        Assert.Equal(("access_denied", Ts), (Params(denied.Headers.Location!)["error"], Params(denied.Headers.Location!)["iss"]));
        var noPkce = await c.GetAsync("/authorize?" + Query(fields.Where(f => f.Item1 != "code_challenge").ToArray()));
        Assert.Equal(("invalid_request", Ts), (Params(noPkce.Headers.Location!)["error"], Params(noPkce.Headers.Location!)["iss"]));
        var token = await c.GetAsync("/authorize?" + Query(fields.Select(f => f.Item1 == "response_type" ? (f.Item1, "token") : f).ToArray()));
        Assert.Equal(("unsupported_response_type", Ts), (Params(token.Headers.Location!)["error"], Params(token.Headers.Location!)["iss"]));
    }

    // --- claude.json from 0.6.0 ----------------------------------------------------------------------------------------

    [Fact]
    public void A_claude_json_from_before_still_loads_and_its_sign_ins_bind_on_refresh()
    {
        using var dir = new TempDir();
        Directory.CreateDirectory(dir["home"]);
        double now = DateTimeOffset.Parse("2026-09-23T10:00:00Z").ToUnixTimeSeconds();
        File.WriteAllText(Path.Combine(dir["home"], "claude.json"), new JsonObject
        {
            ["clients"] = new JsonArray(new JsonObject { ["client_id"] = "ssc_old", ["name"] = "Claude", ["redirect_uris"] = new JsonArray(WebCallback), ["created"] = now - 86400 }),
            ["grants"] = new JsonArray(new JsonObject
            {
                ["id"] = "a1b2c3", ["kind"] = "signin", ["client_id"] = "ssc_old", ["name"] = "Claude", ["access_hash"] = ClaudeAccess.Hash("ssa_old"),
                ["access_expires"] = now + 600, ["refresh_hash"] = ClaudeAccess.Hash("ssr_old"), ["created"] = now - 86400, ["last_used"] = now - 3600,
            }),
            ["public_url"] = Ts, ["tools_on"] = true,
        }.ToJsonString());
        var access = new ClaudeAccess(dir["home"], () => DateTimeOffset.FromUnixTimeSeconds((long)now));
        Assert.Equal(Ts, access.PublicUrl);
        Assert.Equal("Claude", access.Client("ssc_old")!.Name);
        // No address kept: it reads through any, until its next refresh binds it.
        Assert.NotNull(access.Check("ssa_old", "https://other.tail1234.ts.net/mcp"));
        var (tokens, refusal) = access.Refresh("ssr_old", "ssc_old", resource: null, here: Ts + "/mcp");
        Assert.Null(refusal);
        Assert.Equal(Ts + "/mcp", access.Grants().Single().Resource);
        Assert.Null(access.Check(tokens!.AccessToken, "https://other.tail1234.ts.net/mcp"));
        Assert.NotNull(access.Check(tokens.AccessToken, Ts + "/mcp"));
    }

    // --- the MCP endpoint: transport, CORS and Origin, the off switch, and the tools Claude sees -----------------------

    static HttpRequestMessage Rpc(string token, object? body, HttpMethod? method = null, params (string Name, string Value)[] headers)
    {
        var r = new HttpRequestMessage(method ?? HttpMethod.Post, "/mcp")
        {
            Headers = { Accept = { new("application/json"), new("text/event-stream") }, Authorization = new("Bearer", token) },
        };
        if (body is not null) r.Content = JsonContent.Create(body);
        foreach (var (n, v) in headers) r.Headers.TryAddWithoutValidation(n, v);
        return r;
    }

    static object Initialize(string version) => new
    {
        jsonrpc = "2.0", id = 1, method = "initialize",
        @params = new { protocolVersion = version, capabilities = new { }, clientInfo = new { name = "Claude", version = "1.0" } },
    };

    static string Header(HttpResponseMessage r, string name) =>
        r.Headers.TryGetValues(name, out var v) || r.Content.Headers.TryGetValues(name, out v) ? string.Join(", ", v) : "";

    /// <summary>An MCP client on the door's internet address, holding <paramref name="token"/>; every request it
    /// sends is kept in <paramref name="sent"/> when given.</summary>
    static Task<McpClient> Client(Door door, string token, McpClientOptions? options = null, List<HttpRequestMessage>? sent = null) =>
        McpClient.CreateAsync(new HttpClientTransport(new HttpClientTransportOptions
        {
            Endpoint = new Uri(Ts + "/mcp"), TransportMode = HttpTransportMode.StreamableHttp,
            AdditionalHeaders = new Dictionary<string, string> { ["Authorization"] = "Bearer " + token },
        }, new HttpClient(new Recorder(door.Site.App.GetTestServer().CreateHandler(), sent ?? [])) { BaseAddress = new Uri("http://mini.tail1234.ts.net") },
            ownsHttpClient: true), options);

    sealed class Recorder(HttpMessageHandler inner, List<HttpRequestMessage> sent) : DelegatingHandler(inner)
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            // The door sees the internet address as Tailscale's proxy hands it on: plain http, same name.
            request.RequestUri = new UriBuilder(request.RequestUri!) { Scheme = "http", Port = -1 }.Uri;
            lock (sent) sent.Add(request);
            return base.SendAsync(request, cancellationToken);
        }
    }

    static string Text(ModelContextProtocol.Protocol.CallToolResult r) => ((ModelContextProtocol.Protocol.TextContentBlock)r.Content[0]).Text;

    [Theory]
    [InlineData("2025-06-18")]
    [InlineData("2025-11-25")]
    public async Task Every_request_stands_alone_so_a_restart_loses_claude_nothing(string version)
    {
        await using var door = await Door.OpenAsync();
        var c = door.As("http://mini.tail1234.ts.net");
        var (token, _) = await SignIn(door, c);

        var init = await c.SendAsync(Rpc(token, Initialize(version), headers: ("MCP-Protocol-Version", version)));
        Assert.Equal(HttpStatusCode.OK, init.StatusCode);
        Assert.False(init.Headers.Contains("Mcp-Session-Id"));
        string body = await init.Content.ReadAsStringAsync();
        Assert.Contains($"\"protocolVersion\":\"{version}\"", body);
        Assert.Contains("\"name\":\"study-stash\"", body);

        var note = await c.SendAsync(Rpc(token, new { jsonrpc = "2.0", method = "notifications/initialized" }, headers: ("MCP-Protocol-Version", version)));
        Assert.Equal(HttpStatusCode.Accepted, note.StatusCode);

        // A session id kept from before the library restarted is ignored, not refused.
        var list = await c.SendAsync(Rpc(token, new { jsonrpc = "2.0", id = 2, method = "tools/list" }, headers: [("MCP-Protocol-Version", version), ("Mcp-Session-Id", "from-before-a-restart")]));
        Assert.True(list.StatusCode == HttpStatusCode.OK, await list.Content.ReadAsStringAsync());
        Assert.Contains("list_classes", await list.Content.ReadAsStringAsync());

        // No stream to open and no session to end.
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await c.SendAsync(Rpc(token, null, HttpMethod.Get))).StatusCode);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await c.SendAsync(Rpc(token, null, HttpMethod.Delete))).StatusCode);
    }

    [Fact]
    public async Task A_client_on_the_2026_07_28_revision_reads_with_its_per_request_headers()
    {
        await using var door = await Door.OpenAsync();
        var (token, _) = await SignIn(door, door.As("http://mini.tail1234.ts.net"));
        List<HttpRequestMessage> sent = [];
        await using var mcp = await Client(door, token, new McpClientOptions { ProtocolVersion = "2026-07-28" }, sent);
        var result = await mcp.CallToolAsync("list_classes", new Dictionary<string, object?>());
        Assert.StartsWith("Library: Sam's library", Text(result));
        var call = sent.Last();
        Assert.Equal("2026-07-28", call.Headers.GetValues("MCP-Protocol-Version").Single());
        Assert.Equal("tools/call", call.Headers.GetValues("Mcp-Method").Single());
    }

    [Fact]
    public async Task A_browser_may_ask_first_without_signing_in()
    {
        await using var door = await Door.OpenAsync();
        var c = door.As("http://mini.tail1234.ts.net");
        var ask = new HttpRequestMessage(HttpMethod.Options, "/mcp")
        {
            Headers = { { "Origin", "https://claude.ai" }, { "Access-Control-Request-Method", "POST" }, { "Access-Control-Request-Headers", "authorization, content-type, mcp-protocol-version" } },
        };
        var r = await c.SendAsync(ask);
        Assert.Equal(HttpStatusCode.NoContent, r.StatusCode);
        Assert.Equal("https://claude.ai", Header(r, "Access-Control-Allow-Origin"));
        Assert.Contains("Origin", Header(r, "Vary"));
        Assert.Contains("POST", Header(r, "Access-Control-Allow-Methods"));
        foreach (string h in new[] { "Authorization", "Content-Type", "Accept", "MCP-Protocol-Version", "Mcp-Session-Id", "Mcp-Method", "Mcp-Name", "Last-Event-ID" })
            Assert.Contains(h, Header(r, "Access-Control-Allow-Headers"));
        foreach (string h in new[] { "WWW-Authenticate", "Mcp-Session-Id", "MCP-Protocol-Version" })
            Assert.Contains(h, Header(r, "Access-Control-Expose-Headers"));

        // Discovery, registration, tokens and signing out: any page may ask, and they hold no cookie.
        foreach (string path in new[] { "/.well-known/oauth-protected-resource/mcp", "/.well-known/oauth-authorization-server", "/register", "/token", "/revoke" })
        {
            var open = await c.SendAsync(new HttpRequestMessage(HttpMethod.Options, path) { Headers = { { "Origin", "https://inspector.example" }, { "Access-Control-Request-Method", "POST" } } });
            Assert.Equal(HttpStatusCode.NoContent, open.StatusCode);
            Assert.Equal("*", Header(open, "Access-Control-Allow-Origin"));
        }
        Assert.Equal("*", Header(await c.GetAsync("/.well-known/oauth-authorization-server"), "Access-Control-Allow-Origin"));
        // Asking about anything else doesn't fall through to "not found".
        Assert.Equal(HttpStatusCode.NoContent, (await c.SendAsync(new HttpRequestMessage(HttpMethod.Options, "/authorize"))).StatusCode);
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("https://claude.ai", true)]
    [InlineData("https://claude.com", true)]
    [InlineData("https://mini.tail1234.ts.net", true)]
    [InlineData("http://localhost:6274", true)]
    [InlineData("http://127.0.0.1:6274", true)]
    [InlineData("https://evil.example", false)]
    [InlineData("https://claude.ai.evil.example", false)]
    [InlineData("http://mini.tail1234.ts.net", false)]
    [InlineData("null", false)]
    public async Task Only_claude_this_library_or_this_computer_may_call_from_a_page(string? origin, bool allowed)
    {
        await using var door = await Door.OpenAsync();
        var c = door.As("http://mini.tail1234.ts.net");
        var (token, _) = await SignIn(door, c);
        var r = await c.SendAsync(Rpc(token, new { jsonrpc = "2.0", id = 1, method = "tools/list" }, headers: origin is null ? [] : [("Origin", origin)]));
        if (allowed)
        {
            Assert.Equal(HttpStatusCode.OK, r.StatusCode);
            if (origin is not null) Assert.Equal(origin, Header(r, "Access-Control-Allow-Origin"));
            return;
        }
        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
        Assert.Equal("", Header(r, "Access-Control-Allow-Origin"));
        var error = JsonNode.Parse(await r.Content.ReadAsStringAsync())!.AsObject();
        Assert.Equal("2.0", error["jsonrpc"]!.GetValue<string>());
        Assert.False(error.ContainsKey("id"));
        Assert.NotNull(error["error"]!["message"]);
    }

    [Fact]
    public async Task A_page_on_claude_that_isnt_signed_in_can_read_where_to_sign_in()
    {
        await using var door = await Door.OpenAsync();
        var r = await door.As("http://mini.tail1234.ts.net").SendAsync(Rpc("", new { jsonrpc = "2.0", id = 1, method = "tools/list" }, headers: ("Origin", "https://claude.ai")));
        Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
        Assert.Equal("https://claude.ai", Header(r, "Access-Control-Allow-Origin"));
        Assert.Contains("WWW-Authenticate", Header(r, "Access-Control-Expose-Headers"));
    }

    [Fact]
    public async Task Tool_access_off_still_connects_and_every_call_says_why_in_words()
    {
        await using var door = await Door.OpenAsync();
        var (token, _) = await SignIn(door, door.As("http://mini.tail1234.ts.net"));
        door.Access.ToolsOn = false;

        await using var mcp = await Client(door, token);
        var tools = await mcp.ListToolsAsync(); // refused at call time, not hidden
        Assert.Contains("list_classes", tools.Select(t => t.Name));
        foreach (var t in tools)
        {
            var r = await mcp.CallToolAsync(t.Name, new Dictionary<string, object?>());
            Assert.True(r.IsError);
            Assert.Equal("AI tool access is off in Study Stash. The student can turn it on in Study Stash → Settings → AI tool access.", Text(r));
        }

        door.Access.ToolsOn = true; // no reconnect needed
        Assert.StartsWith("Library: Sam's library", Text(await mcp.CallToolAsync("list_classes", new Dictionary<string, object?>())));
    }

    [Fact]
    public async Task Canvas_off_refuses_every_tool_that_reads_canvas_naming_the_setting()
    {
        using var shared = new TempDir();
        Directory.CreateDirectory(shared["library"]);
        await using var door = await Door.OpenAsync(canvas: true, files: [("Library", shared["library"], false)]);
        var (token, _) = await SignIn(door, door.As("http://mini.tail1234.ts.net"));
        door.Access.Reading = door.Access.Reading with { Canvas = false };

        await using var mcp = await Client(door, token);
        var canvasTools = (await mcp.ListToolsAsync()).Select(t => t.Name).Where(n => StudyStash.Core.Ai.ToolAccess.Scope(n) == "canvas").ToList();
        Assert.Equal(
            ["canvas_api", "canvas_courses", "canvas_page", "class_announcements", "class_files", "class_modules", "due_assignments", "get_assignment", "read_file", "search_files"],
            canvasTools.Order());
        foreach (string name in canvasTools)
        {
            var r = await mcp.CallToolAsync(name, new Dictionary<string, object?>());
            Assert.True(r.IsError, name);
            Assert.Equal("Study Stash's settings don't let AI tools read Canvas right now (Settings → AI tool access → Canvas assignments and files).", Text(r));
        }
        Assert.True((await mcp.CallToolAsync("list_classes", new Dictionary<string, object?>())).IsError is null or false);
    }

    [Fact]
    public async Task Claude_on_the_web_sees_only_tools_that_read_each_labelled_so()
    {
        using var shared = new TempDir();
        Directory.CreateDirectory(shared["library"]);
        await using var door = await Door.OpenAsync(canvas: true, files: [("Library", shared["library"], false)]);
        var (token, _) = await SignIn(door, door.As("http://mini.tail1234.ts.net"));
        await using var mcp = await Client(door, token);

        var tools = await mcp.ListToolsAsync();
        Assert.Equal(
            ["canvas_api", "canvas_courses", "canvas_page", "class_announcements", "class_files", "class_modules", "due_assignments", "get_assignment",
             "get_lecture", "get_transcript", "list_classes", "list_lectures", "read_file", "search_files", "search_notes"],
            tools.Select(t => t.Name).Order());
        foreach (var t in tools.Select(t => t.ProtocolTool))
        {
            Assert.False(string.IsNullOrWhiteSpace(t.Title), t.Name);
            Assert.Equal((true, false, true, t.Name is "canvas_api" or "canvas_page"),
                (t.Annotations!.ReadOnlyHint, t.Annotations.DestructiveHint, t.Annotations.IdempotentHint, t.Annotations.OpenWorldHint));
            Assert.DoesNotContain("file tools", t.Description);
            Assert.DoesNotContain("canvas_download", t.Description);
        }
        Assert.DoesNotContain("canvas_download", mcp.ServerInstructions);
        Assert.Contains("get_assignment", tools.Single(t => t.Name == "due_assignments").Description);
        Assert.Contains("the grader's feedback", tools.Single(t => t.Name == "get_assignment").Description);
    }

    /// <summary>The stdio server (Claude Code and Claude Desktop on this computer) keeps its download, and has no
    /// read_file: it can open files itself.</summary>
    [Fact]
    public void The_stdio_server_keeps_canvas_download()
    {
        var names = ClaudeTools.Tools(new ReadingSource()).Select(t => t.ProtocolTool.Name).ToList();
        Assert.Contains("canvas_download", names);
        Assert.DoesNotContain("read_file", names);
        Assert.Contains("your own file tools", ClaudeTools.Tools(new ReadingSource()).Single(t => t.ProtocolTool.Name == "search_files").ProtocolTool.Description);
    }

    /// <summary>Every tool, in both sets, needs a reading toggle on purpose: only list_classes (class names and
    /// counts) needs none. A new tool without a scope fails here.</summary>
    [Fact]
    public void Every_tool_needs_a_reading_toggle_on_purpose()
    {
        foreach (bool web in new[] { false, true })
            foreach (var t in ClaudeTools.Tools(new ReadingSource(), web))
                Assert.True(t.ProtocolTool.Name == "list_classes" ? StudyStash.Core.Ai.ToolAccess.Scope(t.ProtocolTool.Name) is null
                    : StudyStash.Core.Ai.ToolAccess.Scope(t.ProtocolTool.Name) is not null, $"{t.ProtocolTool.Name} has no reading toggle");
    }

    /// <summary>A library with Canvas linked and files to read, for building the whole tool list.</summary>
    sealed class ReadingSource : ILibrarySource
    {
        public Task<JsonObject> OverviewAsync() => Task.FromResult(new JsonObject());
        public Task<JsonArray> LecturesAsync(string? className, int limit, string? before) => Task.FromResult(new JsonArray());
        public Task<JsonObject?> LectureAsync(string id) => Task.FromResult<JsonObject?>(null);
        public Task<JsonObject> SearchAsync(string query, string? className, int limit) => Task.FromResult(new JsonObject());
        public bool HasCanvas => true;
        public bool CanReadFiles => true;
    }

    [Fact]
    public async Task Read_file_reads_only_what_search_could_show_a_page_at_a_time()
    {
        using var dir = new TempDir();
        string lib = dir["pool"], secret = dir["secret"], outside = dir["outside"];
        string week = Path.Combine(lib, "CS 101", "Canvas", "files", "Week 1");
        foreach (string d in new[] { week, secret, outside }) Directory.CreateDirectory(d);
        string slides = Path.Combine(week, "recursion.md");
        File.WriteAllText(slides, "# Recursion\n" + new string('a', ClaudeTools.PageChars) + "\nThe base case ends it.");
        string taxes = Path.Combine(secret, "taxes.txt");
        File.WriteAllText(taxes, "private words");
        string away = Path.Combine(outside, "diary.md");
        File.WriteAllText(away, "not shared");
        File.CreateSymbolicLink(Path.Combine(week, "diary.md"), away);
        string home = dir["home"];
        Directory.CreateDirectory(home);
        var index = new StudyStash.Core.Ai.FileIndex(home, () => [("Library", lib, false), ("Secret", secret, true)], () => []);
        await index.UpdateAsync();
        var cfg = new Config(home, dir["pool"]);
        using var store = new Store(cfg.DbPath, cfg.PoolDir);
        ILibrarySource source = new LocalLibrary(new LibraryReader(cfg, store), null, home, index);

        string first = await source.ReadFileAsync(slides, 0);
        Assert.StartsWith("# Recursion", first);
        int more = first.IndexOf("(More: call again with offset=", StringComparison.Ordinal);
        Assert.True(more > 0);
        int offset = int.Parse(first[(more + "(More: call again with offset=".Length)..].TrimEnd('.', ')'));
        Assert.Contains("The base case ends it.", await source.ReadFileAsync(slides, offset));
        Assert.Equal("That's the end of the file.", await source.ReadFileAsync(slides, int.MaxValue));
        // The "<class>/<path in its folder>" class_files and get_assignment give reads the same file.
        Assert.Equal(first, await source.ReadFileAsync("CS 101/Canvas/files/Week 1/recursion.md", 0));

        Assert.Contains("private folder", await source.ReadFileAsync(taxes, 0));
        const string NotHere = "That isn't a file Study Stash shares with AI tools.";
        Assert.StartsWith(NotHere, await source.ReadFileAsync(Path.Combine(week, "diary.md"), 0)); // a link out of the folder
        Assert.StartsWith(NotHere, await source.ReadFileAsync(away, 0));
        Assert.StartsWith(NotHere, await source.ReadFileAsync(Path.Combine(week, "..", "..", "..", "..", "..", "outside", "diary.md"), 0));
        Assert.StartsWith(NotHere, await source.ReadFileAsync("Week 1/recursion.md", 0));
        Assert.StartsWith(NotHere, await source.ReadFileAsync("CS 101/../../outside/diary.md", 0));
        Assert.False(Directory.Exists(Path.Combine(lib, "Week 1"))); // naming a class that isn't there makes no folder
        Assert.StartsWith(NotHere, await source.ReadFileAsync(Path.Combine(home, "claude.json"), 0));
    }
}
