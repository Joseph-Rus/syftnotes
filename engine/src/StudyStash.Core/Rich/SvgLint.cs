using System.Xml.Linq;

namespace StudyStash.Core.Rich;

/// <summary>
/// What would look wrong with an illustration, found from its markup (<see cref="SvgGeometry"/>, nothing rendered),
/// in plain words its illustrator can act on: drawn too simply; a planned part missing, too small to see or off the
/// canvas; a part with no callout; a callout's words off the edge, on top of another's, or too small; a leader line
/// that stops short of its part. Empty when nothing looks wrong (or when the drawing can't be read at all, which
/// <see cref="SafeSvg"/> says).
/// </summary>
public static class SvgLint
{
    /// <summary>Fewer shapes than this is a diagram, not an illustration.</summary>
    public const int MinShapes = 120;

    /// <summary>The smallest a callout's words may be, in the drawing's units.</summary>
    public const double MinWords = 11;

    /// <summary>How far a leader line may end from its part's box and still be pointing at it.</summary>
    public const double Reach = 14;

    public static IReadOnlyList<string> Problems(string svg, IReadOnlyList<Illustration.Planned> parts)
    {
        var cleaned = SafeSvg.Clean(svg);
        if (cleaned.Svg is null) return [];
        var root = XDocument.Parse(cleaned.Svg).Root!;
        var m = SvgGeometry.Of(root);
        var problems = new List<string>();
        var view = m.View;

        if (m.Shapes.Count < MinShapes)
            problems.Add($"It's drawn too simply: only {m.Shapes.Count} shapes. Draw each part's real outline and its features (bolts, ribs, edges, creases, highlights and shading), about 200 to 500 shapes in all.");

        var places = Illustration.Places(root, m);
        var callouts = Illustration.Callouts(root, m).GroupBy(c => c.Id).ToDictionary(g => g.Key, g => g.First());
        string Q(string s) => "“" + s + "”";
        var named = parts.ToDictionary(p => p.Id, p => p.Name);

        foreach (var p in parts)
        {
            if (!places.TryGetValue(p.Id, out var box))
            {
                problems.Add(root.Descendants().Any(e => (string?)e.Attribute("id") == p.Id)
                    ? $"The part {Q(p.Name)} (id=\"{p.Id}\") has nothing drawn in it."
                    : $"There's no group with id=\"{p.Id}\" for the part {Q(p.Name)}: wrap everything that draws it in <g id=\"{p.Id}\">.");
                continue;
            }
            if (box.W * box.H < 40 || Math.Max(box.W, box.H) < 8)
                problems.Add($"{Q(p.Name)} is too small to see or point at ({box.W:0} by {box.H:0} units): draw it larger.");
            else if (box.Overlap(view) < box.W * box.H * 0.5)
                problems.Add($"{Q(p.Name)} is drawn mostly off the canvas.");

            if (!callouts.TryGetValue(p.Id, out var c))
            {
                problems.Add($"{Q(p.Name)} has no callout: add <g id=\"label-{p.Id}\"> inside <g id=\"labels\">, with a leader line from the part and its name.");
                continue;
            }
            if (c.Anchor is not { } a)
            {
                if (box.Inflate(Reach).Overlap(c.Words) <= 0) problems.Add($"The label {Q(p.Name)} has no leader line to its part.");
            }
            else if (box.Distance(a.X, a.Y) > Reach)
                problems.Add($"The leader line for {Q(p.Name)} ends {box.Distance(a.X, a.Y):0} units from the part: end it on the part.");
        }

        foreach (var c in callouts.Values)
        {
            string name = named.GetValueOrDefault(c.Id, c.Id);
            var w = c.Words;
            var sides = new List<string>();
            if (w.X < view.X - 1) sides.Add("left");
            if (w.Right > view.Right + 1) sides.Add("right");
            if (w.Y < view.Y - 1) sides.Add("top");
            if (w.Bottom > view.Bottom + 1) sides.Add("bottom");
            if (sides.Count > 0) problems.Add($"The label {Q(name)} runs off the {string.Join(" and ", sides)} edge: move it in, or make the canvas wider.");
        }
        var list = callouts.Values.ToList();
        for (int i = 0; i < list.Count; i++)
            for (int j = i + 1; j < list.Count; j++)
                if (list[i].Words.Overlap(list[j].Words) > 4)
                    problems.Add($"The labels {Q(named.GetValueOrDefault(list[i].Id, list[i].Id))} and {Q(named.GetValueOrDefault(list[j].Id, list[j].Id))} overlap: space the labels at least 20 units apart.");

        int tiny = m.Texts.Count(t => t.Size < MinWords - 0.01);
        if (tiny > 0) problems.Add($"{tiny} {(tiny == 1 ? "piece of text is" : "pieces of text are")} smaller than {MinWords:0} units, too small to read in the notes: labels are 13 to 15.");
        return problems;
    }
}
