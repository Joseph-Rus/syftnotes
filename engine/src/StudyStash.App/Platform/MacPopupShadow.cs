using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;

namespace StudyStash.App.Platform;

/// <summary>
/// On a Mac every popup (a menu, a flyout, a submenu) gets the system's soft shadow round its rounded panel, the way
/// the Mac's own menus do. A popup is a window of its own, exactly as big as its panel, so a shadow the app drew round
/// the panel would only fill the window's corners (a faint square behind the rounded ones); the popups' themes draw
/// none, and Avalonia leaves popup windows without a system one (its shadow hint does nothing on macOS). The system's
/// shadow follows what the window shows, so it's shaped again once the panel is drawn. Anything missing (another OS,
/// a popup drawn inside its window, no window handle) leaves the popup as it was.
/// </summary>
public static class MacPopupShadow
{
    static bool used;

    /// <summary>Turns it on for every popup the app opens from now on (once, at start).</summary>
    public static void Use()
    {
        if (used || !OperatingSystem.IsMacOS()) return;
        used = true;
        // After Popup's own handler, which has shown the popup's window by then.
        Popup.IsOpenProperty.Changed.AddClassHandler<Popup>((popup, _) =>
        {
            if (popup.IsOpen) Give(popup);
        });
    }

    static void Give(Popup popup)
    {
        if (popup.IsUsingOverlayLayer || Window(popup) is not { } shown) return;
        try
        {
            if (!ObjC.IsKind(shown.Handle, ObjC.Class("NSWindow"))) return;
            ObjC.SendBool(shown.Handle, ObjC.Sel("setHasShadow:"), true);
            shown.Top.RequestAnimationFrame(_ => Dispatcher.UIThread.Post(() => Reshape(popup, shown.Top), DispatcherPriority.Background));
            DispatcherTimer.RunOnce(() => Reshape(popup, shown.Top), TimeSpan.FromMilliseconds(150));
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
        {
        }
    }

    /// <summary>The shadow again, from the panel as drawn now: only while the same popup window is still up (a popup
    /// opened again gets a new window).</summary>
    static void Reshape(Popup popup, TopLevel top)
    {
        if (!popup.IsOpen || Window(popup) is not { } shown || shown.Top != top) return;
        try
        {
            ObjC.Send(shown.Handle, ObjC.Sel("invalidateShadow"));
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
        {
        }
    }

    static (TopLevel Top, IntPtr Handle)? Window(Popup popup) =>
        popup.Child is { } child && TopLevel.GetTopLevel(child) is { } top
            && top.TryGetPlatformHandle() is { HandleDescriptor: "NSWindow", Handle: var handle } && handle != IntPtr.Zero
            ? (top, handle) : null;

    static class ObjC
    {
        const string Lib = "/usr/lib/libobjc.A.dylib";

        [DllImport(Lib)]
        static extern IntPtr objc_getClass(string name);

        [DllImport(Lib)]
        static extern IntPtr sel_registerName(string name);

        [DllImport(Lib, EntryPoint = "objc_msgSend")]
        static extern byte SendPtrReturnsByte(IntPtr receiver, IntPtr selector, IntPtr arg);

        [DllImport(Lib, EntryPoint = "objc_msgSend")]
        public static extern void Send(IntPtr receiver, IntPtr selector);

        [DllImport(Lib, EntryPoint = "objc_msgSend")]
        public static extern void SendBool(IntPtr receiver, IntPtr selector, [MarshalAs(UnmanagedType.I1)] bool arg);

        public static IntPtr Class(string name) => objc_getClass(name);

        public static IntPtr Sel(string name) => sel_registerName(name);

        public static bool IsKind(IntPtr obj, IntPtr cls) => cls != IntPtr.Zero && SendPtrReturnsByte(obj, Sel("isKindOfClass:"), cls) != 0;
    }
}
