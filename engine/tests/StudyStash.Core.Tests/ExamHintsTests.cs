namespace StudyStash.Core.Tests;

/// <summary>The exam flags: the moments a lecturer says what's (and what isn't) on the exam, found in the transcript
/// without an AI, each with its moment and the sentence it was said in.</summary>
public class ExamHintsTests
{
    [Fact]
    public void A_lecture_s_flags_come_with_their_moments_and_a_won_t_be_on_the_exam_is_marked_as_one()
    {
        var hints = ExamHints.Find(StudyLibrary.BloodTranscript);

        Assert.Equal(3, hints.Count);
        Assert.Equal(("02:30", "This will definitely be on the exam, so make sure you know the three layers of an artery wall.", false),
            (hints[0].Moment, hints[0].Said, hints[0].Not));
        Assert.Equal(("05:15", true), (hints[1].Moment, hints[1].Not));
        Assert.Equal(("06:40", "Write this down.", false), (hints[2].Moment, hints[2].Said, hints[2].Not));
    }

    [Theory]
    [InlineData("This is going to be on the midterm.")]
    [InlineData("Expect a question like this on your final.")]
    [InlineData("You need to know the Krebs cycle.")]
    [InlineData("You'll have to be able to derive this.")]
    [InlineData("Make sure you understand why the loop terminates.")]
    [InlineData("I will ask you this on the quiz.")]
    [InlineData("This will probably show up on the test.")]
    [InlineData("Everything in chapter four is fair game.")]
    [InlineData("Pay close attention to the boundary conditions.")]
    [InlineData("I guarantee one of these is on there.")]
    [InlineData("This is a classic exam question.")]
    [InlineData("Know this cold.")]
    public void What_lecturers_say_to_flag_something_is_found(string said)
    {
        var hint = Assert.Single(ExamHints.Find(said));
        Assert.False(hint.Not);
        Assert.Null(hint.At);
        Assert.Equal("", hint.Moment);
    }

    [Theory]
    [InlineData("This won't be on the exam.")]
    [InlineData("The proof is not on the final.")]
    [InlineData("You don't need to memorize these dates.")]
    [InlineData("I'm not going to test you on the derivation.")]
    [InlineData("None of this will be on any test, it isn't in the syllabus.")]
    public void What_they_say_is_not_on_it_is_found_the_other_way(string said) => Assert.True(Assert.Single(ExamHints.Find(said)).Not);

    [Theory]
    [InlineData("The final step is to normalise the vector.")]
    [InlineData("We tested this in the lab last week.")]
    [InlineData("The function returns true.")]
    [InlineData("Arteries carry blood away from the heart.")]
    [InlineData("")]
    public void Ordinary_teaching_is_not_flagged(string said) => Assert.Empty(ExamHints.Find(said));

    [Fact]
    public void A_line_with_two_sentences_flags_only_the_one_that_says_it()
    {
        var hint = Assert.Single(ExamHints.Find("[01:00] Veins have valves. Make sure you know where the valves are. Then capillaries."));
        Assert.Equal(("01:00", "Make sure you know where the valves are."), (hint.Moment, hint.Said));
    }

    [Fact]
    public void A_flag_said_twice_in_a_row_is_one_moment()
    {
        Assert.Single(ExamHints.Find("[01:00] Write this down.\n[01:02] Write this down."));
    }

    [Fact]
    public void A_sentence_that_runs_on_is_cut_round_what_was_said()
    {
        string before = string.Join(' ', Enumerable.Repeat("and then the", 30));
        string after = string.Join(' ', Enumerable.Repeat("so we go", 30));
        var hint = Assert.Single(ExamHints.Find($"{before} this will be on the exam {after}"));
        Assert.Contains("this will be on the exam", hint.Said);
        Assert.StartsWith("… ", hint.Said);
        Assert.EndsWith(" …", hint.Said);
        Assert.True(hint.Said.Split(' ').Length <= 44);
    }

    [Fact]
    public void Will_be_on_it_counts_when_the_exam_was_just_named()
    {
        var hint = Assert.Single(ExamHints.Find("[18:05] Okay, the midterm. Recursion traces will be on it, the stack diagrams from last week."));
        Assert.Equal("Recursion traces will be on it, the stack diagrams from last week.", hint.Said);
        Assert.Empty(ExamHints.Find("[18:05] Okay, the stack. The plates will be on it, one by one."));
    }
}
