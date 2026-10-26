using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StudyStash.Core;

namespace StudyStash.App.ViewModels;

/// <summary>A lecture in Granola that Egghead Notes could bring over: a row in Settings → Granola.</summary>
public sealed partial class GranolaLecture(GranolaTransfer.Waiting waiting) : ObservableObject
{
    public string Id => waiting.GranolaId;
    public string Title => waiting.Title.Length > 0 ? waiting.Title : "Untitled";
    /// <summary>"Mon 5 Oct", where Granola gave a date that reads as one.</summary>
    public string When { get; } = DateTime.TryParse(waiting.Date, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
        ? d.ToString("ddd d MMM", CultureInfo.InvariantCulture) : waiting.Date;
    /// <summary>Being brought over now: its buttons wait.</summary>
    [ObservableProperty] public partial bool Busy { get; set; }
}

/// <summary>
/// Settings → Granola: sign in to Granola (on its own page, in the browser), see the lectures there that Egghead Notes
/// doesn't have, and bring one over, or all of them, or leave one where it is. And whether a new one is announced.
/// </summary>
public sealed partial class GranolaSettingsModel : ObservableObject, IDisposable
{
    /// <summary>What the page says of itself, and of what a free Granola plan gives.</summary>
    public const string About = "If you record lectures in Granola, Egghead Notes can bring them over: each one is filed under its class and gets study notes.";
    public const string PlanNote = "Granola's free plan shares each lecture's notes from the last 30 days, not its transcript, so Egghead Notes writes its notes from Granola's summary. A paid Granola plan shares the transcript too, and the notes are written from that.";
    public const string SignInNote = "You sign in on Granola's own page in your browser. Egghead Notes never sees your password.";
    public const string NothingWaitingWords = "Nothing in Granola that Egghead Notes doesn't have.";

    readonly GranolaImport granola;
    readonly Func<bool> notifies;
    readonly Action<bool> setNotifies;
    CancellationTokenSource? signingIn;
    bool loading = true;

    public GranolaSettingsModel(GranolaImport granola, Func<bool> notifies, Action<bool> setNotifies)
    {
        this.granola = granola;
        this.notifies = notifies;
        this.setNotifies = setNotifies;
        Notify = notifies();
        loading = false;
        granola.Changed += OnChanged;
        Refresh();
    }

    /// <summary>Hands Granola's sign-in page to the system's browser; a test catches it here instead.</summary>
    public Action<string> OpenUrl { get; set; } = url => Dialogs.OpenUrl(url);

    public ObservableCollection<GranolaLecture> Lectures { get; } = [];

    [ObservableProperty] public partial bool SignedIn { get; set; }
    /// <summary>The browser is open on Granola's sign-in page and Egghead Notes is waiting for it.</summary>
    [ObservableProperty] public partial bool Connecting { get; set; }
    [ObservableProperty] public partial string ConnectedLine { get; set; } = "";
    [ObservableProperty] public partial string? Problem { get; set; }
    /// <summary>A notification when Granola has a lecture Egghead Notes doesn't.</summary>
    [ObservableProperty] public partial bool Notify { get; set; }
    [ObservableProperty] public partial bool Looking { get; set; }

    public bool SignedOut => !SignedIn;
    public bool CanConnect => !SignedIn && !Connecting;
    public bool HasProblem => !string.IsNullOrEmpty(Problem);
    public bool HasLectures => Lectures.Count > 0;
    public bool HasSeveral => Lectures.Count > 1;
    public bool NothingWaiting => SignedIn && Lectures.Count == 0;

    partial void OnSignedInChanged(bool value) => Tell();
    partial void OnConnectingChanged(bool value) => Tell();
    partial void OnProblemChanged(string? value) => OnPropertyChanged(nameof(HasProblem));

    partial void OnNotifyChanged(bool value)
    {
        if (!loading) setNotifies(value);
    }

    void Tell()
    {
        foreach (string p in new[] { nameof(SignedOut), nameof(CanConnect), nameof(HasLectures), nameof(HasSeveral), nameof(NothingWaiting) }) OnPropertyChanged(p);
    }

    void OnChanged() => Dispatcher.UIThread.Post(Refresh);

    /// <summary>The page as Granola stands now: who is signed in, and the lectures waiting (a row being brought over
    /// keeps its place and its busy mark).</summary>
    public void Refresh()
    {
        SignedIn = granola.SignedIn;
        ConnectedLine = granola.Account.Length > 0 ? $"Connected as {granola.Account}" : "Connected to Granola";
        Problem = granola.Problem;
        var busy = Lectures.Where(l => l.Busy).Select(l => l.Id).ToHashSet();
        var waiting = SignedIn ? granola.Waiting : [];
        if (!waiting.Select(w => w.GranolaId).SequenceEqual(Lectures.Select(l => l.Id)))
        {
            Lectures.Clear();
            foreach (var w in waiting) Lectures.Add(new GranolaLecture(w) { Busy = busy.Contains(w.GranolaId) });
        }
        Tell();
    }

    /// <summary>Opens Granola's sign-in page and waits for the student to allow it there; then looks at once.</summary>
    [RelayCommand]
    async Task Connect()
    {
        if (!CanConnect) return;
        Connecting = true;
        Problem = null;
        signingIn = new CancellationTokenSource();
        try
        {
            await Task.Run(() => granola.SignInAsync(url =>
            {
                OpenUrl(url);
                return Task.CompletedTask;
            }, signingIn.Token));
            await LookNow();
        }
        catch (OperationCanceledException)
        {
            // Cancel was pressed: nothing to say.
        }
        catch (Exception e) when (e is OAuthException or HttpRequestException or IOException or System.Net.Sockets.SocketException)
        {
            Problem = e is OAuthException && e.Message.Contains("timed out", StringComparison.Ordinal)
                ? "Nobody signed in within five minutes. Press Connect to try again."
                : e is System.Net.Sockets.SocketException or IOException
                    ? "Egghead Notes couldn't wait for Granola's answer: another app is using the port it comes back on. Close it and try again."
                    : "Signing in to Granola didn't finish. Press Connect to try again.";
        }
        finally
        {
            signingIn.Dispose();
            signingIn = null;
            Connecting = false;
            Refresh();
        }
    }

    [RelayCommand] void CancelConnect() => signingIn?.Cancel();

    [RelayCommand] void Disconnect() => granola.SignOut();

    /// <summary>Looks at Granola now, not at the next five minutes.</summary>
    [RelayCommand]
    async Task LookNow()
    {
        if (Looking || !granola.SignedIn) return;
        Looking = true;
        try
        {
            await Task.Run(() => granola.CheckAsync());
        }
        finally
        {
            Looking = false;
            Refresh();
        }
    }

    [RelayCommand]
    async Task Transfer(GranolaLecture lecture)
    {
        if (lecture.Busy) return;
        lecture.Busy = true;
        try
        {
            await Task.Run(() => granola.TransferAsync(lecture.Id));
        }
        finally
        {
            lecture.Busy = false;
            Refresh();
        }
    }

    /// <summary>Every waiting lecture, one after another; it stops at the first that doesn't go.</summary>
    [RelayCommand]
    async Task TransferAll()
    {
        foreach (var lecture in Lectures.ToList())
        {
            if (lecture.Busy) continue;
            lecture.Busy = true;
            bool went;
            try
            {
                went = await Task.Run(() => granola.TransferAsync(lecture.Id));
            }
            finally
            {
                lecture.Busy = false;
            }
            if (!went) break;
        }
        Refresh();
    }

    [RelayCommand] void Leave(GranolaLecture lecture) => granola.Leave(lecture.Id);

    public void Dispose()
    {
        granola.Changed -= OnChanged;
        signingIn?.Cancel();
    }
}
