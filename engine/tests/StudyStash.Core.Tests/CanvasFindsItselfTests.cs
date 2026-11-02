using System.Net;
using Jint;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using StudyStash.Core.Canvas;
using StudyStash.Library;

namespace StudyStash.Core.Tests;

/// <summary>
/// Connecting the add-on with nothing typed (1.8): the library hands the store copy where it is and its key only
/// while the app's window is open and only to the add-on's own page; the add-on finds the student's Canvas from their
/// own sign-in and says where it is; and the library takes the first one it's told.
/// </summary>
public class CanvasFindsItselfTests
{
    const string Password = "maple otter";
    const string StoreCopy = "chrome-extension://" + Extension.ChromeStoreId;

    static async Task<(TempDir Dir, TestSite Site)> LibraryAsync(string canvasUrl = "")
    {
        var dir = new TempDir();
        var cfg = new Config(dir.Path, dir["pool"]) { PoolPassword = Password, OllamaEnabled = false, Classes = [new ClassDef("CS 101")] };
        if (canvasUrl.Length > 0) CanvasSettings.Update(dir.Path, s => s.Url = canvasUrl);
        var store = new Store(cfg.DbPath, cfg.PoolDir);
        var sync = new CanvasSync(dir.Path, c => store.ClassDir(c), _ => { });
        var options = new LibraryWebOptions
        {
            Canvas = sync, ListModels = _ => Task.FromResult<List<(string, double)>?>(null), Tailscale = () => new TailscaleInfo(),
            Latest = _ => Task.FromResult<Release?>(null), RamGb = () => 16, HostName = () => "library-pc", Nonce = () => "NONCE",
        };
        var site = await TestSite.StartAsync(b => LibraryWeb.Build(b, cfg, store, new Pipeline(cfg, store, log: _ => { }), options));
        return (dir, site);
    }

    static Task<HttpResponseMessage> Hello(TestSite site, string? origin)
    {
        var ask = new HttpRequestMessage(HttpMethod.Get, "/api/v2/canvas/hello");
        if (origin is not null) ask.Headers.Add("Origin", origin);
        return site.Client.SendAsync(ask);
    }

    static Task<HttpResponseMessage> Send(TestSite site, string path, string body, string? key = null)
    {
        var ask = new HttpRequestMessage(HttpMethod.Post, path) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        if (key is null) ask.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Password);
        else ask.Headers.Add("X-Study-Stash-Key", key);
        return site.Client.SendAsync(ask);
    }

    [Fact]
    public async Task The_library_answers_the_store_copy_only_while_the_app_is_connecting_it()
    {
        var (dir, site) = await LibraryAsync();
        using var _ = dir;
        await using var __ = site;

        // A web page, or nothing that says where it's from, is never answered; nor is the add-on before Add was pressed.
        Assert.Equal(HttpStatusCode.Forbidden, (await Hello(site, "https://evil.test")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Hello(site, null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Hello(site, "chrome-extension://someoneelse")).StatusCode);
        var early = await Hello(site, StoreCopy);
        Assert.Equal(HttpStatusCode.Forbidden, early.StatusCode);
        Assert.Equal(StoreCopy, early.Headers.GetValues("Access-Control-Allow-Origin").Single()); // so the add-on can say why

        // The app's Add opens the window (with the library password); a page can't.
        Assert.Equal(HttpStatusCode.Unauthorized, (await Send(site, "/api/v2/canvas/pair", "{}", key: "not-a-key")).StatusCode);
        Assert.True((await Send(site, "/api/v2/canvas/pair", "{}")).IsSuccessStatusCode);

        foreach (string origin in new[] { StoreCopy, "moz-extension://9b2c1f0e-1111-4a6b-9c3d-123456789abc" })
        {
            var hello = await Hello(site, origin);
            Assert.True(hello.IsSuccessStatusCode, origin);
            var c = JsonNode.Parse(await hello.Content.ReadAsStringAsync())!;
            Assert.StartsWith("http://127.0.0.1:", c["app"]!.GetValue<string>());
            Assert.Equal(CanvasSettings.ExtensionKey(dir.Path), c["key"]!.GetValue<string>());
            Assert.Equal("", c["canvas"]!.GetValue<string>()); // the add-on finds it
        }
    }

    [Fact]
    public async Task The_canvas_the_add_on_found_becomes_the_librarys_and_a_set_one_stays()
    {
        var (dir, site) = await LibraryAsync();
        using var _ = dir;
        await using var __ = site;
        string key = CanvasSettings.ExtensionKey(dir.Path);

        Assert.Equal(HttpStatusCode.Unauthorized, (await Send(site, "/api/v2/canvas/found", """{"url":"https://evil.test"}""", key: "wrong")).StatusCode);
        Assert.Equal("", CanvasSettings.Load(dir.Path).Url);

        var found = await Send(site, "/api/v2/canvas/found", """{"url":"https://school.instructure.com/"}""", key: key);
        Assert.True(found.IsSuccessStatusCode);
        Assert.Equal("https://school.instructure.com", CanvasSettings.Load(dir.Path).Url);

        // Told another later, with setup's window shut, the library keeps the first (Settings changes it).
        var later = await Send(site, "/api/v2/canvas/found", """{"url":"https://other.instructure.com"}""", key: key);
        Assert.Equal("https://school.instructure.com", JsonNode.Parse(await later.Content.ReadAsStringAsync())!["canvas"]!.GetValue<string>());
        Assert.Equal("https://school.instructure.com", CanvasSettings.Load(dir.Path).Url);
    }

    // ---- the add-on's side (1.8), in Jint with the browser stood in for ----

    const string Library = "http://127.0.0.1:8789";
    const string Hello18 = """{"app":"http://127.0.0.1:8789","key":"k3y","canvas":"","files":["*.inscloudgate.net"],"protocol":3}""";
    const string Cookies = """
        [{"name":"canvas_session","domain":"old.instructure.com","expirationDate":100},
         {"name":"canvas_session","domain":".school.instructure.com","expirationDate":900},
         {"name":"_ga","domain":".instructure.com","expirationDate":999},
         {"name":"canvas_session","domain":"evil.test","expirationDate":999}]
        """;

    static ExtensionScriptTests.FakeResponse Json(string body, int status = 200) =>
        new() { Status = status, Body = body, Headers = { ["content-type"] = "application/json" } };

    [Fact]
    public void Just_added_it_finds_the_library_on_this_computer_and_keeps_it_with_no_canvas_yet()
    {
        var w = new ExtensionScriptTests.Worker(running: "1.8", config: null)
            .Route("http://127.0.0.1:8787/api/v2/canvas/hello", Json("""{"detail":"Open Syft Notes and press Add Syft Notes to your browser there."}""", 403))
            .Route(Library + "/api/v2/canvas/hello", Json(Hello18));
        var c = w.Eval("adoptLibrary().then(c => JSON.stringify(c))").AsString();
        Assert.Contains("\"app\":\"http://127.0.0.1:8789\"", c);
        Assert.Equal("", w.Stored("connection")!["canvas"]!.GetValue<string>());
        // Before Canvas is found it asks for its library and every Canvas Cloud school, not one school.
        Assert.Contains("https://*.instructure.com/*", w.Eval("JSON.stringify(originsWanted(" + c + "))").AsString());
    }

    [Fact]
    public void It_finds_the_canvas_the_student_is_signed_in_to_from_their_own_session()
    {
        var w = new ExtensionScriptTests.Worker(running: "1.8", config: null, cookies: Cookies)
            .Route("https://school.instructure.com/api/v1/users/self", Json("""while(1);{"id":42,"name":"Sam"}"""))
            .Route("https://old.instructure.com/api/v1/users/self", Json("""{"status":"unauthenticated"}""", 401));
        Assert.Equal("https://school.instructure.com", w.Eval("findCanvas()").AsString());
        // The newest session is asked first, and nothing outside instructure.com is.
        Assert.DoesNotContain(w.Fetched, f => f.Url.Contains("evil.test", StringComparison.Ordinal));

        var signedOut = new ExtensionScriptTests.Worker(running: "1.8", config: null, cookies: Cookies);
        Assert.Equal("", signedOut.Eval("findCanvas()").AsString());
        // A browser that gave no cookies permission (1.6, 1.7) finds nothing and asks nothing.
        var none = new ExtensionScriptTests.Worker(running: "1.8", config: null);
        Assert.Equal("", none.Eval("findCanvas()").AsString());
    }

    [Fact]
    public void A_copy_with_no_canvas_tells_the_library_the_one_it_found_and_keeps_it()
    {
        var w = new ExtensionScriptTests.Worker(running: "1.8", config: null, cookies: Cookies,
                stored: """{"app":"http://127.0.0.1:8789","key":"k3y","canvas":"","files":["*.inscloudgate.net"],"protocol":3}""")
            .Route("https://school.instructure.com/api/v1/users/self", Json("""{"id":42}"""))
            .Route(Library + "/api/v2/canvas/found", Json("""{"canvas":"https://school.instructure.com"}"""))
            .Route(Library + "/api/v2/canvas/work", Json("""{"jobs":[],"hot":false,"p":3}"""));
        w.Eval("(async () => { conn = await loadConnection(); await lookForCanvas(); })()");
        var told = Assert.Single(w.Fetched, f => f.Url == Library + "/api/v2/canvas/found");
        Assert.Equal("""{"url":"https://school.instructure.com"}""", told.Body);
        Assert.Equal("https://school.instructure.com", w.Stored("connection")!["canvas"]!.GetValue<string>());
        Assert.Null(w.Stored("connection")!["stored"]);
    }
}
