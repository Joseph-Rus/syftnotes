using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;

namespace StudyStash.App.Platform;

// SCRATCH PROBE — not to be committed.
public static class ChromeProbe
{
    [StructLayout(LayoutKind.Sequential)] struct CGPoint { public double X, Y; }
    const string Lib = "/usr/lib/libobjc.A.dylib";
    [DllImport(Lib)] static extern IntPtr objc_getClass(string n);
    [DllImport(Lib)] static extern IntPtr sel_registerName(string n);
    [DllImport(Lib, EntryPoint = "objc_msgSend")] static extern IntPtr Send(IntPtr r, IntPtr s);
    [DllImport(Lib, EntryPoint = "objc_msgSend")] static extern long SendL(IntPtr r, IntPtr s);
    [DllImport(Lib, EntryPoint = "objc_msgSend")] static extern byte SendB(IntPtr r, IntPtr s);
    [DllImport(Lib, EntryPoint = "objc_msgSend")] static extern void SendP(IntPtr r, IntPtr s, IntPtr a);
    [DllImport(Lib, EntryPoint = "objc_msgSend")]
    static extern IntPtr MakeEvent(IntPtr cls, IntPtr sel, ulong type, CGPoint loc, ulong flags, double time, long wnum, IntPtr ctx, long enumb, long clicks, float pressure);

    static bool ran;

    public static void Run(Window w)
    {
        if (Environment.GetEnvironmentVariable("STUDYSTASH_CHROME_PROBE") != "1" || ran) return;
        ran = true;
        Program.Log("[probe] armed");
        DispatcherTimer.RunOnce(() => { try { Step(w); } catch (Exception e) { Program.Log("[probe] failed " + e); } }, TimeSpan.FromSeconds(1.5));
    }

    static object Root(Window w)
    {
        const System.Reflection.BindingFlags F = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public;
        for (var t = w.GetType(); t != null; t = t.BaseType)
        {
            foreach (var f in t.GetFields(F)) if (f.GetValue(w) is IInputRoot r) { Program.Log("[probe] root via field " + t.Name + "." + f.Name + " " + r.GetType()); return r; }
            foreach (var pr in t.GetProperties(F)) { if (pr.GetIndexParameters().Length > 0) continue; object? v = null; try { v = pr.GetValue(w); } catch { } if (v is IInputRoot r) { Program.Log("[probe] root via prop " + pr.Name + " " + r.GetType()); return r; } }
        }
        throw new InvalidOperationException("no input root");
    }

    static void Step(Window w)
    {
        var hit = typeof(IInputRoot).GetMethod("HitTestChromeElement", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public)!;
        double width = w.Bounds.Width, height = w.Bounds.Height;
        foreach (var (name, p) in new[] { ("header empty", new Point(width / 2, 26)), ("search button", new Point(width - 30, 26)), ("sidebar", new Point(100, 200)), ("lights", new Point(30, 26)) })
            Program.Log($"[probe] hit {name} {p}: {hit.Invoke(Root(w), [p])?.ToString() ?? "null"}");
        IntPtr ns = w.TryGetPlatformHandle()!.Handle;
        long wnum = SendL(ns, sel_registerName("windowNumber"));
        void Click(double x, double yFromTop, long clicks)
        {
            var loc = new CGPoint { X = x, Y = w.Bounds.Height - yFromTop };
            for (long c = 1; c <= clicks; c++)
            {
                foreach (ulong t in new ulong[] { 1, 2 })
                {
                    IntPtr e = MakeEvent(objc_getClass("NSEvent"), sel_registerName("mouseEventWithType:location:modifierFlags:timestamp:windowNumber:context:eventNumber:clickCount:pressure:"),
                        t, loc, 0, 0, wnum, IntPtr.Zero, 0, c, t == 1 ? 1f : 0f);
                    SendP(ns, sel_registerName("sendEvent:"), e);
                }
            }
        }
        Program.Log($"[probe] zoomed before: {SendB(ns, sel_registerName("isZoomed"))} state {w.WindowState} pos {w.Position}");
        Click(width - 30, 26, 1);
        DispatcherTimer.RunOnce(() =>
        {
            var wins = (Avalonia.Application.Current!.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)!.Windows;
            Program.Log("[probe] after search click, visible windows: " + string.Join(", ", wins.Where(x => x.IsVisible).Select(x => x.Title)));
            Program.Log("[probe] now double-click empty header");
            Click(width / 2, 26, 2);
            DispatcherTimer.RunOnce(() =>
            {
                Program.Log($"[probe] zoomed after dclick: {SendB(ns, sel_registerName("isZoomed"))} state {w.WindowState} size {w.Bounds.Size}");
                Click(width / 2, 26, 2);
                DispatcherTimer.RunOnce(() =>
                {
                    Program.Log($"[probe] zoomed after 2nd dclick: {SendB(ns, sel_registerName("isZoomed"))} size {w.Bounds.Size}");
                    Shell.ShowSettings();
                    DispatcherTimer.RunOnce(() =>
                    {
                        var wins = (Avalonia.Application.Current!.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)!.Windows;
                        foreach (var o in wins.Where(x => x.IsVisible && x != w))
                            Program.Log($"[probe] {o.Title} {o.Bounds.Size}: header centre {hit.Invoke(Root(o), [new Point(o.Bounds.Width / 2, 26)])}, below header {hit.Invoke(Root(o), [new Point(o.Bounds.Width / 2, 70)]) ?? "null"}");
                        Shell.ShowSetup();
                        DispatcherTimer.RunOnce(() =>
                        {
                            foreach (var o in wins.Where(x => x.IsVisible && x.Title!.StartsWith("Set up")))
                                Program.Log($"[probe] {o.Title} {o.Bounds.Size}: header centre {hit.Invoke(Root(o), [new Point(o.Bounds.Width / 2, 26)])}, below header {hit.Invoke(Root(o), [new Point(o.Bounds.Width / 2, 70)]) ?? "null"}");
                        }, TimeSpan.FromSeconds(1.5));
                    }, TimeSpan.FromSeconds(1.5));
                }, TimeSpan.FromSeconds(1));
            }, TimeSpan.FromSeconds(1));
        }, TimeSpan.FromSeconds(1));
    }
}
