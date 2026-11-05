namespace StudyStash.Core.Tests;

public class MoveToApplicationsTests
{
    const string Plist = """
        <?xml version="1.0" encoding="UTF-8"?>
        <plist version="1.0"><dict><key>CFBundleIdentifier</key><string>com.study-stash.app</string></dict></plist>
        """;

    static string Make(string root, string sub, string plist = Plist)
    {
        string contents = Path.Combine(root, sub, "Study Stash.app", "Contents");
        string tree = Path.Combine(contents, "MacOS", "arm64");
        Directory.CreateDirectory(tree);
        File.WriteAllText(Path.Combine(contents, "Info.plist"), plist);
        return tree;
    }

    [Fact]
    public void Offered_from_a_disk_image_from_translocation_and_from_Downloads_or_Desktop()
    {
        if (!OperatingSystem.IsMacOS()) return; // the move is a Mac's, and its disk-image and Applications paths are a Mac's
        using var t = new TempDir();
        string home = Path.Combine(t.Path, "home");
        string vols = Path.Combine(t.Path, "Volumes");
        var dmg = MoveToApplications.Plan(Make(vols, "Syft Notes"), home, true, vols);
        Assert.Equal("/Applications/Syft Notes.app", dmg!.Dest);
        Assert.Equal(Path.Combine(vols, "Syft Notes"), dmg.Volume);
        var moved = MoveToApplications.Plan(Make(t.Path, "x/AppTranslocation/ABC/d"), home, true, vols);
        Assert.NotNull(moved);
        Assert.Null(moved.Volume);
        Assert.NotNull(MoveToApplications.Plan(Make(home, "Downloads"), home, true, vols));
        Assert.NotNull(MoveToApplications.Plan(Make(home, "Desktop"), home, true, vols));
    }

    [Fact]
    public void Goes_to_the_users_Applications_when_the_system_one_is_not_writable()
    {
        using var t = new TempDir();
        string home = Path.Combine(t.Path, "home");
        var plan = MoveToApplications.Plan(Make(home, "Downloads"), home, false);
        Assert.Equal(Path.Combine(home, "Applications", "Syft Notes.app"), plan!.Dest);
    }

    [Fact]
    public void Never_offered_from_Applications_a_build_folder_another_app_or_somewhere_chosen()
    {
        using var t = new TempDir();
        string home = Path.Combine(t.Path, "home");
        Assert.Null(MoveToApplications.Plan(Make(home, "Applications"), home, true));
        Assert.Null(MoveToApplications.Plan(Make(home, "Documents/GitHub/dist"), home, true));
        Assert.Null(MoveToApplications.Plan(Make(home, "Downloads/nested/deeper"), home, true));
        Assert.Null(MoveToApplications.Plan(Make(home, "Downloads", Plist.Replace("com.study-stash.app", "com.example.other")), home, true));
        string bin = Path.Combine(t.Path, "bin", "Debug", "net10.0");
        Directory.CreateDirectory(bin);
        Assert.Null(MoveToApplications.Plan(bin, home, true));
    }

    [Fact]
    public void Not_now_is_remembered_for_that_version_and_never_for_a_copy_gatekeeper_moved()
    {
        using var t = new TempDir();
        string home = Path.Combine(t.Path, "home");
        Directory.CreateDirectory(home);
        int asked = 0;
        // applications=false so the destination is under the temp home, never the real /Applications
        bool Go(string baseDir) => MoveToApplications.Offer(baseDir, home, home, () => { asked++; return false; },
            (_, _, _) => null, _ => throw new InvalidOperationException("nothing starts"), 1, _ => { }, applicationsWritable: false, otherCopyRunning: () => false);
        string downloads = Make(home, "Downloads", Versioned("0.23.2"));
        Assert.False(Go(downloads));
        Assert.False(Go(downloads));
        Assert.Equal(1, asked);
        // A newer download asks again.
        File.WriteAllText(Path.Combine(home, "Downloads", "Study Stash.app", "Contents", "Info.plist"), Versioned("0.23.4"));
        Assert.False(Go(downloads));
        Assert.Equal(2, asked);
        // One Gatekeeper runs from a translocation folder can't update itself from there, so it asks every time.
        string moved = Make(t.Path, "x/AppTranslocation/ABC/d", Versioned("0.23.4"));
        Assert.False(Go(moved));
        Assert.False(Go(moved));
        Assert.Equal(4, asked);
    }

    [Fact]
    public void A_failed_copy_leaves_the_app_where_it_is()
    {
        using var t = new TempDir();
        string home = Path.Combine(t.Path, "home");
        Directory.CreateDirectory(home);
        string baseDir = Make(home, "Downloads");
        // applications=false so the destination is under the temp home, never the real /Applications
        bool went = MoveToApplications.Offer(baseDir, home, home, () => true, (_, _, _) => new ProcResult(1, ""),
            _ => throw new InvalidOperationException("nothing starts"), 1, _ => { }, applicationsWritable: false, otherCopyRunning: () => false);
        Assert.False(went);
        Assert.False(Directory.Exists(Path.Combine(home, "Applications", "Syft Notes.app.new")));
    }

    [Fact]
    public void A_good_copy_moves_then_a_waiter_ejects_and_opens_it()
    {
        using var t = new TempDir();
        string home = Path.Combine(t.Path, "home");
        Directory.CreateDirectory(home);
        string baseDir = Make(home, "Downloads");
        IReadOnlyList<string>? spawned = null;
        var ran = new List<string>();
        // a stand-in for ditto (copies the folder) and xattr
        ProcResult? Run(string exe, IReadOnlyList<string> args, TimeSpan _)
        {
            if (exe == "ditto") CopyDir(args[^2], args[^1]);
            ran.Add(string.Join(' ', [exe, .. args]));
            return new ProcResult(0, "");
        }
        bool went = MoveToApplications.Offer(baseDir, home, home, () => true, Run, a => spawned = a, 4242, _ => { }, applicationsWritable: false, otherCopyRunning: () => false);
        Assert.True(went);
        string dest = Path.Combine(home, "Applications", "Syft Notes.app");
        Assert.True(File.Exists(Path.Combine(dest, "Contents", "Info.plist")));
        // Copied without the download's quarantine, which would have Gatekeeper run it from a translocation folder too.
        string source = Path.Combine(home, "Downloads", "Study Stash.app");
        Assert.Equal([$"ditto --noqtn {source} {dest}.new", $"/usr/bin/xattr -dr com.apple.quarantine {dest}.new"], ran.ToArray());
        Assert.Equal(["sh", "4242", dest, ""], spawned!.Skip(3).ToArray());
        Assert.Contains("/usr/bin/caffeinate -w", spawned![2]);
        Assert.DoesNotContain("sleep", spawned[2]);
    }

    [Fact]
    public void One_in_Applications_as_new_or_newer_opens_instead_unless_its_this_copy_translocated()
    {
        if (OperatingSystem.IsWindows()) return; // a Mac's paths and mounts (Linux runs it the same way)
        using var t = new TempDir();
        string home = Path.Combine(t.Path, "home");
        string dest = Path.Combine(home, "Applications", "Syft Notes.app");
        Directory.CreateDirectory(Path.Combine(dest, "Contents"));
        File.WriteAllText(Path.Combine(dest, "Contents", "Info.plist"), Versioned("0.23.4"));
        string mount = "/dev/disk3s1s1 on / (apfs, sealed, local, read-only, journaled)\n";
        IReadOnlyList<string>? spawned = null;
        var ran = new List<string>();
        ProcResult? Run(string exe, IReadOnlyList<string> args, TimeSpan _)
        {
            if (exe == "ditto") CopyDir(args[^2], args[^1]);
            ran.Add(exe);
            return new ProcResult(0, exe == "/sbin/mount" ? mount : "");
        }
        bool Go(string baseDir) => MoveToApplications.Offer(baseDir, home, home, () => true, Run, a => spawned = a, 4242, _ => { },
            applicationsWritable: false, otherCopyRunning: () => false);

        // An older copy kept in Downloads, or the same version on the Desktop: the one in Applications opens, and nothing
        // is asked or copied (an older one would go over a copy that updated itself).
        Assert.True(Go(Make(home, "Downloads", Versioned("0.23.2"))));
        Assert.Equal(["sh", "4242", dest, ""], spawned!.Skip(3).ToArray());
        Assert.True(Go(Make(home, "Desktop", Versioned("0.23.4"))));
        Assert.DoesNotContain("ditto", ran);

        // That copy itself, run from a translocation folder (an earlier move kept the download's quarantine on it): opening
        // it would only come back here, so it moves again, which takes the quarantine off.
        string self = Make(t.Path, "x/AppTranslocation/ABC/d", Versioned("0.23.4"));
        mount += $"{dest} on {Path.Combine(t.Path, "x", "AppTranslocation", "ABC", "d", "Study Stash.app")} (nullfs, local, nodev, nosuid, read-only, nobrowse, mounted by student)\n";
        Assert.True(Go(self));
        Assert.Contains("ditto", ran);
        Assert.Contains("/usr/bin/xattr", ran);
        Assert.Equal(["sh", "4242", dest, ""], spawned!.Skip(3).ToArray());
    }

    /// <summary>The background library, as the move sees it: one registration, and what's asked of it, in order with the
    /// move's own commands.</summary>
    sealed class Background(string home, string program, List<string> asked) : IBackgroundLibrary
    {
        public bool IsRegistered(string h) => h == home;
        public IReadOnlyList<string>? Registered(string h) => h == home ? new[] { program, "--home", home, "helper" } : null;
        public void Register(string h, string p, IReadOnlyList<string>? extra = null) => asked.Add($"register {p}");
        public void Unregister(string h) => asked.Add("unregister");
        public List<(string Home, IReadOnlyList<string> Args)> All() => [(home, new[] { program, "--home", home, "helper" })];
        public void Stop(string h) => asked.Add("stop");
        public void Start(string h) => asked.Add("start");
        public void Restart(string h) => asked.Add("restart");
    }

    [Fact]
    public void A_translocated_copy_puts_away_the_disk_image_its_folder_mirrors_and_the_background_library_comes_back()
    {
        if (OperatingSystem.IsWindows()) return; // a Mac's paths and mounts (Linux runs it the same way)
        using var t = new TempDir();
        string home = Path.Combine(t.Path, "home");
        Directory.CreateDirectory(home);
        // Gatekeeper ran it from a translocation folder, whose path doesn't say where it came from: `mount` says what the
        // folder mirrors, and so which disk image to eject (not an older Syft Notes' mounted beside it).
        string baseDir = Make(t.Path, "x/AppTranslocation/ABC/d", Versioned("0.23.4"));
        string volumes = Path.Combine(t.Path, "Volumes");
        string mount = "/dev/disk3s1s1 on / (apfs, sealed, local, read-only, journaled)\n"
                       + $"/dev/disk5s1 on {volumes}/Syft Notes 1 (hfs, local, nodev, nosuid, read-only, noowners, quarantine, mounted by student)\n"
                       + $"/dev/disk6s1 on {volumes}/Syft Notes (hfs, local, nodev, nosuid, read-only, noowners, quarantine, mounted by student)\n"
                       + $"{volumes}/Syft Notes on {t.Path}/x/AppTranslocation/ABC/d (nullfs, local, nodev, nosuid, read-only, nobrowse, mounted by student)\n";
        string info = $"/dev/disk5\tGUID_partition_scheme\t\n/dev/disk5s1\tApple_HFS\t{volumes}/Syft Notes 1\n"
                      + $"/dev/disk6\tGUID_partition_scheme\t\n/dev/disk6s1\tApple_HFS\t{volumes}/Syft Notes\n";
        // An older copy is in Applications, and the background library runs from it.
        string dest = Path.Combine(home, "Applications", "Syft Notes.app");
        Directory.CreateDirectory(Path.Combine(dest, "Contents"));
        File.WriteAllText(Path.Combine(dest, "Contents", "Info.plist"), Versioned("0.23.2"));
        IReadOnlyList<string>? spawned = null;
        var ran = new List<string>();
        // a stand-in for ditto (copies the folder), xattr, mount and hdiutil
        ProcResult? Run(string exe, IReadOnlyList<string> args, TimeSpan _)
        {
            if (exe == "ditto") CopyDir(args[^2], args[^1]);
            ran.Add(exe);
            return new ProcResult(0, exe == "/sbin/mount" ? mount : exe == "/usr/bin/hdiutil" ? info : "");
        }
        string program = Path.Combine(dest, "Contents", "MacOS", "StudyStash");
        bool went = MoveToApplications.Offer(baseDir, home, home, () => true, Run, a => spawned = a, 4242, _ => { },
            applicationsWritable: false, otherCopyRunning: () => false, background: new Background(home, program, ran));
        Assert.True(went);
        Assert.Equal("0.23.4", Apps.PlistString(Path.Combine(dest, "Contents", "Info.plist"), "CFBundleShortVersionString"));
        // It goes by its label before the bundle under it is replaced, and comes back on the new one.
        Assert.Equal(["stop", "ditto", "/usr/bin/xattr", $"register {program}"], ran.Where(r => r is "stop" or "ditto" or "/usr/bin/xattr" || r.StartsWith("register")).ToArray());
        Assert.Equal(["sh", "4242", dest, $"{volumes}/Syft Notes"], spawned!.Skip(3).ToArray());
        // Opened as a new copy, then the disk image is asked to go a few times (Finder may still have its window open),
        // and made to last.
        Assert.Contains("/usr/bin/open -n \"$2\"", spawned![2]);
        Assert.Contains("-force", spawned![2]);
        Assert.DoesNotContain("sleep", spawned![2]);
    }

    static string Versioned(string version) =>
        Plist.Replace("</dict>", $"<key>CFBundleShortVersionString</key><string>{version}</string></dict>");

    static void CopyDir(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (string f in Directory.GetFiles(from)) File.Copy(f, Path.Combine(to, Path.GetFileName(f)));
        foreach (string d in Directory.GetDirectories(from)) CopyDir(d, Path.Combine(to, Path.GetFileName(d)));
    }

    [Fact]
    public void Nothing_is_offered_while_another_copy_runs()
    {
        using var t = new TempDir();
        string home = Path.Combine(t.Path, "home");
        Directory.CreateDirectory(home);
        string baseDir = Make(home, "Downloads");
        int asked = 0;
        bool went = MoveToApplications.Offer(baseDir, home, home, () => { asked++; return true; }, (_, _, _) => null,
            _ => throw new InvalidOperationException("nothing starts"), 1, _ => { }, applicationsWritable: false, otherCopyRunning: () => true);
        Assert.False(went);
        Assert.Equal(0, asked);
        Assert.False(File.Exists(Path.Combine(home, MoveToApplications.DeclinedFile)));
    }

    [Fact]
    public void Only_a_copy_of_the_app_holds_the_move_off_never_what_AI_apps_and_the_computer_run_from_it()
    {
        // `pgrep -lf` as a Mac printed it: AI apps' MCP servers from the old bundle and the new (one through Claude's own
        // wrapper) and the background library, then the menu bar copy started at login.
        const string engines = "501 /Applications/Study Stash.app/Contents/MacOS/StudyStash mcp --client claude-code\n"
            + "502 /Applications/Claude.app/Contents/Helpers/disclaimer --pgroup -- /Applications/Study Stash.app/Contents/MacOS/StudyStash mcp --client claude-desktop\n"
            + "503 /Applications/Syft Notes.app/Contents/MacOS/StudyStash --home /Users/student/.study-stash helper\n"
            + "504 /Applications/Syft Notes.app/Contents/MacOS/StudyStash mcp --client chatgpt\n";
        const string menuBar = "505 /Applications/Syft Notes.app/Contents/MacOS/StudyStash --background\n";
        bool Running(string pgrep, int pid = 4242) =>
            MoveToApplications.OtherCopyRunning((_, _, _) => new ProcResult(0, pgrep), pid, engine: StudyStash.Library.Cli.IsCommand);
        Assert.False(Running(engines));
        Assert.True(Running(engines + menuBar));
        Assert.False(Running(menuBar, pid: 505)); // this very copy
        // Without the engine's commands to leave out, every program counts (the bundle's rename still waits for them).
        Assert.True(MoveToApplications.OtherCopyRunning((_, _, _) => new ProcResult(0, engines), 4242));
        Assert.False(MoveToApplications.OtherCopyRunning((_, _, _) => new ProcResult(1, ""), 4242, engine: StudyStash.Library.Cli.IsCommand));
    }
}
