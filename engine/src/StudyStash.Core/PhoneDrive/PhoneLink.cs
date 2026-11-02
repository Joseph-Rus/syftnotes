using System.Text.Json.Nodes;

namespace StudyStash.Core.PhoneDrive;

/// <summary>
/// The phone link as the library runs it (docs/phone-google.md): on a computer that's just this computer, once
/// Settings → Phone has connected a Google account (google.json), the library keeps the phone's copy and answers its
/// requests (<see cref="DriveLink"/>). It lives in the library's process, not the app's, so the phone is answered
/// for as long as the library runs: as the app's own child while the app is open, or in the background with the app
/// closed (<see cref="BackgroundLibrary"/>). Exactly one runs per computer: the library's. The app reads it, and asks
/// it to start, disconnect or remove a phone, through the library's /api/v2/phone/google.
/// </summary>
public sealed class PhoneLink : IDisposable
{
    readonly string home;
    readonly Func<(string Url, string Key)?> library;
    readonly Func<bool> active;
    readonly Action<string> log;
    readonly Func<string?> secret;
    readonly Func<GoogleAccount, IDrive> makeDrive;
    readonly HttpClient? googleHttp;
    readonly HttpClient? libraryHttp;
    readonly string? folder;
    readonly Lock gate = new();
    CancellationTokenSource? running;
    Task linkTask = Task.CompletedTask;

    /// <summary>The test prefix of every file name in the folder (STUDYSTASH_DRIVE_SPACE); "" in real use.</summary>
    public string Space { get; }
    public DriveTimes Times { get; init; } = new();

    /// <param name="library">This computer's library: its address and password, for the phone's requests.</param>
    /// <param name="active">Whether this computer is just this computer, so the link may run (by default, what the
    /// app's app.json says: <see cref="JustThisComputer"/>).</param>
    /// <param name="secret">The Desktop client's secret (<see cref="GoogleClient.Secret"/> unless a test gives one).</param>
    /// <param name="drive">The folder for an account (the real Google Drive unless a test keeps one in memory).</param>
    /// <param name="google">How Google's revoke is reached (a test's pretend Google).</param>
    /// <param name="folder">A folder on this computer standing in for Google Drive (<see cref="FolderDrive"/>): the
    /// self-test's phone, with no Google and no secret. Null for a student.</param>
    public PhoneLink(string home, Func<(string Url, string Key)?> library, Func<bool>? active = null, Action<string>? log = null,
        Func<string?>? secret = null, Func<GoogleAccount, IDrive>? drive = null, HttpClient? google = null,
        HttpClient? libraryHttp = null, string? space = null, string? folder = null)
    {
        this.home = home;
        this.library = library;
        this.active = active ?? JustThisComputer(home);
        this.log = log ?? (_ => { });
        this.secret = secret ?? (() => GoogleClient.Secret);
        googleHttp = google;
        this.libraryHttp = libraryHttp;
        this.folder = folder;
        makeDrive = drive ?? (folder is not null ? _ => new FolderDrive(folder)
            : account => new GoogleDrive(new GoogleTokens(new GoogleSignIn(this.secret() ?? "", googleHttp), account.RefreshToken).GetAsync, log: this.log));
        Space = space ?? Environment.GetEnvironmentVariable("STUDYSTASH_DRIVE_SPACE") ?? "";
        Account = GoogleAccount.Load(home);
    }

    /// <summary>The account this computer is connected to; null when it isn't.</summary>
    public GoogleAccount? Account { get; private set; }

    /// <summary>The link while it runs.</summary>
    public DriveLink? Link { get; private set; }

    public bool Running
    {
        get
        {
            lock (gate) return running is not null;
        }
    }

    public IReadOnlyList<DrivePhone> Phones => Link?.Phones ?? [];

    /// <summary>What the link is doing or what's wrong (Google signed this computer out), in the student's words.</summary>
    public string? Status => Link?.Status;

    /// <summary>Google no longer takes this computer's sign-in: Settings offers to connect again.</summary>
    public bool SignedOut => Link?.Trouble is GoogleTrouble.SignedOut;

    /// <summary>The phone's copy of the library looks again straight away (a recording started or stopped).</summary>
    public void CopyNow() => Link?.CopyNow();

    public event Action? Changed;

    /// <summary>The link's state for the app (GET /api/v2/phone/google).</summary>
    public JsonObject Json()
    {
        var phones = new JsonArray();
        foreach (var p in Phones)
            phones.Add(new JsonObject
            {
                ["id"] = p.Id, ["name"] = p.Name, ["model"] = p.Model, ["open_until"] = p.OpenUntil?.ToString("o"), ["changed"] = p.Changed.ToString("o"),
            });
        return new JsonObject
        {
            ["account"] = Account?.Email, ["running"] = Running, ["status"] = Status, ["signed_out"] = SignedOut, ["phones"] = phones,
        };
    }

    /// <summary>
    /// Whether the app on this computer is set to just this computer (app.json's role is Both, or an app.json from
    /// before roles that said library_here), read again at most every few seconds: the library has no other way to
    /// know, and a computer that becomes a laptop or a library for laptops stops the link by itself.
    /// </summary>
    public static Func<bool> JustThisComputer(string home)
    {
        var gate = new Lock();
        DateTime readAt = DateTime.MinValue;
        bool last = false;
        return () =>
        {
            lock (gate)
            {
                if (DateTime.UtcNow - readAt < TimeSpan.FromSeconds(5)) return last;
                readAt = DateTime.UtcNow;
                try
                {
                    var app = JsonNode.Parse(File.ReadAllText(Path.Combine(home, "app.json"))) as JsonObject;
                    last = app?["role"] is JsonValue role ? string.Equals((string?)role, "Both", StringComparison.OrdinalIgnoreCase)
                        : app?["library_here"]?.GetValue<bool>() == true;
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or InvalidOperationException or FormatException)
                {
                    last = false;
                }
                return last;
            }
        };
    }

    /// <summary>The link, once an account is connected (google.json read again, so a Connect in the app is picked up).
    /// Nothing if it runs.</summary>
    public void Start()
    {
        lock (gate)
        {
            if (running is not null) return;
            Account = GoogleAccount.Load(home);
            // While this computer isn't just this computer (yet), the link waits, doing nothing, until it is.
            if (Account is not { } account || (folder is null && secret() is not { Length: > 0 })) return;
            var link = new DriveLink(makeDrive(account), new LibraryCalls(library, libraryHttp), Me, active, new DriveNames(Space), log)
            {
                Times = Times,
            };
            link.Changed += () => Changed?.Invoke();
            var cts = new CancellationTokenSource();
            running = cts;
            Link = link;
            linkTask = Task.Run(() => link.RunAsync(cts.Token));
        }
        log("[drive] the phone link is on");
        Changed?.Invoke();
    }

    /// <summary>Who this computer is in laptop.json: its install's id, the computer's own name, and the version.</summary>
    (string Id, string Name, string Version) Me() => (GoogleAccount.LaptopId(home), ComputerName.Value, Engine.Version);

    static readonly Lazy<string> ComputerName = new(() =>
    {
        // "Joey's MacBook Air" on a Mac (its Sharing name), the PC's name on Windows.
        if (OperatingSystem.IsMacOS() && Machine.Run("/usr/sbin/scutil", ["--get", "ComputerName"], TimeSpan.FromSeconds(3)) is { ExitCode: 0 } r
            && r.Stdout.Trim() is { Length: > 0 } name)
            return name;
        return Environment.MachineName is { Length: > 0 } m ? m : Machine.HostName();
    });

    /// <summary>Stops the link (the library stopping, or Disconnect), waiting a moment for what it's doing.</summary>
    public void Stop()
    {
        CancellationTokenSource? cts;
        Task task;
        lock (gate)
        {
            cts = running;
            task = linkTask;
            running = null;
        }
        if (cts is null) return;
        cts.Cancel();
        try
        {
            task.Wait(TimeSpan.FromSeconds(5));
        }
        catch (AggregateException)
        {
        }
        cts.Dispose();
    }

    /// <summary>
    /// Disconnect: the link stops, everything this computer put in the folder is deleted (while Google still takes its
    /// sign-in), Google is told to forget it, and google.json goes. Whatever Google says, the computer forgets the account.
    /// </summary>
    public async Task DisconnectAsync()
    {
        var link = Link;
        await Task.Run(Stop);
        var account = Account ?? GoogleAccount.Load(home);
        string? key = secret();
        if (account is not null && (folder is not null || key is { Length: > 0 }))
        {
            try
            {
                var cleaner = link is not null && link.Trouble is null ? link
                    : new DriveLink(makeDrive(account), new LibraryCalls(library, libraryHttp), Me, active, new DriveNames(Space), log);
                await cleaner.DeleteOwnFilesAsync().WaitAsync(TimeSpan.FromSeconds(60));
            }
            catch (Exception e) when (e is DriveException or GoogleException or HttpRequestException or TimeoutException or TaskCanceledException)
            {
                log($"[drive] couldn't empty the folder before disconnecting ({e.GetType().Name})");
            }
            if (key is { Length: > 0 }) await new GoogleSignIn(key, googleHttp).RevokeAsync(account.RefreshToken);
        }
        GoogleAccount.Forget(home);
        Account = null;
        Link = null;
        log("[drive] disconnected from Google");
        Changed?.Invoke();
    }

    /// <summary>Settings' Remove on a phone that uses the folder.</summary>
    public async Task RemovePhoneAsync(string phoneId)
    {
        if (Link is { } link) await link.RemovePhoneAsync(phoneId);
    }

    public void Dispose() => Stop();
}
