using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using StudyStash.Core.Canvas;

namespace StudyStash.Core.Calendar;

/// <summary>A class as matching sees it: its name, its other names, and its Canvas course's short code ("CSCI 321";
/// "" when it has none).</summary>
public sealed record ClassHint(string Name, IReadOnlyList<string> Aliases, string Code = "");

/// <summary>
/// Which class a calendar event is for, from its title: "CS 101 Lecture" is CS 101, "Lab 3 [CS101-02 Fall 2026]" too,
/// "COMP 101" is the class whose Canvas course is COMP 101, and "BIO 110 study group" is Biology 110 (a shared course
/// number and a shared word, as <see cref="CourseMatch"/> suggests courses). Only a clear match counts: a title two
/// classes fit equally well, or only loosely, is no class, and the student picks.
/// </summary>
public static partial class EventClass
{
    [GeneratedRegex(@"([a-z]) (\d)")]
    private static partial Regex LetterSpaceDigit();

    /// <summary>Lower case, words only, and a code's letters joined to its number ("cs 101" → "cs101"), so "CS-101",
    /// "cs101" and "CS 101" read the same.</summary>
    static string Squash(string s) => LetterSpaceDigit().Replace(Classify.Norm(s), "$1$2");

    /// <summary>The class this event title is for, or null when none clearly is.</summary>
    public static string? For(string title, IReadOnlyList<ClassHint> classes)
    {
        if (string.IsNullOrWhiteSpace(title) || classes.Count == 0) return null;
        string t = Squash(title);
        // A class's name or other name, as whole words in the title: the longest one wins ("CS 101 Lab" over "CS 101").
        var named = classes
            .SelectMany(c => c.Aliases.Prepend(c.Name).Select(n => (Class: c.Name, Name: Squash(n))))
            .Where(x => x.Name.Length >= 3 && Regex.IsMatch(t, $@"\b{Regex.Escape(x.Name)}\b"))
            .OrderByDescending(x => x.Name.Length).ToList();
        if (named.Count > 0 && (named.Count == 1 || named[0].Name.Length > named[1].Name.Length || named.All(x => x.Class == named[0].Class)))
            return named[0].Class;
        // The course code in the title, against each class's code.
        string code = Squash(CourseNames.ShortCode(title.ToUpperInvariant()));
        if (code.Length > 0)
        {
            var coded = classes.Where(c => new[] { c.Code, CourseNames.ShortCode(c.Name.ToUpperInvariant()) }.Concat(c.Aliases.Select(a => CourseNames.ShortCode(a.ToUpperInvariant())))
                .Any(x => x.Length > 0 && Squash(x) == code)).Select(c => c.Name).Distinct().ToList();
            if (coded.Count == 1) return coded[0];
        }
        // A shared number and a shared word, clearly better than any other class.
        var scored = classes.Select(c => (c.Name, Score: c.Aliases.Prepend(c.Name).Max(n => CourseMatch.Score(n, c.Code, title))))
            .OrderByDescending(x => x.Score).ToList();
        return scored[0].Score >= 3 && (scored.Count == 1 || scored[1].Score < scored[0].Score) ? scored[0].Name : null;
    }

    /// <summary>The library's classes, with their Canvas course codes (on the library, from config.toml and canvas.json).</summary>
    public static List<ClassHint> Of(Config cfg)
    {
        var canvas = CanvasSettings.Load(cfg.Home);
        return [.. cfg.Classes.Select(c => new ClassHint(c.Name, c.Aliases,
            canvas.Courses.TryGetValue(c.Name, out long id) && canvas.CourseInfo.TryGetValue(id.ToString(CultureInfo.InvariantCulture), out var info)
                ? CourseNames.ShortCode(info.Code, info.Name) : ""))];
    }

    /// <summary>The classes in a library's /api/v2/library answer (on a laptop): name, aliases and code.</summary>
    public static List<ClassHint> From(JsonObject? overview) =>
        [.. (overview?["classes"] as JsonArray ?? []).OfType<JsonObject>()
            .Select(c => new ClassHint(Str(c["name"]), [.. (c["aliases"] as JsonArray ?? []).Select(Str).Where(a => a.Length > 0)], Str(c["code"])))
            .Where(c => c.Name.Length > 0)];

    static string Str(JsonNode? n) => n is JsonValue v && v.TryGetValue(out string? s) ? s : "";
}
