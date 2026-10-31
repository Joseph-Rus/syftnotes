using StudyStash.Core;
using StudyStash.Core.PhoneDrive;

namespace StudyStash.App.Services;

/// <summary>
/// The phone over Google Drive, on a computer that's just this computer (docs/phone-google.md): Settings → Phone's
/// Connect with Google, the Google account it's connected to (google.json), the link that keeps the phone's copy and
/// answers its requests (<see cref="DriveLink"/>), and Disconnect. The link runs only while this computer is its own
/// library and the account is there; <see cref="AppHost.Start"/> starts it, quitting stops it.
/// </summary>
public sealed class PhoneDrive : IDisposable
{
    readonly string home;
    readonly Func<(string Url, string Key)?> library;
    readonly Func<bool> active;
    readonly Action<string> log;
    readonly Func<string?> secret;
    readonly Func<GoogleAccount, GoogleSignIn, IDrive> makeDrive;
    readonly HttpClient? googleHttp;
    readonly HttpClient? libraryHttp;
    readonly Lock gate = new();
    CancellationTokenSource? running;
    Task linkTask = Task.CompletedTask;
    CancellationTokenSource? connecting;

    /// <summary>The test prefix of every file name in the folder (STUDYSTASH_DRIVE_SPACE); "" in real use.</summary>
    public string Space { get; }
    public DriveTimes Times { get; init; } = new();

    /// <param name="library">This computer's library: its address and password (null while there's none).</param>
    /// <param name="active">Whether this computer is just this computer (its own library), so the link may run.</param>
    /// <param name="secret">The Desktop client's secret (<see cref="GoogleClient.Secret"/> unless a test gives one).</param>
    /// <param name="drive">The folder for an account (the real Google Drive unless a test keeps one in memory).</param>
    /// <param name="google">How Google's sign-in and revoke are reached (a test's pretend Google).</param>
    public PhoneDrive(string home, Func<(string Url, string Key)?> library, Func<bool> active, Action<string>? log = null,
        Func<string?>? secret = null, Func<GoogleAccount, GoogleSignIn, IDrive>? drive = null, HttpClient? google = null,
        HttpClient? libraryHttp = null, string? space = null)
    {
        this.home = home;
        this.library = library;
        this.active = active;
        this.log = log ?? (_ => { });
        this.secret = secret ?? (() => GoogleClient.Secret);
        googleHttp = google;
        this.libraryHttp = libraryHttp;
        makeDrive = drive ?? ((account, signIn) =>
        {
            var tokens = new GoogleTokens(signIn, account.RefreshToken);
            return new GoogleDrive(tokens.GetAsync, log: this.log);
        });
        Space = space ?? Environment.GetEnvironmentVariable("STUDYSTASH_DRIVE_SPACE") ?? "";
        Account = GoogleAccount.Load(home);
    }

    /// <summary>The account this laptop is connected to; null when it isn't.</summary>
    public GoogleAccount? Account { get; private set; }

    /// <summary>This copy of Syft Notes can sign in to Google (it was built with the Desktop client's secret).</summary>
    public bool CanConnect => secret() is { Length: > 0 };

    /// <summary>The browser is open on Google's page, waiting for the student.</summary>
    public bool Connecting { get; private set; }

    /// <summary>The link while it runs.</summary>
    public DriveLink? Link { get; private set; }

    /// <summary>The phones that use the folder.</summary>
    /// <summary>The phone's copy of the library looks again straight away (a recording started or stopped here).</summary>
    public void RefreshCopy() => Link?.CopyNow();

    public IReadOnlyList<DrivePhone> Phones => Link?.Phones ?? [];

    /// <summary>What the link is doing or what's wrong (Google signed this laptop out), in the student's words.</summary>
    public string? Status => Link?.Status;

    /// <summary>Google no longer takes this laptop's sign-in: Settings offers to connect again.</summary>
    public bool SignedOut => Link?.Trouble is GoogleTrouble.SignedOut;

    /// <summary>Anything Settings shows changed (on a worker thread).</summary>
    public event Action? Changed;

    /// <summary>The QR code's link: the phone app opens with this account picked. The test prefix rides along in a
    /// test run only.</summary>
    public string QrLink(string email) => "studystash://google?account=" + Uri.EscapeDataString(email)
        + (Space.Length > 0 ? "&space=" + Uri.EscapeDataString(Space) : "");

    GoogleSignIn SignIn(string key) => new(key, googleHttp);

    /// <summary>The link, when this computer is its own library and an account is connected. Nothing if it runs.</summary>
    public void Start()
    {
        lock (gate)
        {
            if (running is not null || Account is not { } account || !active() || secret() is not { Length: > 0 } key) return;
            var signIn = SignIn(key);
            var link = new DriveLink(makeDrive(account, signIn), new LibraryCalls(library, libraryHttp), Me, active, new DriveNames(Space), log)
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

    /// <summary>Who this laptop is in laptop.json: its install's id, the computer's own name, and the version.</summary>
    (string Id, string Name, string Version) Me() => (GoogleAccount.LaptopId(home), ComputerName.Value, Engine.Version);

    static readonly Lazy<string> ComputerName = new(() =>
    {
        // "Joey's MacBook Air" on a Mac (its Sharing name), the PC's name on Windows.
        if (OperatingSystem.IsMacOS() && Machine.Run("/usr/sbin/scutil", ["--get", "ComputerName"], TimeSpan.FromSeconds(3)) is { ExitCode: 0 } r
            && r.Stdout.Trim() is { Length: > 0 } name)
            return name;
        return Environment.MachineName is { Length: > 0 } m ? m : Machine.HostName();
    });

    /// <summary>Stops the link (quitting, or Disconnect), waiting a moment for what it's doing.</summary>
    void StopLink()
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
    /// Connect with Google: the browser opens Google's page (<paramref name="openBrowser"/>), the student picks an
    /// account and presses Allow, and the account is kept (owner-only) and the link starts. Throws
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
        Changed?.Invoke();
        try
        {
            var account = await SignIn(key).SignInAsync(openBrowser, cts.Token);
            await Task.Run(StopLink);
            account.Save(home);
            Account = account;
            log("[drive] connected to a Google account");
            Start();
        }
        finally
        {
            lock (gate)
                if (connecting == cts) connecting = null;
            cts.Dispose();
            Connecting = false;
            Changed?.Invoke();
        }
    }

    /// <summary>The student went back without finishing in the browser.</summary>
    public void CancelConnect()
    {
        lock (gate) connecting?.Cancel();
    }

    /// <summary>
    /// Disconnect: the link stops, everything this laptop put in the folder is deleted (while Google still takes its
    /// sign-in), Google is told to forget it, and google.json goes. Whatever Google says, the laptop forgets the account.
    /// </summary>
    public async Task DisconnectAsync()
    {
        var link = Link;
        await Task.Run(StopLink);
        var account = Account;
        if (account is not null && secret() is { Length: > 0 } key)
        {
            var signIn = SignIn(key);
            try
            {
                var cleaner = link is not null && link.Trouble is null ? link
                    : new DriveLink(makeDrive(account, signIn), new LibraryCalls(library, libraryHttp), Me, active, new DriveNames(Space), log);
                await cleaner.DeleteOwnFilesAsync().WaitAsync(TimeSpan.FromSeconds(60));
            }
            catch (Exception e) when (e is DriveException or GoogleException or HttpRequestException or TimeoutException or TaskCanceledException)
            {
                log($"[drive] couldn't empty the folder before disconnecting ({e.GetType().Name})");
            }
            await signIn.RevokeAsync(account.RefreshToken);
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

    public void Dispose()
    {
        CancelConnect();
        StopLink();
    }
}
