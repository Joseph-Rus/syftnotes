namespace StudyStash.Core;

/// <summary>Where a Mac copy should go, and the disk image it ran from (null when it didn't run from one).</summary>
public sealed record MovePlan(string Source, string Dest, string? Volume);

/// <summary>
/// "Move Syft Notes to Applications" (the LetsMove pattern): a copy opened straight from its DMG, from Gatekeeper's
/// translocation folder (a download opened from Downloads) or from Downloads or the Desktop offers once to move
/// itself into /Applications (~/Applications when that isn't writable), ejects the DMG it came from, and opens the
/// copy there. A build folder, an app somewhere the student put it on purpose, and the self-test never see it.
/// </summary>
public static class MoveToApplications
{
    public const string DeclinedFile = "move-to-applications-declined";

    /// <summary>What to offer for the program that runs from <paramref name="baseDir"/>, or null for nothing.</summary>
    public static MovePlan? Plan(string baseDir, string userHome, bool applicationsWritable, string volumes = "/Volumes")
    {
        if (Apps.EnclosingMacBundle(baseDir) is not { } app) return null;
        if (Apps.PlistString(Path.Combine(app, "Contents", "Info.plist"), "CFBundleIdentifier") != "com.study-stash.app") return null;
        string path = app.Replace('\\', '/');
        string? volume = null;
        string? parent = Path.GetDirectoryName(app)?.Replace('\\', '/').TrimEnd('/');
        string vols = volumes.TrimEnd('/') + "/";
        if (path.Contains("/AppTranslocation/", StringComparison.Ordinal)) { }
        else if (path.StartsWith(vols, StringComparison.Ordinal))
            volume = vols + path[vols.Length..].Split('/')[0];
        else if (parent is not null && (parent == Join(userHome, "Downloads") || parent == Join(userHome, "Desktop"))) { }
        else return null;
        string apps = applicationsWritable ? "/Applications" : Path.Combine(userHome, "Applications");
        return new MovePlan(app, Path.Combine(apps, Path.GetFileName(app)), volume);
    }

    static string Join(string a, string b) => a.Replace('\\', '/').TrimEnd('/') + "/" + b;

    /// <summary>The whole thing for this run: nothing if the student said "Not now" before (or there's nothing to
    /// offer), else asks once. True when the copy is moved and will open from its new place: the caller quits now.
    /// <paramref name="ask"/> shows the one sheet (true for Move). <paramref name="spawn"/> starts the little
    /// waiter that, once this process is gone, ejects the disk image and opens the new copy.</summary>
    public static bool Offer(string baseDir, string home, string userHome, Func<bool> ask, Runner run,
        Action<IReadOnlyList<string>> spawn, int pid, Action<string> log, bool? applicationsWritable = null,
        Func<bool>? otherCopyRunning = null)
    {
        string declined = Path.Combine(home, DeclinedFile);
        if (File.Exists(declined)) return false;
        bool writable = applicationsWritable ?? Machine.Writable("/Applications");
        if (Plan(baseDir, userHome, writable) is not { } plan) return false;
        if (Path.GetFullPath(plan.Source) == Path.GetFullPath(plan.Dest)) return false;
        // Another copy of the app running (an older one in Applications): swapping the bundle under it and opening the
        // new one would only hand off to that old copy, so nothing is offered until it's quit.
        if ((otherCopyRunning ?? (() => OtherCopyRunning(run, pid)))()) return false;
        if (!ask())
        {
            try { File.WriteAllText(declined, ""); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            return false;
        }
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(plan.Dest)!);
            string fresh = plan.Dest + ".new";
            Apps.TryDeleteFolder(fresh);
            var copy = run("ditto", [plan.Source, fresh], TimeSpan.FromMinutes(10));
            if (copy is not { ExitCode: 0 })
            {
                Apps.TryDeleteFolder(fresh);
                log("[move] couldn't copy the app into " + Path.GetDirectoryName(plan.Dest) + "; it stays where it is");
                return false;
            }
            if (Directory.Exists(plan.Dest)) Apps.SwapMacBundle(plan.Dest, fresh, run);
            else Ready.MoveFolder(fresh, plan.Dest, run);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            log("[move] couldn't move the app: " + e.Message);
            return false;
        }
        log("[move] moved to " + plan.Dest);
        string? eject = plan.Volume is { } v && IsDiskImage(v, run) ? v : null;
        spawn(WaiterCommand(pid, plan.Dest, eject));
        return true;
    }

    /// <summary>Any other process running a program from a Study Stash.app bundle (the app, or the library it runs).</summary>
    internal static bool OtherCopyRunning(Runner run, int pid) =>
        run("/usr/bin/pgrep", ["-f", "Study Stash.app/Contents/MacOS/"], TimeSpan.FromSeconds(10)) is { ExitCode: 0 } p
        && p.Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Any(l => int.TryParse(l.Trim(), out int other) && other != pid);

    /// <summary>`hdiutil info` lists only disk images, so a volume it names is one this download made, never a
    /// USB stick or another disk.</summary>
    static bool IsDiskImage(string volume, Runner run) =>
        run("/usr/bin/hdiutil", ["info"], TimeSpan.FromSeconds(20)) is { ExitCode: 0 } p
        && p.Stdout.Split('\n').Any(l => l.TrimEnd().EndsWith(volume, StringComparison.Ordinal));

    /// <summary>Waits for <paramref name="pid"/> to quit (caffeinate -w blocks until it exits: no polling), ejects <paramref name="volume"/> (when not empty), opens the app.</summary>
    internal static IReadOnlyList<string> WaiterCommand(int pid, string dest, string? volume) =>
    [
        "/bin/sh", "-c",
        "/usr/bin/caffeinate -w \"$1\"; [ -z \"$3\" ] || /usr/bin/hdiutil detach \"$3\" -quiet; /usr/bin/open \"$2\"",
        "sh", pid.ToString(System.Globalization.CultureInfo.InvariantCulture), dest, volume ?? "",
    ];
}
