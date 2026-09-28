using System.ComponentModel;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace StudyStash.Core;

/// <summary>Where Claude's tools read the library: in the library itself, or over its API from a laptop.</summary>
public interface ILibrarySource
{
    Task<JsonObject> OverviewAsync();
    Task<JsonArray> LecturesAsync(string? className, int limit, string? before);
    Task<JsonObject?> LectureAsync(string id);
    Task<JsonObject> SearchAsync(string query, string? className, int limit);

    /// <summary>Canvas, read through the Chrome extension: the linked courses ("courses"), a read ("fetch": url,
    /// kind, save_to), the assignments ("assignments": class, days), one assignment's whole detail ("assignment":
    /// class, id or name), a class's modules ("modules": class), its files ("files": class) or its announcements
    /// ("announcements": class, limit). Libraries without Canvas say so.</summary>
    bool HasCanvas => false;

    /// <summary>Files that aren't lectures, and readable folders' files, matching a search ([] where unsupported).</summary>
    Task<JsonArray> SearchFilesAsync(string query, int limit) => Task.FromResult(new JsonArray());

    Task<JsonNode> CanvasAsync(string what, JsonObject? body = null) =>
        Task.FromResult<JsonNode>(new JsonObject { ["error"] = "This library can't read Canvas." });

    /// <summary>Whether read_file can open files here: only in the library itself, where its file search is.</summary>
    bool CanReadFiles => false;

    /// <summary>The words in a file search found, from <paramref name="offset"/> on, or why not.</summary>
    Task<string> ReadFileAsync(string path, int offset) => Task.FromResult("This library can't open files.");
}

/// <summary>The library on this computer.</summary>
public sealed class LocalLibrary(LibraryReader reader, Canvas.CanvasSync? canvas = null, string? home = null, Ai.FileIndex? files = null) : ILibrarySource
{
    public Task<JsonArray> SearchFilesAsync(string query, int limit) => Task.FromResult(new JsonArray((files?.Search(query, limit) ?? [])
        .Select(h => (JsonNode)new JsonObject { ["root"] = h.Root, ["path"] = h.Path, ["title"] = h.Title, ["snippet"] = System.Net.WebUtility.HtmlDecode(h.Snippet.Replace("<mark>", "").Replace("</mark>", "")) }).ToArray()));

    public bool HasCanvas => canvas is not null;

    public bool CanReadFiles => files is not null;

    public Task<string> ReadFileAsync(string path, int offset) => Task.FromResult(ReadFile(path, offset));

    /// <summary>A file's words, only when it's one the file search holds, in a folder that isn't private, and it
    /// really is there (a link can't lead out of the folder, or into Study Stash's own settings).</summary>
    string ReadFile(string path, int offset)
    {
        const string NotHere = "That isn't a file Study Stash shares with AI tools. Use a path exactly as search_files, class_files or get_assignment gave it.";
        if (files is null) return "This library can't open files.";
        path = path.Trim();
        // "<class>/<path in its folder>": how class_files and get_assignment name what Study Stash saved.
        if (!Path.IsPathFullyQualified(path) && path.IndexOf('/') is > 0 and var slash)
            path = Path.GetFullPath(Path.Combine(reader.Store.PoolDir, Notes.Slugify(path[..slash], 60), path[(slash + 1)..]));
        if (path.Length == 0 || !Path.IsPathFullyQualified(path)) return NotHere;
        if (files.RootOf(path) is not { } root) return NotHere;
        if (root.Private) return "That file is in a private folder: AI tools see its name only.";
        string? real = RealPath(path), inside = RealPath(root.Path);
        if (real is null || inside is null || !File.Exists(real) || !real.StartsWith(inside.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            return NotHere;
        if (home is not null && RealPath(home) is { } h && real.StartsWith(h.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            return NotHere;
        return ClaudeTools.Page(Ai.FileIndex.TextOf(real), offset, "Study Stash can't read the words in this file (it may be a picture or a scan).");
    }

    /// <summary>Where a path really is, every link along it followed; null when it can't be followed (a loop, or
    /// a link to nowhere).</summary>
    public static string? RealPath(string path, int depth = 0)
    {
        if (depth > 32) return null;
        string full = Path.GetFullPath(path);
        string root = Path.GetPathRoot(full) ?? "";
        string at = root;
        try
        {
            foreach (string part in full[root.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
            {
                string next = Path.Combine(at, part);
                FileSystemInfo info = Directory.Exists(next) ? new DirectoryInfo(next) : new FileInfo(next);
                if (info.LinkTarget is not null)
                {
                    if (info.ResolveLinkTarget(returnFinalTarget: true) is not { } target || RealPath(target.FullName, depth + 1) is not { } resolved) return null;
                    next = resolved;
                }
                at = next;
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
        return at;
    }

    static JsonObject CanvasErr(string msg) => new() { ["error"] = msg };

    public async Task<JsonNode> CanvasAsync(string what, JsonObject? body = null)
    {
        if (canvas is null) return new JsonObject { ["error"] = "This library can't read Canvas." };
        static string S(JsonNode? n) => n is JsonValue v && v.TryGetValue(out string? s) ? s ?? "" : "";
        string? cls = S(body?["class"]) is { Length: > 0 } c ? c : null;
        switch (what)
        {
            case "courses": return canvas.Courses();
            case "fetch": return await canvas.FetchAsync(S(body?["url"]), S(body?["kind"]) is { Length: > 0 } k ? k : "json", S(body?["save_to"]));
            case "assignment":
            {
                if (cls is null) return CanvasErr("Give a class name.");
                var index = Canvas.CourseIndex.Load(home ?? "", cls);
                if (index is null) return CanvasErr($"{cls} hasn't synced with Canvas yet.");
                long? id = body?["id"] is JsonValue idv && idv.TryGetValue(out long idl) ? idl : null;
                string name = S(body?["name"]);
                if (id is null && name.Length > 0)
                {
                    var matches = index.Assignments.Where(a => a.Name.Contains(name, StringComparison.OrdinalIgnoreCase)).ToList();
                    if (matches.Count == 0) return CanvasErr($"No assignment in {cls} matches \"{name}\".");
                    if (matches.Count > 1)
                        return new JsonObject { ["candidates"] = new JsonArray(matches.Select(m => (JsonNode)new JsonObject { ["id"] = m.Id, ["name"] = m.Name }).ToArray()) };
                    id = matches[0].Id;
                }
                if (id is null) return CanvasErr("Give an assignment id or words of its name.");
                var found = Canvas.CanvasView.Assignment(index, id.Value, canvas.Clock(), canvas.Crawl.AssignmentFolder(cls, id.Value));
                return found is null ? CanvasErr($"No assignment {id} in {cls}.") : found;
            }
            case "modules":
                return cls is null ? CanvasErr("Give a class name.") : Canvas.CanvasView.Modules(Canvas.CourseIndex.Load(home ?? "", cls));
            case "files":
                return cls is null ? CanvasErr("Give a class name.") : Canvas.CanvasView.Files(Canvas.CourseIndex.Load(home ?? "", cls));
            case "announcements":
            {
                if (cls is null) return CanvasErr("Give a class name.");
                var result = Canvas.CanvasView.Announcements(Canvas.CourseIndex.Load(home ?? "", cls), Canvas.CanvasSeen.For(home ?? "", cls));
                if (body?["limit"] is JsonValue lv && lv.TryGetValue(out int limit) && result["items"] is JsonArray items)
                    while (items.Count > limit) items.RemoveAt(items.Count - 1);
                return result;
            }
            default: // "assignments": what's still due, everywhere or in one class
                var all = Canvas.Assignments.Load(home ?? "");
                var list = Canvas.Assignments.Upcoming(all, DateTime.Now, body?["days"] is JsonValue d && d.TryGetValue(out int n) ? n : 14, cls);
                return new JsonArray(list.Select(a => (JsonNode)new JsonObject
                {
                    ["class"] = a.ClassName, ["name"] = a.Name, ["due"] = a.Due, ["status"] = a.Status, ["points"] = a.Points,
                    ["label"] = Canvas.Assignments.Label(a.Status, a.Late), ["folder"] = canvas.Crawl.AssignmentFolder(a.ClassName, a.Id), ["url"] = a.Url,
                }).ToArray());
        }
    }

    public Task<JsonObject> OverviewAsync() => Task.FromResult(reader.Overview());
    public Task<JsonArray> LecturesAsync(string? className, int limit, string? before) => Task.FromResult(reader.Lectures(className, limit, before));
    public Task<JsonObject?> LectureAsync(string id) => Task.FromResult(reader.Lecture(id));
    public Task<JsonObject> SearchAsync(string query, string? className, int limit) => Task.FromResult(reader.Search(query, className, limit));
}

/// <summary>
/// The library over its API (/api/v2), with the laptop's password: what the Study Stash app shows, and what Claude
/// reads through <c>Study Stash mcp</c> on a laptop.
/// </summary>
public sealed class RemoteLibrary(string serverUrl, string key, HttpClient? http = null) : ILibrarySource
{
    static readonly HttpClient Shared = new() { Timeout = TimeSpan.FromSeconds(150) };
    readonly HttpClient client = http ?? Shared;
    readonly string root = serverUrl.TrimEnd('/') + "/api/v2";

    public string ServerUrl { get; } = serverUrl.TrimEnd('/');

    /// <summary>This computer's name, sent with every request so the library can say which laptops reach it ("" sends
    /// none: Claude's MCP server on the library itself, and tests).</summary>
    public static string Computer { get; set; } = "";

    async Task<JsonNode?> SendAsync(HttpMethod method, string path, JsonNode? body = null, CancellationToken stop = default)
    {
        using var request = new HttpRequestMessage(method, root + path);
        if (key.Length > 0) request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + key);
        if (Computer.Length > 0) request.Headers.TryAddWithoutValidation("X-Study-Stash-Computer", Computer);
        if (body is not null) request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        using var r = await client.SendAsync(request, stop);
        if (r.StatusCode == HttpStatusCode.NotFound) return null;
        string text = await r.Content.ReadAsStringAsync(stop);
        if (!r.IsSuccessStatusCode)
        {
            string detail = (JsonNode.Parse(text.Length > 0 && text[0] == '{' ? text : "{}") as JsonObject)?["detail"]?.GetValue<string>() ?? r.ReasonPhrase ?? "";
            throw new LibraryRefusedException((int)r.StatusCode, detail);
        }
        return JsonNode.Parse(text);
    }

    static string Q(string? s) => Uri.EscapeDataString(s ?? "");

    /// <summary>The library's name and classes. A library from before /api/v2 (the Python engine) answers 404.</summary>
    public async Task<JsonObject> OverviewAsync() =>
        await SendAsync(HttpMethod.Get, "/library") as JsonObject ?? throw new LibraryRefusedException(404, "This library is older than the app: update it to browse it here.");

    public async Task<JsonArray> LecturesAsync(string? className, int limit, string? before) =>
        (JsonArray)(await SendAsync(HttpMethod.Get, $"/lectures?limit={limit}" + (className is null ? "" : $"&class={Q(className)}")
            + (before is null ? "" : $"&before={Q(before)}")))!;

    public async Task<JsonObject?> LectureAsync(string id) => await SendAsync(HttpMethod.Get, $"/lectures/{Q(id)}") as JsonObject;

    public async Task<JsonObject> SearchAsync(string query, string? className, int limit) =>
        (JsonObject)(await SendAsync(HttpMethod.Get, $"/search?q={Q(query)}&limit={limit}" + (className is null ? "" : $"&class={Q(className)}")))!;

    public async Task<JsonObject> AskAsync(string question, string? lectureId = null, string? className = null, string? live = null,
        string? liveTitle = null, CancellationToken stop = default) =>
        (JsonObject)(await SendAsync(HttpMethod.Post, "/ask", new JsonObject
        {
            ["question"] = question, ["lecture"] = lectureId, ["class"] = className, ["live"] = live, ["live_title"] = liveTitle,
        }, stop))!;

    public async Task MoveAsync(string id, string className) =>
        await SendAsync(HttpMethod.Post, $"/lectures/{Q(id)}/class", new JsonObject { ["class"] = className });

    public async Task RewriteAsync(string id) => await SendAsync(HttpMethod.Post, $"/lectures/{Q(id)}/rewrite", new JsonObject());

    /// <summary>Deletes a lecture on the library: its notes, transcript and search passages. It waits in the
    /// library's trash for a few minutes, so <see cref="RestoreAsync"/> can undo it. False: there's no such lecture.</summary>
    public async Task<bool> DeleteAsync(string id) => await SendAsync(HttpMethod.Delete, $"/lectures/{Q(id)}") is not null;

    /// <summary>Brings a deleted lecture back from the library's trash. Null: it isn't there any more (the trash was
    /// emptied, or the library runs a Study Stash without one).</summary>
    public async Task<JsonObject?> RestoreAsync(string id) => await SendAsync(HttpMethod.Post, $"/lectures/{Q(id)}/restore", new JsonObject()) as JsonObject;

    /// <summary>Adds a class to the library, with what it covers when given (the AI reads it to sort lectures).</summary>
    public async Task<JsonObject> AddClassAsync(string name, string? description = null) =>
        (JsonObject)(await SendAsync(HttpMethod.Post, "/classes", new JsonObject { ["name"] = name, ["description"] = description }))!;

    public async Task<JsonObject?> ClaudeAsync(HttpMethod method, string path = "", JsonObject? body = null) =>
        await SendAsync(method, "/claude" + path, body) as JsonObject;

    /// <summary>The library's own settings (GET), a change to them (POST), or one of their actions ("/password",
    /// "/update", "/rewrite-all"). Null: a library older than these routes.</summary>
    public async Task<JsonObject?> SettingsAsync(HttpMethod method, string path = "", JsonObject? body = null) =>
        await SendAsync(method, "/settings" + path, body) as JsonObject;

    public bool HasCanvas => true; // a library from before Canvas answers each tool with why not

    public async Task<JsonArray> SearchFilesAsync(string query, int limit)
    {
        try
        {
            return await SendAsync(HttpMethod.Get, $"/files/search?q={Q(query)}&limit={limit}") as JsonArray ?? [];
        }
        catch (LibraryRefusedException)
        {
            return [];
        }
    }

    static JsonObject CanvasErr(string msg) => new() { ["error"] = msg };
    const string OlderLibrary = "The library runs an older Study Stash, without this.";

    static string S(JsonNode? n) => n is JsonValue v && v.TryGetValue(out string? s) ? s ?? "" : "";

    public async Task<JsonNode> CanvasAsync(string what, JsonObject? body = null)
    {
        string? cls = S(body?["class"]) is { Length: > 0 } c ? c : null;
        try
        {
            switch (what)
            {
                case "courses": return await SendAsync(HttpMethod.Get, "/canvas/agent-courses") ?? CanvasErr("The library runs an older Study Stash, without Canvas.");
                case "fetch": return await SendAsync(HttpMethod.Post, "/canvas/fetch", body) ?? CanvasErr("The library runs an older Study Stash, without Canvas.");
                case "assignment":
                {
                    if (cls is null) return CanvasErr("Give a class name.");
                    long? id = body?["id"] is JsonValue idv && idv.TryGetValue(out long idl) ? idl : null;
                    string name = S(body?["name"]);
                    if (id is null && name.Length > 0)
                    {
                        var listAll = await SendAsync(HttpMethod.Get, $"/assignments?class={Q(cls)}") as JsonArray ?? [];
                        var matches = listAll.OfType<JsonObject>().Where(o => S(o["name"]).Contains(name, StringComparison.OrdinalIgnoreCase)).ToList();
                        if (matches.Count == 0) return CanvasErr($"No assignment in {cls} matches \"{name}\".");
                        if (matches.Count > 1)
                            return new JsonObject { ["candidates"] = new JsonArray(matches.Select(m => (JsonNode)new JsonObject { ["id"] = m["id"]!.DeepClone(), ["name"] = m["name"]!.DeepClone() }).ToArray()) };
                        id = matches[0]["id"]!.GetValue<long>();
                    }
                    if (id is null) return CanvasErr("Give an assignment id or words of its name.");
                    return await SendAsync(HttpMethod.Get, $"/canvas/assignment?class={Q(cls)}&id={id}") ?? CanvasErr(OlderLibrary);
                }
                case "modules":
                    return cls is null ? CanvasErr("Give a class name.") : await SendAsync(HttpMethod.Get, $"/canvas/modules?class={Q(cls)}") ?? CanvasErr(OlderLibrary);
                case "files":
                    return cls is null ? CanvasErr("Give a class name.") : await SendAsync(HttpMethod.Get, $"/canvas/files?class={Q(cls)}") ?? CanvasErr(OlderLibrary);
                case "announcements":
                {
                    if (cls is null) return CanvasErr("Give a class name.");
                    if (await SendAsync(HttpMethod.Get, $"/canvas/announcements?class={Q(cls)}") is not JsonObject result) return CanvasErr(OlderLibrary);
                    if (body?["limit"] is JsonValue lv && lv.TryGetValue(out int limit) && result["items"] is JsonArray items)
                        while (items.Count > limit) items.RemoveAt(items.Count - 1);
                    return result;
                }
                default:
                    return await SendAsync(HttpMethod.Get, $"/assignments?days={(body?["days"] is JsonValue d && d.TryGetValue(out int n) ? n : 14)}"
                        + (cls is null ? "" : "&class=" + Q(cls))) ?? CanvasErr("The library runs an older Study Stash, without Canvas.");
            }
        }
        catch (LibraryRefusedException e)
        {
            return new JsonObject { ["error"] = e.Message };
        }
    }

    /// <summary>Canvas's settings for the app: GET, or POST {url, courses, sync}; "/extension" (the extension's key),
    /// "/courses" (POST: look up the person's Canvas courses through Chrome).</summary>
    public async Task<JsonObject?> CanvasSettingsAsync(HttpMethod method, string path = "", JsonObject? body = null) =>
        await SendAsync(method, "/canvas" + path, body) as JsonObject;

    /// <summary>A class's assignments (all of them), or what's due soon everywhere (<paramref name="days"/>).</summary>
    public async Task<JsonArray> AssignmentsAsync(string? className, int? days = null) =>
        await SendAsync(HttpMethod.Get, "/assignments?" + (className is null ? "" : "class=" + Q(className) + "&") + (days is int d ? $"days={d}" : "")) as JsonArray ?? [];

    /// <summary>A text file in a class's folder (an assignment's spec.md, modules.md): its text, or null.</summary>
    public async Task<string?> FileTextAsync(string className, string path) =>
        (await SendAsync(HttpMethod.Get, $"/files?class={Q(className)}&path={Q(path)}") as JsonObject)?["text"]?.GetValue<string>();

    /// <summary>One chat turn, streamed: <paramref name="onEvent"/> gets each event ({"chat"} first, then {"kind", "text",
    /// "name", "path"}). Null when the library is older than chat.</summary>
    public async Task<bool> ChatAsync(JsonObject body, Action<JsonObject> onEvent, CancellationToken stop = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, root + "/chat")
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
        };
        if (key.Length > 0) request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + key);
        using var r = await ChatClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, stop);
        if (r.StatusCode == HttpStatusCode.NotFound) return false;
        if (!r.IsSuccessStatusCode) throw new LibraryRefusedException((int)r.StatusCode, r.ReasonPhrase ?? "");
        using var reader = new StreamReader(await r.Content.ReadAsStreamAsync(stop));
        while (await reader.ReadLineAsync(stop) is { } line)
            if (line.Length > 0 && JsonNode.Parse(line) is JsonObject ev) onEvent(ev);
        return true;
    }

    /// <summary>Keep something jotted down (Capture): the library's AI files it under its class.</summary>
    public async Task<JsonObject?> CaptureAsync(string text, string? className = null) =>
        await SendAsync(HttpMethod.Post, "/capture", new JsonObject { ["text"] = text, ["class"] = className }) as JsonObject;

    /// <summary>Open a terminal with the AI in a class's folder, on the library's own computer. What happened.</summary>
    public async Task<string> TerminalAsync(string? className) =>
        (await SendAsync(HttpMethod.Post, "/terminal", new JsonObject { ["class"] = className }) as JsonObject)?["said"]?.GetValue<string>()
        ?? "Your library runs an older Study Stash.";

    /// <summary>A chat turn can take minutes (an AI reading around): its own client with no time limit.</summary>
    static readonly HttpClient ChatClient = new() { Timeout = Timeout.InfiniteTimeSpan };

    /// <summary>Which AI does the library's work: /api/v2/ai (GET, or POST a choice), and "/test" to try one.</summary>
    public async Task<JsonObject?> AiAsync(HttpMethod method, string path = "", JsonObject? body = null) =>
        await SendAsync(method, "/ai" + path, body) as JsonObject;
}

/// <summary>
/// The tools Claude gets (Claude Code, Claude Desktop, claude.ai): read-only, over the library's lectures. They
/// answer in short plain text with each lecture's id, so Claude can ask for more.
/// </summary>
public static class ClaudeTools
{
    public const string ServerName = "study-stash";

    /// <summary>What the stdio server tells Claude Code and Claude Desktop about the library.</summary>
    public const string Instructions =
        "Study Stash is the user's own lecture library: lectures they recorded, transcribed on their laptop, with notes "
        + "written from each transcript. Search it before answering anything about their classes, lectures, exams or "
        + "assignments, and say which lecture (and the time in it) an answer comes from. Lecture ids look like rec-... "
        + "or a long hex string; pass them to get_lecture and get_transcript. When Canvas is linked, due_assignments, "
        + "get_assignment, class_modules, class_files and class_announcements read what Study Stash already mirrors from "
        + "Canvas; canvas_api, canvas_page and canvas_download read Canvas directly and never other students' data. "
        + "If a tool says AI tool access is off, tell the user what it says: the student turns it on in Study Stash.";

    /// <summary>What the internet door tells Claude on the web and in the desktop app: nothing here writes, and
    /// nothing points at files on a computer Claude can't open.</summary>
    public const string WebInstructions =
        "Study Stash is the student's own lecture library: lectures they recorded and transcribed, with study notes "
        + "written from each transcript, and what their classes have on Canvas. Search it (search_notes) before answering "
        + "anything about their classes, lectures, exams or assignments, and say which lecture, and the time in it, an "
        + "answer comes from. Class names come from list_classes. Lecture ids look like rec-... or a long hex string: "
        + "list_lectures and search_notes give them, get_lecture and get_transcript take them. When Canvas is linked, "
        + "due_assignments, get_assignment, class_modules, class_files and class_announcements read what Study Stash "
        + "already mirrors from Canvas; search_files and read_file find and read course files; canvas_api and canvas_page "
        + "read Canvas live, and never other students' data. Every tool only reads. If a tool says AI tool access is "
        + "off, or that a setting doesn't allow something, tell the student what it says: they change it in Study Stash.";

    /// <summary>The most characters one call hands back, of a transcript or a file.</summary>
    public const int PageChars = 40000;

    /// <summary>A long text a page at a time: <see cref="PageChars"/> from <paramref name="offset"/>, and where to
    /// read on when there's more.</summary>
    public static string Page(string text, int offset, string empty)
    {
        if (text.Length == 0) return empty;
        offset = Math.Clamp(offset, 0, text.Length);
        if (offset == text.Length) return "That's the end of the file.";
        int end = Math.Min(text.Length, offset + PageChars);
        return text[offset..end] + (end < text.Length ? $"\n(More: call again with offset={end}.)" : "");
    }

    static string Day(string? date)
    {
        if (!DateTimeOffset.TryParse(date, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var d)) return date ?? "";
        return d.ToString("ddd d MMM yyyy", CultureInfo.InvariantCulture);
    }

    static string S(JsonNode? n) => n is JsonValue v && v.TryGetValue(out string? s) ? s ?? "" : "";

    static string Line(JsonObject l)
    {
        var sb = new StringBuilder($"- [{S(l["id"])}] {S(l["title"])} — {S(l["class"])}, {Day(S(l["date"]))}");
        if (l["seconds"] is JsonValue v && v.TryGetValue(out double s)) sb.Append(", ").Append(TimedText.Length(s));
        if (S(l["status"]) is not ("done" or "")) sb.Append(" (notes still being written)");
        if (S(l["summary"]) is { Length: > 0 } summary) sb.Append(". ").Append(summary);
        return sb.ToString();
    }

    public static async Task<string> ListClassesAsync(ILibrarySource lib)
    {
        var o = await lib.OverviewAsync();
        var sb = new StringBuilder($"Library: {S(o["name"])}\n");
        var classes = (o["classes"] as JsonArray ?? []).OfType<JsonObject>().ToList();
        if (classes.Count == 0) sb.Append("No classes yet.\n");
        foreach (var c in classes) sb.Append($"- {S(c["name"])} ({c["lectures"]} lecture{(c["lectures"]?.GetValue<int>() == 1 ? "" : "s")})\n");
        if (o["unsorted"]?.GetValue<int>() is int u and > 0) sb.Append($"Unsorted: {u} lecture{(u == 1 ? "" : "s")} not yet in a class\n");
        return sb.ToString().TrimEnd();
    }

    public static async Task<string> ListLecturesAsync(ILibrarySource lib, string? className, int limit, string? before)
    {
        var list = await lib.LecturesAsync(string.IsNullOrWhiteSpace(className) ? null : className, Math.Clamp(limit, 1, 100), before);
        if (list.Count == 0) return className is null ? "No lectures yet." : $"No lectures in {className}.";
        string more = list.Count >= limit ? $"\n(More: call again with before=\"{S(list[^1]?["date"])}\".)" : "";
        return string.Join("\n", list.OfType<JsonObject>().Select(Line)) + more;
    }

    public static async Task<string> SearchAsync(ILibrarySource lib, string query, string? className, int limit)
    {
        var r = await lib.SearchAsync(query, string.IsNullOrWhiteSpace(className) ? null : className, Math.Clamp(limit, 1, 25));
        var sb = new StringBuilder();
        var lectures = (r["lectures"] as JsonArray ?? []).OfType<JsonObject>().ToList();
        var passages = (r["passages"] as JsonArray ?? []).OfType<JsonObject>().ToList();
        if (lectures.Count == 0 && passages.Count == 0) return $"Nothing in the library matches \"{query}\". Try fewer or other words.";
        if (lectures.Count > 0) sb.Append("Lectures:\n").AppendJoin("\n", lectures.Select(Line)).Append("\n\n");
        if (passages.Count > 0)
        {
            sb.Append("Passages:\n");
            foreach (var p in passages)
            {
                string where = p["at"] is JsonValue v && v.TryGetValue(out double at) ? $"at {TimedText.Clock(at)}"
                    : S(p["section"]) is { Length: > 0 } sec ? sec : S(p["kind"]);
                sb.Append($"- [{S(p["id"])}] {S(p["title"])} ({S(p["class"])}, {Day(S(p["date"]))}), {where}: {S(p["text"])}\n");
            }
        }
        return sb.ToString().TrimEnd();
    }

    public static async Task<string> GetLectureAsync(ILibrarySource lib, string id)
    {
        var l = await lib.LectureAsync(id.Trim());
        if (l is null) return $"There's no lecture {id}. Use search_notes or list_lectures to find one.";
        var sb = new StringBuilder($"# {S(l["title"])}\n\nClass: {S(l["class"])}\nDate: {Day(S(l["date"]))}\n");
        if (l["seconds"] is JsonValue v && v.TryGetValue(out double s)) sb.Append($"Length: {TimedText.Length(s)}\n");
        if (S(l["owner"]) is { Length: > 0 } owner) sb.Append($"Recorded by: {owner}\n");
        var topics = (l["topics"] as JsonArray ?? []).Select(S).Where(t => t.Length > 0).ToList();
        if (topics.Count > 0) sb.Append($"Topics: {string.Join(", ", topics)}\n");
        string notes = S(l["notes"]);
        sb.Append('\n').Append(notes.Length > 0 ? notes : "(No notes yet: the library is still writing them, or there was too little to write from.)");
        string transcript = S(l["transcript"]);
        if (transcript.Length > 0)
            sb.Append($"\n\n(The transcript has {transcript.Split('\n').Length} lines: get_transcript reads it, from any time.)");
        return sb.ToString();
    }

    public const int TranscriptChars = PageChars;

    static double? ParseClock(string? t)
    {
        if (string.IsNullOrWhiteSpace(t)) return null;
        var parts = t.Trim().Split(':');
        double total = 0;
        foreach (string p in parts)
        {
            if (!double.TryParse(p, NumberStyles.Float, CultureInfo.InvariantCulture, out double n)) return null;
            total = total * 60 + n;
        }
        return total;
    }

    public static async Task<string> GetTranscriptAsync(ILibrarySource lib, string id, string? start, string? end)
    {
        var l = await lib.LectureAsync(id.Trim());
        if (l is null) return $"There's no lecture {id}.";
        string transcript = S(l["transcript"]);
        if (transcript.Length == 0) return "This lecture has no transcript.";
        if (!TimedText.HasTimes(transcript))
            return transcript.Length > TranscriptChars ? transcript[..TranscriptChars] + "\n(Cut here: this transcript has no times to page by.)" : transcript;
        double from = ParseClock(start) ?? 0, to = ParseClock(end) ?? double.MaxValue;
        var lines = TimedText.Parse(transcript).Where(s => s.Start >= from && s.Start < to).ToList();
        var sb = new StringBuilder();
        foreach (var s in lines)
        {
            string line = $"[{TimedText.Clock(s.Start)}] {s.Text}\n";
            if (sb.Length + line.Length > TranscriptChars)
            {
                sb.Append($"(More: call again with start=\"{TimedText.Clock(s.Start)}\".)");
                return sb.ToString();
            }
            sb.Append(line);
        }
        return sb.Length == 0 ? "Nothing was said in that stretch." : sb.ToString().TrimEnd();
    }

    static async Task<string> SearchFilesText(ILibrarySource lib, string query, int limit)
    {
        var hits = await lib.SearchFilesAsync(query, Math.Clamp(limit, 1, 30));
        return hits.Count == 0 ? "No files match." : string.Join("\n", hits.OfType<JsonObject>().Select(h => $"- {S(h["path"])} ({S(h["root"])}): {S(h["snippet"])}"));
    }

    static async Task<string> CanvasText(ILibrarySource lib, string what, JsonObject? body = null)
    {
        var r = await lib.CanvasAsync(what, body);
        if (r is JsonObject o && o["error"] is not null) return "Couldn't: " + S(o["error"]);
        if (r is JsonObject j && j["markdown"] is not null) return S(j["markdown"]);
        if (r is JsonObject api && api["json"] is not null)
        {
            string text = S(api["json"]);
            api.Remove("json");
            return api.ToJsonString() + "\n" + text;
        }
        return r.ToJsonString();
    }

    static string Num(JsonNode? n) => n is JsonValue v && v.TryGetValue(out double d) ? d.ToString("0.##", CultureInfo.InvariantCulture) : "";

    static bool Bool(JsonNode? n) => n is JsonValue v && v.TryGetValue(out bool b) && b;

    /// <summary>Where a saved file is in its class's folder, or, on the web (<paramref name="cls"/> given), the
    /// "&lt;class&gt;/&lt;path&gt;" read_file takes.</summary>
    static string Saved(string local, string? cls) => local.Length > 0 && cls is not null ? cls + "/" + local : local;

    static string FileLine(JsonObject f, string? cls)
    {
        string name = S(f["name"]);
        string size = f["size"] is JsonValue sv && sv.TryGetValue(out long bytes) && bytes > 0 ? $" ({Canvas.CanvasMarkdown.Size(bytes)})" : "";
        string local = Saved(S(f["local"]), cls), skipped = S(f["skipped"]), url = S(f["url"]);
        if (local.Length > 0) return $"- {name}{size}: {local}";
        if (skipped.Length > 0) return $"- {name}{size}: not saved ({skipped})" + (url.Length > 0 ? " — " + url : "");
        return $"- {name}{size}" + (url.Length > 0 ? ": " + url : "");
    }

    static string RubricLine(JsonObject c)
    {
        string crit = S(c["criterion"]), max = Num(c["points"]);
        if (c["mark"] is not JsonObject mark) return $"- {crit}: not yet marked ({max} pts)";
        string comment = S(mark["comment"]);
        return $"- {crit}: {Num(mark["points"])} / {max}" + (comment.Length > 0 ? " · " + comment : "");
    }

    /// <summary>The Due list, grouped and labelled the way the app shows it: overdue first, then everything else
    /// with a due date, then work with none.</summary>
    public static async Task<string> DueAssignmentsAsync(ILibrarySource lib, string? className, int days, bool folders = true)
    {
        var r = await lib.CanvasAsync("assignments", new JsonObject { ["class"] = className, ["days"] = days });
        if (r is JsonObject err && err["error"] is not null) return "Couldn't: " + S(err["error"]);
        var items = (r as JsonArray ?? []).OfType<JsonObject>().ToList();
        if (items.Count == 0) return className is null ? "Nothing due." : $"Nothing due in {className}.";
        var overdue = items.Where(o => S(o["status"]) is "past due" or "missing").ToList();
        var soon = items.Where(o => !overdue.Contains(o) && S(o["due"]).Length > 0).ToList();
        var undated = items.Where(o => !overdue.Contains(o) && S(o["due"]).Length == 0).ToList();
        string Row(JsonObject o)
        {
            string due = S(o["due"]), pts = Num(o["points"]), folder = folders ? S(o["folder"]) : "";
            return $"- {S(o["class"])} · {S(o["name"])} · {S(o["label"])}"
                + (due.Length > 0 ? " · due " + Canvas.Assignments.Say(due, DateTime.Now) : "")
                + (pts.Length > 0 ? $" · {pts} pts" : "") + (folder.Length > 0 ? $" · {folder}" : "");
        }
        var sb = new StringBuilder();
        void Section(string label, List<JsonObject> list)
        {
            if (list.Count == 0) return;
            sb.Append(label).Append(":\n").Append(string.Join("\n", list.Select(Row))).Append("\n\n");
        }
        Section("Overdue", overdue);
        Section("Due soon", soon);
        Section("No due date", undated);
        return sb.ToString().TrimEnd();
    }

    static string FormatAssignment(JsonObject a, string? cls)
    {
        var sb = new StringBuilder();
        sb.Append($"# {S(a["name"])} — {S(a["class"])}\n\n");
        string due = S(a["due_at"]).Length > 0 ? Canvas.CanvasMarkdown.When(S(a["due_at"]), TimeZoneInfo.Local) : "";
        sb.Append("Due: ").Append(due.Length > 0 ? due : "No due date").Append('\n');
        string pts = Num(a["points"]);
        if (pts.Length > 0) sb.Append("Points: ").Append(pts).Append('\n');
        sb.Append("Status: ").Append(S(a["label"]));
        if (S(a["score_text"]) is { Length: > 0 } score) sb.Append(" · ").Append(score);
        sb.Append('\n');
        if (a["submission"] is JsonObject subm && S(subm["submitted_at"]) is { Length: > 0 } submittedAt)
            sb.Append("Submitted: ").Append(Canvas.CanvasMarkdown.When(submittedAt, TimeZoneInfo.Local)).Append('\n');
        if (S(a["instructions"]) is { Length: > 0 } instructions)
            sb.Append("\nInstructions:\n").Append(Py.Head(instructions, 4000).Trim()).Append('\n');
        var files = (a["files"] as JsonArray ?? []).OfType<JsonObject>().ToList();
        if (files.Count > 0) sb.Append("\nFiles:\n").Append(string.Join("\n", files.Select(f => FileLine(f, cls)))).Append('\n');
        var rubric = (a["rubric"] as JsonArray ?? []).OfType<JsonObject>().ToList();
        if (rubric.Count > 0) sb.Append("\nRubric:\n").Append(string.Join("\n", rubric.Select(RubricLine))).Append('\n');
        if (a["submission"] is JsonObject sub2 && (sub2["files"] as JsonArray ?? []).OfType<JsonObject>().ToList() is { Count: > 0 } subFiles)
            sb.Append("\nSubmission:\n").Append(string.Join("\n", subFiles.Select(f => FileLine(f, cls)))).Append('\n');
        var comments = (a["comments"] as JsonArray ?? []).OfType<JsonObject>().ToList();
        if (comments.Count > 0)
        {
            sb.Append("\nComments:\n");
            foreach (var c in comments)
            {
                string at = S(c["at"]);
                sb.Append($"- {S(c["author"])}").Append(at.Length > 0 ? $" ({Canvas.CanvasMarkdown.When(at, TimeZoneInfo.Local)})" : "").Append(": ").Append(S(c["text"])).Append('\n');
            }
        }
        if (S(a["spec"]) is { Length: > 0 } spec) sb.Append("\nSpec: ").Append(Saved(spec, cls)).Append('\n');
        if (S(a["feedback"]) is { Length: > 0 } feedback) sb.Append("Feedback: ").Append(Saved(feedback, cls)).Append('\n');
        return sb.ToString().TrimEnd();
    }

    /// <summary>One assignment's whole story: instructions, rubric with marks and comments, submission and files,
    /// grader comments with author and date. <paramref name="assignment"/> is its id, or words of its name (several
    /// matches are listed instead). <paramref name="web"/>: saved files as the paths read_file takes.</summary>
    public static async Task<string> GetAssignmentAsync(ILibrarySource lib, string class_name, string assignment, bool web = false)
    {
        var body = new JsonObject { ["class"] = class_name };
        if (long.TryParse(assignment, out long id)) body["id"] = id; else body["name"] = assignment;
        var r = await lib.CanvasAsync("assignment", body);
        if (r is not JsonObject o) return "Couldn't read that assignment.";
        if (o["error"] is not null) return "Couldn't: " + S(o["error"]);
        if (o["candidates"] is JsonArray cands)
            return "Several assignments match: " + string.Join("; ", cands.OfType<JsonObject>().Select(c => $"{S(c["name"])} (id {c["id"]})"));
        return FormatAssignment(o, web ? class_name : null);
    }

    static string FormatModules(JsonObject m)
    {
        var modules = (m["modules"] as JsonArray ?? []).OfType<JsonObject>().ToList();
        if (modules.Count == 0) return "No modules.";
        var sb = new StringBuilder();
        foreach (var mod in modules)
        {
            sb.Append("## ").Append(S(mod["name"])).Append('\n');
            foreach (var it in (mod["items"] as JsonArray ?? []).OfType<JsonObject>())
            {
                string kind = S(it["kind"]), source = S(it["source"]);
                bool saved = Bool(it["saved"]);
                string extra = kind == "file" && S(it["format"]) is { Length: > 0 } fmt ? $" ({fmt})"
                    : kind == "link" ? saved ? source.Length > 0 ? $" (saved from {char.ToUpperInvariant(source[0])}{source[1..]})" : " (saved)" : " (link)"
                    : "";
                sb.Append("- ").Append(S(it["title"])).Append(extra).Append(Bool(it["locked"]) ? " · locked" : "").Append('\n');
            }
        }
        return sb.ToString().TrimEnd();
    }

    public static async Task<string> ClassModulesAsync(ILibrarySource lib, string class_name)
    {
        var r = await lib.CanvasAsync("modules", new JsonObject { ["class"] = class_name });
        if (r is JsonObject err && err["error"] is not null) return "Couldn't: " + S(err["error"]);
        return r is JsonObject m ? FormatModules(m) : "Couldn't read this class's modules.";
    }

    static string FileRow(JsonObject f, string? cls)
    {
        string folder = S(f["folder"]);
        string name = (folder.Length > 0 ? folder + "/" : "") + S(f["name"]);
        string size = f["size"] is JsonValue sv && sv.TryGetValue(out long bytes) && bytes > 0 ? $" ({Canvas.CanvasMarkdown.Size(bytes)})" : "";
        string local = Saved(S(f["local"]), cls);
        return $"- {name}{size}" + (local.Length > 0 ? $": {local}" : "");
    }

    /// <summary>The class's Files area, with its own words when Canvas hides it from the student.</summary>
    public static async Task<string> ClassFilesAsync(ILibrarySource lib, string class_name, bool web = false)
    {
        var r = await lib.CanvasAsync("files", new JsonObject { ["class"] = class_name });
        if (r is JsonObject err && err["error"] is not null) return "Couldn't: " + S(err["error"]);
        if (r is not JsonObject f) return "Couldn't read this class's files.";
        bool allowed = f["allowed"] is not JsonValue av || !av.TryGetValue(out bool a) || a;
        var files = (f["files"] as JsonArray ?? []).OfType<JsonObject>().ToList();
        string header = allowed ? "" : "Canvas hides this class's Files area from the student; showing what's known from modules and links.\n";
        if (files.Count == 0) return header.Length > 0 ? header.TrimEnd() : "No files.";
        return header + string.Join("\n", files.Select(f => FileRow(f, web ? class_name : null)));
    }

    /// <summary>A class's announcements, newest first, with bodies cut short.</summary>
    public static async Task<string> ClassAnnouncementsAsync(ILibrarySource lib, string class_name, int limit)
    {
        var r = await lib.CanvasAsync("announcements", new JsonObject { ["class"] = class_name, ["limit"] = Math.Clamp(limit, 1, 30) });
        if (r is JsonObject err && err["error"] is not null) return "Couldn't: " + S(err["error"]);
        if (r is not JsonObject a) return "Couldn't read this class's announcements.";
        var items = (a["items"] as JsonArray ?? []).OfType<JsonObject>().ToList();
        if (items.Count == 0) return "No announcements.";
        var sb = new StringBuilder();
        foreach (var it in items)
        {
            string when = Canvas.CanvasMarkdown.When(S(it["posted_at"]), TimeZoneInfo.Local);
            sb.Append($"- {S(it["title"])} — {S(it["author"])}, {when}").Append(Bool(it["new"]) ? " (new)" : "").Append('\n');
            if (S(it["body"]) is { Length: > 0 } body) sb.Append("  ").Append(Py.Head(body, 300).ReplaceLineEndings(" ").Trim()).Append('\n');
        }
        return sb.ToString().TrimEnd();
    }

    /// <summary>
    /// The tools, each saying what it returns, then when to use it, and where the ids it takes come from. Every one
    /// only reads, except canvas_download, which is for the stdio server alone. <paramref name="web"/>: the set for
    /// the internet door (Claude on the web and the desktop app), which can't open files on this computer: no
    /// canvas_download, and read_file to read a file search found.
    /// </summary>
    public static List<McpServerTool> Tools(ILibrarySource lib, bool web = false)
    {
        static McpServerToolCreateOptions Named(string name, string title, string description, bool writes = false, bool live = false) =>
            new() { Name = name, Title = title, Description = description, ReadOnly = !writes, Destructive = false, Idempotent = !writes, OpenWorld = live };
        const string ClassName = "A class's exact name, from list_classes.";
        List<McpServerTool> tools =
        [
            McpServerTool.Create(() => ListClassesAsync(lib),
                Named("list_classes", "List classes", "The student's classes in Study Stash, each with how many lectures it has, and how "
                    + "many lectures aren't in a class yet. Use it first to learn the exact class names every class_name takes.")),
            McpServerTool.Create(
                ([Description("Only this class (a name from list_classes). Leave out for every class.")] string? class_name = null,
                 [Description("How many lectures, newest first (1 to 100).")] int limit = 20,
                 [Description("Only lectures before this date: the value a previous answer's last line gives, to page back.")] string? before = null) =>
                    ListLecturesAsync(lib, class_name, limit, before),
                Named("list_lectures", "List lectures", "Lectures, newest first, one a line: [id] title — class, date, length, and the "
                    + "lecture in one sentence. Use it to browse a class or find a lecture by date. The id in brackets is what "
                    + "get_lecture and get_transcript take.")),
            McpServerTool.Create(
                ([Description("Words to look for, as you'd type them in a search box.")] string query,
                 [Description("Only this class (a name from list_classes). Leave out to search everything.")] string? class_name = null,
                 [Description("How many results of each kind (1 to 25).")] int limit = 10) =>
                    SearchAsync(lib, query, class_name, limit),
                Named("search_notes", "Search notes and transcripts",
                    "Full-text search of every lecture's notes and transcript. Returns the matching lectures ([id] title — class, "
                    + "date), and passages, each with the section of the notes or the time in the recording it comes from. Use it "
                    + "first for any question about what was taught; get_lecture and get_transcript take the ids it gives.")),
            McpServerTool.Create(
                ([Description("The lecture's id, from list_lectures or search_notes.")] string lecture_id) => GetLectureAsync(lib, lecture_id),
                Named("get_lecture", "Read a lecture's notes", "One lecture's study notes in Markdown (summary, key points, definitions, "
                    + "announcements, questions), with its class, date, length and topics. Use it to answer from a lecture; "
                    + "get_transcript has the exact words. The id comes from list_lectures or search_notes.")),
            McpServerTool.Create(
                ([Description("The lecture's id, from list_lectures or search_notes.")] string lecture_id,
                 [Description("Start here, as mm:ss or h:mm:ss (a passage's time from search_notes). Leave out for the beginning.")] string? start = null,
                 [Description("Stop before this time. Leave out for the end.")] string? end = null) =>
                    GetTranscriptAsync(lib, lecture_id, start, end),
                Named("get_transcript", "Read a lecture's transcript", "What was said in one lecture, line by line, each line with its "
                    + "time. Up to 40,000 characters a call; a longer stretch ends with the start value to read on from. Use it to "
                    + "quote the lecturer or check what the notes leave out. The id comes from list_lectures or search_notes.")),
        ];
        if (!lib.HasCanvas) return tools;
        bool canRead = web && lib.CanReadFiles;
        tools.Add(McpServerTool.Create(
            ([Description("Words to look for.")] string query, [Description("How many files (1 to 30).")] int limit = 10) => SearchFilesText(lib, query, limit),
            Named("search_files", "Search files", "Full-text search of everything in the library that isn't a lecture (Canvas files "
                + "and pages, slides, PDFs, study guides) and the folders the student lets Study Stash read. Returns each file's "
                + "path, the folder it's in, and the words around the match. Use it to find course materials. "
                + (!web ? "Open a file it finds with your own file tools."
                    : canRead ? "read_file reads a file's words by the path given here (a private folder's files are found by name only)."
                    : "The words around the match are all it shows of a file."))));
        if (canRead)
            tools.Add(McpServerTool.Create(
                ([Description("The file's path, exactly as search_files, class_files or get_assignment gave it.")] string path,
                 [Description("Start this many characters in: the offset a previous answer's last line gives. 0 for the beginning.")] int offset = 0) =>
                    lib.ReadFileAsync(path, offset),
                Named("read_file", "Read a file", "The words in a file search_files, class_files or get_assignment found (slides, PDFs, Word, Markdown, "
                    + "code, notebooks), up to 40,000 characters a call; a longer file ends with the offset to read on from. Use it "
                    + "to read course materials in full. Only files in the library and the folders the student shares with AI, not "
                    + "private ones.")));
        tools.AddRange(
        [
            McpServerTool.Create(
                ([Description("Only this class (a name from list_classes). Leave out for every class.")] string? class_name = null,
                 [Description("Due within this many days (overdue ones are always included).")] int days = 14) =>
                    DueAssignmentsAsync(lib, class_name, days, folders: !web),
                Named("due_assignments", "What's due", "Canvas assignments still to hand in, soonest first, grouped Overdue, Due soon and "
                    + "No due date, each as class · name · status · due date · points. Use it for what's due or overdue. For an "
                    + "assignment's instructions, rubric or the grader's feedback, call get_assignment with its class and name.")),
            McpServerTool.Create(() => CanvasText(lib, "courses"),
                Named("canvas_courses", "Canvas courses", "The classes linked to Canvas, each with its Canvas course id, and whether a "
                    + "course recipe (where this instructor puts things) has been written. Use it for the course id that "
                    + "canvas_api and canvas_page paths take (/courses/<id>).")),
            McpServerTool.Create(
                ([Description(ClassName)] string class_name,
                 [Description("The assignment's id, or words of its name (\"problem set 4\") as due_assignments shows it. Several matches are listed to choose from.")] string assignment) =>
                    GetAssignmentAsync(lib, class_name, assignment, canRead),
                Named("get_assignment", "Read an assignment", "One assignment's whole story, including the grader's feedback: instructions, "
                    + "points, due date, status and score, the rubric with the student's marks and the grader's comments on each, what "
                    + "was submitted and its files, and the grader's comments with author and date. Use it for any question about one "
                    + "assignment. The class comes from list_classes; the assignment from due_assignments.")),
            McpServerTool.Create(([Description(ClassName)] string class_name) => ClassModulesAsync(lib, class_name),
                Named("class_modules", "Read a class's modules", "A class's Canvas modules in order, each item with its kind (file, page, "
                    + "assignment, quiz, discussion, link, tool), whether Study Stash saved it, and whether it's locked. Use it to see "
                    + "how a course is laid out, week by week. The class comes from list_classes.")),
            McpServerTool.Create(([Description(ClassName)] string class_name) => ClassFilesAsync(lib, class_name, canRead),
                Named("class_files", "Read a class's Files area", "A class's Canvas Files area: folders and files with their sizes, and "
                    + (canRead ? "the path of each one Study Stash saved (read_file reads it)" : "where Study Stash saved each one")
                    + "; or says when Canvas hides the area from the student. Use it to find course materials by name. The class "
                    + "comes from list_classes.")),
            McpServerTool.Create(
                ([Description(ClassName)] string class_name, [Description("How many, newest first (1 to 30).")] int limit = 10) =>
                    ClassAnnouncementsAsync(lib, class_name, limit),
                Named("class_announcements", "Read a class's announcements", "A class's Canvas announcements, newest first: title, "
                    + "author, date, whether it's new, and the start of the body. Use it for news from the instructor (exam dates, "
                    + "changes, cancellations). The class comes from list_classes.")),
            McpServerTool.Create(
                ([Description("A Canvas REST API path, like /api/v1/courses/123/modules?include[]=items&per_page=100. Course ids come from canvas_courses.")] string path) =>
                    CanvasText(lib, "fetch", new JsonObject { ["url"] = path, ["kind"] = "json" }),
                Named("canvas_api", "Read Canvas's API", "GETs a Canvas REST API path live, through the student's own Canvas sign-in in "
                    + "Chrome, and returns the JSON text; when there's more, next_page is the address to read next. Use it only when "
                    + "the mirrored tools (due_assignments, get_assignment, class_modules, class_files, class_announcements) don't "
                    + "have it. Useful paths: /api/v1/courses/<id>/pages, /api/v1/courses/<id>/front_page, "
                    + "/api/v1/courses/<id>?include[]=syllabus_body, /api/v1/courses/<id>/files, /api/v1/courses/<id>/discussion_topics. "
                    + "Refuses paths that would read other people's data (rosters, other students' posts, conversations).", live: true)),
            McpServerTool.Create(
                ([Description("A Canvas web page, like /courses/123 or /courses/123/pages/syllabus. Course ids come from canvas_courses.")] string url) =>
                    CanvasText(lib, "fetch", new JsonObject { ["url"] = url, ["kind"] = "text" }),
                Named("canvas_page", "Read a Canvas page", "A Canvas web page read live, as Markdown text, through the student's own "
                    + "Canvas sign-in in Chrome. Use it for a page the mirrored tools don't have, like a syllabus or a wiki page.", live: true)),
        ]);
        if (!web)
            tools.Add(McpServerTool.Create(
                ([Description("The file's download address on Canvas: a file object's url, from canvas_api.")] string url,
                 [Description("Where to keep it: \"<class>/<path in its folder>\", like \"CS 101/Canvas/files/Week 1/slides.pdf\".")] string save_to) =>
                    CanvasText(lib, "fetch", new JsonObject { ["url"] = url, ["kind"] = "bytes", ["save_to"] = save_to }),
                Named("canvas_download", "Save a Canvas file", "Downloads a Canvas file into a class's folder in the library and says "
                    + "where it went. The one tool that writes: use it when the user asks to keep a file.", writes: true, live: true)));
        return tools;
    }

    public static List<McpServerPrompt> Prompts() =>
    [
        McpServerPrompt.Create(
            ([Description("The class, as named in Study Stash.")] string class_name) =>
                $"Make me a study guide for {class_name} from my Study Stash lectures. Use list_lectures for {class_name}, read the "
                + "lectures with get_lecture, and organize the guide by topic: key ideas, definitions, worked examples, and anything "
                + "announced about exams or assignments. Say which lecture each part comes from.",
            new McpServerPromptCreateOptions { Name = "study_guide", Title = "Study guide for a class", Description = "A study guide for a class, from its lectures." }),
        McpServerPrompt.Create(
            ([Description("A class, a topic, or a lecture title.")] string topic) =>
                $"Quiz me on {topic}, using my Study Stash lectures (search_notes, then get_lecture). Ask one question at a time, "
                + "wait for my answer, then tell me what I got right and what the lecture said, with the lecture and time.",
            new McpServerPromptCreateOptions { Name = "quiz_me", Title = "Quiz me", Description = "Questions on a class or topic, one at a time, from your lectures." }),
    ];

    public static McpServerOptions Options(ILibrarySource lib)
    {
        var tools = new McpServerPrimitiveCollection<McpServerTool>();
        foreach (var t in Tools(lib)) tools.Add(t);
        var prompts = new McpServerPrimitiveCollection<McpServerPrompt>();
        foreach (var p in Prompts()) prompts.Add(p);
        return new McpServerOptions
        {
            ServerInfo = new Implementation { Name = ServerName, Title = "Study Stash", Version = Engine.Version },
            ServerInstructions = Instructions,
            ToolCollection = tools,
            PromptCollection = prompts,
        };
    }

    /// <summary>The MCP server over stdin and stdout: what Claude Code and Claude Desktop start (<c>Study Stash mcp</c>).
    /// <paramref name="wrapTools"/> lets a caller guard which tools may actually run (AI tool access, read from the
    /// library); left out, every tool runs as listed.</summary>
    public static async Task RunStdioAsync(ILibrarySource lib, CancellationToken stop, Func<List<McpServerTool>, List<McpServerTool>>? wrapTools = null)
    {
        var options = Options(lib);
        if (wrapTools is not null)
        {
            var tools = new McpServerPrimitiveCollection<McpServerTool>();
            foreach (var t in wrapTools(Tools(lib))) tools.Add(t);
            options.ToolCollection = tools;
        }
        await using var server = McpServer.Create(new StdioServerTransport(ServerName), options);
        await server.RunAsync(stop);
    }
}
