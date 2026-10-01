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
using StudyStash.App.Controls.Rich;
using StudyStash.App.ViewModels;
using StudyStash.App.Views;
using StudyStash.App.Windows;
using StudyStash.Core.Rich;

namespace StudyStash.App.Tests;

/// <summary>
/// The notes' rich content drawn as a note shows it, light and dark, in both looks: each sample diagram under its
/// heading and sentence, in the notes column (620 on a Mac, 640 on Windows) on the library's page.
/// </summary>
public class RichShots
{
    static readonly ThemeVariant[] Themes = [ThemeVariant.Light, ThemeVariant.Dark];

    static RichShots() => Environment.SetEnvironmentVariable("STUDYSTASH_STILL", "1");

    /// <summary>The library's reading page with the given diagrams in its notes column.</summary>
    static Control Page(SkinKind skin, IEnumerable<(string Title, string Words, string Source)> diagrams)
    {
        bool mac = skin == SkinKind.Mac;
        var column = new StackPanel { Width = mac ? 620 : 640, Spacing = mac ? 14 : 12, HorizontalAlignment = HorizontalAlignment.Left };
        foreach (var (title, words, source) in diagrams)
        {
            column.Children.Add(new NoteView { Markdown = $"## {title}\n\n{words}" });
            column.Children.Add(new DiagramView { Chart = Flowchart.Parse(source), Margin = new Thickness(0, 6) });
        }
        var page = new Border { Padding = mac ? new Thickness(64, 28) : new Thickness(56, 24), Child = column };
        page.Bind(Border.BackgroundProperty, page.GetResourceObservable(mac ? "Win" : "Layer"));
        if (mac) return page;
        var mica = new Border { Child = page };
        mica.Bind(Border.BackgroundProperty, mica.GetResourceObservable("Mica"));
        return mica;
    }

    [AvaloniaFact]
    public void Diagrams()
    {
        var all = RichDemo.Diagrams;
        foreach (var skin in new[] { SkinKind.Mac, SkinKind.Win })
            foreach (var t in Themes)
            {
                string look = skin == SkinKind.Mac ? "mac" : "win";
                Shot.Take($"rich-diagrams-1-{look}", skin, t, () => Page(skin, all.Take(3)), size: new Size(876, 1560));
                Shot.Take($"rich-diagrams-2-{look}", skin, t, () => Page(skin, all.Skip(3).Take(3)), size: new Size(876, 2000));
                Shot.Take($"rich-diagrams-3-{look}", skin, t, () => Page(skin, all.Skip(6)), size: new Size(876, 2000));
            }
    }

    /// <summary>Every shape, line and end a chart can have, with a group inside a group.</summary>
    const string Shapes = """
        flowchart TD
          subgraph outer [Assessment]
            A[Box] --> B(Rounded)
            subgraph inner [Vital signs]
              C([Stadium]) ==> D((Circle))
            end
          end
          B --> C
          D -.-> E{Decision?}
          E -->|yes| F{{Hexagon}}:::green
          E -->|no| G[[Subroutine]]:::purple
          F --o H[(Cylinder)]:::amber
          G --x H
          H <--> A
          H --- I[Loose end]
          A --> A
          F -->|again| H
        """;

    [AvaloniaFact]
    public void Diagram_shapes()
    {
        foreach (var skin in new[] { SkinKind.Mac, SkinKind.Win })
            foreach (var t in Themes)
                Shot.Take($"rich-diagram-shapes-{(skin == SkinKind.Mac ? "mac" : "win")}", skin, t,
                    () => Page(skin, [("Every shape", "Boxes, lines and ends of every kind, and a group inside a group.", Shapes)]), size: new Size(876, 1100));
    }

    /// <summary>The same diagrams in a narrow column: scaled down, or scrolling sideways past the floor.</summary>
    [AvaloniaFact]
    public void Diagrams_narrow()
    {
        foreach (var t in Themes)
            Shot.Take("rich-diagrams-narrow-mac", SkinKind.Mac, t, () =>
            {
                var column = new StackPanel { Width = 300, Spacing = 18 };
                foreach (var d in RichDemo.Diagrams) column.Children.Add(new DiagramView { Chart = Flowchart.Parse(d.Source) });
                var page = new Border { Padding = new Thickness(24), Child = column };
                page.Bind(Border.BackgroundProperty, page.GetResourceObservable("Win"));
                return page;
            }, size: new Size(476, 1900));
    }

    /// <summary>A note on the library's page, in the look's notes column.</summary>
    internal static Control NotePage(SkinKind skin, string markdown, Action<NoteView>? after = null)
    {
        bool mac = skin == SkinKind.Mac;
        var note = new NoteView { Markdown = markdown, Width = mac ? 620 : 640, HorizontalAlignment = HorizontalAlignment.Left };
        after?.Invoke(note);
        var page = new Border { Padding = mac ? new Thickness(64, 28) : new Thickness(56, 24), Child = note };
        page.Bind(Border.BackgroundProperty, page.GetResourceObservable(mac ? "Win" : "Layer"));
        if (mac) return page;
        var mica = new Border { Child = page };
        mica.Bind(Border.BackgroundProperty, mica.GetResourceObservable("Mica"));
        return mica;
    }

    /// <summary>The demo lecture's diagrams as its notes show them: a ring, an AI's SVG drawing, a decision chart,
    /// each with its sentence; the first one pointed at, so its open-larger badge shows.</summary>
    [AvaloniaFact]
    public void Note_diagrams()
    {
        foreach (var skin in new[] { SkinKind.Mac, SkinKind.Win })
            foreach (var t in Themes)
                Shot.Take($"rich-note-diagrams-{(skin == SkinKind.Mac ? "mac" : "win")}", skin, t, () => NotePage(skin, RichDemo.DiagramNotes, note =>
                {
                    var first = note.Children.OfType<DiagramView>().First();
                    ((Panel)first.Child!).Children.OfType<Border>().Single().IsVisible = true;
                }), size: new Size(876, 1640));
    }

    /// <summary>Diagrams that can't be drawn: a sequence diagram and a chart with a box left open, each a calm card
    /// with its reason and its source; and a diagram still arriving.</summary>
    [AvaloniaFact]
    public void Diagram_fallback()
    {
        string markdown = RichDemo.FallbackNotes + "\n\nStill arriving:\n\n```mermaid\nflowchart LR\n  A[Assess] --> B";
        foreach (var skin in new[] { SkinKind.Mac, SkinKind.Win })
            foreach (var t in Themes)
                Shot.Take($"rich-diagram-fallback-{(skin == SkinKind.Mac ? "mac" : "win")}", skin, t, () => NotePage(skin, markdown), size: new Size(876, 700));
    }

    /// <summary>A big chart still being laid out: the quiet space its note keeps for it, about its size, with the
    /// words after it already in place.</summary>
    [AvaloniaFact]
    public void Diagram_laying()
    {
        var random = new Random(11);
        // Words no earlier picture laid out (the same length every time, and hidden until the chart is drawn).
        string stamp = Guid.NewGuid().ToString("N")[..6];
        var lines = new List<string> { "flowchart TD" };
        for (int i = 0; i < 40; i++) lines.Add($"N{i}[Admission step {i} {stamp}]");
        for (int i = 0; i < 70; i++) lines.Add($"N{random.Next(40)} --> N{random.Next(40)}");
        string markdown = "## Admitting a patient\n\nEvery step of an admission, from the door to the ward.\n\n```mermaid\n" + string.Join("\n", lines) +
            "\n```\n\nThe notes go on below while the chart is drawn.";
        using var release = new ManualResetEventSlim(false);
        SceneCache.Laying = _ => release.Wait(TimeSpan.FromSeconds(30));
        try
        {
            foreach (var skin in new[] { SkinKind.Mac, SkinKind.Win })
                foreach (var t in Themes)
                {
                    Skin.UseTheme(ColourThemes.Default);
                    ((App)Application.Current!).UseSkin(skin);
                    var size = new Size(876, 960);
                    var window = new Window { Width = size.Width, Height = size.Height, RequestedThemeVariant = t, Content = NotePage(skin, markdown) };
                    Look.Apply(window);
                    window.Show();
                    Dispatcher.UIThread.RunJobs();
                    Assert.True(window.GetVisualDescendants().OfType<DiagramView>().Single().IsLaying);
                    SaveShot($"rich-diagram-laying-{(skin == SkinKind.Mac ? "mac" : "win")}", t, window, size);
                    window.Close();
                }
        }
        finally
        {
            SceneCache.Laying = null;
            release.Set();
        }
    }

    /// <summary>The larger window's content at the size it opens at: the four chambers on the Mac in light, the
    /// decision chart on Windows in dark.</summary>
    [AvaloniaFact]
    public void Diagram_window()
    {
        var chart = Flowchart.Parse(RichDemo.PainReassess);
        foreach (var (skin, variant) in new[] { (SkinKind.Mac, ThemeVariant.Light), (SkinKind.Win, ThemeVariant.Dark) })
        {
            ((App)Application.Current!).UseSkin(skin);
            var font = Application.Current!.FindResource("TextFont") as FontFamily ?? FontFamily.Default;
            var requests = new[]
            {
                new OpenDiagramEventArgs(new Border()) { Title = "The four chambers of the heart", Svg = RichDemo.FourChambers },
                new OpenDiagramEventArgs(new Border()) { Title = "Pain: assess, act, reassess", Chart = chart, Scene = DiagramLayout.Lay(chart, SceneCache.Measurer(font)) },
            };
            foreach (var r in requests)
            {
                var (w, h) = DiagramWindow.Size(DiagramWindow.Natural(r), new Size(1440, 900));
                string name = $"rich-diagram-window-{(skin == SkinKind.Mac ? "mac" : "win")}-{(r.Svg is null ? "chart" : "svg")}";
                Shot.Take(name, skin, variant, () => new Border { Width = w, Height = h, Child = DiagramWindow.Content(r) }, size: new Size(w + 128, h + 128));
            }
        }
    }

    /// <summary>Formulas as the notes show them: inline maths on the text's baseline (cardiac output, MAP), then a
    /// dose calculation, an <c>aligned</c> block, a <c>cases</c> block and a chemistry formula, each on its own
    /// centred line.</summary>
    [AvaloniaFact]
    public void Formulas()
    {
        foreach (var skin in new[] { SkinKind.Mac, SkinKind.Win })
            foreach (var t in Themes)
                Shot.Take($"rich-formulas-{(skin == SkinKind.Mac ? "mac" : "win")}", skin, t, () => NotePage(skin, RichDemo.FormulaNotes), size: new Size(876, 1760));
    }

    /// <summary>What the notes' prompt shows the engines, drawn the way a note shows it — the worked dose, the
    /// blood-flow flowchart and the block on a slope — since the models imitate exactly these.</summary>
    [AvaloniaFact]
    public void Prompt_examples()
    {
        string markdown = string.Join("\n\n",
            "## Details and examples",
            "A dose, worked step by step:",
            Core.Summarize.DoseExample,
            "```mermaid\n" + Core.Summarize.MermaidExample + "\n```",
            "Blood goes from the right side of the heart to the lungs, back to the left side, and out to the body.",
            "```svg\n" + Core.Summarize.SvgExample + "\n```",
            "The block's weight pulls straight down, the slope pushes back square to its surface, and friction holds it up the slope.");
        foreach (var skin in new[] { SkinKind.Mac, SkinKind.Win })
            foreach (var t in Themes)
                Shot.Take($"rich-prompt-examples-{(skin == SkinKind.Mac ? "mac" : "win")}", skin, t, () => NotePage(skin, markdown), size: new Size(876, 1080));
    }

    /// <summary>A formula that lost a brace: shown as its plain source, inline in mono and on its own line in the
    /// code-box look, each with the same quiet line saying it couldn't be typeset.</summary>
    [AvaloniaFact]
    public void Formula_fallback()
    {
        foreach (var skin in new[] { SkinKind.Mac, SkinKind.Win })
            foreach (var t in Themes)
                Shot.Take($"rich-formula-fallback-{(skin == SkinKind.Mac ? "mac" : "win")}", skin, t, () => NotePage(skin, RichDemo.FormulaFallbackNotes), size: new Size(876, 560));
    }

    /// <summary>The share/Export button's menu open over the lecture page: "Download as Markdown…", "Download as PDF…",
    /// "Download all of {class}…" and "Include transcripts" checked, one left edge for every row.</summary>
    static ContextMenu OpenDownloadMenu()
    {
        var menu = DownloadMenu.Build("BIO 110", includeTranscripts: true, () => { }, () => { }, () => { }, () => { });
        menu.VerticalAlignment = VerticalAlignment.Top;
        return menu;
    }

    [AvaloniaFact]
    public void Download_menu()
    {
        foreach (var t in Themes)
        {
            Shot.Take("rich-download-menu-mac", SkinKind.Mac, t, () => Shot.Side(
                new MacLibrary { DataContext = Demo.Library(), Width = 900, Height = 640 }, OpenDownloadMenu()), size: new Size(1350, 640));
            Shot.Take("rich-download-menu-win", SkinKind.Win, t, () => Shot.Side(
                new WinLibrary { DataContext = Demo.Library(), Width = 900, Height = 640 }, OpenDownloadMenu()), size: new Size(1350, 640));
        }
    }

    // -----------------------------------------------------------------------------------------------------------
    // Task 6: the cardiac-cycle lecture proves the whole thing at once — the full app, the notes column alone, a
    // quick answer with a formula and a chat answer with a diagram. Demo.cs stays CS 101's and untouched: this
    // library is our own, built straight from RichDemo.
    // -----------------------------------------------------------------------------------------------------------

    static IBrush BioDot => Skin.ClassDot(1);

    /// <summary>The sidebar and lecture list around the cardiac-cycle lecture, open on the right.</summary>
    internal static LibraryModel CardiacLibrary()
    {
        var m = new LibraryModel { ClassTitle = RichDemo.LectureClassName, ClassCount = "9 lectures", Status = "Library connected", DrawChrome = true };
        m.Classes.Add(new ClassItem { Name = "CS 101", Dot = Skin.ClassDot(0), Count = 12 });
        m.Classes.Add(new ClassItem { Name = RichDemo.LectureClassName, Dot = BioDot, Count = 9, Selected = true });
        m.Classes.Add(new ClassItem { Name = "CALC II", Dot = Skin.ClassDot(2), Count = 11 });
        m.Unsorted = new ClassItem { Name = "Unsorted", IsUnsorted = true, Count = 1 };
        var week = new LectureGroup { Label = "This week", First = true };
        week.Items.Add(new LectureCard { Title = RichDemo.LectureTitle, Meta = "Tue 23 Sep · 1 h 12 min", Summary = "The heart moves blood in one continuous cycle, once a beat.", Selected = true });
        week.Items.Add(new LectureCard { Title = "Blood pressure and perfusion", Meta = "Thu 18 Sep · 1 h 05 min", Summary = "How the body keeps blood moving when pressure drops.", Last = true });
        m.Groups.Add(week);
        m.Note = new NoteModel { ClassName = RichDemo.LectureClassName, Dot = BioDot, Meta = RichDemo.LectureMeta, Title = RichDemo.LectureTitle, Markdown = RichDemo.CardiacLecture };
        var ask = AiDemo.AskIdle();
        ask.ClassName = RichDemo.LectureClassName;
        m.Ask = ask;
        return m;
    }

    /// <summary>Scrolls the notes column so a diagram shows a little below the top, the way someone reading the
    /// lecture would have scrolled to it.</summary>
    internal static void ScrollToDiagrams(Window window)
    {
        DiagramsReady.Wait(window);
        var scroller = window.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault(s => s.GetVisualDescendants().OfType<NoteView>().Any());
        var diagram = window.GetVisualDescendants().FirstOrDefault(v => v is DiagramView or SvgView) as Visual;
        if (scroller is null || diagram is null) return;
        var top = diagram.TranslatePoint(new Point(0, 0), scroller)?.Y ?? 0;
        scroller.Offset = new Vector(0, Math.Max(0, scroller.Offset.Y + top - 40));
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>Captures the window as it stands right now (no extra layout pass), unlike <see cref="Shot.Take"/>
    /// which lays out and captures in the same breath — this shot needs to scroll in between.</summary>
    static void SaveShot(string name, ThemeVariant variant, Window window, Size size)
    {
        var whole = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("nothing rendered");
        var frame = new RenderTargetBitmap(new PixelSize((int)size.Width, (int)size.Height));
        using (var ctx = frame.CreateDrawingContext()) ctx.DrawImage(whole, new Rect(size), new Rect(size));
        using var file = File.Create(Path.Combine(Shot.Dir, $"{name}-{(variant == ThemeVariant.Dark ? "dark" : "light")}.png"));
        frame.Save(file, PngBitmapEncoderOptions.Default);
    }

    /// <summary>The full app on the cardiac-cycle lecture, scrolled to its diagrams: the same window
    /// <c>Shots.Mac_app</c> draws, but BIO 110's lecture instead of CS 101's — read next to
    /// <c>ref/mac-04-full-app-*.png</c> / <c>win-04</c> for type, spacing and margins.</summary>
    [AvaloniaFact]
    public void Full_app_cardiac_lecture()
    {
        foreach (var (skin, look) in new[] { (SkinKind.Mac, "mac"), (SkinKind.Win, "win") })
            foreach (var t in Themes)
            {
                Skin.UseTheme(ColourThemes.Default);
                ((App)Application.Current!).UseSkin(skin);
                var size = new Size(1280, 800);
                Control content = skin == SkinKind.Mac
                    ? new MacLibrary { DataContext = CardiacLibrary(), Width = size.Width, Height = size.Height }
                    : new WinLibrary { DataContext = CardiacLibrary(), Width = size.Width, Height = size.Height };
                var window = new Window { Width = size.Width, Height = size.Height, RequestedThemeVariant = t, Content = content };
                Look.Apply(window);
                window.Show();
                ScrollToDiagrams(window);
                SaveShot($"rich-full-app-{look}", t, window, size);
                window.Close();
            }
    }

    /// <summary>The lecture's whole notes column, alone, at full height — every block visible at once.</summary>
    [AvaloniaFact]
    public void Note_full_lecture()
    {
        foreach (var skin in new[] { SkinKind.Mac, SkinKind.Win })
            foreach (var t in Themes)
                Shot.Take($"rich-note-lecture-{(skin == SkinKind.Mac ? "mac" : "win")}", skin, t, () => NotePage(skin, RichDemo.CardiacLecture), size: new Size(876, 3300));
    }

    /// <summary>The quick panel's answer to "what's a normal MAP?", its formula typeset on the answer's own serif
    /// baseline (mac-03/win-03 sizes).</summary>
    [AvaloniaFact]
    public void Quick_answer_with_formula()
    {
        var q = new QuickModel { Query = "what's a normal map?", Answering = true, Answer = RichDemo.QuickMapAnswer };
        q.Rows.Add(new QuickRow { Kind = QuickKind.Header, Title = "Sources", First = true });
        q.Rows.Add(new QuickRow { Kind = QuickKind.Source, Title = RichDemo.LectureTitle, Meta = "8:40", Selected = true });
        foreach (var t in Themes)
        {
            Shot.Take("rich-quick-answer-mac", SkinKind.Mac, t, () => new MacQuick { DataContext = q }, size: Shot.RefSize("mac-03"));
            Shot.Take("rich-quick-answer-win", SkinKind.Win, t, () => new WinQuick { DataContext = q }, size: Shot.RefSize("win-03"));
        }
    }

    /// <summary>The ask chat's answer with a small flowchart, in its own bubble (mac-16/win-16 sizes).</summary>
    [AvaloniaFact]
    public void Chat_answer_with_diagram()
    {
        var model = new AiAskModel(new FakeAiLibrary()) { LectureId = "cardiac-cycle", ClassName = RichDemo.LectureClassName };
        model.Turns.Add(new AiTurn("How is low blood pressure treated?", "Ollama") { Answer = RichDemo.ChatFlowAnswer, Byline = "Ollama · from 41:20" });
        foreach (var t in Themes)
        {
            Shot.Take("rich-chat-answer-mac", SkinKind.Mac, t, () => new MacAiAskChat { DataContext = model }, size: Shot.RefSize("mac-16"));
            Shot.Take("rich-chat-answer-win", SkinKind.Win, t, () => new WinAiAskChat { DataContext = model }, size: Shot.RefSize("win-16"));
        }
    }
}
