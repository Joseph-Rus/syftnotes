using StudyStash.Core.Ai;
using StudyStash.Core.Study;

namespace StudyStash.Core.Tests;

/// <summary>Review: flashcards from lectures, brought back on a spaced schedule (Again in ten minutes, Good tomorrow,
/// then longer and longer by the card's ease), kept in review.json, made by the library's AI from a lecture's notes
/// and its exam flags, and graded, edited and deleted over the library from any device.</summary>
public class ReviewTests
{
    static readonly DateTimeOffset Monday = new(2026, 10, 19, 9, 0, 0, TimeSpan.Zero);

    static ReviewCard Card(string front = "What do arteries do?", string lecture = "v1", DateTimeOffset? made = null) =>
        Review.New(lecture, "BIO 110", "Blood vessels", front, "Carry blood away from the heart.", 150, "notes", made ?? Monday);

    [Fact]
    public void A_new_card_answered_Good_comes_back_tomorrow_then_further_apart_by_its_ease()
    {
        var c = Card();
        Assert.True(c.IsNew);

        c = Review.Next(c, Grade.Good, Monday);
        Assert.Equal((1d, 1, 2.5), (c.Interval, c.Reps, c.Ease));
        Assert.Equal(Monday.AddDays(1), c.Due);
        Assert.False(c.IsNew);

        c = Review.Next(c, Grade.Good, c.Due);
        Assert.Equal(3d, c.Interval); // max(1 + 1, 1 × 2.5) = 2.5, rounded
        c = Review.Next(c, Grade.Good, c.Due);
        Assert.Equal(8d, c.Interval); // 3 × 2.5 = 7.5
        c = Review.Next(c, Grade.Easy, c.Due);
        Assert.Equal(26d, c.Interval); // 8 × 2.5 × 1.3
        Assert.Equal(2.65, c.Ease);
        c = Review.Next(c, Grade.Hard, c.Due);
        Assert.Equal((31d, 2.5), (c.Interval, c.Ease)); // 26 × 1.2 = 31.2
    }

    [Fact]
    public void Forgetting_a_learned_card_starts_it_again_with_less_ease_and_counts_the_lapse()
    {
        var c = Review.Next(Review.Next(Card(), Grade.Good, Monday), Grade.Good, Monday.AddDays(1));
        var lapsed = Review.Next(c, Grade.Again, Monday.AddDays(4));

        Assert.Equal((0d, 0, 1, 2.3), (lapsed.Interval, lapsed.Reps, lapsed.Lapses, lapsed.Ease));
        Assert.Equal(Monday.AddDays(4) + Review.Relearn, lapsed.Due);
        // A new card answered Again isn't a lapse: it was never learned.
        var fresh = Review.Next(Card(), Grade.Again, Monday);
        Assert.Equal((0, 2.5), (fresh.Lapses, fresh.Ease));
        Assert.Equal(1d, Review.Next(lapsed, Grade.Good, lapsed.Due).Interval);
        Assert.Equal(4d, Review.Next(Card(), Grade.Easy, Monday).Interval);
    }

    [Fact]
    public void Ease_never_drops_under_1_3_and_no_gap_is_over_a_year()
    {
        var c = Card() with { Interval = 300, Ease = 1.35, Reviewed = Monday, Reps = 9 };
        Assert.Equal(Review.MinEase, Review.Next(c, Grade.Hard, Monday).Ease);
        Assert.Equal(Review.MaxDays, Review.Next(c with { Ease = 3 }, Grade.Easy, Monday).Interval);
    }

    [Fact]
    public void The_buttons_say_when_each_grade_brings_it_back()
    {
        var c = Card();
        Assert.Equal(["10 min", "1 day", "1 day", "4 days"], Enum.GetValues<Grade>().Select(g => Review.After(c, g, Monday)));
        var learned = c with { Interval = 40, Reps = 4, Reviewed = Monday };
        Assert.Equal("3.3 mo", Review.After(learned, Grade.Good, Monday));
        Assert.Equal("1 yr", Review.Span(TimeSpan.FromDays(365)));
    }

    [Fact]
    public void Whats_due_comes_longest_overdue_first_then_a_few_new_and_is_counted()
    {
        using var dir = new TempDir();
        var review = new Review(dir["home"]);
        var overdue = Card("Q1") with { Reviewed = Monday.AddDays(-5), Interval = 2, Due = Monday.AddDays(-3) };
        var dueNow = Card("Q2") with { Reviewed = Monday.AddDays(-2), Interval = 2, Due = Monday };
        var later = Card("Q3") with { Reviewed = Monday, Interval = 5, Due = Monday.AddDays(5) };
        var tonight = Card("Q4") with { Reviewed = Monday.AddDays(-1), Interval = 1, Due = Monday.AddHours(8) };
        var news = Enumerable.Range(0, 25).Select(i => Card($"New {i}", made: Monday.AddMinutes(-i))).ToList();
        var chem = Card("Moles?", lecture: "c1") with { Class = "CHEM 121" };
        review.Add([later, dueNow, overdue, tonight, chem, .. news]);

        var due = review.Due(Monday);
        Assert.Equal(["Q1", "Q2"], due.Take(2).Select(c => c.Front));
        Assert.Equal(22, due.Count); // two learned, twenty new (of the 25 new)
        Assert.Equal("New 24", due[2].Front); // the oldest new first
        var counts = review.Counts(Monday);
        Assert.Equal((22, 20, 30, 23), (counts.Due, counts.New, counts.Total, counts.Today));
        Assert.Equal(["Moles?"], review.Due(Monday, "CHEM 121").Select(c => c.Front));
        Assert.Equal(5, review.Due(Monday, limit: 5).Count);
    }

    [Fact]
    public void A_lecture_never_gets_the_same_question_twice_and_its_cards_go_with_it()
    {
        using var dir = new TempDir();
        var review = new Review(dir["home"]);
        Assert.Single(review.Add([Card("What do arteries do?")]));
        Assert.Empty(review.Add([Card("what do ARTERIES do")]));
        Assert.Single(review.Add([Card("What do arteries do?", lecture: "m1")]));
        Assert.Empty(review.Add([Card("  ") ]));

        // Kept on disk: another Review over the same folder sees them.
        Assert.Equal(2, new Review(dir["home"]).All().Count);
        Assert.Equal(1, review.ForgetLecture("v1"));
        Assert.Equal(["m1"], review.All().Select(c => c.Lecture));
    }

    [Fact]
    public void A_card_is_graded_edited_and_deleted_by_its_id()
    {
        using var dir = new TempDir();
        var review = new Review(dir["home"]);
        var card = review.Add([Card()]).Single();

        var graded = review.Grade(card.Id, Grade.Good, Monday)!;
        Assert.Equal(Monday.AddDays(1), review.Get(card.Id)!.Due);
        Assert.Equal(graded, review.Get(card.Id));
        Assert.Null(review.Grade("card-nope", Grade.Good, Monday));

        Assert.Equal("Which way do arteries carry blood?", review.Edit(card.Id, " Which way do arteries carry blood? ", "Away from the heart.")!.Front);
        Assert.Null(review.Edit(card.Id, "", "x"));
        Assert.True(review.Delete(card.Id));
        Assert.False(review.Delete(card.Id));
        Assert.Empty(review.All());
    }

    [Fact]
    public void A_damaged_review_file_reads_as_no_cards()
    {
        using var dir = new TempDir();
        Directory.CreateDirectory(dir["home"]);
        File.WriteAllText(Path.Combine(dir["home"], "review.json"), "[[[");
        Assert.Empty(new Review(dir["home"]).All());
    }

    [Theory]
    [InlineData("02:30", 150d)]
    [InlineData("[1:02:03]", 3723d)]
    [InlineData("about two minutes in", null)]
    [InlineData("", null)]
    public void A_cards_moment_is_read_from_mm_ss(string moment, double? seconds) => Assert.Equal(seconds, StudyMaterial.Seconds(moment));

    const string Cards = """
        Here are your cards:
        {"cards":[
          {"front":"What are the three layers of an artery wall?","back":"Tunica intima, media and externa.","moment":"02:30"},
          {"front":"How thick is a capillary wall?","back":"One cell thick.","moment":"04:00"},
          {"front":"What stops blood flowing backwards in veins?","back":"Valves.","moment":"about six minutes"},
          {"front":"","back":"nothing"}
        ]}
        """;

    [Fact]
    public async Task The_library_makes_a_lectures_cards_from_its_notes_and_exam_flags_once()
    {
        await using var lib = await StudyLibrary.StartAsync();
        lib.Answers.Add(Cards);

        var made = (await lib.Study.MakeCardsAsync("v1"))!;

        Assert.Equal(3, made.Added.Count);
        Assert.Equal(("What are the three layers of an artery wall?", 150d, "BIO 110", "Blood vessels"),
            (made.Added[0].Front, made.Added[0].At!.Value, made.Added[0].Class, made.Added[0].LectureTitle));
        Assert.Null(made.Added[2].At);
        string prompt = lib.Claude.Prompts.Single();
        Assert.Contains("Write 12 flashcards", prompt);
        Assert.Contains("[02:30] This will definitely be on the exam", prompt);
        Assert.Contains("won't be:\n- [05:15]", prompt);
        Assert.Contains("- Capillaries are one cell thick.", prompt);

        // Asked again, it has its cards: none are written twice unless more are asked for.
        var again = (await lib.Study.MakeCardsAsync("v1"))!;
        Assert.Empty(again.Added);
        Assert.Equal(3, again.Cards.Count);
        Assert.Single(lib.Claude.Prompts);
        var more = (await lib.Study.MakeCardsAsync("v1", more: true))!;
        Assert.Empty(more.Added); // the AI said the same three again: nothing new
        Assert.Equal(2, lib.Claude.Prompts.Count);

        var state = (await lib.Study.ReviewAsync())!;
        Assert.Equal((3, 3, 3), (state.Counts.Due, state.Counts.New, state.Counts.Total));
        Assert.Equal(3, (await lib.Study.LectureCardsAsync("v1"))!.Count);
        Assert.Empty((await lib.Study.ReviewAsync("CHEM 121"))!.Due);
    }

    [Fact]
    public async Task Cards_are_graded_edited_added_and_deleted_over_the_library()
    {
        await using var lib = await StudyLibrary.StartAsync();
        lib.Answers.Add(Cards);
        var card = (await lib.Study.MakeCardsAsync("v1"))!.Added[0];

        var graded = (await lib.Study.GradeAsync(card.Id, Grade.Good))!;
        Assert.Equal(lib.Now.AddDays(1), graded.Due);
        Assert.Equal(2, (await lib.Study.ReviewAsync())!.Counts.Due);
        lib.Now = lib.Now.AddDays(1);
        Assert.Equal(card.Id, (await lib.Study.ReviewAsync())!.Due[0].Id); // learned and due comes before the new

        Assert.Equal("Name the three layers.", (await lib.Study.EditCardAsync(card.Id, "Name the three layers.", "Intima, media, externa."))!.Front);
        var own = (await lib.Study.AddCardAsync("v1", "Where are valves?", "In veins.", from: "exam"))!;
        Assert.Equal(("exam", "BIO 110"), (own.From, own.Class));
        await Assert.ThrowsAsync<LibraryRefusedException>(() => lib.Study.AddCardAsync("v1", "where are valves", "In veins."));
        await Assert.ThrowsAsync<LibraryRefusedException>(() => lib.Study.AddCardAsync("nope", "Q", "A"));
        await Assert.ThrowsAsync<LibraryRefusedException>(() => lib.Study.GradeAsync("card-missing", Grade.Good));

        Assert.True(await lib.Study.DeleteCardAsync(own.Id));
        Assert.False(await lib.Study.DeleteCardAsync(own.Id));
    }

    [Fact]
    public async Task An_AI_that_doesnt_answer_with_cards_is_said_so()
    {
        await using var lib = await StudyLibrary.StartAsync();
        lib.Answers.Add("Sorry, I can't help with that.");
        var e = await Assert.ThrowsAsync<LibraryRefusedException>(() => lib.Study.MakeCardsAsync("v1"));
        Assert.Equal(503, e.Status);
        Assert.Contains("couldn't be made", e.Message);
        Assert.Empty((await lib.Study.LectureCardsAsync("v1"))!);
    }

    [Fact]
    public async Task A_lectures_exam_flags_are_read_over_the_library()
    {
        await using var lib = await StudyLibrary.StartAsync();
        var flags = (await lib.Study.ExamFlagsAsync("v1"))!;
        Assert.Equal([(150d, false), (315d, true), (400d, false)], flags.Select(f => (f.At!.Value, f.Not)));
        Assert.Equal(404, (await Assert.ThrowsAsync<LibraryRefusedException>(() => lib.Study.ExamFlagsAsync("nope"))).Status);
    }
}
