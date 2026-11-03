using StudyStash.App.Services;

namespace StudyStash.App.Tests;

/// <summary>Work the app starts without waiting for it (a page being read, a list loading) never fails unseen: an
/// unexpected error is written down with what it was and the student is told; a cancelled one says nothing.</summary>
public class UiTasksTests
{
    [Fact]
    public async Task An_unexpected_failure_is_logged_and_said_and_a_cancelled_one_is_quiet()
    {
        var log = new List<string>();
        int said = 0;

        await UiTasks.Guard(async () =>
        {
            await Task.Yield();
            throw new InvalidOperationException("the page broke");
        }, "ShowHome", log.Add, () => said++);
        Assert.Equal(1, said);
        Assert.Contains(log, l => l.Contains("ShowHome", StringComparison.Ordinal) && l.Contains("the page broke", StringComparison.Ordinal));

        log.Clear();
        said = 0;
        await UiTasks.Guard(async () =>
        {
            await Task.Yield();
            throw new TaskCanceledException();
        }, "ShowHome", log.Add, () => said++);
        await UiTasks.Guard(() => throw new OperationCanceledException(), "ShowHome", log.Add, () => said++);
        Assert.Equal(0, said);
        Assert.Empty(log);
    }
}
