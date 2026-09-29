using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Net.Http.Headers;
using StudyStash.Core;

namespace StudyStash.Library;

/// <summary>Bringing everything from an old library to a new one: "Use just this computer", or a library handing its
/// lectures to another before it becomes a laptop (LibraryMove, docs/one-download.md). Every route needs the library's
/// password, like the rest of the API.</summary>
public sealed partial class LibraryWeb
{
    void MapMove(WebApplication app)
    {
        // The old library: what it has, a page at a time, as it is. Nothing here is changed.
        app.MapGet(LibraryMove.LibraryRoute, (HttpContext ctx) => Api(ctx, () => Http.Json(LibraryMove.ExportLibrary(store, cfg))));
        app.MapGet(LibraryMove.Route, (HttpContext ctx, string? after, int? limit, string? lean, [FromQuery] string[]? id) => Api(ctx, () =>
            Http.Json(id is { Length: > 0 }
                ? LibraryMove.ExportOnly(store, cfg, id)
                : LibraryMove.Export(store, cfg, string.IsNullOrEmpty(after) ? null : after, limit ?? LibraryMove.PageSize, lean == "1"))));
        app.MapGet(LibraryMove.AttachmentsRoute, (HttpContext ctx, string? after, int? limit) => Api(ctx, () =>
            Http.Json(LibraryMove.ExportAttachments(store, string.IsNullOrEmpty(after) ? null : after, limit ?? LibraryMove.PageSize))));
        app.MapGet(LibraryMove.FilesRoute, (HttpContext ctx, string? after, int? limit) => Api(ctx, () =>
            Http.Json(LibraryMove.ExportFiles(store, cfg, string.IsNullOrEmpty(after) ? null : after, limit ?? 50))));
        app.MapGet(LibraryMove.FilesRoute + "/raw", (HttpContext ctx, string? path) => Api(ctx, () =>
            LibraryMove.LibraryFile(cfg, path) is { } file ? Results.File(file, "application/octet-stream") : Http.Detail(404, "no such file")));
        app.MapGet(LibraryMove.ChatsRoute, (HttpContext ctx, string? after, int? limit) => Api(ctx, () =>
            Http.Json(LibraryMove.ExportChats(cfg.Home, string.IsNullOrEmpty(after) ? null : after, limit ?? LibraryMove.PageSize))));

        // The new library: takes what came as it came, with no AI run again; what it has already is never touched.
        app.MapPost(LibraryMove.LibraryRoute, Http.Handle(ctx => ApiAsync(ctx, async () =>
            await Http.JsonBodyAsync(ctx.Request) is { } body ? Http.Json(LibraryMove.ImportLibrary(cfg, body)) : Http.Detail(400, "the library's classes, please"))));
        app.MapPost(LibraryMove.Route + LibraryMove.Wanted, Http.Handle(ctx => ApiAsync(ctx, async () =>
            await Http.JsonBodyAsync(ctx.Request) is { } body ? Http.Json(LibraryMove.WantedLectures(store, body)) : Http.Detail(400, "which lectures, please"))));
        app.MapPost(LibraryMove.Route, Http.Handle(ctx => ApiAsync(ctx, async () =>
        {
            // A page of long transcripts can be more than the usual limit on a request.
            if (ctx.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } limit) limit.MaxRequestBodySize = 512L * 1024 * 1024;
            if (await Http.JsonBodyAsync(ctx.Request) is not JsonObject page) return Http.Detail(400, "a page of lectures, please");
            try
            {
                var done = LibraryMove.Import(store, cfg, page);
                if (done.Writing > 0) pipeline.Wake();
                return Http.Json(new JsonObject { ["filed"] = done.Filed, ["skipped"] = done.Skipped, ["classes"] = done.Classes, ["writing"] = done.Writing });
            }
            catch (PayloadException e)
            {
                return Http.Detail(400, e.Message);
            }
        })));
        app.MapPost(LibraryMove.AttachmentsRoute + LibraryMove.Wanted, Http.Handle(ctx => ApiAsync(ctx, async () =>
            await Http.JsonBodyAsync(ctx.Request) is { } body ? Http.Json(LibraryMove.WantedAttachments(store, body)) : Http.Detail(400, "which attachments, please"))));
        app.MapPost(LibraryMove.AttachmentsRoute + "/{id}", Http.Handle(ctx => ApiAsync(ctx, () => AttachmentInAsync(ctx))));
        app.MapPost(LibraryMove.FilesRoute + LibraryMove.Wanted, Http.Handle(ctx => ApiAsync(ctx, async () =>
            await Http.JsonBodyAsync(ctx.Request) is { } body ? Http.Json(LibraryMove.WantedFiles(cfg, body)) : Http.Detail(400, "which files, please"))));
        app.MapPut(LibraryMove.FilesRoute, Http.Handle(ctx => ApiAsync(ctx, async () =>
        {
            if (ctx.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } limit) limit.MaxRequestBodySize = null;
            try
            {
                string? saved = await LibraryMove.ImportFileAsync(cfg, ctx.Request.Query["path"], ctx.Request.Query["sha256"], ctx.Request.Body, ctx.RequestAborted);
                return Http.Json(new JsonObject { ["files"] = saved is null ? 0 : 1, ["saved"] = saved });
            }
            catch (PayloadException e)
            {
                return Http.Detail(400, e.Message);
            }
            catch (IOException)
            {
                return Http.Detail(400, "the file was cut short: try again");
            }
        })));
        app.MapPost(LibraryMove.ChatsRoute, Http.Handle(ctx => ApiAsync(ctx, async () =>
        {
            if (await Http.JsonBodyAsync(ctx.Request) is not JsonObject page) return Http.Detail(400, "a page of chats, please");
            var done = LibraryMove.ImportChats(cfg.Home, page);
            return Http.Json(new JsonObject { ["chats"] = done.Chats, ["skipped"] = done.Skipped });
        })));
    }

    /// <summary>One attachment from the old library: its details (a "meta" part, JSON) then its file (a "file" part),
    /// written to disk as it arrives, never held in memory. Its words, read there, come with it; words still being read
    /// there are read here.</summary>
    async Task<IResult> AttachmentInAsync(HttpContext ctx)
    {
        if (ctx.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } limit) limit.MaxRequestBodySize = null;
        if (!MediaTypeHeaderValue.TryParse(ctx.Request.ContentType, out var media) || !media.MediaType.Equals("multipart/form-data", StringComparison.OrdinalIgnoreCase)
            || HeaderUtilities.RemoveQuotes(media.Boundary).Value is not { Length: > 0 and <= 200 } boundary)
            return Http.Detail(400, "send the attachment as multipart/form-data");
        string staging = Path.Combine(cfg.PoolDir, ".attachments-incoming", Http.TokenUrlSafe(9));
        Directory.CreateDirectory(staging);
        try
        {
            JsonObject? meta = null;
            string? staged = null;
            var reader = new MultipartReader(boundary, ctx.Request.Body);
            try
            {
                for (var section = await reader.ReadNextSectionAsync(ctx.RequestAborted); section is not null; section = await reader.ReadNextSectionAsync(ctx.RequestAborted))
                {
                    if (!ContentDispositionHeaderValue.TryParse(section.ContentDisposition, out var part)) continue;
                    string field = HeaderUtilities.RemoveQuotes(part.Name).Value ?? "";
                    if (field == "meta")
                    {
                        using var text = new StreamReader(section.Body);
                        meta = JsonNode.Parse(await text.ReadToEndAsync(ctx.RequestAborted)) as JsonObject;
                    }
                    else if (field == "file" && staged is null)
                    {
                        staged = Path.Combine(staging, "file");
                        await using var file = new FileStream(staged, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true);
                        await section.Body.CopyToAsync(file, ctx.RequestAborted);
                    }
                }
            }
            catch (Exception e) when (e is InvalidDataException or IOException or System.Text.Json.JsonException)
            {
                return Http.Detail(400, "the attachment was cut short: try again");
            }
            if (meta is null || staged is null) return Http.Detail(400, "an attachment needs its details and its file");
            if (meta["id"]?.ToString() != (string?)ctx.Request.RouteValues["id"]) return Http.Detail(400, "that's another attachment's details");
            if (meta["size"] is JsonValue sv && sv.TryGetValue(out long size) && size != new FileInfo(staged).Length)
                return Http.Detail(400, "the attachment was cut short: try again");
            try
            {
                var kept = LibraryMove.ImportAttachment(store, meta, staged);
                if (kept is { State: Attachment.Reading }) ReadLater(kept);
                return Http.Json(new JsonObject { ["files"] = kept is null ? 0 : 1 });
            }
            catch (PayloadException e)
            {
                return Http.Detail(400, e.Message);
            }
        }
        finally
        {
            try
            {
                Directory.Delete(staging, recursive: true);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
            }
        }
    }
}
