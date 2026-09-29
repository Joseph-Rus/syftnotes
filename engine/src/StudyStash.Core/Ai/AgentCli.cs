using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace StudyStash.Core.Ai;

/// <summary>An AI's official installer, as its maker's docs give it: the program that runs it, that program's
/// arguments, the one line the docs print (shown under Details), and the docs page it comes from. Nothing a student
/// types ever goes into it.</summary>
public sealed record AgentInstaller(string Program, IReadOnlyList<string> Args, string Line, string DocUrl)
{
    /// <summary>Set for the installer only: Codex's skips its "Start Codex now?" question with it.</summary>
    public IReadOnlyDictionary<string, string> Env { get; init; } = new Dictionary<string, string>();
}

/// <summary>Whether a CLI is signed in, as its own status command says: the exit code, and for Claude Code how
/// (<c>authMethod</c>) and on which plan (<c>subscriptionType</c>, often empty). Its email and organisation are never
/// read.</summary>
public sealed record AgentSignedIn(bool SignedIn, string AuthMethod = "", string Plan = "");

/// <summary>
/// One AI a student can set Study Stash up with: Claude (through Claude Code) or ChatGPT (through Codex). Everything
/// guided setup needs to know about it, in one place: what it's called, which plan it needs, its maker's documented
/// installer for each computer, and the commands that sign in and say whether it's signed in.
/// </summary>
public sealed record AgentCliInfo
{
    /// <summary>"claude" or "codex": the same id the library's AI settings use.</summary>
    public required string Id { get; init; }
    /// <summary>The command-line tool: "Claude Code", "Codex".</summary>
    public required string Name { get; init; }
    /// <summary>The AI as students know it: "Claude", "ChatGPT".</summary>
    public required string Brand { get; init; }
    public required string Maker { get; init; }
    public required string Binary { get; init; }
    /// <summary>Where its installer comes from ("claude.ai"), for "Study Stash couldn't reach claude.ai".</summary>
    public required string Site { get; init; }
    /// <summary>The plans that include it, as the Pick screen says it: "Pro or Max", "Plus or higher".</summary>
    public required string Plans { get; init; }
    /// <summary>The plan the Pick screen's link is about: "Claude Pro", "ChatGPT Plus".</summary>
    public required string PlanName { get; init; }
    public required string PlansUrl { get; init; }
    public required AgentInstaller MacInstaller { get; init; }
    public required AgentInstaller WindowsInstaller { get; init; }
    /// <summary>Signs in (it opens the browser itself): `claude auth login`, `codex login`.</summary>
    public required IReadOnlyList<string> SignInArgs { get; init; }
    /// <summary>Says whether it's signed in, by its exit code: `claude auth status`, `codex login status`.</summary>
    public required IReadOnlyList<string> StatusArgs { get; init; }

    public AgentInstaller Installer(bool windows) => windows ? WindowsInstaller : MacInstaller;
    public string SignInLine => string.Join(' ', [Binary, .. SignInArgs]);
}

/// <summary>
/// The two AIs guided setup offers, and how to read what their commands print. The installers are the providers'
/// own one-liners, exactly as their docs write them (per-user, no administrator): <c>set -o pipefail</c> in front on
/// a Mac only makes a download that failed fail the whole line, instead of piping nothing into the shell.
/// </summary>
public static partial class AgentCli
{
    const string PowerShell = "powershell.exe";
    static readonly string[] PowerShellArgs = ["-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-Command"];

    static AgentInstaller Mac(string line, string doc) => new("/bin/bash", ["-c", "set -o pipefail; " + line], line, doc);
    static AgentInstaller Windows(string line, string doc) => new(PowerShell, [.. PowerShellArgs, line], line, doc);

    const string ClaudeDocs = "https://code.claude.com/docs/en/setup";
    const string CodexDocs = "https://learn.chatgpt.com/docs/codex/cli";
    static readonly Dictionary<string, string> CodexQuiet = new() { ["CODEX_NON_INTERACTIVE"] = "1" };

    public static readonly AgentCliInfo Claude = new()
    {
        Id = "claude", Name = "Claude Code", Brand = "Claude", Maker = "Anthropic", Binary = "claude", Site = "claude.ai",
        Plans = "Pro or Max", PlanName = "Claude Pro", PlansUrl = "https://claude.com/pricing",
        MacInstaller = Mac("curl -fsSL https://claude.ai/install.sh | bash", ClaudeDocs),
        WindowsInstaller = Windows("irm https://claude.ai/install.ps1 | iex", ClaudeDocs),
        SignInArgs = ["auth", "login"],
        StatusArgs = ["auth", "status"],
    };

    public static readonly AgentCliInfo Codex = new()
    {
        Id = "codex", Name = "Codex", Brand = "ChatGPT", Maker = "OpenAI", Binary = "codex", Site = "chatgpt.com",
        Plans = "Plus or higher", PlanName = "ChatGPT Plus", PlansUrl = "https://chatgpt.com/pricing",
        MacInstaller = Mac("curl -fsSL https://chatgpt.com/codex/install.sh | sh", CodexDocs) with { Env = CodexQuiet },
        WindowsInstaller = Windows("irm https://chatgpt.com/codex/install.ps1 | iex", CodexDocs) with { Env = CodexQuiet },
        SignInArgs = ["login"],
        StatusArgs = ["login", "status"],
    };

    public static readonly IReadOnlyList<AgentCliInfo> All = [Claude, Codex];

    public static AgentCliInfo Get(string id) => id == "codex" ? Codex : Claude;

    /// <summary>The only pages a guided-setup card may open, besides the school's own Canvas address and the
    /// computer's settings: the two plan pages and the two install docs.</summary>
    public static readonly IReadOnlyList<string> Pages = [Claude.PlansUrl, Codex.PlansUrl, ClaudeDocs, CodexDocs];

    // --- versions and the flags each one has -------------------------------------------------------------------

    /// <summary>Claude Code's <c>--restricted</c> (only managed settings and <c>--settings</c>, no command tools).</summary>
    public static readonly Version RestrictedSince = new(2, 1, 248);
    /// <summary>Claude Code's <c>--permission-prompts none</c>.</summary>
    public static readonly Version NoPromptsSince = new(2, 1, 259);

    [GeneratedRegex(@"(\d+)\.(\d+)\.(\d+)")]
    private static partial Regex VersionNumber();

    /// <summary>The version a CLI's <c>--version</c> printed ("2.1.211 (Claude Code)", "codex-cli 0.46.0"), or null.</summary>
    public static Version? ParseVersion(string printed) =>
        VersionNumber().Match(printed) is { Success: true } m && Version.TryParse($"{m.Groups[1].Value}.{m.Groups[2].Value}.{m.Groups[3].Value}", out var v) ? v : null;

    /// <summary>A newer flag is used only when the installed CLI has it: an unknown version leaves it out.</summary>
    public static bool Has(Version? installed, Version since) => installed is not null && installed >= since;

    // --- signed in? -----------------------------------------------------------------------------------------------

    /// <summary>What a status command's result means: signed in exactly when it exited 0. Claude Code's JSON may add
    /// how and on which plan (and may say <c>loggedIn: false</c>, which wins); nothing else in it is kept.</summary>
    public static AgentSignedIn ReadStatus(string id, ProcResult? result)
    {
        if (result is not { ExitCode: 0 }) return new AgentSignedIn(false);
        if (id != "claude") return new AgentSignedIn(true);
        JsonObject? json;
        try
        {
            json = JsonNode.Parse(result.Stdout) as JsonObject;
        }
        catch (JsonException)
        {
            json = null;
        }
        if (json is null) return new AgentSignedIn(true);
        if (json["loggedIn"] is JsonValue v && v.GetValueKind() == JsonValueKind.False) return new AgentSignedIn(false);
        return new AgentSignedIn(true, Text(json["authMethod"]), Text(json["subscriptionType"]));
    }

    static string Text(JsonNode? n) => n is JsonValue v && v.GetValueKind() == JsonValueKind.String ? v.GetValue<string>() : "";

    // --- reading what an installer printed ------------------------------------------------------------------------

    [GeneratedRegex(@"\x1B\[[0-9;?]*[ -/]*[@-~]")]
    private static partial Regex Ansi();

    /// <summary>A line as a person would read it: colour codes and stray carriage returns gone.</summary>
    public static string Plain(string line) => Ansi().Replace(line, "").Replace("\r", "").Trim();

    /// <summary>The installer's own words for where it's got to, mapped to setup's ("Setting it up…"); null for a line
    /// that says nothing new.</summary>
    public static string? PhaseOf(AgentCliInfo cli, string line)
    {
        string l = Plain(line);
        if (cli.Id == "claude")
            return l.StartsWith("Setting up Claude Code", StringComparison.Ordinal) ? "Setting it up…" : null;
        if (l.StartsWith("==> Downloading Codex", StringComparison.Ordinal)) return "Downloading Codex…";
        if (l.StartsWith("==> Installing standalone package", StringComparison.Ordinal) || l.StartsWith("==> PATH", StringComparison.Ordinal)
            || l.Contains("installed successfully", StringComparison.Ordinal))
            return "Setting it up…";
        return null;
    }

    [GeneratedRegex(@"could not resolve|couldn't resolve|failed to connect|couldn't connect|connection (refused|timed out|reset)|operation timed out|"
        + @"remote name could not be resolved|no such host is known|unable to connect to the remote server|network is unreachable|"
        + @"temporary failure in name resolution|name or service not known|nodename nor servname|\bENOTFOUND\b|\bECONNREFUSED\b|\bETIMEDOUT\b|"
        + @"internet connection|you appear to be offline|ssl connect error",
        RegexOptions.IgnoreCase)]
    private static partial Regex OfflineWords();

    [GeneratedRegex(@"not available in your (region|country)|supported-countries|unsupported (country|region)|isn't available in your (country|region)|"
        + @"not supported in your (country|region)|unsupported_country",
        RegexOptions.IgnoreCase)]
    private static partial Regex RegionWords();

    [GeneratedRegex(@"exit code 137|ran out of memory|out of memory|cannot allocate memory|OutOfMemory", RegexOptions.IgnoreCase)]
    private static partial Regex MemoryWords();

    /// <summary>curl's own exit codes for "couldn't reach it": no such host, no connection, timed out, TLS failed.</summary>
    static readonly int[] CurlOffline = [6, 7, 28, 35];

    /// <summary>Why an installer failed, from how it ended and what it printed: the network first (nothing else could
    /// have been tried), then a region the maker doesn't serve, then memory, else something else.</summary>
    public static InstallFailure Classify(int exitCode, IEnumerable<string> printed)
    {
        string all = string.Join('\n', printed.Select(Plain));
        if (OfflineWords().IsMatch(all) || !OperatingSystem.IsWindows() && CurlOffline.Contains(exitCode)) return InstallFailure.Offline;
        if (RegionWords().IsMatch(all)) return InstallFailure.Region;
        if (exitCode == 137 || MemoryWords().IsMatch(all)) return InstallFailure.Memory;
        return InstallFailure.Other;
    }

    /// <summary>The last thing an installer said, for "The installer stopped: …" (no full stop of its own).</summary>
    public static string LastWords(IEnumerable<string> printed) =>
        printed.Select(Plain).LastOrDefault(l => l.Length > 0 && !l.StartsWith("At line:", StringComparison.Ordinal) && !l.StartsWith("+ ", StringComparison.Ordinal))
            ?.TrimEnd('.') is { } last ? Py.Head(last, 200) : "";
}

/// <summary>Why an install didn't work, in the kinds setup words differently.</summary>
public enum InstallFailure
{
    None,
    /// <summary>The maker's site couldn't be reached.</summary>
    Offline,
    /// <summary>The maker doesn't offer it in this country.</summary>
    Region,
    /// <summary>The computer ran out of memory while it installed.</summary>
    Memory,
    /// <summary>It installed, but its <c>--version</c> doesn't work.</summary>
    WontStart,
    /// <summary>It took longer than setup waits.</summary>
    TimedOut,
    Cancelled,
    /// <summary>Study Stash is running as an administrator, and a per-user install would go to the wrong account.</summary>
    Elevated,
    Other,
}
