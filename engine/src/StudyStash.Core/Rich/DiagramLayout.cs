using System.Globalization;
using Microsoft.Msagl.Core.Geometry;
using Microsoft.Msagl.Core.Geometry.Curves;
using Microsoft.Msagl.Core.Layout;
using Microsoft.Msagl.Core.Routing;
using Microsoft.Msagl.Layout.Layered;
using Microsoft.Msagl.Miscellaneous;
using MPoint = Microsoft.Msagl.Core.Geometry.Point;
using MsaglEdge = Microsoft.Msagl.Core.Layout.Edge;
using MsaglLabel = Microsoft.Msagl.Core.Layout.Label;
using MsaglNode = Microsoft.Msagl.Core.Layout.Node;

namespace StudyStash.Core.Rich;

/// <summary>
/// Lays a flowchart out for drawing. A single cycle goes on a ring (the cardiac cycle, the nursing process); a
/// hierarchy becomes a tidy tree that keeps the order it was written in (a classification, a binary search tree);
/// anything else — decisions, merges, groups — is a layered chart (MSAGL's Sugiyama layout). Words are measured by
/// the caller, so the boxes fit the font they'll be drawn in.
/// </summary>
public static partial class DiagramLayout
{
    /// <summary>Box words: 13 px, medium weight, lines 17 px apart. Arrow words: 12 px regular, 16 apart. Group
    /// titles: 12 px semibold.</summary>
    public const double TextSize = 13, LineHeight = 17, LabelSize = 12, LabelLineHeight = 16, TitleSize = 12;

    /// <summary>How long each end marker is along its line: arrowheads 9 × 9, circles and crosses 8.</summary>
    public const double ArrowLength = 9, ArrowWidth = 9, MarkLength = 8;

    const double PadX = 14, PadY = 9, MinWidth = 72, MaxLine = 150, CircleLine = 110, LabelLine = 140;
    const double Margin = 3, BoxRadius = 8, RoundedRadius = 14, LoopRoom = 34;

    /// <summary>
    /// The chart laid out at scale 1. <paramref name="measure"/> gives a text's width at a size (bold: the box
    /// words' medium weight, and group titles). <paramref name="direction"/> overrides the chart's own (the app
    /// lays a too-wide left-to-right chart out top-down).
    /// </summary>
    public static DiagramScene Lay(Flowchart chart, Func<string, double, bool, double> measure, ChartDirection? direction = null)
    {
        var m = new Measurer(measure);
        var dir = direction ?? chart.Direction;
        var sizes = chart.Nodes.ToDictionary(n => n.Id, n => Size(n, m));
        var labels = chart.Edges.Select(e => EdgeLabel(e.Label, m)).ToList();
        var scene = Kind(chart) switch
        {
            SceneKind.Ring => Ring(chart, sizes, labels),
            SceneKind.Tree => Tree(chart, sizes, labels, dir),
            SceneKind.Grouped => Grouped(chart, sizes, labels, dir, m),
            SceneKind.Sequence => Sequence(chart, sizes, labels, m),
            SceneKind.Timeline => Timeline(chart, sizes, dir, m),
            SceneKind.Mindmap => Mindmap(chart, sizes, labels, dir, m),
            _ => Layered(chart, sizes, labels, dir, m),
        };
        return Normalise(scene);
    }

    /// <summary>
    /// The scene for a column <paramref name="width"/> wide: laid out as written, except that a chart too wide for the
    /// column is turned (see <see cref="Turned"/>) when that makes it narrower.
    /// </summary>
    public static DiagramScene Fit(Flowchart chart, Func<string, double, bool, double> measure, double width)
    {
        var scene = Lay(chart, measure);
        if (scene.Width > width && Turned(chart, scene.Kind) is { } turned)
        {
            var other = Lay(chart, measure, turned);
            if (other.Width < scene.Width) return other;
        }
        return scene;
    }

    /// <summary>
    /// Roughly how big the chart will be once laid out, found in a moment without laying it out: the room a note keeps
    /// for a diagram while it's laid out in the background, so the page hardly moves when it arrives. Words are taken
    /// at an average width; a ring or a tree is placed as it will be (both are quick), and a layered chart is stacked
    /// level by level, spaced as its layout spaces them.
    /// </summary>
    public static (double Width, double Height) Estimate(Flowchart chart, ChartDirection? direction = null)
    {
        var m = new Measurer((text, size, bold) => text.Length * size * (bold ? 0.58 : 0.55));
        var dir = direction ?? chart.Direction;
        var sizes = chart.Nodes.ToDictionary(n => n.Id, n => Size(n, m));
        var labels = chart.Edges.Select(e => EdgeLabel(e.Label, m)).ToList();
        var kind = Kind(chart);
        if (kind == SceneKind.Grouped) return GroupedEstimate(chart, m, dir);
        if (kind != SceneKind.Layered)
        {
            // Rings, trees and the kinds with layouts of their own are quick to place for real.
            var quick = Normalise(kind switch
            {
                SceneKind.Ring => Ring(chart, sizes, labels),
                SceneKind.Tree => Tree(chart, sizes, labels, dir),
                SceneKind.Sequence => Sequence(chart, sizes, labels, m),
                SceneKind.Timeline => Timeline(chart, sizes, dir, m),
                _ => Mindmap(chart, sizes, labels, dir, m),
            });
            return (quick.Width, quick.Height);
        }
        var back = BackEdges(chart);
        var level = Levels(chart, back);
        bool down = dir is ChartDirection.TopDown or ChartDirection.BottomUp;
        double along = 0, across = 0;
        var levels = chart.Nodes.GroupBy(n => level[n.Id]).ToList();
        foreach (var boxes in levels)
        {
            along += boxes.Max(n => down ? sizes[n.Id].H : sizes[n.Id].W);
            across = Math.Max(across, boxes.Sum(n => down ? sizes[n.Id].W : sizes[n.Id].H) + (boxes.Count() - 1) * 28);
        }
        along += (levels.Count - 1) * 44;
        // An arrow's words sit between two levels and push them apart.
        var worded = chart.Edges.Select((e, i) => (e, i)).Where(x => labels[x.i].Lines.Count > 0 && x.e.From != x.e.To)
            .Select(x => Math.Min(level[x.e.From], level[x.e.To])).Distinct().Count();
        along += worded * (down ? 2 * LabelLineHeight : 60);
        // An arrow back up the chart runs around its side.
        if (back.Count > 0) across += 20;
        double w = (down ? across : along) + 2 * Margin, h = (down ? along : across) + 2 * Margin;
        // A group's title sits at its top whichever way the chart runs; its sides are narrower.
        int depth = chart.Groups.Count == 0 ? 0 : chart.Groups.Any(g => g.Parent is not null) ? 2 : 1;
        return (Math.Ceiling(w + depth * 24), Math.Ceiling(h + depth * 42));
    }

    /// <summary>
    /// The way to try a chart that's too wide for its column: a left-to-right chart top-down (a timeline down the
    /// page, a mind map with every branch on one side), and a top-down tree left-to-right (a classification reads as
    /// well across as down). Null when turning it wouldn't help (a ring, a sequence diagram).
    /// </summary>
    public static ChartDirection? Turned(Flowchart chart, SceneKind kind) => kind switch
    {
        SceneKind.Ring or SceneKind.Sequence => null,
        _ when chart.Direction is ChartDirection.LeftRight or ChartDirection.RightLeft => ChartDirection.TopDown,
        SceneKind.Tree => ChartDirection.LeftRight,
        _ => null,
    };

    /// <summary>Which layout a chart gets: a sequence diagram, a timeline and a mind map their own; a big chart in
    /// groups, blocks (<see cref="Grouped"/>); a ring for one cycle of 3 to 10 boxes, a tree for a hierarchy of up to
    /// 40 with no decisions in it, layered for everything else (and a small chart with groups).</summary>
    public static SceneKind Kind(Flowchart f)
    {
        switch (f.Form)
        {
            case ChartForm.Sequence: return SceneKind.Sequence;
            case ChartForm.Timeline: return SceneKind.Timeline;
            case ChartForm.Mindmap: return SceneKind.Mindmap;
        }
        int n = f.Nodes.Count;
        if (IsGrouped(f)) return SceneKind.Grouped;
        if (f.Groups.Count > 0 || f.Form == ChartForm.State) return SceneKind.Layered;
        var outs = f.Nodes.ToDictionary(x => x.Id, _ => 0);
        var ins = f.Nodes.ToDictionary(x => x.Id, _ => 0);
        foreach (var e in f.Edges)
        {
            outs[e.From]++;
            ins[e.To]++;
        }
        if (n is >= 3 and <= 10 && f.Edges.Count == n && f.Nodes.All(x => outs[x.Id] == 1 && ins[x.Id] == 1))
        {
            var next = f.Edges.ToDictionary(e => e.From, e => e.To);
            var seen = new HashSet<string>();
            string at = f.Nodes[0].Id;
            for (int i = 0; i < n; i++) { seen.Add(at); at = next[at]; }
            if (seen.Count == n && at == f.Nodes[0].Id) return SceneKind.Ring;
        }
        // A decision tree keeps its questions in a line, its answers aside, as a layered chart lays it out; the tidy
        // tree, which centres every box over its children, would step it sideways at each question.
        bool decisions = f.Nodes.Any(x => x.Shape == NodeShape.Decision) || f.Edges.Count(e => e.Label is not null) * 2 > Math.Max(1, f.Edges.Count);
        if (!decisions && n <= 40 && f.Edges.Count == n - 1 && f.Edges.All(e => e.From != e.To)
            && f.Nodes.Count(x => ins[x.Id] == 0) == 1 && f.Nodes.All(x => ins[x.Id] <= 1))
        {
            var children = f.Edges.ToLookup(e => e.From, e => e.To);
            var reached = new HashSet<string>();
            var stack = new Stack<string>([f.Nodes.First(x => ins[x.Id] == 0).Id]);
            while (stack.Count > 0)
                if (reached.Add(stack.Peek())) foreach (string c in children[stack.Pop()]) stack.Push(c);
                else stack.Pop();
            if (reached.Count == n) return SceneKind.Tree;
        }
        return SceneKind.Layered;
    }

    // --- sizes ---

    sealed class Measurer(Func<string, double, bool, double> measure)
    {
        readonly Dictionary<(string, double, bool), double> cache = [];

        public double Width(string text, double size, bool bold)
        {
            if (text.Length == 0) return 0;
            if (cache.TryGetValue((text, size, bold), out double w)) return w;
            w = measure(text, size, bold);
            if (double.IsNaN(w) || double.IsInfinity(w) || w < 0) w = text.Length * size * 0.55;
            return cache[(text, size, bold)] = w;
        }
    }

    /// <summary>A box's size and its words wrapped, the last <see cref="Detail"/> of them its smaller words.</summary>
    sealed record Sized(double W, double H, List<string> Lines, int Detail = 0);

    /// <summary>A box in its place: a state diagram's start and end in the accent, so they read as marks, not states.</summary>
    static SceneNode Placed(FlowNode n, Sized s, Box box) =>
        new(n.Id, box, n.Shape, n.Role is NodeRole.Start or NodeRole.End && n.Tone == Tone.None ? Tone.Accent : n.Tone, s.Lines) { DetailLines = s.Detail };

    sealed record Words(List<string> Lines, double W, double H);

    /// <summary>
    /// A box's size for its words (its smaller words wrapped and sized as its own are, so the box holds them drawn
    /// either way): a state diagram's start a small dot, its end a small ring, a fork a bar, an empty choice a small
    /// rhombus.
    /// </summary>
    static Sized Size(FlowNode n, Measurer m)
    {
        switch (n.Role)
        {
            case NodeRole.Start: return new Sized(18, 18, []);
            case NodeRole.End: return new Sized(22, 22, []);
            case NodeRole.Bar: return new Sized(64, 8, []);
        }
        if (n.Shape == NodeShape.Decision && n.Lines.Count == 0 && n.Detail.Count == 0) return new Sized(30, 30, []);
        bool round = n.Shape is NodeShape.Circle or NodeShape.DoubleCircle;
        double max = round ? CircleLine : MaxLine;
        Func<string, double> width = s => m.Width(s, TextSize, true);
        var own = Wrap(n.Lines, max, width);
        // Smaller words wrap at their own size (so they run to fewer lines), but the box is as wide as they'd be at
        // full size: a renderer that draws them like the rest still fits them.
        var detail = Wrap(n.Detail, max, t => m.Width(t, SceneShapes.DetailSize, false));
        var lines = own.Concat(detail).ToList();
        double tw = lines.Count == 0 ? 0 : lines.Max(width);
        double th = Math.Max(1, lines.Count) * LineHeight;
        double h = th + 2 * PadY;
        int d = detail.Count;
        return n.Shape switch
        {
            NodeShape.Circle => Round(Math.Max(56, Math.Max(tw, th) + 36)),
            NodeShape.DoubleCircle => Round(Math.Max(64, Math.Max(tw, th) + 44)),
            NodeShape.Decision => Diamond(),
            NodeShape.Stadium => new Sized(Math.Max(MinWidth, tw + 2 * PadX + h / 2 - 6), h, lines, d),
            NodeShape.Hexagon => new Sized(Math.Max(MinWidth, tw + 2 * PadX + h / 2), h, lines, d),
            NodeShape.Subroutine => new Sized(Math.Max(MinWidth, tw + 2 * PadX + 16), h, lines, d),
            NodeShape.Cylinder => new Sized(Math.Max(MinWidth, tw + 2 * PadX), h + 12, lines, d),
            NodeShape.Parallelogram => new Sized(Math.Max(MinWidth, tw + 2 * PadX + SceneShapes.Slant(h)), h, lines, d),
            NodeShape.Trapezoid => new Sized(Math.Max(MinWidth, tw + 2 * PadX + 2 * SceneShapes.Slant(h) * (1 - PadY / h)), h, lines, d),
            _ => new Sized(Math.Max(MinWidth, tw + 2 * PadX), h, lines, d),
        };

        Sized Round(double size) => new(size, size, lines, d);

        // A rhombus around the words: wide enough that their corners clear its sides, never taller than it needs.
        Sized Diamond()
        {
            double w = Math.Max(96, tw + th * 1.2 + 24);
            double hd = Math.Max(th + 30, th / (1 - tw / w) + 8);
            return new Sized(w, hd, lines, d);
        }
    }

    static Words EdgeLabel(string? label, Measurer m)
    {
        if (string.IsNullOrWhiteSpace(label)) return new Words([], 0, 0);
        Func<string, double> width = s => m.Width(s, LabelSize, false);
        var lines = Wrap(label.Split('\n'), LabelLine, width);
        if (lines.Count == 0) return new Words([], 0, 0);
        return new Words(lines, lines.Max(width) + 8, lines.Count * LabelLineHeight + 2);
    }

    /// <summary>
    /// Words in balanced lines: the fewest lines that fit <paramref name="max"/>, then the narrowest width that keeps
    /// that many, so a box never ends on one lonely word. A word too wide for a line (a long name, words in a
    /// script without spaces) is split between its characters.
    /// </summary>
    internal static List<string> Wrap(IEnumerable<string> given, double max, Func<string, double> width)
    {
        var result = new List<string>();
        foreach (string line in given)
        {
            var words = line.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .SelectMany(w => width(w) > max * 1.25 ? Chop(w, max, width) : [w]).ToList();
            if (words.Count == 0) continue;
            var best = Greedy(words, max, width);
            if (best.Count > 1)
            {
                double widest = words.Max(width);
                var candidates = new SortedSet<double>();
                for (int i = 0; i < words.Count; i++)
                    for (int j = i; j < words.Count; j++)
                    {
                        double w = width(string.Join(' ', words.Skip(i).Take(j - i + 1)));
                        if (w > max) break;
                        if (w >= widest) candidates.Add(w);
                    }
                foreach (double c in candidates)
                {
                    var tried = Greedy(words, c + 0.01, width);
                    if (tried.Count <= best.Count) { best = tried; break; }
                }
            }
            result.AddRange(best);
        }
        return result;
    }

    static List<string> Greedy(List<string> words, double max, Func<string, double> width)
    {
        var lines = new List<string>();
        string current = "";
        foreach (string word in words)
        {
            string tried = current.Length == 0 ? word : current + " " + word;
            if (current.Length > 0 && width(tried) > max)
            {
                lines.Add(current);
                current = word;
            }
            else current = tried;
        }
        if (current.Length > 0) lines.Add(current);
        return lines;
    }

    static IEnumerable<string> Chop(string word, double max, Func<string, double> width)
    {
        var parts = new List<string>();
        string current = "";
        var e = StringInfo.GetTextElementEnumerator(word);
        while (e.MoveNext())
        {
            string ch = e.GetTextElement();
            if (current.Length > 0 && width(current + ch) > max)
            {
                parts.Add(current);
                current = "";
            }
            current += ch;
        }
        if (current.Length > 0) parts.Add(current);
        return parts;
    }

    static double MarkerLength(EdgeEnd end) => end switch
    {
        EdgeEnd.Arrow => ArrowLength,
        EdgeEnd.None => 0,
        _ => MarkLength,
    };

    // --- ring ---

    static DiagramScene Ring(Flowchart f, Dictionary<string, Sized> sizes, List<Words> labels)
    {
        int n = f.Nodes.Count;
        var next = f.Edges.Select((e, i) => (e, i)).ToDictionary(x => x.e.From);
        var order = new List<string> { f.Nodes[0].Id };
        while (order.Count < n) order.Add(next[order[^1]].e.To);
        double sum = order.Sum(id => sizes[id].W) + 40 * n;
        double widest = order.Max(id => sizes[id].W), tallest = order.Max(id => sizes[id].H);
        double rx = Math.Max(sum / (2 * Math.PI) * 1.05, widest * 0.9 + 40), ry = Math.Max(0.62 * rx, tallest * 1.6 + 30);
        var angles = Enumerable.Range(0, n).Select(i => -Math.PI / 2 + 2 * Math.PI * i / n).ToArray();
        var nodes = new SceneNode[n];
        for (int attempt = 0; ; attempt++)
        {
            for (int i = 0; i < n; i++)
            {
                var s = sizes[order[i]];
                var node = f.Node(order[i])!;
                nodes[i] = Placed(node, s, Box.Around(new Pt(rx * Math.Cos(angles[i]), ry * Math.Sin(angles[i])), s.W, s.H));
            }
            bool crowded = false;
            for (int i = 0; i < n && !crowded; i++)
                for (int j = i + 1; j < n && !crowded; j++)
                    crowded = nodes[i].Box.Inflate(10).Intersects(nodes[j].Box);
            if (!crowded || attempt > 12) break;
            rx *= 1.08;
            ry *= 1.08;
        }
        Pt E(double t) => new(rx * Math.Cos(t), ry * Math.Sin(t));
        Pt D(double t) => new(-rx * Math.Sin(t), ry * Math.Cos(t));
        var edges = new SceneEdge[n];
        for (int i = 0; i < n; i++)
        {
            var (edge, index) = next[order[i]];
            var from = nodes[i];
            var to = nodes[(i + 1) % n];
            double a0 = angles[i], a1 = angles[i] + 2 * Math.PI / n;
            // Along the ring: out of the first box, into the next, the markers taking their length off each end.
            double tOut = First(t => Outline(from, E(t)) > 1.5, a0, a1);
            double tIn = First(t => Outline(to, E(t)) > 1, a1, a0);
            var label = labels[index];
            if (tOut >= tIn)
            {
                edges[index] = Straight(edge, from, to, label);
                continue;
            }
            double tStart = tOut, tEnd = tIn;
            if (MarkerLength(edge.StartEnd) is var ls and > 0) tStart = First(t => Pt.Distance(E(t), E(tOut)) >= ls, tOut, tIn);
            if (MarkerLength(edge.EndEnd) is var le and > 0) tEnd = First(t => Pt.Distance(E(t), E(tIn)) >= le, tIn, tStart);
            var path = new List<PathStep> { new(PathVerb.Move, E(tStart)) };
            int pieces = (int)Math.Ceiling((tEnd - tStart) / (Math.PI / 4));
            for (int k = 0; k < pieces; k++)
            {
                double t0 = tStart + (tEnd - tStart) * k / pieces, t1 = tStart + (tEnd - tStart) * (k + 1) / pieces;
                double kappa = 4.0 / 3 * Math.Tan((t1 - t0) / 4);
                path.Add(new PathStep(PathVerb.Cubic, E(t0) + D(t0) * kappa, E(t1) - D(t1) * kappa, E(t1)));
            }
            var box = new Box();
            if (label.Lines.Count > 0)
            {
                double tm = (tOut + tIn) / 2;
                var normal = new Pt(ry * Math.Cos(tm), rx * Math.Sin(tm)).Unit();
                double reach = Math.Abs(normal.X) * label.W / 2 + Math.Abs(normal.Y) * label.H / 2 + 6;
                box = Box.Around(E(tm) + normal * reach, label.W, label.H);
                for (int step = 0; step < 8 && nodes.Any(nd => nd.Box.Inflate(4).Intersects(box)); step++) box = box.Offset(normal.X * 8, normal.Y * 8);
            }
            edges[index] = new SceneEdge(edge.From, edge.To, path, edge.Line, edge.StartEnd, edge.EndEnd,
                E(tOut), E(tStart), E(tIn), E(tEnd), label.Lines, box);
        }
        var ordered = f.Nodes.Select(x => nodes[order.IndexOf(x.Id)]).ToList();
        return new DiagramScene(SceneKind.Ring, f.Direction, 0, 0, ordered, edges, []);
    }

    /// <summary>The first point, walking from <paramref name="from"/> towards <paramref name="to"/>, where a test turns
    /// true: found by sampling, then halving.</summary>
    static double First(Func<double, bool> test, double from, double to)
    {
        const int Samples = 240;
        double prev = from;
        for (int k = 1; k <= Samples; k++)
        {
            double t = from + (to - from) * k / Samples;
            if (test(t))
            {
                double lo = prev, hi = t;
                for (int i = 0; i < 40; i++)
                {
                    double mid = (lo + hi) / 2;
                    if (test(mid)) hi = mid; else lo = mid;
                }
                return hi;
            }
            prev = t;
        }
        return to;
    }

    static SceneEdge Straight(FlowEdge edge, SceneNode from, SceneNode to, Words label)
    {
        var dir = (to.Box.Center - from.Box.Center).Unit();
        var start = Along(from, dir, 0);
        var tip = Along(to, dir * -1, 0);
        var startBase = start + dir * MarkerLength(edge.StartEnd);
        var endBase = tip - dir * MarkerLength(edge.EndEnd);
        var box = label.Lines.Count > 0 ? Box.Around((startBase + endBase) * 0.5, label.W, label.H) : new Box();
        return new SceneEdge(edge.From, edge.To, [new PathStep(PathVerb.Move, startBase), new PathStep(PathVerb.Line, endBase)],
            edge.Line, edge.StartEnd, edge.EndEnd, start, startBase, tip, endBase, label.Lines, box);
    }

    /// <summary>Where a ray from the box's centre in direction <paramref name="dir"/> leaves its outline.</summary>
    static Pt Along(SceneNode n, Pt dir, double gap)
    {
        var c = n.Box.Center;
        double lo = 0, hi = n.Box.W + n.Box.H;
        for (int i = 0; i < 40; i++)
        {
            double mid = (lo + hi) / 2;
            if (Outline(n, c + dir * mid) > gap) hi = mid; else lo = mid;
        }
        return c + dir * hi;
    }

    // --- tree ---

    static DiagramScene Tree(Flowchart f, Dictionary<string, Sized> sizes, List<Words> labels, ChartDirection dir)
    {
        bool vertical = dir is ChartDirection.TopDown or ChartDirection.BottomUp;
        double gap = vertical ? 24 : 16;
        var edgeIndex = f.Edges.Select((e, i) => (e, i)).ToList();
        var children = edgeIndex.ToLookup(x => x.e.From);
        string root = f.Nodes.First(x => f.Edges.All(e => e.To != x.Id)).Id;
        double Across(string id) => vertical ? sizes[id].W : sizes[id].H;
        double Deep(string id) => vertical ? sizes[id].H : sizes[id].W;

        var depth = new Dictionary<string, int>();
        var span = new Dictionary<string, double>();
        double Measure(string id, int d)
        {
            depth[id] = d;
            var kids = children[id].ToList();
            double total = kids.Sum(k => Measure(k.e.To, d + 1)) + gap * Math.Max(0, kids.Count - 1);
            return span[id] = Math.Max(Across(id), total);
        }
        Measure(root, 0);
        int levels = depth.Values.Max() + 1;
        var levelDeep = new double[levels];
        var gapAfter = new double[levels];
        foreach (var (id, d) in depth) levelDeep[d] = Math.Max(levelDeep[d], Deep(id));
        foreach (var (e, i) in edgeIndex)
        {
            var l = labels[i];
            double need = l.Lines.Count == 0 ? 0 : (vertical ? l.H : l.W) + 28;
            gapAfter[depth[e.From]] = Math.Max(gapAfter[depth[e.From]], Math.Max(44, need));
        }
        var across = new Dictionary<string, double>();
        void Place(string id, double left)
        {
            var kids = children[id].ToList();
            if (kids.Count == 0) { across[id] = left + span[id] / 2; return; }
            double total = kids.Sum(k => span[k.e.To]) + gap * (kids.Count - 1);
            double start = left + (span[id] - total) / 2;
            foreach (var k in kids)
            {
                Place(k.e.To, start);
                start += span[k.e.To] + gap;
            }
            across[id] = (across[kids[0].e.To] + across[kids[^1].e.To]) / 2;
        }
        Place(root, 0);
        // A wide fan gets a deeper gap, so its curves don't run flat along the level.
        foreach (var (e, _) in edgeIndex)
            gapAfter[depth[e.From]] = Math.Max(gapAfter[depth[e.From]], Math.Min(96, Math.Abs(across[e.To] - across[e.From]) * 0.22));
        var centreLine = new double[levels];
        double at = 0;
        for (int d = 0; d < levels; d++)
        {
            centreLine[d] = at + levelDeep[d] / 2;
            at += levelDeep[d] + Math.Max(44, gapAfter[d]);
        }
        double flip = dir is ChartDirection.BottomUp or ChartDirection.RightLeft ? -1 : 1;
        Pt Centre(string id) => vertical ? new Pt(across[id], flip * centreLine[depth[id]]) : new Pt(flip * centreLine[depth[id]], across[id]);
        var nodes = f.Nodes.Select(x => Placed(x, sizes[x.Id], Box.Around(Centre(x.Id), sizes[x.Id].W, sizes[x.Id].H))).ToList();
        var byId = nodes.ToDictionary(x => x.Id);
        var down = vertical ? new Pt(0, flip) : new Pt(flip, 0);
        var edges = new SceneEdge[f.Edges.Count];
        var mids = new Pt[f.Edges.Count];
        foreach (var (e, i) in edgeIndex)
        {
            var from = byId[e.From];
            var to = byId[e.To];
            var start = from.Box.Center + down * (Deep(e.From) / 2);
            var tip = to.Box.Center - down * (Deep(e.To) / 2);
            var startBase = start + down * MarkerLength(e.StartEnd);
            var endBase = tip - down * MarkerLength(e.EndEnd);
            // A soft S from the parent's side to the child's: leaves and arrives square to the level lines.
            double half = ((endBase - startBase).X * down.X + (endBase - startBase).Y * down.Y) / 2;
            var c1 = startBase + down * half;
            var c2 = endBase - down * half;
            edges[i] = new SceneEdge(e.From, e.To, [new PathStep(PathVerb.Move, startBase), new PathStep(PathVerb.Cubic, c1, c2, endBase)],
                e.Line, e.StartEnd, e.EndEnd, start, startBase, tip, endBase, labels[i].Lines, new Box());
            mids[i] = Bezier(startBase, c1, c2, endBase, 0.5);
        }
        // Words sit on their line, halfway; where two would touch, both move closer to their children.
        var boxes = edgeIndex.Select(x => labels[x.i].Lines.Count == 0 ? new Box() : Box.Around(mids[x.i], labels[x.i].W, labels[x.i].H)).ToArray();
        for (int i = 0; i < boxes.Length; i++)
        {
            if (boxes[i].W == 0) continue;
            bool clash = boxes.Where((b, j) => j != i && b.W > 0).Any(b => b.Inflate(3).Intersects(boxes[i]));
            if (!clash) continue;
            var steps = edges[i].Path[1];
            boxes[i] = Box.Around(Bezier(edges[i].Path[0].A, steps.A, steps.B, steps.C, 0.72), boxes[i].W, boxes[i].H);
        }
        for (int i = 0; i < edges.Length; i++) edges[i] = edges[i] with { LabelBox = boxes[i] };
        return new DiagramScene(SceneKind.Tree, dir, 0, 0, nodes, edges, []);
    }

    internal static Pt Bezier(Pt p0, Pt p1, Pt p2, Pt p3, double t)
    {
        double u = 1 - t;
        return p0 * (u * u * u) + p1 * (3 * u * u * t) + p2 * (3 * u * t * t) + p3 * (t * t * t);
    }

    // --- layered (MSAGL) ---

    static DiagramScene Layered(Flowchart f, Dictionary<string, Sized> sizes, List<Words> labels, ChartDirection dir, Measurer m)
    {
        var back = BackEdges(f);
        var pairs = Siblings(f, back);
        bool across = dir is ChartDirection.TopDown or ChartDirection.BottomUp;
        // Siblings in the written order ("yes" before "no") read better, but not at the price of lines crossing.
        // MSAGL's own layout, the same mirrored, and one made to keep the order: the fewest crossings wins, then the
        // fewest siblings out of order.
        DiagramScene Best(HashSet<string>? roomy)
        {
            var free = Layered(f, sizes, labels, dir, m, back, pairs: null, roomy);
            var candidates = new List<DiagramScene> { free };
            if (pairs.Count > 0)
            {
                candidates.Add(Mirror(free, across));
                try
                {
                    candidates.Add(Layered(f, sizes, labels, dir, m, back, pairs, roomy));
                }
                catch (Exception)
                {
                    // Keeping the order is a wish, not a need: without it MSAGL always finds a layout.
                }
            }
            return Titles(ByTheirLines(candidates.OrderBy(Crossings).ThenBy(c => OutOfOrder(c, pairs, across)).First()), out _);
        }
        var scene = Best(null);
        // A group whose title every arrow into it runs across gets a margin of its own for the title, on its left.
        var crossed = Crossed(scene);
        if (crossed.Count == 0) return scene;
        var roomier = Best(crossed);
        return Crossed(roomier).Count < crossed.Count ? roomier : scene;
    }

    /// <summary>The groups whose titles an arrow or an arrow's words still run across.</summary>
    static HashSet<string> Crossed(DiagramScene s)
    {
        Titles(s, out var crossed);
        return crossed;
    }

    /// <summary>
    /// Each group's title along its top: top left, unless an arrow (or an arrow's words) runs over that spot; then top
    /// right, then the clear spot nearest the left. With no clear spot at all (<paramref name="crossed"/> names those
    /// groups), the spot the fewest arrows cross, where the lines leave a gap for it.
    /// </summary>
    static DiagramScene Titles(DiagramScene s, out HashSet<string> crossed)
    {
        crossed = [];
        if (s.Groups.Count == 0) return s;
        var lines = s.Edges.Select(Flatten).ToList();
        var words = s.Edges.Where(e => e.LabelLines.Count > 0).Select(e => e.LabelBox).ToList();
        int Crossing(Box title) =>
            lines.Count(points => points.Zip(points.Skip(1)).Any(seg => Touches(title.Inflate(2), seg.First, seg.Second)))
            + words.Count(w => w.Intersects(title.Inflate(2)));
        var groups = new List<SceneGroup>();
        foreach (var g in s.Groups)
        {
            var left = g.TitleBox with { X = g.Box.X + 12, Y = g.Box.Y + 7 };
            double last = Math.Max(left.X, g.Box.Right - 12 - left.W);
            var spots = new List<Box> { left, left with { X = last } };
            for (double x = left.X + 2; x < last; x += 2) spots.Add(left with { X = x });
            var best = spots.Select(b => (Box: b, Crossing: Crossing(b))).MinBy(x => x.Crossing);
            if (best.Crossing > 0) crossed.Add(g.Id);
            groups.Add(g with { TitleBox = best.Box });
        }
        return s with { Groups = groups };
    }

    /// <summary>
    /// An arrow's words kept by their own arrow. MSAGL places them beside it, but in a busy chart another line can run
    /// right past (or through) them, and then they read as that line's: such words move along their own line, to the
    /// nearest spot beside it that no other line, box, group title or words come near.
    /// </summary>
    static DiagramScene ByTheirLines(DiagramScene s)
    {
        if (!s.Edges.Any(e => e.LabelLines.Count > 0)) return s;
        var lines = s.Edges.Select(Flatten).ToList();
        var edges = s.Edges.ToList();
        // Beside their line (not so close that it leaves a gap in it), and clear of every other line.
        const double Beside = 8, Clear = 10;
        bool Blocked(Box b, int own) =>
            s.Nodes.Any(n => n.Box.Inflate(3).Intersects(b))
            || edges.Where((e, j) => j != own && e.LabelLines.Count > 0).Any(e => e.LabelBox.Inflate(3).Intersects(b))
            || s.Groups.Any(g => g.TitleBox.Inflate(2).Intersects(b));
        double Others(Box b, int own) => lines.Where((_, j) => j != own).Select(l => Distance(b, l)).DefaultIfEmpty(double.MaxValue).Min();
        bool Good(Box b, int own)
        {
            double mine = Distance(b, lines[own]);
            return !Blocked(b, own) && mine is >= 2.5 and <= Beside && Others(b, own) >= Clear;
        }
        for (int i = 0; i < edges.Count; i++)
        {
            var e = edges[i];
            if (e.LabelLines.Count == 0 || Good(e.LabelBox, i)) continue;
            var own = lines[i];
            var cumulative = new double[own.Count];
            for (int k = 1; k < own.Count; k++) cumulative[k] = cumulative[k - 1] + Pt.Distance(own[k - 1], own[k]);
            double total = cumulative[^1];
            if (total < 1) continue;
            // From the middle out, either side of the line; the first good spot wins, else the one furthest from
            // every other line (the spot it has, if none is further).
            var spots = new List<Box>();
            foreach (double t in new[] { 0.5, 0.42, 0.58, 0.34, 0.66, 0.26, 0.74, 0.18, 0.82 })
            {
                int k = Math.Max(1, Array.FindIndex(cumulative, c => c >= t * total));
                var along = (own[k] - own[k - 1]).Unit();
                var at = own[k - 1] + along * (t * total - cumulative[k - 1]);
                var normal = new Pt(-along.Y, along.X);
                double w = e.LabelBox.W, h = e.LabelBox.H;
                double reach = Math.Abs(normal.X) * w / 2 + Math.Abs(normal.Y) * h / 2 + 4;
                spots.Add(Box.Around(at + normal * reach, w, h));
                spots.Add(Box.Around(at - normal * reach, w, h));
            }
            var best = spots.Cast<Box?>().FirstOrDefault(b => Good(b!.Value, i))
                ?? spots.Where(b => !Blocked(b, i) && Distance(b, own) >= 2.5).Prepend(e.LabelBox).MaxBy(b => Math.Min(Others(b, i), 40) - Distance(b, own));
            edges[i] = e with { LabelBox = best };
        }
        return s with { Edges = edges };
    }

    /// <summary>How far a box is from a line (0 when the line touches it).</summary>
    internal static double Distance(Box b, List<Pt> line)
    {
        double best = double.MaxValue;
        Pt[] corners = [new(b.X, b.Y), new(b.Right, b.Y), new(b.Right, b.Bottom), new(b.X, b.Bottom)];
        for (int k = 1; k < line.Count; k++)
        {
            var (p, q) = (line[k - 1], line[k]);
            if (Touches(b, p, q)) return 0;
            best = Math.Min(best, Math.Min(ToBox(b, p), ToBox(b, q)));
            foreach (var c in corners) best = Math.Min(best, ToSegment(c, p, q));
        }
        return best;

        static double ToBox(Box b, Pt p) => Math.Sqrt(Math.Pow(Math.Max(0, Math.Max(b.X - p.X, p.X - b.Right)), 2) + Math.Pow(Math.Max(0, Math.Max(b.Y - p.Y, p.Y - b.Bottom)), 2));

        static double ToSegment(Pt c, Pt p, Pt q)
        {
            var d = q - p;
            double len = d.X * d.X + d.Y * d.Y;
            double t = len < 1e-9 ? 0 : Math.Clamp(((c.X - p.X) * d.X + (c.Y - p.Y) * d.Y) / len, 0, 1);
            return Pt.Distance(c, p + d * t);
        }
    }

    /// <summary>Whether a line segment touches a box.</summary>
    static bool Touches(Box b, Pt p, Pt q)
    {
        if (b.Contains(p) || b.Contains(q)) return true;
        Pt[] c = [new(b.X, b.Y), new(b.Right, b.Y), new(b.Right, b.Bottom), new(b.X, b.Bottom)];
        for (int i = 0; i < 4; i++)
            if (Cross(p, q, c[i], c[(i + 1) % 4]) is not null) return true;
        return false;
    }

    /// <summary>Boxes that share a parent and a level, in the order their arrows were written: the pairs that should
    /// read left to right (or top to bottom).</summary>
    static List<(string First, string Second)> Siblings(Flowchart f, HashSet<int> back)
    {
        var level = Levels(f, back);
        var group = f.Nodes.ToDictionary(n => n.Id, n => f.Groups.FirstOrDefault(g => g.Members.Contains(n.Id))?.Id);
        var pairs = new List<(string, string)>();
        foreach (var siblings in f.Edges.Where((e, i) => e.From != e.To && !back.Contains(i)).GroupBy(e => e.From))
        {
            var kids = siblings.Select(e => e.To).Distinct().ToList();
            for (int k = 0; k + 1 < kids.Count; k++)
                if (level[kids[k]] == level[kids[k + 1]] && group[kids[k]] == group[kids[k + 1]]) pairs.Add((kids[k], kids[k + 1]));
        }
        return pairs;
    }

    static int OutOfOrder(DiagramScene s, List<(string First, string Second)> pairs, bool across)
    {
        var at = s.Nodes.ToDictionary(n => n.Id, n => n.Box.Center);
        return pairs.Count(p => across ? at[p.First].X > at[p.Second].X : at[p.First].Y > at[p.Second].Y);
    }

    /// <summary>The same layout seen in a mirror: left for right in a top-down chart, top for bottom across.</summary>
    static DiagramScene Mirror(DiagramScene s, bool across)
    {
        Pt P(Pt p) => across ? new Pt(-p.X, p.Y) : new Pt(p.X, -p.Y);
        Box B(Box b) => b.W == 0 && b.H == 0 ? b : across ? b with { X = -b.Right } : b with { Y = -b.Bottom };
        return s with
        {
            Nodes = s.Nodes.Select(n => n with { Box = B(n.Box) }).ToList(),
            Groups = s.Groups.Select(g => { var box = B(g.Box); return g with { Box = box, TitleBox = g.TitleBox with { X = box.X + 12, Y = box.Y + 7 } }; }).ToList(),
            Edges = s.Edges.Select(e => e with
            {
                Path = e.Path.Select(p => new PathStep(p.Verb, P(p.A), P(p.B), P(p.C))).ToList(),
                StartTip = P(e.StartTip),
                StartBase = P(e.StartBase),
                Tip = P(e.Tip),
                Base = P(e.Base),
                LabelBox = B(e.LabelBox),
            }).ToList(),
        };
    }

    /// <summary>How many times the arrows cross each other away from the boxes.</summary>
    internal static int Crossings(DiagramScene s)
    {
        var lines = s.Edges.Select(e => Flatten(e)).ToList();
        var boxes = s.Nodes.Select(n => n.Box.Inflate(4)).ToList();
        int count = 0;
        for (int i = 0; i < lines.Count; i++)
            for (int j = i + 1; j < lines.Count; j++)
                for (int a = 1; a < lines[i].Count; a++)
                    for (int b = 1; b < lines[j].Count; b++)
                        if (Cross(lines[i][a - 1], lines[i][a], lines[j][b - 1], lines[j][b]) is { } p && !boxes.Any(x => x.Contains(p)))
                            count++;
        return count;
    }

    static Pt? Cross(Pt p1, Pt p2, Pt q1, Pt q2)
    {
        var r = p2 - p1;
        var d = q2 - q1;
        double den = r.X * d.Y - r.Y * d.X;
        if (Math.Abs(den) < 1e-9) return null;
        var w = q1 - p1;
        double t = (w.X * d.Y - w.Y * d.X) / den, u = (w.X * r.Y - w.Y * r.X) / den;
        return t is >= 0 and <= 1 && u is >= 0 and <= 1 ? p1 + r * t : null;
    }

    /// <summary>
    /// The arrows that close a loop, found walking the chart in the order it was written (so the first box written is
    /// at the top and an arrow back to it, like "still in pain", is the one that runs against the flow).
    /// </summary>
    static HashSet<int> BackEdges(Flowchart f)
    {
        var outs = f.Edges.Select((e, i) => (e, i)).Where(x => x.e.From != x.e.To).ToLookup(x => x.e.From, x => x.i);
        var state = f.Nodes.ToDictionary(n => n.Id, _ => 0);
        var back = new HashSet<int>();
        void Visit(string id)
        {
            state[id] = 1;
            foreach (int i in outs[id])
            {
                string to = f.Edges[i].To;
                if (state[to] == 1) back.Add(i);
                else if (state[to] == 0) Visit(to);
            }
            state[id] = 2;
        }
        foreach (var n in f.Nodes) if (state[n.Id] == 0) Visit(n.Id);
        return back;
    }

    static DiagramScene Layered(Flowchart f, Dictionary<string, Sized> sizes, List<Words> labels, ChartDirection dir, Measurer m, HashSet<int> back,
        List<(string First, string Second)>? pairs, HashSet<string>? roomy)
    {
        var g = new GeometryGraph();
        // MSAGL's cluster layout throws without something here.
        g.RootCluster.UserData = "root";
        var clusters = new Dictionary<string, Cluster>();
        foreach (var group in f.Groups)
        {
            double title = m.Width(group.Title, TitleSize, true) * 1.04;
            // A title that every arrow into its group ran across gets a margin of its own on the left, over no box.
            var cluster = new Cluster
            {
                UserData = group.Id,
                RectangularBoundary = new RectangularClusterBoundary
                {
                    LeftMargin = roomy?.Contains(group.Id) == true ? title + 20 : 12, RightMargin = 12, BottomMargin = 12, TopMargin = 30,
                    MinWidth = title + 24, MinHeight = 40,
                },
            };
            (group.Parent is { } parent && clusters.TryGetValue(parent, out var p) ? p : g.RootCluster).AddChild(cluster);
            clusters[group.Id] = cluster;
        }
        var loops = f.Edges.Where(e => e.From == e.To).Select(e => e.From).ToHashSet();
        var nodes = new Dictionary<string, MsaglNode>();
        var groupOf = new Dictionary<string, string?>();
        foreach (var n in f.Nodes)
        {
            var s = sizes[n.Id];
            // A box with an arrow back to itself asks for room on both sides; the loop is drawn on its right.
            double w = s.W + (loops.Contains(n.Id) ? 2 * LoopRoom : 0);
            var node = new MsaglNode(Curve(n.Shape, w, s.H), n.Id);
            g.Nodes.Add(node);
            nodes[n.Id] = node;
            var group = f.Groups.FirstOrDefault(x => x.Members.Contains(n.Id));
            groupOf[n.Id] = group?.Id;
            if (group is not null) clusters[group.Id].AddChild(node);
            else g.RootCluster.AddChild(node);
        }
        var routed = new List<(int Index, MsaglEdge Edge, bool Reversed)>();
        // MSAGL spreads arrows between the same two boxes in an order that changes from run to run, so only the
        // first is routed; the others bow out beside it.
        var firstBetween = new Dictionary<(string, string), int>();
        var alongside = new List<(int Index, int First, int Nth)>();
        for (int i = 0; i < f.Edges.Count; i++)
        {
            var e = f.Edges[i];
            if (e.From == e.To) continue;
            var pair = string.CompareOrdinal(e.From, e.To) < 0 ? (e.From, e.To) : (e.To, e.From);
            if (firstBetween.TryGetValue(pair, out int first))
            {
                alongside.Add((i, first, alongside.Count(a => a.First == first) + 1));
                continue;
            }
            firstBetween[pair] = i;
            // An arrow that closes a loop is laid out the other way round, then turned back when it's drawn.
            bool reversed = back.Contains(i);
            var ge = reversed ? new MsaglEdge(nodes[e.To], nodes[e.From]) : new MsaglEdge(nodes[e.From], nodes[e.To]);
            var (atTarget, atSource) = reversed ? (e.StartEnd, e.EndEnd) : (e.EndEnd, e.StartEnd);
            if (atTarget != EdgeEnd.None) ge.EdgeGeometry.TargetArrowhead = new Arrowhead { Length = MarkerLength(atTarget) };
            if (atSource != EdgeEnd.None) ge.EdgeGeometry.SourceArrowhead = new Arrowhead { Length = MarkerLength(atSource) };
            if (labels[i].Lines.Count > 0) ge.Label = new MsaglLabel(labels[i].W, labels[i].H, ge);
            g.Edges.Add(ge);
            routed.Add((i, ge, reversed));
        }
        // MSAGL lays each group out as one box in the level around it, but doesn't see the arrows that run into or
        // out of the group when it decides that level's order, so a group could float anywhere. A stand-in arrow
        // between the group and the box (or group) at the other end, at the level where they meet, puts it in line.
        var parentOf = f.Groups.ToDictionary(x => x.Id, x => x.Parent);
        List<string?> Chain(string id)
        {
            var chain = new List<string?>();
            for (string? at = groupOf[id]; at is not null; at = parentOf[at]) chain.Add(at);
            chain.Add(null);
            return chain;
        }
        var standIns = new HashSet<(string, string)>();
        foreach (var (i, ge, reversed) in routed)
        {
            string from = reversed ? f.Edges[i].To : f.Edges[i].From, to = reversed ? f.Edges[i].From : f.Edges[i].To;
            var up = Chain(from);
            var down = Chain(to);
            string? meet = up.First(down.Contains);
            // Just below where the two chains meet: the box itself, or the outermost group holding it there.
            string Top(string node, List<string?> chain)
            {
                int k = chain.IndexOf(meet);
                return k == 0 ? node : "group:" + chain[k - 1];
            }
            string a = Top(from, up), b = Top(to, down);
            if (a == from && b == to) continue;
            if (!standIns.Add((a, b))) continue;
            MsaglNode End(string key) => key.StartsWith("group:", StringComparison.Ordinal) ? clusters[key[6..]] : nodes[key];
            g.Edges.Add(new MsaglEdge(End(a), End(b)));
        }
        var settings = new SugiyamaLayoutSettings { NodeSeparation = 28, LayerSeparation = 44 };
        settings.EdgeRoutingSettings.EdgeRoutingMode = f.Groups.Count > 0 ? EdgeRoutingMode.Spline : EdgeRoutingMode.SugiyamaSplines;
        double turn = dir switch
        {
            ChartDirection.LeftRight => Math.PI / 2,
            ChartDirection.RightLeft => -Math.PI / 2,
            ChartDirection.BottomUp => Math.PI,
            _ => 0,
        };
        if (turn != 0) settings.Transformation = PlaneTransformation.Rotation(turn);
        if (pairs is not null)
        {
            // MSAGL's left becomes the bottom once turned left-to-right, and the right once turned bottom-up.
            bool flip = dir is ChartDirection.LeftRight or ChartDirection.BottomUp;
            foreach (var (a, b) in pairs)
                if (flip) settings.AddLeftRightConstraint(nodes[b], nodes[a]);
                else settings.AddLeftRightConstraint(nodes[a], nodes[b]);
        }
        LayoutHelpers.CalculateLayout(g, settings, null);

        // MSAGL's y points up; the scene's points down.
        static Pt P(MPoint p) => new(p.X, -p.Y);
        var sceneNodes = f.Nodes.Select(n => Placed(n, sizes[n.Id], Box.Around(P(nodes[n.Id].Center), sizes[n.Id].W, sizes[n.Id].H))).ToList();
        var byId = sceneNodes.ToDictionary(n => n.Id);
        var edges = new SceneEdge[f.Edges.Count];
        foreach (var (i, ge, reversed) in routed)
        {
            var e = f.Edges[i];
            var path = new List<PathStep> { new(PathVerb.Move, P(ge.Curve.Start)) };
            AddCurve(path, ge.Curve);
            var curveStart = P(ge.Curve.Start);
            var curveEnd = P(ge.Curve.End);
            var sourceTip = ge.EdgeGeometry.SourceArrowhead is { } sa ? P(sa.TipPosition) : curveStart;
            var targetTip = ge.EdgeGeometry.TargetArrowhead is { } ta ? P(ta.TipPosition) : curveEnd;
            var box = ge.Label is { } l ? Box.Around(P(l.Center), labels[i].W, labels[i].H) : new Box();
            edges[i] = reversed
                ? new SceneEdge(e.From, e.To, Reverse(path), e.Line, e.StartEnd, e.EndEnd, targetTip, curveEnd, sourceTip, curveStart, labels[i].Lines, box)
                : new SceneEdge(e.From, e.To, path, e.Line, e.StartEnd, e.EndEnd, sourceTip, curveStart, targetTip, curveEnd, labels[i].Lines, box);
        }
        // A box with a loop was laid out wider than it is (room for the loop): an arrow that met that wider outline
        // is brought on to the box's own.
        foreach (var (i, _, _) in routed)
        {
            var e = f.Edges[i];
            if (loops.Contains(e.From) || loops.Contains(e.To)) edges[i] = Reclipped(edges[i], e, byId[e.From], byId[e.To], loops);
        }
        foreach (var (i, first, nth) in alongside) edges[i] = Beside(f.Edges[i], labels[i], edges[first], nth, byId[f.Edges[i].From], byId[f.Edges[i].To]);
        for (int i = 0; i < f.Edges.Count; i++)
            if (f.Edges[i].From == f.Edges[i].To)
            {
                var n = byId[f.Edges[i].From];
                edges[i] = Loop(f.Edges[i], n, labels[i], QuietSide(n, edges.Where((x, j) => x is not null && j != i && f.Edges[j].From != f.Edges[j].To)!));
            }
        var groups = f.Groups.Select(group =>
        {
            var r = clusters[group.Id].BoundingBox;
            var box = new Box(r.Left, -r.Top, r.Width, r.Height);
            return new SceneGroup(group.Id, group.Title, box, new Box(box.X + 12, box.Y + 7, m.Width(group.Title, TitleSize, true), 16), group.Parent is null ? 0 : 1);
        }).ToList();
        return new DiagramScene(SceneKind.Layered, dir, 0, 0, sceneNodes, edges, groups);
    }

    /// <summary>
    /// Another arrow between the same two boxes as <paramref name="first"/>: beside it all the way, with ends of its
    /// own a little along each box's edge (the second arrow on one side, the third on the other, further out after
    /// that), bowed out a little more between them, and with its own markers and words.
    /// </summary>
    static SceneEdge Beside(FlowEdge e, Words label, SceneEdge first, int nth, SceneNode from, SceneNode to)
    {
        var points = Flatten(first);
        if (first.From != e.From) points.Reverse();
        // Points every few pixels, so a straight first arrow still bows smoothly.
        points = Resample(points, 6);
        var cumulative = new double[points.Count];
        for (int k = 1; k < points.Count; k++) cumulative[k] = cumulative[k - 1] + Pt.Distance(points[k - 1], points[k]);
        double total = Math.Max(cumulative[^1], 1e-6);
        var dir = (points[^1] - points[0]).Unit();
        var normal = new Pt(-dir.Y, dir.X);
        double side = nth % 2 == 1 ? 1 : -1, step = (nth + 1) / 2;
        // Each end slides along its box's edge until it's 12 px to the side, and the line between moves over with
        // them, bowed out a little more.
        var startBy = Slide(from, points[0], step * 12, normal * side) - points[0];
        var endBy = Slide(to, points[^1], step * 12, normal * side) - points[^1];
        for (int k = 0; k < points.Count; k++)
        {
            double u = cumulative[k] / total;
            points[k] += startBy * (1 - u) + endBy * u + normal * (side * step * 10 * Math.Sin(Math.PI * u));
        }
        // Each end cut (or run on) to where the moved line meets its box.
        OntoOutline(points, to);
        points.Reverse();
        OntoOutline(points, from);
        points.Reverse();
        var (startTip, startBase) = Cut(points, MarkerLength(e.StartEnd), fromEnd: false);
        var (tip, endBase) = Cut(points, MarkerLength(e.EndEnd), fromEnd: true);
        var path = new List<PathStep> { new(PathVerb.Move, startBase) };
        path.AddRange(points.Skip(1).SkipLast(1).Select(p => new PathStep(PathVerb.Line, p)));
        path.Add(new PathStep(PathVerb.Line, endBase));
        var box = new Box();
        if (label.Lines.Count > 0)
        {
            var middle = points[Math.Max(0, Array.FindIndex(cumulative, c => c >= total / 2))];
            var away = normal * side;
            box = Box.Around(middle + away * (Math.Abs(away.X) * label.W / 2 + Math.Abs(away.Y) * label.H / 2 + 4), label.W, label.H);
        }
        return new SceneEdge(e.From, e.To, path, e.Line, e.StartEnd, e.EndEnd, startTip, startBase, tip, endBase, label.Lines, box);
    }

    /// <summary>An arrow ending on the outline of a box laid out wider than it is (see <see cref="Layered"/>), ended on
    /// the box's own outline instead, its last stretch aimed at the box's middle; its markers cut again.</summary>
    static SceneEdge Reclipped(SceneEdge e, FlowEdge fe, SceneNode from, SceneNode to, HashSet<string> wide)
    {
        var points = Flatten(e);
        static void EndOn(List<Pt> pts, SceneNode n)
        {
            int k = pts.Count - 1;
            while (k > 0 && Outline(n, pts[k]) <= 0) k--;
            if (Outline(n, pts[k]) <= 0) return;
            var outside = pts[k];
            var inside = n.Box.Center;
            for (int i = 0; i < 40; i++)
            {
                var mid = (outside + inside) * 0.5;
                if (Outline(n, mid) > 0) outside = mid; else inside = mid;
            }
            pts.RemoveRange(k + 1, pts.Count - k - 1);
            pts.Add(inside);
        }
        if (wide.Contains(to.Id)) EndOn(points, to);
        if (wide.Contains(from.Id))
        {
            points.Reverse();
            EndOn(points, from);
            points.Reverse();
        }
        if (points.Count < 2) return e;
        var (startTip, startBase) = Cut(points, MarkerLength(fe.StartEnd), fromEnd: false);
        var (tip, endBase) = Cut(points, MarkerLength(fe.EndEnd), fromEnd: true);
        var path = new List<PathStep> { new(PathVerb.Move, startBase) };
        path.AddRange(points.Skip(1).SkipLast(1).Select(p => new PathStep(PathVerb.Line, p)));
        path.Add(new PathStep(PathVerb.Line, endBase));
        return e with { Path = path, StartTip = startTip, StartBase = startBase, Tip = tip, Base = endBase };
    }

    /// <summary>The same line with a point at least every <paramref name="spacing"/> pixels along it.</summary>
    static List<Pt> Resample(List<Pt> points, double spacing)
    {
        var dense = new List<Pt> { points[0] };
        for (int k = 1; k < points.Count; k++)
        {
            int pieces = Math.Max(1, (int)Math.Ceiling(Pt.Distance(points[k - 1], points[k]) / spacing));
            for (int j = 1; j <= pieces; j++) dense.Add(points[k - 1] + (points[k] - points[k - 1]) * ((double)j / pieces));
        }
        return dense;
    }

    /// <summary>The point along <paramref name="n"/>'s outline from <paramref name="p"/> (a point on it) that is
    /// <paramref name="by"/> pixels over in the direction <paramref name="toward"/> (a unit vector), or as near as the
    /// outline gets within a box's width.</summary>
    static Pt Slide(SceneNode n, Pt p, double by, Pt toward)
    {
        var q = p;
        for (double walked = 0; walked < n.Box.W + n.Box.H; walked += 1)
        {
            double over = (q.X - p.X) * toward.X + (q.Y - p.Y) * toward.Y;
            if (over >= by) break;
            var g = Gradient(n, q);
            var tangent = new Pt(-g.Y, g.X);
            if (tangent.X * toward.X + tangent.Y * toward.Y < 0) tangent *= -1;
            q += tangent;
            for (int i = 0; i < 3; i++) q -= Gradient(n, q) * Outline(n, q);
        }
        return q;
    }

    /// <summary>Which way is straight out of the box's outline at <paramref name="p"/>.</summary>
    static Pt Gradient(SceneNode n, Pt p)
    {
        const double h = 0.5;
        var dx = new Pt(h, 0);
        var dy = new Pt(0, h);
        return new Pt(Outline(n, p + dx) - Outline(n, p - dx), Outline(n, p + dy) - Outline(n, p - dy)).Unit();
    }

    /// <summary>Ends a line heading into <paramref name="n"/> where it first meets the box's outline: cut short if
    /// it runs inside, run on straight if it stops short.</summary>
    static void OntoOutline(List<Pt> points, SceneNode n)
    {
        static Pt Crossing(SceneNode n, Pt outside, Pt inside)
        {
            for (int i = 0; i < 40; i++)
            {
                var mid = (outside + inside) * 0.5;
                if (Outline(n, mid) > 0) outside = mid; else inside = mid;
            }
            return inside;
        }
        int last = points.Count - 1;
        if (Outline(n, points[last]) > 0)
        {
            var dir = (points[last] - points[last - 1]).Unit();
            for (double d = 2; d <= n.Box.W + n.Box.H; d += 2)
            {
                var ahead = points[last] + dir * d;
                if (Outline(n, ahead) > 0) continue;
                points[last] = Crossing(n, points[last], ahead);
                break;
            }
            return;
        }
        int k = last;
        while (k > 0 && Outline(n, points[k]) <= 0) k--;
        if (Outline(n, points[k]) <= 0) return; // it starts inside the box too; leave it be
        var end = Crossing(n, points[k], points[k + 1]);
        points.RemoveRange(k + 1, points.Count - k - 1);
        points.Add(end);
    }

    internal static List<Pt> Flatten(SceneEdge e)
    {
        var points = new List<Pt> { e.StartTip };
        var at = e.Path[0].A;
        if (Pt.Distance(at, e.StartTip) > 0.01) points.Add(at);
        foreach (var step in e.Path.Skip(1))
        {
            if (step.Verb == PathVerb.Cubic)
                for (int k = 1; k <= 12; k++) points.Add(Bezier(at, step.A, step.B, step.C, k / 12.0));
            else points.Add(step.A);
            at = step.End;
        }
        if (Pt.Distance(at, e.Tip) > 0.01) points.Add(e.Tip);
        return points;
    }

    /// <summary>Takes a marker's length off one end of a run of points: the end itself is the marker's tip, and the
    /// line now stops at its base.</summary>
    static (Pt Tip, Pt Base) Cut(List<Pt> points, double length, bool fromEnd)
    {
        if (fromEnd) points.Reverse();
        var tip = points[0];
        var @base = tip;
        if (length > 0)
        {
            // The marker's base: its length back along the line, on whichever piece of it that falls.
            double walked = 0;
            int k = 1;
            for (; k < points.Count; k++)
            {
                double step = Pt.Distance(points[k - 1], points[k]);
                if (walked + step >= length)
                {
                    @base = points[k - 1] + (points[k] - points[k - 1]).Unit() * (length - walked);
                    break;
                }
                walked += step;
            }
            if (k == points.Count)
            {
                // The whole line is shorter than its marker: the marker takes all of it.
                k = points.Count - 1;
                @base = points[k];
            }
            points.RemoveRange(0, k);
            points.Insert(0, @base);
        }
        if (fromEnd) points.Reverse();
        return (tip, @base);
    }

    /// <summary>Each box's level once the loops are cut: the longest run of arrows that leads to it.</summary>
    static Dictionary<string, int> Levels(Flowchart f, HashSet<int> back)
    {
        var level = f.Nodes.ToDictionary(n => n.Id, _ => 0);
        var forward = f.Edges.Where((e, i) => e.From != e.To && !back.Contains(i)).ToList();
        // At most one pass per box: the arrows left form no loop.
        for (int pass = 0; pass < f.Nodes.Count; pass++)
        {
            bool changed = false;
            foreach (var e in forward)
                if (level[e.To] < level[e.From] + 1)
                {
                    level[e.To] = level[e.From] + 1;
                    changed = true;
                }
            if (!changed) break;
        }
        return level;
    }

    /// <summary>A path run the other way.</summary>
    static List<PathStep> Reverse(List<PathStep> path)
    {
        var pieces = new List<(PathStep Step, Pt From)>();
        var at = path[0].A;
        foreach (var step in path.Skip(1))
        {
            pieces.Add((step, at));
            at = step.End;
        }
        var reversed = new List<PathStep> { new(PathVerb.Move, at) };
        for (int i = pieces.Count - 1; i >= 0; i--)
        {
            var (step, from) = pieces[i];
            reversed.Add(step.Verb == PathVerb.Cubic ? new PathStep(PathVerb.Cubic, step.B, step.A, from) : new PathStep(PathVerb.Line, from));
        }
        return reversed;
    }

    static ICurve Curve(NodeShape shape, double w, double h)
    {
        var c = new MPoint();
        switch (shape)
        {
            case NodeShape.Circle or NodeShape.DoubleCircle:
                return CurveFactory.CreateCircle(w / 2, c);
            case NodeShape.Parallelogram or NodeShape.Trapezoid:
                double slant = SceneShapes.Slant(h);
                bool para = shape == NodeShape.Parallelogram;
                return new Polyline(
                    new MPoint(-w / 2 + slant, h / 2), new MPoint(para ? w / 2 : w / 2 - slant, h / 2),
                    new MPoint(para ? w / 2 - slant : w / 2, -h / 2), new MPoint(-w / 2, -h / 2)) { Closed = true };
            case NodeShape.Decision:
                return CurveFactory.CreateDiamond(w / 2, h / 2, c);
            case NodeShape.Hexagon:
                double inset = h / 4;
                return new Polyline(
                    new MPoint(-w / 2, 0), new MPoint(-w / 2 + inset, h / 2), new MPoint(w / 2 - inset, h / 2),
                    new MPoint(w / 2, 0), new MPoint(w / 2 - inset, -h / 2), new MPoint(-w / 2 + inset, -h / 2)) { Closed = true };
            default:
                double r = Radius(shape, h);
                return CurveFactory.CreateRectangleWithRoundedCorners(w, h, r, r, c);
        }
    }

    /// <summary>A rounded shape's corner radius: boxes 8, rounded boxes 14, a stadium's ends half its height.</summary>
    internal static double Radius(NodeShape shape, double h) => shape switch
    {
        NodeShape.Stadium => h / 2 - 0.5,
        NodeShape.Rounded => Math.Min(RoundedRadius, h / 2 - 0.5),
        NodeShape.Cylinder => 4,
        NodeShape.Circle or NodeShape.DoubleCircle => h / 2,
        _ => BoxRadius,
    };

    static void AddCurve(List<PathStep> path, ICurve curve)
    {
        switch (curve)
        {
            case Curve composite:
                foreach (var segment in composite.Segments) AddCurve(path, segment);
                break;
            case LineSegment line:
                path.Add(new PathStep(PathVerb.Line, new Pt(line.End.X, -line.End.Y)));
                break;
            case CubicBezierSegment b:
                path.Add(new PathStep(PathVerb.Cubic, new Pt(b.B(1).X, -b.B(1).Y), new Pt(b.B(2).X, -b.B(2).Y), new Pt(b.B(3).X, -b.B(3).Y)));
                break;
            case Polyline poly:
                foreach (var p in poly.Skip(1)) path.Add(new PathStep(PathVerb.Line, new Pt(p.X, -p.Y)));
                break;
            default:
                for (int k = 1; k <= 16; k++)
                {
                    var p = curve[curve.ParStart + (curve.ParEnd - curve.ParStart) * k / 16];
                    path.Add(new PathStep(PathVerb.Line, new Pt(p.X, -p.Y)));
                }
                break;
        }
    }

    /// <summary>The side of a box (right, top, bottom or left, as a unit vector) the fewest of its other arrows meet:
    /// where its loop goes.</summary>
    static Pt QuietSide(SceneNode n, IEnumerable<SceneEdge> others)
    {
        Pt[] sides = [new(1, 0), new(0, -1), new(0, 1), new(-1, 0)];
        var count = new int[4];
        foreach (var e in others)
        {
            foreach (var (id, at) in new[] { (e.From, e.StartTip), (e.To, e.Tip) })
            {
                if (id != n.Id) continue;
                var d = (at - n.Box.Center).Unit();
                int best = 0;
                for (int k = 1; k < 4; k++) if (d.X * sides[k].X + d.Y * sides[k].Y > d.X * sides[best].X + d.Y * sides[best].Y) best = k;
                count[best]++;
            }
        }
        int pick = 0;
        for (int k = 1; k < 4; k++) if (count[k] < count[pick]) pick = k;
        return sides[pick];
    }

    /// <summary>An arrow from a box back to itself: a small loop off its <paramref name="side"/> (its right, unless
    /// another side is quieter), its words just beyond it.</summary>
    static SceneEdge Loop(FlowEdge e, SceneNode n, Words label, Pt? side = null)
    {
        var o = side ?? new Pt(1, 0);
        var a = new Pt(-o.Y, o.X);
        var p = Along(n, o, 0);
        var start = p - a * 7;
        var tip = p + a * 7;
        var endBase = tip + o * 8 + a * 4;
        var path = new List<PathStep> { new(PathVerb.Move, start), new(PathVerb.Cubic, p + o * 30 - a * 26, p + o * 32 + a * 22, endBase) };
        var box = new Box();
        if (label.Lines.Count > 0)
        {
            double reach = 34 + (Math.Abs(o.X) * label.W + Math.Abs(o.Y) * label.H) / 2;
            box = Box.Around(p + o * reach, label.W, label.H);
        }
        return new SceneEdge(e.From, e.To, path, e.Line, EdgeEnd.None, e.EndEnd, start, start, e.EndEnd == EdgeEnd.None ? endBase : tip, endBase, label.Lines, box);
    }

    // --- outlines and the finished scene ---

    /// <summary>How far a point is outside a box's outline (negative inside), for its shape.</summary>
    internal static double Outline(SceneNode n, Pt p)
    {
        var b = n.Box;
        var c = b.Center;
        double hw = b.W / 2, hh = b.H / 2;
        switch (n.Shape)
        {
            case NodeShape.Decision:
                return Polygon(p, [new Pt(c.X, b.Y), new Pt(b.Right, c.Y), new Pt(c.X, b.Bottom), new Pt(b.X, c.Y)]);
            case NodeShape.Hexagon or NodeShape.Parallelogram or NodeShape.Trapezoid:
                return Polygon(p, SceneShapes.Corners(n)!);
            default:
                double r = n.Shape is NodeShape.Circle or NodeShape.DoubleCircle ? hw : Radius(n.Shape, b.H);
                double qx = Math.Abs(p.X - c.X) - (hw - r), qy = Math.Abs(p.Y - c.Y) - (hh - r);
                double outside = Math.Sqrt(Math.Max(qx, 0) * Math.Max(qx, 0) + Math.Max(qy, 0) * Math.Max(qy, 0));
                return outside + Math.Min(Math.Max(qx, qy), 0) - r;
        }
    }

    /// <summary>Signed distance to a convex polygon given clockwise on screen (y down).</summary>
    static double Polygon(Pt p, Pt[] corners)
    {
        double nearest = double.MaxValue;
        bool inside = true;
        for (int i = 0; i < corners.Length; i++)
        {
            Pt a = corners[i], b = corners[(i + 1) % corners.Length];
            var ab = b - a;
            var ap = p - a;
            double cross = ab.X * ap.Y - ab.Y * ap.X;
            if (cross < 0) inside = false;
            double t = Math.Clamp((ap.X * ab.X + ap.Y * ab.Y) / (ab.X * ab.X + ab.Y * ab.Y), 0, 1);
            nearest = Math.Min(nearest, Pt.Distance(p, a + ab * t));
        }
        return inside ? -nearest : nearest;
    }

    /// <summary>Moves the scene so everything drawn — boxes, groups, words, lines and their markers — starts a few
    /// pixels in from (0, 0), and sizes it to fit.</summary>
    static DiagramScene Normalise(DiagramScene s)
    {
        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        void Take(Box b)
        {
            minX = Math.Min(minX, b.X);
            minY = Math.Min(minY, b.Y);
            maxX = Math.Max(maxX, b.Right);
            maxY = Math.Max(maxY, b.Bottom);
        }
        void Point(Pt p, double r = 0) => Take(new Box(p.X - r, p.Y - r, 2 * r, 2 * r));
        foreach (var n in s.Nodes) Take(n.Box);
        foreach (var g in s.Groups) Take(g.Box);
        foreach (var e in s.Edges)
        {
            if (e.LabelBox.W > 0) Take(e.LabelBox);
            Point(e.Tip, 5);
            Point(e.StartTip, 5);
            var at = e.Path[0].A;
            foreach (var step in e.Path)
            {
                if (step.Verb == PathVerb.Cubic)
                    for (int k = 1; k <= 8; k++) Point(Bezier(at, step.A, step.B, step.C, k / 8.0), 1);
                else Point(step.A, 1);
                at = step.End;
            }
        }
        double dx = Margin - minX, dy = Margin - minY;
        Pt Move(Pt p) => new(p.X + dx, p.Y + dy);
        Box MoveBox(Box b) => b.W == 0 && b.H == 0 ? b : b.Offset(dx, dy);
        return s with
        {
            Width = Math.Ceiling(maxX - minX + 2 * Margin),
            Height = Math.Ceiling(maxY - minY + 2 * Margin),
            Nodes = s.Nodes.Select(n => n with { Box = n.Box.Offset(dx, dy) }).ToList(),
            Groups = s.Groups.Select(g => g with { Box = g.Box.Offset(dx, dy), TitleBox = g.TitleBox.Offset(dx, dy) }).ToList(),
            Edges = s.Edges.Select(e => e with
            {
                Path = e.Path.Select(p => new PathStep(p.Verb, Move(p.A), Move(p.B), Move(p.C))).ToList(),
                StartTip = Move(e.StartTip),
                StartBase = Move(e.StartBase),
                Tip = Move(e.Tip),
                Base = Move(e.Base),
                LabelBox = MoveBox(e.LabelBox),
            }).ToList(),
        };
    }
}
