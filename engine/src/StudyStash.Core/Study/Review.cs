using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace StudyStash.Core.Study;

/// <summary>How a card went when it was reviewed: forgotten, remembered with effort, remembered, or too easy.</summary>
public enum Grade { Again, Hard, Good, Easy }

/// <summary>
/// A flashcard in Review: a question and its answer from a lecture (with the moment it was taught, when known), and
/// where it is in its schedule: when it's next due, the days between reviews now, how easy it's been (the factor the
/// days grow by), how many times in a row it was remembered, and how often it was forgotten after that.
/// </summary>
public sealed record ReviewCard
{
    public required string Id { get; init; }
    public required string Lecture { get; init; }
    public string Class { get; init; } = "";
    public string LectureTitle { get; init; } = "";
    public required string Front { get; init; }
    public required string Back { get; init; }
    /// <summary>The moment in the lecture it comes from, in seconds; null when it isn't known.</summary>
    public double? At { get; init; }
    /// <summary>Where it came from: the lecture's notes ("notes"), a practice test it was missed on ("exam"), or typed
    /// in ("own").</summary>
    public string From { get; init; } = "notes";
    public DateTimeOffset Made { get; init; }
    public DateTimeOffset Due { get; init; }
    /// <summary>Days between reviews now; 0 for a card not yet learned (new, or forgotten and being learned again).</summary>
    public double Interval { get; init; }
    public double Ease { get; init; } = Review.StartEase;
    public int Reps { get; init; }
    public int Lapses { get; init; }
    public DateTimeOffset? Reviewed { get; init; }

    [JsonIgnore] public bool IsNew => Reviewed is null;
}

/// <summary>How much is waiting in Review: due now, new among them, and every card there is.</summary>
public sealed record ReviewCounts(int Due, int New, int Total)
{
    /// <summary>Due by the end of today, the ones due now included.</summary>
    public int Today { get; init; }
}

/// <summary>
/// Review: flashcards from the student's lectures, brought back on a schedule so what they learned stays learned.
/// Each card answered Again comes back in ten minutes; Good the first time, tomorrow; and after that at longer and
/// longer gaps (the days so far times the card's ease), so a card that's easy is seen a few times a term and one
/// that's hard every few days, the way SM-2 and Anki space them. Kept in the library's <c>review.json</c>, so the
/// laptop and the phone review the same cards.
/// </summary>
public sealed class Review(string home)
{
    public const double StartEase = 2.5, MinEase = 1.3, MaxDays = 365;
    /// <summary>How soon a card answered Again (or new and answered Again) is asked again.</summary>
    public static readonly TimeSpan Relearn = TimeSpan.FromMinutes(10);

    /// <summary>How cards are written, in review.json and over the API.</summary>
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull, Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) },
    };
    static readonly object Gate = new();

    public string PathOf => Path.Combine(home, "review.json");

    sealed record Kept(List<ReviewCard> Cards);

    public List<ReviewCard> All()
    {
        lock (Gate) return Read();
    }

    List<ReviewCard> Read()
    {
        try
        {
            if (SharedFile.Read(PathOf) is { } text && JsonSerializer.Deserialize<Kept>(text, Options) is { Cards: { } cards })
                return cards.Where(c => c is { Id.Length: > 0, Front.Length: > 0, Back.Length: > 0 }).ToList();
        }
        catch (JsonException)
        {
        }
        return [];
    }

    void Write(List<ReviewCard> cards)
    {
        Directory.CreateDirectory(home);
        SharedFile.Write(PathOf, JsonSerializer.Serialize(new Kept(cards), Options) + "\n");
    }

    /// <summary>The cards due by <paramref name="now"/>, the longest overdue first and the new after them (a few at a
    /// time: <paramref name="newCards"/>), for one class or all. <paramref name="limit"/> caps how many.</summary>
    public List<ReviewCard> Due(DateTimeOffset now, string? className = null, int limit = 100, int newCards = 20)
    {
        var mine = All().Where(c => className is null || c.Class == className).ToList();
        var due = mine.Where(c => !c.IsNew && c.Due <= now).OrderBy(c => c.Due).ToList();
        var fresh = mine.Where(c => c.IsNew && c.Due <= now).OrderBy(c => c.Made).Take(newCards); // made together: in the order they were written
        return due.Concat(fresh).Take(limit).ToList();
    }

    public ReviewCounts Counts(DateTimeOffset now, string? className = null, int newCards = 20)
    {
        var mine = All().Where(c => className is null || c.Class == className).ToList();
        var endOfDay = new DateTimeOffset(now.Date.AddDays(1), now.Offset);
        int news = Math.Min(newCards, mine.Count(c => c.IsNew && c.Due <= now));
        int due = mine.Count(c => !c.IsNew && c.Due <= now) + news;
        int today = mine.Count(c => !c.IsNew && c.Due < endOfDay) + news;
        return new ReviewCounts(due, news, mine.Count) { Today = today };
    }

    public List<ReviewCard> OfLecture(string lecture) => All().Where(c => c.Lecture == lecture).OrderBy(c => c.Made).ToList();

    public ReviewCard? Get(string id) => All().FirstOrDefault(c => c.Id == id);

    /// <summary>Adds cards, leaving out any whose question the lecture already has (in other words or case). The
    /// ones added come back.</summary>
    public List<ReviewCard> Add(IEnumerable<ReviewCard> cards)
    {
        lock (Gate)
        {
            var all = Read();
            var seen = all.Select(c => (c.Lecture, Same(c.Front))).ToHashSet();
            var added = new List<ReviewCard>();
            foreach (var c in cards)
            {
                if (c.Front.Trim().Length == 0 || c.Back.Trim().Length == 0 || !seen.Add((c.Lecture, Same(c.Front)))) continue;
                added.Add(c);
            }
            all.AddRange(added);
            Write(all);
            return added;
        }
    }

    /// <summary>A new card for a lecture, due now.</summary>
    public static ReviewCard New(string lecture, string className, string lectureTitle, string front, string back, double? at, string from,
        DateTimeOffset now) => new()
    {
        Id = "card-" + Guid.NewGuid().ToString("N")[..12], Lecture = lecture, Class = className, LectureTitle = lectureTitle,
        Front = front.Trim(), Back = back.Trim(), At = at, From = from, Made = now, Due = now,
    };

    /// <summary>The card reviewed and graded: its next due date, saved. Null when there's no such card.</summary>
    public ReviewCard? Grade(string id, Grade grade, DateTimeOffset now)
    {
        lock (Gate)
        {
            var all = Read();
            int i = all.FindIndex(c => c.Id == id);
            if (i < 0) return null;
            all[i] = Next(all[i], grade, now);
            Write(all);
            return all[i];
        }
    }

    public bool Delete(string id)
    {
        lock (Gate)
        {
            var all = Read();
            if (all.RemoveAll(c => c.Id == id) == 0) return false;
            Write(all);
            return true;
        }
    }

    /// <summary>Changes a card's words (a question the AI put badly). Null when there's no such card.</summary>
    public ReviewCard? Edit(string id, string front, string back)
    {
        if (front.Trim().Length == 0 || back.Trim().Length == 0) return null;
        lock (Gate)
        {
            var all = Read();
            int i = all.FindIndex(c => c.Id == id);
            if (i < 0) return null;
            all[i] = all[i] with { Front = front.Trim(), Back = back.Trim() };
            Write(all);
            return all[i];
        }
    }

    /// <summary>A lecture deleted for good takes its cards with it.</summary>
    public int ForgetLecture(string lecture)
    {
        lock (Gate)
        {
            var all = Read();
            int gone = all.RemoveAll(c => c.Lecture == lecture);
            if (gone > 0) Write(all);
            return gone;
        }
    }

    /// <summary>
    /// The schedule after a review. A card not yet learned (new, or forgotten) answered Again comes back in
    /// <see cref="Relearn"/>; Hard or Good, tomorrow; Easy, in four days. A learned card answered Again is forgotten:
    /// back to learning, its ease down 0.2. Hard: the gap times 1.2, ease down 0.15. Good: the gap times its ease.
    /// Easy: times its ease and 1.3, ease up 0.15. Gaps are whole days, at least one more than the last for Good and
    /// Easy, and at most a year; ease stays at or over 1.3.
    /// </summary>
    public static ReviewCard Next(ReviewCard c, Grade grade, DateTimeOffset now)
    {
        double ease = c.Ease, days;
        int reps = c.Reps, lapses = c.Lapses;
        if (grade == Study.Grade.Again)
        {
            ease = c.Interval > 0 ? Math.Max(MinEase, ease - 0.2) : ease;
            lapses += c.Interval > 0 ? 1 : 0;
            return c with { Interval = 0, Reps = 0, Ease = Round(ease), Lapses = lapses, Due = now + Relearn, Reviewed = now };
        }
        if (c.Interval <= 0)
            days = grade == Study.Grade.Easy ? 4 : 1;
        else
        {
            days = grade switch
            {
                Study.Grade.Hard => c.Interval * 1.2,
                Study.Grade.Good => Math.Max(c.Interval + 1, c.Interval * ease),
                _ => Math.Max(c.Interval + 1, c.Interval * ease * 1.3),
            };
            ease = grade switch
            {
                Study.Grade.Hard => Math.Max(MinEase, ease - 0.15),
                Study.Grade.Easy => ease + 0.15,
                _ => ease,
            };
        }
        days = Math.Clamp(Math.Round(days, MidpointRounding.AwayFromZero), 1, MaxDays);
        return c with { Interval = days, Reps = reps + 1, Ease = Round(ease), Due = now.AddDays(days), Reviewed = now };
    }

    static double Round(double ease) => Math.Round(ease, 2);

    /// <summary>When a card would next come back for each grade ("10 min", "1 day", "3 days", "2 mo"), under its buttons.</summary>
    public static string After(ReviewCard c, Grade grade, DateTimeOffset now) => Span(Next(c, grade, now).Due - now);

    public static string Span(TimeSpan t) =>
        t.TotalMinutes < 60 ? $"{Math.Max(1, (int)Math.Round(t.TotalMinutes))} min"
        : t.TotalDays < 1 ? $"{(int)Math.Round(t.TotalHours)} h"
        : t.TotalDays < 31 ? $"{(int)Math.Round(t.TotalDays)} day{((int)Math.Round(t.TotalDays) == 1 ? "" : "s")}"
        : t.TotalDays < 365 ? $"{Math.Round(t.TotalDays / 30.4, 1):0.#} mo"
        : $"{Math.Round(t.TotalDays / 365, 1):0.#} yr";

    /// <summary>A question as it's compared for sameness: its letters and digits, lower case.</summary>
    static string Same(string front) => Regex.Replace(front.ToLowerInvariant(), @"[^\p{L}\p{N}]+", "");
}
