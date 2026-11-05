namespace StudyStash.Core;

/// <summary>Where a Mac copy should go, and the disk image it ran from (null when it didn't run from one).</summary>
public sealed record MovePlan(string Source, string Dest, string? Volume);

/// <summary>
/// "Move Syft Notes to Applications" (the LetsMove pattern): a copy opened straight from its DMG, from Gatekeeper's
/// translocation folder (a download opened from Downloads) or from Downloads or the Desktop offers once to move
/// itself into /Applications (~/Applications when that isn't writable), ejects the DMG it came from, and opens the
/// copy there. One already there that's as new or newer opens instead. A build folder, an app somewhere the student
/// put it on purpose, and the self-test never see it.
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
        // Always under the app's own name, whatever the copy was called (an old download's "Study Stash.app").
        return new MovePlan(app, Path.Combine(apps, MacRename.Name), volume);
    }

    static string Join(string a, string b) => a.Replace('\\', '/').TrimEnd('/') + "/" + b;

    /// <summary>The whole thing for this run: nothing if the student said "Not now" before (or there's nothing to
    /// offer), else asks once. True when the copy is moved and will open from its new place, or the copy already in
    /// Applications is as new or newer and opens instead: the caller quits now. <paramref name="ask"/> shows the one
    /// sheet (true for Move). <paramref name="spawn"/> starts the little waiter that, once this process is gone, ejects
    /// the disk image and opens the copy in Applications.</summary>
    public static bool Offer(string baseDir, string home, string userHome, Func<bool> ask, Runner run,
        Action<IReadOnlyList<string>> spawn, int pid, Action<string> log, bool? applicationsWritable = null,
        Func<bool>? otherCopyRunning = null)
    {
        bool writable = applicationsWritable ?? Machine.Writable("/Applications");
        if (Plan(baseDir, userHome, writable) is not { } plan) return false;
        if (Path.GetFullPath(plan.Source) == Path.GetFullPath(plan.Dest)) return false;
        // Another copy of the app running (an older one in Applications): swapping the bundle under it and opening the
        // new one would only hand off to that old copy, so nothing is offered until it's quit.
        if ((otherCopyRunning ?? (() => OtherCopyRunning(run, pid)))()) return false;
        string? eject = plan.Volume is { } v && IsDiskImage(v, run) ? v : null;
        // Never over a copy in Applications that's as new or newer (a disk image or a Downloads copy kept from before would
        // put an older version over one that updated itself): that one opens instead, nothing asked or copied. Not when
        // it's this very copy, run from a translocation folder because an earlier move kept the download's quarantine on
        // it: opening it would only come back here, where moving it again takes the quarantine off. This very copy is
        // always the same version, so a newer one opens whatever `mount` says.
        string? installed = Apps.PlistString(Path.Combine(plan.Dest, "Contents", "Info.plist"), "CFBundleShortVersionString");
        int order = installed is null ? -1 : Updates.Compare(Updates.ParseVersion(installed),
            Updates.ParseVersion(Apps.PlistString(Path.Combine(plan.Source, "Contents", "Info.plist"), "CFBundleShortVersionString")));
        if (order > 0 || (order == 0 && !ItselfTranslocated(plan, run)))
        {
            log($"[move] {plan.Dest} is already {installed}: it opens instead of this copy");
            spawn(WaiterCommand(pid, plan.Dest, eject));
            return true;
        }
        string declined = Path.Combine(home, DeclinedFile);
        if (File.Exists(declined)) return false;
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
            // Without the download's quarantine: plain ditto copies it onto every file, and Gatekeeper then runs the copy in
            // Applications from a translocation folder too, where this offer comes back on every open and no update can
            // be put in. --noqtn adds none from the disk image; xattr takes off what a Downloads copy carries itself. The
            // signature still holds: the quarantine isn't part of it.
            var copy = run("ditto", ["--noqtn", plan.Source, fresh], TimeSpan.FromMinutes(10));
            if (copy is not { ExitCode: 0 } || run("/usr/bin/xattr", ["-dr", "com.apple.quarantine", fresh], TimeSpan.FromMinutes(2)) is not { ExitCode: 0 })
            {
                Apps.TryDeleteFolder(fresh);
                log("[move] couldn't copy the app into " + Path.GetDirectoryName(plan.Dest) + $" ({(copy is { ExitCode: 0 } ? "its quarantine stayed on" : "the copy failed")}); it stays where it is");
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
        spawn(WaiterCommand(pid, plan.Dest, eject));
        return true;
    }

    /// <summary>This copy runs from a translocation folder that mirrors <see cref="MovePlan.Dest"/> itself, or where it
    /// really is can't be told: `mount` lists the folder as a mount of that place.</summary>
    static bool ItselfTranslocated(MovePlan plan, Runner run) =>
        plan.Source.Replace('\\', '/').Contains("/AppTranslocation/", StringComparison.Ordinal)
        && (OriginalOf(plan.Source, Mounts(run)) is not { } original || Firm(original) == Firm(plan.Dest.TrimEnd('/')));

    /// <summary>A path without the Data volume's own prefix: /Applications is /System/Volumes/Data/Applications too.</summary>
    static string Firm(string path) => path.StartsWith("/System/Volumes/Data/", StringComparison.Ordinal) ? path["/System/Volumes/Data".Length..] : path;

    /// <summary>What's mounted where, as `mount` lists it ("FROM on TO (kind, …)"): a disk image's volume comes from its
    /// /dev/disk, and a translocation folder from the place it mirrors.</summary>
    internal static List<(string From, string To)> Mounts(Runner run) =>
        run("/sbin/mount", [], TimeSpan.FromSeconds(20)) is { ExitCode: 0 } p
            ? [.. p.Stdout.Split('\n').Select(l => MountLine.Match(l.TrimEnd())).Where(m => m.Success).Select(m => (m.Groups["from"].Value, m.Groups["to"].Value))]
            : [];

    static readonly System.Text.RegularExpressions.Regex MountLine = new(@"^(?<from>.+?) on (?<to>/.*) \([^()]*\)$");

    /// <summary>The mount <paramref name="path"/> is on: the deepest mount point it's inside.</summary>
    internal static (string From, string To)? MountOf(string path, List<(string From, string To)> mounts) =>
        mounts.Where(m => path == m.To || path.StartsWith(m.To.TrimEnd('/') + "/", StringComparison.Ordinal))
            .OrderByDescending(m => m.To.Length).Select(m => ((string From, string To)?)m).FirstOrDefault();

    /// <summary>Where a copy Gatekeeper runs from a translocation folder really is, or null when `mount` doesn't say or
    /// what it says isn't there. macOS lists the folder (…/AppTranslocation/&lt;id&gt;) as a mount of the bundle itself,
    /// which shows inside it as d/&lt;the bundle's name&gt;: "/Applications/X.app on …/AppTranslocation/&lt;id&gt;" for a
    /// copy that runs from …/AppTranslocation/&lt;id&gt;/d/X.app.</summary>
    internal static string? OriginalOf(string translocated, List<(string From, string To)> mounts)
    {
        // /var is /private/var: `mount` names the folder one way, the path a copy runs from may say the other.
        static string Canon(string p) => p.StartsWith("/private/var/", StringComparison.Ordinal) ? p["/private".Length..] : p;
        string path = Canon(translocated.Replace('\\', '/'));
        if (MountOf(path, [.. mounts.Select(m => (m.From, Canon(m.To)))]) is not { } t
            || !t.To.Contains("/AppTranslocation/", StringComparison.Ordinal) || !t.From.StartsWith('/'))
            return null;
        string from = t.From.TrimEnd('/'), root = t.To.TrimEnd('/'), mirror = root + "/d/" + Path.GetFileName(from);
        string? original = Under(path, mirror) ? from + path[mirror.Length..]
            : Under(path, root) ? from + path[root.Length..] : null;
        return original is not null && Directory.Exists(original) ? original : null;
    }

    static bool Under(string path, string folder) => path == folder || path.StartsWith(folder + "/", StringComparison.Ordinal);

    /// <summary>Any other process running a program from a Syft Notes.app (or an old Study Stash.app) bundle: the app,
    /// or the library it runs. <paramref name="bundle"/> narrows it to one bundle.</summary>
    public static bool OtherCopyRunning(Runner run, int pid, string? bundle = null) =>
        run("/usr/bin/pgrep", ["-f", bundle is null ? "(Syft Notes|Study Stash)\\.app/Contents/MacOS/" : Ere(bundle.TrimEnd('/') + "/Contents/MacOS/")], TimeSpan.FromSeconds(10)) is { ExitCode: 0 } p
        && p.Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Any(l => int.TryParse(l.Trim(), out int other) && other != pid);

    /// <summary>A path as a POSIX extended regular expression (pgrep's) that matches it alone: its special characters
    /// escaped, and nothing else (a space stays a space).</summary>
    internal static string Ere(string text) =>
        string.Concat(text.Select(c => "\\.^$|?*+()[]{}".Contains(c) ? "\\" + c : c.ToString()));

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
