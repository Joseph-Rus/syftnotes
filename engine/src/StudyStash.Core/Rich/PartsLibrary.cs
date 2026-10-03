using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace StudyStash.Core.Rich;

/// <summary>A named piece of a library part a label can point at (a heart's left ventricle, a board's USB port): its
/// shapes are the group <c>r-{id}</c> in the part's drawing, and (<see cref="X"/>, <see cref="Y"/>) is a point well
/// inside it, in the part's own units.</summary>
public sealed record LibraryRegion(string Id, string Name, IReadOnlyList<string> Tags, double X, double Y);

/// <summary>A point other parts and connectors attach to (a burette's tip, a board's pin 13), in the part's units.</summary>
public sealed record LibraryPort(string Id, double X, double Y);

/// <summary>
/// One ready-made part of an illustration: a drawing (an SVG symbol, already safe, in the house palette), what it is
/// (name, tags, category, the view it's drawn from, its real size), its box in its own units, a point well inside it,
/// its regions and ports, and who made it under what licence.
/// </summary>
public sealed record LibraryPart(string Id, string Name, string Category, string View, string Size, IReadOnlyList<string> Tags,
    Bounds Box, double X, double Y, IReadOnlyList<LibraryRegion> Regions, IReadOnlyList<LibraryPort> Ports,
    string Author, string Licence, string Credit)
{
    internal XElement Symbol { get; init; } = new("symbol");

    /// <summary>Whether its licence asks for credit wherever it's shown (CC BY): the illustration's caption says so.</summary>
    public bool NeedsCredit => Licence.StartsWith("CC BY", StringComparison.Ordinal);
}

/// <summary>Who made some of the library's parts, under what licence, and how many: the About box and the README's
/// credits.</summary>
public sealed record PartsCredit(string Credit, string Licence, int Parts);

/// <summary>
/// The library of ready-made illustration parts (Core/Rich/PartsLibrary.xml, built by engine/tools/PartsImport from
/// Bioicons' Servier, DBCLS and CC0 art, Wikimedia's public-domain cells, Wokwi's boards and modules, and Study
/// Stash's own drawings; see docs/parts-sources.md). It finds the parts a lecture's illustration could be made of
/// (<see cref="Shortlist"/>, by its words, no model asked), and hands out a part's drawing only after
/// <see cref="SafeSvg"/> has passed it, as every drawing the notes show: a part it refuses is never used.
/// </summary>
public sealed partial class PartsLibrary
{
    readonly Dictionary<string, LibraryPart> byId;
    readonly Dictionary<string, XElement?> safe = new(StringComparer.Ordinal);
    readonly List<(LibraryPart Part, Dictionary<string, int> Terms, int Length)> docs = [];
    readonly Dictionary<string, int> documentFrequency = [];
    readonly double averageLength;

    public IReadOnlyList<LibraryPart> Parts { get; }

    /// <summary>The library's version, from its file: a scene composed with one is only reused with the same.</summary>
    public string Version { get; }

    static readonly Lazy<PartsLibrary> Embedded = new(() =>
    {
        using var stream = typeof(PartsLibrary).Assembly.GetManifestResourceStream("StudyStash.Core.Rich.PartsLibrary.xml");
        if (stream is null) return new PartsLibrary([], "none");
        using var reader = new StreamReader(stream);
        return Read(reader.ReadToEnd());
    });

    /// <summary>The library that ships with the app.</summary>
    public static PartsLibrary Default => Embedded.Value;

    PartsLibrary(List<LibraryPart> parts, string version)
    {
        Parts = parts;
        Version = version;
        byId = parts.ToDictionary(p => p.Id, StringComparer.Ordinal);
        foreach (var p in parts)
        {
            var terms = new Dictionary<string, int>();
            void Add(string text, int weight)
            {
                foreach (string t in Terms(text))
                    terms[t] = terms.GetValueOrDefault(t) + weight;
            }
            Add(p.Name, 3);
            foreach (string t in p.Tags) Add(t, 2);
            foreach (var r in p.Regions)
            {
                Add(r.Name, 2);
                foreach (string t in r.Tags) Add(t, 1);
            }
            Add(p.Category + " " + p.View, 1);
            int length = terms.Values.Sum();
            docs.Add((p, terms, length));
            foreach (string t in terms.Keys) documentFrequency[t] = documentFrequency.GetValueOrDefault(t) + 1;
        }
        averageLength = docs.Count == 0 ? 1 : docs.Average(d => d.Length);
    }

    /// <summary>A library from its XML; a part that can't be read is left out (its drawing is checked when it's used).
    /// Never throws: a library that can't be read is empty.</summary>
    public static PartsLibrary Read(string xml)
    {
        XElement root;
        try
        {
            var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 64_000_000 };
            using var reader = XmlReader.Create(new StringReader(xml), settings);
            root = XDocument.Load(reader).Root!;
        }
        catch (XmlException)
        {
            return new PartsLibrary([], "unreadable");
        }
        var parts = new List<LibraryPart>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var e in root.Elements("part"))
        {
            string id = (string?)e.Attribute("id") ?? "";
            var symbol = e.Elements().FirstOrDefault(x => x.Name.LocalName == "symbol");
            if (!PlainId().IsMatch(id) || !seen.Add(id) || symbol is null || ViewBox((string?)symbol.Attribute("viewBox")) is not { } box) continue;
            static IReadOnlyList<string> Tags(string? s) => (s ?? "").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            parts.Add(new LibraryPart(id, (string?)e.Attribute("name") ?? id, (string?)e.Attribute("category") ?? "", (string?)e.Attribute("view") ?? "",
                (string?)e.Attribute("size") ?? "", Tags((string?)e.Attribute("tags")), box,
                Num(e, "x", box.CenterX), Num(e, "y", box.CenterY),
                e.Elements("region").Where(r => PlainId().IsMatch((string?)r.Attribute("id") ?? ""))
                    .Select(r => new LibraryRegion((string)r.Attribute("id")!, (string?)r.Attribute("name") ?? "", Tags((string?)r.Attribute("tags")), Num(r, "x", box.CenterX), Num(r, "y", box.CenterY))).ToList(),
                e.Elements("port").Where(r => PlainId().IsMatch((string?)r.Attribute("id") ?? ""))
                    .Select(r => new LibraryPort((string)r.Attribute("id")!, Num(r, "x", box.CenterX), Num(r, "y", box.CenterY))).ToList(),
                (string?)e.Attribute("author") ?? "", (string?)e.Attribute("licence") ?? "", (string?)e.Attribute("credit") ?? "")
            { Symbol = symbol });
        }
        return new PartsLibrary(parts, $"{(string?)root.Attribute("version") ?? "0"}-{parts.Count}-{xml.Length}");
    }

    public LibraryPart? Get(string id) => byId.GetValueOrDefault(id);

    /// <summary>
    /// A part's drawing, safe to show: its symbol's contents as <see cref="SafeSvg"/> leaves them, with the part's own
    /// ids. Null when SafeSvg refuses it (or it holds nothing): such a part is never drawn. Checked once per part.
    /// </summary>
    public XElement? Drawing(LibraryPart part)
    {
        lock (safe)
        {
            if (safe.TryGetValue(part.Id, out var done)) return done;
            SafeSvgResult cleaned;
            try
            {
                var svg = new XElement(Ns + "svg", new XAttribute("viewBox", F($"{part.Box.X} {part.Box.Y} {part.Box.W} {part.Box.H}")),
                    part.Symbol.Nodes().Select(n => n is XElement x ? Into(x) : n));
                cleaned = SafeSvg.Clean(svg.ToString(SaveOptions.DisableFormatting));
            }
            catch (Exception)
            {
                // Whatever the part holds, a part that can't be read is never drawn.
                cleaned = SafeSvgResult.Fail("unreadable");
            }
            XElement? drawing = null;
            if (cleaned.Svg is not null)
            {
                drawing = XElement.Parse(cleaned.Svg);
                // Its ids must be its own, so two parts never clash: anything else goes.
                foreach (var x in drawing.Descendants())
                    if ((string?)x.Attribute("id") is { } xid && !xid.StartsWith(part.Id + "--", StringComparison.Ordinal) && !xid.StartsWith("r-", StringComparison.Ordinal))
                        x.SetAttributeValue("id", null);
                if (!drawing.Elements().Any(x => x.Name.LocalName != "defs")) drawing = null;
            }
            safe[part.Id] = drawing;
            return drawing;
        }
    }

    /// <summary>An element and all it holds in the SVG namespace (whatever namespace it claimed, which SafeSvg then judges
    /// by its name), without namespace declarations of its own.</summary>
    static XElement Into(XElement e) =>
        new(Ns + e.Name.LocalName, e.Attributes().Where(a => !a.IsNamespaceDeclaration), e.Nodes().Select(n => n is XElement c ? Into(c) : n));

    static readonly XNamespace Ns = "http://www.w3.org/2000/svg";

    /// <summary>
    /// The parts an illustration described by <paramref name="words"/> (its subject, layout and parts) could be made
    /// of, best first: BM25 over each part's name, tags, regions, category and view, so a part named in the words
    /// outranks one only tagged with them. At most <paramref name="max"/>, each with its score; none when nothing
    /// matches.
    /// </summary>
    public IReadOnlyList<(LibraryPart Part, double Score)> Shortlist(string words, int max = 60)
    {
        var query = Terms(words).GroupBy(t => t).ToDictionary(g => g.Key, g => Math.Min(3, g.Count()));
        var scored = new List<(LibraryPart, double)>();
        foreach (var (part, terms, length) in docs)
        {
            double score = 0;
            foreach (var (t, qf) in query)
            {
                if (!terms.TryGetValue(t, out int tf)) continue;
                double idf = Math.Log(1 + (docs.Count - documentFrequency[t] + 0.5) / (documentFrequency[t] + 0.5));
                score += qf * idf * tf * 2.2 / (tf + 1.2 * (0.25 + 0.75 * length / averageLength));
            }
            if (score > 0) scored.Add((part, score));
        }
        return scored.OrderByDescending(s => s.Item2).ThenBy(s => s.Item1.Id, StringComparer.Ordinal).Take(max).ToList();
    }

    /// <summary>
    /// The credits for the About box, from the library itself: each source whose licence asks for credit (CC BY, MIT)
    /// by its own credit line, then how many more parts are public domain or CC0; and where every part is listed.
    /// </summary>
    public static string AboutLine => aboutLine ??= Describe(Default);

    static string? aboutLine;

    static string Describe(PartsLibrary library)
    {
        var credits = library.Credits();
        var named = credits.Where(c => (c.Licence.StartsWith("CC BY", StringComparison.Ordinal) || c.Licence is "MIT" or "BSD") && !c.Credit.StartsWith("Study Stash", StringComparison.Ordinal))
            .Select(c => c.Credit).Distinct().ToList();
        int free = credits.Where(c => c.Licence is "CC0 1.0" or "Public domain").Sum(c => c.Parts);
        int own = credits.Where(c => c.Credit.StartsWith("Study Stash", StringComparison.Ordinal)).Sum(c => c.Parts);
        var sb = new System.Text.StringBuilder();
        sb.Append(string.Join("; ", named));
        if (free > 0) sb.Append($"; {free} public-domain and CC0 parts from Bioicons' artists and Wikimedia Commons");
        if (own > 0) sb.Append($"; {own} of Study Stash's own");
        sb.Append(". Recoloured and simplified; every part and its licence is listed in docs/parts-credits.md in Study Stash's repository.");
        return sb.ToString();
    }

    /// <summary>Who made the parts, by credit line and licence, most parts first.</summary>
    public IReadOnlyList<PartsCredit> Credits() =>
        Parts.GroupBy(p => (p.Credit, p.Licence)).Select(g => new PartsCredit(g.Key.Credit, g.Key.Licence, g.Count()))
            .OrderByDescending(c => c.Parts).ThenBy(c => c.Credit, StringComparer.Ordinal).ToList();

    static readonly HashSet<string> Stop =
    [
        "the", "and", "with", "from", "into", "onto", "that", "this", "its", "are", "for", "seen", "view", "side", "one", "two",
        "each", "where", "which", "what", "they", "their", "there", "then", "than", "has", "have", "how", "all", "out", "off",
        "left", "right", "top", "bottom", "front", "back", "above", "below", "under", "over", "near", "next", "between", "about",
        "part", "parts", "shown", "show", "drawn", "picture", "illustration", "lecture", "lecturer", "said", "you", "your",
    ];

    [GeneratedRegex(@"[^\p{L}\p{N}]+")]
    private static partial Regex NotWord();

    [GeneratedRegex(@"^[a-z][a-z0-9-]{0,80}$")]
    private static partial Regex PlainId();

    /// <summary>A text's words as the library compares them: lower case, no little words, a plural's s gone.</summary>
    public static IEnumerable<string> Terms(string text)
    {
        foreach (string w in NotWord().Split(text.ToLowerInvariant()))
        {
            if (w.Length < 2 || Stop.Contains(w)) continue;
            yield return Stem(w);
        }
    }

    static string Stem(string w)
    {
        if (w.Length > 4 && w.EndsWith("ies", StringComparison.Ordinal)) return w[..^3] + "y";
        if (w.Length > 4 && (w.EndsWith("ches", StringComparison.Ordinal) || w.EndsWith("shes", StringComparison.Ordinal) || w.EndsWith("xes", StringComparison.Ordinal))) return w[..^2];
        if (w.Length > 3 && w.EndsWith('s') && !w.EndsWith("ss", StringComparison.Ordinal) && !w.EndsWith("us", StringComparison.Ordinal) && !w.EndsWith("is", StringComparison.Ordinal)) return w[..^1];
        if (w.EndsWith("ae", StringComparison.Ordinal) && w.Length > 4) return w[..^1]; // vertebrae, chordae
        return w;
    }

    static Bounds? ViewBox(string? text)
    {
        var n = (text ?? "").Split([' ', ','], StringSplitOptions.RemoveEmptyEntries);
        if (n.Length != 4) return null;
        var v = new double[4];
        for (int i = 0; i < 4; i++)
            if (!double.TryParse(n[i], NumberStyles.Float, CultureInfo.InvariantCulture, out v[i]) || !double.IsFinite(v[i])) return null;
        return v[2] > 0 && v[3] > 0 && v[2] < SafeSvg.MaxSize && v[3] < SafeSvg.MaxSize ? new Bounds(v[0], v[1], v[2], v[3]) : null;
    }

    static double Num(XElement e, string attr, double fallback) =>
        double.TryParse((string?)e.Attribute(attr), NumberStyles.Float, CultureInfo.InvariantCulture, out double d) && double.IsFinite(d) ? d : fallback;

    static string F(FormattableString s) => s.ToString(CultureInfo.InvariantCulture);
}
