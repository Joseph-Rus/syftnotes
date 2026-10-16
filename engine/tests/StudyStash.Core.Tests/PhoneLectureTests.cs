using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using StudyStash.Library;

namespace StudyStash.Core.Tests;

/// <summary>A lecture the iPhone app recorded and wrote down itself, sent to POST /api/v2/lectures.</summary>
public class PhoneLectureTests
{
    const string Id = "phone-20261015-091500-a1b2c3";

    static HttpRequestMessage Post(object body, string? device = null, string? key = null)
    {
        var r = new HttpRequestMessage(HttpMethod.Post, "/api/v2/lectures") { Content = JsonContent.Create(body) };
        if (device is not null) r.Headers.Add("Cookie", $"{Devices.Cookie}={device}");
        if (key is not null) r.Headers.Authorization = new("Bearer", key);
        return r;
    }

    [Fact]
    public async Task A_paired_phone_sends_a_lecture_and_the_library_files_it()
    {
        using var dir = new TempDir();
        var (cfg, store) = PhoneApiTests.Library(dir);
        using var _s = store;
        await using var site = await PhoneApiTests.Site(cfg, store);
        string phone = await PhoneApiTests.PairAsync(site, "Sam's iPhone");

        var lecture = new
        {
            id = Id, @class = "CS 101", date = "2026-10-15T09:15:00-07:00", seconds = 3125.4, language = "en-US",
            transcript = "[00:00] Today we start loops.\n[00:07] A for loop counts.", notes = "- ask about while loops",
        };
        var r = await site.Stranger().SendAsync(Post(lecture, device: phone));
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var reply = JsonNode.Parse(await r.Content.ReadAsStringAsync())!;
        Assert.Equal((Id, "queued", "CS 101", true),
            (reply["id"]!.GetValue<string>(), reply["status"]!.GetValue<string>(), reply["class_name"]!.GetValue<string>(), reply["has_transcript"]!.GetValue<bool>()));

        var row = store.Get(Id)!;
        Assert.Equal("queued", row.Status);
        var m = store.Meeting(row);
        Assert.Equal("CS 101, Thu 15 Oct", m.Title);
        Assert.Equal("Sam's iPhone", m.Owner);
        Assert.Equal("- ask about while loops", m.PrivateNotes);
        Assert.Contains("A for loop counts.", m.Transcript);
        Assert.Equal("phone", m.Raw["source"]!.GetValue<string>());
        Assert.Equal(3125.4, m.Raw["seconds"]!.GetValue<double>());

        // Sent again (the connection dropped before the reply): the same lecture, not a second one.
        Assert.Equal(HttpStatusCode.OK, (await site.Stranger().SendAsync(Post(lecture, device: phone))).StatusCode);
        Assert.Equal("queued", store.Get(Id)!.Status);
        Assert.Equal(1, store.StatusCounts().Values.Sum());
    }

    [Fact]
    public async Task A_typed_title_stays()
    {
        var payload = LibraryWeb.PhoneLecturePayload(new JsonObject
        {
            ["id"] = Id, ["title"] = "Midterm review", ["title_typed"] = true, ["transcript"] = "[00:00] Hi", ["date"] = "2026-10-15T09:15:00Z",
        }, "iPhone")!;
        Assert.Equal("Midterm review", payload["title"]!.GetValue<string>());
        Assert.Equal(Meeting.ByHand, payload["raw"]![Meeting.TitleBy]!.GetValue<string>());
        Assert.Equal("iPhone", payload["owner"]!.GetValue<string>());

        var untitled = LibraryWeb.PhoneLecturePayload(new JsonObject
        {
            ["id"] = Id, ["transcript"] = "[00:00] Hi", ["date"] = "2026-10-10T16:12:00Z",
        }, null)!;
        Assert.Equal("Recording, Sat 10 Oct at 4:12 PM", untitled["title"]!.GetValue<string>());
        Assert.Null(untitled["raw"]![Meeting.TitleBy]);
        await Task.CompletedTask;
    }

    [Theory]
    [InlineData("rec-20261015-091500-a1b2c3")]  // a laptop's lecture: a phone can't overwrite it
    [InlineData("phone-../../state")]
    [InlineData("")]
    public async Task Only_a_phone_id_with_a_transcript_is_taken(string id)
    {
        using var dir = new TempDir();
        var (cfg, store) = PhoneApiTests.Library(dir);
        using var _s = store;
        await using var site = await PhoneApiTests.Site(cfg, store);
        string phone = await PhoneApiTests.PairAsync(site);

        var r = await site.Stranger().SendAsync(Post(new { id, transcript = "[00:00] Hi" }, device: phone));
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        var noTranscript = await site.Stranger().SendAsync(Post(new { id = Id }, device: phone));
        Assert.Equal(HttpStatusCode.BadRequest, noTranscript.StatusCode);
        Assert.Null(store.Get(Id));
    }

    [Fact]
    public async Task A_stranger_is_refused()
    {
        using var dir = new TempDir();
        var (cfg, store) = PhoneApiTests.Library(dir);
        using var _s = store;
        await using var site = await PhoneApiTests.Site(cfg, store);
        var r = await site.Stranger().SendAsync(Post(new { id = Id, transcript = "[00:00] Hi" }));
        Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
        Assert.Null(store.Get(Id));
    }
}
