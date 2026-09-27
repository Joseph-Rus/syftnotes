using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SkiaSharp;

namespace StudyStash.App.Controls;

/// <summary>
/// The Mac's real desktop blur behind a floating window's glass (the dropdown, the recorder, the quick panel). The
/// window itself stays clear (never a window-wide blur, which would show as a box round the panel): instead each
/// visible <see cref="Glass"/> gets its own system <c>NSVisualEffectView</c>, the size and rounded shape of that glass,
/// placed just under the app's drawing. So the blur shows only through the glass, and round it there's nothing but its
/// shadow. When any step fails — no window handle, an interop call that isn't there — the glass stays
/// <c>solidglass</c> (the design's glass colour painted solid), which is always readable. <c>STUDYSTASH_GLASS=solid</c>
/// skips the blur entirely, to compare the two by eye.
/// </summary>
public static class GlassBackdrop
{
    static bool Enabled => OperatingSystem.IsMacOS() && Skin.Current == SkinKind.Mac &&
        Environment.GetEnvironmentVariable("STUDYSTASH_GLASS") != "solid";

    /// <summary>The system material under the glass: a popover's, which follows light and dark.</summary>
    const long MaterialPopover = 6;

    /// <summary>How far the blur sits inside the glass's edge, so its antialiased rim never peeks out past the
    /// glass's own (the glass's fill covers the blur, not the other way round).</summary>
    public const double Inset = 0.5;

    sealed class State
    {
        public readonly List<IntPtr> Views = [];
        public List<(Rect Rect, CornerRadius Radius)>? Shapes;
        public bool Dark;
        public double Height;
        public bool Failed;
        public int Logged = -1;
        public DispatcherTimer? Debounce;
    }

    static readonly ConditionalWeakTable<Window, State> States = new();

    const string NoHandle = "no window handle";

    /// <summary>Put the OS blur under a floating window's glass, and keep it there as the window changes. Safe to call
    /// for every window: it does nothing off macOS, off the Mac skin, or with the opt-out set. The window's
    /// transparency is left as it is (clear): the blur is only ever as big as the glass.</summary>
    public static void Attach(Window window)
    {
        if (!Enabled) return;
        window.Opened += (_, _) => Refresh(window);
        window.ActualThemeVariantChanged += (_, _) => Refresh(window);
        window.LayoutUpdated += (_, _) =>
        {
            var state = States.GetOrCreateValue(window);
            state.Debounce ??= new DispatcherTimer(TimeSpan.FromMilliseconds(50), DispatcherPriority.Background, (_, _) =>
            {
                state.Debounce!.Stop();
                Refresh(window);
            });
            state.Debounce.Stop();
            state.Debounce.Start();
        };
        window.Closed += (_, _) =>
        {
            if (States.TryGetValue(window, out var state)) RemoveAll(state);
        };
    }

    /// <summary>Where a glass's blur goes in AppKit's terms, in points: Avalonia's DIPs are AppKit's points, and a view
    /// that isn't flipped counts y up from the bottom of its superview. Pulled in by <see cref="Inset"/> all round.</summary>
    public static Rect FrameFor(Rect glass, double contentHeight, bool flipped)
    {
        var r = glass.Deflate(Inset);
        return flipped ? r : new Rect(r.X, contentHeight - r.Y - r.Height, r.Width, r.Height);
    }

    /// <summary>The autoresizing mask that keeps a blur view pinned to its superview's top-left while the window grows
    /// or shrinks round it (until the next layout puts it exactly back): a flexible right margin, and a flexible
    /// bottom margin — <c>NSViewMinYMargin</c> when y counts up, <c>NSViewMaxYMargin</c> when the view is flipped.</summary>
    public static ulong AutoresizingFor(bool flipped) => 4 | (flipped ? 32ul : 8ul);

    static void Refresh(Window window)
    {
        var state = States.GetOrCreateValue(window);
        if (state.Failed || !window.IsVisible) return;
        try
        {
            if (Sync(window, state) is { } why)
            {
                // No handle at all is a window with nothing native behind it (the tests' headless ones): nothing to say.
                if (why != NoHandle) Program.Log($"[glass] \"{window.Title}\": no system blur ({why}); the glass stays solid");
                Fail(window, state);
            }
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException or MarshalDirectiveException)
        {
            Program.Log($"[glass] no system blur ({e.GetType().Name}): the glass stays solid");
            Fail(window, state);
        }
    }

    static void Fail(Window window, State state)
    {
        state.Failed = true;
        RemoveAll(state);
        if (!window.Classes.Contains("solidglass")) window.Classes.Add("solidglass");
    }

    /// <summary>Make the window's blur views match its visible glass: one each, same place, same corners. Null when
    /// it worked, else why not.</summary>
    static string? Sync(Window window, State state)
    {
        if (!OperatingSystem.IsMacOS()) return "not a Mac";
        // Avalonia hands the NSWindow (older backends handed its view): the blur goes in the window's content view,
        // below everything Avalonia put there.
        IntPtr windowClass = ObjC.objc_getClass("NSWindow"), viewClass = ObjC.objc_getClass("NSView");
        IntPtr effectClass = ObjC.objc_getClass("NSVisualEffectView");
        if (windowClass == IntPtr.Zero || viewClass == IntPtr.Zero || effectClass == IntPtr.Zero) return "no NSVisualEffectView";
        IntPtr nsWindow = window.TryGetPlatformHandle() switch
        {
            { HandleDescriptor: "NSWindow", Handle: var w } => w,
            { HandleDescriptor: "NSView", Handle: var v } when v != IntPtr.Zero && ObjC.IsKind(v, viewClass) => ObjC.Send(v, ObjC.Sel("window")),
            _ => IntPtr.Zero,
        };
        if (nsWindow == IntPtr.Zero) return NoHandle;
        if (!ObjC.IsKind(nsWindow, windowClass)) return "the handle isn't a window";
        IntPtr container = ObjC.Send(nsWindow, ObjC.Sel("contentView"));
        if (container == IntPtr.Zero || !ObjC.IsKind(container, viewClass)) return "the window has no content view";

        bool flipped = ObjC.SendReturnsByte(container, ObjC.Sel("isFlipped")) != 0;
        bool dark = window.ActualThemeVariant == ThemeVariant.Dark;
        // Until the window has taken its content's size, the glass isn't where it will be: the next layout brings it.
        if (Math.Abs(window.Bounds.Width - window.ClientSize.Width) > 0.5 || Math.Abs(window.Bounds.Height - window.ClientSize.Height) > 0.5)
            return null;
        var shapes = CollectGlass(window);
        double height = window.ClientSize.Height;
        if (state.Shapes is { } last && SameShapes(last, shapes) && state.Dark == dark && state.Height == height &&
            state.Views.Count == shapes.Count) return null;

        while (state.Views.Count > shapes.Count)
        {
            Remove(state.Views[^1]);
            state.Views.RemoveAt(state.Views.Count - 1);
        }
        while (state.Views.Count < shapes.Count)
        {
            IntPtr view = ObjC.Send(ObjC.Send(effectClass, ObjC.Sel("alloc")), ObjC.Sel("init"));
            if (view == IntPtr.Zero) return "couldn't make a blur view";
            ObjC.SendLong(view, ObjC.Sel("setBlendingMode:"), 0); // behind the window: the desktop
            ObjC.SendLong(view, ObjC.Sel("setState:"), 1); // always active: a floating panel is often not the key window
            ObjC.SendLong(view, ObjC.Sel("setMaterial:"), MaterialPopover);
            ObjC.SendByte(view, ObjC.Sel("setWantsLayer:"), 1);
            ObjC.SendULong(view, ObjC.Sel("setAutoresizingMask:"), AutoresizingFor(flipped));
            ObjC.SendPtrLong(container, ObjC.Sel("addSubview:positioned:relativeTo:"), view, -1, IntPtr.Zero); // below all
            state.Views.Add(view);
        }

        IntPtr appearance = Appearance(dark);
        for (int i = 0; i < shapes.Count; i++)
        {
            var (rect, radius) = shapes[i];
            IntPtr view = state.Views[i];
            var frame = FrameFor(rect, height, flipped);
            ObjC.SendRect(view, ObjC.Sel("setFrame:"), new CGRect(frame));
            if (appearance != IntPtr.Zero) ObjC.SendPtr(view, ObjC.Sel("setAppearance:"), appearance);
            double r = Math.Max(0, radius.TopLeft - Inset);
            IntPtr layer = ObjC.Send(view, ObjC.Sel("layer"));
            if (layer != IntPtr.Zero)
            {
                ObjC.SendDouble(layer, ObjC.Sel("setCornerRadius:"), r);
                ObjC.SendByte(layer, ObjC.Sel("setMasksToBounds:"), 1);
            }
            // The mask is what the system reads for its own blur's shape; the layer's corners round what it draws.
            SetMask(view, frame.Size, r, window.RenderScaling);
        }
        state.Shapes = shapes;
        state.Dark = dark;
        state.Height = height;
        if (shapes.Count > 0) window.Classes.Remove("solidglass");
        Describe(window, state, container, flipped);
        return null;
    }

    /// <summary>Once per window and whenever the count changes: what the blur is sitting in, for the log.</summary>
    static void Describe(Window window, State state, IntPtr container, bool flipped)
    {
        if (state.Logged == state.Views.Count) return;
        state.Logged = state.Views.Count;
        if (!OperatingSystem.IsMacOS()) return;
        var names = new List<string>();
        IntPtr subviews = ObjC.Send(container, ObjC.Sel("subviews"));
        nuint count = subviews == IntPtr.Zero ? 0 : ObjC.SendCount(subviews, ObjC.Sel("count"));
        for (nuint i = 0; i < count; i++)
        {
            IntPtr child = ObjC.SendIndex(subviews, ObjC.Sel("objectAtIndex:"), i);
            string name = Marshal.PtrToStringUTF8(ObjC.object_getClassName(child)) ?? "?";
            bool hidden = ObjC.SendReturnsByte(child, ObjC.Sel("isHidden")) != 0;
            names.Add(state.Views.Contains(child) ? $"{name}(ours)" : hidden ? $"{name}(hidden)" : name);
        }
        string shapes = string.Join(" ", (state.Shapes ?? []).Select(s => $"{s.Rect.Width:0}x{s.Rect.Height:0}@{s.Rect.X:0},{s.Rect.Y:0}"));
        IntPtr nsWindow = ObjC.Send(container, ObjC.Sel("window"));
        bool opaque = nsWindow != IntPtr.Zero && ObjC.SendReturnsByte(nsWindow, ObjC.Sel("isOpaque")) != 0;
        bool shadow = nsWindow != IntPtr.Zero && ObjC.SendReturnsByte(nsWindow, ObjC.Sel("hasShadow")) != 0;
        Program.Log($"[glass] \"{window.Title}\" level={window.ActualTransparencyLevel} opaque={opaque} shadow={shadow} " +
            $"{(state.Dark ? "dark" : "light")} blur views: {state.Views.Count} " +
            $"[{shapes}] in {Marshal.PtrToStringUTF8(ObjC.object_getClassName(container))} (flipped={flipped}, " +
            $"{window.ClientSize.Width:0}x{window.ClientSize.Height:0}) subviews: {string.Join(", ", names)}");
    }

    static void RemoveAll(State state)
    {
        foreach (var view in state.Views) Remove(view);
        state.Views.Clear();
        state.Shapes = null;
    }

    static void Remove(IntPtr view)
    {
        if (!OperatingSystem.IsMacOS()) return;
        try
        {
            ObjC.Send(view, ObjC.Sel("removeFromSuperview"));
            ObjC.Send(view, ObjC.Sel("release"));
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
        {
        }
    }

    static IntPtr Appearance(bool dark)
    {
        if (!OperatingSystem.IsMacOS()) return IntPtr.Zero;
        IntPtr name = ObjC.NSString(dark ? "NSAppearanceNameDarkAqua" : "NSAppearanceNameAqua");
        IntPtr appearanceClass = ObjC.objc_getClass("NSAppearance");
        if (name == IntPtr.Zero || appearanceClass == IntPtr.Zero) return IntPtr.Zero;
        return ObjC.SendPtr(appearanceClass, ObjC.Sel("appearanceNamed:"), name);
    }

    /// <summary>A mask the blur view's own size (in points), opaque inside the rounded shape: an
    /// <c>NSVisualEffectView</c> shows its blur only where its mask is opaque.</summary>
    static void SetMask(IntPtr view, Size size, double radius, double scale)
    {
        if (!OperatingSystem.IsMacOS()) return;
        var pixels = new PixelSize(Math.Max(1, (int)Math.Ceiling(size.Width * scale)), Math.Max(1, (int)Math.Ceiling(size.Height * scale)));
        byte[] png = BuildMask([(new Rect(0, 0, size.Width * scale, size.Height * scale), new CornerRadius(radius * scale))], pixels);
        IntPtr image = MakeImage(png);
        if (image == IntPtr.Zero) return;
        try
        {
            ObjC.SendSize(image, ObjC.Sel("setSize:"), new CGSize(size.Width, size.Height));
            ObjC.SendPtr(view, ObjC.Sel("setMaskImage:"), image);
        }
        finally
        {
            ObjC.Send(image, ObjC.Sel("release"));
        }
    }

    static bool SameShapes(List<(Rect Rect, CornerRadius Radius)> a, List<(Rect Rect, CornerRadius Radius)> b)
    {
        if (a.Count != b.Count) return false;
        for (int i = 0; i < a.Count; i++)
        {
            if (!a[i].Rect.Equals(b[i].Rect)) return false;
            if (a[i].Radius != b[i].Radius) return false;
        }
        return true;
    }

    /// <summary>Every visible Glass in the window, in window coordinates: where the blur goes.</summary>
    static List<(Rect Rect, CornerRadius Radius)> CollectGlass(Window window)
    {
        var list = new List<(Rect, CornerRadius)>();
        foreach (var v in window.GetVisualDescendants())
        {
            if (v is not Glass glass || !glass.IsEffectivelyVisible) continue;
            if (glass.Bounds.Width <= 0 || glass.Bounds.Height <= 0) continue;
            if (glass.TranslatePoint(new Point(0, 0), window) is not { } topLeft) continue;
            list.Add((new Rect(topLeft, glass.Bounds.Size), glass.CornerRadius));
        }
        return list;
    }

    /// <summary>An alpha mask, opaque white over every shape and transparent elsewhere. Pure, so it's tested without
    /// a window: corners of a shape come out transparent, its centre opaque.</summary>
    public static byte[] BuildMask(IReadOnlyList<(Rect Rect, CornerRadius Radius)> shapes, PixelSize size)
    {
        using var bitmap = new SKBitmap(Math.Max(1, size.Width), Math.Max(1, size.Height), SKColorType.Rgba8888, SKAlphaType.Premul);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.Transparent);
            using var paint = new SKPaint { Color = SKColors.White, IsAntialias = true };
            foreach (var (rect, radius) in shapes)
            {
                using var rr = new SKRoundRect();
                rr.SetRectRadii(new SKRect((float)rect.X, (float)rect.Y, (float)rect.Right, (float)rect.Bottom),
                [
                    Corner(radius.TopLeft), Corner(radius.TopRight), Corner(radius.BottomRight), Corner(radius.BottomLeft),
                ]);
                canvas.DrawRoundRect(rr, paint);
            }
        }
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    static SKPoint Corner(double r) => new((float)r, (float)r);

    /// <summary>An <c>NSImage</c> made from PNG bytes, retained (the caller releases it once it's handed to
    /// <c>setMaskImage:</c>, which keeps its own reference).</summary>
    static unsafe IntPtr MakeImage(byte[] png)
    {
        if (!OperatingSystem.IsMacOS()) return IntPtr.Zero;
        fixed (byte* p = png)
        {
            IntPtr dataClass = ObjC.objc_getClass("NSData");
            IntPtr data = ObjC.SendPtrBytes(dataClass, ObjC.Sel("dataWithBytes:length:"), (IntPtr)p, (nuint)png.Length);
            if (data == IntPtr.Zero) return IntPtr.Zero;
            IntPtr imageClass = ObjC.objc_getClass("NSImage");
            IntPtr alloc = ObjC.Send(imageClass, ObjC.Sel("alloc"));
            if (alloc == IntPtr.Zero) return IntPtr.Zero;
            return ObjC.SendPtr(alloc, ObjC.Sel("initWithData:"), data);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    readonly struct CGRect(Rect r)
    {
        public readonly double X = r.X, Y = r.Y, Width = r.Width, Height = r.Height;
    }

    [StructLayout(LayoutKind.Sequential)]
    readonly struct CGSize(double width, double height)
    {
        public readonly double Width = width, Height = height;
    }

    [SupportedOSPlatform("macos")]
    static class ObjC
    {
        const string Lib = "/usr/lib/libobjc.A.dylib";

        [DllImport(Lib)]
        public static extern IntPtr objc_getClass(string name);

        [DllImport(Lib)]
        public static extern IntPtr object_getClassName(IntPtr obj);

        [DllImport(Lib)]
        static extern IntPtr sel_registerName(string name);

        public static IntPtr Sel(string name) => sel_registerName(name);

        public static bool IsKind(IntPtr obj, IntPtr cls) => SendPtrReturnsByte(obj, Sel("isKindOfClass:"), cls) != 0;

        /// <summary>An autoreleased NSString (the run loop's pool frees it).</summary>
        public static IntPtr NSString(string s)
        {
            IntPtr utf8 = Marshal.StringToCoTaskMemUTF8(s);
            try
            {
                return SendPtr(objc_getClass("NSString"), Sel("stringWithUTF8String:"), utf8);
            }
            finally
            {
                Marshal.FreeCoTaskMem(utf8);
            }
        }

        [DllImport(Lib, EntryPoint = "objc_msgSend")]
        public static extern IntPtr Send(IntPtr receiver, IntPtr selector);

        [DllImport(Lib, EntryPoint = "objc_msgSend")]
        public static extern IntPtr SendPtr(IntPtr receiver, IntPtr selector, IntPtr arg);

        [DllImport(Lib, EntryPoint = "objc_msgSend")]
        public static extern void SendPtrLong(IntPtr receiver, IntPtr selector, IntPtr arg, long n, IntPtr arg2);

        [DllImport(Lib, EntryPoint = "objc_msgSend")]
        public static extern IntPtr SendPtrBytes(IntPtr receiver, IntPtr selector, IntPtr bytes, nuint length);

        [DllImport(Lib, EntryPoint = "objc_msgSend")]
        public static extern IntPtr SendIndex(IntPtr receiver, IntPtr selector, nuint index);

        [DllImport(Lib, EntryPoint = "objc_msgSend")]
        public static extern nuint SendCount(IntPtr receiver, IntPtr selector);

        [DllImport(Lib, EntryPoint = "objc_msgSend")]
        public static extern byte SendReturnsByte(IntPtr receiver, IntPtr selector);

        [DllImport(Lib, EntryPoint = "objc_msgSend")]
        static extern byte SendPtrReturnsByte(IntPtr receiver, IntPtr selector, IntPtr arg);

        [DllImport(Lib, EntryPoint = "objc_msgSend")]
        public static extern void SendByte(IntPtr receiver, IntPtr selector, byte value);

        [DllImport(Lib, EntryPoint = "objc_msgSend")]
        public static extern void SendLong(IntPtr receiver, IntPtr selector, long value);

        [DllImport(Lib, EntryPoint = "objc_msgSend")]
        public static extern void SendULong(IntPtr receiver, IntPtr selector, ulong value);

        [DllImport(Lib, EntryPoint = "objc_msgSend")]
        public static extern void SendDouble(IntPtr receiver, IntPtr selector, double value);

        [DllImport(Lib, EntryPoint = "objc_msgSend")]
        public static extern void SendRect(IntPtr receiver, IntPtr selector, CGRect rect);

        [DllImport(Lib, EntryPoint = "objc_msgSend")]
        public static extern void SendSize(IntPtr receiver, IntPtr selector, CGSize size);
    }
}
