using System.Diagnostics;
using StudyStash.Audio;

namespace StudyStash.Core.Tests;

/// <summary>Runs alone: it keeps every thread of the .NET thread pool busy for a moment.</summary>
[CollectionDefinition("BusyPool", DisableParallelization = true)]
public sealed class BusyPoolCollection;

/// <summary>Opening the microphone doesn't wait in line behind other work.</summary>
[Collection("BusyPool")]
public class OpeningPoolTests
{
    /// <summary>A computer whose shared threads are all busy (an app starting up, a test machine running everything
    /// at once): opening the microphone still answers at once, instead of waiting its whole limit for a free thread
    /// and saying "The microphone didn't start" (LinuxTests on a CI runner, PR #139).</summary>
    [Fact]
    public void The_microphone_opens_at_once_while_every_shared_thread_is_busy()
    {
        var release = new ManualResetEventSlim(); // not disposed: work still queued behind the test waits on it, set
        ThreadPool.GetMinThreads(out int min, out _);
        int busy = min * 4 + 64;
        for (int i = 0; i < busy; i++) ThreadPool.UnsafeQueueUserWorkItem<ManualResetEventSlim>(r => r.Wait(TimeSpan.FromSeconds(30)), release, preferLocal: false);
        try
        {
            var took = Stopwatch.StartNew();
            string opened = Opening.Within(TimeSpan.FromSeconds(2), () => "the microphone", _ => { }, () => new TimeoutException("waited for a free thread"));
            Assert.Equal("the microphone", opened);
            Assert.True(took.Elapsed < TimeSpan.FromSeconds(1), $"took {took.Elapsed.TotalSeconds:0.0} s");
        }
        finally
        {
            release.Set();
        }
    }
}
