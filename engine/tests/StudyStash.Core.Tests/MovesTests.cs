namespace StudyStash.Core.Tests;

/// <summary>What still names the app's program from before it moved (Moves.cs), and what is left alone.</summary>
public class MovesTests
{
    [Fact]
    public void Only_our_program_in_an_old_place_or_gone_counts_as_moved()
    {
        string now = OperatingSystem.IsWindows() ? @"C:\Users\sam\AppData\Local\SyftNotes\current\StudyStash.exe" : "/Applications/Syft Notes.app/Contents/MacOS/StudyStash";
        bool Exists(string _) => true;
        // The old places, even while the old file is still there.
        Assert.True(Moves.OldProgram(@"C:\Users\sam\AppData\Local\Programs\Study Stash\StudyStash.exe", now, Exists));
        Assert.True(Moves.OldProgram("/Applications/Study Stash.app/Contents/MacOS/StudyStash", now, Exists));
        Assert.True(Moves.OldProgram("/home/sam/.local/share/syft-notes/StudyStash", now, Exists));
        // Ours by name and gone: a copy that was moved or deleted.
        Assert.True(Moves.OldProgram("/somewhere/gone/StudyStash", now, _ => false));
        // Left alone: this program, another copy that's still there (a build folder), and someone else's program.
        Assert.False(Moves.OldProgram(now, now, _ => false));
        Assert.False(Moves.OldProgram("/Users/sam/dev/bin/StudyStash", now, Exists));
        Assert.False(Moves.OldProgram("/usr/local/bin/node", now, _ => false));
        Assert.False(Moves.OldProgram(null, now));
    }

    [Fact]
    public void An_older_copy_of_the_Mac_app_kept_elsewhere_counts_as_moved_but_a_newer_equal_or_other_app_does_not()
    {
        using var t = new TempDir();
        // The program inside a fake bundle: Info.plist with an id and a version.
        string Bundle(string folder, string name, string id, string version)
        {
            string contents = Path.Combine(t.Path, folder, name, "Contents");
            Directory.CreateDirectory(Path.Combine(contents, "MacOS"));
            File.WriteAllText(Path.Combine(contents, "Info.plist"), $"""
                <?xml version="1.0" encoding="UTF-8"?>
                <plist version="1.0"><dict><key>CFBundleIdentifier</key><string>{id}</string>
                <key>CFBundleShortVersionString</key><string>{version}</string></dict></plist>
                """);
            return Path.Combine(contents, "MacOS", "StudyStash");
        }
        const string ours = "com.study-stash.app";
        bool Exists(string _) => true;
        string now = Bundle("Applications", "Syft Notes.app", ours, "0.23.4");
        // Older, wherever it sits: ~/Applications, Finder's "Keep Both" copy, Downloads.
        Assert.True(Moves.OldProgram(Bundle("home/Applications", "Syft Notes.app", ours, "0.23.3"), now, Exists));
        Assert.True(Moves.OldProgram(Bundle("Applications", "Syft Notes 2.app", ours, "0.9.2"), now, Exists));
        Assert.True(Moves.OldProgram(Bundle("home/Downloads", "Syft Notes.app", ours, "0.22.0"), now, Exists));
        // Left alone: newer, equal, another app's, and this one.
        Assert.False(Moves.OldProgram(Bundle("home/Applications", "Newer.app", ours, "0.24.0"), now, Exists));
        Assert.False(Moves.OldProgram(Bundle("home/Applications", "Same.app", ours, "0.23.4"), now, Exists));
        Assert.False(Moves.OldProgram(Bundle("home/Applications", "Other.app", "com.example.other", "0.1.0"), now, Exists));
        Assert.False(Moves.OldProgram(now, now, Exists));
    }

    [Fact]
    public void Only_Inno_Setups_own_folder_for_the_app_counts_as_the_old_install()
    {
        string temp = Path.Combine(Path.GetTempPath(), "studystash-moves-" + Guid.NewGuid().ToString("N")[..8]);
        string old = Path.Combine(temp, "Programs", "Study Stash");
        string running = Path.Combine(temp, "SyftNotes", "current");
        try
        {
            Directory.CreateDirectory(old);
            File.WriteAllText(Path.Combine(old, "StudyStash.exe"), "");
            // Without Inno's uninstall data it's someone's own copy, not the install.
            Assert.False(Moves.OldInstallFolder(old, running));
            File.WriteAllText(Path.Combine(old, "unins000.dat"), "");
            Assert.True(Moves.OldInstallFolder(old, running));
            Assert.True(Moves.OldInstallFolder(old + Path.DirectorySeparatorChar, running));
            // Never a drive's root, the running copy's folder, a folder holding it, any other folder, or a relative path.
            Assert.False(Moves.OldInstallFolder(Path.GetPathRoot(temp), running));
            Assert.False(Moves.OldInstallFolder(old, old));
            Assert.False(Moves.OldInstallFolder(old, Path.Combine(old, "current")));
            Assert.False(Moves.OldInstallFolder(Path.Combine(temp, "Programs"), running));
            Assert.False(Moves.OldInstallFolder(temp, running));
            Assert.False(Moves.OldInstallFolder(Path.Combine("Programs", "Study Stash"), running));
            Assert.False(Moves.OldInstallFolder("", running));
            // Already gone (an earlier start deleted it but couldn't forget its entry): still the old install.
            Assert.True(Moves.OldInstallFolder(Path.Combine(temp, "gone", "Programs", "Study Stash"), running));
        }
        finally
        {
            Directory.Delete(temp, recursive: true);
        }
    }

    [Fact]
    public void A_link_in_the_old_install_goes_as_a_link_and_an_install_that_is_a_link_is_left_alone()
    {
        string temp = Path.Combine(Path.GetTempPath(), "studystash-moves-" + Guid.NewGuid().ToString("N")[..8]);
        static void Install(string dir)
        {
            Directory.CreateDirectory(dir);
            foreach (string file in (string[])["StudyStash.exe", "unins000.exe", "unins000.dat", "Avalonia.dll"]) File.WriteAllText(Path.Combine(dir, file), "");
        }
        // A folder link: a junction on Windows (no special rights needed, unlike a symbolic link), else a symbolic link.
        static void Link(string link, string target)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(link)!);
            if (!OperatingSystem.IsWindows())
            {
                Directory.CreateSymbolicLink(link, target);
                return;
            }
            using var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe", $"/c mklink /J \"{link}\" \"{target}\"")
                { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true })!;
            Assert.True(p.WaitForExit(30_000) && p.ExitCode == 0, "mklink /J failed");
        }
        string linked = Path.Combine(temp, "links", "Programs", "Study Stash");
        try
        {
            string outside = Path.Combine(temp, "outside");
            Directory.CreateDirectory(outside);
            File.WriteAllText(Path.Combine(outside, "keep.txt"), "not the app's");
            string old = Path.Combine(temp, "Programs", "Study Stash");
            Install(old);
            Link(Path.Combine(old, "runtimes", "models"), outside);

            Assert.Null(Moves.DeleteOldInstall(old));
            Assert.False(Directory.Exists(old));
            Assert.True(File.Exists(Path.Combine(outside, "keep.txt")));

            // The install itself a link (moved to another drive): left alone, with everything it points at.
            string moved = Path.Combine(temp, "D", "Study Stash");
            Install(moved);
            Link(linked, moved);
            Assert.True(Moves.OldInstallFolder(linked, Path.Combine(temp, "SyftNotes")));
            Assert.Equal(linked, Moves.DeleteOldInstall(linked));
            Assert.True(File.Exists(Path.Combine(moved, "Avalonia.dll")) && File.Exists(Path.Combine(linked, "StudyStash.exe")));
        }
        finally
        {
            if (Directory.Exists(linked)) Directory.Delete(linked); // the link alone: Windows won't delete through a junction
            Directory.Delete(temp, recursive: true);
        }
    }

    [Fact]
    public void Reads_the_program_a_Run_value_a_LaunchAgent_and_a_desktop_entry_start()
    {
        Assert.Equal(@"C:\Program Files\Study Stash\StudyStash.exe", Moves.RunProgram(@"""C:\Program Files\Study Stash\StudyStash.exe"" --background"));
        Assert.Equal(@"C:\x\StudyStash.exe", Moves.RunProgram(@"C:\x\StudyStash.exe --background"));
        Assert.Equal("/Applications/Study Stash.app/Contents/MacOS/StudyStash", Moves.PlistProgram(
            "<key>Label</key><string>com.study-stash.app</string><key>ProgramArguments</key><array><string>/Applications/Study Stash.app/Contents/MacOS/StudyStash</string><string>--background</string></array>"));
        Assert.Equal("/home/sam/My \"Apps\"/StudyStash", Moves.ExecProgram("[Desktop Entry]\nExec=\"/home/sam/My \\\"Apps\\\"/StudyStash\" --background\n"));
        Assert.Equal("/home/sam/StudyStash", Moves.ExecProgram("Exec=/home/sam/StudyStash --background"));
    }
}
