using System.Runtime.Versioning;
using Microsoft.Win32;
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
/// the self-test, or a connection to another copy someone keeps on purpose. On Windows the Inno Setup install the move
/// left behind goes (<see cref="ClearOldInstall"/>). Settings, lectures and Documents are never touched.
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

    /// <summary>Inno Setup's entry for this account's old install (setup.iss: its AppId, per-user).</summary>
    public const string InnoKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\{6F2C9A51-3B7E-4D28-9C41-8E5A0B7D2F63}_is1";

    /// <summary>
    /// The Inno Setup install the move to Velopack left behind (its uninstaller can hang on Windows 11, and it would
    /// end every StudyStash.exe, this copy's too): its folder <paramref name="folder"/> is deleted directly, never by
    /// running its uninstaller, then its Apps-list entry (<paramref name="forgetEntry"/>), the shortcuts in
    /// <paramref name="shortcutPlaces"/> that still start a program in it, and the remembered failed move. Only a
    /// folder <see cref="Moves.OldInstallFolder"/> agrees is the old install; one with a file in use stays, and is
    /// tried again on the next start. What was done (or what's left), for the log; null when there's nothing to do.
    /// </summary>
    public static string? ClearOldInstall(string? folder, string running, string home, IEnumerable<string> shortcutPlaces, Action forgetEntry)
    {
        if (!Moves.OldInstallFolder(folder, running)) return null;
        string old = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder!));
        if (Directory.Exists(old) && Moves.IsLink(old))
            return $"the old install in {old} is a link to another folder, so it's left as it is";
        if (Moves.DeleteOldInstall(old) is { } held)
            return $"the old install in {old} is still there ({Path.GetFileName(held)} is in use); trying again next start";
        try
        {
            foreach (string place in shortcutPlaces.Where(Directory.Exists))
                foreach (string lnk in Directory.EnumerateFiles(place, "*.lnk").ToList())
                    if (VelopackHooks.LinkNames(lnk, old + Path.DirectorySeparatorChar)) File.Delete(lnk);
            forgetEntry();
            File.Delete(Bridge.FailedFile(home));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return $"removed the old install in {old}, but not all of what named it ({e.Message}); trying again next start";
        }
        return $"removed the old install in {old}";
    }

    /// <summary><see cref="ClearOldInstall"/> for the Inno Setup entry <paramref name="key"/> in this account's
    /// registry, and its Start-menu and desktop shortcuts.</summary>
    [SupportedOSPlatform("windows")]
    public static string? ClearOldInno(string home, string key, IEnumerable<string>? shortcutPlaces = null)
    {
        string? folder;
        using (var entry = Registry.CurrentUser.OpenSubKey(key))
            folder = entry?.GetValue("InstallLocation") as string;
        return ClearOldInstall(folder, AppContext.BaseDirectory, home,
            shortcutPlaces ?? [Environment.GetFolderPath(Environment.SpecialFolder.Programs), Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)],
            () => Registry.CurrentUser.DeleteSubKeyTree(key, throwOnMissingSubKey: false));
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
            // First, so the old copy is gone by the time the AI apps' connections name this one.
            if (OperatingSystem.IsWindows() && ClearOldInno(home, InnoKey) is { } cleared) Program.Log("[moved] " + cleared);
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
