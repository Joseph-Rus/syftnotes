using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StudyStash.App.Services;

namespace StudyStash.App.ViewModels;

/// <summary>Where one line of the first sync stands: still to come, under way, waiting on the student or their
/// browser, done, done with some of it missing, or stopped.</summary>
public enum StageState { ToDo, Working, Waiting, Done, Partial, Failed }

/// <summary>One line of the first sync's list ("Getting assignments and due dates"), with how far it's got ("3 of 6")
/// and, while it's counting, the share of it done for the thin bar under it.</summary>
public sealed partial class SyncStage : ObservableObject
{
    public required string Title { get; init; }

    [NotifyPropertyChangedFor(nameof(IsToDo), nameof(IsWorking), nameof(IsWaiting), nameof(IsDone), nameof(IsPartial), nameof(IsFailed), nameof(IsBusy), nameof(ShowBar))]
    [ObservableProperty]
    public partial StageState State { get; set; }

    /// <summary>What it's doing or found, after its title: "6 found", "3 of 6", "Waiting for Chrome…".</summary>
    [ObservableProperty] public partial string Detail { get; set; } = "";

    /// <summary>How much of it is done (0 to 1) while it counts; null when there's nothing to count yet.</summary>
    [NotifyPropertyChangedFor(nameof(ShowBar), nameof(BarWidth))]
    [ObservableProperty]
    public partial double? Fraction { get; set; }

    public bool IsToDo => State == StageState.ToDo;
    public bool IsWorking => State == StageState.Working;
    public bool IsWaiting => State == StageState.Waiting;
    public bool IsDone => State == StageState.Done;
    public bool IsPartial => State == StageState.Partial;
    public bool IsFailed => State == StageState.Failed;
    /// <summary>Under way or waiting: the loader turns beside it.</summary>
    public bool IsBusy => State is StageState.Working or StageState.Waiting;
    public bool ShowBar => IsBusy && Fraction is not null;
    /// <summary>The bar's fill, out of its 360-wide track.</summary>
    public double BarWidth => Math.Clamp(Fraction ?? 0, 0, 1) * 360;
}

/// <summary>
/// The first run's sync screen: the classes the student's Canvas has, then their assignments and due dates, then their
/// files and pages, one line each with a tick when it's done, fed by what really happens. The classes come from Find
/// my courses (<see cref="CanvasConnectModel"/>, through the student's own browser); the courses it ticks become
/// classes in the library and the first sync starts; after that every line follows the library's own count of each
/// listing (<see cref="CanvasApi.SyncProgressInfo"/>) as the shared <see cref="CanvasWatch"/> hears it, every three
/// seconds while this shows. A wait is said as what it waits for (signing in to Canvas, the browser, Canvas asking to
/// slow down); a problem is said in plain words with Try again; nothing is ever a timer pretending to work. Continue
/// opens once the assignments are in: the files can carry on behind the next step.
/// </summary>
public sealed partial class FirstSyncModel : ObservableObject, IDisposable
{
    /// <summary>Without any change for this long, the screen says what it's waiting for, so it never looks stuck.</summary>
    public static readonly TimeSpan Patience = TimeSpan.FromSeconds(20);

    // Each line's listings. The planner (one request for every class, after each class's own listings) goes with the
    // files and pages, so the assignments line counts up class by class as each class's assignments come in.
    static readonly string[] DueListings = ["assignments", "submissions"];
    static readonly string[] FileListings = ["modules", "announcements", "files", "quizzes", "discussions", "planner"];

    readonly CanvasConnectModel connect;
    readonly CanvasContext context;
    readonly CanvasWatch watch;
    IDisposable? hurry;
    bool started, disposed;
    /// <summary>The courses are classes now, and the sync was asked for.</summary>
    bool requested;
    /// <summary>The library said it was syncing since it was asked.</summary>
    bool sawSyncing;
    /// <summary>When the last sync before this one finished (null: never), to tell this one's end from it.</summary>
    DateTimeOffset? doneBefore;
    /// <summary>What was last shown, and when it last changed (for <see cref="Patience"/>).</summary>
    string shown = "";
    DateTimeOffset movedAt;
    /// <summary>Bringing the courses in didn't work: Try again does that again.</summary>
    bool bringInFailed;

    public FirstSyncModel(CanvasConnectModel connect, CanvasContext context, CanvasWatch watch)
    {
        this.connect = connect;
        this.context = context;
        this.watch = watch;
        Stages = [Find, Due, Files];
    }

    /// <summary>The time now (tests move it on to see the screen say what it's waiting for).</summary>
    public Func<DateTimeOffset> Now { get; init; } = () => DateTimeOffset.Now;

    /// <summary>The library made classes from the courses: the app reads its classes again (the sidebar shows them).</summary>
    public Func<Task>? ClassesChanged { get; init; }

    public SyncStage Find { get; } = new() { Title = "Finding your classes" };
    public SyncStage Due { get; } = new() { Title = "Getting assignments and due dates" };
    public SyncStage Files { get; } = new() { Title = "Getting files and pages" };
    public ObservableCollection<SyncStage> Stages { get; }

    /// <summary>The courses found (all of them, ticked or not), once Find my courses answered.</summary>
    [ObservableProperty] public partial int FoundCount { get; set; }

    /// <summary>Every line is done: the sync finished.</summary>
    [NotifyPropertyChangedFor(nameof(CanContinue), nameof(CanSkip), nameof(HasDoneLine))]
    [ObservableProperty]
    public partial bool Finished { get; set; }

    /// <summary>What the end says: all in, or in with some of Canvas missing.</summary>
    [ObservableProperty] public partial string DoneLine { get; set; } = "";
    public bool HasDoneLine => Finished && DoneLine.Length > 0;

    /// <summary>Something stopped it, in plain words; Try again shows beside it.</summary>
    [NotifyPropertyChangedFor(nameof(HasProblem), nameof(CanSkip))]
    [ObservableProperty]
    public partial string? Problem { get; set; }
    public bool HasProblem => !string.IsNullOrEmpty(Problem);

    /// <summary>Canvas needs the student signed in, in their browser: Open Canvas shows.</summary>
    [ObservableProperty] public partial bool SignInNeeded { get; set; }

    /// <summary>Nothing has moved for a while: what it's waiting for, so it never looks stuck. "" otherwise.</summary>
    [NotifyPropertyChangedFor(nameof(HasSlowLine))]
    [ObservableProperty]
    public partial string SlowLine { get; set; } = "";
    public bool HasSlowLine => SlowLine.Length > 0;

    /// <summary>Continue opens once the assignments and due dates are in (the files carry on in the background), or at
    /// the end.</summary>
    public bool CanContinue => Finished || Due.State is StageState.Done or StageState.Partial;
    /// <summary>"Skip for now", for a student who doesn't want to wait (or is stopped): the sync carries on, or can be
    /// tried again from Settings → Canvas.</summary>
    public bool CanSkip => !CanContinue;
    /// <summary>The assignments are in and the files aren't yet: Continue is open, and this says the rest carries on.</summary>
    public bool CarryOn => CanContinue && !Finished && !HasProblem;
    public string CarryOnLine => $"You can carry on: the files keep coming in while {BrowserName} is open.";

    /// <summary>The browser that reads Canvas, by its name ("Chrome"), or "your browser".</summary>
    string BrowserName => connect.ConnectedIn();

    /// <summary>Starts following: the courses Find my courses finds (it may be under way already, or done), then the
    /// sync. Once only.</summary>
    public async Task StartAsync()
    {
        if (started) return;
        started = true;
        movedAt = Now();
        hurry = watch.Hurry();
        watch.Changed += OnWatchChanged;
        connect.PropertyChanged += OnConnectChanged;
        await FollowFindAsync();
    }

    void OnConnectChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(CanvasConnectModel.FindingCourses) or nameof(CanvasConnectModel.CoursesSay) or nameof(CanvasConnectModel.SignedOut)
            or nameof(CanvasConnectModel.Current))
            _ = FollowFindAsync();
    }

    /// <summary>The first line follows Find my courses: looking, waiting for the student to sign in to Canvas, what
    /// went wrong, or how many it found; then the found courses are brought in.</summary>
    async Task FollowFindAsync()
    {
        if (disposed || requested || bringingIn) return;
        if (connect.SignedOut)
        {
            Find.State = StageState.Waiting;
            Find.Detail = $"Sign in to Canvas in {BrowserName}";
            SignInNeeded = true;
            Problem = null;
        }
        else if (connect.AllDone)
        {
            SignInNeeded = false;
            Problem = null;
            await FoundAsync();
        }
        else if (connect.FindingCourses || connect.CoursesSay is null)
        {
            Find.State = StageState.Working;
            Find.Detail = $"Asking Canvas through {BrowserName}";
            SignInNeeded = false;
            Problem = null;
        }
        else
        {
            // Find my courses said why it found none (the library didn't answer, Canvas refused): said, with Try again.
            Find.State = StageState.Failed;
            Find.Detail = "";
            Problem = connect.CoursesSay;
            SignInNeeded = false;
        }
        Moved();
    }

    bool bringingIn;

    /// <summary>The courses are found: the first line says how many, the ticked ones become classes, and the first sync
    /// is asked for.</summary>
    async Task FoundAsync()
    {
        FoundCount = connect.Picker.Courses.Count;
        var bring = connect.Found;
        Find.Detail = FoundCount == 1 ? "1 found" : $"{FoundCount} found";
        if (bring.Count == 0)
        {
            // Nothing this term (or nothing ticked): nothing to sync. The classes can be added by hand on Home.
            Find.State = StageState.Partial;
            if (FoundCount == 0) Find.Detail = "none found";
            Due.State = Files.State = StageState.ToDo;
            DoneLine = "Canvas has no classes for you this term. Add your classes on Home any time, or connect them again from Settings → Canvas.";
            Finished = true;
            return;
        }
        Find.State = StageState.Done;
        await BringInAsync(bring);
    }

    async Task BringInAsync(IReadOnlyList<FoundCourse> bring)
    {
        if (context.Client is not { } client) return;
        bringingIn = true;
        bringInFailed = false;
        Problem = null;
        Due.State = StageState.Working;
        Due.Detail = "Starting";
        try
        {
            doneBefore = watch.State?.LastSync;
            // The library makes the classes itself from the courses chosen, and remembers the choice (only those sync).
            await client.ChooseAsync([.. bring.Select(c => c.Id)]);
            await client.SaveAsync(sync: true);
            requested = true;
            if (ClassesChanged is not null) await ClassesChanged();
        }
        catch (Exception e) when (e is CanvasLibraryException or HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            bringInFailed = true;
            Due.State = StageState.Failed;
            Due.Detail = "";
            Problem = e is CanvasLibraryException { Message.Length: > 0 } ? e.Message : "Your library didn't answer. Try again in a moment.";
            return;
        }
        finally
        {
            bringingIn = false;
        }
        Due.Detail = $"Waiting for {BrowserName} to start";
        Moved();
        try
        {
            await watch.RefreshAsync();
        }
        catch (Exception e) when (e is CanvasLibraryException or HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            // The watch asks again in three seconds.
        }
    }

    void OnWatchChanged()
    {
        if (disposed) return;
        if (requested && watch.State is { } s) Follow(s);
        else Moved();
    }

    /// <summary>After the sync was asked for, every answer from the library moves the lines on.</summary>
    public void Follow(CanvasApi.State s)
    {
        if (Finished && !HasProblem) return;
        SignInNeeded = false;
        var working = Due.State is StageState.Done or StageState.Partial ? Files : Due;
        switch (s.Status)
        {
            case "syncing":
                sawSyncing = true;
                Problem = null;
                int total = s.Progress?.Classes is > 0 and var n ? n : s.Syncing?.Total ?? 0;
                Count(Due, DueListings, s.Progress, total, done: false);
                if (Due.State == StageState.Done) Count(Files, FileListings, s.Progress, total, done: false);
                else Files.State = StageState.ToDo;
                if (s.PausedUntil is { } until && until > Now())
                {
                    var paused = Due.State == StageState.Done ? Files : Due;
                    paused.State = StageState.Waiting;
                    paused.Detail = $"Canvas asked to slow down, so it carries on at {CanvasWords.Clock(until, context.Clock.Zone)}";
                }
                break;
            case "signed_out":
                working.State = StageState.Waiting;
                working.Detail = $"Sign in to Canvas in {BrowserName}";
                SignInNeeded = true;
                break;
            case "chrome_away":
                working.State = StageState.Waiting;
                working.Detail = $"Waiting for {BrowserName}: open it and leave it open";
                break;
            default:
                // Not syncing ("connected", or "error" for a sync that ended with something unread): finished, if it ran
                // since it was asked for; otherwise the browser hasn't asked for its first piece of work yet.
                bool ran = sawSyncing || s.LastSync is { } last && last != doneBefore;
                if (!ran)
                {
                    Due.State = StageState.Working;
                    Due.Detail = $"Waiting for {BrowserName} to start";
                    Files.State = StageState.ToDo;
                    Files.Detail = "";
                }
                else if (NoneCame(s.Progress, DueListings))
                {
                    // Canvas sent none of the assignments: nothing to show yet, and Try again asks for it all again.
                    Due.State = StageState.Failed;
                    Due.Detail = "";
                    Due.Fraction = null;
                    Files.State = StageState.ToDo;
                    Problem = "Canvas didn't send your assignments and due dates. It may be busy: try again in a moment.";
                }
                else Finish(s);
                break;
        }
        Moved();
    }

    /// <summary>Every class gave up on one of these listings: nothing of that line came through.</summary>
    static bool NoneCame(CanvasApi.SyncProgressInfo? progress, string[] listings) =>
        progress is { Classes: > 0 } p && listings.Any(l => p.Listings.TryGetValue(l, out var one) && one.Failed >= p.Classes);

    /// <summary>A line's count from the listings it's made of: its classes finished with every one of them.</summary>
    static void Count(SyncStage stage, string[] listings, CanvasApi.SyncProgressInfo? progress, int total, bool done)
    {
        int finished = progress is null ? 0 : listings.Min(l => progress.Listings.TryGetValue(l, out var p) ? p.Done : 0);
        bool failed = progress is not null && listings.Any(l => progress.Listings.TryGetValue(l, out var p) && p.Failed > 0);
        if (done || total > 0 && finished >= total)
        {
            stage.State = failed ? StageState.Partial : StageState.Done;
            stage.Fraction = null;
            stage.Detail = total > 0 ? $"{total} of {total}" : "";
            return;
        }
        stage.State = StageState.Working;
        stage.Fraction = total > 0 ? (double)finished / total : null;
        stage.Detail = total > 0 ? $"{finished} of {total}" : "";
    }

    void Finish(CanvasApi.State s)
    {
        int total = s.Progress?.Classes ?? 0;
        Count(Due, DueListings, s.Progress, total, done: true);
        Count(Files, FileListings, s.Progress, total, done: true);
        Problem = null;
        // The library also says "error" for a sync that ended with something it couldn't read or save.
        if (s.Status == "error" && !Due.IsPartial && !Files.IsPartial) Files.State = StageState.Partial;
        bool partial = Due.IsPartial || Files.IsPartial;
        DoneLine = partial
            ? "Done. Some of Canvas didn't come through: Syft Notes tries those again at the next sync."
            : "Done. Your classes are in.";
        SlowLine = "";
        Finished = true;
    }

    /// <summary>What's shown now: when it changes the patience starts again; when it hasn't for <see cref="Patience"/>
    /// the screen says what it's waiting for.</summary>
    void Moved()
    {
        // Continue opens with the assignments line, not only at the end: the buttons hear of every change.
        OnPropertyChanged(nameof(CanContinue));
        OnPropertyChanged(nameof(CanSkip));
        OnPropertyChanged(nameof(CarryOn));
        OnPropertyChanged(nameof(CarryOnLine));
        string now = string.Join("|", Stages.Select(st => $"{st.State}:{st.Detail}")) + $"|{Problem}|{Finished}";
        var at = Now();
        if (now != shown)
        {
            shown = now;
            movedAt = at;
            SlowLine = "";
            return;
        }
        if (Finished || HasProblem || at - movedAt < Patience) return;
        SlowLine = SignInNeeded
            ? $"Syft Notes carries on by itself once you're signed in to Canvas in {BrowserName}."
            : Find.IsBusy
                ? $"Still waiting for {BrowserName} to answer. Keep it open; the first time can take a minute."
                : $"Still working: {BrowserName} reads Canvas a page at a time, and a big class takes a few minutes. Keep {BrowserName} open.";
    }

    /// <summary>Try again: whatever stopped (finding the courses, bringing them in, or the sync) is asked for again.</summary>
    [RelayCommand]
    async Task TryAgain()
    {
        if (context.Client is not { } client) return;
        Problem = null;
        if (!requested && bringInFailed)
        {
            await BringInAsync(connect.Found);
            return;
        }
        if (!requested)
        {
            Find.State = StageState.Working;
            await connect.FindCoursesCommand.ExecuteAsync(null);
            return;
        }
        var working = Due.State is StageState.Done or StageState.Partial ? Files : Due;
        working.State = StageState.Working;
        working.Detail = $"Waiting for {BrowserName} to start";
        try
        {
            doneBefore = watch.State?.LastSync;
            sawSyncing = false;
            Finished = false;
            await client.SaveAsync(sync: true);
            await watch.RefreshAsync();
        }
        catch (Exception e) when (e is CanvasLibraryException or HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            working.State = StageState.Failed;
            Problem = e is CanvasLibraryException { Message.Length: > 0 } ? e.Message : "Your library didn't answer. Try again in a moment.";
        }
    }

    /// <summary>Open Canvas, in the browser that reads it, to sign in.</summary>
    [RelayCommand]
    void OpenCanvas() => connect.OpenCanvasCommand.Execute(null);

    public void Dispose()
    {
        disposed = true;
        watch.Changed -= OnWatchChanged;
        connect.PropertyChanged -= OnConnectChanged;
        hurry?.Dispose();
        hurry = null;
    }

}
