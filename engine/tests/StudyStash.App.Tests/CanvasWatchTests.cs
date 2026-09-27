using StudyStash.App.Services;

namespace StudyStash.App.Tests;

public class CanvasWatchTests
{
    [Fact]
    public async Task RefreshAsync_reads_the_state_and_raises_Changed()
    {
        var fake = new FakeLibrary().Json(HttpMethod.Get, "/api/v2/canvas/state", "state-connected");
        var watch = new CanvasWatch(CanvasFixtures.Context(fake));
        int changed = 0;
        watch.Changed += () => changed++;

        await watch.RefreshAsync(TestContext.Current.CancellationToken);

        Assert.Equal("connected", watch.State?.Status);
        Assert.Equal(1, changed);
    }

    [Fact]
    public async Task RefreshAsync_does_nothing_without_a_library()
    {
        var watch = new CanvasWatch(CanvasFixtures.Context());
        int changed = 0;
        watch.Changed += () => changed++;

        await watch.RefreshAsync(TestContext.Current.CancellationToken);

        Assert.Null(watch.State);
        Assert.Equal(0, changed);
    }

    [Theory]
    [InlineData("state-syncing", 5)]
    [InlineData("state-no-extension", 5)]
    [InlineData("state-connected", 60)]
    [InlineData("state-chrome-away", 60)]
    [InlineData("state-error", 60)]
    public async Task NextDelay_is_fast_only_while_syncing_or_waiting_for_the_extension(string fixture, int seconds)
    {
        var fake = new FakeLibrary().Json(HttpMethod.Get, "/api/v2/canvas/state", fixture);
        var watch = new CanvasWatch(CanvasFixtures.Context(fake));
        await watch.RefreshAsync(TestContext.Current.CancellationToken);

        Assert.Equal(TimeSpan.FromSeconds(seconds), watch.NextDelay);
    }

    [Fact]
    public void NextDelay_defaults_slow_before_the_first_refresh() =>
        Assert.Equal(TimeSpan.FromSeconds(60), new CanvasWatch(CanvasFixtures.Context()).NextDelay);

    [Fact]
    public async Task Start_polls_until_Stop()
    {
        var fake = new FakeLibrary().Json(HttpMethod.Get, "/api/v2/canvas/state", "state-connected");
        var watch = new CanvasWatch(CanvasFixtures.Context(fake));

        watch.Start();
        await Task.Delay(80, TestContext.Current.CancellationToken); // one immediate refresh, then it waits out NextDelay (60 s)
        int afterStart = fake.Requests.Count;
        Assert.True(afterStart >= 1);

        watch.Stop();
        await Task.Delay(80, TestContext.Current.CancellationToken);
        Assert.Equal(afterStart, fake.Requests.Count); // stopped before the next poll, not after it
    }

    [Fact]
    public async Task Hurry_asks_now_and_every_3_seconds_then_the_watch_goes_back_to_its_own_pace()
    {
        var fake = new FakeLibrary().Json(HttpMethod.Get, "/api/v2/canvas/state", "state-connected");
        var watch = new CanvasWatch(CanvasFixtures.Context(fake));
        watch.Start();
        await Task.Delay(80, TestContext.Current.CancellationToken);
        int before = fake.Requests.Count;

        var hurry = watch.Hurry(); // wakes the 60 s wait: asks again straight away
        await Task.Delay(80, TestContext.Current.CancellationToken);
        Assert.True(fake.Requests.Count > before);
        Assert.Equal(TimeSpan.FromSeconds(3), watch.NextDelay);

        hurry.Dispose();
        hurry.Dispose(); // twice is once
        Assert.Equal(TimeSpan.FromSeconds(60), watch.NextDelay);
        Assert.True(watch.Running); // the app started it, so it keeps going
        watch.Stop();
    }

    [Fact]
    public async Task Hurry_starts_a_watch_nothing_else_had_and_stops_it_when_done()
    {
        var fake = new FakeLibrary().Json(HttpMethod.Get, "/api/v2/canvas/state", "state-no-extension");
        var watch = new CanvasWatch(CanvasFixtures.Context(fake));

        using (watch.Hurry())
        {
            await Task.Delay(80, TestContext.Current.CancellationToken);
            Assert.True(watch.Running);
            Assert.NotEmpty(fake.Requests);
        }

        Assert.False(watch.Running);
    }
}
