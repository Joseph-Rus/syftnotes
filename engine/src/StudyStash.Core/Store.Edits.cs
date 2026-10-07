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
    /// </summary>
    public NotesEdited EditNotes(string noteId, string markdown, string? basedOn = null)
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
                    if (text != Notes.Render(m, c, current, model))
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

            string? file = edited is null ? Notes.Render(m, c, markdown, model) : Notes.WithSummary(edited, markdown);
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
            Exec("UPDATE notes SET summary_md=?, updated_at=? WHERE id=?", markdown, now, noteId);
            Index(noteId, markdown, m.Transcript, now);
            return NotesEdited.Saved;
        }
    }
}
