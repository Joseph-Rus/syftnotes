using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace StudyStash.Core;

/// <summary>How far bringing lectures over has got: how many are in, of how many the old library has.</summary>
public sealed record MoveProgress(int Done, int Total);

/// <summary>What bringing lectures over did: lectures filed in the new library, ones it already had (left as they
/// are), and classes it didn't have yet.</summary>
public sealed record MoveResult(int Filed, int Skipped, int Classes);

/// <summary>
/// Bringing lectures from one library to another, when a laptop becomes the library (Settings → Your library, see
/// docs/one-download.md). The old library hands its filed lectures over a page at a time, each with its notes as
/// written, its class and who chose it (GET /api/v2/move/lectures); the new one files them as they are, with no AI
/// run again (POST /api/v2/move/lectures). It's a copy: the old library keeps every lecture, and a lecture the new one
/// already has is left alone, so doing it twice is safe.
/// </summary>
public static class LibraryMove
{
    public const string Route = "/api/v2/move/lectures";
    /// <summary>Lectures per page: transcripts can be long, so a page stays a few megabytes at most.</summary>
    public const int PageSize = 20;

    // --- the old library's side ---------------------------------------------------------------------------------

    /// <summary>One page of this library's filed lectures, ordered by id, after <paramref name="after"/> (the first
    /// page when null): <c>{classes, lectures, total, next}</c>, where <c>next</c> is the id to ask after, or null on
    /// the last page. Every page carries the classes, with their other names and what each covers.</summary>
    public static JsonObject Export(Store store, Config cfg, string? after, int limit = PageSize)
    {
        limit = Math.Clamp(limit, 1, 100);
        var all = store.ListNotes().OrderBy(r => r.Id, StringComparer.Ordinal).ToList();
        var page = all.Where(r => after is null || string.CompareOrdinal(r.Id, after) > 0).Take(limit + 1).ToList();
        bool more = page.Count > limit;
        if (more) page.RemoveAt(limit);
        var lectures = new JsonArray();
        foreach (var row in page)
        {
            var m = store.Meeting(row);
            lectures.Add(new JsonObject
            {
                ["id"] = row.Id,
                ["meeting"] = JsonNode.Parse(Wire.MeetingJson(m)),
                ["class"] = row.ClassName ?? Configs.Unsorted,
                ["confidence"] = row.Confidence ?? 0,
                ["by"] = row.ClassifiedBy ?? "none",
                ["lecture_title"] = row.LectureTitle ?? "",
                ["topics"] = JsonNode.Parse(string.IsNullOrEmpty(row.Topics) ? "[]" : row.Topics) as JsonArray ?? [],
                ["summary"] = row.SummaryMd ?? "",
                ["summary_model"] = row.SummaryModel ?? "",
            });
        }
        return new JsonObject
        {
            ["classes"] = new JsonArray(cfg.Classes.Select(c => (JsonNode?)new JsonObject
            {
                ["name"] = c.Name, ["aliases"] = new JsonArray(c.Aliases.Select(a => (JsonNode?)a).ToArray()), ["description"] = c.Description,
            }).ToArray()),
            ["lectures"] = lectures,
            ["total"] = all.Count,
            ["next"] = more ? page[^1].Id : null,
        };
    }

    // --- the new library's side ---------------------------------------------------------------------------------

    /// <summary>Files a page from <see cref="Export"/> here: classes this library hasn't got are added (with their other
    /// names and what each covers), and each lecture it hasn't got is written as it was, notes and class included.
    /// A lecture already here is never touched. Throws <see cref="PayloadException"/> for a page that isn't one.</summary>
    public static MoveResult Import(Store store, Config cfg, JsonObject page)
    {
        int classes = 0;
        foreach (var node in page["classes"] as JsonArray ?? [])
        {
            if (node is not JsonObject c || Text(c["name"]) is not { Length: > 0 and <= 60 } name) continue;
            if (name.Equals(Configs.Unsorted, StringComparison.OrdinalIgnoreCase)) continue;
            if (cfg.Classes.Any(k => k.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) continue;
            var aliases = (c["aliases"] as JsonArray ?? []).Select(Text).OfType<string>().Where(a => a.Length > 0).ToList();
            cfg.Classes.Add(new ClassDef(name, aliases, Text(c["description"]) ?? ""));
            classes++;
        }
        if (classes > 0) Configs.Save(cfg);

        if (page["lectures"] is not JsonArray lectures) throw new PayloadException("a page needs its lectures");
        int filed = 0, skipped = 0;
        var known = store.KnownIds();
        foreach (var node in lectures)
        {
            if (node is not JsonObject l) throw new PayloadException("a lecture must be an object");
            var m = Wire.MeetingFromJson(l["meeting"]);
            if (!known.Add(m.Id))
            {
                skipped++;
                continue;
            }
            string cls = Text(l["class"]) is { Length: > 0 } named ? named : Configs.Unsorted;
            double confidence = l["confidence"] is JsonValue v && v.TryGetValue(out double d) ? d : 0;
            var topics = (l["topics"] as JsonArray ?? []).Select(Text).OfType<string>().ToList();
            store.Save(m, new Classification(cls, confidence, Text(l["by"]) ?? "none", Text(l["lecture_title"]) ?? "", topics),
                Text(l["summary"]) ?? "", Text(l["summary_model"]) ?? "");
            filed++;
        }
        return new MoveResult(filed, skipped, classes);
    }

    static string? Text(JsonNode? node) => node is JsonValue v && v.TryGetValue(out string? s) ? s.Trim() : null;

    // --- the app in between -------------------------------------------------------------------------------------

    /// <summary>
    /// Copies every lecture from the library <paramref name="from"/> answers at into the one <paramref name="to"/>
    /// answers at (each client's BaseAddress is its library), a page at a time, saying how far it's got. Throws
    /// <see cref="InvalidOperationException"/> with a sentence for the student when either library says no: the old
    /// one can't be reached, its password is wrong, or it runs a Study Stash too old to hand lectures over.
    /// </summary>
    public static async Task<MoveResult> CopyAsync(HttpClient from, string fromKey, HttpClient to, string toKey,
        Action<MoveProgress>? progress = null, CancellationToken ct = default)
    {
        int filed = 0, skipped = 0, classes = 0;
        string? after = null;
        while (true)
        {
            string query = $"{Route}?limit={PageSize}" + (after is null ? "" : "&after=" + Uri.EscapeDataString(after));
            var page = await AskAsync(from, fromKey, HttpMethod.Get, query, null, "your old library", ct);
            var result = await AskAsync(to, toKey, HttpMethod.Post, Route, page, "this library", ct);
            filed += Count(result, "filed");
            skipped += Count(result, "skipped");
            classes += Count(result, "classes");
            int total = Count(page, "total");
            progress?.Invoke(new MoveProgress(Math.Min(filed + skipped, total), total));
            after = Text(page["next"]);
            if (after is null) return new MoveResult(filed, skipped, classes);
        }
    }

    static int Count(JsonObject o, string key) => o[key] is JsonValue v && v.TryGetValue(out int n) ? n : 0;

    static async Task<JsonObject> AskAsync(HttpClient http, string key, HttpMethod method, string path, JsonObject? body, string who, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + key);
        if (body is not null) request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        HttpResponseMessage r;
        try
        {
            r = await http.SendAsync(request, ct);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            throw new InvalidOperationException($"Can't reach {who}. Is its computer on, and is Tailscale connected?", e);
        }
        using (r)
        {
            string text = await r.Content.ReadAsStringAsync(ct);
            if (r.StatusCode == HttpStatusCode.Unauthorized) throw new InvalidOperationException($"The password for {who} isn't right.");
            if (r.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed)
                throw new InvalidOperationException($"{Capital(who)} runs an older Study Stash. Update it, then bring your lectures over.");
            if (!r.IsSuccessStatusCode)
                throw new InvalidOperationException($"{Capital(who)} didn't take it ({(int)r.StatusCode}). Try again in a moment.");
            try
            {
                return JsonNode.Parse(text) as JsonObject ?? throw new InvalidOperationException($"{Capital(who)} answered oddly. Try again in a moment.");
            }
            catch (JsonException e)
            {
                throw new InvalidOperationException($"{Capital(who)} answered oddly. Try again in a moment.", e);
            }
        }
    }

    static string Capital(string s) => char.ToUpper(s[0], CultureInfo.InvariantCulture) + s[1..];
}
