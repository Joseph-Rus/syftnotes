using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using StudyStash.App.Controls;
using StudyStash.App.Services;
using StudyStash.App.ViewModels;
using StudyStash.App.Views;
using StudyStash.Core;
using static StudyStash.App.Controls.Keyframes;

namespace StudyStash.App.Tests;

/// <summary>
/// Syft Notes' pixel s where the app shows it (Controls/Syft): the keyframes are the motion reference's, the loaders keep
/// moving with the UI thread busy, and the splash goes when it should.
/// </summary>
public class SyftMotionTests
{
    // ---- The keyframes, against syft-motion.html's own numbers ----

    [Fact]
    public void The_keyframes_are_the_references()
    {
        // L1: tr 2s linear, opacity 0 → 1 by 6%, held to 30%, out by 52%; p5 starts 0.375 s late.
        Assert.Equal(1, SyftLoader.Traces[0].At(0.12).Opacity, 3);
        Assert.Equal(0.5, SyftLoader.Traces[0].At(0.82).Opacity, 3);
        Assert.Equal(0, SyftLoader.Traces[0].At(1.5).Opacity, 3);
        Assert.Equal(0, SyftLoader.Traces[5].At(0.3).Opacity, 3); // still in its delay (backwards: the 0% keyframe)
        Assert.Equal(1, SyftLoader.Traces[0].At(2.12).Opacity, 3); // round again
        // L2: bb 1.2s, the top of the hop at 30%: 14 up and a quarter turn; the third bit 0.24 s behind.
        var top = SyftLoader.Hops[0].At(0.36);
        Assert.Equal(-14, top.Y, 3);
        Assert.Equal(90, top.Turn, 3);
        Assert.Equal(Pose.Rest, SyftLoader.Hops[2].At(0.2));
        // L3: pg 3s ease-out, scale .4 → 1 by 6%.
        Assert.Equal(0.4, SyftLoader.Fills[0].At(0).Scale, 3);
        Assert.Equal(1, SyftLoader.Fills[0].At(0.18).Opacity, 3);
        // S1: p18 lands first (no delay), from 110 above, with its 1.5 bounce at 29%.
        Assert.Equal(-110, SyftSplash.SortCells[18].At(0).Y, 3);
        Assert.Equal(0, SyftSplash.SortCells[18].At(1.3).Y, 3);
        Assert.Equal(-1.5, SyftSplash.SortCells[18].At(1.45).Y, 3);
        Assert.Equal(1, SyftSplash.SortCells[18].At(30).Opacity, 3); // played once, it holds
        // S2: a cell overshoots to 1.18 at 7%, path order 0.05 s apart.
        Assert.Equal(1.18, SyftSplash.SieveCells[0].At(0.5 + 0.35).Scale, 3);
        Assert.Equal(0, SyftSplash.SieveCells[3].At(0.6).Opacity, 3);
        // S3: the coral bit's arc peaks 22 up at 40% and it's home by 50%.
        Assert.Equal(-22, SyftSplash.PopHop.At(1.8).Y, 3);
        Assert.Equal(0, SyftSplash.PopHop.At(2.25).Y, 3);
        // CSS's ease-out and a bezier solved for x.
        Assert.Equal(0.5, Ease.Linear.Apply(0.5), 6);
        Assert.InRange(Ease.Out.Apply(0.5), 0.68, 0.69);
    }

    // ---- What would break for a student ----

    static byte[] Frame(Window window)
    {
        var frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("nothing rendered");
        using var fb = frame.Lock();
        var bytes = new byte[fb.RowBytes * fb.Size.Height];
        Marshal.Copy(fb.Address, bytes, 0, bytes.Length);
        return bytes;
    }

    /// <summary>The loaders are moved by the render thread: with the UI thread held up (a lecture's notes being built),
    /// the trail still runs on.</summary>
    [AvaloniaFact]
    public void A_loader_moves_on_while_the_UI_thread_is_busy()
    {
        string? still = Environment.GetEnvironmentVariable("STUDYSTASH_STILL");
        Environment.SetEnvironmentVariable("STUDYSTASH_STILL", null);
        Platform.Motion.Override = false;
        try
        {
            var loader = new SyftLoader { Kind = SyftLoaderKind.PathTrace, Size = 48 };
            var window = new Window { Width = 80, Height = 80, Content = loader };
            window.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.True(loader.Turning);
            var first = Frame(window);
            Thread.Sleep(250); // the UI thread busy, nothing posted to it
            var later = Frame(window);
            Assert.NotEqual(first, later);
            window.Close();
            Assert.False(loader.Turning);
        }
        finally
        {
            Platform.Motion.Override = null;
            Environment.SetEnvironmentVariable("STUDYSTASH_STILL", still);
        }
    }

    /// <summary>The library window's splash never stays: ready at once, it still plays its 1.2 s; never ready, it goes
    /// at 4 s anyway.</summary>
    [AvaloniaFact]
    public async Task The_splash_goes_once_ready_but_not_before_its_minimum_and_by_its_maximum()
    {
        foreach (bool ready in new[] { true, false })
        {
            var splash = new SyftSplash { IsReady = ready };
            var window = new Window { Width = 300, Height = 400, Content = splash };
            var clock = Stopwatch.StartNew();
            double? finished = null;
            splash.Finished += () => finished = clock.Elapsed.TotalSeconds;
            window.Show();
            while (finished is null && clock.Elapsed.TotalSeconds < 6)
                await Task.Delay(10, TestContext.Current.CancellationToken);
            window.Close();
            Assert.NotNull(finished);
            if (ready) Assert.InRange(finished!.Value, SyftSplash.MinSeconds, SyftSplash.MinSeconds + 0.8);
            else Assert.InRange(finished!.Value, SyftSplash.MaxSeconds, SyftSplash.MaxSeconds + 0.8);
        }
    }

    /// <summary>The mark and loaders say what they are: "Syft Notes", and "Loading", politely.</summary>
    [AvaloniaFact]
    public void The_mark_and_loaders_say_what_they_are()
    {
        static string Said(Control c) => Avalonia.Automation.Peers.ControlAutomationPeer.CreatePeerForElement(c).GetName() ?? "";
        Assert.Equal("Syft Notes", Said(new SyftMark()));
        Assert.Equal("Syft Notes", Said(new AppIcon()));
        var loader = new SyftLoader();
        Assert.Equal("Loading", Said(loader));
        Assert.Equal(Avalonia.Automation.AutomationLiveSetting.Polite, Avalonia.Automation.AutomationProperties.GetLiveSetting(loader));
    }
}
