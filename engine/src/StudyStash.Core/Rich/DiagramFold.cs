namespace StudyStash.Core.Rich;

/// <summary>
/// A flowchart with some of its groups folded: the chart to draw (each folded group one box, with its title and how
/// many boxes it holds), which box shows each of the original boxes (itself, or the folded group it's in), and how
/// many boxes each folded group holds.
/// </summary>
public sealed record FoldedChart(Flowchart Chart, IReadOnlyDictionary<string, string> Shown, IReadOnlyDictionary<string, int> Inside)
{
    /// <summary>Whether the box <paramref name="id"/> (in <see cref="Chart"/>) is a folded group.</summary>
    public bool IsFolded(string id) => Inside.ContainsKey(id);
}

/// <summary>
/// Folding a chart's groups (its subgraphs) away, so a big diagram can open as a calm overview: each folded group
/// becomes one box with its title (and how many boxes it holds in smaller words under it), standing where its first box
/// stood; an arrow into or out of a group joins its box instead, and the arrows that end up joining the same two boxes
/// the same way become one, keeping their words when they all said the same; an arrow wholly inside a folded group goes
/// with it; and a group inside a folded group folds with it. The chart's own boxes, arrows (a state's loop back to
/// itself too) and groups are otherwise exactly as written, so nothing a student reads is changed, only hidden. Only
/// flowcharts and state diagrams fold: a sequence diagram's blocks, a timeline's sections and a mind map aren't groups
/// to fold. <see cref="Flowchart.Folded"/> (the overview the diagram designer is held to) is this with every outermost
/// group folded.
/// </summary>
public static class DiagramFold
{
    /// <summary>A chart opens folded when it's big and made of parts: at least this many boxes, in at least two groups
    /// side by side.</summary>
    public const int OverviewFrom = 12;

    /// <summary>"1 box", "4 boxes": the count a folded group shows under its title.</summary>
    public static string Count(int boxes) => boxes == 1 ? "1 box" : $"{boxes} boxes";

    /// <summary>Whether this kind of chart has groups that fold (a flowchart's or a state diagram's).</summary>
    public static bool Folds(Flowchart chart) => chart.Groups.Count > 0 && chart.Form is ChartForm.Flowchart or ChartForm.State;

    /// <summary>Whether <paramref name="chart"/> is better opened as an overview, every group folded: a big chart in
    /// two or more groups.</summary>
    public static bool StartsFolded(Flowchart chart) =>
        Folds(chart) && chart.Nodes.Count >= OverviewFrom && chart.Groups.Count(g => g.Parent is null) >= 2;

    /// <summary>Every group's id: what "Collapse all" folds (none, for a kind that doesn't fold).</summary>
    public static IReadOnlyList<string> All(Flowchart chart) => Folds(chart) ? [.. chart.Groups.Select(g => g.Id)] : [];

    /// <summary>The outermost groups: folding these is the overview.</summary>
    public static IReadOnlyList<string> Outermost(Flowchart chart) => Folds(chart) ? [.. chart.Groups.Where(g => g.Parent is null).Select(g => g.Id)] : [];

    /// <summary>The boxes inside group <paramref name="group"/>, its own and its inner groups', in the order written.</summary>
    public static IReadOnlyList<string> Members(Flowchart chart, string group)
    {
        var inside = Within(chart, group);
        return [.. chart.Nodes.Where(n => inside.Contains(Home(chart, n.Id) ?? "")).Select(n => n.Id)];
    }

    /// <summary><paramref name="chart"/> with the groups named in <paramref name="folded"/> folded (ids that aren't
    /// groups are ignored). Folding nothing gives the chart back as it is.</summary>
    public static FoldedChart Fold(Flowchart chart, IEnumerable<string> folded)
    {
        var groups = chart.Groups.ToDictionary(g => g.Id);
        var asked = new HashSet<string>(Folds(chart) ? folded.Where(groups.ContainsKey) : []);
        var shown = chart.Nodes.ToDictionary(n => n.Id, n => n.Id);
        if (asked.Count == 0) return new FoldedChart(chart, shown, new Dictionary<string, int>());

        // A group shows as one box when it's folded and no group around it is: the outermost folded group wins.
        string? Showing(string? group)
        {
            string? outer = null;
            for (string? g = group; g is not null; g = groups.TryGetValue(g, out var fg) ? fg.Parent : null)
                if (asked.Contains(g)) outer = g;
            return outer;
        }
        foreach (var n in chart.Nodes)
            if (Showing(Home(chart, n.Id)) is { } box) shown[n.Id] = box;
        // What a folded group holds: its boxes, not a state diagram's start and end dots or a note.
        var inside = new Dictionary<string, int>();
        foreach (var n in chart.Nodes)
            if (groups.ContainsKey(shown[n.Id]))
                inside[shown[n.Id]] = inside.GetValueOrDefault(shown[n.Id]) + (n.Role is NodeRole.Start or NodeRole.End or NodeRole.Note ? 0 : 1);

        // Boxes in the order written, each folded group where its first box was.
        var nodes = new List<FlowNode>();
        var placed = new HashSet<string>();
        foreach (var n in chart.Nodes)
        {
            string box = shown[n.Id];
            if (!placed.Add(box)) continue;
            string title = groups.TryGetValue(box, out var g) ? g.Title : "";
            nodes.Add(box == n.Id ? n : new FlowNode(box, [title.Length > 0 ? title : box], NodeShape.Rounded, Tone.None) { Detail = [Count(inside[box])] });
        }

        // Arrows re-attached to the boxes that show their ends; one wholly inside a folded group goes; those that now join
        // the same boxes the same way (because of a fold) become one, with their words when they all said the same.
        var edges = new List<FlowEdge>();
        var merged = new Dictionary<(string, string), int>();
        var said = new Dictionary<int, HashSet<string?>>();
        foreach (var e in chart.Edges)
        {
            string from = shown.GetValueOrDefault(e.From, e.From), to = shown.GetValueOrDefault(e.To, e.To);
            bool moved = from != e.From || to != e.To;
            if (!moved)
            {
                edges.Add(e);
                continue;
            }
            if (from == to) continue;
            var key = (from, to);
            if (merged.TryGetValue(key, out int at))
            {
                said[at].Add(e.Label);
                if (said[at].Count > 1) edges[at] = edges[at] with { Label = null };
                continue;
            }
            merged[key] = edges.Count;
            said[edges.Count] = [e.Label];
            edges.Add(e with { From = from, To = to });
        }

        // Groups that still show: those with no folded group around them or folded themselves; a folded group inside one
        // that shows is one of its boxes now.
        var kept = new List<FlowGroup>();
        foreach (var g in chart.Groups)
        {
            if (Showing(g.Id) is not null) continue;
            var members = g.Members.ToList();
            foreach (var child in chart.Groups.Where(c => c.Parent == g.Id && asked.Contains(c.Id))) members.Add(child.Id);
            kept.Add(g with { Members = members });
        }
        return new FoldedChart(new Flowchart(chart.Direction, chart.Title, nodes, edges, kept) { Form = chart.Form }, shown, inside);
    }

    /// <summary>The group a box is written in directly (null: none).</summary>
    static string? Home(Flowchart chart, string node) => chart.Groups.FirstOrDefault(g => g.Members.Contains(node))?.Id;

    /// <summary>A group and every group inside it.</summary>
    static HashSet<string> Within(Flowchart chart, string group)
    {
        var all = new HashSet<string> { group };
        bool grew = true;
        while (grew)
        {
            grew = false;
            foreach (var g in chart.Groups)
                if (g.Parent is { } p && all.Contains(p) && all.Add(g.Id)) grew = true;
        }
        return all;
    }
}
