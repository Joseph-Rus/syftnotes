using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.VisualTree;
using StudyStash.App.Controls;
using StudyStash.App.ViewModels;
using StudyStash.App.Views;

namespace StudyStash.App.Pictures;

/// <summary>
/// The quick search panel on a 1280 x 800 screen, with as many results as it ever shows and a passage with maths in
/// it (shots/quick-search-&lt;mac|win&gt;-&lt;light|dark&gt;.png): the panel sits a fifth down the screen and must end above
/// the bottom, its results scrolling inside it. Drawn off-screen, demo words only.
/// </summary>
public class QuickSearchShots
{
    static QuickModel Many()
    {
        var m = new QuickModel { Query = "k" };
        IBrush dot = Brushes.SteelBlue;
        m.Rows.Add(new QuickRow { Kind = QuickKind.Header, Title = "Notes", First = true });
        foreach (string t in new[] { "K-means clustering", "Choosing k in k-NN", "Cross-validation folds", "Bias and variance" })
            m.Rows.Add(new QuickRow { Kind = QuickKind.Lecture, Title = t, Meta = "CS 229 · Tue 21 Oct", Dot = dot });
        m.Rows.Add(new QuickRow { Kind = QuickKind.Header, Title = "Passages from notes" });
        foreach (string t in new[] { "What is the best value for k? Try several and compare", "A larger k smooths the boundary but blurs small groups", "Pick k by cross-validation on held-out data", "k-means needs k chosen before it starts" })
            m.Rows.Add(new QuickRow { Kind = QuickKind.Passage, Title = "…" + t + "…", Sub = "Choosing k in k-NN · Picking the number of neighbours", Dot = dot });
        m.Rows.Add(new QuickRow { Kind = QuickKind.Header, Title = "Folders" });
        foreach (string t in new[] { "CS 229", "STAT 101", "MATH 240" })
            m.Rows.Add(new QuickRow { Kind = QuickKind.Class, Title = t, Meta = "12 notes", Dot = dot });
        m.Rows.Add(new QuickRow { Kind = QuickKind.Header, Title = "Actions" });
        foreach (var (t, g) in new[] { ("Record CS 229", "mic"), ("Open library", "book_2"), ("What's due", "schedule"), ("Capture “k”", "inbox") })
            m.Rows.Add(new QuickRow { Kind = QuickKind.Action, Title = t, Glyph = g });
        m.SelectFirst();
        return m;
    }

    [AvaloniaTheory]
    [InlineData(SkinKind.Mac)]
    [InlineData(SkinKind.Win)]
    public void Many_results_end_above_the_bottom_of_a_small_screen(SkinKind skin)
    {
        foreach (var variant in new[] { ThemeVariant.Light, ThemeVariant.Dark })
        {
            ((App)Application.Current!).UseSkin(skin);
            var model = Many();
            // A 1280 x 800 screen with a taskbar or menu bar: 760 points of it usable.
            double work = 760, shadow = StudyStash.App.Windows.Floating.ShadowRoom;
            model.FitTo(work, shadow);
            Control view = skin == SkinKind.Mac ? new MacQuick { DataContext = model } : new WinQuick { DataContext = model };
            view.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left;
            view.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top;
            view.Margin = new Thickness(300, work / 5, 0, 0);
            var window = new Window { Width = 1280, Height = 800, RequestedThemeVariant = variant, Content = new Panel { Background = skin == SkinKind.Mac ? new SolidColorBrush(Color.Parse(variant == ThemeVariant.Dark ? "#2b2f3a" : "#cfd8e8")) : null, Children = { view } } };
            Look.Apply(window);
            window.Show();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            Assert.True(view.Bounds.Bottom <= work, $"{skin}: the panel ends at {view.Bounds.Bottom:0} on a screen {work:0} usable");
            string name = $"quick-search-{(skin == SkinKind.Mac ? "mac" : "win")}-{(variant == ThemeVariant.Dark ? "dark" : "light")}";
            Save(window, $"{name}.png");
            // A passage chosen: its line under the title takes the chosen row's muted colour, readable on the lilac.
            model.Move(4);
            QuickKeys.Reveal((UserControl)view, model);
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            var chosen = view.GetVisualDescendants().OfType<Button>().Single(b => b.DataContext == model.Selected && b.IsEffectivelyVisible);
            var line = chosen.GetVisualDescendants().OfType<TextBlock>().First(t => t.Classes.Contains("meta") && t.IsEffectivelyVisible);
            Assert.True(view.TryFindResource("SelectedMuted", variant, out object? muted), "no SelectedMuted");
            Assert.Equal(ColourOf(muted), ColourOf(line.Foreground));
            Save(window, $"{name}-passage.png");
            // Arrow down to the last row: it scrolls into view.
            for (int i = 0; i < 20; i++) model.Move(1);
            QuickKeys.Reveal((UserControl)view, model);
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            Save(window, $"{name}-end.png");
            // A new set of results (another letter typed, the panel opened again) starts at the top, its first row in view.
            var results = ((UserControl)view).FindControl<ScrollViewer>("Results")!;
            results.ScrollToEnd(); // as the wheel would (a Mac's last rows are chips, outside the list)
            window.UpdateLayout();
            Assert.True(results.Offset.Y > 0, $"{skin}: never scrolled");
            var rows = model.Rows.ToList();
            model.Rows.Clear();
            foreach (var r in rows) model.Rows.Add(r);
            model.SelectFirst();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            Assert.Equal(0, results.Offset.Y);
            window.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData(SkinKind.Mac)]
    [InlineData(SkinKind.Win)]
    public void A_passage_with_maths_reads_plainly(SkinKind skin)
    {
        foreach (var variant in new[] { ThemeVariant.Light, ThemeVariant.Dark })
        {
            ((App)Application.Current!).UseSkin(skin);
            var model = new QuickModel { Query = "value" };
            IBrush dot = Brushes.SteelBlue;
            model.Rows.Add(new QuickRow { Kind = QuickKind.Header, Title = "Passages from notes", First = true });
            model.Rows.Add(new QuickRow { Kind = QuickKind.Passage, Title = "…" + Shell.PlainPassage("What is the best value for $k$? Try $O(n^2)$ time…") + "…",
                Sub = $"Choosing k in k-NN · {StudyStash.App.ViewModels.PlainMath.Inline("Picking $k$ by cross-validation")}", Dot = dot });
            model.SelectFirst();
            Control view = skin == SkinKind.Mac ? new MacQuick { DataContext = model } : new WinQuick { DataContext = model };
            view.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left;
            view.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top;
            view.Margin = new Thickness(300, 152, 0, 0);
            var window = new Window { Width = 1280, Height = 500, RequestedThemeVariant = variant, Content = new Panel { Background = skin == SkinKind.Mac ? new SolidColorBrush(Color.Parse(variant == ThemeVariant.Dark ? "#2b2f3a" : "#cfd8e8")) : null, Children = { view } } };
            Look.Apply(window);
            window.Show();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            Save(window, $"quick-search-math-{(skin == SkinKind.Mac ? "mac" : "win")}-{(variant == ThemeVariant.Dark ? "dark" : "light")}.png");
            window.Close();
        }
    }

    static Color? ColourOf(object? paint) => paint switch
    {
        ISolidColorBrush b => b.Color,
        Color c => c,
        _ => null,
    };

    static void Save(Window window, string name)
    {
        var frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("nothing rendered");
        using var file = File.Create(Path.Combine(Shot.Dir, name));
        frame.Save(file, PngBitmapEncoderOptions.Default);
    }
}
