using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StudyStash.App.Controls;

namespace StudyStash.App.Tests;

/// <summary>A lecture's notes selected with the mouse and copied, as a student does to paste part of them somewhere
/// else: one drag runs from a paragraph down through a list, the copy keeps the bullets, a table's cells and a formula's
/// LaTeX; a click on a link still opens it.</summary>
public class NoteSelectionTests
{
    const string Notes = """
        ## Cardiac output

        The heart pumps about five litres a minute at rest, $CO = SV \times HR$.

        - Stroke volume is about 70 mL
        - Heart rate is about 70 beats a minute

        | Term | Value |
        |---|---|
        | CO | SV × HR |

        Read more in [the textbook](https://example.com/heart).
        """;

    static Window Show(NoteView note, SkinKind skin)
    {
        Skin.UseTheme(ColourThemes.Default);
        ((App)Application.Current!).UseSkin(skin);
        note.Width = 620;
        note.HorizontalAlignment = HorizontalAlignment.Left;
        note.VerticalAlignment = VerticalAlignment.Top;
        var window = new Window { Width = 660, Height = 900, RequestedThemeVariant = ThemeVariant.Light, Content = note };
        window.Show();
        DiagramsReady.Wait(window);
        return window;
    }

    static NoteText Piece(NoteView note, string starting) =>
        note.GetVisualDescendants().OfType<NoteText>().Single(t => Words(t).StartsWith(starting, StringComparison.Ordinal));

    static string Words(NoteText t) => t.Inlines is { Count: > 0 } inlines ? inlines.Text ?? "" : t.Text ?? "";

    /// <summary>Where in the window the character <paramref name="index"/> of <paramref name="text"/> begins, half way
    /// down its line.</summary>
    static Point At(NoteText text, int index)
    {
        var box = text.TextLayout.HitTestTextPosition(index);
        return text.TranslatePoint(new Point(box.X + 1 + text.Padding.Left, box.Y + box.Height / 2 + text.Padding.Top), TopLevel.GetTopLevel(text)!)!.Value;
    }

    static void Drag(Window window, Point from, Point to)
    {
        window.MouseMove(from);
        window.MouseDown(from, MouseButton.Left);
        window.MouseMove(new Point((from.X + to.X) / 2, (from.Y + to.Y) / 2), RawInputModifiers.LeftMouseButton);
        window.MouseMove(to, RawInputModifiers.LeftMouseButton);
        window.MouseUp(to, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }

    static void Press(Window window, KeyGesture gesture)
    {
        window.KeyPress(gesture.Key, (RawInputModifiers)gesture.KeyModifiers, PhysicalKey.None, null);
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaTheory]
    [InlineData(SkinKind.Mac)]
    [InlineData(SkinKind.Win)]
    public async Task A_drag_selects_from_a_paragraph_down_through_a_list_and_copy_keeps_the_bullets(SkinKind skin)
    {
        var note = new NoteView { Markdown = Notes };
        var window = Show(note, skin);
        var paragraph = Piece(note, "The heart");
        var second = Piece(note, "Heart rate");

        Drag(window, At(paragraph, Words(paragraph).IndexOf("five", StringComparison.Ordinal)), At(second, Words(second).IndexOf(" beats", StringComparison.Ordinal)));

        Assert.Equal("five litres a minute at rest, $CO = SV \\times HR$.\n• Stroke volume is about 70 mL\n• Heart rate is about 70", note.SelectedText);
        // Each piece shows its part of it; the table, past the end, none.
        Assert.Equal(Words(paragraph).IndexOf("five", StringComparison.Ordinal), paragraph.SelectionStart);
        Assert.Equal(Words(paragraph).Length, paragraph.SelectionEnd);
        Assert.Equal(0, Piece(note, "CO").SelectionEnd);

        // ⌘C (Ctrl+C on Windows): the notes took the focus with the press.
        var keys = Application.Current!.PlatformSettings!.HotkeyConfiguration;
        Press(window, keys.Copy[0]);
        Assert.Equal(note.SelectedText, await window.Clipboard!.TryGetTextAsync());

        // ⌘A (Ctrl+A) takes all of it: the heading, a table's cells a tab apart, the link's words.
        Press(window, keys.SelectAll[0]);
        Assert.Equal("Cardiac output\nThe heart pumps about five litres a minute at rest, $CO = SV \\times HR$.\n• Stroke volume is about 70 mL\n"
            + "• Heart rate is about 70 beats a minute\nTerm\tValue\nCO\tSV × HR\nRead more in the textbook.", note.SelectedText);

        // A click on the notes ends the selection.
        var click = At(second, 3);
        window.MouseDown(click, MouseButton.Left);
        window.MouseUp(click, MouseButton.Left);
        Assert.Equal("", note.SelectedText);
        Assert.Equal(paragraph.SelectionStart, paragraph.SelectionEnd);
        window.Close();
    }

    [AvaloniaFact]
    public void A_click_on_a_link_still_opens_it_and_a_drag_from_it_selects_instead()
    {
        var opened = new List<string>();
        var open = NoteView.OpenLink;
        NoteView.OpenLink = opened.Add;
        try
        {
            var note = new NoteView { Markdown = Notes };
            var window = Show(note, SkinKind.Mac);
            var line = Piece(note, "Read more");
            var link = At(line, Words(line).IndexOf("textbook", StringComparison.Ordinal));
            window.MouseMove(link);
            window.MouseDown(link, MouseButton.Left);
            window.MouseUp(link, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(["https://example.com/heart"], opened);
            Assert.Equal("", note.SelectedText);

            // (Started a few letters along: a second press where the first was is a double click, which takes the word.)
            Drag(window, At(line, Words(line).IndexOf("book", StringComparison.Ordinal)), At(line, 0));
            Assert.Single(opened);
            Assert.Equal("Read more in the text", note.SelectedText);
            window.Close();
        }
        finally
        {
            NoteView.OpenLink = open;
        }
    }

    [Theory]
    [InlineData("the heart's left side", 6, "heart's")]
    [InlineData("the heart's left side", 3, "the")]
    [InlineData("two  spaces", 4, " ")]
    [InlineData("pumps blood", 5, "pumps")]
    [InlineData("70 mL", 0, "70")]
    public void A_double_click_takes_the_word_it_lands_on(string text, int at, string word)
    {
        var (start, end) = NoteView.WordAround(text, at);
        Assert.Equal(word, text[start..end]);
    }
}
