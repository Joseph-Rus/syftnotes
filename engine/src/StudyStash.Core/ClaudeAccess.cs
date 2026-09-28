using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using StudyStash.Core.Ai;

namespace StudyStash.Core;

/// <summary>An app that may sign in to the library (claude.ai, or Claude Code): one that registered (OAuth dynamic
/// client registration), or one whose client_id is the https address of a document describing it (CIMD).</summary>
public sealed class ClaudeClient
{
    public required string ClientId { get; init; }
    /// <summary>What the app calls itself. Anyone can claim any name, so the sign-in page leads with <see cref="Host"/>
    /// when there is one.</summary>
    public string Name { get; init; } = "";
    public List<string> RedirectUris { get; init; } = [];
    public double Created { get; init; }
    /// <summary>For a CIMD client, the host its document is on (claude.ai): the one name nobody else can use.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Host { get; init; }
}

/// <summary>
/// One connection that may read the library: a Claude that signed in (with an access token that lasts an hour and
/// a refresh token that renews it), or a token made in Settings for another MCP client. Only hashes are kept.
/// </summary>
public sealed class ClaudeGrant
{
    public required string Id { get; init; }
    public required string Kind { get; init; } // "signin" | "token"
    public string ClientId { get; init; } = "";
    public string Name { get; set; } = "";
    public string AccessHash { get; set; } = "";
    public double AccessExpires { get; set; }
    public string RefreshHash { get; set; } = "";
    public double Created { get; init; }
    public double LastUsed { get; set; }
    /// <summary>The MCP address the tokens are for (RFC 8707), canonical; "" reads through any of the library's
    /// addresses (a token from Settings, or a sign-in from before this was kept, until its next refresh).</summary>
    public string Resource { get; set; } = "";
    /// <summary>Where the app that signed in lives (claude.ai), for the list of connections.</summary>
    public string ClientHost { get; set; } = "";
    /// <summary>When the refresh token stops working if it isn't used (it moves on with every use); 0 before this was
    /// kept, counted from <see cref="LastUsed"/>.</summary>
    public double RefreshExpires { get; set; }
    /// <summary>The refresh token just replaced, still good for a moment, so a retried refresh whose answer was lost
    /// doesn't end the sign-in.</summary>
    public string PreviousRefreshHash { get; set; } = "";
    public double PreviousRefreshUntil { get; set; }
}

/// <summary>An authorization code waiting to be exchanged: whose it is, where it goes (exactly as asked), its PKCE
/// challenge, and the MCP address its tokens will be for.</summary>
public sealed record ClaudeCode(string ClientId, string RedirectUri, string Challenge, string Resource, double Expires, string ClientName = "", string ClientHost = "");

/// <summary>What /token answers when it gives no tokens: the OAuth error, and a few words on why.</summary>
public sealed record ClaudeRefusal(string Error, string Description);

/// <summary>What /token hands back.</summary>
public sealed record ClaudeTokens(string AccessToken, string RefreshToken, int ExpiresIn);

/// <summary>
/// Who may read the library through its MCP server, kept in claude.json: registered clients, sign-ins and tokens.
/// This is the OAuth 2.1 authorization server Claude needs (authorization code with PKCE, refresh tokens, dynamic
/// client registration); a person allows a sign-in with the library's password.
/// </summary>
public sealed class ClaudeAccess
{
    public const double AccessSeconds = 3600, CodeSeconds = 300, RefreshIdleSeconds = 30 * 86400, RefreshGraceSeconds = 60;
    public const int MaxRedirects = 10, MaxRedirectLength = 2000;

    /// <summary>Shown for a client_id nobody knows, or a redirect_uri that doesn't match one on file: it never says
    /// which, so a guess at a client_id can't be confirmed by the wording.</summary>
    public const string UnknownLinkMessage = "This sign-in link has expired or didn't come from Claude. Go back to Claude and choose Connect again.";

    /// <summary>Shown when a client's published identity (CIMD) couldn't be read or doesn't check out: the document
    /// didn't fetch, wasn't JSON, claimed a different client_id, listed no good redirect_uris, or asked for a secret.</summary>
    public const string CimdInvalidMessage = "Study Stash couldn't confirm which app this is. Go back and try again.";
    static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    sealed class State
    {
        public List<ClaudeClient> Clients { get; set; } = [];
        public List<ClaudeGrant> Grants { get; set; } = [];
        /// <summary>The library's public address for Claude on the web (https://mini.tail1234.ts.net), when it's on.</summary>
        public string PublicUrl { get; set; } = "";
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? TailnetUrl { get; set; }
        /// <summary>Off stops every connected tool at once, without disconnecting them.</summary>
        public bool ToolsOn { get; set; } = true;
        /// <summary>What Claude and other MCP tools may read, while tool access is on.</summary>
        public ReadingScopes Reading { get; set; } = new();
    }

    readonly string path;
    readonly Func<DateTimeOffset> clock;
    readonly Lock gate = new();
    readonly ConcurrentDictionary<string, ClaudeCode> codes = new();
    readonly List<double> failures = [];
    State state;
    DateTime loadedAt;

    /// <summary>Reads a CIMD client's document. Tests always pass a fake: the real one reaches the internet.</summary>
    public Func<Uri, CancellationToken, Task<string?>> FetchClientDocument { get; init; } = (url, ct) =>
        Environment.GetEnvironmentVariable("STUDYSTASH_TESTS") == "1"
            ? throw new InvalidOperationException("A test tried to fetch a client document from the internet; give ClaudeAccess a fake.")
            : ClientDocuments.Shared.FetchAsync(url, ct);

    public ClaudeAccess(string home, Func<DateTimeOffset>? clock = null)
    {
        path = Path.Combine(home, "claude.json");
        this.clock = clock ?? (() => DateTimeOffset.UtcNow);
        state = Load();
    }

    /// <summary>Another copy changed claude.json (the Study Stash app, or a second library process): read it again, so a
    /// disconnect takes effect at once. Called under the lock.</summary>
    void Fresh()
    {
        var at = File.Exists(path) ? File.GetLastWriteTimeUtc(path) : default;
        if (at != loadedAt) state = Load();
    }

    double Now => clock().ToUnixTimeMilliseconds() / 1000.0;

    State Load()
    {
        try
        {
            loadedAt = File.Exists(path) ? File.GetLastWriteTimeUtc(path) : default;
            return JsonSerializer.Deserialize<State>(File.ReadAllText(path), Json) ?? new State();
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            return new State();
        }
    }

    void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(state, Json));
        File.Move(tmp, path, overwrite: true);
        Py.OwnerOnly(path);
        loadedAt = File.GetLastWriteTimeUtc(path);
    }

    public static string Hash(string token) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    static string NewToken(string prefix) => prefix + Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public string PublicUrl
    {
        get
        {
            lock (gate)
            {
                Fresh();
                return state.PublicUrl;
            }
        }
        set
        {
            lock (gate)
            {
                Fresh();
                state.PublicUrl = value.TrimEnd('/');
                Save();
            }
        }
    }

    public string? TailnetUrl
    {
        get
        {
            lock (gate)
            {
                Fresh();
                return state.TailnetUrl;
            }
        }
        set
        {
            lock (gate)
            {
                Fresh();
                state.TailnetUrl = value?.TrimEnd('/');
                Save();
            }
        }
    }

    /// <summary>Off stops every connected tool at once, without forgetting who's connected (the MCP door refuses
    /// while it's off; <see cref="Check"/> still says whose a token is).</summary>
    public bool ToolsOn
    {
        get
        {
            lock (gate)
            {
                Fresh();
                return state.ToolsOn;
            }
        }
        set
        {
            lock (gate)
            {
                Fresh();
                state.ToolsOn = value;
                Save();
            }
        }
    }

    /// <summary>What Claude and other MCP tools may read right now.</summary>
    public ReadingScopes Reading
    {
        get
        {
            lock (gate)
            {
                Fresh();
                return state.Reading;
            }
        }
        set
        {
            lock (gate)
            {
                Fresh();
                state.Reading = value;
                Save();
            }
        }
    }

    // --- clients ------------------------------------------------------------------------------------------------

    /// <summary>A redirect a sign-in may go back to: https anywhere, or http only to this computer (a CLI's
    /// callback).</summary>
    public static bool GoodRedirect(string uri) =>
        Uri.TryCreate(uri, UriKind.Absolute, out var u) && u.Fragment.Length == 0
        && (u.Scheme == Uri.UriSchemeHttps || (u.Scheme == Uri.UriSchemeHttp && (u.IsLoopback || u.Host == "localhost")));

    public ClaudeClient Register(string name, IEnumerable<string> redirectUris)
    {
        var uris = redirectUris.ToList();
        if (uris.Count == 0 || !uris.All(GoodRedirect)) throw new ArgumentException("redirect_uris must be https, or http on localhost");
        if (uris.Count > MaxRedirects || uris.Any(u => u.Length > MaxRedirectLength))
            throw new ArgumentException($"At most {MaxRedirects} redirect_uris, each under {MaxRedirectLength} characters");
        var client = new ClaudeClient { ClientId = "ssc_" + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(12)), Name = name.Trim(), RedirectUris = uris, Created = Now };
        lock (gate)
        {
            Fresh();
            state.Clients.Add(client);
            // Registration is open (it has to be), so keep the list from growing without end: drop the oldest
            // clients that never signed in.
            var unused = state.Clients.Where(c => state.Grants.All(g => g.ClientId != c.ClientId)).OrderBy(c => c.Created).ToList();
            foreach (var c in unused.Take(Math.Max(0, unused.Count - 50))) state.Clients.Remove(c);
            Save();
        }
        return client;
    }

    /// <summary>A client that registered here.</summary>
    public ClaudeClient? Client(string clientId)
    {
        lock (gate)
        {
            Fresh();
            return state.Clients.FirstOrDefault(c => c.ClientId == clientId);
        }
    }

    /// <summary>A client_id that is the address of a client metadata document (CIMD): https, with a path, and nothing
    /// that could make two addresses mean one document.</summary>
    public static bool IsDocumentClientId(string clientId) =>
        clientId.StartsWith("https://", StringComparison.Ordinal)
        && Uri.TryCreate(clientId, UriKind.Absolute, out var u) && u.Scheme == Uri.UriSchemeHttps
        && u.AbsolutePath.Length > 1 && u.Fragment.Length == 0 && u.UserInfo.Length == 0
        && !clientId.Contains("/./", StringComparison.Ordinal) && !clientId.Contains("/../", StringComparison.Ordinal)
        && !clientId.EndsWith("/.", StringComparison.Ordinal) && !clientId.EndsWith("/..", StringComparison.Ordinal);

    /// <summary>Whether /token knows this client: one that registered, or a CIMD client (which can only hold a code
    /// or a sign-in once its document checked out).</summary>
    public bool KnownClient(string clientId) => IsDocumentClientId(clientId) || Client(clientId) is not null;

    /// <summary>The client asking to sign in, from the store or from its document; or, in plain words, why not.</summary>
    public async Task<(ClaudeClient? Client, string? Problem)> ClientForAsync(string clientId, CancellationToken ct = default)
    {
        if (!IsDocumentClientId(clientId))
            return Client(clientId) is { } registered ? (registered, null) : (null, UnknownLinkMessage);
        var url = new Uri(clientId);
        string host = url.IdnHost.ToLowerInvariant();
        string? text = await FetchClientDocument(url, ct);
        if (text is null) return (null, CimdInvalidMessage);
        System.Text.Json.Nodes.JsonObject? doc;
        try
        {
            doc = System.Text.Json.Nodes.JsonNode.Parse(text) as System.Text.Json.Nodes.JsonObject;
        }
        catch (JsonException)
        {
            doc = null;
        }
        string? Str(string key) => doc?[key] is System.Text.Json.Nodes.JsonValue v && v.TryGetValue(out string? x) ? x : null;
        if (doc is null || Str("client_id") != clientId) return (null, CimdInvalidMessage);
        var uris = (doc["redirect_uris"] as System.Text.Json.Nodes.JsonArray ?? []).Select(n => n is System.Text.Json.Nodes.JsonValue v && v.TryGetValue(out string? x) ? x : "").ToList();
        if (uris.Count == 0 || !uris.All(GoodRedirect)) return (null, CimdInvalidMessage);
        if (doc["token_endpoint_auth_method"] is not null && Str("token_endpoint_auth_method") != "none") return (null, CimdInvalidMessage);
        string name = (Str("client_name") ?? "").Trim();
        return (new ClaudeClient { ClientId = clientId, Name = name.Length > 0 ? Py.Head(name, 80) : host, RedirectUris = uris, Created = Now, Host = host }, null);
    }

    static readonly string[] Loopbacks = ["localhost", "127.0.0.1", "[::1]"];

    static string HostOf(Uri u) => u.HostNameType == UriHostNameType.IPv6 && !u.IdnHost.StartsWith('[') ? $"[{u.IdnHost}]" : u.IdnHost.ToLowerInvariant();

    /// <summary>Whether a sign-in may go back to <paramref name="requested"/>, given a redirect the client listed: the
    /// same address exactly, or, for an app on this computer (Claude Code's callback), the same loopback host and path
    /// on any port, since it listens wherever a port is free (RFC 8252 §7.3).</summary>
    public static bool RedirectMatches(string registered, string requested)
    {
        if (string.Equals(registered, requested, StringComparison.Ordinal)) return true;
        if (!Uri.TryCreate(registered, UriKind.Absolute, out var a) || !Uri.TryCreate(requested, UriKind.Absolute, out var b)) return false;
        if (a.Scheme != Uri.UriSchemeHttp || b.Scheme != Uri.UriSchemeHttp || a.UserInfo.Length > 0 || b.UserInfo.Length > 0) return false;
        if (a.Fragment.Length > 0 || b.Fragment.Length > 0) return false;
        string ha = HostOf(a), hb = HostOf(b);
        return ha == hb && Loopbacks.Contains(ha)
            && string.Equals(a.AbsolutePath, b.AbsolutePath, StringComparison.Ordinal) && string.Equals(a.Query, b.Query, StringComparison.Ordinal);
    }

    // --- signing in -------------------------------------------------------------------------------------------------

    /// <summary>Too many wrong passwords lately: the sign-in page refuses for a while (it's on the internet).</summary>
    public bool LockedOut()
    {
        lock (gate)
        {
            failures.RemoveAll(t => Now - t > 900);
            return failures.Count >= 8;
        }
    }

    public void Failed()
    {
        lock (gate) failures.Add(Now);
    }

    /// <summary>A code for a sign-in just allowed: for this client, back to the redirect exactly as asked, for the MCP
    /// address <paramref name="resource"/>.</summary>
    public string NewCode(ClaudeClient client, string redirectUri, string challenge, string resource)
    {
        foreach (var (k, c) in codes)
            if (c.Expires < Now) codes.TryRemove(k, out _);
        string code = NewToken("sscode_");
        string host = client.Host ?? (Uri.TryCreate(redirectUri, UriKind.Absolute, out var r) && !Loopbacks.Contains(HostOf(r)) ? HostOf(r) : "");
        codes[code] = new ClaudeCode(client.ClientId, redirectUri, challenge, resource, Now + CodeSeconds, client.Name, host);
        return code;
    }

    static string S256(string verifier) =>
        Convert.ToBase64String(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    /// <summary>A PKCE verifier as RFC 7636 has it: 43 to 128 unreserved characters.</summary>
    static bool GoodVerifier(string v) =>
        v.Length is >= 43 and <= 128 && v.All(ch => char.IsAsciiLetterOrDigit(ch) || ch is '-' or '.' or '_' or '~');

    /// <summary>A code for tokens (once), if the client, the redirect and the PKCE verifier all match, and the MCP
    /// address asked for (when one is) is the one the code is for. Else why not.</summary>
    public (ClaudeTokens? Tokens, ClaudeRefusal? Refusal) Exchange(string code, string verifier, string clientId, string redirectUri, string? resource = null)
    {
        if (!codes.TryRemove(code, out var c) || c.Expires < Now) return (null, new("invalid_grant", "That sign-in code is used up or too old. Sign in again."));
        if (c.ClientId != clientId) return (null, new("invalid_grant", "That sign-in code is for another app."));
        if (c.RedirectUri != redirectUri) return (null, new("invalid_grant", "redirect_uri isn't the one the code was given for."));
        if (!GoodVerifier(verifier) || !CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(S256(verifier)), Encoding.ASCII.GetBytes(c.Challenge)))
            return (null, new("invalid_grant", "The PKCE code_verifier doesn't match."));
        if (resource is not null && resource != c.Resource) return (null, new("invalid_target", "That isn't the address this sign-in is for."));
        string access = NewToken("ssa_"), refresh = NewToken("ssr_");
        lock (gate)
        {
            Fresh();
            state.Grants.RemoveAll(g => g.Kind == "signin" && RefreshEnds(g) < Now);
            state.Grants.Add(new ClaudeGrant
            {
                Id = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(6)), Kind = "signin", ClientId = clientId,
                Name = c.ClientName.Length > 0 ? c.ClientName : "Claude", AccessHash = Hash(access), AccessExpires = Now + AccessSeconds,
                RefreshHash = Hash(refresh), RefreshExpires = Now + RefreshIdleSeconds, Created = Now, LastUsed = Now,
                Resource = c.Resource, ClientHost = c.ClientHost,
            });
            Save();
        }
        return (new ClaudeTokens(access, refresh, (int)AccessSeconds), null);
    }

    static double RefreshEnds(ClaudeGrant g) => g.RefreshExpires > 0 ? g.RefreshExpires : g.LastUsed + RefreshIdleSeconds;

    /// <summary>
    /// A refresh token for a new pair (rotation). The token just replaced still works for a minute, and using it then
    /// rotates again: an answer lost on the way (a network blip) mustn't end the sign-in, which would have the student
    /// connect Claude again. After that minute it's refused, but the sign-in isn't ended for it either. A refresh
    /// token unused for 30 days has run out. A sign-in from before audiences were kept is bound on its next refresh:
    /// to <paramref name="resource"/> when asked for, else to <paramref name="here"/>.
    /// </summary>
    public (ClaudeTokens? Tokens, ClaudeRefusal? Refusal) Refresh(string refreshToken, string clientId, string? resource = null, string here = "")
    {
        string h = Hash(refreshToken);
        lock (gate)
        {
            Fresh();
            bool retried = false;
            var g = state.Grants.FirstOrDefault(x => x.Kind == "signin" && x.RefreshHash == h);
            if (g is null && refreshToken.Length > 0)
            {
                g = state.Grants.FirstOrDefault(x => x.Kind == "signin" && x.PreviousRefreshHash == h && x.PreviousRefreshUntil >= Now);
                retried = g is not null;
            }
            if (g is null) return (null, new("invalid_grant", "That refresh token isn't current. Sign in again."));
            if (RefreshEnds(g) < Now)
            {
                state.Grants.Remove(g);
                Save();
                return (null, new("invalid_grant", "This sign-in wasn't used for 30 days, so it ended. Sign in again."));
            }
            if (clientId.Length > 0 && g.ClientId != clientId) return (null, new("invalid_grant", "That refresh token is for another app."));
            if (resource is not null && g.Resource.Length > 0 && g.Resource != resource) return (null, new("invalid_target", "That isn't the address this sign-in is for."));
            if (g.Resource.Length == 0) g.Resource = resource ?? here;
            string access = NewToken("ssa_"), refresh = NewToken("ssr_");
            if (!retried)
            {
                g.PreviousRefreshHash = g.RefreshHash;
                g.PreviousRefreshUntil = Now + RefreshGraceSeconds;
            }
            g.AccessHash = Hash(access);
            g.AccessExpires = Now + AccessSeconds;
            g.RefreshHash = Hash(refresh);
            g.RefreshExpires = Now + RefreshIdleSeconds;
            g.LastUsed = Now;
            Save();
            return (new ClaudeTokens(access, refresh, (int)AccessSeconds), null);
        }
    }

    /// <summary>A token made in Settings, for an MCP client that takes one (shown once).</summary>
    public (string Token, ClaudeGrant Grant) CreateToken(string name)
    {
        string token = NewToken("sst_");
        var g = new ClaudeGrant
        {
            Id = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(6)), Kind = "token", Name = name.Trim().Length > 0 ? name.Trim() : "MCP client",
            AccessHash = Hash(token), AccessExpires = double.MaxValue, Created = Now,
        };
        lock (gate)
        {
            Fresh();
            state.Grants.Add(g);
            Save();
        }
        return (token, g);
    }

    /// <summary>The connection a bearer token belongs to, if it may read now through the MCP address
    /// <paramref name="resource"/> (when given): known, not expired, and for that address (or for any). Whether tool
    /// access is on is for the caller: an off switch shouldn't look like a bad token.</summary>
    public ClaudeGrant? Check(string bearer, string? resource = null)
    {
        if (bearer.Length == 0) return null;
        string h = Hash(bearer);
        lock (gate)
        {
            Fresh();
            var g = state.Grants.FirstOrDefault(x => CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(x.AccessHash), Encoding.ASCII.GetBytes(h)));
            if (g is null || g.AccessExpires < Now) return null;
            if (resource is not null && g.Resource.Length > 0 && g.Resource != resource) return null;
            if (Now - g.LastUsed > 60)
            {
                g.LastUsed = Now;
                Save();
            }
            return g;
        }
    }

    /// <summary>An app signing out (RFC 7009): the connection an access or refresh token belongs to ends. Only its
    /// own client may do that, when it says who it is. Whether anything ended isn't told.</summary>
    public void RevokeToken(string token, string clientId = "")
    {
        if (token.Length == 0) return;
        string h = Hash(token);
        lock (gate)
        {
            Fresh();
            int n = state.Grants.RemoveAll(g => (g.AccessHash == h || g.RefreshHash == h || (g.PreviousRefreshHash == h && g.PreviousRefreshUntil >= Now))
                && (clientId.Length == 0 || g.ClientId == clientId));
            if (n > 0) Save();
        }
    }

    public List<ClaudeGrant> Grants()
    {
        lock (gate)
        {
            Fresh();
            return [.. state.Grants.OrderByDescending(g => g.LastUsed)];
        }
    }

    public bool Revoke(string id)
    {
        lock (gate)
        {
            Fresh();
            int n = state.Grants.RemoveAll(g => g.Id == id);
            if (n > 0) Save();
            return n > 0;
        }
    }
}

/// <summary>
/// Putting the Claude port where Claude can reach it, with Tailscale: Serve (HTTPS on your tailnet only: Claude Code
/// on your other computers) or Funnel (HTTPS on the internet: claude.ai and the Claude apps). Off unless
/// <see cref="ThisComputer"/> is used, so a test can't publish anything.
/// </summary>
public sealed class ClaudeReach
{
    public Func<TailscaleInfo> Tailscale { get; init; } = () => new TailscaleInfo();
    public Runner Run { get; init; } = (_, _, _) => throw new InvalidOperationException("Changing Tailscale is off here.");

    public static ClaudeReach ThisComputer() => new() { Tailscale = () => HostInfo.Tailscale(), Run = Machine.Run };

    /// <summary>Serve (tailnet) or Funnel (internet) the Claude port on https://&lt;this computer&gt;.ts.net, or turn it off.
    /// The address, or why not.</summary>
    public (string? Url, string? Problem) Set(int port, bool internet, bool on)
    {
        var ts = Tailscale();
        if (!ts.Installed || ts.Exe.Length == 0) return (null, "Tailscale isn't installed on the library's computer.");
        if (!ts.Running) return (null, "Tailscale isn't running on the library's computer. Open it and sign in.");
        if (ts.Dns.Length == 0) return (null, "Tailscale hasn't given this computer a name yet. Turn on MagicDNS in the Tailscale admin console.");
        string verb = internet ? "funnel" : "serve";
        string[] args = on ? [verb, "--bg", "--https=443", $"http://127.0.0.1:{port}"] : [verb, "--https=443", "off"];
        var p = Run(ts.Exe, args, TimeSpan.FromSeconds(60));
        if (p is not { ExitCode: 0 })
        {
            string said = Py.Strip(p?.Stdout ?? "");
            // Funnel's first use needs a tailnet admin to allow it: Tailscale prints the page for that.
            var link = System.Text.RegularExpressions.Regex.Match(said, @"https://login\.tailscale\.com/\S+");
            return (null, link.Success ? $"Tailscale needs permission first: open {link.Value}, allow it, then try again."
                : $"Tailscale said: {Py.Head(said, 300)}");
        }
        return (on ? $"https://{ts.Dns.TrimEnd('.')}" : null, null);
    }
}
