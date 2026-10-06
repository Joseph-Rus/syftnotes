using System.Text.Json.Nodes;
using StudyStash.App.Services;
using StudyStash.App.ViewModels;
using StudyStash.Core;

namespace StudyStash.App.Tests;

/// <summary>Connecting the AI apps on this computer (Claude Desktop, Claude Code, ChatGPT, Gemini CLI) to the library:
/// what goes into each app's own settings file, and that nothing else in it is ever lost. Every file is in a test's
/// own folder: never a real app's settings.</summary>
public class ClaudeSetupTests
{
    const string Program = "/Applications/Study Stash.app/Contents/MacOS/StudyStash";

    static ClaudeSetup Setup(TempHome home, string program = Program) => new()
    {
        Program = program, Home = home["study-stash"], UserHome = home["user"], Find = _ => null, AppInstalled = _ => false,
        DesktopConfig = home["user/Claude/claude_desktop_config.json"], ClaudeCodeConfig = home["user/.claude.json"],
        CodexConfig = home["user/.codex/config.toml"], GeminiConfig = home["user/.gemini/settings.json"],
    };

    static void Write(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    [Theory]
    [InlineData("claude-desktop")]
    [InlineData("claude-code")]
    [InlineData("gemini")]
    public void Connecting_a_json_app_keeps_its_other_servers_and_settings_and_disconnecting_takes_out_only_study_stash(string id)
    {
        using var home = new TempHome();
        var setup = Setup(home);
        string path = setup.State(id).ConfigPath;
        string before = """{"theme":"dark","note":"<b>&'</b>","mcpServers":{"github":{"command":"gh-mcp","env":{"TOKEN":"x"}}},"numbers":1.50}""" + "\n";
        string real = path;
        if (!OperatingSystem.IsWindows())
        {
            // A dotfiles setup: the settings file is a link to the real one, which is what changes.
            real = home["dotfiles/" + Path.GetFileName(path)];
            Write(real, before);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.CreateSymbolicLink(path, real);
        }
        else Write(path, before);

        var added = setup.Connect(id);

        Assert.True(added.Ok, added.Say);
        Assert.Contains("to load it", added.Say);
        if (!OperatingSystem.IsWindows()) Assert.NotNull(new FileInfo(path).LinkTarget); // still a link
        var config = JsonNode.Parse(File.ReadAllText(real))!;
        Assert.Equal("dark", config["theme"]!.GetValue<string>());
        Assert.Equal("<b>&'</b>", config["note"]!.GetValue<string>());
        Assert.Equal("gh-mcp", config["mcpServers"]!["github"]!["command"]!.GetValue<string>());
        var entry = config["mcpServers"]![ClaudeTools.ServerName]!;
        Assert.Equal(Program, entry["command"]!.GetValue<string>());
        Assert.Equal(["--home", home["study-stash"], "mcp", "--client", id], entry["args"]!.AsArray().Select(a => a!.GetValue<string>()));
        Assert.DoesNotContain("pw", added.Written); // only the command: no password, no address
        Assert.Equal(before, File.ReadAllText(added.Backup!)); // what was there, kept beside it
        var state = setup.State(id);
        Assert.True(state.Added);
        Assert.False(state.OtherCopy);
        Assert.NotNull(state.AddedAt);

        var removed = setup.Disconnect(id);

        Assert.True(removed.Ok, removed.Say);
        var after = JsonNode.Parse(File.ReadAllText(real))!;
        Assert.Null(after["mcpServers"]![ClaudeTools.ServerName]);
        Assert.Equal("gh-mcp", after["mcpServers"]!["github"]!["command"]!.GetValue<string>());
        Assert.Equal("x", after["mcpServers"]!["github"]!["env"]!["TOKEN"]!.GetValue<string>());
        Assert.Equal("1.50", after["numbers"]!.ToJsonString());
        Assert.False(setup.State(id).Added);
        Assert.Null(setup.State(id).AddedAt);
    }

    [Fact]
    public void ChatGPT_gets_its_own_table_and_every_other_line_of_config_toml_stays_as_it_was()
    {
        using var home = new TempHome();
        var setup = Setup(home);
        string before = """
            # my settings
            model = "gpt-5-codex"

            [mcp_servers.github]
            command = "gh-mcp"
            env = { TOKEN = "x" } # keep me

            [profiles.fast]
            model = "gpt-5-mini"

            """;
        Write(setup.CodexConfig, before);

        var added = setup.Connect("codex");

        Assert.True(added.Ok, added.Say);
        Assert.Equal("Added Study Stash to ChatGPT. Quit and reopen ChatGPT to load it (or start a new Codex session).", added.Say);
        string text = File.ReadAllText(setup.CodexConfig);
        Assert.StartsWith(before.TrimEnd('\n'), text);
        Assert.Contains($"[mcp_servers.{ClaudeTools.ServerName}]\ncommand = \"{Program}\"\nargs = [\"--home\", \"{home["study-stash"].Replace("\\", "\\\\")}\", \"mcp\", \"--client\", \"codex\"]", text);
        Assert.True(setup.State("codex").Added);

        // Connecting again (another copy of the app, say) replaces Study Stash's table rather than adding a second.
        var other = Setup(home, "/elsewhere/StudyStash");
        Assert.True(other.State("codex").OtherCopy);
        Assert.True(other.Connect("codex").Ok);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(File.ReadAllText(setup.CodexConfig), @"\[mcp_servers\.study-stash\]"));
        Assert.False(other.State("codex").OtherCopy);

        Assert.True(setup.Disconnect("codex").Ok);
        Assert.Equal(before, File.ReadAllText(setup.CodexConfig)); // exactly as it was
    }

    [Theory]
    [InlineData("gemini", "{\n  // my comment\n  \"theme\": \"dark\"\n}\n", "comments")]
    [InlineData("claude-desktop", "{\"mcpServers\": {", "isn't valid JSON")]
    [InlineData("codex", "model = \n", "isn't valid TOML")]
    public void A_file_study_stash_cant_rewrite_safely_is_left_alone_with_the_setup_to_add_by_hand(string id, string text, string why)
    {
        using var home = new TempHome();
        var setup = Setup(home);
        string path = setup.State(id).ConfigPath;
        Write(path, text);

        var result = setup.Connect(id);

        Assert.False(result.Ok);
        Assert.Contains(why, result.Say);
        Assert.Contains("study-stash", result.Written); // what to paste by hand
        Assert.Equal(text, File.ReadAllText(path));
        Assert.False(File.Exists(path + ".study-stash-backup"));
    }

    [Fact]
    public void A_new_settings_file_is_made_when_the_app_has_none_yet()
    {
        using var home = new TempHome();
        var setup = Setup(home);

        var added = setup.Connect("claude-desktop");

        Assert.True(added.Ok, added.Say);
        Assert.Null(added.Backup);
        Assert.NotNull(JsonNode.Parse(File.ReadAllText(setup.DesktopConfig))!["mcpServers"]![ClaudeTools.ServerName]);

        // A file that had no servers before has none after: not an empty mcpServers left behind.
        Write(setup.GeminiConfig, "{\n  \"theme\": \"Default\"\n}\n");
        Assert.True(setup.Connect("gemini").Ok);
        Assert.True(setup.Disconnect("gemini").Ok);
        Assert.Equal("{\n  \"theme\": \"Default\"\n}\n", File.ReadAllText(setup.GeminiConfig).ReplaceLineEndings("\n"));
    }

    [Fact]
    public void ChatGPT_connects_when_its_settings_have_no_mcp_servers_yet_and_goes_back_to_how_it_was()
    {
        using var home = new TempHome();
        var setup = Setup(home);
        // A Codex CLI that has only ever had a model picked: no [mcp_servers] anywhere in the file.
        const string before = "model = \"gpt-5\"\n";
        Write(setup.CodexConfig, before);
        Assert.False(setup.State("codex").Added);

        Assert.True(setup.Connect("codex").Ok);
        Assert.True(setup.State("codex") is { Added: true, Outdated: false });
        Assert.True(setup.Disconnect("codex").Ok);

        Assert.Equal(before, File.ReadAllText(setup.CodexConfig));
    }

    [Fact]
    public void The_chatgpt_row_is_for_the_app_that_reads_codexs_settings_not_chatgpt_classic()
    {
        using var home = new TempHome();
        // The merged ChatGPT app is Codex's own bundle; ChatGPT Classic isn't, and has no MCP servers on this computer.
        Write(home["ChatGPT.app/Contents/Info.plist"], "<plist><dict><key>CFBundleIdentifier</key><string>com.openai.codex</string></dict></plist>");
        Write(home["ChatGPT Classic.app/Contents/Info.plist"], "<plist><dict><key>CFBundleIdentifier</key><string>com.openai.chat</string></dict></plist>");
        Assert.True(ClaudeSetup.IsCodexBundle(home["ChatGPT.app/Contents/Info.plist"]));
        Assert.False(ClaudeSetup.IsCodexBundle(home["ChatGPT Classic.app/Contents/Info.plist"]));
        Assert.False(ClaudeSetup.IsCodexBundle(home["Nothing.app/Contents/Info.plist"]));

        var none = Setup(home);
        Assert.Equal("ChatGPT", none.State("codex").Name);
        Assert.False(none.State("codex").Installed);
        var withApp = new ClaudeSetup { Home = home["study-stash"], UserHome = home["user"], Find = _ => null, AppInstalled = name => name == "ChatGPT", CodexConfig = home["user/.codex/config.toml"] };
        Assert.True(withApp.State("codex").Installed);
    }

    [Theory]
    [InlineData("claude-desktop")]
    [InlineData("codex")]
    public void Study_stash_pasted_in_by_hand_is_offered_an_update_so_its_row_can_say_connected(string id)
    {
        using var home = new TempHome();
        var setup = Setup(home);
        // What "Copy setup" and the docs give: the command, with nothing saying which app started it.
        Write(setup.State(id).ConfigPath, id == "codex"
            ? $"[mcp_servers.study-stash]\ncommand = \"{Program}\"\nargs = [\"mcp\"]\n"
            : new JsonObject { ["mcpServers"] = new JsonObject { ["study-stash"] = new JsonObject { ["command"] = Program, ["args"] = new JsonArray("mcp") } } }.ToJsonString());

        var state = setup.State(id);
        Assert.True(state is { Added: true, OtherCopy: false, Outdated: true });
        var row = new AiAppRow { Id = id, Name = state.Name };
        row.Show(state, DateTime.Now);
        Assert.Equal("Update", row.ConnectWords);
        Assert.True(row.CanConnect);
        Assert.Contains("Set up by hand", row.Status);

        Assert.True(setup.Connect(id).Ok);

        Assert.True(setup.State(id) is { Added: true, Outdated: false });
        Assert.Contains("--client", File.ReadAllText(setup.State(id).ConfigPath));
    }

    [Fact]
    public void An_app_is_connected_once_it_has_started_study_stash_since_it_was_added()
    {
        var now = new DateTime(2026, 10, 4, 15, 0, 0);
        AiAppState State(bool added = true, DateTime? addedAt = null, DateTime? started = null, bool other = false, bool installed = true) =>
            new("claude-desktop", "Claude Desktop", installed, added, other, addedAt, started, "");
        var row = new AiAppRow { Id = "claude-desktop", Name = "Claude Desktop" };

        row.Show(State(added: false, installed: false), now);
        Assert.Equal("Not on this computer", row.Status);
        Assert.False(row.CanConnect);

        row.Show(State(added: false), now);
        Assert.Equal("Not connected", row.Status);
        Assert.True(row.CanConnect);
        Assert.False(row.CanDisconnect);

        row.Show(State(addedAt: now.AddMinutes(-1), started: now.AddMinutes(-30)), now);
        Assert.Equal("Added. Quit and reopen Claude Desktop to load it.", row.Status);
        Assert.False(row.Ok);
        Assert.True(row.CanDisconnect);

        row.Show(State(addedAt: now.AddMinutes(-30), started: now.AddMinutes(-1)), now);
        Assert.Equal("Connected · started 14:59", row.Status);
        Assert.True(row.Ok);
        Assert.False(row.CanConnect);

        row.Show(State(other: true), now);
        Assert.Equal("Fix", row.ConnectWords);
        Assert.True(row.CanConnect);
    }

    [Fact]
    public void The_mcp_server_notes_which_app_started_it_and_nothing_for_a_name_it_doesnt_know()
    {
        using var home = new TempHome();
        McpClients.Started(home.Path, "codex");
        McpClients.Started(home.Path, "../../evil");
        McpClients.Started(home.Path, null);

        Assert.NotNull(McpClients.When(McpClients.StartedFile(home.Path, "codex")));
        Assert.Single(Directory.GetFiles(Path.Combine(home.Path, "ai-apps")));
    }

    [Fact]
    public void In_a_test_this_computers_setup_reads_and_writes_no_real_app_settings()
    {
        using var home = new TempHome();
        var setup = ClaudeSetup.ThisComputer(home.Path);

        Assert.All(setup.States(), s => Assert.StartsWith(home.Path, s.ConfigPath));
        Assert.False(setup.Connect("claude-desktop").Ok);
        Assert.False(File.Exists(setup.DesktopConfig));
    }
}
