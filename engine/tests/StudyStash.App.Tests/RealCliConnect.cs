using System.Text.Json.Nodes;
using StudyStash.App.Services;
using StudyStash.Core;
using StudyStash.Core.Ai;

namespace StudyStash.App.Tests;

/// <summary>
/// The second half of CI's real-CLI job: once each real CLI has been installed and its hidden sign-in has reached the
/// browser, Connect's own step adds Syft Notes to that AI's apps in the run's home (<see cref="ClaudeSetup"/>, exactly as
/// the app does), and then the real CLIs are asked what they see (<c>claude mcp list</c>, <c>codex mcp list</c>,
/// <c>agy mcp list</c> where it has one), and the server each app's settings name is started exactly as written there
/// and asked for its tools (<see cref="McpProbe"/>: initialize, then tools/list). The Claude app can't be installed on a
/// runner, so its settings are a copy of each computer's real layout (Windows: the Store build after it moved its data,
/// with an earlier Syft Notes' entry left in the old folder). Only yes and no are said.
/// </summary>
public sealed partial class RealCliSignInTests
{
    /// <summary>The app as the tests built it: the same program Settings writes into each app's settings.</summary>
    static string AppProgram => Path.Combine(AppContext.BaseDirectory, OperatingSystem.IsWindows() ? "StudyStash.exe" : "StudyStash");

    static async Task<Result> ConnectAppsAsync(AgentCliInfo cli, string root, string home)
    {
        string name = cli.Brand + "'s apps";
        if (AgentInstall.Find(cli) is not { Works: true } found) return new Result(name, "its CLI isn't here, skipped");
        // A Syft Notes home of the run's own, set up the way the app leaves one (the library's address and password: a
        // dead address here, which listing the tools never needs).
        string syft = Path.Combine(root, "syft home");
        Configs.SaveClient(new ClientConfig(syft) { ServerUrl = "http://127.0.0.1:9", PoolKey = "ci-only" });
        string local = Path.Combine(home, "AppData", "Local"), roaming = Path.Combine(home, "AppData", "Roaming");
        var fixtures = cli.Id == "claude" ? ClaudeAppFixture(home, local) : null;
        var setup = new ClaudeSetup
        {
            Program = AppProgram, Home = syft, UserHome = home, LocalAppData = local, AppData = roaming,
            AppInstalled = n => n == "Claude", Running = _ => false, QuitApp = null, OpenApp = null,
        };
        var parts = new List<string>();
        bool failed = false;
        foreach (string id in ClaudeSetup.AppsOf(cli.Id))
        {
            var state = setup.State(id);
            if (!state.Installed) continue;
            var done = setup.Connect(id);
            var after = setup.State(id);
            parts.Add($"{state.Words} connected: {Yes(done.Ok && after.Good)}");
            failed |= !(done.Ok && after.Good);
            // The server, started exactly as that app's settings say, answers initialize and lists its tools.
            var (command, args) = CommandIn(id, File.ReadAllText(after.ConfigPath));
            var tools = await McpProbe.ToolsAsync(command, args, timeout: TimeSpan.FromSeconds(60));
            bool listed = tools.Ok && tools.Tools.Contains("search_notes");
            parts.Add($"{state.Words}'s server started and listed its tools: {Yes(listed)}");
            failed |= !listed;
        }
        if (fixtures is { } fx) parts.Add(fx.Check(setup, ref failed));

        // What the real CLI sees in its own settings.
        var (args0, must) = cli.Id switch
        {
            "claude" => (new[] { "mcp", "list" }, true),
            "codex" => (new[] { "mcp", "list" }, true),
            _ => (new[] { "mcp", "list" }, false), // agy: only where it has the command
        };
        var listedByCli = Machine.Run(found.Exe!, args0, TimeSpan.FromSeconds(120), cli.Id == "gemini" ? null : cli.Env());
        bool sees = listedByCli is { ExitCode: 0 } r && r.Stdout.Contains(ClaudeTools.ServerName, StringComparison.Ordinal);
        string what = $"{cli.Binary} mcp list sees {ClaudeTools.ServerName}";
        if (listedByCli is not { ExitCode: 0 } && !must) parts.Add($"{what}: no such command here");
        else
        {
            parts.Add($"{what}: {Yes(sees)}");
            string listed = listedByCli?.Stdout ?? "";
            failed |= !sees;
            // Claude Code starts each server to say whether it's connected.
            if (cli.Id == "claude") parts.Add($"claude says it's connected: {Yes(listed.Contains("Connected", StringComparison.OrdinalIgnoreCase))}");
        }
        return new Result(name, string.Join("; ", parts)) { Failed = failed };
    }

    /// <summary>The command and arguments Syft Notes' entry in an app's settings holds, read as the app reads them.</summary>
    static (string Command, IReadOnlyList<string> Args) CommandIn(string id, string text)
    {
        if (id == "codex")
        {
            var table = Tomlyn.TomlSerializer.Deserialize<Tomlyn.Model.TomlTable>(text)!;
            var server = (Tomlyn.Model.TomlTable)((Tomlyn.Model.TomlTable)table["mcp_servers"])[ClaudeTools.ServerName];
            return ((string)server["command"], [.. ((Tomlyn.Model.TomlArray)server["args"]).Select(a => (string)a!)]);
        }
        var entry = JsonNode.Parse(text)!["mcpServers"]![ClaudeTools.ServerName]!;
        return (entry["command"]!.GetValue<string>(), [.. entry["args"]!.AsArray().Select(a => a!.GetValue<string>())]);
    }

    /// <summary>The Claude app's settings as each computer really lays them out, with keys of Claude's own that must
    /// survive, and on Windows an earlier Syft Notes' entry in the folder the Store build left.</summary>
    sealed record AppFixture(string Config, string? Old)
    {
        public string Check(ClaudeSetup setup, ref bool failed)
        {
            bool right = Path.GetFullPath(setup.DesktopConfig) == Path.GetFullPath(Config);
            var json = File.Exists(Config) ? JsonNode.Parse(File.ReadAllText(Config)) : null;
            bool kept = json?["preferences"]?["menuBarEnabled"] is not null;
            bool cleaned = Old is null || JsonNode.Parse(File.ReadAllText(Old))?["mcpServers"] is null;
            failed |= !(right && kept && cleaned);
            return $"Claude app's settings where it reads them: {Yes(right)}, its own keys kept: {Yes(kept)}"
                + (Old is null ? "" : $", the entry left in its old folder taken out: {Yes(cleaned)}");
        }
    }

    static AppFixture ClaudeAppFixture(string home, string local)
    {
        const string own = """{"preferences": {"menuBarEnabled": true}}""";
        if (!OperatingSystem.IsWindows())
        {
            string config = OperatingSystem.IsMacOS()
                ? Path.Combine(home, "Library", "Application Support", "Claude", "claude_desktop_config.json")
                : Path.Combine(home, ".config", "Claude", "claude_desktop_config.json");
            Directory.CreateDirectory(Path.GetDirectoryName(config)!);
            File.WriteAllText(config, own);
            return new AppFixture(config, null);
        }
        string old = Path.Combine(local, "Packages", "Claude_pzs8sxrjxfjjc", "LocalCache", "Roaming", "Claude");
        Directory.CreateDirectory(old);
        File.WriteAllText(Path.Combine(old, ".user-data-left.claude_pzs8sxrjxfjjc"), "");
        File.WriteAllText(Path.Combine(old, "claude_desktop_config.json"),
            new JsonObject { ["mcpServers"] = new JsonObject { ["study-stash"] = new JsonObject { ["command"] = AppProgram, ["args"] = new JsonArray("mcp") } } }.ToJsonString());
        string moved = Path.Combine(local, "Claude-Data", "claude_pzs8sxrjxfjjc", "claude_desktop_config.json");
        Directory.CreateDirectory(Path.GetDirectoryName(moved)!);
        File.WriteAllText(moved, own);
        return new AppFixture(moved, Path.Combine(old, "claude_desktop_config.json"));
    }
}
