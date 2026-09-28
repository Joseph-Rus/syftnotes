using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.Styling;
using StudyStash.App.Controls;
using StudyStash.App.Controls.Rich;
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
}
