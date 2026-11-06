using System.Globalization;
using StudyStash.Core.Ai;
using StudyStash.Core.Canvas;

namespace StudyStash.Core.Filing;

/// <summary>One file Submit &amp; Organize filed: the run it was part of, when, the class and assignment, where it was
/// (<see cref="From"/>), where it went (<see cref="To"/>: in the class's folder, or in the Trash, "" when the system
/// didn't say where), how (<see cref="Moved"/> or <see cref="Trashed"/>), its SHA-256 ("" for a file online only, which
/// isn't read), when it was put back (null until it is), and whether the move was still under way (<see cref="Pending"/>:
/// it was written down before the file moved, and a crash came before it was marked done).</summary>
public sealed record FiledMove(long Seq, string Run, string At, string ClassName, string Assignment, string From, string To, string Kind, string Hash,
    string? Undone = null, bool Pending = false)
{
    public const string Moved = "moved", Trashed = "trashed";
}

/// <summary>What a run did: its id (to undo it), each file it filed, and the files it left where they were (not in an
/// approved folder, gone, or a move that failed), with why.</summary>
public sealed record FilingResult(string Run, List<FiledMove> Moves, List<(string Path, string Why)> Left);

/// <summary>One run in the undo log, as "Filed work" lists it: when, the class and assignment, how many files it filed,
/// and how many of them were put back since.</summary>
public sealed record FiledRun(string Run, string At, string ClassName, string Assignment, int Files, int Undone);

/// <summary>How a file came back on Undo.</summary>
public enum UndoOutcome
{
    /// <summary>Back where it was.</summary>
    Back,
    /// <summary>Something else sits where it was now: it's back beside that, under a name of its own.</summary>
    Beside,
    /// <summary>It's in the Trash, and the system can't put it back from there: the student restores it from the Trash
    /// (the Recycle Bin).</summary>
    InTrash,
    /// <summary>It isn't where it was filed any more (moved or deleted since).</summary>
    Missing,
    /// <summary>Moving it back failed (it's open, or the disk said no); it stays filed, and Undo can be tried again.</summary>
    Failed,
}

/// <summary>One file Undo dealt with: where it was before filing, where it is now (null when it isn't back), and how.</summary>
public sealed record UndoItem(string Original, string? PutAt, UndoOutcome How);

/// <summary>
/// Files a hand-in into its class's folder in the library: the file handed in into &lt;Assignment&gt;/Submitted/, and the
/// materials the student ticked into &lt;Assignment&gt;/Materials/ (Organize) or the system's Trash (Clean; Materials/
/// when the disk has no Trash). Only files in the approved folders move. Nothing is changed or deleted for good, and
/// nothing is written over: a name already taken gets " (2)". A move on one disk is a rename; to another disk, a whole
/// copy checked by size and SHA-256 before the original goes. Every move is written down (<see cref="Store.AddFiledMove"/>)
/// so <see cref="Undo"/> puts a whole run back. Afterwards the file handed in is searchable with the class's other files
/// (search_files, and so Ask), by the library's file index. Test builds only for now (<see cref="TestSwitches.On"/>).
/// </summary>
public sealed class SubmitFiler
{
    public const string SubmittedFolder = "Submitted", MaterialsFolder = "Materials";

    readonly Store store;
    readonly IReadOnlyList<string> approved;
    readonly ITrash trash;
    readonly FileIndex? files;

    static bool Enabled => TestSwitches.On;

    /// <param name="approved">The folders the student let Submit &amp; Organize look in and move from.</param>
    /// <param name="files">The library's file index, brought up to date after filing (none: it catches up on its own).</param>
    public SubmitFiler(Store store, IReadOnlyList<string> approved, ITrash? trash = null, FileIndex? files = null)
    {
        if (!Enabled) throw new InvalidOperationException("Submit & Organize is in test builds only for now.");
        this.store = store;
        this.approved = approved;
        this.trash = trash ?? new SystemTrash();
        this.files = files;
    }

    /// <summary>Whether a rename moves a file from one path's folder to the other's (they're on one disk). Tests say no,
    /// to take the copy-and-check way.</summary>
    internal Func<string, string, bool> SameDisk { get; init; } = OnOneDisk;

    internal Func<DateTimeOffset> Clock { get; init; } = () => DateTimeOffset.UtcNow;

    /// <summary>The assignment's folder in its class's folder, which Submitted/ and Materials/ go in.</summary>
    public string FolderFor(HandedIn work) => Path.Combine(store.ClassDir(work.Class), Crawl.SafeName(work.Assignment.Name, 80));

    /// <summary>Organize: the file handed in into Submitted/, <paramref name="materials"/> (the ticked ones) into Materials/.</summary>
    public Task<FilingResult> OrganizeAsync(HandedIn work, string? submitted, IReadOnlyList<string> materials) => RunAsync(work, submitted, materials, clean: false);

    /// <summary>Clean: the file handed in into Submitted/, <paramref name="materials"/> (the ticked ones) to the Trash.</summary>
    public Task<FilingResult> CleanAsync(HandedIn work, string? submitted, IReadOnlyList<string> materials) => RunAsync(work, submitted, materials, clean: true);

    async Task<FilingResult> RunAsync(HandedIn work, string? submitted, IReadOnlyList<string> materials, bool clean)
    {
        string run = Guid.NewGuid().ToString("N")[..12], at = Stamp();
        string folder = FolderFor(work);
        var result = new FilingResult(run, [], []);
        // Each move is written down before the file moves and marked done after, so a crash between the two leaves a
        // pending row that Undo can still follow.
        FiledMove Log(string from, string to, string kind) =>
            store.AddFiledMove(new FiledMove(0, run, at, work.Class, work.Assignment.Name, from, to, kind, HashOf(from)));
        void Done(FiledMove row, string to)
        {
            store.FinishFiledMove(row.Seq, to);
            result.Moves.Add(row with { To = to, Pending = false });
        }

        void Into(string path, string dir)
        {
            if (!SubmitMatch.Inside(path, approved, store.PoolDir))
            {
                result.Left.Add((path, "It isn't in a folder Syft Notes may tidy."));
                return;
            }
            FiledMove? row = null;
            try
            {
                Directory.CreateDirectory(dir);
                string to = Path.Combine(dir, Attachments.FreeName(dir, Path.GetFileName(path)));
                row = Log(path, to, FiledMove.Moved);
                Move(path, to);
                Done(row, to);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                if (row is not null) store.DropFiledMove(row.Seq);
                result.Left.Add((path, SubmitMatch.OnlineOnly(path)
                    ? "It's kept online only, and moving it to another disk would download it, so it's where it was."
                    : "Syft Notes couldn't move it (it may be open in another app), so it's where it was."));
            }
        }

        if (submitted is not null) Into(submitted, Path.Combine(folder, SubmittedFolder));
        foreach (string m in materials)
        {
            if (clean && SubmitMatch.Inside(m, approved, store.PoolDir))
            {
                var row = Log(m, "", FiledMove.Trashed);
                string? where;
                try
                {
                    where = trash.Send(m);
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    where = null;
                }
                // Gone without the system saying where: it's in the Trash, and Undo says to look there.
                if (where is not null || !File.Exists(m))
                {
                    Done(row, where ?? "");
                    continue;
                }
                store.DropFiledMove(row.Seq);
            }
            // Organize, or Clean where the Trash wouldn't keep it: it goes in with the assignment's materials.
            Into(m, Path.Combine(folder, MaterialsFolder));
        }
        if (files is not null && result.Moves.Count > 0) await files.UpdateAsync();
        return result;
    }

    /// <summary>A file's SHA-256 for the log: "" when it's online only (it isn't read) or can't be read.</summary>
    static string HashOf(string path) => SubmitMatch.OnlineOnly(path) ? "" : LibraryBringIn.Sha(path) ?? "";

    /// <summary>
    /// Puts a run's files back where they were, newest first. A file whose place is taken now comes back beside it
    /// under a name of its own (<see cref="UndoOutcome.Beside"/>); one in the Trash comes back from there where the system
    /// allows (<see cref="UndoOutcome.InTrash"/> where it doesn't). A move a crash cut short (still pending) is followed
    /// to where the file really is: still where it was, it's left there. Files already put back are left alone, so Undo
    /// can be run again after one failed.
    /// </summary>
    public List<UndoItem> Undo(string run)
    {
        var done = new List<UndoItem>();
        foreach (var m in store.FiledMoves(run).Where(f => f.Undone is null).OrderByDescending(f => f.Seq))
        {
            if (m.Pending && File.Exists(m.From))
            {
                // It never left (or a copy to another disk stopped before the original went): nothing to put back.
                store.MarkFiledUndone(m.Seq, Stamp());
                done.Add(new UndoItem(m.From, m.From, UndoOutcome.Back));
                continue;
            }
            string dir = Py.Parent(m.From);
            string back = File.Exists(m.From) ? Path.Combine(dir, Attachments.FreeName(dir, Path.GetFileName(m.From))) : m.From;
            try
            {
                Directory.CreateDirectory(dir);
                if (m.Kind == FiledMove.Trashed)
                {
                    if (!trash.Restore(m.To, back))
                    {
                        done.Add(new UndoItem(m.From, null, UndoOutcome.InTrash));
                        continue;
                    }
                }
                else if (!File.Exists(m.To))
                {
                    done.Add(new UndoItem(m.From, null, UndoOutcome.Missing));
                    continue;
                }
                else Move(m.To, back);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                done.Add(new UndoItem(m.From, null, UndoOutcome.Failed));
                continue;
            }
            store.MarkFiledUndone(m.Seq, Stamp());
            done.Add(new UndoItem(m.From, back, back == m.From ? UndoOutcome.Back : UndoOutcome.Beside));
        }
        return done;
    }

    string Stamp() => Clock().UtcDateTime.ToString("o", CultureInfo.InvariantCulture);

    /// <summary>Moves <paramref name="from"/> to <paramref name="to"/>, never over a file there. On one disk it's a
    /// rename. To another disk it's a whole copy, made beside its place and only renamed into it once its size and
    /// SHA-256 are the original's; then the original goes. If the original can't go, the copy goes instead: never two
    /// copies, never half of one. A file kept online only isn't copied (that would download it): IOException.</summary>
    void Move(string from, string to)
    {
        if (SameDisk(from, to))
        {
            File.Move(from, to, overwrite: false);
            return;
        }
        if (SubmitMatch.OnlineOnly(from)) throw new IOException("The file is kept online only; copying it would download it.");
        long size = new FileInfo(from).Length;
        string sha = LibraryBringIn.Sha(from) ?? throw new IOException("The file couldn't be read to copy it.");
        Store.PutWhole(to, temp =>
        {
            File.Copy(from, temp);
            if (new FileInfo(temp).Length != size || LibraryBringIn.Sha(temp) != sha) throw new IOException("The copy wasn't the same as the file.");
        }, overwrite: false);
        try
        {
            File.Delete(from);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            File.Delete(to);
            throw;
        }
    }

    /// <summary>Both paths are on one disk: the same mounted volume (the deepest one holding each), as the system lists
    /// them. Not knowing counts as two disks, which only means the careful way.</summary>
    static bool OnOneDisk(string a, string b)
    {
        try
        {
            var roots = DriveInfo.GetDrives().Select(d => d.RootDirectory.FullName).ToList();
            string? Of(string p)
            {
                string full = Path.GetFullPath(p);
                return roots.Where(r => SubmitMatch.Within(full, r)).MaxBy(r => r.Length);
            }
            return Of(a) is { } x && Of(b) is { } y && x.Equals(y, SubmitMatch.Case);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
