using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace StudyStash.Core;

/// <summary>
/// A class's "What it covers" (Settings → Classes), written for the student so the AI files each lecture in the right
/// class: one or two lines of topics, comma separated. The first lines come once the class's Canvas course is synced
/// (its syllabus, module and page titles, assignment names); they're written again, with the topics its lectures
/// covered, once a few more lectures are filed there, at most once a day. The student's AI (the one that sorts)
/// writes them; with no AI, or when it doesn't answer, they're Canvas's own words. Text the student typed is never
/// touched (<see cref="Writable"/>), and an edit of theirs makes it theirs (<see cref="Kept"/>). It runs in the
/// pipeline's loop, between lectures, so it never holds up notes; a failure is logged and the class is left as it was.
/// </summary>
public sealed partial class ClassTopics(Config cfg, Store store, SortChatFn? chat = null, Action<string>? log = null, TimeProvider? time = null)
{
    /// <summary><see cref="ClassDef.DescriptionBy"/>: written by the student's AI.</summary>
    public const string ByAi = "ai";
    /// <summary><see cref="ClassDef.DescriptionBy"/>: Canvas's own words, with no AI.</summary>
    public const string ByCanvas = "canvas";
    /// <summary>About two lines in Settings.</summary>
    public const int MaxChars = 160;
    /// <summary>How many more filed lectures than last time make the topics worth writing again.</summary>
    public const int NewLectures = 3;

    readonly Action<string> log = log ?? Console.WriteLine;
    readonly TimeProvider time = time ?? TimeProvider.System;
    volatile bool due = true; // a library that just started looks once

    /// <summary>Something happened that may make a class's topics worth writing: a Canvas sync finished, a lecture was filed.</summary>
    public void Due() => due = true;

    /// <summary>The quiet line under the box while it's not the student's: "Written by your AI", "From Canvas", or "".</summary>
    public static string Mark(string by) => by switch
    {
        ByAi => "Written by your AI",
        ByCanvas => "From Canvas",
        _ => "",
    };

    /// <summary>The one rule: a class's "What it covers" may be written for the student only while it's empty, or it
    /// was written for them and they haven't changed it since.</summary>
    public static bool Writable(ClassDef c) => c.Description.Trim().Length == 0 || c.DescriptionBy.Length > 0;

    /// <summary>A class's "What it covers" as a Settings screen sent it back (<paramref name="had"/> is the library's
    /// own). Text written for the student stays, marked, while the screen sent that same text, said it left it alone
    /// (<paramref name="sentBy"/>: it may have shown an older version), or sent the box empty (a screen opened before
    /// it was written). Anything else the student typed: it's theirs from then on.</summary>
    public static (string Text, string By) Kept(ClassDef? had, string sent, string? sentBy = null)
    {
        sent = sent.Trim();
        if (had is { DescriptionBy.Length: > 0 } && (sent.Length == 0 || sent == had.Description.Trim() || sentBy == had.DescriptionBy))
            return (had.Description, had.DescriptionBy);
        return (sent, "");
    }

    /// <summary>Writes the topics of every class that's due, one at a time. Nothing to do unless <see cref="Due"/> was called.</summary>
    public async Task RunAsync(CancellationToken ct = default)
    {
        if (!due) return;
        due = false;
        var counts = store.ClassesSummary().ToDictionary(c => c.ClassName, c => c.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var c in cfg.Classes.ToList())
        {
            if (ct.IsCancellationRequested) return;
            try
            {
                await WriteIfDueAsync(c.Name, counts.GetValueOrDefault(c.Name), ct);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                log($"[topics] {c.Name}: couldn't save what it covers ({e.Message})");
            }
        }
    }

    async Task WriteIfDueAsync(string cls, int lectures, CancellationToken ct)
    {
        if (cfg.Classes.FirstOrDefault(c => c.Name == cls) is not { } def || !Writable(def)) return;
        Canvas.CourseIndex? course;
        try
        {
            course = Canvas.CourseIndex.Load(cfg.Home, cls);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            course = null;
        }
        if (course is null) return; // written from Canvas first: a class not on Canvas is as the student left it
        var now = time.GetUtcNow();
        var last = Last(cls);
        bool first = def.Description.Trim().Length == 0 || last is null;
        bool again = last is { } l && lectures >= l.Lectures + NewLectures && now - l.At >= TimeSpan.FromDays(1);
        if (!first && !again) return;

        List<string> tags = lectures > 0 ? LectureTopics(cls) : [];
        string text = "", by = ByCanvas;
        if (AiOn())
        {
            try
            {
                string answer = await chat!(cfg, Prompt(def, course, Syllabus(cls, course), tags, cfg.ClassNames()), Schema());
                text = Join(TopicsIn(answer));
                if (text.Length > 0) by = ByAi;
            }
            catch (Exception e) when (e is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                log($"[topics] {cls}: the AI didn't write what it covers ({Py.Head(e.Message, 200)}); using Canvas's words");
            }
        }
        if (text.Length == 0) text = FromCanvas(course);
        if (text.Length == 0) return;

        // The student may have typed in the box while the AI wrote: theirs wins.
        if (cfg.Classes.FirstOrDefault(c => c.Name == cls) is not { } still || !Writable(still)) return;
        bool changed = still.Description != text || still.DescriptionBy != by;
        still.Description = text;
        still.DescriptionBy = by;
        if (changed) Configs.Save(cfg);
        store.SetState(StateKey(cls), $"{lectures} {now.ToString("o", CultureInfo.InvariantCulture)}");
        log($"[topics] {cls}: {(by == ByAi ? "your AI wrote" : "Canvas's words for")} what it covers: {text}");
    }

    bool AiOn() => chat is not null && cfg.OllamaEnabled && Ai.AiSettings.Load(cfg.Home).Picked("sort");

    static string StateKey(string cls) => "class_topics:" + cls;

    (int Lectures, DateTimeOffset At)? Last(string cls) =>
        store.GetState(StateKey(cls))?.Split(' ', 2) is [var n, var at]
        && int.TryParse(n, NumberStyles.Integer, CultureInfo.InvariantCulture, out int count)
        && DateTimeOffset.TryParse(at, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var when)
            ? (count, when) : null;

    /// <summary>The topic tags the class's lectures were given when they were sorted, most common first.</summary>
    List<string> LectureTopics(string cls) =>
        [.. store.ListNotes(cls, 40)
            .SelectMany(r => JsonNode.Parse(string.IsNullOrEmpty(r.Topics) ? "[]" : r.Topics) is JsonArray a ? a.Select(Py.Str) : [])
            .Select(t => Py.Strip(t)).Where(t => t.Length > 0)
            .GroupBy(t => t, StringComparer.OrdinalIgnoreCase).OrderByDescending(g => g.Count()).Select(g => g.Key).Take(30)];

    /// <summary>The syllabus Canvas has for the class, as text, or "".</summary>
    string Syllabus(string cls, Canvas.CourseIndex course)
    {
        if (course.Syllabus is not { Length: > 0 } rel) return "";
        try
        {
            string path = Path.Combine(store.ClassDir(cls), rel);
            return File.Exists(path) ? File.ReadAllText(path) : "";
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return "";
        }
    }

    public static JsonObject Schema() => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            ["topics"] = new JsonObject { ["type"] = "array", ["items"] = new JsonObject { ["type"] = "string" } },
        },
        ["required"] = new JsonArray("topics"),
    };

    /// <summary>What the AI is asked: the class as Canvas has it (and its lectures' topics, later on), for its topics only.</summary>
    public static string Prompt(ClassDef c, Canvas.CourseIndex course, string syllabus, IReadOnlyList<string> lectureTopics, IReadOnlyList<string> classes)
    {
        string onCanvas = string.Join(" · ", new[] { course.Name, course.Code }.Select(n => n.Trim()).Where(n => n.Length > 0).Distinct());
        var others = classes.Where(n => n != c.Name).ToList();
        var lines = new List<string>
        {
            "You write the topics a class covers, for a student's lecture library. The library reads them to file each new lecture in the right class.",
            $"Class: {c.Name}" + (onCanvas.Length > 0 ? $" (on Canvas: {onCanvas})" : ""),
        };
        if (others.Count > 0) lines.Add($"The student's other classes (name what sets this one apart from them): {string.Join(", ", others)}");
        void List(string head, IEnumerable<string> items, int most)
        {
            var list = items.Select(Py.Strip).Where(i => i.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).Take(most).ToList();
            if (list.Count == 0) return;
            lines.Add("");
            lines.Add(head);
            lines.AddRange(list.Select(i => "- " + i));
        }
        List("Modules on Canvas:", course.Modules.OrderBy(m => m.Position).Select(m => m.Name), 40);
        List("Pages and module items on Canvas:", course.Pages.Select(p => p.Title)
            .Concat(course.Modules.SelectMany(m => m.Items).Where(i => i.Kind is "page" or "header").Select(i => i.Title)), 40);
        List("Assignments on Canvas:", course.Assignments.Select(a => a.Name), 40);
        List("Topics its lectures have covered so far:", lectureTopics, 30);
        if (Py.Strip(syllabus) is { Length: > 0 } s)
        {
            lines.Add("");
            lines.Add("The syllabus (the start of it):");
            lines.Add(Py.Head(s, 3000));
        }
        lines.AddRange([
            "",
            "Return JSON with topics: 4 to 8 topics this class covers, taken from what's above, the most telling first, each a few words (\"entropy and the second law\").",
            "No sentences, no course codes, dates, week numbers, grading or schedule; together under 160 characters.",
        ]);
        return string.Join("\n", lines);
    }

    /// <summary>The topics in the AI's answer.</summary>
    static IEnumerable<string> TopicsIn(string answer) =>
        JsonNode.Parse(answer) is JsonObject o && o["topics"] is JsonArray a ? a.Select(Py.Str) : [];

    /// <summary>Canvas's own words for what the class covers, with no AI: its module titles with the week numbers,
    /// dates and numbering taken off, then its pages' and assignments' names if the modules say too little.</summary>
    public static string FromCanvas(Canvas.CourseIndex course)
    {
        var topics = new List<string>();
        void Add(IEnumerable<string> names)
        {
            foreach (string n in names)
                if (Topic(n) is { } t && !topics.Contains(t, StringComparer.OrdinalIgnoreCase)) topics.Add(t);
        }
        Add(course.Modules.OrderBy(m => m.Position).Select(m => m.Name));
        if (topics.Count < 3)
            Add(course.Modules.OrderBy(m => m.Position).SelectMany(m => m.Items).Where(i => i.Kind is "page" or "header").Select(i => i.Title)
                .Concat(course.Pages.Select(p => p.Title)));
        if (topics.Count < 3) Add(course.Assignments.Select(a => a.Name));
        return Join(topics);
    }

    /// <summary>Topics as one line: comma separated, as many whole ones as fit in <see cref="MaxChars"/>.</summary>
    static string Join(IEnumerable<string> topics)
    {
        string line = "";
        foreach (string t in topics.Select(t => Py.Strip(t).TrimEnd('.')).Where(t => t.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            string next = line.Length == 0 ? t : $"{line}, {t}";
            if (next.Length > MaxChars) break;
            line = next;
        }
        return line;
    }

    // "Week 3:", "Module 2 -", "Unit 4.", "HW 3:", "Lab 2 –", "L4 |", "Chapter 5" and the like, at the start of a title.
    const string Labels = @"week|wk|module|mod|unit|chapter|ch|lecture|lec|topic|part|session|day|class|lesson|section|"
        + @"homework|hw|lab|quiz|exam|midterm|problem\s*set|pset|ps|project|assignment|reading|discussion|worksheet|activity|l|m|w|q";

    [GeneratedRegex(@"^\s*(?:(?:" + Labels + @")\.?\s*#?\s*\d+[a-z]?(?:\.\d+)*\s*(?:[:\-–—|.)]\s*|\s+|$)|(?:" + Labels + @")\s*[:\-–—|]\s*|\d+[a-z]?(?:\.\d+)*\s*(?:[:\-–—|.)]\s*|\s+))", RegexOptions.IgnoreCase)]
    private static partial Regex Label();

    // A bracket with a number in it: "(Aug 26–30)", "[9/4]".
    [GeneratedRegex(@"\s*[(\[][^)\]]*\d[^)\]]*[)\]]")]
    private static partial Regex Bracketed();

    // Dates: "Sept 4", "Sep 4 - 8", "Oct 3, 2026", "9/4", "2026-09-04".
    [GeneratedRegex(@"\b(?:jan|feb|mar|apr|may|jun|jul|aug|sep|sept|oct|nov|dec)[a-z]*\.?\s+\d{1,2}(?:st|nd|rd|th)?(?:\s*[-–]\s*(?:[a-z]+\.?\s+)?\d{1,2})?(?:,?\s*\d{4})?\b|\b\d{1,4}[/-]\d{1,2}(?:[/-]\d{2,4})?\b", RegexOptions.IgnoreCase)]
    private static partial Regex Date();

    [GeneratedRegex(@"^[\s:\-–—|.,;/]+|[\s:\-–—|.,;/]+$")]
    private static partial Regex Ends();

    // Titles that say where things are, not what's taught.
    static readonly HashSet<string> NotTopics = new(StringComparer.OrdinalIgnoreCase)
    {
        "start here", "welcome", "getting started", "course information", "course info", "course overview", "overview",
        "syllabus", "course syllabus", "resources", "course resources", "announcements", "homework", "homeworks", "assignments",
        "exams", "exam", "midterm", "midterm exam", "final", "final exam", "finals", "review", "exam review", "final review",
        "quizzes", "labs", "office hours", "orientation", "introduction", "intro", "misc", "miscellaneous", "extra credit",
        "readings", "lecture notes", "lecture slides", "slides", "notes", "videos", "recordings", "zoom", "discussions",
        "practice", "solutions", "grades", "schedule", "calendar", "faq", "help", "wrap up", "wrap-up", "summary", "template",
    };

    /// <summary>A Canvas title as a topic ("Week 3: Entropy and the Second Law (Sep 8)" → "Entropy and the Second Law"),
    /// or null when nothing is left of it that says what's taught.</summary>
    public static string? Topic(string title)
    {
        string t = Bracketed().Replace(title, "");
        t = Date().Replace(t, " ");
        for (int i = 0; i < 3; i++)
        {
            string stripped = Label().Replace(Ends().Replace(t, ""), "", 1);
            if (stripped == t) break;
            t = stripped;
        }
        t = Ends().Replace(Regex.Replace(t, @"\s+", " "), "");
        if (t.Length < 3 || t.Length > 60 || !t.Any(char.IsLetter) || NotTopics.Contains(t)) return null;
        return t;
    }
}
