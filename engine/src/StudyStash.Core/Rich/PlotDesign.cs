namespace StudyStash.Core.Rich;

/// <summary>
/// The plots the diagram designer is taught with, and what it's told about them: each example is a plot from one of
/// the kinds of class a student takes (machine learning, statistics, algorithms, linear algebra), and each one
/// parses, evaluates sensibly and passes <see cref="PlotLint"/> (a test checks).
/// </summary>
public static class PlotDesign
{
    /// <summary>A curve with a slider: the sigmoid and its steepness.</summary>
    public static readonly string Sigmoid = """
        title The sigmoid squashes any score into (0, 1)
        x -6 to 6 "z (the score)"
        y -0.05 to 1.05 "σ(z)"
        param k = 1 from 0.2 to 5 "steepness k"
        σ(z) = 1 / (1 + e^(-k z)) "σ(kz)"
        y = 0.5 dashed "threshold 0.5"
        point (0, σ(0)) "σ(0) = 0.5"
        """.ReplaceLineEndings("\n");

    /// <summary>Curves compared: three activation functions, one with its own slider.</summary>
    public static readonly string Activations = """
        title ReLU, leaky ReLU and softplus
        x -4 to 4 "z"
        y -1 to 4 "activation"
        param a = 0.1 from 0 to 0.5 "leak a"
        relu(z) = max(0, z) "ReLU"
        leaky(z) = max(a z, z) "leaky ReLU"
        softplus(z) = ln(1 + e^z) "softplus"
        """.ReplaceLineEndings("\n");

    /// <summary>A distribution with its parameters and a shaded probability.</summary>
    public static readonly string Normal = """
        title The normal distribution
        x -6 to 6 "x"
        y 0 to 0.85 "density"
        param μ = 0 from -3 to 3 "mean μ"
        param σ = 1 from 0.5 to 3 "spread σ"
        f(x) = 1 / (σ sqrt(2π)) e^(-(x - μ)^2 / (2σ^2)) "N(μ, σ²)"
        shade f from μ - σ to μ + σ "{area} of it within one σ of μ"
        x = μ dashed "μ"
        """.ReplaceLineEndings("\n");

    /// <summary>Growth rates compared.</summary>
    public static readonly string Growth = """
        title How running time grows with n
        x 1 to 20 "n (input size)"
        y 0 to 400 "steps"
        a(n) = log2(n) "log₂ n"
        b(n) = n "n"
        c(n) = n log2(n) "n log₂ n"
        d(n) = n^2 "n²"
        g(n) = 2^n "2ⁿ"
        """.ReplaceLineEndings("\n");

    /// <summary>A curve's slope at a point the student drags.</summary>
    public static readonly string Tangent = """
        title The slope of the loss says which way to step
        x -1 to 4 "weight w"
        y 0 to 5 "loss L(w)"
        param w0 = 2.6 from -0.6 to 3.6 "where we are, w"
        L(w) = 0.5 (w - 1.4)^2 + 0.6 "loss"
        tangent L at w0 "slope {slope}"
        """.ReplaceLineEndings("\n");

    /// <summary>A loss surface as a heat map, and gradient descent across it.</summary>
    public static readonly string Surface = """
        title Gradient descent on a long, narrow bowl
        x -3 to 3 "w₁"
        y -2 to 2 "w₂"
        param η = 0.17 from 0.01 to 0.21 "learning rate η"
        heat L(w_1, w_2) = w_1^2 + 5 w_2^2 "loss"
        descent L from (-2.6, 1.5) rate η steps 25 "gradient descent"
        """.ReplaceLineEndings("\n");

    /// <summary>A 2×2 matrix as the map it is.</summary>
    public static readonly string Matrix = """
        title A matrix stretches the plane along its eigenvectors
        matrix [[2, 1], [1, 2]] morph "det = {det}"
        """.ReplaceLineEndings("\n");

    /// <summary>A discrete distribution with its parameters.</summary>
    public static readonly string Binomial = """
        title The binomial distribution
        x -0.5 to 20.5 "k (successes)"
        y 0 to 0.42 "P(X = k)"
        param n = 10 from 1 to 20 step 1 "trials n"
        param p = 0.5 from 0 to 1 "chance of success p"
        bars P(k) = choose(n, k) p^k (1 - p)^(n - k) for k from 0 to n "P(X = k)"
        x = n p dashed "mean np"
        """.ReplaceLineEndings("\n");

    /// <summary>The format, in brief: what a repair or an answer is told when it may write a ```plot.</summary>
    public static readonly string Reference = """
        A ```plot block is drawn exactly from its formulas: one statement a line.
        - title Words naming what it shows
        - x -6 to 6 "label" and y 0 to 1 "label": the ranges shown (leave y out to fit what's drawn; add ticks pi for multiples of π)
        - param k = 1 from 0.2 to 5 "what k is": a slider (add step 1 for whole numbers); a Greek name is fine (μ, σ, η)
        - f(x) = expression "label": a named curve, in the lecture's own letter (σ(z) = …), usable by later lines; y = expression for an unnamed one; y = 0.5 dashed "threshold" is a flat reference line and x = 2 dashed "asymptote" a vertical one
        - curve (cos(t), sin(t)) for t from 0 to 2pi "label": a parametric curve
        - bars P(k) = expression for k from 0 to n "label" (or stems, dots, steps): a sequence at whole numbers; a term may use earlier ones, a(n-1)
        - points (1, 2), (2, 3.5) "label"; point (x, y) "label"; label (x, y) "words"
        - shade f from a to b "label" (or shade f and g from a to b): {area} in its label is the area
        - tangent f at a "slope {slope}"; secant f from a to b
        - vector (x, y) "label"; vector (x0, y0) to (x, y); field (P, Q) for arrows of a field in x and y
        - heat L(u, v) = expression "label": a function of two variables as a heat map with contours (contour L(u, v) = … for lines alone)
        - descent L from (u0, v0) rate η steps 25 "gradient descent" (descent f from x0 rate η steps 10 on a curve)
        - matrix [[a, b], [c, d]] morph "det = {det}": what a 2×2 matrix does to the plane (its grid, the unit square, where e₁ and e₂ go, its eigenvectors); morph adds a slider from the identity to it
        - After a line's formula: a colour (blue, orange, green, purple, pink, red, grey), dashed or dotted, then its "label" last. {expression} in a label is worked out live ("k = {k}").
        Expressions: + − * / ^, 2x or k x for times, brackets ( ) or { } (exponents in brackets: e^(-k x)), pi, e, sqrt, abs, exp, ln and log (both natural), log2, log10, sin, cos, tan, asin, sinh…, floor, ceil, round, min, max, mod, choose(n, k), n!, gamma, erf, Phi (the normal CDF), if(test, a, b), a < x < b, sum(k, 1, n, term), prod(k, 1, n, term), f'(x) for a named curve's slope, Greek letters by symbol or name.
        """.ReplaceLineEndings("\n");

    /// <summary>Every example, with what it shows.</summary>
    public static IReadOnlyList<(string Title, string Words, string Source)> Examples { get; } =
    [
        ("The sigmoid", "Drag the steepness: a larger k makes the step from 0 to 1 sharper, and σ(0) stays 0.5.", Sigmoid),
        ("Activation functions", "ReLU is zero for negative inputs; leaky ReLU lets a little through; softplus is a smooth ReLU.", Activations),
        ("The normal distribution", "μ moves the bell and σ widens it; about 68% of it is always within one σ of μ.", Normal),
        ("Growth rates", "2ⁿ leaves every polynomial behind; log n barely grows.", Growth),
        ("The tangent to the loss", "Drag the point: the slope is negative left of the minimum and positive right of it.", Tangent),
        ("Gradient descent", "With η a little too large the path zigzags across the narrow direction; past 0.2 it diverges.", Surface),
        ("A linear map", "The unit square becomes a parallelogram of area det A; the eigenvectors only stretch.", Matrix),
        ("The binomial distribution", "n trials, each a success with chance p: the bars peak near np.", Binomial),
    ];
}
