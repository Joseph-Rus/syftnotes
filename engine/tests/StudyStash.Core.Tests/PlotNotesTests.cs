using System.Text.Json.Nodes;
using StudyStash.Core.Ai;
using StudyStash.Core.Rich;
using StudyStash.Library;

namespace StudyStash.Core.Tests;

/// <summary>
/// Plots in a lecture's notes, everywhere notes go: the diagram designer can draw one (strictly read, looked over and
/// sent back once with what's wrong), a broken one is repaired once, and a later pass replaces it; a downloaded note
/// keeps its source and saves its picture; the phone shows the picture; search and Ask read its words. Engines are fakes.
/// </summary>
public class PlotNotesTests
{
    const string Said = "The sigmoid squashes any score into a probability between zero and one, and the steepness k sets how "
        + "sharp the step is; at a score of zero the sigmoid is exactly one half, the decision threshold.";

    static readonly Meeting Lecture = new("ml-1")
    {
        Title = "Logistic regression", Date = "2026-09-28T09:00:00", Folder = "CS 4780",
        Transcript = string.Join("\n", Enumerable.Range(0, 24).Select(i => $"[{TimedText.Clock(i * 25)}] {Said}")),
    };

    const string Notes = "## Summary\nLogistic regression turns a score into a probability.\n\n## The sigmoid\n- σ(z) = 1/(1 + e^{-z}) squashes a score into (0, 1).\n\n## Questions to review\n1. What is σ(0)?";

    static string Reply(string kind, string source, string title = "The sigmoid squashes a score") =>
        new JsonObject
        {
            ["reason"] = "The sigmoid's shape is the point.",
            ["diagrams"] = new JsonArray(new JsonObject
            {
                ["title"] = title, ["after"] = "The sigmoid", ["at"] = "00:25", ["kind"] = kind, ["source"] = source,
                ["caption"] = "A larger steepness makes the step sharper; at zero it's one half.",
            }),
        }.ToJsonString();

    [Fact]
    public void Every_example_looks_right_and_a_bad_plot_is_told_what_to_fix()
    {
        foreach (var (title, _, source) in PlotDesign.Examples)
            Assert.True(PlotLint.Problems(Plot.Parse(source)).Count == 0, $"{title}: {string.Join("; ", PlotLint.Problems(Plot.Parse(source)))}");
        static IReadOnlyList<string> Of(string source) => PlotLint.Problems(Plot.Parse(source));
        Assert.Contains(Of("x -5 to 5\ny 0 to 1\nf(x) = sqrt(-1 - x^2)"), p => p.Contains("undefined almost everywhere"));
        Assert.Contains(Of("x 0 to 1\ny -10 to 10\nf(x) = 0.001 x"), p => p.Contains("flat"));
        Assert.Contains(Of("x 0 to 10\ny 0 to 1\nf(x) = 100 + x"), p => p.Contains("mostly off the plot"));
        Assert.Contains(Of("x 0 to 1\nparam k = 1 from 0 to 2\nf(x) = x"), p => p.Contains("changes nothing"));
        Assert.Contains(Of("x 0 to 1\ny 0 to 1\nf(x) = x\npoint (5, 5) \"far\""), p => p.Contains("outside the plot"));
        Assert.Contains(Of("y = x^2"), p => p.Contains("no x range"));
        Assert.Contains(Of("x -3 to 3\ny -1 to 1\nparam k = 1 from 0.1 to 100\nf(x) = k + x / 10"), p => p.Contains("leaves the plot entirely"));
    }

    [Fact]
    public async Task The_designer_draws_a_plot_at_the_end_of_its_section_and_a_later_pass_replaces_it()
    {
        var prompts = new List<string>();
        var result = await DiagramDesign.DesignAsync(Lecture, Notes, Drawings.Flowcharts, (p, _) =>
        {
            prompts.Add(p);
            return Task.FromResult(Reply("plot", PlotDesign.Sigmoid));
        });
        Assert.Contains("\"plot\"", prompts[0]);
        Assert.Contains("```plot", prompts[0]);
        Assert.Single(result.Drawn);
        Assert.Empty(result.Problems);
        Assert.Contains($"**The sigmoid squashes a score**\n\n```plot\n%% Study Stash diagram, from {TimedText.Clock(25)}\ntitle The sigmoid squashes", result.Notes);
        Assert.True(result.Notes.IndexOf("```plot", StringComparison.Ordinal) < result.Notes.IndexOf("## Questions to review", StringComparison.Ordinal));
        var block = NoteBlocks.Find(result.Notes).Single(b => b.Kind == NoteBlockKind.Plot);
        Assert.Equal(25, DiagramMoment.From(block.Text));
        Assert.Null(Plot.Problem(block.Text));
        Assert.Equal(Notes, DiagramDesign.Strip(result.Notes));
    }

    [Fact]
    public async Task A_plot_that_would_look_wrong_goes_back_once_with_what_is_wrong()
    {
        const string OffThePlot = "x -6 to 6 \"z\"\ny 5 to 10\nσ(z) = 1 / (1 + e^(-z)) \"sigmoid\"";
        var prompts = new List<string>();
        var result = await DiagramDesign.DesignAsync(Lecture, Notes, Drawings.Flowcharts, (p, _) =>
        {
            prompts.Add(p);
            return Task.FromResult(prompts.Count == 1 ? Reply("plot", OffThePlot) : Reply("plot", PlotDesign.Sigmoid));
        });
        Assert.Equal(2, prompts.Count);
        Assert.Contains("mostly off the plot", prompts[1]);
        Assert.Contains("```plot\n" + OffThePlot, prompts[1]);
        Assert.Contains("A ```plot block is drawn exactly", prompts[1]);
        Assert.Equal(1, result.Revised);
        Assert.Contains("y -0.05 to 1.05", result.Notes);
    }

    [Fact]
    public async Task A_broken_plot_is_repaired_once_with_the_format_beside_it()
    {
        string asked = "";
        string notes = await Summarize.RepairDiagramsAsync("Intro\n\n```plot\nx -5 to 5\ny = sigm(x)\n```\n", p =>
        {
            asked = p;
            return Task.FromResult("```plot\nx -5 to 5\ny = 1 / (1 + e^(-x))\n```");
        });
        Assert.Contains("Line 2", asked);
        Assert.Contains("“sigm”", asked);
        Assert.Contains("A ```plot block is drawn exactly", asked);
        Assert.Contains("```plot\nx -5 to 5\ny = 1 / (1 + e^(-x))\n```", notes);
    }

    [Fact]
    public void A_downloaded_note_keeps_the_plots_source_and_saves_its_picture()
    {
        var lecture = new JsonObject
        {
            ["title"] = "Logistic regression", ["date"] = "2026-09-28T09:00:00", ["class"] = "CS 4780",
            ["notes"] = "## The sigmoid\n\n```plot\n" + PlotDesign.Sigmoid + "\n```\n\nAfter it.",
        };
        var file = NoteExport.Lecture(lecture, transcript: false, _ => null);
        Assert.Contains("```plot\n" + Plot.Parse(PlotDesign.Sigmoid).ToSource() + "\n```\n\n![Plot: The sigmoid squashes any score into (0, 1)](", file.Markdown);
        var asset = Assert.Single(file.Assets);
        Assert.EndsWith(".assets/diagram-1.svg", asset.RelativePath);
        Assert.StartsWith("<svg", asset.Text);
        Assert.Contains("σ(kz)", asset.Text);
    }

    [Fact]
    public void The_phone_shows_a_plot_as_its_picture_and_search_reads_its_words()
    {
        string html = PhoneNotes.Render("## Activations\n\n```plot\n" + PlotDesign.Activations + "\n```\n\n```plot\n" + PlotDesign.Sigmoid + "\n```");
        Assert.Equal(2, html.Split("<figure class=\"diagram\"><svg").Length - 1);
        Assert.Contains("leaky ReLU", html);
        Assert.DoesNotContain("<script", html);
        // Two plots on one page never share a clip.
        var ids = System.Text.RegularExpressions.Regex.Matches(html, "clipPath id=\"([^\"]+)\"").Select(m => m.Groups[1].Value).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());

        var passages = Passages.FromNotes("n", "## Activations\n\n```plot\n" + PlotDesign.Activations + "\n```");
        var plot = Assert.Single(passages, p => p.Text.StartsWith("Plot: ", StringComparison.Ordinal));
        Assert.Contains("ReLU, leaky ReLU and softplus", plot.Text);
        Assert.Contains("leaky(z) = max(a z, z)", plot.Text);
        Assert.Contains("a (leak a)", plot.Text);
    }

    [Fact]
    public void Asking_to_see_a_formula_offers_the_plot_format_and_only_then()
    {
        Assert.True(PlotDesign.AsksForPlot(PlotDesign.ShowQuestion("\\sigma(z) = \\frac{1}{1+e^{-z}}")));
        Assert.True(PlotDesign.AsksForPlot("Can you graph the loss?"));
        Assert.False(PlotDesign.AsksForPlot("What is the chain rule?"));
        Assert.True(PlotDesign.Plottable("\\sigma(z) = \\frac{1}{1+e^{-z}}"));
        Assert.False(PlotDesign.Plottable("\\Omega"));
    }
}
