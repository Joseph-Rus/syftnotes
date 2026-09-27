using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using Avalonia.Styling;
using Avalonia.Threading;
using SkiaSharp;
using StudyStash.App.Controls;
using StudyStash.App.Services;
using StudyStash.App.ViewModels;
using StudyStash.App.Views;

[assembly: AvaloniaTestApplication(typeof(StudyStash.App.Tests.TestApp))]
// Every test on Avalonia's one UI thread, plain [Fact]s too: a plain test touching Avalonia first can otherwise
// make a worker thread the UI thread, and the pictures drawn after it fail (it happened on Windows CI).
[assembly: AvaloniaTestFramework]

namespace StudyStash.App.Tests;

public static class TestApp
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<StudyStash.App.App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}

/// <summary>
/// Every surface drawn as the design shows it, light and dark, into STUDYSTASH_SHOTS (or shots/ beside the tests): the
/// same size, ground and file name as the design's picture of it (ref/mac-01-dropdown-light.png and so on), the
/// surface 64 px in, so the two can be laid over each other. Also shows the Windows look from CI.
/// </summary>
public static class Shot
{
    public static string Dir
    {
        get
        {
            string dir = Environment.GetEnvironmentVariable("STUDYSTASH_SHOTS") is { Length: > 0 } d ? d : Path.Combine(AppContext.BaseDirectory, "shots");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    /// <summary>How tall the design's picture of each section is (they're all 1700 wide): ref/index.json.</summary>
    static readonly Dictionary<string, int> Heights = new()
    {
        ["mac-01"] = 548, ["mac-02"] = 648, ["mac-03"] = 968, ["mac-04"] = 928, ["mac-05"] = 608, ["mac-06"] = 948,
        ["mac-07"] = 888, ["mac-08"] = 654, ["mac-09"] = 928, ["mac-10"] = 928, ["mac-11"] = 928, ["mac-12"] = 507,
        ["mac-13"] = 908, ["mac-14"] = 908, ["mac-15"] = 768, ["mac-16"] = 588, ["mac-17"] = 920, ["mac-18"] = 520,
        ["win-01"] = 673, ["win-02"] = 648, ["win-03"] = 1097, ["win-04"] = 928, ["win-05"] = 608, ["win-06"] = 1118,
        ["win-07"] = 928, ["win-08"] = 668, ["win-09"] = 928, ["win-10"] = 928, ["win-11"] = 928, ["win-12"] = 608,
        ["win-13"] = 988, ["win-14"] = 988, ["win-15"] = 808, ["win-16"] = 588, ["win-17"] = 920, ["win-18"] = 567,
    };

    /// <summary>The size of the design's picture a shot is named after ("mac-05-setup-microphone" is section 05's).</summary>
    public static Size RefSize(string name) =>
        name.Length >= 6 && Heights.TryGetValue(name[..6], out int h) ? new Size(1700, h)
            : throw new ArgumentException($"{name} isn't named after a section of the design; give its size", nameof(name));

    /// <summary>
    /// Draws what <paramref name="build"/> makes 64 px in on the design's ground (the Mac's wallpaper, Windows' grey)
    /// and saves it as <c>&lt;name&gt;-&lt;light|dark&gt;.png</c>, the size of the design's picture of that section
    /// (or <paramref name="size"/>). The glass blurs what's behind it here, as the design does.
    /// </summary>
    public static Bitmap Take(string name, SkinKind skin, ThemeVariant variant, Func<Control> build, ColourTheme? theme = null, Size? size = null)
    {
        // The colour theme every time too, so one test's theme never shows up in another's picture.
        Skin.UseTheme(theme ?? ColourThemes.Default);
        ((StudyStash.App.App)Application.Current!).UseSkin(skin);
        var (width, height) = size ?? RefSize(name);
        var content = build();
        content.HorizontalAlignment = HorizontalAlignment.Left;
        content.VerticalAlignment = VerticalAlignment.Top;
        // Top-aligned, like the design's canvas: a surface taller than the picture runs off the bottom, not up.
        var surface = new Border { Padding = new Thickness(64), Child = content, VerticalAlignment = VerticalAlignment.Top, HorizontalAlignment = HorizontalAlignment.Left };
        var root = new Panel { Children = { skin == SkinKind.Mac ? Wallpaper(width, height, variant == ThemeVariant.Dark) : Ground(width, height), surface } };
        // A window with room to spare, cut to the picture's size afterwards, so nothing is squeezed.
        var window = new Window { Width = width + 400, Height = height + 400, RequestedThemeVariant = variant, Content = root };
        Glass.SetBlurBackdrop(window, true);
        Views.Look.Apply(window);
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var whole = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("nothing rendered");
        var frame = new RenderTargetBitmap(new PixelSize((int)width, (int)height));
        using (var ctx = frame.CreateDrawingContext())
            ctx.DrawImage(whole, new Rect(0, 0, width, height), new Rect(0, 0, width, height));
        using (var file = File.Create(Path.Combine(Dir, $"{name}-{(variant == ThemeVariant.Dark ? "dark" : "light")}.png"))) frame.Save(file, PngBitmapEncoderOptions.Default);
        window.Close();
        return frame;
    }

    public static StackPanel Side(params Control[] items)
    {
        var p = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 56, VerticalAlignment = VerticalAlignment.Top, HorizontalAlignment = HorizontalAlignment.Left };
        foreach (var c in items) p.Children.Add(c);
        return p;
    }

    /// <summary>Windows' ground: the design's plain grey (the Ground token).</summary>
    static Border Ground(double width, double height)
    {
        var ground = new Border { Width = width, Height = height, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        ground.Bind(Border.BackgroundProperty, ground.GetResourceObservable("Ground"));
        return ground;
    }

    /// <summary>The Mac's ground: the design's desktop, with its "desktop wallpaper placeholder" in the corner.</summary>
    public static Panel Wallpaper(double width, double height, bool dark)
    {
        var label = new TextBlock
        {
            Text = "desktop wallpaper placeholder", FontFamily = new FontFamily("SF Mono, Menlo, Consolas, monospace"), FontSize = 11,
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 16, 12),
        };
        label.Bind(TextBlock.ForegroundProperty, label.GetResourceObservable("Fg2"));
        return new Panel
        {
            Width = width, Height = height, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
            Children = { new WallpaperPicture(dark), label },
        };
    }
}

/// <summary>
/// The design's desktop (renderVals' WALL_L and WALL_D): a wash from top to bottom with three soft patches of colour
/// over it, blue at the bottom left, violet at the top right and a paler one in the middle. Drawn with Skia directly
/// so the gradients are dithered as Chrome's are: undithered steps come out as rings once the glass saturates them.
/// </summary>
sealed class WallpaperPicture(bool dark) : Control
{
    public override void Render(DrawingContext context) => context.Custom(new Paint(new Rect(Bounds.Size), dark));

    sealed class Paint(Rect rect, bool dark) : ICustomDrawOperation
    {
        public Rect Bounds => rect;

        public bool HitTest(Point p) => false;

        public bool Equals(ICustomDrawOperation? other) => false;

        public void Dispose() { }

        public void Render(ImmediateDrawingContext context)
        {
            if (context.TryGetFeature<ISkiaSharpApiLeaseFeature>() is not { } feature) return;
            using var lease = feature.Lease();
            var canvas = lease.SkCanvas;
            float w = (float)rect.Width, h = (float)rect.Height;
            using var paint = new SKPaint { IsDither = true };
            void Fill(SKShader shader)
            {
                using (shader)
                {
                    paint.Shader = shader;
                    canvas.DrawRect(0, 0, w, h, paint);
                    paint.Shader = null;
                }
            }
            // CSS lists the top layer first; here the bottom one comes first.
            Fill(dark ? Wash(w, h, (0.2, 0.04, 250), (0.14, 0.05, 275)) : Wash(w, h, (0.9, 0.04, 240), (0.82, 0.08, 265)));
            (double X, double Y, double Rx, double Ry, double Fade, Color Colour)[] patches = dark
                ?
                [
                    (0.60, 0.60, 0.6, 0.6, 0.7, Oklch.ToColor(0.3, 0.08, 230)),
                    (0.95, 0.00, 0.8, 0.7, 0.6, Oklch.ToColor(0.34, 0.12, 300)),
                    (0.15, 1.10, 0.9, 0.8, 0.6, Oklch.ToColor(0.38, 0.14, 260)),
                ]
                :
                [
                    (0.60, 0.60, 0.6, 0.6, 0.7, Oklch.ToColor(0.86, 0.07, 220)),
                    (0.95, 0.00, 0.8, 0.7, 0.6, Oklch.ToColor(0.8, 0.1, 300)),
                    (0.15, 1.10, 0.9, 0.8, 0.6, Oklch.ToColor(0.72, 0.13, 255)),
                ];
            foreach (var (x, y, rx, ry, fade, colour) in patches) Fill(Patch(w, h, x, y, rx, ry, colour, fade));
        }

        /// <summary>
        /// CSS <c>linear-gradient(160deg, from, to)</c> over a w × h box: the line runs at 160° through the centre, long
        /// enough that the corners get the end colours. oklch() colours mix in Oklab in CSS, so the stops between are
        /// worked out there.
        /// </summary>
        static SKShader Wash(float w, float h, (double L, double C, double H) from, (double L, double C, double H) to)
        {
            double angle = 160 * Math.PI / 180, dx = Math.Sin(angle), dy = -Math.Cos(angle);
            double half = (Math.Abs(w * dx) + Math.Abs(h * dy)) / 2;
            static (double L, double A, double B) Lab((double L, double C, double H) c) =>
                (c.L, c.C * Math.Cos(c.H * Math.PI / 180), c.C * Math.Sin(c.H * Math.PI / 180));
            var (a, b) = (Lab(from), Lab(to));
            const int steps = 8;
            var colours = new SKColor[steps + 1];
            var at = new float[steps + 1];
            for (int i = 0; i <= steps; i++)
            {
                double t = (double)i / steps, l = a.L + (b.L - a.L) * t, x = a.A + (b.A - a.A) * t, y = a.B + (b.B - a.B) * t;
                colours[i] = Sk(Oklch.ToColor(l, Math.Sqrt(x * x + y * y), Math.Atan2(y, x) * 180 / Math.PI));
                at[i] = (float)t;
            }
            return SKShader.CreateLinearGradient(new SKPoint((float)(w / 2 - dx * half), (float)(h / 2 - dy * half)),
                new SKPoint((float)(w / 2 + dx * half), (float)(h / 2 + dy * half)), colours, at, SKShaderTileMode.Clamp);
        }

        /// <summary>
        /// CSS <c>radial-gradient(rx ry at x y, colour 0%, transparent fade)</c>, all relative to the box: a unit circle
        /// stretched to the ellipse. It fades to the same colour with no alpha (as CSS mixes toward transparent), not
        /// to transparent black, which would grey it.
        /// </summary>
        static SKShader Patch(float w, float h, double x, double y, double rx, double ry, Color colour, double fade) =>
            SKShader.CreateRadialGradient(new SKPoint(0, 0), 1, [Sk(colour), Sk(colour).WithAlpha(0)], [0, (float)fade], SKShaderTileMode.Clamp,
                SKMatrix.CreateScale((float)(rx * w), (float)(ry * h)).PostConcat(SKMatrix.CreateTranslation((float)(x * w), (float)(y * h))));

        static SKColor Sk(Color c) => new(c.R, c.G, c.B, c.A);
    }
}

public class SurfaceShots
{
    static readonly ThemeVariant[] Themes = [ThemeVariant.Light, ThemeVariant.Dark];

    static SurfaceShots() => Environment.SetEnvironmentVariable("STUDYSTASH_STILL", "1");

    [AvaloniaFact]
    public void Mac_dropdown()
    {
        foreach (var t in Themes)
            Shot.Take("mac-01-dropdown", SkinKind.Mac, t, () => Shot.Side(
                new MacPanel { DataContext = Demo.Panel(recording: false), VerticalAlignment = VerticalAlignment.Top },
                new MacPanel { DataContext = Demo.Panel(recording: true), VerticalAlignment = VerticalAlignment.Top }));
    }

    /// <summary>A library-only computer's dropdown: the library's state instead of Record.</summary>
    [AvaloniaFact]
    public void Mac_dropdown_library()
    {
        foreach (var t in Themes)
            Shot.Take("mac-01-dropdown-library", SkinKind.Mac, t, () => new MacPanel { DataContext = Demo.LibraryPanel(), VerticalAlignment = VerticalAlignment.Top });
    }

    [AvaloniaFact]
    public void Win_flyout_library()
    {
        foreach (var t in Themes)
            Shot.Take("win-01-dropdown-library", SkinKind.Win, t, () => OverTaskbar(new WinPanel { DataContext = Demo.LibraryPanel(), VerticalAlignment = VerticalAlignment.Top }, false, "12:34", "25/09/2026"));
    }

    /// <summary>Windows-only, shots-only: a flyout/recorder mockup sits over a taskbar strip in the design's
    /// pictures for context (not part of the app) — <see cref="TaskbarStrip"/> right-aligned under it.</summary>
    static StackPanel OverTaskbar(Control surface, bool recording, string time, string date) => new()
    {
        Spacing = 12, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top,
        Children = { surface, TaskbarStrip(recording, time, date) },
    };

    /// <summary>An <see cref="Icon"/> in a themed foreground, for the shots-only Windows taskbar mockups.</summary>
    static Icon TaskbarIcon(string glyph, double size, string fg)
    {
        var icon = new Icon { Glyph = glyph, Size = size, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        icon.Bind(Icon.ForegroundProperty, icon.GetResourceObservable(fg));
        return icon;
    }

    /// <summary>
    /// Windows-only, shots-only (win-01/02's Win Flyout.html): the taskbar strip under a flyout — the launcher
    /// arrow, the app's taskbar button (a red dot on it while recording), network/volume/battery, and the clock.
    /// Not part of the app; drawn only so the comparison picture matches the design's context around it.
    /// </summary>
    static Border TaskbarStrip(bool recording, string time, string date)
    {
        var appButton = new Border { Width = 36, Height = 40, CornerRadius = new CornerRadius(4), Child = new AppIcon { Width = 22, Height = 22 } };
        appButton.Bind(Border.BackgroundProperty, appButton.GetResourceObservable("Subtle2"));
        if (recording)
        {
            var ring = new Border { Width = 10, Height = 10, CornerRadius = new CornerRadius(5), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 3, 5) };
            ring.Bind(Border.BackgroundProperty, ring.GetResourceObservable("Subtle2"));
            var dot = new Border
            {
                Width = 8, Height = 8, CornerRadius = new CornerRadius(4), Background = new SolidColorBrush(Color.Parse("#E5484D")),
                HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 4, 6),
            };
            appButton.Child = new Panel { Children = { new AppIcon { Width = 22, Height = 22 }, ring, dot } };
        }

        var icons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Margin = new Thickness(10, 0), VerticalAlignment = VerticalAlignment.Center };
        foreach (var glyph in new[] { "wifi", "volume_up", "battery_5_bar" }) icons.Children.Add(TaskbarIcon(glyph, 18, "Fg"));

        TextBlock Line(string text)
        {
            var t = new TextBlock { Text = text, FontSize = 12, LineHeight = 16.2, TextAlignment = TextAlignment.Right };
            t.Bind(TextBlock.ForegroundProperty, t.GetResourceObservable("Fg"));
            return t;
        }
        var clock = new StackPanel { Margin = new Thickness(6, 0), HorizontalAlignment = HorizontalAlignment.Right, Children = { Line(time), Line(date) } };

        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 2, VerticalAlignment = VerticalAlignment.Center,
            Children = { new Border { Width = 32, Height = 40, Child = TaskbarIcon("keyboard_arrow_up", 18, "Fg") }, appButton, icons, clock },
        };
        var strip = new Border
        {
            Height = 48, CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), Padding = new Thickness(8, 0),
            Child = row, HorizontalAlignment = HorizontalAlignment.Right,
        };
        strip.Bind(Border.BackgroundProperty, strip.GetResourceObservable("Taskbar"));
        strip.Bind(Border.BorderBrushProperty, strip.GetResourceObservable("FlyStroke"));
        return strip;
    }

    [AvaloniaFact]
    public void Win_flyout()
    {
        foreach (var t in Themes)
            Shot.Take("win-01-dropdown", SkinKind.Win, t, () => Shot.Side(
                OverTaskbar(new WinPanel { DataContext = Demo.Panel(recording: false), VerticalAlignment = VerticalAlignment.Top }, false, "12:34", "25/09/2026"),
                OverTaskbar(new WinPanel { DataContext = Demo.Panel(recording: true), VerticalAlignment = VerticalAlignment.Top }, true, "10:24", "23/09/2026")));
    }

    /// <summary>
    /// Windows-only, shots-only (win-02's Win Recorder.html): the app's taskbar button while a lecture transcribes
    /// — a 2×2 grid button, search, and the app icon over a 42% progress track — with the design's caption.
    /// </summary>
    static Control TranscribingTaskbarButton()
    {
        Border Square() { var b = new Border { Width = 8, Height = 8, CornerRadius = new CornerRadius(1) }; b.Bind(Border.BackgroundProperty, b.GetResourceObservable("Fg2")); return b; }
        StackPanel Row() => new() { Orientation = Orientation.Horizontal, Spacing = 2, Children = { Square(), Square() } };
        var grid = new StackPanel { Spacing = 2, Width = 18, Height = 18, Children = { Row(), Row() } };
        var gridButton = new Border { Width = 40, Height = 40, CornerRadius = new CornerRadius(4), Child = grid };
        var searchButton = new Border { Width = 40, Height = 40, CornerRadius = new CornerRadius(4), Child = TaskbarIcon("search", 20, "Fg2") };

        var accentIcon = new AppIcon { Width = 22, Height = 22 };
        var track = new Border { Width = 28, Height = 3, CornerRadius = new CornerRadius(2) };
        track.Bind(Border.BackgroundProperty, track.GetResourceObservable("Fg3"));
        var fill = new Border { Width = 28 * 0.42, Height = 3, CornerRadius = new CornerRadius(2), Background = new SolidColorBrush(Color.Parse("#4CAF50")), HorizontalAlignment = HorizontalAlignment.Left };
        var progressButton = new Border
        {
            Width = 40, Height = 40, CornerRadius = new CornerRadius(4),
            Child = new StackPanel
            {
                Spacing = 5, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 4, 0, 0),
                Children = { accentIcon, new Panel { Width = 28, Height = 3, Children = { track, fill } } },
            },
        };
        progressButton.Bind(Border.BackgroundProperty, progressButton.GetResourceObservable("Subtle2"));

        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center, Children = { gridButton, searchButton, progressButton } };
        var strip = new Border
        {
            Height = 48, CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), Padding = new Thickness(8, 0),
            Child = row, HorizontalAlignment = HorizontalAlignment.Left,
        };
        strip.Bind(Border.BackgroundProperty, strip.GetResourceObservable("Taskbar"));
        strip.Bind(Border.BorderBrushProperty, strip.GetResourceObservable("FlyStroke"));

        var caption = new TextBlock { Text = "Taskbar button while a lecture transcribes (42%)", FontSize = 12 };
        caption.Bind(TextBlock.ForegroundProperty, caption.GetResourceObservable("Fg2"));
        return new StackPanel { Spacing = 8, HorizontalAlignment = HorizontalAlignment.Left, Children = { strip, caption } };
    }

    static StackPanel Recorders(Func<RecorderModel, Control> view, Control? extra = null)
    {
        var pills = new StackPanel { Spacing = 32, VerticalAlignment = VerticalAlignment.Top };
        pills.Children.Add(view(Demo.Recorder()));
        // The pointer over it: Stop in the level meter's place.
        var hovered = Demo.Recorder();
        hovered.Hovered = true;
        pills.Children.Add(view(hovered));
        pills.Children.Add(view(Demo.Recorder(paused: true)));
        if (extra is not null) pills.Children.Add(extra);
        return Shot.Side(pills, view(Demo.Recorder(expanded: true)));
    }

    [AvaloniaFact]
    public void Mac_recorder()
    {
        foreach (var t in Themes) Shot.Take("mac-02-recorder", SkinKind.Mac, t, () => Recorders(m => new MacRecorder { DataContext = m, VerticalAlignment = VerticalAlignment.Top }));
    }

    [AvaloniaFact]
    public void Win_recorder()
    {
        foreach (var t in Themes) Shot.Take("win-02-recorder", SkinKind.Win, t, () => Recorders(m => new WinRecorder { DataContext = m, VerticalAlignment = VerticalAlignment.Top }, TranscribingTaskbarButton()));
    }

    static StackPanel Quick(Func<QuickModel, Control> view)
    {
        var p = new StackPanel { Spacing = 48, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        p.Children.Add(view(Demo.Quick(answer: false)));
        p.Children.Add(view(Demo.Quick(answer: true)));
        return p;
    }

    [AvaloniaFact]
    public void Mac_quick()
    {
        foreach (var t in Themes) Shot.Take("mac-03-quick-panel", SkinKind.Mac, t, () => Quick(m => new MacQuick { DataContext = m }));
    }

    [AvaloniaFact]
    public void Win_quick()
    {
        foreach (var t in Themes) Shot.Take("win-03-quick-panel", SkinKind.Win, t, () => Quick(m => new WinQuick { DataContext = m }));
    }

    [AvaloniaFact]
    public void Mac_app()
    {
        foreach (var t in Themes) Shot.Take("mac-04-full-app", SkinKind.Mac, t, () => new MacLibrary { DataContext = Demo.Library(), Width = 1280, Height = 800 });
    }

    /// <summary>The sidebar with a Due entry, to eyeball against the Canvas Due screen's.</summary>
    [AvaloniaFact]
    public void Mac_app_due()
    {
        Shot.Take("mac-04-full-app-due", SkinKind.Mac, ThemeVariant.Light, () => new MacLibrary { DataContext = Demo.Library(due: true), Width = 1280, Height = 800 });
    }

    [AvaloniaFact]
    public void Win_app()
    {
        foreach (var t in Themes) Shot.Take("win-04-full-app", SkinKind.Win, t, () => new WinLibrary { DataContext = Demo.Library(), Width = 1280, Height = 800 });
    }

    /// <summary>The sidebar with a Due entry, to eyeball against the Canvas Due screen's.</summary>
    [AvaloniaFact]
    public void Win_app_due()
    {
        Shot.Take("win-04-full-app-due", SkinKind.Win, ThemeVariant.Light, () => new WinLibrary { DataContext = Demo.Library(due: true), Width = 1280, Height = 800 });
    }

    [AvaloniaFact]
    public void Mac_setup()
    {
        foreach (var t in Themes) Shot.Take("mac-05-setup", SkinKind.Mac, t, () => new MacSetup { DataContext = Demo.Setup(SkinKind.Mac), DrawChrome = true });
    }

    [AvaloniaFact]
    public void Win_setup()
    {
        foreach (var t in Themes) Shot.Take("win-05-setup", SkinKind.Win, t, () => new WinSetup { DataContext = Demo.Setup(SkinKind.Win), DrawChrome = true });
    }

    /// <summary>Every page of both setups, the library's and the laptop's, in both looks, light and dark (plus the
    /// welcome a build with no installer role shows): "mac-05-setup-library-password-light.png" and so on.</summary>
    [AvaloniaFact]
    public async Task Setup_steps()
    {
        foreach (var skin in new[] { SkinKind.Mac, SkinKind.Win })
        {
            string look = skin == SkinKind.Mac ? "mac" : "win";
            foreach (var role in new[] { AppRole.Library, AppRole.Laptop })
                foreach (var step in SetupModel.StepsFor(role, skin))
                {
                    var m = await SetupPage(skin, role, step);
                    var size = step switch { SetupStep.Canvas => new Size(1100, 928), SetupStep.Ai => new Size(1100, 808), _ => new Size(850, 608) };
                    string name = $"{look}-05-setup-{(role == AppRole.Library ? "library" : "laptop")}-{step.ToString().ToLowerInvariant()}";
                    foreach (var t in Themes)
                        Shot.Take(name, skin, t, () => skin == SkinKind.Mac ? new MacSetup { DataContext = m, DrawChrome = true } : new WinSetup { DataContext = m, DrawChrome = true },
                            size: size);
                }
            // Canvas once Chrome is connected and the courses are found, and Classes made from those courses.
            foreach (var (step, what) in new[] { (SetupStep.Canvas, "canvas-found"), (SetupStep.Classes, "classes-canvas") })
            {
                var m = await SetupPage(skin, AppRole.Laptop, step, canvasFound: true);
                var size = step == SetupStep.Canvas ? new Size(1100, 928) : new Size(850, 768);
                foreach (var t in Themes)
                    Shot.Take($"{look}-05-setup-laptop-{what}", skin, t, () => skin == SkinKind.Mac ? new MacSetup { DataContext = m, DrawChrome = true } : new WinSetup { DataContext = m, DrawChrome = true },
                        size: size);
            }
            // Setup run again on the library: it keeps its name, password and notes folder.
            var again = await SetupPage(skin, AppRole.Library, SetupStep.Password);
            again.ExistingLibrary = true;
            again.NotesFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Documents", "Lecture notes");
            foreach (var t in Themes)
                Shot.Take($"{look}-05-setup-library-password-existing", skin, t, () => skin == SkinKind.Mac ? new MacSetup { DataContext = again, DrawChrome = true } : new WinSetup { DataContext = again, DrawChrome = true },
                    size: new Size(850, 608));
            var ask = SetupModel.For(skin);
            ask.ChooseLibraryCommand.Execute(null);
            foreach (var t in Themes)
                Shot.Take($"{look}-05-setup-welcome-ask", skin, t, () => skin == SkinKind.Mac ? new MacSetup { DataContext = ask, DrawChrome = true } : new WinSetup { DataContext = ask, DrawChrome = true },
                    size: new Size(850, 608));
        }
    }

    /// <summary>One page of a setup, filled in the way a student would have it by then.</summary>
    static async Task<SetupModel> SetupPage(SkinKind skin, AppRole role, SetupStep step, bool canvasFound = false)
    {
        var m = SetupModel.For(skin, role);
        m.LibraryName = "Ada's library";
        m.Password = "correct-horse";
        m.Address = "http://mac-mini:8787";
        m.Classes.Add(new SetupClass { Name = "CS 101", When = "Tue Thu 10:00–11:15", Dot = Skin.ClassDot(0) });
        m.Classes.Add(new SetupClass { Name = "BIO 110", When = "Tue 11:00–12:30", Dot = Skin.ClassDot(1) });
        m.ModelProgress = 0.62;
        m.ModelDone = "1.9 GB of 3.1 GB";
        m.ModelLeft = "About 4 minutes left";
        m.Addresses.Add(new SetupAddress("At home", "http://mac-mini.local:8787"));
        m.Addresses.Add(new SetupAddress("With Tailscale", "http://mac-mini.example.ts.net:8787"));
        m.NotesFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Documents", "Study Stash");
        if (step > SetupStep.Library)
        {
            m.LibraryOk = true;
            m.LibraryResult = role == AppRole.Laptop ? "Connected to Ada's library." : $"Ada's library is ready on this {m.DeviceWord}.";
        }
        if (step == SetupStep.Ai) m.Ai = AiDemo.Setup();
        if (step == SetupStep.Canvas || canvasFound)
        {
            // Setup's own Canvas: Add to Chrome pressed and waiting for Chrome, or everything done and the courses found.
            m.Canvas = await CanvasShots.SetupStepAsync(canvasFound ? "found" : "waiting");
            m.Canvas.StepLabel = "";
            m.Canvas.ShowFooter = false;
        }
        if (canvasFound && step == SetupStep.Classes)
        {
            m.Classes.Clear();
            m.Classes.Add(new SetupClass { Name = "Chapel", When = "Wed 10:00–10:50", Dot = Skin.ClassDot(4) });
            m.Courses[0].When = "Tue Thu 11:00–12:30";
            m.Courses[1].When = "MWF 9:00–9:50";
            m.Courses[3].When = "Tue Thu 10:00–11:15";
            m.Courses[4].Ticked = false;
        }
        m.Go(step);
        return m;
    }

    /// <summary>The idle dropdown (or flyout) in every colour theme, five to a row: each cell gets its own theme's
    /// tokens, so all ten sit in one picture.</summary>
    static StackPanel ThemeSheet(SkinKind skin, Func<Control> view)
    {
        var sheet = new StackPanel { Spacing = 40, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        StackPanel? row = null;
        foreach (var theme in ColourThemes.All)
        {
            if (row is null || row.Children.Count == 5) sheet.Children.Add(row = Shot.Side());
            row.Spacing = 40;
            var cell = new StackPanel { Spacing = 12, VerticalAlignment = VerticalAlignment.Top };
            cell.Resources.MergedDictionaries.Add(Skin.Build(skin, theme));
            var name = new TextBlock { Text = theme.Name, FontSize = 13, FontWeight = FontWeight.SemiBold };
            name.Bind(TextBlock.ForegroundProperty, name.GetResourceObservable("Fg"));
            cell.Children.Add(name);
            cell.Children.Add(view());
            row.Children.Add(cell);
        }
        return sheet;
    }

    [AvaloniaFact]
    public void Mac_themes()
    {
        foreach (var t in Themes)
            Shot.Take("mac-themes", SkinKind.Mac, t, () => ThemeSheet(SkinKind.Mac, () => new MacPanel { DataContext = Demo.Panel(recording: false) }), size: new Size(2090, 1040));
    }

    [AvaloniaFact]
    public void Win_themes()
    {
        foreach (var t in Themes)
            Shot.Take("win-themes", SkinKind.Win, t, () => ThemeSheet(SkinKind.Win, () => new WinPanel { DataContext = Demo.Panel(recording: false) }), size: new Size(2090, 1120));
    }

    /// <summary>A settings window over a temp home (never a real one), for a section's shot; disposed after.</summary>
    static (Services.SettingsModel Model, Services.AppHost Host, string Home) MakeSettings(string section)
    {
        string home = Path.Combine(Path.GetTempPath(), "studystash-settings-" + Guid.NewGuid().ToString("N"));
        var host = new Services.AppHost(home);
        // The library's own pages read the design's example library (Sam's, on a Mac mini); "Unreachable" shows the
        // Library page when it doesn't answer.
        var library = new FakeLibrarySettings { Down = section == "Unreachable" };
        bool renaming = section == "Rename";
        if (renaming) library.Settings = FakeLibrarySettings.CodeNamed();
        if (section == "Unreachable") section = "Library";
        var model = Services.SettingsModel.Make(host, library: () => library.Call);
        model.Section = renaming ? "Classes" : section;
        // "Use Canvas course names", its preview open.
        if (renaming) model.Lib.ConfirmingCourseNames = true;
        return (model, host, home);
    }

    static void SettingsShots(SkinKind skin, Size size)
    {
        foreach (string section in new[] { "General", "Appearance", "Library", "Classes", "Rename", "Notes", "Folders", "Unreachable" })
        {
            var (model, host, home) = MakeSettings(section);
            try
            {
                foreach (var t in Themes)
                    Shot.Take($"{(skin == SkinKind.Mac ? "mac" : "win")}-settings-{section.ToLowerInvariant()}", skin, t,
                        () => new SettingsView { DataContext = model, DrawChrome = true }, size: size);
            }
            finally
            {
                model.Dispose();
                host.Dispose();
                if (Directory.Exists(home)) Directory.Delete(home, recursive: true);
            }
        }
    }

    [AvaloniaFact]
    public void Mac_settings() => SettingsShots(SkinKind.Mac, new Size(1700, 908));

    [AvaloniaFact]
    public void Win_settings() => SettingsShots(SkinKind.Win, new Size(1700, 988));

    /// <summary>The dropdown's class picker as Shell builds it (drawn in place here: a real one is a popup window).</summary>
    internal static ContextMenu ClassMenu()
    {
        var menu = ClassPicker.Build([("CS 101", 0), ("BIO 110", 1), ("CALC II", 2), ("HIST 210", 3)], null, _ => { });
        menu.VerticalAlignment = VerticalAlignment.Top;
        return menu;
    }

    [AvaloniaFact]
    public void Class_menu()
    {
        foreach (var t in Themes)
        {
            Shot.Take("mac-01-dropdown-menu", SkinKind.Mac, t, () => Shot.Side(
                new MacPanel { DataContext = Demo.Panel(recording: false), VerticalAlignment = VerticalAlignment.Top },
                new Border { Margin = new Thickness(-80, 72, 0, 0), Child = ClassMenu() }));
            Shot.Take("win-01-flyout-menu", SkinKind.Win, t, () => Shot.Side(
                new WinPanel { DataContext = Demo.Panel(recording: false), VerticalAlignment = VerticalAlignment.Top },
                new Border { Margin = new Thickness(-80, 72, 0, 0), Child = ClassMenu() }));
        }
    }

    /// <summary>Notifications: the plain ones the app says (one line, two, with a button, with words too long to fit)
    /// and a stack of Canvas ones, the freshest with its buttons.</summary>
    static StackPanel Toasts()
    {
        var plain = new StackPanel { Spacing = 10, VerticalAlignment = VerticalAlignment.Top };
        plain.Children.Add(new ToastView { Title = "Filed in CS 101", Text = "Recursion and the call stack" , ActionLabel = "Open" });
        plain.Children.Add(new ToastView { Title = "Recording saved", Text = "Study Stash is writing it down; the library files it and writes your notes." });
        plain.Children.Add(new ToastView { Title = "The model isn't downloaded yet", Text = "Download it in Settings → Recording.", ActionLabel = "Settings" });
        plain.Children.Add(new ToastView
        {
            Title = "Your library didn't answer, and this title is far too long to fit on one line",
            Text = "Study Stash keeps the recording here and sends it when the library is back. It tries again every few minutes, so there's nothing to do.",
        });
        var canvas = new StackPanel { Spacing = 10, VerticalAlignment = VerticalAlignment.Top };
        foreach (var toast in CanvasShots.ToastGallery()) canvas.Children.Add(ToastView.For(toast));
        return Shot.Side(plain, canvas);
    }

    [AvaloniaFact]
    public void Mac_toast()
    {
        foreach (var t in Themes) Shot.Take("mac-12-toast", SkinKind.Mac, t, Toasts, size: new Size(1000, 560));
    }

    [AvaloniaFact]
    public void Win_toast()
    {
        foreach (var t in Themes) Shot.Take("win-12-toast", SkinKind.Win, t, Toasts, size: new Size(1000, 620));
    }
}
