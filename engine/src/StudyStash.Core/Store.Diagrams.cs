using System.Security.Cryptography;
using System.Text;

namespace StudyStash.Core;

/// <summary>What became of diagrams designed for a lecture's notes, once they were ready.</summary>
public enum DiagramsLanded
{
    /// <summary>The notes were as the designer read them: every diagram went in where it was designed to go.</summary>
    Added,
    /// <summary>The notes had changed meanwhile (an edit): each diagram whose heading is still there went under it.</summary>
    Reanchored,
    /// <summary>The notes had changed and none of the diagrams' headings are there now: nothing was touched.</summary>
    Skipped,
    /// <summary>The lecture was deleted, or is being written again (its new notes get diagrams of their own).</summary>
    Gone,
    /// <summary>The note file couldn't be written (another app has it locked): nothing changed; it's tried again.</summary>
    Unwritten,
}

/// <summary>Where diagrams designed for a lecture landed: how, which went in and which were skipped, and whether the
/// note file kept the student's own edits (the diagrams went into it in place) or was written afresh.</summary>
public sealed record DiagramsOutcome(DiagramsLanded How, IReadOnlyList<DesignedDiagram> Placed, IReadOnlyList<DesignedDiagram> Skipped, string Why = "")
{
    /// <summary>The note file had edits of its own (by hand, or by an AI in a chat): the diagrams went into its summary
    /// in place, and nothing else in it changed.</summary>
    public bool FileEdited { get; init; }

    /// <summary>The note file had edits of its own and no summary with their headings to put them in: it was left
    /// exactly as it is (the app, the phone and Ask still get them).</summary>
    public bool FileLeft { get; init; }
}

public static partial class Notes
{
    /// <summary>What a designer read, as it's remembered: a hash of the notes, so a pass that finishes later can tell
    /// whether they're still the same.</summary>
    public static string Fingerprint(string notes) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(notes ?? "")));

    /// <summary>
    /// A note file edited since it was written (by hand, or by an AI in a chat) with diagrams put into its summary in
    /// place (<see cref="DiagramDesign.Place"/>): every other line of it exactly as it is. Null when it has no summary
    /// to put them in, or none of their headings is in it.
    /// </summary>
    public static string? PlaceInFile(string text, IReadOnlyList<DesignedDiagram> diagrams)
    {
        var lines = text.Split('\n').ToList();
        int head = lines.FindIndex(l => l.TrimEnd() == "## Summary");
        if (head < 0) return null;
        int end = head + 1;
        while (end < lines.Count && !lines[end].StartsWith("_Written by ", StringComparison.Ordinal)
               && lines[end].TrimEnd() is not ("## Private notes" or "## Transcript")) end++;
        string section = string.Join("\n", lines.Skip(head + 1).Take(end - head - 1));
        var (placed, which, _) = DiagramDesign.Place(section, diagrams);
        if (which.Count == 0) return null;
        lines.RemoveRange(head + 1, end - head - 1);
        lines.InsertRange(head + 1, placed.Split('\n'));
        return string.Join("\n", lines);
    }
}

public sealed partial class Store
{
    /// <summary>
    /// Diagrams designed for a lecture's notes, put into them once they're ready, in one step under the store's lock.
    /// Notes still as the designer read them (<paramref name="seen"/>, their <see cref="Notes.Fingerprint"/>) get every
    /// diagram where it was designed to go; notes changed since (a student's edit) get each one under its heading while
    /// that heading is still there, and nothing else in them moves; with none of their headings left, nothing changes.
    /// Diagrams an earlier pass added are replaced, never stacked. A lecture deleted, or queued to be written again,
    /// is left alone. The note file is replaced in one step (written beside it, then moved over it): a file nobody
    /// touched is written afresh, as filing would have written it; one edited since keeps every edit, the diagrams put
    /// into its summary in place. Search and Ask read the new notes at once.
    /// </summary>
    public DiagramsOutcome AddDiagrams(string noteId, string seen, IReadOnlyList<DesignedDiagram> diagrams)
    {
        lock (gate)
        {
            var row = Get(noteId);
            if (row is null || (row.Status is { Length: > 0 } s && s != Done) || string.IsNullOrEmpty(row.SummaryMd) || string.IsNullOrEmpty(row.MdPath))
                return new DiagramsOutcome(DiagramsLanded.Gone, [], diagrams);
            string current = row.SummaryMd;
            bool same = Notes.Fingerprint(current) == seen;
            var (updated, placed, skipped) = DiagramDesign.Place(current, diagrams);
            if (placed.Count == 0) return new DiagramsOutcome(DiagramsLanded.Skipped, [], skipped);

            var m = Meeting(row);
            var c = new Classification(row.ClassName ?? "", row.Confidence ?? 1, row.ClassifiedBy ?? "", row.LectureTitle ?? "", JsonList(row.Topics));
            string model = row.SummaryModel ?? "";
            string path = row.MdPath;
            bool edited = false, crlf = OperatingSystem.IsWindows(); // as filing writes it (Py.WriteText)
            string? file;
            if (!File.Exists(path)) file = Notes.Render(m, c, updated, model);
            else
            {
                byte[] bytes = File.ReadAllBytes(path);
                string text = new UTF8Encoding(false).GetString(bytes).Replace("\r\n", "\n").Replace('\r', '\n');
                if (text == Notes.Render(m, c, current, model)) file = Notes.Render(m, c, updated, model);
                else
                {
                    // Edited since it was filed: those edits stay, line endings and all. A file with no summary to put
                    // them in is kept as it is (the app, the phone and Ask still get them).
                    edited = true;
                    crlf = bytes.AsSpan().IndexOf("\r\n"u8) >= 0;
                    file = Notes.PlaceInFile(text, placed);
                }
            }
            if (file is not null)
            {
                try
                {
                    ReplaceFile(path, crlf ? file.Replace("\n", "\r\n") : file);
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    return new DiagramsOutcome(DiagramsLanded.Unwritten, [], diagrams, e.Message);
                }
            }
            string now = Now();
            Exec("UPDATE notes SET summary_md=?, updated_at=? WHERE id=?", updated, now, noteId);
            Index(noteId, updated, m.Transcript, now);
            return new DiagramsOutcome(same ? DiagramsLanded.Added : DiagramsLanded.Reanchored, placed, skipped) { FileEdited = edited, FileLeft = file is null };
        }
    }

    /// <summary>A file's new text, in one step: written to a hidden file beside it, then moved over it, so nothing ever
    /// reads it half-written (and a crash leaves the old one whole).</summary>
    static void ReplaceFile(string path, string text)
    {
        string temp = Path.Combine(Py.Parent(path), "." + Path.GetFileName(path) + ".diagrams");
        try
        {
            File.WriteAllBytes(temp, new UTF8Encoding(false).GetBytes(text));
            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }
}
