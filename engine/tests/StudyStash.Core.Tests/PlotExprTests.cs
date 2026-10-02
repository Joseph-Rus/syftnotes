using System.Diagnostics;
using StudyStash.Core.Rich;

namespace StudyStash.Core.Tests;

/// <summary>
/// The plots' expression engine: it reads what lecturers write (2x, x², e^{-x^2/2}, σ, \frac, sin 2x) to the value
/// they mean, since a wrong value would silently mislead a student; and nothing it's given — a typo, nonsense, or an
/// expression built to hang or crash it — does anything but say why or come out undefined, quickly.
/// </summary>
public class PlotExprTests
{
    /// <summary>The value of <paramref name="text"/> with x (and the given sliders) set.</summary>
    static double Value(string text, double x = 0, params (string Name, double Value)[] sliders)
    {
        var scope = new PlotScope();
        var env0 = new List<(int, double)>();
        foreach (var (name, v) in sliders) env0.Add((scope.AddParam(name), v));
        var locals = new Dictionary<string, int> { ["x"] = scope.Variable("x") };
        var e = PlotExprReader.Read(text, scope, locals);
        var env = new PlotEnv(scope.SlotCount);
        env.Slots[locals["x"]] = x;
        foreach (var (slot, v) in env0) env.Slots[slot] = v;
        return e.Eval(env);
    }

    [Theory]
    [InlineData("2x", 3, 6)]
    [InlineData("x^2", 3, 9)]
    [InlineData("x²", 3, 9)]
    [InlineData("-x^2", 3, -9)]
    [InlineData("−x", 3, -3)]
    [InlineData("2^3^2", 0, 512)]
    [InlineData("1/2x", 4, 2)]
    [InlineData("2(x+1)", 2, 6)]
    [InlineData("(x+1)(x-1)", 3, 8)]
    [InlineData("3!", 0, 6)]
    [InlineData("|x - 5|", 2, 3)]
    [InlineData("2|x|", -3, 6)]
    [InlineData("sqrt 16", 0, 4)]
    [InlineData("√x", 9, 3)]
    [InlineData("\\sqrt{x}", 9, 3)]
    [InlineData("\\sqrt[3]{x}", 27, 3)]
    [InlineData("x^(1/3)", -8, -2)]
    [InlineData("\\frac{1}{2}x", 6, 3)]
    [InlineData("\\frac12", 0, 0.5)]
    [InlineData("log2(8)", 0, 3)]
    [InlineData("log_2 8", 0, 3)]
    [InlineData("log_{10}(1000)", 0, 3)]
    [InlineData("ln e", 0, 1)]
    [InlineData("log(e^2)", 0, 2)]
    [InlineData("1e-3", 0, 0.001)]
    [InlineData("2e3", 0, 2000)]
    [InlineData("max(0, x)", -2, 0)]
    [InlineData("max(0, x)", 2, 2)]
    [InlineData("min(3, x, 7)", 1, 1)]
    [InlineData("if(x < 0, 0.01x, x)", -100, -1)]
    [InlineData("if(x < 0, 0.01x, x)", 5, 5)]
    [InlineData("0 < x < 1", 0.5, 1)]
    [InlineData("0 < x < 1", 1.5, 0)]
    [InlineData("x >= 2 and x <= 3", 2.5, 1)]
    [InlineData("sum(k, 1, 100, k)", 0, 5050)]
    [InlineData("sum(k = 1, 4, k^2)", 0, 30)]
    [InlineData("prod(k, 1, 5, k)", 0, 120)]
    [InlineData("choose(10, 3)", 0, 120)]
    [InlineData("binom(52, 5)", 0, 2598960)]
    [InlineData("gamma(5)", 0, 24)]
    [InlineData("Γ(0.5)^2", 0, Math.PI)]
    [InlineData("floor(2.7) + ceil(2.1)", 0, 5)]
    [InlineData("mod(7, 3)", 0, 1)]
    [InlineData("mod(-1, 3)", 0, 2)]
    [InlineData("2 × 3 ÷ 4", 0, 1.5)]
    [InlineData("2 \\cdot 3", 0, 6)]
    [InlineData("clamp(x, 0, 1)", 3, 1)]
    public void Reads_what_a_lecturer_writes_to_the_value_they_mean(string text, double x, double expected)
    {
        double v = Value(text, x);
        Assert.True(Math.Abs(v - expected) <= 1e-9 * Math.Max(1, Math.Abs(expected)), $"{text} at x = {x} came out {v}, not {expected}");
    }

    [Fact]
    public void Functions_written_without_brackets_take_what_follows_them()
    {
        double x = 0.7;
        Assert.Equal(Math.Sin(2 * x), Value("sin 2x", x), 12);
        Assert.Equal(Math.Sin(x) * Math.Cos(x), Value("sin x cos x", x), 12);
        Assert.Equal(1, Value("sin^2 x + cos^2 x", x), 12);
        Assert.Equal(Math.Sin(x) + 1, Value("sin x + 1", x), 12);
        Assert.Equal(Math.Exp(-x * x / 2), Value("e^{-x^2/2}", x), 12);
        Assert.Equal(Math.Exp(-x * x / 2), Value("exp(-x²/2)", x), 12);
        // e^-2x is e^(-2)·x, as written: a plot puts an exponent in brackets.
        Assert.Equal(Math.Exp(-2) * x, Value("e^-2x", x), 12);
        Assert.Equal(Math.Exp(-2 * x), Value("e^(-2x)", x), 12);
    }

    [Fact]
    public void Greek_letters_and_names_written_together_read_as_the_lecture_means_them()
    {
        // The normal density, in σ and μ, written the way the notes write it.
        double x = 1.3, mu = 0.5, sigma = 2;
        double want = 1 / (sigma * Math.Sqrt(2 * Math.PI)) * Math.Exp(-(x - mu) * (x - mu) / (2 * sigma * sigma));
        Assert.Equal(want, Value("1/(σ sqrt(2π)) e^(-(x - μ)^2/(2σ^2))", x, ("σ", sigma), ("μ", mu)), 12);
        Assert.Equal(want, Value("\\frac{1}{\\sigma\\sqrt{2\\pi}} e^{-\\frac{(x-\\mu)^2}{2\\sigma^2}}", x, ("σ", sigma), ("μ", mu)), 12);
        Assert.Equal(want, Value("1/(sigma sqrt(2 pi)) exp(-(x - mu)^2/(2 sigma^2))", x, ("σ", sigma), ("μ", mu)), 12);
        // kx is k times x; πx is π times x.
        Assert.Equal(6, Value("kx", 3, ("k", 2)), 12);
        Assert.Equal(Math.PI * 2, Value("πx", 2), 12);
        Assert.Equal(1 / (1 + Math.Exp(-2 * 3.0)), Value("1/(1+e^(-kx))", 3, ("k", 2)), 12);
    }

    [Fact]
    public void A_name_it_doesnt_know_says_which()
    {
        var e = Assert.Throws<PlotException>(() => Value("1/(1+e^(-sigm x))"));
        Assert.Contains("“sigm”", e.Message);
        Assert.Contains("“mu”", Assert.Throws<PlotException>(() => Value("x - mu")).Message.Replace("μ", "mu"));
        foreach (string bad in new[] { "", "2 +", "(x", "x)", "sin", "f(x)", "x ,, 2", "max()", "if(x, 1)", "sum(1, 2, 3)", "\\unknown{x}", "x @ 2", "\"label\"", "x'" })
            Assert.Throws<PlotException>(() => Value(bad));
    }

    [Fact]
    public void Undefined_and_infinite_values_come_out_as_such_never_as_a_wrong_number()
    {
        Assert.True(double.IsNaN(Value("sqrt(x)", -1)));
        Assert.True(double.IsNaN(Value("ln(x)", -1)));
        Assert.True(double.IsNegativeInfinity(Value("ln(x)", 0)));
        Assert.True(double.IsPositiveInfinity(Value("1/x", 0)));
        Assert.True(double.IsNaN(Value("0/0")));
        Assert.True(double.IsPositiveInfinity(Value("200!")));
        Assert.True(double.IsNaN(Value("(-1)!")));
        Assert.True(double.IsNaN(Value("max(x, sqrt(-1))", 2)));
        Assert.True(double.IsNaN(Value("if(sqrt(-1), 1, 2)")));
        Assert.True(double.IsPositiveInfinity(Value("10^10^10")));
    }

    [Fact]
    public void Nothing_it_is_given_hangs_or_crashes_it()
    {
        var clock = Stopwatch.StartNew();
        // Nested past any sensible depth: refused, not a stack overflow.
        Assert.Throws<PlotException>(() => Value(new string('(', 5000) + "x" + new string(')', 5000)));
        Assert.Throws<PlotException>(() => Value(new string('(', 100) + "x" + new string(')', 100)));
        Assert.Throws<PlotException>(() => Value(string.Concat(Enumerable.Repeat("-", 300)) + "x"));
        Assert.Throws<PlotException>(() => Value(string.Join("^", Enumerable.Repeat("x", 150))));
        Assert.Throws<PlotException>(() => Value(string.Join("+", Enumerable.Repeat("x", 3000))));
        Assert.Equal(150, Value(string.Join("+", Enumerable.Repeat("x", 150)), 1));
        // Sums too long to finish: undefined, at once.
        Assert.True(double.IsNaN(Value("sum(k, 1, 1e12, k)")));
        Assert.True(double.IsNaN(Value("sum(i, 1, 1000, sum(j, 1, 1000, sum(k, 1, 1000, i j k)))")));
        Assert.True(double.IsNaN(Value("sum(k, 1, x, k)", double.PositiveInfinity)));
        Assert.True(double.IsNaN(Value("sum(k, 1, x, k)", double.NaN)));
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(2), $"took {clock.Elapsed}");
    }

    [Fact]
    public void Curves_that_call_each_other_in_a_circle_are_refused_and_sequences_may_use_their_earlier_terms()
    {
        var e = Assert.Throws<PlotException>(() => Plot.Parse("f(x) = g(x) + 1\ng(x) = f(x) - 1"));
        Assert.Contains("circle", e.Message);
        Assert.Throws<PlotException>(() => Plot.Parse("f(x) = f(x - 1)"));
        // Fibonacci and merge sort's recurrence, as sequences.
        var plot = Plot.Parse("dots F(n) = if(n < 2, n, F(n-1) + F(n-2)) for n from 0 to 30\nstems T(n) = if(n <= 1, 1, 2 T(floor(n/2)) + n) for n from 1 to 64");
        var state = new PlotState(plot);
        var fib = plot.Items.OfType<PlotSeries>().First();
        var merge = plot.Items.OfType<PlotSeries>().Last();
        Assert.Equal(832040, state.Term(fib, 30));
        Assert.Equal(64 * 6 + 64, state.Term(merge, 64)); // n log₂ n + n at a power of two
        Assert.True(double.IsNaN(state.Term(fib, 31)));
        Assert.True(double.IsNaN(state.Term(fib, 2.5)));
    }

    [Fact]
    public void A_hostile_sequence_or_a_chain_of_curves_stops_within_its_budget()
    {
        var clock = Stopwatch.StartNew();
        // Each term a long sum, ten thousand terms: the table shares one budget.
        var heavy = Plot.Parse("bars a(n) = sum(k, 1, 40000, k) for n from 0 to 10000");
        var state = new PlotState(heavy);
        double last = state.Term(heavy.Items.OfType<PlotSeries>().Single(), 10000);
        Assert.True(double.IsNaN(last));
        // Each curve calls the one before twice: 2^23 calls if nothing stopped it.
        var lines = new List<string> { "f0(x) = x" };
        for (int i = 1; i < 24; i++) lines.Add($"f{i}(x) = f{i - 1}(x) + f{i - 1}(x + 1)");
        var chain = Plot.Parse(string.Join("\n", lines));
        var s2 = new PlotState(chain);
        s2.NewFrame();
        double v = s2.At(chain.Items.OfType<PlotCurve>().Last(), 1);
        Assert.True(double.IsNaN(v));
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(3), $"took {clock.Elapsed}");
    }

    [Fact]
    public void Derivatives_of_a_curve_are_right_to_plotting_accuracy()
    {
        var plot = Plot.Parse("f(x) = x^3 - 2x\ng(x) = f'(x)\nh(x) = f''(x)");
        var state = new PlotState(plot);
        var curves = plot.Items.OfType<PlotCurve>().ToList();
        Assert.Equal(3 * 4 - 2, state.At(curves[1], 2), 6);
        Assert.Equal(6 * 2, state.At(curves[2], 2), 3);
    }

    [Fact]
    public void Erf_and_the_normal_cdf_are_accurate()
    {
        Assert.Equal(0.8427007929497149, PlotMath.Erf(1), 9);
        Assert.Equal(0.9953222650189527, PlotMath.Erf(2), 9);
        Assert.Equal(0.9999779095030014, PlotMath.Erf(3), 9);
        Assert.Equal(-0.5204998778130465, PlotMath.Erf(-0.5), 9);
        Assert.Equal(0.6826894921370859, PlotMath.NormalCdf(1) - PlotMath.NormalCdf(-1), 9);
        Assert.Equal(0.975, PlotMath.NormalCdf(1.959963984540054), 9);
    }
}
