using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
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
        DiagramsReady.Wait(window);
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
        Assert.Equal(DiagramView.Help, AutomationProperties.GetHelpText(view));
        Assert.Null(ToolTip.GetTip(view)); // no tooltip popping up over the whole diagram while reading
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
    public async Task A_download_draws_a_chart_away_from_the_window_from_the_scene_the_note_shows()
    {
        var chart = Flowchart.Parse(RichDemo.PainReassess);
        var view = new DiagramView { Chart = chart };
        var window = Show(620, SkinKind.Mac, ThemeVariant.Light, view);
        var font = view.GetVisualDescendants().OfType<DiagramCanvas>().Single().Font!;
        bool? onUiThread = null;
        var scene = await Task.Run(() =>
        {
            onUiThread = Dispatcher.UIThread.CheckAccess();
            return SceneCache.Laid(chart, font);
        }, TestContext.Current.CancellationToken);
        Assert.False(onUiThread);
        Assert.Same(view.Scene, scene);
        window.Close();
    }

    [AvaloniaFact]
    public void The_whole_picture_answers_a_click()
    {
        var view = new DiagramView { Chart = Flowchart.Parse(RichDemo.SearchTree) };
        var window = Show(620, SkinKind.Mac, ThemeVariant.Light, view);
        // A point inside the picture with no line or box on it (the top-left corner) still finds the view.
        var picture = view.GetVisualDescendants().OfType<DiagramCanvas>().Single();
        var corner = picture.TranslatePoint(new Point(1, 1), window)!.Value;
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

    /// <summary>At 100%, 125%, 150%, 175% and 200%, a box's outline lands with its outer edge on the screen's own
    /// pixels (a whole point is 1.5 pixels at 150%: rounding to points alone would leave it straddling two), and a
    /// word starts on a whole pixel.</summary>
    [Theory]
    [InlineData(1.0)]
    [InlineData(1.25)]
    [InlineData(1.5)]
    [InlineData(1.75)]
    [InlineData(2.0)]
    public void Outlines_and_words_snap_to_the_screens_own_pixels(double scaling)
    {
        foreach (double x in new[] { 11.0, 12.3, 37.8, 101.5 })
        {
            var r = DiagramCanvas.Snap(new Rect(x, x + 3.2, 80.4, 36.6), scaling, hairline: true);
            // The outline is 1 point wide, centred on the rectangle's edge: its outside edge is half a point out.
            Assert.Equal(0, Frac((r.X - 0.5) * scaling), 6);
            Assert.Equal(0, Frac((r.Y - 0.5) * scaling), 6);
            Assert.Equal(0, Frac((r.Right + 0.5) * scaling), 6);
            Assert.Equal(0, Frac((r.Bottom + 0.5) * scaling), 6);
            Assert.InRange(r.X - 0.5, x - 1 / scaling, x + 1 / scaling);
            Assert.Equal(0, Frac(DiagramCanvas.OnPixel(x, scaling) * scaling), 6);
        }
    }

    static double Frac(double v) => Math.Abs(v - Math.Round(v));

    /// <summary>A picture of a chart drawn into a bitmap at 200% (as the self-test pictures the Mac's windows) has its
    /// boxes, words and lines where they are at 100%, twice as many pixels across: nothing drawn after the lines'
    /// clip lands twice as far out.</summary>
    [AvaloniaFact]
    public void A_chart_drawn_into_a_bitmap_at_200_percent_keeps_its_boxes_in_place()
    {
        var view = new DiagramView { Chart = Flowchart.Parse(RichDemo.NursingProcess) };
        var window = Show(620, SkinKind.Win, ThemeVariant.Light, view);
        var ink = new Dictionary<int, PixelRect>();
        foreach (int times in new[] { 1, 2 })
        {
            var size = new PixelSize((int)Math.Ceiling(view.Bounds.Width) * times, (int)Math.Ceiling(view.Bounds.Height) * times);
            using var bmp = new RenderTargetBitmap(size, new Vector(96 * times, 96 * times));
            bmp.Render(view);
            var buffer = new byte[size.Width * size.Height * 4];
            var pinned = System.Runtime.InteropServices.GCHandle.Alloc(buffer, System.Runtime.InteropServices.GCHandleType.Pinned);
            try
            {
                bmp.CopyPixels(new PixelRect(size), pinned.AddrOfPinnedObject(), buffer.Length, size.Width * 4);
            }
            finally
            {
                pinned.Free();
            }
            int left = size.Width, top = size.Height, right = -1, bottom = -1, count = 0;
            for (int i = 0; i < buffer.Length; i += 4)
            {
                if (buffer[i + 3] < 40) continue;
                int x = i / 4 % size.Width, y = i / 4 / size.Width;
                (left, top, right, bottom) = (Math.Min(left, x), Math.Min(top, y), Math.Max(right, x), Math.Max(bottom, y));
                count++;
            }
            Assert.True(count > 1000 * times * times, $"{times}x drew {count} pixels");
            ink[times] = new PixelRect(left, top, right - left + 1, bottom - top + 1);
        }
        // Within a few pixels (type is shaped a little differently at each size); a misplaced box is off by hundreds.
        Assert.InRange(ink[2].X, ink[1].X * 2 - 8, ink[1].X * 2 + 8);
        Assert.InRange(ink[2].Y, ink[1].Y * 2 - 8, ink[1].Y * 2 + 8);
        Assert.InRange(ink[2].Width, ink[1].Width * 2 - 8, ink[1].Width * 2 + 8);
        Assert.InRange(ink[2].Height, ink[1].Height * 2 - 8, ink[1].Height * 2 + 8);
        window.Close();
    }

    /// <summary>Before a test's app goes (and its fonts with it), the layout under way finishes and the ones queued
    /// behind it never start: nothing measures words with fonts that are gone.</summary>
    [AvaloniaFact]
    public void Forgetting_the_charts_waits_for_the_layout_under_way_and_drops_the_ones_queued()
    {
        string stamp = Guid.NewGuid().ToString("N")[..6];
        var first = Flowchart.Parse($"flowchart TD\n  A[First {stamp}] --> B[Done]");
        var second = Flowchart.Parse($"flowchart TD\n  A[Second {stamp}] --> B[Done]");
        var started = new System.Collections.Concurrent.ConcurrentQueue<Flowchart>();
        using var release = new ManualResetEventSlim(false);
        SceneCache.Laying = chart =>
        {
            started.Enqueue(chart);
            release.Wait(TimeSpan.FromSeconds(30));
        };
        try
        {
            Assert.NotNull(SceneCache.Find(first, first.ToSource(), FontFamily.Default, null).Laying);
            Assert.NotNull(SceneCache.Find(second, second.ToSource(), FontFamily.Default, null).Laying);
            var until = DateTime.UtcNow.AddSeconds(10);
            while (started.IsEmpty && DateTime.UtcNow < until) Thread.Sleep(5);

            Assert.False(SceneCache.Forget(TimeSpan.FromMilliseconds(100)));
            release.Set();
            Assert.True(SceneCache.Forget(TimeSpan.FromSeconds(30)));
            Assert.Same(first, Assert.Single(started));
            Assert.False(SceneCache.Busy);

            // Asked for again, a forgotten chart is laid out afresh.
            SceneCache.Laying = null;
            var again = SceneCache.Find(first, first.ToSource(), FontFamily.Default, null);
            Assert.True(again.Laying is Task<DiagramScene?> job && job.Wait(TimeSpan.FromSeconds(30)) && job.Result is not null);
        }
        finally
        {
            SceneCache.Laying = null;
            release.Set();
        }
    }
}
