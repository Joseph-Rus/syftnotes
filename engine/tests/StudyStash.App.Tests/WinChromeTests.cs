using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Chrome;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using StudyStash.App.Controls;
using StudyStash.App.Platform;
using StudyStash.App.Views;

namespace StudyStash.App.Tests;

/// <summary>
/// What a Study Stash window on Windows shows at its top. On Windows, Avalonia draws the caption buttons itself (its
/// "drawn decorations", laid over the window's content), and by default the window's title too — over ours, so the
/// app's name came out twice, overlapping ("StSdy Stash"). The headless platform draws no decorations, so these tests
/// lay Avalonia's over the view the way the real window does.
/// </summary>
public class WinChromeTests
{
    /// <summary>A panel that holds Avalonia's drawn decorations and lays their overlay over <paramref name="content"/>,
    /// as the real window's host does: a <paramref name="resizable"/> window's minimise, maximise and close, or a fixed
    /// one's minimise and close, in a title bar <paramref name="height"/> tall.</summary>
    internal sealed class DrawnHost(Control content, ControlTheme? theme, double height, bool resizable = true) : Panel
    {
        public WindowDrawnDecorations? Decorations { get; private set; }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            if (Decorations is not null) return;
            Children.Add(content);
            Resources["CaptionButtonHeight"] = height;
            var d = Decorations = new WindowDrawnDecorations { Theme = theme, Title = "Study Stash" };
            // What Windows asks for: the title bar only (Avalonia keeps these internal).
            var parts = typeof(WindowDrawnDecorations).GetProperty("EnabledParts", BindingFlags.NonPublic | BindingFlags.Instance)!;
            parts.SetValue(d, Enum.ToObject(parts.PropertyType, 4));
            typeof(WindowDrawnDecorations).GetProperty("TitleBarHeightOverride", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(d, height);
            // Minimise allowed; maximise (and, as on Windows, full screen) when the window can be resized.
            var pseudo = (IPseudoClasses)d.Classes;
            pseudo.Set(":normal", true);
            pseudo.Set(":has-minimize", true);
            pseudo.Set(":has-maximize", resizable);
            pseudo.Set(":has-fullscreen", resizable);
            LogicalChildren.Add(d);
            d.ApplyStyling();
            typeof(WindowDrawnDecorations).GetMethod("ApplyTemplate", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(d, null);
            Children.Add(d.Content!.Overlay!);
        }
    }

    static ViewModels.LibraryModel NoChrome(ViewModels.LibraryModel m)
    {
        m.DrawChrome = false;
        return m;
    }

    static Control Part(WindowDrawnDecorations d, string name) =>
        d.Content!.Overlay!.GetVisualDescendants().OfType<Control>().Single(c => c.Name == name);

    static (Window Window, WindowDrawnDecorations Decorations) Open(ControlTheme? theme)
    {
        ((App)Application.Current!).UseSkin(SkinKind.Win);
        var host = new DrawnHost(new WinLibrary { DataContext = Demo.Library(), Width = 1280, Height = 800 }, theme, 48);
        var window = new Window { Width = 1280, Height = 800, Content = host };
        window.Show();
        window.UpdateLayout();
        return (window, host.Decorations!);
    }

    /// <summary>The library, Settings and setup as a real Windows window shows them: the view without its own drawn
    /// buttons, Avalonia's caption buttons over it (win-04-full-app-real-light.png and so on).</summary>
    [AvaloniaFact]
    public void Real_title_bars()
    {
        using var home = new TempHome();
        using var host = new Services.AppHost(home.Path);
        foreach (var t in new[] { ThemeVariant.Light, ThemeVariant.Dark })
        {
            Shot.Take("win-04-full-app-real", SkinKind.Win, t, () => new DrawnHost(
                new WinLibrary { DataContext = NoChrome(Demo.Library()), Width = 1280, Height = 800 }, WinChrome.Decorations(), 48) { Width = 1280, Height = 800 });
            Shot.Take("win-05-setup-real", SkinKind.Win, t, () =>
            {
                var view = new WinSetup { DataContext = Demo.Setup(SkinKind.Win), DrawChrome = false };
                return new DrawnHost(view, WinChrome.Decorations(), 32, resizable: false) { Width = view.Width, Height = view.Height };
            });
            var model = Services.SettingsModel.Make(host);
            Shot.Take("win-settings-real", SkinKind.Win, t, () => new DrawnHost(new SettingsView { DataContext = model, DrawChrome = false }, WinChrome.Decorations(), 32, resizable: false)
                { Width = 900, Height = 860 }, size: new Size(1100, 988));
            model.Dispose();
        }
    }

    [AvaloniaFact]
    public void Avalonias_own_decorations_draw_a_second_title_which_ours_leave_out()
    {
        try
        {
            // Fluent's as it comes: its title over the view's, and a full-screen button no Windows window has.
            var (plain, fluent) = Open(theme: null);
            Assert.True(Part(fluent, "PART_TitleTextPanel").IsEffectivelyVisible, "Fluent's decorations should show their title (the doubled name)");
            Assert.True(Part(fluent, "PART_FullScreenButton").IsEffectivelyVisible);
            plain.Close();

            var theme = WinChrome.Decorations();
            Assert.NotNull(theme);
            var (window, ours) = Open(theme);
            Assert.False(Part(ours, "PART_TitleTextPanel").IsEffectivelyVisible, "the drawn title still shows over the header's");
            Assert.False(Part(ours, "PART_FullScreenButton").IsEffectivelyVisible, "a full-screen caption button shows");
            foreach (var button in new[] { "PART_MinimizeButton", "PART_MaximizeButton", "PART_CloseButton" })
            {
                var b = Part(ours, button);
                Assert.True(b.IsEffectivelyVisible, $"{button} is hidden");
                Assert.Equal(46, b.Bounds.Width, 0.5);
                Assert.Equal(48, b.Bounds.Height, 0.5);
            }
            // The one title left is the header's, beside the app's mark.
            var header = ((Visual)window.Content!).GetVisualDescendants().OfType<WindowHeader>().Single();
            Assert.Equal("Study Stash", header.Title);
            // The caption buttons sit in the room the header keeps for them, on its right.
            var close = Part(ours, "PART_CloseButton");
            var right = close.TranslatePoint(new Point(close.Bounds.Width, 0), window)!.Value.X;
            var min = Part(ours, "PART_MinimizeButton").TranslatePoint(new Point(0, 0), window)!.Value.X;
            Assert.Equal(window.Bounds.Width, right, 1);
            Assert.True(window.Bounds.Width - min <= header.ButtonRoom.Right + 4, $"the caption buttons start {window.Bounds.Width - min} from the right, past the header's {header.ButtonRoom.Right}");
            window.Close();
        }
        finally
        {
            ((App)Application.Current!).UseSkin(SkinKind.Mac);
        }
    }

    [AvaloniaFact]
    public void The_caption_buttons_take_the_themes_ink()
    {
        try
        {
            var (window, ours) = Open(WinChrome.Decorations());
            var glyph = Part(ours, "PART_MinimizeButton").GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Path>().Single();
            Assert.True(window.TryFindResource("Fg", window.ActualThemeVariant, out var fg));
            Assert.Equal(((ISolidColorBrush)fg!).Color, ((ISolidColorBrush)glyph.Fill!).Color);
            window.Close();
        }
        finally
        {
            ((App)Application.Current!).UseSkin(SkinKind.Mac);
        }
    }

    /// <summary>The library's bar is 48 tall (32 when the window is narrow): the window's drag strip and caption
    /// buttons follow whichever it is.</summary>
    [AvaloniaFact]
    public void The_windows_title_bar_is_as_tall_as_the_header()
    {
        try
        {
            ((App)Application.Current!).UseSkin(SkinKind.Win);
            var model = Demo.Library();
            var window = new Window { Width = 1280, Height = 800, ExtendClientAreaToDecorationsHint = true, Content = new WinLibrary { DataContext = model } };
            WinChrome.Apply(window);
            window.Show();
            window.UpdateLayout();
            Assert.Equal(48, window.ExtendClientAreaTitleBarHeightHint);
            Assert.Equal(48.0, window.Resources["CaptionButtonHeight"]);
            Assert.Same(WinChrome.Decorations(), window.WindowDecorationsTheme);
            model.Narrow = true;
            window.UpdateLayout();
            Assert.Equal(32, window.ExtendClientAreaTitleBarHeightHint);
            Assert.Equal(32.0, window.Resources["CaptionButtonHeight"]);
            window.Close();
        }
        finally
        {
            ((App)Application.Current!).UseSkin(SkinKind.Mac);
        }
    }

    /// <summary>Without Windows 11's backdrop (headless has none), the window's ground is the design's Mica colour, in
    /// the colour theme: solid, never clear.</summary>
    [AvaloniaFact]
    public void Without_mica_the_ground_is_the_designs_colour()
    {
        try
        {
            ((App)Application.Current!).UseSkin(SkinKind.Win);
            var window = new Window { Width = 640, Height = 480, ExtendClientAreaToDecorationsHint = true, Content = new Border() };
            WinChrome.Apply(window);
            window.Show();
            Assert.Equal(WinChrome.Ground.Solid, WinChrome.Of(window));
            Assert.True(window.TryFindResource("Mica", window.ActualThemeVariant, out var mica));
            var ground = Assert.IsAssignableFrom<ISolidColorBrush>(window.Background);
            Assert.Equal(((ISolidColorBrush)mica!).Color, ground.Color);
            Assert.Equal(255, ground.Color.A);
            window.Close();
        }
        finally
        {
            ((App)Application.Current!).UseSkin(SkinKind.Mac);
        }
    }

    /// <summary>The notes fade out under the ask bar by a mask, not a colour laid over them: over the real Mica (the
    /// Mica token clear) a guessed colour was the pale band over the bottom of the page.</summary>
    [AvaloniaFact]
    public void The_notes_fade_out_by_a_mask_whatever_is_behind_the_page()
    {
        try
        {
            ((App)Application.Current!).UseSkin(SkinKind.Win);
            var view = new WinLibrary { DataContext = Demo.Library(), Width = 1280, Height = 800 };
            var window = new Window { Width = 1280, Height = 800, Content = view };
            window.Show();
            window.UpdateLayout();
            var fade = view.FindControl<Border>("Fade")!;
            var notes = view.FindControl<ScrollViewer>("NoteScroll")!;
            Assert.Null(fade.Background);
            var mask = Assert.IsType<LinearGradientBrush>(notes.OpacityMask);
            // Clear from the fade's top to the page's bottom: 120 px, gone for the last quarter.
            Assert.Equal(notes.Bounds.Height - fade.Bounds.Height, mask.StartPoint.Point.Y, 0.5);
            Assert.Equal(notes.Bounds.Height, mask.EndPoint.Point.Y, 0.5);
            Assert.Equal(0, mask.GradientStops[1].Color.A);
            window.Resources["Mica"] = Brushes.Transparent;
            window.UpdateLayout();
            Assert.Null(fade.Background);
            Assert.NotNull(notes.OpacityMask);
            window.Close();
        }
        finally
        {
            ((App)Application.Current!).UseSkin(SkinKind.Mac);
        }
    }

    /// <summary>Nothing blurs behind a Windows panel, so the design's slightly see-through acrylic is solid: the
    /// window behind the quick panel no longer shows through it as ghosted words.</summary>
    [AvaloniaFact]
    public void Windows_acrylic_is_solid()
    {
        try
        {
            ((App)Application.Current!).UseSkin(SkinKind.Win);
            foreach (var variant in new[] { ThemeVariant.Light, ThemeVariant.Dark })
            {
                Assert.True(Application.Current!.TryFindResource("Acrylic", variant, out var acrylic));
                Assert.Equal(255, ((ISolidColorBrush)acrylic!).Color.A);
                Assert.True(Skin.Build(SkinKind.Win).TryGetResource("Acrylic", variant, out var design));
                var d = ((ISolidColorBrush)design!).Color;
                Assert.Equal(Color.FromRgb(d.R, d.G, d.B), ((ISolidColorBrush)acrylic).Color);
            }
        }
        finally
        {
            ((App)Application.Current!).UseSkin(SkinKind.Mac);
        }
    }
}
