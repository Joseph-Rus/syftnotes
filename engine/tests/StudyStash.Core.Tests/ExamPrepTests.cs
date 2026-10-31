using System.Text.Json.Nodes;
using StudyStash.Core.Ai;
using StudyStash.Core.Calendar;
using StudyStash.Core.Canvas;
using StudyStash.Core.Study;

namespace StudyStash.Core.Tests;

/// <summary>Exam prep: the exams coming up found in Canvas and the calendar by their names, and for one a study guide
/// and practice test from the class's lectures since the exam before it, written by the library's AI with what the
/// lecturer flagged first; each go at the test kept, and what was missed put into Review.</summary>
public class ExamPrepTests
{
    static readonly DateTimeOffset Now = new(2026, 10, 20, 9, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("Midterm 2", true)]
    [InlineData("Final Exam", true)]
    [InlineData("Quiz 4: Vessels", true)]
    [InlineData("Unit test (chapters 3–5)", true)]
    [InlineData("Prelim 1", true)]
    [InlineData("Final project", false)]
    [InlineData("Quiz 3 solutions", false)]
    [InlineData("Exam review session", false)]
    [InlineData("Lab report 2", false)]
    [InlineData("Test plan draft", false)]
    public void An_exam_is_known_by_its_name(string title, bool exam) => Assert.Equal(exam, ExamPrep.IsExam(title));

    [Fact]
    public void Exams_coming_up_are_soonest_first_each_once_in_known_classes()
    {
        var found = new List<ExamDate>
        {
            new("BIO 110", "Midterm 2", Now.AddDays(9), "calendar"),
            new("BIO 110", "Midterm 2", Now.AddDays(9).AddHours(1), "canvas"),
            new("BIO 110", "Midterm 1", Now.AddDays(-20), "canvas"),
            new("CHEM 121", "Quiz 4", Now.AddDays(2), "canvas"),
            new("CHEM 121", "Final exam", Now.AddDays(90), "canvas"),
            new("ART 100", "Final", Now.AddDays(5), "canvas"),
            new("BIO 110", "Lab report", Now.AddDays(3), "canvas"),
        };

        var upcoming = ExamPrep.Upcoming(found, Now, ["BIO 110", "CHEM 121"]);

        Assert.Equal([("CHEM 121", "Quiz 4"), ("BIO 110", "Midterm 2")], upcoming.Select(e => (e.Class, e.Title)));
        Assert.Equal("canvas", upcoming[1].From);
    }

    [Fact]
    public void A_preps_lectures_are_the_classes_since_the_exam_before_up_to_this_one()
    {
        using var dir = new TempDir();
        using var store = new Store(Path.Combine(dir["home"], "db.sqlite"), dir["pool"]);
        foreach (var (id, date) in new[] { ("a", "2026-09-01"), ("b", "2026-09-20"), ("c", "2026-10-02"), ("d", "2026-10-15"), ("e", "2026-11-01") })
            store.Save(new Meeting(id) { Title = id, Date = date, Transcript = "words" }, new Classification("BIO 110", 1, "folder"), "## Notes", "Claude");

        var covered = ExamPrep.Covered(store, "BIO 110", since: new DateTimeOffset(2026, 9, 25, 0, 0, 0, TimeSpan.Zero),
            until: new DateTimeOffset(2026, 10, 29, 0, 0, 0, TimeSpan.Zero), Now);
        Assert.Equal(["c", "d"], covered.Select(r => r.Id));
        Assert.Equal(["a", "b", "c", "d"], ExamPrep.Covered(store, "BIO 110", null, null, Now).Select(r => r.Id));
        Assert.Equal(["c", "d"], ExamPrep.Covered(store, "BIO 110", null, null, Now, most: 2).Select(r => r.Id));
    }

    const string Prep = """
        {"guide":"## What the lecturer said to know\n- The three layers of an artery wall (Lecture 1, 02:30)\n\n## Vessels\nArteries carry blood away.",
         "questions":[
           {"kind":"choice","question":"Which vessel is one cell thick?","choices":["Artery","Vein","Capillary","Aorta"],"answer":2,"why":"Exchange happens there.","lecture":1,"moment":"04:00"},
           {"kind":"short","question":"Name the three layers of an artery wall.","model_answer":"Intima, media and externa.","why":"Said to know it.","lecture":1,"moment":"02:30"},
           {"kind":"choice","question":"Broken: answer out of range","choices":["A","B"],"answer":5},
           {"kind":"short","question":"No answer given"},
           {"kind":"choice","question":"What slides over myosin?","choices":["Actin","Titin"],"answer":0,"lecture":2}
         ]}
        """;

    static async Task<StudyLibrary> LibraryAsync()
    {
        var lib = await StudyLibrary.StartAsync(l =>
        {
            Assignments.Save(l.Cfg.Home,
            [
                new Assignment("BIO 110", 1, "Midterm 1", "", null, "graded", null, "", "", DueAt: "2026-10-05T14:00:00Z", Kind: "quiz"),
                new Assignment("BIO 110", 2, "Midterm 2", "", null, "", null, "", "", DueAt: "2026-10-29T14:00:00Z", Kind: "quiz"),
                new Assignment("BIO 110", 3, "Lab report 3", "", null, "", null, "", "", DueAt: "2026-10-23T14:00:00Z"),
            ]);
            LibraryCalendar.Save(l.Cfg.Home, new JsonObject
            {
                ["events"] = new JsonArray(new JsonObject
                {
                    ["id"] = "q4", ["title"] = "CHEM 121 Quiz 4", ["start"] = "2026-10-22T15:00:00Z", ["end"] = "2026-10-22T16:00:00Z", ["class"] = "CHEM 121",
                }),
            }, DateTimeOffset.UtcNow);
        });
        lib.Answers.Add(Prep);
        return lib;
    }

    [Fact]
    public async Task The_library_lists_the_exams_coming_up_from_Canvas_and_the_calendar()
    {
        await using var lib = await LibraryAsync();
        var exams = (await lib.Study.ExamsAsync())!;
        Assert.Equal([("CHEM 121", "CHEM 121 Quiz 4", "calendar"), ("BIO 110", "Midterm 2", "canvas")],
            exams.Upcoming.Select(e => (e.Class, e.Title, e.From)));
        Assert.Empty(exams.Preps);
    }

    [Fact]
    public async Task A_prep_is_written_from_the_lectures_since_the_exam_before_with_the_flags_first()
    {
        await using var lib = await LibraryAsync();

        var prep = (await lib.Study.MakePrepAsync("BIO 110", "Midterm 2", new DateTimeOffset(2026, 10, 29, 14, 0, 0, TimeSpan.Zero)))!;

        // Midterm 1 was on the 5th: Muscles (the 1st) was for it; Blood vessels (the 8th) is for this one.
        Assert.Equal(["Blood vessels"], prep.Lectures.Select(l => l.Title));
        string prompt = lib.Claude.Prompts.Single();
        Assert.Contains("\"Midterm 2\" on Thursday 29 October", prompt);
        Assert.Contains("=== Lecture 1 ===\nLecture: Blood vessels (2026-10-08)", prompt);
        Assert.Contains("[02:30] This will definitely be on the exam", prompt);
        Assert.DoesNotContain("Muscles", prompt);

        Assert.StartsWith("## What the lecturer said to know", prep.Guide);
        // The broken ones are left out; one from a lecture it doesn't have keeps its question, from no lecture.
        Assert.Equal(["q1", "q2", "q3"], prep.Questions.Select(q => q.Id));
        Assert.Equal(("What slides over myosin?", ""), (prep.Questions[2].Question, prep.Questions[2].Lecture));
        Assert.Equal(("choice", "Capillary", "v1", 240d), (prep.Questions[0].Kind, prep.Questions[0].ModelAnswer, prep.Questions[0].Lecture, prep.Questions[0].At!.Value));
        Assert.Equal(("short", "Intima, media and externa."), (prep.Questions[1].Kind, prep.Questions[1].ModelAnswer));
        Assert.Equal("Claude", prep.By);

        var read = (await lib.Study.PrepAsync(prep.Id))!;
        Assert.Equal((prep.Guide, 3, "Blood vessels"), (read.Guide, read.Questions.Count, read.Lectures.Single().Title));
        Assert.Equal(prep.Id, (await lib.Study.ExamsAsync())!.Preps.Single().Id);
    }

    [Fact]
    public async Task A_go_at_the_test_is_kept_and_the_missed_questions_go_into_Review()
    {
        await using var lib = await LibraryAsync();
        var prep = (await lib.Study.MakePrepAsync("BIO 110", "Midterm 2", null))!;

        var (after, added) = (await lib.Study.AttemptAsync(prep.Id, new() { ["q1"] = true, ["q2"] = false, ["q9"] = false }, reviewMissed: true))!.Value;

        Assert.Equal(1, added);
        var attempt = after.Attempts.Single();
        Assert.Equal((1, 2), (attempt.Right, attempt.Marks.Count)); // q9 isn't one of its questions
        var card = (await lib.Study.LectureCardsAsync("v1"))!.Single();
        Assert.Equal(("Name the three layers of an artery wall.", "Intima, media and externa.\n\nSaid to know it.", "exam", 150d),
            (card.Front, card.Back, card.From, card.At!.Value));
        Assert.Equal(1, (await lib.Study.ExamsAsync())!.Preps.Single().LastRight);

        // Missed again: it's in Review already, so it isn't added twice.
        Assert.Equal(0, (await lib.Study.AttemptAsync(prep.Id, new() { ["q2"] = false }, true))!.Value.AddedToReview);
        Assert.True(await lib.Study.DeletePrepAsync(prep.Id));
        Assert.False(await lib.Study.DeletePrepAsync(prep.Id));
        Assert.Empty((await lib.Study.ExamsAsync())!.Preps);
    }

    [Fact]
    public void A_choice_question_goes_into_Review_with_its_choices()
    {
        var q = new PrepQuestion { Id = "q1", Kind = "choice", Question = "Which is one cell thick?", Choices = ["Artery", "Capillary"], Answer = 1, ModelAnswer = "Capillary" };
        Assert.Equal(("Which is one cell thick?\nA. Artery\nB. Capillary", "Capillary"), ExamPrep.Card(q));
    }

    [Fact]
    public async Task A_prep_needs_a_known_class_and_lectures_and_an_AI_that_answers()
    {
        await using var lib = await LibraryAsync();
        Assert.Equal(400, (await Assert.ThrowsAsync<LibraryRefusedException>(() => lib.Study.MakePrepAsync("BIO 110", " ", null))).Status);
        Assert.Equal(404, (await Assert.ThrowsAsync<LibraryRefusedException>(() => lib.Study.MakePrepAsync("ART 100", "Final", null))).Status);
        Assert.Equal(409, (await Assert.ThrowsAsync<LibraryRefusedException>(() =>
            lib.Study.MakePrepAsync("BIO 110", "Midterm 1", new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero)))).Status);
        lib.Answers[0] = "{\"guide\":\"\",\"questions\":[]}";
        var e = await Assert.ThrowsAsync<LibraryRefusedException>(() => lib.Study.MakePrepAsync("BIO 110", "Midterm 2", null));
        Assert.Equal(503, e.Status);
        Assert.Empty((await lib.Study.ExamsAsync())!.Preps);
    }
}
