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

    /// <summary>The class Record will use, picked by hand; null follows the timetable.</summary>
    static string? chosenClass;
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
        host = new AppHost(home, laptop: new LaptopHost(), log: Program.Log);
        Skin.UseTheme(ColourThemes.Find(host.Settings.Theme));
        host.Changed += RequestRefresh;
        host.Heard += (l, lines) => Dispatcher.UIThread.Post(() => AddHeard(l, lines));
        host.Filed += l => Dispatcher.UIThread.Post(() =>
        {
            Toast($"Filed in {(l.FiledClass.Length > 0 ? l.FiledClass : "your library")}",
                l.FiledTitle.Length > 0 ? l.FiledTitle : "The notes are written.", "Open note", () => OpenLecture(l.Id));
            RequestLibraryReload();
        });
        host.Problem += (title, why) => Dispatcher.UIThread.Post(() => Toast(title, why, null, null));
        if (host.PretendMic) Program.Log("[app] recording from a pretend microphone (STUDYSTASH_MIC_FILE)");
        Wire();
        host.Start();
        AppUpdates.Start(host, stop.Token);
        MakeTray();
        // A Mac's app menu (About, Settings… ⌘,, and the system's Hide and Quit ⌘Q) while a window is in front.
        if (OperatingSystem.IsMacOS()) AppMenu.Use(app, AppMenu.ShowAbout, SettingsFromAnywhere);
        ApplyShortcutsSetting(force: true);
        ticker = new DispatcherTimer(TimeSpan.FromMilliseconds(250), DispatcherPriority.Background, (_, _) => Tick());
        ticker.Start();
        Refresh();
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
        /// <summary>Setup's own view model, while its window is open: what the self-test drives (steps, connect,
        /// find, add a class) the same way the view's bindings would.</summary>
        public static ViewModels.SetupModel? SetupModel => setup;
        public static void TogglePanel() => Shell.TogglePanel();
        public static void ToggleQuick() => Shell.ToggleQuick();
        public static void Record() => ToggleRecording();
        public static void StopRecording() => Shell.StopRecording();
        public static void TogglePause() => Shell.TogglePause();
        public static void ShowRecorder(bool expanded) => Shell.ShowRecorder(expanded);
        /// <summary>Record the way the global shortcut would, so the self-test exercises that path too (setup first,
        /// if it isn't done).</summary>
        public static void RecordViaShortcut() => OnShortcut(Shortcut.Record);
        /// <summary>Opens a lecture's notes (or transcript) in the library window, as clicking it would.</summary>
        public static void OpenLecture(string id, bool transcript = false) => Shell.OpenLecture(id, transcript);
        /// <summary>Searches the quick panel, as typing in it would.</summary>
        public static Task Search(string query) => SearchAsync(query);
        public static QuickModel QuickModel => quick;
        public static PanelModel PanelModel => panel;

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
        bool wanted = host.Settings.Shortcuts;
        if (!force && wanted == hotkeysOn) return;
        hotkeysOn = wanted;
        if (wanted)
        {
            hotkeys = Hotkeys.Register(OnShortcut);
            if (!hotkeys.All) Program.Log("[app] a shortcut is taken by another app");
        }
        else
        {
            Hotkeys.Unregister();
            hotkeys = new HotkeyResult(true, true);
        }
    }

    /// <summary>Settings' words about the shortcuts: which one (if any) another app already has.</summary>
    public static string? ShortcutsSay()
    {
        if (!hotkeysOn) return null;
        string quick = Skin.Current == SkinKind.Mac ? "⌥Space" : "Alt+Shift+Space";
        string record = Skin.Current == SkinKind.Mac ? "⌥⇧R" : "Ctrl+Alt+R";
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
                record: ToggleRecording, search: ToggleQuick, open: ShowLibrary, settings: ShowSettings, quit: () => Quit());
            MacStatusItem.SetIcon(TrayImageBytes(false));
            // Once the menu bar has laid it out: where it is, and whether the student can see it at all.
            DispatcherTimer.RunOnce(CheckMenuBarIcon, TimeSpan.FromSeconds(2));
            return;
        }
        tray = new TrayIcon { Icon = TrayImage(false), ToolTipText = "Study Stash", IsVisible = true };
        tray.Clicked += (_, _) => TogglePanel();
        var menu = new NativeMenu();
        void Item(string title, Action act)
        {
            var i = new NativeMenuItem(title);
            i.Click += (_, _) => act();
            menu.Add(i);
        }
        Item("Record", ToggleRecording);
        Item("Search notes and lectures", ToggleQuick);
        Item("Open Study Stash", ShowLibrary);
        Item("Settings…", ShowSettings);
        menu.Add(new NativeMenuItemSeparator());
        Item("Quit Study Stash", () => Quit());
        tray.Menu = menu;
        TrayIcon.SetIcons(app, new TrayIcons { tray });
        // The tray's ink is black or white depending on the theme; redraw it when that changes.
        app.ActualThemeVariantChanged += (_, _) => tray.Icon = TrayImage(trayRecording);
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
        var (title, text) = IconWords.WhereItIs(mac, hidden);
        // Longer than most: it's the one way to find the app when its icon can't be seen.
        Toast(title, text, hidden ? IconWords.ShowIt : null, hidden ? MoveIconIntoView : null, TimeSpan.FromSeconds(hidden ? 30 : 12));
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
        panel.OnSwitchClass = PickClass;
        panel.OnFixProblem = FixProblem;
        panel.OnOpenLecture = OpenRecentLecture;

        recorder.OnPause = TogglePause;
        recorder.OnStop = () => StopRecording();
        recorder.OnExpand = expanded => PlaceRecorder();

        quick.OnQuery = q => _ = SearchAsync(q);
        quick.OnAsk = AskQuick;
        quick.OnOpen = OpenQuickRow;
        quick.OnClose = () => quickWindow?.Hide();

        library.OnClass = c =>
        {
            library.NarrowDetail = false;
            if (c.IsDue)
            {
                dueSelection = null;
                _ = ShowDueAsync();
                return;
            }
            allLectures = false;
            _ = ShowClassAsync(c.Name);
        };
        library.OnLecture = l => OpenFromList(() => ShowLectureAsync(l.Id));
        library.OnSearch = ToggleQuick;
        library.OnSettings = ShowSettings;
        library.OnMove = MoveLecture;
        library.OnExport = () => _ = ExportAsync();
        library.OnMore = MoreMenu;
    }

    // --- recording ----------------------------------------------------------------------------------------------------

    static string RecordClass() => chosenClass ?? host.ClassNow()?.Name ?? "";

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
        if (!host.ModelReady)
        {
            Toast("The model isn't downloaded yet", host.Downloading is not null ? "It's downloading: Record works once it's done." : "Download it in Settings → Recording.",
                "Settings", ShowSettings);
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
            Toast(trouble.Title, trouble.Detail, trouble.HasAction ? trouble.ActionLabel : null,
                trouble.ActionUrl.Length > 0 ? () => Dialogs.OpenUrl(trouble.ActionUrl) : null);
        }
        else if (start.Lecture is { } l)
        {
            liveId = l.Id;
            recorder.Lines.Clear();
            recorder.Ask = LiveAsk();
            recorder.Waiting = "What's said shows here a few seconds after it's said.";
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
        chosenClass = null;
        recorderWindow?.Hide();
        if (l is { State: LectureState.Failed }) Toast(l.Error, "Its sound file is damaged, so it can't be written down.", null, null);
        else if (l is not null) Toast("Recording saved", "Study Stash is writing it down; the library files it and writes your notes.", null, null);
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
            Program.Log($"[panel] class picked: {(name is null ? "follow the timetable" : name.Length == 0 ? "let the library sort it" : name)}");
            Refresh();
        });
        if (panelWindow is null) return;
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
    static void FixProblem()
    {
        switch (currentProblem?.Kind)
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
                ShowSettings();
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

    static void AddHeard(Lecture l, IReadOnlyList<Spoken> lines)
    {
        if (l.Id != liveId) return;
        foreach (var old in recorder.Lines.Where(x => x.Latest).ToList())
            recorder.Lines[recorder.Lines.IndexOf(old)] = new HeardLine { Time = old.Time, Text = old.Text };
        for (int i = 0; i < lines.Count; i++)
            recorder.Lines.Add(new HeardLine { Time = TimedText.Clock(lines[i].Start), Text = lines[i].Text, Latest = i == lines.Count - 1 });
        while (recorder.Lines.Count > 200) recorder.Lines.RemoveAt(0);
        panel.LastLine = $"“…{Trim(lines[^1].Text, 90)}”";
    }

    static string Trim(string s, int n) => s.Length <= n ? s : s[..n].TrimEnd() + "…";

    /// <summary>The recorder's chat for a new lecture: asks about what's been said so far, with any engine (the
    /// library's default for questions until another is picked).</summary>
    static AiAskModel LiveAsk()
    {
        var ask = new AiAskModel(Ai())
        {
            Live = () => host.Recorder.Current?.Transcript(),
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
            panelWindow = new Floating { Content = PanelView(), CloseOnDeactivate = true, Title = "Study Stash" };
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
        panelWindow.Position = OperatingSystem.IsMacOS()
            ? Placement.MacDropdown(anchor, panelWindow.ScreenList(), size, room)
            : Placement.TrayFlyout(anchor, panelWindow.ScreenList(), size, room);
        panelWindow.Show();
        panelWindow.Activate();
        Desktop.Activate();
    }

    static void ShowRecorder(bool expanded)
    {
        recorder.Expanded = expanded;
        recorderWindow ??= MakeRecorderWindow();
        PlaceRecorder();
        recorderWindow.Show();
    }

    static Floating MakeRecorderWindow()
    {
        var view = Skin.Current == SkinKind.Mac ? (Control)new MacRecorder { DataContext = recorder } : new WinRecorder { DataContext = recorder };
        var w = new Floating { Content = view, Title = "Study Stash recorder" };
        AppMenu.AddSettingsKey(w, SettingsFromAnywhere);
        // Drag it anywhere by its background; where it lands is saved once the drag ends, not on every pixel moved.
        // The small pill moves by hand, so a press that never moves is a click, which opens the recorder.
        Point? pressed = null;
        bool moved = false;
        view.PointerPressed += (_, e) =>
        {
            if (e.Source is TextBox || !e.GetCurrentPoint(view).Properties.IsLeftButtonPressed) return;
            if (recorder.Expanded)
            {
                w.BeginMoveDrag(e);
                return;
            }
            pressed = e.GetPosition(view);
            moved = false;
            e.Pointer.Capture(view);
        };
        view.PointerMoved += (_, e) =>
        {
            if (pressed is not { } from) return;
            var by = e.GetPosition(view) - from;
            if (!moved && Math.Abs(by.X) + Math.Abs(by.Y) < 4) return;
            moved = true;
            // The window follows the pointer, so the pointer stays over the same spot of the pill.
            w.Position += new PixelVector((int)Math.Round(by.X * w.DesktopScaling), (int)Math.Round(by.Y * w.DesktopScaling));
        };
        view.PointerReleased += (_, e) =>
        {
            if (pressed is not null)
            {
                pressed = null;
                e.Pointer.Capture(null);
                if (!moved)
                {
                    recorder.ToggleCommand.Execute(null);
                    return;
                }
            }
            SaveRecorderPosition();
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

    /// <summary>Remembers the recorder's top right corner, so it comes back there next time (a drag just ended, or
    /// the window is about to hide or the app to quit).</summary>
    static void SaveRecorderPosition()
    {
        if (recorderWindow is not { IsVisible: true } w) return;
        var (_, scale) = w.WorkArea(w.Position);
        var size = w.Measured(scale);
        host.Save(s =>
        {
            s.RecorderX = w.Position.X + size.Width;
            s.RecorderY = w.Position.Y;
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
        recorderWindow.Position = Placement.KeepOnScreen(saved, recorderWindow.ScreenList(), size, OperatingSystem.IsMacOS(), room);
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
            quickWindow = new Floating { Content = view, CloseOnDeactivate = true, Title = "Study Stash search" };
            AppMenu.AddSettingsKey(quickWindow, SettingsFromAnywhere);
        }
        quick.Answering = false;
        quick.Query = "";
        _ = SearchAsync("");
        var pointer = Floating.Pointer();
        var (_, scale) = quickWindow.WorkArea(pointer);
        var size = quickWindow.Measured(scale);
        int room = (int)(Floating.ShadowRoom * scale);
        quickWindow.Position = Placement.QuickPanel(pointer, quickWindow.ScreenList(), size, room);
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
            w.TransparencyLevelHint = [WindowTransparencyLevel.Mica, WindowTransparencyLevel.None];
            w.Background = Brushes.Transparent;
            w.Content = new WinLibrary { DataContext = library };
            w.Opened += (_, _) => MicaIfAvailable(w);
        }
        PutWhereLeft(w);
        AppMenu.Attach(w, ShowLibrary, SettingsFromAnywhere);
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
            if (quitting) return;
            SaveLibraryPlace();
            e.Cancel = true;
            w.Hide();
            UpdateDock();
        };
        return mainWindow = w;
    }

    /// <summary>The library window opens where it was left, at the size it was left (or zoomed), if that spot is
    /// still on a display; otherwise centred at its usual size.</summary>
    static void PutWhereLeft(Window w)
    {
        if (host.Settings.LibraryWindow is not { } place) return;
        var screens = w.Screens.All.Select(s => new ScreenGeometry(s.Bounds, s.WorkingArea, s.Scaling, s.IsPrimary)).ToList();
        var at = new PixelPoint(place.X, place.Y);
        double scale = Placement.Pick(screens, at).Scaling;
        var size = new PixelSize((int)(Math.Max(place.Width, w.MinWidth) * scale), (int)(Math.Max(place.Height, w.MinHeight) * scale));
        if (Placement.Restore(at, size, screens, out var fitted) is not { } spot) return;
        w.WindowStartupLocation = WindowStartupLocation.Manual;
        w.Position = spot;
        w.Width = fitted.Width / scale;
        w.Height = fitted.Height / scale;
        if (place.Zoomed) w.Opened += (_, _) => w.WindowState = WindowState.Maximized;
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

    /// <summary>Windows 11's Mica shows through where the design has its Mica color; elsewhere the color stands in.</summary>
    static void MicaIfAvailable(Window w)
    {
        if (w.ActualTransparencyLevel == WindowTransparencyLevel.Mica) w.Resources["Mica"] = Brushes.Transparent;
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

    public static void ShowSetup()
    {
        if (setupWindow is { IsVisible: true })
        {
            setupWindow.Activate();
            return;
        }
        setup = Setup.Make(host);
        var view = Skin.Current == SkinKind.Mac ? (Control)new MacSetup { DataContext = setup, DrawChrome = false } : new WinSetup { DataContext = setup, DrawChrome = false };
        var w = new Window
        {
            Title = setup.HeaderTitle, Width = view.Width, Height = view.Height, CanResize = false, WindowStartupLocation = WindowStartupLocation.CenterScreen, Content = view,
            ExtendClientAreaToDecorationsHint = true, ExtendClientAreaTitleBarHeightHint = Skin.Current == SkinKind.Mac ? WindowHeader.MacHeight : 32,
        };
        Look.Apply(w);
        AppMenu.Attach(w, ShowLibrary, SettingsFromAnywhere);
        if (Skin.Current == SkinKind.Mac) MacTitleBar.Attach(w);
        if (Skin.Current == SkinKind.Win)
        {
            w.TransparencyLevelHint = [WindowTransparencyLevel.Mica, WindowTransparencyLevel.None];
            w.Background = Brushes.Transparent;
            w.Opened += (_, _) => MicaIfAvailable(w);
        }
        var model = setup;
        var mic = micCheck = new MicCheck();
        setup.OnFinish = () =>
        {
            Setup.Finish(model, host);
            w.Close();
            ShowLibrary();
            // The first run ends by saying where the S. lives (or that a full menu bar hides it).
            DispatcherTimer.RunOnce(SayWhereTheIconIs, TimeSpan.FromSeconds(1));
        };
        setup.OnEnter = step => EnterSetupStep(model, step);
        setup.OnCopy = text => _ = w.Clipboard?.SetTextAsync(text);
        // The library's setup and the laptop's have their own names: the window's follows the flow.
        setup.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SetupModel.HeaderTitle)) w.Title = model.HeaderTitle;
        };
        // The AI and Canvas steps are the design's bigger window (the view sizes itself per step): the window follows.
        view.PropertyChanged += (_, e) =>
        {
            if (e.Property == Layoutable.WidthProperty) w.Width = view.Width;
            else if (e.Property == Layoutable.HeightProperty) w.Height = view.Height;
        };
        // The AI engines step saves its choice before moving on; if it can't, it says why and stays.
        var leave = setup.LeaveAsync;
        setup.LeaveAsync = async step => step == SetupStep.Ai ? model.Ai is not { } ai || await ai.SaveAsync() : leave is null || await leave(step);
        w.Closed += (_, _) =>
        {
            setupWindow = null;
            if (setup == model) setup = null;
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
    }

    /// <summary>The AI and Canvas steps need the library (connected two steps before): their models are made as each
    /// opens, reading the library then.</summary>
    static void EnterSetupStep(SetupModel model, SetupStep step)
    {
        switch (step)
        {
            case SetupStep.Ai:
                model.Ai ??= new AiSetupModel(Ai());
                _ = model.Ai.Load();
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

    public static void ShowSettings() => ShowSettings(null);

    /// <summary>The app menu's Settings… (⌘,): see <see cref="SettingsFromAnywhere"/>.</summary>
    public static void SettingsFromMenu()
    {
        if (host is not null) SettingsFromAnywhere();
    }

    /// <summary>Settings from the dropdown's gear, the app menu or ⌘, (Ctrl+,) anywhere: the dropdown and the quick
    /// panel make way for it; before setup's done, setup comes forward instead.</summary>
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
        model.Canvas.Status.OnConnect = ShowCanvasConnect;
        model.Canvas.Status.OnShowMeHow = ShowCanvasConnect;
        if (section is not null) model.Section = section;
        var w = new Window
        {
            Title = "Study Stash settings", Width = 900, Height = Skin.Current == SkinKind.Mac ? 780 : 860, CanResize = false, WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Content = new SettingsView { DataContext = model, DrawChrome = false },
            ExtendClientAreaToDecorationsHint = true, ExtendClientAreaTitleBarHeightHint = Skin.Current == SkinKind.Mac ? WindowHeader.MacHeight : 32,
        };
        Look.Apply(w);
        AppMenu.Attach(w, ShowLibrary, SettingsFromAnywhere);
        model.Lib.Copy = text => _ = w.Clipboard?.SetTextAsync(text);
        model.Lib.ClassesChanged = LibraryClassesChanged;
        if (Skin.Current == SkinKind.Mac) MacTitleBar.Attach(w);
        if (Skin.Current == SkinKind.Win)
        {
            w.TransparencyLevelHint = [WindowTransparencyLevel.Mica, WindowTransparencyLevel.None];
            w.Background = Brushes.Transparent;
            w.Opened += (_, _) => MicaIfAvailable(w);
        }
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

    /// <summary>Toasts on screen right now, oldest first: how they stack, and what stops the same title firing twice
    /// in a row.</summary>
    static readonly List<(string Title, DateTime At, Floating Window)> toasts = [];

    /// <summary>A notification like the system's: top right on a Mac, above the tray on Windows. It goes by itself.
    /// Error codes in the words go to the log, not on screen.</summary>
    public static void Toast(string title, string text, string? action, Action? run, TimeSpan? stay = null)
    {
        if (quitting) return;
        if (ToastWords.HadCodes(title) || ToastWords.HadCodes(text)) Program.Log($"[toast] {title}: {text}");
        title = ToastWords.Plain(title) is { Length: > 0 } plain ? plain : "Study Stash";
        text = ToastWords.Plain(text);
        var now = DateTime.UtcNow;
        toasts.RemoveAll(t => !t.Window.IsVisible);
        if (toasts.Any(t => t.Title == title && now - t.At < TimeSpan.FromSeconds(10))) return;
        var view = new ToastView { Title = title, Text = text, ActionLabel = action };
        var w = new Floating { Content = view, Title = title, ShowActivated = false };
        view.Acted += () =>
        {
            w.Close();
            run?.Invoke();
        };
        view.Dismissed += w.Close;
        w.Closed += (_, _) => toasts.RemoveAll(t => t.Window == w);
        var (_, scale) = w.WorkArea();
        var size = w.Measured(scale);
        int room = (int)(Floating.ShadowRoom * scale);
        // Stacked below (a Mac, top right) or above (Windows, bottom right) whichever toasts are already showing.
        w.Position = Placement.ToastSpot(w.ScreenList(), toasts.Count, size, OperatingSystem.IsMacOS(), room);
        toasts.Add((title, now, w));
        w.Show();
        DispatcherTimer.RunOnce(() =>
        {
            if (w.IsVisible && !view.IsPointerOver) w.Close();
        }, stay ?? TimeSpan.FromSeconds(7));
    }

    // --- keeping it all up to date ------------------------------------------------------------------------------------

    static void Tick()
    {
        if (setup is not null && micCheck is not null) Setup.TickMic(setup, host, micCheck);
        var live = host.Recorder.Current;
        if (live is null) return;
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
        panel.IsRecording = recording;
        panel.IsPaused = recorder.IsPaused = live?.State == LectureState.Paused;
        string cls = recording ? live!.ClassName : RecordClass();
        panel.ClassName = recorder.ClassName = cls;
        int color = host.ColorOf(cls);
        panel.ClassDot = recorder.ClassDot = color >= 0 ? Skin.ClassDot(color) : Brushes.Gray;
        var now = host.ClassNow();
        var problem = Problems.For(host);
        currentProblem = problem;
        panel.ProblemTitle = problem?.Title;
        panel.ProblemDetail = problem?.Detail;
        panel.ProblemAction = problem is { HasAction: true } p ? p.ActionLabel : null;
        panel.CanRecord = recording || (host.ModelReady && host.MicAccess() is not (MicAccess.Denied or MicAccess.Restricted));
        panel.Hint = recording ? null
            : !panel.CanRecord && problem is not null ? problem.Title
            : chosenClass is { Length: > 0 } ? "Picked by you"
            : chosenClass is "" ? "The library will sort it"
            : now is not null ? $"From your timetable · {now.Time.Describe()}"
            : host.Timetable.Next(DateTime.Now) is { } next ? $"No class on now · next, {next.Class.Name} {next.Class.Time.Describe()}" : null;
        var (status, good) = host.Status();
        panel.Status = status;
        panel.StatusGood = good;
        KeepCanvasWatched();
        library.Status = LibraryStatus();
        library.StatusGood = host.Library == LibraryState.Connected;
        // The library just came back: the window, if it's open, gets its class reloaded so a note written while it
        // was gone shows up without reopening the window.
        if (lastLibraryState != LibraryState.Connected && host.Library == LibraryState.Connected) RequestLibraryReload();
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
