using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace StudyStash.Core.Ai;

/// <summary>
/// The words of a lecture's notes an answer was built on, as the AI hands them back with its answer: up to four short
/// passages copied word for word, which the app highlights in the notes. They travel in the reply's own JSON (a
/// "quotes" list beside "sources"), so the answer's text, and what streams while it's written, never has them in it. An
/// AI that leaves them out gives an answer with no highlights; one that gives odd ones (too long, empty, twice) is
/// tidied here.
/// </summary>
public static partial class AskQuotes
{
    public const int Most = 4;

    /// <summary>What the AI is told about them, said once among the question's instructions.</summary>
    public const string Instructions =
        "Also return quotes: up to 4 passages from the sources marked (notes) that your answer is built on, each copied exactly, word for word, "
        + "from its source (about 4 to 25 words, one unbroken piece: don't join pieces, shorten the middle, reword, or add quote marks or formatting). "
        + "The student sees them highlighted in their notes. Use an empty list when the answer doesn't come from the notes.\n";

    /// <summary>The reply's JSON shape when quotes are asked for: the answer and its sources as always, and the quotes.</summary>
    public static JsonObject Schema(JsonObject plain)
    {
        var schema = (JsonObject)plain.DeepClone();
        ((JsonObject)schema["properties"]!)["quotes"] = new JsonObject { ["type"] = "array", ["items"] = new JsonObject { ["type"] = "string" } };
        schema["required"] = new JsonArray("answer", "sources", "quotes");
        return schema;
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();

    /// <summary>The quotes in a reply's "quotes" (anything else, or nothing, is none): each trimmed of the quote marks the
    /// AI put round it, one line, at least two words and at most sixty, no two the same, the first four.</summary>
    public static List<string> Clean(JsonNode? quotes)
    {
        var kept = new List<string>();
        if (quotes is not JsonArray list) return kept;
        foreach (var item in list)
        {
            if (item is not JsonValue v || !v.TryGetValue(out string? raw) || raw is null) continue;
            string q = Spaces().Replace(raw, " ").Trim().Trim('"', '“', '”', '\'', '‘', '’').Trim();
            int words = q.Count(c => c == ' ') + 1;
            if (q.Length < 8 || words < 2 || words > 60) continue;
            if (kept.Any(k => string.Equals(k, q, StringComparison.OrdinalIgnoreCase))) continue;
            kept.Add(q);
            if (kept.Count == Most) break;
        }
        return kept;
    }
}
