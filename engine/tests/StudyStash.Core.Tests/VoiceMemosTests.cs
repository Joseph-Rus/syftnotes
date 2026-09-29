using System.Net;
using System.Net.Http.Headers;
using System.Text.Json.Nodes;
using StudyStash.Core;
using StudyStash.Library;

namespace StudyStash.Core.Tests;

/// <summary>Voice memos from the phone: sent to the library, taken by one computer that records, fetched, and
/// finished as a lecture (or failed, and tried again); a memo taken and never finished goes back to waiting.</summary>
public class VoiceMemosTests
{
    static HttpRequestMessage Req(HttpMethod m, string path, HttpContent? content = null, string? device = null)
    {
        var r = new HttpRequestMessage(m, path) { Content = content };
        if (device is null) r.Headers.Authorization = new("Bearer", "pw");
        else r.Headers.Add("Cookie", $"{Devices.Cookie}={device}");
        return r;
    }

    static MultipartFormDataContent Memo(string name = "Lecture 3.m4a", byte[]? bytes = null, string? cls = null, string? title = null)
    {
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes ?? [0, 0, 0, 0x18, (byte)'f', (byte)'t', (byte)'y', (byte)'p', (byte)'M', (byte)'4', (byte)'A', (byte)' ']);
        file.Headers.ContentType = new MediaTypeHeaderValue("audio/x-m4a");
        form.Add(file, "file", name);
        if (cls is not null) form.Add(new StringContent(cls), "class");
        if (title is not null) form.Add(new StringContent(title), "title");
        return form;
    }

    static async Task<JsonObject> Json(HttpResponseMessage r)
    {
        Assert.True(r.IsSuccessStatusCode, $"{(int)r.StatusCode}: {await r.Content.ReadAsStringAsync()}");
        return (JsonObject)JsonNode.Parse(await r.Content.ReadAsStringAsync())!;
    }

    [Fact]
    public async Task A_memo_waits_is_taken_by_one_computer_and_becomes_a_lecture()
    {
        using var dir = new TempDir();
        var (cfg, store) = PhoneApiTests.Library(dir);
        using var _s = store;
        await using var site = await PhoneApiTests.Site(cfg, store);

        var sent = await Json(await site.Client.SendAsync(Req(HttpMethod.Post, "/api/v2/voice-memos", Memo(cls: "CS 101", title: "Recursion, part 2"))));
        string id = sent["id"]!.GetValue<string>();
        Assert.Equal(("waiting", "CS 101", "Recursion, part 2", "Lecture 3.m4a"),
            (sent["state"]!.GetValue<string>(), sent["class"]!.GetValue<string>(), sent["title"]!.GetValue<string>(), sent["name"]!.GetValue<string>()));

        var list = await Json(await site.Client.SendAsync(Req(HttpMethod.Get, "/api/v2/voice-memos")));
        Assert.Equal(id, list["memos"]!.AsArray().Single()!["id"]!.GetValue<string>());

        // The recording comes back byte for byte.
        var audio = await site.Client.SendAsync(Req(HttpMethod.Get, $"/api/v2/voice-memos/{id}/audio"));
        Assert.Equal(HttpStatusCode.OK, audio.StatusCode);
        Assert.Equal(12, (await audio.Content.ReadAsByteArrayAsync()).Length);

        // One computer takes it; a second can't.
        var claimed = await Json(await site.Client.SendAsync(Req(HttpMethod.Post, $"/api/v2/voice-memos/{id}/claim", JsonContent(new { computer = "Sam's MacBook" }))));
        Assert.Equal(("transcribing", "Sam's MacBook"), (claimed["state"]!.GetValue<string>(), claimed["computer"]!.GetValue<string>()));
        Assert.Equal(HttpStatusCode.Conflict, (await site.Client.SendAsync(Req(HttpMethod.Post, $"/api/v2/voice-memos/{id}/claim", JsonContent(new { computer = "Other" })))).StatusCode);

        // Done: it's a lecture, and the library lets go of the recording (the computer has its own copy).
        var done = await Json(await site.Client.SendAsync(Req(HttpMethod.Post, $"/api/v2/voice-memos/{id}/done", JsonContent(new { lecture = "rec-20260929-101500-abcdef" }))));
        Assert.Equal(("done", "rec-20260929-101500-abcdef"), (done["state"]!.GetValue<string>(), done["lecture"]!.GetValue<string>()));
        // Done here, but the lecture reaches the library only once the computer has written it down and sent it.
        var listed = (await Json(await site.Client.SendAsync(Req(HttpMethod.Get, "/api/v2/voice-memos"))))["memos"]!.AsArray().Single()!;
        Assert.False(listed["ready"]!.GetValue<bool>());
        store.Save(new Meeting("rec-20260929-101500-abcdef") { Title = "Recursion, part 2", Date = "2026-09-29", Transcript = "Today." }, new Classification("CS 101", 0.9, "folder"));
        listed = (await Json(await site.Client.SendAsync(Req(HttpMethod.Get, "/api/v2/voice-memos"))))["memos"]!.AsArray().Single()!;
        Assert.True(listed["ready"]!.GetValue<bool>());
        Assert.Empty(Directory.GetFiles(Path.Combine(cfg.Home, "voice-memos"), "*.m4a"));
    }

    [Fact]
    public async Task A_failed_memo_says_why_and_can_be_tried_again()
    {
        using var dir = new TempDir();
        var (cfg, store) = PhoneApiTests.Library(dir);
        using var _s = store;
        await using var site = await PhoneApiTests.Site(cfg, store);
        string id = (await Json(await site.Client.SendAsync(Req(HttpMethod.Post, "/api/v2/voice-memos", Memo()))))["id"]!.GetValue<string>();
        await Json(await site.Client.SendAsync(Req(HttpMethod.Post, $"/api/v2/voice-memos/{id}/claim", JsonContent(new { computer = "Mac" }))));

        var failed = await Json(await site.Client.SendAsync(Req(HttpMethod.Post, $"/api/v2/voice-memos/{id}/done", JsonContent(new { error = "the recording couldn't be read" }))));
        Assert.Equal(("failed", "the recording couldn't be read"), (failed["state"]!.GetValue<string>(), failed["error"]!.GetValue<string>()));

        var again = await Json(await site.Client.SendAsync(Req(HttpMethod.Post, $"/api/v2/voice-memos/{id}/retry")));
        Assert.Equal("waiting", again["state"]!.GetValue<string>());
        Assert.Null(again["error"]);

        await Json(await site.Client.SendAsync(Req(HttpMethod.Delete, $"/api/v2/voice-memos/{id}")));
        Assert.Empty((await Json(await site.Client.SendAsync(Req(HttpMethod.Get, "/api/v2/voice-memos"))))["memos"]!.AsArray());
    }

    [Fact]
    public async Task Only_a_recording_to_a_real_class_is_taken()
    {
        using var dir = new TempDir();
        var (cfg, store) = PhoneApiTests.Library(dir);
        using var _s = store;
        await using var site = await PhoneApiTests.Site(cfg, store);

        var notAudio = await site.Client.SendAsync(Req(HttpMethod.Post, "/api/v2/voice-memos", Memo(name: "notes.pdf")));
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, notAudio.StatusCode);
        Assert.Contains("Voice Memos", await notAudio.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.BadRequest, (await site.Client.SendAsync(Req(HttpMethod.Post, "/api/v2/voice-memos", Memo(cls: "Underwater Basket Weaving")))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await site.Client.SendAsync(Req(HttpMethod.Post, "/api/v2/voice-memos", Memo(bytes: [])))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await site.Stranger().PostAsync("/api/v2/voice-memos", Memo())).StatusCode);
        // With no title, the recording's own name is its title.
        var plain = await Json(await site.Client.SendAsync(Req(HttpMethod.Post, "/api/v2/voice-memos", Memo(name: "Bio lab.m4a"))));
        Assert.Equal("Bio lab", plain["title"]!.GetValue<string>());
        Assert.Null(plain["class"]);
    }

    [Fact]
    public async Task A_paired_phone_sends_one_and_it_says_so()
    {
        using var dir = new TempDir();
        var (cfg, store) = PhoneApiTests.Library(dir);
        using var _s = store;
        await using var site = await PhoneApiTests.Site(cfg, store);
        string device = await PhoneApiTests.PairAsync(site);

        var sent = await Json(await site.Stranger().SendAsync(Req(HttpMethod.Post, "/api/v2/voice-memos", Memo(), device: device)));
        Assert.Equal("Sam's iPhone", sent["by"]!.GetValue<string>());
    }

    [Fact]
    public void A_memo_taken_and_never_finished_waits_again()
    {
        using var dir = new TempDir();
        var now = new DateTimeOffset(2026, 9, 29, 9, 0, 0, TimeSpan.Zero);
        var memos = new VoiceMemos(dir.Path, () => now);
        string staged = Path.Combine(dir.Path, "staged");
        File.WriteAllBytes(staged, [1, 2, 3]);
        var m = memos.Add(staged, "Memo.m4a", 3, null, null, "iPhone");
        Assert.NotNull(memos.Claim(m.Id, "Mac"));
        Assert.Equal(VoiceMemos.Transcribing, memos.Get(m.Id)!.State);

        now += VoiceMemos.ClaimLasts + TimeSpan.FromMinutes(1);

        Assert.Equal(VoiceMemos.Waiting, memos.Get(m.Id)!.State);
        Assert.NotNull(memos.Claim(m.Id, "Another Mac"));
    }

    static StringContent JsonContent(object body) =>
        new(System.Text.Json.JsonSerializer.Serialize(body), System.Text.Encoding.UTF8, "application/json");
}
