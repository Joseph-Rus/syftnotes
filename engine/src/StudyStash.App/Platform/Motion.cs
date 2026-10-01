using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;

namespace StudyStash.App.Platform;

/// <summary>
/// Whether things on screen should move: not when the student asked their computer to reduce motion (a Mac's
/// Accessibility → Display → Reduce motion; Windows' "Animation effects" off), nor in the pictures the tests take
/// (STUDYSTASH_STILL=1). Asked of the system at most every few seconds, so a change in Settings is picked up soon.
/// </summary>
public static class Motion
{
    static bool? reduced;
    static DateTime askedAt;

    /// <summary>For tests: still (true) or moving (false) whatever the system says; null asks the system.</summary>
    internal static bool? Override { get; set; }

    public static bool Reduced
    {
        get
        {
            if (Override is { } forced) return forced;
            if (Environment.GetEnvironmentVariable("STUDYSTASH_STILL") == "1") return true;
            if (reduced is { } known && DateTime.UtcNow - askedAt < TimeSpan.FromSeconds(5)) return known;
            askedAt = DateTime.UtcNow;
            return (reduced = Ask()).Value;
        }
    }

    static bool Ask()
    {
        try
        {
            if (OperatingSystem.IsMacOS())
            {
                IntPtr workspace = Send(objc_getClass("NSWorkspace"), sel_registerName("sharedWorkspace"));
                return workspace != IntPtr.Zero && SendByte(workspace, sel_registerName("accessibilityDisplayShouldReduceMotion")) != 0;
            }
            if (OperatingSystem.IsWindows())
            {
                // SPI_GETCLIENTAREAANIMATION: Windows' "Animation effects" (Settings → Accessibility → Visual effects).
                bool animate = true;
                return SystemParametersInfo(0x1042, 0, ref animate, 0) && !animate;
            }
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
        {
        }
        return false;
    }

    /// <summary>
    /// Runs <paramref name="step"/> from 0 to 1 over <paramref name="duration"/>, eased out (quick at first, settling
    /// gently), a step each frame <paramref name="owner"/>'s window draws, then <paramref name="done"/>. Reduced motion,
    /// or nothing on screen to draw it: straight to 1. Dispose the result to stop it partway.
    /// </summary>
    public static IDisposable Animate(Visual owner, TimeSpan duration, Action<double> step, Action? done = null)
    {
        var run = new Run(owner, duration, step, done);
        run.Start();
        return run;
    }

    sealed class Run(Visual owner, TimeSpan duration, Action<double> step, Action? done) : IDisposable
    {
        bool stopped;
        TimeSpan? first;

        public void Start()
        {
            if (Reduced || duration <= TimeSpan.Zero || TopLevel.GetTopLevel(owner) is null)
            {
                step(1);
                done?.Invoke();
                return;
            }
            step(0);
            Next();
        }

        void Next()
        {
            if (TopLevel.GetTopLevel(owner) is { } top) top.RequestAnimationFrame(Frame);
            else Finish();
        }

        void Frame(TimeSpan now)
        {
            if (stopped) return;
            first ??= now;
            double t = Math.Clamp((now - first.Value).TotalMilliseconds / duration.TotalMilliseconds, 0, 1);
            // The first frame starts the clock; one that never moves on (no time between frames) still ends.
            step(1 - Math.Pow(1 - t, 3));
            if (t < 1) Next();
            else Finish();
        }

        void Finish()
        {
            if (stopped) return;
            stopped = true;
            step(1);
            done?.Invoke();
        }

        public void Dispose() => stopped = true;
    }

    const string Lib = "/usr/lib/libobjc.A.dylib";

    [DllImport(Lib)] static extern IntPtr objc_getClass(string name);
    [DllImport(Lib)] static extern IntPtr sel_registerName(string name);
    [DllImport(Lib, EntryPoint = "objc_msgSend")] static extern IntPtr Send(IntPtr receiver, IntPtr selector);
    [DllImport(Lib, EntryPoint = "objc_msgSend")] static extern byte SendByte(IntPtr receiver, IntPtr selector);

    [DllImport("user32.dll", SetLastError = true)]
    static extern bool SystemParametersInfo(uint action, uint param, ref bool value, uint winIni);
}
