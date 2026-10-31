using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using StudyStash.Core;
using StudyStash.Core.Study;

namespace StudyStash.Library;

/// <summary>
/// The study tools: Review (flashcards from the student's lectures, brought back on a schedule), what the lecturer
/// flagged for the exam, exam prep and listening to a lecture. Every device that reaches the library studies the same
/// cards and the same prep.
/// </summary>
public sealed partial class LibraryWeb
{
    Review? reviewCards;
    Review ReviewCards => reviewCards ??= new Review(cfg.Home);
    DateTimeOffset Now() => options.Now?.Invoke() ?? DateTimeOffset.Now;

    /// <summary>Cards being written for a lecture right now, so a second press doesn't write them twice.</summary>
    readonly HashSet<string> makingCards = [];

    static IResult StudyJson<T>(T value) => Http.Json(JsonSerializer.SerializeToNode(value, Review.Options));

    void MapStudy(WebApplication app)
    {
        // Review: what's due, and how much.
        app.MapGet("/api/v2/review", (HttpContext ctx, string? @class, int? limit) => Api(ctx, () =>
        {
            string? cls = string.IsNullOrWhiteSpace(@class) ? null : @class.Trim();
            var now = Now();
            return Http.Json(new JsonObject
            {
                ["counts"] = JsonSerializer.SerializeToNode(ReviewCards.Counts(now, cls), Review.Options),
                ["due"] = JsonSerializer.SerializeToNode(ReviewCards.Due(now, cls, Math.Clamp(limit ?? 100, 1, 500)), Review.Options),
            });
        }));
        app.MapGet("/api/v2/review/lectures/{id}", (HttpContext ctx, string id) => Api(ctx, () => StudyJson(ReviewCards.OfLecture(id))));

        // Cards for a lecture, written by the library's AI from its notes. A lecture that has some already gets more
        // only when asked for more.
        app.MapPost("/api/v2/review/lectures/{id}", Http.Handle(ctx => ApiAsync(ctx, async () =>
        {
            string id = (string)ctx.Request.RouteValues["id"]!;
            bool more = (await Http.JsonBodyAsync(ctx.Request))?["more"] is JsonValue mv && mv.TryGetValue(out bool m) && m;
            if (StudyMaterial.Read(store, id) is not { } lecture) return Http.Detail(404, "no such lecture");
            if (!lecture.HasWords) return Http.Detail(409, "This lecture has no notes or transcript to make cards from yet.");
            var had = ReviewCards.OfLecture(id);
            if (had.Count > 0 && !more) return StudyJson(new { added = new List<ReviewCard>(), cards = had });
            lock (makingCards)
                if (!makingCards.Add(id)) return Http.Detail(409, "Cards for this lecture are already being made.");
            try
            {
                var made = await StudyMaterial.MakeCardsAsync((p, s) => Jobs.PlanAsync("ask", p, s), lecture, Now());
                var added = ReviewCards.Add(made);
                return StudyJson(new { added, cards = ReviewCards.OfLecture(id) });
            }
            catch (Exception e) when (e is InvalidOperationException or InvalidDataException or HttpRequestException or TimeoutException or JsonException)
            {
                return Http.Detail(503, $"The cards couldn't be made: {e.Message}");
            }
            finally
            {
                lock (makingCards) makingCards.Remove(id);
            }
        })));

        // A card typed in (or one missed on a practice test): due now.
        app.MapPost("/api/v2/review/cards", Http.Handle(ctx => ApiAsync(ctx, async () =>
        {
            var body = await Http.JsonBodyAsync(ctx.Request);
            string lectureId = Str(body, "lecture") ?? "", front = Str(body, "front") ?? "", back = Str(body, "back") ?? "";
            if (front.Trim().Length == 0 || back.Trim().Length == 0) return Http.Detail(400, "a card needs a question and an answer");
            var row = lectureId.Length > 0 ? store.Get(lectureId) : null;
            if (lectureId.Length > 0 && row is null) return Http.Detail(404, "no such lecture");
            double? at = body?["at"] is JsonValue av && av.TryGetValue(out double a) ? a : null;
            string from = Str(body, "from") is "exam" ? "exam" : "own";
            var card = Review.New(lectureId, row?.ClassName ?? Str(body, "class") ?? "", row?.Title ?? "", front, back, at, from, Now());
            var added = ReviewCards.Add([card]);
            return added.Count == 0 ? Http.Detail(409, "That card is already in Review.") : StudyJson(added[0]);
        })));

        app.MapPost("/api/v2/review/cards/{id}/grade", Http.Handle(ctx => ApiAsync(ctx, async () =>
        {
            string id = (string)ctx.Request.RouteValues["id"]!;
            if (!Enum.TryParse<Grade>(Str(await Http.JsonBodyAsync(ctx.Request), "grade"), ignoreCase: true, out var grade))
                return Http.Detail(400, "grade is again, hard, good or easy");
            return ReviewCards.Grade(id, grade, Now()) is { } card ? StudyJson(card) : Http.Detail(404, "no such card");
        })));

        app.MapPost("/api/v2/review/cards/{id}", Http.Handle(ctx => ApiAsync(ctx, async () =>
        {
            string id = (string)ctx.Request.RouteValues["id"]!;
            var body = await Http.JsonBodyAsync(ctx.Request);
            return ReviewCards.Edit(id, Str(body, "front") ?? "", Str(body, "back") ?? "") is { } card ? StudyJson(card)
                : ReviewCards.Get(id) is null ? Http.Detail(404, "no such card") : Http.Detail(400, "a card needs a question and an answer");
        })));

        app.MapDelete("/api/v2/review/cards/{id}", (HttpContext ctx, string id) => Api(ctx, () =>
            ReviewCards.Delete(id) ? Http.Json(new JsonObject { ["deleted"] = id }) : Http.Detail(404, "no such card")));

        // What the lecturer said is on the exam, for the phone and Claude.
        app.MapGet("/api/v2/lectures/{id}/exam-flags", (HttpContext ctx, string id) => Api(ctx, () =>
            StudyMaterial.Read(store, id) is { } l ? StudyJson(l.Flags) : Http.Detail(404, "no such lecture")));
    }
}
