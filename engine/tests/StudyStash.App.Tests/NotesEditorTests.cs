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
        Assert.StartsWith("## Summary\n\n==Mine. Paragraph 1", Assert.Single(lib.Edits).Markdown);
        Assert.False(model.Editing);
    }

    /// <summary>The line over the notes says who wrote them (when, on hover), level with Edit at the other end of the
    /// row; with words of the student's own in them it says whose are whose.</summary>
    [AvaloniaFact]
    public async Task The_line_over_the_notes_says_who_wrote_them_level_with_edit()
    {
        var skin = SkinKind.Mac;
        string at = new DateTime(2026, 9, 29, 9, 40, 0).ToString("o");
        string notes = "## Summary\n\nWhat the lecture was about.";
        var lib = new FakeAiLibrary { Overview = AiTestData.MixedOverview(), OnRewrite = _ => new RewriteInfo("lec-1", "none") { Current = new NotesVersion(notes, "Claude", at) } };
        var model = new AiNotesModel(lib) { Delay = (_, ct) => Task.Delay(Timeout.InfiniteTimeSpan, ct) };
        await model.Load("lec-1", notes, "Claude", at);

        Skin.UseTheme(ColourThemes.Default);
        ((App)Application.Current!).UseSkin(skin);
        var page = new MacAiNotes { DataContext = model, Width = 620 };
        var window = new Window { Width = 660, Height = 400, RequestedThemeVariant = ThemeVariant.Light, Content = page };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        List<TextBlock> Texts() => [.. page.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible)];
        var writer = Texts().Single(t => t.Name == "LegendAi");
        var edit = Texts().Single(t => t.Text == "Edit");
        double Middle(Visual v) => v.TranslatePoint(new Point(0, v.Bounds.Height / 2), page)!.Value.Y;
        Assert.Equal("Written by Claude from the recording", writer.Text);
        Assert.DoesNotContain(Texts(), t => t.Text == "What you wrote");
        Assert.StartsWith("Written by Claude", (string)ToolTip.GetTip(page.GetVisualDescendants().OfType<WrapPanel>().Single(p => p.Name == "Legend"))!);
        Assert.True(Math.Abs(Middle(writer) - Middle(edit)) <= 1.5, $"the line's middle is at {Middle(writer):0.00}, Edit's at {Middle(edit):0.00}");
        window.Close();

        // Notes with the student's own words in them: both halves of the line.
        const string mixed = "## ==History==\n\n- ==2017== The transformer paper.";
        lib = new FakeAiLibrary { Overview = AiTestData.MixedOverview(), OnRewrite = _ => new RewriteInfo("lec-1", "none") { Current = new NotesVersion(mixed, "Claude", at) } };
        model = new AiNotesModel(lib) { Delay = (_, ct) => Task.Delay(Timeout.InfiniteTimeSpan, ct) };
        await model.Load("lec-1", mixed, "Claude", at);
        window = new Window { Width = 660, Height = 400, RequestedThemeVariant = ThemeVariant.Light, Content = page = new MacAiNotes { DataContext = model, Width = 620 } };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        Assert.Contains(Texts(), t => t.Text == "What you wrote");
        Assert.Contains(Texts(), t => t.Text == "Added by Claude from the recording");
        window.Close();
    }
}
