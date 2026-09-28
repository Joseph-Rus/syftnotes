using StudyStash.Core;

namespace StudyStash.App.Services;

/// <summary>Opens Google Chrome, a URL inside it, and Chrome's own extensions page — the three things the Canvas
/// connect flow (design 07) asks of Chrome, each through one <see cref="Machine.Run"/> call, so a computer with no
/// Chrome installed reads back as one plain sentence instead of a swallowed exception. Tests hand in their own
/// <see cref="Runner"/> instead of ever starting a real browser.</summary>
public static class Chrome
{
    public const string NotInstalled = "Study Stash reads Canvas through Google Chrome. Install it, then try again.";

    /// <summary>Opens Chrome, or a URL inside it when one is given. Null on success; <see cref="NotInstalled"/> when
    /// nothing could be started. <paramref name="windowsChrome"/> finds chrome.exe on Windows (tests give their own).</summary>
    public static string? Open(string? url, Runner? run = null, Func<string?>? windowsChrome = null)
    {
        var runner = run ?? Machine.Run;
        ProcResult? started = null;
        if (OperatingSystem.IsMacOS())
            started = runner("open", url is null ? ["-a", "Google Chrome"] : ["-a", "Google Chrome", url], TimeSpan.FromSeconds(10));
        else if (OperatingSystem.IsWindows() && (windowsChrome ?? WindowsChrome)() is { } exe)
            // Through `start`, so this doesn't wait for (or end with) a Chrome it started; by its path, so a missing
            // Chrome is said here rather than in Windows' own "cannot find 'chrome'" box.
            started = runner("cmd", url is null ? ["/c", "start", "", exe] : ["/c", "start", "", exe, url], TimeSpan.FromSeconds(10));
        return started is { ExitCode: 0 } ? null : NotInstalled;
    }

    /// <summary>Windows: chrome.exe where Chrome's installer puts it (for everyone, or just this account), or null.</summary>
    public static string? WindowsChrome() =>
        new[] { Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86, Environment.SpecialFolder.LocalApplicationData }
            .Select(f => Environment.GetFolderPath(f))
            .Where(d => d.Length > 0)
            .Select(d => Path.Combine(d, "Google", "Chrome", "Application", "chrome.exe"))
            .FirstOrDefault(File.Exists);

    /// <summary>Opens Chrome's own <c>chrome://extensions</c> page, where "Load unpacked" picks up the folder
    /// <see cref="StudyStash.Core.Canvas.Extension"/> just wrote.</summary>
    public static string? OpenExtensions(Runner? run = null, Func<string?>? windowsChrome = null) => Open("chrome://extensions", run, windowsChrome);
}
