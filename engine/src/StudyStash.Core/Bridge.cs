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
/// too) pointed at the new program, the app opened again; the new copy then deletes the old folder and its Apps-list
/// entry itself (the app's NewPlace), without the Inno uninstaller.</item>
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

    const string FuseWords = "The new Syft Notes (an AppImage) doesn't run on this computer yet: it needs FUSE. On Ubuntu and "
        + "Debian, install it with: sudo apt install libfuse2 (libfuse2t64 on Ubuntu 24.04). This copy stays until then.";

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
    /// carries the download for it, it isn't older than this copy, and a move to it hasn't failed here in the last
    /// <see cref="RetryAfter"/> (<see cref="Failed"/>): a move that keeps failing would otherwise download again and
    /// close the app every round.</summary>
    public static bool Offers(Release release, UpdateHost host, string? running = null, Architecture? arch = null, string? home = null) =>
        AssetFor(host, arch) is { } asset && release.Assets?.GetValueOrDefault(asset) is { Length: > 0 }
        && Updates.Compare(release.Version, Updates.ParseVersion(running ?? Engine.Version)) >= 0
        && !(home is not null && FailedLately(home, release.Tag, host.System));

    /// <summary>How long a release whose move failed here is left alone before it's tried again.</summary>
    public static readonly TimeSpan RetryAfter = TimeSpan.FromDays(7);

    /// <summary>Where a failed move is remembered: the release's tag (the move's script writes it too, windows/bridge.ps1).</summary>
    public static string FailedFile(string home) => Path.Combine(home, "bridge-failed.txt");

    /// <summary>Why a move failed, as <see cref="FailedFile"/> says after the tag: the AppImage needs FUSE, which this
    /// computer doesn't have yet.</summary>
    public const string NeedsFuse = "fuse";

    /// <summary>Remembers that the move to <paramref name="tag"/> failed here, and why (<see cref="NeedsFuse"/>, or
    /// nothing).</summary>
    public static void Failed(string home, string tag, string? why = null)
    {
        try
        {
            File.WriteAllText(FailedFile(home), why is null ? tag : $"{tag} {why}");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>Why the last move failed here (<see cref="NeedsFuse"/>, or "" for any other reason), or null when
    /// none has.</summary>
    public static string? FailedWhy(string home)
    {
        try
        {
            string file = FailedFile(home);
            if (!File.Exists(file)) return null;
            string[] words = File.ReadAllText(file).Trim().Split(' ', 2);
            return words.Length > 1 ? words[1] : "";
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    static bool FailedLately(string home, string tag, string system)
    {
        try
        {
            string file = FailedFile(home);
            if (!File.Exists(file)) return false;
            string[] words = File.ReadAllText(file).Trim().Split(' ', 2);
            if (words[0] != tag || DateTime.UtcNow - File.GetLastWriteTimeUtc(file) >= RetryAfter) return false;
            // Waiting for FUSE: tried again as soon as it's there, not a week later.
            return !(words.Length > 1 && words[1] == NeedsFuse && system == "Linux" && FuseReady());
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>Whether this Linux computer can run an AppImage: the FUSE device, and its mount helper.</summary>
    public static bool FuseReady() =>
        File.Exists("/dev/fuse") && (Machine.Which("fusermount") is not null || Machine.Which("fusermount3") is not null);

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
        var (ok, retry, why) = host.System == "Windows"
            ? await WindowsAsync(release, home, host, asset, ready, log, pid, relaunchArgs)
            : await LinuxAsync(release, host, asset, ready, log, pid, relaunchArgs, userHome ?? Py.UserHome(), menu ?? new LinuxMenu());
        // Put off for a lecture, or a download that didn't come: the next round tries again. Anything else is
        // remembered, so the move isn't tried again for a week.
        if (!ok && !retry) Failed(home, release.Tag, why);
        return ok;
    }

    static async Task<(bool Ok, bool Retry, string? Why)> WindowsAsync(Release release, string home, UpdateHost host, string asset, Func<bool> ready,
        Action<string> log, int pid, IReadOnlyList<string> relaunchArgs)
    {
        // The Inno install's entry and uninstaller: without them there's nothing to move over from.
        if (!InnoInstalled())
        {
            log("This copy's Setup entry is missing, so it can't move to the new installer here.");
            return (false, false, null);
        }
        string dir = Path.Combine(host.TempDir, "Syft Notes move");
        if (await FetchAsync(release, host, asset, Path.Combine(dir, asset), log) is not { } setup) return (false, true, null);
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
            return (false, false, null);
        }
        // The last moment to put it off: once the script starts, the old copy is replaced.
        if (!ready())
        {
            log(Updates.PutOff);
            return (false, true, null);
        }
        string logPath = Path.Combine(home, "logs", "update.log");
        try
        {
            // The script stops what still runs from the old folder (the background library) once the app has quit.
            host.SpawnDetached(["powershell.exe", "-NoProfile", "-ExecutionPolicy", "Bypass", "-WindowStyle", "Hidden", "-File", script,
                "-Setup", setup, "-WaitPid", pid.ToString(CultureInfo.InvariantCulture), "-Log", logPath,
                "-OldExe", Path.Combine(host.AppDir, "StudyStash.exe"), "-FailedFile", FailedFile(home), "-Tag", release.Tag,
                "-OpenArgs", string.Join(' ', relaunchArgs.Select(a => a.Contains(' ') ? $"\"{a}\"" : a))]);
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or IOException or InvalidOperationException)
        {
            log($"Couldn't start the move: {e.Message}");
            return (false, false, null);
        }
        log($"Moving to {release.Tag} once Syft Notes has quit (log: {logPath}).");
        return (true, false, null);
    }

    /// <summary>Inno Setup's uninstall entry for Syft Notes on this account (its AppId).</summary>
    static bool InnoInstalled()
    {
        if (!OperatingSystem.IsWindows()) return true;
        using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
            @"Software\Microsoft\Windows\CurrentVersion\Uninstall\{6F2C9A51-3B7E-4D28-9C41-8E5A0B7D2F63}_is1");
        return key?.GetValue("UninstallString") is string u && key.GetValue("InstallLocation") is string { Length: > 0 }
            && File.Exists(u.Trim('"').Split("\" ")[0].Trim('"'));
    }

    static async Task<(bool Ok, bool Retry, string? Why)> LinuxAsync(Release release, UpdateHost host, string asset, Func<bool> ready, Action<string> log,
        int pid, IReadOnlyList<string> relaunchArgs, string userHome, LinuxMenu menu)
    {
        string place = AppImagePlace(userHome);
        string fresh = place + ".new";
        // An AppImage runs through FUSE, which some systems don't have: nothing is downloaded until it's there, and
        // this copy stays and says what to install (the app tells the student, NoticeWords.MoveNeedsFuse).
        if (!FuseReady())
        {
            log(FuseWords);
            return (false, false, NeedsFuse);
        }
        if (await FetchAsync(release, host, asset, fresh, log) is null) return (false, true, null);
        try
        {
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(fresh, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
                | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            TryDelete(fresh);
            log($"Couldn't get the move ready: {e.Message}");
            return (false, false, null);
        }
        // Tried once before anything changes: one that doesn't run here leaves this copy as it is.
        if (host.VersionOf(fresh) is not { } v || Updates.Compare(Updates.ParseVersion(v), release.Version) != 0)
        {
            TryDelete(fresh);
            log(FuseWords);
            return (false, false, NeedsFuse);
        }
        if (!ready())
        {
            TryDelete(fresh);
            log(Updates.PutOff);
            return (false, true, null);
        }
        try
        {
            File.Move(fresh, place, overwrite: true);
            menu.Write(place, Path.Combine(host.AppDir, "syft-notes.png"), log);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            TryDelete(fresh);
            log($"Install failed: {e.Message}");
            return (false, false, null);
        }
        // The background library runs the old program: it stops now, and the AppImage registers it again on its first
        // start (NewPlace). The old folder goes once this process has quit (this process still loads code from it):
        // the waiter below deletes it, then opens the AppImage in its place (tail --pid follows this process to its end).
        Updates.StopBackground(Updates.LinuxProgram(host.AppDir), host);
        log($"Installed {release.Tag} as {place}.");
        const string script = "exe=\"$1\"; old=\"$2\"; shift 2; tail --pid=\"$0\" -f /dev/null; rm -rf -- \"$old\"; exec \"$exe\" \"$@\"";
        host.SpawnDetached(["/bin/sh", "-c", script, pid.ToString(CultureInfo.InvariantCulture), place, host.AppDir, .. relaunchArgs]);
        return (true, false, null);
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
