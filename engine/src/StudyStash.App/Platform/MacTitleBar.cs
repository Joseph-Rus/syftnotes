using System.Runtime.InteropServices;
using Avalonia.Controls;
using StudyStash.App.Controls;

namespace StudyStash.App.Platform;

/// <summary>A Mac window with its own 52 px title bar (<see cref="WindowHeader"/>): the traffic lights sit centred in
/// it, the way a Finder or Notes window has them. Avalonia leaves them at the plain title bar's spot, near the top
/// edge; an empty unified toolbar is the system's own way to give a window the taller bar, so that's what it adds.
/// Anything missing (another OS, no window handle) leaves the window as it was.</summary>
public static class MacTitleBar
{
    /// <summary>Title bar and toolbar in one row (NSWindowToolbarStyleUnified = 3), and the title hidden
    /// (NSWindowTitleHidden): the header draws its own.</summary>
    const long ToolbarStyleUnified = 3, TitleHidden = 1;

    /// <summary>Put the lights in the header now and whenever the window comes back from full screen or is shown
    /// again. Call before the window is shown.</summary>
    public static void Attach(Window window)
    {
        if (!OperatingSystem.IsMacOS()) return;
        window.Opened += (_, _) => Apply(window);
        window.PropertyChanged += (_, e) =>
        {
            if (e.Property == Window.WindowStateProperty && window.IsVisible) Apply(window);
        };
    }

    /// <summary>Where the lights are, and which way they got there, go to the log once per window.</summary>
    static void Apply(Window window)
    {
        try
        {
            string how = Place(window, out double before, out double after);
            ChromeProbe.Run(window);
            Program.Log($"[chrome] \"{window.Title}\": lights {how}; centre {Describe(before)} → {Describe(after)} from the top " +
                        $"(header {WindowHeader.MacHeight / 2:0} wanted)");
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException or MarshalDirectiveException)
        {
            Program.Log($"[chrome] \"{window.Title}\": lights left where the system put them ({e.GetType().Name})");
        }
    }

    static string Describe(double y) => double.IsNaN(y) ? "unknown" : $"{y:0.#} pt";

    static string Place(Window window, out double before, out double after)
    {
        before = after = double.NaN;
        IntPtr windowClass = ObjC.objc_getClass("NSWindow"), toolbarClass = ObjC.objc_getClass("NSToolbar");
        if (windowClass == IntPtr.Zero || toolbarClass == IntPtr.Zero) return "left alone (no AppKit)";
        IntPtr ns = window.TryGetPlatformHandle() is { HandleDescriptor: "NSWindow", Handle: var h } ? h : IntPtr.Zero;
        if (ns == IntPtr.Zero || !ObjC.IsKind(ns, windowClass)) return "left alone (no NSWindow)";

        before = LightsCentre(ns);
        if (Math.Abs(before - WindowHeader.MacHeight / 2) <= 2)
        {
            after = before;
            return "already centred by Avalonia";
        }
        if (ObjC.Send(ns, ObjC.Sel("toolbar")) == IntPtr.Zero)
        {
            IntPtr toolbar = ObjC.Send(ObjC.Send(toolbarClass, ObjC.Sel("alloc")), ObjC.Sel("initWithIdentifier:"), ObjC.Str("StudyStashHeader"));
            if (toolbar == IntPtr.Zero) return "left alone (no toolbar)";
            if (ObjC.Responds(toolbar, "setShowsBaselineSeparator:")) ObjC.SendByte(toolbar, ObjC.Sel("setShowsBaselineSeparator:"), 0);
            ObjC.Send(ns, ObjC.Sel("setToolbar:"), toolbar);
            ObjC.Send(toolbar, ObjC.Sel("release"));
        }
        if (ObjC.Responds(ns, "setToolbarStyle:")) ObjC.SendLong(ns, ObjC.Sel("setToolbarStyle:"), ToolbarStyleUnified);
        ObjC.SendLong(ns, ObjC.Sel("setTitleVisibility:"), TitleHidden);
        after = LightsCentre(ns);
        return "centred by an empty unified toolbar";
    }

    /// <summary>The close button's centre, in points down from the window's top edge. Only read on Apple silicon,
    /// where AppKit hands rectangles back in registers; elsewhere it's not known (and the toolbar goes on anyway).</summary>
    static double LightsCentre(IntPtr ns)
    {
        if (RuntimeInformation.ProcessArchitecture != Architecture.Arm64) return double.NaN;
        IntPtr close = ObjC.SendLongReturnsPtr(ns, ObjC.Sel("standardWindowButton:"), 0);
        if (close == IntPtr.Zero) return double.NaN;
        // The button's own bounds, into the window's (bottom-up) coordinates: that handles the button being flipped.
        var inWindow = ObjC.Convert(close, ObjC.Sel("convertRect:toView:"), ObjC.RectOf(close, ObjC.Sel("bounds")), IntPtr.Zero);
        var frame = ObjC.RectOf(ns, ObjC.Sel("frame"));
        return frame.H - (inWindow.Y + inWindow.H / 2);
    }

    [StructLayout(LayoutKind.Sequential)]
    readonly record struct CGRect(double X, double Y, double W, double H);

    static class ObjC
    {
        const string Lib = "/usr/lib/libobjc.A.dylib";

        [DllImport(Lib)]
        public static extern IntPtr objc_getClass(string name);

        [DllImport(Lib)]
        static extern IntPtr sel_registerName(string name);

        public static IntPtr Sel(string name) => sel_registerName(name);

        public static bool IsKind(IntPtr obj, IntPtr cls) => SendPtrReturnsByte(obj, Sel("isKindOfClass:"), cls) != 0;

        public static bool Responds(IntPtr obj, string selector) => SendPtrReturnsByte(obj, Sel("respondsToSelector:"), Sel(selector)) != 0;

        public static IntPtr Str(string s)
        {
            IntPtr utf8 = Marshal.StringToCoTaskMemUTF8(s);
            try
            {
                return Send(objc_getClass("NSString"), Sel("stringWithUTF8String:"), utf8);
            }
            finally
            {
                Marshal.FreeCoTaskMem(utf8);
            }
        }

        [DllImport(Lib, EntryPoint = "objc_msgSend")]
        public static extern IntPtr Send(IntPtr receiver, IntPtr selector);

        [DllImport(Lib, EntryPoint = "objc_msgSend")]
        public static extern IntPtr Send(IntPtr receiver, IntPtr selector, IntPtr arg);

        [DllImport(Lib, EntryPoint = "objc_msgSend")]
        public static extern IntPtr SendLongReturnsPtr(IntPtr receiver, IntPtr selector, long value);

        [DllImport(Lib, EntryPoint = "objc_msgSend")]
        static extern byte SendPtrReturnsByte(IntPtr receiver, IntPtr selector, IntPtr arg);

        [DllImport(Lib, EntryPoint = "objc_msgSend")]
        public static extern void SendByte(IntPtr receiver, IntPtr selector, byte value);

        [DllImport(Lib, EntryPoint = "objc_msgSend")]
        public static extern void SendLong(IntPtr receiver, IntPtr selector, long value);

        // Rectangles come back in registers on arm64 only (callers check); x64 would need objc_msgSend_stret.
        [DllImport(Lib, EntryPoint = "objc_msgSend")]
        public static extern CGRect RectOf(IntPtr receiver, IntPtr selector);

        [DllImport(Lib, EntryPoint = "objc_msgSend")]
        public static extern CGRect Convert(IntPtr receiver, IntPtr selector, CGRect rect, IntPtr view);
    }
}
