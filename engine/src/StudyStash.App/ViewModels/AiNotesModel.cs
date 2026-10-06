using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StudyStash.Core;
using StudyStash.Core.Ai;
using StudyStash.Core.Rich;

namespace StudyStash.App.ViewModels;

/// <summary>Where a lecture's notes stand: nothing running, an engine writing a fresh draft, a draft ready to
/// choose, the two side by side, or the last attempt failed. Never <c>Comparing</c> from the library itself — that's
/// this model's own "Compare" button, since the library only ever says none/working/ready/failed/cancelled.</summary>
public enum RewriteState { Idle, Rewriting, Ready, Comparing, Failed }

/// <summary>
/// The lecture's notes with "Rewrite notes" (design 17): the current notes never change on screen until "Use new" is
/// pressed, however long a rewrite takes or however it ends. Polls the library while a rewrite is running so a job
/// started elsewhere (another device, or before the app was last opened) still shows up here. The notes arrive before
/// their diagrams: while the library adds those, the byline says so quietly and the library is asked now and then, and
/// the diagrams appear in the notes on screen where they go, the rest of the page as it was. "Edit" turns the current
/// notes into a text editor (<see cref="NoteEdit"/>: their Markdown, a line for each diagram) until Save or Cancel.
/// Reads and drives one library's AI (<see cref="IAiLibrary"/>); the view is just this.
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
    /// <summary>Closes the "Rewrite notes with" popup once an engine is picked; the view wires this to the real flyout.</summary>
    public Action? CloseMenu { get; set; }
    [ObservableProperty] public partial bool Busy { get; set; }
    /// <summary>A refusal from the library (already installed, already running, …): a passing word, not a card.</summary>
    [ObservableProperty] public partial string? Say { get; set; }
    [ObservableProperty] public partial bool Offline { get; set; }
    [ObservableProperty] public partial bool OlderLibrary { get; set; }

    /// <summary>What the notes area shows: the draft once it's ready (or while comparing), the current notes
    /// otherwise — either way with a leading "Summary" heading dropped, since the header row already says it.</summary>
    public string ShownMarkdown => State is RewriteState.Ready or RewriteState.Comparing ? DraftBody : CurrentBody;
    public string ShownByline => State is RewriteState.Ready or RewriteState.Comparing ? DraftByline
        : DiagramsLine.Length == 0 ? CurrentByline : CurrentByline.Length == 0 ? DiagramsLine : $"{CurrentByline} · {DiagramsLine}";

    /// <summary>"Adding diagrams…" while the library adds the current notes' diagrams (they follow the notes by a few
    /// minutes), "Diagrams added" once they've appeared, until the lecture is left; "" otherwise. On the byline.</summary>
    [ObservableProperty] public partial string DiagramsLine { get; set; } = "";
    public bool AddingDiagrams { get; private set; }
    /// <summary>The notes when the diagrams were first seen on their way: different once they're in.</summary>
    string beforeDiagrams = "";

    partial void OnDiagramsLineChanged(string value) => OnPropertyChanged(nameof(ShownByline));
    /// <summary>The current and draft notes, each with a leading "Summary" heading dropped: what the compare
    /// view's two columns show side by side.</summary>
    public string CurrentBody => AiWords.DropLeadingSummary(CurrentMarkdown);
    public string DraftBody => AiWords.DropLeadingSummary(DraftMarkdown);
    public bool ShowRewriteButton => State == RewriteState.Idle && !Editing;
    /// <summary>"Edit" sits beside "Rewrite notes", and goes when it does: the notes are edited while nothing else is
    /// happening to them.</summary>
    public bool ShowEditButton => State == RewriteState.Idle && !Editing;
    /// <summary>The notes as they read: not while the two are compared side by side, or the editor has their place.</summary>
    public bool ShowNotes => !IsComparing && !Editing;
    public bool ShowBar => State is RewriteState.Rewriting or RewriteState.Ready or RewriteState.Failed;
    public bool Dimmed => MenuOpen;
    public bool IsRewriting => State == RewriteState.Rewriting;
    public bool IsReady => State == RewriteState.Ready;
    public bool IsComparing => State == RewriteState.Comparing;
    public bool IsFailed => State == RewriteState.Failed;

    /// <summary>The failed bar's words, built the same way the matching problem card would be, so the two agree.</summary>
    public string FailedTitle => AiWords.ProblemTitle("rewrite_failed", EngineName, "");
    public string FailedMessage => AiWords.ProblemMessage("rewrite_failed", EngineName, "", "", 0, Error);
    public string RewritingLead => $"Rewriting with {EngineName}…";
    public string ReadyLead => $"New notes from {EngineName} are ready.";
    public string DraftHeading => $"New notes from {EngineName}";

    public EngineMenuModel Menu { get; } = new() { Header = "Rewrite notes with", Width = 280, FooterText = "Keeps your current notes until you choose" };

    /// <summary>Fired once "Use new" saves the draft as the lecture's notes: the host reloads the lecture.</summary>
    public event Action? NotesChanged;
    /// <summary>How long to wait between polls while working (2 s; 5 s while only diagrams are on their way); tests
    /// make this instant.</summary>
    public Func<TimeSpan, CancellationToken, Task> Delay { get; set; } = Task.Delay;
    /// <summary>What "now" is, for the draft's "just now"/"5 min ago" byline; tests can fix it.</summary>
    public Func<DateTime> Now { get; set; } = () => DateTime.Now;

    static string FindEngineId(string engineName) => Engines.Order.FirstOrDefault(id => Engines.Name(id) == engineName, "ollama");

    partial void OnStateChanged(RewriteState value)
    {
        OnPropertyChanged(nameof(ShownMarkdown));
        OnPropertyChanged(nameof(ShownByline));
        OnPropertyChanged(nameof(ShowRewriteButton));
        OnPropertyChanged(nameof(ShowEditButton));
        OnPropertyChanged(nameof(ShowNotes));
        OnPropertyChanged(nameof(ShowBar));
        OnPropertyChanged(nameof(IsRewriting));
        OnPropertyChanged(nameof(IsReady));
        OnPropertyChanged(nameof(IsComparing));
        OnPropertyChanged(nameof(IsFailed));
    }

    partial void OnEngineNameChanged(string value)
    {
        OnPropertyChanged(nameof(FailedTitle));
        OnPropertyChanged(nameof(FailedMessage));
        OnPropertyChanged(nameof(RewritingLead));
        OnPropertyChanged(nameof(ReadyLead));
        OnPropertyChanged(nameof(DraftHeading));
    }

    partial void OnErrorChanged(string value) => OnPropertyChanged(nameof(FailedMessage));

    partial void OnMenuOpenChanged(bool value) => OnPropertyChanged(nameof(Dimmed));

    partial void OnEngineChanged(string value)
    {
        foreach (var i in Menu.Items) i.Selected = i.Id == value;
    }

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
        bool changed = markdown != CurrentMarkdown;
        CurrentMarkdown = markdown;
        CurrentByline = AiWords.WrittenByline(engineName, updatedAt);
        WriterId = FindEngineId(engineName);
        OnPropertyChanged(nameof(CurrentBody));
        OnPropertyChanged(nameof(ShownMarkdown));
        OnPropertyChanged(nameof(ShownByline));
        // Notes that changed under an editor nobody has typed in yet: it starts again from them. Typed in, it keeps
        // the student's words, and Save hears from the library that the notes changed.
        if (changed && Editing && !EditDirty) BeginEdit();
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
        if (info.Engine.Length > 0) lastEngine = info.Engine;
        if (info.Draft is { } d)
        {
            DraftMarkdown = d.Markdown;
            DraftByline = AiWords.DraftByline(d.By, d.At, Now());
            OnPropertyChanged(nameof(DraftBody));
            OnPropertyChanged(nameof(ShownMarkdown));
        }
        State = info.State switch
        {
            "working" => RewriteState.Rewriting,
            "ready" => RewriteState.Ready,
            "failed" => RewriteState.Failed,
            _ => State == RewriteState.Comparing ? RewriteState.Comparing : RewriteState.Idle,
        };
        bool was = AddingDiagrams;
        AddingDiagrams = info.Diagrams == "adding";
        if (AddingDiagrams && !was) beforeDiagrams = CurrentMarkdown;
        if (AddingDiagrams) DiagramsLine = "Adding diagrams…";
        else if (was) DiagramsLine = CurrentMarkdown != beforeDiagrams ? "Diagrams added" : "";
        if (Polls) { if (pollCts is null) StartPolling(); }
        else StopPolling();
    }

    /// <summary>The library is asked again while a rewrite runs or diagrams are on their way.</summary>
    bool Polls => State == RewriteState.Rewriting || AddingDiagrams;

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
            while (!ct.IsCancellationRequested && Polls)
            {
                await Delay(TimeSpan.FromSeconds(State == RewriteState.Rewriting ? 2 : 5), ct);
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
        // The pick shows as picked, and the menu goes: the bar under the notes says what happens next.
        Engine = engine;
        CloseMenu?.Invoke();
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

    // --- Editing the notes by hand --------------------------------------------------------------------------------

    NoteEdit? edit;
    /// <summary>The notes the edit started from, whole: the library is told, so it never saves over other ones unseen.</summary>
    string editFrom = "";

    /// <summary>The student is editing the current notes: the notes area is a text editor until Save or Cancel.</summary>
    [ObservableProperty] public partial bool Editing { get; set; }
    /// <summary>The editor's text: the notes' Markdown without the leading "Summary" heading (the header row says it),
    /// each diagram one line (<see cref="NoteEdit"/>).</summary>
    [ObservableProperty] public partial string EditText { get; set; } = "";
    /// <summary>Why the last Save didn't save, said in the editor (what was typed is still there); "" otherwise.</summary>
    [ObservableProperty] public partial string EditProblem { get; set; } = "";
    public bool HasEditProblem => EditProblem.Length > 0;
    /// <summary>The notes have diagrams, each a "[Diagram 1]" line in the editor: it says what those lines are.</summary>
    public bool EditHasDiagrams => edit?.HasDiagrams == true;
    /// <summary>Something's been typed that isn't saved: the host keeps these notes when their lecture is left.</summary>
    public bool EditDirty => Editing && edit is not null && EditText.ReplaceLineEndings("\n") != edit.Text;
    public bool CanSaveEdit => !Busy && EditText.Trim().Length > 0;

    partial void OnEditingChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowRewriteButton));
        OnPropertyChanged(nameof(ShowEditButton));
        OnPropertyChanged(nameof(ShowNotes));
        OnPropertyChanged(nameof(EditDirty));
    }

    partial void OnEditTextChanged(string value)
    {
        OnPropertyChanged(nameof(EditDirty));
        OnPropertyChanged(nameof(CanSaveEdit));
    }

    partial void OnEditProblemChanged(string value) => OnPropertyChanged(nameof(HasEditProblem));

    partial void OnBusyChanged(bool value) => OnPropertyChanged(nameof(CanSaveEdit));

    void BeginEdit()
    {
        editFrom = CurrentMarkdown;
        edit = NoteEdit.Begin(CurrentBody);
        EditText = edit.Text;
        OnPropertyChanged(nameof(EditHasDiagrams));
        OnPropertyChanged(nameof(EditDirty));
    }

    [RelayCommand]
    void Edit()
    {
        if (Editing || State != RewriteState.Idle) return;
        EditProblem = "";
        BeginEdit();
        Editing = true;
    }

    [RelayCommand]
    void CancelEdit()
    {
        Editing = false;
        EditProblem = "";
        edit = null;
        EditText = "";
    }

    /// <summary>Saves what's in the editor as the lecture's notes. With nothing typed, it just closes. When the library
    /// has other notes by now (their diagrams arrived, or another device changed them) nothing is saved: the editor
    /// says so and stays, and Save again is the student's choice to put theirs over them.</summary>
    [RelayCommand]
    async Task SaveEdit()
    {
        if (!Editing || edit is null || Busy || !CanSaveEdit) return;
        if (!EditDirty)
        {
            CancelEdit();
            return;
        }
        Busy = true;
        try
        {
            // The leading "Summary" heading the editor leaves out goes back as it was.
            string head = editFrom[..(editFrom.Length - AiWords.DropLeadingSummary(editFrom).Length)];
            var info = await library.EditNotesAsync(LectureId, head + edit.End(EditText), Notes.Fingerprint(editFrom));
            if (info is null)
            {
                OlderLibrary = true;
                EditProblem = AiWords.EditNeedsNewerLibrary;
                return;
            }
            CancelEdit();
            Apply(info);
            NotesChanged?.Invoke();
        }
        catch (LibraryRefusedException ex) when (ex.Status == 412)
        {
            EditProblem = await SeeWhatChanged() ? AiWords.EditNotesChanged : AiWords.EditOffline;
        }
        catch (LibraryRefusedException ex)
        {
            EditProblem = AiWords.EditRefused(ex.Status, ex.Message);
        }
        catch
        {
            Offline = true;
            EditProblem = AiWords.EditOffline;
        }
        finally
        {
            Busy = false;
        }
    }

    /// <summary>The notes as the library has them now become what the edit is based on, the typing untouched: so the
    /// next Save says so truthfully. False when the library couldn't be asked.</summary>
    async Task<bool> SeeWhatChanged()
    {
        try
        {
            if (await library.RewriteAsync(LectureId) is not { } info) return false;
            Apply(info);
            editFrom = CurrentMarkdown;
            return true;
        }
        catch
        {
            return false;
        }
    }

    [RelayCommand]
    void Dismiss()
    {
        if (State == RewriteState.Failed) { State = RewriteState.Idle; Error = ""; }
    }

    public void Dispose() => StopPolling();
}
