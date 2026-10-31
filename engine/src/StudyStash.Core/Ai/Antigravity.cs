using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace StudyStash.Core.Ai;

/// <summary>
/// Google's Antigravity CLI (<c>agy</c>), which Gemini runs through: Google moved personal Google accounts (free, AI Pro,
/// Ultra) from Gemini CLI to it on 18 June 2026, and Gemini CLI now serves only API keys and company licences.
/// <para>
/// Syft Notes runs it with a home of its own (<see cref="Home"/>: agy keeps everything under <c>~/.gemini</c>, and reads
/// its home from HOME, or USERPROFILE on Windows), so nothing of the student's own agy (their MCP servers, plugins,
/// skills, hooks, rules, settings) ever runs inside Syft Notes' work, and Syft Notes never writes theirs. The sign-in
/// is kept in that home too (agy 1.4.5 keeps it there, <c>antigravity-cli/antigravity-oauth-token</c>, which Syft Notes
/// never reads): a student signs in to Gemini for Syft Notes once, in setup, and its notes and its setup chat use that.
/// </para>
/// <para>
/// The setup chat runs in that home as an agent of its own (<see cref="SetupAgent"/>): Antigravity's default prompt and
/// built-in tools left out (tried with the real agy 1.4.5: what's left is only <c>call_mcp_tool</c>, <c>list_resources</c>,
/// <c>read_resource</c> and <c>manage_task</c>, so no command, file, web page or browser), none of the home's skills,
/// rules, plugins, subagents or MCP servers taken in, commands off, and the setup tools as its only MCP server, declared
/// in the agent itself. The turn's guard holds it to that as it runs (<see cref="SetupChatParser"/>).
/// </para>
/// </summary>
public static class Antigravity
{
    public const string Binary = "agy";
    /// <summary>The setup guide's agent, in Syft Notes' home for agy.</summary>
    public const string SetupAgent = "study-stash-setup";
    /// <summary>The one tool agy calls any MCP server's tools through, naming the server and the tool.</summary>
    public const string McpTool = "call_mcp_tool";
    /// <summary>The setup agent's other tools, as agy gives them (tried): listing or stopping its own background tasks
    /// (it can start none). Its MCP resource tools are held to the setup tools' server like its MCP calls.</summary>
    public static readonly string[] Harmless = ["manage_task"];
    public static readonly string[] ResourceTools = ["list_resources", "read_resource"];

    /// <summary>Syft Notes' own home for agy, where its sign-in, its notes and its setup chat are: the library's home's
    /// <c>gemini</c> folder (<see cref="For"/>). The app and the library set it from their own home; a test gives its own
    /// with <see cref="Use"/>, which no other test running beside it sees.</summary>
    public static Func<string> Home
    {
        get => () => scoped.Value ?? home();
        set => home = value;
    }

    static Func<string> home = () => For(Configs.DefaultHome);
    static readonly AsyncLocal<string?> scoped = new();

    /// <summary>
    /// <paramref name="agyHome"/> as the home for the calls that follow in this flow of work only (a test's own), until
    /// disposed. <see cref="Home"/> is shared by the whole process, and a library run in a test (its command line sets
    /// it) changed it under tests beside it: one wrote into a folder another had just deleted.
    /// </summary>
    public static IDisposable Use(string agyHome)
    {
        string? before = scoped.Value;
        scoped.Value = agyHome;
        return new Restore(() => scoped.Value = before);
    }

    sealed class Restore(Action undo) : IDisposable
    {
        public void Dispose() => undo();
    }

    /// <summary>The agy home for a Syft Notes home: its <c>gemini</c> folder, or STUDYSTASH_AGY_HOME when that's set
    /// (an app and its library pointed at one signed-in home, as the off-screen films are).</summary>
    public static string For(string studyStashHome) =>
        Environment.GetEnvironmentVariable("STUDYSTASH_AGY_HOME") is { Length: > 0 } set ? set : Path.Combine(studyStashHome, "gemini");

    /// <summary>The empty folder the setup chat's agy works in: nothing in it to read, no instructions to pick up.</summary>
    public static string SetupWorkspace(string workDir) => Path.Combine(workDir, "gemini-workspace");

    /// <summary>The environment that gives agy <paramref name="home"/> as its home.</summary>
    public static Dictionary<string, string> HomeEnv(string home, bool windows) =>
        windows ? new() { ["HOME"] = home, ["USERPROFILE"] = home } : new() { ["HOME"] = home };

    public static Dictionary<string, string> HomeEnv() => HomeEnv(Home(), OperatingSystem.IsWindows());

    public static string SettingsPath(string home) => Path.Combine(home, ".gemini", "antigravity-cli", "settings.json");
    public static string McpConfigPath(string home) => Path.Combine(home, ".gemini", "config", "mcp_config.json");
    public static string AgentPath(string home, string name) => Path.Combine(home, ".gemini", "config", "agents", name, "agent.md");

    /// <summary>
    /// The sign-in in a terminal window (when the hidden one can't open the browser), in Syft Notes' own home for
    /// agy as well: <c>/usr/bin/env HOME=… agy -p=/usage</c> on a Mac, PowerShell setting the home first on Windows.
    /// It opens the browser, waits a minute (a code can be pasted there too), then says how much is left.
    /// </summary>
    public static (string Exe, IReadOnlyList<string> Args) InTerminal(string agy, bool windows)
    {
        string home = Home();
        if (!windows) return ("/usr/bin/env", [$"HOME={home}", agy, "-p=/usage"]);
        static string Quoted(string s) => "'" + s.Replace("'", "''", StringComparison.Ordinal) + "'";
        return ("powershell.exe", ["-NoProfile", "-NoExit", "-Command",
            $"$env:USERPROFILE = {Quoted(home)}; $env:HOME = {Quoted(home)}; & {Quoted(agy)} '-p=/usage'"]);
    }

    /// <summary>
    /// The sign-in as a line to type, for where only words can be given (the library's own computer, reached from
    /// another): <c>HOME="…/gemini" agy -p=/usage</c> in a Mac's Terminal, PowerShell setting the home first on Windows.
    /// Never bare <c>agy</c>, which signs in to the student's own home, not the one the library's notes use.
    /// </summary>
    public static string SignInLine(bool windows)
    {
        string home = Home();
        if (windows)
        {
            string q = "'" + home.Replace("'", "''", StringComparison.Ordinal) + "'";
            return $"$env:USERPROFILE = {q}; $env:HOME = {q}; agy '-p=/usage'";
        }
        return $"HOME=\"{home.Replace("\"", "\\\"", StringComparison.Ordinal)}\" agy -p=/usage";
    }

    // --- the setup agent -----------------------------------------------------------------------------------------------

    /// <summary>
    /// The setup guide as an agy agent: picked with <c>--agent</c>, never one another agent can call, with
    /// Antigravity's default prompt sections and built-in tools left out (<c>excludeDefaultComponents</c>, and no tools
    /// listed), none of the home's skills, rules, plugins, subagents or MCP servers taken in, commands off, and the setup
    /// tools as its one MCP server (none for a turn with no tools). agy doesn't fill in <c>${VAR}</c> in a header
    /// (tried: it sends the words as they are), so the token is written in: in a file only this account can read, in
    /// Syft Notes' own folder, for the turn alone (<see cref="ForgetSetup"/>). Its body (after the H1) is the brief.
    /// </summary>
    public static string SetupAgentFile(string brief, string mcpUrl = "", string token = "")
    {
        string servers = mcpUrl.Length == 0 ? "mcpServers: []" : $"""
            mcpServers:
              - name: {SetupChat.Server}
                url: "{mcpUrl}"
                headers:
                  Authorization: "Bearer {token}"
            """;
        return $"""
            ---
            name: {SetupAgent}
            description: Syft Notes' setup guide. It only uses the study_stash_setup tools.
            mainAgent: true
            subagent: false
            hidden: true
            excludeDefaultComponents: true
            inheritCustomizations: false
            inheritMcp: false
            commandExecutionPolicy: "off"
            tools: []
            {servers}
            ---
            # Syft Notes setup guide

            {brief.Replace("\r\n", "\n").Trim()}

            """.Replace("\r\n", "\n");
    }

    /// <summary>The agent the library's plain answers (notes, sorting, its Check) run as: <see cref="AnswerAgentFile"/>.</summary>
    public const string AnswerAgent = "study-stash-answer";

    /// <summary>
    /// The library's plain answers as an agy agent: everything it needs is in the message (the transcript, the brief),
    /// so it has no tools at all, like the setup agent with no MCP server. agy's own agent sometimes reaches for a tool
    /// even for "Reply with the single word: ready", and a print run refuses it and ends with no answer (seen with the
    /// real agy: the library's Check of Gemini came back empty).
    /// </summary>
    public static string AnswerAgentFile() => """
        ---
        name: study-stash-answer
        description: Syft Notes' notes and answers, from what the message gives it. It has no tools.
        mainAgent: true
        subagent: false
        hidden: true
        excludeDefaultComponents: true
        inheritCustomizations: false
        inheritMcp: false
        commandExecutionPolicy: "off"
        tools: []
        mcpServers: []
        ---
        # Syft Notes

        Answer from what the message gives you. You have no tools: don't try to use any.

        """.Replace("\r\n", "\n");

    /// <summary>The agent the library's agent work runs as (the course scout, a chat that may change notes, "Open in
    /// Antigravity"): agy's own tools and prompt, and the library's own MCP server declared in it, never in the home's
    /// global MCP config, whose servers start in every session there, the setup chat's included.</summary>
    public const string LibraryAgent = "study-stash-library";

    /// <summary>The library agent's file: <paramref name="mcp"/> is the command that starts Syft Notes' MCP server
    /// (none: no server).</summary>
    public static string LibraryAgentFile(IReadOnlyList<string> mcp)
    {
        static string Y(string v) => JsonSerializer.Serialize(v);
        string servers = mcp.Count == 0 ? "mcpServers: []" : $"""
            mcpServers:
              - name: {ClaudeTools.ServerName}
                command: {Y(mcp[0])}
                args: [{string.Join(", ", mcp.Skip(1).Select(Y))}]
            """;
        return $"""
            ---
            name: {LibraryAgent}
            description: Works with the student's Syft Notes library, its lectures and its Canvas material.
            mainAgent: true
            subagent: false
            hidden: true
            {servers}
            ---
            # Syft Notes

            You were opened from Syft Notes, the student's lecture library. The {ClaudeTools.ServerName} MCP tools (search_notes, get_lecture, due_assignments, canvas_*) read the library and Canvas.

            """.Replace("\r\n", "\n");
    }

    /// <summary>The library agent written (when it isn't as it should be), its MCP tools allowed to run without asking,
    /// and no library server left in the home's global MCP config.</summary>
    public static void PrepareLibrary(IReadOnlyList<string> mcp)
    {
        string path = AgentPath(Home(), LibraryAgent);
        string file = LibraryAgentFile(mcp);
        if (!File.Exists(path) || File.ReadAllText(path) != file) Private(path, file);
        Allow(SettingsPath(Home()), [$"mcp({ClaudeTools.ServerName}/*)"]);
        ForgetGlobal(ClaudeTools.ServerName);
    }

    /// <summary>Takes <paramref name="server"/> out of the home's global MCP config (Syft Notes' own home: an earlier
    /// build put the library's server there with <c>agy mcp add</c>), keeping everything else. True when it did.</summary>
    public static bool ForgetGlobal(string server)
    {
        string path = McpConfigPath(Home());
        if (!File.Exists(path)) return false;
        JsonObject root;
        try
        {
            root = JsonNode.Parse(File.ReadAllText(path), documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true }) as JsonObject ?? [];
        }
        catch (JsonException)
        {
            return false;
        }
        if (root["mcpServers"] is not JsonObject servers || !servers.Remove(server)) return false;
        File.WriteAllText(path, root.ToJsonString(Indented));
        return true;
    }

    /// <summary>The answer agent's file in Syft Notes' home for agy, written when it isn't there as it should be.</summary>
    public static void PrepareAnswer()
    {
        string path = AgentPath(Home(), AnswerAgent);
        if (!File.Exists(path) || File.ReadAllText(path) != AnswerAgentFile()) Private(path, AnswerAgentFile());
    }

    /// <summary>Removes the setup agent's file (it holds the setup tools' token): when a turn ends, and when setup
    /// closes. A turn writes it again before it starts.</summary>
    public static void ForgetSetup()
    {
        try
        {
            string path = AgentPath(Home(), SetupAgent);
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>
    /// Writes the setup agent afresh for a turn, in Syft Notes' home for agy, with <paramref name="brief"/> and the
    /// setup tools' address and token (none: no tools at all); the folder the run works in is made empty. agy keeps the
    /// conversation itself, so a turn carries it on.
    /// </summary>
    public static void PrepareSetup(string workDir, string brief, string mcpUrl, string token)
    {
        Private(AgentPath(Home(), SetupAgent), SetupAgentFile(brief, mcpUrl, token));
        // The home's global MCP servers start in every session there, the setup chat's too: never the library's.
        ForgetGlobal(ClaudeTools.ServerName);
        // agy asks before any MCP tool runs, and a print run can't ask, so it refuses: the setup tools are allowed
        // (only them: any other server's tools stay refused, and the turn's guard stops a call to one besides).
        Allow(SettingsPath(Home()), [SetupToolsRule]);
        string room = SetupWorkspace(workDir);
        if (Directory.Exists(room))
        {
            foreach (string f in Directory.EnumerateFiles(room)) File.Delete(f);
            foreach (string d in Directory.EnumerateDirectories(room)) Directory.Delete(d, recursive: true);
        }
        Directory.CreateDirectory(room);
    }

    /// <summary>The permission rule that lets the setup tools run without asking.</summary>
    public const string SetupToolsRule = "mcp(" + SetupChat.Server + "/*)";

    /// <summary>Adds <paramref name="rules"/> to agy's <c>permissions.allow</c> in <paramref name="settingsPath"/> (made
    /// when there's none yet), keeping everything else in it. True when it changed it; false too when it isn't JSON
    /// (not Syft Notes' to repair).</summary>
    public static bool Allow(string settingsPath, IEnumerable<string> rules)
    {
        JsonObject root;
        try
        {
            root = File.Exists(settingsPath) ? JsonNode.Parse(File.ReadAllText(settingsPath)) as JsonObject ?? [] : [];
        }
        catch (JsonException)
        {
            return false;
        }
        var perms = root["permissions"] as JsonObject ?? [];
        var allow = perms["allow"] as JsonArray ?? [];
        var have = allow.Select(n => n is JsonValue v && v.TryGetValue(out string? r) ? r : "").ToHashSet();
        bool changed = false;
        foreach (string rule in rules)
            if (have.Add(rule))
            {
                allow.Add(rule);
                changed = true;
            }
        if (!changed) return false;
        perms["allow"] = allow;
        root["permissions"] = perms;
        Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);
        File.WriteAllText(settingsPath, root.ToJsonString(Indented));
        return true;
    }

    static void Private(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }

    static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    // --- running it ---------------------------------------------------------------------------------------------------

    /// <summary>What a run printed and how it ended.</summary>
    public sealed record Ran(int ExitCode, string Stdout, string Stderr);

    /// <summary>
    /// Runs agy with <paramref name="env"/>, its input piped and closed at once: a run like that that isn't signed in
    /// says "authentication required" and stops, instead of opening a browser to sign in (agy opens one only when its
    /// input isn't a pipe). Null when it didn't start or didn't end within <paramref name="timeout"/> (it and anything
    /// it started are stopped then).
    /// </summary>
    public static async Task<Ran?> RunPipedAsync(string exe, IReadOnlyList<string> args, IReadOnlyDictionary<string, string> env, TimeSpan timeout,
        CancellationToken ct = default)
    {
        var cmd = new List<string> { exe };
        cmd.AddRange(args);
        var launch = OperatingSystem.IsWindows() ? WindowsCommand.For(cmd) : new Launch(exe, [.. args]);
        var psi = new ProcessStartInfo(launch.FileName)
        {
            RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true, UseShellExecute = false,
            CreateNoWindow = true, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
        };
        if (launch.CommandLine is { } whole) psi.Arguments = whole;
        else foreach (string a in launch.Arguments) psi.ArgumentList.Add(a);
        psi.Environment["PATH"] = AiProvider.SearchPath();
        foreach (var (k, v) in env) psi.Environment[k] = v;
        Process? p;
        try
        {
            p = Process.Start(psi);
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return null;
        }
        if (p is null) return null;
        using var proc = p;
        try { proc.StandardInput.Close(); } catch (IOException) { }
        var stdout = proc.StandardOutput.ReadToEndAsync(CancellationToken.None);
        var stderr = proc.StandardError.ReadToEndAsync(CancellationToken.None);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);
        try
        {
            await proc.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            try { proc.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            return null;
        }
        return new Ran(proc.ExitCode, await stdout, await stderr);
    }

    /// <summary>Why a print run failed, in its own words: the envelope's <c>error</c>, else the last line it wrote on
    /// its error output that says something ("Error: authentication required…").</summary>
    public static string ErrorOf(string stdout, string stderr)
    {
        foreach (string line in stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Reverse())
        {
            JsonNode? n;
            try
            {
                n = JsonNode.Parse(line);
            }
            catch (JsonException)
            {
                continue;
            }
            var r = n?["result"] ?? n;
            if (r?["error"] is JsonValue e && e.TryGetValue(out string? why) && why.Length > 0) return why;
        }
        string[] lines = stderr.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return lines.LastOrDefault(l => l.StartsWith("Error:", StringComparison.OrdinalIgnoreCase)) is { } said ? said[6..].Trim()
            : lines.LastOrDefault() ?? "Gemini stopped without saying why.";
    }

    // --- the plan check ----------------------------------------------------------------------------------------------

    /// <summary>
    /// How much of the account's Gemini allowance is left, as <c>agy -p=/usage --output-format json</c> says it (a
    /// slash command print mode answers itself: no turn, nothing spent), in the student's words: "About 80% of this
    /// week's Gemini allowance is left.", or used up and when it's back. Only the Gemini group counts (agy also lists
    /// the Claude and GPT models it offers, which have their own). Null when it doesn't say.
    /// </summary>
    public static string? UsageWords(string printed) => Allowance(printed) is { } a ? Words(a.Left, a.Window, a.Back) : null;

    /// <summary>The allowance is used up (<see cref="UsageWords"/> found none left).</summary>
    public static bool UsedUp(string printed) => Allowance(printed) is { Left: <= 0 };

    /// <summary>How the plan check says it: "About 80% of this week's Gemini allowance is left."</summary>
    public static string Words(double left, string window, DateTimeOffset? back)
    {
        int percent = (int)Math.Round(Math.Clamp(left, 0, 1) * 100);
        string whose = window == "weekly" ? "this week's Gemini allowance" : "your Gemini allowance";
        string Cap(string w) => char.ToUpperInvariant(w[0]) + w[1..];
        if (percent <= 0)
            return back is { } t
                ? $"{Cap(whose)} is used up until {t.ToLocalTime().ToString("ddd d MMM, h:mm tt", System.Globalization.CultureInfo.InvariantCulture).Replace("AM", "am").Replace("PM", "pm")}."
                : $"{Cap(whose)} is used up for now.";
        return percent >= 100 ? $"All of {whose} is left." : $"About {percent}% of {whose} is left.";
    }

    /// <summary>The Gemini group's least-left bucket: what's left (0 to 1), its window ("weekly"), and when it's back.</summary>
    static (double Left, string Window, DateTimeOffset? Back)? Allowance(string printed)
    {
        JsonNode? root;
        try
        {
            root = JsonNode.Parse(printed);
        }
        catch (JsonException)
        {
            return null;
        }
        var groups = (root?["command"]?["data"]?["groups"] ?? root?["data"]?["groups"]) as JsonArray ?? [];
        var gemini = groups.Where(g => (g?["name"] is JsonValue n && n.TryGetValue(out string? name) ? name : "").Contains("Gemini", StringComparison.OrdinalIgnoreCase)).ToList();
        (double Left, string Window, DateTimeOffset? Back)? least = null;
        foreach (var bucket in (gemini.Count > 0 ? gemini : [.. groups]).SelectMany(g => g?["buckets"] as JsonArray ?? []))
        {
            if (Number(bucket?["remaining_fraction"] ?? bucket?["remainingFraction"]) is not { } left) continue;
            string window = bucket?["window"] is JsonValue w && w.TryGetValue(out string? win) ? win : "";
            DateTimeOffset? back = bucket?["reset_time"] is JsonValue r && r.TryGetValue(out string? at)
                && DateTimeOffset.TryParse(at, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal, out var t) ? t : null;
            if (least is null || left < least.Value.Left) least = (left, window, back);
        }
        return least;
    }

    static double? Number(JsonNode? n) => n is JsonValue v && v.TryGetValue(out double d) ? d : null;
}
