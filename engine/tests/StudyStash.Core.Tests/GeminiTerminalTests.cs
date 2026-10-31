using StudyStash.Core.Ai;

namespace StudyStash.Core.Tests;

/// <summary>"Open in Antigravity": agy in a terminal runs in Syft Notes' own home for it, where it's signed in and has
/// the library's tools, never the student's own ~/.gemini (signed out, no library, and a folder made there).</summary>
public class GeminiTerminalTests
{
    [Fact]
    public void Open_in_antigravity_runs_agy_in_study_stashs_own_home()
    {
        using var dir = new TempDir();
        using var agyHome = Antigravity.Use(dir["agy"]); // this test's own, whatever runs beside it
        var (bin, args, env) = Terminal.Command(dir["pool/BIO 110"], dir["pool"], "BIO 110", "gemini", []);
        Assert.Equal("agy", bin);
        Assert.Equal(["--agent", "study-stash-library", "--add-dir", dir["pool"]], args);
        // What agy reads for its home: HOME on a Mac; on Windows USERPROFILE (where its ~/.gemini is, %USERPROFILE%\.gemini
        // in its own docs), and HOME too, for a library inside it that looks there first.
        Assert.Equal(Antigravity.HomeEnv(Antigravity.Home(), OperatingSystem.IsWindows()), env);
        Assert.Equal(Antigravity.Home(), env["HOME"]);
        if (OperatingSystem.IsWindows()) Assert.Equal(Antigravity.Home(), env["USERPROFILE"]);

        // The launch this computer uses. A Mac terminal runs a zsh script that sets the home for agy alone.
        if (!OperatingSystem.IsWindows())
        {
            string script = Terminal.MacScript(dir["pool/BIO 110"], "/Users/student/.local/bin/agy", args, env);
            Assert.Contains($"\nHOME='{Antigravity.Home()}' '/Users/student/.local/bin/agy' '--agent' 'study-stash-library'", script);
            Assert.EndsWith("exec zsh -l\n", script);
        }
        // Windows Terminal (or a console) runs PowerShell, which sets both first, in its own syntax.
        else
        {
            var (program, ps) = Terminal.WindowsWithEnv(@"C:\Users\s\AppData\Local\agy\bin\agy.exe", args, env);
            Assert.Equal(("powershell.exe", "-Command"), (program, ps[^2]));
            Assert.StartsWith($@"$env:HOME = '{Antigravity.Home()}'; $env:USERPROFILE = '{Antigravity.Home()}'; & 'C:\Users\s\AppData\Local\agy\bin\agy.exe' '--agent' 'study-stash-library'", ps[^1]);
        }
        // Both builders, whichever computer the test runs on: each in its own syntax (a quote in a path kept whole).
        var winEnv = Antigravity.HomeEnv(@"C:\Users\O'Neil\.study-stash\gemini", windows: true);
        Assert.Equal(@"$env:HOME = 'C:\Users\O''Neil\.study-stash\gemini'; $env:USERPROFILE = 'C:\Users\O''Neil\.study-stash\gemini'; & 'agy.exe' '--agent'",
            Terminal.WindowsWithEnv("agy.exe", ["--agent"], winEnv).Args[^1]);
        Assert.Equal("HOME='/Users/o'\\''neil/.study-stash/gemini' 'agy' '--agent'",
            Terminal.MacScript("/x", "agy", ["--agent"], Antigravity.HomeEnv("/Users/o'neil/.study-stash/gemini", windows: false)).Split('\n')[2]);

        // Claude and Codex are as they were: no environment of their own.
        Assert.Empty(Terminal.Command(dir["pool"], dir["pool"], null, "claude", []).Env);
        Assert.Equal("'claude' ", Terminal.MacScript("/x", "claude", [], null).Split('\n')[2]);
    }
}
