using StudyStash.Core.Rich;

namespace StudyStash.Core.Tests;

/// <summary>What the app's diagrams answer to: what's under the pointer, what lights up with it, which box an arrow
/// key goes to, a big chart folded into its groups (wrong folding would silently mislead a student), the order a
/// diagram is stepped through in, and where in the lecture a box was said.</summary>
public class DiagramInteractionTests
{
    static double Measure(string text, double size, bool bold) => text.Length * size * (bold ? 0.58 : 0.55);

    static DiagramScene Lay(Flowchart chart) => DiagramLayout.Lay(chart, Measure);

    // --- hit-testing --------------------------------------------------------------------------------------------------

    [Fact]
    public void A_point_finds_the_box_the_arrow_or_the_arrows_words_under_it_and_empty_paper_finds_nothing()
    {
        var chart = Flowchart.Parse(DiagramLayoutTests.Pain);
        var scene = Lay(chart);
        // The scene's arrows are the chart's, in the same order.
        Assert.Equal(chart.Edges.Select(e => (e.From, e.To)), scene.Edges.Select(e => (e.From, e.To)));
        foreach (var n in scene.Nodes)
            Assert.Equal(DiagramTarget.Node(n.Id), DiagramHit.At(scene, n.Box.Center));

        int yes = chart.Edges.ToList().FindIndex(e => e.Label == "yes");
        var words = DiagramHit.At(scene, scene.Edges[yes].LabelBox.Center);
        Assert.Equal(new DiagramTarget(DiagramPart.EdgeLabel, null, yes), words);

        // Halfway along a plain arrow, clear of its boxes, and a few pixels off its line.
        int plain = chart.Edges.ToList().FindIndex(e => e.From == "A" && e.To == "B");
        var line = DiagramHit.Line(scene.Edges[plain]);
        var mid = line[line.Count / 2];
        Assert.Equal(new DiagramTarget(DiagramPart.Edge, null, plain), DiagramHit.At(scene, mid + new Pt(3, 0)));
        Assert.True(DiagramHit.At(scene, new Pt(1, 1)).IsNothing);
    }

    [Fact]
    public void A_groups_title_and_inside_are_found_inner_group_first()
    {
        var chart = Flowchart.Parse(Nested);
        var scene = Lay(chart);
        var inner = scene.Groups.Single(g => g.Id == "vitals");
        Assert.Equal(new DiagramTarget(DiagramPart.GroupTitle, "vitals"), DiagramHit.At(scene, inner.TitleBox.Center));
        // A corner of the inner group, inside the outer one too, with no box there.
        var corner = new Pt(inner.Box.Right - 2, inner.Box.Bottom - 2);
        Assert.Equal(new DiagramTarget(DiagramPart.Group, "vitals"), DiagramHit.At(scene, corner));
    }

    [Fact]
    public void A_box_lights_its_arrows_and_the_boxes_they_join_and_an_arrow_its_two_ends()
    {
        var chart = Flowchart.Parse(DiagramLayoutTests.Pain);
        var scene = Lay(chart);
        var focus = DiagramHit.Around(scene, DiagramTarget.Node("B"));
        Assert.Equal(["A", "B", "C", "D"], focus.Nodes.Order());
        Assert.Equal([0, 1, 2], focus.Edges.Order());
        var arrow = DiagramHit.Around(scene, new DiagramTarget(DiagramPart.EdgeLabel, null, 5));
        Assert.Equal(["A", "E"], arrow.Nodes.Order());
        Assert.Equal([5], arrow.Edges);
        Assert.True(DiagramHit.Around(scene, DiagramTarget.Nothing).IsEmpty);
    }

    [Fact]
    public void Arrow_keys_walk_along_the_arrows_the_way_they_point_on_the_page()
    {
        var scene = Lay(Flowchart.Parse(DiagramLayoutTests.Pain));
        Assert.Equal("B", DiagramHit.Toward(scene, "A", 0, 1));
        Assert.Equal("A", DiagramHit.Toward(scene, "B", 0, -1));
        var below = DiagramHit.Toward(scene, "B", 0, 1);
        Assert.Contains(below, new[] { "C", "D" });
        // Sideways from one branch is the other branch, though no arrow joins them.
        string other = below == "C" ? "D" : "C";
        var c = scene.Nodes.Single(n => n.Id == below).Box.Center;
        var d = scene.Nodes.Single(n => n.Id == other).Box.Center;
        Assert.Equal(other, DiagramHit.Toward(scene, below!, Math.Sign(d.X - c.X), 0));
        Assert.Null(DiagramHit.Toward(scene, "A", 0, -1));
    }

    // --- folding groups -----------------------------------------------------------------------------------------------

    const string Nested = """
        flowchart TD
          Start([Patient arrives]) --> A
          subgraph assess [Assessment]
            A[Take history] --> B[Examine]
            subgraph vitals [Vital signs]
              C[Pulse] --> D[Blood pressure]
            end
            B --> C
          end
          D -->|stable| P[Plan care]
          D -->|unstable| X[Call for help]
          B -->|red flag| X
          P --> E[Evaluate]
          E -.->|not met| A
        """;

    [Fact]
    public void Folding_nothing_is_the_chart_as_written()
    {
        var chart = Flowchart.Parse(Nested);
        var folded = DiagramFold.Fold(chart, []);
        Assert.Same(chart, folded.Chart);
        Assert.All(chart.Nodes, n => Assert.Equal(n.Id, folded.Shown[n.Id]));
        Assert.Empty(folded.Inside);
    }

    [Fact]
    public void A_folded_group_is_one_box_with_its_title_and_count_and_its_arrows_join_that_box()
    {
        var chart = Flowchart.Parse(Nested);
        var folded = DiagramFold.Fold(chart, ["assess"]);
        var f = folded.Chart;
        // Its four boxes (two of them in the inner group) are one, where its first box was written.
        Assert.Equal(["Start", "assess", "P", "X", "E"], f.Nodes.Select(n => n.Id));
        var box = f.Node("assess")!;
        Assert.Equal(["Assessment", "4 boxes inside"], box.Lines);
        Assert.Equal(NodeShape.Subroutine, box.Shape);
        Assert.Equal(4, folded.Inside["assess"]);
        Assert.All(new[] { "A", "B", "C", "D" }, id => Assert.Equal("assess", folded.Shown[id]));
        Assert.True(folded.IsFolded("assess"));
        // Arrows inside it are gone; arrows in and out join its box; two that now join the same boxes the same way are
        // one, with both their words; the loop back still comes back to it.
        Assert.Equal(
            [("Start", "assess", null), ("assess", "P", "stable"), ("assess", "X", "unstable / red flag"), ("P", "E", null), ("E", "assess", "not met")],
            f.Edges.Select(e => (e.From, e.To, e.Label)));
        // Folding a group folds the groups inside it.
        Assert.Empty(f.Groups);
        // It reads and lays out like any chart (the app lays it out from its source).
        Assert.Equal(f.ToSource(), Flowchart.Parse(f.ToSource()).ToSource());
        Assert.NotEmpty(Lay(f).Nodes);
    }

    [Fact]
    public void An_inner_group_folded_is_a_box_of_the_group_around_it()
    {
        var chart = Flowchart.Parse(Nested);
        var folded = DiagramFold.Fold(chart, ["vitals"]);
        var f = folded.Chart;
        Assert.Equal(["Start", "A", "B", "vitals", "P", "X", "E"], f.Nodes.Select(n => n.Id));
        var outer = Assert.Single(f.Groups);
        Assert.Equal("assess", outer.Id);
        Assert.Equal(["A", "B", "vitals"], outer.Members);
        Assert.Equal(["Vital signs", "2 boxes inside"], f.Node("vitals")!.Lines);
        Assert.Contains(f.Edges, e => e.From == "B" && e.To == "vitals");
        Assert.Contains(f.Edges, e => e.From == "vitals" && e.To == "P" && e.Label == "stable");
        // Its own arrow (pulse to blood pressure) went with it; nothing else changed.
        Assert.DoesNotContain(f.Edges, e => e.From == "C" || e.To == "D");
        Assert.Contains(f.Edges, e => e.From == "B" && e.To == "X" && e.Label == "red flag");
        Assert.NotEmpty(Lay(f).Nodes);
        // Every box of the chart is shown by exactly one box of the folded chart.
        Assert.Equal(chart.Nodes.Count, f.Nodes.Sum(n => folded.Inside.GetValueOrDefault(n.Id, 1)));
    }

    [Fact]
    public void A_group_holds_its_own_boxes_and_its_inner_groups()
    {
        var chart = Flowchart.Parse(Nested);
        Assert.Equal(["A", "B", "C", "D"], DiagramFold.Members(chart, "assess"));
        Assert.Equal(["C", "D"], DiagramFold.Members(chart, "vitals"));
        Assert.Equal(["assess", "vitals"], DiagramFold.All(chart));
        // A small chart opens as written; a big one made of groups opens as its overview.
        Assert.False(DiagramFold.StartsFolded(chart));
        var big = Flowchart.Parse(string.Join("\n",
            ["flowchart LR", "subgraph a [Before]", .. Enumerable.Range(1, 6).Select(i => $"a{i}[Step {i}]"), "end",
             "subgraph b [After]", .. Enumerable.Range(1, 6).Select(i => $"b{i}[Then {i}]"), "end", "a6 --> b1"]));
        Assert.True(DiagramFold.StartsFolded(big));
    }

    // --- stepping through ---------------------------------------------------------------------------------------------

    static string Words(Flowchart chart, IReadOnlyList<DiagramStep> steps) => string.Join(" > ", steps.Select(s => chart.Node(s.Node)!.Label));

    [Fact]
    public void A_decision_is_read_from_the_question_down_each_branch_and_its_loop_goes_back_to_the_start()
    {
        var chart = Flowchart.Parse(DiagramLayoutTests.Pain);
        var steps = DiagramSteps.Order(chart);
        Assert.Equal(["A", "B", "C", "D", "E"], steps.Select(s => s.Node));
        Assert.Empty(steps[0].Arrives);
        Assert.Equal("yes", steps[2].Via);
        Assert.Equal("no", steps[3].Via);
        // The merge arrives from both branches; the loop back is its way out.
        Assert.Equal([3, 4], steps[4].Arrives);
        var back = Assert.Single(steps[4].Returns);
        Assert.Equal(("E", "A"), (chart.Edges[back].From, chart.Edges[back].To));
    }

    [Fact]
    public void A_cycle_starts_at_its_first_box_and_closes_back_to_it()
    {
        var chart = Flowchart.Parse(DiagramLayoutTests.CardiacCycle);
        var steps = DiagramSteps.Order(chart);
        Assert.Equal("Atrial systole > Isovolumetric contraction > Ventricular ejection > Isovolumetric relaxation > Ventricular filling", Words(chart, steps));
        Assert.Equal("A", chart.Edges[Assert.Single(steps[^1].Returns)].To);
    }

    [Fact]
    public void A_tree_reads_like_an_outline_and_a_group_is_finished_before_the_walk_leaves_it()
    {
        var shock = Flowchart.Parse(DiagramLayoutTests.Shock);
        Assert.Equal(
            "Shock > Hypovolaemic > Haemorrhage > Burns > Cardiogenic > Distributive > Septic > Anaphylactic > Neurogenic > Obstructive > Tension pneumothorax",
            Words(shock, DiagramSteps.Order(shock)));
        var nested = Flowchart.Parse(Nested);
        Assert.Equal(["Start", "A", "B", "C", "D", "P", "E", "X"], DiagramSteps.Order(nested).Select(s => s.Node));
        // A box nothing joins comes last; every box is a step exactly once.
        var loose = Flowchart.Parse("flowchart TD\n  N[Note this]\n  A[One] --> B[Two]");
        Assert.Equal(["A", "B", "N"], DiagramSteps.Order(loose).Select(s => s.Node));
    }

    [Fact]
    public void A_folded_chart_steps_through_its_overview()
    {
        var folded = DiagramFold.Fold(Flowchart.Parse(Nested), ["assess"]).Chart;
        Assert.Equal(["Start", "assess", "P", "E", "X"], DiagramSteps.Order(folded).Select(s => s.Node));
    }

    // --- the moment of the lecture ------------------------------------------------------------------------------------

    [Fact]
    public void A_designed_diagram_says_the_moment_it_comes_from()
    {
        Assert.Equal(12 * 60 + 34, DiagramMoment.From("%% Study Stash diagram, from 12:34\nflowchart LR\n  A --> B"));
        Assert.Equal(3723, DiagramMoment.From("<!-- Study Stash diagram, from 1:02:03 -->\n<svg></svg>"));
        Assert.Null(DiagramMoment.From("%% Study Stash diagram\nflowchart LR\n  A --> B"));
        Assert.Null(DiagramMoment.From("flowchart LR\n  A --> B"));
    }

    [Fact]
    public void A_boxs_words_find_the_lines_they_were_said_in_rare_words_counting_most()
    {
        Spoken[] lines =
        [
            new(10, 20, "Good morning everyone, today is the heart."),
            new(300, 310, "So the ventricles contract with the valves shut, that's isovolumetric contraction."),
            new(320, 330, "Then the pressure is high enough and we get ventricular ejection into the arteries."),
            new(900, 910, "Remember the ventricles also relax, isovolumetric relaxation."),
            new(2000, 2010, "Ejection fraction is a separate idea we'll cover next week."),
        ];
        var found = DiagramMoment.Said("Ventricular ejection", lines);
        Assert.Equal(320, found[0].Line.Start);
        Assert.Equal(300, DiagramMoment.Said("Isovolumetric contraction", lines)[0].Line.Start);
        // A tie goes to the line nearest the diagram's own moment.
        Assert.Equal(900, DiagramMoment.Said("Isovolumetric relaxation", lines, near: 880)[0].Line.Start);
        Assert.Empty(DiagramMoment.Said("Kidney filtration", lines));
        Assert.Empty(DiagramMoment.Said("8", lines));
    }
}
