using StudyStash.Core.Ai;

namespace StudyStash.Core.Tests;

/// <summary>"Open in Antigravity": agy in a terminal runs in Study Stash's own home for it, where it's signed in and has
/// the library's tools, never the student's own ~/.gemini (signed out, no library, and a folder made there).</summary>
public class GeminiTerminalTests
{
    [Fact]
    public void Open_in_antigravity_runs_agy_in_study_stashs_own_home()
    {
        using var dir = new TempDir();
        var (bin, args, env) = Terminal.Command(dir["pool/BIO 110"], dir["pool"], "BIO 110", "gemini", []);
        Assert.Equal("agy", bin);
        Assert.Equal(["--agent", "study-stash-library", "--add-dir", dir["pool"]], args);
        Assert.Equal(Antigravity.Home(), env["HOME"]);

        // On a Mac, the terminal's script sets that home for agy alone; on Windows, PowerShell sets it first.
        string script = Terminal.MacScript(dir["pool/BIO 110"], "/Users/student/.local/bin/agy", args, env);
        Assert.Contains($"HOME='{Antigravity.Home()}' '/Users/student/.local/bin/agy' '--agent' 'study-stash-library'", script);
        Assert.EndsWith("exec zsh -l\n", script);
        var (program, ps) = Terminal.WindowsWithEnv(@"C:\Users\s\AppData\Local\agy\bin\agy.exe", args, new Dictionary<string, string> { ["USERPROFILE"] = @"C:\Users\s\.study-stash\gemini" });
        Assert.Equal("powershell.exe", program);
        Assert.StartsWith(@"$env:USERPROFILE = 'C:\Users\s\.study-stash\gemini'; & 'C:\Users\s\AppData\Local\agy\bin\agy.exe' '--agent'", ps[^1]);

        // Claude and Codex are as they were: no environment of their own.
        Assert.Empty(Terminal.Command(dir["pool"], dir["pool"], null, "claude", []).Env);
        Assert.Equal("'claude' ", Terminal.MacScript("/x", "claude", [], null).Split('\n')[2]);
    }
}
