using StudyStash.Core.Canvas;

namespace StudyStash.Core.Tests;

/// <summary>Pages, the syllabus and the files their HTML links to: saved once, wherever they first landed, and read
/// back with real local links once the sync finishes.</summary>
public class CanvasContentTests
{
    const string AssignmentsPath = "/api/v1/courses/4201/assignments";
    const string PagesPath = "/api/v1/courses/4201/pages";
    const string FrontPagePath = "/api/v1/courses/4201/front_page";
    const string CoursePath = "/api/v1/courses/4201";
    const string FileDownload = "/files/555/download";

    // Lab 3's instructions link the same Canvas file (555, "recursion-slides.pdf") the Week 4 module also has, so a
    // sync of this fixture always wants that file from at least two places.
    const string LinkedAssignment = """
        [{"id": 9001, "name": "Lab 3: recursion traces", "course_id": 4201, "due_at": "2025-10-01T06:59:00Z",
          "points_possible": 20.0, "grading_type": "points", "submission_types": ["online_upload"], "allowed_attempts": -1,
          "html_url": "https://canvas.test/courses/4201/assignments/9001",
          "description": "<p>See the <a href=\"/courses/4201/files/555\">recursion slides</a> for background. Trace factorial(4) and fib(5) by hand.</p>"}]
        """;

    static string RootFor(TempDir dir) => FakeCanvas.CanvasRoot(dir);

    [Fact]
    public void An_instructions_link_downloads_once_and_spec_md_links_there()
    {
        using var dir = new TempDir();
        var sync = FakeCanvas.Library(dir, () => FakeCanvas.DesignNow);
        var canvas = FakeCanvas.Cs101().Json(AssignmentsPath, LinkedAssignment);
        Assert.True(canvas.Run(sync));

        string root = RootFor(dir);
        string filePath = Path.Combine(root, "assignments", "Lab 3- recursion traces", "files", "recursion-slides.pdf");
        Assert.Equal("%PDF-1.4 recursion slides", File.ReadAllText(filePath));
        Assert.Equal(1, canvas.Requested.Count(u => u.Contains(FileDownload, StringComparison.Ordinal)));

        string spec = File.ReadAllText(Path.Combine(root, "assignments", "Lab 3- recursion traces", "spec.md"));
        Assert.Contains("(files/recursion-slides.pdf)", spec);

        var index = CourseIndex.Load(dir.Path, "CS 101")!;
        var lab3 = index.Assignments.Single(a => a.Id == 9001);
        var file = Assert.Single(lab3.InstructionFiles);
        Assert.Equal((555L, "Canvas/assignments/Lab 3- recursion traces/files/recursion-slides.pdf"), (file.Id, file.Local));
    }

    [Fact]
    public void A_file_that_failed_to_download_is_asked_for_again_next_sync()
    {
        using var dir = new TempDir();
        var sync = FakeCanvas.Library(dir, () => FakeCanvas.DesignNow);
        var canvas = FakeCanvas.Cs101().Json(AssignmentsPath, LinkedAssignment).FailTimes(FileDownload, 3, 500);
        Assert.True(canvas.Run(sync));

        string filePath = Path.Combine(RootFor(dir), "assignments", "Lab 3- recursion traces", "files", "recursion-slides.pdf");
        Assert.False(File.Exists(filePath));

        // Nothing about "someone already asked" survives a download that never arrived: the next sync tries again,
        // and this time Canvas answers.
        Assert.True(canvas.Run(sync));
        Assert.Equal("%PDF-1.4 recursion slides", File.ReadAllText(filePath));
    }

    [Fact]
    public void The_same_file_linked_from_a_page_is_not_downloaded_twice_and_links_to_the_first_copy()
    {
        using var dir = new TempDir();
        var sync = FakeCanvas.Library(dir, () => FakeCanvas.DesignNow);
        var canvas = FakeCanvas.Cs101().Json(AssignmentsPath, LinkedAssignment)
            .Json(PagesPath, """[{"url": "office-hours", "title": "Office hours", "front_page": false, "updated_at": "2025-09-10T12:00:00Z"}]""")
            .Json($"{PagesPath}/office-hours", """
                {"url": "office-hours", "title": "Office hours", "locked_for_user": false,
                 "html_url": "https://canvas.test/courses/4201/pages/office-hours", "updated_at": "2025-09-10T12:00:00Z",
                 "body": "<p>See the <a href=\"/courses/4201/files/555\">recursion slides</a> again, and stop by Tuesdays.</p>"}
                """);
        Assert.True(canvas.Run(sync));
        Assert.Equal("", CanvasSettings.Load(dir.Path).Error);

        Assert.Equal(1, canvas.Requested.Count(u => u.Contains(FileDownload, StringComparison.Ordinal)));
        string page = File.ReadAllText(Path.Combine(RootFor(dir), "pages", "Office hours.md"));
        string expected = CanvasMarkdown.RelativeLink("Canvas/pages", "Canvas/assignments/Lab 3- recursion traces/files/recursion-slides.pdf");
        Assert.Contains($"]({expected})", page);
    }

    [Fact]
    public void A_page_outside_modules_is_saved_once_and_a_module_page_is_not_duplicated()
    {
        using var dir = new TempDir();
        var sync = FakeCanvas.Library(dir, () => FakeCanvas.DesignNow);
        // "lab-3-instructions" is already a page inside the Week 4 module (cs101-modules.json): the listing outside
        // modules must not save it a second time under pages/.
        var canvas = FakeCanvas.Cs101()
            .Json(PagesPath, """
                [{"url": "lab-3-instructions", "title": "Lab 3 instructions", "front_page": false, "updated_at": "2025-09-22T21:10:00Z"},
                 {"url": "office-hours", "title": "Office hours", "front_page": false, "updated_at": "2025-09-10T12:00:00Z"}]
                """)
            .Json($"{PagesPath}/office-hours", """
                {"url": "office-hours", "title": "Office hours", "locked_for_user": false,
                 "html_url": "https://canvas.test/courses/4201/pages/office-hours", "updated_at": "2025-09-10T12:00:00Z", "body": "<p>Tuesdays, 2pm.</p>"}
                """);
        Assert.True(canvas.Run(sync));

        Assert.False(File.Exists(Path.Combine(RootFor(dir), "pages", "Lab 3 instructions.md")));
        Assert.True(File.Exists(Path.Combine(RootFor(dir), "pages", "Office hours.md")));
        // The module's own page is still fetched once (for its module folder copy), never a second time for pages/.
        Assert.Equal(1, canvas.Requested.Count(u => u.EndsWith("/pages/lab-3-instructions", StringComparison.Ordinal)));

        var index = CourseIndex.Load(dir.Path, "CS 101")!;
        Assert.Contains(index.Pages, p => p.Url == "office-hours" && p.Local == "Canvas/pages/Office hours.md");
        Assert.DoesNotContain(index.Pages, p => p.Url == "lab-3-instructions");
    }

    [Fact]
    public void The_front_page_is_saved()
    {
        using var dir = new TempDir();
        var sync = FakeCanvas.Library(dir, () => FakeCanvas.DesignNow);
        var canvas = FakeCanvas.Cs101().Json(FrontPagePath, """
            {"url": "welcome", "title": "Welcome to CS 101", "front_page": true, "locked_for_user": false,
             "html_url": "https://canvas.test/courses/4201/pages/welcome", "updated_at": "2025-09-01T00:00:00Z", "body": "<p>Welcome! Office hours are Tuesdays.</p>"}
            """);
        Assert.True(canvas.Run(sync));

        string text = File.ReadAllText(Path.Combine(RootFor(dir), "pages", "Welcome to CS 101.md"));
        Assert.Contains("Office hours are Tuesdays.", text);

        var front = CourseIndex.Load(dir.Path, "CS 101")!.FrontPage!;
        Assert.Equal(("welcome", "Canvas/pages/Welcome to CS 101.md", true), (front.Url, front.Local, front.FrontPage));
    }

    [Fact]
    public void The_syllabus_has_the_equation_and_the_course_s_own_words()
    {
        using var dir = new TempDir();
        var sync = FakeCanvas.Library(dir, () => FakeCanvas.DesignNow);
        var canvas = FakeCanvas.Cs101().Json(CoursePath, """
            {"id": 4201, "name": "COMP 101 · Intro to Programming", "course_code": "COMP 101", "term": {"name": "Fall 2025"},
             "html_url": "https://canvas.test/courses/4201",
             "syllabus_body": "<p>Grading uses <img class=\"equation_image\" data-equation-content=\"x^2\" src=\"https://canvas.test/equation_images/x.gif\" alt=\"LaTeX: x^2\"> weighting.</p>"}
            """);
        Assert.True(canvas.Run(sync));

        string text = File.ReadAllText(Path.Combine(RootFor(dir), "syllabus.md"));
        Assert.StartsWith("# CS 101: syllabus", text);
        Assert.Contains("$x^2$", text);

        var index = CourseIndex.Load(dir.Path, "CS 101")!;
        Assert.Equal(("COMP 101", "Fall 2025", "Canvas/syllabus.md"), (index.Code, index.Term, index.Syllabus));
    }

    [Fact]
    public void The_class_grade_is_the_students_own_and_only_when_canvas_shares_it()
    {
        using var dir = new TempDir();
        var sync = FakeCanvas.Library(dir, () => FakeCanvas.DesignNow);
        const string head = """{"id": 4201, "name": "COMP 101 · Intro to Programming", "course_code": "COMP 101", "term": {"name": "Fall 2025"}, "html_url": "https://canvas.test/courses/4201",""";
        ClassGrade? Synced(string rest)
        {
            var canvas = FakeCanvas.Cs101().Json(CoursePath, head + rest);
            Assert.True(canvas.Run(sync));
            // The grade rides on the request that already names the course: no call of its own.
            Assert.Single(canvas.Requested, u => new Uri(u).AbsolutePath == CoursePath);
            Assert.Contains("include[]=total_scores", canvas.Requested.First(u => new Uri(u).AbsolutePath == CoursePath), StringComparison.Ordinal);
            return CourseIndex.Load(dir.Path, "CS 101")!.Grade;
        }

        // What's graded so far, not the final that counts the gaps as zero.
        var grade = Synced("""
            "enrollments": [{"type": "student", "computed_current_score": 91.2, "computed_current_grade": "A-", "computed_final_score": 80.0, "computed_final_grade": "B-"}]}
            """);
        Assert.Equal((91.2, "A-"), (grade!.Score, grade.Letter));
        var settings = new CanvasSettings { Url = FakeCanvas.Base };
        settings.Courses["CS 101"] = 4201;
        var row = CanvasView.ClassRow("CS 101", settings, CourseIndex.Load(dir.Path, "CS 101"), new ScoutState("never", 0, "", ""), FakeCanvas.DesignNow);
        Assert.Equal((91.2, "A-"), (row["grade"]!["score"]!.GetValue<double>(), row["grade"]!["letter"]!.GetValue<string>()));

        // A course with no letter scheme has a percentage and no letter.
        var plain = Synced("""
            "enrollments": [{"type": "student", "computed_current_score": 91.2, "computed_current_grade": null}]}
            """);
        Assert.Equal((91.2, ""), (plain!.Score, plain.Letter));

        // Canvas stops sharing it (hidden by the course, or nothing graded yet): the old grade doesn't linger.
        Assert.Null(Synced("""
            "hide_final_grades": true, "enrollments": [{"type": "student", "computed_current_score": 91.2, "computed_current_grade": "A-"}]}
            """));
        Assert.Null(CanvasView.ClassRow("CS 101", settings, CourseIndex.Load(dir.Path, "CS 101"), new ScoutState("never", 0, "", ""), FakeCanvas.DesignNow)["grade"]);
        Assert.Null(Synced("""
            "enrollments": [{"type": "student", "computed_current_score": null, "computed_current_grade": null}]}
            """));
    }

    [Fact]
    public void Pages_hidden_from_the_student_still_lets_the_sync_finish()
    {
        using var dir = new TempDir();
        var sync = FakeCanvas.Library(dir, () => FakeCanvas.DesignNow);
        var canvas = FakeCanvas.Cs101().Status(PagesPath, 401, """{"status":"unauthorized","errors":[{"message":"user not authorized to perform that action"}]}""");
        Assert.True(canvas.Run(sync));

        Assert.False(Directory.Exists(Path.Combine(RootFor(dir), "pages")));
        Assert.Empty(CourseIndex.Load(dir.Path, "CS 101")!.Pages);
        Assert.Equal("", CanvasSettings.Load(dir.Path).Error);
    }

    [Fact]
    public void A_locked_page_is_skipped_but_still_recorded()
    {
        using var dir = new TempDir();
        var sync = FakeCanvas.Library(dir, () => FakeCanvas.DesignNow);
        var canvas = FakeCanvas.Cs101()
            .Json(PagesPath, """[{"url": "final-brief", "title": "Final project brief", "front_page": false, "updated_at": "2025-09-20T00:00:00Z"}]""")
            .Json($"{PagesPath}/final-brief", """
                {"url": "final-brief", "title": "Final project brief", "locked_for_user": true,
                 "html_url": "https://canvas.test/courses/4201/pages/final-brief", "updated_at": "2025-09-20T00:00:00Z"}
                """);
        Assert.True(canvas.Run(sync));

        Assert.False(File.Exists(Path.Combine(RootFor(dir), "pages", "Final project brief.md")));
        var page = CourseIndex.Load(dir.Path, "CS 101")!.Pages.Single(p => p.Url == "final-brief");
        Assert.True(page.Locked);
        Assert.Null(page.Local);
    }
}
