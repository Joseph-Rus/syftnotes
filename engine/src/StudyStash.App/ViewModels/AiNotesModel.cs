using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StudyStash.Core;
using StudyStash.Core.Ai;

namespace StudyStash.App.ViewModels;

/// <summary>Where a lecture's notes stand: nothing running, an engine writing a fresh draft, a draft ready to
/// choose, the two side by side, or the last attempt failed. Never <c>Comparing</c> from the library itself — that's
/// this model's own "Compare" button, since the library only ever says none/working/ready/failed/cancelled.</summary>
public enum RewriteState { Idle, Rewriting, Ready, Comparing, Failed }

/// <summary>
/// The lecture's notes with "Rewrite notes" (design 17): the current notes never change on screen until "Use new" is
/// pressed, however long a rewrite takes or however it ends. Polls the library while a rewrite is running so a job
/// started elsewhere (another device, or before the app was last opened) still shows up here. Reads and drives one
/// library's AI (<see cref="IAiLibrary"/>); the view is just this.
/// </summary>
public sealed partial class AiNotesModel : ObservableObject, IDisposable
{
    readonly IAiLibrary library;
    CancellationTokenSource? pollCts;
    string lastEngine = "";

    public AiNotesModel(IAiLibrary library) => this.library = library;

    public string LectureId { get; private set; } = "";
    /// <summary>The engine id that wrote the notes on screen right now (<see cref="CurrentByline"/>'s engine): the
    /// "Rewrite notes with" menu shows it last, marked "Wrote the current notes".</summary>
    public string WriterId { get; private set; } = "ollama";

    [ObservableProperty] public partial RewriteState State { get; set; } = RewriteState.Idle;
    [ObservableProperty] public partial string CurrentMarkdown { get; set; } = "";
    [ObservableProperty] public partial string CurrentByline { get; set; } = "";
    [ObservableProperty] public partial string DraftMarkdown { get; set; } = "";
    [ObservableProperty] public partial string DraftByline { get; set; } = "";
    /// <summary>The engine rewriting (or that just finished, or failed): the bar's "Rewriting with Claude Code…".</summary>
    [ObservableProperty] public partial string EngineName { get; set; } = "";
    [ObservableProperty] public partial string Error { get; set; } = "";
    /// <summary>The "Rewrite notes with" menu's pick, before a rewrite starts (starts on the writer).</summary>
    [ObservableProperty] public partial string Engine { get; set; } = "ollama";
    [ObservableProperty] public partial bool MenuOpen { get; set; }
    [ObservableProperty] public partial bool Busy { get; set; }
    /// <summary>A refusal from the library (already installed, already running, …): a passing word, not a card.</summary>
    [ObservableProperty] public partial string? Say { get; set; }
    [ObservableProperty] public partial bool Offline { get; set; }
    [ObservableProperty] public partial bool OlderLibrary { get; set; }

    /// <summary>What the notes area shows: the draft once it's ready (or while comparing), the current notes
    /// otherwise — either way with a leading "Summary" heading dropped, since the header row already says it.</summary>
    public string ShownMarkdown => AiWords.DropLeadingSummary(State is RewriteState.Ready or RewriteState.Comparing ? DraftMarkdown : CurrentMarkdown);
    public string ShownByline => State is RewriteState.Ready or RewriteState.Comparing ? DraftByline : CurrentByline;
    public bool ShowRewriteButton => State == RewriteState.Idle;
    public bool ShowBar => State is RewriteState.Rewriting or RewriteState.Ready or RewriteState.Failed;
    public bool Dimmed => MenuOpen;

    public EngineMenuModel Menu { get; } = new() { Header = "Rewrite notes with", Width = 280, FooterText = "Keeps your current notes until you choose" };

    /// <summary>Fired once "Use new" saves the draft as the lecture's notes: the host reloads the lecture.</summary>
    public event Action? NotesChanged;
    /// <summary>How long to wait between polls while working (2 s); tests make this instant.</summary>
    public Func<TimeSpan, CancellationToken, Task> Delay { get; set; } = Task.Delay;
    /// <summary>What "now" is, for the draft's "just now"/"5 min ago" byline; tests can fix it.</summary>
    public Func<DateTime> Now { get; set; } = () => DateTime.Now;

    static string FindEngineId(string engineName) => Engines.Order.FirstOrDefault(id => Engines.Name(id) == engineName, "ollama");

    partial void OnStateChanged(RewriteState value)
    {
        OnPropertyChanged(nameof(ShownMarkdown));
        OnPropertyChanged(nameof(ShownByline));
        OnPropertyChanged(nameof(ShowRewriteButton));
        OnPropertyChanged(nameof(ShowBar));
    }

    partial void OnMenuOpenChanged(bool value) => OnPropertyChanged(nameof(Dimmed));

    /// <summary>Sets the notes as they stand right now (from the lecture the host already has), then reads the
    /// library once to build the "Rewrite notes with" menu and pick up any job already running.</summary>
    public async Task Load(string lectureId, string markdown, string notesModel, string updatedAt)
    {
        LectureId = lectureId;
        SetCurrent(markdown, Engines.WhoWrote(notesModel), updatedAt);
        Engine = WriterId;
        await Refresh();
    }

    void SetCurrent(string markdown, string engineName, string updatedAt)
    {
        CurrentMarkdown = markdown;
        CurrentByline = AiWords.WrittenByline(engineName, updatedAt);
        WriterId = FindEngineId(engineName);
        OnPropertyChanged(nameof(ShownMarkdown));
        OnPropertyChanged(nameof(ShownByline));
    }

    [RelayCommand]
    public Task Refresh() => LoadOnce();

    async Task LoadOnce()
    {
        AiOverview? overview;
        RewriteInfo? info;
        try
        {
            overview = await library.EnginesAsync();
            info = await library.RewriteAsync(LectureId);
        }
        catch
        {
            Offline = true;
            return;
        }
        if (overview is null || info is null)
        {
            OlderLibrary = true;
            return;
        }
        OlderLibrary = false;
        Offline = false;
        Apply(info);
        BuildMenu(overview);
    }

    void BuildMenu(AiOverview overview)
    {
        var usable = overview.Engines.Where(e => e.State != "not_installed").ToList();
        var others = usable.Where(e => e.Id != WriterId);
        var ordered = others.Where(e => e.Id == Engine).Concat(others.Where(e => e.Id != Engine));
        var writer = usable.FirstOrDefault(e => e.Id == WriterId);
        var full = writer is null ? ordered : ordered.Append(writer);

        Menu.Items.Clear();
        foreach (var e in full)
        {
            bool isWriter = e.Id == WriterId;
            var item = new EngineMenuItem(e.Id, e.Name, AiWords.RewriteEngineSubtitle(e.Id, e.State, isWriter), isWriter, AiWords.EngineUsable(e.State));
            item.Command = new AsyncRelayCommand(() => Rewrite(item.Id));
            Menu.Items.Add(item);
        }
        if (Menu.Items.All(i => i.Id != Engine)) Engine = WriterId;
        foreach (var i in Menu.Items) i.Selected = i.Id == Engine;
    }

    void Apply(RewriteInfo info)
    {
        if (info.Current is { } cur) SetCurrent(cur.Markdown, cur.By, cur.At);
        EngineName = info.EngineName;
        Error = info.Error;
        if (info.Draft is { } d)
        {
            DraftMarkdown = d.Markdown;
            DraftByline = AiWords.DraftByline(d.By, d.At, Now());
        }
        State = info.State switch
        {
            "working" => RewriteState.Rewriting,
            "ready" => RewriteState.Ready,
            "failed" => RewriteState.Failed,
            _ => State == RewriteState.Comparing ? RewriteState.Comparing : RewriteState.Idle,
        };
        if (State == RewriteState.Rewriting) { if (pollCts is null) StartPolling(); }
        else StopPolling();
    }

    void StartPolling()
    {
        pollCts = new CancellationTokenSource();
        _ = PollLoop(pollCts.Token);
    }

    void StopPolling()
    {
        pollCts?.Cancel();
        pollCts = null;
    }

    async Task PollLoop(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested && State == RewriteState.Rewriting)
            {
                await Delay(TimeSpan.FromSeconds(2), ct);
                if (ct.IsCancellationRequested) return;
                RewriteInfo? info;
                try
                {
                    info = await library.RewriteAsync(LectureId);
                }
                catch
                {
                    Offline = true;
                    return;
                }
                if (info is null)
                {
                    OlderLibrary = true;
                    return;
                }
                Apply(info);
            }
        }
        catch (OperationCanceledException)
        {
            // Cancel()/Dispose() stopped this on purpose.
        }
    }

    [RelayCommand]
    async Task Rewrite(string engine)
    {
        if (Busy) return;
        Busy = true;
        Say = null;
        lastEngine = engine;
        try
        {
            var info = await library.RewriteStartAsync(LectureId, engine);
            MenuOpen = false;
            if (info is null) { OlderLibrary = true; return; }
            Apply(info);
        }
        catch (LibraryRefusedException ex)
        {
            Say = ex.Message;
        }
        catch
        {
            Offline = true;
        }
        finally
        {
            Busy = false;
        }
    }

    [RelayCommand]
    async Task Cancel()
    {
        Busy = true;
        try
        {
            var info = await library.RewriteCancelAsync(LectureId);
            if (info is null) { OlderLibrary = true; return; }
            Apply(info);
        }
        catch (LibraryRefusedException ex)
        {
            Say = ex.Message;
        }
        catch
        {
            Offline = true;
        }
        finally
        {
            Busy = false;
        }
    }

    [RelayCommand]
    async Task KeepOld()
    {
        Busy = true;
        try
        {
            var info = await library.RewriteKeepAsync(LectureId);
            if (info is null) { OlderLibrary = true; return; }
            Apply(info);
        }
        catch (LibraryRefusedException ex)
        {
            Say = ex.Message;
        }
        catch
        {
            Offline = true;
        }
        finally
        {
            Busy = false;
        }
    }

    [RelayCommand]
    void Compare()
    {
        if (State == RewriteState.Ready) State = RewriteState.Comparing;
    }

    [RelayCommand]
    void BackFromCompare()
    {
        if (State == RewriteState.Comparing) State = RewriteState.Ready;
    }

    [RelayCommand]
    async Task UseNew()
    {
        Busy = true;
        try
        {
            var info = await library.RewriteUseAsync(LectureId);
            if (info is null) { OlderLibrary = true; return; }
            Apply(info);
            NotesChanged?.Invoke();
        }
        catch (LibraryRefusedException ex)
        {
            Say = ex.Message;
        }
        catch
        {
            Offline = true;
        }
        finally
        {
            Busy = false;
        }
    }

    [RelayCommand]
    Task TryAgain() => Rewrite(lastEngine.Length > 0 ? lastEngine : WriterId);

    [RelayCommand]
    void Dismiss()
    {
        if (State == RewriteState.Failed) { State = RewriteState.Idle; Error = ""; }
    }

    public void Dispose() => StopPolling();
}
