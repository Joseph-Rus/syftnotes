using System.Globalization;

namespace StudyStash.Core;

/// <summary>What made a version of a lecture's notes (<see cref="Store.NotesVersions"/>). The words each is said in
/// are the app's.</summary>
public static class NotesChangeKind
{
    /// <summary>The library wrote them (filing a lecture, writing it again after more was recorded, another library's
    /// lecture brought over); also the notes a lecture had before it kept versions, by whoever wrote them.</summary>
    public const string Written = "written";
    /// <summary>Rewritten with another AI ("Rewrite notes with", then Use new).</summary>
    public const string Rewrite = "rewrite";
    /// <summary>An AI app saved them through Study Stash's connection (save_notes).</summary>
    public const string App = "app";
    /// <summary>Pasted from a chat (Paste notes).</summary>
    public const string Paste = "paste";
    /// <summary>The student's edit (Edit, then Save).</summary>
    public const string Edit = "edit";
    /// <summary>A version chosen in History, put back.</summary>
    public const string Restore = "restore";
    /// <summary>Undo: the version before, put back.</summary>
    public const string Undo = "undo";
    /// <summary>Redo: the version an undo went back from, put back.</summary>
    public const string Redo = "redo";
    /// <summary>The designer added its diagrams.</summary>
    public const string Diagrams = "diagrams";
    /// <summary>Changed in the note file itself (in another app, or by an AI working in the folder), taken in.</summary>
    public const string File = "file";
}

/// <summary>One version of a lecture's notes: its words, who wrote them (a model or an app's name as notes store it,
/// "" for the student's own), what made it (<see cref="NotesChangeKind"/>), when, and for an undo, a redo or a restore,
/// the version it put back. <see cref="UndoTo"/> and <see cref="RedoTo"/>: the versions Undo and Redo put back from
/// this one, worked out when it was made (so thinning old versions away never changes where they go).</summary>
public sealed record NoteVersion(long Seq, string Markdown, string Model, string Kind, string At, long? Ref, long? UndoTo = null, long? RedoTo = null)
{
    /// <summary>How long its words are, in characters (a listing leaves the words themselves out).</summary>
    public int Length { get; init; }
}

/// <summary>Where Undo and Redo would take a lecture's notes: the version each would put back, or null.</summary>
public sealed record NotesUndo(NoteVersion? Undo, NoteVersion? Redo);

/// <summary>
/// Version control for a lecture's notes: a version for every change to them, whatever made it (the library writing
/// them, a rewrite, an AI app's save, a paste, an edit, a version put back, Undo and Redo, diagrams added, a change in
/// the note file), each with who made it, when and how. Undo puts the version before back, Redo the one an undo went
/// back from, and History any version: each as a version of its own, so nothing is ever only overwritten.
/// <para>Bounded, and it's text, so it's small: every version for <see cref="EveryVersionFor"/>, then the day's last
/// one a day, and never more than <see cref="MostVersions"/> a lecture; the first version is always kept. A lecture
/// keeps its versions in the trash and loses them when it's deleted for good.</para>
/// </summary>
public sealed partial class Store
{
    /// <summary>How long every version is kept; older ones are thinned to the last of each day.</summary>
    public static readonly TimeSpan EveryVersionFor = TimeSpan.FromDays(30);

    /// <summary>The most versions a lecture keeps: its first, and the newest others.</summary>
    public const int MostVersions = 200;

    /// <summary>An AI app's save this close to its own last one, untouched since, is a draft put right ("fix the diagram
    /// and save again"): it takes that version's place rather than making one of its own.</summary>
    public static readonly TimeSpan RedraftWithin = TimeSpan.FromMinutes(10);

    void CreateNotesVersions() => Exec("""
        CREATE TABLE IF NOT EXISTS notes_history (
            seq INTEGER PRIMARY KEY AUTOINCREMENT,
            id TEXT NOT NULL,
            summary_md TEXT,
            summary_model TEXT,
            kind TEXT,
            at TEXT,
            ref INTEGER,
            undo_to INTEGER,
            redo_to INTEGER
        );
        CREATE INDEX IF NOT EXISTS notes_history_id ON notes_history(id, seq);
        """);

    /// <summary>A history table from before Undo and Redo were written down with each version gets their columns.</summary>
    void MigrateNotesVersions()
    {
        var cols = new HashSet<string>();
        using (var cmd = Command("PRAGMA table_info(notes_history)"))
        using (var r = cmd.ExecuteReader())
            while (r.Read()) cols.Add(r.GetString(r.GetOrdinal("name")));
        foreach (string col in new[] { "undo_to", "redo_to" })
            if (!cols.Contains(col)) Exec($"ALTER TABLE notes_history ADD COLUMN {col} INTEGER");
    }

    const string VersionColumns = "seq, summary_model, kind, at, ref, undo_to, redo_to, length(summary_md)";

    static NoteVersion ReadVersion(Microsoft.Data.Sqlite.SqliteDataReader r, string markdown = "") =>
        new(r.GetInt64(0), markdown, r.IsDBNull(1) ? "" : r.GetString(1), r.IsDBNull(2) ? NotesChangeKind.Written : r.GetString(2),
            r.IsDBNull(3) ? "" : r.GetString(3), r.IsDBNull(4) ? null : r.GetInt64(4), r.IsDBNull(5) ? null : r.GetInt64(5), r.IsDBNull(6) ? null : r.GetInt64(6))
        {
            Length = r.IsDBNull(7) ? 0 : r.GetInt32(7),
        };

    /// <summary>A lecture's versions, newest first (the first of them is the notes as they are now): who, what kind,
    /// when, where Undo and Redo go, and how long, without their words (<see cref="NotesVersion"/> has one's).</summary>
    public List<NoteVersion> NotesVersions(string noteId)
    {
        lock (gate)
        {
            var list = new List<NoteVersion>();
            using var cmd = Command($"SELECT {VersionColumns} FROM notes_history WHERE id=? ORDER BY seq DESC", noteId);
            using var r = cmd.ExecuteReader();
            while (r.Read()) list.Add(ReadVersion(r));
            return list;
        }
    }

    /// <summary>The newest version, without its words; null with none.</summary>
    public NoteVersion? NewestVersion(string noteId)
    {
        lock (gate)
        {
            using var cmd = Command($"SELECT {VersionColumns} FROM notes_history WHERE id=? ORDER BY seq DESC LIMIT 1", noteId);
            using var r = cmd.ExecuteReader();
            return r.Read() ? ReadVersion(r) : null;
        }
    }

    /// <summary>One version, with its words; null when the lecture has no such version.</summary>
    public NoteVersion? NotesVersion(string noteId, long seq)
    {
        lock (gate)
        {
            using var cmd = Command($"SELECT {VersionColumns}, summary_md FROM notes_history WHERE id=? AND seq=?", noteId, seq);
            using var r = cmd.ExecuteReader();
            return r.Read() ? ReadVersion(r, r.IsDBNull(8) ? "" : r.GetString(8)) : null;
        }
    }

    /// <summary>Every version with its words, newest first: for tests (listings leave the words out).</summary>
    internal List<NoteVersion> NotesVersionsWithText(string noteId) =>
        [.. NotesVersions(noteId).Select(v => NotesVersion(noteId, v.Seq)!)];

    /// <summary>A lecture from before versions were kept gets its notes as they are as its first version (by whoever
    /// wrote them, when they last changed), so the first change to them has something to go back to.</summary>
    public void EnsureFirstVersion(string noteId)
    {
        lock (gate)
        {
            if (Get(noteId) is { } row) FirstVersion(noteId, row.SummaryMd ?? "", row.SummaryModel ?? "", row.UpdatedAt ?? "");
        }
    }

    /// <summary>The notes a lecture has before a change, as its first version, when it has none yet.</summary>
    void FirstVersion(string noteId, string markdown, string model, string at)
    {
        if (Py.Strip(markdown).Length == 0) return;
        using var cmd = Command("SELECT COUNT(*) FROM notes_history WHERE id=?", noteId);
        if (Convert.ToInt64(cmd.ExecuteScalar(), CultureInfo.InvariantCulture) > 0) return;
        Exec("INSERT INTO notes_history(id, summary_md, summary_model, kind, at) VALUES(?,?,?,?,?)",
            noteId, markdown, model.Length > 0 ? model : null, NotesChangeKind.Written, at.Length > 0 ? at : Now());
    }

    /// <summary>
    /// A change to a lecture's notes, as a version: after <paramref name="before"/> (the notes it had, kept as its first
    /// version when it has none), <paramref name="markdown"/> by <paramref name="model"/>, made by
    /// <paramref name="kind"/>. Notes the same as the newest version again (the lecture moved, renamed, written again
    /// the same) are no new version, but for an undo or a redo, which say where they went. <paramref name="draftOf"/>:
    /// the newest version is an AI app's draft it's putting right, and these take its place.
    /// </summary>
    void RecordVersion(string noteId, (string Markdown, string Model, string At) before, string markdown, string model, string kind,
        long? refSeq = null, bool draftOf = false)
    {
        // Any change but an AI app's save ends what that app could put right as a draft.
        if (kind != NotesChangeKind.App) Exec("DELETE FROM sync_state WHERE key=?", OutsideKey(noteId));
        FirstVersion(noteId, before.Markdown, before.Model, before.At);
        if (Py.Strip(markdown).Length == 0) return;
        string now = Now();
        long? newest = null, newestUndo = null, newestRedo = null;
        string newestMd = "";
        using (var cmd = Command("SELECT seq, summary_md, undo_to, redo_to FROM notes_history WHERE id=? ORDER BY seq DESC LIMIT 1", noteId))
        using (var r = cmd.ExecuteReader())
            if (r.Read())
            {
                newest = r.GetInt64(0);
                newestMd = r.IsDBNull(1) ? "" : r.GetString(1);
                newestUndo = r.IsDBNull(2) ? null : r.GetInt64(2);
                newestRedo = r.IsDBNull(3) ? null : r.GetInt64(3);
            }
        if (draftOf && newest is { } draft)
        {
            Exec("UPDATE notes_history SET summary_md=?, summary_model=?, at=? WHERE seq=?", markdown, model.Length > 0 ? model : null, now, draft);
            return;
        }
        if (newestMd == markdown && kind is not (NotesChangeKind.Undo or NotesChangeKind.Redo)) return;
        // Where Undo and Redo go from these notes, written down with them. A change: Undo goes back to the notes it
        // changed, and nothing can be redone over it. An undo: Undo goes on back from the version it put back, and Redo
        // returns to the notes it left. A redo: Undo returns to the notes it left, Redo goes on from the version it put back.
        (long? undoTo, long? redoTo) = kind switch
        {
            NotesChangeKind.Undo => (Pointer(refSeq, "undo_to"), newest),
            NotesChangeKind.Redo => (newest, Pointer(refSeq, "redo_to")),
            _ => (newest, (long?)null),
        };
        Exec("INSERT INTO notes_history(id, summary_md, summary_model, kind, at, ref, undo_to, redo_to) VALUES(?,?,?,?,?,?,?,?)",
            noteId, markdown, model.Length > 0 ? model : null, kind, now, refSeq, undoTo, redoTo);
        Prune(noteId, DateTimeOffset.UtcNow);
    }

    /// <summary>The bound: every version for <see cref="EveryVersionFor"/>, the last of each day before that, never more
    /// than <see cref="MostVersions"/>, and always the first.</summary>
    internal void PruneVersions(string noteId, DateTimeOffset now)
    {
        lock (gate) Prune(noteId, now);
    }

    /// <summary>A version's Undo or Redo pointer (<paramref name="column"/>), or null.</summary>
    long? Pointer(long? seq, string column)
    {
        if (seq is null) return null;
        using var cmd = Command($"SELECT {column} FROM notes_history WHERE seq=?", seq);
        return cmd.ExecuteScalar() is long v ? v : null;
    }

    void Prune(string noteId, DateTimeOffset now)
    {
        var all = new List<(long Seq, DateTimeOffset At, long? Ref, long? UndoTo, long? RedoTo)>();
        using (var cmd = Command("SELECT seq, at, ref, undo_to, redo_to FROM notes_history WHERE id=? ORDER BY seq", noteId))
        using (var r = cmd.ExecuteReader())
            while (r.Read())
                all.Add((r.GetInt64(0), DateTimeOffset.TryParse(r.IsDBNull(1) ? "" : r.GetString(1), CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal, out var at) ? at : now, r.IsDBNull(2) ? null : r.GetInt64(2),
                    r.IsDBNull(3) ? null : r.GetInt64(3), r.IsDBNull(4) ? null : r.GetInt64(4)));
        if (all.Count <= 1) return;
        var gone = all.Skip(1).Where(v => now - v.At > EveryVersionFor).GroupBy(v => v.At.UtcDateTime.Date)
            .SelectMany(day => day.OrderBy(v => v.Seq).SkipLast(1)).Select(v => v.Seq).ToHashSet();
        var left = all.Where(v => !gone.Contains(v.Seq)).ToList();
        foreach (var v in left.Skip(1).Take(Math.Max(0, left.Count - MostVersions))) gone.Add(v.Seq);
        if (gone.Count == 0) return;
        // Nothing left pointing at a version thinned away: Undo goes on back past it to the nearest one kept (the first is
        // always kept); Redo to it, or "put back" naming it, has nowhere to go and is dropped.
        var bySeq = all.ToDictionary(v => v.Seq);
        long? Back(long? seq)
        {
            while (seq is { } s && gone.Contains(s)) seq = bySeq.TryGetValue(s, out var v) ? v.UndoTo : null;
            return seq;
        }
        foreach (var v in all.Where(v => !gone.Contains(v.Seq)))
        {
            long? undoTo = Back(v.UndoTo);
            long? redoTo = v.RedoTo is { } rt && gone.Contains(rt) ? null : v.RedoTo;
            long? refSeq = v.Ref is { } rf && gone.Contains(rf) ? null : v.Ref;
            if (undoTo != v.UndoTo || redoTo != v.RedoTo || refSeq != v.Ref)
                Exec("UPDATE notes_history SET undo_to=?, redo_to=?, ref=? WHERE seq=?", undoTo, redoTo, refSeq, v.Seq);
        }
        foreach (long seq in gone) Exec("DELETE FROM notes_history WHERE seq=?", seq);
    }

    /// <summary>
    /// Where Undo and Redo would take the notes, as an editor's would: written down with the newest version when it was
    /// made (a change: back to the notes it changed; an undo: further back, or forward to the notes it left; a redo: the
    /// other way), so it never depends on versions that have been thinned away since.
    /// </summary>
    public NotesUndo UndoState(string noteId)
    {
        lock (gate)
        {
            var newest = NewestVersion(noteId);
            return new NotesUndo(newest?.UndoTo is { } u ? NotesVersion(noteId, u) : null, newest?.RedoTo is { } r ? NotesVersion(noteId, r) : null);
        }
    }

    /// <summary>Undo: the version before, put back, as a version of its own. <see cref="NotesEdited.Gone"/> with
    /// nothing to undo. <paramref name="expect"/>: the version the undo was asked from (the newest, as the asker last
    /// saw it); when another device or app has changed the notes since, nothing is undone (<see cref="NotesEdited.Changed"/>),
    /// so two at once never go back two steps.</summary>
    public NotesEdited UndoNotes(string noteId, long? expect = null)
    {
        lock (gate)
        {
            if (expect is { } seen && NewestVersion(noteId)?.Seq != seen) return NotesEdited.Changed;
            return UndoState(noteId).Undo is { } back ? PutBack(noteId, back, NotesChangeKind.Undo) : NotesEdited.Gone;
        }
    }

    /// <summary>Redo: the version the last undo went back from, put back. <see cref="NotesEdited.Gone"/> with nothing to
    /// redo; <see cref="NotesEdited.Changed"/> as for <see cref="UndoNotes"/>.</summary>
    public NotesEdited RedoNotes(string noteId, long? expect = null)
    {
        lock (gate)
        {
            if (expect is { } seen && NewestVersion(noteId)?.Seq != seen) return NotesEdited.Changed;
            return UndoState(noteId).Redo is { } forward ? PutBack(noteId, forward, NotesChangeKind.Redo) : NotesEdited.Gone;
        }
    }

    /// <summary>A version chosen in History (<paramref name="seq"/>, or the one before the notes now), put back as a new
    /// version: undone like any other change. <see cref="NotesEdited.Gone"/> when there's no such version.</summary>
    public NotesEdited RestoreNotesVersion(string noteId, long? seq = null)
    {
        lock (gate)
        {
            long? wanted = seq ?? NotesVersions(noteId).Skip(1).FirstOrDefault()?.Seq;
            return wanted is { } at && NotesVersion(noteId, at) is { } version ? PutBack(noteId, version, NotesChangeKind.Restore) : NotesEdited.Gone;
        }
    }

    NotesEdited PutBack(string noteId, NoteVersion version, string kind) =>
        EditNotes(noteId, version.Markdown, writer: version.Model, kind: kind, refSeq: version.Seq);

    /// <summary>The newest version is an AI app's save by <paramref name="writer"/>, with the words it saved (their
    /// <see cref="Notes.Fingerprint"/>, <paramref name="print"/>): nothing has been made on top of it.</summary>
    bool NewestIsOwnSave(string noteId, string writer, string print)
    {
        using var cmd = Command("SELECT kind, summary_model, summary_md FROM notes_history WHERE id=? ORDER BY seq DESC LIMIT 1", noteId);
        using var r = cmd.ExecuteReader();
        return r.Read() && !r.IsDBNull(0) && r.GetString(0) == NotesChangeKind.App && !r.IsDBNull(1) && r.GetString(1) == writer
            && Notes.Fingerprint(r.IsDBNull(2) ? "" : r.GetString(2)) == print;
    }

    /// <summary>Who last saved a lecture's notes through an AI app, when, and what they saved: how a draft put right is
    /// told from notes that are the lecture's own by now.</summary>
    static string OutsideKey(string noteId) => "notes-from-outside:" + noteId;

    /// <summary>A lecture deleted for good takes its versions with it.</summary>
    void ForgetNotesVersions(string noteId)
    {
        Exec("DELETE FROM notes_history WHERE id=?", noteId);
        Exec("DELETE FROM sync_state WHERE key=?", OutsideKey(noteId));
    }
}
