using System.Text.Json.Nodes;
using StudyStash.Core.Calendar;

namespace StudyStash.Core.Tests;

/// <summary>The laptop sending its week to the library.</summary>
public class UpcomingSenderTests
{
    static readonly DateTimeOffset Start = new(2026, 9, 28, 9, 0, 0, TimeSpan.FromHours(-4));

    [Fact]
    public async Task The_week_goes_to_the_library_once_until_it_changes_or_half_an_hour_passes()
    {
        var now = Start;
        var posts = new List<(string Url, JsonObject Body, string Key)>();
        Exception? fails = null;
        var host = new LaptopHost
        {
            Clock = () => now,
            Post = (url, body, key) =>
            {
                if (fails is not null) throw fails;
                posts.Add((url, JsonNode.Parse(body)!.AsObject(), key));
                return Task.FromResult(new JsonObject());
            },
        };
        var cc = new ClientConfig("/nowhere");
        List<CalendarEvent> events =
        [
            FakeCalendarSource.Event("over", "c", "s", "Over", Start.AddHours(-3)),
            FakeCalendarSource.Event("a", "c", "s", "CS 101 Lecture", Start.AddHours(1)),
            FakeCalendarSource.Event("far", "c", "s", "Too far", Start.AddDays(8)),
        ];
        var logs = new List<string>();
        var sender = new UpcomingSender(() => events, () => cc, host, () => [new ClassHint("CS 101", [])], logs.Add);
        Assert.False(await sender.SendAsync());
        cc.ServerUrl = "http://library:8787/";
        cc.PoolKey = "pw";
        Assert.True(await sender.SendAsync());
        var (url, body, key) = Assert.Single(posts);
        Assert.Equal("http://library:8787/api/v2/calendar/upcoming", url);
        Assert.Equal("pw", key);
        var sent = Assert.Single(body["events"]!.AsArray())!;
        Assert.Equal("CS 101", sent["class"]!.GetValue<string>());
        Assert.False(await sender.SendAsync());
        now = now.AddMinutes(31);
        Assert.True(await sender.SendAsync());
        events.Add(FakeCalendarSource.Event("b", "c", "s", "BIO 110", Start.AddHours(4)));
        Assert.True(await sender.SendAsync());
        Assert.Equal(3, posts.Count);
        // An unreachable library: said once, sent at the next try.
        fails = new HttpRequestException("down");
        events.Add(FakeCalendarSource.Event("c", "c", "s", "Art", Start.AddHours(5)));
        Assert.False(await sender.SendAsync());
        Assert.False(await sender.SendAsync());
        Assert.Single(logs);
        fails = null;
        Assert.True(await sender.SendAsync());
        Assert.Equal(4, posts.Count);
    }
}
