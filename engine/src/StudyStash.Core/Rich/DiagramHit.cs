namespace StudyStash.Core.Rich;

/// <summary>What part of a diagram something is: a box, an arrow's line, an arrow's words, a group's title, or the
/// inside of a group.</summary>
public enum DiagramPart { None, Node, Edge, EdgeLabel, GroupTitle, Group }

/// <summary>
/// The thing under a point of a laid-out diagram: a box (by its id), an arrow (by its place in the scene's arrows,
/// which is its place in the chart's), its words, or a group (by its id). <see cref="Nothing"/> is empty paper.
/// </summary>
public readonly record struct DiagramTarget(DiagramPart Part, string? Id = null, int Edge = -1)
{
    public static readonly DiagramTarget Nothing = new(DiagramPart.None);

    public static DiagramTarget Node(string id) => new(DiagramPart.Node, id);

    public bool IsNothing => Part == DiagramPart.None;
    public bool IsNode => Part == DiagramPart.Node;
    public bool IsEdge => Part is DiagramPart.Edge or DiagramPart.EdgeLabel;
    public bool IsGroup => Part is DiagramPart.GroupTitle or DiagramPart.Group;
}

/// <summary>What lights up with a box or an arrow: its boxes and arrows (the rest of the diagram dims).</summary>
public sealed record DiagramFocus(IReadOnlySet<string> Nodes, IReadOnlySet<int> Edges)
{
    public static readonly DiagramFocus None = new(new HashSet<string>(), new HashSet<int>());

    public bool IsEmpty => Nodes.Count == 0 && Edges.Count == 0;
}

/// <summary>
/// Finding things on a laid-out diagram, in its own units (a pointer's position divided by the scale it's drawn at):
/// what's under a point, what lights up with it, and which box an arrow key goes to next.
/// </summary>
public static class DiagramHit
{
    /// <summary>How near a line (in the diagram's units) a point counts as on it: a line is thin, so it's given some
    /// room either side.</summary>
    public const double LineSlop = 5;

    /// <summary>
    /// What's at <paramref name="p"/>: a box first (it's drawn over the lines), then an arrow's words, then the
    /// nearest line within <paramref name="slop"/>, then a group's title, then the innermost group it's inside. Only
    /// the first <paramref name="arrows"/> of the scene's lines are arrows (the chart's own); any after them are lines a
    /// layout adds (a sequence diagram's lifelines, a timeline's axis, a block's divider), which are nothing to point at.
    /// </summary>
    public static DiagramTarget At(DiagramScene scene, Pt p, double slop = LineSlop, int arrows = int.MaxValue)
    {
        int real = Math.Min(arrows, scene.Edges.Count);
        for (int i = scene.Nodes.Count - 1; i >= 0; i--)
        {
            var n = scene.Nodes[i];
            if (n.Box.Inflate(1).Contains(p) && DiagramLayout.Outline(n, p) <= 1) return DiagramTarget.Node(n.Id);
        }
        for (int i = 0; i < real; i++)
        {
            var e = scene.Edges[i];
            if (e.LabelLines.Count > 0 && e.LabelBox.Inflate(2).Contains(p)) return new DiagramTarget(DiagramPart.EdgeLabel, null, i);
        }
        int nearest = -1;
        double best = slop;
        for (int i = 0; i < real; i++)
        {
            double d = Distance(Line(scene.Edges[i]), p);
            if (d <= best)
            {
                best = d;
                nearest = i;
            }
        }
        if (nearest >= 0) return new DiagramTarget(DiagramPart.Edge, null, nearest);
        foreach (var g in scene.Groups.OrderByDescending(g => g.Depth))
            if (g.Title.Length > 0 && g.TitleBox.Inflate(3).Contains(p)) return new DiagramTarget(DiagramPart.GroupTitle, g.Id);
        foreach (var g in scene.Groups.OrderByDescending(g => g.Depth))
            if (g.Box.Contains(p)) return new DiagramTarget(DiagramPart.Group, g.Id);
        return DiagramTarget.Nothing;
    }

    /// <summary>An arrow's line as points, curves flattened, from the tip at its start to the tip at its end (worked out
    /// once per arrow: the pointer asks on every move).</summary>
    public static IReadOnlyList<Pt> Line(SceneEdge e) => Flat.GetValue(e, x => DiagramLayout.Flatten(x));

    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<SceneEdge, List<Pt>> Flat = [];

    /// <summary>How far <paramref name="p"/> is from the nearest point of a run of points.</summary>
    public static double Distance(IReadOnlyList<Pt> line, Pt p)
    {
        if (line.Count == 0) return double.MaxValue;
        if (line.Count == 1) return Pt.Distance(line[0], p);
        double best = double.MaxValue;
        for (int i = 0; i + 1 < line.Count; i++)
        {
            Pt a = line[i], b = line[i + 1];
            var ab = b - a;
            double len = ab.X * ab.X + ab.Y * ab.Y;
            double t = len < 1e-12 ? 0 : Math.Clamp(((p.X - a.X) * ab.X + (p.Y - a.Y) * ab.Y) / len, 0, 1);
            best = Math.Min(best, Pt.Distance(p, a + ab * t));
        }
        return best;
    }

    /// <summary>
    /// What lights up with <paramref name="target"/>: a box with every arrow in or out of it and the boxes at their
    /// other ends; an arrow (or its words) with the two boxes it joins; a group with the boxes and arrows inside it
    /// (<paramref name="members"/> says which boxes those are). Nothing, for empty paper.
    /// </summary>
    public static DiagramFocus Around(DiagramScene scene, DiagramTarget target, Func<string, IEnumerable<string>>? members = null, int arrows = int.MaxValue)
    {
        var nodes = new HashSet<string>();
        var edges = new HashSet<int>();
        int real = Math.Min(arrows, scene.Edges.Count);
        switch (target.Part)
        {
            case DiagramPart.Node when target.Id is { } id:
                nodes.Add(id);
                for (int i = 0; i < real; i++)
                {
                    var e = scene.Edges[i];
                    if (e.From != id && e.To != id) continue;
                    edges.Add(i);
                    nodes.Add(e.From);
                    nodes.Add(e.To);
                }
                break;
            case DiagramPart.Edge or DiagramPart.EdgeLabel when target.Edge >= 0 && target.Edge < real:
                var edge = scene.Edges[target.Edge];
                edges.Add(target.Edge);
                nodes.Add(edge.From);
                nodes.Add(edge.To);
                break;
            case DiagramPart.Group or DiagramPart.GroupTitle when target.Id is { } group && members is not null:
                foreach (string m in members(group)) nodes.Add(m);
                for (int i = 0; i < real; i++)
                    if (nodes.Contains(scene.Edges[i].From) && nodes.Contains(scene.Edges[i].To)) edges.Add(i);
                break;
        }
        return nodes.Count == 0 && edges.Count == 0 ? DiagramFocus.None : new DiagramFocus(nodes, edges);
    }

    /// <summary>
    /// The box an arrow key goes to from <paramref name="from"/>, heading (<paramref name="dx"/>, <paramref name="dy"/>)
    /// on the page: the box joined to it by an arrow that lies most that way (nearer and straighter ahead wins), so the
    /// keys walk along the arrows whichever way a chart is laid out; failing that, the nearest box that way at all.
    /// Null when nothing lies that way.
    /// </summary>
    public static string? Toward(DiagramScene scene, string from, double dx, double dy, int arrows = int.MaxValue)
    {
        var start = scene.Nodes.FirstOrDefault(n => n.Id == from);
        if (start is null) return null;
        var joined = new HashSet<string>();
        foreach (var e in scene.Edges.Take(arrows))
        {
            if (e.From == from && e.To != from) joined.Add(e.To);
            if (e.To == from && e.From != from) joined.Add(e.From);
        }
        var heading = new Pt(dx, dy).Unit();
        string? Best(IEnumerable<SceneNode> candidates)
        {
            string? best = null;
            double cost = double.MaxValue;
            foreach (var n in candidates)
            {
                var d = n.Box.Center - start.Box.Center;
                double length = d.Length;
                if (length < 1e-6) continue;
                double cos = (d.X * heading.X + d.Y * heading.Y) / length;
                // Ahead at all: within about 70 degrees of the way the key points.
                if (cos < 0.34) continue;
                double c = length * (1 + 2 * (1 - cos));
                if (c < cost)
                {
                    cost = c;
                    best = n.Id;
                }
            }
            return best;
        }
        return Best(scene.Nodes.Where(n => joined.Contains(n.Id))) ?? Best(scene.Nodes.Where(n => n.Id != from));
    }
}
