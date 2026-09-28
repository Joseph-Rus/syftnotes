namespace StudyStash.Core.Tests;

/// <summary>Attachments in the index: kept, listed, read, removed, and following their lecture and class wherever they go.</summary>
public class StoreAttachmentsTests
{
    static Store Library(TempDir dir)
    {
        var store = new Store(dir["state.db"], dir["pool"]);
        store.Save(new Meeting("n1") { Title = "Cells", Date = "2026-09-01", Transcript = "membranes" }, new Classification("Bio 110", 1, "folder"));
        store.Save(new Meeting("n2") { Title = "Loops", Date = "2026-09-02", Transcript = "for loops" }, new Classification("CS 101", 1, "folder"));
        return store;
    }

    /// <summary>A file put in a class's Attachments folder and kept in the index, as an upload does.</summary>
    static Attachment Attach(Store store, string id, string className, string? noteId, string name = "notes.pdf", string added = "2026-09-01T10:00:00+00:00")
    {
        string dir = store.AttachmentsDir(className);
        string file = Attachments.FreeName(dir, name);
        File.WriteAllText(Path.Combine(dir, file), "%PDF-1.7 " + id);
        var a = new Attachment(id, name, file, className, noteId, 12, "application/pdf", added);
        store.AddAttachment(a);
        return a;
    }

    [Fact]
    public void An_attachment_is_kept_and_listed_by_lecture_and_by_class()
    {
        using var dir = new TempDir();
        using var store = Library(dir);
        Attach(store, "a1", "Bio 110", "n1");
        Attach(store, "a2", "Bio 110", null, "syllabus.pdf", "2026-09-02T10:00:00+00:00");
        Attach(store, "a3", "CS 101", "n2");

        Assert.Equal(["a1"], store.ListAttachments(noteId: "n1").Select(a => a.Id));
        Assert.Equal(["a1", "a2"], store.ListAttachments("Bio 110").Select(a => a.Id));
        Assert.Equal(3, store.ListAttachments().Count);
        var a1 = store.GetAttachment("a1")!;
        Assert.Equal(Attachment.Reading, a1.State);
        Assert.False(a1.HasText);
        Assert.Equal(Path.Combine(dir["pool"], "Bio 110", "Attachments", "notes.pdf"), store.AttachmentPath(a1));
        Assert.True(File.Exists(store.AttachmentPath(a1)));
        Assert.Null(store.GetAttachment("nope"));
    }

    [Fact]
    public void Its_words_are_kept_once_read_and_none_is_said_plainly()
    {
        using var dir = new TempDir();
        using var store = Library(dir);
        Attach(store, "a1", "Bio 110", "n1");
        Attach(store, "a2", "Bio 110", "n1", "blank.png");
        Assert.Equal(2, store.UnreadAttachments().Count);
        Assert.True(store.SetAttachmentText("a1", "Osmosis moves water."));
        Assert.True(store.SetAttachmentText("a2", "  "));
        Assert.False(store.SetAttachmentText("gone", "x"));
        Assert.Empty(store.UnreadAttachments());
        Assert.Equal(Attachment.Read, store.GetAttachment("a1")!.State);
        Assert.Equal(Attachment.Unread, store.GetAttachment("a2")!.State);
        var attached = Assert.Single(store.AttachedTo("n1"));
        Assert.Equal(new AttachedText("a1", Attachments.OwnNotes, "notes.pdf", "Osmosis moves water."), attached);
    }

    [Fact]
    public void Notes_written_without_an_attachment_offer_a_rewrite_until_they_use_it()
    {
        using var dir = new TempDir();
        using var store = Library(dir);
        Assert.False(store.AttachmentsUnused("n1"));
        Attach(store, "a1", "Bio 110", "n1");
        Assert.False(store.AttachmentsUnused("n1")); // nothing read from it yet
        store.SetAttachmentText("a1", "words");
        Assert.True(store.AttachmentsUnused("n1"));
        store.MarkAttachmentsUsed(["a1"]);
        Assert.False(store.AttachmentsUnused("n1"));
        Assert.True(store.GetAttachment("a1")!.Used);
    }

    [Fact]
    public void Removing_one_removes_its_file_too()
    {
        using var dir = new TempDir();
        using var store = Library(dir);
        var a = Attach(store, "a1", "Bio 110", "n1");
        string path = store.AttachmentPath(a);
        Assert.True(store.RemoveAttachment("a1"));
        Assert.False(File.Exists(path));
        Assert.Null(store.GetAttachment("a1"));
        Assert.False(store.RemoveAttachment("a1"));
    }

    [Fact]
    public void A_lecture_moved_to_another_class_takes_its_attachments_along()
    {
        using var dir = new TempDir();
        using var store = Library(dir);
        Attach(store, "a1", "Bio 110", "n1");
        Attach(store, "a2", "Bio 110", null, "syllabus.pdf");
        // Something of the same name is already there: the one moving takes another name.
        File.WriteAllText(Path.Combine(store.AttachmentsDir("CS 101"), "notes.pdf"), "someone else's");

        store.SetClass("n1", "CS 101");

        var moved = store.GetAttachment("a1")!;
        Assert.Equal("CS 101", moved.ClassName);
        Assert.Equal("notes (2).pdf", moved.File);
        Assert.Equal("%PDF-1.7 a1", File.ReadAllText(store.AttachmentPath(moved)));
        Assert.False(File.Exists(Path.Combine(dir["pool"], "Bio 110", "Attachments", "notes.pdf")));
        Assert.Equal("someone else's", File.ReadAllText(Path.Combine(dir["pool"], "CS 101", "Attachments", "notes.pdf")));
        Assert.Equal("Bio 110", store.GetAttachment("a2")!.ClassName); // the class's own stays
    }

    [Fact]
    public void An_unsorted_lecture_is_filed_and_its_attachment_goes_with_it()
    {
        using var dir = new TempDir();
        using var store = new Store(dir["state.db"], dir["pool"]);
        store.Enqueue(new Meeting("q1") { Title = "Waiting", Date = "2026-09-04", Transcript = "t" });
        Attach(store, "a1", Configs.Unsorted, "q1", "IMG_1.heic");
        var row = store.ClaimNext()!;
        store.Finish(row, store.Meeting(row), new Classification("Chem 200", 0.9, "ollama"));
        var a = store.GetAttachment("a1")!;
        Assert.Equal("Chem 200", a.ClassName);
        Assert.True(File.Exists(Path.Combine(dir["pool"], "Chem 200", "Attachments", "IMG_1.heic")));
    }

    [Fact]
    public void A_renamed_class_keeps_its_attachments()
    {
        using var dir = new TempDir();
        using var store = Library(dir);
        Attach(store, "a1", "Bio 110", "n1");
        Attach(store, "a2", "Bio 110", null, "syllabus.pdf");
        store.RenameClass("Bio 110", "Cell Biology");
        Assert.All(store.ListAttachments("Cell Biology"), a => Assert.True(File.Exists(store.AttachmentPath(a))));
        Assert.Equal(2, store.ListAttachments("Cell Biology").Count);
        Assert.Empty(store.ListAttachments("Bio 110"));
    }

    [Fact]
    public void A_deleted_lecture_keeps_its_attachments_through_undo_and_leaves_them_to_the_class_when_gone()
    {
        using var dir = new TempDir();
        using var store = Library(dir);
        Attach(store, "a1", "Bio 110", "n1");
        store.Trash("n1");
        Assert.Equal("n1", store.GetAttachment("a1")!.NoteId);
        store.Restore("n1");
        Assert.Single(store.ListAttachments(noteId: "n1"));
        store.Trash("n1");
        store.EmptyTrash();
        var a = store.GetAttachment("a1")!;
        Assert.Null(a.NoteId);
        Assert.Equal("Bio 110", a.ClassName);
        Assert.True(File.Exists(store.AttachmentPath(a)));
    }
}
