using StudyStash.Core.Rich;

namespace StudyStash.Core.Tests;

/// <summary>LaTeX made ready for the places that show it.</summary>
public class MathTextTests
{
    [Theory]
    [InlineData(@"\text{CO} = \text{HR} \times \text{SV}", "CO = HR × SV")]
    [InlineData(@"\frac{SBP + 2 \cdot DBP}{3}", "(SBP + 2 · DBP)/3")]
    [InlineData(@"\frac{a}{b}", "a/b")]
    [InlineData(@"x^2 + y_1", "x² + y₁")]
    [InlineData(@"\mathrm{H_2O}", "H₂O")]
    [InlineData(@"37^\circ\text{C}", "37°C")]
    [InlineData(@"\Delta G \le 0 \neq \nu", "ΔG ≤ 0 ≠ ν")]
    [InlineData(@"\left( \alpha \right)", "( α )")]
    [InlineData(@"\sqrt{x}", "√x")]
    [InlineData(@"\mathrm{Ca}^{2+}", "Ca²⁺")]
    [InlineData(@"\frac{a}{", "a/")]
    [InlineData(@"\", "")]
    [InlineData(@"{{{", "")]
    public void Plain_reads_as_text(string latex, string plain) => Assert.Equal(plain, MathText.Plain(latex));

    [Theory]
    [InlineData(@"\dfrac{a}{b}", @"\frac{a}{b}")]
    [InlineData(@"\tfrac{a}{b}", @"\frac{a}{b}")]
    [InlineData(@"\cfrac{a}{b}", @"\frac{a}{b}")]
    [InlineData(@"\textbf{SV}", @"\mathbf{SV}")]
    [InlineData(@"\textbf{stroke volume}", @"\text{stroke volume}")]
    [InlineData(@"\textit{x}", @"\mathit{x}")]
    [InlineData(@"\textit{per minute}", @"\text{per minute}")]
    [InlineData(@"\boxed{x + 1}", "x + 1")]
    [InlineData(@"\cancel{x}", "x")]
    [InlineData(@"\bcancel{x}", "x")]
    [InlineData(@"\xcancel{x}", "x")]
    [InlineData(@"\phantom{x}y", "y")]
    [InlineData(@"\overset{def}{=}", @"{=}^{def}")]
    [InlineData(@"\stackrel{def}{=}", @"{=}^{def}")]
    [InlineData(@"\underset{n \to \infty}{\lim}", @"{\lim}_{n \to \infty}")]
    [InlineData(@"\xrightarrow{k_1}", @"\longrightarrow^{k_1}")]
    [InlineData(@"\xleftarrow{k_1}", @"\longleftarrow^{k_1}")]
    [InlineData(@"\not=", @"\neq")]
    [InlineData(@"\lvert x \rvert", "| x |")]
    [InlineData(@"\lVert x \rVert", @"\| x \|")]
    [InlineData(@"\boldsymbol{\alpha}", @"\bm{\alpha}")]
    [InlineData(@"\operatorname*{arg\,max}", @"\operatorname{arg\,max}")]
    [InlineData(@"\tag{1} x = 1", " x = 1")]
    [InlineData(@"\label{eq:one} x = 1", " x = 1")]
    [InlineData(@"a \nonumber \notag \hline \displaybreak b", "a     b")]
    [InlineData(@"\overbrace{a+b}^{c}", @"\overline{a+b}^{c}")]
    [InlineData(@"\overbrace{a+b}", @"\overline{a+b}")]
    [InlineData(@"\ket{\psi}", @"|\psi\rangle")]
    [InlineData(@"\bra{\psi}", @"\langle \psi|")]
    [InlineData(@"\SI{500}{\milli\gram}", @"500\ \text{mg}")]
    [InlineData(@"\qty{5}{\milli\liter}", @"5\ \text{mL}")]
    [InlineData(@"\si{\milli\gram\per\milli\liter}", @"\text{mg/mL}")]
    [InlineData(@"\unit{mg}", @"\text{mg}")]
    // Environments.
    [InlineData(@"\begin{align} a &= b \\ c &= d \end{align}", @"\begin{aligned} a &= b \\ c &= d \end{aligned}")]
    [InlineData(@"\begin{align*} a &= b \end{align*}", @"\begin{aligned} a &= b \end{aligned}")]
    [InlineData(@"\begin{eqnarray} a &=& b \end{eqnarray}", @"\begin{aligned} a &=& b \end{aligned}")]
    [InlineData(@"\begin{gather*} a \\ b \end{gather*}", @"\begin{gather} a \\ b \end{gather}")]
    [InlineData(@"\begin{gather} a \\ b \end{gather}", @"\begin{gather} a \\ b \end{gather}")]
    [InlineData(@"\begin{multline} a \\ b \end{multline}", @"\begin{gather} a \\ b \end{gather}")]
    [InlineData(@"\begin{equation} x = 1 \end{equation}", " x = 1 ")]
    [InlineData(@"\begin{equation*} x = 1 \end{equation*}", " x = 1 ")]
    [InlineData(@"\begin{array}{cc} a & b \\ c & d \end{array}", @"\begin{matrix} a & b \\ c & d \end{matrix}")]
    [InlineData(@"\begin{cases} a & b \end{cases}", @"\begin{cases} a & b \end{cases}")]
    // A rewrite inside a group still applies (nested).
    [InlineData(@"\frac{\dfrac{a}{b}}{c}", @"\frac{\frac{a}{b}}{c}")]
    // Untouched, per the "supported as is" list.
    [InlineData(@"\frac{a}{b} \sqrt[3]{x} \sum_{i} \int_0^1 \lim \text{a} \mathrm{b} \mathbf{c} \mathbb{R} \mathcal{L} \bm{v} \vec{v} \hat{x} \bar{x} \dot{x} \overline{x} \underbrace{x}_{y} \left( \right) \binom{n}{k} \pm \times \cdot \approx \propto \to \Rightarrow \iff \degree \% \quad",
        @"\frac{a}{b} \sqrt[3]{x} \sum_{i} \int_0^1 \lim \text{a} \mathrm{b} \mathbf{c} \mathbb{R} \mathcal{L} \bm{v} \vec{v} \hat{x} \bar{x} \dot{x} \overline{x} \underbrace{x}_{y} \left( \right) \binom{n}{k} \pm \times \cdot \approx \propto \to \Rightarrow \iff \degree \% \quad")]
    // Never throws, unbalanced braces pass through as far as they can.
    [InlineData(@"\dfrac{a}{", @"\frac{a}{")]
    [InlineData(@"\begin{align} a", @"\begin{aligned} a")]
    [InlineData(@"\", @"\")]
    public void Prepare_rewrites_what_csharpmath_cannot(string latex, string prepared)
    {
        Assert.Equal(prepared, MathText.Prepare(latex));
        // Rewriting an already-rewritten formula (a note re-typeset, or a rewrite that touches the same $…$ twice)
        // changes nothing further: none of the rewritten names are themselves rewrite targets.
        Assert.Equal(prepared, MathText.Prepare(prepared));
    }

    [Theory]
    [InlineData("H2O", "H_{2}O")]
    [InlineData("Na+ + Cl- -> NaCl", @"Na^{+} + Cl^{-} \rightarrow NaCl")]
    [InlineData("CO2 + H2O <=> H2CO3", @"CO_{2} + H_{2}O \rightleftharpoons H_{2}CO_{3}")]
    [InlineData("Ca^{2+}", "Ca^{2+}")]
    [InlineData("NaCl(aq) -> Na+ + Cl-", @"NaCl(aq) \rightarrow Na^{+} + Cl^{-}")]
    [InlineData("A <-> B", @"A \rightleftharpoons B")]
    public void Ce_reads_as_a_chemistry_formula(string ce, string mathrm) => Assert.Equal(@"\mathrm{" + mathrm + "}", MathText.Prepare($@"\ce{{{ce}}}"));

    [Theory]
    [InlineData("Ollama runs on my laptop.", "Ollama runs on my laptop.")]
    [InlineData(@"The gas law is \(PV = nRT\).", "The gas law is $PV = nRT$.")]
    [InlineData(@"On its own line: \[E = mc^2\]", "On its own line: $$E = mc^2$$")]
    [InlineData("Prices vary: \\(5\\) to \\(10\\).", "Prices vary: $5$ to $10$.")]
    [InlineData("A backtick code span: `\\(not math\\)` stays put.", "A backtick code span: `\\(not math\\)` stays put.")]
    [InlineData("```\nfenced: \\(not math\\)\n```\nafter: \\(x\\)", "```\nfenced: \\(not math\\)\n```\nafter: $x$")]
    public void DollarDelimiters_turns_backslash_parens_into_dollars_outside_code(string markdown, string converted) =>
        Assert.Equal(converted, MathText.DollarDelimiters(markdown));

    [Theory]
    [InlineData("- Assume:\n  $$\\hat{D}(A,xy)\n  =\n  \\hat{D}(B,y).$$\n\n- Next", "- Assume:\n\n  $$\n  \\hat{D}(A,xy)\n  =\n  \\hat{D}(B,y).\n  $$\n\n- Next")]
    [InlineData("$$\nx = 1\n$$", "$$\nx = 1\n$$")]
    [InlineData("Then:\n$$\nx = 1\n$$\nso x is one.", "Then:\n\n$$\nx = 1\n$$\n\nso x is one.")]
    [InlineData("Inline $$x$$ and\n$$y$$ alone", "Inline $$x$$ and\n$$y$$ alone")]
    [InlineData("$$a\n= b\n$$", "$$\na\n= b\n$$")]
    [InlineData("$$\na\n= b$$", "$$\na\n= b\n$$")]
    public void FenceFormulas_puts_each_dollar_fence_on_its_own_line(string markdown, string fenced) =>
        Assert.Equal(fenced, MathText.FenceFormulas(markdown));

    [Theory]
    [InlineData("Then:\n$$\nx = 1\n$$.\nSo x is one.", "Then:\n\n$$\nx = 1.\n$$\n\nSo x is one.")]
    [InlineData("- Then:\n  $$a\n  = b$$,\n\n- Next", "- Then:\n\n  $$\n  a\n  = b,\n  $$\n\n- Next")]
    [InlineData("Then:\n$$\nx = 1\n$$\nSo x is one.", "Then:\n\n$$\nx = 1\n$$\n\nSo x is one.")]
    [InlineData("Then $x = 1$. And $$y = 2$$. Done.", "Then $x = 1$. And $$y = 2$$. Done.")]
    public void FenceFormulas_keeps_a_full_stop_after_the_closing_dollars_inside_the_formula(string markdown, string fenced) =>
        Assert.Equal(fenced, MathText.FenceFormulas(markdown));

    [Theory]
    [InlineData(". Attention on its own", ".")]
    [InlineData(", so that", ",")]
    [InlineData(": the", "{:}")]
    [InlineData(".5 of it", null)]
    [InlineData("... and on", null)]
    [InlineData(" and so on", null)]
    public void TrailingPunctuation_takes_the_mark_that_follows_a_formula(string after, string? mark) =>
        Assert.Equal(mark, MathText.TrailingPunctuation(after));

    [Fact]
    public void Prepare_drops_bracket_sizes_and_keeps_the_brackets() =>
        Assert.Equal(@"\hat{D}(\hat{D}(A,x),y)\{a\}", MathText.Prepare(@"\hat{D}\bigl(\hat{D}(A,x),y\bigr)\Bigl\{a\Bigr\}"));

    [Fact]
    public void Prepare_never_throws_on_garbled_input()
    {
        var rng = new Random(1);
        string[] alphabet = ["\\", "{", "}", "$", "[", "]", "^", "_", "*", "a", " ", "begin", "end", "align", "ce", "SI",
            "overset", "not", "=", "\n", "\t"];
        for (int i = 0; i < 300; i++)
        {
            string s = string.Concat(Enumerable.Range(0, rng.Next(1, 40)).Select(_ => alphabet[rng.Next(alphabet.Length)]));
            var ex = Record.Exception(() => MathText.Prepare(s));
            Assert.Null(ex);
        }
    }

    [Fact]
    public void RepairJsonEscapes_restores_commands_a_json_decoder_ate_inside_dollars_only()
    {
        Assert.Equal(@"The dose is $\frac{500}{250} \times 5$ mL.", MathText.RepairJsonEscapes("The dose is $\f" + "rac{500}{250} \t" + "imes 5$ mL."));
        Assert.Equal(@"$\beta$ decay", MathText.RepairJsonEscapes("$\b" + "eta$ decay"));
        Assert.Equal(@"$\rho$ and $\rightarrow$", MathText.RepairJsonEscapes("$\r" + "ho$ and $\r" + "ightarrow$"));
        Assert.Equal(@"$\nu$", MathText.RepairJsonEscapes("$\n" + "u$"));
        // A real newline between sentences, even one that touches a formula, is left alone.
        Assert.Equal("First sentence.\nSecond sentence with $x$ in it.", MathText.RepairJsonEscapes("First sentence.\nSecond sentence with $x$ in it."));
        Assert.Equal("Outside a formula, a lone \f stays put.", MathText.RepairJsonEscapes("Outside a formula, a lone \f stays put."));
    }
}
