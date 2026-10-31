using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StudyStash.App.ViewModels;
using StudyStash.App.Views;
using StudyStash.Core.Ai;
using StudyStash.Core.Tests;

namespace StudyStash.App.Tests;

/// <summary>Recipes in the Ask bar, against a library run here (its AI a stand-in): a slash word shows the recipes it
/// could be and Tab takes the first; the Recipes list asks one at once; the question in the bar can be kept as a
/// recipe of the student's own, and deleted again.</summary>
public class AskRecipesTests
{
    static async Task<AiAskModel> BarAsync(StudyLibrary lib)
    {
        var ask = new AiAskModel(lib.Remote) { LectureId = "v1", ClassName = "BIO 110" };
        await ask.Load();
        ask.Engine = "claude";
        return ask;
    }

    [AvaloniaFact]
    public async Task A_slash_word_offers_the_recipes_it_could_be_and_Tab_takes_the_first()
    {
        await using var lib = await StudyLibrary.StartAsync();
        var ask = await BarAsync(lib);

        Assert.Equal(Recipes.BuiltIns.Count, ask.Recipes.Count);
        Assert.False(ask.ShowRecipeMatches);
        ask.Question = "/";
        Assert.Equal(Recipes.BuiltIns.Count, ask.RecipeMatches.Count);
        ask.Question = "/stu";
        Assert.True(ask.ShowRecipeMatches);
        Assert.Equal("/study-guide", ask.RecipeMatches[0].Slash);

        Assert.True(ask.PickFirstRecipeCommand.CanExecute(null));
        ask.PickFirstRecipeCommand.Execute(null);
        Assert.Equal("/study-guide ", ask.Question);
        Assert.False(ask.ShowRecipeMatches); // a space after it: done choosing
        Assert.False(ask.PickFirstRecipeCommand.CanExecute(null)); // so Tab moves on as it always does

        ask.Question = "/study-guide just veins";
        await ask.AskCommand.ExecuteAsync(null);
        Assert.Contains("Write a study guide for this.", lib.Claude.Prompts[^1]);
        Assert.Contains("Also, from the student: just veins", lib.Claude.Prompts[^1]);
        Assert.Equal("/study-guide just veins", ask.Turns[^1].Question);
        Assert.Equal("Arteries, capillaries and veins.", ask.Turns[^1].Answer);
    }

    [AvaloniaFact]
    public async Task A_recipe_pressed_in_the_list_is_asked_at_once()
    {
        await using var lib = await StudyLibrary.StartAsync();
        var ask = await BarAsync(lib);

        await ask.Recipes.First(r => r.Recipe.Id == "tldr").Run.ExecuteAsync(null);

        Assert.Equal("/tldr", ask.Turns.Single().Question);
        Assert.Contains("Sum this up in five bullets", lib.Claude.Prompts[^1]);
        Assert.Equal("", ask.Question);
    }

    [AvaloniaFact]
    public async Task The_question_in_the_bar_can_be_kept_as_a_recipe_and_deleted()
    {
        await using var lib = await StudyLibrary.StartAsync();
        var ask = await BarAsync(lib);

        Assert.False(ask.CanSaveRecipe);
        ask.Question = "Which of this is on the lab practical?";
        Assert.True(ask.CanSaveRecipe);
        Assert.False(ask.SaveRecipeCommand.CanExecute(null)); // no name yet
        ask.RecipeName = "Lab practical";
        await ask.SaveRecipeCommand.ExecuteAsync(null);

        var mine = ask.Recipes[0];
        Assert.Equal(("/lab-practical", true), (mine.Slash, mine.CanDelete));
        Assert.Equal("", ask.RecipeName);
        Assert.False(ask.Recipes.First(r => r.Recipe.Id == "exam").CanDelete);

        await mine.Delete.ExecuteAsync(null);
        Assert.DoesNotContain(ask.Recipes, r => r.Recipe.Id == "lab-practical");
        Assert.Equal(Recipes.BuiltIns.Count, ask.Recipes.Count);
    }

    [AvaloniaFact]
    public async Task A_library_too_old_for_recipes_shows_no_Recipes_button()
    {
        var ask = new AiAskModel(new FakeAiLibrary { Overview = AiDemo.Overview() });
        await ask.Load();
        Assert.False(ask.HasRecipes);
        ask.Question = "/stu";
        Assert.False(ask.ShowRecipeMatches);
    }

    [AvaloniaTheory]
    [InlineData(SkinKind.Mac)]
    [InlineData(SkinKind.Win)]
    public async Task The_bar_shows_the_matches_over_it_and_the_Recipes_list_asks_the_one_clicked(SkinKind skin)
    {
        await using var lib = await StudyLibrary.StartAsync();
        var ask = await BarAsync(lib);
        Control bar = skin == SkinKind.Mac ? new MacAiAskBar { DataContext = ask } : new WinAiAskBar { DataContext = ask };
        var w = MenuPickTests.Host(bar, skin);
        try
        {
            var popup = bar.GetVisualDescendants().OfType<Popup>().Single(p => p.Name == "RecipeMatchesPopup");
            Assert.False(popup.IsOpen);
            ask.Question = "/ex";
            Dispatcher.UIThread.RunJobs();
            Assert.True(popup.IsOpen);
            Assert.Contains(popup.Child!.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "/exam");
            ask.Question = "";
            Dispatcher.UIThread.RunJobs();
            Assert.False(popup.IsOpen);

            var button = bar.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "RecipesButton");
            Assert.True(button.IsEffectivelyVisible);
            var menu = MenuPickTests.OpenFlyout(button);
            var row = menu.GetVisualDescendants().OfType<TextBlock>().First(t => t.Text == "Key terms")
                .GetVisualAncestors().OfType<Button>().First();
            MenuPickTests.Click(row);
            for (int i = 0; i < 200 && ask.Busy | ask.Turns.Count == 0; i++)
            {
                await Task.Delay(10, TestContext.Current.CancellationToken);
                Dispatcher.UIThread.RunJobs();
            }
            Assert.Equal("/key-terms", ask.Turns.Single().Question);
            Assert.False(button.Flyout!.IsOpen);
        }
        finally
        {
            w.Close();
        }
    }
}
