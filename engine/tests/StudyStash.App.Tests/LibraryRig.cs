using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Avalonia.Media;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using StudyStash.App.Services;
using StudyStash.Core;
using StudyStash.Core.Canvas;
using StudyStash.Core.Tests;
using StudyStash.Library;

namespace StudyStash.App.Tests;

/// <summary>
/// A real library, built the way <c>serve</c> builds it, on a free port of this computer: a throwaway home, classes
/// CS 101 and BIO 110, and nothing synced, so it answers the way a freshly set up library does. <see
/// cref="VisitAsync"/> plays Chrome's extension over HTTP (with the library's key, or another) against a pretend
/// Canvas, so the app's own <see cref="CanvasClient"/> can be tested against what a library really sends.
/// </summary>
public sealed class LibraryRig : IAsyncDisposable
{
    public const string Password = "maple otter";

    /// <summary>The two courses Find my courses finds on the pretend Canvas, in a term that's on now (so the picker
    /// ticks both).</summary>
    static readonly string CoursesJson = $$$"""
        [{"id": 4201, "name": "CS 101 · Intro to Computer Science", "course_code": "CS 101", "term": {"name": "This term", "start_at": "{{{TermStart}}}", "end_at": "{{{TermEnd}}}"}},
         {"id": 4202, "name": "BIO 110 · Cells and Systems", "course_code": "BIO 110", "term": {"name": "This term", "start_at": "{{{TermStart}}}", "end_at": "{{{TermEnd}}}"}}]
        """;
    static string TermStart => DateTimeOffset.UtcNow.AddDays(-30).ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
    static string TermEnd => DateTimeOffset.UtcNow.AddDays(90).ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    readonly TempDir dir = new();
    readonly HttpClient http = new() { Timeout = TimeSpan.FromMinutes(2) };
    readonly Lock answering = new();
    WebApplication? app;
    Store? store;

    public string Url { get; private set; } = "";
    public string Home => dir.Path;
    public CanvasSync Sync { get; private set; } = null!;
    public FakeCanvas Canvas { get; } = FakeCanvas.Cs101()
        .Json("/api/v1/courses", CoursesJson)
        .Json("/api/v1/courses/4202/assignments", "[]").Json("/api/v1/courses/4202/students/submissions", "[]")
        .Json("/api/v1/courses/4202/modules", "[]").Json("/api/v1/courses/4202/discussion_topics", "[]");

    /// <summary>A class's folder in the library.</summary>
    public string ClassDir(string cls) => store!.ClassDir(cls);

    /// <summary>Puts a lecture straight into the store with its notes already written, the way the pipeline leaves
    /// one once it's summarized — for tests that need lectures already there, not sent in through ingest.</summary>
    public string AddLecture(Meeting m, string className, string summaryMd = "", List<string>? topics = null) =>
        store!.Save(m, new Classification(className, 0.95, "folder", topics: topics), summaryMd: summaryMd);

    /// <summary>A place for a test's own files, removed with the rest.</summary>
    public string Scratch(string name) => dir[name];

    /// <summary>The library's port on this computer.</summary>
    public int Port => new Uri(Url).Port;

    /// <summary>The key the library gave its extension.</summary>
    public string ExtensionKey => CanvasSettings.ExtensionKey(Home);

    /// <summary>The app's client for this library, with the library password.</summary>
    public CanvasClient Client() => new(Url, Password, http);

    public static async Task<LibraryRig> StartAsync()
    {
        var rig = new LibraryRig();
        await rig.StartLibraryAsync();
        return rig;
    }

    async Task StartLibraryAsync()
    {
        var cfg = new Config(dir.Path, dir["pool"])
        {
            PoolPassword = Password, OllamaEnabled = false,
            Classes = [new ClassDef("CS 101", ["cs101"]), new ClassDef("BIO 110", ["bio110"])],
        };
        store = new Store(cfg.DbPath, cfg.PoolDir);
        var classDirs = store;
        Sync = new CanvasSync(dir.Path, c => classDirs.ClassDir(c), _ => { }) { Zone = FakeCanvas.Zone, FirstContactWait = TimeSpan.FromSeconds(5) };
        var options = new LibraryWebOptions
        {
            Canvas = Sync, ListModels = _ => Task.FromResult<List<(string, double)>?>(null), Tailscale = () => new TailscaleInfo(),
            Latest = _ => Task.FromResult<Release?>(null), RamGb = () => 16, HostName = () => "library-pc", Nonce = () => "NONCE",
        };
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        WebHostBuilderKestrelExtensions.ConfigureKestrel(builder.WebHost, k => k.Listen(IPAddress.Loopback, 0));
        app = LibraryWeb.Build(builder, cfg, store, new Pipeline(cfg, store, log: _ => { }), options);
        await app.StartAsync();
        Url = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
    }

    /// <summary>One visit from Chrome's extension: ask for work (with <paramref name="key"/>, the library's own by
    /// default, from the library address <paramref name="address"/>), answer it from the pretend Canvas, and hand the
    /// answers back. The number of jobs answered, or -1 when the library turned the key away.</summary>
    public async Task<int> VisitAsync(string? key = null, string address = "http://127.0.0.1:1", CancellationToken stop = default)
    {
        string query = string.Create(CultureInfo.InvariantCulture,
            $"?v={Extension.Version()}&p={Extension.Protocol}&a={Uri.EscapeDataString(address)}&wait=0");
        using var ask = new HttpRequestMessage(HttpMethod.Get, Url + "/api/v2/canvas/work" + query);
        ask.Headers.Add("X-Study-Stash-Key", key ?? ExtensionKey);
        using var r = await http.SendAsync(ask, stop);
        if (r.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden) return -1;
        r.EnsureSuccessStatusCode();
        var jobs = (JsonNode.Parse(await r.Content.ReadAsStringAsync(stop))!["jobs"] as JsonArray ?? []).OfType<JsonObject>().ToList();
        if (jobs.Count == 0) return 0;
        var results = new JsonArray();
        lock (answering)
            foreach (var j in jobs)
            {
                var a = Canvas.Answer(new CanvasJob(j["id"]!.ToString(), (string)j["url"]!, (string)j["kind"]!));
                results.Add(new JsonObject
                {
                    ["id"] = a.Id, ["status"] = a.Status, ["link"] = a.Link, ["text"] = a.Text, ["b64"] = a.B64, ["error"] = a.Error,
                    ["final"] = a.Final, ["signed_out"] = a.SignedOut, ["rate"] = a.Rate, ["retry_after"] = a.RetryAfter, ["type"] = a.Type,
                });
            }
        using var post = new HttpRequestMessage(HttpMethod.Post, Url + "/api/v2/canvas/results")
        {
            Content = new StringContent(new JsonObject { ["results"] = results }.ToJsonString(), Encoding.UTF8, "application/json"),
        };
        post.Headers.Add("X-Study-Stash-Key", key ?? ExtensionKey);
        using var answered = await http.SendAsync(post, stop);
        answered.EnsureSuccessStatusCode();
        return jobs.Count;
    }

    /// <summary>Chrome with the extension running: visits again and again until <paramref name="stop"/>.</summary>
    public async Task RunExtensionAsync(CancellationToken stop, string? key = null, string address = "http://127.0.0.1:1")
    {
        while (!stop.IsCancellationRequested)
        {
            try
            {
                if (await VisitAsync(key, address, stop) <= 0) await Task.Delay(50, stop);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    /// <summary>The app's Canvas context over this library, with actions that do nothing.</summary>
    public CanvasContext Context(CanvasClient? client = null)
    {
        var actions = new CanvasActions(
            OpenUrl: _ => { }, OpenInChrome: _ => { }, OpenChrome: () => { }, OpenChromeExtensions: () => { }, RevealFolder: _ => { },
            OpenFile: _ => { }, PrepareExtension: (_, _) => Path.Combine(Home, "laptop-extension"));
        return new CanvasContext(client ?? Client(), new CanvasClock(() => DateTimeOffset.Now, FakeCanvas.Zone),
            _ => new SolidColorBrush(Colors.Gray), actions, Home);
    }

    /// <summary>The raw JSON the library answers at <paramref name="path"/>.</summary>
    public async Task<JsonNode> RawAsync(string path)
    {
        using var ask = new HttpRequestMessage(HttpMethod.Get, Url + path);
        ask.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", Password);
        using var r = await http.SendAsync(ask);
        string text = await r.Content.ReadAsStringAsync();
        Assert.True(r.IsSuccessStatusCode, $"GET {path}: {(int)r.StatusCode} {text}");
        return JsonNode.Parse(text)!;
    }

    public async ValueTask DisposeAsync()
    {
        if (app is not null)
        {
            await app.StopAsync();
            await app.DisposeAsync();
        }
        store?.Dispose();
        http.Dispose();
        dir.Dispose();
    }
}
