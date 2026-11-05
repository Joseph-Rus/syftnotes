using System.Text.Json.Nodes;
using StudyStash.App.Services;

namespace StudyStash.App.Tests;

/// <summary>The first start from the new place (NewPlace.cs, docs/LIBRARIES.md phase 5): an AI app's connection and
/// start at login that still name the program from before the move are written again for this one; a connection to
/// another copy that's still there is left alone. Every file is in the test's own folder.</summary>
public class NewPlaceTests
{
    [Fact]
    public void Connections_and_start_at_login_naming_the_old_program_follow_it_and_others_stay()
    {
        using var home = new TempHome();
        const string now = "/Applications/Syft Notes.app/Contents/MacOS/StudyStash";
        string other = home["dev/bin/StudyStash"];
        Directory.CreateDirectory(Path.GetDirectoryName(other)!);
        File.WriteAllText(other, "");
        var setup = new ClaudeSetup
        {
            Program = now, Home = home["study-stash"], UserHome = home["user"], Find = _ => null, AppInstalled = _ => false, ReopenApp = null,
            Running = _ => false, Probe = null,
            DesktopConfig = home["user/Claude/claude_desktop_config.json"], ClaudeCodeConfig = home["user/.claude.json"],
            CodexConfig = home["user/.codex/config.toml"], GeminiConfig = home["user/.gemini/settings.json"],
            AntigravityConfig = home["user/.gemini/config/mcp_config.json"],
        };
        static void Write(string path, string text)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, text);
        }
        Write(home["user/Claude/claude_desktop_config.json"],
            """{"mcpServers":{"syft-notes":{"command":"/Applications/Study Stash.app/Contents/MacOS/StudyStash","args":["mcp","--client","claude-desktop"]},"github":{"command":"gh-mcp"}}}""");
        Write(home["user/.codex/config.toml"], "[mcp_servers.syft-notes]\ncommand = \"/Applications/Study Stash.app/Contents/MacOS/StudyStash\"\nargs = [\"mcp\"]\n");
        Write(home["user/.claude.json"], new JsonObject { ["mcpServers"] = new JsonObject { ["syft-notes"] = new JsonObject { ["type"] = "stdio", ["command"] = other, ["args"] = new JsonArray("mcp") } } }.ToJsonString());
        bool relogged = false;

        var done = NewPlace.Run(setup, home["study-stash"], now, () => "/Applications/Study Stash.app/Contents/MacOS/StudyStash", () => relogged = true);

        Assert.Equal(now, setup.CommandOf("claude-desktop"));
        Assert.Equal("gh-mcp", JsonNode.Parse(File.ReadAllText(home["user/Claude/claude_desktop_config.json"]))!["mcpServers"]!["github"]!["command"]!.GetValue<string>());
        Assert.Equal(now, setup.CommandOf("codex"));
        Assert.Equal(other, setup.CommandOf("claude-code")); // another copy, still there: not ours to move
        Assert.True(relogged);
        Assert.Equal(3, done.Count);
        // A second start finds nothing left to do.
        Assert.Empty(NewPlace.Run(setup, home["study-stash"], now, () => now, () => throw new InvalidOperationException("nothing to do")));
    }

    [Fact]
    public void The_old_install_left_behind_goes_with_its_entry_and_shortcuts_and_nothing_else_does()
    {
        using var home = new TempHome();
        string old = Path.Combine(home.Path, "Local", "Programs", "Study Stash");
        string running = Path.Combine(home.Path, "Local", "SyftNotes", "current");
        string menu = home["Start Menu"];
        string studyStash = home["study-stash"];
        foreach (string dir in (string[])[Path.Combine(old, "runtimes", "win-x64"), running, menu, studyStash]) Directory.CreateDirectory(dir);
        foreach (string file in (string[])["StudyStash.exe", "unins000.exe", "unins000.dat", "Avalonia.dll", Path.Combine("runtimes", "win-x64", "libSkiaSharp.dll")])
            File.WriteAllText(Path.Combine(old, file), "");
        // A .lnk keeps its target's path as UTF-16 (VelopackHooks.LinkNames reads it so).
        static void Link(string lnk, string target) => File.WriteAllBytes(lnk, System.Text.Encoding.Unicode.GetBytes("\0L" + target + "\0"));
        Link(Path.Combine(menu, "Old Syft Notes.lnk"), Path.Combine(old, "StudyStash.exe"));
        Link(Path.Combine(menu, "Syft Notes.lnk"), Path.Combine(running, "StudyStash.exe"));
        Link(Path.Combine(menu, "Study Stash 2.lnk"), Path.Combine(home.Path, "Local", "Programs", "Study Stash 2", "StudyStash.exe"));
        File.WriteAllText(Path.Combine(studyStash, "bridge-failed.txt"), "v0.23.0");
        int forgot = 0;

        // Its program running (or its uninstaller, hung): nothing is touched, and the next start tries again.
        using (new FileStream(Path.Combine(old, "unins000.exe"), FileMode.Open, FileAccess.Read, FileShare.None))
            if (OperatingSystem.IsWindows())
            {
                Assert.Contains("still there", NewPlace.ClearOldInstall(old, running, studyStash, [menu], () => forgot++));
                Assert.True(File.Exists(Path.Combine(old, "Avalonia.dll")));
            }

        Assert.Equal($"removed the old install in {old}", NewPlace.ClearOldInstall(old, running, studyStash, [menu], () => forgot++));
        Assert.False(Directory.Exists(old));
        Assert.Equal(1, forgot);
        Assert.Equal(["Study Stash 2.lnk", "Syft Notes.lnk"], Directory.GetFiles(menu).Select(Path.GetFileName).Order());
        Assert.False(File.Exists(Path.Combine(studyStash, "bridge-failed.txt")));
        // An entry naming the running copy's own folder, or a folder that isn't Inno's: left alone.
        Assert.Null(NewPlace.ClearOldInstall(running, running, studyStash, [menu], () => forgot++));
        Assert.Null(NewPlace.ClearOldInstall(home.Path, running, studyStash, [menu], () => forgot++));
        Assert.True(Directory.Exists(running));
        Assert.Equal(1, forgot);
    }
}
