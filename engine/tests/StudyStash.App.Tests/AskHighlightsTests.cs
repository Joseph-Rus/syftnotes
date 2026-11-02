using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StudyStash.App.Controls;
using StudyStash.App.ViewModels;
using StudyStash.Core.Ai;

namespace StudyStash.App.Tests;

/// <summary>An answer about a lecture points at the words of its notes it was built on: the quotes are found in the notes
/// however the AI copied them (spacing, case, curly quotes, Markdown's stars and bullets, a word or two off), one that
/// isn't there is dropped, the notes highlight the ones of the answer being read, and they clear with the answer.</summary>
public class AskHighlightsTests
{
    const string Notes = "## Three kinds of vessel\n- **Arteries** carry blood away from the heart under high pressure; thick, elastic walls.\n- *Capillaries* are one cell thick: this is where gases and nutrients are exchanged.\n- Veins carry blood back at low pressure and have valves to stop backflow.";

    [Theory]
    [InlineData("Arteries carry blood away from the heart under high pressure")]
    [InlineData("  arteries   CARRY blood\naway from the heart ")]
    [InlineData("- **Arteries** carry blood away from the heart under high pressure")]
    [InlineData("1. Arteries carry blood away from the heart under high pressure;")]
    [InlineData("“Capillaries are one cell thick”")]
    [InlineData("Capillaries are one cell thick: this is where gases and nutrients are exchanged")]
    public void A_quote_is_found_however_the_AI_copied_it(string quote) => Assert.True(QuoteFinder.InNotes(Notes, quote));

    [Fact]
    public void A_quote_that_is_nearly_there_is_found_and_one_that_is_not_is_not()
    {
        // A word changed or left out still points at the bullet; words from nowhere don't point at anything.
        Assert.True(QuoteFinder.InNotes(Notes, "Veins carry blood back at a low pressure and have valves to prevent backflow"));
        Assert.True(QuoteFinder.InNotes(Notes, "Arteries take blood away from the heart under high pressure"));
        Assert.False(QuoteFinder.InNotes(Notes, "Mitochondria are the powerhouse of the cell"));
        Assert.False(QuoteFinder.InNotes(Notes, "blood rushes through"));
        Assert.False(QuoteFinder.InNotes("", "Arteries carry blood away from the heart"));
    }

    [Fact]
    public void A_quote_is_found_where_the_page_has_it_not_in_the_Markdown()
    {
        string page = "Arteries carry blood away from the heart under high pressure; thick, elastic walls.";
        var m = QuoteFinder.Find(page, "**Arteries** carry blood  away from the heart")!.Value;
        Assert.Equal("Arteries carry blood away from the heart", page.Substring(m.Start, m.Length));
        Assert.Equal(1, m.Score);
        var near = QuoteFinder.Find(page, "Arteries take blood away from the heart under high pressure")!.Value;
        Assert.StartsWith("Arteries carry blood away", page.Substring(near.Start, near.Length));
        Assert.EndsWith("high pressure", page.Substring(near.Start, near.Length));
    }

    static async Task<(AiAskModel Model, FakeAiLibrary Lib)> Asked(params string[] quotes)
    {
        var lib = new FakeAiLibrary { Overview = AiTestData.MixedOverview() };
        var model = new AiAskModel(lib) { LectureId = "lec-vessels" };
        await model.Load();
        model.Highlights.SetNotes(Notes);
        lib.OnAsk = r => new AskReply("Arteries carry blood away.", [new AskSource("lec-vessels", "Vessels", null, null, null, "Three kinds of vessel")], "claude", "Claude")
        {
            Quotes = [.. quotes],
        };
        model.Question = "Which vessels carry blood away?";
        await model.AskCommand.ExecuteAsync(null);
        return (model, lib);
    }

    [AvaloniaFact]
    public async Task The_answers_quotes_the_notes_hold_are_chips_and_highlights_until_the_answer_goes()
    {
        var (model, _) = await Asked("Arteries carry blood away from the heart under high pressure", "Mitochondria are the powerhouse of the cell",
            "capillaries are one cell thick");

        // The one the notes don't hold is dropped, silently.
        var turn = model.Turns[0];
        Assert.Equal(["Arteries carry blood away from the heart under high pressure", "capillaries are one cell thick"], turn.Shown);
        Assert.Equal(["Arteries carry blood away from the…", "capillaries are one cell thick"], turn.Chips.Select(c => c.Text));
        Assert.Equal(turn.Shown, model.Highlights.Quotes);

        // The pointer on a chip: its highlight goes stronger, and back.
        turn.Chips[1].Hover(true);
        Assert.Equal(1, model.Highlights.Strong);
        turn.Chips[1].Hover(false);
        Assert.Equal(-1, model.Highlights.Strong);

        // Closed, the notes are clear; shown again, they're back; a new chat clears them for good.
        model.CloseAnswerCommand.Execute(null);
        Assert.Empty(model.Highlights.Quotes);
        model.ShowChatCommand.Execute(null);
        Assert.Equal(2, model.Highlights.Quotes.Count);
        model.NewChatCommand.Execute(null);
        Assert.Empty(model.Highlights.Quotes);
    }

    [AvaloniaFact]
    public async Task A_quote_with_a_formula_reads_plainly_and_one_that_is_only_a_formula_has_a_highlight_but_no_chip()
    {
        const string formulaNotes = "## Attention\nScores are used to weight the values: $\\text{Attention}(Q,K,V)=\\text{softmax}\\left(\\frac{QK^T}{\\sqrt{d_k}}\\right)V$ and nothing else.\n\n$$\\frac{QK^T}{\\sqrt{d_k}} = \\sum_{i=1}^{n} x_i$$";
        var lib = new FakeAiLibrary { Overview = AiTestData.MixedOverview() };
        var model = new AiAskModel(lib) { LectureId = "lec-vessels" };
        await model.Load();
        model.Highlights.SetNotes(formulaNotes);
        lib.OnAsk = r => new AskReply("Scores weight the values.", [new AskSource("lec-vessels", "Vessels", null, null, null, "Attention")], "claude", "Claude")
        {
            Quotes = ["used to weight the values: $\\text{Attention}(Q,K,V)=\\text{softmax}\\left(\\frac{QK^T}{\\sqrt{d_k}}\\right)V$", "$$\\frac{QK^T}{\\sqrt{d_k}} = \\sum_{i=1}^{n} x_i$$"],
        };
        model.Question = "How does attention work?";
        await model.AskCommand.ExecuteAsync(null);

        var turn = model.Turns[0];
        // Both are still found in the notes and highlighted; only the readable one is a chip, and its words have no LaTeX.
        Assert.Equal(2, turn.Shown.Count);
        var chip = Assert.Single(turn.Chips);
        Assert.Equal("used to weight the values: Attention(Q,K,V)=softmax((QKT)/(√(dk)))V", chip.Full);
        Assert.DoesNotContain('\\', chip.Text);
        Assert.DoesNotContain('$', chip.Text);
    }

    [AvaloniaFact]
    public async Task Quotes_wait_for_the_notes_and_follow_them()
    {
        var (model, _) = await Asked("capillaries are one cell thick");
        // The notes change under the answer (a rewrite): what they no longer hold goes from the chips and the page.
        model.Highlights.SetNotes("## Summary\nNothing about vessels here.");
        Assert.Empty(model.Turns[0].Chips);
        Assert.Empty(model.Highlights.Quotes);
        model.Highlights.SetNotes(Notes);
        Assert.Single(model.Turns[0].Chips);
        Assert.Single(model.Highlights.Quotes);
    }

    [AvaloniaFact]
    public async Task The_notes_draw_the_quotes_and_keep_them_when_they_are_built_again()
    {
        var (model, _) = await Asked("Arteries carry blood away from the heart under high pressure", "capillaries are one cell thick");
        var view = new NoteView { Markdown = Notes, Highlights = model.Highlights };
        var window = new Window { Width = 700, Height = 500, Content = new ScrollViewer { Content = view } };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        string[] Marked() => [.. view.GetVisualDescendants().OfType<NoteText>().SelectMany(t => t.Quoted.Select(q => Words(t).Substring(q.Start, q.Length)))];
        Assert.Equal(["Arteries carry blood away from the heart under high pressure", "Capillaries are one cell thick"], Marked());

        // The notes grow (a diagram arrives after them): built again, still marked. Another answer, other words.
        view.Markdown = Notes + "\n\n- A new line.";
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(2, Marked().Length);
        model.Highlights.Show(["Veins carry blood back at low pressure"]);
        Assert.Equal(["Veins carry blood back at low pressure"], Marked());
        model.Highlights.Show([]);
        Assert.Empty(Marked());
        window.Close();
    }

    static string Words(NoteText t) => t.Inlines is { Count: > 0 } inlines ? inlines.Text ?? "" : t.Text ?? "";
}
