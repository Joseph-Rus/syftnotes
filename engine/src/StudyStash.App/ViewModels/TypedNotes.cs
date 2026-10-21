using System.Text;
using System.Text.RegularExpressions;
using StudyStash.Core;

namespace StudyStash.App.ViewModels;

/// <summary>
/// Whose words are whose in a lecture's notes. What the student typed is marked in the notes' Markdown
/// (<c>==like this==</c>, a highlighter's mark in any Markdown app); everything else was written from the recording.
/// On the page theirs is ink and heavier, the rest grey; "Only my notes" shows theirs alone; and a line they change or
/// add while editing becomes theirs.
/// </summary>
public static partial class TypedNotes
{
    public const string Mark = Summarize.Mark;

    [GeneratedRegex(@"==(?=\S)(.+?)(?<=\S)==")]
    private static partial Regex Marked();

    /// <summary>What a line starts with before its words: a heading's #s, a bullet (and its checkbox), a number, a
    /// quote's &gt;. The marks go round the words after it, so the line is still a heading, a bullet or a checkbox.</summary>
    [GeneratedRegex(@"^\s*(?:#{1,6}\s+|[-*+]\s+(?:\[[ xX]\](?:\s+|$))?|\d+[.)]\s+|>\s+)*")]
    private static partial Regex Lead();

    [GeneratedRegex(@"^(\s*)(?:([-*+])\s+(\[[ xX]\](?:\s+|$))?|(\d+)([.)])\s+)")]
    private static partial Regex ListLead();

    /// <summary>The start the next line of a list takes after this one ("- ", "  * ", "3. " after "2. ", an empty
    /// checkbox after a checkbox); null when the line isn't one of a list's.</summary>
    public static string? Bullet(string line)
    {
        var m = ListLead().Match(line);
        if (!m.Success) return null;
        return m.Groups[2].Success ? $"{m.Groups[1].Value}{m.Groups[2].Value} {(m.Groups[3].Success ? "[ ] " : "")}"
            : $"{m.Groups[1].Value}{long.Parse(m.Groups[4].Value, System.Globalization.CultureInfo.InvariantCulture) + 1}{m.Groups[5].Value} ";
    }

    /// <summary>Something in the notes is the student's own.</summary>
    public static bool HasMarks(string? markdown) => !string.IsNullOrEmpty(markdown) && Marked().IsMatch(markdown);

    /// <summary>The notes without the marks: every word as it reads.</summary>
    public static string Unmarked(string markdown) => Marked().Replace(markdown, "$1");

    /// <summary>
    /// Only what the student wrote: each of their lines with its heading or bullet, in order, and nothing that was
    /// added from the recording. A blank line stays between a heading's bullets and the next heading.
    /// </summary>
    public static string Only(string markdown)
    {
        var kept = new StringBuilder();
        bool fenced = false;
        foreach (string line in markdown.ReplaceLineEndings("\n").Split('\n'))
        {
            if (line.TrimStart().StartsWith("```", StringComparison.Ordinal)) fenced = !fenced;
            if (fenced) continue;
            var mine = Marked().Matches(line);
            if (mine.Count == 0) continue;
            string lead = Lead().Match(line).Value;
            if (lead.TrimStart().StartsWith('#') && kept.Length > 0) kept.Append('\n');
            kept.Append(lead).Append(string.Join(" ", mine.Select(m => m.Value))).Append('\n');
        }
        return kept.ToString().TrimEnd();
    }

    /// <summary>What a student typed, as notes that are all theirs: each line's words marked, its heading, bullet or
    /// checkbox kept, its formatting inside the marks. What a lecture shows when nothing was written from its
    /// recording. A block of code is left as it was typed, and a rule ("---") keeps a blank line above it, so the line
    /// before it doesn't read as a heading.</summary>
    public static string AllMine(string typed)
    {
        var lines = new List<string>();
        bool fenced = false;
        foreach (string line in typed.ReplaceLineEndings("\n").Split('\n'))
        {
            bool fence = line.TrimStart().StartsWith("```", StringComparison.Ordinal) || line.TrimStart().StartsWith("~~~", StringComparison.Ordinal);
            if (!fenced && !fence && Rule().IsMatch(line) && lines.Count > 0 && lines[^1].Trim().Length > 0) lines.Add("");
            lines.Add(fenced || fence ? line : Mine(line));
            if (fence) fenced = !fenced;
        }
        return string.Join("\n", lines);
    }

    [GeneratedRegex(@"^\s*(?:-{3,}|\*{3,}|_{3,})\s*$")]
    private static partial Regex Rule();

    /// <summary>Every word of the notes is the student's: they read exactly as what a student typed does
    /// (<see cref="AllMine"/>), with nothing added from the recording.</summary>
    public static bool IsAllMine(string? markdown) => HasMarks(markdown) && AllMine(Unmarked(markdown!)) == markdown!.ReplaceLineEndings("\n");

    /// <summary>A line with its words marked as the student's (its heading or bullet marker left outside the marks).
    /// A line with nothing to mark, or that isn't words (a table's row, a rule, a fence, a diagram's line) stays as it is.</summary>
    static string Mine(string line)
    {
        string lead = Lead().Match(line).Value, words = line[lead.Length..].Trim();
        if (words.Length == 0 || NotWords(line)) return line;
        return lead + Mark + Unmarked(words) + Mark;
    }

    static bool NotWords(string line)
    {
        string t = line.Trim();
        // (A table's row starts and ends with |; "|x| = 5" is words.)
        return (t.StartsWith('|') && t.EndsWith('|') && t.Length > 1) || t.StartsWith("```", StringComparison.Ordinal) || t.StartsWith("$$", StringComparison.Ordinal)
            || t.StartsWith("![", StringComparison.Ordinal) || Rule().IsMatch(t) || (t.StartsWith('[') && t.EndsWith(']'));
    }

    /// <summary>
    /// The notes after an edit, with whose words are whose brought up to date: a line left as it was keeps its marks
    /// (the editor shows the notes without them), and a line the student changed or added is theirs. Lines inside a
    /// code block or a formula block are left alone.
    /// </summary>
    public static string Remark(string edited, string marked)
    {
        var was = new Dictionary<string, string>();
        foreach (string line in marked.ReplaceLineEndings("\n").Split('\n')) was.TryAdd(Unmarked(line).TrimEnd(), line);
        var lines = edited.ReplaceLineEndings("\n").Split('\n');
        bool fenced = false, maths = false;
        for (int i = 0; i < lines.Length; i++)
        {
            string t = lines[i].Trim();
            bool fence = t.StartsWith("```", StringComparison.Ordinal), block = t == "$$";
            if (fence) fenced = !fenced;
            if (block) maths = !maths;
            if (was.TryGetValue(lines[i].TrimEnd(), out string? kept)) lines[i] = kept;
            else if (!fenced && !maths && !fence && !block) lines[i] = Mine(lines[i]);
        }
        return string.Join("\n", lines);
    }
}

/// <summary>
/// What a recording is called depends on where it's filed: a lecture in a class, a meeting in a club's or a work
/// folder, a recording when it's in no folder yet. Where kinds mix (Home's list, search, Ask on Home) they're notes.
/// </summary>
public static class Nouns
{
    public const string Lecture = "lecture", Meeting = "meeting", Recording = "recording";

    /// <summary>The noun for a note in <paramref name="folder"/> (a class's name; "" or Unsorted for none), which sits
    /// in the sidebar's <paramref name="group"/> ("" for Classes; "Clubs", "Work" or a group of the student's own).</summary>
    public static string For(string? folder, string group) =>
        string.IsNullOrEmpty(folder) || folder == Configs.Unsorted ? Recording : group.Length == 0 ? Lecture : Meeting;

    /// <summary>"Lectures", "Meetings": a folder's section of them.</summary>
    public static string Section(string noun) => Capital(noun) + "s";

    public static string Capital(string noun) => noun.Length == 0 ? noun : char.ToUpperInvariant(noun[0]) + noun[1..];
}
