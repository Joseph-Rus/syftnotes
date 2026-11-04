using System.Net.Http.Json;
using System.Text.Json.Nodes;
using StudyStash.Core.Ai;
using StudyStash.Core.Canvas;
using StudyStash.Library;

namespace StudyStash.Core.Tests;

/// <summary>A student with no Canvas at all can put an exam and a to-do on their own Due list. They are in the Due list that
/// the app and the phone read, with the times as the student typed them, and in what Ask is told (each time converted once;
/// an undated one says so, a class-less one names no class). Ticking one off is a mark like any other; deleting one takes
/// it from every list.</summary>
public class DueMineTests
{
    [Fact]
    public async Task An_exam_and_a_todo_added_with_no_Canvas_are_in_the_Due_list_and_Ask_then_one_is_ticked_off_and_one_deleted()
    {
        using var dir = new TempDir();
        var cfg = new Config(dir.Path, dir["pool"]) { PoolPassword = "pw", OllamaEnabled = true, Classes = [new ClassDef("Chemistry")] };
        using var store = new Store(cfg.DbPath, cfg.PoolDir);
        // Thu 25 Sep 2025, 10:24 in California, and no Canvas linked: the exam is in standard time (UTC-8).
        var sync = new CanvasSync(dir.Path, c => FakeCanvas.ClassDir(dir, c), _ => { }) { Clock = () => FakeCanvas.DesignNow, Zone = FakeCanvas.Zone };
        var claude = new ScriptedAi("claude", "Claude") { Script = [new AiEvent("final", """{"answer":"ok","sources":[]}""")] };
        var ai = new AiJobs(cfg.Home) { Providers = id => id == "claude" ? claude : new ScriptedAi(id), Checks = new FakeChecks().Installed("claude").Build() };
        await using var site = await TestSite.StartAsync(b => LibraryWeb.Build(b, cfg, store, new Pipeline(cfg, store, log: _ => { }), new LibraryWebOptions
        {
            ListModels = _ => Task.FromResult<List<(string, double)>?>(null), Tailscale = () => new TailscaleInfo(),
            Latest = _ => Task.FromResult<Release?>(null), Ai = ai, Canvas = sync,
        }));
        site.Client.DefaultRequestHeaders.Authorization = new("Bearer", "pw");
        var remote = new AiRemote("http://localhost", "pw", site.Client);

        static JsonObject Parse(string text) => JsonNode.Parse(text)!.AsObject();
        static JsonObject[] Rows(JsonObject due, string key) =>
            [.. due["groups"]!.AsArray().Single(g => g!["key"]!.GetValue<string>() == key)!["items"]!.AsArray().Select(i => i!.AsObject())];
        async Task<JsonObject> Add(object item)
        {
            using var reply = await site.Client.PostAsJsonAsync("/api/v2/due/mine", item);
            Assert.True(reply.IsSuccessStatusCode, await reply.Content.ReadAsStringAsync());
            return Parse(await reply.Content.ReadAsStringAsync());
        }
        async Task<string> Asked()
        {
            await remote.AskAsync(new AskRequest("What is due?") { Engine = "claude" });
            return claude.Prompts[^1];
        }

        await Add(new { title = "Chemistry midterm", what = "exam", @class = "Chemistry", day = "2025-11-05", time = "09:00" });
        var due = await Add(new { title = "Renew library card", what = "task", @class = "", day = "", time = "" });

        // Both are in the Due list, as ordinary rows: the exam later, with the time as typed and the same moment in UTC; the task undated.
        var exam = Assert.Single(Rows(due, "later"));
        Assert.Equal(("Chemistry midterm", "mine", "exam", "Chemistry", "2025-11-05T09:00", "2025-11-05T17:00:00Z", false),
            (exam["name"]!.GetValue<string>(), exam["kind"]!.GetValue<string>(), exam["what"]!.GetValue<string>(), exam["class"]!.GetValue<string>(),
             exam["due"]!.GetValue<string>(), exam["due_at"]!.GetValue<string>(), exam["all_day"]!.GetValue<bool>()));
        var task = Assert.Single(Rows(due, "undated"));
        Assert.Equal(("Renew library card", "", (string?)null, 2), (task["name"]!.GetValue<string>(), task["class"]!.GetValue<string>(), (string?)task["due"], (int)due["to_hand_in"]!));
        Assert.Equal("open", task["status"]!.GetValue<string>());
        Assert.False(File.Exists(Assignments.PathIn(dir.Path))); // Canvas never came into it

        // Ask is told of both: the exam at 9:00 AM (converted once, not 1:00 AM), the task with no date and no empty class, and that Canvas isn't linked.
        string asked = await Asked();
        Assert.Contains("Canvas isn't linked to Syft Notes, so its due dates aren't known.", asked);
        Assert.Contains("- Wed 5 Nov 2025, 9:00 AM: Chemistry: Chemistry midterm (own exam)", asked);
        Assert.Contains("- no due date: Renew library card (own to-do)", asked);
        Assert.DoesNotContain(": :", asked);

        // Ticking the exam off is a mark like Canvas's: it is handed in, and no longer to hand in.
        using (var marked = await site.Client.PostAsJsonAsync("/api/v2/canvas/mark", new { @class = "Chemistry", id = exam["id"]!.GetValue<long>(), kind = "mine", mark = "done" }))
        {
            var after = Parse(await marked.Content.ReadAsStringAsync());
            Assert.Equal(("done", 1, 0), (Rows(after, "handed_in").Single()["mark"]!.GetValue<string>(), (int)after["to_hand_in"]!, Rows(after, "later").Length));
        }
        Assert.DoesNotContain("Chemistry midterm", await Asked());

        // Deleting the task takes it from the list and from Ask; the same item again is gone already.
        long taskId = task["id"]!.GetValue<long>();
        using (var deleted = await site.Client.DeleteAsync($"/api/v2/due/mine/{taskId}"))
        {
            var after = Parse(await deleted.Content.ReadAsStringAsync());
            Assert.Empty(Rows(after, "undated"));
            Assert.Equal(0, (int)after["to_hand_in"]!);
        }
        Assert.DoesNotContain("Renew library card", await Asked());
        using var again = await site.Client.DeleteAsync($"/api/v2/due/mine/{taskId}");
        Assert.Equal(System.Net.HttpStatusCode.NotFound, again.StatusCode);

        // What isn't a day or a title is refused, in words.
        using var junk = await site.Client.PostAsJsonAsync("/api/v2/due/mine", new { title = "", what = "exam", @class = "", day = "", time = "" });
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, junk.StatusCode);
    }
}
