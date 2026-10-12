using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Avalonia.VisualTree;
using StudyStash.App.Views;

namespace StudyStash.App.Tests;

/// <summary>
/// The library's sidebar with a whole term of classes, their names as long as a school's catalogue makes them: the
/// classes scroll above the library's status (which has a row of its own, never under a class), each count keeps a
/// column of its own clear of the name, a name too long ends in "…" with the whole of it on hover, and the open class's
/// name wraps in the list's header instead of running off its edge.
/// </summary>
public class SidebarTests
{
    static Control Library(SkinKind skin) => skin == SkinKind.Mac
        ? new MacLibrary { DataContext = Demo.Crowded() }
        : new WinLibrary { DataContext = Demo.Crowded() };

    static Rect In(Visual v, Visual root) => new(v.TranslatePoint(new Point(0, 0), root)!.Value, v.Bounds.Size);

    [AvaloniaTheory]
    [InlineData(SkinKind.Win, 1280, 800)]
    [InlineData(SkinKind.Win, 1024, 700)]
    [InlineData(SkinKind.Mac, 1280, 800)]
    [InlineData(SkinKind.Mac, 1024, 700)]
    [InlineData(SkinKind.Mac, 1024, 560)]
    [InlineData(SkinKind.Win, 1024, 560)]
    public void A_whole_term_of_classes_fits_the_sidebar(SkinKind skin, double width, double height)
    {
        try
        {
            ((App)Application.Current!).UseSkin(skin);
            var view = Library(skin);
            var window = new Window { Width = width, Height = height, Content = view };
            window.Show();
            window.UpdateLayout();

            var list = view.Part<ScrollViewer>("ClassList");
            var status = view.Part<ItemsControl>("StatusRow");
            // Whatever doesn't fit scrolls, inside the list: nothing of it reaches the status lines.
            var last = list.GetVisualDescendants().OfType<TextBlock>().Last(t => t.Classes.Contains("name"));
            bool fits = In(last, view).Bottom <= In(list, view).Bottom + 0.5;
            Assert.True(fits || list.Extent.Height > list.Viewport.Height, $"{skin}: the classes run past the list without scrolling");
            if (height <= 600) Assert.False(fits, "thirteen classes should scroll in a 600 tall window");
            Assert.True(In(status, view).Top >= In(list, view).Bottom - 0.5, $"{skin}: the status starts inside the class list");
            Assert.True(status.GetVisualDescendants().OfType<TextBlock>().Single().TextLayout.TextLines.Count == 1, $"{skin}: the status wraps");

            var rows = list.GetVisualDescendants().OfType<TextBlock>().Where(t => t.Classes.Contains("name")).ToList();
            Assert.Equal(13, rows.Count);
            foreach (var name in rows.Where(r => r.IsEffectivelyVisible))
            {
                // A class with lectures shows how many, clear of its name; one with none shows no count at all.
                var count = name.GetVisualParent()!.GetVisualChildren().OfType<TextBlock>().Single(t => t.Classes.Contains("count"));
                if (count.IsVisible) Assert.True(In(count, view).Left - In(name, view).Right >= 8, $"{skin}: '{name.Text}' runs into its count");
                else Assert.Equal("0", count.Text);
                // The name is without its registrar's code; the tooltip of one cut short has the whole name, and the code.
                Assert.DoesNotContain("(", name.Text);
                Assert.StartsWith(name.Text!, (string)ToolTip.GetTip(name)!);
                if (name.Text!.Length > 32)
                    Assert.True(name.TextLayout.TextLines[0].HasCollapsed, $"{skin}: '{name.Text}' isn't cut short with an ellipsis");
            }

            // Every count that shows ends on the same right edge.
            var counts = list.GetVisualDescendants().OfType<TextBlock>().Where(t => t.Classes.Contains("count") && t.IsEffectivelyVisible).Select(t => In(t, view).Right).ToList();
            Assert.NotEmpty(counts);
            Assert.True(counts.Max() - counts.Min() < 0.5, $"{skin}: the counts end {counts.Min():0.#}–{counts.Max():0.#}");

            // The open folder's page: its long name wraps in its own column, without the registrar's code.
            var title = view.Part<StudyStash.App.Controls.Highlight>("FolderTitle");
            Assert.Equal("Probability and Statistics for Engineers", title.Text);
            Assert.True(In(title, view).Right <= width - 8, $"{skin}: the folder's name runs past the page");
            Assert.True(title.TextLayout.TextLines.Count <= 3, $"{skin}: the name takes {title.TextLayout.TextLines.Count} lines");
            window.Close();
        }
        finally
        {
            ((App)Application.Current!).UseSkin(SkinKind.Mac);
        }
    }

    [AvaloniaFact]
    public void Crowded_shots()
    {
        foreach (var t in new[] { ThemeVariant.Light, ThemeVariant.Dark })
        {
            Shot.Take("win-04-full-app-crowded", SkinKind.Win, t, () => new WinLibrary { DataContext = Demo.Crowded(), Width = 1280, Height = 800 });
            Shot.Take("mac-04-full-app-crowded", SkinKind.Mac, t, () => new MacLibrary { DataContext = Demo.Crowded(), Width = 1280, Height = 800 });
        }
    }
}
