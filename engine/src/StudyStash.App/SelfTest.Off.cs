#if !TEST_HELPERS
namespace StudyStash.App;

/// <summary>A student's copy (built with TestHelpers=false) has no self-test: SelfTest*.cs and the demo libraries
/// aren't built into it, and this answers the app's few questions about one. There's never a self-test running, so
/// the app always starts as itself.</summary>
public static class SelfTest
{
    public static string? Dir => null;
    internal static bool BackgroundOn => false;
    public static int? Prepare(string home) => null;
    public static int AfterQuit(string home) => 0;
    public static void Run() { }
}
#endif
