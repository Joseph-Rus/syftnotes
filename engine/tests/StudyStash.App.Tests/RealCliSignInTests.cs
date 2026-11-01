using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using StudyStash.App.Platform;
using StudyStash.App.Services;
using StudyStash.Core;
using StudyStash.Core.Ai;

namespace StudyStash.App.Tests;

/// <summary>
/// CI's proof that every AI sign-in works with no terminal (the "Real sign-ins, hidden" job, STUDYSTASH_REAL_CLIS=1 on
/// a GitHub runner only): the real Claude Code, Codex and Antigravity CLI (Gemini), installed by Syft Notes' own
/// installer (<see cref="AgentInstall"/>, the makers' own installers) into a home of the test's own, each signed in with
/// Syft Notes' own hidden sign-in (<see cref="AgentSignIn"/>) until it waits for the browser, then stopped. No account is
/// used and nothing finishes. Nothing opens: Syft Notes' browser opener is replaced, and the system's (open, xdg-open,
/// BROWSER) are stand-ins that only count. On Windows every window on the screen is looked at while it runs, and none
/// may belong to the sign-in. What's said is only yes or no, never an address, a code or a token; a copy of what each
/// showed, with every address and anything like a code taken out, is kept only when STUDYSTASH_REAL_CLIS_RECORD=1.
/// A CLI whose download fails for the network is skipped, and said so.
/// <para>Never on a computer of our own: the real CLIs would find its own sign-ins.</para>
/// </summary>
[Collection(nameof(EnvironmentTests))]
public sealed partial class RealCliSignInTests
{
    static string Dir => Environment.GetEnvironmentVariable("STUDYSTASH_REAL_CLIS_DIR") is { Length: > 0 } d ? d : Path.Combine(Path.GetTempPath(), "real-sign-in");

    sealed record Result(string Brand, string What)
    {
        public bool Failed { get; init; }
    }

    [Fact]
    public async Task Each_real_AIs_sign_in_waits_for_the_browser_in_the_hidden_terminal()
    {
        Assert.SkipUnless(Environment.GetEnvironmentVariable("STUDYSTASH_REAL_CLIS") == "1" && Environment.GetEnvironmentVariable("GITHUB_ACTIONS") == "true",
            "CI only: the real CLIs, never on a computer of our own.");
        string root = Dir;
        Directory.CreateDirectory(root);
        string home = Path.Combine(root, "home"), openers = Path.Combine(root, "openers"), opened = Path.Combine(root, "system-opener-called");
        Directory.CreateDirectory(home);
        bool record = Environment.GetEnvironmentVariable("STUDYSTASH_REAL_CLIS_RECORD") == "1";
        string records = Path.Combine(root, "transcripts");
        if (record) Directory.CreateDirectory(records);
        WriteOpeners(openers, opened);

        var before = new Dictionary<string, string?>();
        void Set(string k, string? v)
        {
            if (!before.ContainsKey(k)) before[k] = Environment.GetEnvironmentVariable(k);
            Environment.SetEnvironmentVariable(k, v);
        }
        Set("HOME", home);
        if (OperatingSystem.IsWindows()) Set("USERPROFILE", home);
        Set("PATH", openers + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH"));
        Set("BROWSER", Path.Combine(openers, OperatingSystem.IsWindows() ? "xdg-open.cmd" : "xdg-open"));
        var agyHome = Antigravity.Use(Path.Combine(home, "syft-gemini"));
        bool changes = Desktop.SystemChangesOff;
        Desktop.SystemChangesOff = false;
        var results = new List<Result>();
        try
        {
            foreach (var cli in AgentCli.All) results.Add(await TryAsync(cli, root, opened, record ? records : null));
        }
        finally
        {
            Desktop.SystemChangesOff = changes;
            agyHome.Dispose();
            foreach (var (k, v) in before) Environment.SetEnvironmentVariable(k, v);
        }

        string os = OperatingSystem.IsWindows() ? "Windows" : OperatingSystem.IsMacOS() ? "macOS" : "Linux";
        var summary = results.Select(r => $"{os} · {r.Brand}: {r.What}").ToList();
        File.WriteAllLines(Path.Combine(root, "summary.txt"), summary);
        if (Environment.GetEnvironmentVariable("GITHUB_STEP_SUMMARY") is { Length: > 0 } step)
            File.AppendAllLines(step, ["### Real sign-ins, hidden (" + os + ")", "", .. summary.Select(s => "- " + s), ""]);
        Assert.False(results.Any(r => r.Failed), string.Join("\n", summary));
    }

    static async Task<Result> TryAsync(AgentCliInfo cli, string root, string opened, string? records)
    {
        var install = new AgentInstall(cli, OperatingSystem.IsWindows(), Path.Combine(root, $"install-{cli.Id}.log")) { Elevated = () => false };
        var outcome = await install.RunAsync();
        if (!outcome.Ok)
            return outcome.Failure == InstallFailure.Offline
                ? new Result(cli.Brand, "download failed (network), skipped")
                : new Result(cli.Brand, $"install failed ({outcome.Failure}: {Redact(outcome.LastLine)})") { Failed = true };
        string exe = outcome.Found.Exe!;
        if (File.Exists(opened)) File.Delete(opened);

        var transcript = new List<string>();
        int pages = 0;
        var listening = Listeners();
        using var signIn = new AgentSignIn(cli, exe)
        {
            OpenPage = _ =>
            {
                Interlocked.Increment(ref pages);
                return true;
            },
            Shown = line =>
            {
                lock (transcript) transcript.Add(Redact(line));
            },
        };
        var windows = new HashSet<string>();
        var browsers = new HashSet<string>();
        using var watching = new CancellationTokenSource();
        var watcher = OperatingSystem.IsWindows() ? Task.Run(() => WatchWindows(signIn, windows, browsers, watching.Token)) : Task.CompletedTask;
        var at = Stopwatch.StartNew();
        bool started = signIn.Start();
        bool listens = false;
        while (started && at.Elapsed < TimeSpan.FromSeconds(75))
        {
            listens = Listeners().Except(listening).Any();
            if (signIn.Waiting && (listens || Volatile.Read(ref pages) > 0 || File.Exists(opened))) break;
            if (signIn.Stuck || signIn.GaveUp || signIn.Region || !signIn.Running) break;
            await Task.Delay(250);
        }
        // A little longer, for a window that comes late.
        if (OperatingSystem.IsWindows()) await Task.Delay(2000);
        // Everything read before it's stopped (stopping it forgets what it showed).
        bool alive = signIn.Running, shown = signIn.Waiting, wanted = signIn.WantedTerminal, stuck = signIn.Stuck, region = signIn.Region;
        bool finishes = signIn.PageFinishesByItself;
        var answered = signIn.Answered;
        await watching.CancelAsync();
        await watcher;
        signIn.Dispose();

        bool waiting = started && shown && alive && (listens || Volatile.Read(ref pages) > 0 || File.Exists(opened));
        if (records is not null)
        {
            lock (transcript) File.WriteAllLines(Path.Combine(records, $"{cli.Id}.txt"), transcript);
        }
        var parts = new List<string>
        {
            $"needed a terminal: {Yes(wanted || stuck)}",
            $"prompts answered: {(answered.Count == 0 ? "none" : string.Join(", ", answered))}",
            $"waiting for the browser: {Yes(waiting)}",
            $"its callback listening: {Yes(listens)}",
            $"page finishes by itself: {Yes(finishes)}",
            $"Syft Notes opened the page: {Yes(Volatile.Read(ref pages) > 0)}",
            $"system opener called: {Yes(File.Exists(opened))}",
        };
        if (OperatingSystem.IsWindows())
        {
            parts.Add($"window: {(windows.Count == 0 ? "none" : string.Join(", ", windows))}");
            // The CLI opening the runner's browser itself is the sign-in working, not a window of its own.
            if (browsers.Count > 0) parts.Add($"browser it opened: {string.Join(", ", browsers)}");
        }
        if (region) parts.Add("refused for the country");
        return new Result(cli.Brand, string.Join("; ", parts)) { Failed = !waiting || wanted || stuck || windows.Count > 0 };
    }

    static string Yes(bool b) => b ? "yes" : "no";

    /// <summary>The system's browser openers, as stand-ins that only leave a mark (never the address).</summary>
    static void WriteOpeners(string dir, string mark)
    {
        Directory.CreateDirectory(dir);
        foreach (string name in new[] { "open", "xdg-open", "x-www-browser", "www-browser", "sensible-browser", "gnome-open", "kde-open", "wslview" })
        {
            if (OperatingSystem.IsWindows())
            {
                File.WriteAllText(Path.Combine(dir, name + ".cmd"), $"@echo off\r\necho x>>\"{mark}\"\r\nexit /b 0\r\n");
                continue;
            }
            string path = Path.Combine(dir, name);
            File.WriteAllText(path, $"#!/bin/sh\necho x >> '{mark}'\nexit 0\n");
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    /// <summary>What is listening on this computer now: a sign-in that waits for the browser listens for it to come back.</summary>
    static HashSet<string> Listeners()
    {
        if (!OperatingSystem.IsMacOS())
            return [.. IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners().Select(e => e.ToString())];
        var r = Machine.Run("netstat", ["-an", "-p", "tcp"], TimeSpan.FromSeconds(20));
        return [.. (r?.Stdout ?? "").Split('\n').Where(l => l.Contains("LISTEN", StringComparison.Ordinal))
            .Select(l => l.Split(' ', StringSplitOptions.RemoveEmptyEntries)).Where(p => p.Length > 3).Select(p => p[3])];
    }

    /// <summary>A line with every address, email and anything that looks like a code or a key taken out.</summary>
    internal static string Redact(string line)
    {
        string s = Address().Replace(line, "<address>");
        s = Email().Replace(s, "<email>");
        s = Long().Replace(s, "<long>");
        return Code().Replace(s, "<code>");
    }

    [GeneratedRegex(@"[a-z][a-z0-9+.-]*://\S+", RegexOptions.IgnoreCase)]
    private static partial Regex Address();
    [GeneratedRegex(@"[\w.+-]+@[\w-]+\.[\w.-]+")]
    private static partial Regex Email();
    [GeneratedRegex(@"[A-Za-z0-9_\-+/=%.~]{24,}")]
    private static partial Regex Long();
    [GeneratedRegex(@"\b[A-Z0-9]{4,}-[A-Z0-9]{4,}\b")]
    private static partial Regex Code();

    // --- Windows: no window of the sign-in's own ---------------------------------------------------------------------

    /// <summary>Every visible window on the screen, looked at ten times a second while the sign-in runs: one that belongs
    /// to it (or to the console host of its hidden console) is named by its program and window class.</summary>
    static void WatchWindows(AgentSignIn signIn, HashSet<string> seen, HashSet<string> browsers, CancellationToken ct)
    {
        if (!OperatingSystem.IsWindows()) return;
        var already = VisibleWindows().Select(w => w.Handle).ToHashSet();
        int self = Environment.ProcessId;
        while (!ct.IsCancellationRequested)
        {
            var tree = Descendants(signIn.ProcessId is { } p ? [p, self] : [self]);
            foreach (var w in VisibleWindows())
            {
                if (already.Contains(w.Handle) || !tree.Contains(w.Pid)) continue;
                string program;
                try
                {
                    using var proc = Process.GetProcessById(w.Pid);
                    program = proc.ProcessName;
                }
                catch (ArgumentException)
                {
                    program = "?";
                }
                if (Browsers.Contains(program, StringComparer.OrdinalIgnoreCase)) lock (browsers) browsers.Add(program);
                else lock (seen) seen.Add($"{program} ({w.Class})");
            }
            ct.WaitHandle.WaitOne(100);
        }
    }

    /// <summary>Web browsers: a sign-in opens one, which is the point.</summary>
    static readonly string[] Browsers = ["msedge", "chrome", "firefox", "brave", "opera", "iexplore", "MicrosoftEdge"];

    static List<(IntPtr Handle, int Pid, string Class)> VisibleWindows()
    {
        var found = new List<(IntPtr, int, string)>();
        EnumWindows((h, _) =>
        {
            if (!IsWindowVisible(h)) return true;
            GetWindowThreadProcessId(h, out uint pid);
            var name = new StringBuilder(256);
            GetClassName(h, name, name.Capacity);
            found.Add((h, (int)pid, name.ToString()));
            return true;
        }, IntPtr.Zero);
        return found;
    }

    /// <summary>The processes started by <paramref name="roots"/>, and by those, all the way down (not the roots' own
    /// process when it's this test's).</summary>
    static HashSet<int> Descendants(IEnumerable<int> roots)
    {
        var parents = new Dictionary<int, int>();
        IntPtr snap = CreateToolhelp32Snapshot(0x2, 0);
        try
        {
            var e = new ProcessEntry { dwSize = Marshal.SizeOf<ProcessEntry>() };
            for (bool ok = Process32FirstW(snap, ref e); ok; ok = Process32NextW(snap, ref e)) parents[e.th32ProcessID] = e.th32ParentProcessID;
        }
        finally
        {
            CloseHandle(snap);
        }
        var tree = new HashSet<int>(roots.Where(r => r != Environment.ProcessId));
        var from = new Queue<int>(roots);
        while (from.TryDequeue(out int p))
            foreach (var (child, parent) in parents)
                if (parent == p && child != p && tree.Add(child)) from.Enqueue(child);
        return tree;
    }

    delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct ProcessEntry
    {
        public int dwSize, cntUsage, th32ProcessID;
        public IntPtr th32DefaultHeapID;
        public int th32ModuleID, cntThreads, th32ParentProcessID, pcPriClassBase, dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szExeFile;
    }

    [DllImport("user32.dll")] static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr hwnd, StringBuilder name, int max);
    [DllImport("kernel32.dll", SetLastError = true)] static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint pid);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern bool Process32FirstW(IntPtr snap, ref ProcessEntry entry);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern bool Process32NextW(IntPtr snap, ref ProcessEntry entry);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
}
