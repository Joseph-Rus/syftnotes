using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StudyStash.App.Controls;

namespace StudyStash.App.Tests;

/// <summary>A student types their notes in Markdown, as they would in Notion, Bear or Obsidian: it shows formatted as
/// they type, the keys they know work, it stays Markdown underneath, and the written-up notes show it formatted, theirs
/// in ink.</summary>
public class TypedMarkdownTests
{
    static RawInputModifiers Command => OperatingSystem.IsMacOS() ? RawInputModifiers.Meta : RawInputModifiers.Control;

    static Window Show(Control content)
    {
        Skin.UseTheme(ColourThemes.Default);
        ((App)Application.Current!).UseSkin(SkinKind.Mac);
        content.HorizontalAlignment = HorizontalAlignment.Left;
        content.VerticalAlignment = VerticalAlignment.Top;
        var window = new Window { Width = 660, Height = 700, RequestedThemeVariant = ThemeVariant.Light, Content = content };
        window.Show();
        DiagramsReady.Wait(window);
        return window;
    }

    static void Settle()
    {
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>A field's text as it's drawn: each run's words, weight, and whether it's out of sight (a mark hidden
    /// while the caret's in another line).</summary>
    static List<(string Text, FontWeight Weight, bool Hidden)> Drawn(TextBox box)
    {
        var presenter = box.GetVisualDescendants().OfType<TextPresenter>().Single();
        return [.. presenter.TextLayout.TextLines.SelectMany(l => l.TextRuns).OfType<ShapedTextRun>()
            .Select(r => (r.Text.ToString(), r.Properties.Typeface.Weight, r.Properties.FontRenderingEmSize < 1))];
    }

    [AvaloniaFact]
    public void Markdown_typed_while_it_records_shows_formatted_and_stays_Markdown()
    {
        var editor = new TypedEditor { Width = 600 };
        var w = Show(editor);
        try
        {
            void Key(Key key, RawInputModifiers modifiers = RawInputModifiers.None)
            {
                w.KeyPress(key, modifiers, PhysicalKey.None, null);
                Settle();
            }
            // A key at a time, as a student types: "# " makes a heading, "- " a bullet, the moment the space is typed.
            void Type(string text)
            {
                foreach (char c in text) w.KeyTextInput(c.ToString());
                Settle();
            }
            editor.Fields[0].Focus();
            Settle();
            Type("# Cell division");
            Assert.Equal(("# Cell division", "Cell division"), (editor.Text, editor.Fields[0].Text));
            Key(Avalonia.Input.Key.Enter);
            // **Mitosis** is bold as it's typed, its marks dimmed while the caret is in the line.
            Type("- **Mitosis**: cell division");
            Assert.Equal("# Cell division\n- **Mitosis**: cell division", editor.Text);
            Assert.Equal(["", "•"], editor.Markers);
            var drawn = Drawn(editor.Fields[1]);
            Assert.Contains(drawn, r => r.Text == "Mitosis" && r.Weight == FontWeight.ExtraBold);
            Assert.Contains(drawn, r => r.Text == ": cell division" && r.Weight == FontWeight.SemiBold);
            Assert.Contains(drawn, r => r.Text == "**" && !r.Hidden);
            // Return: the next bullet. The line left reads as it will: its marks out of sight.
            Key(Avalonia.Input.Key.Enter);
            Assert.Equal("# Cell division\n- **Mitosis**: cell division\n- ", editor.Text);
            Assert.All(Drawn(editor.Fields[1]).Where(r => r.Text.Contains('*')), r => Assert.True(r.Hidden));

            // ⌘B (Ctrl+B) makes what's selected bold, and again takes the bold off; ⌘I with nothing selected makes
            // what's typed next italic.
            var second = editor.Fields[2];
            Type("anaphase splits the pairs");
            second.SelectionStart = 0;
            second.SelectionEnd = "anaphase".Length;
            Key(Avalonia.Input.Key.B, Command);
            Assert.Equal("# Cell division\n- **Mitosis**: cell division\n- **anaphase** splits the pairs", editor.Text);
            Assert.Equal("anaphase", second.SelectedText);
            Key(Avalonia.Input.Key.B, Command);
            Assert.EndsWith("\n- anaphase splits the pairs", editor.Text);
            second.SelectionStart = second.SelectionEnd = second.CaretIndex = second.Text!.Length;
            Type(" ");
            Key(Avalonia.Input.Key.I, Command);
            Type("fast");
            Assert.EndsWith("\n- anaphase splits the pairs *fast*", editor.Text);
            // With the caret in a word and nothing selected, the word is what goes bold, and plain again.
            second.SelectionStart = second.SelectionEnd = second.CaretIndex = 3;
            Key(Avalonia.Input.Key.B, Command);
            Assert.EndsWith("\n- **anaphase** splits the pairs *fast*", editor.Text);
            Key(Avalonia.Input.Key.B, Command);
            Assert.EndsWith("\n- anaphase splits the pairs *fast*", editor.Text);

            // "[ ] " in a bullet makes a checkbox, which ticks; Tab moves it in under the bullet above, Shift-Tab out.
            second.CaretIndex = second.SelectionStart = second.SelectionEnd = second.Text!.Length;
            Key(Avalonia.Input.Key.Enter);
            Type("[ ] read chapter 12");
            Assert.EndsWith("\n- [ ] read chapter 12", editor.Text);
            editor.Ticks[3]!.IsChecked = true;
            Settle();
            Assert.EndsWith("\n- [x] read chapter 12", editor.Text);
            Key(Avalonia.Input.Key.Tab);
            Assert.EndsWith("\n  - [x] read chapter 12", editor.Text);
            Assert.True(editor.Fields[3].IsFocused);
            Key(Avalonia.Input.Key.Tab, RawInputModifiers.Shift);
            Assert.EndsWith("\n- [x] read chapter 12", editor.Text);

            // Return carries a numbered list on with the next number; Return on an empty item ends the list.
            editor.Fields[3].CaretIndex = editor.Fields[3].Text!.Length;
            Key(Avalonia.Input.Key.Enter);
            Assert.EndsWith("\n- [ ] ", editor.Text);
            Key(Avalonia.Input.Key.Enter);
            Type("1. DNA copies itself");
            Key(Avalonia.Input.Key.Enter);
            Type("chromosomes line up");
            Assert.EndsWith("\n- [x] read chapter 12\n1. DNA copies itself\n2. chromosomes line up", editor.Text);
            Assert.Equal(["", "•", "•", "", "1.", "2."], editor.Markers);
            // Backspace at the start of an item makes it plain text again, its words kept.
            editor.Fields[5].CaretIndex = editor.Fields[5].SelectionStart = editor.Fields[5].SelectionEnd = 0;
            Key(Avalonia.Input.Key.Back);
            Assert.EndsWith("\n1. DNA copies itself\nchromosomes line up", editor.Text);
        }
        finally
        {
            w.Close();
        }
    }

    [AvaloniaFact]
    public void Editing_the_written_notes_carries_lists_on_and_takes_the_bold_key()
    {
        var notes = new MarkdownBox { Width = 600, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, WholeNotes = true, Text = "## Cell division\n- mitosis" };
        notes.Classes.Add("bare");
        var w = Show(notes);
        try
        {
            void Key(Key key, RawInputModifiers modifiers = RawInputModifiers.None)
            {
                w.KeyPress(key, modifiers, PhysicalKey.None, null);
                Dispatcher.UIThread.RunJobs();
            }
            notes.Focus();
            notes.CaretIndex = notes.Text!.Length;
            // Return in a list: the next bullet. Tab moves it in under the one above; Return on it empty moves it out,
            // and on an empty bullet at the left, ends the list.
            Key(Avalonia.Input.Key.Enter);
            Assert.Equal("## Cell division\n- mitosis\n- ", notes.Text);
            Key(Avalonia.Input.Key.Tab);
            Assert.Equal("## Cell division\n- mitosis\n  - ", notes.Text);
            w.KeyTextInput("prophase");
            Key(Avalonia.Input.Key.Enter);
            Key(Avalonia.Input.Key.Enter);
            Assert.Equal("## Cell division\n- mitosis\n  - prophase\n- ", notes.Text);
            Key(Avalonia.Input.Key.Enter);
            Assert.Equal("## Cell division\n- mitosis\n  - prophase\n", notes.Text);
            // ⌘B round a selected word; the heading's line is drawn heavy, its #s faint.
            notes.SelectionStart = "## Cell division\n- ".Length;
            notes.SelectionEnd = notes.SelectionStart + "mitosis".Length;
            Key(Avalonia.Input.Key.B, Command);
            Assert.Equal("## Cell division\n- **mitosis**\n  - prophase\n", notes.Text);
            var drawn = Drawn(notes);
            Assert.Contains(drawn, r => r.Text == "Cell division" && r.Weight == FontWeight.Bold);
            Assert.Contains(drawn, r => r.Text == "mitosis" && r.Weight == FontWeight.Bold);
        }
        finally
        {
            w.Close();
        }
    }

    [AvaloniaFact]
    public void The_written_notes_show_the_students_Markdown_formatted_and_in_ink()
    {
        var note = new NoteView
        {
            Width = 600, Provenance = true,
            Markdown = """
                ## Summary
                How cells divide.

                ## ==Cell division==
                - ==**Mitosis**: one cell → two== The cell copies its DNA, then splits.
                - ==~~interphase~~ prophase first==
                - [x] ==email the TA==
                """,
        };
        var w = Show(note);
        try
        {
            var ink = ((ISolidColorBrush)note.FindResource(ThemeVariant.Light, "Ink")!).Color;
            var muted = ((ISolidColorBrush)note.FindResource(ThemeVariant.Light, "InkMuted")!).Color;
            Color Colour(TextBlock t, Run r) => ((ISolidColorBrush)(r.Foreground ?? t.Foreground)!).Color;
            var texts = note.GetVisualDescendants().OfType<TextBlock>().Where(t => t.Inlines is { Count: > 0 }).ToList();
            TextBlock Starting(string words) => texts.Single(t => t.Inlines!.Text!.StartsWith(words, StringComparison.Ordinal));

            // The student's bold reads bolder than the rest of their words, both in ink; what was added is grey.
            var mitosis = Starting("Mitosis");
            Assert.Equal("Mitosis: one cell → two The cell copies its DNA, then splits.", mitosis.Inlines!.Text);
            var runs = mitosis.Inlines.OfType<Run>().ToList();
            var bold = runs.Single(r => r.Text == "Mitosis");
            Assert.Equal((FontWeight.ExtraBold, ink), (bold.FontWeight, Colour(mitosis, bold)));
            var theirs = runs.Single(r => r.Text == ": one cell → two");
            Assert.Equal((FontWeight.SemiBold, ink), (theirs.FontWeight, Colour(mitosis, theirs)));
            Assert.Equal(muted, Colour(mitosis, runs.Single(r => r.Text!.Contains("copies"))));

            // Struck through is struck through (not bold).
            var struck = Starting("interphase").Inlines!.OfType<Run>().Single(r => r.Text == "interphase");
            Assert.Equal((TextDecorations.Strikethrough, FontWeight.SemiBold), (struck.TextDecorations, struck.FontWeight));

            // A checkbox is drawn ticked in the bullet's place, its words the student's.
            Assert.Contains(note.GetVisualDescendants().OfType<Border>(), b => AutomationProperties.GetName(b) == "Done");
            var task = Starting("email the TA");
            Assert.Equal(ink, Colour(task, task.Inlines!.OfType<Run>().Single()));
            Assert.DoesNotContain(texts, t => t.Inlines!.Text!.Contains("[x]", StringComparison.Ordinal));
        }
        finally
        {
            w.Close();
        }
    }
}
