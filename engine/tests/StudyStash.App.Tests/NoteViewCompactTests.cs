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
using StudyStash.App.Views;
using StudyStash.App.ViewModels;

namespace StudyStash.App.Tests;

/// <summary>A quick answer or a chat bubble's answer, drawn by <see cref="NoteView"/>'s Compact mode: the caller's own
/// type, no room above a heading, selectable paragraphs, a formula on the baseline, and a diagram kept small in the
/// column (the ask chat's bubble stays within its own max width).</summary>
public class NoteViewCompactTests
{
    static Window Show(Control content, SkinKind skin = SkinKind.Mac, ThemeVariant? variant = null, double width = 317)
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

    [AvaloniaTheory]
    [InlineData(SkinKind.Mac)]
    [InlineData(SkinKind.Win)]
    public void A_compact_answer_measures_and_renders_in_both_looks(SkinKind skin)
    {
        foreach (var variant in new[] { ThemeVariant.Light, ThemeVariant.Dark })
        {
            var note = new NoteView { Compact = true, BodySize = 13, BodyLineHeight = 18.85, Markdown = "Cardiac output is normally 4 to 8 litres a minute." };
            var window = Show(note, skin, variant);
            Assert.True(note.DesiredSize.Height > 0);
            var text = Assert.Single(note.GetLogicalDescendants().OfType<SelectableTextBlock>());
            Assert.Equal(13, text.FontSize);
            Assert.Equal(18.85, text.LineHeight);
            window.Close();
        }
    }

    [AvaloniaFact]
    public void A_compact_heading_has_no_top_margin()
    {
        var note = new NoteView { Compact = true, BodySize = 13, BodyLineHeight = 18.85, Markdown = "## From your notes\n\nA short answer." };
        var window = Show(note);
        var heading = Assert.Single(note.GetLogicalDescendants().OfType<TextBlock>(), t => t is not SelectableTextBlock);
        Assert.Equal(new Thickness(0), heading.Margin);
        window.Close();
    }

    [AvaloniaFact]
    public void A_formula_in_a_compact_answer_typesets_at_the_callers_size()
    {
        var note = new NoteView { Compact = true, BodyFont = "SerifFont", BodySize = 17, BodyLineHeight = 26.35, Markdown = @"Mean arterial pressure, $\text{MAP} = \text{DBP} + \frac{1}{3}(\text{SBP} - \text{DBP})$, should stay above 65 mmHg." };
        var window = Show(note, width: 620);
        var mv = Assert.Single(note.GetLogicalDescendants().OfType<InlineUIContainer>().Select(c => c.Child).OfType<MathView>());
        Assert.False(mv.Display);
        Assert.Null(mv.ErrorMessage);
        Assert.Equal(17 * 0.94, mv.Size);
        window.Close();
    }

    [AvaloniaFact]
    public void A_diagram_in_a_compact_answer_never_grows_past_260_high()
    {
        string markdown = "A quick flowchart:\n\n```mermaid\n" + RichDemo.PainReassess + "\n```";
        var note = new NoteView { Compact = true, BodySize = 13, BodyLineHeight = 18.85, Markdown = markdown };
        var window = Show(note, width: 300);
        var diagram = Assert.Single(note.GetLogicalDescendants().OfType<DiagramView>());
        Assert.True(diagram.Bounds.Height <= NoteView.CompactDiagramMaxHeight + 0.5);
        window.Close();
    }

    /// <summary>The recorder/quick panel's ask chat (design 16): a turn whose answer has a small flowchart draws it
    /// as a <see cref="DiagramView"/> that never grows past the bubble's own max width.</summary>
    [AvaloniaFact]
    public void The_chat_bubble_draws_a_diagram_no_wider_than_its_own_max_width()
    {
        var model = new AiAskModel(new FakeAiLibrary()) { LectureId = "l1", ClassName = "BIO 110" };
        var turn = new AiTurn("How does blood flow through the heart?", "Ollama")
        {
            Answer = "Oxygen-poor blood goes right, oxygen-rich blood goes left:\n\n```mermaid\n" + RichDemo.BloodFlow + "\n```",
            Byline = "Ollama · 4:10",
        };
        model.Turns.Add(turn);
        var chat = new MacAiAskChat { DataContext = model };
        var window = Show(chat, width: 400);
        var diagram = Assert.Single(chat.GetLogicalDescendants().OfType<DiagramView>());
        Assert.True(diagram.Bounds.Width <= 317.5, $"was {diagram.Bounds.Width}");
        window.Close();
    }
}
