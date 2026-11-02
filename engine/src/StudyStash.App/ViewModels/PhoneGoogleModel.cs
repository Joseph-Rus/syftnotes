using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StudyStash.App.Services;
using StudyStash.Core.PhoneDrive;

namespace StudyStash.App.ViewModels;

/// <summary>Settings → Phone's "Answer your phone when Syft Notes is closed": what it is now, turning it on or off (the
/// library moves to the background or back), and why it couldn't, if it couldn't.</summary>
public sealed record PhoneBackgroundSwitch(Func<bool> Get, Func<bool, Task> Set, Func<string?> Problem);

/// <summary>A phone that uses the Google Drive folder: its name, and whether it's open now or when it last was.</summary>
public sealed record DrivePhoneRow(string Id, string Name, string Meta);

/// <summary>
/// Settings → Phone on just this computer: Connect with Google (the browser opens Google's page), then the account,
/// a QR code that opens the phone app with that account picked, Disconnect, and the phones that use the folder, each
/// with Remove. Over <see cref="PhoneDrive"/>; the words are the desktop's (docs/phone-google.md).
/// </summary>
public sealed partial class PhoneGoogleModel : ObservableObject, IDisposable
{
    readonly PhoneDrive drive;
    readonly PhoneBackgroundSwitch? background;
    readonly Func<DateTimeOffset> now;
    readonly Action<Action> ui;
    bool reading;

    public PhoneGoogleModel(PhoneDrive drive, Func<DateTimeOffset>? now = null, Action<Action>? ui = null, PhoneBackgroundSwitch? background = null)
    {
        this.drive = drive;
        this.background = background;
        this.now = now ?? (() => DateTimeOffset.UtcNow);
        this.ui = ui ?? (a => Dispatcher.UIThread.Post(a));
        drive.Changed += OnChanged;
        Refresh();
    }

    void OnChanged() => ui(Refresh);

    /// <summary>Settings closed: the app's PhoneDrive outlives it, so it lets go of this page.</summary>
    public void Dispose() => drive.Changed -= OnChanged;

    /// <summary>Opens Google's page in the browser (a test watches it instead).</summary>
    public Action<string> OpenUrl { get; set; } = url => StudyStash.Core.Dialogs.OpenUrl(url);

    public string Title => "Connect your phone through your Google account";
    public string Lede => "Your notes go into a private Syft Notes folder in your Google Drive that only Syft Notes can see.";
    public string Device => StudyStash.Core.Machine.Device;

    [ObservableProperty] public partial bool Connected { get; set; }
    [ObservableProperty] public partial bool Connecting { get; set; }
    [ObservableProperty] public partial string Email { get; set; } = "";
    [ObservableProperty] public partial string QrLink { get; set; } = "";
    [ObservableProperty] public partial Geometry? Qr { get; set; }
    [ObservableProperty] public partial int QrSize { get; set; }
    /// <summary>What just happened, or why it didn't.</summary>
    [ObservableProperty] public partial string? Say { get; set; }
    /// <summary>What the link is doing (the first copy) or what's wrong with it.</summary>
    [ObservableProperty] public partial string? Status { get; set; }
    [ObservableProperty] public partial bool SignedOut { get; set; }
    [ObservableProperty] public partial bool ConfirmingDisconnect { get; set; }
    [ObservableProperty] public partial bool Busy { get; set; }
    public ObservableCollection<DrivePhoneRow> Phones { get; } = [];

    /// <summary>This copy can sign in to Google; otherwise the button's off and <see cref="CantConnect"/> says why.</summary>
    public bool CanConnect => drive.CanConnect;
    public string CantConnect => GoogleException.Words(GoogleTrouble.NotBuilt);
    public bool ShowConnect => !Connected && !Connecting;
    public bool ShowConnected => Connected;
    public bool HasPhones => Phones.Count > 0;
    public bool NoPhones => Connected && Phones.Count == 0;
    public string Account => $"Connected as {Email}";
    public string StepOne => "On your iPhone, open Syft Notes and tap Continue with Google, or scan this code with the camera.";
    public string StepTwo => Email.Length > 0 ? $"Use the same Google account: {Email}." : "Use the same Google account as here.";
    public string HowItWorks => $"Your phone keeps a copy of your notes, so it can read them while this {Device} is off. Recordings, files and questions from your phone reach this {Device} when it's on.";
    public string ConfirmWords => "Your phone stops getting your notes, and Syft Notes empties its folder in your Google Drive.";
    public string WhenClosedTitle => "Answer your phone when Syft Notes is closed";
    public string WhenClosedSub => $"Your phone's questions, recordings and files are answered whenever this {Device} is on, even after you quit Syft Notes. Recording still needs Syft Notes open.";

    /// <summary>The switch shows once the phone is linked (and wherever Settings has one to give).</summary>
    public bool ShowWhenClosed => Connected && background is not null;
    [ObservableProperty] public partial bool AnswerWhenClosed { get; set; }
    /// <summary>Why it couldn't keep answering with the app closed; null while it's fine.</summary>
    [ObservableProperty] public partial string? WhenClosedProblem { get; set; }

    partial void OnAnswerWhenClosedChanged(bool value)
    {
        if (reading || background is null || value == background.Get()) return;
        _ = SwitchAsync(value);
    }

    async Task SwitchAsync(bool on)
    {
        Busy = true;
        try
        {
            await background!.Set(on);
        }
        finally
        {
            Busy = false;
            Refresh();
        }
    }

    partial void OnConnectedChanged(bool value) => Notify();
    partial void OnConnectingChanged(bool value) => Notify();
    partial void OnEmailChanged(string value) => Notify();

    void Notify()
    {
        foreach (string p in new[] { nameof(ShowConnect), nameof(ShowConnected), nameof(NoPhones), nameof(HasPhones), nameof(Account), nameof(StepTwo), nameof(ShowWhenClosed) })
            OnPropertyChanged(p);
    }

    /// <summary>Read <see cref="PhoneDrive"/> again: the account, the link's state and its phones.</summary>
    public void Refresh()
    {
        var account = drive.Account;
        Connected = account is not null;
        Connecting = drive.Connecting;
        SignedOut = drive.SignedOut;
        Status = drive.Status;
        if (background is not null)
        {
            reading = true;
            AnswerWhenClosed = background.Get();
            reading = false;
            WhenClosedProblem = AnswerWhenClosed ? background.Problem() : null;
        }
        if (account is null)
        {
            Email = "";
            Qr = null;
        }
        else if (account.Email != Email || Qr is null)
        {
            Email = account.Email;
            QrLink = drive.QrLink(Email);
            var modules = StudyStash.Core.Qr.Modules(QrLink);
            QrSize = modules.GetLength(0) + 2 * StudyStash.Core.Qr.Margin;
            Qr = Geometry.Parse(StudyStash.Core.Qr.PathData(modules));
        }
        var rows = drive.Phones.Select(p => new DrivePhoneRow(p.Id, p.Name, Meta(p))).ToList();
        if (!rows.SequenceEqual(Phones))
        {
            Phones.Clear();
            foreach (var r in rows) Phones.Add(r);
        }
        Notify();
    }

    /// <summary>"Open now", or "Last open 3 hours ago".</summary>
    string Meta(DrivePhone p)
    {
        var at = now();
        if (p.OpenAt(at)) return "Open now";
        var last = p.OpenUntil ?? p.Changed;
        var ago = at - last;
        string when = ago < TimeSpan.FromMinutes(1) ? "just now"
            : ago < TimeSpan.FromHours(1) ? Plural((int)ago.TotalMinutes, "minute") + " ago"
            : ago < TimeSpan.FromDays(1) ? Plural((int)ago.TotalHours, "hour") + " ago"
            : last.ToLocalTime().Date == at.ToLocalTime().Date.AddDays(-1) ? "yesterday"
            : last.ToLocalTime().ToString(last.Year == at.Year ? "MMM d" : "MMM d, yyyy", CultureInfo.InvariantCulture);
        return "Last open " + when;
    }

    static string Plural(int n, string what) => $"{n} {what}{(n == 1 ? "" : "s")}";

    /// <summary>Connect with Google: the browser opens Google's page; the account shows here once they've pressed Allow.</summary>
    [RelayCommand]
    async Task Connect()
    {
        if (Connecting) return;
        Say = null;
        try
        {
            await drive.ConnectAsync(OpenUrl);
            Say = $"Your phone can connect now. Open Syft Notes on it and tap Continue with Google.";
        }
        catch (GoogleException e)
        {
            Say = e.Message;
        }
        catch (OperationCanceledException)
        {
            Say = null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Say = $"Syft Notes couldn't keep your Google account on this {Device}: {e.Message}";
        }
        Refresh();
    }

    /// <summary>Back from the browser without finishing there.</summary>
    [RelayCommand]
    void CancelConnect() => drive.CancelConnect();

    [RelayCommand]
    void AskDisconnect() => ConfirmingDisconnect = true;

    [RelayCommand]
    void KeepConnected() => ConfirmingDisconnect = false;

    [RelayCommand]
    async Task Disconnect()
    {
        ConfirmingDisconnect = false;
        Busy = true;
        try
        {
            await drive.DisconnectAsync();
            Say = "Disconnected from Google. Your phone doesn't get your notes this way any more.";
        }
        finally
        {
            Busy = false;
            Refresh();
        }
    }

    /// <summary>Remove a phone: this {Device} stops answering it, and its box in the folder goes.</summary>
    [RelayCommand]
    async Task Remove(DrivePhoneRow? row)
    {
        if (row is null) return;
        try
        {
            await drive.RemovePhoneAsync(row.Id);
            Say = $"{row.Name} is removed: this {Device} doesn't answer it any more. To use it again, sign in on it with Continue with Google.";
        }
        catch (Exception e) when (e is DriveException or GoogleException or HttpRequestException or TaskCanceledException)
        {
            Say = "Can't reach Google Drive right now, so the phone is still there. Try again in a moment.";
        }
        Refresh();
    }
}
