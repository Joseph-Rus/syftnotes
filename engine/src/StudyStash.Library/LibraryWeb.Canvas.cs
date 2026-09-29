using System.Globalization;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using StudyStash.Core;
using StudyStash.Core.Canvas;

namespace StudyStash.Library;

/// <summary>
/// Canvas in the library: the Chrome extension's door (it takes the extension's own key, or the library password),
/// the API the app and Claude's tools use, and the pages: Due, each class's assignments, and Canvas in Settings.
/// </summary>
public sealed partial class LibraryWeb
{
    CanvasSync? canvas;

    /// <summary>One per library: the extension and AIs' reads meet in its queue.</summary>
    public CanvasSync Canvas => canvas ??= options.Canvas ?? new CanvasSync(cfg.Home, c => store.ClassDir(c))
    {
        KnownClass = c => cfg.ClassNames().Contains(c),
    };

    /// <summary>The extension's key (X-Study-Stash-Key), or the library password like the rest of the API. An
    /// extension with another key (an old registration, or another library's) is turned away even when the library
    /// has no password: it would otherwise look connected while the student's Chrome can't be told apart from a
    /// stale one. When it last knocked is kept, so the app can say to connect it again.</summary>
    IResult? RequireExtension(HttpContext ctx)
    {
        string given = ctx.Request.Headers["X-Study-Stash-Key"].ToString();
        if (CanvasSettings.KeyMatches(cfg.Home, given)) return null;
        if (given.Length > 0 && !(cfg.PoolPassword.Length > 0 && Same(given, cfg.PoolPassword)))
        {
            refusedAt = Canvas.Clock().ToString("o", CultureInfo.InvariantCulture);
            return Http.Detail(401, "Wrong key.");
        }
        return RequireKey(ctx);
    }

    /// <summary>When a Chrome with a key that isn't this library's last asked for work (ISO); null since the library
    /// started, or since one with the right key checked in.</summary>
    string? refusedAt;

    /// <summary>The request came with the extension's current key (not the library password).</summary>
    bool WithExtensionKey(HttpContext ctx) => CanvasSettings.KeyMatches(cfg.Home, ctx.Request.Headers["X-Study-Stash-Key"].ToString());

    static string S(JsonNode? v) => v is JsonValue j && j.TryGetValue(out string? s) ? s : "";

    /// <summary>A date for the JSON: null, never "", when there isn't one (<see cref="CanvasView.When"/>).</summary>
    static JsonNode? W(string? iso) => CanvasView.When(iso);

    /// <summary>The most one post of the extension's answers may carry: a 40 MiB file is about 56 MB of base64, and an
    /// extension from before protocol 2 posts up to six answers together.</summary>
    const long ResultsLimit = 256L * 1024 * 1024;

    void MapCanvas(WebApplication app)
    {
        // The extension's folder is ready before anyone asks for it, and an updated Study Stash brings it up to date:
        // Chrome's copy reloads itself from it.
        EnsureHere();
        // A sync's jobs that were out with Chrome when the library stopped: their answers found nobody to take them,
        // so they go out again at once rather than after the ten minutes a job is given (a sync mid-way through a
        // restart carries on as soon as Chrome asks again). An answer that still comes for one is simply asked again.
        Canvas.Crawl.Requeue();

        // The extension. It says its version (v), its protocol (p; one from before protocol 2 sends none) and, from
        // 1.4, the library address it uses (a) and how long it will wait for work (wait, in seconds): the request is
        // held until there's work, so an AI's read reaches Chrome in about a second. A library that's stopping
        // lets go of it at once.
        app.MapGet("/api/v2/canvas/work", async (HttpContext ctx, int? force, string? v, int? p, string? a, int? wait) =>
        {
            if (RequireExtension(ctx) is { } no) return no;
            using var gone = CancellationTokenSource.CreateLinkedTokenSource(ctx.RequestAborted, app.Lifetime.ApplicationStopping);
            bool withKey = WithExtensionKey(ctx);
            if (withKey) refusedAt = null; // the student's Chrome has the right key now
            var w = await Canvas.WorkAsync(force is 1, v, p ?? 1, a, TimeSpan.FromSeconds(Math.Clamp(wait ?? 0, 0, 60)), gone.Token, withKey);
            return Http.Json(new JsonObject
            {
                ["jobs"] = new JsonArray(w.Jobs.Select(j => (JsonNode)new JsonObject { ["id"] = j.Id, ["url"] = j.Url, ["kind"] = j.Kind }).ToArray()),
                ["hot"] = w.Hot, ["ext"] = w.Ext, ["p"] = Extension.Protocol,
            });
        });
        app.MapPost("/api/v2/canvas/results", Http.Handle(async ctx =>
        {
            if (RequireExtension(ctx) is { } no) return no;
            // Files come as base64 inside JSON: past Kestrel's 30 MB default for a big one.
            if (ctx.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } limit) limit.MaxRequestBodySize = ResultsLimit;
            var body = await Http.JsonBodyAsync(ctx.Request);
            Canvas.Results((body?["results"] as JsonArray ?? []).OfType<JsonObject>().Select(CanvasResult.From).ToList());
            return Http.Json(new JsonObject { ["ok"] = true });
        }));
        app.MapGet("/api/v2/canvas/status", (HttpContext ctx) =>
        {
            if (RequireExtension(ctx) is { } no) return no;
            var s = Canvas.Settings;
            var (waiting, inflight) = Canvas.Crawl.Left;
            return Http.Json(new JsonObject
            {
                ["synced"] = W(s.LastSync), ["error"] = s.Error,
                ["busy"] = Canvas.Crawl.Active ? $"Syncing Canvas… {waiting + inflight} left" : "",
            });
        });

        // The app, and Claude's tools.
        app.MapGet("/api/v2/canvas", (HttpContext ctx) => Api(ctx, () => Http.Json(CanvasJson())));
        app.MapPost("/api/v2/canvas", Http.Handle(ctx => ApiAsync(ctx, async () =>
        {
            var body = await Http.JsonBodyAsync(ctx.Request);
            if (S(body?["url"]) is { Length: > 0 } typed && CanvasSettings.CleanUrl(typed) is null) return Http.Detail(400, "That isn't a web address.");
            if (body?["courses"] is JsonObject courses && courses.Any(kv => !cfg.ClassNames().Contains(kv.Key)))
                return Http.Detail(400, "Link Canvas courses to classes this library has.");
            CanvasSettings.Update(cfg.Home, s =>
            {
                if (S(body?["url"]) is { Length: > 0 } u) s.Url = CanvasSettings.CleanUrl(u)!;
                if (body?["courses"] is JsonObject cs)
                    foreach (var (cls, id) in cs)
                    {
                        if (id is JsonValue n && n.TryGetValue(out long cid) && cid > 0)
                        {
                            s.Courses[cls] = cid;
                            s.Choose(cid); // linking a class to a course brings the course in
                        }
                        else if (s.Courses.Remove(cls, out long was) && !s.Courses.ContainsValue(was))
                            s.Chosen?.Remove(was.ToString(CultureInfo.InvariantCulture));
                    }
                // Sync now while a sync is running is that sync: another straight after it would read it all again.
                if (body?["sync"] is JsonValue sv && sv.TryGetValue(out bool now) && now && !Canvas.Crawl.Active) s.SyncNow = true;
                if (body?["poll_minutes"] is JsonValue pv && pv.TryGetValue(out double pm)) s.PollMinutes = Math.Clamp((int)pm, 15, 1440);
                if (body?["dismiss_update"] is JsonValue dv && dv.TryGetValue(out bool dismiss) && dismiss && s.ExtensionUpdate is { } noted)
                    s.ExtensionUpdate = noted with { Dismissed = true };
            });
            if (body?["url"] is not null) EnsureHere(); // the extension may now reach the new Canvas
            if (body?["sync"] is not null) Canvas.Nudge();
            return Http.Json(CanvasJson());
        })));
        app.MapGet("/api/v2/canvas/state", (HttpContext ctx) => Api(ctx, () => Http.Json(CanvasView.State(Canvas, Canvas.Clock(), refusedAt))));
        app.MapGet("/api/v2/canvas/classes", (HttpContext ctx) => Api(ctx, () =>
            Http.Json(CanvasView.Classes(cfg.ClassNames(), Canvas.Settings, cfg.Home, ScoutOf, Canvas.Clock()))));
        app.MapGet("/api/v2/canvas/due", (HttpContext ctx) => Api(ctx, () =>
        {
            var s = Canvas.Settings;
            var all = Assignments.Load(cfg.Home).ToList();
            // A planner to-do with no assignment of its own rides along as a "todo" row (kind, never graded).
            var graded = all.ToList();
            foreach (string cls in cfg.ClassNames())
                if (CourseIndex.Load(cfg.Home, cls) is { } index)
                    all.AddRange(Assignments.Todos(cls, index.Todos, graded, Canvas.Zone));
            return Http.Json(CanvasView.Due(all, s.LastDone, Canvas.Clock(), Canvas.Zone, Canvas.Crawl.AssignmentFolder));
        }));
        app.MapGet("/api/v2/canvas/assignments", (HttpContext ctx, string? @class) => Api(ctx, () =>
        {
            if (@class is null || !cfg.ClassNames().Contains(@class)) return Http.Detail(400, "unknown class");
            var mine = Assignments.Load(cfg.Home).Where(a => a.ClassName == @class).ToList();
            return Http.Json(CanvasView.ForClass(@class, mine, id => Canvas.Crawl.AssignmentFolder(@class, id)));
        }));
        app.MapGet("/api/v2/canvas/assignment", (HttpContext ctx, string? @class, long? id) => Api(ctx, () =>
        {
            if (@class is null || id is null || !cfg.ClassNames().Contains(@class)) return Http.Detail(404, "no such assignment");
            var found = CanvasView.Assignment(CourseIndex.Load(cfg.Home, @class), id.Value, Canvas.Clock(), Canvas.Crawl.AssignmentFolder(@class, id.Value));
            if (found is null) return Http.Detail(404, "no such assignment");
            // The phone shows the instructions as they're drawn for a lecture's notes: safe HTML, formulas ready for KaTeX.
            found["instructions_html"] = PhoneNotes.Render(Py.AsString(found["instructions"]));
            return Http.Json(found);
        }));
        app.MapGet("/api/v2/canvas/modules", (HttpContext ctx, string? @class) => Api(ctx, () =>
            @class is null || !cfg.ClassNames().Contains(@class) ? Http.Detail(400, "unknown class") : Http.Json(CanvasView.Modules(CourseIndex.Load(cfg.Home, @class)))));
        app.MapGet("/api/v2/canvas/files", (HttpContext ctx, string? @class) => Api(ctx, () =>
            @class is null || !cfg.ClassNames().Contains(@class) ? Http.Detail(400, "unknown class") : Http.Json(CanvasView.Files(CourseIndex.Load(cfg.Home, @class)))));
        app.MapGet("/api/v2/canvas/pages", (HttpContext ctx, string? @class) => Api(ctx, () =>
            @class is null || !cfg.ClassNames().Contains(@class) ? Http.Detail(400, "unknown class") : Http.Json(CanvasView.Pages(CourseIndex.Load(cfg.Home, @class)))));
        app.MapGet("/api/v2/canvas/announcements", (HttpContext ctx, string? @class) => Api(ctx, () =>
        {
            if (@class is null || !cfg.ClassNames().Contains(@class)) return Http.Detail(400, "unknown class");
            return Http.Json(CanvasView.Announcements(CourseIndex.Load(cfg.Home, @class), CanvasSeen.For(cfg.Home, @class)));
        }));
        app.MapPost("/api/v2/canvas/announcements/seen", Http.Handle(ctx => ApiAsync(ctx, async () =>
        {
            var body = await Http.JsonBodyAsync(ctx.Request);
            string cls = S(body?["class"]);
            if (cls.Length == 0 || !cfg.ClassNames().Contains(cls)) return Http.Detail(400, "unknown class");
            var ids = (body?["ids"] as JsonArray ?? []).Select(n => n is JsonValue v && v.TryGetValue(out long id) ? id : (long?)null).OfType<long>();
            CanvasSeen.Mark(cfg.Home, cls, ids);
            return Http.Json(new JsonObject { ["ok"] = true });
        })));
        app.MapGet("/api/v2/canvas/notifications", (HttpContext ctx, long? after) => Api(ctx, () =>
        {
            var soonNow = Canvas.Clock();
            CanvasNotifications.EnsureDueSoon(cfg.Home, Assignments.Upcoming(Assignments.Load(cfg.Home), TimeZoneInfo.ConvertTime(soonNow, Canvas.Zone).DateTime, 1), soonNow, Canvas.Zone);
            var (last, items) = CanvasNotifications.Since(cfg.Home, after ?? 0);
            return Http.Json(CanvasView.Notifications(last, items));
        }));
        app.MapPost("/api/v2/canvas/notifications/seen", Http.Handle(ctx => ApiAsync(ctx, async () =>
        {
            var body = await Http.JsonBodyAsync(ctx.Request);
            if (body?["up_to"] is not JsonValue v || !v.TryGetValue(out long upTo)) return Http.Detail(400, "up_to is required");
            CanvasNotifications.MarkSeen(cfg.Home, upTo);
            return Http.Json(new JsonObject { ["ok"] = true });
        })));
        app.MapGet("/api/v2/files/raw", (HttpContext ctx, string? @class, string? path) => Api(ctx, () =>
        {
            if (@class is null || path is null || !cfg.ClassNames().Contains(@class)) return Http.Detail(404, "no such file");
            string root = Path.GetFullPath(store.ClassDir(@class)), full = Path.GetFullPath(Path.Combine(root, path));
            if (!full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal) || !File.Exists(full)) return Http.Detail(404, "no such file");
            return Results.File(full, "application/octet-stream", Path.GetFileName(full));
        }));
        app.MapGet("/api/v2/canvas/extension", (HttpContext ctx) => Api(ctx, () =>
        {
            var about = ExtensionJson();
            string key = CanvasSettings.ExtensionKey(cfg.Home);
            about["key"] = key;
            // For a Chrome Web Store copy in this computer's Chrome: what this folder's config.json says, as a code to
            // paste. A laptop makes its own from the same key and Canvas, with the library address it uses.
            about["connection_code"] = Extension.ConnectionCode($"http://127.0.0.1:{cfg.WebPort}", key, Canvas.Settings.Url);
            return Http.Json(about);
        }));
        app.MapPost("/api/v2/canvas/courses", Http.Handle(ctx => ApiAsync(ctx, async () => Http.Json(await FindCoursesAsync()))));
        // Which courses to bring in (every course id the student ticked): a class for each new one, and each one no
        // longer ticked stops syncing, its class and files kept ("keep": true, the default) or removed (a class with
        // lectures always stays). "match": true links a new course to a class of this library it seems to be first.
        app.MapPost("/api/v2/canvas/choose", Http.Handle(ctx => ApiAsync(ctx, async () =>
        {
            var body = await Http.JsonBodyAsync(ctx.Request);
            if (body?["courses"] is not JsonArray list) return Http.Detail(400, "Say which courses to bring in.");
            var ids = list.Select(n => n is JsonValue v ? v.TryGetValue(out string? t) ? t : v.TryGetValue(out long l) ? l.ToString(CultureInfo.InvariantCulture) : null : null)
                .OfType<string>().ToList();
            bool keep = body["keep"] is not JsonValue kv || !kv.TryGetValue(out bool k) || k;
            bool match = body["match"] is JsonValue mv && mv.TryGetValue(out bool m) && m;
            if (!keep && CourseChoices.Blocked(Canvas.Crawl) is { } busy) return Http.Detail(409, busy);
            var outcome = CourseChoices.Apply(cfg, store, ids, keep, match, Canvas.Crawl);
            if (outcome.Added.Count > 0) Canvas.Nudge();
            var result = CanvasJson();
            result["outcome"] = new JsonObject
            {
                ["added"] = new JsonArray(outcome.Added.Select(c => (JsonNode)c).ToArray()),
                ["stopped"] = new JsonArray(outcome.Stopped.Select(c => (JsonNode)c).ToArray()),
                ["removed"] = new JsonArray(outcome.Removed.Select(c => (JsonNode)c).ToArray()),
                ["kept_for_lectures"] = new JsonArray(outcome.KeptForLectures.Select(c => (JsonNode)c).ToArray()),
            };
            return Http.Json(result);
        })));
        app.MapPost("/api/v2/canvas/fetch", Http.Handle(ctx => ApiAsync(ctx, async () =>
        {
            var body = await Http.JsonBodyAsync(ctx.Request);
            string kind = S(body?["kind"]) is "text" or "bytes" ? S(body?["kind"]) : "json";
            return Http.Json(await Canvas.FetchAsync(S(body?["url"]), kind, S(body?["save_to"]), ctx.RequestAborted));
        })));
        app.MapGet("/api/v2/canvas/agent-courses", (HttpContext ctx) => Api(ctx, () => Http.Json(Canvas.Courses())));
        app.MapGet("/api/v2/assignments", (HttpContext ctx, string? @class, int? days) => Api(ctx, () =>
        {
            var all = Assignments.Load(cfg.Home);
            var list = days is int d ? Assignments.Upcoming(all, DateTime.Now, d, @class) : all.Where(a => @class is null || a.ClassName == @class).ToList();
            return Http.Json(new JsonArray(list.Select(a => (JsonNode)AssignmentJson(a)).ToArray()));
        }));

        app.MapGet("/api/v2/files", (HttpContext ctx, string? @class, string? path) => Api(ctx, () =>
        {
            if (@class is null || path is null || !cfg.ClassNames().Contains(@class)) return Http.Detail(404, "no such file");
            string root = Path.GetFullPath(store.ClassDir(@class)), full = Path.GetFullPath(Path.Combine(root, path));
            if (!full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal) || !File.Exists(full)
                || new FileInfo(full).Length > 2_000_000 || !(full.EndsWith(".md", StringComparison.OrdinalIgnoreCase) || full.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)))
                return Http.Detail(404, "no such file");
            return Http.Json(new JsonObject { ["text"] = File.ReadAllText(full) });
        }));

        app.MapPost("/api/v2/canvas/scout", Http.Handle(ctx => ApiAsync(ctx, async () =>
        {
            string cls = S((await Http.JsonBodyAsync(ctx.Request))?["class"]);
            if (!Canvas.Settings.Courses.ContainsKey(cls)) return Http.Detail(400, "Link that class to Canvas first.");
            if (options.Scout is not { } scout) return Http.Detail(503, "Exploring needs the library's service.");
            scout.Queue(cls);
            return Http.Json(CanvasJson());
        })));
        app.MapPost("/settings/canvas/scout", Http.Handle(ctx => WithMemberAsync(ctx, async _ =>
        {
            string cls = (await Http.FormAsync(ctx.Request)).Get("class");
            if (Canvas.Settings.Courses.ContainsKey(cls)) options.Scout?.Queue(cls);
            return Http.SeeOther("/settings?canvas=scout#canvas");
        })));

        // The pages.
        app.MapGet("/due", (HttpContext ctx) => WithMember(ctx, DuePage));
        app.MapGet("/files/{cls}/{**path}", (HttpContext ctx, string cls, string path) => WithMember(ctx, role => FilePage(role, RouteName(cls), path)));
        app.MapPost("/settings/canvas", Http.Handle(SaveCanvas));
        // The Canvas address as typed goes in first (Find sits in the same form as Save), then Chrome is asked.
        app.MapPost("/settings/canvas/find", Http.Handle(ctx => WithMemberAsync(ctx, async _ =>
        {
            var f = await Http.FormAsync(ctx.Request);
            string typed = Py.Strip(f.Get("canvas_url"));
            string? clean = CanvasSettings.CleanUrl(typed);
            if (f.ContainsKey("canvas_url") && (typed.Length == 0 || clean is not null))
            {
                CanvasSettings.Update(cfg.Home, s => s.Url = clean ?? "");
                EnsureHere();
            }
            string outcome = typed.Length > 0 && clean is null ? FindOutcome.NoAddress
                : FindOutcome.Of(await FindCoursesAsync(), Canvas.Settings, Canvas.Clock());
            return Http.SeeOther("/settings?canvas=" + outcome + "#canvas");
        })));
        // The folder is made by the library itself now; an old page's "Make it" just goes back to Settings.
        app.MapPost("/settings/canvas/extension", Http.Handle(ctx => WithMemberAsync(ctx, _ =>
        {
            EnsureHere();
            return Task.FromResult(Http.SeeOther("/settings#canvas"));
        })));
    }

    /// <summary>Make (or bring up to date) the extension's folder on this computer, pointing at this library and this
    /// Canvas. Never fatal: a folder that can't be written is logged, and Settings says it isn't ready.</summary>
    void EnsureHere()
    {
        try
        {
            // The folder of its own, and the one inside the home a Chrome may already have loaded (kept up to date too).
            var made = Extension.EnsureFor(cfg.Home, $"http://127.0.0.1:{cfg.WebPort}", CanvasSettings.ExtensionKey(cfg.Home), Canvas.Settings.Url);
            string folder = made.Path;
            if (!made.Changed) return;
            Console.WriteLine($"[canvas] the Chrome extension's folder is ready (version {Extension.Version()}): {folder}");
            // Chrome's copy is waiting for work with the old folder's permissions: it looks at the folder now, and
            // reloads itself into the new one.
            Canvas.Nudge();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Console.WriteLine($"[canvas] couldn't write the Chrome extension's folder: {e.Message}");
        }
    }

    /// <summary>What went wrong with the last find, in Canvas's own words, for the Settings page's "other".</summary>
    string lastFindError = "";

    /// <summary>Ask Canvas (through the extension) which courses the person is in, and keep the list (with each
    /// course's code and term, for <see cref="CourseMatch"/>) for Settings. Follows every page. Works before any class
    /// exists. While no extension has ever checked in, Chrome gets a short while to turn up rather than two minutes.</summary>
    async Task<JsonObject> FindCoursesAsync()
    {
        var settings = Canvas.Settings;
        if (!settings.On) return Remember(new JsonObject { ["error"] = FindOutcome.LibraryNoAddress });
        TimeSpan? wait = settings.SeenWithKey.Length == 0 ? Canvas.FirstContactWait : null;
        var found = new Dictionary<string, string>();
        var info = new Dictionary<string, CourseInfo>();
        string? next = "/api/v1/courses?enrollment_state=active&include[]=term&per_page=100";
        for (int page = 0; page < 20 && next is not null; page++)
        {
            var r = await Canvas.FetchAsync(next, "json", timeout: wait);
            if (r["error"] is not null) return page == 0 ? Remember(r) : CanvasJson();
            wait = null;
            foreach (var c in (JsonNode.Parse(S(r["json"])) as JsonArray ?? []).OfType<JsonObject>())
                if (c["id"] is JsonValue id && S(c["name"]) is { Length: > 0 } name)
                {
                    string sid = id.ToJsonString();
                    found[sid] = name;
                    info[sid] = new CourseInfo(S(c["course_code"]), name, S(c["term"]?["name"]),
                        TermStart: S(c["term"]?["start_at"]), TermEnd: S(c["term"]?["end_at"]), CourseStart: S(c["start_at"]), CourseEnd: S(c["end_at"]));
                }
            next = r["next_page"] is JsonValue nv ? S(nv) : null;
        }
        CanvasSettings.Update(cfg.Home, s => { s.Available = found; s.CourseInfo = info; });
        lastFindError = "";
        return CanvasJson();
    }

    /// <summary>A course as a picker shows it: its class name, with its short code beside it ("Software Engineering ·
    /// CSCI 321") when the name doesn't already say it.</summary>
    static string CourseLabel(CanvasSettings s, string id, string title)
    {
        var info = s.CourseInfo.GetValueOrDefault(id);
        string code = info is null ? "" : CourseNames.ShortCode(info.Code, info.Name);
        return code.Length > 0 && !title.Contains(code, StringComparison.OrdinalIgnoreCase) ? $"{title} · {code}" : title;
    }

    JsonObject Remember(JsonObject failed)
    {
        lastFindError = S(failed["error"]);
        return failed;
    }

    /// <summary>The extension's folder here and the last Chrome that checked in, for the app's "Add to Chrome" →
    /// "Connected" (without the key; <c>/api/v2/canvas/extension</c> adds it), and every Chrome that checks in
    /// (<c>copies</c>: this computer's and another's can both run it).</summary>
    JsonObject ExtensionJson()
    {
        var s = Canvas.Settings;
        var now = Canvas.Clock();
        string folder = Extension.Folder(cfg.Home);
        var copies = new JsonArray();
        foreach (var (where, c) in s.ExtensionCopies.OrderByDescending(kv => kv.Value.Seen, StringComparer.Ordinal))
            copies.Add(new JsonObject
            {
                ["where"] = where, ["seen"] = W(c.Seen), ["version"] = c.Version, ["protocol"] = c.Protocol,
                ["key_matches"] = c.Key == s.CurrentKeyId,
                ["connected"] = c.Key == s.CurrentKeyId && CanvasSettings.Connected(c.Seen, c.Protocol, now),
            });
        return new JsonObject
        {
            ["canvas"] = s.Url, ["version"] = Extension.Version(), ["protocol"] = Extension.Protocol,
            ["folder"] = folder, ["folder_ready"] = Extension.Ready(folder),
            ["seen"] = W(s.ExtensionSeen), ["seen_version"] = s.ExtensionVersion, ["seen_protocol"] = s.ExtensionProtocol,
            ["seen_where"] = s.ExtensionWhere, ["key_matches"] = s.LastKeyMatches, ["seen_with_key"] = W(s.SeenWithKey),
            ["refused_at"] = W(refusedAt), ["connected"] = s.ExtensionConnected(now), ["copies"] = copies,
        };
    }

    /// <summary>Where the course scout stands on a class, for <c>/api/v2/canvas/classes</c>.</summary>
    ScoutState ScoutOf(string cls)
    {
        var s = Canvas.Settings;
        if (options.Scout?.Running == cls) return new ScoutState("exploring", 0, "", "");
        if (options.Scout?.Waiting.Contains(cls) == true) return new ScoutState("waiting", 0, "", "");
        return s.Scouts.TryGetValue(cls, out var r) ? new ScoutState(r.Ok ? "done" : "failed", r.Files, r.When, r.Report) : new ScoutState("never", 0, "", "");
    }

    JsonObject AssignmentJson(Assignment a) => new()
    {
        ["class"] = a.ClassName, ["id"] = a.Id, ["name"] = a.Name, ["due"] = W(a.Due), ["points"] = a.Points, ["status"] = a.Status,
        ["score"] = a.Score, ["submitted"] = W(a.Submitted), ["url"] = a.Url, ["done"] = a.Done,
        ["folder"] = Canvas.Crawl.AssignmentFolder(a.ClassName, a.Id),
        ["label"] = Assignments.Label(a.Status, a.Late), ["score_text"] = Assignments.ScoreText(a), ["grade"] = a.Grade,
        ["late"] = a.Late, ["missing"] = a.Missing, ["excused"] = a.Excused, ["graded_at"] = W(a.GradedAt), ["kind"] = a.Kind,
        ["due_at"] = W(a.DueAt), ["comments"] = a.Comments ?? 0,
    };

    JsonObject CanvasJson()
    {
        var s = Canvas.Settings;
        var courses = new JsonObject();
        foreach (var (cls, id) in s.Courses) courses[cls] = id;
        var available = new JsonObject();
        foreach (var (id, name) in CourseNames.Of(s)) available[id] = name;
        var (waiting, inflight) = Canvas.Crawl.Left;
        var courseInfo = new JsonObject();
        var classOf = s.Courses.GroupBy(kv => kv.Value.ToString(CultureInfo.InvariantCulture)).ToDictionary(g => g.Key, g => g.First().Key);
        var now = Canvas.Clock();
        foreach (var (id, info) in s.CourseInfo)
        {
            var (ticked, why) = CourseChoices.Suggest(info, now);
            courseInfo[id] = new JsonObject
            {
                ["code"] = info.Code, ["name"] = info.Name, ["term"] = info.Term,
                ["title"] = available[id]?.GetValue<string>() ?? CourseNames.Title(info.Name, info.Code), ["short_code"] = CourseNames.ShortCode(info.Code, info.Name),
                // Brought in now (chosen, or linked in a library from before the choice), and whether the picker
                // would tick it for a student choosing afresh, and why not.
                ["chosen"] = s.Chosen?.Contains(id) ?? classOf.ContainsKey(id), ["suggested"] = ticked, ["why"] = why,
                ["class"] = classOf.GetValueOrDefault(id),
            };
        }
        return new JsonObject
        {
            ["url"] = s.Url, ["courses"] = courses, ["available"] = available, ["last_sync"] = W(s.LastSync), ["error"] = s.Error,
            ["chosen"] = s.Chosen is null ? null : new JsonArray(s.Chosen.Select(id => (JsonNode)id).ToArray()),
            ["needs_login"] = s.NeedsLogin, ["extension_seen"] = W(s.ExtensionSeen), ["extension_version"] = s.ExtensionVersion,
            ["extension_latest"] = Extension.Version(), ["extension_outdated"] = s.ExtensionOutdated,
            ["extension_update"] = s.ExtensionUpdate is { Dismissed: false } up ? new JsonObject { ["from"] = up.From, ["to"] = up.To, ["at"] = W(up.At) } : null,
            ["state"] = CanvasView.State(Canvas, Canvas.Clock(), refusedAt), ["course_info"] = courseInfo,
            ["syncing"] = Canvas.Crawl.Active, ["left"] = waiting + inflight, ["extension"] = ExtensionJson(),
            ["exploring"] = options.Scout?.Running, ["scouts"] = new JsonObject(s.Scouts.Select(kv => KeyValuePair.Create(kv.Key, (JsonNode?)new JsonObject
            {
                ["ok"] = kv.Value.Ok, ["report"] = kv.Value.Report, ["when"] = W(kv.Value.When), ["files"] = kv.Value.Files,
            }))), ["changes"] = new JsonArray(s.Changes.Select(c => (JsonNode)c).ToArray()),
            ["last_changes"] = new JsonArray(s.LastChanges.Select(c => (JsonNode)new JsonObject
            {
                ["kind"] = c.Kind, ["class"] = c.Class, ["name"] = c.Name, ["text"] = c.Text, ["assignment_id"] = c.AssignmentId,
                ["announcement_id"] = c.AnnouncementId,
            }).ToArray()),
        };
    }

    // --- pages -------------------------------------------------------------------------------------------------

    /// <summary>Canvas has linked classes: the Due page and the assignment sections show.</summary>
    bool CanvasOn => CanvasSettings.PathIn(cfg.Home) is var p && File.Exists(p) && Canvas.Settings is { On: true, Courses.Count: > 0 };

    static readonly Dictionary<string, (string Label, string Color)> StatusLook = new()
    {
        ["missing"] = ("Missing", "var(--red)"), ["past due"] = ("Past due", "var(--red)"), ["open"] = ("To do", "var(--label-3)"),
        ["graded"] = ("Graded", "var(--green)"), ["submitted"] = ("Submitted", "var(--green)"), ["late"] = ("Submitted late", "var(--orange)"),
        ["excused"] = ("Excused", "var(--label-3)"), ["no submission"] = ("Nothing to hand in", "var(--label-3)"),
    };

    string AssignmentRow(Assignment a, bool showClass)
    {
        var (label, color) = StatusLook.GetValueOrDefault(a.Status, (a.Status, "var(--label-3)"));
        string? folder = Canvas.Crawl.AssignmentFolder(a.ClassName, a.Id);
        string title = Ui.Esc(a.Name);
        title = folder is not null ? $"<a href=\"/files/{Ui.Quote(a.ClassName, "")}/{Ui.Quote(folder, "/")}/spec.md\">{title}</a>"
            : a.Url.Length > 0 ? $"<a href=\"{Ui.Esc(a.Url)}\">{title}</a>" : title;
        string score = a.Score is double sc ? $" · {sc.ToString("0.##", CultureInfo.InvariantCulture)}/{(a.Points ?? 0).ToString("0.##", CultureInfo.InvariantCulture)}" : "";
        string where = showClass ? $"<span class=\"tag\" style=\"{Ui.HueStyle(a.ClassName)}\">{Ui.Esc(a.ClassName)}</span> " : "";
        return $"<div class=\"row\"><span class=\"dot\" style=\"--tag:{color}\"></span><div class=\"grow\"><div class=\"title\">{title}</div>"
            + $"<div class=\"subtitle\">{where}{Ui.Esc(Assignments.Say(a.Due, DateTime.Now))} · {Ui.Esc(label)}{score}</div></div></div>";
    }

    string AssignmentGroups(List<Assignment> items, bool showClass)
    {
        var now = DateTime.Now;
        DateTime Due(Assignment a) => a.Due.Length > 0 ? DateTime.Parse(a.Due, CultureInfo.InvariantCulture) : DateTime.MaxValue;
        var groups = new (string Title, Func<Assignment, bool> In)[]
        {
            ("Overdue", a => Due(a) < now),
            ("Next 7 days", a => Due(a) >= now && Due(a) < now.Date.AddDays(8)),
            ("Later", a => Due(a) >= now.Date.AddDays(8) && Due(a) != DateTime.MaxValue),
            ("No due date", a => Due(a) == DateTime.MaxValue),
        };
        return string.Concat(groups.Select(g => items.Where(g.In).ToList() is { Count: > 0 } rows
            ? $"<h2>{g.Title}</h2><div class=\"group\">{string.Concat(rows.Select(a => AssignmentRow(a, showClass)))}</div>" : ""));
    }

    IResult DuePage(string role)
    {
        var c = Context(role, "due", ("/", cfg.PoolName));
        var s = Canvas.Settings;
        var upcoming = Assignments.Upcoming(Assignments.Load(cfg.Home), DateTime.Now, 30);
        string state = s.NeedsLogin || s.Error.Length > 0 ? $"<div class=\"notice\"><div>{Ui.Esc(s.Error)}</div></div>" : "";
        string synced = DateTimeOffset.TryParse(s.LastSync, out var t) ? $"Synced from Canvas {Ui.Esc(t.LocalDateTime.ToString("ddd d MMM, h:mm tt", CultureInfo.InvariantCulture))}." : "Not synced yet.";
        string body = $"<h1>Due</h1><p class=\"sub\">{synced}</p>{state}"
            + (upcoming.Count > 0 ? AssignmentGroups(upcoming, showClass: true)
                : "<div class=\"empty\"><strong>Nothing due</strong>Nothing to hand in for the next month.</div>");
        if (s.Changes.Count > 0)
            body += $"<h2>Last changes on Canvas</h2><div class=\"group\">{string.Concat(s.Changes.Take(12).Select(ch => $"<div class=\"row\"><div class=\"grow\">{Ui.Esc(ch)}</div></div>"))}</div>";
        return Show("Due", body, c);
    }

    /// <summary>A class's assignments and Canvas material, for its page (empty when Canvas doesn't know the class).</summary>
    string ClassCanvas(string name)
    {
        if (!CanvasOn || !Canvas.Settings.Courses.ContainsKey(name)) return "";
        var mine = Assignments.Load(cfg.Home).Where(a => a.ClassName == name).ToList();
        var todo = Assignments.Upcoming(mine, DateTime.Now, 30);
        int done = mine.Count(a => a.Done);
        string cq = Ui.Quote(name, "");
        var links = new List<string>();
        foreach (var (file, label) in new[] { ("Canvas/modules.md", "Modules"), ("Canvas/announcements.md", "Announcements"), ("Canvas/canvas-recipe.md", "How this class uses Canvas") })
            if (File.Exists(Path.Combine(store.ClassDir(name), file))) links.Add($"<a class=\"btn\" href=\"/files/{cq}/{Ui.Quote(file, "/")}\">{label}</a>");
        string todoHtml = todo.Count > 0 ? AssignmentGroups(todo, showClass: false) : "";
        string doneHtml = done > 0
            ? $"<details class=\"help\"><summary>{done} handed in</summary><div class=\"group\">{string.Concat(mine.Where(a => a.Done).OrderByDescending(a => a.Due, StringComparer.Ordinal).Select(a => AssignmentRow(a, false)))}</div></details>"
            : "";
        return $"<section class=\"assignments\">{(links.Count > 0 ? $"<div class=\"toolbar\" style=\"margin:0 0 1rem\">{string.Concat(links)}</div>" : "")}{todoHtml}{doneHtml}</section>";
    }

    /// <summary>A file in a class's folder: Markdown is shown as a page, anything else is downloaded.</summary>
    IResult FilePage(string role, string cls, string path)
    {
        if (!cfg.ClassNames().Contains(cls)) return NotFound(role, "class");
        string root = Path.GetFullPath(store.ClassDir(cls)), full = Path.GetFullPath(Path.Combine(root, Uri.UnescapeDataString(path)));
        if (!full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal) || !File.Exists(full)) return NotFound(role, "file");
        if (!full.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
            return Results.File(full, "application/octet-stream", Path.GetFileName(full));
        var c = Context(role, $"class:{cls}", (Ui.ClassUrl(cls), cls));
        string text = File.ReadAllText(full);
        // An assignment's instructions and your submission link to each other, and to Canvas.
        string bar = "";
        if (Path.GetFileName(full) is "spec.md" or "feedback.md")
        {
            string here = Path.GetDirectoryName(full)!, rel = Ui.Quote(cls, "") + "/" + Ui.Quote(Path.GetRelativePath(root, here).Replace('\\', '/'), "/");
            var links = new List<string>();
            if (Path.GetFileName(full) == "feedback.md") links.Add($"<a class=\"btn\" href=\"/files/{rel}/spec.md\">Instructions</a>");
            else if (File.Exists(Path.Combine(here, "feedback.md"))) links.Add($"<a class=\"btn\" href=\"/files/{rel}/feedback.md\">My submission and feedback</a>");
            string spec = File.Exists(Path.Combine(here, "spec.md")) ? File.ReadAllText(Path.Combine(here, "spec.md")) : "";
            if (System.Text.RegularExpressions.Regex.Match(spec, "^url: (https?://\\S+)$", System.Text.RegularExpressions.RegexOptions.Multiline) is { Success: true } m)
                links.Add($"<a class=\"btn\" href=\"{Ui.Esc(m.Groups[1].Value)}\">Open in Canvas</a>");
            bar = $"<div class=\"toolbar\" style=\"margin:0 0 1.2rem\">{string.Concat(links)}</div>";
        }
        if (text.StartsWith("---\n", StringComparison.Ordinal) && text.IndexOf("\n---\n", 4, StringComparison.Ordinal) is int end and > 0) text = text[(end + 5)..];
        // Links between the mirrored files are relative: point them at this same page.
        string dir = Path.GetRelativePath(root, Path.GetDirectoryName(full)!).Replace('\\', '/');
        string html = Ui.RenderMd(text).Replace("href=\"", "href=\"\u0001").Replace("href=\"\u0001http", "href=\"http").Replace("href=\"\u0001#", "href=\"#")
            .Replace("href=\"\u0001mailto", "href=\"mailto").Replace("\u0001", $"/files/{Ui.Quote(cls, "")}/{(dir == "." ? "" : Ui.Quote(dir, "/") + "/")}");
        string title = text.Split('\n').FirstOrDefault(l => l.StartsWith("# ", StringComparison.Ordinal))?[2..].Trim() is { Length: > 0 } h ? h : Path.GetFileNameWithoutExtension(full);
        return Show(title, $"{bar}<article class=\"prose\">{html}</article>", c, math: true);
    }

    string CanvasSettingsGroup(string? flash)
    {
        var s = Canvas.Settings;
        string say = flash switch
        {
            FindOutcome.Found => $"<div class=\"notice good\"><div>{Ui.Esc(FindOutcome.Say(FindOutcome.Found))}</div></div>",
            FindOutcome.NoAddress or FindOutcome.SignedOut or FindOutcome.NoExtension or FindOutcome.Away or FindOutcome.Other =>
                $"<div class=\"notice\"><div>{Ui.Esc(FindOutcome.Say(flash, flash == FindOutcome.Other ? lastFindError : ""))}</div></div>",
            "saved" => "<div class=\"notice good\"><div>Saved. Canvas syncs within a minute while Chrome is open.</div></div>",
            "scout" => "<div class=\"notice good\"><div>Exploring. It takes a few minutes; what it finds lands in the class's Canvas folder.</div></div>",
            _ => "",
        };
        if (s.Error.Length > 0) say += $"<div class=\"notice\"><div>{Ui.Esc(s.Error)}</div></div>";
        var titles = CourseNames.Of(s);
        string CourseSelect(string cls)
        {
            long have = s.Courses.GetValueOrDefault(cls);
            if (s.Available.Count == 0)
                return $"<input type=\"number\" name=\"canvas_{Ui.Esc(cls)}\" value=\"{(have > 0 ? have.ToString(CultureInfo.InvariantCulture) : "")}\" placeholder=\"Canvas course id\" style=\"width:9rem\">";
            var opts = "<option value=\"\">Not on Canvas</option>" + string.Concat(titles.OrderBy(kv => kv.Value, StringComparer.OrdinalIgnoreCase).Select(kv =>
                $"<option value=\"{Ui.Esc(kv.Key)}\"{(kv.Key == have.ToString(CultureInfo.InvariantCulture) ? " selected" : "")}>{Ui.Esc(CourseLabel(s, kv.Key, kv.Value))}</option>"));
            if (have > 0 && !s.Available.ContainsKey(have.ToString(CultureInfo.InvariantCulture))) opts += $"<option value=\"{have}\" selected>Course {have}</option>";
            return $"<select name=\"canvas_{Ui.Esc(cls)}\">{opts}</select>";
        }
        string rows = string.Concat(cfg.ClassNames().Select(cls => $"<div class=\"row\"><span class=\"grow\">{Ui.Esc(cls)}</span>{CourseSelect(cls)}</div>"));
        // The course scout: what it found per class, and a way to explore again.
        string scouts = "";
        if (options.Scout is { } scout && s.Courses.Count > 0)
        {
            var items = s.Courses.Keys.Select(cls =>
            {
                string state = scout.Running == cls ? "Exploring now…" : scout.Waiting.Contains(cls) ? "Waiting its turn"
                    : s.Scouts.TryGetValue(cls, out var r) ? (r.Ok ? $"Explored {Ui.Esc(DateTimeOffset.TryParse(r.When, out var w) ? w.LocalDateTime.ToString("d MMM", CultureInfo.InvariantCulture) : "")}, {r.Files} file(s)" : "Didn't finish: " + Ui.Esc(Py.Head(r.Report, 140)))
                    : "Not explored yet";
                string report = s.Scouts.TryGetValue(cls, out var rr) && rr.Ok && rr.Report.Length > 0 ? $"<div class=\"subtitle\">{Ui.Esc(Py.Head(rr.Report, 300))}</div>" : "";
                return $"<form class=\"row\" method=\"post\" action=\"/settings/canvas/scout\"><input type=\"hidden\" name=\"class\" value=\"{Ui.Esc(cls)}\">"
                    + $"<div class=\"grow\"><div class=\"title\">{Ui.Esc(cls)}</div><div class=\"subtitle\">{state}</div>{report}</div><button>Explore</button></form>";
            });
            scouts = "<div class=\"group-head\">Explore each class with AI</div><div class=\"group\">" + string.Concat(items) + "</div>"
                + "<p class=\"group-foot\">Every instructor lays Canvas out differently. The AI looks through a class's Canvas (the syllabus, files "
                + "outside modules, links to Box or Google Drive), saves what the sync misses, and writes a short guide to where things are.</p>";
        }
        string synced = DateTimeOffset.TryParse(s.LastSync, out var t) ? t.LocalDateTime.ToString("ddd d MMM, h:mm tt", CultureInfo.InvariantCulture) : "Never";
        string seen = s.ExtensionConnected(DateTimeOffset.Now) ? "Connected"
            : refusedAt is not null || s.ExtensionSeen.Length > 0 && !s.LastKeyMatches ? "Chrome has an old key: connect it again"
            : s.SeenWithKey.Length > 0 ? "Not lately (is Chrome open?)" : "Not set up";
        string folder = Extension.Folder(cfg.Home);
        string where = Extension.Ready(folder) ? $"<code>{Ui.Esc(folder)}</code>" : $"<code>{Ui.Esc(folder)}</code> (not ready: restart Study Stash)";
        return $"<div class=\"group-head\" id=\"canvas\">Canvas</div>{say}<form method=\"post\" action=\"/settings/canvas\"><div class=\"group\">"
            + $"<div class=\"row\"><label class=\"grow\" for=\"canvas_url\">Your school's Canvas</label><input id=\"canvas_url\" name=\"canvas_url\" type=\"text\" value=\"{Ui.Esc(s.Url)}\" placeholder=\"school.instructure.com\" style=\"width:16rem\"></div>"
            + rows
            + $"<div class=\"row\"><span class=\"grow\">Chrome extension</span><span class=\"value\">{seen}</span></div>"
            + $"<div class=\"row\"><span class=\"grow\">Last sync</span><span class=\"value\">{Ui.Esc(synced)}</span></div>"
            + "</div><div class=\"actions\"><button class=\"primary\">Save and sync</button>"
            + "<button formaction=\"/settings/canvas/find\">Find my courses</button></div></form>"
            + "<p class=\"group-foot\">Canvas is read through Chrome with your own sign-in, so no Canvas token is needed. It only reads: "
            + "assignments and instructions, your submissions and feedback, modules, files and announcements, into each class's Canvas folder.</p>"
            + scouts
            + "<details class=\"help\"><summary>Set up the Chrome extension on this computer</summary><div class=\"group\">"
            + $"<div class=\"row\"><span class=\"grow\">The extension's folder is ready: {where}</span></div>"
            + "<div class=\"row\"><span class=\"grow\">1. In Chrome, open chrome://extensions and turn on Developer mode</span></div>"
            + "<div class=\"row\"><span class=\"grow\">2. Click Load unpacked and pick that folder, or drag the folder onto the Extensions page</span></div>"
            + "</div><p class=\"group-foot\">On a laptop that reaches this library from elsewhere, set it up from the Study Stash app's Settings instead.</p></details>";
    }

    async Task<IResult> SaveCanvas(HttpContext ctx) => await WithMemberAsync(ctx, async _ =>
    {
        var f = await Http.FormAsync(ctx.Request);
        string typed = Py.Strip(f.Get("canvas_url"));
        CanvasSettings.Update(cfg.Home, s =>
        {
            if (typed.Length == 0) s.Url = "";
            else if (CanvasSettings.CleanUrl(typed) is string u) s.Url = u;
            foreach (string cls in cfg.ClassNames())
            {
                if (!f.ContainsKey("canvas_" + cls)) continue;
                if (long.TryParse(Py.Strip(f.Get("canvas_" + cls)), out long id) && id > 0) s.Courses[cls] = id;
                else s.Courses.Remove(cls);
            }
            s.SyncNow = true;
        });
        EnsureHere(); // its Canvas address may have changed
        Canvas.Nudge();
        return Http.SeeOther("/settings?canvas=saved#canvas");
    });
}
