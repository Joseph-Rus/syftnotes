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
        Opened += (_, _) => JoinFullScreenSpaces();
        PositionChanged += (_, _) => SayClearAreaChanged();
    }

    /// <summary>Notifications step out of this window's way (the dropdown, the recorder, the quick panel): they never
    /// cover it.</summary>
    public bool KeepClear { get; init; }

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
        int left = Math.Clamp(p.X - area.X, 0, full), top = Math.Clamp(p.Y - area.Y, 0, full);
        int right = Math.Clamp(area.Right - p.Right, 0, full), bottom = Math.Clamp(area.Bottom - p.Bottom, 0, full);
        var padding = new Thickness(left / scale, top / scale, right / scale, bottom / scale);
        if (holder.Padding != padding) holder.Padding = padding;
        var position = new PixelPoint(p.X - left, p.Y - top);
        if (Position != position) Position = position;
    }

    /// <summary><see cref="MoveTo"/> on whichever display the panel lands on.</summary>
    public void Put(PixelPoint at)
    {
        var screen = Placement.PanelScreen(at, ScreenList(), ShadowRoom);
        MoveTo(at, screen.WorkingArea, screen.Scaling);
    }

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
            const nuint canJoinAllSpaces = 1 << 0, fullScreenAuxiliary = 1 << 8;
            objc_msgSend_setCollectionBehavior(nsWindow, sel_registerName("setCollectionBehavior:"), canJoinAllSpaces | fullScreenAuxiliary);
            objc_msgSend_setBool(nsWindow, sel_registerName("setHasShadow:"), 0);
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
        {
        }
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
}
