using Avalonia;
using StudyStash.App.Windows;

namespace StudyStash.App.Tests;

/// <summary>
/// Where notifications go (<see cref="Placement.ToastStack"/>), on made-up displays: a Mac's top right, just under the
/// menu bar (a notch's taller one, one that hides itself), with the Dock on any side; Windows' bottom right, just above
/// the taskbar (on any side), at 100 to 200%; several displays; a stack of three with a small gap, newest nearest the
/// edge; and never over the dropdown, the tray flyout, the recorder or the quick panel.
/// </summary>
public class ToastPlacementTests
{
    // A Mac's displays are in points (Avalonia's scale 1 there): a 14" MacBook Pro, notch and all, Dock at the bottom.
    static readonly ScreenGeometry MacBook = new(new PixelRect(0, 0, 1512, 982), new PixelRect(0, 33, 1512, 882), 1, IsPrimary: true);
    // Windows' are in pixels, with the display's own scale.
    static ScreenGeometry Pc(double scale, int taskbar = 48) =>
        new(new PixelRect(0, 0, (int)(1920 * scale), (int)(1080 * scale)), new PixelRect(0, 0, (int)(1920 * scale), (int)(1080 * scale) - (int)(taskbar * scale)), scale, IsPrimary: true);

    static readonly PixelSize Card = new(356, 64);
    static readonly PixelSize Tall = new(356, 112);

    static PixelRect At(PixelPoint? spot, PixelSize size, int room = 0) =>
        spot is { } p ? new PixelRect(p.X + room, p.Y + room, size.Width, size.Height) : throw new InvalidOperationException("it waits");

    static PixelRect One(ScreenGeometry s, bool mac, PixelSize? size = null, IReadOnlyList<PixelRect>? clear = null) =>
        At(Placement.ToastStack(s, [size ?? Card], clear ?? [], mac)[0], size ?? Card);

    static void Inside(PixelRect card, PixelRect area) =>
        Assert.True(card.X >= area.X && card.Y >= area.Y && card.Right <= area.Right && card.Bottom <= area.Bottom, $"{card} isn't inside {area}");

    [Fact]
    public void On_a_mac_one_sits_top_right_just_under_the_menu_bar()
    {
        var card = One(MacBook, mac: true);
        Assert.Equal(MacBook.WorkingArea.Right - 12, card.Right);
        Assert.Equal(MacBook.WorkingArea.Y + 10, card.Y);
        Inside(card, MacBook.WorkingArea);
    }

    [Fact]
    public void On_a_mac_whose_menu_bar_hides_itself_it_still_never_rises_into_the_menu_bar()
    {
        var hiding = MacBook with { WorkingArea = new PixelRect(0, 0, 1512, 915) };
        var card = One(hiding, mac: true);
        Assert.True(card.Y >= 24 + 10, $"top at {card.Y}");
    }

    [Theory]
    [InlineData("bottom", 0, 33, 1512, 882)]
    [InlineData("left", 64, 33, 1448, 949)]
    [InlineData("right", 0, 33, 1448, 949)]
    public void On_a_mac_the_dock_on_any_side_is_never_covered(string dock, int x, int y, int w, int h)
    {
        var s = MacBook with { WorkingArea = new PixelRect(x, y, w, h) };
        var card = One(s, mac: true);
        Inside(card, s.WorkingArea);
        Assert.Equal(s.WorkingArea.Right - 12, card.Right);
        Assert.Equal(s.WorkingArea.Y + 10, card.Y);
        Assert.NotNull(dock);
    }

    [Theory]
    [InlineData(1.0)]
    [InlineData(1.25)]
    [InlineData(1.5)]
    [InlineData(1.75)]
    [InlineData(2.0)]
    public void On_windows_one_sits_bottom_right_just_above_the_taskbar_at_any_scale(double scale)
    {
        var s = Pc(scale);
        var size = new PixelSize((int)(Card.Width * scale), (int)(Card.Height * scale));
        var card = One(s, mac: false, size);
        int edge = (int)Math.Round(12 * scale);
        Assert.Equal(s.WorkingArea.Right - edge, card.Right);
        Assert.Equal(s.WorkingArea.Bottom - edge, card.Bottom);
        Inside(card, s.WorkingArea);
    }

    [Fact]
    public void On_windows_a_taskbar_on_the_right_left_or_top_is_never_covered()
    {
        var full = new PixelRect(0, 0, 1920, 1080);
        foreach (var work in new[] { new PixelRect(0, 0, 1872, 1080), new PixelRect(48, 0, 1872, 1080), new PixelRect(0, 48, 1920, 1032) })
        {
            var s = new ScreenGeometry(full, work, 1, IsPrimary: true);
            var card = One(s, mac: false);
            Inside(card, work);
            Assert.Equal(work.Right - 12, card.Right);
            Assert.Equal(work.Bottom - 12, card.Bottom);
        }
    }

    [Fact]
    public void With_several_displays_windows_uses_the_main_one_and_a_mac_the_one_with_the_s()
    {
        // A 1.5x monitor to the left of the main display, its coordinates negative.
        var main = Pc(1);
        var left = new ScreenGeometry(new PixelRect(-2560, -200, 2560, 1440), new PixelRect(-2560, -200, 2560, 1392), 1.5);
        Assert.Equal(main, Placement.Pick([left, main], null));

        // A Mac with a display above the MacBook, its own menu bar (separate Spaces) holding the S.
        var above = new ScreenGeometry(new PixelRect(-300, -1440, 2560, 1440), new PixelRect(-300, -1415, 2560, 1415), 1);
        // AppKit measures up from the main display's bottom: the S. near the top of the display above.
        var icon = Placement.FromAppKit(2000, 982 + 1440 - 12, MacBook.Bounds.Height);
        Assert.Equal(new PixelPoint(2000, -1428), icon);
        var s = Placement.Pick([MacBook, above], icon);
        Assert.Equal(above, s);
        var card = One(s, mac: true);
        Assert.Equal(above.WorkingArea.Right - 12, card.Right);
        Assert.Equal(above.WorkingArea.Y + 10, card.Y);
    }

    [Fact]
    public void A_stack_of_three_has_the_newest_nearest_the_menu_bar_on_a_mac_with_a_small_gap()
    {
        var spots = Placement.ToastStack(MacBook, [Card, Tall, Card], [], mac: true);
        var newest = At(spots[0], Card);
        var middle = At(spots[1], Tall);
        var oldest = At(spots[2], Card);
        Assert.Equal(MacBook.WorkingArea.Y + 10, newest.Y);
        Assert.Equal(newest.Bottom + 8, middle.Y);
        Assert.Equal(middle.Bottom + 8, oldest.Y);
        Assert.All(new[] { newest, middle, oldest }, c => Assert.Equal(MacBook.WorkingArea.Right - 12, c.Right));
    }

    [Theory]
    [InlineData(1.0)]
    [InlineData(1.5)]
    [InlineData(2.0)]
    public void A_stack_of_three_has_the_newest_nearest_the_taskbar_on_windows(double scale)
    {
        var s = Pc(scale);
        var size = new PixelSize((int)(Card.Width * scale), (int)(Card.Height * scale));
        var spots = Placement.ToastStack(s, [size, size, size], [], mac: false);
        var cards = spots.Select(p => At(p, size)).ToList();
        int gap = (int)Math.Round(8 * scale);
        Assert.Equal(s.WorkingArea.Bottom - (int)Math.Round(12 * scale), cards[0].Bottom);
        Assert.Equal(cards[0].Y - gap, cards[1].Bottom);
        Assert.Equal(cards[1].Y - gap, cards[2].Bottom);
    }

    [Fact]
    public void The_window_lands_its_shadow_room_up_and_left_of_the_panel()
    {
        var bare = Placement.ToastStack(MacBook, [Card], [], mac: true)[0]!.Value;
        var roomy = Placement.ToastStack(MacBook, [Card], [], mac: true, room: 60)[0]!.Value;
        Assert.Equal(new PixelPoint(bare.X - 60, bare.Y - 60), roomy);
    }

    [Fact]
    public void An_open_dropdown_in_the_corner_is_stepped_past_not_covered()
    {
        // The S. near the right of the menu bar: its dropdown hangs down over the corner.
        var dropdown = new PixelRect(1100, 39, 380, 470);
        var spots = Placement.ToastStack(MacBook, [Card, Card], [dropdown], mac: true);
        var first = At(spots[0], Card);
        Assert.False(first.Intersects(dropdown));
        Assert.Equal(dropdown.Bottom + 8, first.Y);
        Assert.Equal(first.Bottom + 8, At(spots[1], Card).Y);
    }

    [Fact]
    public void An_open_tray_flyout_is_stepped_over_on_windows()
    {
        var s = Pc(1);
        var flyout = new PixelRect(1920 - 12 - 360, 1032 - 12 - 520, 360, 520);
        var card = At(Placement.ToastStack(s, [Card], [flyout], mac: false)[0], Card);
        Assert.False(card.Intersects(flyout));
        Assert.Equal(flyout.Y - 8, card.Bottom);
    }

    [Fact]
    public void The_recorder_pill_in_the_corner_keeps_its_place_and_notifications_go_under_it()
    {
        var pill = new PixelRect(1512 - 12 - 240, 33 + 10, 240, 44);
        var card = At(Placement.ToastStack(MacBook, [Card], [pill], mac: true)[0], Card);
        Assert.Equal(pill.Bottom + 8, card.Y);
    }

    [Fact]
    public void Panels_away_from_the_corner_change_nothing()
    {
        var quick = new PixelRect((1512 - 680) / 2, 200, 680, 420);
        Assert.Equal(One(MacBook, mac: true), One(MacBook, mac: true, clear: [quick]));
    }

    [Fact]
    public void Panels_one_after_another_are_all_stepped_past()
    {
        var pill = new PixelRect(1512 - 12 - 240, 43, 240, 44);
        var dropdown = new PixelRect(1100, 95, 380, 300);
        var card = At(Placement.ToastStack(MacBook, [Card], [dropdown, pill], mac: true)[0], Card);
        Assert.False(card.Intersects(pill) || card.Intersects(dropdown));
        Assert.Equal(dropdown.Bottom + 8, card.Y);
    }

    [Fact]
    public void With_no_room_left_a_notification_waits_and_so_do_older_ones()
    {
        // The recorder, opened up, fills the corner's whole height: nothing fits under it.
        var recorder = new PixelRect(1100, 43, 400, 860);
        var spots = Placement.ToastStack(MacBook, [Card, Card], [recorder], mac: true);
        Assert.All(spots, Assert.Null);

        // A short display: two fit, the third (oldest) waits.
        var small = new ScreenGeometry(new PixelRect(0, 0, 1280, 230), new PixelRect(0, 25, 1280, 200), 1, IsPrimary: true);
        var three = Placement.ToastStack(small, [Card, Card, Card], [], mac: true);
        Assert.NotNull(three[0]);
        Assert.NotNull(three[1]);
        Assert.Null(three[2]);
    }

    [Fact]
    public void Nothing_ever_lands_off_the_display_or_under_the_menu_bar_or_taskbar()
    {
        var screens = new[]
        {
            MacBook, MacBook with { WorkingArea = new PixelRect(0, 0, 1512, 982) }, MacBook with { WorkingArea = new PixelRect(80, 25, 1432, 957) },
            Pc(1), Pc(1.25), Pc(1.5), Pc(2), new ScreenGeometry(new PixelRect(0, 0, 1366, 768), new PixelRect(0, 0, 1366, 728), 1, true),
        };
        foreach (var s in screens)
            foreach (bool mac in new[] { true, false })
            {
                var spots = Placement.ToastStack(s, [Card, Tall, Card], [], mac);
                for (int i = 0; i < spots.Count; i++)
                {
                    if (spots[i] is null) continue;
                    var size = i == 1 ? Tall : Card;
                    var card = At(spots[i], size);
                    Inside(card, s.WorkingArea);
                    if (mac) Assert.True(card.Y >= s.Bounds.Y + 24, $"{card} is under the menu bar of {s}");
                }
            }
    }
}
