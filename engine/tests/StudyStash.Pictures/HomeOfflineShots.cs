using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using StudyStash.App.Views;

namespace StudyStash.App.Pictures;

/// <summary>Home when the library couldn't list the notes: it says the library can't be reached, never "No notes yet".
/// Drawn off screen into STUDYSTASH_SHOTS; only with STUDYSTASH_FILM=1 (demo data only).</summary>
public class HomeOfflineShots
{
    /// <summary>Home as a student sees it when the list failed on a first opening (demo data only).</summary>
    [AvaloniaFact]
    public void Home_when_the_library_cant_list_the_notes_says_so()
    {
        if (Environment.GetEnvironmentVariable("STUDYSTASH_FILM") != "1") return;
        foreach (var skin in new[] { SkinKind.Mac, SkinKind.Win })
            foreach (var t in new[] { ThemeVariant.Light, ThemeVariant.Dark })
                Shot.Take($"{(skin == SkinKind.Mac ? "mac" : "win")}-home-offline", skin, t, () =>
                {
                    var m = Demo.Semester();
                    m.Overview = null;
                    m.Empty = "Can't reach your library. What you record waits on this computer until it's back.";
                    return skin == SkinKind.Mac ? new MacLibrary { DataContext = m, Width = 1280, Height = 860 } : new WinLibrary { DataContext = m, Width = 1280, Height = 860 };
                }, size: new Size(1408, 988));
    }
}
