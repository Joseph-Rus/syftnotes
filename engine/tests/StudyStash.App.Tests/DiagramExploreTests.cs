using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StudyStash.App.Controls;
using StudyStash.App.Controls.Rich;
using StudyStash.App.ViewModels;
using StudyStash.App.Windows;
using StudyStash.Core;
using StudyStash.Core.Rich;

namespace StudyStash.App.Tests;

/// <summary>A diagram a student explores: the pointer lights a box's arrows and a click pins it, the keyboard walks
/// it, groups fold and open, it steps through and tests recall, a box is asked about or found in the lecture (from the
/// note or the larger window), and on paper it's only ever the still picture.</summary>
public sealed class DiagramExploreTests : IDisposable
{
    public DiagramExploreTests()
    {
        Platform.Motion.Override = true; // no glides: each step lands at once
        DiagramExplorer.HintWasSeen = () => false;
        DiagramExplorer.RememberHint = () => { };
    }

    public void Dispose() => Platform.Motion.Override = null;

    /// <summary>A lecture page that hears what its diagrams ask for.</summary>
    sealed class Page : IDiagramHost
    {
        public List<string> Asked { get; } = [];
        public List<double> Shown { get; } = [];
        public bool CanAsk => true;
        public void Ask(string question) => Asked.Add(question);
        public IReadOnlyList<Spoken> Transcript { get; init; } = [];
        public void ShowTranscript(double seconds) => Shown.Add(seconds);
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
        var window = new Window { Width = width + 40, Height = 2400, RequestedThemeVariant = ThemeVariant.Light, Content = content };
        window.Show();
        DiagramsReady.Wait(window);
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    static DiagramCanvas Picture(DiagramView view) => view.GetVisualDescendants().OfType<DiagramCanvas>().Single();

    /// <summary>Where box <paramref name="id"/>'s middle is in the window.</summary>
    static Point At(DiagramView view, string id)
    {
        var n = view.Scene!.Nodes.Single(x => x.Id == id);
        return Picture(view).TranslatePoint(new Point(n.Box.Center.X * view.Scale, n.Box.Center.Y * view.Scale), TopLevel.GetTopLevel(view)!)!.Value;
    }

    static void Click(TopLevel top, Point p)
    {
        top.MouseMove(p);
        top.MouseDown(p, MouseButton.Left);
        top.MouseUp(p, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        DiagramsReady.Wait(top);
    }

    static void Key(TopLevel top, Key key, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        top.KeyPress(key, modifiers, PhysicalKey.None, null);
        Dispatcher.UIThread.RunJobs();
        DiagramsReady.Wait(top);
    }

    static byte[] Pixels(Visual v)
    {
        var size = new PixelSize((int)Math.Ceiling(v.Bounds.Width), (int)Math.Ceiling(v.Bounds.Height));
        using var bmp = new RenderTargetBitmap(size);
        bmp.Render(v);
        var buffer = new byte[size.Width * size.Height * 4];
        var pinned = System.Runtime.InteropServices.GCHandle.Alloc(buffer, System.Runtime.InteropServices.GCHandleType.Pinned);
        try
        {
            bmp.CopyPixels(new PixelRect(size), pinned.AddrOfPinnedObject(), buffer.Length, size.Width * 4);
        }
        finally
        {
            pinned.Free();
        }
        return buffer;
    }

    [AvaloniaFact]
    public void On_paper_a_diagram_is_only_ever_the_still_picture()
    {
        var paper = new NoteView { Markdown = "```mermaid\n" + RichDemo.PainReassess + "\n```", PageHeight = 900 };
        var window = Show(paper);
        var view = window.GetVisualDescendants().OfType<DiagramView>().Single();
        Assert.Null(view.Explorer);
        Assert.Null(view.Chrome);
        Assert.False(view.Focusable);
        Assert.Empty(view.GetVisualDescendants().OfType<Button>());
        var before = Pixels(view);
        window.MouseMove(At(view, "B"));
        window.MouseDown(At(view, "B"), MouseButton.Left);
        window.MouseUp(At(view, "B"), MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        Assert.Null(Picture(view).Look);
        Assert.Equal(before, Pixels(view));

        // A note's own diagram, explored and left as it was, draws the same picture as before it was touched.
        var note = new NoteView { Markdown = "```mermaid\n" + RichDemo.PainReassess + "\n```" };
        var screen = Show(note);
        var live = screen.GetVisualDescendants().OfType<DiagramView>().Single();
        var still = Pixels(Picture(live));
        screen.MouseMove(At(live, "B"));
        Dispatcher.UIThread.RunJobs();
        Assert.NotEqual(still, Pixels(Picture(live)));
        screen.MouseMove(new Point(2, 2390));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(still, Pixels(Picture(live)));
        window.Close();
        screen.Close();
    }

    [AvaloniaFact]
    public void The_pointer_lights_a_box_and_its_arrows_a_click_pins_it_and_the_keyboard_walks_the_arrows()
    {
        var view = new DiagramView { Chart = Flowchart.Parse(RichDemo.PainReassess) };
        var window = Show(new StackPanel { Children = { view } });
        var picture = Picture(view);
        Assert.Null(picture.Look);

        window.MouseMove(At(view, "B"));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(["A", "B", "C", "D"], picture.Look!.Lit!.Order());
        Assert.Equal(1, picture.Look.Dim);
        Assert.True(view.Chrome!.Hint.IsVisible); // the first time: how it works

        Click(window, At(view, "B"));
        Assert.Equal("B", view.Explorer!.Pinned);
        Assert.Equal("B", picture.Look!.Picked);
        Assert.Equal([0, 1, 2], picture.Look.Accented!.Order());
        Assert.True(view.Chrome.Actions.IsVisible);
        Assert.False(view.Chrome.Hint.IsVisible); // once is enough
        // Moving off it leaves it pinned.
        window.MouseMove(At(view, "E"));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("B", picture.Look!.Picked);

        // Esc lets go, one thing at a time: the actions, then the pin.
        Key(window, Avalonia.Input.Key.Escape);
        Assert.False(view.Chrome.Actions.IsVisible);
        Key(window, Avalonia.Input.Key.Escape);
        Assert.Null(view.Explorer.Pinned);

        // Arrow keys walk along the arrows, the way they point: down from the question is one of its branches.
        Key(window, Avalonia.Input.Key.Up);
        Assert.Equal("A", view.Explorer.Ring);
        Key(window, Avalonia.Input.Key.Down);
        Assert.Equal("B", view.Explorer.Ring);
        Assert.Contains("Score above 4?", view.Chrome.Announcer.Text);
        Key(window, Avalonia.Input.Key.Down);
        Assert.Contains(view.Explorer.Ring, new[] { "C", "D" });
        Assert.Equal(view.Explorer.Ring, picture.Look!.Ring);
        // Enter on a box pins it with its actions; no box and Enter opens it larger instead (as a click on its paper).
        Key(window, Avalonia.Input.Key.Enter);
        Assert.Equal(view.Explorer.Ring, view.Explorer.Pinned);
        window.Close();
    }

    [AvaloniaFact]
    public void Zooming_is_ctrl_or_cmd_and_the_wheel_a_plain_wheel_scrolls_the_note_and_zero_fits_again()
    {
        var view = new DiagramView { Chart = Flowchart.Parse(RichDemo.PainReassess) };
        var window = Show(new StackPanel { Children = { view } });
        var p = At(view, "B");
        window.MouseWheel(p, new Vector(0, -1));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(1, view.Explorer!.Zoomer.Zoom);
        window.MouseWheel(p, new Vector(0, 2), RawInputModifiers.Meta);
        Dispatcher.UIThread.RunJobs();
        Assert.True(view.Explorer.Zoomer.Zoom > 1.3);
        // The box under the pointer stays under it.
        Assert.True(Math.Abs(At(view, "B").X - p.X) < 1.5 && Math.Abs(At(view, "B").Y - p.Y) < 1.5, $"{At(view, "B")} vs {p}");
        // In a note it never zooms out past fitted, nor in past four times.
        window.MouseWheel(p, new Vector(0, 40), RawInputModifiers.Control);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(4, view.Explorer.Zoomer.Zoom, 3);
        Key(window, Avalonia.Input.Key.D0);
        Assert.Equal(1, view.Explorer.Zoomer.Zoom, 3);
        window.Close();
    }

    const string Groups = """
        flowchart LR
          subgraph a [Assessment]
            A1[Collect subjective data] --> A2[Collect objective data] --> A3[Validate the cues]
          end
          subgraph d [Diagnosis]
            D1[Cluster the cues] --> D2[Name the problem]
          end
          subgraph p [Planning]
            P1[Set goals] --> P2[Choose interventions] --> P3[Write the care plan]
          end
          subgraph i [Implementation]
            I1[Carry out the plan] --> I2[Document care]
          end
          A3 --> D1
          D2 --> P1
          P3 --> I1
          I2 --> E{Outcomes met?}
          E -->|yes| R([Resolve])
          E -.->|no| A1
        """;

    [AvaloniaFact]
    public void A_big_chart_in_groups_opens_as_its_overview_a_click_opens_a_group_and_the_window_opens_it_the_same_way()
    {
        var note = new NoteView { Markdown = "```mermaid\n" + Groups + "\n```" };
        var window = Show(note);
        var view = window.GetVisualDescendants().OfType<DiagramView>().Single();
        Assert.Equal(["E", "R", "a", "d", "i", "p"], view.Scene!.Nodes.Select(n => n.Id).Order(StringComparer.Ordinal));
        Assert.Equal(["a", "d", "i", "p"], Picture(view).Look!.Folded!.Order());

        Click(window, At(view, "p"));
        Assert.Contains(view.Scene!.Nodes, n => n.Id == "P2");
        Assert.DoesNotContain(view.Scene.Nodes, n => n.Id == "p");
        Assert.Contains(view.Scene.Groups, g => g.Id == "p");
        Assert.Equal(["a", "d", "i"], view.Explorer!.Folded.Order());

        // Clicking the open group's title folds it again; O opens them all, and again folds them all.
        var title = view.Scene.Groups.Single(g => g.Id == "p").TitleBox.Center;
        Click(window, Picture(view).TranslatePoint(new Point(title.X * view.Scale, title.Y * view.Scale), window)!.Value);
        Assert.Contains(view.Scene!.Nodes, n => n.Id == "p");
        Key(window, Avalonia.Input.Key.O);
        Assert.Equal(12, view.Scene!.Nodes.Count);
        Assert.False(view.Explorer.AnyFolded);
        Key(window, Avalonia.Input.Key.O);
        Assert.True(view.Explorer.AnyFolded);

        // Opened larger with one group open, the window shows it the same way.
        Click(window, At(view, "d"));
        view.OpenLarger();
        var larger = DiagramWindow.Current!;
        DiagramsReady.Wait(larger);
        var big = larger.GetVisualDescendants().OfType<DiagramView>().Single();
        Assert.Equal(["a", "i", "p"], big.Explorer!.Folded.Order());
        Assert.Contains(big.Scene!.Nodes, n => n.Id == "D1");
        larger.Close();
        window.Close();
    }

    [AvaloniaFact]
    public void Steps_walk_it_in_reading_order_with_the_keyboard_and_say_where_they_are()
    {
        var view = new DiagramView { Chart = Flowchart.Parse(RichDemo.PainReassess) };
        var window = Show(new StackPanel { Children = { view } });
        view.Focus();
        Key(window, Avalonia.Input.Key.S);
        Assert.Equal(DiagramMode.Steps, view.Explorer!.Mode);
        Assert.True(view.Chrome!.Strip.IsVisible);
        Key(window, Avalonia.Input.Key.Right);
        Key(window, Avalonia.Input.Key.Right);
        Assert.Equal("C", view.Explorer.CurrentStep!.Node);
        Assert.Equal("yes → Give the prescribed analgesic", view.Explorer.StepCaption);
        Assert.Contains("Step 3 of 5", view.Chrome.Announcer.Text);
        // The walk so far is lit, what's to come dims; the arrow that brought it here is picked out.
        var look = Picture(view).Look!;
        Assert.Equal(["A", "B", "C"], look.Lit!.Order());
        Assert.Equal([1], look.Accented!);
        // The last step says where the loop goes back to.
        Key(window, Avalonia.Input.Key.End);
        Assert.Equal("Reassess in 30 to 60 minutes, then back to Assess pain on a 0 to 10 scale (still in pain)", view.Explorer.StepCaption);
        Key(window, Avalonia.Input.Key.Escape);
        Assert.Equal(DiagramMode.Explore, view.Explorer.Mode);
        Assert.False(view.Chrome.Strip.IsVisible);
        window.Close();
    }

    [AvaloniaFact]
    public void Recall_hides_the_words_a_click_checks_one_and_the_score_counts_what_was_known()
    {
        var view = new DiagramView { Chart = Flowchart.Parse(RichDemo.PainReassess) };
        var window = Show(new StackPanel { Children = { view } });
        view.Focus();
        Key(window, Avalonia.Input.Key.H);
        var x = view.Explorer!;
        Assert.Equal(5, x.HiddenCount);
        Assert.Equal(5, Picture(view).Look!.Hidden!.Count);

        Click(window, At(view, "C"));
        Assert.Equal("C", x.Asking);
        Assert.DoesNotContain("C", Picture(view).Look!.Hidden!);
        Key(window, Avalonia.Input.Key.Y);
        Click(window, At(view, "A"));
        Key(window, Avalonia.Input.Key.N);
        Assert.Equal((1, 2), (x.Knew, x.Checked));
        Assert.True(Picture(view).Look!.Marks!["C"]);
        Assert.False(Picture(view).Look!.Marks!["A"]);

        x.ShowAll();
        Assert.True(x.AllChecked);
        Assert.Empty(Picture(view).Look!.Hidden!);
        // Practise the one missed: only it is hidden again, the score starts afresh.
        x.PractiseMissed();
        Assert.Equal(["A"], Picture(view).Look!.Hidden!);
        Assert.Equal(0, x.Checked);
        x.Shuffle();
        Assert.Equal(3, x.HiddenCount);
        window.Close();
    }

    [AvaloniaFact]
    public void A_pinned_box_is_asked_about_in_the_lectures_ask_bar_and_found_in_its_transcript_from_the_note_or_the_larger_window()
    {
        var page = new Page
        {
            Transcript =
            [
                new(60, 70, "First we assess pain on a scale from zero to ten."),
                new(400, 410, "If they're still in pain we give the prescribed analgesic, as charted."),
                new(900, 910, "Then reassess in thirty to sixty minutes."),
            ],
        };
        string source = "%% Study Stash diagram, from 6:30\n" + RichDemo.PainReassess;
        var note = new NoteView { Markdown = "```mermaid\n" + source + "\n```" };
        DiagramHost.SetHost(note, page);
        var window = Show(note);
        var view = window.GetVisualDescendants().OfType<DiagramView>().Single();
        Click(window, At(view, "C"));
        var explain = view.Chrome!.Actions.GetVisualDescendants().OfType<Button>().First(b => b.IsVisible);
        explain.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        var q = Assert.Single(page.Asked);
        Assert.Equal("Explain “Give the prescribed analgesic” from this lecture's diagram, as the lecture taught it. In the diagram it comes from “Score above 4?” (yes) and leads to “Reassess in 30 to 60 minutes”.", q);

        view.Explorer!.FindSaid("C");
        Assert.Equal([400.0], page.Shown);

        // From the larger window, the same lecture hears it.
        view.OpenLarger();
        var larger = DiagramWindow.Current!;
        DiagramsReady.Wait(larger);
        var big = larger.GetVisualDescendants().OfType<DiagramView>().Single();
        Assert.Equal(390, big.Explorer!.At);
        big.Explorer.Pin("E", actions: true);
        big.Explorer.Ask(big.Explorer.QuizQuestion("E"));
        Assert.StartsWith("Quiz me on “Reassess in 30 to 60 minutes”", page.Asked[1]);
        big.Explorer.JumpToMoment();
        Assert.Equal(390, page.Shown[^1]);
        larger.Close();
        window.Close();

        // A diagram on no lecture's page (a quick answer) offers no asking at all.
        var lone = new DiagramView { Chart = Flowchart.Parse(RichDemo.PainReassess) };
        var other = Show(new StackPanel { Children = { lone } });
        Click(other, At(lone, "C"));
        Assert.All(lone.Chrome!.Actions.GetVisualDescendants().OfType<Button>().Where(b => b.IsVisible),
            b => Assert.Equal("Zoom in", Avalonia.Automation.AutomationProperties.GetName(b)));
        other.Close();
    }

    [AvaloniaFact]
    public void The_lecture_page_asks_in_its_ask_bar_and_shows_the_transcript_at_the_line_said_then()
    {
        var library = new LibraryModel();
        var note = new NoteModel { Id = "lec", Title = "Pain" };
        foreach (var (t, s) in new[] { (10.0, "Hello."), (65.0, "Assess pain first."), (130.0, "Then treat.") })
        {
            note.Transcript.Add(new HeardLine { Time = TimedText.Clock(t), Text = s, Start = t });
            note.Spoken.Add(new Spoken(t, t + 5, s));
        }
        library.Note = note;
        var ai = new FakeAiLibrary();
        library.Ask = new AiAskModel(ai) { LectureId = "lec" };
        var host = new LectureDiagrams(library, _ => false, (_, _) => { });
        int jumped = -1;
        note.Jumped += i => jumped = i;

        host.ShowTranscript(70);
        Assert.True(note.ShowTranscript);
        Assert.Equal(1, jumped);
        Assert.Equal([false, true, false], note.Transcript.Select(l => l.Here));
        // One click takes the student back to the notes, where the diagram is.
        Assert.True(note.CanGoBack);
        note.BackToNotesCommand.Execute(null);
        Assert.False(note.ShowTranscript);
        Assert.False(note.CanGoBack);
        Assert.Equal(3, host.Transcript.Count);

        Assert.True(host.CanAsk);
        host.Ask("Explain “Assess pain” from the diagram “Pain”.");
        Assert.Equal("Explain “Assess pain” from the diagram “Pain”.", Assert.Single(library.Ask.Turns).Question);
    }
}
