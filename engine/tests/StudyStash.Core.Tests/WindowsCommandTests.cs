using System.Text.Json.Nodes;
using StudyStash.Core.Ai;

namespace StudyStash.Core.Tests;

/// <summary>
/// A command-line AI npm installed on Windows (claude.cmd, codex.cmd) gets every argument exactly as written: the npm
/// shim is seen through to its own Node.js and script, and a batch file that isn't one gets a command line escaped for
/// cmd.exe. On Windows, a stand-in CLI (a Node.js script that prints what it was given) proves it for real; no AI
/// account is ever reached.
/// </summary>
public class WindowsCommandTests
{
    /// <summary>What npm's cmd-shim writes today for Claude Code.</summary>
    const string NpmShim = """
        @ECHO off
        GOTO start
        :find_dp0
        SET dp0=%~dp0
        EXIT /b
        :start
        SETLOCAL
        CALL :find_dp0

        IF EXIST "%dp0%\node.exe" (
          SET "_prog=%dp0%\node.exe"
        ) ELSE (
          SET "_prog=node"
          SET PATHEXT=%PATHEXT:;.JS;=;%
        )

        endLocal & goto #_undefined_# 2>NUL || title %COMSPEC% & "%_prog%"  "%dp0%\node_modules\@anthropic-ai\claude-code\cli.js" %*
        """;

    /// <summary>What npm 6 wrote, and pnpm still writes much like it.</summary>
    const string OldNpmShim = """
        @IF EXIST "%~dp0\node.exe" (
          "%~dp0\node.exe"  "%~dp0\node_modules\@openai\codex\bin\codex.js" %*
        ) ELSE (
          @SETLOCAL
          @SET PATHEXT=%PATHEXT:;.JS;=;%
          node  "%~dp0\node_modules\@openai\codex\bin\codex.js" %*
        )
        """;

    static string Local(string windowsPath) => windowsPath.Replace('\\', Path.DirectorySeparatorChar);

    static string Touch(TempDir dir, string relative, string text = "")
    {
        string path = Path.Combine(dir.Path, Local(relative));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text.ReplaceLineEndings("\r\n"));
        return path;
    }

    [Fact]
    public void An_npm_shim_is_seen_through_to_the_node_and_script_it_runs()
    {
        using var dir = new TempDir();
        string shim = Touch(dir, "claude.cmd", NpmShim);
        string script = Touch(dir, @"node_modules\@anthropic-ai\claude-code\cli.js", "// Claude Code");

        // No node.exe beside it: the one on the PATH, as the shim itself would pick.
        Assert.Equal(["/bin/node-on-path", script], WindowsCommand.SeeThrough(shim, _ => "/bin/node-on-path"));
        Assert.False(WindowsCommand.ThroughCmd(shim, _ => "/bin/node-on-path"));
        // A node.exe beside it (npm's own install of Node.js) comes first.
        string beside = Touch(dir, "node.exe");
        Assert.Equal([beside, script], WindowsCommand.SeeThrough(shim, _ => "/bin/node-on-path"));

        var launch = WindowsCommand.For([shim, "-p", "--append-system-prompt", "line one\nline two & 50%"], _ => null);
        Assert.Equal(beside, launch.FileName);
        Assert.Null(launch.CommandLine);
        Assert.Equal([script, "-p", "--append-system-prompt", "line one\nline two & 50%"], launch.Arguments);
    }

    [Fact]
    public void An_older_npm_or_pnpm_shim_is_seen_through_too()
    {
        using var dir = new TempDir();
        string shim = Touch(dir, "codex.cmd", OldNpmShim);
        string script = Touch(dir, @"node_modules\@openai\codex\bin\codex.js", "// Codex");
        Assert.Equal(["node", script], WindowsCommand.SeeThrough(shim, _ => "node"));
    }

    [Fact]
    public void A_shim_of_a_program_runs_the_program_itself()
    {
        using var dir = new TempDir();
        string shim = Touch(dir, "agy.cmd", "@\"%~dp0\\bin\\agy.exe\" %*\r\n");
        string exe = Touch(dir, @"bin\agy.exe");
        Assert.Equal([exe], WindowsCommand.SeeThrough(shim, _ => null));
    }

    [Fact]
    public void What_isnt_an_npm_shim_or_points_nowhere_goes_through_cmd()
    {
        using var dir = new TempDir();
        // A hand-written wrapper around something that isn't Node.js.
        string wrapper = Touch(dir, "fake-cli.cmd", "@echo off\npowershell -NoProfile -File \"%~dp0save.ps1\" %*\n");
        Touch(dir, "save.ps1");
        Assert.Null(WindowsCommand.SeeThrough(wrapper, _ => "node"));
        Assert.True(WindowsCommand.ThroughCmd(wrapper, _ => "node"));
        // An npm shim whose package was removed.
        string orphan = Touch(dir, "claude.cmd", NpmShim);
        Assert.Null(WindowsCommand.SeeThrough(orphan, _ => "node"));
        // No Node.js anywhere to run its script with.
        Touch(dir, @"node_modules\@anthropic-ai\claude-code\cli.js");
        Assert.Null(WindowsCommand.SeeThrough(orphan, _ => null));

        var launch = WindowsCommand.For([wrapper, "a & b"], _ => null);
        Assert.EndsWith("cmd.exe", launch.FileName, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(launch.Arguments);
        Assert.StartsWith("/d /s /c \"", launch.CommandLine);

        // A program that isn't a batch file starts as it is.
        var direct = WindowsCommand.For(["claude.exe", "-p"]);
        Assert.Equal(("claude.exe", null), (direct.FileName, direct.CommandLine));
        Assert.Equal(["-p"], direct.Arguments);
    }

    [Theory]
    [InlineData("plain", "\"plain\"")]
    [InlineData("two words", "\"two words\"")]
    [InlineData("say \"hi\"", "\"say \\\"hi\\\"\"")]
    [InlineData(@"C:\dir\", @"""C:\dir\\""")]
    [InlineData(@"a\\""b", @"""a\\\\\""b""")]
    [InlineData("", "\"\"")]
    public void An_argument_is_quoted_as_a_windows_program_reads_it(string arg, string quoted) =>
        Assert.Equal(quoted, WindowsCommand.Quote(arg));

    [Fact]
    public void Through_cmd_every_sign_it_reads_is_escaped_twice_and_line_breaks_become_spaces()
    {
        string line = WindowsCommand.CmdLine(@"C:\npm\fake cli.cmd", ["A & B | C", "50%", "one\ntwo"]);
        Assert.StartsWith(@"/d /s /c ""C:\npm\fake^ cli.cmd ", line);
        Assert.Contains("^^^&", line);
        Assert.Contains("^^^|", line);
        Assert.Contains("50^^^%", line);
        Assert.Contains("one^^^ two", line);
        Assert.DoesNotContain('\n', line);
        Assert.EndsWith("\"", line);
    }

    /// <summary>A system brief of several lines stays on claude's command line only where nothing can cut it: an
    /// npm shim seen through (or anywhere but Windows). A claude.cmd that would run through cmd.exe gets it on its
    /// input instead, ahead of the prompt, whole.</summary>
    [Fact]
    public void Claudes_brief_goes_where_nothing_cuts_it()
    {
        using var dir = new TempDir();
        string brief = "You are the study companion.\nAnswer from the notes: 50% & more | \"quoted\".";
        var req = new AiRequest("What is preload?", dir.Path) { System = brief };

        string shim = Touch(dir, @"npm\claude.cmd", NpmShim);
        Touch(dir, @"npm\node_modules\@anthropic-ai\claude-code\cli.js");
        Touch(dir, @"npm\node.exe");
        var npm = new ClaudeProvider { PromptOnInput = true, At = shim };
        var cmd = npm.Command(req, stream: false);
        Assert.Equal(brief, cmd[cmd.IndexOf("--append-system-prompt") + 1]);
        Assert.Equal("What is preload?", npm.Input(req));

        string wrapper = Touch(dir, @"other\claude.cmd", "@echo off\r\n\"C:\\tools\\claude-real.exe\" %*\r\n");
        var viaCmd = new ClaudeProvider { PromptOnInput = true, At = wrapper };
        Assert.DoesNotContain("--append-system-prompt", viaCmd.Command(req, stream: false));
        Assert.Equal(brief + "\n\n---\n\nWhat is preload?", viaCmd.Input(req));

        // A real program, or a Mac or Linux: the brief stays a system prompt, on the command line.
        var exe = new ClaudeProvider { PromptOnInput = true, At = Touch(dir, @"bin\claude.exe") };
        Assert.Contains(brief, exe.Command(req, stream: false));
        Assert.Equal("What is preload?", exe.Input(req));
    }

    /// <summary>Open in Claude Code on Windows: Windows Terminal is given the folder, the program and its arguments,
    /// with each ";" (which it would read as the start of another tab) escaped.</summary>
    [Fact]
    public void Windows_terminal_keeps_a_semicolon_in_an_argument()
    {
        var args = Terminal.WindowsTerminalArgs(@"C:\Notes\CS 101; Fall", @"C:\nodejs\node.exe", [@"C:\npm\cli.js", "--append-system-prompt", "This is the class \"R&D; lab\"."]);
        Assert.Equal(["-d", @"C:\Notes\CS 101\; Fall", @"C:\nodejs\node.exe", @"C:\npm\cli.js", "--append-system-prompt", "This is the class \"R&D\\; lab\"."], args);
    }

    /// <summary>A stand-in CLI that answers with one line of JSON: the arguments it was given and what came in on its
    /// input.</summary>
    sealed class EchoCli(List<string> cmd) : AiProvider
    {
        public override string Id => "echo";
        public override string Name => "Echo";
        public override string Binary => cmd[0];
        public override string Site => "";
        public override bool Available() => true;
        public override List<string> Command(AiRequest req, bool stream) => cmd;
        public override string? Input(AiRequest req) => req.Prompt;
        public override IEnumerable<AiEvent> Parse(string line) => line.Trim().Length > 0 ? [new AiEvent("final", line.Trim())] : [];
    }

    /// <summary>What cmd.exe would read as its own, a line break, quotes, backslashes, and words beyond ASCII.</summary>
    static readonly string[] Tricky =
    [
        "plain", "two words", "line one\nline two", "50% of %PATH% and %USERPROFILE%", "A & B | C > D < E ^ F",
        "say \"hi\" (twice) !bang!", "trailing backslash\\", @"C:\Program Files (x86)\Study Stash\", "",
        "café — ✓ CO₂ α", "semi;colon,comma=equals", "tab\there", "R&D \"lab\" 100%",
    ];

    /// <summary>
    /// For real, on Windows: an npm-style claude.cmd and a hand-written wrapper, each in front of a Node.js script
    /// that prints what it got. Through the npm shim every argument arrives byte for byte, line breaks too; through
    /// the wrapper (cmd.exe) every one arrives with its line breaks as spaces, and nothing is run that wasn't asked
    /// for. The prompt on the input is whole either way. Elsewhere there's no cmd.exe to go through.
    /// </summary>
    [Fact]
    public async Task On_windows_every_argument_reaches_an_npm_installed_cli_exactly()
    {
        if (!OperatingSystem.IsWindows()) return;
        string node = AiProvider.Which("node") ?? throw new InvalidOperationException("Node.js isn't on this Windows");
        using var dir = new TempDir();
        string script = Touch(dir, @"npm\node_modules\fake-cli\cli.js", """
            let input = "";
            process.stdin.setEncoding("utf8");
            process.stdin.on("data", d => input += d);
            process.stdin.on("end", () => process.stdout.write(JSON.stringify({ args: process.argv.slice(2), input }) + "\n"));
            """);
        string shim = Touch(dir, @"npm\fake-cli.cmd", NpmShim.Replace(@"@anthropic-ai\claude-code\cli.js", @"fake-cli\cli.js"));
        string wrapper = Touch(dir, @"tools\fake-cli.cmd", $"@echo off\r\n\"{node}\" \"{script}\" %*\r\n");
        string prompt = "Write notes.\nDr. Okafor: 50% of A & B | C, \"recursion\" — café";

        foreach (var (program, expected) in new[] { (shim, Tricky), (wrapper, Tricky.Select(t => t.Replace('\n', ' ')).ToArray()) })
        {
            var result = await new EchoCli([program, .. Tricky]).CompleteAsync(new AiRequest(prompt, dir.Path) { Timeout = TimeSpan.FromMinutes(1) });
            Assert.True(result.Ok, result.Text);
            var got = JsonNode.Parse(result.Text)!;
            Assert.Equal(expected, got["args"]!.AsArray().Select(a => (string)a!).ToArray());
            Assert.Equal(prompt, (string)got["input"]!);
        }
    }
}
