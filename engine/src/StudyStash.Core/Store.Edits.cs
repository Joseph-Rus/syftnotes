using System.Text;

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
    /// rewritten somewhere else, or in their own file): the edit wasn't saved.</summary>
    Changed,
    /// <summary>The note file couldn't be read or written (another app has it locked): nothing changed.</summary>
    Unwritten,
}

public static partial class Notes
{
    /// <summary>Undoes <see cref="DemoteHeadings"/>: every heading it pushed down a level, back up.</summary>
    public static string PromoteHeadings(string text)
    {
        var output = new List<string>();
        bool fenced = false;
        foreach (string raw in Py.SplitLines(text))
        {
            string line = raw;
            if (Py.LStrip(line).StartsWith("```", StringComparison.Ordinal)) fenced = !fenced;
            if (!fenced && line.StartsWith("##", StringComparison.Ordinal) && Heading().IsMatch(line[1..])) line = line[1..];
            output.Add(line);
        }
        return string.Join("\n", output);
    }

    /// <summary>The notes in a note file's summary, as the library keeps them (their headings back up the level a
    /// note file has them down); null for a file with no summary.</summary>
    public static string? SummaryInFile(string text)
    {
        var lines = text.Split('\n').ToList();
        return SummaryLines(lines) is { } at ? PromoteHeadings(Py.Strip(string.Join("\n", lines.Skip(at.First).Take(at.End - at.First)))) : null;
    }

    /// <summary>A note file with <paramref name="summaryMd"/> as its summary, and every other line of it exactly as it
    /// is. Null for a file with no summary to put them in.</summary>
    public static string? WithSummary(string text, string summaryMd)
    {
        var lines = text.Split('\n').ToList();
        if (SummaryLines(lines) is not { } at) return null;
        lines.RemoveRange(at.First, at.End - at.First);
        lines.InsertRange(at.First, ["", .. DemoteHeadings(Py.Strip(summaryMd)).Split('\n'), ""]);
        return string.Join("\n", lines);
    }

    /// <summary>A note file whose summary says who wrote it as <paramref name="writer"/> ("_Written by … from the
    /// transcript._" under it, as <see cref="Render"/> writes it; with no writer, no such line), every other line as it
    /// is. As it is, for a file with no summary.</summary>
    public static string WithWriter(string text, string writer, bool fromNotes)
    {
        var lines = text.Split('\n').ToList();
        if (SummaryLines(lines) is not { } at) return text;
        string said = $"_Written by {writer} from {(fromNotes ? "the notes it came with" : "the transcript")}._";
        bool has = at.End < lines.Count && lines[at.End].StartsWith("_Written by ", StringComparison.Ordinal);
        if (has && writer.Length > 0) lines[at.End] = said;
        else if (has) lines.RemoveRange(at.End, at.End + 1 < lines.Count && lines[at.End + 1].Length == 0 ? 2 : 1);
        else if (writer.Length > 0) lines.InsertRange(at.End, [said, ""]);
        return string.Join("\n", lines);
    }

    /// <summary>Whether two versions of some notes say the same thing, whatever level their headings are at and
    /// whatever follows the end of a line: what tells an edit from a note file written by an older Study Stash.</summary>
    public static bool SameWords(string a, string b)
    {
        static string Plain(string notes) => string.Join("\n", Py.SplitLines(Py.Strip(notes)).Select(l => l.TrimEnd().TrimStart('#').TrimStart()));
        return Plain(a) == Plain(b);
    }
}

public sealed partial class Store
{
    /// <summary>How many earlier versions of its notes a lecture keeps: its first ones, and the newest others.</summary>
    public const int VersionsKept = 5;

    /// <summary>Saves of notes from outside the library (an AI app, a paste) this close to the same writer's last ones
    /// are a draft put right ("fix the diagram and save again"), not a version of their own.</summary>
    public static readonly TimeSpan RedraftWithin = TimeSpan.FromMinutes(10);

    /// <summary>The notes a lecture had before each time they were replaced (an edit, notes an AI app saved, notes
    /// pasted from a chat, putting earlier ones back), newest last: <see cref="RestoreNotesVersion"/> puts one back. At
    /// most <see cref="VersionsKept"/> a lecture, and never without the oldest (the library's own notes, or the
    /// student's first), however often an AI saves. Kept while a lecture is in the trash; gone with it for good.</summary>
    void CreateNotesVersions() => Exec("""
        CREATE TABLE IF NOT EXISTS notes_versions (
            seq INTEGER PRIMARY KEY AUTOINCREMENT,
            id TEXT NOT NULL,
            summary_md TEXT,
            summary_model TEXT,
            written_at TEXT,
            replaced_at TEXT
        );
        CREATE INDEX IF NOT EXISTS notes_versions_id ON notes_versions(id, seq);
        """);

    /// <summary>
    /// A lecture's notes as the student edited them, saved as its notes in one step under the store's lock: search and
    /// Ask read them at once, and who wrote them, the lecture's class and everything else about it stay as they were.
    /// <paramref name="basedOn"/> is the <see cref="Notes.Fingerprint"/> of the notes the edit started from: with other
    /// notes there now, nothing is saved (<see cref="NotesEdited.Changed"/>), so an edit never quietly undoes what
    /// arrived meanwhile; null saves over whatever is there.
    /// <para>The note file is replaced in one step. One nobody has touched since the library wrote it is written
    /// afresh (as using a rewrite's new notes writes it). One edited since, in another app or by an AI in a chat,
    /// keeps every line of its own: only its summary is replaced, and with no summary in it, it's left exactly as it
    /// is (the app, the phone and Ask still get the edit).</para>
    /// <para>A summary edited in the file itself is notes the app never showed: it shows the library's. Those become
    /// the library's notes here (where the app, search and Ask read them), and an edit that says what it started
    /// from isn't saved over them unseen (<see cref="NotesEdited.Changed"/>): the student sees them and chooses.</para>
    /// <para><paramref name="writer"/>: notes someone else wrote (an AI app through Study Stash's connection, a chat the
    /// student pasted them from), named as their writer the way the library names its own AI ("Claude (in the Claude
    /// app)"), and the lecture's notes done, any old error about them gone. Null keeps whoever wrote the notes as their
    /// writer; "" names none (the student's own).</para>
    /// <para>The notes replaced are kept as a version (<see cref="NotesVersions"/>), so whatever replaced them can be
    /// undone. <paramref name="redraft"/>: notes from outside the library (an AI app, a paste) that replace the same
    /// writer's own from a few minutes before keep no version of those (a draft put right); the version before them
    /// stays.</para>
    /// </summary>
    public NotesEdited EditNotes(string noteId, string markdown, string? basedOn = null, string? writer = null, bool redraft = false)
    {
        lock (gate)
        {
            var row = Get(noteId);
            if (row is null || string.IsNullOrEmpty(row.MdPath)) return NotesEdited.Gone;
            if (row.Status is Queued or Working) return NotesEdited.Busy;
            string current = row.SummaryMd ?? "";
            var m = Meeting(row);
            var c = new Classification(row.ClassName ?? "", row.Confidence ?? 1, row.ClassifiedBy ?? "", row.LectureTitle ?? "", JsonList(row.Topics));
            string model = row.SummaryModel ?? "";
            string path = row.MdPath;
            bool crlf = OperatingSystem.IsWindows(); // as filing writes it (Py.WriteText)
            string? edited = null; // the note file's text, when it has edits of its own
            try
            {
                if (File.Exists(path))
                {
                    byte[] bytes = File.ReadAllBytes(path);
                    string text = new UTF8Encoding(false).GetString(bytes).Replace("\r\n", "\n").Replace('\r', '\n');
                    // (A file from before a too-short recording's file said so has the older words where its notes
                    // would be: nobody has touched that one either.)
                    string written = Notes.Render(m, c, current, model);
                    if (text != written && text != written.Replace(Notes.TooShortForNotes, Notes.NoNotesReturned))
                    {
                        edited = text;
                        crlf = bytes.AsSpan().IndexOf("\r\n"u8) >= 0; // its own line endings stay
                    }
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                return NotesEdited.Unwritten;
            }

            if (basedOn is not null && edited is not null && Notes.SummaryInFile(edited) is { Length: > 0 } theirs && !Notes.SameWords(theirs, current))
            {
                string seen = Now();
                Exec("UPDATE notes SET summary_md=?, updated_at=? WHERE id=?", theirs, seen, noteId);
                Index(noteId, theirs, m.Transcript, seen);
                return NotesEdited.Changed;
            }
            if (basedOn is not null && Notes.Fingerprint(current) != basedOn) return NotesEdited.Changed;

            string by = writer ?? model;
            string? file = edited is null ? Notes.Render(m, c, markdown, by) : Notes.WithSummary(edited, markdown);
            // A file with edits of its own says who wrote the notes now, under them, as the library's own file would.
            if (edited is not null && file is not null && writer is not null) file = Notes.WithWriter(file, by, Summarize.FromNotes(m));
            if (file is not null)
            {
                try
                {
                    Directory.CreateDirectory(Py.Parent(path));
                    ReplaceFile(path, crlf ? file.Replace("\n", "\r\n") : file);
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    return NotesEdited.Unwritten;
                }
            }
            string now = Now();
            // What they replace can be put back, but for a draft the same writer is putting right: notes it saved from
            // outside a few minutes ago, untouched since (not edited, not put back, not changed in their file).
            string outside = OutsideKey(noteId);
            bool draft = redraft && writer is not null && GetState(outside)?.Split('\n') is [var who, var at, var print]
                && who == writer && print == Notes.Fingerprint(current)
                && DateTimeOffset.TryParse(at, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal, out var last)
                && DateTimeOffset.UtcNow - last < RedraftWithin;
            if (Py.Strip(current).Length > 0 && (current != markdown || model != by) && !draft)
                KeepVersion(noteId, current, model, row.UpdatedAt ?? "", now);
            if (redraft && writer is not null) SetState(outside, $"{writer}\n{now}\n{Notes.Fingerprint(markdown)}");
            if (writer is null) Exec("UPDATE notes SET summary_md=?, updated_at=? WHERE id=?", markdown, now, noteId);
            else Exec("UPDATE notes SET summary_md=?, summary_model=?, status=?, error=NULL, updated_at=? WHERE id=?",
                markdown, by.Length > 0 ? by : null, Done, now, noteId);
            Index(noteId, markdown, m.Transcript, now);
            return NotesEdited.Saved;
        }
    }

    /// <summary>One version kept, and the oldest kept with the newest others when there are more than
    /// <see cref="VersionsKept"/>.</summary>
    void KeepVersion(string noteId, string markdown, string model, string writtenAt, string replacedAt)
    {
        Exec("INSERT INTO notes_versions(id, summary_md, summary_model, written_at, replaced_at) VALUES(?,?,?,?,?)",
            noteId, markdown, model.Length > 0 ? model : null, writtenAt, replacedAt);
        var seqs = new List<long>();
        using (var cmd = Command("SELECT seq FROM notes_versions WHERE id=? ORDER BY seq", noteId))
        using (var r = cmd.ExecuteReader())
            while (r.Read()) seqs.Add(r.GetInt64(0));
        foreach (long seq in seqs.Skip(1).Take(Math.Max(0, seqs.Count - VersionsKept)))
            Exec("DELETE FROM notes_versions WHERE seq=?", seq);
    }

    /// <summary>The versions of a lecture's notes kept from before they were replaced, newest first: their words, who
    /// wrote them ("" for the student's own), when they were written and when they were replaced.</summary>
    public List<(long Seq, string Markdown, string Model, string WrittenAt, string ReplacedAt)> NotesVersions(string noteId)
    {
        lock (gate)
        {
            var list = new List<(long, string, string, string, string)>();
            using var cmd = Command("SELECT seq, summary_md, summary_model, written_at, replaced_at FROM notes_versions WHERE id=? ORDER BY seq DESC", noteId);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                string md = r.IsDBNull(1) ? "" : r.GetString(1);
                if (md.Trim().Length > 0)
                    list.Add((r.GetInt64(0), md, r.IsDBNull(2) ? "" : r.GetString(2), r.IsDBNull(3) ? "" : r.GetString(3), r.IsDBNull(4) ? "" : r.GetString(4)));
            }
            return list;
        }
    }

    /// <summary>Puts a version of a lecture's notes back (<paramref name="seq"/>, or the newest), with its writer, the way
    /// any edit is saved (<see cref="EditNotes"/>): the notes it replaces become a version in its place, so putting back
    /// can be undone, or gone back from again. <see cref="NotesEdited.Gone"/> when there's no such version.</summary>
    public NotesEdited RestoreNotesVersion(string noteId, long? seq = null)
    {
        lock (gate)
        {
            var versions = NotesVersions(noteId);
            if ((seq is { } wanted ? versions.FirstOrDefault(v => v.Seq == wanted) : versions.FirstOrDefault()) is not { Seq: > 0 } version)
                return NotesEdited.Gone;
            var said = EditNotes(noteId, version.Markdown, writer: version.Model);
            // Back as the lecture's notes, it isn't an earlier version of them any more.
            if (said == NotesEdited.Saved) Exec("DELETE FROM notes_versions WHERE seq=?", version.Seq);
            return said;
        }
    }

    /// <summary>Who last saved a lecture's notes from outside the library, when, and what they saved: how a draft put
    /// right is told from notes that are the lecture's own by now.</summary>
    static string OutsideKey(string noteId) => "notes-from-outside:" + noteId;

    /// <summary>A lecture deleted for good takes its earlier versions with it.</summary>
    void ForgetNotesVersions(string noteId)
    {
        Exec("DELETE FROM notes_versions WHERE id=?", noteId);
        Exec("DELETE FROM sync_state WHERE key=?", OutsideKey(noteId));
    }
}
