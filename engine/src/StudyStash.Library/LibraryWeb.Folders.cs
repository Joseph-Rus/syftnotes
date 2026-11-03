using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using StudyStash.Core;

namespace StudyStash.Library;

/// <summary>A student with two library folders on this computer (<see cref="LibraryFolders"/>): what bringing in the
/// other's notes would do, and doing it (<see cref="LibraryBringIn"/>). Only a folder this computer's own look finds is
/// read, never a path a request names on its own. Every route needs the library's password, like the rest of the API.</summary>
public sealed partial class LibraryWeb
{
    void MapFolders(WebApplication app)
    {
        // What bringing in each folder would do, for the library's folder or one the student is about to use instead.
        app.MapGet(LibraryBringIn.Route, (HttpContext ctx, string? into, [FromQuery] string[]? from) => Api(ctx, () =>
        {
            var found = LibraryFolders.Find(cfg.PoolDir);
            string target = into is { Length: > 0 } && Known(found, into) is { } picked ? picked : cfg.PoolDir;
            var plans = new JsonArray();
            foreach (string f in from ?? [])
                if (Known(found, f) is { } dir && !LibraryFolders.Comparer.Equals(LibraryFolders.Real(dir), LibraryFolders.Real(target)))
                    plans.Add(LibraryBringIn.Plan(store, cfg, target, dir, Py.UserHome()).Json());
            return Http.Json(new JsonObject { ["plans"] = plans });
        }));
        // This folder's own unlisted lectures listed, then each folder's notes brought in.
        app.MapPost(LibraryBringIn.BringInRoute, Http.Handle(ctx => ApiAsync(ctx, async () =>
        {
            var body = await Http.JsonBodyAsync(ctx.Request) as JsonObject ?? [];
            var found = LibraryFolders.Find(cfg.PoolDir);
            int listed = LibraryBringIn.ListOwn(store, cfg);
            var done = new JsonArray();
            foreach (var node in body["from"] as JsonArray ?? [])
                if (Known(found, Py.AsString(node)) is { } dir && !LibraryFolders.Comparer.Equals(LibraryFolders.Real(dir), LibraryFolders.Real(cfg.PoolDir)))
                    done.Add(LibraryBringIn.Apply(store, cfg, dir, Py.UserHome()).Json());
            return Http.Json(new JsonObject { ["listed"] = listed, ["done"] = done });
        })));
    }

    /// <summary>The library folder this computer has at <paramref name="path"/>, or null when there's none there.</summary>
    static string? Known(List<LibraryFolder> found, string? path) =>
        path is { Length: > 0 } ? found.FirstOrDefault(f => LibraryFolders.Comparer.Equals(LibraryFolders.Real(f.Path), LibraryFolders.Real(path)))?.Path : null;
}
