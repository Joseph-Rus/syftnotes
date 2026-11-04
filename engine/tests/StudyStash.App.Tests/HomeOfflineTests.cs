using Avalonia.Media;
using StudyStash.App.ViewModels;

namespace StudyStash.App.Tests;

/// <summary>A list the library couldn't give is not "No notes yet": the page showing stays when it is only read again,
/// and any other opening says the library can't be reached.</summary>
public class HomeOfflineTests
{
    [Fact]
    public void A_notes_list_that_failed_keeps_the_page_only_when_that_page_is_showing()
    {
        var sources = new OverviewModel.Sources { Now = DateTimeOffset.UnixEpoch, Zone = TimeZoneInfo.Utc, Classes = [("CS 101", Brushes.Gray, 0)] };
        var home = OverviewModel.Home(sources, onlyUnsorted: false);
        var folder = OverviewModel.ForClass(new ClassHomeFacts("CS 101", Brushes.Gray, 0, null, null, null, null), sources);

        Assert.True(OverviewModel.KeepsPageWhenListFails(home, null, unsorted: false)); // Home, read again
        Assert.True(OverviewModel.KeepsPageWhenListFails(folder, "CS 101", unsorted: false)); // the folder, read again
        Assert.False(OverviewModel.KeepsPageWhenListFails(folder, null, unsorted: false)); // Back from the folder to Home
        Assert.False(OverviewModel.KeepsPageWhenListFails(home, "CS 101", unsorted: false)); // Home to a folder
        Assert.False(OverviewModel.KeepsPageWhenListFails(folder, "BIO 110", unsorted: false)); // one folder to another
        Assert.False(OverviewModel.KeepsPageWhenListFails(home, null, unsorted: true)); // Home's other filter
        Assert.False(OverviewModel.KeepsPageWhenListFails(null, null, unsorted: false)); // nothing showing
    }
}
