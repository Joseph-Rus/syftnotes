using System.Text.Json;
using System.Text.RegularExpressions;

namespace StudyStash.Core.Ai;

/// <summary>A question kept to ask again: typed in Ask as <c>/study-guide</c> (its <see cref="Id"/>), it's asked as its
/// <see cref="Prompt"/>, about whatever the ask bar is on (the lecture, the folder, everything). The student sees and
/// keeps the short form; the AI gets the whole of it.</summary>
public sealed record Recipe(string Id, string Name, string Prompt)
{
    /// <summary>One of Syft Notes' own: it can't be deleted or changed (a student's own of the same name hides it).</summary>
    public bool BuiltIn { get; init; }
    /// <summary>A line under its name in the list.</summary>
    public string About { get; init; } = "";
}

/// <summary>
/// Recipes: the questions students ask of every lecture (a study guide, what's likely on the exam, the terms to
/// know), kept so they're one <c>/</c> away. Syft Notes comes with a few (<see cref="BuiltIns"/>); a student's own
/// are kept in the library's <c>recipes.json</c>, so every device that asks this library has them. The library
/// expands one in a question before the AI sees it (<see cref="Expand"/>), so the phone, the web app and the quick
/// panel get them without knowing about them.
/// </summary>
public static partial class Recipes
{
    public static readonly IReadOnlyList<Recipe> BuiltIns =
    [
        new("study-guide", "Study guide",
            "Write a study guide for this. Group it by topic, in the order it was taught. Under each topic: the key ideas in a " +
            "sentence or two each, the terms to know with short definitions, any formulas with what each symbol means, and a " +
            "worked example where the lecture gave one. End with a short list of the things most worth reviewing. Cite the " +
            "moments in the lecture you draw on.")
        { BuiltIn = true, About = "Topics, terms, formulas and what to review" },
        new("exam", "Likely on the exam",
            "What is most likely to be on the exam from this? Start with anything the lecturer said outright would be on an " +
            "exam, a quiz or a test, or said to make sure to know, quoting them and giving the moment. Then the ideas that " +
            "got the most time, were repeated, or were worked through as examples. For each, say in a sentence why it's " +
            "likely and what a question on it might ask.")
        { BuiltIn = true, About = "What was flagged, repeated or worked through" },
        new("explain-simply", "Explain it simply",
            "Explain this simply, as if to a classmate who missed the lecture and is new to the subject: the main idea first in " +
            "plain words, then each part built on it, with an everyday analogy where one helps. No jargon without saying what " +
            "it means.")
        { BuiltIn = true, About = "The main idea in plain words, with analogies" },
        new("key-terms", "Key terms",
            "List the key terms and names from this, each with a one-sentence definition in the lecturer's sense and the " +
            "moment it was explained. Keep the order they came up in.")
        { BuiltIn = true, About = "Every term, defined, with where it came up" },
        new("practice", "Practice questions",
            "Write 6 practice questions on this, mixing short answer, multiple choice and one longer problem worked like the " +
            "lecture's examples. Put the answers after all the questions, under a heading \"Answers\", each with a sentence " +
            "on why and the moment in the lecture it comes from.")
        { BuiltIn = true, About = "Six questions, with answers after them" },
        new("tldr", "In five bullets",
            "Sum this up in five bullets: the five things to remember if you remember nothing else.")
        { BuiltIn = true, About = "The five things to remember" },
        new("connect", "How it connects",
            "How does this connect to the rest of the course? Say what it builds on from earlier lectures, what it sets up, and " +
            "where the same ideas come back, naming the lectures and moments.")
        { BuiltIn = true, About = "What it builds on and what it sets up" },
        new("confusing", "What's tricky",
            "What in this is easiest to get wrong or confuse? List the common mistakes, look-alike ideas and subtle points, " +
            "each with the right way to think about it and the moment the lecturer addressed it, if they did.")
        { BuiltIn = true, About = "Common mistakes and look-alike ideas" },
    ];

    static readonly JsonSerializerOptions Options = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };
    static readonly object Gate = new();

    public static string PathIn(string home) => Path.Combine(home, "recipes.json");

    /// <summary>The student's own, in the order they made them.</summary>
    public static List<Recipe> Own(string home)
    {
        try
        {
            if (SharedFile.Read(PathIn(home)) is { } text && JsonSerializer.Deserialize<List<Recipe>>(text, Options) is { } kept)
                return kept.Where(r => IsId(r.Id) && r.Prompt.Trim().Length > 0).Select(r => r with { BuiltIn = false }).ToList();
        }
        catch (JsonException)
        {
        }
        return [];
    }

    /// <summary>Every recipe, the student's own first (newest first), then the built-ins they haven't replaced.</summary>
    public static List<Recipe> All(string home)
    {
        var own = Own(home);
        own.Reverse();
        return own.Concat(BuiltIns.Where(b => own.All(o => o.Id != b.Id))).ToList();
    }

    /// <summary>Keeps a recipe of the student's own, named <paramref name="name"/> ("Lab report check" is
    /// <c>/lab-report-check</c>); one of the same id is replaced. Null when there's no name or no prompt.</summary>
    public static Recipe? Save(string home, string name, string prompt)
    {
        name = Regex.Replace(name.Trim().TrimStart('/'), @"\s+", " ");
        prompt = prompt.Trim();
        string id = IdFor(name);
        if (id.Length == 0 || prompt.Length == 0) return null;
        if (name.Length > 60) name = name[..60].TrimEnd();
        var recipe = new Recipe(id, name, prompt) { About = About(prompt) };
        lock (Gate)
        {
            var own = Own(home);
            own.RemoveAll(r => r.Id == id);
            own.Add(recipe);
            Write(home, own);
        }
        return recipe;
    }

    /// <summary>Deletes one of the student's own; a built-in can't be. False when there was none.</summary>
    public static bool Delete(string home, string id)
    {
        lock (Gate)
        {
            var own = Own(home);
            if (own.RemoveAll(r => r.Id == id) == 0) return false;
            Write(home, own);
            return true;
        }
    }

    static void Write(string home, List<Recipe> own)
    {
        Directory.CreateDirectory(home);
        SharedFile.Write(PathIn(home), JsonSerializer.Serialize(own.Select(r => new Recipe(r.Id, r.Name, r.Prompt) { About = r.About }), Options) + "\n");
    }

    /// <summary>"Lab report check!" → "lab-report-check": lower case letters and digits, dashes between words.</summary>
    public static string IdFor(string name)
    {
        string id = Regex.Replace(name.Trim().TrimStart('/').ToLowerInvariant(), @"[^\p{L}\p{N}]+", "-").Trim('-');
        return id.Length > 40 ? id[..40].TrimEnd('-') : id;
    }

    static bool IsId(string? id) => !string.IsNullOrEmpty(id) && IdFor(id) == id;

    /// <summary>A line under a student's own recipe: the start of its prompt.</summary>
    static string About(string prompt)
    {
        string line = Regex.Replace(prompt, @"\s+", " ");
        return line.Length <= 70 ? line : line[..69].TrimEnd() + "…";
    }

    [GeneratedRegex(@"^\s*/([\p{L}\p{N}][\p{L}\p{N}-]*)(?:\s+(.*))?$", RegexOptions.Singleline)]
    private static partial Regex Slash();

    /// <summary>The recipe a question starts with, and what was typed after it; null when it doesn't start with one.</summary>
    public static (Recipe Recipe, string After)? Find(string question, IEnumerable<Recipe> recipes)
    {
        if (Slash().Match(question) is not { Success: true } m) return null;
        string id = m.Groups[1].Value.ToLowerInvariant();
        return recipes.FirstOrDefault(r => r.Id == id) is { } recipe ? (recipe, m.Groups[2].Value.Trim()) : null;
    }

    /// <summary>
    /// The question as the AI should get it: one that starts with a recipe (<c>/study-guide</c>, or <c>/study-guide
    /// only chapter 3</c>) becomes the recipe's prompt, with anything typed after it added as the student's own words.
    /// Anything else is unchanged, including a slash that names no recipe.
    /// </summary>
    public static string Expand(string question, IEnumerable<Recipe> recipes)
    {
        if (Find(question, recipes) is not var (recipe, rest)) return question;
        return rest.Length == 0 ? recipe.Prompt : $"{recipe.Prompt}\n\nAlso, from the student: {rest}";
    }

    /// <summary>The recipes a half-typed <c>/stu</c> could be, best first: names that start with it, then ones with it
    /// anywhere. Every recipe for a lone "/"; none once the question isn't a slash word any more (a space typed after
    /// it).</summary>
    public static List<Recipe> Matching(string typed, IEnumerable<Recipe> recipes)
    {
        string t = typed.TrimStart();
        if (!t.StartsWith('/') || t.Contains(' ') || t.Contains('\n')) return [];
        string q = t[1..].ToLowerInvariant();
        var all = recipes.ToList();
        if (q.Length == 0) return all;
        return all.Where(r => r.Id.StartsWith(q) || r.Name.StartsWith(q, StringComparison.OrdinalIgnoreCase))
            .Concat(all.Where(r => r.Id.Contains(q) || r.Name.Contains(q, StringComparison.OrdinalIgnoreCase)))
            .Distinct().ToList();
    }
}
