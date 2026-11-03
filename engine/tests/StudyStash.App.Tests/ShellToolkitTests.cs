using StudyStash.App.Services;

namespace StudyStash.App.Tests;

/// <summary>Work the app starts without waiting for it (a page being read, a list loading) never fails unseen: an
/// unexpected error is written down with what it was and the student is told; a cancelled one says nothing.</summary>
public class UiTasksTests
{
    [Fact]
    public async Task An_unexpected_failure_is_logged_and_said_and_a_cancelled_one_is_quiet()
    {
        var log = new List<string>();
        int said = 0;

        await UiTasks.Guard(async () =>
        {
            await Task.Yield();
            throw new InvalidOperationException("the page broke");
        }, "ShowHome", log.Add, () => said++);
        Assert.Equal(1, said);
        Assert.Contains(log, l => l.Contains("ShowHome", StringComparison.Ordinal) && l.Contains("the page broke", StringComparison.Ordinal));

        log.Clear();
        said = 0;
        await UiTasks.Guard(async () =>
        {
            await Task.Yield();
            throw new TaskCanceledException();
        }, "ShowHome", log.Add, () => said++);
        await UiTasks.Guard(() => throw new OperationCanceledException(), "ShowHome", log.Add, () => said++);
        Assert.Equal(0, said);
        Assert.Empty(log);
    }
}

/// <summary>Leaving a page for another leaves nothing of the old one open: whatever the window knew about the last page
/// (its note, its overview, its Canvas class, its assignment, and anything added later) is gone from the next, which
/// takes the next turn so a late answer for the old page can't draw over it.</summary>
public class PageStateTests
{
    [Fact]
    public void Leaving_a_page_and_opening_another_keeps_nothing_of_the_old_one()
    {
        var onNote = new PageState().Next() with
        {
            Lecture = "rec-1", Overview = "Biology", CanvasClass = "Biology", Assignment = ("Biology", "42"), Class = "Biology",
        };
        int loading = onNote.Turn;

        var home = onNote.Next() with { Overview = "" };
        var left = onNote.Left();

        Assert.NotEqual(loading, home.Turn); // the note's late answer no longer draws
        Assert.Equal(loading, left.Turn);
        foreach (var next in new[] { home, left })
            foreach (var p in typeof(PageState).GetProperties())
            {
                // The turn moves on, the folder in mind stays (until a page names another), Home is the overview asked for.
                if (p.Name is nameof(PageState.Turn) or nameof(PageState.Class)) continue;
                if (ReferenceEquals(next, home) && p.Name == nameof(PageState.Overview)) continue;
                Assert.True(p.GetValue(next) is null, $"{p.Name} was left over from the page before");
            }
        Assert.Equal("", home.Overview);
        Assert.Equal("Biology", home.Class);
    }
}
