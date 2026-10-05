using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using StudyStash.Core;
using StudyStash.Core.Ai;

namespace StudyStash.Library;

/// <summary>Starting the library when its computer starts: the app's login item there. The library alone (`serve`
/// from a terminal) has none to offer.</summary>
public sealed record LoginSwitch(Func<bool> IsOn, Action<bool> Set);

/// <summary>
/// /api/v2/settings: everything the library's Settings page holds, for the app, so a laptop changes its library
/// from its own Settings and the web page is only a fallback. The name and password, the classes (other names, what
/// each covers, its folder), writing and sorting notes and the local models, the folders it may read, how laptops
/// reach it, starting at login on its computer, and updates. (The AI engines, AI tool access and Canvas have routes
/// of their own.) Every call takes the laptop's key, like the rest of /api/v2.
/// </summary>
public sealed partial class LibraryWeb
{
    void MapSettings(WebApplication app)
    {
        app.MapGet("/api/v2/settings", Http.Handle(ctx => ApiAsync(ctx, async () => Http.Json(await SettingsJsonAsync()))));
        app.MapPost("/api/v2/settings", Http.Handle(ctx => ApiAsync(ctx, async () =>
        {
            var body = await Http.JsonBodyAsync(ctx.Request);
            if (body is null) return Http.Detail(422, "send the settings to change as a JSON object");
            return ChangeSettings(body) is { } refused ? refused : Http.Json(await SettingsJsonAsync());
        })));
        // A class's own folders: link one on this computer (a project's repo, say) or let it go. The folder stays where
        // it is; it's listed on the class's home, searched, and read by the AI like the folders in Settings.
        app.MapPost("/api/v2/classes/folders", Http.Handle(ctx => ApiAsync(ctx, async () =>
        {
            var body = await Http.JsonBodyAsync(ctx.Request);
            string cls = Text(body?["class"]) ?? "", typed = Text(body?["path"]) ?? "";
            bool remove = Bool(body?["remove"]) == true;
            if (cfg.Classes.All(c => c.Name != cls)) return Http.Detail(404, $"There's no class called {cls}.");
            if (typed.Trim().Length == 0) return Http.Detail(400, "Which folder?");
            string full = Path.GetFullPath(Py.ExpandUser(typed.Trim()));
            var list = Folders.Load(cfg.Home);
            if (remove) list.RemoveAll(f => f.Class == cls && Path.GetFullPath(f.Path) == full);
            else
            {
                if (!Directory.Exists(full)) return Http.Detail(400, "There's no folder there on the library's computer.");
                if (full.StartsWith(Path.GetFullPath(cfg.PoolDir), StringComparison.Ordinal))
                    return Http.Detail(400, "That folder is in the library already.");
                list.RemoveAll(f => Path.GetFullPath(f.Path) == full);
                list.Add(new ReadFolder(full, Path.GetFileName(full.TrimEnd(Path.DirectorySeparatorChar)), Class: cls));
            }
            Folders.Save(cfg.Home, list);
            _ = Files.UpdateAsync(Console.WriteLine);
            return Http.Json(Reader.Overview());
        })));
        // A class's sidebar folder: "" puts it back under Classes.
        app.MapPost("/api/v2/classes/group", Http.Handle(ctx => ApiAsync(ctx, async () =>
        {
            var body = await Http.JsonBodyAsync(ctx.Request);
            string cls = Text(body?["class"]) ?? "", group = (Text(body?["group"]) ?? "").Trim();
            if (cfg.Classes.FirstOrDefault(c => c.Name == cls) is not { } found) return Http.Detail(404, $"There's no class called {cls}.");
            if (group.Length > 40) return Http.Detail(400, "Keep a folder's name to 40 characters.");
            if (group.Equals("Classes", StringComparison.OrdinalIgnoreCase)) group = "";
            found.Group = group;
            Configs.Save(cfg);
            return Http.Json(Reader.Overview());
        })));
        app.MapPost("/api/v2/settings/password", Http.Handle(ctx => ApiAsync(ctx, async () =>
        {
            string password = (await Http.JsonBodyAsync(ctx.Request))?["password"] is JsonValue v && v.TryGetValue(out string? p) ? p.Trim() : "";
            if (password.Length < 4) return Http.Detail(400, "Use a password of at least 4 characters.");
            string old = cfg.PoolPassword;
            cfg.PoolPassword = password;
            Configs.Save(cfg);
            // The library's own computer reaches it with the password too: keep that working.
            var here = Configs.LoadClient(cfg.Home);
            if (here.ServerUrl.Length > 0 && here.PoolKey == old && old.Length > 0)
            {
                here.PoolKey = password;
                Configs.SaveClient(here);
            }
            return Http.Json(new JsonObject { ["has_password"] = true });
        })));
        app.MapPost("/api/v2/settings/update", Http.Handle(ctx => ApiAsync(ctx, async () =>
        {
            var rel = await options.Latest(0);
            bool updating = Updates.IsNewer(rel) && options.Apply is not null;
            if (updating) _ = Task.Run(() => options.Apply!(rel!, cfg.Home));
            return Http.Json(new JsonObject { ["updating"] = updating, ["latest"] = rel?.Tag });
        })));
        app.MapPost("/api/v2/settings/rewrite-all", (HttpContext ctx) => Api(ctx, () =>
        {
            int n = store.RequeueAll();
            pipeline.Wake();
            return Http.Json(new JsonObject { ["queued"] = n });
        }));
        // "Use Canvas course names": the preview, and doing it (for every class in the preview, or the ones named in
        // "classes"). The answer says what was renamed, so the app can follow on its own computer.
        app.MapGet("/api/v2/settings/course-names", (HttpContext ctx) => Api(ctx, () => Http.Json(CourseNamesJson())));
        app.MapPost("/api/v2/settings/course-names", Http.Handle(ctx => ApiAsync(ctx, async () =>
        {
            var body = await Http.JsonBodyAsync(ctx.Request);
            var only = body?["classes"] is JsonArray list ? list.Select(n => n?.GetValue<string>()).OfType<string>().ToHashSet() : null;
            return Http.Json(UseCourseNames(only));
        })));
    }

    /// <summary>The classes "Use Canvas course names" would rename, each with how many lectures move with it, and
    /// why it can't right now (null when it can).</summary>
    JsonObject CourseNamesJson()
    {
        var counts = store.ClassesSummary().ToDictionary(c => c.ClassName, c => c.Count);
        return new JsonObject
        {
            ["blocked"] = ClassRename.Blocked(Canvas.Crawl),
            ["renames"] = new JsonArray(ClassRename.Plan(cfg, Canvas.Settings).Select(st => (JsonNode?)new JsonObject
            {
                ["from"] = st.From, ["to"] = st.To, ["code"] = st.Code, ["lectures"] = counts.GetValueOrDefault(st.From, 0),
            }).ToArray()),
        };
    }

    /// <summary>Rename the planned classes (or just <paramref name="only"/>) to their Canvas course names.</summary>
    JsonObject UseCourseNames(IReadOnlySet<string>? only)
    {
        var plan = ClassRename.Plan(cfg, Canvas.Settings).Where(st => only is null || only.Contains(st.From)).ToList();
        var outcome = ClassRename.Apply(cfg, store, plan, Canvas.Crawl);
        if (outcome.Renamed.Count > 0)
        {
            Console.WriteLine($"[classes] renamed to their Canvas course names: {string.Join(", ", outcome.Renamed.Select(st => $"{st.From} → {st.To}"))} ({outcome.Lectures} lectures moved)");
            pipeline.Wake();
        }
        var result = CourseNamesJson();
        result["renamed"] = new JsonArray(outcome.Renamed.Select(st => (JsonNode?)new JsonObject { ["from"] = st.From, ["to"] = st.To }).ToArray());
        result["lectures"] = outcome.Lectures;
        result["problem"] = outcome.Problem;
        return result;
    }

    async Task<JsonObject> SettingsJsonAsync()
    {
        var models = await options.ListModels(cfg.OllamaHost);
        var ts = options.Tailscale();
        var rel = await options.Latest(3600);
        var picked = AiSettings.Load(cfg.Home);
        // What sorting follows: the main AI, or the notes engine while Ollama has no model to sort with (AiJobs.SortAsync).
        string sortsWith = await AiJobs.SortFollowsNotesAsync(picked, cfg, Jobs.Checks) ?? picked.Provider;
        var counts = store.ClassesSummary().ToDictionary(c => c.ClassName, c => c.Count);
        bool? atLogin = null;
        try
        {
            atLogin = options.StartAtLogin?.IsOn();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
        }
        return new JsonObject
        {
            ["name"] = cfg.PoolName,
            ["has_password"] = cfg.PoolPassword.Length > 0,
            ["notes_folder"] = cfg.PoolDir,
            ["lectures"] = counts.Values.Sum(),
            ["classes"] = new JsonArray(cfg.Classes.Select(c => (JsonNode?)new JsonObject
            {
                ["name"] = c.Name, ["aliases"] = new JsonArray(c.Aliases.Select(a => (JsonNode?)a).ToArray()), ["description"] = c.Description,
                ["group"] = c.Group,
                ["folder"] = store.ClassDir(c.Name), ["lectures"] = counts.GetValueOrDefault(c.Name, 0),
            }).ToArray()),
            ["course_names"] = CourseNamesJson(),
            ["notes"] = new JsonObject
            {
                ["write"] = cfg.SummaryEnabled, ["sort"] = cfg.OllamaEnabled, ["min_confidence"] = cfg.MinConfidence, ["writer"] = NotesWriter,
            },
            ["ollama"] = new JsonObject
            {
                ["host"] = cfg.OllamaHost,
                ["answering"] = models is not null,
                ["models"] = new JsonArray((models ?? []).Select(m => (JsonNode?)new JsonObject { ["name"] = m.Name, ["size"] = Ollama.SizeLabel(m.SizeGb) }).ToArray()),
                ["summary_model"] = cfg.SummaryModel,
                ["sort_model"] = cfg.OllamaModel,
                ["recommended"] = Ollama.RecommendedModel(options.RamGb()),
            },
            // Which AI sorts lectures into classes (the web page's "Sorts lectures"): its own, or "" for the library's main one.
            ["sorting"] = new JsonObject
            {
                ["engine"] = picked.ByJob.TryGetValue("sort", out var sorts) ? sorts.Provider : "",
                ["default"] = AiProviders.All(() => cfg.OllamaHost).FirstOrDefault(p => p.Id == sortsWith)?.Name ?? sortsWith,
                ["engines"] = new JsonArray(AiProviders.All(() => cfg.OllamaHost).Select(p => (JsonNode?)new JsonObject
                {
                    ["id"] = p.Id, ["name"] = p.Name, ["installed"] = p.Available(),
                }).ToArray()),
            },
            ["terminal"] = new JsonObject
            {
                ["current"] = picked.Terminal,
                ["choices"] = new JsonArray(Terminal.Available().Select(t => (JsonNode?)new JsonObject { ["id"] = t.Id, ["name"] = t.Name }).ToArray()),
            },
            ["folders"] = new JsonArray(Folders.Load(cfg.Home).Select(f => (JsonNode?)new JsonObject
            {
                ["name"] = f.Name, ["path"] = f.Path, ["ai"] = f.Ai, ["private"] = f.Private, ["class"] = f.Class,
            }).ToArray()),
            ["files"] = Files.Files,
            ["reach"] = new JsonObject
            {
                ["addresses"] = new JsonArray(HostInfo.ServerUrls(cfg.WebPort, ts, options.HostName()).Select(u => (JsonNode?)u).ToArray()),
                ["tailscale"] = ts.Running,
                ["port"] = cfg.WebPort,
                // Listening to the network, so laptops can reach it (not a one-computer library, on this computer alone).
                ["laptops"] = !(System.Net.IPAddress.TryParse(cfg.WebHost, out var ip) && System.Net.IPAddress.IsLoopback(ip)),
            },
            ["start_at_login"] = atLogin,
            ["updates"] = new JsonObject
            {
                ["version"] = Engine.Version,
                ["latest"] = rel?.Tag,
                ["newer"] = Updates.IsNewer(rel),
                ["page"] = rel?.Page,
                ["auto"] = cfg.AutoUpdate,
                ["can_update"] = options.Apply is not null,
            },
        };
    }

    static bool? Bool(JsonNode? node) => node is JsonValue v && v.TryGetValue(out bool b) ? b : null;

    static string? Text(JsonNode? node) => node is JsonValue v && v.TryGetValue(out string? s) ? s.Trim() : null;

    /// <summary>The classes a new class list renames, each from the name it had to the one typed (the app's Settings and the
    /// library's own web page both send which was which): the way "Use Canvas course names" does it, a class's lectures,
    /// folder and Canvas course move with it, instead of staying behind under the old name. Its old name stays as another
    /// name in <paramref name="classes"/> (the list about to be saved), so a lecture a laptop recorded under it still files
    /// here. Null when done, or why not ("Canvas is syncing…"); some of them may be done by then, and stay done.</summary>
    string? RenameClasses(List<ClassRenameStep> renames, List<ClassDef> classes)
    {
        var outcome = ClassRename.Apply(cfg, store, renames, Canvas.Crawl);
        if (outcome.Renamed.Count > 0)
        {
            Console.WriteLine($"[classes] renamed in Settings: {string.Join(", ", outcome.Renamed.Select(st => $"{st.From} → {st.To}"))} ({outcome.Lectures} lectures moved)");
            pipeline.Wake();
        }
        foreach (var st in outcome.Renamed)
            if (classes.FirstOrDefault(c => c.Name == st.To) is { } sent && cfg.Classes.FirstOrDefault(c => c.Name == st.To) is { } kept)
                sent.Aliases = [.. sent.Aliases.Concat(kept.Aliases).Distinct(StringComparer.OrdinalIgnoreCase)];
        return outcome.Problem;
    }

    /// <summary>Changes what the body names and leaves the rest; null when done, or why not (nothing is changed then).</summary>
    IResult? ChangeSettings(JsonObject body)
    {
        // Check everything first, so a refused change leaves the library as it was.
        string? name = Text(body["name"]);
        if (name is not null && (name.Length == 0 || name.Length > 80)) return Http.Detail(400, "Give the library a name (up to 80 characters).");
        List<ClassDef>? classes = null;
        // A class sent with the name it had ("was") under a new one is a rename: its lectures, folder and Canvas course
        // go with it (as "Use Canvas course names" does), rather than staying behind under the old name.
        var renames = new List<ClassRenameStep>();
        if (body["classes"] is JsonArray list)
        {
            classes = [];
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in list.OfType<JsonObject>())
            {
                string cls = Text(item["name"]) ?? "";
                if (cls.Length == 0) continue;
                if (cls.Length > 60) return Http.Detail(400, $"“{cls[..20]}…” is too long for a class name (up to 60 characters).");
                if (cls.Equals(Configs.Unsorted, StringComparison.OrdinalIgnoreCase)) return Http.Detail(400, $"“{Configs.Unsorted}” is taken: lectures wait there to be filed.");
                if (!seen.Add(cls)) return Http.Detail(400, $"There are two classes called {cls}.");
                var aliases = (item["aliases"] as JsonArray)?.Select(Text).OfType<string>().Where(a => a.Length > 0).Distinct().ToList() ?? [];
                // A class's sidebar folder: as sent, else what it had (under this name, or the one it's renamed from).
                string group = Text(item["group"]) ?? cfg.Classes.FirstOrDefault(c => c.Name == cls || c.Name == Text(item["was"]))?.Group ?? "";
                classes.Add(new ClassDef(cls, aliases, Text(item["description"]) ?? "", group.Trim()));
                if (Text(item["was"]) is { Length: > 0 } was && was != cls && cfg.Classes.Any(c => c.Name == was)) renames.Add(new ClassRenameStep(was, cls, 0, ""));
            }
            if (renames.Count > 0 && ClassRename.Blocked(Canvas.Crawl) is { } busy) return Http.Detail(409, busy);
        }
        double? confidence = body["notes"]?["min_confidence"] is JsonValue cv && cv.TryGetValue(out double d) && !double.IsNaN(d) ? Math.Clamp(d, 0, 1) : null;
        string? sortEngine = Text(body["sort_engine"]);
        if (sortEngine is { Length: > 0 } && AiProviders.All().All(p => p.Id != sortEngine)) return Http.Detail(400, $"There's no AI called {sortEngine}.");
        string? terminal = Text(body["terminal"]);
        if (terminal is not null && Terminal.Available().All(t => t.Id != terminal)) return Http.Detail(400, $"There's no terminal called {terminal} on the library's computer.");
        bool? atLogin = Bool(body["start_at_login"]);
        if (atLogin is not null && options.StartAtLogin is null)
            return Http.Detail(409, "This library runs without the Study Stash app, so it can't start at login from here.");
        List<ReadFolder>? folders = null;
        if (body["folders"] is JsonArray folderList || body["add_folder"] is not null)
        {
            var had = Folders.Load(cfg.Home);
            folders = body["folders"] is JsonArray given
                ? [.. given.OfType<JsonObject>().Select(f => (Path: Text(f["path"]) ?? "", Ai: Bool(f["ai"]), Private: Bool(f["private"])))
                    .Select(f => had.FirstOrDefault(h => h.Path == f.Path) is { } h ? h with { Ai = f.Ai ?? h.Ai, Private = f.Private ?? h.Private } : null)
                    .OfType<ReadFolder>()]
                : had;
            if (Text(body["add_folder"]) is { Length: > 0 } typed)
            {
                string full = Path.GetFullPath(Py.ExpandUser(typed));
                if (!Directory.Exists(full)) return Http.Detail(400, "There's no folder there on the library's computer.");
                if (folders.All(x => Path.GetFullPath(x.Path) != full)) folders.Add(new ReadFolder(full, Path.GetFileName(full.TrimEnd(Path.DirectorySeparatorChar))));
            }
        }

        if (atLogin is { } on)
        {
            try
            {
                options.StartAtLogin!.Set(on);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException or System.Security.SecurityException)
            {
                return Http.Detail(409, $"The library's computer didn't let it change starting at login: {e.Message}");
            }
        }
        if (name is not null) cfg.PoolName = name;
        if (renames.Count > 0 && RenameClasses(renames, classes!) is { } problem) return Http.Detail(409, problem);
        if (renames.Count > 0)
        {
            var linked = folders ?? Folders.Load(cfg.Home);
            folders = [.. linked.Select(f => renames.FirstOrDefault(r => r.From == f.Class) is { } r ? f with { Class = r.To } : f)];
        }
        if (classes is not null) cfg.Classes = classes;
        if (body["notes"] is JsonObject notes)
        {
            if (Bool(notes["write"]) is { } write) cfg.SummaryEnabled = write;
            if (Bool(notes["sort"]) is { } sort) cfg.OllamaEnabled = sort;
            if (confidence is { } c) cfg.MinConfidence = c;
        }
        if (body["ollama"] is JsonObject ollama)
        {
            if (Text(ollama["summary_model"]) is { } summary) cfg.SummaryModel = summary;
            if (Text(ollama["sort_model"]) is { Length: > 0 } sortModel) cfg.OllamaModel = sortModel;
        }
        if (Bool(body["auto_update"]) is { } auto) cfg.AutoUpdate = auto;
        Configs.Save(cfg);
        if (terminal is not null || sortEngine is not null)
        {
            var picked = AiSettings.Load(cfg.Home);
            if (terminal is not null) picked.Terminal = terminal;
            if (sortEngine is { Length: > 0 }) picked.ByJob["sort"] = new AiChoice(sortEngine);
            else if (sortEngine is not null) picked.ByJob.Remove("sort");
            picked.Save(cfg.Home);
        }
        if (folders is not null)
        {
            Folders.Save(cfg.Home, folders);
            _ = Files.UpdateAsync(Console.WriteLine);
        }
        return null;
    }
}
