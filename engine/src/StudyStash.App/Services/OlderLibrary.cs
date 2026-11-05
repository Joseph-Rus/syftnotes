using StudyStash.App.Platform;
using StudyStash.Core;

namespace StudyStash.App.Services;

/// <summary>What's done about one of our processes holding something this copy wants (the library's port).</summary>
public enum OlderVerdict
{
    /// <summary>Not a Syft Notes program, or one that can't be told: never touched.</summary>
    NotOurs,
    /// <summary>Ours, for another settings folder, or one whose folder can't be read: never touched.</summary>
    OtherHome,
    /// <summary>Ours, as new as this copy or newer (or its version can't be read): never touched.</summary>
    NotOlder,
    /// <summary>This copy runs from its disk image or a translocation folder (<see cref="MacRename.Passing"/>): it never
    /// touches what the installed copy runs.</summary>
    Passing,
    /// <summary>Ours, but a lecture is being recorded or written down in its folder: left alone, looked at again later.</summary>
    Busy,
    /// <summary>This copy's own program (its background library), for this folder, with nothing on the go.</summary>
    Own,
    /// <summary>An older copy, for this folder, with nothing on the go: asked to quit, and this copy takes its place.</summary>
    Replace,
}

/// <summary>
/// An older copy of Syft Notes still holding the library's port and turning this app away: a Mac's old background
/// library (a LaunchAgent running an old Study Stash.app), an old copy's library left running. Who it is comes from
/// <see cref="OurProcess"/>; what's done from <see cref="Judge"/>: only a copy of ours, strictly older than this one, for
/// this settings folder, with no lecture on the go (<see cref="LecturesBusy"/>) is replaced, and only politely: launchd
/// lets go of its job, then SIGTERM, which every copy takes as a clean stop. Never a forced end: the library's note files
/// are written in place, not swapped in whole, so a process ended mid-write could leave half a note. Windows has no
/// polite way to ask a library this app didn't start, so there it's said instead. Everything that touches the computer
/// is swapped in a test, and does nothing while <see cref="Desktop.SystemChangesOff"/> (tests, the self-test).
/// </summary>
public sealed class OlderLibrary
{
    /// <summary>A lecture's record changed this recently: it's being written down right now (each piece saves it).</summary>
    static readonly TimeSpan WritingDownNow = TimeSpan.FromMinutes(5);

    public static readonly OlderLibrary ThisComputer = new();

    /// <summary>Who listens on the port: null when no one process can be told.</summary>
    public Func<int, OurProcess?> Holder { get; init; } = HolderOf;
    /// <summary>A lecture in this settings folder is being recorded or written down.</summary>
    public Func<string, bool> Busy { get; init; } = home => LecturesBusy(home, DateTime.UtcNow);
    /// <summary>(home, this app's program): a Mac's background library for this folder that doesn't run
    /// <c>program</c> (null: whatever it runs) lets go (launchctl bootout, even with no plist left), so launchd doesn't
    /// start it again once it quits.</summary>
    public Action<string, string?> StopRegistration { get; init; } = StopRegistered;
    /// <summary>Asks the process to quit (SIGTERM); false where there's no polite way (Windows).</summary>
    public Func<int, bool> AskToQuit { get; init; } = Quit;

    /// <summary>The processes already asked: a second SIGTERM means "stop now", with no clean-up, so it's never sent.</summary>
    readonly HashSet<int> asked = [];
    readonly Lock gate = new();

    /// <summary>Asks <paramref name="pid"/> to quit, once: true when it has been asked (<paramref name="before"/>: on an
    /// earlier look, so nothing is sent now), false where there's no polite way.</summary>
    public bool AskOnce(int pid, out bool before)
    {
        lock (gate) before = asked.Contains(pid);
        if (before) return true;
        if (!AskToQuit(pid)) return false;
        lock (gate) asked.Add(pid);
        return true;
    }

    /// <summary>The words for a library left alone because a lecture is on the go there.</summary>
    public const string BusyWords = "An older copy of Syft Notes is still busy with a lecture (recording it or writing it down). Your library starts by itself once it's done.";
    /// <summary>The words for one asked to quit that hasn't yet.</summary>
    public const string ClosingWords = "An older copy of Syft Notes is still closing. Your library starts by itself once it has.";
    /// <summary>The words for one this app can't ask to quit (Windows).</summary>
    public const string StillRunningWords = "An older copy of Syft Notes is still running. End it in Task Manager, then open Syft Notes again.";
    /// <summary>The words for a library of ours that didn't answer in time (busy with a long job, a class renamed or the
    /// notes indexed again): never taken for a refusal.</summary>
    public const string SlowWords = "Your library is busy and didn't answer just now. Syft Notes tries again in a minute.";
    /// <summary>The words for a copy as new as this one or newer, or one set up apart from this one (or this copy runs
    /// from its disk image): never touched.</summary>
    public const string OtherCopyWords = "Another copy of Syft Notes is already running its own library. Quit that copy, then open Syft Notes again.";

    /// <summary>What to do about <paramref name="holder"/>, for this copy running <paramref name="self"/> at
    /// <paramref name="version"/> for <paramref name="home"/>. <paramref name="busy"/> is asked only about one of ours
    /// for this folder, and is asked of this folder's lectures. <paramref name="installing"/>: the app itself asks, about
    /// the copy holding its folder, right before it offers the move into Applications (Desktop.TakeOver), so running from
    /// its disk image doesn't hold it off there; it never registers anything from the image (MakeBackgroundPlan).</summary>
    public static OlderVerdict Judge(OurProcess? holder, string self, string version, string home, Func<string, bool> busy,
        bool installing = false)
    {
        if (holder is not { Ours: true }) return OlderVerdict.NotOurs;
        if (!holder.Serves(home)) return OlderVerdict.OtherHome;
        if (!installing && MacRename.Passing(self)) return OlderVerdict.Passing;
        bool own = LibraryService.SameProgram(holder.Program, self);
        if (!own && !holder.OlderThan(version)) return OlderVerdict.NotOlder;
        if (busy(home)) return OlderVerdict.Busy;
        return own ? OlderVerdict.Own : OlderVerdict.Replace;
    }

    /// <summary>A lecture is recording or paused; one is being written down right now (its record changed in the last
    /// few minutes: one left Transcribing with no model to write it never holds this off); or one is on its way to the
    /// library or having its notes written there (<see cref="LectureStore.OnItsWay"/>, as an update waits for it). True
    /// when the lectures can't be read, as it can't be told.</summary>
    public static bool LecturesBusy(string home, DateTime now)
    {
        try
        {
            var store = new LectureStore(home);
            var all = store.All();
            bool writingDown = all.Any(l => l.State == LectureState.Transcribing && now - store.ChangedAt(l.Id) < WritingDownNow);
            return !AppUpdates.NoLectureInHand(all, writingDown) || store.OnItsWay(now - AppUpdates.WritingWait);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return true;
        }
    }

    /// <summary>A Mac: lsof's one listening process; Windows: netstat's.</summary>
    static OurProcess? HolderOf(int port)
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
        return pids is [int pid] ? OurProcess.Of(pid) : null;
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

    static void StopRegistered(string home, string? program)
    {
        if (Desktop.SystemChangesOff || !OperatingSystem.IsMacOS()) return;
        var background = BackgroundLibrary.ThisComputer();
        if (program is not null && background.Registered(home) is [var registered, ..] && LibraryService.SameProgram(registered, program)) return;
        background.Stop(home);
    }

    static bool Quit(int pid)
    {
        if (Desktop.SystemChangesOff || OperatingSystem.IsWindows()) return false;
        Native.Kill(pid, Native.SigTerm);
        return true;
    }
}
