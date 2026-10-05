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

    // This computer's role, changed after setup (RoleSwitch, docs/one-download.md)
    [ObservableProperty] public partial string RoleWords { get; set; } = "";
    public bool IsLaptopRole => host.Settings.Role == AppRole.Laptop;
    public bool IsLibraryRole => host.Settings.Role != AppRole.Laptop;
    /// <summary>"Use just this computer"'s line under its name: what it does, naming the library it would stop using.</summary>
    [ObservableProperty] public partial string JustThisComputerLine { get; set; } = "";
    [ObservableProperty] public partial bool ConfirmingBecomeLibrary { get; set; }
    [ObservableProperty] public partial string BecomeLibraryQuestion { get; set; } = "";
    [ObservableProperty] public partial bool ConfirmingBecomeLaptop { get; set; }
    [ObservableProperty] public partial string BecomeLaptopAddress { get; set; } = "";
    [ObservableProperty] public partial string BecomeLaptopPassword { get; set; } = "";
    [ObservableProperty] public partial string? SwitchSay { get; set; }
    [ObservableProperty] public partial bool Switching { get; set; }
    /// <summary>While a library is brought over (or handed on): what's happening now, "Bringing lectures over: 40 of
    /// 140…".</summary>
    [ObservableProperty] public partial string? BringLine { get; set; }
    /// <summary>How far that part has got, 0 to 1; below 0 while it can't say.</summary>
    [ObservableProperty] public partial double BringFraction { get; set; } = -1;
    public bool ShowBringBar => Switching && BringFraction >= 0;
    public double BringBarWidth => Math.Clamp(BringFraction, 0, 1) * 480;
    partial void OnBringFractionChanged(double value)
    {
        OnPropertyChanged(nameof(ShowBringBar));
        OnPropertyChanged(nameof(BringBarWidth));
    }
    partial void OnSwitchingChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowBringBar));
        OnPropertyChanged(nameof(ShowPendingBring));
    }
    /// <summary>This computer is the library, and an old library's lectures haven't all come over yet (bringing them
    /// stopped part-way, or setup made this the library): the row that says so, with Try again.</summary>
    [ObservableProperty] public partial bool HasPendingBring { get; set; }
    /// <summary>That row, except while something is being brought over (the progress line says how it's going).</summary>
    public bool ShowPendingBring => HasPendingBring && !Switching;
    partial void OnHasPendingBringChanged(bool value) => OnPropertyChanged(nameof(ShowPendingBring));
    [ObservableProperty] public partial string PendingBringTitle { get; set; } = "";
    [ObservableProperty] public partial string PendingBringLine { get; set; } = "";
    /// <summary>"Try again" once bringing them has been tried; "Bring them over" before.</summary>
    [ObservableProperty] public partial string PendingBringAction { get; set; } = "";

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
    /// <summary>When a lecture is written down: as it records (the recorder's transcript and chat), or after class
    /// (it only records, which saves battery). One setting, for the next lecture on.</summary>
    [ObservableProperty] public partial bool LiveTranscript { get; set; } = true;
    public bool AfterClass => !LiveTranscript;
    /// <summary>Under the choice, while a lecture records the other way: it stays as it started.</summary>
    [ObservableProperty] public partial string LiveTranscriptLine { get; set; } = "";
    [ObservableProperty] public partial bool ComputerAudio { get; set; }
    [ObservableProperty] public partial bool Speakers { get; set; }
    /// <summary>Under "Tell speakers apart": what it does, or where its model is.</summary>
    [ObservableProperty] public partial string SpeakersLine { get; set; } = "";
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
    /// <summary>The calendar feeds and sources connected on this computer, and which of their calendars are shown.</summary>
    public CalendarSettingsModel Calendars { get; }
    /// <summary>The library's own settings (Library, Classes, Notes and sorting, Folders).</summary>
    public LibrarySettingsModel Lib { get; }
    /// <summary>Adding a phone to the library, and the phones already added.</summary>
    public PhonesModel Phones { get; private set; }
    /// <summary>Settings → Shortcuts: the keys for search, Record, Ask and Settings.</summary>
    public ShortcutsModel Keys { get; }
    public bool OnShortcuts => Section == "Shortcuts";
    public bool OnPhone => Section == "Phone";

    /// <summary>Making (or finding) the library on this computer: <see cref="Services.LibraryHere.ThisComputer"/>
    /// unless a test gives its own (a built engine to run, and its own notes folder).</summary>
    Func<LibraryHere> libraryHere = Services.LibraryHere.ThisComputer;

    /// <summary>For a test or a shot: Phone asks <paramref name="phones"/> instead of the connected library.</summary>
    public SettingsModel WithPhones(Func<LibrarySettingsModel.Call?> phones)
    {
        Phones = new PhonesModel(phones);
        return this;
    }

    /// <summary>For a test: making this computer the library uses <paramref name="here"/> (a built engine to run)
    /// instead of the app's own copy of itself.</summary>
    public SettingsModel WithLibraryHere(Func<LibraryHere> here)
    {
        libraryHere = here;
        return this;
    }

    /// <summary>How bringing a library over (or handing one on) reaches each library: the network, unless a test gives
    /// its own (an old library "on another computer" that's really on this one).</summary>
    Func<string, HttpClient>? libraryHttp;

    /// <summary>For a test: bringing a library over reaches each address through <paramref name="http"/>.</summary>
    public SettingsModel WithLibraryHttp(Func<string, HttpClient> http)
    {
        libraryHttp = http;
        return this;
    }

    // General
    [ObservableProperty] public partial bool StartAtLogin { get; set; }
    public string Version => Engine.Version;

    // This laptop's own updates (General → Updates, a laptop only: on a computer that is the library the one switch and
    // Update now are Your library → Library's, which update the app and the library together). client.toml's
    // auto_update, which the app reads each round; the library's own is a different switch on its own page.
    [ObservableProperty] public partial string UpdateHereLine { get; set; } = "";
    /// <summary>Check now found a newer version this copy can install: Update now shows.</summary>
    [ObservableProperty] public partial bool CanUpdateHere { get; set; }
    [ObservableProperty] public partial bool UpdateHereBusy { get; set; }
    [ObservableProperty] public partial bool AutoUpdateHere { get; set; }
    public string AutoUpdateHereSub => "Study Stash on this laptop installs each new version by itself, never while you're recording. Your library has its own switch under Your library → Library.";
    Func<AppUpdates?> updater = () => AppUpdates.Current;

    /// <summary>For a test: Check now and Update now go through <paramref name="updates"/>, not the running app's.</summary>
    public SettingsModel WithUpdater(AppUpdates updates)
    {
        updater = () => updates;
        return this;
    }

    /// <summary>Hands a web page to the system's browser; a test catches it here instead.</summary>
    public Action<string> OpenUrl { get; set; } = url => Dialogs.OpenUrl(url);

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
    public bool OnCalendars => Section == "Calendars";
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
            new() { Id = "Shortcuts", Glyph = "keyboard", Label = "Shortcuts" },
            .. records ? new NavItem[] { new() { Id = "Recording", Glyph = "mic", Label = "Recording" } } : [],
            .. records ? new NavItem[] { new() { Id = "Calendars", Glyph = "event", Label = "Calendars" } } : [],
            new() { Id = "Connection", Glyph = "link", Label = "Connection" },
        ];
        string device = OperatingSystem.IsWindows() ? "PC" : "Mac";
        ComputerNavTitle = host.Settings.Role == AppRole.Laptop ? "This laptop" : $"This {device}";
        Lib = new LibrarySettingsModel(library ?? (() => host.Remote() is { } lib ? (m, path, body) => lib.SettingsAsync(m, path, body) : null))
        {
            IsHere = host.Settings.LibraryHere,
            UpdateHere = AppUpdates.Current is { } updates ? async () => NoticeWords.UpdateNowLine(await Task.Run(updates.NowAsync)) : null,
            AppAutoUpdate = () => host.Client().AutoUpdate,
            AppAutoUpdateChanged = on =>
            {
                var c = host.Client();
                c.AutoUpdate = on;
                host.SaveClient(c);
            },
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
        Canvas = new CanvasSettingsModel(canvas ?? CanvasContext.For(host), watch) { OnSetUpExtension = () => Shell.ShowCanvasConnect() };
        Calendars = new CalendarSettingsModel(host.Home, wake: () => host.Calendars.Wake());
        Phones = new PhonesModel(() => host.Remote() is { } phonesLib ? (m, path, body) => phonesLib.DevicesAsync(m, path, body) : null);
        Keys = new ShortcutsModel(() => host.Settings.Keys, change => host.Save(s => change(s.Keys)), Skin.Current == SkinKind.Mac, records);
        Phones.Ticking = Phones.UiTicking();
        Address = cc.ServerUrl;
        DisplayName = cc.DisplayName;
        LibraryHere = host.Settings.LibraryHere;
        Language = host.Settings.Language;
        LiveTranscript = host.Settings.LiveTranscript;
        ComputerAudio = host.Settings.ComputerAudio;
        Speakers = host.Settings.Speakers;
        SpeakersLine = SpeakersWords(host);
        KeepAudio = host.Settings.KeepAudioDays.ToString(System.Globalization.CultureInfo.InvariantCulture);
        Shortcuts = host.Settings.Shortcuts;
        StartAtLogin = host.LoginItems.StartsAtLogin(host.Home);
        AutoUpdateHere = cc.AutoUpdate;
        UpdateHereLine = $"Study Stash {Version} on this laptop";
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

    /// <summary>AI tool access, with the AI apps on this computer (Claude Desktop, Claude Code, Codex, Gemini CLI: each
    /// connected to the library with one click), and the library's Claude routes for turning on the web address and
    /// removing a connection.</summary>
    static AiAccessModel MakeAccess(IAiLibrary ai, AppHost host)
    {
        var setup = ClaudeSetup.ThisComputer(host.Home);
        return new AiAccessModel(ai)
        {
            McpJson = setup.McpJson,
            ReadApps = setup.States,
            ConnectApp = setup.Connect,
            DisconnectApp = setup.Disconnect,
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
            case "Calendars":
                Calendars.Load();
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
        bool runsHere = host.Settings.Role != AppRole.Laptop && host.LocalLibrary?.State == LibraryServiceState.Running;
        LibraryLine = host.Library switch
        {
            LibraryState.Connected when runsHere && !host.OlderLibrary =>
                $"{cc.PoolName} runs on this {(OperatingSystem.IsMacOS() ? "Mac" : "PC")}, on port {host.LocalLibrary!.Cfg.WebPort}.",
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
            // Running and answering: the line above says so.
            LibraryServiceState.Running when host.Library == LibraryState.Connected => "",
            LibraryServiceState.Running => $"Your library runs on this {device}, on port {host.LocalLibrary.Cfg.WebPort}.",
            LibraryServiceState.Starting => "Starting your library…",
            LibraryServiceState.Elsewhere => $"A library is already running on this {device}.",
            LibraryServiceState.PortTaken => host.LocalLibrary.Failure ?? "Its port is taken by another program.",
            LibraryServiceState.Failed => $"It stopped: {host.LocalLibrary.Failure}",
            _ => "Stopped.",
        };
        var old = host.Settings.Role == AppRole.Laptop ? RoleSwitch.OldLibraryOf(host) : null;
        RoleWords = host.Settings.Role switch
        {
            AppRole.Laptop when old is not null => $"This {device} is a laptop: it records lectures and sends them to {OldName(old)} on {RoleSwitch.ComputerOf(old.Url)}.",
            AppRole.Laptop => $"This {device} is a laptop: it records lectures and sends them to your library.",
            AppRole.Library => $"This {device} is your library: it keeps and writes up lectures, but doesn't record its own.",
            _ => $"This {device} records lectures and is your library, all in one place.",
        };
        JustThisComputerLine = old is null
            ? $"Keep your notes and classes on this {device}: it becomes your library and keeps recording."
            : $"Stop using {OldName(old)} on {RoleSwitch.ComputerOf(old.Url)}: your classes and notes come to this {device}, which becomes your library and keeps recording. Nothing is deleted there.";
        RefreshPending();
        OnPropertyChanged(nameof(IsLaptopRole));
        OnPropertyChanged(nameof(IsLibraryRole));
        OnPropertyChanged(nameof(CanStartLibrary));
        OnPropertyChanged(nameof(CanStopLibrary));
        ShortcutsSay = Shell.ShortcutsSay();
        ModelLine = ModelWords(host);
        SpeakersLine = SpeakersWords(host);
        LiveTranscriptLine = LiveTranscriptWords(host.Recorder.Current, host.Settings.LiveTranscript);
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
                     nameof(OnGeneral), nameof(OnAppearance), nameof(OnShortcuts), nameof(OnPhone), nameof(OnCalendars),
                 })
            OnPropertyChanged(p);
        foreach (var n in NavItems) n.On = n.Id == value;
        // A library on this computer starts at login through the same login item as the app (Your library → Start the
        // library when this Mac starts): General shows it as it is now, not as it was when Settings opened.
        if (value == "General" && !loading)
        {
            settingLogin = true;
            StartAtLogin = host.LoginItems.StartsAtLogin(host.Home);
            settingLogin = false;
        }
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

    [RelayCommand] void PickLiveTranscript(string when) => LiveTranscript = when == "live";

    partial void OnLiveTranscriptChanged(bool value)
    {
        OnPropertyChanged(nameof(AfterClass));
        if (loading) return;
        host.Save(s => s.LiveTranscript = value);
        LiveTranscriptLine = LiveTranscriptWords(host.Recorder.Current, value);
    }

    /// <summary>The line under "When it's written down": nothing, unless the lecture recording now started the other
    /// way, which it keeps (a lecture is written down one way, start to end).</summary>
    public static string LiveTranscriptWords(Lecture? recording, bool live) => recording switch
    {
        { AfterClass: true } when live => "The lecture recording now is still written down after class. The next one is written down as you record.",
        { AfterClass: false } when !live => "The lecture recording now is still written down as you record. The next one is written down after class.",
        _ => "",
    };

    partial void OnComputerAudioChanged(bool value)
    {
        if (!loading) host.Save(s => s.ComputerAudio = value);
    }

    partial void OnSpeakersChanged(bool value)
    {
        if (loading) return;
        host.Save(s => s.Speakers = value);
        host.EnsureSpeakerModel();
        SpeakersLine = SpeakersWords(host);
    }

    /// <summary>The line under "Tell speakers apart": what it does while it's off, and once it's on, whether its model is
    /// here, coming, or waiting for the transcription model's download to end.</summary>
    public static string SpeakersWords(AppHost host)
    {
        string size = WhisperModel.SizeOf(WhisperModels.Speakers.Bytes);
        if (!host.Settings.Speakers)
            return $"Marks in the transcript where a voice other than the lecturer's seems to speak (\"Speaker 2:\"), so a student's question isn't taken for the lecturer's words. Experimental: it can be wrong. Done on this computer after the lecture; needs a {size} download.";
        if (host.SpeakerModelReady) return "On. Each lecture's voices are told apart after it's written down, on this computer. A lecture under 2 minutes or over 3 hours is left alone.";
        if (host.DownloadProblem is { } problem && host.DownloadingModel?.Id == WhisperModels.Speakers.Id) return problem;
        if (host.Downloading is { } d && host.DownloadingModel?.Id == WhisperModels.Speakers.Id) return $"Downloading the voice model: {d.Amount}.";
        return $"On. The voice model ({size}) downloads once the transcription model is here.";
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

    partial void OnAutoUpdateHereChanged(bool value)
    {
        if (loading) return;
        var c = host.Client();
        c.AutoUpdate = value;
        host.SaveClient(c);
    }

    /// <summary>Check now: is a newer version out for this laptop? Says so without installing it.</summary>
    [RelayCommand]
    async Task CheckUpdateHere()
    {
        if (UpdateHereBusy) return;
        if (updater() is not { } updates)
        {
            UpdateHereLine = NoticeWords.UpdateNowLine(AppUpdates.Off);
            return;
        }
        UpdateHereBusy = true;
        UpdateHereLine = "Looking for a new version…";
        try
        {
            (UpdateHereLine, CanUpdateHere) = NoticeWords.UpdateChecked(await Task.Run(updates.CheckAsync), Version);
        }
        finally
        {
            UpdateHereBusy = false;
        }
    }

    /// <summary>Update now: this laptop installs the newest version (once a lecture being recorded is over) and opens again.</summary>
    [RelayCommand]
    async Task UpdateHereNow()
    {
        if (UpdateHereBusy || updater() is not { } updates) return;
        UpdateHereBusy = true;
        CanUpdateHere = false;
        UpdateHereLine = "Downloading the new version…";
        try
        {
            UpdateHereLine = NoticeWords.UpdateNowLine(await Task.Run(updates.NowAsync));
        }
        finally
        {
            UpdateHereBusy = false;
        }
    }

    partial void OnKeepAudioChanged(string value)
    {
        if (loading || !int.TryParse(value, out int days) || days < 0) return;
        host.Save(s => s.KeepAudioDays = days);
        host.PruneAudioSoon();
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
        string url = Setup.NormalizeAddress(Address);
        // This computer is the library: sending to another one instead would hide its lectures here, so that's
        // Become a laptop, which hands everything over first.
        if (host.Settings.Role != AppRole.Laptop && url.Length > 0 && RoleSwitch.Elsewhere(host, url))
        {
            BecomeLaptopAddress = url;
            BecomeLaptopPassword = Password;
            LibrarySay = $"This {Device} is your library. To use the one at {url} instead, press Become a laptop under This computer: everything here goes there first, so nothing is left behind.";
            return;
        }
        LibrarySay = "Connecting…";
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

    static string Device => OperatingSystem.IsWindows() ? "PC" : "Mac";

    static string OldName(OldLibrary old) => old.Name.Length > 0 ? old.Name : "your library";

    /// <summary>The library pages' way in for a laptop: Connection, with "Use just this computer" asked.</summary>
    [RelayCommand]
    void GoJustThisComputer()
    {
        Section = "Connection";
        AskBecomeLibrary();
    }

    /// <summary>"Use just this computer": a laptop asks to keep everything on this computer, which becomes the library
    /// too (and keeps recording), bringing over the library it used. It says what will happen before anything does.</summary>
    [RelayCommand]
    void AskBecomeLibrary()
    {
        var old = RoleSwitch.OldLibraryOf(host);
        BecomeLibraryQuestion = old is null
            ? $"This {Device} keeps recording, and becomes your library too: its own place for notes, classes and settings."
            : $"{OldName(old)} on {RoleSwitch.ComputerOf(old.Url)} keeps its own copy. Classes and notes come here: every lecture with its notes and transcript, "
              + $"the files you attached, your classes with their other names and Canvas courses, and your chats. Then this {Device} is your library, and keeps recording. "
              + $"A big library takes a while; if it stops part-way, what came stays and you can try again.";
        ConfirmingBecomeLibrary = true;
        SwitchSay = null;
    }

    [RelayCommand] void CancelBecomeLibrary() => ConfirmingBecomeLibrary = false;

    /// <summary>The student said yes: this computer becomes the library (taking over the old one's name and password
    /// when it has none of its own), then brings over everything in the library it used, if any, saying how far it's
    /// got and, at the end, what came. A problem bringing things over (the old library's offline, say) never undoes
    /// the switch: it's said in plain words, and Try again picks up where it stopped.</summary>
    [RelayCommand]
    async Task ConfirmBecomeLibrary()
    {
        Switching = true;
        SwitchSay = null;
        BringFraction = -1;
        BringLine = $"Making this {Device} your library…";
        try
        {
            var old = RoleSwitch.OldLibraryOf(host);
            string done = await RoleSwitch.ToLibraryAsync(host, libraryHere(), old, DisplayName);
            LibraryHere = true;
            ConfirmingBecomeLibrary = false;
            Refresh();
            // What the switch remembered: whether the library here is new, so it takes the old one's notes settings too.
            var bring = RoleSwitch.Pending(host.Home) ?? old;
            SwitchSay = bring is null ? done : done + " " + await BringAsync(bring);
        }
        catch (Exception e) when (e is InvalidOperationException or ArgumentException)
        {
            SwitchSay = e.Message;
        }
        finally
        {
            Switching = false;
            BringLine = null;
            BringFraction = -1;
            Refresh();
        }
    }

    /// <summary>Try again (or, after setup, Bring them over): whatever hasn't come over from the old library yet.</summary>
    [RelayCommand]
    async Task BringAgain()
    {
        if (RoleSwitch.Pending(host.Home) is not { } old) return;
        Switching = true;
        SwitchSay = null;
        try
        {
            SwitchSay = await BringAsync(old);
        }
        finally
        {
            Switching = false;
            BringLine = null;
            BringFraction = -1;
            Refresh();
        }
    }

    /// <summary>The student would rather leave them where they are (that library is gone for good, say): the row goes,
    /// and nothing anywhere is deleted.</summary>
    [RelayCommand]
    void LeaveThemThere()
    {
        if (RoleSwitch.Pending(host.Home) is { } old) SwitchSay = $"OK: what didn't come over stays on {OldName(old)}, as it is.";
        RoleSwitch.Forget(host.Home);
        RefreshPending();
    }

    /// <summary>Brings <paramref name="old"/> over, saying how far it's got; returns what came, or (it stopped) why and
    /// that Try again picks up where it stopped. Never throws for a library that can't be reached.</summary>
    async Task<string> BringAsync(OldLibrary old)
    {
        string who = OldName(old);
        BringFraction = -1;
        BringLine = $"Bringing your classes over from {who}…";
        try
        {
            return await RoleSwitch.BringLecturesAsync(host, old, Progress, libraryHttp);
        }
        catch (InvalidOperationException e)
        {
            return $"Not everything from {who} has come over yet. {e.Message} What came is safe here, and {who} still has everything: press Try again when it can be reached.";
        }
    }

    void Progress(MoveProgress p) => ShowProgress(p, sending: false);

    void SendProgress(MoveProgress p) => ShowProgress(p, sending: true);

    /// <summary>How far bringing (or handing on) a library has got, on the window's thread, while it's going.</summary>
    void ShowProgress(MoveProgress p, bool sending)
    {
        void Show()
        {
            if (!Switching) return; // it's over: a late word about it would stay up
            BringFraction = p.Total > 0 ? (double)p.Done / p.Total : -1;
            string counted = p.Total > 0 ? $": {p.Done} of {p.Total}…" : "…";
            BringLine = (p.Stage, sending) switch
            {
                (LibraryMove.AttachmentsStage, false) => "Bringing attached files over" + counted,
                (LibraryMove.FilesStage, false) => "Bringing your other notes and files over" + counted,
                (_, false) => "Bringing lectures over" + counted,
                (LibraryMove.AttachmentsStage, true) => "Sending attached files" + counted,
                (LibraryMove.FilesStage, true) => "Sending your other notes and files" + counted,
                _ => "Sending lectures" + counted,
            };
        }
        if (Dispatcher.UIThread.CheckAccess()) Show();
        else Dispatcher.UIThread.Post(Show);
    }

    /// <summary>The row for an old library whose lectures haven't all come over: shown on a computer that's the library.</summary>
    void RefreshPending()
    {
        var waiting = host.Settings.Role != AppRole.Laptop ? RoleSwitch.Pending(host.Home) : null;
        HasPendingBring = waiting is not null;
        if (waiting is null) return;
        string who = OldName(waiting);
        PendingBringTitle = waiting.Tried ? $"Not everything from {who} has come over yet" : $"Your classes and notes are still on {who}";
        PendingBringLine = waiting.Tried
            ? $"What came is safe on this {Device}, and {who} still has everything. Try again once {RoleSwitch.ComputerOf(waiting.Url)} is on and reachable: only what's missing comes."
            : $"Bring them to this {Device}: every lecture with its notes and transcript, your classes and files. {who} keeps its own copy.";
        PendingBringAction = waiting.Tried ? "Try again" : "Bring them over";
    }

    /// <summary>A library (or a one-computer setup) asks to become a plain laptop, sending to another library.</summary>
    [RelayCommand]
    void AskBecomeLaptop()
    {
        if (Setup.NormalizeAddress(BecomeLaptopAddress).Length == 0)
        {
            SwitchSay = "Type your new library's address first, like http://mac-mini:8787.";
            return;
        }
        ConfirmingBecomeLaptop = true;
        SwitchSay = null;
    }

    [RelayCommand] void CancelBecomeLaptop() => ConfirmingBecomeLaptop = false;

    /// <summary>The student said yes: everything in this library goes to the new one first, so nothing is left behind,
    /// then this computer stops being the library and becomes a laptop that sends to it. Nothing changes if either
    /// step fails (or the other library is too old to take everything): the library here keeps running, ready to be
    /// tried again.</summary>
    [RelayCommand]
    async Task ConfirmBecomeLaptop()
    {
        Switching = true;
        SwitchSay = null;
        BringFraction = -1;
        BringLine = "Sending your lectures…";
        try
        {
            string sent = await RoleSwitch.HandOffLecturesAsync(host, BecomeLaptopAddress, BecomeLaptopPassword, SendProgress, libraryHttp);
            string done = await RoleSwitch.ToLaptopAsync(host, BecomeLaptopAddress, BecomeLaptopPassword);
            LibraryHere = false;
            ConfirmingBecomeLaptop = false;
            SwitchSay = sent + " " + done;
        }
        catch (InvalidOperationException e)
        {
            SwitchSay = e.Message;
        }
        finally
        {
            Switching = false;
            BringLine = null;
            BringFraction = -1;
            Refresh();
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

    /// <summary>Setup's steps again, from the welcome, with this computer's role kept.</summary>
    [RelayCommand] static void RunSetupAgain() => Shell.RunSetupAgain();
    /// <summary>The row's quiet link: setup by hand, as it was before guided setup.</summary>
    [RelayCommand] static void RunSetupByHand() => Shell.RunSetupAgain(byHand: true);

    [RelayCommand] static void Quit() => Shell.Quit();

    /// <summary>About's "Buy us more Claude usage": the team's Ko-fi page, in the browser, and the library window
    /// won't ask for a tip after that.</summary>
    [RelayCommand] void BuyClaudeUsage() => SupportAsk.Answer(host, SupportAnswer.Tip, OpenUrl, DateTimeOffset.Now);

    public void Dispose()
    {
        host.Changed -= OnHostChanged;
        Canvas.Dispose();
    }
}
