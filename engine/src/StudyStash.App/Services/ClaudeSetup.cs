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
/// Syft Notes is in its settings (and for this copy of Syft Notes), when Settings added it, and when the app last
/// started it. <paramref name="Outdated"/>: this copy is in there, but not the way Connect writes it (pasted in by
/// hand, say), so it never says which app started it and Settings can't tell when it's connected.
/// <paramref name="CanReopen"/>: it's a desktop app Syft Notes can close and open again for the student.</summary>
public sealed record AiAppState(string Id, string Name, bool Installed, bool Added, bool OtherCopy, DateTime? AddedAt, DateTime? Started, string ConfigPath,
    bool Outdated = false, bool CanReopen = false)
{
    /// <summary>Syft Notes is in the app's settings only under its old name (study-stash): Connect renames it.</summary>
    public bool OldName { get; init; }
    /// <summary>A copy of Syft Notes' entry is in a settings file this app no longer reads (Claude's folder from before
    /// it moved, on Windows): Connect takes it out.</summary>
    public bool Stale { get; init; }
    /// <summary>The desktop app is open now, so it has to be quit and opened again to load what Connect wrote.</summary>
    public bool Running { get; init; }
    /// <summary>The app as a student calls it, on the AI's card ("Claude app", "Claude Code", "ChatGPT app").</summary>
    public string Words { get; init; } = Name;
    /// <summary>The AI the app belongs to: "claude", "codex" (ChatGPT) or "gemini".</summary>
    public string Provider => ClaudeSetup.ProviderOf(Id);
    /// <summary>Syft Notes is in the file the app reads, for this copy, the way Connect writes it.</summary>
    public bool Good => Added && !OtherCopy && !Outdated && !OldName && !Stale;
    /// <summary>The app has started Syft Notes since it was added (it has loaded it).</summary>
    public bool Loaded => Started is { } s && (AddedAt is null || s >= AddedAt);
    /// <summary>When Syft Notes last opened the app again itself (after closing it for Connect).</summary>
    public DateTime? Opened { get; init; }
    /// <summary>The app is open with Syft Notes in its settings but hasn't loaded it, and wasn't opened by Syft Notes
    /// since it went in: it has to be closed and opened again.</summary>
    public bool NeedsReopen => Good && Running && !Loaded && !(Opened is { } o && (AddedAt is null || o >= AddedAt));
}

/// <summary>Where Claude Desktop reads its settings on this computer (<see cref="Config"/>), and the other places
/// Syft Notes (or Windows, for the Microsoft Store build) may have put them before, which it no longer reads.</summary>
public sealed record DesktopPlace(string Config, IReadOnlyList<string> Others);

/// <summary>What connecting or disconnecting an app did: the words to show, and, for the "what was written" panel,
/// the file, the copy of it kept from before, and the setup that went in (never anything secret: there's none in it).</summary>
public sealed record AiAppChange(bool Ok, string Say, string? File = null, string? Backup = null, string? Written = null);

/// <summary>
/// Connecting the AI apps on this computer to your lectures: what Connect on an AI's card in Settings → Your AI does
/// for that AI's apps. Claude Desktop and Claude Code (Claude), the ChatGPT app with Codex (ChatGPT: the app, the Codex
/// CLI and its IDE extension share one settings file, Codex's), and Gemini CLI and Google's Antigravity CLI (Gemini)
/// each start Syft Notes' MCP server themselves (<c>StudyStash mcp --client ID</c>), which reads the library with the
/// password this computer already keeps in Syft Notes' own settings. So their settings get no password and no address:
/// nothing on the network, no Tailscale. Every change is a merge into the file the app really reads (its other servers
/// and settings stay as they were), with the file as it was kept next to it, and a file that can't be read safely (not
/// valid, or with comments a rewrite would drop) is left alone. Syft Notes' entry is called
/// <see cref="ClaudeTools.ServerName"/>; one under its old name is renamed, and one left in a file the app no longer
/// reads is taken out. Claude and ChatGPT on the web need an internet address instead: that's the library's Tailscale
/// Funnel switch, not this.
/// </summary>
public sealed class ClaudeSetup
{
    public string Program { get; init; } = Platform.Desktop.Program;
    public string Home { get; init; } = Configs.DefaultHome;
    /// <summary>The student's home folder, where the AI apps keep their settings.</summary>
    public string UserHome { get; init; } = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    /// <summary>Windows' AppData\Local and AppData\Roaming, wherever this account has them (a redirected AppData too):
    /// where Claude Desktop keeps its settings there.</summary>
    public string LocalAppData { get; init; } = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    public string AppData { get; init; } = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
    /// <summary>Which computer's places to look in (a test looks at Windows' on a Mac).</summary>
    public bool Windows { get; init; } = OperatingSystem.IsWindows();
    public bool Mac { get; init; } = OperatingSystem.IsMacOS();
    /// <summary>A command on the PATH (or where installers put it), or null.</summary>
    public Func<string, string?> Find { get; init; } = StudyStash.Core.Ai.AiProvider.Which;
    /// <summary>Whether an app by this name ("Claude", "ChatGPT") is installed as a desktop app; null looks for it.</summary>
    public Func<string, bool>? AppInstalled { get; init; }
    /// <summary>Whether a desktop app (by this setup's id: claude-desktop, codex) is open now; null looks.</summary>
    public Func<string, bool>? Running { get; init; }
    /// <summary>False in a test or a self-test: nothing is written to an AI app's settings.</summary>
    public bool CanWrite { get; init; } = true;
    /// <summary>Closes a desktop app (by this setup's id), true once it's closed (<see cref="Platform.DesktopApps"/>).
    /// Null: Syft Notes can't, and the student is told to. Only real where whether it's open is looked at for real too,
    /// so a test that stands in for <see cref="Running"/> never closes an app that's really open.</summary>
    public Func<string, Task<bool>>? QuitApp { get => quitSet ? quitApp : Real(id => Platform.DesktopApps.This.QuitAsync(id)); init => (quitApp, quitSet) = (value, true); }
    /// <summary>Opens a desktop app Syft Notes closed; whether it did.</summary>
    public Func<string, Task<bool>>? OpenApp { get => openSet ? openApp : Real(id => Platform.DesktopApps.This.OpenAsync(id)); init => (openApp, openSet) = (value, true); }
    Func<string, Task<bool>>? quitApp, openApp;
    bool quitSet, openSet;
    Func<string, Task<bool>>? Real(Func<string, Task<bool>> real) =>
        Running is null && !Platform.Desktop.SystemChangesOff && (OperatingSystem.IsMacOS() || OperatingSystem.IsWindows()) ? real : null;

    /// <summary>When Syft Notes last opened each app again itself, by the settings file it reads.</summary>
    static readonly System.Collections.Concurrent.ConcurrentDictionary<string, DateTime> opened = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Starts a server the way an app's settings say (command, arguments) and asks for its tools; null: not checked.</summary>
    public Func<string, IReadOnlyList<string>, Task<McpProbeResult>>? Probe { get; init; } = (command, args) => McpProbe.ToolsAsync(command, args);

    string? claudeCode, codex, gemini, antigravity;
    DesktopPlace? desktop;

    /// <summary>Where Claude Desktop reads its settings, and where it no longer does.</summary>
    public DesktopPlace Desktop => desktop ??= DesktopPlaceIn(UserHome, LocalAppData, AppData, Windows, Mac);

    /// <summary>Claude Desktop's settings (claude_desktop_config.json), the one it reads.</summary>
    public string DesktopConfig { get => Desktop.Config; init => desktop = new DesktopPlace(value, []); }
    /// <summary>Claude Code's own settings, where its servers for every project are (~/.claude.json).</summary>
    public string ClaudeCodeConfig
    {
        get => claudeCode ??= Path.Combine(Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR") is { Length: > 0 } d ? d : UserHome, ".claude.json");
        init => claudeCode = value;
    }
    /// <summary>Codex's settings (~/.codex/config.toml), which the ChatGPT app, the Codex CLI and its IDE extension all read.</summary>
    public string CodexConfig
    {
        get => codex ??= Path.Combine(Environment.GetEnvironmentVariable("CODEX_HOME") is { Length: > 0 } d ? d : Path.Combine(UserHome, ".codex"), "config.toml");
        init => codex = value;
    }
    /// <summary>Gemini CLI's settings for every folder (~/.gemini/settings.json).</summary>
    public string GeminiConfig { get => gemini ??= Path.Combine(UserHome, ".gemini", "settings.json"); init => gemini = value; }
    /// <summary>The student's own Antigravity CLI's MCP servers (~/.gemini/config/mcp_config.json, where <c>agy mcp add</c>
    /// puts them). Never Syft Notes' own home for agy (<see cref="StudyStash.Core.Ai.Antigravity.Home"/>), whose
    /// global servers would start inside Syft Notes' own work.</summary>
    public string AntigravityConfig
    {
        get => antigravity ??= StudyStash.Core.Ai.Antigravity.McpConfigPath(UserHome);
        init => antigravity = value;
    }

    public static ClaudeSetup ThisComputer(string home)
    {
        if (!Platform.Desktop.SystemChangesOff) return new() { Home = home };
        // A test or a self-test: a made-up home folder, so no AI app's real settings are even read, and nothing written.
        string user = Path.Combine(home, "ai-apps-home");
        return new()
        {
            Home = home, UserHome = user, CanWrite = false, Find = _ => null, AppInstalled = _ => false, Running = _ => false, QuitApp = null, OpenApp = null, Probe = null,
            LocalAppData = Path.Combine(user, "AppData", "Local"), AppData = Path.Combine(user, "AppData", "Roaming"),
            DesktopConfig = Path.Combine(user, "Claude", "claude_desktop_config.json"), ClaudeCodeConfig = Path.Combine(user, ".claude.json"),
            CodexConfig = Path.Combine(user, ".codex", "config.toml"), GeminiConfig = Path.Combine(user, ".gemini", "settings.json"),
            AntigravityConfig = Path.Combine(user, ".gemini", "config", "mcp_config.json"),
        };
    }

    /// <summary>The apps, in the order Settings lists them.</summary>
    public static readonly IReadOnlyList<(string Id, string Name)> Apps =
        [("claude-desktop", "Claude Desktop"), ("claude-code", "Claude Code"), ("codex", "ChatGPT"), ("gemini", "Gemini CLI"), ("antigravity", "Antigravity CLI")];

    static string NameOf(string id) => Apps.First(a => a.Id == id).Name;

    /// <summary>The AI an app belongs to, by the library's ids: "claude", "codex" (ChatGPT) or "gemini".</summary>
    public static string ProviderOf(string appId) => appId switch
    {
        "claude-desktop" or "claude-code" => "claude",
        "codex" => "codex",
        _ => "gemini",
    };

    /// <summary>The apps of one AI, in the order its card names them.</summary>
    public static IEnumerable<string> AppsOf(string provider) => Apps.Select(a => a.Id).Where(id => ProviderOf(id) == provider);

    /// <summary>The app as a student calls it on its AI's card.</summary>
    string WordsFor(string id) => id switch
    {
        "claude-desktop" => "Claude app",
        "codex" => Installed("ChatGPT") ? "ChatGPT app" : "Codex",
        _ => NameOf(id),
    };

    /// <summary>The desktop app behind a row, as its own menu bar names it ("Claude", "ChatGPT"), and its bundle on a
    /// Mac; null for the ones that live in a terminal.</summary>
    public static (string Name, string Bundle)? DesktopApp(string id) => id switch
    {
        "claude-desktop" => ("Claude", "com.anthropic.claudefordesktop"),
        "codex" => ("ChatGPT", "com.openai.codex"),
        _ => null,
    };

    /// <summary>The ChatGPT and Claude desktop apps that are on this computer, as their settings stand: the ones
    /// guided setup offers to connect.</summary>
    public IReadOnlyList<AiAppState> DesktopStates() =>
        [.. Apps.Where(a => DesktopApp(a.Id) is { } desktop && Installed(desktop.Name)).Select(a => State(a.Id))];

    /// <summary>Syft Notes can close the desktop apps here and open them again.</summary>
    public bool CanClose => QuitApp is not null && OpenApp is not null;

    /// <summary>The AI's desktop app that is open and whose settings Connect has to change, so it has to be closed first
    /// (it would write its own copy back over them); null when there's none, or Syft Notes can't close it.</summary>
    public AiAppState? OpenToChange(string provider) =>
        CanClose ? StatesOf(provider).FirstOrDefault(a => a is { Installed: true, Good: false, Running: true } && DesktopApp(a.Id) is not null) : null;

    /// <summary>Closes a desktop app; true once it's closed.</summary>
    public Task<bool> CloseAsync(string id) => QuitApp is { } quit ? quit(id) : Task.FromResult(false);

    /// <summary>Opens a desktop app again; whether it did. Remembered, so it isn't asked to be opened again once more.</summary>
    public async Task<bool> OpenAsync(string id)
    {
        if (OpenApp is not { } open || !await open(id)) return false;
        opened[ConfigOf(id)] = DateTime.Now;
        return true;
    }

    /// <summary>Closes the row's desktop app and opens it again, so it loads the settings Connect wrote.</summary>
    public async Task<AiAppChange> ReopenAsync(string id)
    {
        if (DesktopApp(id) is not { } desktop || !CanClose || !Installed(desktop.Name)) return new(false, $"Quit {NameOf(id)} completely, then open it again.");
        if (!await CloseAsync(id)) return new(false, AiConnect.CouldntCloseWords(desktop.Name, Windows, "open it again"));
        return await OpenAsync(id) ? new(true, "") : new(false, $"Open {desktop.Name} again.");
    }

    const string DesktopFile = "claude_desktop_config.json";

    /// <summary>Claude Desktop's settings file on this computer, for a Mac and Linux.</summary>
    public static string DesktopConfigIn(string userHome) => OperatingSystem.IsMacOS()
        ? Path.Combine(userHome, "Library", "Application Support", "Claude", DesktopFile)
        : Path.Combine(userHome, ".config", "Claude", DesktopFile);

    /// <summary>
    /// Where Claude Desktop reads its settings. A Mac: ~/Library/Application Support/Claude. Linux: ~/.config/Claude.
    /// Windows has three:
    /// <list type="bullet">
    /// <item>the Microsoft Store (MSIX) build since it moved its data out of its package (Claude 3.x):
    /// <c>%LOCALAPPDATA%\Claude-Data\claude_ID\</c>, for the package <c>%LOCALAPPDATA%\Packages\Claude_ID</c>. It has
    /// moved when that folder is there, or when the package's old folder holds Claude's marker
    /// <c>.user-data-left.claude_ID</c>; a package never opened yet gets it there too, where it will look;</item>
    /// <item>the Store build before that move: its package's own copy of AppData, <c>Packages\Claude_ID\LocalCache\Roaming\Claude\</c>
    /// (Windows shows it the app instead of <c>%APPDATA%\Claude</c>);</item>
    /// <item>the installer's build (no package): <c>%APPDATA%\Claude\</c>.</item>
    /// </list>
    /// Every other one of those that exists is in <see cref="DesktopPlace.Others"/>: one Syft Notes wrote to before is
    /// cleaned of its entry, and none counts as connected.
    /// </summary>
    public static DesktopPlace DesktopPlaceIn(string userHome, string localAppData, string appData, bool windows, bool mac)
    {
        if (!windows)
            return new(mac ? Path.Combine(userHome, "Library", "Application Support", "Claude", DesktopFile) : Path.Combine(userHome, ".config", "Claude", DesktopFile), []);
        string roaming = Path.Combine(appData, "Claude", DesktopFile);
        var packages = Folders(Path.Combine(localAppData, "Packages"), "Claude_*");
        var moved = Folders(Path.Combine(localAppData, "Claude-Data"), "claude_*");
        string? config = null;
        foreach (string package in packages)
        {
            string id = Path.GetFileName(package)["Claude_".Length..];
            string old = Path.Combine(package, "LocalCache", "Roaming", "Claude");
            string? data = moved.FirstOrDefault(m => string.Equals(Path.GetFileName(m), "claude_" + id, StringComparison.OrdinalIgnoreCase));
            bool left = Directory.Exists(old) && Files(old, ".user-data-left.*").Count > 0;
            if (data is not null || left || !Directory.Exists(old))
                config = Path.Combine(data ?? Path.Combine(localAppData, "Claude-Data", "claude_" + id.ToLowerInvariant()), DesktopFile);
            else config = Path.Combine(old, DesktopFile);
            break;
        }
        // No Store build: the installer's, unless only the Store build's moved data is here (and no installer's build).
        config ??= moved.Count > 0 && !Directory.Exists(Path.Combine(localAppData, "AnthropicClaude")) && !File.Exists(roaming)
            ? Path.Combine(moved[0], DesktopFile) : roaming;
        var others = new List<string> { roaming };
        others.AddRange(packages.Select(p => Path.Combine(p, "LocalCache", "Roaming", "Claude", DesktopFile)));
        others.AddRange(moved.Select(m => Path.Combine(m, DesktopFile)));
        return new(config, [.. others.Where(o => !string.Equals(o, config, StringComparison.OrdinalIgnoreCase)).Distinct(StringComparer.OrdinalIgnoreCase)]);
    }

    /// <summary>The folders in <paramref name="dir"/> named like <paramref name="pattern"/> (case doesn't matter on
    /// Windows), sorted; none when it can't be read.</summary>
    static List<string> Folders(string dir, string pattern)
    {
        try
        {
            return Directory.Exists(dir)
                ? [.. Directory.EnumerateDirectories(dir, pattern, new EnumerationOptions { MatchCasing = MatchCasing.CaseInsensitive }).Order(StringComparer.Ordinal)]
                : [];
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    static List<string> Files(string dir, string pattern)
    {
        try
        {
            return [.. Directory.EnumerateFiles(dir, pattern, new EnumerationOptions { MatchCasing = MatchCasing.CaseInsensitive, AttributesToSkip = 0 })];
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    bool Installed(string name) => (AppInstalled ?? DesktopAppInstalled)(name);

    bool DesktopAppInstalled(string name)
    {
        if (name == "ChatGPT") return ChatGptAppInstalled();
        if (Mac) return MacApps(name).Any(Directory.Exists);
        // Claude on Windows: the Store build (its package, or the data it moved out of it), or the installer's.
        if (Windows && name == "Claude")
            return Folders(Path.Combine(LocalAppData, "Packages"), "Claude_*").Count > 0 || Folders(Path.Combine(LocalAppData, "Claude-Data"), "claude_*").Count > 0
                || Directory.Exists(Path.Combine(LocalAppData, "AnthropicClaude"));
        return false;
    }

    static string[] MacApps(string name) =>
        [$"/Applications/{name}.app", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Applications", name + ".app")];

    /// <summary>The ChatGPT app that reads Codex's settings: the one OpenAI merged with Codex in July 2026 (it's
    /// Codex's own app underneath, whichever of the two names it has). ChatGPT Classic, the app from before, has no
    /// MCP servers on this computer, so it doesn't count.</summary>
    bool ChatGptAppInstalled()
    {
        try
        {
            if (Mac)
                return new[] { "ChatGPT", "Codex" }.SelectMany(MacApps).Any(app => IsCodexBundle(Path.Combine(app, "Contents", "Info.plist")));
            // The Microsoft Store's "ChatGPT" is the OpenAI.Codex package.
            if (Windows) return Folders(Path.Combine(LocalAppData, "Packages"), "OpenAI.Codex_*").Count > 0;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
        return false;
    }

    /// <summary>Whether an app's Info.plist names Codex's bundle (com.openai.codex): true of the merged ChatGPT app,
    /// not of ChatGPT Classic (com.openai.chat). The name is in the file as plain text whether the plist is XML or binary.</summary>
    public static bool IsCodexBundle(string infoPlist) =>
        File.Exists(infoPlist) && File.ReadAllBytes(infoPlist).AsSpan().IndexOf("com.openai.codex"u8) >= 0;

    /// <summary>Whether a desktop app is open now (<see cref="Platform.DesktopApps.IsRunning"/>).</summary>
    bool IsRunning(string id) => Running is { } running ? running(id) : DesktopApp(id) is not null && Platform.DesktopApps.This.IsRunning(id);

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

    /// <summary>Codex's block for Syft Notes, under its own heading in config.toml.</summary>
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
        "antigravity" => AntigravityConfig,
        _ => throw new ArgumentException($"no AI app called {id}", nameof(id)),
    };

    bool IsInstalled(string id) => id switch
    {
        "claude-desktop" => Installed("Claude") || Directory.Exists(Path.GetDirectoryName(DesktopConfig)),
        "claude-code" => Find("claude") is not null || File.Exists(ClaudeCodeConfig),
        "codex" => Find("codex") is not null || Installed("ChatGPT") || Directory.Exists(Path.GetDirectoryName(CodexConfig)),
        // Antigravity keeps its own things under ~/.gemini too, so only Gemini CLI's own command, or its settings file, says it's here.
        "gemini" => Find("gemini") is not null || File.Exists(GeminiConfig),
        "antigravity" => Find(StudyStash.Core.Ai.Antigravity.Binary) is not null,
        _ => false,
    };

    bool SameProgram(string? command) => command is not null
        && string.Equals(command, Program, Windows || Mac ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    /// <summary>Every app, read fresh from its own settings file (read only: nothing is changed).</summary>
    public IReadOnlyList<AiAppState> States() => [.. Apps.Select(a => State(a.Id))];

    /// <summary>One AI's apps, read fresh (read only).</summary>
    public IReadOnlyList<AiAppState> StatesOf(string provider) => [.. AppsOf(provider).Select(State)];

    /// <summary>Syft Notes' entry in an app's settings text: its command and arguments, and whether it's under the old
    /// name; null when there's none.</summary>
    static (string? Command, List<string?> Args, bool OldName)? EntryIn(string id, string text)
    {
        if (id == "codex")
        {
            if (ServersOf(TomlOf(text)) is not { } servers) return null;
            foreach (string name in (string[])[ClaudeTools.ServerName, .. ClaudeTools.OldServerNames])
                if (servers.TryGetValue(name, out var s) && s is TomlTable t)
                {
                    string? command = t.TryGetValue("command", out var c) ? c as string : null;
                    List<string?> args = t.TryGetValue("args", out var a) && a is TomlArray list ? [.. list.Select(x => x as string)] : [];
                    return (command, args, name != ClaudeTools.ServerName);
                }
            return null;
        }
        if (JsonOf(text, lenient: true)?["mcpServers"] is not JsonObject json) return null;
        foreach (string name in (string[])[ClaudeTools.ServerName, .. ClaudeTools.OldServerNames])
            if (json[name] is JsonObject entry)
            {
                string? command = entry["command"] is JsonValue v && v.TryGetValue(out string? c) ? c : null;
                List<string?> args = entry["args"] is JsonArray list ? [.. list.Select(x => x is JsonValue av && av.TryGetValue(out string? arg) ? arg : null)] : [];
                return (command, args, name != ClaudeTools.ServerName);
            }
        return null;
    }

    static string? ReadText(string path)
    {
        try
        {
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>The places this app doesn't read that may hold a copy of Syft Notes' entry (Claude Desktop's older folders).</summary>
    IReadOnlyList<string> OthersOf(string id) => id == "claude-desktop" ? Desktop.Others : [];

    /// <summary>The program Syft Notes' entry in this app's settings starts, or null when there's none.</summary>
    public string? CommandOf(string id) => ReadText(ConfigOf(id)) is { } text ? EntryIn(id, text)?.Command : null;

    public AiAppState State(string id)
    {
        string path = ConfigOf(id);
        var entry = ReadText(path) is { } text ? EntryIn(id, text) : null;
        bool added = entry is not null;
        bool ours = added && SameProgram(entry!.Value.Command);
        bool oldName = entry?.OldName == true;
        bool stale = OthersOf(id).Any(o => ReadText(o) is { } t && EntryIn(id, t) is not null);
        bool outdated = ours && (!entry!.Value.Args.SequenceEqual(ArgsFor(id)) || oldName);
        return new AiAppState(id, NameOf(id), IsInstalled(id) || added, added, added && !ours,
            ToLocal(McpClients.When(McpClients.AddedFile(Home, id))), ToLocal(McpClients.When(McpClients.StartedFile(Home, id))), path,
            Outdated: outdated || stale && ours,
            CanReopen: CanClose && DesktopApp(id) is { } desktop && Installed(desktop.Name))
        {
            OldName = oldName, Stale = stale, Running = DesktopApp(id) is not null && IsRunning(id), Words = WordsFor(id),
            Opened = opened.TryGetValue(path, out var at) ? at : null,
        };
    }

    static DateTime? ToLocal(DateTime? utc) => utc?.ToLocalTime();

    /// <summary>Adds Syft Notes to this app's settings, or puts this copy of it in place of another (or of its old
    /// name), and takes any copy out of the places the app no longer reads.</summary>
    public AiAppChange Connect(string id) => Change(id, add: true);

    /// <summary>Takes Syft Notes out of this app's settings (and the places it no longer reads), leaving everything else.</summary>
    public AiAppChange Disconnect(string id) => Change(id, add: false);

    /// <summary>
    /// Checks one AI's apps the way the apps will: starts Syft Notes' server exactly as the first good entry's settings
    /// say (its command and arguments), and asks it for its tools, as an app does when it loads it. That start leaves no
    /// "started" note of its own (<see cref="McpClients.ProbeEnv"/>). Null when there's nothing to check, or no way to.
    /// </summary>
    public async Task<McpProbeResult?> CheckAsync(string provider)
    {
        if (Probe is null) return null;
        foreach (string id in AppsOf(provider))
            if (State(id) is { Good: true } && ReadText(ConfigOf(id)) is { } text && EntryIn(id, text) is { Command: { } command } entry)
                return await Probe(command, [.. entry.Args.Select(a => a ?? "")]);
        return null;
    }

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
        // Claude Desktop's older folders (on Windows): a copy of the entry there is never read, and would only mislead.
        foreach (string other in OthersOf(id)) Forget(id, other);
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

    AiAppChange ChangeJson(string id, string name, string path, bool add, string written)
    {
        string? before = File.Exists(path) ? File.ReadAllText(path) : null;
        JsonObject root;
        if (string.IsNullOrWhiteSpace(before)) root = [];
        else if (JsonOf(before, lenient: false) is { } strict) root = strict;
        else
        {
            string byHand = add ? "Add the setup below to it by hand." : $"Take {ClaudeTools.ServerName} out of its mcpServers by hand.";
            return new(false, JsonOf(before, lenient: true) is not null
                ? $"{name}'s settings file has comments in it, which rewriting it would lose, so it's left alone. {byHand}"
                : $"{name}'s settings file isn't valid JSON, so it's left alone. Fix it, or {char.ToLowerInvariant(byHand[0])}{byHand[1..]}", path, null, add ? written : null);
        }
        if (root["mcpServers"] is { } existing && existing is not JsonObject)
            return new(false, $"{name}'s settings have mcpServers in a shape Syft Notes doesn't know, so they're left alone.", path, null, add ? written : null);
        var servers = root["mcpServers"] as JsonObject;
        // Its old name goes either way: an app never has both.
        int removed = servers is null ? 0 : ClaudeTools.OldServerNames.Count(servers.Remove);
        if (add)
        {
            if (servers is null) root["mcpServers"] = servers = [];
            servers[ClaudeTools.ServerName] = EntryFor(id);
        }
        else if (servers is null || !servers.Remove(ClaudeTools.ServerName) && removed == 0)
            return new(true, $"{name} doesn't have Syft Notes.", path);
        else if (servers.Count == 0) root.Remove("mcpServers"); // the file goes back to how it was before Syft Notes

        string after = root.ToJsonString(Indented);
        if (before is not null && before.Contains("\r\n", StringComparison.Ordinal)) after = after.ReplaceLineEndings("\r\n");
        if (before is null || before.EndsWith('\n')) after += before?.Contains("\r\n", StringComparison.Ordinal) == true ? "\r\n" : "\n";
        // Already just so (Connect pressed again): nothing is written, and no backup made of it.
        string? backup = after == before ? null : Save(path, before, after);
        return add
            ? new(true, $"Added Syft Notes to {name}.", path, backup, written)
            : new(true, $"Took Syft Notes out of {name}. Its other settings are as they were.", path, backup);
    }

    // A heading for Syft Notes' own table in config.toml, or one of its sub-tables ([mcp_servers.syft-notes.env]), under
    // its name or its old one.
    static readonly string OurNames = string.Join("|", ((string[])[ClaudeTools.ServerName, .. ClaudeTools.OldServerNames]).Select(Regex.Escape));
    static readonly Regex OurHeading = new($@"^\s*\[\s*mcp_servers\s*\.\s*(?:(?:{OurNames})|""(?:{OurNames})""|'(?:{OurNames})')\s*(?:\.[^\]]*)?\]\s*(?:#.*)?$");
    static readonly Regex AnyHeading = new(@"^\s*\[");

    AiAppChange ChangeToml(string name, string path, bool add, string written)
    {
        string before = File.Exists(path) ? File.ReadAllText(path) : "";
        string Manual(string why) => why + (add ? " Add the setup below to it by hand." : $" Take [mcp_servers.{ClaudeTools.ServerName}] out of it by hand.");
        if (before.Trim().Length > 0 && TomlOf(before) is null)
            return new(false, Manual($"{name}'s config.toml isn't valid TOML, so it's left alone."), path, null, add ? written : null);
        string nl = before.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var (rest, had) = WithoutOurTables(before, nl);
        var left = TomlOf(rest);
        if (left is null || ServersOf(left)?.Keys.Any(ClaudeTools.IsOurServer) == true)
            return new(false, Manual($"{name}'s config.toml has Syft Notes set up by hand in a way Syft Notes won't rewrite, so it's left alone."), path, null, add ? written : null);
        string after;
        if (add) after = (rest.Trim().Length > 0 ? rest.TrimEnd('\r', '\n') + nl + nl : "") + written.ReplaceLineEndings(nl) + nl;
        else if (!had) return new(true, $"{name} doesn't have Syft Notes.", path);
        else after = rest;
        // What ChatGPT and Codex will read has Syft Notes exactly when it should, or nothing is written.
        if (TomlOf(after) is not { } check || (ServersOf(check)?.ContainsKey(ClaudeTools.ServerName) == true) != add
            || ServersOf(check)?.Keys.Any(k => ClaudeTools.OldServerNames.Contains(k)) == true)
            return new(false, Manual($"Couldn't make a change to {name}'s config.toml that it would read, so it's left alone."), path, null, add ? written : null);
        string? backup = after == before ? null : Save(path, File.Exists(path) ? before : null, after);
        return add
            ? new(true, $"Added Syft Notes to {name}.", path, backup, written)
            : new(true, $"Took Syft Notes out of {name}. Its other settings are as they were.", path, backup);
    }

    /// <summary>Takes Syft Notes' entry (under either name) out of a settings file the app no longer reads, keeping the
    /// rest and a backup. Quietly nothing when the file isn't there, has none, or can't be read safely.</summary>
    void Forget(string id, string path)
    {
        if (!CanWrite || ReadText(path) is not { } text || EntryIn(id, text) is null || JsonOf(text, lenient: false) is not { } root) return;
        if (root["mcpServers"] is not JsonObject servers) return;
        foreach (string name in (string[])[ClaudeTools.ServerName, .. ClaudeTools.OldServerNames]) servers.Remove(name);
        if (servers.Count == 0) root.Remove("mcpServers");
        string after = root.ToJsonString(Indented);
        if (text.Contains("\r\n", StringComparison.Ordinal)) after = after.ReplaceLineEndings("\r\n");
        if (text.EndsWith('\n')) after += text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        try
        {
            Save(path, text, after);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>config.toml without Syft Notes' own tables (and the blank lines that led up to them), and whether it
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

    /// <summary>config.toml's MCP servers, or null when it has none (asking a table for a key it doesn't have throws).</summary>
    static TomlTable? ServersOf(TomlTable? config) => config is not null && config.TryGetValue("mcp_servers", out var servers) ? servers as TomlTable : null;

    static TomlTable? TomlOf(string text)
    {
        try
        {
            return TomlSerializer.Deserialize<TomlTable>(text);
        }
        catch (TomlException)
        {
            return null;
        }
#pragma warning disable CA1031 // whatever else the parser throws, the answer is the same: not TOML Syft Notes can rewrite
        catch (Exception e)
#pragma warning restore CA1031
        {
            StudyStash.App.Program.Log($"[claude-setup] config.toml wouldn't parse: {e.GetType().Name}");
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
    /// <c>NAME.syft-notes-backup</c> beside it. A link (a dotfiles setup) is followed, so the file it points to is
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
            backup = target + ".syft-notes-backup";
            File.Copy(target, backup, overwrite: true);
        }
        string tmp = target + ".syft-notes-new";
        File.WriteAllText(tmp, after, new UTF8Encoding(false));
        if (!OperatingSystem.IsWindows() && File.Exists(target)) File.SetUnixFileMode(tmp, File.GetUnixFileMode(target));
        File.Move(tmp, target, overwrite: true);
        return backup;
    }
}
