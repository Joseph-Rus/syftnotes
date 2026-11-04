using StudyStash.App.Services;
using StudyStash.Core;

namespace StudyStash.App.Tests;

/// <summary>"Which notes folder should Syft Notes use?": picking the other folder points config.toml's pool_dir at it,
/// moves no file, and isn't asked again.</summary>
public sealed class LibraryFolderPickTests
{
    static Dictionary<string, long> Files(string dir) =>
        Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).ToDictionary(f => Path.GetRelativePath(dir, f), f => new FileInfo(f).Length);

    [Fact]
    public async Task Picking_the_other_folder_writes_pool_dir_and_moves_nothing()
    {
        using var home = new TempHome();
        string inUse = home["Documents/Study Stash"], older = home["OneDrive/Documents/Study Stash"];
        foreach (var (dir, file) in new[] { (inUse, "2026-11-01 Genes.md"), (older, "2026-10-02 Cell division.md") })
        {
            Directory.CreateDirectory(Path.Combine(dir, "Biology"));
            File.WriteAllText(Path.Combine(dir, "Biology", file), "# Notes\n");
        }
        var cfg = Configs.Load(home.Path);
        cfg.PoolDir = inUse;
        Configs.Save(cfg);
        using var host = new AppHost(home.Path, log: _ => { }, loginItems: new CountingLoginItems());
        host.Save(s => (s.Role, s.SetupDone) = (AppRole.Both, true));
        List<LibraryFolder> Find(string? used) => LibraryFolders.Find(used, [inUse, older]);
        var before = (Files(inUse), Files(older));

        var asked = LibraryFolderPick.ToAsk(host, Find);
        Assert.NotNull(asked);
        var outcome = await LibraryFolderPick.UseAsync(host, asked.Single(f => !f.InUse), asked, ct: TestContext.Current.CancellationToken);

        // The folder is switched, but with no library running nothing could be copied over: that's said, and the
        // answer isn't remembered, so it's asked again at the next start.
        Assert.False(outcome.Ok);
        Assert.Equal(LibraryFolderPick.NotFinished, outcome.Failure);
        Assert.True(LibraryFolders.Comparer.Equals(LibraryFolders.Real(older), LibraryFolders.Real(Configs.Load(home.Path).PoolDir)));
        Assert.Equal(before.Item1, Files(inUse));
        Assert.Equal(before.Item2, Files(older));
        Assert.Null(host.Settings.NotesFoldersSeen);
        Assert.NotNull(LibraryFolderPick.ToAsk(host, Find));
    }
}
