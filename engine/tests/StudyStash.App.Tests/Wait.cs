using StudyStash.App.Services.Kit;

namespace StudyStash.App.Tests;

/// <summary>How a test waits for something real (a server answering, a file written, a process): until the condition
/// holds, asked every few milliseconds, failing when it doesn't within the limit. Never a fixed sleep.</summary>
public static class Wait
{
    public static readonly TimeSpan Limit = TimeSpan.FromSeconds(10);

    public static async Task UntilAsync(Func<bool> condition, string what = "what the test waits for", TimeSpan? limit = null) =>
        Assert.True(await Poll.UntilAsync(condition, TimeSpan.FromMilliseconds(5), limit ?? Limit, TestContext.Current.CancellationToken),
            $"waited too long for {what}");

    public static async Task UntilAsync(Func<Task<bool>> condition, string what = "what the test waits for", TimeSpan? limit = null) =>
        Assert.True(await Poll.UntilAsync(_ => condition(), TimeSpan.FromMilliseconds(5), limit ?? Limit, TestContext.Current.CancellationToken),
            $"waited too long for {what}");
}
