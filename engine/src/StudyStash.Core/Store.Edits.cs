namespace StudyStash.Core;

/// <summary>What became of a student's own edit of a lecture's notes.</summary>
public enum NotesEdited
{
    /// <summary>They're the lecture's notes now.</summary>
    Saved,
    /// <summary>There's no such lecture, or it was never filed.</summary>
    Gone,
    /// <summary>The library is writing this lecture's notes right now (those would replace the edit): nothing changed.</summary>
    Busy,
    /// <summary>The notes aren't the ones the edit started from any more (their diagrams arrived, or they were edited or
    /// rewritten somewhere else): nothing changed.</summary>
    Changed,
    /// <summary>The note file couldn't be written (another app has it locked): nothing changed.</summary>
    Unwritten,
}

public sealed partial class Store
{
    /// <summary>
    /// A lecture's notes as the student edited them, saved as its notes in one step under the store's lock: the note
    /// file is written afresh (as using a rewrite's new notes writes it), and search and Ask read them at once. Who
    /// wrote them, the lecture's class and everything else about it stay as they were. <paramref name="basedOn"/> is
    /// the <see cref="Notes.Fingerprint"/> of the notes the edit started from: with other notes there now, nothing is
    /// saved (<see cref="NotesEdited.Changed"/>), so an edit never quietly undoes what arrived meanwhile; null saves
    /// over whatever is there.
    /// </summary>
    public NotesEdited EditNotes(string noteId, string markdown, string? basedOn = null)
    {
        lock (gate)
        {
            var row = Get(noteId);
            if (row is null || string.IsNullOrEmpty(row.MdPath)) return NotesEdited.Gone;
            if (row.Status is Queued or Working) return NotesEdited.Busy;
            if (basedOn is not null && Notes.Fingerprint(row.SummaryMd ?? "") != basedOn) return NotesEdited.Changed;
            var m = Meeting(row);
            var c = new Classification(row.ClassName ?? "", row.Confidence ?? 1, row.ClassifiedBy ?? "", row.LectureTitle ?? "", JsonList(row.Topics));
            string file = Notes.Render(m, c, markdown, row.SummaryModel ?? "");
            try
            {
                Directory.CreateDirectory(Py.Parent(row.MdPath));
                ReplaceFile(row.MdPath, OperatingSystem.IsWindows() ? file.Replace("\n", "\r\n") : file); // as filing writes it (Py.WriteText)
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                return NotesEdited.Unwritten;
            }
            string now = Now();
            Exec("UPDATE notes SET summary_md=?, updated_at=? WHERE id=?", markdown, now, noteId);
            Index(noteId, markdown, m.Transcript, now);
            return NotesEdited.Saved;
        }
    }
}
