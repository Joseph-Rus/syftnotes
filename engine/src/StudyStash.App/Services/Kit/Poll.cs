namespace StudyStash.App.Services.Kit;

/// <summary>
/// The app's one way to wait for something: ask again every so often until it's true (<see cref="UntilAsync"/>), or do
/// something every so often until told to stop (<see cref="Every"/>). Nothing else in the app sleeps or waits a fixed
/// time: a wait always has a real condition and a time it gives up at. The clock is a <see cref="TimeProvider"/>, so a
/// test moves time on by hand instead of waiting for it.
/// <para>Continuations run where the caller started (no <c>ConfigureAwait(false)</c>): a poll started on the UI thread
/// asks and answers there, as the hand-written loops it replaces did.</para>
/// </summary>
public static class Poll
{
    /// <summary>Asks <paramref name="check"/> now, then again every <paramref name="every"/>, until it says true (true)
    /// or <paramref name="timeout"/> has gone by (false; <see cref="Timeout.InfiniteTimeSpan"/> never gives up).
    /// <paramref name="ct"/> stops it with an <see cref="OperationCanceledException"/>.</summary>
    public static async Task<bool> UntilAsync(Func<CancellationToken, Task<bool>> check, TimeSpan every, TimeSpan timeout,
        CancellationToken ct = default, TimeProvider? clock = null)
    {
        clock ??= TimeProvider.System;
        long started = clock.GetTimestamp();
        bool forever = timeout == Timeout.InfiniteTimeSpan;
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            if (await check(ct)) return true;
            var left = forever ? every : timeout - clock.GetElapsedTime(started);
            if (left <= TimeSpan.Zero) return false;
            await Task.Delay(every < left ? every : left, clock, ct);
        }
    }

    /// <summary><see cref="UntilAsync(Func{CancellationToken, Task{bool}}, TimeSpan, TimeSpan, CancellationToken, TimeProvider?)"/>
    /// for a check that needn't wait for anything itself.</summary>
    public static Task<bool> UntilAsync(Func<bool> check, TimeSpan every, TimeSpan timeout, CancellationToken ct = default,
        TimeProvider? clock = null) =>
        UntilAsync(_ => Task.FromResult(check()), every, timeout, ct, clock);

    /// <summary>
    /// Waits <paramref name="every"/>, runs <paramref name="tick"/>, and again, until the returned
    /// <see cref="Poller"/> is disposed or <paramref name="keepGoing"/> (asked straight after each tick, on the same
    /// thread) says no. <see cref="Poller.Wake"/> cuts the current wait short. A tick that throws ends the poll, with the
    /// exception in <see cref="Poller.Done"/>: a tick that should carry on after a failure catches it itself.
    /// </summary>
    public static Poller Every(TimeSpan every, Func<CancellationToken, Task> tick, Func<bool>? keepGoing = null, TimeProvider? clock = null)
    {
        var poller = new Poller();
        poller.Done = poller.RunAsync(every, tick, keepGoing, clock ?? TimeProvider.System);
        return poller;
    }
}

/// <summary>A poll started by <see cref="Poll.Every"/>: dispose it to stop it.</summary>
public sealed class Poller : IDisposable
{
    readonly CancellationTokenSource stop = new();
    /// <summary>The wait between two ticks, cancelled to tick straight away.</summary>
    CancellationTokenSource? nap;

    internal Poller()
    {
    }

    /// <summary>Still polling: false once it was disposed, <c>keepGoing</c> said no, or a tick threw.</summary>
    public bool Running { get; private set; } = true;

    /// <summary>Ends when the poll does (faulted when a tick threw).</summary>
    public Task Done { get; internal set; } = Task.CompletedTask;

    /// <summary>Ticks now instead of at the end of the current wait (nothing while a tick is running).</summary>
    public void Wake()
    {
        try
        {
            nap?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // That wait had just ended: the tick is on its way anyway.
        }
    }

    public void Dispose() => stop.Cancel();

    internal async Task RunAsync(TimeSpan every, Func<CancellationToken, Task> tick, Func<bool>? keepGoing, TimeProvider clock)
    {
        var token = stop.Token;
        try
        {
            while (!token.IsCancellationRequested)
            {
                using (var wake = CancellationTokenSource.CreateLinkedTokenSource(token))
                {
                    nap = wake;
                    try
                    {
                        await Task.Delay(every, clock, wake.Token);
                    }
                    catch (OperationCanceledException) when (!token.IsCancellationRequested)
                    {
                        // Woken early: tick now.
                    }
                    finally
                    {
                        nap = null;
                    }
                }
                if (token.IsCancellationRequested) return;
                await tick(token);
                if (keepGoing is not null && !keepGoing()) return;
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        finally
        {
            Running = false;
        }
    }
}
