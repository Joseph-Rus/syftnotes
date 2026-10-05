using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using StudyStash.Core;
using Tomlyn;
using Tomlyn.Model;

namespace StudyStash.App.Services;

/// <summary>One AI app on this computer, as its own settings file has it: whether the app is here at all, whether
/// Study Stash is in its settings (and for this copy of Study Stash), when Settings added it, and when the app last
/// started it.</summary>
public sealed record AiAppState(string Id, string Name, bool Installed, bool Added, bool OtherCopy, DateTime? AddedAt, DateTime? Started, string ConfigPath);

/// <summary>What connecting or disconnecting an app did: the words to show, and, for the "what was written" panel,
/// the file, the copy of it kept from before, and the setup that went in (never anything secret: there's none in it).</summary>
public sealed record AiAppChange(bool Ok, string Say, string? File = null, string? Backup = null, string? Written = null);

/// <summary>
/// Connecting AI apps on this computer to your lectures (Settings → AI tool access). Claude Desktop, Claude Code,
/// Codex (its app, CLI and IDE extension share one settings file) and Gemini CLI each start Study Stash's MCP server
/// themselves (<c>StudyStash mcp --client ID</c>), which reads the library with the password this computer already
/// keeps in Study Stash's own settings. So their settings get no password and no address: nothing on the network,
/// no Tailscale. Every change is a merge into the app's own file (its other servers and settings stay as they were),
/// with the file as it was kept next to it, and a file that can't be read safely (not valid, or with comments a
/// rewrite would drop) is left alone. Claude on the web needs an internet address instead: that's the library's
/// Tailscale Funnel switch, not this.
/// </summary>
public sealed class ClaudeSetup
{
    public string Program { get; init; } = Platform.Desktop.Program;
    public string Home { get; init; } = Configs.DefaultHome;
    /// <summary>The student's home folder, where the AI apps keep their settings.</summary>
    public string UserHome { get; init; } = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    /// <summary>A command on the PATH (or where installers put it), or null.</summary>
    public Func<string, string?> Find { get; init; } = StudyStash.Core.Ai.AiProvider.Which;
    /// <summary>Whether an app by this name ("Claude", "Codex") is installed as a desktop app.</summary>
    public Func<string, bool> AppInstalled { get; init; } = DesktopAppInstalled;
    /// <summary>False in a test or a self-test: nothing is written to an AI app's settings.</summary>
    public bool CanWrite { get; init; } = true;

    string? desktop, claudeCode, codex, gemini;

    /// <summary>Claude Desktop's settings (claude_desktop_config.json).</summary>
    public string DesktopConfig { get => desktop ??= DesktopConfigIn(UserHome); init => desktop = value; }
    /// <summary>Claude Code's own settings, where its servers for every project are (~/.claude.json).</summary>
    public string ClaudeCodeConfig
    {
        get => claudeCode ??= Path.Combine(Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR") is { Length: > 0 } d ? d : UserHome, ".claude.json");
        init => claudeCode = value;
    }
    /// <summary>Codex's settings (~/.codex/config.toml), shared by the Codex app, its CLI and its IDE extension.</summary>
    public string CodexConfig
    {
        get => codex ??= Path.Combine(Environment.GetEnvironmentVariable("CODEX_HOME") is { Length: > 0 } d ? d : Path.Combine(UserHome, ".codex"), "config.toml");
        init => codex = value;
    }
    /// <summary>Gemini CLI's settings for every folder (~/.gemini/settings.json).</summary>
    public string GeminiConfig { get => gemini ??= Path.Combine(UserHome, ".gemini", "settings.json"); init => gemini = value; }

    public static ClaudeSetup ThisComputer(string home)
    {
        if (!Platform.Desktop.SystemChangesOff) return new() { Home = home };
        // A test or a self-test: a made-up home folder, so no AI app's real settings are even read, and nothing written.
        string user = Path.Combine(home, "ai-apps-home");
        return new()
        {
            Home = home, UserHome = user, CanWrite = false, Find = _ => null, AppInstalled = _ => false,
            DesktopConfig = Path.Combine(user, "Claude", "claude_desktop_config.json"), ClaudeCodeConfig = Path.Combine(user, ".claude.json"),
            CodexConfig = Path.Combine(user, ".codex", "config.toml"), GeminiConfig = Path.Combine(user, ".gemini", "settings.json"),
        };
    }

    /// <summary>The apps, in the order Settings lists them.</summary>
    public static readonly IReadOnlyList<(string Id, string Name)> Apps =
        [("claude-desktop", "Claude Desktop"), ("claude-code", "Claude Code"), ("codex", "Codex"), ("gemini", "Gemini CLI")];

    static string NameOf(string id) => Apps.First(a => a.Id == id).Name;

    /// <summary>Claude Desktop's settings file. On Windows the Microsoft Store (MSIX) build keeps its AppData inside its
    /// package folder, and reads that copy first, so that's the one to change when it's there.</summary>
    public static string DesktopConfigIn(string userHome)
    {
        if (OperatingSystem.IsWindows())
        {
            string packages = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Packages");
            try
            {
                foreach (string pkg in Directory.Exists(packages) ? Directory.EnumerateDirectories(packages, "Claude_*") : [])
                {
                    string dir = Path.Combine(pkg, "LocalCache", "Roaming", "Claude");
                    if (Directory.Exists(dir)) return Path.Combine(dir, "claude_desktop_config.json");
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
            }
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Claude", "claude_desktop_config.json");
        }
        return OperatingSystem.IsMacOS()
            ? Path.Combine(userHome, "Library", "Application Support", "Claude", "claude_desktop_config.json")
            : Path.Combine(userHome, ".config", "Claude", "claude_desktop_config.json");
    }

    static bool DesktopAppInstalled(string name)
    {
        if (OperatingSystem.IsMacOS())
            return Directory.Exists($"/Applications/{name}.app")
                || Directory.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Applications", name + ".app"));
        if (OperatingSystem.IsWindows() && name == "Claude")
            return Directory.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AnthropicClaude"));
        return false;
    }

    /// <summary>The MCP server's command: the app, told to be the server (and where its settings are, if not the usual).</summary>
    public List<string> McpArgs => Home == Configs.DefaultHome ? ["mcp"] : ["--home", Home, "mcp"];

    List<string> ArgsFor(string id) => [.. McpArgs, McpClients.Option, id];

    JsonObject EntryFor(string id)
    {
        var entry = new JsonObject();
        if (id == "claude-code") entry["type"] = "stdio"; // as `claude mcp add` writes it
        entry["command"] = Program;
        entry["args"] = new JsonArray(ArgsFor(id).Select(a => (JsonNode?)a).ToArray());
        return entry;
    }

    static readonly JsonSerializerOptions Indented = new() { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>The <c>mcpServers</c> JSON any other MCP app's settings take, to paste in by hand.</summary>
    public string McpJson => new JsonObject { ["mcpServers"] = new JsonObject { [ClaudeTools.ServerName] = new JsonObject
    {
        ["command"] = Program, ["args"] = new JsonArray(McpArgs.Select(a => (JsonNode?)a).ToArray()),
    } } }.ToJsonString(Indented);

    static string TomlString(string s) => "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

    /// <summary>Codex's block for Study Stash, under its own heading in config.toml.</summary>
    public string CodexBlock(string id = "codex") => $"""
        [mcp_servers.{ClaudeTools.ServerName}]
        command = {TomlString(Program)}
        args = [{string.Join(", ", ArgsFor(id).Select(TomlString))}]
        """;

    string ConfigOf(string id) => id switch
    {
        "claude-desktop" => DesktopConfig,
        "claude-code" => ClaudeCodeConfig,
        "codex" => CodexConfig,
        "gemini" => GeminiConfig,
        _ => throw new ArgumentException($"no AI app called {id}", nameof(id)),
    };

    bool IsInstalled(string id) => id switch
    {
        "claude-desktop" => AppInstalled("Claude") || Directory.Exists(Path.GetDirectoryName(DesktopConfig)),
        "claude-code" => Find("claude") is not null || File.Exists(ClaudeCodeConfig),
        "codex" => Find("codex") is not null || AppInstalled("Codex") || Directory.Exists(Path.GetDirectoryName(CodexConfig)),
        "gemini" => Find("gemini") is not null || Directory.Exists(Path.GetDirectoryName(GeminiConfig)),
        _ => false,
    };

    bool SameProgram(string? command) => command is not null
        && string.Equals(command, Program, OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    /// <summary>Every app, read fresh from its own settings file (read only: nothing is changed).</summary>
    public IReadOnlyList<AiAppState> States() => [.. Apps.Select(a => State(a.Id))];

    public AiAppState State(string id)
    {
        string path = ConfigOf(id);
        string? command = null;
        bool added = false;
        try
        {
            if (File.Exists(path))
            {
                string text = File.ReadAllText(path);
                if (id == "codex")
                {
                    if (TomlOf(text)?["mcp_servers"] is TomlTable servers && servers.TryGetValue(ClaudeTools.ServerName, out var s))
                    {
                        added = true;
                        command = (s as TomlTable)?.TryGetValue("command", out var c) == true ? c as string : null;
                    }
                }
                else if (JsonOf(text, lenient: true)?["mcpServers"]?[ClaudeTools.ServerName] is JsonObject entry)
                {
                    added = true;
                    command = entry["command"] is JsonValue v && v.TryGetValue(out string? c) ? c : null;
                }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
        return new AiAppState(id, NameOf(id), IsInstalled(id) || added, added, added && !SameProgram(command),
            ToLocal(McpClients.When(McpClients.AddedFile(Home, id))), ToLocal(McpClients.When(McpClients.StartedFile(Home, id))), path);
    }

    static DateTime? ToLocal(DateTime? utc) => utc?.ToLocalTime();

    /// <summary>Adds Study Stash to this app's settings, or puts this copy of it in place of another.</summary>
    public AiAppChange Connect(string id) => Change(id, add: true);

    /// <summary>Takes Study Stash out of this app's settings, leaving everything else in them.</summary>
    public AiAppChange Disconnect(string id) => Change(id, add: false);

    AiAppChange Change(string id, bool add)
    {
        string name = NameOf(id), path = ConfigOf(id);
        string written = id == "codex" ? CodexBlock(id) : new JsonObject { ["mcpServers"] = new JsonObject { [ClaudeTools.ServerName] = EntryFor(id) } }.ToJsonString(Indented);
        if (!CanWrite) return new(false, $"Changing {name}'s settings is off here.", path, null, add ? written : null);
        AiAppChange result;
        try
        {
            result = id == "codex" ? ChangeToml(name, path, add, written) : ChangeJson(id, name, path, add, written);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return new(false, $"Couldn't change {name}'s settings ({path}): {e.Message}", path, null, add ? written : null);
        }
        if (result.Ok)
        {
            string marker = McpClients.AddedFile(Home, id);
            try
            {
                if (add) McpClients.Touch(marker);
                else File.Delete(marker);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
            }
        }
        return result;
    }

    static string Restart(string name) => name switch
    {
        "Claude Desktop" => "Quit and reopen Claude Desktop to load it.",
        "Claude Code" => "Start a new Claude Code session to load it.",
        "Codex" => "Restart Codex to load it.",
        _ => $"Start {name} again to load it.",
    };

    AiAppChange ChangeJson(string id, string name, string path, bool add, string written)
    {
        string? before = File.Exists(path) ? File.ReadAllText(path) : null;
        JsonObject root;
        if (string.IsNullOrWhiteSpace(before)) root = [];
        else if (JsonOf(before, lenient: false) is { } strict) root = strict;
        else
            return new(false, JsonOf(before, lenient: true) is not null
                ? $"{name}'s settings file has comments in it, which rewriting it would lose, so it's left alone. Add the setup below to it by hand."
                : $"{name}'s settings file isn't valid JSON, so it's left alone. Fix it, or add the setup below to it by hand.", path, null, add ? written : null);
        if (root["mcpServers"] is { } existing && existing is not JsonObject)
            return new(false, $"{name}'s settings have mcpServers in a shape Study Stash doesn't know, so they're left alone.", path, null, add ? written : null);
        var servers = root["mcpServers"] as JsonObject;
        if (add)
        {
            if (servers is null) root["mcpServers"] = servers = [];
            servers[ClaudeTools.ServerName] = EntryFor(id);
        }
        else if (servers is null || !servers.Remove(ClaudeTools.ServerName))
            return new(true, $"{name} doesn't have Study Stash.", path);
        else if (servers.Count == 0) root.Remove("mcpServers"); // the file goes back to how it was before Study Stash

        string after = root.ToJsonString(Indented);
        if (before is not null && before.Contains("\r\n", StringComparison.Ordinal)) after = after.ReplaceLineEndings("\r\n");
        if (before is null || before.EndsWith('\n')) after += before?.Contains("\r\n", StringComparison.Ordinal) == true ? "\r\n" : "\n";
        string? backup = Save(path, before, after);
        return add
            ? new(true, $"Added Study Stash to {name}. {Restart(name)}", path, backup, written)
            : new(true, $"Took Study Stash out of {name}. Its other settings are as they were.", path, backup);
    }

    // A heading for Study Stash's own table in config.toml, or one of its sub-tables ([mcp_servers.study-stash.env]).
    static readonly Regex OurHeading = new($@"^\s*\[\s*mcp_servers\s*\.\s*(?:{Regex.Escape(ClaudeTools.ServerName)}|""{Regex.Escape(ClaudeTools.ServerName)}""|'{Regex.Escape(ClaudeTools.ServerName)}')\s*(?:\.[^\]]*)?\]\s*(?:#.*)?$");
    static readonly Regex AnyHeading = new(@"^\s*\[");

    AiAppChange ChangeToml(string name, string path, bool add, string written)
    {
        string before = File.Exists(path) ? File.ReadAllText(path) : "";
        string Manual(string why) => why + (add ? " Add the setup below to it by hand." : "");
        if (before.Trim().Length > 0 && TomlOf(before) is null)
            return new(false, Manual($"{name}'s config.toml isn't valid TOML, so it's left alone."), path, null, add ? written : null);
        string nl = before.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var (rest, had) = WithoutOurTables(before, nl);
        var left = TomlOf(rest);
        if (left is null || left["mcp_servers"] is TomlTable s && s.ContainsKey(ClaudeTools.ServerName))
            return new(false, Manual($"{name}'s config.toml has Study Stash set up by hand in a way Study Stash won't rewrite, so it's left alone."), path, null, add ? written : null);
        string after;
        if (add) after = (rest.Trim().Length > 0 ? rest.TrimEnd('\r', '\n') + nl + nl : "") + written.ReplaceLineEndings(nl) + nl;
        else if (!had) return new(true, $"{name} doesn't have Study Stash.", path);
        else after = rest;
        // What Codex will read has Study Stash exactly when it should, or nothing is written.
        if (TomlOf(after) is not { } check || (check["mcp_servers"] is TomlTable c && c.ContainsKey(ClaudeTools.ServerName)) != add)
            return new(false, Manual($"Couldn't make a change to {name}'s config.toml that Codex would read, so it's left alone."), path, null, add ? written : null);
        string? backup = Save(path, File.Exists(path) ? before : null, after);
        return add
            ? new(true, $"Added Study Stash to {name}. {Restart(name)}", path, backup, written)
            : new(true, $"Took Study Stash out of {name}. Its other settings are as they were.", path, backup);
    }

    /// <summary>config.toml without Study Stash's own tables (and the blank lines that led up to them), and whether it
    /// had any. Every other line stays exactly as it was.</summary>
    static (string Kept, bool Had) WithoutOurTables(string text, string nl)
    {
        if (text.Length == 0) return ("", false);
        var lines = text.Split('\n').Select(l => l.TrimEnd('\r')).ToList();
        bool endsWithNewline = text.EndsWith('\n');
        if (endsWithNewline) lines.RemoveAt(lines.Count - 1);
        var kept = new List<string>();
        bool skipping = false, had = false, separate = false;
        foreach (string line in lines)
        {
            if (OurHeading.IsMatch(line))
            {
                had = skipping = true;
                while (kept.Count > 0 && kept[^1].Trim().Length == 0)
                {
                    kept.RemoveAt(kept.Count - 1);
                    separate = true;
                }
                continue;
            }
            if (skipping && AnyHeading.IsMatch(line))
            {
                skipping = false;
                if (separate && kept.Count > 0) kept.Add("");
                separate = false;
            }
            if (!skipping) kept.Add(line);
        }
        if (!had) return (text, false);
        string rest = string.Join(nl, kept);
        return (rest.Length > 0 ? rest + nl : "", true);
    }

    static TomlTable? TomlOf(string text)
    {
        try
        {
            return TomlSerializer.Deserialize<TomlTable>(text);
        }
#pragma warning disable CA1031 // whatever the parser throws, the answer is the same: not TOML Study Stash can rewrite
        catch (Exception)
#pragma warning restore CA1031
        {
            return null;
        }
    }

    static JsonObject? JsonOf(string text, bool lenient)
    {
        try
        {
            return JsonNode.Parse(text, documentOptions: lenient
                ? new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true }
                : default) as JsonObject;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Writes <paramref name="after"/> in place of the file, keeping what was there as
    /// <c>NAME.study-stash-backup</c> beside it. A link (a dotfiles setup) is followed, so the file it points to is
    /// changed, not the link replaced; the file keeps its permissions; it's swapped in whole, never half-written.
    /// The backup's path, or null when there was no file before.</summary>
    static string? Save(string path, string? before, string after)
    {
        string target = path;
        if (new FileInfo(path).LinkTarget is not null && new FileInfo(path).ResolveLinkTarget(returnFinalTarget: true) is { } real) target = real.FullName;
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        string? backup = null;
        if (before is not null && File.Exists(target))
        {
            backup = target + ".study-stash-backup";
            File.Copy(target, backup, overwrite: true);
        }
        string tmp = target + ".study-stash-new";
        File.WriteAllText(tmp, after, new UTF8Encoding(false));
        if (!OperatingSystem.IsWindows() && File.Exists(target)) File.SetUnixFileMode(tmp, File.GetUnixFileMode(target));
        File.Move(tmp, target, overwrite: true);
        return backup;
    }
}
