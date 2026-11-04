using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using StudyStash.Core.Ai;
using StudyStash.Core.Canvas;
using StudyStash.Library;

namespace StudyStash.Core.Tests;

/// <summary>A student can hide a missing assignment from the Due list, tick it off, and take either back, in Syft Notes
/// alone. Every list that shows what's due reads that one state: the Due list (as the app and the phone have it), Home's
/// count (<c>Assignments.Upcoming</c>) and what Ask is told.</summary>
public class DueMarksTests
{
    [Fact]
    public async Task A_missing_assignment_hidden_then_done_then_undone_reads_the_same_in_the_Due_list_Home_and_Ask()
    {
        using var dir = new TempDir();
        var cfg = new Config(dir.Path, dir["pool"]) { PoolPassword = "pw", OllamaEnabled = true, Classes = [new ClassDef("CS 101")] };
        using var store = new Store(cfg.DbPath, cfg.PoolDir);
        var sync = FakeCanvas.Library(dir, () => FakeCanvas.DesignNow); // Thu 25 Sep 2025, 10:24 in California
        Assignments.Save(dir.Path,
        [
            new Assignment("CS 101", 9101, "Asynchronous Lecture 5", "2025-09-23T23:59", 10, "missing", null, "", "", Missing: true, DueAt: "2025-09-24T06:59:00Z"),
            new Assignment("CS 101", 9102, "Homework 4", "2025-09-28T23:59", 20, "open", null, "", "", DueAt: "2025-09-29T06:59:00Z"),
            new Assignment("CS 101", 9103, "In-class quiz 2", "2025-09-22T09:00", 5, "no submission", null, "", "", DueAt: "2025-09-22T16:00:00Z"),
        ]);
        string before = File.ReadAllText(Assignments.PathIn(dir.Path));

        var claude = new ScriptedAi("claude", "Claude") { Script = [new AiEvent("final", """{"answer":"ok","sources":[]}""")] };
        var ai = new AiJobs(cfg.Home) { Providers = id => id == "claude" ? claude : new ScriptedAi(id), Checks = new FakeChecks().Installed("claude").Build() };
        await using var site = await TestSite.StartAsync(b => LibraryWeb.Build(b, cfg, store, new Pipeline(cfg, store, log: _ => { }), new LibraryWebOptions
        {
            ListModels = _ => Task.FromResult<List<(string, double)>?>(null), Tailscale = () => new TailscaleInfo(),
            Latest = _ => Task.FromResult<Release?>(null), Ai = ai, Canvas = sync,
        }));
        site.Client.DefaultRequestHeaders.Authorization = new("Bearer", "pw");
        var remote = new AiRemote("http://localhost", "pw", site.Client);
        var wall = TimeZoneInfo.ConvertTime(FakeCanvas.DesignNow, FakeCanvas.Zone).DateTime;
        const string lecture5 = "Asynchronous Lecture 5";

        static string[] Names(JsonNode? items) => [.. items!.AsArray().Select(i => i!["name"]!.GetValue<string>())];
        static string[] In(JsonObject due, string key) => Names(due["groups"]!.AsArray().Single(g => g!["key"]!.GetValue<string>() == key)!["items"]);

        // What the Due list just given says, what Home's count would be, and what Ask was told, in one line each.
        async Task<(string State, string Asked)> Read(JsonObject due)
        {
            await remote.AskAsync(new AskRequest("What is due?") { Engine = "claude" });
            string home = string.Join(", ", Assignments.Upcoming(Assignments.Shown(dir.Path), wall, 7).Select(a => a.Name));
            return ($"overdue [{string.Join(", ", In(due, "overdue"))}] handed in [{string.Join(", ", In(due, "handed_in"))}] hidden [{string.Join(", ", Names(due["hidden"]))}] "
                + $"to hand in {due["to_hand_in"]} Home [{home}]", claude.Prompts[^1]);
        }
        async Task<JsonObject> Mark(string mark)
        {
            using var reply = await site.Client.PostAsJsonAsync("/api/v2/canvas/mark", new { @class = "CS 101", id = 9101, kind = "assignment", mark });
            Assert.True(reply.IsSuccessStatusCode);
            return JsonNode.Parse(await reply.Content.ReadAsStringAsync())!.AsObject();
        }

        async Task<HttpStatusCode> Post(long id, string kind, string mark)
        {
            using var reply = await site.Client.PostAsJsonAsync("/api/v2/canvas/mark", new { @class = "CS 101", id, kind, mark });
            return reply.StatusCode;
        }

        // Nothing marked: missing work is overdue, counted, and Ask is told so.
        var plain = await Read(JsonNode.Parse(await site.Text("/api/v2/canvas/due"))!.AsObject());
        Assert.Equal($"overdue [{lecture5}] handed in [] hidden [] to hand in 2 Home [{lecture5}, Homework 4]", plain.State);
        Assert.Contains($"{lecture5} (missing)", plain.Asked);

        // An in-class quiz that has already happened takes nothing to hand in: it isn't overdue or counted above, Ask isn't told
        // of it, and the class's list puts it with the done ones.
        Assert.DoesNotContain("In-class quiz", plain.Asked);
        var classList = JsonNode.Parse(await site.Text("/api/v2/canvas/assignments?class=CS%20101"))!;
        Assert.Equal($"to hand in [{lecture5}, Homework 4] done [In-class quiz 2]",
            $"to hand in [{string.Join(", ", Names(classList["to_hand_in"]))}] done [{string.Join(", ", Names(classList["done"]))}]");

        // Hidden: off every list, and in the Due list's own "hidden" for the Due tab's "Show hidden".
        var hidden = await Read(await Mark("hidden"));
        Assert.Equal($"overdue [] handed in [] hidden [{lecture5}] to hand in 1 Home [Homework 4]", hidden.State);
        Assert.DoesNotContain(lecture5, hidden.Asked);
        Assert.Contains("Homework 4", hidden.Asked);

        // Done (it replaces the hide): handed in, with the mark that Undo takes back; still off the to-do lists.
        var done = await Mark("done");
        Assert.Equal("done", done["groups"]!.AsArray().Single(g => g!["key"]!.GetValue<string>() == "handed_in")!["items"]![0]!["mark"]!.GetValue<string>());
        var ticked = await Read(done);
        Assert.Equal($"overdue [] handed in [{lecture5}] hidden [] to hand in 1 Home [Homework 4]", ticked.State);
        Assert.DoesNotContain(lecture5, ticked.Asked);

        // Undone: as if nothing had been marked.
        var undone = await Read(await Mark(""));
        Assert.Equal(plain.State, undone.State);
        Assert.Contains($"{lecture5} (missing)", undone.Asked);

        // An item that isn't one of the class's (an id Canvas never gave, 0, or a kind the row isn't) is refused, and nothing is saved.
        Assert.Equal([HttpStatusCode.NotFound, HttpStatusCode.NotFound, HttpStatusCode.NotFound],
            [await Post(424242, "assignment", "done"), await Post(0, "assignment", "hidden"), await Post(9101, "quiz", "done")]);
        Assert.Empty(DueMarks.Load(dir.Path));

        // A class no longer linked to Canvas (its course un-ticked and kept) has its rows in the file until the next sync: they leave
        // every list at once, the same way.
        Assert.Equal(HttpStatusCode.OK, await Post(9102, "assignment", "hidden"));
        CanvasSettings.Update(dir.Path, s => s.Courses.Remove("CS 101"));
        var unlinked = await Read(JsonNode.Parse(await site.Text("/api/v2/canvas/due"))!.AsObject());
        Assert.Equal("overdue [] handed in [] hidden [] to hand in 0 Home []", unlinked.State);
        Assert.DoesNotContain("Homework 4", unlinked.Asked);

        // Its row has gone, so no new mark can be made on it, but the mark already saved can still be taken back.
        Assert.Equal(HttpStatusCode.NotFound, await Post(9102, "assignment", "done"));
        Assert.Equal(HttpStatusCode.OK, await Post(9102, "assignment", ""));
        Assert.Empty(DueMarks.Load(dir.Path));

        // The marks are the student's own: Canvas's saved list is untouched, and a mark that isn't one is refused.
        Assert.Equal(before, File.ReadAllText(Assignments.PathIn(dir.Path)));
        using var junk = await site.Client.PostAsJsonAsync("/api/v2/canvas/mark", new { @class = "CS 101", id = 9101, kind = "assignment", mark = "deleted" });
        Assert.Equal(HttpStatusCode.BadRequest, junk.StatusCode);
    }

    [Fact]
    public void A_marks_file_that_cant_be_read_is_kept_aside_and_not_written_over()
    {
        using var dir = new TempDir();
        string path = DueMarks.PathIn(dir.Path), bad = path + ".bad";
        File.WriteAllText(path, """[{"class": "CS 101", "kind": "assi""");

        // The next mark starts a new file; the one nobody could read is still there to look at.
        DueMarks.Set(dir.Path, "CS 101", "assignment", 9101, DueMarks.Done, FakeCanvas.DesignNow);
        Assert.Equal("""[{"class": "CS 101", "kind": "assi""", File.ReadAllText(bad));
        Assert.Equal(9101, Assert.Single(DueMarks.Load(dir.Path)).Id);

        // A second broken file replaces the older .bad, and reading it alone gives no marks.
        File.WriteAllText(path, "not json");
        Assert.Empty(DueMarks.Load(dir.Path));
        Assert.Equal("not json", File.ReadAllText(bad));
        Assert.False(File.Exists(path));
    }

    /// <summary>What a student hid says nothing when Canvas changes it or when it falls due; what they ticked off still does.</summary>
    [Fact]
    public void A_hidden_assignment_raises_no_notice_but_a_done_one_still_does()
    {
        using var dir = new TempDir();
        var sync = FakeCanvas.Library(dir, () => FakeCanvas.DesignNow);
        var canvas = FakeCanvas.Cs101();
        Assert.True(canvas.Run(sync));
        var said = new List<CanvasChange>();
        sync.Finished += said.AddRange;
        var lab3 = Assignments.Load(dir.Path).Single(a => a.Id == 9001);
        var ps4 = Assignments.Load(dir.Path).Single(a => a.Id == 9002);
        DueMarks.Set(dir.Path, "CS 101", lab3.Kind, lab3.Id, DueMarks.Hidden, FakeCanvas.DesignNow);
        DueMarks.Set(dir.Path, "CS 101", ps4.Kind, ps4.Id, DueMarks.Done, FakeCanvas.DesignNow);

        // Lab 3 (hidden) now falls due tonight and is missing; Problem set 4 (ticked off) gets a comment from the grader.
        static string Edit(string fixture, long id, Action<JsonObject> change)
        {
            var list = JsonNode.Parse(FakeCanvas.Fixture(fixture))!.AsArray();
            change(list.OfType<JsonObject>().Single(o => (o["assignment_id"] ?? o["id"])!.GetValue<long>() == id));
            return list.ToJsonString();
        }
        canvas.Json("/api/v1/courses/4201/assignments", Edit("cs101-assignments.json", 9001, a => a["due_at"] = "2025-09-26T06:59:00Z"));
        canvas.Json("/api/v1/courses/4201/students/submissions", Edit("cs101-submissions.json", 9002, s => s["submission_comments"]!.AsArray().Add(new JsonObject
        {
            ["id"] = 51005, ["author_id"] = 3301, ["author_name"] = "Dr. Okafor", ["comment"] = "See me about 3b.", ["created_at"] = "2025-09-25T16:00:00Z",
        })));
        Assert.True(canvas.Run(sync));

        // The same check the notifications route makes when the app asks what's due soon.
        var wall = TimeZoneInfo.ConvertTime(FakeCanvas.DesignNow, FakeCanvas.Zone).DateTime;
        void DueSoon() => CanvasNotifications.EnsureDueSoon(dir.Path, Assignments.Upcoming(Assignments.Shown(dir.Path), wall, 1), FakeCanvas.DesignNow, FakeCanvas.Zone);
        DueSoon();
        Assert.Equal(["feedback"], said.Select(c => c.Kind));
        Assert.Equal(["new_feedback"], CanvasNotifications.Since(dir.Path, 0).Items.Select(n => n.Kind));
        Assert.Equal(9002, CanvasNotifications.Since(dir.Path, 0).Items.Single().AssignmentId);

        // Show it again and it is due soon like any other.
        DueMarks.Set(dir.Path, "CS 101", lab3.Kind, lab3.Id, "", FakeCanvas.DesignNow);
        DueSoon();
        Assert.Equal(9001, CanvasNotifications.Since(dir.Path, 0).Items.Single(n => n.Kind == "due_soon").AssignmentId);
    }
}
