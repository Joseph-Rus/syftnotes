using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using StudyStash.App.Controls;
using StudyStash.App.Controls.Rich;

namespace StudyStash.App.Tests;

/// <summary>Formulas in a note: inline <c>$…$</c> on the text's baseline, <c>$$…$$</c> centred on its own line, and
/// a calm fallback for a formula that won't typeset — both looks, light and dark.</summary>
public class NoteViewMathTests
{
    static Window Show(Control content, SkinKind skin = SkinKind.Mac, ThemeVariant? variant = null, double width = 620)
    {
        Skin.UseTheme(ColourThemes.Default);
        ((App)Application.Current!).UseSkin(skin);
        content.Width = width;
        content.HorizontalAlignment = HorizontalAlignment.Left;
        content.VerticalAlignment = VerticalAlignment.Top;
        var window = new Window { Width = width + 40, Height = 900, RequestedThemeVariant = variant ?? ThemeVariant.Light, Content = content };
        window.Show();
        DiagramsReady.Wait(window);
        return window;
    }

    static IEnumerable<MathView> MathViews(Control root) => root.GetLogicalDescendants().OfType<InlineUIContainer>().Select(c => c.Child).OfType<MathView>();

    [AvaloniaFact]
    public void An_inline_formula_sits_in_the_paragraph_as_a_math_view()
    {
        var note = new NoteView { Markdown = "Cardiac output is $\\text{CO} = \\text{HR} \\times \\text{SV}$, in litres a minute." };
        var window = Show(note);
        var mv = Assert.Single(MathViews(note));
        Assert.False(mv.Display);
        Assert.Null(mv.ErrorMessage);
        Assert.True(mv.Bounds.Width > 0);
        window.Close();
    }

    [AvaloniaFact]
    public void A_display_formula_is_its_own_centred_math_display()
    {
        // The AI writes a display formula's $$ alone on their own lines — Markdig's block parser needs that, just
        // like a fenced code block, to tell it apart from $$x$$ inline (DelimiterCount 2, still in a sentence).
        var note = new NoteView { Markdown = "The formula:\n\n$$\n\\text{MAP} = \\text{DBP} + \\frac{1}{3}(\\text{SBP} - \\text{DBP})\n$$\n\nRead as above." };
        var window = Show(note);
        var display = Assert.Single(note.GetLogicalDescendants().OfType<MathDisplay>());
        var mv = Assert.Single(display.GetLogicalDescendants().OfType<MathView>());
        Assert.True(mv.Display);
        Assert.Null(mv.ErrorMessage);
        window.Close();
    }

    [AvaloniaFact]
    public void A_display_formula_in_a_bullet_with_its_latex_beside_the_dollars_is_typeset_whole()
    {
        // The AI sometimes writes $$ against the formula's first and last line inside a bullet: Markdig alone took
        // the first line for the fence's info, never found the close, and the bullet's text and formula were lost.
        var note = new NoteView { Markdown = "- Assume that the claim holds for $y$:\n  $$\\widehat{\\Delta}(A,xy)\n  =\n  \\widehat{\\Delta}\\bigl(\\widehat{\\Delta}(A,x),y\\bigr).$$\n\n- Next" };
        var window = Show(note);
        var display = Assert.Single(note.GetLogicalDescendants().OfType<MathDisplay>());
        var mv = Assert.Single(display.GetLogicalDescendants().OfType<MathView>());
        Assert.Null(mv.ErrorMessage);
        Assert.Contains(@"\widehat{\Delta}(A,xy)", mv.Latex);
        Assert.DoesNotContain("$$", mv.Latex);
        var texts = note.GetLogicalDescendants().OfType<TextBlock>().Select(t => string.Concat(t.Inlines?.OfType<Run>().Select(r => r.Text) ?? [])).ToList();
        Assert.Contains(texts, t => t.Contains("Assume that the claim holds"));
        window.Close();
    }

    static List<string> Words(NoteView note) => [.. note.GetLogicalDescendants().OfType<TextBlock>().Select(t => string.Concat(t.Inlines?.OfType<Run>().Select(r => r.Text) ?? []))];

    [AvaloniaFact]
    public void A_full_stop_after_a_tall_formula_is_drawn_after_it_not_starting_the_next_line()
    {
        // An answer's "…the values: $$…$$. Attention on its own…": the tall formula is lifted onto a line of its own, and
        // the full stop that belongs to it went with the words below it.
        var note = new NoteView { Markdown = "Scores are used to weight the values: $$\\text{Attention}(Q,K,V)=\\text{softmax}\\left(\\frac{QK^T}{\\sqrt{d_k}}\\right)V$$. Attention on its own ignores word order." };
        var window = Show(note);
        var mv = Assert.Single(note.GetLogicalDescendants().OfType<MathDisplay>().SelectMany(d => d.GetLogicalDescendants().OfType<MathView>()));
        Assert.EndsWith("V.", mv.Latex);
        Assert.Null(mv.ErrorMessage);
        Assert.DoesNotContain(Words(note), t => t.StartsWith('.') || t.StartsWith(" ."));
        Assert.Contains(Words(note), t => t == "Attention on its own ignores word order.");
        window.Close();
    }

    [AvaloniaFact]
    public void A_tall_formula_with_no_punctuation_after_it_and_inline_formulas_stay_as_written()
    {
        var note = new NoteView { Markdown = "Scores weight the values: $$\\text{softmax}\\left(\\frac{QK^T}{\\sqrt{d_k}}\\right)V$$ and so on, with $x = 1$. Done." };
        var window = Show(note);
        var tall = Assert.Single(note.GetLogicalDescendants().OfType<MathDisplay>().SelectMany(d => d.GetLogicalDescendants().OfType<MathView>()));
        Assert.EndsWith("V", tall.Latex);
        Assert.Contains(Words(note), t => t.StartsWith(" and so on, with "));
        var inline = Assert.Single(MathViews(note), m => !m.Display);
        Assert.Equal("x = 1", inline.Latex);
        Assert.Contains(Words(note), t => t.EndsWith(". Done."));
        window.Close();
    }

    [AvaloniaFact]
    public void A_display_formula_closed_with_a_full_stop_after_its_dollars_is_typeset_with_the_stop_inside()
    {
        var note = new NoteView { Markdown = "So:\n$$\n\\frac{a}{b} = c\n$$.\nThat is all, and here is more." };
        var window = Show(note);
        var display = Assert.Single(note.GetLogicalDescendants().OfType<MathDisplay>());
        var mv = Assert.Single(display.GetLogicalDescendants().OfType<MathView>());
        Assert.EndsWith("= c.", mv.Latex!.Trim());
        Assert.Contains(Words(note), t => t.Contains("That is all, and here is more."));
        window.Close();
    }

    [AvaloniaFact]
    public void A_formula_csharpmath_cannot_typeset_shows_its_source_calmly_instead()
    {
        var note = new NoteView { Markdown = "An inline one that fails: $\\frac{a}{$ here.\n\n$$\n\\frac{a}{\n$$" };
        var window = Show(note);
        Assert.Empty(MathViews(note));
        Assert.Empty(note.GetLogicalDescendants().OfType<MathDisplay>());
        var texts = note.GetLogicalDescendants().OfType<TextBlock>().Select(t => string.Concat(t.Inlines!.OfType<Run>().Select(r => r.Text))).ToList();
        Assert.Contains(texts, t => t.Contains(@"$\frac{a}{$"));
        Assert.Contains(note.GetLogicalDescendants().OfType<TextBlock>(), t => t.Text == "Couldn't typeset this formula.");
        window.Close();
    }

    [AvaloniaFact]
    public void Every_formula_renders_in_both_looks_light_and_dark()
    {
        string markdown = "Inline $x^2 + y^2 = z^2$ here.\n\n$$\n\\frac{a}{b} = c\n$$\n\nDone.";
        foreach (var skin in new[] { SkinKind.Mac, SkinKind.Win })
            foreach (var variant in new[] { ThemeVariant.Light, ThemeVariant.Dark })
            {
                var note = new NoteView { Markdown = markdown };
                var window = Show(note, skin, variant, skin == SkinKind.Mac ? 620 : 640);
                Assert.Single(MathViews(note));
                Assert.Single(note.GetLogicalDescendants().OfType<MathDisplay>());
                window.Close();
            }
    }

    [AvaloniaFact]
    public void A_definitions_meaning_keeps_its_maths_coloured_like_the_rest_of_the_meaning()
    {
        var note = new NoteView { Markdown = "## Definitions\n\n- **Stroke volume**: the blood ejected each beat, $SV = EDV - ESV$." };
        var window = Show(note);
        var mv = Assert.Single(MathViews(note));
        Assert.Null(mv.ErrorMessage);
        window.Close();
    }

    [AvaloniaFact]
    public void A_tall_inline_formula_is_lifted_onto_its_own_line()
    {
        var note = new NoteView
        {
            Markdown = "Before the matrix, $\\begin{cases} a & x > 0 \\\\ b & x \\le 0 \\\\ c & x = 0 \\end{cases}$, after it.",
        };
        var window = Show(note);
        var lifted = Assert.Single(note.GetLogicalDescendants().OfType<MathDisplay>());
        var mv = Assert.Single(lifted.GetLogicalDescendants().OfType<MathView>());
        Assert.True(mv.Display);
        window.Close();
    }

    [AvaloniaFact]
    public void A_short_inline_formula_is_never_lifted()
    {
        var note = new NoteView { Markdown = "Just $x + 1$ here, nothing tall about it." };
        var window = Show(note);
        Assert.Empty(note.GetLogicalDescendants().OfType<MathDisplay>());
        Assert.Single(MathViews(note));
        window.Close();
    }

    [AvaloniaFact]
    public void A_formula_over_the_length_cap_goes_straight_to_its_fallback()
    {
        string huge = string.Concat(Enumerable.Repeat("x + ", 700));
        var note = new NoteView { Markdown = $"Inline: ${huge}x$ end." };
        var window = Show(note);
        Assert.Empty(MathViews(note));
        window.Close();
    }
}
