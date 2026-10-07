using Avalonia;
using Avalonia.Threading;
using StudyStash.App.Platform;
using StudyStash.App.Services;
using StudyStash.App.ViewModels;
using StudyStash.App.Windows;
using StudyStash.Core;

namespace StudyStash.App;

/// <summary>The shell's notifications: what the app says, and when. They're the computer's own (a Mac's Notification
/// Center, Windows' notifications, through <see cref="SystemNotices"/>) wherever it will have them; Study Stash's own
/// cards (<see cref="ToastShelf"/>) where it won't (a build run from its folder, a copy opened from its disk image).</summary>
public static partial class Shell
{
    static ToastShelf? shelf;
    static SystemNotices? systemNotices;
    static bool systemNoticesTried;

    /// <summary>The computer's own notifications, made the first time something is said; null where there are none
    /// to be had. A self-test keeps to the app's own cards: it measures where those land, and must never have macOS
    /// ask anyone anything.</summary>
    static SystemNotices? SystemNotifications()
    {
        if (systemNoticesTried) return systemNotices;
        systemNoticesTried = true;
        if (Desktop.SystemChangesOff) return null;
        ISystemNotifications? center = OperatingSystem.IsMacOS() ? MacNotifications.Make() : OperatingSystem.IsWindows() ? WindowsNotifications() : null;
        if (center is null) return null;
        return systemNotices = new SystemNotices(center, a => Dispatcher.UIThread.Post(a), notice => Shelf().Show(notice), log: Program.Log);
    }

    /// <summary>Windows' notifications, for the installed app in its usual settings folder: a click on one comes back
    /// through a link that opens the installed program, which a build run from its folder, or a second profile,
    /// mustn't take over. The app's icon is written out for Windows to show beside its name, as it does for any app.</summary>
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    static WinNotifications? WindowsNotifications()
    {
        if (!File.Exists(Path.Combine(AppContext.BaseDirectory, "study-stash.ini")) || !Desktop.SameFolder(host.Home, Configs.DefaultHome)) return null;
        string? icon = null;
        try
        {
            icon = Path.Combine(host.Home, "notification-icon.png");
            using var from = Avalonia.Platform.AssetLoader.Open(new Uri("avares://StudyStash/Assets/icon.png"));
            using var to = File.Create(icon);
            from.CopyTo(to);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or FileNotFoundException)
        {
            icon = null;
        }
        WinNotifications.Log = Program.Log;
        var made = WinNotifications.Make(icon: icon, program: Desktop.Program);
        if (made is null) Program.Log($"[notifications] Windows won't have them here ({WinNotifications.LastError ?? "no reason given"}): Study Stash shows its own");
        return made;
    }

    /// <summary>The notifications' shelf, made the first time one is said: they go on the display whose menu bar has
    /// the S. (a Mac) or the main one (Windows), clear of the dropdown or tray flyout, the recorder and the quick panel.</summary>
    static ToastShelf Shelf()
    {
        if (shelf is not null) return shelf;
        shelf = new ToastShelf(OperatingSystem.IsMacOS(), screens: null, MenuBarIcon,
            () => [.. new[] { panelWindow, recorderWindow, quickWindow }.Select(w => w?.Panel()).OfType<PixelRect>()]);
        // A panel showing, hiding, moving or growing moves the cards out of its way.
        Floating.ClearAreaChanged += shelf.ArrangeSoon;
        shelf.PointerCame += _ => MacFocus.Remember();
        shelf.Clicked += _ => AfterToastClick();
        return shelf;
    }

    /// <summary>A notification (see <see cref="Notify"/>). Error codes in the words go to the log, not on screen.</summary>
    public static void Toast(string title, string text, string? action, Action? run, TimeSpan? stay = null)
    {
        if (ToastWords.HadCodes(title) || ToastWords.HadCodes(text)) Program.Log($"[toast] {title}: {text}");
        Notify(new Notice
        {
            Title = ToastWords.Plain(title) is { Length: > 0 } plain ? plain : "Study Stash", Text = ToastWords.Plain(text),
            ActionLabel = action, Act = run, Stay = stay,
        });
    }

    /// <summary>Says <paramref name="notice"/>: as one of the computer's own notifications where it has them, else as
    /// a card of the app's own, on top of the stack (the same one showing already starts its time again instead). Its
    /// words are the app's own, already plain.</summary>
    static void Notify(Notice notice)
    {
        if (quitting) return;
        if (SystemNotifications() is { } system) system.Show(notice);
        else Shelf().Show(notice);
    }

    /// <summary>The S. in a Mac's menu bar, as a point on screen: the notifications go on that display. Null on
    /// Windows (they go on the main display there, as Windows' own), or before the icon exists.</summary>
    static PixelPoint? MenuBarIcon(IReadOnlyList<ScreenGeometry> screens)
    {
        if (!OperatingSystem.IsMacOS() || MacStatusItem.ButtonFrame() is not { } f) return null;
        var main = screens.FirstOrDefault(s => s.IsPrimary);
        if (main.Bounds.Height <= 0) return null;
        return Placement.FromAppKit(f.X + f.Width / 2, f.Y + f.Height / 2, main.Bounds.Height);
    }

    /// <summary>After a click on a notification: on a Mac, if it opened none of Study Stash's windows, the app you were
    /// in comes back to the front (AppKit brought Study Stash forward for the click).</summary>
    static void AfterToastClick()
    {
        if (!OperatingSystem.IsMacOS()) return;
        Dispatcher.UIThread.Post(() =>
        {
            bool opened = mainWindow?.IsVisible == true || settingsWindow?.IsVisible == true || setupWindow?.IsVisible == true
                          || canvasConnectWindow?.IsVisible == true || panelWindow?.IsVisible == true || quickWindow?.IsVisible == true;
            if (!opened) MacFocus.GiveBack();
        }, DispatcherPriority.Background);
    }

    /// <summary>The lecture paused by itself (the microphone stopped, the disk filled, the system stopped letting Study
    /// Stash hear): said until it's resumed or stopped, or the notification is closed, with the button that deals with
    /// it (Resume, or the privacy settings).</summary>
    static void SayRecordingPaused(string why)
    {
        var (text, action, opensSettings) = NoticeWords.Paused(why, OperatingSystem.IsWindows());
        string? id = host.Recorder.Current?.Id;
        Notify(new Notice
        {
            Title = NoticeWords.PausedTitle, Text = text, ActionLabel = action,
            Act = opensSettings ? () => Dialogs.OpenUrl(host.MicSettingsUrl) : () =>
            {
                if (host.Recorder.Current is { State: LectureState.Paused }) TogglePause();
            },
            UntilClosed = true,
            StillTrue = () => host.Recorder.Current is { State: LectureState.Paused } l && l.Id == id,
        });
    }

    static readonly ProblemNotices problemNotices = new();

    /// <summary>What's wrong changed (<see cref="Refresh"/>): see <see cref="ProblemNotices"/>.</summary>
    static void SayNewProblems(IReadOnlyList<AppProblem> problems) => problemNotices.Seen(problems, DateTime.UtcNow);

    /// <summary>Four times a second: a problem that has lasted a moment is said, with its fix as the button, and stays
    /// until it's over or closed.</summary>
    static void SaySettledProblems()
    {
        systemNotices?.Tick();
        foreach (var p in problemNotices.Due(DateTime.UtcNow, quiet: !host.Settings.SetupDone || setupWindow?.IsVisible == true))
        {
            var kind = p.Kind;
            Notify(new Notice
            {
                Title = p.Title, Text = ToastWords.Plain(p.Detail), ActionLabel = p.HasAction ? p.ActionLabel : null, Act = () => FixProblem(kind),
                UntilClosed = true, StillTrue = () => problemNotices.Showing(kind),
            });
        }
    }

    /// <summary>The first start after Study Stash updated itself says so, once, with the release's notes a click away;
    /// a start after quitting for an update that didn't take (Windows' Setup.exe stopped short) says that instead.
    /// Every start remembers its version.</summary>
    static void SayIfUpdated()
    {
        string last = host.Settings.LastVersion, tried = host.Settings.UpdatingTo;
        if (last == Engine.Version && tried.Length == 0) return;
        host.Save(s =>
        {
            s.LastVersion = Engine.Version;
            s.UpdatingTo = "";
        });
        if (!host.Settings.SetupDone) return;
        if (NoticeWords.UpdateDidntTake(tried, Engine.Version) is { } failed)
            Notify(new Notice
            {
                Title = failed.Title, Text = failed.Text, ActionLabel = failed.Action, Act = () => Dialogs.OpenUrl(Updates.ReleasePage(tried)),
                Stay = NoticeTimes.Advice,
            });
        else if (NoticeWords.Updated(last, Engine.Version) is { } said)
            Notify(new Notice
            {
                Title = said.Title, Text = said.Text, ActionLabel = said.Action, Act = () => Dialogs.OpenUrl(Updates.ReleasePage(Engine.Version)),
                Stay = NoticeTimes.Advice,
            });
    }

    /// <summary>What the app's own updates say (<see cref="AppUpdates.Tell"/>, from the updater's thread): a new
    /// version to install, one this copy can't install itself, one on its way or waiting for the lecture, or one that
    /// didn't take. Quitting for one remembers which, so the next start can say whether it took.</summary>
    static void SayUpdate(UpdateNews news) => Dispatcher.UIThread.Post(() =>
    {
        string v = news.Version;
        string page = Updates.ReleasePage(v);
        switch (news.Kind)
        {
            case UpdateNewsKind.Ready:
            {
                var (title, text, action) = NoticeWords.UpdateReady(v);
                Notify(new Notice { Title = title, Text = text, ActionLabel = action, Act = () => _ = UpdateNowAsync(), Stay = NoticeTimes.Advice });
                break;
            }
            case UpdateNewsKind.CantInstall:
            {
                var (title, text, action) = NoticeWords.UpdateBlocked(v, news.Why ?? "");
                Notify(new Notice { Title = title, Text = text, ActionLabel = action, Act = () => Dialogs.OpenUrl(page), Stay = NoticeTimes.Advice });
                break;
            }
            case UpdateNewsKind.Installing:
            {
                var (title, text) = NoticeWords.Updating(v);
                Notify(new Notice { Title = title, Text = text, UntilClosed = true, StillTrue = () => AppUpdates.Current?.Installing == true });
                break;
            }
            case UpdateNewsKind.Waiting:
            {
                var (title, text) = NoticeWords.UpdateWaits(v);
                Notify(new Notice { Title = title, Text = text, Stay = NoticeTimes.Advice });
                break;
            }
            case UpdateNewsKind.Failed:
            {
                var (title, text, action) = NoticeWords.UpdateFailed(v, Engine.Version);
                Notify(new Notice { Title = title, Text = text, ActionLabel = action, Act = () => Dialogs.OpenUrl(page), Stay = NoticeTimes.Advice });
                break;
            }
            case UpdateNewsKind.Restarting:
                host.Save(s => s.UpdatingTo = v);
                break;
        }
    });

    /// <summary>Update now, from a notification or Settings: installs the newest release (or waits for the lecture),
    /// off the UI thread. What happened, in Settings' words.</summary>
    internal static async Task<string> UpdateNowAsync()
    {
        if (AppUpdates.Current is not { } updates) return NoticeWords.UpdateNowLine(AppUpdates.Off);
        return NoticeWords.UpdateNowLine(await Task.Run(updates.NowAsync));
    }

    /// <summary>Quitting: every card goes, and so does any of the system's that said something was still so.</summary>
    static void CloseAllToasts()
    {
        shelf?.CloseAll();
        systemNotices?.Quit();
    }
}
