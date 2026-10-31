using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace StudyStash.Core.Study;

/// <summary>What a lecture gives the study tools to work from: its notes (or, with none, its transcript), what the
/// lecturer flagged for the exam, and where it is.</summary>
public sealed record StudyLecture(string Id, string Title, string Class, string Date, string Notes, string Transcript, IReadOnlyList<ExamHint> Flags)
{
    /// <summary>The lecture as an AI is given it: its title and date, what was flagged for the exam with the moments,
    /// and its notes (cut to <paramref name="max"/> characters), or its transcript when there are no notes.</summary>
    public string ForAi(int max = 24000)
    {
        var sb = new StringBuilder();
        sb.Append("Lecture: ").Append(Title.Length > 0 ? Title : "Untitled");
        if (Date.Length > 0) sb.Append(" (").Append(Date).Append(')');
        sb.Append('\n');
        var on = Flags.Where(f => !f.Not).ToList();
        if (on.Count > 0)
        {
            sb.Append("\nThe lecturer said these will be on the exam (or to know them):\n");
            foreach (var f in on) sb.Append("- ").Append(f.Moment.Length > 0 ? $"[{f.Moment}] " : "").Append(f.Said).Append('\n');
        }
        var off = Flags.Where(f => f.Not).ToList();
        if (off.Count > 0)
        {
            sb.Append("\nThe lecturer said these won't be:\n");
            foreach (var f in off) sb.Append("- ").Append(f.Moment.Length > 0 ? $"[{f.Moment}] " : "").Append(f.Said).Append('\n');
        }
        string body = Notes.Trim().Length > 0 ? Notes.Trim() : Transcript.Trim();
        sb.Append(Notes.Trim().Length > 0 ? "\nNotes:\n" : "\nTranscript:\n").Append(Cut(body, max)).Append('\n');
        return sb.ToString();
    }

    static string Cut(string text, int max) => text.Length <= max ? text : text[..max] + "\n…";

    public bool HasWords => Notes.Trim().Length > 0 || Transcript.Trim().Length > 0;
}

/// <summary>Reading a lecture for the study tools, and asking an AI for flashcards from it.</summary>
public static partial class StudyMaterial
{
    /// <summary>A lecture from the library, with its exam flags found; null when there's no such lecture.</summary>
    public static StudyLecture? Read(Store store, string id)
    {
        if (store.Get(id) is not { } row) return null;
        var m = store.Meeting(row);
        string notes = !string.IsNullOrEmpty(row.SummaryMd) ? row.SummaryMd : m.NotesMarkdown;
        return new StudyLecture(row.Id, row.Title ?? m.Title, row.ClassName ?? "", row.Date ?? m.Date, notes ?? "", m.Transcript ?? "", ExamHints.Find(m.Transcript ?? ""));
    }

    /// <summary>A JSON answer from the library's AI: (prompt, schema) → the JSON text.</summary>
    public delegate Task<string> Planner(string prompt, JsonObject schema);

    public static readonly JsonObject CardsSchema = JsonNode.Parse("""
        {"type":"object","properties":{"cards":{"type":"array","items":{"type":"object","properties":{
          "front":{"type":"string"},"back":{"type":"string"},"moment":{"type":"string"}},"required":["front","back"]}}},
         "required":["cards"]}
        """)!.AsObject();

    /// <summary>What an AI is asked for a lecture's flashcards.</summary>
    public static string CardsPrompt(StudyLecture l, int count) =>
        $"Write {count} flashcards for reviewing this lecture with spaced repetition. Each card tests one thing: a term, a " +
        "fact, a formula, a cause and its effect, a step in a process, or how two ideas differ. The front is a question " +
        "that can be answered from memory in a few seconds, without seeing the lecture; the back is the answer, short " +
        "(a sentence, a formula, a few words), in the lecturer's own terms. Make a card for everything the lecturer said " +
        "would be on the exam, and none for what they said won't be. No two cards ask the same thing. Where the moment " +
        "it was taught is known, give it as \"moment\" (mm:ss).\n\n" + l.ForAi();

    /// <summary>Flashcards for a lecture from the AI: new cards, due now. None from a lecture with no words.</summary>
    public static async Task<List<ReviewCard>> MakeCardsAsync(Planner plan, StudyLecture l, DateTimeOffset now, int count = 12)
    {
        if (!l.HasWords) return [];
        string json = await plan(CardsPrompt(l, count), CardsSchema);
        var cards = new List<ReviewCard>();
        foreach (var c in (JsonNode.Parse(json)?["cards"] as JsonArray ?? []).OfType<JsonObject>())
        {
            string front = Text(c["front"]), back = Text(c["back"]);
            if (front.Length == 0 || back.Length == 0) continue;
            cards.Add(Review.New(l.Id, l.Class, l.Title, front, back, Seconds(Text(c["moment"])), "notes", now));
        }
        return cards;
    }

    public static string Text(JsonNode? n) => n is JsonValue v && v.TryGetValue(out string? s) ? s.Trim() : "";

    [GeneratedRegex(@"^\[?(?:(\d+):)?(\d{1,2}):(\d{2})\]?$")]
    private static partial Regex Clock();

    /// <summary>"02:30" or "1:02:30" in seconds; null for anything else.</summary>
    public static double? Seconds(string moment)
    {
        if (Clock().Match(moment.Trim()) is not { Success: true } m) return null;
        int h = m.Groups[1].Success ? int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) : 0;
        return h * 3600 + int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture) * 60 + int.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture);
    }
}
