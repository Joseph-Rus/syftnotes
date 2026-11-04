namespace StudyStash.Core;

/// <summary>
/// Linux, for the AppImage (docs/LIBRARIES.md, phase 5): an AppImage is the program itself, and nothing puts it in the
/// applications menu, so the app does, every time it starts from one (the AppImage may have been moved, or updated in
/// place): Syft Notes in the menu with its icon, and <c>syft-notes</c> on the PATH (~/.local/bin), as linux/install.sh
/// did for the tarball. <see cref="Remove"/> takes them off again (<c>syft-notes --uninstall</c>).
/// </summary>
public sealed class LinuxMenu
{
    public string DataHome { get; init; } = Environment.GetEnvironmentVariable("XDG_DATA_HOME") is { Length: > 0 } d
        ? d : Path.Combine(Py.UserHome(), ".local", "share");
    public string BinDir { get; init; } = Path.Combine(Py.UserHome(), ".local", "bin");

    public string EntryPath => Path.Combine(DataHome, "applications", "syft-notes.desktop");
    public string IconPath => Path.Combine(DataHome, "icons", "hicolor", "256x256", "apps", "syft-notes.png");
    public string LinkPath => Path.Combine(BinDir, "syft-notes");

    /// <summary>The menu entry for <paramref name="appImage"/> (Exec quoted as the Desktop Entry spec says).</summary>
    public static string Entry(string appImage) => $"""
        [Desktop Entry]
        Type=Application
        Name=Syft Notes
        GenericName=Lecture recorder
        Comment=Record your lectures. Get the notes.
        Exec="{appImage.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("`", "\\`").Replace("$", "\\$")}" %u
        Icon=syft-notes
        Terminal=false
        Categories=Education;Office;AudioVideo;Audio;Recorder;
        Keywords=lecture;notes;record;transcribe;whisper;study;canvas;
        StartupWMClass=StudyStash
        SingleMainWindow=true

        """;

    /// <summary>Writes what differs from what it should be: the entry, the icon (from <paramref name="icon"/>, the one
    /// inside the AppImage) and the PATH link. True when something changed.</summary>
    public bool Write(string appImage, string? icon, Action<string>? log = null)
    {
        bool changed = false;
        string entry = Entry(appImage);
        if (Read(EntryPath) != entry)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(EntryPath)!);
            File.WriteAllText(EntryPath, entry);
            changed = true;
        }
        if (icon is not null && File.Exists(icon) && (!File.Exists(IconPath) || new FileInfo(IconPath).Length != new FileInfo(icon).Length))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(IconPath)!);
            File.Copy(icon, IconPath, overwrite: true);
            changed = true;
        }
        var link = new FileInfo(LinkPath);
        if (link.LinkTarget != appImage)
        {
            Directory.CreateDirectory(BinDir);
            if (link.Exists || link.LinkTarget is not null) File.Delete(LinkPath);
            File.CreateSymbolicLink(LinkPath, appImage);
            changed = true;
        }
        if (changed) log?.Invoke($"[menu] Syft Notes is in the applications menu and on the PATH, as {appImage}");
        return changed;
    }

    /// <summary>Takes the entry, the icon and the PATH link off (only a link that points at an AppImage of ours).</summary>
    public void Remove()
    {
        Delete(EntryPath);
        Delete(IconPath);
        if (new FileInfo(LinkPath).LinkTarget is { } target && target.EndsWith(".AppImage", StringComparison.Ordinal)) Delete(LinkPath);
    }

    static string? Read(string path)
    {
        try
        {
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    static void Delete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }
}
