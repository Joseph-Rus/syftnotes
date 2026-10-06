using System.Diagnostics;
using System.Runtime.InteropServices;

namespace StudyStash.App.Platform;

/// <summary>
/// A Mac: quitting another app and opening it again, for an AI app that only reads its settings when it starts.
/// Closing its window isn't quitting it on a Mac, which is where "quit and reopen" goes wrong; this asks the app to
/// quit the way its own Quit menu does (it may still ask about unsaved work), waits for it to go, and opens it.
/// </summary>
public static class MacApps
{
    const string Lib = "/usr/lib/libobjc.A.dylib";

    [DllImport(Lib)] static extern IntPtr objc_getClass(string name);
    [DllImport(Lib)] static extern IntPtr sel_registerName(string name);
    [DllImport(Lib, EntryPoint = "objc_msgSend")] static extern IntPtr Send(IntPtr receiver, IntPtr selector);
    [DllImport(Lib, EntryPoint = "objc_msgSend")] static extern IntPtr SendPtr(IntPtr receiver, IntPtr selector, IntPtr arg);
    [DllImport(Lib, EntryPoint = "objc_msgSend")] static extern IntPtr SendText(IntPtr receiver, IntPtr selector, [MarshalAs(UnmanagedType.LPUTF8Str)] string arg);
    [DllImport(Lib, EntryPoint = "objc_msgSend")] static extern IntPtr SendIndex(IntPtr receiver, IntPtr selector, nuint index);
    [DllImport(Lib, EntryPoint = "objc_msgSend")] static extern nuint SendCount(IntPtr receiver, IntPtr selector);
    [DllImport(Lib, EntryPoint = "objc_msgSend")] static extern byte SendByte(IntPtr receiver, IntPtr selector);

    /// <summary>How long an app gets to quit before this gives up (it may be asking about unsaved work).</summary>
    public static readonly TimeSpan QuitWait = TimeSpan.FromSeconds(12);

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

    public static bool IsRunning(string bundleId) => Running(bundleId).Count > 0;

    /// <summary>Quits the app if it's running, then opens it. False when it wouldn't quit in time or wouldn't open:
    /// nothing is forced.</summary>
    public static async Task<bool> ReopenAsync(string bundleId, CancellationToken ct = default)
    {
        if (!OperatingSystem.IsMacOS()) return false;
        foreach (IntPtr app in Running(bundleId)) SendByte(app, sel_registerName("terminate"));
        var until = DateTime.UtcNow + QuitWait;
        while (IsRunning(bundleId))
        {
            if (DateTime.UtcNow > until) return false;
            await Task.Delay(250, ct);
        }
        try
        {
            using var open = Process.Start(new ProcessStartInfo("/usr/bin/open") { ArgumentList = { "-b", bundleId }, UseShellExecute = false, CreateNoWindow = true });
            if (open is null) return false;
            await open.WaitForExitAsync(ct);
            return open.ExitCode == 0;
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            return false;
        }
    }
}
