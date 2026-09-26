using System.Net.Http;
using StudyStash.App.Services;

namespace StudyStash.App.Tests;

public class CanvasNotifierTests
{
    static FakeLibrary Library() => new FakeLibrary()
        .Json(HttpMethod.Get, "/api/v2/canvas/notifications", "notifications")
        .Json(HttpMethod.Post, "/api/v2/canvas/notifications/seen", "{}");

    [Fact]
    public async Task The_first_poll_ever_is_quiet_it_only_marks_where_the_library_s_list_currently_ends()
    {
        using var home = new TempHome();
        var fake = Library();
        var notifier = new CanvasNotifier(CanvasFixtures.Context(fake, home.Path));

        var toasts = await notifier.PollAsync(TestContext.Current.CancellationToken);

        Assert.Empty(toasts);
        Assert.True(File.Exists(home["canvas-notified.json"]));
    }

    [Fact]
    public async Task The_next_poll_shows_up_to_three_toasts_the_freshest_one_expanded()
    {
        using var home = new TempHome();
        var fake = Library();
        var notifier = new CanvasNotifier(CanvasFixtures.Context(fake, home.Path));
        await notifier.PollAsync(TestContext.Current.CancellationToken);

        var toasts = await notifier.PollAsync(TestContext.Current.CancellationToken);

        Assert.Equal(3, toasts.Count);
        // The library lists oldest first; the most recently posted (New score) is the one made prominent.
        Assert.Equal(["New score", "Due date moved", "New assignment"], toasts.Select(t => t.Title));
        Assert.True(toasts[0].Expanded);
        Assert.False(toasts[1].Expanded);
        Assert.False(toasts[2].Expanded);
        Assert.Equal("CS 101 · Problem set 4 · 18/20", toasts[0].Text);
    }

    [Fact]
    public async Task A_restart_remembers_where_it_left_off_instead_of_starting_over()
    {
        using var home = new TempHome();
        var first = new CanvasNotifier(CanvasFixtures.Context(Library(), home.Path));
        await first.PollAsync(TestContext.Current.CancellationToken);

        // A fresh CanvasNotifier, same home: it reads the bookmark back and isn't quiet this time.
        var restarted = new CanvasNotifier(CanvasFixtures.Context(Library(), home.Path));
        var toasts = await restarted.PollAsync(TestContext.Current.CancellationToken);

        Assert.Equal(3, toasts.Count);
    }

    [Fact]
    public async Task Opening_a_toast_calls_the_host_back_then_marks_it_seen()
    {
        using var home = new TempHome();
        var fake = Library();
        var notifier = new CanvasNotifier(CanvasFixtures.Context(fake, home.Path));
        CanvasApi.NotificationRow? opened = null;
        notifier.OnOpen = n => opened = n;
        await notifier.PollAsync(TestContext.Current.CancellationToken);
        var toasts = await notifier.PollAsync(TestContext.Current.CancellationToken);

        await toasts[0].OpenCommand.ExecuteAsync(null);

        Assert.Equal(3, opened!.Id);
        var seen = fake.Requests.Single(r => r.Path == "/api/v2/canvas/notifications/seen");
        Assert.Contains("\"up_to\":3", seen.Body);
    }

    [Fact]
    public async Task Dismissing_a_toast_only_marks_it_seen_no_host_callback()
    {
        using var home = new TempHome();
        var fake = Library();
        var notifier = new CanvasNotifier(CanvasFixtures.Context(fake, home.Path));
        bool opened = false;
        notifier.OnOpen = _ => opened = true;
        await notifier.PollAsync(TestContext.Current.CancellationToken);
        var toasts = await notifier.PollAsync(TestContext.Current.CancellationToken);

        await toasts[1].DismissCommand.ExecuteAsync(null);

        Assert.False(opened);
        var seen = fake.Requests.Single(r => r.Path == "/api/v2/canvas/notifications/seen");
        Assert.Contains("\"up_to\":2", seen.Body);
    }

    [Fact]
    public async Task No_library_no_toasts()
    {
        using var home = new TempHome();
        var notifier = new CanvasNotifier(CanvasFixtures.Context(handler: null, home.Path));

        Assert.Empty(await notifier.PollAsync(TestContext.Current.CancellationToken));
        Assert.False(File.Exists(home["canvas-notified.json"]));
    }
}
