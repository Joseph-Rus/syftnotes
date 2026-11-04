using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Threading;
using DialogHostAvalonia;
using StudyStash.App.Platform;
using StudyStash.App.ViewModels.Kit;
using StudyStash.App.Views;
using StudyStash.App.Windows;

namespace StudyStash.App.Controls;

/// <summary>
/// A window's dialog layer: DialogHost.Avalonia's host wrapped round the window's view (<c>AppWindows.Make</c> does it
/// for every window it makes), so <see cref="Confirm"/> can put a question over that window. It dims the view behind
/// and keeps the keyboard in the question. Styled by <c>DialogHost.confirm</c> in Styles.axaml.
/// </summary>
public sealed class ConfirmLayer
{
    static readonly List<ConfirmLayer> layers = [];
    static long activations;

    readonly Window window;
    long lastActive;

    public DialogHost Host { get; }

    ConfirmLayer(Window window, Control view)
    {
        this.window = window;
        Host = new DialogHost { Content = view, CloseOnClickAway = true, CloseOnClickAwayParameter = false, Classes = { "confirm" } };
    }

    /// <summary>The layer for <paramref name="window"/>, holding <paramref name="view"/>: set it as the window's content.</summary>
    public static DialogHost Wrap(Window window, Control view)
    {
        var layer = new ConfirmLayer(window, view);
        layers.Add(layer);
        window.Activated += (_, _) => layer.lastActive = ++activations;
        window.Closed += (_, _) => layers.Remove(layer);
        return layer.Host;
    }

    /// <summary>What a window shows: its view, whether or not a dialog layer is round it.</summary>
    public static Control? ViewOf(Window w) => w.Content is DialogHost h && layers.Any(l => l.Host == h) ? h.Content as Control : w.Content as Control;

    /// <summary>Changes the view a window shows (setup's two screens) and keeps its dialog layer.</summary>
    public static void SetView(Window w, Control view)
    {
        if (w.Content is DialogHost h && layers.Any(l => l.Host == h)) h.Content = view;
        else w.Content = view;
    }

    /// <summary>The layer of <paramref name="w"/> (one made by <see cref="Wrap"/>), if it has one.</summary>
    internal static ConfirmLayer? For(Window w) => layers.FirstOrDefault(l => l.window == w);

    /// <summary>The window to ask over: the one the student is in, else the one they were in last, else any that shows.</summary>
    public static ConfirmLayer? Pick() =>
        layers.Where(l => l.window.IsVisible).OrderByDescending(l => l.window.IsActive).ThenByDescending(l => l.lastActive).FirstOrDefault();

    /// <summary>Asks over this window: <see cref="ShowAsync"/> with the question as <see cref="ConfirmDialog"/> draws it.</summary>
    public async Task<bool> AskAsync(ConfirmAsk ask)
    {
        var dialog = new ConfirmDialog(ask);
        dialog.Answered += yes => Host.CurrentSession?.Close(yes);
        return await ShowAsync(dialog) is true;
    }

    /// <summary>Shows <paramref name="view"/> over this window, which comes to the front first if it isn't there (the library open
    /// behind another app: the question must not appear where the student isn't looking). It lasts until the view closes its
    /// <see cref="DialogHost"/> session, or a click outside does (null). The keyboard goes back to where it was afterwards. A
    /// window shows one at a time: asked while one is showing, this answers null at once.</summary>
    public async Task<object?> ShowAsync(Control view)
    {
        if (Host.IsOpen) return null;
        if (!window.IsActive)
        {
            AppWindows.Raise(window);
            // The app itself comes forward too (a Mac); not in a test, which never takes the student's screen.
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime) Desktop.Activate();
        }
        // A window that has only just been shown is ready for the question a moment later.
        if (!Host.IsLoaded) await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Loaded);
        var before = TopLevel.GetTopLevel(Host)?.FocusManager?.GetFocusedElement();
        var said = await DialogHost.Show(view, Host);
        if (before is Control c && c.IsEffectivelyVisible) Dispatcher.UIThread.Post(() => c.Focus());
        return said;
    }
}
