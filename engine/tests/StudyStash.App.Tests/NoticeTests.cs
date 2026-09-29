using StudyStash.App.Services;

namespace StudyStash.App.Tests;

/// <summary>How long a notification stays and when it goes (<see cref="Notice"/>), and the stack's rules
/// (<see cref="NoticeStack"/>): never two alike, three at most, the oldest folding away.</summary>
public class NoticeTests
{
    static readonly DateTime T0 = new(2026, 9, 28, 10, 0, 0, DateTimeKind.Utc);
    static DateTime At(double seconds) => T0.AddSeconds(seconds);

    static Notice Plain(string title = "Recording saved", string text = "Study Stash is writing it down.") => new() { Title = title, Text = text };

    [Fact]
    public void Without_a_button_it_goes_after_six_seconds_and_with_one_after_ten()
    {
        var stack = new NoticeStack();
        var plain = Plain();
        var withButton = new Notice { Title = "Filed in CS 101", Text = "Recursion and the call stack", ActionLabel = "Open note" };
        stack.Add(plain, T0);
        stack.Add(withButton, T0);

        Assert.Empty(stack.Due(At(5.9)));
        Assert.Equal([plain], stack.Due(At(6)));
        Assert.DoesNotContain(withButton, stack.Due(At(9.9)));
        Assert.Contains(withButton, stack.Due(At(10)));
    }

    [Fact]
    public void Its_own_time_wins()
    {
        var n = new Notice { Title = "A lighter model would keep up better", ActionLabel = "Settings", Stay = NoticeTimes.Advice };
        var stack = new NoticeStack();
        stack.Add(n, T0);
        Assert.Empty(stack.Due(At(29)));
        Assert.Single(stack.Due(At(30)));
    }

    [Fact]
    public void Hovering_holds_it_and_leaving_gives_it_a_few_seconds_more()
    {
        var stack = new NoticeStack();
        var n = Plain();
        stack.Add(n, T0);
        n.Hover(true, At(5));
        // Long past its time, but the pointer is on it.
        Assert.Empty(stack.Due(At(60)));
        n.Hover(false, At(60));
        // It had one second left: it gets three once the pointer leaves.
        Assert.Empty(stack.Due(At(62.9)));
        Assert.Single(stack.Due(At(63)));
    }

    [Fact]
    public void Leaving_early_keeps_the_time_it_had()
    {
        var n = new Notice { Title = "Filed in CS 101", ActionLabel = "Open note" };
        var stack = new NoticeStack();
        stack.Add(n, T0);
        n.Hover(true, At(2));
        n.Hover(false, At(4));
        // 8 of its 10 seconds were left when the pointer came: they run from when it left.
        Assert.Empty(stack.Due(At(11.9)));
        Assert.Single(stack.Due(At(12)));
    }

    [Fact]
    public void Waiting_for_room_doesnt_use_up_its_time()
    {
        var stack = new NoticeStack();
        var n = Plain();
        stack.Add(n, T0);
        n.Wait(true, At(1));
        Assert.Empty(stack.Due(At(100)));
        n.Wait(false, At(100));
        Assert.Empty(stack.Due(At(104.9)));
        Assert.Single(stack.Due(At(105)));
    }

    [Fact]
    public void One_that_needs_you_stays_until_closed_or_no_longer_so()
    {
        bool paused = true;
        var n = new Notice { Title = "Recording paused", Text = "The microphone stopped.", ActionLabel = "Resume", UntilClosed = true, StillTrue = () => paused };
        var stack = new NoticeStack();
        stack.Add(n, T0);
        Assert.Empty(stack.Due(At(3600)));
        Assert.Equal(TimeSpan.MaxValue, n.Left(At(3600)));
        paused = false;
        Assert.Single(stack.Due(At(3601)));
    }

    [Fact]
    public void The_same_one_twice_shows_once_and_starts_its_time_again()
    {
        var stack = new NoticeStack();
        var first = Plain();
        Assert.True(stack.Add(first, T0).Added);
        var (added, folded) = stack.Add(Plain(), At(5));
        Assert.False(added);
        Assert.Empty(folded);
        Assert.Equal([first], stack.Showing);
        // Its six seconds start again from the second.
        Assert.Empty(stack.Due(At(10.9)));
        Assert.Single(stack.Due(At(11)));
    }

    [Fact]
    public void Two_lectures_filed_in_the_same_class_are_two_notifications()
    {
        var stack = new NoticeStack();
        stack.Add(new Notice { Title = "Filed in CS 101", Text = "Recursion and the call stack" }, T0);
        stack.Add(new Notice { Title = "Filed in CS 101", Text = "Big-O, by example" }, T0);
        Assert.Equal(2, stack.Showing.Count);
    }

    [Fact]
    public void A_canvas_notification_is_known_by_its_id()
    {
        var stack = new NoticeStack();
        stack.Add(new Notice { Title = "New score", Text = "CS 101 · Problem set 4 · 18/20", Key = "canvas:3" }, T0);
        Assert.False(stack.Add(new Notice { Title = "New score", Text = "CS 101 · Problem set 4 · 18/20", Key = "canvas:3" }, T0).Added);
        Assert.True(stack.Add(new Notice { Title = "New score", Text = "CS 101 · Problem set 4 · 18/20", Key = "canvas:4" }, T0).Added);
    }

    [Fact]
    public void Newest_first_and_three_at_most_the_oldest_folding_away()
    {
        var stack = new NoticeStack();
        var a = Plain("A");
        var b = Plain("B");
        var c = Plain("C");
        var d = Plain("D");
        stack.Add(a, T0);
        stack.Add(b, T0);
        stack.Add(c, T0);
        var (_, folded) = stack.Add(d, T0);
        Assert.Equal([a], folded);
        Assert.Equal([d, c, b], stack.Showing);
    }

    [Fact]
    public void One_that_needs_you_folds_away_last()
    {
        var stack = new NoticeStack();
        var paused = new Notice { Title = "Recording paused", ActionLabel = "Resume", UntilClosed = true };
        stack.Add(paused, T0);
        stack.Add(Plain("B"), T0);
        stack.Add(Plain("C"), T0);
        var (_, folded) = stack.Add(Plain("D"), T0);
        Assert.Equal("B", Assert.Single(folded).Title);
        Assert.Contains(paused, stack.Showing);

        // Only ones that need you left: the oldest of them goes, never the one just added.
        var all = new NoticeStack();
        var first = new Notice { Title = "1", UntilClosed = true };
        all.Add(first, T0);
        all.Add(new Notice { Title = "2", UntilClosed = true }, T0);
        all.Add(new Notice { Title = "3", UntilClosed = true }, T0);
        var newest = Plain("4");
        Assert.Equal([first], all.Add(newest, T0).Folded);
        Assert.Equal(newest, all.Showing[0]);
    }
}
