using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StudyStash.App.ViewModels;
using StudyStash.App.Views;

namespace StudyStash.App.Tests;

/// <summary>The library window as the student uses it: Unsorted always in the sidebar, deleting a lecture, and an
/// answer that has its own room above the ask bar.</summary>
public class LibraryWindowTests
{
    static Window Host(Control content, SkinKind skin)
    {
        ((App)Application.Current!).UseSkin(skin);
        var w = new Window { Width = 1280, Height = 800, RequestedThemeVariant = ThemeVariant.Light, Content = content };
        w.Show();
        Dispatcher.UIThread.RunJobs();
        return w;
    }

    static Control Library(SkinKind skin, LibraryModel m) => skin == SkinKind.Mac ? new MacLibrary { DataContext = m } : new WinLibrary { DataContext = m };

    static TextBlock Text(Control root, string text) =>
        root.GetVisualDescendants().OfType<TextBlock>().First(t => t.Text == text && t.IsEffectivelyVisible);

    [AvaloniaTheory]
    [InlineData(SkinKind.Mac)]
    [InlineData(SkinKind.Win)]
    public void Unsorted_is_always_in_the_sidebar_under_the_classes_even_empty(SkinKind skin)
    {
        var m = new LibraryModel();
        m.Classes.Add(new ClassItem { Name = "CS 101", Count = 3 });
        var view = Library(skin, m);
        var w = Host(view, skin);
        try
        {
            var unsorted = Text(view, "Unsorted");
            var cs = Text(view, "CS 101");
            Assert.True(unsorted.TranslatePoint(default, view)!.Value.Y > cs.TranslatePoint(default, view)!.Value.Y, "Unsorted sits under the classes");
            var row = unsorted.FindAncestorOfType<Button>()!;
            Assert.Contains(row.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "0");

            // Lectures the library couldn't place: their count shows, and one click opens them.
            m.Unsorted.Count = 2;
            Dispatcher.UIThread.RunJobs();
            Assert.Contains(row.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "2");
            ClassItem? picked = null;
            m.OnClass = c => picked = c;
            row.Command!.Execute(row.CommandParameter);
            Assert.Same(m.Unsorted, picked);
            Assert.True(picked!.IsUnsorted);
        }
        finally
        {
            w.Close();
        }
    }
}
