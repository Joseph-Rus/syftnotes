using StudyStash.Core;
using Velopack;

namespace StudyStash.App;

/// <summary>
/// The Velopack trial (docs/LIBRARIES.md, phase 5, step 1), run only by .github/workflows/velopack-trial.yml. Compiled
/// only with the test helpers (StudyStash.App.csproj), so a student's copy never has it, nor the Velopack package.
/// <list type="bullet">
/// <item>Velopack's start-up hooks first, as its installer and updater expect: <c>--veloapp-install</c> and the like
/// are answered here and the process ends. In a copy Velopack didn't install it does nothing.</item>
/// <item><c>velopack-trial-update FEED REPORT</c>: checks the feed (a folder of releases), downloads the update and
/// has Velopack's updater apply it once this process has quit, then start the new version with
/// <c>velopack-trial-report REPORT</c>.</item>
/// <item><c>velopack-trial-report REPORT</c>: adds this copy's version and path to REPORT, and quits.</item>
/// </list>
/// AppUpdates (waiting until the app is idle and not recording) is not involved: the trial only proves the install,
/// update and uninstall themselves.
/// </summary>
static class VelopackTrial
{
    /// <summary>An exit code when the arguments were one of the trial's commands or a Velopack hook; null otherwise.</summary>
    public static int? Run(string[] args)
    {
        // Not applied at start-up: Velopack would otherwise apply a downloaded update whenever the program starts, even
        // as `mcp` for an AI app or as the background library, and start it again in its place (run 1 of the trial saw
        // `version` do that). Only the trial's own command applies one, as AppUpdates would.
        VelopackApp.Build().SetAutoApplyOnStartup(false).Run();
        if (args.Length == 3 && args[0] == "velopack-trial-update") return Update(args[1], args[2]);
        if (args.Length == 2 && args[0] == "velopack-trial-report") return Report(args[1], "relaunched");
        return null;
    }

    static void Write(string report, string line) =>
        File.AppendAllText(report, $"{DateTime.UtcNow:O} {line}{Environment.NewLine}");

    static int Report(string report, string what)
    {
        Write(report, $"{what} version={Engine.Version} path={Environment.ProcessPath}"
            + (Environment.GetEnvironmentVariable("APPIMAGE") is { Length: > 0 } image ? $" appimage={image}" : ""));
        return 0;
    }

    static int Update(string feed, string report)
    {
        Report(report, "started");
        var manager = new UpdateManager(feed);
        if (!manager.IsInstalled)
        {
            Write(report, "failed: this copy wasn't installed by Velopack");
            return 3;
        }
        Write(report, $"installed version={manager.CurrentVersion} channel={Velopack.Locators.VelopackLocator.Current.Channel} root={Velopack.Locators.VelopackLocator.Current.RootAppDir}");
        try
        {
            var update = manager.CheckForUpdatesAsync().GetAwaiter().GetResult();
            if (update is null)
            {
                Write(report, "failed: the feed has nothing newer");
                return 4;
            }
            Write(report, $"found version={update.TargetFullRelease.Version} full={update.TargetFullRelease.Size} "
                + $"deltas={update.DeltasToTarget.Length} delta_bytes={update.DeltasToTarget.Sum(d => d.Size)}");
            manager.DownloadUpdatesAsync(update).GetAwaiter().GetResult();
            Write(report, "downloaded");
            // Silent: no progress window or questions. Velopack's updater waits for this process to end, applies the
            // update and starts the new version with the arguments that add its line to the report.
            manager.WaitExitThenApplyUpdates(update.TargetFullRelease, silent: true, restart: true,
                restartArgs: ["velopack-trial-report", report]);
            Write(report, "handed to the updater");
            return 0;
        }
        catch (Exception e)
        {
            // Any failure is the trial's finding: written down whole for the workflow to show, and the command fails.
            Write(report, $"failed: {e}");
            return 5;
        }
    }
}
