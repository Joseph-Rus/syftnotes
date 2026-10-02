using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StudyStash.App.Controls;
using StudyStash.App.Controls.Rich;
using StudyStash.App.Windows;
using StudyStash.Core;

namespace StudyStash.App.Tests;

/// <summary>An illustration a student explores: the pointer lights a part and a click pins it with its name, its line
/// and its questions; the labels turn off; Test yourself hides them and scores what's named; it opens larger with the
/// same; on paper, and for a plain drawing, nothing changes.</summary>
public sealed class IllustrationViewTests : IDisposable
{
    public IllustrationViewTests()
    {
        Platform.Motion.Override = true;
        DiagramExplorer.HintWasSeen = () => false;
        DiagramExplorer.RememberHint = () => { };
    }

    public void Dispose() => Platform.Motion.Override = null;

    /// <summary>A small drone as the illustrator draws one, its parts named.</summary>
    public const string Drone = """
        <!-- Study Stash diagram, from 00:20 -->
        <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 400 300">
          <title>A small drone</title>
          <g id="art">
            <g id="frame" transform="translate(200 150)"><title>Frame</title><desc>X-shaped carbon fibre, 450 mm</desc><path d="M-80 -60 L80 60 M80 -60 L-80 60" stroke="#3B4045" stroke-width="8"/></g>
            <g id="motor"><title>Motor</title><desc>Brushless outrunner, spins clockwise</desc><circle cx="120" cy="90" r="18" fill="#AEB6BE" stroke="#6B747D"/></g>
            <g id="battery"><title>Battery</title><desc>4S LiPo strapped on top</desc><rect x="180" y="135" width="40" height="30" rx="4" fill="#4A9AD1" stroke="#174C75"/></g>
          </g>
          <g id="labels" font-size="14">
            <g id="label-frame"><polyline points="150,113 104,40" fill="none" stroke="#6E6E73"/><text x="100" y="44" text-anchor="end" fill="#1D1D1F">Frame</text></g>
            <g id="label-motor"><polyline points="104,82 104,74" fill="none" stroke="#6E6E73"/><text x="100" y="78" text-anchor="end" fill="#1D1D1F">Motor</text></g>
            <g id="label-battery"><polyline points="220,150 296,150" fill="none" stroke="#6E6E73"/><text x="300" y="155" fill="#1D1D1F">Battery</text></g>
          </g>
        </svg>
        """;

    sealed class Page : IDiagramHost
    {
        public List<string> Asked { get; } = [];
        public bool CanAsk => true;
        public void Ask(string question) => Asked.Add(question);
        public IReadOnlyList<Spoken> Transcript { get; init; } = [];
        public void ShowTranscript(double seconds) { }
        public bool CanPlay => false;
        public void Play(double seconds) { }
    }

    static Window Show(Control content, double width = 620)
    {
        Skin.UseTheme(ColourThemes.Default);
        ((App)Application.Current!).UseSkin(SkinKind.Mac);
        content.Width = width;
        content.HorizontalAlignment = HorizontalAlignment.Left;
        content.VerticalAlignment = VerticalAlignment.Top;
        var window = new Window { Width = width + 40, Height = 1200, RequestedThemeVariant = ThemeVariant.Light, Content = content };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    /// <summary>Waits for the map of which part is where (made away from the window).</summary>
    static void Mapped(SvgView view)
    {
        var until = DateTime.UtcNow.AddSeconds(30);
        while (view.Explorer!.Map is null)
        {
            Assert.True(DateTime.UtcNow < until, "the parts' map took over 30 seconds");
            Thread.Sleep(5);
            Dispatcher.UIThread.RunJobs();
        }
    }

    static SvgCanvas Picture(SvgView view) => view.GetVisualDescendants().OfType<SvgCanvas>().Single();

    /// <summary>Where a point of the drawing (its own units) is in the window.</summary>
    static Point At(SvgView view, double x, double y)
    {
        var canvas = Picture(view);
        double k = canvas.Bounds.Width / 400;
        return canvas.TranslatePoint(new Point(x * k, y * k), TopLevel.GetTopLevel(view)!)!.Value;
    }

    static void Click(TopLevel top, Point p)
    {
        top.MouseMove(p);
        top.MouseDown(p, MouseButton.Left);
        top.MouseUp(p, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }

    static void Key(TopLevel top, Key key)
    {
        top.KeyPress(key, RawInputModifiers.None, PhysicalKey.None, null);
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public void A_part_lights_under_the_pointer_and_a_click_pins_it_with_its_name_its_line_and_its_questions()
    {
        var page = new Page();
        var note = new NoteView { Markdown = "## The drone\n\n```svg\n" + Drone + "\n```" };
        DiagramHost.SetHost(note, page);
        var window = Show(note);
        var view = window.GetVisualDescendants().OfType<SvgView>().Single();
        Assert.NotNull(view.Explorer);
        Mapped(view);
        var canvas = Picture(view);
        Assert.Null(canvas.Look); // untouched, it's the still picture
        Assert.Equal(["frame", "motor", "battery"], view.Explorer!.Parts.Select(p => p.Id)); // the order its labels are read

        window.MouseMove(At(view, 120, 90));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("motor", canvas.Look!.Lit);
        Assert.Equal("Motor: Brushless outrunner, spins clockwise", view.Chrome!.Announcer.Text);
        // A thin part is found near its line, and a click on a label is a click on its part.
        window.MouseMove(At(view, 255, 190));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("frame", canvas.Look!.Lit);

        Click(window, At(view, 320, 150));
        Assert.Equal("battery", view.Explorer.Pinned);
        Assert.True(view.Chrome.Actions.IsVisible);
        var card = view.Chrome.Actions.GetLogicalDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
        Assert.Contains("Battery", card);
        Assert.Contains("4S LiPo strapped on top", card);
        var explain = view.Chrome.Actions.GetLogicalDescendants().OfType<Button>().First(b => AutomationName(b) == "Explain this");
        explain.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("Explain the “Battery” in the illustration “A small drone”, as the lecture taught it: what it is, where it sits and what it does. The figure says: 4S LiPo strapped on top",
            Assert.Single(page.Asked));

        // L turns the labels off (a part's name still shows when it's lit), and on again.
        Key(window, Avalonia.Input.Key.Escape);
        Key(window, Avalonia.Input.Key.L);
        Assert.False(view.Explorer.Labels);
        Assert.False(canvas.Look!.Labels);
        Key(window, Avalonia.Input.Key.L);
        Assert.True(view.Explorer.Labels);

        // Test yourself: every label hidden; a click on a part shows its name and asks if it was known.
        Key(window, Avalonia.Input.Key.H);
        Assert.True(canvas.Look!.Recall);
        Assert.Equal(["battery", "frame", "motor"], canvas.Look.Blanks.Order());
        Click(window, At(view, 120, 90));
        Assert.Equal("motor", view.Explorer.Asking);
        Assert.Contains("motor", canvas.Look!.Shown);
        Key(window, Avalonia.Input.Key.Y);
        Assert.Equal(1, view.Explorer.Knew);
        Key(window, Avalonia.Input.Key.Escape);
        Assert.Equal(DiagramMode.Explore, view.Explorer.Mode);

        // The tour walks the parts in the order their labels are read.
        Key(window, Avalonia.Input.Key.S);
        Assert.Equal("frame", view.Explorer.CurrentStep);
        Key(window, Avalonia.Input.Key.Right);
        Assert.Equal("motor", view.Explorer.CurrentStep);
        window.Close();
    }

    static string? AutomationName(Control c) => Avalonia.Automation.AutomationProperties.GetName(c);

    [AvaloniaFact]
    public void On_paper_and_as_a_plain_drawing_nothing_changes_and_larger_it_explores_the_same()
    {
        var paper = new NoteView { Markdown = "```svg\n" + Drone + "\n```", PageHeight = 900 };
        var printed = Show(paper);
        var still = printed.GetVisualDescendants().OfType<SvgView>().Single();
        Assert.Null(still.Explorer);
        Assert.Empty(still.GetVisualDescendants().OfType<Button>());
        printed.Close();

        var plain = new NoteView { Markdown = "```svg\n" + Core.Summarize.SvgExample + "\n```" };
        var shown = Show(plain);
        var drawing = shown.GetVisualDescendants().OfType<SvgView>().Single();
        Assert.Null(drawing.Explorer);
        Assert.NotNull(drawing.Cursor); // a click on it opens it larger, as before
        shown.Close();

        var larger = DiagramWindow.Content(new OpenDiagramEventArgs(new Border()) { Title = "A small drone", Svg = Drone });
        var window = Show(new Border { Height = 700, Child = larger }, 900);
        var view = window.GetVisualDescendants().OfType<SvgView>().Single();
        Assert.True(view.Explorer!.Windowed);
        Assert.Contains(window.GetVisualDescendants().OfType<Button>(), b => AutomationName(b) == "Tour the parts");
        Mapped(view);
        window.MouseMove(At(view, 120, 90));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("motor", Picture(view).Look!.Lit);
        // Its strip names the part the pointer is on, with its line.
        Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Brushless outrunner, spins clockwise" && t.IsEffectivelyVisible);
        window.Close();
    }
}
