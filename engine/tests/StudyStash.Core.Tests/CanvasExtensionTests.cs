using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
using StudyStash.Core.Canvas;
using StudyStash.Library;

namespace StudyStash.Core.Tests;

/// <summary>The extension and the library together: its folder kept up to date, work for every version, updates that
/// lose nothing, big files through the results door, and what an AI's page reads come back as.</summary>
public class CanvasExtensionTests
{
    const string Password = "maple otter";

    static string Manifest(string dir) => File.ReadAllText(Path.Combine(dir, "manifest.json"));

    static string Embedded(string name)
    {
        using var s = typeof(Extension).Assembly.GetManifestResourceStream("extension/" + name)!;
        return new StreamReader(s).ReadToEnd();
    }

    [Fact]
    public void Refresh_keeps_the_key_and_hosts_and_takes_the_new_version()
    {
        using var dir = new TempDir();
        string ext = dir["chrome-extension"];
        Extension.Prepare(ext, "http://mini.tail.ts.net:8787", "k3y", "https://canvas.test/");
        string[] hosts = ["https://canvas.test/*", "https://*.inscloudgate.net/*", "http://mini.tail.ts.net:8787/*"];

        // As an older Study Stash left it: version 1.2, its own scripts, a config.js from before the list of file hosts.
        var manifest = JsonNode.Parse(Manifest(ext))!.AsObject();
        manifest["version"] = "1.2";
        File.WriteAllText(Path.Combine(ext, "manifest.json"), manifest.ToJsonString());
        File.WriteAllText(Path.Combine(ext, "background.js"), "// the 1.2 service worker\n");
        File.Delete(Path.Combine(ext, "popup.js"));
        const string oldConfig = "const STUDY_STASH = {\"app\":\"http://mini.tail.ts.net:8787\",\"key\":\"k3y\",\"canvas\":\"https://canvas.test\"};\n";
        File.WriteAllText(Path.Combine(ext, "config.js"), oldConfig);

        File.Delete(Path.Combine(ext, "config.json"));

        Assert.True(Extension.Refresh(ext));
        manifest = JsonNode.Parse(Manifest(ext))!.AsObject();
        Assert.Equal(Extension.Version(), manifest["version"]!.GetValue<string>());
        Assert.Equal(hosts, manifest["host_permissions"]!.AsArray().Select(h => h!.GetValue<string>()));
        Assert.Equal(["alarms", "storage"], manifest["permissions"]!.AsArray().Select(p => p!.GetValue<string>()));
        // The key, the library's address and the Canvas stay; config.json joins config.js.
        Assert.Equal(new ExtensionConnection("http://mini.tail.ts.net:8787", "k3y", "https://canvas.test"), Extension.Connection(ext));
        Assert.Contains("\"key\":\"k3y\"", File.ReadAllText(Path.Combine(ext, "config.js")));
        Assert.Equal(Embedded("background.js"), File.ReadAllText(Path.Combine(ext, "background.js")));
        Assert.Equal(Embedded("popup.js"), File.ReadAllText(Path.Combine(ext, "popup.js")));
        Assert.Equal(Embedded("connection.js"), File.ReadAllText(Path.Combine(ext, "connection.js"))); // new in 1.4

        // Up to date already: nothing changes, nothing is rewritten.
        var written = Directory.GetFiles(ext).ToDictionary(f => f, File.GetLastWriteTimeUtc);
        Thread.Sleep(20);
        Assert.False(Extension.Refresh(ext));
        Assert.Equal(written, Directory.GetFiles(ext).ToDictionary(f => f, File.GetLastWriteTimeUtc));

        // A folder that isn't one Prepare made is left alone.
        Assert.False(Extension.Refresh(dir["nowhere"]));
        Directory.CreateDirectory(dir["other"]);
        File.WriteAllText(Path.Combine(dir["other"], "manifest.json"), "{\"version\":\"0.1\"}");
        Assert.False(Extension.Refresh(dir["other"]));
        Assert.Equal(["manifest.json"], Directory.GetFiles(dir["other"]).Select(Path.GetFileName));
    }

    static Dictionary<string, DateTime> Written(string dir) => Directory.GetFiles(dir).ToDictionary(f => f, File.GetLastWriteTimeUtc);

    static List<string> Hosts(string dir) =>
        JsonNode.Parse(Manifest(dir))!["host_permissions"]!.AsArray().Select(h => h!.GetValue<string>()).ToList();

    static JsonObject ConfigJson(string dir) => JsonNode.Parse(File.ReadAllText(Path.Combine(dir, "config.json")))!.AsObject();

    [Fact]
    public void Ensure_makes_a_fresh_folder_chrome_can_load_and_only_its_owner_can_read_the_key()
    {
        using var dir = new TempDir();
        string ext = dir["chrome-extension"];
        Assert.False(Extension.Ready(ext));

        var made = Extension.Ensure(ext, "http://127.0.0.1:8787", "k3y", "https://canvas.test/");
        Assert.Equal(new EnsureResult(true, ext), made);
        Assert.True(Extension.Ready(ext));
        Assert.Equal(["https://canvas.test/*", "https://*.inscloudgate.net/*", "http://127.0.0.1:8787/*"], Hosts(ext));
        var config = ConfigJson(ext);
        Assert.Equal(("http://127.0.0.1:8787", "k3y", "https://canvas.test", Extension.Protocol),
            (config["app"]!.GetValue<string>(), config["key"]!.GetValue<string>(), config["canvas"]!.GetValue<string>(), config["protocol"]!.GetValue<int>()));
        Assert.Equal($"const STUDY_STASH = {config.ToJsonString()};\n", File.ReadAllText(Path.Combine(ext, "config.js")));
        Assert.Equal(Embedded("background.js"), File.ReadAllText(Path.Combine(ext, "background.js")));
        if (!OperatingSystem.IsWindows())
            foreach (string f in new[] { "config.json", "config.js" })
                Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(Path.Combine(ext, f)));
    }

    [Fact]
    public void Ensure_before_there_is_a_canvas_address_gives_no_canvas_permission()
    {
        using var dir = new TempDir();
        Extension.Ensure(dir.Path, "http://127.0.0.1:8787", "k3y", "");
        Assert.Equal(["https://*.inscloudgate.net/*", "http://127.0.0.1:8787/*"], Hosts(dir.Path)); // never a bare "/*"
        Assert.Equal("", ConfigJson(dir.Path)["canvas"]!.GetValue<string>());
    }

    [Fact]
    public void Ensure_follows_a_new_canvas_address_and_leaves_default_ports_out()
    {
        using var dir = new TempDir();
        Extension.Ensure(dir.Path, "https://mini.tail.ts.net", "k3y", "");
        Assert.True(Extension.Ensure(dir.Path, "https://mini.tail.ts.net", "k3y", "https://canvas.test").Changed);
        Assert.Equal(["https://canvas.test/*", "https://*.inscloudgate.net/*", "https://mini.tail.ts.net/*"], Hosts(dir.Path));
        Assert.True(Extension.Ensure(dir.Path, "https://mini.tail.ts.net", "k3y", "http://canvas2.test:8443").Changed);
        Assert.Equal("http://canvas2.test:8443/*", Hosts(dir.Path)[0]);
        Assert.Equal("http://canvas2.test:8443", ConfigJson(dir.Path)["canvas"]!.GetValue<string>());
        Assert.Contains("\"canvas\":\"http://canvas2.test:8443\"", File.ReadAllText(dir["config.js"]));
    }

    [Fact]
    public void Ensure_brings_a_1_3_folder_with_only_config_js_up_to_date()
    {
        using var dir = new TempDir();
        string ext = dir.Path;
        Extension.Ensure(ext, "http://127.0.0.1:8787", "k3y", "https://canvas.test");
        File.Delete(Path.Combine(ext, "config.json"));
        var manifest = JsonNode.Parse(Manifest(ext))!.AsObject();
        manifest["version"] = "1.3";
        File.WriteAllText(Path.Combine(ext, "manifest.json"), manifest.ToJsonString());
        Assert.False(Extension.Ready(ext));
        Assert.Equal(new ExtensionConnection("http://127.0.0.1:8787", "k3y", "https://canvas.test"), Extension.Connection(ext)); // read from config.js

        Assert.True(Extension.Ensure(ext, "http://127.0.0.1:8787", "k3y", "https://canvas.test").Changed);
        Assert.True(Extension.Ready(ext));
        Assert.Equal("k3y", ConfigJson(ext)["key"]!.GetValue<string>());
        Assert.Equal(Extension.Version(), JsonNode.Parse(Manifest(ext))!["version"]!.GetValue<string>());
    }

    [Fact]
    public void Ensure_with_nothing_new_writes_nothing()
    {
        using var dir = new TempDir();
        Extension.Ensure(dir.Path, "http://127.0.0.1:8787", "k3y", "https://canvas.test");
        var before = Written(dir.Path);
        Thread.Sleep(20);
        Assert.Equal(new EnsureResult(false, dir.Path), Extension.Ensure(dir.Path, "http://127.0.0.1:8787", "k3y", "https://canvas.test"));
        Assert.Equal(before, Written(dir.Path));
        // The key alone changing rewrites only the two config files.
        Thread.Sleep(20);
        Assert.True(Extension.Ensure(dir.Path, "http://127.0.0.1:8787", "n3w", "https://canvas.test").Changed);
        var after = Written(dir.Path);
        Assert.Equal(["config.js", "config.json"], after.Where(kv => kv.Value != before[kv.Key]).Select(kv => Path.GetFileName(kv.Key)).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void The_folder_lists_the_file_hosts_and_the_protocol_once()
    {
        using var dir = new TempDir();
        Extension.Prepare(dir.Path, "http://127.0.0.1:8787", "k3y", "https://canvas.test");
        string config = File.ReadAllText(dir["config.js"]);
        var cfg = JsonNode.Parse(config["const STUDY_STASH = ".Length..].TrimEnd().TrimEnd(';'))!;
        Assert.Equal(Extension.FileHosts, cfg["files"]!.AsArray().Select(h => h!.GetValue<string>()));
        Assert.Equal(Extension.Protocol, cfg["protocol"]!.GetValue<int>());
        var hosts = JsonNode.Parse(File.ReadAllText(dir["manifest.json"]))!["host_permissions"]!.AsArray().Select(h => h!.GetValue<string>()).ToList();
        Assert.All(Extension.FileHosts, h => Assert.Contains($"https://{h}/*", hosts));
    }

    [Theory]
    [InlineData("/api/v1/courses/4201/files", "https://canvas.test/api/v1/courses/4201/files")]
    [InlineData("https://canvas.test/courses/4201/pages/syllabus", "https://canvas.test/courses/4201/pages/syllabus")]
    [InlineData("https://cluster1.inscloudgate.net/files/9/notes.pdf?sig=abc", "https://cluster1.inscloudgate.net/files/9/notes.pdf?sig=abc")]
    [InlineData("https://evil.test/api/v1/courses", null)]
    [InlineData("https://canvas.test.evil.test/x", null)]
    [InlineData("http://cluster1.inscloudgate.net/files/9", null)]
    [InlineData("https://cluster1.inscloudgate.net.evil.test/files/9", null)]
    [InlineData("https://cluster1.inscloudgate.net@evil.test/files/9", null)]
    [InlineData("https://cluster1.inscloudgate.net:8443/files/9", null)]
    public void Only_canvas_and_its_file_store_can_be_read(string given, string? url)
    {
        using var dir = new TempDir();
        var sync = FakeCanvas.Library(dir, () => FakeCanvas.DesignNow);
        Assert.Equal(url, sync.CanvasUrl(given));
    }

    [Fact]
    public void An_updated_extension_loses_nothing()
    {
        using var dir = new TempDir();
        var sync = FakeCanvas.Library(dir, () => FakeCanvas.DesignNow);
        var canvas = FakeCanvas.Cs101();

        // Chrome runs 1.2: it takes the sync's first jobs, then reloads into the new folder before answering them.
        var taken = sync.Work(force: false, "1.2").Jobs;
        Assert.Equal(6, taken.Count);
        Assert.Equal((1, 6), sync.Crawl.Left); // the planner's one cross-class job is still waiting (Next takes 6 at a time)

        // The new copy starts with force: what the old one held comes back at once, not in ten minutes.
        var again = sync.Work(force: true, Extension.Version(), Extension.Protocol).Jobs;
        Assert.Equal(taken.Select(j => j.Url), again.Select(j => j.Url));
        sync.Results(again.Select(canvas.Answer).ToList());
        Assert.True(canvas.Run(sync, force: false));

        string root = FakeCanvas.CanvasRoot(dir);
        Assert.Equal(5, Assignments.Load(dir.Path).Count);
        Assert.True(File.Exists(Path.Combine(root, "assignments", "Lab 3- recursion traces", "spec.md")));
        Assert.Equal("%PDF-1.4 ps4 answers", File.ReadAllText(Path.Combine(root, "assignments", "Problem set 4", "submission", "ps4-answers.pdf")));
        Assert.True(File.Exists(Path.Combine(root, "modules.md")));
        Assert.True(File.Exists(Path.Combine(root, "announcements.md")));
        Assert.All(sync.Crawl.Sections["CS 101"].Values, state => Assert.Equal("ok", state));
        var s = CanvasSettings.Load(dir.Path);
        Assert.Equal("", s.Error);
        Assert.Equal(new ExtensionUpdate("1.2", Extension.Version(), FakeCanvas.DesignNow.ToString("o", System.Globalization.CultureInfo.InvariantCulture), false), s.ExtensionUpdate);
        Assert.Equal(Extension.Version(), s.ExtensionVersion);
        Assert.False(s.ExtensionOutdated);
    }

    /// <summary>A library with CS 101 linked to COMP 101 on Canvas, a password, and this sync.</summary>
    static (Config Cfg, Store Store, LibraryWebOptions Options) LibraryFor(TempDir dir, CanvasSync sync)
    {
        var cfg = new Config(dir.Path, dir["pool"]) { PoolPassword = Password, OllamaEnabled = false, Classes = [new ClassDef("CS 101", ["cs101"])] };
        var store = new Store(cfg.DbPath, cfg.PoolDir);
        return (cfg, store, new LibraryWebOptions
        {
            Canvas = sync,
            ListModels = _ => Task.FromResult<List<(string, double)>?>(null),
            Tailscale = () => new TailscaleInfo(),
            Latest = _ => Task.FromResult<Release?>(null),
            RamGb = () => 16,
            HostName = () => "library-pc",
            Nonce = () => "NONCE",
        });
    }

    static async Task<JsonObject> JsonOf(HttpResponseMessage r)
    {
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        return JsonNode.Parse(await r.Content.ReadAsStringAsync())!.AsObject();
    }

    [Fact]
    public async Task An_older_extension_still_gets_work_and_the_update_is_noted_then_dismissed()
    {
        using var dir = new TempDir();
        var now = FakeCanvas.DesignNow;
        var sync = FakeCanvas.Library(dir, () => now);
        var (cfg, store, options) = LibraryFor(dir, sync);
        using var _ = store;
        await using var site = await TestSite.StartAsync(b => LibraryWeb.Build(b, cfg, store, new Pipeline(cfg, store, log: _ => { }), options));
        string key = CanvasSettings.ExtensionKey(cfg.Home);
        Task<HttpResponseMessage> AsExtension(string path)
        {
            var ask = new HttpRequestMessage(HttpMethod.Get, path);
            ask.Headers.Add("X-Study-Stash-Key", key);
            return site.Client.SendAsync(ask);
        }
        async Task<JsonObject> AsApp(HttpMethod method, string body = "")
        {
            var ask = new HttpRequestMessage(method, "/api/v2/canvas") { Headers = { Authorization = new AuthenticationHeaderValue("Bearer", Password) } };
            if (method == HttpMethod.Post) ask.Content = new StringContent(body, Encoding.UTF8, "application/json");
            return await JsonOf(await site.Client.SendAsync(ask));
        }

        // Version 1.2 speaks no protocol: it's given the sync's jobs like any other.
        var work = await JsonOf(await AsExtension("/api/v2/canvas/work?v=1.2"));
        Assert.Equal(6, work["jobs"]!.AsArray().Count); // Next()'s batch of 6; the planner's cross-class job trails behind
        Assert.Equal((Extension.Version(), Extension.Protocol), (work["ext"]!.GetValue<string>(), work["p"]!.GetValue<int>()));
        var state = await AsApp(HttpMethod.Get);
        Assert.Equal(("1.2", Extension.Version(), true), (state["extension_version"]!.GetValue<string>(), state["extension_latest"]!.GetValue<string>(), state["extension_outdated"]!.GetValue<bool>()));
        Assert.Null(state["extension_update"]);

        // It reloads into the new version: the update is noted once, with when.
        now = now.AddMinutes(1);
        await JsonOf(await AsExtension($"/api/v2/canvas/work?v={Extension.Version()}&p={Extension.Protocol}&force=1"));
        state = await AsApp(HttpMethod.Get);
        Assert.False(state["extension_outdated"]!.GetValue<bool>());
        var update = state["extension_update"]!.AsObject();
        Assert.Equal(("1.2", Extension.Version(), "2025-09-25T17:25:00.0000000+00:00"),
            (update["from"]!.GetValue<string>(), update["to"]!.GetValue<string>(), update["at"]!.GetValue<string>()));

        // Seen: dismissed, and the next visit of the same version doesn't bring it back.
        state = await AsApp(HttpMethod.Post, """{"dismiss_update":true}""");
        Assert.Null(state["extension_update"]);
        Assert.True(CanvasSettings.Load(cfg.Home).ExtensionUpdate!.Dismissed);
        await JsonOf(await AsExtension($"/api/v2/canvas/work?v={Extension.Version()}&p={Extension.Protocol}"));
        Assert.Null((await AsApp(HttpMethod.Get))["extension_update"]);

        // The door still wants the extension's key (or the library's password).
        Assert.Equal(HttpStatusCode.Unauthorized, (await site.Client.GetAsync("/api/v2/canvas/work?v=1.3&p=2")).StatusCode);
    }

    /// <summary>A fresh library, before Canvas and before any class: the setup's "Canvas before classes" step.</summary>
    static (Config Cfg, Store Store, LibraryWebOptions Options, CanvasSync Sync) FreshLibrary(TempDir dir)
    {
        var sync = new CanvasSync(dir.Path, c => FakeCanvas.ClassDir(dir, c), _ => { })
        {
            Clock = () => FakeCanvas.DesignNow, Zone = FakeCanvas.Zone, FirstContactWait = TimeSpan.FromMilliseconds(200),
        };
        var cfg = new Config(dir.Path, dir["pool"]) { PoolPassword = Password, OllamaEnabled = false, WebPort = 8790 };
        var store = new Store(cfg.DbPath, cfg.PoolDir);
        var (_, _, options) = LibraryFor(dir, sync);
        return (cfg, store, options, sync);
    }

    static List<string> HostsOf(string home) => Hosts(Extension.Folder(home));

    [Fact]
    public async Task The_library_makes_the_extension_folder_on_start_and_keeps_it_on_the_canvas_address()
    {
        using var dir = new TempDir();
        var (cfg, store, options, _) = FreshLibrary(dir);
        using var _1 = store;
        await using var site = await TestSite.StartAsync(b => LibraryWeb.Build(b, cfg, store, new Pipeline(cfg, store, log: _ => { }), options));

        // Ready before anyone asked: this library's address and the extension's own key, no Canvas yet.
        string folder = Extension.Folder(cfg.Home);
        Assert.True(Extension.Ready(folder));
        Assert.Equal(new ExtensionConnection("http://127.0.0.1:8790", CanvasSettings.ExtensionKey(cfg.Home), ""), Extension.Connection(folder));
        Assert.Equal(["https://*.inscloudgate.net/*", "http://127.0.0.1:8790/*"], HostsOf(cfg.Home));

        // The app sets the Canvas address: the folder follows.
        var ask = new HttpRequestMessage(HttpMethod.Post, "/api/v2/canvas")
        {
            Headers = { Authorization = new AuthenticationHeaderValue("Bearer", Password) },
            Content = new StringContent("""{"url":"canvas.test"}""", Encoding.UTF8, "application/json"),
        };
        var state = await JsonOf(await site.Client.SendAsync(ask));
        Assert.Equal("https://canvas.test/*", HostsOf(cfg.Home)[0]);
        Assert.True(state["extension"]!["folder_ready"]!.GetValue<bool>());

        // So does Settings' Save.
        await site.PostForm("/login", ("password", Password), ("next", "/"));
        var saved = await site.PostForm("/settings/canvas", ("canvas_url", "https://canvas2.test/courses/12"));
        Assert.Equal("/settings?canvas=saved#canvas", saved.Headers.Location!.OriginalString);
        Assert.Equal("https://canvas2.test/*", HostsOf(cfg.Home)[0]);
        Assert.Equal("https://canvas2.test", Extension.Connection(folder)!.Canvas);

        // No "Make it" any more: the page shows the ready folder.
        string page = await site.Text("/settings");
        Assert.DoesNotContain("Make it", page);
        Assert.Contains($"The extension's folder is ready: <code>{Ui.Esc(folder)}</code>", page);
        // An old page's "Make it" just goes back to Settings.
        Assert.Equal("/settings#canvas", (await site.PostForm("/settings/canvas/extension")).Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task Find_my_courses_saves_the_typed_address_first_and_works_before_any_class()
    {
        using var dir = new TempDir();
        var (cfg, store, options, sync) = FreshLibrary(dir);
        using var _1 = store;
        Assert.Empty(cfg.ClassNames());
        await using var site = await TestSite.StartAsync(b => LibraryWeb.Build(b, cfg, store, new Pipeline(cfg, store, log: _ => { }), options));
        await site.PostForm("/login", ("password", Password), ("next", "/"));
        var canvas = new FakeCanvas().Json("/api/v1/courses", """
            [{"id": 4201, "name": "CS 101 · Intro to Computer Science", "course_code": "CS 101", "term": {"name": "Fall 2025"}}]
            """);

        // Typed, never saved, then Find: Chrome (here, this test) is asked about the typed Canvas.
        var finding = site.PostForm("/settings/canvas/find", ("canvas_url", "canvas.test"));
        var waited = System.Diagnostics.Stopwatch.StartNew();
        while (!finding.IsCompleted && waited.Elapsed < TimeSpan.FromSeconds(10))
        {
            if (sync.Work(force: false, "1.3", 2).Jobs is { Count: > 0 } jobs) sync.Results(jobs.Select(canvas.Answer).ToList());
            await Task.Delay(5);
        }
        var found = await finding;
        Assert.Equal("/settings?canvas=found#canvas", found.Headers.Location!.OriginalString);
        var s = CanvasSettings.Load(cfg.Home);
        Assert.Equal("https://canvas.test", s.Url);
        Assert.Equal("CS 101 · Intro to Computer Science", s.Available["4201"]);
        Assert.Equal(["https://canvas.test/*"], HostsOf(cfg.Home).Take(1));
        Assert.Contains(FindOutcome.Say(FindOutcome.Found), await site.Text("/settings?canvas=found"));
    }

    [Fact]
    public async Task Find_my_courses_says_what_really_went_wrong()
    {
        using var dir = new TempDir();
        var (cfg, store, options, sync) = FreshLibrary(dir);
        using var _1 = store;
        await using var site = await TestSite.StartAsync(b => LibraryWeb.Build(b, cfg, store, new Pipeline(cfg, store, log: _ => { }), options));
        await site.PostForm("/login", ("password", Password), ("next", "/"));
        async Task<string> Find(string typed) =>
            (await site.PostForm("/settings/canvas/find", ("canvas_url", typed))).Headers.Location!.OriginalString;

        Assert.Equal("/settings?canvas=noaddress#canvas", await Find(""));
        Assert.Equal("/settings?canvas=noaddress#canvas", await Find("not an address"));
        // No extension has ever checked in: said after a short wait, not two minutes.
        var sw = System.Diagnostics.Stopwatch.StartNew();
        Assert.Equal("/settings?canvas=noextension#canvas", await Find("canvas.test"));
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(10));
        Assert.Contains(Ui.Esc(FindOutcome.Say(FindOutcome.NoExtension)), await site.Text("/settings?canvas=noextension"));

        // Chrome answers, signed out.
        var finding = site.PostForm("/settings/canvas/find", ("canvas_url", "canvas.test"));
        while (!finding.IsCompleted)
        {
            if (sync.Work(force: false, "1.3", 2).Jobs is { Count: > 0 } jobs)
                sync.Results(jobs.Select(j => new CanvasResult(j.Id, 401, "", """{"status":"unauthenticated","errors":[{"message":"user authorization required"}]}""", "", "", j.Url)).ToList());
            await Task.Delay(5);
        }
        Assert.Equal("/settings?canvas=signedout#canvas", (await finding).Headers.Location!.OriginalString);

        // Chrome answers with something else: Canvas's own words, escaped.
        finding = site.PostForm("/settings/canvas/find", ("canvas_url", "canvas.test"));
        while (!finding.IsCompleted)
        {
            if (sync.Work(force: false, "1.3", 2).Jobs is { Count: > 0 } jobs)
                sync.Results(jobs.Select(j => new CanvasResult(j.Id, 0, "", "", "", "TypeError: <Failed> to fetch", j.Url)).ToList());
            await Task.Delay(5);
        }
        Assert.Equal("/settings?canvas=other#canvas", (await finding).Headers.Location!.OriginalString);
        Assert.Contains(Ui.Esc(FindOutcome.Say(FindOutcome.Other, "TypeError: <Failed> to fetch")), await site.Text("/settings?canvas=other"));
        Assert.DoesNotContain("<Failed>", await site.Text("/settings?canvas=other"));
    }

    [Fact]
    public async Task The_results_door_takes_a_40_MB_file()
    {
        using var dir = new TempDir();
        var sync = FakeCanvas.Library(dir, () => FakeCanvas.DesignNow);
        var slides = new byte[40_000_000];
        new Random(42).NextBytes(slides);
        var meta = JsonNode.Parse(FakeCanvas.Fixture("cs101-file-555.json"))!.AsObject();
        meta["size"] = slides.Length;
        var canvas = FakeCanvas.Cs101().Json("/api/v1/courses/4201/files/555", meta.ToJsonString()).Bytes("/files/555/download", slides);
        var (cfg, store, options) = LibraryFor(dir, sync);
        using var _ = store;

        // Kestrel itself, on a spare port: an in-memory test server doesn't hold requests to Kestrel's size limit.
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(k => k.Listen(IPAddress.Loopback, 0));
        await using var app = LibraryWeb.Build(builder, cfg, store, new Pipeline(cfg, store, log: _ => { }), options);
        await app.StartAsync();
        using var http = new HttpClient { BaseAddress = new Uri(app.Urls.First()), Timeout = TimeSpan.FromMinutes(2) };
        http.DefaultRequestHeaders.Add("X-Study-Stash-Key", CanvasSettings.ExtensionKey(cfg.Home));

        // Be the extension (protocol 2): the answers together, each file on its own.
        long biggest = 0;
        async Task Post(IEnumerable<CanvasResult> results)
        {
            var body = new JsonObject
            {
                ["results"] = new JsonArray(results.Select(r => (JsonNode)new JsonObject
                {
                    ["id"] = r.Id, ["status"] = r.Status, ["link"] = r.Link, ["type"] = r.Type, ["final"] = r.Final,
                    [r.B64.Length > 0 ? "b64" : "text"] = r.B64.Length > 0 ? r.B64 : r.Text,
                }).ToArray()),
            }.ToJsonString();
            using var content = new StringContent(body, Encoding.UTF8, "application/json");
            biggest = Math.Max(biggest, Encoding.UTF8.GetByteCount(body));
            var answer = await http.PostAsync("/api/v2/canvas/results", content);
            Assert.Equal(HttpStatusCode.OK, answer.StatusCode);
        }
        for (int round = 0; round < 100; round++)
        {
            var work = await JsonOf(await http.GetAsync($"/api/v2/canvas/work?v={Extension.Version()}&p={Extension.Protocol}" + (round == 0 ? "&force=1" : "")));
            var jobs = work["jobs"]!.AsArray().Select(j => new CanvasJob(j!["id"]!.GetValue<string>(), j["url"]!.GetValue<string>(), j["kind"]!.GetValue<string>())).ToList();
            if (jobs.Count == 0) break;
            var answers = jobs.Select(j => (j.Kind, Result: canvas.Answer(j))).ToList();
            if (answers.Where(a => a.Kind != "bytes").Select(a => a.Result).ToList() is { Count: > 0 } rest) await Post(rest);
            foreach (var (_, file) in answers.Where(a => a.Kind == "bytes")) await Post([file]);
        }

        Assert.False(sync.Crawl.Active);
        Assert.True(biggest > 30_000_000, $"the biggest post was only {biggest} bytes"); // past Kestrel's default limit
        string saved = Path.Combine(FakeCanvas.CanvasRoot(dir), "modules", "04 Week 4 · Recursion", "recursion-slides.pdf");
        Assert.True(File.ReadAllBytes(saved).AsSpan().SequenceEqual(slides));
        Assert.Equal("", CanvasSettings.Load(cfg.Home).Error);
        await app.StopAsync();
    }

    [Fact]
    public async Task An_ai_reading_a_page_gets_markdown_only_for_html()
    {
        using var dir = new TempDir();
        var sync = FakeCanvas.Library(dir, () => FakeCanvas.DesignNow);
        var canvas = new FakeCanvas()
            .On("/courses/4201/pages/office-hours", j => new CanvasResult(j.Id, 200, "", "<h2>Office hours</h2><p>Thursdays, <strong>2 to 4</strong>.</p>", "", "", j.Url)
            {
                Type = "text/html; charset=utf-8",
            })
            .Status("/courses/4201/files/9/grades.csv", 200, "name,score\nLab 3,20\n");
        async Task<string> Read(string path)
        {
            var reading = sync.FetchAsync(path, "text");
            sync.Results(sync.Work(force: false).Jobs.Select(canvas.Answer).ToList());
            return (await reading)["markdown"]!.GetValue<string>();
        }

        string page = await Read("/courses/4201/pages/office-hours");
        Assert.Contains("## Office hours", page);
        Assert.Contains("**2 to 4**", page);
        Assert.Equal("name,score\nLab 3,20\n", await Read("/courses/4201/files/9/grades.csv")); // as Canvas sent it
    }
}
