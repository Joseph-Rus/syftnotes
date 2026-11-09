using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace StudyStash.App.Platform;

/// <summary>An app holding the microphone that may be in a call: its name as the student knows it ("Zoom"), and whether
/// it's a browser (Google Meet in Chrome, say), which can't be looked into.</summary>
public readonly record struct MicUser(string App, bool Browser);

/// <summary>
/// Which call apps hold the microphone right now, read from what the computer already keeps: a Mac's Core Audio list of
/// the processes using sound, Windows' record behind the taskbar's microphone icon (its ConsentStore). Nothing is asked
/// for and nothing else is read: no window titles, no browser tabs, nothing that's said.
/// </summary>
public static class CallApps
{
    sealed record Known(string Name, bool Browser, string[] Mac, string[] Win);

    // Mac: the start of a bundle id (a helper process holds the microphone for some: com.google.Chrome.helper).
    // Windows: a program's file name, or the start of a Store app's package name (the new Teams).
    static readonly Known[] Apps =
    [
        new("Zoom", false, ["us.zoom."], ["Zoom.exe"]),
        new("Teams", false, ["com.microsoft.teams"], ["ms-teams.exe", "Teams.exe", "MSTeams_"]),
        new("Webex", false, ["com.webex.", "Cisco-Systems.Spark"], ["CiscoCollabHost.exe", "atmgr.exe"]),
        new("Chrome", true, ["com.google.Chrome"], ["chrome.exe"]),
        new("Edge", true, ["com.microsoft.edgemac"], ["msedge.exe"]),
        new("Firefox", true, ["org.mozilla.firefox"], ["firefox.exe"]),
        new("Brave", true, ["com.brave.Browser"], ["brave.exe"]),
        new("Arc", true, ["company.thebrowser."], []),
        // shortcut: WebKit's sound process holds the microphone for Safari and for any other app built on WebKit; it's
        // called Safari, since a minute of it is almost always a call there. Name it apart if that's ever wrong.
        new("Safari", true, ["com.apple.Safari", "com.apple.WebKit.GPU"], []),
    ];

    /// <summary>This computer can tell: Windows, and a Mac on macOS 14.2 or later (an older one can't record the call's
    /// other side anyway).</summary>
    public static bool Supported => OperatingSystem.IsWindows() || OperatingSystem.IsMacOSVersionAtLeast(14, 2);

    /// <summary>The known apps holding the microphone now (Syft Notes isn't one). Empty where it can't be told.</summary>
    public static IReadOnlyList<MicUser> Holding()
    {
        if (OperatingSystem.IsMacOSVersionAtLeast(14, 2))
            return Pick(MacHolding(), (app, id) => app.Mac.Any(p => id.StartsWith(p, StringComparison.Ordinal)));
        if (OperatingSystem.IsWindows())
            return Pick(WinHolding(), (app, name) => app.Win.Any(w => w.EndsWith('_')
                ? name.StartsWith(w, StringComparison.OrdinalIgnoreCase) : name.Equals(w, StringComparison.OrdinalIgnoreCase)) && WinRunning(app));
        return [];
    }

    static List<MicUser> Pick(IEnumerable<string> holders, Func<Known, string, bool> matches) =>
        [.. Apps.Where(app => holders.Any(h => matches(app, h))).Select(app => new MicUser(app.Name, app.Browser))];

    // --- Mac: Core Audio's process objects (macOS 14 and later): which are taking sound in, and whose they are --------

    const string CoreAudio = "/System/Library/Frameworks/CoreAudio.framework/CoreAudio";
    const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    const uint SystemObject = 1, Global = 0x676C6F62; // kAudioObjectSystemObject, 'glob'
    const uint ProcessList = 0x70727323, Pid = 0x70706964, BundleId = 0x70626964, TakingInput = 0x70697269; // 'prs#' 'ppid' 'pbid' 'piri'

    [StructLayout(LayoutKind.Sequential)]
    struct Address
    {
        public uint Selector, Scope, Element;
    }

    [DllImport(CoreAudio)]
    static extern int AudioObjectGetPropertyDataSize(uint id, ref Address address, uint qualifierSize, IntPtr qualifier, out uint size);

    [DllImport(CoreAudio)]
    static extern int AudioObjectGetPropertyData(uint id, ref Address address, uint qualifierSize, IntPtr qualifier, ref uint size, [Out] uint[] data);

    [DllImport(CoreAudio)]
    static extern int AudioObjectGetPropertyData(uint id, ref Address address, uint qualifierSize, IntPtr qualifier, ref uint size, out uint data);

    [DllImport(CoreAudio)]
    static extern int AudioObjectGetPropertyData(uint id, ref Address address, uint qualifierSize, IntPtr qualifier, ref uint size, out IntPtr data);

    [DllImport(CoreFoundation)]
    static extern bool CFStringGetCString(IntPtr text, byte[] buffer, nint size, uint encoding);

    [DllImport(CoreFoundation)]
    static extern void CFRelease(IntPtr cf);

    /// <summary>The bundle ids of the processes taking in sound now, other than this one.</summary>
    static List<string> MacHolding()
    {
        var found = new List<string>();
        try
        {
            var list = new Address { Selector = ProcessList, Scope = Global };
            if (AudioObjectGetPropertyDataSize(SystemObject, ref list, 0, IntPtr.Zero, out uint size) != 0 || size == 0) return found;
            var ids = new uint[size / sizeof(uint)];
            if (AudioObjectGetPropertyData(SystemObject, ref list, 0, IntPtr.Zero, ref size, ids) != 0) return found;
            foreach (uint id in ids.AsSpan(0, (int)(size / sizeof(uint))))
                if (Read(id, TakingInput) is > 0 && Read(id, Pid) != (uint)Environment.ProcessId && BundleOf(id) is { Length: > 0 } bundle)
                    found.Add(bundle);
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
        {
        }
        return found;
    }

    static uint? Read(uint id, uint selector)
    {
        var a = new Address { Selector = selector, Scope = Global };
        uint size = sizeof(uint);
        return AudioObjectGetPropertyData(id, ref a, 0, IntPtr.Zero, ref size, out uint value) == 0 ? value : null;
    }

    static string? BundleOf(uint id)
    {
        var a = new Address { Selector = BundleId, Scope = Global };
        uint size = (uint)IntPtr.Size;
        if (AudioObjectGetPropertyData(id, ref a, 0, IntPtr.Zero, ref size, out IntPtr text) != 0 || text == IntPtr.Zero) return null;
        try
        {
            var buffer = new byte[256];
            return CFStringGetCString(text, buffer, buffer.Length, 0x08000100 /* UTF-8 */) ? System.Text.Encoding.UTF8.GetString(buffer).TrimEnd('\0') : null;
        }
        finally
        {
            CFRelease(text);
        }
    }

    // --- Windows: the ConsentStore, where Windows notes when each app last started and stopped using the microphone --

    const string MicStore = @"Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\microphone";

    /// <summary>The apps Windows says are using the microphone now (it hasn't noted them stopping): a program's file name
    /// ("Zoom.exe"), or a Store app's package name.</summary>
    [SupportedOSPlatform("windows")]
    static List<string> WinHolding()
    {
        var found = new List<string>();
        try
        {
            using var mic = Registry.CurrentUser.OpenSubKey(MicStore);
            if (mic is null) return found;
            foreach (string name in mic.GetSubKeyNames())
            {
                if (name != "NonPackaged")
                {
                    if (InUse(mic, name)) found.Add(name);
                    continue;
                }
                using var programs = mic.OpenSubKey(name);
                // Each program is its path with # for \: C:#Program Files#Zoom#bin#Zoom.exe.
                foreach (string path in programs?.GetSubKeyNames() ?? [])
                    if (InUse(programs!, path)) found.Add(path[(path.LastIndexOf('#') + 1)..]);
            }
        }
        catch (Exception e) when (e is System.Security.SecurityException or IOException or UnauthorizedAccessException)
        {
        }
        return found;
    }

    [SupportedOSPlatform("windows")]
    static bool InUse(RegistryKey parent, string name)
    {
        using var key = parent.OpenSubKey(name);
        return key?.GetValue("LastUsedTimeStop") is long stop && stop == 0 && key.GetValue("LastUsedTimeStart") is long start && start != 0;
    }

    /// <summary>One of the app's programs is running: a program that quit without Windows noting it stopped (it
    /// crashed) leaves its microphone record saying it's still in use.</summary>
    static bool WinRunning(Known app) => app.Win.Where(w => w.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)).Any(exe =>
    {
        var running = Process.GetProcessesByName(Path.GetFileNameWithoutExtension(exe));
        foreach (var p in running) p.Dispose();
        return running.Length > 0;
    });
}
