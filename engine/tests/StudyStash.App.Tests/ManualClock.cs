namespace StudyStash.App.Tests;

/// <summary>
/// A clock a test moves on by hand: <see cref="Advance"/> fires every timer that has come due, on the test's own thread,
/// so a wait through <see cref="Services.Kit.Poll"/> ends exactly when the test says and never on its own.
/// </summary>
public sealed class ManualClock : TimeProvider
{
    readonly Lock gate = new();
    readonly List<Timer> timers = [];
    DateTimeOffset now = new(2026, 11, 2, 9, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow()
    {
        lock (gate) return now;
    }

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    public override long GetTimestamp() => GetUtcNow().UtcTicks;

    /// <summary>Timers waiting to fire.</summary>
    public int Waiting
    {
        get
        {
            lock (gate) return timers.Count(t => t.Due is not null);
        }
    }

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new Timer(this, callback, state);
        timer.Change(dueTime, period);
        lock (gate) timers.Add(timer);
        return timer;
    }

    /// <summary>Moves time on by <paramref name="by"/>, firing each timer that comes due on the way, in order.</summary>
    public void Advance(TimeSpan by)
    {
        DateTimeOffset until;
        lock (gate) until = now + by;
        while (true)
        {
            Timer? next;
            lock (gate)
            {
                next = timers.Where(t => t.Due is { } d && d <= until).OrderBy(t => t.Due).FirstOrDefault();
                if (next is null)
                {
                    now = until;
                    return;
                }
                now = next.Due!.Value;
                next.Due = next.Period > TimeSpan.Zero ? now + next.Period : null;
            }
            next.Fire();
        }
    }

    sealed class Timer(ManualClock clock, TimerCallback callback, object? state) : ITimer
    {
        public DateTimeOffset? Due { get; set; }
        public TimeSpan Period { get; private set; }

        public void Fire() => callback(state);

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            lock (clock.gate)
            {
                Due = dueTime == Timeout.InfiniteTimeSpan ? null : clock.now + dueTime;
                Period = period == Timeout.InfiniteTimeSpan ? TimeSpan.Zero : period;
            }
            return true;
        }

        public void Dispose()
        {
            lock (clock.gate)
            {
                Due = null;
                clock.timers.Remove(this);
            }
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
