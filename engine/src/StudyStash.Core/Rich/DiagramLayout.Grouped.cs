using Microsoft.Msagl.Core.Geometry;
using Microsoft.Msagl.Core.Geometry.Curves;
using Microsoft.Msagl.Core.Layout;
using Microsoft.Msagl.Routing.Rectilinear;
using MPoint = Microsoft.Msagl.Core.Geometry.Point;
using MsaglEdge = Microsoft.Msagl.Core.Layout.Edge;
using MsaglNode = Microsoft.Msagl.Core.Layout.Node;

namespace StudyStash.Core.Rich;

/// <summary>
/// A big chart in groups (a whole lecture's topic: phases of a process, parts of a system, each with its own steps),
/// laid out in two steps so it reads at a glance and fits a notes column. Each group is laid out on its own, across
/// the chart's flow (a left-to-right chart's groups are columns read downwards; a top-down chart's, rows read across,
/// unless a row would be too wide), then the groups are placed as blocks along the flow by the same layered layout
/// as any chart, so the groups alone read as a diagram of their own. The arrows between groups are routed last,
/// around every box and group in the way.
/// </summary>
public static partial class DiagramLayout
{
    /// <summary>A chart in groups is laid out this way once it has more boxes than this, or three groups side by
    /// side; a smaller one is one layered chart, as before.</summary>
    public const int GroupedFrom = 12;

    /// <summary>The widest a group laid out across the flow may be before it's laid out along it instead.</summary>
    const double InnerMax = 520;

    const double GroupPad = 12, GroupTop = 30;

    /// <summary>Whether a chart's groups are laid out as blocks (see <see cref="Grouped"/>).</summary>
    internal static bool IsGrouped(Flowchart f) =>
        f.Groups.Count > 0 && f.Form is ChartForm.Flowchart or ChartForm.State
        && (f.Nodes.Count > GroupedFrom || f.Groups.Count(g => g.Parent is null) >= 3);

    /// <summary>Which way a group's own boxes run when the chart flows <paramref name="dir"/>: across it.</summary>
    static ChartDirection Across(ChartDirection dir) => dir is ChartDirection.LeftRight or ChartDirection.RightLeft ? ChartDirection.TopDown : ChartDirection.LeftRight;

    /// <summary>The outermost group each box is in (its own id when it's in none).</summary>
    static Dictionary<string, string> Units(Flowchart f)
    {
        var parent = f.Groups.ToDictionary(g => g.Id, g => g.Parent);
        string Top(string group)
        {
            while (parent.TryGetValue(group, out var p) && p is not null) group = p;
            return group;
        }
        var unit = f.Nodes.ToDictionary(n => n.Id, n => n.Id);
        foreach (var g in f.Groups)
            foreach (string m in g.Members) if (unit.ContainsKey(m)) unit[m] = Top(g.Id);
        return unit;
    }

    /// <summary>The part of the chart inside one outermost group: its boxes, the arrows between them, and the groups
    /// inside it (now outermost), with the index in the whole chart of each arrow kept.</summary>
    static (Flowchart Chart, List<int> Index) Inside(Flowchart f, string group, Dictionary<string, string> unit, ChartDirection dir)
    {
        var members = f.Nodes.Where(n => unit[n.Id] == group).ToList();
        var ids = members.Select(n => n.Id).ToHashSet();
        var index = new List<int>();
        for (int i = 0; i < f.Edges.Count; i++)
            if (ids.Contains(f.Edges[i].From) && ids.Contains(f.Edges[i].To)) index.Add(i);
        var inner = f.Groups.Where(g => g.Parent == group).Select(g => g with { Parent = null }).ToList();
        return (new Flowchart(dir, null, members, index.Select(i => f.Edges[i]).ToList(), inner) { Form = f.Form }, index);
    }

    /// <summary>A part of a chart laid out the plain way (a ring, a tree, or layered with its groups), at scale 1.</summary>
    static DiagramScene Plain(Flowchart part, Dictionary<string, Sized> sizes, List<Words> labels, ChartDirection dir, Measurer m) =>
        Normalise(part.Groups.Count == 0 && Kind(part) is var k && k is SceneKind.Ring or SceneKind.Tree
            ? k == SceneKind.Ring ? Ring(part, sizes, labels) : Tree(part, sizes, labels, dir)
            : Layered(part, sizes, labels, dir, m));

    static DiagramScene Grouped(Flowchart f, Dictionary<string, Sized> sizes, List<Words> labels, ChartDirection dir, Measurer m)
    {
        var unit = Units(f);
        var tops = f.Groups.Where(g => g.Parent is null).ToList();
        // Each group on its own, across the flow (along it, if across would be too wide).
        var inner = new Dictionary<string, (DiagramScene Scene, Box Size)>();
        foreach (var g in tops)
        {
            var across = Across(dir);
            var (part, index) = Inside(f, g.Id, unit, across);
            if (part.Nodes.Count == 0) continue;
            var partLabels = index.Select(i => labels[i]).ToList();
            var scene = Plain(part, sizes, partLabels, across, m);
            if (across == ChartDirection.LeftRight && scene.Width > InnerMax)
            {
                var along = Plain(part.WithDirection(ChartDirection.TopDown), sizes, partLabels, ChartDirection.TopDown, m);
                if (along.Width < scene.Width) scene = along;
            }
            double title = m.Width(g.Title, TitleSize, true) + 2 * GroupPad + 4;
            inner[g.Id] = (scene, new Box(0, 0, Math.Max(title, scene.Width + 2 * GroupPad), scene.Height + GroupTop + GroupPad));
        }

        // The groups (and the boxes in none) as blocks, placed along the flow like any chart's boxes; an arrow between
        // two blocks stands for every arrow between their boxes, and keeps the room its words need.
        var blocks = new List<FlowNode>();
        var blockSizes = new Dictionary<string, Sized>();
        foreach (var n in f.Nodes)
        {
            string u = unit[n.Id];
            if (blockSizes.ContainsKey(u)) continue;
            if (u == n.Id) blockSizes[u] = sizes[u];
            else if (inner.TryGetValue(u, out var made)) blockSizes[u] = new Sized(made.Size.W, made.Size.H, []);
            else continue;
            blocks.Add(new FlowNode(u, [], NodeShape.Box, Tone.None));
        }
        var blockEdges = new List<FlowEdge>();
        var blockLabels = new List<Words>();
        var between = new List<int>();
        for (int i = 0; i < f.Edges.Count; i++)
        {
            var e = f.Edges[i];
            string a = unit[e.From], b = unit[e.To];
            if (a == b) continue;
            between.Add(i);
            if (blockEdges.Any(x => x.From == a && x.To == b)) continue;
            blockEdges.Add(new FlowEdge(a, b, e.Label, e.Line, EdgeEnd.None, EdgeEnd.Arrow));
            blockLabels.Add(labels[i]);
        }
        var placed = Layered(new Flowchart(dir, null, blocks, blockEdges, []), blockSizes, blockLabels, dir, m);
        var at = Aligned(placed.Nodes.ToDictionary(n => n.Id, n => n.Box), dir);

        // Everything in its place: each group's own layout moved into its block, each loose box where its block is.
        var nodes = new List<SceneNode>();
        var edges = new SceneEdge?[f.Edges.Count];
        var groups = new List<SceneGroup>();
        foreach (var g in tops)
        {
            if (!inner.TryGetValue(g.Id, out var made)) continue;
            var box = at[g.Id];
            double dx = box.X + (box.W - made.Scene.Width) / 2, dy = box.Y + GroupTop;
            groups.Add(new SceneGroup(g.Id, g.Title, box, new Box(box.X + 12, box.Y + 7, m.Width(g.Title, TitleSize, true), 16), 0));
            nodes.AddRange(made.Scene.Nodes.Select(n => n with { Box = n.Box.Offset(dx, dy) }));
            groups.AddRange(made.Scene.Groups.Select(x => x with { Box = x.Box.Offset(dx, dy), TitleBox = x.TitleBox.Offset(dx, dy), Depth = 1 }));
            var (_, index) = Inside(f, g.Id, unit, dir);
            for (int k = 0; k < index.Count; k++) edges[index[k]] = Moved(made.Scene.Edges[k], dx, dy);
        }
        foreach (var n in f.Nodes.Where(n => unit[n.Id] == n.Id))
            nodes.Add(Placed(n, sizes[n.Id], Box.Around(at[n.Id].Center, sizes[n.Id].W, sizes[n.Id].H)));
        var byId = nodes.ToDictionary(n => n.Id);
        for (int i = 0; i < f.Edges.Count; i++)
            if (edges[i] is null && f.Edges[i].From == f.Edges[i].To) edges[i] = Loop(f.Edges[i], byId[f.Edges[i].From], labels[i]);

        // The arrows between groups, routed around everything in the way.
        foreach (var (i, edge) in Routed(f, between, byId, groups, labels, edges.Where(e => e is not null).Select(e => e!).ToList())) edges[i] = edge;
        var ordered = f.Nodes.Select(n => byId[n.Id]).ToList();
        var whole = new DiagramScene(SceneKind.Grouped, dir, 0, 0, ordered, edges.Select(e => e!).ToList(), groups);
        return Titles(ByTheirLines(whole), out _);
    }

    /// <summary>
    /// The blocks lined up layer by layer, in the order the layered layout gave them: a left-to-right chart's columns
    /// each stacked from the same top, a top-down chart's rows side by side from the same top, each row centred. The
    /// layout centres every block on its arrows, which leaves a chart of big blocks staggered; lined up, the groups
    /// read as columns (or rows) of one table.
    /// </summary>
    static Dictionary<string, Box> Aligned(Dictionary<string, Box> at, ChartDirection dir)
    {
        bool columns = dir is ChartDirection.LeftRight or ChartDirection.RightLeft;
        var layers = new List<List<string>>();
        double end = double.MinValue;
        foreach (var (id, b) in at.OrderBy(kv => columns ? kv.Value.X : kv.Value.Y))
        {
            double lo = columns ? b.X : b.Y, hi = columns ? b.Right : b.Bottom;
            if (layers.Count == 0 || lo >= end - 1) { layers.Add([id]); end = hi; }
            else { layers[^1].Add(id); end = Math.Max(end, hi); }
        }
        var lined = new Dictionary<string, Box>();
        double middle = at.Values.Select(b => b.Center.X).DefaultIfEmpty(0).Average();
        double top = at.Values.Select(b => b.Y).DefaultIfEmpty(0).Min();
        foreach (var layer in layers)
        {
            var inOrder = layer.OrderBy(id => columns ? at[id].Center.Y : at[id].Center.X).ToList();
            if (columns)
            {
                double y = top;
                foreach (string id in inOrder)
                {
                    lined[id] = at[id] with { Y = y };
                    y += at[id].H + 28;
                }
            }
            else
            {
                double rowTop = inOrder.Min(id => at[id].Y);
                double width = inOrder.Sum(id => at[id].W) + (inOrder.Count - 1) * 28;
                double x = middle - width / 2;
                foreach (string id in inOrder)
                {
                    lined[id] = new Box(x, rowTop, at[id].W, at[id].H);
                    x += at[id].W + 28;
                }
            }
        }
        return lined;
    }

    static SceneEdge Moved(SceneEdge e, double dx, double dy)
    {
        Pt M(Pt p) => new(p.X + dx, p.Y + dy);
        return e with
        {
            Path = e.Path.Select(p => new PathStep(p.Verb, M(p.A), M(p.B), M(p.C))).ToList(),
            StartTip = M(e.StartTip),
            StartBase = M(e.StartBase),
            Tip = M(e.Tip),
            Base = M(e.Base),
            LabelBox = e.LabelBox.W == 0 && e.LabelBox.H == 0 ? e.LabelBox : e.LabelBox.Offset(dx, dy),
        };
    }

    /// <summary>
    /// The arrows <paramref name="which"/> names, routed with every box and group where it already is: MSAGL's
    /// rectilinear router runs them square through the gaps, clear of the boxes they don't join, into the groups
    /// they reach, with rounded corners. One it can't route is a straight line from box to box. Each arrow's words sit
    /// above its longest level stretch.
    /// </summary>
    static IEnumerable<(int Index, SceneEdge Edge)> Routed(Flowchart f, List<int> which, Dictionary<string, SceneNode> byId, List<SceneGroup> groups, List<Words> labels,
        List<SceneEdge> inside)
    {
        // Whether one of a group's own arrows already meets a box on this side (left or right).
        bool Taken(SceneNode n, double side) => inside.Any(e =>
            (e.From == n.Id && Math.Abs(e.StartTip.X - (side > 0 ? n.Box.Right : n.Box.X)) < 3) || (e.To == n.Id && Math.Abs(e.Tip.X - (side > 0 ? n.Box.Right : n.Box.X)) < 3));
        if (which.Count == 0) yield break;
        static MPoint M(Pt p) => new(p.X, -p.Y);
        static Pt P(MPoint p) => new(p.X, -p.Y);
        var g = new GeometryGraph();
        g.RootCluster.UserData = "root";
        var clusters = new Dictionary<string, Cluster>();
        var groupOf = new Dictionary<string, string>();
        foreach (var fg in f.Groups) foreach (string mem in fg.Members) groupOf[mem] = fg.Id;
        foreach (var sg in groups.OrderBy(x => x.Depth))
        {
            var cluster = new Cluster { UserData = sg.Id, BoundaryCurve = CurveFactory.CreateRectangleWithRoundedCorners(sg.Box.W, sg.Box.H, 12, 12, M(sg.Box.Center)) };
            var parent = f.Groups.FirstOrDefault(x => x.Id == sg.Id)?.Parent;
            (parent is not null && clusters.TryGetValue(parent, out var p) ? p : g.RootCluster).AddChild(cluster);
            clusters[sg.Id] = cluster;
        }
        // Each group's title (top left) is in the way too, so no arrow runs through it.
        foreach (var sg in groups)
        {
            var t = sg.TitleBox.Inflate(3);
            var title = new MsaglNode(CurveFactory.CreateRectangle(t.W, t.H, M(t.Center)), "title:" + sg.Id);
            g.Nodes.Add(title);
            clusters[sg.Id].AddChild(title);
        }
        var msagl = new Dictionary<string, MsaglNode>();
        foreach (var n in byId.Values)
        {
            var node = new MsaglNode(Curve(n.Shape, n.Box.W, n.Box.H), n.Id) { Center = M(n.Box.Center) };
            g.Nodes.Add(node);
            msagl[n.Id] = node;
            (groupOf.TryGetValue(n.Id, out var gid) && clusters.TryGetValue(gid, out var c) ? c : g.RootCluster).AddChild(node);
        }
        // Each arrow leaves its box on the side facing the group it goes to, and comes in on the side facing the
        // group it comes from, so it runs in the gap between the groups, never along a group's own arrows.
        var parentOf = f.Groups.ToDictionary(x => x.Id, x => x.Parent);
        Box Block(string id)
        {
            if (!groupOf.TryGetValue(id, out var gid)) return byId[id].Box;
            while (parentOf.TryGetValue(gid, out var up) && up is not null) gid = up;
            return groups.FirstOrDefault(x => x.Id == gid)?.Box ?? byId[id].Box;
        }
        static Pt Facing(Box from, Box to) =>
            to.X >= from.Right - 1 ? new Pt(1, 0) : to.Right <= from.X + 1 ? new Pt(-1, 0) : to.Y >= from.Bottom - 1 ? new Pt(0, 1) : new Pt(0, -1);
        var routes = new List<(int Index, MsaglEdge Edge)>();
        foreach (int i in which)
        {
            var e = f.Edges[i];
            var me = new MsaglEdge(msagl[e.From], msagl[e.To]);
            var (fromBlock, toBlock) = (Block(e.From), Block(e.To));
            me.SourcePort = new FloatingPort(msagl[e.From].BoundaryCurve, M(Port(byId[e.From], Facing(fromBlock, toBlock), byId[e.To].Box.Center)));
            var inSide = Facing(toBlock, fromBlock);
            var target = byId[e.To];
            var aimFrom = byId[e.From].Box.Center;
            // Coming down into a box at the top of its group, the arrow keeps clear of the group's title (top left):
            // it comes in right of the title, or, when the title spans the box, from the box's side.
            if (inSide.Y < 0 && groups.FirstOrDefault(x => x.Box.Contains(target.Box) && target.Box.Y - x.Box.Y < GroupTop + 12) is { } sg)
            {
                double clear = Math.Max(sg.TitleBox.Right + 12, target.Box.Center.X);
                if (clear <= target.Box.Center.X + target.Box.W / 3) aimFrom = new Pt(clear, 0);
                else
                {
                    double toward = aimFrom.X >= target.Box.Center.X ? 1 : -1;
                    inSide = new Pt(Taken(target, toward) && !Taken(target, -toward) ? -toward : toward, 0);
                }
            }
            me.TargetPort = new FloatingPort(msagl[e.To].BoundaryCurve, M(Port(target, inSide, aimFrom)));
            if (e.EndEnd != EdgeEnd.None) me.EdgeGeometry.TargetArrowhead = new Arrowhead { Length = MarkerLength(e.EndEnd) };
            if (e.StartEnd != EdgeEnd.None) me.EdgeGeometry.SourceArrowhead = new Arrowhead { Length = MarkerLength(e.StartEnd) };
            g.Edges.Add(me);
            routes.Add((i, me));
        }
        bool ok;
        try
        {
            new RectilinearEdgeRouter(g, 8, 8, false).Run();
            ok = routes.All(r => r.Edge.Curve is not null);
        }
        catch (Exception)
        {
            ok = false;
        }
        foreach (var (i, me) in routes)
        {
            var e = f.Edges[i];
            if (!ok || me.Curve is null)
            {
                var straight = Straight(e, byId[e.From], byId[e.To], labels[i]);
                yield return (i, straight with { LabelBox = Beside(straight, labels[i]) });
                continue;
            }
            var path = new List<PathStep> { new(PathVerb.Move, P(me.Curve.Start)) };
            AddCurve(path, me.Curve);
            var start = P(me.Curve.Start);
            var end = P(me.Curve.End);
            var sourceTip = me.EdgeGeometry.SourceArrowhead is { } sa ? P(sa.TipPosition) : start;
            var targetTip = me.EdgeGeometry.TargetArrowhead is { } ta ? P(ta.TipPosition) : end;
            var edge = new SceneEdge(e.From, e.To, path, e.Line, e.StartEnd, e.EndEnd, sourceTip, start, targetTip, end, labels[i].Lines, new Box());
            yield return (i, edge with { LabelBox = Beside(edge, labels[i]) });
        }
    }

    /// <summary>Where an arrow between groups meets a box: on the side facing the other end, moved along that side
    /// towards it (by up to a third of the side), so it doesn't leave from the middle a group's own arrows use.</summary>
    static Pt Port(SceneNode n, Pt side, Pt other)
    {
        var b = n.Box;
        var along = side.X != 0 ? new Pt(0, 1) : new Pt(1, 0);
        double reach = side.X != 0 ? b.H / 4 : b.W / 3;
        double toward = side.X != 0 ? other.Y - b.Center.Y : other.X - b.Center.X;
        double by = Math.Clamp(toward, -reach, reach);
        // A round or pointed box is left from its middle: off it, a side has no flat stretch to move along.
        var from = n.Shape is NodeShape.Circle or NodeShape.DoubleCircle or NodeShape.Decision or NodeShape.Hexagon ? b.Center : b.Center + along * by;
        return AlongFrom(n, from, side);
    }

    /// <summary>Where a ray from <paramref name="from"/> (inside the box) in direction <paramref name="dir"/> leaves
    /// its outline.</summary>
    static Pt AlongFrom(SceneNode n, Pt from, Pt dir)
    {
        double lo = 0, hi = n.Box.W + n.Box.H;
        for (int i = 0; i < 40; i++)
        {
            double mid = (lo + hi) / 2;
            if (Outline(n, from + dir * mid) > 0) hi = mid; else lo = mid;
        }
        return from + dir * hi;
    }

    /// <summary>Where an arrow's words go to start with: above its longest level stretch (an arrow routed between
    /// groups turns corners), else beside its line halfway along; moved later if anything's in the way.</summary>
    static Box Beside(SceneEdge e, Words label)
    {
        if (label.Lines.Count == 0) return new Box();
        var line = Flatten(e);
        var level = line.Zip(line.Skip(1)).Where(s => Math.Abs(s.First.Y - s.Second.Y) < 0.5 && Math.Abs(s.First.X - s.Second.X) >= label.W + 12)
            .OrderByDescending(s => Math.Abs(s.First.X - s.Second.X)).FirstOrDefault();
        if (level != default)
            return Box.Around(new Pt((level.First.X + level.Second.X) / 2, level.First.Y - label.H / 2 - 3), label.W, label.H);
        var cumulative = new double[line.Count];
        for (int k = 1; k < line.Count; k++) cumulative[k] = cumulative[k - 1] + Pt.Distance(line[k - 1], line[k]);
        double half = cumulative[^1] / 2;
        int j = Math.Max(1, Array.FindIndex(cumulative, c => c >= half));
        var along = (line[j] - line[j - 1]).Unit();
        var at = line[j - 1] + along * (half - cumulative[j - 1]);
        var normal = new Pt(-along.Y, along.X);
        double reach = Math.Abs(normal.X) * label.W / 2 + Math.Abs(normal.Y) * label.H / 2 + 4;
        return Box.Around(at + normal * reach, label.W, label.H);
    }

    /// <summary>Roughly how big a chart in groups lays out: each group's own estimate, the blocks stacked level by
    /// level along the flow as the layered estimate stacks boxes.</summary>
    static (double Width, double Height) GroupedEstimate(Flowchart f, Measurer m, ChartDirection dir)
    {
        var unit = Units(f);
        var size = new Dictionary<string, (double W, double H)>();
        foreach (var n in f.Nodes.Where(n => unit[n.Id] == n.Id))
        {
            var s = Size(n, m);
            size[n.Id] = (s.W, s.H);
        }
        foreach (var g in f.Groups.Where(g => g.Parent is null))
        {
            var (part, _) = Inside(f, g.Id, unit, Across(dir));
            if (part.Nodes.Count == 0) continue;
            var (w, h) = Estimate(part, Across(dir));
            if (Across(dir) == ChartDirection.LeftRight && w > InnerMax)
            {
                var (w2, h2) = Estimate(part, ChartDirection.TopDown);
                if (w2 < w) (w, h) = (w2, h2);
            }
            size[g.Id] = (Math.Max(w + 2 * GroupPad, m.Width(g.Title, TitleSize, true) + 2 * GroupPad + 4), h + GroupTop + GroupPad);
        }
        var blocks = size.Keys.Select(id => new FlowNode(id, [], NodeShape.Box, Tone.None)).ToList();
        var links = f.Edges.Select(e => (A: unit[e.From], B: unit[e.To])).Where(x => x.A != x.B && size.ContainsKey(x.A) && size.ContainsKey(x.B)).Distinct()
            .Select(x => new FlowEdge(x.A, x.B, null, EdgeLine.Solid, EdgeEnd.None, EdgeEnd.Arrow)).ToList();
        var chart = new Flowchart(dir, null, blocks, links, []);
        var level = Levels(chart, BackEdges(chart));
        bool down = dir is ChartDirection.TopDown or ChartDirection.BottomUp;
        double along = 0, across = 0;
        var levels = blocks.GroupBy(b => level[b.Id]).ToList();
        foreach (var at in levels)
        {
            along += at.Max(b => down ? size[b.Id].H : size[b.Id].W);
            across = Math.Max(across, at.Sum(b => down ? size[b.Id].W : size[b.Id].H) + (at.Count() - 1) * 28);
        }
        along += (levels.Count - 1) * 56;
        return (Math.Ceiling((down ? across : along) + 2 * Margin), Math.Ceiling((down ? along : across) + 2 * Margin));
    }
}
