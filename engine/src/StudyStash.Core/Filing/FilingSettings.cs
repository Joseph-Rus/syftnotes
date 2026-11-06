using StudyStash.Core.Canvas;

namespace StudyStash.Core.Filing;

/// <summary>
/// The student's Submit &amp; Organize choices (in canvas.json, <see cref="CanvasSettings.Filing"/>): whether to tidy up
/// after a hand-in, the folders it may look in, when (ask first, or on its own after 12 hours), and what happens to the
/// files worked from (kept together with the work, or sent to the Trash). <see cref="Waiting"/> is the hand-ins a sync
/// saw that haven't been filed yet. Off, with no folders, until the student says. Test builds only for now.
/// </summary>
public sealed class FilingSettings
{
    public const string Ask = "ask", Later = "after_12_hours";
    public const string Organize = "organize", Clean = "clean";

    public bool On { get; set; }
    public List<string> Folders { get; set; } = [];
    /// <summary><see cref="Ask"/> (the default) or <see cref="Later"/>, which is kept but not acted on yet.</summary>
    public string When { get; set; } = Ask;
    /// <summary><see cref="Organize"/> (the default) or <see cref="Clean"/>.</summary>
    public string Mode { get; set; } = Organize;
    public List<WaitingHandIn> Waiting { get; set; } = [];

    /// <summary>At most this many hand-ins wait; the oldest go first.</summary>
    public const int MostWaiting = 20;

    /// <summary>A sync saw <paramref name="handedIn"/>: each waits to be filed, once, while tidying up is on.</summary>
    public void Saw(IEnumerable<HandedIn> handedIn)
    {
        if (!On) return;
        foreach (var h in handedIn)
            if (!Waiting.Any(w => w.Class == h.Class && w.Key == h.Key)) Waiting.Add(new WaitingHandIn(h.Class, h.Key, h.Assignment.Name));
        if (Waiting.Count > MostWaiting) Waiting.RemoveRange(0, Waiting.Count - MostWaiting);
    }
}

/// <summary>A hand-in a sync saw and that waits to be filed: its class, <see cref="HandedIn.Key"/>, and its assignment's name.</summary>
public sealed record WaitingHandIn(string Class, string Key, string Assignment);

/// <summary>Which folders Submit &amp; Organize may be given, and the one look into a folder as it's added.</summary>
public static class FilingFolders
{
    public const string WholeDisk = "That's a whole disk. Choose a folder on it, like Downloads.";
    public const string WholeHome = "That's your whole home folder. Choose a folder in it, like Downloads or Documents.";
    public const string HoldsHome = "That folder holds your whole home folder. Choose a folder in it, like Downloads or Documents.";
    public const string Shortcut = "That's a shortcut to another folder. Add the folder itself.";
    public const string Library = "That's your Syft Notes library. Your filed work goes there, so it isn't tidied.";
    public const string Gone = "That folder isn't there.";
    public const string NotAllowed = "Syft Notes wasn't allowed to look in that folder.";

    /// <summary>Why <paramref name="folder"/> can't be one, or null when it can: a whole disk (a Mac's /Volumes/&lt;disk&gt;
    /// too), the home folder or a folder that holds it, the library or a folder in it, or a link (a shortcut to another
    /// folder, or a folder reached through one). The folder as it really is, every link followed, is checked too.</summary>
    public static string? Refuse(string folder, string home, string library)
    {
        string full = Trim(Path.GetFullPath(folder));
        if (Linked(full)) return Shortcut;
        string[] homes = [.. new[] { home, LibraryFolders.Real(home) }.Select(h => Trim(Path.GetFullPath(h))).Distinct()];
        string[] libraries = [.. new[] { library, LibraryFolders.Real(library) }.Select(l => Trim(Path.GetFullPath(l))).Distinct()];
        foreach (string f in new[] { full, Trim(LibraryFolders.Real(full)) }.Distinct())
        {
            if (Disk(f)) return WholeDisk;
            if (homes.Any(h => h.Equals(f, SubmitMatch.Case))) return WholeHome;
            if (homes.Any(h => SubmitMatch.Within(h, f))) return HoldsHome;
            if (libraries.Any(l => SubmitMatch.Within(f, l))) return Library;
        }
        return null;
    }

    static string Trim(string path) => Path.TrimEndingDirectorySeparator(path);

    /// <summary>A disk's root: "/", "C:\\", or on a Mac a mounted disk (/Volumes/USB), or /Volumes itself.</summary>
    static bool Disk(string full) =>
        Path.GetPathRoot(full) is { } root && Trim(root).Equals(full, SubmitMatch.Case)
        || !OperatingSystem.IsWindows() && (full == "/Volumes" || Path.GetDirectoryName(full) == "/Volumes");

    /// <summary>The folder is a link, or sits under one, by the rule <see cref="SubmitMatch.Inside"/> uses below a folder.
    /// The links the system keeps at the top of the disk (a Mac's /var and /tmp) are passed by as the folder's parents;
    /// the folder as it really is is checked against home, the disks and the library all the same.</summary>
    static bool Linked(string full)
    {
        string root = Trim(Path.GetPathRoot(full) ?? "");
        try
        {
            for (string? d = full; d is not null && Trim(d).Length > root.Length; d = Path.GetDirectoryName(d))
                if ((d == full || Trim(Path.GetDirectoryName(d) ?? "") != root) && SubmitMatch.IsLink(d)) return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // A part that can't be looked at: Look says so when the folder is added.
        }
        return false;
    }

    /// <summary>Looks into <paramref name="folder"/> once, as it's added: on a Mac the system asks the student there and
    /// then whether Syft Notes may (Downloads, Desktop, Documents), right after their click rather than at some later
    /// sync. Null when it could; else why not.</summary>
    public static string? Look(string folder)
    {
        if (!Directory.Exists(folder)) return Gone;
        try
        {
            using var e = Directory.EnumerateFileSystemEntries(folder).GetEnumerator();
            e.MoveNext();
            return null;
        }
        catch (Exception x) when (x is UnauthorizedAccessException or IOException)
        {
            return NotAllowed;
        }
    }

    /// <summary>The folders offered first (none is added until the student picks it): Downloads, Desktop, Documents.
    /// Only their paths, as the system names them (OneDrive's Desktop and Documents on a PC that keeps them there): nothing
    /// in them is read.</summary>
    public static IReadOnlyList<(string Name, string Path)> Suggested()
    {
        static string Or(Environment.SpecialFolder f, string name) =>
            Environment.GetFolderPath(f) is { Length: > 0 } p ? p : Path.Combine(Py.UserHome(), name);
        return
        [
            ("Downloads", Path.Combine(Py.UserHome(), "Downloads")),
            ("Desktop", Or(Environment.SpecialFolder.DesktopDirectory, "Desktop")),
            ("Documents", Or(Environment.SpecialFolder.MyDocuments, "Documents")),
        ];
    }
}
