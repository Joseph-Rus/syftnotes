using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using StudyStash.App.Services;
using StudyStash.App.Services.Kit;

namespace StudyStash.App.Platform;

/// <summary>One running process of an AI's desktop app, as Windows lists it: its id and the program file it runs.</summary>
public sealed record AppProcess(int Id, string Program);

/// <summary>
/// Closing an AI's desktop app (Claude, ChatGPT) and opening it again, on a Mac and on Windows, for Connect. Both apps
/// keep their settings file in memory while they run and write it back whole, so an entry added while one is open can
/// be lost; and they only read it when they start. So Connect closes the app before it writes, and opens it after.
/// <list type="bullet">
/// <item>A Mac: asks the app to quit the way its own Quit menu does (it may ask about unsaved work), and never forces it.</item>
/// <item>Windows: asks each of its windows to close. These apps then sit on in the tray by the clock, so after
/// <see cref="PoliteWait"/> it ends the app's own processes, found by their program's name and where the app is
/// installed. Never anything else, and never Syft Notes itself.</item>
/// </list>
/// The app ids are <see cref="ClaudeSetup"/>'s ("claude-desktop", "codex").
/// </summary>
public sealed class DesktopApps
{
    /// <summary>This computer's.</summary>
    public static DesktopApps This { get; } = new();

    /// <summary>How long a Mac app gets to quit before this gives up (it may be asking about unsaved work).</summary>
    public static readonly TimeSpan QuitWait = TimeSpan.FromSeconds(12);
    /// <summary>Windows: how long the app gets to close by itself before its processes are ended, and then to go.</summary>
    public TimeSpan PoliteWait { get; init; } = TimeSpan.FromSeconds(5);
    public TimeSpan EndWait { get; init; } = TimeSpan.FromSeconds(5);
    public TimeSpan Every { get; init; } = TimeSpan.FromMilliseconds(250);
    public TimeProvider Clock { get; init; } = TimeProvider.System;

    // Windows' hooks: the real ones by default; a test lists made-up processes and records what's asked.
    /// <summary>The processes with any of these program names (no ".exe"), and the program each runs.</summary>
    public Func<IReadOnlyList<string>, IReadOnlyList<AppProcess>> Find { get; init; } = FindProcesses;
    /// <summary>Asks a process to close its window, as its close button does.</summary>
    public Action<int> AskToClose { get; init; } = CloseWindow;
    /// <summary>Ends a process (that process only, not what it started).</summary>
    public Action<int> End { get; init; } = EndProcess;
    /// <summary>Starts a program again; whether it started.</summary>
    public Func<string, bool> Start { get; init; } = StartProgram;
    /// <summary>Syft Notes' own process, never closed.</summary>
    public int Own { get; init; } = Environment.ProcessId;

    /// <summary>The program each app was closed from on Windows, to start it again.</summary>
    readonly ConcurrentDictionary<string, string> closedFrom = new();

    /// <summary>A desktop app's program names on Windows and the folders it's installed in (Claude Code's own claude.exe
    /// isn't the Claude app: it's somewhere else).</summary>
    public static (string[] Names, string[] Places)? OnWindows(string id) => id switch
    {
        "claude-desktop" => (["claude"], [@"\AnthropicClaude\", @"\WindowsApps\Claude_"]),
        "codex" => (["ChatGPT", "Codex"], [@"\WindowsApps\OpenAI.Codex_"]),
        _ => null,
    };

    /// <summary>The app's own processes on Windows: named like it, run from where it's installed, and not this one.</summary>
    public IReadOnlyList<AppProcess> RunningOnWindows(string id) =>
        OnWindows(id) is { } app
            ? [.. Find(app.Names).Where(p => p.Id != Own && app.Places.Any(place => p.Program.Contains(place, StringComparison.OrdinalIgnoreCase)))]
            : [];

    /// <summary>Whether the app is open now.</summary>
    public bool IsRunning(string id)
    {
        if (OperatingSystem.IsMacOS()) return ClaudeSetup.DesktopApp(id) is { } app && Running(app.Bundle).Count > 0;
        return OperatingSystem.IsWindows() && RunningOnWindows(id).Count > 0;
    }

    /// <summary>Closes the app if it's open; true once it isn't. A Mac never forces it; Windows ends its own processes
    /// when it's still there after <see cref="PoliteWait"/>.</summary>
    public async Task<bool> QuitAsync(string id, CancellationToken ct = default)
    {
        if (OperatingSystem.IsMacOS())
        {
            if (ClaudeSetup.DesktopApp(id) is not { } app) return false;
            foreach (IntPtr running in Running(app.Bundle)) SendByte(running, sel_registerName("terminate"));
            return await Poll.UntilAsync(() => Running(app.Bundle).Count == 0, Every, QuitWait, ct, Clock);
        }
        return await QuitOnWindowsAsync(id, ct);
    }

    /// <summary>Windows' close: each window asked first, then, if it's still there, its own processes ended.</summary>
    public async Task<bool> QuitOnWindowsAsync(string id, CancellationToken ct = default)
    {
        var running = RunningOnWindows(id);
        if (running.Count == 0) return true;
        closedFrom[id] = running[0].Program;
        foreach (var p in running) AskToClose(p.Id);
        if (await Poll.UntilAsync(() => RunningOnWindows(id).Count == 0, Every, PoliteWait, ct, Clock)) return true;
        // Still in the tray: its own processes (found again, so only those still there) are ended.
        foreach (var p in RunningOnWindows(id)) End(p.Id);
        return await Poll.UntilAsync(() => RunningOnWindows(id).Count == 0, Every, EndWait, ct, Clock);
    }

    /// <summary>Opens the app: a Mac by its bundle, Windows from where it was closed. Whether it did.</summary>
    public async Task<bool> OpenAsync(string id, CancellationToken ct = default)
    {
        if (OperatingSystem.IsMacOS())
        {
            if (ClaudeSetup.DesktopApp(id) is not { } app) return false;
            try
            {
                using var open = Process.Start(new ProcessStartInfo("/usr/bin/open") { ArgumentList = { "-b", app.Bundle }, UseShellExecute = false, CreateNoWindow = true });
                if (open is null) return false;
                await open.WaitForExitAsync(ct);
                return open.ExitCode == 0;
            }
            catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
            {
                return false;
            }
        }
        return closedFrom.TryRemove(id, out var program) && Start(program);
    }

    // --- Windows ---------------------------------------------------------------------------------------------------

    static IReadOnlyList<AppProcess> FindProcesses(IReadOnlyList<string> names)
    {
        var found = new List<AppProcess>();
        foreach (string name in names)
            foreach (var p in Process.GetProcessesByName(name))
                using (p)
                {
                    try
                    {
                        if (p.MainModule?.FileName is { } file) found.Add(new AppProcess(p.Id, file));
                    }
                    catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException or NotSupportedException)
                    {
                        // Gone, or not ours to read: not counted.
                    }
                }
        return found;
    }

    static void CloseWindow(int id)
    {
        try
        {
            using var p = Process.GetProcessById(id);
            p.CloseMainWindow();
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            // Already gone.
        }
    }

    static void EndProcess(int id)
    {
        try
        {
            using var p = Process.GetProcessById(id);
            p.Kill(entireProcessTree: false);
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
        {
            // Already gone, or not ours to end: the wait after says whether it went.
        }
    }

    /// <summary>A Microsoft Store app is started through Windows (its program can't be run straight from its package
    /// folder); one from an installer, by its program.</summary>
    static bool StartProgram(string program)
    {
        try
        {
            var start = StoreApp(program) is { } store
                ? new ProcessStartInfo("explorer.exe") { ArgumentList = { store }, UseShellExecute = false }
                : new ProcessStartInfo(program) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(program) ?? "" };
            using var started = Process.Start(start);
            return started is not null;
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            return false;
        }
    }

    /// <summary>How Windows starts a Store app run from <paramref name="program"/>: shell:AppsFolder\family!app, from
    /// its package folder's name (Claude_1.2.3.0_x64__publisher) and its manifest; null when it isn't one.</summary>
    public static string? StoreApp(string program)
    {
        const string apps = @"\WindowsApps\";
        int at = program.IndexOf(apps, StringComparison.OrdinalIgnoreCase);
        if (at < 0) return null;
        string rest = program[(at + apps.Length)..];
        int slash = rest.IndexOf('\\');
        if (slash <= 0) return null;
        string folder = rest[..slash];
        int first = folder.IndexOf('_');
        int publisher = folder.LastIndexOf("__", StringComparison.Ordinal);
        if (first <= 0 || publisher < first) return null;
        string family = folder[..first] + "_" + folder[(publisher + 2)..];
        string appId = "App";
        try
        {
            string manifest = Path.Combine(program[..(at + apps.Length)], folder, "AppxManifest.xml");
            if (File.Exists(manifest) && Regex.Match(File.ReadAllText(manifest), @"<Application\s[^>]*\bId=""([^""]+)""") is { Success: true } m) appId = m.Groups[1].Value;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Its usual name, then.
        }
        return $@"shell:AppsFolder\{family}!{appId}";
    }

    // --- Mac -------------------------------------------------------------------------------------------------------

    const string Lib = "/usr/lib/libobjc.A.dylib";

    [DllImport(Lib)] static extern IntPtr objc_getClass(string name);
    [DllImport(Lib)] static extern IntPtr sel_registerName(string name);
    [DllImport(Lib, EntryPoint = "objc_msgSend")] static extern IntPtr SendPtr(IntPtr receiver, IntPtr selector, IntPtr arg);
    [DllImport(Lib, EntryPoint = "objc_msgSend")] static extern IntPtr SendText(IntPtr receiver, IntPtr selector, [MarshalAs(UnmanagedType.LPUTF8Str)] string arg);
    [DllImport(Lib, EntryPoint = "objc_msgSend")] static extern IntPtr SendIndex(IntPtr receiver, IntPtr selector, nuint index);
    [DllImport(Lib, EntryPoint = "objc_msgSend")] static extern nuint SendCount(IntPtr receiver, IntPtr selector);
    [DllImport(Lib, EntryPoint = "objc_msgSend")] static extern byte SendByte(IntPtr receiver, IntPtr selector);

    /// <summary>Every running copy of the app with this bundle id, asked fresh.</summary>
    static List<IntPtr> Running(string bundleId)
    {
        var found = new List<IntPtr>();
        if (!OperatingSystem.IsMacOS()) return found;
        try
        {
            IntPtr id = SendText(objc_getClass("NSString"), sel_registerName("stringWithUTF8String:"), bundleId);
            IntPtr apps = SendPtr(objc_getClass("NSRunningApplication"), sel_registerName("runningApplicationsWithBundleIdentifier:"), id);
            if (apps == IntPtr.Zero) return found;
            nuint count = SendCount(apps, sel_registerName("count"));
            for (nuint i = 0; i < count; i++) found.Add(SendIndex(apps, sel_registerName("objectAtIndex:"), i));
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
        {
        }
        return found;
    }
}
