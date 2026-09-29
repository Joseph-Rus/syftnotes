using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using StudyStash.App.Services;
using StudyStash.App.ViewModels;
using StudyStash.App.Windows;

namespace StudyStash.App.Tests;

/// <summary>
/// The notifications as windows (<see cref="ToastShelf"/>), on a made-up display with a made-up clock: where each
/// card's window lands, how a stack moves as one comes and goes, stepping out of an open panel's way, going by itself
/// (not while the pointer is on it), showing once, three at most, and what a click does.
/// </summary>
public class ToastShelfTests
{
    static readonly ScreenGeometry MacBook = new(new PixelRect(0, 0, 1512, 982), new PixelRect(0, 33, 1512, 882), 1, IsPrimary: true);
    static readonly ScreenGeometry Pc = new(new PixelRect(0, 0, 1920, 1080), new PixelRect(0, 0, 1920, 1032), 1, IsPrimary: true);

    sealed class Rig : IDisposable
    {
        public DateTime Now = new(2026, 9, 28, 10, 0, 0, DateTimeKind.Utc);
        public readonly List<PixelRect> Clear = [];
        public readonly ToastShelf Shelf;

        public Rig(bool mac)
        {
            ((App)Application.Current!).UseSkin(mac ? SkinKind.Mac : SkinKind.Win);
            var screen = mac ? MacBook : Pc;
            Shelf = new ToastShelf(mac, () => [screen], _ => null, () => Clear, () => Now);
        }

        public void Pass(double seconds)
        {
            Now = Now.AddSeconds(seconds);
            Shelf.Tick();
            Dispatcher.UIThread.RunJobs();
        }

        /// <summary>The card a notice shows, on screen (its window less the shadow room).</summary>
        public PixelRect Card(Notice n)
        {
            Dispatcher.UIThread.RunJobs();
            return Shelf.WindowOf(n)!.Panel()!.Value;
        }

        public bool Showing(Notice n) => Shelf.WindowOf(n)?.IsVisible == true;

        /// <summary>A point on the card (a little in from its top left, clear of its buttons), in its window.</summary>
        public Point On(Notice n, double x = 40, double y = 20)
        {
            var w = Shelf.WindowOf(n)!;
            var card = Card(n);
            return new Point(card.X - w.Position.X + x, card.Y - w.Position.Y + y);
        }

        public void Dispose() => Shelf.CloseAll();
    }

    static Notice Say(string title, string text = "Study Stash is writing it down.", string? action = null, Action? act = null) =>
        new() { Title = title, Text = text, ActionLabel = action, Act = act };

    [AvaloniaFact]
    public void A_mac_notification_lands_top_right_under_the_menu_bar_without_taking_the_keyboard()
    {
        using var rig = new Rig(mac: true);
        var n = Say("Recording saved");
        rig.Shelf.Show(n);
        var card = rig.Card(n);
        Assert.Equal(MacBook.WorkingArea.Right - 12, card.Right);
        Assert.Equal(MacBook.WorkingArea.Y + 10, card.Y);
        var w = rig.Shelf.WindowOf(n)!;
        Assert.True(w.IsVisible);
        // The window itself stays inside the usable area: a Mac would push one reaching up into the menu bar down,
        // card and all. Its clear room is trimmed there instead.
        Assert.True(w.Position.Y >= MacBook.WorkingArea.Y, $"window top {w.Position.Y}");
        Assert.True(w.Position.X + w.ClientSize.Width <= MacBook.WorkingArea.Right, $"window right {w.Position.X + w.ClientSize.Width}");
        Assert.False(w.ShowActivated);
        Assert.True(w.Topmost);
        Assert.False(w.ShowInTaskbar);
        Assert.True(w.Notification);
    }

    [AvaloniaFact]
    public void A_windows_notification_lands_bottom_right_above_the_taskbar()
    {
        using var rig = new Rig(mac: false);
        var n = Say("Recording saved");
        rig.Shelf.Show(n);
        var card = rig.Card(n);
        Assert.Equal(Pc.WorkingArea.Right - 12, card.Right);
        Assert.Equal(Pc.WorkingArea.Bottom - 12, card.Bottom);
        // Its clear room never reaches over the taskbar (where it would take the taskbar's clicks) or off the display.
        var w = rig.Shelf.WindowOf(n)!;
        Assert.True(w.Position.Y + w.ClientSize.Height <= Pc.WorkingArea.Bottom, $"window bottom {w.Position.Y + w.ClientSize.Height}");
        Assert.True(w.Position.X + w.ClientSize.Width <= Pc.WorkingArea.Right, $"window right {w.Position.X + w.ClientSize.Width}");
    }

    [AvaloniaFact]
    public void A_floating_panel_meant_just_under_the_menu_bar_lands_there_and_its_room_comes_back_lower_down()
    {
        ((App)Application.Current!).UseSkin(SkinKind.Mac);
        var w = new Floating { Content = new Border { Width = 320, Height = 240 } };
        try
        {
            int room = (int)Floating.ShadowRoom;
            // The dropdown's spot: 6 points under the menu bar, its window reaching 54 points above the usable area.
            var at = new PixelPoint(900, MacBook.WorkingArea.Y + 6 - room);
            w.MoveTo(at, MacBook.WorkingArea, 1);
            w.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(new PixelRect(900 + room, MacBook.WorkingArea.Y + 6, 320, 240), w.Panel());
            Assert.Equal(MacBook.WorkingArea.Y, w.Position.Y);

            // Somewhere with room all round: the whole shadow room again.
            w.MoveTo(new PixelPoint(300, 300), MacBook.WorkingArea, 1);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(new PixelRect(300 + room, 300 + room, 320, 240), w.Panel());
            Assert.Equal(new PixelPoint(300, 300), w.Position);
        }
        finally
        {
            w.Close();
        }
    }

    [AvaloniaFact]
    public void A_new_one_takes_the_corner_and_pushes_the_older_along_with_a_small_gap()
    {
        using var rig = new Rig(mac: true);
        var first = Say("Filed in CS 101", "Recursion and the call stack", "Open note");
        var second = Say("Recording saved");
        rig.Shelf.Show(first);
        rig.Shelf.Show(second);
        Assert.Equal(MacBook.WorkingArea.Y + 10, rig.Card(second).Y);
        Assert.Equal(rig.Card(second).Bottom + 8, rig.Card(first).Y);

        // The newer one goes: the older one moves up into the corner.
        rig.Shelf.Close(second);
        Assert.Equal(MacBook.WorkingArea.Y + 10, rig.Card(first).Y);
    }

    [AvaloniaFact]
    public void On_windows_the_stack_grows_up_from_the_taskbar()
    {
        using var rig = new Rig(mac: false);
        var first = Say("First");
        var second = Say("Second");
        rig.Shelf.Show(first);
        rig.Shelf.Show(second);
        Assert.Equal(Pc.WorkingArea.Bottom - 12, rig.Card(second).Bottom);
        Assert.Equal(rig.Card(second).Y - 8, rig.Card(first).Bottom);
    }

    [AvaloniaFact]
    public void The_same_one_twice_is_one_window_and_three_at_most_show()
    {
        using var rig = new Rig(mac: true);
        var a = Say("A");
        rig.Shelf.Show(a);
        rig.Shelf.Show(Say("A"));
        Assert.Single(rig.Shelf.Stack.Showing);

        var b = Say("B");
        var c = Say("C");
        var d = Say("D");
        rig.Shelf.Show(b);
        rig.Shelf.Show(c);
        rig.Shelf.Show(d);
        Assert.Equal([d, c, b], rig.Shelf.Stack.Showing);
        Assert.Null(rig.Shelf.WindowOf(a));
    }

    [AvaloniaFact]
    public void It_goes_by_itself_but_not_while_the_pointer_is_on_it()
    {
        using var rig = new Rig(mac: true);
        var n = Say("Recording saved");
        rig.Shelf.Show(n);
        rig.Pass(5);
        Assert.True(rig.Showing(n));

        var w = rig.Shelf.WindowOf(n)!;
        w.MouseMove(rig.On(n));
        Dispatcher.UIThread.RunJobs();
        Assert.True(n.Hovered);
        rig.Pass(60);
        Assert.True(rig.Showing(n));

        w.MouseMove(new Point(2, 2));
        Dispatcher.UIThread.RunJobs();
        Assert.False(n.Hovered);
        rig.Pass(2.5);
        Assert.True(rig.Showing(n));
        rig.Pass(1);
        Assert.Null(rig.Shelf.WindowOf(n));
    }

    [AvaloniaFact]
    public void While_the_pointer_is_on_one_a_new_one_waits_so_nothing_moves_under_it()
    {
        using var rig = new Rig(mac: true);
        var held = Say("Filed in CS 101", "Recursion and the call stack", "Open note");
        rig.Shelf.Show(held);
        var before = rig.Card(held);
        rig.Shelf.WindowOf(held)!.MouseMove(rig.On(held));
        Dispatcher.UIThread.RunJobs();

        var late = Say("Recording saved");
        rig.Shelf.Show(late);
        Assert.Equal(before, rig.Card(held));
        Assert.False(rig.Showing(late));
        Assert.True(late.Waiting);

        rig.Shelf.WindowOf(held)!.MouseMove(new Point(2, 2));
        Dispatcher.UIThread.RunJobs();
        Assert.True(rig.Showing(late));
        Assert.Equal(MacBook.WorkingArea.Y + 10, rig.Card(late).Y);
    }

    [AvaloniaFact]
    public void An_open_panel_in_the_corner_moves_them_out_of_its_way_and_they_come_back_when_it_closes()
    {
        using var rig = new Rig(mac: true);
        var n = Say("Recording saved");
        rig.Shelf.Show(n);
        var home = rig.Card(n);

        var dropdown = new PixelRect(1100, 39, 380, 470);
        rig.Clear.Add(dropdown);
        rig.Pass(0.25);
        Assert.False(rig.Card(n).Intersects(dropdown));
        Assert.Equal(dropdown.Bottom + 8, rig.Card(n).Y);

        rig.Clear.Clear();
        rig.Pass(0.25);
        Assert.Equal(home, rig.Card(n));
    }

    [AvaloniaFact]
    public void With_no_room_it_waits_hidden_and_its_time_doesnt_run()
    {
        using var rig = new Rig(mac: true);
        rig.Clear.Add(new PixelRect(1100, 40, 400, 870));
        var n = Say("Recording saved");
        rig.Shelf.Show(n);
        Assert.False(rig.Showing(n));
        rig.Pass(30);
        Assert.NotNull(rig.Shelf.WindowOf(n));

        rig.Clear.Clear();
        rig.Pass(0.25);
        Assert.True(rig.Showing(n));
        // All six of its seconds are still to come.
        rig.Pass(5.5);
        Assert.True(rig.Showing(n));
        rig.Pass(0.5);
        Assert.Null(rig.Shelf.WindowOf(n));
    }

    [AvaloniaFact]
    public void Its_button_or_a_click_on_it_does_the_thing_and_it_goes()
    {
        using var rig = new Rig(mac: true);
        int opened = 0, clicked = 0;
        rig.Shelf.Clicked += _ => clicked++;
        var n = Say("Filed in CS 101", "Recursion and the call stack", "Open note", () => opened++);
        rig.Shelf.Show(n);
        rig.Shelf.ViewOf(n)!.FindControl<Button>("ActionButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(1, opened);
        Assert.Equal(1, clicked);
        Assert.Null(rig.Shelf.WindowOf(n));

        // A click on the card itself, away from its buttons, opens it too.
        var again = Say("Filed in CS 101", "Big-O, by example", "Open note", () => opened++);
        rig.Shelf.Show(again);
        var w = rig.Shelf.WindowOf(again)!;
        var at = rig.On(again, 120, 30);
        w.MouseMove(at);
        w.MouseDown(at, MouseButton.Left);
        w.MouseUp(at, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(2, opened);
        Assert.Null(rig.Shelf.WindowOf(again));
    }

    [AvaloniaFact]
    public void The_cross_closes_it_without_doing_the_thing()
    {
        using var rig = new Rig(mac: false);
        int opened = 0, clicked = 0;
        rig.Shelf.Clicked += _ => clicked++;
        var n = Say("Filed in CS 101", "Recursion and the call stack", "Open note", () => opened++);
        rig.Shelf.Show(n);
        rig.Shelf.ViewOf(n)!.FindControl<Button>("WinClose")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(0, opened);
        Assert.Equal(1, clicked);
        Assert.Null(rig.Shelf.WindowOf(n));
    }

    [AvaloniaFact]
    public void One_that_needs_you_stays_until_it_is_no_longer_so()
    {
        using var rig = new Rig(mac: true);
        bool paused = true;
        var n = new Notice
        {
            Title = NoticeWords.PausedTitle, Text = "The microphone stopped. Plug it back in, then press Resume.", ActionLabel = "Resume",
            UntilClosed = true, StillTrue = () => paused,
        };
        rig.Shelf.Show(n);
        rig.Pass(600);
        Assert.True(rig.Showing(n));
        paused = false;
        rig.Pass(0.25);
        Assert.Null(rig.Shelf.WindowOf(n));
    }

    [AvaloniaFact]
    public void A_canvas_notification_opens_its_work_and_later_only_marks_it_seen()
    {
        using var rig = new Rig(mac: true);
        int opened = 0, dismissed = 0;
        var model = new CanvasToastModel(CanvasShots.ToastGallery()[0].Item)
        {
            When = "now", Expanded = true,
            OnOpen = _ => { opened++; return Task.CompletedTask; },
            OnDismiss = _ => { dismissed++; return Task.CompletedTask; },
        };
        var n = new Notice { Title = model.Title, Text = model.Text, ActionLabel = "Open", Model = model, Key = "canvas:1" };
        rig.Shelf.Show(n);
        rig.Shelf.ViewOf(n)!.FindControl<Button>("SecondButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal((0, 1), (opened, dismissed));
        Assert.Null(rig.Shelf.WindowOf(n));

        var again = new Notice { Title = model.Title, Text = model.Text, ActionLabel = "Open", Model = model, Key = "canvas:1" };
        rig.Shelf.Show(again);
        rig.Shelf.ViewOf(again)!.FindControl<Button>("ActionButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal((1, 1), (opened, dismissed));
        Assert.Null(rig.Shelf.WindowOf(again));
    }
}
