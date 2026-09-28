using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Avalonia.VisualTree;
using StudyStash.App.Views;

namespace StudyStash.App.Tests;

/// <summary>
/// The Windows library in a small window: a laptop at 150% has about 1250 × 640 points for it, a 1024 × 768 screen
/// about 990 × 700. The notes' "Rewrite notes" and the ask bar stay whole and inside the page: the bar is 600 wide
/// where there's room and narrower where there isn't (the engine chip down to its icon), never cut off at its sides.
/// </summary>
public class SmallWindowTests
{
    static ViewModels.LibraryModel WithNotes()
    {
        var m = Demo.Library();
        m.Notes = AiDemo.NotesIdle();
        return m;
    }

    static Rect In(Visual v, Visual root) => new(v.TranslatePoint(new Point(0, 0), root)!.Value, v.Bounds.Size);

    [AvaloniaTheory]
    [InlineData(992, 696)]
    [InlineData(1248, 640)]
    [InlineData(1504, 784)]
    public void The_notes_page_keeps_its_buttons_and_ask_bar_whole(double width, double height)
    {
        try
        {
            ((App)Application.Current!).UseSkin(SkinKind.Win);
            var view = new WinLibrary { DataContext = WithNotes() };
            var window = new Window { Width = width, Height = height, Content = view };
            window.Show();
            window.UpdateLayout();

            var page = view.FindControl<Panel>("AskRoom")!.GetVisualParent<Grid>()!;
            var bounds = In(page, view);
            var bar = view.GetVisualDescendants().OfType<WinAiAskBar>().Single();
            var barAt = In(bar, view);
            Assert.True(barAt.Left >= bounds.Left + 23.5 && barAt.Right <= bounds.Right - 23.5, $"{width}: the ask bar ({barAt}) runs past the page ({bounds})");
            Assert.Equal(Math.Min(WinAiAskBar.Wide, bounds.Width - 48), barAt.Width, 0.5);
            Assert.Equal(barAt.Width < 480, bar.Compact);
            // Every part of the bar is inside it: the send button isn't cut off.
            foreach (var b in bar.GetVisualDescendants().OfType<Button>().Where(b => b.IsEffectivelyVisible))
                Assert.True(In(b, view).Right <= barAt.Right + 0.5, $"{width}: a button in the ask bar runs past it");

            var rewrite = view.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "RewriteButton");
            Assert.True(rewrite.IsEffectivelyVisible);
            Assert.True(In(rewrite, view).Right <= bounds.Right + 0.5, $"{width}: Rewrite notes runs past the page");
            window.Close();
        }
        finally
        {
            ((App)Application.Current!).UseSkin(SkinKind.Mac);
        }
    }

    [AvaloniaFact]
    public void Small_window_shots()
    {
        foreach (var t in new[] { ThemeVariant.Light, ThemeVariant.Dark })
            Shot.Take("win-04-full-app-small", SkinKind.Win, t, () => new WinLibrary { DataContext = WithNotes(), Width = 992, Height = 696 });
    }
}
