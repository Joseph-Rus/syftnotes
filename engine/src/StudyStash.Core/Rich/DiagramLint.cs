using System.Globalization;

namespace StudyStash.Core.Rich;

/// <summary>
/// What's wrong with how a diagram will look in a student's notes, found by laying it out at the notes' width the way
/// the app does and looking at the result: too wide to read once shrunk to fit, taller than a screen and a half,
/// arrows crossing, words on boxes, boxes with a sentence in them, a big chart with no groups (or groups that don't
/// make an overview), and each kind's own limits. Each problem is one sentence that says what to change, for the
/// designer's one revision. Nothing here judges whether the diagram is true to the lecture; that's checked apart.
/// </summary>
public static class DiagramLint
{
    /// <summary>The notes' column a diagram is checked at (620 px on a Mac, 640 on Windows: the narrower).</summary>
    public const double Column = 620;

    /// <summary>The smallest scale a diagram's words still read at (13 px box words at about 9.4 px).</summary>
    public const double ReadableScale = 0.72;

    /// <summary>How tall a diagram may stand in the notes, at the scale it's shown, before it's too long to take in.</summary>
    public const double TallestShown = 1500;

    /// <summary>Text widths without a font: an average letter of the app's sans-serif, a little wide (diagram words
    /// in Helvetica Neue and Segoe UI average about half their size).</summary>
    public static double Measure(string text, double size, bool bold) =>
        new StringInfo(text).LengthInTextElements * size * (bold ? 0.56 : 0.52);

    /// <summary>The problems with how <paramref name="chart"/> will look, worst first; none when it's fine.</summary>
    public static IReadOnlyList<string> Problems(Flowchart chart)
    {
        var found = new List<string>();
        DiagramScene scene;
        try
        {
            scene = DiagramLayout.Fit(chart, Measure, Column);
        }
        catch (Exception)
        {
            found.Add("it couldn't be laid out at all: simplify it");
            return found;
        }
        double scale = Math.Min(1, Column / scene.Width);
        if (scale < ReadableScale)
            found.Add($"it is {N(scene.Width)} px wide even laid out the narrower way, so in the {N(Column)} px notes column its words would shrink to {N(13 * scale)} px: {Widest(scene)}{Narrower(chart)}");
        double tall = scene.Height * scale;
        if (tall > TallestShown)
            found.Add($"it would stand {N(tall)} px tall in the notes, more than a screen and a half: use fewer boxes, fold minor steps into a box's <small> line, or lay its groups side by side (flowchart LR with subgraphs)");
        // A sequence diagram's messages cross its lifelines by design; only a flowchart's or state diagram's crossings say anything.
        int crossings = chart.Form is ChartForm.Flowchart or ChartForm.State ? DiagramLayout.Crossings(scene) : 0;
        if (crossings > Math.Max(2, chart.Nodes.Count / 6))
            found.Add($"{crossings} places where arrows cross each other: order the boxes (and groups) the way the arrows run, and drop arrows that only repeat what the order already shows");
        var crowded = scene.Edges.Where(e => e.LabelLines.Count > 0 && scene.Nodes.Any(n => n.Box.Inflate(1).Intersects(e.LabelBox))).Select(e => string.Join(" ", e.LabelLines)).Distinct().ToList();
        if (crowded.Count > 0)
            found.Add($"the arrow words {Quoted(crowded)} have no room beside their arrows and sit on boxes: shorten them to one to three words");

        var wordy = chart.Nodes.Where(n => Words(n.Label) > 6 || n.Label.Length > 48).Select(n => n.Label).ToList();
        if (wordy.Count > 0)
            found.Add($"{Quoted(wordy)} {(wordy.Count == 1 ? "is a sentence, not a box" : "are sentences, not boxes")}: a box is one to five words; put what it does or where in a smaller line under it (\"Name<br><small>what, where, how much</small>\")" + (chart.Form == ChartForm.Flowchart ? "" : ", or in the caption"));
        var cut = chart.Nodes.Where(n => n.Lines.Concat(n.Detail).Any(l => l.EndsWith('…'))).Select(n => n.Label).ToList();
        if (cut.Count > 0) found.Add($"{Quoted(cut)} {(cut.Count == 1 ? "was" : "were")} cut short for being too long: say it in fewer words");
        var talky = chart.Edges.Where(e => e.Label is { } l && Words(l) > 5).Select(e => e.Label!.Replace('\n', ' ')).Distinct().ToList();
        if (talky.Count > 0) found.Add($"the arrow words {Quoted(talky)} are too long: one to four words on an arrow");

        switch (chart.Form)
        {
            case ChartForm.Flowchart or ChartForm.State:
                Structure(chart, found);
                break;
            case ChartForm.Sequence:
                int people = chart.Nodes.Count(n => n.Role != NodeRole.Note);
                if (people > 6) found.Add($"it has {people} participants; at most 6 fit side by side in the notes: leave out the ones the lecturer only mentioned");
                if (chart.Edges.Count > 24) found.Add($"it has {chart.Edges.Count} messages; keep to the 24 that tell the exchange");
                break;
            case ChartForm.Timeline:
                int periods = chart.Nodes.Count(n => n.Role == NodeRole.Period);
                if (periods > 12) found.Add($"it has {periods} periods; keep to the 12 the lecture dwelt on, and group them in sections");
                if (chart.Nodes.Where(n => n.Role == NodeRole.Period).Any(p => chart.Edges.Count(e => e.From == p.Id) > 4))
                    found.Add("a period has more than 4 events: keep the ones the lecture stressed");
                break;
            case ChartForm.Mindmap:
                if (Depth(chart) > 3) found.Add("it branches more than three levels below its root: keep to three, with the finest points in the notes");
                break;
        }
        return found;
    }

    /// <summary>A flowchart's or state diagram's groups: a big one needs them, and they must read as an overview.</summary>
    static void Structure(Flowchart chart, List<string> found)
    {
        int boxes = chart.Nodes.Count(n => n.Role is NodeRole.Plain);
        if (chart.Groups.Count == 0)
        {
            if (boxes > NoGroupsFrom)
                found.Add($"it has {boxes} boxes and no groups: put them in 3 to 7 subgraphs of 3 to 7 boxes each (the phases, places or parts the lecturer named), so the chart folds into an overview of its groups");
            return;
        }
        var tops = chart.Groups.Where(g => g.Parent is null).ToList();
        var parent = chart.Groups.ToDictionary(g => g.Id, g => g.Parent);
        string Top(string id)
        {
            while (parent.TryGetValue(id, out var p) && p is not null) id = p;
            return id;
        }
        var unit = chart.Nodes.ToDictionary(n => n.Id, n => n.Id);
        foreach (var g in chart.Groups) foreach (string m in g.Members) if (unit.ContainsKey(m)) unit[m] = Top(g.Id);
        foreach (var g in tops)
        {
            int inside = chart.Nodes.Count(n => unit[n.Id] == g.Id && n.Role is NodeRole.Plain);
            if (inside > 8) found.Add($"the group “{g.Title}” holds {inside} boxes: split it, or fold minor steps into <small> lines (a group reads best with 3 to 7)");
            else if (inside == 1 && tops.Count > 1) found.Add($"the group “{g.Title}” holds a single box: drop the group, or give it the boxes that belong with it");
        }
        int units = unit.Values.Distinct().Count();
        if (units > 9) found.Add($"folded into its groups it still has {units} parts (groups and boxes in none): at most 9, so put the loose boxes in groups");
        if (tops.Count >= 2)
        {
            var linked = chart.Edges.Where(e => unit.ContainsKey(e.From) && unit.ContainsKey(e.To) && unit[e.From] != unit[e.To])
                .SelectMany(e => new[] { unit[e.From], unit[e.To] }).ToHashSet();
            var alone = tops.Where(g => !linked.Contains(g.Id)).Select(g => g.Title).ToList();
            if (linked.Count > 0 && alone.Count > 0)
                found.Add($"{Quoted(alone)} {(alone.Count == 1 ? "isn't" : "aren't")} joined to the rest by any arrow, so the overview falls apart: show how {(alone.Count == 1 ? "it connects" : "they connect")}, or leave {(alone.Count == 1 ? "it" : "them")} out");
        }
    }

    /// <summary>A flowchart with more boxes than this and no groups is too much to take in as one.</summary>
    public const int NoGroupsFrom = 16;

    /// <summary>The boxes that make a laid-out diagram as wide as it is: its widest row (or column, for one laid out
    /// left to right), named, when there are several side by side.</summary>
    static string Widest(DiagramScene scene)
    {
        bool rows = scene.Direction is ChartDirection.TopDown or ChartDirection.BottomUp || scene.Kind is SceneKind.Mindmap or SceneKind.Sequence;
        if (!rows || scene.Nodes.Count < 2) return "";
        var row = scene.Nodes.Select(n => scene.Nodes.Where(o => o.Box.Y < n.Box.Bottom && n.Box.Y < o.Box.Bottom).ToList())
            .MaxBy(r => r.Sum(o => o.Box.W))!;
        if (row.Count < 3) return "";
        var named = row.OrderBy(n => n.Box.X).Select(n => string.Join(" ", n.Lines.Take(n.Lines.Count - n.DetailLines)));
        return $"{row.Count} boxes stand side by side ({Quoted(named)}): shorten their words and smaller lines, or have fewer of them in one row; ";
    }

    /// <summary>What makes a too-wide diagram of this kind narrower.</summary>
    static string Narrower(Flowchart chart) => chart.Form switch
    {
        ChartForm.Sequence => "use fewer participants (at most 5) and shorter message words",
        ChartForm.Timeline => "use fewer periods, or shorter words for each event",
        ChartForm.Mindmap => "use fewer words per branch, or fewer branches",
        _ when chart.Groups.Count > 0 => "make each group narrower (its boxes one under another) or use fewer groups side by side, and shorten long labels",
        _ when chart.Direction is ChartDirection.LeftRight or ChartDirection.RightLeft => "lay it out top-down (flowchart TD), put long rows of boxes into groups, and shorten long labels",
        _ => "shorten long labels, and move detail that doesn't need to be on the picture into the caption",
    };

    static int Depth(Flowchart chart)
    {
        var root = chart.Nodes.FirstOrDefault(n => n.Role == NodeRole.Root) ?? chart.Nodes.FirstOrDefault();
        if (root is null) return 0;
        var children = chart.Edges.ToLookup(e => e.From, e => e.To);
        int deepest = 0;
        var seen = new HashSet<string>();
        void Walk(string id, int depth)
        {
            if (!seen.Add(id)) return;
            deepest = Math.Max(deepest, depth);
            foreach (string c in children[id]) Walk(c, depth + 1);
        }
        Walk(root.Id, 0);
        return deepest;
    }

    static int Words(string text) => text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;

    static string Quoted(IEnumerable<string> labels)
    {
        var all = labels.Select(l => $"“{(l.Length > 40 ? l[..39] + "…" : l)}”").ToList();
        return all.Count <= 3 ? string.Join(", ", all) : string.Join(", ", all.Take(3)) + $" and {all.Count - 3} more";
    }

    static string N(double v) => Math.Round(v).ToString(CultureInfo.InvariantCulture);
}
