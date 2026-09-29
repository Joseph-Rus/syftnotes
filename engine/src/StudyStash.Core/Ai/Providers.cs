using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace StudyStash.Core.Ai;

/// <summary>
/// One piece of work for an AI: what to ask, the folder it works in, and what it may do there. A job with
/// <see cref="Tools"/> off and <see cref="Write"/> off just answers (notes, sorting, Ask); with them on, the AI
/// works like an agent: it reads the library and any folders it's given, uses Study Stash's MCP tools (Canvas),
/// and with <see cref="Write"/> may change files in <see cref="Cwd"/>.
/// </summary>
public sealed record AiRequest(string Prompt, string Cwd)
{
    public string System { get; init; } = "";
    public IReadOnlyList<string> ReadDirs { get; init; } = [];
    public bool Write { get; init; }
    public string Model { get; init; } = "";
    public bool Tools { get; init; }
    /// <summary>The command that starts Study Stash's MCP server (the engine itself, told `mcp`).</summary>
    public IReadOnlyList<string> McpCommand { get; init; } = [];
    /// <summary>Carries on a conversation this provider started (its own id for it).</summary>
    public string Session { get; init; } = "";
    public TimeSpan Timeout { get; init; } = TimeSpan.FromMinutes(25);
}

/// <summary>What an AI did: <c>session</c> (its conversation id), <c>text</c> (more of the answer), <c>tool</c> (it
/// used one: name and what on), <c>final</c> (the whole answer, when it says it separately) or <c>error</c>.</summary>
public sealed record AiEvent(string Kind, string Text = "", string Name = "", string Path = "")
{
    public static AiEvent Error(string message) => new("error", message);
}

/// <summary>A finished run: its answer (or why it failed) and the conversation id to continue it.</summary>
public sealed record AiResult(bool Ok, string Text, string Session, IReadOnlyList<AiEvent> Tools);

/// <summary>
/// One AI a person may pick. Each provider is its own command-line agent, signed in with the person's own account
/// and plan (Claude Code, OpenAI's Codex for ChatGPT, Google's Antigravity for Gemini), or Ollama on this computer.
/// Study Stash never holds their keys.
/// </summary>
public abstract class AiProvider
{
    public abstract string Id { get; }
    public abstract string Name { get; }
    /// <summary>The command it runs ("claude", "codex", "agy", "ollama").</summary>
    public abstract string Binary { get; }
    /// <summary>Where to get it.</summary>
    public abstract string Site { get; }
    /// <summary>Models to offer: id ("" is the provider's own default) and what it's good for.</summary>
    public virtual IReadOnlyList<(string Id, string Label)> Models => [("", "Its default model")];

    /// <summary>Where its command is, when that's not where <see cref="Which"/> looks (a test's stand-in).</summary>
    public string? At { get; init; }

    public virtual string? Exe() => At ?? Which(Binary);
    public virtual bool Available() => Exe() is not null;

    /// <summary>The prompt goes in on the command's input, not its command line (for the CLIs that read it there: see
    /// <see cref="Input"/>). On by default on Windows: a command line there holds at most 32,767 characters, fewer
    /// than a long lecture's transcript, and a CLI npm installed (claude.cmd, codex.cmd) runs through cmd.exe, which
    /// cuts its command line at the first line break and reads &amp;, | and % as its own.</summary>
    public bool PromptOnInput { get; init; } = OperatingSystem.IsWindows();

    public abstract List<string> Command(AiRequest req, bool stream);
    public abstract IEnumerable<AiEvent> Parse(string line);

    /// <summary>What to write to the command's input before it's closed: the prompt, for a CLI that reads it there
    /// while <see cref="PromptOnInput"/> is on; null (nothing) otherwise.</summary>
    public virtual string? Input(AiRequest req) => null;

    /// <summary>Gets ready to answer soon (a student has started typing a question), so the answer starts sooner:
    /// true when it did something. Only a local model has anything to do (load itself into memory); a CLI starts
    /// with each question.</summary>
    public virtual Task<bool> WarmAsync(string model, CancellationToken ct = default) => Task.FromResult(false);

    /// <summary>What it does, as it does it. Ends with an <c>error</c> event when it fails.</summary>
    public virtual async IAsyncEnumerable<AiEvent> RunAsync(AiRequest req, bool stream = true,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        if (!Available())
        {
            yield return AiEvent.Error($"{Name} isn't installed on this computer ({Site}).");
            yield break;
        }
        await foreach (var e in Spawn(Command(req, stream), req.Cwd, req.Timeout, Parse, Input(req), ct)) yield return e;
    }

    /// <summary>Run to the end: the answer (the provider's final word, else everything it wrote), or why not. With
    /// <paramref name="soFar"/> it streams: that hears everything written so far each time more arrives (or the
    /// final word, from a provider that only says it at the end).</summary>
    public async Task<AiResult> CompleteAsync(AiRequest req, CancellationToken ct = default, Action<string>? soFar = null)
    {
        var text = new StringBuilder();
        string final = "", session = "", error = "";
        var tools = new List<AiEvent>();
        await foreach (var e in RunAsync(req, stream: soFar is not null, ct))
        {
            switch (e.Kind)
            {
                case "text":
                    text.Append(e.Text);
                    soFar?.Invoke(text.ToString());
                    break;
                case "final":
                    final = e.Text;
                    if (text.Length == 0 && final.Length > 0) soFar?.Invoke(final);
                    break;
                case "session" when e.Text.Length > 0: session = e.Text; break;
                case "tool": tools.Add(e); break;
                case "error": error = e.Text; break;
            }
        }
        string answer = Py.Strip(final.Length > 0 ? final : text.ToString());
        return error.Length > 0 ? new AiResult(false, error, session, tools) : new AiResult(true, answer, session, tools);
    }

    // --- running a command ------------------------------------------------------------------------------------

    /// <summary>Where these commands usually live: a background service starts with a short PATH. On Windows also
    /// the PATH as Windows has it now: an engine (or Node.js, which npm's claude.cmd and codex.cmd run on) installed
    /// while Study Stash was running isn't on the PATH it started with, and "Check again" in setup should find it.
    /// Codex's own Windows installer puts it in %LOCALAPPDATA%\Programs\OpenAI\Codex\bin.</summary>
    public static string SearchPath()
    {
        string home = Py.UserHome();
        string[] extra = OperatingSystem.IsWindows()
            ? [.. WindowsPathNow(), Path.Combine(home, "AppData", "Roaming", "npm"), Path.Combine(home, ".local", "bin"),
               Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "OpenAI", "Codex", "bin"),
               Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "nodejs")]
            : ["/opt/homebrew/bin", "/usr/local/bin", "/usr/bin", "/bin", Path.Combine(home, ".local", "bin"),
               Path.Combine(home, ".npm-global", "bin"), Path.Combine(home, ".bun", "bin"), Path.Combine(home, ".claude", "local")];
        var dirs = new[] { Environment.GetEnvironmentVariable("PATH") ?? "" }.Concat(extra)
            .SelectMany(p => p.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Distinct(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        return string.Join(Path.PathSeparator, dirs);
    }

    /// <summary>Windows: the folders on this account's PATH and the computer's, as the registry has them now (an
    /// installer that just ran has already added its own). Empty elsewhere, or when they can't be read.</summary>
    static IEnumerable<string> WindowsPathNow()
    {
        if (!OperatingSystem.IsWindows()) return [];
        var found = new List<string>();
        try
        {
            using var user = Microsoft.Win32.Registry.CurrentUser.OpenSubKey("Environment");
            using var machine = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Session Manager\Environment");
            foreach (var key in new[] { machine, user })
                if (key?.GetValue("Path") is string path) found.AddRange(path.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        }
        catch (Exception e) when (e is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
        }
        return found;
    }

    public static string? Which(string name)
    {
        string[] exts = OperatingSystem.IsWindows() ? [".exe", ".cmd", ".bat", ""] : [""];
        foreach (string dir in SearchPath().Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            foreach (string ext in exts)
            {
                string p = Path.Combine(dir, name + ext);
                if (File.Exists(p)) return p;
            }
        return null;
    }

    /// <summary>Start a command and read its lines as they come, turning each into events. A command that fails
    /// without saying why ends with the tail of what it printed to stderr.</summary>
    protected static async IAsyncEnumerable<AiEvent> Spawn(List<string> cmd, string cwd, TimeSpan timeout,
        Func<string, IEnumerable<AiEvent>> parse, string? input = null, [EnumeratorCancellation] CancellationToken ct = default)
    {
        // On Windows an npm-installed CLI is a .cmd, which cmd.exe would mangle the arguments of: see WindowsCommand.
        var launch = OperatingSystem.IsWindows() ? WindowsCommand.For(cmd) : new Launch(cmd[0], cmd[1..]);
        var psi = new ProcessStartInfo(launch.FileName)
        {
            WorkingDirectory = cwd, RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true,
            UseShellExecute = false, CreateNoWindow = true, StandardOutputEncoding = Encoding.UTF8,
            // UTF-8 with no byte-order mark: Windows would otherwise send the prompt in its old code page.
            StandardInputEncoding = new UTF8Encoding(false),
        };
        if (launch.CommandLine is { } whole) psi.Arguments = whole;
        else foreach (string a in launch.Arguments) psi.ArgumentList.Add(a);
        psi.Environment["PATH"] = SearchPath();
        psi.Environment.Remove("CLAUDECODE"); // started from inside Claude Code, claude would refuse to nest
        Process? p;
        string? startError = null;
        try
        {
            p = Process.Start(psi);
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            p = null;
            startError = e.Message;
        }
        if (p is null)
        {
            yield return AiEvent.Error($"{Path.GetFileName(cmd[0])} didn't start: {startError}");
            yield break;
        }
        using var proc = p;
        var feeding = Task.Run(() => Feed(proc.StandardInput, input), CancellationToken.None);
        var stderr = proc.StandardError.ReadToEndAsync(CancellationToken.None);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);
        bool final = false, failed = false, timedOut = false;
        try
        {
            while (true)
            {
                string? line;
                try
                {
                    line = await proc.StandardOutput.ReadLineAsync(cts.Token);
                }
                catch (OperationCanceledException)
                {
                    timedOut = !ct.IsCancellationRequested;
                    break;
                }
                if (line is null) break;
                foreach (var e in parse(line))
                {
                    final |= e.Kind == "final";
                    failed |= e.Kind == "error";
                    yield return e;
                }
            }
            if (timedOut)
            {
                yield return AiEvent.Error($"{Path.GetFileName(cmd[0])} didn't finish within {timeout.TotalMinutes:0} minutes.");
                yield break;
            }
            if (!proc.WaitForExit(30_000)) yield break;
            if (proc.ExitCode != 0 && !final && !failed)
            {
                string why = Py.Strip(await stderr);
                yield return AiEvent.Error(why.Length > 0 ? Py.Tail(why, 800) : $"{Path.GetFileName(cmd[0])} stopped (exit {proc.ExitCode}).");
            }
        }
        finally
        {
            if (!proc.HasExited)
                try { proc.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            await feeding;
        }
    }

    /// <summary>Writes <paramref name="text"/> (if any) to a command's input, then closes it so the command knows
    /// that's all. Run in the background: a long prompt fills the pipe before the command starts reading. A command
    /// that stops early just leaves the rest unwritten.</summary>
    static async Task Feed(StreamWriter stdin, string? text)
    {
        try
        {
            if (text is not null) await stdin.WriteAsync(text);
            stdin.Close();
        }
        catch (Exception e) when (e is IOException or ObjectDisposedException)
        {
        }
    }

    protected static JsonObject? Json(string line)
    {
        try
        {
            return JsonNode.Parse(line) as JsonObject;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    protected static string S(JsonNode? v) => v is JsonValue j && j.GetValueKind() == JsonValueKind.String ? j.GetValue<string>() : "";

    /// <summary>The system brief goes first in the prompt for CLIs that don't take one separately.</summary>
    protected static string WithSystem(AiRequest req) =>
        req.System.Length > 0 && req.Session.Length == 0 ? req.System + "\n\n---\n\n" + req.Prompt : req.Prompt;
}

/// <summary>Claude, through Claude Code (<c>claude -p</c>).</summary>
public sealed class ClaudeProvider : AiProvider
{
    public override string Id => "claude";
    public override string Name => "Claude";
    public override string Binary => "claude";
    public override string Site => "https://claude.com/claude-code";
    public override IReadOnlyList<(string Id, string Label)> Models =>
        [("sonnet", "Sonnet: fast, great for studying"), ("opus", "Opus: strongest, costs more"), ("haiku", "Haiku: fastest, cheapest")];

    /// <summary>A folder as Claude Code's rules spell it: "/Users/x" stays; Windows' "C:\Users\x" is "/c/Users/x".</summary>
    internal static string ClaudePath(string full) =>
        full.Length > 1 && full[1] == ':' ? "/" + char.ToLowerInvariant(full[0]) + full[2..].Replace('\\', '/').TrimEnd('/')
            : "/" + full.TrimStart('/');

    public override List<string> Command(AiRequest req, bool stream)
    {
        // Edit and Write are allowed only inside the working folder: "//" makes the rule an absolute path.
        string root = ClaudePath(Path.GetFullPath(req.Cwd));
        var allowed = new List<string> { "Read", "Grep", "Glob" };
        var never = new List<string> { "Bash", "NotebookEdit", "WebFetch", "WebSearch", "Task" };
        if (req.Write) allowed.AddRange([$"Edit(/{root}/**)", $"Write(/{root}/**)"]);
        else never.AddRange(["Edit", "Write"]);
        string exe = Exe() ?? "claude";
        var cmd = new List<string> { exe, "-p" };
        if (!PromptOnInput) cmd.Add(req.Prompt);
        cmd.AddRange(["--output-format", "stream-json", "--verbose"]);
        if (stream) cmd.Add("--include-partial-messages");
        // A plain answer uses no tools: don't wait for the person's own MCP servers to start first.
        if (!req.Tools) cmd.Add("--strict-mcp-config");
        if (req.Tools && req.McpCommand.Count > 0)
        {
            var server = new JsonObject { ["command"] = req.McpCommand[0], ["args"] = new JsonArray(req.McpCommand.Skip(1).Select(a => (JsonNode)a).ToArray()) };
            cmd.AddRange(["--mcp-config", new JsonObject { ["mcpServers"] = new JsonObject { [ClaudeTools.ServerName] = server } }.ToJsonString()]);
            allowed.Add("mcp__" + ClaudeTools.ServerName);
        }
        cmd.Add("--allowedTools");
        cmd.AddRange(allowed);
        cmd.Add("--disallowedTools");
        cmd.AddRange(never);
        if (req.System.Length > 0 && !SystemOnInput(exe)) cmd.AddRange(["--append-system-prompt", req.System]);
        foreach (string d in req.ReadDirs) cmd.AddRange(["--add-dir", d]);
        if (req.Model.Length > 0) cmd.AddRange(["--model", req.Model]);
        if (req.Session.Length > 0) cmd.AddRange(["--resume", req.Session]);
        return cmd;
    }

    /// <summary>`claude -p` with no prompt on its command line reads it from its input, the system brief first when it
    /// can't go on the command line (<see cref="SystemOnInput"/>).</summary>
    public override string? Input(AiRequest req) => !PromptOnInput ? null : SystemOnInput(Exe() ?? "claude") ? WithSystem(req) : req.Prompt;

    /// <summary>Whether the system brief goes in on the input, ahead of the prompt, instead of on the command line:
    /// only for a claude that would run through cmd.exe (a claude.cmd that isn't an npm shim
    /// <see cref="WindowsCommand"/> can see through), where a brief of several lines would be cut at its first.</summary>
    bool SystemOnInput(string exe) => PromptOnInput && WindowsCommand.ThroughCmd(exe);

    public override IEnumerable<AiEvent> Parse(string line)
    {
        if (Json(line) is not { } e) yield break;
        string type = S(e["type"]);
        if (type == "system" && S(e["subtype"]) == "init") yield return new AiEvent("session", S(e["session_id"]));
        else if (type == "stream_event" && e["parent_tool_use_id"] is null)
        {
            var ev = e["event"];
            if (S(ev?["type"]) == "content_block_delta" && S(ev?["delta"]?["type"]) == "text_delta")
                yield return new AiEvent("text", S(ev?["delta"]?["text"]));
        }
        else if (type == "assistant")
        {
            foreach (var b in e["message"]?["content"] as JsonArray ?? [])
            {
                if (S(b?["type"]) != "tool_use") continue;
                var input = b?["input"];
                string on = new[] { "file_path", "path", "url", "save_to", "pattern" }.Select(k => S(input?[k])).FirstOrDefault(v => v.Length > 0) ?? "";
                yield return new AiEvent("tool", Name: S(b?["name"]), Path: on);
            }
        }
        else if (type == "result")
        {
            yield return new AiEvent("session", S(e["session_id"]));
            bool isError = e["is_error"] is JsonValue v && v.GetValueKind() == JsonValueKind.True;
            yield return isError ? AiEvent.Error(S(e["result"]) is { Length: > 0 } r ? r : "Claude stopped with an error.") : new AiEvent("final", S(e["result"]));
        }
    }
}

/// <summary>ChatGPT, through OpenAI's Codex CLI (<c>codex exec</c>), signed in with a ChatGPT account.</summary>
public class CodexProvider : AiProvider
{
    public override string Id => "codex";
    public override string Name => "ChatGPT";
    public override string Binary => "codex";
    public override string Site => "https://developers.openai.com/codex/cli";
    public override IReadOnlyList<(string Id, string Label)> Models => [("", "Codex's default model")];

    /// <summary>Extra arguments before the prompt (a local model's, for <see cref="OllamaProvider"/>).</summary>
    protected virtual IEnumerable<string> ExtraArgs => [];

    public override List<string> Command(AiRequest req, bool stream)
    {
        // The next turn of a conversation is `codex exec [options] resume ID prompt`: -C and -s are exec's own options
        // and `resume` turns them away, so every option comes before it.
        var cmd = new List<string> { Which("codex") ?? "codex", "exec" };
        cmd.AddRange(ExtraArgs);
        cmd.AddRange(["--json", "--skip-git-repo-check", "-C", req.Cwd, "-s", req.Write ? "workspace-write" : "read-only",
            "-c", "approval_policy=\"never\""]);
        foreach (string d in req.ReadDirs) cmd.AddRange(["--add-dir", d]);
        if (req.Tools && req.McpCommand.Count > 0)
            cmd.AddRange(["-c", $"mcp_servers.{McpKey}.command={JsonSerializer.Serialize(req.McpCommand[0])}",
                "-c", $"mcp_servers.{McpKey}.args={new JsonArray(req.McpCommand.Skip(1).Select(a => (JsonNode)a).ToArray()).ToJsonString()}"]);
        if (req.Model.Length > 0) cmd.AddRange(["-m", req.Model]);
        if (req.Session.Length > 0) cmd.AddRange(["resume", req.Session]);
        cmd.Add(PromptOnInput ? "-" : WithSystem(req));
        return cmd;
    }

    /// <summary>`codex exec -` (and `codex exec [options] resume ID -`) reads the prompt from its input.</summary>
    public override string? Input(AiRequest req) => PromptOnInput ? WithSystem(req) : null;

    const string McpKey = "study_stash";

    // Codex says each message whole. Between tool calls it narrates ("I'll look at…"); only its last message is
    // the answer, so messages stream as they come but the final answer is the last one.
    string last = "";

    public override IEnumerable<AiEvent> Parse(string line)
    {
        if (Json(line) is not { } e) yield break;
        string type = S(e["type"]) is { Length: > 0 } t ? t : S(e["msg"]?["type"]);
        string sid = new[] { S(e["thread_id"]), S(e["session_id"]), S(e["msg"]?["session_id"]) }.FirstOrDefault(s => s.Length > 0) ?? "";
        if (sid.Length > 0) yield return new AiEvent("session", sid);
        var item = e["item"];
        string itemType = S(item?["type"]) is { Length: > 0 } it ? it : S(item?["item_type"]);
        if (type.EndsWith("completed", StringComparison.Ordinal) && itemType is "agent_message" or "assistant_message")
        {
            last = S(item?["text"]);
            yield return new AiEvent("text", last + "\n\n");
        }
        else if (type.EndsWith("started", StringComparison.Ordinal) && itemType is "command_execution" or "mcp_tool_call" or "file_change" or "web_search")
        {
            string label = S(item?["command"]) is { Length: > 0 } c ? c : S(item?["tool"]) is { Length: > 0 } tool ? tool
                : string.Join(", ", (item?["changes"] as JsonArray ?? []).Select(x => S(x?["path"]))) is { Length: > 0 } ch ? ch : itemType;
            yield return new AiEvent("tool", Name: itemType, Path: Py.Head(label, 200));
        }
        else if (type is "error" or "turn.failed")
        {
            string msg = S(e["message"]) is { Length: > 0 } m ? m : S(e["error"]?["message"]);
            if (type == "error" && msg.StartsWith("Reconnecting", StringComparison.Ordinal)) yield break; // it retries on its own
            yield return AiEvent.Error(msg.Length > 0 ? msg : "ChatGPT stopped with an error.");
        }
        else if (type == "turn.completed")
        {
            yield return new AiEvent("final", last);
            last = "";
        }
    }

    public override IAsyncEnumerable<AiEvent> RunAsync(AiRequest req, bool stream = true, CancellationToken ct = default)
    {
        // Parse keeps the last message: each run gets its own copy.
        var fresh = (CodexProvider)MemberwiseClone();
        fresh.last = "";
        return fresh.RunCoreAsync(req, stream, ct);
    }

    protected virtual IAsyncEnumerable<AiEvent> RunCoreAsync(AiRequest req, bool stream, CancellationToken ct) =>
        base.RunAsync(req, stream, ct);
}

/// <summary>Gemini, through Google's Antigravity CLI (<c>agy -p</c>), which took over from Gemini CLI for
/// personal Google accounts.</summary>
public sealed class GeminiProvider : AiProvider
{
    public override string Id => "gemini";
    public override string Name => "Gemini";
    public override string Binary => "agy";
    public override string Site => "https://antigravity.google";
    public override IReadOnlyList<(string Id, string Label)> Models =>
        [("", "Antigravity's default model"), ("gemini-3.8-flash-medium", "Gemini 3.8 Flash: fast"), ("gemini-3.1-pro-high", "Gemini 3.1 Pro: strongest")];

    /// <summary>
    /// Antigravity keeps its MCP servers in its own settings: add Study Stash's once. In print mode it can't ask
    /// before running a command, so it refuses them; allow the ones that only read (ls, cat, grep…), or a
    /// question that needs a look around comes back empty.
    /// </summary>
    public void Prepare(IReadOnlyList<string> mcp)
    {
        string exe = Exe() ?? "agy";
        // Once seen there, it stays: later turns don't wait for `agy mcp list` again before they start.
        if (mcp.Count > 0 && !mcpAdded && Machine.Run(exe, ["mcp", "list"], TimeSpan.FromSeconds(20)) is { } listed)
        {
            mcpAdded = listed.Stdout.Contains(ClaudeTools.ServerName, StringComparison.Ordinal)
                || Machine.Run(exe, ["mcp", "add", ClaudeTools.ServerName, .. mcp], TimeSpan.FromSeconds(20)) is { ExitCode: 0 };
        }
        AllowReading(Path.Combine(Py.UserHome(), ".gemini", "antigravity-cli", "settings.json"));
    }

    /// <summary>Study Stash's MCP server is in Antigravity's settings (seen by this process, or added by it).</summary>
    static bool mcpAdded;

    public static readonly string[] ReadOnlyCommands = ["ls", "cat", "head", "tail", "grep", "rg", "find", "wc", "pdftotext", "file", "stat"];

    /// <summary>Add read-only command rules to Antigravity's settings, keeping everything else in them.</summary>
    public static bool AllowReading(string settingsPath)
    {
        if (!File.Exists(settingsPath)) return false;
        JsonObject root;
        try
        {
            root = JsonNode.Parse(File.ReadAllText(settingsPath)) as JsonObject ?? [];
        }
        catch (JsonException)
        {
            return false; // not ours to repair
        }
        var perms = root["permissions"] as JsonObject ?? [];
        var allow = perms["allow"] as JsonArray ?? [];
        var have = allow.Select(S).ToHashSet();
        bool changed = false;
        foreach (string c in ReadOnlyCommands)
            if (have.Add($"command({c})")) { allow.Add($"command({c})"); changed = true; }
        if (!changed) return false;
        perms["allow"] = allow;
        root["permissions"] = perms;
        File.WriteAllText(settingsPath, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        return true;
    }

    public override List<string> Command(AiRequest req, bool stream)
    {
        var cmd = new List<string> { Exe() ?? "agy", "-p", WithSystem(req), "--output-format", "stream-json",
            "--mode", req.Write ? "accept-edits" : "plan" };
        if (!req.Write) // read-only turns may look around the other folders; editing turns stay in the library
            foreach (string d in req.ReadDirs) cmd.AddRange(["--add-dir", d]);
        if (req.Model.Length > 0) cmd.AddRange(["--model", req.Model]);
        if (req.Session.Length > 0) cmd.AddRange(["--conversation", req.Session]);
        return cmd;
    }

    public override IAsyncEnumerable<AiEvent> RunAsync(AiRequest req, bool stream = true, CancellationToken ct = default)
    {
        if (Available()) Prepare(req.Tools ? req.McpCommand : []);
        return base.RunAsync(req, stream, ct);
    }

    public override IEnumerable<AiEvent> Parse(string line)
    {
        if (Json(line) is not { } e) yield break;
        switch (S(e["event"]))
        {
            case "init":
                yield return new AiEvent("session", S(e["conversation_id"]));
                break;
            case "step_update":
                var st = e["step_update"];
                string stepType = S(st?["step_type"]);
                if (stepType == "agent_response" && S(st?["text_delta"]) is { Length: > 0 } delta)
                    yield return new AiEvent("text", delta);
                else if (S(st?["state"]) == "ACTIVE" && stepType is not ("user_input" or "agent_response" or ""))
                {
                    var p = st?["tool_info"]?["parameters"];
                    string on = new[] { "AbsolutePath", "path", "file_path", "Query", "CommandLine" }.Select(k => S(p?[k])).FirstOrDefault(v => v.Length > 0) ?? "";
                    yield return new AiEvent("tool", Name: S(st?["tool_name"]) is { Length: > 0 } n ? n : stepType, Path: on);
                }
                break;
            case "result":
                var r = e["result"];
                if (S(r?["status"]).Equals("SUCCESS", StringComparison.OrdinalIgnoreCase))
                {
                    yield return new AiEvent("session", S(r?["conversation_id"]));
                    yield return new AiEvent("final", S(r?["response"]));
                }
                else yield return AiEvent.Error(S(r?["error"]) is { Length: > 0 } why ? why : S(r?["status"]) is { Length: > 0 } s ? s : "Gemini stopped with an error.");
                break;
            case "error":
                yield return AiEvent.Error(S(e["message"]) is { Length: > 0 } m ? m : e.ToJsonString());
                break;
        }
    }
}

/// <summary>
/// A model on this computer, through Ollama: private and free. Plain answers (notes, sorting, Ask) go straight to
/// Ollama. Agent work (reading files, Canvas) runs Codex's agent on the local model when Codex is installed.
/// </summary>
public sealed class OllamaProvider(Func<string> host) : CodexProvider
{
    public OllamaProvider() : this(() => "http://localhost:11434") { }

    public override string Id => "ollama";
    public override string Name => "Local model (Ollama)";
    public override string Binary => "ollama";
    public override string Site => "https://ollama.com/download";
    public override IReadOnlyList<(string Id, string Label)> Models => [("", "The library's model")];

    public override string? Exe() => Ollama.FindExe();
    public override bool Available() => Ollama.Installed();
    /// <summary>Codex can drive the local model as an agent.</summary>
    public static bool Agentic() => Which("codex") is not null;

    protected override IEnumerable<string> ExtraArgs => ["--oss", "--local-provider", "ollama"];

    protected override async IAsyncEnumerable<AiEvent> RunCoreAsync(AiRequest req, bool stream, [EnumeratorCancellation] CancellationToken ct)
    {
        if (!Available())
        {
            yield return AiEvent.Error($"Ollama isn't installed ({Site}).");
            yield break;
        }
        if (!await Ollama.StartAsync(host()))
        {
            yield return AiEvent.Error("Ollama didn't start. Open the Ollama app and try again.");
            yield break;
        }
        string model = await ModelAsync(req.Model) ?? "";
        if (model.Length == 0)
        {
            yield return AiEvent.Error("No local models yet: pick and download one in Settings.");
            yield break;
        }
        if ((req.Tools || req.Write) && Agentic())
        {
            await foreach (var e in base.RunCoreAsync(req with { Model = model }, stream, ct)) yield return e;
            yield break;
        }
        if (req.Write)
        {
            yield return AiEvent.Error("Changing files with a local model needs the Codex CLI (brew install codex). Questions work without it.");
            yield break;
        }
        await foreach (var e in ChatAsync(req, model, ct)) yield return e;
    }

    /// <summary>The model a run uses: the one asked for when it's installed, else the best one that is; null when
    /// there's none (or Ollama isn't answering).</summary>
    async Task<string?> ModelAsync(string asked)
    {
        var have = await Ollama.ListModelsAsync(host()) ?? [];
        var names = have.Select(m => m.Name).ToList();
        if (asked.Length > 0 && Ollama.HasModel(names, asked)) return asked;
        return have.Count == 0 ? null : Ollama.PickDefaultModel(names, have[0].Name);
    }

    /// <summary>How long Ollama keeps the model loaded after a question, so a follow-up doesn't wait for it to load
    /// again (Ollama's own default is 5 minutes).</summary>
    public const string KeepLoaded = "15m";

    /// <summary>Loads the model a question would use into memory now (a big one takes seconds to load), when Ollama
    /// is already running: an empty generate request is Ollama's way to load a model without asking it anything.</summary>
    public override async Task<bool> WarmAsync(string model, CancellationToken ct = default)
    {
        if (await ModelAsync(model) is not { } picked) return false;
        try
        {
            await Ollama.PostAsync(host(), "/api/generate", new JsonObject { ["model"] = picked, ["keep_alive"] = KeepLoaded },
                TimeSpan.FromMinutes(2), ct: ct);
            return true;
        }
        catch (Exception e) when (e is HttpRequestException or TimeoutException or OperationCanceledException or JsonException)
        {
            return false;
        }
    }

    /// <summary>Straight to Ollama, streaming: the system brief, then the prompt.</summary>
    async IAsyncEnumerable<AiEvent> ChatAsync(AiRequest req, string model, [EnumeratorCancellation] CancellationToken ct)
    {
        var messages = new JsonArray();
        if (req.System.Length > 0) messages.Add(new JsonObject { ["role"] = "system", ["content"] = req.System });
        messages.Add(new JsonObject { ["role"] = "user", ["content"] = req.Prompt });
        var body = new JsonObject
        {
            ["model"] = model, ["messages"] = messages, ["stream"] = true, ["think"] = false, ["keep_alive"] = KeepLoaded,
        };
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(req.Timeout);
        using var msg = new HttpRequestMessage(HttpMethod.Post, host().TrimEnd('/') + "/api/chat")
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
        };
        HttpResponseMessage? r = null;
        string? fail = null;
        try
        {
            r = await Ollama.Http.SendAsync(msg, HttpCompletionOption.ResponseHeadersRead, cts.Token);
            if (!r.IsSuccessStatusCode) fail = $"Ollama answered {(int)r.StatusCode}: {Py.Head(await r.Content.ReadAsStringAsync(cts.Token), 300)}";
        }
        catch (Exception e) when (e is HttpRequestException or OperationCanceledException)
        {
            fail = $"Ollama: {e.Message}";
        }
        if (fail is not null || r is null)
        {
            r?.Dispose();
            yield return AiEvent.Error(fail ?? "Ollama didn't answer.");
            yield break;
        }
        using (r)
        {
            using var reader = new StreamReader(await r.Content.ReadAsStreamAsync(cts.Token));
            while (await reader.ReadLineAsync(cts.Token) is { } line)
            {
                if (Json(line) is not { } d) continue;
                if (S(d["message"]?["content"]) is { Length: > 0 } piece) yield return new AiEvent("text", piece);
                if (d["done"] is JsonValue v && v.GetValueKind() == JsonValueKind.True) yield return new AiEvent("final", "");
            }
        }
    }
}

/// <summary>Every AI Study Stash knows, by id.</summary>
public static class AiProviders
{
    public static IReadOnlyList<AiProvider> All(Func<string>? ollamaHost = null) =>
        [new ClaudeProvider(), new CodexProvider(), new GeminiProvider(), ollamaHost is null ? new OllamaProvider() : new OllamaProvider(ollamaHost)];

    public static AiProvider Get(string id, Func<string>? ollamaHost = null) =>
        All(ollamaHost).FirstOrDefault(p => p.Id == id) ?? All(ollamaHost)[^1];
}
