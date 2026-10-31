using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StudyStash.Core.Ai;

namespace StudyStash.App.ViewModels;

/// <summary>One recipe in an Ask bar's Recipes list, or under a half-typed <c>/stu</c>: its name, the <c>/</c> word
/// that asks it, a line on what it does, and (one of the student's own) a delete.</summary>
public sealed class RecipeRow(Recipe recipe, AiAskModel ask)
{
    public Recipe Recipe { get; } = recipe;
    public string Name => Recipe.Name;
    public string Slash => "/" + Recipe.Id;
    public string About => Recipe.About;
    public bool CanDelete => !Recipe.BuiltIn;
    public string DeleteSaid => $"Delete the recipe {Recipe.Name}";
    /// <summary>From the Recipes list: asked at once. Under a half-typed slash: put in the bar, to add to or send.</summary>
    public IAsyncRelayCommand Run { get; } = new AsyncRelayCommand(() => ask.RunRecipe(recipe));
    public IRelayCommand Pick { get; } = new RelayCommand(() => ask.PickRecipe(recipe));
    public IAsyncRelayCommand Delete { get; } = new AsyncRelayCommand(() => ask.DeleteRecipe(recipe));
}

public sealed partial class AiAskModel
{
    /// <summary>Every recipe the library has, the student's own first: the Recipes list.</summary>
    public ObservableCollection<RecipeRow> Recipes { get; } = [];
    public bool HasRecipes => Recipes.Count > 0;

    /// <summary>The recipes a half-typed <c>/stu</c> could be, shown over the bar while it's typed.</summary>
    public ObservableCollection<RecipeRow> RecipeMatches { get; } = [];
    public bool ShowRecipeMatches => RecipeMatches.Count > 0;

    /// <summary>Saving what's typed as a recipe: its name, as the student types it in the Recipes list.</summary>
    [ObservableProperty] public partial string RecipeName { get; set; } = "";
    /// <summary>What a saved recipe would ask: the question in the bar, when it isn't itself a recipe.</summary>
    public string RecipePrompt => Question.Trim().StartsWith('/') ? "" : Question.Trim();
    public bool CanSaveRecipe => RecipePrompt.Length > 0;
    public bool CanSaveRecipeNow => CanSaveRecipe && RecipeName.Trim().Length > 0;
    public string SaveRecipeHint => CanSaveRecipe
        ? $"Keep “{Shorter(RecipePrompt)}” to ask again"
        : "Type a question in the bar, then keep it here to ask again with /.";

    /// <summary>Puts the caret back in the bar after a recipe is picked from under it (the view wires this).</summary>
    public Action? Refocus { get; set; }

    partial void OnRecipeNameChanged(string value) => SaveRecipeCommand.NotifyCanExecuteChanged();

    /// <summary>Reads the library's recipes (with the engines, when the bar opens). A library too old has none.</summary>
    public async Task LoadRecipesAsync()
    {
        List<Recipe> recipes;
        try
        {
            recipes = await library.RecipesAsync();
        }
        catch
        {
            return;
        }
        ShowRecipes(recipes);
    }

    void ShowRecipes(IEnumerable<Recipe> recipes)
    {
        Recipes.Clear();
        foreach (var r in recipes) Recipes.Add(new RecipeRow(r, this));
        OnPropertyChanged(nameof(HasRecipes));
        Matched();
    }

    /// <summary>The question changed: the recipes it could be, and whether it can be kept as one.</summary>
    void RecipesFollowQuestion()
    {
        Matched();
        OnPropertyChanged(nameof(RecipePrompt));
        OnPropertyChanged(nameof(CanSaveRecipe));
        OnPropertyChanged(nameof(SaveRecipeHint));
        SaveRecipeCommand.NotifyCanExecuteChanged();
    }

    void Matched()
    {
        var mine = Core.Ai.Recipes.Matching(Question, Recipes.Select(r => r.Recipe));
        // A slash word typed in full is left alone: Enter sends it.
        if (mine.Count == 1 && "/" + mine[0].Id == Question.Trim()) mine = [];
        RecipeMatches.Clear();
        foreach (var r in mine) RecipeMatches.Add(Recipes.First(x => x.Recipe == r));
        OnPropertyChanged(nameof(ShowRecipeMatches));
        PickFirstRecipeCommand.NotifyCanExecuteChanged();
    }

    /// <summary>A recipe picked under a half-typed slash: its slash word goes in the bar, a space after it, to send as
    /// it is or with more said after it.</summary>
    public void PickRecipe(Recipe recipe)
    {
        Question = "/" + recipe.Id + " ";
        Refocus?.Invoke();
    }

    /// <summary>Tab in the bar while a slash word is typed: the first recipe it could be.</summary>
    [RelayCommand(CanExecute = nameof(ShowRecipeMatches))]
    void PickFirstRecipe()
    {
        if (RecipeMatches.Count > 0) PickRecipe(RecipeMatches[0].Recipe);
    }

    /// <summary>A recipe pressed in the Recipes list: asked now, about what the bar is on.</summary>
    public async Task RunRecipe(Recipe recipe)
    {
        CloseRecipes?.Invoke();
        if (Busy) return;
        Question = "/" + recipe.Id;
        await AskCommand.ExecuteAsync(null);
    }

    /// <summary>Closes the Recipes popup; the view wires this to the real flyout.</summary>
    public Action? CloseRecipes { get; set; }

    /// <summary>Keeps the question in the bar as a recipe of the student's own, named as they typed.</summary>
    [RelayCommand(CanExecute = nameof(CanSaveRecipeNow))]
    async Task SaveRecipe()
    {
        Recipe? saved;
        try
        {
            saved = await library.SaveRecipeAsync(RecipeName.Trim(), RecipePrompt);
        }
        catch
        {
            return;
        }
        if (saved is null) return;
        RecipeName = "";
        await LoadRecipesAsync();
    }

    public async Task DeleteRecipe(Recipe recipe)
    {
        List<Recipe>? left;
        try
        {
            left = await library.DeleteRecipeAsync(recipe.Id);
        }
        catch
        {
            return;
        }
        if (left is not null) ShowRecipes(left);
        else await LoadRecipesAsync();
    }

    static string Shorter(string text) => text.Length <= 48 ? text : text[..47].TrimEnd() + "…";
}
