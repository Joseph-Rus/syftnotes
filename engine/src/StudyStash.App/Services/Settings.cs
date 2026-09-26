using System.Collections.ObjectModel;
using System.Text.Json.Nodes;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StudyStash.Audio;
using StudyStash.Core;
using StudyStash.Core.Ai;
using StudyStash.App.ViewModels;

namespace StudyStash.App.Services;

/// <summary>A Whisper model in Settings: its name, size, what it's for, and whether it's here.</summary>
public sealed partial class ModelChoice : ObservableObject
{
    public required WhisperModel Model { get; init; }
    public string Name => Model.Name;
    public string About => $"{Model.Size}. {Model.About}";
    [ObservableProperty] public partial bool Chosen { get; set; }
    [ObservableProperty] public partial bool Here { get; set; }
}

/// <summary>A class in the timetable editor: its name and its times as typed.</summary>
public sealed partial class TimetableRow : ObservableObject
{
    [ObservableProperty] public partial string Name { get; set; } = "";
    [ObservableProperty] public partial string Times { get; set; } = "";
    [ObservableProperty] public partial bool Bad { get; set; }
    public Avalonia.Media.IBrush Dot { get; init; } = Avalonia.Media.Brushes.Gray;
}

/// <summary>One row in the settings sidebar: its section id, icon and label, and whether it's the one showing.</summary>
public sealed partial class NavItem : ObservableObject
{
    public required string Id { get; init; }
    public required string Glyph { get; init; }
    public required string Label { get; init; }
    [ObservableProperty] public partial bool On { get; set; }
}

/// <summary>
/// Settings: General, Appearance (the colour theme), Recording (the model, language, the computer's sound, how long
/// audio stays), Library (where it is; this computer's own), Classes (the timetable), AI engines (who writes the notes
/// and answers questions), AI tool access (what Claude Code, Codex and other MCP tools may read) and Canvas.
/// </summary>
public sealed partial class SettingsModel : ObservableObject, IDisposable
{
    readonly AppHost host;

    [ObservableProperty] public partial string Section { get; set; } = "Library";

    /// <summary>The sidebar's rows, in the design's order: General, Appearance, then the rest as they were.</summary>
    public IReadOnlyList<NavItem> NavItems { get; } =
    [
        new() { Id = "General", Glyph = "tune", Label = "General" },
        new() { Id = "Appearance", Glyph = "palette", Label = "Appearance" },
        new() { Id = "Recording", Glyph = "mic", Label = "Recording" },
        new() { Id = "Library", Glyph = "dns", Label = "Library" },
        new() { Id = "Classes", Glyph = "schedule", Label = "Classes" },
        new() { Id = "AI", Glyph = "auto_awesome", Label = "AI engines" },
        new() { Id = "Access", Glyph = "hub", Label = "AI tool access" },
        new() { Id = "Canvas", Glyph = "school", Label = "Canvas" },
    ];

    /// <summary>Whether this window draws the Mac's round swatches or Windows' squared ones.</summary>
    public bool ShowMacSwatch => Skin.Current == SkinKind.Mac;

    // Appearance
    public IReadOnlyList<ThemeSwatch> Themes { get; } = [.. ColourThemes.All.Select(t => new ThemeSwatch(t))];
    [ObservableProperty] public partial string ColourTheme { get; set; } = "";

    // Library
    [ObservableProperty] public partial string LibraryLine { get; set; } = "";
    [ObservableProperty] public partial string Address { get; set; } = "";
    [ObservableProperty] public partial string Password { get; set; } = "";
    [ObservableProperty] public partial string? LibrarySay { get; set; }
    [ObservableProperty] public partial bool LibraryHere { get; set; }
    /// <summary>How this computer's own library is doing (Both/Library roles only): running, starting, stopped,
    /// already running from elsewhere, its port taken, or why it stopped.</summary>
    [ObservableProperty] public partial string LibraryServiceLine { get; set; } = "";
    [ObservableProperty] public partial string DisplayName { get; set; } = "";

    // Recording
    public ObservableCollection<ModelChoice> Models { get; } = [];
    [ObservableProperty] public partial string ModelLine { get; set; } = "";
    [ObservableProperty] public partial string Language { get; set; } = "";
    [ObservableProperty] public partial bool ComputerAudio { get; set; }
    [ObservableProperty] public partial string KeepAudio { get; set; } = "30";
    [ObservableProperty] public partial bool Shortcuts { get; set; }
    /// <summary>Which shortcut (if either) another app already has, once the toggle's had a moment to try them.</summary>
    [ObservableProperty] public partial string? ShortcutsSay { get; set; }
    public bool CanRecordComputerAudio => host.CanRecordComputerAudio;

    // Classes
    public ObservableCollection<TimetableRow> Rows { get; } = [];
    [ObservableProperty] public partial string NewClass { get; set; } = "";
    [ObservableProperty] public partial string NewTimes { get; set; } = "";
    [ObservableProperty] public partial string? ClassesSay { get; set; }

    // AI engines, AI tool access and Canvas: each is its own pane over its own model, read when it's opened.
    public AiEnginesModel Engines { get; }
    /// <summary>What's wrong with an engine right now (offline, signed out, a model missing, a usage limit), above
    /// the engines.</summary>
    public AiProblemsModel AiProblems { get; }
    public AiAccessModel Access { get; }
    public CanvasSettingsModel Canvas { get; }

    // General
    [ObservableProperty] public partial bool StartAtLogin { get; set; }
    public string Version => Engine.Version;

    public bool OnLibrary => Section == "Library";
    public bool OnRecording => Section == "Recording";
    public bool OnClasses => Section == "Classes";
    public bool OnAi => Section == "AI";
    public bool OnCanvas => Section == "Canvas";
    public bool OnAccess => Section == "Access";
    /// <summary>The AI panes scroll and pad themselves; every other section sits in the page's own scroller.</summary>
    public bool OnPlainPage => !OnAi && !OnAccess;
    public bool IsMac => Skin.Current == SkinKind.Mac;
    public bool IsWin => !IsMac;
    public bool OnGeneral => Section == "General";
    public bool OnAppearance => Section == "Appearance";
    /// <summary>This computer's own library isn't running (or couldn't), and its role calls for one.</summary>
    public bool CanStartLibrary => host.Settings.Role != AppRole.Laptop
        && host.LocalLibrary?.State is null or LibraryServiceState.Stopped or LibraryServiceState.Failed or LibraryServiceState.PortTaken;
    /// <summary>This computer's own library is ours to stop (started by us, not one already running elsewhere).</summary>
    public bool CanStopLibrary => host.LocalLibrary?.State == LibraryServiceState.Running;

    /// <summary>Settings over <paramref name="host"/>. The AI panes read <paramref name="ai"/> (the connected library's
    /// AI when not given); Canvas reads <paramref name="canvas"/> and follows <paramref name="watch"/>, the app's one
    /// shared Canvas poll (none in a test or a shot, so nothing there polls).</summary>
    public static SettingsModel Make(AppHost host, IAiLibrary? ai = null, CanvasContext? canvas = null, CanvasWatch? watch = null) =>
        new(host, ai, canvas, watch);

    /// <summary>True while the constructor fills in what's already set: nothing is saved, and starting at login isn't
    /// touched, until the student changes something.</summary>
    readonly bool loading = true;
    /// <summary>Putting the box back after the login item couldn't be changed.</summary>
    bool settingLogin;

    SettingsModel(AppHost host, IAiLibrary? ai, CanvasContext? canvas, CanvasWatch? watch)
    {
        this.host = host;
        var cc = host.Client();
        ai ??= new AiRemote(cc.ServerUrl, cc.PoolKey);
        Engines = new AiEnginesModel(ai) { OpenUrl = url => Dialogs.OpenUrl(url) };
        AiProblems = new AiProblemsModel(ai);
        Access = MakeAccess(ai, host);
        Canvas = new CanvasSettingsModel(canvas ?? CanvasContext.For(host), watch);
        Address = cc.ServerUrl;
        DisplayName = cc.DisplayName;
        LibraryHere = host.Settings.LibraryHere;
        Language = host.Settings.Language;
        ComputerAudio = host.Settings.ComputerAudio;
        KeepAudio = host.Settings.KeepAudioDays.ToString(System.Globalization.CultureInfo.InvariantCulture);
        Shortcuts = host.Settings.Shortcuts;
        StartAtLogin = host.LoginItems.StartsAtLogin(host.Home);
        ColourTheme = host.Settings.Theme;
        // Whisper tiny is only for trying things out: listed only when it's the one in use.
        foreach (var m in WhisperModels.All.Where(m => m.Id != WhisperModels.Tiny.Id || m.Id == host.Model.Id))
            Models.Add(new ModelChoice { Model = m, Chosen = m.Id == host.Model.Id, Here = WhisperModels.IsDownloaded(host.Home, m) });
        foreach (var c in host.Timetable.Classes)
            Rows.Add(new TimetableRow { Name = c.Name, Times = string.Join(", ", c.Times.Select(t => t.Describe())), Dot = Skin.ClassDot(Math.Max(0, host.ColorOf(c.Name))) });
        foreach (var (name, color, _) in host.Classes().Where(c => Rows.All(r => r.Name != c.Name)))
            Rows.Add(new TimetableRow { Name = name, Dot = Skin.ClassDot(color) });
        host.Changed += OnHostChanged;
        foreach (var n in NavItems) n.On = n.Id == Section;
        foreach (var t in Themes) t.Chosen = t.Name == ColourTheme;
        loading = false;
        Refresh();
    }

    /// <summary>AI tool access, with this computer's own Claude Code, Claude Desktop and Codex setup, and the
    /// library's Claude routes for turning on the web address and removing a connection.</summary>
    static AiAccessModel MakeAccess(IAiLibrary ai, AppHost host)
    {
        var setup = ClaudeSetup.ThisComputer(host.Home);
        return new AiAccessModel(ai)
        {
            ClaudeCodeCommand = setup.ClaudeCodeCommand,
            CodexSetup = setup.CodexSetup,
            McpJson = setup.McpJson,
            CheckInClaudeCode = setup.InClaudeCode,
            CheckInClaudeDesktop = setup.InClaudeDesktop,
            AddToClaudeDesktop = () => Task.FromResult(setup.AddToClaudeDesktop()),
            RemoveFromClaudeDesktopHook = () => Task.FromResult(setup.RemoveFromClaudeDesktop()),
            TurnOnWeb = async () =>
            {
                if (host.Remote() is not { } lib) return null;
                var r = await lib.ClaudeAsync(HttpMethod.Post, "/reach", new JsonObject { ["internet"] = true, ["on"] = true });
                return r?["public_url"]?.GetValue<string>();
            },
            RevokeConnection = async id =>
            {
                if (host.Remote() is not { } lib) return false;
                return await lib.ClaudeAsync(HttpMethod.Delete, "/connections/" + Uri.EscapeDataString(id)) is not null;
            },
        };
    }

    /// <summary>The panes that read the library do so when they're opened (and each time again), not before.</summary>
    void LoadSection(string section)
    {
        switch (section)
        {
            case "AI":
                _ = Engines.Load();
                _ = AiProblems.Load();
                break;
            case "Access":
                _ = Access.Load();
                break;
            case "Canvas":
                _ = Canvas.LoadAsync();
                break;
        }
    }

    void OnHostChanged() => Dispatcher.UIThread.Post(Refresh);

    void Refresh()
    {
        var cc = host.Client();
        LibraryLine = host.Library switch
        {
            LibraryState.Connected when host.OlderLibrary => $"Connected to {cc.PoolName}. It runs an older Study Stash: update it to browse, search and ask from here.",
            LibraryState.Connected => $"Connected to {cc.PoolName} at {cc.ServerUrl}.",
            LibraryState.Starting => "Starting your library…",
            LibraryState.Unreachable => $"Can't reach {cc.ServerUrl} right now. Lectures wait here until it's back.",
            LibraryState.WrongPassword => "The library's password changed. Type the new one below.",
            _ => "No library yet.",
        };
        string device = OperatingSystem.IsMacOS() ? "Mac" : "PC";
        LibraryServiceLine = host.Settings.Role == AppRole.Laptop ? "" : host.LocalLibrary?.State switch
        {
            LibraryServiceState.Running => $"Your library runs on this {device}, on port {host.LocalLibrary.Cfg.WebPort}.",
            LibraryServiceState.Starting => "Starting your library…",
            LibraryServiceState.Elsewhere => $"A library is already running on this {device}.",
            LibraryServiceState.PortTaken => host.LocalLibrary.Failure ?? "Its port is taken by another program.",
            LibraryServiceState.Failed => $"It stopped: {host.LocalLibrary.Failure}",
            _ => "Stopped.",
        };
        OnPropertyChanged(nameof(CanStartLibrary));
        OnPropertyChanged(nameof(CanStopLibrary));
        ShortcutsSay = Shell.ShortcutsSay();
        ModelLine = ModelWords(host);
        foreach (var m in Models)
        {
            m.Chosen = m.Model.Id == host.Model.Id;
            m.Here = WhisperModels.IsDownloaded(host.Home, m.Model);
        }
    }

    /// <summary>Settings → Recording's line about the model: ready, "Downloading Whisper large-v3: 1.9 GB of 3.1 GB.
    /// About 4 minutes left.", why the download stopped, or not downloaded yet.</summary>
    public static string ModelWords(AppHost host)
    {
        if (host.ModelReady) return $"{host.Model.Name} is ready.";
        if (host.DownloadProblem is { } problem) return problem;
        if (host.Downloading is { } d)
            return $"Downloading {(host.DownloadingModel ?? host.Model).Name}: {d.Amount}." + (d.Left() is { } left ? $" {left}." : "");
        return $"{host.Model.Name} isn't downloaded yet.";
    }

    partial void OnSectionChanged(string value)
    {
        foreach (string p in new[] { nameof(OnLibrary), nameof(OnRecording), nameof(OnClasses), nameof(OnAi), nameof(OnCanvas), nameof(OnAccess), nameof(OnPlainPage), nameof(OnGeneral), nameof(OnAppearance) })
            OnPropertyChanged(p);
        foreach (var n in NavItems) n.On = n.Id == value;
        LoadSection(value);
    }

    partial void OnColourThemeChanged(string value)
    {
        if (!loading) host.Save(s => s.Theme = value);
        Skin.UseTheme(ColourThemes.Find(value));
        foreach (var t in Themes) t.Chosen = t.Name == value;
    }

    [RelayCommand] void PickTheme(string name) => ColourTheme = name;

    partial void OnLanguageChanged(string value)
    {
        if (!loading) host.Save(s => s.Language = value.Trim());
    }

    partial void OnComputerAudioChanged(bool value)
    {
        if (!loading) host.Save(s => s.ComputerAudio = value);
    }

    partial void OnShortcutsChanged(bool value)
    {
        if (!loading) host.Save(s => s.Shortcuts = value);
    }

    /// <summary>Only the student's own tick adds (or takes away) the login item.</summary>
    partial void OnStartAtLoginChanged(bool value)
    {
        if (loading || settingLogin) return;
        try
        {
            host.LoginItems.StartAtLogin(value, host.Home);
        }
        catch (Exception e) when (e is InvalidOperationException or IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            host.Log($"[app] start at login: {e.Message}");
            settingLogin = true;
            StartAtLogin = !value;
            settingLogin = false;
        }
    }

    partial void OnKeepAudioChanged(string value)
    {
        if (!loading && int.TryParse(value, out int days) && days >= 0) host.Save(s => s.KeepAudioDays = days);
    }

    partial void OnDisplayNameChanged(string value)
    {
        if (loading) return;
        var cc = host.Client();
        if (cc.DisplayName == value.Trim()) return;
        cc.DisplayName = value.Trim();
        host.SaveClient(cc);
    }

    [RelayCommand] void Go(string section) => Section = section;

    [RelayCommand]
    async Task Connect()
    {
        LibrarySay = "Connecting…";
        string url = Setup.NormalizeAddress(Address);
        try
        {
            var health = await LibraryApi.CheckServerAsync(url, Password.Trim());
            var cc = host.Client();
            cc.ServerUrl = url;
            cc.PoolKey = Password.Trim();
            cc.PoolName = health["pool_name"]?.GetValue<string>() ?? "";
            host.SaveClient(cc);
            Address = url;
            Password = "";
            LibrarySay = $"Connected to {cc.PoolName}.";
            await host.CheckLibraryAsync();
        }
        catch (InvalidOperationException e)
        {
            LibrarySay = e.Message == "wrong password" ? "That password isn't right." : "Can't reach that address. Is the library's computer on, and Tailscale connected?";
        }
    }

    [RelayCommand]
    void OpenLibraryPage()
    {
        var cc = host.Client();
        if (cc.ServerUrl.Length > 0) Dialogs.OpenUrl(cc.ServerUrl);
    }

    /// <summary>Start this computer's own library again (after Stop, or a problem such as a taken port).</summary>
    [RelayCommand]
    void StartLibrary() => _ = host.RefreshLocalLibraryAsync();

    /// <summary>Stop this computer's own library. Only the one we started or adopted: never a library from elsewhere.</summary>
    [RelayCommand]
    async Task StopLibrary()
    {
        if (host.LocalLibrary is { } svc) await svc.StopAsync();
    }

    /// <summary>A plain laptop decides, from Settings, to also run the library on this computer.</summary>
    [RelayCommand]
    async Task MakeThisTheLibrary()
    {
        LibrarySay = "Starting your library…";
        try
        {
            string done = await Services.LibraryHere.ThisComputer().CreateAsync(host, $"{DisplayName}'s library", StudyStash.Library.Http.TokenUrlSafe(12), DisplayName);
            LibraryHere = true;
            LibrarySay = done;
        }
        catch (Exception e) when (e is InvalidOperationException or ArgumentException)
        {
            LibrarySay = e.Message;
        }
    }

    [RelayCommand]
    void PickModel(ModelChoice choice)
    {
        host.Save(s => s.Model = choice.Model.Id);
        Refresh();
        // One already here needs nothing, and a download of another one is no longer wanted.
        if (WhisperModels.IsDownloaded(host.Home, choice.Model)) host.StopDownload();
        else _ = host.DownloadModelAsync(choice.Model);
    }

    /// <summary>Download (or try again now).</summary>
    [RelayCommand] void DownloadModel() => _ = host.DownloadModelAsync();

    /// <summary>Whisper couldn't start with the model: throw it away and download it again.</summary>
    [RelayCommand] void RedownloadModel() => _ = host.RedownloadModel();

    [RelayCommand]
    async Task AddClass()
    {
        string name = NewClass.Trim();
        if (name.Length == 0) return;
        if (NewTimes.Trim().Length > 0 && ClassTime.ParseMany(NewTimes) is null)
        {
            ClassesSay = "Write the days and times like “Tue Thu 10:00–11:15” or “MWF 9–9:50”.";
            return;
        }
        if (host.Remote() is { } lib && !host.OlderLibrary)
        {
            try
            {
                await lib.AddClassAsync(name);
                await host.CheckLibraryAsync();
            }
            catch (Exception e) when (e is HttpRequestException or TaskCanceledException or LibraryRefusedException)
            {
                ClassesSay = "The library didn't take the class; it's in your timetable here.";
            }
        }
        Rows.Add(new TimetableRow { Name = name, Times = NewTimes.Trim(), Dot = Skin.ClassDot(Math.Max(0, host.ColorOf(name))) });
        NewClass = NewTimes = "";
        SaveTimetable();
    }

    [RelayCommand]
    void RemoveClass(TimetableRow row)
    {
        Rows.Remove(row);
        SaveTimetable();
    }

    [RelayCommand]
    void SaveTimetable()
    {
        var t = new Timetable();
        bool bad = false;
        foreach (var r in Rows)
        {
            var times = r.Times.Trim().Length == 0 ? [] : ClassTime.ParseMany(r.Times.Replace(",", " "));
            r.Bad = times is null;
            bad |= r.Bad;
            if (times is { Count: > 0 }) t.Classes.Add(new TimetableClass(r.Name, times));
        }
        host.SaveTimetable(t);
        ClassesSay = bad ? "Some times couldn't be read (marked): write them like “Tue Thu 10:00–11:15”." : "Saved.";
    }

    [RelayCommand] static void Quit() => Shell.Quit();

    public void Dispose()
    {
        host.Changed -= OnHostChanged;
        Canvas.Dispose();
    }
}
