using System.Xml;
using System.Xml.Linq;

namespace StudyStash.Core.Rich;

/// <summary>Where a part's callout is: its words' box, and the point on the part its leader line ends at.</summary>
public sealed record Callout(string Id, Bounds Words, (double X, double Y)? Anchor);

/// <summary>
/// An illustration: a detailed drawing of a thing a lecture described (a drone, a hand, a heart), drawn in SVG with
/// its parts named so a student can point at them. Its conventions: the drawing in groups, each part a group with an
/// id whose first children are a <c>title</c> (the part's name) and a <c>desc</c> (one line on it); the callouts in a
/// group <c>labels</c>, one group <c>label-{id}</c> each, holding a leader line from the part out to its words.
/// Here: naming the parts in a drawing as the designer planned them, the drawing with its callouts or without them
/// (or with only their lines), one part or one callout alone, and where each callout and part is.
/// </summary>
public static class Illustration
{
    static readonly XNamespace Ns = "http://www.w3.org/2000/svg";

    /// <summary>What a drawing names as a part: one it's told about (its id, name and line).</summary>
    public sealed record Planned(string Id, string Name, string Note);

    /// <summary>The kinds of element a drawing defines for others to use: kept whenever any of it is drawn.</summary>
    static readonly HashSet<string> Resources = ["defs", "linearGradient", "radialGradient", "marker", "clipPath", "symbol"];

    /// <summary>The label group's id for a part.</summary>
    public static string LabelId(string part) => "label-" + part;

    static XDocument? Parse(string svg)
    {
        try
        {
            var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, IgnoreComments = true, IgnoreProcessingInstructions = true, MaxCharactersInDocument = 800_000 };
            using var reader = XmlReader.Create(new StringReader(svg), settings);
            return XDocument.Load(reader, LoadOptions.PreserveWhitespace);
        }
        catch (XmlException)
        {
            return null;
        }
    }

    static string Write(XDocument doc) => doc.Root!.ToString(SaveOptions.DisableFormatting);

    static XName Name(XElement root, string local) => root.Name.Namespace + local;

    /// <summary>
    /// The drawing with each planned part named in it: its element (by id) gets the part's name as its first child, a
    /// title, and its line as a desc after it (replacing any it wrote itself). A part with no element of its id is left
    /// out. The drawing as given when it can't be read.
    /// </summary>
    public static string Name(string svg, IReadOnlyList<Planned> parts)
    {
        if (Parse(svg) is not { Root: { } root } doc) return svg;
        var byId = root.Descendants().Where(e => (string?)e.Attribute("id") is { Length: > 0 }).GroupBy(e => (string)e.Attribute("id")!).ToDictionary(g => g.Key, g => g.First());
        foreach (var p in parts)
        {
            if (!byId.TryGetValue(p.Id, out var e) || ReferenceEquals(e, root) || e.Name.LocalName is "title" or "desc" or "use" || Resources.Contains(e.Name.LocalName)) continue;
            foreach (var old in e.Elements().Where(c => c.Name.LocalName is "title" or "desc").ToList()) old.Remove();
            var named = new List<XElement> { new(Name(root, "title"), p.Name) };
            if (p.Note.Length > 0) named.Add(new XElement(Name(root, "desc"), p.Note));
            e.AddFirst(named);
        }
        return Write(doc);
    }

    /// <summary>The drawing without its callouts.</summary>
    public static string Bare(string svg) => Edit(svg, root =>
    {
        foreach (var e in root.Descendants().Where(IsCallout).ToList()) e.Remove();
    });

    /// <summary>The drawing with its callouts' lines but none of their words (for testing yourself).</summary>
    public static string Leaders(string svg) => Edit(svg, root =>
    {
        foreach (var t in root.Descendants().Where(IsCallout).SelectMany(c => c.Descendants()).Where(e => e.Name.LocalName == "text").Distinct().ToList()) t.Remove();
    });

    /// <summary>One part alone (and whatever it holds), where the whole drawing has it, without any callout.</summary>
    public static string Part(string svg, string id) => Only(svg, e => (string?)e.Attribute("id") == id && !IsCallout(e), callouts: false);

    /// <summary>One part's callout alone.</summary>
    public static string CalloutOf(string svg, string id) => Only(svg, e => (string?)e.Attribute("id") == LabelId(id), callouts: true);

    static bool IsCallout(XElement e) => (string?)e.Attribute("id") is { } id && (id == "labels" || id.StartsWith("label-", StringComparison.Ordinal));

    static string Edit(string svg, Action<XElement> edit)
    {
        if (Parse(svg) is not { Root: { } root } doc) return svg;
        edit(root);
        return Write(doc);
    }

    /// <summary>The drawing with only the element <paramref name="keep"/> picks (with all it holds, and the groups it
    /// sits in, so it's drawn exactly where and as it is in the whole), and everything drawings define for others to
    /// use (gradients, markers, clips, symbols), gathered into defs.</summary>
    static string Only(string svg, Func<XElement, bool> keep, bool callouts)
    {
        if (Parse(svg) is not { Root: { } root } doc) return svg;
        var target = root.Descendants().FirstOrDefault(keep);
        var resources = root.Descendants().Where(e => Resources.Contains(e.Name.LocalName) && e.Ancestors().All(a => !Resources.Contains(a.Name.LocalName))).ToList();
        var defs = new XElement(Name(root, "defs"));
        foreach (var r in resources)
        {
            if (target is not null && (ReferenceEquals(r, target) || r.Ancestors().Contains(target))) continue;
            if (r.Name.LocalName == "defs") defs.Add(r.Elements());
            else defs.Add(new XElement(r));
        }
        var path = target is null ? [] : target.AncestorsAndSelf().ToHashSet();
        void Prune(XElement e)
        {
            foreach (var child in e.Elements().ToList())
            {
                if (ReferenceEquals(child, target)) continue;
                if (path.Contains(child)) Prune(child);
                else child.Remove();
            }
        }
        Prune(root);
        root.AddFirst(defs);
        // A part shown alone never brings its callout: one shown alone brings only itself.
        if (!callouts && target is not null)
            foreach (var c in target.Descendants().Where(IsCallout).ToList()) c.Remove();
        return Write(doc);
    }

    /// <summary>Where each named part's callout is: its words' box, and the end of its leader line farthest from
    /// them (on the part).</summary>
    public static IReadOnlyList<Callout> Callouts(string svg)
    {
        if (Parse(svg) is not { Root: { } root }) return [];
        return Callouts(root, SvgGeometry.Of(root));
    }

    internal static IReadOnlyList<Callout> Callouts(XElement root, SvgGeometry.Measure measure)
    {
        var found = new List<Callout>();
        foreach (var g in root.Descendants().Where(e => ((string?)e.Attribute("id"))?.StartsWith("label-", StringComparison.Ordinal) == true))
        {
            string id = ((string)g.Attribute("id")!)["label-".Length..];
            var words = measure.Texts.Where(t => t.Text.AncestorsAndSelf().Contains(g)).Select(t => t.Box).ToList();
            if (words.Count == 0) continue;
            var box = words.Aggregate((a, b) => a.Union(b));
            (double X, double Y)? anchor = null;
            double far = -1;
            foreach (var (_, points) in measure.Shapes.Where(s => s.Shape.Ancestors().Contains(g)))
                foreach (var p in points)
                {
                    double d = box.Distance(p.X, p.Y);
                    if (d > far)
                    {
                        far = d;
                        anchor = p;
                    }
                }
            found.Add(new Callout(id, box, anchor));
        }
        return found;
    }

    /// <summary>Where each part is drawn: its box, by id (callouts not counted).</summary>
    public static IReadOnlyDictionary<string, Bounds> Places(string svg)
    {
        if (Parse(svg) is not { Root: { } root }) return new Dictionary<string, Bounds>();
        return Places(root, SvgGeometry.Of(root));
    }

    internal static Dictionary<string, Bounds> Places(XElement root, SvgGeometry.Measure measure)
    {
        var places = new Dictionary<string, Bounds>();
        foreach (var e in root.Descendants())
            if ((string?)e.Attribute("id") is { Length: > 0 } id && !IsCallout(e) && measure.Boxes.TryGetValue(e, out var b)) places.TryAdd(id, b);
        return places;
    }
}
