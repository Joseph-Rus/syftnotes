using System.Net;
using System.Text.RegularExpressions;
using StudyStash.Library;

namespace StudyStash.Core.Tests;

/// <summary>"Add a phone" on the library's own Settings page: the QR code and the code, then the paired phones with
/// Remove, and Tailscale's problem in words when there's no https address.</summary>
public class PhoneSettingsPageTests
{
    [Fact]
    public async Task Add_a_phone_shows_the_qr_code_and_a_code_that_pairs()
    {
        using var dir = new TempDir();
        var (cfg, store) = PhoneApiTests.Library(dir);
        using var _s = store;
        await using var site = await PhoneApiTests.Site(cfg, store);
        await site.PostForm("/login", ("password", "pw"), ("next", "/"));

        string before = await site.Text("/settings");
        Assert.Contains("id=\"phone\"", before);
        Assert.Contains("Read your notes and lectures on your phone", before);

        var r = await site.PostForm("/settings/phone");
        Assert.Equal((HttpStatusCode.SeeOther, "/settings?phone=new#phone"), (r.StatusCode, r.Headers.Location!.OriginalString));
        string page = await site.Text("/settings?phone=new");
        Assert.Contains("<svg xmlns=\"http://www.w3.org/2000/svg\"", page);
        // The address a person could type: the Tailscale IP's (the QR code also offers the https name).
        Assert.Contains($"http://100.64.0.9:{cfg.WebPort}/app/</div>", page);
        var shown = Regex.Match(page, @">(\d{3}) (\d{3})</div>");
        Assert.True(shown.Success);

        var pair = await site.Stranger().PostAsync("/api/v2/devices/pair",
            System.Net.Http.Json.JsonContent.Create(new { code = shown.Groups[1].Value + shown.Groups[2].Value, name = "Pixel 10" }));
        Assert.Equal(HttpStatusCode.OK, pair.StatusCode);

        string after = await site.Text("/settings");
        Assert.Contains("Pixel 10", after);
        Assert.Contains("Add another phone", after);
        Assert.DoesNotContain("<svg xmlns", after); // the code is shown once, on the page right after
    }

    [Fact]
    public async Task Remove_locks_the_phone_out()
    {
        using var dir = new TempDir();
        var (cfg, store) = PhoneApiTests.Library(dir);
        using var _s = store;
        await using var site = await PhoneApiTests.Site(cfg, store);
        string device = await PhoneApiTests.PairAsync(site, "Old phone");
        await site.PostForm("/login", ("password", "pw"), ("next", "/"));
        string page = await site.Text("/settings");
        string action = Regex.Match(page, "action=\"(/settings/phone/[^\"]+/remove)\"").Groups[1].Value;

        Assert.Equal(HttpStatusCode.SeeOther, (await site.PostForm(action)).StatusCode);

        Assert.DoesNotContain("Old phone", await site.Text("/settings"));
        var ask = new HttpRequestMessage(HttpMethod.Get, "/api/v2/library");
        ask.Headers.Add("Cookie", $"device={device}");
        Assert.Equal(HttpStatusCode.Unauthorized, (await site.Stranger().SendAsync(ask)).StatusCode);
    }

    [Fact]
    public async Task Without_tailscale_the_page_says_what_to_do()
    {
        using var dir = new TempDir();
        var (cfg, store) = PhoneApiTests.Library(dir);
        using var _s = store;
        await using var site = await PhoneApiTests.Site(cfg, store, reach: new ClaudeReach());
        await site.PostForm("/login", ("password", "pw"), ("next", "/"));

        await site.PostForm("/settings/phone");
        string page = await site.Text("/settings?phone=new");

        Assert.Contains("Your phone reaches the library over Tailscale", page);
        Assert.Contains("tailscale.com/download", page);
        Assert.DoesNotContain("<svg xmlns", page);
    }

    [Fact]
    public async Task Only_someone_signed_in_can_add_or_remove_a_phone()
    {
        using var dir = new TempDir();
        var (cfg, store) = PhoneApiTests.Library(dir);
        using var _s = store;
        await using var site = await PhoneApiTests.Site(cfg, store);
        Assert.StartsWith("/login", (await site.PostForm("/settings/phone")).Headers.Location!.OriginalString);
        Assert.StartsWith("/login", (await site.PostForm("/settings/phone/x/remove")).Headers.Location!.OriginalString);
    }
}
