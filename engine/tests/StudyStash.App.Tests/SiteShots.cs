using Avalonia;
using CommunityToolkit.Mvvm.Input;
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
using StudyStash.App.Controls.Rich;
using StudyStash.App.ViewModels;
using StudyStash.App.Views;
using StudyStash.Core;
using StudyStash.Core.Ai;
using StudyStash.Core.Rich;

namespace StudyStash.App.Tests;

/// <summary>
/// The website's pictures of the app (site/assets/img), drawn by the app's own views from its demo lectures, light and
/// dark, as a Retina screen draws them (2×): the whole library window, cut to its edge, and pieces of it for the cards.
/// The window stands on the site's own page colour instead of the desktop, so the sidebar's glass takes the page's
/// tint and nothing of the test's wallpaper shows. Only when <c>STUDYSTASH_SITE</c> names the folder to put them in:
/// <code>STUDYSTASH_SITE=/tmp/site-shots dotnet test engine/tests/StudyStash.App.Tests --filter SiteShots</code>
/// then <c>python3 tools/site_pictures.py /tmp/site-shots</c> rounds the window's corners and writes the WebP files.
/// </summary>
public class SiteShots
{
    const double Scale = 2, Margin = 48;
    static readonly Size WindowSize = new(1280, 800);

    static string? Folder => Environment.GetEnvironmentVariable("STUDYSTASH_SITE") is { Length: > 0 } folder ? folder : null;

    static readonly ThemeVariant[] Themes = [ThemeVariant.Light, ThemeVariant.Dark];

    /// <summary>The site's page colours (site.css's --page), light and dark.</summary>
    static IBrush Page(ThemeVariant t) => new SolidColorBrush(Color.Parse(t == ThemeVariant.Dark ? "#16131D" : "#F4F4F1"));

    [AvaloniaFact]
    public void The_sites_pictures()
    {
        if (Folder is null) return;
        Environment.SetEnvironmentVariable("STUDYSTASH_STILL", "1");
        Platform.Motion.Override = true;
        try
        {
            foreach (var t in Themes)
            {
                Home(t);
                Record(t);
                Notes(t);
                Formulas(t);
                Graph(t);
                Heart(t);
                Cycle(t);
                Due(t);
                Ask(t);
                AiApps(t);
            }
        }
        finally
        {
            Platform.Motion.Override = null;
        }
    }

    // --- the pictures ------------------------------------------------------------------------------------------------

    /// <summary>Home, the whole window: the design's own sample (Monday 12 October).</summary>
    static void Home(ThemeVariant t)
    {
        var window = Stage(new MacLibrary { DataContext = Demo.Semester() }, t, WindowSize);
        Save("home", t, window, Bounds(window, window.GetVisualDescendants().OfType<MacLibrary>().Single()));
    }

    /// <summary>A lecture recording, the student's own notes typed under the recording bar.</summary>
    static void Record(ThemeVariant t)
    {
        var window = Stage(new MacLibrary { DataContext = Demo.Semester(Demo.Page.Recording) }, t, WindowSize);
        var pane = Pane(window);
        var column = Column(window);
        Save("record", t, window, new Rect(column.X, pane.Y + 36, column.Width, 664));
    }

    /// <summary>The cardiac cycle lecture's notes from their top: who wrote them, the summary, the key points.</summary>
    static void Notes(ThemeVariant t)
    {
        var window = Stage(new MacLibrary { DataContext = RichShots.CardiacLibrary() }, t, WindowSize);
        var pane = Pane(window);
        var column = Column(window);
        Save("notes", t, window, new Rect(column.X, pane.Y + 36, column.Width, 548));
    }

    /// <summary>The same notes further down: a dose worked through, and mean arterial pressure line by line.</summary>
    static void Formulas(ThemeVariant t)
    {
        var window = Stage(new MacLibrary { DataContext = RichShots.CardiacLibrary() }, t, WindowSize);
        var maths = window.GetVisualDescendants().OfType<MathView>().Where(m => m.Bounds.Height > 30).ToList();
        ScrollTo(window, maths[0], above: 180);
        maths = window.GetVisualDescendants().OfType<MathView>().Where(m => m.Bounds.Height > 30).ToList();
        var column = Column(window);
        var first = Bounds(window, maths[0]);
        var last = Bounds(window, maths[Math.Min(1, maths.Count - 1)]);
        Save("formulas", t, window, new Rect(column.X, first.Y - 46, column.Width, last.Bottom - first.Y + 66));
    }

    /// <summary>A plot in a machine learning lecture's notes, a reading pinned on its curve.</summary>
    static void Graph(ThemeVariant t)
    {
        var model = RichShots.CardiacLibrary();
        foreach (var c in model.Classes) c.Selected = false;
        model.Classes.Insert(0, new ClassItem { Name = "CSCI 4220", Dot = Skin.ClassDot(4), Count = 14, Selected = true });
        model.Note = new NoteModel { ClassName = "CSCI 4220", Dot = Skin.ClassDot(4), When = "Monday 28 September at 9:30 AM, 52 min", Title = "Logistic regression", Markdown = PlotShots.MlLecture }.Titled();
        model.Diagrams = new Lecture();
        var window = Stage(new MacLibrary { DataContext = model }, t, WindowSize);
        var plot = window.GetVisualDescendants().OfType<PlotView>().First();
        ScrollTo(window, plot, above: 160);
        var map = plot.Canvas.Scene!.Map;
        plot.Canvas.Pin(1.4, 0.5);
        Dispatcher.UIThread.RunJobs();
        var column = Column(window);
        var at = Bounds(window, plot);
        Save("graph", t, window, new Rect(column.X, at.Y - 56, column.Width, at.Height + 68));
    }

    /// <summary>The heart cut open and labelled, as a lecture's notes draw it.</summary>
    static void Heart(ThemeVariant t)
    {
        var model = RichShots.CardiacLibrary();
        model.Note = new NoteModel { ClassName = "BIO 110", Dot = Skin.ClassDot(1), When = "Tuesday 30 September at 11:00 AM, 58 min", Title = "The heart, chamber by chamber", Markdown = ReadmeShots.HeartLecture() }.Titled();
        model.Diagrams = new Lecture();
        var window = Stage(new MacLibrary { DataContext = model }, t, WindowSize);
        var figure = window.GetVisualDescendants().OfType<SvgView>().Single();
        ScrollTo(window, figure, above: 160);
        var until = DateTime.UtcNow.AddSeconds(30);
        while (figure.Explorer is { Map: null } && DateTime.UtcNow < until)
        {
            Thread.Sleep(5);
            Dispatcher.UIThread.RunJobs();
        }
        var column = Column(window);
        var at = Bounds(window, figure);
        Save("heart", t, window, new Rect(column.X, at.Y - 56, column.Width, at.Height + 68));
    }

    /// <summary>The five phases of the cardiac cycle, a diagram in the notes.</summary>
    static void Cycle(ThemeVariant t)
    {
        var window = Stage(new MacLibrary { DataContext = RichShots.CardiacLibrary() }, t, WindowSize);
        var diagram = window.GetVisualDescendants().OfType<DiagramView>().First();
        ScrollTo(window, diagram, above: 160);
        var column = Column(window);
        var at = Bounds(window, diagram);
        Save("cycle", t, window, new Rect(column.X, at.Y - 60, column.Width, at.Height + 72));
    }

    /// <summary>Due: the month as a calendar, each class's work on its day in the class's colour.</summary>
    static void Due(ThemeVariant t)
    {
        var window = Stage(new MacLibrary { DataContext = Demo.Semester(Demo.Page.Due) }, t, WindowSize);
        var pane = Pane(window);
        Save("due", t, window, new Rect(pane.X, pane.Y, pane.Width - 14, pane.Height - 10));
    }

    /// <summary>An answer above the ask bar under a lecture's notes, its formula typeset and the minutes it came from.</summary>
    static void Ask(ThemeVariant t)
    {
        var card = AiDemo.Ask();
        card.Engine = "claude";
        card.Turns.Add(new AiTurn("What's a normal MAP?", "Claude")
        {
            Answer = RichDemo.QuickMapAnswer,
            Byline = AiWords.AskByline("Claude", [new AskSource("lec-cardiac", "The cardiac cycle", null, null, 31 * 60 + 5, ""), new AskSource("lec-cardiac", "The cardiac cycle", null, null, 44 * 60 + 20, "")]),
        });
        var column = new StackPanel { Width = 600, Spacing = 16, Children = { new MacAiAnswer { DataContext = card }, new MacAiAskBar { DataContext = card } } };
        var page = new Border { Padding = new Thickness(32, 28), Child = column };
        page.Bind(Border.BackgroundProperty, page.GetResourceObservable("Surface"));
        var window = Stage(page, t, null);
        Save("ask", t, window, Bounds(window, page));
    }

    /// <summary>Settings → AI apps: Claude Desktop and ChatGPT connected, Claude Code with its Connect button.</summary>
    static void AiApps(ThemeVariant t)
    {
        // Each button as it is in the app, where every row's Connect, Reopen and Disconnect do something.
        var access = AiShots.AccessModel();
        foreach (var row in access.Apps)
        {
            row.Connect = new AsyncRelayCommand(() => Task.CompletedTask);
            row.Reopen = new AsyncRelayCommand(() => Task.CompletedTask);
            row.Disconnect = new AsyncRelayCommand(() => Task.CompletedTask);
        }
        var page = new Border { Padding = new Thickness(32, 28), Child = new MacAiAccess { DataContext = access, Width = 620 } };
        page.Bind(Border.BackgroundProperty, page.GetResourceObservable("Surface"));
        var window = Stage(page, t, null);
        var at = Bounds(window, page);
        Save("ai-apps", t, window, new Rect(at.X, at.Y, at.Width - 44, Math.Min(at.Height, 486)));
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

    /// <summary>What <paramref name="content"/> draws, <see cref="Margin"/> in on the site's page colour, at 2×.</summary>
    static Window Stage(Control content, ThemeVariant t, Size? size)
    {
        Skin.UseTheme(ColourThemes.Default);
        ((App)Application.Current!).UseSkin(SkinKind.Mac);
        if (size is { } s)
        {
            content.Width = s.Width;
            content.Height = s.Height;
        }
        content.HorizontalAlignment = HorizontalAlignment.Left;
        content.VerticalAlignment = VerticalAlignment.Top;
        var ground = new Border { Background = Page(t), Padding = new Thickness(Margin), Child = content, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        var window = new Window { Width = 1800, Height = 1400, RequestedThemeVariant = t, Background = Page(t), Content = new Panel { Background = Page(t), Children = { ground } } };
        Glass.SetBlurBackdrop(window, true);
        Look.Apply(window);
        window.SetRenderScaling(Scale);
        window.Show();
        DiagramsReady.Wait(window);
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    static Rect Bounds(Window window, Visual what) =>
        new(what.TranslatePoint(default, window) ?? throw new InvalidOperationException("not on the window"), what.Bounds.Size);

    /// <summary>The note page's column (its tabs' width), with 40 points of the page either side.</summary>
    static Rect Column(Window window) =>
        Bounds(window, window.GetVisualDescendants().OfType<Grid>().First(g => g.Name == "Tabs")).Inflate(new Thickness(40, 0));

    /// <summary>The library's white page (its Pane), where the notes and Due are.</summary>
    static Rect Pane(Window window) => Bounds(window, window.GetVisualDescendants().OfType<Border>().First(b => b.Name == "Pane"));

    /// <summary>Scrolls the notes so <paramref name="what"/> starts <paramref name="above"/> points under their top.</summary>
    static void ScrollTo(Window window, Visual what, double above)
    {
        var scroller = window.GetVisualDescendants().OfType<ScrollViewer>().First(s => s.GetVisualDescendants().OfType<NoteView>().Any());
        scroller.Offset = new Vector(0, Math.Max(0, scroller.Offset.Y + (what.TranslatePoint(default, scroller)?.Y ?? 0) - above));
        Dispatcher.UIThread.RunJobs();
        DiagramsReady.Wait(window);
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>Saves <paramref name="cut"/> (in points) of what the window shows, pixel for pixel at 2×.</summary>
    static void Save(string name, ThemeVariant t, Window window, Rect cut)
    {
        var whole = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("nothing rendered");
        var px = new PixelRect((int)Math.Round(cut.X * Scale), (int)Math.Round(cut.Y * Scale), (int)Math.Round(cut.Width * Scale), (int)Math.Round(cut.Height * Scale));
        px = px.Intersect(new PixelRect(whole.PixelSize));
        var part = new WriteableBitmap(px.Size, whole.Dpi, whole.Format, whole.AlphaFormat);
        using (var from = whole.Lock())
        using (var to = part.Lock())
        {
            var row = new byte[px.Width * 4];
            for (int y = 0; y < px.Height; y++)
            {
                System.Runtime.InteropServices.Marshal.Copy(from.Address + (px.Y + y) * from.RowBytes + px.X * 4, row, 0, row.Length);
                System.Runtime.InteropServices.Marshal.Copy(row, 0, to.Address + y * to.RowBytes, row.Length);
            }
        }
        Directory.CreateDirectory(Folder!);
        using (var file = File.Create(Path.Combine(Folder!, $"{name}-{(t == ThemeVariant.Dark ? "dark" : "light")}.png"))) part.Save(file, PngBitmapEncoderOptions.Default);
        window.Close();
    }
}
