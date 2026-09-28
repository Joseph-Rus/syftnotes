using Avalonia;
using Avalonia.Threading;
using StudyStash.App.Platform;
using StudyStash.App.Services;
using StudyStash.App.ViewModels;
using StudyStash.App.Windows;
using StudyStash.Core;

namespace StudyStash.App;

/// <summary>The shell's notifications (<see cref="ToastShelf"/>): what the app says, and when.</summary>
public static partial class Shell
{
    static ToastShelf? shelf;

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

    /// <summary>A notification like the system's own (see <see cref="Notify"/>). Error codes in the words go to the log,
    /// not on screen.</summary>
    public static void Toast(string title, string text, string? action, Action? run, TimeSpan? stay = null)
    {
        if (ToastWords.HadCodes(title) || ToastWords.HadCodes(text)) Program.Log($"[toast] {title}: {text}");
        Notify(new Notice
        {
            Title = ToastWords.Plain(title) is { Length: > 0 } plain ? plain : "Study Stash", Text = ToastWords.Plain(text),
            ActionLabel = action, Act = run, Stay = stay,
        });
    }

    /// <summary>Shows <paramref name="notice"/> on top of the stack, unless the same one is showing already (that one
    /// starts its time again instead). Its words are the app's own, already plain.</summary>
    static void Notify(Notice notice)
    {
        if (quitting) return;
        Shelf().Show(notice);
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

    /// <summary>The problems showing at the last look, and when each was last said.</summary>
    static HashSet<ProblemKind> problemsNow = [];
    static readonly Dictionary<ProblemKind, DateTime> problemSaid = [];

    /// <summary>A problem that stops lectures being written down or filed (<see cref="NoticeWords.WorthNotifying"/>)
    /// is said once when it starts, with its fix as the button, and stays until it's over or closed. Not during setup
    /// (setup shows it), and not again within ten minutes if it comes and goes.</summary>
    static void SayNewProblems(IReadOnlyList<AppProblem> problems)
    {
        var before = problemsNow;
        problemsNow = [.. problems.Select(p => p.Kind)];
        if (!host.Settings.SetupDone || setupWindow?.IsVisible == true) return;
        var now = DateTime.UtcNow;
        foreach (var p in problems)
        {
            if (before.Contains(p.Kind) || !NoticeWords.WorthNotifying(p.Kind)) continue;
            if (problemSaid.TryGetValue(p.Kind, out var said) && now - said < TimeSpan.FromMinutes(10)) continue;
            problemSaid[p.Kind] = now;
            var kind = p.Kind;
            Notify(new Notice
            {
                Title = p.Title, Text = ToastWords.Plain(p.Detail), ActionLabel = p.HasAction ? p.ActionLabel : null, Act = () => FixProblem(kind),
                UntilClosed = true, StillTrue = () => problemsNow.Contains(kind),
            });
        }
    }

    /// <summary>Quitting: every notification goes.</summary>
    static void CloseAllToasts() => shelf?.CloseAll();
}
