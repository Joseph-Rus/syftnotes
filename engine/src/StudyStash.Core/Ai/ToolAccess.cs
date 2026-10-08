using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace StudyStash.Core.Ai;

/// <summary>
/// Which of a student's reading toggles (<see cref="ReadingScopes"/>) a tool needs, and the guard that refuses a
/// call when AI tool access is off, or the toggle its kind needs is off. Refusing happens at call time, not by
/// hiding the tool from <c>tools/list</c>, so a client's tool list never changes underneath it, and the refusal is
/// a tool error in words: it reaches the model and the student, where a refused connection would only say
/// "couldn't connect".
/// </summary>
public static class ToolAccess
{
    /// <summary>What every tool says while Settings → AI apps has reading switched off.</summary>
    public const string Off = "Reading is switched off for AI apps in Study Stash. The student can turn it on in Study Stash → Settings → AI apps.";

    /// <summary>Each scope as its toggle is named in Settings → AI apps, and what it lets tools read.</summary>
    static readonly Dictionary<string, (string Toggle, string What)> Toggles = new()
    {
        ["lectures"] = ("Lectures and transcripts", "lectures and transcripts"),
        ["notes"] = ("Study notes", "study notes"),
        ["canvas"] = ("Canvas assignments and files", "Canvas"),
    };

    /// <summary>What a tool says when the toggle its scope needs is off, naming that toggle.</summary>
    public static string Refused(string scope) => Toggles.TryGetValue(scope, out var t)
        ? $"Study Stash's settings don't let AI tools read {t.What} right now (Settings → AI apps → {t.Toggle})."
        : "Study Stash's settings don't let AI tools read that right now (Settings → AI apps).";

    /// <summary>The scope a tool needs, or null only for list_classes (just the class names and counts). Every other
    /// tool is named here on purpose: a test fails when a new tool isn't.</summary>
    public static string? Scope(string tool) => tool switch
    {
        "list_lectures" or "search_notes" or "get_transcript" => "lectures",
        "get_lecture" or "list_attachments" or "read_attachment" => "notes",
        "search_files" or "read_file" or "due_assignments" or "get_assignment" or "class_modules" or "class_files" or "class_announcements"
            or "canvas_courses" or "canvas_api" or "canvas_page" or "canvas_download" => "canvas",
        _ => null,
    };

    static bool Allowed(string? scope, ReadingScopes reading) => scope switch
    {
        "lectures" => reading.Lectures,
        "notes" => reading.Notes,
        "canvas" => reading.Canvas,
        _ => true,
    };

    sealed class Guarded(McpServerTool inner, Func<Task<(bool On, ReadingScopes Reading)>> access) : DelegatingMcpServerTool(inner)
    {
        public override async ValueTask<CallToolResult> InvokeAsync(RequestContext<CallToolRequestParams> request, CancellationToken cancellationToken = default)
        {
            var (on, reading) = await access();
            string? scope = Scope(ProtocolTool.Name);
            string? refusal = !on ? Off : !Allowed(scope, reading) ? Refused(scope!) : null;
            return refusal is not null
                ? new CallToolResult { IsError = true, Content = [new TextContentBlock { Text = refusal }] }
                : await base.InvokeAsync(request, cancellationToken);
        }
    }

    /// <summary>Each tool, refusing at call time instead of running it, once <paramref name="access"/> says access
    /// is off or the scope it needs is off. Called fresh for every call, so a change (or the off switch) takes
    /// effect at once, without reconnecting.</summary>
    public static List<McpServerTool> Guard(IEnumerable<McpServerTool> tools, Func<Task<(bool On, ReadingScopes Reading)>> access) =>
        [.. tools.Select(t => (McpServerTool)new Guarded(t, access))];
}
