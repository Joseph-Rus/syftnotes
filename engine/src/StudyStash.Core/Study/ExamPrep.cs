using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace StudyStash.Core.Study;

/// <summary>An exam coming up, as Canvas or the calendar has it: its class, its name, when (null when it isn't known),
/// and where it was found ("canvas", "calendar", or "own" for one the student named).</summary>
public sealed record ExamDate(string Class, string Title, DateTimeOffset? When, string From)
{
    /// <summary>The same exam found twice (Canvas and the calendar) has one key.</summary>
    [JsonIgnore] public string Key => $"{Class}\u0001{ExamPrep.Same(Title)}";
}

/// <summary>A lecture an exam prep covers.</summary>
public sealed record PrepLecture(string Id, string Title, string Date);

/// <summary>One practice question: multiple choice (its <see cref="Choices"/> and the right one's place) or short
/// answer (a model answer to mark against), with why, and the lecture and moment it comes from.</summary>
public sealed record PrepQuestion
{
    public required string Id { get; init; }
    /// <summary>"choice" or "short".</summary>
    public required string Kind { get; init; }
    public required string Question { get; init; }
    public List<string> Choices { get; init; } = [];
    /// <summary>The right choice's place in <see cref="Choices"/>, for "choice".</summary>
    public int Answer { get; init; } = -1;
    /// <summary>The answer in words: the right choice, or a short answer's model answer.</summary>
    public string ModelAnswer { get; init; } = "";
    public string Why { get; init; } = "";
    public string Lecture { get; init; } = "";
    public double? At { get; init; }
}

/// <summary>A go at a practice test: when, and which questions were got right.</summary>
public sealed record PrepAttempt(DateTimeOffset At, Dictionary<string, bool> Marks)
{
    public int Right => Marks.Values.Count(r => r);
}

/// <summary>
/// Prep for one exam: what it's for (the class, the exam and when), the lectures it covers, a study guide written from
/// them (Markdown, with the moments), a practice test, and each go at the test.
/// </summary>
public sealed record ExamPrepDoc
{
    public required string Id { get; init; }
    public required string Class { get; init; }
    public required string Title { get; init; }
    public DateTimeOffset? When { get; init; }
    public DateTimeOffset Made { get; init; }
    public string By { get; init; } = "";
    public List<PrepLecture> Lectures { get; init; } = [];
    public string Guide { get; init; } = "";
    public List<PrepQuestion> Questions { get; init; } = [];
    public List<PrepAttempt> Attempts { get; init; } = [];
}

/// <summary>An exam prep as a list shows it: no guide or questions, just what it's for and the last score.</summary>
public sealed record PrepSummary(string Id, string Class, string Title, DateTimeOffset? When, DateTimeOffset Made, int Lectures, int Questions,
    int? LastRight);

/// <summary>What the Study page lists for exams: the ones coming up (Canvas, the calendar) and the prep made.</summary>
public sealed record ExamsNow(List<ExamDate> Upcoming, List<PrepSummary> Preps);

/// <summary>
/// Exam prep: the exams coming up, found in Canvas's assignments and the calendar by their names ("Midterm 2", "Final
/// exam", "Quiz 4"), and for one of them a study guide and a practice test written by the library's AI from the class's
/// lectures since the exam before it, putting first what the lecturer said would be on it. Kept in the library's
/// exam-prep folder, one file a prep; questions missed on the test can go into Review.
/// </summary>
public sealed partial class ExamPrep(string home)
{
    [GeneratedRegex(@"\b(?:exam|exams|midterm|mid-term|final|finals|test|quiz|quizzes|prelim|assessment)\b", RegexOptions.IgnoreCase)]
    private static partial Regex ExamWord();

    // "Final project", "Test plan", "Quiz solutions" and the like aren't exams.
    [GeneratedRegex(@"\b(?:project|paper|essay|report|plan|solutions?|review session|corrections?|reflection|survey|wrapper|practice|retake form)\b", RegexOptions.IgnoreCase)]
    private static partial Regex NotExam();

    /// <summary>Whether a Canvas assignment or calendar event is an exam by its name.</summary>
    public static bool IsExam(string title) => ExamWord().IsMatch(title) && !NotExam().IsMatch(title);

    /// <summary>How far ahead exams are looked for.</summary>
    public static readonly TimeSpan Ahead = TimeSpan.FromDays(60);

    /// <summary>The exams coming up (from <paramref name="now"/> to <see cref="Ahead"/>), soonest first, each once
    /// (Canvas's when both have it), in the classes the library knows.</summary>
    public static List<ExamDate> Upcoming(IEnumerable<ExamDate> found, DateTimeOffset now, IReadOnlyCollection<string>? classes = null) =>
        found.Where(e => e.Class.Length > 0 && (classes is null || classes.Contains(e.Class)) && IsExam(e.Title)
                         && e.When is { } w && w >= now.AddHours(-12) && w <= now + Ahead)
            .OrderBy(e => e.From == "canvas" ? 0 : 1)
            .DistinctBy(e => e.Key)
            .OrderBy(e => e.When)
            .ToList();

    static readonly JsonSerializerOptions Options = Review.Options;
    static readonly object Gate = new();
    string Dir => Path.Combine(home, "exam-prep");
    string PathOf(string id) => Path.Combine(Dir, id + ".json");

    public ExamPrepDoc? Get(string id)
    {
        if (!IsId(id)) return null;
        try
        {
            return SharedFile.Read(PathOf(id)) is { } text ? JsonSerializer.Deserialize<ExamPrepDoc>(text, Options) : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public List<ExamPrepDoc> All()
    {
        if (!Directory.Exists(Dir)) return [];
        return Directory.EnumerateFiles(Dir, "*.json").Select(f => Get(Path.GetFileNameWithoutExtension(f))).OfType<ExamPrepDoc>()
            .OrderByDescending(p => p.Made).ToList();
    }

    public List<PrepSummary> Summaries() => All().Select(p => new PrepSummary(p.Id, p.Class, p.Title, p.When, p.Made, p.Lectures.Count,
        p.Questions.Count, p.Attempts.Count > 0 ? p.Attempts[^1].Right : null)).ToList();

    public void Save(ExamPrepDoc prep)
    {
        lock (Gate)
        {
            Directory.CreateDirectory(Dir);
            SharedFile.Write(PathOf(prep.Id), JsonSerializer.Serialize(prep, Options) + "\n");
        }
    }

    public bool Delete(string id)
    {
        lock (Gate)
        {
            if (!IsId(id) || !File.Exists(PathOf(id))) return false;
            File.Delete(PathOf(id));
            return true;
        }
    }

    /// <summary>Keeps a go at the practice test. Null when there's no such prep.</summary>
    public ExamPrepDoc? Attempt(string id, Dictionary<string, bool> marks, DateTimeOffset now)
    {
        lock (Gate)
        {
            if (Get(id) is not { } prep) return null;
            var known = prep.Questions.Select(q => q.Id).ToHashSet();
            var kept = marks.Where(m => known.Contains(m.Key)).ToDictionary(m => m.Key, m => m.Value);
            prep = prep with { Attempts = [.. prep.Attempts, new PrepAttempt(now, kept)] };
            Save(prep);
            return prep;
        }
    }

    static bool IsId(string id) => Regex.IsMatch(id, "^prep-[a-z0-9]{6,32}$");

    public static string NewId() => "prep-" + Guid.NewGuid().ToString("N")[..12];

    /// <summary>The lectures an exam covers: the class's, from <paramref name="since"/> (the exam before it; null for
    /// the start) up to the exam (or <paramref name="now"/>, if that's sooner), oldest first, the latest
    /// <paramref name="most"/> of them.</summary>
    public static List<NoteRow> Covered(Store store, string className, DateTimeOffset? since, DateTimeOffset? until, DateTimeOffset now, int most = 16)
    {
        var end = until is { } u && u < now ? u : now;
        return store.ListNotes(className)
            .Where(r => Day(r.Date) is not { } d || ((since is null || d >= since.Value.Date) && d <= end.Date))
            .OrderBy(r => r.Date, StringComparer.Ordinal)
            .TakeLast(most)
            .ToList();
    }

    static DateTime? Day(string? date) => DateTime.TryParse(date, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var d) ? d.Date : null;

    public static readonly JsonObject Schema = JsonNode.Parse("""
        {"type":"object","properties":{
          "guide":{"type":"string"},
          "questions":{"type":"array","items":{"type":"object","properties":{
            "kind":{"type":"string","enum":["choice","short"]},"question":{"type":"string"},
            "choices":{"type":"array","items":{"type":"string"}},"answer":{"type":"integer"},
            "model_answer":{"type":"string"},"why":{"type":"string"},"lecture":{"type":"integer"},"moment":{"type":"string"}},
            "required":["kind","question"]}}},
         "required":["guide","questions"]}
        """)!.AsObject();

    /// <summary>What the AI is asked for an exam's prep: the lectures numbered, each with its flags and notes.</summary>
    public static string Prompt(string className, string title, DateTimeOffset? when, IReadOnlyList<StudyLecture> lectures, int questions = 12)
    {
        var sb = new StringBuilder();
        sb.Append($"A student in {className} has \"{title}\"")
          .Append(when is { } w ? $" on {w.ToString("dddd d MMMM", CultureInfo.InvariantCulture)}" : "")
          .Append(". Below are the lectures it covers, numbered. Write two things.\n\n")
          .Append("1. \"guide\": a study guide in Markdown. Start with \"## What the lecturer said to know\", listing everything ")
          .Append("they said would be on the exam (or to know, or to write down), with the lecture and moment. Then a section a ")
          .Append("topic, in the order taught: the key ideas, terms with definitions, formulas with what each symbol means, and ")
          .Append("the kinds of problem worked in class. End with \"## Check yourself\", a short list of what to be able to do. ")
          .Append("Leave out what the lecturer said won't be on it. Cite moments as (Lecture 2, 14:05).\n\n")
          .Append($"2. \"questions\": a practice test of {questions} questions like the ones an exam on this would ask, most ")
          .Append("on what was flagged or got the most time. Mix \"choice\" (four \"choices\", \"answer\" the right one's place ")
          .Append("from 0, the wrong ones believable) with \"short\" (a \"model_answer\" of a sentence or two, or a worked ")
          .Append("answer). Every question has \"why\" (a sentence), \"lecture\" (its number) and, when known, \"moment\" (mm:ss).\n\n");
        int budget = Math.Max(2500, 48000 / Math.Max(1, lectures.Count));
        for (int i = 0; i < lectures.Count; i++)
            sb.Append($"=== Lecture {i + 1} ===\n").Append(lectures[i].ForAi(budget)).Append('\n');
        return sb.ToString();
    }

    /// <summary>The prep from the AI's answer: the guide, and the questions that make sense (a choice question needs
    /// its choices and a right one among them; a short one, its answer).</summary>
    public static ExamPrepDoc Read(string json, string className, string title, DateTimeOffset? when, IReadOnlyList<StudyLecture> lectures,
        DateTimeOffset now, string by)
    {
        var o = JsonNode.Parse(json) as JsonObject ?? throw new InvalidDataException("the answer wasn't a prep");
        string guide = StudyMaterial.Text(o["guide"]);
        var questions = new List<PrepQuestion>();
        foreach (var q in (o["questions"] as JsonArray ?? []).OfType<JsonObject>())
        {
            string kind = StudyMaterial.Text(q["kind"]), text = StudyMaterial.Text(q["question"]);
            if (text.Length == 0) continue;
            var choices = (q["choices"] as JsonArray ?? []).Select(StudyMaterial.Text).Where(c => c.Length > 0).ToList();
            int answer = q["answer"] is JsonValue av && av.TryGetValue(out int a) ? a : -1;
            string model = StudyMaterial.Text(q["model_answer"]);
            int n = q["lecture"] is JsonValue lv && lv.TryGetValue(out int l) ? l : 0;
            var lecture = n >= 1 && n <= lectures.Count ? lectures[n - 1] : null;
            bool choice = kind == "choice" || (kind != "short" && choices.Count >= 2);
            if (choice && (choices.Count < 2 || answer < 0 || answer >= choices.Count)) continue;
            if (!choice && model.Length == 0) continue;
            questions.Add(new PrepQuestion
            {
                Id = $"q{questions.Count + 1}", Kind = choice ? "choice" : "short", Question = text,
                Choices = choice ? choices : [], Answer = choice ? answer : -1, ModelAnswer = choice ? choices[answer] : model,
                Why = StudyMaterial.Text(q["why"]), Lecture = lecture?.Id ?? "", At = StudyMaterial.Seconds(StudyMaterial.Text(q["moment"])),
            });
        }
        if (guide.Length == 0 && questions.Count == 0) throw new InvalidDataException("the answer had no guide and no questions");
        return new ExamPrepDoc
        {
            Id = NewId(), Class = className, Title = title, When = when, Made = now, By = by, Guide = guide, Questions = questions,
            Lectures = [.. lectures.Select(l => new PrepLecture(l.Id, l.Title, l.Date))],
        };
    }

    /// <summary>A question's words as one card in Review: the question (with its choices), and its answer and why.</summary>
    public static (string Front, string Back) Card(PrepQuestion q) =>
        (q.Kind == "choice" ? q.Question + "\n" + string.Join("\n", q.Choices.Select((c, i) => $"{(char)('A' + i)}. {c}")) : q.Question,
         q.Why.Length > 0 ? $"{q.ModelAnswer}\n\n{q.Why}" : q.ModelAnswer);

    /// <summary>A title compared for sameness: its letters and digits, lower case.</summary>
    public static string Same(string title) => Regex.Replace(title.ToLowerInvariant(), @"[^\p{L}\p{N}]+", "");
}
