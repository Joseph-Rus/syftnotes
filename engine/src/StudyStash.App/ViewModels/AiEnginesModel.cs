using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StudyStash.App.Services;
using StudyStash.App.Services.Kit;
using StudyStash.App.Services.Stores;
using StudyStash.Core;
using StudyStash.Core.Ai;

namespace StudyStash.App.ViewModels;

/// <summary>An engine in a select or a menu: what's shown, picking it (each list makes its own <see cref="Pick"/>,
/// so a menu's item binds it straight without reaching back to the model that built the list), and whether it's the
/// one picked now (the menu's check).</summary>
public sealed partial class EngineChoice(string id, string name) : ObservableObject
{
    public string Id { get; } = id;
    public string Name { get; } = name;
    public IRelayCommand Pick { get; internal set; } = null!;
    [ObservableProperty] public partial bool Current { get; set; }

    /// <summary>Checks the choice whose id is <paramref name="id"/>, and only that one.</summary>
    public static void Mark(IEnumerable<EngineChoice> choices, string id)
    {
        foreach (var c in choices) c.Current = c.Id == id;
    }
}

/// <summary>One of an engine row's models, in its Options menu: picking it, and whether it's the one running now.</summary>
public sealed class AiModelChoice(string id, string label, bool current, IAsyncRelayCommand pick)
{
    public string Id { get; } = id;
    public string Label { get; } = label;
    public bool Current { get; } = current;
    public IAsyncRelayCommand Pick { get; } = pick;
}

/// <summary>One card of Settings → Your AI: Claude, ChatGPT, Gemini or Ollama, with its state (Not set up, Connected,
/// Needs attention, or where its Connect has got to), what's connected in the student's words, and one button: Connect,
/// or Fix when something's wrong (Ollama keeps its own Set up). The row owns its own commands, closed over its id, so a
/// view binds them straight from the row.</summary>
public sealed partial class AiEngineRow : ObservableObject
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Icon { get; init; } = "";
    public string About { get; init; } = "";
    public string State { get; init; } = "";
    public string StateWords { get; init; } = "";
    public bool Good { get; init; }
    public bool Warn { get; init; }
    public bool Muted { get; init; }
    public string ActionWords { get; init; } = "";
    public bool IsPrimary { get; init; }
    /// <summary>The row's button opens the Options menu (models, Check it works) instead of acting straight away.</summary>
    public bool NeedsOptions { get; init; }
    /// <summary>Where the AI's own sign-in is while one is going from this row (<see cref="SignInPhase"/>); "" otherwise.</summary>
    public string SignIn { get; init; } = "";
    /// <summary>The button's name for a screen reader: "Connect Claude", "Options for ChatGPT".</summary>
    public string ActionName => NeedsOptions ? $"{ActionWords} for {Name}" : $"{ActionWords} {Name}";
    /// <summary>The row's pill: not while it has no words (a sign-in that waits for the browser has no button to press).</summary>
    public bool ShowPill => !NeedsOptions && ActionWords.Length > 0;
    /// <summary>A small link beside a sign-in: "Open the page again" while it waits, or "Get help" once Syft Notes couldn't
    /// start it.</summary>
    public string LinkWords { get; init; } = "";
    public bool HasLink => LinkWords.Length > 0;
    public IAsyncRelayCommand LinkCommand { get; internal set; } = null!;
    public string Model { get; init; } = "";
    public List<ModelOption> Models { get; init; } = [];
    public List<AiModelChoice> ModelChoices { get; internal set; } = [];
    public bool HasModels => ModelChoices.Count > 0;
    public string Site { get; init; } = "";
    /// <summary>The first row in its group shows no separator above it.</summary>
    public bool First { get; internal set; }
    /// <summary>The one thing the student still has to do, said once and plainly ("Quit Claude fully and open it
    /// again."); "" when there's nothing.</summary>
    public string Next { get; init; } = "";
    public bool HasNext => Next.Length > 0;
    /// <summary>A Mac can quit the app and open it again for the student: the card offers to.</summary>
    public bool CanReopen { get; init; }
    public string ReopenWords { get; init; } = "";
    public IAsyncRelayCommand ReopenCommand { get; internal set; } = null!;
    /// <summary>"Connect again", in a connected card's Options menu: puts right anything that's wrong (safe to press).</summary>
    public IAsyncRelayCommand ConnectCommand { get; internal set; } = null!;
    /// <summary>Claude, ChatGPT and Gemini have Connect again in their Options menu; Ollama doesn't.</summary>
    public bool CanConnectAgain => Id != "ollama";

    /// <summary>The row's button: Set up / Sign in / Start / Download / Check, whichever this row's state needs.</summary>
    public IAsyncRelayCommand ActCommand { get; internal set; } = null!;
    /// <summary>"Check it works", from the Options menu (ready/limited/failed rows).</summary>
    public IAsyncRelayCommand CheckCommand { get; internal set; } = null!;
    /// <summary>A library on another computer: not-installed rows are in the "Add another AI" menu, and open the AI's site.</summary>
    public IRelayCommand AddCommand { get; internal set; } = null!;
}

/// <summary>The API key box's placeholder: "Paste a key", or "Paste a new key" over one that's saved.</summary>
public static class KeyWords
{
    public static readonly Avalonia.Data.Converters.IValueConverter Placeholder =
        new Avalonia.Data.Converters.FuncValueConverter<bool, string>(has => has ? "Paste a new key" : "Paste a key");
}

/// <summary>One engine's API key in Settings → AI engines: where to get one, the last four characters of the one saved,
/// and a box to paste a new one. The key itself is never read back from the library.</summary>
public sealed partial class ApiKeyRow : ObservableObject
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    /// <summary>Where the student makes a key: Anthropic's Console, OpenAI's platform, Google AI Studio.</summary>
    public string Where { get; init; } = "";
    public string WhereUrl { get; init; } = "";
    public bool First { get; init; }
    [ObservableProperty] public partial string Hint { get; set; } = "";
    [ObservableProperty] public partial string Draft { get; set; } = "";
    public bool HasKey => Hint.Length > 0;
    /// <summary>A line under the row (Gemini's: what Google's free tier does with what's sent, and who can have a key).</summary>
    public string Note { get; init; } = "";
    public bool HasNote => Note.Length > 0;
    public string Sub => HasKey ? $"Key {Hint} saved" : $"From {Where}";
    partial void OnHintChanged(string value)
    {
        OnPropertyChanged(nameof(HasKey));
        OnPropertyChanged(nameof(Sub));
    }
    public IAsyncRelayCommand SaveCommand { get; internal set; } = null!;
    public IAsyncRelayCommand RemoveCommand { get; internal set; } = null!;
    public IRelayCommand GetKeyCommand { get; internal set; } = null!;
}

/// <summary>
/// Settings → AI engines (design 13): who writes the notes and who answers questions, the rich notes switches (diagrams,
/// formula plots and drawings, and who designs them), Claude Code's speed, every engine's state and what to do about it,
/// and the Ollama fallback. Reads where the AIs stand from the one <see cref="AiStatusStore"/> (the problem cards above it
/// read the same one, so a sign-in started from either shows on both) and keeps only its rows; the view is just this.
/// </summary>
public sealed partial class AiEnginesModel : ObservableObject, IDisposable
{
    readonly AiStatusStore store;
    /// <summary>Made here for a model given only a library (a test, a picture): disposed with it.</summary>
    readonly bool ownsStore;
    bool loading;
    IAiLibrary library => store.Library;

    public AiEnginesModel(AiStatusStore store)
    {
        this.store = store;
        store.Changed += OnStoreChanged;
    }

    /// <summary>Over a store of its own (a test, a picture, a page with nothing else on it reading the AI).</summary>
    public AiEnginesModel(IAiLibrary library) : this(new AiStatusStore(library)) => ownsStore = true;

    string shown = "";
    /// <summary>Above zero while this model's own call to the store is going: it draws the answer itself, with what it
    /// says of it.</summary>
    int own;

    /// <summary>How often a sign-in that's going is looked at; a test sets it small.</summary>
    public TimeSpan SignInEvery
    {
        get => store.Every;
        set => store.Every = value;
    }

    /// <summary>The store learned something (the library's look at a sign-in, or the problem cards did something): the
    /// rows follow, and a row that waited for its sign-in says it's signed in.</summary>
    void OnStoreChanged()
    {
        if (own > 0 || store.Current is not { } now || store.Signature == shown) return;
        var waiting = Engines.Where(r => r.SignIn == SignInPhase.Waiting).ToList();
        Apply(now);
        foreach (var r in waiting)
            if (now.Engines.FirstOrDefault(e => e.Id == r.Id) is { } e && AiStatusStore.StateOf(e) != AiEngineState.NotSignedIn)
                Say = $"{r.Name} is signed in.";
    }

    async Task<T> Own<T>(Func<Task<T>> call)
    {
        own++;
        try
        {
            return await call();
        }
        finally
        {
            own--;
        }
    }

    /// <summary>An overview the library answered a change with: the store keeps it (the cards follow), and it's drawn here.</summary>
    AiOverview? Kept(AiOverview? overview)
    {
        if (overview is null) return null;
        own++;
        try
        {
            store.Take(overview);
        }
        finally
        {
            own--;
        }
        return overview;
    }

    /// <summary>What the page does when a call to the library didn't work: the library's refusal is said under the AIs, an
    /// older library says so instead of the AIs, and one not answering shows the pane's Offline words.</summary>
    void Failed(Failure failure)
    {
        switch (failure.Kind)
        {
            case FailureKind.Refused:
                Say = failure.Words;
                break;
            case FailureKind.OlderLibrary:
                OlderLibrary = true;
                break;
            default:
                Offline = true;
                break;
        }
    }

    /// <summary>The line under the pane's title: where the engines run (the library elsewhere, or this computer).</summary>
    public string Lede { get; init; } = "Choose which AI writes your notes and answers your questions. You can change it here any time. It runs on your library, and you can set it up there or from this laptop.";

    /// <summary>Whether the library is on this computer (one computer does everything): its rows say "this computer"
    /// rather than "your library".</summary>
    public bool Here { get; init; }

    /// <summary>The heading over the list of AIs.</summary>
    public string ListTitle => $"AIs on {AiWords.Where(Here)}";

    public ObservableCollection<AiEngineRow> Engines { get; } = [];
    /// <summary>API keys for Claude, ChatGPT and Gemini (shown once the library says it takes them).</summary>
    public ObservableCollection<ApiKeyRow> Keys { get; } = [];
    public bool HasKeys => Keys.Count > 0;
    /// <summary>Not-installed engines on a library on another computer: the "Add another AI" menu. (On this computer every
    /// AI is a row of its own.)</summary>
    public ObservableCollection<AiEngineRow> AddChoices { get; } = [];
    public List<EngineChoice> NotesChoices { get; private set; } = [];
    public List<EngineChoice> AskChoices { get; private set; } = [];
    /// <summary>Who draws the diagrams: Automatic, Same as notes, each engine. (Off is the Rich notes switch.)</summary>
    public List<EngineChoice> DiagramsChoices { get; private set; } = [];
    /// <summary>How fast Claude Code writes the notes and designs the rich notes: Standard, Fast mode, Quicker model.</summary>
    public List<EngineChoice> SpeedChoices { get; private set; } = [];

    [ObservableProperty] public partial string SelectedNotes { get; set; } = "";
    [ObservableProperty] public partial string SelectedAsk { get; set; } = "";
    /// <summary>The diagrams pick ("" from a library too old to have one: the row isn't shown).</summary>
    [ObservableProperty] public partial string SelectedDiagrams { get; set; } = "";
    /// <summary>The engine that pick comes to on the library right now ("" when nobody draws them).</summary>
    [ObservableProperty] public partial string DiagramsBy { get; set; } = "";
    /// <summary>The library has the Rich notes switches (a library too old to have them shows none of them).</summary>
    [ObservableProperty] public partial bool HasRich { get; set; }
    /// <summary>Rich notes: diagrams, formula plots and drawings are added after the notes; off, plain notes and nothing more
    /// is asked of the AI.</summary>
    [ObservableProperty] public partial bool RichOn { get; set; }
    [ObservableProperty] public partial bool RichDiagrams { get; set; }
    [ObservableProperty] public partial bool RichPlots { get; set; }
    [ObservableProperty] public partial bool RichDrawings { get; set; }
    /// <summary>Claude Code's speed ("" from a library too old to have it: the row isn't shown).</summary>
    [ObservableProperty] public partial string SelectedSpeed { get; set; } = "";
    /// <summary>Claude Code is installed on the library: its speed only matters then.</summary>
    [ObservableProperty] public partial bool HasClaude { get; set; }
    [ObservableProperty] public partial bool Fallback { get; set; }
    [ObservableProperty] public partial bool Busy { get; set; }
    [ObservableProperty] public partial string? Say { get; set; }

    /// <summary>What the page says of the last thing done on it ("Claude works with your API key.", "The key was
    /// saved, but Claude said: …", a refusal), where the student is looking: under API keys when it was a key, else
    /// under the AIs. Neither view showed it, so a key the AI's maker refused looked saved and fine.</summary>
    bool aboutKey;
    public string? SayOfKeys => aboutKey ? Say : null;
    public string? SayOfAis => aboutKey ? null : Say;
    public bool HasSayOfKeys => SayOfKeys is { Length: > 0 };
    public bool HasSayOfAis => SayOfAis is { Length: > 0 };

    partial void OnSayChanged(string? value)
    {
        OnPropertyChanged(nameof(SayOfKeys));
        OnPropertyChanged(nameof(SayOfAis));
        OnPropertyChanged(nameof(HasSayOfKeys));
        OnPropertyChanged(nameof(HasSayOfAis));
    }

    // Each thing done on the page starts by being busy: what's said next is about that one, not the key before it.
    partial void OnBusyChanged(bool value)
    {
        if (value) aboutKey = false;
    }
    [ObservableProperty] public partial bool OlderLibrary { get; set; }
    [ObservableProperty] public partial bool Offline { get; set; }

    public string SelectedNotesName => SelectedNotes.Length == 0 ? AiWords.ChooseAnAi : NotesChoices.FirstOrDefault(c => c.Id == SelectedNotes)?.Name ?? SelectedNotes;
    public string SelectedAskName => SelectedAsk.Length == 0 ? AiWords.ChooseAnAi : AskChoices.FirstOrDefault(c => c.Id == SelectedAsk)?.Name ?? SelectedAsk;
    /// <summary>Nobody writes the notes yet (a fresh library, nothing chosen): the row says so and offers the choice, rather
    /// than naming an AI that may not even be here.</summary>
    public bool NoNotesAi => last is not null && SelectedNotes.Length == 0;
    /// <summary>The notes row's line: where the notes are written, or that no AI writes them yet.</summary>
    public string NotesRowAbout => NoNotesAi ? AiWords.NoNotesAi : "After each lecture, on your library";
    public string SelectedDiagramsName => DiagramsChoices.FirstOrDefault(c => c.Id == SelectedDiagrams)?.Name ?? SelectedDiagrams;
    /// <summary>The diagrams row's line: who reads each transcript to draw them, or that nobody does.</summary>
    public string DiagramsAbout => AiWords.DiagramsAbout(SelectedDiagrams, DiagramsBy);
    public bool HasDiagrams => SelectedDiagrams.Length > 0;
    /// <summary>The "Drawn by" row: while rich notes are on.</summary>
    public bool ShowDrawnBy => HasDiagrams && HasRich && RichOn;
    /// <summary>The Rich notes group's per-kind switches: shown while rich notes are on.</summary>
    public bool ShowKinds => HasRich && RichOn;
    public string RichAbout => AiWords.RichAbout(RichOn);
    public bool HasSpeed => SelectedSpeed.Length > 0 && HasClaude;
    public string SelectedSpeedName => SpeedChoices.FirstOrDefault(c => c.Id == SelectedSpeed)?.Name ?? SelectedSpeed;
    public string SpeedAbout => AiWords.SpeedAbout(SelectedSpeed);
    public bool HasAddChoices => AddChoices.Count > 0;
    public string OlderLibraryWords => AiWords.OlderLibraryWords;

    /// <summary>Opens a link (the app hands this to the OS browser); null in tests.</summary>
    public Action<string>? OpenUrl { get; set; }

    /// <summary>Installs an AI's own helper on this computer with its maker's installer (<see cref="AgentInstall"/>), saying
    /// where it's got to: Set up on a row. Null where the library is on another computer (it's installed there).</summary>
    public Func<string, Action<string>, Task<InstallOutcome>>? Install
    {
        get => install;
        set
        {
            install = value;
            connector = null;
        }
    }
    Func<string, Action<string>, Task<InstallOutcome>>? install;

    /// <summary>The AI apps on this computer (Claude Desktop, Claude Code, the ChatGPT app with Codex, Gemini CLI,
    /// Antigravity), which Connect adds Syft Notes to; null where it can't (a test, a shot).</summary>
    public ClaudeSetup? AiApps
    {
        get => aiApps;
        set
        {
            aiApps = value;
            connector = null;
        }
    }
    ClaudeSetup? aiApps;

    AiConnect? connector;
    /// <summary>The one Connect per AI that the cards here and setup's AI step both call (<see cref="AiConnect"/>).</summary>
    public AiConnect Connector
    {
        get => connector ??= new AiConnect(library) { Apps = AiApps, Install = Install, WaitForSignIn = false };
        set => connector = value;
    }

    /// <summary>The AIs being connected from a card now, and where each has got to ("Installing Claude…").</summary>
    readonly Dictionary<string, string> settingUp = [];
    /// <summary>How each AI's last Connect here ended, while it says something the card should show (why it stopped).</summary>
    readonly Dictionary<string, ConnectOutcome> outcomes = [];
    /// <summary>AIs whose install stopped for a reason Syft Notes can't put right: their row offers Get help.</summary>
    readonly HashSet<string> installStopped = [];
    AiOverview? last;
    /// <summary>The AI apps on this computer, as their own settings had them when last looked at.</summary>
    IReadOnlyList<AiAppState> apps = [];

    /// <summary>The Advanced part of the page (Claude's speed, the fallback, API keys, what AI apps may read, the web
    /// address, opening a class in a terminal): folded away until asked for.</summary>
    [ObservableProperty] public partial bool AdvancedOpen { get; set; }
    public string AdvancedWords => AdvancedOpen ? "Hide advanced settings" : "Advanced settings";
    partial void OnAdvancedOpenChanged(bool value) => OnPropertyChanged(nameof(AdvancedWords));
    [RelayCommand] void ToggleAdvanced() => AdvancedOpen = !AdvancedOpen;

    /// <summary>How often the apps are looked at again while one still has to be opened again to load Syft Notes.</summary>
    public TimeSpan AppsEvery { get; set; } = TimeSpan.FromSeconds(3);

    partial void OnSelectedNotesChanged(string value)
    {
        EngineChoice.Mark(NotesChoices, value);
        OnPropertyChanged(nameof(SelectedNotesName));
        OnPropertyChanged(nameof(NoNotesAi));
        OnPropertyChanged(nameof(NotesRowAbout));
        if (!loading) _ = PostDefaultsAsync(notes: value);
    }

    partial void OnSelectedAskChanged(string value)
    {
        EngineChoice.Mark(AskChoices, value);
        OnPropertyChanged(nameof(SelectedAskName));
        if (!loading) _ = PostDefaultsAsync(ask: value);
    }

    partial void OnSelectedDiagramsChanged(string value)
    {
        EngineChoice.Mark(DiagramsChoices, value);
        OnPropertyChanged(nameof(SelectedDiagramsName));
        OnPropertyChanged(nameof(DiagramsAbout));
        OnPropertyChanged(nameof(HasDiagrams));
        OnPropertyChanged(nameof(ShowDrawnBy));
        if (!loading && value.Length > 0) _ = PostDefaultsAsync(diagrams: value);
    }

    partial void OnHasRichChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowDrawnBy));
        OnPropertyChanged(nameof(ShowKinds));
    }

    partial void OnRichOnChanged(bool value)
    {
        OnPropertyChanged(nameof(RichAbout));
        OnPropertyChanged(nameof(ShowDrawnBy));
        OnPropertyChanged(nameof(ShowKinds));
        if (!loading) _ = PostRichAsync(on: value);
    }

    partial void OnRichDiagramsChanged(bool value)
    {
        if (!loading) _ = PostRichAsync(diagrams: value);
    }

    partial void OnRichPlotsChanged(bool value)
    {
        if (!loading) _ = PostRichAsync(plots: value);
    }

    partial void OnRichDrawingsChanged(bool value)
    {
        if (!loading) _ = PostRichAsync(drawings: value);
    }

    partial void OnSelectedSpeedChanged(string value)
    {
        EngineChoice.Mark(SpeedChoices, value);
        OnPropertyChanged(nameof(SelectedSpeedName));
        OnPropertyChanged(nameof(SpeedAbout));
        OnPropertyChanged(nameof(HasSpeed));
        if (!loading && value.Length > 0) _ = PostRichAsync(speed: value);
    }

    partial void OnHasClaudeChanged(bool value) => OnPropertyChanged(nameof(HasSpeed));

    partial void OnDiagramsByChanged(string value) => OnPropertyChanged(nameof(DiagramsAbout));

    partial void OnFallbackChanged(bool value)
    {
        if (!loading) _ = PostDefaultsAsync(fallback: value);
    }

    [RelayCommand]
    public Task Refresh() => Load();

    /// <summary>Reads the library's AI, and the AI apps on this computer, once and fills every row: called when the pane opens.</summary>
    public async Task Load()
    {
        await LoadAppsAsync();
        var asked = await Own(() => store.RefreshAsync());
        if (asked.Value is { } overview) Apply(overview);
        else if (asked.Failure!.Kind == FailureKind.OlderLibrary) Apply(null);
        else Offline = true;
    }

    void Apply(AiOverview? overview)
    {
        loading = true;
        try
        {
            if (overview is null)
            {
                OlderLibrary = true;
                Offline = false;
                return;
            }
            OlderLibrary = false;
            Offline = false;
            last = overview;
            NotesChoices = [.. AiWords.InShownOrder(overview.Engines).Select(e => new EngineChoice(e.Id, AiWords.PlainName(e.Id, e.Name)))];
            foreach (var c in NotesChoices) c.Pick = new RelayCommand(() => SelectedNotes = c.Id);
            AskChoices = [.. AiWords.InShownOrder(overview.Engines).Select(e => new EngineChoice(e.Id, AiWords.PlainName(e.Id, e.Name)))];
            foreach (var c in AskChoices) c.Pick = new RelayCommand(() => SelectedAsk = c.Id);
            DiagramsChoices = [.. DiagramEngines.Picks.Select(id => new EngineChoice(id, AiWords.DiagramsChoiceName(id)))];
            foreach (var c in DiagramsChoices) c.Pick = new RelayCommand(() => SelectedDiagrams = c.Id);
            SpeedChoices = [.. AiSpeed.Choices.Select(id => new EngineChoice(id, AiWords.SpeedChoiceName(id)))];
            foreach (var c in SpeedChoices) c.Pick = new RelayCommand(() => SelectedSpeed = c.Id);
            OnPropertyChanged(nameof(NotesChoices));
            OnPropertyChanged(nameof(AskChoices));
            OnPropertyChanged(nameof(DiagramsChoices));
            OnPropertyChanged(nameof(SpeedChoices));
            SelectedNotes = overview.Notes;
            SelectedAsk = overview.Ask;
            DiagramsBy = overview.DiagramsBy;
            SelectedDiagrams = overview.Diagrams == DiagramEngines.Off ? DiagramEngines.Auto : overview.Diagrams; // off is the switch's now
            HasRich = overview.Rich is not null;
            RichOn = overview.Rich?.On ?? false;
            RichDiagrams = overview.Rich?.Diagrams ?? true;
            RichPlots = overview.Rich?.Plots ?? true;
            RichDrawings = overview.Rich?.Drawings ?? true;
            HasClaude = overview.Engines.Any(e => e.Id == "claude" && e.Installed);
            SelectedSpeed = overview.Speed;
            EngineChoice.Mark(NotesChoices, SelectedNotes);
            EngineChoice.Mark(AskChoices, SelectedAsk);
            EngineChoice.Mark(DiagramsChoices, SelectedDiagrams);
            EngineChoice.Mark(SpeedChoices, SelectedSpeed);
            OnPropertyChanged(nameof(SelectedDiagramsName));
            OnPropertyChanged(nameof(SelectedSpeedName));
            Fallback = overview.Fallback;
            OnPropertyChanged(nameof(NoNotesAi));
            OnPropertyChanged(nameof(NotesRowAbout));
            OnPropertyChanged(nameof(SelectedAskName));
            // A sign-in the press said was going that has stopped since (or never started): the line says so, as its row does.
            if (Say?.StartsWith("Finish signing in to ", StringComparison.Ordinal) == true
                && overview.Engines.FirstOrDefault(e => SignInPhase.Stopped(AiStatusStore.SignInOf(e))) is { } stopped)
                Say = AiWords.SignInSaid(AiWords.PlainName(stopped.Id, stopped.Name), stopped.SignIn);
            // Or it worked (learned here, or from the problem card on the same page): the line says so, not "Finish…".
            else if (Say is { } said && overview.Engines.FirstOrDefault(e => said == AiSignIns.StartedWords(e.Id)) is { } done
                     && AiStatusStore.StateOf(done) != AiEngineState.NotSignedIn)
                Say = $"{AiWords.PlainName(done.Id, done.Name)} is signed in.";

            Engines.Clear();
            AddChoices.Clear();
            // Every AI, always, in the order a student meets them: Claude, ChatGPT, Gemini, then Ollama (advanced). One
            // not on this computer yet has Set up. (A library on another computer keeps those in its Add menu.)
            foreach (var e in AiWords.InShownOrder(overview.Engines))
            {
                var row = BuildRow(e);
                if (Here || e.Id == "ollama" || e.Installed) { row.First = Engines.Count == 0; Engines.Add(row); }
                else AddChoices.Add(row);
            }
            OnPropertyChanged(nameof(HasAddChoices));

            Keys.Clear();
            foreach (var (id, name, where, url) in KeyPlaces)
                if (overview.Engines.FirstOrDefault(e => e.Id == id) is { } e)
                {
                    var k = new ApiKeyRow
                    {
                        Id = id, Name = name, Where = where, WhereUrl = url, Hint = e.KeyHint, First = Keys.Count == 0,
                        Note = id == "gemini" ? string.Join("\n", ApiKeys.GeminiKeyNotes("computer")) : "",
                    };
                    k.SaveCommand = new AsyncRelayCommand(() => SetKeyAsync(k, k.Draft));
                    k.RemoveCommand = new AsyncRelayCommand(() => SetKeyAsync(k, ""));
                    k.GetKeyCommand = new RelayCommand(() => OpenUrl?.Invoke(k.WhereUrl));
                    Keys.Add(k);
                }
            OnPropertyChanged(nameof(HasKeys));
            shown = AiStatusStore.SignatureOf(overview);
            // An app still to be opened again: looked at until it has loaded Syft Notes.
            if (appWatch is null && Engines.Any(r => r.HasNext)) WatchApps();
        }
        finally
        {
            loading = false;
        }
    }

    /// <summary>For a picture: the AI apps as <paramref name="states"/> say (on Windows' words when <paramref name="windows"/>),
    /// without looking at any file.</summary>
    public void UseApps(IReadOnlyList<AiAppState> states, bool windows = false)
    {
        apps = states;
        appsOnWindows = windows;
        Redraw();
    }
    bool? appsOnWindows;

    /// <summary>Reads the AI apps' own settings again (off the UI thread: Claude Code's file can be large).</summary>
    async Task LoadAppsAsync()
    {
        if (AiApps is not { } setup) return;
        try
        {
            apps = await Task.Run(setup.States);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }

    static readonly (string Id, string Name, string Where, string Url)[] KeyPlaces =
    [
        ("claude", "Claude", "the Anthropic Console", "https://console.anthropic.com/settings/keys"),
        ("codex", "ChatGPT", "OpenAI's platform", "https://platform.openai.com/api-keys"),
        ("gemini", "Gemini", "Google AI Studio", "https://aistudio.google.com/apikey"),
    ];

    async Task SetKeyAsync(ApiKeyRow row, string key)
    {
        if (key.Trim().Length == 0 && !row.HasKey) return;
        Busy = true;
        aboutKey = true;
        Say = key.Trim().Length > 0 ? $"Trying {row.Name} with your key…" : null;
        try
        {
            row.Draft = "";
            await AfterAction(await LibraryCall.Run(() => library.KeyAsync(row.Id, key.Trim())));
        }
        finally
        {
            Busy = false;
        }
    }

    AiEngineRow BuildRow(EngineInfo e) => e.Id == "ollama" ? BuildOllamaRow(e) : BuildCard(e);

    /// <summary>
    /// Claude's, ChatGPT's or Gemini's card: where it stands, in one of a few states, and the one button that moves it on.
    /// <list type="bullet">
    /// <item>Connecting: where its Connect has got to ("Installing Claude…", "Signing in to Claude… finish in your
    /// browser"), with no button (and "Open the page again" while the browser waits);</item>
    /// <item>Not set up: what it's for, and Connect;</item>
    /// <item>Needs attention: what's wrong in a sentence (signed out, an app not reading Syft Notes, an install or a sign-in
    /// that stopped), and Fix (Try again for a sign-in that stopped), which runs the same Connect;</item>
    /// <item>Connected: what it does and the apps it's in ("Writes your notes · Claude app · Claude Code"), with its Options
    /// menu, and the one thing left to do when an app still has to be opened again ("Almost done").</item>
    /// </list>
    /// </summary>
    AiEngineRow BuildCard(EngineInfo e)
    {
        string name = AiWords.PlainName(e.Id, e.Name);
        var status = store.StatusOf(e);
        string signIn = status.SignIn;
        string? going = settingUp.GetValueOrDefault(e.Id);
        var mine = apps.Where(a => a.Provider == e.Id).ToList();
        var broken = mine.FirstOrDefault(a => a is { Installed: true, Added: true, Good: false } || a.Stale);
        var said = outcomes.GetValueOrDefault(e.Id);
        bool connected = e.Installed && status.State is AiEngineState.Ready or AiEngineState.Limited;
        string next = connected && broken is null ? NextFor(mine) : "";
        var reopen = next.Length > 0 ? mine.FirstOrDefault(a => a is { Good: true, Running: true, Loaded: false, CanReopen: true }) : null;

        string state, about, action = "", link = "";
        bool good = false, warn = false, primary = false, options = false;
        if (going is not null || signIn == SignInPhase.Waiting)
        {
            state = signIn == SignInPhase.Waiting ? "Signing in" : "Connecting";
            about = going ?? AiWords.SignInWaiting;
            if (signIn == SignInPhase.Waiting) link = AiWords.SignInLink(signIn);
        }
        else if (said is { Ok: false } || signIn.Length > 0 || status.State is AiEngineState.NotSignedIn or AiEngineState.Failed || broken is not null && e.Installed
                 || !e.Installed && installStopped.Contains(e.Id))
        {
            state = "Needs attention";
            warn = true;
            primary = true;
            about = signIn.Length > 0 ? AiWords.SignInAbout(name, signIn)
                : said is { Ok: false } ? said.Words
                : status.State == AiEngineState.NotSignedIn ? $"Signed out of {name}. Fix signs you in again."
                : status.State == AiEngineState.Failed ? $"{name} didn't work last time. Fix checks it and signs you in if it needs to."
                : broken is not null ? $"{broken.Words} isn't reading your lectures yet. Fix connects it."
                : AiWords.ConnectAbout(e.Id);
            action = SignInPhase.TryAgain(signIn) || SignInPhase.TryAgain(said?.SignIn) ? "Try again"
                : signIn == SignInPhase.Region ? "" : "Fix";
            if (signIn == SignInPhase.Stuck || said?.SignIn == SignInPhase.Stuck || installStopped.Contains(e.Id)) link = "Get help";
        }
        else if (!e.Installed)
        {
            state = "Not set up";
            about = AiWords.ConnectAbout(e.Id);
            action = "Connect";
            primary = true;
        }
        else if (status.State == AiEngineState.Unchecked)
        {
            // Here, but nobody knows yet whether it's signed in (a tool installed before Syft Notes, say).
            state = "Not checked yet";
            about = "Connect checks it works, signs you in if it needs to, and adds your lectures to its apps.";
            action = "Connect";
            primary = true;
        }
        else
        {
            bool limited = status.State == AiEngineState.Limited;
            state = limited ? status.Words : next.Length > 0 ? "Almost done" : "Connected";
            good = !limited && next.Length == 0;
            warn = limited;
            about = ConnectedWords(e, mine);
            options = true;
            action = "Options";
        }
        var row = new AiEngineRow
        {
            Id = e.Id,
            Name = name,
            Icon = AiWords.EngineIcon(e.Id),
            About = about,
            State = e.State,
            StateWords = state,
            Good = good,
            Warn = warn,
            Muted = !good && !warn,
            SignIn = signIn,
            LinkWords = link,
            ActionWords = action,
            IsPrimary = primary,
            NeedsOptions = options,
            Next = next,
            CanReopen = reopen is not null,
            ReopenWords = reopen is not null ? $"Reopen {ClaudeSetup.DesktopApp(reopen.Id)?.Name}" : "",
            Model = e.Model,
            Models = e.Models,
            Site = e.Site,
        };
        Wire(row, e);
        if (reopen is not null) row.ReopenCommand = new AsyncRelayCommand(() => ReopenAsync(reopen.Id));
        return row;
    }

    /// <summary>What a connected AI does and the apps it's in, in the student's words: "Writes your notes · Claude app ·
    /// Claude Code".</summary>
    string ConnectedWords(EngineInfo e, List<AiAppState> mine)
    {
        var parts = new List<string>();
        if (SelectedNotes == e.Id) parts.Add("Writes your notes");
        else if (SelectedAsk == e.Id) parts.Add("Answers your questions");
        parts.AddRange(mine.Where(a => a.Good).Select(a => a.Words));
        return parts.Count > 0 ? string.Join(" · ", parts) : $"Signed in, on {AiWords.Where(Here)}.";
    }

    /// <summary>The one thing left: an app of this AI that is open and hasn't loaded Syft Notes since it was added has to
    /// be quit and opened again (it reads its settings only when it starts).</summary>
    string NextFor(List<AiAppState> mine) =>
        mine.FirstOrDefault(a => a is { Good: true, Running: true, Loaded: false } && ClaudeSetup.DesktopApp(a.Id) is not null) is { } app
            ? AiConnect.QuitWords(ClaudeSetup.DesktopApp(app.Id)!.Value.Name, appsOnWindows ?? AiApps?.Windows ?? OperatingSystem.IsWindows())
            : "";

    void Wire(AiEngineRow row, EngineInfo e)
    {
        row.ActCommand = new AsyncRelayCommand(() => RunRowActionAsync(row));
        row.CheckCommand = new AsyncRelayCommand(() => CheckAsync(row.Id));
        row.LinkCommand = new AsyncRelayCommand(() => SignInLinkAsync(row));
        row.ConnectCommand = new AsyncRelayCommand(() => ConnectAsync(row.Id));
        row.ReopenCommand = new AsyncRelayCommand(() => Task.CompletedTask);
        row.ModelChoices = [.. e.Models.Select(m => new AiModelChoice(m.Id, m.Label, m.Id == e.Model, new AsyncRelayCommand(() => PickModelAsync(row.Id, m.Id))))];
        row.AddCommand = new RelayCommand(() =>
        {
            if (row.Site.Length > 0) OpenUrl?.Invoke(row.Site);
            Say = Here ? $"Install {row.Name} on this computer." : $"Install {row.Name} on your library's computer.";
        });
    }

    /// <summary>Ollama's row (the free AI, advanced): its own Set up, Start, Download and Check, as before.</summary>
    AiEngineRow BuildOllamaRow(EngineInfo e)
    {
        // A sign-in going from here (the AI's own, hidden, in the student's browser): the row says where it is, and turns
        // ready by itself when it's done.
        var status = store.StatusOf(e);
        string signIn = status.SignIn;
        string name = status.Brand;
        bool cli = e.Id != "ollama";
        string? phase = settingUp.GetValueOrDefault(e.Id);
        var row = new AiEngineRow
        {
            Id = e.Id,
            Name = name,
            Icon = AiWords.EngineIcon(e.Id),
            About = phase ?? (signIn.Length > 0 ? AiWords.SignInAbout(name, signIn)
                : e.Installed || !cli ? AiWords.EngineAbout(e.Id, e.State, Here) : Here ? AiWords.NotSetUpAbout(e.Id) : "Install it on your library's computer."),
            State = e.State,
            StateWords = phase is not null ? "Setting up" : signIn == SignInPhase.Waiting ? "Signing in" : AiWords.EngineStateWords(e.State),
            Good = AiWords.StateIsGood(e.State),
            Warn = AiWords.StateIsWarn(e.State) && signIn != SignInPhase.Waiting && phase is null,
            Muted = AiWords.StateIsMuted(e.State) || signIn == SignInPhase.Waiting || phase is not null,
            SignIn = signIn,
            LinkWords = signIn.Length > 0 ? AiWords.SignInLink(signIn) : !e.Installed && installStopped.Contains(e.Id) ? "Get help" : "",
            ActionWords = phase is not null ? ""
                : signIn.Length > 0 ? AiWords.SignInButton(signIn)
                : !e.Installed && cli && !Here ? "Get it"
                : status.Action == AiAction.Check && status.State == AiEngineState.NotSignedIn ? "Check" : AiWords.RowActionWords(e.State),
            IsPrimary = AiWords.RowActionPrimary(e.State),
            NeedsOptions = AiWords.NeedsOptions(e.State),
            Model = e.Model,
            Models = e.Models,
            Site = e.Site,
        };
        Wire(row, e);
        return row;
    }

    async Task RunRowActionAsync(AiEngineRow row)
    {
        // Claude, ChatGPT and Gemini: one Connect, whatever is needed.
        if (row.Id != "ollama")
        {
            await ConnectAsync(row.Id);
            return;
        }
        Busy = true;
        try
        {
            // Not set up · signed out (Sign in, or Check once a Sign in that isn't hidden was pressed) · not running ·
            // its model missing · not checked yet: the library's own action for it. (Anything else asks nothing.)
            var action = store.NextAction(row.Id, AiStatusStore.StateOf(row.State));
            var said = action == AiAction.None ? Outcome<AiSaid>.Failed(Failure.OlderLibrary) : await Own(() => store.ActAsync(row.Id, action));
            await AfterAction(said);
            // Sign in from a laptop says where to sign in: the row's button is Check for when that's done.
            if (said.Failure?.Kind == FailureKind.Refused && store.ChecksNext(row.Id)) await Load();
        }
        finally
        {
            Busy = false;
        }
    }

    /// <summary>
    /// Connect on Claude's, ChatGPT's or Gemini's card (and setup's AI step): installs it when it isn't here, adds Syft
    /// Notes to every app of it on this computer, signs in hidden in the student's browser, and makes it the AI that
    /// writes the notes when none is chosen (<see cref="AiConnect"/>), the card saying where it's got to all the way.
    /// Safe to press again: it puts right whatever is wrong. On a library on another computer, an AI not installed there
    /// opens its site, as before.
    /// </summary>
    public async Task<ConnectOutcome> ConnectAsync(string id)
    {
        if (!Here && last?.Engines.FirstOrDefault(e => e.Id == id) is { Installed: false } away)
        {
            if (away.Site.Length > 0) OpenUrl?.Invoke(away.Site);
            Say = $"Install {AiWords.PlainName(away.Id, away.Name)} on your library's computer.";
            return new ConnectOutcome(false, Say);
        }
        if (settingUp.ContainsKey(id)) return new ConnectOutcome(false, "");
        outcomes.Remove(id);
        installStopped.Remove(id);
        Say = null;
        settingUp[id] = $"Connecting {AiConnect.Brand(id)}…";
        Redraw();
        var ui = SynchronizationContext.Current;
        ConnectOutcome outcome;
        try
        {
            // Said on the UI thread (most steps are), it shows at once; from an installer's own thread, it's posted there.
            outcome = await Own(() => store.ConnectAsync(Connector, id, step => (ui is null || SynchronizationContext.Current == ui ? (Action<Action>)(a => a()) : a => ui.Post(_ => a(), null))(() =>
            {
                if (!settingUp.ContainsKey(id)) return;
                settingUp[id] = step.Words;
                Redraw();
            })));
        }
        finally
        {
            settingUp.Remove(id);
        }
        if (!outcome.Ok) outcomes[id] = outcome;
        if (outcome.Install is InstallFailure.Other or InstallFailure.WontStart or InstallFailure.TimedOut) installStopped.Add(id);
        await LoadAppsAsync();
        if (outcome.Overview is { } o) Apply(o);
        else await Load();
        // A card that stopped says why itself; one with a step left says that.
        Say = outcome.Ok && outcome.Next.Length == 0 ? outcome.Words : null;
        return outcome;
    }

    /// <summary>Quits an app and opens it again (a Mac), so it loads Syft Notes; then looks until it has.</summary>
    async Task ReopenAsync(string appId)
    {
        if (AiApps is not { } setup) return;
        var done = await setup.ReopenAsync(appId);
        if (!done.Ok) Say = done.Say;
        await LoadAppsAsync();
        Redraw();
        WatchApps();
    }

    CancellationTokenSource? appWatch;

    /// <summary>Stops looking at the apps again (Settings closed, or a newer look took its place).</summary>
    public void StopWatchingApps()
    {
        appWatch?.Cancel();
        appWatch = null;
    }

    /// <summary>While an app still has to be opened again to load Syft Notes, looks at the apps every few seconds for
    /// five minutes, so the card turns to Connected by itself the moment it has.</summary>
    void WatchApps()
    {
        StopWatchingApps();
        var cts = new CancellationTokenSource();
        appWatch = cts;
        _ = LoopAsync();

        async Task LoopAsync()
        {
            bool first = true;
            try
            {
                await Poll.UntilAsync(async _ =>
                {
                    // Looked at after the first wait, as before: the apps were read just now.
                    if (first)
                    {
                        first = false;
                        return false;
                    }
                    string before = AppsSignature();
                    await LoadAppsAsync();
                    // Drawn again only when something changed: a menu open on a card stays open meanwhile.
                    if (AppsSignature() != before) Redraw();
                    return !Engines.Any(r => r.HasNext);
                }, AppsEvery, AppsEvery * 100, cts.Token);
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                if (appWatch == cts) appWatch = null;
            }
        }
    }

    /// <summary>What the cards show of the apps, to tell when that's changed.</summary>
    string AppsSignature() => string.Join("|", apps.Select(a => $"{a.Id}:{a.Good}:{a.Running}:{a.Loaded}:{a.Stale}"));

    /// <summary>The rows drawn again from what the library last said (a set-up row's words moved on).</summary>
    void Redraw()
    {
        if (last is { } o) Apply(o);
    }

    /// <summary>The small link on a row's sign-in: the page again (it opens once more, or the sign-in runs once more and
    /// opens it), or Syft Notes' help page once it couldn't start (or its install stopped).</summary>
    async Task SignInLinkAsync(AiEngineRow row)
    {
        if (row.SignIn == SignInPhase.Stuck || row.LinkWords == "Get help")
        {
            OpenUrl?.Invoke(AiWords.HelpPage);
            return;
        }
        Busy = true;
        try
        {
            await AfterAction(await Own(() => store.ActAsync(row.Id, AiAction.SignInAgain)));
        }
        finally
        {
            Busy = false;
        }
    }

    async Task CheckAsync(string id)
    {
        Busy = true;
        try
        {
            await AfterAction(await Own(() => store.ActAsync(id, AiAction.Check)));
        }
        finally
        {
            Busy = false;
        }
    }

    async Task PickModelAsync(string id, string model)
    {
        Busy = true;
        try
        {
            var asked = await LibraryCall.Run(() => library.ModelAsync(id, model));
            if (asked.Failure is { Kind: not FailureKind.OlderLibrary } failure) Failed(failure);
            else Apply(Kept(asked.Value));
        }
        finally
        {
            Busy = false;
        }
    }

    async Task AfterAction(Outcome<AiSaid> asked)
    {
        if (asked.Value is not { } said)
        {
            Failed(asked.Failure!);
            return;
        }
        Say = said.Said;
        if (said.Overview is not null) Apply(Kept(said.Overview));
        else await Load();
    }

    async Task PostDefaultsAsync(string? notes = null, string? ask = null, bool? fallback = null, string? diagrams = null)
    {
        Busy = true;
        try
        {
            var asked = await LibraryCall.Run(() => library.DefaultsAsync(notes, ask, fallback, diagrams));
            if (asked.Failure is { Kind: not FailureKind.OlderLibrary } failure) Failed(failure);
            else Apply(Kept(asked.Value));
        }
        finally
        {
            Busy = false;
        }
    }

    /// <summary>A rich notes switch or Claude Code's speed changed: the library keeps it and says what it all comes to (turning
    /// off the last kind, say, switches rich notes off).</summary>
    async Task PostRichAsync(bool? on = null, bool? diagrams = null, bool? plots = null, bool? drawings = null, string? speed = null)
    {
        Busy = true;
        try
        {
            var asked = await LibraryCall.Run(() => library.RichAsync(on, diagrams, plots, drawings, speed));
            if (asked.Failure is { Kind: not FailureKind.OlderLibrary } failure) Failed(failure);
            else Apply(Kept(asked.Value));
        }
        finally
        {
            Busy = false;
        }
    }

    public void Dispose()
    {
        store.Changed -= OnStoreChanged;
        StopWatchingApps();
        if (ownsStore) store.Dispose();
    }
}
