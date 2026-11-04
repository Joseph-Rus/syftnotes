using System.Diagnostics;

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

    static bool Go(RenamePlan plan, List<IReadOnlyList<string>> spawned, Func<string, bool>? running = null, string[]? args = null) =>
        MacRename.Run(plan, running ?? (_ => false), () => { }, spawned.Add, 4242, args ?? ["--background"], _ => { });

    /// <summary>Runs the shell program the rename spawned, as it would run once the app has quit, with the wait for the
    /// app and macOS's open stood in for (caffeinate returns at once; open says what it would open). What it printed.</summary>
    static string RunScript(IReadOnlyList<string> spawned)
    {
        string script = spawned[2].Replace("/usr/bin/caffeinate -w \"$pid\"", ":").Replace("/usr/bin/open", "echo open");
        var psi = new ProcessStartInfo("/bin/sh") { RedirectStandardOutput = true, RedirectStandardError = true };
        psi.ArgumentList.Add("-c");
        psi.ArgumentList.Add(script);
        foreach (string a in spawned.Skip(3)) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        string output = p.StandardOutput.ReadToEnd();
        p.WaitForExit();
        return output.Trim();
    }

    [Fact]
    public void The_rename_happens_after_the_app_quits_never_inside_it_and_then_it_opens_from_the_new_name()
    {
        // Nothing moves inside the app: once its own bundle has moved, a process can't load any more of its code, and
        // the first start of 0.23.0 crashed that way. The rename is left to a shell program that runs once it has quit.
        if (OperatingSystem.IsWindows()) return; // the shell program is a Mac's
        using var t = new TempDir();
        var plan = MacRename.Plan(Bundle(t.Path, "Study Stash.app", "0.23.0"), _ => true)!;
        Assert.Equal(Path.Combine(t.Path, "Syft Notes.app"), plan.To);
        var spawned = new List<IReadOnlyList<string>>();
        Assert.True(Go(plan, spawned));
        Assert.True(Directory.Exists(plan.From));
        Assert.False(Directory.Exists(plan.To));
        Assert.Contains("caffeinate -w", spawned.Single()[2]);

        Assert.Equal($"open {plan.To} --args --background", RunScript(spawned.Single()));
        Assert.False(Directory.Exists(plan.From));
        Assert.True(File.Exists(Path.Combine(plan.To, "Contents", "Info.plist")));

        // A move that fails (something already in the way) opens the old copy again, which then keeps its name.
        string blocked = Path.Combine(t.Path, "b");
        var stuck = MacRename.Plan(Bundle(blocked, "Study Stash.app", "0.23.0"), _ => true)!;
        spawned.Clear();
        Assert.True(Go(stuck, spawned));
        File.WriteAllText(stuck.To, "in the way");
        Assert.Equal($"open {stuck.From} --args --background {MacRename.Failed}", RunScript(spawned.Single()));
        Assert.True(Directory.Exists(stuck.From));
        spawned.Clear();
        Assert.False(Go(stuck, spawned, args: ["--background", MacRename.Failed]));
        Assert.Empty(spawned);

        // A folder that turned up at the new name after the plan was made: mv would move the app inside it and say
        // it worked. The old copy opens again instead, as it was.
        string folder = Path.Combine(t.Path, "c");
        var late = MacRename.Plan(Bundle(folder, "Study Stash.app", "0.23.0"), _ => true)!;
        spawned.Clear();
        Assert.True(Go(late, spawned));
        Directory.CreateDirectory(late.To);
        Assert.Equal($"open {late.From} --args --background {MacRename.Failed}", RunScript(spawned.Single()));
        Assert.True(File.Exists(Path.Combine(late.From, "Contents", "Info.plist")));
        Assert.Empty(Directory.GetFileSystemEntries(late.To));
    }

    [Fact]
    public void One_of_each_ends_as_the_newer_one_under_the_new_name()
    {
        if (OperatingSystem.IsWindows()) return; // the shell program is a Mac's
        using var t = new TempDir();
        var spawned = new List<IReadOnlyList<string>>();
        // An older Syft Notes.app is replaced by this newer copy, once it has quit.
        string older = Path.Combine(t.Path, "a");
        Bundle(older, "Syft Notes.app", "0.22.1");
        Assert.True(Go(MacRename.Plan(Bundle(older, "Study Stash.app", "0.23.0"), _ => true)!, spawned));
        Assert.Equal(2, Directory.GetDirectories(older).Length);
        RunScript(spawned.Single());
        Assert.Equal(["Syft Notes.app"], Directory.GetDirectories(older).Select(Path.GetFileName));
        Assert.Contains("0.23.0", File.ReadAllText(Path.Combine(older, "Syft Notes.app", "Contents", "Info.plist")));
        // A newer Syft Notes.app stays, and this older copy goes once it has quit, and that one opens.
        string newer = Path.Combine(t.Path, "b");
        Bundle(newer, "Syft Notes.app", "0.24.0");
        var plan = MacRename.Plan(Bundle(newer, "Study Stash.app", "0.23.0"), _ => true)!;
        Assert.True(plan.OpenInstead);
        spawned.Clear();
        Assert.True(Go(plan, spawned));
        Assert.True(Directory.Exists(plan.From));
        Assert.Equal($"open {plan.To} --args --background", RunScript(spawned.Single()));
        Assert.Contains("0.24.0", File.ReadAllText(Path.Combine(newer, "Syft Notes.app", "Contents", "Info.plist")));
        Assert.False(Directory.Exists(Path.Combine(newer, "Study Stash.app")));
        // Started from Syft Notes.app, an old Study Stash.app beside it goes (not the bundle this copy runs from, so
        // right away), but not while it's still open.
        string beside = Path.Combine(t.Path, "c");
        Bundle(beside, "Study Stash.app", "0.22.1");
        var tidy = MacRename.Plan(Bundle(beside, "Syft Notes.app", "0.23.0"), _ => true)!;
        spawned.Clear();
        Assert.False(Go(tidy, spawned, running: _ => true));
        Assert.True(Directory.Exists(Path.Combine(beside, "Study Stash.app")));
        Assert.False(Go(tidy, spawned));
        Assert.False(Directory.Exists(Path.Combine(beside, "Study Stash.app")));
        Assert.Empty(spawned);
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
