using System.Reflection;
using StudyStash.App.Controls.Rich;
using Xunit.v3;

[assembly: StudyStash.App.Tests.LayoutsSettle]

namespace StudyStash.App.Tests;

/// <summary>
/// After every test, before its headless app goes: the flowchart being laid out in the background finishes, and any
/// still queued are dropped. The app's fonts are freed the moment the test ends: a layout still measuring words with
/// them could read freed memory, and once they'd gone it failed, or touched the next test's app from the wrong thread.
/// </summary>
[AttributeUsage(AttributeTargets.Assembly)]
public sealed class LayoutsSettleAttribute : BeforeAfterTestAttribute
{
    public override void After(MethodInfo methodUnderTest, IXunitTest test)
    {
        if (!SceneCache.Forget(TimeSpan.FromSeconds(60)))
            throw new TimeoutException($"a flowchart {test.TestDisplayName} started was still being laid out a minute after it ended");
    }
}
