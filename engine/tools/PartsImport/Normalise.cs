using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using StudyStash.Core;
using StudyStash.Core.Rich;

namespace PartsImport;

/// <summary>A part SafeSvg wouldn't show: it never goes in the library.</summary>
sealed class Refused(string why) : Exception(why);

/// <summary>A region of a part, as written in the manifest: which of its shapes (by where they sit, as fractions of the
/// part's box, and/or by their place in the drawing order) make the named piece.</summary>
sealed record RegionSpec(string Id, string Name, string Tags, double[]? Rect, string? Leaves, double[]? Anchor, string? Colour = null, string? Group = null);

sealed record PortSpec(string Id, double X, double Y);

/// <summary>One part, ready for the library: its drawing as a symbol (cropped, recoloured, rounded, its ids its own,
/// each region a group <c>r-{id}</c>), with where each region's leader should point.</summary>
sealed record BuiltPart(XElement Symbol, Bounds Box, IReadOnlyList<(RegionSpec Spec, double X, double Y)> Regions, int Shapes, int Bytes, IReadOnlyList<string> Notes)
{
    public (double X, double Y) Anchor { get; init; }
}

static partial class Normalise
{
    /// <summary>The last part's shapes' boxes, in drawing order, before regions (for <c>leaves</c>, to write regions by).</summary>
    public static List<Bounds> LastLeaves { get; private set; } = [];
    public static Bounds LastBox { get; private set; }

    static readonly XNamespace Ns = "http://www.w3.org/2000/svg";

    static readonly HashSet<string> Resources = ["linearGradient", "radialGradient", "clipPath", "symbol", "marker"];

    static readonly string[] Inherited =
    [
        "fill", "fill-opacity", "fill-rule", "stroke", "stroke-width", "stroke-opacity", "stroke-linecap", "stroke-linejoin",
        "stroke-miterlimit", "stroke-dasharray", "stroke-dashoffset", "font-size", "font-weight", "font-style", "text-anchor",
        "letter-spacing", "clip-rule", "visibility",
    ];

    public static BuiltPart Build(string id, string source, IReadOnlyList<RegionSpec> regions, double[]? crop = null, string? drop = null,
        bool recolour = true, bool dropText = false, double simplify = 0, bool groups = false, string? dropStroke = null)
    {
        if (simplify > 0)
        {
            // Heavy traced art is made light before anything else (SafeSvg won't read over 192 KB).
            var raw = XElement.Parse(source);
            var vb = ((string?)raw.Attribute("viewBox") ?? "0 0 1000 1000").Split([' ', ','], StringSplitOptions.RemoveEmptyEntries).Select(v => double.Parse(v, CultureInfo.InvariantCulture)).ToArray();
            Simplify.All(raw.Descendants(), simplify * Math.Max(vb[2], vb[3]) / 1000);
            source = raw.ToString(SaveOptions.DisableFormatting);
        }
        var safe = SafeSvg.Clean(source);
        if (safe.Svg is null) throw new Refused(safe.Problem ?? "SafeSvg refused it");
        var root = XElement.Parse(safe.Svg);
        var notes = new List<string>();

        var resources = new List<XElement>();
        var leaves = new List<XElement>();
        keepGroups = groups;
        Flatten(root, new Dictionary<string, string>(), null, resources, leaves);
        if (dropText) leaves.RemoveAll(l => l.Name.LocalName == "text" || l.Descendants(Ns + "text").Any());
        // A labelled source's own leader lines (picked by their colour) go with its words.
        if (dropStroke is not null)
            leaves.RemoveAll(l => (string?)l.Attribute("fill") == "none" && SafeSvg.NormalColour((string?)l.Attribute("stroke") ?? "") is { } st && House.Close(dropStroke, st));
        foreach (var l in leaves) foreach (var t in l.DescendantsAndSelf().Where(e => e.Name.LocalName is "title" or "desc").ToList()) t.Remove();

        // Where everything is, from the markup.
        var view = Measure(root, resources, leaves, out var boxes);
        if (drop is not null)
            foreach (int i in Indices(drop, leaves.Count).OrderDescending()) { leaves.RemoveAt(i); boxes.RemoveAt(i); }
        // Anything wholly outside the file's own canvas was never seen.
        for (int i = leaves.Count - 1; i >= 0; i--)
            if (boxes[i] is not { } b || b.Overlap(view) <= 0 && b.W * b.H > 0) { leaves.RemoveAt(i); boxes.RemoveAt(i); }
        if (leaves.Count == 0) throw new Refused("nothing left to draw");
        var box = boxes.Select(b => b!.Value).Aggregate((a, b) => a.Union(b));
        box = Clip(box, view);
        if (crop is { Length: 4 })
        {
            box = new Bounds(box.X + crop[0] * box.W, box.Y + crop[1] * box.H, crop[2] * box.W, crop[3] * box.H);
            for (int i = leaves.Count - 1; i >= 0; i--)
                if (boxes[i]!.Value.Overlap(box) <= 0) { leaves.RemoveAt(i); boxes.RemoveAt(i); }
        }
        box = box.Inflate(Math.Max(box.W, box.H) * 0.01);

        LastLeaves = boxes.Select(b => b!.Value).ToList();
        LastBox = box;

        if (recolour)
            foreach (var e in resources.Concat(leaves).SelectMany(x => x.DescendantsAndSelf()))
                foreach (string attr in new[] { "fill", "stroke", "stop-color" })
                    if ((string?)e.Attribute(attr) is { } v && SafeSvg.NormalColour(v) is { } hex && hex.StartsWith('#'))
                        e.SetAttributeValue(attr, House.Nearest(hex));

        // Regions: their shapes gathered into one group each, without changing what's drawn over what.
        var placed = new List<(RegionSpec, double, double)>();
        var taken = new HashSet<XElement>();
        masks = regions.Count > 0 ? Masks(leaves, box) : [];
        foreach (var r in regions)
        {
            var picked = Enumerable.Range(0, leaves.Count).Where(i => !taken.Contains(leaves[i]) && Picks(r, leaves[i], boxes[i]!.Value, i, box, leaves.Count)).ToList();
            if (picked.Count == 0) { notes.Add($"region {r.Id}: nothing picked"); continue; }
            takenNow = taken;
            var run = Gather(leaves, boxes, picked, out var left);
            if (left.Count > 0) notes.Add($"region {r.Id}: {left.Count} shape(s) couldn't join without changing the drawing");
            var group = new XElement(Ns + "g", new XAttribute("id", "r-" + r.Id));
            int first = leaves.IndexOf(run[0]);
            foreach (var l in run) taken.Add(l);
            var runBoxes = run.Select(l => boxes[leaves.IndexOf(l)]!.Value).ToList();
            foreach (var l in run) { int k = leaves.IndexOf(l); leaves.RemoveAt(k); boxes.RemoveAt(k); }
            if (masks is not null && run.All(masks.ContainsKey))
                masks[group] = run.Select(l => (System.Collections.BitArray)masks[l].Clone()).Aggregate((a, b) => a.Or(b));
            group.Add(run);
            leaves.Insert(first, group);
            var gbox = runBoxes.Aggregate((a, b) => a.Union(b));
            boxes.Insert(first, gbox);
            taken.Add(group);
            var (ax, ay) = r.Anchor is { Length: 2 } a ? (box.X + a[0] * box.W, box.Y + a[1] * box.H) : Inside(group, gbox, resources);
            placed.Add((r, ax, ay));
        }

        var middle = Inside(new XElement(Ns + "g", leaves.Select(l => new XElement(l))), box.Inflate(-Math.Max(box.W, box.H) * 0.01), resources);

        // Numbers rounded to what shows; ids made the part's own.
        int decimals = Math.Clamp((int)Math.Ceiling(-Math.Log10(Math.Max(box.W, box.H) / 1500)), 0, 3);
        foreach (var e in resources.Concat(leaves).SelectMany(x => x.DescendantsAndSelf())) Round(e, decimals);
        var symbol = new XElement(Ns + "symbol", new XAttribute("viewBox", $"{R(box.X, decimals)} {R(box.Y, decimals)} {R(box.W, decimals)} {R(box.H, decimals)}"));
        var used = Used(leaves, resources);
        var keep = resources.Where(r => used.Contains((string?)r.Attribute("id") ?? "")).ToList();
        if (keep.Count > 0) symbol.Add(new XElement(Ns + "defs", keep));
        symbol.Add(leaves);
        Own(symbol, id);
        int shapes = symbol.Descendants().Count(e => e.Name.LocalName is "path" or "rect" or "circle" or "ellipse" or "line" or "polyline" or "polygon" or "text" or "use");
        int bytes = symbol.ToString(SaveOptions.DisableFormatting).Length;
        return new BuiltPart(symbol, box, placed.Select(p => (p.Item1, Math.Round(p.Item2, decimals), Math.Round(p.Item3, decimals))).ToList(), shapes, bytes, notes)
        {
            Anchor = (Math.Round(middle.X, decimals), Math.Round(middle.Y, decimals)),
        };
    }

    static Bounds Clip(Bounds b, Bounds view)
    {
        if (view.W <= 0) return b;
        double x0 = Math.Max(b.X, view.X), y0 = Math.Max(b.Y, view.Y), x1 = Math.Min(b.Right, view.Right), y1 = Math.Min(b.Bottom, view.Bottom);
        return x1 > x0 && y1 > y0 ? new Bounds(x0, y0, x1 - x0, y1 - y0) : b;
    }

    /// <summary>The drawing's elements in the order drawn, groups opened where opening them changes nothing (a group's
    /// transform and inherited look pushed onto its children); a group with its own clip or opacity stays whole.
    /// Gradients, clips and symbols go to <paramref name="resources"/>.</summary>
    [ThreadStatic] static bool keepGroups;

    static void Flatten(XElement parent, Dictionary<string, string> inherited, string? transform, List<XElement> resources, List<XElement> leaves)
    {
        foreach (var child in parent.Elements())
        {
            string name = child.Name.LocalName;
            if (name == "defs")
            {
                // What a file defines for use elsewhere (a gradient, a clip, a tile a <use> copies) is kept whole.
                foreach (var d in child.Elements().Where(d => Resources.Contains(d.Name.LocalName) || (string?)d.Attribute("id") is { Length: > 0 }))
                    resources.Add(new XElement(d));
                continue;
            }
            if (Resources.Contains(name)) { resources.Add(new XElement(child)); continue; }
            if (name is "title" or "desc" or "marker") continue;
            if ((string?)child.Attribute("display") == "none" || (string?)child.Attribute("visibility") == "hidden") continue;
            string? t = Join(transform, (string?)child.Attribute("transform"));
            bool whole = child.Attribute("clip-path") is not null || ((string?)child.Attribute("opacity") is { } o && o.Trim() is not ("1" or "1.0"))
                || keepGroups && (string?)child.Attribute("id") is { Length: > 0 } gid && gid != "art";
            if (name == "g" && !whole)
            {
                var inh = new Dictionary<string, string>(inherited);
                foreach (string a in Inherited)
                    if ((string?)child.Attribute(a) is { } v) inh[a] = v;
                Flatten(child, inh, t, resources, leaves);
                continue;
            }
            var leaf = new XElement(child);
            leaf.SetAttributeValue("id", name == "use" ? null : leaf.Attribute("id")?.Value);
            foreach (var (a, v) in inherited)
                if (leaf.Attribute(a) is null) leaf.SetAttributeValue(a, v);
            leaf.SetAttributeValue("transform", t);
            leaves.Add(leaf);
        }
    }

    static string? Join(string? outer, string? inner) => outer is null ? inner : inner is null ? outer : outer + " " + inner;

    static Bounds Measure(XElement original, List<XElement> resources, List<XElement> leaves, out List<Bounds?> boxes)
    {
        var temp = new XElement(Ns + "svg", original.Attribute("viewBox"), new XElement(Ns + "defs", resources.Select(r => new XElement(r))), leaves);
        var m = SvgGeometry.Of(temp);
        var list = new List<Bounds?>();
        foreach (var l in leaves) list.Add(m.Boxes.TryGetValue(l, out var b) ? b : null);
        // The measured copies are the leaves themselves (re-parented): take them back out.
        foreach (var l in leaves) l.Remove();
        boxes = list;
        return m.View;
    }

    static IEnumerable<int> Indices(string spec, int count)
    {
        foreach (string part in spec.Split([',', ' '], StringSplitOptions.RemoveEmptyEntries))
        {
            var ends = part.Split('-');
            int a = int.Parse(ends[0], CultureInfo.InvariantCulture), b = ends.Length > 1 ? int.Parse(ends[1], CultureInfo.InvariantCulture) : a;
            for (int i = a; i <= b && i < count; i++) yield return i;
        }
    }

    static bool Picks(RegionSpec r, XElement leaf, Bounds b, int index, Bounds box, int count)
    {
        bool any = false;
        if (r.Leaves is not null)
        {
            if (!Indices(r.Leaves, count).Contains(index)) return false;
            any = true;
        }
        if (r.Group is not null)
        {
            if ((string?)leaf.Attribute("id") != r.Group) return false;
            any = true;
        }
        if (r.Rect is { Length: >= 4 } q)
        {
            // One or more boxes, as fractions of the part's: a shape whose middle is in any of them.
            bool inside = false;
            for (int k = 0; k + 3 < q.Length; k += 4)
                if (new Bounds(box.X + q[k] * box.W, box.Y + q[k + 1] * box.H, q[k + 2] * box.W, q[k + 3] * box.H).Contains(b.CenterX, b.CenterY)) inside = true;
            if (!inside) return false;
            any = true;
        }
        if (r.Colour is not null)
        {
            string[] want = r.Colour.Split(',', StringSplitOptions.TrimEntries);
            string? fill = SafeSvg.NormalColour((string?)leaf.Attribute("fill") ?? "");
            if (fill is null || !want.Any(w => House.Close(w, fill))) return false;
            any = true;
        }
        return any;
    }

    const int Grid = 160;

    [ThreadStatic] static Dictionary<XElement, System.Collections.BitArray>? masks;

    /// <summary>Where each shape paints, coarsely: the cells of a grid over the part that its outline encloses or
    /// passes through. Two shapes whose cells don't meet can swap places in the drawing order without anything
    /// changing on screen.</summary>
    static Dictionary<XElement, System.Collections.BitArray> Masks(List<XElement> leaves, Bounds box)
    {
        var temp = new XElement(Ns + "svg", new XAttribute("viewBox", "0 0 1 1"), leaves.Select(l => new XElement(l)));
        var copies = temp.Elements().ToList();
        var m = SvgGeometry.Of(temp);
        var result = new Dictionary<XElement, System.Collections.BitArray>();
        double cw = box.W / Grid, ch = box.H / Grid;
        for (int i = 0; i < leaves.Count; i++)
        {
            var bits = new System.Collections.BitArray(Grid * Grid);
            foreach (var (shape, pts) in m.Shapes.Where(sh => sh.Shape.AncestorsAndSelf().Contains(copies[i])))
            {
                if (pts.Count == 0) continue;
                bool filled = (string?)shape.AncestorsAndSelf().Select(a => a.Attribute("fill")).FirstOrDefault(a => a is not null) != "none" && pts.Count >= 3;
                var b = Bounds.Around(pts)!.Value;
                int x0 = Math.Clamp((int)((b.X - box.X) / cw) - 1, 0, Grid - 1), x1 = Math.Clamp((int)((b.Right - box.X) / cw) + 1, 0, Grid - 1);
                int y0 = Math.Clamp((int)((b.Y - box.Y) / ch) - 1, 0, Grid - 1), y1 = Math.Clamp((int)((b.Bottom - box.Y) / ch) + 1, 0, Grid - 1);
                if (filled)
                    for (int gy = y0; gy <= y1; gy++)
                        for (int gx = x0; gx <= x1; gx++)
                            if (In(pts, box.X + (gx + 0.5) * cw, box.Y + (gy + 0.5) * ch)) bits[gy * Grid + gx] = true;
                // The outline itself (a stroke, or a thin shape).
                for (int k = 0; k < pts.Count; k++)
                {
                    var (ax, ay) = pts[k];
                    var (bx, by) = pts[(k + 1) % pts.Count];
                    int steps = (int)Math.Ceiling(Math.Max(Math.Abs(bx - ax) / cw, Math.Abs(by - ay) / ch)) + 1;
                    for (int t = 0; t <= steps; t++)
                    {
                        double x = ax + (bx - ax) * t / steps, y = ay + (by - ay) * t / steps;
                        int gx = (int)((x - box.X) / cw), gy = (int)((y - box.Y) / ch);
                        if (gx >= 0 && gx < Grid && gy >= 0 && gy < Grid) bits[gy * Grid + gx] = true;
                    }
                }
            }
            result[leaves[i]] = bits;
        }
        return result;
    }

    static bool Meet(XElement a, XElement b)
    {
        if (masks is null || !masks.TryGetValue(a, out var ma) || !masks.TryGetValue(b, out var mb)) return true;
        int both = 0;
        for (int i = 0; i < ma.Length; i++)
            if (ma[i] && mb[i] && ++both > 2) return true;
        return false;
    }

    /// <summary>The picked shapes as one run in the drawing order: each later one brought down to the run when it
    /// overlaps nothing it passes (so nothing changes on screen), or else the run taken up to it when the run overlaps
    /// nothing it passes. A shape that can do neither is left out (<paramref name="left"/>).</summary>
    [ThreadStatic] static HashSet<XElement>? takenNow;

    static bool Taken(XElement x) => takenNow?.Contains(x) == true || x.Name.LocalName == "g" && ((string?)x.Attribute("id"))?.StartsWith("r-", StringComparison.Ordinal) == true;

    static List<XElement> Gather(List<XElement> leaves, List<Bounds?> boxes, List<int> picked, out List<XElement> left)
    {
        left = [];
        var order = leaves.ToList();
        var bx = order.Select((l, i) => (l, boxes[i])).ToDictionary(p => p.l, p => p.Item2!.Value);
        var run = new List<XElement> { order[picked[0]] };
        var area = picked.Select(i => bx[leaves[i]]).Aggregate((a, b) => a.Union(b));
        area = area.Inflate(Math.Max(area.W, area.H) * 0.05);
        foreach (int p in picked.Skip(1))
        {
            var e = leaves[p];
            int end = order.IndexOf(run[^1]), at = order.IndexOf(e);
            var between = order.Skip(end + 1).Take(at - end - 1).ToList();
            // Small shapes drawn between two of the region's, inside its area, are the region's detail: they join it
            // where they are, and nothing moves.
            if (between.Count > 0 && between.All(x => !Taken(x) && area.Contains(bx[x].X, bx[x].Y) && area.Contains(bx[x].Right, bx[x].Bottom)))
            {
                run.AddRange(between);
                run.Add(e);
                continue;
            }
            if (between.All(x => bx[x].Inflate(-0.01).Overlap(bx[e]) <= 0 || !Meet(x, e)))
            {
                order.Remove(e);
                order.Insert(order.IndexOf(run[^1]) + 1, e);
                run.Add(e);
            }
            else if (between.All(x => run.All(r => bx[x].Inflate(-0.01).Overlap(bx[r]) <= 0 || !Meet(x, r))))
            {
                foreach (var r in run) order.Remove(r);
                int k = order.IndexOf(e);
                order.InsertRange(k, run);
                run.Add(e);
            }
            else left.Add(e);
        }
        // The new order, written back.
        leaves.Clear();
        leaves.AddRange(order);
        var newBoxes = order.Select(l => (Bounds?)bx[l]).ToList();
        boxes.Clear();
        boxes.AddRange(newBoxes);
        return run;
    }

    /// <summary>A point well inside a region's shapes (where its leader should end): of points on a grid over its box,
    /// the one inside a filled shape farthest from the box's edge, or else the middle of the biggest shape.</summary>
    static (double X, double Y) Inside(XElement group, Bounds box, List<XElement> resources)
    {
        var temp = new XElement(Ns + "svg", new XAttribute("viewBox", "0 0 1 1"), new XElement(group));
        var m = SvgGeometry.Of(temp);
        var polys = m.Shapes.Where(s => (string?)s.Shape.Attribute("fill") != "none" && s.Points.Count >= 3).Select(s => s.Points).ToList();
        (double, double)? best = null;
        double score = -1;
        for (int i = 1; i < 12; i++)
            for (int j = 1; j < 12; j++)
            {
                double x = box.X + box.W * i / 12, y = box.Y + box.H * j / 12;
                if (!polys.Any(p => In(p, x, y))) continue;
                double s = Math.Min(Math.Min(x - box.X, box.Right - x) / box.W, Math.Min(y - box.Y, box.Bottom - y) / box.H);
                if (s > score) { score = s; best = (x, y); }
            }
        return best ?? (box.CenterX, box.CenterY);
    }

    static bool In(IReadOnlyList<(double X, double Y)> poly, double x, double y)
    {
        bool inside = false;
        for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
            if ((poly[i].Y > y) != (poly[j].Y > y) && x < (poly[j].X - poly[i].X) * (y - poly[i].Y) / (poly[j].Y - poly[i].Y) + poly[i].X)
                inside = !inside;
        return inside;
    }

    [GeneratedRegex(@"-?(?:\d+\.?\d*|\.\d+)(?:[eE][-+]?\d+)?")]
    private static partial Regex Number();

    static readonly HashSet<string> Numeric =
    [
        "d", "points", "x", "y", "x1", "y1", "x2", "y2", "cx", "cy", "r", "rx", "ry", "fx", "fy", "width", "height", "transform",
        "stroke-width", "gradientTransform", "font-size", "stroke-dasharray", "stroke-dashoffset",
    ];

    static void Round(XElement e, int decimals)
    {
        // A gradient in its box's own units (0 to 1) keeps three places.
        bool unit = e.Name.LocalName is "linearGradient" or "radialGradient" && (string?)e.Attribute("gradientUnits") != "userSpaceOnUse";
        foreach (var a in e.Attributes().ToList())
        {
            string n = a.Name.LocalName;
            if (!Numeric.Contains(n) || a.Name.Namespace != XNamespace.None) continue;
            int places = unit && n is "x1" or "y1" or "x2" or "y2" or "cx" or "cy" or "r" or "fx" or "fy" ? 3 : n is "transform" or "gradientTransform" ? decimals + 3 : decimals;
            string v = a.Value;
            // A number that loses its point must not run into a next one written ".5".
            a.Value = Number().Replace(v, m =>
            {
                string r = R(double.Parse(m.Value, CultureInfo.InvariantCulture), places);
                int after = m.Index + m.Length;
                return after < v.Length && v[after] == '.' && !r.Contains('.') ? r + " " : r;
            });
        }
    }

    static string R(double d, int places)
    {
        string s = Math.Round(d, places).ToString("0." + new string('#', Math.Max(1, places)), CultureInfo.InvariantCulture);
        if (s.StartsWith("0.", StringComparison.Ordinal)) s = s[1..];
        else if (s.StartsWith("-0.", StringComparison.Ordinal)) s = "-" + s[2..];
        return s == "-0" ? "0" : s;
    }

    static HashSet<string> Used(IEnumerable<XElement> leaves, IEnumerable<XElement> resources)
    {
        var used = new HashSet<string>();
        var byId = resources.Where(r => (string?)r.Attribute("id") is { Length: > 0 }).ToDictionary(r => (string)r.Attribute("id")!, r => r);
        var todo = new Stack<XElement>(leaves);
        while (todo.Count > 0)
        {
            foreach (var e in todo.Pop().DescendantsAndSelf())
                foreach (var a in e.Attributes())
                    foreach (Match m in Ref().Matches(a.Value))
                    {
                        string id = m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value;
                        if (used.Add(id) && byId.TryGetValue(id, out var r)) todo.Push(r);
                    }
        }
        return used;
    }

    [GeneratedRegex(@"url\(\s*['""]?#([^'"")\s]+)['""]?\s*\)|^#([A-Za-z_][\w\-.:]*)$")]
    private static partial Regex Ref();

    /// <summary>Every id in the part made its own (<c>{part}--{id}</c>), and every reference to one; a region's group
    /// keeps its plain <c>r-</c> id, which the composer renames.</summary>
    static void Own(XElement symbol, string part)
    {
        var map = new Dictionary<string, string>();
        foreach (var e in symbol.Descendants())
            if ((string?)e.Attribute("id") is { Length: > 0 } id && !id.StartsWith("r-", StringComparison.Ordinal))
                e.SetAttributeValue("id", map[id] = $"{part}--{id}");
        foreach (var e in symbol.Descendants())
            foreach (var a in e.Attributes().Where(a => a.Name.LocalName != "id").ToList())
                if (a.Value.Contains('#'))
                    a.Value = Ref().Replace(a.Value, m =>
                    {
                        string id = m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value;
                        return map.TryGetValue(id, out var own) ? (m.Groups[1].Success ? $"url(#{own})" : "#" + own) : m.Value;
                    });
    }
}

/// <summary>The house palette (the illustrator's, by material) as ramps from light to outline, and every colour moved
/// to the nearest point on the nearest ramp, in OKLab: a part from any source comes out in the same few materials,
/// its light and shade kept.</summary>
static class House
{
    static readonly List<(double L, double A, double B)[]> Ramps = IllustrationDesign.Palette
        .Select(p => new[] { p.Light, p.Base, p.Shade, p.Line }.Select(h => SvgColour.Lab(h.ToLowerInvariant())!.Value).ToArray()).ToList();

    static readonly Dictionary<string, string> Seen = [];

    public static string Nearest(string hex)
    {
        if (Seen.TryGetValue(hex, out var done)) return done;
        if (SvgColour.Lab(hex) is not { } c) return hex;
        double best = double.MaxValue;
        (double L, double A, double B) point = c;
        foreach (var ramp in Ramps)
        {
            // Beyond either end the ramp goes on, a little, the way it was going: a black stays darker than an outline.
            var stops = new List<(double L, double A, double B)> { Extend(ramp[1], ramp[0], 0.6) };
            stops.AddRange(ramp);
            stops.Add(Extend(ramp[2], ramp[3], 0.8));
            for (int i = 0; i + 1 < stops.Count; i++)
            {
                var (p, d) = Project(c, stops[i], stops[i + 1]);
                if (d < best) { best = d; point = p; }
            }
        }
        return Seen[hex] = SvgColour.Hex(Math.Clamp(point.L, 0.18, 0.985), point.A, point.B);
    }

    public static bool Close(string a, string b) =>
        SvgColour.Lab(SafeSvg.NormalColour(a) ?? a) is { } x && SvgColour.Lab(b) is { } y && Math.Sqrt(Sq(x.L - y.L) + Sq(x.A - y.A) + Sq(x.B - y.B)) < 0.04;

    static (double, double, double) Extend((double L, double A, double B) from, (double L, double A, double B) to, double k) =>
        (to.L + (to.L - from.L) * k, to.A + (to.A - from.A) * k, to.B + (to.B - from.B) * k);

    static ((double L, double A, double B), double) Project((double L, double A, double B) c, (double L, double A, double B) p, (double L, double A, double B) q)
    {
        double dl = q.L - p.L, da = q.A - p.A, db = q.B - p.B;
        double len = dl * dl + da * da + db * db;
        double t = len == 0 ? 0 : Math.Clamp(((c.L - p.L) * dl + (c.A - p.A) * da + (c.B - p.B) * db) / len, 0, 1);
        var at = (p.L + t * dl, p.A + t * da, p.B + t * db);
        // Hue matters more than lightness in choosing the material.
        double d = Sq(c.L - at.Item1) * 0.5 + Sq(c.A - at.Item2) + Sq(c.B - at.Item3);
        return (at, d);
    }

    static double Sq(double x) => x * x;
}
