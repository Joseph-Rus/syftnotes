using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using StudyStash.App.Controls;

namespace StudyStash.App.Tests;

/// <summary>Where the app measures things the way the design's browser does: a shadow's blur spreads as far as CSS
/// spreads it, and a chat bubble that wraps takes its whole width.</summary>
public class MacMetricsTests
{
    static void Mac()
    {
        Skin.UseTheme(ColourThemes.Default);
        ((App)Application.Current!).UseSkin(SkinKind.Mac);
    }

    // The normal distribution's tail, from erf (Abramowitz and Stegun 7.1.26, good to 1.5e-7).
    static double Tail(double z)
    {
        double x = Math.Abs(z) / Math.Sqrt(2), t = 1 / (1 + 0.3275911 * x);
        double erf = 1 - (((((1.061405429 * t - 1.453152027) * t) + 1.421413741) * t - 0.284496736) * t + 0.254829592) * t * Math.Exp(-x * x);
        return z >= 0 ? (1 - erf) / 2 : (1 + erf) / 2;
    }

    /// <summary>A CSS shadow of blur 20 fades out as a Gaussian of σ 10 past the box's edge; a black one drawn with
    /// <see cref="Skin.Blur"/> darkens a white ground by that much at each distance.</summary>
    [AvaloniaFact]
    public void A_design_shadow_spreads_as_far_as_in_CSS()
    {
        Mac();
        var box = new Border
        {
            Width = 100, Height = 100, Margin = new Thickness(100), Background = Brushes.Black,
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
            BoxShadow = new BoxShadows(new BoxShadow { Blur = Skin.Blur(20), Color = Colors.Black }),
        };
        var frame = GlassTests.Draw(new Panel { Background = Brushes.White, Children = { box } }, 400, 300);

        foreach (int d in new[] { 0, 4, 9, 14, 19 })
        {
            double expected = Tail((d + 0.5) / 10);
            double got = 1 - GlassTests.Pixel(frame, 200 + d, 150).R / 255.0;
            Assert.True(Math.Abs(got - expected) < 0.03, $"{d + 0.5} px past the edge the shadow is {got:F3}; CSS draws {expected:F3}");
        }
        Assert.Equal(0.0, Skin.Blur(0));
        Assert.Equal(0.0, Skin.Blur(1));
    }

    /// <summary>A chat bubble is as wide as its text on one line, and once the text wraps it takes all the width it
    /// may, as in the design's browser (Avalonia alone would shrink it to its longest line).</summary>
    [AvaloniaFact]
    public void A_bubble_that_wraps_takes_its_whole_width()
    {
        Mac();
        Border Bubble(string text) => new()
        {
            MaxWidth = 200, Padding = new Thickness(14, 8), HorizontalAlignment = HorizontalAlignment.Left,
            Child = new FitWidth { Child = new TextBlock { Text = text, FontSize = 13, TextWrapping = TextWrapping.Wrap } },
        };
        var shortOne = Bubble("Big-O?");
        var longOne = Bubble("Recursion traces and call-stack diagrams, like last week's. Big-O proofs won't be on it.");
        var window = new Window { Width = 300, Height = 300, Content = new StackPanel { Children = { shortOne, longOne } } };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            Assert.InRange(shortOne.Bounds.Width, 30, 100);
            Assert.Equal(200, longOne.Bounds.Width, 3);
            Assert.True(longOne.Bounds.Height > 40, "the long answer wraps");
        }
        finally
        {
            window.Close();
        }
    }
}
