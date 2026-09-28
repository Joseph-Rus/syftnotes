using System.Net;
using StudyStash.Core.Calendar;
using StudyStash.Core.Calendar.Ics;

namespace StudyStash.Core.Tests;

/// <summary>A pretend web server for calendar feeds: each address answers what the test says, and counts its asks.</summary>
public sealed class FeedServer : HttpMessageHandler
{
    public Dictionary<string, Func<HttpResponseMessage>> Answers { get; } = [];
    public List<string> Asked { get; } = [];

    public static string Fixture(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "calendar", name));

    public FeedServer Serve(string url, string text)
    {
        Answers[url] = () => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(text) };
        return this;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        string url = request.RequestUri!.ToString();
        Asked.Add(url);
        if (Answers.TryGetValue(url, out var answer)) return Task.FromResult(answer());
        throw new HttpRequestException("no route to host");
    }
}

/// <summary>A pasted feed: its address, its id, downloading it, and what goes wrong in words.</summary>
public class IcsSourceTests
{
    static readonly TimeZoneInfo NewYork = IcsZones.SystemZone("America/New_York")!;

    [Fact]
    public void Addresses_are_tidied_and_webcal_is_https()
    {
        Assert.Equal("https://p01-caldav.icloud.com/published/2/abc", IcsSource.FetchUrl("webcal://p01-caldav.icloud.com/published/2/abc"));
        Assert.Equal("https://x.edu/a.ics", IcsSource.FetchUrl("WEBCALS://x.edu/a.ics"));
        Assert.Equal("https://calendar.google.com/calendar/ical/x/basic.ics", IcsSource.CleanUrl("  calendar.google.com/calendar/ical/x/basic.ics "));
        Assert.Equal("webcal://x.edu/a.ics", IcsSource.CleanUrl("webcal://x.edu/a.ics"));
        Assert.Null(IcsSource.CleanUrl(""));
        Assert.Null(IcsSource.CleanUrl("not a url"));
        Assert.Null(IcsSource.CleanUrl("ftp://x.edu/a.ics"));
        Assert.Null(IcsSource.CleanUrl("localhost"));
    }

    [Fact]
    public void One_feed_is_one_id_however_it_s_written()
    {
        Assert.Equal(IcsSource.IdFor("webcal://X.edu/a.ics"), IcsSource.IdFor("https://x.edu/a.ics"));
        Assert.NotEqual(IcsSource.IdFor("https://x.edu/a.ics"), IcsSource.IdFor("https://x.edu/b.ics"));
        Assert.Matches("^ics:[0-9a-f]{12}$", IcsSource.IdFor("https://x.edu/a.ics"));
        var s = IcsSource.New("https://x.edu/a.ics", null, DateTimeOffset.UnixEpoch);
        Assert.Equal("x.edu", s.Name);
        Assert.Equal("ics", s.Kind);
        Assert.Equal("Timetable", IcsSource.New("https://x.edu/a.ics", "Timetable", DateTimeOffset.UnixEpoch).Name);
    }

    [Fact]
    public async Task A_feed_lists_one_calendar_and_its_events_and_is_read_once_a_minute()
    {
        var server = new FeedServer().Serve("https://calendar.google.com/basic.ics", FeedServer.Fixture("google.ics"));
        var now = new DateTimeOffset(2026, 9, 28, 12, 0, 0, TimeSpan.Zero);
        var settings = IcsSource.New("webcal://calendar.google.com/basic.ics", null, now);
        var source = new IcsSource(settings, new HttpClient(server), NewYork, () => now);
        var cal = Assert.Single(await source.ListCalendarsAsync(default));
        Assert.Equal(new CalendarInfo(settings.Id, settings.Id, "Fall 2026 classes", null, true), cal);
        var events = await source.EventsAsync([cal.Id], now, now.AddDays(7), default);
        Assert.Contains(events, e => e.Title == "CS 101 Lecture");
        Assert.All(events, e => Assert.Equal(settings.Id, e.SourceId));
        Assert.Empty(await source.EventsAsync(["another"], now, now.AddDays(7), default));
        Assert.Single(server.Asked);
        now = now.AddMinutes(2);
        await source.ListCalendarsAsync(default);
        Assert.Equal(2, server.Asked.Count);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "isn't public")]
    [InlineData(HttpStatusCode.NotFound, "isn't there any more")]
    [InlineData(HttpStatusCode.BadGateway, "didn't answer (502)")]
    public async Task What_the_server_says_becomes_words(HttpStatusCode status, string words)
    {
        var server = new FeedServer();
        server.Answers["https://x.edu/a.ics"] = () => new HttpResponseMessage(status);
        var e = await Assert.ThrowsAsync<CalendarFeedException>(() => IcsSource.DownloadAsync("https://x.edu/a.ics", new HttpClient(server), null, default));
        Assert.Contains(words, e.Message);
    }

    [Fact]
    public async Task A_page_that_isn_t_a_calendar_or_no_answer_at_all_says_so()
    {
        var server = new FeedServer().Serve("https://x.edu/login", "<html>Sign in</html>");
        var notCal = await Assert.ThrowsAsync<CalendarFeedException>(() => IcsSource.DownloadAsync("https://x.edu/login", new HttpClient(server), null, default));
        Assert.Contains("isn't a calendar feed", notCal.Message);
        var gone = await Assert.ThrowsAsync<CalendarFeedException>(() => IcsSource.DownloadAsync("https://nowhere.edu/a.ics", new HttpClient(server), null, default));
        Assert.Contains("Couldn't reach", gone.Message);
    }

    [Fact]
    public async Task A_feed_too_big_to_read_is_refused()
    {
        var server = new FeedServer();
        server.Answers["https://x.edu/big.ics"] = () => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[IcsSource.MaxBytes + 1]) };
        var e = await Assert.ThrowsAsync<CalendarFeedException>(() => IcsSource.DownloadAsync("https://x.edu/big.ics", new HttpClient(server), null, default));
        Assert.Contains("too big", e.Message);
    }

    [Fact]
    public void The_registry_knows_feeds_and_takes_new_kinds_with_their_buttons()
    {
        var kinds = CalendarKinds.WithFeeds(new HttpClient(new FeedServer()));
        Assert.Empty(kinds.Connectable);
        Assert.IsType<IcsSource>(kinds.Create(IcsSource.New("https://x.edu/a.ics", null, DateTimeOffset.UnixEpoch)));
        Assert.Null(kinds.Create(new CalendarSourceSettings { Id = "apple", Kind = "apple" }));
        int changed = 0;
        kinds.Changed += () => changed++;
        kinds.Register(new CalendarKind("google", "Google Calendar", s => throw new NotImplementedException())
        {
            ConnectLabel = "Connect Google", Connect = _ => Task.FromResult<CalendarSourceSettings?>(null),
        });
        Assert.Equal("Connect Google", Assert.Single(kinds.Connectable).ConnectLabel);
        // Registering a kind again replaces it.
        kinds.Register(new CalendarKind("google", "Google", s => throw new NotImplementedException()));
        Assert.Empty(kinds.Connectable);
        Assert.Equal(["ics", "google"], kinds.All.Select(k => k.Kind));
        Assert.Equal(2, changed);
        Assert.NotNull(CalendarKinds.Default.Find("ics"));
    }
}
