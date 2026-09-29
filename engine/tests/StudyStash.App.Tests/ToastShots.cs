using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using StudyStash.App.ViewModels;
using StudyStash.App.Views;
using StudyStash.App.Windows;

namespace StudyStash.App.Tests;

/// <summary>
/// Notifications where they land (<see cref="Placement.ToastStack"/>), drawn on a corner of a made-up display: a Mac's
/// under its menu bar, Windows' above its taskbar. One on its own; a stack of three, newest nearest the edge; and one
/// with the dropdown (or the tray flyout) open, stepping out of its way (and the rest waiting for room). Light and dark.
/// </summary>
public class ToastShots
{
    static readonly ThemeVariant[] Themes = [ThemeVariant.Light, ThemeVariant.Dark];

    /// <summary>
    /// A corner of a display, <see cref="Width"/> × <see cref="Height"/>: its menu bar (a Mac, 33 pt as on a notched
    /// MacBook) or taskbar (Windows, 48), maybe an open dropdown or flyout, and notifications (newest first) placed
    /// exactly as the app places them, from their real measured sizes.
    /// </summary>
    sealed class Desk : Panel
    {
        const double MenuBar = 33, Taskbar = 48;
        readonly bool mac;
        readonly List<Control> toasts;
        readonly Control? panel;
        readonly Border bar;

        public Desk(bool mac, double width, double height, IEnumerable<Control> newestFirst, Control? panel = null)
        {
            this.mac = mac;
            Width = width;
            Height = height;
            HorizontalAlignment = HorizontalAlignment.Left;
            VerticalAlignment = VerticalAlignment.Top;
            ClipToBounds = true;
            toasts = [.. newestFirst];
            this.panel = panel;
            bar = Bar(mac);
            Children.Add(bar);
            if (panel is not null) Children.Add(panel);
            foreach (var t in toasts) Children.Add(t);
        }

        /// <summary>The menu bar (the S., the clock) or the taskbar (the tray's S. and the clock), as a stand-in.</summary>
        static Border Bar(bool mac)
        {
            var ink = new SolidColorBrush(Color.FromArgb(0xE6, 0x80, 0x80, 0x80));
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 18, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 16, 0) };
            row.Children.Add(new TextBlock { Text = "S.", FontWeight = FontWeight.Bold, FontSize = 14 });
            row.Children.Add(new TextBlock { Text = mac ? "Mon 28 Sep  10:24" : "10:24\n28/09/2026", FontSize = 12, TextAlignment = TextAlignment.Right });
            foreach (var t in row.Children.OfType<TextBlock>()) t.Bind(TextBlock.ForegroundProperty, t.GetResourceObservable("Fg"));
            var b = new Border { Child = row, Background = mac ? new SolidColorBrush(Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF)) : null, Height = mac ? MenuBar : Taskbar };
            if (!mac) b.Bind(Border.BackgroundProperty, b.GetResourceObservable("PopupBg"));
            b.BorderBrush = ink;
            b.BorderThickness = mac ? new Thickness(0) : new Thickness(0, 0.5, 0, 0);
            return b;
        }

        ScreenGeometry Screen => new(new PixelRect(0, 0, (int)Width, (int)Height),
            mac ? new PixelRect(0, (int)MenuBar, (int)Width, (int)(Height - MenuBar)) : new PixelRect(0, 0, (int)Width, (int)(Height - Taskbar)), 1, IsPrimary: true);

        protected override Size MeasureOverride(Size availableSize)
        {
            foreach (var c in Children) c.Measure(Size.Infinity);
            return new Size(Width, Height);
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            bar.Arrange(mac ? new Rect(0, 0, Width, MenuBar) : new Rect(0, Height - Taskbar, Width, Taskbar));
            var clear = new List<PixelRect>();
            if (panel is not null)
            {
                var size = new PixelSize((int)Math.Ceiling(panel.DesiredSize.Width), (int)Math.Ceiling(panel.DesiredSize.Height));
                // The dropdown hangs under the S. (a little in from the right, as on a full menu bar); the flyout sits
                // above the taskbar's corner.
                var at = mac ? Placement.MacDropdown(new PixelPoint((int)Width - 150, 0), [Screen], size)
                    : Placement.TrayFlyout(new PixelPoint((int)Width - 40, (int)Height - 20), [Screen], size);
                panel.Arrange(new Rect(at.X, at.Y, size.Width, size.Height));
                clear.Add(new PixelRect(at, size));
            }
            var cards = toasts.Select(t => new PixelSize((int)Math.Ceiling(t.DesiredSize.Width), (int)Math.Ceiling(t.DesiredSize.Height))).ToList();
            var spots = Placement.ToastStack(Screen, cards, clear, mac);
            for (int i = 0; i < toasts.Count; i++)
            {
                // One with no room waits: it isn't drawn.
                var r = spots[i] is { } p ? new Rect(p.X, p.Y, cards[i].Width, cards[i].Height) : new Rect(-10000, 0, cards[i].Width, cards[i].Height);
                toasts[i].Arrange(r);
            }
            return finalSize;
        }
    }

    /// <summary>The newest first: a lecture filed, then the recording saved before it, then a Canvas notification.</summary>
    static List<Control> Three() =>
    [
        new ToastView { Title = "Filed in CS 101", Text = "Recursion and the call stack", ActionLabel = "Open note" },
        new ToastView { Title = "Recording saved", Text = "Study Stash is writing it down; the library files it and writes your notes." },
        ToastView.For(CanvasShots.ToastGallery()[2]),
    ];

    static List<Control> One() => [new ToastView { Title = "Filed in CS 101", Text = "Recursion and the call stack", ActionLabel = "Open note" }];

    [AvaloniaFact]
    public void Mac_notifications_in_their_corner()
    {
        foreach (var t in Themes)
        {
            Shot.Take("mac-12-notify-one", SkinKind.Mac, t, () => new Desk(mac: true, 900, 400, One()), size: new Size(1028, 528));
            Shot.Take("mac-12-notify-stack", SkinKind.Mac, t, () => new Desk(mac: true, 900, 480, Three()), size: new Size(1028, 608));
            Shot.Take("mac-12-notify-dropdown", SkinKind.Mac, t,
                () => new Desk(mac: true, 900, 700, Three(), new MacPanel { DataContext = Demo.Panel(recording: false) }), size: new Size(1028, 828));
        }
    }

    [AvaloniaFact]
    public void Windows_notifications_in_their_corner()
    {
        foreach (var t in Themes)
        {
            Shot.Take("win-12-notify-one", SkinKind.Win, t, () => new Desk(mac: false, 900, 400, One()), size: new Size(1028, 528));
            Shot.Take("win-12-notify-stack", SkinKind.Win, t, () => new Desk(mac: false, 900, 600, Three()), size: new Size(1028, 728));
            Shot.Take("win-12-notify-flyout", SkinKind.Win, t,
                () => new Desk(mac: false, 900, 900, Three(), new WinPanel { DataContext = Demo.Panel(recording: false) }), size: new Size(1028, 1028));
        }
    }
}
