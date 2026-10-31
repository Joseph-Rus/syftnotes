using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StudyStash.App.Controls;
using StudyStash.App.Platform;
using StudyStash.App.Controls.Rich;
using StudyStash.App.ViewModels;
using StudyStash.App.Views;
using StudyStash.Core;
using StudyStash.Core.Rich;

namespace StudyStash.App.Tests;

/// <summary>
/// The README's pictures, drawn by the app's own views from demo lectures: the Mac's library window, 1280 × 800, with
/// 28 points of desktop round it, light and dark (the README shows whichever the reader's GitHub is in), and the menu
/// bar's dropdown and the quick panel on the light desktop. Only when
/// <c>STUDYSTASH_README</c> names the folder to put them in, so an ordinary test run draws none:
/// <code>STUDYSTASH_README=$PWD/docs/images dotnet test engine/tests/StudyStash.App.Tests --filter ReadmeShots</code>
/// </summary>
public class ReadmeShots
{
    const double Wide = 1280, Tall = 800, Desk = 28;

    static string? Folder => Environment.GetEnvironmentVariable("STUDYSTASH_README") is { Length: > 0 } folder ? folder : null;

    static readonly ThemeVariant[] Themes = [ThemeVariant.Light, ThemeVariant.Dark];

    [AvaloniaFact]
    public void The_readmes_pictures()
    {
        if (Folder is null) return;
        Environment.SetEnvironmentVariable("STUDYSTASH_STILL", "1");
        Platform.Motion.Override = true;
        try
        {
            foreach (var t in Themes)
            {
                Heart(t);
                Plot(t);
                Formulas(t);
                Home(t);
            }
            MenuBar();
            QuickPanel();
        }
        finally
        {
            Platform.Motion.Override = null;
        }
    }

    // --- the labelled heart ------------------------------------------------------------------------------------------

    /// <summary>The parts the lecturer named, each with the line it was named in: what a part's card says.</summary>
    static readonly (string Id, string Said)[] HeartParts =
    [
        ("superior-vena-cava", "Brings blood back from the head and arms."),
        ("right-atrium", "Fills with blood that has given up its oxygen."),
        ("tricuspid-valve", "Three flaps between the right atrium and ventricle."),
        ("right-ventricle", "Pumps to the lungs, at low pressure."),
        ("pulmonary-trunk", "Carries blood out to the lungs."),
        ("pulmonary-veins", "Bring oxygen-rich blood back from the lungs."),
        ("left-atrium", "Receives blood from the lungs."),
        ("mitral-valve", "Two flaps between the left atrium and ventricle."),
        ("left-ventricle", "Its wall is about three times as thick as the right's: it pumps to the whole body."),
        ("aortic-valve", "Opens when the left ventricle contracts."),
        ("aorta", "The body's largest artery."),
        ("interventricular-septum", "The wall between the two ventricles."),
    ];

    /// <summary>A lecture on the heart with the figure its notes get: the parts library's heart, cut open, composed
    /// and labelled by the same code that draws one for a real lecture (the scene is what the composer answers with).</summary>
    internal static string HeartLecture()
    {
        var library = PartsLibrary.Default;
        var heart = library.Get("heart-section")!;
        var regions = heart.Regions.ToDictionary(r => r.Id, r => r.Name);
        const string caption = "Blood comes in on the right side, goes out to the lungs, comes back on the left and leaves through the aorta.";
        var plan = new IllustrationPlan("The heart, cut open", "The chambers", 14 * 60 + 20, caption,
            "A human heart cut open from the front, all four chambers.", "Atria above, ventricles below.",
            [.. HeartParts.Select(p => new Illustration.Planned(p.Id, regions[p.Id], p.Said))]);
        string scene = "{\"compose\": true, \"parts\": [{\"as\": \"heart\", \"use\": \"heart-section\", \"x\": 500, \"y\": 500, \"w\": 600}], \"labels\": ["
            + string.Join(", ", HeartParts.Select(p => $"{{\"part\": \"{p.Id}\", \"to\": \"heart.{p.Id}\"}}")) + "]}";
        var read = PartsScene.Read(scene, new Dictionary<string, LibraryPart> { [heart.Id] = heart }, plan.Parts, [])!;
        var drawn = PartsScene.Draw(read, plan, library)!;
        return string.Join("\n\n",
            "## Summary",
            "Blood passes through the heart twice on every lap of the body. The right side takes in blood that has given up its oxygen and sends it to the lungs; the left side takes it back and pumps it out to everything else.",
            "## The chambers",
            "- The two atria receive blood; the two ventricles pump it out.\n- A valve between each atrium and its ventricle keeps blood moving one way.",
            $"**{plan.Title}**",
            "```svg\n<!-- Syft Notes diagram, from 14:20 -->\n" + drawn.Svg + "\n```",
            $"*{caption} Parts: {string.Join("; ", drawn.Credits)}.* (from 14:20)",
            "## Questions to review",
            "1. Why is the left ventricle's wall thicker than the right's?");
    }

    static void Heart(ThemeVariant t)
    {
        var model = RichShots.CardiacLibrary();
        model.Note = new NoteModel { ClassName = "BIO 110", Dot = Skin.ClassDot(1), When = "Tuesday 30 September at 11:00 AM, 58 min", Title = "The heart, chamber by chamber", Markdown = HeartLecture() }.Titled();
        model.Diagrams = new Lecture();
        var window = Stage(new MacLibrary { DataContext = model }, t);
        var figure = window.GetVisualDescendants().OfType<SvgView>().Single();
        ScrollTo(window, figure, above: 64);
        // Its parts found, so every label is drawn in its place.
        var until = DateTime.UtcNow.AddSeconds(30);
        while (figure.Explorer is { Map: null } && DateTime.UtcNow < until)
        {
            Thread.Sleep(5);
            Dispatcher.UIThread.RunJobs();
        }
        Save("notes-heart", t, window);
    }

    // --- a plot, and formulas beside a diagram -----------------------------------------------------------------------

    static void Plot(ThemeVariant t)
    {
        var model = RichShots.CardiacLibrary();
        foreach (var c in model.Classes) c.Selected = false;
        model.Classes.Insert(0, new ClassItem { Name = "CSCI 4220", Dot = Skin.ClassDot(4), Count = 14, Selected = true });
        model.Note = new NoteModel { ClassName = "CSCI 4220", Dot = Skin.ClassDot(4), When = "Monday 28 September at 9:30 AM, 52 min", Title = "Logistic regression", Markdown = PlotShots.MlLecture }.Titled();
        model.Diagrams = new Lecture();
        var window = Stage(new MacLibrary { DataContext = model }, t);
        var plot = window.GetVisualDescendants().OfType<PlotView>().First();
        ScrollTo(window, plot, above: 60);
        // A reading pinned on the curve, and the pointer reading another.
        var map = plot.Canvas.Scene!.Map;
        plot.Canvas.Pin(1.4, 0.5);
        window.MouseMove(plot.Canvas.TranslatePoint(new Point(map.ScreenX(-2.2), map.ScreenY(0.3)), window)!.Value);
        Dispatcher.UIThread.RunJobs();
        Save("notes-plot", t, window);
    }

    static void Formulas(ThemeVariant t)
    {
        var window = Stage(new MacLibrary { DataContext = RichShots.CardiacLibrary() }, t);
        RichShots.ScrollToDiagrams(window);
        Save("notes-formulas", t, window);
    }

    static void Home(ThemeVariant t) => Save("home", t, Stage(new MacLibrary { DataContext = Demo.Overview() }, t));

    // --- the menu bar's dropdown and the quick panel, light only, as the README shows them --------------------------

    /// <summary>The menu bar's dropdown, ready to record and recording, side by side, each under the menu bar's ink s
    /// (its top-right cell red while recording, the time beside it) as the Mac shows it.</summary>
    static void MenuBar()
    {
        var bar = new Canvas { Width = 764, Height = 24, Background = new SolidColorBrush(Color.Parse("#F6F5F2"), 0.86), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        // The status item over each dropdown's right-hand side, where the dropdown hangs from it.
        Image Mark(bool recording) => new() { Source = new Bitmap(new MemoryStream(TrayMark.Png(18, Brushes.Black, recording))), Width = 18, Height = 18 };
        var idle = Mark(false);
        Canvas.SetLeft(idle, 34 + 320 - 46);
        Canvas.SetTop(idle, 3);
        var live = Mark(true);
        Canvas.SetLeft(live, 410 + 320 - 86);
        Canvas.SetTop(live, 3);
        var time = new TextBlock { Text = "24:18", FontSize = 13, FontWeight = FontWeight.Medium, Foreground = Brushes.Black };
        Canvas.SetLeft(time, 410 + 320 - 62);
        Canvas.SetTop(time, 4);
        bar.Children.Add(idle);
        bar.Children.Add(live);
        bar.Children.Add(time);
        Surface("menu-bar", new Size(764, 446), Shot.Side(
            new MacPanel { DataContext = Demo.Panel(recording: false), VerticalAlignment = VerticalAlignment.Top },
            new MacPanel { DataContext = Demo.Panel(recording: true), VerticalAlignment = VerticalAlignment.Top }), new Thickness(34, 32, 34, 34), bar);
    }

    /// <summary>The quick panel with a search's results.</summary>
    static void QuickPanel() => Surface("quick-panel", new Size(750, 540), new MacQuick { DataContext = Demo.Quick(answer: false) });

    /// <summary>A surface 34 points in (or <paramref name="at"/>) on the light desktop, under <paramref name="over"/>
    /// if there is one, cut to <paramref name="size"/>.</summary>
    static void Surface(string name, Size size, Control surface, Thickness? at = null, Control? over = null)
    {
        Skin.UseTheme(ColourThemes.Default);
        ((App)Application.Current!).UseSkin(SkinKind.Mac);
        surface.HorizontalAlignment = HorizontalAlignment.Left;
        surface.VerticalAlignment = VerticalAlignment.Top;
        var desk = new WallpaperPicture(false) { Width = size.Width, Height = size.Height, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        var on = new Border { Padding = at ?? new Thickness(34), Child = surface, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        var root = new Panel { Children = { desk } };
        if (over is not null) root.Children.Add(over);
        root.Children.Add(on);
        var window = new Window { Width = size.Width + 200, Height = size.Height + 200, RequestedThemeVariant = ThemeVariant.Light, Content = root };
        Glass.SetBlurBackdrop(window, true);
        Look.Apply(window);
        window.Show();
        DiagramsReady.Wait(window);
        Dispatcher.UIThread.RunJobs();
        var whole = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("nothing rendered");
        var frame = new RenderTargetBitmap(new PixelSize((int)size.Width, (int)size.Height));
        using (var ctx = frame.CreateDrawingContext()) ctx.DrawImage(whole, new Rect(size), new Rect(size));
        Directory.CreateDirectory(Folder!);
        using (var file = File.Create(Path.Combine(Folder!, $"{name}.png"))) frame.Save(file, PngBitmapEncoderOptions.Default);
        window.Close();
    }

    // --- the stage ---------------------------------------------------------------------------------------------------

    /// <summary>A lecture that can be asked about and has a transcript, so a figure's own buttons all show.</summary>
    sealed class Lecture : IDiagramHost
    {
        public bool CanAsk => true;
        public void Ask(string question) { }
        public IReadOnlyList<Spoken> Transcript { get; } = [new(860, 872, "Here is the heart cut open from the front.")];
        public void ShowTranscript(double seconds) { }
        public bool CanPlay => true;
        public void Play(double seconds) { }
    }

    /// <summary>The library window on the Mac's desktop, shown and laid out, ready to be scrolled and pointed at.</summary>
    static Window Stage(Control library, ThemeVariant t)
    {
        Skin.UseTheme(ColourThemes.Default);
        ((App)Application.Current!).UseSkin(SkinKind.Mac);
        library.Width = Wide;
        library.Height = Tall;
        double width = Wide + 2 * Desk, height = Tall + 2 * Desk;
        var desk = new WallpaperPicture(t == ThemeVariant.Dark) { Width = width, Height = height, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        var on = new Border { Padding = new Thickness(Desk), Child = library, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        // Room to spare, cut to the picture's size when it's saved, so nothing is squeezed.
        var window = new Window { Width = width + 200, Height = height + 200, RequestedThemeVariant = t, Content = new Panel { Children = { desk, on } } };
        Glass.SetBlurBackdrop(window, true);
        Look.Apply(window);
        window.Show();
        DiagramsReady.Wait(window);
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    /// <summary>Scrolls the notes so <paramref name="what"/> starts <paramref name="above"/> points under their top.</summary>
    static void ScrollTo(Window window, Visual what, double above)
    {
        var scroller = window.GetVisualDescendants().OfType<ScrollViewer>().First(s => s.GetVisualDescendants().OfType<NoteView>().Any());
        scroller.Offset = new Vector(0, Math.Max(0, scroller.Offset.Y + (what.TranslatePoint(default, scroller)?.Y ?? 0) - above));
        Dispatcher.UIThread.RunJobs();
    }

    static void Save(string name, ThemeVariant t, Window window)
    {
        var size = new Size(Wide + 2 * Desk, Tall + 2 * Desk);
        var whole = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("nothing rendered");
        var frame = new RenderTargetBitmap(new PixelSize((int)size.Width, (int)size.Height));
        using (var ctx = frame.CreateDrawingContext()) ctx.DrawImage(whole, new Rect(size), new Rect(size));
        Directory.CreateDirectory(Folder!);
        using (var file = File.Create(Path.Combine(Folder!, $"{name}-{(t == ThemeVariant.Dark ? "dark" : "light")}.png"))) frame.Save(file, PngBitmapEncoderOptions.Default);
        window.Close();
    }
}
