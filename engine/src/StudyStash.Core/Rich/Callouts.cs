using System.Globalization;
using System.Xml;
using System.Xml.Linq;

namespace StudyStash.Core.Rich;

/// <summary>
/// Keeps an illustration's callouts readable, without asking anyone: when labels sit on top of each other or run off the
/// canvas, each column of labels (left and right of the drawing) is spaced out in the order it was drawn, kept inside
/// the canvas, and each label that moved gets a straight leader line from the same point on its part to its words'
/// near edge. Labels above or below the drawing are only brought back inside. A drawing whose callouts are already
/// fine is handed back exactly as it came.
/// </summary>
public static class Callouts
{
    /// <summary>The room kept between a column's labels, and between a word and the canvas's edge.</summary>
    public const double Gap = 7, Margin = 8;

    sealed class Label
    {
        public required XElement Group;
        public required List<XElement> Texts;
        public required Bounds Words;
        public (double X, double Y)? Anchor;
        public double Dx, Dy;
        public Bounds Moved => new(Words.X + Dx, Words.Y + Dy, Words.W, Words.H);
    }

    public static string Tidy(string svg)
    {
        XDocument doc;
        try
        {
            var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 800_000 };
            using var reader = XmlReader.Create(new StringReader(svg), settings);
            doc = XDocument.Load(reader, LoadOptions.PreserveWhitespace);
        }
        catch (XmlException)
        {
            return svg;
        }
        if (doc.Root is not { } root) return svg;
        var m = SvgGeometry.Of(root);
        var view = m.View;
        if (view.W <= 0 || view.H <= 0) return svg;

        var labels = new List<Label>();
        foreach (var g in root.Descendants().Where(e => ((string?)e.Attribute("id"))?.StartsWith("label-", StringComparison.Ordinal) == true))
        {
            var texts = m.Texts.Where(t => t.Text.Ancestors().Contains(g)).ToList();
            if (texts.Count == 0) continue;
            var box = texts.Select(t => t.Box).Aggregate((a, b) => a.Union(b));
            (double X, double Y)? anchor = null;
            double far = -1;
            foreach (var (_, points) in m.Shapes.Where(s => s.Shape.Ancestors().Contains(g)))
                foreach (var p in points)
                    if (box.Distance(p.X, p.Y) is var d && d > far)
                    {
                        far = d;
                        anchor = p;
                    }
            labels.Add(new Label { Group = g, Texts = texts.Select(t => t.Text).ToList(), Words = box, Anchor = anchor });
        }
        if (labels.Count == 0) return svg;

        bool crowded = false;
        for (int i = 0; i < labels.Count && !crowded; i++)
        {
            var w = labels[i].Words;
            if (w.X < view.X || w.Right > view.Right || w.Y < view.Y || w.Bottom > view.Bottom) crowded = true;
            for (int j = i + 1; j < labels.Count && !crowded; j++)
                if (w.Inflate(1).Overlap(labels[j].Words) > 4) crowded = true;
        }
        if (!crowded) return svg;

        // Sides: a label left or right of the middle third is in that column; one over the middle is above or below.
        double third = view.W / 3;
        var left = labels.Where(l => l.Words.CenterX < view.X + third).ToList();
        var right = labels.Where(l => l.Words.CenterX > view.Right - third).ToList();
        foreach (var l in labels)
        {
            if (l.Words.X < view.X + Margin) l.Dx = view.X + Margin - l.Words.X;
            else if (l.Words.Right > view.Right - Margin) l.Dx = view.Right - Margin - l.Words.Right;
            if (left.Contains(l) || right.Contains(l)) continue;
            if (l.Words.Y < view.Y + Margin) l.Dy = view.Y + Margin - l.Words.Y;
            else if (l.Words.Bottom > view.Bottom - Margin) l.Dy = view.Bottom - Margin - l.Words.Bottom;
        }
        Pack(left, view);
        Pack(right, view);

        foreach (var l in labels.Where(l => Math.Abs(l.Dx) > 0.01 || Math.Abs(l.Dy) > 0.01))
        {
            foreach (var t in l.Texts) Shift(t, l.Dx, l.Dy);
            if (l.Anchor is { } a) Lead(l, a);
        }
        return root.ToString(SaveOptions.DisableFormatting);
    }

    /// <summary>Spaces a column's labels out in the order they're drawn (top to bottom), each where it was unless the
    /// one above pushes it down, then the lot pulled up where the last runs off the bottom; all inside the canvas.</summary>
    static void Pack(List<Label> column, Bounds view)
    {
        if (column.Count == 0) return;
        column.Sort((a, b) => a.Words.Y.CompareTo(b.Words.Y));
        double top = view.Y + Margin, bottom = view.Bottom - Margin;
        var ys = column.Select(l => Math.Max(top, l.Words.Y)).ToArray();
        for (int i = 1; i < ys.Length; i++) ys[i] = Math.Max(ys[i], ys[i - 1] + column[i - 1].Words.H + Gap);
        if (ys[^1] + column[^1].Words.H > bottom)
        {
            ys[^1] = bottom - column[^1].Words.H;
            for (int i = ys.Length - 2; i >= 0; i--) ys[i] = Math.Min(ys[i], ys[i + 1] - column[i].Words.H - Gap);
            // A column too tall for the canvas starts at the top and runs as it must.
            if (ys[0] < top)
            {
                ys[0] = top;
                for (int i = 1; i < ys.Length; i++) ys[i] = Math.Max(ys[i], ys[i - 1] + column[i - 1].Words.H + Gap);
            }
        }
        for (int i = 0; i < column.Count; i++) column[i].Dy = ys[i] - column[i].Words.Y;
    }

    static void Shift(XElement text, double dx, double dy)
    {
        foreach (var e in text.DescendantsAndSelf().Where(e => e.Name.LocalName is "text" or "tspan"))
        {
            Move(e, "x", dx);
            Move(e, "y", dy);
        }
        // A text placed by a transform alone moves with its own translation.
        if (text.Attribute("x") is null && text.Attribute("y") is null && (Math.Abs(dx) > 0 || Math.Abs(dy) > 0))
        {
            string before = (string?)text.Attribute("transform") ?? "";
            text.SetAttributeValue("transform", F($"translate({dx:0.##} {dy:0.##}) ") + before);
        }
    }

    static void Move(XElement e, string attr, double by)
    {
        if (Math.Abs(by) < 0.01 || (string?)e.Attribute(attr) is not { } value) return;
        var numbers = SvgGeometry.Numbers(value).ToList();
        if (numbers.Count == 0) return;
        e.SetAttributeValue(attr, string.Join(" ", numbers.Select(n => F($"{n + by:0.##}"))));
    }

    /// <summary>A moved label's leader: straight from the same point on its part to the near edge of its words.</summary>
    static void Lead(Label l, (double X, double Y) anchor)
    {
        var words = l.Moved;
        double x = anchor.X < words.X ? words.X - 4 : anchor.X > words.Right ? words.Right + 4 : words.CenterX;
        double y = x == words.CenterX ? (anchor.Y < words.Y ? words.Y - 3 : words.Bottom + 3) : words.CenterY;
        var lines = l.Group.Descendants().Where(e => e.Name.LocalName is "line" or "polyline" or "path").ToList();
        var keep = lines.FirstOrDefault();
        var ns = l.Group.Name.Namespace;
        var leader = new XElement(ns + "polyline", new XAttribute("points", F($"{anchor.X:0.##},{anchor.Y:0.##} {x:0.##},{y:0.##}")), new XAttribute("fill", "none"));
        foreach (string attr in new[] { "stroke", "stroke-width", "stroke-dasharray", "stroke-linecap", "opacity" })
            if (keep?.Attribute(attr) is { } a) leader.SetAttributeValue(attr, a.Value);
        if (leader.Attribute("stroke") is null) leader.SetAttributeValue("stroke", "#6E6E73");
        if (leader.Attribute("stroke-width") is null) leader.SetAttributeValue("stroke-width", "1");
        foreach (var line in lines) line.Remove();
        l.Group.AddFirst(leader);
    }

    static string F(FormattableString s) => s.ToString(CultureInfo.InvariantCulture);
}
