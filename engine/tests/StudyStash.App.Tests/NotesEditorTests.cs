using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StudyStash.App.ViewModels;
using StudyStash.App.Views;
using StudyStash.Core.Ai;

namespace StudyStash.App.Tests;

/// <summary>Editing a lecture's notes on its own page: "Edit" puts the caret in them, what's typed is their text, long
/// notes are all there with the page following the caret, and the save key saves.</summary>
public class NotesEditorTests
{
    [AvaloniaTheory]
    [InlineData(SkinKind.Mac)]
    [InlineData(SkinKind.Win)]
    public async Task Edit_puts_the_caret_in_the_notes_typing_lands_there_and_the_save_key_saves(SkinKind skin)
    {
        string at = new DateTime(2026, 9, 29, 9, 40, 0).ToString("o");
        string notes = "## Summary\n\n" + string.Join("\n\n", Enumerable.Range(1, 30).Select(i => $"Paragraph {i} of the notes."));
        var lib = new FakeAiLibrary { Overview = AiTestData.MixedOverview(), OnRewrite = _ => new RewriteInfo("lec-1", "none") { Current = new NotesVersion(notes, "Claude Code", at) } };
        var model = new AiNotesModel(lib) { Delay = (_, ct) => Task.Delay(Timeout.InfiniteTimeSpan, ct) };
        await model.Load("lec-1", notes, "Claude Code", at);

        Skin.UseTheme(ColourThemes.Default);
        ((App)Application.Current!).UseSkin(skin);
        Control page = skin == SkinKind.Mac ? new MacAiNotes { DataContext = model } : new WinAiNotes { DataContext = model };
        page.Width = 620;
        var scroller = new ScrollViewer { Content = page, Height = 360 };
        var window = new Window { Width = 660, Height = 400, RequestedThemeVariant = ThemeVariant.Light, Content = scroller };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        model.EditCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        var editor = page.GetVisualDescendants().OfType<TextBox>().Single();
        Assert.True(editor.IsEffectivelyVisible);
        Assert.True(editor.IsFocused);

        window.KeyTextInput("Mine. ");
        Dispatcher.UIThread.RunJobs();
        Assert.StartsWith("Mine. Paragraph 1", model.EditText);

        // The whole of long notes is there to edit: the page scrolls (not a box inside it), and follows the caret.
        Assert.True(editor.Bounds.Height > scroller.Viewport.Height);
        editor.CaretIndex = editor.Text!.Length;
        Dispatcher.UIThread.RunJobs();
        Assert.True(scroller.Offset.Y > 0);

        window.KeyPress(Key.S, OperatingSystem.IsMacOS() ? RawInputModifiers.Meta : RawInputModifiers.Control, PhysicalKey.S, null);
        Dispatcher.UIThread.RunJobs();
        Assert.StartsWith("## Summary\n\nMine. Paragraph 1", Assert.Single(lib.Edits).Markdown);
        Assert.False(model.Editing);
    }

    /// <summary>The notes' header reads as one line: "Written by Claude · Mon 8:36" sits on the same baseline as
    /// "Summary" (smaller type beside bigger, not hung from its top), and Summary is level with Edit and Rewrite
    /// notes at the other end of the row.</summary>
    [AvaloniaFact]
    public async Task The_macs_byline_sits_on_summarys_line_level_with_the_buttons()
    {
        var skin = SkinKind.Mac;
        string at = new DateTime(2026, 9, 29, 9, 40, 0).ToString("o");
        string notes = "## Summary\n\nWhat the lecture was about.";
        var lib = new FakeAiLibrary { Overview = AiTestData.MixedOverview(), OnRewrite = _ => new RewriteInfo("lec-1", "none") { Current = new NotesVersion(notes, "Claude Code", at) } };
        var model = new AiNotesModel(lib) { Delay = (_, ct) => Task.Delay(Timeout.InfiniteTimeSpan, ct) };
        await model.Load("lec-1", notes, "Claude Code", at);

        Skin.UseTheme(ColourThemes.Default);
        ((App)Application.Current!).UseSkin(skin);
        var page = new MacAiNotes { DataContext = model, Width = 620 };
        var window = new Window { Width = 660, Height = 400, RequestedThemeVariant = ThemeVariant.Light, Content = page };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var texts = page.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible).ToList();
        var title = texts.Single(t => t.Classes.Contains("ai-notes-title"));
        var byline = texts.Single(t => t.Classes.Contains("ai-notes-byline"));
        var edit = texts.Single(t => t.Text == "Edit");
        // Where each one's letters stand, down the page.
        double Baseline(TextBlock t) => t.TranslatePoint(new Point(0, t.Padding.Top + t.TextLayout.TextLines[0].Baseline), page)!.Value.Y;
        double Middle(Visual v) => v.TranslatePoint(new Point(0, v.Bounds.Height / 2), page)!.Value.Y;

        Assert.StartsWith("Written by", byline.Text);
        Assert.True(Math.Abs(Baseline(byline) - Baseline(title)) <= 0.75, $"the byline's letters stand at {Baseline(byline):0.00}, Summary's at {Baseline(title):0.00}");
        Assert.True(Math.Abs(Middle(title) - Middle(edit)) <= 1.5, $"Summary's middle is at {Middle(title):0.00}, Edit's at {Middle(edit):0.00}");
    }
}
