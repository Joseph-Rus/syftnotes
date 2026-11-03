using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using StudyStash.App.Services;
using StudyStash.App.ViewModels;
using StudyStash.App.Views;

namespace StudyStash.App.Tests;

/// <summary>A list the library couldn't give is not "No notes yet": the page showing stays when it is only read again,
/// and any other opening says the library can't be reached.</summary>
public class HomeOfflineTests
{
    [Fact]
    public void A_notes_list_that_failed_keeps_the_page_only_when_it_is_read_again()
    {
        Assert.True(OverviewModel.KeepsPageWhenListFails(readingAgain: true, pageShowing: true));
        Assert.False(OverviewModel.KeepsPageWhenListFails(readingAgain: true, pageShowing: false));
        Assert.False(OverviewModel.KeepsPageWhenListFails(readingAgain: false, pageShowing: true));
    }

    /// <summary>Home as a student sees it when the list failed on a first opening (demo data only).</summary>
    [AvaloniaFact]
    public void Home_when_the_library_cant_list_the_notes_says_so()
    {
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
