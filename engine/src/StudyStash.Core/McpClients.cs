namespace StudyStash.Core;

/// <summary>
/// The AI apps on this computer that Settings connects to the library (Claude Desktop, Claude Code, ChatGPT with
/// Codex, Gemini CLI).
/// Each starts Study Stash's own MCP server over stdio (<c>mcp --client ID</c>), which reads the library on this
/// computer with the password the app already keeps, so nothing secret goes into the AI app's settings and no
/// network address is involved. The server leaves a note when an app starts it: that's how Settings tells
/// "connected" from "added, but the app hasn't loaded it yet".
/// </summary>
public static class McpClients
{
    public static readonly IReadOnlyList<string> Ids = ["claude-desktop", "claude-code", "codex", "gemini"];

    /// <summary>The option that names which app started the server.</summary>
    public const string Option = "--client";

    static string Dir(string home) => Path.Combine(home, "ai-apps");

    /// <summary>Touched by the MCP server each time this app starts it.</summary>
    public static string StartedFile(string home, string id) => Path.Combine(Dir(home), id + ".started");

    /// <summary>Touched by Settings when it adds Study Stash to this app, removed when it takes it out.</summary>
    public static string AddedFile(string home, string id) => Path.Combine(Dir(home), id + ".added");

    public static DateTime? When(string path) => File.Exists(path) ? File.GetLastWriteTimeUtc(path) : null;

    public static void Touch(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "");
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow);
    }

    /// <summary>The server was started by <paramref name="id"/> (one of <see cref="Ids"/>; anything else is ignored).
    /// Never fails the server: a note that can't be written only leaves Settings saying "restart it".</summary>
    public static void Started(string home, string? id)
    {
        if (id is null || !Ids.Contains(id)) return;
        try
        {
            Touch(StartedFile(home, id));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }
}
