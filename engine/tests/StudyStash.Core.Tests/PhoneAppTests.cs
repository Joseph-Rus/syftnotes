using System.Net;
using StudyStash.Library;

namespace StudyStash.Core.Tests;

/// <summary>The phone app's files at /app/: its own screens all answered with index.html, the right types and cache
/// rules, its own content security policy, and a plain page when this copy was built without it.</summary>
public class PhoneAppTests
{
    const string Index = "<!doctype html><title>Study Stash</title><script type=\"module\" src=\"/app/assets/index-a1b2c3.js\"></script>";

    /// <summary>A built phone app, as Vite leaves it in web/dist.</summary>
    static string BuiltApp(TempDir dir)
    {
        string web = dir["web"];
        Directory.CreateDirectory(Path.Combine(web, "assets"));
        File.WriteAllText(Path.Combine(web, "index.html"), Index);
        File.WriteAllText(Path.Combine(web, "sw.js"), "self.addEventListener('fetch',()=>{});");
        File.WriteAllText(Path.Combine(web, "manifest.webmanifest"), "{\"name\":\"Study Stash\",\"start_url\":\"/app/\"}");
        File.WriteAllText(Path.Combine(web, "assets", "index-a1b2c3.js"), "console.log('hi')");
        File.WriteAllText(Path.Combine(web, "assets", "index-d4e5f6.css"), "body{}");
        File.WriteAllBytes(Path.Combine(web, "icon-192.png"), [0x89, 0x50, 0x4E, 0x47]);
        File.WriteAllText(Path.Combine(web, ".secret"), "hidden");
        return web;
    }

    static async Task<TestSite> Site(TempDir dir, Config cfg, Store store, bool built = true) =>
        await PhoneApiTests.Site(cfg, store, phoneApp: built ? BuiltApp(dir) : dir["nothing-here"]);

    [Theory]
    [InlineData("/app/")]
    [InlineData("/app/lectures/42")]
    [InlineData("/app/search?q=cells")]
    [InlineData("/app/classes/CS%20101")]
    public async Task Every_screen_of_the_app_is_its_index_page_with_its_own_rules(string path)
    {
        using var dir = new TempDir();
        var (cfg, store) = PhoneApiTests.Library(dir);
        using var _s = store;
        await using var site = await Site(dir, cfg, store);

        var r = await site.Stranger().GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal(Index, await r.Content.ReadAsStringAsync());
        Assert.Equal("text/html; charset=utf-8", r.Content.Headers.ContentType!.ToString());
        Assert.Equal(LibraryWeb.PhoneCsp, r.Headers.GetValues("Content-Security-Policy").Single());
        Assert.Equal("default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data: blob:; connect-src 'self'; "
            + "worker-src 'self'; manifest-src 'self'; frame-ancestors 'none'", LibraryWeb.PhoneCsp);
        Assert.Equal("no-cache", r.Headers.CacheControl!.ToString());
        Assert.Equal("nosniff", r.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", r.Headers.GetValues("X-Frame-Options").Single());
    }

    [Fact]
    public async Task Built_files_keep_for_a_year_and_the_rest_are_checked_each_time()
    {
        using var dir = new TempDir();
        var (cfg, store) = PhoneApiTests.Library(dir);
        using var _s = store;
        await using var site = await Site(dir, cfg, store);
        var phone = site.Stranger();

        var js = await phone.GetAsync("/app/assets/index-a1b2c3.js");
        Assert.Equal(HttpStatusCode.OK, js.StatusCode);
        Assert.Equal("console.log('hi')", await js.Content.ReadAsStringAsync());
        Assert.Equal("text/javascript; charset=utf-8", js.Content.Headers.ContentType!.ToString());
        Assert.Equal("public, max-age=31536000, immutable", js.Headers.CacheControl!.ToString());
        var css = await phone.GetAsync("/app/assets/index-d4e5f6.css");
        Assert.Equal("text/css; charset=utf-8", css.Content.Headers.ContentType!.ToString());

        var manifest = await phone.GetAsync("/app/manifest.webmanifest");
        Assert.Equal("application/manifest+json", manifest.Content.Headers.ContentType!.MediaType);
        Assert.Equal("no-cache", manifest.Headers.CacheControl!.ToString());

        var icon = await phone.GetAsync("/app/icon-192.png");
        Assert.Equal("image/png", icon.Content.Headers.ContentType!.ToString());
        Assert.Equal("no-cache", icon.Headers.CacheControl!.ToString());
    }

    [Fact]
    public async Task The_service_worker_may_look_after_all_of_app_and_is_never_kept_stale()
    {
        using var dir = new TempDir();
        var (cfg, store) = PhoneApiTests.Library(dir);
        using var _s = store;
        await using var site = await Site(dir, cfg, store);

        var r = await site.Stranger().GetAsync("/app/sw.js");

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal("/app/", r.Headers.GetValues("Service-Worker-Allowed").Single());
        Assert.Equal("no-cache", r.Headers.CacheControl!.ToString());
        Assert.Equal("text/javascript", r.Content.Headers.ContentType!.MediaType);
    }

    [Fact]
    public async Task A_missing_file_is_a_404_not_the_page()
    {
        using var dir = new TempDir();
        var (cfg, store) = PhoneApiTests.Library(dir);
        using var _s = store;
        await using var site = await Site(dir, cfg, store);

        Assert.Equal(HttpStatusCode.NotFound, (await site.Stranger().GetAsync("/app/assets/index-old.js")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await site.Stranger().GetAsync("/app/.secret")).StatusCode);
    }

    [Theory]
    [InlineData("/app/..%2F..%2Fhome%2Fweb_secret")]
    [InlineData("/app/%2e%2e/%2e%2e/home/web_secret")]
    [InlineData("/app/..%5C..%5Chome%5Cweb_secret")]
    [InlineData("/app/assets/..%2F..%2F..%2Fhome%2Fconfig.toml")]
    public async Task Nothing_outside_the_app_folder_is_served(string path)
    {
        using var dir = new TempDir();
        var (cfg, store) = PhoneApiTests.Library(dir);
        using var _s = store;
        await using var site = await Site(dir, cfg, store);
        string secret = File.ReadAllText(Path.Combine(cfg.Home, "web_secret"));

        var r = await site.Stranger().SendAsync(new HttpRequestMessage(HttpMethod.Get, new Uri("http://localhost" + path, UriKind.Absolute)));

        string body = await r.Content.ReadAsStringAsync();
        Assert.DoesNotContain(secret, body);
        Assert.DoesNotContain("pool_password", body);
    }

    [Fact]
    public async Task App_without_its_slash_goes_to_it()
    {
        using var dir = new TempDir();
        var (cfg, store) = PhoneApiTests.Library(dir);
        using var _s = store;
        await using var site = await Site(dir, cfg, store);
        var r = await site.Stranger().GetAsync("/app");
        Assert.Equal(HttpStatusCode.MovedPermanently, r.StatusCode);
        Assert.Equal("/app/", r.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task Without_the_app_built_a_page_says_so_in_the_library_look()
    {
        using var dir = new TempDir();
        var (cfg, store) = PhoneApiTests.Library(dir);
        using var _s = store;
        await using var site = await Site(dir, cfg, store, built: false);

        var r = await site.Stranger().GetAsync("/app/");

        Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
        string page = await r.Content.ReadAsStringAsync();
        Assert.Contains("The phone app isn't here yet", page);
        Assert.Contains("class=\"login\"", page);
        Assert.StartsWith("default-src 'self'; script-src 'nonce-", r.Headers.GetValues("Content-Security-Policy").Single());
    }
}
