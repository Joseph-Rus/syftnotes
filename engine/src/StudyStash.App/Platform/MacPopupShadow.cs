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
/// shadow follows what the window shows, so it's shaped again once the panel is drawn. Each popup window is also put on
/// every desktop (Space), as Avalonia does for its own dialogs and the app for its floating windows: left to AppKit, a
/// popup can land on another desktop than the window it opened from, and the dropdown or menu seems not to open.
/// Only popups that close as their window loses focus (dropdowns, menus) and tooltips do, so none trails onto another
/// desktop; one that stays open keeps AppKit's choice and is only brought to the front. Anything missing
/// (another OS, a popup drawn inside its window, no window handle) leaves the popup as it was.
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
            // Only a popup that closes by itself when its window loses focus (or a tooltip) may join every desktop: one
            // that stays open (the Ask bar's recipe list) would otherwise float over the next desktop too.
            if (popup.IsLightDismissEnabled || popup.Child is ToolTip)
            {
                const nuint canJoinAllSpaces = 1 << 0, fullScreenAuxiliary = 1 << 8;
                var behavior = ObjC.GetNuint(shown.Handle, ObjC.Sel("collectionBehavior"));
                ObjC.SendNuint(shown.Handle, ObjC.Sel("setCollectionBehavior:"), behavior | canJoinAllSpaces | fullScreenAuxiliary);
            }
            // Already shown by now (on whichever desktop AppKit chose): in front again, on the one showing.
            ObjC.SendPtr(shown.Handle, ObjC.Sel("orderFront:"), IntPtr.Zero);
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

        [DllImport(Lib, EntryPoint = "objc_msgSend")]
        public static extern nuint GetNuint(IntPtr receiver, IntPtr selector);

        [DllImport(Lib, EntryPoint = "objc_msgSend")]
        public static extern void SendNuint(IntPtr receiver, IntPtr selector, nuint arg);

        [DllImport(Lib, EntryPoint = "objc_msgSend")]
        public static extern void SendPtr(IntPtr receiver, IntPtr selector, IntPtr arg);

        public static IntPtr Class(string name) => objc_getClass(name);

        public static IntPtr Sel(string name) => sel_registerName(name);

        public static bool IsKind(IntPtr obj, IntPtr cls) => cls != IntPtr.Zero && SendPtrReturnsByte(obj, Sel("isKindOfClass:"), cls) != 0;
    }
}
