using Avalonia;
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
/// (and not counted), the keyboard walks it and it ends seen, once; and the button in the hole still works, ending the
/// tour there.</summary>
public class TourTests
{
    static async Task<(TourModel Tour, TourLayer Layer, List<bool> Ended)> StartAsync(Window window, LibraryModel library, IReadOnlyList<TourStep> steps)
    {
        var tour = new TourModel(steps);
        var ended = new List<bool>();
        tour.Ended += ended.Add;
        library.Tour = tour;
        var layer = window.GetVisualDescendants().OfType<TourLayer>().Single();
        var next = layer.GetVisualDescendants().OfType<TourCard>().Single().FindControl<Button>("Next")!;
        await Wait.UntilAsync(() => tour.Current is not null && layer.IsVisible && next.IsFocused, "the tour to start with Next focused");
        return (tour, layer, ended);
    }

    [AvaloniaFact]
    public async Task The_tour_leaves_out_what_isnt_on_screen_and_the_button_in_it_still_works()
    {
        var library = Demo.Overview();
        var window = new Window { Width = 1280, Height = 800, Content = new MacLibrary { DataContext = library } };
        window.Show();

        var (tour, layer, ended) = await StartAsync(window, library, [.. HomeTour.Steps, new TourStep("NothingByThisName", "Not here", "", "")]);
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
        Assert.Equal([true], ended); // seen through, once
        Assert.False(layer.IsVisible);

        // Again, Study then Settings: Settings, dimmed, does nothing; in the hole, it opens and the tour ends there.
        int settings = 0;
        library.OnSettings = () => settings++;
        (tour, layer, ended) = await StartAsync(window, library, [new TourStep("StudyRow", "Study", "", ""), new TourStep("Settings", "Settings", "", "")]);
        var button = window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "Settings");
        var middle = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!.Value;
        Click(window, middle);
        Assert.Equal(0, settings);
        Assert.Equal("Study", tour.Current?.Title);
        tour.NextCommand.Execute(null);
        Click(window, middle);
        await Wait.UntilAsync(() => ended.Count > 0, "the tour to end once the button was used");
        Assert.Equal(1, settings);
        Assert.Equal([true], ended);
        window.Close();
    }

    static void Click(Window window, Point at)
    {
        window.MouseDown(at, MouseButton.Left);
        window.MouseUp(at, MouseButton.Left);
    }
}
