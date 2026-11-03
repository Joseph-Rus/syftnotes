using System.Net;
using StudyStash.Core.Canvas;

namespace StudyStash.Core.Tests;

/// <summary>Which add-on each browser store serves, asked at setup: what decides whether a student types their school's
/// address or finds Canvas with nothing typed.</summary>
public class StoreVersionsTests
{
    // Trimmed from the real answers (no personal data in either).
    const string GoogleAnswer = """
        <?xml version="1.0" encoding="UTF-8"?><gupdate xmlns="http://www.google.com/update2/response" protocol="2.0" server="prod"><daystart elapsed_days="7245" elapsed_seconds="40443"/><app appid="dfhoenhdjjbnopbepgkgjdgdnfmecjjd" cohort="1::" cohortname="" status="ok"><updatecheck _esbAllowlist="false" codebase="https://clients2.googleusercontent.com/crx/blobs/x/DFHOENHDJJBNOPBEPGKGJDGDNFMECJJD_1_6_0_0.crx" hash_sha256="2fa6" protected="0" size="33908" status="ok" version="1.6"/></app></gupdate>
        """;
    const string GoogleNotThere = """<?xml version="1.0" encoding="UTF-8"?><gupdate xmlns="http://www.google.com/update2/response" protocol="2.0"><app appid="x" status="ok"><updatecheck status="noupdate"/></app></gupdate>""";
    const string MozillaAnswer = """{"id":1,"guid":"canvas@study-stash-app.web.app","slug":"study-stash-for-canvas","current_version":{"id":2,"version":"1.8","compatibility":{"firefox":{"min":"140.0"}}}}""";

    sealed class FakeStores : HttpMessageHandler
    {
        public bool Offline { get; set; }
        public string Google { get; set; } = GoogleAnswer;
        public string Mozilla { get; set; } = MozillaAnswer;
        public List<string> Asked { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Asked.Add(request.RequestUri!.Host);
            if (Offline) throw new HttpRequestException("no network");
            string body = request.RequestUri.Host.Contains("google", StringComparison.Ordinal) ? Google : Mozilla;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });
        }
    }

    static (StoreVersions Stores, FakeStores Fake, Func<DateTimeOffset> Clock, Action<TimeSpan> Pass) Make()
    {
        var fake = new FakeStores();
        var now = new DateTimeOffset(2026, 11, 2, 9, 0, 0, TimeSpan.Zero);
        string home = Directory.CreateTempSubdirectory("ss-stores-").FullName;
        return (new StoreVersions(new HttpClient(fake), Path.Combine(home, "store-versions.json"), () => now), fake, () => now, d => now += d);
    }

    [Fact]
    public void Each_stores_real_answer_gives_its_version_and_an_unpublished_item_gives_none()
    {
        Assert.Equal("1.6", StoreVersions.ParseChrome(GoogleAnswer));
        Assert.Null(StoreVersions.ParseChrome(GoogleNotThere));
        Assert.Equal("1.8", StoreVersions.ParseFirefox(MozillaAnswer));
        Assert.Null(StoreVersions.ParseFirefox("""{"detail":"Not found."}"""));
        Assert.Null(StoreVersions.ParseFirefox("not json"));
    }

    [Theory]
    [InlineData("1.8", true)]
    [InlineData("1.10", true)]
    [InlineData("2.0", true)]
    [InlineData("1.7", false)]
    [InlineData("1.6", false)]
    [InlineData("1.9.1", true)]
    public async Task From_1_8_on_the_store_finds_canvas_by_itself(string served, bool finds)
    {
        var (stores, fake, _, _) = Make();
        fake.Mozilla = MozillaAnswer.Replace("\"1.8\"", $"\"{served}\"", StringComparison.Ordinal);
        Assert.Equal(finds, await stores.FindsCanvasAsync(AddOnStore.Firefox));
    }

    [Fact]
    public async Task Firefox_serving_1_8_finds_canvas_while_chrome_serving_1_6_asks_for_the_address()
    {
        var (stores, _, _, _) = Make();
        Assert.True(await stores.FindsCanvasAsync(AddOnStore.Firefox));
        Assert.False(await stores.FindsCanvasAsync(AddOnStore.Chrome));
    }

    [Fact]
    public async Task An_answer_is_kept_for_a_few_hours_then_asked_again_and_a_new_version_shows()
    {
        var (stores, fake, _, pass) = Make();
        Assert.Equal("1.6", await stores.VersionAsync(AddOnStore.Chrome));
        fake.Google = GoogleAnswer.Replace("version=\"1.6\"", "version=\"1.8\"", StringComparison.Ordinal); // Google approves 1.8
        pass(TimeSpan.FromHours(1));
        Assert.Equal("1.6", await stores.VersionAsync(AddOnStore.Chrome));
        Assert.Single(fake.Asked);
        pass(TimeSpan.FromHours(3));
        Assert.Equal("1.8", await stores.VersionAsync(AddOnStore.Chrome));
        Assert.Equal(2, fake.Asked.Count);
    }

    [Fact]
    public async Task Offline_the_last_answer_stays_and_with_none_it_is_unknown_so_the_address_is_asked()
    {
        var (stores, fake, _, pass) = Make();
        fake.Offline = true;
        Assert.Null(await stores.VersionAsync(AddOnStore.Firefox));
        Assert.False(await stores.FindsCanvasAsync(AddOnStore.Firefox));

        fake.Offline = false;
        Assert.True(await stores.FindsCanvasAsync(AddOnStore.Firefox));
        fake.Offline = true;
        pass(TimeSpan.FromDays(2));
        Assert.Equal("1.8", await stores.VersionAsync(AddOnStore.Firefox)); // the one heard last
    }

    [Fact]
    public async Task The_setting_for_trying_either_flow_wins_over_the_store()
    {
        var (stores, fake, _, _) = Make();
        fake.Offline = true;
        try
        {
            Environment.SetEnvironmentVariable("STUDYSTASH_ADDON_FINDS_CANVAS", "1");
            Assert.True(await stores.FindsCanvasAsync(AddOnStore.Chrome));
            Environment.SetEnvironmentVariable("STUDYSTASH_ADDON_FINDS_CANVAS", "0");
            fake.Offline = false;
            Assert.False(await stores.FindsCanvasAsync(AddOnStore.Firefox)); // though Firefox serves 1.8
        }
        finally
        {
            Environment.SetEnvironmentVariable("STUDYSTASH_ADDON_FINDS_CANVAS", null);
        }
    }
}
