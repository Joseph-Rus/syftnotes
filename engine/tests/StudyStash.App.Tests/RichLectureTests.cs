using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StudyStash.App.Controls;
using StudyStash.App.Controls.Rich;

namespace StudyStash.App.Tests;

/// <summary>The demo cardiac-cycle lecture (<see cref="RichDemo.CardiacLecture"/>), the note that carries every rich
/// piece at once: its three diagrams all draw (none falls back to a calm card), and its formulas all typeset.</summary>
public class RichLectureTests
{
    static Window Show(Control content, SkinKind skin, ThemeVariant variant, double width = 620)
    {
        Skin.UseTheme(ColourThemes.Default);
        ((App)Application.Current!).UseSkin(skin);
        content.Width = width;
        content.HorizontalAlignment = HorizontalAlignment.Left;
        var window = new Window { Width = width + 40, Height = 3200, RequestedThemeVariant = variant, Content = content };
        window.Show();
        DiagramsReady.Wait(window);
        return window;
    }

    [AvaloniaTheory]
    [InlineData(SkinKind.Mac)]
    [InlineData(SkinKind.Win)]
    public void The_lectures_notes_build_in_both_looks_light_and_dark(SkinKind skin)
    {
        foreach (var variant in new[] { ThemeVariant.Light, ThemeVariant.Dark })
        {
            var note = new NoteView { Markdown = RichDemo.CardiacLecture };
            var window = Show(note, skin, variant);
            Assert.Empty(note.GetLogicalDescendants().OfType<DiagramCard>());
            // The cardiac cycle, blood flow and the low-BP decision chart (all Mermaid) plus the four chambers (SVG).
            Assert.Equal(4, note.GetLogicalDescendants().OfType<DiagramView>().Count() + note.GetLogicalDescendants().OfType<SvgView>().Count());
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Every_formula_in_the_lecture_typesets()
    {
        var note = new NoteView { Markdown = RichDemo.CardiacLecture };
        var window = Show(note, SkinKind.Mac, ThemeVariant.Light);
        // A formula that couldn't typeset falls back to mono source text; none of the lecture's should.
        var mono = note.GetLogicalDescendants().OfType<TextBlock>()
            .Where(t => t.FontFamily is { Name: var n } && n.Contains("Mono", StringComparison.OrdinalIgnoreCase))
            .Select(t => string.Concat(t.Inlines?.OfType<Avalonia.Controls.Documents.Run>().Select(r => r.Text) ?? []));
        Assert.DoesNotContain(mono, t => t.Contains('$') || t.Contains("\\text"));
        window.Close();
    }

    /// <summary>The self-test's own notes, which the self-test checks the app draws: they survive the notes pipeline's
    /// cleaning, and hold four formulas that typeset (three inline, one on its own line) and a flowchart that lays out.</summary>
    [AvaloniaFact]
    public void The_self_tests_notes_carry_formulas_and_a_flowchart_that_draw()
    {
        string notes = Core.Summarize.CleanOutput(SelfTestEngine.Notes());
        Assert.Contains(SelfTestEngine.NotesMarker, notes);
        var note = new NoteView { Markdown = notes };
        var window = Show(note, SkinKind.Win, ThemeVariant.Light);
        var formulas = note.GetVisualDescendants().OfType<MathView>().ToList();
        Assert.Equal(4, formulas.Count);
        Assert.All(formulas, f => Assert.Null(f.ErrorMessage));
        Assert.Single(formulas, f => f.Display);
        Assert.NotNull(Assert.Single(note.GetVisualDescendants().OfType<DiagramView>()).Scene);
        window.Close();
    }
}
