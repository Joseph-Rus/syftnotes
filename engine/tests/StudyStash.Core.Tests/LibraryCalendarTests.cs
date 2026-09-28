using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using StudyStash.Core.Calendar;
using StudyStash.Library;

namespace StudyStash.Core.Tests;

/// <summary>The library's copy of what's coming up: the laptop's POST, the GET with each event's class.</summary>
public class LibraryCalendarTests
{
    static readonly DateTimeOffset Now = new(2026, 9, 28, 9, 0, 0, TimeSpan.FromHours(-4));
    static readonly List<ClassHint> Classes = [new("CS 101", []), new("Biology 110", [])];

    static JsonObject Ev(string id, string title, DateTimeOffset start, int minutes = 60, string? cls = null, bool allDay = false) => new()
    {
        ["id"] = id, ["title"] = title, ["start"] = start.ToString("o"), ["end"] = start.AddMinutes(minutes).ToString("o"),
        ["allDay"] = allDay, ["location"] = "Hall 1", ["class"] = cls,
    };

    [Fact]
    public void A_laptop_s_week_is_kept_and_read_back_soonest_first_with_classes()
    {
        using var dir = new TempDir();
        int kept = LibraryCalendar.Save(dir.Path, new JsonObject
        {
            ["events"] = new JsonArray(
                Ev("b", "BIO 110 study group", Now.AddHours(5)),
                Ev("a", "CS 101 Lecture", Now.AddHours(1)),
                Ev("over", "Earlier", Now.AddHours(-3)),
                Ev("x", "Office hours", Now.AddHours(2), cls: "CS 101"),
                Ev("y", "Something", Now.AddHours(3), cls: "Not a class here"),
                new JsonObject { ["id"] = "bad", ["title"] = "No times" },
                new JsonObject { ["title"] = "No id", ["start"] = Now.ToString("o"), ["end"] = Now.ToString("o") },
                Ev("backwards", "Ends before it starts", Now.AddHours(1), minutes: -30)),
        }, Now);
        Assert.Equal(5, kept);
        var read = LibraryCalendar.Read(dir.Path, Classes, Now);
        var events = read["events"]!.AsArray().Select(e => e!.AsObject()).ToList();
        Assert.Equal(["a", "x", "y", "b"], events.Select(e => e["id"]!.GetValue<string>()));
        Assert.Equal("CS 101", events[0]["class"]!.GetValue<string>());
        // The laptop's match stands when the library has that class and finds none itself; otherwise null.
        Assert.Equal("CS 101", events[1]["class"]!.GetValue<string>());
        Assert.Null(events[2]["class"]);
        Assert.Equal("Biology 110", events[3]["class"]!.GetValue<string>());
        Assert.Equal("Hall 1", events[0]["location"]!.GetValue<string>());
        Assert.False(events[0]["allDay"]!.GetValue<bool>());
        Assert.Equal(["id", "title", "start", "end", "allDay", "location", "class"], events[0].Select(kv => kv.Key));
        Assert.Equal("2026-09-28T09:00:00-04:00", read["updated"]!.GetValue<string>());
    }

    [Fact]
    public void Nothing_sent_yet_is_no_events_and_no_update_and_too_many_are_cut()
    {
        using var dir = new TempDir();
        var empty = LibraryCalendar.Read(dir.Path, Classes, Now);
        Assert.Empty(empty["events"]!.AsArray());
        Assert.Null(empty["updated"]);
        var many = new JsonArray([.. Enumerable.Range(0, LibraryCalendar.MaxEvents + 20).Select(i => (JsonNode)Ev($"e{i}", "x", Now.AddMinutes(i)))]);
        Assert.Equal(LibraryCalendar.MaxEvents, LibraryCalendar.Save(dir.Path, new JsonObject { ["events"] = many }, Now));
        File.WriteAllText(LibraryCalendar.PathIn(dir.Path), "{broken");
        Assert.Empty(LibraryCalendar.Read(dir.Path, Classes, Now)["events"]!.AsArray());
    }

    [Fact]
    public void The_laptop_sends_its_events_with_its_own_matches()
    {
        var e = new CalendarEvent("uid/1", "cal", "ics:abc", "CS 101 Lecture", Now, Now.AddHours(1), false, "Hall", [], null, false);
        var body = LibraryCalendar.Body([e], Classes);
        var sent = body["events"]![0]!;
        Assert.Equal("ics:abc|uid/1", sent["id"]!.GetValue<string>());
        Assert.Equal("CS 101", sent["class"]!.GetValue<string>());
        using var dir = new TempDir();
        Assert.Equal(1, LibraryCalendar.Save(dir.Path, body, Now));
        Assert.Equal("CS 101", LibraryCalendar.Read(dir.Path, [new("CS 101", [])], Now)["events"]![0]!["class"]!.GetValue<string>());
    }

    static HttpRequestMessage Req(HttpMethod m, string path, object? body = null, string key = "pw")
    {
        var r = new HttpRequestMessage(m, path);
        r.Headers.Authorization = new("Bearer", key);
        if (body is not null) r.Content = JsonContent.Create(body);
        return r;
    }

    [Fact]
    public async Task The_api_takes_the_laptop_s_week_and_serves_it_with_the_library_s_classes()
    {
        using var dir = new TempDir();
        var cfg = new Config(dir["home"], dir["pool"]) { PoolPassword = "pw", OllamaEnabled = false, Classes = [new ClassDef("Software Engineering", ["SE"])] };
        Directory.CreateDirectory(cfg.Home);
        Configs.Save(cfg);
        Canvas.CanvasSettings.Update(cfg.Home, c =>
        {
            c.Courses["Software Engineering"] = 4242;
            c.CourseInfo["4242"] = new Canvas.CourseInfo("202710.TS.CSCI321.A", "Software Engineering", "Fall 2026");
        });
        var store = new Store(cfg.DbPath, cfg.PoolDir);
        await using var site = await TestSite.StartAsync(b => LibraryWeb.Build(b, cfg, store, new Pipeline(cfg, store, log: _ => { }),
            new LibraryWebOptions { Tailscale = () => new TailscaleInfo(false, false, "", "", []) }));
        var soon = DateTimeOffset.UtcNow.AddHours(1);
        var body = new JsonObject { ["events"] = new JsonArray(Ev("1", "CSCI 321 lecture", soon), Ev("2", "Dentist", soon.AddHours(2))) };
        var posted = await site.Client.SendAsync(Req(HttpMethod.Post, "/api/v2/calendar/upcoming", body));
        Assert.Equal(HttpStatusCode.OK, posted.StatusCode);
        Assert.Equal(2, JsonNode.Parse(await posted.Content.ReadAsStringAsync())!["kept"]!.GetValue<int>());
        var got = JsonNode.Parse(await (await site.Client.SendAsync(Req(HttpMethod.Get, "/api/v2/calendar/upcoming"))).Content.ReadAsStringAsync())!;
        Assert.Equal("Software Engineering", got["events"]![0]!["class"]!.GetValue<string>());
        Assert.Null(got["events"]![1]!["class"]);
        Assert.NotNull(got["updated"]);
        // Without the password: nothing, either way.
        Assert.Equal(HttpStatusCode.Unauthorized, (await site.Client.SendAsync(Req(HttpMethod.Get, "/api/v2/calendar/upcoming", key: "nope"))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await site.Client.SendAsync(Req(HttpMethod.Post, "/api/v2/calendar/upcoming", body, key: "nope"))).StatusCode);
        // A body without events is refused.
        Assert.Equal(HttpStatusCode.BadRequest, (await site.Client.SendAsync(Req(HttpMethod.Post, "/api/v2/calendar/upcoming", new { nothing = 1 }))).StatusCode);
    }
}
