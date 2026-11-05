using System.Buffers.Binary;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using StudyStash.Core.Ai;
using StudyStash.Core.Canvas;

namespace StudyStash.Core.Filing;

/// <summary>How sure Submit &amp; Organize is that a file on this computer is the one handed in.</summary>
public enum Found
{
    /// <summary>No file in the approved folders has its name and size.</summary>
    None,
    /// <summary>Its name and size agree, but its bytes couldn't be compared (it's online only, or Canvas's copy
    /// couldn't be read): the student says.</summary>
    Unsure,
    /// <summary>Its bytes are the ones on Canvas.</summary>
    Exact,
}

/// <summary>Where the file handed in is on this computer, and how sure that is (no path for <see cref="Found.None"/>).</summary>
public sealed record FileMatch(Found Kind, string? Path);

/// <summary>How sure Submit &amp; Organize is that a file was worked from; only <see cref="High"/> is ticked for the student.</summary>
public enum Sure
{
    Low,
    Medium,
    High,
}

/// <summary>A file the student may have worked from for an assignment, why it's thought to be one, and how sure that is.</summary>
public sealed record Material(string Path, string Why, Sure Sure);

/// <summary>
/// Finding, in the folders the student approved and nowhere else, the file they handed in and the files they worked
/// from. Never the library folder or anything in it, nothing hidden or the system's, no link followed out of a folder,
/// and an online-only file (OneDrive's, iCloud's) is never read, so it isn't downloaded. Test builds only for now.
/// </summary>
public static partial class SubmitMatch
{
    /// <summary>Paths compare as the computer's own disks do: in any case, except on Linux.</summary>
    internal static readonly StringComparison Case = OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

    static readonly StringComparer PathComparer = OperatingSystem.IsLinux() ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase;

    /// <summary>At most this many materials are offered for one assignment.</summary>
    const int Most = 60;

    static string Full(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    /// <summary><paramref name="path"/> is <paramref name="root"/> or something in it.</summary>
    internal static bool Within(string path, string root)
    {
        string r = Path.EndsInDirectorySeparator(root) ? root : root + Path.DirectorySeparatorChar;
        return path.Equals(Path.TrimEndingDirectorySeparator(root), Case) || path.StartsWith(r, Case);
    }

    /// <summary>Hidden, or a file the system keeps (desktop.ini). A folder marked only as the system's isn't passed
    /// by: Windows marks a student's own folders so when they have an icon of their own.</summary>
    static bool Hidden(FileSystemInfo e)
    {
        try
        {
            return (e.Attributes & ((e is FileInfo) ? FileAttributes.Hidden | FileAttributes.System : FileAttributes.Hidden)) != 0;
        }
        catch (Exception x) when (x is IOException or UnauthorizedAccessException)
        {
            return true;
        }
    }

    /// <summary>Every file in the approved folders, each once: never the library folder or anything in it, nothing
    /// hidden or marked as the system's, and no link followed, so nothing outside the folders is reached.</summary>
    public static IEnumerable<string> FilesIn(IReadOnlyList<string> approved, string library)
    {
        string lib = Full(library);
        var seen = new HashSet<string>(PathComparer);
        foreach (string folder in approved.Select(Full))
        {
            if (!Directory.Exists(folder) || Within(folder, lib)) continue;
            foreach (string f in FileIndex.Walk(folder, e => Hidden(e) || Within(Full(e.FullName), lib)))
                if (Within(f, folder) && seen.Add(f)) yield return f;
        }
    }

    /// <summary>The file is in one of the approved folders (and not in the library), reached without a link: the only
    /// files Submit &amp; Organize moves.</summary>
    public static bool Inside(string path, IReadOnlyList<string> approved, string library)
    {
        string full = Full(path);
        if (Within(full, Full(library)) || !File.Exists(full)) return false;
        foreach (string root in approved.Select(Full))
        {
            if (!Within(full, root) || full.Length == root.Length) continue;
            bool linked = false;
            for (string? d = full; d is not null && d.Length > root.Length; d = Path.GetDirectoryName(d))
                if ((Directory.Exists(d) ? (FileSystemInfo)new DirectoryInfo(d) : new FileInfo(d)).LinkTarget is not null)
                {
                    linked = true;
                    break;
                }
            if (!linked) return true;
        }
        return false;
    }

    [GeneratedRegex(@" \(\d+\)$")]
    private static partial Regex DownloadCopy();

    [GeneratedRegex(@"-\d+$")]
    private static partial Regex CanvasCopy();

    /// <summary>A file here has a Canvas file's name, in any case, allowing the browser's " (1)" on a second download
    /// here and Canvas's own "-1" on a file handed in under a name it already had.</summary>
    public static bool SameName(string local, string canvas)
    {
        if (!Path.GetExtension(local).Equals(Path.GetExtension(canvas), StringComparison.OrdinalIgnoreCase)) return false;
        string mine = Path.GetFileNameWithoutExtension(local), theirs = Path.GetFileNameWithoutExtension(canvas);
        string[] ours = [mine, DownloadCopy().Replace(mine, "")], canvases = [theirs, CanvasCopy().Replace(theirs, "")];
        return ours.Any(o => canvases.Any(c => o.Equals(c, StringComparison.OrdinalIgnoreCase)));
    }

    const FileAttributes RecallOnOpen = (FileAttributes)0x40000, RecallOnDataAccess = (FileAttributes)0x400000;

    /// <summary>The file is a placeholder for one kept online (OneDrive's "online only", iCloud's "Remove download"):
    /// reading it would download it, so it isn't read.</summary>
    public static bool OnlineOnly(string path)
    {
        try
        {
            if (OperatingSystem.IsWindows())
                return (File.GetAttributes(path) & (FileAttributes.Offline | RecallOnOpen | RecallOnDataAccess)) != 0;
            return OperatingSystem.IsMacOS() && Dataless(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or DllNotFoundException or EntryPointNotFoundException)
        {
            return false;
        }
    }

    // A Mac's struct stat (64-bit inodes): 144 bytes, st_flags at 116. SF_DATALESS marks a file whose bytes are online only.
    [DllImport("libc", EntryPoint = "lstat", SetLastError = true)]
    static extern int LStat([MarshalAs(UnmanagedType.LPUTF8Str)] string path, byte[] stat);

    [DllImport("libc", EntryPoint = "lstat$INODE64", SetLastError = true)]
    static extern int LStatIntel([MarshalAs(UnmanagedType.LPUTF8Str)] string path, byte[] stat);

    const uint SfDataless = 0x40000000;

    static bool Dataless(string path)
    {
        var stat = new byte[144];
        int r = RuntimeInformation.ProcessArchitecture == Architecture.X64 ? LStatIntel(path, stat) : LStat(path, stat);
        return r == 0 && (BinaryPrimitives.ReadUInt32LittleEndian(stat.AsSpan(116)) & SfDataless) != 0;
    }

    /// <summary>The SHA-256 of Canvas's copy of a file handed in: the copy the sync already saved in the class's folder
    /// when there is one, else its bytes read from Canvas by <paramref name="read"/> (through the extension, kept in
    /// memory only, never saved). Null when neither can be had.</summary>
    public static Func<CancellationToken, Task<string?>> CanvasSha(FileRef file, string classDir, Func<string, CancellationToken, Task<byte[]?>>? read) =>
        async ct =>
        {
            if (file.Local is { Length: > 0 } local && LibraryBringIn.Sha(Path.Combine(classDir, local.Replace('/', Path.DirectorySeparatorChar))) is { } saved)
                return saved;
            if (read is null || file.Url.Length == 0) return null;
            return await read(file.Url, ct) is { } bytes ? Convert.ToHexStringLower(SHA256.HashData(bytes)) : null;
        };

    /// <summary>
    /// Where the file handed in is, in the approved folders: same name (<see cref="SameName"/>) and size, confirmed by
    /// comparing its SHA-256 with Canvas's copy (<paramref name="canvasSha"/>, asked once, only when a file here is
    /// worth comparing). A file whose bytes can't be compared (online only, Canvas's copy unknown) is
    /// <see cref="Found.Unsure"/>; one with the name and size but other bytes isn't it.
    /// </summary>
    public static async Task<FileMatch> FindAsync(FileRef handedIn, IReadOnlyList<string> approved, string library,
        Func<CancellationToken, Task<string?>> canvasSha, CancellationToken ct = default)
    {
        string? unsure = null;
        Task<string?>? theirs = null;
        foreach (string f in FilesIn(approved, library))
        {
            ct.ThrowIfCancellationRequested();
            if (!SameName(Path.GetFileName(f), handedIn.Name)) continue;
            long size;
            try
            {
                size = new FileInfo(f).Length;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                continue;
            }
            if (handedIn.Size is long want && size != want) continue;
            if (handedIn.Size is null || OnlineOnly(f))
            {
                unsure ??= f;
                continue;
            }
            if (await (theirs ??= canvasSha(ct)) is not { } sha)
            {
                unsure ??= f;
                continue;
            }
            if (sha.Equals(LibraryBringIn.Sha(f), StringComparison.OrdinalIgnoreCase)) return new FileMatch(Found.Exact, f);
        }
        return unsure is null ? new FileMatch(Found.None, null) : new FileMatch(Found.Unsure, unsure);
    }

    /// <summary>Words every assignment's name has, which say nothing about which one it is.</summary>
    static readonly HashSet<string> Plain =
    [
        "assignment", "homework", "project", "essay", "paper", "report", "draft", "final", "part", "week", "problem",
        "problems", "quiz", "with", "from", "your", "this", "that", "notes", "submission", "question", "questions",
        "chapter", "unit", "module", "exercise", "exercises", "worksheet", "file", "copy", "version", "untitled", "document",
    ];

    [GeneratedRegex(@"\p{L}+|\d+")]
    private static partial Regex Words();

    static IEnumerable<string> WordsOf(string text) => Words().Matches(text.ToLowerInvariant()).Select(m => m.Value);

    /// <summary>The words in an assignment's name that say which one it is: four letters or more, and not ones every
    /// assignment has ("homework", "draft", "week").</summary>
    public static HashSet<string> Distinctive(string name) =>
        [.. WordsOf(name).Where(w => w.Length >= 4 && !Plain.Contains(w) && !w.All(char.IsAsciiDigit))];

    static DateTimeOffset? When(string? iso) =>
        DateTimeOffset.TryParse(iso, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var t) ? t : null;

    static DateTimeOffset? Changed(string path)
    {
        try
        {
            return new DateTimeOffset(File.GetLastWriteTimeUtc(path), TimeSpan.Zero);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// The files in the approved folders the student may have worked from for <paramref name="work"/>, surest first:
    /// a file of the name of one the professor attached to the assignment (sure), one from the assignment's module on
    /// Canvas, and drafts and notes whose name shares the assignment's own words (<see cref="Distinctive"/>) or that
    /// changed between the assignment's opening (its unlock date, else when it was made) and the hand-in (sure when
    /// both). <paramref name="handedIn"/> (the files found for the hand-in itself) are left out.
    /// </summary>
    public static List<Material> Materials(HandedIn work, CourseIndex? index, IReadOnlyList<string> approved, string library, IEnumerable<string> handedIn)
    {
        var a = work.Assignment;
        var professor = a.InstructionFiles.Select(f => f.Name).ToList();
        var module = (index?.Modules ?? []).Where(m => m.Items.Any(i => i.AssignmentId == a.Id)).SelectMany(m => m.Items)
            .Where(i => i.Kind == "file").SelectMany(i => new[] { i.Title, Path.GetFileName(i.Local ?? "") }).Where(n => n.Length > 0).ToList();
        var words = Distinctive(a.Name);
        DateTimeOffset? opened = When(a.UnlockAt) ?? When(a.CreatedAt), handed = When(work.SubmittedAt);
        var skip = handedIn.Select(Full).ToHashSet(PathComparer);
        var found = new List<Material>();
        foreach (string f in FilesIn(approved, library))
        {
            if (skip.Contains(f)) continue;
            string name = Path.GetFileName(f);
            bool named = words.Overlaps(WordsOf(Path.GetFileNameWithoutExtension(f)));
            bool during = opened is { } start && handed is { } end && Changed(f) is { } at && at >= start && at <= end;
            if (professor.Any(p => SameName(name, p)))
                found.Add(new Material(f, "Your professor attached a file of this name to the assignment", Sure.High));
            else if (module.Any(m => SameName(name, m)))
                found.Add(new Material(f, "It's in the assignment's module on Canvas", named || during ? Sure.High : Sure.Medium));
            else if (named && during)
                found.Add(new Material(f, "Its name shares words with the assignment, and you changed it while working on it", Sure.High));
            else if (named)
                found.Add(new Material(f, "Its name shares words with the assignment", Sure.Medium));
            else if (during)
                found.Add(new Material(f, "You changed it while working on the assignment", Sure.Low));
        }
        return [.. found.OrderByDescending(m => m.Sure).Take(Most)];
    }
}
