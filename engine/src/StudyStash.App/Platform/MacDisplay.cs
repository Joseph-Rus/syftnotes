using System.Runtime.InteropServices;
using StudyStash.App.Services.Kit;

namespace StudyStash.App.Platform;

/// <summary>
/// A Mac started with its screen asleep (an update's relaunch overnight, the login item with the monitor off, a Mac
/// mini with nobody at it): Avalonia draws on a CoreVideo display link, made from the displays that are awake, and with
/// none it can't start and ends the app (Avalonia issue 18895, still open in 12.1). Before Avalonia starts, the app
/// waits until it can make that same display link, so it starts once the screen wakes instead of crashing.
/// </summary>
static class MacDisplay
{
    const string CoreVideo = "/System/Library/Frameworks/CoreVideo.framework/CoreVideo";

    [DllImport(CoreVideo)] static extern int CVDisplayLinkCreateWithActiveCGDisplays(out IntPtr link);
    [DllImport(CoreVideo)] static extern void CVDisplayLinkRelease(IntPtr link);

    /// <summary>What Avalonia's render timer does first (PlatformRenderTimer.mm): it fails (-6661) while no display is
    /// awake.</summary>
    static bool Awake()
    {
        if (CVDisplayLinkCreateWithActiveCGDisplays(out var link) != 0) return false;
        CVDisplayLinkRelease(link);
        return true;
    }

    /// <summary>Returns once a display is awake: at once on a Mac in use, and on other systems.</summary>
    public static void WaitUntilAwake(Action<string> log)
    {
        if (!OperatingSystem.IsMacOS() || Awake()) return;
        log("[app] the screen is asleep: Syft Notes starts when it wakes");
        Poll.UntilAsync(Awake, TimeSpan.FromSeconds(1), Timeout.InfiniteTimeSpan).GetAwaiter().GetResult();
        log("[app] the screen woke: starting");
    }
}
