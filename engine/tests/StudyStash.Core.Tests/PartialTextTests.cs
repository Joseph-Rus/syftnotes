using StudyStash.Core.Rich;

namespace StudyStash.Core.Tests;

/// <summary>An answer still being written, as the app shows it: a formula in the middle of being written waits for
/// its closing $, and what's left of an answer that stopped partway has no half-drawn diagram at its end.</summary>
public class PartialTextTests
{
    [Theory]
    [InlineData("The ratio is $\\frac{a}{", "The ratio is ")]
    [InlineData("The ratio is $\\frac{a}{b}$ and", "The ratio is $\\frac{a}{b}$ and")]
    [InlineData("First $x$.\n\nThen $y", "First $x$.\n\nThen ")]
    [InlineData("It costs \\$5 and more", "It costs \\$5 and more")]
    [InlineData("Say `$HOME` and go", "Say `$HOME` and go")]
    [InlineData("An inline $$E = mc", "An inline ")]
    public void A_formula_waits_for_its_closing_dollar(string text, string shown) => Assert.Equal(shown, PartialText.Showable(text));

    [Fact]
    public void A_lone_dollar_that_runs_on_is_a_price_not_a_formula()
    {
        string text = "Tickets are $5 each, " + string.Concat(Enumerable.Repeat("and the rest of the sentence runs on ", 10));
        Assert.Equal(text, PartialText.Showable(text));
    }

    [Fact]
    public void A_block_still_open_is_left_for_the_note_to_show_as_being_drawn()
    {
        const string chart = "Steps:\n\n```mermaid\nflowchart LR\n  A[Assess] --> B[Act $x";
        Assert.Equal(chart, PartialText.Showable(chart));
        const string formula = "The mean:\n\n$$\n\\bar{x} = \\frac{1}{n}";
        Assert.Equal(formula, PartialText.Showable(formula));
    }

    [Fact]
    public void An_answer_that_stopped_partway_keeps_no_half_drawn_diagram_or_formula()
    {
        Assert.Equal("Steps:", PartialText.Ended("Steps:\n\n```mermaid\nflowchart LR\n  A[Assess] --> B"));
        Assert.Equal("The mean:", PartialText.Ended("The mean:\n\n$$\n\\bar{x} = \\frac{1}{n}"));
        Assert.Equal("The ratio is", PartialText.Ended("The ratio is $\\frac{a}{"));
        // Code still being written is kept: every line of it already reads as code.
        Assert.Equal("Try:\n\n```python\nprint(1)", PartialText.Ended("Try:\n\n```python\nprint(1)"));
    }
}
