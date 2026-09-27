using System.Globalization;
using System.Net;
using System.Text.Json.Nodes;
using StudyStash.Core.Canvas;
using StudyStash.Library;

namespace StudyStash.Core.Tests;

/// <summary>"Connected" means a Chrome checking in now with the key this library gives its extension: an old
/// registration, a Chrome with another key, or one that came with the library password never counts.</summary>
public class CanvasKeyTests
{
    static readonly DateTimeOffset Now = FakeCanvas.DesignNow;
    static string At(DateTimeOffset t) => t.ToString("o", CultureInfo.InvariantCulture);

    [Fact]
    public void A_registration_from_before_keys_were_written_down_is_not_connected_until_chrome_checks_in_again()
    {
        using var dir = new TempDir();
        var sync = FakeCanvas.Library(dir, () => Now);
        // canvas.json as an older library left it: a Chrome seen a minute ago, with no key written down.
        CanvasSettings.Update(dir.Path, s =>
        {
            s.ExtensionSeen = At(Now.AddMinutes(-1));
            s.ExtensionVersion = "1.4";
            s.ExtensionProtocol = 3;
            s.ExtensionCopies["this_computer"] = new ExtensionCopy(At(Now.AddMinutes(-1)), "1.4", 3);
        });
        var old = CanvasSettings.Load(dir.Path);
        Assert.False(old.ExtensionConnected(Now));
        Assert.False(old.LastKeyMatches);
        Assert.Equal("", old.SeenWithKey);
        Assert.Equal("no_extension", CanvasView.StateOf(old, false, Now));

        sync.Work(false, "1.4", 3, "http://127.0.0.1:8787");
        var now = CanvasSettings.Load(dir.Path);
        Assert.True(now.ExtensionConnected(Now));
        Assert.True(now.LastKeyMatches);
        Assert.Equal(At(Now), now.SeenWithKey);
        Assert.Equal("connected", CanvasView.StateOf(now, false, Now));
    }

    [Fact]
    public void A_new_key_makes_every_chrome_with_the_old_one_not_connected()
    {
        using var dir = new TempDir();
        var sync = FakeCanvas.Library(dir, () => Now);
        sync.Work(false, "1.4", 3, "http://127.0.0.1:8787");
        Assert.True(CanvasSettings.Load(dir.Path).ExtensionConnected(Now));

        File.WriteAllText(Path.Combine(dir.Path, "canvas_key"), "a-brand-new-key");
        var s = CanvasSettings.Load(dir.Path);
        Assert.False(s.ExtensionConnected(Now));
        Assert.False(s.LastKeyMatches);
        Assert.Equal("no_extension", CanvasView.StateOf(s, false, Now));
    }

    [Fact]
    public void A_check_in_with_the_library_password_gets_work_but_is_not_the_extension_connected()
    {
        using var dir = new TempDir();
        var sync = FakeCanvas.Library(dir, () => Now);
        sync.Work(false, "1.4", 3, "http://127.0.0.1:8787", withKey: false);
        var s = CanvasSettings.Load(dir.Path);
        Assert.Equal(CanvasSettings.PasswordKeyId, s.ExtensionCopies["this_computer"].Key);
        Assert.False(s.ExtensionConnected(Now));
        Assert.Equal("no_extension", CanvasView.StateOf(s, false, Now));
    }

    [Fact]
    public void A_key_is_written_down_only_as_a_fingerprint()
    {
        using var dir = new TempDir();
        string key = CanvasSettings.ExtensionKey(dir.Path);
        var sync = FakeCanvas.Library(dir, () => Now);
        sync.Work(false, "1.4", 3, "http://127.0.0.1:8787");
        string written = File.ReadAllText(CanvasSettings.PathIn(dir.Path));
        Assert.DoesNotContain(key, written, StringComparison.Ordinal);
        Assert.Contains(CanvasSettings.KeyId(key), written, StringComparison.Ordinal);
    }

    // ---- through the library's own door, with no library password (the usual home setup) ----

    static async Task<(TempDir Dir, TestSite Site)> LibraryAsync()
    {
        var dir = new TempDir();
        var sync = FakeCanvas.Library(dir, () => DateTimeOffset.Now);
        CanvasSettings.Update(dir.Path, s => s.Courses.Clear()); // nothing to sync: a check-in is only a check-in
        var cfg = new Config(dir.Path, dir["pool"]) { OllamaEnabled = false, Classes = [new ClassDef("CS 101", ["cs101"])] };
        var store = new Store(cfg.DbPath, cfg.PoolDir);
        var options = new LibraryWebOptions
        {
            Canvas = sync, ListModels = _ => Task.FromResult<List<(string, double)>?>(null), Tailscale = () => new TailscaleInfo(),
            Latest = _ => Task.FromResult<Release?>(null), RamGb = () => 16, HostName = () => "library-pc", Nonce = () => "NONCE",
        };
        return (dir, await TestSite.StartAsync(b => LibraryWeb.Build(b, cfg, store, new Pipeline(cfg, store, log: _ => { }), options)));
    }

    static async Task<HttpResponseMessage> CheckInAsync(TestSite site, string key)
    {
        var ask = new HttpRequestMessage(HttpMethod.Get, "/api/v2/canvas/work?v=1.4&p=3&a=http%3A%2F%2F127.0.0.1%3A8787&wait=0");
        ask.Headers.Add("X-Study-Stash-Key", key);
        return await site.Client.SendAsync(ask);
    }

    static async Task<JsonObject> GetAsync(TestSite site, string path) =>
        JsonNode.Parse(await (await site.Client.GetAsync(path)).Content.ReadAsStringAsync())!.AsObject();

    [Fact]
    public async Task A_chrome_with_another_key_is_turned_away_and_the_app_is_told_to_connect_it_again()
    {
        var (dir, site) = await LibraryAsync();
        await using var _1 = site;
        using var _2 = dir;

        var refused = await CheckInAsync(site, "an-old-registration-key");
        Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);
        var state = await GetAsync(site, "/api/v2/canvas/state");
        Assert.Equal("no_extension", state["state"]!.GetValue<string>());
        var ext = state["extension"]!;
        Assert.False(ext["connected"]!.GetValue<bool>());
        Assert.Null(ext["seen"]);
        Assert.NotNull(ext["refused_at"]);
        var about = await GetAsync(site, "/api/v2/canvas/extension");
        Assert.False(about["connected"]!.GetValue<bool>());
        Assert.NotNull(about["refused_at"]);
        Assert.Empty(CanvasSettings.Load(dir.Path).ExtensionCopies); // nothing written down for it

        // The student connects Chrome again (the library's own key): connected, and the refusal is forgotten.
        var ok = await CheckInAsync(site, CanvasSettings.ExtensionKey(dir.Path));
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        state = await GetAsync(site, "/api/v2/canvas/state");
        Assert.Equal("connected", state["state"]!.GetValue<string>());
        ext = state["extension"]!;
        Assert.True(ext["connected"]!.GetValue<bool>());
        Assert.True(ext["key_matches"]!.GetValue<bool>());
        Assert.NotNull(ext["seen"]);
        Assert.Null(ext["refused_at"]);
        about = await GetAsync(site, "/api/v2/canvas/extension");
        Assert.True(about["connected"]!.GetValue<bool>());
        Assert.True(about["key_matches"]!.GetValue<bool>());
        Assert.True(about["copies"]![0]!["key_matches"]!.GetValue<bool>());
    }

    [Fact]
    public async Task A_request_with_no_extension_key_still_goes_by_the_library_password()
    {
        var (dir, site) = await LibraryAsync();
        await using var _1 = site;
        using var _2 = dir;
        // No password on this library: the app's own reads (no extension key at all) are let in as before.
        var r = await site.Client.GetAsync("/api/v2/canvas/status");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
    }
}
