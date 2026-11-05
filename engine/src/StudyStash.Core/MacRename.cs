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

    /// <summary>True when <paramref name="program"/> sits in another bundle of ours (the same CFBundleIdentifier) with a
    /// lower version than the bundle <paramref name="current"/> runs from: an older copy kept beside this one (in
    /// ~/Applications, a Finder "Keep Both" copy, one left in Downloads). This bundle itself, a newer or equal copy and
    /// someone else's app are never older.</summary>
    internal static bool OlderCopy(string program, string current) =>
        Apps.EnclosingMacBundle(program) is { } theirs && Apps.EnclosingMacBundle(current) is { } mine
        && !string.Equals(theirs, mine, StringComparison.OrdinalIgnoreCase)
        && Ours(theirs) && Updates.Compare(VersionOf(theirs), VersionOf(mine)) < 0;

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

    /// <summary>What a copy that couldn't be renamed is opened again with, so it carries on under its old name instead
    /// of trying the same rename again.</summary>
    public const string Failed = "--rename-failed";

    /// <summary>
    /// The little shell program that does the rename once this copy is gone: it waits for <paramref name="pid"/> to quit
    /// (caffeinate -w blocks until it exits), then moves <c>From</c> to <c>To</c> (an older copy at <c>To</c> deleted
    /// first) and opens it with the arguments this copy had (--background after an update's relaunch, so no window). If
    /// anything is still at <c>To</c> (mv would put the app inside a folder there) or the move fails, it opens the old
    /// bundle again, with <see cref="Failed"/>. For a newer copy already in place
    /// (<c>OpenInstead</c>) it deletes this copy and opens that one.
    /// </summary>
    internal static IReadOnlyList<string> Script(int pid, RenamePlan plan, IReadOnlyList<string> args) =>
    [
        "/bin/sh", "-c",
        """
        pid="$1"; from="$2"; to="$3"; old="$4"; newer="$5"; shift 5
        /usr/bin/caffeinate -w "$pid"
        if [ "$newer" = 1 ]; then /bin/rm -rf "$from"; exec /usr/bin/open "$to" --args "$@"; fi
        [ -n "$old" ] && /bin/rm -rf "$old"
        [ ! -e "$to" ] && /bin/mv "$from" "$to" && exec /usr/bin/open "$to" --args "$@"
        exec /usr/bin/open "$from" --args "$@" "$0"
        """,
        Failed, pid.ToString(System.Globalization.CultureInfo.InvariantCulture), plan.From, plan.To,
        plan.OpenInstead ? "" : plan.Remove ?? "", plan.OpenInstead ? "1" : "", .. args,
    ];

    /// <summary>
    /// Carry out <paramref name="plan"/>. True when this process should quit now: the rename happens after it has gone.
    /// This copy runs from the bundle that moves, and a process can't load any more of its code once its bundle has moved
    /// (starting a program needs System.IO.Pipes; the first start of 0.23.0 crashed on that), so nothing moves or is
    /// deleted here: <paramref name="spawn"/> starts <see cref="Script"/>, which does it all once this process is gone.
    /// A copy of the app running from a bundle about to go (an older one still open) holds the plan off until it's quit.
    /// <paramref name="stopBackground"/> stops this copy's own background library (the phone answered with the app
    /// closed) before its bundle moves; the app starts it again from the new place. Started with <see cref="Failed"/>,
    /// it doesn't try again.
    /// </summary>
    public static bool Run(RenamePlan plan, Func<string, bool> runningFrom, Action stopBackground,
        Action<IReadOnlyList<string>> spawn, int pid, IReadOnlyList<string> args, Action<string> log)
    {
        if (args.Contains(Failed))
        {
            log($"[rename] {plan.From} couldn't become {plan.To}, so it keeps its name for now");
            return false;
        }
        if (plan.Remove is { } remove && !string.Equals(remove, plan.From, StringComparison.Ordinal) && runningFrom(remove))
        {
            log($"[rename] {remove} is still open, so it stays until it's quit");
            return false;
        }
        if (plan.From == plan.To)
        {
            // Another bundle (an old Study Stash.app beside this one), never the one this copy runs from.
            Apps.TryDeleteFolder(plan.Remove!);
            log($"[rename] removed the old {plan.Remove}");
            return false;
        }
        stopBackground();
        spawn(Script(pid, plan, args));
        log(plan.OpenInstead ? $"[rename] {plan.To} is newer: {plan.From} goes once this copy has quit, and that one opens"
            : $"[rename] {plan.From} becomes {plan.To} once this copy has quit");
        return true;
    }
}
