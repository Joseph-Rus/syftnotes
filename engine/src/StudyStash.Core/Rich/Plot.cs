using System.Globalization;
using System.Text;

namespace StudyStash.Core.Rich;

/// <summary>How a line in a plot is drawn.</summary>
public enum PlotDash { Solid, Dashed, Dotted }

/// <summary>How a sequence is drawn: bars, stems with a dot, dots alone, or a staircase.</summary>
public enum SeriesStyle { Bars, Stems, Dots, Steps }

/// <summary>
/// A label in a plot: words, with any <c>{…}</c> in them worked out live (<c>"P = {area}"</c>, <c>"slope {slope}"</c>,
/// <c>"k = {k}"</c>), to three significant figures.
/// </summary>
public sealed class PlotLabel
{
    readonly List<(string Text, PlotExpression? Expr)> pieces;

    PlotLabel(string written, List<(string, PlotExpression?)> pieces)
    {
        Written = written;
        this.pieces = pieces;
    }

    /// <summary>The label as written (its {…} unworked).</summary>
    public string Written { get; }

    /// <summary>Whether it has anything to work out.</summary>
    public bool Live => pieces.Any(p => p.Expr is not null);

    public static PlotLabel Read(string written, PlotScope scope, IReadOnlyDictionary<string, int>? locals)
    {
        var pieces = new List<(string, PlotExpression?)>();
        int i = 0;
        while (i < written.Length)
        {
            int open = written.IndexOf('{', i);
            if (open < 0)
            {
                pieces.Add((written[i..], null));
                break;
            }
            int close = written.IndexOf('}', open);
            if (close < 0) throw new PlotException($"a {{ never closed in the label “{written}”");
            if (open > i) pieces.Add((written[i..open], null));
            pieces.Add(("", PlotExprReader.Read(written[(open + 1)..close], scope, locals)));
            i = close + 1;
        }
        return new PlotLabel(written, pieces);
    }

    /// <summary>The words, with each {…} worked out now.</summary>
    public string Text(PlotEnv env)
    {
        if (!Live) return Written;
        var sb = new StringBuilder();
        foreach (var (text, expr) in pieces) sb.Append(expr is null ? text : PlotNumber.Format(expr.Eval(env)));
        return sb.ToString();
    }

    /// <summary>The words without what's worked out (search reads these).</summary>
    public string Plain => string.Concat(pieces.Select(p => p.Expr is null ? p.Text : p.Expr.Text));
}

/// <summary>Numbers as a plot writes them: three significant figures, a real minus sign, ×10ⁿ for the very large and
/// very small.</summary>
public static class PlotNumber
{
    public static string Format(double v, int figures = 3)
    {
        if (double.IsNaN(v)) return "undefined";
        if (double.IsPositiveInfinity(v)) return "∞";
        if (double.IsNegativeInfinity(v)) return "−∞";
        if (v == 0) return "0";
        double a = Math.Abs(v);
        string s;
        if (a >= 1e6 || a < 1e-4)
        {
            int exp = (int)Math.Floor(Math.Log10(a));
            double mant = v / Math.Pow(10, exp);
            if (Math.Abs(Math.Round(mant, figures - 1)) >= 10)
            {
                exp++;
                mant /= 10;
            }
            s = Trim(Math.Round(mant, figures - 1).ToString("F" + (figures - 1), CultureInfo.InvariantCulture)) + "×10" + Super(exp);
        }
        else
        {
            int digits = Math.Max(0, figures - 1 - (int)Math.Floor(Math.Log10(a)));
            s = Trim(Math.Round(v, Math.Min(digits, 15)).ToString("F" + Math.Min(digits, 15), CultureInfo.InvariantCulture));
        }
        return s.Replace('-', '−');
    }

    static string Trim(string s) => s.Contains('.') ? s.TrimEnd('0').TrimEnd('.') : s;

    public static string Super(int n) => string.Concat(n.ToString(CultureInfo.InvariantCulture).Select(c => c switch
    {
        '-' => '⁻', '0' => '⁰', '1' => '¹', '2' => '²', '3' => '³', '4' => '⁴', '5' => '⁵', '6' => '⁶', '7' => '⁷', '8' => '⁸', _ => '⁹',
    }));

    /// <summary>A number as the plot's source writes it (shortest exact form).</summary>
    public static string Source(double v) => v.ToString("R", CultureInfo.InvariantCulture);
}

/// <summary>An axis: its range (expressions of numbers, kept as written: <c>-2pi to 2pi</c>), its label, and whether
/// its ticks are multiples of π.</summary>
public sealed class PlotAxis
{
    public required PlotExpression Min { get; init; }
    public required PlotExpression Max { get; init; }
    public string? Label { get; init; }
    public bool PiTicks { get; init; }
    public double From => Min.Constant ?? double.NaN;
    public double To => Max.Constant ?? double.NaN;
}

/// <summary>A slider: its name, where it starts, its range and step, and what it means.</summary>
public sealed class PlotParam
{
    public required string Name { get; init; }
    public required int Slot { get; init; }
    public required double Default { get; init; }
    public required double Min { get; init; }
    public required double Max { get; init; }
    /// <summary>The step it moves in (0: smooth); a whole-number step when the source asks for one.</summary>
    public double Step { get; init; }
    public string? Label { get; init; }
    internal string DefaultText { get; init; } = "";
    internal string MinText { get; init; } = "";
    internal string MaxText { get; init; } = "";
    internal string? StepText { get; init; }

    /// <summary>A value the slider can hold: in range, on its step.</summary>
    public double Snap(double v)
    {
        if (!double.IsFinite(v)) return Default;
        v = Math.Clamp(v, Min, Max);
        if (Step > 0) v = Math.Clamp(Min + Math.Round((v - Min) / Step) * Step, Min, Max);
        return v;
    }
}

/// <summary>Something a plot draws, from one of its lines: <see cref="Line"/> is that line's number (from 1).</summary>
public abstract class PlotItem
{
    public int Line { get; internal set; }
    public PlotLabel? Label { get; internal set; }
    /// <summary>The colour asked for (0 to 4: blue, orange, green, purple, pink; 5 red; -2 grey), or null to take the
    /// next one in turn.</summary>
    public int? Colour { get; internal set; }
    public PlotDash Dash { get; internal set; }
    internal string Canonical { get; set; } = "";
    /// <summary>The words the student sees for it, and what search reads.</summary>
    public virtual string Name => Label?.Plain ?? "";
}

/// <summary>A curve y = f(x) (or f(z), in the lecture's own letter), on part of the axis if it says so.</summary>
public sealed class PlotCurve : PlotItem
{
    public required PlotFunction Function { get; init; }
    public required string Variable { get; init; }
    /// <summary>Its name when it has one (f, σ, L), so others can use it; null for y = ….</summary>
    public string? Named { get; init; }
    public PlotExpression Body { get; internal set; } = null!;
    public PlotExpression? From { get; internal set; }
    public PlotExpression? To { get; internal set; }
    /// <summary>A flat line (y = 0.5): a reference, drawn quietly and not counted as a curve.</summary>
    public bool Flat { get; internal set; }
    public override string Name => Label?.Plain ?? (Named is { } n ? $"{n}({Variable})" : "y = " + Body.Text);
}

/// <summary>A vertical line x = c (an asymptote, a threshold, the mean).</summary>
public sealed class PlotVLine : PlotItem
{
    public PlotExpression X { get; internal set; } = null!;
    public override string Name => Label?.Plain ?? "x = " + X.Text;
}

/// <summary>A parametric curve (x(t), y(t)) as t runs over its range.</summary>
public sealed class PlotParametric : PlotItem
{
    public required string Variable { get; init; }
    public required int Slot { get; init; }
    public PlotExpression X { get; internal set; } = null!;
    public PlotExpression Y { get; internal set; } = null!;
    public PlotExpression From { get; internal set; } = null!;
    public PlotExpression To { get; internal set; } = null!;
    public override string Name => Label?.Plain ?? $"({X.Text}, {Y.Text})";
}

/// <summary>A sequence at whole numbers (a distribution's probabilities, a recurrence's terms), drawn as bars, stems,
/// dots or steps.</summary>
public sealed class PlotSeries : PlotItem
{
    public required SeriesStyle Style { get; init; }
    public required PlotFunction Function { get; init; }
    public required string Variable { get; init; }
    public PlotExpression Body { get; internal set; } = null!;
    public override string Name => Label?.Plain ?? $"{Function.Name}({Variable})";
}

/// <summary>Points from data (a lecturer's few measurements).</summary>
public sealed class PlotPoints : PlotItem
{
    public List<(PlotExpression X, PlotExpression Y)> Points { get; } = [];
    public override string Name => Label?.Plain ?? "data";
}

/// <summary>One marked point with its label; one whose x is a slider can be dragged along.</summary>
public sealed class PlotPoint : PlotItem
{
    public PlotExpression X { get; internal set; } = null!;
    public PlotExpression Y { get; internal set; } = null!;
}

/// <summary>Words at a place in the plot.</summary>
public sealed class PlotText : PlotItem
{
    public PlotExpression X { get; internal set; } = null!;
    public PlotExpression Y { get; internal set; } = null!;
}

/// <summary>The area under a curve, or between two, from a to b; its label can say the area ({area}).</summary>
public sealed class PlotShade : PlotItem
{
    public required PlotCurve Under { get; init; }
    public PlotCurve? Over { get; init; }
    public PlotExpression From { get; internal set; } = null!;
    public PlotExpression To { get; internal set; } = null!;
    public required int AreaSlot { get; init; }
}

/// <summary>The tangent to a curve at a point (its label can say the slope, {slope}); dragging the point moves it
/// when the point is a slider.</summary>
public sealed class PlotTangent : PlotItem
{
    public required PlotCurve Of { get; init; }
    public PlotExpression At { get; internal set; } = null!;
    public required int SlopeSlot { get; init; }
}

/// <summary>The secant through a curve at two points ({slope}).</summary>
public sealed class PlotSecant : PlotItem
{
    public required PlotCurve Of { get; init; }
    public PlotExpression A { get; internal set; } = null!;
    public PlotExpression B { get; internal set; } = null!;
    public required int SlopeSlot { get; init; }
}

/// <summary>An arrow from one point (the origin unless it says) to another.</summary>
public sealed class PlotVector : PlotItem
{
    public PlotExpression? X0 { get; internal set; }
    public PlotExpression? Y0 { get; internal set; }
    public PlotExpression X { get; internal set; } = null!;
    public PlotExpression Y { get; internal set; } = null!;
}

/// <summary>A vector field (P(x, y), Q(x, y)), as arrows over a grid.</summary>
public sealed class PlotField : PlotItem
{
    public PlotExpression P { get; internal set; } = null!;
    public PlotExpression Q { get; internal set; } = null!;
    public required int XSlot { get; init; }
    public required int YSlot { get; init; }
}

/// <summary>A function of two variables (a loss surface): a heat map with contour lines, or contour lines alone.</summary>
public sealed class PlotHeat : PlotItem
{
    public required PlotFunction Function { get; init; }
    public required bool Fill { get; init; }
    public PlotExpression Body { get; internal set; } = null!;
    public override string Name => Label?.Plain ?? $"{Function.Name}({string.Join(", ", Variables)})";
    public required string[] Variables { get; init; }
}

/// <summary>Gradient descent on a curve or a surface: from a start, a step of rate × the slope, so many times.</summary>
public sealed class PlotDescent : PlotItem
{
    public required PlotFunction On { get; init; }
    public PlotExpression X0 { get; internal set; } = null!;
    public PlotExpression? Y0 { get; internal set; }
    public PlotExpression Rate { get; internal set; } = null!;
    public PlotExpression Steps { get; internal set; } = null!;
    public override string Name => Label?.Plain ?? "gradient descent";
}

/// <summary>A 2×2 matrix as the linear map it is: the unit grid and square before and after, where the basis
/// vectors go, its eigenvectors (when they're real), and {det}. With morph, a slider takes the plane from the
/// identity to the matrix.</summary>
public sealed class PlotMatrix : PlotItem
{
    public PlotExpression[] Entries { get; } = new PlotExpression[4];
    public bool Morph { get; init; }
    public required int DetSlot { get; init; }
    /// <summary>The slider morph adds (0: the identity, 1: the matrix).</summary>
    public PlotParam? MorphParam { get; internal set; }
    public override string Name => Label?.Plain ?? "the matrix";
}

/// <summary>
/// A plot in a lecture's notes, read from a ```plot block: what the lecture's formula looks like, drawn exactly from
/// its expression (the AI only writes the expression, the ranges and the labels). One statement a line:
/// <code>
/// title The sigmoid squashes any score into (0, 1)
/// x -6 to 6 "z"
/// y 0 to 1 "σ(z)"
/// param k = 1 from 0.2 to 5 "steepness"
/// σ(z) = 1 / (1 + e^(-k z)) "σ(kz)"
/// y = 0.5 dashed "threshold"
/// point (0, 0.5) "σ(0) = ½"
/// </code>
/// Read strictly: anything it can't read says which line and why (<see cref="PlotException"/>), so the AI that wrote
/// it can be told. <see cref="ToSource"/> writes it back in one canonical form (expressions as written).
/// </summary>
public sealed class Plot
{
    public const int MaxLines = 80, MaxParams = 8, MaxItems = 24, MaxFunctions = 30, MaxLabel = 80;

    public string? Title { get; private set; }
    public PlotAxis? X { get; private set; }
    public PlotAxis? Y { get; private set; }
    public bool Equal { get; private set; }
    public List<string> Comments { get; } = [];
    public List<PlotParam> Params { get; } = [];
    public List<PlotItem> Items { get; } = [];
    public PlotScope Scope { get; } = new();

    /// <summary>The variable the x axis is: the letter its curves are written in (z for σ(z)), x if they differ.</summary>
    public string XName { get; private set; } = "x";

    /// <summary>Equal units on both axes (a matrix, a circle, vectors): its height follows from its width.</summary>
    public bool Square => Equal || Items.Any(i => i is PlotMatrix or PlotVector or PlotField);

    /// <summary>A plot with sliders and a function of two variables: worth drawing at a lower resolution while a
    /// slider moves.</summary>
    public bool Heavy => Items.Any(i => i is PlotHeat or PlotField);

    // --- reading -----------------------------------------------------------------------------------------------------

    public static Plot Parse(string source)
    {
        var plot = new Plot();
        plot.Read(source ?? "");
        return plot;
    }

    /// <summary>Why <paramref name="source"/> can't be drawn as a plot, or null when it can.</summary>
    public static string? Problem(string source)
    {
        try
        {
            Parse(source);
            return null;
        }
        catch (PlotException e)
        {
            return e.Message;
        }
    }

    sealed record Pending(int Line, Action Resolve);

    /// <summary>The labels, read once every name in the plot is known (one may use a curve defined below it).</summary>
    readonly List<Pending> labels = [];

    int lineNumber;

    void Read(string source)
    {
        string[] lines = NoteBlocks.Lines(source);
        if (lines.Length > MaxLines * 2) throw new PlotException($"more than {MaxLines} lines");
        var pending = new List<Pending>();
        int statements = 0;
        for (int n = 0; n < lines.Length; n++)
        {
            string raw = lines[n].Trim();
            if (raw.Length == 0) continue;
            if (raw.StartsWith("%%", StringComparison.Ordinal))
            {
                if (Items.Count == 0 && Params.Count == 0) Comments.Add(raw);
                continue;
            }
            if (++statements > MaxLines) throw new PlotException($"more than {MaxLines} lines");
            if (statements == 1 && raw is "plot" or "```plot") continue;
            try
            {
                lineNumber = n + 1;
                Statement(raw, n + 1, pending);
            }
            catch (PlotException e) when (!e.Message.StartsWith("Line ", StringComparison.Ordinal))
            {
                throw new PlotException($"Line {n + 1}: {e.Message}");
            }
        }
        foreach (var p in pending.Concat(labels))
        {
            try
            {
                p.Resolve();
            }
            catch (PlotException e) when (!e.Message.StartsWith("Line ", StringComparison.Ordinal))
            {
                throw new PlotException($"Line {p.Line}: {e.Message}");
            }
        }
        Check();
    }

    /// <summary>One line of the plot: what it starts with says what it is.</summary>
    void Statement(string line, int number, List<Pending> pending)
    {
        var t = PlotLexer.Lex(line);
        if (t.Count == 0) return;
        var first = t[0];
        string head = first.Kind == PlotTokenKind.Name ? first.Text : "";
        bool assigns = t.Count > 1 && t[1].Is("=");
        switch (head)
        {
            case "title":
                if (Title is not null) throw new PlotException("a second title");
                string title = line[first.End..].Trim().Trim('"', '“', '”').Trim();
                if (title.Length == 0) throw new PlotException("a title with no words");
                Title = Limit(title, "title");
                return;
            case "x" or "y" when !assigns && !(t.Count > 1 && t[1].Is("(")):
                Axis(line, t, head == "x");
                return;
            case "x" when assigns:
                VLine(line, t, number, pending);
                return;
            case "y" when assigns:
                Curve(line, t, number, pending, named: null, variable: "x", bodyFrom: 2);
                return;
            case "param" or "slider":
                Param(line, t);
                return;
            case "curve":
                Parametric(line, t, number, pending);
                return;
            case "bars" or "stems" or "dots" or "steps":
                Series(line, t, number, pending, head);
                return;
            case "points":
                Points(line, t, number, pending);
                return;
            case "point" or "label":
                PointOrText(line, t, number, pending, head == "label");
                return;
            case "shade":
                Shade(line, t, number, pending);
                return;
            case "tangent" or "secant":
                TangentOrSecant(line, t, number, pending, head == "tangent");
                return;
            case "vector" or "arrow":
                Vector(line, t, number, pending);
                return;
            case "field":
                Field(line, t, number, pending);
                return;
            case "heat" or "heatmap" or "contour":
                Heat(line, t, number, pending, fill: head != "contour");
                return;
            case "descent":
                Descent(line, t, number, pending);
                return;
            case "matrix":
                Matrix(line, t, number, pending);
                return;
            case "equal" when t.Count == 1:
                Equal = true;
                return;
        }
        // f(x) = …: a named curve.
        if (first.Kind == PlotTokenKind.Name && t.Count > 4 && t[1].Is("(") && t[2].Kind == PlotTokenKind.Name && t[3].Is(")") && t[4].Is("="))
        {
            Curve(line, t, number, pending, named: first.Text, variable: t[2].Text, bodyFrom: 5);
            return;
        }
        throw new PlotException($"didn't understand “{Short(line)}” (a plot's lines start with title, x, y, param, a curve such as f(x) = …, curve, bars, points, point, label, shade, tangent, secant, vector, field, heat, contour, descent or matrix)");
    }

    static string Short(string line) => line.Length <= 60 ? line : line[..57] + "…";

    static string Limit(string words, string what)
    {
        if (words.Length > MaxLabel) throw new PlotException($"a {what} longer than {MaxLabel} characters");
        return words;
    }

    // --- the pieces of a line ------------------------------------------------------------------------------------------

    /// <summary>The token closing the bracket at <paramref name="open"/>.</summary>
    static int Close(List<PlotToken> t, int open)
    {
        int depth = 0;
        for (int i = open; i < t.Count; i++)
        {
            if (t[i].Kind != PlotTokenKind.Op) continue;
            if (t[i].Text is "(" or "[" or "{") depth++;
            else if (t[i].Text is ")" or "]" or "}" && --depth == 0) return i;
        }
        throw new PlotException("a bracket that's never closed");
    }

    /// <summary>Where an expression starting at <paramref name="from"/> ends: at a label, at a word of the plot's
    /// own (from, to, a colour…), or at a comma, outside any brackets.</summary>
    static int ExprEnd(List<PlotToken> t, int from, bool comma = false)
    {
        int depth = 0;
        for (int i = from; i < t.Count; i++)
        {
            var k = t[i];
            if (k.Kind == PlotTokenKind.Op)
            {
                if (k.Text is "(" or "[" or "{") depth++;
                else if (k.Text is ")" or "]" or "}")
                {
                    if (depth == 0) return i;
                    depth--;
                }
                else if (depth == 0 && comma && k.Text == ",") return i;
                continue;
            }
            if (depth > 0) continue;
            if (k.Kind == PlotTokenKind.Text || (k.Kind == PlotTokenKind.Name && PlotSyntax.StopWords.Contains(k.Text))) return i;
        }
        return t.Count;
    }

    static void Expect(List<PlotToken> t, int i, string what)
    {
        if (i >= t.Count) throw new PlotException($"a missing “{what}” at the end");
        bool ok = what is "(" or ")" or "," or "=" or "[" or "]" ? t[i].Is(what) : t[i].IsWord(what);
        if (!ok) throw new PlotException($"expected “{what}” but found “{t[i].Text}”");
    }

    /// <summary>A (a, b) pair from <paramref name="i"/>: the two expressions' token ranges, and where it ends.</summary>
    static ((int, int) A, (int, int) B, int Next) Pair(List<PlotToken> t, int i)
    {
        Expect(t, i, "(");
        int close = Close(t, i);
        int comma = ExprEnd(t, i + 1, comma: true);
        if (comma >= close || !t[comma].Is(",")) throw new PlotException("a point needs two numbers: (x, y)");
        int second = ExprEnd(t, comma + 1, comma: true);
        if (second != close) throw new PlotException("a point needs exactly two numbers: (x, y)");
        return ((i + 1, comma), (comma + 1, close), close + 1);
    }

    /// <summary>"from a to b" at <paramref name="i"/>: the two ranges and where it ends.</summary>
    static ((int, int) From, (int, int) To, int Next) Range(List<PlotToken> t, int i, string first = "from")
    {
        Expect(t, i, first);
        int a = ExprEnd(t, i + 1);
        if (a >= t.Count || !(t[a].IsWord("to") || t[a].Is(".."))) throw new PlotException($"“{first} … to …” without its “to”");
        int b = ExprEnd(t, a + 1);
        return ((i + 1, a), (a + 1, b), b);
    }

    static string Text(string line, List<PlotToken> t, (int From, int To) r) =>
        r.To > r.From ? PlotExprReader.Text(line, t, r.From, r.To) : throw new PlotException("a number or expression is missing");

    PlotExpression Expr(string line, List<PlotToken> t, (int From, int To) r, IReadOnlyDictionary<string, int>? locals = null) =>
        PlotExprReader.Read(line, t, r.From, r.To, Scope, locals);

    /// <summary>A number that can't depend on anything (an axis's end, a slider's range): an expression of numbers
    /// only, like 2pi.</summary>
    double Number(string line, List<PlotToken> t, (int From, int To) r, string what)
    {
        var e = PlotExprReader.Read(line, t, r.From, r.To, new PlotScope());
        if (e.Constant is not double v || !double.IsFinite(v)) throw new PlotException($"{what} must be a plain number, not “{e.Text}”");
        return v;
    }

    /// <summary>What can follow a line's main part: its "label", a colour, dashed or dotted, and the words in
    /// <paramref name="extra"/>. Returns the extra words given.</summary>
    HashSet<string> Options(string line, List<PlotToken> t, int i, PlotItem? item, IReadOnlyDictionary<string, int>? locals = null, params string[] extra)
    {
        var words = new HashSet<string>();
        var canon = new StringBuilder();
        string? label = null, colour = null, dash = null;
        for (; i < t.Count; i++)
        {
            var k = t[i];
            if (k.Kind == PlotTokenKind.Text)
            {
                if (label is not null) throw new PlotException("two labels on one line");
                label = Limit(k.Text.Trim(), "label");
                continue;
            }
            if (k.Kind == PlotTokenKind.Name)
            {
                int? c = k.Text switch
                {
                    "blue" => 0, "orange" => 1, "green" => 2, "purple" => 3, "pink" => 4, "red" => 5, "grey" or "gray" => -2, _ => null,
                };
                if (c is int colourIndex && item is not null)
                {
                    if (colour is not null) throw new PlotException("two colours on one line");
                    colour = k.Text == "gray" ? "grey" : k.Text;
                    item.Colour = colourIndex;
                    continue;
                }
                if (k.Text is "dashed" or "dotted" && item is not null)
                {
                    dash = k.Text;
                    item.Dash = k.Text == "dashed" ? PlotDash.Dashed : PlotDash.Dotted;
                    continue;
                }
                if (extra.Contains(k.Text))
                {
                    words.Add(k.Text);
                    continue;
                }
            }
            throw new PlotException($"didn't expect “{k.Text}” there");
        }
        if (item is not null)
        {
            if (label is not null)
            {
                labels.Add(new Pending(lineNumber, () => item.Label = PlotLabel.Read(label, Scope, locals)));
                canon.Append(" \"").Append(label).Append('"');
            }
            if (colour is not null) canon.Append(' ').Append(colour);
            if (dash is not null) canon.Append(' ').Append(dash);
            foreach (string w in extra.Where(words.Contains)) canon.Append(' ').Append(w);
            item.Canonical += canon.ToString();
        }
        return words;
    }

    void Add(PlotItem item, int number)
    {
        if (Items.Count >= MaxItems) throw new PlotException($"more than {MaxItems} things to draw");
        item.Line = number;
        Items.Add(item);
    }

    void NewName(string name, string what)
    {
        if (PlotScope.Reserved(name)) throw new PlotException($"“{name}” can't be the name of a {what} (it's a word plots already use)");
        if (Scope.Params.ContainsKey(name) || Scope.Functions.ContainsKey(name)) throw new PlotException($"two things named “{name}”");
        if (Scope.Functions.Count >= MaxFunctions) throw new PlotException($"more than {MaxFunctions} named curves");
    }

    Dictionary<string, int> Locals(params string[] names)
    {
        var d = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (string n in names) d[n] = Scope.Variable(n);
        return d;
    }

    // --- each kind of line ---------------------------------------------------------------------------------------------

    void Axis(string line, List<PlotToken> t, bool x)
    {
        if (x ? X is not null : Y is not null) throw new PlotException($"a second {(x ? "x" : "y")} axis");
        int a = ExprEnd(t, 1);
        if (a >= t.Count || !(t[a].IsWord("to") || t[a].Is(".."))) throw new PlotException($"an axis is written “{(x ? "x" : "y")} -5 to 5 \"label\"”");
        int b = ExprEnd(t, a + 1);
        double lo = Number(line, t, (1, a), "an axis's end"), hi = Number(line, t, (a + 1, b), "an axis's end");
        if (!(hi > lo)) throw new PlotException($"an axis from {PlotNumber.Format(lo)} to {PlotNumber.Format(hi)}, which runs backwards");
        if (hi - lo < 1e-9 * Math.Max(1, Math.Abs(lo)) || Math.Abs(lo) > 1e12 || Math.Abs(hi) > 1e12) throw new PlotException("an axis too narrow or too far out to draw");
        string? label = null;
        bool pi = false;
        int i = b;
        while (i < t.Count)
        {
            if (t[i].Kind == PlotTokenKind.Text && label is null) label = Limit(t[i].Text.Trim(), "label");
            else if (t[i].IsWord("ticks") && i + 1 < t.Count && t[i + 1].IsWord("pi"))
            {
                pi = true;
                i++;
            }
            else throw new PlotException($"didn't expect “{t[i].Text}” there");
            i++;
        }
        var axis = new PlotAxis
        {
            Min = PlotExprReader.Read(line, t, 1, a, new PlotScope()), Max = PlotExprReader.Read(line, t, a + 1, b, new PlotScope()),
            Label = label, PiTicks = pi,
        };
        if (x) X = axis;
        else Y = axis;
    }

    void Param(string line, List<PlotToken> t)
    {
        // param k = 1 from 0.2 to 5 [step 0.1] ["label"]
        if (t.Count < 2 || t[1].Kind != PlotTokenKind.Name) throw new PlotException("a slider is written “param k = 1 from 0 to 5 \"what k is\"”");
        string name = t[1].Text;
        NewName(name, "slider");
        if (Params.Count >= MaxParams) throw new PlotException($"more than {MaxParams} sliders");
        Expect(t, 2, "=");
        int d = ExprEnd(t, 3);
        var (from, to, next) = Range(t, d);
        double def = Number(line, t, (3, d), "a slider's starting value");
        double lo = Number(line, t, from, "a slider's end"), hi = Number(line, t, to, "a slider's end");
        if (!(hi > lo)) throw new PlotException($"a slider from {PlotNumber.Format(lo)} to {PlotNumber.Format(hi)}, which runs backwards");
        if (def < lo - 1e-12 || def > hi + 1e-12) throw new PlotException($"{name} starts at {PlotNumber.Format(def)}, outside its range {PlotNumber.Format(lo)} to {PlotNumber.Format(hi)}");
        double step = 0;
        string? stepText = null;
        if (next < t.Count && t[next].IsWord("step"))
        {
            int s = ExprEnd(t, next + 1);
            step = Number(line, t, (next + 1, s), "a slider's step");
            if (!(step > 0) || step > hi - lo) throw new PlotException($"a step of {PlotNumber.Format(step)} for a slider from {PlotNumber.Format(lo)} to {PlotNumber.Format(hi)}");
            stepText = Text(line, t, (next + 1, s));
            next = s;
        }
        string? label = null;
        for (int i = next; i < t.Count; i++)
        {
            if (t[i].Kind == PlotTokenKind.Text && label is null) label = Limit(t[i].Text.Trim(), "label");
            else throw new PlotException($"didn't expect “{t[i].Text}” there");
        }
        Params.Add(new PlotParam
        {
            Name = name, Slot = Scope.AddParam(name), Default = def, Min = lo, Max = hi, Step = step, Label = label,
            DefaultText = Text(line, t, (3, d)), MinText = Text(line, t, from), MaxText = Text(line, t, to), StepText = stepText,
        });
    }

    void Curve(string line, List<PlotToken> t, int number, List<Pending> pending, string? named, string variable, int bodyFrom)
    {
        if (named is not null) NewName(named, "curve");
        if (PlotScope.Reserved(variable) || Scope.Params.ContainsKey(variable)) throw new PlotException($"“{variable}” can't be a curve's variable");
        var locals = Locals(variable);
        var f = new PlotFunction(named ?? "\u0001y" + number, [locals[variable]]);
        Scope.Add(f);
        var curve = new PlotCurve { Function = f, Variable = variable, Named = named };
        int end = ExprEnd(t, bodyFrom);
        (int, int)? from = null, to = null;
        int next = end;
        if (end < t.Count && t[end].IsWord("from"))
        {
            var r = Range(t, end);
            (from, to, next) = (r.From, r.To, r.Next);
        }
        string body = Text(line, t, (bodyFrom, end));
        curve.Canonical = (named is null ? "y = " : $"{named}({variable}) = ") + body
            + (from is { } f0 && to is { } t0 ? $" from {Text(line, t, f0)} to {Text(line, t, t0)}" : "");
        Options(line, t, next, curve, locals);
        Add(curve, number);
        pending.Add(new Pending(number, () =>
        {
            curve.Body = Expr(line, t, (bodyFrom, end), locals);
            f.Body = curve.Body.Root;
            if (from is { } a && to is { } b)
            {
                curve.From = Expr(line, t, a);
                curve.To = Expr(line, t, b);
            }
            curve.Flat = !curve.Body.Reads().Contains(locals[variable]);
        }));
    }

    void VLine(string line, List<PlotToken> t, int number, List<Pending> pending)
    {
        var v = new PlotVLine();
        int end = ExprEnd(t, 2);
        v.Canonical = "x = " + Text(line, t, (2, end));
        Options(line, t, end, v);
        Add(v, number);
        pending.Add(new Pending(number, () => v.X = Expr(line, t, (2, end))));
    }

    void Parametric(string line, List<PlotToken> t, int number, List<Pending> pending)
    {
        // curve (x(t), y(t)) for t from a to b
        var (a, b, next) = Pair(t, 1);
        Expect(t, next, "for");
        if (next + 1 >= t.Count || t[next + 1].Kind != PlotTokenKind.Name) throw new PlotException("a parametric curve is written “curve (cos(t), sin(t)) for t from 0 to 2pi”");
        string v = t[next + 1].Text;
        if (PlotScope.Reserved(v) || Scope.Params.ContainsKey(v)) throw new PlotException($"“{v}” can't be a curve's variable");
        var locals = Locals(v);
        var (from, to, after) = Range(t, next + 2);
        var item = new PlotParametric { Variable = v, Slot = locals[v] };
        item.Canonical = $"curve ({Text(line, t, a)}, {Text(line, t, b)}) for {v} from {Text(line, t, from)} to {Text(line, t, to)}";
        Options(line, t, after, item, locals);
        Add(item, number);
        pending.Add(new Pending(number, () =>
        {
            item.X = Expr(line, t, a, locals);
            item.Y = Expr(line, t, b, locals);
            item.From = Expr(line, t, from);
            item.To = Expr(line, t, to);
        }));
    }

    void Series(string line, List<PlotToken> t, int number, List<Pending> pending, string style)
    {
        // bars P(k) = … for k from 0 to n
        if (!(t.Count > 5 && t[1].Kind == PlotTokenKind.Name && t[2].Is("(") && t[3].Kind == PlotTokenKind.Name && t[4].Is(")") && t[5].Is("=")))
            throw new PlotException($"a sequence is written “{style} P(k) = … for k from 0 to n”");
        string name = t[1].Text, v = t[3].Text;
        NewName(name, "sequence");
        if (PlotScope.Reserved(v) || Scope.Params.ContainsKey(v)) throw new PlotException($"“{v}” can't be a sequence's variable");
        var locals = Locals(v);
        var f = new PlotFunction(name, [locals[v]]) { Sequence = true };
        Scope.Add(f);
        int end = ExprEnd(t, 6);
        Expect(t, end, "for");
        if (end + 1 >= t.Count || !t[end + 1].IsWord(v)) throw new PlotException($"a sequence in {v} runs “for {v} from … to …”");
        var (from, to, after) = Range(t, end + 2);
        var item = new PlotSeries
        {
            Style = style switch { "bars" => SeriesStyle.Bars, "stems" => SeriesStyle.Stems, "dots" => SeriesStyle.Dots, _ => SeriesStyle.Steps },
            Function = f, Variable = v,
        };
        item.Canonical = $"{style} {name}({v}) = {Text(line, t, (6, end))} for {v} from {Text(line, t, from)} to {Text(line, t, to)}";
        Options(line, t, after, item, locals);
        Add(item, number);
        pending.Add(new Pending(number, () =>
        {
            item.Body = Expr(line, t, (6, end), locals);
            f.Body = item.Body.Root;
            f.From = Expr(line, t, from);
            f.To = Expr(line, t, to);
        }));
    }

    void Points(string line, List<PlotToken> t, int number, List<Pending> pending)
    {
        var item = new PlotPoints();
        var pairs = new List<((int, int), (int, int))>();
        int i = 1;
        while (i < t.Count && t[i].Is("("))
        {
            var (a, b, next) = Pair(t, i);
            pairs.Add((a, b));
            i = next;
            if (i < t.Count && t[i].Is(",")) i++;
            if (pairs.Count > 200) throw new PlotException("more than 200 points");
        }
        if (pairs.Count == 0) throw new PlotException("points are written “points (1, 2), (2, 3.5), (3, 5)”");
        item.Canonical = "points " + string.Join(", ", pairs.Select(p => $"({Text(line, t, p.Item1)}, {Text(line, t, p.Item2)})"));
        Options(line, t, i, item);
        Add(item, number);
        pending.Add(new Pending(number, () =>
        {
            foreach (var (a, b) in pairs) item.Points.Add((Expr(line, t, a), Expr(line, t, b)));
        }));
    }

    void PointOrText(string line, List<PlotToken> t, int number, List<Pending> pending, bool text)
    {
        var (a, b, next) = Pair(t, 1);
        PlotItem item = text ? new PlotText() : new PlotPoint();
        item.Canonical = $"{(text ? "label" : "point")} ({Text(line, t, a)}, {Text(line, t, b)})";
        Options(line, t, next, item);
        if (text && !t.Skip(next).Any(k => k.Kind == PlotTokenKind.Text)) throw new PlotException("a label needs its words: label (1, 2) \"words\"");
        Add(item, number);
        pending.Add(new Pending(number, () =>
        {
            var x = Expr(line, t, a);
            var y = Expr(line, t, b);
            if (item is PlotPoint p)
            {
                p.X = x;
                p.Y = y;
            }
            else if (item is PlotText w)
            {
                w.X = x;
                w.Y = y;
            }
        }));
    }

    PlotCurve CurveNamed(string name)
    {
        if (Items.OfType<PlotCurve>().FirstOrDefault(c => c.Named == name) is { } c) return c;
        throw new PlotException($"“{name}” isn't a curve in this plot (name it first: {name}(x) = …)");
    }

    void Shade(string line, List<PlotToken> t, int number, List<Pending> pending)
    {
        // shade f [and g] from a to b
        if (t.Count < 2 || t[1].Kind != PlotTokenKind.Name) throw new PlotException("shading is written “shade f from a to b”, f a named curve");
        var under = CurveNamed(t[1].Text);
        PlotCurve? over = null;
        int i = 2;
        if (i < t.Count && t[i].IsWord("and"))
        {
            if (i + 1 >= t.Count || t[i + 1].Kind != PlotTokenKind.Name) throw new PlotException("“shade f and g” needs the second curve's name");
            over = CurveNamed(t[i + 1].Text);
            i += 2;
        }
        var (from, to, next) = Range(t, i);
        var locals = new Dictionary<string, int>(StringComparer.Ordinal) { ["area"] = Scope.Slot("area " + number) };
        var item = new PlotShade { Under = under, Over = over, AreaSlot = locals["area"] };
        item.Canonical = $"shade {under.Named}{(over is null ? "" : " and " + over.Named)} from {Text(line, t, from)} to {Text(line, t, to)}";
        Options(line, t, next, item, locals);
        Add(item, number);
        pending.Add(new Pending(number, () =>
        {
            item.From = Expr(line, t, from);
            item.To = Expr(line, t, to);
        }));
    }

    void TangentOrSecant(string line, List<PlotToken> t, int number, List<Pending> pending, bool tangent)
    {
        if (t.Count < 2 || t[1].Kind != PlotTokenKind.Name)
            throw new PlotException(tangent ? "a tangent is written “tangent f at a”" : "a secant is written “secant f from a to b”");
        var of = CurveNamed(t[1].Text);
        var locals = new Dictionary<string, int>(StringComparer.Ordinal) { ["slope"] = Scope.Slot("slope " + number) };
        if (tangent)
        {
            Expect(t, 2, "at");
            int end = ExprEnd(t, 3);
            var item = new PlotTangent { Of = of, SlopeSlot = locals["slope"] };
            item.Canonical = $"tangent {of.Named} at {Text(line, t, (3, end))}";
            Options(line, t, end, item, locals);
            Add(item, number);
            pending.Add(new Pending(number, () => item.At = Expr(line, t, (3, end))));
        }
        else
        {
            var (a, b, next) = Range(t, 2);
            var item = new PlotSecant { Of = of, SlopeSlot = locals["slope"] };
            item.Canonical = $"secant {of.Named} from {Text(line, t, a)} to {Text(line, t, b)}";
            Options(line, t, next, item, locals);
            Add(item, number);
            pending.Add(new Pending(number, () =>
            {
                item.A = Expr(line, t, a);
                item.B = Expr(line, t, b);
            }));
        }
    }

    void Vector(string line, List<PlotToken> t, int number, List<Pending> pending)
    {
        // vector (x, y)  or  vector (x0, y0) to (x, y)
        var (a, b, next) = Pair(t, 1);
        (int, int)? a0 = null, b0 = null;
        if (next < t.Count && t[next].IsWord("to"))
        {
            (a0, b0) = (a, b);
            (a, b, next) = Pair(t, next + 1);
        }
        var item = new PlotVector();
        item.Canonical = "vector " + (a0 is { } p && b0 is { } q ? $"({Text(line, t, p)}, {Text(line, t, q)}) to " : "") + $"({Text(line, t, a)}, {Text(line, t, b)})";
        Options(line, t, next, item);
        Add(item, number);
        pending.Add(new Pending(number, () =>
        {
            if (a0 is { } x0 && b0 is { } y0)
            {
                item.X0 = Expr(line, t, x0);
                item.Y0 = Expr(line, t, y0);
            }
            item.X = Expr(line, t, a);
            item.Y = Expr(line, t, b);
        }));
    }

    void Field(string line, List<PlotToken> t, int number, List<Pending> pending)
    {
        var (a, b, next) = Pair(t, 1);
        var locals = Locals("x", "y");
        var item = new PlotField { XSlot = locals["x"], YSlot = locals["y"] };
        item.Canonical = $"field ({Text(line, t, a)}, {Text(line, t, b)})";
        Options(line, t, next, item);
        Add(item, number);
        pending.Add(new Pending(number, () =>
        {
            item.P = Expr(line, t, a, locals);
            item.Q = Expr(line, t, b, locals);
        }));
    }

    void Heat(string line, List<PlotToken> t, int number, List<Pending> pending, bool fill)
    {
        // heat L(w, b) = …
        string kind = fill ? "heat" : "contour";
        if (!(t.Count > 7 && t[1].Kind == PlotTokenKind.Name && t[2].Is("(") && t[3].Kind == PlotTokenKind.Name && t[4].Is(",")
              && t[5].Kind == PlotTokenKind.Name && t[6].Is(")") && t[7].Is("=")))
            throw new PlotException($"a surface is written “{kind} L(x, y) = …”");
        string name = t[1].Text, v1 = t[3].Text, v2 = t[5].Text;
        NewName(name, "surface");
        foreach (string v in new[] { v1, v2 })
            if (PlotScope.Reserved(v) || Scope.Params.ContainsKey(v)) throw new PlotException($"“{v}” can't be a surface's variable");
        if (v1 == v2) throw new PlotException("a surface needs two different variables");
        var locals = Locals(v1, v2);
        var f = new PlotFunction(name, [locals[v1], locals[v2]]);
        Scope.Add(f);
        if (Items.OfType<PlotHeat>().Any(h => h.Fill) && fill) throw new PlotException("a second heat map (one plot shows one surface)");
        var item = new PlotHeat { Function = f, Fill = fill, Variables = [v1, v2] };
        int end = ExprEnd(t, 8);
        item.Canonical = $"{kind} {name}({v1}, {v2}) = {Text(line, t, (8, end))}";
        Options(line, t, end, item);
        Add(item, number);
        pending.Add(new Pending(number, () =>
        {
            item.Body = Expr(line, t, (8, end), locals);
            f.Body = item.Body.Root;
        }));
    }

    void Descent(string line, List<PlotToken> t, int number, List<Pending> pending)
    {
        // descent L from (x0, y0) rate η steps 20   |   descent f from x0 rate η steps 10
        if (t.Count < 2 || t[1].Kind != PlotTokenKind.Name) throw new PlotException("gradient descent is written “descent L from (1, 2) rate 0.1 steps 20”");
        if (!Scope.Functions.TryGetValue(t[1].Text, out var on) || on.Sequence)
            throw new PlotException($"“{t[1].Text}” isn't a curve or surface in this plot (name it first: {t[1].Text}(x, y) = …)");
        Expect(t, 2, "from");
        (int, int) x0, y0 = default;
        int next;
        bool two = on.Arity == 2;
        if (two)
        {
            var p = Pair(t, 3);
            (x0, y0, next) = (p.A, p.B, p.Next);
        }
        else
        {
            next = ExprEnd(t, 3);
            x0 = (3, next);
        }
        Expect(t, next, "rate");
        int r = ExprEnd(t, next + 1);
        Expect(t, r, "steps");
        int s = ExprEnd(t, r + 1);
        var item = new PlotDescent { On = on };
        item.Canonical = $"descent {on.Name} from " + (two ? $"({Text(line, t, x0)}, {Text(line, t, y0)})" : Text(line, t, x0))
            + $" rate {Text(line, t, (next + 1, r))} steps {Text(line, t, (r + 1, s))}";
        Options(line, t, s, item);
        Add(item, number);
        pending.Add(new Pending(number, () =>
        {
            item.X0 = Expr(line, t, x0);
            if (two) item.Y0 = Expr(line, t, y0);
            item.Rate = Expr(line, t, (next + 1, r));
            item.Steps = Expr(line, t, (r + 1, s));
        }));
    }

    void Matrix(string line, List<PlotToken> t, int number, List<Pending> pending)
    {
        // matrix [[a, b], [c, d]]
        if (t.Count < 2 || !t[1].Is("[")) throw new PlotException("a matrix is written “matrix [[2, 1], [1, 2]]”");
        if (Items.OfType<PlotMatrix>().Any()) throw new PlotException("a second matrix (one plot shows one map)");
        int close = Close(t, 1);
        var rows = new List<(int, int)>();
        int i = 2;
        while (i < close)
        {
            Expect(t, i, "[");
            int rc = Close(t, i);
            rows.Add((i + 1, rc));
            i = rc + 1;
            if (i < close && t[i].Is(",")) i++;
        }
        if (rows.Count != 2) throw new PlotException("a matrix here is 2 × 2: [[a, b], [c, d]]");
        var cells = new List<(int, int)>();
        foreach (var (from, to) in rows)
        {
            int comma = ExprEnd(t, from, comma: true);
            if (comma >= to || !t[comma].Is(",")) throw new PlotException("a matrix here is 2 × 2: [[a, b], [c, d]]");
            int second = ExprEnd(t, comma + 1, comma: true);
            if (second != to) throw new PlotException("a matrix here is 2 × 2: [[a, b], [c, d]]");
            cells.Add((from, comma));
            cells.Add((comma + 1, to));
        }
        var locals = new Dictionary<string, int>(StringComparer.Ordinal) { ["det"] = Scope.Slot("det " + number) };
        var item = new PlotMatrix { DetSlot = locals["det"], Morph = t.Skip(close + 1).Any(k => k.IsWord("morph")) };
        item.Canonical = $"matrix [[{Text(line, t, cells[0])}, {Text(line, t, cells[1])}], [{Text(line, t, cells[2])}, {Text(line, t, cells[3])}]]";
        Options(line, t, close + 1, item, locals, "morph");
        if (item.Morph)
        {
            if (Params.Count >= MaxParams) throw new PlotException($"more than {MaxParams} sliders");
            const string morph = "\u0001morph";
            item.MorphParam = new PlotParam
            {
                Name = morph, Slot = Scope.AddParam(morph), Default = 1, Min = 0, Max = 1, Label = "From the identity to the matrix",
                DefaultText = "1", MinText = "0", MaxText = "1",
            };
            Params.Add(item.MorphParam);
        }
        Add(item, number);
        pending.Add(new Pending(number, () =>
        {
            for (int k = 0; k < 4; k++) item.Entries[k] = Expr(line, t, cells[k]);
        }));
    }

    /// <summary>What must hold of the whole plot: something to draw, a curve's domain on its axis, no curve that
    /// calls itself round in a circle.</summary>
    void Check()
    {
        if (Items.Count == 0) throw new PlotException("nothing to draw (a plot needs at least one curve, sequence, surface, matrix or set of points)");
        if (!Items.Any(i => i is PlotCurve or PlotParametric or PlotSeries or PlotPoints or PlotHeat or PlotMatrix or PlotVector or PlotField or PlotVLine))
            throw new PlotException("nothing to draw (a plot needs at least one curve, sequence, surface, matrix or set of points)");
        // Curves that call each other in a circle can never be worked out (a sequence may use its own earlier terms).
        var state = new Dictionary<PlotFunction, int>();
        void Visit(PlotFunction f, List<string> path)
        {
            if (state.GetValueOrDefault(f) == 2) return;
            if (state.GetValueOrDefault(f) == 1) throw new PlotException($"{string.Join(" → ", path.Append(f.Name))}: curves that use each other in a circle");
            state[f] = 1;
            var calls = new HashSet<PlotFunction>();
            f.Body?.Calls(calls);
            foreach (var g in calls)
                if (!(g == f && f.Sequence)) Visit(g, [.. path, Display(f.Name)]);
            state[f] = 2;
        }
        foreach (var f in Scope.Functions.Values) Visit(f, []);
        // The x axis is the curves' own letter when they share one.
        var letters = Items.OfType<PlotCurve>().Select(c => c.Variable).Concat(Items.OfType<PlotSeries>().Select(s => s.Variable)).Distinct().ToList();
        XName = letters.Count == 1 ? letters[0] : Items.OfType<PlotHeat>().FirstOrDefault()?.Variables[0] ?? "x";
    }

    static string Display(string name) => name.StartsWith('\u0001') ? "y" : name;

    // --- writing it out again -------------------------------------------------------------------------------------------

    /// <summary>The plot as one canonical source: its comments, title, axes, sliders, then what it draws in order, each
    /// expression as it was written.</summary>
    public string ToSource()
    {
        var sb = new StringBuilder();
        foreach (string c in Comments) sb.Append(c).Append('\n');
        if (Title is not null) sb.Append("title ").Append(Title).Append('\n');
        if (X is { } x) sb.Append(AxisSource("x", x)).Append('\n');
        if (Y is { } y) sb.Append(AxisSource("y", y)).Append('\n');
        if (Equal) sb.Append("equal\n");
        foreach (var p in Params.Where(p => !p.Name.StartsWith('\u0001')))
        {
            sb.Append($"param {p.Name} = {p.DefaultText} from {p.MinText} to {p.MaxText}");
            if (p.StepText is { } s) sb.Append(" step ").Append(s);
            if (p.Label is { } l) sb.Append(" \"").Append(l).Append('"');
            sb.Append('\n');
        }
        foreach (var item in Items) sb.Append(item.Canonical).Append('\n');
        return sb.ToString().TrimEnd('\n');
    }

    static string AxisSource(string name, PlotAxis a) =>
        $"{name} {a.Min.Text} to {a.Max.Text}" + (a.Label is { } l ? $" \"{l}\"" : "") + (a.PiTicks ? " ticks pi" : "");

    /// <summary>Every word a student reads in it: its title, axes, sliders and what each thing is called, then its
    /// formulas (what search and Ask read).</summary>
    public IReadOnlyList<string> Words()
    {
        var words = new List<string>();
        if (Title is { } t) words.Add(t);
        if (X?.Label is { } xl) words.Add(xl);
        if (Y?.Label is { } yl) words.Add(yl);
        foreach (var p in Params.Where(p => !p.Name.StartsWith('\u0001'))) words.Add(p.Label is { } l ? $"{p.Name} ({l})" : p.Name);
        foreach (var i in Items)
        {
            switch (i)
            {
                case PlotCurve c:
                    words.Add((c.Label?.Plain is { } cl ? cl + ": " : "") + (c.Named is { } n ? $"{n}({c.Variable}) = " : "y = ") + c.Body.Text);
                    break;
                case PlotSeries s:
                    words.Add((s.Label?.Plain is { } sl ? sl + ": " : "") + $"{s.Function.Name}({s.Variable}) = {s.Body.Text}");
                    break;
                case PlotHeat h:
                    words.Add((h.Label?.Plain is { } hl ? hl + ": " : "") + $"{h.Function.Name}({string.Join(", ", h.Variables)}) = {h.Body.Text}");
                    break;
                default:
                    if (i.Label?.Plain is { Length: > 0 } other) words.Add(other);
                    break;
            }
        }
        return words;
    }
}
