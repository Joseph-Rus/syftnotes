using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace StudyStash.Core;

/// <summary>
/// The library run in the background by the computer itself, so the phone is still answered once the app is closed
/// (Settings → Phone's "Answer your phone when Syft Notes is closed"): `StudyStash --home H helper`, the library and
/// the phone's Google Drive link, with no window, no Dock or taskbar icon and no console. It starts when the student
/// logs in and starts again if it stops, on each system the way that system keeps a program running:
/// <list type="bullet">
/// <item>a Mac: a LaunchAgent (~/Library/LaunchAgents) loaded with launchctl, kept alive by launchd only while its
/// program is there, so an app dragged to the Trash leaves nothing that keeps failing;</item>
/// <item>Windows: a value under the Run key, and the helper keeps its library going itself (<c>--keep-alive</c>:
/// Windows has nothing like launchd's KeepAlive for a program in the Run key);</item>
/// <item>Linux: a systemd --user unit (Restart=on-failure), or, where there's no user systemd to talk to, an XDG
/// autostart entry with <c>--keep-alive</c>, as on Windows.</item>
/// </list>
/// What changes the computer (its places, commands, the Run key, starting a program) is handed in, so a test sees the
/// file it would write and the commands it would run, and nothing real is touched.
/// </summary>
public sealed class BackgroundLibrary : IBackgroundLibrary
{
    /// <summary>"Darwin", "Windows" or "Linux".</summary>
    public string System { get; init; } = Machine.Platform;
    /// <summary>~/Library/LaunchAgents (a Mac).</summary>
    public required string LaunchAgents { get; init; }
    /// <summary>~/.config/systemd/user (Linux).</summary>
    public required string Systemd { get; init; }
    /// <summary>~/.config/autostart (Linux, with no user systemd).</summary>
    public required string Autostart { get; init; }
    public required Runner Run { get; init; }
    /// <summary>HKCU's Run key (Windows).</summary>
    public required IRunKey RunKey { get; init; }
    /// <summary>Starts a program that outlives this one, with no window (Windows, and Linux without systemd).</summary>
    public required Action<IReadOnlyList<string>> Spawn { get; init; }
    /// <summary>Ends a process by its id, if it's still one of ours (Windows, and Linux without systemd).</summary>
    public required Func<int, bool> Kill { get; init; }
    /// <summary>The user's id, for launchctl's gui/&lt;uid&gt;.</summary>
    public Func<string> Uid { get; init; } = Machine.Uid;
    /// <summary>The usual home (~/.study-stash): its registration has the plain name.</summary>
    public string DefaultHome { get; init; } = Configs.DefaultHome;

    static readonly TimeSpan Quick = TimeSpan.FromSeconds(30);

    /// <summary>This computer, for real. Only the app makes one, and only when the student turned it on.</summary>
    public static BackgroundLibrary ThisComputer()
    {
        string home = Py.UserHome();
        string config = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") is { Length: > 0 } x ? x : Path.Combine(home, ".config");
        return new BackgroundLibrary
        {
            LaunchAgents = Path.Combine(home, "Library", "LaunchAgents"),
            Systemd = Path.Combine(config, "systemd", "user"),
            Autostart = Path.Combine(config, "autostart"),
            Run = Machine.Run,
            RunKey = OperatingSystem.IsWindows() ? new WindowsRunKey() : new NoRunKey(),
            Spawn = SpawnHidden,
            Kill = KillOurs,
        };
    }

    // --- names ------------------------------------------------------------------------------------------------------

    /// <summary>A short mark of a settings folder that isn't the usual one, so a second profile (the self-test's) gets
    /// a registration of its own and never replaces the real one.</summary>
    string? Mark(string home)
    {
        string full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(home));
        if (string.Equals(full, Path.TrimEndingDirectorySeparator(Path.GetFullPath(DefaultHome)),
                System == "Windows" ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            return null;
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(full)))[..8];
    }

    /// <summary>The LaunchAgent's label: com.study-stash.background (the app's own is com.study-stash.app).</summary>
    public string Label(string home) => Mark(home) is { } m ? $"com.study-stash.background.{m}" : "com.study-stash.background";

    /// <summary>The systemd unit's (and the autostart entry's) name, without its ending.</summary>
    public string UnitName(string home) => Mark(home) is { } m ? $"syft-notes-background-{m}" : "syft-notes-background";

    /// <summary>The Run key's value name.</summary>
    public string RunValue(string home) => Mark(home) is { } m ? $"Syft Notes background ({m})" : "Syft Notes background";

    public const string RunValuePrefix = "Syft Notes background";

    public string PlistPath(string home) => Path.Combine(LaunchAgents, Label(home) + ".plist");
    public string UnitPath(string home) => Path.Combine(Systemd, UnitName(home) + ".service");
    public string AutostartPath(string home) => Path.Combine(Autostart, UnitName(home) + ".desktop");

    /// <summary>Where the background library writes its own process id (and, kept alive by itself, its library's).</summary>
    public static string PidPath(string home) => Path.Combine(home, "background.pid");

    /// <summary>What the computer runs: the program, its folder, and `helper`; on Windows and Linux's autostart, also
    /// <c>--keep-alive</c>. <paramref name="extra"/> is the self-test's stand-in folder for Google Drive.</summary>
    public static List<string> Command(string program, string home, bool keepAlive, IReadOnlyList<string>? extra = null) =>
        [program, "--home", Path.GetFullPath(home), "helper", .. keepAlive ? new[] { "--keep-alive" } : [], .. extra ?? []];

    // --- the files ----------------------------------------------------------------------------------------------------

    static string Xml(string s) => s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");

    /// <summary>The LaunchAgent. No RunAtLoad: KeepAlive's PathState starts it at login and after a crash, but only
    /// while its program is there, so once the app is in the Trash launchd quietly leaves it be.</summary>
    public static string Plist(string label, IReadOnlyList<string> args, string userHome)
    {
        string items = string.Concat(args.Select(a => $"\n        <string>{Xml(a)}</string>"));
        return $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
            <plist version="1.0">
            <dict>
                <key>Label</key><string>{Xml(label)}</string>
                <key>ProgramArguments</key>
                <array>{items}
                </array>
                <key>KeepAlive</key>
                <dict>
                    <key>PathState</key>
                    <dict>
                        <key>{Xml(args[0])}</key><true/>
                    </dict>
                </dict>
                <key>ThrottleInterval</key><integer>10</integer>
                <key>ProcessType</key><string>Standard</string>
                <key>EnvironmentVariables</key>
                <dict>
                    <key>PATH</key><string>/usr/local/bin:/opt/homebrew/bin:/usr/bin:/bin:/usr/sbin:/sbin:{Xml(userHome)}/.local/bin</string>
                </dict>
            </dict>
            </plist>

            """.ReplaceLineEndings("\n");
    }

    /// <summary>systemd's quoting for ExecStart: each word in double quotes, with \ and " escaped, and $ and % doubled
    /// so neither is read as a variable or a specifier.</summary>
    static string SystemdWord(string a) => "\"" + a.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("$", "$$").Replace("%", "%%") + "\"";

    /// <summary>The systemd --user unit: it doesn't start when its program is gone (uninstalled without
    /// uninstall.sh), and starts again 10 s after it stops unexpectedly.</summary>
    public static string Unit(IReadOnlyList<string> args) =>
        $"""
        [Unit]
        Description=Syft Notes: answers your phone while the app is closed
        ConditionPathExists={args[0].Replace("%", "%%")}

        [Service]
        ExecStart={string.Join(' ', args.Select(SystemdWord))}
        Environment=PATH=%h/.local/bin:/usr/local/bin:/usr/bin:/bin
        Restart=on-failure
        RestartSec=10

        [Install]
        WantedBy=default.target

        """.ReplaceLineEndings("\n");

    /// <summary>Linux with no user systemd: the autostart entry every desktop starts at login, quoted as the Desktop
    /// Entry spec says.</summary>
    public static string AutostartEntry(IReadOnlyList<string> args)
    {
        static string Quote(string a) => a.Length > 0 && a.All(c => char.IsLetterOrDigit(c) || "-_./=+:,@".Contains(c))
            ? a : "\"" + string.Concat(a.Select(c => c is '"' or '`' or '$' or '\\' ? "\\" + c : c == '%' ? "%%" : c.ToString())) + "\"";
        return $"""
            [Desktop Entry]
            Type=Application
            Name=Syft Notes (background)
            Comment=Answers your phone while Syft Notes is closed
            Exec={string.Join(' ', args.Select(Quote))}
            Terminal=false
            NoDisplay=true
            X-GNOME-Autostart-enabled=true

            """.ReplaceLineEndings("\n");
    }

    /// <summary>The Run key's command line: each word quoted the way Windows splits a command line.</summary>
    public static string RunCommand(IReadOnlyList<string> args) => string.Join(' ', args.Select(QuoteWindows));

    static string QuoteWindows(string a)
    {
        if (a.Length > 0 && !a.Any(c => c is ' ' or '\t' or '"')) return a;
        var b = new StringBuilder("\"");
        int slashes = 0;
        foreach (char c in a)
        {
            if (c == '\\')
            {
                slashes++;
                continue;
            }
            b.Append('\\', c == '"' ? slashes * 2 + 1 : slashes);
            b.Append(c);
            slashes = 0;
        }
        b.Append('\\', slashes * 2);
        return b.Append('"').ToString();
    }

    // --- what's registered --------------------------------------------------------------------------------------------

    /// <summary>Which way Linux registered it: "systemd", "autostart", or null when it isn't.</summary>
    public string? LinuxWay(string home) => File.Exists(UnitPath(home)) ? "systemd" : File.Exists(AutostartPath(home)) ? "autostart" : null;

    public bool IsRegistered(string home) => System switch
    {
        "Darwin" => File.Exists(PlistPath(home)),
        "Windows" => RunKey.Get(RunValue(home)) is not null,
        _ => LinuxWay(home) is not null,
    };

    /// <summary>What the registration runs: its program and arguments, or null when there's none.</summary>
    public IReadOnlyList<string>? Registered(string home)
    {
        try
        {
            return System switch
            {
                "Darwin" when File.Exists(PlistPath(home)) => PlistArgs(File.ReadAllText(PlistPath(home))),
                "Windows" when RunKey.Get(RunValue(home)) is { } v => SplitWindows(v),
                "Linux" when File.Exists(UnitPath(home)) => UnitArgs(File.ReadAllText(UnitPath(home))),
                "Linux" when File.Exists(AutostartPath(home)) => AutostartArgs(File.ReadAllText(AutostartPath(home))),
                _ => null,
            };
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Every background library registered on this computer, by its folder: the update stops each one running
    /// this copy's program before swapping it, and starts it again on the new one.</summary>
    public List<(string Home, IReadOnlyList<string> Args)> All()
    {
        var found = new List<IReadOnlyList<string>?>();
        try
        {
            if (System == "Darwin" && Directory.Exists(LaunchAgents))
                found.AddRange(Directory.EnumerateFiles(LaunchAgents, "com.study-stash.background*.plist").Select(f => PlistArgs(File.ReadAllText(f))));
            else if (System == "Windows")
                found.AddRange(RunKey.Names().Where(n => n.StartsWith(RunValuePrefix, StringComparison.Ordinal)).Select(n => RunKey.Get(n) is { } v ? SplitWindows(v) : null));
            else
            {
                if (Directory.Exists(Systemd))
                    found.AddRange(Directory.EnumerateFiles(Systemd, "syft-notes-background*.service").Select(f => UnitArgs(File.ReadAllText(f))));
                if (Directory.Exists(Autostart))
                    found.AddRange(Directory.EnumerateFiles(Autostart, "syft-notes-background*.desktop").Select(f => AutostartArgs(File.ReadAllText(f))));
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
        return [.. found.OfType<IReadOnlyList<string>>().Where(a => a.Count >= 4 && a[1] == "--home").Select(a => (a[2], a))];
    }

    static List<string> PlistArgs(string plist)
    {
        var array = Regex.Match(plist, @"<key>ProgramArguments</key>\s*<array>(.*?)</array>", RegexOptions.Singleline);
        return [.. Regex.Matches(array.Groups[1].Value, "<string>(.*?)</string>", RegexOptions.Singleline)
            .Select(m => m.Groups[1].Value.Replace("&quot;", "\"").Replace("&lt;", "<").Replace("&gt;", ">").Replace("&amp;", "&"))];
    }

    static List<string>? UnitArgs(string unit)
    {
        var line = unit.Split('\n').FirstOrDefault(l => l.StartsWith("ExecStart=", StringComparison.Ordinal));
        if (line is null) return null;
        return [.. Regex.Matches(line["ExecStart=".Length..], @"""((?:\\.|[^""\\])*)""")
            .Select(m => Regex.Replace(m.Groups[1].Value, @"\\(.)", "$1").Replace("$$", "$").Replace("%%", "%"))];
    }

    static List<string>? AutostartArgs(string entry)
    {
        var line = entry.Split('\n').FirstOrDefault(l => l.StartsWith("Exec=", StringComparison.Ordinal));
        if (line is null) return null;
        return [.. Regex.Matches(line["Exec=".Length..], @"""((?:\\.|[^""\\])*)""|(\S+)")
            .Select(m => m.Groups[1].Success ? Regex.Replace(m.Groups[1].Value, @"\\(.)", "$1").Replace("%%", "%") : m.Groups[2].Value)];
    }

    /// <summary>A Windows command line split into its words (CommandLineToArgvW's rules, for what <see cref="RunCommand"/>
    /// writes).</summary>
    static List<string> SplitWindows(string line)
    {
        var words = new List<string>();
        var w = new StringBuilder();
        bool quoted = false, any = false;
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (c == '\\')
            {
                int n = 0;
                while (i < line.Length && line[i] == '\\') { n++; i++; }
                if (i < line.Length && line[i] == '"')
                {
                    w.Append('\\', n / 2);
                    if (n % 2 == 1) w.Append('"');
                    else { quoted = !quoted; any = true; }
                }
                else
                {
                    w.Append('\\', n);
                    i--;
                }
            }
            else if (c == '"')
            {
                quoted = !quoted;
                any = true;
            }
            else if (c is ' ' or '\t' && !quoted)
            {
                if (w.Length > 0 || any) words.Add(w.ToString());
                w.Clear();
                any = false;
            }
            else
            {
                w.Append(c);
            }
        }
        if (w.Length > 0 || any) words.Add(w.ToString());
        return words;
    }

    // --- registering, starting and stopping -------------------------------------------------------------------------

    bool Ok(string exe, params string[] args) => Run(exe, args, Quick) is { ExitCode: 0 };

    string Target => $"gui/{Uid()}";

    /// <summary>Linux: whether this account has a systemd of its own to talk to (a desktop login; not every container
    /// or minimal session does).</summary>
    bool UserSystemd() => Ok("systemctl", "--user", "show-environment");

    /// <summary>Registers the background library for <paramref name="home"/> and starts it now, replacing a
    /// registration of an older copy (the one already running is stopped first, so two never fight over the port).</summary>
    public void Register(string home, string program, IReadOnlyList<string>? extra = null)
    {
        if (IsRegistered(home)) Unregister(home);
        Directory.CreateDirectory(Path.Combine(home, "logs"));
        if (System == "Darwin")
        {
            var args = Command(program, home, keepAlive: false, extra);
            Directory.CreateDirectory(LaunchAgents);
            Py.WriteText(PlistPath(home), Plist(Label(home), args, Py.UserHome()));
            Run("launchctl", ["bootstrap", Target, PlistPath(home)], Quick);
        }
        else if (System == "Windows")
        {
            var args = Command(program, home, keepAlive: true, extra);
            RunKey.Set(RunValue(home), RunCommand(args));
            Spawn(args);
        }
        else if (UserSystemd())
        {
            var args = Command(program, home, keepAlive: false, extra);
            Directory.CreateDirectory(Systemd);
            Py.WriteText(UnitPath(home), Unit(args));
            Run("systemctl", ["--user", "daemon-reload"], Quick);
            Run("systemctl", ["--user", "enable", "--now", UnitName(home) + ".service"], Quick);
        }
        else
        {
            var args = Command(program, home, keepAlive: true, extra);
            Directory.CreateDirectory(Autostart);
            Py.WriteText(AutostartPath(home), AutostartEntry(args));
            Spawn(args);
        }
    }

    /// <summary>Stops it and takes the registration away: nothing of it is left to start at login.</summary>
    public void Unregister(string home)
    {
        Stop(home);
        if (System == "Darwin")
        {
            if (File.Exists(PlistPath(home))) File.Delete(PlistPath(home));
        }
        else if (System == "Windows")
        {
            RunKey.Delete(RunValue(home));
        }
        else
        {
            if (File.Exists(UnitPath(home)))
            {
                Run("systemctl", ["--user", "disable", UnitName(home) + ".service"], Quick);
                File.Delete(UnitPath(home));
                Run("systemctl", ["--user", "daemon-reload"], Quick);
            }
            if (File.Exists(AutostartPath(home))) File.Delete(AutostartPath(home));
        }
    }

    /// <summary>Stops it for now, its registration kept (an update replacing its program, or before a restart). The
    /// library it runs gets launchd's or systemd's SIGTERM, or, kept alive by itself, sees its input end: either way a
    /// clean stop.</summary>
    public void Stop(string home)
    {
        if (System == "Darwin")
        {
            Run("launchctl", ["bootout", $"{Target}/{Label(home)}"], Quick);
        }
        else if (System == "Linux" && File.Exists(UnitPath(home)))
        {
            Run("systemctl", ["--user", "stop", UnitName(home) + ".service"], Quick);
        }
        else if (ReadPids(home) is { Count: > 0 } pids)
        {
            // The one that keeps the library going goes first, so it doesn't start it again; its library then sees its
            // input end and stops by itself.
            Kill(pids[0]);
            try
            {
                File.Delete(PidPath(home));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    /// <summary>Starts a registered one that isn't running (after an update, or the app found it stopped).</summary>
    public void Start(string home)
    {
        var args = Registered(home);
        if (args is null) return;
        if (System == "Darwin")
        {
            if (!Ok("launchctl", "kickstart", $"{Target}/{Label(home)}")) Run("launchctl", ["bootstrap", Target, PlistPath(home)], Quick);
        }
        else if (System == "Linux" && File.Exists(UnitPath(home)))
        {
            Run("systemctl", ["--user", "start", UnitName(home) + ".service"], Quick);
        }
        else
        {
            Spawn(args);
        }
    }

    /// <summary>Stops it and starts it again (the library's settings changed: a new password, or laptops let in).</summary>
    public void Restart(string home)
    {
        if (System == "Darwin" && Ok("launchctl", "kickstart", "-k", $"{Target}/{Label(home)}")) return;
        if (System == "Linux" && File.Exists(UnitPath(home)))
        {
            Run("systemctl", ["--user", "restart", UnitName(home) + ".service"], Quick);
            return;
        }
        Stop(home);
        Start(home);
    }

    // --- the process ids the background library writes ---------------------------------------------------------------

    /// <summary>The background library's own id, then (kept alive by itself) its library's.</summary>
    public static List<int> ReadPids(string home)
    {
        try
        {
            return [.. File.ReadAllText(PidPath(home)).Split((char[])[' ', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries)
                .Select(s => int.TryParse(s, out int n) ? n : 0).Where(n => n > 0)];
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    public static void WritePids(string home, params int[] pids)
    {
        try
        {
            File.WriteAllText(PidPath(home), string.Join(' ', pids));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>Starts it with no window and nothing of this program's: Windows through the shell, hidden (what
    /// Explorer does with the Run key at login); Linux through sh, in the background with its input and output let go,
    /// so nothing waiting on this program's output waits on it too.</summary>
    static void SpawnHidden(IReadOnlyList<string> args)
    {
        ProcessStartInfo psi;
        if (OperatingSystem.IsWindows())
        {
            psi = new ProcessStartInfo(args[0]) { UseShellExecute = true, WindowStyle = ProcessWindowStyle.Hidden };
            foreach (string a in args.Skip(1)) psi.ArgumentList.Add(a);
        }
        else
        {
            psi = new ProcessStartInfo("/bin/sh") { UseShellExecute = false, CreateNoWindow = true };
            foreach (string a in (string[])["-c", "\"$@\" </dev/null >/dev/null 2>&1 &", "sh", .. args]) psi.ArgumentList.Add(a);
            psi.Environment.Remove("STUDYSTASH_SELFTEST");
        }
        using var p = Process.Start(psi);
        if (!OperatingSystem.IsWindows()) p?.WaitForExit(TimeSpan.FromSeconds(10));
    }

    /// <summary>Ends a process, if it's still a Syft Notes one (a process id is reused once its owner is gone).</summary>
    static bool KillOurs(int pid)
    {
        try
        {
            using var p = Process.GetProcessById(pid);
            if (!p.ProcessName.StartsWith("StudyStash", StringComparison.OrdinalIgnoreCase)) return false;
            p.Kill();
            return true;
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or global::System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }
}

/// <summary>The background library as the app sees it: <see cref="BackgroundLibrary"/> is the real thing; a test
/// gives its own and watches what it's asked to do.</summary>
public interface IBackgroundLibrary
{
    bool IsRegistered(string home);
    /// <summary>The program and arguments the registration runs, or null when there's none.</summary>
    IReadOnlyList<string>? Registered(string home);
    void Register(string home, string program, IReadOnlyList<string>? extra = null);
    void Unregister(string home);
    /// <summary>Every one registered on this computer, by its folder, with what it runs.</summary>
    List<(string Home, IReadOnlyList<string> Args)> All();
    void Stop(string home);
    void Start(string home);
    void Restart(string home);
}

/// <summary>HKCU\Software\Microsoft\Windows\CurrentVersion\Run, as <see cref="BackgroundLibrary"/> uses it.</summary>
public interface IRunKey
{
    string? Get(string name);
    void Set(string name, string value);
    void Delete(string name);
    IEnumerable<string> Names();
}

sealed class NoRunKey : IRunKey
{
    public string? Get(string name) => null;
    public void Set(string name, string value) => throw new PlatformNotSupportedException("The Run key is Windows'.");
    public void Delete(string name) { }
    public IEnumerable<string> Names() => [];
}

[global::System.Runtime.Versioning.SupportedOSPlatform("windows")]
sealed class WindowsRunKey : IRunKey
{
    const string Key = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public string? Get(string name)
    {
        using var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(Key);
        return k?.GetValue(name) as string;
    }

    public void Set(string name, string value)
    {
        using var k = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(Key);
        k.SetValue(name, value);
    }

    public void Delete(string name)
    {
        using var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(Key, writable: true);
        k?.DeleteValue(name, throwOnMissingValue: false);
    }

    public IEnumerable<string> Names()
    {
        using var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(Key);
        return k?.GetValueNames() ?? [];
    }
}
