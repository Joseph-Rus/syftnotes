using System.Runtime.InteropServices;

namespace StudyStash.App.Platform;

/// <summary>
/// A Mac: a notification never leaves you outside the app you were typing in. It shows without taking the keyboard,
/// but AppKit brings Study Stash forward on any click in one of its windows, so the app that was in front when the
/// pointer came onto the notification is remembered, and brought back after a click that opens nothing of Study
/// Stash's own (the ×, Later). No-op off a Mac.
/// </summary>
public static class MacFocus
{
    const string Lib = "/usr/lib/libobjc.A.dylib";

    [DllImport(Lib)] static extern IntPtr objc_getClass(string name);
    [DllImport(Lib)] static extern IntPtr sel_registerName(string name);
    [DllImport(Lib, EntryPoint = "objc_msgSend")] static extern IntPtr Send(IntPtr receiver, IntPtr selector);
    [DllImport(Lib, EntryPoint = "objc_msgSend")] static extern int SendInt(IntPtr receiver, IntPtr selector);
    [DllImport(Lib, EntryPoint = "objc_msgSend")] static extern byte SendByte(IntPtr receiver, IntPtr selector);
    [DllImport(Lib, EntryPoint = "objc_msgSend")] static extern IntPtr SendPid(IntPtr receiver, IntPtr selector, int pid);
    [DllImport(Lib, EntryPoint = "objc_msgSend")] static extern byte SendOptions(IntPtr receiver, IntPtr selector, nuint options);

    /// <summary>The app in front when the pointer came onto a notification (its process), unless that was this one.</summary>
    static int? before;

    /// <summary>The pointer came onto a notification: remember which app is in front.</summary>
    public static void Remember()
    {
        if (!OperatingSystem.IsMacOS()) return;
        try
        {
            IntPtr workspace = Send(objc_getClass("NSWorkspace"), sel_registerName("sharedWorkspace"));
            IntPtr front = workspace == IntPtr.Zero ? IntPtr.Zero : Send(workspace, sel_registerName("frontmostApplication"));
            if (front == IntPtr.Zero) return;
            int pid = SendInt(front, sel_registerName("processIdentifier"));
            before = pid > 0 && pid != Environment.ProcessId ? pid : null;
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
        {
        }
    }

    /// <summary>The click is done and opened nothing of Study Stash's: if that click brought Study Stash forward, the
    /// app you were in comes back to the front.</summary>
    public static void GiveBack()
    {
        if (!OperatingSystem.IsMacOS() || before is not { } pid) return;
        before = null;
        try
        {
            IntPtr app = Send(objc_getClass("NSApplication"), sel_registerName("sharedApplication"));
            if (app == IntPtr.Zero || SendByte(app, sel_registerName("isActive")) == 0) return;
            IntPtr other = SendPid(objc_getClass("NSRunningApplication"), sel_registerName("runningApplicationWithProcessIdentifier:"), pid);
            if (other != IntPtr.Zero) SendOptions(other, sel_registerName("activateWithOptions:"), 0);
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
        {
        }
    }
}
