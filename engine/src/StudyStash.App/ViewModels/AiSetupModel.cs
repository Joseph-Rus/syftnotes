using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StudyStash.Core;
using StudyStash.Core.Ai;

namespace StudyStash.App.ViewModels;

/// <summary>One step in a setup row's help: its number, what to do, the command to paste (with Copy) and a line under
/// it.</summary>
public sealed class AiSetupStep
{
    public int Number { get; init; }
    public string Title { get; init; } = "";
    public string Command { get; init; } = "";
    public string Note { get; init; } = "";
    public bool HasCommand => Command.Length > 0;
    public bool HasNote => Note.Length > 0;
    public IRelayCommand CopyCommand { get; internal set; } = null!;
}

/// <summary>One engine in setup's "Choose who writes your notes" list: a radio row, the free AI (Ollama) marked Advanced. An
/// engine that can't write notes yet (not installed, not signed in) offers "Set up": the steps to get it going, with
/// the commands to copy, a button to open Terminal (or sign in there), and Check again.</summary>
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
    /// <summary>It can write notes now (as far as anyone can tell without asking it): picking it is allowed.</summary>
    public bool CanWrite { get; init; }
    /// <summary>The first row in the list shows no separator above it.</summary>
    public bool First { get; internal set; }
    [ObservableProperty] public partial bool Selected { get; set; }
    /// <summary>The steps to get it going show under the row.</summary>
    [ObservableProperty] public partial bool ShowHelp { get; set; }
    public IReadOnlyList<AiSetupStep> Help { get; init; } = [];
    public bool HasHelp => Help.Count > 0;
    public string HelpLabel => ShowHelp ? "Hide" : "Set up";
    /// <summary>The help's first button ("Open Terminal" to install, or "Sign in in Terminal"); for the free AI, the
    /// row's own one button ("Set it up").</summary>
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
    internal Action? HelpChanged { get; set; }

    partial void OnShowHelpChanged(bool value)
    {
        OnPropertyChanged(nameof(HelpLabel));
        HelpChanged?.Invoke();
    }

    public IRelayCommand SelectCommand { get; internal set; } = null!;
    public IAsyncRelayCommand SignInCommand { get; internal set; } = null!;
    public IRelayCommand ToggleHelpCommand { get; internal set; } = null!;
    public IAsyncRelayCommand OpenCommand { get; internal set; } = null!;
    public IAsyncRelayCommand CheckAgainCommand { get; internal set; } = null!;
}

/// <summary>
/// Setup's AI step (design 15): "Choose who writes your notes" (a radio list of the engines on this computer, Ollama
/// recommended, Claude Code and Codex with the steps to install and sign in when they aren't ready, and "No AI for
/// now" for transcripts alone) and "Who answers your questions" (a select that starts as Same as notes). The step's
/// body only: the host's sidebar and Back/Continue footer are its own. Continue calls <see cref="SaveAsync"/>.
/// </summary>
public sealed partial class AiSetupModel : ObservableObject
{
    /// <summary>The Ask select's first choice: follow whichever engine writes the notes.</summary>
    public const string SameAsNotes = "";
    /// <summary>"No AI for now": lectures are filed with their transcripts, and no notes are written.</summary>
    public const string None = "none";

    readonly IAiLibrary library;
    bool picked;

    public AiSetupModel(IAiLibrary library) => this.library = library;

    public ObservableCollection<AiSetupRow> Engines { get; } = [];
    public List<EngineChoice> AskChoices { get; private set; } = [new(SameAsNotes, "Same as notes")];

    /// <summary>The line under the title: where the engines run.</summary>
    public string Lede { get; init; } = "This computer is your library, so the AI runs here. You can change this later from any of your computers, in Settings → Your AI.";
    /// <summary>Windows words (PowerShell, not Terminal) and commands.</summary>
    public bool Windows { get; init; } = OperatingSystem.IsWindows();
    /// <summary>Puts a command on the clipboard.</summary>
    public Action<string>? Copy { get; init; }
    /// <summary>Opens Terminal (PowerShell on Windows) for a command to be pasted into.</summary>
    public Action? OpenTerminal { get; init; }
    public Action<string>? OpenUrl { get; init; }
    /// <summary>Gets Claude or ChatGPT ready on this computer with buttons (its maker's installer, then its own
    /// sign-in page), for a host that can: setup's window, which has guided setup's screens for it. Their rows then
    /// have one button each, and no commands to paste into a terminal. Null: the steps and commands, as before.</summary>
    public Func<string, Task>? SetUpPaid { get; init; }
    /// <summary>Turns the library's note writing (and sorting with AI) on or off; true when it did. Null in a host
    /// that can't, which then doesn't offer "No AI for now".</summary>
    public Func<bool, Task<bool>>? WriteNotes { get; init; }
    public bool OffersNoAi => WriteNotes is not null;

    [ObservableProperty] public partial string SelectedNotes { get; set; } = "ollama";
    [ObservableProperty] public partial string SelectedAsk { get; set; } = SameAsNotes;
    [ObservableProperty] public partial string? Say { get; set; }
    [ObservableProperty] public partial bool Busy { get; set; }
    [ObservableProperty] public partial bool OlderLibrary { get; set; }
    [ObservableProperty] public partial bool Offline { get; set; }
    /// <summary>A row's steps are showing (the setup window grows to fit them).</summary>
    [ObservableProperty] public partial bool HelpOpen { get; set; }

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

    /// <summary>Reads the library's AI once and fills the rows: called when the step opens.</summary>
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
        // Come back to while the free AI is still coming down: the row carries on showing it.
        if (overview?.Pulling is { Why.Length: 0 }) _ = FollowAsync();
    }

    /// <summary>It can write notes now: Ollama once installed (it may still need starting, or its model), a CLI
    /// that's installed and not known to be signed out.</summary>
    static bool CanWrite(EngineInfo e) => e.Installed && (e.Id == "ollama" || e.State is not ("not_signed_in" or "not_installed"));

    /// <summary>The first time: the library's own pick when it can write notes, else the first engine that's ready,
    /// else no AI for now (where offered).</summary>
    string FirstPick(AiOverview overview)
    {
        if (overview.Engines.FirstOrDefault(e => e.Id == overview.Notes) is { } notes && CanWrite(notes)) return notes.Id;
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

        var open = Engines.Where(r => r.ShowHelp).Select(r => r.Id).ToHashSet();
        // The free AI being got ready (its app, starting it, its model), and whether there's still something to get.
        var working = overview.Pulling is { Why.Length: 0 } at ? at : null;
        var failed = overview.Pulling is { Why.Length: > 0 } stopped ? stopped : null;
        bool freeNeedsWork = overview.Engines.FirstOrDefault(e => e.Id == "ollama") is { State: "not_installed" or "not_running" or "model_missing" };
        // Claude or ChatGPT, not on this computer or not signed in, where the host gets them ready with buttons.
        bool Buttons(EngineInfo e) => SetUpPaid is not null && e is { Id: "claude" or "codex", State: "not_installed" or "not_signed_in" };
        Engines.Clear();
        // Claude Code and Codex always show, with the steps to get them going; Gemini only once it's here.
        foreach (var e in overview.Engines.Where(e => e.Id is "ollama" or "claude" or "codex" || e.Installed))
        {
            var steps = Buttons(e) ? [] : EngineSetupWords.Steps(e.Id, e.State, Windows);
            string terminal = EngineSetupWords.TerminalName(Windows);
            var row = new AiSetupRow
            {
                Id = e.Id,
                Name = AiWords.SetupName(e.Id, e.Name, Windows ? "PC" : "Mac"),
                About = AiWords.SetupAbout(e.Id, e.State, Windows ? "PC" : "Mac", e.SetUpGb, e.Small),
                State = e.State,
                Advanced = e.Id == "ollama",
                OneButton = e.Id == "ollama" && freeNeedsWork || Buttons(e),
                Working = e.Id == "ollama" && working is not null,
                Progress = e.Id == "ollama" && working is not null ? AiWords.FreeAiProgress(working) : "",
                Fraction = working?.Fraction ?? 0,
                Steady = working is { Step: "start" },
                ShowSignIn = e.State == "not_signed_in" && steps.Count == 0 && !Buttons(e),
                CanWrite = CanWrite(e),
                Selected = e.Id == SelectedNotes,
                First = Engines.Count == 0,
                Help = [.. steps.Select((h, i) => new AiSetupStep
                {
                    Number = i + 1, Title = h.Title, Command = h.Command, Note = h.Note,
                    CopyCommand = new RelayCommand(() => Copy?.Invoke(h.Command)),
                })],
                OpenLabel = (e.Id, e.State) switch
                {
                    ("ollama", _) => failed is null ? "Set it up" : "Try again",
                    (_, "not_signed_in") when Buttons(e) => "Sign in",
                    _ when Buttons(e) => "Set it up",
                    (_, "not_signed_in") => $"Sign in in {terminal}",
                    _ => $"Open {terminal}",
                },
                ShowHelp = open.Contains(e.Id) && steps.Count > 0,
                HelpChanged = () => HelpOpen = Engines.Any(r => r.ShowHelp),
            };
            row.SelectCommand = new RelayCommand(() => Pick(row));
            row.SignInCommand = new AsyncRelayCommand(() => SignInAsync(row.Id));
            row.ToggleHelpCommand = new RelayCommand(() => ShowHelpOf(row, !row.ShowHelp));
            row.OpenCommand = new AsyncRelayCommand(() => OpenAsync(row));
            row.CheckAgainCommand = new AsyncRelayCommand(() => CheckAgainAsync(row.Id));
            Engines.Add(row);
        }
        HelpOpen = Engines.Any(r => r.ShowHelp);
        OnPropertyChanged(nameof(ChoiceWords));
    }

    /// <summary>A row's help opens alone (the list stays short enough to see).</summary>
    void ShowHelpOf(AiSetupRow row, bool show)
    {
        foreach (var r in Engines) r.ShowHelp = r == row && show && r.HasHelp;
    }

    /// <summary>Picking a row: an engine that can write notes becomes the pick; one that can't yet shows how to get it
    /// going instead.</summary>
    void Pick(AiSetupRow row)
    {
        if (row.CanWrite) SelectedNotes = row.Id;
        else ShowHelpOf(row, true);
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
            Say = ex.Message;
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
        if (row.OneButton && SetUpPaid is { } withButtons)
        {
            Say = null;
            await withButtons(row.Id);
            return;
        }
        if (row.State == "not_signed_in")
        {
            await SignInAsync(row.Id);
            return;
        }
        OpenTerminal?.Invoke();
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
                if (row.HasHelp)
                {
                    // Ollama's here, but still needs starting or its model: the next step shows.
                    Say = $"{name} is here. {row.About}";
                }
                else
                {
                    ShowHelpOf(row, false);
                    Say = $"{name} is ready. It writes your notes.";
                }
            }
            else
            {
                Say = said ?? (row?.State == "not_installed"
                    ? $"{name} isn't installed yet. Run the first step, then Check again."
                    : $"{name} isn't signed in yet. Run the sign-in step, then Check again.");
            }
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
            string ask = SelectedAsk == SameAsNotes ? SelectedNotes : SelectedAsk;
            var overview = await library.DefaultsAsync(notes: SelectedNotes, ask: ask);
            if (overview is null) { OlderLibrary = true; return false; }
            if (WriteNotes is not null) await WriteNotes(true);
            return true;
        }
        catch (LibraryRefusedException ex)
        {
            Say = ex.Message;
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
