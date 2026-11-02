using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using StudyStash.Core;
using StudyStash.Core.Ai;

namespace StudyStash.Library;

/// <summary>
/// /api/v2: the library for the Syft Notes app (and Claude, through <see cref="RemoteLibrary"/>). Everything takes the
/// laptop's key (Authorization: Bearer &lt;library password&gt;), like /api/ingest.
/// </summary>
public sealed partial class LibraryWeb
{
    LibraryReader? reader;
    ClaudeAccess? claude;

    LibraryReader Reader => reader ??= new LibraryReader(cfg, store);

    /// <summary>The laptops that have reached this library since it started, and when each last did.</summary>
    public LaptopsSeen Laptops { get; } = new();

    public ClaudeAccess Claude => claude ??= options.Claude ?? new ClaudeAccess(cfg.Home);

    IResult Api(HttpContext ctx, Func<IResult> answer) => RequireKey(ctx) ?? answer();

    async Task<IResult> ApiAsync(HttpContext ctx, Func<Task<IResult>> answer) => RequireKey(ctx) ?? await answer();

    static string? Str(JsonObject? body, string key) => body?[key] is JsonValue v && v.TryGetValue(out string? s) && !string.IsNullOrWhiteSpace(s) ? s.Trim() : null;

    /// <summary>How long a deleted lecture waits in the trash, so the app's Undo can bring it back.</summary>
    public static readonly TimeSpan TrashKeeps = TimeSpan.FromMinutes(10);

    /// <summary>Lectures deleted longer ago than <see cref="TrashKeeps"/> are gone for good.</summary>
    void EmptyOldTrash() => EmptyTrash(DateTimeOffset.UtcNow - TrashKeeps);

    /// <summary>The trash emptied (what was deleted before <paramref name="before"/>; everything with none): a lecture
    /// gone for good takes its chats with it.</summary>
    int EmptyTrash(DateTimeOffset? before = null)
    {
        var trashed = store.Trashed();
        int n = store.EmptyTrash(before);
        if (n > 0)
        {
            var gone = store.Gone().ToHashSet();
            foreach (string id in trashed.Where(gone.Contains))
            {
                LibraryChats.ForgetLecture(id);
                ReviewCards.ForgetLecture(id);
                Listening.Delete(id);
            }
        }
        return n;
    }

    void MapApp(WebApplication app)
    {
        if (store.IndexMissing() is int n and > 0) Console.WriteLine($"[library] indexed {n} lecture(s) for search");
        // The trash lasts while the library runs: what was deleted before it started again is gone.
        if (EmptyTrash() is int t and > 0) Console.WriteLine($"[library] emptied the trash ({t} lecture(s))");

        app.MapGet("/api/v2/library", (HttpContext ctx) => Api(ctx, () =>
        {
            // Another computer asking (with the password) is a laptop reaching the library: its dropdown says so.
            if (!IsLocal(ctx)) Laptops.Seen(ctx.Request.Headers[LaptopsSeen.Header].ToString(), ctx.Connection.RemoteIpAddress?.ToString(), DateTimeOffset.UtcNow);
            EmptyOldTrash();
            var overview = Reader.Overview();
            overview["laptops"] = Laptops.Json();
            // Deleted for good: a laptop drops its own recording of these.
            overview["gone"] = new JsonArray([.. store.Gone().Select(id => (JsonNode)id)]);
            return Http.Json(overview);
        }));
        // The laptop says what it is recording (at start, every 30 seconds, at stop): the phone's camera banner reads it.
        app.MapPost("/api/v2/recording-now", Http.Handle(ctx => ApiAsync(ctx, async () =>
        {
            var body = await Http.JsonBodyAsync(ctx.Request);
            string? id = Str(body, "id");
            if (id is null) return Http.Detail(400, "which recording?");
            Reader.Recording.Report(id, Str(body, "class") ?? "", Str(body, "title") ?? "", Str(body, "started") ?? "",
                stopped: body?["stopped"] is JsonValue v && v.TryGetValue(out bool b) && b);
            return Http.Json(new JsonObject { ["recording_now"] = Reader.Recording.Json() });
        })));
        app.MapGet("/api/v2/lectures", (HttpContext ctx, string? @class, int? limit, string? before) =>
            Api(ctx, () => Http.Json(Reader.Lectures(@class, limit ?? 50, before))));
        app.MapGet("/api/v2/lectures/{id}", (HttpContext ctx, string id) =>
            Api(ctx, () => Reader.Lecture(id) is { } l ? Http.Json(l) : Http.Detail(404, "no such lecture")));
        app.MapPost("/api/v2/lectures/{id}/class", Http.Handle(ctx => ApiAsync(ctx, async () =>
        {
            string id = (string)ctx.Request.RouteValues["id"]!;
            string? cls = Str(await Http.JsonBodyAsync(ctx.Request), "class");
            if (cls is null) return Http.Detail(400, "which class?");
            if (cls != Configs.Unsorted && !cfg.ClassNames().Contains(cls)) return Http.Detail(400, $"there's no class {cls}");
            return store.SetClass(id, cls) is null && store.Get(id) is null ? Http.Detail(404, "no such lecture")
                : Http.Json(Reader.Lecture(id));
        })));
        // A title of the student's own for a lecture: kept from then on, whatever writes its notes again.
        app.MapPost("/api/v2/lectures/{id}/title", Http.Handle(ctx => ApiAsync(ctx, async () =>
        {
            string id = (string)ctx.Request.RouteValues["id"]!;
            string title = Py.Strip(Str(await Http.JsonBodyAsync(ctx.Request), "title") ?? "").ReplaceLineEndings(" ");
            if (title.Length == 0) return Http.Detail(400, "what should it be called?");
            if (title.Length > 200) return Http.Detail(400, "that title is too long: 200 characters at most");
            return store.SetTitle(id, title) switch
            {
                Store.Renamed.Gone => Http.Detail(404, "no such lecture"),
                Store.Renamed.Busy => Http.Detail(409, "Your library is still writing this lecture's notes. Rename it once they're done."),
                _ => Http.Json(Reader.Lecture(id)),
            };
        })));
        app.MapPost("/api/v2/lectures/{id}/rewrite", (HttpContext ctx, string id) => Api(ctx, () =>
        {
            if (!store.Requeue(id)) return Http.Detail(404, "no such lecture");
            pipeline.Wake();
            return Http.Json(new JsonObject { ["id"] = id, ["status"] = Store.Queued });
        }));
        // Delete a lecture (its notes, transcript and search passages), into the trash for a few minutes so the app can
        // undo it; then it's gone for good.
        app.MapDelete("/api/v2/lectures/{id}", (HttpContext ctx, string id) => Api(ctx, () =>
        {
            EmptyOldTrash();
            return store.Trash(id)
                ? Http.Json(new JsonObject { ["deleted"] = id, ["undo_minutes"] = (int)TrashKeeps.TotalMinutes })
                : Http.Detail(404, "no such lecture");
        }));
        app.MapPost("/api/v2/lectures/{id}/restore", (HttpContext ctx, string id) => Api(ctx, () =>
        {
            EmptyOldTrash();
            if (!store.Restore(id)) return Http.Detail(404, "It's no longer in the trash, so it can't come back.");
            pipeline.Wake();
            return Reader.Lecture(id) is { } l ? Http.Json(l) : Http.Json(new JsonObject { ["id"] = id });
        }));
        app.MapGet("/api/v2/search", (HttpContext ctx, string? q, string? @class, int? limit) =>
            Api(ctx, () => Http.Json(Reader.Search(q ?? "", @class, Math.Clamp(limit ?? 8, 1, 50)))));
        app.MapPost("/api/v2/ask", Http.Handle(ctx => ApiAsync(ctx, async () =>
        {
            var body = await Http.JsonBodyAsync(ctx.Request);
            string? question = Str(body, "question");
            if (question is null) return Http.Detail(400, "what's the question?");
            if (!cfg.OllamaEnabled && options.AskChat is null)
                return Http.Detail(503, "Asking needs the library's AI model: turn it on in the library's Settings.");
            try
            {
                return Http.Json(await Reader.AskAsync(question, Str(body, "lecture"), Str(body, "class"), Str(body, "live"),
                    Str(body, "live_title") ?? "This lecture", options.AskChat));
            }
            catch (Exception e) when (e is HttpRequestException or TimeoutException or InvalidOperationException)
            {
                return Http.Detail(503, $"The library's model didn't answer: {e.Message}");
            }
        })));
        // A new class, and optionally what it covers (the AI reads it to sort lectures); an existing class with nothing
        // said about it yet takes the description.
        app.MapPost("/api/v2/classes", Http.Handle(ctx => ApiAsync(ctx, async () =>
        {
            var body = await Http.JsonBodyAsync(ctx.Request);
            string? name = Str(body, "name");
            string about = Str(body, "description") ?? "";
            if (name is null || name.Length > 60) return Http.Detail(400, "a class needs a name (up to 60 characters)");
            if (name.Equals(Configs.Unsorted, StringComparison.OrdinalIgnoreCase)) return Http.Detail(400, "that name is taken");
            if (about.Length > 500) about = about[..500];
            var known = cfg.Classes.FirstOrDefault(c => c.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (known is null)
            {
                // "group": a sidebar folder of the student's own ("Projects") rather than Classes.
                cfg.Classes.Add(new ClassDef(name, [], about, (Str(body, "group") ?? "").Trim()));
                Configs.Save(cfg);
            }
            else if (known.Description.Length == 0 && about.Length > 0)
            {
                known.Description = about;
                Configs.Save(cfg);
            }
            return Http.Json(Reader.Overview());
        })));

        // What Syft Notes is for ({uses: ["student", "work", ...]}), as setup or the phone asked: not a student, and
        // it's folders rather than classes, without Canvas.
        app.MapPost("/api/v2/uses", Http.Handle(ctx => ApiAsync(ctx, async () =>
        {
            var body = await Http.JsonBodyAsync(ctx.Request);
            if (body?["uses"] is not JsonArray list) return Http.Detail(400, "say what it's for: uses, a list of " + string.Join(", ", Config.UseKinds));
            cfg.Uses = [.. list.Select(n => n is JsonValue v && v.TryGetValue(out string? u) ? u.Trim().ToLowerInvariant() : "")
                .Where(u => Config.UseKinds.Contains(u)).Distinct()];
            Configs.Save(cfg);
            return Http.Json(Reader.Overview());
        })));

        // A calendar event's title as another name of a class ({class, alias}; remove: true takes it away): every
        // event with that title (a class that meets every week) is that class's from then on, and so are the
        // lectures recorded in it.
        app.MapPost("/api/v2/classes/alias", Http.Handle(ctx => ApiAsync(ctx, async () =>
        {
            var body = await Http.JsonBodyAsync(ctx.Request);
            string? name = Str(body, "class"), alias = Str(body, "alias");
            bool remove = body?["remove"] is JsonValue rv && rv.TryGetValue(out bool r) && r;
            if (name is null || alias is null || alias.Length > 120) return Http.Detail(400, "say which class, and the name (up to 120 characters)");
            var known = cfg.Classes.FirstOrDefault(c => c.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (known is null) return Http.Detail(404, "there's no class called " + name);
            // One name belongs to one class: given to this one, it's taken from any other.
            foreach (var c in cfg.Classes) c.Aliases.RemoveAll(a => a.Equals(alias, StringComparison.OrdinalIgnoreCase));
            if (!remove && !alias.Equals(known.Name, StringComparison.OrdinalIgnoreCase)) known.Aliases.Add(alias);
            Configs.Save(cfg);
            return Http.Json(Reader.Overview());
        })));

        // Which AI does the work: one for everything, and optionally another per kind of work (ai.json).
        app.MapGet("/api/v2/ai", (HttpContext ctx) => Api(ctx, () => Http.Json(AiSettings.Load(cfg.Home).ToJson())));
        app.MapPost("/api/v2/ai", Http.Handle(ctx => ApiAsync(ctx, async () =>
        {
            var body = await Http.JsonBodyAsync(ctx.Request);
            var ai = AiSettings.Load(cfg.Home);
            bool hadNotesAi = ai.NotesChosen;
            var known = AiProviders.All().Select(p => p.Id).ToHashSet();
            string? provider = Str(body, "provider"), job = Str(body, "job"), model = body?["model"] is JsonValue mv && mv.TryGetValue(out string? m) ? m.Trim() : null;
            if (provider is not null && provider != "default" && !known.Contains(provider)) return Http.Detail(400, $"there's no AI called {provider}");
            if (job is not null && !AiSettings.Jobs.Contains(job)) return Http.Detail(400, $"there's no kind of work called {job}");
            if (job is null)
            {
                if (provider is not null) ai.Provider = provider;
                if (model is not null) ai.Models[ai.Provider] = model;
            }
            else if (provider == "default") ai.ByJob.Remove(job); // back to the one for everything
            else if (provider is not null || model is not null)
            {
                var was = ai.ByJob.GetValueOrDefault(job) ?? new AiChoice(ai.Provider);
                ai.ByJob[job] = new AiChoice(provider ?? was.Provider, model ?? (provider is null ? was.Model : ""));
            }
            ai.Save(cfg.Home);
            if (!hadNotesAi && ai.NotesChosen) WriteWaitingNotes();
            return Http.Json(ai.ToJson());
        })));
        app.MapPost("/api/v2/ai/test", Http.Handle(ctx => ApiAsync(ctx, async () =>
        {
            var body = await Http.JsonBodyAsync(ctx.Request);
            string provider = Str(body, "provider") ?? AiSettings.Load(cfg.Home).Provider;
            var jobs = options.Ai ?? new AiJobs(cfg.Home, () => cfg.OllamaHost);
            var (ok, why) = await jobs.TestAsync(provider, Str(body, "model") ?? "");
            var result = AiSettings.Load(cfg.Home).ToJson();
            result["tested"] = provider;
            result["ok"] = ok;
            result["why"] = why;
            return Http.Json(result);
        })));

        // Connecting Claude: what's on, the connections that can read, and turning the web address on or off.
        app.MapGet("/api/v2/claude", (HttpContext ctx) => Api(ctx, () => Http.Json(ClaudeJson())));
        app.MapPost("/api/v2/claude/reach", Http.Handle(ctx => ApiAsync(ctx, async () =>
        {
            var body = await Http.JsonBodyAsync(ctx.Request);
            bool internet = body?["internet"] is JsonValue iv && iv.TryGetValue(out bool i) && i;
            bool on = body?["on"] is not JsonValue ov || !ov.TryGetValue(out bool o) || o;
            if (on && cfg.PoolPassword.Length == 0) return Http.Detail(400, "Set a library password first, so only you can let Claude in.");
            var (url, problem) = options.Reach.Set(ClaudeWeb.PortFor(cfg), internet, on);
            if (problem is not null) return Http.Detail(409, problem.Words);
            if (internet) Claude.PublicUrl = url ?? "";
            else Claude.TailnetUrl = url;
            return Http.Json(ClaudeJson());
        })));
        app.MapPost("/api/v2/claude/tokens", Http.Handle(ctx => ApiAsync(ctx, async () =>
        {
            var (token, grant) = Claude.CreateToken(Str(await Http.JsonBodyAsync(ctx.Request), "name") ?? "MCP client");
            var result = ClaudeJson();
            result["token"] = token;
            result["token_id"] = grant.Id;
            return Http.Json(result);
        })));
        app.MapDelete("/api/v2/claude/connections/{id}", (HttpContext ctx, string id) =>
            Api(ctx, () => Claude.Revoke(id) ? Http.Json(ClaudeJson()) : Http.Detail(404, "no such connection")));
    }

    JsonObject ClaudeJson()
    {
        int port = ClaudeWeb.PortFor(cfg);
        return new JsonObject
        {
            ["local_url"] = $"http://127.0.0.1:{port}{ClaudeWeb.McpPath}",
            ["tailnet_url"] = Claude.TailnetUrl is { Length: > 0 } t ? t + ClaudeWeb.McpPath : null,
            ["public_url"] = Claude.PublicUrl.Length > 0 ? Claude.PublicUrl + ClaudeWeb.McpPath : null,
            ["has_password"] = cfg.PoolPassword.Length > 0,
            ["connections"] = new JsonArray(Claude.Grants().Select(g => (JsonNode?)new JsonObject
            {
                ["id"] = g.Id, ["name"] = g.Name, ["kind"] = g.Kind, ["created"] = g.Created, ["last_used"] = g.LastUsed > 0 ? g.LastUsed : null,
            }).ToArray()),
        };
    }
}
