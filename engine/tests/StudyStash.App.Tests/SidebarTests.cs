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
    public void A_whole_term_of_classes_fits_the_sidebar(SkinKind skin, double width, double height)
    {
        try
        {
            ((App)Application.Current!).UseSkin(skin);
            var view = Library(skin);
            var window = new Window { Width = width, Height = height, Content = view };
            window.Show();
            window.UpdateLayout();

            var list = view.FindControl<ScrollViewer>("ClassList")!;
            var status = view.FindControl<Grid>("StatusRow")!;
            // Whatever doesn't fit scrolls, inside the list: nothing of it reaches the status row.
            var unsorted = list.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == "Unsorted");
            bool fits = In(unsorted, view).Bottom <= In(list, view).Bottom + 0.5;
            Assert.True(fits || list.Extent.Height > list.Viewport.Height, $"{skin}: the classes run past the list without scrolling");
            if (skin == SkinKind.Win && height <= 700) Assert.False(fits, "thirteen classes should scroll in a 700 tall window");
            Assert.True(In(status, view).Top >= In(list, view).Bottom - 0.5, $"{skin}: the status row starts inside the class list");
            Assert.True(status.GetVisualDescendants().OfType<TextBlock>().Single().TextLayout.TextLines.Count == 1, $"{skin}: the status wraps");

            var rows = list.GetVisualDescendants().OfType<TextBlock>().Where(t => t.Classes.Contains("name")).ToList();
            Assert.Equal(13, rows.Count);
            foreach (var name in rows.Where(r => r.IsEffectivelyVisible))
            {
                var count = name.GetVisualParent()!.GetVisualChildren().OfType<TextBlock>().Single(t => t.Classes.Contains("count"));
                Assert.True(In(count, view).Left - In(name, view).Right >= 8, $"{skin}: '{name.Text}' runs into its count");
                Assert.Equal(name.Text, ToolTip.GetTip(name));
                if (name.Text!.Length > 40)
                    Assert.True(name.TextLayout.TextLines[0].HasCollapsed, $"{skin}: '{name.Text}' isn't cut short with an ellipsis");
            }

            // Every count, Unsorted's too, ends on the same right edge.
            var counts = list.GetVisualDescendants().OfType<TextBlock>().Where(t => t.Classes.Contains("count") && t.IsEffectivelyVisible).Select(t => In(t, view).Right).ToList();
            Assert.True(counts.Max() - counts.Min() < 0.5, $"{skin}: the counts end {counts.Min():0.#}–{counts.Max():0.#}");

            var title = view.FindControl<TextBlock>("ListTitle")!;
            var column = title.GetVisualParent()!;
            Assert.True(title.Bounds.Right <= column.Bounds.Width + 0.5, $"{skin}: the list's header runs past its column");
            var lines = title.TextLayout.TextLines;
            Assert.True(lines.Count <= 2, $"{skin}: the header takes {lines.Count} lines");
            Assert.True(lines.All(l => l.WidthIncludingTrailingWhitespace <= title.Bounds.Width + 0.5) || lines[^1].HasCollapsed, $"{skin}: the header is clipped, not wrapped or cut short");
            Assert.Equal(title.Text, ToolTip.GetTip(title));
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
