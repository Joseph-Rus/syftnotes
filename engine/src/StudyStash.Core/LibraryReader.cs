using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using StudyStash.Core.Ai;
using StudyStash.Core.Rich;

namespace StudyStash.Core;

/// <summary>
/// The library as the Study Stash app and Claude read it: its classes, lectures, notes, transcripts, search, and
/// answers from your notes. Every answer is plain JSON, the same over /api/v2 and from the MCP tools.
/// </summary>
public sealed partial class LibraryReader(Config cfg, Store store)
{
    public Config Cfg { get; } = cfg;
    public Store Store { get; } = store;

    /// <summary>The library's name, its classes (in the order they were added: their dot colors follow it), and how
    /// much is waiting.</summary>
    public JsonObject Overview()
    {
        var counts = Store.ClassesSummary().ToDictionary(c => c.ClassName, c => c.Count);
        var classes = new JsonArray();
        int index = 0;
        var canvas = Canvas.CanvasSettings.Load(Cfg.Home);
        var linked = Ai.Folders.Load(Cfg.Home);
        foreach (var c in Cfg.Classes)
            classes.Add(new JsonObject
            {
                ["name"] = c.Name, ["lectures"] = counts.GetValueOrDefault(c.Name), ["color"] = index++, ["description"] = c.Description,
                // The sidebar section it's in: "" for Classes, else one of the student's own folders ("Projects").
                ["group"] = c.Group,
                // Folders on the library's computer that go with it (a project's repo, say), not part of the library.
                ["folders"] = new JsonArray(linked.Where(f => f.Class == c.Name).Select(f => (JsonNode?)new JsonObject
                {
                    ["path"] = f.Path, ["name"] = f.Name, ["here"] = Directory.Exists(f.Path),
                }).ToArray()),
                // The linked Canvas course's short code ("CSCI 321"), a label beside the name; "" when there's none.
                ["code"] = ShortCodeOf(canvas, c.Name),
                // Its other names (one it had before a rename, say), so a laptop's lectures filed under an old name can follow it.
                ["aliases"] = new JsonArray(c.Aliases.Select(a => (JsonNode?)a).ToArray()),
            });
        // Lectures filed under a class since removed from the config still show, after the rest.
        foreach (var (name, n) in counts.Where(kv => kv.Key != Configs.Unsorted && Cfg.Classes.All(c => c.Name != kv.Key)))
            classes.Add(new JsonObject { ["name"] = name, ["lectures"] = n, ["color"] = index++ });
        var status = Store.StatusCounts();
        return new JsonObject
        {
            ["name"] = Cfg.PoolName,
            ["version"] = Engine.Version,
            ["classes"] = classes,
            ["unsorted"] = counts.GetValueOrDefault(Configs.Unsorted),
            ["writing"] = status.GetValueOrDefault(Store.Queued) + status.GetValueOrDefault(Store.Working),
            ["ask"] = Cfg.OllamaEnabled,
            ["notes_model"] = Cfg.SummaryEnabled && Cfg.OllamaEnabled ? Cfg.EffectiveSummaryModel : null,
            // What it's for (empty until asked) and so whether it's for school: classes, Canvas and due dates, or folders.
            ["uses"] = new JsonArray(Cfg.Uses.Select(u => (JsonNode?)u).ToArray()),
            ["for_school"] = Cfg.ForSchool,
        };
    }

    static string ShortCodeOf(Canvas.CanvasSettings s, string cls) =>
        s.Courses.TryGetValue(cls, out long id) && s.CourseInfo.TryGetValue(id.ToString(CultureInfo.InvariantCulture), out var info)
            ? Canvas.CourseNames.ShortCode(info.Code, info.Name) : "";

    /// <summary>Lectures newest first, in one class (or Unsorted) or all, before a date to page back.</summary>
    public JsonArray Lectures(string? className = null, int limit = 50, string? before = null)
    {
        var rows = Store.ListNotes(className);
        if (!string.IsNullOrEmpty(before)) rows = rows.Where(r => string.CompareOrdinal(r.Date ?? "", before) < 0).ToList();
        return new JsonArray(rows.Take(Math.Clamp(limit, 1, 500)).Select(r => (JsonNode?)Summary(r)).ToArray());
    }

    public JsonObject? Lecture(string id)
    {
        var row = Store.Get(id);
        if (row is null) return null;
        var m = Store.Meeting(row);
        var result = Summary(row);
        result["notes"] = NotesOf(row, m);
        // What the student typed themselves during the lecture, as they typed it: the notes are written round it
        // (their lines marked ==like this==), and with no notes written it's all there is to read.
        result["typed"] = m.PrivateNotes;
        result["transcript"] = m.Transcript;
        result["classified_by"] = row.ClassifiedBy;
        result["notes_model"] = row.SummaryModel;
        result["error"] = row.Error;
        // The notes are the library's own (written for it, or saved by the student), not ones the lecture came with:
        // an error beside those is a rewrite that didn't happen, and they're as they were.
        result["notes_kept"] = !string.IsNullOrEmpty(row.SummaryMd) && !string.IsNullOrEmpty(row.Error);
        // Why no notes were written from it, when the library knows: the app and the phone say so where the notes
        // would be. The words are for the phone, which can't work them out itself.
        string? why = WhyNoNotes(row, m);
        result["no_notes"] = why;
        result["no_notes_why"] = why switch
        {
            Summarize.NoNotesShort => Summarize.TooShortWords("This lecture"),
            Summarize.NoNotesOff => Summarize.NotesOffWords("this lecture"),
            _ => null,
        };
        return result;
    }

    /// <summary>Why nothing writes notes for a lecture, when that's known: <see cref="Summarize.NoNotesOff"/> while the
    /// library writes none at all, <see cref="Summarize.NoNotesShort"/> for one with too little said to write them
    /// from. Either is known as soon as it arrives (so nothing says its notes are on their way while it's filed). Null
    /// when an AI wrote its notes, when it has the notes it came with, and when notes were asked for and none came
    /// back (its error says why, if anything does).</summary>
    string? WhyNoNotes(NoteRow row, Meeting m)
    {
        bool written = !string.IsNullOrEmpty(row.SummaryMd) && !string.IsNullOrEmpty(row.SummaryModel);
        if (written || Py.Strip(m.NotesMarkdown).Length > 0) return null;
        return !Summarize.NotesOn(Cfg) ? Summarize.NoNotesOff : Summarize.TooShort(m) ? Summarize.NoNotesShort : null;
    }

    /// <summary>The notes a lecture shows: ours, written from the transcript, or else the ones it came with.</summary>
    static string NotesOf(NoteRow row, Meeting m) => !string.IsNullOrEmpty(row.SummaryMd) ? row.SummaryMd : m.NotesMarkdown;

    JsonObject Summary(NoteRow r)
    {
        var raw = JsonNode.Parse(string.IsNullOrEmpty(r.RawJson) ? "{}" : r.RawJson) as JsonObject;
        double? seconds = raw?["seconds"] is JsonValue v && v.TryGetValue(out double s) ? s : null;
        string notes = r.SummaryMd ?? "";
        if (notes.Length == 0 || seconds is null)
        {
            var m = Store.Meeting(r);
            if (notes.Length == 0) notes = m.NotesMarkdown;
            if (seconds is null && TimedText.HasTimes(m.Transcript)) seconds = TimedText.Parse(m.Transcript).LastOrDefault()?.End;
        }
        string status = string.IsNullOrEmpty(r.Status) ? Store.Done : r.Status;
        return new JsonObject
        {
            ["id"] = r.Id,
            ["title"] = !string.IsNullOrEmpty(r.LectureTitle) ? r.LectureTitle : r.Title,
            ["class"] = r.ClassName,
            ["date"] = r.Date,
            ["seconds"] = seconds,
            ["owner"] = r.Owner,
            ["status"] = status,
            ["summary"] = FirstSentence(notes),
            ["topics"] = JsonNode.Parse(string.IsNullOrEmpty(r.Topics) ? "[]" : r.Topics),
            ["has_transcript"] = r.HasTranscript is > 0,
        };
    }

    [GeneratedRegex(@"(?<=[.!?])\s+(?=[A-Z0-9“""(])")]
    private static partial Regex SentenceEnd();

    [GeneratedRegex(@"\*\*|__|`|\[([^\]]*)\]\([^)]*\)")]
    private static partial Regex Markup();

    [GeneratedRegex(@"\A\s*---[ \t]*\r?\n.*?\r?\n---[ \t]*(\r?\n|\z)", RegexOptions.Singleline)]
    private static partial Regex FrontMatter();

    // The marks round what the student wrote themselves (==like this==, Summarize.Mark): never shown as they're kept.
    [GeneratedRegex(@"==(?=\S)(.+?)(?<=\S)==")]
    private static partial Regex Typed();

    [GeneratedRegex(@"\A(?:[-*+]|\d+[.)])\s+")]
    private static partial Regex ListItem();

    /// <summary>The lecture in a line: the first sentence of its Summary (or Overview), for lists.</summary>
    public static string FirstSentence(string notes)
    {
        // Notes brought in from another app can start with its front matter ("---", "granola_id: …", "---"): not the lecture.
        notes = FrontMatter().Replace(notes, "", 1);
        string text = "";
        foreach (string heading in new[] { "Summary", "Overview" })
            if ((text = Notes.Section(notes, heading)).Length > 0) break;
        if (text.Length == 0)
        {
            var lines = notes.ReplaceLineEndings("\n").Split('\n').Select(l => l.Trim()).SkipWhile(l => l.Length == 0 || l.StartsWith('#'))
                .TakeWhile(l => l.Length > 0 && !l.StartsWith('#')).ToList();
            // A list (a student's own notes, a bullet a line) is said by its first item, not all of them run together.
            text = lines.Count > 0 && ListItem().Match(lines[0]) is { Success: true } item ? lines[0][item.Length..] : string.Join(" ", lines);
        }
        text = Typed().Replace(Markup().Replace(text, m => m.Groups[1].Success ? m.Groups[1].Value : ""), "$1").Replace('\n', ' ').Trim();
        string first = SentenceEnd().Split(text).FirstOrDefault() ?? "";
        return first.Length > 200 ? first[..197].TrimEnd() + "…" : first;
    }

    /// <summary>What the search box finds as you type: lectures by title and topic, passages from notes and
    /// transcripts, and classes by name.</summary>
    public JsonObject Search(string query, string? className = null, int limit = 8)
    {
        query = query.Trim();
        var result = new JsonObject { ["query"] = query, ["lectures"] = new JsonArray(), ["passages"] = new JsonArray(), ["classes"] = new JsonArray() };
        if (query.Length == 0) return result;
        var lectures = (JsonArray)result["lectures"]!;
        foreach (var r in Store.Search(query, 50).Where(r => className is null || r.ClassName == className).Take(limit)) lectures.Add(Summary(r));
        var passages = (JsonArray)result["passages"]!;
        if (Passages.AllWords(query) is string match)
            foreach (var hit in Store.SearchPassages(match, className, limit: limit * 2).Take(limit * 2)) passages.Add(PassageJson(hit.Note, hit.Passage));
        var classes = (JsonArray)result["classes"]!;
        foreach (var c in ((JsonArray)Overview()["classes"]!).OfType<JsonObject>())
            if (c["name"]!.GetValue<string>().Contains(query, StringComparison.OrdinalIgnoreCase)) classes.Add(c.DeepClone());
        return result;
    }

    static JsonObject PassageJson(NoteRow note, Passage p) => new()
    {
        ["id"] = note.Id,
        ["title"] = !string.IsNullOrEmpty(note.LectureTitle) ? note.LectureTitle : note.Title,
        ["class"] = note.ClassName,
        ["date"] = note.Date,
        ["kind"] = p.Kind,
        ["section"] = p.Section,
        ["at"] = p.Start,
        ["text"] = p.Text,
    };

    // --- asking your notes ----------------------------------------------------------------------------------------

    /// <summary>(prompt, JSON schema) → the model's answer, as JSON text.</summary>
    public delegate Task<string> AskChatFn(string prompt, JsonObject schema);

    public const string NoAnswer = "I couldn't find that in your notes.";

    /// <summary>A passage Ask picked from a file the student attached.</summary>
    public const string AttachmentKind = "attachment";

    static readonly JsonObject AnswerSchema = new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            ["answer"] = new JsonObject { ["type"] = "string" },
            ["sources"] = new JsonObject { ["type"] = "array", ["items"] = new JsonObject { ["type"] = "integer" } },
        },
        ["required"] = new JsonArray("answer", "sources"),
    };

    /// <summary>
    /// A question answered from your own lectures by the library's model: the passages that best match it (in one
    /// lecture, one class, or all), and while you record, what's been said so far. The answer names its sources, each
    /// with the moment in the recording when there is one. A question about one lecture reads that lecture and no
    /// other, the one being recorded too: the library doesn't have it yet, so what's been said so far is all of it.
    /// <para>A follow-up comes with the conversation so far (<paramref name="earlier"/>, oldest first, already cut to
    /// what the answering AI is given: <see cref="AskMemory.For"/>). Its search reads the previous question too, and the
    /// passages the previous answer drew on are read again, so "explain that more simply" is about the same thing; the
    /// AI reads the turns before the question. The answer says how many went with it ("remembered").</para>
    /// </summary>
    public async Task<JsonObject> AskAsync(string question, string? lectureId = null, string? className = null,
        string? liveTranscript = null, string liveTitle = "This lecture", AskChatFn? chat = null, IReadOnlyList<AskTurn>? earlier = null)
    {
        earlier ??= [];
        // What to look for: the question, and with a follow-up the one before it ("and the second one?" says nothing of what).
        string lookFor = earlier.Count > 0 ? question + "\n" + earlier[^1].Question : question;
        var picked = new List<(NoteRow? Note, Passage Passage)>();
        bool live = !string.IsNullOrWhiteSpace(liveTranscript);
        var heard = live ? Passages.FromTranscript("live", liveTranscript!) : [];
        // The passages the previous answer drew on, first: what the conversation is about.
        if (earlier.Count > 0) picked.AddRange(DrawnOn(earlier[^1].Sources, lectureId, className, heard));
        void Add(NoteRow? note, Passage p)
        {
            if (!picked.Any(x => x.Passage == p)) picked.Add((note, p));
        }
        foreach (var p in BestOf(heard, lookFor, 6)) Add(null, p);
        if (lectureId is not null)
        {
            // A note that's being recorded again is in the library as it was: its notes are read too, and its
            // transcript came with the question, as far as it has got.
            if (Store.Get(lectureId) is { } one)
                foreach (var p in BestOf([.. Store.PassagesOf(lectureId).Where(p => !live || p.Kind != Passage.TranscriptKind)], lookFor, 8)) Add(one, p);
        }
        else if (Passages.AnyWords(lookFor) is string match)
        {
            foreach (var hit in Store.SearchPassages(match, className, limit: 10)) Add(hit.Note, hit.Passage);
        }
        // What the student attached to the lecture (or anywhere in the class): the pieces that best match, too.
        var attachedTo = new Dictionary<Passage, Attachment>(ReferenceEqualityComparer.Instance);
        var files = lectureId is not null ? Store.ListAttachments(noteId: lectureId) : className is not null ? Store.ListAttachments(className) : [];
        var pieces = files.Where(a => a.HasText).SelectMany(a => Attachments.Pieces(a.Text).Select(t =>
        {
            var p = new Passage(a.NoteId ?? "", AttachmentKind, $"{Attachments.KindOf(a.Name, a.Type)}: {a.Name}", null, t);
            attachedTo[p] = a;
            return p;
        })).ToList();
        foreach (var p in BestOf(pieces, lookFor, 4))
            Add(attachedTo[p].NoteId is { } id ? Store.Get(id) : null, p);
        var sources = new JsonArray();
        if (picked.Count == 0) return new JsonObject { ["answer"] = NoAnswer, ["sources"] = sources, ["remembered"] = 0 };

        var prompt = new StringBuilder("""
            You answer a student's question from their own lecture notes and transcripts, numbered below.
            Use only these sources. If they don't answer the question, say so in one sentence.
            Answer in one to three plain sentences, the way a classmate who took good notes would. Don't mention the sources by number in the answer.
            Write any formula in LaTeX between $ signs; in the JSON, write each backslash twice (\\frac). If the student asks you to draw or show something, or the answer is a short process, you may add one small Mermaid flowchart after the sentences, in a ```mermaid block (flowchart LR or TD, a few words per box).
            Return JSON: answer (the answer), sources (the numbers of the sources you used, most important first).

            """);
        // Asked what a formula looks like (or to plot one): the answer may draw it, exactly, as a plot to play with.
        if (PlotDesign.AsksForPlot(question))
            prompt.Append("If the student asks what a formula or function looks like, or asks you to plot or graph it, add one ```plot block after the sentences instead of a flowchart: ")
                .Append("the formula as the sources give it, a slider for each parameter they vary, ranges where its shape shows. In the JSON, write its quotes as \\\" and its line breaks as \\n.\n")
                .Append(PlotDesign.Reference).Append('\n');
        for (int i = 0; i < picked.Count; i++)
        {
            var (note, p) = picked[i];
            bool file = attachedTo.ContainsKey(p);
            string where = file ? note is null ? $"Attached to {attachedTo[p].ClassName}" : $"Attached to {Title(note)} ({note.ClassName}, {note.Date})"
                : note is null ? liveTitle : $"{Title(note)} ({note.ClassName}, {note.Date})";
            string at = p.Start is double s ? $" at {TimedText.Clock(s)}" : p.Section.Length > 0 ? $", {p.Section}" : "";
            prompt.Append('[').Append(i + 1).Append("] ").Append(where).Append(at).Append(":\n").Append(p.Text).Append("\n\n");
        }
        if (earlier.Count > 0)
        {
            // Said last, just before the question: a small model heeds what it read most recently.
            prompt.Append("The conversation so far, oldest first:\n\n");
            foreach (var turn in earlier) prompt.Append("Student: ").Append(turn.Question).Append("\nYou: ").Append(turn.Answer).Append("\n\n");
            prompt.Append("The new question follows on from it. Answer only the new question, from the sources above: when it says \"that\", \"it\" ")
                .Append("or \"the second one\", work out from the conversation what it means, and don't repeat an earlier answer.\n");
        }
        prompt.Append("Question: ").Append(question.Trim());
        string text = await (chat ?? OllamaAsk)(prompt.ToString(), AnswerSchema);
        string answer;
        List<int> used;
        try
        {
            var data = ParseAnswer(text);
            answer = Py.Strip(MathText.RepairJsonEscapes(Py.AsString(data?["answer"]) ?? ""));
            used = (data?["sources"] as JsonArray ?? []).Select(n => n is JsonValue v && v.TryGetValue(out int k) ? k : 0).ToList();
        }
        catch (System.Text.Json.JsonException)
        {
            answer = Py.Strip(text);
            used = [];
        }
        if (answer.Length == 0) answer = NoAnswer;
        foreach (int k in used.Where(k => k >= 1 && k <= picked.Count).Distinct())
        {
            var (note, p) = picked[k - 1];
            var source = new JsonObject
            {
                ["id"] = note?.Id, ["title"] = note is null ? liveTitle : Title(note), ["class"] = note?.ClassName,
                ["date"] = note?.Date, ["at"] = p.Start, ["section"] = p.Section, ["text"] = p.Text,
            };
            if (attachedTo.TryGetValue(p, out var a))
            {
                // An attached file: named for itself, with its id so the app can open it.
                source["title"] = a.Name;
                source["class"] = a.ClassName;
                source["attachment"] = a.Id;
            }
            sources.Add(source);
        }
        return new JsonObject { ["answer"] = answer, ["sources"] = sources, ["remembered"] = earlier.Count };
    }

    /// <summary>The passages an earlier answer drew on, found again where the question looks now: one of this lecture's
    /// (or another's, when the question looks further) by its moment, or a section of its notes whole (each bullet is a
    /// passage of its own, and "the second one" is about the bullets the answer didn't name); one of what's being
    /// recorded by its moment. An attached file's is found again by the search, if at all.</summary>
    List<(NoteRow? Note, Passage Passage)> DrawnOn(IReadOnlyList<AskSource> sources, string? lectureId, string? className, List<Passage> heard)
    {
        static bool Same(Passage p, AskSource s) => s.At is double at ? p.Start is double start && Math.Abs(start - at) < 0.5
            : p.Start is null && p.Kind == Passage.NotesKind && p.Section == s.Section;
        var found = new List<(NoteRow?, Passage)>();
        foreach (var s in sources.Take(4))
        {
            IEnumerable<Passage> again;
            NoteRow? note = null;
            if (s.Id is null || (s.Id == lectureId && heard.Count > 0 && s.At is not null)) again = heard.Where(p => Same(p, s)).Take(1);
            else if ((lectureId is not null && s.Id != lectureId) || (note = Store.Get(s.Id)) is null || (className is not null && note.ClassName != className)) continue;
            else again = Store.PassagesOf(s.Id).Where(p => Same(p, s)).Take(s.At is null ? 6 : 1);
            foreach (var p in again)
                if (!found.Any(f => f.Item2 == p)) found.Add((note, p));
        }
        return found.Count > 8 ? found[..8] : found; // what came before leaves room for what the new question finds
    }

    /// <summary>The answer's JSON; when it doesn't parse because its LaTeX kept single backslashes, it parses with
    /// them doubled.</summary>
    static JsonObject? ParseAnswer(string text)
    {
        try
        {
            return JsonNode.Parse(text) as JsonObject;
        }
        catch (System.Text.Json.JsonException) when (text.Contains('\\'))
        {
            return JsonNode.Parse(MathText.DoubleLoneBackslashes(text)) as JsonObject;
        }
    }

    static string Title(NoteRow n) => !string.IsNullOrEmpty(n.LectureTitle) ? n.LectureTitle : n.Title ?? "";

    /// <summary>A lecture's passages that share the most words with the question (ties keep the order they came in).</summary>
    static IEnumerable<Passage> BestOf(List<Passage> passages, string question, int n)
    {
        var words = Passages.Words(question).Where(w => w.Length > 2).ToHashSet();
        return passages.Select((p, i) => (p, i, score: Passages.Words(p.Text).Count(words.Contains)))
            .OrderByDescending(x => x.score).ThenBy(x => x.i).Take(n).OrderBy(x => x.i).Select(x => x.p);
    }

    Task<string> OllamaAsk(string prompt, JsonObject schema) => OllamaAskAsync(Cfg, prompt, schema);

    /// <summary>An answer from the library's own Ollama model.</summary>
    public static async Task<string> OllamaAskAsync(Config Cfg, string prompt, JsonObject schema)
    {
        if (!Cfg.OllamaEnabled) throw new InvalidOperationException("Asking needs the library's AI model: turn it on in the library's Settings.");
        var body = new JsonObject
        {
            ["model"] = Cfg.EffectiveSummaryModel,
            ["messages"] = new JsonArray(new JsonObject { ["role"] = "user", ["content"] = prompt }),
            ["stream"] = false,
            ["think"] = false,
            ["format"] = schema.DeepClone(),
            ["options"] = new JsonObject { ["temperature"] = 0.1, ["num_ctx"] = 8192, ["num_predict"] = 400 },
        };
        var data = await Ollama.PostAsync(Cfg.OllamaHost, "/api/chat", body, TimeSpan.FromSeconds(120));
        return Py.AsString(data?["message"]?["content"]) ?? "";
    }
}
