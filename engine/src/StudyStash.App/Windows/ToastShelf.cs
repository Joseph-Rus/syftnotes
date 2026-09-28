using Avalonia;
using Avalonia.Threading;
using StudyStash.App.Services;
using StudyStash.App.ViewModels;
using StudyStash.App.Views;

namespace StudyStash.App.Windows;

/// <summary>
/// The notifications on screen, each in a floating card of its own: stacked in the system's corner (top right under a
/// Mac's menu bar, bottom right above Windows' taskbar), newest nearest the edge, three at most; out of the way of the
/// dropdown, the recorder and the quick panel (or waiting, hidden, until there's room); never taking the keyboard; gone
/// by themselves when their time's up, unless the pointer is on one. What it asks of the rest of the app (the
/// displays, where the S. is, the panels to keep clear of, the clock) is handed in, so a test can give its own.
/// </summary>
public sealed class ToastShelf
{
    readonly bool mac;
    readonly Func<IReadOnlyList<ScreenGeometry>>? screens;
    readonly Func<IReadOnlyList<ScreenGeometry>, PixelPoint?> anchor;
    readonly Func<IReadOnlyList<PixelRect>> keepClear;
    readonly Func<DateTime> clock;
    readonly Dictionary<Notice, (Floating Window, ToastView View)> shown = [];
    DispatcherTimer? timer;
    bool arrangeQueued;
    /// <summary>The panels last stepped round, so a tick only moves the cards when one of them changed.</summary>
    string clearedFor = "";

    /// <param name="mac">A Mac's corner and stacking (top right, downward) or Windows' (bottom right, upward).</param>
    /// <param name="screens">Every display; null to ask the cards' own windows.</param>
    /// <param name="anchor">A point on the display the notifications belong on (a Mac: the S. in its menu bar); null
    /// for the main display.</param>
    /// <param name="keepClear">The panels on screen now that a notification mustn't cover.</param>
    /// <param name="clock">Now (UTC).</param>
    public ToastShelf(bool mac, Func<IReadOnlyList<ScreenGeometry>>? screens, Func<IReadOnlyList<ScreenGeometry>, PixelPoint?> anchor,
        Func<IReadOnlyList<PixelRect>> keepClear, Func<DateTime>? clock = null)
    {
        this.mac = mac;
        this.screens = screens;
        this.anchor = anchor;
        this.keepClear = keepClear;
        this.clock = clock ?? (() => DateTime.UtcNow);
    }

    public NoticeStack Stack { get; } = new();

    /// <summary>A notification's window, while it has one.</summary>
    public Floating? WindowOf(Notice notice) => shown.TryGetValue(notice, out var s) ? s.Window : null;

    /// <summary>A notification's card, while it has one.</summary>
    public ToastView? ViewOf(Notice notice) => shown.TryGetValue(notice, out var s) ? s.View : null;

    /// <summary>Its button (or the card itself) or its × was clicked, and it's gone: the host may need to hand the
    /// keyboard back.</summary>
    public event Action<Notice>? Clicked;

    /// <summary>The pointer came onto a notification (before any click on it).</summary>
    public event Action<Notice>? PointerCame;

    /// <summary>Shows <paramref name="notice"/> on top of the stack, unless the same one is showing already (that one
    /// starts its time again instead).</summary>
    public void Show(Notice notice)
    {
        var (added, folded) = Stack.Add(notice, clock());
        foreach (var old in folded) Close(old);
        if (added)
        {
            var view = notice.Model is CanvasToastModel canvasToast ? ToastView.For(canvasToast)
                : new ToastView { Title = notice.Title, Text = notice.Text, ActionLabel = notice.ActionLabel };
            var w = new Floating { Content = view, Title = notice.Title, ShowActivated = false, NoActivate = true };
            view.Acted += () =>
            {
                Close(notice);
                notice.Act?.Invoke();
                Clicked?.Invoke(notice);
            };
            view.Dismissed += () =>
            {
                Close(notice);
                Clicked?.Invoke(notice);
            };
            view.PointerEntered += (_, _) =>
            {
                notice.Hover(true, clock());
                PointerCame?.Invoke(notice);
            };
            view.PointerExited += (_, _) =>
            {
                notice.Hover(false, clock());
                // Whatever came while the pointer was on it takes its place now.
                Arrange();
            };
            // A display plugged in or out, or its scale changed: the cards go where they now belong.
            EventHandler displaysChanged = (_, _) => ArrangeSoon();
            w.Screens.Changed += displaysChanged;
            w.Closed += (_, _) =>
            {
                w.Screens.Changed -= displaysChanged;
                shown.Remove(notice);
                Stack.Remove(notice);
            };
            shown[notice] = (w, view);
        }
        Arrange();
        StartTimer();
    }

    void StartTimer()
    {
        if (timer is null)
        {
            timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(250) };
            timer.Tick += (_, _) => Tick();
        }
        timer.Start();
    }

    /// <summary>Takes <paramref name="notice"/> down (its time ran out, it was closed or acted on, or it folded away).</summary>
    public void Close(Notice notice)
    {
        Stack.Remove(notice);
        if (shown.Remove(notice, out var s)) s.Window.Close();
        Arrange();
    }

    /// <summary>Every notification goes (quitting).</summary>
    public void CloseAll()
    {
        timer?.Stop();
        foreach (var (w, _) in shown.Values.ToList()) w.Close();
        shown.Clear();
        foreach (var n in Stack.Showing.ToList()) Stack.Remove(n);
    }

    /// <summary>Four times a second while any show: takes down the ones whose time is up (or that are no longer so),
    /// and steps round a panel that moved without saying.</summary>
    public void Tick()
    {
        foreach (var n in Stack.Due(clock())) Close(n);
        if (shown.Count == 0)
        {
            timer?.Stop();
            return;
        }
        if (string.Join(";", keepClear()) != clearedFor) Arrange();
    }

    /// <summary>A panel to keep clear of moved, or a display changed: the cards go where they now belong, once the
    /// moment's other changes are in.</summary>
    public void ArrangeSoon()
    {
        if (arrangeQueued) return;
        arrangeQueued = true;
        Dispatcher.UIThread.Post(() =>
        {
            arrangeQueued = false;
            Arrange();
        });
    }

    /// <summary>Puts every card in its place (<see cref="Placement.ToastStack"/>). One with no room waits, hidden, and
    /// its time doesn't run. While the pointer is on one, none moves: a new one waits until the pointer leaves, so
    /// what you're about to click stays under the pointer.</summary>
    public void Arrange()
    {
        if (shown.Count == 0) return;
        var now = clock();
        var order = Stack.Showing.Where(shown.ContainsKey).ToList();
        if (order.Any(n => n.Hovered))
        {
            foreach (var n in order)
                if (!shown[n].Window.IsVisible) n.Wait(true, now);
            return;
        }
        var all = screens?.Invoke() ?? shown[order[0]].Window.ScreenList();
        var screen = Placement.Pick(all, anchor(all));
        int room = (int)(Floating.ShadowRoom * screen.Scaling);
        var cards = order.Select(n =>
        {
            var size = shown[n].Window.Measured(screen.Scaling);
            return new PixelSize(Math.Max(0, size.Width - 2 * room), Math.Max(0, size.Height - 2 * room));
        }).ToList();
        var clear = keepClear();
        clearedFor = string.Join(";", clear);
        var spots = Placement.ToastStack(screen, cards, clear, mac, room);
        for (int i = 0; i < order.Count; i++)
        {
            var w = shown[order[i]].Window;
            if (spots[i] is { } at)
            {
                if (w.Position != at) w.Position = at;
                order[i].Wait(false, now);
                if (!w.IsVisible) w.Show();
            }
            else
            {
                order[i].Wait(true, now);
                if (w.IsVisible) w.Hide();
            }
        }
    }
}
