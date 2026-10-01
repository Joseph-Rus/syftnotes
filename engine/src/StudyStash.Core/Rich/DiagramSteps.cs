namespace StudyStash.Core.Rich;

/// <summary>
/// One step of walking through a diagram: the box (<see cref="Node"/>), the arrows that bring the walk to it from
/// steps already taken (<see cref="Arrives"/>, by their place in the chart's arrows) and their words, and the arrows
/// that leave it back to an earlier step (<see cref="Returns"/>: a cycle closing, a "still in pain" loop).
/// </summary>
public sealed record DiagramStep(string Node, IReadOnlyList<int> Arrives, string? Via, IReadOnlyList<int> Returns);

/// <summary>
/// The order a student reads a diagram in, to step through it: from where it starts (a box no arrow points to; for a
/// cycle, its first box as written), along the arrows, each box once and only after every box with an arrow to it
/// (except arrows that go back round a loop); a branch is read to its end before the next one starts, a group is
/// finished before the walk leaves it, and boxes nothing joins come last, in the order written.
/// </summary>
public static class DiagramSteps
{
    public static IReadOnlyList<DiagramStep> Order(Flowchart chart)
    {
        var index = new Dictionary<string, int>();
        for (int i = 0; i < chart.Nodes.Count; i++) index[chart.Nodes[i].Id] = i;
        var outs = chart.Nodes.ToDictionary(n => n.Id, _ => new List<int>());
        var ins = chart.Nodes.ToDictionary(n => n.Id, _ => new List<int>());
        for (int i = 0; i < chart.Edges.Count; i++)
        {
            var e = chart.Edges[i];
            if (e.From == e.To || !index.ContainsKey(e.From) || !index.ContainsKey(e.To)) continue;
            outs[e.From].Add(i);
            ins[e.To].Add(i);
        }

        // Depth first from each start, in the order written, then from anything not yet reached: the order boxes are
        // found in, and which arrows go back to a box still being followed (a loop's way back).
        var found = new Dictionary<string, int>();
        var back = new HashSet<int>();
        var onPath = new HashSet<string>();
        void Visit(string id)
        {
            found[id] = found.Count;
            onPath.Add(id);
            foreach (int i in outs[id])
            {
                string to = chart.Edges[i].To;
                if (onPath.Contains(to)) back.Add(i);
                else if (!found.ContainsKey(to)) Visit(to);
            }
            onPath.Remove(id);
        }
        var starts = chart.Nodes.Where(n => ins[n.Id].Count == 0 && outs[n.Id].Count > 0).Select(n => n.Id).ToList();
        foreach (string s in starts) if (!found.ContainsKey(s)) Visit(s);
        // What's left: cycles nothing leads into (from their first box written), then boxes nothing joins.
        foreach (var n in chart.Nodes) if (!found.ContainsKey(n.Id) && outs[n.Id].Count + ins[n.Id].Count > 0) Visit(n.Id);
        foreach (var n in chart.Nodes) if (!found.ContainsKey(n.Id)) Visit(n.Id);

        // Each box once every box with a forward arrow to it has been read: of those ready, one in the same group as the
        // last step first, then the one found first.
        var waiting = chart.Nodes.ToDictionary(n => n.Id, n => ins[n.Id].Count(i => !back.Contains(i)));
        var group = chart.Nodes.ToDictionary(n => n.Id, n => chart.Groups.FirstOrDefault(g => g.Members.Contains(n.Id))?.Id);
        var ready = new List<string>(chart.Nodes.Where(n => waiting[n.Id] == 0).Select(n => n.Id));
        var order = new List<string>();
        string? last = null;
        while (ready.Count > 0)
        {
            string next = ready.OrderBy(id => last is not null && group[last] is { } g && group[id] == g ? 0 : 1).ThenBy(id => found[id]).First();
            ready.Remove(next);
            order.Add(next);
            last = next;
            foreach (int i in outs[next])
            {
                if (back.Contains(i)) continue;
                string to = chart.Edges[i].To;
                if (--waiting[to] == 0) ready.Add(to);
            }
        }

        var steps = new List<DiagramStep>();
        foreach (string id in order)
        {
            var arrives = ins[id].Where(i => !back.Contains(i)).ToList();
            var words = arrives.Select(i => chart.Edges[i].Label?.Replace('\n', ' ')).Where(w => w is { Length: > 0 }).Distinct().ToList();
            var returns = outs[id].Where(back.Contains).ToList();
            steps.Add(new DiagramStep(id, arrives, words.Count == 0 ? null : string.Join(" / ", words), returns));
        }
        return steps;
    }
}
