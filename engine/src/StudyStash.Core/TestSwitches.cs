namespace StudyStash.Core;

/// <summary>
/// The STUDYSTASH_* switches that tests and the self-test steer the app with: a pretend microphone, a notes folder and
/// ports of their own, a made-up newest release, still pictures. A student's copy is built without them
/// (TestHelpers=false in engine/Directory.Build.props): there every one reads as unset, whatever the environment says,
/// so nothing a student's computer has set can change what the app does. The switches the app sets for itself (the
/// library as a service or as the app's child, Connect's own check) aren't these, and read the environment as usual.
/// </summary>
public static class TestSwitches
{
#if TEST_HELPERS
    /// <summary>Whether this copy has the test helpers (the self-test, the demo libraries, these switches).</summary>
    public const bool On = true;

    /// <summary>The switch's value, or null when it's unset.</summary>
    public static string? Get(string name) => Environment.GetEnvironmentVariable(name);
#else
    /// <summary>Whether this copy has the test helpers (the self-test, the demo libraries, these switches).</summary>
    public const bool On = false;

    /// <summary>Null: a student's copy has no test switches.</summary>
    public static string? Get(string name) => null;
#endif
}
