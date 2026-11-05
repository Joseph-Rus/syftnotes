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
        Assert.Equal(LibraryFolderPick.NotFinished(1).Words, outcome.Failure?.Words);
        Assert.True(LibraryFolders.Comparer.Equals(LibraryFolders.Real(older), LibraryFolders.Real(Configs.Load(home.Path).PoolDir)));
        Assert.Equal(before.Item1, Files(inUse));
        Assert.Equal(before.Item2, Files(older));
        Assert.Null(host.Settings.NotesFoldersSeen);
        Assert.NotNull(LibraryFolderPick.ToAsk(host, Find));
        // Picking the row the window still tags "In use now" goes by config.toml, not the tag: it switches back.
        var back = await LibraryFolderPick.UseAsync(host, asked.Single(f => f.InUse), asked, ct: TestContext.Current.CancellationToken);
        Assert.Equal(LibraryFolderPick.NotFinished(1).Words, back.Failure?.Words);
        Assert.True(LibraryFolderPick.IsInUse(Configs.Load(home.Path).PoolDir, inUse));
        Assert.Null(host.Settings.NotesFoldersSeen);
    }

    /// <summary>What happens at start: two folders with notes still ask; one earlier folder (the one in use empty) is
    /// brought in without a question, until it's been done; only the folder in use lists its own notes.</summary>
    [Fact]
    public void One_earlier_folder_is_brought_in_and_two_still_ask()
    {
        using var home = new TempHome();
        string inUse = home["Documents/Study Stash"], older = home["OneDrive/Documents/Study Stash"];
        Directory.CreateDirectory(Path.Combine(older, "Biology"));
        File.WriteAllText(Path.Combine(older, "Biology", "2026-10-02 Cell division.md"), "# Notes\n");
        var cfg = Configs.Load(home.Path);
        cfg.PoolDir = inUse;
        Configs.Save(cfg);
        using var host = new AppHost(home.Path, log: _ => { }, loginItems: new CountingLoginItems());
        host.Save(s => (s.Role, s.SetupDone) = (AppRole.Both, true));
        List<LibraryFolder> Find(string? used) => LibraryFolders.Find(used, [inUse, older]);

        var (step, found) = LibraryFolderPick.Look(host, Find);
        Assert.Equal(LibraryFolderPick.AtStart.BringIn, step);
        Assert.True(LibraryFolders.Comparer.Equals(LibraryFolders.Real(older), Assert.Single(found).Path));
        Assert.Equal("Brought in 2 lectures from your earlier Syft Notes folder.", LibraryFolderPick.StartWords(step, [new(older, 2, 0, 0, 0, 1, 0)]));
        Assert.Null(LibraryFolderPick.StartWords(step, [new(older, 0, 2, 0, 0, 0, 0)]));

        // Done once (remembered with both folders): from then on, only the folder in use's own notes.
        host.Save(s =>
        {
            s.NotesFoldersSeen = [.. found.Select(f => f.Path), inUse];
            s.NotesFolderChosen = inUse;
        });
        Directory.CreateDirectory(Path.Combine(inUse, "Biology"));
        File.WriteAllText(Path.Combine(inUse, "Biology", "2026-10-02 Cell division.md"), "# Notes\n");
        Assert.Equal(LibraryFolderPick.AtStart.ListOwn, LibraryFolderPick.Look(host, Find).Step);

        // A third folder with notes turns up: two or more to choose from, so the question comes back.
        string school = home["OneDrive - Example University/Documents/Study Stash"];
        Directory.CreateDirectory(Path.Combine(school, "Biology"));
        File.WriteAllText(Path.Combine(school, "Biology", "2026-11-01 Genes.md"), "# Notes\n");
        Assert.Equal(LibraryFolderPick.AtStart.Ask, LibraryFolderPick.Look(host, used => LibraryFolders.Find(used, [inUse, older, school])).Step);
    }
}
