using System.Text.Json.Nodes;
using StudyStash.Core.Ai;

namespace StudyStash.Core.Tests;

/// <summary>Search and Ask read a note's diagrams as their words and its code and formulas as blocks, never as
/// markup or headings; quick answers may carry formulas and a small flowchart, their LaTeX intact through JSON.</summary>
public class PassagesRichTests
{
    static readonly string Notes = string.Join("\n\n",
        "## Details and examples",
        "Blood returns to the right side of the heart.",
        "```mermaid\n" + Summarize.MermaidExample + "\n```",
        "Blood goes from the right side to the lungs, then the left side, then the body.",
        "```svg\n" + Summarize.SvgExample + "\n```",
        "$$\n\\text{MAP} = \\frac{\\text{SBP} + 2 \\times \\text{DBP}}{3}\n$$",
        "```python\n# the mean arterial pressure\ndef map_of(sbp, dbp):\n    return (sbp + 2 * dbp) / 3\n```",
        "A MAP under 65 needs a closer look.",
        "## Questions to review",
        "1. What does the tricuspid valve separate?");

    [Fact]
    public void A_diagram_is_one_passage_of_its_words_and_code_never_starts_a_section()
    {
        var passages = Passages.FromNotes("n", Notes);
        Assert.Equal(8, passages.Count);
        Assert.All(passages.Take(7), p => Assert.Equal("Details and examples", p.Section));
        Assert.Equal("Questions to review", passages[^1].Section);
        Assert.Equal("Diagram: Right atrium, tricuspid valve, Right ventricle, pulmonary valve, Lungs, Left atrium, mitral valve, "
            + "Left ventricle, aortic valve, Body", passages[1].Text);
        Assert.Equal("Diagram: Forces on a block on a slope, θ, weight mg, normal force N, friction f", passages[3].Text);
        Assert.Equal("\\text{MAP} = \\frac{\\text{SBP} + 2 \\times \\text{DBP}}{3}", passages[4].Text);
        Assert.StartsWith("# the mean arterial pressure\ndef map_of", passages[5].Text);
        Assert.Equal("A MAP under 65 needs a closer look.", passages[6].Text);
        foreach (var p in passages)
            foreach (string markup in new[] { "<svg", "viewBox", "-->", ":::", "```", "$$" })
                Assert.DoesNotContain(markup, p.Text);
    }

    [Fact]
    public void A_diagram_that_cant_be_drawn_still_finds_its_words_and_a_raw_svg_is_read_as_a_drawing()
    {
        var passages = Passages.FromNotes("n", "## Care\n\n```mermaid\nflowchart TD\n  A[Assess pain] -->\n```\n\n"
            + "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 640 200\">\n<text x=\"20\" y=\"40\">Left ventricle</text>\n</svg>\n\n"
            + "```mermaid\nsequenceDiagram\n  Nurse->>Patient: How is the pain?\n```");
        Assert.Equal(3, passages.Count);
        Assert.All(passages, p => Assert.Equal("Care", p.Section));
        Assert.StartsWith("Diagram: ", passages[0].Text);
        Assert.Contains("assess pain", passages[0].Text);
        Assert.DoesNotContain("-->", passages[0].Text);
        Assert.Equal("Diagram: Left ventricle", passages[1].Text);
        Assert.Contains("nurse", passages[2].Text);
        Assert.DoesNotContain("->>", passages[2].Text);
    }

    [Fact]
    public void Searching_the_library_finds_a_lecture_by_its_diagrams_words()
    {
        using var dir = new TempDir();
        var cfg = new Config(dir["home"], dir["pool"]);
        using var store = new Store(cfg.DbPath, cfg.PoolDir);
        store.Save(new Meeting("lec-1") { Title = "Blood flow", Date = "2026-09-23T09:00:00", Transcript = "Blood flows." },
            new Classification("BIO 110", 0.9, "ollama", "Blood flow", ["heart"]), summaryMd: Notes, summaryModel: "qwen3:8b");
        var hit = Assert.Single(store.SearchPassages(Passages.AllWords("tricuspid valve")!), h => h.Passage.Kind == Passage.NotesKind && h.Passage.Text.StartsWith("Diagram:"));
        Assert.Equal("lec-1", hit.Note.Id);
        Assert.Contains(store.SearchPassages(Passages.AllWords("friction")!), h => h.Passage.Text.Contains("friction f"));
        Assert.Empty(store.SearchPassages(Passages.AllWords("viewBox")!));
        Assert.Empty(store.SearchPassages(Passages.AllWords("marker")!));
    }

    [Fact]
    public async Task The_ask_prompt_asks_for_latex_and_allows_one_small_flowchart_and_single_backslashes_survive_the_json()
    {
        using var dir = new TempDir();
        var cfg = new Config(dir["home"], dir["pool"]);
        using var store = new Store(cfg.DbPath, cfg.PoolDir);
        var reader = new LibraryReader(cfg, store);
        string? prompt = null;
        string reply = """{"answer": "A normal MAP is $\frac{\text{SBP} + 2\times\text{DBP}}{3}$, about $\beta \rho \nu$ and $\sqrt{x}$.", "sources": [1]}""";
        var answer = await reader.AskAsync("What's a normal MAP?", liveTranscript: "A normal mean arterial pressure is 70 to 100.",
            chat: (p, _) =>
            {
                prompt = p;
                return Task.FromResult(reply);
            });
        Assert.Contains("Write any formula in LaTeX between $ signs; in the JSON, write each backslash twice (\\\\frac).", prompt);
        Assert.Contains("you may add one small Mermaid flowchart after the sentences, in a ```mermaid block", prompt);
        Assert.Equal("A normal MAP is $\\frac{\\text{SBP} + 2\\times\\text{DBP}}{3}$, about $\\beta \\rho \\nu$ and $\\sqrt{x}$.", answer["answer"]!.GetValue<string>());
        Assert.Single(answer["sources"]!.AsArray());

        // Written properly, with doubled backslashes and a flowchart after the sentence, it comes through as written.
        reply = new JsonObject { ["answer"] = "Assess, then act.\n\n```mermaid\nflowchart LR\n  A[\"Assess\"] --> B[\"Act\"]\n```\n\n$\\frac{1}{2}$", ["sources"] = new JsonArray(1) }.ToJsonString();
        answer = await reader.AskAsync("Draw the steps", liveTranscript: "Assess first, then act.", chat: (_, _) => Task.FromResult(reply));
        Assert.Equal("Assess, then act.\n\n```mermaid\nflowchart LR\n  A[\"Assess\"] --> B[\"Act\"]\n```\n\n$\\frac{1}{2}$", answer["answer"]!.GetValue<string>());
    }

    [Fact]
    public void An_engines_answer_with_single_backslash_latex_is_still_found_as_json()
    {
        string found = AiJobs.FirstObject("""Here you go: {"answer": "It is $\sqrt{x}$ and $\underbrace{a}$.", "sources": [2]} Hope that helps.""")!;
        Assert.NotNull(found);
        Assert.Equal("It is $\\sqrt{x}$ and $\\underbrace{a}$.", JsonNode.Parse(found)!["answer"]!.GetValue<string>());
        Assert.Equal("""{"answer": "fine", "sources": []}""", AiJobs.FirstObject("""{"answer": "fine", "sources": []}"""));
    }
}
