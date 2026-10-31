using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using StudyStash.Core;
using StudyStash.Core.Calendar;
using StudyStash.Core.Canvas;
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

        MapExamPrep(app);

        // What the lecturer said is on the exam, for the phone and Claude.
        app.MapGet("/api/v2/lectures/{id}/exam-flags", (HttpContext ctx, string id) => Api(ctx, () =>
            StudyMaterial.Read(store, id) is { } l ? StudyJson(l.Flags) : Http.Detail(404, "no such lecture")));
    }

    ExamPrep? examPreps;
    ExamPrep ExamPreps => examPreps ??= new ExamPrep(cfg.Home);
    /// <summary>Exams whose prep is being written right now (class and title), so a second press doesn't write it twice.</summary>
    readonly HashSet<string> prepping = [];

    /// <summary>Every exam Canvas and the calendar know of, past and coming, in the library's classes.</summary>
    List<ExamDate> ExamDates()
    {
        var found = new List<ExamDate>();
        try
        {
            foreach (var a in Assignments.Load(cfg.Home))
                if (When(a.DueAt.Length > 0 ? a.DueAt : a.Due) is { } due) found.Add(new ExamDate(a.ClassName, a.Name, due, "canvas"));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
        }
        try
        {
            foreach (var e in (LibraryCalendar.Read(cfg.Home, EventClass.Of(cfg), Now().AddDays(-1))["events"] as JsonArray ?? []).OfType<JsonObject>())
                if (Str(e, "class") is { Length: > 0 } cls && When(Str(e, "start")) is { } start)
                    found.Add(new ExamDate(cls, Str(e, "title") ?? "", start, "calendar"));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
        }
        return found;
    }

    static DateTimeOffset? When(string? iso) =>
        DateTimeOffset.TryParse(iso, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal, out var d) ? d : null;

    void MapExamPrep(WebApplication app)
    {
        // The exams coming up, and the prep made.
        app.MapGet("/api/v2/exam-prep", (HttpContext ctx) => Api(ctx, () =>
            StudyJson(new ExamsNow(ExamPrep.Upcoming(ExamDates(), Now(), cfg.ClassNames().ToHashSet()), ExamPreps.Summaries()))));

        app.MapGet("/api/v2/exam-prep/{id}", (HttpContext ctx, string id) => Api(ctx, () =>
            ExamPreps.Get(id) is { } prep ? StudyJson(prep) : Http.Detail(404, "no such exam prep")));

        app.MapDelete("/api/v2/exam-prep/{id}", (HttpContext ctx, string id) => Api(ctx, () =>
            ExamPreps.Delete(id) ? Http.Json(new JsonObject { ["deleted"] = id }) : Http.Detail(404, "no such exam prep")));

        // A study guide and a practice test for an exam, from the class's lectures since the exam before it.
        app.MapPost("/api/v2/exam-prep", Http.Handle(ctx => ApiAsync(ctx, async () =>
        {
            var body = await Http.JsonBodyAsync(ctx.Request);
            string cls = (Str(body, "class") ?? "").Trim(), title = (Str(body, "title") ?? "").Trim();
            var when = When(Str(body, "when"));
            if (cls.Length == 0 || title.Length == 0) return Http.Detail(400, "an exam prep needs a class and the exam's name");
            if (!cfg.ClassNames().Contains(cls)) return Http.Detail(404, $"there's no class called {cls}");
            var now = Now();
            // The exam before this one in the same class: its lectures were for that one.
            var since = ExamDates().Where(e => e.Class == cls && ExamPrep.IsExam(e.Title) && e.When is { } w && w < (when ?? now).AddHours(-12)
                    && ExamPrep.Same(e.Title) != ExamPrep.Same(title))
                .Select(e => e.When).Max();
            var lectures = ExamPrep.Covered(store, cls, since, when, now).Select(r => StudyMaterial.Read(store, r.Id)).OfType<StudyLecture>()
                .Where(l => l.HasWords).ToList();
            if (lectures.Count == 0) return Http.Detail(409, $"{cls} has no lectures with notes to prep from yet.");
            string key = $"{cls}\u0001{ExamPrep.Same(title)}";
            lock (prepping)
                if (!prepping.Add(key)) return Http.Detail(409, "That exam's prep is already being written.");
            try
            {
                string json = await Jobs.PlanAsync("ask", ExamPrep.Prompt(cls, title, when, lectures), ExamPrep.Schema);
                string by = Core.Ai.Engines.Name(Core.Ai.AiSettings.Load(cfg.Home).For("ask").Provider);
                var prep = ExamPrep.Read(json, cls, title, when, lectures, now, by);
                ExamPreps.Save(prep);
                return StudyJson(prep);
            }
            catch (Exception e) when (e is InvalidOperationException or InvalidDataException or HttpRequestException or TimeoutException or JsonException)
            {
                return Http.Detail(503, $"The prep couldn't be written: {e.Message}");
            }
            finally
            {
                lock (prepping) prepping.Remove(key);
            }
        })));

        // A go at the practice test: which questions were right. With review_missed, the missed ones go into Review.
        app.MapPost("/api/v2/exam-prep/{id}/attempt", Http.Handle(ctx => ApiAsync(ctx, async () =>
        {
            string id = (string)ctx.Request.RouteValues["id"]!;
            var body = await Http.JsonBodyAsync(ctx.Request);
            var marks = (body?["marks"] as JsonObject ?? []).Where(m => m.Value is JsonValue v && v.TryGetValue(out bool _))
                .ToDictionary(m => m.Key, m => m.Value!.GetValue<bool>());
            if (ExamPreps.Attempt(id, marks, Now()) is not { } prep) return Http.Detail(404, "no such exam prep");
            int added = 0;
            if (body?["review_missed"] is JsonValue rv && rv.TryGetValue(out bool review) && review)
            {
                var missed = prep.Questions.Where(q => marks.TryGetValue(q.Id, out bool right) && !right).Select(q =>
                {
                    var (front, back) = ExamPrep.Card(q);
                    string title = q.Lecture.Length > 0 ? store.Get(q.Lecture)?.Title ?? "" : "";
                    return Review.New(q.Lecture, prep.Class, title, front, back, q.At, "exam", Now());
                });
                added = ReviewCards.Add(missed).Count;
            }
            var result = JsonSerializer.SerializeToNode(prep, Review.Options)!.AsObject();
            result["added_to_review"] = added;
            return Http.Json(result);
        })));
    }
}
