using System.Runtime.CompilerServices;
using StudyStash.Core.Ai;
using StudyStash.Core.Rich;

namespace StudyStash.Core.Tests;

/// <summary>
/// The notes ask for formulas in LaTeX and a diagram where one helps: flowcharts from every engine, SVG drawings too
/// from the CLI engines; the prompts' own examples draw; a broken diagram gets one repair; code, formulas and
/// diagrams never read as a model stuck in a loop. Every engine here is a fake that keeps the prompts it was sent.
/// </summary>
public class NotesDrawingTests
{
    static readonly Meeting Lecture = new("lec-1")
    {
        Title = "The cardiac cycle", Date = "2026-09-23T09:00:00", Folder = "BIO 110",
        Transcript = string.Concat(Enumerable.Repeat("Dr. Okafor walked through the cardiac cycle: systole, diastole, and why the valves close. ", 30)),
    };

    /// <summary>An engine that answers every prompt with <paramref name="answer"/>'s reply and keeps what it was asked.</summary>
    sealed class Recording(string id, Func<string, string> answer) : AiProvider
    {
        public override string Id { get; } = id;
        public override string Name => Id;
        public override string Binary => Id;
        public override string Site => "https://example.test/" + Id;
        public override bool Available() => true;
        public List<string> Prompts { get; } = [];
        public override List<string> Command(AiRequest req, bool stream) => [];
        public override IEnumerable<AiEvent> Parse(string line) => [];

        public override async IAsyncEnumerable<AiEvent> RunAsync(AiRequest req, bool stream = true,
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            lock (Prompts) Prompts.Add(req.Prompt);
            await Task.Yield();
            yield return new AiEvent("final", answer(req.Prompt));
        }
    }

    static bool AsksForSvg(string prompt) => prompt.Contains("```svg\n" + Summarize.SvgExample + "\n```");

    [Fact]
    public void Whole_and_merge_prompts_ask_for_latex_and_a_diagram_where_one_helps()
    {
        string[] flowcharts = [Summarize.WholePrompt(Lecture, "T"), Summarize.MergePrompt(Lecture, ["a", "b"])];
        string[] svg = [Summarize.WholePrompt(Lecture, "T", Drawings.FlowchartsAndSvg), Summarize.MergePrompt(Lecture, ["a", "b"], Drawings.FlowchartsAndSvg)];
        foreach (string p in flowcharts.Concat(svg))
        {
            Assert.Contains("write every formula, equation, unit conversion and calculation in LaTeX", p);
            Assert.Contains(Summarize.DoseExample, p);
            Assert.Contains("```mermaid\n" + Summarize.MermaidExample + "\n```", p);
            Assert.Contains("follow it with one sentence that says the same in words", p);
            Assert.Contains("none for a lecture of facts or discussion", p);
            Assert.Contains("- Write every formula in LaTeX ($...$ or $$...$$).", p);
            Assert.Contains("formulas and calculations, code, and demonstrations", p);
            Assert.DoesNotContain("\r", p);
        }
        foreach (string p in flowcharts)
        {
            Assert.Contains("at most two", p);
            Assert.False(AsksForSvg(p));
            Assert.DoesNotContain("#D93025", p);
            Assert.DoesNotContain("forces on a block", p);
        }
        foreach (string p in svg)
        {
            Assert.Contains("at most three", p);
            Assert.True(AsksForSvg(p));
            Assert.Contains("Colours only from: #1D1D1F (lines and text)", p);
            Assert.Contains("No scripts, images, links, fonts, CSS or foreignObject.", p);
        }
    }

    [Fact]
    public void Part_and_condense_prompts_get_no_diagram_brief_but_the_rules_keep_what_they_are_given()
    {
        foreach (string p in new[] { Summarize.PartPrompt(Lecture, "PART", 1, 2), Summarize.CondensePrompt(Lecture, ["a", "b"]) })
        {
            Assert.DoesNotContain(Summarize.MermaidExample, p);
            Assert.DoesNotContain("Diagrams:", p);
            Assert.Contains("- Write every formula in LaTeX ($...$ or $$...$$).", p);
            Assert.Contains("- Keep any formula and any ```mermaid or ```svg block you are given exactly as it is.", p);
        }
    }

    [Fact]
    public void The_prompts_flowchart_parses_and_draws_as_a_ring_coloured_by_what_colour_means()
    {
        var chart = Flowchart.Parse(Summarize.MermaidExample);
        Assert.Equal(SceneKind.Ring, DiagramLayout.Kind(chart));
        Assert.Equal(6, chart.Nodes.Count);
        Assert.Equal([Tone.Blue, Tone.Blue, Tone.None, Tone.Red, Tone.Red, Tone.None], chart.Nodes.Select(n => n.Tone));
        Assert.Equal(["tricuspid valve", "pulmonary valve", "mitral valve", "aortic valve"], chart.Edges.Select(e => e.Label).OfType<string>());
        var scene = DiagramLayout.Lay(chart, (text, size, bold) => text.Length * size * 0.55);
        Assert.Equal(SceneKind.Ring, scene.Kind);
        Assert.Equal(6, scene.Edges.Count);
    }

    [Fact]
    public void The_prompts_drawing_is_safe_as_written_and_keeps_to_its_own_brief()
    {
        string svg = Summarize.SvgExample;
        Assert.True(svg.Split('\n').Length <= 14);
        var r = SafeSvg.Clean(svg);
        Assert.Null(r.Problem);
        Assert.Equal((640, 280), (r.Width, r.Height));
        Assert.Equal("Forces on a block on a slope", r.Title);
        Assert.Equal(["θ", "weight mg", "normal force N", "friction f"], r.Texts);
        Assert.DoesNotContain("width=\"640\"", svg);
        var colours = System.Text.RegularExpressions.Regex.Matches(svg, "(?:fill|stroke)=\"(#[0-9A-Fa-f]{6})\"").Select(m => m.Groups[1].Value.ToUpperInvariant()).ToHashSet();
        var palette = new[] { SvgPalette.Written.Ink, SvgPalette.Written.Secondary, SvgPalette.Written.LightLines, SvgPalette.Written.Paper }
            .Concat(SvgPalette.Written.Tones.Values.SelectMany(t => new[] { t.Stroke, t.Fill })).ToHashSet();
        Assert.Subset(palette, colours);
        foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(svg, "font-size=\"(\\d+)\""))
            Assert.InRange(int.Parse(m.Groups[1].Value), 13, 15);
    }

    [Fact]
    public async Task Ollama_is_asked_for_flowcharts_and_the_cli_engines_may_draw_svg_too()
    {
        using var dir = new TempDir();
        var cfg = new Config(dir["home"], dir["pool"]) { OllamaEnabled = true, OllamaModel = "qwen3:8b" };
        var ollama = new Recording("ollama", _ => "## Summary\nThe heart fills, then pumps.");
        var claude = new Recording("claude", _ => "## Summary\nThe heart fills, then pumps.");
        var ai = new AiJobs(cfg.Home) { Providers = id => id == "claude" ? claude : ollama, Checks = new FakeChecks().Installed("claude").Build() };
        new AiSettings { Diagrams = "notes" }.Save(cfg.Home); // the notes engine draws its own (no diagram pass)

        Assert.Equal("## Summary\nThe heart fills, then pumps.", await ai.SummarizeAsync(Lecture, cfg)); // notes on Ollama
        await ai.WriteNotesAsync(Lecture, cfg, "ollama", null, CancellationToken.None);
        Assert.Equal(2, ollama.Prompts.Count);
        Assert.All(ollama.Prompts, p => Assert.Contains(Summarize.MermaidExample, p));
        Assert.All(ollama.Prompts, p => Assert.False(AsksForSvg(p)));

        new AiSettings { Provider = "claude", Fallback = false, Diagrams = "notes" }.Save(cfg.Home);
        await ai.SummarizeAsync(Lecture, cfg);
        await ai.WriteNotesAsync(Lecture, cfg, "claude", null, CancellationToken.None);
        Assert.Equal(2, claude.Prompts.Count);
        Assert.All(claude.Prompts, p => Assert.True(AsksForSvg(p)));
        Assert.Equal(2, ollama.Prompts.Count);
    }

    const string Svg30 = """
        <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 640 300">
        <title>A call stack</title>
        """;

    static string ThirtyLineSvg()
    {
        var lines = new List<string> { Svg30.TrimEnd() };
        for (int i = 0; i < 12; i++) lines.Add("""  <line x1="40" y1="60" x2="600" y2="60" stroke="#C7C7CC" stroke-width="1"/>""");
        for (int i = 0; i < 12; i++) lines.Add("""  <rect x="40" y="80" width="560" height="40" rx="6" fill="#E8F0FE" stroke="#1A73E8"/>""");
        lines.Add("""  <text x="60" y="105" font-size="14" fill="#1D1D1F">main()</text>""");
        lines.Add("""  <text x="60" y="145" font-size="14" fill="#1D1D1F">factorial(3)</text>""");
        lines.Add("""  <text x="60" y="185" font-size="14" fill="#1D1D1F">factorial(2)</text>""");
        lines.Add("</svg>");
        return string.Join("\n", lines);
    }

    [Fact]
    public void Code_formulas_and_diagrams_never_read_as_a_model_stuck_in_a_loop()
    {
        string code = "```python\n" + string.Concat(Enumerable.Repeat("    total = total + values[index]\n", 8)) + "```";
        string mermaid = "```mermaid\nflowchart TD\n" + string.Concat(Enumerable.Range(0, 8).Select(i => $"  subgraph g{i} [\"Group number {i}\"]\n    n{i}[\"Step number {i}\"]\n  end\n")) + "```";
        string maths = "$$\n" + string.Concat(Enumerable.Repeat("\\text{Stroke volume} = \\text{EDV} - \\text{ESV} \\\\\n", 6)) + "$$";
        string note = "## Details and examples\nA recursive factorial, traced on the stack.\n\n```svg\n" + ThirtyLineSvg() + "\n```\n\n"
            + "The loop adds each value.\n\n" + code + "\n\nGrouped steps.\n\n" + mermaid + "\n\n" + maths;
        Assert.Equal(30, ThirtyLineSvg().Split('\n').Length);
        Assert.False(Summarize.Repetitive(note));
        Assert.Equal(note, Summarize.CleanOutput(note));

        string loop = note + "\n\n" + string.Concat(Enumerable.Repeat("- **Osmosis** is water moving across a membrane.\n", 6));
        Assert.True(Summarize.Repetitive(loop));
        // A fence left open at the end is no block yet: a loop inside it is still a loop.
        Assert.True(Summarize.Repetitive("## Notes\n\n```\n" + string.Concat(Enumerable.Repeat("the same line over and over again\n", 6))));
    }

    [Fact]
    public void A_whole_note_fence_comes_off_but_a_note_that_starts_with_a_diagram_keeps_it()
    {
        const string Diagram = "```mermaid\nflowchart LR\n  A[\"Assess\"] --> B[\"Act\"]\n```";
        Assert.Equal("## Summary\nThe cycle.\n\n" + Diagram, Summarize.CleanOutput("```markdown\n## Summary\nThe cycle.\n\n" + Diagram + "\n```"));
        Assert.Equal("## Summary\nThe cycle.\n\n" + Diagram, Summarize.CleanOutput("```\n## Summary\nThe cycle.\n\n" + Diagram + "\n```"));
        foreach (string kept in new[]
        {
            Diagram,
            "```\nflowchart LR\n  A --> B\n```",
            "```svg\n" + Summarize.SvgExample + "\n```",
            "```\nflowchart TD\n  A --> B\n```\n\nFirst assess, then act.\n\n```\nprint(1)\n```",
            "```\nx = 1\n```\n\nA variable is set.\n\n```\nprint(x)\n```",
            Diagram + "\n\nFirst assess, then act.\n\n" + Diagram,
        })
            Assert.Equal(kept, Summarize.CleanOutput(kept));
    }

    const string Broken = "```mermaid\nflowchart LR\n  A[\"Assess\"] -->\n```";
    const string Fixed = "flowchart LR\n  A[\"Assess\"] --> B[\"Act\"]";

    [Fact]
    public async Task A_broken_diagram_goes_back_once_with_its_problem_and_the_fix_replaces_it()
    {
        var asked = new List<string>();
        string notes = "## Details and examples\nPain care.\n\n" + Broken + "\n\nAssess, then act.";
        string repaired = await Summarize.RepairDiagramsAsync(notes, p =>
        {
            asked.Add(p);
            return Task.FromResult("Here it is:\n\n```mermaid\n" + Fixed + "\n```");
        });
        var prompt = Assert.Single(asked);
        Assert.StartsWith("This Mermaid flowchart has a problem: Line 2 has an arrow that doesn't point at a box. Reply", prompt);
        Assert.Contains("Reply with only the corrected block.", prompt);
        Assert.EndsWith("\n\n```mermaid\nflowchart LR\n  A[\"Assess\"] -->\n```", prompt);
        Assert.Equal("## Details and examples\nPain care.\n\n```mermaid\n" + Fixed + "\n```\n\nAssess, then act.", repaired);
    }

    [Fact]
    public async Task A_broken_drawing_and_a_diagram_in_a_bullet_are_fixed_in_place()
    {
        string notes = "- The steps:\n\n  ```mermaid\n  flowchart LR\n    A[\"Assess\"] -->\n  ```\n\n```svg\n<svg xmlns=\"http://www.w3.org/2000/svg\"><text>Heart</text></svg>\n```";
        var asked = new List<string>();
        string repaired = await Summarize.RepairDiagramsAsync(notes, p =>
        {
            asked.Add(p);
            return Task.FromResult(p.Contains("SVG drawing")
                ? "```svg\n<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 640 200\"><text x=\"20\" y=\"40\">Heart</text></svg>\n```"
                : Fixed);
        });
        Assert.Equal(2, asked.Count);
        Assert.StartsWith("This SVG drawing has a problem: This drawing has no size", asked[1]);
        Assert.Equal("- The steps:\n\n  ```mermaid\n  flowchart LR\n    A[\"Assess\"] --> B[\"Act\"]\n  ```\n\n```svg\n"
            + "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 640 200\"><text x=\"20\" y=\"40\">Heart</text></svg>\n```", repaired);
    }

    [Fact]
    public async Task A_fix_that_is_still_broken_or_an_engine_that_fails_leaves_the_diagram_as_it_was()
    {
        string notes = "## Notes\n\n" + Broken + "\n\n```mermaid\nclassDiagram\n  Nurse <|-- Patient\n```";
        var asked = new List<string>();
        string still = await Summarize.RepairDiagramsAsync(notes, p =>
        {
            asked.Add(p);
            return Task.FromResult("```mermaid\nclassDiagram\n  A <|-- B\n```");
        });
        Assert.Equal(notes, still);
        Assert.Equal(2, asked.Count);
        Assert.StartsWith("This Mermaid flowchart has a problem: Study Stash draws flowcharts, state and sequence diagrams, timelines and mind maps; this is a class diagram. Reply", asked[1]);

        Assert.Equal(notes, await Summarize.RepairDiagramsAsync(notes, _ => throw new InvalidOperationException("Claude: usage limit")));
        Assert.Equal(notes, await Summarize.RepairDiagramsAsync(notes, _ => throw new TimeoutException("Ollama did not answer within 15 minutes")));
        await Assert.ThrowsAsync<OperationCanceledException>(() => Summarize.RepairDiagramsAsync(notes, _ => throw new OperationCanceledException()));
    }

    [Fact]
    public async Task A_diagram_fence_never_closed_is_left_alone_so_nothing_after_it_is_lost_and_a_reply_may_skip_its_closing_fence()
    {
        string unclosed = "## Notes\n\n```mermaid\nflowchart LR\n  A[\"Assess\"] -->\n\n## Questions to review\n1. Why reassess?";
        Assert.Same(unclosed, await Summarize.RepairDiagramsAsync(unclosed, _ => throw new InvalidOperationException("never asked")));

        string notes = "## Notes\n\n" + Broken;
        Assert.Equal("## Notes\n\n```mermaid\n" + Fixed + "\n```", await Summarize.RepairDiagramsAsync(notes, _ => Task.FromResult("```mermaid\n" + Fixed + "\n")));
    }

    [Fact]
    public async Task At_most_two_diagrams_go_back_and_good_notes_are_left_alone()
    {
        string notes = string.Join("\n\n", "## Notes", Broken, Broken, Broken);
        int calls = 0;
        string repaired = await Summarize.RepairDiagramsAsync(notes, _ =>
        {
            calls++;
            return Task.FromResult(Fixed);
        });
        Assert.Equal(Summarize.MaxDiagramRepairs, calls);
        Assert.Equal(string.Join("\n\n", "## Notes", "```mermaid\n" + Fixed + "\n```", "```mermaid\n" + Fixed + "\n```", Broken), repaired);

        string good = "## Notes\r\n\r\n```mermaid\r\n" + Summarize.MermaidExample + "\r\n```\r\n\r\n```svg\r\n" + Summarize.SvgExample + "\r\n```\r\n\r\n$$\\frac{a}{b}$$";
        Assert.Same(good, await Summarize.RepairDiagramsAsync(good, _ => throw new InvalidOperationException("never asked")));
    }

    [Fact]
    public async Task Notes_with_a_broken_diagram_are_repaired_by_the_engine_that_wrote_them()
    {
        using var dir = new TempDir();
        var cfg = new Config(dir["home"], dir["pool"]);
        var prompts = new List<string>();
        string notes = await Summarize.SummarizeTranscriptAsync(Lecture, cfg, (_, _, prompt, _) =>
        {
            prompts.Add(prompt);
            return Task.FromResult(prompts.Count == 1 ? "## Details and examples\nThe steps.\n\n" + Broken : Fixed);
        }, (_, _) => Task.FromResult<int?>(null), Drawings.FlowchartsAndSvg);
        Assert.Equal(2, prompts.Count);
        Assert.True(AsksForSvg(prompts[0]));
        Assert.StartsWith("This Mermaid flowchart has a problem", prompts[1]);
        Assert.Equal("## Details and examples\nThe steps.\n\n```mermaid\n" + Fixed + "\n```", notes);
    }
}
