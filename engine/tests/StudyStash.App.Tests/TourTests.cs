using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using StudyStash.App.Controls;
using StudyStash.App.ViewModels;
using StudyStash.App.ViewModels.Kit;
using StudyStash.App.Views;

namespace StudyStash.App.Tests;

/// <summary>The look round Home's buttons on the real library window: a step whose button isn't on screen is left out
/// (and not counted), the keyboard walks it, and it ends once.</summary>
public class TourTests
{
    [AvaloniaFact]
    public async Task The_tour_leaves_out_what_isnt_on_screen_and_ends_once()
    {
        var library = Demo.Overview();
        var window = new Window { Width = 1280, Height = 800, Content = new MacLibrary { DataContext = library } };
        window.Show();
        var tour = new TourModel([.. HomeTour.Steps, new TourStep("NothingByThisName", "Not here", "", "")]);
        int ended = 0;
        tour.Ended += () => ended++;
        library.Tour = tour;
        var layer = window.GetVisualDescendants().OfType<TourLayer>().Single();
        var card = layer.GetVisualDescendants().OfType<TourCard>().Single();
        await Wait.UntilAsync(() => tour.Current is not null && layer.IsVisible && card.FindControl<Button>("Next")!.IsFocused, "the tour to start with Next focused");

        var seen = new List<string>();
        string first = tour.Counter;
        while (tour.Current is { } step)
        {
            seen.Add(step.Title);
            window.KeyPressQwerty(PhysicalKey.ArrowRight, RawInputModifiers.None);
        }

        Assert.Equal("Record a lecture", seen[0]);
        Assert.DoesNotContain("Not here", seen);
        Assert.Equal($"Step 1 of {seen.Count}", first);
        Assert.Equal(1, ended);
        Assert.False(layer.IsVisible);
        tour.SkipCommand.Execute(null); // over already: nothing more happens
        Assert.Equal(1, ended);
        window.Close();
    }
}
