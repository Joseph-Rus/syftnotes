namespace StudyStash.App.Tests;

public class PlainPassageTests
{
    [Theory]
    [InlineData("**Gradient Descent Process**", "Gradient Descent Process")]
    [InlineData("- Updating parameters using `gradient descent`:", "Updating parameters using gradient descent:")]
    [InlineData("## Key points\nThe [slides](files/week2.pdf) say so", "Key points The slides say so")]
    [InlineData("a snake_case name stays", "a snake_case name stays")]
    public void A_search_passage_reads_without_its_Markdown(string text, string expected) =>
        Assert.Equal(expected, Shell.PlainPassage(text));
}

public class TranscriptParagraphTests
{
    [Fact]
    public void A_short_turn_stays_one_paragraph() =>
        Assert.Equal(["Me: Hello. How's everybody?"], Shell.Paragraphs("Me: Hello. How's everybody?").ToList());

    [Fact]
    public void A_whole_lecture_on_one_line_breaks_at_sentence_ends()
    {
        string sentence = "This is one sentence of the lecture that goes on for a while. ";
        string text = string.Concat(Enumerable.Repeat(sentence, 60)).Trim();
        var paragraphs = Shell.Paragraphs(text, 600).ToList();
        Assert.True(paragraphs.Count >= 5);
        Assert.All(paragraphs, p => Assert.EndsWith(".", p));
        Assert.Equal(text, string.Join(" ", paragraphs));
    }
}
