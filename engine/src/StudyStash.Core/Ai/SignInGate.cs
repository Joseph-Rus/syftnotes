using System.Collections.Concurrent;
using System.Diagnostics;

namespace StudyStash.Core.Ai;

/// <summary>
/// One Claude Code at a time gets past its sign-in when that sign-in may need renewing: the first run after a quiet
/// spell (<see cref="WarmFor"/>), or after one Claude turned away. Claude Code's access token lasts about eight hours;
/// the first run after it ran out renews it with the sign-in's refresh token, which then changes, and several started
/// at once (a lecture's sorting and notes, Ask, diagrams) can each try to renew it with the same one. The one that
/// loses can make the sign-in itself stop working ("Failed to authenticate: OAuth session expired and could not be
/// refreshed"), though nobody signed out: anthropics/claude-code #91708, #80585, #41507 (see
/// docs/claude-stays-signed-in.md). So the first run leads alone, and the others start once it has got past sign-in
/// (Claude has begun to answer) or has finished. Egghead Notes never reads or touches the sign-in itself: it only
/// decides when to start the unmodified <c>claude</c>.
/// </summary>
public sealed class SignInGate
{
    static readonly ConcurrentDictionary<string, SignInGate> Gates =
        new(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

    /// <summary>The gate for one Claude Code program, which every run this process starts with it goes through.</summary>
    public static SignInGate For(string exe) => Gates.GetOrAdd(exe, _ => new SignInGate());

    /// <summary>How long a run that got past sign-in vouches for the next ones: within it they start at once.</summary>
    public TimeSpan WarmFor { get; init; } = TimeSpan.FromMinutes(5);
    /// <summary>The most a run waits for the one signing in: a leader that's slow to start answering (a long
    /// transcript to read first) doesn't hold the others for ever.</summary>
    public TimeSpan WaitAtMost { get; init; } = TimeSpan.FromMinutes(2);
    public Func<DateTime> Now { get; init; } = () => DateTime.UtcNow;

    readonly object sync = new();
    DateTime warmUntil = DateTime.MinValue;
    TaskCompletionSource? leading;

    /// <summary>Whether a run would start at once now, without leading.</summary>
    public bool Warm
    {
        get
        {
            lock (sync) return Now() < warmUntil;
        }
    }

    /// <summary>Waits for this run's turn: it leads (signs in alone) when nobody has got past sign-in lately and
    /// nobody else is leading; it waits while someone else leads; it goes at once when the sign-in is known good.</summary>
    public async Task<Turn> EnterAsync(CancellationToken ct = default)
    {
        var waited = Stopwatch.StartNew();
        while (true)
        {
            Task others;
            lock (sync)
            {
                if (Now() < warmUntil) return new Turn(this, leads: false);
                if (leading is null)
                {
                    leading = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    return new Turn(this, leads: true);
                }
                others = leading.Task;
            }
            var left = WaitAtMost - waited.Elapsed;
            if (left <= TimeSpan.Zero) return new Turn(this, leads: false);
            using var stop = CancellationTokenSource.CreateLinkedTokenSource(ct);
            await Task.WhenAny(others, Task.Delay(left, stop.Token));
            await stop.CancelAsync();
            ct.ThrowIfCancellationRequested();
        }
    }

    /// <summary>Claude turned a run away (signed out, or its sign-in couldn't be renewed): the next run leads again.</summary>
    public void Cold()
    {
        lock (sync) warmUntil = DateTime.MinValue;
    }

    void Passed()
    {
        lock (sync) warmUntil = Now() + WarmFor;
    }

    void Release()
    {
        TaskCompletionSource? done;
        lock (sync)
        {
            done = leading;
            leading = null;
        }
        done?.TrySetResult();
    }

    /// <summary>One run's place at the gate. <see cref="Passed"/> once Claude has begun to answer; disposed when the
    /// run ends, which lets the next one lead if this one led and never got past sign-in.</summary>
    public sealed class Turn : IDisposable
    {
        readonly SignInGate gate;
        bool open;

        internal Turn(SignInGate gate, bool leads)
        {
            this.gate = gate;
            Leads = leads;
            open = leads;
        }

        /// <summary>This run signs in alone; the others wait for it.</summary>
        public bool Leads { get; }

        /// <summary>Claude has begun to answer, so its sign-in works: the ones waiting go, and the next ones for a while.</summary>
        public void Passed()
        {
            gate.Passed();
            Leave();
        }

        public void Dispose() => Leave();

        void Leave()
        {
            if (!open) return;
            open = false;
            gate.Release();
        }
    }
}
