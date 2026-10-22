using StudyStash.Core.Ai;
using StudyStash.Core.Rich;

namespace StudyStash.Core.Tests;

/// <summary>Version control for a lecture's notes: a version for every change whatever made it, Undo and Redo as an
/// editor's, a version put back as a new one, a bound that never loses the first, and versions gone with the lecture.</summary>
public class NotesVersionTests
{
    static Store Open(TempDir dir) => new(dir["state.db"], dir["pool"]);

    static void File(Store store, string id = "lec-1", string notes = "## Summary\nThe library's own.", string model = "qwen3:8b", string kind = NotesChangeKind.Written) =>
        store.Save(new Meeting(id) { Title = "Cells", Date = "2026-10-01T09:00:00", Transcript = "Today: cells." },
            new Classification("Bio 110", 0.9, "ollama", "Cells", []), notes, model, versionKind: kind);

    static string[] Kinds(Store store, string id = "lec-1") => [.. store.NotesVersionsWithText(id).Select(v => v.Kind).Reverse()];

    [Fact]
    public void Every_kind_of_change_to_the_notes_is_a_version()
    {
        using var dir = new TempDir();
        using var store = Open(dir);
        File(store);                                                                       // the library writes them
        File(store, kind: NotesChangeKind.Written);                                        // filed again the same: no new version
        File(store, notes: "## Summary\nRewritten.", model: "Claude sonnet", kind: NotesChangeKind.Rewrite);
        store.EditNotes("lec-1", "## Summary\nFrom the Claude app.", writer: "Claude (in the Claude app)", redraft: true, kind: NotesChangeKind.App);
        store.EditNotes("lec-1", "## Summary\nPasted.", writer: Engines.PastedWriter, kind: NotesChangeKind.Paste);
        store.EditNotes("lec-1", "## Summary\nPasted, typo fixed.");                      // an edit by hand
        store.RestoreNotesVersion("lec-1", store.NotesVersionsWithText("lec-1")[^1].Seq);          // History: the first, put back
        store.UndoNotes("lec-1");
        store.RedoNotes("lec-1");
        string path = store.Get("lec-1")!.MdPath!;
        System.IO.File.WriteAllText(path, System.IO.File.ReadAllText(path).Replace("The library's own.", "Changed in Obsidian."));
        store.EditNotes("lec-1", "## Summary\nAfter the file.\n\n## Key points\n- One.");  // the file's change is taken in first
        store.AddDiagrams("lec-1", Notes.Fingerprint(store.Get("lec-1")!.SummaryMd!),
            [new DesignedDiagram("Chain", "Key points", 25, NoteBlockKind.Mermaid, "flowchart LR\n  A --> B", "A leads to B.")]);

        Assert.Equal([NotesChangeKind.Written, NotesChangeKind.Rewrite, NotesChangeKind.App, NotesChangeKind.Paste, NotesChangeKind.Edit,
            NotesChangeKind.Restore, NotesChangeKind.Undo, NotesChangeKind.Redo, NotesChangeKind.File, NotesChangeKind.Edit, NotesChangeKind.Diagrams], Kinds(store));
        var versions = store.NotesVersionsWithText("lec-1");
        Assert.Equal("Claude sonnet", versions.Single(v => v.Kind == NotesChangeKind.Rewrite).Model); // who made each
        Assert.Equal(store.Get("lec-1")!.SummaryMd, versions[0].Markdown);                    // the newest is the notes now
        Assert.All(versions, v => Assert.True(DateTimeOffset.TryParse(v.At, out _)));          // and when
        Assert.All(store.NotesVersions("lec-1"), v => Assert.True(v.Markdown == "" && v.Length > 0)); // listed without their words
    }

    [Fact]
    public void Undo_and_redo_go_back_and_forward_as_an_editors_do_and_a_new_change_ends_redo()
    {
        using var dir = new TempDir();
        using var store = Open(dir);
        File(store, notes: "A");
        store.EditNotes("lec-1", "B");
        store.EditNotes("lec-1", "C");
        string Notes() => store.Get("lec-1")!.SummaryMd!;

        Assert.Equal(NotesEdited.Saved, store.UndoNotes("lec-1"));
        Assert.Equal("B", Notes());
        store.UndoNotes("lec-1");
        Assert.Equal("A", Notes());
        Assert.Equal(NotesEdited.Gone, store.UndoNotes("lec-1"));  // nothing before the first
        store.RedoNotes("lec-1");
        Assert.Equal("B", Notes());
        store.RedoNotes("lec-1");
        Assert.Equal("C", Notes());
        Assert.Equal(NotesEdited.Gone, store.RedoNotes("lec-1"));

        store.UndoNotes("lec-1");                                     // back to B, then a new change
        store.EditNotes("lec-1", "D");
        Assert.Null(store.UndoState("lec-1").Redo);                   // C can't be redone over it
        store.UndoNotes("lec-1");
        Assert.Equal("B", Notes());                                   // Undo goes back to what D changed
        Assert.Equal("C", store.NotesVersionsWithText("lec-1").Single(v => v.Kind == NotesChangeKind.Edit && v.Markdown == "C").Markdown); // still in History
    }

    [Fact]
    public void Putting_a_version_back_is_a_new_version_and_an_AI_apps_draft_put_right_is_one()
    {
        using var dir = new TempDir();
        using var store = Open(dir);
        File(store, notes: "Own");
        const string Claude = "Claude (in the Claude app)";
        store.EditNotes("lec-1", "Draft one", writer: Claude, redraft: true, kind: NotesChangeKind.App);
        store.EditNotes("lec-1", "Draft two", writer: Claude, redraft: true, kind: NotesChangeKind.App); // a diagram put right
        Assert.Equal(["Draft two", "Own"], store.NotesVersionsWithText("lec-1").Select(v => v.Markdown));

        long own = store.NotesVersionsWithText("lec-1")[^1].Seq;
        store.RestoreNotesVersion("lec-1", own);
        var versions = store.NotesVersionsWithText("lec-1");
        Assert.Equal(3, versions.Count);                               // nothing was removed: a new version on top
        Assert.Equal(("Own", NotesChangeKind.Restore, own), (versions[0].Markdown, versions[0].Kind, versions[0].Ref));
        Assert.Equal("qwen3:8b", store.Get("lec-1")!.SummaryModel);   // and its writer with it
        store.UndoNotes("lec-1");
        Assert.Equal("Draft two", store.Get("lec-1")!.SummaryMd);      // undone like any change

        // Undone and redone, or edited and undone, its save isn't a draft any more: the next is a version of its own,
        // and the undo and the redo stay what they were.
        store.EditNotes("lec-1", "Claude A", writer: Claude, redraft: true, kind: NotesChangeKind.App);
        store.UndoNotes("lec-1");
        store.RedoNotes("lec-1");
        store.EditNotes("lec-1", "Claude B", writer: Claude, redraft: true, kind: NotesChangeKind.App);
        Assert.Equal([("Claude B", NotesChangeKind.App), ("Claude A", NotesChangeKind.Redo), ("Draft two", NotesChangeKind.Undo), ("Claude A", NotesChangeKind.App)],
            store.NotesVersionsWithText("lec-1").Take(4).Select(v => (v.Markdown, v.Kind)));
        store.UndoNotes("lec-1");
        Assert.Equal("Claude A", store.Get("lec-1")!.SummaryMd);
        store.EditNotes("lec-1", "My edit");
        store.UndoNotes("lec-1");
        store.EditNotes("lec-1", "Claude C", writer: Claude, redraft: true, kind: NotesChangeKind.App);
        Assert.Equal([("Claude C", NotesChangeKind.App), ("Claude A", NotesChangeKind.Undo), ("My edit", NotesChangeKind.Edit)],
            store.NotesVersionsWithText("lec-1").Take(3).Select(v => (v.Markdown, v.Kind)));

        // A paste isn't a draft: each is its own version.
        store.EditNotes("lec-1", "Pasted one", writer: Engines.PastedWriter, kind: NotesChangeKind.Paste);
        store.EditNotes("lec-1", "Pasted two", writer: Engines.PastedWriter, kind: NotesChangeKind.Paste);
        Assert.Equal(["Pasted two", "Pasted one"], store.NotesVersionsWithText("lec-1").Take(2).Select(v => v.Markdown));
    }

    [Fact]
    public void The_bound_thins_old_versions_and_caps_them_but_always_keeps_the_first()
    {
        using var dir = new TempDir();
        using var store = Open(dir);
        File(store, notes: "First");
        for (int i = 1; i <= Store.MostVersions + 5; i++) store.EditNotes("lec-1", $"Edit {i}");
        var versions = store.NotesVersionsWithText("lec-1");
        Assert.Equal(Store.MostVersions, versions.Count);
        Assert.Equal("First", versions[^1].Markdown);
        Assert.Equal($"Edit {Store.MostVersions + 5}", versions[0].Markdown);

        // Looked at two months on: one a day before the last 30 days (all of these are one day's), and the first.
        store.PruneVersions("lec-1", DateTimeOffset.UtcNow.AddDays(60));
        Assert.Equal(["Edit 205", "First"], store.NotesVersionsWithText("lec-1").Select(v => v.Markdown));
    }

    [Fact]
    public void Thinning_away_the_versions_an_undo_pointed_at_never_sends_Undo_or_Redo_somewhere_else()
    {
        using var dir = new TempDir();
        using var store = Open(dir);
        File(store, notes: "## Summary\nOwn.\n\n## Key points\n- One.");                                   // 1
        store.AddDiagrams("lec-1", Notes.Fingerprint(store.Get("lec-1")!.SummaryMd!),
            [new DesignedDiagram("Chain", "Key points", 25, NoteBlockKind.Mermaid, "flowchart LR\n  A --> B", "A leads to B.")]); // 2
        string withDiagrams = store.Get("lec-1")!.SummaryMd!;
        store.EditNotes("lec-1", "## Summary\nClaude's.", writer: "Claude (in the Claude app)", redraft: true, kind: NotesChangeKind.App); // 3
        store.UndoNotes("lec-1");                                                                                // 4: back to 2
        Assert.Equal(withDiagrams, store.Get("lec-1")!.SummaryMd);

        // A month on, only the day's last version and the first are kept: 2 and 3 go.
        store.PruneVersions("lec-1", DateTimeOffset.UtcNow.AddDays(60));
        Assert.Equal([NotesChangeKind.Written, NotesChangeKind.Undo], Kinds(store));
        var state = store.UndoState("lec-1");
        Assert.Null(state.Redo);                                       // Claude's notes are gone: nothing to redo, not the first notes
        Assert.Equal("## Summary\nOwn.\n\n## Key points\n- One.", state.Undo!.Markdown); // and Undo still goes back past the diagrams
        Assert.All(store.NotesVersionsWithText("lec-1"), v => Assert.True(v.Ref is null || store.NotesVersionsWithText("lec-1").Any(o => o.Seq == v.Ref)));

        // The next change has its Undo: back to the notes with diagrams.
        store.EditNotes("lec-1", withDiagrams + "\n- Two.");
        Assert.Equal(withDiagrams, store.UndoState("lec-1").Undo!.Markdown);
        Assert.Equal(NotesEdited.Saved, store.UndoNotes("lec-1"));
        Assert.Equal(withDiagrams, store.Get("lec-1")!.SummaryMd);
    }

    [Fact]
    public void A_lectures_versions_wait_in_the_trash_with_it_and_go_when_its_deleted_for_good()
    {
        using var dir = new TempDir();
        using var store = Open(dir);
        File(store);
        File(store, id: "lec-2");
        store.EditNotes("lec-1", "## Summary\nNew.");
        store.EditNotes("lec-2", "## Summary\nNew.");

        store.Trash("lec-1");
        Assert.Equal(2, store.NotesVersionsWithText("lec-1").Count);
        store.Restore("lec-1");
        Assert.Equal(2, store.NotesVersionsWithText("lec-1").Count);
        store.Trash("lec-1");
        store.EmptyTrash();
        Assert.Empty(store.NotesVersionsWithText("lec-1"));

        store.Delete("lec-2");
        Assert.Empty(store.NotesVersionsWithText("lec-2"));
    }
}
