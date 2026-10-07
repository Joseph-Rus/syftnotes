using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace StudyStash.App.ViewModels;

/// <summary>One of the browsers to pick, and its own button.</summary>
public sealed class BrowserOption
{
    public required string Name { get; init; }
    public required IAsyncRelayCommand Pick { get; init; }
}

/// <summary>
/// "Which browser do you use for Canvas?": asked when the Study Stash extension checks in from two browsers in one
/// place (Chrome and Edge on the same computer, say), and again whenever the student asks to change it in Settings. Until it's answered the one that was reading Canvas carries on
/// and the other is left alone; the answer makes it that browser from then on, in the library's own settings.
/// The model holds on to nothing that outlives it: what picking does and how the window closes are handed in, and it
/// listens to nobody.
/// </summary>
public sealed partial class BrowserChoiceModel : ObservableObject
{
    readonly Func<string, Task<string?>> choose;

    /// <param name="browsers">The browsers the extension is checking in from, as they name themselves.</param>
    /// <param name="choose">Tells the library the pick. Null when it took it; else why not, in words.</param>
    public BrowserChoiceModel(IReadOnlyList<string> browsers, Func<string, Task<string?>> choose)
    {
        this.choose = choose;
        Lede = $"The Study Stash extension is in {(browsers.Count == 2 ? "both " : "")}{Join(browsers)}. Study Stash reads Canvas through one of them, and opens Canvas there. You can change this later in Settings.";
        Options = [.. browsers.Select(name => new BrowserOption { Name = name, Pick = new AsyncRelayCommand(() => PickAsync(name)) })];
    }

    public static string Title => "Which browser do you use for Canvas?";
    public string Lede { get; }
    public IReadOnlyList<BrowserOption> Options { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasProblem))]
    public partial string? Problem { get; set; }
    public bool HasProblem => Problem is { Length: > 0 };

    /// <summary>The pick was taken (the browser's name), or the student chose Not now (null): the window closes.</summary>
    public Action<string?>? OnDone { get; set; }

    /// <summary>"Chrome and Edge", "Chrome, Edge and Firefox".</summary>
    public static string Join(IReadOnlyList<string> names) => names.Count switch
    {
        0 => "",
        1 => names[0],
        _ => string.Join(", ", names.Take(names.Count - 1)) + " and " + names[^1],
    };

    async Task PickAsync(string name)
    {
        Problem = null;
        string? refused;
        try
        {
            refused = await choose(name);
        }
        catch (Services.CanvasLibraryException e)
        {
            refused = e.Message.Length > 0 ? e.Message : "Your library didn't take that. Try again in a moment.";
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
        {
            refused = "Your library didn't answer. Try again in a moment.";
        }
        if (refused is null) OnDone?.Invoke(name);
        else Problem = refused;
    }

    [RelayCommand]
    void Later() => OnDone?.Invoke(null);
}
