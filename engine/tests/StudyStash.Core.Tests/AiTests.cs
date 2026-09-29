using System.Text.Json.Nodes;
using StudyStash.Core.Ai;

namespace StudyStash.Core.Tests;

/// <summary>Picking an AI: ai.json, each provider's command line, and reading what each one prints.</summary>
public class AiTests
{
    [Fact]
    public void Without_ai_json_everything_stays_on_ollama()
    {
        using var dir = new TempDir();
        var s = AiSettings.Load(dir.Path);
        foreach (string job in AiSettings.Jobs) Assert.True(s.Local(job));
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
        var rw = claude.Command(new AiRequest("hi", "/lib") { Write = true, Tools = true, McpCommand = ["/app/studystash", "mcp"], Model = "sonnet" }, stream: true);
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
        var local = new OllamaProvider().Command(new AiRequest("q", "/lib") { Write = true }, stream: false);
        Assert.Contains("--oss", local);
        Assert.Equal("workspace-write", local[local.IndexOf("-s") + 1]);
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
        Assert.Equal("plan", g.Command(new AiRequest("q", "/lib"), false)[g.Command(new AiRequest("q", "/lib"), false).IndexOf("--mode") + 1]);
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
    }
}
