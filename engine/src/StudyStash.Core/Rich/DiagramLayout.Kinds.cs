namespace StudyStash.Core.Rich;

/// <summary>
/// The layouts of the kinds that aren't flowcharts, each made of the same boxes, lines and groups a flowchart is
/// drawn with: a sequence diagram's participants across the top with their lifelines down, its messages in rows; a
/// timeline's periods along an axis with what happened in each beside them; a mind map's root with its branches on
/// both sides (or on one, when two would be too wide). The scene's arrows come first in the chart's own order, one
/// for each of its arrows, then any lines a layout adds (lifelines, an axis, a block's divider).
/// </summary>
public static partial class DiagramLayout
{
    /// <summary>A line from <paramref name="a"/> to <paramref name="b"/> (straight, or through two controls), with the
    /// markers its ends have taking their length off it.</summary>
    static SceneEdge Drawn(string from, string to, EdgeLine line, EdgeEnd startEnd, EdgeEnd endEnd, Pt a, Pt b, IReadOnlyList<string> words, Box wordsAt,
        Pt? c1 = null, Pt? c2 = null)
    {
        var outDir = ((c1 ?? b) - a).Unit();
        var inDir = (b - (c2 ?? a)).Unit();
        var startBase = a + outDir * MarkerLength(startEnd);
        var endBase = b - inDir * MarkerLength(endEnd);
        var path = new List<PathStep> { new(PathVerb.Move, startBase) };
        path.Add(c1 is { } p1 && c2 is { } p2 ? new PathStep(PathVerb.Cubic, p1, p2, endBase) : new PathStep(PathVerb.Line, endBase));
        return new SceneEdge(from, to, path, line, startEnd, endEnd, a, startBase, b, endBase, words, wordsAt);
    }

    // --- sequence diagrams ---

    const double SeqGap = 36, SeqRow = 16, SeqTop = 22, BlockPad = 14, BlockTitle = 24;

    static DiagramScene Sequence(Flowchart f, Dictionary<string, Sized> sizes, List<Words> labels, Measurer m)
    {
        var people = f.Nodes.Where(n => n.Role != NodeRole.Note).ToList();
        int count = people.Count;
        var column = people.Select((p, i) => (p.Id, i)).ToDictionary(x => x.Id, x => x.i);
        // Words numbered when the diagram says autonumber.
        var words = labels.ToList();
        if (f.Numbered)
        {
            int k = 0;
            foreach (var step in f.Steps.Where(s => s.Kind == StepKind.Message && s.Index >= 0))
            {
                string said = $"{++k}. " + (f.Edges[step.Index].Label?.Replace('\n', ' ') ?? "");
                words[step.Index] = EdgeLabel(said.TrimEnd(), m);
            }
        }

        // Columns far enough apart for each box, each message's words, and the notes beside a lifeline.
        var gap = new double[Math.Max(0, count - 1)];
        for (int i = 0; i + 1 < count; i++) gap[i] = sizes[people[i].Id].W / 2 + sizes[people[i + 1].Id].W / 2 + SeqGap;
        double rightRoom = sizes[people[^1].Id].W / 2, leftRoom = sizes[people[0].Id].W / 2;
        void Need(int lo, int hi, double room)
        {
            if (lo == hi) return;
            double have = 0;
            for (int i = lo; i < hi; i++) have += gap[i];
            if (have >= room) return;
            for (int i = lo; i < hi; i++) gap[i] += (room - have) / (hi - lo);
        }
        foreach (var step in f.Steps)
        {
            if (step.Kind == StepKind.Message && step.Index >= 0)
            {
                var e = f.Edges[step.Index];
                int a = column[e.From], b = column[e.To];
                var w = words[step.Index];
                if (a == b)
                {
                    double room = 34 + w.W + 8;
                    if (a + 1 < count) Need(a, a + 1, room + sizes[people[a + 1].Id].W / 2);
                    else rightRoom = Math.Max(rightRoom, room);
                }
                else Need(Math.Min(a, b), Math.Max(a, b), w.W + 28);
            }
            else if (step.Kind == StepKind.Note && step.Over.Count > 0)
            {
                var size = sizes[step.Text];
                int a = column[step.Over[0]], b = column[step.Over[^1]];
                if (step.Where == "right of")
                {
                    if (a + 1 < count) Need(a, a + 1, size.W + 24 + sizes[people[a + 1].Id].W / 2);
                    else rightRoom = Math.Max(rightRoom, size.W + 12);
                }
                else if (step.Where == "left of")
                {
                    if (a > 0) Need(a - 1, a, size.W + 24 + sizes[people[a - 1].Id].W / 2);
                    else leftRoom = Math.Max(leftRoom, size.W + 12);
                }
                else if (a == b) leftRoom = Math.Max(leftRoom, a == 0 ? size.W / 2 : 0);
            }
        }
        var x = new double[count];
        x[0] = leftRoom;
        for (int i = 1; i < count; i++) x[i] = x[i - 1] + gap[i - 1];
        double tallest = people.Max(p => sizes[p.Id].H);
        var nodes = new List<SceneNode>();
        foreach (var p in people)
        {
            var s = sizes[p.Id];
            var box = new Box(x[column[p.Id]] - s.W / 2, tallest - s.H, s.W, s.H);
            nodes.Add(Placed(p, s, box) with { Shape = p.Role == NodeRole.Actor ? NodeShape.Stadium : p.Shape });
        }

        // The rows, top to bottom.
        double y = tallest + SeqTop;
        var edges = new SceneEdge?[f.Edges.Count];
        var extra = new List<SceneEdge>();
        var groups = new List<SceneGroup>();
        var open = new Stack<(SeqStep Step, double Top, HashSet<int> Columns, List<(double Y, SeqStep Step)> Dividers, double[] Span)>();
        void Touch(params int[] cols)
        {
            foreach (var b in open) foreach (int c in cols) b.Columns.Add(c);
        }
        // What a block holds reaches this far across (a self-message's words, a note), so the block takes it in.
        void Reach(double left, double right)
        {
            foreach (var b in open)
            {
                b.Span[0] = Math.Min(b.Span[0], left);
                b.Span[1] = Math.Max(b.Span[1], right);
            }
        }
        foreach (var step in f.Steps)
        {
            switch (step.Kind)
            {
                case StepKind.Message when step.Index >= 0:
                {
                    var e = f.Edges[step.Index];
                    var w = words[step.Index];
                    int a = column[e.From], b = column[e.To];
                    Touch(a, b);
                    if (a == b)
                    {
                        double top = y + 4, xa = x[a];
                        var loopEnd = new Pt(xa, top + 22);
                        var edge = Drawn(e.From, e.To, e.Line, e.StartEnd, e.EndEnd, new Pt(xa, top), loopEnd, w.Lines,
                            w.Lines.Count > 0 ? new Box(xa + 36, top + 11 - w.H / 2, w.W, w.H) : new Box(), new Pt(xa + 40, top - 2), new Pt(xa + 40, top + 24));
                        edges[step.Index] = edge;
                        Reach(xa, xa + 44 + (w.Lines.Count > 0 ? w.W : 0));
                        y = top + Math.Max(26, w.H) + SeqRow;
                    }
                    else
                    {
                        double arrowY = y + (w.Lines.Count > 0 ? w.H + 3 : 4);
                        double from = x[a], to = x[b];
                        var labelBox = w.Lines.Count > 0 ? new Box((from + to) / 2 - w.W / 2, arrowY - 3 - w.H, w.W, w.H) : new Box();
                        edges[step.Index] = Drawn(e.From, e.To, e.Line, e.StartEnd, e.EndEnd, new Pt(from, arrowY), new Pt(to, arrowY), w.Lines, labelBox);
                        Reach(Math.Min(Math.Min(from, to), labelBox.W > 0 ? labelBox.X : double.MaxValue), Math.Max(Math.Max(from, to), labelBox.Right));
                        y = arrowY + SeqRow;
                    }
                    break;
                }
                case StepKind.Note when f.Node(step.Text) is { } note:
                {
                    var s = sizes[note.Id];
                    var cols = step.Over.Select(o => column[o]).ToList();
                    Touch([.. cols]);
                    double left = cols.Count == 0 ? 0 : x[cols.Min()], right = cols.Count == 0 ? 0 : x[cols.Max()];
                    double w = step.Where == "over" ? Math.Max(s.W, right - left + 40) : s.W;
                    double bx = step.Where switch
                    {
                        "right of" => left + 10,
                        "left of" => left - 10 - w,
                        _ => (left + right) / 2 - w / 2,
                    };
                    nodes.Add(Placed(note, s, new Box(bx, y, w, s.H)));
                    Reach(bx, bx + w);
                    y += s.H + SeqRow;
                    break;
                }
                case StepKind.Open:
                    open.Push((step, y, [], [], [double.MaxValue, double.MinValue]));
                    y += BlockTitle + 6;
                    break;
                case StepKind.Else when open.Count > 0:
                    open.Peek().Dividers.Add((y, step));
                    y += BlockTitle;
                    break;
                case StepKind.Close when open.Count > 0:
                {
                    var (s, top, cols, dividers, span) = open.Pop();
                    if (cols.Count == 0) cols = [.. Enumerable.Range(0, count)];
                    foreach (var outer in open) outer.Columns.UnionWith(cols);
                    double inset = open.Count * 7;
                    string titleText = s.Text.Length > 0 ? $"{s.Where}: {s.Text}" : s.Where;
                    double left = Math.Min(x[cols.Min()] - (cols.Count == 1 ? 60 : BlockPad + 40), span[0] - BlockPad) + inset;
                    double right = Math.Max(Math.Max(x[cols.Max()] + (cols.Count == 1 ? 60 : BlockPad + 40), span[1] + BlockPad), left + m.Width(titleText, TitleSize, true) + 24) - inset;
                    Reach(left, right);
                    double bottom = y + 2;
                    var box = new Box(left, top, right - left, bottom - top);
                    groups.Add(new SceneGroup("block" + groups.Count, titleText, box, new Box(left + 10, top + 5, m.Width(titleText, TitleSize, true), 16), open.Count > 0 ? 1 : 0));
                    foreach (var (dy, d) in dividers)
                    {
                        string dt = d.Text.Length > 0 ? $"{d.Where}: {d.Text}" : d.Where;
                        var dw = EdgeLabel(dt, m);
                        string id = people[cols.Min()].Id;
                        extra.Add(Drawn(id, id, EdgeLine.Dotted, EdgeEnd.None, EdgeEnd.None, new Pt(left, dy + 2), new Pt(right, dy + 2), dw.Lines,
                            new Box(left + 10, dy + 6, dw.W, dw.H)));
                    }
                    y = bottom + SeqRow;
                    break;
                }
            }
        }
        // Lifelines, from under each box to below the last row, behind everything else.
        var lifelines = people.Select(p =>
        {
            var b = nodes.First(n => n.Id == p.Id).Box;
            return Drawn(p.Id, p.Id, EdgeLine.Dotted, EdgeEnd.None, EdgeEnd.None, new Pt(b.Center.X, b.Bottom), new Pt(b.Center.X, y + 4), [], new Box());
        });
        Unplaced(f, edges, nodes.ToDictionary(n => n.Id), labels);
        var all = edges.Select(e => e!).Concat(lifelines).Concat(extra).ToList();
        var ordered = f.Nodes.Select(n => nodes.FirstOrDefault(s => s.Id == n.Id)).Where(n => n is not null).Select(n => n!).ToList();
        return new DiagramScene(SceneKind.Sequence, ChartDirection.TopDown, 0, 0, ordered, all, groups);
    }

    /// <summary>Any arrow a layout had no place for (a mind map's link across its branches, a box it never reached):
    /// a dotted line straight from box to box, so nothing the chart says goes missing.</summary>
    static void Unplaced(Flowchart f, SceneEdge?[] edges, Dictionary<string, SceneNode> nodes, List<Words>? labels)
    {
        for (int i = 0; i < edges.Length; i++)
        {
            if (edges[i] is not null) continue;
            var e = f.Edges[i];
            var words = labels?[i] ?? new Words([], 0, 0);
            if (nodes.TryGetValue(e.From, out var a) && nodes.TryGetValue(e.To, out var b) && a != b)
                edges[i] = Straight(e with { Line = EdgeLine.Dotted }, a, b, words);
            else if (nodes.TryGetValue(e.From, out var only))
                edges[i] = Drawn(e.From, e.To, EdgeLine.Solid, EdgeEnd.None, EdgeEnd.None, only.Box.Center, only.Box.Center, [], new Box());
        }
    }

    // --- timelines ---

    const double PeriodGap = 18, EventGap = 8, SectionPad = 12, SectionTitle = 30;

    static DiagramScene Timeline(Flowchart f, Dictionary<string, Sized> sizes, ChartDirection dir, Measurer m)
    {
        var periods = f.Nodes.Where(n => n.Role == NodeRole.Period).ToList();
        var eventsOf = periods.ToDictionary(p => p.Id, p => f.Edges.Select((e, i) => (e, i)).Where(x => x.e.From == p.Id).ToList());
        var sectionOf = new Dictionary<string, FlowGroup>();
        foreach (var g in f.Groups) foreach (string mem in g.Members) sectionOf[mem] = g;
        bool sections = f.Groups.Count > 0;
        var nodes = new Dictionary<string, SceneNode>();
        var edges = new SceneEdge?[f.Edges.Count];
        var axis = new List<SceneEdge>();
        var groups = new List<SceneGroup>();
        SceneNode Put(FlowNode n, Box box, Tone? tone = null) => nodes[n.Id] = Placed(n, sizes[n.Id], box) with { Tone = tone ?? n.Tone };

        if (dir is ChartDirection.LeftRight or ChartDirection.RightLeft)
        {
            // Periods along the top, each a column with what happened in it hanging below.
            double x = 0, top = sections ? SectionTitle : 0;
            double pillH = periods.Max(p => sizes[p.Id].H);
            var columns = new List<(FlowNode Period, double X, double W)>();
            FlowGroup? current = null;
            foreach (var p in periods)
            {
                var g = sectionOf.GetValueOrDefault(p.Id);
                if (columns.Count > 0 && g != current && sections) x += SectionPad * 2 + 4;
                current = g;
                double w = Math.Max(sizes[p.Id].W, eventsOf[p.Id].Select(ev => sizes[ev.e.To].W).DefaultIfEmpty(0).Max());
                w = Math.Max(w, 96);
                columns.Add((p, x, w));
                x += w + PeriodGap;
            }
            foreach (var (p, cx, w) in columns)
            {
                var pill = Put(p, Box.Around(new Pt(cx + w / 2, top + pillH / 2), sizes[p.Id].W, sizes[p.Id].H), p.Tone == Tone.None ? Tone.Accent : p.Tone);
                double y = top + pillH + 14;
                SceneNode above = pill;
                foreach (var (e, i) in eventsOf[p.Id])
                {
                    var ev = f.Node(e.To)!;
                    var box = new Box(cx, y, w, sizes[ev.Id].H);
                    var placed = Put(ev, box);
                    edges[i] = Drawn(e.From, e.To, EdgeLine.Solid, EdgeEnd.None, EdgeEnd.None, new Pt(cx + w / 2, above.Box.Bottom), new Pt(cx + w / 2, box.Y), [], new Box());
                    above = placed;
                    y = box.Bottom + EventGap;
                }
            }
            for (int k = 0; k + 1 < columns.Count; k++)
            {
                var a = nodes[columns[k].Period.Id].Box;
                var b = nodes[columns[k + 1].Period.Id].Box;
                axis.Add(Drawn(columns[k].Period.Id, columns[k + 1].Period.Id, EdgeLine.Thick, EdgeEnd.None, k + 2 == columns.Count ? EdgeEnd.Arrow : EdgeEnd.None,
                    new Pt(a.Right, a.Center.Y), new Pt(b.X, b.Center.Y), [], new Box()));
            }
            foreach (var g in f.Groups)
            {
                var inside = g.Members.Where(nodes.ContainsKey).Select(id => nodes[id].Box).ToList();
                if (inside.Count == 0) continue;
                var bound = inside.Aggregate((a, b) => a.Union(b));
                var box = new Box(bound.X - SectionPad, bound.Y - SectionTitle + 2, bound.W + 2 * SectionPad, bound.H + SectionTitle - 2 + SectionPad);
                groups.Add(new SceneGroup(g.Id, g.Title, box, new Box(box.X + 12, box.Y + 7, m.Width(g.Title, TitleSize, true), 16), 0));
            }
        }
        else
        {
            // Down the page: each period on the axis at the left, what happened in it beside it, one under another.
            double pillW = periods.Max(p => sizes[p.Id].W);
            double axisX = (sections ? SectionPad : 0) + pillW / 2;
            double eventX = axisX + pillW / 2 + 26;
            double eventW = Math.Max(120, f.Nodes.Where(n => n.Role == NodeRole.Event).Select(n => sizes[n.Id].W).DefaultIfEmpty(0).Max());
            double y = 0;
            FlowGroup? current = null;
            var bands = new List<(FlowGroup Group, double Top, double Bottom)>();
            foreach (var p in periods)
            {
                var g = sectionOf.GetValueOrDefault(p.Id);
                if (g != current)
                {
                    if (current is not null) { bands[^1] = bands[^1] with { Bottom = y - PeriodGap + SectionPad }; y += SectionPad + 6; }
                    if (g is not null) { bands.Add((g, y, y)); y += SectionTitle; }
                    current = g;
                }
                var evs = eventsOf[p.Id];
                double evH = evs.Sum(ev => sizes[ev.e.To].H) + Math.Max(0, evs.Count - 1) * EventGap;
                double rowH = Math.Max(sizes[p.Id].H, evH);
                var pill = Put(p, Box.Around(new Pt(axisX, y + sizes[p.Id].H / 2), sizes[p.Id].W, sizes[p.Id].H), p.Tone == Tone.None ? Tone.Accent : p.Tone);
                double ey = y;
                SceneNode? above = null;
                foreach (var (e, i) in evs)
                {
                    var ev = f.Node(e.To)!;
                    var box = new Box(eventX, ey, eventW, sizes[ev.Id].H);
                    var placed = Put(ev, box);
                    edges[i] = above is null
                        ? Drawn(e.From, e.To, EdgeLine.Solid, EdgeEnd.None, EdgeEnd.None, new Pt(pill.Box.Right, pill.Box.Center.Y), new Pt(box.X, pill.Box.Center.Y), [], new Box())
                        : Drawn(e.From, e.To, EdgeLine.Solid, EdgeEnd.None, EdgeEnd.None, new Pt(box.X + 16, above.Box.Bottom), new Pt(box.X + 16, box.Y), [], new Box());
                    above = placed;
                    ey = box.Bottom + EventGap;
                }
                y += rowH + PeriodGap;
            }
            if (current is not null) bands[^1] = bands[^1] with { Bottom = y - PeriodGap + SectionPad };
            for (int k = 0; k + 1 < periods.Count; k++)
            {
                var a = nodes[periods[k].Id].Box;
                var b = nodes[periods[k + 1].Id].Box;
                axis.Add(Drawn(periods[k].Id, periods[k + 1].Id, EdgeLine.Thick, EdgeEnd.None, k + 2 == periods.Count ? EdgeEnd.Arrow : EdgeEnd.None,
                    new Pt(axisX, a.Bottom), new Pt(axisX, b.Y), [], new Box()));
            }
            double right = eventX + eventW + SectionPad;
            foreach (var (g, top, bottom) in bands)
                groups.Add(new SceneGroup(g.Id, g.Title, new Box(0, top, right, bottom - top), new Box(12, top + 7, m.Width(g.Title, TitleSize, true), 16), 0));
        }
        var ordered = f.Nodes.Where(n => nodes.ContainsKey(n.Id)).Select(n => nodes[n.Id]).ToList();
        Unplaced(f, edges, nodes, null);
        return new DiagramScene(SceneKind.Timeline, dir, 0, 0, ordered, [.. edges.Select(e => e!), .. axis], groups);
    }

    // --- mind maps ---

    const double MindGap = 40, MindSpace = 10;

    /// <summary>The tones a mind map's branches take, in turn, when it gives none of its own.</summary>
    static readonly Tone[] BranchTones = [Tone.Blue, Tone.Green, Tone.Amber, Tone.Purple, Tone.Red];

    static DiagramScene Mindmap(Flowchart f, Dictionary<string, Sized> sizes, List<Words> labels, ChartDirection dir, Measurer measure)
    {
        sizes = new Dictionary<string, Sized>(sizes);
        var root = f.Nodes.FirstOrDefault(n => n.Role == NodeRole.Root) ?? f.Nodes[0];
        var kids = f.Edges.Select((e, i) => (e, i)).Where(x => x.e.From != x.e.To).ToLookup(x => x.e.From);
        var seen = new HashSet<string> { root.Id };
        var children = new Dictionary<string, List<(FlowEdge E, int I)>>();
        var queue = new Queue<string>([root.Id]);
        while (queue.Count > 0)
        {
            string at = queue.Dequeue();
            children[at] = [];
            foreach (var (e, i) in kids[at])
                if (seen.Add(e.To))
                {
                    children[at].Add((e, i));
                    queue.Enqueue(e.To);
                }
        }
        var span = new Dictionary<string, double>();
        double Span(string id) => span.TryGetValue(id, out double s) ? s
            : span[id] = Math.Max(sizes[id].H, children[id].Sum(c => Span(c.E.To)) + Math.Max(0, children[id].Count - 1) * MindSpace);
        var branches = children[root.Id];
        // Both sides when there's more than one branch and the map was left as written; else all on the right.
        var right = new List<(FlowEdge E, int I)>();
        var left = new List<(FlowEdge E, int I)>();
        if (dir is ChartDirection.LeftRight or ChartDirection.RightLeft && branches.Count > 1)
        {
            double total = branches.Sum(b => Span(b.E.To)), sum = 0;
            foreach (var b in branches)
            {
                if (sum < total / 2 || right.Count == 0) { right.Add(b); sum += Span(b.E.To); }
                else left.Add(b);
            }
            left.Reverse();
        }
        else right.AddRange(branches);

        bool coloured = f.Nodes.Any(n => n.Tone != Tone.None);
        var tone = new Dictionary<string, Tone>();
        for (int k = 0; k < branches.Count; k++) tone[branches[k].E.To] = coloured ? Tone.None : BranchTones[k % BranchTones.Length];
        var boxes = new Dictionary<string, Box>();
        var edges = new SceneEdge?[f.Edges.Count];
        // A root whose words make too big a circle is a pill instead.
        var rootShape = root.Shape is NodeShape.Circle or NodeShape.DoubleCircle && sizes[root.Id].W > 140 ? NodeShape.Stadium : root.Shape;
        if (rootShape != root.Shape) sizes[root.Id] = Size(root with { Shape = rootShape }, measure);
        var rootSize = sizes[root.Id];
        var rootBox = Box.Around(new Pt(0, 0), rootSize.W, rootSize.H);
        boxes[root.Id] = rootBox;

        // A subtree beside its parent: its children stacked, the parent's side facing theirs, each joined by a soft S.
        void Place(string id, double x, double top, int side)
        {
            var s = sizes[id];
            double mid = top + Span(id) / 2;
            boxes[id] = side > 0 ? new Box(x, mid - s.H / 2, s.W, s.H) : new Box(x - s.W, mid - s.H / 2, s.W, s.H);
            double childX = side > 0 ? boxes[id].Right + MindGap : boxes[id].X - MindGap;
            double kidsTop = mid - (children[id].Sum(c => Span(c.E.To)) + Math.Max(0, children[id].Count - 1) * MindSpace) / 2;
            foreach (var (e, i) in children[id])
            {
                Place(e.To, childX, kidsTop, side);
                kidsTop += Span(e.To) + MindSpace;
            }
        }
        void Side(List<(FlowEdge E, int I)> list, int side)
        {
            double total = list.Sum(b => Span(b.E.To)) + Math.Max(0, list.Count - 1) * MindSpace * 2;
            double top = -total / 2;
            foreach (var (e, _) in list)
            {
                Place(e.To, side > 0 ? rootBox.Right + MindGap * 1.4 : rootBox.X - MindGap * 1.4, top, side);
                top += Span(e.To) + MindSpace * 2;
            }
        }
        Side(right, 1);
        Side(left, -1);

        foreach (var (from, list) in children)
            foreach (var (e, i) in list)
            {
                var a = boxes[from];
                var b = boxes[e.To];
                int side = b.Center.X >= a.Center.X ? 1 : -1;
                Pt start = from == root.Id
                    ? new Pt(a.Center.X + side * Math.Min(a.W / 2, Math.Abs(b.Center.Y - a.Center.Y) < a.H / 2 ? a.W / 2 : a.W / 2 - 6), a.Center.Y + Math.Clamp(b.Center.Y - a.Center.Y, -a.H / 4, a.H / 4))
                    : new Pt(side > 0 ? a.Right : a.X, a.Center.Y);
                if (from == root.Id && rootShape is NodeShape.Circle or NodeShape.DoubleCircle)
                {
                    var toward = (b.Center - a.Center).Unit();
                    start = a.Center + toward * (a.W / 2);
                }
                var end = new Pt(side > 0 ? b.X : b.Right, b.Center.Y);
                double bend = Math.Abs(end.X - start.X) * 0.5;
                edges[i] = Drawn(e.From, e.To, from == root.Id ? EdgeLine.Thick : EdgeLine.Solid, EdgeEnd.None, EdgeEnd.None, start, end, [], new Box(),
                    new Pt(start.X + side * bend, start.Y), new Pt(end.X - side * bend, end.Y));
            }
        var nodes = f.Nodes.Where(n => boxes.ContainsKey(n.Id)).Select(n =>
        {
            var t = n.Tone != Tone.None ? n.Tone : n.Id == root.Id && !coloured ? Tone.Accent : tone.GetValueOrDefault(n.Id, Tone.None);
            return Placed(n, sizes[n.Id], boxes[n.Id]) with { Tone = t, Shape = n.Id == root.Id ? rootShape : n.Shape };
        }).ToList();
        Unplaced(f, edges, nodes.ToDictionary(n => n.Id), labels);
        return new DiagramScene(SceneKind.Mindmap, dir, 0, 0, nodes, edges.Select(e => e!).ToList(), []);
    }
}
