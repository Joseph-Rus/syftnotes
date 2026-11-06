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
    public const string Library = "That's your Syft Notes library. Your filed work goes there, so it isn't tidied.";
    public const string Gone = "That folder isn't there.";
    public const string NotAllowed = "Syft Notes wasn't allowed to look in that folder.";

    /// <summary>Why <paramref name="folder"/> can't be one (a whole disk, the home folder itself, the library or a folder
    /// in it), or null when it can.</summary>
    public static string? Refuse(string folder, string home, string library)
    {
        string full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
        if (Path.GetPathRoot(full) is { } root && Path.TrimEndingDirectorySeparator(root).Equals(full, SubmitMatch.Case)) return WholeDisk;
        if (full.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(home)), SubmitMatch.Case)) return WholeHome;
        return SubmitMatch.Within(full, Path.TrimEndingDirectorySeparator(Path.GetFullPath(library))) ? Library : null;
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
