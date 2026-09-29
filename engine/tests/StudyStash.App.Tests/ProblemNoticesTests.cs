using StudyStash.App.Services;

namespace StudyStash.App.Tests;

/// <summary>Which problems become notifications, and when (<see cref="ProblemNotices"/>).</summary>
public class ProblemNoticesTests
{
    static readonly DateTime T0 = new(2026, 9, 28, 10, 0, 0, DateTimeKind.Utc);
    static DateTime At(double seconds) => T0.AddSeconds(seconds);

    static AppProblem P(ProblemKind kind) => new(kind, kind.ToString(), "What to do.", "Fix");

    [Fact]
    public void A_problem_that_needs_you_is_said_once_it_has_lasted_a_moment()
    {
        var n = new ProblemNotices();
        n.Seen([P(ProblemKind.WrongPassword)], T0);
        Assert.Empty(n.Due(At(4.9), quiet: false));
        Assert.Equal(ProblemKind.WrongPassword, Assert.Single(n.Due(At(5), quiet: false)).Kind);
        // Once.
        n.Seen([P(ProblemKind.WrongPassword)], At(6));
        Assert.Empty(n.Due(At(60), quiet: false));
        Assert.True(n.Showing(ProblemKind.WrongPassword));
    }

    [Fact]
    public void One_that_flickers_is_never_said()
    {
        var n = new ProblemNotices();
        n.Seen([P(ProblemKind.LibraryStopped)], T0);
        n.Seen([], At(2));
        Assert.Empty(n.Due(At(10), quiet: false));
        Assert.False(n.Showing(ProblemKind.LibraryStopped));
    }

    [Fact]
    public void Coming_back_within_ten_minutes_it_isnt_said_again_but_later_it_is()
    {
        var n = new ProblemNotices();
        n.Seen([P(ProblemKind.DownloadFailed)], T0);
        Assert.Single(n.Due(At(5), quiet: false));
        n.Seen([], At(10));
        n.Seen([P(ProblemKind.DownloadFailed)], At(20));
        Assert.Empty(n.Due(At(30), quiet: false));
        n.Seen([], At(40));
        n.Seen([P(ProblemKind.DownloadFailed)], At(700));
        Assert.Single(n.Due(At(705), quiet: false));
    }

    [Fact]
    public void Problems_that_dont_stop_lectures_or_fix_themselves_arent_said()
    {
        var n = new ProblemNotices();
        n.Seen([P(ProblemKind.Unreachable), P(ProblemKind.NoModel), P(ProblemKind.Downloading), P(ProblemKind.NotSetUp)], T0);
        Assert.Empty(n.Due(At(60), quiet: false));
    }

    [Fact]
    public void While_setup_shows_them_itself_they_go_unsaid()
    {
        var n = new ProblemNotices();
        n.Seen([P(ProblemKind.WhisperFailed)], T0);
        Assert.Empty(n.Due(At(6), quiet: true));
        Assert.Empty(n.Due(At(60), quiet: false));
    }

    [Fact]
    public void Two_at_once_are_both_said_with_their_own_words()
    {
        var n = new ProblemNotices();
        n.Seen([P(ProblemKind.WhisperFailed), P(ProblemKind.WrongPassword)], T0);
        // The words may change while it settles: the latest are said.
        n.Seen([new AppProblem(ProblemKind.WhisperFailed, "Whisper couldn't start", "the model file is damaged", "Download again"), P(ProblemKind.WrongPassword)], At(1));
        var due = n.Due(At(5), quiet: false);
        Assert.Equal(2, due.Count);
        Assert.Contains(due, p => p.Detail == "the model file is damaged");
    }
}
