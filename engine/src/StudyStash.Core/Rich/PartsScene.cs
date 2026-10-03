using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace StudyStash.Core.Rich;

/// <summary>
/// An illustration composed from ready-made parts (<see cref="PartsLibrary"/>) rather than drawn shape by shape: the
/// composer's small JSON plan (which parts, where and how big, a few simple shapes where no part exists, and which part
/// or region each label points at), read strictly (<see cref="Read"/>), and the SVG Study Stash draws from it
/// (<see cref="Draw"/>) in the house style: each labelled part a group with its id, title and desc, the callouts in
/// columns with leader lines, exactly as an AI-drawn illustration is structured, so everything that shows, searches,
/// exports and prints an illustration takes it as it is.
/// </summary>
public static partial class PartsScene
{
    /// <summary>The most a scene may hold: parts placed, simple shapes, points in one shape.</summary>
    public const int MaxPlaced = 30, MaxShapes = 12, MaxPoints = 40;

    /// <summary>How far from the middle anything may be, in the composer's units.</summary>
    const double Far = 10_000;

    /// <summary>A part placed: which library part, its name in the scene, its middle and width (its height keeps the
    /// part's proportions), how far it's turned (degrees, clockwise) and whether it's mirrored; or, instead of a middle,
    /// one of its ports put on a point.</summary>
    public sealed record Placed(string As, LibraryPart Part, double X, double Y, double W, double Turn, bool Flip, string? On, Point? At)
    {
        /// <summary>A part placed before it whose scale this one takes (parts drawn to scale: a rack and a server).</summary>
        public string? Like { get; init; }
    }

    /// <summary>A point: a number pair, or a port or region of a part placed earlier (<c>stand.tip</c>).</summary>
    public sealed record Point(double X, double Y, string? Ref);

    /// <summary>A simple shape for what no part provides: an ellipse or a rectangle (middle and size), a smooth closed
    /// blob or a smooth band (a tendon, a wire, a tube) through points, or an arrow; in a material of the house palette.</summary>
    public sealed record Shape(string As, string Kind, string Material, IReadOnlyList<Point> Points, double W, double H, double Width, bool Under);

    /// <summary>A label: the planned part it names, and what it points at (a placed part, one of its regions, or a
    /// shape), with an optional short fact under the name.</summary>
    public sealed record Label(string Part, string To, string Fact)
    {
        /// <summary>Where on its target the leader ends, when the composer says (in its units); else Study Stash picks.</summary>
        public (double X, double Y)? At { get; init; }
    }

    public sealed record Scene(IReadOnlyList<Placed> Parts, IReadOnlyList<Shape> Shapes, IReadOnlyList<Label> Labels);

    /// <summary>
    /// A composer's answer, read strictly: JSON with <c>compose</c> true, parts only from <paramref name="allowed"/>,
    /// every number finite and in range, every reference to something placed before it, every label for a planned
    /// part and pointing at something that exists. Anything that breaks a rule is left out with a word on why
    /// (<paramref name="dropped"/>); null, with why, when nothing usable is left or the composer said it can't.
    /// </summary>
    public static Scene? Read(string answer, IReadOnlyDictionary<string, LibraryPart> allowed, IReadOnlyList<Illustration.Planned> plan, List<string> dropped)
    {
        if (Json(answer) is not JsonObject o)
        {
            dropped.Add("the composer's answer wasn't JSON");
            return null;
        }
        if (o["compose"] is JsonValue c && c.GetValueKind() == JsonValueKind.False)
        {
            dropped.Add("the composer said these parts can't make it" + (Str(o["why"]) is { Length: > 0 } why ? $": {Clip(why, 200)}" : ""));
            return null;
        }
        var placed = new List<Placed>();
        var names = new HashSet<string>(StringComparer.Ordinal);
        var everyName = o["parts"] is JsonArray all ? all.OfType<JsonObject>().Select(p => Str(p["as"]).ToLowerInvariant()).ToHashSet(StringComparer.Ordinal) : [];
        if (o["parts"] is JsonArray ps)
            foreach (var p in ps.OfType<JsonObject>().Take(MaxPlaced * 2))
            {
                if (placed.Count == MaxPlaced) { dropped.Add($"more than {MaxPlaced} parts placed"); break; }
                string name = Str(p["as"]).ToLowerInvariant(), use = Str(p["use"]);
                if (!Plain().IsMatch(name) || !names.Add(name)) { dropped.Add($"a part named '{Clip(name, 40)}' (names are plain and used once)"); continue; }
                if (!allowed.TryGetValue(use, out var part)) { dropped.Add($"'{name}' uses '{Clip(use, 40)}', which isn't in the catalogue"); continue; }
                double w = Num(p["w"]) ?? double.NaN;
                // A scale may come from a part placed later (a PDU behind the rack it's sized like).
                string? like = Str(p["like"]).ToLowerInvariant() is { Length: > 0 } l && l != name && everyName.Contains(l) ? l : null;
                if (like is not null) w = double.IsFinite(w) && w > 0 ? w : 1;
                if (!(w > 0 && w <= Far)) { dropped.Add($"'{name}' has no width"); continue; }
                double turn = Num(p["turn"]) ?? 0;
                if (Math.Abs(turn) > 360) { dropped.Add($"'{name}' turns {turn:0} degrees"); continue; }
                bool flip = p["flip"] is JsonValue f && f.GetValueKind() == JsonValueKind.True;
                string? on = Str(p["on"]) is { Length: > 0 } port ? port : null;
                Point? at = null;
                double x = Num(p["x"]) ?? double.NaN, y = Num(p["y"]) ?? double.NaN;
                if (on is not null)
                {
                    if (part.Ports.All(q => q.Id != on) && part.Regions.All(r => r.Id != on)) { dropped.Add($"'{name}' has no port '{Clip(on, 40)}'"); continue; }
                    at = Pt(p["at"], placed, []);
                    if (at is null) { dropped.Add($"'{name}' is put on a point that isn't there"); continue; }
                }
                else if (!Finite(x) || !Finite(y)) { dropped.Add($"'{name}' has no place"); continue; }
                placed.Add(new Placed(name, part, on is null ? x : 0, on is null ? y : 0, w, turn, flip, on, at) { Like = like });
            }
        var shapes = new List<Shape>();
        if (o["shapes"] is JsonArray ss)
            foreach (var s in ss.OfType<JsonObject>().Take(MaxShapes * 2))
            {
                if (shapes.Count == MaxShapes) { dropped.Add($"more than {MaxShapes} shapes"); break; }
                string name = Str(s["as"]).ToLowerInvariant(), kind = Str(s["kind"]), material = Str(s["material"]);
                if (!Plain().IsMatch(name) || !names.Add(name)) { dropped.Add($"a shape named '{Clip(name, 40)}' (names are plain and used once)"); continue; }
                if (kind is not ("ellipse" or "rect" or "blob" or "band" or "arrow")) { dropped.Add($"'{name}' is a {Clip(kind, 20)}, not a shape Study Stash draws"); continue; }
                if (Material(material) is null) material = kind == "arrow" ? "ink" : "light-metal";
                var points = new List<Point>();
                if (s["points"] is JsonArray pts)
                    foreach (var n in pts.Take(MaxPoints))
                        if (Pt(n, placed, shapes) is { } q) points.Add(q);
                if (kind is "ellipse" or "rect" && Pt(s["at"], placed, shapes) is { } middle) points = [middle];
                else if (kind is "ellipse" or "rect" && Num(s["x"]) is double sx && Num(s["y"]) is double sy && Finite(sx) && Finite(sy)) points = [new Point(sx, sy, null)];
                double w = Num(s["w"]) ?? 0, h = Num(s["h"]) ?? 0, width = Num(s["width"]) ?? 6;
                bool ok = kind switch
                {
                    "ellipse" or "rect" => points.Count == 1 && w > 0 && h > 0 && w <= Far && h <= Far,
                    "blob" => points.Count >= 3,
                    _ => points.Count >= 2 && width > 0 && width <= 400,
                };
                if (!ok) { dropped.Add($"'{name}' isn't a whole {kind}"); continue; }
                shapes.Add(new Shape(name, kind, material, points, w, h, width, s["under"] is JsonValue u && u.GetValueKind() == JsonValueKind.True));
            }
        var planned = plan.ToDictionary(p => p.Id, StringComparer.Ordinal);
        var labels = new List<Label>();
        if (o["labels"] is JsonArray ls)
            foreach (var l in ls.OfType<JsonObject>())
            {
                string part = Str(l["part"]), to = Lower(Str(l["to"]));
                if (!planned.ContainsKey(part) || labels.Any(x => x.Part == part)) continue;
                if (!Resolves(to, placed, shapes)) { dropped.Add($"the label '{planned[part].Name}' points at '{Clip(to, 40)}', which isn't in the picture"); continue; }
                // Two labels on one thing would be one part with two names: the one the thing is named for keeps it, or
                // else the first.
                int other = labels.FindIndex(x => x.To == to);
                var label = new Label(part, to, Clip(Str(l["fact"]), 48))
                {
                    At = l["at"] is JsonArray a && a.Count == 2 && Num(a[0]) is double ax && Num(a[1]) is double ay && Finite(ax) && Finite(ay) ? (ax, ay) : null,
                };
                if (other >= 0)
                {
                    if (Named(to, planned[part]) && !Named(to, planned[labels[other].Part]))
                    {
                        dropped.Add($"the label '{planned[labels[other].Part].Name}' points at what '{planned[part].Name}' is");
                        labels[other] = label;
                    }
                    else dropped.Add($"the label '{planned[part].Name}' points at what another label does");
                    continue;
                }
                labels.Add(label);
            }
        if (placed.Count == 0 && shapes.Count == 0)
        {
            dropped.Add("nothing was placed");
            return null;
        }
        return new Scene(placed, shapes, labels);
    }

    /// <summary>Whether a label's target is named for the part it labels (the region <c>aorta</c> for the Aorta).</summary>
    static bool Named(string to, Illustration.Planned part)
    {
        string spot = to[(to.IndexOf('.') + 1)..];
        string name = Regex.Replace(part.Name.ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-');
        return spot == part.Id || spot == name;
    }

    static bool Resolves(string to, List<Placed> placed, List<Shape> shapes)
    {
        int dot = to.IndexOf('.');
        string name = dot < 0 ? to : to[..dot];
        if (shapes.Any(s => s.As == name)) return dot < 0;
        if (placed.FirstOrDefault(p => p.As == name) is not { } p) return false;
        return dot < 0 || p.Part.Regions.Any(r => r.Id == to[(dot + 1)..]) || p.Part.Ports.Any(q => q.Id == to[(dot + 1)..]);
    }

    static Point? Pt(JsonNode? n, List<Placed> placed, List<Shape> shapes)
    {
        if (n is JsonArray a && a.Count == 2 && Num(a[0]) is double x && Num(a[1]) is double y && Finite(x) && Finite(y)) return new Point(x, y, null);
        if (n is JsonValue v && v.GetValueKind() == JsonValueKind.String && Lower(v.GetValue<string>()) is { } r)
        {
            int dot = r.IndexOf('.');
            if (dot <= 0) return null;
            string name = r[..dot], port = r[(dot + 1)..];
            if (placed.FirstOrDefault(p => p.As == name) is { } p && (p.Part.Ports.Any(q => q.Id == port) || p.Part.Regions.Any(g => g.Id == port) || port == "middle"))
                return new Point(0, 0, r);
        }
        return null;
    }

    /// <summary>A reference as names are kept: the placed thing's name in lower case, the port or region as given.</summary>
    static string Lower(string reference)
    {
        int dot = reference.IndexOf('.');
        return dot < 0 ? reference.ToLowerInvariant() : reference[..dot].ToLowerInvariant() + reference[dot..];
    }

    static bool Finite(double d) => double.IsFinite(d) && Math.Abs(d) <= Far;

    static double? Num(JsonNode? n) => n is JsonValue v && v.GetValueKind() == JsonValueKind.Number ? v.GetValue<double>() : null;

    static string Str(JsonNode? n) => n is JsonValue v && v.GetValueKind() == JsonValueKind.String ? v.GetValue<string>().Trim() : "";

    static string Clip(string s, int max)
    {
        string t = Regex.Replace(s, @"\s+", " ").Trim();
        return t.Length <= max ? t : t[..max].TrimEnd() + "…";
    }

    [GeneratedRegex(@"^[a-z][a-z0-9-]{0,40}$")]
    private static partial Regex Plain();

    /// <summary>The JSON object in an answer (a fenced block, or the outermost braces); null when there's none.</summary>
    public static JsonNode? Json(string answer)
    {
        string text = Summarize.Thinking().Replace(answer ?? "", "");
        int start = text.IndexOf('{'), end = text.LastIndexOf('}');
        if (start < 0 || end <= start) return null;
        try
        {
            return JsonNode.Parse(text[start..(end + 1)], documentOptions: new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip, MaxDepth = 16 });
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // --- the house materials ------------------------------------------------------------------------------------------

    /// <summary>The palette's materials by a short name the composer uses: light, base, shade, outline.</summary>
    static readonly Dictionary<string, (string Light, string Base, string Shade, string Line)> Materials = Build();

    static Dictionary<string, (string, string, string, string)> Build()
    {
        string[] keys = ["skin", "bone", "tendon", "muscle", "artery", "vein", "nerve", "light-metal", "dark-metal", "copper", "green-board", "blue-board", "red-plastic", "yellow", "glass", "purple"];
        var map = new Dictionary<string, (string, string, string, string)>(StringComparer.Ordinal);
        for (int i = 0; i < keys.Length && i < IllustrationDesign.Palette.Length; i++)
        {
            var p = IllustrationDesign.Palette[i];
            map[keys[i]] = (p.Light, p.Base, p.Shade, p.Line);
        }
        map["ink"] = ("#6E6E73", "#6E6E73", "#6E6E73", "#6E6E73");
        return map;
    }

    /// <summary>The materials a shape may be made of, by name.</summary>
    public static IEnumerable<string> MaterialNames => Materials.Keys.Where(k => k != "ink");

    static (string Light, string Base, string Shade, string Line)? Material(string name) => Materials.TryGetValue(name, out var m) ? m : null;

    // --- drawing it ---------------------------------------------------------------------------------------------------

    /// <summary>What drawing a scene came to: the SVG, the planned parts it labels, and the parts' credits that must
    /// travel with it (CC BY).</summary>
    public sealed record Drawn(string Svg, IReadOnlyList<string> Labelled, IReadOnlyList<string> Credits);

    const double LabelSize = 14, FactSize = 12, Edge = 10, Reach = 36, MinHeight = 360;

    enum Band { Left, Right, Top, Bottom }

    sealed class Callout
    {
        public required Illustration.Planned Part;
        public required string Fact;
        public double X, Y; // where it points, on the canvas
        public double Width, Height, Top, TextX;
        public Band Side;
    }

    /// <summary>Callouts split between two sides by where they point, neither side with more than half and one: the
    /// ones nearest the middle move over.</summary>
    static void Balance(List<Callout> all, Band a, Band b, Func<Callout, double> at, double middle)
    {
        int most = (all.Count + 1) / 2 + 1;
        for (int guard = 0; guard < all.Count; guard++)
        {
            var first = all.Where(c => c.Side == a).ToList();
            var second = all.Where(c => c.Side == b).ToList();
            if (first.Count > most) first.OrderBy(c => Math.Abs(at(c) - middle)).First().Side = b;
            else if (second.Count > most) second.OrderBy(c => Math.Abs(at(c) - middle)).First().Side = a;
            else break;
        }
    }

    /// <summary>A band of callouts above or below the art, in rows: in the order their points sit from left to right,
    /// taking turns between the rows so a far row's leaders pass between the near row's words, each centred over its
    /// point as far as its neighbours and the canvas allow.</summary>
    static void Lay(List<Callout> band, int rowCount, Func<int, double> rowTop, double canvasW, bool up)
    {
        if (band.Count == 0) return;
        band.Sort((x, y) => x.X.CompareTo(y.X));
        var placedRows = new List<List<Callout>>();
        for (int r = 0; r < rowCount; r++)
        {
            var row = band.Where((_, i) => i % rowCount == r).ToList();
            foreach (var c in row)
            {
                c.Top = rowTop(r);
                c.TextX = c.X;
            }
            // A far row's leader must pass the nearer rows' words: such a label moves sideways till it does.
            for (int pass = 0; pass < 3; pass++)
            {
                foreach (var c in row)
                    foreach (var near in placedRows.SelectMany(x => x))
                    {
                        double y = up ? near.Top + near.Height / 2 : near.Top + near.Height / 2;
                        double fromY = up ? c.Top + c.Height : c.Top;
                        if (Math.Abs(c.Y - fromY) < 1) continue;
                        double t = (y - fromY) / (c.Y - fromY);
                        if (t <= 0 || t >= 1) continue;
                        double x = c.TextX + (c.X - c.TextX) * t;
                        double left = near.TextX - near.Width / 2 - 6, right = near.TextX + near.Width / 2 + 6;
                        if (x <= left || x >= right) continue;
                        // Shift so the crossing clears the nearer label, on the nearer side.
                        double shift = (x - left < right - x ? left - x : right - x) / Math.Max(0.05, 1 - t);
                        c.TextX += shift;
                    }
                Spread(row, canvasW);
            }
            placedRows.Add(row);
        }
    }

    static void Spread(List<Callout> row, double canvasW)
    {
        const double gap = 16;
        row.Sort((x, y) => x.TextX.CompareTo(y.TextX));
        for (int i = 0; i < row.Count; i++)
        {
            double least = i == 0 ? Edge + row[i].Width / 2 : row[i - 1].TextX + row[i - 1].Width / 2 + gap + row[i].Width / 2;
            row[i].TextX = Math.Max(row[i].TextX, least);
        }
        for (int i = row.Count - 1; i >= 0; i--)
        {
            double most = i == row.Count - 1 ? canvasW - Edge - row[i].Width / 2 : row[i + 1].TextX - row[i + 1].Width / 2 - gap - row[i].Width / 2;
            row[i].TextX = Math.Min(row[i].TextX, most);
        }
    }

    /// <summary>
    /// The illustration a scene describes, drawn deterministically: the parts placed (each a safe copy of its library
    /// drawing, under one transform), the shapes, then the callouts: in columns either side of the art, or for a wide
    /// thing above and below it too, each in the order its point sits so leaders don't cross, the art scaled to fit
    /// on a canvas <see cref="IllustrationDesign.Width"/> wide. Null when nothing of it can be drawn.
    /// </summary>
    public static Drawn? Draw(Scene scene, IllustrationPlan plan, PartsLibrary library)
    {
        // Where each part goes, in the composer's units.
        var transforms = new Dictionary<string, SvgGeometry.Affine>(StringComparer.Ordinal);
        var drawings = new Dictionary<string, XElement>(StringComparer.Ordinal);
        var scales = new Dictionary<string, double>(StringComparer.Ordinal);
        Bounds? extent = null;
        foreach (var p in scene.Parts.Where(p => p.Like is null)) scales[p.As] = p.W / p.Part.Box.W;
        foreach (var p in scene.Parts)
        {
            if (library.Drawing(p.Part) is not { } drawing) continue;
            var b = p.Part.Box;
            double s = p.Like is { } like && scales.TryGetValue(like, out double theirs) ? theirs : p.W / b.W;
            scales[p.As] = s;
            var local = Rotate(p.Turn).Then(Scale(p.Flip ? -s : s, s)).Then(Move(-b.CenterX, -b.CenterY));
            double cx = p.X, cy = p.Y;
            if (p.On is { } on && p.At is { } at && Where(at, transforms, scene) is { } target && Spot(p.Part, on) is { } port)
            {
                var q = local.Apply(port.X, port.Y);
                cx = target.X - q.X;
                cy = target.Y - q.Y;
            }
            var m = Move(cx, cy).Then(local);
            transforms[p.As] = m;
            drawings[p.As] = drawing;
            var corners = new[] { (b.X, b.Y), (b.Right, b.Y), (b.Right, b.Bottom), (b.X, b.Bottom) }.Select(c => m.Apply(c.Item1, c.Item2)).ToList();
            extent = Union(extent, Bounds.Around(corners));
        }
        var shapePoints = new Dictionary<string, List<(double X, double Y)>>(StringComparer.Ordinal);
        foreach (var s in scene.Shapes)
        {
            var pts = s.Points.Select(pt => Where(pt, transforms, scene)).Where(pt => pt is not null).Select(pt => pt!.Value).ToList();
            if (pts.Count == 0) continue;
            if (s.Kind is "ellipse" or "rect") pts = [(pts[0].X - s.W / 2, pts[0].Y - s.H / 2), (pts[0].X + s.W / 2, pts[0].Y + s.H / 2)];
            shapePoints[s.As] = pts;
            extent = Union(extent, Bounds.Around(pts)?.Inflate(s.Kind is "band" or "arrow" ? s.Width / 2 : 0));
        }
        if (extent is not { } art || art.W <= 0 || art.H <= 0) return null;

        // The callouts: each planned part labelled, at the point it names.
        var planned = plan.Parts.ToDictionary(p => p.Id, StringComparer.Ordinal);
        var callouts = new List<(Callout C, string To)>();
        // Points on parts first; then on the big shapes (a cell's membrane and cytoplasm), each where it's clear of
        // the others.
        var targets = new Dictionary<Label, (double X, double Y)>();
        foreach (var l in scene.Labels.Where(l => !Big(l.To)))
            if (Target(l.To, transforms, shapePoints, scene) is { } t) targets[l] = t;
        foreach (var l in scene.Labels.Where(l => Big(l.To)))
            if (Clear(l.To, shapePoints, scene, targets.Values.ToList()) is { } t) targets[l] = t;
        bool Big(string to) => scene.Shapes.FirstOrDefault(s => s.As == to) is { Kind: "ellipse" or "rect" or "blob" } shape && shapePoints.TryGetValue(to, out var pts)
            && Bounds.Around(pts) is { } b && b.W * b.H > 0.04 * extent.Value.W * extent.Value.H;
        foreach (var l in scene.Labels)
        {
            if (!planned.TryGetValue(l.Part, out var part) || !targets.TryGetValue(l, out var point)) continue;
            // The composer's own point, when it's on the thing.
            if (l.At is { } at && Extent(l.To, transforms, shapePoints, scene) is { } on && on.Inflate(Math.Max(on.W, on.H) * 0.05).Contains(at.X, at.Y)) point = at;
            double width = Math.Max(SvgGeometry.Width(part.Name, LabelSize), l.Fact.Length > 0 ? SvgGeometry.Width(l.Fact, FactSize) : 0);
            callouts.Add((new Callout { Part = part, Fact = l.Fact, X = point.X, Y = point.Y, Width = width, Height = l.Fact.Length > 0 ? 34 : 18 }, l.To));
        }

        double canvasW = IllustrationDesign.Width;
        double leftW = 0, rightW = 0, artLeft, artRight, height, k;
        SvgGeometry.Affine canvas;
        // A wide thing (a drone from the side) has its labels above and below it; anything else, in columns either side.
        bool rows = art.W / art.H >= 1.45 && callouts.Count > 4;
        if (rows)
        {
            // Points near either end go to a column that side; the rest above or below, by height.
            foreach (var (c, _) in callouts)
                c.Side = (c.X - art.X) / art.W < 0.18 ? Band.Left : (c.X - art.X) / art.W > 0.82 ? Band.Right : c.Y < art.CenterY ? Band.Top : Band.Bottom;
            Balance(callouts.Where(x => x.C.Side is Band.Top or Band.Bottom).Select(x => x.C).ToList(), Band.Top, Band.Bottom, c => c.Y, art.CenterY);
            var top = callouts.Where(x => x.C.Side == Band.Top).Select(x => x.C).ToList();
            var bottom = callouts.Where(x => x.C.Side == Band.Bottom).Select(x => x.C).ToList();
            leftW = callouts.Where(x => x.C.Side == Band.Left).Select(x => x.C.Width).DefaultIfEmpty(0).Max();
            rightW = callouts.Where(x => x.C.Side == Band.Right).Select(x => x.C.Width).DefaultIfEmpty(0).Max();
            artLeft = leftW > 0 ? Edge + leftW + Reach : Edge;
            artRight = canvasW - (rightW > 0 ? Edge + rightW + Reach : Edge);
            double usable = canvasW - 2 * Edge;
            int Rows(List<Callout> band) => band.Count == 0 ? 0 : Math.Max(1, (int)Math.Ceiling(band.Sum(c => c.Width + 18) / (usable * 0.8)));
            int rt = Rows(top), rb = Rows(bottom);
            double RowH(List<Callout> band) => band.Any(c => c.Fact.Length > 0) ? 36 : 21;
            double topH = rt * RowH(top), bottomH = rb * RowH(bottom), gap = 18;
            k = (artRight - artLeft) / art.W;
            double fixedH = 2 * Edge + topH + bottomH + (rt > 0 ? gap : 0) + (rb > 0 ? gap : 0);
            k = Math.Min(k, (IllustrationDesign.MaxHeight - fixedH) / art.H);
            height = Math.Clamp(fixedH + art.H * k, MinHeight, IllustrationDesign.MaxHeight);
            double artTop = Edge + topH + (rt > 0 ? gap : 0) + (height - fixedH - art.H * k) / 2;
            canvas = Move(artLeft + (artRight - artLeft - art.W * k) / 2, artTop).Then(Scale(k, k)).Then(Move(-art.X, -art.Y));
            foreach (var (c, _) in callouts) (c.X, c.Y) = canvas.Apply(c.X, c.Y);
            double artBottom = artTop + art.H * k;
            Lay(top, rt, r => artTop - gap - (r + 1) * RowH(top), canvasW, up: true);
            Lay(bottom, rb, r => artBottom + gap + r * RowH(bottom), canvasW, up: false);
            Stack(callouts.Where(x => x.C.Side == Band.Left).Select(x => x.C).ToList(), height);
            Stack(callouts.Where(x => x.C.Side == Band.Right).Select(x => x.C).ToList(), height);
        }
        else
        {
            foreach (var (c, _) in callouts) c.Side = c.X < art.CenterX ? Band.Left : Band.Right;
            Balance(callouts.Select(x => x.C).ToList(), Band.Left, Band.Right, c => c.X, art.CenterX);
            leftW = callouts.Where(x => x.C.Side == Band.Left).Select(x => x.C.Width).DefaultIfEmpty(0).Max();
            rightW = callouts.Where(x => x.C.Side == Band.Right).Select(x => x.C.Width).DefaultIfEmpty(0).Max();
            artLeft = leftW > 0 ? Edge + leftW + Reach : Edge * 2;
            artRight = canvasW - (rightW > 0 ? Edge + rightW + Reach : Edge * 2);
            double room = Math.Max(160, artRight - artLeft);
            int perSide = Math.Max(callouts.Count(x => x.C.Side == Band.Left), callouts.Count(x => x.C.Side == Band.Right));
            double labelsH = callouts.Count == 0 ? 0 : perSide * 40 + 2 * Edge;
            k = Math.Min(room / art.W, (IllustrationDesign.MaxHeight - 2 * Edge - 16) / art.H);
            height = Math.Clamp(Math.Max(art.H * k + 2 * Edge + 16, labelsH), MinHeight, IllustrationDesign.MaxHeight);
            canvas = Move(artLeft + (room - art.W * k) / 2, (height - art.H * k) / 2).Then(Scale(k, k)).Then(Move(-art.X, -art.Y));
            foreach (var (c, _) in callouts) (c.X, c.Y) = canvas.Apply(c.X, c.Y);
            Stack(callouts.Where(x => x.C.Side == Band.Left).Select(x => x.C).ToList(), height);
            Stack(callouts.Where(x => x.C.Side == Band.Right).Select(x => x.C).ToList(), height);
        }

        // The drawing.
        XNamespace ns = "http://www.w3.org/2000/svg";
        var svg = new XElement(ns + "svg", new XAttribute("viewBox", F($"0 0 {canvasW} {Math.Ceiling(height)}")),
            new XElement(ns + "title", plan.Title));
        var defs = new XElement(ns + "defs");
        svg.Add(defs);
        var artGroup = new XElement(ns + "g", new XAttribute("id", "art"));
        svg.Add(artGroup);
        var labelled = callouts.ToDictionary(x => x.To, x => x.C.Part, StringComparer.Ordinal);
        var used = new HashSet<string>(StringComparer.Ordinal);
        var credits = new List<string>();
        var materials = new HashSet<string>(StringComparer.Ordinal);

        var repeats = scene.Parts.GroupBy(p => p.Part.Id).Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet(StringComparer.Ordinal);
        var shared = new HashSet<string>(StringComparer.Ordinal);
        foreach (var s in scene.Shapes.Where(s => s.Under)) DrawShape(s);
        foreach (var p in scene.Parts)
        {
            if (!drawings.TryGetValue(p.As, out var drawing)) continue;
            if (used.Add(p.Part.Id))
            {
                foreach (var d in drawing.Elements(ns + "defs").SelectMany(d => d.Elements())) defs.Add(new XElement(d));
                if (p.Part.NeedsCredit && !credits.Contains(p.Part.Credit)) credits.Add(p.Part.Credit);
            }
            var m = canvas.Then(transforms[p.As]);
            // A part drawn more than once (four motors, a cell's ribosomes) is written once and copied, unless a copy
            // has labelled regions or the part copies things itself (a copy may not hold copies).
            bool copy = repeats.Contains(p.Part.Id)
                && !p.Part.Regions.Any(r => labelled.ContainsKey($"{p.As}.{r.Id}")) && !p.Part.Ports.Any(q => labelled.ContainsKey($"{p.As}.{q.Id}"))
                && !drawing.Descendants(ns + "use").Any();
            if (copy && shared.Add(p.Part.Id))
                defs.Add(new XElement(ns + "g", new XAttribute("id", p.Part.Id + "--whole"), drawing.Elements().Where(e => e.Name != ns + "defs").Select(e => new XElement(e))));
            var body = copy
                ? new XElement(ns + "g", new XAttribute("transform", Matrix(m)), new XElement(ns + "use", new XAttribute("href", "#" + p.Part.Id + "--whole")))
                : new XElement(ns + "g", new XAttribute("transform", Matrix(m)), drawing.Elements().Where(e => e.Name != ns + "defs").Select(e => new XElement(e)));
            if (copy)
                foreach (var r in body.Descendants().Where(e => ((string?)e.Attribute("id"))?.StartsWith("r-", StringComparison.Ordinal) == true)) r.SetAttributeValue("id", null);
            // Regions: the labelled ones become parts; the rest are just drawing.
            foreach (var r in body.Descendants(ns + "g").Where(g => ((string?)g.Attribute("id"))?.StartsWith("r-", StringComparison.Ordinal) == true).ToList())
            {
                string region = ((string)r.Attribute("id")!)[2..];
                if (labelled.TryGetValue($"{p.As}.{region}", out var named)) Name(r, named);
                else r.SetAttributeValue("id", null);
            }
            // A labelled region or port the drawing has no shapes for is marked where it is.
            var marks = new List<XElement>();
            foreach (var (spotId, sx, sy) in p.Part.Regions.Select(r => (r.Id, r.X, r.Y)).Concat(p.Part.Ports.Select(q => (q.Id, q.X, q.Y))))
            {
                if (!labelled.TryGetValue($"{p.As}.{spotId}", out var named) || body.Descendants().Any(e => (string?)e.Attribute("id") == named.Id)) continue;
                var at = m.Apply(sx, sy);
                var mark = new XElement(ns + "g", new XElement(ns + "circle", new XAttribute("cx", N(at.X)), new XAttribute("cy", N(at.Y)), new XAttribute("r", "4.5"),
                    new XAttribute("fill", "#F3F5F7"), new XAttribute("fill-opacity", "0.7"), new XAttribute("stroke", "#6E6E73"), new XAttribute("stroke-width", "1.2")));
                Name(mark, named);
                marks.Add(mark);
            }
            var node = marks.Count == 0 ? body : new XElement(ns + "g", body, marks);
            if (labelled.TryGetValue(p.As, out var whole))
            {
                node = new XElement(ns + "g", node);
                Name(node, whole);
            }
            artGroup.Add(node);
        }
        foreach (var s in scene.Shapes.Where(s => !s.Under)) DrawShape(s);

        void DrawShape(Shape s)
        {
            if (!shapePoints.TryGetValue(s.As, out var raw) || Material(s.Material) is not { } mat) return;
            var pts = raw.Select(pt => canvas.Apply(pt.X, pt.Y)).ToList();
            string fill = s.Kind is "band" or "arrow" ? mat.Base : $"url(#mat-{s.Material})";
            if (s.Kind is not ("band" or "arrow") && materials.Add(s.Material))
                defs.Add(new XElement(ns + "linearGradient", new XAttribute("id", "mat-" + s.Material), new XAttribute("x1", "0"), new XAttribute("y1", "0"), new XAttribute("x2", "1"), new XAttribute("y2", "1"),
                    new XElement(ns + "stop", new XAttribute("offset", "0"), new XAttribute("stop-color", mat.Light)),
                    new XElement(ns + "stop", new XAttribute("offset", "0.55"), new XAttribute("stop-color", mat.Base)),
                    new XElement(ns + "stop", new XAttribute("offset", "1"), new XAttribute("stop-color", mat.Shade))));
            var g = new XElement(ns + "g");
            double width = Math.Max(1, s.Width * k);
            switch (s.Kind)
            {
                case "ellipse":
                    g.Add(new XElement(ns + "ellipse", new XAttribute("cx", N((pts[0].X + pts[1].X) / 2)), new XAttribute("cy", N((pts[0].Y + pts[1].Y) / 2)),
                        new XAttribute("rx", N(Math.Abs(pts[1].X - pts[0].X) / 2)), new XAttribute("ry", N(Math.Abs(pts[1].Y - pts[0].Y) / 2)),
                        new XAttribute("fill", fill), new XAttribute("stroke", mat.Line), new XAttribute("stroke-width", "1.5")));
                    break;
                case "rect":
                    double rw = Math.Abs(pts[1].X - pts[0].X), rh = Math.Abs(pts[1].Y - pts[0].Y);
                    g.Add(new XElement(ns + "rect", new XAttribute("x", N(Math.Min(pts[0].X, pts[1].X))), new XAttribute("y", N(Math.Min(pts[0].Y, pts[1].Y))),
                        new XAttribute("width", N(rw)), new XAttribute("height", N(rh)), new XAttribute("rx", N(Math.Min(6, Math.Min(rw, rh) / 4))),
                        new XAttribute("fill", fill), new XAttribute("stroke", mat.Line), new XAttribute("stroke-width", "1.5")));
                    break;
                case "blob":
                    g.Add(new XElement(ns + "path", new XAttribute("d", Smooth(pts, closed: true)), new XAttribute("fill", fill), new XAttribute("stroke", mat.Line),
                        new XAttribute("stroke-width", "1.5"), new XAttribute("stroke-linejoin", "round")));
                    break;
                case "band":
                    string d = Smooth(pts, closed: false);
                    g.Add(new XElement(ns + "path", new XAttribute("d", d), new XAttribute("fill", "none"), new XAttribute("stroke", mat.Line), new XAttribute("stroke-width", N(width + 2)),
                        new XAttribute("stroke-linecap", "round"), new XAttribute("stroke-linejoin", "round")));
                    g.Add(new XElement(ns + "path", new XAttribute("d", d), new XAttribute("fill", "none"), new XAttribute("stroke", mat.Base), new XAttribute("stroke-width", N(width)),
                        new XAttribute("stroke-linecap", "round"), new XAttribute("stroke-linejoin", "round")));
                    if (width >= 4)
                        g.Add(new XElement(ns + "path", new XAttribute("d", d), new XAttribute("fill", "none"), new XAttribute("stroke", mat.Light), new XAttribute("stroke-width", N(width / 3)),
                            new XAttribute("stroke-linecap", "round"), new XAttribute("stroke-opacity", "0.7"), new XAttribute("transform", F($"translate({-width / 5:0.##} {-width / 5:0.##})"))));
                    break;
                case "arrow":
                    var (ax, ay) = pts[^2];
                    var (bx, by) = pts[^1];
                    double len = Math.Max(0.001, Math.Sqrt((bx - ax) * (bx - ax) + (by - ay) * (by - ay))), ux = (bx - ax) / len, uy = (by - ay) / len;
                    double head = Math.Max(8, width * 3);
                    var shaft = pts.Take(pts.Count - 1).Append((bx - ux * head * 0.8, by - uy * head * 0.8)).ToList();
                    g.Add(new XElement(ns + "path", new XAttribute("d", Smooth(shaft, closed: false)), new XAttribute("fill", "none"), new XAttribute("stroke", mat.Line),
                        new XAttribute("stroke-width", N(Math.Max(1.5, width))), new XAttribute("stroke-linecap", "round")));
                    g.Add(new XElement(ns + "polygon", new XAttribute("fill", mat.Line), new XAttribute("points",
                        F($"{bx:0.##},{by:0.##} {bx - ux * head - uy * head / 2:0.##},{by - uy * head + ux * head / 2:0.##} {bx - ux * head + uy * head / 2:0.##},{by - uy * head - ux * head / 2:0.##}"))));
                    break;
            }
            if (labelled.TryGetValue(s.As, out var named)) Name(g, named);
            artGroup.Add(g);
        }

        // The callouts, last, on top.
        var labelsGroup = new XElement(ns + "g", new XAttribute("id", "labels"), new XAttribute("font-size", F($"{LabelSize}")));
        svg.Add(labelsGroup);
        foreach (var (c, _) in callouts.OrderBy(x => x.C.Side).ThenBy(x => x.C.Top).ThenBy(x => x.C.TextX))
        {
            double textX, lineY = c.Top + 13;
            string anchor, points;
            if (c.Side is Band.Top or Band.Bottom)
            {
                textX = c.TextX;
                anchor = "middle";
                double endY = c.Side == Band.Top ? c.Top + c.Height + 1 : c.Top - 3;
                points = F($"{c.X:0.##},{c.Y:0.##} {textX:0.##},{endY:0.##}");
            }
            else
            {
                bool left = c.Side == Band.Left;
                textX = left ? Edge + leftW : canvasW - Edge - rightW;
                anchor = left ? "end" : "start";
                double nearX = left ? textX + 5 : textX - 5;
                double bendX = left ? Math.Min(artLeft - 6, c.X - 4) : Math.Max(artRight + 6, c.X + 4);
                points = Math.Abs(bendX - nearX) > 2 && (left ? c.X > bendX : c.X < bendX)
                    ? F($"{c.X:0.##},{c.Y:0.##} {bendX:0.##},{lineY - 4.5:0.##} {nearX:0.##},{lineY - 4.5:0.##}")
                    : F($"{c.X:0.##},{c.Y:0.##} {nearX:0.##},{lineY - 4.5:0.##}");
            }
            var g = new XElement(ns + "g", new XAttribute("id", Illustration.LabelId(c.Part.Id)),
                new XElement(ns + "polyline", new XAttribute("points", points), new XAttribute("fill", "none"), new XAttribute("stroke", "#6E6E73"), new XAttribute("stroke-width", "1")),
                new XElement(ns + "circle", new XAttribute("cx", N(c.X)), new XAttribute("cy", N(c.Y)), new XAttribute("r", "2.5"), new XAttribute("fill", "#6E6E73")),
                new XElement(ns + "text", new XAttribute("x", N(textX)), new XAttribute("y", N(lineY)), new XAttribute("fill", "#1D1D1F"),
                    new XAttribute("text-anchor", anchor), c.Part.Name));
            if (c.Fact.Length > 0)
                g.Add(new XElement(ns + "text", new XAttribute("x", N(textX)), new XAttribute("y", N(lineY + 16)), new XAttribute("fill", "#6E6E73"), new XAttribute("font-size", F($"{FactSize}")),
                    new XAttribute("text-anchor", anchor), c.Fact));
            labelsGroup.Add(g);
        }
        string text = svg.ToString(SaveOptions.DisableFormatting);
        return new Drawn(Callouts.Tidy(text), callouts.Select(x => x.C.Part.Id).ToList(), credits);
    }

    /// <summary>A group made a named part: its id, then its title and desc first, as SafeSvg reads parts.</summary>
    static void Name(XElement g, Illustration.Planned part)
    {
        XNamespace ns = g.Name.Namespace;
        g.SetAttributeValue("id", part.Id);
        foreach (var t in g.Elements().Where(e => e.Name.LocalName is "title" or "desc").ToList()) t.Remove();
        var named = new List<XElement> { new(ns + "title", part.Name) };
        if (part.Note.Length > 0) named.Add(new XElement(ns + "desc", part.Note));
        g.AddFirst(named);
    }

    /// <summary>A column of callouts, top to bottom in the order their points sit, each as near its point's height as
    /// the ones above allow, the lot pulled up if the last runs off the bottom.</summary>
    static void Stack(List<Callout> column, double height)
    {
        if (column.Count == 0) return;
        column.Sort((a, b) => a.Y.CompareTo(b.Y));
        double top = Edge, bottom = height - Edge, gap = 8;
        var ys = column.Select(c => Math.Max(top, c.Y - 9)).ToArray();
        for (int i = 1; i < ys.Length; i++) ys[i] = Math.Max(ys[i], ys[i - 1] + column[i - 1].Height + gap);
        if (ys[^1] + column[^1].Height > bottom)
        {
            ys[^1] = bottom - column[^1].Height;
            for (int i = ys.Length - 2; i >= 0; i--) ys[i] = Math.Min(ys[i], ys[i + 1] - column[i].Height - gap);
            if (ys[0] < top)
            {
                ys[0] = top;
                for (int i = 1; i < ys.Length; i++) ys[i] = Math.Max(ys[i], ys[i - 1] + column[i - 1].Height + gap);
            }
        }
        for (int i = 0; i < column.Count; i++) column[i].Top = ys[i];
    }

    /// <summary>Where a reference points, in the composer's units: a port, a region's middle, or a part's own
    /// middle; a number pair as it is.</summary>
    static (double X, double Y)? Where(Point p, Dictionary<string, SvgGeometry.Affine> transforms, Scene scene)
    {
        if (p.Ref is null) return (p.X, p.Y);
        int dot = p.Ref.IndexOf('.');
        string name = p.Ref[..dot], spot = p.Ref[(dot + 1)..];
        if (!transforms.TryGetValue(name, out var m) || scene.Parts.FirstOrDefault(x => x.As == name) is not { } placed) return null;
        if (spot == "middle") return m.Apply(placed.Part.X, placed.Part.Y);
        return Spot(placed.Part, spot) is { } s ? m.Apply(s.X, s.Y) : null;
    }

    static (double X, double Y)? Spot(LibraryPart part, string id) =>
        part.Ports.FirstOrDefault(q => q.Id == id) is { } port ? (port.X, port.Y)
        : part.Regions.FirstOrDefault(r => r.Id == id) is { } region ? (region.X, region.Y) : null;

    /// <summary>The point a label's leader ends at, in the composer's units.</summary>
    static (double X, double Y)? Target(string to, Dictionary<string, SvgGeometry.Affine> transforms, Dictionary<string, List<(double X, double Y)>> shapes, Scene scene)
    {
        int dot = to.IndexOf('.');
        string name = dot < 0 ? to : to[..dot];
        if (shapes.TryGetValue(name, out var pts))
        {
            var shape = scene.Shapes.First(s => s.As == name);
            if (shape.Kind is "band" or "arrow") return pts[pts.Count / 2];
            var b = Bounds.Around(pts)!.Value;
            return (b.CenterX, b.CenterY);
        }
        if (!transforms.TryGetValue(name, out var m) || scene.Parts.FirstOrDefault(x => x.As == name) is not { } placed) return null;
        if (dot < 0) return m.Apply(placed.Part.X, placed.Part.Y);
        return Spot(placed.Part, to[(dot + 1)..]) is { } spot ? m.Apply(spot.X, spot.Y) : null;
    }

    /// <summary>A point just inside the edge of a big shape (a cell's membrane, its cytoplasm), of eight round it the one
    /// farthest from every other label's point.</summary>
    static (double X, double Y)? Clear(string name, Dictionary<string, List<(double X, double Y)>> shapes, Scene scene, List<(double X, double Y)> taken)
    {
        if (!shapes.TryGetValue(name, out var pts) || Bounds.Around(pts) is not { } b) return null;
        var shape = scene.Shapes.First(s => s.As == name);
        var candidates = new List<(double X, double Y)>();
        for (int i = 0; i < 8; i++)
        {
            double angle = Math.PI / 4 * i + Math.PI / 8;
            if (shape.Kind == "rect")
            {
                double cx = Math.Clamp(Math.Cos(angle) * 1.5, -1, 1), cy = Math.Clamp(Math.Sin(angle) * 1.5, -1, 1);
                candidates.Add((b.CenterX + cx * b.W / 2 * 0.95, b.CenterY + cy * b.H / 2 * 0.95));
            }
            else candidates.Add((b.CenterX + Math.Cos(angle) * b.W / 2 * 0.975, b.CenterY + Math.Sin(angle) * b.H / 2 * 0.975));
        }
        foreach (var c in candidates.ToList())
            if (taken.Any(t => Math.Abs(t.X - c.X) < 1e-6 && Math.Abs(t.Y - c.Y) < 1e-6)) candidates.Remove(c);
        var best = candidates.OrderByDescending(c => taken.Count == 0 ? 0 : taken.Min(t => Math.Sqrt((t.X - c.X) * (t.X - c.X) + (t.Y - c.Y) * (t.Y - c.Y)))).FirstOrDefault();
        taken.Add(best);
        return best;
    }

    /// <summary>The box of what a label points at, in the composer's units.</summary>
    static Bounds? Extent(string to, Dictionary<string, SvgGeometry.Affine> transforms, Dictionary<string, List<(double X, double Y)>> shapes, Scene scene)
    {
        int dot = to.IndexOf('.');
        string name = dot < 0 ? to : to[..dot];
        if (shapes.TryGetValue(name, out var pts)) return Bounds.Around(pts);
        if (!transforms.TryGetValue(name, out var m) || scene.Parts.FirstOrDefault(x => x.As == name) is not { } placed) return null;
        var b = placed.Part.Box;
        return Bounds.Around(new[] { (b.X, b.Y), (b.Right, b.Y), (b.Right, b.Bottom), (b.X, b.Bottom) }.Select(c => m.Apply(c.Item1, c.Item2)).ToList());
    }

    /// <summary>A smooth path through points (Catmull-Rom as cubic curves).</summary>
    static string Smooth(List<(double X, double Y)> p, bool closed)
    {
        var sb = new StringBuilder(F($"M{p[0].X:0.##} {p[0].Y:0.##}"));
        int n = p.Count;
        if (n == 2 && !closed) return sb.Append(F($"L{p[1].X:0.##} {p[1].Y:0.##}")).ToString();
        int segments = closed ? n : n - 1;
        for (int i = 0; i < segments; i++)
        {
            var p0 = p[closed ? (i - 1 + n) % n : Math.Max(0, i - 1)];
            var p1 = p[i % n];
            var p2 = p[(i + 1) % n];
            var p3 = p[closed ? (i + 2) % n : Math.Min(n - 1, i + 2)];
            sb.Append(F($"C{p1.X + (p2.X - p0.X) / 6:0.##} {p1.Y + (p2.Y - p0.Y) / 6:0.##} {p2.X - (p3.X - p1.X) / 6:0.##} {p2.Y - (p3.Y - p1.Y) / 6:0.##} {p2.X:0.##} {p2.Y:0.##}"));
        }
        if (closed) sb.Append('Z');
        return sb.ToString();
    }

    static Bounds? Union(Bounds? a, Bounds? b) => a is { } x ? b is { } y ? x.Union(y) : x : b;

    static SvgGeometry.Affine Move(double x, double y) => new(1, 0, 0, 1, x, y);

    static SvgGeometry.Affine Scale(double x, double y) => new(x, 0, 0, y, 0, 0);

    static SvgGeometry.Affine Rotate(double degrees)
    {
        double r = degrees * Math.PI / 180, c = Math.Cos(r), s = Math.Sin(r);
        return new(c, s, -s, c, 0, 0);
    }

    static string Matrix(SvgGeometry.Affine m) => F($"matrix({m.A:0.#####} {m.B:0.#####} {m.C:0.#####} {m.D:0.#####} {m.E:0.###} {m.F:0.###})");

    static string N(double d) => d.ToString("0.##", CultureInfo.InvariantCulture);

    static string F(FormattableString s) => s.ToString(CultureInfo.InvariantCulture);
}
