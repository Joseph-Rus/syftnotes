using System.Diagnostics;
using Avalonia;
using Avalonia.Controls.Shapes;
using Avalonia.Threading;

namespace StudyStash.App.Controls;

/// <summary>
/// The recording dot, breathing slowly (full to a third and back, once every 3.2 seconds): it's alive, and never glows.
/// It steps ten times a second, which reads as the same slow breath on a dot eight pixels across. An animation that runs
/// every frame (Avalonia's own, in a style) kept the recorder's window, with its shadow and blur, redrawn sixty times a
/// second for the whole lecture, which cost more of the computer than the microphone and the recorder together. It
/// only breathes while it's on screen.
/// </summary>
public sealed class PulseDot : Ellipse
{
    public static readonly TimeSpan Step = TimeSpan.FromMilliseconds(100);
    public const double Period = 3.2, Faintest = 0.35;

    readonly DispatcherTimer timer = new(DispatcherPriority.Background) { Interval = Step };
    readonly long began = Stopwatch.GetTimestamp();

    public PulseDot()
    {
        timer.Tick += (_, _) => Opacity = OpacityAt((Stopwatch.GetTimestamp() - began) / (double)Stopwatch.Frequency);
        _ = new Seen(this, Sync);
    }

    /// <summary>How opaque the dot is <paramref name="seconds"/> into its breath: 1 at the start, <see cref="Faintest"/> half a period later.</summary>
    public static double OpacityAt(double seconds) => Faintest + (1 - Faintest) * (1 + Math.Cos(2 * Math.PI * seconds / Period)) / 2;

    /// <summary>Whether it's breathing now: only while it's on screen.</summary>
    internal bool Breathing => timer.IsEnabled;

    void Sync(bool inView)
    {
        if (inView) timer.Start();
        else
        {
            timer.Stop();
            Opacity = 1;
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        timer.Stop();
    }
}
