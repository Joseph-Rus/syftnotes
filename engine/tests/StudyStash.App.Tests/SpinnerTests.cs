using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Spinner = StudyStash.App.Controls.Spinner;

namespace StudyStash.App.Tests;

/// <summary>
/// The spinner a student watches while something loads, moved on by the render thread so it keeps going however busy
/// the app is. What would show if it went wrong: a blank where it should be, one that stands still, one left in the
/// last theme's colour (dark on dark after switching to dark), or one that jumps once every turn.
/// </summary>
public class SpinnerTests
{
    /// <summary>The window's frame as bytes, and how many of its pixels are near black and near white (the ground is mid grey).</summary>
    static (byte[] Bytes, int Dark, int Light) Frame(Window window)
    {
        var frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("nothing rendered");
        using var fb = frame.Lock();
        var bytes = new byte[fb.RowBytes * fb.Size.Height];
        Marshal.Copy(fb.Address, bytes, 0, bytes.Length);
        int dark = 0, light = 0;
        for (int i = 0; i + 3 < bytes.Length; i += 4)
        {
            int lo = Math.Min(bytes[i], Math.Min(bytes[i + 1], bytes[i + 2])), hi = Math.Max(bytes[i], Math.Max(bytes[i + 1], bytes[i + 2]));
            if (hi < 80) dark++;
            if (lo > 180) light++;
        }
        return (bytes, dark, light);
    }

    [AvaloniaFact]
    public void A_spinner_draws_moves_on_and_follows_the_theme()
    {
        string? still = Environment.GetEnvironmentVariable("STUDYSTASH_STILL");
        Environment.SetEnvironmentVariable("STUDYSTASH_STILL", null); // other tests' pictures set it for the whole run
        try
        {
            var spinner = new Spinner { Width = 18, Height = 18 };
            spinner.Bind(Spinner.ForegroundProperty, spinner.GetResourceObservable("TestInk"));
            var window = new Window
            {
                Width = 60, Height = 60, Background = new SolidColorBrush(Color.FromRgb(128, 128, 128)), RequestedThemeVariant = ThemeVariant.Light,
                Content = spinner,
            };
            window.Resources.ThemeDictionaries[ThemeVariant.Light] = new ResourceDictionary { ["TestInk"] = new SolidColorBrush(Colors.Black) };
            window.Resources.ThemeDictionaries[ThemeVariant.Dark] = new ResourceDictionary { ["TestInk"] = new SolidColorBrush(Colors.White) };
            window.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.True(spinner.Turning);

            var first = Frame(window);
            Assert.True(first.Dark >= 6, $"the spinner drew {first.Dark} pixels of its colour");
            Thread.Sleep(150);
            var later = Frame(window);
            Assert.True(later.Dark >= 6);
            Assert.NotEqual(first.Bytes, later.Bytes); // it moved on

            // Switched to dark while it turns: drawn in the dark theme's colour from then on.
            window.RequestedThemeVariant = ThemeVariant.Dark;
            Dispatcher.UIThread.RunJobs();
            var dark = Frame(window);
            Assert.Equal(0, dark.Dark);
            Assert.True(dark.Light >= 6, $"after the switch the spinner drew {dark.Light} pixels of the dark theme's colour");
            window.Close();
        }
        finally
        {
            Environment.SetEnvironmentVariable("STUDYSTASH_STILL", still);
        }
    }

    [Fact]
    public void Its_arc_never_jumps_and_stays_between_its_shortest_and_longest()
    {
        // A millisecond at a time over three turns, both ends move on smoothly (the quickest, mid-stretch, is well under
        // a degree a millisecond), including where one period ends and the next begins.
        static double Step(double from, double to) => ((to - from) % 360 + 540) % 360 - 180;
        var (start, sweep) = Spinner.Pose(0);
        for (double t = 0.001; t < 3 * Spinner.Period; t += 0.001)
        {
            var (s, w) = Spinner.Pose(t);
            Assert.InRange(w, Spinner.Shortest - 1e-6, Spinner.Longest + 1e-6);
            Assert.InRange(Step(start, s), 0, 1.5);
            Assert.InRange(Step(start + sweep, s + w), 0, 1.5);
            (start, sweep) = (s, w);
        }
    }
}
