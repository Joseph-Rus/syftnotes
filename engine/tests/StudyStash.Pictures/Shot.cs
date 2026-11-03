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
using StudyStash.Core;

[assembly: AvaloniaTestApplication(typeof(StudyStash.App.Pictures.TestApp))]
// Every test on Avalonia's one UI thread, plain [Fact]s too: a plain test touching Avalonia first can otherwise
// make a worker thread the UI thread, and the pictures drawn after it fail (it happened on Windows CI).
[assembly: AvaloniaTestFramework]


namespace StudyStash.App.Pictures;

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
    /// (or <paramref name="size"/>). The glass blurs what's behind it here, as the design does. At a
    /// <paramref name="scale"/> of 2 it's drawn as a Retina screen draws it, two pixels to a point (a picture twice the
    /// size, cut pixel for pixel, never resampled).
    /// </summary>
    public static Bitmap Take(string name, SkinKind skin, ThemeVariant variant, Func<Control> build, ColourTheme? theme = null, Size? size = null, double scale = 1)
    {
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
        if (scale != 1) window.SetRenderScaling(scale);
        window.Show();
        DiagramsReady.Wait(window);
        var whole = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("nothing rendered");
        Bitmap frame;
        if (scale == 1)
        {
            var rtb = new RenderTargetBitmap(new PixelSize((int)width, (int)height));
            using (var ctx = rtb.CreateDrawingContext())
                ctx.DrawImage(whole, new Rect(0, 0, width, height), new Rect(0, 0, width, height));
            frame = rtb;
        }
        else frame = Cut(whole, new PixelSize((int)Math.Round(width * scale), (int)Math.Round(height * scale)));
        using (var file = File.Create(Path.Combine(Dir, $"{name}-{(variant == ThemeVariant.Dark ? "dark" : "light")}.png"))) frame.Save(file, PngBitmapEncoderOptions.Default);
        window.Close();
        return frame;
    }

    /// <summary>The top left <paramref name="size"/> of <paramref name="whole"/>, pixel for pixel, at its own scale.</summary>
    static WriteableBitmap Cut(WriteableBitmap whole, PixelSize size)
    {
        var cut = new WriteableBitmap(size, whole.Dpi, whole.Format, whole.AlphaFormat);
        using var from = whole.Lock();
        using var to = cut.Lock();
        var row = new byte[size.Width * 4];
        for (int y = 0; y < size.Height; y++)
        {
            System.Runtime.InteropServices.Marshal.Copy(from.Address + y * from.RowBytes, row, 0, row.Length);
            System.Runtime.InteropServices.Marshal.Copy(row, 0, to.Address + y * to.RowBytes, row.Length);
        }
        return cut;
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
