using StudyStash.App.Platform;
using StudyStash.Core;

namespace StudyStash.App.Services;

/// <summary>
/// Every start of an installed copy (docs/LIBRARIES.md, phase 5): what other programs keep of the app's path is put
/// right where it still names the program as it was before it moved (Moves.cs: Inno Setup's folder, "Study
/// Stash.app", the tarball's folder, or one that's gone). The AI apps' connections (Claude, ChatGPT/Codex, Gemini and
/// the rest), start at login and the background library are written again for <see cref="Desktop.Program"/>, and
/// Windows' notification name and link are written on their first use. A Mac Dock icon pinned to the old bundle is
/// pointed at the new one, once. An AppImage puts itself in the applications menu. Nothing changes for a build folder,
/// the self-test, or a connection to another copy someone keeps on purpose. Settings, lectures and Documents are never
/// touched.
/// </summary>
public static class NewPlace
{
    /// <summary>What was put right, for the log; empty when nothing needed it.</summary>
    public static IReadOnlyList<string> Run(ClaudeSetup aiApps, string home, string program, Func<string?> loginProgram, Action relogin,
        BackgroundLibrary? background = null)
    {
        var done = new List<string>();
        // The background library (the phone answered with the app closed), registered from the old program: registered
        // again from this one, which starts it.
        if (background?.Registered(home) is [var was, ..] && Moves.OldProgram(was, program))
        {
            background.Register(home, program);
            done.Add("the background library now runs " + program);
        }
        foreach (var app in aiApps.States())
        {
            if (!app.Added || !app.OtherCopy || !Moves.OldProgram(aiApps.CommandOf(app.Id), program)) continue;
            var change = aiApps.Connect(app.Id);
            done.Add(change.Ok ? $"{app.Name} now starts {program}" : $"{app.Name}: {change.Say}");
        }
        if (Moves.OldProgram(loginProgram(), program))
        {
            relogin();
            done.Add("start at login now starts " + program);
        }
        return done;
    }

    /// <summary>The real thing, once the app has started: only for a copy that's installed (Velopack's, or a Mac
    /// bundle), never while the self-test or a test runs.</summary>
    public static void RunHere(string home)
    {
        if (Desktop.SystemChangesOff || SelfTest.Dir is not null) return;
        bool installed = VelopackUpdates.Installed || OperatingSystem.IsMacOS() && Apps.MacBundleOf() is not null;
        if (!installed) return;
        try
        {
            foreach (string line in Run(ClaudeSetup.ThisComputer(home), home, Desktop.Program, () => Desktop.LoginProgram(home),
                         () => Desktop.StartAtLogin(true, home), BackgroundLibrary.ThisComputer()))
                Program.Log("[moved] " + line);
            // A Dock icon pinned to "Study Stash.app" before the rename: pointed at this bundle, once (MacDock.cs).
            if (OperatingSystem.IsMacOS() && Apps.MacBundleOf() is { } bundle && Path.GetFileName(bundle) == MacRename.Name)
                MacDock.Repair(home, bundle, Machine.Run, Program.Log);
            if (OperatingSystem.IsLinux() && Environment.GetEnvironmentVariable("APPIMAGE") is { Length: > 0 } image)
                new LinuxMenu().Write(image, Path.Combine(AppContext.BaseDirectory, "syft-notes.png"), Program.Log);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException or System.Security.SecurityException)
        {
            Program.Log("[moved] couldn't put everything right for the new place: " + e.Message);
        }
    }
}
