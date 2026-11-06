using System.Text.Json.Nodes;
using StudyStash.App.Services;
using StudyStash.App.ViewModels;
using StudyStash.Core;

namespace StudyStash.App.Tests;

/// <summary>Connecting the AI apps on this computer (Claude Desktop, Claude Code, ChatGPT, Gemini CLI, Antigravity) to the
/// library: what goes into the file each app really reads, that nothing else in it is ever lost, that Syft Notes' old
/// name and a copy left where an app no longer reads are put right, and where Claude Desktop reads on each computer.
/// Every file is in a test's own folder: never a real app's settings.</summary>
public class ClaudeSetupTests
{
    const string Program = "/Applications/Study Stash.app/Contents/MacOS/StudyStash";

    static ClaudeSetup Setup(TempHome home, string program = Program) => new()
    {
        Program = program, Home = home["study-stash"], UserHome = home["user"], Find = _ => null, AppInstalled = _ => false, QuitApp = null, OpenApp = null,
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

    [Theory]
    [InlineData("claude-desktop")]
    [InlineData("claude-code")]
    [InlineData("gemini")]
    [InlineData("antigravity")]
    public void Connecting_a_json_app_keeps_its_other_servers_and_settings_and_disconnecting_takes_out_only_syft_notes(string id)
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
        Assert.StartsWith("Added Syft Notes to ", added.Say);
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
        Assert.Equal("Added Syft Notes to ChatGPT.", added.Say);
        string text = File.ReadAllText(setup.CodexConfig);
        Assert.StartsWith(before.TrimEnd('\n'), text);
        Assert.Contains($"[mcp_servers.{ClaudeTools.ServerName}]\ncommand = \"{Program}\"\nargs = [\"--home\", \"{home["study-stash"].Replace("\\", "\\\\")}\", \"mcp\", \"--client\", \"codex\"]", text);
        Assert.True(setup.State("codex").Added);

        // Connecting again (another copy of the app, say) replaces Syft Notes' table rather than adding a second.
        var other = Setup(home, "/elsewhere/StudyStash");
        Assert.True(other.State("codex").OtherCopy);
        Assert.True(other.Connect("codex").Ok);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(File.ReadAllText(setup.CodexConfig), @"\[mcp_servers\.syft-notes\]"));
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
        Assert.Contains("syft-notes", result.Written); // what to paste by hand
        Assert.Equal(text, File.ReadAllText(path));
        Assert.False(File.Exists(path + ".syft-notes-backup"));
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
        var withApp = new ClaudeSetup { Home = home["study-stash"], UserHome = home["user"], Find = _ => null, AppInstalled = name => name == "ChatGPT", QuitApp = null, OpenApp = null, CodexConfig = home["user/.codex/config.toml"] };
        Assert.True(withApp.State("codex").Installed);
    }

    [Theory]
    [InlineData("claude-desktop")]
    [InlineData("claude-code")]
    [InlineData("codex")]
    public void Syft_notes_under_its_old_name_is_renamed_and_never_left_twice(string id)
    {
        using var home = new TempHome();
        var setup = Setup(home);
        // What an earlier Syft Notes (Study Stash) wrote: its old name, and the app's own server beside it.
        Write(setup.State(id).ConfigPath, id == "codex"
            ? $"[mcp_servers.github]\ncommand = \"gh-mcp\"\n\n[mcp_servers.study-stash]\ncommand = \"{Program}\"\nargs = [\"mcp\"]\n"
            : new JsonObject { ["mcpServers"] = new JsonObject { ["github"] = new JsonObject { ["command"] = "gh-mcp" }, ["study-stash"] = new JsonObject { ["command"] = Program, ["args"] = new JsonArray("mcp") } } }.ToJsonString());

        var before = setup.State(id);
        Assert.True(before is { Added: true, OldName: true, Outdated: true, Good: false });

        Assert.True(setup.Connect(id).Ok);

        string text = File.ReadAllText(setup.State(id).ConfigPath);
        Assert.DoesNotContain(id == "codex" ? "mcp_servers.study-stash" : "\"study-stash\":", text);
        Assert.Contains("syft-notes", text);
        Assert.Contains("gh-mcp", text);
        Assert.True(setup.State(id) is { Good: true, OldName: false });
        // Pressed again, nothing changes: no rewrite, and no backup of it.
        var again = setup.Connect(id);
        Assert.True(again.Ok);
        Assert.Null(again.Backup);
        Assert.Equal(text, File.ReadAllText(setup.State(id).ConfigPath));
    }

    /// <summary>A Windows home in a test's own folder, for a user name with a space and an accent in it.</summary>
    static ClaudeSetup Windows(TempHome home, string program = @"C:\Users\Zoë Test\AppData\Local\Programs\Syft Notes\StudyStash.exe") => new()
    {
        Program = program, Home = home[Path.Combine("Zoë Test", ".study-stash")], UserHome = home["Zoë Test"], Windows = true, Mac = false,
        LocalAppData = home[Path.Combine("Zoë Test", "AppData", "Local")], AppData = home[Path.Combine("Zoë Test", "AppData", "Roaming")],
        Find = _ => null, Running = _ => false, QuitApp = null, OpenApp = null, Probe = null,
    };

    const string Package = "Claude_pzs8sxrjxfjjc";

    /// <summary>Claude's own settings as the Microsoft Store build keeps them: keys Syft Notes must never lose.</summary>
    const string ClaudesOwn = """{"coworkUserFilesPath": "D:\\Uni", "preferences": {"menuBarEnabled": true, "quickEntryShortcut": "off"}}""";

    [Fact]
    public void Claude_on_windows_after_its_data_moved_reads_claude_data_and_the_entry_left_in_the_old_folder_is_taken_out()
    {
        using var home = new TempHome();
        var setup = Windows(home);
        // As a tester's PC had it: the Store build moved its data to Claude-Data (the old folder keeps Claude's marker and
        // the entry an earlier Syft Notes wrote there), and the moved settings have no mcpServers at all.
        string old = home[$"Zoë Test/AppData/Local/Packages/{Package}/LocalCache/Roaming/Claude/claude_desktop_config.json"];
        Write(old, new JsonObject { ["mcpServers"] = new JsonObject { ["study-stash"] = new JsonObject { ["command"] = setup.Program, ["args"] = new JsonArray("mcp", "--client", "claude-desktop") } } }.ToJsonString());
        Write(Path.Combine(Path.GetDirectoryName(old)!, ".user-data-left.claude_pzs8sxrjxfjjc"), "");
        string moved = home["Zoë Test/AppData/Local/Claude-Data/claude_pzs8sxrjxfjjc/claude_desktop_config.json"];
        Write(moved, ClaudesOwn);

        Assert.Equal(Path.GetFullPath(moved), Path.GetFullPath(setup.DesktopConfig));
        // Never "connected" from a file Claude ignores.
        Assert.True(setup.State("claude-desktop") is { Installed: true, Added: false, Stale: true, Good: false });

        var done = setup.Connect("claude-desktop");

        Assert.True(done.Ok, done.Say);
        var config = JsonNode.Parse(File.ReadAllText(moved))!;
        Assert.Equal(@"D:\Uni", config["coworkUserFilesPath"]!.GetValue<string>());
        Assert.True(config["preferences"]!["menuBarEnabled"]!.GetValue<bool>());
        Assert.Equal(setup.Program, config["mcpServers"]!["syft-notes"]!["command"]!.GetValue<string>());
        Assert.Contains("Zoë Test", File.ReadAllText(moved)); // the path as it is, not escaped
        Assert.Null(JsonNode.Parse(File.ReadAllText(old))!["mcpServers"]);
        Assert.True(File.Exists(old + ".syft-notes-backup"));
        Assert.True(setup.State("claude-desktop") is { Good: true, Stale: false });
    }

    [Theory]
    [InlineData("moved", "Claude-Data/claude_pzs8sxrjxfjjc")]
    [InlineData("marker only", "Claude-Data/claude_pzs8sxrjxfjjc")]
    [InlineData("not moved", $"Packages/{Package}/LocalCache/Roaming/Claude")]
    [InlineData("never opened", "Claude-Data/claude_pzs8sxrjxfjjc")]
    [InlineData("installer", "../Roaming/Claude")]
    public void Claude_on_windows_is_connected_where_its_build_reads(string how, string folder)
    {
        using var home = new TempHome();
        string local = home[Path.Combine("Zoë Test", "AppData", "Local")];
        string legacy = Path.Combine(local, "Packages", Package, "LocalCache", "Roaming", "Claude");
        switch (how)
        {
            case "moved":
                Directory.CreateDirectory(Path.Combine(local, "Claude-Data", "claude_pzs8sxrjxfjjc"));
                Directory.CreateDirectory(legacy);
                break;
            case "marker only":
                Write(Path.Combine(legacy, ".user-data-left.claude_pzs8sxrjxfjjc"), "");
                break;
            case "not moved":
                Write(Path.Combine(legacy, "claude_desktop_config.json"), ClaudesOwn);
                break;
            case "never opened":
                Directory.CreateDirectory(Path.Combine(local, "Packages", Package));
                break;
            default:
                Directory.CreateDirectory(Path.Combine(local, "AnthropicClaude"));
                break;
        }
        var setup = Windows(home);

        Assert.Equal(Path.GetFullPath(Path.Combine(local, folder, "claude_desktop_config.json")), Path.GetFullPath(setup.DesktopConfig));
        Assert.True(setup.State("claude-desktop").Installed);
        Assert.True(setup.Connect("claude-desktop").Ok);
        Assert.True(setup.State("claude-desktop").Good);
        var config = JsonNode.Parse(File.ReadAllText(setup.DesktopConfig))!;
        Assert.Equal(["--home", home[Path.Combine("Zoë Test", ".study-stash")], "mcp", "--client", "claude-desktop"],
            config["mcpServers"]!["syft-notes"]!["args"]!.AsArray().Select(a => a!.GetValue<string>()));
        if (how == "not moved") Assert.Equal(@"D:\Uni", config["coworkUserFilesPath"]!.GetValue<string>());
    }

    [Fact]
    public void Claude_on_a_mac_reads_application_support_and_gemini_cli_counts_only_when_its_own_command_is_here()
    {
        using var home = new TempHome();
        var mac = new ClaudeSetup { Home = home["s"], UserHome = home["Zoë Test"], Windows = false, Mac = true, Find = _ => null, AppInstalled = _ => false, Running = _ => false, Probe = null };
        Assert.Equal(Path.GetFullPath(home["Zoë Test/Library/Application Support/Claude/claude_desktop_config.json"]), Path.GetFullPath(mac.DesktopConfig));
        Assert.Empty(mac.Desktop.Others);

        // Antigravity keeps its things under ~/.gemini too: that folder alone isn't Gemini CLI.
        Directory.CreateDirectory(home["Zoë Test/.gemini/antigravity-cli"]);
        Assert.False(mac.State("gemini").Installed);
        var withAgy = new ClaudeSetup { Home = home["s"], UserHome = home["Zoë Test"], Windows = false, Mac = true, Find = c => c == "agy" ? "/x/agy" : null, AppInstalled = _ => false, Running = _ => false, Probe = null };
        Assert.True(withAgy.State("antigravity").Installed);
        Assert.Equal(Path.GetFullPath(home["Zoë Test/.gemini/config/mcp_config.json"]), Path.GetFullPath(withAgy.State("antigravity").ConfigPath));
        Assert.Equal(["antigravity", "gemini"], ClaudeSetup.AppsOf("gemini").Order());
        Assert.Equal(["claude-code", "claude-desktop"], ClaudeSetup.AppsOf("claude").Order());
    }

    [Fact]
    public async Task Reopen_closes_and_opens_the_rows_desktop_app_and_says_what_to_do_when_it_cant()
    {
        using var home = new TempHome();
        var asked = new List<string>();
        bool works = true;
        var setup = new ClaudeSetup
        {
            Home = home["study-stash"], UserHome = home["user"], Find = _ => null, AppInstalled = name => name is "ChatGPT" or "Claude",
            CodexConfig = home["user/.codex/config.toml"], DesktopConfig = home["user/Claude/claude_desktop_config.json"],
            QuitApp = id => { asked.Add("close " + id); return Task.FromResult(works); },
            OpenApp = id => { asked.Add("open " + id); return Task.FromResult(true); },
        };
        Assert.True(setup.State("codex").CanReopen);
        Assert.False(setup.State("gemini").CanReopen); // it lives in a terminal: nothing to reopen

        Assert.True((await setup.ReopenAsync("codex")).Ok);
        Assert.True((await setup.ReopenAsync("claude-desktop")).Ok);
        Assert.Equal(["close codex", "open codex", "close claude-desktop", "open claude-desktop"], asked);

        works = false; // it's asking about unsaved work, say: nothing is forced, and it isn't opened
        var stuck = await setup.ReopenAsync("codex");
        Assert.False(stuck.Ok);
        Assert.StartsWith("Couldn't close ChatGPT. Quit it", stuck.Say);
        Assert.Equal("close codex", asked[^1]);
        // Where Syft Notes can't close apps (a test that stands in for whether they're open), no row offers to.
        Assert.False(Setup(home).State("codex").CanReopen);
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
