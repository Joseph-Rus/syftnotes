using System.Runtime.InteropServices;
using System.Text;

namespace StudyStash.App.Platform;

/// <summary>Pictures of the app's own Mac windows, for checking them by eye on a computer where the terminal has no
/// screen-recording permission (an app may always picture its own windows). Only with STUDYSTASH_SNAPS set to a folder:
/// a later copy's `--show snap:&lt;name&gt;` saves each window on screen as &lt;name&gt;-&lt;n&gt;.png there, the system's
/// full-screen title strip included, plus &lt;name&gt;-screen.png of the whole screen as this app may see it.</summary>
public static class MacSnap
{
    public const string Variable = "STUDYSTASH_SNAPS";

    /// <summary>Where pictures go, or null when they're off.</summary>
    public static string? Folder => Environment.GetEnvironmentVariable(Variable) is { Length: > 0 } dir ? dir : null;

    /// <summary>A picture's name: letters, digits and dashes, so it can't reach outside the folder.</summary>
    public static bool IsName(string name) => name.Length is > 0 and <= 40 && name.All(c => char.IsAsciiLetterOrDigit(c) || c == '-');

    // CGWindowListOption / CGWindowImageOption
    const uint OnScreenOnly = 1, IncludingWindow = 1 << 3, IgnoreFraming = 1, BestResolution = 1 << 3;

    /// <summary>Saves the pictures and logs what each window is (its class, frame and whether it's the full-screen
    /// strip), so the log says what the pictures show.</summary>
    public static void Save(string name)
    {
        if (!OperatingSystem.IsMacOS() || Folder is not { } dir || !IsName(name)) return;
        try
        {
            Directory.CreateDirectory(dir);
            IntPtr app = MacTitleBar.ObjC.Send(MacTitleBar.ObjC.objc_getClass("NSApplication"), MacTitleBar.ObjC.Sel("sharedApplication"));
            IntPtr windows = MacTitleBar.ObjC.Send(app, MacTitleBar.ObjC.Sel("windows"));
            long count = windows == IntPtr.Zero ? 0 : (long)MacTitleBar.ObjC.Send(windows, MacTitleBar.ObjC.Sel("count"));
            int n = 0;
            for (long i = 0; i < count; i++)
            {
                IntPtr w = MacTitleBar.ObjC.SendLongReturnsPtr(windows, MacTitleBar.ObjC.Sel("objectAtIndex:"), i);
                if (MacTitleBar.ObjC.SendReturnsByte(w, MacTitleBar.ObjC.Sel("isVisible")) == 0) continue;
                long number = (long)MacTitleBar.ObjC.Send(w, MacTitleBar.ObjC.Sel("windowNumber"));
                string file = Path.Combine(dir, $"{name}-{++n}.png");
                bool saved = SavePng(CGWindowListCreateImage(Null, IncludingWindow, (uint)number, IgnoreFraming | BestResolution), file);
                var f = RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? MacTitleBar.ObjC.RectOf(w, MacTitleBar.ObjC.Sel("frame")) : default;
                bool here = MacTitleBar.ObjC.SendReturnsByte(w, MacTitleBar.ObjC.Sel("isOnActiveSpace")) != 0;
                long occlusion = (long)MacTitleBar.ObjC.Send(w, MacTitleBar.ObjC.Sel("occlusionState"));
                Program.Log($"[snap] {name}-{n}: {MacTitleBar.ObjC.ClassName(w)} #{number} at {f.X:0},{f.Y:0} {f.W:0}×{f.H:0}" +
                            $"{(here ? "" : " (on another Space)")}{((occlusion & 2) != 0 ? "" : " (not on screen)")}{(saved ? "" : " (no picture)")}");
                // The system's own full-screen windows: what they're made of, view by view.
                if (MacTitleBar.ObjC.ClassName(w).Contains("FullScreen", StringComparison.Ordinal))
                    Views(MacTitleBar.ObjC.Send(MacTitleBar.ObjC.Send(w, MacTitleBar.ObjC.Sel("contentView")), MacTitleBar.ObjC.Sel("superview")), 1);
            }
            bool screen = SavePng(CGWindowListCreateImage(Infinite, OnScreenOnly, 0, BestResolution), Path.Combine(dir, $"{name}-screen.png"));
            Program.Log($"[snap] {name}: {n} window(s){(screen ? " and the screen" : "")} saved");
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException or MarshalDirectiveException or IOException or UnauthorizedAccessException)
        {
            Program.Log($"[snap] {name}: no pictures ({e.GetType().Name}: {e.Message})");
        }
    }

    static void Views(IntPtr view, int depth)
    {
        if (view == IntPtr.Zero || depth > 8) return;
        var f = RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? MacTitleBar.ObjC.RectOf(view, MacTitleBar.ObjC.Sel("frame")) : default;
        bool hidden = MacTitleBar.ObjC.SendReturnsByte(view, MacTitleBar.ObjC.Sel("isHidden")) != 0;
        bool shadow = MacTitleBar.ObjC.Send(view, MacTitleBar.ObjC.Sel("shadow")) != IntPtr.Zero;
        IntPtr layer = MacTitleBar.ObjC.Send(view, MacTitleBar.ObjC.Sel("layer"));
        float layerShadow = layer == IntPtr.Zero ? 0 : SendReturnsFloat(layer, MacTitleBar.ObjC.Sel("shadowOpacity"));
        Program.Log($"[snap] {new string(' ', depth * 2)}{MacTitleBar.ObjC.ClassName(view)} {f.X:0},{f.Y:0} {f.W:0}×{f.H:0}{(hidden ? " hidden" : "")}"
                    + $"{(shadow ? " shadow" : "")}{(layerShadow > 0 ? $" layer-shadow {layerShadow:0.##}" : "")}{(layer != IntPtr.Zero ? " layer " + MacTitleBar.ObjC.ClassName(layer) : "")}");
        if (layer != IntPtr.Zero) Layers(layer, depth + 1);
        IntPtr subviews = MacTitleBar.ObjC.Send(view, MacTitleBar.ObjC.Sel("subviews"));
        long count = subviews == IntPtr.Zero ? 0 : (long)MacTitleBar.ObjC.Send(subviews, MacTitleBar.ObjC.Sel("count"));
        for (long i = 0; i < count; i++) Views(MacTitleBar.ObjC.SendLongReturnsPtr(subviews, MacTitleBar.ObjC.Sel("objectAtIndex:"), i), depth + 1);
    }

    static void Layers(IntPtr layer, int depth)
    {
        IntPtr subs = MacTitleBar.ObjC.Send(layer, MacTitleBar.ObjC.Sel("sublayers"));
        long count = subs == IntPtr.Zero ? 0 : (long)MacTitleBar.ObjC.Send(subs, MacTitleBar.ObjC.Sel("count"));
        for (long i = 0; i < count && depth < 12; i++)
        {
            IntPtr l = MacTitleBar.ObjC.SendLongReturnsPtr(subs, MacTitleBar.ObjC.Sel("objectAtIndex:"), i);
            float sh = SendReturnsFloat(l, MacTitleBar.ObjC.Sel("shadowOpacity"));
            var f = MacTitleBar.ObjC.RectOf(l, MacTitleBar.ObjC.Sel("frame"));
            Program.Log($"[snap] {new string(' ', depth * 2)}~{MacTitleBar.ObjC.ClassName(l)} {f.X:0},{f.Y:0} {f.W:0}×{f.H:0}{(sh > 0 ? $" shadow {sh:0.##}" : "")}");
            Layers(l, depth + 1);
        }
    }

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    static extern float SendReturnsFloat(IntPtr receiver, IntPtr selector);

    static bool SavePng(IntPtr image, string file)
    {
        if (image == IntPtr.Zero) return false;
        IntPtr url = IntPtr.Zero, type = IntPtr.Zero, dest = IntPtr.Zero;
        try
        {
            byte[] path = Encoding.UTF8.GetBytes(file);
            url = CFURLCreateFromFileSystemRepresentation(IntPtr.Zero, path, path.Length, 0);
            type = CFStringCreateWithCString(IntPtr.Zero, "public.png", 0x08000100);
            dest = url == IntPtr.Zero || type == IntPtr.Zero ? IntPtr.Zero : CGImageDestinationCreateWithURL(url, type, 1, IntPtr.Zero);
            if (dest == IntPtr.Zero) return false;
            CGImageDestinationAddImage(dest, image, IntPtr.Zero);
            return CGImageDestinationFinalize(dest) != 0;
        }
        finally
        {
            foreach (IntPtr p in new[] { dest, type, url, image })
                if (p != IntPtr.Zero) CFRelease(p);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    readonly record struct Rect(double X, double Y, double W, double H);

    static readonly Rect Null = new(double.PositiveInfinity, double.PositiveInfinity, 0, 0);
    static readonly Rect Infinite = new(-double.MaxValue / 2, -double.MaxValue / 2, double.MaxValue, double.MaxValue);

    const string CoreGraphics = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";
    const string ImageIO = "/System/Library/Frameworks/ImageIO.framework/ImageIO";
    const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

    [DllImport(CoreGraphics)]
    static extern IntPtr CGWindowListCreateImage(Rect bounds, uint listOption, uint windowId, uint imageOption);

    [DllImport(ImageIO)]
    static extern IntPtr CGImageDestinationCreateWithURL(IntPtr url, IntPtr type, nint count, IntPtr options);

    [DllImport(ImageIO)]
    static extern void CGImageDestinationAddImage(IntPtr dest, IntPtr image, IntPtr properties);

    [DllImport(ImageIO)]
    static extern byte CGImageDestinationFinalize(IntPtr dest);

    [DllImport(CoreFoundation)]
    static extern IntPtr CFURLCreateFromFileSystemRepresentation(IntPtr allocator, byte[] buffer, nint length, byte isDirectory);

    [DllImport(CoreFoundation)]
    static extern IntPtr CFStringCreateWithCString(IntPtr allocator, string text, uint encoding);

    [DllImport(CoreFoundation)]
    static extern void CFRelease(IntPtr obj);
}
