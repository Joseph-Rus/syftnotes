using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace StudyStash.Core.Study;

/// <summary>One thing that's important to remember from a lecture: the idea (a few words), what to know about it (a
/// sentence), and the moment the lecturer flagged it, with the sentence they flagged it in; or one they said to skip.</summary>
public sealed record KeyIdea(double? At, string Concept, string Detail, string Said, bool Not = false)
{
    /// <summary>"18:05", or "" without a time.</summary>
    public string Moment => At is { } s ? TimedText.Clock(s) : "";
}

/// <summary>
/// Important to remember: what a lecture's flags were about. A flag is found by what was said ("make sure you know
/// this"), and the sentence alone rarely says what "this" is, so the library's AI reads each one with the minute and a
/// half of the transcript before it and the half minute after, names the idea and says in a sentence what to know, and
/// drops the ones that weren't about anything. Kept in the library's important folder, one file a lecture, made again only
/// when its flags change.
/// </summary>
public sealed partial class Important(string home)
{
    string Dir => Path.Combine(home, "important");
    string PathOf(string lecture) => Path.Combine(Dir, Regex.Replace(lecture, @"[^A-Za-z0-9_-]", "_") + ".json");

    sealed record Kept(string Key, List<KeyIdea> Items);

    /// <summary>Which flags the kept file was made from.</summary>
    static string Key(IReadOnlyList<ExamHint> flags) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', flags.Select(f => $"{f.At}|{f.Not}|{f.Said}")))))[..16];

    /// <summary>What's kept for these flags; null when nothing is, or it was made from other flags.</summary>
    public List<KeyIdea>? Get(StudyLecture l)
    {
        try
        {
            if (SharedFile.Read(PathOf(l.Id)) is { } text && JsonSerializer.Deserialize<Kept>(text, Review.Options) is { } kept && kept.Key == Key(l.Flags))
                return kept.Items;
        }
        catch (JsonException)
        {
        }
        return null;
    }

    /// <summary>The kept ones, or the AI's names for the flags now (kept for next time). None for a lecture with no flags.</summary>
    public async Task<List<KeyIdea>> MakeAsync(StudyLecture l, StudyMaterial.Planner plan)
    {
        if (l.Flags.Count == 0) return [];
        if (Get(l) is { } kept) return kept;
        return Keep(l.Id, l.Flags, await plan(Prompt(l), Schema));
    }

    /// <summary>Keeps an AI's answer for a lecture's flags (from the notes' own call, or one of its own).</summary>
    public List<KeyIdea> Keep(string lecture, IReadOnlyList<ExamHint> flags, string json)
    {
        var items = Read(json, flags);
        Directory.CreateDirectory(Dir);
        SharedFile.Write(PathOf(lecture), JsonSerializer.Serialize(new Kept(Key(flags), items), Review.Options) + "\n");
        return items;
    }

    /// <summary>What the notes' own call is asked as well, so naming the flags costs no call of its own: the flags,
    /// numbered, and a block after the notes to answer in (taken off them by <see cref="Take"/>).</summary>
    public static string NotesAsk(IReadOnlyList<ExamHint> flags)
    {
        var sb = new StringBuilder("\n\nAlso: the speaker flagged these moments as worth remembering (or, marked SKIP, as not ")
            .Append("needed). The flagged sentence alone usually doesn't say what \"this\" or \"it\" is; the transcript round it ")
            .Append("does.\n");
        for (int i = 0; i < flags.Count; i++)
            sb.Append($"Flag {i + 1}{(flags[i].Not ? " (SKIP)" : "")}{(flags[i].Moment.Length > 0 ? $" at {flags[i].Moment}" : "")}: \"{flags[i].Said}\"\n");
        sb.Append("After the notes, on its own, add one ```important code block holding a JSON object that fits this schema: ")
          .Append(Schema.ToJsonString())
          .Append(". For each flag, \"concept\" names the idea it's about (2 to 7 words, a noun phrase in the speaker's own ")
          .Append("terms) and \"detail\" says in one short sentence what to know about it. \"keep\" is false for a flag that ")
          .Append("isn't about anything to remember (a figure of speech, a rhetorical \"right?\", admin) and for one that repeats ")
          .Append("an earlier flag's idea. \"flag\" is the flag's number. The block is read by the app, not shown in the notes.");
        return sb.ToString();
    }

    [GeneratedRegex(@"\n*```important[ \t]*\n(?<json>[\s\S]*?)\n?```[ \t]*", RegexOptions.IgnoreCase)]
    private static partial Regex Block();

    /// <summary>The notes with the ```important block taken off, and the JSON it held (null with none, or none whole).</summary>
    public static (string Notes, string? Json) Take(string reply)
    {
        if (Block().Match(reply) is not { Success: true } m) return (reply, null);
        string notes = (reply[..m.Index] + reply[(m.Index + m.Length)..]).TrimEnd() + "\n";
        return (notes, Ai.AiJobs.FirstObject(m.Groups["json"].Value));
    }

    public static readonly JsonObject Schema = JsonNode.Parse("""
        {"type":"object","properties":{"items":{"type":"array","items":{"type":"object","properties":{
          "flag":{"type":"integer"},"keep":{"type":"boolean"},"concept":{"type":"string"},"detail":{"type":"string"}},
          "required":["flag","keep","concept","detail"]}}},
         "required":["items"]}
        """)!.AsObject();

    /// <summary>What an AI is asked: each flag with what was said round it, then the lecture's notes.</summary>
    public static string Prompt(StudyLecture l)
    {
        var lines = TimedText.HasTimes(l.Transcript) ? TimedText.Parse(l.Transcript) : [];
        var sb = new StringBuilder();
        sb.Append("A lecturer flagged these moments in a lecture as worth remembering (or, marked SKIP, as not needed). ")
          .Append("The flagged sentence alone usually doesn't say what \"this\" or \"it\" is; the transcript around it does. ")
          .Append("For each flag, name the idea it's about as \"concept\" (2 to 7 words, a noun phrase in the lecturer's ")
          .Append("own terms, e.g. \"Three layers of an artery wall\"), and in \"detail\" say in one short sentence what to ")
          .Append("know about it, from the transcript and the notes. Set \"keep\" false for a flag that isn't really about ")
          .Append("anything to remember (a figure of speech, a rhetorical \"right?\", admin like where to hand in work), and ")
          .Append("for one that repeats an earlier flag's idea. \"flag\" is the flag's number.\n\nLecture: ")
          .Append(l.Title.Length > 0 ? l.Title : "Untitled").Append('\n');
        for (int i = 0; i < l.Flags.Count; i++)
        {
            var f = l.Flags[i];
            sb.Append($"\nFlag {i + 1}{(f.Not ? " (SKIP)" : "")}{(f.Moment.Length > 0 ? $" at {f.Moment}" : "")}: \"{f.Said}\"\n");
            if (f.At is { } at && lines.Count > 0)
            {
                sb.Append("Around it:\n");
                foreach (var s in lines.Where(s => s.Start >= at - 90 && s.Start <= at + 30))
                    sb.Append("  [").Append(TimedText.Clock(s.Start)).Append("] ").Append(s.Text).Append('\n');
            }
        }
        string notes = l.Notes.Trim();
        if (notes.Length > 0) sb.Append("\nThe lecture's notes:\n").Append(notes.Length > 12000 ? notes[..12000] + "\n…" : notes).Append('\n');
        return sb.ToString();
    }

    /// <summary>The AI's answer as things to remember, in the order flagged; a flag it didn't name is left out.</summary>
    public static List<KeyIdea> Read(string json, IReadOnlyList<ExamHint> flags)
    {
        var named = new Dictionary<int, KeyIdea>();
        foreach (var o in (JsonNode.Parse(json)?["items"] as JsonArray ?? []).OfType<JsonObject>())
        {
            if (o["flag"] is not JsonValue v || !v.TryGetValue(out int n) || n < 1 || n > flags.Count) continue;
            if (o["keep"] is JsonValue k && k.TryGetValue(out bool keep) && !keep) continue;
            string concept = StudyMaterial.Text(o["concept"]).TrimEnd('.'), detail = StudyMaterial.Text(o["detail"]);
            if (concept.Length == 0) continue;
            var f = flags[n - 1];
            named.TryAdd(n, new KeyIdea(f.At, concept, detail, f.Said, f.Not));
        }
        return [.. named.OrderBy(p => p.Key).Select(p => p.Value)];
    }
}
