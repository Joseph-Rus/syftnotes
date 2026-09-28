using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using StudyStash.Library;

namespace StudyStash.Core.Tests;

/// <summary>Adding a phone through the library's API: a code from a computer that's in, the phone pairing with it and
/// getting a cookie that reads /api/v2 like the app, and removing the phone locking it out at once.</summary>
public class PhoneApiTests
{
    static readonly TailscaleInfo Tailnet = new(true, true, "Running", "mini.tail1234.ts.net.", ["100.64.0.9"], "/usr/local/bin/tailscale");

    internal sealed class Clock
    {
        public DateTimeOffset Now { get; set; } = new(2026, 9, 28, 12, 0, 0, TimeSpan.Zero);
    }

    internal static (Config Cfg, Store Store) Library(TempDir dir, string password = "pw")
    {
        var cfg = new Config(dir["home"], dir["pool"])
        {
            PoolName = "Sam's library", PoolPassword = password, OllamaEnabled = false, Classes = [new ClassDef("CS 101")],
        };
        Directory.CreateDirectory(cfg.Home);
        Configs.Save(cfg);
        return (cfg, new Store(cfg.DbPath, cfg.PoolDir));
    }

    /// <summary>Tailscale here, faked: each Serve it's asked for is noted in <paramref name="served"/>.</summary>
    internal static ClaudeReach Reach(List<IReadOnlyList<string>>? served = null, TailscaleInfo? ts = null) => new()
    {
        Tailscale = () => ts ?? Tailnet,
        Watch = (_, args, _, _) =>
        {
            served?.Add(args);
            return new WatchResult(0, "");
        },
    };

    internal static Task<TestSite> Site(Config cfg, Store store, Clock? clock = null, ClaudeReach? reach = null, string? phoneApp = null) =>
        TestSite.StartAsync(b => LibraryWeb.Build(b, cfg, store, new Pipeline(cfg, store, log: _ => { }), new LibraryWebOptions
        {
            ListModels = _ => Task.FromResult<List<(string, double)>?>(null),
            Tailscale = () => Tailnet,
            Latest = _ => Task.FromResult<Release?>(null),
            Reach = reach ?? Reach(),
            Devices = new Devices(cfg.Home, clock is null ? null : () => clock.Now),
            PhoneApp = phoneApp ?? Path.Combine(cfg.Home, "no-phone-app"),
        }));

    static HttpRequestMessage Req(HttpMethod m, string path, object? body = null, string? key = "pw", string? device = null)
    {
        var r = new HttpRequestMessage(m, path);
        if (key is not null) r.Headers.Authorization = new("Bearer", key);
        if (device is not null) r.Headers.Add("Cookie", $"{Devices.Cookie}={device}");
        if (body is not null) r.Content = JsonContent.Create(body);
        return r;
    }

    static async Task<JsonObject> Json(HttpResponseMessage r)
    {
        Assert.True(r.IsSuccessStatusCode, $"{(int)r.StatusCode}: {await r.Content.ReadAsStringAsync()}");
        return (JsonObject)JsonNode.Parse(await r.Content.ReadAsStringAsync())!;
    }

    static async Task<string> Error(HttpResponseMessage r, HttpStatusCode status)
    {
        Assert.Equal(status, r.StatusCode);
        return (JsonNode.Parse(await r.Content.ReadAsStringAsync()) as JsonObject)?["error"]?.GetValue<string>() ?? "";
    }

    internal static async Task<string> CodeAsync(TestSite site) =>
        (await Json(await site.Client.SendAsync(Req(HttpMethod.Post, "/api/v2/devices/code", new { }))))["code"]!.GetValue<string>();

    /// <summary>Pairs a phone and hands back its cookie's value, read from the Set-Cookie header (a Secure cookie
    /// isn't kept by a test's http:// cookie jar).</summary>
    internal static async Task<string> PairAsync(TestSite site, string? name = "Sam's iPhone")
    {
        var r = await site.Stranger().SendAsync(Req(HttpMethod.Post, "/api/v2/devices/pair", new { code = await CodeAsync(site), name }, key: null));
        await Json(r);
        string set = r.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith(Devices.Cookie + "=", StringComparison.Ordinal));
        return set[(Devices.Cookie.Length + 1)..set.IndexOf(';')];
    }

    [Fact]
    public async Task A_code_turns_on_serve_for_the_phone_app_and_gives_its_address()
    {
        using var dir = new TempDir();
        var (cfg, store) = Library(dir);
        using var _s = store;
        var served = new List<IReadOnlyList<string>>();
        await using var site = await Site(cfg, store, reach: Reach(served));

        var r = await site.Client.SendAsync(Req(HttpMethod.Post, "/api/v2/devices/code", new { }));
        var code = await Json(r);

        Assert.Matches("^[0-9]{6}$", code["code"]!.GetValue<string>());
        Assert.Equal("https://mini.tail1234.ts.net:8443/app/", code["url"]!.GetValue<string>());
        var expires = DateTimeOffset.Parse(code["expires"]!.GetValue<string>(), System.Globalization.CultureInfo.InvariantCulture);
        Assert.InRange(expires - DateTimeOffset.UtcNow, TimeSpan.FromMinutes(9), TimeSpan.FromMinutes(10));
        Assert.Equal(["serve", "--bg", "--https=8443", $"http://127.0.0.1:{cfg.WebPort}"], served.Single());
        Assert.Equal("no-store", r.Headers.CacheControl!.ToString());
    }

    [Fact]
    public async Task Only_a_computer_that_is_in_can_make_a_code()
    {
        using var dir = new TempDir();
        var (cfg, store) = Library(dir);
        using var _s = store;
        await using var site = await Site(cfg, store);
        Assert.Equal(HttpStatusCode.Unauthorized, (await site.Client.SendAsync(Req(HttpMethod.Post, "/api/v2/devices/code", new { }, key: null))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await site.Client.SendAsync(Req(HttpMethod.Post, "/api/v2/devices/code", new { }, key: "nope"))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await site.Client.SendAsync(Req(HttpMethod.Get, "/api/v2/devices", key: null))).StatusCode);
    }

    [Fact]
    public async Task Without_tailscale_there_is_no_code_and_it_says_why()
    {
        using var dir = new TempDir();
        var (cfg, store) = Library(dir);
        using var _s = store;
        await using var site = await Site(cfg, store, reach: Reach(ts: Tailnet with { Dns = "" }));

        var r = await site.Client.SendAsync(Req(HttpMethod.Post, "/api/v2/devices/code", new { }));
        Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
        var said = (JsonObject)JsonNode.Parse(await r.Content.ReadAsStringAsync())!;
        Assert.StartsWith("Your phone reaches the library over Tailscale", said["detail"]!.GetValue<string>());
        Assert.Contains("MagicDNS", said["detail"]!.GetValue<string>());
        Assert.Equal(ClaudeReach.DnsPage, said["fix"]!.GetValue<string>());

        await using var none = await Site(cfg, store, reach: new ClaudeReach());
        Assert.Contains("Tailscale isn't on the library's computer",
            (await (await none.Client.SendAsync(Req(HttpMethod.Post, "/api/v2/devices/code", new { }))).Content.ReadAsStringAsync()));
    }

    [Fact]
    public async Task A_paired_phone_gets_a_strict_cookie_that_reads_the_library_like_the_app()
    {
        using var dir = new TempDir();
        var (cfg, store) = Library(dir);
        using var _s = store;
        store.Save(new Meeting("m1") { Title = "Recursion", Date = "2026-09-01", Transcript = "Today." }, new Classification("CS 101", 0.9, "folder"));
        await using var site = await Site(cfg, store);
        var phone = site.Stranger();

        var r = await phone.SendAsync(Req(HttpMethod.Post, "/api/v2/devices/pair", new { code = await CodeAsync(site), name = "Sam's iPhone" }, key: null));
        var paired = await Json(r);

        Assert.Equal("Sam's iPhone", paired["device"]!["name"]!.GetValue<string>());
        Assert.NotNull(paired["device"]!["id"]);
        Assert.NotNull(paired["device"]!["added"]);
        string set = r.Headers.GetValues("Set-Cookie").Single();
        Assert.StartsWith("device=", set);
        foreach (string part in new[] { "max-age=34560000", "path=/", "secure", "samesite=strict", "httponly" })
            Assert.Contains(part, set, StringComparison.OrdinalIgnoreCase);
        string device = set[7..set.IndexOf(';')];

        Assert.Equal(HttpStatusCode.Unauthorized, (await phone.SendAsync(Req(HttpMethod.Get, "/api/v2/lectures", key: null))).StatusCode);
        var lectures = (JsonArray)JsonNode.Parse(await (await phone.SendAsync(Req(HttpMethod.Get, "/api/v2/lectures", key: null, device: device))).Content.ReadAsStringAsync())!;
        Assert.Equal("Recursion", lectures.Single()!["title"]!.GetValue<string>());
        Assert.Equal("CS 101", (await Json(await phone.SendAsync(Req(HttpMethod.Get, "/api/v2/lectures/m1", key: null, device: device))))["class"]!.GetValue<string>());
        Assert.True((await Json(await phone.SendAsync(Req(HttpMethod.Get, "/api/v2/me", key: null, device: device))))["paired"]!.GetValue<bool>());
        // Like a signed-in member, it can add another phone and see the phones.
        Assert.Equal(HttpStatusCode.OK, (await phone.SendAsync(Req(HttpMethod.Post, "/api/v2/devices/code", new { }, key: null, device: device))).StatusCode);
        var list = await Json(await phone.SendAsync(Req(HttpMethod.Get, "/api/v2/devices", key: null, device: device)));
        var only = list["devices"]!.AsArray().Single()!;
        Assert.Equal(("Sam's iPhone", paired["device"]!["id"]!.GetValue<string>()), (only["name"]!.GetValue<string>(), only["id"]!.GetValue<string>()));
        Assert.NotNull(only["lastSeen"]);
    }

    [Fact]
    public async Task The_cookie_opens_only_the_api_not_the_pages_or_the_laptop_routes()
    {
        using var dir = new TempDir();
        var (cfg, store) = Library(dir);
        using var _s = store;
        await using var site = await Site(cfg, store);
        string device = await PairAsync(site);
        var phone = site.Stranger();

        Assert.Equal(HttpStatusCode.Unauthorized, (await phone.SendAsync(Req(HttpMethod.Get, "/api/health", key: null, device: device))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await phone.SendAsync(Req(HttpMethod.Post, "/api/ingest", new { id = "x" }, key: null, device: device))).StatusCode);
        var page = await phone.SendAsync(Req(HttpMethod.Get, "/settings", key: null, device: device));
        Assert.Equal(HttpStatusCode.SeeOther, page.StatusCode);
        Assert.StartsWith("/login", page.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task A_wrong_code_is_refused_in_words()
    {
        using var dir = new TempDir();
        var (cfg, store) = Library(dir);
        using var _s = store;
        await using var site = await Site(cfg, store);
        string code = await CodeAsync(site);
        string wrong = code == "000000" ? "111111" : "000000";

        var r = await site.Stranger().SendAsync(Req(HttpMethod.Post, "/api/v2/devices/pair", new { code = wrong, name = "Guess" }, key: null));
        Assert.StartsWith("That code isn't right", await Error(r, HttpStatusCode.Unauthorized));
        Assert.False(r.Headers.Contains("Set-Cookie"));
        Assert.Equal(HttpStatusCode.Unauthorized, (await site.Stranger().SendAsync(Req(HttpMethod.Post, "/api/v2/devices/pair", new { }, key: null))).StatusCode);
        var junk = new HttpRequestMessage(HttpMethod.Post, "/api/v2/devices/pair") { Content = new StringContent("not json") };
        Assert.Equal(HttpStatusCode.Unauthorized, (await site.Stranger().SendAsync(junk)).StatusCode);
        // The right one still works after a wrong one.
        Assert.Equal(HttpStatusCode.OK, (await site.Stranger().SendAsync(Req(HttpMethod.Post, "/api/v2/devices/pair", new { code }, key: null))).StatusCode);
    }

    [Fact]
    public async Task A_code_sent_as_a_number_pairs_too()
    {
        using var dir = new TempDir();
        var (cfg, store) = Library(dir);
        using var _s = store;
        await using var site = await Site(cfg, store);
        int code = int.Parse(await CodeAsync(site), System.Globalization.CultureInfo.InvariantCulture);
        var r = await site.Stranger().SendAsync(Req(HttpMethod.Post, "/api/v2/devices/pair", new { code }, key: null));
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
    }

    [Fact]
    public async Task A_code_works_once()
    {
        using var dir = new TempDir();
        var (cfg, store) = Library(dir);
        using var _s = store;
        await using var site = await Site(cfg, store);
        string code = await CodeAsync(site);

        Assert.Equal(HttpStatusCode.OK, (await site.Stranger().SendAsync(Req(HttpMethod.Post, "/api/v2/devices/pair", new { code }, key: null))).StatusCode);
        await Error(await site.Stranger().SendAsync(Req(HttpMethod.Post, "/api/v2/devices/pair", new { code }, key: null)), HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_code_stops_working_after_ten_minutes()
    {
        using var dir = new TempDir();
        var (cfg, store) = Library(dir);
        using var _s = store;
        var clock = new Clock();
        await using var site = await Site(cfg, store, clock);
        string code = await CodeAsync(site);
        clock.Now += TimeSpan.FromMinutes(10);

        await Error(await site.Stranger().SendAsync(Req(HttpMethod.Post, "/api/v2/devices/pair", new { code }, key: null)), HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Many_wrong_codes_stop_pairing_for_a_while()
    {
        using var dir = new TempDir();
        var (cfg, store) = Library(dir);
        using var _s = store;
        var clock = new Clock();
        await using var site = await Site(cfg, store, clock);
        for (int i = 0; i < Devices.TriesPerWindow; i++)
            await Error(await site.Stranger().SendAsync(Req(HttpMethod.Post, "/api/v2/devices/pair", new { code = "12345" }, key: null)), HttpStatusCode.Unauthorized);

        string code = await CodeAsync(site);
        var r = await site.Stranger().SendAsync(Req(HttpMethod.Post, "/api/v2/devices/pair", new { code }, key: null));
        Assert.StartsWith("Too many wrong codes", await Error(r, HttpStatusCode.TooManyRequests));

        clock.Now += Devices.TryWindow + TimeSpan.FromSeconds(1);
        Assert.Equal(HttpStatusCode.OK, (await site.Stranger().SendAsync(Req(HttpMethod.Post, "/api/v2/devices/pair", new { code = await CodeAsync(site) }, key: null))).StatusCode);
    }

    [Fact]
    public async Task A_removed_phone_is_locked_out_on_its_next_request()
    {
        using var dir = new TempDir();
        var (cfg, store) = Library(dir);
        using var _s = store;
        await using var site = await Site(cfg, store);
        string device = await PairAsync(site);
        var phone = site.Stranger();
        Assert.Equal(HttpStatusCode.OK, (await phone.SendAsync(Req(HttpMethod.Get, "/api/v2/library", key: null, device: device))).StatusCode);
        string id = (await Json(await site.Client.SendAsync(Req(HttpMethod.Get, "/api/v2/devices"))))["devices"]![0]!["id"]!.GetValue<string>();

        var left = await Json(await site.Client.SendAsync(Req(HttpMethod.Delete, "/api/v2/devices/" + id)));

        Assert.Empty(left["devices"]!.AsArray());
        Assert.Equal(HttpStatusCode.Unauthorized, (await phone.SendAsync(Req(HttpMethod.Get, "/api/v2/library", key: null, device: device))).StatusCode);
        Assert.False((await Json(await phone.SendAsync(Req(HttpMethod.Get, "/api/v2/me", key: null, device: device))))["paired"]!.GetValue<bool>());
        Assert.Equal(HttpStatusCode.NotFound, (await site.Client.SendAsync(Req(HttpMethod.Delete, "/api/v2/devices/" + id))).StatusCode);
    }

    [Fact]
    public async Task Me_says_whether_a_phone_is_paired_without_a_sign_in()
    {
        using var dir = new TempDir();
        var (cfg, store) = Library(dir);
        using var _s = store;
        await using var site = await Site(cfg, store);

        var me = await Json(await site.Stranger().GetAsync("/api/v2/me"));
        Assert.False(me["paired"]!.GetValue<bool>());
        Assert.Equal(("Sam's library", Engine.Version), (me["library"]!["name"]!.GetValue<string>(), me["library"]!["version"]!.GetValue<string>()));
        Assert.False((await Json(await site.Stranger().SendAsync(Req(HttpMethod.Get, "/api/v2/me", key: null, device: "made-up"))))["paired"]!.GetValue<bool>());
    }

    [Fact]
    public async Task A_phone_that_gives_no_name_is_named_from_its_browser()
    {
        using var dir = new TempDir();
        var (cfg, store) = Library(dir);
        using var _s = store;
        await using var site = await Site(cfg, store);
        var r = Req(HttpMethod.Post, "/api/v2/devices/pair", new { code = await CodeAsync(site) }, key: null);
        r.Headers.UserAgent.ParseAdd("Mozilla/5.0 (iPhone; CPU iPhone OS 19_0 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/19.0 Mobile/15E148 Safari/604.1");
        Assert.Equal("iPhone", (await Json(await site.Stranger().SendAsync(r)))["device"]!["name"]!.GetValue<string>());
    }

    [Theory]
    [InlineData(null, "Phone")]
    [InlineData("Mozilla/5.0 (iPad; CPU OS 19_0 like Mac OS X)", "iPad")]
    [InlineData("Mozilla/5.0 (Linux; Android 16; Pixel 10) AppleWebKit/537.36 Chrome/140.0 Mobile Safari/537.36", "Android phone")]
    [InlineData("Mozilla/5.0 (Linux; Android 16; SM-X910) AppleWebKit/537.36 Chrome/140.0 Safari/537.36", "Android tablet")]
    public void Phones_are_named_from_their_browser(string? userAgent, string name) => Assert.Equal(name, LibraryWeb.PhoneName(userAgent));
}
