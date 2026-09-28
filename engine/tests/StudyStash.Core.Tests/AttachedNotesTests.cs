namespace StudyStash.Core.Tests;

/// <summary>What a student attached goes with the transcript when a lecture's notes are written: labelled, and never
/// crowding the transcript out.</summary>
public class AttachedNotesTests
{
    const string Line = "The derivative measures how fast a function changes at a point.\n";

    static string Lines(int n) => string.Concat(Enumerable.Repeat(Line, n));

    static Config CfgFor(TempDir dir) => new(dir["home"], dir["pool"]) { OllamaModel = "small:1b", SummaryModel = "big:35b" };

    static readonly AttachedText Handwritten = new("a1", Attachments.OwnNotes, "iPad notes.pdf", "Power rule: d/dx x^n = n x^(n-1)");
    static readonly AttachedText Deck = new("a2", Attachments.Slides, "Week 3.pptx", "Slide 1: Limits and continuity");

    [Fact]
    public async Task A_short_lecture_is_written_with_the_attachments_labelled()
    {
        using var dir = new TempDir();
        var prompts = new List<string>();
        var m = new Meeting("1") { Title = "Calc 3", Transcript = Lines(40), Attached = [Handwritten, Deck] };
        await Summarize.SummarizeTranscriptAsync(m, CfgFor(dir), (_, _, prompt, _) =>
        {
            prompts.Add(prompt);
            return Task.FromResult("## Overview\nok");
        }, (_, _) => Task.FromResult<int?>(32768));
        string p = Assert.Single(prompts);
        Assert.Contains("The student's own notes (iPad notes.pdf):\nPower rule", p);
        Assert.Contains("Slides (Week 3.pptx):\nSlide 1: Limits", p);
        Assert.Contains("write the notes from the lecture itself", p);
        // Before the transcript, which stays last.
        Assert.True(p.IndexOf("Slides (Week 3.pptx)", StringComparison.Ordinal) < p.IndexOf("Transcript:\n", StringComparison.Ordinal));
        Assert.EndsWith(Lines(40).Trim(), p);
    }

    [Fact]
    public async Task Without_attachments_the_prompt_is_as_it_always_was()
    {
        using var dir = new TempDir();
        string? prompt = null;
        var m = new Meeting("1") { Title = "Calc 3", Transcript = Lines(40) };
        await Summarize.SummarizeTranscriptAsync(m, CfgFor(dir), (_, _, p, _) =>
        {
            prompt = p;
            return Task.FromResult("ok");
        }, (_, _) => Task.FromResult<int?>(32768));
        Assert.Equal(Summarize.WholePrompt(m, Lines(40).Trim()), prompt);
        Assert.DoesNotContain("The student attached", prompt);
        // An attachment with no words is the same as none.
        m.Attached = [new AttachedText("a", Attachments.Slides, "blank.pdf", "  ")];
        Assert.Equal(Summarize.WholePrompt(m, "T"), Summarize.WholePrompt(m, "T", attached: Attachments.Context(m.Attached)));
    }

    [Fact]
    public async Task A_long_lecture_puts_them_in_the_merge_only_and_never_past_a_third_of_the_room()
    {
        using var dir = new TempDir();
        var cfg = CfgFor(dir);
        cfg.SummaryMaxContext = 8192;
        int budget = Summarize.TranscriptBudget(8192);
        var prompts = new List<string>();
        var huge = new AttachedText("a", Attachments.Handout, "reader.pdf", new string('§', 100_000));
        var m = new Meeting("1") { Title = "Long", Transcript = Lines(budget / Line.Length * 3), Attached = [huge] };
        await Summarize.SummarizeTranscriptAsync(m, cfg, (_, _, prompt, _) =>
        {
            prompts.Add(prompt);
            return Task.FromResult(prompt.Contains("Merge them into one set") ? "## Overview\nmerged" : "- notes");
        }, (_, _) => Task.FromResult<int?>(null));
        Assert.All(prompts.Where(p => p.Contains("Transcript part")), p => Assert.DoesNotContain("reader.pdf", p));
        Assert.Contains("Handout (reader.pdf)", prompts[^1]);
        Assert.True(prompts[^1].Count(c => c == '§') <= budget / 3);
    }

    [Fact]
    public void A_copy_with_other_notes_keeps_the_attachments()
    {
        var m = new Meeting("1") { Attached = [Deck] };
        Assert.Equal([Deck], m.WithNotes("new").Attached);
    }
}
