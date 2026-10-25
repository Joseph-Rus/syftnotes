using System.Net;
using System.Net.Http.Headers;
using System.Text.Json.Nodes;
using StudyStash.Core.Ai;
using StudyStash.Library;

namespace StudyStash.Core.Tests;

/// <summary>Whiteboard photos: the laptop's "recording now", a photo for a lecture the library hasn't got yet, its moment
/// in the lecture, what the notes' AI is given, and that notes show only their own lecture's photos.</summary>
public class WhiteboardPhotosTests
{
    static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0, 16, 0x4A, 0x46, 0x49, 0x46, 0, 1, 1, 0, 0, 1, 0, 1, 0, 0, 0xFF, 0xD9];
    const string Started = "2026-10-24T10:00:00-07:00";
    const string Sentence = "Today we covered the cell membrane and how osmosis moves water across it, and worked "
        + "through the sodium-potassium pump and why it needs energy to run against the gradient. ";

    static Config Cfg(TempDir dir) => new(dir["home"], dir["pool"])
    {
        PoolPassword = "pw", OllamaEnabled = false, OllamaModel = "qwen3:8b", Classes = [new ClassDef("Bio 110")],
    };

    static Task<TestSite> Site(Config cfg, Store store, AiJobs? ai = null) => TestSite.StartAsync(b => LibraryWeb.Build(b, cfg, store,
        new Pipeline(cfg, store, log: _ => { }), new LibraryWebOptions
        {
            ListModels = _ => Task.FromResult<List<(string, double)>?>(null), Tailscale = () => new TailscaleInfo(),
            Latest = _ => Task.FromResult<Release?>(null), ReadDocument = (_, _) => Task.FromResult<string?>(null), Ai = ai,
        }));

    static HttpRequestMessage Req(HttpMethod m, string path, HttpContent? content = null)
    {
        var r = new HttpRequestMessage(m, path) { Content = content };
        r.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "pw");
        return r;
    }

    static async Task<JsonObject> Json(HttpResponseMessage r) => (JsonObject)JsonNode.Parse(await r.Content.ReadAsStringAsync())!;

    static async Task<HttpResponseMessage> Photo(TestSite site, string lecture, string taken)
    {
        var form = new MultipartFormDataContent { { new StringContent(lecture), "lecture" }, { new StringContent(taken), "taken" }, { new StringContent("whiteboard"), "kind" } };
        form.Add(new ByteArrayContent(Jpeg), "file", "board.jpg");
        return await site.Client.SendAsync(Req(HttpMethod.Post, "/api/v2/attachments", form));
    }

    static Task<HttpResponseMessage> Say(TestSite site, string path, JsonObject body) =>
        site.Client.SendAsync(Req(HttpMethod.Post, path, new StringContent(body.ToJsonString(), System.Text.Encoding.UTF8, "application/json")));

    [Fact]
    public void Recording_now_lapses_90_seconds_after_the_last_report_and_stops_at_once_when_told()
    {
        var now = DateTimeOffset.Parse("2026-10-24T17:00:00Z");
        var live = new RecordingNow(() => now);
        Assert.Null(live.Json());
        live.Report("rec-1", "Bio 110", "Cells", Started);
        now += TimeSpan.FromSeconds(60);
        Assert.Equal("rec-1", live.Json()!["id"]!.GetValue<string>());
        now += TimeSpan.FromSeconds(31); // 91 s after the last report: a crash left it, so it is gone
        Assert.Null(live.Json());
        live.Report("rec-1", "Bio 110", "Cells", Started);
        live.Report("rec-1", "", "", "", stopped: true);
        Assert.Null(live.Json());
        Assert.Equal("Bio 110", live.ClassOf("rec-1")); // photos for it are still taken for a day
        now += TimeSpan.FromHours(25);
        Assert.Null(live.ClassOf("rec-1"));
    }

    [Fact]
    public async Task A_photo_for_the_recording_waits_under_its_id_then_is_the_lectures_with_its_moment()
    {
        using var dir = new TempDir();
        var cfg = Cfg(dir);
        using var store = new Store(cfg.DbPath, cfg.PoolDir);
        await using var site = await Site(cfg, store);

        // A lecture the library has never heard of is refused ...
        Assert.Equal(HttpStatusCode.NotFound, (await Photo(site, "rec-1", "2026-10-24T10:05:30-07:00")).StatusCode);
        // ... until the laptop says it is recording it.
        var said = await Say(site, "/api/v2/recording-now", new JsonObject { ["id"] = "rec-1", ["class"] = "Bio 110", ["title"] = "Cells", ["started"] = Started });
        Assert.Equal(HttpStatusCode.OK, said.StatusCode);
        var library = await Json(await site.Client.SendAsync(Req(HttpMethod.Get, "/api/v2/library")));
        Assert.Equal("""{"id":"rec-1","class":"Bio 110","title":"Cells","started":"2026-10-24T10:00:00-07:00"}""", library["recording_now"]!.ToJsonString());

        var sent = await Photo(site, "rec-1", "2026-10-24T10:05:30-07:00");
        Assert.Equal(HttpStatusCode.OK, sent.StatusCode);
        var a = (JsonObject)((JsonArray)(await Json(sent))["attachments"]!)[0]!;
        Assert.Equal("rec-1", a["lecture"]!.GetValue<string>());
        Assert.Equal("Bio 110", a["class"]!.GetValue<string>());
        Assert.Equal("whiteboard", a["kind"]!.GetValue<string>());
        Assert.Equal("2026-10-24T10:05:30-07:00", a["taken"]!.GetValue<string>());
        Assert.Null(a["at"]); // the lecture isn't here to count from yet

        // The lecture arrives (written down after the recording): the photo is its own, 330 s in.
        store.Save(new Meeting("rec-1") { Title = "Cells", Date = Started, Transcript = Sentence },
            new Classification("Bio 110", 1, "folder"), "## Notes", "qwen3");
        var listed = await Json(await site.Client.SendAsync(Req(HttpMethod.Get, "/api/v2/attachments?lecture=rec-1")));
        var mine = (JsonObject)((JsonArray)listed["attachments"]!)[0]!;
        Assert.Equal(330, mine["at"]!.GetValue<double>());
        Assert.Equal("""{"id":"rec-1","class":"Bio 110","title":"Cells","started":"2026-10-24T10:00:00-07:00"}""", library["recording_now"]!.ToJsonString());
    }

    [Fact]
    public async Task The_notes_ai_is_given_the_photos_Claude_as_files_it_can_read_and_Codex_on_its_command_line()
    {
        using var dir = new TempDir();
        var cfg = Cfg(dir);
        cfg.OllamaEnabled = true;
        using var store = new Store(cfg.DbPath, cfg.PoolDir);
        store.Save(new Meeting("lec-1") { Title = "Cells", Date = Started, Transcript = string.Concat(Enumerable.Repeat(Sentence, 12)) },
            new Classification("Bio 110", 1, "folder"), "Old notes.", "qwen3");
        string file = Path.Combine(store.AttachmentsDir("Bio 110"), "board.jpg");
        File.WriteAllBytes(file, Jpeg);
        store.AddAttachment(new Attachment("ph1", "board.jpg", "board.jpg", "Bio 110", "lec-1", Jpeg.Length, "image/jpeg", "2026-10-24T17:06:00Z",
            Taken: "2026-10-24T10:12:34-07:00", Kind: "whiteboard"));
        store.SetAttachmentText("ph1", "Na/K pump 3:2");
        Assert.True(store.AttachmentsUnused("lec-1")); // a photo that came after the notes offers the rewrite

        foreach (string engine in new[] { "claude", "codex" })
        {
            var ai = new ScriptedAi(engine) { Script = [new AiEvent("final", "New notes.")] };
            var jobs = new AiJobs(cfg.Home) { Providers = _ => ai, Checks = new FakeChecks().Installed(engine).Build() };
            await using var site = await Site(cfg, store, jobs);
            var remote = new AiRemote("http://localhost", "pw", site.Client);
            await remote.RewriteStartAsync("lec-1", engine);
            for (int i = 0; i < 250 && ai.Requests.Count == 0; i++) await Task.Delay(20);
            var request = Assert.Single(ai.Requests);
            Assert.Contains("at 12:34", request.Prompt);
            Assert.Contains("id ph1", request.Prompt);
            if (engine == "claude")
            {
                Assert.Empty(request.Images);
                Assert.Matches(@"file photos-lec1/ph1\.jpg", request.Prompt); // a file in its working folder, named for it
            }
            else
            {
                Assert.EndsWith("ph1.jpg", Assert.Single(request.Images));
                Assert.DoesNotContain("Na/K pump", request.Prompt); // it sees the picture, so no second-hand words
            }
        }
    }

    [Fact]
    public async Task Notes_show_a_photo_only_when_it_is_that_lectures_own()
    {
        using var dir = new TempDir();
        var cfg = Cfg(dir);
        using var store = new Store(cfg.DbPath, cfg.PoolDir);
        const string Embed = "Board:\n\n![Whiteboard at 12:34](attachment:{0})\n";
        store.Save(new Meeting("lec-1") { Title = "One", Date = Started, Transcript = Sentence }, new Classification("Bio 110", 1, "folder"),
            string.Format(Embed, "mine") + string.Format(Embed, "theirs"), "m");
        store.Save(new Meeting("lec-2") { Title = "Two", Date = Started, Transcript = Sentence }, new Classification("Bio 110", 1, "folder"), "Two.", "m");
        foreach (var (id, lecture) in new[] { ("mine", "lec-1"), ("theirs", "lec-2") })
        {
            File.WriteAllBytes(Path.Combine(store.AttachmentsDir("Bio 110"), id + ".jpg"), Jpeg);
            store.AddAttachment(new Attachment(id, id + ".jpg", id + ".jpg", "Bio 110", lecture, Jpeg.Length, "image/jpeg", "2026-10-24T17:06:00Z", Kind: "whiteboard", Taken: Started));
        }

        // The phone's rendered notes show lecture 1's own photo, and lecture 2's id only as its words.
        await using var site = await Site(cfg, store);
        string rendered = (await Json(await site.Client.SendAsync(Req(HttpMethod.Get, "/api/v2/lectures/lec-1/rendered"))))["html"]!.GetValue<string>();
        Assert.Contains("<img src=\"/api/v2/attachments/mine/raw\"", rendered);
        Assert.DoesNotContain("theirs", rendered);
        Assert.Equal(1, rendered.Split("<img").Length - 1);
        Assert.Contains("Whiteboard at 12:34", rendered);

        // The library's own web page, the same.
        await site.PostForm("/login", ("password", "pw"), ("next", "/"));
        var page = await site.Client.GetAsync("/note/lec-1");
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        string html = await page.Content.ReadAsStringAsync();
        Assert.Contains("src=\"/attachments/mine\"", html);
        Assert.DoesNotContain("src=\"/attachments/theirs\"", html);
    }
}
