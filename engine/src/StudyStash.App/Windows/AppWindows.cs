using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using StudyStash.App.Controls;
using StudyStash.App.Platform;
using StudyStash.App.Views;

namespace StudyStash.App.Windows;

/// <summary>What a window is for: one the student sizes and zooms (the library, Connect Canvas), or one whose view
/// sets its size (setup, Settings, Get ready).</summary>
public enum WindowKind
{
    Main,
    Dialog,
}

/// <summary>Where a window opens and how big: following its view's own size (and its changes), centred at a size, or
/// placed by whoever opens it.</summary>
public enum WindowSize
{
    Follow,
    Centred,
    Placed,
}

/// <summary>A window opened through <see cref="AppWindows"/>. <paramref name="Key"/> names it: one window per key.</summary>
public sealed record WindowSpec(string Key, string Title, Control View)
{
    public WindowKind Kind { get; init; } = WindowKind.Dialog;
    public WindowSize Size { get; init; } = WindowSize.Follow;
    /// <summary>The size <see cref="WindowSize.Centred"/> asks for (smaller where the display has less room).</summary>
    public Size Centred { get; init; }
    public Size MinSize { get; init; }
    /// <summary>Shown, it takes the keyboard (false: it comes up without taking it from what the student is doing).</summary>
    public bool Activate { get; init; } = true;
    /// <summary>Shown, the app comes in front of the one the student was in (a Mac).</summary>
    public bool AppForward { get; init; } = true;
    /// <summary>A Mac shows the app in the Dock (and ⌘Tab) while it's open.</summary>
    public bool InDock { get; init; } = true;
}

/// <summary>
/// Every one of the app's own windows (the library, setup, Settings, Connect Canvas, Get ready, the browser question)
/// is made here, the same way: the app's title bar drawn into the window, its text look (<see cref="Look"/>), its menu
/// and ⌘, (<see cref="AppMenu"/>), a Mac's traffic lights in the header (<see cref="MacTitleBar"/>) and Windows' Mica
/// and caption buttons (<see cref="WinChrome"/>), so none is missing one. It knows which are open, for the Dock.
/// The floating ones (the dropdown, the recorder, the quick panel, toasts) are <see cref="Floating"/>.
/// </summary>
public static class AppWindows
{
    sealed record Entry(Window Window, string Key, bool InDock, bool Activate, bool AppForward);

    static readonly List<Entry> windows = [];
    static Action library = () => { }, settings = () => { }, dockChanged = () => { };

    /// <summary>The library and Settings (the Window menu's items and ⌘,), and what follows a window opening or closing
    /// (the Dock).</summary>
    public static void Use(Action showLibrary, Action showSettings, Action onDockChange)
    {
        library = showLibrary;
        settings = showSettings;
        dockChanged = onDockChange;
    }

    /// <summary>A window that keeps the app in the Dock is showing.</summary>
    public static bool AnyInDock => windows.Any(o => o.InDock && o.Window.IsVisible);

    /// <summary>The window showing under <paramref name="key"/>, if any (one closing, or hidden, isn't).</summary>
    public static Window? Find(string key) => windows.FirstOrDefault(o => o.Key == key && o.Window.IsVisible)?.Window;

    /// <summary>The window for <paramref name="spec"/>, shown: <paramref name="wire"/> hooks it up first (its buttons,
    /// what closing it lets go of). One is already open under its key: that one comes forward instead.</summary>
    public static Window Open(WindowSpec spec, Action<Window>? wire = null)
    {
        if (Find(spec.Key) is { } already)
        {
            Raise(already);
            return already;
        }
        var w = Make(spec);
        wire?.Invoke(w);
        Show(w);
        return w;
    }

    /// <summary>The window for <paramref name="spec"/>, dressed and placed but not shown yet (the library's, which
    /// opens where it was left and hides rather than closes).</summary>
    public static Window Make(WindowSpec spec)
    {
        var w = new Window
        {
            Title = spec.Title, Content = spec.View,
            ExtendClientAreaToDecorationsHint = true, ExtendClientAreaTitleBarHeightHint = Skin.Current == SkinKind.Mac ? WindowHeader.MacHeight : 32,
        };
        if (spec.Kind == WindowKind.Dialog)
        {
            w.CanResize = false;
            w.CanMaximize = false;
        }
        if (spec.MinSize.Width > 0) w.MinWidth = spec.MinSize.Width;
        if (spec.MinSize.Height > 0) w.MinHeight = spec.MinSize.Height;
        if (spec.Size == WindowSize.Follow) Follow(w, spec.View, () => w.Content == spec.View);
        else if (spec.Size == WindowSize.Centred) Centre(w, spec.Centred);
        Dress(w, spec.Key, spec.InDock, spec.Activate, spec.AppForward);
        return w;
    }

    /// <summary>Dresses a window made elsewhere (<see cref="BrowserChoiceWindow"/>) as the app's, and counts it open
    /// under <paramref name="key"/> until it closes.</summary>
    public static void Dress(Window w, string key, bool inDock = true, bool activate = true, bool appForward = true)
    {
        Look.Apply(w);
        AppMenu.Attach(w, library, settings);
        if (Skin.Current == SkinKind.Mac) MacTitleBar.Attach(w);
        WinChrome.Apply(w);
        var entry = new Entry(w, key, inDock, activate, appForward);
        windows.Add(entry);
        w.Closed += (_, _) =>
        {
            windows.Remove(entry);
            dockChanged();
        };
    }

    /// <summary>Shows <paramref name="w"/> and, as it was made to, gives it the keyboard and the app the front.</summary>
    public static void Show(Window w)
    {
        w.Show();
        dockChanged();
        if (windows.FirstOrDefault(o => o.Window == w) is not { Activate: true } entry) return;
        w.Activate();
        if (entry.AppForward) Desktop.Activate();
    }

    /// <summary>Brings a window the student asked for to the front. On Windows, activating a minimized window leaves it
    /// in the taskbar: Open Syft Notes, opening the app again and a click on a lecture seemed to do nothing. So it
    /// comes back up first, as large as it was.</summary>
    public static void Raise(Window w)
    {
        if (w.WindowState == WindowState.Minimized) w.WindowState = WindowState.Normal;
        w.Activate();
    }

    // --- where a window opens, and how big ------------------------------------------------------------------------------

    /// <summary>The display a window opening now belongs on: on Windows the one the pointer is on, on a Mac the main one.</summary>
    public static ScreenGeometry ScreenFor(Window w) =>
        Placement.Pick([.. w.Screens.All.Select(s => new ScreenGeometry(s.Bounds, s.WorkingArea, s.Scaling, s.IsPrimary))], OperatingSystem.IsWindows() ? Floating.Pointer() : null);

    /// <summary>Opens <paramref name="w"/> in the middle of the pointer's display, at <paramref name="wanted"/> where
    /// that fits and smaller where it doesn't (a small screen at 125 or 150%), its title bar always on the screen.
    /// Returns the most room that display has for a window.</summary>
    public static Size Centre(Window w, Size wanted)
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
    public static void Follow(Window w, Control view, Func<bool>? showing = null, bool open = true)
    {
        var room = open ? Centre(w, new Size(view.Width, view.Height)) : Placement.Centred(ScreenFor(w), new Size(1e6, 1e6)).Size;
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

    /// <summary>The window takes the size of the view now showing in it (still on the display).</summary>
    public static void FitTo(Window w, Control view)
    {
        var screen = ScreenFor(w);
        double width = Math.Min(view.Width, view.MaxWidth), height = Math.Min(view.Height, view.MaxHeight);
        w.Width = width;
        w.Height = height;
        var size = new PixelSize((int)(width * screen.Scaling), (int)(height * screen.Scaling));
        var inside = Placement.KeepInside(screen.WorkingArea, size, w.Position);
        if (inside != w.Position) w.Position = inside;
    }
}
