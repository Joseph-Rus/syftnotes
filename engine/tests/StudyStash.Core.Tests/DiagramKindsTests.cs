using StudyStash.Core.Rich;

namespace StudyStash.Core.Tests;

/// <summary>
/// The kinds besides flowcharts (state and sequence diagrams, timelines, mind maps), a box's smaller words, and big
/// charts in groups: each reads into the right boxes and arrows, writes back as itself, and lays out with nothing on
/// top of anything else, the same every time.
/// </summary>
public class DiagramKindsTests
{
    static double Measure(string text, double size, bool bold) => new System.Globalization.StringInfo(text).LengthInTextElements * size * (bold ? 0.58 : 0.55);

    internal const string Automaton = """
        stateDiagram-v2
          direction LR
          [*] --> q0
          q0 --> q1 : a
          q1 --> q1 : b
          q1 --> q2 : a
          q2 --> q0 : b
          class q2 accept
        """;

    internal const string Process = """
        stateDiagram-v2
          [*] --> New
          New --> Ready : admitted
          Ready --> Running : scheduler dispatch
          Running --> Ready : interrupt
          Running --> Waiting : I/O wait
          Waiting --> Ready : I/O done
          Running --> [*] : exit
          Running : on the CPU
          note right of Waiting : not using the CPU
        """;

    internal const string Handshake = """
        sequenceDiagram
          autonumber
          actor U as Student
          participant C as Browser
          participant S as Web server
          U->>C: types the URL
          C->>S: GET /orders
          alt logged in
            S-->>C: 200 OK
          else not logged in
            S-->>C: 302 to /login
          end
          loop every second
            C->>C: redraw
          end
          Note over C,S: the cookie carries the session
        """;

    internal const string History = """
        timeline
          title Neural networks
          section Early
            1958 : Perceptron
            1969 : Its limits shown : funding dries up
          section Deep learning
            2012 : AlexNet wins ImageNet
        """;

    internal const string Map = """
        mindmap
          root((Data warehouse))
            Traits:::blue
              Subject-oriented
              Integrated
            Builds
              Inmon top-down
              Kimball bottom-up
        """;

    internal const string Compiler = """
        flowchart LR
          subgraph FE ["Front end"]
            SRC["Source"] --> SC["Scanner<br><small>characters to tokens</small>"] -->|tokens| PA["Parser"] --> SEM["Semantic analysis"]
          end
          subgraph MID ["Middle"]
            IR["IR generator"] --> OPT["Optimizer"]
          end
          subgraph BE ["Back end"]
            CG["Code generator"] --> ASM[/"Assembly"/] --> EXE["Executable"]
          end
          subgraph ERR ["Errors"]
            LE["Lexical"]:::red
            SE["Syntax"]:::red
          end
          SEM -->|annotated tree| IR
          OPT --> CG
          SC -.-> LE
          PA -.-> SE
        """;

    public static TheoryData<string> Kinds => new() { Automaton, Process, Handshake, History, Map, Compiler };

    [Theory]
    [MemberData(nameof(Kinds))]
    public void Each_kind_reads_and_writes_back_as_itself(string source)
    {
        var first = Flowchart.Parse(source);
        string canonical = first.ToSource();
        var second = Flowchart.Parse(canonical);
        Assert.Equal(first.Form, second.Form);
        Assert.Equal(canonical, second.ToSource());
        Assert.Equal(first.Labels(), second.Labels());
        static IEnumerable<string> Boxes(Flowchart f) => f.Nodes.Select(n => $"{n.Label}|{n.Shape}|{n.Tone}|{n.Role}|{string.Join("/", n.Detail)}").Order();
        Assert.Equal(Boxes(first), Boxes(second));
        Assert.Equal(first.Edges.Select(e => (e.Label, e.Line, e.EndEnd)), second.Edges.Select(e => (e.Label, e.Line, e.EndEnd)));
    }

    [Fact]
    public void A_state_diagram_reads_as_an_automaton_or_as_states()
    {
        var dfa = Flowchart.Parse(Automaton);
        Assert.Equal(ChartForm.State, dfa.Form);
        Assert.Equal(ChartDirection.LeftRight, dfa.Direction);
        Assert.Equal(NodeRole.Start, dfa.Nodes[0].Role);
        Assert.Equal([NodeShape.Circle, NodeShape.Circle, NodeShape.DoubleCircle], dfa.Nodes.Skip(1).Select(n => n.Shape));
        Assert.Contains(dfa.Edges, e => e.From == "q1" && e.To == "q1" && e.Label == "b");

        var states = Flowchart.Parse(Process);
        var running = states.Node("Running")!;
        Assert.Equal(NodeShape.Rounded, running.Shape);
        Assert.Equal(["on the CPU"], running.Detail);
        Assert.Contains(states.Nodes, n => n.Role == NodeRole.End);
        var note = Assert.Single(states.Nodes, n => n.Role == NodeRole.Note);
        Assert.Equal("not using the CPU", note.Label);
        Assert.Contains(states.Edges, e => e.From == "Waiting" && e.To == note.Id && e.Line == EdgeLine.Dotted);
        Assert.Equal(["admitted", "New"], states.Labels().Take(2).Reverse());
    }

    [Fact]
    public void A_sequence_diagram_keeps_its_rows_and_blocks_in_order()
    {
        var seq = Flowchart.Parse(Handshake);
        Assert.Equal(ChartForm.Sequence, seq.Form);
        Assert.True(seq.Numbered);
        Assert.Equal(["Student", "Browser", "Web server"], seq.Nodes.Where(n => n.Role != NodeRole.Note).Select(n => n.Label));
        Assert.Equal(NodeRole.Actor, seq.Node("U")!.Role);
        Assert.Equal([StepKind.Message, StepKind.Message, StepKind.Open, StepKind.Message, StepKind.Else, StepKind.Message, StepKind.Close,
            StepKind.Open, StepKind.Message, StepKind.Close, StepKind.Note], seq.Steps.Select(s => s.Kind));
        var reply = seq.Edges[seq.Steps[3].Index];
        Assert.Equal(("S", "C", "200 OK", EdgeLine.Dotted), (reply.From, reply.To, reply.Label, reply.Line));
        Assert.Equal(["C", "S"], seq.Steps[^1].Over);
        Assert.Equal("the cookie carries the session", seq.Labels()[^1]);
    }

    [Fact]
    public void A_timeline_and_a_mind_map_read_their_structure()
    {
        var t = Flowchart.Parse(History);
        Assert.Equal(("Neural networks", ChartForm.Timeline), (t.Title, t.Form));
        Assert.Equal(["1958", "1969", "2012"], t.Nodes.Where(n => n.Role == NodeRole.Period).Select(n => n.Label));
        Assert.Equal(2, t.Edges.Count(e => e.From == t.Nodes.First(n => n.Label == "1969").Id));
        Assert.Equal(["Early", "Deep learning"], t.Groups.Select(g => g.Title));

        var m = Flowchart.Parse(Map);
        Assert.Equal(ChartForm.Mindmap, m.Form);
        Assert.Equal(("Data warehouse", NodeShape.Circle, NodeRole.Root), (m.Nodes[0].Label, m.Nodes[0].Shape, m.Nodes[0].Role));
        Assert.Equal(Tone.Blue, m.Nodes.Single(n => n.Label == "Traits").Tone);
        Assert.Equal("Builds", m.Node(m.Edges.Single(e => m.Node(e.To)!.Label == "Kimball bottom-up").From)!.Label);
        Assert.Contains("one root", Assert.Throws<MermaidException>(() => Flowchart.Parse("mindmap\n  A\n    B\n  C")).Message);
    }

    [Fact]
    public void A_box_has_smaller_words_and_slanted_boxes_lean_the_way_they_were_written()
    {
        var chart = Flowchart.Parse("flowchart LR\n  G[\"Glycolysis<br><small>cytoplasm<br>2 ATP</small>\"] --> I[/Input/] --> T[/Manual\\]");
        Assert.Equal(["Glycolysis"], chart.Nodes[0].Lines);
        Assert.Equal(["cytoplasm", "2 ATP"], chart.Nodes[0].Detail);
        Assert.Equal(["Glycolysis", "cytoplasm 2 ATP", "Input", "Manual"], chart.Labels());
        Assert.Equal([NodeShape.Box, NodeShape.Parallelogram, NodeShape.Trapezoid], chart.Nodes.Select(n => n.Shape));
        var scene = DiagramLayout.Lay(chart, Measure);
        var g = scene.Nodes[0];
        Assert.Equal(2, g.DetailLines);
        Assert.Equal(["Glycolysis", "cytoplasm", "2 ATP"], g.Lines);
        Assert.Contains("font-size=\"11.5\"", DiagramSvg.Render(scene));
    }

    [Fact]
    public void Unclosed_blocks_and_stray_lines_in_the_new_kinds_are_plain_reasons()
    {
        foreach (string bad in new[]
        {
            "sequenceDiagram\n  A->>B: hi\n  loop forever\n    A->>B: again",
            "sequenceDiagram\n  end",
            "sequenceDiagram\n  A hello B",
            "stateDiagram-v2\n  state Busy {\n    A --> B",
            "stateDiagram-v2\n  }",
            "timeline\n  : an event with no period",
        })
            Assert.False(string.IsNullOrWhiteSpace(Assert.Throws<MermaidException>(() => Flowchart.Parse(bad)).Message));
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public void Each_kind_lays_out_with_nothing_on_anything_else_the_same_every_time(string source)
    {
        var chart = Flowchart.Parse(source);
        var s = DiagramLayout.Lay(chart, Measure);
        var all = new Box(0, 0, s.Width, s.Height);
        for (int i = 0; i < s.Nodes.Count; i++)
        {
            Assert.True(all.Contains(s.Nodes[i].Box), $"{s.Nodes[i].Id} is outside the picture");
            for (int j = i + 1; j < s.Nodes.Count; j++)
                Assert.False(s.Nodes[i].Box.Intersects(s.Nodes[j].Box), $"{s.Nodes[i].Id} overlaps {s.Nodes[j].Id}");
        }
        var worded = s.Edges.Where(e => e.LabelLines.Count > 0).ToList();
        foreach (var e in worded)
        {
            Assert.True(all.Contains(e.LabelBox), $"the words “{string.Join(" ", e.LabelLines)}” are outside the picture");
            Assert.DoesNotContain(s.Nodes, n => n.Box.Intersects(e.LabelBox));
        }
        foreach (var e in s.Edges) Assert.True(all.Contains(e.Path[0].A) && all.Contains(e.Tip), $"{e.From}→{e.To} runs outside the picture");
        // The scene's first arrows are the chart's own, in its order.
        Assert.True(s.Edges.Count >= chart.Edges.Count);
        Assert.Equal(chart.Edges.Select(e => (e.From, e.To)), s.Edges.Take(chart.Edges.Count).Select(e => (e.From, e.To)));
        foreach (var g in s.Groups) Assert.True(all.Contains(g.Box) && g.Box.Contains(g.TitleBox), $"{g.Title} is outside the picture, or its title outside it");
        Assert.Equal(DiagramSvg.Render(s), DiagramSvg.Render(DiagramLayout.Lay(chart, Measure)));
        var estimate = DiagramLayout.Estimate(chart);
        Assert.InRange(estimate.Width / s.Width, 0.5, 2);
    }

    [Fact]
    public void A_big_chart_in_groups_is_columns_of_its_groups_and_folds_into_them()
    {
        var chart = Flowchart.Parse(Compiler);
        Assert.Equal(SceneKind.Grouped, DiagramLayout.Kind(chart));
        var s = DiagramLayout.Lay(chart, Measure);
        var box = s.Groups.ToDictionary(g => g.Id, g => g.Box);
        // Each group holds its own boxes; the columns run left to right in the flow's order, their tops in line.
        foreach (var g in chart.Groups)
            Assert.All(g.Members, m => Assert.True(box[g.Id].Contains(s.Nodes.Single(n => n.Id == m).Box), $"{m} is outside {g.Title}"));
        Assert.True(box["FE"].Right < box["MID"].X && box["MID"].Right < box["BE"].X);
        Assert.Equal(box["FE"].Y, box["MID"].Y, 1);
        // Inside a column, its boxes run down; no arrow runs through a group's title.
        var byId = s.Nodes.ToDictionary(n => n.Id);
        Assert.True(byId["SRC"].Box.Bottom < byId["SC"].Box.Y);
        foreach (var e in s.Edges)
            foreach (var g in s.Groups)
                Assert.True(DiagramLayout.Distance(g.TitleBox, DiagramLayout.Flatten(e)) > 0, $"{e.From}→{e.To} runs across {g.Title}'s title");

        var folded = chart.Folded()!;
        Assert.Equal(["Front end", "Middle", "Back end", "Errors"], folded.Nodes.Select(n => n.Label));
        Assert.Equal([("FE", "MID", "annotated tree"), ("MID", "BE", null), ("FE", "ERR", null)], folded.Edges.Select(e => (e.From, e.To, e.Label)));
        Assert.Null(Flowchart.Parse(Map).Folded());
    }

    [Fact]
    public void Nothing_but_a_MermaidException_ever_escapes_the_new_kinds_and_whatever_reads_lays_out()
    {
        var random = new Random(7);
        const string alphabet = "ABab01 _-.=>|<ox&:;\"'()[]{}/\\#%$*\n\t~+";
        string[] seeds = [Automaton, Process, Handshake, History, Map];
        for (int round = 0; round < 250; round++)
        {
            var sb = new System.Text.StringBuilder(seeds[round % seeds.Length]);
            for (int k = 0; k < 5; k++)
            {
                int at = random.Next(sb.Length);
                switch (random.Next(3))
                {
                    case 0: sb.Remove(at, 1); break;
                    case 1: sb.Insert(at, sb[at]); break;
                    default: sb[at] = alphabet[random.Next(alphabet.Length)]; break;
                }
            }
            Flowchart? f = null;
            try
            {
                f = Flowchart.Parse(sb.ToString());
            }
            catch (MermaidException)
            {
            }
            if (f is null) continue;
            Assert.Equal(f.ToSource(), Flowchart.Parse(f.ToSource()).ToSource());
            var scene = DiagramLayout.Lay(f, Measure);
            Assert.True(scene.Width > 0 && scene.Height > 0);
        }
    }
}
