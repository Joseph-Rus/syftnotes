using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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

/// <summary>One row of the AI engines pane: an installed engine (or Ollama, always) with its state and what its
/// button does. The row owns its own commands, closed over its id, so a view binds them straight from the row.</summary>
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
    public string Model { get; init; } = "";
    public List<ModelOption> Models { get; init; } = [];
    public List<AiModelChoice> ModelChoices { get; internal set; } = [];
    public bool HasModels => ModelChoices.Count > 0;
    public string Site { get; init; } = "";
    /// <summary>The first row in its group shows no separator above it.</summary>
    public bool First { get; internal set; }

    /// <summary>Ready to shown rows: Sign in / Start / Download / Check / Get it, whichever this row's state needs.</summary>
    public IAsyncRelayCommand ActCommand { get; internal set; } = null!;
    /// <summary>"Check it works", from the Options menu (ready/limited/failed rows).</summary>
    public IAsyncRelayCommand CheckCommand { get; internal set; } = null!;
    /// <summary>Not-installed rows in the "Add an engine" menu: opens the engine's site.</summary>
    public IRelayCommand AddCommand { get; internal set; } = null!;
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

    public AiEnginesModel(IAiLibrary library) => this.library = library;

    /// <summary>The line under the pane's title: where the engines run (the library elsewhere, or this computer).</summary>
    public string Lede { get; init; } = "Notes are written after each lecture. Answers come while you ask. Engines run on your library, and you can set them up there or from this laptop.";

    public ObservableCollection<AiEngineRow> Engines { get; } = [];
    /// <summary>Not-installed engines: the "Add an engine" menu.</summary>
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
            NotesChoices = [.. overview.Engines.Select(e => new EngineChoice(e.Id, e.Name))];
            foreach (var c in NotesChoices) c.Pick = new RelayCommand(() => SelectedNotes = c.Id);
            AskChoices = [.. overview.Engines.Select(e => new EngineChoice(e.Id, e.Name))];
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

            Engines.Clear();
            AddChoices.Clear();
            foreach (var e in overview.Engines)
            {
                var row = BuildRow(e);
                if (e.Id == "ollama" || e.Installed) { row.First = Engines.Count == 0; Engines.Add(row); }
                else AddChoices.Add(row);
            }
            OnPropertyChanged(nameof(HasAddChoices));
        }
        finally
        {
            loading = false;
        }
    }

    AiEngineRow BuildRow(EngineInfo e)
    {
        var row = new AiEngineRow
        {
            Id = e.Id,
            Name = e.Name,
            Icon = AiWords.EngineIcon(e.Id),
            About = e.Installed || e.Id == "ollama" ? AiWords.EngineAbout(e.Id, e.State) : "Install it on your library's computer.",
            State = e.State,
            StateWords = AiWords.EngineStateWords(e.State),
            Good = AiWords.StateIsGood(e.State),
            Warn = AiWords.StateIsWarn(e.State),
            Muted = AiWords.StateIsMuted(e.State),
            ActionWords = e.Installed || e.Id == "ollama" ? AiWords.RowActionWords(e.State) : "Get it",
            IsPrimary = AiWords.RowActionPrimary(e.State),
            NeedsOptions = AiWords.NeedsOptions(e.State),
            Model = e.Model,
            Models = e.Models,
            Site = e.Site,
        };
        row.ActCommand = new AsyncRelayCommand(() => RunRowActionAsync(row));
        row.CheckCommand = new AsyncRelayCommand(() => CheckAsync(row.Id));
        row.ModelChoices = [.. e.Models.Select(m => new AiModelChoice(m.Id, m.Label, m.Id == e.Model, new AsyncRelayCommand(() => PickModelAsync(row.Id, m.Id))))];
        row.AddCommand = new RelayCommand(() =>
        {
            if (row.Site.Length > 0) OpenUrl?.Invoke(row.Site);
            Say = $"Install {row.Name} on your library's computer.";
        });
        return row;
    }

    async Task RunRowActionAsync(AiEngineRow row)
    {
        Busy = true;
        try
        {
            AiSaid? said = row.State switch
            {
                "not_signed_in" => await library.SignInAsync(row.Id),
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
