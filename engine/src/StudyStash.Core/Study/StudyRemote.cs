using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using StudyStash.Core.Ai;

namespace StudyStash.Core.Study;

/// <summary>What's waiting in Review: the counts, and the cards due now in the order to ask them.</summary>
public sealed record ReviewState(ReviewCounts Counts, List<ReviewCard> Due);

/// <summary>Cards made for a lecture: the ones just added, and all it has now.</summary>
public sealed record MadeCards(List<ReviewCard> Added, List<ReviewCard> Cards);

/// <summary>What the app's study tools need from the library: Review, exam prep and listening. Every member answers
/// null (or nothing) when the library is too old to have the route.</summary>
public interface IStudyLibrary
{
    Task<ReviewState?> ReviewAsync(string? className = null, int limit = 100);
    Task<List<ReviewCard>?> LectureCardsAsync(string lecture);
    /// <summary>The library's AI writes cards for a lecture (more of them when it has some already and
    /// <paramref name="more"/>). Throws <see cref="LibraryRefusedException"/> with why when it can't.</summary>
    Task<MadeCards?> MakeCardsAsync(string lecture, bool more = false);
    Task<ReviewCard?> AddCardAsync(string lecture, string front, string back, double? at = null, string from = "own", string? className = null);
    Task<ReviewCard?> GradeAsync(string card, Grade grade);
    Task<ReviewCard?> EditCardAsync(string card, string front, string back);
    Task<bool> DeleteCardAsync(string card);
    Task<List<ExamHint>?> ExamFlagsAsync(string lecture);
    /// <summary>The exams coming up and the prep made; null from a library too old for exam prep.</summary>
    Task<ExamsNow?> ExamsAsync() => Task.FromResult<ExamsNow?>(null);
    Task<ExamPrepDoc?> PrepAsync(string id) => Task.FromResult<ExamPrepDoc?>(null);
    /// <summary>The library's AI writes a study guide and practice test for an exam. Throws
    /// <see cref="LibraryRefusedException"/> with why when it can't.</summary>
    Task<ExamPrepDoc?> MakePrepAsync(string className, string title, DateTimeOffset? when) => Task.FromResult<ExamPrepDoc?>(null);
    Task<bool> DeletePrepAsync(string id) => Task.FromResult(false);
    /// <summary>Keeps a go at the practice test; with <paramref name="reviewMissed"/> the missed questions go into Review
    /// (how many did comes back with it).</summary>
    Task<(ExamPrepDoc Prep, int AddedToReview)?> AttemptAsync(string id, Dictionary<string, bool> marks, bool reviewMissed) =>
        Task.FromResult<(ExamPrepDoc, int)?>(null);
}

/// <summary>The study tools over the library's API (/api/v2/review and the rest), the way the Syft Notes app reads them.
/// Like <see cref="AiRemote"/>: a plain 404 means an older library (null), one of our refusals throws
/// <see cref="LibraryRefusedException"/> with its words.</summary>
public sealed partial class StudyRemote(string serverUrl, Func<string> key, HttpClient? http = null) : IStudyLibrary
{
    public StudyRemote(string serverUrl, string key, HttpClient? http = null) : this(serverUrl, () => key, http) { }

    /// <summary>Making cards, a study guide or a script is the AI's work, and takes longer than a question does.</summary>
    static readonly HttpClient Shared = new() { Timeout = TimeSpan.FromMinutes(10) };
    readonly HttpClient client = http ?? Shared;
    readonly string server = serverUrl.TrimEnd('/');

    HttpRequestMessage Request(HttpMethod method, string path, JsonNode? body)
    {
        var request = new HttpRequestMessage(method, server + path);
        if (key() is { Length: > 0 } now) request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + now);
        if (body is not null) request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        return request;
    }

    async Task<JsonNode?> SendAsync(HttpMethod method, string path, JsonNode? body = null, CancellationToken ct = default)
    {
        using var request = Request(method, path, body);
        using var r = await client.SendAsync(request, ct);
        string text = await r.Content.ReadAsStringAsync(ct);
        JsonObject? asObject = text.Length > 0 && text.TrimStart().StartsWith('{') ? JsonNode.Parse(text) as JsonObject : null;
        if (r.StatusCode == HttpStatusCode.NotFound && asObject?["detail"] is null) return null;
        if (!r.IsSuccessStatusCode)
            throw new LibraryRefusedException((int)r.StatusCode, Py.AsString(asObject?["detail"]) ?? r.ReasonPhrase ?? "");
        return text.Length > 0 ? JsonNode.Parse(text) : null;
    }

    async Task<byte[]?> BytesAsync(string path, CancellationToken ct = default)
    {
        using var request = Request(HttpMethod.Get, path, null);
        using var r = await client.SendAsync(request, ct);
        if (r.StatusCode == HttpStatusCode.NotFound) return null;
        if (!r.IsSuccessStatusCode) throw new LibraryRefusedException((int)r.StatusCode, r.ReasonPhrase ?? "");
        return await r.Content.ReadAsByteArrayAsync(ct);
    }

    static T? As<T>(JsonNode? n) where T : class => n?.Deserialize<T>(Review.Options);
    static string Seg(string s) => Uri.EscapeDataString(s);

    public async Task<ReviewState?> ReviewAsync(string? className = null, int limit = 100)
    {
        string query = $"?limit={limit}" + (className is { Length: > 0 } c ? "&class=" + Seg(c) : "");
        return await SendAsync(HttpMethod.Get, "/api/v2/review" + query) is JsonObject o
            ? new ReviewState(As<ReviewCounts>(o["counts"]) ?? new ReviewCounts(0, 0, 0), As<List<ReviewCard>>(o["due"]) ?? [])
            : null;
    }

    public async Task<List<ReviewCard>?> LectureCardsAsync(string lecture) =>
        As<List<ReviewCard>>(await SendAsync(HttpMethod.Get, $"/api/v2/review/lectures/{Seg(lecture)}"));

    public async Task<MadeCards?> MakeCardsAsync(string lecture, bool more = false) =>
        await SendAsync(HttpMethod.Post, $"/api/v2/review/lectures/{Seg(lecture)}", new JsonObject { ["more"] = more }) is JsonObject o
            ? new MadeCards(As<List<ReviewCard>>(o["added"]) ?? [], As<List<ReviewCard>>(o["cards"]) ?? [])
            : null;

    public async Task<ReviewCard?> AddCardAsync(string lecture, string front, string back, double? at = null, string from = "own", string? className = null) =>
        As<ReviewCard>(await SendAsync(HttpMethod.Post, "/api/v2/review/cards", new JsonObject
        {
            ["lecture"] = lecture, ["front"] = front, ["back"] = back, ["at"] = at, ["from"] = from, ["class"] = className,
        }));

    public async Task<ReviewCard?> GradeAsync(string card, Grade grade) =>
        As<ReviewCard>(await SendAsync(HttpMethod.Post, $"/api/v2/review/cards/{Seg(card)}/grade",
            new JsonObject { ["grade"] = grade.ToString().ToLowerInvariant() }));

    public async Task<ReviewCard?> EditCardAsync(string card, string front, string back) =>
        As<ReviewCard>(await SendAsync(HttpMethod.Post, $"/api/v2/review/cards/{Seg(card)}", new JsonObject { ["front"] = front, ["back"] = back }));

    public async Task<bool> DeleteCardAsync(string card)
    {
        try
        {
            return await SendAsync(HttpMethod.Delete, $"/api/v2/review/cards/{Seg(card)}") is not null;
        }
        catch (LibraryRefusedException e) when (e.Status == 404)
        {
            return false;
        }
    }

    public async Task<ExamsNow?> ExamsAsync() => As<ExamsNow>(await SendAsync(HttpMethod.Get, "/api/v2/exam-prep"));

    public async Task<ExamPrepDoc?> PrepAsync(string id) => As<ExamPrepDoc>(await SendAsync(HttpMethod.Get, $"/api/v2/exam-prep/{Seg(id)}"));

    public async Task<ExamPrepDoc?> MakePrepAsync(string className, string title, DateTimeOffset? when) =>
        As<ExamPrepDoc>(await SendAsync(HttpMethod.Post, "/api/v2/exam-prep", new JsonObject
        {
            ["class"] = className, ["title"] = title, ["when"] = when?.ToString("o", System.Globalization.CultureInfo.InvariantCulture),
        }));

    public async Task<bool> DeletePrepAsync(string id)
    {
        try
        {
            return await SendAsync(HttpMethod.Delete, $"/api/v2/exam-prep/{Seg(id)}") is not null;
        }
        catch (LibraryRefusedException e) when (e.Status == 404)
        {
            return false;
        }
    }

    public async Task<(ExamPrepDoc Prep, int AddedToReview)?> AttemptAsync(string id, Dictionary<string, bool> marks, bool reviewMissed)
    {
        var body = new JsonObject { ["marks"] = new JsonObject(marks.Select(m => KeyValuePair.Create(m.Key, (JsonNode?)m.Value))), ["review_missed"] = reviewMissed };
        if (await SendAsync(HttpMethod.Post, $"/api/v2/exam-prep/{Seg(id)}/attempt", body) is not JsonObject o || As<ExamPrepDoc>(o) is not { } prep) return null;
        return (prep, o["added_to_review"] is JsonValue v && v.TryGetValue(out int n) ? n : 0);
    }

    public async Task<List<ExamHint>?> ExamFlagsAsync(string lecture) =>
        As<List<ExamHint>>(await SendAsync(HttpMethod.Get, $"/api/v2/lectures/{Seg(lecture)}/exam-flags"));
}
