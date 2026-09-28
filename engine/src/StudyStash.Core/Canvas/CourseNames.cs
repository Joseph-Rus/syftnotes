using System.Text.RegularExpressions;

namespace StudyStash.Core.Canvas;

/// <summary>
/// What a Canvas course is called in Study Stash: its name ("Software Engineering"), cleaned of the school's term
/// and section codes ("202710.TS.CSCI321.A - Software Engineering (Fall 2026)"), with its short code ("CSCI 321")
/// only as a label beside it. Classes made from courses take these names, and every list shows them.
/// </summary>
public static partial class CourseNames
{
    /// <summary>The longest class name the library takes.</summary>
    public const int MaxLength = 60;

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();

    // A school's SIS id at the front: a term ("202710") then dotted or dashed parts ("TS.CSCI321.A").
    [GeneratedRegex(@"^\d{4,6}[._-]\S*")]
    private static partial Regex SisToken();

    // A bare term number at the front: "202710 - ", "202710: ".
    [GeneratedRegex(@"^\d{4,6}\s*[.:·|–—-]\s*")]
    private static partial Regex TermNumber();

    [GeneratedRegex(@"^(?:Fall|Spring|Summer|Winter|Autumn)\s+\d{2,4}\s*[:·|–—-]\s*", RegexOptions.IgnoreCase)]
    private static partial Regex LeadingTerm();

    [GeneratedRegex(@"\s*(?:[(\[]\s*(?:(?:Fall|Spring|Summer|Winter|Autumn)\s*\d{2,4}|\d{4,6})\s*[)\]]|[:·|–—-]\s*(?:Fall|Spring|Summer|Winter|Autumn)\s+\d{4})$", RegexOptions.IgnoreCase)]
    private static partial Regex TrailingTerm();

    // "CSCI 321", "CS-101", "BIO110A" in a code: a subject in capitals and a course number.
    [GeneratedRegex(@"(?<![A-Za-z])([A-Z]{2,5})[ -]?(\d{2,4}[A-Z]?)(?![0-9A-Za-z])")]
    private static partial Regex SubjectNumber();

    // A code at the front of a name, with a separator after it: "CS 101: ", "CSCI 321.A - ".
    [GeneratedRegex(@"^[A-Z]{2,5}[ -]?\d{2,4}[A-Z]?(?:[.-][A-Za-z0-9]{1,3})?\s*[:·|–—-]\s*")]
    private static partial Regex CodePrefix();

    [GeneratedRegex(@"^[\s:·|–—.-]+|[\s:·|–—-]+$")]
    private static partial Regex EdgeSeparators();

    [GeneratedRegex("[A-Za-z]{3,}")]
    private static partial Regex AWord();

    static readonly HashSet<string> TermWords = new(StringComparer.OrdinalIgnoreCase) { "FA", "SP", "SU", "WI", "FALL", "SPR", "SUM", "WIN", "TERM", "SEM" };

    static string Collapse(string s) => Spaces().Replace(s.Trim(), " ");

    /// <summary>The short code for a course ("CSCI 321" from "202710.TS.CSCI321.A"), from its code or else its name;
    /// "" when neither has a clean subject and number in it.</summary>
    public static string ShortCode(string code, string name = "")
    {
        foreach (string s in new[] { code, name })
            foreach (Match m in SubjectNumber().Matches(s ?? ""))
                if (!TermWords.Contains(m.Groups[1].Value))
                    return $"{m.Groups[1].Value} {m.Groups[2].Value}";
        return "";
    }

    /// <summary>The course's name as a class name: trimmed, single-spaced, without the term or SIS code the school
    /// puts in front ("202710.TS.CSCI321.A - ") or behind (" (Fall 2026)"), and without a leading course code when a
    /// real name follows it ("CS 101: Intro to Programming" → "Intro to Programming"). A name that is nothing but a code
    /// becomes the short code ("CSCI 321"); an empty one, the course code.</summary>
    public static string Title(string name, string code = "")
    {
        string original = Collapse(name ?? "");
        string t = original;
        for (int pass = 0; pass < 3; pass++)
        {
            string before = t;
            var sis = SisToken().Match(t);
            if (sis.Success)
            {
                string rest = t[sis.Length..];
                // An SIS token is capitals and digits; "202710.Software …" is a term number glued to a real word.
                bool codeLike = !sis.Value.Any(char.IsLower);
                t = codeLike ? rest : TermNumber().Replace(Regex.Replace(t, @"^(\d{4,6})[._-]", "$1 - "), "");
            }
            t = TermNumber().Replace(t, "");
            t = LeadingTerm().Replace(t, "");
            t = TrailingTerm().Replace(t, "");
            t = EdgeSeparators().Replace(t, "").Trim();
            // A leading code goes only when a real name is left after it.
            var prefix = CodePrefix().Match(t);
            if (prefix.Success && AWord().IsMatch(t[prefix.Length..])) t = t[prefix.Length..];
            else if (code.Length > 0 && t.StartsWith(code.Trim(), StringComparison.OrdinalIgnoreCase) && code.Trim().Length > 0)
            {
                string rest = EdgeSeparators().Replace(t[code.Trim().Length..], "").Trim();
                if (rest.Length > 0 && AWord().IsMatch(rest) && rest != t) t = rest;
            }
            t = Collapse(t);
            if (t == before) break;
        }
        if (t.Length == 0 || !t.Any(char.IsLetter))
        {
            string shortCode = ShortCode(code, original);
            t = shortCode.Length > 0 ? shortCode : original.Length > 0 ? original : Collapse(code ?? "");
        }
        return Fit(t);
    }

    /// <summary>Cut to the library's longest class name, at a word where it can.</summary>
    static string Fit(string t)
    {
        if (t.Length <= MaxLength) return t;
        string cut = t[..MaxLength];
        int space = cut.LastIndexOf(' ');
        return (space > MaxLength / 2 ? cut[..space] : cut).TrimEnd(' ', '-', ':', '·', '|', ',');
    }

    /// <summary>
    /// The class name for each course (id → name), every one different (ignoring case) from the others and from
    /// <paramref name="taken"/> (and never "Unsorted"): two courses both called "Seminar" become "Seminar (ENGR 401)"
    /// and "Seminar (ENGR 402)", or "Seminar" and "Seminar 2" when their codes don't tell them apart.
    /// </summary>
    public static Dictionary<string, string> Unique(IEnumerable<(string Id, string Name, string Code)> courses, IEnumerable<string>? taken = null)
    {
        var list = courses.Select(c => (c.Id, Title: Title(c.Name, c.Code), Short: ShortCode(c.Code, c.Name))).ToList();
        var used = new HashSet<string>(taken ?? [], StringComparer.OrdinalIgnoreCase) { Configs.Unsorted };
        var result = new Dictionary<string, string>();
        foreach (var group in list.GroupBy(c => c.Title, StringComparer.OrdinalIgnoreCase).OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
        {
            var members = group.OrderBy(c => c.Id, StringComparer.Ordinal).ToList();
            bool clash = members.Count > 1 || used.Contains(group.Key);
            bool codesTell = members.Select(c => c.Short).Where(s => s.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).Count() == members.Count;
            foreach (var c in members)
            {
                string name = !clash ? c.Title : codesTell ? Fit($"{c.Title} ({c.Short})") : c.Title;
                for (int n = 2; used.Contains(name); n++) name = Fit(c.Title) + $" {n}";
                used.Add(name);
                result[c.Id] = name;
            }
        }
        return result;
    }

    /// <summary>Every known course's class name (id → name) from what Find my courses kept: <see cref="CanvasSettings.Available"/>
    /// names with <see cref="CanvasSettings.CourseInfo"/> codes.</summary>
    public static Dictionary<string, string> Of(CanvasSettings s) =>
        Unique(s.Available.Select(kv => (kv.Key, s.CourseInfo.TryGetValue(kv.Key, out var i) && i.Name.Length > 0 ? i.Name : kv.Value,
            s.CourseInfo.TryGetValue(kv.Key, out var j) ? j.Code : "")));
}
