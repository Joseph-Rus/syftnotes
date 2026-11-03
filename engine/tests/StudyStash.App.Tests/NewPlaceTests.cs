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
        Write(home["user/.claude.json"], "{\"mcpServers\":{\"syft-notes\":{\"type\":\"stdio\",\"command\":\"" + other + "\",\"args\":[\"mcp\"]}}}");
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
}
