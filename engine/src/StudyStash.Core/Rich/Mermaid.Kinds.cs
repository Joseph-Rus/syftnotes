using System.Text;
using System.Text.RegularExpressions;

namespace StudyStash.Core.Rich;

/// <summary>
/// The kinds besides flowcharts, read (leniently, as flowcharts are) into the same boxes, arrows and groups, and
/// written back as their own kind:
/// <list type="bullet">
/// <item>a state diagram (stateDiagram-v2): states, transitions with their events, [*] for where it starts and ends,
/// composite states as groups, choices, notes, and a state's description as its smaller words. A state classed
/// <c>accept</c> (or final, accepting) is a double circle, and when every state's name is a short symbol (q0, S1) the
/// states are circles, as an automaton is drawn;</item>
/// <item>a sequence diagram: participants and actors, its messages in order (solid, dotted, crossed, both ways),
/// notes, and loop, alt, opt, par, critical and break blocks with their dividers;</item>
/// <item>a timeline: its periods in order, what happened in each, and its sections as groups;</item>
/// <item>a mind map: one root and its branches by indentation, in the shapes Mermaid gives them, coloured with
/// <c>:::red</c> and the rest.</item>
/// </list>
/// </summary>
public static partial class Mermaid
{
    [GeneratedRegex(@"^(?<from>[^\s:;<>+\-][^:;<>]*?)\s*(?<arrow><<-->>|<<->>|-->>|->>|--x|-x|--\)|-\)|-->|->)\s*[+-]?\s*(?<to>[^\s:;<>+\-][^:;]*?)\s*(?::(?<text>.*))?$")]
    private static partial Regex Message();

    [GeneratedRegex(@"^note\s+(?<where>left of|right of|over)\s+(?<who>[^:]+?)\s*(?::(?<text>.*))?$", RegexOptions.IgnoreCase)]
    private static partial Regex NoteLine();

    [GeneratedRegex(@"^(?<id>[\p{L}\p{N}_]+)?(?<open>\(\(|\)\)|\{\{|\(|\)|\[)(?<text>.*?)(?<close>\)\)|\(\(|\}\}|\)|\(|\])(?<rest>\s*(?::::.*)?)$")]
    private static partial Regex MindNode();

    [GeneratedRegex(@":::\s*([\w\s-]+)$")]
    private static partial Regex Classes();

    static readonly HashSet<string> StateIgnored = new(StringComparer.Ordinal) { "classDef", "style", "hide", "scale", "accTitle", "accDescr", "linkStyle", "click" };
    static readonly HashSet<string> SequenceIgnored = new(StringComparer.OrdinalIgnoreCase)
    {
        "activate", "deactivate", "accTitle", "accDescr", "link", "links", "properties", "details", "destroy", "create",
    };
    static readonly HashSet<string> Blocks = new(StringComparer.OrdinalIgnoreCase) { "loop", "alt", "opt", "par", "critical", "break" };
    static readonly HashSet<string> Dividers = new(StringComparer.OrdinalIgnoreCase) { "else", "and", "option" };

    /// <summary>A state classed one of these is an accepting (final) state: a double circle.</summary>
    static readonly HashSet<string> Accepting = new(StringComparer.OrdinalIgnoreCase) { "accept", "accepting", "final" };

    /// <summary>Reads every kind but the flowchart, one statement a line, saying which line it couldn't read.</summary>
    sealed class KindReader(ChartForm form)
    {
        public int LineNumber;
        string? title;
        ChartDirection direction = ChartDirection.TopDown;

        readonly Dictionary<string, MutNode> nodes = new(StringComparer.Ordinal);
        readonly Dictionary<string, NodeRole> roles = new(StringComparer.Ordinal);
        readonly List<MutNode> order = [];
        readonly List<MutGroup> groups = [];
        readonly Stack<MutGroup> open = new();
        readonly List<FlowEdge> edges = [];
        readonly List<SeqStep> steps = [];
        readonly HashSet<string> accepting = new(StringComparer.Ordinal);
        bool numbered;

        public Flowchart Read(string source)
        {
            var lines = source.ReplaceLineEndings("\n").Split('\n');
            int i = BodyStart(lines, out title);
            if (i >= lines.Length) throw new MermaidException("There's no diagram here.", 0);
            LineNumber = i + 1;
            string header = lines[i].Trim();
            int n = 0;
            while (n < header.Length && (char.IsLetterOrDigit(header[n]) || header[n] == '-')) n++;
            string rest = header[n..].Trim();
            if (form == ChartForm.Timeline) direction = rest.ToUpperInvariant() is "TD" or "TB" ? ChartDirection.TopDown : ChartDirection.LeftRight;
            if (form == ChartForm.Mindmap) direction = ChartDirection.LeftRight;
            var body = new List<(string Raw, int Line)>();
            for (i++; i < lines.Length; i++)
            {
                string t = lines[i].Trim();
                if (t.Length == 0 || t.StartsWith("%%", StringComparison.Ordinal)) continue;
                body.Add((lines[i], i + 1));
            }
            switch (form)
            {
                case ChartForm.State: State(body); break;
                case ChartForm.Sequence: Sequence(body); break;
                case ChartForm.Timeline: Timeline(body); break;
                default: Mindmap(body); break;
            }
            if (order.Count == 0) throw new MermaidException($"This {Name(form)} has nothing to draw.", LineNumber);
            return Build();
        }

        static string Name(ChartForm f) => f switch
        {
            ChartForm.State => "state diagram",
            ChartForm.Sequence => "sequence diagram",
            ChartForm.Timeline => "timeline",
            _ => "mind map",
        };

        MermaidException Unreadable(string s)
        {
            string bit = s.Length > 48 ? s[..47] + "…" : s;
            return new MermaidException($"Couldn't read line {LineNumber}: “{bit}”.", LineNumber);
        }

        MutNode Node(string id, NodeRole role = NodeRole.Plain)
        {
            if (nodes.TryGetValue(id, out var node)) return node;
            if (nodes.Count >= Flowchart.MaxNodes)
                throw new MermaidException($"This diagram has more than {Flowchart.MaxNodes} boxes; Study Stash draws up to {Flowchart.MaxNodes}.", LineNumber);
            node = new MutNode(id);
            nodes[id] = node;
            roles[id] = role;
            order.Add(node);
            if (open.Count > 0)
            {
                node.Group = open.Peek();
                node.Group.Members.Add(id);
            }
            return node;
        }

        void Edge(FlowEdge e)
        {
            if (edges.Count >= Flowchart.MaxEdges)
                throw new MermaidException($"This diagram has more than {Flowchart.MaxEdges} arrows; Study Stash draws up to {Flowchart.MaxEdges}.", LineNumber);
            edges.Add(e);
        }

        MutGroup Group(string id, string groupTitle)
        {
            if (open.Count >= 2) throw new MermaidException($"Line {LineNumber} puts a group inside a group inside a group; Study Stash draws two levels.", LineNumber);
            var g = new MutGroup(id, groupTitle, open.Count > 0 ? open.Peek() : null);
            groups.Add(g);
            open.Push(g);
            return g;
        }

        static string Id(string raw) => raw.Trim();

        // --- state diagrams ---

        void State(List<(string Raw, int Line)> body)
        {
            int notes = 0;
            for (int k = 0; k < body.Count; k++)
            {
                LineNumber = body[k].Line;
                string s = body[k].Raw.Trim();
                string word = FirstWord(s);
                if (s == "}")
                {
                    if (open.Count == 0) throw new MermaidException($"Line {LineNumber} closes a state that was never opened.", LineNumber);
                    open.Pop();
                    continue;
                }
                if (s == "--" || StateIgnored.Contains(word)) continue;
                if (word == "direction")
                {
                    if (open.Count == 0) direction = DirectionOf(s[9..].Trim()) ?? direction;
                    continue;
                }
                if (word.Equals("note", StringComparison.OrdinalIgnoreCase))
                {
                    var m = NoteLine().Match(s);
                    if (!m.Success) throw Unreadable(s);
                    string text = m.Groups["text"].Value;
                    if (!m.Groups["text"].Success)
                    {
                        var said = new List<string>();
                        for (k++; k < body.Count && !body[k].Raw.Trim().Equals("end note", StringComparison.OrdinalIgnoreCase); k++) said.Add(body[k].Raw.Trim());
                        text = string.Join("<br>", said);
                    }
                    string about = StateRef(m.Groups["who"].Value.Split(',')[0], false);
                    var note = Node("note" + ++notes, NodeRole.Note);
                    note.Lines = Words(text);
                    note.Shape = NodeShape.Box;
                    note.Tone = Tone.Amber;
                    note.Declared = true;
                    Edge(new FlowEdge(about, note.Id, null, EdgeLine.Dotted, EdgeEnd.None, EdgeEnd.None));
                    continue;
                }
                if (word == "class")
                {
                    var parts = s[5..].Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 2) foreach (string id in string.Join("", parts[..^1]).Split(',', StringSplitOptions.RemoveEmptyEntries)) Classed(id.Trim(), parts[^1]);
                    continue;
                }
                if (word == "state")
                {
                    StateDeclaration(s[5..].Trim());
                    continue;
                }
                int arrow = s.IndexOf("-->", StringComparison.Ordinal);
                if (arrow > 0)
                {
                    string left = s[..arrow], right = s[(arrow + 3)..];
                    string? label = null;
                    int colon = right.IndexOf(':');
                    if (colon >= 0)
                    {
                        label = string.Join("\n", Words(right[(colon + 1)..]));
                        right = right[..colon];
                    }
                    if (left.Trim().Length == 0 || right.Trim().Length == 0) throw Unreadable(s);
                    Edge(new FlowEdge(StateRef(left, true), StateRef(right, false), label is { Length: > 0 } ? label : null, EdgeLine.Solid, EdgeEnd.None, EdgeEnd.Arrow));
                    continue;
                }
                int c = s.IndexOf(':');
                if (c > 0 && !s.Contains(":::", StringComparison.Ordinal))
                {
                    var node = Node(StateRef(s[..c], false));
                    node.Detail.AddRange(Words(s[(c + 1)..]));
                    continue;
                }
                StateRef(s, false);
            }
            if (open.Count > 0) throw new MermaidException($"The state “{open.Peek().Title}” is never closed with }}.", LineNumber);
        }

        static string FirstWord(string s)
        {
            int n = 0;
            while (n < s.Length && (char.IsLetterOrDigit(s[n]) || s[n] == '_')) n++;
            return n < s.Length && !char.IsWhiteSpace(s[n]) && s[n] != ':' ? "" : s[..n];
        }

        static ChartDirection? DirectionOf(string d) => d.ToUpperInvariant() switch
        {
            "LR" => ChartDirection.LeftRight,
            "RL" => ChartDirection.RightLeft,
            "BT" => ChartDirection.BottomUp,
            "TB" or "TD" => ChartDirection.TopDown,
            _ => null,
        };

        /// <summary>A state named in a transition or on its own (a :::class on it read too): [*] is where the
        /// current composite (or the whole diagram) starts, on an arrow's left, or ends, on its right.</summary>
        string StateRef(string raw, bool left)
        {
            string t = raw.Trim();
            string? cls = null;
            int colons = t.IndexOf(":::", StringComparison.Ordinal);
            if (colons >= 0)
            {
                cls = t[(colons + 3)..].Trim();
                t = t[..colons].Trim();
            }
            if (t == "[*]")
            {
                string scope = open.Count > 0 ? "_" + open.Peek().Id : "";
                var role = left ? NodeRole.Start : NodeRole.End;
                string id = (left ? "_start" : "_end") + scope;
                while (nodes.TryGetValue(id, out var taken) && roles[taken.Id] != role) id += "_";
                var pseudo = Node(id, role);
                pseudo.Lines = [];
                pseudo.Shape = left ? NodeShape.Circle : NodeShape.DoubleCircle;
                pseudo.Declared = true;
                return id;
            }
            if (t.Length == 0 || !t.All(ch => char.IsLetterOrDigit(ch) || ch is '_' or '-' or '.')) throw Unreadable(raw.Trim());
            Node(t);
            if (cls is not null) Classed(t, cls);
            return t;
        }

        void Classed(string id, string cls)
        {
            if (!nodes.TryGetValue(id, out var node)) return;
            if (Accepting.Contains(cls)) accepting.Add(id);
            else if (ToneOf(cls) is { } tone) node.Tone = tone;
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

        void StateDeclaration(string rest)
        {
            bool opens = rest.EndsWith('{');
            if (opens) rest = rest[..^1].Trim();
            string id, label;
            if (rest.StartsWith('"'))
            {
                int end = rest.IndexOf('"', 1);
                int asAt = end > 0 ? rest.IndexOf(" as ", end, StringComparison.Ordinal) : -1;
                if (asAt < 0) throw Unreadable("state " + rest);
                label = rest[..(end + 1)];
                id = rest[(asAt + 4)..].Trim();
            }
            else
            {
                int sp = rest.IndexOfAny([' ', '\t']);
                id = sp < 0 ? rest : rest[..sp];
                string after = sp < 0 ? "" : rest[sp..].Trim();
                label = "";
                if (after.StartsWith("<<", StringComparison.Ordinal))
                {
                    string kind = after.Trim('<', '>', ' ').ToLowerInvariant();
                    var special = Node(StateRef(id, false));
                    special.Lines = [];
                    special.Shape = kind == "choice" ? NodeShape.Decision : NodeShape.Box;
                    if (kind is "fork" or "join") roles[special.Id] = NodeRole.Bar;
                    special.Declared = true;
                    return;
                }
                if (after.StartsWith("as ", StringComparison.Ordinal)) label = after[3..];
                else if (after.Length > 0) throw Unreadable("state " + rest);
            }
            if (opens)
            {
                if (id.Length == 0) throw Unreadable("state " + rest);
                string groupTitle = label.Length > 0 ? string.Join(" ", Words(label)) : id;
                Group(id, groupTitle);
                return;
            }
            var node = Node(StateRef(id, false));
            if (label.Length > 0)
            {
                node.Lines = Words(label);
                node.Declared = true;
            }
        }

        // --- sequence diagrams ---

        void Sequence(List<(string Raw, int Line)> body)
        {
            var blocks = new Stack<string>();
            int notes = 0;
            foreach (var (raw, line) in body)
            {
                LineNumber = line;
                string s = raw.Trim();
                string word = s.Split((char[]?)null, 2, StringSplitOptions.RemoveEmptyEntries)[0];
                string rest = s[word.Length..].Trim();
                if (word.Equals("autonumber", StringComparison.OrdinalIgnoreCase)) { numbered = true; continue; }
                if (word.Equals("title", StringComparison.OrdinalIgnoreCase)) { title = string.Join(" ", Words(rest.TrimStart(':'))); continue; }
                if (SequenceIgnored.Contains(word) && !Message().IsMatch(s))
                {
                    if (word.Equals("create", StringComparison.OrdinalIgnoreCase) || word.Equals("destroy", StringComparison.OrdinalIgnoreCase))
                        Participant(Regex.Replace(rest, @"^(participant|actor)\s+", "", RegexOptions.IgnoreCase));
                    continue;
                }
                if (word.Equals("participant", StringComparison.OrdinalIgnoreCase) || word.Equals("actor", StringComparison.OrdinalIgnoreCase))
                {
                    var p = Participant(rest);
                    if (word.Equals("actor", StringComparison.OrdinalIgnoreCase)) roles[p.Id] = NodeRole.Actor;
                    continue;
                }
                if (word.Equals("box", StringComparison.OrdinalIgnoreCase) || word.Equals("rect", StringComparison.OrdinalIgnoreCase))
                {
                    blocks.Push("");
                    continue;
                }
                if (Blocks.Contains(word))
                {
                    blocks.Push(word);
                    steps.Add(new SeqStep(StepKind.Open, string.Join(" ", Words(rest)), Where: word.ToLowerInvariant()));
                    continue;
                }
                if (Dividers.Contains(word))
                {
                    if (blocks.Count == 0 || blocks.Peek().Length == 0) throw new MermaidException($"Line {LineNumber} has an {word.ToLowerInvariant()} outside a block.", LineNumber);
                    steps.Add(new SeqStep(StepKind.Else, string.Join(" ", Words(rest)), Where: word.ToLowerInvariant()));
                    continue;
                }
                if (s.Equals("end", StringComparison.OrdinalIgnoreCase))
                {
                    if (blocks.Count == 0) throw new MermaidException($"Line {LineNumber} has an end with no block to close.", LineNumber);
                    if (blocks.Pop().Length > 0) steps.Add(new SeqStep(StepKind.Close));
                    continue;
                }
                if (word.Equals("note", StringComparison.OrdinalIgnoreCase))
                {
                    var m = NoteLine().Match(s);
                    if (!m.Success || !m.Groups["text"].Success) throw Unreadable(s);
                    var over = m.Groups["who"].Value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Take(2).Select(w => Participant(w).Id).ToList();
                    var note = Node("note" + ++notes, NodeRole.Note);
                    note.Lines = Words(m.Groups["text"].Value);
                    note.Shape = NodeShape.Box;
                    note.Tone = Tone.Amber;
                    note.Declared = true;
                    steps.Add(new SeqStep(StepKind.Note, note.Id, Where: m.Groups["where"].Value.ToLowerInvariant()) { Over = over });
                    continue;
                }
                var msg = Message().Match(s);
                if (!msg.Success) throw Unreadable(s);
                string from = Participant(msg.Groups["from"].Value).Id, to = Participant(msg.Groups["to"].Value).Id;
                string arrow = msg.Groups["arrow"].Value;
                var lineKind = arrow.Contains("--", StringComparison.Ordinal) ? EdgeLine.Dotted : EdgeLine.Solid;
                var end = arrow.EndsWith('x') ? EdgeEnd.Cross : arrow is "->" or "-->" ? EdgeEnd.None : EdgeEnd.Arrow;
                var start = arrow.StartsWith("<<", StringComparison.Ordinal) ? EdgeEnd.Arrow : EdgeEnd.None;
                string said = msg.Groups["text"].Success ? string.Join("\n", Words(msg.Groups["text"].Value)) : "";
                Edge(new FlowEdge(from, to, said.Length > 0 ? said : null, lineKind, start, end));
                steps.Add(new SeqStep(StepKind.Message, Index: edges.Count - 1));
            }
            if (blocks.Count > 0) throw new MermaidException($"A {(blocks.Peek().Length > 0 ? blocks.Peek() : "box")} block is never closed with end.", LineNumber);
        }

        /// <summary>A participant by its id ("A", "A as Alice", "A@{…}"), declared once and named by its alias.</summary>
        MutNode Participant(string raw)
        {
            string t = raw.Trim();
            int meta = t.IndexOf("@{", StringComparison.Ordinal);
            if (meta >= 0) t = t[..meta].Trim();
            string id = t, alias = "";
            int asAt = t.IndexOf(" as ", StringComparison.Ordinal);
            if (asAt > 0)
            {
                id = t[..asAt].Trim();
                alias = t[(asAt + 4)..].Trim();
            }
            if (id.Length == 0) throw Unreadable(raw.Trim());
            bool fresh = !nodes.ContainsKey(id);
            var node = Node(id);
            if (fresh) node.Shape = NodeShape.Box;
            if (alias.Length > 0)
            {
                node.Lines = Words(alias);
                node.Declared = true;
            }
            return node;
        }

        // --- timelines ---

        void Timeline(List<(string Raw, int Line)> body)
        {
            MutNode? period = null;
            int periods = 0, sections = 0;
            foreach (var (raw, line) in body)
            {
                LineNumber = line;
                string s = raw.Trim();
                string word = FirstWord(s);
                if (word is "title")
                {
                    title = string.Join(" ", Words(s[5..]));
                    continue;
                }
                if (word is "accTitle" or "accDescr") continue;
                if (word is "section")
                {
                    if (open.Count > 0) open.Pop();
                    Group("section" + ++sections, string.Join(" ", Words(s[7..])));
                    continue;
                }
                var parts = s.Split(':');
                if (parts[0].Trim().Length > 0)
                {
                    period = Node("p" + ++periods, NodeRole.Period);
                    period.Lines = Words(parts[0]);
                    period.Shape = NodeShape.Stadium;
                    period.Declared = true;
                }
                else if (period is null) throw new MermaidException($"Line {LineNumber} has events before any period.", LineNumber);
                foreach (string happened in parts.Skip(1))
                {
                    var words = Words(happened);
                    if (words.Count == 0) continue;
                    var ev = Node($"{period!.Id}e{edges.Count(e => e.From == period.Id) + 1}", NodeRole.Event);
                    ev.Lines = words;
                    ev.Shape = NodeShape.Rounded;
                    ev.Declared = true;
                    Edge(new FlowEdge(period.Id, ev.Id, null, EdgeLine.Solid, EdgeEnd.None, EdgeEnd.None));
                }
            }
            open.Clear();
        }

        // --- mind maps ---

        void Mindmap(List<(string Raw, int Line)> body)
        {
            var stack = new Stack<(int Indent, string Id)>();
            int made = 0;
            foreach (var (raw, line) in body)
            {
                LineNumber = line;
                string s = raw.Trim();
                if (s.StartsWith("::icon(", StringComparison.Ordinal) || s.StartsWith("::", StringComparison.Ordinal) && !s.StartsWith(":::", StringComparison.Ordinal)) continue;
                int indent = 0;
                foreach (char ch in raw)
                {
                    if (ch == ' ') indent++;
                    else if (ch == '\t') indent += 4;
                    else break;
                }
                Tone? tone = null;
                var classes = Classes().Match(s);
                if (classes.Success)
                {
                    tone = classes.Groups[1].Value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Select(ToneOf).FirstOrDefault(t => t is not null);
                    s = s[..classes.Index].Trim();
                }
                string id;
                List<string> words;
                var shape = NodeShape.Rounded;
                var m = MindNode().Match(s);
                if (m.Success && Pair(m.Groups["open"].Value, m.Groups["close"].Value) is { } paired)
                {
                    id = m.Groups["id"].Success ? m.Groups["id"].Value : "n" + (made + 1);
                    words = Words(m.Groups["text"].Value);
                    shape = paired;
                }
                else
                {
                    id = "n" + (made + 1);
                    words = Words(s);
                }
                while (nodes.ContainsKey(id)) id += "_";
                while (stack.Count > 0 && stack.Peek().Indent >= indent) stack.Pop();
                if (stack.Count == 0 && made > 0) throw new MermaidException($"A mind map has one root; line {LineNumber} starts another.", LineNumber);
                var node = Node(id, made == 0 ? NodeRole.Root : NodeRole.Plain);
                node.Lines = words;
                node.Shape = shape;
                node.Tone = tone ?? Tone.None;
                node.Declared = true;
                made++;
                if (stack.Count > 0) Edge(new FlowEdge(stack.Peek().Id, id, null, EdgeLine.Solid, EdgeEnd.None, EdgeEnd.None));
                stack.Push((indent, id));
            }
        }

        /// <summary>A mind map node's shape from its brackets: [square], (rounded), ((circle)), ))bang((,
        /// )cloud( and {{hexagon}}; null when they don't pair.</summary>
        static NodeShape? Pair(string open, string close) => (open, close) switch
        {
            ("[", "]") => NodeShape.Box,
            ("(", ")") => NodeShape.Rounded,
            ("((", "))") => NodeShape.Circle,
            ("))", "((") => NodeShape.Hexagon,
            (")", "(") => NodeShape.Rounded,
            ("{{", "}}") => NodeShape.Hexagon,
            _ => null,
        };

        // --- the chart ---

        Flowchart Build()
        {
            if (form == ChartForm.State) StateShapes();
            var kept = groups.Where(Holds).ToList();
            var taken = new HashSet<string>(order.Select(n => n.Id));
            var ids = new Dictionary<MutGroup, string>();
            foreach (var g in kept)
            {
                string id = g.Id;
                for (int k = 2; taken.Contains(id); k++) id = g.Id + "_" + k;
                taken.Add(id);
                ids[g] = id;
            }
            var flowGroups = kept.Select(g => new FlowGroup(ids[g], g.Title, g.Members.ToList(), g.Parent is { } p && ids.TryGetValue(p, out string? pid) ? pid : null)).ToList();
            var flowNodes = order.Select(n => new FlowNode(n.Id, n.Lines, n.Shape, n.Tone) { Detail = n.Detail, Role = roles[n.Id] }).ToList();
            var finalEdges = edges;
            if (form == ChartForm.State)
            {
                // A transition to or from a composite state goes to where it starts, or from where it ends (its
                // first or last state, when it says neither).
                var composite = kept.ToDictionary(g => g.Id);
                string Into(string id) => composite.TryGetValue(id, out var g) && !nodes[id].Declared
                    ? g.Members.FirstOrDefault(m => roles[m] == NodeRole.Start) ?? g.Members.FirstOrDefault(m => m != id) ?? id : id;
                string OutOf(string id) => composite.TryGetValue(id, out var g) && !nodes[id].Declared
                    ? g.Members.LastOrDefault(m => roles[m] == NodeRole.End) ?? g.Members.LastOrDefault(m => m != id) ?? id : id;
                finalEdges = edges.Select(e => e with { From = OutOf(e.From), To = Into(e.To) }).ToList();
                var standIns = composite.Keys.Where(id => nodes.ContainsKey(id) && !nodes[id].Declared && finalEdges.All(e => e.From != id && e.To != id)).ToHashSet();
                flowNodes = flowNodes.Where(n => !standIns.Contains(n.Id)).ToList();
                flowGroups = flowGroups.Select(g => g with { Members = g.Members.Where(m => !standIns.Contains(m)).ToList() }).ToList();
            }
            return new Flowchart(direction, title, flowNodes, finalEdges, flowGroups) { Form = form, Steps = steps, Numbered = numbered };
        }

        bool Holds(MutGroup g) => g.Members.Count > 0 || groups.Any(c => c.Parent == g && Holds(c));

        /// <summary>A state diagram's states as an automaton's circles when every one is named by a short symbol
        /// (q0, S1, A) and says nothing more; rounded boxes otherwise; accepting states double either way.</summary>
        void StateShapes()
        {
            var states = order.Where(n => roles[n.Id] == NodeRole.Plain && n.Shape is NodeShape.Box or NodeShape.Rounded && (n.Lines.Count > 0 || n.Detail.Count > 0)
                && !groups.Any(g => g.Id == n.Id && !n.Declared)).ToList();
            bool symbols = states.Count > 0 && states.All(n => n.Detail.Count == 0 && n.Joined().Length <= 3);
            foreach (var n in states)
                n.Shape = accepting.Contains(n.Id) ? NodeShape.DoubleCircle : symbols ? NodeShape.Circle : NodeShape.Rounded;
        }
    }

    static string Joined(this MutNode n) => string.Join(" ", n.Lines);

    // --- writing each kind back ---

    /// <summary>A chart of any kind but the flowchart as canonical Mermaid of its own kind.</summary>
    internal static string KindSource(Flowchart f) => f.Form switch
    {
        ChartForm.State => StateSource(f),
        ChartForm.Sequence => SequenceSource(f),
        ChartForm.Timeline => TimelineSource(f),
        _ => MindmapSource(f),
    };

    static void FrontMatter(StringBuilder sb, Flowchart f)
    {
        if (f.Title is { Length: > 0 } title) sb.Append("---\ntitle: ").Append(title.Replace('\n', ' ')).Append("\n---\n");
    }

    /// <summary>Words that end at the end of a line (a transition's, a note's), with what would end them early or
    /// break the line escaped.</summary>
    static string Said(string text) =>
        Regex.Replace(text, @"[#&](?=#?\w+;)|<(?=[A-Za-z/])|[;:]", m => m.Value switch { "#" => "#35;", "&" => "#38;", ";" => "#59;", ":" => "#58;", _ => "#lt;" })
            .Replace("\n", "<br>");

    static string StateSource(Flowchart f)
    {
        var sb = new StringBuilder();
        FrontMatter(sb, f);
        sb.Append("stateDiagram-v2\n");
        if (f.Direction != ChartDirection.TopDown)
            sb.Append("  direction ").Append(f.Direction switch { ChartDirection.LeftRight => "LR", ChartDirection.RightLeft => "RL", _ => "BT" }).Append('\n');
        var groupOf = new Dictionary<string, string>();
        foreach (var g in f.Groups) foreach (string m in g.Members) groupOf[m] = g.Id;
        var byId = f.Nodes.ToDictionary(n => n.Id);
        string Ref(string id) => byId.TryGetValue(id, out var n) && n.Role is NodeRole.Start or NodeRole.End ? "[*]" : id;
        string? Scope(FlowEdge e) => byId.TryGetValue(e.From, out var a) && a.Role == NodeRole.Start ? groupOf.GetValueOrDefault(a.Id)
            : byId.TryGetValue(e.To, out var b) && b.Role == NodeRole.End ? groupOf.GetValueOrDefault(b.Id)
            : groupOf.GetValueOrDefault(e.From) is { } ga && ga == groupOf.GetValueOrDefault(e.To) ? ga : null;
        void States(string? scope, string indent)
        {
            foreach (var n in f.Nodes.Where(n => groupOf.GetValueOrDefault(n.Id) == scope && n.Role is NodeRole.Plain or NodeRole.Bar))
            {
                if (n.Shape == NodeShape.Decision && n.Lines.Count == 0) sb.Append(indent).Append("state ").Append(n.Id).Append(" <<choice>>\n");
                else if (n.Role == NodeRole.Bar) sb.Append(indent).Append("state ").Append(n.Id).Append(" <<fork>>\n");
                else if (n.Label != n.Id) sb.Append(indent).Append("state ").Append(Flowchart.Quote(string.Join("\n", n.Lines))).Append(" as ").Append(n.Id).Append('\n');
                else sb.Append(indent).Append(n.Id).Append('\n');
                foreach (string d in n.Detail) sb.Append(indent).Append(n.Id).Append(" : ").Append(Said(d)).Append('\n');
            }
            foreach (var g in f.Groups.Where(g => g.Parent == scope))
            {
                sb.Append(indent).Append("state ");
                sb.Append(g.Title != g.Id ? Flowchart.Quote(g.Title) + " as " + g.Id : g.Id).Append(" {\n");
                States(g.Id, indent + "  ");
                sb.Append(indent).Append("}\n");
            }
            foreach (var e in f.Edges.Where(e => Scope(e) == scope && byId.GetValueOrDefault(e.To)?.Role != NodeRole.Note))
            {
                sb.Append(indent).Append(Ref(e.From)).Append(" --> ").Append(Ref(e.To));
                if (e.Label is { Length: > 0 } l) sb.Append(" : ").Append(Said(l));
                sb.Append('\n');
            }
        }
        States(null, "  ");
        foreach (var e in f.Edges.Where(e => byId.GetValueOrDefault(e.To)?.Role == NodeRole.Note))
            sb.Append("  note right of ").Append(e.From).Append(" : ").Append(Said(string.Join("\n", byId[e.To].Lines))).Append('\n');
        if (f.Nodes.Any(n => n.Role == NodeRole.Plain && n.Shape == NodeShape.DoubleCircle)) sb.Append("  classDef accept stroke-width:3px\n");
        foreach (var n in f.Nodes.Where(n => n.Role == NodeRole.Plain))
        {
            if (n.Shape == NodeShape.DoubleCircle) sb.Append("  class ").Append(n.Id).Append(" accept\n");
            if (n.Tone != Tone.None) sb.Append("  class ").Append(n.Id).Append(' ').Append(n.Tone.ToString().ToLowerInvariant()).Append('\n');
        }
        return sb.ToString();
    }

    static string SequenceSource(Flowchart f)
    {
        var sb = new StringBuilder();
        FrontMatter(sb, f);
        sb.Append("sequenceDiagram\n");
        if (f.Numbered) sb.Append("  autonumber\n");
        foreach (var n in f.Nodes.Where(n => n.Role is NodeRole.Plain or NodeRole.Actor))
        {
            sb.Append("  ").Append(n.Role == NodeRole.Actor ? "actor " : "participant ").Append(n.Id);
            if (n.Label != n.Id) sb.Append(" as ").Append(Said(string.Join("\n", n.Lines)));
            sb.Append('\n');
        }
        var byId = f.Nodes.ToDictionary(n => n.Id);
        string indent = "  ";
        foreach (var step in f.Steps)
        {
            switch (step.Kind)
            {
                case StepKind.Message:
                    var e = f.Edges[step.Index];
                    string dash = e.Line == EdgeLine.Dotted ? "--" : "-";
                    string arrow = e.StartEnd == EdgeEnd.Arrow ? "<<" + dash + ">>" : e.EndEnd switch
                    {
                        EdgeEnd.Cross => dash + "x",
                        EdgeEnd.None => dash + ">",
                        _ => dash + ">>",
                    };
                    sb.Append(indent).Append(e.From).Append(arrow).Append(e.To).Append(": ").Append(Said(e.Label ?? "")).Append('\n');
                    break;
                case StepKind.Note:
                    sb.Append(indent).Append("Note ").Append(step.Where).Append(' ').Append(string.Join(",", step.Over)).Append(": ")
                        .Append(Said(string.Join("\n", byId[step.Text].Lines))).Append('\n');
                    break;
                case StepKind.Open:
                    sb.Append(indent).Append(step.Where).Append(step.Text.Length > 0 ? " " + Said(step.Text) : "").Append('\n');
                    indent += "  ";
                    break;
                case StepKind.Else:
                    sb.Append(indent[..^2]).Append(step.Where).Append(step.Text.Length > 0 ? " " + Said(step.Text) : "").Append('\n');
                    break;
                default:
                    indent = indent[..^2];
                    sb.Append(indent).Append("end\n");
                    break;
            }
        }
        return sb.ToString();
    }

    static string TimelineSource(Flowchart f)
    {
        var sb = new StringBuilder("timeline\n");
        if (f.Title is { Length: > 0 } title) sb.Append("  title ").Append(Said(title)).Append('\n');
        var sectionOf = new Dictionary<string, FlowGroup>();
        foreach (var g in f.Groups) foreach (string m in g.Members) sectionOf[m] = g;
        FlowGroup? section = null;
        foreach (var p in f.Nodes.Where(n => n.Role == NodeRole.Period))
        {
            var g = sectionOf.GetValueOrDefault(p.Id);
            if (g is not null && g != section) sb.Append("  section ").Append(Said(g.Title)).Append('\n');
            section = g ?? section;
            sb.Append(section is null ? "  " : "    ").Append(Said(string.Join("\n", p.Lines)));
            foreach (var e in f.Edges.Where(e => e.From == p.Id)) sb.Append(" : ").Append(Said(string.Join("\n", f.Node(e.To)?.Lines ?? [])));
            sb.Append('\n');
        }
        return sb.ToString();
    }

    static string MindmapSource(Flowchart f)
    {
        var sb = new StringBuilder();
        FrontMatter(sb, f);
        sb.Append("mindmap\n");
        var children = f.Edges.ToLookup(e => e.From, e => e.To);
        var byId = f.Nodes.ToDictionary(n => n.Id);
        void Write(string id, string indent, HashSet<string> seen)
        {
            if (!seen.Add(id) || !byId.TryGetValue(id, out var n)) return;
            string words = string.Join("<br>", n.Lines);
            bool plain = n.Shape is NodeShape.Rounded or NodeShape.Stadium && !words.Any(c => c is '(' or ')' or '[' or ']' or '{' or '}') && !words.StartsWith("::", StringComparison.Ordinal) && words.Length > 0;
            sb.Append(indent);
            if (plain) sb.Append(words);
            else
            {
                var (open, close) = n.Shape switch
                {
                    NodeShape.Circle or NodeShape.DoubleCircle => ("((", "))"),
                    NodeShape.Hexagon => ("{{", "}}"),
                    _ => ("[", "]"),
                };
                sb.Append(n.Id).Append(open).Append(words.Replace("]", ")").Replace(close, "")).Append(close);
            }
            if (n.Tone != Tone.None) sb.Append(":::").Append(n.Tone.ToString().ToLowerInvariant());
            sb.Append('\n');
            foreach (string c in children[id]) Write(c, indent + "  ", seen);
        }
        if ((f.Nodes.FirstOrDefault(n => n.Role == NodeRole.Root) ?? f.Nodes.FirstOrDefault()) is { } root) Write(root.Id, "  ", []);
        return sb.ToString();
    }
}
