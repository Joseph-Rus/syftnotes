using StudyStash.App.Services;

namespace StudyStash.App.Tests;

public class BrandRefreshTests
{
    [Fact]
    public void The_Mac_asks_only_about_this_bundle_and_never_restarts_the_Dock_or_Finder()
    {
        var commands = BrandRefresh.MacCommands("/Applications/Study Stash.app");
        Assert.All(commands, c => Assert.Equal("/Applications/Study Stash.app", c[^1]));
        Assert.DoesNotContain(commands, c => c[0].EndsWith("killall", StringComparison.Ordinal));
        Assert.Contains(commands, c => c[0].EndsWith("lsregister", StringComparison.Ordinal) && c.Contains("-f"));
    }

    [Fact]
    public void A_build_run_from_its_folder_asks_the_system_nothing_and_isnt_remembered()
    {
        Assert.False(BrandRefresh.Run(baseDir: Path.GetTempPath()));
    }
}
