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
/// Folding a flowchart's groups (its subgraphs) away, so a big diagram can open as a calm overview: each folded group
/// becomes one box with its title and a count, standing where its first box stood; an arrow into or out of a group
/// joins its box instead (two arrows that end up joining the same two boxes become one, their words together); an
/// arrow wholly inside a folded group goes with it; and a group inside a folded group folds with it. The chart's own
/// boxes, arrows and groups are otherwise exactly as written, so nothing a student reads is changed, only hidden.
/// </summary>
public static class DiagramFold
{
    /// <summary>A chart opens folded when it's big and made of parts: at least this many boxes, in at least two groups
    /// side by side.</summary>
    public const int OverviewFrom = 12;

    /// <summary>"1 box inside", "4 boxes inside": the count a folded group shows under its title.</summary>
    public static string Count(int boxes) => boxes == 1 ? "1 box inside" : $"{boxes} boxes inside";

    /// <summary>Whether <paramref name="chart"/> is better opened as an overview, every group folded: a big chart in
    /// two or more groups.</summary>
    public static bool StartsFolded(Flowchart chart) =>
        chart.Nodes.Count >= OverviewFrom && chart.Groups.Count(g => g.Parent is null) >= 2;

    /// <summary>Every group's id: what "Collapse all" folds.</summary>
    public static IReadOnlyList<string> All(Flowchart chart) => [.. chart.Groups.Select(g => g.Id)];

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
        var asked = new HashSet<string>(folded.Where(groups.ContainsKey));
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
        var inside = new Dictionary<string, int>();
        foreach (string box in shown.Values) if (groups.ContainsKey(box)) inside[box] = inside.GetValueOrDefault(box) + 1;

        // Boxes in the order written, each folded group where its first box was.
        var nodes = new List<FlowNode>();
        var placed = new HashSet<string>();
        foreach (var n in chart.Nodes)
        {
            string box = shown[n.Id];
            if (!placed.Add(box)) continue;
            string title = groups.TryGetValue(box, out var g) ? g.Title : "";
            nodes.Add(box == n.Id ? n : new FlowNode(box, title.Length > 0 ? [title, Count(inside[box])] : [Count(inside[box])], NodeShape.Subroutine, Tone.None));
        }

        // Arrows re-attached to the boxes that show their ends; one wholly inside a folded group goes; two that now join
        // the same boxes the same way (because of a fold) become one, keeping both their words.
        var edges = new List<FlowEdge>();
        var merged = new Dictionary<(string, string), int>();
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
                edges[at] = edges[at] with { Label = Words(edges[at].Label, e.Label) };
                continue;
            }
            merged[key] = edges.Count;
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
        return new FoldedChart(new Flowchart(chart.Direction, chart.Title, nodes, edges, kept), shown, inside);
    }

    /// <summary>Two arrows' words as one arrow's: each said once, joined with a slash; none if neither had any.</summary>
    static string? Words(string? a, string? b)
    {
        var said = new List<string>();
        foreach (string? w in new[] { a, b })
            foreach (string part in (w ?? "").Split(" / "))
                if (part.Trim() is { Length: > 0 } t && !said.Contains(t, StringComparer.OrdinalIgnoreCase)) said.Add(t);
        return said.Count == 0 ? null : string.Join(" / ", said);
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
