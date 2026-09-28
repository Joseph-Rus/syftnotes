using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StudyStash.App.Controls;
using StudyStash.App.Controls.Rich;
using StudyStash.App.Windows;
using StudyStash.Core.Rich;

namespace StudyStash.App.Tests;

/// <summary>Notes that draw their diagrams: flowcharts and safe SVG in order (in a bullet too), a calm card for what
/// can't be drawn, a quiet line for a diagram still arriving, and a click that opens one larger.</summary>
public class NoteViewRichTests
{
    static string Fence(string info, string source) => $"```{info}\n{source.TrimEnd()}\n```";

    /// <summary>Every kind of diagram block a note can hold, the unfinished one last (its fence runs to the end).</summary>
    static readonly string Everything = string.Join("\n\n",
        "## Diagrams",
        Fence("mermaid", RichDemo.CardiacCycle),
        Fence("svg", RichDemo.FourChambers),
        Fence("mermaid", RichDemo.BrokenChart),
        "- Blood flow, in a bullet:\n\n  " + Fence("mermaid", RichDemo.BloodFlow).Replace("\n", "\n  "),
        Fence("mermaid", RichDemo.PainConversation),
        Fence("svg", """<!DOCTYPE svg [<!ENTITY a "a">]><svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 9 9"><text>&a;</text></svg>"""),
        "```mermaid\nflowchart LR\n  A[Assess] --> B[Act");

    static Window Show(Control content, SkinKind skin = SkinKind.Mac, ThemeVariant? variant = null, double width = 620)
    {
        Skin.UseTheme(ColourThemes.Default);
        ((App)Application.Current!).UseSkin(skin);
        content.Width = width;
        content.HorizontalAlignment = HorizontalAlignment.Left;
        content.VerticalAlignment = VerticalAlignment.Top;
        var window = new Window { Width = width + 40, Height = 3000, RequestedThemeVariant = variant ?? ThemeVariant.Light, Content = content };
        window.Show();
        DiagramsReady.Wait(window);
        return window;
    }

    /// <summary>The note's diagram pieces in reading order (a card's own words aren't counted as separate pieces).</summary>
    static List<Control> Pieces(Control root)
    {
        var found = new List<Control>();
        void Walk(ILogical node)
        {
            foreach (var child in node.LogicalChildren)
            {
                if (child is DiagramView or SvgView or DiagramCard || child is TextBlock { Text: NoteView.DrawingWords }) found.Add((Control)child);
                else Walk(child);
            }
        }
        Walk(root);
        return found;
    }

    [AvaloniaFact]
    public void Every_kind_of_diagram_block_builds_the_right_control_in_order()
    {
        var note = new NoteView { Markdown = Everything };
        var window = Show(note);
        var pieces = Pieces(note);
        Assert.Equal(
            [typeof(DiagramView), typeof(SvgView), typeof(DiagramCard), typeof(DiagramView), typeof(DiagramCard), typeof(DiagramCard), typeof(TextBlock)],
            pieces.Select(p => p.GetType()));
        // The bullet's diagram sits inside the list, not after it.
        Assert.Contains(pieces[3].GetLogicalAncestors(), a => a is Grid);
        Assert.Equal("Line 2 has a box that isn't closed: “B[Give the dose”.", ((DiagramCard)pieces[2]).Reason);
        Assert.Equal("Study Stash draws flowcharts; this is a sequence diagram.", ((DiagramCard)pieces[4]).Reason);
        Assert.Equal("This drawing declares its own document type, which isn't allowed.", ((DiagramCard)pieces[5]).Reason);
        Assert.Contains("sequenceDiagram", ((DiagramCard)pieces[4]).Source);
        Assert.All(pieces.Take(2).Append(pieces[3]), p => Assert.True(p.Bounds.Width > 100 && p.Bounds.Height > 50, $"{p.GetType().Name} {p.Bounds}"));
        window.Close();
    }

    [AvaloniaFact]
    public void Every_diagram_block_renders_in_both_looks_light_and_dark()
    {
        foreach (var skin in new[] { SkinKind.Mac, SkinKind.Win })
            foreach (var variant in new[] { ThemeVariant.Light, ThemeVariant.Dark })
            {
                var note = new NoteView { Markdown = RichDemo.DiagramNotes + "\n\n" + RichDemo.FallbackNotes + "\n\n" + Everything };
                var window = Show(note, skin, variant, skin == SkinKind.Mac ? 620 : 640);
                Assert.Equal(3 + 2 + 7, Pieces(note).Count);
                window.Close();
            }
    }

    [AvaloniaFact]
    public void A_fence_inside_a_bullet_is_no_longer_dropped()
    {
        var note = new NoteView { Markdown = "- Step one\n\n  ```\n  code in a bullet\n  ```\n- Step two" };
        var window = Show(note);
        Assert.Contains(note.GetLogicalDescendants().OfType<TextBlock>(), t => t.Text == "code in a bullet");
        window.Close();
    }

    [AvaloniaFact]
    public void Which_fences_are_diagrams()
    {
        string svg = """<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 100 40"><rect width="100" height="40" fill="#E8F0FE"/></svg>""";
        var note = new NoteView
        {
            Markdown = string.Join("\n\n",
                Fence("mmd", "graph TD\n  A --> B"),
                Fence("", "flowchart LR\n  A --> B"),
                Fence("xml", "<?xml version=\"1.0\"?>\n" + svg),
                Fence("html", svg),
                Fence("", "graphs are drawn with --> arrows"),
                Fence("python", "print('flowchart')")),
        };
        var window = Show(note);
        Assert.Equal([typeof(DiagramView), typeof(DiagramView), typeof(SvgView), typeof(SvgView)], Pieces(note).Select(p => p.GetType()));
        Assert.Equal(2, note.Children.OfType<Border>().Count(b => b is not DiagramCard));
        window.Close();
    }

    [AvaloniaFact]
    public void An_svg_written_straight_into_the_markdown_is_drawn_whole_even_with_blank_lines()
    {
        string svg = "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 200 80\">\n<rect width=\"200\" height=\"80\" fill=\"#FCE8E6\"/>\n\n<text x=\"10\" y=\"40\">Left atrium</text>\n</svg>";
        var note = new NoteView { Markdown = "Before.\n\n" + svg + "\n\nAfter." };
        var window = Show(note);
        var view = Assert.Single(Pieces(note).OfType<SvgView>());
        Assert.Equal(["Left atrium"], view.Drawing!.Texts);
        Assert.Equal(["Before.", "After."], note.Children.OfType<TextBlock>().Select(t => string.Concat(t.Inlines!.OfType<Avalonia.Controls.Documents.Run>().Select(r => r.Text))));
        window.Close();
    }

    [AvaloniaFact]
    public void The_same_notes_set_again_keep_their_diagrams()
    {
        var note = new NoteView { Markdown = RichDemo.DiagramNotes };
        var window = Show(note);
        var before = Pieces(note);
        note.Markdown = RichDemo.DiagramNotes + "\n\nOne more sentence.";
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(before, Pieces(note));
        // A changed diagram is drawn afresh; the others stay.
        note.Markdown = RichDemo.DiagramNotes.Replace("Ventricular filling", "Filling");
        Dispatcher.UIThread.RunJobs();
        var after = Pieces(note);
        Assert.NotSame(before[0], after[0]);
        Assert.Same(before[1], after[1]);
        Assert.Same(before[2], after[2]);
        Assert.NotNull(window.CaptureRenderedFrame());
        window.Close();
    }

    [AvaloniaFact]
    public void A_big_chart_is_laid_out_away_from_the_window_which_keeps_a_quiet_space_for_it_meanwhile()
    {
        // 60 boxes and 110 arrows every which way (the most a chart may have, and about half a second of layout),
        // with words no other test uses, so nothing has laid it out before.
        var random = new Random(11);
        string stamp = Guid.NewGuid().ToString("N")[..6];
        var lines = new List<string> { "flowchart TD" };
        for (int i = 0; i < 60; i++) lines.Add($"N{i}[Step {i} {stamp}]");
        for (int i = 0; i < 110; i++) lines.Add($"N{random.Next(60)} --> N{random.Next(60)}");
        using var release = new ManualResetEventSlim(false);
        bool? onUiThread = null;
        // Every layout is held back until the page has been built, measured and drawn: if the page waited for one,
        // it would still be waiting (for 30 s, and then find it done).
        SceneCache.Laying = _ =>
        {
            onUiThread ??= Dispatcher.UIThread.CheckAccess();
            release.Wait(TimeSpan.FromSeconds(30));
        };
        try
        {
            var note = new NoteView { Markdown = "## Details\nBefore the chart.\n\n" + Fence("mermaid", string.Join("\n", lines)) + "\n\nAfter the chart.", Width = 620 };
            var window = new Window { Width = 660, Height = 3000, Content = note };
            window.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.NotNull(window.CaptureRenderedFrame());

            var view = window.GetVisualDescendants().OfType<DiagramView>().Single();
            Assert.True(view.IsLaying);
            Assert.Null(view.Scene);
            var kept = view.Bounds.Size;
            Assert.True(kept.Width > 100 && kept.Height > 100, $"kept {kept}");
            // The words after the chart are laid out below the space it keeps.
            var after = window.GetVisualDescendants().OfType<TextBlock>().Last();
            Assert.True(after.TranslatePoint(default, window)!.Value.Y >= view.TranslatePoint(new Point(0, kept.Height), window)!.Value.Y);

            release.Set();
            DiagramsReady.Wait(window);
            Assert.False(onUiThread);
            Assert.False(view.IsLaying);
            Assert.Equal(60, view.Scene!.Nodes.Count);
            // The space kept was about the picture's own size, so the page hardly moved when it came.
            Assert.InRange(view.Bounds.Height / kept.Height, 0.5, 2);
            window.Close();
        }
        finally
        {
            SceneCache.Laying = null;
            release.Set();
        }
    }

    [AvaloniaFact]
    public void A_chart_that_cant_be_laid_out_becomes_the_calm_card_with_its_source()
    {
        const string source = "flowchart TD\n  A[Unlayable start] --> B[Unlayable end]";
        SceneCache.Laying = chart =>
        {
            if (chart.Labels().Contains("Unlayable start")) throw new InvalidOperationException("a layout that fails");
        };
        var logged = new System.Collections.Concurrent.ConcurrentQueue<string>();
        var log = SceneCache.Log;
        SceneCache.Log = logged.Enqueue;
        try
        {
            var note = new NoteView { Markdown = "## Details\n\n" + Fence("mermaid", source) };
            var window = Show(note);
            var card = window.GetVisualDescendants().OfType<DiagramCard>().Single();
            Assert.Equal("Study Stash couldn't lay this flowchart out.", card.Reason);
            Assert.Equal(source, card.Source);
            var view = card.FindAncestorOfType<DiagramView>()!;
            Assert.Null(AutomationProperties.GetHelpText(view)); // it no longer offers to open larger
            Assert.Contains(logged, line => line.Contains("a layout that fails"));
            window.Close();
        }
        finally
        {
            SceneCache.Laying = null;
            SceneCache.Log = log;
        }
    }

    [AvaloniaFact]
    public void A_diagram_still_arriving_is_a_quiet_line_until_its_fence_closes()
    {
        var note = new NoteView { Markdown = "Here's the cycle:\n\n```mermaid\n" + RichDemo.CardiacCycle[..40] };
        var window = Show(note);
        Assert.IsType<TextBlock>(Assert.Single(Pieces(note)));
        note.Markdown = "Here's the cycle:\n\n" + Fence("mermaid", RichDemo.CardiacCycle);
        Dispatcher.UIThread.RunJobs();
        Assert.IsType<DiagramView>(Assert.Single(Pieces(note)));
        window.Close();
    }

    [AvaloniaFact]
    public void An_unfinished_diagram_before_the_end_is_drawn_as_far_as_it_goes_not_left_waiting()
    {
        var note = new NoteView
        {
            Markdown = "- In a bullet:\n\n  ```mermaid\n  flowchart LR\n  A[Assess] --> B[Act]\n\nAfter the list.\n\n"
                + "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 10 10\">\n<rect width=\"10\" height=\"10\"/>\n\n```mermaid\nflowchart LR\n  C --> D\n```\n\nThe end.",
        };
        var window = Show(note);
        Assert.Equal([typeof(DiagramView), typeof(DiagramCard), typeof(DiagramView)], Pieces(note).Select(p => p.GetType()));
        Assert.StartsWith("This drawing isn't well-formed SVG", ((DiagramCard)Pieces(note)[1]).Reason);
        window.Close();
    }

    static void Click(Visual target, Point? at = null)
    {
        var top = TopLevel.GetTopLevel(target)!;
        var p = target.TranslatePoint(at ?? new Point(target.Bounds.Width / 2, target.Bounds.Height / 2), top)!.Value;
        top.MouseMove(p);
        top.MouseDown(p, MouseButton.Left);
        top.MouseUp(p, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public void Clicking_a_diagram_asks_to_open_it_larger_with_its_scene()
    {
        var note = new NoteView { Markdown = RichDemo.DiagramNotes };
        var window = Show(note);
        var asked = new List<OpenDiagramEventArgs>();
        note.AddHandler(OpenLarger.Event, (_, e) =>
        {
            asked.Add(e);
            e.Handled = true;
        });
        var pieces = Pieces(note);
        var chart = (DiagramView)pieces[0];
        Click(chart, new Point(2, 2));
        var e = Assert.Single(asked);
        Assert.Same(chart.Scene, e.Scene);
        Assert.Same(chart.Chart, e.Chart);
        Assert.Equal("Atrial systole", e.Title);
        Assert.Null(e.Svg);

        var drawing = (SvgView)pieces[1];
        Click(drawing);
        Assert.Equal("The four chambers of the heart", asked[1].Title);
        Assert.Equal(RichDemo.FourChambers.Trim(), asked[1].Svg!.Trim());

        // Enter on a focused diagram asks too.
        pieces[2].Focus();
        window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(3, asked.Count);
        Assert.Equal("Assess pain on a 0 to 10 scale", asked[2].Title);
        Assert.Null(DiagramWindow.Current);
        window.Close();
    }

    [AvaloniaFact]
    public void Diagrams_say_they_open_larger_and_show_a_badge_while_pointed_at()
    {
        var note = new NoteView { Markdown = RichDemo.DiagramNotes };
        var window = Show(note);
        foreach (var view in Pieces(note))
        {
            Assert.Equal(OpenLarger.Help, AutomationProperties.GetHelpText(view));
            var badge = view.GetVisualDescendants().OfType<Icon>().Single(i => i.Glyph == "open_in_full").Parent as Control;
            Assert.False(badge!.IsVisible);
            window.MouseMove(view.TranslatePoint(new Point(10, 10), window)!.Value);
            Dispatcher.UIThread.RunJobs();
            Assert.True(badge.IsVisible, view.GetType().Name);
        }
        window.MouseMove(new Point(0, 2990));
        window.Close();
    }

    [AvaloniaFact]
    public void An_unhandled_request_opens_one_window_that_a_second_diagram_reuses_and_esc_closes()
    {
        var note = new NoteView { Markdown = RichDemo.DiagramNotes };
        var window = Show(note);
        var pieces = Pieces(note);
        Click(pieces[0], new Point(2, 2));
        var larger = DiagramWindow.Current;
        Assert.NotNull(larger);
        Assert.Equal("Atrial systole", larger.Title);
        var big = larger.GetVisualDescendants().OfType<DiagramView>().Single();
        Assert.Null(AutomationProperties.GetHelpText(big));
        Assert.True(larger.Width >= 420 && larger.Height >= 300);

        Click(pieces[1]);
        Assert.Same(larger, DiagramWindow.Current);
        Assert.Equal("The four chambers of the heart", larger.Title);
        Assert.Single(larger.GetVisualDescendants().OfType<SvgView>());

        larger.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
        Dispatcher.UIThread.RunJobs();
        Assert.Null(DiagramWindow.Current);
        window.Close();
    }

    [AvaloniaFact]
    public void Command_w_or_ctrl_w_closes_the_larger_window()
    {
        foreach (var modifier in new[] { RawInputModifiers.Meta, RawInputModifiers.Control })
        {
            var w = DiagramWindow.Open(new OpenDiagramEventArgs(new Border()) { Title = "Types of shock", Chart = Flowchart.Parse(RichDemo.TypesOfShock) }, null);
            Dispatcher.UIThread.RunJobs();
            w.KeyPress(Key.W, modifier, PhysicalKey.W, "w");
            Dispatcher.UIThread.RunJobs();
            Assert.Null(DiagramWindow.Current);
        }
    }

    [Fact]
    public void The_larger_window_is_one_and_a_half_times_the_diagram_within_85_percent_of_the_screen()
    {
        Assert.Equal((664, 514), DiagramWindow.Size(new Size(400, 300), new Size(1440, 900)));
        Assert.Equal((1224, 765), DiagramWindow.Size(new Size(1400, 800), new Size(1440, 900)));
        Assert.Equal((420, 300), DiagramWindow.Size(new Size(80, 40), new Size(1440, 900)));
    }

    [AvaloniaFact]
    public void A_drawing_fits_its_column_never_below_the_floor_and_a_tiny_one_grows_a_little()
    {
        var wide = new SvgView { Source = RichDemo.FourChambers };
        var window = Show(new StackPanel { Children = { wide } }, width: 300);
        Assert.Equal(0.6, wide.Scale, 3);
        Assert.True(wide.GetVisualDescendants().OfType<ScrollViewer>().First().Extent.Width > 300);
        window.Close();

        var fits = new SvgView { Source = RichDemo.FourChambers };
        window = Show(new StackPanel { Children = { fits } }, width: 500);
        Assert.Equal(500.0 / 640, fits.Scale, 3);
        Assert.Equal(500, fits.Bounds.Width, 0);
        window.Close();

        var tiny = new SvgView { Source = """<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 200 100"><rect width="200" height="100" fill="#E8F0FE"/></svg>""" };
        window = Show(new StackPanel { Children = { tiny } });
        Assert.Equal((250, 125), (tiny.Bounds.Width, tiny.Bounds.Height));
        window.Close();
        Assert.Equal(720, SvgView.Natural(1000));
        Assert.Equal(480, SvgView.Natural(400));
        Assert.Equal(640, SvgView.Natural(640));
    }

    [AvaloniaFact]
    public void A_drawing_is_recoloured_for_dark_and_named_for_screen_readers()
    {
        var view = new SvgView { Source = RichDemo.FourChambers };
        Assert.Equal("Diagram: The four chambers of the heart, Right atrium, from the body, Left atrium, from the lungs, Right ventricle, to the lungs, Left ventricle, to the body, tricuspid valve, mitral valve, septum",
            AutomationProperties.GetName(view));
        var window = Show(new StackPanel { Children = { view } }, width: 640);
        var inside = view.TranslatePoint(new Point(170, 80), window)!.Value;
        var light = Pixel(window, inside);
        window.RequestedThemeVariant = ThemeVariant.Dark;
        Dispatcher.UIThread.RunJobs();
        var dark = Pixel(window, inside);
        Assert.True(Luma(light) > 200, $"a pale blue box in light, got {light}");
        Assert.True(Luma(dark) < 90, $"a deep blue box in dark, got {dark}");
        Assert.True(dark.B > dark.R, $"still blue in dark, got {dark}");
        window.Close();
    }

    static double Luma(Color c) => 0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B;

    static Color Pixel(Window window, Point at)
    {
        var frame = window.CaptureRenderedFrame()!;
        var buffer = new byte[4];
        var handle = System.Runtime.InteropServices.GCHandle.Alloc(buffer, System.Runtime.InteropServices.GCHandleType.Pinned);
        try
        {
            frame.CopyPixels(new PixelRect((int)at.X, (int)at.Y, 1, 1), handle.AddrOfPinnedObject(), 4, 4);
        }
        finally
        {
            handle.Free();
        }
        bool rgba = frame.Format == Avalonia.Platform.PixelFormats.Rgba8888;
        return rgba ? Color.FromArgb(buffer[3], buffer[0], buffer[1], buffer[2]) : Color.FromArgb(buffer[3], buffer[2], buffer[1], buffer[0]);
    }
}
