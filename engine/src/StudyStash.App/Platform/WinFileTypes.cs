using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32;
using StudyStash.Core;

namespace StudyStash.App.Platform;

/// <summary>
/// Windows: a lecture a friend shared (a .syft file) opens in Syft Notes with a double-click, for this account. Velopack
/// has no file types of its own, so the installed app says it here at each start (the same values every time; nothing
/// changes when they're there already), as it does for its notifications' link. The uninstallers take them out.
/// </summary>
[SupportedOSPlatform("windows")]
static partial class WinFileTypes
{
    public const string Kind = "SyftNotes.Lecture";
    const string Classes = @"Software\Classes\";

    public static void Register(string program)
    {
        string command = $"\"{program}\" \"%1\"";
        using (var open = Registry.CurrentUser.OpenSubKey($@"{Classes}{Kind}\shell\open\command"))
            if (open?.GetValue("") as string == command) return;
        using (var ending = Registry.CurrentUser.CreateSubKey(Classes + SyftFile.Ending)) ending.SetValue("", Kind);
        using (var kind = Registry.CurrentUser.CreateSubKey(Classes + Kind)) kind.SetValue("", "Syft Notes lecture");
        using (var icon = Registry.CurrentUser.CreateSubKey($@"{Classes}{Kind}\DefaultIcon")) icon.SetValue("", $"\"{program}\",0");
        using (var open = Registry.CurrentUser.CreateSubKey($@"{Classes}{Kind}\shell\open\command")) open.SetValue("", command);
        // Explorer shows the new icon and opens with it straight away, not after a restart.
        SHChangeNotify(0x08000000 /* SHCNE_ASSOCCHANGED */, 0, IntPtr.Zero, IntPtr.Zero);
    }

    /// <summary>Takes them out, when they open <paramref name="ours"/> (the copy being uninstalled).</summary>
    public static void Unregister(Func<string?, bool> ours)
    {
        using (var open = Registry.CurrentUser.OpenSubKey($@"{Classes}{Kind}\shell\open\command"))
            if (!ours(open?.GetValue("") as string)) return;
        Registry.CurrentUser.DeleteSubKeyTree(Classes + Kind, throwOnMissingSubKey: false);
        using (var ending = Registry.CurrentUser.OpenSubKey(Classes + SyftFile.Ending))
            if (ending?.GetValue("") as string != Kind) return;
        Registry.CurrentUser.DeleteSubKeyTree(Classes + SyftFile.Ending, throwOnMissingSubKey: false);
    }

    [LibraryImport("shell32.dll")]
    private static partial void SHChangeNotify(int eventId, uint flags, IntPtr item1, IntPtr item2);
}
