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
        using var t = new TempDir();
        string home = Path.Combine(t.Path, "home");
        string vols = Path.Combine(t.Path, "Volumes");
        var dmg = MoveToApplications.Plan(Make(vols, "Syft Notes"), home, true, vols);
        Assert.Equal("/Applications/Study Stash.app", dmg!.Dest);
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
        Assert.Equal(Path.Combine(home, "Applications", "Study Stash.app"), plan!.Dest);
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
    public void Not_now_is_remembered_and_nothing_is_asked_twice()
    {
        using var t = new TempDir();
        string home = Path.Combine(t.Path, "home");
        Directory.CreateDirectory(home);
        string baseDir = Make(home, "Downloads");
        int asked = 0;
        bool Go() => MoveToApplications.Offer(baseDir, home, home, () => { asked++; return false; },
            (_, _, _) => null, _ => throw new InvalidOperationException("nothing starts"), 1, _ => { }, applicationsWritable: true);
        Assert.False(Go());
        Assert.False(Go());
        Assert.Equal(1, asked);
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
            _ => throw new InvalidOperationException("nothing starts"), 1, _ => { }, applicationsWritable: false);
        Assert.False(went);
        Assert.False(Directory.Exists(Path.Combine(home, "Applications", "Study Stash.app.new")));
    }

    [Fact]
    public void A_good_copy_moves_then_a_waiter_ejects_and_opens_it()
    {
        using var t = new TempDir();
        string home = Path.Combine(t.Path, "home");
        Directory.CreateDirectory(home);
        string baseDir = Make(home, "Downloads");
        IReadOnlyList<string>? spawned = null;
        // a stand-in for ditto: copies the folder
        ProcResult? Run(string exe, IReadOnlyList<string> args, TimeSpan _)
        {
            if (exe == "ditto") CopyDir(args[0], args[1]);
            return new ProcResult(0, "");
        }
        bool went = MoveToApplications.Offer(baseDir, home, home, () => true, Run, a => spawned = a, 4242, _ => { }, applicationsWritable: false);
        Assert.True(went);
        string dest = Path.Combine(home, "Applications", "Study Stash.app");
        Assert.True(File.Exists(Path.Combine(dest, "Contents", "Info.plist")));
        Assert.Equal(["sh", "4242", dest, ""], spawned!.Skip(3).ToArray());
    }

    static void CopyDir(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (string f in Directory.GetFiles(from)) File.Copy(f, Path.Combine(to, Path.GetFileName(f)));
        foreach (string d in Directory.GetDirectories(from)) CopyDir(d, Path.Combine(to, Path.GetFileName(d)));
    }
}
