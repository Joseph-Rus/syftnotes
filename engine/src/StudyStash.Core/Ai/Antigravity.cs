using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace StudyStash.Core.Ai;

/// <summary>
/// Google's Antigravity CLI (<c>agy</c>), which Gemini runs through: Google moved personal Google accounts (free, AI Pro,
/// Ultra) from Gemini CLI to it on 18 June 2026, and Gemini CLI now serves only API keys and company licences.
/// <para>
/// Study Stash runs it with a home of its own (<see cref="Home"/>; agy keeps everything under <c>~/.gemini</c>, and reads
/// its home from HOME, or USERPROFILE on Windows), so nothing of the student's own agy (their MCP servers, plugins,
/// skills, hooks, rules, settings) ever runs inside Study Stash's work, and Study Stash never writes theirs. The sign-in
/// isn't in that home: agy keeps it in the computer's own keychain (the Mac's Keychain, Windows' Credential Manager),
/// so a student already signed in to agy is signed in here too.
/// </para>
/// <para>
/// The setup chat gets a home of its own again, beside its folder (<see cref="SetupHome"/>): settings that refuse every
/// command, file, web page and browser action and allow only the setup tools, the setup guide as an agent of its own
/// (<see cref="SetupAgent"/>) with Antigravity's default tools and prompt left out and none of the home's customizations
/// taken in, and the setup tools as the only MCP server.
/// </para>
/// </summary>
public static class Antigravity
{
    public const string Binary = "agy";
    /// <summary>The setup guide's agent, in the setup chat's home.</summary>
    public const string SetupAgent = "study-stash-setup";
    /// <summary>The one tool agy calls any MCP server's tools through, naming the server and the tool.</summary>
    public const string McpTool = "call_mcp_tool";

    /// <summary>Study Stash's own home for agy, where its notes run and its sign-in is checked: the library's home's
    /// <c>gemini</c> folder. The tests give their own.</summary>
    public static Func<string> Home { get; set; } = () => Path.Combine(Configs.DefaultHome, "gemini");

    /// <summary>The setup chat's own home for agy, beside the chat's folder (never inside it: the folder agy works in
    /// is one it may read, and this home holds the setup tools' token).</summary>
    public static string SetupHome(string workDir) => Path.Combine(workDir, "gemini-home");

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
    /// The sign-in in a terminal window (when the hidden one can't open the browser), in Study Stash's own home for
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

    // --- the setup chat's home -----------------------------------------------------------------------------------------

    /// <summary>Everything the setup chat may never do, as agy's permission rules: run a command (in or out of its
    /// sandbox), read or write a file, read a web page or act in a browser. A refused action in a print run is
    /// skipped, and the turn's guard stops the run at once besides (<see cref="SetupChatParser"/>).</summary>
    public static readonly string[] SetupDenied =
        ["command(*)", "unsandboxed(*)", "read_file(*)", "write_file(*)", "read_url(*)", "execute_url(*)"];

    /// <summary>The setup chat's agy settings: nothing allowed but the setup tools, the terminal sandbox on, nothing
    /// outside its folder, no telemetry, tips or surveys.</summary>
    public static string SetupSettings() => new JsonObject
    {
        ["permissions"] = new JsonObject
        {
            ["allow"] = new JsonArray($"mcp({SetupChat.Server}/*)"),
            ["deny"] = new JsonArray([.. SetupDenied.Select(d => (JsonNode)d)]),
        },
        ["toolPermission"] = "request-review",
        ["enableTerminalSandbox"] = true,
        ["allowNonWorkspaceAccess"] = false,
        ["enableTelemetry"] = false,
        ["showTips"] = false,
        ["showFeedbackSurvey"] = false,
    }.ToJsonString(Indented);

    /// <summary>
    /// The setup guide as an agy agent: picked with <c>--agent</c>, never one another agent can call, with
    /// Antigravity's default prompt sections and built-in tools left out (<c>excludeDefaultComponents</c>, and no tools
    /// listed), none of the home's skills, rules, plugins, subagents or MCP servers taken in, and commands off. Its body
    /// (after the H1) is the brief.
    /// </summary>
    public static string SetupAgentFile(string brief) => $"""
        ---
        name: {SetupAgent}
        description: Study Stash's setup guide. It only uses the study_stash_setup tools.
        mainAgent: true
        subagent: false
        hidden: true
        excludeDefaultComponents: true
        inheritCustomizations: false
        commandExecutionPolicy: "off"
        tools: []
        mcpServers:
          - {SetupChat.Server}
        ---
        # Study Stash setup guide

        {brief.Replace("\r\n", "\n").Trim()}

        """.Replace("\r\n", "\n");

    /// <summary>The setup chat's MCP config: the setup tools only, or nothing for a turn with no tools. agy doesn't
    /// fill in <c>${VAR}</c> in a header (tried: it sends the words as they are), so the token is written in, in a file
    /// only this account can read, in Study Stash's own folder, and it lasts only while setup's window is open.</summary>
    public static string SetupMcpConfig(string mcpUrl, string token)
    {
        var servers = new JsonObject();
        if (mcpUrl.Length > 0)
            servers[SetupChat.Server] = new JsonObject
            {
                ["serverUrl"] = mcpUrl,
                ["headers"] = new JsonObject { ["Authorization"] = "Bearer " + token },
            };
        return new JsonObject { ["mcpServers"] = servers }.ToJsonString(Indented);
    }

    /// <summary>
    /// Writes the setup chat's home afresh for a turn (its conversations, which agy keeps there, stay): the settings, the
    /// setup agent with <paramref name="brief"/>, and the setup tools' address and token (none: no tools at all). The
    /// folder the run works in is made empty. The home's path.
    /// </summary>
    public static string PrepareSetup(string workDir, string brief, string mcpUrl, string token)
    {
        string home = SetupHome(workDir);
        Private(SettingsPath(home), SetupSettings());
        Private(AgentPath(home, SetupAgent), SetupAgentFile(brief));
        Private(McpConfigPath(home), SetupMcpConfig(mcpUrl, token));
        string room = SetupWorkspace(workDir);
        if (Directory.Exists(room))
        {
            foreach (string f in Directory.EnumerateFiles(room)) File.Delete(f);
            foreach (string d in Directory.EnumerateDirectories(room)) Directory.Delete(d, recursive: true);
        }
        Directory.CreateDirectory(room);
        return home;
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

    /// <summary>How much of the account's Gemini allowance is left, as <c>agy -p=/usage --output-format json</c> says
    /// it (a slash command print mode answers itself: no turn, nothing spent), in the student's words: "About 80% of
    /// this week's Gemini allowance is left." Null when it doesn't say.</summary>
    public static string? UsageWords(string printed)
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
        // The answer may come whole or inside the print envelope's structured output.
        var usage = root?["structured_output"] ?? root?["usage_info"] ?? root;
        double? left = null;
        foreach (var node in Walk(usage))
        {
            if (node is not JsonObject o) continue;
            if (Number(o["remainingFraction"] ?? o["remaining_fraction"]) is { } f) left = left is { } l ? Math.Min(l, f) : f;
            else if (Number(o["remaining_percent"] ?? o["remainingPercent"]) is { } p) left = left is { } l2 ? Math.Min(l2, p / 100) : p / 100;
            else if (Number(o["used_percent"] ?? o["usedPercent"]) is { } u) left = left is { } l3 ? Math.Min(l3, 1 - u / 100) : 1 - u / 100;
        }
        if (left is not { } share) return null;
        int percent = (int)Math.Round(Math.Clamp(share, 0, 1) * 100);
        return percent switch
        {
            0 => "Your Gemini allowance is used up for now.",
            100 => "All of your Gemini allowance is left.",
            _ => $"About {percent}% of your Gemini allowance is left.",
        };
    }

    /// <summary>The allowance is used up (<see cref="UsageWords"/> found none left).</summary>
    public static bool UsedUp(string printed) => UsageWords(printed) is { } w && w.Contains("used up", StringComparison.Ordinal);

    static IEnumerable<JsonNode?> Walk(JsonNode? node)
    {
        yield return node;
        if (node is JsonObject o)
            foreach (var (_, v) in o)
                foreach (var n in Walk(v)) yield return n;
        else if (node is JsonArray a)
            foreach (var v in a)
                foreach (var n in Walk(v)) yield return n;
    }

    static double? Number(JsonNode? n) => n is JsonValue v && v.TryGetValue(out double d) ? d : null;
}
