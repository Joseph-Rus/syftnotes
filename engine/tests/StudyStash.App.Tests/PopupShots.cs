using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StudyStash.App.Controls;
using StudyStash.App.ViewModels;
using StudyStash.App.Views;
using StudyStash.Core.Ai;

namespace StudyStash.App.Tests;

/// <summary>
/// Every popup menu, flyout, context menu, select list and tooltip, opened for real over what opens it, light and dark
/// in both looks: just its own rounded panel. The ask bar's "Answer with" once showed a second, square-ish box round
/// its rounded glass (Fluent's flyout presenter, with its padding), and tooltips Fluent's dark-edged box; the pictures
/// go to STUDYSTASH_SHOTS as <c>popup-&lt;mac|win&gt;-&lt;menu&gt;-&lt;light|dark&gt;.png</c>.
/// </summary>
public class PopupShots
{
    const string Lecture = "lec-1";
    static readonly ThemeVariant[] Themes = [ThemeVariant.Light, ThemeVariant.Dark];
    static readonly SkinKind[] Skins = [SkinKind.Mac, SkinKind.Win];

    static PopupShots() => Environment.SetEnvironmentVariable("STUDYSTASH_STILL", "1");

    /// <summary>One menu to open: what it hangs off, and how a student opens it (it hands back the popup's panel).
    /// <paramref name="Corner"/> is how far in from the panel's edge its corner pixels are checked: a tooltip's small
    /// radius already covers the pixel one in.</summary>
    sealed record Menu(string Name, Func<SkinKind, Task<Control>> Build, Func<Control, Control> Open, Size Size, int Corner = 1);

    static IEnumerable<Menu> Menus()
    {
        yield return new("answer-with", s => Task.FromResult<Control>(AskBar(s)), host => OpenFlyout(host.FindControl<Button>("EngineChip")
            ?? host.GetVisualDescendants().OfType<Button>().First(b => b.Name == "EngineChip")), new Size(900, 400));
        yield return new("scope", s => Task.FromResult<Control>(AskBar(s)), host => OpenFlyout(host.GetVisualDescendants().OfType<Button>().First(b => b.Flyout is MenuFlyout)), new Size(900, 400));
        yield return new("rewrite", RewriteNotes, host => OpenFlyout(host.GetVisualDescendants().OfType<Button>().First(b => b.Name == "RewriteButton")), new Size(900, 420));
        yield return new("class-picker", _ => Task.FromResult<Control>(Anchor()), host =>
        {
            var menu = ClassPicker.Build([("CS 101", 0), ("BIO 110", 1), ("CALC II", 2), ("HIST 210", 3)], "BIO 110", _ => { });
            menu.Placement = PlacementMode.BottomEdgeAlignedLeft; // (the app opens it at the pointer)
            menu.Open(host);
            Dispatcher.UIThread.RunJobs();
            return menu;
        }, new Size(600, 340));
        yield return new("settings-select", _ => Task.FromResult<Control>(SettingsSelect()), host => OpenFlyout((Button)host), new Size(600, 260));
        yield return new("engine-options", Engines, host => OpenFlyout(EngineOptions(host)), new Size(900, 420));
        yield return new("engine-models", Engines, host =>
        {
            var options = OpenFlyout(EngineOptions(host));
            var models = options.GetVisualDescendants().OfType<MenuItem>().First(i => i.Header as string == "Models");
            Click(models);
            Assert.True(models.IsSubMenuOpen, "the Models submenu didn't open");
            Dispatcher.UIThread.RunJobs();
            return SubmenuPanel(models);
        }, new Size(900, 420));
        yield return new("lecture-move", _ => Task.FromResult<Control>(Anchor()), host =>
        {
            var menu = Shell.Menu();
            foreach (var (name, color) in new[] { ("CS 101", 0), ("CALC II", 2), ("HIST 210", 3), ("Unsorted", -1) })
            {
                var item = new MenuItem { Header = name };
                if (color >= 0) item.Icon = new Avalonia.Controls.Shapes.Ellipse { Width = 8, Height = 8, Fill = Skin.ClassDot(color) };
                menu.Items.Add(item);
            }
            menu.Placement = PlacementMode.BottomEdgeAlignedLeft; // (the app opens it at the pointer)
            menu.Open(host);
            Dispatcher.UIThread.RunJobs();
            return menu;
        }, new Size(600, 300));
        yield return new("select", _ => Task.FromResult<Control>(Select()), host =>
        {
            var select = (ComboBox)host;
            select.IsDropDownOpen = true;
            Dispatcher.UIThread.RunJobs();
            var panel = select.GetVisualDescendants().OfType<Popup>().First(p => p.Name == "PART_Popup").Child!;
            var hovered = panel.GetVisualDescendants().OfType<ComboBoxItem>().ElementAt(1);
            TopLevel.GetTopLevel(select)!.MouseMove(hovered.TranslatePoint(new Point(20, 8), TopLevel.GetTopLevel(select)!)!.Value);
            return panel;
        }, new Size(600, 300));
        yield return new("tooltip", _ => Task.FromResult<Control>(Tipped()), host =>
        {
            ToolTip.SetIsOpen(host, true);
            Dispatcher.UIThread.RunJobs();
            var tip = TopLevel.GetTopLevel(host)!.GetVisualDescendants().OfType<ToolTip>().Single();
            Assert.True(ToolTip.GetIsOpen(host), "the tooltip didn't open");
            tip.Transitions = null; // (its quick fade in: the picture is of it shown)
            tip.Opacity = 1;
            return tip;
        }, new Size(400, 160), Corner: 0);
        yield return new("canvas-select", CanvasSettings, host => OpenFlyout(host.GetVisualDescendants().OfType<Button>().First(b => b.Classes.Contains("cv-select") && b.Flyout is Flyout && b.IsEffectivelyVisible)), new Size(1000, 640));
    }

    static Control AskBar(SkinKind skin)
    {
        var m = AiDemo.AskIdle();
        Control bar = skin == SkinKind.Mac ? new MacAiAskBar { DataContext = m } : new WinAiAskBar { DataContext = m };
        bar.Width = 600;
        bar.Margin = new Thickness(0, 240, 0, 0);
        return bar;
    }

    static async Task<Control> RewriteNotes(SkinKind skin)
    {
        var m = new AiNotesModel(new FakeAiLibrary { Overview = AiDemo.Overview(), OnRewrite = id => new RewriteInfo(id, "none") });
        await m.Load(Lecture, "# Recursion\n\nA function that calls itself.", "Ollama", "2026-09-22T10:00:00Z");
        Control view = skin == SkinKind.Mac ? new MacAiNotes { DataContext = m } : new WinAiNotes { DataContext = m };
        view.Width = 640;
        view.Height = 300;
        return view;
    }

    static Button Anchor() => new() { Content = "Pick", Width = 64, Height = 32 };

    static ComboBox Select() => new()
    {
        ItemsSource = new[] { "Same as notes", "Claude Code", "Ollama · qwen3:30b" },
        SelectedIndex = 0,
        Width = 220,
    };

    static Button Tipped()
    {
        var button = new Button { Content = new Icon { Glyph = "drive_file_move", Size = 16 }, Width = 32, Height = 32 };
        ToolTip.SetTip(button, "Move to another class");
        ToolTip.SetPlacement(button, PlacementMode.Bottom);
        return button;
    }

    static Button SettingsSelect()
    {
        var settings = new SettingsView();
        return new Button
        {
            Content = "Sorting",
            Flyout = new MenuFlyout
            {
                Placement = PlacementMode.BottomEdgeAlignedLeft,
                ItemsSource = new[]
                {
                    new SettingChoice("qwen3:30b", "qwen3:30b (19 GB)", _ => { }),
                    new SettingChoice("llama3:8b", "llama3:8b (5 GB)", _ => { }),
                    new SettingChoice("gemma3:12b", "gemma3:12b (8 GB)", _ => { }),
                },
                ItemContainerTheme = (ControlTheme)settings.Resources["PickItem"]!,
                FlyoutPresenterTheme = (ControlTheme)settings.Resources["SettingsMenu"]!,
            },
        };
    }

    static async Task<Control> Engines(SkinKind skin)
    {
        var overview = AiDemo.Overview() with
        {
            Engines = [new EngineInfo("ollama", "Ollama", "ready") { Installed = true, Model = "qwen3:30b",
                Models = [new ModelOption("qwen3:30b", "qwen3:30b (19 GB)"), new ModelOption("llama3:8b", "llama3:8b (5 GB)")] }],
        };
        var m = new AiEnginesModel(new FakeAiLibrary { Overview = overview });
        await m.Load();
        Control view = skin == SkinKind.Mac ? new MacAiEngines { DataContext = m } : new WinAiEngines { DataContext = m };
        return new ScrollViewer { Content = view, Width = 760, Height = 360 };
    }

    static Button EngineOptions(Control host) =>
        host.GetVisualDescendants().OfType<Button>().First(b => b.Flyout is MenuFlyout && b.IsEffectivelyVisible && b.DataContext is AiEngineRow);

    static async Task<Control> CanvasSettings(SkinKind skin)
    {
        var model = await CanvasShots.Settings();
        Control view = skin == SkinKind.Mac ? new MacCanvasSettings { DataContext = model } : new WinCanvasSettings { DataContext = model };
        view.Width = 720;
        return view;
    }

    static void Click(Visual target)
    {
        var top = TopLevel.GetTopLevel(target) ?? throw new InvalidOperationException("not on screen");
        var at = target.TranslatePoint(new Point(target.Bounds.Width / 2, target.Bounds.Height / 2), top)!.Value;
        top.MouseMove(at);
        top.MouseDown(at, MouseButton.Left);
        top.MouseUp(at, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>Clicks <paramref name="button"/> (its command fills some menus) and hands back its flyout's
    /// presenter, what the popup shows. MenuPickTests checks the click itself opens it.</summary>
    static Control OpenFlyout(Button button)
    {
        var flyout = Assert.IsAssignableFrom<PopupFlyoutBase>(button.Flyout);
        Click(button);
        if (!flyout.IsOpen) flyout.ShowAt(button);
        Assert.True(flyout.IsOpen, "the menu didn't open");
        Dispatcher.UIThread.RunJobs();
        return Assert.IsAssignableFrom<Control>(flyout.Popup.Child);
    }

    /// <summary>The panel a submenu shows: the child of the item's own popup.</summary>
    static Control SubmenuPanel(MenuItem item) =>
        item.GetVisualDescendants().OfType<Popup>().First(p => p.Name == "PART_Popup").Child!;

    /// <summary>The popup's panel drawn on its own, on nothing, so what isn't the panel is see-through.</summary>
    static RenderTargetBitmap Alone(Control panel)
    {
        var size = new PixelSize((int)Math.Ceiling(panel.Bounds.Width), (int)Math.Ceiling(panel.Bounds.Height));
        var bitmap = new RenderTargetBitmap(size);
        bitmap.Render(panel);
        return bitmap;
    }

    static byte AlphaAt(RenderTargetBitmap bitmap, int x, int y)
    {
        using var copy = new WriteableBitmap(bitmap.PixelSize, bitmap.Dpi, Avalonia.Platform.PixelFormat.Bgra8888, Avalonia.Platform.AlphaFormat.Premul);
        using (var fb = copy.Lock()) bitmap.CopyPixels(new PixelRect(bitmap.PixelSize), fb.Address, fb.RowBytes * fb.Size.Height, fb.RowBytes);
        using var read = copy.Lock();
        return System.Runtime.InteropServices.Marshal.ReadByte(read.Address, y * read.RowBytes + x * 4 + 3);
    }

    /// <summary>
    /// Opens <paramref name="menu"/> over its host on the design's ground, checks it's one rounded panel with nothing
    /// round it, and saves the picture.
    /// </summary>
    static async Task Open(Menu menu, SkinKind skin, ThemeVariant variant)
    {
        Skin.UseTheme(ColourThemes.Default);
        ((App)Application.Current!).UseSkin(skin);
        var (width, height) = (menu.Size.Width, menu.Size.Height);
        var host = await menu.Build(skin);
        host.HorizontalAlignment = HorizontalAlignment.Left;
        host.VerticalAlignment = VerticalAlignment.Top;
        var ground = skin == SkinKind.Mac ? (Control)Shot.Wallpaper(width, height, variant == ThemeVariant.Dark) : new Border { Width = width, Height = height };
        if (ground is Border b) b.Bind(Border.BackgroundProperty, b.GetResourceObservable("Ground"));
        var root = new Panel { Children = { ground, new Border { Padding = new Thickness(48), Child = host } } };
        var window = new Window { Width = width, Height = height, RequestedThemeVariant = variant, Content = root };
        Glass.SetBlurBackdrop(window, true);
        Look.Apply(window);
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            var panel = menu.Open(host);
            Dispatcher.UIThread.RunJobs();
            string label = $"{skin} {menu.Name} {variant}";
            Assert.True(panel.Bounds.Width > 40 && panel.Bounds.Height > 16, $"{label}: the menu is an empty box ({panel.Bounds})");

            string name = $"popup-{skin.ToString().ToLowerInvariant()}-{menu.Name}-{(variant == ThemeVariant.Dark ? "dark" : "light")}";
            var whole = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("nothing rendered");
            using (var file = File.Create(Path.Combine(Shot.Dir, name + ".png"))) whole.Save(file, PngBitmapEncoderOptions.Default);

            // A flyout whose content draws its own panel (the glass menus) shows nothing of its own round it.
            if (panel is FlyoutPresenter presenter && presenter.Content is Control content
                && content.GetVisualDescendants().Prepend(content).OfType<Glass>().FirstOrDefault() is { } glass)
            {
                Assert.True(presenter.Background is null or ISolidColorBrush { Color.A: 0 }, $"{label}: the flyout paints a box round its glass");
                Assert.Equal(default, presenter.Padding);
                Assert.Equal(default, presenter.BorderThickness);
                Assert.Equal(panel.Bounds.Size, glass.Bounds.Size);
            }

            // Rounded, and nothing square behind it: the corners are see-through (no box, and no drawn shadow, which in
            // a popup's own window would only fill them), the edges are the panel's.
            var alone = Alone(panel);
            int w = alone.PixelSize.Width, h = alone.PixelSize.Height;
            int c = menu.Corner;
            foreach (var (x, y) in new[] { (c, c), (w - 1 - c, c), (c, h - 1 - c), (w - 1 - c, h - 1 - c) })
                Assert.True(AlphaAt(alone, x, y) < 8, $"{label}: its corner ({x},{y}) isn't see-through: a box or a drawn shadow fills it");
            foreach (var (x, y) in new[] { (w / 2, 1), (w / 2, h - 2), (1, h / 2), (w - 2, h / 2) })
                Assert.True(AlphaAt(alone, x, y) > 60, $"{label}: ({x},{y}) is empty: the panel doesn't reach the popup's edge");

        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task Every_menu_is_just_its_own_rounded_panel()
    {
        foreach (var skin in Skins)
            foreach (var menu in Menus())
                foreach (var t in Themes)
                    await Open(menu, skin, t);
    }
}
