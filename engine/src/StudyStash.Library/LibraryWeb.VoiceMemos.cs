using System.Text;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Net.Http.Headers;
using StudyStash.Core;

namespace StudyStash.Library;

/// <summary>
/// /api/v2/voice-memos: a recording made with the phone's own Voice Memos, sent from the phone app, becomes a lecture.
/// The library keeps it until a computer that records takes it (<c>claim</c>), fetches it (<c>audio</c>), writes it down
/// with Whisper as a lecture of its own, and says which (<c>done</c>); that lecture then reaches the library the way
/// any recording does. The phone watches the list to say how each is getting on.
/// </summary>
public sealed partial class LibraryWeb
{
    VoiceMemos? memos;

    public VoiceMemos Memos => memos ??= new VoiceMemos(cfg.Home);

    static JsonObject MemosJson(IEnumerable<VoiceMemo> list) => new() { ["memos"] = new JsonArray([.. list.Select(m => (JsonNode)m.ToJson())]) };

    void MapVoiceMemos(WebApplication app)
    {
        app.MapPost("/api/v2/voice-memos", Http.Handle(ctx => ApiAsync(ctx, () => MemoUploadAsync(ctx))));
        app.MapGet("/api/v2/voice-memos", (HttpContext ctx) => Api(ctx, () => Http.Json(MemosJson(Memos.List()))));
        app.MapGet("/api/v2/voice-memos/{id}/audio", (HttpContext ctx, string id) => Api(ctx, () =>
            Memos.Get(id) is { } m && File.Exists(Memos.PathOf(m))
                ? Results.File(Memos.PathOf(m), "application/octet-stream", m.Name, enableRangeProcessing: true)
                : Http.Detail(404, "that recording isn't here any more")));
        app.MapPost("/api/v2/voice-memos/{id}/claim", Http.Handle(ctx => ApiAsync(ctx, async () =>
        {
            var body = await Http.JsonBodyAsync(ctx.Request);
            string id = ctx.GetRouteValue("id")?.ToString() ?? "";
            string computer = Str(body, "computer") ?? ctx.Request.Headers["X-Study-Stash-Computer"].ToString();
            if (Memos.Get(id) is null) return Http.Detail(404, "no such voice memo");
            return Memos.Claim(id, string.IsNullOrWhiteSpace(computer) ? "a computer" : Py.Head(computer.Trim(), 60)) is { } m
                ? Http.Json(m.ToJson())
                : Http.Detail(409, "another computer is already writing it down");
        })));
        app.MapPost("/api/v2/voice-memos/{id}/done", Http.Handle(ctx => ApiAsync(ctx, async () =>
        {
            var body = await Http.JsonBodyAsync(ctx.Request);
            return Memos.Finish(ctx.GetRouteValue("id")?.ToString() ?? "", Str(body, "lecture"), Str(body, "error")) is { } m
                ? Http.Json(m.ToJson())
                : Http.Detail(404, "no such voice memo");
        })));
        app.MapPost("/api/v2/voice-memos/{id}/retry", (HttpContext ctx, string id) => Api(ctx, () =>
            Memos.Retry(id) is { } m ? Http.Json(m.ToJson()) : Http.Detail(409, "only a memo that failed, with its recording still here, can be tried again")));
        app.MapDelete("/api/v2/voice-memos/{id}", (HttpContext ctx, string id) => Api(ctx, () =>
            Memos.Remove(id) ? Http.Json(MemosJson(Memos.List())) : Http.Detail(404, "no such voice memo")));
    }

    /// <summary>One recording (a part named "file"), with the class ("class") and title ("title") it should have; both
    /// may be left out (the library sorts it, and the recording's name titles it). Written to disk as it arrives.</summary>
    async Task<IResult> MemoUploadAsync(HttpContext ctx)
    {
        if (ctx.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } limit) limit.MaxRequestBodySize = VoiceMemos.MaxBytes + UploadSlack;
        if (ctx.Request.ContentLength > VoiceMemos.MaxBytes + UploadSlack) return MemoTooBig();
        if (!MediaTypeHeaderValue.TryParse(ctx.Request.ContentType, out var media) || !media.MediaType.Equals("multipart/form-data", StringComparison.OrdinalIgnoreCase)
            || HeaderUtilities.RemoveQuotes(media.Boundary).Value is not { Length: > 0 and <= 200 } boundary)
            return Http.Detail(400, "send the recording as multipart/form-data");

        Directory.CreateDirectory(Memos.Dir);
        string staged = Path.Combine(Memos.Dir, ".incoming-" + Http.TokenUrlSafe(9));
        try
        {
            string? sent = null, className = null, title = null;
            long size = 0;
            var reader = new MultipartReader(boundary, ctx.Request.Body);
            try
            {
                for (var section = await reader.ReadNextSectionAsync(ctx.RequestAborted); section is not null; section = await reader.ReadNextSectionAsync(ctx.RequestAborted))
                {
                    if (!ContentDispositionHeaderValue.TryParse(section.ContentDisposition, out var part)) continue;
                    string field = HeaderUtilities.RemoveQuotes(part.Name).Value ?? "";
                    if (part.IsFileDisposition())
                    {
                        if (field != "file" || sent is not null) continue;
                        sent = HeaderUtilities.RemoveQuotes(part.FileNameStar.HasValue ? part.FileNameStar : part.FileName).Value ?? "Voice memo.m4a";
                        (size, _) = await SaveUpToAsync(section.Body, staged, VoiceMemos.MaxBytes, ctx.RequestAborted);
                        if (size < 0) return MemoTooBig();
                    }
                    else if (field is "class" or "title")
                    {
                        using var text = new StreamReader(section.Body, Encoding.UTF8);
                        char[] buffer = new char[1024];
                        string value = new string(buffer, 0, await text.ReadBlockAsync(buffer, ctx.RequestAborted)).Trim();
                        if (field == "class") className = value.Length > 0 ? value : null;
                        else title = value.Length > 0 ? value : null;
                    }
                }
            }
            catch (BadHttpRequestException e) when (e.StatusCode == StatusCodes.Status413PayloadTooLarge)
            {
                return MemoTooBig();
            }
            catch (Exception e) when (e is InvalidDataException or IOException)
            {
                return Http.Detail(400, "the recording was cut short: try again");
            }
            if (sent is null) return Http.Detail(400, "there's no recording in it (send it as a part named \"file\")");
            if (size == 0) return Http.Detail(400, "that recording is empty");
            if (!VoiceMemos.IsAudio(sent)) return Http.Detail(415, "That isn't a recording Study Stash can read. Send an .m4a from Voice Memos, or an .mp3 or .wav.");
            if (className is not null && className != Configs.Unsorted && !cfg.ClassNames().Contains(className)) return Http.Detail(400, $"there's no class {className}");
            string by = PhoneOf(ctx)?.Name ?? (ctx.Request.Headers["X-Study-Stash-Computer"].ToString() is { Length: > 0 } c ? c : "this computer");
            var memo = Memos.Add(staged, sent, size, className == Configs.Unsorted ? null : className, title, by);
            Console.WriteLine($"[memo] {memo.Id} from {by}: {memo.Title} ({size / 1024} KB)");
            return Http.Json(memo.ToJson());
        }
        finally
        {
            try
            {
                if (File.Exists(staged)) File.Delete(staged);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    static IResult MemoTooBig() => Http.Detail(StatusCodes.Status413PayloadTooLarge, "That recording is more than 500 MB. Trim it in Voice Memos, or split it in two.");
}
