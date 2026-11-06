using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using StudyStash.Core;
using StudyStash.Core.Canvas;
using StudyStash.Core.Filing;

namespace StudyStash.Library;

/// <summary>
/// Submit &amp; Organize for the app (test builds only for now): the student's choices and folders, the hand-ins a sync
/// saw, what was found for one (the file handed in, the files worked from), filing it, and the runs filed with Undo.
/// Every folder is looked in by this process, the one that later reads it, so a Mac asks about it as it's added.
/// </summary>
public sealed partial class LibraryWeb
{
    FilingSettings FilingNow() => CanvasSettings.Load(cfg.Home).Filing ?? new FilingSettings();

    static JsonArray Texts(IEnumerable<string> items) => [.. items.Select(i => (JsonNode)i)];

    /// <summary>The hand-ins waiting for the student's say ("Ask me"): none while tidying up is off, or set to happen by itself.</summary>
    JsonArray WaitingJson()
    {
        var f = FilingNow();
        if (!f.On || f.When != FilingSettings.Ask) return [];
        return [.. f.Waiting.Select(w => (JsonNode)new JsonObject { ["class"] = w.Class, ["key"] = w.Key, ["assignment"] = w.Assignment })];
    }

    JsonObject FilingJson()
    {
        var f = FilingNow();
        return new JsonObject
        {
            ["on"] = f.On,
            ["folders"] = Texts(f.Folders),
            ["suggested"] = new JsonArray([.. FilingFolders.Suggested().Where(s => !f.Folders.Contains(s.Path, StringComparer.OrdinalIgnoreCase))
                .Select(s => (JsonNode)new JsonObject { ["name"] = s.Name, ["path"] = s.Path })]),
            ["when"] = f.When,
            ["mode"] = f.Mode,
            ["waiting"] = WaitingJson(),
            ["runs"] = new JsonArray([.. store.FiledRuns().Select(r => (JsonNode)new JsonObject
            {
                ["run"] = r.Run, ["at"] = r.At, ["class"] = r.ClassName, ["assignment"] = r.Assignment, ["files"] = r.Files, ["undone"] = r.Undone,
            })]),
        };
    }

    /// <summary>The hand-in a sync saw, as its class's index has it now; null when it isn't there any more.</summary>
    HandedIn? HandInOf(string cls, string key) =>
        cfg.ClassNames().Contains(cls) && CourseIndex.Load(cfg.Home, cls) is { } index ? HandedIn.In(index).FirstOrDefault(h => h.Key == key) : null;

    static string Sure(Sure s) => s switch { Core.Filing.Sure.High => "high", Core.Filing.Sure.Medium => "medium", _ => "low" };

    static string How(UndoOutcome o) => o switch
    {
        UndoOutcome.Back => "back",
        UndoOutcome.Beside => "beside",
        UndoOutcome.InTrash => "in_trash",
        UndoOutcome.Missing => "missing",
        _ => "failed",
    };

    void MapFiling(WebApplication app)
    {
        app.MapGet("/api/v2/canvas/filing", (HttpContext ctx) => Api(ctx, () => Http.Json(FilingJson())));

        // The choices, and a folder added or taken off. A folder is looked in as it's added (FilingFolders.Look).
        app.MapPost("/api/v2/canvas/filing", Http.Handle(ctx => ApiAsync(ctx, async () =>
        {
            var body = await Http.JsonBodyAsync(ctx.Request);
            string add = S(body?["add"]) is { Length: > 0 } given ? Path.TrimEndingDirectorySeparator(Path.GetFullPath(given)) : "";
            if (add.Length > 0 && (FilingFolders.Refuse(add, Py.UserHome(), cfg.PoolDir) ?? FilingFolders.Look(add)) is { } why) return Http.Detail(400, why);
            CanvasSettings.Update(cfg.Home, s =>
            {
                var f = s.Filing ??= new FilingSettings();
                if (body?["on"] is JsonValue on && on.TryGetValue(out bool yes)) f.On = yes;
                if (S(body?["when"]) is FilingSettings.Ask or FilingSettings.Later) f.When = S(body?["when"]);
                if (S(body?["mode"]) is FilingSettings.Organize or FilingSettings.Clean) f.Mode = S(body?["mode"]);
                if (add.Length > 0 && !f.Folders.Contains(add, StringComparer.OrdinalIgnoreCase)) f.Folders.Add(add);
                if (S(body?["remove"]) is { Length: > 0 } remove) f.Folders.RemoveAll(p => p.Equals(remove, StringComparison.OrdinalIgnoreCase));
                if (!f.On) f.Waiting.Clear();
            });
            return Http.Json(FilingJson());
        })));

        // What was found for a hand-in, in the approved folders only: the file handed in (its bytes checked against
        // Canvas's copy) and the files worked from, each with why and how sure.
        app.MapGet("/api/v2/canvas/filing/check", (HttpContext ctx, string? @class, string? key) => ApiAsync(ctx, async () =>
        {
            if (HandInOf(@class ?? "", key ?? "") is not { } work) return Http.Detail(409, "That hand-in isn't on Canvas any more.");
            var f = FilingNow();
            var file = work.Files[0];
            var match = await SubmitMatch.FindAsync(file, f.Folders, cfg.PoolDir,
                SubmitMatch.CanvasSha(file, store.ClassDir(work.Class), Canvas.BytesAsync), ctx.RequestAborted);
            var materials = SubmitMatch.Materials(work, CourseIndex.Load(cfg.Home, work.Class), f.Folders, cfg.PoolDir, match.Path is { } p ? [p] : []);
            return Http.Json(new JsonObject
            {
                ["class"] = work.Class, ["key"] = work.Key, ["assignment"] = work.Assignment.Name, ["mode"] = f.Mode,
                ["folders"] = Texts(f.Folders),
                ["file"] = new JsonObject
                {
                    ["name"] = file.Name, ["path"] = match.Path,
                    ["found"] = match.Kind switch { Found.Exact => "exact", Found.Unsure => "unsure", _ => "none" },
                },
                ["materials"] = new JsonArray([.. materials.Select(m => (JsonNode)new JsonObject { ["path"] = m.Path, ["why"] = m.Why, ["sure"] = Sure(m.Sure) })]),
            });
        }));

        // File it: the file handed in into Submitted/, the ticked files into Materials/ or (clean) the Trash. Nothing
        // moves before this.
        app.MapPost("/api/v2/canvas/filing/file", Http.Handle(ctx => ApiAsync(ctx, async () =>
        {
            var body = await Http.JsonBodyAsync(ctx.Request);
            if (HandInOf(S(body?["class"]), S(body?["key"])) is not { } work) return Http.Detail(409, "That hand-in isn't on Canvas any more.");
            var f = FilingNow();
            string? file = S(body?["file"]) is { Length: > 0 } given ? given : null;
            var materials = (body?["materials"] as JsonArray ?? []).Select(S).Where(m => m.Length > 0).ToList();
            bool clean = body?["clean"] is JsonValue c && c.TryGetValue(out bool yes) && yes;
            var filer = new SubmitFiler(store, f.Folders, files: Files);
            var done = clean ? await filer.CleanAsync(work, file, materials) : await filer.OrganizeAsync(work, file, materials);
            CanvasSettings.Update(cfg.Home, s => s.Filing?.Waiting.RemoveAll(w => w.Class == work.Class && w.Key == work.Key));
            return Http.Json(new JsonObject
            {
                ["run"] = done.Run, ["class"] = work.Class, ["assignment"] = work.Assignment.Name, ["filed"] = done.Moves.Count,
                ["left"] = new JsonArray([.. done.Left.Select(l => (JsonNode)new JsonObject { ["path"] = l.Path, ["why"] = l.Why })]),
            });
        })));

        app.MapPost("/api/v2/canvas/filing/undo", Http.Handle(ctx => ApiAsync(ctx, async () =>
        {
            var body = await Http.JsonBodyAsync(ctx.Request);
            string run = S(body?["run"]);
            if (run.Length == 0 || store.FiledMoves(run).Count == 0) return Http.Detail(409, "Syft Notes has nothing to put back for that.");
            var items = new SubmitFiler(store, FilingNow().Folders, files: Files).Undo(run);
            return Http.Json(new JsonObject
            {
                ["items"] = new JsonArray([.. items.Select(i => (JsonNode)new JsonObject { ["from"] = i.Original, ["to"] = i.PutAt, ["how"] = How(i.How) })]),
            });
        })));
    }
}
