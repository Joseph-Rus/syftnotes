using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Logging;
using StudyStash.Core.Canvas;
using StudyStash.Core.Tests.E2E;
using StudyStash.Library;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace StudyStash.Core.Tests;

/// <summary>The end-to-end tests share one Chrome, one pretend Canvas and one library: never alongside other tests
/// (ports, one Chrome), and in the order they're written.</summary>
[CollectionDefinition("ExtensionE2E", DisableParallelization = true)]
public sealed class ExtensionE2ECollection : ICollectionFixture<ExtensionRig>;

/// <summary>Runs a class's tests by name, so a story told in steps (S1_…, S2_…) runs in its order.</summary>
public sealed class ByNameOrderer : ITestCaseOrderer
{
    public IEnumerable<TTestCase> OrderTestCases<TTestCase>(IEnumerable<TTestCase> testCases) where TTestCase : ITestCase =>
        testCases.OrderBy(t => t.TestMethod.Method.Name, StringComparer.Ordinal);
}

/// <summary>
/// The whole Canvas path in a real Chrome: a pretend Canvas over HTTP (<see cref="CanvasServer"/>), a library on real
/// Kestrel with a throwaway home and classes CS 101 and BIO 110, and Chrome for Testing with the extension folder the
/// library made by itself. Skipped unless STUDYSTASH_E2E_CHROME is set (engine/tests/extension-e2e.sh).
/// </summary>
[Collection("ExtensionE2E")]
[TestCaseOrderer("StudyStash.Core.Tests.ByNameOrderer", "StudyStash.Core.Tests")]
public sealed class ExtensionE2ETests(ExtensionRig rig, ITestOutputHelper output)
{
    void Note(string what) => output.WriteLine($"[e2e] {what}");

    [ChromeFact]
    public async Task S1_the_extension_checks_in()
    {
        var sw = Stopwatch.StartNew();
        var seen = await rig.UntilAsync(async () =>
        {
            var c = await rig.GetAsync("/api/v2/canvas");
            return ExtensionRig.S(c["extension_seen"]).Length > 0 ? c : null;
        }, TimeSpan.FromSeconds(70), "the extension to check in");
        Note($"checked in {rig.SinceChrome.TotalSeconds:0.0} s after Chrome started (waited {sw.Elapsed.TotalSeconds:0.0} s here), version {seen["extension_version"]}");
        Assert.Equal(Extension.Version(), ExtensionRig.S(seen["extension_version"]));
        Assert.False(seen["extension_outdated"]?.GetValue<bool>() ?? false);
        var about = await rig.GetAsync("/api/v2/canvas/extension");
        Assert.True(about["folder_ready"]!.GetValue<bool>());
        Assert.True(about["connected"]!.GetValue<bool>());
        Assert.Equal(Extension.Protocol, about["seen_protocol"]!.GetValue<int>()); // it waits for work with the library
    }

    [ChromeFact]
    public async Task S2_find_courses_lists_the_courses()
    {
        var sw = Stopwatch.StartNew();
        var found = await rig.PostAsync("/api/v2/canvas/courses", "{}");
        Note($"Find courses answered in {sw.Elapsed.TotalSeconds:0.0} s, error '{ExtensionRig.S(found["error"])}'");
        Assert.Equal("", ExtensionRig.S(found["error"]));
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(5), $"Find took {sw.Elapsed.TotalSeconds:0.0} s: the extension should be waiting for work");
        var available = found["available"]!.AsObject();
        Assert.Equal("CS 101 · Intro to Computer Science", ExtensionRig.S(available["4201"]));
        Assert.Equal("BIO 110 · Cells and Systems", ExtensionRig.S(available["4202"]));
        Assert.Equal("Fall 2025", ExtensionRig.S(found["course_info"]!["4201"]!["term"]));
        var asked = rig.Canvas.HitsTo("/api/v1/courses").ToList();
        Assert.NotEmpty(asked);
        Assert.All(asked, h => Assert.True(h.Cookie && h.SignedIn, "Chrome's Canvas session cookie didn't come with the extension's fetch"));
    }

    [ChromeFact]
    public async Task S3_a_linked_class_syncs_its_canvas_into_the_library()
    {
        var sw = Stopwatch.StartNew();
        await rig.PostAsync("/api/v2/canvas", """{"courses":{"CS 101":4201},"sync":true}""");
        await rig.UntilAsync(() => Task.FromResult(CanvasSettings.Load(rig.Home).LastDone.Length > 0 ? "" : null),
            TimeSpan.FromSeconds(90), "the sync to finish");
        var s = CanvasSettings.Load(rig.Home);
        Note($"sync finished in {sw.Elapsed.TotalSeconds:0.0} s; {rig.Canvas.Hits.Count} requests reached Canvas; error: '{s.Error}'");
        Assert.Equal("", s.Error);
        Assert.False(s.NeedsLogin);

        string root = Path.Combine(rig.ClassDir("CS 101"), "Canvas");
        Assert.Equal(5, Assignments.Load(rig.Home).Count(a => a.ClassName == "CS 101"));
        Assert.True(File.Exists(Path.Combine(root, "assignments", "Lab 3- recursion traces", "spec.md")));
        Assert.True(File.Exists(Path.Combine(root, "modules.md")));
        // A file's bytes came through Chrome as base64 and are Canvas's exactly.
        Assert.Equal("%PDF-1.4 ps4 answers", File.ReadAllText(Path.Combine(root, "assignments", "Problem set 4", "submission", "ps4-answers.pdf")));
        string slides = Directory.EnumerateFiles(root, "recursion-slides.pdf", SearchOption.AllDirectories).First();
        Assert.Equal("%PDF-1.4 recursion slides", File.ReadAllText(slides));
        Assert.All(rig.Canvas.Hits.Where(h => !h.PathAndQuery.StartsWith("/login", StringComparison.Ordinal)),
            h => Assert.True(h.SignedIn, $"{h.PathAndQuery} came without Chrome's Canvas session"));
    }

    [ChromeFact]
    public async Task S4_signed_out_of_canvas_says_so()
    {
        using (var http = new HttpClient())
            Assert.Equal(HttpStatusCode.NoContent, (await http.PostAsync(rig.Canvas.Url.Replace(CanvasServer.Host, "127.0.0.1", StringComparison.Ordinal) + "/_e2e/sign-out", null)).StatusCode);
        try
        {
            var sw = Stopwatch.StartNew();
            var found = await rig.PostAsync("/api/v2/canvas/courses", "{}");
            Note($"Find courses signed out answered in {sw.Elapsed.TotalSeconds:0.0} s: {found["error"]?.ToJsonString()}");
            Assert.Equal("Chrome isn't signed in to Canvas.", ExtensionRig.S(found["error"]));
            // And the library now knows: the app and Settings say "Sign in to Canvas".
            Assert.Equal("signed_out", ExtensionRig.S((await rig.GetAsync("/api/v2/canvas/state"))["state"]));
        }
        finally
        {
            rig.Canvas.SignIn(); // the student signs back in for the stories after this one
        }
        var again = await rig.PostAsync("/api/v2/canvas/courses", "{}");
        Assert.Equal("", ExtensionRig.S(again["error"]));
    }

    [ChromeFact]
    public async Task S5_a_newer_manifest_on_disk_reloads_the_extension()
    {
        // The update scheme: the library rewrites the folder, the running copy reads its manifest from disk, sees a
        // new version and reloads. Chrome has to read the unpacked folder live for that to work.
        string path = Path.Combine(rig.Folder, "manifest.json");
        var manifest = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        string next = Extension.Version() + ".1";
        manifest["version"] = next;
        File.WriteAllText(path, manifest.ToJsonString());
        var sw = Stopwatch.StartNew();
        await rig.UntilAsync(async () => ExtensionRig.S((await rig.GetAsync("/api/v2/canvas"))["extension_version"]) == next ? "" : null,
            TimeSpan.FromSeconds(90), $"the extension to reload as {next}");
        Note($"reloaded from its folder as {next} {sw.Elapsed.TotalSeconds:0.0} s after the manifest changed");
    }

    [ChromeFact]
    public async Task S6_a_new_canvas_address_reloads_the_extension_with_no_human_step()
    {
        // The school's Canvas moves: the student types the new address in Settings. The library rewrites the folder
        // (host permissions for the new Canvas), the waiting extension is told at once, reloads itself into the new
        // permissions, and Find works on the new Canvas.
        var sw = Stopwatch.StartNew();
        await rig.PostAsync("/api/v2/canvas", new JsonObject { ["url"] = rig.Canvas.OtherUrl }.ToJsonString());
        Assert.Contains(rig.Canvas.OtherUrl + "/*", File.ReadAllText(Path.Combine(rig.Folder, "manifest.json")), StringComparison.Ordinal);
        var found = await rig.PostAsync("/api/v2/canvas/courses", "{}");
        Note($"Find on {CanvasServer.OtherHost} answered {sw.Elapsed.TotalSeconds:0.0} s after the address changed, error '{ExtensionRig.S(found["error"])}'");
        Assert.Equal("", ExtensionRig.S(found["error"]));
        var asked = rig.Canvas.HitsTo("/api/v1/courses").Where(h => h.Host == CanvasServer.OtherHost).ToList();
        Assert.NotEmpty(asked);
        Assert.All(asked, h => Assert.True(h.Cookie && h.SignedIn, "the new Canvas's session cookie didn't come"));
        // It reloaded from the rewritten folder: the version is the engine's again (S5 left 1.x.1 running).
        await rig.UntilAsync(async () => ExtensionRig.S((await rig.GetAsync("/api/v2/canvas"))["extension_version"]) == Extension.Version() ? "" : null,
            TimeSpan.FromSeconds(30), "the extension to check in as " + Extension.Version());
    }

    [ChromeFact]
    public async Task S7_a_restarted_library_is_found_again_within_40_seconds()
    {
        await rig.StopLibraryAsync();
        var down = DateTimeOffset.UtcNow;
        await Task.Delay(TimeSpan.FromSeconds(2));
        await rig.StartLibraryAsync(); // same port, same key
        var sw = Stopwatch.StartNew();
        await rig.UntilAsync(async () =>
        {
            var seen = ExtensionRig.S((await rig.GetAsync("/api/v2/canvas/extension"))["seen"]);
            return DateTimeOffset.TryParse(seen, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var at) && at > down ? "" : null;
        }, TimeSpan.FromSeconds(40), "the extension to find the restarted library");
        Note($"checked in with the restarted library {sw.Elapsed.TotalSeconds:0.0} s after it came back");
        var found = await rig.PostAsync("/api/v2/canvas/courses", "{}");
        Assert.Equal("", ExtensionRig.S(found["error"]));
    }

    [ChromeFact]
    public async Task S8_the_extension_stays_awake_while_nothing_happens()
    {
        // Three quiet minutes: the extension keeps waiting with the library the whole time (a check-in about every
        // 20 seconds). A worker Chrome put to sleep would leave a gap until its 30-second alarm woke it.
        var idle = TimeSpan.FromMinutes(3);
        var sw = Stopwatch.StartNew();
        var changes = new List<TimeSpan>();
        string last = "";
        while (sw.Elapsed < idle)
        {
            string seen = ExtensionRig.S((await rig.GetAsync("/api/v2/canvas/extension"))["seen"]);
            if (seen != last)
            {
                changes.Add(sw.Elapsed);
                last = seen;
            }
            await Task.Delay(500);
        }
        var gaps = changes.Zip(changes.Skip(1), (a, b) => b - a).ToList();
        var longest = gaps.Count > 0 ? gaps.Max() : idle;
        Note($"idle {idle.TotalMinutes:0} min: {changes.Count} check-ins seen, longest gap {longest.TotalSeconds:0.0} s");
        Assert.True(changes.Count >= 7, $"only {changes.Count} check-ins in {idle.TotalMinutes} minutes");
        Assert.True(longest < TimeSpan.FromSeconds(28), $"a {longest.TotalSeconds:0} s gap: the worker slept");
        sw.Restart();
        var found = await rig.PostAsync("/api/v2/canvas/courses", "{}");
        Note($"Find after the quiet minutes answered in {sw.Elapsed.TotalSeconds:0.0} s");
        Assert.Equal("", ExtensionRig.S(found["error"]));
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(5), $"Find took {sw.Elapsed.TotalSeconds:0.0} s");
    }

    [ChromeFact]
    public async Task S9_a_1_3_folder_updates_itself_to_the_new_version()
    {
        // A student's Chrome still runs the extension as Study Stash 0.5.0 left it (1.3: config.js, no config.json,
        // asks once a minute). The library brings the folder up to date; the running 1.3 sees the new version on
        // disk and reloads into it by itself.
        var kept = Extension.Connection(rig.Folder)!;
        rig.StopChrome();
        string old = Path.Combine(AppContext.BaseDirectory, "Fixtures", "extension-1.3");
        foreach (string f in new[] { "config.json", "connection.js" }) File.Delete(Path.Combine(rig.Folder, f));
        foreach (string f in Directory.GetFiles(old)) File.Copy(f, Path.Combine(rig.Folder, Path.GetFileName(f)), overwrite: true);
        var manifest = JsonNode.Parse(File.ReadAllText(Path.Combine(rig.Folder, "manifest.json")))!.AsObject();
        manifest["host_permissions"] = new JsonArray(kept.Canvas + "/*", "https://*.inscloudgate.net/*", kept.App + "/*");
        File.WriteAllText(Path.Combine(rig.Folder, "manifest.json"), manifest.ToJsonString());
        File.WriteAllText(Path.Combine(rig.Folder, "config.js"), "const STUDY_STASH = " + new JsonObject
        {
            ["app"] = kept.App, ["key"] = kept.Key, ["canvas"] = kept.Canvas, ["files"] = new JsonArray("*.inscloudgate.net"), ["protocol"] = 2,
        }.ToJsonString() + ";\n");
        rig.StartChrome("chrome-profile-1.3");

        await rig.UntilAsync(async () => ExtensionRig.S((await rig.GetAsync("/api/v2/canvas/extension"))["seen_version"]) == "1.3" ? "" : null,
            TimeSpan.FromSeconds(70), "the 1.3 extension to check in");
        Note($"1.3 checked in {rig.SinceChrome.TotalSeconds:0.0} s after Chrome started");

        Assert.True(Extension.Refresh(rig.Folder)); // what the library does on start after an update
        Assert.True(File.Exists(Path.Combine(rig.Folder, "config.json")));
        var sw = Stopwatch.StartNew();
        var about = await rig.UntilAsync(async () =>
        {
            var e = await rig.GetAsync("/api/v2/canvas/extension");
            return ExtensionRig.S(e["seen_version"]) == Extension.Version() ? e : null;
        }, TimeSpan.FromSeconds(100), "the 1.3 extension to reload itself as " + Extension.Version());
        Note($"1.3 reloaded itself as {Extension.Version()} {sw.Elapsed.TotalSeconds:0.0} s after the folder was updated");
        Assert.Equal(Extension.Protocol, about["seen_protocol"]!.GetValue<int>());
        var found = await rig.PostAsync("/api/v2/canvas/courses", "{}");
        Assert.Equal("", ExtensionRig.S(found["error"]));
    }

    [ChromeFact]
    public async Task S9b_the_chrome_web_store_build_loads_and_waits_for_its_code()
    {
        // The zip for the Chrome Web Store, unzipped the way Chrome installs it: no folder for Study Stash to write,
        // no sites until the student pastes the code and allows them.
        rig.StopChrome();
        string zip = rig.Scratch("store.zip"), store = rig.Scratch("store-extension");
        Extension.PackForStore(zip);
        System.IO.Compression.ZipFile.ExtractToDirectory(zip, store);
        rig.StartChrome("chrome-profile-store", store);
        int ApiHits() => rig.Canvas.Hits.Count(h => h.PathAndQuery.StartsWith("/api/", StringComparison.Ordinal));
        int before = ApiHits();

        var status = await rig.UntilAsync(async () =>
        {
            string s = await rig.EvaluateAsync("chrome.storage.local.get('status').then(s => s.status ? s.status.state : '')");
            return s is "\"no_config\"" ? s : null;
        }, TimeSpan.FromSeconds(30), "the store copy to say it has no connection");
        Note($"store copy registered and said no_config {rig.SinceChrome.TotalSeconds:0.0} s after Chrome started");
        Assert.Equal("[]", await rig.EvaluateAsync("chrome.permissions.getAll().then(p => p.origins)"));
        Assert.Equal("\"!\"", await rig.EvaluateAsync("chrome.action.getBadgeText({})"));

        // Its popup asks for the code.
        // Its popup asks for the code (once it has looked for a connection).
        Assert.Equal("false", await rig.EvaluateAsync(
            "new Promise(r => setTimeout(() => r(document.getElementById('connect').hidden), 500))", "popup.html"));

        // The library's code, pasted: the connection is kept, but without Chrome's say-so (a click on Allow, which
        // headless Chrome can't give) it reaches neither Canvas nor the library.
        string code = ExtensionRig.S((await rig.GetAsync("/api/v2/canvas/extension"))["connection_code"]);
        Assert.NotEqual("", code);
        await rig.EvaluateAsync($"connectWithCode({System.Text.Json.JsonSerializer.Serialize(code)}).catch(e => 'asked')", "popup.html");
        await rig.UntilAsync(async () =>
            await rig.EvaluateAsync("pump(true).then(() => chrome.storage.local.get('status')).then(s => s.status.state)") is "\"no_access\"" ? "" : null,
            TimeSpan.FromSeconds(30), "the store copy to wait for Chrome's permission");
        string kept = await rig.EvaluateAsync("chrome.storage.local.get('connection').then(c => c.connection.app + ' ' + c.connection.canvas)");
        var pasted = Extension.ReadConnectionCode(code)!;
        Assert.Equal((Extension.Connection(Extension.Folder(rig.Home))!.App, CanvasSettings.Load(rig.Home).Url), (pasted.App, pasted.Canvas));
        Assert.Equal(System.Text.Json.JsonSerializer.Serialize($"{pasted.App} {pasted.Canvas}"), kept);
        Assert.Equal(before, ApiHits()); // nothing asked the pretend Canvas
    }
}

/// <summary>One pretend Canvas, one library and one Chrome for all the end-to-end tests, started once (and only
/// when there's a Chrome to run).</summary>
public sealed class ExtensionRig : IAsyncLifetime
{
    const string Password = "e2e-library-password";

    readonly TempDir dir = new();
    readonly Stopwatch sinceChrome = new();
    Store? store;
    Config? cfg;
    LibraryWebOptions? options;
    IPAddress host = IPAddress.Loopback;
    WebApplication? library;
    ChromeRunner? chrome;
    HttpClient? api;

    public CanvasServer Canvas { get; private set; } = null!;
    public string Home => dir.Path;
    /// <summary>The extension folder Chrome loaded: the library's own, or (with STUDYSTASH_E2E_LIBRARY_HOST) one
    /// pointing at the library's other address, the way a laptop's is.</summary>
    public string Folder { get; private set; } = "";
    public TimeSpan SinceChrome => sinceChrome.Elapsed;
    public string ClassDir(string cls) => store!.ClassDir(cls);

    public static string S(JsonNode? v) => v is JsonValue j && j.TryGetValue(out string? s) ? s : "";

    public async Task InitializeAsync()
    {
        if (ChromeRunner.Binary.Length == 0) return;
        Canvas = await CanvasServer.StartAsync(FakeCanvas.Cs101().Json("/api/v1/courses", """
            [{"id": 4201, "name": "CS 101 · Intro to Computer Science", "course_code": "CS 101", "term": {"name": "Fall 2025"}},
             {"id": 4202, "name": "BIO 110 · Cells and Systems", "course_code": "BIO 110", "term": {"name": "Fall 2025"}}]
            """));

        cfg = new Config(dir.Path, dir["pool"])
        {
            PoolPassword = Password, OllamaEnabled = false, WebPort = FreePort(),
            Classes = [new ClassDef("CS 101", ["cs101"]), new ClassDef("BIO 110", ["bio110"])],
        };
        store = new Store(cfg.DbPath, cfg.PoolDir);
        options = new LibraryWebOptions
        {
            ListModels = _ => Task.FromResult<List<(string, double)>?>(null), Tailscale = () => new TailscaleInfo(),
            Latest = _ => Task.FromResult<Release?>(null), RamGb = () => 16, HostName = () => "library-pc",
        };
        // The way `serve` runs it: Kestrel, HTTP/1, on this computer's loopback. STUDYSTASH_E2E_LIBRARY_HOST (an
        // address of this computer's, like its LAN one) puts it there instead, the way Chrome on a laptop reaches a
        // library on another computer.
        host = IPAddress.Parse(Environment.GetEnvironmentVariable("STUDYSTASH_E2E_LIBRARY_HOST") is { Length: > 0 } h ? h : "127.0.0.1");
        await StartLibraryAsync();
        string libraryUrl = $"http://{host}:{cfg.WebPort}";
        api = new HttpClient { BaseAddress = new Uri(libraryUrl), Timeout = TimeSpan.FromMinutes(3) };
        api.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Password);

        // No "Make it": the library made its folder on start, and typing the Canvas address points it there.
        Assert.True(Extension.Ready(Extension.Folder(Home)), "the library didn't make the extension's folder on start");
        await PostAsync("/api/v2/canvas", new JsonObject { ["url"] = Canvas.Url }.ToJsonString());
        Folder = Extension.Folder(Home);
        if (!IPAddress.IsLoopback(host))
        {
            // The library's own folder reaches it on 127.0.0.1, where this one isn't listening: be the laptop instead.
            Folder = dir["laptop-extension"];
            Extension.Ensure(Folder, libraryUrl, CanvasSettings.ExtensionKey(Home), CanvasSettings.Load(Home).Url);
        }
        Assert.Equal(Canvas.Url, Extension.Connection(Folder)!.Canvas);
        StartChrome("chrome-profile");
    }

    /// <summary>The library, the way `serve` builds it, on its port (again, after <see cref="StopLibraryAsync"/>).</summary>
    public async Task StartLibraryAsync()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(k => k.Listen(host, cfg!.WebPort, o => o.Protocols = HttpProtocols.Http1));
        library = LibraryWeb.Build(builder, cfg!, store!, new Pipeline(cfg!, store!, log: _ => { }), options!);
        await library.StartAsync();
    }

    public async Task StopLibraryAsync()
    {
        if (library is null) return;
        await library.StopAsync();
        await library.DisposeAsync();
        library = null;
    }

    /// <summary>Chrome with the extension folder (or <paramref name="folder"/>), signed in to the pretend Canvas under
    /// both its names, on a fresh profile.</summary>
    public void StartChrome(string profile, string? folder = null)
    {
        chrome = ChromeRunner.Start(folder ?? Folder, dir[profile], Canvas.Url + "/login/e2e", CanvasServer.Host, CanvasServer.OtherHost);
        sinceChrome.Restart();
    }

    /// <summary>Run JavaScript in the extension Chrome is running (<see cref="ChromeRunner.EvaluateAsync"/>).</summary>
    public Task<string> EvaluateAsync(string expression, string? page = null) => chrome!.EvaluateAsync(expression, page);

    /// <summary>A place for a test's own files, removed with the rest.</summary>
    public string Scratch(string name) => dir[name];

    public void StopChrome()
    {
        chrome?.Dispose();
        chrome = null;
    }

    static int FreePort()
    {
        using var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        return ((IPEndPoint)l.LocalEndpoint).Port;
    }

    public async Task<JsonObject> GetAsync(string path)
    {
        var r = await api!.GetAsync(path);
        string text = await r.Content.ReadAsStringAsync();
        Assert.True(r.IsSuccessStatusCode, $"GET {path}: {(int)r.StatusCode} {text}");
        return JsonNode.Parse(text)!.AsObject();
    }

    public async Task<JsonObject> PostAsync(string path, string json)
    {
        using var body = new StringContent(json, Encoding.UTF8, "application/json");
        var r = await api!.PostAsync(path, body);
        string text = await r.Content.ReadAsStringAsync();
        Assert.True(r.IsSuccessStatusCode, $"POST {path}: {(int)r.StatusCode} {text}");
        return JsonNode.Parse(text)!.AsObject();
    }

    /// <summary>Ask again every quarter second until <paramref name="check"/> gives something, or fail saying what
    /// never happened, with what Chrome and the pretend Canvas saw.</summary>
    public async Task<T> UntilAsync<T>(Func<Task<T?>> check, TimeSpan timeout, string what) where T : class
    {
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < timeout)
        {
            if (await check() is { } done) return done;
            if (chrome!.Exited) break;
            await Task.Delay(250);
        }
        throw new XunitException($"Waited {sw.Elapsed.TotalSeconds:0} s for {what}.\nCanvas saw:\n"
            + string.Join('\n', Canvas.Hits.TakeLast(15).Select(h => $"  {h.Method} {h.PathAndQuery} cookie={h.Cookie} signed_in={h.SignedIn} → {h.Status}"))
            + $"\nChrome{(chrome!.Exited ? " (exited)" : "")}:\n{chrome.Tail()}");
    }

    public async Task DisposeAsync()
    {
        chrome?.Dispose();
        api?.Dispose();
        await StopLibraryAsync();
        if (Canvas is not null) await Canvas.DisposeAsync();
        store?.Dispose();
        dir.Dispose();
    }
}
