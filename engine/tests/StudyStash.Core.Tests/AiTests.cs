using System.Text.Json.Nodes;
using StudyStash.Core.Ai;

namespace StudyStash.Core.Tests;

/// <summary>Picking an AI: ai.json, each provider's command line, and reading what each one prints.</summary>
public class AiTests
{
    [Fact]
    public void Without_ai_json_nobody_writes_the_notes_and_sorting_and_ask_keep_to_ollama()
    {
        using var dir = new TempDir();
        var s = AiSettings.Load(dir.Path);
        Assert.False(s.NotesChosen);
        Assert.Equal("", s.For("notes").Provider);
        Assert.False(s.Local("notes"));
        foreach (string job in new[] { "sort", "ask", "agent" }) Assert.True(s.Local(job));
        Assert.False(s.Picked("ask"));
    }

    [Fact]
    public void A_job_can_use_its_own_ai_and_the_rest_follow_the_main_one()
    {
        using var dir = new TempDir();
        var s = new AiSettings { Provider = "claude", Models = { ["claude"] = "sonnet" } };
        s.ByJob["sort"] = new AiChoice("ollama");
        s.Save(dir.Path);
        var back = AiSettings.Load(dir.Path);
        Assert.Equal(new AiChoice("claude", "sonnet"), back.For("notes"));
        Assert.True(back.Local("sort"));
        Assert.Contains("\"by_job\"", File.ReadAllText(AiSettings.PathIn(dir.Path)));
    }

    [Fact]
    public void An_ai_json_from_before_fallback_and_limits_still_loads()
    {
        using var dir = new TempDir();
        File.WriteAllText(AiSettings.PathIn(dir.Path), """{"provider": "claude", "models": {"claude": "sonnet"}}""");
        var s = AiSettings.Load(dir.Path);
        Assert.Equal("claude", s.Provider);
        Assert.True(s.Fallback);
        Assert.Empty(s.Limits);
        Assert.Empty(s.Dismissed);
    }

    [Fact]
    public void Json_is_found_inside_a_chatty_answer()
    {
        Assert.Equal("""{"a": "}"}""", AiJobs.FirstObject("Sure! ```json\n{\"a\": \"}\"}\n```"));
        Assert.Equal("""{"b":1}""", AiJobs.FirstObject("{not json} then {\"b\":1}"));
        Assert.Null(AiJobs.FirstObject("no json here"));
    }

    [Fact]
    public void Claude_reads_only_unless_it_may_write_and_then_only_in_its_folder()
    {
        var claude = new ClaudeProvider();
        var ro = claude.Command(new AiRequest("hi", "/lib"), stream: false);
        Assert.Contains("Edit", ro.SkipWhile(a => a != "--disallowedTools"));
        Assert.DoesNotContain("--include-partial-messages", ro);
        // A plain answer runs none of the student's own hooks or settings (Claude Code 2.1.248 on).
        Assert.Contains("--restricted", ro);
        var rw = claude.Command(new AiRequest("hi", "/lib") { Write = true, Tools = true, McpCommand = ["/app/studystash", "mcp"], Model = "sonnet" }, stream: true);
        Assert.DoesNotContain("--restricted", rw);
        Assert.Contains("Edit(/" + ClaudeProvider.ClaudePath(Path.GetFullPath("/lib")) + "/**)", rw);
        Assert.Equal("/c/Users/sam/lib", ClaudeProvider.ClaudePath(@"C:\Users\sam\lib"));
        Assert.Equal("/lib", ClaudeProvider.ClaudePath("/lib"));
        Assert.Contains("mcp__study-stash", rw);
        Assert.Equal("sonnet", rw[rw.IndexOf("--model") + 1]);
        Assert.Contains("\"study-stash\"", rw[rw.IndexOf("--mcp-config") + 1]);
    }

    [Fact]
    public void Claude_streams_text_tools_and_its_result()
    {
        var c = new ClaudeProvider();
        Assert.Equal("session", c.Parse("""{"type":"system","subtype":"init","session_id":"s1"}""").Single().Kind);
        Assert.Equal("Hel", c.Parse("""{"type":"stream_event","event":{"type":"content_block_delta","delta":{"type":"text_delta","text":"Hel"}}}""").Single().Text);
        var tool = c.Parse("""{"type":"assistant","message":{"content":[{"type":"tool_use","name":"Read","input":{"file_path":"/lib/a.md"}}]}}""").Single();
        Assert.Equal(("tool", "Read", "/lib/a.md"), (tool.Kind, tool.Name, tool.Path));
        var end = c.Parse("""{"type":"result","session_id":"s1","is_error":false,"result":"Hello"}""").ToList();
        Assert.Equal("Hello", end.Single(e => e.Kind == "final").Text);
        Assert.Equal("error", c.Parse("""{"type":"result","is_error":true,"result":"out of credits"}""").Last().Kind);
    }

    [Fact]
    public void Codex_answers_with_its_last_message_not_its_narration()
    {
        var codex = new CodexProvider();
        codex.Parse("""{"type":"thread.started","thread_id":"t1"}""").ToList();
        codex.Parse("""{"type":"item.completed","item":{"type":"agent_message","text":"I'll look at your notes."}}""").ToList();
        var tool = codex.Parse("""{"type":"item.started","item":{"type":"command_execution","command":"rg recursion"}}""").Single();
        Assert.Equal("rg recursion", tool.Path);
        codex.Parse("""{"type":"item.completed","item":{"type":"agent_message","text":"Recursion is a function calling itself."}}""").ToList();
        Assert.Empty(codex.Parse("""{"type":"error","message":"Reconnecting... 2/5 (stream disconnected)"}"""));
        var final = codex.Parse("""{"type":"turn.completed"}""").Single();
        Assert.Equal(("final", "Recursion is a function calling itself."), (final.Kind, final.Text));
    }

    [Fact]
    public void Codex_is_read_only_unless_it_may_write()
    {
        var cmd = new CodexProvider { PromptOnInput = false }.Command(new AiRequest("q", "/lib") { System = "brief" }, stream: false);
        Assert.Equal("read-only", cmd[cmd.IndexOf("-s") + 1]);
        Assert.StartsWith("brief", cmd[^1]);
        // A plain answer skips the student's own config.toml (and the MCP servers it would start), and their ChatGPT apps.
        Assert.Contains("--ignore-user-config", cmd);
        Assert.Contains("features.apps=false", cmd);
        var local = new OllamaProvider().Command(new AiRequest("q", "/lib") { Write = true }, stream: false);
        Assert.Contains("--oss", local);
        Assert.DoesNotContain("--ignore-user-config", local);
        Assert.Equal("workspace-write", local[local.IndexOf("-s") + 1]);
    }

    /// <summary>The library's plain answers (notes, Check, Ask) run the student's own AI with nothing of theirs in
    /// it: Claude without claude.ai's connectors; ChatGPT's Codex without their config.toml, unless its sign-in turns
    /// out to be kept there, and then with every MCP server it names turned off (Codex would start them all).</summary>
    [Fact]
    public async Task Plain_answers_run_nothing_of_the_students_own_and_codex_reads_its_config_only_for_a_sign_in_kept_there()
    {
        using var dir = new TempDir();
        string claudeExe = FakeAgents.WriteCli(dir["claude"], "claude");
        FakeAgents.Replay(dir["claude"], ["""{"type":"result","is_error":false,"result":"ready","session_id":"s1"}"""]);
        Assert.True((await new ClaudeProvider { At = claudeExe }.CompleteAsync(new AiRequest("hi", dir.Path))).Ok);
        Assert.Contains("ENABLE_CLAUDEAI_MCP_SERVERS", FakeAgents.EnvNames(dir["claude"], 1));
        Assert.Contains("--restricted", FakeAgents.Argv(dir["claude"], 1));

        string codexExe = FakeAgents.WriteCli(dir["codex"], "codex");
        File.WriteAllText(dir["config.toml"], "[mcp_servers.study-stash]\ncommand = \"studystash\"\n");
        FakeAgents.Replay(dir["codex"], ["""{"type":"thread.started","thread_id":"t-1"}""", """{"type":"turn.failed","error":{"message":"401 Unauthorized: not logged in"}}"""], 1);
        string[] works = ["""{"type":"thread.started","thread_id":"t-2"}""", """{"type":"item.completed","item":{"type":"agent_message","text":"ready"}}""", """{"type":"turn.completed"}"""];
        FakeAgents.Replay(dir["codex"], works, 2);
        FakeAgents.Replay(dir["codex"], works, 3);
        var codex = new CodexProvider { At = codexExe, ConfigPath = () => dir["config.toml"] };
        var first = await codex.CompleteAsync(new AiRequest("hi", dir.Path));
        Assert.True(first.Ok, first.Text);
        Assert.Contains("--ignore-user-config", FakeAgents.Argv(dir["codex"], 1));
        Assert.DoesNotContain("--ignore-user-config", FakeAgents.Argv(dir["codex"], 2));
        Assert.Contains("mcp_servers.study-stash.enabled=false", FakeAgents.Argv(dir["codex"], 2));
        Assert.Contains("notify=[]", FakeAgents.Argv(dir["codex"], 2));
        Assert.Contains("features.apps=false", FakeAgents.Argv(dir["codex"], 2));
        // Remembered: the next plain answer reads it at once, its servers still off.
        Assert.True((await codex.CompleteAsync(new AiRequest("again", dir.Path))).Ok);
        Assert.Equal(3, FakeAgents.Turns(dir["codex"]));
        Assert.Contains("mcp_servers.study-stash.enabled=false", FakeAgents.Argv(dir["codex"], 3));
    }

    /// <summary>A Codex older than --ignore-user-config turns the flag itself away: the answer is asked again without
    /// it (the student's MCP servers named off), and that's remembered. With the config skipped, the model the student
    /// set there is still the one used; a model provider of their own keeps their config read.</summary>
    [Fact]
    public async Task An_older_codex_answers_without_the_flag_and_the_students_own_model_stays()
    {
        using var dir = new TempDir();
        string[] works = ["""{"type":"thread.started","thread_id":"t"}""", """{"type":"item.completed","item":{"type":"agent_message","text":"ready"}}""", """{"type":"turn.completed"}"""];
        string old = FakeAgents.WriteCli(dir["old"], "codex");
        File.WriteAllText(Path.Combine(dir["old"], "refuse"), "--ignore-user-config");
        FakeAgents.Replay(dir["old"], works);
        File.WriteAllText(dir["old.toml"], "[mcp_servers.github]\ncommand = \"gh-mcp\"\n");
        var codex = new CodexProvider { At = old, ConfigPath = () => dir["old.toml"] };
        var first = await codex.CompleteAsync(new AiRequest("hi", dir.Path));
        Assert.True(first.Ok, first.Text);
        Assert.Contains("--ignore-user-config", FakeAgents.Argv(dir["old"], 1));
        Assert.DoesNotContain("--ignore-user-config", FakeAgents.Argv(dir["old"], 2));
        Assert.Contains("mcp_servers.github.enabled=false", FakeAgents.Argv(dir["old"], 2));
        Assert.True((await codex.CompleteAsync(new AiRequest("again", dir.Path))).Ok);
        Assert.Equal(3, FakeAgents.Turns(dir["old"])); // straight to it the next time

        string now = FakeAgents.WriteCli(dir["now"], "codex");
        FakeAgents.Replay(dir["now"], works);
        File.WriteAllText(dir["model.toml"], "model = \"gpt-5.5\"\n\n[mcp_servers.github]\ncommand = \"gh-mcp\"\nmodel = \"not-this\"\n");
        var cmd = new CodexProvider { At = now, ConfigPath = () => dir["model.toml"], PromptOnInput = true }.Command(new AiRequest("hi", dir.Path), stream: false);
        Assert.Contains("--ignore-user-config", cmd);
        Assert.Equal("gpt-5.5", cmd[cmd.IndexOf("-m") + 1]);
        Assert.Equal("o-pick", new CodexProvider { At = now, ConfigPath = () => dir["model.toml"] }.Command(new AiRequest("hi", dir.Path) { Model = "o-pick" }, stream: false) is var picked ? picked[picked.IndexOf("-m") + 1] : "");

        File.WriteAllText(dir["provider.toml"], "model_provider = 'azure'\nmodel = \"my-deployment\"\n[mcp_servers.github]\ncommand = \"gh-mcp\"\n");
        var own = new CodexProvider { At = now, ConfigPath = () => dir["provider.toml"] }.Command(new AiRequest("hi", dir.Path), stream: false);
        Assert.DoesNotContain("--ignore-user-config", own);
        Assert.Contains("mcp_servers.github.enabled=false", own);
        Assert.DoesNotContain("-m", own);
    }

    [Fact]
    public void On_windows_the_prompt_goes_in_on_input_where_no_command_line_can_cut_it()
    {
        Assert.Equal(OperatingSystem.IsWindows(), new ClaudeProvider().PromptOnInput);
        string prompt = "Write notes.\n\nTranscript: 50% of A & B | C, \"quoted\"";
        var req = new AiRequest(prompt, "/lib") { System = "brief" };

        var claude = new ClaudeProvider { PromptOnInput = true };
        Assert.DoesNotContain(prompt, claude.Command(req, stream: false));
        Assert.Equal("-p", claude.Command(req, stream: false)[1]);
        Assert.Equal(prompt, claude.Input(req));
        var claudeArgs = new ClaudeProvider { PromptOnInput = false };
        Assert.Equal(prompt, claudeArgs.Command(req, stream: false)[2]);
        Assert.Null(claudeArgs.Input(req));

        // Codex reads it from its input when told "-"; the system prompt comes first, as on its command line.
        var codex = new CodexProvider { PromptOnInput = true };
        Assert.Equal("-", codex.Command(req, stream: false)[^1]);
        Assert.StartsWith("brief", codex.Input(req));
        Assert.EndsWith(prompt, codex.Input(req));
        var resumed = codex.Command(req with { Session = "t1" }, stream: false);
        // -C and -s are exec's own options, which `resume` turns away: they come before it.
        Assert.Equal(new[] { "resume", "t1", "-" }, resumed.TakeLast(3));
        Assert.True(resumed.IndexOf("-C") < resumed.IndexOf("resume") && resumed.IndexOf("-s") < resumed.IndexOf("resume"));
        Assert.Equal("-", new OllamaProvider { PromptOnInput = true }.Command(req, stream: false)[^1]);

        // Antigravity keeps its prompt on the command line.
        Assert.Null(new GeminiProvider { PromptOnInput = true }.Input(req));
    }

    /// <summary>A stand-in CLI that saves what it was given on its input to a file, and answers "saved".</summary>
    sealed class SavingCli(string exe, string saveTo) : AiProvider
    {
        public override string Id => "saving";
        public override string Name => "Saving CLI";
        public override string Binary => exe;
        public override string Site => "";
        public override bool Available() => true;
        public override List<string> Command(AiRequest req, bool stream) => [exe, saveTo];
        public override string? Input(AiRequest req) => req.Prompt;
        public override IEnumerable<AiEvent> Parse(string line) => line.Trim().Length > 0 ? [new AiEvent("final", line.Trim())] : [];
    }

    /// <summary>An engine that runs no command: it says what it's given, as events.</summary>
    sealed class ScriptedAi(params AiEvent[] events) : AiProvider
    {
        public override string Id => "scripted";
        public override string Name => "Scripted";
        public override string Binary => "scripted";
        public override string Site => "";
        public override bool Available() => true;
        public override List<string> Command(AiRequest req, bool stream) => ["scripted"];
        public override IEnumerable<AiEvent> Parse(string line) => [];
#pragma warning disable CS1998 // the events are all there already
        public override async IAsyncEnumerable<AiEvent> RunAsync(AiRequest req, bool stream = true, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
#pragma warning restore CS1998
        {
            foreach (var e in events) yield return e;
        }
    }

    [Fact]
    public async Task An_engine_that_finishes_without_answering_has_failed_so_no_notes_are_saved_empty()
    {
        // Antigravity in print mode, when it refuses a tool it wanted: a successful end, and not a word.
        var silent = await new ScriptedAi(new AiEvent("session", "c1"), new AiEvent("final", "  \n")).CompleteAsync(new AiRequest("Write notes.", "/lib"));
        Assert.False(silent.Ok);
        Assert.Equal("Scripted finished without answering.", silent.Text);

        var answered = await new ScriptedAi(new AiEvent("text", "## Summary\n"), new AiEvent("final", "")).CompleteAsync(new AiRequest("Write notes.", "/lib"));
        Assert.True(answered.Ok);
        Assert.Equal("## Summary", answered.Text);
    }

    [Fact]
    public async Task A_long_prompt_reaches_the_cli_whole_through_its_input_even_an_npm_style_cmd_on_windows()
    {
        using var dir = new TempDir();
        string saved = dir["saved.txt"];
        string exe;
        if (OperatingSystem.IsWindows())
        {
            // What npm installs: a .cmd that Windows runs through cmd.exe, which hands its input on to the real program.
            File.WriteAllText(dir["save.ps1"], "$in = [Console]::OpenStandardInput(); $out = [IO.File]::Create($args[0]); $in.CopyTo($out); $out.Close(); 'saved'\r\n");
            exe = dir["fake-cli.cmd"];
            File.WriteAllText(exe, "@echo off\r\npowershell -NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"%~dp0save.ps1\" %*\r\n");
        }
        else
        {
            exe = dir["fake-cli"];
            File.WriteAllText(exe, "#!/bin/sh\ncat > \"$1\"\necho saved\n");
            File.SetUnixFileMode(exe, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
        // Longer than any Windows command line (32,767 characters), with line breaks and what cmd.exe reads as its own.
        var prompt = new System.Text.StringBuilder("Write study notes for this lecture.\n\n");
        while (prompt.Length < 60_000) prompt.Append("Dr. Okafor: 50% of A & B | C > D, \"recursion\" — café ^ %PATH% !\r\n");

        var result = await new SavingCli(exe, saved).CompleteAsync(new AiRequest(prompt.ToString(), dir.Path) { Timeout = TimeSpan.FromMinutes(2) });

        Assert.True(result.Ok, result.Text);
        Assert.Equal("saved", result.Text);
        Assert.Equal(System.Text.Encoding.UTF8.GetBytes(prompt.ToString()), File.ReadAllBytes(saved));
    }

    [Fact]
    public async Task Ollama_at_localhost_is_reached_though_it_listens_on_127_0_0_1_only()
    {
        // Ollama listens on 127.0.0.1 alone; "localhost" goes there first rather than to ::1 (which Windows tries
        // first, and takes a couple of seconds to give up on).
        var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        int port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        var serving = Task.Run(async () =>
        {
            using var client = await listener.AcceptTcpClientAsync();
            var stream = client.GetStream();
            var asked = new System.Text.StringBuilder();
            var buffer = new byte[1024];
            while (!asked.ToString().Contains("\r\n\r\n", StringComparison.Ordinal))
            {
                int n = await stream.ReadAsync(buffer);
                if (n == 0) break;
                asked.Append(System.Text.Encoding.ASCII.GetString(buffer, 0, n));
            }
            string body = """{"models":[{"name":"qwen3:8b","size":5200000000}]}""";
            await stream.WriteAsync(System.Text.Encoding.ASCII.GetBytes(
                $"HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n{body}"));
            return asked.ToString();
        });
        try
        {
            var models = await Ollama.ListModelsAsync($"http://localhost:{port}");
            Assert.NotNull(models);
            Assert.Equal("qwen3:8b", Assert.Single(models).Name);
            Assert.StartsWith("GET /api/tags", await serving);
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public async Task An_ollama_that_isnt_running_is_known_soon()
    {
        // Nothing listens here. Windows takes two seconds to refuse each of localhost's two addresses (and some PCs
        // never refuse): the library's settings and setup's notes step waited four or five for an Ollama most
        // students don't have.
        var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        int port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        var watch = System.Diagnostics.Stopwatch.StartNew();
        Assert.Null(await Ollama.ListModelsAsync($"http://localhost:{port}"));
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(3.5), $"took {watch.Elapsed}");
    }

    [Fact]
    public void Only_this_computers_own_addresses_are_given_the_short_wait()
    {
        Assert.Equal([System.Net.IPAddress.Loopback, System.Net.IPAddress.IPv6Loopback], Ollama.LocalAddresses("localhost"));
        Assert.Equal([System.Net.IPAddress.Loopback, System.Net.IPAddress.IPv6Loopback], Ollama.LocalAddresses("LocalHost"));
        Assert.Equal([System.Net.IPAddress.Loopback], Ollama.LocalAddresses("127.0.0.1"));
        Assert.Equal([System.Net.IPAddress.IPv6Loopback], Ollama.LocalAddresses("::1"));
        // Another computer (an Ollama on the Mac mini, over Tailscale) keeps the usual wait.
        Assert.Null(Ollama.LocalAddresses("mac-mini"));
        Assert.Null(Ollama.LocalAddresses("100.64.0.7"));
        Assert.Null(Ollama.LocalAddresses("192.168.1.20"));
    }

    [Fact]
    public void Gemini_reads_its_step_updates()
    {
        var g = new GeminiProvider();
        Assert.Equal("c1", g.Parse("""{"event":"init","conversation_id":"c1"}""").Single().Text);
        Assert.Equal("Hi", g.Parse("""{"event":"step_update","step_update":{"state":"ACTIVE","step_type":"agent_response","text_delta":"Hi"}}""").Single().Text);
        var tool = g.Parse("""{"event":"step_update","step_update":{"state":"ACTIVE","step_type":"tool","tool_name":"view_file","tool_info":{"parameters":{"AbsolutePath":"/lib/x.md"}}}}""").Single();
        Assert.Equal(("view_file", "/lib/x.md"), (tool.Name, tool.Path));
        Assert.Empty(g.Parse("""{"event":"step_update","step_update":{"state":"DONE","step_type":"tool"}}"""));
        Assert.Equal("Done", g.Parse("""{"event":"result","result":{"status":"SUCCESS","response":"Done","conversation_id":"c1"}}""").Last().Text);
        Assert.Equal("error", g.Parse("""{"event":"result","result":{"status":"FAILED","error":"quota"}}""").Single().Kind);
        // A plain answer runs as Syft Notes' answer agent, with no tools to reach for; agent work in plan mode.
        var plain = g.Command(new AiRequest("q", "/lib"), false);
        Assert.Equal("study-stash-answer", plain[plain.IndexOf("--agent") + 1]);
        Assert.DoesNotContain("--mode", plain);
        var agent = g.Command(new AiRequest("q", "/lib") { Tools = true }, false);
        Assert.Equal("plan", agent[agent.IndexOf("--mode") + 1]);
    }

    [Fact]
    public void Gemini_may_run_read_only_commands_and_its_settings_are_kept()
    {
        using var dir = new TempDir();
        string path = dir["settings.json"];
        File.WriteAllText(path, """{"colorScheme":"dark","permissions":{"allow":["command(ls)"]}}""");
        Assert.True(GeminiProvider.AllowReading(path));
        var j = JsonNode.Parse(File.ReadAllText(path))!;
        Assert.Equal("dark", j["colorScheme"]!.GetValue<string>());
        var allow = j["permissions"]!["allow"]!.AsArray().Select(a => a!.GetValue<string>()).ToList();
        Assert.Single(allow, "command(ls)");
        Assert.Contains("command(grep)", allow);
        Assert.False(GeminiProvider.AllowReading(path)); // already there
        Assert.False(GeminiProvider.AllowReading(dir["missing.json"]));

        // The turn's own folders may be read too: in print mode a file it can't open ends the turn with nothing said.
        Assert.True(GeminiProvider.AllowReading(path, ["/lib/scratch/", "/lib/pool", ""]));
        allow = JsonNode.Parse(File.ReadAllText(path))!["permissions"]!["allow"]!.AsArray().Select(a => a!.GetValue<string>()).ToList();
        // As the computer writes the folder (C:\lib\scratch on Windows), with no separator at its end.
        string Rule(string folder) => $"read_file({Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder))})";
        Assert.Contains(Rule("/lib/scratch"), allow);
        Assert.Contains(Rule("/lib/pool"), allow);
        Assert.DoesNotContain(allow, a => a.StartsWith("read_file()", StringComparison.Ordinal));
        Assert.False(GeminiProvider.AllowReading(path, ["/lib/pool"])); // already there
    }
}
