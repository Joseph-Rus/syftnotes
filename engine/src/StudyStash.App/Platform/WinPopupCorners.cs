using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;

namespace StudyStash.App.Platform;

/// <summary>
/// On Windows 11 every popup's window (a menu, a flyout, a select's list, a tooltip) is rounded by the system to the
/// panel it shows, the way Windows' own menus are: a popup is a window exactly the panel's size, so if the window
/// can't be see-through (no graphics card, a remote session) its square corners would show round the rounded panel.
/// The system's own thin border is turned off, since the panel draws its own. Tooltips get the small rounding, the
/// rest the menu's. Anything missing (another OS, Windows 10, a popup drawn inside its window) leaves the popup as it was.
/// </summary>
public static class WinPopupCorners
{
    const int CornerPreference = 33; // DWMWA_WINDOW_CORNER_PREFERENCE
    const int BorderColor = 34; // DWMWA_BORDER_COLOR
    const int Round = 2; // DWMWCP_ROUND: 8 px, the Windows look's menu radius
    const int RoundSmall = 3; // DWMWCP_ROUNDSMALL: 4 px, its tooltip radius
    const uint NoColor = 0xFFFFFFFE; // DWMWA_COLOR_NONE

    static bool used;

    /// <summary>Turns it on for every popup the app opens from now on (once, at start).</summary>
    public static void Use()
    {
        if (used || !OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000)) return;
        used = true;
        Popup.IsOpenProperty.Changed.AddClassHandler<Popup>((popup, _) =>
        {
            if (popup.IsOpen) Give(popup);
        });
    }

    static void Give(Popup popup)
    {
        if (popup.IsUsingOverlayLayer || popup.Child is not { } child || TopLevel.GetTopLevel(child) is not { } top
            || top.TryGetPlatformHandle() is not { HandleDescriptor: "HWND", Handle: var hwnd } || hwnd == IntPtr.Zero) return;
        try
        {
            int corners = child is ToolTip ? RoundSmall : Round;
            DwmSetWindowAttribute(hwnd, CornerPreference, ref corners, sizeof(int));
            uint none = NoColor;
            DwmSetWindowAttribute(hwnd, BorderColor, ref none, sizeof(uint));
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
        {
        }
    }

    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref uint value, int size);
}
