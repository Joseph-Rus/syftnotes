namespace StudyStash.Core.Tests;

/// <summary>The lecture in a line, as lists show it under a lecture's title.</summary>
public class FirstSentenceTests
{
    [Fact]
    public void Notes_brought_in_from_another_app_skip_its_front_matter()
    {
        const string notes = "---\ngranola_id: f0cde2a0-de73\ntitle: 'Computer Architecture'\n---\n\n# MIPS procedures\n\nProcedures save their registers on the stack. Then they return.\n";
        Assert.Equal("Procedures save their registers on the stack.", LibraryReader.FirstSentence(notes));
    }

    [Fact]
    public void A_rule_further_down_is_left_alone()
    {
        const string notes = "## Summary\nLoops repeat work. More here.\n\n---\n\nAfter the rule.";
        Assert.Equal("Loops repeat work.", LibraryReader.FirstSentence(notes));
    }
}
