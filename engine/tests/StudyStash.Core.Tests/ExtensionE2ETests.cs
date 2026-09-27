using System.Diagnostics;
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
/// library's "Make it" writes. Skipped unless STUDYSTASH_E2E_CHROME is set (engine/tests/extension-e2e.sh).
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
    }

    [ChromeFact]
    public async Task S2_find_courses_lists_the_courses()
    {
        var sw = Stopwatch.StartNew();
        var found = await rig.PostAsync("/api/v2/canvas/courses", "{}");
        Note($"Find courses answered in {sw.Elapsed.TotalSeconds:0.0} s, error '{ExtensionRig.S(found["error"])}'");
        Assert.Equal("", ExtensionRig.S(found["error"]));
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
        var sw = Stopwatch.StartNew();
        var found = await rig.PostAsync("/api/v2/canvas/courses", "{}");
        Note($"Find courses signed out answered in {sw.Elapsed.TotalSeconds:0.0} s: {found["error"]?.ToJsonString()}");
        Assert.Equal("Chrome isn't signed in to Canvas.", ExtensionRig.S(found["error"]));
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
}

/// <summary>One pretend Canvas, one library and one Chrome for all the end-to-end tests, started once (and only
/// when there's a Chrome to run).</summary>
public sealed class ExtensionRig : IAsyncLifetime
{
    const string Password = "e2e-library-password";

    readonly TempDir dir = new();
    readonly Stopwatch sinceChrome = new();
    Store? store;
    WebApplication? library;
    ChromeRunner? chrome;
    HttpClient? api;

    public CanvasServer Canvas { get; private set; } = null!;
    public string Home => dir.Path;
    /// <summary>The extension folder Chrome loaded.</summary>
    public string Folder => Extension.Folder(Home);
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

        var cfg = new Config(dir.Path, dir["pool"])
        {
            PoolPassword = Password, OllamaEnabled = false, WebPort = FreePort(),
            Classes = [new ClassDef("CS 101", ["cs101"]), new ClassDef("BIO 110", ["bio110"])],
        };
        store = new Store(cfg.DbPath, cfg.PoolDir);
        var options = new LibraryWebOptions
        {
            ListModels = _ => Task.FromResult<List<(string, double)>?>(null), Tailscale = () => new TailscaleInfo(),
            Latest = _ => Task.FromResult<Release?>(null), RamGb = () => 16, HostName = () => "library-pc",
        };
        // The way `serve` runs it: Kestrel, HTTP/1, on this computer's loopback. STUDYSTASH_E2E_LIBRARY_HOST (an
        // address of this computer's, like its LAN one) puts it there instead, the way Chrome on a laptop reaches a
        // library on another computer.
        var host = IPAddress.Parse(Environment.GetEnvironmentVariable("STUDYSTASH_E2E_LIBRARY_HOST") is { Length: > 0 } h ? h : "127.0.0.1");
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(k => k.Listen(host, cfg.WebPort, o => o.Protocols = HttpProtocols.Http1));
        library = LibraryWeb.Build(builder, cfg, store, new Pipeline(cfg, store, log: _ => { }), options);
        await library.StartAsync();
        string libraryUrl = $"http://{host}:{cfg.WebPort}";
        api = new HttpClient { BaseAddress = new Uri(libraryUrl), Timeout = TimeSpan.FromMinutes(3) };
        api.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Password);

        // What a student does today: type the Canvas address, then "Make it" writes the folder Chrome loads.
        await PostAsync("/api/v2/canvas", new JsonObject { ["url"] = Canvas.Url }.ToJsonString());
        Extension.Prepare(Folder, libraryUrl, CanvasSettings.ExtensionKey(Home), CanvasSettings.Load(Home).Url);

        chrome = ChromeRunner.Start(Folder, dir["chrome-profile"], Canvas.Url + "/login/e2e", CanvasServer.Host);
        sinceChrome.Start();
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
        if (library is not null)
        {
            await library.StopAsync();
            await library.DisposeAsync();
        }
        if (Canvas is not null) await Canvas.DisposeAsync();
        store?.Dispose();
        dir.Dispose();
    }
}
