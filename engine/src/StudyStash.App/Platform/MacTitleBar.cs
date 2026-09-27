using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Threading;
using StudyStash.App.Controls;

namespace StudyStash.App.Platform;

/// <summary>A Mac window with its own 52 px title bar (<see cref="WindowHeader"/>): the traffic lights sit centred in
/// it, the way a Finder or Notes window has them. Avalonia leaves them at the plain title bar's spot, near the top
/// edge; an empty unified toolbar is the system's own way to give a window the taller bar, so that's what it adds.
/// In full screen the toolbar stays, the way a native window keeps its own toolbar row: macOS shows the lights only when
/// the menu bar slides down, on a strip of the toolbar's height, which it would paint grey across the header. That
/// strip is cleared, so at rest the header is just the header and on the reveal the lights sit centred in it.
/// Anything missing (another OS, no window handle) leaves the window as it was.</summary>
public static class MacTitleBar
{
    /// <summary>Title bar and toolbar in one row (NSWindowToolbarStyleUnified = 3), and the title hidden
    /// (NSWindowTitleHidden): the header draws its own.</summary>
    const long ToolbarStyleUnified = 3, TitleHidden = 1;

    /// <summary>Put the lights in the header now, and again whenever the window goes into or out of full screen or
    /// zooms. Call before the window is shown.</summary>
    public static void Attach(Window window)
    {
        if (!OperatingSystem.IsMacOS()) return;
        window.Opened += (_, _) => Apply(window);
        window.PropertyChanged += (_, e) =>
        {
            if (e.Property == Window.WindowStateProperty && window.IsVisible) Apply(window);
        };
    }

    /// <summary>What the lights do now, and which way they got there, goes to the log.</summary>
    static void Apply(Window window)
    {
        try
        {
            string how = Place(window, out double before, out double after);
            Program.Log($"[chrome] \"{window.Title}\" ({window.WindowState}): lights {how}; centre {Describe(before)} → {Describe(after)} " +
                        $"from the top (header {WindowHeader.MacHeight / 2:0} wanted)");
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

        bool hasToolbar = ObjC.Send(ns, ObjC.Sel("toolbar")) != IntPtr.Zero;
        if (window.WindowState == WindowState.FullScreen)
        {
            // Full screen keeps the unified toolbar, so the strip the lights come down on with the menu bar is the
            // header's own height: the lights land centred in the header, as in Finder or Notes. That strip is a
            // window of its own (made during the system's switch), painted grey; clear it, so at rest the header
            // shows as it is and on the reveal only the lights appear. Again a little later, as the switch can
            // outlast the first try. The title bar stays clear too (full screen from the green button would make
            // it opaque again).
            ObjC.SendByte(ns, ObjC.Sel("setTitlebarAppearsTransparent:"), 1);
            int cleared = ClearFullScreenStrip(ns);
            foreach (int ms in new[] { 800, 2000 })
                DispatcherTimer.RunOnce(() =>
                {
                    if (window is not { IsVisible: true, WindowState: WindowState.FullScreen }) return;
                    try
                    {
                        ObjC.SendByte(ns, ObjC.Sel("setTitlebarAppearsTransparent:"), 1);
                        int n = ClearFullScreenStrip(ns);
                        if (n > 0) Program.Log($"[chrome] \"{window.Title}\": full screen's title strip is clear ({n} backgrounds and shadows hidden)");
                    }
                    catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException or MarshalDirectiveException)
                    {
                    }
                }, TimeSpan.FromMilliseconds(ms));
            if (hasToolbar) return $"come down with the menu bar in full screen, centred in the header on a clear strip ({cleared} backgrounds and shadows hidden now)";
        }
        before = LightsCentre(ns);
        if (!hasToolbar)
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
        return hasToolbar ? "kept centred by the unified toolbar" : "centred by an empty unified toolbar";
    }

    /// <summary>In full screen the toolbar and the lights live in a strip of their own that slides down with the menu
    /// bar, painted grey (its title bar background and the line under it) and casting a shadow: hide those two and the
    /// shadow, so the strip shows only the lights over the window's own header. How many it hid; 0 before the strip
    /// exists, or once they're hidden.
    /// (Out of full screen the title bar is clear anyway, so the hidden background changes nothing there.)</summary>
    static int ClearFullScreenStrip(IntPtr ns)
    {
        IntPtr viewClass = ObjC.objc_getClass("NSView");
        IntPtr close = ObjC.SendLongReturnsPtr(ns, ObjC.Sel("standardWindowButton:"), 0);
        IntPtr strip = close == IntPtr.Zero ? IntPtr.Zero : ObjC.Send(close, ObjC.Sel("window"));
        if (strip == IntPtr.Zero || viewClass == IntPtr.Zero || strip == ns) return 0;
        int hidden = 0;
        // The strip's own shadow would still draw its edge across the window when it comes down: it casts none.
        if (ObjC.SendReturnsByte(strip, ObjC.Sel("hasShadow")) != 0)
        {
            ObjC.SendByte(strip, ObjC.Sel("setHasShadow:"), 0);
            hidden++;
        }
        // The lights' title bar view, then its container: each holds one of the two painted views.
        IntPtr bar = ObjC.Send(close, ObjC.Sel("superview"));
        foreach (IntPtr v in new[] { bar, bar == IntPtr.Zero ? IntPtr.Zero : ObjC.Send(bar, ObjC.Sel("superview")) })
        {
            if (v == IntPtr.Zero || !ObjC.IsKind(v, viewClass)) continue;
            IntPtr subviews = ObjC.Send(v, ObjC.Sel("subviews"));
            long count = subviews == IntPtr.Zero ? 0 : (long)ObjC.Send(subviews, ObjC.Sel("count"));
            for (long i = 0; i < count; i++)
            {
                IntPtr sub = ObjC.SendLongReturnsPtr(subviews, ObjC.Sel("objectAtIndex:"), i);
                if (!ObjC.IsKind(sub, viewClass) || !PaintsTheStrip(ObjC.ClassName(sub))) continue;
                if (ObjC.SendReturnsByte(sub, ObjC.Sel("isHidden")) != 0) continue;
                ObjC.SendByte(sub, ObjC.Sel("setHidden:"), 1);
                hidden++;
            }
        }
        // And the shadow it draws under the header: a plain layer of its own in the strip's spare height below the
        // title bar, which would lay a dark edge across the window. (Rectangles are only read on Apple silicon.)
        IntPtr content = ObjC.Send(strip, ObjC.Sel("contentView"));
        IntPtr stripLayer = content == IntPtr.Zero ? IntPtr.Zero : ObjC.Send(content, ObjC.Sel("layer"));
        if (RuntimeInformation.ProcessArchitecture == Architecture.Arm64 && bar != IntPtr.Zero && stripLayer != IntPtr.Zero)
        {
            double barHeight = ObjC.RectOf(bar, ObjC.Sel("frame")).H;
            IntPtr layers = ObjC.Send(stripLayer, ObjC.Sel("sublayers"));
            long count = layers == IntPtr.Zero ? 0 : (long)ObjC.Send(layers, ObjC.Sel("count"));
            for (long i = 0; i < count; i++)
            {
                IntPtr layer = ObjC.SendLongReturnsPtr(layers, ObjC.Sel("objectAtIndex:"), i);
                if (!IsShadowUnderBar(ObjC.ClassName(layer), ObjC.RectOf(layer, ObjC.Sel("frame")).Y, barHeight)) continue;
                if (ObjC.SendReturnsByte(layer, ObjC.Sel("isHidden")) != 0) continue;
                ObjC.SendByte(layer, ObjC.Sel("setHidden:"), 1);
                hidden++;
            }
        }
        return hidden;
    }

    /// <summary>The full-screen strip's shadow under the header: a plain CALayer (no view of its own) lying wholly below
    /// the title bar. Never the title bar's own layers, which start at its top.</summary>
    internal static bool IsShadowUnderBar(string className, double top, double barHeight) =>
        className == "CALayer" && barHeight > 0 && top >= barHeight - 0.5;

    /// <summary>The two views that paint the full-screen strip grey: its title bar background, and the line under it
    /// (AppKit's own classes, sometimes under a KVO subclass's name). Never the lights, the toolbar or anything else.</summary>
    internal static bool PaintsTheStrip(string className) =>
        className.EndsWith("NSTitlebarBackgroundView", StringComparison.Ordinal) || className.EndsWith("NSTitlebarDecorationView", StringComparison.Ordinal);

    /// <summary>The close button's centre, in points down from the window's top edge. Only read on Apple silicon,
    /// where AppKit hands rectangles back in registers; elsewhere it's not known (and the toolbar goes on anyway).</summary>
    static double LightsCentre(IntPtr ns)
    {
        if (RuntimeInformation.ProcessArchitecture != Architecture.Arm64) return double.NaN;
        IntPtr close = ObjC.SendLongReturnsPtr(ns, ObjC.Sel("standardWindowButton:"), 0);
        // Coming out of full screen the buttons are still in the system's own full-screen title bar window.
        if (close == IntPtr.Zero || ObjC.Send(close, ObjC.Sel("window")) != ns) return double.NaN;
        // The button's own bounds, into the window's (bottom-up) coordinates: that handles the button being flipped.
        var inWindow = ObjC.Convert(close, ObjC.Sel("convertRect:toView:"), ObjC.RectOf(close, ObjC.Sel("bounds")), IntPtr.Zero);
        var frame = ObjC.RectOf(ns, ObjC.Sel("frame"));
        return frame.H - (inWindow.Y + inWindow.H / 2);
    }

    [StructLayout(LayoutKind.Sequential)]
    internal readonly record struct CGRect(double X, double Y, double W, double H);

    internal static class ObjC
    {
        const string Lib = "/usr/lib/libobjc.A.dylib";

        [DllImport(Lib)]
        public static extern IntPtr objc_getClass(string name);

        [DllImport(Lib)]
        static extern IntPtr sel_registerName(string name);

        public static IntPtr Sel(string name) => sel_registerName(name);

        public static bool IsKind(IntPtr obj, IntPtr cls) => SendPtrReturnsByte(obj, Sel("isKindOfClass:"), cls) != 0;

        [DllImport(Lib)]
        static extern IntPtr object_getClassName(IntPtr obj);

        public static string ClassName(IntPtr obj) => obj == IntPtr.Zero ? "" : Marshal.PtrToStringAnsi(object_getClassName(obj)) ?? "";

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
        public static extern byte SendReturnsByte(IntPtr receiver, IntPtr selector);

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
