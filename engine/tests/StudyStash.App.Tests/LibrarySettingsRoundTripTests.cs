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
using StudyStash.Core.Ai;
using StudyStash.App.ViewModels;
using StudyStash.Library;

namespace StudyStash.App.Tests;

/// <summary>Settings → Your library against a real library over HTTP, the way a laptop reaches its Mac mini: what the
/// laptop changes lands in the library's config and shows on the library's own web page.</summary>
public sealed class LibrarySettingsRoundTripTests
{
    /// <summary>A real library (its web app and API) on a free port on this computer, with nothing that reaches out:
    /// no Ollama, no Tailscale, no GitHub.</summary>
    static async Task<(string Url, WebApplication App, Config Cfg, Store Store)> LibraryAsync(TempHome home)
    {
        var cfg = new Config(home["library"], home["pool"])
        {
            PoolName = "Sam's library", PoolPassword = "old-pw", OllamaEnabled = false, OllamaHost = "http://127.0.0.1:9",
            Classes = [new ClassDef("CS 101", ["cs101"], "Recursion and the call stack")],
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
        await app.StartAsync();
        string url = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
        return (url, app, cfg, store);
    }

    /// <summary>A change sent in the background (a switch) has reached the library once <paramref name="done"/> holds.</summary>
    static async Task Until(Func<bool> done)
    {
        // Up to 30 s: a busy CI machine can take a few seconds to save a switch; it's quick when all is well.
        for (int i = 0; i < 1200 && !done(); i++) await Task.Delay(25);
        Assert.True(done());
    }

    /// <summary>Logs in to the library's web page from a browser of its own: where it lands, and its Settings page.</summary>
    static async Task<(bool In, string Settings)> WebPageAsync(string url, string password)
    {
        using var web = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = true, CookieContainer = new CookieContainer() })
        {
            BaseAddress = new Uri(url),
        };
        using var login = await web.PostAsync("/login", new FormUrlEncodedContent(new Dictionary<string, string> { ["password"] = password, ["next"] = "/settings" }));
        bool loggedIn = login.Headers.Location?.OriginalString == "/settings";
        return (loggedIn, loggedIn ? await web.GetStringAsync("/settings") : "");
    }

    [AvaloniaFact]
    public async Task Renaming_adding_a_class_sorting_and_a_new_password_reach_the_librarys_config_and_web_page()
    {
        using var home = new TempHome();
        var (url, app, cfg, store) = await LibraryAsync(home);
        await using var _app = app;
        using var _store = store;
        string laptopHome = home["laptop"];
        Directory.CreateDirectory(laptopHome);
        new AppSettings { SetupDone = true, Role = AppRole.Laptop }.Save(laptopHome);
        var cc = Configs.LoadClient(laptopHome);
        cc.ServerUrl = url;
        cc.PoolKey = "old-pw";
        cc.PoolName = "Sam's library";
        Configs.SaveClient(cc);
        using var host = new AppHost(laptopHome, log: _ => { });
        using var settings = SettingsModel.Make(host);
        var lib = settings.Lib;

        settings.Section = "Library";
        await Until(() => lib.IsReady);
        Assert.Equal("Sam's library", lib.Name);

        lib.Name = "Sam's home library";
        await lib.SaveNameCommand.ExecuteAsync(null);
        lib.NewClass = "BIO 110";
        await lib.AddClassCommand.ExecuteAsync(null);
        settings.Section = "Notes";
        lib.SortWithAi = true;
        await Until(() => Configs.Load(cfg.Home).OllamaEnabled);
        settings.Section = "Library";
        lib.ChangePasswordCommand.Execute(null);
        lib.NewPassword = "new-pw";
        await lib.SavePasswordCommand.ExecuteAsync(null);

        // The library's config, as its next start reads it.
        var saved = Configs.Load(cfg.Home);
        Assert.Equal("Sam's home library", saved.PoolName);
        Assert.Equal(["CS 101", "BIO 110"], saved.Classes.Select(c => c.Name));
        Assert.Equal(["cs101"], saved.Classes[0].Aliases);
        Assert.True(saved.OllamaEnabled);
        Assert.Equal("new-pw", saved.PoolPassword);
        Assert.Null(lib.Say);
        Assert.Equal("Changed. Your other computers need the new password to connect.", lib.PasswordSay);

        // The laptop keeps up: it connects with the new password, under the new name, and reads the library again.
        var mine = Configs.LoadClient(laptopHome);
        Assert.Equal("new-pw", mine.PoolKey);
        Assert.Equal("Sam's home library", mine.PoolName);
        await lib.LoadCommand.ExecuteAsync(null);
        Assert.True(lib.IsReady);
        Assert.Equal(["CS 101", "BIO 110"], lib.Classes.Select(c => c.Name));
        Assert.True(lib.SortWithAi);

        // The library's web page: the old password no longer opens it; the new one shows every change.
        Assert.False((await WebPageAsync(url, "old-pw")).In);
        var (loggedIn, page) = await WebPageAsync(url, "new-pw");
        Assert.True(loggedIn);
        Assert.Contains("Sam&#x27;s home library", page);
        Assert.Contains("value=\"BIO 110\"", page);
        Assert.Contains("name=\"ollama_enabled\" value=\"1\" checked", page);
    }

    [AvaloniaFact]
    public async Task The_rich_notes_switches_and_Claude_Codes_speed_set_on_the_laptop_reach_the_librarys_ai_json_and_web_page()
    {
        using var home = new TempHome();
        var (url, app, cfg, store) = await LibraryAsync(home);
        await using var _app = app;
        using var _store = store;

        // The laptop's Settings → AI engines, reading and changing the library's AI over its API.
        var model = new AiEnginesModel(new AiRemote(url, "old-pw"));
        await model.Load();
        Assert.True(model.HasRich && model.RichOn && model.RichDiagrams && model.RichPlots && model.RichDrawings); // on, as before
        Assert.Equal("standard", model.SelectedSpeed);

        model.RichPlots = false;
        await Until(() => !AiSettings.Load(cfg.Home).RichPlots);
        model.SelectedSpeed = "fast";
        await Until(() => AiSettings.Load(cfg.Home).Speed == "fast");
        Assert.Equal(RichKinds.Diagrams | RichKinds.Drawings, AiSettings.Load(cfg.Home).Kinds());

        // The library's own web page shows what the laptop set: the speed picked, the kind off, rich notes still on.
        var (loggedIn, page) = await WebPageAsync(url, "old-pw");
        Assert.True(loggedIn);
        Assert.Contains("<option value=\"fast\" selected>", page);
        Assert.Contains("name=\"ai_rich\" value=\"1\" checked", page);
        Assert.Contains("name=\"ai_rich_diagrams\" value=\"1\" checked", page);
        Assert.Contains("name=\"ai_rich_plots\" value=\"1\">", page);

        // Rich notes off: plain notes, nothing extra asked; the laptop reading the library again still shows it off.
        model.RichOn = false;
        await Until(() => !AiSettings.Load(cfg.Home).RichNotes);
        Assert.Equal(RichKinds.None, AiSettings.Load(cfg.Home).Kinds());
        var again = new AiEnginesModel(new AiRemote(url, "old-pw"));
        await again.Load();
        Assert.False(again.RichOn);
        Assert.Equal("fast", again.SelectedSpeed);
        Assert.Contains("name=\"ai_rich\" value=\"1\">", (await WebPageAsync(url, "old-pw")).Settings);
    }
}
