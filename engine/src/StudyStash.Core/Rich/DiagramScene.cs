namespace StudyStash.Core.Rich;

/// <summary>A point on a diagram, y down, in the diagram's own pixels.</summary>
public readonly record struct Pt(double X, double Y)
{
    public static Pt operator +(Pt a, Pt b) => new(a.X + b.X, a.Y + b.Y);
    public static Pt operator -(Pt a, Pt b) => new(a.X - b.X, a.Y - b.Y);
    public static Pt operator *(Pt a, double k) => new(a.X * k, a.Y * k);

    public double Length => Math.Sqrt(X * X + Y * Y);

    /// <summary>The same direction, one pixel long (nothing, for no direction).</summary>
    public Pt Unit() => Length < 1e-9 ? new Pt(0, 0) : this * (1 / Length);

    public static double Distance(Pt a, Pt b) => (a - b).Length;

    public override string ToString() => string.Create(System.Globalization.CultureInfo.InvariantCulture, $"({X:0.##}, {Y:0.##})");
}

/// <summary>A rectangle on a diagram: its top-left corner and size.</summary>
public readonly record struct Box(double X, double Y, double W, double H)
{
    public double Right => X + W;
    public double Bottom => Y + H;
    public Pt Center => new(X + W / 2, Y + H / 2);

    public static Box Around(Pt center, double w, double h) => new(center.X - w / 2, center.Y - h / 2, w, h);

    public Box Inflate(double d) => new(X - d, Y - d, W + 2 * d, H + 2 * d);

    public Box Offset(double dx, double dy) => this with { X = X + dx, Y = Y + dy };

    public bool Contains(Pt p) => p.X >= X && p.X <= Right && p.Y >= Y && p.Y <= Bottom;

    public bool Contains(Box b) => b.X >= X - 1e-6 && b.Y >= Y - 1e-6 && b.Right <= Right + 1e-6 && b.Bottom <= Bottom + 1e-6;

    public bool Intersects(Box b) => X < b.Right && b.X < Right && Y < b.Bottom && b.Y < Bottom;

    public Box Union(Box b)
    {
        double x = Math.Min(X, b.X), y = Math.Min(Y, b.Y);
        return new Box(x, y, Math.Max(Right, b.Right) - x, Math.Max(Bottom, b.Bottom) - y);
    }
}

/// <summary>What a diagram was laid out as: a cycle on a ring, a tree, a layered chart, a big chart in groups laid
/// out as blocks, a sequence diagram's columns and rows, a timeline, or a mind map around its root.</summary>
public enum SceneKind { Ring, Tree, Layered, Grouped, Sequence, Timeline, Mindmap }

public enum PathVerb { Move, Line, Cubic }

/// <summary>One step of a line's path: move to <see cref="A"/>, a line to <see cref="A"/>, or a cubic curve through
/// the controls <see cref="A"/> and <see cref="B"/> to <see cref="C"/>.</summary>
public readonly record struct PathStep(PathVerb Verb, Pt A, Pt B = default, Pt C = default)
{
    public Pt End => Verb == PathVerb.Cubic ? C : A;
}

/// <summary>A box as drawn: where it sits, its outline and colour, and its words already wrapped into lines.
/// <see cref="DetailLines"/> of those lines, at the end, are the box's smaller words (a step's where or what): the box
/// has room for them at full size, so a renderer may draw them like the rest, or smaller and quieter
/// (<see cref="SceneShapes.IsDetail"/>), as the exported SVG does.</summary>
public sealed record SceneNode(string Id, Box Box, NodeShape Shape, Tone Tone, IReadOnlyList<string> Lines)
{
    public int DetailLines { get; init; }
}

/// <summary>
/// An arrow as drawn. <see cref="Path"/> runs from <see cref="StartBase"/> to <see cref="Base"/>; each end's marker
/// (arrowhead, circle, cross) fills the gap from its base to its tip, which touches the box. With no marker the
/// tip and base are the same point. Words, if any, sit in <see cref="LabelBox"/>, and the line leaves a gap there.
/// </summary>
public sealed record SceneEdge(
    string From, string To, IReadOnlyList<PathStep> Path, EdgeLine Line, EdgeEnd StartEnd, EdgeEnd EndEnd,
    Pt StartTip, Pt StartBase, Pt Tip, Pt Base, IReadOnlyList<string> LabelLines, Box LabelBox);

/// <summary>A group as drawn: its box, and its title's (top left, or top right where an arrow comes in over the
/// left; lines leave a gap there). <see cref="Depth"/> is 1 for a group inside another.</summary>
public sealed record SceneGroup(string Id, string Title, Box Box, Box TitleBox, int Depth);

/// <summary>
/// A laid-out diagram, in its own pixels at scale 1 with its top-left at (0, 0): no colours and no fonts, so the
/// same scene is drawn in either look, light or dark, on screen or as SVG.
/// </summary>
public sealed record DiagramScene(
    SceneKind Kind, ChartDirection Direction, double Width, double Height,
    IReadOnlyList<SceneNode> Nodes, IReadOnlyList<SceneEdge> Edges, IReadOnlyList<SceneGroup> Groups);

/// <summary>The outlines and end markers every renderer of a scene draws the same way (the app and the SVG).</summary>
public static class SceneShapes
{
    /// <summary>A shape's corners, for the ones drawn as polygons (a decision's rhombus, a hexagon, a parallelogram, a
    /// trapezoid), clockwise on screen; null otherwise.</summary>
    public static Pt[]? Corners(SceneNode n)
    {
        var b = n.Box;
        var c = b.Center;
        double slant = Slant(b.H);
        return n.Shape switch
        {
            NodeShape.Decision => [new Pt(c.X, b.Y), new Pt(b.Right, c.Y), new Pt(c.X, b.Bottom), new Pt(b.X, c.Y)],
            NodeShape.Hexagon => [new Pt(b.X, c.Y), new Pt(b.X + b.H / 4, b.Y), new Pt(b.Right - b.H / 4, b.Y), new Pt(b.Right, c.Y), new Pt(b.Right - b.H / 4, b.Bottom), new Pt(b.X + b.H / 4, b.Bottom)],
            NodeShape.Parallelogram => [new Pt(b.X + slant, b.Y), new Pt(b.Right, b.Y), new Pt(b.Right - slant, b.Bottom), new Pt(b.X, b.Bottom)],
            NodeShape.Trapezoid => [new Pt(b.X + slant, b.Y), new Pt(b.Right - slant, b.Y), new Pt(b.Right, b.Bottom), new Pt(b.X, b.Bottom)],
            _ => null,
        };
    }

    /// <summary>How far a parallelogram's or trapezoid's sides lean over its height.</summary>
    public static double Slant(double height) => Math.Min(18, height * 0.36);

    /// <summary>A rounded shape's corner radius (a circle's, single or double, is half its width).</summary>
    public static double Radius(SceneNode n) => n.Shape is NodeShape.Circle or NodeShape.DoubleCircle ? n.Box.W / 2 : DiagramLayout.Radius(n.Shape, n.Box.H);

    /// <summary>A double circle's inner ring (an accepting state, a state diagram's end); null for every other shape.
    /// The outer circle is drawn as a circle is, then this one inside it.</summary>
    public static Box? InnerRing(SceneNode n) => n.Shape == NodeShape.DoubleCircle ? n.Box.Inflate(-Math.Min(4, n.Box.W / 5)) : null;

    /// <summary>Whether line <paramref name="i"/> of a box's words is one of its smaller words (see
    /// <see cref="SceneNode.DetailLines"/>).</summary>
    public static bool IsDetail(SceneNode n, int i) => i >= n.Lines.Count - n.DetailLines;

    /// <summary>Smaller words: 11.5 px, regular weight, in the quieter colour arrows' words have.</summary>
    public const double DetailSize = 11.5;

    /// <summary>How far a cylinder's top and bottom curve.</summary>
    public const double CylinderCap = 6;

    /// <summary>The top of the first line of a box's words, so its lines sit centred (below a cylinder's cap).</summary>
    public static double TextTop(SceneNode n) =>
        n.Box.Center.Y - Math.Max(1, n.Lines.Count) * DiagramLayout.LineHeight / 2 + (n.Shape == NodeShape.Cylinder ? CylinderCap / 2 : 0);

    /// <summary>An arrowhead's three corners: its tip, then either side of its base.</summary>
    public static Pt[] ArrowHead(Pt tip, Pt @base)
    {
        var u = (tip - @base).Unit();
        var across = new Pt(-u.Y, u.X) * (DiagramLayout.ArrowWidth / 2);
        var back = tip - u * DiagramLayout.ArrowLength;
        return [tip, back + across, back - across];
    }

    /// <summary>A cross's two strokes, centred between the marker's base and tip.</summary>
    public static (Pt, Pt, Pt, Pt) Cross(Pt tip, Pt @base)
    {
        var c = (tip + @base) * 0.5;
        var u = (tip - @base).Unit();
        var n = new Pt(-u.Y, u.X);
        var a = (u + n) * 2.9;
        var b = (u - n) * 2.9;
        return (c - a, c + a, c - b, c + b);
    }

    /// <summary>A circle marker's centre; it is <see cref="DotRadius"/> across its half.</summary>
    public static Pt Dot(Pt tip, Pt @base) => (tip + @base) * 0.5;

    public const double DotRadius = 3.6;

    /// <summary>A line's dashes for a dotted arrow, in pixels.</summary>
    public static readonly double[] Dashes = [6, 4.5];

    /// <summary>How thick each kind of line is.</summary>
    public static double Thickness(EdgeLine line) => line == EdgeLine.Thick ? 2.5 : 1.5;
}
