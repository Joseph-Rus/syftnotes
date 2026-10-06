using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Chrome;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using StudyStash.App.Controls;
using StudyStash.App.Views;

namespace StudyStash.App.Tests;

/// <summary>Every full window has a real title bar across its top: the system's window buttons sit in it, it moves the
/// window, and nothing else in the window starts above it.</summary>
public class ChromeTests
{
    /// <summary>A view in a window of its own, laid out, with its title bar.</summary>
    static (Window Window, WindowHeader Header) Open(SkinKind skin, Func<Control> build)
    {
        ((App)Application.Current!).UseSkin(skin);
        var view = build();
        var window = new Window { Width = 1280, Height = 860, Content = view };
        window.Show();
        window.UpdateLayout();
        var header = view.GetLogicalDescendants().OfType<WindowHeader>().Single();
        return (window, header);
    }

    static IEnumerable<(string Name, SkinKind Skin, double Height, Func<Control> Build)> Windows(Services.AppHost host)
    {
        yield return ("Mac library", SkinKind.Mac, 52, () => new MacLibrary { DataContext = Demo.Library(), Width = 1280, Height = 800 });
        yield return ("Win library", SkinKind.Win, 48, () => new WinLibrary { DataContext = Demo.Library(), Width = 1280, Height = 800 });
        yield return ("Mac setup", SkinKind.Mac, 52, () => new MacSetup { DataContext = Demo.Setup(SkinKind.Mac) });
        yield return ("Win setup", SkinKind.Win, 32, () => new WinSetup { DataContext = Demo.Setup(SkinKind.Win) });
        yield return ("Mac settings", SkinKind.Mac, 52, () => new SettingsView { DataContext = Services.SettingsModel.Make(host) });
        yield return ("Win settings", SkinKind.Win, 32, () => new SettingsView { DataContext = Services.SettingsModel.Make(host) });
    }

    [AvaloniaFact]
    public void Each_window_has_a_title_bar_across_its_top_that_moves_it()
    {
        using var home = new TempHome();
        using var host = new Services.AppHost(home.Path);
        try
        {
            foreach (var (name, skin, height, build) in Windows(host))
            {
                var (window, header) = Open(skin, build);
                try
                {
                    var root = (Visual)window.Content!;
                    Assert.True(Math.Abs(header.Bounds.Height - height) < 0.5, $"{name}: title bar {header.Bounds.Height} tall, not {height}");
                    var top = header.TranslatePoint(new Point(0, 0), root)!.Value;
                    Assert.True(top.Y < 1.5, $"{name}: title bar starts {top.Y} down");
                    Assert.True(header.Bounds.Width >= root.Bounds.Width - 2, $"{name}: title bar {header.Bounds.Width} wide in a {root.Bounds.Width} window");
                    Assert.Equal(WindowDecorationsElementRole.TitleBar, WindowDecorationProperties.GetElementRole(header.DragArea));
                    // Its buttons and fields stay clickable: only the bar's own background moves the window.
                    foreach (var control in header.GetVisualDescendants().OfType<Control>().Where(c => c is Button or TextBox))
                        Assert.Equal(WindowDecorationsElementRole.None, WindowDecorationProperties.GetElementRole(control));

                    // Everything else in the window starts below the bar (the sidebar isn't under the lights).
                    var dock = header.GetVisualParent()!;
                    foreach (var sibling in dock.GetVisualChildren().OfType<Control>().Where(c => c != header && c.IsVisible))
                    {
                        double y = sibling.TranslatePoint(new Point(0, 0), root)!.Value.Y;
                        Assert.True(y >= top.Y + height - 0.5, $"{name}: {sibling.GetType().Name} starts at {y}, inside the title bar");
                    }
                }
                finally
                {
                    window.Close();
                }
            }
        }
        finally
        {
            ((App)Application.Current!).UseSkin(SkinKind.Mac);
        }
    }

    /// <summary>The sidebar's button and Back/Forward sit in the Mac window's top left corner: right after the lights,
    /// and in full screen (no lights at rest) in the corner itself. They once sat a second lights' width further in,
    /// half over the sidebar's edge.</summary>
    [AvaloniaFact]
    public void The_mac_sidebar_button_starts_after_the_lights_and_in_the_corner_in_full_screen()
    {
        try
        {
            var (window, _) = Open(SkinKind.Mac, () => new MacLibrary { DataContext = Demo.Library(), Width = 1280, Height = 800 });
            var view = (MacLibrary)window.Content!;
            var nav = view.FindControl<StackPanel>("Nav")!;
            double Left()
            {
                window.UpdateLayout();
                return nav.TranslatePoint(new Point(0, 0), view)!.Value.X;
            }
            Assert.Equal(WindowHeader.LightsRoom, Left(), 1);
            window.WindowState = WindowState.FullScreen;
            Assert.Equal(MacLibrary.FullScreenInset, Left(), 1);
            window.WindowState = WindowState.Normal;
            Assert.Equal(WindowHeader.LightsRoom, Left(), 1);
            window.Close();
        }
        finally
        {
            ((App)Application.Current!).UseSkin(SkinKind.Mac);
        }
    }

    [AvaloniaFact]
    public void The_mac_title_bar_keeps_the_lights_room_on_the_left_and_windows_keeps_the_caption_buttons_room_on_the_right()
    {
        try
        {
            var (mac, macHeader) = Open(SkinKind.Mac, () => new MacLibrary { DataContext = Demo.Library(), Width = 1280, Height = 800 });
            Assert.Equal(WindowHeader.LightsRoom, macHeader.ButtonRoom.Left);
            Assert.Equal(0, macHeader.ButtonRoom.Right);
            mac.Close();
            var (win, winHeader) = Open(SkinKind.Win, () => new WinLibrary { DataContext = Demo.Library(), Width = 1280, Height = 800 });
            Assert.Equal(0, winHeader.ButtonRoom.Left);
            Assert.Equal(3 * WindowHeader.CaptionWidth, winHeader.ButtonRoom.Right);
            win.Close();
        }
        finally
        {
            ((App)Application.Current!).UseSkin(SkinKind.Mac);
        }
    }

    [AvaloniaFact]
    public void Empty_sidebar_space_moves_the_library_window_too()
    {
        try
        {
            foreach (var skin in new[] { SkinKind.Mac, SkinKind.Win })
            {
                var (window, _) = Open(skin, () => skin == SkinKind.Mac
                    ? new MacLibrary { DataContext = Demo.Library(), Width = 1280, Height = 800 }
                    : new WinLibrary { DataContext = Demo.Library(), Width = 1280, Height = 800 });
                var sidebar = ((Control)window.Content!).FindControl<Control>("Sidebar")!;
                Assert.True(WindowHeader.GetMovesWindow(sidebar), $"{skin}: the sidebar doesn't move the window");
                window.Close();
            }
        }
        finally
        {
            ((App)Application.Current!).UseSkin(SkinKind.Mac);
        }
    }
}
