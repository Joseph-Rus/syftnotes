using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace StudyStash.App.Platform;

/// <summary>
/// Linux's global shortcuts under X11: XGrabKey on the root window, from a connection to the X server of its own,
/// read on a thread of its own (Avalonia's connection is never touched). Each shortcut is grabbed with and without
/// Caps Lock and Num Lock, which X counts as modifiers. A grab another app holds already fails with BadAccess, which is
/// caught for this connection only (any other error goes to whoever handled them before), so Settings can say which
/// shortcut is taken.
/// </summary>
[SupportedOSPlatform("linux")]
static unsafe class X11Keys
{
    const string X11 = "libX11.so.6";
    const int KeyPress = 2, GrabModeAsync = 1, BadAccess = 10;
    const uint LockMask = 0x2, Mod2Mask = 0x10; // Caps Lock, Num Lock

    [DllImport(X11)]
    static extern IntPtr XOpenDisplay(IntPtr name);

    [DllImport(X11)]
    static extern int XCloseDisplay(IntPtr display);

    [DllImport(X11)]
    static extern IntPtr XDefaultRootWindow(IntPtr display);

    [DllImport(X11)]
    static extern IntPtr XStringToKeysym(string name);

    [DllImport(X11)]
    static extern byte XKeysymToKeycode(IntPtr display, IntPtr keysym);

    [DllImport(X11)]
    static extern int XGrabKey(IntPtr display, int keycode, uint modifiers, IntPtr window, int ownerEvents, int pointerMode, int keyboardMode);

    [DllImport(X11)]
    static extern int XSync(IntPtr display, int discard);

    [DllImport(X11)]
    static extern int XPending(IntPtr display);

    [DllImport(X11)]
    static extern int XNextEvent(IntPtr display, byte* ev);

    [DllImport(X11)]
    static extern IntPtr XSetErrorHandler(IntPtr handler);

    /// <summary>A Wayland session: X11's grabs only reach XWayland's own windows there, so they'd seem to work and
    /// never fire.</summary>
    public static bool Wayland => Environment.GetEnvironmentVariable("XDG_SESSION_TYPE") == "wayland"
        || Environment.GetEnvironmentVariable("DISPLAY") is not { Length: > 0 };

    static Thread? reader;
    static volatile bool stopping;
    static IntPtr ours, previousHandler;
    static volatile bool denied;

    /// <summary>The modifiers X counts that a shortcut shouldn't care about.</summary>
    static readonly uint[] Locks = [0, LockMask, Mod2Mask, LockMask | Mod2Mask];

    public static HotkeyResult Register(KeyCombo quick, KeyCombo record, Action<Shortcut> fire)
    {
        Unregister();
        var ready = new TaskCompletionSource<HotkeyResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        stopping = false;
        reader = new Thread(() => Run(quick, record, fire, ready)) { IsBackground = true, Name = "Syft Notes shortcuts" };
        reader.Start();
        return ready.Task.Wait(TimeSpan.FromSeconds(5)) ? ready.Task.Result : new HotkeyResult(false, false);
    }

    public static void Unregister()
    {
        stopping = true;
        reader?.Join(TimeSpan.FromSeconds(2));
        reader = null;
    }

    static void Run(KeyCombo quick, KeyCombo record, Action<Shortcut> fire, TaskCompletionSource<HotkeyResult> ready)
    {
        IntPtr display = XOpenDisplay(IntPtr.Zero);
        if (display == IntPtr.Zero)
        {
            ready.SetResult(new HotkeyResult(false, false));
            return;
        }
        try
        {
            IntPtr root = XDefaultRootWindow(display);
            var keys = new List<(int Code, uint Mods, Shortcut What)>();
            bool Grab(KeyCombo combo, Shortcut what)
            {
                var (name, mods) = combo.X11();
                int code = name.Length == 0 ? 0 : XKeysymToKeycode(display, XStringToKeysym(name));
                if (code == 0) return false;
                denied = false;
                ours = display;
                previousHandler = XSetErrorHandler((IntPtr)(delegate* unmanaged<IntPtr, IntPtr, int>)&OnError);
                foreach (uint locks in Locks) XGrabKey(display, code, mods | locks, root, 0, GrabModeAsync, GrabModeAsync);
                XSync(display, 0);
                XSetErrorHandler(previousHandler);
                if (denied) return false;
                keys.Add((code, mods, what));
                return true;
            }
            ready.SetResult(new HotkeyResult(Grab(quick, Shortcut.Quick), Grab(record, Shortcut.Record)));
            // XEvent is a union of 24 longs; a key press's state is at byte 80 and its keycode at 84 (64-bit).
            byte* ev = stackalloc byte[24 * 8];
            while (!stopping)
            {
                while (XPending(display) > 0)
                {
                    XNextEvent(display, ev);
                    if (*(int*)ev != KeyPress) continue;
                    uint state = *(uint*)(ev + 80) & ~(LockMask | Mod2Mask);
                    int code = (int)*(uint*)(ev + 84);
                    foreach (var k in keys)
                        if (k.Code == code && k.Mods == state) fire(k.What);
                }
                Thread.Sleep(50);
            }
        }
        finally
        {
            XCloseDisplay(display); // lets every grab go
        }
    }

    /// <summary>XErrorEvent: int type, the display it came from, a resource id and a serial; its error code is the byte at
    /// 32 (64-bit).</summary>
    [UnmanagedCallersOnly]
    static int OnError(IntPtr display, IntPtr error)
    {
        if (display == ours)
        {
            if (((byte*)error)[32] == BadAccess) denied = true;
            return 0;
        }
        return previousHandler == IntPtr.Zero ? 0 : ((delegate* unmanaged<IntPtr, IntPtr, int>)previousHandler)(display, error);
    }
}
