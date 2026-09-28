using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using StudyStash.Core;

namespace StudyStash.Library;

/// <summary>Bringing lectures from an old library to a new one, when a laptop becomes the library (LibraryMove,
/// docs/one-download.md). Both routes need the library's password, like the rest of the API.</summary>
public sealed partial class LibraryWeb
{
    void MapMove(WebApplication app)
    {
        // The old library: a page of its filed lectures, notes and class as they are. Nothing here is changed.
        app.MapGet(LibraryMove.Route, (HttpContext ctx, string? after, int? limit) => Api(ctx, () =>
            Http.Json(LibraryMove.Export(store, cfg, string.IsNullOrEmpty(after) ? null : after, limit ?? LibraryMove.PageSize))));
        // The new library: files a page as it came, with no AI run again; lectures it already has are left alone.
        app.MapPost(LibraryMove.Route, Http.Handle(ctx => ApiAsync(ctx, async () =>
        {
            if (await Http.JsonBodyAsync(ctx.Request) is not JsonObject page) return Http.Detail(400, "a page of lectures, please");
            try
            {
                var done = LibraryMove.Import(store, cfg, page);
                return Http.Json(new JsonObject { ["filed"] = done.Filed, ["skipped"] = done.Skipped, ["classes"] = done.Classes });
            }
            catch (PayloadException e)
            {
                return Http.Detail(400, e.Message);
            }
        })));
    }
}
