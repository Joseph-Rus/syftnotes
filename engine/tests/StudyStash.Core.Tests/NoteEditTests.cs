using StudyStash.Core.Rich;

namespace StudyStash.Core.Tests;

/// <summary>A lecture's notes as a student edits them by hand: each diagram is one line, and comes back exactly as it
/// was wherever that line ends up.</summary>
public class NoteEditTests
{
    const string Chain = "```mermaid\n%% Study Stash diagram, from 0:25\nflowchart LR\n  A --> B\n```";
    const string Plot = "```plot\nf(x) = x^2\n```";
    const string Drawing = "    ```svg\n    <svg viewBox=\"0 0 10 10\"></svg>\n    ```";

    // The last line reads like a diagram's line and isn't one: it keeps its number to itself.
    static readonly string Markdown = "## Key points\n- Renin starts it.\n\n**The renin chain**\n\n" + Chain + "\n\n*Low pressure releases renin.*\n\n"
        + "## A graph\n\n" + Plot + "\n\n  - In a bullet:\n\n" + Drawing + "\n\n$$E = mc^2$$\n\n```python\nprint('code')\n```\n\n[Diagram 1]";

    [Fact]
    public void Each_diagram_is_a_line_and_comes_back_as_it_was_where_its_line_is()
    {
        var edit = NoteEdit.Begin(Markdown);

        Assert.True(edit.HasDiagrams);
        Assert.Equal("## Key points\n- Renin starts it.\n\n**The renin chain**\n\n[Diagram 2]\n\n*Low pressure releases renin.*\n\n"
            + "## A graph\n\n[Plot 3]\n\n  - In a bullet:\n\n    [Drawing 4]\n\n$$E = mc^2$$\n\n```python\nprint('code')\n```\n\n[Diagram 1]", edit.Text);
        Assert.Equal(Markdown, edit.End(edit.Text));

        // The chain moved to the top, the graph deleted, a word fixed, typed with Windows returns.
        string typed = "[Diagram 2]\r\n\r\n## Key points\r\n- Renin begins it.\r\n\r\n  - In a bullet:\r\n\r\n    [Drawing 4]\r\n\r\n[Diagram 1]";
        Assert.Equal(Chain + "\n\n## Key points\n- Renin begins it.\n\n  - In a bullet:\n\n" + Drawing + "\n\n[Diagram 1]", edit.End(typed));
    }

    [Fact]
    public void Notes_without_diagrams_are_their_own_text()
    {
        var edit = NoteEdit.Begin("## Summary\n\nJust words.");

        Assert.False(edit.HasDiagrams);
        Assert.Equal("## Summary\n\nJust words.", edit.Text);
        Assert.Equal("## Summary\n\nJust words, and more.", edit.End("## Summary\n\nJust words, and more."));
    }
}
