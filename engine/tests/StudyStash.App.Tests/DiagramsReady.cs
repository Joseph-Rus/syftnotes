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
    public static void Wait(TopLevel top)
    {
        for (int round = 0; round < 8; round++)
        {
            Dispatcher.UIThread.RunJobs();
            Assert.NotNull(top.CaptureRenderedFrame());
            if (!SceneCache.Busy && !top.GetVisualDescendants().OfType<DiagramView>().Any(d => d.IsLaying)) return;
            Assert.True(SceneCache.Settle(TimeSpan.FromSeconds(60)), "a diagram took over a minute to lay out");
        }
    }
}
