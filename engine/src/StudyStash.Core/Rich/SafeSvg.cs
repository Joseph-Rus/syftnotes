using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace StudyStash.Core.Rich;

/// <summary>
/// The colours an SVG diagram may be written in, by what each is for: the ink its lines and words are drawn in, a
/// secondary grey for lesser words, light lines, the paper, and a stroke and a fill for each tone. The notes' writers
/// are given <see cref="Written"/>; the app swaps each for its own colour of the same role, light or dark.
/// </summary>
public sealed record SvgPalette(string Ink, string Secondary, string LightLines, string Paper, IReadOnlyDictionary<Tone, (string Stroke, string Fill)> Tones)
{
    /// <summary>The colours the writers are told to use (and the ones an exported file keeps).</summary>
    public static SvgPalette Written { get; } = new("#1D1D1F", "#6E6E73", "#C7C7CC", "#FFFFFF", new Dictionary<Tone, (string, string)>
    {
        [Tone.Red] = ("#D93025", "#FCE8E6"),
        [Tone.Blue] = ("#1A73E8", "#E8F0FE"),
        [Tone.Green] = ("#188038", "#E6F4EA"),
        [Tone.Amber] = ("#E37400", "#FEF7E0"),
        [Tone.Purple] = ("#8E24AA", "#F3E8FD"),
    });

    /// <summary>The paper a drawing sits on in dark mode: a raised dark grey, so white paper doesn't glare.</summary>
    public const string DarkPaper = "#2C2C2E";

    /// <summary>Each written colour (lower-case #rrggbb, plus black, white and currentColor) to this palette's colour
    /// for the same role.</summary>
    public IReadOnlyDictionary<string, string> From(SvgPalette written)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [Key(written.Ink)] = Ink,
            [Key(written.Secondary)] = Secondary,
            [Key(written.LightLines)] = LightLines,
            [Key(written.Paper)] = Paper,
            ["#000000"] = Ink,
            ["currentcolor"] = Ink,
        };
        map.TryAdd("#ffffff", Paper);
        foreach (var (tone, pair) in written.Tones)
        {
            if (!Tones.TryGetValue(tone, out var ours)) continue;
            map[Key(pair.Stroke)] = ours.Stroke;
            map[Key(pair.Fill)] = ours.Fill;
        }
        return map;
    }

    static string Key(string hex) => SafeSvg.NormalColour(hex) ?? hex.ToLowerInvariant();
}

/// <summary>How <see cref="SafeSvg.Clean"/> dresses a drawing for where it's shown.</summary>
public sealed record SafeSvgOptions
{
    /// <summary>Every piece of text is set in this font (the look's own).</summary>
    public string FontFamily { get; init; } = DiagramSvg.FontFamily;

    /// <summary>The colours to draw in; null keeps the written colours (an exported file).</summary>
    public SvgPalette? Palette { get; init; }

    /// <summary>For a dark page: a colour outside the palette that is nearly black becomes the palette's ink, and
    /// one that is nearly white its paper, so nothing disappears into the page.</summary>
    public bool Dark { get; init; }
}

/// <summary>A cleaned drawing, or why it can't be shown; its title and words either way (for search and screen
/// readers), and its size in its own units.</summary>
public sealed record SafeSvgResult(string? Svg, string? Problem, string Title, IReadOnlyList<string> Texts, double Width, double Height)
{
    public static SafeSvgResult Fail(string problem) => new(null, problem, "", [], 0, 0);

    /// <summary>The parts an illustration names (see <see cref="SvgPart"/>), in the order it draws them; none for a
    /// plain drawing.</summary>
    public IReadOnlyList<SvgPart> Parts { get; init; } = [];

    /// <summary>An illustration: a drawing whose parts are named, to be pointed at.</summary>
    public bool Illustrated => Parts.Count > 0;
}

/// <summary>
/// A part of an illustration a student can point at: a group with an id whose first child is a title (its name) and,
/// usually, a desc (one line on what it is or does), as SVG itself names a part. Its callout, if it has one, is the
/// group <c>label-{id}</c>.
/// </summary>
public sealed record SvgPart(string Id, string Name, string Note);

/// <summary>
/// Makes an SVG an AI wrote safe to draw: only shapes, text, markers, gradients and clips survive, with only the
/// attributes that shape and colour them. Scripts, links, images, embedded HTML, styles, filters, fonts and animation
/// go (with everything inside them), as does any value that could reach outside the drawing; the file may not define
/// its own entities or expand into a huge picture. The fonts become the app's, the colours the look's.
/// </summary>
public static partial class SafeSvg
{
    /// <summary>The most a drawing may be: 192 KB (a detailed illustration is a few hundred shapes, most of them
    /// curves), 4,000 elements, 32 deep, 1,000 copies of a part and 20,000 elements drawn by copies all told (a board's
    /// hundred pins are a hundred copies of one small pin; a big part copied hundreds of times is a bomb), 5,000 units
    /// either way.</summary>
    public const int MaxBytes = 192 * 1024, MaxElements = 4000, MaxDepth = 32, MaxUses = 1000, MaxCopied = 20_000;
    public const double MaxSize = 5000;

    static readonly XNamespace Ns = "http://www.w3.org/2000/svg";
    static readonly XNamespace XLink = "http://www.w3.org/1999/xlink";

    static readonly HashSet<string> Elements =
    [
        "svg", "g", "defs", "title", "desc", "symbol", "use", "path", "rect", "circle", "ellipse", "line", "polyline",
        "polygon", "text", "tspan", "marker", "linearGradient", "radialGradient", "stop", "clipPath",
    ];

    /// <summary>What shapes and positions things: kept as written (their values are still checked).</summary>
    static readonly HashSet<string> Geometry =
    [
        "x", "y", "x1", "y1", "x2", "y2", "cx", "cy", "r", "rx", "ry", "fx", "fy", "width", "height", "d", "points",
        "dx", "dy", "rotate", "transform", "viewBox", "preserveAspectRatio", "pathLength", "id",
        "markerWidth", "markerHeight", "refX", "refY", "orient", "markerUnits",
        "gradientUnits", "gradientTransform", "spreadMethod", "offset", "clipPathUnits",
    ];

    /// <summary>How things look: allowed as attributes and inside a style attribute alike.</summary>
    static readonly HashSet<string> Presentation =
    [
        "fill", "fill-opacity", "fill-rule", "stroke", "stroke-width", "stroke-opacity", "stroke-dasharray",
        "stroke-dashoffset", "stroke-linecap", "stroke-linejoin", "stroke-miterlimit", "opacity", "font-family",
        "font-size", "font-weight", "font-style", "text-anchor", "dominant-baseline", "alignment-baseline",
        "letter-spacing", "marker-start", "marker-mid", "marker-end", "stop-color", "stop-opacity", "clip-path",
        "clip-rule", "display", "visibility",
    ];

    static readonly HashSet<string> Colours = ["fill", "stroke", "stop-color"];

    static readonly string[] Forbidden = ["javascript:", "vbscript:", "data:", "http", "@import", "expression(", "//"];

    /// <summary>Cleans <paramref name="svg"/> for drawing; never throws. A drawing that can't be made safe (or read)
    /// comes back with a <see cref="SafeSvgResult.Problem"/> in plain English and no SVG.</summary>
    public static SafeSvgResult Clean(string svg, SafeSvgOptions? options = null)
    {
        options ??= new SafeSvgOptions();
        try
        {
            return CleanUnsafe(svg ?? "", options);
        }
        catch (Exception) // whatever the file held, a note shows its source instead
        {
            return SafeSvgResult.Fail("Couldn't read this drawing.");
        }
    }

    static SafeSvgResult CleanUnsafe(string svg, SafeSvgOptions options)
    {
        if (Encoding.UTF8.GetByteCount(svg) > MaxBytes) return SafeSvgResult.Fail($"This drawing is too big to show (over {MaxBytes / 1024} KB).");
        if (string.IsNullOrWhiteSpace(svg)) return SafeSvgResult.Fail("There's no drawing here.");
        XDocument doc;
        try
        {
            var settings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                MaxCharactersInDocument = 800_000,
                MaxCharactersFromEntities = 1024,
                IgnoreComments = true,
                IgnoreProcessingInstructions = true,
            };
            using var reader = XmlReader.Create(new StringReader(BareAmpersands().Replace(svg.Trim(), "&amp;")), settings);
            doc = XDocument.Load(reader, LoadOptions.None);
        }
        catch (XmlException e) when (e.Message.Contains("DTD", StringComparison.OrdinalIgnoreCase))
        {
            return SafeSvgResult.Fail("This drawing declares its own document type, which isn't allowed.");
        }
        catch (XmlException e)
        {
            return SafeSvgResult.Fail($"This drawing isn't well-formed SVG (line {e.LineNumber}).");
        }
        if (doc.Root is not { } root || root.Name.LocalName != "svg" || (root.Name.Namespace != Ns && root.Name.Namespace != XNamespace.None))
            return SafeSvgResult.Fail("This isn't an SVG drawing.");

        int count = 0;
        foreach (var e in root.DescendantsAndSelf())
        {
            if (++count > MaxElements) return SafeSvgResult.Fail($"This drawing has too many parts to show (over {MaxElements:N0}).");
            if (e.Ancestors().Count() >= MaxDepth) return SafeSvgResult.Fail("This drawing is nested too deeply to show.");
        }

        if (ViewBox(root) is not { } box) return SafeSvgResult.Fail("This drawing has no size: it needs a viewBox, or a width and height.");
        if (!Sane(box)) return SafeSvgResult.Fail($"This drawing is too big to show (over {MaxSize:N0} units).");

        var map = options.Palette?.From(SvgPalette.Written);
        // An illustration's own colours are kept, and only moved into the lightness a dark page can carry.
        bool art = root.Descendants().Any(e => PartName(e) is not null);
        // A background an illustration left in (a near-white rectangle over the whole canvas) is the paper.
        var grounds = art ? root.Descendants().Where(e => e.Name.LocalName == "rect" && Covers(e, box)).ToHashSet() : [];
        var clean = Copy(root, options, map, art, grounds, depth: 0)!;
        clean.SetAttributeValue("viewBox", string.Create(CultureInfo.InvariantCulture, $"{box.X:0.###} {box.Y:0.###} {box.W:0.###} {box.H:0.###}"));
        clean.SetAttributeValue("width", null);
        clean.SetAttributeValue("height", null);
        clean.SetAttributeValue("font-family", options.FontFamily);

        var uses = clean.Descendants(Ns + "use").ToList();
        if (uses.Count > MaxUses) return SafeSvgResult.Fail("This drawing repeats its parts too many times to show.");
        var byId = new Dictionary<string, XElement>();
        foreach (var e in clean.DescendantsAndSelf())
            if ((string?)e.Attribute("id") is { Length: > 0 } id) byId.TryAdd(id, e);
        // A use may only copy a plain part: one with a use inside is how a small file expands into a huge picture.
        // Judged on the drawing as written, so removing one copy never makes a copy of it look plain.
        var bombs = uses.Where(use => (string?)use.Attribute("href") is not { } href || !byId.TryGetValue(href[1..], out var target)
            || target.Name.LocalName is "use" or "svg" || target.Descendants(Ns + "use").Any()).ToList();
        foreach (var use in bombs) use.Remove();
        long copied = 0;
        foreach (var use in clean.Descendants(Ns + "use"))
            if (byId.TryGetValue(((string)use.Attribute("href")!)[1..], out var target)) copied += target.DescendantsAndSelf().Count();
        if (copied > MaxCopied) return SafeSvgResult.Fail("This drawing repeats its parts too many times to show.");

        var texts = clean.Descendants(Ns + "text").Select(t => Words(t.Value)).Where(t => t.Length > 0).ToList();
        string title = clean.Elements(Ns + "title").Select(t => Words(t.Value)).FirstOrDefault(t => t.Length > 0) ?? texts.FirstOrDefault() ?? "";
        var parts = new List<SvgPart>();
        foreach (var e in clean.Descendants())
            if (PartName(e) is { } name && (string?)e.Attribute("id") is { } id && parts.All(p => p.Id != id))
                parts.Add(new SvgPart(id, name, e.Elements(Ns + "desc").Select(d => Words(d.Value)).FirstOrDefault() ?? ""));
        return new SafeSvgResult(clean.ToString(SaveOptions.DisableFormatting), null, title, texts, box.W, box.H) { Parts = parts };
    }

    /// <summary>A part's name: the words of the title that is the first child of an element with an id (never the
    /// drawing's own title, nor a callout's); null for anything else.</summary>
    static string? PartName(XElement e)
    {
        if (e.Parent is null || (string?)e.Attribute("id") is not { Length: > 0 } id || id == "labels" || id.StartsWith("label-", StringComparison.Ordinal)) return null;
        if (e.Elements().FirstOrDefault() is not { } first || first.Name.LocalName != "title") return null;
        return Words(first.Value) is { Length: > 0 } name ? name : null;
    }

    /// <summary>A copy of an allowed element with only its allowed attributes and children; null for anything else.</summary>
    static XElement? Copy(XElement source, SafeSvgOptions options, IReadOnlyDictionary<string, string>? map, bool art, HashSet<XElement> grounds, int depth)
    {
        if (source.Name.Namespace != Ns && source.Name.Namespace != XNamespace.None) return null;
        string name = source.Name.LocalName;
        if (!Elements.Contains(name)) return null;
        var copy = new XElement(Ns + name);
        var values = new Dictionary<string, string>();
        string? style = null;
        foreach (var a in source.Attributes())
        {
            if (a.IsNamespaceDeclaration) continue;
            string local = a.Name.LocalName;
            if (a.Name.Namespace == XLink || (a.Name.Namespace == XNamespace.None && local == "href"))
            {
                if (local == "href" && name == "use" && LocalRef().IsMatch(a.Value)) values["href"] = a.Value.Trim();
                continue;
            }
            if (a.Name.Namespace == XNamespace.Xml)
            {
                if (local == "space") copy.SetAttributeValue(XNamespace.Xml + "space", a.Value == "preserve" ? "preserve" : "default");
                continue;
            }
            if (a.Name.Namespace != XNamespace.None) continue;
            if (local == "style") style = a.Value;
            else if ((Geometry.Contains(local) || Presentation.Contains(local)) && Safe(a.Value)) values[local] = a.Value;
        }
        // A style attribute outranks the plain attributes, so its properties land last.
        if (style is not null)
            foreach (var declaration in style.Split(';'))
            {
                int colon = declaration.IndexOf(':');
                if (colon <= 0) continue;
                string property = declaration[..colon].Trim().ToLowerInvariant(), value = declaration[(colon + 1)..].Trim();
                if (value.EndsWith("!important", StringComparison.OrdinalIgnoreCase)) value = value[..^10].Trim();
                if (Presentation.Contains(property) && value.Length > 0 && Safe(value) && !value.Contains('\\')) values[property] = value;
            }
        foreach (var (key, raw) in values)
        {
            string value = raw;
            if (key == "font-family") continue;
            if (Colours.Contains(key))
                value = grounds.Contains(source) && key == "fill" && options is { Dark: true, Palette: { } p } && NormalColour(value) is { } g && SvgColour.IsPaper(g)
                    ? p.Paper
                    : Recolour(value, options, map, art && name is not ("text" or "tspan"));
            copy.SetAttributeValue(key, value);
        }
        if (values.ContainsKey("font-family") && name != "svg") copy.SetAttributeValue("font-family", options.FontFamily);

        foreach (var node in source.Nodes())
        {
            if (node is XElement child)
            {
                if (depth + 1 < MaxDepth && Copy(child, options, map, art, grounds, depth + 1) is { } kept) copy.Add(kept);
            }
            else if (node is XText text && name is "text" or "tspan" or "title" or "desc")
                copy.Add(new XText(text.Value));
        }
        return copy;
    }

    /// <summary>No value may reach outside the drawing: no scripts, no web addresses, no embedded files, no imports,
    /// and a url() only ever points at a part of the drawing itself.</summary>
    static bool Safe(string value)
    {
        var squeezed = new StringBuilder(value.Length);
        foreach (char c in value)
            if (!char.IsWhiteSpace(c) && !char.IsControl(c)) squeezed.Append(char.ToLowerInvariant(c));
        string s = squeezed.ToString();
        if (Forbidden.Any(f => s.Contains(f, StringComparison.Ordinal))) return false;
        if (s.Contains("url(", StringComparison.Ordinal) && !AnyUrl().Matches(value).All(m => LocalUrl().IsMatch(m.Value))) return false;
        return !s.Contains('<') && !s.Contains('>');
    }

    /// <summary>A written colour in the look's colour for the same role; in dark, an unknown near-black or near-white
    /// flips so it stays visible, and an illustration's own colours (<paramref name="art"/>: a shape in one, not its
    /// words) move into the lightness a dark page carries (<see cref="SvgColour.ForDark"/>). Anything else (none, a gradient, a colour of its own in
    /// light) as it was.</summary>
    static string Recolour(string value, SafeSvgOptions options, IReadOnlyDictionary<string, string>? map, bool art)
    {
        if (map is null) return value;
        string? key = NormalColour(value);
        if (key is null) return value;
        // In an illustration black and white are colours like any other (a motor's body, a highlight), not the ink words
        // are in or the paper.
        if (!(art && key is "#000000" or "#ffffff") && map.TryGetValue(key, out var ours)) return ours;
        if (options.Dark && art && key.StartsWith('#')) return SvgColour.ForDark(key);
        if (options.Dark && options.Palette is { } palette && Lightness(key) is { } l)
        {
            if (l < 0.3) return palette.Ink;
            if (l > 0.9) return palette.Paper;
        }
        return value;
    }

    /// <summary>A colour as lower-case #rrggbb (or "currentcolor"), or null when it isn't a plain, solid colour.</summary>
    public static string? NormalColour(string value)
    {
        string v = value.Trim().ToLowerInvariant();
        if (v == "currentcolor") return v;
        if (Named.TryGetValue(v, out var named)) return named;
        if (v.StartsWith('#') && v.Length is 4 or 7 && v.Skip(1).All(Uri.IsHexDigit))
            return v.Length == 7 ? v : $"#{v[1]}{v[1]}{v[2]}{v[2]}{v[3]}{v[3]}";
        if (RgbFunction().Match(v) is { Success: true } m)
        {
            int Channel(Group g) => (int)Math.Clamp(Math.Round(g.Value.EndsWith('%')
                ? double.Parse(g.Value[..^1], CultureInfo.InvariantCulture) * 2.55
                : double.Parse(g.Value, CultureInfo.InvariantCulture)), 0, 255);
            return $"#{Channel(m.Groups[1]):x2}{Channel(m.Groups[2]):x2}{Channel(m.Groups[3]):x2}";
        }
        return null;
    }

    static readonly Dictionary<string, string> Named = new()
    {
        ["black"] = "#000000", ["white"] = "#ffffff", ["gray"] = "#808080", ["grey"] = "#808080", ["silver"] = "#c0c0c0",
        ["dimgray"] = "#696969", ["dimgrey"] = "#696969", ["darkgray"] = "#a9a9a9", ["darkgrey"] = "#a9a9a9",
        ["lightgray"] = "#d3d3d3", ["lightgrey"] = "#d3d3d3", ["gainsboro"] = "#dcdcdc", ["whitesmoke"] = "#f5f5f5",
        ["snow"] = "#fffafa", ["ivory"] = "#fffff0", ["navy"] = "#000080", ["maroon"] = "#800000", ["red"] = "#ff0000",
        ["blue"] = "#0000ff", ["green"] = "#008000", ["lime"] = "#00ff00", ["yellow"] = "#ffff00", ["orange"] = "#ffa500",
        ["purple"] = "#800080", ["teal"] = "#008080", ["olive"] = "#808000", ["aqua"] = "#00ffff", ["fuchsia"] = "#ff00ff",
    };

    /// <summary>A colour's OKLCH lightness (0 black to 1 white).</summary>
    static double? Lightness(string hex)
    {
        if (hex.Length != 7 || hex[0] != '#') return null;
        static double Linear(int c)
        {
            double x = c / 255.0;
            return x <= 0.04045 ? x / 12.92 : Math.Pow((x + 0.055) / 1.055, 2.4);
        }
        double r = Linear(Convert.ToInt32(hex[1..3], 16)), g = Linear(Convert.ToInt32(hex[3..5], 16)), b = Linear(Convert.ToInt32(hex[5..7], 16));
        double l = Math.Cbrt(0.4122214708 * r + 0.5363325363 * g + 0.0514459929 * b);
        double m = Math.Cbrt(0.2119034982 * r + 0.6806995451 * g + 0.1073969566 * b);
        double s = Math.Cbrt(0.0883024619 * r + 0.2817188376 * g + 0.6299787005 * b);
        return 0.2104542553 * l + 0.7936177850 * m - 0.0040720468 * s;
    }

    readonly record struct Rect(double X, double Y, double W, double H);

    /// <summary>The drawing's own coordinates: its viewBox, or 0 0 width height when it only has a size.</summary>
    static Rect? ViewBox(XElement root)
    {
        if ((string?)root.Attribute("viewBox") is { } text)
        {
            var parts = text.Split([' ', ',', '\t', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 4 && parts.All(p => double.TryParse(p, NumberStyles.Float, CultureInfo.InvariantCulture, out _)))
            {
                var n = parts.Select(p => double.Parse(p, NumberStyles.Float, CultureInfo.InvariantCulture)).ToArray();
                return new Rect(n[0], n[1], n[2], n[3]);
            }
        }
        return Length((string?)root.Attribute("width")) is { } w && Length((string?)root.Attribute("height")) is { } h ? new Rect(0, 0, w, h) : null;
    }

    static double? Length(string? value)
    {
        if (value is null) return null;
        string v = value.Trim();
        if (v.EndsWith("px", StringComparison.OrdinalIgnoreCase)) v = v[..^2];
        return double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out double d) ? d : null;
    }

    /// <summary>A rectangle over (nearly) the whole canvas, as written (no transform).</summary>
    static bool Covers(XElement rect, Rect box)
    {
        if (rect.Attribute("transform") is not null) return false;
        double x = Length((string?)rect.Attribute("x")) ?? 0, y = Length((string?)rect.Attribute("y")) ?? 0;
        string? w = (string?)rect.Attribute("width"), h = (string?)rect.Attribute("height");
        double width = w?.Trim() == "100%" ? box.W : Length(w) ?? 0, height = h?.Trim() == "100%" ? box.H : Length(h) ?? 0;
        return x <= box.X + box.W * 0.03 && y <= box.Y + box.H * 0.03 && x + width >= box.X + box.W * 0.97 && y + height >= box.Y + box.H * 0.97;
    }

    static bool Sane(Rect r) =>
        double.IsFinite(r.X) && double.IsFinite(r.Y) && double.IsFinite(r.W) && double.IsFinite(r.H)
        && r.W > 0 && r.H > 0 && r.W <= MaxSize && r.H <= MaxSize && Math.Abs(r.X) <= MaxSize && Math.Abs(r.Y) <= MaxSize;

    static string Words(string text) => Spaces().Replace(text, " ").Trim();

    [GeneratedRegex(@"&(?!(?:#[0-9]+|#x[0-9a-fA-F]+|[A-Za-z][A-Za-z0-9]*);)")]
    private static partial Regex BareAmpersands();

    [GeneratedRegex(@"^\s*#[A-Za-z_][\w\-.:]*\s*$")]
    private static partial Regex LocalRef();

    [GeneratedRegex(@"url\([^)]*\)?", RegexOptions.IgnoreCase)]
    private static partial Regex AnyUrl();

    [GeneratedRegex(@"^url\(\s*(['""]?)#[A-Za-z_][\w\-.:]*\1\s*\)$", RegexOptions.IgnoreCase)]
    private static partial Regex LocalUrl();

    [GeneratedRegex(@"^rgb\(\s*([\d.]+%?)\s*[, ]\s*([\d.]+%?)\s*[, ]\s*([\d.]+%?)\s*\)$")]
    private static partial Regex RgbFunction();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();
}
