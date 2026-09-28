using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using StudyStash.App.Controls;
using StudyStash.App.Controls.Rich;
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
                Shot.Take($"rich-diagrams-2-{look}", skin, t, () => Page(skin, all.Skip(3)), size: new Size(876, 2000));
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
    static Control NotePage(SkinKind skin, string markdown, Action<NoteView>? after = null)
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

    /// <summary>A formula that lost a brace: shown as its plain source, inline in mono and on its own line in the
    /// code-box look, each with the same quiet line saying it couldn't be typeset.</summary>
    [AvaloniaFact]
    public void Formula_fallback()
    {
        foreach (var skin in new[] { SkinKind.Mac, SkinKind.Win })
            foreach (var t in Themes)
                Shot.Take($"rich-formula-fallback-{(skin == SkinKind.Mac ? "mac" : "win")}", skin, t, () => NotePage(skin, RichDemo.FormulaFallbackNotes), size: new Size(876, 560));
    }
}
