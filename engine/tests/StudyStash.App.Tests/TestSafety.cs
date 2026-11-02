using System.Runtime.CompilerServices;

namespace StudyStash.App.Tests;

/// <summary>Before any test runs: nothing the app's tests do changes this computer (no login item is written or
/// even looked for).</summary>
static class TestSafety
{
#pragma warning disable CA2255 // the tests are the application here: this has to run before any of them
    [ModuleInitializer]
    internal static void TurnOffSystemChanges()
    {
        Platform.Desktop.SystemChangesOff = true;
        // The Mac and Windows wording reads the same on a Linux test runner; the Linux tests ask for Linux's words.
        StudyStash.Core.Machine.Linux = false;
        // The daily count never asks the real Firestore from a test; its own tests give it a fake handler.
        Services.DailyCount.Off = true;
        // The app's home, and so its log, is a folder of the tests' own: nothing a test logs reaches the real one.
        Program.Home = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "studystash-app-tests", "home");
        Directory.CreateDirectory(System.IO.Path.Combine(Program.Home, "logs"));
        Controls.Rich.SceneCache.Log = _ => { };
        // Codex's own config.toml is the student's: no test reads the real one.
        Core.Ai.SetupChat.CodexConfig = () => System.IO.Path.Combine(System.IO.Path.GetTempPath(), "studystash-app-tests-no-codex", "config.toml");
        // Claude Code's version, as a current one says it: no test asks the real one.
        Core.Ai.ClaudeProvider.VersionOf = _ => new Version(2, 1, 260);
        // Antigravity's home of Syft Notes' own: a folder of the tests' own, never ~/.study-stash/gemini (an off-screen
        // film points it at a signed-in one with STUDYSTASH_AGY_HOME).
        Core.Ai.Antigravity.Home = () => Core.Ai.Antigravity.For(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "studystash-app-tests"));
        // A sign-in's page never goes to this computer's browser from a test (CI's real sign-ins give their own opener).
        Core.Ai.AgentSignIn.Browser = _ => true;
    }
#pragma warning restore CA2255
}

/// <summary>A settings folder of its own for one test, removed afterwards: never the real one.</summary>
public sealed class TempHome : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "studystash-app-tests", Guid.NewGuid().ToString("N")[..12]);

    public TempHome() => Directory.CreateDirectory(Path);

    public string this[string name] => System.IO.Path.Combine(Path, name);

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }
}

/// <summary>Tests that change the process's environment variables run alone, so no other test sees them.</summary>
[CollectionDefinition(nameof(EnvironmentTests), DisableParallelization = true)]
public sealed class EnvironmentTests;
