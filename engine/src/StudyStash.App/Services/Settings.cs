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

/// <summary>A Whisper model in setup and Settings: its name, size, what it's for, whether it's the one for this
/// computer, and whether it's here.</summary>
public sealed partial class ModelChoice : ObservableObject
{
    public required WhisperModel Model { get; init; }
    public string Name => Model.Name;
    /// <summary>"574 MB", "3 GB": as setup says it.</summary>
    public string Size => Setup.About(Model.Bytes);
    public string About => $"{Model.Size}. {Model.About}";
    /// <summary>The model that keeps up with a lecture on this computer.</summary>
    public bool Recommended { get; init; }
    /// <summary>Why it's the one for this computer (the recommended one only).</summary>
    public string Why { get; init; } = "";
    public bool HasWhy => Why.Length > 0;
    /// <summary>Its line under the name: why it suits this computer, or what it's for.</summary>
    public string Line => Why.Length > 0 ? Why : Model.About;
    [ObservableProperty] public partial bool Chosen { get; set; }
    [ObservableProperty] public partial bool Here { get; set; }

    /// <summary>The models to offer, the one in use (<paramref name="chosen"/>) marked: Whisper tiny is only for
    /// trying things out, so it's there only when it's the one in use.</summary>
    public static IEnumerable<ModelChoice> For(WhisperModel chosen, ModelAdvice advice, string home) =>
        WhisperModels.All.Where(m => m.Id != WhisperModels.Tiny.Id || m.Id == chosen.Id).Select(m => new ModelChoice
        {
            Model = m,
            Recommended = m.Id == advice.Model.Id,
            Why = m.Id == advice.Model.Id ? advice.Why : "",
            Chosen = m.Id == chosen.Id,
            Here = WhisperModels.IsDownloaded(home, m),
        });
}

/// <summary>One row in the settings sidebar: its section id, icon and label, and whether it's the one showing.</summary>
public sealed partial class NavItem : ObservableObject
{
    public required string Id { get; init; }
    public required string Glyph { get; init; }
    public required string Label { get; init; }
    [ObservableProperty] public partial bool On { get; set; }
}

/// <summary>One choice in the Appearance mode picker: "Match system", "Light" or "Dark", and whether it's the one in
/// use.</summary>
public sealed partial class AppearanceOption : ObservableObject
{
    public required AppAppearance Mode { get; init; }
    public required string Label { get; init; }
    [ObservableProperty] public partial bool Chosen { get; set; }
}

/// <summary>
/// Settings, in two groups. This laptop (or this Mac/PC): General, Appearance (the colour theme), Recording (the
/// model, language, the computer's sound, how long audio stays) and Connection (which library; this computer's own).
/// Your library, changed through its API: Library (name, password, how laptops reach
/// it, start at login, updates), Classes, Notes and sorting, AI engines (who writes the notes and answers questions),
/// AI tool access (what Claude Code, Codex and other MCP tools may read), Canvas, and the Folders it may read.
/// </summary>
public sealed partial class SettingsModel : ObservableObject, IDisposable
{
    readonly AppHost host;

    [ObservableProperty] public partial string Section { get; set; } = "Connection";

    /// <summary>The sidebar's first group: this computer's own settings (General, Appearance, then recording and the
    /// connection to the library; a library-only computer doesn't record, so it has no Recording).</summary>
    public IReadOnlyList<NavItem> ComputerNav { get; }

    /// <summary>The sidebar's second group: the library's own settings, read and changed through its API, so its web
    /// page is never needed.</summary>
    public IReadOnlyList<NavItem> LibraryNav { get; } =
    [
        new() { Id = "Library", Glyph = "dns", Label = "Library" },
        new() { Id = "Classes", Glyph = "book_2", Label = "Classes" },
        new() { Id = "Notes", Glyph = "edit_note", Label = "Notes and sorting" },
        new() { Id = "AI", Glyph = "auto_awesome", Label = "AI engines" },
        new() { Id = "Access", Glyph = "hub", Label = "AI tool access" },
        new() { Id = "Canvas", Glyph = "school", Label = "Canvas" },
        new() { Id = "Folders", Glyph = "folder", Label = "Folders" },
        new() { Id = "Phone", Glyph = "smartphone", Label = "Phone" },
    ];

    /// <summary>Every row of the sidebar, both groups.</summary>
    public IEnumerable<NavItem> NavItems => ComputerNav.Concat(LibraryNav);

    /// <summary>The first group's heading: "This laptop" on a laptop, this Mac or PC where the library runs.</summary>
    public string ComputerNavTitle { get; }

    /// <summary>Whether this window draws the Mac's round swatches or Windows' squared ones.</summary>
    public bool ShowMacSwatch => Skin.Current == SkinKind.Mac;

    // Appearance
    public IReadOnlyList<ThemeSwatch> Themes { get; } = [.. ColourThemes.All.Select(t => new ThemeSwatch(t))];
    [ObservableProperty] public partial string ColourTheme { get; set; } = "";
    public IReadOnlyList<AppearanceOption> AppearanceOptions { get; } =
    [
        new() { Mode = AppAppearance.System, Label = "Match system" },
        new() { Mode = AppAppearance.Light, Label = "Light" },
        new() { Mode = AppAppearance.Dark, Label = "Dark" },
    ];
    [ObservableProperty] public partial AppAppearance Appearance { get; set; } = AppAppearance.System;
    public bool AppearanceIsSystem => Appearance == AppAppearance.System;
    public bool AppearanceIsLight => Appearance == AppAppearance.Light;
    public bool AppearanceIsDark => Appearance == AppAppearance.Dark;

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
    /// <summary>Why the recommended model suits this computer, and, when the one in use is heavier, that it may fall
    /// behind.</summary>
    [ObservableProperty] public partial string ModelAdviceLine { get; set; } = "";
    /// <summary>A model is downloading: the bar under the list shows how far.</summary>
    [ObservableProperty] public partial bool ModelDownloading { get; set; }
    [ObservableProperty] public partial double ModelProgress { get; set; }
    public double ModelBarWidth => Math.Clamp(ModelProgress, 0, 1) * 480;
    partial void OnModelProgressChanged(double value) => OnPropertyChanged(nameof(ModelBarWidth));
    readonly ModelAdvice advice;
    /// <summary>Models downloaded here but not in use (after a switch): offered for removal, never removed unasked.</summary>
    [ObservableProperty] public partial string SpareLine { get; set; } = "";
    public bool HasSpare => SpareLine.Length > 0 && !ConfirmingRemove;
    /// <summary>Remove was pressed: the question, with Remove and Keep them.</summary>
    [ObservableProperty] public partial bool ConfirmingRemove { get; set; }
    public string RemoveQuestion => spare.Count switch
    {
        0 => "",
        1 => $"Remove {spare[0].Name} from this computer? It frees {WhisperModel.SizeOf(spare[0].Bytes)}, and you can download it again any time.",
        _ => $"Remove {string.Join(" and ", spare.Select(m => m.Name))} from this computer? They free {WhisperModel.SizeOf(spare.Sum(m => m.Bytes))}, and you can download them again any time.",
    };
    partial void OnSpareLineChanged(string value) => OnPropertyChanged(nameof(HasSpare));
    partial void OnConfirmingRemoveChanged(bool value)
    {
        OnPropertyChanged(nameof(HasSpare));
        OnPropertyChanged(nameof(RemoveQuestion));
    }
    List<WhisperModel> spare = [];
    [ObservableProperty] public partial string Language { get; set; } = "";
    [ObservableProperty] public partial bool ComputerAudio { get; set; }
    [ObservableProperty] public partial string KeepAudio { get; set; } = "30";
    [ObservableProperty] public partial bool Shortcuts { get; set; }
    /// <summary>Which shortcut (if either) another app already has, once the toggle's had a moment to try them.</summary>
    [ObservableProperty] public partial string? ShortcutsSay { get; set; }
    public bool CanRecordComputerAudio => host.CanRecordComputerAudio;

    // AI engines, AI tool access and Canvas: each is its own pane over its own model, read when it's opened.
    public AiEnginesModel Engines { get; }
    /// <summary>What's wrong with an engine right now (offline, signed out, a model missing, a usage limit), above
    /// the engines.</summary>
    public AiProblemsModel AiProblems { get; }
    public AiAccessModel Access { get; }
    public CanvasSettingsModel Canvas { get; }
    /// <summary>The library's own settings (Library, Classes, Notes and sorting, Folders).</summary>
    public LibrarySettingsModel Lib { get; }
    /// <summary>Adding a phone to the library, and the phones already added.</summary>
    public PhonesModel Phones { get; private set; }
    public bool OnPhone => Section == "Phone";

    /// <summary>For a test or a shot: Phone asks <paramref name="phones"/> instead of the connected library.</summary>
    public SettingsModel WithPhones(Func<LibrarySettingsModel.Call?> phones)
    {
        Phones = new PhonesModel(phones);
        return this;
    }

    // General
    [ObservableProperty] public partial bool StartAtLogin { get; set; }
    public string Version => Engine.Version;

    public bool OnConnection => Section == "Connection";
    public bool OnRecording => Section == "Recording";
    public bool OnLibrary => Section == "Library";
    public bool OnClasses => Section == "Classes";
    public bool OnNotes => Section == "Notes";
    public bool OnFolders => Section == "Folders";
    /// <summary>One of the library's own pages, which share a heading and what to say when the library can't be reached.</summary>
    public bool OnLibraryPage => OnLibrary || OnClasses || OnNotes || OnFolders;
    /// <summary>The page's heading, above the library's own pages.</summary>
    public string LibraryPageTitle => Section switch { "Classes" => "Classes", "Notes" => "Notes and sorting", "Folders" => "Folders", _ => "Library" };
    public string LibraryPageLine => Section switch
    {
        "Classes" => "The classes your library files lectures into. It reads each lecture and picks the class it's about from these names and what each covers; a lecture you record for a class goes straight there.",
        "Notes" => "How your library writes the notes for each lecture and sorts it into a class.",
        "Folders" => "Folders on the library's computer that search, and the AI when you chat, may read.",
        _ => Lib.IsHere ? $"Your library, on this {(OperatingSystem.IsWindows() ? "PC" : "Mac")}." : "Your library, on the computer that keeps your lectures.",
    };
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
    public static SettingsModel Make(AppHost host, IAiLibrary? ai = null, CanvasContext? canvas = null, CanvasWatch? watch = null,
        Func<LibrarySettingsModel.Call?>? library = null) =>
        new(host, ai, canvas, watch, library);

    /// <summary>True while the constructor fills in what's already set: nothing is saved, and starting at login isn't
    /// touched, until the student changes something.</summary>
    readonly bool loading = true;
    /// <summary>Putting the box back after the login item couldn't be changed.</summary>
    bool settingLogin;

    SettingsModel(AppHost host, IAiLibrary? ai, CanvasContext? canvas, CanvasWatch? watch, Func<LibrarySettingsModel.Call?>? library)
    {
        this.host = host;
        bool records = host.Settings.Role != AppRole.Library;
        ComputerNav =
        [
            new() { Id = "General", Glyph = "tune", Label = "General" },
            new() { Id = "Appearance", Glyph = "palette", Label = "Appearance" },
            .. records ? new NavItem[] { new() { Id = "Recording", Glyph = "mic", Label = "Recording" } } : [],
            new() { Id = "Connection", Glyph = "link", Label = "Connection" },
        ];
        string device = OperatingSystem.IsWindows() ? "PC" : "Mac";
        ComputerNavTitle = host.Settings.Role == AppRole.Laptop ? "This laptop" : $"This {device}";
        Lib = new LibrarySettingsModel(library ?? (() => host.Remote() is { } lib ? (m, path, body) => lib.SettingsAsync(m, path, body) : null))
        {
            IsHere = host.Settings.LibraryHere,
            Renamed = name =>
            {
                var c = host.Client();
                c.PoolName = name;
                host.SaveClient(c);
            },
            PasswordChanged = password =>
            {
                var c = host.Client();
                c.PoolKey = password;
                host.SaveClient(c);
            },
            Reveal = dir => Machine.Open(dir),
            OpenPage = OpenLibraryPage,
            // Just this computer's library can let laptops in (or not); a library for other computers always does.
            LetLaptopsConnect = host.Settings.Role == AppRole.Both ? (on, password) => Services.LibraryHere.ThisComputer().LetLaptopsConnectAsync(host, on, password) : null,
            ClassesRenamed = renamed =>
            {
                foreach (var (from, to) in renamed) host.FollowRename(from, to);
            },
        };
        // A library-only computer opens on its library.
        if (!records) Section = "Library";
        var cc = host.Client();
        ai ??= new AiRemote(cc.ServerUrl, cc.PoolKey);
        Engines = new AiEnginesModel(ai)
        {
            OpenUrl = url => Dialogs.OpenUrl(url),
            Lede = "Notes are written after each lecture. Answers come while you ask. " + (host.Settings.Role == AppRole.Laptop
                ? "Engines run on your library, and you can set them up there or from this laptop."
                : $"Engines run on this {device}, as part of your library."),
        };
        AiProblems = new AiProblemsModel(ai);
        Access = MakeAccess(ai, host);
        Canvas = new CanvasSettingsModel(canvas ?? CanvasContext.For(host), watch);
        Phones = new PhonesModel(() => host.Remote() is { } phonesLib ? (m, path, body) => phonesLib.DevicesAsync(m, path, body) : null);
        Phones.Ticking = Phones.UiTicking();
        Address = cc.ServerUrl;
        DisplayName = cc.DisplayName;
        LibraryHere = host.Settings.LibraryHere;
        Language = host.Settings.Language;
        ComputerAudio = host.Settings.ComputerAudio;
        KeepAudio = host.Settings.KeepAudioDays.ToString(System.Globalization.CultureInfo.InvariantCulture);
        Shortcuts = host.Settings.Shortcuts;
        StartAtLogin = host.LoginItems.StartsAtLogin(host.Home);
        ColourTheme = host.Settings.Theme;
        Appearance = host.Settings.Appearance;
        advice = host.Advice;
        foreach (var m in ModelChoice.For(host.Model, advice, host.Home)) Models.Add(m);
        host.Changed += OnHostChanged;
        foreach (var n in NavItems) n.On = n.Id == Section;
        foreach (var t in Themes) t.Chosen = t.Name == ColourTheme;
        foreach (var o in AppearanceOptions) o.Chosen = o.Mode == Appearance;
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
            OpenUrl = url => Dialogs.OpenUrl(url),
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
            case "Library" or "Classes" or "Notes" or "Folders":
                _ = Lib.Load();
                break;
            case "Phone":
                _ = Phones.Load();
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
        ModelAdviceLine = AdviceWords(host.Model, advice);
        ModelDownloading = host.Downloading is not null;
        ModelProgress = host.Downloading?.Fraction ?? 0;
        spare = [.. WhisperModels.All.Where(m => m.Id != host.Model.Id && m.Id != host.DownloadingModel?.Id && WhisperModels.IsDownloaded(host.Home, m))];
        SpareLine = spare.Count == 0 ? ""
            : $"Also on this computer: {string.Join(", ", spare.Select(m => $"{m.Name} ({WhisperModel.SizeOf(m.Bytes)})"))}.";
        if (spare.Count == 0) ConfirmingRemove = false;
        OnPropertyChanged(nameof(RemoveQuestion));
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

    /// <summary>Settings → Recording's line about the model for this computer: why it suits it, and when the one in use
    /// is heavier, that it may fall behind (never a switch: the student picks).</summary>
    public static string AdviceWords(WhisperModel inUse, ModelAdvice advice) =>
        WhisperModels.Heavier(inUse, advice.Model)
            ? $"{advice.Why} {inUse.Name} may fall behind a lecture here: pick {advice.Model.Name} to switch. The one you have stays."
            : advice.Why;

    partial void OnSectionChanged(string value)
    {
        foreach (string p in new[]
                 {
                     nameof(OnConnection), nameof(OnRecording), nameof(OnLibrary), nameof(OnClasses), nameof(OnNotes), nameof(OnFolders),
                     nameof(OnLibraryPage), nameof(LibraryPageTitle), nameof(LibraryPageLine), nameof(OnAi), nameof(OnCanvas), nameof(OnAccess), nameof(OnPlainPage),
                     nameof(OnGeneral), nameof(OnAppearance), nameof(OnPhone),
                 })
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

    partial void OnAppearanceChanged(AppAppearance value)
    {
        if (!loading) host.Save(s => s.Appearance = value);
        Skin.UseAppearance(value);
        foreach (var o in AppearanceOptions) o.Chosen = o.Mode == value;
        OnPropertyChanged(nameof(AppearanceIsSystem));
        OnPropertyChanged(nameof(AppearanceIsLight));
        OnPropertyChanged(nameof(AppearanceIsDark));
    }

    [RelayCommand] void PickAppearance(AppAppearance mode) => Appearance = mode;

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
            // A library that was here before comes back with its own name, password and notes.
            bool had = Services.LibraryHere.Existing(host.Home) is not null;
            string done = await Services.LibraryHere.ThisComputer().CreateAsync(host, had ? null : $"{DisplayName}'s library", null, DisplayName);
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

    [RelayCommand] void AskRemoveSpare() => ConfirmingRemove = true;

    [RelayCommand] void KeepSpare() => ConfirmingRemove = false;

    /// <summary>The student said yes: remove the models not in use.</summary>
    [RelayCommand]
    void RemoveSpare()
    {
        var problems = spare.Select(host.RemoveModel).OfType<string>().ToList();
        ConfirmingRemove = false;
        Refresh();
        if (problems.Count > 0) ModelLine = string.Join(" ", problems);
    }

    /// <summary>Download (or try again now).</summary>
    [RelayCommand] void DownloadModel() => _ = host.DownloadModelAsync();

    /// <summary>Whisper couldn't start with the model: throw it away and download it again.</summary>
    [RelayCommand] void RedownloadModel() => _ = host.RedownloadModel();

    [RelayCommand] static void Quit() => Shell.Quit();

    public void Dispose()
    {
        host.Changed -= OnHostChanged;
        Canvas.Dispose();
    }
}
