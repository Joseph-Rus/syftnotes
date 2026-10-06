using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StudyStash.App.Controls;
using StudyStash.App.Platform;
using StudyStash.App.Services;
using StudyStash.App.ViewModels;
using StudyStash.App.Views;
using StudyStash.App.Windows;
using StudyStash.Audio;
using StudyStash.Core;
using StudyStash.Core.Setup;
using StudyStash.Library;

namespace StudyStash.App;

/// <summary>
/// The running app: the menu bar (or tray) icon and the windows it opens, the shortcuts, and the models the windows
/// show, kept up to date from the <see cref="AppHost"/>.
/// </summary>
public static partial class Shell
{
    static App app = null!;
    static IClassicDesktopStyleApplicationLifetime life = null!;
    static AppHost host = null!;
    static TrayIcon? tray;
    static bool trayRecording;
    static readonly CancellationTokenSource stop = new();

    static readonly PanelModel panel = new();
    static readonly RecorderModel recorder = new();
    /// <summary>The lecture the student has been told is falling behind (once is enough).</summary>
    static string? behindToldFor;
    static readonly QuickModel quick = new();
    static readonly LibraryModel library = new();
    static SetupModel? setup;
    static MicCheck? micCheck;

    static Floating? panelWindow, recorderWindow, quickWindow;
    static Window? mainWindow, setupWindow, settingsWindow;
    static DispatcherTimer? ticker;
    /// <summary>Quitting has begun: windows close for real, and nothing refreshes.</summary>
    static bool quitting;
    static DateTime lastOops;
    /// <summary>Kept for the app's life: a registration nobody refers to is collected, and stops listening.</summary>
    static PosixSignalRegistration? terminate;
    static int terminations;

    /// <summary>The class Record will use, picked by hand; "" (the default) lets the library sort the lecture by what
    /// was said in it.</summary>
    static string chosenClass = "";
    static string? liveId;
    /// <summary>What's wrong right now (from <see cref="Problems"/>), so the panel's Fix button knows what to do.</summary>
    static AppProblem? currentProblem;
    static LibraryState lastLibraryState = LibraryState.NotSetUp;
    static bool hotkeysOn;
    static HotkeyResult hotkeys;

    public static AppHost Host => host;

    public static void Start(App application, IClassicDesktopStyleApplicationLifetime desktop)
    {
        app = application;
        life = desktop;
        string home = Program.Home;
        // First, so a copy started a moment after this one finds it listening (what it says waits for the UI thread).
        Desktop.Listen(home, OnHandOff, stop.Token);
        Dispatcher.UIThread.UnhandledException += OnUnhandled;
        desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        // The system quitting the app (logging out, the Dock's Quit): tidy up and let it go on.
        desktop.ShutdownRequested += (_, _) => CleanUp();
        // Told to stop (kill, launchd): quit properly, so a lecture being recorded is saved. Told twice, just stop.
        if (!OperatingSystem.IsWindows())
            terminate = PosixSignalRegistration.Create(PosixSignal.SIGTERM, c =>
            {
                if (Interlocked.Increment(ref terminations) > 1) return;
                c.Cancel = true;
                Dispatcher.UIThread.Post(() => Quit());
            });
        // A Mac: opening the app again (Finder, Spotlight, the Dock) while it runs.
        if (application.TryGetFeature<IActivatableLifetime>() is { } activatable)
            activatable.Activated += (_, e) =>
            {
                if (e.Kind == ActivationKind.Reopen) OnHandOff("show");
            };
        else if (OperatingSystem.IsMacOS())
            Program.Log("[app] this Mac doesn't say when the app is opened again; a second copy still hands off");
        // The library's dropdown names the laptops that reach it.
        RemoteLibrary.Computer = Environment.MachineName;
        host = new AppHost(home, laptop: new LaptopHost(), log: Program.Log);
        Skin.UseTheme(ColourThemes.Find(host.Settings.Theme));
        // Before any window shows, so it never opens in the wrong mode and then flips.
        Skin.UseAppearance(host.Settings.Appearance);
        host.Changed += RequestRefresh;
        host.Heard += (l, _) => ShowLiveSoon(l.Id);
        host.LiveWords += l => ShowLiveSoon(l.Id);
        host.Filed += l => Dispatcher.UIThread.Post(() =>
        {
            var (title, text, action) = NoticeWords.Filed(l.FiledClass, l.FiledTitle, l.Error);
            Toast(title, text, action, () => OpenLecture(l.Id));
            RequestLibraryReload();
        });
        host.Problem += (title, why) => Dispatcher.UIThread.Post(() =>
        {
            if (title == AppHost.RecordingPaused) SayRecordingPaused(why);
            else Toast(title, why, null, null);
        });
        if (host.PretendMic) Program.Log("[app] recording from a pretend microphone (STUDYSTASH_MIC_FILE)");
        Wire();
        host.Start();
        _ = SuggestLighterModelAsync();
        AppUpdates.Start(host, stop.Token, SayUpdate);
        if (AppUpdates.Current is { } updater)
            updater.FoundChanged += () => Dispatcher.UIThread.Post(() => panel.UpdateVersion = updater.Found is { } r ? string.Join('.', r.Version) : null);
        MakeTray();
        // A Mac's app menu (About, Settings… ⌘,, and the system's Hide and Quit ⌘Q) while a window is in front.
        Keybindings.Saved = () => host.Settings.Keys;
        if (OperatingSystem.IsMacOS()) AppMenu.Use(app, AppMenu.ShowAbout, SettingsFromAnywhere);
        ApplyShortcutsSetting(force: true);
        ticker = new DispatcherTimer(TimeSpan.FromMilliseconds(250), DispatcherPriority.Background, (_, _) => Tick());
        ticker.Start();
        Refresh();
        SayIfUpdated();
        if (!host.Settings.SetupDone) ShowSetup();
        else if (!Program.Background) ShowLibrary();
        if (SelfTest.Dir is not null) SelfTest.Run();
    }

    /// <summary>The windows, for the self-test.</summary>
    public static class Windows
    {
        public static Window? Setup => setupWindow;
        public static Window? Main => mainWindow;
        public static Window? Panel => panelWindow;
        public static Window? Quick => quickWindow;
        public static Window? Recorder => recorderWindow;
        public static Window? Settings => settingsWindow;
        public static Window? CanvasConnect => canvasConnectWindow;
        /// <summary>Setup's own view model, while its window is open: what the self-test drives (steps, connect,
        /// find, add a class) the same way the view's bindings would.</summary>
        public static ViewModels.SetupModel? SetupModel => setup;
        /// <summary>Guided setup's model, while setup's window is open.</summary>
        public static ViewModels.GuidedSetupModel? Guided => guidedSetup;
        public static void TogglePanel() => Shell.TogglePanel();
        public static void ToggleQuick() => Shell.ToggleQuick();
        public static void Record() => ToggleRecording();
        public static void StopRecording() => Shell.StopRecording();
        public static void TogglePause() => Shell.TogglePause();
        public static void ShowRecorder(bool expanded) => Shell.ShowRecorder(expanded);
        /// <summary>One step of dragging the recorder, as the pointer would take it: the self-test's check that it
        /// goes right up to a Mac's menu bar and down to Windows' taskbar.</summary>
        public static void DragRecorder(PixelPoint at, PixelPoint pointer)
        {
            if (recorderWindow is not null) MoveRecorder(recorderWindow, at, pointer);
        }
        /// <summary>Record the way the global shortcut would, so the self-test exercises that path too (setup first,
        /// if it isn't done).</summary>
        public static void RecordViaShortcut() => OnShortcut(Shortcut.Record);
        /// <summary>Opens a lecture's notes (or transcript) in the library window, as clicking it would.</summary>
        public static void OpenLecture(string id, bool transcript = false) => Shell.OpenLecture(id, transcript);
        /// <summary>Searches the quick panel, as typing in it would.</summary>
        public static Task Search(string query) => SearchAsync(query);
        public static QuickModel QuickModel => quick;
        public static PanelModel PanelModel => panel;
        public static RecorderModel RecorderModel => recorder;
        /// <summary>The notifications on screen, newest first, and the display they're on.</summary>
        public static ToastShelf Toasts => Shelf();

        /// <summary>Opens the dropdown the way clicking the real icon would (a Mac's status item; Windows' tray
        /// otherwise), and how far its centre landed from the icon's own, in points — the self-test's placement
        /// check ("panel under the icon"). Null off a Mac, or before the icon exists.</summary>
        public static double? OpenPanelViaIcon()
        {
            if (!OperatingSystem.IsMacOS())
            {
                Shell.TogglePanel();
                return null;
            }
            double? iconCentre = MacStatusItem.ButtonFrame() is { } f ? f.X + f.Width / 2 : null;
            MacStatusItem.PerformClick();
            return iconCentre is double x && panelWindow is { } w ? Math.Abs(w.Position.X + w.Bounds.Width / 2 - x) : null;
        }
    }

    static void OnHandOff(string message)
    {
        if (quitting) return;
        Program.Log($"[app] another copy said \"{message}\"");
        if (!host.Settings.SetupDone) ShowSetup();
        else if (message == "record") ToggleRecording();
        // "--show panel" / "--show quick": open the dropdown or the quick panel without the menu bar or the shortcut
        // (to look at them, or when another copy of the app owns the shortcuts).
        else if (message == "panel") TogglePanel();
        else if (message == "quick") ToggleQuick();
        // "--show settings" opens Settings, "--show settings:Library" at one of its pages.
        else if (message == "settings" || message.StartsWith("settings:", StringComparison.Ordinal))
            ShowSettings(message.Length > "settings:".Length ? message["settings:".Length..] : null);
        else if (message.StartsWith("snap:", StringComparison.Ordinal)) MacSnap.Save(message["snap:".Length..]);
        else ShowLibrary();
    }

    /// <summary>Something the app didn't expect, on the UI thread: it's written down and said, and the app carries on.</summary>
    static void OnUnhandled(object? sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Program.Log($"[error] {e.Exception}");
        e.Handled = true;
        if (quitting || DateTime.UtcNow - lastOops < TimeSpan.FromSeconds(10)) return;
        lastOops = DateTime.UtcNow;
        try
        {
            // The exception is in the log already; the student gets what happened, not the stack's words.
            Toast("Something went wrong", "Study Stash carried on. If something looks wrong, quit it and open it again.", null, null);
        }
        catch (Exception again)
        {
            Program.Log($"[error] couldn't say so: {again.Message}");
        }
    }

    static void OnShortcut(Shortcut s)
    {
        if (!host.Settings.SetupDone)
        {
            ShowSetup();
            return;
        }
        if (s == Shortcut.Quick) ToggleQuick();
        else ToggleRecording();
    }

    /// <summary>The Shortcuts toggle in Settings applies at once, without restarting: turned off, both let go;
    /// turned back on, they're asked for again (and the app says if either is taken).</summary>
    static void ApplyShortcutsSetting(bool force = false)
    {
        bool wanted = host.Settings.Shortcuts && !shortcutsPaused;
        var quickKeys = Keybindings.Of(KeyAction.Quick);
        var recordKeys = Keybindings.Of(KeyAction.Record);
        string keys = $"{quickKeys} {recordKeys}";
        AppMenu.UseSettingsKeys(Keybindings.Of(KeyAction.Settings));
        if (!force && wanted == hotkeysOn && keys == hotkeysKeys) return;
        if (hotkeysOn) Hotkeys.Unregister();
        hotkeysOn = wanted;
        hotkeysKeys = keys;
        if (wanted)
        {
            hotkeys = Hotkeys.Register(OnShortcut, quickKeys, recordKeys);
            if (!hotkeys.All) Program.Log("[app] a shortcut is taken by another app");
        }
        else
        {
            Hotkeys.Unregister();
            hotkeys = new HotkeyResult(true, true);
        }
    }

    /// <summary>The shortcuts registered last, so a change to them re-registers.</summary>
    static string hotkeysKeys = "";
    /// <summary>Settings is listening for a new shortcut: the system's hold on the current ones is let go meanwhile,
    /// or pressing one would do it rather than reach Settings.</summary>
    static bool shortcutsPaused;

    /// <summary>Settings → Shortcuts listening for keys (true), or done (false).</summary>
    public static void PauseShortcuts(bool paused)
    {
        shortcutsPaused = paused;
        ApplyShortcutsSetting();
    }

    /// <summary>Settings' words about the shortcuts: which one (if any) another app already has.</summary>
    public static string? ShortcutsSay()
    {
        if (!hotkeysOn) return null;
        string quick = Keybindings.Show(KeyAction.Quick);
        string record = Keybindings.Show(KeyAction.Record);
        if (!hotkeys.Quick) return $"{quick} is taken by another app, so search from the menu bar.";
        if (!hotkeys.Record) return $"{record} is taken by another app, so record from the menu bar.";
        return null;
    }

    /// <summary>Quit Study Stash, with an exit code (the self-test's pass or fail). Only the first call counts.</summary>
    public static void Quit(int code = 0)
    {
        if (quitting) return;
        CleanUp();
        life.Shutdown(code);
    }

    /// <summary>Everything quitting does before the app goes, once: the recording is saved, Whisper and the sender stop,
    /// the icon goes, and the settings are written.</summary>
    static void CleanUp()
    {
        if (quitting) return;
        quitting = true;
        ticker?.Stop();
        CloseAllToasts();
        StopCanvas();
        stop.Cancel();
        try
        {
            host.Dispose();
        }
        catch (Exception e)
        {
            Program.Log($"[error] stopping: {e}");
        }
        tray?.Dispose();
        tray = null;
        if (OperatingSystem.IsMacOS()) MacStatusItem.Destroy();
        Player.Stop();
        SaveLibraryPlace();
        host.Save(_ => { });
        Program.Log("[app] quitting");
    }

    // --- the tray ---------------------------------------------------------------------------------------------------

    /// <summary>The icon, as PNG bytes: the "S." mark — black on a Mac (a template image the menu bar tints, or, while
    /// recording, in the menu bar's own ink so its red dot stays red), black or white on Windows by theme — with a red
    /// dot while recording.</summary>
    static byte[] TrayImageBytes(bool recording)
    {
        if (OperatingSystem.IsMacOS())
            return TrayMark.Png(36, recording && (trayDark = MacStatusItem.DarkMenuBar()) ? Brushes.White : Brushes.Black, recording);
        IBrush ink = Application.Current?.ActualThemeVariant == Avalonia.Styling.ThemeVariant.Light ? Brushes.Black : Brushes.White;
        return TrayMark.Png(32, ink, recording);
    }

    /// <summary>The Mac menu bar was dark when the recording icon was last drawn.</summary>
    static bool trayDark;
    /// <summary>This recording's menu bar icon has been written to the log once.</summary>
    static bool trayLogged;

    static WindowIcon TrayImage(bool recording)
    {
        var stream = new MemoryStream(TrayImageBytes(recording));
        return new WindowIcon(stream);
    }

    static void MakeTray()
    {
        if (OperatingSystem.IsMacOS())
        {
            // Avalonia's own TrayIcon never raises Clicked on macOS, so the menu bar icon is a real NSStatusItem.
            MacStatusItem.Create(leftClick: x => TogglePanel(new PixelPoint((int)x, 0)),
                record: host.Settings.Role == AppRole.Library ? null : ToggleRecording, search: ToggleQuick, open: ShowLibrary, settings: SettingsFromAnywhere, quit: () => Quit());
            MacStatusItem.SetIcon(TrayImageBytes(false));
            // Once the menu bar has laid it out: where it is, and whether the student can see it at all.
            DispatcherTimer.RunOnce(CheckMenuBarIcon, TimeSpan.FromSeconds(2));
            return;
        }
        tray = new TrayIcon { Icon = TrayImage(false), ToolTipText = "Study Stash", IsVisible = true };
        tray.Clicked += (_, _) => TogglePanel();
        trayRole = host.Settings.Role;
        tray.Menu = TrayMenu(host.Settings.Role);
        TrayIcon.SetIcons(app, new TrayIcons { tray });
        // The tray's ink is black or white depending on the theme; redraw it when that changes.
        app.ActualThemeVariantChanged += (_, _) => tray.Icon = TrayImage(trayRecording);
    }

    /// <summary>The role the tray's menu was made for: setup (or Settings) changing it makes the menu again.</summary>
    static AppRole? trayRole;

    /// <summary>The tray icon's right-click menu (Windows). Record is there on every computer that records: a laptop,
    /// and just this computer; a library-only computer doesn't record.</summary>
    internal static NativeMenu TrayMenu(AppRole role)
    {
        var menu = new NativeMenu();
        void Item(string title, Action act)
        {
            var i = new NativeMenuItem(title);
            i.Click += (_, _) => act();
            menu.Add(i);
        }
        if (role != AppRole.Library) Item("Record", ToggleRecording);
        Item("Search notes and lectures", ToggleQuick);
        Item("Open Study Stash", ShowLibrary);
        Item("Settings…", SettingsFromAnywhere);
        menu.Add(new NativeMenuItemSeparator());
        Item("Quit Study Stash", () => Quit());
        return menu;
    }

    /// <summary>The menu bar had no room to show the S. when it was last checked.</summary>
    static bool iconHidden;

    /// <summary>Writes where the menu bar icon is to the log; one the menu bar hides (full, or behind the camera notch)
    /// is said once, with how to reach the app anyway, when setup's done.</summary>
    static void CheckMenuBarIcon()
    {
        if (quitting || !OperatingSystem.IsMacOS() || MacStatusItem.Check() is not { } p) return;
        Program.Log($"[tray] menu bar icon: {p}");
        bool hidden = !p.Seen;
        if (hidden && !iconHidden && host.Settings.SetupDone) SayWhereTheIconIs();
        iconHidden = hidden;
    }

    /// <summary>The hidden S.'s "Show it": the menu bar puts it just right of the notch (and remembers that), and the
    /// log says where it landed.</summary>
    static void MoveIconIntoView()
    {
        if (!OperatingSystem.IsMacOS()) return;
        MacStatusItem.MoveIntoView();
        Program.Log("[tray] moving the menu bar icon into view");
        DispatcherTimer.RunOnce(CheckMenuBarIcon, TimeSpan.FromSeconds(1.5));
    }

    /// <summary>A notification saying where the S. lives (after setup, and when a full menu bar hides it).</summary>
    static void SayWhereTheIconIs()
    {
        bool mac = OperatingSystem.IsMacOS();
        bool hidden = mac && MacStatusItem.Check() is { Seen: false };
        if (!IconWords.WorthSaying(hidden, panelWindow?.IsVisible == true)) return;
        var (title, text) = IconWords.WhereItIs(mac, hidden);
        // Longer than most: it's the one way to find the app when its icon can't be seen.
        Toast(title, text, hidden ? IconWords.ShowIt : null, hidden ? MoveIconIntoView : null, hidden ? NoticeTimes.Advice : TimeSpan.FromSeconds(12));
    }

    // --- what the buttons do ---------------------------------------------------------------------------------------

    static void Wire()
    {
        panel.OnRecord = ToggleRecording;
        panel.OnStop = () => StopRecording();
        panel.OnPause = TogglePause;
        panel.OnShowRecorder = () => ShowRecorder(expanded: true);
        panel.OnSearch = () =>
        {
            panelWindow?.Hide();
            ToggleQuick();
        };
        panel.OnOpenApp = () =>
        {
            panelWindow?.Hide();
            ShowLibrary();
        };
        panel.OnSettings = SettingsFromAnywhere;
        panel.OnUpdate = () =>
        {
            panel.UpdateVersion = null;
            _ = UpdateNowAsync();
        };
        panel.OnSwitchClass = PickClass;
        panel.OnFixProblem = FixProblem;
        panel.OnOpenLecture = OpenRecentLecture;

        recorder.OnPause = TogglePause;
        recorder.OnStop = () => StopRecording();
        recorder.OnExpand = expanded =>
        {
            recorderWindow?.Refit(PlaceRecorder);
            host.WatchLiveWords(WatchingWords());
        };
        recorder.Busy = () => recorderWindow?.Refitting == true;

        quick.OnQuery = q => _ = SearchAsync(q);
        quick.OnAsk = AskQuick;
        quick.OnOpen = OpenQuickRow;
        quick.OnClose = () => quickWindow?.Hide();

        library.OnClass = c =>
        {
            Remember();
            library.NarrowDetail = false;
            if (c.IsHome)
            {
                _ = ShowHomeAsync();
                return;
            }
            // Due opens on its home (what's to hand in, by when and by class); its Full list is the two-column list.
            if (c.IsDue)
            {
                dueSelection = null;
                _ = ShowDueHomeAsync();
                return;
            }
            // A class opens on its home; Unsorted, which has none, on its lectures.
            allLectures = false;
            _ = c.IsUnsorted ? ShowClassAsync(c.Name) : ShowClassHomeAsync(c.Name);
        };
        library.OnMoveToFolder = MoveToFolderAsync;
        library.OnLecture = l =>
        {
            Remember();
            OpenFromList(() => ShowLectureAsync(l.Id));
        };
        library.OnClassPage = () =>
        {
            if (openClass is not { } cls) return;
            Remember();
            _ = ShowClassHomeAsync(cls);
        };
        library.OnGoBack = GoBack;
        library.OnGoForward = GoForward;
        library.SidebarHidden = host.Settings.SidebarHidden;
        library.OnSidebarToggled = hidden => host.Save(s => s.SidebarHidden = hidden);
        library.ListHidden = host.Settings.ListHidden;
        library.OnListToggled = hidden => host.Save(s => s.ListHidden = hidden);
        library.OnSearch = ToggleQuick;
        library.OnSettings = ShowSettings;
        library.OnMove = MoveLecture;
        library.OnDelete = DeleteLectureAsync;
        library.OnUndo = UndoDeleteAsync;
        library.OnExport = () => _ = ExportAsync();
        library.OnMore = MoreMenu;
        library.OnSupportAnswer = answer => SupportAsk.Answer(host, answer, url => Dialogs.OpenUrl(url), DateTimeOffset.Now);
        // A diagram in a lecture's notes asks about its boxes in the lecture's Ask bar and finds them in its transcript;
        // how to explore one is shown once, ever.
        library.Diagrams = new LectureDiagrams(library, id => File.Exists(host.Lectures.AudioPath(id)), (id, at) => Play(id, at));
        Controls.Rich.DiagramExplorer.HintWasSeen = () => host.Settings.DiagramHintSeen;
        Controls.Rich.DiagramExplorer.RememberHint = () => host.Save(s => s.DiagramHintSeen = true);
    }

    // --- recording ----------------------------------------------------------------------------------------------------

    /// <summary>The class picked for Record, while the library still has it; otherwise "" (the library sorts it).</summary>
    static string RecordClass() => chosenClass.Length > 0 && host.Classes().Any(c => c.Name == chosenClass) ? chosenClass : "";

    /// <summary>Record is waiting on macOS's microphone prompt: another press does nothing until it's answered.</summary>
    static bool askingForMic;

    static async void ToggleRecording()
    {
        if (host.Recorder.Current is not null)
        {
            StopRecording();
            return;
        }
        if (askingForMic) return;
        if (host.Settings.Role == AppRole.Library)
        {
            Toast("This computer is your library", "Record on your laptop: this one keeps the lectures and writes the notes.", "Settings", SettingsFromAnywhere);
            return;
        }
        if (!host.ModelReady)
        {
            var (title, text, action) = NoticeWords.NoModelYet(host.Downloading);
            Toast(title, text, action, () => _ = host.DownloadModelAsync());
            return;
        }
        RecordStart start;
        askingForMic = true;
        try
        {
            // Not asked yet: macOS's own prompt is all the student sees until they answer; recording starts after.
            start = await host.RecordAsync(RecordClass());
        }
        catch (Exception e) when (e is InvalidOperationException or PlatformNotSupportedException)
        {
            Toast("Couldn't start recording", e.Message, null, null);
            Refresh();
            return;
        }
        finally
        {
            askingForMic = false;
        }
        if (start.Trouble is { } trouble)
        {
            // The lecture didn't start: this stays until it's closed, or until a lecture does start.
            Notify(new Notice
            {
                Title = trouble.Title, Text = trouble.Detail, ActionLabel = trouble.HasAction ? trouble.ActionLabel : null,
                Act = trouble.ActionUrl.Length > 0 ? () => Dialogs.OpenUrl(trouble.ActionUrl) : null,
                UntilClosed = true, StillTrue = () => host.Recorder.Current is null,
            });
        }
        else if (start.Lecture is { } l)
        {
            liveId = l.Id;
            recorder.Lines.Clear();
            recorder.QuickWords = host.LiveWordsOn;
            // Written down after class: nothing to show or ask about until it stops, and the recorder says so.
            recorder.AfterClass = l.AfterClass;
            recorder.Ask = l.AfterClass ? null : LiveAsk();
            panel.LastLine = l.AfterClass ? "Only recording. It's written down after class." : "";
            panelWindow?.Hide();
            ShowRecorder(expanded: false);
        }
        Refresh();
    }

    static void TogglePause()
    {
        if (host.Recorder.Current is not { } l) return;
        if (l.State == LectureState.Paused) host.Resume();
        else host.Pause();
        Refresh();
    }

    static void StopRecording()
    {
        var l = host.StopRecording();
        chosenClass = "";
        recorderWindow?.Hide();
        if (l is { State: LectureState.Failed }) Toast(l.Error, "Its sound file is damaged, so it can't be written down.", null, null);
        else if (l is not null) Toast("Recording saved", NoticeWords.Saved(host.Library, OperatingSystem.IsMacOS()), null, null);
        Refresh();
    }

    /// <summary>A menu in the app's look (Styles.axaml): on a Mac the system draws its soft shadow round the rounded
    /// panel, as it does for its own menus.</summary>
    internal static ContextMenu Menu() => new() { WindowManagerAddShadowHint = OperatingSystem.IsMacOS() };

    /// <summary>The dropdown's class picker, hung under its button: a check on what Record will do now, and a pick
    /// changes Record's label and the line under it at once.</summary>
    static void PickClass()
    {
        var menu = ClassPicker.Build([.. host.Classes().Select(c => (c.Name, c.Color))], chosenClass, name =>
        {
            chosenClass = name;
            Program.Log($"[panel] class picked: {(name.Length == 0 ? "let the library sort it" : name)}");
            Refresh();
            // The dropdown stays up, showing Record's new label and the line under it.
            panelWindow?.Activate();
        });
        if (panelWindow is null) return;
        var dropdown = panelWindow;
        dropdown.HoldOpen = true;
        menu.Closed += (_, _) =>
        {
            dropdown.HoldOpen = false;
            // Closed by a click somewhere else altogether: the dropdown goes too, as it would have.
            DispatcherTimer.RunOnce(() =>
            {
                if (dropdown.IsVisible && !dropdown.IsActive) dropdown.Hide();
            }, TimeSpan.FromMilliseconds(150));
        };
        var button = panelWindow.GetVisualDescendants().OfType<Button>().FirstOrDefault(b => b.Command == panel.SwitchClassCommand && b.IsEffectivelyVisible);
        if (button is null)
        {
            if (panelWindow.Content is Control c) menu.Open(c);
            return;
        }
        menu.PlacementTarget = button;
        menu.Placement = PlacementMode.BottomEdgeAlignedRight;
        menu.VerticalOffset = 6;
        menu.Open(button);
    }

    /// <summary>The panel's Fix button: what to do depends on which problem is showing right now.</summary>
    static void FixProblem() => FixProblem(currentProblem?.Kind);

    /// <summary>What a problem's button does (the dropdown's Fix, or a problem's notification).</summary>
    static void FixProblem(ProblemKind? kind)
    {
        switch (kind)
        {
            case ProblemKind.MicDenied:
                Dialogs.OpenUrl(host.MicSettingsUrl);
                break;
            case ProblemKind.WhisperFailed:
                _ = host.RedownloadModel();
                break;
            case ProblemKind.DownloadFailed or ProblemKind.NoModel:
                _ = host.DownloadModelAsync();
                break;
            case ProblemKind.LibraryStopped:
                _ = host.RefreshLocalLibraryAsync();
                break;
            case ProblemKind.WrongPassword or ProblemKind.NotSetUp:
                ShowSettings("Connection");
                break;
        }
    }

    /// <summary>A click on one of the dropdown's recent lectures: the live one opens the recorder, a failed one is
    /// tried again, one still on its way just says where it is, and a filed one (or one being written up) opens.</summary>
    static void OpenRecentLecture(LectureItem item)
    {
        panelWindow?.Hide();
        if (item.Id == liveId)
        {
            ShowRecorder(expanded: true);
            return;
        }
        switch (item.State)
        {
            case LectureState.Failed:
                host.Retry(item.Id);
                Toast("Trying again", item.Title, null, null);
                break;
            case LectureState.Transcribing or LectureState.Sending:
                Toast(item.Title, item.Detail, null, null);
                break;
            default:
                OpenLecture(item.Id);
                break;
        }
    }

    /// <summary>A show is waiting on the UI thread: more news before it runs is shown by that one.</summary>
    static int liveShowPending;

    /// <summary>New words for a lecture (the live words, every second or so, or the transcript's own lines): shown once
    /// the UI thread gets to it, however many came meanwhile.</summary>
    static void ShowLiveSoon(string id)
    {
        if (Interlocked.Exchange(ref liveShowPending, 1) == 1) return;
        Dispatcher.UIThread.Post(() =>
        {
            Volatile.Write(ref liveShowPending, 0);
            if (id == liveId) ShowLive();
        });
    }

    /// <summary>
    /// The recorder's transcript: what's been said so far (<see cref="AppHost.LiveLines"/>, the transcript's own lines
    /// and the live words after them), its last 200 lines. Only what changed is redrawn: lines that scrolled off the top
    /// go, the lines that read the same stay, and the rest (the live words' newest line, or live words the transcript
    /// has now written down) are drawn again.
    /// </summary>
    static void ShowLive()
    {
        // The live words turned off for being too slow in this lecture: the waiting line says when the transcript's lines come.
        recorder.QuickWords = host.LiveWordsNow;
        var lines = host.LiveLines();
        if (lines.Count > 200) lines = lines[^200..];
        while (recorder.Lines.Count > 0 && lines.Count > 0 && recorder.Lines[0].Start < lines[0].Start) recorder.Lines.RemoveAt(0);
        int same = 0;
        while (same < recorder.Lines.Count && same < lines.Count && recorder.Lines[same].Text == lines[same].Text
               && recorder.Lines[same].Start == lines[same].Start && recorder.Lines[same].Latest == (same == lines.Count - 1)) same++;
        while (recorder.Lines.Count > same) recorder.Lines.RemoveAt(recorder.Lines.Count - 1);
        for (int i = same; i < lines.Count; i++)
            recorder.Lines.Add(new HeardLine { Time = TimedText.Clock(lines[i].Start), Text = lines[i].Text, Start = lines[i].Start, Latest = i == lines.Count - 1 });
        if (lines.Count > 0) panel.LastLine = $"“…{Trim(lines[^1].Text, 90)}”";
    }

    static string Trim(string s, int n) => s.Length <= n ? s : s[..n].TrimEnd() + "…";

    /// <summary>The recorder's chat for a new lecture: asks about what's been said so far, with any engine (the
    /// library's default for questions until another is picked).</summary>
    static AiAskModel LiveAsk()
    {
        var ask = new AiAskModel(Ai())
        {
            // What's been said so far: the transcript as far as it has got, and the live words after it.
            Live = host.LiveTranscript,
            LiveTitle = $"{(host.Recorder.Current?.ClassName is { Length: > 0 } c ? c : "This lecture")}, now",
            OpenSettings = () => ShowSettings("AI"),
            OnSource = s => Play(s.Id ?? liveId, s.At ?? 0),
        };
        _ = ask.Load();
        return ask;
    }

    static async Task Answer(ChatMessage into, Func<RemoteLibrary, Task<JsonObject>> ask)
    {
        if (host.Remote() is not { } lib)
        {
            into.Thinking = false;
            into.Text = "Connect to your library first (Settings → Library).";
            return;
        }
        try
        {
            var r = await ask(lib);
            into.Text = r["answer"]?.GetValue<string>() ?? "";
            foreach (var s in (r["sources"] as JsonArray ?? []).OfType<JsonObject>())
                if (s["at"] is JsonValue v && v.TryGetValue(out double at))
                    into.Sources.Add(new SourceChip { Label = TimedText.Clock(at), At = at, LectureId = s["id"]?.GetValue<string>() });
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or LibraryRefusedException)
        {
            into.Text = e is LibraryRefusedException r ? r.Message : "Your library didn't answer. Is it on?";
        }
        into.Thinking = false;
    }

    // --- the windows -----------------------------------------------------------------------------------------------

    static Control PanelView() => Skin.Current == SkinKind.Mac ? new MacPanel { DataContext = panel } : new WinPanel { DataContext = panel };

    /// <summary>Opens or closes the dropdown. <paramref name="near"/> is where the icon was clicked, when that's
    /// known outright (the Mac status item hands its own icon's position); otherwise the pointer's own position is
    /// asked for (a Windows tray click, or the shortcut).</summary>
    static void TogglePanel(PixelPoint? near = null)
    {
        if (panelWindow?.IsVisible == true)
        {
            panelWindow.Hide();
            return;
        }
        // The click that opens it follows the deactivate that just closed it (one gesture, two events): don't reopen.
        if (panelWindow is not null && DateTime.UtcNow - panelWindow.LastDeactivateHide < Floating.ToggleDebounce) return;
        if (panelWindow is null)
        {
            panelWindow = new Floating { Content = PanelView(), CloseOnDeactivate = true, KeepClear = true, Title = "Study Stash" };
            AppMenu.AddSettingsKey(panelWindow, SettingsFromAnywhere);
        }
        Refresh();
        // NSEvent's mouse location is in points, in the same coordinate space Avalonia's screens report: no
        // rescaling (a display's own scale factor doesn't change where its menu bar sits in that shared space).
        var pointer = near ?? Floating.Pointer();
        var (_, scale) = panelWindow.WorkArea(pointer);
        var size = panelWindow.Measured(scale);
        int room = (int)(Floating.ShadowRoom * scale);
        var anchor = pointer ?? new PixelPoint(0, 0);
        panelWindow.Put(OperatingSystem.IsMacOS()
            ? Placement.MacDropdown(anchor, panelWindow.ScreenList(), size, room)
            : Placement.TrayFlyout(anchor, panelWindow.ScreenList(), size, room));
        panelWindow.Show();
        panelWindow.Activate();
        Desktop.Activate();
        host.WatchLiveWords(true);
    }

    /// <summary>The live words can be seen: the recorder is open on screen, or the menu's panel (its last line) is.</summary>
    static bool WatchingWords() => (recorder.Expanded && recorderWindow?.IsVisible == true) || panelWindow?.IsVisible == true;

    static void ShowRecorder(bool expanded)
    {
        recorderWindow ??= MakeRecorderWindow();
        bool changed = recorder.Expanded != expanded;
        recorder.Expanded = expanded;
        if (recorderWindow.IsVisible)
        {
            // On screen already: a change of size goes through Refit, never a resize in view.
            if (changed) recorderWindow.Refit(PlaceRecorder);
            host.WatchLiveWords(WatchingWords());
            return;
        }
        recorderWindow.SizeToContent = SizeToContent.WidthAndHeight;
        PlaceRecorder();
        recorderWindow.Show();
        Dispatcher.UIThread.Post(PlaceRecorder, DispatcherPriority.Loaded);
        host.WatchLiveWords(WatchingWords());
    }

    static Floating MakeRecorderWindow()
    {
        var view = Skin.Current == SkinKind.Mac ? (Control)new MacRecorder { DataContext = recorder } : new WinRecorder { DataContext = recorder };
        var w = new Floating { Content = view, KeepClear = true, Title = "Study Stash recorder" };
        AppMenu.AddSettingsKey(w, SettingsFromAnywhere);
        // Drag it anywhere by its background; where it lands is saved once the drag ends, not on every pixel moved.
        // It moves by hand, not by the system's own window drag, so it can go right up to a Mac's menu bar (see
        // Placement.Dragged). On the small pill a press that never moves is a click, which opens the recorder.
        PixelPoint? grabbed = null;
        PixelPoint grabbedAt = default;
        bool moved = false;
        view.PointerPressed += (_, e) =>
        {
            if (e.Source is TextBox || w.Refitting || !e.GetCurrentPoint(view).Properties.IsLeftButtonPressed || w.Panel() is not { } panel) return;
            // All in screen pixels: the window's clear room shrinks and grows at the display's edges as it moves, which
            // shifts the pill inside it, so a point on the pill itself wouldn't stay put.
            grabbed = view.PointToScreen(e.GetPosition(view));
            int room = (int)(Floating.ShadowRoom * w.DesktopScaling);
            grabbedAt = new PixelPoint(panel.X - room, panel.Y - room);
            moved = false;
            e.Pointer.Capture(view);
        };
        view.PointerMoved += (_, e) =>
        {
            if (grabbed is not { } from) return;
            var now = view.PointToScreen(e.GetPosition(view));
            var by = now - from;
            if (!moved && Math.Abs(by.X) + Math.Abs(by.Y) < 4 * w.DesktopScaling) return;
            moved = true;
            // The pill follows the pointer, so the pointer stays over the same spot of it.
            MoveRecorder(w, grabbedAt + by, now);
        };
        view.PointerReleased += (_, e) =>
        {
            if (grabbed is null) return;
            grabbed = null;
            e.Pointer.Capture(null);
            if (moved) SaveRecorderPosition();
            else if (!recorder.Expanded) recorder.ToggleCommand.Execute(null);
        };
        w.Closing += (_, e) =>
        {
            if (quitting) return;
            e.Cancel = true;
            w.Hide();
            SaveRecorderPosition();
        };
        // A display is unplugged, or one's plugged back in: put it back where it belongs, or on screen at least.
        w.Screens.Changed += (_, _) => PlaceRecorder();
        return w;
    }

    /// <summary>One step of dragging the recorder: to <paramref name="at"/> (its top left with the whole shadow room),
    /// with the pill kept on the usable part of the display under <paramref name="pointer"/>.</summary>
    static void MoveRecorder(Floating w, PixelPoint at, PixelPoint pointer)
    {
        var screens = w.ScreenList();
        double scale = Placement.Pick(screens, pointer).Scaling;
        w.Put(Placement.Dragged(at, pointer, screens, w.Measured(scale), (int)(Floating.ShadowRoom * scale)));
    }

    /// <summary>Remembers the recorder's top right corner, so it comes back there next time (a drag just ended, or
    /// the window is about to hide or the app to quit).</summary>
    static void SaveRecorderPosition()
    {
        if (recorderWindow is not { IsVisible: true } w || w.Panel() is not { } panel) return;
        var (_, scale) = w.WorkArea(w.Position);
        // Where the window's top right would be with its whole shadow room, whatever of that room is trimmed now.
        int room = (int)(Floating.ShadowRoom * scale);
        host.Save(s =>
        {
            s.RecorderX = panel.Right + room;
            s.RecorderY = panel.Y - room;
        });
    }

    /// <summary>The recorder keeps its top right corner where you left it (a corner by default) as it grows and
    /// shrinks, on whichever display it was on — or the default corner, if that display is gone.</summary>
    static void PlaceRecorder()
    {
        if (recorderWindow is null) return;
        var (_, scale) = recorderWindow.WorkArea(recorderWindow.Position);
        var size = recorderWindow.Measured(scale);
        int room = (int)(Floating.ShadowRoom * scale);
        PixelPoint? saved = host.Settings.RecorderX is double rx && host.Settings.RecorderY is double ry ? new PixelPoint((int)rx, (int)ry) : null;
        recorderWindow.Put(Placement.KeepOnScreen(saved, recorderWindow.ScreenList(), size, OperatingSystem.IsMacOS(), room));
    }

    static void ToggleQuick()
    {
        if (quickWindow?.IsVisible == true)
        {
            quickWindow.Hide();
            return;
        }
        if (quickWindow is not null && DateTime.UtcNow - quickWindow.LastDeactivateHide < Floating.ToggleDebounce) return;
        var view = quickWindow?.Content;
        if (quickWindow is null)
        {
            view = Skin.Current == SkinKind.Mac ? new MacQuick { DataContext = quick } : new WinQuick { DataContext = quick };
            quickWindow = new Floating { Content = view, CloseOnDeactivate = true, KeepClear = true, Title = "Study Stash search" };
            AppMenu.AddSettingsKey(quickWindow, SettingsFromAnywhere);
        }
        quick.Answering = false;
        quick.Query = "";
        _ = SearchAsync("");
        var pointer = Floating.Pointer();
        var (_, scale) = quickWindow.WorkArea(pointer);
        var size = quickWindow.Measured(scale);
        int room = (int)(Floating.ShadowRoom * scale);
        quickWindow.Put(Placement.QuickPanel(pointer, quickWindow.ScreenList(), size, room));
        quickWindow.Show();
        quickWindow.Activate();
        Desktop.Activate();
        if (view is MacQuick mq) mq.FocusQuery();
        else if (view is WinQuick wq) wq.FocusQuery();
    }

    static Window MainWindow()
    {
        if (mainWindow is not null) return mainWindow;
        var w = new Window
        {
            Title = "Study Stash", Width = 1280, Height = 800, MinWidth = 600, MinHeight = 560, WindowStartupLocation = WindowStartupLocation.CenterScreen,
            ExtendClientAreaToDecorationsHint = true, ExtendClientAreaTitleBarHeightHint = Skin.Current == SkinKind.Mac ? WindowHeader.MacHeight : 48,
        };
        Look.Apply(w);
        if (Skin.Current == SkinKind.Mac)
        {
            w.Content = new MacLibrary { DataContext = library };
            MacTitleBar.Attach(w);
        }
        else
        {
            w.Content = new WinLibrary { DataContext = library };
            WinChrome.Apply(w);
        }
        if (!PutWhereLeft(w)) OpenCentred(w, new Size(1280, 800));
        AppMenu.Attach(w, ShowLibrary, SettingsFromAnywhere);
        AddLibraryKeys(w);
        // The menu bar, read back once, so the log shows the app menu really has Settings… ⌘, and Quit ⌘Q.
        if (OperatingSystem.IsMacOS())
        {
            bool described = false;
            w.Activated += (_, _) =>
            {
                if (described) return;
                described = true;
                DispatcherTimer.RunOnce(() => Program.Log($"[menu] {AppMenu.Describe()}"), TimeSpan.FromMilliseconds(500));
            };
        }
        w.Closing += (_, e) =>
        {
            library.WalkAwayFromSupport();
            if (quitting) return;
            SaveLibraryPlace();
            e.Cancel = true;
            w.Hide();
            UpdateDock();
        };
        return mainWindow = w;
    }

    /// <summary>The library window opens where it was left, at the size it was left (or zoomed), if that spot is
    /// still on a display (true); otherwise it's for the caller to centre it.</summary>
    static bool PutWhereLeft(Window w)
    {
        if (host.Settings.LibraryWindow is not { } place) return false;
        var screens = w.Screens.All.Select(s => new ScreenGeometry(s.Bounds, s.WorkingArea, s.Scaling, s.IsPrimary)).ToList();
        var at = new PixelPoint(place.X, place.Y);
        double scale = Placement.Pick(screens, at).Scaling;
        var size = new PixelSize((int)(Math.Max(place.Width, w.MinWidth) * scale), (int)(Math.Max(place.Height, w.MinHeight) * scale));
        if (Placement.Restore(at, size, screens, out var fitted) is not { } spot) return false;
        w.WindowStartupLocation = WindowStartupLocation.Manual;
        w.Position = spot;
        w.Width = fitted.Width / scale;
        w.Height = fitted.Height / scale;
        if (place.Zoomed) w.Opened += (_, _) => w.WindowState = WindowState.Maximized;
        return true;
    }

    /// <summary>The display a window opening now belongs on: on Windows the one the pointer is on, on a Mac the main one.</summary>
    static ScreenGeometry ScreenFor(Window w) =>
        Placement.Pick([.. w.Screens.All.Select(s => new ScreenGeometry(s.Bounds, s.WorkingArea, s.Scaling, s.IsPrimary))], OperatingSystem.IsWindows() ? Floating.Pointer() : null);

    /// <summary>Opens <paramref name="w"/> in the middle of the pointer's display, at <paramref name="wanted"/> where
    /// that fits and smaller where it doesn't (a small screen at 125 or 150%), its title bar always on the screen.
    /// Returns the most room that display has for a window.</summary>
    static Size OpenCentred(Window w, Size wanted)
    {
        var screen = ScreenFor(w);
        var (at, size) = Placement.Centred(screen, wanted, new Size(w.MinWidth, w.MinHeight));
        w.WindowStartupLocation = WindowStartupLocation.Manual;
        w.Position = at;
        w.Width = size.Width;
        w.Height = size.Height;
        return Placement.Centred(screen, new Size(1e6, 1e6)).Size;
    }

    /// <summary>A fixed-size window whose view sets its size (setup's steps): no bigger than the display has room for
    /// (the view scrolls inside), and after it grows, still on the display. <paramref name="showing"/>: only while it's
    /// the view in the window; <paramref name="open"/> false for a second view that isn't showing yet.</summary>
    static void FollowView(Window w, Control view, Func<bool>? showing = null, bool open = true)
    {
        var room = open ? OpenCentred(w, new Size(view.Width, view.Height)) : Placement.Centred(ScreenFor(w), new Size(1e6, 1e6)).Size;
        view.MaxWidth = room.Width;
        view.MaxHeight = room.Height;
        view.PropertyChanged += (_, e) =>
        {
            if (showing?.Invoke() == false) return;
            if (e.Property == Layoutable.WidthProperty) w.Width = Math.Min(view.Width, room.Width);
            else if (e.Property == Layoutable.HeightProperty) w.Height = Math.Min(view.Height, room.Height);
            else return;
            var screen = ScreenFor(w);
            var size = new PixelSize((int)(w.Width * screen.Scaling), (int)(w.Height * screen.Scaling));
            var inside = Placement.KeepInside(screen.WorkingArea, size, w.Position);
            if (inside != w.Position) w.Position = inside;
        };
    }

    /// <summary>Remembers where the library window is and its size, so it opens there next time (it's being hidden,
    /// or the app is quitting). Zoomed, it keeps the size it had before, and full screen isn't remembered.</summary>
    static void SaveLibraryPlace()
    {
        if (mainWindow is not { IsVisible: true } w || w.WindowState is WindowState.FullScreen or WindowState.Minimized) return;
        bool zoomed = w.WindowState == WindowState.Maximized;
        var place = zoomed && host.Settings.LibraryWindow is { } before
            ? before with { Zoomed = true }
            : new WindowPlace(w.Position.X, w.Position.Y, w.ClientSize.Width, w.ClientSize.Height, zoomed);
        host.Save(s => s.LibraryWindow = place);
    }

    public static void ShowLibrary()
    {
        if (!host.Settings.SetupDone)
        {
            ShowSetup();
            return;
        }
        var w = MainWindow();
        library.DrawChrome = false;
        w.Show();
        UpdateDock();
        w.Activate();
        Desktop.Activate();
        _ = LoadLibraryAsync();
    }

    /// <summary>Settings → General's Run setup again: Settings makes way, and setup opens (guided, straight to the chat
    /// when the AI picked before is still signed in; <paramref name="byHand"/>: setup by hand, on its welcome) with
    /// what's already set up filled in. The computer counts as set up throughout, so closing it part-way changes
    /// nothing.</summary>
    public static void RunSetupAgain(bool byHand = false)
    {
        settingsWindow?.Close();
        ShowSetup(byHand);
    }

    public static void ShowSetup() => ShowSetup(false);

    /// <summary>
    /// Setup's window: guided setup (pick an AI, install it, sign in, then the chat), or setup by hand when
    /// <paramref name="byHand"/>. Both are over one <see cref="SetupModel"/>, swapped in the same window, so nothing
    /// done in one is lost in the other. The setup tools' door opens with the window and closes with it.
    /// </summary>
    public static void ShowSetup(bool byHand)
    {
        if (setupWindow is { IsVisible: true })
        {
            setupWindow.Activate();
            return;
        }
        library.Support = null; // the ask for a tip never shows during setup
        bool again = host.Settings.SetupDone;
        setup = Setup.Make(host);
        setup.Again = again;
        var model = setup;
        var manualView = Skin.Current == SkinKind.Mac ? (Control)new MacSetup { DataContext = setup, DrawChrome = false } : new WinSetup { DataContext = setup, DrawChrome = false };
        var guided = guidedSetup = MakeGuided(model);
        var guidedView = Skin.Current == SkinKind.Mac ? (Control)new MacGuidedSetup { DataContext = guided, DrawChrome = false } : new WinGuidedSetup { DataContext = guided, DrawChrome = false };
        if (byHand)
        {
            guided.Screen = GuidedScreen.Manual;
            model.GuidedLabel = "Set up with an AI instead";
        }
        var view = byHand ? manualView : guidedView;
        var w = new Window
        {
            Title = byHand ? setup.HeaderTitle : "Set up Study Stash", CanResize = false, CanMaximize = false, Content = view,
            ExtendClientAreaToDecorationsHint = true, ExtendClientAreaTitleBarHeightHint = Skin.Current == SkinKind.Mac ? WindowHeader.MacHeight : 32,
        };
        Look.Apply(w);
        AppMenu.Attach(w, ShowLibrary, SettingsFromAnywhere);
        if (Skin.Current == SkinKind.Mac) MacTitleBar.Attach(w);
        WinChrome.Apply(w);
        var mic = micCheck = new MicCheck();
        setup.OnFinish = () =>
        {
            Setup.Finish(model, host);
            w.Close();
            ShowLibrary();
            // The first run ends by saying where the S. lives (or that a full menu bar hides it).
            if (!again) DispatcherTimer.RunOnce(SayWhereTheIconIs, TimeSpan.FromSeconds(1));
        };
        guided.OnFinish = () => model.OnFinish?.Invoke();
        setup.OnEnter = step => EnterSetupStep(model, step);
        setup.OnCopy = text => _ = w.Clipboard?.SetTextAsync(text);
        // Setup by hand's link back to the guided setup, named for the AI when it's ready.
        setup.OnGuided = () => guided.BackToChatCommand.Execute(null);
        // The library's setup and the laptop's have their own names: the window's follows the flow.
        setup.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SetupModel.HeaderTitle) && w.Content == manualView) w.Title = model.HeaderTitle;
        };
        // The AI and Canvas steps are the design's bigger window, and so is the chat (each view sizes itself): the
        // window follows whichever is showing.
        FollowView(w, view, () => w.Content == view);
        var other = byHand ? guidedView : manualView;
        FollowView(w, other, () => w.Content == other, open: false);
        guided.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(GuidedSetupModel.Screen)) return;
            var want = guided.Screen == GuidedScreen.Manual ? manualView : guidedView;
            model.GuidedLabel = guided.AiReady ? $"Set up with {guided.Brand} instead" : "Set up with an AI instead";
            if (w.Content == want) return;
            w.Content = want;
            w.Title = want == manualView ? model.HeaderTitle : "Set up Study Stash";
            FitTo(w, want);
        };
        // The AI engines step saves its choice before moving on; if it can't, it says why and stays.
        var leave = setup.LeaveAsync;
        setup.LeaveAsync = async step =>
        {
            if (step != SetupStep.Ai) return leave is null || await leave(step);
            if (model.Ai is not { } ai) return true;
            if (!await ai.SaveAsync()) return false;
            model.NotesSummary = ai.ChoiceWords;
            return true;
        };
        SetupMcpHost? door = null;
        bool closed = false;
        w.Closed += (_, _) =>
        {
            closed = true;
            setupWindow = null;
            if (setup == model) setup = null;
            if (guidedSetup == guided) guidedSetup = null;
            guided.Dispose();
            if (door is not null) _ = door.DisposeAsync().AsTask();
            mic.Close();
            if (micCheck == mic) micCheck = null;
            model.Canvas?.Dispose();
            UpdateDock();
        };
        setupWindow = w;
        w.Show();
        UpdateDock();
        w.Activate();
        Desktop.Activate();
        _ = OpenGuidedAsync();

        async Task OpenGuidedAsync()
        {
            try
            {
                var tools = new SetupTools(new GuidedSetup(guided, host) { MakeCanvas = () => SetupCanvasAsync(model) });
                door = await SetupMcpHost.StartAsync(tools);
                if (closed)
                {
                    await door.DisposeAsync();
                    return;
                }
                guided.Door = new SetupDoor(door.Url, door.Token, tools.NewTurn);
                if (!byHand) await guided.OpenAsync();
            }
            catch (Exception e) when (e is IOException or InvalidOperationException or System.Net.Sockets.SocketException)
            {
                // No door, no chat: guided setup can't run here, and setup by hand always can.
                host.Log($"[setup] guided setup couldn't start: {e.Message}");
                guided.Screen = GuidedScreen.Manual;
            }
        }
    }

    /// <summary>Guided setup's model for this window, reaching the app's own installer, sign-in, terminal and pages.
    /// The self-test finds no AI's CLI at all, so it never installs one or reaches an account.</summary>
    static GuidedSetupModel MakeGuided(SetupModel model)
    {
        var services = new GuidedServices
        {
            Home = host.Home,
            Find = SelfTest.Dir is not null ? _ => AgentFound.None : cli => AgentInstall.Find(cli),
            OpenTerminal = (exe, args) =>
            {
                try
                {
                    Core.Ai.Terminal.RunCommand(host.Home, OperatingSystem.IsMacOS() ? "terminal" : "", exe, args);
                }
                catch (InvalidOperationException e)
                {
                    host.Log($"[setup] couldn't open a terminal to sign in: {e.Message}");
                }
            },
            Downloading = () => host.Downloading?.Fraction,
            Log = host.Log,
        };
        return new GuidedSetupModel(model, services, () => host.Settings, host.Save);
    }

    /// <summary>The browser helper card's Canvas connection, as the Canvas step makes it, started before the card shows.</summary>
    static async Task<CanvasConnectModel?> SetupCanvasAsync(SetupModel model)
    {
        if (host.Remote() is null) return null;
        var watch = CanvasPoll();
        var connect = new CanvasConnectModel(Canvas(), watch, forSetup: true) { ShowFooter = false, FinishLabel = model.ContinueLabel };
        connect.OnSkip = () => model.SkipCommand.Execute(null);
        connect.OnFinish = () => model.NextCommand.Execute(null);
        await StartConnectAsync(connect, watch);
        return connect;
    }

    /// <summary>The window takes the size of the view now showing in it (still on the display).</summary>
    static void FitTo(Window w, Control view)
    {
        var screen = ScreenFor(w);
        double width = Math.Min(view.Width, view.MaxWidth), height = Math.Min(view.Height, view.MaxHeight);
        w.Width = width;
        w.Height = height;
        var size = new PixelSize((int)(width * screen.Scaling), (int)(height * screen.Scaling));
        var inside = Placement.KeepInside(screen.WorkingArea, size, w.Position);
        if (inside != w.Position) w.Position = inside;
    }

    /// <summary>Guided setup's model, while its window is open.</summary>
    static GuidedSetupModel? guidedSetup;

    /// <summary>The AI and Canvas steps need the library (connected two steps before): their models are made as each
    /// opens, reading the library then.</summary>
    static void EnterSetupStep(SetupModel model, SetupStep step)
    {
        switch (step)
        {
            case SetupStep.Ai:
                // Made afresh when setup changed its mind about which computer this is (the library may have been
                // made again, with another password), so the step reads the library as it is now.
                var aiNow = (model.Role, host.Client().PoolKey);
                if (model.Ai is null || aiMadeFor != aiNow)
                {
                    aiMadeFor = aiNow;
                    model.Ai = new AiSetupModel(Ai())
                    {
                        Lede = model.IsOneComputer
                            ? $"They run on this {model.DeviceWord}, as part of your library. You can change this later in Settings."
                            : "This computer is your library, so the engines run here. You can change this later from any of your computers.",
                        Windows = Skin.Current == SkinKind.Win,
                        Copy = text => model.OnCopy?.Invoke(text),
                        OpenTerminal = TerminalApp.Open,
                        OpenUrl = url => Dialogs.OpenUrl(url),
                        WriteNotes = WriteNotesAsync,
                    };
                }
                _ = LoadAiStepAsync(model.Ai);
                break;
            case SetupStep.Canvas when model.Canvas is null:
                var watch = CanvasPoll();
                var connect = new CanvasConnectModel(Canvas(), watch, forSetup: true) { ShowFooter = false, FinishLabel = model.ContinueLabel };
                connect.OnSkip = () => model.SkipCommand.Execute(null);
                connect.OnFinish = () => model.NextCommand.Execute(null);
                model.Canvas = connect;
                _ = StartConnectAsync(connect, watch);
                break;
        }
    }

    /// <summary>The AI step reads the library; "No subscription? Use a free model" on guided setup's first screen
    /// comes here with the free model on this computer picked for the notes.</summary>
    static async Task LoadAiStepAsync(AiSetupModel ai)
    {
        await ai.Load();
        if (host.Settings.SetupAi == "ollama" && ai.Engines.Any(e => e.Id == "ollama")) ai.SelectedNotes = "ollama";
    }

    /// <summary>Which flow, and which library password, setup's AI step was made for.</summary>
    static (AppRole Role, string Key)? aiMadeFor;

    /// <summary>Setup's "No AI for now" (and picking an engine after it): the library writes notes, and sorts with AI,
    /// or doesn't. True when the library took it.</summary>
    static async Task<bool> WriteNotesAsync(bool on)
    {
        if (host.Remote() is not { } lib) return false;
        try
        {
            return await lib.SettingsAsync(HttpMethod.Post, "", new JsonObject
            {
                ["notes"] = new JsonObject { ["write"] = on, ["sort"] = on },
            }) is not null;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or LibraryRefusedException)
        {
            host.Log($"[setup] notes {(on ? "on" : "off")}: {e.Message}");
            return false;
        }
    }

    public static void ShowSettings() => ShowSettings(null);

    /// <summary>The app menu's Settings… (⌘,): see <see cref="SettingsFromAnywhere"/>.</summary>
    public static void SettingsFromMenu()
    {
        if (host is not null) SettingsFromAnywhere();
    }

    /// <summary>Settings from the dropdown's gear, the app menu or ⌘, (Ctrl+,) anywhere: the dropdown and the quick
    /// panel make way for it; before setup's done, setup comes forward instead.</summary>
    /// <summary>An install on a model heavier than this computer keeps up with (large-v3 on a PC with no graphics card
    /// Whisper can use) hears once, a little after starting, that a lighter one would. Nothing switches by itself.</summary>
    static async Task SuggestLighterModelAsync()
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(5), stop.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        if (await host.ModelSuggestionAsync().ConfigureAwait(false) is not { } advice) return;
        Dispatcher.UIThread.Post(() =>
        {
            host.ModelSuggestionMade(advice.Model);
            Program.Log($"[model] suggested {advice.Model.Name} in place of {host.Model.Name}");
            var (title, text) = NoticeWords.LighterModel(advice);
            Toast(title, text, "Settings", () => ShowSettings("Recording"), NoticeTimes.Advice);
        });
    }

    static void SettingsFromAnywhere()
    {
        panelWindow?.Hide();
        quickWindow?.Hide();
        if (!host.Settings.SetupDone) ShowSetup();
        else ShowSettings();
    }

    /// <summary>Settings, open at <paramref name="section"/> when one's given ("AI", "Access", "Canvas"…).</summary>
    public static void ShowSettings(string? section)
    {
        if (settingsWindow is { IsVisible: true })
        {
            if (section is not null && settingsWindow.Content is Control { DataContext: SettingsModel open }) open.Section = section;
            settingsWindow.Activate();
            return;
        }
        var model = SettingsModel.Make(host, canvas: Canvas(), watch: CanvasPoll());
        model.Keys.Listening = PauseShortcuts;
        model.Canvas.Status.OnConnect = ShowCanvasConnect;
        model.Canvas.Status.OnShowMeHow = ShowCanvasConnect;
        if (section is not null) model.Section = section;
        var w = new Window
        {
            Title = "Study Stash settings", CanResize = false, CanMaximize = false,
            Content = new SettingsView { DataContext = model, DrawChrome = false },
            ExtendClientAreaToDecorationsHint = true, ExtendClientAreaTitleBarHeightHint = Skin.Current == SkinKind.Mac ? WindowHeader.MacHeight : 32,
        };
        // The view's own size (900 × 860 on Windows), smaller on a small screen: its pages scroll inside.
        FollowView(w, (Control)w.Content!);
        Look.Apply(w);
        AppMenu.Attach(w, ShowLibrary, SettingsFromAnywhere);
        model.Lib.Copy = text => _ = w.Clipboard?.SetTextAsync(text);
        // AI tool access's Copy buttons (the web address, a fix link, the setup for another app).
        model.Access.Copy = text => w.Clipboard?.SetTextAsync(text) ?? Task.CompletedTask;
        model.Lib.ClassesChanged = LibraryClassesChanged;
        model.Canvas.OnClassesChanged = LibraryClassesChanged;
        if (Skin.Current == SkinKind.Mac) MacTitleBar.Attach(w);
        WinChrome.Apply(w);
        w.Closed += (_, _) =>
        {
            settingsWindow = null;
            model.Dispose();
            UpdateDock();
        };
        settingsWindow = w;
        w.Show();
        UpdateDock();
        w.Activate();
        Desktop.Activate();
    }

    /// <summary>A Mac shows the app in the Dock (and ⌘Tab) while the library, setup or Settings is open, and keeps it
    /// to the menu bar otherwise.</summary>
    static void UpdateDock() =>
        Desktop.ShowInDock(!quitting && (mainWindow?.IsVisible == true || setupWindow?.IsVisible == true || settingsWindow?.IsVisible == true
            || canvasConnectWindow?.IsVisible == true));

    // --- keeping it all up to date ------------------------------------------------------------------------------------

    static void Tick()
    {
        SaySettledProblems();
        if (setup is not null && micCheck is not null) Setup.TickMic(setup, host, micCheck);
        var live = host.Recorder.Current;
        if (live is null) return;
        // Said once a lecture, when Whisper clearly can't keep up with it.
        if (behindToldFor != live.Id && host.FallingBehind() is { } behind)
        {
            behindToldFor = live.Id;
            Program.Log($"[whisper] {live.Id}: the transcript is {TimedText.Clock(host.Recorder.Elapsed - live.TranscribedSeconds)} behind with {host.Model.Name}");
            Toast(behind.Title, behind.Text, "Settings", () => ShowSettings("Recording"), NoticeTimes.Advice);
        }
        host.WatchLiveWords(WatchingWords());
        string elapsed = TimedText.Clock(host.Recorder.Elapsed);
        var levels = host.Recorder.Levels();
        panel.Elapsed = recorder.Elapsed = elapsed;
        panel.Levels = recorder.Levels = levels;
        // The menu bar shows the time beside the icon (the tray, on hover); a menu bar that turned dark or light since
        // gets the recording icon in its new ink.
        if (OperatingSystem.IsMacOS())
        {
            MacStatusItem.ShowElapsed(elapsed);
            if (trayRecording && MacStatusItem.DarkMenuBar() != trayDark) MacStatusItem.SetIcon(TrayImageBytes(true), template: false);
            if (trayRecording && !trayLogged)
            {
                trayLogged = true;
                Program.Log($"[tray] recording: the S. with its red dot, {(trayDark ? "white on a dark" : "black on a light")} menu bar, \"{MacStatusItem.Title()}\" beside it");
            }
        }
        else if (tray is not null)
        {
            tray.ToolTipText = $"Study Stash · recording {elapsed}";
        }
    }

    static DateTime lastRefreshAt = DateTime.MinValue;
    static bool refreshDue, refreshRetryQueued;

    /// <summary>The host changed (called from any thread, often several times a second): at most one <see cref="Refresh"/>
    /// runs every 100 ms, on the UI thread, however many times this fires in between.</summary>
    static void RequestRefresh()
    {
        refreshDue = true;
        Dispatcher.UIThread.Post(TryRefresh);
    }

    static void TryRefresh()
    {
        if (quitting || !refreshDue) return;
        var now = DateTime.UtcNow;
        var since = now - lastRefreshAt;
        if (since < TimeSpan.FromMilliseconds(100))
        {
            if (!refreshRetryQueued)
            {
                refreshRetryQueued = true;
                DispatcherTimer.RunOnce(() =>
                {
                    refreshRetryQueued = false;
                    TryRefresh();
                }, TimeSpan.FromMilliseconds(100) - since);
            }
            return;
        }
        refreshDue = false;
        lastRefreshAt = now;
        Refresh();
    }

    static void Refresh()
    {
        if (quitting) return;
        ApplyShortcutsSetting();
        var live = host.Recorder.Current;
        liveId = live?.Id;
        bool recording = live is not null;
        if (recording) library.Support = null; // the ask for a tip never shows while recording
        panel.IsRecording = recording;
        panel.IsPaused = recorder.IsPaused = live?.State == LectureState.Paused;
        string cls = recording ? live!.ClassName : RecordClass() is { Length: > 0 } picked ? picked : CalendarClass();
        panel.ClassName = recorder.ClassName = cls;
        int color = host.ColorOf(cls);
        panel.ClassDot = recorder.ClassDot = color >= 0 ? Skin.ClassDot(color) : Brushes.Gray;
        var problems = Problems.All(host).ToList();
        var problem = problems.FirstOrDefault();
        currentProblem = problem;
        SayNewProblems(problems);
        panel.ProblemTitle = problem?.Title;
        panel.ProblemDetail = problem?.Detail;
        panel.ProblemAction = problem is { HasAction: true } p ? p.ActionLabel : null;
        panel.CanRecord = recording || (host.ModelReady && host.MicAccess() is not (MicAccess.Denied or MicAccess.Restricted));
        panel.Hint = recording ? null
            : !panel.CanRecord && problem is not null ? problem.Title
            : RecordClass().Length > 0 ? "Picked by you"
            : CalendarHint() is { } fromCalendar ? fromCalendar
            : ClassPicker.SortHint;
        var (status, good) = host.Status();
        panel.Status = status;
        panel.StatusGood = good;
        // A library-only computer doesn't record: its dropdown says what the library is doing instead.
        panel.LibraryOnly = host.Settings.Role == AppRole.Library;
        if (tray is not null && trayRole != host.Settings.Role)
        {
            trayRole = host.Settings.Role;
            tray.Menu = TrayMenu(host.Settings.Role);
        }
        panel.Library = panel.LibraryOnly
            ? LibraryPanelWords.From(host.Library == LibraryState.Connected || host.LocalLibrary?.State is LibraryServiceState.Running or LibraryServiceState.Elsewhere,
                host.Library == LibraryState.Starting || host.LocalLibrary?.State == LibraryServiceState.Starting,
                OperatingSystem.IsMacOS() ? "Mac" : "PC", host.Overview, canvasWatch?.State, DateTimeOffset.Now, TimeZoneInfo.Local)
            : null;
        KeepCanvasWatched();
        UpdateComingUp();
        library.Status = LibraryStatus();
        library.StatusGood = host.Library == LibraryState.Connected;
        // The library just came back: the window, if it's open, is loaded again (its classes, and the class showing) so
        // a note written while it was gone shows up without reopening the window.
        if (lastLibraryState != LibraryState.Connected && host.Library == LibraryState.Connected) RequestLibraryReload(whole: true);
        lastLibraryState = host.Library;
        RefreshRecent();
        if (trayRecording != recording)
        {
            trayRecording = recording;
            if (OperatingSystem.IsMacOS())
            {
                MacStatusItem.SetIcon(TrayImageBytes(recording), template: !recording);
                if (!recording)
                {
                    MacStatusItem.ShowElapsed("");
                    trayLogged = false;
                    Program.Log($"[tray] idle: the S. mark as a template image, \"{MacStatusItem.Title()}\" beside it");
                }
            }
            else if (tray is not null)
            {
                tray.Icon = TrayImage(recording);
                if (!recording) tray.ToolTipText = "Study Stash";
            }
        }
        if (OperatingSystem.IsWindows()) UpdateTaskbar(problem);
        if (setup is not null) Setup.Refresh(setup, host);
    }

    static int lastTaskbarPercent = -1;

    /// <summary>Windows: every visible regular window's taskbar button shows the model's download, or a lecture
    /// transcribing, or turns red while <paramref name="problem"/> is stopping a lecture being recorded.</summary>
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    static void UpdateTaskbar(AppProblem? problem)
    {
        double? fraction = !host.ModelReady && host.Downloading is { } d ? d.Fraction
            : host.Lectures.All().FirstOrDefault(l => l.State == LectureState.Transcribing)?.Progress;
        bool blocked = !panel.CanRecord && problem is not null;
        foreach (var w in new[] { mainWindow, setupWindow, settingsWindow })
            if (w?.IsVisible == true && w.TryGetPlatformHandle()?.Handle is IntPtr hwnd)
                Desktop.TaskbarProgress(hwnd, fraction, blocked);
        int percent = fraction is double f ? (int)(Math.Round(f * 10) * 10) : -1;
        if (percent != lastTaskbarPercent && percent >= 0) Program.Log($"[taskbar] {percent}%");
        lastTaskbarPercent = percent;
    }

    /// <summary>The dropdown's recent lectures: this laptop's (where each is on its way), newest first.</summary>
    static void RefreshRecent()
    {
        // A lecture written down after class is recording: one still waiting for Whisper waits until it stops too.
        bool inClass = host.Recorder.Current is { AfterClass: true };
        var items = host.Lectures.All().Where(l => l.Id != liveId).Take(4).Select(l =>
        {
            int color = host.ColorOf(l.FiledClass.Length > 0 ? l.FiledClass : l.ClassName);
            return new LectureItem
            {
                Id = l.Id,
                State = l.State,
                Title = l.FiledTitle.Length > 0 ? l.FiledTitle : l.ClassName.Length > 0 ? $"{l.ClassName} lecture" : "Lecture",
                Detail = l.State switch
                {
                    LectureState.Transcribing when inClass => $"Transcribing after this class ({Math.Round(l.Progress * 100)}%)",
                    LectureState.Transcribing => $"Transcribing {Math.Round(l.Progress * 100)}%",
                    LectureState.Sending => host.Library == LibraryState.Connected ? "Sending…" : "Waiting for your library",
                    LectureState.Writing => "Writing notes…",
                    LectureState.Filed => $"Filed in {(l.FiledClass.Length > 0 ? l.FiledClass : "your library")}",
                    LectureState.Failed => l.Error.Length > 0 ? l.Error : "Something went wrong",
                    _ => "Recording…",
                },
                Progress = l.State == LectureState.Transcribing ? l.Progress : null,
                Busy = l.State == LectureState.Writing,
                Problem = l.State == LectureState.Failed,
                Time = When(l.StartedAt.LocalDateTime),
                Dot = color >= 0 ? Skin.ClassDot(color) : Brushes.Gray,
            };
        }).ToList();
        bool same = items.Count == panel.Recent.Count && items.Zip(panel.Recent).All(p => p.First.Id == p.Second.Id && p.First.Detail == p.Second.Detail
            && p.First.Title == p.Second.Title && p.First.Progress == p.Second.Progress);
        if (same) return;
        panel.Recent.Clear();
        foreach (var i in items) panel.Recent.Add(i);
    }

    /// <summary>"11:40" today, "Mon" this week, "23 Sep" before.</summary>
    public static string When(DateTime t)
    {
        var today = DateTime.Today;
        if (t.Date == today) return t.ToString("H:mm", CultureInfo.InvariantCulture);
        if (t.Date > today.AddDays(-6)) return t.ToString("ddd", CultureInfo.InvariantCulture);
        return t.ToString("d MMM", CultureInfo.InvariantCulture);
    }

    // --- audio ---------------------------------------------------------------------------------------------------------

    /// <summary>Play the recording from a moment, when this laptop still has it; otherwise open the lecture's
    /// transcript there.</summary>
    static void Play(string? lectureId, double at)
    {
        if (lectureId is not null && File.Exists(host.Lectures.AudioPath(lectureId)))
        {
            Player.Play(host.Lectures.AudioPath(lectureId), Math.Max(0, at - 2));
            return;
        }
        if (lectureId is not null) OpenLecture(lectureId, transcript: true);
    }
}
