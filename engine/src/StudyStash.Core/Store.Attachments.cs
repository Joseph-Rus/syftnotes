namespace StudyStash.Core;

/// <summary>
/// Attachments in the index: which class and lecture each belongs to, when it came, what it is, and the words read
/// from it. The file itself lives in its class's folder, in Attachments/, so it goes wherever the class's folder goes.
/// </summary>
public sealed partial class Store
{
    /// <summary>The folder in a class's folder that holds its attachments.</summary>
    public const string AttachmentsFolder = "Attachments";

    void CreateAttachments() => Exec("""
        CREATE TABLE IF NOT EXISTS attachments (
            id TEXT PRIMARY KEY,
            name TEXT,
            file TEXT,
            class_name TEXT,
            note_id TEXT,
            size INTEGER,
            type TEXT,
            added TEXT,
            text TEXT,
            state TEXT,
            used INTEGER DEFAULT 0
        );
        CREATE INDEX IF NOT EXISTS attachments_note ON attachments(note_id);
        CREATE INDEX IF NOT EXISTS attachments_class ON attachments(class_name);
        """);

    /// <summary>A class's Attachments folder, made if it isn't there yet.</summary>
    public string AttachmentsDir(string? className)
    {
        lock (gate)
        {
            string d = Path.Combine(ClassDir(className), AttachmentsFolder);
            Directory.CreateDirectory(d);
            return d;
        }
    }

    /// <summary>Where an attachment's file is.</summary>
    public string AttachmentPath(Attachment a) => Path.Combine(ClassFolder(a.ClassName), AttachmentsFolder, a.File);

    List<Attachment> AttachmentRows(string sql, params object?[] args)
    {
        using var cmd = Command(sql, args);
        using var r = cmd.ExecuteReader();
        var rows = new List<Attachment>();
        string S(int i) => r.IsDBNull(i) ? "" : r.GetString(i);
        while (r.Read())
            rows.Add(new Attachment(S(0), S(1), S(2), S(3), r.IsDBNull(4) ? null : r.GetString(4), r.IsDBNull(5) ? 0 : r.GetInt64(5),
                S(6), S(7), S(8), S(9) is { Length: > 0 } state ? state : Attachment.Unread, !r.IsDBNull(10) && r.GetInt64(10) != 0));
        return rows;
    }

    const string AttachmentColumns = "id, name, file, class_name, note_id, size, type, added, text, state, used";

    /// <summary>Keep a new attachment (its file already in place).</summary>
    public void AddAttachment(Attachment a)
    {
        lock (gate)
            Exec($"INSERT INTO attachments({AttachmentColumns}) VALUES(?,?,?,?,?,?,?,?,?,?,?)",
                a.Id, a.Name, a.File, a.ClassName, a.NoteId, a.Size, a.Type, a.Added, a.Text, a.State, a.Used ? 1L : 0L);
    }

    public Attachment? GetAttachment(string id)
    {
        lock (gate) return AttachmentRows($"SELECT {AttachmentColumns} FROM attachments WHERE id=?", id).FirstOrDefault();
    }

    /// <summary>A lecture's attachments (when <paramref name="noteId"/> is given), else a class's (its own and its
    /// lectures'), else every one; oldest first.</summary>
    public List<Attachment> ListAttachments(string? className = null, string? noteId = null)
    {
        lock (gate)
        {
            if (noteId is not null) return AttachmentRows($"SELECT {AttachmentColumns} FROM attachments WHERE note_id=? ORDER BY added, rowid", noteId);
            if (className is not null) return AttachmentRows($"SELECT {AttachmentColumns} FROM attachments WHERE class_name=? ORDER BY added, rowid", className);
            return AttachmentRows($"SELECT {AttachmentColumns} FROM attachments ORDER BY added, rowid");
        }
    }

    /// <summary>The attachments whose words are still to be read (a restart stopped them partway).</summary>
    public List<Attachment> UnreadAttachments()
    {
        lock (gate) return AttachmentRows($"SELECT {AttachmentColumns} FROM attachments WHERE state=? ORDER BY added", Attachment.Reading);
    }

    /// <summary>The words read from an attachment (empty: it had none). False when it's been removed meanwhile.</summary>
    public bool SetAttachmentText(string id, string? text)
    {
        lock (gate)
        {
            bool none = string.IsNullOrWhiteSpace(text);
            return Exec("UPDATE attachments SET text=?, state=? WHERE id=?", none ? "" : text, none ? Attachment.Unread : Attachment.Read, id) > 0;
        }
    }

    /// <summary>These attachments' words went into their lecture's notes.</summary>
    public void MarkAttachmentsUsed(IEnumerable<string> ids)
    {
        lock (gate)
            foreach (string id in ids) Exec("UPDATE attachments SET used=1 WHERE id=?", id);
    }

    /// <summary>What a lecture's attachments add to its notes: each one's kind, name and words (those with words only).</summary>
    public List<AttachedText> AttachedTo(string noteId) =>
        [.. ListAttachments(noteId: noteId).Where(a => a.HasText).Select(a => new AttachedText(a.Id, Attachments.KindOf(a.Name, a.Type), a.Name, a.Text))];

    /// <summary>The lecture has notes written without some of its attachments' words: rewriting would use them.</summary>
    public bool AttachmentsUnused(string noteId) => ListAttachments(noteId: noteId).Any(a => a.HasText && !a.Used);

    /// <summary>Removes an attachment: its file and its row. False when there's no such attachment.</summary>
    public bool RemoveAttachment(string id)
    {
        lock (gate)
        {
            var a = GetAttachment(id);
            if (a is null) return false;
            string path = AttachmentPath(a);
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch (IOException)
            {
            }
            Exec("DELETE FROM attachments WHERE id=?", id);
            return true;
        }
    }

    /// <summary>A lecture filed under another class takes its attachments along: their files move to that class's
    /// Attachments folder (renamed if the name's taken there).</summary>
    void AttachmentsFollow(string noteId, string className)
    {
        foreach (var a in AttachmentRows($"SELECT {AttachmentColumns} FROM attachments WHERE note_id=? AND class_name IS NOT ?", noteId, className))
        {
            string from = AttachmentPath(a), dir = AttachmentsDir(className), file = a.File;
            try
            {
                if (File.Exists(from))
                {
                    file = Attachments.FreeName(dir, a.File);
                    File.Move(from, Path.Combine(dir, file));
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                continue; // the file stays where it was, and so does the row that finds it
            }
            Exec("UPDATE attachments SET class_name=?, file=? WHERE id=?", className, file, a.Id);
        }
    }

    /// <summary>A class renamed: its folder (and so its Attachments) moved whole; the rows follow.</summary>
    void RenameAttachments(string from, string to) => Exec("UPDATE attachments SET class_name=? WHERE class_name=?", to, from);

    /// <summary>A lecture deleted for good: its attachments stay, now the class's own.</summary>
    void DetachAttachments(string noteId) => Exec("UPDATE attachments SET note_id=NULL WHERE note_id=?", noteId);
}
