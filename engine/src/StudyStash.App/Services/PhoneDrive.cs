using System.Net.Http.Headers;
using System.Text.Json.Nodes;
using StudyStash.Core;
using StudyStash.Core.PhoneDrive;

namespace StudyStash.App.Services;

/// <summary>
/// The phone over Google Drive, on a computer that's just this computer (docs/phone-google.md), as the app sees it:
/// Settings → Phone's Connect with Google (the browser opens Google's page; the account is kept in google.json), and
/// then everything else through the library on this computer, which runs the link itself (<see cref="PhoneLink"/>, at
/// /api/v2/phone/google): what it's doing, the phones that use it, Remove, and Disconnect. The link lives in the
/// library, not here, so the phone is answered whenever the library runs, with the app closed too when the library
/// runs in the background (<see cref="BackgroundLibrary"/>).
/// </summary>
public sealed class PhoneDrive : IDisposable
{
    readonly string home;
    readonly Func<(string Url, string Key)?> library;
    readonly Action<string> log;
    readonly Func<string?> secret;
    readonly HttpClient? googleHttp;
    readonly HttpClient http;
    readonly Lock gate = new();
    CancellationTokenSource? connecting;
    Timer? watch;
    Action? changed;
    string seen = "";

    /// <summary>The test prefix of every file name in the folder (STUDYSTASH_DRIVE_SPACE); "" in real use.</summary>
    public string Space { get; }

    /// <summary>How often Settings → Phone asks the library again while it's open.</summary>
    public TimeSpan WatchEvery { get; init; } = TimeSpan.FromSeconds(3);

    /// <param name="library">This computer's library: its address and password (null while there's none).</param>
    /// <param name="secret">The Desktop client's secret (<see cref="GoogleClient.Secret"/> unless a test gives one).</param>
    /// <param name="google">How Google's sign-in and revoke are reached (a test's pretend Google).</param>
    public PhoneDrive(string home, Func<(string Url, string Key)?> library, Action<string>? log = null, Func<string?>? secret = null,
        HttpClient? google = null, HttpClient? libraryHttp = null, string? space = null)
    {
        this.home = home;
        this.library = library;
        this.log = log ?? (_ => { });
        this.secret = secret ?? (() => GoogleClient.Secret);
        googleHttp = google;
        http = libraryHttp ?? new HttpClient { Timeout = TimeSpan.FromSeconds(90) };
        Space = space ?? Environment.GetEnvironmentVariable("STUDYSTASH_DRIVE_SPACE") ?? "";
        Account = GoogleAccount.Load(home);
    }

    /// <summary>The account this computer is connected to; null when it isn't.</summary>
    public GoogleAccount? Account { get; private set; }

    /// <summary>This copy of Syft Notes can sign in to Google (it was built with the Desktop client's secret).</summary>
    public bool CanConnect => secret() is { Length: > 0 };

    /// <summary>The browser is open on Google's page, waiting for the student.</summary>
    public bool Connecting { get; private set; }

    /// <summary>The phones that use the folder, as the library last said.</summary>
    public IReadOnlyList<DrivePhone> Phones { get; private set; } = [];

    /// <summary>What the link is doing or what's wrong (Google signed this computer out), in the student's words.</summary>
    public string? Status { get; private set; }

    /// <summary>Google no longer takes this computer's sign-in: Settings offers to connect again.</summary>
    public bool SignedOut { get; private set; }

    /// <summary>The library that runs the link is the background one (it answers with the app closed).</summary>
    public bool InBackground { get; private set; }

    /// <summary>Anything Settings shows changed (on a worker thread). While anything listens, the library is asked
    /// again every few seconds.</summary>
    public event Action? Changed
    {
        add
        {
            lock (gate)
            {
                changed += value;
                watch ??= new Timer(_ => _ = RefreshAsync(), null, TimeSpan.Zero, WatchEvery);
            }
        }
        remove
        {
            lock (gate)
            {
                changed -= value;
                if (changed is not null) return;
                watch?.Dispose();
                watch = null;
            }
        }
    }

    void Say()
    {
        Action? listeners;
        lock (gate) listeners = changed;
        listeners?.Invoke();
    }

    /// <summary>An account was connected or forgotten: the app moves its library to the background or back.</summary>
    public event Action? AccountChanged;

    /// <summary>The QR code's link: the phone app opens with this account picked. The test prefix rides along in a
    /// test run only.</summary>
    public string QrLink(string email) => "studystash://google?account=" + Uri.EscapeDataString(email)
        + (Space.Length > 0 ? "&space=" + Uri.EscapeDataString(Space) : "");

    GoogleSignIn SignIn(string key) => new(key, googleHttp);

    /// <summary>A call to the link in this computer's library; null when there's no library here or it didn't answer.</summary>
    async Task<JsonObject?> CallAsync(HttpMethod method, string path, CancellationToken ct = default)
    {
        if (library() is not { } where) return null;
        var (url, key) = where;
        using var request = new HttpRequestMessage(method, url.TrimEnd('/') + "/api/v2/phone/google" + path);
        if (key.Length > 0) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        if (method == HttpMethod.Post) request.Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json");
        using var r = await http.SendAsync(request, ct);
        if (r.StatusCode == System.Net.HttpStatusCode.BadGateway) throw new DriveException(502, "Can't reach Google Drive right now.");
        if (!r.IsSuccessStatusCode) return null;
        return JsonNode.Parse(await r.Content.ReadAsStringAsync(ct)) as JsonObject;
    }

    /// <summary>Asks the library what the link is doing, and says so if anything changed.</summary>
    public async Task RefreshAsync()
    {
        JsonObject? o;
        try
        {
            o = await CallAsync(HttpMethod.Get, "");
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException or DriveException)
        {
            o = null;
        }
        Take(o);
    }

    void Take(JsonObject? o)
    {
        string json = o?.ToJsonString() ?? "";
        var account = GoogleAccount.Load(home);
        if (json == seen && account?.Email == Account?.Email) return;
        seen = json;
        Account = account;
        Status = (string?)o?["status"];
        SignedOut = o?["signed_out"]?.GetValue<bool>() == true;
        InBackground = o?["background"]?.GetValue<bool>() == true;
        Phones = [.. (o?["phones"] as JsonArray ?? []).OfType<JsonObject>().Select(p => new DrivePhone(
            (string?)p["id"] ?? "", (string?)p["name"] ?? "Phone", (string?)p["model"] ?? "",
            DateTimeOffset.TryParse((string?)p["open_until"], out var u) ? u : null,
            DateTimeOffset.TryParse((string?)p["changed"], out var c) ? c : DateTimeOffset.MinValue))];
        Say();
    }

    /// <summary>The phone's copy of the library looks again straight away (a recording started or stopped here).</summary>
    public void RefreshCopy() => _ = Task.Run(async () =>
    {
        try
        {
            await CallAsync(HttpMethod.Post, "/copy");
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or DriveException)
        {
        }
    });

    /// <summary>
    /// Connect with Google: the browser opens Google's page (<paramref name="openBrowser"/>), the student picks an
    /// account and presses Allow, the account is kept (owner-only), and the library starts the link with it. Throws
    /// <see cref="GoogleException"/> with the student's words when it doesn't work; cancelled by <see cref="CancelConnect"/>.
    /// </summary>
    public async Task ConnectAsync(Action<string> openBrowser)
    {
        if (secret() is not { Length: > 0 } key) throw GoogleException.Of(GoogleTrouble.NotBuilt);
        var cts = new CancellationTokenSource();
        lock (gate)
        {
            connecting?.Cancel();
            connecting = cts;
        }
        Connecting = true;
        Say();
        try
        {
            var account = await SignIn(key).SignInAsync(openBrowser, cts.Token);
            account.Save(home);
            Account = account;
            log("[drive] connected to a Google account");
            try
            {
                Take(await CallAsync(HttpMethod.Post, "/start"));
            }
            catch (Exception e) when (e is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException or DriveException)
            {
                // The library isn't answering right now: it starts the link with this account when it next starts.
                log($"[drive] the library didn't hear about the account ({e.GetType().Name}); it starts the link when it starts");
            }
            AccountChanged?.Invoke();
        }
        finally
        {
            lock (gate)
                if (connecting == cts) connecting = null;
            cts.Dispose();
            Connecting = false;
            Say();
        }
    }

    /// <summary>The student went back without finishing in the browser.</summary>
    public void CancelConnect()
    {
        lock (gate) connecting?.Cancel();
    }

    /// <summary>
    /// Disconnect: the library stops the link, deletes everything this computer put in the folder, tells Google to
    /// forget it and forgets the account. With no library answering, the app forgets the account itself (Google is
    /// still told), and the folder's files wait for the phone's next look to find no laptop.
    /// </summary>
    public async Task DisconnectAsync()
    {
        bool done = false;
        try
        {
            done = await CallAsync(HttpMethod.Post, "/disconnect") is not null;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException or DriveException)
        {
            log($"[drive] the library didn't answer Disconnect ({e.GetType().Name}): forgetting the account here");
        }
        if (!done && GoogleAccount.Load(home) is { } account)
        {
            if (secret() is { Length: > 0 } key) await SignIn(key).RevokeAsync(account.RefreshToken);
            GoogleAccount.Forget(home);
        }
        Account = null;
        Phones = [];
        Status = null;
        SignedOut = false;
        seen = "";
        log("[drive] disconnected from Google");
        AccountChanged?.Invoke();
        Say();
    }

    /// <summary>Settings' Remove on a phone that uses the folder. Throws when Google Drive or the library can't be
    /// reached, so the phone is still there.</summary>
    public async Task RemovePhoneAsync(string phoneId)
    {
        var o = await CallAsync(HttpMethod.Post, "/phones/" + Uri.EscapeDataString(phoneId) + "/remove")
            ?? throw new HttpRequestException("the library didn't remove the phone");
        Take(o);
    }

    public void Dispose()
    {
        CancelConnect();
        lock (gate)
        {
            watch?.Dispose();
            watch = null;
        }
    }
}
