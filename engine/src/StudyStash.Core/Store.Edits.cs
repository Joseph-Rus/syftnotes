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
    /// whatever follows the end of a line: what tells an edit from a note file written by an older Egghead Notes.</summary>
    public static bool SameWords(string a, string b)
    {
        static string Plain(string notes) => string.Join("\n", Py.SplitLines(Py.Strip(notes)).Select(l => l.TrimEnd().TrimStart('#').TrimStart()));
        return Plain(a) == Plain(b);
    }
}

public sealed partial class Store
{
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
    /// the library's notes here first (where the app, search and Ask read them; the older copy kept as a version). An
    /// edit that says what it started from isn't saved over them unseen (<see cref="NotesEdited.Changed"/>): the
    /// student sees them and chooses. Without (an AI app's save, a paste, a version put back), they're kept as the
    /// version the new notes replace.</para>
    /// <para><paramref name="writer"/>: notes someone else wrote (an AI app through Egghead Notes' connection, a chat the
    /// student pasted them from), named as their writer the way the library names its own AI ("Claude (in the Claude
    /// app)"), and the lecture's notes done, any old error about them gone. Null keeps whoever wrote the notes as their
    /// writer; "" names none (the student's own).</para>
    /// <para>Every change is a version (<see cref="NotesVersions"/>), made by <paramref name="kind"/> (an edit, unless
    /// it's an AI app's save, a paste, a version put back, an undo or a redo; <paramref name="refSeq"/>: the version
    /// put back), so it can be undone. <paramref name="redraft"/>: an AI app's save that replaces its own from a few
    /// minutes before, untouched since, takes that version's place (a draft put right).</para>
    /// </summary>
    public NotesEdited EditNotes(string noteId, string markdown, string? basedOn = null, string? writer = null, bool redraft = false,
        string kind = NotesChangeKind.Edit, long? refSeq = null)
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

            // A summary edited in the note file itself (in another app, or by an AI working in the folder) is taken in
            // first, the library's older copy kept as a version. An edit that says what it started from then stops, so
            // the student sees them and chooses; anything else (an AI app's save, a paste, a version put back) goes on,
            // and they're the notes it replaces: kept as a version, never saved over unseen.
            if (edited is not null && Notes.SummaryInFile(edited) is { Length: > 0 } theirs && !Notes.SameWords(theirs, current))
            {
                string seen = Now();
                Exec("UPDATE notes SET summary_md=?, updated_at=? WHERE id=?", theirs, seen, noteId);
                Index(noteId, theirs, m.Transcript, seen);
                RecordVersion(noteId, (current, model, row.UpdatedAt ?? ""), theirs, model, NotesChangeKind.File);
                if (basedOn is not null) return NotesEdited.Changed;
                current = theirs;
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
            // A draft the same AI app is putting right: notes it saved a few minutes ago, untouched since (not edited,
            // put back, undone, redone or changed in their file). Its version takes these instead of keeping both. Only
            // when the newest version is that very save: the app's, by the same writer, with the words it saved.
            string outside = OutsideKey(noteId);
            bool draft = redraft && writer is not null && GetState(outside)?.Split('\n') is [var who, var at, var print]
                && who == writer && print == Notes.Fingerprint(current)
                && DateTimeOffset.TryParse(at, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal, out var last)
                && DateTimeOffset.UtcNow - last < RedraftWithin
                && NewestIsOwnSave(noteId, writer, print);
            if (redraft && writer is not null) SetState(outside, $"{writer}\n{now}\n{Notes.Fingerprint(markdown)}");
            if (writer is null) Exec("UPDATE notes SET summary_md=?, updated_at=? WHERE id=?", markdown, now, noteId);
            else Exec("UPDATE notes SET summary_md=?, summary_model=?, status=?, error=NULL, updated_at=? WHERE id=?",
                markdown, by.Length > 0 ? by : null, Done, now, noteId);
            Index(noteId, markdown, m.Transcript, now);
            RecordVersion(noteId, (current, model, row.UpdatedAt ?? ""), markdown, by, kind, refSeq, draftOf: draft);
            return NotesEdited.Saved;
        }
    }
}
