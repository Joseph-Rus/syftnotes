using System.ComponentModel;
using System.Diagnostics;

namespace StudyStash.App.Platform;

/// <summary>Opens a terminal for the student to paste a command into (setup's "Open Terminal"): Terminal on a Mac,
/// PowerShell in Windows Terminal (or on its own) on Windows. Never runs anything in it.</summary>
public static class TerminalApp
{
    public static void Open()
    {
        try
        {
            if (OperatingSystem.IsMacOS())
            {
                using var p = Process.Start(new ProcessStartInfo("open") { ArgumentList = { "-a", "Terminal" }, UseShellExecute = false });
            }
            else if (OperatingSystem.IsWindows())
            {
                try
                {
                    using var wt = Process.Start(new ProcessStartInfo("wt.exe") { ArgumentList = { "powershell.exe" }, UseShellExecute = true });
                }
                catch (Win32Exception)
                {
                    using var ps = Process.Start(new ProcessStartInfo("powershell.exe") { UseShellExecute = true });
                }
            }
        }
        catch (Exception e) when (e is Win32Exception or InvalidOperationException)
        {
            Program.Log($"[setup] couldn't open a terminal: {e.Message}");
        }
    }
}
