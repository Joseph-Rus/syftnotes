using System.Diagnostics;
using System.Text.RegularExpressions;
using StudyStash.Core;

namespace StudyStash.App.Platform;

/// <summary>
/// Another running process, as Syft Notes tells whether it's one of its own copies: the file it really runs (where that
/// is now: proc_pidpath on a Mac, through Process.MainModule; never its arguments, which keep the path it started from),
/// whether that's ours (inside a bundle that says com.study-stash.app, <see cref="MacRename.Ours"/>, or StudyStash.exe),
/// its version (the bundle's, <see cref="MacRename.VersionOf"/>, or the file's), and the settings folder it serves (its
/// --home; the usual one without). Version and Home are null when they can't be read. Anything that decides to stop
/// another copy reads it here.
/// </summary>
public sealed partial record OurProcess(int Pid, string Program, bool Ours, int[]? Version, string? Home)
{
    /// <summary>The process <paramref name="pid"/>, or null when it can't be told (it has gone, or its file can't be
    /// read): such a process is never touched.</summary>
    public static OurProcess? Of(int pid)
    {
        string? program;
        try
        {
            using var process = Process.GetProcessById(pid);
            program = process.MainModule?.FileName;
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
        {
            return null;
        }
        if (string.IsNullOrEmpty(program)) return null;
        int[]? version;
        if (OperatingSystem.IsMacOS())
        {
            if (Apps.EnclosingMacBundle(Path.GetDirectoryName(program) ?? program) is not { } bundle || !MacRename.Ours(bundle))
                return new OurProcess(pid, program, false, null, null);
            version = MacRename.VersionOf(bundle);
        }
        else if (OperatingSystem.IsWindows() && string.Equals(Path.GetFileName(program), "StudyStash.exe", StringComparison.OrdinalIgnoreCase))
        {
            version = FileVersion(program);
        }
        else
        {
            return new OurProcess(pid, program, false, null, null);
        }
        return new OurProcess(pid, program, true, version is [0] ? null : version,
            CommandLine(pid) is { Length: > 0 } line ? HomeIn(line, Configs.DefaultHome) : null);
    }

    /// <summary>This one is strictly older than <paramref name="version"/>: false when its version can't be read.</summary>
    public bool OlderThan(string version) => Version is { } v && Updates.Compare(v, Updates.ParseVersion(version)) < 0;

    /// <summary>It serves the settings folder <paramref name="home"/>: false when that can't be read.</summary>
    public bool Serves(string home) => Home is { } h && SameFolder(h, home);

    static int[]? FileVersion(string program)
    {
        try
        {
            return FileVersionInfo.GetVersionInfo(program).ProductVersion is { Length: > 0 } v ? Updates.ParseVersion(v) : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Its whole command line: ps on a Mac, the process's own record on Windows.</summary>
    static string? CommandLine(int pid)
    {
        var r = OperatingSystem.IsWindows()
            ? Machine.Run("powershell.exe", ["-NoProfile", "-Command", $"(Get-CimInstance Win32_Process -Filter 'ProcessId={pid}').CommandLine"], TimeSpan.FromSeconds(20))
            : Machine.Run("/bin/ps", ["-ww", "-o", "command=", "-p", pid.ToString(System.Globalization.CultureInfo.InvariantCulture)], TimeSpan.FromSeconds(20));
        return r is { ExitCode: 0 } ? r.Stdout.Trim() : null;
    }

    /// <summary>The folder after --home in a command line (quoted, or up to the command after it: serve, helper, or the
    /// next switch), or <paramref name="usual"/> when there's none.</summary>
    internal static string HomeIn(string commandLine, string usual) =>
        HomeSwitch().Match(commandLine) is { Success: true } m ? (m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value) : usual;

    [GeneratedRegex(@"(?:^|\s)--home\s+(?:""([^""]+)""|(.+?))(?=\s+(?:serve|helper|--\S+)(?:\s|$)|\s*$)")]
    private static partial Regex HomeSwitch();

    internal static bool SameFolder(string a, string b) =>
        string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(a)), Path.TrimEndingDirectorySeparator(Path.GetFullPath(b)),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
}
