using System.Globalization;
using Avalonia;
using Avalonia.Media;
using StudyStash.Core.Rich;

namespace StudyStash.App.Controls.Rich;

/// <summary>The colours a diagram is drawn in: the look's ink, line, group fill, accent (and its tint), and the quiet
/// words' colour, light or dark.</summary>
sealed record DiagramPalette(bool Dark, SolidColorBrush Ink, IBrush Line, IBrush? GroupFill, Color Accent, Color? Tint, IBrush Quiet)
{
    public (Color Fill, Color Stroke) Neutral => DiagramColours.Neutral(Dark, Ink.Color);
}

/// <summary>
/// What a diagram shows on top of its picture while a student explores it (none of it on paper): which boxes and
/// arrows are lit while the rest dims (and how far the dimming has got, 0 to 1), the arrows and box picked out in the
/// accent (a pinned box's, a step's), the box the keyboard is on, the boxes whose words are hidden for recall and how
/// each recalled one went, and which boxes are folded groups.
/// </summary>
sealed class DiagramLook
{
    public IReadOnlySet<string>? Lit { get; init; }
    public IReadOnlySet<int>? LitEdges { get; init; }
    public double Dim { get; init; }
    public IReadOnlySet<int>? Accented { get; init; }
    public string? Picked { get; init; }
    public string? Ring { get; init; }
    public IReadOnlySet<string>? Hidden { get; init; }
    public IReadOnlyDictionary<string, bool>? Marks { get; init; }
    public IReadOnlySet<string>? Folded { get; init; }
    public string? GroupHover { get; init; }

    public bool Dims => Dim > 0.001 && (Lit is not null || LitEdges is not null);

    /// <summary>Nothing to show over the still picture.</summary>
    public bool IsEmpty => !Dims && Accented is not { Count: > 0 } && Picked is null && Ring is null && Hidden is not { Count: > 0 }
        && Marks is not { Count: > 0 } && Folded is not { Count: > 0 } && GroupHover is null;

    public bool NodeLit(string id) => Lit?.Contains(id) ?? false;

    public bool EdgeLit(int i) => LitEdges?.Contains(i) ?? false;
}

/// <summary>
/// Draws one laid-out scene in one palette, keeping what it makes (outlines, lines, markers and every line of words)
/// from one frame to the next, so lighting a box up as the pointer crosses it, or a step, redraws without making
/// anything again. With no <see cref="DiagramLook"/> it draws exactly the still picture the notes and paper show.
/// </summary>
sealed class DiagramPainter
{
    readonly DiagramScene scene;
    readonly DiagramPalette palette;
    readonly FontFamily family;
    readonly Dictionary<(string, double, FontWeight, IBrush), FormattedText> words = [];
    readonly Dictionary<int, (Pen Pen, StreamGeometry Path)> lines = [];
    readonly Dictionary<string, (IBrush Fill, Pen Pen)> boxColours = [];
    readonly Dictionary<string, StreamGeometry> polygons = [];
    Geometry? gaps;

    public DiagramPainter(DiagramScene scene, DiagramPalette palette, FontFamily family)
    {
        this.scene = scene;
        this.palette = palette;
        this.family = family;
    }

    public DiagramScene Scene => scene;

    public DiagramPalette Palette => palette;

    public FontFamily Family => family;

    /// <summary>
    /// Draws the scene at <paramref name="scale"/>, boxes and words on whole pixels of a screen with
    /// <paramref name="snap"/> pixels to a unit of the scene, with <paramref name="look"/> over it (or none: the still
    /// picture); without its boxes, when <paramref name="nodes"/> is false (they're drawn one at a time as they move).
    /// </summary>
    public void Draw(DrawingContext context, double scale, double snap, DiagramLook? look = null, bool nodes = true)
    {
        var scaled = context.PushTransform(Matrix.CreateScale(scale, scale));
        bool dims = look?.Dims == true;
        double faded = dims ? 1 - 0.7 * look!.Dim : 1;

        using (dims ? context.PushOpacity(1 - 0.4 * look!.Dim) : default(DrawingContext.PushedState?))
            foreach (var g in scene.Groups)
            {
                context.DrawRectangle(palette.GroupFill, null, new RoundedRect(DiagramCanvas.Snap(ToRect(g.Box), snap, false), 12));
                DrawCentred(context, g.Title, DiagramLayout.TitleSize, FontWeight.SemiBold, palette.Line, g.TitleBox.Center.X, g.TitleBox.Y, g.TitleBox.H, snap);
            }

        IDisposable? clip = null;
        if (scene.Edges.Any(e => e.LabelLines.Count > 0) || scene.Groups.Count > 0)
            clip = context.PushGeometryClip(gaps ??= Gaps());
        if (!dims)
            for (int i = 0; i < scene.Edges.Count; i++) DrawEdge(context, i, look);
        else
        {
            using (context.PushOpacity(faded))
                for (int i = 0; i < scene.Edges.Count; i++)
                    if (!look!.EdgeLit(i)) DrawEdge(context, i, look);
            for (int i = 0; i < scene.Edges.Count; i++)
                if (look!.EdgeLit(i)) DrawEdge(context, i, look);
        }
        clip?.Dispose();
        // The scale again, afresh: drawn into a bitmap at 200% (a picture of the window, as the self-test takes), Avalonia
        // puts the words and boxes that follow a clip taken off inside the same transform twice as far out.
        scaled.Dispose();
        using var _ = context.PushTransform(Matrix.CreateScale(scale, scale));

        if (!dims)
        {
            for (int i = 0; i < scene.Edges.Count; i++) DrawLabel(context, i, snap, look);
            if (nodes) foreach (var n in scene.Nodes) DrawNode(context, n, snap, look);
        }
        else
        {
            using (context.PushOpacity(faded))
            {
                for (int i = 0; i < scene.Edges.Count; i++) if (!look!.EdgeLit(i)) DrawLabel(context, i, snap, look);
                if (nodes) foreach (var n in scene.Nodes) if (!look!.NodeLit(n.Id)) DrawNode(context, n, snap, look);
            }
            for (int i = 0; i < scene.Edges.Count; i++) if (look!.EdgeLit(i)) DrawLabel(context, i, snap, look);
            foreach (var n in scene.Nodes) if (look!.NodeLit(n.Id)) DrawNode(context, n, snap, look);
        }
        if (look is not null && nodes) DrawRings(context, snap, look);
    }

    /// <summary>The lines stop short of their words and of the groups' titles, so neither needs a background.</summary>
    Geometry Gaps()
    {
        var holes = new GeometryGroup { FillRule = FillRule.NonZero };
        foreach (var e in scene.Edges.Where(e => e.LabelLines.Count > 0)) holes.Children.Add(new RectangleGeometry(ToRect(e.LabelBox.Inflate(2))));
        foreach (var g in scene.Groups) holes.Children.Add(new RectangleGeometry(ToRect(g.TitleBox.Inflate(2))));
        return new CombinedGeometry(GeometryCombineMode.Exclude, new RectangleGeometry(new Rect(-50, -50, scene.Width + 100, scene.Height + 100)), holes);
    }

    void DrawEdge(DrawingContext context, int i, DiagramLook? look)
    {
        var e = scene.Edges[i];
        var (pen, path) = Line(i);
        IBrush brush = palette.Line;
        if (look?.Accented?.Contains(i) == true)
        {
            brush = AccentBrush;
            pen = new Pen(brush, pen.Thickness + 0.75, pen.DashStyle, PenLineCap.Round, PenLineJoin.Round);
        }
        context.DrawGeometry(null, pen, path);
        Marker(context, e.EndEnd, e.Tip, e.Base, brush);
        Marker(context, e.StartEnd, e.StartTip, e.StartBase, brush);
    }

    (Pen, StreamGeometry) Line(int i)
    {
        if (lines.TryGetValue(i, out var made)) return made;
        var e = scene.Edges[i];
        var pen = new Pen(palette.Line, SceneShapes.Thickness(e.Line), e.Line == EdgeLine.Dotted ? new DashStyle(SceneShapes.Dashes.Select(d => d / SceneShapes.Thickness(e.Line)), 0) : null, PenLineCap.Round, PenLineJoin.Round);
        var path = new StreamGeometry();
        using (var g = path.Open())
        {
            bool open = false;
            foreach (var step in e.Path)
            {
                switch (step.Verb)
                {
                    case PathVerb.Move:
                        if (open) g.EndFigure(false);
                        g.BeginFigure(P(step.A), false);
                        open = true;
                        break;
                    case PathVerb.Line:
                        g.LineTo(P(step.A));
                        break;
                    default:
                        g.CubicBezierTo(P(step.A), P(step.B), P(step.C));
                        break;
                }
            }
            if (open) g.EndFigure(false);
        }
        return lines[i] = (pen, path);
    }

    SolidColorBrush? accentBrush;

    SolidColorBrush AccentBrush => accentBrush ??= new SolidColorBrush(palette.Accent);

    void DrawLabel(DrawingContext context, int i, double snap, DiagramLook? look)
    {
        var e = scene.Edges[i];
        if (e.LabelLines.Count == 0) return;
        IBrush brush = look?.Accented?.Contains(i) == true ? AccentBrush : palette.Line;
        double top = e.LabelBox.Center.Y - e.LabelLines.Count * DiagramLayout.LabelLineHeight / 2;
        for (int k = 0; k < e.LabelLines.Count; k++)
            DrawCentred(context, e.LabelLines[k], DiagramLayout.LabelSize, FontWeight.Normal, brush, e.LabelBox.Center.X, top + k * DiagramLayout.LabelLineHeight, DiagramLayout.LabelLineHeight, snap);
    }

    (IBrush Fill, Pen Pen) Colours(SceneNode n)
    {
        if (boxColours.TryGetValue(n.Id, out var made)) return made;
        var (fill, stroke) = n.Tone == Tone.None ? palette.Neutral : DiagramColours.Of(n.Tone, palette.Dark, palette.Accent, palette.Tint);
        return boxColours[n.Id] = (new SolidColorBrush(fill), new Pen(new SolidColorBrush(stroke), 1));
    }

    /// <summary>Draws one box and its words where the scene has it.</summary>
    public void DrawNode(DrawingContext context, SceneNode n, double snap, DiagramLook? look)
    {
        if (look?.Folded?.Contains(n.Id) == true)
        {
            DrawFolded(context, n, snap, look);
            return;
        }
        var (brush, pen) = Colours(n);
        var b = ToRect(n.Box);
        if (SceneShapes.Corners(n) is { } corners)
        {
            if (!polygons.TryGetValue(n.Id, out var poly))
            {
                poly = new StreamGeometry();
                using (var g = poly.Open())
                {
                    g.BeginFigure(P(corners[0]), true);
                    foreach (var c in corners.Skip(1)) g.LineTo(P(c));
                    g.EndFigure(true);
                }
                polygons[n.Id] = poly;
            }
            context.DrawGeometry(brush, pen, poly);
        }
        else if (n.Shape == NodeShape.Circle) context.DrawEllipse(brush, pen, b.Center, b.Width / 2, b.Height / 2);
        else if (n.Shape == NodeShape.Cylinder) Cylinder(context, DiagramCanvas.Snap(b, snap, true), brush, pen);
        else
        {
            var r = DiagramCanvas.Snap(b, snap, true);
            double radius = SceneShapes.Radius(n);
            context.DrawRectangle(brush, pen, new RoundedRect(r, radius));
            if (n.Shape == NodeShape.Subroutine)
            {
                context.DrawLine(pen, new Point(r.X + 8, r.Y), new Point(r.X + 8, r.Bottom));
                context.DrawLine(pen, new Point(r.Right - 8, r.Y), new Point(r.Right - 8, r.Bottom));
            }
        }
        double top = SceneShapes.TextTop(n);
        if (look?.Hidden?.Contains(n.Id) == true)
        {
            Hidden(context, n, top, snap);
            return;
        }
        for (int i = 0; i < n.Lines.Count; i++)
            DrawCentred(context, n.Lines[i], DiagramLayout.TextSize, FontWeight.Medium, palette.Ink, n.Box.Center.X, top + i * DiagramLayout.LineHeight, DiagramLayout.LineHeight, snap);
    }

    /// <summary>A folded group: a closed card in the groups' own fill, a second card's edge behind it (there's more
    /// inside), its title as a group's title is set and its count quieter, and a small plus where it opens.</summary>
    void DrawFolded(DrawingContext context, SceneNode n, double snap, DiagramLook look)
    {
        var r = DiagramCanvas.Snap(ToRect(n.Box), snap, true);
        var (fill, stroke) = palette.Neutral;
        var edge = new Pen(new SolidColorBrush(stroke), 1);
        var behind = r.Translate(new Vector(3, 3));
        context.DrawRectangle(palette.GroupFill ?? new SolidColorBrush(fill), edge, new RoundedRect(behind, 10));
        context.DrawRectangle(new SolidColorBrush(fill), edge, new RoundedRect(r, 10));
        context.DrawRectangle(palette.GroupFill, null, new RoundedRect(r.Deflate(0.5), 9.5));
        double top = SceneShapes.TextTop(n);
        bool hidden = look.Hidden?.Contains(n.Id) == true;
        for (int i = 0; i < n.Lines.Count; i++)
        {
            bool count = i == n.Lines.Count - 1 && n.Lines.Count > 1;
            if (hidden && !count) continue;
            DrawCentred(context, n.Lines[i], count ? DiagramLayout.LabelSize : DiagramLayout.TextSize, count ? FontWeight.Normal : FontWeight.SemiBold,
                count ? palette.Quiet : palette.Ink, n.Box.Center.X, top + i * DiagramLayout.LineHeight, DiagramLayout.LineHeight, snap);
        }
        if (hidden && n.Lines.Count > 1) Bar(context, n.Box.Center.X, top, Width(n.Lines[0], DiagramLayout.TextSize, FontWeight.SemiBold), snap);
        // The plus, in the corner the words leave clear (beside the shorter count).
        var c = new Point(r.Right - 11, n.Lines.Count > 1 ? r.Bottom - 11 : r.Y + 11);
        var plus = new Pen(palette.Quiet, 1.4, lineCap: PenLineCap.Round);
        context.DrawLine(plus, c + new Vector(-3.5, 0), c + new Vector(3.5, 0));
        context.DrawLine(plus, c + new Vector(0, -3.5), c + new Vector(0, 3.5));
    }

    /// <summary>Words hidden for recall: a soft bar the width of each line, where the words would be.</summary>
    void Hidden(DrawingContext context, SceneNode n, double top, double snap)
    {
        for (int i = 0; i < n.Lines.Count; i++)
            Bar(context, n.Box.Center.X, top + i * DiagramLayout.LineHeight, Math.Min(Width(n.Lines[i], DiagramLayout.TextSize, FontWeight.Medium), n.Box.W - 20), snap);
    }

    void Bar(DrawingContext context, double centreX, double top, double width, double snap)
    {
        width = Math.Max(24, width);
        var bar = new Rect(DiagramCanvas.OnPixel(centreX - width / 2, snap), DiagramCanvas.OnPixel(top + 3, snap), width, DiagramLayout.LineHeight - 6);
        context.DrawRectangle(new SolidColorBrush(palette.Ink.Color, palette.Dark ? 0.16 : 0.10), null, new RoundedRect(bar, 5));
    }

    /// <summary>The keyboard's ring round its box, the accent outline of a picked box or step, and how each recalled box
    /// went (a tick for one known, a dot for one missed).</summary>
    void DrawRings(DrawingContext context, double snap, DiagramLook look)
    {
        foreach (var n in scene.Nodes)
        {
            bool picked = n.Id == look.Picked, ring = n.Id == look.Ring;
            if (picked) Outline(context, n, 2.5, 2, AccentBrush);
            if (ring && !picked) Outline(context, n, 3.5, 2, AccentBrush);
            else if (ring) Outline(context, n, 6, 1.25, AccentBrush);
            if (look.Marks?.TryGetValue(n.Id, out bool knew) == true) Mark(context, n, knew);
        }
    }

    void Outline(DrawingContext context, SceneNode n, double outset, double thickness, IBrush brush)
    {
        var pen = new Pen(brush, thickness);
        var b = ToRect(n.Box.Inflate(outset));
        if (n.Shape == NodeShape.Circle) context.DrawEllipse(null, pen, b.Center, b.Width / 2, b.Height / 2);
        else if (n.Shape is NodeShape.Decision or NodeShape.Hexagon && SceneShapes.Corners(n with { Box = n.Box.Inflate(outset) }) is { } corners)
        {
            var g = new StreamGeometry();
            using (var c = g.Open())
            {
                c.BeginFigure(P(corners[0]), false);
                foreach (var k in corners.Skip(1)) c.LineTo(P(k));
                c.EndFigure(true);
            }
            context.DrawGeometry(null, pen, g);
        }
        else context.DrawRectangle(null, pen, new RoundedRect(b, SceneShapes.Radius(n) + outset));
    }

    void Mark(DrawingContext context, SceneNode n, bool knew)
    {
        var at = new Point(n.Box.Right - 2, n.Box.Y + 2);
        var (fill, stroke) = DiagramColours.Of(knew ? Tone.Green : Tone.Amber, palette.Dark, palette.Accent, palette.Tint);
        context.DrawEllipse(new SolidColorBrush(stroke), new Pen(new SolidColorBrush(fill), 1.5), at, 7, 7);
        var pen = new Pen(new SolidColorBrush(fill), 1.6, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
        if (knew)
        {
            var tick = new StreamGeometry();
            using (var g = tick.Open())
            {
                g.BeginFigure(at + new Vector(-3.2, 0.2), false);
                g.LineTo(at + new Vector(-0.9, 2.4));
                g.LineTo(at + new Vector(3.2, -2.2));
                g.EndFigure(false);
            }
            context.DrawGeometry(null, pen, tick);
        }
        else context.DrawLine(pen, at + new Vector(-2.8, 0), at + new Vector(2.8, 0));
    }

    static void Cylinder(DrawingContext context, Rect b, IBrush fill, Pen pen)
    {
        double cap = SceneShapes.CylinderCap, rx = b.Width / 2;
        var body = new StreamGeometry();
        using (var g = body.Open())
        {
            g.BeginFigure(new Point(b.X, b.Y + cap), true);
            g.LineTo(new Point(b.X, b.Bottom - cap));
            g.ArcTo(new Point(b.Right, b.Bottom - cap), new Size(rx, cap), 0, false, SweepDirection.CounterClockwise);
            g.LineTo(new Point(b.Right, b.Y + cap));
            g.ArcTo(new Point(b.X, b.Y + cap), new Size(rx, cap), 0, false, SweepDirection.CounterClockwise);
            g.EndFigure(true);
        }
        context.DrawGeometry(fill, pen, body);
        var rim = new StreamGeometry();
        using (var g = rim.Open())
        {
            g.BeginFigure(new Point(b.X, b.Y + cap), false);
            g.ArcTo(new Point(b.Right, b.Y + cap), new Size(rx, cap), 0, false, SweepDirection.CounterClockwise);
            g.EndFigure(false);
        }
        context.DrawGeometry(null, pen, rim);
    }

    static void Marker(DrawingContext context, EdgeEnd end, Pt tip, Pt @base, IBrush brush)
    {
        if (end == EdgeEnd.None || Pt.Distance(tip, @base) < 0.01) return;
        switch (end)
        {
            case EdgeEnd.Arrow:
                var head = SceneShapes.ArrowHead(tip, @base);
                var geometry = new StreamGeometry();
                using (var g = geometry.Open())
                {
                    g.BeginFigure(P(head[0]), true);
                    g.LineTo(P(head[1]));
                    g.LineTo(P(head[2]));
                    g.EndFigure(true);
                }
                context.DrawGeometry(brush, null, geometry);
                break;
            case EdgeEnd.Circle:
                context.DrawEllipse(brush, null, P(SceneShapes.Dot(tip, @base)), SceneShapes.DotRadius, SceneShapes.DotRadius);
                break;
            case EdgeEnd.Cross:
                var (a, b, c, d) = SceneShapes.Cross(tip, @base);
                var pen = new Pen(brush, 1.5, lineCap: PenLineCap.Round);
                context.DrawLine(pen, P(a), P(b));
                context.DrawLine(pen, P(c), P(d));
                break;
        }
    }

    void DrawCentred(DrawingContext context, string text, double size, FontWeight weight, IBrush brush, double centreX, double top, double lineHeight, double snap)
    {
        var t = Text(text, size, weight, brush);
        double x = centreX - t.WidthIncludingTrailingWhitespace / 2, y = top + (lineHeight - t.Height) / 2;
        context.DrawText(t, new Point(DiagramCanvas.OnPixel(x, snap), DiagramCanvas.OnPixel(y, snap)));
    }

    double Width(string text, double size, FontWeight weight) => Text(text, size, weight, palette.Ink).WidthIncludingTrailingWhitespace;

    FormattedText Text(string text, double size, FontWeight weight, IBrush brush)
    {
        var key = (text, size, weight, brush);
        if (words.TryGetValue(key, out var made)) return made;
        return words[key] = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface(family, FontStyle.Normal, weight), size, brush);
    }

    static Point P(Pt p) => new(p.X, p.Y);

    static Rect ToRect(Box b) => new(b.X, b.Y, b.W, b.H);
}
