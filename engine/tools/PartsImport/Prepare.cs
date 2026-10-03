using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace PartsImport;

/// <summary>
/// A downloaded SVG made plain enough for <see cref="StudyStash.Core.Rich.SafeSvg"/> to judge: no XML declaration,
/// document type or editor metadata; Illustrator's <c>switch</c> wrappers opened; CSS classes written onto each
/// element as a style attribute; pattern fills tiled as real shapes (copies of one tile). Nothing here makes anything
/// safe: SafeSvg does that, afterwards, and refuses what it must.
/// </summary>
static partial class Prepare
{
    static readonly XNamespace Svg = "http://www.w3.org/2000/svg";
    static readonly XNamespace XLink = "http://www.w3.org/1999/xlink";

    [GeneratedRegex(@"<\?xml[^>]*\?>")]
    private static partial Regex Declaration();

    [GeneratedRegex(@"<!DOCTYPE[^\[>]*(\[[\s\S]*?\])?\s*>")]
    private static partial Regex DocType();

    [GeneratedRegex(@"<!--[\s\S]*?-->")]
    private static partial Regex Comment();

    [GeneratedRegex(@"&ns_[a-z_]+;")]
    private static partial Regex AdobeEntity();

    // What Lit leaves of an event handler or a property binding once its value is gone: @click="", .value="", ?hidden="".
    [GeneratedRegex(@"\s[@.?][\w-]+=(""[^""]*""|'[^']*'|[^\s>]*)")]
    private static partial Regex LitBinding();

    [GeneratedRegex(@"(\s[\w:-]+)=([^\s""'>]+)")]
    private static partial Regex Unquoted();

    public static string Text(string svg)
    {
        string s = svg;
        // A template's drawing may sit among other markup (a button, a label): the drawing is its outermost svg.
        int start = s.IndexOf("<svg", StringComparison.Ordinal), end = s.LastIndexOf("</svg>", StringComparison.Ordinal);
        if (start > 0 && end > start && !s[..start].TrimStart().StartsWith("<?xml", StringComparison.Ordinal) && !s[..start].Contains("<!DOCTYPE", StringComparison.Ordinal))
            s = s[start..(end + 6)];
        s = Declaration().Replace(s, "");
        s = DocType().Replace(s, "");
        s = Comment().Replace(s, "");
        s = AdobeEntity().Replace(s, "urn:x-adobe");
        s = LitBinding().Replace(s, "");
        // Lit fills an attribute's value without quotes (fill=${colour}); XML wants them.
        s = Regex.Replace(s, @"<[^<>!?/][^<>]*>", tag => Unquoted().Replace(tag.Value, m => $"{m.Groups[1].Value}=\"{m.Groups[2].Value.TrimEnd('/')}\"" + (m.Groups[2].Value.EndsWith('/') ? "/" : "")));
        return s.Trim();
    }

    public static XElement Parse(string svg)
    {
        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null, IgnoreComments = true, IgnoreProcessingInstructions = true };
        using var reader = XmlReader.Create(new StringReader(Text(svg)), settings);
        var root = XDocument.Load(reader).Root!;
        // A file without the SVG namespace (Lit's templates) is read as SVG.
        if (root.Name.Namespace == XNamespace.None)
            foreach (var e in root.DescendantsAndSelf()) e.Name = Svg + e.Name.LocalName;
        return root;
    }

    /// <summary>The file, plain: switches opened, styles written onto elements, patterns tiled.</summary>
    public static string Plain(string svg)
    {
        var root = Parse(svg);
        foreach (var sw in root.Descendants(Svg + "switch").ToList())
            sw.ReplaceWith(sw.Elements().Where(e => e.Name.LocalName != "foreignObject"));
        // Editors' own data (Illustrator's private copy of the file, Inkscape's views, metadata) isn't drawing.
        foreach (var fo in root.Descendants().Where(e => e.Name.Namespace != Svg || e.Name.LocalName is "foreignObject" or "metadata").ToList()) fo.Remove();
        InlineStyles(root);
        Tile(root);
        return root.ToString(SaveOptions.DisableFormatting);
    }

    [GeneratedRegex(@"([^{}]+)\{([^{}]*)\}")]
    private static partial Regex Rule();

    /// <summary>Each <c>.class</c> rule of the file's style sheets onto the elements of that class (in the sheet's order,
    /// an element's own style last), as a style attribute; the sheets go.</summary>
    static void InlineStyles(XElement root)
    {
        var rules = new List<(string Class, string Declarations)>();
        var tags = new List<(string Tag, string Declarations)>();
        foreach (var style in root.Descendants().Where(e => e.Name.LocalName == "style").ToList())
        {
            string css = Comment().Replace(Regex.Replace(style.Value, @"/\*[\s\S]*?\*/", ""), "");
            foreach (Match m in Rule().Matches(css))
                foreach (string selector in m.Groups[1].Value.Split(','))
                {
                    string sel = selector.Trim();
                    if (sel.StartsWith("svg ", StringComparison.Ordinal)) sel = sel[4..].Trim();
                    int dot = sel.LastIndexOf('.');
                    if (sel.Contains(' ') || sel.Contains(':') || sel.Contains('[') || sel.Contains('>')) continue;
                    if (dot >= 0) rules.Add((sel[(dot + 1)..], m.Groups[2].Value.Trim()));
                    else if (Regex.IsMatch(sel, "^[a-z]+$")) tags.Add((sel, m.Groups[2].Value.Trim()));
                }
            style.Remove();
        }
        if (rules.Count == 0 && tags.Count == 0) return;
        foreach (var e in root.DescendantsAndSelf())
        {
            var classes = ((string?)e.Attribute("class") ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var sb = new StringBuilder();
            foreach (var (tag, d) in tags)
                if (e.Name.LocalName == tag) sb.Append(d).Append(';');
            if (classes.Length == 0 && sb.Length == 0) continue;
            foreach (var (c, d) in rules)
                if (classes.Contains(c)) sb.Append(d).Append(';');
            if ((string?)e.Attribute("style") is { } own) sb.Append(own);
            if (sb.Length > 0) e.SetAttributeValue("style", sb.ToString());
            e.SetAttributeValue("class", null);
        }
    }

    /// <summary>A rectangle filled with a pattern becomes copies of the pattern's tile over it (a <c>use</c> each, of
    /// one group in defs), clipped to the rectangle; patterns go.</summary>
    static void Tile(XElement root)
    {
        var patterns = root.Descendants().Where(e => e.Name.LocalName == "pattern" && (string?)e.Attribute("id") is { Length: > 0 }).ToList();
        if (patterns.Count == 0) return;
        var defs = root.Elements().FirstOrDefault(e => e.Name.LocalName == "defs");
        if (defs is null) root.AddFirst(defs = new XElement(Svg + "defs"));
        int clips = 0;
        foreach (var pattern in patterns)
        {
            string id = (string)pattern.Attribute("id")!;
            double pw = N(pattern, "width"), ph = N(pattern, "height"), px = N(pattern, "x"), py = N(pattern, "y");
            if (pw <= 0 || ph <= 0) continue;
            string tile = "tile-" + id;
            defs.Add(new XElement(Svg + "g", new XAttribute("id", tile), pattern.Elements().Select(c => new XElement(c))));
            foreach (var rect in root.Descendants().Where(e => e.Name.LocalName == "rect" && ((string?)e.Attribute("fill"))?.Replace(" ", "") == $"url(#{id})").ToList())
            {
                double x = N(rect, "x"), y = N(rect, "y"), w = N(rect, "width"), h = N(rect, "height");
                if (w <= 0 || h <= 0 || w / pw * (h / ph) > 400) { rect.Remove(); continue; }
                string clip = $"tiles-{++clips}";
                defs.Add(new XElement(Svg + "clipPath", new XAttribute("id", clip),
                    new XElement(Svg + "rect", new XAttribute("x", F(x)), new XAttribute("y", F(y)), new XAttribute("width", F(w)), new XAttribute("height", F(h)))));
                var g = new XElement(Svg + "g", new XAttribute("clip-path", $"url(#{clip})"));
                if ((string?)rect.Attribute("transform") is { } t) g.SetAttributeValue("transform", t);
                double x0 = px + Math.Floor((x - px) / pw + 1e-6) * pw, y0 = py + Math.Floor((y - py) / ph + 1e-6) * ph;
                for (double ty = y0; ty < y + h - 1e-6; ty += ph)
                    for (double tx = x0; tx < x + w - 1e-6; tx += pw)
                        g.Add(new XElement(Svg + "use", new XAttribute("href", "#" + tile), new XAttribute("x", F(tx)), new XAttribute("y", F(ty))));
                rect.ReplaceWith(g);
            }
            pattern.Remove();
        }
    }

    static double N(XElement e, string attr) =>
        double.TryParse(((string?)e.Attribute(attr))?.Replace("px", ""), NumberStyles.Float, CultureInfo.InvariantCulture, out double d) ? d : 0;

    static string F(double d) => d.ToString("0.####", CultureInfo.InvariantCulture);
}
