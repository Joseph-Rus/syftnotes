using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using StudyStash.Core.Ai;
using StudyStash.Core.Rich;

namespace StudyStash.Core.Tests;

/// <summary>
/// The diagram pass after the notes: its reply is read strictly, what it draws goes at the end of the right section
/// and a second pass replaces it, a pass that adds nothing (or fails, or runs out of time) leaves the notes byte for
/// byte, and automatic picks the strongest engine that already reads the lectures. Every engine is a fake.
/// </summary>
public class DiagramDesignTests
{
    const string Said = "Low blood pressure makes the JG cells release renin, and renin cleaves angiotensinogen into angiotensin I. "
        + "ACE in the lungs converts angiotensin I into angiotensin II, blood pressure rises, and that stops renin.";

    /// <summary>A timed transcript of <paramref name="lines"/> lines, 25 seconds apart.</summary>
    static string Timed(int lines) => string.Join("\n", Enumerable.Range(0, lines).Select(i => $"[{TimedText.Clock(i * 25)}] {Said}"));

    static readonly Meeting Lecture = new("lec-1") { Title = "Blood pressure", Date = "2026-09-29T09:00:00", Folder = "BIO 210", Transcript = Timed(24) };

    const string Notes = "## Summary\nHow the kidney raises blood pressure.\n\n## Key points\n- Renin starts it.\n- Angiotensin II does the work.\n\n"
        + "## Details and examples\n### Drugs\n- ACE inhibitors block it.\n\n## Questions to review\n1. What releases renin?";

    const string Chain = "flowchart LR\n  Low[\"Low blood pressure\"] --> JG[\"JG cells\"]\n  JG -->|renin| A1[\"Angiotensin I\"]\n"
        + "  A1 -->|ACE in lungs| A2[\"Angiotensin II\"]\n  A2 --> BP[\"Blood pressure rises\"]\n  BP -.->|stops renin| JG";

    static JsonObject Diagram(string title = "The renin chain", string after = "Key points", string at = "00:25", string kind = "mermaid",
        string source = Chain, string caption = "Low pressure releases renin, which leads to angiotensin II.") =>
        new() { ["title"] = title, ["after"] = after, ["at"] = at, ["kind"] = kind, ["source"] = source, ["caption"] = caption };

    static string Reply(string reason, params JsonObject[] diagrams) =>
        new JsonObject { ["reason"] = reason, ["diagrams"] = new JsonArray([.. diagrams]) }.ToJsonString();

    [Fact]
    public void The_reply_is_read_strictly_and_a_diagram_missing_anything_is_left_out()
    {
        foreach (string bad in new[] { "No diagrams needed.", """{"diagrams": []}""", """{"reason": "  ", "diagrams": []}""", """{"reason": "fine"}""" })
            Assert.True(DiagramDesign.Read(bad, Notes, Lecture.Transcript, Drawings.FlowchartsAndSvg).Malformed, bad);

        string reply = "Here you go:\n```json\n" + Reply("The chain is the lecture's point.",
            Diagram(source: "```mermaid\n" + Chain + "\n```", title: "**The renin chain**"),
            Diagram(after: "Pharmacology"),
            Diagram(kind: "svg", source: Summarize.SvgExample),
            Diagram(at: "mm:ss"),
            Diagram(at: "59:00"),
            Diagram(caption: ""),
            Diagram(kind: "pie")) + "\n```";
        var read = DiagramDesign.Read(reply, Notes, Lecture.Transcript, Drawings.Flowcharts);
        Assert.False(read.Malformed);
        Assert.Equal("The chain is the lecture's point.", read.Reason);
        var d = Assert.Single(read.Diagrams);
        Assert.Equal(new DesignedDiagram("The renin chain", "Key points", 25, NoteBlockKind.Mermaid, Chain, "Low pressure releases renin, which leads to angiotensin II."), d);
        Assert.Equal(6, read.Dropped.Count);
        Assert.Contains("isn't a heading in the notes", read.Dropped[0]);
        Assert.Contains("SVG drawing, which this engine doesn't draw", read.Dropped[1]);
        Assert.Contains("isn't a time", read.Dropped[2]);
        Assert.Contains("after the lecture ends", read.Dropped[3]);

        // A CLI engine may draw SVG; a transcript without times needs no moment, and gets none.
        var svg = DiagramDesign.Read(Reply("Spatial.", Diagram(kind: "svg", source: Summarize.SvgExample, at: "")), Notes, Lecture.Transcript, Drawings.FlowchartsAndSvg);
        Assert.Equal(NoteBlockKind.Svg, Assert.Single(svg.Diagrams).Kind);
        var plain = DiagramDesign.Read(Reply("x", Diagram(at: "whenever")), Notes, TimedText.Plain(Lecture.Transcript), Drawings.Flowcharts);
        Assert.Null(Assert.Single(plain.Diagrams).At);
    }

    static DesignedDiagram Designed(string after, string title = "The renin chain", double? at = 25) =>
        new(title, after, at, NoteBlockKind.Mermaid, Chain, "Low pressure releases renin.");

    [Fact]
    public void A_diagram_goes_at_the_end_of_its_section_and_a_second_pass_replaces_it_instead_of_stacking()
    {
        string one = DiagramDesign.Insert(Notes, [Designed("Key points")]);
        Assert.Equal("## Summary\nHow the kidney raises blood pressure.\n\n## Key points\n- Renin starts it.\n- Angiotensin II does the work.\n\n"
            + "**The renin chain**\n\n```mermaid\n%% Study Stash diagram, from 00:25\n" + Chain + "\n```\n\n*Low pressure releases renin.* (from 00:25)\n\n"
            + "## Details and examples\n### Drugs\n- ACE inhibitors block it.\n\n## Questions to review\n1. What releases renin?", one);
        Assert.Null(Summarize.DiagramProblem(NoteBlockKind.Mermaid, NoteBlocks.Find(one).Single().Text));

        // Under a subheading, before the next heading of its level; two for one section keep their order.
        string two = DiagramDesign.Insert(Notes, [Designed("Drugs", "First", null), Designed("### drugs:", "Second", null)]);
        Assert.Contains("- ACE inhibitors block it.\n\n**First**\n\n```mermaid\n%% Study Stash diagram\n", two);
        Assert.Contains("*Low pressure releases renin.*\n\n**Second**", two);
        Assert.EndsWith("*Low pressure releases renin.*\n\n## Questions to review\n1. What releases renin?", two);

        // Taking them out gives back the notes exactly; a new pass's diagrams replace the old ones.
        Assert.Equal(Notes, DiagramDesign.Strip(one));
        Assert.Equal(Notes, DiagramDesign.Strip(two));
        Assert.Equal(DiagramDesign.Insert(Notes, [Designed("Drugs")]), DiagramDesign.Insert(DiagramDesign.Strip(one), [Designed("Drugs")]));
        Assert.Same(Notes, DiagramDesign.Strip(Notes));
        Assert.Same(Notes, DiagramDesign.Insert(Notes, []));
        // The notes engine's own diagram is never taken for one of the pass's.
        string own = Notes + "\n\n```mermaid\n" + Chain + "\n```";
        Assert.Same(own, DiagramDesign.Strip(own));
    }

    [Fact]
    public async Task A_pass_that_adds_nothing_leaves_the_notes_byte_for_byte()
    {
        int asked = 0;
        Func<string, bool, Task<string>> Says(string reply) => (_, _) =>
        {
            asked++;
            return Task.FromResult(reply);
        };
        foreach (string reply in new[] { Reply("It's all discussion, with nothing to picture."), "I'd rather not.", Reply("x", Diagram(after: "Nowhere")) })
            Assert.Same(Notes, (await DiagramDesign.DesignAsync(Lecture, Notes, Drawings.Flowcharts, Says(reply))).Notes);
        Assert.Equal(3, asked);

        // Too short to matter: nobody is asked.
        var shortOne = new Meeting("s") { Transcript = Timed(3) };
        var none = await DiagramDesign.DesignAsync(shortOne, Notes, Drawings.Flowcharts, Says(Reply("x", Diagram())));
        Assert.Same(Notes, none.Notes);
        Assert.Equal(3, asked);
    }

    [Fact]
    public async Task A_broken_diagram_is_repaired_once_or_left_out_as_is_one_the_lecture_never_said()
    {
        const string Broken = "flowchart LR\n  Low[\"Low blood pressure\"] -->";
        var prompts = new List<string>();
        string reply = Reply("The chain.", Diagram(source: Broken));
        var json = new List<bool>();
        var fixedOne = await DiagramDesign.DesignAsync(Lecture, Notes, Drawings.Flowcharts, (p, j) =>
        {
            prompts.Add(p);
            json.Add(j);
            return Task.FromResult(prompts.Count == 1 ? reply : "```mermaid\n" + Chain + "\n```");
        });
        Assert.Equal([true, false], json); // the design is JSON; its repair, just the block
        Assert.Equal(2, prompts.Count);
        Assert.StartsWith("This Mermaid flowchart has a problem", prompts[1]);
        Assert.Equal(Chain, Assert.Single(fixedOne.Drawn).Source);

        var stillBroken = await DiagramDesign.DesignAsync(Lecture, Notes, Drawings.Flowcharts, (p, _) => Task.FromResult(p.StartsWith("This Mermaid", StringComparison.Ordinal) ? Broken : reply));
        Assert.Same(Notes, stillBroken.Notes);
        Assert.Contains("even after a repair", Assert.Single(stillBroken.Dropped));

        string invented = "flowchart TD\n  K[\"Krebs cycle\"] --> G[\"Glycolysis pyruvate\"] --> E[\"Electron transport chain\"] --> M[\"Mitochondrial membrane\"]";
        string scattered = "flowchart LR\n  A[\"Renin\"] --> B[\"JG cells\"]\n  C[\"Angiotensin I\"] --> D[\"Lungs\"]\n  E[\"Blood pressure\"] --> F[\"Renin\"]";
        string crowded = "flowchart LR\n" + string.Join("\n", Enumerable.Range(0, DiagramDesign.MaxNodes).Select(i => $"  N{i}[\"Renin {i}\"] --> N{i + 1}[\"Renin {i + 1}\"]"));
        foreach (string source in new[] { invented, scattered, crowded })
        {
            var left = await DiagramDesign.DesignAsync(Lecture, Notes, Drawings.Flowcharts, (_, _) => Task.FromResult(Reply("x", Diagram(source: source))));
            Assert.Same(Notes, left.Notes);
            Assert.Single(left.Dropped);
        }
    }

    [Fact]
    public void Every_example_the_brief_shows_parses_and_lays_out_well_in_the_notes()
    {
        string prompt = DiagramDesign.Prompt(Lecture, Notes, Drawings.FlowchartsAndSvg, 3);
        foreach (string example in DiagramDesign.Examples)
        {
            Assert.Contains("```mermaid\n" + example + "\n```", prompt);
            Assert.Empty(DiagramLint.Problems(Flowchart.Parse(example)));
        }
        Assert.Equal([ChartForm.Flowchart, ChartForm.Flowchart, ChartForm.State, ChartForm.Sequence, ChartForm.Timeline, ChartForm.Mindmap],
            DiagramDesign.Examples.Select(e => Flowchart.Parse(e).Form));
        Assert.Contains("at most 3 diagrams", prompt);
        Assert.Equal([0, 1, 2, 3, 4], new[] { 2000, 9000, 20_000, 39_000, 60_000 }.Select(n => DiagramDesign.Cap(new string('a', n))));
    }

    [Fact]
    public void How_a_diagram_would_look_is_checked_at_the_notes_width()
    {
        // Fine: a small cycle, and a big chart in groups.
        Assert.Empty(DiagramLint.Problems(Flowchart.Parse(Summarize.MermaidExample)));
        Assert.Empty(DiagramLint.Problems(Flowchart.Parse(DiagramDesign.GroupedExample)));
        // Twenty boxes in a row with no groups; a box that's a sentence; a sequence with too many people.
        string row = "flowchart LR\n" + string.Join("\n", Enumerable.Range(0, 19).Select(i => $"  N{i}[\"Step {i}\"] --> N{i + 1}[\"Step {i + 1}\"]"));
        Assert.Contains(DiagramLint.Problems(Flowchart.Parse(row)), p => p.Contains("20 boxes and no groups"));
        Assert.Contains(DiagramLint.Problems(Flowchart.Parse("flowchart TD\n  A[\"The renin is released by the JG cells when pressure falls\"] --> B[\"Renin\"] --> C[\"ACE\"]")),
            p => p.Contains("is a sentence, not a box"));
        string crowd = "sequenceDiagram\n" + string.Join("\n", Enumerable.Range(0, 7).Select(i => $"  P{i}->>P{i + 1}: pass it on"));
        Assert.Contains(DiagramLint.Problems(Flowchart.Parse(crowd)), p => p.Contains("8 participants"));
    }

    [Fact]
    public async Task A_diagram_that_would_look_wrong_goes_back_once_and_only_a_better_redesign_replaces_it()
    {
        // Sixteen steps in a row, no groups: true to the lecture, but it would look wrong.
        string row = "flowchart LR\n" + string.Join("\n", Enumerable.Range(0, 15).Select(i => $"  N{i}[\"Renin {i}\"] --> N{i + 1}[\"Renin {i + 1}\"]"));
        string grouped = "flowchart LR\n" + string.Join("\n", Enumerable.Range(0, 4).Select(g =>
            $"  subgraph G{g} [\"Renin phase {g}\"]\n" + string.Join("\n", Enumerable.Range(g * 4, 3).Select(i => $"    N{i}[\"Renin {i}\"] --> N{i + 1}[\"Renin {i + 1}\"]")) + "\n  end"))
            + "\n" + string.Join("\n", Enumerable.Range(0, 3).Select(g => $"  N{g * 4 + 3} --> N{g * 4 + 4}"));
        var prompts = new List<string>();
        Func<string, bool, Task<string>> Answers(string redesign) => (p, _) =>
        {
            prompts.Add(p);
            return Task.FromResult(prompts.Count == 1 ? Reply("The chain.", Diagram(source: row)) : Reply("Grouped.", Diagram(source: redesign)));
        };
        var better = await DiagramDesign.DesignAsync(Lecture, Notes, Drawings.Flowcharts, Answers(grouped));
        Assert.Equal(2, prompts.Count);
        Assert.StartsWith("You designed this diagram", prompts[1]);
        Assert.Contains("16 boxes and no groups", prompts[1]);
        Assert.Contains("\"after\": \"Key points\", \"at\": \"00:25\"", prompts[1]);
        Assert.Equal(1, better.Revised);
        Assert.Equal(grouped, Assert.Single(better.Drawn).Source);
        Assert.Equal("Key points", better.Drawn[0].After);
        Assert.Empty(better.Problems);

        // A redesign that's no better, or one that doesn't draw, leaves the draft as it was.
        foreach (string worse in new[] { row, "flowchart LR\n  A[\"Renin\"] -->" })
        {
            prompts.Clear();
            var kept = await DiagramDesign.DesignAsync(Lecture, Notes, Drawings.Flowcharts, Answers(worse));
            Assert.Equal(0, kept.Revised);
            Assert.Equal(row, Assert.Single(kept.Drawn).Source);
            Assert.NotEmpty(kept.Problems);
        }
        // A revision that fails keeps the draft; with no time left there's none.
        prompts.Clear();
        var failed = await DiagramDesign.DesignAsync(Lecture, Notes, Drawings.Flowcharts, (p, _) =>
        {
            prompts.Add(p);
            return prompts.Count == 1 ? Task.FromResult(Reply("The chain.", Diagram(source: row))) : throw new InvalidOperationException("Claude: usage limit");
        });
        Assert.Equal(row, Assert.Single(failed.Drawn).Source);
        prompts.Clear();
        await DiagramDesign.DesignAsync(Lecture, Notes, Drawings.Flowcharts, Answers(grouped), budget: TimeSpan.FromSeconds(30));
        Assert.Single(prompts);
    }

    // --- through AiJobs: which engine, and never a failure of the notes -----------------------------------------------

    /// <summary>An engine that answers each prompt its own way, and keeps every request it was sent.</summary>
    sealed class Answering(string id, Func<string, CancellationToken, Task<string>> answer) : AiProvider
    {
        public override string Id { get; } = id;
        public override string Name => Id == "claude" ? "Claude" : Id;
        public override string Binary => Id;
        public override string Site => "https://example.test/" + Id;
        public override bool Available() => true;
        public List<AiRequest> Requests { get; } = [];
        public override List<string> Command(AiRequest req, bool stream) => [];
        public override IEnumerable<AiEvent> Parse(string line) => [];

        public override async IAsyncEnumerable<AiEvent> RunAsync(AiRequest req, bool stream = true, [EnumeratorCancellation] CancellationToken ct = default)
        {
            lock (Requests) Requests.Add(req);
            AiEvent said;
            try
            {
                said = new AiEvent("final", await answer(req.Prompt, ct));
            }
            catch (InvalidOperationException e)
            {
                said = AiEvent.Error(e.Message);
            }
            yield return said;
        }
    }

    static bool Designing(string prompt) => prompt.StartsWith("You design the diagrams", StringComparison.Ordinal);

    [Fact]
    public async Task Notes_leave_diagrams_to_the_pass_which_adds_them_and_never_fails_the_notes()
    {
        using var dir = new TempDir();
        var cfg = new Config(dir["home"], dir["pool"]) { OllamaEnabled = true, OllamaModel = "qwen3:8b" };
        new AiSettings { Provider = "claude", Fallback = false }.Save(cfg.Home); // diagrams: automatic
        Func<string, CancellationToken, Task<string>> design = (_, _) => Task.FromResult(Reply("The chain.", Diagram()));
        var claude = new Answering("claude", (p, ct) => Designing(p) ? design(p, ct) : Task.FromResult(Notes));
        var log = new List<string>();
        var ai = new AiJobs(cfg.Home)
        {
            Providers = _ => claude, Checks = new FakeChecks().Installed("claude").Build(), Log = log.Add, DiagramTimeout = TimeSpan.FromSeconds(2),
        };

        string drawn = await ai.SummarizeAsync(Lecture, cfg);
        Assert.Equal(DiagramDesign.Insert(Notes, [new DesignedDiagram("The renin chain", "Key points", 25, NoteBlockKind.Mermaid, Chain,
            "Low pressure releases renin, which leads to angiotensin II.")]), drawn);
        Assert.Contains("Diagrams: draw none", claude.Requests[0].Prompt);
        Assert.DoesNotContain(Summarize.MermaidExample, claude.Requests[0].Prompt);
        Assert.Equal(("", ""), (claude.Requests[0].Model, claude.Requests[0].Effort));
        Assert.True(Designing(claude.Requests[1].Prompt));
        Assert.Equal(("opus", "high"), (claude.Requests[1].Model, claude.Requests[1].Effort));
        Assert.Contains("[diagrams] 'Blood pressure': Claude opus drew 1 (The renin chain)", log[^1]);

        // Whatever goes wrong in the pass, the notes are exactly as written.
        design = (_, _) => throw new InvalidOperationException("unknown option '--effort'");
        Assert.Equal(Notes, await ai.SummarizeAsync(Lecture, cfg));
        Assert.Equal(("", ""), (claude.Requests[^1].Model, claude.Requests[^1].Effort)); // asked again as set up
        design = (_, ct) => Task.Delay(Timeout.Infinite, ct).ContinueWith(_ => "", TaskScheduler.Default);
        Assert.Equal(Notes, await ai.SummarizeAsync(Lecture, cfg));
        Assert.Contains("took longer than", log[^1]);
        design = (_, _) => throw new InvalidOperationException("Claude usage limit reached, resets at 3pm");
        Assert.Equal(Notes, await ai.SummarizeAsync(Lecture, cfg));
        Assert.True(AiSettings.Load(cfg.Home).Limits.ContainsKey("claude"));

        // Same as notes: the notes engine draws its own, as before; off: no diagrams, and no pass.
        int before = claude.Requests.Count;
        new AiSettings { Provider = "claude", Fallback = false, Diagrams = "notes" }.Save(cfg.Home);
        await ai.SummarizeAsync(Lecture, cfg);
        new AiSettings { Provider = "claude", Fallback = false, Diagrams = "off" }.Save(cfg.Home);
        await ai.SummarizeAsync(Lecture, cfg);
        Assert.Equal(before + 2, claude.Requests.Count);
        Assert.Contains("```mermaid\n" + Summarize.MermaidExample, claude.Requests[before].Prompt);
        Assert.Contains("Diagrams: draw none", claude.Requests[before + 1].Prompt);
    }

    [Fact]
    public async Task Automatic_is_the_strongest_engine_that_already_reads_the_lectures_else_the_biggest_local_model()
    {
        var cfg = new Config("/lib", "/lib") { OllamaModel = "qwen3:8b" };
        var checks = new FakeChecks().Installed("claude", "codex", "agy").HasEnv("OPENAI_API_KEY", "sk").HasEnv("GEMINI_API_KEY", "g")
            .Ollama(models: [("glm-5:cloud", 0), ("qwen3:30b", 18.6), ("big-cloud", 40), ("qwen3:8b", 5.2)]);
        async Task<DiagramPick?> Pick(AiSettings s, string notesEngine = "ollama") => await DiagramEngines.PickAsync(s, cfg, checks.Build(), notesEngine);

        Assert.Equal(new DiagramPick("claude", "opus", "high", Drawings.FlowchartsAndSvg), await Pick(new AiSettings(), "claude"));
        // Claude Code is here, but nothing sends it lectures: automatic keeps to the engine that answers questions.
        var askCodex = new AiSettings { ByJob = { ["ask"] = new AiChoice("codex") } };
        Assert.Equal(new DiagramPick("codex", "", "high", Drawings.FlowchartsAndSvg), await Pick(askCodex));
        Assert.Equal(new DiagramPick("claude", "opus", "high", Drawings.FlowchartsAndSvg), await Pick(askCodex, "claude"));
        // Only Ollama reads them: its biggest model that runs here, never a cloud one.
        Assert.Equal(new DiagramPick("ollama", "qwen3:30b", "", Drawings.Flowcharts), await Pick(new AiSettings()));
        // Over its usage limit, Claude is passed over.
        var limited = new AiSettings { Limits = { ["claude"] = DateTime.Now.AddHours(2).ToString("o") } };
        Assert.Equal("ollama", (await Pick(limited, "claude"))?.Engine);
        // Nothing else to be had: the notes engine itself.
        Assert.Equal(new DiagramPick("ollama", "qwen3:8b", "", Drawings.Flowcharts),
            await DiagramEngines.PickAsync(new AiSettings(), cfg, new FakeChecks().Build(), "ollama"));

        // A pick of its own is used while it looks usable; otherwise the fallback to Ollama decides.
        Assert.Equal(new DiagramPick("gemini", "gemini-3.1-pro-high", "", Drawings.FlowchartsAndSvg), await Pick(new AiSettings { Diagrams = "gemini" }));
        var gone = new FakeChecks().Ollama(models: [("qwen3:8b", 5.2)]).Build();
        Assert.Equal("ollama", (await DiagramEngines.PickAsync(new AiSettings { Diagrams = "codex" }, cfg, gone, "ollama"))?.Engine);
        Assert.Null(await DiagramEngines.PickAsync(new AiSettings { Diagrams = "codex", Fallback = false }, cfg, gone, "ollama"));
        Assert.Null(await Pick(new AiSettings { Diagrams = "off" }, "claude"));
        Assert.Null(await Pick(new AiSettings { Diagrams = "notes" }, "claude"));
    }
}
