using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Avalonia.Threading;
using StudyStash.App.Controls;
using StudyStash.App.ViewModels;
using StudyStash.App.Views;
using StudyStash.Core;
using StudyStash.Core.Ai;

namespace StudyStash.App.Tests;

/// <summary>A lecture's page offers notes from an AI Study Stash doesn't run (the Claude app, any free chat), and the
/// notes' ⋯ menu copies the lecture for a chat, pastes a chat's notes in, and puts earlier notes back.</summary>
public class FreeAiNotesViewTests
{
    static (LibraryModel Page, FreeAiModel Free) NoNotes(FakeAiLibrary ai)
    {
        var page = Demo.Semester(Demo.Page.Note);
        page.DrawChrome = false;
        page.Notes = null;
        page.Note = new NoteModel
        {
            Id = "n1", ClassName = "Machine Learning", Noun = Nouns.Lecture, Title = "Transformers, Self-Attention, and LLM Fundamentals",
            Pending = NoteModel.NotesAreOff(Nouns.Lecture, transcript: true),
        }.Titled();
        var free = new FreeAiModel(ai, "n1") { Offer = true };
        page.FreeAi = free;
        return (page, free);
    }

    static Window Show(LibraryModel page, SkinKind skin)
    {
        Skin.UseTheme(ColourThemes.Default);
        ((App)Avalonia.Application.Current!).UseSkin(skin);
        Control view = skin == SkinKind.Mac ? new MacLibrary { DataContext = page } : new WinLibrary { DataContext = page };
        var window = new Window { Width = 1280, Height = 860, RequestedThemeVariant = ThemeVariant.Light, Content = view };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    [AvaloniaTheory]
    [InlineData(SkinKind.Mac)]
    [InlineData(SkinKind.Win)]
    public async Task A_lecture_with_no_notes_offers_a_free_AI_and_a_chats_notes_are_pasted_in(SkinKind skin)
    {
        var ai = new FakeAiLibrary { OnNotesBrief = _ => new NotesBrief("The brief.", ["The brief.\n\nTranscript:\nSo the query for this token..."]) };
        var (page, free) = NoNotes(ai);
        string? copied = null;
        free.Copy = text =>
        {
            copied = text;
            return Task.CompletedTask;
        };
        int saved = 0;
        free.Saved += () => saved++;
        var window = Show(page, skin);

        Assert.True(window.Part<Border>("FreeAiOffer").IsEffectivelyVisible);
        Assert.Equal("No paid AI? Ask the Claude app to “write notes for this lecture” and they'll appear here.", window.Part<PageText>("OfferLine").Text);

        await free.CopyForAiCommand.ExecuteAsync(null);
        Assert.Equal("The brief.\n\nTranscript:\nSo the query for this token...", copied);
        Assert.StartsWith("Copied. Paste it into ChatGPT, Claude or Gemini", free.Say);

        free.PasteCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        Assert.True(window.Part<Border>("PastePanel").IsEffectivelyVisible);
        Assert.False(window.Part<Border>("FreeAiOffer").IsEffectivelyVisible);
        free.PasteText = "Sure! Here are your notes:\n\n## Summary\nSelf-attention.";
        await free.SavePasteCommand.ExecuteAsync(null);

        var (markdown, replace, _) = Assert.Single(ai.Saves);
        Assert.Equal("Sure! Here are your notes:\n\n## Summary\nSelf-attention.", markdown); // the library tidies it, not the app
        Assert.False(replace); // there were no notes to replace
        Assert.Equal(1, saved);
        Assert.False(free.Pasting);
        window.Close();
    }

    [AvaloniaFact]
    public async Task Right_after_a_change_Undo_shows_and_History_puts_any_version_back()
    {
        string now = DateTime.Now.ToString("o");
        RewriteInfo Info(string id, string notes, string by, string kind, bool undo, bool redo) => new(id, "none")
        {
            Current = new NotesVersion(notes, by, now), Change = new NotesChange("1", kind, by, now) { CanUndo = undo, CanRedo = redo },
        };
        var ai = new FakeAiLibrary
        {
            Overview = AiDemo.Overview(),
            OnRewrite = id => Info(id, "## Summary\nClaude's notes.", "Claude", NotesChangeKind.App, undo: true, redo: false),
            OnUndoNotes = id => Info(id, "## Summary\nThe library's own.", "Ollama", NotesChangeKind.Undo, undo: false, redo: true),
            OnNotesVersions = _ =>
            [
                new NotesKept("9", NotesChangeKind.App, "Claude", now) { Current = true },
                new NotesKept("8", NotesChangeKind.Edit, "", now),
                new NotesKept("3", NotesChangeKind.Written, "Ollama", "2026-10-20T09:30:00"),
            ],
            OnNotesVersion = (_, v) => new NotesKept(v, NotesChangeKind.Written, "Ollama", now) { Markdown = "## Summary\nThe library's own.\n- A line it had." },
            OnRestoreNotes = (id, _) => Info(id, "## Summary\nThe library's own.\n- A line it had.", "Ollama", NotesChangeKind.Restore, undo: true, redo: false),
        };
        int copies = 0;
        var notes = new AiNotesModel(ai) { OnCopyForAi = () => copies++, OnPasteNotes = () => { } };
        await notes.Load("n1", "## Summary\nClaude's notes.", "Claude", now);

        Assert.True(notes.ShowChange);
        Assert.Equal("Notes replaced by Claude", notes.ChangeLine);
        await notes.UndoCommand.ExecuteAsync(null);
        Assert.Contains("undo:n1", ai.Calls);
        Assert.Equal(("## Summary\nThe library's own.", "Undone", true), (notes.CurrentMarkdown, notes.ChangeLine, notes.CanRedo));

        Assert.Equal(["Copy for your AI", "Paste notes", "History"], notes.Menu.Actions.Select(a => a.Name));
        await notes.OpenHistory();
        Assert.True(notes.HistoryOpen);
        Assert.False(notes.ShowNotes);
        Assert.Equal(["Written by Claude", "Your edit", "Written by Ollama"], notes.Versions.Select(v => v.Words));
        await notes.Pick(notes.Versions[2]);
        Assert.Contains(notes.PickedChanges, l => l.Added && l.Text == "- A line it had.");
        await notes.RestorePickedCommand.ExecuteAsync(null);
        Assert.Contains("restore:n1:3", ai.Calls);
        Assert.False(notes.HistoryOpen);
        Assert.Equal("Earlier notes put back", notes.ChangeLine);
    }
}
