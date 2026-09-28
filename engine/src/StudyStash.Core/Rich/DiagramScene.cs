namespace StudyStash.Core.Rich;

/// <summary>A point on a diagram, y down, in the diagram's own pixels.</summary>
public readonly record struct Pt(double X, double Y)
{
    public static Pt operator +(Pt a, Pt b) => new(a.X + b.X, a.Y + b.Y);
    public static Pt operator -(Pt a, Pt b) => new(a.X - b.X, a.Y - b.Y);
    public static Pt operator *(Pt a, double k) => new(a.X * k, a.Y * k);

    public double Length => Math.Sqrt(X * X + Y * Y);

    public Pt Unit => Length < 1e-9 ? new Pt(0, 0) : this * (1 / Length);

    public static double Distance(Pt a, Pt b) => (a - b).Length;
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

/// <summary>What a diagram was laid out as: a cycle on a ring, a tree, or a layered chart.</summary>
public enum SceneKind { Ring, Tree, Layered }

public enum PathVerb { Move, Line, Cubic }

/// <summary>One step of a line's path: move to <see cref="A"/>, a line to <see cref="A"/>, or a cubic curve through
/// the controls <see cref="A"/> and <see cref="B"/> to <see cref="C"/>.</summary>
public readonly record struct PathStep(PathVerb Verb, Pt A, Pt B = default, Pt C = default)
{
    public Pt End => Verb == PathVerb.Cubic ? C : A;
}

/// <summary>A box as drawn: where it sits, its outline and colour, and its words already wrapped into lines.</summary>
public sealed record SceneNode(string Id, Box Box, NodeShape Shape, Tone Tone, IReadOnlyList<string> Lines);

/// <summary>
/// An arrow as drawn. <see cref="Path"/> runs from <see cref="StartBase"/> to <see cref="Base"/>; each end's marker
/// (arrowhead, circle, cross) fills the gap from its base to its tip, which touches the box. With no marker the
/// tip and base are the same point. Words, if any, sit in <see cref="LabelBox"/>, and the line leaves a gap there.
/// </summary>
public sealed record SceneEdge(
    string From, string To, IReadOnlyList<PathStep> Path, EdgeLine Line, EdgeEnd StartEnd, EdgeEnd EndEnd,
    Pt StartTip, Pt StartBase, Pt Tip, Pt Base, IReadOnlyList<string> LabelLines, Box LabelBox);

/// <summary>A group as drawn: its box, and where its title's top-left corner sits.</summary>
public sealed record SceneGroup(string Id, string Title, Box Box, Pt TitleAt, int Depth);

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
    /// <summary>A shape's corners, for the ones drawn as polygons (a decision's rhombus, a hexagon); null otherwise.</summary>
    public static Pt[]? Corners(SceneNode n)
    {
        var b = n.Box;
        var c = b.Center;
        return n.Shape switch
        {
            NodeShape.Decision => [new Pt(c.X, b.Y), new Pt(b.Right, c.Y), new Pt(c.X, b.Bottom), new Pt(b.X, c.Y)],
            NodeShape.Hexagon => [new Pt(b.X, c.Y), new Pt(b.X + b.H / 4, b.Y), new Pt(b.Right - b.H / 4, b.Y), new Pt(b.Right, c.Y), new Pt(b.Right - b.H / 4, b.Bottom), new Pt(b.X + b.H / 4, b.Bottom)],
            _ => null,
        };
    }

    /// <summary>A rounded shape's corner radius (a circle's is half its width).</summary>
    public static double Radius(SceneNode n) => n.Shape == NodeShape.Circle ? n.Box.W / 2 : DiagramLayout.Radius(n.Shape, n.Box.H);

    /// <summary>How far a cylinder's top and bottom curve.</summary>
    public const double CylinderCap = 6;

    /// <summary>The top of the first line of a box's words, so its lines sit centred (below a cylinder's cap).</summary>
    public static double TextTop(SceneNode n) =>
        n.Box.Center.Y - Math.Max(1, n.Lines.Count) * DiagramLayout.LineHeight / 2 + (n.Shape == NodeShape.Cylinder ? CylinderCap / 2 : 0);

    /// <summary>An arrowhead's three corners: its tip, then either side of its base.</summary>
    public static Pt[] ArrowHead(Pt tip, Pt @base)
    {
        var u = (tip - @base).Unit;
        var across = new Pt(-u.Y, u.X) * (DiagramLayout.ArrowWidth / 2);
        var back = tip - u * DiagramLayout.ArrowLength;
        return [tip, back + across, back - across];
    }

    /// <summary>A cross's two strokes, centred between the marker's base and tip.</summary>
    public static (Pt, Pt, Pt, Pt) Cross(Pt tip, Pt @base)
    {
        var c = (tip + @base) * 0.5;
        var u = (tip - @base).Unit;
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
