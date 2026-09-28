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

    /// <summary>A file in a class's Attachments folder, kept in the index with its words already read.</summary>
    static void Attach(Store store, string id, string className, string noteId, string name, string? words)
    {
        string dir = store.AttachmentsDir(className);
        File.WriteAllText(Path.Combine(dir, name), "x");
        store.AddAttachment(new Attachment(id, name, name, className, noteId, 1, "application/pdf", "2026-09-01T00:00:00Z"));
        store.SetAttachmentText(id, words);
    }

    [Fact]
    public async Task The_pipeline_writes_a_lecture_with_its_attachments_and_then_counts_them_used()
    {
        using var dir = new TempDir();
        var cfg = new Config(dir["home"], dir["pool"]) { OllamaModel = "small:1b", Classes = [new ClassDef("Calc 1")] };
        using var store = new Store(cfg.DbPath, cfg.PoolDir);
        store.Enqueue(new Meeting("l1") { Title = "Calc 1 lecture", Folder = "Calc 1", Transcript = Lines(40) });
        Attach(store, "a1", Configs.Unsorted, "l1", "my notes.pdf", "Chain rule: f(g(x))' = f'(g(x)) g'(x)");
        Attach(store, "a2", Configs.Unsorted, "l1", "IMG_3.png", null); // nothing read from it
        Meeting? seen = null;
        var p = new Pipeline(cfg, store, (_, _, _) => Task.FromResult("{}"), (m, _) =>
        {
            seen = m;
            return Task.FromResult("## Summary\nChain rule.");
        }, _ => { });
        Assert.Equal(1, await p.RunPendingAsync());
        var attached = Assert.Single(seen!.Attached);
        Assert.Equal(("a1", Attachments.OwnNotes), (attached.Id, attached.Label));
        Assert.True(store.GetAttachment("a1")!.Used);
        Assert.False(store.GetAttachment("a2")!.Used);
        Assert.False(store.AttachmentsUnused("l1"));
        Assert.Equal("Calc 1", store.GetAttachment("a1")!.ClassName); // filed, and they went with it

        // One that comes after the notes are written is not used until they're written again.
        Attach(store, "a3", "Calc 1", "l1", "Week 2 slides.pdf", "Slide: the chain rule");
        Assert.True(store.AttachmentsUnused("l1"));
        store.Requeue("l1");
        await p.RunPendingAsync();
        Assert.Equal(2, seen.Attached.Count);
        Assert.False(store.AttachmentsUnused("l1"));
    }

    [Fact]
    public async Task Notes_that_fail_to_be_written_leave_the_attachments_unused()
    {
        using var dir = new TempDir();
        var cfg = new Config(dir["home"], dir["pool"]) { OllamaModel = "small:1b", Classes = [new ClassDef("Calc 1")] };
        using var store = new Store(cfg.DbPath, cfg.PoolDir);
        store.Enqueue(new Meeting("l1") { Title = "Calc 1 lecture", Folder = "Calc 1", Transcript = Lines(40) });
        Attach(store, "a1", Configs.Unsorted, "l1", "my notes.pdf", "words");
        var p = new Pipeline(cfg, store, (_, _, _) => Task.FromResult("{}"), (_, _) => throw new InvalidOperationException("model not found"), _ => { });
        await p.RunPendingAsync();
        Assert.False(store.GetAttachment("a1")!.Used);
    }
}
