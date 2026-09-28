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

/// <summary>Deleting a lecture from a laptop, against a real library over HTTP: the library deletes it into its trash,
/// Undo brings it back, and once it's gone for good the laptop drops its own recording of it.</summary>
public sealed class LectureDeleteRoundTripTests
{
    [Fact]
    public async Task A_lecture_deleted_from_the_laptop_leaves_the_library_and_its_recording_goes_once_its_gone_for_good()
    {
        using var home = new TempHome();
        var cfg = new Config(home["library"], home["pool"])
        {
            PoolName = "Sam's library", PoolPassword = "pw", OllamaEnabled = false, OllamaHost = "http://127.0.0.1:9",
            Classes = [new ClassDef("CS 101", [])],
        };
        Directory.CreateDirectory(cfg.Home);
        Configs.Save(cfg);
        using var store = new Store(cfg.DbPath, cfg.PoolDir);
        const string Id = "rec-20260923-100212-abcdef";
        store.Save(new Meeting(Id) { Title = "CS 101 lecture, Tue 23 Sep", Date = "2026-09-23T10:02:12-07:00", Folder = "CS 101", Transcript = "[00:05] Recursion." },
            new Classification("CS 101", 1, "folder", "Recursion and the call stack", []), summaryMd: "## Summary\nRecursion.");
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        WebHostBuilderKestrelExtensions.ConfigureKestrel(builder.WebHost, k => k.Listen(IPAddress.Loopback, 0));
        await using var app = LibraryWeb.Build(builder, cfg, store, new Pipeline(cfg, store, log: _ => { }), new LibraryWebOptions
        {
            ListModels = _ => Task.FromResult<List<(string, double)>?>(null), Tailscale = () => new TailscaleInfo(false, false, "", "", []),
            Latest = _ => Task.FromResult<Release?>(null), RamGb = () => 16, HostName = () => "mac-mini",
        });
        await app.StartAsync(TestContext.Current.CancellationToken);
        string url = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();

        // The laptop that recorded it: its recording (audio and what Whisper wrote) is still here, filed.
        string laptopHome = home["laptop"];
        Directory.CreateDirectory(laptopHome);
        new AppSettings { SetupDone = true, Role = AppRole.Laptop }.Save(laptopHome);
        var cc = Configs.LoadClient(laptopHome);
        cc.ServerUrl = url;
        cc.PoolKey = "pw";
        Configs.SaveClient(cc);
        using var host = new AppHost(laptopHome, log: _ => { });
        host.Lectures.Add(new Lecture { Id = Id, Started = "2026-09-23T10:02:12-07:00", State = LectureState.Filed, ClassName = "CS 101" });
        File.WriteAllBytes(host.Lectures.AudioPath(Id), [1, 2, 3]);

        var lib = host.Remote()!;
        Assert.True(await lib.DeleteAsync(Id));
        Assert.Null(store.Get(Id));
        await host.CheckLibraryAsync();
        Assert.Equal(LibraryState.Connected, host.Library);
        Assert.True(File.Exists(host.Lectures.AudioPath(Id)), "Undo can still bring it back, recording and all");

        Assert.NotNull(await lib.RestoreAsync(Id));
        Assert.NotNull(store.Get(Id));
        Assert.True(await lib.DeleteAsync(Id));

        // Its time in the trash is up: the laptop drops its recording on its next look at the library.
        store.EmptyTrash();
        await host.CheckLibraryAsync();
        Assert.Contains(Id, (host.Overview!["gone"] as JsonArray)!.Select(n => n!.GetValue<string>()));
        Assert.Null(host.Lectures.Get(Id));
        Assert.False(File.Exists(host.Lectures.AudioPath(Id)));
    }
}
