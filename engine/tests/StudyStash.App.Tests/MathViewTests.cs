using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using StudyStash.App.Controls;
using StudyStash.App.Controls.Rich;

namespace StudyStash.App.Tests;

/// <summary>Formulas typeset with CSharpMath: inline on the text's baseline, display centred and scaled to the
/// column, and a plain fallback for what won't typeset.</summary>
public class MathViewTests
{
    static Window Show(Control content, double width = 620, ThemeVariant? variant = null)
    {
        Skin.UseTheme(ColourThemes.Default);
        ((App)Application.Current!).UseSkin(SkinKind.Mac);
        content.Width = width;
        content.HorizontalAlignment = HorizontalAlignment.Left;
        var window = new Window { Width = width + 40, Height = 400, RequestedThemeVariant = variant ?? ThemeVariant.Light, Content = content };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        Assert.NotNull(window.CaptureRenderedFrame());
        return window;
    }

    static MathView Mv(string latex, bool display = false, double size = 16) =>
        new() { Latex = latex, Display = display, Size = size, Foreground = Brushes.Black };

    [AvaloniaTheory]
    [InlineData(@"\text{CO} = \text{HR} \times \text{SV}")]
    [InlineData(@"\frac{500\ \text{mg}}{250\ \text{mg}} \times 5\ \text{mL} = 10\ \text{mL}")]
    [InlineData(@"\begin{aligned} a &= b \\ c &= d \end{aligned}")]
    [InlineData(@"\begin{cases} a & x > 0 \\ b & x \le 0 \end{cases}")]
    [InlineData(@"\mathrm{H_2O}")]
    [InlineData(@"\ce{Na+ + Cl- -> NaCl}")]
    [InlineData(@"\dfrac{a}{b} + \overset{\text{def}}{=} \boldsymbol{x}")]
    public void Every_sample_measures_and_renders(string latex)
    {
        foreach (bool display in new[] { false, true })
        {
            var mv = Mv(latex, display);
            var window = Show(mv);
            Assert.Null(mv.ErrorMessage);
            Assert.True(mv.Bounds.Width > 0 && mv.Bounds.Height > 0, $"{latex} display={display}: {mv.Bounds}");
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Baseline_offset_is_the_ascent()
    {
        var mv = Mv(@"\frac{a}{b}");
        Show(mv);
        Assert.True(TextBlock.GetBaselineOffset(mv) > 0);
        Assert.True(TextBlock.GetBaselineOffset(mv) < mv.Bounds.Height);
    }

    [AvaloniaTheory]
    [InlineData(@"\frac{a}{")]
    [InlineData(@"\notacommand{x}")]
    public void Bad_latex_sets_the_error_and_draws_nothing(string latex)
    {
        var mv = Mv(latex);
        mv.Measure(Size.Infinity);
        Assert.NotNull(mv.ErrorMessage);
        Assert.Equal(new Size(0, 0), mv.DesiredSize);
        Show(mv); // never throws once it's in a real tree either
    }

    [AvaloniaFact]
    public void A_very_long_source_still_measures_without_throwing()
    {
        var mv = Mv(string.Concat(Enumerable.Repeat("x + ", 800)) + "x"); // > 3000 chars
        Show(mv);
        // CSharpMath may or may not typeset something this long; either way it must not throw or hang.
        Assert.True(mv.ErrorMessage is not null || mv.Bounds.Width > 0);
    }

    [AvaloniaFact]
    public void A_theme_switch_changes_the_colour_without_re_measuring()
    {
        var mv = Mv(@"\frac{a}{b}");
        var window = Show(mv);
        var before = mv.Bounds.Size;
        mv.Foreground = Brushes.White;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(before, mv.Bounds.Size);
        window.Close();
    }

    [AvaloniaTheory]
    [InlineData(@"\dfrac{a}{b}")]
    [InlineData(@"\textbf{stroke volume}")]
    [InlineData(@"\ce{CO2 + H2O <=> H2CO3}")]
    [InlineData(@"\SI{500}{\milli\gram}")]
    [InlineData(@"\overset{\text{def}}{=}")]
    [InlineData(@"\underset{n \to \infty}{\lim}")]
    [InlineData(@"\xrightarrow{k_1}")]
    [InlineData(@"\boxed{E = mc^2}")]
    public void Every_spike4_rewrite_sample_renders_after_prepare(string latex)
    {
        var mv = Mv(latex);
        Show(mv);
        Assert.Null(mv.ErrorMessage);
    }

    [AvaloniaFact]
    public void A_display_formula_wider_than_the_column_scales_down_never_below_the_floor()
    {
        string wide = @"\frac{\text{a very long numerator that keeps on going}}{\text{a very long denominator that keeps on going too}}";
        var mv = new MathView { Latex = wide, Display = true, Size = 18, Foreground = Brushes.Black };
        var display = new MathDisplay(mv);
        var window = Show(display, width: 120);
        Assert.True(mv.Scale >= MathDisplay.MinScale - 0.001 && mv.Scale <= 1, $"scale={mv.Scale}");
        window.Close();

        // Plenty of room: no shrinking.
        var mv2 = new MathView { Latex = @"\frac{a}{b}", Display = true, Size = 18, Foreground = Brushes.Black };
        var display2 = new MathDisplay(mv2);
        window = Show(display2, width: 620);
        Assert.Equal(1, mv2.Scale);
        window.Close();
    }
}
