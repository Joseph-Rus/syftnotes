using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Net.Http.Headers;
using StudyStash.Core;
using StudyStash.Core.Ai;

namespace StudyStash.Library;

/// <summary>
/// Attachments: handwritten notes, slides and handouts a student adds to a lecture or a class, from the app, the phone
/// or the web. Each is kept in its class's folder (Attachments/), its words are read in the background (one at a time:
/// reading handwriting is slow), and those words help write the lecture's notes and answer questions about it.
/// </summary>
public sealed partial class LibraryWeb
{
    /// <summary>Room for the parts' own headers on top of <see cref="Attachments.MaxRequestBytes"/> of files.</summary>
    const long UploadSlack = 1024 * 1024;

    /// <summary>One document read at a time, so reading a stack of scans doesn't take the whole computer.</summary>
    readonly SemaphoreSlim readingOne = new(1, 1);

    void MapAttachments(WebApplication app)
    {
        // Attachments whose reading a restart cut short are read again.
        foreach (var a in store.UnreadAttachments()) ReadLater(a);

        app.MapPost("/api/v2/attachments", Http.Handle(ctx => ApiAsync(ctx, () => UploadAsync(ctx))));
        app.MapGet("/api/v2/attachments", (HttpContext ctx, string? @class, string? lecture) => Api(ctx, () => AttachmentList(@class, lecture)));
        app.MapGet("/api/v2/attachments/{id}/raw", (HttpContext ctx, string id) => Api(ctx, () => AttachmentFile(id)));
        app.MapGet("/api/v2/attachments/{id}/text", (HttpContext ctx, string id) => Api(ctx, () =>
            store.GetAttachment(id) is { } a
                ? Http.Json(new JsonObject { ["id"] = a.Id, ["name"] = a.Name, ["text"] = a.Text, ["reading"] = a.State == Attachment.Reading })
                : Http.Detail(404, "no such attachment")));
        app.MapDelete("/api/v2/attachments/{id}", (HttpContext ctx, string id) => Api(ctx, () =>
            store.RemoveAttachment(id) ? Http.Json(new JsonObject { ["deleted"] = id }) : Http.Detail(404, "no such attachment")));
        // The web pages' own link to a file (a browser signed in with the password, not the laptop's key).
        app.MapGet("/attachments/{id}", (HttpContext ctx, string id) => WithMember(ctx, role =>
            store.GetAttachment(id) is null ? NotFound(role, "attachment") : AttachmentFile(id)));
    }

    /// <summary>{attachments: [...]} for a lecture, a class, or everything; for a lecture also whether its notes were
    /// written without some of them (<c>rewrite</c>), so the app can offer "Rewrite notes with your attachments".</summary>
    IResult AttachmentList(string? className, string? lecture)
    {
        string? cls = string.IsNullOrWhiteSpace(className) ? null : className.Trim();
        string? note = string.IsNullOrWhiteSpace(lecture) ? null : lecture.Trim();
        var list = store.ListAttachments(cls, note);
        var result = new JsonObject { ["attachments"] = new JsonArray([.. list.Select(a => (JsonNode)a.ToJson())]) };
        if (note is not null) result["rewrite"] = RewriteOffered(note);
        return Http.Json(result);
    }

    /// <summary>The lecture has study notes written from its transcript, and an attachment with words that didn't go into
    /// them (it came after, or its words were still being read).</summary>
    bool RewriteOffered(string noteId) =>
        store.Get(noteId) is { } row && !string.IsNullOrEmpty(row.SummaryMd) && row.HasTranscript is > 0
        && row.Status is not (Store.Queued or Store.Working) && store.AttachmentsUnused(noteId);

    /// <summary>The file itself, as what its bytes say it is: PDFs and pictures open in the browser, anything else is
    /// downloaded, and nothing is ever run as a page on the library's address.</summary>
    IResult AttachmentFile(string id)
    {
        if (store.GetAttachment(id) is not { } a) return Http.Detail(404, "no such attachment");
        string path = Path.GetFullPath(store.AttachmentPath(a)), root = Path.GetFullPath(cfg.PoolDir).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!path.StartsWith(root, StringComparison.Ordinal) || !File.Exists(path)) return Http.Detail(404, "the file isn't there any more");
        return new AttachmentResult(path, a);
    }

    sealed class AttachmentResult(string path, Attachment a) : IResult
    {
        public Task ExecuteAsync(HttpContext ctx)
        {
            bool inline = Attachments.ShowsInline(a.Type);
            ctx.Response.Headers["X-Content-Type-Options"] = "nosniff";
            var disposition = new ContentDispositionHeaderValue(inline ? "inline" : "attachment");
            disposition.SetHttpFileName(a.Name);
            ctx.Response.Headers.ContentDisposition = disposition.ToString();
            return Results.File(path, inline ? a.Type : "application/octet-stream", enableRangeProcessing: true).ExecuteAsync(ctx);
        }
    }

    /// <summary>
    /// One or more files (form parts named "file"), for a lecture ("lecture": its id) or a class ("class"); with neither
    /// they wait in Unsorted. Every file is written to the library's disk as it arrives, never held in memory, and the
    /// whole request may carry up to 200 MB.
    /// </summary>
    async Task<IResult> UploadAsync(HttpContext ctx)
    {
        if (ctx.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } limit) limit.MaxRequestBodySize = Attachments.MaxRequestBytes + UploadSlack;
        if (ctx.Request.ContentLength > Attachments.MaxRequestBytes + UploadSlack) return TooBig();
        if (!MediaTypeHeaderValue.TryParse(ctx.Request.ContentType, out var media) || !media.MediaType.Equals("multipart/form-data", StringComparison.OrdinalIgnoreCase)
            || HeaderUtilities.RemoveQuotes(media.Boundary).Value is not { Length: > 0 and <= 200 } boundary)
            return Http.Detail(400, "send the files as multipart/form-data");

        string staging = Path.Combine(cfg.PoolDir, ".attachments-incoming", Http.TokenUrlSafe(9));
        Directory.CreateDirectory(staging);
        try
        {
            var files = new List<(string Sent, string Staged, long Size, byte[] Head)>();
            string? className = null, lecture = null;
            long total = 0;
            var reader = new MultipartReader(boundary, ctx.Request.Body);
            try
            {
                for (var section = await reader.ReadNextSectionAsync(ctx.RequestAborted); section is not null; section = await reader.ReadNextSectionAsync(ctx.RequestAborted))
                {
                    if (!ContentDispositionHeaderValue.TryParse(section.ContentDisposition, out var part)) continue;
                    string field = HeaderUtilities.RemoveQuotes(part.Name).Value ?? "";
                    if (part.IsFileDisposition())
                    {
                        if (field != "file") continue;
                        string staged = Path.Combine(staging, files.Count.ToString(CultureInfo.InvariantCulture));
                        var (size, head) = await SaveUpToAsync(section.Body, staged, Attachments.MaxRequestBytes - total, ctx.RequestAborted);
                        if (size < 0) return TooBig();
                        total += size;
                        files.Add((HeaderUtilities.RemoveQuotes(part.FileNameStar.HasValue ? part.FileNameStar : part.FileName).Value ?? "", staged, size, head));
                    }
                    else if (field is "class" or "lecture")
                    {
                        using var text = new StreamReader(section.Body, Encoding.UTF8);
                        char[] buffer = new char[1024];
                        string value = new string(buffer, 0, await text.ReadBlockAsync(buffer, ctx.RequestAborted)).Trim();
                        if (field == "class") className = value.Length > 0 ? value : null;
                        else lecture = value.Length > 0 ? value : null;
                    }
                }
            }
            catch (BadHttpRequestException e) when (e.StatusCode == StatusCodes.Status413PayloadTooLarge)
            {
                return TooBig();
            }
            catch (Exception e) when (e is InvalidDataException or IOException)
            {
                return Http.Detail(400, "the upload was cut short or isn't a proper form: try again");
            }

            if (files.Count == 0) return Http.Detail(400, "there's no file in it (send each as a part named \"file\")");
            if (files.FirstOrDefault(f => f.Size == 0) is { Staged: not null } empty) return Http.Detail(400, $"“{Attachments.SafeName(empty.Sent)}” is empty");
            string target;
            if (lecture is not null)
            {
                if (store.Get(lecture) is not { } row) return Http.Detail(404, "no such lecture");
                target = string.IsNullOrEmpty(row.ClassName) ? Configs.Unsorted : row.ClassName;
            }
            else if (className is not null)
            {
                if (className != Configs.Unsorted && !cfg.ClassNames().Contains(className)) return Http.Detail(400, $"there's no class {className}");
                target = className;
            }
            else target = Configs.Unsorted;

            string dir = store.AttachmentsDir(target);
            string added = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
            var kept = new JsonArray();
            foreach (var f in files)
            {
                string name = Attachments.SafeName(f.Sent);
                string file = Attachments.FreeName(dir, name);
                File.Move(f.Staged, Path.Combine(dir, file));
                var a = new Attachment(Http.TokenUrlSafe(9), name, file, target, lecture, f.Size, Attachments.Sniff(f.Head, name), added);
                store.AddAttachment(a);
                ReadLater(a);
                kept.Add(a.ToJson());
            }
            return Http.Json(new JsonObject { ["attachments"] = kept });
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

    /// <summary>"Reading your handwriting…" while an attachment's words are read.</summary>
    public const string ReadingWords = "Reading your handwriting…";

    /// <summary>The attachments part of a lecture's page (<paramref name="noteId"/>) or a class's page: each file,
    /// which opens it, what kind it is and its size; and on a lecture whose notes were written without some of them,
    /// "Rewrite notes with your attachments". Nothing at all when there are none, so the page looks as it always has.</summary>
    string AttachmentsPart(string? className, string? noteId, bool admin)
    {
        var list = store.ListAttachments(noteId is null ? className : null, noteId);
        if (list.Count == 0) return "";
        var titles = new Dictionary<string, string>();
        string Of(string id) => titles.TryGetValue(id, out var t) ? t
            : titles[id] = store.Get(id) is { } r ? FirstOf(r.LectureTitle, r.Title, "a lecture") : "a lecture";
        string rows = string.Concat(list.Select(a =>
        {
            string state = a.State == Attachment.Reading ? ReadingWords : a.HasText ? "" : "No words to read in it";
            string about = string.Join(" · ", new[]
            {
                noteId is null && a.NoteId is { } n ? Of(n) : "", Attachments.KindOf(a.Name, a.Type), Attachments.SizeLabel(a.Size), state,
            }.Where(s => s.Length > 0));
            return $"<a class=\"row\" href=\"/attachments/{Ui.Quote(a.Id, "")}\"><div class=\"grow\"><div class=\"title\">{Ui.Esc(a.Name)}</div>"
                + $"<div class=\"subtitle\">{Ui.Esc(about)}</div></div></a>";
        }));
        bool reading = list.Any(a => a.State == Attachment.Reading);
        string rewrite = noteId is not null && admin && RewriteOffered(noteId)
            ? $"<form class=\"row\" method=\"post\" action=\"/note/{Ui.Quote(noteId, "")}/resummarize\"><span class=\"grow\">Your notes were written before some of these came.</span>"
              + "<button>Rewrite notes with your attachments</button></form>"
            : "";
        return $"<h2>Attachments</h2><div class=\"group\"{(reading ? " data-refresh=\"15\"" : "")}>{rows}{rewrite}</div>";
    }

    static IResult TooBig() => Http.Detail(StatusCodes.Status413PayloadTooLarge, "That's more than 200 MB at once. Attach fewer files, or smaller ones.");

    /// <summary>Copies a part to disk, stopping once it passes <paramref name="room"/> bytes (then the size is -1).
    /// Hands back its first bytes too, to tell what it is.</summary>
    static async Task<(long Size, byte[] Head)> SaveUpToAsync(Stream body, string path, long room, CancellationToken ct)
    {
        var head = new MemoryStream();
        long size = 0;
        byte[] buffer = new byte[81920];
        await using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, buffer.Length, useAsync: true);
        int n;
        while ((n = await body.ReadAsync(buffer, ct)) > 0)
        {
            size += n;
            if (size > room) return (-1, []);
            if (head.Length < 32) head.Write(buffer, 0, (int)Math.Min(n, 32 - head.Length));
            await file.WriteAsync(buffer.AsMemory(0, n), ct);
        }
        return (size, head.ToArray());
    }

    /// <summary>Reads an attachment's words in the background, then keeps them (none: it's marked as having none).</summary>
    void ReadLater(Attachment a)
    {
        var read = options.ReadDocument ?? DocumentText.ExtractAsync;
        string path = store.AttachmentPath(a);
        _ = Task.Run(async () =>
        {
            await readingOne.WaitAsync();
            string? text = null;
            try
            {
                using var enough = new CancellationTokenSource(TimeSpan.FromMinutes(10));
                if (File.Exists(path)) text = await read(path, enough.Token);
            }
            catch (Exception e)
            {
                Console.WriteLine($"[attachments] couldn't read {a.Name}: {e.Message}");
            }
            finally
            {
                readingOne.Release();
            }
            try
            {
                store.SetAttachmentText(a.Id, text);
            }
            catch (Exception e) when (e is ObjectDisposedException or InvalidOperationException or Microsoft.Data.Sqlite.SqliteException)
            {
                // The library stopped meanwhile: it's read again when it starts.
            }
        });
    }
}
