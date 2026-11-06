using Microsoft.Extensions.Time.Testing;
using StudyStash.App.Platform;

namespace StudyStash.App.Tests;

/// <summary>
/// Closing the Claude app on Windows before Connect writes its settings: its window is asked to close first, and only
/// when it's still there after the wait (it sits in the tray) are its own processes ended. Never Claude Code's
/// claude.exe, never Syft Notes, never another account's Claude. Over made-up processes: nothing real is closed or ended.
/// </summary>
public class DesktopAppsTests
{
    const string App = @"C:\Users\student\AppData\Local\AnthropicClaude\app-1.0.0\claude.exe";

    [Fact]
    public async Task Windows_asks_claude_to_close_and_ends_only_its_own_processes_and_only_after_the_wait()
    {
        var alive = new Dictionary<int, string>
        {
            [11] = App, [12] = App, // the app, and one of its helpers
            [7] = @"C:\Users\student\.local\bin\claude.exe", // Claude Code: same name, not the app
            [100] = App, // Syft Notes itself, however it's listed
            [21] = App, // the Claude app of another account signed in to this PC
        };
        var (asked, ended) = (new List<int>(), new List<int>());
        bool closesPolitely = false;
        var clock = new FakeTimeProvider();
        var apps = new DesktopApps
        {
            Find = names => names.Contains("claude") ? [.. alive.Select(p => new AppProcess(p.Key, p.Value, p.Key == 21 ? 2 : 1))] : [],
            AskToClose = id =>
            {
                asked.Add(id);
                if (closesPolitely) alive.Remove(id);
            },
            End = id =>
            {
                ended.Add(id);
                alive.Remove(id);
            },
            Own = 100, Session = 1, Clock = clock,
        };

        // It stays in the tray: nothing is ended until the wait is up, then only the app's own two.
        var quitting = apps.QuitOnWindowsAsync("claude-desktop", TestContext.Current.CancellationToken);
        Assert.Equal([11, 12], asked);
        Assert.False(quitting.IsCompleted);
        Assert.Empty(ended);
        clock.Advance(apps.PoliteWait);
        Assert.True(await quitting.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
        Assert.Equal([11, 12], ended);
        Assert.Equal([7, 21, 100], alive.Keys.Order().ToList());

        // One that closes when asked is never ended.
        (alive[11], closesPolitely) = (App, true);
        asked.Clear();
        ended.Clear();
        Assert.True(await apps.QuitOnWindowsAsync("claude-desktop", TestContext.Current.CancellationToken));
        Assert.Equal([11], asked);
        Assert.Empty(ended);
    }
}
