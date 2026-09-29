using Avalonia;

namespace StudyStash.App.Windows;

/// <summary>A display, as much as placing a window on it needs: its full bounds, its usable area (without the menu
/// bar, the Dock, or the taskbar), and its scale. A stand-in for Avalonia's own <c>Screen</c> (which can't be built
/// outside Avalonia), so placement can be tested without opening a real display.</summary>
public readonly record struct ScreenGeometry(PixelRect Bounds, PixelRect WorkingArea, double Scaling, bool IsPrimary = false);

/// <summary>
/// Where the app's floating windows land: the dropdown under the menu bar icon, the tray flyout above the taskbar,
/// the quick panel, the recorder where it was left, a toast — each clamped to whichever display it's near. Pure: a
/// test hands it a list of screens instead of opening any, so multi-display and taskbar-side cases can be checked
/// without a real window.
/// </summary>
public static class Placement
{
    /// <summary>Room, in points, a floating window keeps from the edge it hugs.</summary>
    public const double Gap = 12;

    /// <summary>A plain screen to fall back on when there's truly nothing to ask (no displays at all).</summary>
    static readonly ScreenGeometry NoScreens = new(new PixelRect(0, 0, 1440, 900), new PixelRect(0, 0, 1440, 864), 1, true);

    /// <summary>The display <paramref name="near"/> is on; else the primary one; else the first; else a plain
    /// 1440×900 stand-in.</summary>
    public static ScreenGeometry Pick(IReadOnlyList<ScreenGeometry> screens, PixelPoint? near = null)
    {
        if (near is { } p)
            foreach (var s in screens)
                if (s.Bounds.Contains(p)) return s;
        foreach (var s in screens)
            if (s.IsPrimary) return s;
        return screens.Count > 0 ? screens[0] : NoScreens;
    }

    /// <summary>Keeps a window of <paramref name="size"/> inside <paramref name="area"/>, allowing <paramref
    /// name="room"/> of it (the window's own invisible shadow padding) to spill past the edge.</summary>
    static PixelPoint Clamp(PixelRect area, PixelSize size, int x, int y, int room) => new(
        Math.Clamp(x, area.X - room, Math.Max(area.X - room, area.Right - size.Width + room)),
        Math.Clamp(y, area.Y - room, Math.Max(area.Y - room, area.Bottom - size.Height + room)));

    /// <summary>Centred under the menu bar icon (Mac), just below the menu bar, on whichever display the icon is
    /// on. <paramref name="anchor"/> is the icon's position in pixels.</summary>
    public static PixelPoint MacDropdown(PixelPoint anchor, IReadOnlyList<ScreenGeometry> screens, PixelSize size, int room = 0)
    {
        var s = Pick(screens, anchor);
        return Clamp(s.WorkingArea, size, anchor.X - size.Width / 2, s.WorkingArea.Y + (int)(6 * s.Scaling) - room, room);
    }

    /// <summary>Above the taskbar, on the display under the cursor: 12 px off the taskbar's own edge (wherever
    /// that is — bottom on Windows 11, any side on Windows 10) and off the screen's facing edge.</summary>
    public static PixelPoint TrayFlyout(PixelPoint cursor, IReadOnlyList<ScreenGeometry> screens, PixelSize size, int room = 0)
    {
        var s = Pick(screens, cursor);
        int gap = (int)(Gap * s.Scaling);
        bool taskbarTop = s.WorkingArea.Y > s.Bounds.Y, taskbarLeft = s.WorkingArea.X > s.Bounds.X;
        int x = taskbarLeft ? s.WorkingArea.X + gap - room : s.WorkingArea.Right - size.Width - gap + room;
        int y = taskbarTop ? s.WorkingArea.Y + gap - room : s.WorkingArea.Bottom - size.Height - gap + room;
        return Clamp(s.WorkingArea, size, x, y, room);
    }

    /// <summary>Centred, a fifth down the display under the pointer.</summary>
    public static PixelPoint QuickPanel(PixelPoint? pointer, IReadOnlyList<ScreenGeometry> screens, PixelSize size, int room = 0)
    {
        var s = Pick(screens, pointer);
        int x = s.WorkingArea.X + (s.WorkingArea.Width - size.Width) / 2;
        int y = s.WorkingArea.Y + s.WorkingArea.Height / 5 - room;
        return Clamp(s.WorkingArea, size, x, y, room);
    }

    /// <summary>The recorder's spot: where it was left (its saved top-right corner), if that display still exists;
    /// otherwise the default corner (top right, under the menu bar, on a Mac; bottom right, above the taskbar, on
    /// Windows) of the primary display.</summary>
    public static PixelPoint KeepOnScreen(PixelPoint? savedTopRight, IReadOnlyList<ScreenGeometry> screens, PixelSize size, bool mac, int room = 0)
    {
        if (savedTopRight is { } tr)
            foreach (var s in screens)
                if (s.Bounds.Contains(tr)) return Clamp(s.WorkingArea, size, tr.X - size.Width, tr.Y, room);
        var d = Pick(screens);
        int gap = (int)(Gap * d.Scaling);
        return mac
            ? new PixelPoint(d.WorkingArea.Right - size.Width - gap + room, d.WorkingArea.Y + (int)(10 * d.Scaling) - room)
            : new PixelPoint(d.WorkingArea.Right - size.Width - gap + room, d.WorkingArea.Bottom - size.Height - gap + room);
    }

    /// <summary>The recorder as it's dragged: at <paramref name="at"/> (its top left with the whole shadow room, where
    /// the pointer has taken it), but with the panel kept inside the usable area of the display the pointer is on. A
    /// Mac pushes a window that reaches into its menu bar down below it, shadow room and all, so the pill used to stop
    /// a shadow's width short of the menu bar; on Windows it could slide under the taskbar.</summary>
    public static PixelPoint Dragged(PixelPoint at, PixelPoint pointer, IReadOnlyList<ScreenGeometry> screens, PixelSize size, int room) =>
        Clamp(Pick(screens, pointer).WorkingArea, size, at.X, at.Y, room);

    /// <summary>The display a floating window's panel lands on, given the window's top left with its whole shadow room
    /// round the panel: <paramref name="roomPoints"/> points, which is more pixels on a sharper display.</summary>
    public static ScreenGeometry PanelScreen(PixelPoint at, IReadOnlyList<ScreenGeometry> screens, double roomPoints)
    {
        foreach (var s in screens)
        {
            int room = (int)(roomPoints * s.Scaling);
            if (s.Bounds.Contains(new PixelPoint(at.X + room, at.Y + room))) return s;
        }
        return Pick(screens, at);
    }

    /// <summary>The library window's last spot, if its title bar would still land on a display (so it can be seen
    /// and dragged); otherwise null, and the window opens centred. Left hanging off an edge, it stays that way (as a
    /// Mac or Windows window would), but never with its title bar under the menu bar; too big for the display now, it
    /// shrinks to fit and comes fully onto it. <paramref name="size"/> is the saved size in pixels.</summary>
    public static PixelPoint? Restore(PixelPoint saved, PixelSize size, IReadOnlyList<ScreenGeometry> screens, out PixelSize fitted)
    {
        fitted = size;
        if (size.Width <= 0 || size.Height <= 0) return null;
        foreach (var s in screens)
        {
            // The title bar's top strip, as far as it needs to be grabbed.
            var bar = new PixelRect(saved.X, saved.Y, Math.Min(size.Width, (int)(200 * s.Scaling)), (int)(30 * s.Scaling));
            var shared = s.WorkingArea.Intersect(bar);
            if (shared.Width < 60 * s.Scaling || shared.Height < 10 * s.Scaling) continue;
            fitted = new PixelSize(Math.Min(size.Width, s.WorkingArea.Width), Math.Min(size.Height, s.WorkingArea.Height));
            return fitted == size ? new PixelPoint(saved.X, Math.Max(saved.Y, s.WorkingArea.Y)) : Clamp(s.WorkingArea, fitted, saved.X, saved.Y, 0);
        }
        return null;
    }

    /// <summary>A window opening in the middle of <paramref name="screen"/> (the library the first time, setup,
    /// Settings, Connect Canvas): at the size it asks for (<paramref name="wanted"/>, in points) where that fits the
    /// display's usable area with <paramref name="margin"/> to spare, smaller where it doesn't (a 1080p laptop at 150%
    /// has 672 points above its taskbar), never under <paramref name="min"/>; and centred on that area, its title bar
    /// never above the top of it.</summary>
    public static (PixelPoint Position, Size Size) Centred(ScreenGeometry screen, Size wanted, Size min = default, double margin = 16)
    {
        var area = screen.WorkingArea;
        double scale = screen.Scaling;
        // A size the window doesn't give (NaN) is as big as the display allows.
        static double Fit(double want, double room, double least) => Math.Max(double.IsNaN(least) ? 0 : least, double.IsNaN(want) ? room : Math.Min(want, room));
        var size = new Size(Fit(wanted.Width, area.Width / scale - 2 * margin, min.Width), Fit(wanted.Height, area.Height / scale - 2 * margin, min.Height));
        int w = (int)Math.Round(size.Width * scale), h = (int)Math.Round(size.Height * scale);
        return (new PixelPoint(area.X + Math.Max(0, (area.Width - w) / 2), area.Y + Math.Max(0, (area.Height - h) / 2)), size);
    }

    /// <summary>A window that has just grown (a bigger setup step) moves up or left as little as it takes to stay
    /// inside <paramref name="area"/>.</summary>
    public static PixelPoint KeepInside(PixelRect area, PixelSize size, PixelPoint at) => Clamp(area, size, at.X, at.Y, 0);

    // --- notifications ------------------------------------------------------------------------------------------------

    /// <summary>Room, in points, a notification keeps from the display's right edge, and from the taskbar (Windows).</summary>
    public const double ToastEdge = 12;

    /// <summary>Room, in points, between a Mac's menu bar and the notification under it (the recorder's own corner
    /// keeps the same).</summary>
    public const double ToastUnderMenuBar = 10;

    /// <summary>Room, in points, between two notifications stacked one on the other.</summary>
    public const double ToastSpacing = 8;

    /// <summary>A Mac's menu bar is at least this tall, in points: a notification never rises into it, even on a
    /// display whose menu bar hides itself (whose usable area then starts at the very top).</summary>
    const double MenuBarAtLeast = 24;

    /// <summary>
    /// Where each notification of a stack goes, newest first, as the system's own do: on a Mac, top right of
    /// <paramref name="screen"/>, just under the menu bar, older ones below it; on Windows, bottom right, just above
    /// the taskbar (whichever side that's on, the usable area's corner), older ones above it. <paramref name="cards"/>
    /// are the panels' own sizes, without their shadow room. A panel in <paramref name="keepClear"/> (the dropdown or
    /// the tray flyout, the recorder, the quick panel) is never covered: a notification in its way steps past it. A
    /// notification with no room left on the display waits (null) until there is, and so does every older one. The
    /// points are the windows' own: <paramref name="room"/> up and left of the panel they show.
    /// </summary>
    public static IReadOnlyList<PixelPoint?> ToastStack(ScreenGeometry screen, IReadOnlyList<PixelSize> cards, IReadOnlyList<PixelRect> keepClear,
        bool mac, int room = 0)
    {
        var area = screen.WorkingArea;
        int Px(double points) => (int)Math.Round(points * screen.Scaling);
        int edge = Px(ToastEdge), spacing = Px(ToastSpacing);
        int top = mac ? Math.Max(area.Y, screen.Bounds.Y + Px(MenuBarAtLeast)) + Px(ToastUnderMenuBar) : area.Y + edge;
        int bottom = area.Bottom - edge;
        var spots = new PixelPoint?[cards.Count];
        // A Mac's stack grows down from the top; Windows' grows up from the bottom.
        int next = mac ? top : bottom;
        for (int i = 0; i < cards.Count; i++)
        {
            var size = cards[i];
            int x = Math.Max(area.X + edge, area.Right - edge - size.Width);
            var card = new PixelRect(x, mac ? next : next - size.Height, size.Width, size.Height);
            // Step past a panel in the way; the step may land on another, so look again (never more often than there
            // are panels).
            for (int tries = 0; tries < keepClear.Count && InTheWay(card, keepClear, spacing) is { } panel; tries++)
                card = card.WithY(mac ? panel.Bottom + spacing : panel.Y - spacing - size.Height);
            if (card.Y < top || card.Bottom > bottom || InTheWay(card, keepClear, spacing) is not null) break;
            spots[i] = new PixelPoint(card.X - room, card.Y - room);
            next = mac ? card.Bottom + spacing : card.Y - spacing;
        }
        return spots;
    }

    /// <summary>The first of <paramref name="panels"/> that <paramref name="card"/> would touch (closer than
    /// <paramref name="spacing"/>), if any.</summary>
    static PixelRect? InTheWay(PixelRect card, IReadOnlyList<PixelRect> panels, int spacing)
    {
        var near = new PixelRect(card.X - spacing, card.Y - spacing, card.Width + 2 * spacing, card.Height + 2 * spacing);
        foreach (var p in panels)
            if (p.Width > 0 && p.Height > 0 && near.Intersects(p)) return p;
        return null;
    }

    /// <summary>A point in AppKit's screen space (points up from the bottom of the main display, the one with the menu
    /// bar in Displays settings) in Avalonia's (down from its top): <paramref name="mainHeight"/> is the main display's
    /// height in points.</summary>
    public static PixelPoint FromAppKit(double x, double y, double mainHeight) =>
        new((int)Math.Round(x), (int)Math.Round(mainHeight - y));

    /// <summary>Where a Mac's menu bar should put the S. so it shows, as its "preferred position" (points in from the
    /// screen's right edge): just right of the camera notch when there is one (the stretch of menu bar right of it,
    /// less room for the icon), else halfway along.</summary>
    public static double MenuBarPosition(double screenWidth, double rightOfNotch) =>
        Math.Max(40, (rightOfNotch > 0 ? rightOfNotch : screenWidth / 2) - 64);
}
