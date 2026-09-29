using System.Net;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using StudyStash.App.Services;
using StudyStash.Core;
using StudyStash.Library;

namespace StudyStash.App.Tests;

/// <summary>Changing what a computer is for after setup (RoleSwitch, docs/one-download.md): a laptop becomes the
/// library and can bring its old library's lectures over; a library becomes a laptop, checked first and never
/// deleting anything; the plain-English lines each move says.</summary>
public sealed class RoleSwitchTests
{
    static AppHost Host(string home) => new(home, log: _ => { }, loginItems: new CountingLoginItems());

    static string BuiltEngine()
    {
        var bin = new DirectoryInfo(AppContext.BaseDirectory);
        string config = bin.Parent!.Name, framework = bin.Name;
        string engineDir = Path.GetFullPath(Path.Combine(bin.FullName, "..", "..", "..", "..", "..", "src", "StudyStash.Engine", "bin", config, framework));
        return Path.Combine(engineDir, OperatingSystem.IsWindows() ? "studystash.exe" : "studystash");
    }

    /// <summary>A real library (its web app and API) on a free loopback port, with nothing that reaches out.</summary>
    static async Task<(string Url, WebApplication App, Config Cfg, Store Store)> LibraryAsync(TempHome home, string name, string password, string tag,
        params ClassDef[] classes)
    {
        var cfg = new Config(home[tag + "-home"], home[tag + "-pool"])
        {
            PoolName = name, PoolPassword = password, OllamaEnabled = false, OllamaHost = "http://127.0.0.1:9", Classes = [.. classes],
        };
        Directory.CreateDirectory(cfg.Home);
        Configs.Save(cfg);
        var store = new Store(cfg.DbPath, cfg.PoolDir);
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        WebHostBuilderKestrelExtensions.ConfigureKestrel(builder.WebHost, k => k.Listen(IPAddress.Loopback, 0));
        var app = LibraryWeb.Build(builder, cfg, store, new Pipeline(cfg, store, log: _ => { }), new LibraryWebOptions
        {
            ListModels = _ => Task.FromResult<List<(string, double)>?>(null),
            Tailscale = () => new TailscaleInfo(false, false, "", "", []),
            Latest = _ => Task.FromResult<Release?>(null),
            RamGb = () => 16,
            HostName = () => "mac-mini",
        });
        await app.StartAsync(TestContext.Current.CancellationToken);
        string url = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
        return (url, app, cfg, store);
    }

    static void FileLecture(Store store, string id, string cls) => store.Save(new Meeting(id)
    {
        Title = "Lecture " + id, Date = "2026-09-01", Owner = "Sam", NotesMarkdown = "notes " + id, Transcript = "today " + id,
    }, new Classification(cls, 0.8, "ollama", "Lecture " + id, ["topic"]), "", "");

    // --- OldLibraryOf --------------------------------------------------------------------------------------------

    [Fact]
    public void No_library_connected_means_no_old_library_to_bring_over()
    {
        using var home = new TempHome();
        using var host = Host(home.Path);
        Assert.Null(RoleSwitch.OldLibraryOf(host));
    }

    [Fact]
    public void A_library_on_this_computer_itself_isnt_an_old_library_to_bring_over()
    {
        using var home = new TempHome();
        using var host = Host(home.Path);
        var cc = host.Client();
        cc.ServerUrl = "http://127.0.0.1:8787";
        cc.PoolKey = "pw";
        cc.PoolName = "Ada's library";
        host.SaveClient(cc);
        Assert.Null(RoleSwitch.OldLibraryOf(host));
    }

    [Fact]
    public void A_library_on_another_computer_is_the_old_library_to_bring_over()
    {
        using var home = new TempHome();
        using var host = Host(home.Path);
        var cc = host.Client();
        cc.ServerUrl = "http://mac-mini:8787";
        cc.PoolKey = "pw";
        cc.PoolName = "Sam's library";
        host.SaveClient(cc);
        var old = RoleSwitch.OldLibraryOf(host);
        Assert.Equal(new OldLibrary("http://mac-mini:8787", "pw", "Sam's library"), old);
    }

    // --- ToLaptopAsync ---------------------------------------------------------------------------------------

    [Fact]
    public async Task An_empty_address_is_refused_and_changes_nothing()
    {
        using var home = new TempHome();
        using var host = Host(home.Path);
        host.Save(s => s.Role = AppRole.Library);
        await Assert.ThrowsAsync<InvalidOperationException>(() => RoleSwitch.ToLaptopAsync(host, "  ", "pw"));
        Assert.Equal(AppRole.Library, host.Settings.Role);
    }

    [Fact]
    public async Task This_computers_own_library_is_refused_as_the_address_to_send_to()
    {
        using var home = new TempHome();
        using var host = Host(home.Path);
        var cfg = new Config(home.Path, home["pool"]) { WebPort = 8787, PoolName = "Ada's library" };
        Configs.Save(cfg);
        host.Save(s => s.Role = AppRole.Both);
        var e = await Assert.ThrowsAsync<InvalidOperationException>(() => RoleSwitch.ToLaptopAsync(host, "http://127.0.0.1:8787", "pw"));
        Assert.Contains("own library", e.Message, StringComparison.Ordinal);
        Assert.Equal(AppRole.Both, host.Settings.Role);
    }

    [Fact]
    public async Task A_wrong_password_says_so_in_plain_words_and_changes_nothing()
    {
        using var home = new TempHome();
        using var host = Host(home.Path);
        host.Save(s => s.Role = AppRole.Library);
        var e = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            RoleSwitch.ToLaptopAsync(host, "http://mac-mini:8787", "wrong", (_, _) => throw new InvalidOperationException("wrong password")));
        Assert.Equal("That password isn't right.", e.Message);
        Assert.Equal(AppRole.Library, host.Settings.Role);
    }

    [Fact]
    public async Task An_address_that_cant_be_reached_says_so_and_changes_nothing()
    {
        using var home = new TempHome();
        using var host = Host(home.Path);
        host.Save(s => s.Role = AppRole.Library);
        var e = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            RoleSwitch.ToLaptopAsync(host, "http://mac-mini:8787", "pw", (_, _) => throw new InvalidOperationException("boom")));
        Assert.Contains("Tailscale", e.Message, StringComparison.Ordinal);
        Assert.Equal(AppRole.Library, host.Settings.Role);
    }

    [Fact]
    public async Task A_working_address_makes_this_computer_a_laptop_and_starts_a_download_for_a_computer_that_never_recorded()
    {
        using var home = new TempHome();
        using var host = Host(home.Path);
        host.Save(s => s.Role = AppRole.Library);
        string said = await RoleSwitch.ToLaptopAsync(host, "mac-mini", "the-pw",
            (_, _) => Task.FromResult(new JsonObject { ["pool_name"] = "Sam's library" }));

        Assert.Equal(AppRole.Laptop, host.Settings.Role);
        var cc = host.Client();
        Assert.Equal("http://mac-mini:8787", cc.ServerUrl);
        Assert.Equal("the-pw", cc.PoolKey);
        Assert.Equal("Sam's library", cc.PoolName);
        Assert.Contains("laptop now", said, StringComparison.Ordinal);
        Assert.Contains("Sam's library", said, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_computer_that_already_recorded_as_a_laptop_before_doesnt_start_a_fresh_download()
    {
        using var home = new TempHome();
        using var host = Host(home.Path);
        host.Save(s => s.Role = AppRole.Laptop);
        await RoleSwitch.ToLaptopAsync(host, "mac-mini", "the-pw", (_, _) => Task.FromResult(new JsonObject { ["pool_name"] = "Sam's library" }));
        Assert.False(host.Downloading is not null); // nothing kicked off: it already recorded as a laptop before
    }

    // --- BroughtWords / SentWords: the plain words each direction says --------------------------------------------

    [Fact]
    public void BroughtWords_says_how_many_came_over_and_that_the_old_library_keeps_its_own()
    {
        Assert.Equal("Sam's library had no lectures to bring over.", RoleSwitch.BroughtWords(new MoveResult(0, 0, 0), "Sam's library"));
        Assert.Equal("Every lecture from Sam's library is already here. It keeps its own copy.", RoleSwitch.BroughtWords(new MoveResult(0, 3, 0), "Sam's library"));
        Assert.Equal("1 lecture came over from Sam's library. It keeps its own copy.", RoleSwitch.BroughtWords(new MoveResult(1, 0, 0), "Sam's library"));
        Assert.Equal("3 lectures and 1 class came over from Sam's library. 1 was already here. It keeps its own copy.", RoleSwitch.BroughtWords(new MoveResult(3, 1, 1), "Sam's library"));
        Assert.Equal("Your old library had no lectures to bring over.", RoleSwitch.BroughtWords(new MoveResult(0, 0, 0), ""));
    }

    [Fact]
    public void SentWords_says_how_many_went_over_and_that_this_library_keeps_its_own()
    {
        Assert.Equal("There were no lectures to send.", RoleSwitch.SentWords(new MoveResult(0, 0, 0)));
        Assert.Equal("Every lecture is already there. This library keeps its own copy.", RoleSwitch.SentWords(new MoveResult(0, 2, 0)));
        Assert.Equal("1 lecture went to your new library. This library keeps its own copy.", RoleSwitch.SentWords(new MoveResult(1, 0, 0)));
        Assert.Equal("4 lectures went to your new library. 2 were already there. This library keeps its own copy.", RoleSwitch.SentWords(new MoveResult(4, 2, 0)));
    }

    // --- ToLibraryAsync and BringLecturesAsync: real end-to-end moves ---------------------------------------------

    [Fact]
    public async Task A_laptop_becomes_the_library_taking_the_old_ones_name_and_password_when_it_has_none_of_its_own()
    {
        string exe = BuiltEngine();
        Assert.True(File.Exists(exe), $"build the solution first: no {exe}");
        using var home = new TempHome();
        using var host = Host(home.Path);
        host.Save(s => s.Role = AppRole.Laptop);
        var old = new OldLibrary("http://mac-mini:8787", "old-pw", "Sam's library");
        var here = new LibraryHere { Command = [exe], Folder = home["pool"] };
        try
        {
            string said = await RoleSwitch.ToLibraryAsync(host, here, old, "Ada");
            Assert.Contains("ready", said, StringComparison.Ordinal);
            Assert.Equal(AppRole.Both, host.Settings.Role);
            var cfg = Configs.Load(home.Path);
            Assert.Equal("Sam's library", cfg.PoolName);
            Assert.Equal("old-pw", cfg.PoolPassword);
        }
        finally { if (host.LocalLibrary is { } svc) await svc.StopAsync(); }
    }

    [Fact]
    public async Task Bringing_lectures_over_needs_a_library_on_this_computer_first()
    {
        using var home = new TempHome();
        using var host = Host(home.Path);
        var old = new OldLibrary("http://mac-mini:8787", "pw", "Sam's library");
        await Assert.ThrowsAsync<InvalidOperationException>(() => RoleSwitch.BringLecturesAsync(host, old, ct: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Every_lecture_from_the_old_library_comes_over_and_it_keeps_its_own()
    {
        using var home = new TempHome();
        var (oldUrl, oldApp, _, oldStore) = await LibraryAsync(home, "Sam's library", "old-pw", "old", new ClassDef("CS 101", []));
        await using var _oldApp = oldApp;
        using var _oldStore = oldStore;
        FileLecture(oldStore, "a1", "CS 101");
        FileLecture(oldStore, "a2", "CS 101");

        var (newUrl, newApp, newCfg, newStore) = await LibraryAsync(home, "Ada's library", "new-pw", "new");
        await using var _newApp = newApp;
        using var _newStore = newStore;

        using var host = Host(home.Path);
        var cc = host.Client();
        cc.ServerUrl = newUrl;
        cc.PoolKey = "new-pw";
        host.SaveClient(cc);

        var old = new OldLibrary(oldUrl, "old-pw", "Sam's library");
        var seen = new List<MoveProgress>();
        string said = await RoleSwitch.BringLecturesAsync(host, old, seen.Add, ct: TestContext.Current.CancellationToken);

        Assert.Equal("2 lectures and 1 class came over from Sam's library. It keeps its own copy.", said);
        Assert.Equal(2, seen[^1].Total);
        Assert.NotNull(newStore.Get("a1"));
        Assert.NotNull(oldStore.Get("a1")); // the old library keeps every lecture of its own
    }

    [Fact]
    public async Task Handing_lectures_off_needs_a_library_on_this_computer_first()
    {
        using var home = new TempHome();
        using var host = Host(home.Path);
        await Assert.ThrowsAsync<InvalidOperationException>(() => RoleSwitch.HandOffLecturesAsync(host, "http://mac-mini:8787", "pw", ct: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Every_lecture_here_is_sent_to_the_new_library_before_this_one_becomes_a_laptop()
    {
        using var home = new TempHome();
        var (hereUrl, hereApp, _, hereStore) = await LibraryAsync(home, "Ada's library", "here-pw", "here", new ClassDef("CS 101", []));
        await using var _hereApp = hereApp;
        using var _hereStore = hereStore;
        FileLecture(hereStore, "a1", "CS 101");

        var (newUrl, newApp, _, newStore) = await LibraryAsync(home, "Sam's library", "new-pw", "new");
        await using var _newApp = newApp;
        using var _newStore = newStore;

        using var host = Host(home.Path);
        var cc = host.Client();
        cc.ServerUrl = hereUrl;
        cc.PoolKey = "here-pw";
        host.SaveClient(cc);

        var seen = new List<MoveProgress>();
        string said = await RoleSwitch.HandOffLecturesAsync(host, newUrl, "new-pw", seen.Add, ct: TestContext.Current.CancellationToken);

        Assert.Equal("1 lecture and 1 class went to your new library. This library keeps its own copy.", said);
        Assert.NotNull(newStore.Get("a1"));
        Assert.NotNull(hereStore.Get("a1")); // this library keeps every lecture of its own
    }
}
