using System.Globalization;
using System.Runtime.InteropServices;

namespace StudyStash.Core;

/// <summary>
/// The bridge release (docs/LIBRARIES.md, phase 5, step 2): a copy installed the old way moves over to Velopack's
/// install, so every later update is Velopack's. It's an update like any other to AppUpdates (it waits until nothing is
/// recording, and auto_update decides whether it goes by itself), only it happens even when the release is this copy's
/// own version, and what does it opens the app again itself.
/// <list type="bullet">
/// <item>Windows (an Inno Setup install): Velopack's Setup.exe for this processor is downloaded and checked, then
/// windows/bridge.ps1 takes over once the app has quit: Setup.exe --silent, the Run entries and shortcuts (taskbar pins
/// too) pointed at the new program, the Inno uninstaller, the Start-menu entry put back, the app opened again.</item>
/// <item>Linux (the tarball's folder): the AppImage is downloaded and checked, put in ~/Applications and tried once
/// (it needs FUSE, which some systems don't have); if it runs, it goes in the applications menu and on the PATH in the
/// old copy's place, the old folder goes, and the AppImage opens.</item>
/// <item>A Mac needs none of this: the DMG an older copy's updater swaps in is already Velopack's bundle.</item>
/// </list>
/// On the new copy's first start, what still names the old program (AI apps' connections, start at login) is pointed at
/// the new one (Moves.cs, the app's NewPlace). Settings, lectures and Documents are never touched.
/// </summary>
public static class Bridge
{
    /// <summary>Velopack's Setup.exe for a Windows processor, as vpk names it for that channel.</summary>
    public static string? WindowsSetup(Architecture arch) => arch switch
    {
        Architecture.X64 => "SyftNotes-win-x64-Setup.exe",
        Architecture.Arm64 => "SyftNotes-win-arm64-Setup.exe",
        _ => null,
    };

    /// <summary>The AppImage for a Linux processor, as vpk names it for that channel.</summary>
    public static string? LinuxAppImage(Architecture arch) => arch switch
    {
        Architecture.X64 => "SyftNotes-linux-x64.AppImage",
        Architecture.Arm64 => "SyftNotes-linux-arm64.AppImage",
        _ => null,
    };

    /// <summary>Where the AppImage goes: ~/Applications, where AppImages are kept.</summary>
    public static string AppImagePlace(string userHome) => Path.Combine(userHome, "Applications", "SyftNotes.AppImage");

    /// <summary>The download that moves this copy over, or null: not a copy installed the old way, or no such
    /// download for this computer.</summary>
    public static string? AssetFor(UpdateHost host, Architecture? arch = null)
    {
        if (host.AppDir.Length == 0) return null;
        var a = arch ?? RuntimeInformation.OSArchitecture;
        return host.System switch
        {
            "Windows" => WindowsSetup(a),
            "Linux" => LinuxAppImage(a),
            _ => null,
        };
    }

    /// <summary>Whether <paramref name="release"/> moves this copy over: it's installed the old way, the release
    /// carries the download for it, and it isn't older than this copy.</summary>
    public static bool Offers(Release release, UpdateHost host, string? running = null, Architecture? arch = null) =>
        AssetFor(host, arch) is { } asset && release.Assets?.GetValueOrDefault(asset) is { Length: > 0 }
        && Updates.Compare(release.Version, Updates.ParseVersion(running ?? Engine.Version)) >= 0;

    /// <summary>Downloads the move's file and checks it against the release's SHA256SUMS.txt; null (and said) when
    /// either fails.</summary>
    static async Task<string?> FetchAsync(Release release, UpdateHost host, string asset, string dest, Action<string> log)
    {
        string url = release.Assets?.GetValueOrDefault(asset) ?? "";
        IReadOnlyDictionary<string, string>? sums = null;
        if (release.Assets?.GetValueOrDefault(Updates.ChecksumsAsset) is { Length: > 0 } sumsUrl)
        {
            try
            {
                using var r = await (host.Http ?? Updates.SharedHttp).GetAsync(sumsUrl);
                r.EnsureSuccessStatusCode();
                sums = Checksums.Parse(await r.Content.ReadAsStringAsync());
            }
            catch (Exception e) when (e is IOException or HttpRequestException or TaskCanceledException)
            {
                log($"The release's checksums didn't download: {e.Message}");
                return null;
            }
        }
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            await Ready.DownloadAsync(url, dest, http: host.Http);
        }
        catch (Exception e) when (e is IOException or HttpRequestException or UnauthorizedAccessException or TaskCanceledException)
        {
            log($"Download failed: {e.Message}");
            return null;
        }
        if (!Updates.ChecksumMatches(dest, asset, sums))
        {
            TryDelete(dest);
            log("The download didn't match its checksum, so this version stays.");
            return null;
        }
        return dest;
    }

    /// <summary>Moves this copy over to Velopack's install of <paramref name="release"/>. True once it's under way:
    /// the caller quits, and what does the move opens the app again. False leaves this copy as it is.</summary>
    /// <paramref name="pid"/> is the app's own process, which the move waits for; <paramref name="relaunchArgs"/> what
    /// the new copy opens with.
    public static async Task<bool> ApplyAsync(Release release, string home, UpdateHost host, Func<bool> ready, Action<string> log,
        int pid, IReadOnlyList<string> relaunchArgs, string? userHome = null, LinuxMenu? menu = null)
    {
        if (AssetFor(host) is not { } asset) return false;
        log($"Moving to {release.Tag}, installed the new way...");
        return host.System == "Windows"
            ? await WindowsAsync(release, home, host, asset, ready, log, pid)
            : await LinuxAsync(release, host, asset, ready, log, pid, relaunchArgs, userHome ?? Py.UserHome(), menu ?? new LinuxMenu());
    }

    static async Task<bool> WindowsAsync(Release release, string home, UpdateHost host, string asset, Func<bool> ready, Action<string> log, int pid)
    {
        string dir = Path.Combine(host.TempDir, "Syft Notes move");
        if (await FetchAsync(release, host, asset, Path.Combine(dir, asset), log) is not { } setup) return false;
        string script = Path.Combine(dir, "bridge.ps1");
        try
        {
            using var from = typeof(Bridge).Assembly.GetManifestResourceStream("bridge.ps1")
                ?? throw new IOException("the bridge script isn't in this copy");
            using var to = File.Create(script);
            from.CopyTo(to);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            log($"Couldn't get the move ready: {e.Message}");
            return false;
        }
        // The last moment to put it off: the script closes nothing itself, but once it starts the old copy is uninstalled.
        if (!ready())
        {
            log(Updates.PutOff);
            return false;
        }
        Updates.StopBackground(Path.Combine(host.AppDir, "StudyStash.exe"), host);
        string logPath = Path.Combine(home, "logs", "update.log");
        host.SpawnDetached(["powershell.exe", "-NoProfile", "-ExecutionPolicy", "Bypass", "-WindowStyle", "Hidden", "-File", script,
            "-Setup", setup, "-WaitPid", pid.ToString(CultureInfo.InvariantCulture), "-Log", logPath]);
        log($"Moving to {release.Tag} once Syft Notes has quit (log: {logPath}).");
        return true;
    }

    static async Task<bool> LinuxAsync(Release release, UpdateHost host, string asset, Func<bool> ready, Action<string> log,
        int pid, IReadOnlyList<string> relaunchArgs, string userHome, LinuxMenu menu)
    {
        string place = AppImagePlace(userHome);
        string fresh = place + ".new";
        if (await FetchAsync(release, host, asset, fresh, log) is null) return false;
        try
        {
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(fresh, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
                | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            TryDelete(fresh);
            log($"Couldn't get the move ready: {e.Message}");
            return false;
        }
        // An AppImage runs through FUSE, which some systems don't have: then this copy stays, and says what to install.
        if (host.VersionOf(fresh) is not { } v || Updates.Compare(Updates.ParseVersion(v), release.Version) != 0)
        {
            TryDelete(fresh);
            log("The new Syft Notes (an AppImage) doesn't run on this computer yet: it needs FUSE. On Ubuntu and Debian, "
                + "install it with: sudo apt install libfuse2 (libfuse2t64 on Ubuntu 24.04). This copy stays until then.");
            return false;
        }
        if (!ready())
        {
            TryDelete(fresh);
            log(Updates.PutOff);
            return false;
        }
        string oldProgram = Updates.LinuxProgram(host.AppDir);
        Updates.StopBackground(oldProgram, host);
        try
        {
            File.Move(fresh, place, overwrite: true);
            menu.Write(place, Path.Combine(host.AppDir, "syft-notes.png"), log);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            TryDelete(fresh);
            log($"Install failed: {e.Message}");
            return false;
        }
        // The old folder goes (this process keeps running from what it already loaded until it quits).
        Apps.TryDeleteFolder(host.AppDir);
        log($"Installed {release.Tag} as {place}.");
        // Waits for this process to be gone (tail --pid follows it to its end), then opens the AppImage in its place.
        const string script = "exe=\"$1\"; shift; tail --pid=\"$0\" -f /dev/null; exec \"$exe\" \"$@\"";
        host.SpawnDetached(["/bin/sh", "-c", script, pid.ToString(CultureInfo.InvariantCulture), place, .. relaunchArgs]);
        return true;
    }

    static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }
}
