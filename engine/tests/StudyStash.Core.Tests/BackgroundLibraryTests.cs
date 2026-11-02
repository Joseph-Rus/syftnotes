namespace StudyStash.Core.Tests;

/// <summary>
/// The background library's registration on each system (<see cref="BackgroundLibrary"/>), with everything that
/// would change the computer standing in: the file it writes, the commands it runs, the Run key value and the program
/// it starts. Nothing here touches this computer's real LaunchAgents, Run key or systemd.
/// </summary>
public class BackgroundLibraryTests
{
    sealed class Stand : IRunKey
    {
        public readonly List<string> Calls = [];
        public readonly Dictionary<string, string> Values = [];
        public readonly List<IReadOnlyList<string>> Started = [];
        public readonly List<int> Killed = [];
        public bool UserSystemd = true;

        public string? Get(string name) => Values.GetValueOrDefault(name);
        public void Set(string name, string value) => Values[name] = value;
        public void Delete(string name) => Values.Remove(name);
        public IEnumerable<string> Names() => Values.Keys.ToList();

        public ProcResult? Run(string exe, IReadOnlyList<string> args, TimeSpan _)
        {
            Calls.Add(string.Join(' ', [exe, .. args]));
            return args is ["--user", "show-environment"] && !UserSystemd ? new ProcResult(1, "") : new ProcResult(0, "");
        }
    }

    static BackgroundLibrary Make(TempDir dir, string system, Stand s) => new()
    {
        System = system, LaunchAgents = dir["agents"], Systemd = dir["systemd"], Autostart = dir["autostart"],
        Run = s.Run, RunKey = s, Spawn = s.Started.Add, Kill = pid => { s.Killed.Add(pid); return true; }, Uid = () => "501",
        DefaultHome = dir["usual home"],
    };

    [Fact]
    public void A_mac_gets_a_launch_agent_that_runs_only_while_the_app_is_there()
    {
        using var dir = new TempDir();
        var s = new Stand();
        var bg = Make(dir, "Darwin", s);
        string home = dir["usual home"], program = "/Applications/Syft Notes.app/Contents/MacOS/StudyStash";

        bg.Register(home, program);

        // Written on a Mac, but on a Windows runner the checkout's line ends are \r\n
        string plist = File.ReadAllText(Path.Combine(dir["agents"], "com.study-stash.background.plist")).ReplaceLineEndings("\n");
        Assert.Contains("<string>/Applications/Syft Notes.app/Contents/MacOS/StudyStash</string>", plist);
        Assert.Contains($"<string>{Path.GetFullPath(home)}</string>\n        <string>helper</string>\n    </array>", plist);
        // Kept alive while its program is there, so an app in the Trash leaves nothing that keeps failing; and never
        // RunAtLoad, which would try once at every login even with the app gone.
        Assert.Contains("<key>PathState</key>\n        <dict>\n            <key>/Applications/Syft Notes.app/Contents/MacOS/StudyStash</key><true/>", plist);
        Assert.DoesNotContain("RunAtLoad", plist);
        if (OperatingSystem.IsMacOS()) Assert.Equal(0, Machine.Run("/usr/bin/plutil", ["-lint", bg.PlistPath(home)], TimeSpan.FromSeconds(20))?.ExitCode);
        Assert.Equal(["launchctl bootstrap gui/501 " + bg.PlistPath(home)], s.Calls);
        Assert.Equal([program, "--home", Path.GetFullPath(home), "helper"], bg.Registered(home));

        // A second folder (the self-test's) gets its own, never the usual one's.
        Assert.StartsWith("com.study-stash.background.", bg.Label(dir["other"]));
        var all = Assert.Single(bg.All());
        Assert.Equal(Path.GetFullPath(home), all.Home);
        Assert.Equal([program, "--home", Path.GetFullPath(home), "helper"], all.Args);

        s.Calls.Clear();
        bg.Unregister(home);
        Assert.Equal(["launchctl bootout gui/501/com.study-stash.background"], s.Calls);
        Assert.False(bg.IsRegistered(home));
        Assert.Empty(Directory.GetFiles(dir["agents"]));
    }

    [Fact]
    public void Windows_gets_a_run_key_value_that_keeps_its_library_going_itself()
    {
        using var dir = new TempDir();
        var s = new Stand();
        var bg = Make(dir, "Windows", s);
        string home = dir["Joey's odd home"], program = @"C:\Users\Joey Q\AppData\Local\Programs\Study Stash\StudyStash.exe";

        bg.Register(home, program, ["--drive-folder", @"C:\temp\drive\"]);

        var (name, value) = Assert.Single(s.Values);
        Assert.StartsWith("Syft Notes background (", name);
        Assert.StartsWith(@"""C:\Users\Joey Q\AppData\Local\Programs\Study Stash\StudyStash.exe"" --home ", value);
        Assert.Contains(@" helper --keep-alive --drive-folder C:\temp\drive\", value);
        // Read back exactly, however its words were quoted; and started now, the same way login starts it.
        List<string> args = [program, "--home", Path.GetFullPath(home), "helper", "--keep-alive", "--drive-folder", @"C:\temp\drive\"];
        Assert.Equal(args, bg.Registered(home));
        Assert.Equal(args, Assert.Single(s.Started));
        Assert.Empty(s.Calls);

        // Stopped by its process id (the one that keeps the library going: the library then stops by itself).
        BackgroundLibrary.WritePids(home, 4242, 4343);
        bg.Unregister(home);
        Assert.Equal([4242], s.Killed);
        Assert.Empty(s.Values);
        Assert.False(File.Exists(BackgroundLibrary.PidPath(home)));
    }

    [Fact]
    public void Linux_gets_a_systemd_user_unit_or_an_autostart_entry_without_one()
    {
        // A unit is only ever written on Linux: a Windows runner's C:\ home would come out in systemd's escaping.
        if (OperatingSystem.IsWindows()) return;
        using var dir = new TempDir();
        var s = new Stand();
        var bg = Make(dir, "Linux", s);
        string home = dir["home"], program = "/home/sam/.local/share/syft-notes/StudyStash";

        bg.Register(home, program);

        string unit = File.ReadAllText(bg.UnitPath(home)).ReplaceLineEndings("\n");
        Assert.Contains($"ConditionPathExists={program}\n", unit);
        Assert.Contains($"ExecStart=\"{program}\" \"--home\" \"{Path.GetFullPath(home)}\" \"helper\"\n", unit);
        Assert.Contains("Restart=on-failure\n", unit);
        Assert.Contains("WantedBy=default.target\n", unit);
        string name = bg.UnitName(home) + ".service";
        Assert.Equal(["systemctl --user show-environment", "systemctl --user daemon-reload", $"systemctl --user enable --now {name}"], s.Calls);
        Assert.Equal("systemd", bg.LinuxWay(home));
        Assert.Equal([program, "--home", Path.GetFullPath(home), "helper"], bg.Registered(home));
        Assert.Empty(s.Started);

        s.Calls.Clear();
        bg.Unregister(home);
        Assert.Equal([$"systemctl --user stop {name}", $"systemctl --user disable {name}", "systemctl --user daemon-reload"], s.Calls);
        Assert.Null(bg.LinuxWay(home));

        // No systemd of the account's own: an autostart entry, and it keeps its library going itself.
        s.UserSystemd = false;
        s.Calls.Clear();
        bg.Register(home, program);
        Assert.Equal("autostart", bg.LinuxWay(home));
        Assert.Contains($"Exec={program} --home {Path.GetFullPath(home)} helper --keep-alive\n", File.ReadAllText(bg.AutostartPath(home)).ReplaceLineEndings("\n"));
        Assert.Equal([program, "--home", Path.GetFullPath(home), "helper", "--keep-alive"], Assert.Single(s.Started));
        BackgroundLibrary.WritePids(home, 777);
        bg.Unregister(home);
        Assert.Equal([777], s.Killed);
        Assert.Null(bg.LinuxWay(home));
    }
}
