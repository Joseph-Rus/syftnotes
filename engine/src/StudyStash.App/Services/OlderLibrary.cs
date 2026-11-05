using System.Diagnostics;
using StudyStash.App.Platform;
using StudyStash.Core;

namespace StudyStash.App.Services;

/// <summary>The process listening on a port, and the file it really runs: null when that can't be read.</summary>
public sealed record PortHolder(int Pid, string? Program);

/// <summary>What's done about a library on the port that turns this app away.</summary>
public enum OlderVerdict
{
    /// <summary>Not a Syft Notes program, or one that can't be told: left alone, as before.</summary>
    NotOurs,
    /// <summary>This app's own program (its background library): left alone, as before.</summary>
    Self,
    /// <summary>An older copy, but a lecture is being recorded or written down: left alone, looked at again later.</summary>
    Busy,
    /// <summary>An older copy with nothing on the go: asked to quit, and this app's library starts in its place.</summary>
    Replace,
}

/// <summary>
/// An older copy of Syft Notes still holding the library's port, with another password or not answering: a Mac's old
/// background library (a LaunchAgent running an old Study Stash.app), an old copy's library left running, a copy run
/// from Downloads. It's only ever stopped when its real program (never its arguments, which keep the path it started
/// from) is in a bundle that says com.study-stash.app, or is StudyStash.exe, and isn't this app's own program; never
/// while a lecture is being recorded or written down (<see cref="LecturesBusy"/>); and only politely: launchd lets go of
/// its job, then SIGTERM, which every copy takes as a clean stop. Never a forced end: the library's note files are
/// written in place, not swapped in whole, so a process ended mid-write could leave half a note. Windows has no polite
/// way to ask a library it didn't start, so there it's said instead. Everything that touches the computer is swapped in
/// a test, and does nothing while <see cref="Desktop.SystemChangesOff"/> (tests, the self-test).
/// </summary>
public sealed class OlderLibrary
{
    /// <summary>A lecture's record changed this recently: it's being written down right now (each piece saves it).</summary>
    static readonly TimeSpan WritingDownNow = TimeSpan.FromMinutes(5);

    public static readonly OlderLibrary ThisComputer = new();

    /// <summary>Who listens on the port.</summary>
    public Func<int, PortHolder?> Holder { get; init; } = HolderOf;
    /// <summary>The program is a Syft Notes one.</summary>
    public Func<string, bool> Ours { get; init; } = IsOurs;
    /// <summary>A lecture in this settings folder is being recorded or written down.</summary>
    public Func<string, bool> Busy { get; init; } = home => LecturesBusy(home, DateTime.UtcNow);
    /// <summary>(home, this app's program): a Mac's background library for this folder that doesn't run this program
    /// lets go (launchctl bootout), so launchd doesn't start the old one again once it quits.</summary>
    public Action<string, string> StopOldRegistration { get; init; } = StopRegistration;
    /// <summary>Asks the process to quit (SIGTERM); false where there's no polite way (Windows).</summary>
    public Func<int, bool> AskToQuit { get; init; } = Quit;

    /// <summary>The words for a library left alone because a lecture is on the go there.</summary>
    public const string BusyWords = "An older copy of Syft Notes is still busy with a lecture (recording it or writing it down). Your library starts by itself once it's done.";
    /// <summary>The words for one asked to quit that hasn't yet.</summary>
    public const string ClosingWords = "An older copy of Syft Notes is still closing. Your library starts by itself once it has.";
    /// <summary>The words for one this app can't ask to quit (Windows).</summary>
    public const string StillRunningWords = "An older copy of Syft Notes is still running. End it in Task Manager, then open Syft Notes again.";

    /// <summary>What to do about the program <paramref name="program"/> holding the port, for this app running
    /// <paramref name="self"/>. <paramref name="busy"/> is only asked about an older copy of ours.</summary>
    public static OlderVerdict Judge(string? program, string self, Func<string, bool> ours, Func<bool> busy)
    {
        if (program is null) return OlderVerdict.NotOurs;
        if (LibraryService.SameProgram(program, self)) return OlderVerdict.Self;
        if (!ours(program)) return OlderVerdict.NotOurs;
        return busy() ? OlderVerdict.Busy : OlderVerdict.Replace;
    }

    /// <summary>A lecture is recording or paused; one is being written down right now (its record changed in the last
    /// few minutes: one left Transcribing with no model to write it never holds this off); or one the library has is
    /// having its notes written, sent within <see cref="AppUpdates.WritingWait"/>. One still waiting to be sent doesn't
    /// count: it's here, and goes to the new library. True when the lectures can't be read, as it can't be told.</summary>
    public static bool LecturesBusy(string home, DateTime now)
    {
        try
        {
            var store = new LectureStore(home);
            var all = store.All();
            bool writingDown = all.Any(l => l.State == LectureState.Transcribing && now - store.ChangedAt(l.Id) < WritingDownNow);
            return !AppUpdates.NoLectureInHand(all, writingDown)
                   || all.Any(l => l.State == LectureState.Writing && File.Exists(store.AudioPath(l.Id))
                                   && now - File.GetLastWriteTimeUtc(store.AudioPath(l.Id)) < AppUpdates.WritingWait);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return true;
        }
    }

    /// <summary>A Mac: lsof's one listening process; Windows: netstat's. Its program is where its file really is now
    /// (proc_pidpath on a Mac, through Process.MainModule), never what it was started as.</summary>
    static PortHolder? HolderOf(int port)
    {
        if (Desktop.SystemChangesOff) return null;
        List<int> pids;
        if (OperatingSystem.IsMacOS())
        {
            if (Machine.Run("/usr/sbin/lsof", ["-nP", $"-iTCP:{port}", "-sTCP:LISTEN", "-t"], TimeSpan.FromSeconds(20)) is not { ExitCode: 0 } r) return null;
            pids = [.. r.Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(s => int.TryParse(s.Trim(), out int p) ? p : 0)];
        }
        else if (OperatingSystem.IsWindows())
        {
            if (Machine.Run("netstat", ["-ano", "-p", "TCP"], TimeSpan.FromSeconds(20)) is not { ExitCode: 0 } r) return null;
            pids = ListeningPids(r.Stdout, port);
        }
        else
        {
            return null;
        }
        pids = [.. pids.Where(p => p > 0 && p != Environment.ProcessId).Distinct()];
        if (pids is not [int pid]) return null;
        try
        {
            using var process = Process.GetProcessById(pid);
            return new PortHolder(pid, process.MainModule?.FileName);
        }
        catch (ArgumentException)
        {
            return null; // it has just quit
        }
        catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
        {
            return new PortHolder(pid, null); // its file can't be read: never taken for ours
        }
    }

    /// <summary>The processes netstat -ano says listen on <paramref name="port"/>: a local address ending :port with no
    /// one at the other end (*:0), so the line is read the same in every language Windows speaks.</summary>
    internal static List<int> ListeningPids(string netstat, int port)
    {
        var pids = new List<int>();
        foreach (string line in netstat.Split('\n'))
        {
            var w = line.Split((char[])[' ', '\t', '\r'], StringSplitOptions.RemoveEmptyEntries);
            if (w.Length >= 5 && w[0] == "TCP" && w[1].EndsWith($":{port}", StringComparison.Ordinal) && w[2].EndsWith(":0", StringComparison.Ordinal)
                && int.TryParse(w[^1], out int pid))
                pids.Add(pid);
        }
        return pids;
    }

    /// <summary>A Mac: the program is inside a bundle that says com.study-stash.app (an old Study Stash.app too).
    /// Windows: it's StudyStash.exe.</summary>
    static bool IsOurs(string program)
    {
        if (OperatingSystem.IsMacOS())
            return Apps.EnclosingMacBundle(Path.GetDirectoryName(program) ?? program) is { } bundle
                   && Apps.PlistString(Path.Combine(bundle, "Contents", "Info.plist"), "CFBundleIdentifier") == "com.study-stash.app";
        return OperatingSystem.IsWindows() && string.Equals(Path.GetFileName(program), "StudyStash.exe", StringComparison.OrdinalIgnoreCase);
    }

    static void StopRegistration(string home, string program)
    {
        if (Desktop.SystemChangesOff || !OperatingSystem.IsMacOS()) return;
        var background = BackgroundLibrary.ThisComputer();
        if (background.Registered(home) is [var registered, ..] && LibraryService.SameProgram(registered, program)) return;
        // Even with no plist on disk: launchd keeps a job loaded after its plist was deleted.
        background.Stop(home);
    }

    static bool Quit(int pid)
    {
        if (Desktop.SystemChangesOff || OperatingSystem.IsWindows()) return false;
        Native.Kill(pid, Native.SigTerm);
        return true;
    }
}
