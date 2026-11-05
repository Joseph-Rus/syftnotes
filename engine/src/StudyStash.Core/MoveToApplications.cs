namespace StudyStash.Core;

/// <summary>Where a Mac copy should go, and the disk image it ran from (null when it didn't run from one).</summary>
public sealed record MovePlan(string Source, string Dest, string? Volume);

/// <summary>
/// "Move Syft Notes to Applications" (the LetsMove pattern): a copy opened straight from its DMG, from Gatekeeper's
/// translocation folder (a download opened from Downloads) or from Downloads or the Desktop offers once to move
/// itself into /Applications (~/Applications when that isn't writable), opens the copy there, and ejects the DMG it
/// came from (for a translocated copy, the one its translocation folder mirrors). One already there that's as new or
/// newer opens instead. A build folder, an app somewhere the student put it on purpose, and the self-test never see it.
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

    /// <summary>The whole thing for this run: nothing if there's nothing to offer (or the student said "Not now" to this
    /// version in Downloads or on the Desktop), else asks once. True when the copy is moved and will open from its new
    /// place, or the copy already in Applications is as new or newer and opens instead: the caller quits now.
    /// <paramref name="ask"/> shows the one sheet (true for Move). <paramref name="spawn"/> starts the little waiter
    /// that, once this process is gone, opens the copy in Applications and ejects the disk image.
    /// <paramref name="otherCopyRunning"/> is <see cref="OtherCopyRunning"/> unless a test says otherwise (the app passes
    /// the engine's commands to leave out). <paramref name="background"/> is the computer's background library, stopped
    /// while the bundle it runs from is replaced.</summary>
    public static bool Offer(string baseDir, string home, string userHome, Func<bool> ask, Runner run,
        Action<IReadOnlyList<string>> spawn, int pid, Action<string> log, bool? applicationsWritable = null,
        Func<bool>? otherCopyRunning = null, IBackgroundLibrary? background = null)
    {
        bool writable = applicationsWritable ?? Machine.Writable("/Applications");
        if (Plan(baseDir, userHome, writable) is not { } plan) return false;
        if (Path.GetFullPath(plan.Source) == Path.GetFullPath(plan.Dest)) return false;
        // Another copy of the app open (an older one in Applications, its window or its menu bar copy): swapping the
        // bundle under it and opening the new one would only hand off to that old copy, so nothing is offered until it's
        // quit. What AI apps and the computer run from the bundle (MCP, the background library) doesn't count.
        if ((otherCopyRunning ?? (() => OtherCopyRunning(run, pid)))()) return false;
        // The disk image to put away: the one it runs from, or for a copy Gatekeeper runs from a translocation folder
        // (whose path doesn't say), the one that folder mirrors, as `mount` lists it.
        bool translocated = plan.Source.Replace('\\', '/').Contains("/AppTranslocation/", StringComparison.Ordinal);
        List<(string From, string To)> mounts = translocated ? Mounts(run) : [];
        string? volume = !translocated ? plan.Volume : OriginalOf(plan.Source, mounts) is { } original ? MountOf(original, mounts)?.To : null;
        string? eject = volume is not null && DiskImages(run).Contains(volume) ? volume : null;
        // Never over a copy in Applications that's as new or newer (a disk image or a Downloads copy kept from before would
        // put an older version over one that updated itself): that one opens instead, nothing asked or copied. Not when
        // it's this very copy, run from a translocation folder because an earlier move kept the download's quarantine on
        // it: opening it would only come back here, where moving it again takes the quarantine off.
        if (Apps.PlistString(Path.Combine(plan.Dest, "Contents", "Info.plist"), "CFBundleShortVersionString") is { } installed
            && Updates.Compare(Updates.ParseVersion(installed), Updates.ParseVersion(Apps.PlistString(Path.Combine(plan.Source, "Contents", "Info.plist"), "CFBundleShortVersionString"))) >= 0
            && !ItselfTranslocated(plan, mounts))
        {
            log($"[move] {plan.Dest} is already {installed}: it opens instead of this copy");
            spawn(WaiterCommand(pid, plan.Dest, eject));
            return true;
        }
        // "Not now" is kept for this version, and only for a copy in Downloads or on the Desktop: one on its disk image or in
        // a translocation folder can't update itself from there, so it asks every time.
        bool remembers = plan.Volume is null && !translocated;
        string version = Apps.PlistString(Path.Combine(plan.Source, "Contents", "Info.plist"), "CFBundleShortVersionString") ?? "";
        string declined = Path.Combine(home, DeclinedFile);
        if (remembers && Said(declined) == version) return false;
        if (!ask())
        {
            if (remembers)
                try { File.WriteAllText(declined, version); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            return false;
        }
        // The background library runs from the bundle about to be replaced: it goes first, by its label, so launchd neither
        // keeps it on a bundle being deleted nor starts it from a half-made one, and it's registered again once the new
        // bundle is in place (or the old one is back).
        var paused = background?.All().Where(r => r.Args.Count >= 4 && r.Args[0].StartsWith(plan.Dest.TrimEnd('/') + "/", StringComparison.Ordinal)).ToList() ?? [];
        foreach (var (h, _) in paused) background!.Stop(h);
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
        finally
        {
            foreach (var (h, args) in paused)
                try { background!.Register(h, args[0], [.. args.Skip(4)]); }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException)
                {
                    log("[move] couldn't start the background library again: " + e.Message);
                }
        }
        log("[move] moved to " + plan.Dest);
        spawn(WaiterCommand(pid, plan.Dest, eject));
        return true;
    }

    /// <summary>This copy runs from a translocation folder that mirrors <see cref="MovePlan.Dest"/> itself, or where it
    /// really is can't be told: `mount` lists the folder as a mount of that place.</summary>
    static bool ItselfTranslocated(MovePlan plan, List<(string From, string To)> mounts) =>
        plan.Source.Replace('\\', '/').Contains("/AppTranslocation/", StringComparison.Ordinal)
        && (OriginalOf(plan.Source, mounts) is not { } original || Firm(original) == Firm(plan.Dest.TrimEnd('/')));

    /// <summary>The version "Not now" was said to (an older copy wrote nothing in the file), or null when it wasn't.</summary>
    static string? Said(string declined)
    {
        try
        {
            return File.Exists(declined) ? File.ReadAllText(declined).Trim() : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

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

    /// <summary>Where a copy Gatekeeper runs from a translocation folder really is: the place that folder is a mount of
    /// (with the rest of the path below it), or null when `mount` doesn't say.</summary>
    internal static string? OriginalOf(string translocated, List<(string From, string To)> mounts) =>
        MountOf(translocated, mounts) is { } t && t.To.Contains("/AppTranslocation/", StringComparison.Ordinal) && t.From.StartsWith('/')
            ? t.From + translocated[t.To.TrimEnd('/').Length..] : null;

    /// <summary>Any other process running the app from a Syft Notes.app (or an old Study Stash.app) bundle: its window,
    /// its menu bar copy started at login, or another program of the bundle's (an update being put in). The engine's
    /// own commands (<paramref name="engine"/> knows them: StudyStash.Library.Cli.IsCommand) never count: AI apps start
    /// `mcp` from the bundle and the computer runs the background library's `helper`, and they run for days (a Mac had
    /// seventeen, so nothing was ever offered). Without <paramref name="engine"/> every program counts.
    /// <paramref name="bundle"/> narrows it to one bundle.</summary>
    public static bool OtherCopyRunning(Runner run, int pid, string? bundle = null, Func<IReadOnlyList<string>, bool>? engine = null)
    {
        string where = bundle is null ? "(Syft Notes|Study Stash)\\.app/Contents/MacOS/" : Ere(bundle.TrimEnd('/') + "/Contents/MacOS/");
        if (run("/usr/bin/pgrep", ["-lf", where], TimeSpan.FromSeconds(10)) is not { ExitCode: 0 } p) return false;
        // `pgrep -lf`: a process id, then its whole command line (Claude's own wrapper names the bundle's program in its).
        var program = new System.Text.RegularExpressions.Regex(where + "(?<exe>\\S+)(?<args>.*)");
        foreach (string line in p.Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] words = line.Trim().Split(' ', 2);
            if (!int.TryParse(words[0], out int other) || other == pid) continue;
            if (engine is not null && program.Match(words.Length > 1 ? words[1] : "") is { Success: true } m && m.Groups["exe"].Value == "StudyStash"
                && engine(m.Groups["args"].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))) continue;
            return true;
        }
        return false;
    }

    /// <summary>A path as a POSIX extended regular expression (pgrep's) that matches it alone: its special characters
    /// escaped, and nothing else (a space stays a space).</summary>
    internal static string Ere(string text) =>
        string.Concat(text.Select(c => "\\.^$|?*+()[]{}".Contains(c) ? "\\" + c : c.ToString()));

    /// <summary>Where each disk image is mounted, from `hdiutil info`: its device lines are the device, its kind and its
    /// mount point, split by tabs. It lists only disk images, so a volume named there is one a download made, never a
    /// USB stick or another disk.</summary>
    internal static List<string> DiskImages(Runner run) =>
        run("/usr/bin/hdiutil", ["info"], TimeSpan.FromSeconds(20)) is { ExitCode: 0 } p
            ? [.. p.Stdout.Split('\n').Where(l => l.StartsWith("/dev/", StringComparison.Ordinal)).Select(l => l.Split('\t'))
                .Where(f => f.Length > 2 && f[2].Trim().Length > 0).Select(f => f[2].Trim())]
            : [];

    /// <summary>Waits for <paramref name="pid"/> to quit (caffeinate -w blocks until it exits: no polling) and opens the
    /// app: a new copy of it, as a program the computer still runs from the old bundle may otherwise be taken for the app.
    /// Then it puts <paramref name="volume"/> (when not empty) away: asked up to three times while Finder may still have
    /// its window open or the system its translocation folder (gone within seconds of this copy quitting), with `sync`
    /// between tries rather than a fixed wait (macos/dmg.sh's way), then made to. It stops as soon as the volume is
    /// gone.</summary>
    internal static IReadOnlyList<string> WaiterCommand(int pid, string dest, string? volume) =>
    [
        "/bin/sh", "-c",
        """
        /usr/bin/caffeinate -w "$1"
        /usr/bin/open -n "$2"
        [ -n "$3" ] || exit 0
        for try in 1 2 3; do
          [ -d "$3" ] || exit 0
          /usr/bin/hdiutil detach "$3" -quiet && exit 0
          sync
        done
        exec /usr/bin/hdiutil detach "$3" -quiet -force
        """,
        "sh", pid.ToString(System.Globalization.CultureInfo.InvariantCulture), dest, volume ?? "",
    ];
}
