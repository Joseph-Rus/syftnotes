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
