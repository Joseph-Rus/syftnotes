using System.Runtime.Versioning;
using Microsoft.Win32;
using StudyStash.Core;
using Velopack;
using Velopack.Locators;

namespace StudyStash.App.Platform;

/// <summary>
/// Velopack's start-up code, the first thing every start of the program runs (Program.cs), as Velopack asks: its
/// installer and updater start the program with <c>--veloapp-*</c> arguments, answered here before the process ends.
/// A downloaded update is never applied just because the program started (it would be, by default, even for an AI
/// app's <c>mcp</c> or the background library): only AppUpdates applies one, when nothing is recording. In a copy
/// Velopack didn't install this does nothing.
/// </summary>
static class VelopackHooks
{
    public static void Run()
    {
        var app = VelopackApp.Build().SetAutoApplyOnStartup(false);
        if (OperatingSystem.IsWindows()) app = app.OnBeforeUninstallFastCallback(_ => { if (OperatingSystem.IsWindows()) WindowsUninstalling(); });
        try
        {
            app.Run();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            // Only Velopack's own hooks could fail here: the program goes on as if Velopack weren't there.
            Console.Error.WriteLine("Velopack: " + e.Message);
        }
    }

    /// <summary>The folder Velopack installed this copy into, or null.</summary>
    static string? Root() => VelopackLocator.IsCurrentSet && VelopackLocator.Current.RootAppDir is { Length: > 0 } root ? root : null;

    /// <summary>
    /// Windows' Apps list uninstalls Syft Notes (Velopack's Update.exe --uninstall, which then removes the folder and its
    /// own shortcuts): what the app itself put in this account's registry goes too, as Inno Setup's uninstaller took it,
    /// where it names this install: starting at login, the background library, the notifications' name and the link a
    /// click on one opens, and pins to the taskbar. Settings, lectures and Documents stay. Velopack gives this 60 seconds.
    /// </summary>
    [SupportedOSPlatform("windows")]
    static void WindowsUninstalling()
    {
        if (Root() is not { } root) return;
        bool Ours(string? text) => text?.Contains(root, StringComparison.OrdinalIgnoreCase) == true;
        try
        {
            using (var run = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", writable: true))
                if (run is not null)
                    foreach (string name in run.GetValueNames())
                        if (Ours(run.GetValue(name) as string)) run.DeleteValue(name, throwOnMissingValue: false);
            using (var link = Registry.CurrentUser.OpenSubKey(@"Software\Classes\studystash\shell\open\command"))
                if (Ours(link?.GetValue(null) as string))
                {
                    Registry.CurrentUser.DeleteSubKeyTree(@"Software\Classes\studystash", throwOnMissingSubKey: false);
                    Registry.CurrentUser.DeleteSubKeyTree(@"Software\Classes\AppUserModelId\StudyStash.App", throwOnMissingSubKey: false);
                }
            string pins = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Microsoft", "Internet Explorer", "Quick Launch", "User Pinned", "TaskBar");
            if (Directory.Exists(pins))
                foreach (string lnk in Directory.EnumerateFiles(pins, "*.lnk"))
                    if (LinkNames(lnk, root)) File.Delete(lnk);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            Console.Error.WriteLine("Syft Notes' uninstall cleanup: " + e.Message);
        }
    }

    /// <summary>Whether a shortcut file names a path inside <paramref name="root"/> (a .lnk keeps its target both as
    /// UTF-16 and in the system's own code page; either is enough).</summary>
    internal static bool LinkNames(string lnk, string root)
    {
        byte[] bytes = File.ReadAllBytes(lnk);
        return Contains(bytes, System.Text.Encoding.Unicode.GetBytes(root)) || Contains(bytes, System.Text.Encoding.Latin1.GetBytes(root))
            || Contains(bytes, System.Text.Encoding.Unicode.GetBytes(root.ToLowerInvariant())) || Contains(bytes, System.Text.Encoding.Latin1.GetBytes(root.ToLowerInvariant()));
    }

    static bool Contains(byte[] hay, byte[] needle) => hay.AsSpan().IndexOf(needle) >= 0;

    /// <summary>
    /// Linux: <c>syft-notes --uninstall</c>, what takes an AppImage copy off this account (an AppImage has no
    /// uninstaller of its own): starting at login and the background library stop and go, the menu entry, icon and
    /// PATH link go, then the AppImage itself and Velopack's downloads. Settings, lectures and Documents stay.
    /// </summary>
    public static int LinuxUninstall(Action<string> say)
    {
        string? image = Environment.GetEnvironmentVariable("APPIMAGE");
        if (!OperatingSystem.IsLinux() || string.IsNullOrEmpty(image))
        {
            say("Only the AppImage of Syft Notes uninstalls itself this way.");
            return 2;
        }
        var background = BackgroundLibrary.ThisComputer();
        foreach (var (home, args) in background.All())
            if (args.Count > 0 && (args[0] == image || Moves.OldProgram(args[0], image)))
                background.Unregister(home);
        try
        {
            if (Desktop.StartsAtLogin(Configs.DefaultHome)) Desktop.StartAtLogin(false, Configs.DefaultHome);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            say("Couldn't stop starting at login: " + e.Message);
        }
        new LinuxMenu().Remove();
        foreach (string dir in (string[])["/var/tmp/velopack/SyftNotes", Path.Combine(Py.UserHome(), ".cache", "velopack", "SyftNotes")])
            Apps.TryDeleteFolder(dir);
        try
        {
            File.Delete(image);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            say($"Couldn't delete {image}: {e.Message}");
            return 1;
        }
        say("Syft Notes is uninstalled. Your lectures and notes are still where they were.");
        return 0;
    }
}
