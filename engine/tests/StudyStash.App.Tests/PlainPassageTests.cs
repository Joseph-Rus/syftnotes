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
