using StudyStash.Core.Rich;

namespace StudyStash.Core.Tests;

/// <summary>Reading the Mermaid flowcharts the notes draw: every form the writers use, and a plain reason for
/// anything that can't be drawn.</summary>
public class MermaidTests
{
    static Flowchart P(string source) => Flowchart.Parse(source);

    static FlowNode N(Flowchart f, string id) => f.Node(id) ?? throw new Xunit.Sdk.XunitException("no node " + id);

    [Theory]
    [InlineData("flowchart TD\nA", ChartDirection.TopDown)]
    [InlineData("flowchart TB\nA", ChartDirection.TopDown)]
    [InlineData("graph BT\nA", ChartDirection.BottomUp)]
    [InlineData("flowchart LR\nA", ChartDirection.LeftRight)]
    [InlineData("graph RL;\nA", ChartDirection.RightLeft)]
    [InlineData("graph\nA", ChartDirection.TopDown)]
    [InlineData("flowchart lr\nA", ChartDirection.LeftRight)]
    public void The_header_sets_the_direction(string source, ChartDirection direction) =>
        Assert.Equal(direction, P(source).Direction);

    [Theory]
    [InlineData("A[Box]", NodeShape.Box, "Box")]
    [InlineData("A(Rounded)", NodeShape.Rounded, "Rounded")]
    [InlineData("A([Stadium])", NodeShape.Stadium, "Stadium")]
    [InlineData("A((Circle))", NodeShape.Circle, "Circle")]
    [InlineData("A(((Double)))", NodeShape.Circle, "Double")]
    [InlineData("A{Decision?}", NodeShape.Decision, "Decision?")]
    [InlineData("A{{Hexagon}}", NodeShape.Hexagon, "Hexagon")]
    [InlineData("A[[Subroutine]]", NodeShape.Subroutine, "Subroutine")]
    [InlineData("A[(Database)]", NodeShape.Cylinder, "Database")]
    [InlineData("A>Flag]", NodeShape.Box, "Flag")]
    [InlineData("A[/Lean right/]", NodeShape.Box, "Lean right")]
    [InlineData("A[\\Lean left\\]", NodeShape.Box, "Lean left")]
    [InlineData("A[/Trapezoid\\]", NodeShape.Box, "Trapezoid")]
    [InlineData("A", NodeShape.Box, "A")]
    [InlineData("A [Spaced]", NodeShape.Box, "Spaced")]
    public void Every_shape_reads(string node, NodeShape shape, string label)
    {
        var n = P("flowchart TD\n  " + node).Nodes.Single();
        Assert.Equal("A", n.Id);
        Assert.Equal(shape, n.Shape);
        Assert.Equal(label, n.Label);
    }

    [Fact]
    public void Quoted_labels_hold_brackets_and_unquoted_ones_are_read_leniently()
    {
        var f = P("""
            flowchart LR
              A["Right atrium (RA) [top]"] --> B[Right ventricle (RV)]
              C(Rate (bpm) at rest) --> D{Is it {x}?}
              E["a; b"]; F[f]
            """);
        Assert.Equal("Right atrium (RA) [top]", N(f, "A").Label);
        Assert.Equal("Right ventricle (RV)", N(f, "B").Label);
        Assert.Equal("Rate (bpm) at rest", N(f, "C").Label);
        Assert.Equal(NodeShape.Rounded, N(f, "C").Shape);
        Assert.Equal("Is it {x}?", N(f, "D").Label);
        Assert.Equal("a; b", N(f, "E").Label);
        Assert.Equal(["A", "B", "C", "D", "E", "F"], f.Nodes.Select(n => n.Id));
    }

    [Fact]
    public void Labels_break_lines_and_decode_entities_and_plain_maths()
    {
        var f = P("""
            flowchart TD
              A["Stroke<br>volume"] --> B[Cardiac<br/>output]
              C["He said #quot;hi#quot; #amp; left"]
              D["$\text{CO} = \text{HR} \times \text{SV}$"]
              E["H<sub>2</sub>O and <b>bold</b>"]
              F["Tickets $5 to $10"]
              G["`**Markdown** string`"]
            """);
        Assert.Equal(["Stroke", "volume"], N(f, "A").Lines);
        Assert.Equal(["Cardiac", "output"], N(f, "B").Lines);
        Assert.Equal("He said \"hi\" & left", N(f, "C").Label);
        Assert.Equal("CO = HR × SV", N(f, "D").Label);
        Assert.Equal("H₂O and bold", N(f, "E").Label);
        Assert.Equal("Tickets $5 to $10", N(f, "F").Label);
        Assert.Equal("Markdown string", N(f, "G").Label);
    }

    [Fact]
    public void A_long_label_is_cut_with_an_ellipsis()
    {
        string words = string.Join(" ", Enumerable.Repeat("word", 40));
        var n = P($"flowchart TD\nA[\"{words}\"]").Nodes.Single();
        Assert.Equal(Flowchart.MaxLabel, n.Label.Length);
        Assert.EndsWith("…", n.Label);
    }

    [Fact]
    public void Empty_and_blank_labels_are_empty_boxes()
    {
        var f = P("flowchart TD\nA[\"\"] --> B[\"   \"]");
        Assert.Empty(N(f, "A").Lines);
        Assert.Empty(N(f, "B").Lines);
    }

    [Theory]
    [InlineData("A --> B", EdgeLine.Solid, EdgeEnd.None, EdgeEnd.Arrow)]
    [InlineData("A --- B", EdgeLine.Solid, EdgeEnd.None, EdgeEnd.None)]
    [InlineData("A -.-> B", EdgeLine.Dotted, EdgeEnd.None, EdgeEnd.Arrow)]
    [InlineData("A -.- B", EdgeLine.Dotted, EdgeEnd.None, EdgeEnd.None)]
    [InlineData("A ==> B", EdgeLine.Thick, EdgeEnd.None, EdgeEnd.Arrow)]
    [InlineData("A === B", EdgeLine.Thick, EdgeEnd.None, EdgeEnd.None)]
    [InlineData("A --o B", EdgeLine.Solid, EdgeEnd.None, EdgeEnd.Circle)]
    [InlineData("A --x B", EdgeLine.Solid, EdgeEnd.None, EdgeEnd.Cross)]
    [InlineData("A <--> B", EdgeLine.Solid, EdgeEnd.Arrow, EdgeEnd.Arrow)]
    [InlineData("A o--o B", EdgeLine.Solid, EdgeEnd.Circle, EdgeEnd.Circle)]
    [InlineData("A x--x B", EdgeLine.Solid, EdgeEnd.Cross, EdgeEnd.Cross)]
    [InlineData("A <-.-> B", EdgeLine.Dotted, EdgeEnd.Arrow, EdgeEnd.Arrow)]
    [InlineData("A <==> B", EdgeLine.Thick, EdgeEnd.Arrow, EdgeEnd.Arrow)]
    [InlineData("A ---> B", EdgeLine.Solid, EdgeEnd.None, EdgeEnd.Arrow)]
    [InlineData("A ----> B", EdgeLine.Solid, EdgeEnd.None, EdgeEnd.Arrow)]
    [InlineData("A -..-> B", EdgeLine.Dotted, EdgeEnd.None, EdgeEnd.Arrow)]
    [InlineData("A ==>> B", EdgeLine.Thick, EdgeEnd.None, EdgeEnd.Arrow)]
    [InlineData("A-->B", EdgeLine.Solid, EdgeEnd.None, EdgeEnd.Arrow)]
    [InlineData("A-.->B", EdgeLine.Dotted, EdgeEnd.None, EdgeEnd.Arrow)]
    public void Every_arrow_reads(string statement, EdgeLine line, EdgeEnd start, EdgeEnd end)
    {
        var f = P("flowchart LR\n" + statement);
        var e = f.Edges.Single();
        Assert.Equal(("A", "B"), (e.From, e.To));
        Assert.Equal((line, start, end), (e.Line, e.StartEnd, e.EndEnd));
        Assert.Null(e.Label);
        Assert.Equal(2, f.Nodes.Count);
    }

    [Theory]
    [InlineData("A -->|yes| B", EdgeLine.Solid, EdgeEnd.Arrow)]
    [InlineData("A --> |yes| B", EdgeLine.Solid, EdgeEnd.Arrow)]
    [InlineData("A -->|\"yes\"| B", EdgeLine.Solid, EdgeEnd.Arrow)]
    [InlineData("A ---|yes| B", EdgeLine.Solid, EdgeEnd.None)]
    [InlineData("A -- yes --> B", EdgeLine.Solid, EdgeEnd.Arrow)]
    [InlineData("A -- yes --- B", EdgeLine.Solid, EdgeEnd.None)]
    [InlineData("A -. yes .-> B", EdgeLine.Dotted, EdgeEnd.Arrow)]
    [InlineData("A -.->|yes| B", EdgeLine.Dotted, EdgeEnd.Arrow)]
    [InlineData("A == yes ==> B", EdgeLine.Thick, EdgeEnd.Arrow)]
    [InlineData("A ==>|yes| B", EdgeLine.Thick, EdgeEnd.Arrow)]
    public void Arrow_words_read_in_every_form(string statement, EdgeLine line, EdgeEnd end)
    {
        var e = P("flowchart TD\n" + statement).Edges.Single();
        Assert.Equal(("A", "B", "yes"), (e.From, e.To, e.Label));
        Assert.Equal((line, end), (e.Line, e.EndEnd));
    }

    [Fact]
    public void Arrow_words_keep_pipes_inside_quotes_and_break_lines()
    {
        var f = P("flowchart TD\nA -->|\"a | b\"| B\nB -->|two<br>lines| C");
        Assert.Equal("a | b", f.Edges[0].Label);
        Assert.Equal("two\nlines", f.Edges[1].Label);
    }

    [Fact]
    public void Chains_and_fans_make_every_arrow()
    {
        var f = P("flowchart LR\nA --> B --> C\nD & E --> F & G");
        Assert.Equal(
            ["A>B", "B>C", "D>F", "D>G", "E>F", "E>G"],
            f.Edges.Select(e => e.From + ">" + e.To));
    }

    [Fact]
    public void A_chain_carries_each_arrows_own_words()
    {
        var f = P("""
            flowchart LR
              RA["Right atrium"]:::blue -->|tricuspid valve| RV["Right ventricle"]:::blue -->|pulmonary valve| Lungs(("Lungs"))
            """);
        Assert.Equal(["tricuspid valve", "pulmonary valve"], f.Edges.Select(e => e.Label));
        Assert.Equal(Tone.Blue, N(f, "RV").Tone);
        Assert.Equal(NodeShape.Circle, N(f, "Lungs").Shape);
    }

    [Fact]
    public void A_later_declaration_gives_a_box_its_words_and_shape()
    {
        var f = P("flowchart TD\nA --> B\nB{Why?}\nA[Start]");
        Assert.Equal(("Start", NodeShape.Box), (N(f, "A").Label, N(f, "A").Shape));
        Assert.Equal(("Why?", NodeShape.Decision), (N(f, "B").Label, N(f, "B").Shape));
    }

    [Fact]
    public void Tones_come_from_triple_colons_and_class_lines()
    {
        var f = P("""
            flowchart TD
              A:::accent --> B[b]:::Red
              C --> D & E
              class C,D green
              class E purple
              F:::sparkly
              classDef red fill:#f00
              class F nonsense
              G[g]:::amber --> H[h]:::blue
            """);
        Assert.Equal(Tone.Accent, N(f, "A").Tone);
        Assert.Equal(Tone.Red, N(f, "B").Tone);
        Assert.Equal(Tone.Green, N(f, "C").Tone);
        Assert.Equal(Tone.Green, N(f, "D").Tone);
        Assert.Equal(Tone.Purple, N(f, "E").Tone);
        Assert.Equal(Tone.None, N(f, "F").Tone);
        Assert.Equal(Tone.Amber, N(f, "G").Tone);
        Assert.Equal(Tone.Blue, N(f, "H").Tone);
    }

    [Fact]
    public void Subgraphs_read_in_every_form_and_nest()
    {
        var f = P("""
            flowchart TD
              subgraph assess [Assessment]
                A[Collect data] --> B[Analyse]
                subgraph inner ["Vital signs"]
                  V[Pulse]
                end
              end
              subgraph "Planning and care"
                C[Plan]
              end
              subgraph Evaluate
                direction LR
                D[Evaluate]
              end
              subgraph two words here
                E[e]
              end
              B --> C --> D --> E
            """);
        Assert.Equal(["assess", "inner", "group3", "Evaluate", "group5"], f.Groups.Select(g => g.Id));
        Assert.Equal(["Assessment", "Vital signs", "Planning and care", "Evaluate", "two words here"], f.Groups.Select(g => g.Title));
        Assert.Equal(["A", "B"], f.Groups[0].Members);
        Assert.Equal(["V"], f.Groups[1].Members);
        Assert.Equal("assess", f.Groups[1].Parent);
        Assert.Null(f.Groups[0].Parent);
        Assert.Equal(["C"], f.Groups[2].Members);
        Assert.Equal(["D"], f.Groups[3].Members);
    }

    [Fact]
    public void A_box_first_named_outside_moves_into_the_group_that_lists_it()
    {
        var f = P("flowchart LR\nA --> B --> C\nsubgraph G [Group]\nB\nC\nend");
        Assert.Equal(["B", "C"], f.Groups.Single().Members);
        Assert.Equal(["A", "B", "C"], f.Nodes.Select(n => n.Id));
    }

    [Fact]
    public void An_arrow_to_a_group_points_at_its_first_box_and_empty_groups_go()
    {
        var f = P("flowchart LR\nsubgraph G [Heart]\nH1[Atria] --> H2[Ventricles]\nend\nsubgraph E [Empty]\nend\nLungs --> G");
        Assert.DoesNotContain(f.Nodes, n => n.Id == "G");
        Assert.Contains(f.Edges, e => e.From == "Lungs" && e.To == "H1");
        Assert.Equal(["G"], f.Groups.Select(g => g.Id));
    }

    [Fact]
    public void Nesting_deeper_than_two_is_refused()
    {
        var e = Assert.Throws<MermaidException>(() => P("flowchart TD\nsubgraph a\nsubgraph b\nsubgraph c\nX\nend\nend\nend"));
        Assert.Equal(4, e.Line);
    }

    [Fact]
    public void Unclosed_groups_and_stray_ends_are_refused()
    {
        Assert.Throws<MermaidException>(() => P("flowchart TD\nsubgraph a\nX"));
        Assert.Throws<MermaidException>(() => P("flowchart TD\nX\nend"));
    }

    [Fact]
    public void Styling_clicks_comments_and_accessibility_lines_are_ignored()
    {
        var f = P("""
            %%{init: {"theme": "dark"}}%%
            flowchart TD
              %% a comment
              A[Start] --> B[Stop] %% trailing
              classDef hot fill:#f96,stroke:#333
              style A fill:#f9f,stroke:#333,stroke-width:4px
              linkStyle 0 stroke:#ff3,stroke-width:4px
              click A "https://example.com" "Go"
              click B callback
              accTitle: A title
              accDescr: A description
              accDescr {
                many lines
              }
            """);
        Assert.Equal(["A", "B"], f.Nodes.Select(n => n.Id));
        Assert.Single(f.Edges);
    }

    [Fact]
    public void Statements_split_on_semicolons_and_front_matter_gives_a_title()
    {
        var f = P("---\ntitle: The cardiac cycle\n---\ngraph LR; A-->B; B-->C;");
        Assert.Equal("The cardiac cycle", f.Title);
        Assert.Equal(2, f.Edges.Count);
        Assert.Equal(ChartDirection.LeftRight, f.Direction);
    }

    [Fact]
    public void Ids_can_hold_dashes_digits_and_other_scripts()
    {
        var f = P("flowchart LR\nstep-1 --> step-2\n1 --> 2\n心脏 --> 肺");
        Assert.Equal(["step-1", "step-2", "1", "2", "心脏", "肺"], f.Nodes.Select(n => n.Id));
    }

    [Fact]
    public void Self_loops_and_repeated_arrows_are_kept()
    {
        var f = P("flowchart LR\nA --> A\nA --> B\nA --> B");
        Assert.Equal(3, f.Edges.Count);
    }

    [Fact]
    public void Labels_come_in_arrow_order_then_loose_boxes_then_group_titles()
    {
        var f = P("""
            flowchart TD
              Z[Alone]
              subgraph g [Assessment]
                A[Assess pain]
              end
              A --> B{Score above 4?}
              B -->|yes| C[Give analgesic]
              B -->|no| D[Reposition]
            """);
        Assert.Equal(["Assess pain", "Score above 4?", "yes", "Give analgesic", "no", "Reposition", "Alone", "Assessment"], f.Labels());
    }

    public static TheoryData<string> Sources =>
    [
        """
        flowchart LR
          A([Atrial systole]):::accent --> B[Isovolumetric contraction] --> C[Ventricular ejection] --> D[Isovolumetric relaxation] --> E[Ventricular filling] --> A
        """,
        """
        flowchart TD
          A[Assess pain on a 0 to 10 scale] --> B{Score above 4?}
          B -->|yes| C[Give the prescribed analgesic]
          B -->|no| D[Reposition and use non-drug comfort measures]
          C --> E([Reassess in 30 to 60 minutes])
          D --> E
          E -.->|still in pain| A
        """,
        """
        ---
        title: Nursing process
        ---
        graph TB
          subgraph plan [Plan & act]
            P["Plan #quot;care#quot; C#; notes"] ==> I{{Implement}}
            subgraph inner
              X[(Chart)]
            end
          end
          A[[Assess]] <--> D>Diagnose] --o P
          I -.- E((Evaluate)) --x A
          E o--o A
          D -- words here --- I
          Q["a<br>b"]:::purple
          class A red
        """,
        "flowchart RL\n  A[\"&lt;b&gt;tag&lt;/b&gt; &amp;amp; #35;1 &#39;q&#39;\"] --> B",
    ];

    [Theory]
    [MemberData(nameof(Sources))]
    public void ToSource_round_trips(string source)
    {
        var first = P(source);
        string canonical = first.ToSource();
        var second = P(canonical);
        Assert.Equal(canonical, second.ToSource());
        Assert.Equal(first.Direction, second.Direction);
        Assert.Equal(first.Title, second.Title);
        Assert.Equal(first.Nodes.Select(Describe), second.Nodes.Select(Describe));
        Assert.Equal(first.Edges, second.Edges);
        Assert.Equal(first.Groups.Select(g => (g.Id, g.Title, string.Join(",", g.Members), g.Parent)), second.Groups.Select(g => (g.Id, g.Title, string.Join(",", g.Members), g.Parent)));
        Assert.Equal(first.Labels(), second.Labels());
    }

    static string Describe(FlowNode n) => $"{n.Id}|{string.Join("/", n.Lines)}|{n.Shape}|{n.Tone}";

    [Fact]
    public void ToSource_quotes_every_label()
    {
        string source = P("flowchart LR\nA[Right atrium (RA)] -->|tricuspid valve| B(Right ventricle)").ToSource();
        Assert.Equal("flowchart LR\n  A[\"Right atrium (RA)\"]\n  B(\"Right ventricle\")\n  A -->|\"tricuspid valve\"| B\n", source);
    }

    [Theory]
    [InlineData("sequenceDiagram\n  Alice->>Bob: Hi", "Study Stash draws flowcharts; this is a sequence diagram.")]
    [InlineData("classDiagram\n  Animal <|-- Duck", "Study Stash draws flowcharts; this is a class diagram.")]
    [InlineData("stateDiagram-v2\n  [*] --> Still", "Study Stash draws flowcharts; this is a state diagram.")]
    [InlineData("erDiagram\n  A ||--o{ B : has", "Study Stash draws flowcharts; this is an entity-relationship diagram.")]
    [InlineData("mindmap\n  root", "Study Stash draws flowcharts; this is a mind map.")]
    [InlineData("pie title Pets\n  \"Dogs\" : 386", "Study Stash draws flowcharts; this is a pie chart.")]
    [InlineData("gantt\n  title A", "Study Stash draws flowcharts; this is a Gantt chart.")]
    [InlineData("timeline\n  title A", "Study Stash draws flowcharts; this is a timeline.")]
    public void Other_diagram_types_say_what_they_are(string source, string message)
    {
        var e = Assert.Throws<MermaidException>(() => P(source));
        Assert.Equal(message, e.Message);
        Assert.Equal(1, e.Line);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   \n  ")]
    [InlineData("hello world")]
    [InlineData("flowchart TD")]
    [InlineData("flowchart TD\nA -->")]
    [InlineData("flowchart TD\nA[unclosed")]
    [InlineData("flowchart TD\nA -->|unclosed B")]
    [InlineData("flowchart TD\nA --> B --> ")]
    [InlineData("flowchart TD\n--> B")]
    public void Garbage_is_a_plain_reason(string source)
    {
        var e = Assert.Throws<MermaidException>(() => P(source));
        Assert.False(string.IsNullOrWhiteSpace(e.Message));
    }

    [Fact]
    public void An_unreadable_line_is_named()
    {
        var e = Assert.Throws<MermaidException>(() => P("flowchart TD\nA --> B\nA -->"));
        Assert.Equal(3, e.Line);
        Assert.Contains("line 3", e.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void An_unclosed_box_is_quoted_from_its_id()
    {
        var e = Assert.Throws<MermaidException>(() => P("flowchart TD\n  A[Check the order] --> B[Give the dose\n  B --> C"));
        Assert.Equal("Line 2 has a box that isn't closed: “B[Give the dose”.", e.Message);
    }

    [Fact]
    public void Caps_are_refused_with_a_reason()
    {
        string nodes = "flowchart TD\n" + string.Join("\n", Enumerable.Range(0, 61).Select(i => $"N{i}[Box {i}]"));
        Assert.Contains("60 boxes", Assert.Throws<MermaidException>(() => P(nodes)).Message);
        string edges = "flowchart TD\n" + string.Join("\n", Enumerable.Range(0, 121).Select(i => $"A --> B{i % 10}"));
        Assert.Contains("120 arrows", Assert.Throws<MermaidException>(() => P(edges)).Message);
        string big = "flowchart TD\n" + new string('%', Flowchart.MaxSource);
        Assert.Contains("8 KB", Assert.Throws<MermaidException>(() => P(big)).Message);
        var sixty = P("flowchart TD\n" + string.Join("\n", Enumerable.Range(0, 60).Select(i => $"N{i}")));
        Assert.Equal(60, sixty.Nodes.Count);
    }

    [Fact]
    public void Nothing_but_a_MermaidException_ever_escapes()
    {
        var random = new Random(42);
        const string alphabet = "ABab01 _-.=>|<ox&:;\"'()[]{}/\\#%$\n\t~*^`";
        string[] seeds =
        [
            "flowchart TD\nA[Start] --> B{Ok?}\nB -->|yes| C((Done))\nB -. no .-> A\nsubgraph g [G]\nC\nend\nclass A red",
            "graph LR; A-->B; B==>C; C-.-D; D<-->E; E o--o F; F x--x A",
        ];
        for (int round = 0; round < 220; round++)
        {
            string source;
            if (round % 2 == 0)
            {
                var chars = new char[random.Next(0, 120)];
                for (int k = 0; k < chars.Length; k++) chars[k] = alphabet[random.Next(alphabet.Length)];
                source = (round % 4 == 0 ? "flowchart TD\n" : "") + new string(chars);
            }
            else
            {
                // A good chart, garbled: characters dropped, doubled or swapped.
                var sb = new System.Text.StringBuilder(seeds[round % seeds.Length]);
                for (int k = 0; k < 6; k++)
                {
                    int at = random.Next(sb.Length);
                    switch (random.Next(3))
                    {
                        case 0: sb.Remove(at, 1); break;
                        case 1: sb.Insert(at, sb[at]); break;
                        default: sb[at] = alphabet[random.Next(alphabet.Length)]; break;
                    }
                }
                source = sb.ToString();
            }
            Flowchart? f = null;
            try
            {
                f = P(source);
            }
            catch (MermaidException)
            {
            }
            // Whatever reads, reads again from its own source, the same.
            if (f is not null) Assert.Equal(f.ToSource(), P(f.ToSource()).ToSource());
        }
    }
}
