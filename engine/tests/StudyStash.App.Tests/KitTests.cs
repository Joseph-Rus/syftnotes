using StudyStash.App.Services;
using StudyStash.App.Services.Kit;
using StudyStash.App.ViewModels;
using StudyStash.App.ViewModels.Kit;
using StudyStash.Core;

namespace StudyStash.App.Tests;

/// <summary>The toolkit's small pieces: the one way to wait, and the one way a library call goes wrong. Time is moved on
/// by hand (<see cref="ManualClock"/>): nothing here waits for real.</summary>
public class KitTests
{
    static CancellationToken Ct => TestContext.Current.CancellationToken;

    // Off the test's own synchronization context, so a wait's end runs as the clock fires it.
    static Task Off(Func<Task> test) => Task.Run(test, Ct);

    [Fact]
    public Task Poll_returns_as_soon_as_the_check_is_true() => Off(async () =>
    {
        var clock = new ManualClock();
        int asked = 0;
        var poll = Poll.UntilAsync(() => ++asked == 3, TimeSpan.FromSeconds(2), TimeSpan.FromMinutes(1), Ct, clock);

        Assert.Equal(1, asked); // asked straight away
        clock.Advance(TimeSpan.FromSeconds(2));
        Assert.False(poll.IsCompleted);
        clock.Advance(TimeSpan.FromSeconds(2));

        Assert.True(await poll);
        Assert.Equal(3, asked);
        Assert.Equal(0, clock.Waiting);
    });

    [Fact]
    public Task Poll_every_ends_when_the_callers_own_token_is_cancelled_and_after_waits_on_the_clock() => Off(async () =>
    {
        var clock = new ManualClock();
        using var stop = new CancellationTokenSource();
        int ticks = 0;
        var poller = Poll.Every(TimeSpan.FromSeconds(2), _ => { ticks++; return Task.CompletedTask; }, clock: clock, stop: stop.Token);
        clock.Advance(TimeSpan.FromSeconds(2));
        Assert.Equal(1, ticks);
        await stop.CancelAsync();
        await poller.Done;
        Assert.False(poller.Running);

        var after = Poll.AfterAsync(TimeSpan.FromSeconds(5), Ct, clock);
        clock.Advance(TimeSpan.FromSeconds(4));
        Assert.False(after.IsCompleted);
        clock.Advance(TimeSpan.FromSeconds(1));
        await after;
    });

    [Fact]
    public Task Poll_gives_up_at_the_timeout() => Off(async () =>
    {
        var clock = new ManualClock();
        int asked = 0;
        var poll = Poll.UntilAsync(() => { asked++; return false; }, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), Ct, clock);

        clock.Advance(TimeSpan.FromSeconds(4));
        Assert.False(poll.IsCompleted);
        clock.Advance(TimeSpan.FromSeconds(1)); // the last wait is only what's left of the five seconds

        Assert.False(await poll);
        Assert.Equal(4, asked); // at 0, 2, 4 and 5 seconds
    });

    [Fact]
    public async Task LibraryCall_turns_refused_offline_and_older_into_one_failure_each()
    {
        var refused = await LibraryCall.Run<AiSaidStub>(() => throw new LibraryRefusedException(401, "wrong password"), Ct);
        Assert.Equal((FailureKind.Refused, "wrong password", 401), (refused.Failure!.Kind, refused.Failure.Words, refused.Failure.Status));

        var offline = await LibraryCall.Run<AiSaidStub>(() => throw new HttpRequestException("down"), Ct);
        Assert.Equal((FailureKind.Offline, AiWords.NotAnswering), (offline.Failure!.Kind, offline.Failure.Words));
        var timedOut = await LibraryCall.Run<AiSaidStub>(() => throw new TaskCanceledException("HttpClient.Timeout"), Ct);
        Assert.Equal(FailureKind.Offline, timedOut.Failure!.Kind);

        var older = await LibraryCall.Run(() => Task.FromResult<AiSaidStub?>(null), Ct);
        Assert.Equal((FailureKind.OlderLibrary, AiWords.OlderLibraryWords), (older.Failure!.Kind, older.Failure.Words));

        var ok = await LibraryCall.Run(() => Task.FromResult<AiSaidStub?>(new AiSaidStub()), Ct);
        Assert.True(ok.Ok);
        Assert.NotNull(ok.Value);
    }

    [Fact]
    public async Task AsyncAction_ignores_a_second_press_and_says_why_it_failed()
    {
        var action = new AsyncAction();
        var gate = new TaskCompletionSource<Outcome>();
        int runs = 0;
        var first = action.RunAsync(_ => { runs++; return gate.Task; });

        Assert.True(action.Busy);
        Assert.Null(await action.RunAsync(_ => { runs++; return Task.FromResult(Outcome.Done); })); // busy: ignored
        gate.SetResult(Outcome.Failed(Failure.Offline));
        await first;

        Assert.Equal(1, runs);
        Assert.False(action.Busy);
        Assert.Equal(AiWords.NotAnswering, action.Say);
    }

    [Fact]
    public async Task AsyncAction_cancel_ends_quietly_and_the_command_runs_the_work()
    {
        var action = new AsyncAction(async ct => { await new TaskCompletionSource().Task.WaitAsync(ct); return Outcome.Done; });
        var run = action.Command.ExecuteAsync(null);
        Assert.True(action.Busy);
        action.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
        Assert.False(action.Busy);
        Assert.Null(action.Say);

        var once = new AsyncAction(_ => Task.FromResult(Outcome.Failed(Failure.Offline)));
        await once.Command.ExecuteAsync(null);
        Assert.Equal(AiWords.NotAnswering, once.Say);
    }

    public sealed class AiSaidStub;
}
