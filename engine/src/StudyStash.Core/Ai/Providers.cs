using System.Collections.Concurrent;
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
    /// <summary>How hard the model thinks ("high"), where its command takes a reasoning effort; "" for its own default.</summary>
    public string Effort { get; init; } = "";
    /// <summary>Claude Code's fast mode: the same Opus with faster output, billed at a higher rate
    /// (<see cref="ClaudeProvider.FastSettings"/>). Only Claude Code has one; every other provider ignores it.</summary>
    public bool Fast { get; init; }
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
public sealed record AiResult(bool Ok, string Text, string Session, IReadOnlyList<AiEvent> Tools)
{
    /// <summary>Which model really answered, when it wasn't the one asked ("Gemini 3.5 Flash-Lite (Flash's free daily
    /// limit is used up)"), in words for the answer's byline; "" otherwise.</summary>
    public string Note { get; init; } = "";
}

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

    /// <summary>An API key the student gave this engine (<see cref="ApiKeys"/>): with one, work that only answers
    /// (no tools, no files to change) goes to the engine's API instead of its command, and everything does when the
    /// command isn't installed.</summary>
    public string? ApiKey { get; set; }

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
        if (ApiKey is { } key && (!Available() || !req.Tools && !req.Write))
        {
            await foreach (var e in ApiKeys.RunAsync(Id, key, req, ct)) yield return e;
            yield break;
        }
        if (!Available())
        {
            yield return AiEvent.Error($"{Name} isn't installed on this computer ({Site}).");
            yield break;
        }
        await foreach (var e in Spawn(Command(req, stream), req.Cwd, req.Timeout, Parse, Input(req), ct, Env(req))) yield return e;
    }

    /// <summary>What the command's environment has besides the app's own (Claude's connectors kept out of a plain
    /// answer, say).</summary>
    protected virtual IReadOnlyDictionary<string, string> Env(AiRequest req) => new Dictionary<string, string>();

    /// <summary>Run to the end: the answer (the provider's final word, else everything it wrote), or why not. With
    /// <paramref name="soFar"/> it streams: that hears everything written so far each time more arrives (or the
    /// final word, from a provider that only says it at the end).</summary>
    public async Task<AiResult> CompleteAsync(AiRequest req, CancellationToken ct = default, Action<string>? soFar = null)
    {
        var text = new StringBuilder();
        string final = "", session = "", error = "", note = "";
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
                case "note": note = e.Text; break;
                case "error": error = e.Text; break;
            }
        }
        string answer = Py.Strip(final.Length > 0 ? final : text.ToString());
        if (error.Length > 0) return new AiResult(false, error, session, tools);
        // Finished but said nothing (Antigravity does when print mode refuses a tool it wanted): that's no answer, so
        // nothing is saved as a lecture's notes and the job can try again or fall back.
        if (answer.Length == 0) return new AiResult(false, $"{Name} finished without answering.", session, tools);
        return new AiResult(true, answer, session, tools) { Note = note };
    }

    // --- running a command ------------------------------------------------------------------------------------

    /// <summary>Where these commands usually live: a background service starts with a short PATH. On Windows also
    /// the PATH as Windows has it now: an engine (or Node.js, which npm's claude.cmd and codex.cmd run on) installed
    /// while Study Stash was running isn't on the PATH it started with, and "Check again" in setup should find it.
    /// Codex's own Windows installer puts it in %LOCALAPPDATA%\Programs\OpenAI\Codex\bin, Antigravity's in
    /// %LOCALAPPDATA%\agy\bin. Last of all, the copies the
    /// ChatGPT and Claude desktop apps carry (<see cref="DesktopAppDirs"/>): one installed on its own comes first.</summary>
    public static string SearchPath()
    {
        string home = Py.UserHome();
        string[] extra = OperatingSystem.IsWindows()
            ? [.. WindowsPathNow(), Path.Combine(home, "AppData", "Roaming", "npm"), Path.Combine(home, ".local", "bin"),
               Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "OpenAI", "Codex", "bin"),
               Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "agy", "bin"),
               Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "nodejs")]
            : ["/opt/homebrew/bin", "/usr/local/bin", "/usr/bin", "/bin", Path.Combine(home, ".local", "bin"),
               Path.Combine(home, ".npm-global", "bin"), Path.Combine(home, ".bun", "bin"), Path.Combine(home, ".claude", "local"),
               .. OperatingSystem.IsMacOS() ? DesktopAppDirs(home) : []];
        var dirs = new[] { Environment.GetEnvironmentVariable("PATH") ?? "" }.Concat(extra)
            .SelectMany(p => p.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Distinct(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        return string.Join(Path.PathSeparator, dirs);
    }

    /// <summary>
    /// A Mac: where the ChatGPT app keeps its Codex and Claude Desktop keeps its Claude Code, so a student who already
    /// has the app needs no second install, and usually no second sign-in (ChatGPT's Codex is signed in with the app).
    /// The ChatGPT app carries Codex inside its bundle; Claude Desktop downloads Claude Code beside its own settings,
    /// a folder per version, once its Code tab has been opened: the newest one counts. Only folders that are there.
    /// </summary>
    public static List<string> DesktopAppDirs(string home, string applications = "/Applications")
    {
        var dirs = new List<string>();
        try
        {
            foreach (string apps in new[] { applications, Path.Combine(home, "Applications") })
                foreach (string app in new[] { "ChatGPT.app", "Codex.app" })
                {
                    string dir = Path.Combine(apps, app, "Contents", "Resources", "codex-cli", "CodexCLI.app", "Contents", "MacOS");
                    if (File.Exists(Path.Combine(dir, "codex")) && !dirs.Contains(dir)) dirs.Add(dir);
                }
            string kept = Path.Combine(home, "Library", "Application Support", "Claude", "claude-code");
            if (Directory.Exists(kept))
            {
                var newest = Directory.EnumerateDirectories(kept)
                    .Select(d => (Dir: d, Version: Version.TryParse(Path.GetFileName(d), out var v) ? v : null))
                    .Where(d => d.Version is not null).OrderByDescending(d => d.Version)
                    .SelectMany(d => Directory.EnumerateDirectories(d.Dir).OrderByDescending(Directory.GetLastWriteTimeUtc))
                    .Select(d => Path.Combine(d, "claude.app", "Contents", "MacOS"))
                    .FirstOrDefault(d => File.Exists(Path.Combine(d, "claude")));
                if (newest is not null) dirs.Add(newest);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
        return dirs;
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
        Func<string, IEnumerable<AiEvent>> parse, string? input = null, [EnumeratorCancellation] CancellationToken ct = default,
        IReadOnlyDictionary<string, string>? env = null)
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
        foreach (var (k, v) in env ?? new Dictionary<string, string>()) psi.Environment[k] = v;
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
                yield return AiEvent.Error($"{Path.GetFileName(cmd[0])} didn't finish within " +
                    (timeout < TimeSpan.FromMinutes(2) ? $"{timeout.TotalSeconds:0} seconds." : $"{timeout.TotalMinutes:0} minutes."));
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

    /// <summary>What turns fast mode on for one `claude -p` run: its own settings, for that run only (nothing is saved to
    /// the person's settings). Needs Claude Code 2.1.205 or later and an Opus model; an older Claude Code ignores it, and
    /// a Claude account that can't use fast mode runs at normal speed.</summary>
    public const string FastSettings = """{"fastMode":true}""";

    /// <summary>A folder as Claude Code's rules spell it: "/Users/x" stays; Windows' "C:\Users\x" is "/c/Users/x".</summary>
    internal static string ClaudePath(string full) =>
        full.Length > 1 && full[1] == ':' ? "/" + char.ToLowerInvariant(full[0]) + full[2..].Replace('\\', '/').TrimEnd('/')
            : "/" + full.TrimStart('/');

    static readonly ConcurrentDictionary<string, Version?> Versions = new();

    /// <summary>The installed Claude Code's version (its <c>--version</c>, asked once per program): a newer lockdown
    /// flag is added only when it has it. The tests give their own.</summary>
    public static Func<string, Version?> VersionOf { get; set; } = exe =>
        Versions.GetOrAdd(exe, e => Machine.Run(e, ["--version"], TimeSpan.FromSeconds(20)) is { ExitCode: 0 } r ? AgentCli.ParseVersion(r.Stdout) : null);

    /// <summary>How long a run waits before it's tried again when Claude Code stopped because another of its processes
    /// was renewing the sign-in (<see cref="Engines.LooksLikeRefreshBusy"/>), up to <see cref="RefreshTries"/> tries in
    /// all, about the minute Anthropic's docs say it takes. Study Stash starts several at once (notes, diagrams, Ask,
    /// beside the student's own), and one it stopped halfway can leave the renewal locked for a while: without this the
    /// lecture said "Claude needs you to sign in again" when it didn't. The tests make it short.</summary>
    public TimeSpan RefreshWait { get; init; } = TimeSpan.FromSeconds(30);
    public const int RefreshTries = 3;

    /// <summary>How long a run Claude turned away as signed out waits before it's tried once more, as a fresh
    /// <c>claude</c> that signs in alone (<see cref="SignInGate"/>). Another Claude Code may just have renewed the
    /// sign-in and left this one holding the old one; only when the fresh run fails too is Claude signed out (the
    /// lecture says so, with Sign in). Signed out for real, the fresh run stops at once on this computer, with
    /// nothing sent. The tests make it short.</summary>
    public TimeSpan SignedOutWait { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>The gate this provider's runs go through; null for this process's own for the program
    /// (<see cref="SignInGate.For"/>), which every Claude run of the library's shares. The tests give their own.</summary>
    public SignInGate? Gate { get; init; }

    /// <summary>
    /// The run, through the <see cref="SignInGate"/>: after a quiet spell, or after Claude turned one away, it waits
    /// while another run signs in, or signs in alone itself (streaming, so the others hear the moment Claude begins to
    /// answer; a caller that didn't ask for the stream doesn't get its pieces). A run that stops before saying
    /// anything is tried again: after <see cref="RefreshWait"/> while another Claude Code is renewing the sign-in (up
    /// to <see cref="RefreshTries"/> tries), and once, after <see cref="SignedOutWait"/>, when it was turned away as
    /// signed out. After that its words stand. An API key's run doesn't use the sign-in and goes straight through.
    /// </summary>
    public override async IAsyncEnumerable<AiEvent> RunAsync(AiRequest req, bool stream = true,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        bool onKey = ApiKey is not null && (!Available() || !req.Tools && !req.Write);
        if (onKey || Exe() is not { } exe)
        {
            await foreach (var e in base.RunAsync(req, stream, ct)) yield return e;
            yield break;
        }
        var gate = Gate ?? SignInGate.For(exe);
        int busy = 0;
        bool againAlone = false;
        while (true)
        {
            SignInGate.Turn turn;
            try
            {
                turn = await gate.EnterAsync(ct);
            }
            catch (OperationCanceledException)
            {
                yield break;
            }
            TimeSpan? again = null;
            using (turn)
            {
                bool said = false;
                await foreach (var e in base.RunAsync(req, stream || turn.Leads, ct))
                {
                    // Claude has begun to answer: past sign-in, and never run twice from here (it may have used tools).
                    if (e.Kind is "started" or "text" or "tool" or "final")
                    {
                        turn.Passed();
                        said = true;
                    }
                    if (e.Kind == "started" || e.Kind == "text" && !stream) continue;
                    if (e.Kind == "error" && !said)
                    {
                        bool renewing = Engines.LooksLikeRefreshBusy(e.Text), signedOut = !renewing && Engines.LooksSignedOut(e.Text);
                        if (renewing || signedOut) gate.Cold(); // the next run, this one's again included, leads
                        if (renewing && ++busy < RefreshTries) again = RefreshWait;
                        else if (signedOut && !againAlone)
                        {
                            againAlone = true;
                            again = SignedOutWait;
                        }
                        if (again is not null) break;
                    }
                    yield return e;
                }
            }
            if (again is not { } wait) yield break;
            bool stopped = false;
            try
            {
                await Task.Delay(wait, ct);
            }
            catch (OperationCanceledException)
            {
                stopped = true;
            }
            if (stopped) yield break;
        }
    }

    /// <summary>A plain answer (notes, Check, Ask) is the student's own Claude Code with nothing of theirs added: no MCP
    /// servers of theirs, none of claude.ai's connectors, and, where the version has it, only managed settings
    /// (<c>--restricted</c>), so their own hooks and settings don't run inside the library's work. Agent work
    /// (<see cref="AiRequest.Tools"/>) is left as it was.</summary>
    protected override IReadOnlyDictionary<string, string> Env(AiRequest req) =>
        req.Tools ? new Dictionary<string, string>() : new Dictionary<string, string> { ["ENABLE_CLAUDEAI_MCP_SERVERS"] = "false" };

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
        // A plain answer uses no tools: don't wait for the person's own MCP servers to start first, and run none of
        // their own hooks or settings in it (Claude Code 2.1.248 on).
        if (!req.Tools) cmd.Add("--strict-mcp-config");
        if (!req.Tools && AgentCli.Has(VersionOf(exe), AgentCli.RestrictedSince)) cmd.Add("--restricted");
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
        if (req.Effort.Length > 0) cmd.AddRange(["--effort", req.Effort]);
        if (req.Fast) cmd.AddRange(["--settings", FastSettings]);
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
        // The model's answer has begun, so Claude Code got past its sign-in (for the SignInGate; not passed on).
        else if (type == "stream_event" && S(e["event"]?["type"]) == "message_start") yield return new AiEvent("started");
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

    /// <summary>The student's own Codex config.toml (where its MCP servers are named).</summary>
    public Func<string> ConfigPath { get; init; } = () => SetupChat.CodexConfig();

    /// <summary>Codex programs whose plain answers read the student's own config.toml: their sign-in turned out to be
    /// kept there (turned away as signed out without it, and with it worked), or they're older than
    /// <c>--ignore-user-config</c> (turned away for the flag itself). Remembered for as long as the library runs.</summary>
    static readonly ConcurrentDictionary<string, bool> KeptConfig = new();

    string CodexExe() => (Id == "codex" ? Exe() : null) ?? Which("codex") ?? "codex";

    /// <summary>A plain answer (notes, Check, Ask: no tools, no files to change) on ChatGPT's own Codex.</summary>
    bool Plain(AiRequest req) => Id == "codex" && !req.Tools && !req.Write;

    /// <summary>
    /// Whether a plain answer reads the student's config.toml (with their MCP servers, hooks, notify and apps off): when
    /// it has to (above), or when they point Codex at a model provider of their own there (its definition is in that
    /// file, and without it the notes would go to another provider).
    /// </summary>
    bool ReadsConfig(AiRequest req) =>
        !Plain(req) || KeptConfig.ContainsKey(CodexExe()) || SetupChat.CodexTopLevel(ConfigPath()).Provider is { Length: > 0 } p && p != "openai";

    public override List<string> Command(AiRequest req, bool stream) => Command(req, stream, readConfig: ReadsConfig(req));

    /// <summary>The command-line flag an older Codex turns away ("error: unexpected argument '--ignore-user-config'").</summary>
    static bool RefusedTheFlag(string error) => error.Contains("--ignore-user-config", StringComparison.Ordinal);

    /// <summary>
    /// <paramref name="readConfig"/> off (a plain answer): the student's own config.toml is skipped, so nothing of
    /// theirs (MCP servers, which can reach their library; hooks; notify) runs inside the library's work. On, for a
    /// plain answer whose sign-in is kept there: it's read, but every MCP server it names stays off, and so do hooks
    /// and notify. Agent work reads it as before.
    /// </summary>
    List<string> Command(AiRequest req, bool stream, bool readConfig)
    {
        // The next turn of a conversation is `codex exec [options] resume ID prompt`: -C and -s are exec's own options
        // and `resume` turns them away, so every option comes before it.
        var cmd = new List<string> { CodexExe(), "exec" };
        cmd.AddRange(ExtraArgs);
        cmd.AddRange(["--json", "--skip-git-repo-check"]);
        if (!readConfig) cmd.Add("--ignore-user-config");
        cmd.AddRange(["-C", req.Cwd, "-s", req.Write ? "workspace-write" : "read-only", "-c", "approval_policy=\"never\""]);
        // Nor the student's ChatGPT apps (connectors: their mail, drive…), as Claude's plain answers leave out claude.ai's.
        if (Plain(req)) cmd.AddRange(["-c", "features.apps=false"]);
        if (readConfig && Plain(req))
        {
            foreach (string own in SetupChat.CodexServers(ConfigPath())) cmd.AddRange(["-c", $"mcp_servers.{own}.enabled=false"]);
            cmd.AddRange(["-c", "features.hooks=false", "-c", "notify=[]"]);
        }
        foreach (string d in req.ReadDirs) cmd.AddRange(["--add-dir", d]);
        if (req.Tools && req.McpCommand.Count > 0)
            cmd.AddRange(["-c", $"mcp_servers.{McpKey}.command={JsonSerializer.Serialize(req.McpCommand[0])}",
                "-c", $"mcp_servers.{McpKey}.args={new JsonArray(req.McpCommand.Skip(1).Select(a => (JsonNode)a).ToArray()).ToJsonString()}"]);
        // The model: the one picked in Settings; else, with the student's config.toml skipped, the one they set there
        // (otherwise the notes would quietly move to Codex's own default).
        string model = req.Model.Length > 0 ? req.Model : !readConfig && Plain(req) ? SetupChat.CodexTopLevel(ConfigPath()).Model : "";
        if (model.Length > 0) cmd.AddRange(["-m", model]);
        if (req.Effort.Length > 0 && Id == "codex") cmd.AddRange(["-c", $"model_reasoning_effort=\"{req.Effort}\""]);
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

    /// <summary>A plain answer turned away without the student's config.toml, as signed out (their sign-in may be kept
    /// there) or for the flag that skips it (a Codex older than it), is asked once more reading it, its MCP servers
    /// off; that's remembered (an older Codex at once; a sign-in when it then works).</summary>
    protected virtual async IAsyncEnumerable<AiEvent> RunCoreAsync(AiRequest req, bool stream, [EnumeratorCancellation] CancellationToken ct)
    {
        string exe = CodexExe();
        bool retry = Plain(req) && ApiKey is null && !ReadsConfig(req) && Available();
        bool signedOut = false;
        await foreach (var e in base.RunAsync(req, stream, ct))
        {
            if (retry && e.Kind == "error" && RefusedTheFlag(e.Text))
            {
                KeptConfig[exe] = true;
                signedOut = true;
                break;
            }
            if (retry && e.Kind == "error" && Engines.LooksSignedOut(e.Text))
            {
                signedOut = true;
                break;
            }
            yield return e;
        }
        if (!signedOut || ct.IsCancellationRequested) yield break;
        last = "";
        bool worked = true;
        await foreach (var e in Spawn(Command(req, stream, readConfig: true), req.Cwd, req.Timeout, Parse, Input(req), ct, Env(req)))
        {
            if (e.Kind == "error") worked = !Engines.LooksSignedOut(e.Text);
            yield return e;
        }
        if (worked) KeptConfig[exe] = true;
    }
}

/// <summary>Gemini, through Google's Antigravity CLI (<c>agy -p</c>), which took over from Gemini CLI for
/// personal Google accounts, in a home of Study Stash's own (<see cref="Antigravity.Home"/>): the student's own agy
/// settings, MCP servers, plugins and skills don't run in the library's work, and Study Stash never changes theirs. The
/// sign-in is kept in that home too: the one setup's "Sign in to Gemini" made.</summary>
public sealed class GeminiProvider : AiProvider
{
    public override string Id => "gemini";
    public override string Name => "Gemini";
    public override string Binary => "agy";
    public override string Site => "https://antigravity.google";
    public override IReadOnlyList<(string Id, string Label)> Models =>
        [("", "Antigravity's default model"), ("gemini-3.8-flash-medium", "Gemini 3.8 Flash: fast"), ("gemini-3.1-pro-high", "Gemini 3.1 Pro: strongest")];

    /// <summary>
    /// Agent work runs as Study Stash's library agent (<see cref="Antigravity.LibraryAgent"/>), which declares the
    /// library's MCP server itself (<paramref name="mcp"/>), so it never sits in the home's global config, where it would
    /// start in the setup chat's sessions too. In print mode agy can't ask before running a command or opening a file, so
    /// it refuses them and ends with an empty answer; allow the commands that only read (ls, cat, grep…), reading the
    /// turn's own folders (<paramref name="folders"/>) and the library's tools, or a turn that wants a look around comes
    /// back empty.
    /// </summary>
    public void Prepare(IReadOnlyList<string> mcp, IEnumerable<string>? folders = null)
    {
        Antigravity.PrepareLibrary(mcp);
        AllowReading(Antigravity.SettingsPath(Antigravity.Home()), folders, create: true);
    }

    /// <summary>agy's home of Study Stash's own.</summary>
    protected override IReadOnlyDictionary<string, string> Env(AiRequest req) => Antigravity.HomeEnv();

    public static readonly string[] ReadOnlyCommands = ["ls", "cat", "head", "tail", "grep", "rg", "find", "wc", "pdftotext", "file", "stat"];

    /// <summary>Add read-only command rules, and reading each of <paramref name="folders"/> (Antigravity's
    /// <c>read_file(/a/folder)</c>), to Antigravity's settings, keeping everything else in them (made, with
    /// <paramref name="create"/>, when there are none yet: Study Stash's own home). True when it changed them.</summary>
    public static bool AllowReading(string settingsPath, IEnumerable<string>? folders = null, bool create = false)
    {
        if (!File.Exists(settingsPath) && !create) return false;
        var rules = ReadOnlyCommands.Select(c => $"command({c})")
            .Concat((folders ?? []).Where(f => f.Length > 0).Select(f => $"read_file({Path.TrimEndingDirectorySeparator(Path.GetFullPath(f))})"));
        return Antigravity.Allow(settingsPath, rules);
    }

    /// <summary>A plain answer (notes, sorting, the Check: no tools, no files to change, no folders to look at): it runs
    /// as Study Stash's answer agent, which has no tools to reach for (<see cref="Antigravity.AnswerAgentFile"/>).</summary>
    static bool PlainAnswer(AiRequest req) => !req.Tools && !req.Write && req.ReadDirs.Count == 0;

    public override List<string> Command(AiRequest req, bool stream)
    {
        if (PlainAnswer(req))
        {
            var plain = new List<string> { Exe() ?? "agy", "-p", WithSystem(req), "--output-format", "stream-json", "--agent", Antigravity.AnswerAgent, "--disable-slash-commands" };
            if (req.Model.Length > 0) plain.AddRange(["--model", req.Model]);
            if (req.Session.Length > 0) plain.AddRange(["--conversation", req.Session]);
            return plain;
        }
        var cmd = new List<string> { Exe() ?? "agy", "-p", WithSystem(req), "--output-format", "stream-json", "--agent", Antigravity.LibraryAgent,
            "--mode", req.Write ? "accept-edits" : "plan" };
        if (!req.Write) // read-only turns may look around the other folders; editing turns stay in the library
            foreach (string d in req.ReadDirs) cmd.AddRange(["--add-dir", d]);
        if (req.Model.Length > 0) cmd.AddRange(["--model", req.Model]);
        if (req.Session.Length > 0) cmd.AddRange(["--conversation", req.Session]);
        return cmd;
    }

    public override IAsyncEnumerable<AiEvent> RunAsync(AiRequest req, bool stream = true, CancellationToken ct = default)
    {
        if (Available())
        {
            if (PlainAnswer(req)) Antigravity.PrepareAnswer();
            else Prepare(req.Tools ? req.McpCommand : [], [req.Cwd, .. req.ReadDirs]);
        }
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

    /// <summary>The model a run uses: the one asked for when it's installed, else the best one that is on this
    /// computer (never an Ollama cloud model, which would send the lecture off it); null when there's none (or Ollama
    /// isn't answering).</summary>
    async Task<string?> ModelAsync(string asked)
    {
        var have = await Ollama.ListModelsAsync(host()) ?? [];
        var names = have.Select(m => m.Name).ToList();
        if (asked.Length > 0 && Ollama.HasModel(names, asked)) return asked;
        return Ollama.LocalPick(have);
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
