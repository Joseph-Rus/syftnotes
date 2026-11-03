using StudyStash.App.ViewModels;

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
}
