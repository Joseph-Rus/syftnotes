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
