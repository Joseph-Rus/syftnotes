using StudyStash.Core;

namespace StudyStash.App.Platform;

/// <summary>A Mac copy opened from its DMG or Downloads offers, once, to move itself into Applications
/// (<see cref="MoveToApplications"/>). One plain sheet before any window; it is the system's own dialog, so it
/// needs no window of ours. Never during the self-test, a login start, or when STUDYSTASH_NO_MOVE is set.</summary>
static class MoveSheet
{
    /// <summary>True when the copy moved and is about to open from its new place: quit at once.</summary>
    public static bool Run(string home, bool background)
    {
        if (!OperatingSystem.IsMacOS() || background || SelfTest.Dir is not null) return false;
        if (Environment.GetEnvironmentVariable("STUDYSTASH_NO_MOVE") is { Length: > 0 }) return false;
        try
        {
            return MoveToApplications.Offer(AppContext.BaseDirectory, home, Py.UserHome(), Ask, Machine.Run,
                Updates.SpawnDetached, Environment.ProcessId, Program.Log);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Program.Log("[move] couldn't offer to move the app: " + e.Message);
            return false;
        }
    }

    internal const string Move = "Move to Applications";

    /// <summary>The sheet. Anything but the Move button (Not now, Esc) is "no".</summary>
    static bool Ask()
    {
        const string script =
            "display dialog \"Syft Notes works best from your Applications folder. It will move there, put the installer away, and open again.\" " +
            "with title \"Move Syft Notes to Applications?\" buttons {\"Not now\", \"" + Move + "\"} default button \"" + Move + "\" with icon note";
        var p = Machine.Run("/usr/bin/osascript", ["-e", script], TimeSpan.FromHours(1));
        return p is { ExitCode: 0 } && p.Stdout.Contains(Move, StringComparison.Ordinal);
    }
}
