using Avalonia;
using StudyStash.App.Windows;

namespace StudyStash.App;

/// <summary>The self-test's notifications, on the real display: three said at once land in the system's corner (top
/// right under a Mac's menu bar, bottom right above Windows' taskbar), newest nearest the edge, a small gap apart, as
/// <see cref="Placement.ToastStack"/> puts them; the dropdown (or the tray flyout) opened over them moves them out of
/// its way; and closed, they come back. Anything else is reported FAILED.</summary>
public static partial class SelfTest
{
    static async Task RunNotificationsAsync()
    {
        var shelf = Shell.Windows.Toasts;
        shelf.CloseAll();
        Shell.Toast("Filed in CS 101", "Recursion and the call stack", "Open note", () => { }, TimeSpan.FromMinutes(1));
        Shell.Toast("Recording saved", "Study Stash is writing it down; the library files it and writes your notes.", null, null, TimeSpan.FromMinutes(1));
        Shell.Toast("Syncing Canvas", "On your browser's next check, within a minute.", null, null, TimeSpan.FromMinutes(1));
        await Wait(1);
        CheckNotifications(shelf, "notifications", []);
        if (OperatingSystem.IsWindows() && Stack(shelf) is { } stack && shelf.Screen is { } display)
            AreaShot(Around(stack, display), "notifications");

        Shell.Windows.OpenPanelViaIcon();
        await Wait(1);
        if (Shell.Windows.Panel is Floating { IsVisible: true } dropdown && dropdown.Panel() is { } panel)
        {
            CheckDropdown(panel, shelf.Screen);
            CheckNotifications(shelf, "notifications, dropdown open", [panel]);
            if (OperatingSystem.IsWindows() && shelf.Screen is { } flyoutDisplay)
                AreaShot(Around(Stack(shelf) is { } s ? s.Union(panel) : panel, flyoutDisplay), "notifications-flyout");
            dropdown.Hide();
            await Wait(1);
            CheckNotifications(shelf, "notifications, dropdown closed again", []);
        }
        else
        {
            Say("notifications: the dropdown didn't open to check them against");
        }
        shelf.CloseAll();
    }

    /// <summary>The dropdown hangs just under the menu bar (6 points), not pushed further down by the Mac with its
    /// clear shadow room; the tray flyout sits 12 pixels above the taskbar.</summary>
    static void CheckDropdown(PixelRect panel, ScreenGeometry? screen)
    {
        if (screen is not { } s) return;
        var area = s.WorkingArea;
        bool mac = OperatingSystem.IsMacOS();
        int want = mac ? area.Y + (int)(6 * s.Scaling) : area.Bottom - (int)(Placement.Gap * s.Scaling);
        int got = mac ? panel.Y : panel.Bottom;
        Say(Math.Abs(got - want) <= 1 ? $"dropdown: {(mac ? "top" : "bottom")} at {got}, just {(mac ? "under the menu bar" : "above the taskbar")}"
            : $"FAILED dropdown: its {(mac ? "top" : "bottom")} is at {got}, not {want} ({panel} in {area})");
    }

    /// <summary>Every card on screen, as one rectangle.</summary>
    static PixelRect? Stack(ToastShelf shelf)
    {
        PixelRect? all = null;
        foreach (var (_, w) in shelf.Cards)
            if (w.Panel() is { } p) all = all is { } a ? a.Union(p) : p;
        return all;
    }

    /// <summary>A picture's stretch of the screen: the cards (and dropdown) with room round them, down to the display's
    /// bottom right corner, so the taskbar shows too.</summary>
    static PixelRect Around(PixelRect r, ScreenGeometry screen)
    {
        int margin = (int)(40 * screen.Scaling);
        int left = Math.Max(screen.Bounds.X, r.X - margin), top = Math.Max(screen.Bounds.Y, r.Y - margin);
        return new PixelRect(left, top, screen.Bounds.Right - left, screen.Bounds.Bottom - top);
    }

    /// <summary>Where each card is, said; and FAILED for any off its display, under the menu bar or taskbar, over another
    /// card or over <paramref name="keepClear"/>, or not where the placement puts it.</summary>
    static void CheckNotifications(ToastShelf shelf, string what, IReadOnlyList<PixelRect> keepClear)
    {
        var cards = shelf.Cards.ToList();
        if (shelf.Screen is not { } screen || cards.Count == 0)
        {
            Say($"FAILED {what}: none showing");
            return;
        }
        bool mac = OperatingSystem.IsMacOS();
        int room = (int)(Floating.ShadowRoom * screen.Scaling);
        var rects = cards.Select(c => c.Window.IsVisible ? c.Window.Panel() : null).ToList();
        Say($"{what}: on {screen.Bounds} (usable {screen.WorkingArea}, scale {screen.Scaling:0.##}): "
            + string.Join("; ", cards.Select((c, i) => $"{c.Notice.Title} {(rects[i] is { } r ? r.ToString() : "waiting")}")));
        var problems = new List<string>();
        var sizes = cards.Select(c =>
        {
            var m = c.Window.Measured(screen.Scaling);
            return new PixelSize(m.Width - 2 * room, m.Height - 2 * room);
        }).ToList();
        var expected = Placement.ToastStack(screen, sizes, keepClear, mac, room);
        for (int i = 0; i < cards.Count; i++)
        {
            if (rects[i] is not { } r)
            {
                if (expected[i] is not null) problems.Add($"{cards[i].Notice.Title} is hidden but has room");
                continue;
            }
            var area = screen.WorkingArea;
            if (r.X < area.X || r.Y < area.Y || r.Right > area.Right || r.Bottom > area.Bottom) problems.Add($"{cards[i].Notice.Title} {r} isn't inside {area}");
            if (keepClear.Any(k => k.Intersects(r))) problems.Add($"{cards[i].Notice.Title} {r} covers the dropdown");
            for (int j = 0; j < i; j++)
                if (rects[j] is { } other && other.Intersects(r)) problems.Add($"{cards[i].Notice.Title} overlaps {cards[j].Notice.Title}");
            // Within a pixel of where the placement puts it (a window position rounds).
            if (expected[i] is { } at && (Math.Abs(at.X + room - r.X) > 1 || Math.Abs(at.Y + room - r.Y) > 1))
                problems.Add($"{cards[i].Notice.Title} is at {r.X},{r.Y}, not {at.X + room},{at.Y + room}");
        }
        Say(problems.Count == 0 ? $"{what}: in the corner, newest nearest the {(mac ? "menu bar" : "taskbar")}, clear of each other"
            : $"FAILED {what}: {string.Join("; ", problems)}");
    }
}
