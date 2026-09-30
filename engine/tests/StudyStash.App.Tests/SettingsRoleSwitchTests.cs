using System.Net;
using Avalonia.Headless.XUnit;
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

/// <summary>Settings → Connection's "This computer": switching role end to end through the model a real window would
/// bind to (RoleSwitch, docs/one-download.md).</summary>
public sealed class SettingsRoleSwitchTests
{
    static string BuiltEngine()
    {
        var bin = new DirectoryInfo(AppContext.BaseDirectory);
        string config = bin.Parent!.Name, framework = bin.Name;
        string engineDir = Path.GetFullPath(Path.Combine(bin.FullName, "..", "..", "..", "..", "..", "src", "StudyStash.Engine", "bin", config, framework));
        return Path.Combine(engineDir, OperatingSystem.IsWindows() ? "studystash.exe" : "studystash");
    }

    static async Task<(string Url, WebApplication App, Store Store)> LibraryAsync(TempHome home, string name, string password, string tag)
    {
        var cfg = new Config(home[tag + "-home"], home[tag + "-pool"]) { PoolName = name, PoolPassword = password, OllamaEnabled = false, OllamaHost = "http://127.0.0.1:9" };
        Directory.CreateDirectory(cfg.Home);
        Configs.Save(cfg);
        var store = new Store(cfg.DbPath, cfg.PoolDir);
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        WebHostBuilderKestrelExtensions.ConfigureKestrel(builder.WebHost, k => k.Listen(IPAddress.Loopback, 0));
        var app = LibraryWeb.Build(builder, cfg, store, new Pipeline(cfg, store, log: _ => { }), new LibraryWebOptions
        {
            ListModels = _ => Task.FromResult<List<(string, double)>?>(null), Tailscale = () => new TailscaleInfo(false, false, "", "", []),
            Latest = _ => Task.FromResult<Release?>(null), RamGb = () => 16, HostName = () => "mac-mini",
        });
        await app.StartAsync(TestContext.Current.CancellationToken);
        string url = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
        return (url, app, store);
    }

    static void FileLecture(Store store, string id) => store.Save(new Meeting(id)
    {
        Title = "Lecture " + id, Date = "2026-09-01", Owner = "Sam", NotesMarkdown = "notes " + id, Transcript = "today " + id,
    }, new Classification(Configs.Unsorted, 0, "none", "Lecture " + id, []), "", "");

    [AvaloniaFact]
    public void The_role_line_says_what_this_computer_is_for()
    {
        using var home = new TempHome();
        new AppSettings { SetupDone = true, Role = AppRole.Laptop }.Save(home.Path);
        using var host = new AppHost(home.Path, log: _ => { });
        using var model = SettingsModel.Make(host);
        Assert.Contains("laptop", model.RoleWords, StringComparison.Ordinal);
        Assert.True(model.IsLaptopRole);
        Assert.False(model.IsLibraryRole);
    }

    [AvaloniaFact]
    public void Asking_to_become_the_library_explains_what_happens_and_names_the_old_library()
    {
        using var home = new TempHome();
        new AppSettings { SetupDone = true, Role = AppRole.Laptop }.Save(home.Path);
        var cc = Configs.LoadClient(home.Path);
        cc.ServerUrl = "http://mac-mini:8787";
        cc.PoolName = "Sam's library";
        Configs.SaveClient(cc);
        using var host = new AppHost(home.Path, log: _ => { });
        using var model = SettingsModel.Make(host);

        model.AskBecomeLibraryCommand.Execute(null);

        Assert.True(model.ConfirmingBecomeLibrary);
        Assert.Contains("Sam's library", model.BecomeLibraryQuestion, StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public void Asking_to_become_a_laptop_without_an_address_says_so_and_asks_nothing()
    {
        using var home = new TempHome();
        new AppSettings { SetupDone = true, Role = AppRole.Library }.Save(home.Path);
        using var host = new AppHost(home.Path, log: _ => { });
        using var model = SettingsModel.Make(host);

        model.AskBecomeLaptopCommand.Execute(null);

        Assert.False(model.ConfirmingBecomeLaptop);
        Assert.Contains("address", model.SwitchSay, StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public async Task Confirming_becoming_the_library_switches_the_role_and_starts_it()
    {
        string exe = BuiltEngine();
        Assert.True(File.Exists(exe), $"build the solution first: no {exe}");
        using var home = new TempHome();
        // A laptop not yet connected to any library: becoming one names it after the student, and there's nothing
        // to bring over (RoleSwitchTests covers that, with an old library on another host to bring lectures from).
        new AppSettings { SetupDone = true, Role = AppRole.Laptop }.Save(home["laptop"]);
        using var host = new AppHost(home["laptop"], log: _ => { });
        using var model = SettingsModel.Make(host).WithLibraryHere(() => new LibraryHere { Command = [exe], Folder = home["new-pool"] });
        model.DisplayName = "Ada";
        try
        {
            model.AskBecomeLibraryCommand.Execute(null);
            await model.ConfirmBecomeLibraryCommand.ExecuteAsync(null);

            Assert.Equal(AppRole.Both, host.Settings.Role);
            Assert.True(model.LibraryHere);
            Assert.False(model.ConfirmingBecomeLibrary);
            Assert.Contains("Ada's library", model.SwitchSay, StringComparison.Ordinal);
        }
        finally
        {
            if (host.LocalLibrary is { } svc) await svc.StopAsync();
        }
    }

    [AvaloniaFact]
    public async Task Confirming_becoming_a_laptop_sends_lectures_first_then_switches()
    {
        string exe = BuiltEngine();
        Assert.True(File.Exists(exe), $"build the solution first: no {exe}");
        using var home = new TempHome();
        var (newUrl, newApp, newStore) = await LibraryAsync(home, "Sam's library", "new-pw", "new");
        await using var _newApp = newApp;
        using var _newStore = newStore;

        using var host = new AppHost(home.Path, log: _ => { });
        // Write config.toml and a lecture before starting the library, the way LibraryHere.CreateAsync finds an
        // existing one and keeps its notes: the child process opens the same database once it starts.
        // A port of its own: the default (8787) may be a real library's on this computer, and this one keeps whatever
        // port its config says, so the hand-off would reach that library instead.
        var cfg = new Config(home.Path, home["here-pool"]) { PoolName = "Ada's library", PoolPassword = "here-pw", WebPort = FreePort() };
        Directory.CreateDirectory(cfg.Home);
        Directory.CreateDirectory(cfg.PoolDir);
        Configs.Save(cfg);
        using (var seed = new Store(cfg.DbPath, cfg.PoolDir)) FileLecture(seed, "b1");

        var here = new LibraryHere { Command = [exe] };
        string done = await here.CreateAsync(host, null, null, "Ada");
        Assert.Contains("ready", done, StringComparison.Ordinal);

        try
        {
            using var model = SettingsModel.Make(host);
            model.BecomeLaptopAddress = newUrl;
            model.BecomeLaptopPassword = "new-pw";

            model.AskBecomeLaptopCommand.Execute(null);
            Assert.True(model.ConfirmingBecomeLaptop);
            await model.ConfirmBecomeLaptopCommand.ExecuteAsync(null);

            Assert.Equal(AppRole.Laptop, host.Settings.Role);
            Assert.False(model.ConfirmingBecomeLaptop);
            Assert.False(model.LibraryHere);
            Assert.NotNull(newStore.Get("b1"));
        }
        finally
        {
            if (host.LocalLibrary is { } svc) await svc.StopAsync();
        }
    }

    static int FreePort()
    {
        for (int port = 18_787; port < 19_787; port += 2)
            if (StudyStash.Core.HostInfo.PortFree(port) && StudyStash.Core.HostInfo.PortFree(port + 1)) return port;
        throw new InvalidOperationException("no free ports");
    }
}
