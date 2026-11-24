using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using Avalonia.Threading;
using Microsoft.Win32;
using StudyStash.Core;

namespace StudyStash.App.Platform;

/// <summary>The desktop around the app: the Dock (a Mac shows the app there only while a window is open), starting at
/// login (a LaunchAgent, the Run key, or an XDG autostart entry on Linux), the taskbar button's progress (Windows), and
/// one copy of the app at a time.</summary>
public static class Desktop
{
    // --- the Mac's Dock and app activation --------------------------------------------------------------------------

    [SupportedOSPlatform("macos")]
    static class ObjC
    {
        const string Lib = "/usr/lib/libobjc.A.dylib";

        [DllImport(Lib)]
        public static extern IntPtr objc_getClass(string name);

        [DllImport(Lib)]
        public static extern IntPtr sel_registerName(string name);

        [DllImport(Lib, EntryPoint = "objc_msgSend")]
        public static extern IntPtr Send(IntPtr receiver, IntPtr selector);

        [DllImport(Lib, EntryPoint = "objc_msgSend")]
        public static extern bool SendLong(IntPtr receiver, IntPtr selector, long arg);

        [DllImport(Lib, EntryPoint = "objc_msgSend")]
        public static extern void SendBool(IntPtr receiver, IntPtr selector, bool arg);

        [DllImport(Lib, EntryPoint = "objc_msgSend")]
        public static extern IntPtr SendCode(IntPtr receiver, IntPtr selector, uint code);

        public static IntPtr App => Send(objc_getClass("NSApplication"), sel_registerName("sharedApplication"));
    }

    /// <summary>A Mac: show the app in the Dock and the ⌘Tab switcher (a window is open), or keep it to the menu bar.</summary>
    public static void ShowInDock(bool show)
    {
        if (!OperatingSystem.IsMacOS()) return;
        try
        {
            ObjC.SendLong(ObjC.App, ObjC.sel_registerName("setActivationPolicy:"), show ? 0 : 1); // regular : accessory
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
        {
        }
    }

    /// <summary>The system asking the app to quit is the student's own Quit (⌘Q, the app menu's, the Dock's), not the
    /// computer logging out, restarting or shutting down: only a Mac's Quit menu and Dock ask that way, and a log out's
    /// quit event says why it came ('why?', which a student's has not). Windows asks only as the session ends.</summary>
    public static bool StudentQuits()
    {
        if (!OperatingSystem.IsMacOS()) return false;
        try
        {
            IntPtr events = ObjC.Send(ObjC.objc_getClass("NSAppleEventManager"), ObjC.sel_registerName("sharedAppleEventManager"));
            IntPtr quit = events == IntPtr.Zero ? IntPtr.Zero : ObjC.Send(events, ObjC.sel_registerName("currentAppleEvent"));
            if (quit == IntPtr.Zero) return true;
            return ObjC.SendCode(quit, ObjC.sel_registerName("attributeDescriptorForKeyword:"), 0x7768793F /* 'why?' keyAEQuitReason */) == IntPtr.Zero;
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
        {
            return true;
        }
    }

    /// <summary>A Mac: bring the app forward, so the window it opened is in front of the one you were in.</summary>
    public static void Activate()
    {
        if (!OperatingSystem.IsMacOS()) return;
        try
        {
            ObjC.SendBool(ObjC.App, ObjC.sel_registerName("activateIgnoringOtherApps:"), true);
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
        {
        }
    }

    /// <summary>A Mac: step back as if the student clicked into another app (the self-test's way of leaving the
    /// library for a class's slides).</summary>
    public static void Deactivate()
    {
        if (!OperatingSystem.IsMacOS()) return;
        try
        {
            ObjC.Send(ObjC.App, ObjC.sel_registerName("deactivate"));
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
        {
        }
    }

    // --- starting at login -------------------------------------------------------------------------------------------

    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    /// <summary>Tests and the self-test: nothing here changes this computer. Starting at login refuses, and whether it
    /// starts at login reads "no" without looking. On when STUDYSTASH_SELFTEST is set; the app's tests turn it on as
    /// they load.</summary>
    internal static bool SystemChangesOff { get; set; } = StudyStash.Core.TestSwitches.Get("STUDYSTASH_SELFTEST") is { Length: > 0 };

    /// <summary>The program to start: the app bundle's executable (a Mac), StudyStash.exe, or on Linux the AppImage
    /// itself ($APPIMAGE), never the folder it's mounted on while it runs, which is gone once it quits.</summary>
    public static string Program => OperatingSystem.IsLinux() && Environment.GetEnvironmentVariable("APPIMAGE") is { Length: > 0 } image
        ? image : Environment.ProcessPath ?? "StudyStash";

    /// <summary>The login item's name for a settings folder: the usual folder is "com.study-stash.app" (a LaunchAgent)
    /// and "Study Stash" (the Run key); any other folder gets its own, so a second profile never replaces the real one.</summary>
    internal static (string Label, string RunValue) LoginName(string home)
    {
        if (SameFolder(home, Configs.DefaultHome)) return ("com.study-stash.app", "Study Stash");
        string hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(home))))[..8];
        return ($"com.study-stash.app.{hash}", $"Study Stash ({hash})");
    }

    internal static bool SameFolder(string a, string b) =>
        string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(a)), Path.TrimEndingDirectorySeparator(Path.GetFullPath(b)),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    /// <summary>What starts the app at login: quietly (--background), and with its folder when it isn't the usual one.</summary>
    internal static List<string> LoginArgs(string program, string home)
    {
        var args = new List<string> { program, "--background" };
        if (!SameFolder(home, Configs.DefaultHome)) args.AddRange(["--home", home]);
        return args;
    }

    /// <summary>The Run key's command that starts the app when you log in to Windows.</summary>
    internal static string RunCommand(string program, string home) =>
        $"\"{program}\" --background" + (SameFolder(home, Configs.DefaultHome) ? "" : $" --home \"{home}\"");

    /// <summary>The LaunchAgent that starts the app when you log in to a Mac.</summary>
    internal static string LaunchAgentPlist(string label, IEnumerable<string> args)
    {
        string items = string.Concat(args.Select(a => $"\n    <string>{SecurityElement.Escape(a)}</string>"));
        return $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
            <plist version="1.0">
            <dict>
              <key>Label</key>
              <string>{SecurityElement.Escape(label)}</string>
              <key>ProgramArguments</key>
              <array>{items}
              </array>
              <key>RunAtLoad</key>
              <true/>
              <key>ProcessType</key>
              <string>Interactive</string>
            </dict>
            </plist>
            """;
    }

    static string LaunchAgent(string label) =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "LaunchAgents", label + ".plist");

    /// <summary>Linux: the XDG autostart entry every desktop (GNOME, KDE, Xfce, Cinnamon) starts at login, in
    /// $XDG_CONFIG_HOME/autostart (~/.config/autostart).</summary>
    internal static string AutostartEntry(string label, string? configHome = null)
    {
        configHome ??= Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") is { Length: > 0 } x
            ? x : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
        return Path.Combine(configHome, "autostart", label + ".desktop");
    }

    /// <summary>The autostart entry that starts the app when you log in to Linux. Exec's arguments are quoted as the
    /// Desktop Entry spec says (double quotes, with ", `, $ and \ escaped), so a folder with spaces still opens.</summary>
    internal static string AutostartDesktopFile(IEnumerable<string> args)
    {
        static string Quote(string a) => a.Length > 0 && a.All(c => char.IsLetterOrDigit(c) || "-_./=+:,@".Contains(c))
            ? a : "\"" + string.Concat(a.Select(c => c is '"' or '`' or '$' or '\\' ? "\\" + c : c == '%' ? "%%" : c.ToString())) + "\"";
        return $"""
            [Desktop Entry]
            Type=Application
            Name=Syft Notes
            Comment=Record your lectures. Get the notes.
            Exec={string.Join(' ', args.Select(Quote))}
            Icon=syft-notes
            Terminal=false
            X-GNOME-Autostart-enabled=true
            NoDisplay=true

            """;
    }

    /// <summary>Whether the app starts when you log in, for this settings folder.</summary>
    public static bool StartsAtLogin(string home)
    {
        if (SystemChangesOff) return false;
        var (label, value) = LoginName(home);
        if (OperatingSystem.IsMacOS()) return File.Exists(LaunchAgent(label));
        if (OperatingSystem.IsWindows()) return Registry.CurrentUser.OpenSubKey(RunKey)?.GetValue(value) is string;
        if (OperatingSystem.IsLinux()) return File.Exists(AutostartEntry(label));
        return false;
    }

    /// <summary>The program the login item for this settings folder starts, or null when there's none (or it can't be
    /// read): a LaunchAgent's first argument, a Run value's first word, an autostart entry's Exec.</summary>
    public static string? LoginProgram(string home)
    {
        if (SystemChangesOff) return null;
        var (label, value) = LoginName(home);
        try
        {
            if (OperatingSystem.IsMacOS()) return File.Exists(LaunchAgent(label)) ? Moves.PlistProgram(File.ReadAllText(LaunchAgent(label))) : null;
            if (OperatingSystem.IsWindows()) return Moves.RunProgram(Registry.CurrentUser.OpenSubKey(RunKey)?.GetValue(value) as string);
            if (OperatingSystem.IsLinux()) return File.Exists(AutostartEntry(label)) ? Moves.ExecProgram(File.ReadAllText(AutostartEntry(label))) : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
        }
        return null;
    }

    /// <summary>Start (or stop starting) when you log in, quietly: to the menu bar or tray, no window. Only when the
    /// student turns it on.</summary>
    public static void StartAtLogin(bool on, string home)
    {
        if (SystemChangesOff) throw new InvalidOperationException("Start at login is off here.");
        var (label, value) = LoginName(home);
        if (OperatingSystem.IsMacOS())
        {
            string plist = LaunchAgent(label);
            if (!on)
            {
                File.Delete(plist);
                return;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(plist)!);
            File.WriteAllText(plist, LaunchAgentPlist(label, LoginArgs(Program, home)));
        }
        else if (OperatingSystem.IsWindows())
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (on) key.SetValue(value, RunCommand(Program, home));
            else key.DeleteValue(value, throwOnMissingValue: false);
        }
        else if (OperatingSystem.IsLinux())
        {
            string entry = AutostartEntry(label);
            if (!on)
            {
                File.Delete(entry);
                return;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(entry)!);
            File.WriteAllText(entry, AutostartDesktopFile(LoginArgs(Program, home)));
        }
    }

    // --- the taskbar button's progress (Windows) ------------------------------------------------------------------

    [ComImport, Guid("ea1afb91-9e28-4b86-90e9-9e9f8a5eefaf"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface ITaskbarList3
    {
        void HrInit();
        void AddTab(IntPtr hwnd);
        void DeleteTab(IntPtr hwnd);
        void ActivateTab(IntPtr hwnd);
        void SetActiveAlt(IntPtr hwnd);
        void MarkFullscreenWindow(IntPtr hwnd, [MarshalAs(UnmanagedType.Bool)] bool fullscreen);
        void SetProgressValue(IntPtr hwnd, ulong done, ulong total);
        void SetProgressState(IntPtr hwnd, int state);
    }

    [ComImport, Guid("56FDF344-FD6D-11d0-958A-006097C9A090"), ClassInterface(ClassInterfaceType.None)]
    class TaskbarList;

    static ITaskbarList3? taskbar;

    /// <summary>Windows: the bar under the app's taskbar button while the model downloads or a lecture transcribes
    /// (null clears it), red while <paramref name="blocked"/> says a problem is stopping a lecture being recorded.</summary>
    [SupportedOSPlatform("windows")]
    public static void TaskbarProgress(IntPtr hwnd, double? fraction, bool blocked = false)
    {
        if (hwnd == IntPtr.Zero) return;
        try
        {
            if (taskbar is null)
            {
                taskbar = (ITaskbarList3)new TaskbarList();
                taskbar.HrInit();
            }
            if (blocked)
            {
                taskbar.SetProgressState(hwnd, 4); // TBPF_ERROR
            }
            else if (fraction is double f)
            {
                taskbar.SetProgressState(hwnd, 2); // TBPF_NORMAL
                taskbar.SetProgressValue(hwnd, (ulong)Math.Round(Math.Clamp(f, 0, 1) * 1000), 1000);
            }
            else
            {
                taskbar.SetProgressState(hwnd, 0); // TBPF_NOPROGRESS
            }
        }
        catch (COMException)
        {
        }
    }

    // --- one copy at a time ------------------------------------------------------------------------------------------

    /// <summary>The lock each settings folder's copy holds while it runs. Kept here for the life of the process: a
    /// stream nobody refers to is collected, and that lets the lock go.</summary>
    static readonly Dictionary<string, FileStream> claims = [];

    internal static string PipeName(string home) => "StudyStash-" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(home)))[..16];

    /// <summary>Be the one copy of the app for this settings folder: hold its app.lock until the app ends. False when
    /// another copy holds it.</summary>
    public static bool Claim(string home)
    {
        string path = Path.Combine(home, "app.lock");
        try
        {
            Directory.CreateDirectory(home);
            var lockFile = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.None);
            lock (claims)
            {
                if (claims.Remove(Path.GetFullPath(home), out var old)) old.Dispose();
                claims[Path.GetFullPath(home)] = lockFile;
            }
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>Let the folder go (tests; the app keeps its claim until it ends).</summary>
    internal static void Release(string home)
    {
        lock (claims)
            if (claims.Remove(Path.GetFullPath(home), out var f)) f.Dispose();
    }

    /// <summary>Tell the copy already running (for this folder) to show itself, or to record. It may still be starting,
    /// so this keeps trying for a while. True when it heard.</summary>
    public static bool HandOff(string home, string message, TimeSpan? wait = null)
    {
        var until = DateTime.UtcNow + (wait ?? TimeSpan.FromSeconds(5));
        while (true)
        {
            try
            {
                using var pipe = new NamedPipeClientStream(".", PipeName(home), PipeDirection.Out);
                pipe.Connect((int)Math.Clamp((until - DateTime.UtcNow).TotalMilliseconds, 1, 1000));
                using var w = new StreamWriter(pipe);
                w.WriteLine(message);
                return true;
            }
            catch (Exception e) when (e is TimeoutException or IOException or UnauthorizedAccessException)
            {
                if (DateTime.UtcNow >= until) return false;
                Thread.Sleep(100);
            }
        }
    }

    /// <summary>How long an older copy asked to quit has before this one hands off to it instead: quitting ends for good
    /// after 30 s in every copy since 0.20 (Shell.QuitLimit).</summary>
    internal static readonly TimeSpan TakeOverWait = TimeSpan.FromSeconds(45);

    /// <summary>
    /// A Mac: this copy couldn't claim the folder, and the copy holding it (its menu bar copy, started at login before an
    /// update) may be an older Syft Notes than this one. No word a later copy hands off asks an older one to quit, so
    /// it's asked the way an update's quit and launchd ask, and every copy takes as its own Quit: SIGTERM (its library
    /// stops cleanly, its typed notes are kept). This copy claims the folder once it has gone, so the student sees the
    /// version just installed rather than the old one's window. Who holds it is read as for the library's port
    /// (<see cref="OurProcess.Of"/>), and the same rule decides (<see cref="Services.OlderLibrary.Judge"/>): only an older
    /// copy of ours, for this folder, with no lecture on the go (<see cref="Services.OlderLibrary.LecturesBusy"/>), is
    /// asked, and only once (<see cref="Services.OlderLibrary.AskOnce"/>), from Applications or from the disk image (the
    /// move into Applications follows). One that can't be told, as new as this one or newer, another folder's, or busy:
    /// handed off to, as before. A copy that isn't in a bundle (a build folder) never asks. The background library is this copy's to start
    /// again as it starts (LibraryService). True when this copy holds the folder now.
    /// </summary>
    public static bool TakeOver(string home, Action<string> log)
    {
        if (!OperatingSystem.IsMacOS() || SystemChangesOff || SelfTest.Dir is not null) return false;
        if (Apps.EnclosingMacBundle(AppContext.BaseDirectory) is null) return false;
        return TakeOverAsync(home, LockHolder(home), Program, Engine.Version, Services.OlderLibrary.ThisComputer, () => Claim(home), log)
            .GetAwaiter().GetResult();
    }

    /// <summary><see cref="TakeOver(string, Action{string})"/> once the folder's holder is looked up
    /// (<paramref name="holder"/>, null when it can't be told), with the asking (<paramref name="older"/>), the claim and
    /// the clock swapped in a test.</summary>
    internal static async Task<bool> TakeOverAsync(string home, OurProcess? holder, string self, string version,
        Services.OlderLibrary older, Func<bool> claim, Action<string> log, TimeProvider? clock = null)
    {
        // Can't be told: never asked. The folder is this copy's only if it's free by now (the holder quit as this copy
        // looked); otherwise it's handed off to, as before.
        if (holder is null) return claim();
        string who = $"Syft Notes {(holder.Version is { } v ? string.Join('.', v) : "(version unknown)")} ({holder.Program}, process {holder.Pid})";
        // A copy opened from its disk image takes over too: the move into Applications follows (MoveSheet), and it never
        // registers anything from the image.
        var verdict = Services.OlderLibrary.Judge(holder, self, version, home, older.Busy, installing: true);
        if (verdict != Services.OlderVerdict.Replace)
        {
            log($"[app] {who} holds this folder and isn't asked to quit ({verdict}): handing off to it");
            return false;
        }
        log($"[app] {who} holds this folder: asking it to quit, so {version} runs instead");
        if (!older.AskOnce(holder.Pid, out _)) return false;
        if (await Services.Kit.Poll.UntilAsync(claim, TimeSpan.FromMilliseconds(250), TakeOverWait, clock: clock))
        {
            log($"[app] the older copy quit; {version} takes over");
            return true;
        }
        log($"[app] the older copy didn't quit in {TakeOverWait.TotalSeconds:0} s: handing off to it instead");
        return false;
    }

    /// <summary>The process holding <paramref name="home"/>'s app.lock (lsof), as <see cref="OurProcess.Of"/> tells it:
    /// null when there's no one other process holding it, or it can't be told.</summary>
    static OurProcess? LockHolder(string home)
    {
        if (Machine.Run("/usr/sbin/lsof", ["-t", "--", Path.Combine(home, "app.lock")], TimeSpan.FromSeconds(20)) is not { ExitCode: 0 } open) return null;
        var pids = open.Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(s => int.TryParse(s.Trim(), out int p) ? p : 0)
            .Where(p => p > 0 && p != Environment.ProcessId).Distinct().ToList();
        return pids is [int pid] ? OurProcess.Of(pid) : null;
    }

    /// <summary>A Mac: the copy holding the folder didn't answer, and this one couldn't take over from it, so the student
    /// isn't left with nothing at all: the system's own dialog (no window of ours needed) says so, and what to do.</summary>
    public static void SayNotAnswering()
    {
        if (!OperatingSystem.IsMacOS() || SelfTest.Dir is not null) return;
        const string script =
            "display dialog \"Syft Notes is already open, but it didn't answer. Wait a minute and open it again. If it still doesn't open, restart your Mac.\" " +
            "with title \"Syft Notes isn't answering\" buttons {\"OK\"} default button \"OK\" with icon note";
        Machine.Run("/usr/bin/osascript", ["-e", script], TimeSpan.FromMinutes(10));
    }

    /// <summary>The words a later copy can hand off: show the library, record, open the dropdown, the quick panel or
    /// Settings ("settings", or "settings:Library" for one of its pages). A click on one of Windows' notifications
    /// comes the same way, as a word of its own (<see cref="NoticeLinks"/>).</summary>
    static readonly HashSet<string> Words = ["show", "record", "panel", "quick", "settings"];

    /// <summary>Settings' pages a later copy can open it at.</summary>
    static readonly HashSet<string> SettingsPages = ["General", "Appearance", "Recording", "Connection", "Library", "Classes", "Notes", "AI", "Access", "Canvas", "Folders"];

    /// <summary>Whether a later copy's word is one the app acts on; anything else is ignored. ("snap:&lt;name&gt;" only
    /// while pictures are on: see <see cref="MacSnap"/>.)</summary>
    public static bool IsWord(string word) =>
        Words.Contains(word) || NoticeLinks.IsWord(word)
        || (word.StartsWith("settings:", StringComparison.Ordinal) && SettingsPages.Contains(word["settings:".Length..]))
        || (word.StartsWith("snap:", StringComparison.Ordinal) && MacSnap.Folder is not null && MacSnap.IsName(word["snap:".Length..]))
        || OpenedFile(word) is not null;

    /// <summary>"open:&lt;path&gt;": a later copy was started with a lecture a friend shared (Explorer opened a .syft file
    /// with it), which the running app opens.</summary>
    public static string OpenWord(string path) => "open:" + path;

    /// <summary>The shared lecture's file an <see cref="OpenWord"/> names (a whole path to a .syft file), or null.</summary>
    public static string? OpenedFile(string word) =>
        word.StartsWith("open:", StringComparison.Ordinal) && word[5..] is var path && Path.IsPathFullyQualified(path) && SyftFile.Is(path) ? path : null;

    /// <summary>Listen for later copies handing off (they say "show", or "record"); what they say is passed to
    /// <paramref name="onMessage"/> through <paramref name="post"/> (the UI thread, unless a test says otherwise).
    /// Anything else is ignored, and a copy that connects and says nothing is let go after 2 seconds.</summary>
    public static void Listen(string home, Action<string> onMessage, CancellationToken stop, Action<Action>? post = null)
    {
        post ??= a => Dispatcher.UIThread.Post(a);
        string name = PipeName(home);
        _ = Task.Run(async () =>
        {
            NamedPipeServerStream? next = null;
            while (!stop.IsCancellationRequested)
            {
                NamedPipeServerStream? pipe = null;
                try
                {
                    pipe = next ?? Server(name);
                    next = null;
                    await pipe.WaitForConnectionAsync(stop);
                    // Listen again before reading this one: a copy that comes meanwhile waits its turn, not turned away.
                    next = Server(name);
                    if (await ReadWordAsync(pipe, stop) is { } word && IsWord(word)) post(() => onMessage(word));
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (IOException)
                {
                    try
                    {
                        await Services.Kit.Poll.AfterAsync(TimeSpan.FromMilliseconds(500), stop);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                }
                finally
                {
                    pipe?.Dispose();
                }
            }
            next?.Dispose();
        }, CancellationToken.None);
    }

    static NamedPipeServerStream Server(string name) =>
        new(name, PipeDirection.In, NamedPipeServerStream.MaxAllowedServerInstances, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);

    /// <summary>The line a copy sent, or null when it said nothing within 2 seconds.</summary>
    static async Task<string?> ReadWordAsync(Stream pipe, CancellationToken stop)
    {
        using var quiet = CancellationTokenSource.CreateLinkedTokenSource(stop);
        quiet.CancelAfter(TimeSpan.FromSeconds(2));
        using var r = new StreamReader(pipe, leaveOpen: true);
        try
        {
            return (await r.ReadLineAsync(quiet.Token))?.Trim();
        }
        catch (OperationCanceledException) when (!stop.IsCancellationRequested)
        {
            return null;
        }
    }
}

/// <summary>Starting the app when you log in, as the app sees it: <see cref="LoginItems.System"/> is the real thing
/// (<see cref="Desktop"/>); a test gives its own and counts the calls.</summary>
public interface ILoginItems
{
    bool StartsAtLogin(string home);
    void StartAtLogin(bool on, string home);
}

/// <summary>This computer's login items: a LaunchAgent on a Mac, the Run key on Windows.</summary>
public sealed class LoginItems : ILoginItems
{
    public static readonly LoginItems System = new();

    public bool StartsAtLogin(string home) => Desktop.StartsAtLogin(home);

    public void StartAtLogin(bool on, string home) => Desktop.StartAtLogin(on, home);
}
