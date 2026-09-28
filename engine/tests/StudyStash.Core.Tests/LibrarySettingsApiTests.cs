using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using StudyStash.Core.Ai;
using Microsoft.AspNetCore.TestHost;
using StudyStash.Library;

namespace StudyStash.Core.Tests;

/// <summary>/api/v2/settings: the library's own settings, read and changed from the app with the library's password,
/// so nothing needs the web page.</summary>
public class LibrarySettingsApiTests
{
    static readonly TailscaleInfo Tailnet = new(true, true, "Running", "mini.tail.ts.net", ["100.64.0.9"]);

    static (Config Cfg, Store Store) Library(TempDir dir)
    {
        var cfg = new Config(dir["home"], dir["pool"])
        {
            PoolName = "Sam's library", PoolPassword = "pw", OllamaEnabled = false,
            Classes = [new ClassDef("CS 101", ["cs101"], "Recursion and the call stack"), new ClassDef("BIO 110", [])],
        };
        Directory.CreateDirectory(cfg.Home);
        Configs.Save(cfg);
        return (cfg, new Store(cfg.DbPath, cfg.PoolDir));
    }

    static LibraryWebOptions Options(LoginSwitch? login = null, Func<Release, string, Task>? apply = null, Release? latest = null) => new()
    {
        ListModels = _ => Task.FromResult<List<(string, double)>?>([("qwen3:1.7b", 1.4), ("gemma4:e4b", 9.6)]),
        Tailscale = () => Tailnet,
        Latest = _ => Task.FromResult(latest),
        Apply = apply,
        RamGb = () => 16,
        HostName = () => "mac-mini",
        StartAtLogin = login,
    };

    static Task<TestSite> Site(Config cfg, Store store, LibraryWebOptions? options = null) =>
        TestSite.StartAsync(b => LibraryWeb.Build(b, cfg, store, new Pipeline(cfg, store, log: _ => { }), options ?? Options()));

    static HttpRequestMessage Req(HttpMethod m, string path, object? body = null, string key = "pw")
    {
        var r = new HttpRequestMessage(m, path);
        r.Headers.Authorization = new("Bearer", key);
        if (body is not null) r.Content = JsonContent.Create(body);
        return r;
    }

    static async Task<JsonObject> Json(HttpResponseMessage r)
    {
        Assert.True(r.IsSuccessStatusCode, $"{(int)r.StatusCode}: {await r.Content.ReadAsStringAsync()}");
        return (JsonObject)JsonNode.Parse(await r.Content.ReadAsStringAsync())!;
    }

    static async Task<string> Refused(HttpResponseMessage r, HttpStatusCode status)
    {
        Assert.Equal(status, r.StatusCode);
        return (JsonNode.Parse(await r.Content.ReadAsStringAsync()) as JsonObject)?["detail"]?.GetValue<string>() ?? "";
    }

    [Fact]
    public async Task The_app_reads_everything_the_settings_page_shows()
    {
        using var dir = new TempDir();
        var (cfg, store) = Library(dir);
        using var _s = store;
        await using var site = await Site(cfg, store);

        var s = await Json(await site.Client.SendAsync(Req(HttpMethod.Get, "/api/v2/settings")));

        Assert.Equal("Sam's library", s["name"]!.GetValue<string>());
        Assert.True(s["has_password"]!.GetValue<bool>());
        Assert.Equal(cfg.PoolDir, s["notes_folder"]!.GetValue<string>());
        var cs = s["classes"]![0]!;
        Assert.Equal("CS 101", cs["name"]!.GetValue<string>());
        Assert.Equal("cs101", cs["aliases"]![0]!.GetValue<string>());
        Assert.Equal("Recursion and the call stack", cs["description"]!.GetValue<string>());
        Assert.Equal(store.ClassDir("CS 101"), cs["folder"]!.GetValue<string>());
        Assert.False(s["notes"]!["sort"]!.GetValue<bool>());
        Assert.True(s["notes"]!["write"]!.GetValue<bool>());
        Assert.True(s["ollama"]!["answering"]!.GetValue<bool>());
        Assert.Equal("gemma4:e4b", s["ollama"]!["models"]![1]!["name"]!.GetValue<string>());
        Assert.Equal(["http://mini.tail.ts.net:8787", "http://100.64.0.9:8787", "http://mac-mini:8787"],
            s["reach"]!["addresses"]!.AsArray().Select(a => a!.GetValue<string>()));
        Assert.Null(s["start_at_login"]); // `serve` alone has no login item to offer
        Assert.Equal(Engine.Version, s["updates"]!["version"]!.GetValue<string>());
        Assert.False(s["updates"]!["newer"]!.GetValue<bool>());
    }

    [Fact]
    public async Task A_library_on_its_own_computer_alone_says_laptops_cant_reach_it_yet()
    {
        using var dir = new TempDir();
        var (cfg, store) = Library(dir);
        using var _s = store;
        Assert.Equal("0.0.0.0", cfg.WebHost);
        await using (var site = await Site(cfg, store))
            Assert.True((await Json(await site.Client.SendAsync(Req(HttpMethod.Get, "/api/v2/settings"))))["reach"]!["laptops"]!.GetValue<bool>());

        cfg.WebHost = "127.0.0.1";
        await using (var site = await Site(cfg, store))
            Assert.False((await Json(await site.Client.SendAsync(Req(HttpMethod.Get, "/api/v2/settings"))))["reach"]!["laptops"]!.GetValue<bool>());
    }

    [Fact]
    public async Task Only_the_library_password_opens_its_settings()
    {
        using var dir = new TempDir();
        var (cfg, store) = Library(dir);
        using var _s = store;
        await using var site = await Site(cfg, store);

        await Refused(await site.Client.SendAsync(Req(HttpMethod.Get, "/api/v2/settings", key: "nope")), HttpStatusCode.Unauthorized);
        await Refused(await site.Client.SendAsync(Req(HttpMethod.Post, "/api/v2/settings", new { name = "Mine now" }, key: "nope")), HttpStatusCode.Unauthorized);
        Assert.Equal("Sam's library", Configs.Load(cfg.Home).PoolName);
    }

    [Fact]
    public async Task What_the_app_changes_is_saved_in_the_librarys_config()
    {
        using var dir = new TempDir();
        var (cfg, store) = Library(dir);
        using var _s = store;
        await using var site = await Site(cfg, store);

        var s = await Json(await site.Client.SendAsync(Req(HttpMethod.Post, "/api/v2/settings", new
        {
            name = "Lecture notes",
            classes = new object[]
            {
                new { name = "CS 101", aliases = new[] { "cs101", "intro to cs" }, description = "Recursion" },
                new { name = "HIST 210", aliases = Array.Empty<string>(), description = "" },
            },
            notes = new { write = false, sort = true, min_confidence = 0.75 },
            ollama = new { summary_model = "gemma4:e4b", sort_model = "qwen3:1.7b" },
            auto_update = false,
            sort_engine = "claude",
        })));

        Assert.Equal("Lecture notes", s["name"]!.GetValue<string>());
        Assert.Equal("claude", s["sorting"]!["engine"]!.GetValue<string>());
        Assert.Equal("claude", AiSettings.Load(cfg.Home).ByJob["sort"].Provider);
        var saved = Configs.Load(cfg.Home);
        Assert.Equal("Lecture notes", saved.PoolName);
        Assert.Equal(["CS 101", "HIST 210"], saved.Classes.Select(c => c.Name));
        Assert.Equal(["cs101", "intro to cs"], saved.Classes[0].Aliases);
        Assert.Equal("Recursion", saved.Classes[0].Description);
        Assert.False(saved.SummaryEnabled);
        Assert.True(saved.OllamaEnabled);
        Assert.Equal(0.75, saved.MinConfidence);
        Assert.Equal("gemma4:e4b", saved.SummaryModel);
        Assert.Equal("qwen3:1.7b", saved.OllamaModel);
        Assert.False(saved.AutoUpdate);
        Assert.Equal("pw", saved.PoolPassword); // untouched: only what was sent changes

        await Json(await site.Client.SendAsync(Req(HttpMethod.Post, "/api/v2/settings", new { sort_engine = "" })));
        Assert.False(AiSettings.Load(cfg.Home).ByJob.ContainsKey("sort")); // back to the library's main AI
        await Refused(await site.Client.SendAsync(Req(HttpMethod.Post, "/api/v2/settings", new { sort_engine = "hal9000" })), HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_refused_change_changes_nothing()
    {
        using var dir = new TempDir();
        var (cfg, store) = Library(dir);
        using var _s = store;
        await using var site = await Site(cfg, store);

        string why = await Refused(await site.Client.SendAsync(Req(HttpMethod.Post, "/api/v2/settings", new
        {
            name = "Renamed",
            classes = new object[] { new { name = "CS 101" }, new { name = "cs 101" } },
        })), HttpStatusCode.BadRequest);

        Assert.Contains("two classes called", why);
        var saved = Configs.Load(cfg.Home);
        Assert.Equal("Sam's library", saved.PoolName);
        Assert.Equal(["CS 101", "BIO 110"], saved.Classes.Select(c => c.Name));
        await Refused(await site.Client.SendAsync(Req(HttpMethod.Post, "/api/v2/settings", new { classes = new object[] { new { name = "Unsorted" } } })), HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_new_password_locks_out_the_old_one_and_the_librarys_own_computer_keeps_up()
    {
        using var dir = new TempDir();
        var (cfg, store) = Library(dir);
        using var _s = store;
        var here = Configs.LoadClient(cfg.Home);
        here.ServerUrl = "http://127.0.0.1:8787";
        here.PoolKey = "pw";
        Configs.SaveClient(here);
        await using var site = await Site(cfg, store);

        await Refused(await site.Client.SendAsync(Req(HttpMethod.Post, "/api/v2/settings/password", new { password = "ab" })), HttpStatusCode.BadRequest);
        await Json(await site.Client.SendAsync(Req(HttpMethod.Post, "/api/v2/settings/password", new { password = "correct horse" })));

        Assert.Equal("correct horse", Configs.Load(cfg.Home).PoolPassword);
        Assert.Equal("correct horse", Configs.LoadClient(cfg.Home).PoolKey);
        await Refused(await site.Client.SendAsync(Req(HttpMethod.Get, "/api/v2/settings")), HttpStatusCode.Unauthorized);
        await Json(await site.Client.SendAsync(Req(HttpMethod.Get, "/api/v2/settings", key: "correct horse")));
    }

    [Fact]
    public async Task Start_at_login_changes_only_where_the_app_runs_the_library()
    {
        using var dir = new TempDir();
        var (cfg, store) = Library(dir);
        using var _s = store;
        bool on = false;
        var calls = new List<bool>();
        await using (var site = await Site(cfg, store, Options(new LoginSwitch(() => on, v => { calls.Add(v); on = v; }))))
        {
            Assert.False((await Json(await site.Client.SendAsync(Req(HttpMethod.Get, "/api/v2/settings"))))["start_at_login"]!.GetValue<bool>());
            Assert.Empty(calls); // reading never changes it

            var s = await Json(await site.Client.SendAsync(Req(HttpMethod.Post, "/api/v2/settings", new { start_at_login = true })));

            Assert.Equal([true], calls);
            Assert.True(s["start_at_login"]!.GetValue<bool>());
        }
        await using (var alone = await Site(cfg, store))
        {
            string why = await Refused(await alone.Client.SendAsync(Req(HttpMethod.Post, "/api/v2/settings", new { start_at_login = true })), HttpStatusCode.Conflict);
            Assert.Contains("without the Study Stash app", why);
        }
    }

    [Fact]
    public async Task Folders_it_may_read_are_added_changed_and_removed()
    {
        using var dir = new TempDir();
        var (cfg, store) = Library(dir);
        using var _s = store;
        string school = Directory.CreateDirectory(Path.Combine(dir.Path, "School")).FullName;
        await using var site = await Site(cfg, store);

        string why = await Refused(await site.Client.SendAsync(Req(HttpMethod.Post, "/api/v2/settings", new { add_folder = Path.Combine(dir.Path, "Nowhere") })), HttpStatusCode.BadRequest);
        Assert.Contains("no folder there", why);

        var s = await Json(await site.Client.SendAsync(Req(HttpMethod.Post, "/api/v2/settings", new { add_folder = school })));
        Assert.Equal(school, s["folders"]![0]!["path"]!.GetValue<string>());
        Assert.Equal("School", s["folders"]![0]!["name"]!.GetValue<string>());

        await Json(await site.Client.SendAsync(Req(HttpMethod.Post, "/api/v2/settings", new { folders = new[] { new { path = school, ai = false, @private = true } } })));
        Assert.Equal(new ReadFolder(school, "School", Ai: false, Private: true), Folders.Load(cfg.Home).Single());

        await Json(await site.Client.SendAsync(Req(HttpMethod.Post, "/api/v2/settings", new { folders = Array.Empty<object>() })));
        Assert.Empty(Folders.Load(cfg.Home));
    }

    [Fact]
    public async Task Update_now_and_rewrite_every_summary_run_from_the_app()
    {
        using var dir = new TempDir();
        var (cfg, store) = Library(dir);
        using var _s = store;
        var applied = new TaskCompletionSource<string>();
        var newer = new Release("v9.0.0", [9, 0, 0], "https://example.com/r", "https://example.com/p");
        await using var site = await Site(cfg, store, Options(apply: (r, _) => { applied.TrySetResult(r.Tag); return Task.CompletedTask; }, latest: newer));

        var s = await Json(await site.Client.SendAsync(Req(HttpMethod.Get, "/api/v2/settings")));
        Assert.True(s["updates"]!["newer"]!.GetValue<bool>());
        Assert.Equal("v9.0.0", s["updates"]!["latest"]!.GetValue<string>());

        var u = await Json(await site.Client.SendAsync(Req(HttpMethod.Post, "/api/v2/settings/update", new { })));
        Assert.True(u["updating"]!.GetValue<bool>());
        Assert.Equal("v9.0.0", await applied.Task.WaitAsync(TimeSpan.FromSeconds(10)));

        var r = await Json(await site.Client.SendAsync(Req(HttpMethod.Post, "/api/v2/settings/rewrite-all", new { })));
        Assert.Equal(0, r["queued"]!.GetValue<int>());
    }

    [Fact]
    public async Task The_apps_client_reads_and_changes_them_with_the_password()
    {
        using var dir = new TempDir();
        var (cfg, store) = Library(dir);
        using var _s = store;
        await using var site = await Site(cfg, store);
        var lib = new RemoteLibrary("http://localhost", "pw", new HttpClient(site.App.GetTestServer().CreateHandler()));

        var read = await lib.SettingsAsync(HttpMethod.Get);
        var changed = await lib.SettingsAsync(HttpMethod.Post, "", new JsonObject { ["notes"] = new JsonObject { ["write"] = false } });

        Assert.Equal("Sam's library", read!["name"]!.GetValue<string>());
        Assert.False(changed!["notes"]!["write"]!.GetValue<bool>());
        Assert.False(Configs.Load(cfg.Home).SummaryEnabled);
        var wrong = new RemoteLibrary("http://localhost", "nope", new HttpClient(site.App.GetTestServer().CreateHandler()));
        Assert.Equal(401, (await Assert.ThrowsAsync<LibraryRefusedException>(() => wrong.SettingsAsync(HttpMethod.Get))).Status);
    }
}
