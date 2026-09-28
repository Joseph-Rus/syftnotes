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
}
