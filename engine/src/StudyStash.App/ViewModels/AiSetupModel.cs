using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StudyStash.Core;
using StudyStash.Core.Ai;

namespace StudyStash.App.ViewModels;

/// <summary>One engine in setup's "Choose who writes your notes" list: a radio row, the free AI (Ollama) marked Advanced. An
/// engine that can't write notes yet (not installed, not signed in) has one button that gets it ready (Syft Notes
/// installs it and signs in, with nothing to type and no terminal), or Sign in, or an API key.</summary>
public sealed partial class AiSetupRow : ObservableObject
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string About { get; init; } = "";
    public string State { get; init; } = "";
    /// <summary>The free AI that runs on the computer itself (Ollama): for someone comfortable with a big download, a
    /// slower computer while it works and simpler notes. Marked, so a student who isn't doesn't take it for the easy way.</summary>
    public bool Advanced { get; init; }
    public bool ShowSignIn { get; init; }
    /// <summary>"Use an API key" shows on the row: an AI that takes one (Claude, ChatGPT, Gemini) and isn't ready yet,
    /// where the host can save keys. The key goes in the panel under the list (<see cref="AiSetupModel.KeyFor"/>).</summary>
    public bool ShowKeyLink { get; init; }
    /// <summary>It can write notes now (as far as anyone can tell without asking it): picking it is allowed.</summary>
    public bool CanWrite { get; init; }
    /// <summary>The first row in the list shows no separator above it.</summary>
    public bool First { get; internal set; }
    [ObservableProperty] public partial bool Selected { get; set; }
    /// <summary>The row's one button: "Set it up", "Sign in", "Add a key", "Try again".</summary>
    public string OpenLabel { get; init; } = "";
    /// <summary>The free AI, not ready yet: one button on the row gets it ready (its app, starting it, its model), and
    /// nothing is left for the student to do in a browser or a terminal.</summary>
    public bool OneButton { get; init; }
    /// <summary>That's happening now: the row shows how far along instead of the button.</summary>
    public bool Working { get; init; }
    public string Progress { get; init; } = "";
    /// <summary>How far along, from 0 to 1; no figure while it's only starting.</summary>
    public double Fraction { get; init; }
    public bool Steady { get; init; }
    public bool ShowOneButton => OneButton && !Working;

    public IRelayCommand SelectCommand { get; internal set; } = null!;
    public IRelayCommand KeyCommand { get; internal set; } = null!;
    public IAsyncRelayCommand SignInCommand { get; internal set; } = null!;
    public IAsyncRelayCommand OpenCommand { get; internal set; } = null!;
    public IAsyncRelayCommand CheckAgainCommand { get; internal set; } = null!;
}

/// <summary>
/// Setup's AI step (design 15): "Choose who writes your notes" (a radio list: the free AI marked Advanced, Claude and
/// ChatGPT with one button each to get them ready, Gemini, each of those three able to run on an API key pasted in the
/// box under the list, and "No AI for now" for transcripts alone) and "Who answers your questions" (a select that
/// starts as Same as notes). The step's body only: the host's sidebar and Back/Continue footer are its own. Continue
/// calls <see cref="SaveAsync"/>, which first tries an AI that's only installed.
/// </summary>
public sealed partial class AiSetupModel : ObservableObject
{
    /// <summary>The Ask select's first choice: follow whichever engine writes the notes.</summary>
    public const string SameAsNotes = "";
    /// <summary>"No AI for now": lectures are filed with their transcripts, and no notes are written.</summary>
    public const string None = "none";

    readonly IAiLibrary library;
    bool picked;

    public AiSetupModel(IAiLibrary library)
    {
        this.library = library;
        watch = new SignInWatch(library, SignInMovedAsync);
    }

    /// <summary>Keeps asking the library while a sign-in started from a row is going, so the row turns ready by itself.</summary>
    readonly SignInWatch watch;
    string shown = "";

    /// <summary>How often a sign-in that's going is looked at; a test sets it small.</summary>
    public TimeSpan SignInEvery
    {
        get => watch.Every;
        set => watch.Every = value;
    }

    async Task SignInMovedAsync(AiOverview now)
    {
        if (SignInWatch.Signature(now) == shown) return;
        var waiting = Engines.Where(r => r.Working && r.Id != "ollama").Select(r => r.Id).ToList();
        Apply(now);
        // The one that was waiting is signed in now: the same as pressing Check again, which picks it when it can write notes.
        foreach (string id in waiting)
        {
            var e = now.Engines.FirstOrDefault(x => x.Id == id);
            if (e is { State: not "not_signed_in" }) await CheckAgainAsync(id);
            // It stopped without being signed in: said plainly, and its button tries again.
            else if (e is { SignIn: SignInPhase.GaveUp or SignInPhase.Region or SignInPhase.Stuck })
                Say = $"{AiWords.SignInTitle(AiWords.PlainName(e.Id, e.Name), e.SignIn)}. {AiWords.SignInAbout(AiWords.PlainName(e.Id, e.Name), e.SignIn)}";
        }
    }

    public ObservableCollection<AiSetupRow> Engines { get; } = [];
    public List<EngineChoice> AskChoices { get; private set; } = [new(SameAsNotes, "Same as notes")];

    /// <summary>The line under the title: where the engines run.</summary>
    public string Lede { get; init; } = "This computer is your library, so the AI runs here. You can change this later from any of your computers, in Settings → Your AI.";
    /// <summary>Windows words (PowerShell, not Terminal) and commands.</summary>
    public bool Windows { get; init; } = OperatingSystem.IsWindows();
    public Action<string>? OpenUrl { get; init; }
    /// <summary>Gets Claude, ChatGPT or Gemini ready on this computer with buttons (its maker's installer, then its own
    /// sign-in, hidden), for a host that can: setup's window, which has guided setup's screens for it. Their rows then
    /// have one button each. Null: a row signs in from here, or takes an API key, and is never given commands to type.</summary>
    public Func<string, Task>? SetUpPaid { get; init; }
    /// <summary>Turns the library's note writing (and sorting with AI) on or off; true when it did. Null in a host
    /// that can't, which then doesn't offer "No AI for now".</summary>
    public Func<bool, Task<bool>>? WriteNotes { get; init; }
    public bool OffersNoAi => WriteNotes is not null;
    /// <summary>Whether the library writes notes now: false once "No AI for now" was chosen, and the list then opens on
    /// it again. Null (the host can't ask, or the library didn't say) leaves the engines to decide.</summary>
    public Func<Task<bool?>>? NotesOn { get; init; }
    /// <summary>Setup run again on a computer that's set up: the library's engine was chosen before, so it stays
    /// picked even while it isn't ready (its download stopped, say).</summary>
    public bool ChosenBefore { get; init; }
    /// <summary>The AI the student answered with on setup's first screen, for a first run: "ollama" ("I don't have a paid
    /// plan"), "gemini", or Claude or ChatGPT once guided setup got it ready. It's the pick when it can write notes
    /// (the free AI only once its model is here too); Gemini that isn't ready opens on what it needs. "" for none.</summary>
    public string Prefer { get; init; } = "";
    /// <summary>The host can save an API key for an AI (setup's window can; a test that doesn't say can't).</summary>
    public bool TakesKeys { get; init; }
    /// <summary>An AI whose key box opens with the step: the one whose plan couldn't be used, when the student carried
    /// on from guided setup's "plan other apps can't use" card, which says a key can be added here.</summary>
    public string OpenKeyFor { get; init; } = "";
    bool? notesOn;
    bool preferShown, keyShown;

    [ObservableProperty] public partial string SelectedNotes { get; set; } = "ollama";
    [ObservableProperty] public partial string SelectedAsk { get; set; } = SameAsNotes;
    [ObservableProperty] public partial string? Say { get; set; }
    [ObservableProperty] public partial bool Busy { get; set; }
    [ObservableProperty] public partial bool OlderLibrary { get; set; }
    [ObservableProperty] public partial bool Offline { get; set; }
    /// <summary>The API key panel is open under the list (the setup window grows to fit it).</summary>
    [ObservableProperty] public partial bool HelpOpen { get; set; }
    /// <summary>The API key panel under the list is open for this AI ("claude", "codex", "gemini"), or "" when shut.
    /// It lives under the list, not in a row: the rows are made afresh each time the library answers, and a box in one
    /// would lose what's being typed.</summary>
    [ObservableProperty] public partial string KeyFor { get; set; } = "";
    [ObservableProperty] public partial string KeyDraft { get; set; } = "";

    public bool NoAi => SelectedNotes == None;
    public bool HasSay => !string.IsNullOrEmpty(Say);
    public string SelectedAskName => AskChoices.FirstOrDefault(c => c.Id == SelectedAsk)?.Name ?? SelectedAsk;
    public string OlderLibraryWords => AiWords.OlderLibraryWords;
    /// <summary>Who writes the notes, for setup's last page: "Ollama", or "Not yet: just transcripts".</summary>
    public string ChoiceWords => NoAi ? "Not yet: just transcripts" : Engines.FirstOrDefault(r => r.Id == SelectedNotes)?.Name ?? Engines.FirstOrDefault()?.Name ?? "";

    partial void OnSelectedAskChanged(string value)
    {
        EngineChoice.Mark(AskChoices, value);
        OnPropertyChanged(nameof(SelectedAskName));
    }

    partial void OnSayChanged(string? value) => OnPropertyChanged(nameof(HasSay));

    partial void OnKeyForChanged(string value)
    {
        foreach (string n in new[] { nameof(KeyOpen), nameof(KeyTitle), nameof(KeyLede), nameof(KeyWhere), nameof(KeyAlternative), nameof(HasKeyAlternative),
                     nameof(KeyPrivacy), nameof(HasKeyPrivacy) })
            OnPropertyChanged(n);
        HelpOpen = KeyOpen;
    }

    // --- the API key panel ------------------------------------------------------------------------------------------

    public bool KeyOpen => KeyFor.Length > 0;
    string KeyName => Core.Ai.Engines.Name(KeyFor);
    string KeyMaker => KeyFor switch { "claude" => "Anthropic", "codex" => "OpenAI", _ => "Google" };
    public string KeyTitle => $"Your {KeyName} API key";
    /// <summary>Where the key is made, what it costs (Claude's and ChatGPT's are paid for as they're used, apart from a
    /// plan), and where it goes.</summary>
    public string KeyLede => KeyFor switch
    {
        "" => "",
        "gemini" => $"Make one in {KeyWhere}, then paste it here. It stays on this {Device} and only goes to Google.",
        _ => $"Make one in {KeyWhere}, then paste it here. A key is paid for as it's used, apart from a {KeyName} plan. It stays on this {Device} and only goes to {KeyMaker}.",
    };
    public string KeyWhere => KeyFor switch { "claude" => "the Anthropic Console", "codex" => "OpenAI's platform", _ => "Google AI Studio" };
    static string KeyUrl(string id) => id switch
    {
        "claude" => "https://console.anthropic.com/settings/keys",
        "codex" => "https://platform.openai.com/api-keys",
        _ => "https://aistudio.google.com/apikey",
    };
    /// <summary>Gemini's other way, where it isn't on this computer at all: Set it up on its row, which installs Google's
    /// Antigravity and signs in to it, with nothing to type.</summary>
    public string KeyAlternative => KeyFor == "gemini" && SetUpPaid is not null && Engines.FirstOrDefault(r => r.Id == "gemini") is { State: "not_installed" }
        ? "Or press Set it up on Gemini's row, and sign in with your Google account." : "";
    public bool HasKeyAlternative => KeyAlternative.Length > 0;
    /// <summary>Under Gemini's key box: what Google's free tier does with what's sent, and who can have a free key.</summary>
    public string KeyPrivacy => KeyFor == "gemini" ? string.Join("\n", ApiKeys.GeminiKeyNotes(Device)) : "";
    public bool HasKeyPrivacy => KeyPrivacy.Length > 0;
    string Device => Machine.DeviceFor(Windows);

    void OpenKey(string id)
    {
        Say = null;
        KeyDraft = "";
        KeyFor = id;
    }

    [RelayCommand]
    void CloseKey()
    {
        KeyFor = "";
        KeyDraft = "";
    }

    [RelayCommand]
    void GetKey()
    {
        if (KeyFor.Length > 0) OpenUrl?.Invoke(KeyUrl(KeyFor));
    }

    [RelayCommand]
    Task CheckKeyAlternative() => CheckAgainAsync(KeyFor);

    /// <summary>Save: the library keeps the key and tries the AI with it at once. When it answers, that AI is the pick
    /// and the panel shuts; when it doesn't, the panel stays, with what the AI said.</summary>
    [RelayCommand]
    async Task SaveKey()
    {
        string id = KeyFor, key = KeyDraft.Trim();
        if (id.Length == 0 || key.Length == 0) return;
        Busy = true;
        Say = $"Trying {Core.Ai.Engines.Name(id)} with your key…";
        try
        {
            var said = await library.KeyAsync(id, key);
            KeyDraft = "";
            if (said is null)
            {
                OlderLibrary = true;
                Say = null;
                return;
            }
            if (said.Overview is not null) Apply(said.Overview);
            else await Load();
            Say = said.Said;
            if (said.Overview?.Engines.FirstOrDefault(e => e.Id == id) is { State: "ready" } && Engines.FirstOrDefault(r => r.Id == id) is { CanWrite: true })
            {
                SelectedNotes = id;
                KeyFor = "";
            }
        }
        catch (LibraryRefusedException ex)
        {
            Say = Words(ex);
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

    /// <summary>Reads the library's AI once and fills the rows: called when the step opens.</summary>
    public async Task Load()
    {
        AiOverview? overview;
        try
        {
            overview = await library.EnginesAsync();
            if (!picked && NotesOn is not null) notesOn = await NotesOn();
        }
        catch
        {
            Offline = true;
            return;
        }
        Apply(overview);
        // Come back to while the free AI is still coming down: the row carries on showing it.
        if (overview?.Pulling is { Why.Length: 0 }) _ = FollowAsync();
    }

    /// <summary>It can write notes now: Ollama once installed (it may still need starting, or its model), a CLI
    /// that's installed and not known to be signed out.</summary>
    static bool CanWrite(EngineInfo e) => e.Installed && (e.Id == "ollama" || e.State is not ("not_signed_in" or "not_installed"));

    /// <summary>It writes notes as it is, with nothing more to press: the free AI only once its model is here too.</summary>
    static bool WritesNow(EngineInfo e) => CanWrite(e) && (e.Id != "ollama" || e.State == "ready");

    /// <summary>Where the list opens, once (coming back to the step keeps what was picked on it): on what the library
    /// does now. Notes switched off ("No AI for now", chosen before) is that choice again. The library's own engine is
    /// the pick when it writes notes as it is, or when it was chosen before (setup run again, or the free AI being got
    /// ready this minute). Otherwise nothing has been chosen yet: the first engine that's ready, else no AI for now
    /// (where offered), so nothing is left picked that would switch notes on with an AI that can't write them.</summary>
    string FirstPick(AiOverview overview)
    {
        if (notesOn == false && OffersNoAi) return None;
        if (Prefer.Length > 0 && !ChosenBefore && overview.Engines.FirstOrDefault(e => e.Id == Prefer) is { } preferred
            && (Prefer == "ollama" ? WritesNow(preferred) : CanWrite(preferred)))
            return Prefer;
        if (overview.Engines.FirstOrDefault(e => e.Id == overview.Notes) is { } notes
            && (WritesNow(notes) || CanWrite(notes) && (ChosenBefore || overview.Pulling is not null)))
            return notes.Id;
        if (overview.Engines.FirstOrDefault(e => e.State == "ready" && CanWrite(e)) is { } ready) return ready.Id;
        return OffersNoAi ? None : overview.Notes;
    }

    void Apply(AiOverview? overview)
    {
        if (overview is null)
        {
            OlderLibrary = true;
            Offline = false;
            return;
        }
        OlderLibrary = false;
        Offline = false;
        if (!picked)
        {
            SelectedNotes = FirstPick(overview);
            // Questions follow the notes unless the library asks another engine that's here.
            SelectedAsk = overview.Ask == overview.Notes || overview.Engines.FirstOrDefault(e => e.Id == overview.Ask) is not { Installed: true } ? SameAsNotes : overview.Ask;
            picked = true;
        }
        AskChoices = [new EngineChoice(SameAsNotes, "Same as notes"), .. overview.Engines.Where(e => e.Installed).Select(e => new EngineChoice(e.Id, AiWords.PlainName(e.Id, e.Name)))];
        foreach (var c in AskChoices) c.Pick = new RelayCommand(() => SelectedAsk = c.Id);
        EngineChoice.Mark(AskChoices, SelectedAsk);
        OnPropertyChanged(nameof(AskChoices));
        OnPropertyChanged(nameof(SelectedAskName));

        // The free AI being got ready (its app, starting it, its model), and whether there's still something to get.
        var working = overview.Pulling is { Why.Length: 0 } at ? at : null;
        var failed = overview.Pulling is { Why.Length: > 0 } stopped ? stopped : null;
        bool freeNeedsWork = overview.Engines.FirstOrDefault(e => e.Id == "ollama") is { State: "not_installed" or "not_running" or "model_missing" };
        // Claude, ChatGPT or Gemini, not on this computer or not signed in, where the host gets them ready with buttons
        // (guided setup's screens: Syft Notes installs it and signs in, hidden; Gemini's sign-in is in Syft Notes' own
        // home for agy).
        bool Buttons(EngineInfo e) => SetUpPaid is not null && e is { Id: "claude" or "codex" or "gemini", State: "not_installed" or "not_signed_in" };
        // Gemini, not here at all, where the host has no such screens: its one button is its API key.
        bool GeminiKey(EngineInfo e) => TakesKeys && SetUpPaid is null && e is { Id: "gemini", State: "not_installed" };
        // Not a working pick: not here, signed out, or its last try failed (a plan other apps can't use, say).
        bool NotReady(EngineInfo e) => e.State is "not_installed" or "not_signed_in" or "failed";
        // A sign-in going from a row (the AI's own, hidden, in the student's browser): the row says it's waiting.
        static bool SigningIn(EngineInfo e) => e is { State: "not_signed_in", SignIn: SignInPhase.Waiting };
        Engines.Clear();
        // Every AI shows, each with what gets it going: Claude, ChatGPT, Gemini and the free AI.
        foreach (var e in AiWords.InShownOrder(overview.Engines))
        {
            var row = new AiSetupRow
            {
                Id = e.Id,
                Name = AiWords.SetupName(e.Id, e.Name, Machine.DeviceFor(Windows)),
                About = AiWords.SetupAbout(e.Id, e.State, Machine.DeviceFor(Windows), e.SetUpGb, e.Small, e.HasKey),
                State = e.State,
                Advanced = e.Id == "ollama",
                OneButton = e.Id == "ollama" && freeNeedsWork || Buttons(e) || GeminiKey(e),
                Working = e.Id == "ollama" && working is not null || SigningIn(e),
                Progress = e.Id == "ollama" && working is not null ? AiWords.FreeAiProgress(working) : SigningIn(e) ? AiWords.SignInWaiting : "",
                Fraction = working?.Fraction ?? 0,
                Steady = working is { Step: "start" } || SigningIn(e),
                ShowSignIn = e.State == "not_signed_in" && !Buttons(e),
                ShowKeyLink = TakesKeys && e.Id is ("claude" or "codex" or "gemini") && NotReady(e) && !GeminiKey(e),
                CanWrite = CanWrite(e),
                Selected = e.Id == SelectedNotes,
                First = Engines.Count == 0,
                OpenLabel = (e.Id, e.State) switch
                {
                    ("ollama", _) => failed is null ? "Set it up" : "Try again",
                    _ when GeminiKey(e) => "Add a key",
                    (_, "not_signed_in") => "Sign in",
                    _ when Buttons(e) => "Set it up",
                    _ => "",
                },
            };
            row.SelectCommand = new RelayCommand(() => Pick(row));
            row.KeyCommand = new RelayCommand(() => OpenKey(row.Id));
            row.SignInCommand = new AsyncRelayCommand(() => SignInAsync(row.Id));
            row.OpenCommand = new AsyncRelayCommand(() => OpenAsync(row));
            row.CheckAgainCommand = new AsyncRelayCommand(() => CheckAgainAsync(row.Id));
            Engines.Add(row);
        }
        // The AI answered with on the first screen, not ready yet: it opens on what it needs, once (Gemini's key, where
        // that's its one button), or the step says which button gets it ready.
        if (!preferShown && Prefer.Length > 0 && !ChosenBefore && SelectedNotes != Prefer && Engines.FirstOrDefault(r => r.Id == Prefer) is { } wanted)
        {
            preferShown = true;
            if (wanted is { Id: "gemini", ShowOneButton: true, OpenLabel: "Add a key" }) KeyFor = "gemini";
            // A button that downloads (the free AI's is big) isn't pressed for the student: the step says where it is.
            else if (wanted is { ShowOneButton: true }) Say = wanted.Id == "ollama"
                ? $"To have the free AI write your notes, press “{wanted.OpenLabel}” on its row."
                : $"To have {wanted.Name} write your notes, press “{wanted.OpenLabel}” on its row.";
        }
        if (!keyShown && TakesKeys && OpenKeyFor is "claude" or "codex" or "gemini" && SelectedNotes != OpenKeyFor && Engines.Any(r => r.Id == OpenKeyFor))
        {
            keyShown = true;
            KeyFor = OpenKeyFor;
        }
        if (KeyFor.Length > 0 && Engines.All(r => r.Id != KeyFor)) KeyFor = "";
        HelpOpen = KeyOpen;
        OnPropertyChanged(nameof(ChoiceWords));
        OnPropertyChanged(nameof(KeyAlternative));
        OnPropertyChanged(nameof(HasKeyAlternative));
        shown = SignInWatch.Signature(overview);
        watch.Begin(overview);
    }

    /// <summary>What the library said when it refused, for the student: turned away at the door ("wrong password"),
    /// the app's copy of the library's password is out of date, and Settings is where the new one goes.</summary>
    static string Words(LibraryRefusedException ex) =>
        ex.Status == 401 ? "The library's password changed. Type the new one in Settings." : ex.Message;

    /// <summary>Picking a row: an engine that can write notes becomes the pick; one that can't yet gets ready instead.
    /// The row is never dead: Claude's, ChatGPT's and Gemini's do what their button does (it only opens the screens
    /// that get them ready). The free AI's button starts a long download at once, so its row says to press it and
    /// stays unpicked: a click never leaves notes switched on with an AI that has nothing to write them with.</summary>
    void Pick(AiSetupRow row)
    {
        if (row.CanWrite && !row.ShowOneButton) SelectedNotes = row.Id;
        else if (row is { ShowOneButton: true, OpenLabel: "Add a key" }) OpenKey(row.Id);
        else if (row.ShowOneButton && row.Id != "ollama") _ = row.OpenCommand.ExecuteAsync(null);
        else if (row.ShowOneButton || row.ShowSignIn) Say = $"{row.Name} isn't ready to write notes yet. Press “{(row.ShowSignIn ? "Sign in" : row.OpenLabel)}” on its row to get it ready.";
    }

    [RelayCommand]
    void PickNoAi() => SelectedNotes = None;

    partial void OnSelectedNotesChanged(string value)
    {
        foreach (var row in Engines) row.Selected = row.Id == value;
        OnPropertyChanged(nameof(NoAi));
        OnPropertyChanged(nameof(ChoiceWords));
    }

    /// <summary>How often the library is asked how far the free AI's download has got.</summary>
    public Func<Task> Wait { get; init; } = () => Task.Delay(TimeSpan.FromSeconds(1.5));
    bool following;

    /// <summary>"Set it up" on the free AI's row: the library gets it ready in one go (downloads and installs its app,
    /// starts it, downloads what it writes notes with), and the row shows how far along. As soon as its app is in, it's
    /// the pick for the notes, and setup can go on while the rest comes down.</summary>
    async Task SetUpFreeAsync()
    {
        Say = null;
        try
        {
            var said = await library.SetUpAsync("ollama");
            if (said is null)
            {
                // A library from before it could: the old way, one piece at a time.
                OlderLibrary = true;
                return;
            }
            if (said.Overview is not null) Apply(said.Overview);
            await FollowAsync();
        }
        catch (LibraryRefusedException ex)
        {
            Say = Words(ex);
        }
        catch
        {
            Offline = true;
        }
    }

    /// <summary>Follows the free AI being got ready until it's done or has stopped, the rows showing each answer.</summary>
    async Task FollowAsync()
    {
        if (following) return;
        following = true;
        try
        {
            while (true)
            {
                await Wait();
                var overview = await library.EnginesAsync();
                if (overview is null) return;
                Apply(overview);
                var free = Engines.FirstOrDefault(r => r.Id == "ollama");
                // Its app is here: it's the pick (the student pressed its button), while its model comes down.
                if (free is { CanWrite: true } && SelectedNotes != "ollama") SelectedNotes = "ollama";
                if (overview.Pulling is { Why.Length: > 0 } stopped)
                {
                    Say = stopped.Why;
                    return;
                }
                if (overview.Pulling is null)
                {
                    if (free is { State: "ready" }) Say = $"{free.Name} is ready. It writes your notes.";
                    return;
                }
            }
        }
        catch
        {
            Offline = true;
        }
        finally
        {
            following = false;
        }
    }

    async Task OpenAsync(AiSetupRow row)
    {
        if (row.Id == "ollama")
        {
            await SetUpFreeAsync();
            return;
        }
        if (row is { OneButton: true, OpenLabel: "Add a key" })
        {
            OpenKey(row.Id);
            return;
        }
        if (row.OneButton && SetUpPaid is { } withButtons)
        {
            Say = null;
            await withButtons(row.Id);
            return;
        }
        if (row.State == "not_signed_in") await SignInAsync(row.Id);
    }

    /// <summary>Check again: looks at the engine afresh (after installing it, or signing in) and, when it can write
    /// notes now, picks it, without leaving setup. A CLI that's there is tried for real, which is how its sign-in shows.</summary>
    async Task CheckAgainAsync(string id)
    {
        Busy = true;
        Say = null;
        try
        {
            var overview = await library.EnginesAsync();
            if (overview is null)
            {
                OlderLibrary = true;
                return;
            }
            string? said = null;
            if (overview.Engines.FirstOrDefault(e => e.Id == id) is { Installed: true, State: "unchecked" or "not_signed_in" } && id != "ollama")
            {
                var check = await library.CheckAsync(id);
                if (check is null)
                {
                    OlderLibrary = true;
                    return;
                }
                said = check.Said;
                overview = check.Overview ?? overview;
            }
            Apply(overview);
            var row = Engines.FirstOrDefault(r => r.Id == id);
            string name = row?.Name ?? Core.Ai.Engines.Name(id);
            if (row is { CanWrite: true })
            {
                SelectedNotes = id;
                if (KeyFor == id) KeyFor = "";
                Say = $"{name} is ready. It writes your notes.";
            }
            else
            {
                Say = said ?? (row?.State == "not_installed"
                    ? $"{name} isn't on this {Device} yet."
                    : $"{name} isn't signed in yet. Press Sign in on its row.");
            }
        }
        catch (LibraryRefusedException ex)
        {
            Say = Words(ex);
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

    Task SignInAsync(string id) => ActAsync(() => library.SignInAsync(id));

    /// <summary>One of the library's engine actions (sign in, start Ollama, download its model): what it said, and
    /// the rows as they are after it.</summary>
    async Task ActAsync(Func<Task<AiSaid?>> act)
    {
        Busy = true;
        try
        {
            var said = await act();
            if (said is null) { OlderLibrary = true; return; }
            Say = said.Said;
            if (said.Overview is not null) Apply(said.Overview);
            else await Load();
        }
        catch (LibraryRefusedException ex)
        {
            Say = Words(ex);
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

    /// <summary>
    /// The picked AI is saved only when it can write the notes. One that's only installed (never tried: Claude's
    /// sign-in is in the keychain, Antigravity's somewhere of its own), or whose last try failed, is tried once more
    /// with one tiny question, so a signed-out one doesn't pass setup and leave the notes to fail later. One known not
    /// to work (signed out, not here) isn't saved at all: Continue again says why again. True when it can be saved
    /// (or the library is too old to ask); else the step says why and stays.
    /// </summary>
    async Task<bool> CheckedAsync(string id)
    {
        if (id == "ollama" || Engines.FirstOrDefault(r => r.Id == id) is not { } row) return true;
        if (row.State == "not_signed_in" || !row.CanWrite)
        {
            Say = NotWorking(row, row.State == "not_signed_in" ? "signed out" : "");
            return false;
        }
        if (row.State is not ("unchecked" or "failed")) return true;
        Say = $"Checking {row.Name} works…";
        AiSaid? check;
        try
        {
            check = await library.CheckAsync(id);
        }
        catch (TaskCanceledException)
        {
            // The library took longer than the app waits for it: the try ran out of time, the library is fine.
            Say = NotWorking(row, "late");
            return false;
        }
        if (check is null)
        {
            Say = null;
            return true;
        }
        if (check.Overview is not null) Apply(check.Overview);
        if (check.Overview?.Engines.FirstOrDefault(e => e.Id == id) is { State: "ready" })
        {
            Say = null;
            return true;
        }
        Say = NotWorking(Engines.FirstOrDefault(r => r.Id == id) ?? row,
            Core.Ai.Engines.LooksSignedOut(check.Said) ? "signed out" : check.Said.Contains("didn't finish within", StringComparison.Ordinal) ? "late" : check.Said);
        return false;
    }

    /// <summary>Why the picked AI can't be saved, in the step's words: signed out, not here, too slow to answer, or
    /// what it said.</summary>
    string NotWorking(AiSetupRow row, string why)
    {
        string device = Machine.DeviceFor(Windows);
        return why switch
        {
            "signed out" => $"{row.Name} isn't signed in on this {device}. Sign in from its row, or pick another.",
            "late" => $"{row.Name} didn't answer in time: try again, or pick another.",
            "" => $"{row.Name} isn't ready to write notes on this {device} yet. Set it up from its row, or pick another.",
            _ => $"{row.Name} didn't answer: {Py.Head(why, 160).TrimEnd('.')}. Try again, or pick another.",
        };
    }

    /// <summary>The setup host calls this on Continue: saves the pick (ask follows notes when the student left it
    /// as Same as notes) and says whether it worked. "No AI for now" turns note writing off instead (and on again
    /// once an engine is picked).</summary>
    public async Task<bool> SaveAsync()
    {
        Busy = true;
        try
        {
            if (NoAi)
            {
                if (WriteNotes is not null && !await WriteNotes(false))
                {
                    Say = "Your library didn't take that. Try again.";
                    return false;
                }
                if (SelectedAsk != SameAsNotes && await library.DefaultsAsync(ask: SelectedAsk) is null) { OlderLibrary = true; return false; }
                return true;
            }
            if (!await CheckedAsync(SelectedNotes)) return false;
            string ask = SelectedAsk == SameAsNotes ? SelectedNotes : SelectedAsk;
            var overview = await library.DefaultsAsync(notes: SelectedNotes, ask: ask);
            if (overview is null) { OlderLibrary = true; return false; }
            if (WriteNotes is not null) await WriteNotes(true);
            return true;
        }
        catch (LibraryRefusedException ex)
        {
            Say = Words(ex);
            return false;
        }
        catch
        {
            Offline = true;
            return false;
        }
        finally
        {
            Busy = false;
        }
    }
}
