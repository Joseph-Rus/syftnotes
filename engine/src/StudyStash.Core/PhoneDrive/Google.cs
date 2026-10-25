using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace StudyStash.Core.PhoneDrive;

/// <summary>
/// The laptop's Google sign-in ID: the Desktop client of the study-stash-app project, the scopes both the laptop and
/// the phone ask for, and Google's addresses. The client's secret is never in the repo: it's built in from the
/// environment variable <see cref="SecretVariable"/> (CI's repository secret of that name) or the git-ignored file
/// engine/google-desktop-client-secret.txt, and the same variable at run time overrides it.
/// </summary>
public static class GoogleClient
{
    public const string DesktopClientId = "1073354844610-rlceur96jtct2rnhb5616c14n3fnmf6e.apps.googleusercontent.com";
    /// <summary>Who the student is (openid email) and Study Stash's own hidden folder in their Drive. Nothing else.</summary>
    public const string Scopes = "openid email https://www.googleapis.com/auth/drive.appdata";
    public const string DriveScope = "https://www.googleapis.com/auth/drive.appdata";
    public const string AuthorizeUrl = "https://accounts.google.com/o/oauth2/v2/auth";
    public const string TokenUrl = "https://oauth2.googleapis.com/token";
    public const string RevokeUrl = "https://oauth2.googleapis.com/revoke";
    public const string SecretVariable = "GOOGLE_DESKTOP_CLIENT_SECRET";
    /// <summary>The name of the assembly metadata the build puts the secret in (StudyStash.Core.csproj).</summary>
    public const string SecretMetadata = "GoogleDesktopClientSecret";

    /// <summary>The Desktop client's secret: the environment's, else the one this build carries; null when there's
    /// neither, and then Connect with Google says this copy can't connect.</summary>
    public static string? Secret =>
        Environment.GetEnvironmentVariable(SecretVariable) is { Length: > 0 } env ? env.Trim()
        : typeof(GoogleClient).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().FirstOrDefault(a => a.Key == SecretMetadata)?.Value is { Length: > 0 } built ? built
        : null;
}

/// <summary>Why connecting to Google didn't work, which picks the words the student sees.</summary>
public enum GoogleTrouble
{
    /// <summary>They pressed Cancel on Google's page (or Google turned the account away without saying why).</summary>
    Denied,
    /// <summary>A school (Workspace) account whose admin doesn't let outside apps in.</summary>
    School,
    /// <summary>They unticked the Google Drive box on Google's page: Study Stash can't keep its folder.</summary>
    NoDrive,
    /// <summary>Nothing came back from the browser in time (Google showed its own error, or the tab was closed).</summary>
    NoAnswer,
    /// <summary>Google couldn't be reached.</summary>
    Offline,
    /// <summary>Google no longer takes this laptop's sign-in (revoked from the account, or it expired).</summary>
    SignedOut,
    /// <summary>This copy of Study Stash was built without the Desktop client's secret.</summary>
    NotBuilt,
}

/// <summary>Connecting to Google didn't work: <see cref="Exception.Message"/> is the student's words.</summary>
public sealed class GoogleException(GoogleTrouble trouble, string words) : Exception(words)
{
    public GoogleTrouble Trouble { get; } = trouble;

    /// <summary>The words for each kind of trouble: plain, second person, no jargon.</summary>
    public static string Words(GoogleTrouble t) => t switch
    {
        GoogleTrouble.School => "Your school's Google account doesn't allow Study Stash. Try a personal Gmail on both.",
        GoogleTrouble.NoDrive => "Study Stash needs its private folder in your Google Drive. Press Connect with Google again and leave the Google Drive box ticked.",
        GoogleTrouble.NoAnswer => "Google didn't send you back to Study Stash. Press Connect with Google to try again. If Google said your account isn't allowed (a school account often isn't), try a personal Gmail on both.",
        GoogleTrouble.Offline => "Can't reach Google right now. Check this computer is online, then try again.",
        GoogleTrouble.SignedOut => "Google signed Study Stash out of your account, so your phone isn't kept up to date. Connect again to carry on.",
        GoogleTrouble.NotBuilt => "This copy of Study Stash can't connect to Google: it was built without Google sign-in. The download from studystash.app can.",
        _ => "Google didn't connect. If you pressed Cancel, press Connect with Google again. If it's a school account, it may not allow Study Stash: try a personal Gmail on both.",
    };

    public static GoogleException Of(GoogleTrouble t) => new(t, Words(t));
}

/// <summary>The Google account this laptop is connected to: kept in google.json, readable by the owner only.</summary>
public sealed record GoogleAccount(string Email, string RefreshToken, DateTimeOffset Connected)
{
    public static string PathIn(string home) => Path.Combine(home, "google.json");

    public static GoogleAccount? Load(string home)
    {
        try
        {
            if (JsonNode.Parse(File.ReadAllText(PathIn(home))) is not JsonObject o) return null;
            string email = Py.AsString(o["email"]) ?? "", token = Py.AsString(o["refresh_token"]) ?? "";
            if (token.Length == 0) return null;
            var at = DateTimeOffset.TryParse(Py.AsString(o["connected"]), System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal, out var d) ? d : DateTimeOffset.UtcNow;
            return new GoogleAccount(email, token, at);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            return null;
        }
    }

    /// <summary>Written to a file only the owner can read before anything goes in it, then put in place.</summary>
    public void Save(string home)
    {
        Directory.CreateDirectory(home);
        string path = PathIn(home), tmp = path + ".tmp";
        var json = new JsonObject
        {
            ["email"] = Email, ["refresh_token"] = RefreshToken,
            ["connected"] = Connected.ToString("o", System.Globalization.CultureInfo.InvariantCulture),
        }.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(tmp, "");
        Py.OwnerOnly(tmp);
        File.WriteAllText(tmp, json);
        File.Move(tmp, path, overwrite: true);
    }

    public static void Forget(string home)
    {
        if (File.Exists(PathIn(home))) File.Delete(PathIn(home));
    }

    /// <summary>This install's id in the folder (laptop.json's laptop.id): made once, kept through Disconnect.</summary>
    public static string LaptopId(string home)
    {
        string path = Path.Combine(home, "laptop-id");
        try
        {
            if (File.Exists(path) && File.ReadAllText(path).Trim() is { Length: >= 8 } had) return had;
            Directory.CreateDirectory(home);
            string id = Guid.NewGuid().ToString("N");
            File.WriteAllText(path, id);
            return id;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return "laptop";
        }
    }
}

/// <summary>
/// Signing in to Google from the laptop, the way Google says an installed app does: the system browser opens Google's
/// page with PKCE (S256), Google sends it back to a loopback address on this computer (http://127.0.0.1:port, a bare
/// socket, so Windows needs no admin rights), and the code it brings is swapped for a refresh token. Also the access
/// token from that refresh token, and revoking it. Nothing here logs or puts in an exception a token, the secret, or
/// what Google sent back.
/// </summary>
public sealed class GoogleSignIn(string secret, HttpClient? http = null, string clientId = GoogleClient.DesktopClientId)
{
    static readonly HttpClient Shared = new() { Timeout = TimeSpan.FromSeconds(30) };
    readonly HttpClient http = http ?? Shared;

    /// <summary>Google's own addresses, unless a test points them at its own.</summary>
    public string AuthorizeUrl { get; init; } = GoogleClient.AuthorizeUrl;
    public string TokenUrl { get; init; } = GoogleClient.TokenUrl;
    public string RevokeUrl { get; init; } = GoogleClient.RevokeUrl;

    static string B64Url(byte[] data) => Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    /// <summary>Google's page for this sign-in: the account picker, then the consent screen (a refresh token comes
    /// back only with consent).</summary>
    public string PageFor(string redirect, string state, string challenge) => AuthorizeUrl + "?" + Py.UrlEncode(
    [
        ("client_id", clientId), ("redirect_uri", redirect), ("response_type", "code"), ("scope", GoogleClient.Scopes),
        ("code_challenge", challenge), ("code_challenge_method", "S256"), ("access_type", "offline"),
        ("prompt", "select_account consent"), ("state", state),
    ]);

    /// <summary>
    /// The whole sign-in: open Google's page with <paramref name="openBrowser"/>, wait for the browser to come back (up
    /// to <paramref name="timeout"/>, five minutes by default), and swap the code for the account. Throws
    /// <see cref="GoogleException"/> with the student's words when it doesn't work.
    /// </summary>
    public async Task<GoogleAccount> SignInAsync(Action<string> openBrowser, CancellationToken ct = default, TimeSpan? timeout = null)
    {
        string verifier = B64Url(RandomNumberGenerator.GetBytes(48));
        string challenge = B64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        string state = B64Url(RandomNumberGenerator.GetBytes(16));
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        string redirect = $"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}";
        openBrowser(PageFor(redirect, state, challenge));
        using var wait = CancellationTokenSource.CreateLinkedTokenSource(ct);
        wait.CancelAfter(timeout ?? TimeSpan.FromMinutes(5));
        Dictionary<string, string> back;
        try
        {
            back = await CallbackAsync(listener, state, wait.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw GoogleException.Of(GoogleTrouble.NoAnswer);
        }
        if (back.TryGetValue("error", out string? error))
            throw GoogleException.Of(error is "admin_policy_enforced" or "org_internal" or "disallowed_useragent" ? GoogleTrouble.School : GoogleTrouble.Denied);
        if (!back.TryGetValue("code", out string? code) || code.Length == 0) throw GoogleException.Of(GoogleTrouble.Denied);
        var token = await PostAsync(TokenUrl,
        [
            ("code", code), ("client_id", clientId), ("client_secret", secret), ("redirect_uri", redirect),
            ("grant_type", "authorization_code"), ("code_verifier", verifier),
        ], ct);
        string scope = Py.AsString(token["scope"]) ?? "";
        if (!scope.Split(' ').Contains(GoogleClient.DriveScope)) throw GoogleException.Of(GoogleTrouble.NoDrive);
        string refresh = Py.AsString(token["refresh_token"]) ?? throw GoogleException.Of(GoogleTrouble.Denied);
        string email = EmailOf(Py.AsString(token["id_token"]) ?? "") ?? "";
        return new GoogleAccount(email, refresh, DateTimeOffset.UtcNow);
    }

    /// <summary>A fresh access token, and when it runs out. <see cref="GoogleTrouble.SignedOut"/> when Google no longer
    /// takes the refresh token; <see cref="GoogleTrouble.Offline"/> when it can't be asked.</summary>
    public async Task<(string Token, DateTimeOffset Until)> AccessAsync(string refreshToken, CancellationToken ct = default)
    {
        var token = await PostAsync(TokenUrl,
            [("client_id", clientId), ("client_secret", secret), ("refresh_token", refreshToken), ("grant_type", "refresh_token")], ct);
        string access = Py.AsString(token["access_token"]) ?? throw GoogleException.Of(GoogleTrouble.SignedOut);
        double seconds = token["expires_in"] is JsonValue v && v.TryGetValue(out double s) ? s : 3600;
        return (access, DateTimeOffset.UtcNow.AddSeconds(seconds));
    }

    /// <summary>Tells Google this laptop is done with its sign-in. One it can't reach or that's already gone is no
    /// matter: the token is forgotten here either way.</summary>
    public async Task<bool> RevokeAsync(string token, CancellationToken ct = default)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, RevokeUrl)
            {
                Content = new StringContent(Py.UrlEncode([("token", token)]), Encoding.UTF8, "application/x-www-form-urlencoded"),
            };
            using var r = await http.SendAsync(request, ct);
            return r.IsSuccessStatusCode;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
        {
            return false;
        }
    }

    /// <summary>The account's email from the id_token Google's token answer carries (straight from Google over https,
    /// so it's read, not checked); null when there's none.</summary>
    public static string? EmailOf(string idToken)
    {
        string[] parts = idToken.Split('.');
        if (parts.Length < 2) return null;
        try
        {
            string p = parts[1].Replace('-', '+').Replace('_', '/');
            p = p.PadRight(p.Length + (4 - p.Length % 4) % 4, '=');
            return Py.AsString((JsonNode.Parse(Convert.FromBase64String(p)) as JsonObject)?["email"]);
        }
        catch (Exception e) when (e is FormatException or System.Text.Json.JsonException)
        {
            return null;
        }
    }

    /// <summary>A form to Google's token endpoint, as JSON. Its error code (never the body) picks the trouble.</summary>
    async Task<JsonObject> PostAsync(string url, (string, string)[] form, CancellationToken ct)
    {
        HttpResponseMessage r;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(Py.UrlEncode(form), Encoding.UTF8, "application/x-www-form-urlencoded"),
            };
            r = await http.SendAsync(request, ct);
        }
        catch (Exception e) when (e is HttpRequestException || (e is TaskCanceledException && !ct.IsCancellationRequested))
        {
            throw GoogleException.Of(GoogleTrouble.Offline);
        }
        using (r)
        {
            string text = await r.Content.ReadAsStringAsync(ct);
            JsonObject? body = null;
            try
            {
                body = JsonNode.Parse(text.Length > 0 ? text : "{}") as JsonObject;
            }
            catch (System.Text.Json.JsonException)
            {
            }
            if (r.IsSuccessStatusCode && body is not null) return body;
            if ((int)r.StatusCode >= 500) throw GoogleException.Of(GoogleTrouble.Offline);
            string error = Py.AsString(body?["error"]) ?? "";
            throw GoogleException.Of(error switch
            {
                "invalid_grant" => GoogleTrouble.SignedOut,
                "admin_policy_enforced" or "org_internal" => GoogleTrouble.School,
                "invalid_client" or "unauthorized_client" => GoogleTrouble.NotBuilt,
                _ => GoogleTrouble.Denied,
            });
        }
    }

    /// <summary>Answers the browser's visits until one brings the code (or an error) with this sign-in's state; a
    /// visit with another state (an old tab) is told so and waited past.</summary>
    static async Task<Dictionary<string, string>> CallbackAsync(TcpListener listener, string state, CancellationToken ct)
    {
        while (true)
        {
            using var client = await listener.AcceptTcpClientAsync(ct);
            var stream = client.GetStream();
            using var read = CancellationTokenSource.CreateLinkedTokenSource(ct);
            read.CancelAfter(TimeSpan.FromSeconds(10));
            var head = new StringBuilder();
            var buffer = new byte[4096];
            try
            {
                while (!head.ToString().Contains("\r\n\r\n", StringComparison.Ordinal) && head.Length < 16384)
                {
                    int n = await stream.ReadAsync(buffer, read.Token);
                    if (n == 0) break;
                    head.Append(Encoding.Latin1.GetString(buffer, 0, n));
                }
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                continue;
            }
            string[] line = head.ToString().Split("\r\n")[0].Split(' ');
            string target = line.Length >= 2 ? line[1] : "";
            int q = target.IndexOf('?');
            var got = q >= 0 ? Py.ParseQs(target[(q + 1)..]) : [];
            bool ours = got.GetValueOrDefault("state") == state && (got.ContainsKey("code") || got.ContainsKey("error"));
            string page = !ours ? Page("This page is from an older try.", "Go back to Study Stash and press Connect with Google again.")
                : got.ContainsKey("code") ? Page("Study Stash is connected to your Google account.", "You can close this tab and go back to Study Stash.")
                : Page("Study Stash didn't connect.", "Go back to Study Stash: it says what to do.");
            byte[] bytes = Encoding.UTF8.GetBytes(page);
            string reply = $"HTTP/1.1 {(target.StartsWith("/favicon", StringComparison.Ordinal) ? "404 Not Found" : "200 OK")}\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: {bytes.Length}\r\nConnection: close\r\nCache-Control: no-store\r\n\r\n";
            try
            {
                await stream.WriteAsync(Encoding.ASCII.GetBytes(reply), read.Token);
                await stream.WriteAsync(bytes, read.Token);
            }
            catch (Exception e) when (e is IOException or OperationCanceledException && !ct.IsCancellationRequested)
            {
            }
            if (ours) return got;
        }
    }

    static string Page(string title, string line) =>
        "<!doctype html><meta charset=utf-8><meta name=viewport content=\"width=device-width\"><title>Study Stash</title>"
        + "<body style=\"font:16px -apple-system,Segoe UI,sans-serif;max-width:32em;margin:15vh auto;padding:0 1em;color:#1d2433\">"
        + $"<h2 style=\"font-weight:600\">{WebUtility.HtmlEncode(title)}</h2><p>{WebUtility.HtmlEncode(line)}</p></body>";
}
