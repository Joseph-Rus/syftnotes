using StudyStash.Core.Ai;

namespace StudyStash.Core.Tests;

/// <summary>Recipes: questions kept to ask again as /study-guide, Syft Notes' own and a student's, kept on the library,
/// expanded before the AI sees them (the chat keeps what was typed), and offered as a half-typed /stu narrows them.</summary>
public class RecipesTests
{
    [Fact]
    public void A_question_that_starts_with_a_recipe_is_asked_as_its_prompt()
    {
        var all = Recipes.BuiltIns;
        string guide = all.Single(r => r.Id == "study-guide").Prompt;

        Assert.Equal(guide, Recipes.Expand("/study-guide", all));
        Assert.Equal(guide, Recipes.Expand("  /Study-Guide  ", all));
        Assert.Equal($"{guide}\n\nAlso, from the student: only the part on veins", Recipes.Expand("/study-guide only the part on veins", all));
    }

    [Theory]
    [InlineData("What are arteries?")]
    [InlineData("/nothing-called-this")]
    [InlineData("and/or what")]
    [InlineData("/")]
    public void Anything_else_is_asked_as_it_was_typed(string question) => Assert.Equal(question, Recipes.Expand(question, Recipes.BuiltIns));

    [Fact]
    public void A_half_typed_slash_narrows_the_list_names_that_start_with_it_first()
    {
        var all = Recipes.BuiltIns;
        Assert.Equal(all.Count, Recipes.Matching("/", all).Count);
        Assert.Equal("study-guide", Recipes.Matching("/stu", all)[0].Id);
        // "ex" starts "exam" and is inside "explain-simply" and "practice"... by id or name
        var ex = Recipes.Matching("/ex", all).Select(r => r.Id).ToList();
        Assert.Equal("exam", ex[0]);
        Assert.Contains("explain-simply", ex);
        Assert.Empty(Recipes.Matching("/study-guide now", all));
        Assert.Empty(Recipes.Matching("study", all));
    }

    [Fact]
    public void A_students_own_recipe_is_kept_named_and_can_replace_a_built_in()
    {
        using var dir = new TempDir();
        string home = dir["home"];

        var mine = Recipes.Save(home, " Lab report check! ", "Check my lab report against this lecture.")!;
        Assert.Equal(("lab-report-check", "Lab report check!"), (mine.Id, mine.Name));
        Assert.False(mine.BuiltIn);
        Assert.Equal("lab-report-check", Recipes.All(home)[0].Id);
        Assert.Equal(Recipes.BuiltIns.Count + 1, Recipes.All(home).Count);

        // One of the student's own called "Study guide" is used in place of Syft Notes' own.
        Recipes.Save(home, "Study guide", "Just the formulas, please.");
        var all = Recipes.All(home);
        Assert.Equal(Recipes.BuiltIns.Count + 1, all.Count);
        Assert.Equal("Just the formulas, please.", Recipes.Expand("/study-guide", all));

        Assert.True(Recipes.Delete(home, "study-guide"));
        Assert.False(Recipes.Delete(home, "study-guide"));
        Assert.False(Recipes.Delete(home, "exam")); // a built-in stays
        Assert.Equal(Recipes.BuiltIns.Single(r => r.Id == "study-guide").Prompt, Recipes.Expand("/study-guide", Recipes.All(home)));
    }

    [Theory]
    [InlineData("", "Ask this")]
    [InlineData("!!!", "Ask this")]
    [InlineData("Name", "   ")]
    public void A_recipe_needs_a_name_and_a_prompt(string name, string prompt)
    {
        using var dir = new TempDir();
        Assert.Null(Recipes.Save(dir["home"], name, prompt));
        Assert.Equal(Recipes.BuiltIns.Count, Recipes.All(dir["home"]).Count);
    }

    [Fact]
    public void A_damaged_recipes_file_leaves_the_built_ins()
    {
        using var dir = new TempDir();
        Directory.CreateDirectory(dir["home"]);
        File.WriteAllText(Recipes.PathIn(dir["home"]), "{ not json");
        Assert.Equal(Recipes.BuiltIns.Count, Recipes.All(dir["home"]).Count);
    }

    [Fact]
    public async Task The_library_asks_the_AI_the_whole_recipe_and_keeps_what_was_typed()
    {
        await using var lib = await StudyLibrary.StartAsync();

        var reply = await lib.Remote.AskAsync(new AskRequest("/key-terms") { Engine = "claude", Lecture = "v1", Chat = "new", Place = new AskPlace("lecture", "v1", "BIO 110") },
            _ => { }, CancellationToken.None);

        Assert.Contains("List the key terms and names from this", lib.Claude.Prompts[^1]);
        Assert.DoesNotContain("/key-terms", lib.Claude.Prompts[^1]);
        var chat = (await lib.Remote.ChatAsync(reply!.Chat!))!;
        Assert.Equal("/key-terms", chat.Messages![0].Text);
    }

    [Fact]
    public async Task Recipes_are_listed_kept_and_deleted_over_the_library()
    {
        await using var lib = await StudyLibrary.StartAsync();

        Assert.Equal(Recipes.BuiltIns.Select(r => r.Id), (await lib.Remote.RecipesAsync()).Select(r => r.Id));
        Assert.True((await lib.Remote.RecipesAsync()).All(r => r.BuiltIn && r.About.Length > 0));

        var saved = await lib.Remote.SaveRecipeAsync("Mnemonics", "Make up a mnemonic for each list in this.");
        Assert.Equal("mnemonics", saved!.Id);
        Assert.Equal("mnemonics", (await lib.Remote.RecipesAsync())[0].Id);

        await lib.AskAsync("/mnemonics", lecture: "v1");
        Assert.Contains("Make up a mnemonic for each list in this.", lib.Claude.Prompts[^1]);

        var left = await lib.Remote.DeleteRecipeAsync("mnemonics");
        Assert.DoesNotContain(left!, r => r.Id == "mnemonics");
        Assert.Null(await lib.Remote.DeleteRecipeAsync("mnemonics"));
        await Assert.ThrowsAsync<LibraryRefusedException>(() => lib.Remote.SaveRecipeAsync("", "x"));
    }
}
