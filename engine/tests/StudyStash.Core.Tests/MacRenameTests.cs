namespace StudyStash.Core.Tests;

/// <summary>The Mac bundle's rename to "Syft Notes.app" (MacRename.cs): an old copy's updater leaves the new app under
/// the old name, and a drag from the new DMG can leave one of each.</summary>
public class MacRenameTests
{
    static string Bundle(string parent, string name, string version)
    {
        string contents = Path.Combine(parent, name, "Contents");
        Directory.CreateDirectory(Path.Combine(contents, "MacOS", "arm64"));
        File.WriteAllText(Path.Combine(contents, "Info.plist"), $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <plist version="1.0"><dict><key>CFBundleIdentifier</key><string>com.study-stash.app</string>
            <key>CFBundleShortVersionString</key><string>{version}</string></dict></plist>
            """);
        return Path.Combine(contents, "MacOS", "arm64");
    }

    static bool Go(RenamePlan plan, List<IReadOnlyList<string>> spawned, Func<string, bool>? running = null) =>
        MacRename.Run(plan, running ?? (_ => false), () => { }, (_, _, _) => new ProcResult(0, ""), spawned.Add, 4242, ["--background"], _ => { });

    [Fact]
    public void The_old_name_becomes_the_new_one_and_it_opens_from_there_with_the_same_arguments()
    {
        using var t = new TempDir();
        var plan = MacRename.Plan(Bundle(t.Path, "Study Stash.app", "0.23.0"), _ => true)!;
        Assert.Equal(Path.Combine(t.Path, "Syft Notes.app"), plan.To);
        var spawned = new List<IReadOnlyList<string>>();
        // The waiter starts while the bundle this process runs from is still in place: once it has moved, the process
        // can't load the code that starts a program (a student's first 0.23.0 start crashed that way).
        bool movedBeforeSpawn = true;
        Assert.True(MacRename.Run(plan, _ => false, () => { }, (_, _, _) => new ProcResult(0, ""),
            a => { movedBeforeSpawn = !Directory.Exists(plan.From); spawned.Add(a); }, 4242, ["--background"], _ => { }));
        Assert.False(movedBeforeSpawn);
        Assert.False(Directory.Exists(Path.Combine(t.Path, "Study Stash.app")));
        Assert.True(File.Exists(Path.Combine(t.Path, "Syft Notes.app", "Contents", "Info.plist")));
        Assert.Equal(["4242", plan.From, plan.To, "--background"], spawned.Single().Skip(3).ToArray());
        Assert.Contains("caffeinate -w", spawned.Single()[2]);
    }

    [Fact]
    public void One_of_each_ends_as_the_newer_one_under_the_new_name()
    {
        using var t = new TempDir();
        var spawned = new List<IReadOnlyList<string>>();
        // An older Syft Notes.app is replaced by this newer copy.
        string older = Path.Combine(t.Path, "a");
        Bundle(older, "Syft Notes.app", "0.22.1");
        Assert.True(Go(MacRename.Plan(Bundle(older, "Study Stash.app", "0.23.0"), _ => true)!, spawned));
        Assert.Equal(["Syft Notes.app"], Directory.GetDirectories(older).Select(Path.GetFileName));
        Assert.Contains("0.23.0", File.ReadAllText(Path.Combine(older, "Syft Notes.app", "Contents", "Info.plist")));
        // A newer Syft Notes.app stays, and this older copy goes.
        string newer = Path.Combine(t.Path, "b");
        Bundle(newer, "Syft Notes.app", "0.24.0");
        var plan = MacRename.Plan(Bundle(newer, "Study Stash.app", "0.23.0"), _ => true)!;
        Assert.True(plan.OpenInstead);
        Assert.True(Go(plan, spawned));
        Assert.Contains("0.24.0", File.ReadAllText(Path.Combine(newer, "Syft Notes.app", "Contents", "Info.plist")));
        Assert.False(Directory.Exists(Path.Combine(newer, "Study Stash.app")));
        // Started from Syft Notes.app, an old Study Stash.app beside it goes, but not while it's still open.
        string beside = Path.Combine(t.Path, "c");
        Bundle(beside, "Study Stash.app", "0.22.1");
        var tidy = MacRename.Plan(Bundle(beside, "Syft Notes.app", "0.23.0"), _ => true)!;
        Assert.False(Go(tidy, spawned, running: _ => true));
        Assert.True(Directory.Exists(Path.Combine(beside, "Study Stash.app")));
        Assert.False(Go(tidy, spawned));
        Assert.False(Directory.Exists(Path.Combine(beside, "Study Stash.app")));
        Assert.Equal(2, spawned.Count);
    }

    [Fact]
    public void Nothing_for_a_disk_image_a_folder_it_cant_change_or_someone_elses_app()
    {
        using var t = new TempDir();
        Assert.Null(MacRename.Plan(Bundle(t.Path, "Study Stash.app", "0.23.0"), _ => false));
        Assert.Null(MacRename.Plan(Bundle(Path.Combine(t.Path, "x", "AppTranslocation", "A"), "Study Stash.app", "0.23.0"), _ => true));
        Assert.Null(MacRename.Plan(Bundle(Path.Combine(t.Path, "d"), "Syft Notes.app", "0.23.0"), _ => true));
        string theirs = Path.Combine(t.Path, "e");
        string mine = Bundle(theirs, "Study Stash.app", "0.23.0");
        Directory.CreateDirectory(Path.Combine(theirs, "Syft Notes.app"));
        Assert.Null(MacRename.Plan(mine, _ => true));
    }
}
