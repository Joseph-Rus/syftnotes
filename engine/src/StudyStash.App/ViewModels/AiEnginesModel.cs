using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StudyStash.App.Services;
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

/// <summary>One row of the AI engines pane: Claude, ChatGPT, Gemini or Ollama, with its state and what its button does
/// (Set up, for one not on this computer yet). The row owns its own commands, closed over its id, so a view binds them
/// straight from the row.</summary>
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
/// and the Ollama fallback. Reads and drives one library's AI (`IAiLibrary`); the view is just this.
/// </summary>
public sealed partial class AiEnginesModel : ObservableObject
{
    readonly IAiLibrary library;
    bool loading;

    public AiEnginesModel(IAiLibrary library)
    {
        this.library = library;
        watch = new SignInWatch(library, SignInMovedAsync);
    }

    /// <summary>Keeps asking the library while a sign-in is going, so its row turns ready by itself (also when the
    /// student left this page and came back to a sign-in still waiting: <see cref="Apply"/> starts it again).</summary>
    readonly SignInWatch watch;
    string shown = "";

    /// <summary>Said whenever this pane learns the AIs' state from the library (a sign-in's progress among it), so the
    /// problem cards on the same page show it too; <see cref="Adopt"/> is the other direction.</summary>
    public event Action<AiOverview>? Heard;
    bool adopting;

    /// <summary>Settings' page shows both this pane and the problem cards: each shows what the other learned.</summary>
    public void ShareWith(AiProblemsModel cards)
    {
        Heard += cards.Adopt;
        cards.Heard += Adopt;
    }

    /// <summary>Takes the state the problem cards learned, so a sign-in started from a card shows here at once.</summary>
    public void Adopt(AiOverview overview)
    {
        if (SignInWatch.Signature(overview) == shown) return;
        adopting = true;
        try
        {
            Apply(overview);
        }
        finally
        {
            adopting = false;
        }
    }

    /// <summary>How often a sign-in that's going is looked at; a test sets it small.</summary>
    public TimeSpan SignInEvery
    {
        get => watch.Every;
        set => watch.Every = value;
    }

    Task SignInMovedAsync(AiOverview now)
    {
        if (SignInWatch.Signature(now) == shown) return Task.CompletedTask;
        var waiting = Engines.Where(r => r.SignIn == SignInPhase.Waiting).ToList();
        Apply(now);
        foreach (var r in waiting)
            if (now.Engines.FirstOrDefault(e => e.Id == r.Id) is { State: not "not_signed_in" })
                Say = $"{r.Name} is signed in.";
        return Task.CompletedTask;
    }

    /// <summary>AIs whose Sign in was pressed: their row's button is Check until it's pressed, since nothing else here
    /// learns the student has signed in (the library still remembers the AI turning them away). Check that finds it
    /// still signed out gives Sign in back.</summary>
    readonly HashSet<string> signingIn = [];

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

    public string SelectedNotesName => NotesChoices.FirstOrDefault(c => c.Id == SelectedNotes)?.Name ?? SelectedNotes;
    public string SelectedAskName => AskChoices.FirstOrDefault(c => c.Id == SelectedAsk)?.Name ?? SelectedAsk;
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
    public Func<string, Action<string>, Task<InstallOutcome>>? Install { get; set; }

    /// <summary>The AIs being set up from a row now, and where each has got to ("Downloading Claude Code…").</summary>
    readonly Dictionary<string, string> settingUp = [];
    /// <summary>AIs whose install stopped for a reason Syft Notes can't put right: their row offers Get help.</summary>
    readonly HashSet<string> installStopped = [];
    AiOverview? last;

    partial void OnSelectedNotesChanged(string value)
    {
        EngineChoice.Mark(NotesChoices, value);
        OnPropertyChanged(nameof(SelectedNotesName));
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

    /// <summary>Reads the library's AI once and fills every row: called when the pane opens.</summary>
    public async Task Load()
    {
        AiOverview? overview;
        try
        {
            overview = await library.EnginesAsync();
        }
        catch
        {
            Offline = true;
            return;
        }
        Apply(overview);
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
            // A sign-in the press said was going that has stopped since (or never started): the line says so, as its row does.
            if (Say?.StartsWith("Finish signing in to ", StringComparison.Ordinal) == true
                && overview.Engines.FirstOrDefault(e => e.State == "not_signed_in" && e.SignIn is SignInPhase.GaveUp or SignInPhase.Region or SignInPhase.Stuck) is { } stopped)
                Say = AiWords.SignInSaid(AiWords.PlainName(stopped.Id, stopped.Name), stopped.SignIn);
            // Or it worked (learned here, or from the problem card on the same page): the line says so, not "Finish…".
            else if (Say is { } said && overview.Engines.FirstOrDefault(e => said == AiSignIns.StartedWords(e.Id)) is { State: not "not_signed_in" } done)
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
            shown = SignInWatch.Signature(overview);
            watch.Begin(overview);
            if (!adopting) Heard?.Invoke(overview);
        }
        finally
        {
            loading = false;
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
            await AfterAction(await library.KeyAsync(row.Id, key.Trim()));
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

    AiEngineRow BuildRow(EngineInfo e)
    {
        // A sign-in going from here (the AI's own, hidden, in the student's browser): the row says where it is, and turns
        // ready by itself when it's done.
        string signIn = e.State == "not_signed_in" ? e.SignIn : "";
        string name = AiWords.PlainName(e.Id, e.Name);
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
                : e.State == "not_signed_in" && signingIn.Contains(e.Id) ? "Check" : AiWords.RowActionWords(e.State),
            IsPrimary = AiWords.RowActionPrimary(e.State),
            NeedsOptions = AiWords.NeedsOptions(e.State),
            Model = e.Model,
            Models = e.Models,
            Site = e.Site,
        };
        row.ActCommand = new AsyncRelayCommand(() => RunRowActionAsync(row));
        row.CheckCommand = new AsyncRelayCommand(() => CheckAsync(row.Id));
        row.LinkCommand = new AsyncRelayCommand(() => SignInLinkAsync(row));
        row.ModelChoices = [.. e.Models.Select(m => new AiModelChoice(m.Id, m.Label, m.Id == e.Model, new AsyncRelayCommand(() => PickModelAsync(row.Id, m.Id))))];
        row.AddCommand = new RelayCommand(() =>
        {
            if (row.Site.Length > 0) OpenUrl?.Invoke(row.Site);
            Say = Here ? $"Install {row.Name} on this computer." : $"Install {row.Name} on your library's computer.";
        });
        return row;
    }

    async Task RunRowActionAsync(AiEngineRow row)
    {
        Busy = true;
        try
        {
            if (row.State == "not_installed" && row.Id != "ollama")
            {
                Busy = false;
                await SetUpAsync(row);
                return;
            }
            AiSaid? said = row.State switch
            {
                "not_installed" => await library.SetUpAsync(row.Id),
                "not_signed_in" when signingIn.Remove(row.Id) => await library.CheckAsync(row.Id),
                "not_signed_in" => await SignInAsync(row.Id),
                "not_running" => await library.StartAsync(row.Id),
                "model_missing" => await library.DownloadAsync(row.Id),
                "unchecked" => await library.CheckAsync(row.Id),
                _ => null,
            };
            await AfterAction(said);
        }
        catch (LibraryRefusedException ex)
        {
            Say = ex.Message;
            // Sign in from a laptop says where to sign in: the row's button is Check for when that's done.
            if (signingIn.Contains(row.Id)) await Load();
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

    /// <summary>Sign in: the library starts the AI's own sign-in, hidden, and it opens the student's browser; the row says
    /// it's waiting and turns ready by itself. (A library from before that opens a terminal to do it in, or says where to,
    /// from a laptop: the row's button becomes Check for when that's done.)</summary>
    async Task<AiSaid?> SignInAsync(string id)
    {
        signingIn.Add(id);
        var said = await library.SignInAsync(id);
        return HiddenOrOld(id, said);
    }

    /// <summary>A library that signs in hidden says where each sign-in is on the AI's row; one that doesn't leaves the
    /// student to press Check.</summary>
    AiSaid? HiddenOrOld(string id, AiSaid? said)
    {
        if (said?.Overview?.Engines.FirstOrDefault(e => e.Id == id) is { SignIn.Length: > 0 }) signingIn.Remove(id);
        return said;
    }

    /// <summary>Set up, on an AI not on this computer yet: its maker's installer, for this account, the row saying where
    /// it's got to, then its own sign-in, hidden (the row waits for the browser and turns ready by itself). Nothing to
    /// type, and no terminal. On a library on another computer, its site, as before.</summary>
    async Task SetUpAsync(AiEngineRow row)
    {
        if (Install is not { } install)
        {
            if (row.Site.Length > 0) OpenUrl?.Invoke(row.Site);
            Say = $"Install {row.Name} on your library's computer.";
            return;
        }
        if (settingUp.ContainsKey(row.Id)) return;
        string id = row.Id;
        installStopped.Remove(id);
        Say = null;
        settingUp[id] = $"Downloading {Core.Ai.Engines.Tool(id)}…";
        Redraw();
        var ui = SynchronizationContext.Current;
        InstallOutcome outcome;
        try
        {
            outcome = await install(id, phase => (ui is null ? (Action<Action>)(a => a()) : a => ui.Post(_ => a(), null))(() =>
            {
                if (!settingUp.ContainsKey(id)) return;
                settingUp[id] = phase;
                Redraw();
            }));
        }
        finally
        {
            settingUp.Remove(id);
        }
        if (!outcome.Ok)
        {
            if (outcome.Failure is InstallFailure.Other or InstallFailure.WontStart or InstallFailure.TimedOut) installStopped.Add(id);
            await Load();
            Say = outcome.Words;
            return;
        }
        await Load();
        Busy = true;
        try
        {
            await AfterAction(await SignInAsync(id));
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
            await AfterAction(await library.SignInAgainAsync(row.Id));
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

    async Task CheckAsync(string id)
    {
        Busy = true;
        try
        {
            await AfterAction(await library.CheckAsync(id));
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

    async Task PickModelAsync(string id, string model)
    {
        Busy = true;
        try
        {
            var overview = await library.ModelAsync(id, model);
            Apply(overview);
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

    async Task AfterAction(AiSaid? said)
    {
        if (said is null)
        {
            OlderLibrary = true;
            return;
        }
        Say = said.Said;
        if (said.Overview is not null) Apply(said.Overview);
        else await Load();
    }

    async Task PostDefaultsAsync(string? notes = null, string? ask = null, bool? fallback = null, string? diagrams = null)
    {
        Busy = true;
        try
        {
            var overview = await library.DefaultsAsync(notes, ask, fallback, diagrams);
            Apply(overview);
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

    /// <summary>A rich notes switch or Claude Code's speed changed: the library keeps it and says what it all comes to (turning
    /// off the last kind, say, switches rich notes off).</summary>
    async Task PostRichAsync(bool? on = null, bool? diagrams = null, bool? plots = null, bool? drawings = null, string? speed = null)
    {
        Busy = true;
        try
        {
            Apply(await library.RichAsync(on, diagrams, plots, drawings, speed));
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
}
