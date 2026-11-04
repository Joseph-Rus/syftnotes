using StudyStash.Core;
using Velopack;
using Velopack.Sources;

namespace StudyStash.App.Services;

/// <summary>
/// The download and install of an update, for a copy Velopack installed (docs/LIBRARIES.md, phase 5). Only that moves
/// here: when to look, waiting until nothing is recording, and what the student is told stay <see cref="AppUpdates"/>'
/// own. Velopack reads the release's own feed on GitHub for this computer's channel (win-x64, win-arm64, osx, linux-x64,
/// linux-arm64: whichever this copy was packed for), downloads the smallest way there (a delta from this version when the
/// release has one), checks it against the feed's checksum, and once this process has quit, swaps it in and opens the
/// app again. A copy Velopack didn't install (a build folder, the self-test, an Inno-era copy) never gets here.
/// </summary>
public static class VelopackUpdates
{
    /// <summary>Where the releases are (GitHub's own feed for each release's Velopack files).</summary>
    public const string Repo = "https://github.com/Joseph-Rus/syftnotes";

    /// <summary>The switch that points Velopack at another feed (test helpers only).</summary>
    public const string FeedVariable = "STUDYSTASH_VELOPACK_FEED";

    /// <summary>This copy's Velopack, or null when Velopack didn't install it; made once, whichever thread asks first
    /// (the start-up repair and the updater ask at the same time).</summary>
    static readonly Lazy<UpdateManager?> manager = new(Make, LazyThreadSafetyMode.ExecutionAndPublication);

    static UpdateManager? Manager => manager.Value;

    static UpdateManager? Make()
    {
        try
        {
            // STUDYSTASH_VELOPACK_FEED (test helpers only): a folder of releases served by the bridge test's runner.
            IUpdateSource source = TestSwitches.Get(FeedVariable) is { Length: > 0 } feed ? new SimpleWebSource(feed) : new GithubSource(Repo, null, false);
            var m = new UpdateManager(source);
            return m.IsInstalled ? m : null;
        }
        catch (Exception e) when (e is InvalidOperationException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            Program.Log("[update] Velopack couldn't read this copy: " + e.Message);
            return null;
        }
    }

    /// <summary>Velopack installed this copy, so it updates through Velopack.</summary>
    public static bool Installed => Manager is not null;

    /// <summary>Downloads the newest release for this computer and, if <paramref name="ready"/> still says so (a lecture
    /// may have started during the download), hands it to Velopack's updater, which applies it once this process quits
    /// and opens the app again with <paramref name="relaunchArgs"/> (asked at that moment). False leaves this version as it is.</summary>
    public static async Task<bool> ApplyAsync(Release release, Func<bool> ready, Func<Action> stopBackground, Func<IReadOnlyList<string>> relaunchArgs, Action<string> log)
    {
        if (Manager is not { } m) return false;
        UpdateInfo? update;
        try
        {
            update = await m.CheckForUpdatesAsync();
            if (update is null)
            {
                log($"{release.Tag} has no package for this computer yet; the next check tries again.");
                return false;
            }
            log($"Downloading {update.TargetFullRelease.Version} ({(update.DeltasToTarget.Length > 0 ? $"{update.DeltasToTarget.Length} small update(s)" : "the whole app")})...");
            await m.DownloadUpdatesAsync(update);
        }
        catch (Exception e) when (e is HttpRequestException or IOException or UnauthorizedAccessException or TaskCanceledException
                                     or InvalidOperationException or System.Text.Json.JsonException)
        {
            log($"Download failed: {e.Message}");
            return false;
        }
        if (!ready())
        {
            log(Updates.PutOff);
            return false;
        }
        // The background library runs this copy's program: it stops first, so nothing holds the files being replaced,
        // and the app starts it again from the new version when it opens.
        // stopBackground gives back what starts them again, if the install never gets going.
        var startBackground = stopBackground();
        try
        {
            m.WaitExitThenApplyUpdates(update.TargetFullRelease, silent: true, restart: true, [.. relaunchArgs()]);
        }
        catch (Exception e) when (e is IOException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            log($"Install failed: {e.Message}");
            startBackground();
            return false;
        }
        log($"Installing {update.TargetFullRelease.Version} once Syft Notes has quit.");
        return true;
    }
}
