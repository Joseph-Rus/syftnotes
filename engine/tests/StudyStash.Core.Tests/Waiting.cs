using StudyStash.App.Services.Kit;

namespace StudyStash.Core.Tests;

/// <summary>How a test waits for something real (a server, a background job, a process): asked every few milliseconds
/// until it's true or the limit passes (false). Never a fixed sleep. (<see cref="Poll"/> is the app's own, linked in.)</summary>
public static class Waiting
{
    static readonly TimeSpan Every = TimeSpan.FromMilliseconds(5);

    public static Task<bool> UntilAsync(Func<bool> condition, double seconds = 10) =>
        Poll.UntilAsync(condition, Every, TimeSpan.FromSeconds(seconds), TestContext.Current.CancellationToken);

    public static Task<bool> UntilAsync(Func<Task<bool>> condition, double seconds = 10) =>
        Poll.UntilAsync(_ => condition(), Every, TimeSpan.FromSeconds(seconds), TestContext.Current.CancellationToken);
}
