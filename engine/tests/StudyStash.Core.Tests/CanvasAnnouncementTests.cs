using StudyStash.Core.Canvas;

namespace StudyStash.Core.Tests;

/// <summary>Announcements (their attachments, and what's news between syncs), quizzes (facts, never questions) and
/// discussions (a prompt, never anyone's replies).</summary>
public class CanvasAnnouncementTests
{
    static (TempDir Dir, CanvasSync Sync, FakeCanvas Canvas) Synced()
    {
        var dir = new TempDir();
        var sync = FakeCanvas.Library(dir, () => FakeCanvas.DesignNow);
        var canvas = FakeCanvas.Cs101();
        Assert.True(canvas.Run(sync));
        return (dir, sync, canvas);
    }

    [Fact]
    public void Announcements_are_read_newest_first_with_their_attachment_saved()
    {
        var (dir, sync, canvas) = Synced();
        using var _dir = dir;
        var index = CourseIndex.Load(dir.Path, "CS 101")!;
        Assert.Equal(["Lab 3 is up", "Office hours move to Thursday this week", "Problem set 3 solutions", "Welcome to COMP 101"],
            index.Announcements.Select(a => a.Title));
        var unread = index.Announcements.Single(a => a.Title == "Lab 3 is up");
        Assert.Equal(("Dr. Okafor", false), (unread.Author, unread.ReadOnCanvas));
        Assert.Contains("**Tuesday 30 September at 11:59 PM**", unread.Body); // <strong> reads as Markdown bold

        var withFile = index.Announcements.Single(a => a.Title == "Problem set 3 solutions");
        var file = Assert.Single(withFile.Files);
        Assert.Equal("Canvas/announcements/files/ps3-solutions.pdf", file.Local);
        Assert.Equal("%PDF-1.4 ps3 solutions", File.ReadAllText(Path.Combine(FakeCanvas.CanvasRoot(dir), "announcements", "files", "ps3-solutions.pdf")));

        string news = File.ReadAllText(Path.Combine(FakeCanvas.CanvasRoot(dir), "announcements.md"));
        Assert.Contains("**Attached:** [ps3-solutions.pdf](announcements/files/ps3-solutions.pdf)", news);

        // Synced again, unchanged: the file isn't asked for twice.
        int asked = canvas.Requested.Count;
        Assert.True(canvas.Run(sync));
        Assert.DoesNotContain(canvas.Requested.Skip(asked), u => u.Contains("/download", StringComparison.Ordinal));
    }

    [Fact]
    public void A_new_announcement_is_news_only_from_its_second_sync_on()
    {
        using var dir = new TempDir();
        var sync = FakeCanvas.Library(dir, () => FakeCanvas.DesignNow);
        var said = new List<CanvasChange>();
        sync.Finished += said.AddRange;
        Assert.True(FakeCanvas.Cs101().Run(sync)); // first sync: a whole term's worth of announcements is never "news"
        Assert.DoesNotContain(said, c => c.Kind == "announcement");

        said.Clear();
        var again = FakeCanvas.Cs101().Json("/api/v1/courses/4201/discussion_topics", "cs101-announcements-plus-midterm.json");
        Assert.True(again.Run(sync));
        var change = Assert.Single(said, c => c.Kind == "announcement");
        Assert.Equal("Announcement: CS 101 · Midterm review posted", change.Text);
    }

    [Fact]
    public void A_quiz_keeps_only_its_facts_never_its_questions()
    {
        var (dir, _, canvas) = Synced();
        using var _dir = dir;
        var index = CourseIndex.Load(dir.Path, "CS 101")!;
        Assert.Equal(2, index.Quizzes.Count);

        // Folded into "Syllabus quiz" (assignment 9005): no standalone file of its own.
        var syllabusQuiz = index.Quizzes.Single(q => q.Title == "Syllabus quiz");
        Assert.Null(syllabusQuiz.Local);

        // Practice/ungraded: its own file, with its facts, never a question.
        var practice = index.Quizzes.Single(q => q.Title == "Practice: recursion warm-up");
        Assert.Equal("Canvas/quizzes/Practice- recursion warm-up.md", practice.Local);
        string text = File.ReadAllText(Path.Combine(dir.Path, "pool", "CS 101", practice.Local!));
        Assert.Contains("Quiz · 8 questions · unlimited attempts", text);
        Assert.Contains("Ungraded practice questions on recursion.", text);
        Assert.DoesNotContain(canvas.Requested, u => u.Contains("/quizzes/7002/questions", StringComparison.Ordinal) || u.Contains("quiz_submissions", StringComparison.Ordinal));
    }

    [Fact]
    public void A_discussion_keeps_only_its_prompt_never_a_classmates_reply()
    {
        var (dir, _, canvas) = Synced();
        using var _dir = dir;
        var index = CourseIndex.Load(dir.Path, "CS 101")!;
        var topic = Assert.Single(index.Discussions);
        Assert.Equal(("Introduce yourself", "Canvas/discussions/Introduce yourself.md"), (topic.Title, topic.Local));
        Assert.Contains("Post a short introduction", topic.Prompt);

        string text = File.ReadAllText(Path.Combine(dir.Path, "pool", "CS 101", topic.Local!));
        Assert.DoesNotContain("Sam Rivera", text); // another student's reply, never read
        Assert.DoesNotContain(canvas.Requested, u => u.Contains("/discussion_topics/8001/entries", StringComparison.Ordinal));
    }
}
