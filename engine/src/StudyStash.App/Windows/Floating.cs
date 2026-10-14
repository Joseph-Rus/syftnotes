using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Platform;
using StudyStash.App.Views;

namespace StudyStash.App.Windows;

/// <summary>
/// A window that is only its content: the dropdown, the recorder, the quick panel, a notification. The window is clear
/// and a little bigger than what it shows, so the content's own rounded corners and shadow (the design's) show round
/// it. It sits above other windows and out of the taskbar.
/// </summary>
public class Floating : Window
{
    /// <summary>Room for the content's shadow, all round: the Mac's Liquid Glass shadow reaches further (18+50px)
    /// than Windows' does.</summary>
    public static readonly double ShadowRoom = OperatingSystem.IsMacOS() ? 60 : 28;

    readonly Border holder = new() { Padding = new Thickness(ShadowRoom), ClipToBounds = false };

    public Floating()
    {
        WindowDecorations = WindowDecorations.None;
        Background = Brushes.Transparent;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        SizeToContent = SizeToContent.WidthAndHeight;
        CanResize = false;
        ShowInTaskbar = false;
        Topmost = true;
        base.Content = holder;
        Look.Apply(this);
        Deactivated += (_, _) =>
        {
            if (!CloseOnDeactivate || !IsVisible || HoldOpen) return;
            Hide();
            LastDeactivateHide = DateTime.UtcNow;
        };
        Opened += (_, _) =>
        {
            JoinFullScreenSpaces();
            if (ClickThrough) IgnoreClicks(true);
        };
        clicks.Tick += (_, _) => FollowPointer();
        Closing += (_, _) => Unpanel();
        PositionChanged += (_, _) => SayClearAreaChanged();
    }

    /// <summary>Notifications step out of this window's way (the dropdown, the recorder, the quick panel): they never
    /// cover it.</summary>
    public bool KeepClear { get; init; }

    /// <summary>A Mac: this window is a non-activating panel, the only kind macOS lets float over another app's
    /// full-screen Space while the app keeps its Dock icon (the recorder pill over full-screen slides or Zoom).</summary>
    public bool OverFullScreen { get; init; }

    /// <summary>A Mac: the pointer and clicks go straight through it to whatever's under it (the pill's card, which
    /// would otherwise take the pointer off the pill whenever its clear room lay over it).</summary>
    public bool ClickThrough { get; init; }

    /// <summary>A Mac: only the panel itself takes the pointer; its clear shadow room lets clicks through to what's
    /// under it (the recorder pill's room is 60 points all round, a box nothing behind it could be clicked in). macOS
    /// only lets a whole window ignore the mouse, so while it's shown the pointer is looked at every 30 ms.</summary>
    public bool ClicksOnPanelOnly { get; init; }

    readonly Avalonia.Threading.DispatcherTimer clicks = new() { Interval = TimeSpan.FromMilliseconds(30) };
    bool ignoring;

    void IgnoreClicks(bool ignore)
    {
        if (ignore == ignoring && !ClickThrough) return;
        try
        {
            IntPtr w = NativeWindow();
            if (w == IntPtr.Zero) return;
            objc_msgSend_setBool(w, sel_registerName("setIgnoresMouseEvents:"), (byte)(ignore ? 1 : 0));
            ignoring = ignore;
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
        {
        }
    }

    /// <summary>Takes the mouse while the pointer's on the panel, lets it through elsewhere; never in the middle of a
    /// press (a drag that runs ahead of the pill keeps going).</summary>
    void FollowPointer()
    {
        try
        {
            IntPtr w = NativeWindow();
            if (w == IntPtr.Zero || objc_msgSend_nuint(objc_getClass("NSEvent"), sel_registerName("pressedMouseButtons")) != 0) return;
            var at = MouseLocation(w, sel_registerName("mouseLocationOutsideOfEventStream")); // points, from its bottom left
            var pad = holder.Padding;
            var panel = new Rect(pad.Left, pad.Top, ClientSize.Width - pad.Left - pad.Right, ClientSize.Height - pad.Top - pad.Bottom);
            IgnoreClicks(!panel.Contains(new Point(at.X, ClientSize.Height - at.Y)));
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
        {
        }
    }

    /// <summary>A window that <see cref="KeepClear"/>s showed, hid, moved or changed size: notifications look again at
    /// where they can go.</summary>
    public static event Action? ClearAreaChanged;

    void SayClearAreaChanged()
    {
        if (KeepClear) ClearAreaChanged?.Invoke();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsVisibleProperty || change.Property == ClientSizeProperty) SayClearAreaChanged();
        if (change.Property == IsVisibleProperty && ClicksOnPanelOnly && OperatingSystem.IsMacOS())
        {
            if (IsVisible) clicks.Start();
            else clicks.Stop();
        }
    }

    /// <summary>The panel it shows, on screen in pixels (the window less its shadow room); null while it's hidden.</summary>
    public PixelRect? Panel()
    {
        if (!IsVisible) return null;
        double scale = DesktopScaling;
        var pad = holder.Padding;
        var size = PixelSize.FromSize(ClientSize, scale);
        int left = (int)Math.Round(pad.Left * scale), top = (int)Math.Round(pad.Top * scale);
        int width = size.Width - left - (int)Math.Round(pad.Right * scale), height = size.Height - top - (int)Math.Round(pad.Bottom * scale);
        return new PixelRect(Position.X + left, Position.Y + top, Math.Max(0, width), Math.Max(0, height));
    }

    /// <summary>
    /// Puts the window where <paramref name="at"/> says (its top left with the whole shadow room round the panel, as
    /// <see cref="Placement"/> works it out), except that it never reaches past <paramref name="area"/> (the display's
    /// usable area): the clear room on that side shrinks instead, so the panel lands exactly where it was meant to. A
    /// Mac pushes any window whose top would be above its menu bar down below it, panel and all (the dropdown, the
    /// recorder and a notification hung 60 points under the menu bar, not just under it); on Windows a window's clear
    /// edge over the taskbar, or over the next display, takes the clicks meant for them.
    /// </summary>
    public void MoveTo(PixelPoint at, PixelRect area, double scale)
    {
        int full = (int)(ShadowRoom * scale);
        var panel = PanelSize(scale);
        var p = new PixelRect(at.X + full, at.Y + full, panel.Width, panel.Height);
        var (left, right) = Room(p.X - area.X, area.Right - p.Right, full, SteadySize);
        var (top, bottom) = Room(p.Y - area.Y, area.Bottom - p.Bottom, full, SteadySize);
        var padding = new Thickness(left / scale, top / scale, right / scale, bottom / scale);
        if (holder.Padding != padding) holder.Padding = padding;
        var position = new PixelPoint(p.X - left, p.Y - top);
        if (Position != position) Position = position;
    }

    /// <summary>A window that's dragged about (the recorder) is one size wherever it is: by a display's edge, the clear
    /// room that side can't have goes to the other side, where a window that only sits where it's put gives it up.
    /// Giving it up changes the window's size, and a drag along an edge (or out of the corner the recorder lives in)
    /// did that at every step: a see-through window resized under the pointer over and over, which lagged behind it,
    /// and on a Mac left the system's blur up to 60 points off the glass it belongs under until the drag paused.</summary>
    public bool SteadySize { get; init; }

    /// <summary>The clear room each side of the panel along one line (left and right, or top and bottom), in pixels,
    /// given how far the panel is from each edge of the usable area: the <paramref name="full"/> room where there's
    /// that much, less where there isn't. <paramref name="steady"/>: what one side is short of, the other takes, so the
    /// two always come to twice the full room (on a display with too little space for that, as much as there is).</summary>
    public static (int Near, int Far) Room(int nearSpace, int farSpace, int full, bool steady)
    {
        int near = Math.Clamp(nearSpace, 0, full), far = Math.Clamp(farSpace, 0, full);
        if (!steady) return (near, far);
        int missing = 2 * full - near - far;
        int more = Math.Min(missing, Math.Max(0, nearSpace - near));
        return (near + more, far + Math.Min(missing - more, Math.Max(0, farSpace - far)));
    }

    /// <summary><see cref="MoveTo"/> on whichever display the panel lands on.</summary>
    public void Put(PixelPoint at)
    {
        var screen = Placement.PanelScreen(at, ScreenList(), ShadowRoom);
        MoveTo(at, screen.WorkingArea, screen.Scaling);
    }

    /// <summary>
    /// The content changed size (the recorder's pill opened or closed). Resizing a see-through window while it's on
    /// screen left a Mac showing its last frame stretched over the new size, shifted off the panel, and clicking fast
    /// stacked one resize on another before a frame landed. So the window steps off screen for the change:
    /// <paramref name="place"/> puts it for its new size, it's laid out at that size, and only then does it show again
    /// (a window coming on screen draws itself whole), placed once more now its size is final. While that's under way
    /// <see cref="Refitting"/> is true, so the caller can let clicks wait. The window always sizes to its content again
    /// first: Avalonia stops that for good whenever the system resizes the window itself (a display with another scale).
    /// </summary>
    public void Refit(Action place)
    {
        SizeToContent = SizeToContent.WidthAndHeight;
        bool shown = IsVisible;
        Refitting = true;
        if (shown) Hide();
        holder.InvalidateMeasure();
        place();
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            place();
            if (shown) Show();
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                if (IsVisible) place();
                Refitting = false;
            }, Avalonia.Threading.DispatcherPriority.Loaded);
        }, Avalonia.Threading.DispatcherPriority.Loaded);
    }

    /// <summary>A <see cref="Refit"/> is under way: the window isn't at its new size and place yet.</summary>
    public bool Refitting { get; private set; }

    /// <summary>The panel's own size in pixels, without the room round it.</summary>
    public PixelSize PanelSize(double scale)
    {
        if (holder.Child is not { } child) return default;
        child.Measure(Size.Infinity);
        var s = child.DesiredSize;
        return new PixelSize((int)Math.Ceiling(s.Width * scale), (int)Math.Ceiling(s.Height * scale));
    }

    /// <summary>A notification: a click on it never takes the keyboard from the app you're typing in (Windows: it isn't
    /// activated by a click, as the system's own notifications aren't; a Mac is looked after by
    /// <see cref="Platform.MacFocus"/>), and a click in its clear shadow room, where a neighbouring notification or
    /// the recorder may be, goes to that window under it instead.</summary>
    public bool Notification
    {
        get => notification;
        init
        {
            notification = value;
            if (value && OperatingSystem.IsWindows()) Win32Properties.AddWndProcHookCallback(this, NotificationHook);
        }
    }

    readonly bool notification;

    const uint WmMouseActivate = 0x0021, WmNcHitTest = 0x0084;
    const int MaNoActivate = 3, HtTransparent = -1;

    /// <summary>WM_MOUSEACTIVATE answered "don't activate" (the click still lands on the button under it); outside the
    /// panel, WM_NCHITTEST answers "transparent", which hands the click to the app's own window underneath.</summary>
    IntPtr NotificationHook(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmMouseActivate)
        {
            handled = true;
            return MaNoActivate;
        }
        if (msg == WmNcHitTest && Panel() is { } panel)
        {
            long l = lParam.ToInt64();
            var at = new PixelPoint((short)(l & 0xFFFF), (short)((l >> 16) & 0xFFFF));
            if (!panel.Contains(at))
            {
                handled = true;
                return HtTransparent;
            }
        }
        return IntPtr.Zero;
    }

    /// <summary>In front of the app's other floating windows, without taking the keyboard: a stack of notifications
    /// puts the lower ones in front, so a neighbour's faint shadow never sits over what you'd click.</summary>
    public void OrderFront()
    {
        if (!IsVisible) return;
        try
        {
            if (OperatingSystem.IsMacOS() && NativeWindow() is var w && w != IntPtr.Zero)
                objc_msgSend_id(w, sel_registerName("orderFront:"), IntPtr.Zero);
            else if (OperatingSystem.IsWindows() && TryGetPlatformHandle() is { HandleDescriptor: "HWND", Handle: var hwnd } && hwnd != IntPtr.Zero)
                SetWindowPos(hwnd, new IntPtr(-1), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010); // HWND_TOPMOST; NOSIZE | NOMOVE | NOACTIVATE
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
        {
        }
    }

    /// <summary>A Mac: what AppKit itself says of this window — shown, its size in points, and whether any of it is
    /// actually visible on the Space showing (not behind another app, not on another Space). Null anywhere else, or
    /// before it has an NSWindow. The self-test's check that the pill is really there, not just "shown".</summary>
    public (bool Visible, double Width, double Height, bool OnScreen)? Native() => NativeOf(this);

    /// <summary><see cref="Native"/> for any window: the library's too.</summary>
    public static (bool Visible, double Width, double Height, bool OnScreen)? NativeOf(Window window)
    {
        if (!OperatingSystem.IsMacOS()) return null;
        try
        {
            IntPtr w = window.TryGetPlatformHandle() switch
            {
                { HandleDescriptor: "NSWindow", Handle: var h } => h,
                { HandleDescriptor: "NSView", Handle: var v } when v != IntPtr.Zero => objc_msgSend(v, sel_registerName("window")),
                _ => IntPtr.Zero,
            };
            if (w == IntPtr.Zero) return null;
            bool visible = objc_msgSend_bool(w, sel_registerName("isVisible")) != 0;
            var frame = objc_msgSend_rect(w, sel_registerName("frame"));
            bool onScreen = (objc_msgSend_nuint(w, sel_registerName("occlusionState")) & 2) != 0; // NSWindowOcclusionStateVisible
            return (visible, frame.Width, frame.Height, onScreen);
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
        {
            return null;
        }
    }

    /// <summary>A Mac: the NSWindow (Avalonia 12 hands it itself; older backends handed its view). Zero anywhere
    /// else, and in tests.</summary>
    IntPtr NativeWindow() => TryGetPlatformHandle() switch
    {
        { HandleDescriptor: "NSWindow", Handle: var w } => w,
        { HandleDescriptor: "NSView", Handle: var v } when v != IntPtr.Zero => objc_msgSend(v, sel_registerName("window")),
        _ => IntPtr.Zero,
    };

    /// <summary>A Mac: let this window show over a full-screen app's own Space (a lecture on a full-screen Zoom
    /// call), instead of being stuck behind it, and turn off AppKit's own window shadow — for a clear window it's
    /// traced from everything drawn, the soft shadow room included, so it would outline a box; the content draws its
    /// own shadow. No-op off a Mac, and in tests (no real NSWindow to ask).</summary>
    void JoinFullScreenSpaces()
    {
        if (!OperatingSystem.IsMacOS()) return;
        try
        {
            IntPtr nsWindow = NativeWindow();
            if (nsWindow == IntPtr.Zero) return;
            if (OverFullScreen) MakePanel(nsWindow);
            const nuint canJoinAllSpaces = 1 << 0, fullScreenAuxiliary = 1 << 8;
            objc_msgSend_setCollectionBehavior(nsWindow, sel_registerName("setCollectionBehavior:"), canJoinAllSpaces | fullScreenAuxiliary);
            objc_msgSend_setBool(nsWindow, sel_registerName("setHasShadow:"), 0);
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
        {
        }
    }

    /// <summary>
    /// Turns this window into Avalonia's own panel class (AvnPanel: the same code and the same fields as its AvnWindow,
    /// only over NSPanel, which adds none; the self-test checks the two still match) and makes it non-activating: then
    /// macOS shows it over another app's full-screen Space, and a click on it doesn't bring the app forward. It stays
    /// put when the app isn't in front (a panel would hide then by default).
    /// </summary>
    void MakePanel(IntPtr nsWindow)
    {
        if (!PanelFits()) return;
        IntPtr panel = objc_getClass("AvnPanel");
        IntPtr was = object_getClass(nsWindow);
        if (was == panel) return;
        // What it was (AppKit's key-value observing subclass of AvnWindow, by now): it's put back before the window
        // goes, or AppKit's own clean-up, finding the class it observed through gone, aborts the app as it quits.
        swapped = (nsWindow, was);
        Panels.Add(this);
        object_setClass(nsWindow, panel);
        const nuint nonactivatingPanel = 1 << 7;
        nuint mask = objc_msgSend_nuint(nsWindow, sel_registerName("styleMask"));
        objc_msgSend_setCollectionBehavior(nsWindow, sel_registerName("setStyleMask:"), mask | nonactivatingPanel);
        objc_msgSend_setBool(nsWindow, sel_registerName("setHidesOnDeactivate:"), 0);
        objc_msgSend_setBool(nsWindow, sel_registerName("setFloatingPanel:"), 1);
    }

    /// <summary>The NSWindow made a panel, and the class it had: put back by <see cref="Unpanel"/>.</summary>
    (IntPtr Window, IntPtr Class)? swapped;

    static readonly List<Floating> Panels = [];

    /// <summary>Puts this window's own class back before it closes (see <see cref="MakePanel"/>).</summary>
    void Unpanel()
    {
        if (swapped is not { } s) return;
        swapped = null;
        Panels.Remove(this);
        try
        {
            object_setClass(s.Window, s.Class);
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
        {
        }
    }

    /// <summary>Every window made a panel goes back to its own class: the app is quitting.</summary>
    public static void UnpanelAll()
    {
        foreach (var w in Panels.ToList()) w.Unpanel();
    }

    /// <summary>AvnPanel and AvnWindow have the same size and the same fields at the same places, so one can stand in
    /// for the other: false (nothing changes) if a newer Avalonia ever makes them differ.</summary>
    public static bool PanelFits()
    {
        IntPtr window = objc_getClass("AvnWindow"), panel = objc_getClass("AvnPanel");
        if (window == IntPtr.Zero || panel == IntPtr.Zero || class_getInstanceSize(window) != class_getInstanceSize(panel)) return false;
        return Fields(window).SequenceEqual(Fields(panel));
    }

    static List<(string, long)> Fields(IntPtr cls)
    {
        IntPtr list = class_copyIvarList(cls, out uint count);
        var fields = new List<(string, long)>();
        try
        {
            for (int i = 0; i < count; i++)
            {
                IntPtr ivar = Marshal.ReadIntPtr(list, i * IntPtr.Size);
                fields.Add((Marshal.PtrToStringUTF8(ivar_getName(ivar)) ?? "", ivar_getOffset(ivar)));
            }
        }
        finally
        {
            if (list != IntPtr.Zero) free(list);
        }
        return fields;
    }

    /// <summary>When this window last hid itself because it lost focus (clicking elsewhere, or the icon that opened
    /// it): toggling it again within <see cref="ToggleDebounce"/> of that just closes it, rather than reopening it
    /// (the deactivate and the click that follows are two events for one gesture).</summary>
    public DateTime LastDeactivateHide { get; private set; } = DateTime.MinValue;

    public static readonly TimeSpan ToggleDebounce = TimeSpan.FromMilliseconds(300);

    /// <summary>What it shows (the window's real content is the padding round it).</summary>
    public new Control? Content
    {
        get => holder.Child;
        set => holder.Child = value;
    }

    /// <summary>Close when you click elsewhere (the dropdown, the quick panel).</summary>
    public bool CloseOnDeactivate { get; init; }

    /// <summary>A menu of its own is open (the dropdown's class picker): clicking in that menu mustn't close this
    /// window underneath it, or the pick would be lost with it.</summary>
    public bool HoldOpen { get; set; }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape && CloseOnDeactivate)
        {
            Hide();
            e.Handled = true;
        }
    }

    /// <summary>The screen's usable area in pixels, and its scale.</summary>
    public (PixelRect Area, double Scale) WorkArea(PixelPoint? near = null)
    {
        var screen = (near is { } p ? Screens.ScreenFromPoint(p) : null) ?? Screens.Primary ?? Screens.All.FirstOrDefault();
        return screen is null ? (new PixelRect(0, 0, 1440, 900), 1) : (screen.WorkingArea, screen.Scaling);
    }

    /// <summary>Every display, as <see cref="Placement"/> needs them.</summary>
    public IReadOnlyList<ScreenGeometry> ScreenList() =>
        Screens.All.Select(s => new ScreenGeometry(s.Bounds, s.WorkingArea, s.Scaling, s.IsPrimary)).ToList();

    /// <summary>The size it is with the whole shadow room round the panel, in pixels (measured before it's shown):
    /// what <see cref="Placement"/> works with. <see cref="MoveTo"/> may trim the room where it would reach past the
    /// display's usable area.</summary>
    public PixelSize Measured(double scale)
    {
        var panel = PanelSize(scale);
        int room = (int)(ShadowRoom * scale);
        return new PixelSize(panel.Width + 2 * room, panel.Height + 2 * room);
    }

    /// <summary>Where the pointer is on screen, in pixels: where the menu bar icon was clicked.</summary>
    public static PixelPoint? Pointer()
    {
        try
        {
            if (OperatingSystem.IsWindows() && GetCursorPos(out var p)) return new PixelPoint(p.X, p.Y);
            if (OperatingSystem.IsMacOS())
            {
                var at = MouseLocation(objc_getClass("NSEvent"), sel_registerName("mouseLocation"));
                return new PixelPoint((int)at.X, 0); // points from the bottom left; only across matters for the menu bar
            }
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
        {
        }
        return null;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct CGPoint
    {
        public double X, Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct POINT
    {
        public int X, Y;
    }

    [DllImport("user32.dll")]
    static extern bool GetCursorPos(out POINT p);

    [DllImport("user32.dll")]
    static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    static extern void objc_msgSend_id(IntPtr receiver, IntPtr selector, IntPtr argument);

    [DllImport("/usr/lib/libobjc.A.dylib")]
    static extern IntPtr objc_getClass(string name);

    [DllImport("/usr/lib/libobjc.A.dylib")]
    static extern IntPtr sel_registerName(string name);

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    static extern CGPoint MouseLocation(IntPtr receiver, IntPtr selector);

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    static extern IntPtr objc_msgSend(IntPtr receiver, IntPtr selector);

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    static extern void objc_msgSend_setCollectionBehavior(IntPtr receiver, IntPtr selector, nuint behavior);

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    static extern void objc_msgSend_setBool(IntPtr receiver, IntPtr selector, byte value);

    [DllImport("/usr/lib/libobjc.A.dylib")]
    static extern IntPtr object_setClass(IntPtr obj, IntPtr cls);

    [DllImport("/usr/lib/libobjc.A.dylib")]
    static extern IntPtr object_getClass(IntPtr obj);

    [DllImport("/usr/lib/libobjc.A.dylib")]
    static extern nuint class_getInstanceSize(IntPtr cls);

    [DllImport("/usr/lib/libobjc.A.dylib")]
    static extern IntPtr class_copyIvarList(IntPtr cls, out uint count);

    [DllImport("/usr/lib/libobjc.A.dylib")]
    static extern IntPtr ivar_getName(IntPtr ivar);

    [DllImport("/usr/lib/libobjc.A.dylib")]
    static extern long ivar_getOffset(IntPtr ivar);

    [DllImport("libc")]
    static extern void free(IntPtr p);

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    static extern byte objc_msgSend_bool(IntPtr receiver, IntPtr selector);

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    static extern nuint objc_msgSend_nuint(IntPtr receiver, IntPtr selector);

    // An NSRect comes back in registers on Apple silicon, and through objc_msgSend_stret on Intel.
    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    static extern NSRect objc_msgSend_rect_arm(IntPtr receiver, IntPtr selector);

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend_stret")]
    static extern void objc_msgSend_rect_stret(out NSRect result, IntPtr receiver, IntPtr selector);

    static NSRect objc_msgSend_rect(IntPtr receiver, IntPtr selector)
    {
        if (RuntimeInformation.ProcessArchitecture == Architecture.Arm64) return objc_msgSend_rect_arm(receiver, selector);
        objc_msgSend_rect_stret(out var r, receiver, selector);
        return r;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct NSRect
    {
        public double X, Y, Width, Height;
    }
}
