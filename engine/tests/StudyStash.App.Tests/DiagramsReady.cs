using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StudyStash.App.Controls.Rich;

namespace StudyStash.App.Tests;

/// <summary>Lets the flowcharts a window shows finish laying out (in the background, as the app does) and draw, for
/// a test or a picture that looks at them.</summary>
static class DiagramsReady
{
    static bool Waiting(DiagramView d) => d.IsLaying || (d.IsEffectivelyVisible && d.Chart is not null && d.Scene is null && !d.HasFailed);

    public static void Wait(TopLevel top)
    {
        var until = DateTime.UtcNow.AddSeconds(60);
        while (true)
        {
            Dispatcher.UIThread.RunJobs();
            Assert.NotNull(top.CaptureRenderedFrame());
            // Done when every diagram on show has its picture (or couldn't have one): nothing busy and nothing laying out
            // isn't enough, since a finished layout still has to reach its view from the thread pool.
            if (!SceneCache.Busy && !top.GetVisualDescendants().OfType<DiagramView>().Any(Waiting)) return;
            Assert.True(DateTime.UtcNow < until, "a diagram took over a minute to lay out and draw");
            // A finished layout tells its view from the thread pool: give that a moment to arrive, then look again.
            if (!SceneCache.Settle(TimeSpan.FromSeconds(60))) continue;
            Thread.Sleep(5);
        }
    }
}
