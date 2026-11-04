using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using StudyStash.App.ViewModels;
using StudyStash.App.Views;

namespace StudyStash.App.Tests;

/// <summary>Back and Forward bring a page back to where the student scrolled it to: asked of a page that has just opened,
/// it goes there once the page reaches that far, even when the page grows into it.</summary>
public class ScrollBackTests
{
    static readonly DateTimeOffset Day = new(2026, 9, 30, 9, 0, 0, TimeSpan.FromHours(-7));

    static OverviewModel.LectureFacts Note(int i) => new($"n{i}", $"Note {i}", "CS 101", Day.AddDays(-i), 3600, $"About {i}.", false, Day.AddDays(-i).ToString("o"));

    [AvaloniaFact]
    public async Task A_page_is_put_back_where_it_was_scrolled_to_even_as_it_grows_into_it()
    {
        var home = OverviewModel.Home(OverviewTests.Sources() with { Lectures = [.. Enumerable.Range(0, 20).Select(Note)] });
        var view = new Overview { DataContext = home };
        var window = new Window { Width = 900, Height = 500, Content = view };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var page = view.FindControl<ScrollViewer>("Page")!;
        double Reach() => page.Extent.Height - page.Viewport.Height;
        try
        {
            // What the student scrolls to is what the page says it's scrolled to (what the history keeps).
            page.Offset = new Vector(0, 150);
            await Wait.UntilAsync(() => { Dispatcher.UIThread.RunJobs(); return Math.Abs(home.Scroll.Y - 150) < 1; }, "the page to say where it is scrolled to");

            // Back to a page that opened at its top, where it was scrolled well down: it goes there.
            page.Offset = default;
            double far = Reach() - 40;
            Assert.True(far > 150, "the test page should be taller than the window");
            home.Scroll.PutBack(far);
            await Wait.UntilAsync(() => { Dispatcher.UIThread.RunJobs(); return Math.Abs(page.Offset.Y - far) < 1; }, "the page to be put back");

            // A place the page doesn't reach yet (more of its notes are still to arrive): as far as it goes, then on to it.
            double beyond = Reach() + 600;
            home.Scroll.PutBack(beyond);
            await Wait.UntilAsync(() => { Dispatcher.UIThread.RunJobs(); return Math.Abs(page.Offset.Y - Reach()) < 1; }, "the page to go as far as it reaches");
            Assert.True(page.Offset.Y < beyond);
            var sources = OverviewTests.Sources();
            var dot = OverviewModel.DotsOf(sources);
            home.Add(Enumerable.Range(20, 20).Select(i => OverviewModel.Row(Note(i), sources, dot, showFolder: true)), canLoadMore: false);
            await Wait.UntilAsync(() => { Dispatcher.UIThread.RunJobs(); return Math.Abs(page.Offset.Y - beyond) < 1; }, "the page to be put back as it grew");
        }
        finally
        {
            window.Close();
        }
    }
}
