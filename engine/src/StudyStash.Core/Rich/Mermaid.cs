using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace StudyStash.Core.Rich;

/// <summary>Which way a flowchart reads: top to bottom (the default), bottom to top, left to right, right to left.</summary>
public enum ChartDirection { TopDown, BottomUp, LeftRight, RightLeft }

/// <summary>A box's outline. A parallelogram is an input or output, a trapezoid a manual step, a double circle an
/// accepting state (or a state diagram's end); Mermaid's rarer shapes (flags) are drawn as plain boxes.</summary>
public enum NodeShape { Box, Rounded, Stadium, Circle, Decision, Hexagon, Subroutine, Cylinder, Parallelogram, Trapezoid, DoubleCircle }

/// <summary>A box's colour, when the colour means something (oxygen-rich red, oxygen-poor blue).</summary>
public enum Tone { None, Accent, Red, Blue, Green, Amber, Purple }

/// <summary>How an arrow's line is drawn.</summary>
public enum EdgeLine { Solid, Dotted, Thick }

/// <summary>What sits at an end of an arrow's line.</summary>
public enum EdgeEnd { None, Arrow, Circle, Cross }

/// <summary>
/// What kind of diagram a chart is, from its first line: a flowchart (a process, a decision, a pathway, a concept map),
/// a state diagram (states and the events between them, an automaton), a sequence diagram (who sends what to whom, in
/// order), a timeline (periods and what happened in each) or a mind map (a topic broken into its themes). Every kind
/// is read into the same boxes, arrows and groups, so search, export and drawing treat them alike.
/// </summary>
public enum ChartForm { Flowchart, State, Sequence, Timeline, Mindmap }

/// <summary>What a box is in its kind of diagram, where that changes how it's laid out or drawn: a state diagram's
/// start and end and its fork and join bars, a note, a sequence diagram's actor, a timeline's period and its events,
/// a mind map's root.</summary>
public enum NodeRole { Plain, Start, End, Note, Actor, Period, Event, Root, Bar }

/// <summary>A box: its id in the source, its words (one entry per line the writer broke), its outline and colour.
/// <see cref="Detail"/> is a second, smaller line or two of words under them (a step's where or what); <see
/// cref="Role"/> what it is in its kind of diagram.</summary>
public sealed record FlowNode(string Id, IReadOnlyList<string> Lines, NodeShape Shape, Tone Tone)
{
    /// <summary>The box's words on one line.</summary>
    public string Label => string.Join(" ", Lines);

    /// <summary>The smaller words under the box's own, one entry per line (none for most boxes).</summary>
    public IReadOnlyList<string> Detail { get; init; } = [];

    public NodeRole Role { get; init; }
}

/// <summary>What a row of a sequence diagram is: a message (an arrow), a note, or the start, divider or end of a
/// block (a loop, alternatives, an option, things in parallel).</summary>
public enum StepKind { Message, Note, Open, Else, Close }

/// <summary>
/// One row of a sequence diagram, in order. A message is <see cref="Flowchart.Edges"/>[<see cref="Index"/>]; a note
/// is the box <see cref="Text"/> names, <see cref="Where"/> its side ("left of", "right of", "over") of the
/// participants <see cref="Over"/>; a block's start has its keyword in <see cref="Where"/> (loop, alt, opt, par,
/// critical, break) and its words in <see cref="Text"/>, and a divider its keyword (else, and, option) and words.
/// </summary>
public sealed record SeqStep(StepKind Kind, string Text = "", int Index = -1, string Where = "")
{
    public IReadOnlyList<string> Over { get; init; } = [];
}

/// <summary>An arrow (or a plain line) from one box to another, with its words, if any.</summary>
public sealed record FlowEdge(string From, string To, string? Label, EdgeLine Line, EdgeEnd StartEnd, EdgeEnd EndEnd);

/// <summary>A subgraph: a titled group of boxes, possibly inside another group.</summary>
public sealed record FlowGroup(string Id, string Title, IReadOnlyList<string> Members, string? Parent);

/// <summary>Why a diagram can't be drawn, in plain English, and the line of its source that says so (0: the whole).</summary>
public sealed class MermaidException(string message, int line) : Exception(message)
{
    public int Line { get; } = line;
}

/// <summary>
/// A Mermaid flowchart, read leniently: the subset the notes' writers use (boxes of every common shape, arrows of
/// every kind with or without words, chains and fans, subgraphs, colour classes). Anything else — another diagram
/// type, a line it can't read, a diagram too big to draw well — is a <see cref="MermaidException"/> saying why.
/// </summary>
public sealed class Flowchart
{
    public const int MaxSource = 8 * 1024, MaxNodes = 60, MaxEdges = 120, MaxLabel = 80;

    public ChartDirection Direction { get; }
    public string? Title { get; }
    public IReadOnlyList<FlowNode> Nodes { get; }
    public IReadOnlyList<FlowEdge> Edges { get; }
    public IReadOnlyList<FlowGroup> Groups { get; }

    public Flowchart(ChartDirection direction, string? title, IReadOnlyList<FlowNode> nodes, IReadOnlyList<FlowEdge> edges, IReadOnlyList<FlowGroup> groups)
    {
        Direction = direction;
        Title = title;
        Nodes = nodes;
        Edges = edges;
        Groups = groups;
    }

    /// <summary>Reads a diagram (a flowchart, a state or sequence diagram, a timeline or a mind map); anything it
    /// can't draw throws a <see cref="MermaidException"/>, never anything else.</summary>
    public static Flowchart Parse(string source) => Mermaid.Parse(source);

    /// <summary>What kind of diagram this is (a flowchart unless its first line said otherwise).</summary>
    public ChartForm Form { get; init; }

    /// <summary>A sequence diagram's rows in order (its messages, notes and blocks); empty for every other kind.</summary>
    public IReadOnlyList<SeqStep> Steps { get; init; } = [];

    /// <summary>A sequence diagram whose messages are numbered (Mermaid's autonumber).</summary>
    public bool Numbered { get; init; }

    /// <summary>The same flowchart, laid out another way (the app turns a too-wide left-to-right chart top-down).</summary>
    public Flowchart WithDirection(ChartDirection direction) =>
        new(direction, Title, Nodes, Edges, Groups) { Form = Form, Steps = Steps, Numbered = Numbered };

    public FlowNode? Node(string id) => Nodes.FirstOrDefault(n => n.Id == id);

    /// <summary>
    /// The chart's words in reading order, for search and screen readers: each arrow's boxes and words in the order
    /// the arrows were written (a box once, its smaller words after its own), then any box no arrow touches, then the
    /// groups' titles (a sequence diagram's blocks, where they open).
    /// </summary>
    public IReadOnlyList<string> Labels()
    {
        var said = new HashSet<string>();
        var words = new List<string>();
        void Box(string id)
        {
            if (!said.Add(id) || Node(id) is not { } n) return;
            if (n.Label.Length > 0) words.Add(n.Label);
            if (n.Detail.Count > 0) words.Add(string.Join(" ", n.Detail));
        }
        if (Form == ChartForm.Sequence)
        {
            foreach (var n in Nodes.Where(n => n.Role != NodeRole.Note)) Box(n.Id);
            foreach (var step in Steps)
            {
                if (step.Kind == StepKind.Message && step.Index >= 0 && step.Index < Edges.Count && Edges[step.Index].Label is { Length: > 0 } said1)
                    words.Add(said1.Replace('\n', ' '));
                else if (step.Kind == StepKind.Note) Box(step.Text);
                else if (step.Kind is StepKind.Open or StepKind.Else && step.Text.Length > 0) words.Add(step.Text);
            }
            return words;
        }
        foreach (var e in Edges)
        {
            Box(e.From);
            if (e.Label is { Length: > 0 } l) words.Add(l.Replace('\n', ' '));
            Box(e.To);
        }
        foreach (var n in Nodes) Box(n.Id);
        foreach (var g in Groups) if (g.Title.Length > 0) words.Add(g.Title);
        return words;
    }

    /// <summary>
    /// The overview of a chart whose boxes are in groups: each outermost group as one box (its title, in its place),
    /// the boxes in no group as they are, and one arrow for each pair of them an arrow joined (its words kept when
    /// every arrow between them said the same). What a folded chart shows, and what the designer is held to: a big
    /// diagram's groups must read as a diagram of their own. Null for a chart with no groups, or one whose kind
    /// doesn't fold (a sequence diagram, a timeline, a mind map).
    /// </summary>
    public Flowchart? Folded()
    {
        if (Groups.Count == 0 || Form is not (ChartForm.Flowchart or ChartForm.State)) return null;
        var parent = Groups.ToDictionary(g => g.Id, g => g.Parent);
        string Top(string group)
        {
            while (parent.TryGetValue(group, out var p) && p is not null) group = p;
            return group;
        }
        var unitOf = new Dictionary<string, string>();
        foreach (var g in Groups)
            foreach (string m in g.Members) unitOf[m] = Top(g.Id);
        string Unit(string id) => unitOf.TryGetValue(id, out var u) ? u : id;
        var nodes = new List<FlowNode>();
        var placed = new HashSet<string>();
        foreach (var n in Nodes)
        {
            string u = Unit(n.Id);
            if (!placed.Add(u)) continue;
            if (u == n.Id) nodes.Add(n);
            else
            {
                var g = Groups.First(x => x.Id == u);
                int inside = Nodes.Count(x => Unit(x.Id) == u && x.Role is not (NodeRole.Start or NodeRole.End or NodeRole.Note));
                nodes.Add(new FlowNode(u, g.Title.Length > 0 ? [g.Title] : [u], NodeShape.Rounded, Tone.None) { Detail = [inside == 1 ? "1 box" : $"{inside} boxes"] });
            }
        }
        var edges = new List<FlowEdge>();
        foreach (var pair in Edges.Select(e => (e, From: Unit(e.From), To: Unit(e.To))).Where(x => x.From != x.To).GroupBy(x => (x.From, x.To)))
        {
            var first = pair.First().e;
            var words = pair.Select(x => x.e.Label).Distinct().ToList();
            edges.Add(new FlowEdge(pair.Key.From, pair.Key.To, words.Count == 1 ? words[0] : null, first.Line, first.StartEnd, first.EndEnd));
        }
        return new Flowchart(Direction, Title, nodes, edges, []);
    }

    /// <summary>
    /// The chart as canonical Mermaid, every label quoted, so any Mermaid reader (Obsidian, Typora, GitHub) draws
    /// what this lenient reader read: every box in order, then the groups naming their boxes, then the arrows. A
    /// state or sequence diagram, a timeline or a mind map is written back as its own kind.
    /// </summary>
    public string ToSource()
    {
        if (Form != ChartForm.Flowchart) return Mermaid.KindSource(this);
        var sb = new StringBuilder();
        if (Title is { Length: > 0 } title) sb.Append("---\ntitle: ").Append(title.Replace('\n', ' ')).Append("\n---\n");
        sb.Append("flowchart ").Append(Direction switch
        {
            ChartDirection.BottomUp => "BT",
            ChartDirection.LeftRight => "LR",
            ChartDirection.RightLeft => "RL",
            _ => "TD",
        }).Append('\n');
        foreach (var n in Nodes)
        {
            var (open, close) = n.Shape switch
            {
                NodeShape.Rounded => ("(", ")"),
                NodeShape.Stadium => ("([", "])"),
                NodeShape.Circle => ("((", "))"),
                NodeShape.DoubleCircle => ("(((", ")))"),
                NodeShape.Decision => ("{", "}"),
                NodeShape.Hexagon => ("{{", "}}"),
                NodeShape.Subroutine => ("[[", "]]"),
                NodeShape.Cylinder => ("[(", ")]"),
                NodeShape.Parallelogram => ("[/", "/]"),
                NodeShape.Trapezoid => ("[/", "\\]"),
                _ => ("[", "]"),
            };
            sb.Append("  ").Append(n.Id).Append(open).Append(BoxWords(n)).Append(close);
            if (n.Tone != Tone.None) sb.Append(":::").Append(n.Tone.ToString().ToLowerInvariant());
            sb.Append('\n');
        }
        void Group(FlowGroup g, string indent)
        {
            sb.Append(indent).Append("subgraph ").Append(g.Id).Append(" [").Append(Quote(g.Title)).Append("]\n");
            foreach (string m in g.Members) sb.Append(indent).Append("  ").Append(m).Append('\n');
            foreach (var child in Groups.Where(c => c.Parent == g.Id)) Group(child, indent + "  ");
            sb.Append(indent).Append("end\n");
        }
        foreach (var g in Groups.Where(g => g.Parent is null)) Group(g, "  ");
        foreach (var e in Edges)
        {
            sb.Append("  ").Append(e.From).Append(' ').Append(Arrow(e));
            if (e.Label is not null) sb.Append('|').Append(Quote(e.Label)).Append('|');
            sb.Append(' ').Append(e.To).Append('\n');
        }
        return sb.ToString();
    }

    static string Arrow(FlowEdge e)
    {
        static string End(EdgeEnd end, bool start) => end switch
        {
            EdgeEnd.Arrow => start ? "<" : ">",
            EdgeEnd.Circle => "o",
            EdgeEnd.Cross => "x",
            _ => "",
        };
        bool open = e.EndEnd == EdgeEnd.None;
        string body = e.Line switch
        {
            EdgeLine.Dotted => "-.-",
            EdgeLine.Thick => open ? "===" : "==",
            _ => open ? "---" : "--",
        };
        return End(e.StartEnd, true) + body + End(e.EndEnd, false);
    }

    /// <summary>A label in Mermaid's quotes: its quotes and anything that would read as an entity or a tag escaped,
    /// its line breaks as &lt;br&gt;.</summary>
    internal static string Quote(string text) => "\"" + Escape(text) + "\"";

    static string Escape(string text) =>
        Regex.Replace(text, @"[#&](?=#?\w+;)|<(?=[A-Za-z/])", m => m.Value switch { "#" => "#35;", "&" => "#38;", _ => "#lt;" })
            .Replace("\"", "#quot;").Replace("\n", "<br>");

    /// <summary>A box's words quoted, its smaller words after them in &lt;small&gt; (which Mermaid itself draws small).</summary>
    internal static string BoxWords(FlowNode n) => n.Detail.Count == 0
        ? Quote(string.Join("\n", n.Lines))
        : "\"" + Escape(string.Join("\n", n.Lines)) + "<br><small>" + Escape(string.Join("\n", n.Detail)) + "</small>\"";
}

/// <summary>Reads Mermaid source (a flowchart, a state or sequence diagram, a timeline or a mind map) into a
/// <see cref="Flowchart"/>.</summary>
public static partial class Mermaid
{
    /// <summary>What every kind Study Stash draws is called, for a reason given about one it doesn't.</summary>
    public const string Drawn = "Study Stash draws flowcharts, state and sequence diagrams, timelines and mind maps";

    static readonly Dictionary<string, string> OtherKinds = new(StringComparer.OrdinalIgnoreCase)
    {
        ["zenuml"] = "a ZenUML sequence diagram", ["classDiagram"] = "a class diagram",
        ["classDiagram-v2"] = "a class diagram",
        ["erDiagram"] = "an entity-relationship diagram", ["journey"] = "a user journey", ["gantt"] = "a Gantt chart",
        ["pie"] = "a pie chart", ["quadrantChart"] = "a quadrant chart", ["requirementDiagram"] = "a requirement diagram",
        ["gitGraph"] = "a git graph", ["sankey-beta"] = "a Sankey diagram",
        ["sankey"] = "a Sankey diagram", ["xychart-beta"] = "an XY chart", ["xychart"] = "an XY chart", ["block-beta"] = "a block diagram",
        ["block"] = "a block diagram", ["packet-beta"] = "a packet diagram", ["packet"] = "a packet diagram", ["kanban"] = "a kanban board",
        ["architecture-beta"] = "an architecture diagram", ["architecture"] = "an architecture diagram", ["radar-beta"] = "a radar chart",
        ["treemap"] = "a treemap", ["treemap-beta"] = "a treemap", ["C4Context"] = "a C4 diagram", ["C4Container"] = "a C4 diagram",
        ["C4Component"] = "a C4 diagram", ["C4Dynamic"] = "a C4 diagram", ["C4Deployment"] = "a C4 diagram",
    };

    /// <summary>The kinds besides flowcharts that are read, by the word their first line starts with.</summary>
    static readonly Dictionary<string, ChartForm> Kinds = new(StringComparer.OrdinalIgnoreCase)
    {
        ["stateDiagram"] = ChartForm.State, ["stateDiagram-v2"] = ChartForm.State, ["sequenceDiagram"] = ChartForm.Sequence,
        ["timeline"] = ChartForm.Timeline, ["mindmap"] = ChartForm.Mindmap,
    };

    static readonly HashSet<string> Ignored = new(StringComparer.Ordinal)
    {
        "classDef", "style", "linkStyle", "click", "callback", "accTitle", "accDescr", "direction",
    };

    /// <summary>The shapes, longest opener first so "((" wins over "(". A slanted box is a parallelogram when both its
    /// sides lean the same way (/…/ or \…\), a trapezoid when they don't (see <see cref="Slanted"/>).</summary>
    static readonly (string Open, string[] Close, NodeShape Shape)[] Shapes =
    [
        ("(((", [")))"], NodeShape.DoubleCircle), ("([", ["])"], NodeShape.Stadium), ("((", ["))"], NodeShape.Circle),
        ("[[", ["]]"], NodeShape.Subroutine), ("[(", [")]"], NodeShape.Cylinder), ("[/", ["/]", "\\]"], NodeShape.Parallelogram),
        ("[\\", ["\\]", "/]"], NodeShape.Parallelogram), ("{{", ["}}"], NodeShape.Hexagon), ("[", ["]"], NodeShape.Box),
        ("(", [")"], NodeShape.Rounded), ("{", ["}"], NodeShape.Decision), (">", ["]"], NodeShape.Box),
    ];

    /// <summary>A slanted box's shape from how it opened and closed: [/…/] and [\…\] are parallelograms, [/…\] and
    /// [\…/] trapezoids.</summary>
    static NodeShape Slanted(string open, string close) => open[^1] == close[0] ? NodeShape.Parallelogram : NodeShape.Trapezoid;

    /// <summary>How many characters a box's smaller words may run to, all told.</summary>
    public const int MaxDetail = 120;

    [GeneratedRegex(@"<small>(.*?)</small>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex Small();

    [GeneratedRegex(@"<br\s*/?>", RegexOptions.IgnoreCase)]
    private static partial Regex LineBreak();

    [GeneratedRegex(@"(#|&)([A-Za-z]+\d*|#?\d+|#x[0-9A-Fa-f]+);")]
    private static partial Regex Entity();

    [GeneratedRegex(@"</?(?:b|i|u|em|strong|small|big|span|font|code|p|div|center|s|strike|mark)\b[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex Tag();

    [GeneratedRegex(@"<(sub|sup)>(.*?)</\1>", RegexOptions.IgnoreCase)]
    private static partial Regex SubSup();

    [GeneratedRegex(@"\bfa[bsr]?:fa-[\w-]+\s*")]
    private static partial Regex FontAwesome();

    // $x$ is maths, but "$5 to $10" is money: no space just inside the dollars, no digit straight after the last.
    [GeneratedRegex(@"\$(?!\s)([^$]+?)(?<!\s)\$(?!\d)")]
    private static partial Regex InlineMath();

    /// <summary>Reads a diagram; anything it can't draw throws a <see cref="MermaidException"/>, never anything else.</summary>
    public static Flowchart Parse(string source)
    {
        if (string.IsNullOrWhiteSpace(source)) throw new MermaidException("There's no diagram here.", 0);
        if (source.Length > Flowchart.MaxSource) throw new MermaidException("This diagram is too long to draw (over 8 KB).", 0);
        var form = FormOf(source);
        var reader = new Reader();
        var kind = form is ChartForm.Flowchart ? null : new KindReader(form);
        try
        {
            return kind is null ? reader.Read(source) : kind.Read(source);
        }
        catch (MermaidException)
        {
            throw;
        }
        catch (Exception)
        {
            int line = kind?.LineNumber ?? reader.LineNumber;
            throw new MermaidException($"Couldn't read line {line} of this diagram.", line);
        }
    }

    /// <summary>The kind a source says it is on its first line (after any front matter); a flowchart when it says
    /// none this reader knows (the flowchart reader then says why it can't draw it).</summary>
    static ChartForm FormOf(string source)
    {
        var lines = source.ReplaceLineEndings("\n").Split('\n');
        int i = BodyStart(lines, out _);
        if (i >= lines.Length) return ChartForm.Flowchart;
        string line = lines[i].Trim();
        int n = 0;
        while (n < line.Length && (char.IsLetterOrDigit(line[n]) || line[n] == '-')) n++;
        return Kinds.TryGetValue(line[..n], out var form) ? form : ChartForm.Flowchart;
    }

    /// <summary>Where a source's first line that says something is (front matter, blank lines and comments before it
    /// skipped, the front matter's title kept); past the end when there's none.</summary>
    static int BodyStart(string[] lines, out string? title)
    {
        title = null;
        int i = 0;
        static bool Blank(string l) => l.Trim().Length == 0 || l.TrimStart().StartsWith("%%", StringComparison.Ordinal);
        while (i < lines.Length && Blank(lines[i])) i++;
        if (i < lines.Length && lines[i].Trim() == "---")
        {
            for (i++; i < lines.Length && lines[i].Trim() != "---"; i++)
                if (lines[i].TrimStart().StartsWith("title:", StringComparison.Ordinal)) title = Words(lines[i].Trim()[6..]) is { Count: > 0 } w ? string.Join(" ", w) : null;
            i++;
            while (i < lines.Length && Blank(lines[i])) i++;
        }
        return i;
    }

    /// <summary>A label as the writer meant it: quotes and markdown ticks gone, entities decoded, &lt;br&gt; as line
    /// breaks, other HTML dropped, maths made plain, at most <see cref="Flowchart.MaxLabel"/> characters.</summary>
    internal static List<string> Words(string raw) => Plain(Unquoted(raw), Flowchart.MaxLabel);

    /// <summary>A box's words and, apart, its smaller ones (what's in &lt;small&gt;), each read as <see cref="Words"/>.</summary>
    internal static (List<string> Lines, List<string> Detail) BoxLabel(string raw)
    {
        string t = Unquoted(raw);
        var detail = new List<string>();
        t = Small().Replace(t, m =>
        {
            detail.Add(m.Groups[1].Value);
            return "";
        });
        return (Plain(t, Flowchart.MaxLabel), detail.Count == 0 ? [] : Plain(string.Join("<br>", detail), MaxDetail));
    }

    static string Unquoted(string raw)
    {
        string t = raw.Trim();
        if (t.Length >= 2 && t[0] == '"' && t[^1] == '"') t = t[1..^1];
        if (t.Length >= 2 && t[0] == '`' && t[^1] == '`') t = t[1..^1];
        return t;
    }

    static List<string> Plain(string text, int max)
    {
        string t = LineBreak().Replace(text, "\n").Replace("**", "");
        t = SubSup().Replace(t, m => Script(m.Groups[2].Value, m.Groups[1].Value.Equals("sup", StringComparison.OrdinalIgnoreCase)));
        t = Tag().Replace(t, "");
        t = FontAwesome().Replace(t, "");
        // Mermaid's #quot; and #35; and HTML's &amp; and &#39;, each decoded once.
        t = Entity().Replace(t, m => WebUtility.HtmlDecode("&" + (m.Groups[1].Value == "#" && char.IsAsciiDigit(m.Groups[2].Value[0]) ? "#" : "") + m.Groups[2].Value + ";"));
        t = InlineMath().Replace(t, m => MathText.Plain(m.Groups[1].Value));
        var lines = new List<string>();
        int budget = max;
        foreach (string part in t.Split('\n'))
        {
            string line = string.Join(' ', part.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
            if (line.Length == 0) continue;
            if (budget <= 0) break;
            if (line.Length > budget)
            {
                lines.Add(line[..Math.Max(0, budget - 1)].TrimEnd() + "…");
                break;
            }
            lines.Add(line);
            budget -= line.Length;
        }
        return lines;
    }

    static string Script(string text, bool sup)
    {
        const string Sup = "⁰¹²³⁴⁵⁶⁷⁸⁹⁺⁻", Sub = "₀₁₂₃₄₅₆₇₈₉₊₋";
        string digits = "0123456789+-", table = sup ? Sup : Sub;
        return text.All(c => digits.Contains(c)) ? string.Concat(text.Select(c => table[digits.IndexOf(c)])) : text;
    }

    sealed class MutNode(string id)
    {
        public readonly string Id = id;
        public List<string> Lines = [id];
        public List<string> Detail = [];
        public NodeShape Shape;
        public Tone Tone;
        public bool Declared;
        public MutGroup? Group;
    }

    sealed class MutGroup(string id, string title, MutGroup? parent)
    {
        public readonly string Id = id, Title = title;
        public readonly MutGroup? Parent = parent;
        public readonly List<string> Members = [];
    }

    readonly record struct NodeRef(string Id, NodeShape? Shape, List<string>? Lines, Tone? Tone, List<string>? Detail = null);

    readonly record struct Link(EdgeLine Line, EdgeEnd Start, EdgeEnd End, string? Label, bool Invisible);

    sealed class Reader
    {
        readonly Dictionary<string, MutNode> nodes = new(StringComparer.Ordinal);
        readonly List<MutNode> order = [];
        readonly List<MutGroup> groups = [];
        readonly Stack<MutGroup> open = new();
        readonly List<FlowEdge> edges = [];
        ChartDirection direction = ChartDirection.TopDown;
        string? title;
        public int LineNumber;

        public Flowchart Read(string source)
        {
            var lines = source.ReplaceLineEndings("\n").Split('\n');
            int i = 0;
            bool Blank(string l) => l.Trim().Length == 0 || l.TrimStart().StartsWith("%%", StringComparison.Ordinal);
            while (i < lines.Length && Blank(lines[i])) i++;
            if (i < lines.Length && lines[i].Trim() == "---")
            {
                // Front matter: only its title matters here.
                for (i++; i < lines.Length && lines[i].Trim() != "---"; i++)
                    if (lines[i].TrimStart().StartsWith("title:", StringComparison.Ordinal)) title = Words(lines[i].Trim()[6..]) is { Count: > 0 } w ? string.Join(" ", w) : null;
                i++;
                while (i < lines.Length && Blank(lines[i])) i++;
            }
            if (i >= lines.Length) throw new MermaidException("There's no diagram here.", 0);
            LineNumber = i + 1;
            string rest = Header(lines[i].Trim());
            Statements(rest);
            for (i++; i < lines.Length; i++)
            {
                LineNumber = i + 1;
                string line = lines[i].Trim();
                if (line.StartsWith("%%", StringComparison.Ordinal)) continue;
                if (line.StartsWith("accDescr", StringComparison.Ordinal) && line.Contains('{') && !line.Contains('}'))
                {
                    while (i + 1 < lines.Length && !lines[i].Contains('}')) i++;
                    continue;
                }
                Statements(line);
            }
            if (open.Count > 0) throw new MermaidException($"The group “{open.Peek().Title}” is never closed with end.", LineNumber);
            return Build();
        }

        /// <summary>The first line: flowchart or graph and a direction. Returns what follows it on the same line.</summary>
        string Header(string line)
        {
            int n = 0;
            while (n < line.Length && (char.IsLetterOrDigit(line[n]) || line[n] == '-')) n++;
            string word = line[..n];
            if (!word.Equals("flowchart", StringComparison.OrdinalIgnoreCase) && !word.Equals("graph", StringComparison.OrdinalIgnoreCase)
                && !word.Equals("flowchart-elk", StringComparison.OrdinalIgnoreCase))
            {
                if (OtherKinds.TryGetValue(word, out string? kind))
                    throw new MermaidException($"{Drawn}; this is {kind}.", LineNumber);
                throw new MermaidException("This diagram doesn't start with flowchart, graph, stateDiagram, sequenceDiagram, timeline or mindmap, so it isn't one Study Stash can draw.", LineNumber);
            }
            string rest = line[n..].TrimStart();
            int d = 0;
            while (d < rest.Length && char.IsLetter(rest[d])) d++;
            string dir = rest[..d].ToUpperInvariant();
            if (dir is "TD" or "TB" or "BT" or "LR" or "RL" && (d == rest.Length || !char.IsLetterOrDigit(rest[d])))
            {
                direction = dir switch
                {
                    "BT" => ChartDirection.BottomUp,
                    "LR" => ChartDirection.LeftRight,
                    "RL" => ChartDirection.RightLeft,
                    _ => ChartDirection.TopDown,
                };
                rest = rest[d..];
            }
            rest = rest.TrimStart();
            return rest.StartsWith(';') ? rest[1..] : rest;
        }

        /// <summary>One line's statements: split at semicolons outside quotes (and not ending an entity like #quot;).</summary>
        void Statements(string line)
        {
            int start = 0;
            bool quoted = false;
            for (int k = 0; k < line.Length; k++)
            {
                char c = line[k];
                if (c == '"') quoted = !quoted;
                else if (!quoted && c == '%' && k + 1 < line.Length && line[k + 1] == '%') { Statement(line[start..k]); return; }
                else if (!quoted && c == ';' && !EndsEntity(line, k)) { Statement(line[start..k]); start = k + 1; }
            }
            Statement(line[start..]);
        }

        static bool EndsEntity(string s, int semicolon)
        {
            int k = semicolon - 1;
            while (k >= 0 && char.IsLetterOrDigit(s[k])) k--;
            return k >= 0 && k < semicolon - 1 && s[k] == '#';
        }

        void Statement(string raw)
        {
            string s = raw.Trim();
            if (s.Length == 0) return;
            int n = 0;
            while (n < s.Length && (char.IsLetterOrDigit(s[n]) || s[n] == '_')) n++;
            string word = s[..n];
            bool spaced = n == s.Length || char.IsWhiteSpace(s[n]) || s[n] == ':';
            if (word == "end" && n == s.Length)
            {
                if (open.Count == 0) throw new MermaidException($"Line {LineNumber} has an end with no subgraph to close.", LineNumber);
                open.Pop();
                return;
            }
            if (word == "subgraph" && spaced) { Subgraph(s[n..].Trim()); return; }
            if (Ignored.Contains(word) && spaced) return;
            if (word == "class" && n < s.Length && char.IsWhiteSpace(s[n])) { Classes(s[n..].Trim()); return; }
            Chain(s);
        }

        void Subgraph(string rest)
        {
            if (open.Count >= 2) throw new MermaidException($"Line {LineNumber} puts a group inside a group inside a group; Study Stash draws two levels.", LineNumber);
            string id, groupTitle;
            if (rest.StartsWith('"'))
            {
                groupTitle = Title(rest);
                id = "";
            }
            else
            {
                int n = 0;
                while (n < rest.Length && IdChar(rest, n)) n++;
                id = rest[..n];
                string after = rest[n..].Trim();
                if (after.StartsWith('[') && after.EndsWith(']')) groupTitle = Title(after[1..^1]);
                else if (after.Length > 0 || id.Length == 0) { groupTitle = Title(rest); id = ""; }
                else groupTitle = id;
            }
            if (id.Length == 0) id = "group" + (groups.Count + 1);
            var group = new MutGroup(id, groupTitle, open.Count > 0 ? open.Peek() : null);
            groups.Add(group);
            open.Push(group);
        }

        static string Title(string raw) => string.Join(" ", Words(raw));

        void Classes(string rest)
        {
            var parts = rest.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2 || ToneOf(parts[^1]) is not { } tone) return;
            foreach (string id in string.Join("", parts[..^1]).Split(',', StringSplitOptions.RemoveEmptyEntries))
                if (nodes.TryGetValue(id.Trim(), out var node)) node.Tone = tone;
        }

        static Tone? ToneOf(string name) => name.Trim().ToLowerInvariant() switch
        {
            "accent" => Tone.Accent,
            "red" => Tone.Red,
            "blue" => Tone.Blue,
            "green" => Tone.Green,
            "amber" => Tone.Amber,
            "purple" => Tone.Purple,
            _ => null,
        };

        // --- a statement of boxes and arrows: A --> B -->|yes| C & D ---

        string s = "";
        int pos;

        void Chain(string statement)
        {
            s = statement;
            pos = 0;
            var left = NodeGroup() ?? throw Unreadable();
            while (true)
            {
                Spaces();
                if (pos >= s.Length) break;
                var link = ReadLink() ?? throw Unreadable();
                var right = NodeGroup() ?? throw new MermaidException($"Line {LineNumber} has an arrow that doesn't point at a box.", LineNumber);
                if (!link.Invisible)
                    foreach (var a in left)
                        foreach (var b in right)
                        {
                            if (edges.Count >= Flowchart.MaxEdges)
                                throw new MermaidException($"This diagram has more than {Flowchart.MaxEdges} arrows; Study Stash draws up to {Flowchart.MaxEdges}.", LineNumber);
                            edges.Add(new FlowEdge(a, b, link.Label, link.Line, link.Start, link.End));
                        }
                left = right;
            }
        }

        MermaidException Unreadable()
        {
            string bit = s.Length > 48 ? s[..47] + "…" : s;
            return new MermaidException($"Couldn't read line {LineNumber}: “{bit}”.", LineNumber);
        }

        void Spaces()
        {
            while (pos < s.Length && char.IsWhiteSpace(s[pos])) pos++;
        }

        char Peek(int ahead = 0) => pos + ahead < s.Length ? s[pos + ahead] : '\0';

        bool At(string text) => string.CompareOrdinal(s, pos, text, 0, text.Length) == 0;

        /// <summary>Boxes joined by &amp; (a fan); null when there's no box here.</summary>
        List<string>? NodeGroup()
        {
            var ids = new List<string>();
            while (true)
            {
                Spaces();
                var node = ReadNode();
                if (node is null)
                {
                    if (ids.Count == 0) return null;
                    throw Unreadable();
                }
                ids.Add(Touch(node.Value));
                Spaces();
                if (Peek() != '&') return ids;
                pos++;
            }
        }

        static bool IdChar(string text, int k)
        {
            char c = text[k];
            if (char.IsLetterOrDigit(c) || c == '_') return true;
            // step-1 is one id; A-->B isn't.
            return c == '-' && k > 0 && k + 1 < text.Length && (char.IsLetterOrDigit(text[k + 1]) || text[k + 1] == '_') && text[k - 1] != '-';
        }

        NodeRef? ReadNode()
        {
            int start = pos;
            while (pos < s.Length && IdChar(s, pos)) pos++;
            if (pos == start) return null;
            string id = s[start..pos];
            NodeShape? shape = null;
            List<string>? lines = null, detail = null;
            Tone? tone = null;
            int save = pos;
            Spaces();
            foreach (var (openText, closers, kind) in Shapes)
            {
                if (!At(openText)) continue;
                pos += openText.Length;
                (lines, detail) = BoxLabel(Label(closers));
                shape = kind == NodeShape.Parallelogram ? Slanted(openText, closedWith) : kind;
                break;
            }
            if (shape is null) pos = save;
            if (At(":::"))
            {
                pos += 3;
                int t = pos;
                while (pos < s.Length && (char.IsLetterOrDigit(s[pos]) || s[pos] is '_' or '-')) pos++;
                tone = ToneOf(s[t..pos]) ?? Tone.None;
            }
            return new NodeRef(id, shape, lines, tone, detail);
        }

        /// <summary>The closer the last box's words ended with (a slanted box's shape depends on it).</summary>
        string closedWith = "";

        /// <summary>
        /// A box's words up to its closer. Quoted words may hold anything; unquoted ones run to the first closer
        /// that ends the box — one followed by the end, a colour class, an &amp; or an arrow — so
        /// A[Right atrium (RA)] reads as the writer meant.
        /// </summary>
        string Label(string[] closers)
        {
            int start = pos;
            Spaces();
            if (Peek() == '"')
            {
                int end = s.IndexOf('"', pos + 1);
                if (end > 0)
                {
                    int after = end + 1;
                    while (after < s.Length && char.IsWhiteSpace(s[after])) after++;
                    foreach (string c in closers)
                        if (string.CompareOrdinal(s, after, c, 0, c.Length) == 0)
                        {
                            string text = s[(pos + 1)..end];
                            pos = after + c.Length;
                            closedWith = c;
                            return text;
                        }
                }
            }
            pos = start;
            int best = -1;
            string? closer = null;
            foreach (string c in closers)
            {
                for (int k = s.IndexOf(c, pos, StringComparison.Ordinal); k >= 0; k = s.IndexOf(c, k + 1, StringComparison.Ordinal))
                {
                    if (!EndsBox(k + c.Length)) continue;
                    if (best < 0 || k < best) { best = k; closer = c; }
                    break;
                }
            }
            if (best < 0) throw new MermaidException($"Line {LineNumber} has a box that isn't closed: “{Snippet(start)}”.", LineNumber);
            string label = s[pos..best];
            pos = best + closer!.Length;
            closedWith = closer;
            return label;
        }

        /// <summary>The unclosed box as written, from its id ("B[Give the dose"), not from mid-word before it.</summary>
        string Snippet(int from)
        {
            int at = Math.Min(from, s.Length);
            while (at > 0 && s[at - 1] is '[' or '(' or '{' or '>' or '/' or '\\') at--;
            while (at > 0 && (char.IsLetterOrDigit(s[at - 1]) || s[at - 1] == '_')) at--;
            string bit = s[at..];
            return bit.Length > 40 ? bit[..39] + "…" : bit;
        }

        bool EndsBox(int at)
        {
            while (at < s.Length && char.IsWhiteSpace(s[at])) at++;
            if (at >= s.Length) return true;
            if (s[at] == '&') return true;
            if (string.CompareOrdinal(s, at, ":::", 0, 3) == 0) return true;
            return LinkStart(at);
        }

        bool LinkStart(int at)
        {
            if (at < s.Length && s[at] is '<' or 'o' or 'x') at++;
            if (at + 1 >= s.Length) return false;
            string two = s.Substring(at, 2);
            return two is "--" or "==" or "-." or "~~";
        }

        Link? ReadLink()
        {
            Spaces();
            var start = EdgeEnd.None;
            if (Peek() is '<' or 'o' or 'x' && (Peek(1) == '-' && Peek(2) is '-' or '.' || Peek(1) == '=' && Peek(2) == '='))
            {
                start = Peek() switch { '<' => EdgeEnd.Arrow, 'o' => EdgeEnd.Circle, _ => EdgeEnd.Cross };
                pos++;
            }
            if (At("~~~"))
            {
                while (Peek() == '~') pos++;
                return new Link(EdgeLine.Solid, EdgeEnd.None, EdgeEnd.None, null, true);
            }
            var (line, arrow, close) = At("-.") ? (EdgeLine.Dotted, DottedArrow(), DottedClose())
                : At("==") ? (EdgeLine.Thick, ThickArrow(), ThickClose())
                : At("--") ? (EdgeLine.Solid, SolidArrow(), SolidClose())
                : default;
            if (arrow is null || close is null) return null;
            EdgeEnd end;
            string? label = null;
            var m = arrow.Match(s, pos);
            if (m.Success && EndFits(m))
            {
                pos += m.Length;
                end = EndOf(m.Groups["end"].Value);
            }
            else if (char.IsWhiteSpace(Peek(2)))
            {
                // The words-inside form: A -- words --> B, A -. words .-> B, A == words ==> B.
                var c = close.Match(s, pos + 2);
                if (!c.Success) return null;
                label = s[(pos + 2)..c.Index];
                pos = c.Index + c.Length;
                end = EndOf(c.Groups["end"].Value);
            }
            else return null;
            Spaces();
            if (Peek() == '|')
            {
                pos++;
                Spaces();
                int to = -1;
                if (Peek() == '"' && s.IndexOf('"', pos + 1) is var q and > 0)
                {
                    int after = q + 1;
                    while (after < s.Length && char.IsWhiteSpace(s[after])) after++;
                    if (after < s.Length && s[after] == '|') { label = s[(pos + 1)..q]; to = after; }
                }
                if (to < 0)
                {
                    to = s.IndexOf('|', pos);
                    if (to < 0) throw new MermaidException($"Line {LineNumber} has an arrow label that isn't closed with |.", LineNumber);
                    label = s[pos..to];
                }
                pos = to + 1;
            }
            string? words = label is null ? null : string.Join("\n", Words(label));
            return new Link(line, start, end, words is { Length: > 0 } ? words : null, false);
        }

        /// <summary>An o or x only ends an arrow when a word doesn't carry on after it (--xray is no arrow).</summary>
        bool EndFits(Match m)
        {
            if (m.Groups["end"].Value is not ("o" or "x")) return true;
            int after = m.Index + m.Length;
            return after >= s.Length || !char.IsLetterOrDigit(s[after]);
        }

        static EdgeEnd EndOf(string mark) => mark switch
        {
            "o" => EdgeEnd.Circle,
            "x" => EdgeEnd.Cross,
            "" => EdgeEnd.None,
            _ => EdgeEnd.Arrow,
        };

        string Touch(NodeRef r)
        {
            if (!nodes.TryGetValue(r.Id, out var node))
            {
                if (nodes.Count >= Flowchart.MaxNodes)
                    throw new MermaidException($"This diagram has more than {Flowchart.MaxNodes} boxes; Study Stash draws up to {Flowchart.MaxNodes}.", LineNumber);
                node = new MutNode(r.Id);
                nodes[r.Id] = node;
                order.Add(node);
            }
            if (r.Shape is { } shape)
            {
                node.Shape = shape;
                node.Lines = r.Lines ?? [];
                node.Detail = r.Detail ?? [];
                node.Declared = true;
            }
            if (r.Tone is { } tone) node.Tone = tone;
            if (open.Count > 0 && node.Group is null)
            {
                node.Group = open.Peek();
                node.Group.Members.Add(node.Id);
            }
            return node.Id;
        }

        Flowchart Build()
        {
            // An arrow to a group's id means the group: point it at the group's first box instead.
            var byId = groups.GroupBy(g => g.Id).ToDictionary(g => g.Key, g => g.First());
            var retarget = new Dictionary<string, string>();
            foreach (var n in order.ToList())
            {
                if (n.Declared || !byId.TryGetValue(n.Id, out var g)) continue;
                if (FirstMember(g, n.Id) is not { } member) continue;
                retarget[n.Id] = member;
                order.Remove(n);
                n.Group?.Members.Remove(n.Id);
            }
            string Map(string id) => retarget.TryGetValue(id, out string? to) ? to : id;
            var finalEdges = edges.Select(e => e with { From = Map(e.From), To = Map(e.To) }).ToList();
            if (order.Count == 0) throw new MermaidException("This flowchart has no boxes to draw.", LineNumber);
            // Groups keep ids unique against boxes, and only groups with something in them are drawn.
            var taken = new HashSet<string>(order.Select(n => n.Id));
            var kept = groups.Where(g => Holds(g)).ToList();
            var ids = new Dictionary<MutGroup, string>();
            foreach (var g in kept)
            {
                string id = g.Id;
                for (int k = 2; taken.Contains(id); k++) id = g.Id + "_" + k;
                taken.Add(id);
                ids[g] = id;
            }
            var flowGroups = kept.Select(g => new FlowGroup(ids[g], g.Title, g.Members.ToList(), g.Parent is { } p && ids.TryGetValue(p, out string? pid) ? pid : null)).ToList();
            var flowNodes = order.Select(n => new FlowNode(n.Id, n.Lines, n.Shape, n.Tone) { Detail = n.Detail }).ToList();
            return new Flowchart(direction, title, flowNodes, finalEdges, flowGroups);
        }

        string? FirstMember(MutGroup g, string except)
        {
            foreach (string m in g.Members) if (m != except) return m;
            foreach (var child in groups.Where(c => c.Parent == g)) if (FirstMember(child, except) is { } m) return m;
            return null;
        }

        bool Holds(MutGroup g) => g.Members.Count > 0 || groups.Any(c => c.Parent == g && Holds(c));
    }

    // Arrows, read at the current position (\G): the line, then what ends it. A plain line needs three dashes (---),
    // an arrow two (-->); longer ones (--->, ==>>) are the same arrow.
    [GeneratedRegex(@"\G-\.+-(?<end>>+|o|x)?")]
    private static partial Regex DottedArrow();

    [GeneratedRegex(@"\.+-+(?<end>>+|o|x)?")]
    private static partial Regex DottedClose();

    [GeneratedRegex(@"\G(?:={2,}(?<end>>+|o|x)|={3,})")]
    private static partial Regex ThickArrow();

    [GeneratedRegex(@"={2,}(?<end>>+|o|x)|={3,}")]
    private static partial Regex ThickClose();

    [GeneratedRegex(@"\G(?:-{2,}(?<end>>+|o|x)|-{3,})")]
    private static partial Regex SolidArrow();

    [GeneratedRegex(@"-{2,}(?<end>>+|o|x)|-{3,}")]
    private static partial Regex SolidClose();
}
