using System.Text.Json.Nodes;
using StudyStash.App.Services;
using StudyStash.Core.Canvas;

namespace StudyStash.App.Tests;

/// <summary>The laptop's Chrome extension folder, kept current by the app itself against a pretend library.</summary>
public class ExtensionKeeperTests
{
    const string NewLibrary = """
        {"key": "test-key-abc123", "canvas": "https://school.instructure.com", "version": "1.4", "protocol": 3,
         "folder": "/elsewhere/chrome-extension", "folder_ready": true, "seen": "2025-09-25T17:23:40Z",
         "seen_version": "1.4", "seen_protocol": 3, "seen_where": "another_computer", "connected": true}
        """;

    static CanvasClient Client(FakeLibrary fake, string url = "https://mini.tail.ts.net") => new(url, "test-key", fake.Client());

    static JsonObject Config(string folder) => JsonNode.Parse(File.ReadAllText(Path.Combine(folder, "config.json")))!.AsObject();

    static string[] Hosts(string folder) =>
        [.. JsonNode.Parse(File.ReadAllText(Path.Combine(folder, "manifest.json")))!["host_permissions"]!.AsArray().Select(h => h!.GetValue<string>())];

    [Fact]
    public async Task Paired_with_a_library_that_has_Canvas_it_writes_the_folder()
    {
        using var home = new TempHome();
        var fake = new FakeLibrary().Json(HttpMethod.Get, "/api/v2/canvas/extension", NewLibrary);
        var keeper = new ExtensionKeeper(home.Path, () => Client(fake));
        int changed = 0;
        keeper.Changed += () => changed++;

        Assert.True(await keeper.KeepAsync(TestContext.Current.CancellationToken));

        Assert.Equal(home["chrome-extension"], keeper.LocalFolder);
        Assert.True(Extension.Ready(keeper.LocalFolder));
        Assert.True(keeper.FolderReady);
        Assert.True(keeper.Connected);
        Assert.Equal("another_computer", keeper.SeenWhere);
        Assert.Equal(1, changed);
        var config = Config(keeper.LocalFolder);
        Assert.Equal("https://mini.tail.ts.net", config["app"]!.GetValue<string>());
        Assert.Equal("test-key-abc123", config["key"]!.GetValue<string>());
        Assert.Equal("https://school.instructure.com", config["canvas"]!.GetValue<string>());
        Assert.Equal(["https://school.instructure.com/*", "https://*.inscloudgate.net/*", "https://mini.tail.ts.net/*"], Hosts(keeper.LocalFolder));
    }

    [Fact]
    public async Task Nothing_changed_leaves_the_folder_alone()
    {
        using var home = new TempHome();
        var fake = new FakeLibrary().Json(HttpMethod.Get, "/api/v2/canvas/extension", NewLibrary);
        var keeper = new ExtensionKeeper(home.Path, () => Client(fake));
        await keeper.KeepAsync(TestContext.Current.CancellationToken);
        string configPath = Path.Combine(keeper.LocalFolder, "config.json");
        var written = DateTime.UtcNow.AddHours(-1);
        foreach (string f in Directory.GetFiles(keeper.LocalFolder)) File.SetLastWriteTimeUtc(f, written);
        int changed = 0;
        keeper.Changed += () => changed++;

        Assert.False(await keeper.KeepAsync(TestContext.Current.CancellationToken));

        Assert.All(Directory.GetFiles(keeper.LocalFolder), f => Assert.Equal(written, File.GetLastWriteTimeUtc(f)));
        Assert.True(keeper.FolderReady);
        Assert.Equal(0, changed);
        Assert.Equal(2, fake.Requests.Count(r => r.Path == "/api/v2/canvas/extension")); // asked each time, wrote once
    }

    [Fact]
    public async Task A_new_Canvas_address_rewrites_the_folder()
    {
        using var home = new TempHome();
        string canvas = "https://school.instructure.com";
        var fake = new FakeLibrary();
        void Library() => fake.Json(HttpMethod.Get, "/api/v2/canvas/extension", $$"""{"key": "test-key-abc123", "canvas": "{{canvas}}", "version": "1.4"}""");
        Library();
        var keeper = new ExtensionKeeper(home.Path, () => Client(fake));
        await keeper.KeepAsync(TestContext.Current.CancellationToken);

        canvas = "https://canvas.other.edu";
        Library();
        Assert.True(await keeper.KeepAsync(TestContext.Current.CancellationToken));

        Assert.Equal("https://canvas.other.edu", Config(keeper.LocalFolder)["canvas"]!.GetValue<string>());
        Assert.Equal("https://canvas.other.edu/*", Hosts(keeper.LocalFolder)[0]);
    }

    [Fact]
    public async Task A_new_library_address_rewrites_the_folder_and_leaves_the_default_port_out()
    {
        using var home = new TempHome();
        var fake = new FakeLibrary().Json(HttpMethod.Get, "/api/v2/canvas/extension", NewLibrary);
        string url = "http://192.168.1.20:8787";
        var keeper = new ExtensionKeeper(home.Path, () => Client(fake, url));
        await keeper.KeepAsync(TestContext.Current.CancellationToken);
        Assert.Equal("http://192.168.1.20:8787/*", Hosts(keeper.LocalFolder)[^1]);

        url = "https://mini.tail.ts.net:443";
        Assert.True(await keeper.KeepAsync(TestContext.Current.CancellationToken));

        Assert.Equal("https://mini.tail.ts.net/*", Hosts(keeper.LocalFolder)[^1]);
        Assert.StartsWith("https://mini.tail.ts.net", Config(keeper.LocalFolder)["app"]!.GetValue<string>());
    }

    [Fact]
    public async Task Unpaired_it_does_nothing()
    {
        using var home = new TempHome();
        var keeper = new ExtensionKeeper(home.Path, () => null);

        Assert.False(await keeper.KeepAsync(TestContext.Current.CancellationToken));

        Assert.False(Directory.Exists(keeper.LocalFolder));
        Assert.False(keeper.FolderReady);
    }

    [Fact]
    public async Task No_Canvas_address_yet_writes_nothing()
    {
        using var home = new TempHome();
        var fake = new FakeLibrary().Json(HttpMethod.Get, "/api/v2/canvas/extension", """{"key": "test-key-abc123", "canvas": "", "version": "1.4"}""");
        var keeper = new ExtensionKeeper(home.Path, () => Client(fake));

        Assert.False(await keeper.KeepAsync(TestContext.Current.CancellationToken));

        Assert.False(Directory.Exists(keeper.LocalFolder));
        Assert.False(keeper.FolderReady);
    }

    [Fact]
    public async Task An_older_library_without_the_new_fields_still_gets_a_folder()
    {
        using var home = new TempHome();
        var fake = new FakeLibrary().Json(HttpMethod.Get, "/api/v2/canvas/extension", "extension"); // key, canvas, version only
        var keeper = new ExtensionKeeper(home.Path, () => Client(fake));

        Assert.True(await keeper.KeepAsync(TestContext.Current.CancellationToken));

        Assert.True(keeper.FolderReady);
        Assert.False(keeper.Connected);
        Assert.Equal("", keeper.SeenWhere);
    }

    [Fact]
    public async Task A_library_without_the_call_or_not_answering_changes_nothing()
    {
        using var home = new TempHome();
        var missing = new ExtensionKeeper(home.Path, () => Client(new FakeLibrary())); // 404: a library from before the call
        Assert.False(await missing.KeepAsync(TestContext.Current.CancellationToken));

        var refused = new FakeLibrary().Status(HttpMethod.Get, "/api/v2/canvas/extension", System.Net.HttpStatusCode.Unauthorized, """{"detail": "Wrong key."}""");
        var keeper = new ExtensionKeeper(home.Path, () => Client(refused));
        Assert.False(await keeper.KeepAsync(TestContext.Current.CancellationToken));

        Assert.False(Directory.Exists(Path.Combine(home.Path, "chrome-extension")));
    }

    [Fact]
    public async Task The_library_on_this_computer_keeps_its_own_folder()
    {
        using var home = new TempHome();
        string folder = home["chrome-extension"].Replace("\\", "\\\\", StringComparison.Ordinal);
        var fake = new FakeLibrary().Json(HttpMethod.Get, "/api/v2/canvas/extension",
            $$"""{"key": "test-key-abc123", "canvas": "https://school.instructure.com", "folder": "{{folder}}", "folder_ready": true, "seen_where": "this_computer", "connected": true}""");
        var keeper = new ExtensionKeeper(home.Path, () => Client(fake, "http://127.0.0.1:8787"));

        Assert.False(await keeper.KeepAsync(TestContext.Current.CancellationToken));

        Assert.False(Directory.Exists(keeper.LocalFolder)); // the library writes it, not the app
        Assert.True(keeper.FolderReady);
        Assert.Equal("this_computer", keeper.SeenWhere);
    }

    [Fact]
    public async Task The_watch_keeps_the_folder_on_every_refresh()
    {
        using var home = new TempHome();
        var fake = new FakeLibrary()
            .Json(HttpMethod.Get, "/api/v2/canvas/state", "state-connected")
            .Json(HttpMethod.Get, "/api/v2/canvas/extension", NewLibrary);
        var context = CanvasFixtures.Context(fake, home.Path);
        var watch = new CanvasWatch(context) { Extension = new ExtensionKeeper(home.Path, () => context.Client) };

        await watch.RefreshAsync(TestContext.Current.CancellationToken);

        Assert.True(watch.Extension.FolderReady);
        Assert.Equal("https://library.test", Config(watch.Extension.LocalFolder)["app"]!.GetValue<string>());
    }
}
