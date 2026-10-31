using System.Diagnostics;
using System.Runtime.InteropServices;
using StudyStash.Core;

namespace StudyStash.App.Services;

/// <summary>
/// The first start after the app took the name Syft Notes (and its new icon), on a computer that had it as Study Stash
/// or Egghead Notes: the system keeps the name and the icon it saw first in caches of its own, so this nudges them
/// to look again, for this app alone. The update itself was installed by the older app's updater (which knows
/// nothing of this), so it can only happen here, on the new app's first start. Done once (<see cref="AppSettings.Brand"/>).
/// </summary>
public static class BrandRefresh
{
    /// <summary>What <see cref="AppSettings.Brand"/> holds once it's been done.</summary>
    public const string Current = "syft-notes";

    const string LsRegister = "/System/Library/Frameworks/CoreServices.framework/Frameworks/LaunchServices.framework/Support/lsregister";

    /// <summary>The Mac commands, each naming only this bundle: Launch Services registers it again (-f) so Finder,
    /// the Dock, Launchpad and the notification settings read its name and icon afresh, and Spotlight reads it again.
    /// Nothing that restarts the Dock or Finder, nothing that touches another app.</summary>
    internal static IReadOnlyList<string[]> MacCommands(string bundle) =>
        [[LsRegister, "-f", bundle], ["/usr/bin/mdimport", bundle]];

    /// <summary>True when something was done (so the caller remembers it); false where there's nothing to refresh
    /// (a build run from its folder, a second profile): that run doesn't remember it, an installed one still will.</summary>
    public static bool Run(Action<string>? log = null, string? baseDir = null)
    {
        try
        {
            if (OperatingSystem.IsMacOS())
            {
                if (Apps.MacBundleOf(baseDir) is not { } bundle) return false;
                // The bundle's own modified time moves, which is what makes the system look at it again.
                Directory.SetLastWriteTimeUtc(bundle, DateTime.UtcNow);
                foreach (var cmd in MacCommands(bundle))
                    Quiet(cmd);
                log?.Invoke("[brand] asked macOS to read this app's name and icon again");
                return true;
            }
            if (OperatingSystem.IsWindows())
            {
                if (Apps.WindowsInstallOf(baseDir) is null) return false;
                // Explorer, the Start menu and the taskbar drop the icons they cached (what Setup.exe asks too).
                SHChangeNotify(0x08000000 /* SHCNE_ASSOCCHANGED */, 0 /* SHCNF_IDLIST */, IntPtr.Zero, IntPtr.Zero);
                log?.Invoke("[brand] asked Windows to read this app's name and icon again");
                return true;
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            log?.Invoke("[brand] couldn't ask the system to read the name again: " + e.Message);
        }
        return false;
    }

    static void Quiet(string[] cmd)
    {
        if (!File.Exists(cmd[0])) return;
        var info = new ProcessStartInfo(cmd[0]) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        foreach (string a in cmd.Skip(1)) info.ArgumentList.Add(a);
        using var p = Process.Start(info);
        if (p is null) return;
        _ = p.StandardOutput.ReadToEndAsync();
        _ = p.StandardError.ReadToEndAsync();
        if (!p.WaitForExit(20_000)) p.Kill();
    }

    [DllImport("shell32.dll")]
    static extern void SHChangeNotify(int eventId, uint flags, IntPtr item1, IntPtr item2);
}
