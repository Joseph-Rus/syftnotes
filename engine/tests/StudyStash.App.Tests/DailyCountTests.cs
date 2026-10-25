using System.Net;
using System.Text.Json;
using StudyStash.App.Services;

namespace StudyStash.App.Tests;

public class DailyCountTests
{
    sealed class Fake(HttpStatusCode answer = HttpStatusCode.OK) : HttpMessageHandler
    {
        public List<(string Url, string Body)> Sent { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            Sent.Add((r.RequestUri!.ToString(), await r.Content!.ReadAsStringAsync(ct)));
            return new HttpResponseMessage(answer);
        }
    }

    static DailyCount Make(TempHome home, Fake fake, Func<bool>? on = null, DateTime? day = null) =>
        new(home.Path, on ?? (() => true), new HttpClient(fake), "9.9.9", () => day ?? new DateTime(2026, 10, 25, 9, 0, 0));

    [Fact]
    public async Task The_body_sent_is_exactly_the_documents_shape()
    {
        using var home = new TempHome();
        var fake = new Fake();
        var count = Make(home, fake);

        Assert.True(await count.RoundAsync(TestContext.Current.CancellationToken));

        var (url, body) = Assert.Single(fake.Sent);
        Assert.Equal($"https://firestore.googleapis.com/v1/projects/study-stash-app/databases/(default)/documents/pings?documentId={count.Code()}_2026-10-25", url);
        string p = OperatingSystem.IsMacOS() ? "mac" : OperatingSystem.IsWindows() ? "windows" : "linux";
        Assert.Equal($$$$"""{"fields":{"d":{"stringValue":"2026-10-25"},"v":{"stringValue":"9.9.9"},"p":{"stringValue":"{{{{p}}}}"}}}""", body);
        using var _ = JsonDocument.Parse(body);
    }

    [Fact]
    public async Task It_counts_once_a_day_and_a_409_is_done_too()
    {
        using var home = new TempHome();
        var fake = new Fake(HttpStatusCode.Conflict);

        await Make(home, fake).RoundAsync(TestContext.Current.CancellationToken);
        await Make(home, fake).RoundAsync(TestContext.Current.CancellationToken);
        Assert.Single(fake.Sent);

        await Make(home, fake, day: new DateTime(2026, 10, 26)).RoundAsync(TestContext.Current.CancellationToken);
        Assert.Equal(2, fake.Sent.Count);
    }

    [Fact]
    public async Task A_failed_try_is_tried_again()
    {
        using var home = new TempHome();
        var fake = new Fake(HttpStatusCode.Forbidden);
        var count = Make(home, fake);

        Assert.False(await count.RoundAsync(TestContext.Current.CancellationToken));
        Assert.False(await count.RoundAsync(TestContext.Current.CancellationToken));
        Assert.Equal(2, fake.Sent.Count);
    }

    [Fact]
    public async Task Off_sends_nothing_and_deletes_the_code()
    {
        using var home = new TempHome();
        var fake = new Fake();
        bool on = true;
        var count = Make(home, fake, () => on);
        await count.RoundAsync(TestContext.Current.CancellationToken);
        Assert.True(File.Exists(home["count-code"]));

        on = false;
        fake.Sent.Clear();
        await count.RoundAsync(TestContext.Current.CancellationToken);

        Assert.Empty(fake.Sent);
        Assert.False(File.Exists(home["count-code"]));
    }

    [Fact]
    public void The_code_is_32_hex_characters_and_stable_across_restarts()
    {
        using var home = new TempHome();
        string code = Make(home, new Fake()).Code();

        Assert.Matches("^[0-9a-f]{32}$", code);
        Assert.Equal(code, Make(home, new Fake()).Code());
    }
}
