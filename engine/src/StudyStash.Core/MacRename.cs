namespace StudyStash.Core;

/// <summary>What <see cref="MacRename.Plan"/> found for the bundle this copy runs from.</summary>
/// <param name="From">This copy's bundle.</param>
/// <param name="To">Where it ends up: "Syft Notes.app" in the same folder.</param>
/// <param name="Remove">A bundle to delete first: an older copy of the app already at <paramref name="To"/>, or an
/// old "Study Stash.app" left beside this one.</param>
/// <param name="OpenInstead">The copy at <paramref name="To"/> is newer than this one: this copy is the one removed,
/// and that one opens.</param>
public sealed record RenamePlan(string From, string To, string? Remove, bool OpenInstead = false);

/// <summary>
/// The Mac bundle's own name, "Study Stash.app" until the Velopack bridge (docs/LIBRARIES.md, phase 5), is now
/// "Syft Notes.app". An older copy's updater swaps the new app in under the name it already had (it looks for
/// "Study Stash.app" in the DMG, which keeps a hidden link by that name), so the new app renames its own bundle on its
/// first start, before any window, and opens again from there. A student who drags the new "Syft Notes.app" in beside
/// an old "Study Stash.app" has the old one removed the same way, so there is never one of each. Settings, lectures
/// and Documents aren't in the bundle and are never touched.
/// </summary>
public static class MacRename
{
    public const string Name = "Syft Notes.app";
    public const string OldName = "Study Stash.app";
    const string BundleId = "com.study-stash.app";

    static bool Ours(string bundle) =>
        Directory.Exists(bundle) && Apps.PlistString(Path.Combine(bundle, "Contents", "Info.plist"), "CFBundleIdentifier") == BundleId;

    static int[] VersionOf(string bundle) =>
        Updates.ParseVersion(Apps.PlistString(Path.Combine(bundle, "Contents", "Info.plist"), "CFBundleShortVersionString"));

    /// <summary>What to do for the copy that runs from <paramref name="baseDir"/>, or null for nothing: not inside a
    /// bundle of ours, still on its disk image or in Gatekeeper's translocation folder (moving into Applications comes
    /// first, MoveToApplications.cs), or in a folder this account can't change.</summary>
    public static RenamePlan? Plan(string baseDir, Func<string, bool>? writable = null)
    {
        writable ??= Machine.Writable;
        if (Apps.EnclosingMacBundle(baseDir) is not { } app || !Ours(app)) return null;
        string path = app.Replace('\\', '/');
        if (path.Contains("/AppTranslocation/", StringComparison.Ordinal) || path.StartsWith("/Volumes/", StringComparison.Ordinal)) return null;
        if (Path.GetDirectoryName(app) is not { } parent || !writable(parent)) return null;
        string to = Path.Combine(parent, Name);
        if (string.Equals(Path.GetFileName(app), Name, StringComparison.Ordinal))
        {
            // Already renamed: an old "Study Stash.app" beside it, no newer than this one, goes.
            string old = Path.Combine(parent, OldName);
            return Ours(old) && Updates.Compare(VersionOf(old), VersionOf(app)) <= 0 ? new RenamePlan(app, app, old) : null;
        }
        if (!string.Equals(Path.GetFileName(app), OldName, StringComparison.Ordinal)) return null;
        if (!Directory.Exists(to)) return new RenamePlan(app, to, null);
        if (!Ours(to)) return null; // someone else's "Syft Notes.app": leave both alone
        return Updates.Compare(VersionOf(to), VersionOf(app)) > 0 ? new RenamePlan(app, to, app, OpenInstead: true) : new RenamePlan(app, to, to);
    }

    /// <summary>Waits for <paramref name="pid"/> to quit (caffeinate -w blocks until it exits), then opens the app at
    /// <paramref name="app"/> with the arguments this copy had (--background when it was an update's relaunch, so no
    /// window opens). With a <paramref name="from"/>, only once it's gone: a move that didn't happen opens nothing.</summary>
    internal static IReadOnlyList<string> Reopen(int pid, string from, string app, IReadOnlyList<string> args) =>
    [
        "/bin/sh", "-c",
        "from=\"$1\"; app=\"$2\"; shift 2; /usr/bin/caffeinate -w \"$0\"; [ -n \"$from\" ] && [ -e \"$from\" ] && exit 0; exec /usr/bin/open \"$app\" --args \"$@\"",
        pid.ToString(System.Globalization.CultureInfo.InvariantCulture), from, app, .. args,
    ];

    /// <summary>
    /// Carry out <paramref name="plan"/>. True when this process should quit now because the app opens from its new
    /// place (<paramref name="spawn"/> started the waiter that opens it once this process is gone). A copy of the app
    /// running from the bundle about to go (an older one still open) holds the plan off until it's quit: deleting it
    /// under that copy would leave it running from nothing. <paramref name="stopBackground"/> stops this copy's own
    /// background library (the phone answered with the app closed) before its bundle moves; the app starts it again
    /// from the new place. The waiter starts before this copy's own bundle moves or goes: from then on this process can't
    /// load any more of its code (starting a process needs System.IO.Pipes, not loaded yet), so it only returns and quits.
    /// </summary>
    public static bool Run(RenamePlan plan, Func<string, bool> runningFrom, Action stopBackground, Runner run,
        Action<IReadOnlyList<string>> spawn, int pid, IReadOnlyList<string> args, Action<string> log)
    {
        try
        {
            if (plan.Remove is { } remove && !string.Equals(remove, plan.From, StringComparison.Ordinal) && runningFrom(remove))
            {
                log($"[rename] {remove} is still open, so it stays until it's quit");
                return false;
            }
            if (plan.From == plan.To)
            {
                Apps.TryDeleteFolder(plan.Remove!);
                log($"[rename] removed the old {plan.Remove}");
                return false;
            }
            stopBackground();
            // A newer copy already in place opens whatever happens to this one; a moved one only if it really moved.
            spawn(Reopen(pid, plan.OpenInstead ? "" : plan.From, plan.To, args));
            if (plan.OpenInstead)
            {
                // The newer copy is already in place: this one goes, and that one opens.
                Apps.TryDeleteFolder(plan.From);
                log($"[rename] {plan.To} is newer; removed {plan.From} and opening that one");
            }
            else
            {
                if (plan.Remove is { } older) Apps.TryDeleteFolder(older);
                Ready.MoveFolder(plan.From, plan.To, run);
                log($"[rename] {plan.From} is now {plan.To}");
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // The waiter opens nothing while the old bundle is still there; this copy carries on from it.
            log("[rename] couldn't rename the app: " + e.Message);
            return false;
        }
        return true;
    }
}
