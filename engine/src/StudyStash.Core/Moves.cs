namespace StudyStash.Core;

/// <summary>
/// The app's program moved (docs/LIBRARIES.md, phase 5): Windows from Inno Setup's folder to Velopack's, the Mac bundle
/// from "Study Stash.app" to "Syft Notes.app", Linux from a tarball's folder to an AppImage. What other programs keep
/// of its path (an AI app's connection, a login item) is put right on the first start from the new place, and only
/// where it names a program of ours that is gone, or one of the old places: another copy someone runs on purpose (a
/// build folder) is left alone.
/// </summary>
public static class Moves
{
    /// <summary>Where installed copies lived before Velopack, as a piece of their program's path.</summary>
    static readonly string[] OldPlaces =
    [
        @"\Programs\Study Stash\", // Windows: Inno Setup's {localappdata}\Programs\Study Stash
        "/Study Stash.app/",       // a Mac bundle before the rename
        "/.local/share/syft-notes/", // Linux: install.sh's folder for the tarball
    ];

    static bool NamedLikeOurs(string path) =>
        Path.GetFileName(path.Replace('\\', '/').TrimEnd('/')) is { } name
        && (name.Equals("StudyStash", StringComparison.OrdinalIgnoreCase) || name.Equals("StudyStash.exe", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("SyftNotes", StringComparison.Ordinal) && name.EndsWith(".AppImage", StringComparison.Ordinal));

    /// <summary>True when <paramref name="command"/> is this app's program as it was before it moved, so whatever names
    /// it should name <paramref name="current"/> instead: it's our program by name, it isn't the current one, and it is
    /// gone or in one of the old places. <paramref name="exists"/> is File.Exists unless a test says otherwise.</summary>
    public static bool OldProgram(string? command, string current, Func<string, bool>? exists = null)
    {
        if (string.IsNullOrWhiteSpace(command) || !NamedLikeOurs(command)) return false;
        var same = OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        if (string.Equals(command, current, same)) return false;
        string slashes = command.Replace('\\', '/');
        bool oldPlace = OldPlaces.Any(p => command.Contains(p, StringComparison.OrdinalIgnoreCase) || slashes.Contains(p, StringComparison.OrdinalIgnoreCase));
        return oldPlace || !(exists ?? File.Exists)(command);
    }

    /// <summary>The files that make a folder an Inno Setup install of this app, deleted last so a folder only partly
    /// deleted is still known as one the next time.</summary>
    static readonly string[] InnoMarks = ["unins000.exe", "StudyStash.exe", "unins000.dat"];

    /// <summary>True when <paramref name="folder"/> (an Inno Setup entry's InstallLocation) is the old Windows install
    /// of this app, safe to delete: Inno Setup's own place for it (a "Study Stash" folder in "Programs", setup.iss), not
    /// a drive's root, not inside the running copy's folder <paramref name="running"/> or holding it, and either gone
    /// already or holding the app's program and Inno's uninstall data.</summary>
    public static bool OldInstallFolder(string? folder, string running)
    {
        if (string.IsNullOrWhiteSpace(folder) || !Path.IsPathFullyQualified(folder)) return false;
        string full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
        if (Path.GetDirectoryName(full) is not { Length: > 0 } parent) return false; // a drive's root
        if (!Path.GetFileName(full).Equals("Study Stash", StringComparison.OrdinalIgnoreCase)
            || !Path.GetFileName(parent).Equals("Programs", StringComparison.OrdinalIgnoreCase)) return false;
        string mine = Path.TrimEndingDirectorySeparator(Path.GetFullPath(running)) + Path.DirectorySeparatorChar;
        string theirs = full + Path.DirectorySeparatorChar;
        if (mine.StartsWith(theirs, StringComparison.OrdinalIgnoreCase) || theirs.StartsWith(mine, StringComparison.OrdinalIgnoreCase)) return false;
        return !Directory.Exists(full)
            || File.Exists(Path.Combine(full, "StudyStash.exe")) && File.Exists(Path.Combine(full, "unins000.dat"));
    }

    /// <summary>Deletes the old install's folder (<see cref="OldInstallFolder"/> says it is one), without its
    /// uninstaller: null once it's gone, or the first file that couldn't go. Nothing is touched while its program or
    /// uninstaller is running (neither is ever stopped); a file that's held open is skipped, and Inno's own files go
    /// last, one at a time, so a folder only partly deleted is found and tried again on the next start. A link inside
    /// it (a junction, a symbolic link) goes as a link: what it points at is never touched, and a folder that is a link
    /// itself is left alone (<see cref="IsLink"/>).</summary>
    public static string? DeleteOldInstall(string folder)
    {
        if (!Directory.Exists(folder)) return null;
        if (IsLink(folder)) return folder;
        string[] marks = InnoMarks.Select(m => Path.Combine(folder, m)).ToArray();
        if (marks.FirstOrDefault(InUse) is { } running) return running;
        // Never through a link: its target isn't the install.
        var walk = new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint };
        var here = new EnumerationOptions { AttributesToSkip = 0 };
        string? held = null;
        string[] dirs = [folder, .. Directory.EnumerateDirectories(folder, "*", walk)];
        foreach (string link in dirs.SelectMany(d => Directory.EnumerateFileSystemEntries(d, "*", here)).Where(IsLink).ToList())
            if (!TryUnlink(link)) held ??= link;
        foreach (string file in Directory.EnumerateFiles(folder, "*", walk).ToList())
            if (!marks.Contains(file, StringComparer.OrdinalIgnoreCase) && !TryDelete(file)) held ??= file;
        if (held is not null) return held;
        foreach (string mark in marks)
            if (File.Exists(mark) && !TryDelete(mark)) return mark;
        try
        {
            Directory.Delete(folder, recursive: true); // only empty folders are left
            return null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return folder;
        }
    }

    /// <summary>Whether <paramref name="path"/> is a link (a junction or a symbolic link) rather than a real file or
    /// folder; one that can't be read counts as a link, so it's left alone.</summary>
    public static bool IsLink(string path)
    {
        try
        {
            return File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return true;
        }
    }

    static bool TryDelete(string file)
    {
        try
        {
            File.SetAttributes(file, FileAttributes.Normal);
            File.Delete(file);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>Removes a link itself, never what it points at (a folder's link is removed as an empty folder).</summary>
    static bool TryUnlink(string link)
    {
        try
        {
            if (new FileInfo(link).Attributes.HasFlag(FileAttributes.Directory)) Directory.Delete(link, recursive: false);
            else File.Delete(link);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>Whether a program is running from <paramref name="file"/>: Windows lets a running program be read,
    /// even with no sharing, but never written.</summary>
    static bool InUse(string file)
    {
        if (!File.Exists(file)) return false;
        try
        {
            using var _ = new FileStream(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            return false;
        }
        catch (IOException)
        {
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return false; // read-only, or not this account's to write: the delete finds out
        }
    }

    /// <summary>The program a Windows Run value starts: its first word, or the quoted path it starts with.</summary>
    public static string? RunProgram(string? command)
    {
        string c = (command ?? "").Trim();
        if (c.Length == 0) return null;
        if (c[0] == '"') return c.IndexOf('"', 1) is int end and > 0 ? c[1..end] : null;
        int space = c.IndexOf(' ');
        return space < 0 ? c : c[..space];
    }

    /// <summary>The program a LaunchAgent starts: the first string of its ProgramArguments.</summary>
    public static string? PlistProgram(string? plist)
    {
        string text = plist ?? "";
        int at = text.IndexOf("<key>ProgramArguments</key>", StringComparison.Ordinal);
        if (at < 0) return null;
        int open = text.IndexOf("<string>", at, StringComparison.Ordinal);
        int close = open < 0 ? -1 : text.IndexOf("</string>", open, StringComparison.Ordinal);
        return close < 0 ? null : System.Net.WebUtility.HtmlDecode(text[(open + "<string>".Length)..close]);
    }

    /// <summary>The program a desktop entry's Exec= line starts (quoted as the Desktop Entry spec says, or not).</summary>
    public static string? ExecProgram(string? desktopFile)
    {
        foreach (string raw in (desktopFile ?? "").Split('\n'))
        {
            string line = raw.Trim();
            if (!line.StartsWith("Exec=", StringComparison.Ordinal)) continue;
            string exec = line["Exec=".Length..].Trim();
            if (exec.Length == 0) return null;
            if (exec[0] != '"') return exec.Split(' ')[0];
            var word = new System.Text.StringBuilder();
            for (int i = 1; i < exec.Length; i++)
            {
                if (exec[i] == '\\' && i + 1 < exec.Length) word.Append(exec[++i]);
                else if (exec[i] == '"') return word.ToString();
                else word.Append(exec[i]);
            }
            return null;
        }
        return null;
    }
}
