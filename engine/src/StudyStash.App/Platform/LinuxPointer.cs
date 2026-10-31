using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Avalonia;

namespace StudyStash.App.Platform;

/// <summary>
/// Linux: where the pointer is, from the X server (XQueryPointer), so the panel opens by the tray icon that was
/// clicked as it does on Windows. Under Wayland the app runs through XWayland, which knows the pointer while it's over
/// one of its windows (the tray's menu) and otherwise says where it last saw it; with no X server at all it's null and
/// the panel opens in the top right, where most Linux desktops keep their tray.
/// </summary>
[SupportedOSPlatform("linux")]
static class LinuxPointer
{
    const string X11 = "libX11.so.6";

    [DllImport(X11)]
    static extern IntPtr XOpenDisplay(IntPtr name);

    [DllImport(X11)]
    static extern IntPtr XDefaultRootWindow(IntPtr display);

    [DllImport(X11)]
    static extern int XQueryPointer(IntPtr display, IntPtr window, out IntPtr root, out IntPtr child, out int rootX, out int rootY,
        out int winX, out int winY, out uint mask);

    static IntPtr display;
    static bool tried;

    /// <summary>The pointer in screen pixels, or null when there's no X server to ask.</summary>
    public static PixelPoint? Where()
    {
        if (!tried)
        {
            tried = true;
            if (Environment.GetEnvironmentVariable("DISPLAY") is { Length: > 0 }) display = XOpenDisplay(IntPtr.Zero);
        }
        if (display == IntPtr.Zero) return null;
        return XQueryPointer(display, XDefaultRootWindow(display), out _, out _, out int x, out int y, out _, out _, out _) != 0
            ? new PixelPoint(x, y) : null;
    }
}
