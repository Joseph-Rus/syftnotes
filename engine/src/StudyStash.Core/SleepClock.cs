using System.Diagnostics;
using System.Runtime.InteropServices;

namespace StudyStash.Core;

/// <summary>
/// Whether the computer slept while something ran: the wall clock kept going while the awake clock (which stops in
/// sleep) didn't. A lecture's notes being written when a laptop's lid closed fail on waking (the engine's connection
/// dropped, or its time ran out); knowing it slept, the library tries them again instead of filing the lecture
/// without notes. Both clocks can be swapped, so a test can pretend the computer slept.
/// </summary>
public sealed record SleepClock(Func<DateTime> Wall, Func<TimeSpan> Awake)
{
    /// <summary>This computer's own clocks.</summary>
    public static readonly SleepClock System = new(() => DateTime.UtcNow, AwakeTime);

    /// <summary>More than this between the two clocks is sleep, not a busy moment.</summary>
    public static readonly TimeSpan Slack = TimeSpan.FromMinutes(1);

    /// <summary>Starts watching: the answer says whether the computer has slept since.</summary>
    public Func<bool> Start()
    {
        DateTime wall = Wall();
        TimeSpan awake = Awake();
        return () => Wall() - wall - (Awake() - awake) > Slack;
    }

    /// <summary>Time the computer has been awake: the Stopwatch's clock on a Mac and Linux (it stops in sleep), and on
    /// Windows the unbiased interrupt time (its Stopwatch keeps counting through sleep).</summary>
    static TimeSpan AwakeTime()
    {
        if (OperatingSystem.IsWindows() && QueryUnbiasedInterruptTime(out ulong hundredNs)) return TimeSpan.FromTicks((long)hundredNs);
        return TimeSpan.FromSeconds(Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency);
    }

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool QueryUnbiasedInterruptTime(out ulong time);
}
