using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StudyStash.App.Controls.Rich;
using StudyStash.Core.Rich;

namespace StudyStash.App.Tests;

/// <summary>A flowchart drawn in the app: every sample in both looks, light and dark; fitted to narrow columns;
/// named for screen readers; and a theme switch that only recolours.</summary>
public class DiagramViewTests
{
    public static TheoryData<string> Samples => [.. RichDemo.Diagrams.Select(d => d.Title)];

    static string SourceOf(string title) => RichDemo.Diagrams.Single(d => d.Title == title).Source;

    /// <summary>Shows the views in a column of <paramref name="width"/>, lets them lay out and draw, and hands back
    /// the window (still open) for a look.</summary>
    static Window Show(double width, SkinKind skin, ThemeVariant variant, params Control[] views)
    {
        Skin.UseTheme(ColourThemes.Default);
        ((App)Application.Current!).UseSkin(skin);
        var column = new StackPanel { Width = width, Spacing = 12, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        foreach (var v in views) column.Children.Add(v);
        var window = new Window { Width = width + 40, Height = 3000, RequestedThemeVariant = variant, Content = column };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        Assert.NotNull(window.CaptureRenderedFrame());
        return window;
    }

    [AvaloniaTheory]
    [MemberData(nameof(Samples))]
    public void Every_sample_measures_and_draws_in_both_looks_light_and_dark(string title)
    {
        foreach (var skin in new[] { SkinKind.Mac, SkinKind.Win })
            foreach (var variant in new[] { ThemeVariant.Light, ThemeVariant.Dark })
            {
                var view = new DiagramView { Chart = Flowchart.Parse(SourceOf(title)) };
                var window = Show(620, skin, variant, view);
                Assert.NotNull(view.Scene);
                Assert.True(view.Bounds.Width is > 0 and <= 620, $"{title}: {view.Bounds.Width} wide");
                Assert.True(view.Bounds.Height > 0);
                Assert.Equal(1, view.Scale, 3);
                window.Close();
            }
    }

    [AvaloniaTheory]
    [MemberData(nameof(Samples))]
    public void A_narrow_column_scales_the_picture_but_never_below_the_floor(string title)
    {
        var view = new DiagramView { Chart = Flowchart.Parse(SourceOf(title)) };
        var window = Show(300, SkinKind.Mac, ThemeVariant.Light, view);
        var scene = view.Scene!;
        Assert.True(view.Scale >= DiagramView.MinScale);
        Assert.True(view.Bounds.Width <= 300.5);
        if (scene.Width * DiagramView.MinScale > 300)
        {
            // Past the floor the picture keeps its size and scrolls sideways.
            var scroller = view.GetVisualDescendants().OfType<ScrollViewer>().First();
            Assert.Equal(DiagramView.MinScale, view.Scale, 3);
            Assert.True(scroller.Extent.Width > scroller.Viewport.Width);
        }
        else Assert.Equal(Math.Min(1, 300 / scene.Width), view.Scale, 3);
        window.Close();
    }

    [AvaloniaFact]
    public void A_wide_left_to_right_chart_turns_top_down_in_its_column()
    {
        var chart = Flowchart.Parse("flowchart LR\n  A[Wash your hands] --> B[Check the order] --> C{Right patient?} -->|yes| D[Give the dose] --> E[Document it]\n  C -->|no| F[Stop and ask]");
        var view = new DiagramView { Chart = chart };
        var window = Show(620, SkinKind.Mac, ThemeVariant.Light, view);
        Assert.Equal(ChartDirection.TopDown, view.Scene!.Direction);
        var wide = new DiagramView { Chart = chart };
        var roomy = Show(2000, SkinKind.Mac, ThemeVariant.Light, wide);
        Assert.Equal(ChartDirection.LeftRight, wide.Scene!.Direction);
        window.Close();
        roomy.Close();
    }

    [AvaloniaFact]
    public void Screen_readers_hear_the_diagrams_words()
    {
        var view = new DiagramView { Chart = Flowchart.Parse(RichDemo.PainReassess) };
        Assert.Equal(
            "Diagram: Assess pain on a 0 to 10 scale, Score above 4?, yes, Give the prescribed analgesic, no, Reposition and use non-drug comfort measures, Reassess in 30 to 60 minutes, still in pain",
            AutomationProperties.GetName(view));
        Assert.Equal("Open larger", ToolTip.GetTip(view));
    }

    [AvaloniaFact]
    public void Switching_light_and_dark_recolours_without_laying_out_again()
    {
        var view = new DiagramView { Chart = Flowchart.Parse(RichDemo.BloodFlow) };
        var window = Show(620, SkinKind.Mac, ThemeVariant.Light, view);
        var scene = view.Scene;
        var canvas = view.GetVisualDescendants().OfType<DiagramCanvas>().Single();
        Assert.Equal(Color.Parse("#1D1D1F"), ((ISolidColorBrush)canvas.Ink!).Color);
        window.RequestedThemeVariant = ThemeVariant.Dark;
        Dispatcher.UIThread.RunJobs();
        Assert.NotNull(window.CaptureRenderedFrame());
        Assert.Same(scene, view.Scene);
        Assert.Equal(Color.Parse("#F5F5F7"), ((ISolidColorBrush)canvas.Ink!).Color);
        window.Close();
    }

    [AvaloniaFact]
    public void The_whole_picture_answers_a_click()
    {
        var view = new DiagramView { Chart = Flowchart.Parse(RichDemo.SearchTree) };
        var window = Show(620, SkinKind.Mac, ThemeVariant.Light, view);
        // A point inside the picture with no line or box on it (the top-left corner) still finds the view.
        var corner = view.TranslatePoint(new Point(1, 1), window)!.Value;
        var hit = window.InputHitTest(corner);
        Assert.True(hit is Visual v && (ReferenceEquals(v, view) || v.GetSelfAndVisualAncestors().Contains(view)), $"hit {hit}");
        window.Close();
    }

    [AvaloniaFact]
    public void Tones_follow_the_variant_and_the_accent_follows_the_theme()
    {
        var (lightFill, lightStroke) = DiagramColours.Of(Tone.Red, false, Colors.Teal, null);
        var (darkFill, darkStroke) = DiagramColours.Of(Tone.Red, true, Colors.Teal, null);
        Assert.True(Luma(lightFill) > Luma(lightStroke), "a pale fill and a strong stroke in light");
        Assert.True(Luma(darkFill) < Luma(darkStroke), "a deep fill and a soft stroke in dark");
        var accent = Color.Parse("#3366CC");
        Assert.Equal(accent, DiagramColours.Of(Tone.Accent, false, accent, null).Stroke);
        Assert.Equal(Colors.Pink, DiagramColours.Of(Tone.Accent, false, accent, Colors.Pink).Fill);
    }

    static double Luma(Color c) => 0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B;
}
