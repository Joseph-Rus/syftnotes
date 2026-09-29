using StudyStash.Core.Canvas;

namespace StudyStash.Core.Tests;

/// <summary>The cross-class Due list, and what Canvas's planner adds to it: a to-do with no assignment of its own,
/// and an assignment the student ticked off there even though nothing was ever handed in.</summary>
public class CanvasDueTests
{
    [Fact]
    public void An_assignment_marked_done_in_the_planner_moves_to_handed_in()
    {
        using var dir = new TempDir();
        var sync = FakeCanvas.Library(dir, () => FakeCanvas.DesignNow);
        var canvas = FakeCanvas.Cs101().Json("/api/v1/planner/items", "cs101-planner-marked-done.json");
        Assert.True(canvas.Run(sync));

        var lab3 = Assignments.Load(dir.Path).Single(a => a.Id == 9001);
        Assert.True(lab3.Done);
        Assert.Equal(("open", true, "2025-09-24T20:00:00Z"), (lab3.Status, lab3.MarkedDone, lab3.MarkedDoneAt));

        var due = CanvasView.Due(Assignments.Load(dir.Path), "now", FakeCanvas.DesignNow, FakeCanvas.Zone, (_, _) => null);
        var groups = due["groups"]!.AsArray().ToDictionary(g => g!["key"]!.GetValue<string>(), g => g!["items"]!.AsArray());
        Assert.DoesNotContain(groups["week"]!, i => i!["name"]!.GetValue<string>().Contains("Lab 3"));
        var handedIn = groups["handed_in"]!.Single(i => i!["name"]!.GetValue<string>().Contains("Lab 3"))!;
        Assert.Equal("2025-09-24T20:00:00Z", handedIn["marked_done"]!.GetValue<string>()); // when, not just that
        Assert.Equal("To do", handedIn["label"]!.GetValue<string>()); // marked done says nothing about being graded or submitted

        // The index keeps it (ApplyPlanner bakes the planner's read into the promoted index).
        var info = CourseIndex.Load(dir.Path, "CS 101")!.Assignments.Single(a => a.Id == 9001);
        Assert.True(info.MarkedDone);

        // A sync whose planner listing fails keeps what was known, rather than un-marking it.
        var canvas2 = FakeCanvas.Cs101().Status("/api/v1/planner/items", 500, "Service Unavailable");
        Assert.True(canvas2.Run(sync));
        Assert.True(CourseIndex.Load(dir.Path, "CS 101")!.Assignments.Single(a => a.Id == 9001).MarkedDone);
    }

    [Fact]
    public void A_planner_to_do_with_no_assignment_of_its_own_is_kept_apart_and_calendar_events_announcements_and_graded_quizzes_are_dropped()
    {
        using var dir = new TempDir();
        var sync = FakeCanvas.Library(dir, () => FakeCanvas.DesignNow);
        var canvas = FakeCanvas.Cs101(); // its default planner fixture: one wiki_page to-do, one calendar_event, one
                                         // announcement, one graded quiz
        Assert.True(canvas.Run(sync));

        var index = CourseIndex.Load(dir.Path, "CS 101")!;
        var todo = Assert.Single(index.Todos);
        Assert.Equal(("wiki_page", "Read: Chapter 4 overview", "2025-09-29T06:59:00Z"), (todo.Kind, todo.Title, todo.TodoAt));
        Assert.DoesNotContain(index.Todos, t => t.Kind == "calendar_event"); // not coursework
        Assert.DoesNotContain(index.Todos, t => t.Kind == "announcement"); // its date is when it was posted
        Assert.DoesNotContain(index.Todos, t => t.Kind == "quiz"); // an assignment already
        Assert.DoesNotContain(index.Assignments, a => a.MarkedDone); // nobody marked anything done here

        // A to-do rides along in the Due list too (the app merges CourseIndex.Todos in the same way for every
        // linked class): a "todo" kind row, due Mon 29 Sep, so it's in "This week".
        var all = Assignments.Load(dir.Path).Append(Assignments.From("CS 101", todo, FakeCanvas.Zone)).ToList();
        var due = CanvasView.Due(all, "now", FakeCanvas.DesignNow, FakeCanvas.Zone, (_, _) => null);
        var groups = due["groups"]!.AsArray().ToDictionary(g => g!["key"]!.GetValue<string>(), g => g!["items"]!.AsArray());
        var row = groups["week"]!.Single(i => i!["kind"]!.GetValue<string>() == "todo")!;
        Assert.Equal("Read: Chapter 4 overview", row["name"]!.GetValue<string>());
    }

    [Fact]
    public void An_older_syncs_announcements_and_duplicate_quizzes_stay_out_of_the_Due_list()
    {
        var quiz = new Assignment("CS 101", 9004, "Syllabus quiz", "", 5, "open", null, "", "", Kind: "quiz");
        TodoInfo[] kept =
        [
            new() { Id = 1, Kind = "announcement", Title = "Welcome", TodoAt = "2025-09-02T07:00:05Z" },
            new() { Id = 2, Kind = "quiz", Title = "Syllabus quiz", TodoAt = "2025-09-26T17:45:00Z" },
            new() { Id = 3, Kind = "wiki_page", Title = "Read: Chapter 4 overview", TodoAt = "2025-09-29T06:59:00Z" },
        ];

        var rows = Assignments.Todos("CS 101", kept, [quiz], FakeCanvas.Zone).ToList();

        Assert.Equal(["Read: Chapter 4 overview"], rows.Select(r => r.Name));
    }
}
