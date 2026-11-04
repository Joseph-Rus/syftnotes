using System.Globalization;
using System.Text.RegularExpressions;

namespace StudyStash.Core;

/// <summary>A folder on this computer that holds a Syft Notes library: where it is, how many lectures' notes it has
/// (counted up to <see cref="LibraryFolders.CountCap"/>; <paramref name="Capped"/> when there are more), the day of
/// its newest one, and whether it's the one the library uses now.</summary>
public sealed record LibraryFolder(string Path, int Lectures, bool Capped, DateOnly? Newest, bool InUse);

/// <summary>
/// The library folders on this computer, when there may be more than one. A student's Documents can move (OneDrive
/// taking it over, or moving it from a personal account to a school one), and a library set up afresh after that made
/// a second Documents/Study Stash beside the first, with no word said. The app asks which one to use when it finds two
/// or more (Shell's library folder question); nothing here moves, merges or deletes a file.
/// <para>A folder counts only if it really holds a library: a class folder with at least one lecture's notes in it
/// (<c>&lt;folder&gt;/&lt;Class&gt;/*.md</c>, as the library files them). Each is looked at only one level deep, and
/// counting stops at <see cref="CountCap"/>, so a look is quick even in a folder OneDrive keeps online.</para>
/// </summary>
public static partial class LibraryFolders
{
    /// <summary>The names a library folder has had: Study Stash is the one the app has always made; the others are the
    /// app's later names, looked for in case a student named theirs after it.</summary>
    public static readonly string[] Names = ["Study Stash", "Syft Notes", "Egghead Notes"];

    /// <summary>The first versions (before the app) kept the library in the home folder, under this name.</summary>
    public const string OldestName = "GranolaShare";

    /// <summary>Lectures counted in one folder before the count stops.</summary>
    public const int CountCap = 5000;

    /// <summary>The library folders on this computer, the one in use (<paramref name="inUse"/>, config.toml's
    /// pool_dir) first, then the newest first.</summary>
    public static List<LibraryFolder> Find(string? inUse) => FindWith(inUse, []);

    /// <summary><see cref="Find(string?)"/>, with <paramref name="also"/> looked at too: each counts only if it really
    /// holds a library, as the others do (a folder named by hand once, outside the usual places).</summary>
    public static List<LibraryFolder> FindWith(string? inUse, IEnumerable<string> also) =>
        Find(inUse, [.. Places(inUse, Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), Py.UserHome(),
            Machine.Platform, Environment.GetEnvironmentVariable), .. also]);

    /// <summary>Which of <paramref name="places"/> hold a library, each place once however it's reached (a link or a
    /// junction to the same folder, or the same path in another case where the system ignores case).</summary>
    public static List<LibraryFolder> Find(string? inUse, IEnumerable<string> places)
    {
        string? used = inUse is { Length: > 0 } ? Real(inUse) : null;
        var seen = new HashSet<string>(Comparer);
        var found = new List<LibraryFolder>();
        foreach (string place in places)
        {
            string real = Real(place);
            if (!seen.Add(real)) continue;
            if (Look(real, used is not null && Comparer.Equals(real, used)) is { } folder) found.Add(folder);
        }
        return [.. found.OrderByDescending(f => f.InUse).ThenByDescending(f => f.Newest)];
    }

    /// <summary>
    /// Whether to ask which folder to use: two or more libraries are found, and the student hasn't answered for them
    /// yet. <paramref name="answered"/> is the folders they were shown last time, <paramref name="chose"/> the one they
    /// picked. It asks again only when a folder they haven't seen turns up, or when the library is no longer using the
    /// one they picked while that one is still here (set up afresh again).
    /// </summary>
    public static bool ShouldAsk(IReadOnlyList<LibraryFolder> found, IReadOnlyCollection<string>? answered, string? chose)
    {
        if (found.Count < 2) return false;
        if (answered is null || answered.Count == 0) return true;
        var known = new HashSet<string>(answered.Select(a => Real(a)), Comparer);
        if (found.Any(f => !known.Contains(Real(f.Path)))) return true;
        if (chose is not { Length: > 0 }) return false;
        string picked = Real(chose);
        return found.Any(f => Comparer.Equals(Real(f.Path), picked) && !f.InUse);
    }

    /// <summary>Paths compared the way this computer's files are: ignoring case on Windows and a Mac.</summary>
    public static StringComparer Comparer => OperatingSystem.IsLinux() ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase;

    /// <summary>
    /// Where a library folder may be on a computer: the one in use; Documents (wherever the system keeps it now) under
    /// each name a library folder has had; on Windows also the profile's own Documents, OneDrive's (a personal one
    /// and every "OneDrive - School"), and the OneDrive folders Windows names; on a Mac ~/Documents and each OneDrive
    /// in ~/Library/CloudStorage; and the first versions' folder in the home folder. Only lists them: whether each is
    /// there, and holds a library, is <see cref="Find(string?, IEnumerable{string})"/>'s look.
    /// </summary>
    public static List<string> Places(string? inUse, string documents, string userHome, string system, Func<string, string?> env)
    {
        var docs = new List<string>();
        if (documents.Length > 0) docs.Add(documents);
        docs.Add(Path.Combine(userHome, "Documents"));
        if (system == "Windows")
        {
            docs.Add(Path.Combine(userHome, "OneDrive", "Documents"));
            docs.AddRange(Children(userHome, "OneDrive - *").Select(d => Path.Combine(d, "Documents")));
            foreach (string name in (string[])["OneDrive", "OneDriveConsumer", "OneDriveCommercial"])
                if (env(name) is { Length: > 0 } root) docs.Add(Path.Combine(root, "Documents"));
        }
        else if (system == "Darwin")
        {
            docs.AddRange(Children(Path.Combine(userHome, "Library", "CloudStorage"), "OneDrive*").Select(d => Path.Combine(d, "Documents")));
        }
        var places = new List<string>();
        if (inUse is { Length: > 0 }) places.Add(inUse);
        foreach (string d in docs)
            foreach (string name in Names)
                places.Add(Path.Combine(d, name));
        places.Add(Path.Combine(userHome, OldestName));
        return places;
    }

    static IEnumerable<string> Children(string dir, string pattern)
    {
        try
        {
            return Directory.Exists(dir) ? Directory.EnumerateDirectories(dir, pattern).ToList() : [];
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <summary>The library in <paramref name="dir"/>, or null when it holds none: no class folder with a lecture's
    /// notes in it (the Inbox's captured notes, and hidden folders, aren't classes).</summary>
    static LibraryFolder? Look(string dir, bool inUse)
    {
        if (!Directory.Exists(dir)) return null;
        var options = new EnumerationOptions { IgnoreInaccessible = true, MatchCasing = MatchCasing.CaseInsensitive };
        int count = 0;
        DateOnly? newest = null;
        try
        {
            foreach (var cls in new DirectoryInfo(dir).EnumerateDirectories("*", options))
            {
                if (cls.Name.StartsWith('.') || cls.Name.Equals("Inbox", StringComparison.OrdinalIgnoreCase)) continue;
                foreach (var note in cls.EnumerateFiles("*.md", options))
                {
                    var day = DayOf(note);
                    if (newest is null || day > newest) newest = day;
                    if (++count >= CountCap) return new LibraryFolder(dir, count, true, newest, inUse);
                }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Gone, or turned away part-way: what was counted stands.
        }
        return count > 0 ? new LibraryFolder(dir, count, false, newest, inUse) : null;
    }

    /// <summary>A lecture's day: its file name starts with it ("2026-10-02 Cell division.md"), else the day the file
    /// was last written.</summary>
    static DateOnly DayOf(FileInfo note) =>
        DayPrefix().Match(note.Name) is { Success: true } m && DateOnly.TryParseExact(m.Value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day)
            ? day
            : DateOnly.FromDateTime(note.LastWriteTime);

    [GeneratedRegex(@"^\d{4}-\d{2}-\d{2}")]
    private static partial Regex DayPrefix();

    /// <summary>
    /// <paramref name="path"/> as it really is on disk: every link or junction along it followed to where it goes, so
    /// two ways to one folder come out the same. A part that isn't there is kept as it's written.
    /// </summary>
    public static string Real(string path) => Real(path, 0);

    static string Real(string path, int depth)
    {
        string full = Path.GetFullPath(path);
        string root = Path.GetPathRoot(full) ?? "";
        string at = root;
        foreach (string part in full[root.Length..].Split(Seps, StringSplitOptions.RemoveEmptyEntries))
        {
            at = Path.Combine(at, part);
            if (depth > 16) continue;
            try
            {
                var info = new DirectoryInfo(at);
                if (info.LinkTarget is null) continue;
                if (Directory.ResolveLinkTarget(at, returnFinalTarget: true) is { } target)
                    at = Real(target.FullName, depth + 1);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // A link that goes nowhere, or can't be read: the path stays as written from here.
            }
        }
        return at.Length > root.Length ? at.TrimEnd(Seps) : at;
    }

    static readonly char[] Seps = [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar];

    /// <summary>
    /// Where a library folder is, in the student's words: "Documents", "OneDrive – Personal › Documents",
    /// "OneDrive – Example University › Documents"; "› Syft Notes" after it for a folder not called Study Stash; the
    /// whole path for one outside the home folder that isn't in OneDrive.
    /// </summary>
    public static string Where(string path, string userHome)
    {
        string name = Path.GetFileName(path.TrimEnd(Seps));
        string parent = Path.GetDirectoryName(path.TrimEnd(Seps)) ?? "";
        var parts = new List<string>();
        string rest = parent;
        string home = userHome.TrimEnd(Seps);
        bool underHome = parent.Length > home.Length && parent.StartsWith(home, StringComparison.OrdinalIgnoreCase) && Seps.Contains(parent[home.Length]);
        if (underHome) rest = parent[(home.Length + 1)..];
        var bits = rest.Split(Seps, StringSplitOptions.RemoveEmptyEntries).ToList();
        // A Mac's cloud folders live in ~/Library/CloudStorage/<provider>.
        if (underHome && bits.Count >= 3 && bits[0] == "Library" && bits[1] == "CloudStorage") bits.RemoveRange(0, 2);
        int cloud = bits.FindLastIndex(b => b.StartsWith("OneDrive", StringComparison.OrdinalIgnoreCase) || b.StartsWith("GoogleDrive", StringComparison.Ordinal));
        if (cloud >= 0)
        {
            parts.Add(Cloud(bits[cloud]));
            parts.AddRange(bits.Skip(cloud + 1));
        }
        else if (underHome)
        {
            parts.AddRange(bits);
            if (parts.Count == 0) parts.Add("Your home folder");
        }
        else
        {
            parts.Add(parent);
        }
        if (!name.Equals(Names[0], StringComparison.OrdinalIgnoreCase)) parts.Add(name);
        return string.Join(" › ", parts);
    }

    /// <summary>"OneDrive" and "OneDrive-Personal" → OneDrive – Personal; "OneDrive - Example University" (Windows) or
    /// "OneDrive-Example University" (a Mac) → OneDrive – Example University; Google Drive's folder → Google Drive.</summary>
    static string Cloud(string folder)
    {
        if (folder.StartsWith("GoogleDrive", StringComparison.Ordinal)) return "Google Drive";
        string who = folder["OneDrive".Length..].TrimStart(' ', '-', '–').Trim();
        return $"OneDrive – {(who.Length == 0 ? "Personal" : who)}";
    }
}
