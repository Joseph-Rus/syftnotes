using System.Net;
using StudyStash.App.ViewModels;
using StudyStash.Core.Calendar;
using StudyStash.Core.Calendar.Ics;

namespace StudyStash.App.Tests;

/// <summary>Settings → Calendars: a fake calendar server for whatever the test asks a feed for.</summary>
sealed class FakeFeeds : HttpMessageHandler
{
    public Dictionary<string, string> Feeds { get; } = [];
    public Dictionary<string, HttpStatusCode> Failures { get; } = [];

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        string url = request.RequestUri!.ToString();
        if (Failures.TryGetValue(url, out var code)) return Task.FromResult(new HttpResponseMessage(code));
        if (Feeds.TryGetValue(url, out string? text)) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(text) });
        throw new HttpRequestException("no route to host");
    }
}

/// <summary>Settings → Calendars: adding a feed, the switch per calendar it turns up, Remove, and the words shown
/// when a pasted address isn't one, is already added, or the feed can't be read.</summary>
public class CalendarSettingsModelTests
{
    const string Ics = "BEGIN:VCALENDAR\r\nVERSION:2.0\r\nX-WR-CALNAME:Fall 2026 classes\r\nBEGIN:VEVENT\r\nUID:1\r\nDTSTART:20260101T100000Z\r\nDTEND:20260101T110000Z\r\nSUMMARY:CS 101 Lecture\r\nEND:VEVENT\r\nEND:VCALENDAR\r\n";

    static CalendarKinds Kinds(FakeFeeds feeds) => CalendarKinds.WithFeeds(new HttpClient(feeds));

    [Fact]
    public async Task Adding_a_feed_names_it_from_the_feed_and_lists_its_calendar_with_a_switch()
    {
        using var home = new TempHome();
        var feeds = new FakeFeeds();
        feeds.Feeds["https://x.edu/a.ics"] = Ics;
        int woke = 0;
        var model = new CalendarSettingsModel(home.Path, Kinds(feeds), () => woke++);

        model.FeedUrl = "https://x.edu/a.ics";
        await model.AddFeedCommand.ExecuteAsync(null);

        var source = Assert.Single(model.Sources);
        Assert.Equal("Fall 2026 classes", source.Name);
        var cal = Assert.Single(source.Calendars);
        Assert.True(cal.On);
        Assert.Equal("", model.FeedUrl);
        Assert.Null(model.FeedError);
        Assert.Equal(1, woke);
    }

    [Fact]
    public async Task A_pasted_address_that_isn_t_one_says_so_and_adds_nothing()
    {
        using var home = new TempHome();
        var model = new CalendarSettingsModel(home.Path, Kinds(new FakeFeeds()));

        model.FeedUrl = "not a url";
        await model.AddFeedCommand.ExecuteAsync(null);

        Assert.Equal(CalendarWords.NotAnAddress, model.FeedError);
        Assert.Empty(model.Sources);
    }

    [Fact]
    public async Task The_same_feed_twice_is_said_once()
    {
        using var home = new TempHome();
        var feeds = new FakeFeeds();
        feeds.Feeds["https://x.edu/a.ics"] = Ics;
        var model = new CalendarSettingsModel(home.Path, Kinds(feeds));
        model.FeedUrl = "https://x.edu/a.ics";
        await model.AddFeedCommand.ExecuteAsync(null);

        model.FeedUrl = "https://x.edu/a.ics";
        await model.AddFeedCommand.ExecuteAsync(null);

        Assert.Equal(CalendarWords.AlreadyAdded, model.FeedError);
        Assert.Single(model.Sources);
    }

    [Fact]
    public async Task A_feed_that_can_t_be_read_says_why_in_plain_words()
    {
        using var home = new TempHome();
        var feeds = new FakeFeeds();
        feeds.Failures["https://x.edu/gone.ics"] = HttpStatusCode.NotFound;
        var model = new CalendarSettingsModel(home.Path, Kinds(feeds));

        model.FeedUrl = "https://x.edu/gone.ics";
        await model.AddFeedCommand.ExecuteAsync(null);

        Assert.Equal("That calendar isn't there any more. Check its address.", model.FeedError);
        Assert.Empty(model.Sources);
    }

    [Fact]
    public async Task Turning_a_calendar_off_saves_at_once_and_wakes_the_read()
    {
        using var home = new TempHome();
        var feeds = new FakeFeeds();
        feeds.Feeds["https://x.edu/a.ics"] = Ics;
        int woke = 0;
        var model = new CalendarSettingsModel(home.Path, Kinds(feeds), () => woke++);
        model.FeedUrl = "https://x.edu/a.ics";
        await model.AddFeedCommand.ExecuteAsync(null);
        var cal = model.Sources[0].Calendars[0];

        cal.On = false;

        var saved = CalendarSettings.Load(home.Path);
        Assert.False(saved.IsEnabled(saved.Calendars[0]));
        Assert.Equal(2, woke);
    }

    [Fact]
    public async Task Remove_takes_the_source_and_its_calendars_away()
    {
        using var home = new TempHome();
        var feeds = new FakeFeeds();
        feeds.Feeds["https://x.edu/a.ics"] = Ics;
        int woke = 0;
        var model = new CalendarSettingsModel(home.Path, Kinds(feeds), () => woke++);
        model.FeedUrl = "https://x.edu/a.ics";
        await model.AddFeedCommand.ExecuteAsync(null);
        var source = model.Sources[0];

        source.RemoveCommand.Execute(null);

        Assert.Empty(model.Sources);
        Assert.Empty(CalendarSettings.Load(home.Path).Sources);
        Assert.Equal(2, woke);
    }

    [Fact]
    public void With_no_connectable_kind_the_coming_soon_note_shows_instead()
    {
        using var home = new TempHome();
        var model = new CalendarSettingsModel(home.Path, Kinds(new FakeFeeds()));

        Assert.False(model.HasConnectable);
        Assert.Empty(model.Connectable);
    }
}
