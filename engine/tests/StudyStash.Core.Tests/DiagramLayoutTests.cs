using System.Diagnostics;
using StudyStash.Core.Rich;

namespace StudyStash.Core.Tests;

/// <summary>Laying flowcharts out: the right layout for each kind of chart, nothing overlapping, every arrow from
/// border to border, and the same picture every time.</summary>
public class DiagramLayoutTests
{
    /// <summary>A stand-in for the app's font: every character the same width, box words a little wider.</summary>
    static double Measure(string text, double size, bool bold) => new System.Globalization.StringInfo(text).LengthInTextElements * size * (bold ? 0.58 : 0.55);

    static DiagramScene Lay(string source, ChartDirection? direction = null) => DiagramLayout.Lay(Flowchart.Parse(source), Measure, direction);

    internal const string CardiacCycle = """
        flowchart LR
          A([Atrial systole]):::accent --> B[Isovolumetric contraction] --> C[Ventricular ejection] --> D[Isovolumetric relaxation] --> E[Ventricular filling] --> A
        """;

    internal const string BloodFlow = """
        flowchart TD
          VC[Venae cavae]:::blue --> RA[Right atrium]:::blue -->|tricuspid valve| RV[Right ventricle]:::blue -->|pulmonary valve| PA[Pulmonary arteries]:::blue --> L((Lungs))
          L --> PV[Pulmonary veins]:::red --> LA[Left atrium]:::red -->|mitral valve| LV[Left ventricle]:::red -->|aortic valve| AO[Aorta]:::red --> B((Body)) --> VC
        """;

    internal const string Pain = """
        flowchart TD
          A[Assess pain on a 0 to 10 scale] --> B{Score above 4?}
          B -->|yes| C[Give the prescribed analgesic]
          B -->|no| D[Reposition and use non-drug comfort measures]
          C --> E([Reassess in 30 to 60 minutes])
          D --> E
          E -.->|still in pain| A
        """;

    internal const string NursingProcess = """
        flowchart LR
          subgraph gather [Gather]
            A[Assess] --> D[Diagnose]
          end
          subgraph act [Act]
            P[Plan] --> I[Implement]
          end
          D --> P
          I --> E[Evaluate]
          E -.->|new data| A
        """;

    internal const string Shock = """
        flowchart TD
          S[Shock] --> H[Hypovolaemic]
          S --> C[Cardiogenic]
          S --> D[Distributive]
          S --> O[Obstructive]
          H --> H1[Haemorrhage]
          H --> H2[Burns]
          D --> D1[Septic]
          D --> D2[Anaphylactic]
          D --> D3[Neurogenic]
          O --> O1[Tension pneumothorax]
        """;

    internal const string SearchTree = """
        flowchart TD
          R((8)) --> L((3))
          R --> Q((10))
          L --> L1((1))
          L --> L2((6))
          Q --> Q2((14))
          L2 --> L3((4))
          L2 --> L4((7))
        """;

    const string Everything = """
        flowchart TD
          subgraph outer [Outer group]
            A[Box] --> B(Rounded)
            subgraph inner [Inner group]
              C([Stadium]) ==> D((Circle))
            end
          end
          B --> C
          D -.-> E{Decision?}
          E -->|yes| F{{Hexagon}}
          E -->|no| G[[Subroutine]]
          F --o H[(Cylinder)]
          G --x H
          H <--> A
          H --- I[Loose end]
          A --> A
          F --> H
          F --> H
        """;

    public static TheoryData<string> Samples => [CardiacCycle, BloodFlow, Pain, NursingProcess, Shock, SearchTree, Everything];

    [Fact]
    public void Cycles_go_on_a_ring()
    {
        Assert.Equal(SceneKind.Ring, Lay(CardiacCycle).Kind);
        var blood = Lay(BloodFlow);
        Assert.Equal(SceneKind.Ring, blood.Kind);
        Assert.Equal(10, blood.Nodes.Count);
    }

    [Fact]
    public void A_ring_starts_at_twelve_and_runs_clockwise()
    {
        var s = Lay(CardiacCycle);
        var c = s.Nodes.Select(n => n.Box.Center).ToList();
        Assert.True(c[0].Y < c.Skip(1).Min(p => p.Y), "the first box is at the top");
        Assert.True(c[1].X > c[0].X, "the second box is to its right");
        Assert.True(c[4].X < c[0].X, "the last box is to its left");
    }

    [Fact]
    public void Ring_words_sit_outside_the_ring()
    {
        var s = Lay(BloodFlow);
        double cx = s.Nodes.Average(n => n.Box.Center.X), cy = s.Nodes.Average(n => n.Box.Center.Y);
        foreach (var e in s.Edges.Where(e => e.LabelLines.Count > 0))
        {
            var mid = e.Path[^1].End;
            Assert.True(Pt.Distance(e.LabelBox.Center, new Pt(cx, cy)) > Pt.Distance(mid, new Pt(cx, cy)) - 30);
        }
        Assert.Equal(4, s.Edges.Count(e => e.LabelLines.Count > 0));
    }

    [Fact]
    public void Hierarchies_are_trees_that_keep_left_and_right()
    {
        var s = Lay(SearchTree);
        Assert.Equal(SceneKind.Tree, s.Kind);
        double X(string id) => s.Nodes.Single(n => n.Id == id).Box.Center.X;
        double Y(string id) => s.Nodes.Single(n => n.Id == id).Box.Center.Y;
        Assert.True(X("L") < X("R") && X("R") < X("Q"), "3 left of 8, 10 right of it");
        Assert.True(X("L1") < X("L2"), "1 left of 6");
        Assert.True(X("L3") < X("L4"), "4 left of 7");
        Assert.True(Y("R") < Y("L") && Y("L") < Y("L2") && Y("L2") < Y("L3"));
        Assert.Equal(Y("L"), Y("Q"), 3);
        Assert.Equal((X("L3") + X("L4")) / 2, X("L2"), 3);
    }

    [Fact]
    public void A_top_down_tree_too_wide_for_the_column_turns_left_to_right()
    {
        var chart = Flowchart.Parse(Shock);
        var down = DiagramLayout.Lay(chart, Measure);
        Assert.True(down.Width > 620);
        var fitted = DiagramLayout.Fit(chart, Measure, 620);
        Assert.Equal(ChartDirection.LeftRight, fitted.Direction);
        Assert.True(fitted.Width <= 620);
    }

    [Fact]
    public void A_classification_is_a_tree_in_the_order_written()
    {
        var s = Lay(Shock);
        Assert.Equal(SceneKind.Tree, s.Kind);
        var types = new[] { "H", "C", "D", "O" }.Select(id => s.Nodes.Single(n => n.Id == id).Box.Center.X).ToList();
        Assert.Equal(types.Order(), types);
    }

    [Theory]
    [InlineData(ChartDirection.LeftRight)]
    [InlineData(ChartDirection.RightLeft)]
    [InlineData(ChartDirection.BottomUp)]
    public void Trees_honour_the_direction(ChartDirection direction)
    {
        var s = Lay(Shock, direction);
        var root = s.Nodes.Single(n => n.Id == "S").Box.Center;
        var child = s.Nodes.Single(n => n.Id == "H").Box.Center;
        switch (direction)
        {
            case ChartDirection.LeftRight: Assert.True(root.X < child.X); break;
            case ChartDirection.RightLeft: Assert.True(root.X > child.X); break;
            default: Assert.True(root.Y > child.Y); break;
        }
    }

    [Fact]
    public void Decisions_and_groups_are_layered()
    {
        Assert.Equal(SceneKind.Layered, Lay(Pain).Kind);
        Assert.Equal(SceneKind.Layered, Lay(NursingProcess).Kind);
        Assert.Equal(SceneKind.Layered, Lay("flowchart TD\nsubgraph g [G]\nA --> B\nend").Kind);
    }

    [Theory]
    [InlineData(ChartDirection.TopDown)]
    [InlineData(ChartDirection.LeftRight)]
    [InlineData(ChartDirection.RightLeft)]
    [InlineData(ChartDirection.BottomUp)]
    public void Layered_charts_honour_the_direction(ChartDirection direction)
    {
        var s = Lay(Pain, direction);
        var first = s.Nodes.Single(n => n.Id == "A").Box.Center;
        var last = s.Nodes.Single(n => n.Id == "E").Box.Center;
        switch (direction)
        {
            case ChartDirection.TopDown: Assert.True(first.Y < last.Y); break;
            case ChartDirection.BottomUp: Assert.True(first.Y > last.Y); break;
            case ChartDirection.LeftRight: Assert.True(first.X < last.X); break;
            default: Assert.True(first.X > last.X); break;
        }
    }

    [Theory]
    [InlineData(ChartDirection.TopDown)]
    [InlineData(ChartDirection.LeftRight)]
    [InlineData(ChartDirection.RightLeft)]
    [InlineData(ChartDirection.BottomUp)]
    public void A_decisions_answers_keep_the_order_they_were_written_in(ChartDirection direction)
    {
        var s = Lay(Pain, direction);
        var yes = s.Nodes.Single(n => n.Id == "C").Box.Center;
        var no = s.Nodes.Single(n => n.Id == "D").Box.Center;
        if (direction is ChartDirection.TopDown or ChartDirection.BottomUp) Assert.True(yes.X < no.X, "yes is left of no");
        else Assert.True(yes.Y < no.Y, "yes is above no");
        // ...and the arrow back to the start goes round the outside, crossing nothing.
        Assert.Equal(0, DiagramLayout.Crossings(s));
    }

    [Fact]
    public void An_arrow_back_to_the_start_runs_against_the_flow_and_the_start_stays_first()
    {
        var s = Lay(NursingProcess.Replace("flowchart LR", "flowchart TD"));
        double Y(string id) => s.Nodes.Single(n => n.Id == id).Box.Center.Y;
        Assert.True(Y("A") < Y("D") && Y("D") < Y("P") && Y("I") < Y("E"), "written order, top to bottom");
        var back = s.Edges.Single(e => e.From == "E" && e.To == "A");
        Assert.True(back.Path[0].A.Y > back.Tip.Y, "the dotted arrow climbs back to the start");
    }

    [Fact]
    public void A_group_sits_in_line_with_the_arrows_into_and_out_of_it()
    {
        var s = Lay("""
            flowchart TD
              subgraph assess [Assessment]
                A[Collect data] --> V[Validate the cues]
              end
              V --> D[Nursing diagnosis] --> P[Plan] --> I[Implement] --> E{Outcomes met?}
              E -->|yes| R([Resolve])
              E -.->|no| A
            """);
        var group = s.Groups.Single();
        Box Of(string id) => s.Nodes.Single(n => n.Id == id).Box;
        Assert.True(group.Box.Bottom <= Of("D").Y, "the group is above the diagnosis it leads to");
        Assert.True(Of("D").Bottom <= Of("P").Y && Of("I").Bottom <= Of("E").Y);
        Assert.All(s.Nodes.Where(n => n.Id is not ("A" or "V")), n => Assert.False(group.Box.Intersects(n.Box)));
    }

    [Theory]
    [MemberData(nameof(Samples))]
    public void No_two_boxes_overlap(string source)
    {
        var s = Lay(source);
        for (int i = 0; i < s.Nodes.Count; i++)
            for (int j = i + 1; j < s.Nodes.Count; j++)
                Assert.False(s.Nodes[i].Box.Intersects(s.Nodes[j].Box), $"{s.Nodes[i].Id} overlaps {s.Nodes[j].Id}");
    }

    [Theory]
    [MemberData(nameof(Samples))]
    public void Words_never_sit_on_a_box(string source)
    {
        var s = Lay(source);
        foreach (var e in s.Edges.Where(e => e.LabelLines.Count > 0))
            foreach (var n in s.Nodes)
                Assert.False(e.LabelBox.Intersects(n.Box), $"the words “{string.Join(" ", e.LabelLines)}” sit on {n.Id}");
        var labelled = s.Edges.Where(e => e.LabelLines.Count > 0).ToList();
        for (int i = 0; i < labelled.Count; i++)
            for (int j = i + 1; j < labelled.Count; j++)
                Assert.False(labelled[i].LabelBox.Intersects(labelled[j].LabelBox));
    }

    [Theory]
    [MemberData(nameof(Samples))]
    public void Every_arrow_runs_from_border_to_border(string source)
    {
        var s = Lay(source);
        var byId = s.Nodes.ToDictionary(n => n.Id);
        foreach (var e in s.Edges)
        {
            double start = DiagramLayout.Outline(byId[e.From], e.StartTip), tip = DiagramLayout.Outline(byId[e.To], e.Tip);
            Assert.True(Math.Abs(start) <= 2, $"{e.From}→{e.To} starts {start:0.0} px from {e.From}'s border");
            Assert.True(Math.Abs(tip) <= 2, $"{e.From}→{e.To} ends {tip:0.0} px from {e.To}'s border");
            Assert.Equal(e.StartBase, e.Path[0].A);
            Assert.Equal(e.Base, e.Path[^1].End);
            if (e.EndEnd == EdgeEnd.Arrow) Assert.Equal(DiagramLayout.ArrowLength, Pt.Distance(e.Tip, e.Base), 1.5);
            if (e.EndEnd == EdgeEnd.None && e.From != e.To) Assert.Equal(e.Tip, e.Base);
        }
    }

    [Theory]
    [MemberData(nameof(Samples))]
    public void Everything_is_inside_the_scene(string source)
    {
        var s = Lay(source);
        var all = new Box(0, 0, s.Width, s.Height);
        foreach (var n in s.Nodes) Assert.True(all.Contains(n.Box));
        foreach (var g in s.Groups) Assert.True(all.Contains(g.Box));
        foreach (var e in s.Edges.Where(e => e.LabelLines.Count > 0)) Assert.True(all.Contains(e.LabelBox));
        foreach (var e in s.Edges) Assert.True(all.Contains(e.Path[0].A) && all.Contains(e.Tip));
    }

    [Fact]
    public void Groups_hold_their_boxes_and_their_title()
    {
        var s = Lay(Everything);
        var outer = s.Groups.Single(g => g.Id == "outer");
        var inner = s.Groups.Single(g => g.Id == "inner");
        Box Of(string id) => s.Nodes.Single(n => n.Id == id).Box;
        Assert.True(outer.Box.Contains(Of("A")) && outer.Box.Contains(Of("B")));
        Assert.True(inner.Box.Contains(Of("C")) && inner.Box.Contains(Of("D")));
        Assert.True(outer.Box.Contains(inner.Box));
        Assert.Equal(0, outer.Depth);
        Assert.Equal(1, inner.Depth);
        foreach (var g in s.Groups)
        {
            Assert.True(g.Box.Contains(g.TitleBox));
            // The title's line sits above every box in the group.
            foreach (var n in s.Nodes.Where(n => g.Box.Contains(n.Box)))
                Assert.False(n.Box.Intersects(g.TitleBox), $"{n.Id} runs into {g.Title}'s title");
        }
        var nursing = Lay(NursingProcess);
        Assert.True(nursing.Groups.Single(g => g.Id == "gather").Box.Contains(nursing.Nodes.Single(n => n.Id == "A").Box));
    }

    [Theory]
    [MemberData(nameof(Samples))]
    public void The_same_chart_lays_out_the_same_twice(string source)
    {
        Assert.Equal(DiagramSvg.Render(Lay(source)), DiagramSvg.Render(Lay(source)));
    }

    [Fact]
    public void A_left_to_right_chart_too_wide_for_the_column_comes_back_top_down()
    {
        const string steps = "flowchart LR\n  A[Wash your hands] --> B[Check the order] --> C{Right patient?} -->|yes| D[Give the dose] --> E[Document it]\n  C -->|no| F[Stop and ask]";
        var chart = Flowchart.Parse(steps);
        var wide = DiagramLayout.Lay(chart, Measure);
        Assert.True(wide.Width > 620);
        var fitted = DiagramLayout.Fit(chart, Measure, 620);
        Assert.Equal(ChartDirection.TopDown, fitted.Direction);
        Assert.True(fitted.Width < wide.Width);
        // A chart that fits keeps its direction.
        Assert.Equal(ChartDirection.LeftRight, DiagramLayout.Fit(Flowchart.Parse("flowchart LR\nA --> B"), Measure, 620).Direction);
    }

    [Fact]
    public void Words_wrap_into_balanced_lines()
    {
        static double W(string s) => s.Length * 7.5;
        Assert.Equal(["Assess pain on", "a 0 to 10 scale"], DiagramLayout.Wrap(["Assess pain on a 0 to 10 scale"], 150, W));
        Assert.Equal(["Short"], DiagramLayout.Wrap(["Short"], 150, W));
        Assert.Equal(["Reposition and", "use non-drug", "comfort measures"], DiagramLayout.Wrap(["Reposition and use non-drug comfort measures"], 150, W));
        Assert.Equal(["one", "two"], DiagramLayout.Wrap(["one", "two"], 150, W));
        var cjk = DiagramLayout.Wrap([new string('心', 40)], 150, W);
        Assert.True(cjk.Count > 1 && cjk.All(l => W(l) <= 150));
    }

    [Fact]
    public void Boxes_fit_their_words()
    {
        var s = Lay(Pain);
        foreach (var n in s.Nodes)
        {
            double widest = n.Lines.Max(l => Measure(l, DiagramLayout.TextSize, true));
            Assert.True(n.Box.W >= widest + 20, $"{n.Id} is too narrow");
            Assert.True(n.Box.H >= n.Lines.Count * DiagramLayout.LineHeight);
        }
        var decision = s.Nodes.Single(n => n.Id == "B");
        Assert.Equal(NodeShape.Decision, decision.Shape);
        // The words' corners are inside the rhombus.
        double tw = decision.Lines.Max(l => Measure(l, 13, true)), th = decision.Lines.Count * 17;
        Assert.True(tw / decision.Box.W + th / decision.Box.H <= 1);
    }

    [Fact]
    public void Odd_charts_lay_out_without_trouble()
    {
        foreach (string source in new[]
        {
            "flowchart TD\nA",
            "flowchart TD\nA --> A",
            "flowchart TD\nA --> B\nA --> B\nB --> A",
            "flowchart LR\nA[\"\"] --> B[\"   \"]",
            "flowchart TD\n心脏[心脏收缩期心室射血] --> 肺[肺循环]",
            "flowchart TD\nA & B & C --> D & E & F",
            "flowchart BT\nsubgraph a [A]\nsubgraph b [B]\nX --> Y\nend\nend\nY --> Z",
            "flowchart TD\nA --> B\nC --> D",
        })
        {
            var s = Lay(source);
            Assert.True(s.Width > 0 && s.Height > 0, source);
            Assert.All(s.Edges, e => Assert.True(double.IsFinite(e.Tip.X) && double.IsFinite(e.Tip.Y)));
        }
    }

    [Fact]
    public void Sixty_boxes_lay_out_quickly()
    {
        var random = new Random(7);
        var lines = new List<string> { "flowchart TD" };
        for (int i = 0; i < 60; i++) lines.Add($"N{i}[Step number {i}]");
        for (int i = 0; i < 110; i++) lines.Add($"N{random.Next(60)} --> N{random.Next(60)}");
        var chart = Flowchart.Parse(string.Join("\n", lines));
        DiagramLayout.Lay(chart, Measure);
        var watch = Stopwatch.StartNew();
        var s = DiagramLayout.Lay(chart, Measure);
        Assert.Equal(60, s.Nodes.Count);
        // A guard against a layout that hangs, not a benchmark: this worst case (random arrows everywhere) takes about
        // half a second in a release build, and several times that in a debug build beside the rest of the suite.
        Assert.True(watch.ElapsedMilliseconds < 10_000, $"{watch.ElapsedMilliseconds} ms");
        // Random arrows repeat and cross: still the same picture every time.
        Assert.Equal(DiagramSvg.Render(s), DiagramSvg.Render(DiagramLayout.Lay(chart, Measure)));
    }

    [Fact]
    public void The_svg_escapes_every_word_and_draws_every_box()
    {
        var chart = Flowchart.Parse("flowchart LR\nA[\"<script> & 'quotes'\"] -->|a < b| B[Plain]:::red");
        string svg = DiagramSvg.Render(DiagramLayout.Lay(chart, Measure));
        Assert.StartsWith("<svg xmlns=\"http://www.w3.org/2000/svg\"", svg);
        Assert.DoesNotContain("<script>", svg);
        Assert.Contains("&lt;script&gt; &amp; &#39;quotes&#39;", svg);
        Assert.Contains("a &lt; b", svg);
        Assert.Contains("#D93025", svg);
        Assert.Contains("font-family=\"&quot;Helvetica Neue&quot;, &quot;Segoe UI&quot;, Arial, sans-serif\"", svg);
        var doc = System.Xml.Linq.XDocument.Parse(svg);
        Assert.Equal("svg", doc.Root!.Name.LocalName);
    }
}
