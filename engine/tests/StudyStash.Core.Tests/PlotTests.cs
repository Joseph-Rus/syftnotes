using System.Xml.Linq;
using StudyStash.Core.Rich;

namespace StudyStash.Core.Tests;

/// <summary>
/// A ```plot block: read strictly (a mistake says which line), written back in one canonical form, sampled so what's
/// drawn is the curve (no line across an asymptote or a jump, nothing thousands of screens away), and drawn as a
/// standalone SVG for a downloaded note and the phone.
/// </summary>
public class PlotTests
{
    static double Measure(string text, double size, bool bold) => text.Length * size * 0.55;

    public static TheoryData<string> Examples => [.. PlotDesign.Examples.Select(e => e.Title)];

    static string SourceOf(string title) => PlotDesign.Examples.Single(e => e.Title == title).Source;

    [Theory]
    [MemberData(nameof(Examples))]
    public void Every_example_reads_writes_back_the_same_and_draws(string title)
    {
        string source = SourceOf(title);
        var plot = Plot.Parse(source);
        string canonical = plot.ToSource();
        Assert.Equal(canonical, Plot.Parse(canonical).ToSource());
        Assert.Equal(source.Trim(), canonical);
        var state = new PlotState(plot);
        var scene = PlotLayout.Build(state, 620, Measure);
        Assert.True(scene.Height > 200 && scene.Height < 900, $"{title}: {scene.Height} tall");
        Assert.False(scene.OutOfTime);
        string svg = PlotSvg.Render(scene, plot.Title!);
        var xml = XDocument.Parse(svg);
        Assert.Equal(plot.Title, xml.Root!.Elements().First(e => e.Name.LocalName == "title").Value);
        if (Environment.GetEnvironmentVariable("STUDYSTASH_SHOTS") is { Length: > 0 } dir)
        {
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, $"plot-{title.ToLowerInvariant().Replace(' ', '-')}.svg"), svg);
        }
    }

    [Fact]
    public void A_mistake_says_which_line_and_what()
    {
        void Says(string source, params string[] words)
        {
            var e = Assert.Throws<PlotException>(() => Plot.Parse(source));
            foreach (string w in words) Assert.Contains(w, e.Message);
        }
        Says("x -5 to 5\ny = sigm(x)", "Line 2", "“sigm”");
        Says("x 5 to -5\ny = x", "Line 1", "backwards");
        Says("param k = 9 from 0 to 5\ny = k x", "Line 1", "outside its range");
        Says("y = x\nshade f from 0 to 1", "Line 2", "“f” isn't a curve");
        Says("flowchart LR\n A --> B", "Line 1", "didn't understand");
        Says("title Only a title", "nothing to draw");
        Says("param sin = 1 from 0 to 2\ny = x", "“sin” can't be");
        Says("y = x \"one\" \"two\"", "two labels");
        Says("f(x) = x\nf(x) = 2x", "two things named");
        Says("matrix [[1, 2, 3], [4, 5, 6]]", "2 × 2");
        Says("bars P(k) = k for j from 0 to 3", "for k");
        Says("y = x \"label {sigm}\"", "Line 1", "“sigm”");
    }

    [Fact]
    public void Any_order_of_lines_reads_and_comments_are_kept_for_the_moment_they_mark()
    {
        var plot = Plot.Parse("%% Study Stash diagram, from 12:34\nplot\npoint (2, f(2)) \"here\"\nf(x) = x^2\nparam a = 1 from 0 to 2\ny = a x");
        Assert.Equal(12 * 60 + 34, DiagramMoment.From(plot.ToSource()));
        Assert.StartsWith("%% Study Stash diagram, from 12:34\nparam a = 1 from 0 to 2\npoint (2, f(2)) \"here\"", plot.ToSource());
        var state = new PlotState(plot);
        Assert.Equal(4, state.Eval(plot.Items.OfType<PlotPoint>().Single().Y));
    }

    /// <summary>The screen points of y = f(x) drawn over a view, one list per piece.</summary>
    static List<List<PlotPt>> Pieces(string f, double x0, double x1, double y0, double y1)
    {
        var plot = Plot.Parse($"x {x0} to {x1}\ny {y0} to {y1}\nf(x) = {f}");
        var state = new PlotState(plot);
        var curve = plot.Items.OfType<PlotCurve>().Single();
        var map = new PlotMap(state.View, 0, 0, 600, 400);
        return PlotSampler.Curve(x => state.At(curve, x), x0, x1, map, state.Env);
    }

    [Fact]
    public void An_asymptote_or_a_jump_breaks_the_curve_instead_of_drawing_a_line_across()
    {
        // 1/x: two pieces, neither crossing x = 0 (screen x 300).
        var hyperbola = Pieces("1/x", -5, 5, -5, 5);
        Assert.Equal(2, hyperbola.Count);
        Assert.All(hyperbola, p => Assert.True(p.All(q => q.X < 300) || p.All(q => q.X > 300)));
        // tan x from −4.5 to 4.5: its three branches there (asymptotes at ±π/2), none spanning an asymptote.
        var tan = Pieces("tan(x)", -4.5, 4.5, -4, 4);
        Assert.Equal(3, tan.Count);
        // (A line across one would run from above the view to below it.)
        foreach (var p in tan)
            Assert.DoesNotContain(p.Zip(p.Skip(1)), s => Math.Min(s.First.Y, s.Second.Y) < 0 && Math.Max(s.First.Y, s.Second.Y) > 400);
        // A step function: a piece for each step, no vertical risers.
        var floor = Pieces("floor(x)", 0, 4, -1, 5);
        Assert.Equal(4, floor.Count);
        foreach (var p in floor) Assert.True(p.Max(q => q.Y) - p.Min(q => q.Y) < 1);
    }

    [Fact]
    public void A_curve_reaches_the_edge_of_its_domain_and_nothing_is_drawn_far_off_the_screen()
    {
        // √x starts at 0 exactly (screen x 300), not a sample's width later.
        var root = Pieces("sqrt(x)", -2, 2, -1, 2);
        Assert.Single(root);
        Assert.True(root[0][0].X - 300 < 0.5, $"starts at {root[0][0].X}");
        // e^(x^2) leaves the top within the view: its points stay within a few screens of it.
        var steep = Pieces("e^(x^2)", -10, 10, 0, 5);
        Assert.All(steep.SelectMany(p => p), q => Assert.True(q.Y > -400 * 9 && q.Y < 400 * 9));
        // Undefined everywhere: nothing at all.
        Assert.Empty(Pieces("sqrt(-1 - x^2)", -5, 5, -1, 1));
        // A smooth curve is refined where it bends: sin over many periods stays within a pixel of itself.
        var sine = Pieces("sin(x)", 0, 60, -1.2, 1.2);
        Assert.Single(sine);
        var pts = sine[0];
        for (int i = 0; i + 1 < pts.Count; i++)
        {
            double xm = (pts[i].X + pts[i + 1].X) / 2, ym = (pts[i].Y + pts[i + 1].Y) / 2;
            double truth = 200 - Math.Sin(xm / 10) * 400 / 2.4;
            Assert.True(Math.Abs(ym - truth) < 1, $"strays {Math.Abs(ym - truth):0.00} px at {xm}");
        }
    }

    [Fact]
    public void A_shaded_probability_and_a_slope_are_right()
    {
        var plot = Plot.Parse(PlotDesign.Normal);
        var state = new PlotState(plot);
        PlotLayout.Build(state, 620, Measure);
        var shade = plot.Items.OfType<PlotShade>().Single();
        Assert.Equal(0.6827, state.Env.Slots[shade.AreaSlot], 3);
        Assert.Equal("0.683 of it within one σ of μ", shade.Label!.Text(state.Env));
        state.Set(plot.Params.Single(p => p.Name == "σ"), 2.5);
        PlotLayout.Build(state, 620, Measure);
        Assert.Equal(0.6827, state.Env.Slots[shade.AreaSlot], 3);

        var tangent = Plot.Parse(PlotDesign.Tangent);
        var ts = new PlotState(tangent);
        PlotLayout.Build(ts, 620, Measure);
        var t = tangent.Items.OfType<PlotTangent>().Single();
        Assert.Equal(2.6 - 1.4, ts.Env.Slots[t.SlopeSlot], 6);
    }

    [Fact]
    public void Gradient_descent_and_eigenvectors_are_worked_out_exactly()
    {
        // On w₁² + 5w₂² with η = 0.1, w₂ is gone after one step (1 − 2·5·0.1 = 0) and w₁ shrinks by 0.8 a step.
        var path = PlotSampler.Descent((x, y) => x * x + 5 * y * y, true, -2.6, 1.5, 0.1, 3);
        Assert.Equal(4, path.Count);
        Assert.Equal(0, path[1].Y, 6);
        Assert.Equal(-2.6 * 0.8 * 0.8 * 0.8, path[3].X, 6);
        // A rate too large diverges, and stops before it's absurd.
        var wild = PlotSampler.Descent((x, y) => x * x + 5 * y * y, true, 1, 1, 0.5, 500);
        Assert.All(wild, p => Assert.True(Math.Abs(p.Y) <= 1e9));
        var eigen = PlotSampler.Eigen(2, 1, 1, 2);
        Assert.Equal([3.0, 1.0], eigen.Select(e => Math.Round(e.Lambda, 9)));
        Assert.Equal(Math.Abs(eigen[0].X), Math.Abs(eigen[0].Y), 9);
        Assert.Empty(PlotSampler.Eigen(0, -1, 1, 0)); // a rotation has no real eigenvectors
    }

    [Fact]
    public void A_plot_with_no_axes_opens_on_what_it_draws()
    {
        var state = new PlotState(Plot.Parse("bars P(k) = 0.5^k for k from 0 to 8"));
        Assert.True(state.Home.X0 < 0 && state.Home.X0 > -1 && state.Home.X1 > 8 && state.Home.X1 < 9);
        Assert.Equal(0, state.Home.Y0);
        Assert.True(state.Home.Y1 > 1);
        // 1/x near 0 doesn't flatten the rest: the view keeps to most of it.
        var hyper = new PlotState(Plot.Parse("x -5 to 5\ny = 1/x"));
        Assert.True(hyper.Home.Y1 < 100, $"{hyper.Home.Y1}");
    }
}
