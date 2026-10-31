using System.Text;
using System.Text.RegularExpressions;

namespace StudyStash.App.Controls;

/// <summary>
/// Finding the words an answer quoted in a lecture's notes, as the page shows them. The AI copies passages from the
/// notes' Markdown, and a model's copy is rarely exact: its spacing and line breaks differ, its quote marks are curly or
/// straight, it keeps the **bold** stars and a bullet's dash the page doesn't show, it changes a capital. So the match
/// ignores all of those, and when no run of the notes' words matches whole, the stretch of the notes that shares most
/// of the quote's words (three at least, and most of them) stands in. A quote that isn't there at all is not found.
/// </summary>
public static partial class QuoteFinder
{
    /// <summary>Where a quote is in a text: the characters from <see cref="Start"/>, <see cref="Length"/> of them, and how
    /// well it matched (1 for the same words; less for a near match).</summary>
    public readonly record struct Match(int Start, int Length, double Score);

    /// <summary>A near match has to share this much of the quote's words.</summary>
    const double Near = 0.7;

    [GeneratedRegex(@"(?m)^[ \t]*(?:[-*+•·▪◦]|\d+[.)]|>|#{1,6})[ \t]+")]
    private static partial Regex LineLead();

    [GeneratedRegex(@"!?\[([^\]]*)\]\([^)]*\)")]
    private static partial Regex Link();

    /// <summary>The words of some Markdown as the page shows them, one line each: the heading marks, bullets, bold stars,
    /// links' addresses and table bars gone. What a quote is looked for in when there's no page to look in.</summary>
    public static string Plain(string markdown)
    {
        string text = Link().Replace(markdown, "$1");
        text = LineLead().Replace(text, "");
        return text.Replace('|', ' ');
    }

    /// <summary>Whether <paramref name="quote"/> is somewhere in the notes (<paramref name="markdown"/>).</summary>
    public static bool InNotes(string markdown, string quote) => markdown.Length > 0 && Find(Plain(markdown), quote) is not null;

    /// <summary>Where <paramref name="quote"/> is in <paramref name="text"/>, or null: the same words first (ignoring spacing,
    /// case, curly quotes, dashes, Markdown's stars and a bullet), else the near match.</summary>
    public static Match? Find(string text, string quote)
    {
        var (haystack, at) = Normal(text, text.Length);
        var (needle, _) = Normal(LineLead().Replace(Link().Replace(quote, "$1"), ""), quote.Length);
        if (needle.Length == 0 || haystack.Length == 0) return null;
        int i = haystack.IndexOf(needle, StringComparison.Ordinal);
        if (i >= 0) return new Match(at[i], at[i + needle.Length - 1] + 1 - at[i], 1);
        return NearMatch(text, haystack, at, needle);
    }

    /// <summary>The run of the text's words that shares most of the quote's words, trimmed to the first and last of them.</summary>
    static Match? NearMatch(string text, string haystack, int[] at, string needle)
    {
        var want = Words(needle);
        if (want.Count < 3) return null;
        var have = Words(haystack);
        var wanted = new Dictionary<string, int>();
        foreach (var (w, _, _) in want) wanted[w] = wanted.GetValueOrDefault(w) + 1;
        double best = 0;
        (int From, int To)? span = null;
        for (int from = 0; from < have.Count; from++)
        {
            if (!wanted.ContainsKey(have[from].Word)) continue;
            var left = new Dictionary<string, int>(wanted);
            int hits = 0, last = from;
            for (int to = from; to < have.Count && to < from + want.Count + 3; to++)
            {
                if (left.TryGetValue(have[to].Word, out int n) && n > 0)
                {
                    left[have[to].Word] = n - 1;
                    hits++;
                    last = to;
                }
                double score = hits / (double)Math.Max(want.Count, last - from + 1);
                if (hits >= 3 && score > best)
                {
                    best = score;
                    span = (from, last);
                }
            }
        }
        if (span is not { } s || best < Near) return null;
        int start = at[have[s.From].Start], end = at[have[s.To].End - 1] + 1;
        return new Match(start, end - start, best);
    }

    /// <summary>The words (runs of letters and digits) of a normal form, each with where it starts and ends in it.</summary>
    static List<(string Word, int Start, int End)> Words(string normal)
    {
        var words = new List<(string, int, int)>();
        for (int i = 0; i < normal.Length;)
        {
            if (!char.IsLetterOrDigit(normal[i]))
            {
                i++;
                continue;
            }
            int j = i;
            while (j < normal.Length && char.IsLetterOrDigit(normal[j])) j++;
            words.Add((normal[i..j], i, j));
            i = j;
        }
        return words;
    }

    /// <summary>A text as it's compared, with where each of its characters came from in the text: lower case, every run of
    /// white space one space (none at the ends), curly quotes and long dashes plain, Markdown's emphasis marks gone.</summary>
    static (string Normal, int[] From) Normal(string text, int capacity)
    {
        var sb = new StringBuilder(capacity);
        var from = new List<int>(capacity);
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c is '*' or '`' or '~') continue;
            // _emphasis_ is dropped, a_b (a snake_case word) is kept.
            if (c == '_' && (i == 0 || !char.IsLetterOrDigit(text[i - 1]) || i + 1 >= text.Length || !char.IsLetterOrDigit(text[i + 1]))) continue;
            if (char.IsWhiteSpace(c) || c == ' ')
            {
                if (sb.Length > 0 && sb[^1] != ' ')
                {
                    sb.Append(' ');
                    from.Add(i);
                }
                continue;
            }
            c = c switch
            {
                '“' or '”' or '„' => '"', '‘' or '’' or '′' => '\'', '–' or '—' or '−' => '-', '…' => '.', _ => char.ToLowerInvariant(c),
            };
            sb.Append(c);
            from.Add(i);
        }
        while (sb.Length > 0 && sb[^1] == ' ')
        {
            sb.Length--;
            from.RemoveAt(from.Count - 1);
        }
        return (sb.ToString(), [.. from]);
    }
}
