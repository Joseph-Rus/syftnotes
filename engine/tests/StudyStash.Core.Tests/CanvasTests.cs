using System.Text.Json.Nodes;
using StudyStash.Core.Canvas;

namespace StudyStash.Core.Tests;

/// <summary>Canvas through the Chrome extension: the mirror of each class, the assignments list, and reads for AIs.</summary>
public class CanvasTests
{
    static readonly DateTimeOffset Now = new(2026, 9, 25, 12, 0, 0, TimeSpan.Zero);

    static JsonObject A(string json) => JsonNode.Parse(json)!.AsObject();

    [Theory]
    [InlineData("""{"submission":{"excused":true}}""", "excused")]
    [InlineData("""{"submission":{"workflow_state":"graded","score":9}}""", "graded")]
    [InlineData("""{"submission":{"missing":true}}""", "missing")]
    [InlineData("""{"submission":{"submitted_at":"2026-09-20T00:00:00Z","late":true}}""", "late")]
    [InlineData("""{"submission_types":["on_paper"]}""", "no submission")]
    [InlineData("""{"submission_types":["online_upload"],"due_at":"2026-09-20T00:00:00Z"}""", "past due")]
    [InlineData("""{"submission_types":["online_upload"],"due_at":"2026-10-20T00:00:00Z"}""", "open")]
    public void An_assignment_s_status_reads_like_canvas(string json, string status) =>
        Assert.Equal(status, Assignments.StatusOf(A(json), Now));

    [Fact]
    public void Changes_between_syncs_are_said_in_words()
    {
        // The design's now: Thu 25 Sep 2025, 10:24 in California.
        var now = new DateTime(2025, 9, 25, 10, 24, 0);
        var old = new List<Assignment>
        {
            new("CS 101", 9002, "Problem set 4", "2025-09-16T23:59", 20, "submitted", null, "2025-09-16T21:41", "", Comments: 0),
            new("CALC II", 9301, "Quiz 3 practice", "2025-09-25T23:59", 10, "open", null, "", ""),
            new("BIO 110", 9201, "Osmosis lab report", "2025-09-24T23:59", 20, "submitted", null, "2025-09-24T20:15", ""),
        };
        var after = new List<Assignment>
        {
            new("CS 101", 9002, "Problem set 4", "2025-09-16T23:59", 20, "graded", 18, "2025-09-16T21:41", "", Grade: "18", Comments: 1),
            new("CALC II", 9301, "Quiz 3 practice", "2025-09-26T09:00", 10, "open", null, "", ""),
            new("CS 101", 9001, "Lab 3: recursion traces", "2025-09-30T23:59", 20, "open", null, "", ""),
        };
        var said = Assignments.Diff(old, after, now);
        Assert.Equal(
        [
            "Graded: CS 101 · Problem set 4 · 18/20",
            "Feedback: CS 101 · Problem set 4 · 1 new comment",
            "Moved: CALC II · Quiz 3 practice · now Fri 9:00 AM",
            "New: CS 101 · Lab 3: recursion traces · due Tue 11:59 PM",
            "Removed: BIO 110 · Osmosis lab report",
        ], said.Select(c => c.Text));
        Assert.Equal(["graded", "feedback", "moved", "new", "removed"], said.Select(c => c.Kind));
        Assert.All(said, c => Assert.Null(c.AnnouncementId));
        Assert.Equal((9002L, "CS 101", "Problem set 4"), (said[0].AssignmentId!.Value, said[0].Class, said[0].Name));
    }

    [Fact]
    public void A_canvas_address_is_cleaned_up_from_whatever_was_pasted()
    {
        Assert.Equal("https://school.instructure.com", CanvasSettings.CleanUrl("school.instructure.com/courses/12/"));
        Assert.Equal("https://school.instructure.com", CanvasSettings.CleanUrl(" https://school.instructure.com "));
        Assert.Null(CanvasSettings.CleanUrl("not a url"));
    }

    [Fact]
    public void A_sync_mirrors_specs_feedback_modules_pages_files_and_announcements()
    {
        using var dir = new TempDir();
        var sync = FakeCanvas.Library(dir, () => FakeCanvas.DesignNow);
        var canvas = FakeCanvas.Cs101();
        Assert.True(canvas.Run(sync));

        // Everything is looked at once the sync has finished: Markdown may be written at the end.
        string root = FakeCanvas.CanvasRoot(dir);
        string spec = File.ReadAllText(Path.Combine(root, "assignments", "Lab 3- recursion traces", "spec.md"));
        Assert.Contains("canvas_id: 9001\n", spec);
        Assert.Contains("generated_by: study-stash", spec);
        Assert.Contains("Trace factorial(4) and fib(5) by hand.", spec);
        Assert.Contains("| Correct traces (Every call and every return value is right.) | 10 |", spec);
        string feedback = File.ReadAllText(Path.Combine(root, "assignments", "Problem set 4", "feedback.md"));
        Assert.Contains("- **Score:** 18/20", feedback);
        Assert.Contains("| Stack traces | 8 / 10 | One frame missing | The frame for n = 1 is missing in 3b. |", feedback);
        Assert.Contains("**Dr. Okafor**", feedback);
        Assert.Contains("Watch the last frame in 3b, it’s the one people drop.", feedback);
        Assert.Equal("%PDF-1.4 ps4 answers", File.ReadAllText(Path.Combine(root, "assignments", "Problem set 4", "submission", "ps4-answers.pdf")));
        Assert.StartsWith("def fact(n):", File.ReadAllText(Path.Combine(root, "assignments", "Problem set 4", "submission", "ps4.py")));
        Assert.False(File.Exists(Path.Combine(root, "assignments", "Lab 3- recursion traces", "feedback.md"))); // nothing handed in yet

        string modules = File.ReadAllText(Path.Combine(root, "modules.md"));
        Assert.True(modules.IndexOf("## Week 3 · Scope", StringComparison.Ordinal) < modules.IndexOf("## Week 4 · Recursion", StringComparison.Ordinal));
        Assert.Contains("[Lab 3 instructions](modules/04%20Week%204%20·%20Recursion/Lab%203%20instructions.md)", modules);
        Assert.Contains("[Tracing worksheet](https://app.box.com/s/abc123) (link)", modules);
        string week4 = Path.Combine(root, "modules", "04 Week 4 · Recursion");
        Assert.Contains("## What to hand in", File.ReadAllText(Path.Combine(week4, "Lab 3 instructions.md")));
        Assert.Equal("%PDF-1.4 recursion slides", File.ReadAllText(Path.Combine(week4, "recursion-slides.pdf")));

        string news = File.ReadAllText(Path.Combine(root, "announcements.md"));
        Assert.True(news.IndexOf("## Lab 3 is up", StringComparison.Ordinal) < news.IndexOf("## Welcome to COMP 101", StringComparison.Ordinal));
        Assert.Contains("## Office hours move to Thursday this week", news);
        Assert.Contains("**Tuesday 30 September at 11:59 PM**", news);

        var saved = Assignments.Load(dir.Path);
        Assert.Equal(["Syllabus quiz", "Problem set 3", "Lab 2: tracing loops", "Problem set 4", "Lab 3: recursion traces"], saved.Select(a => a.Name));
        Assert.Equal("open", saved.Single(a => a.Id == 9001).Status);
        Assert.Equal(18, saved.Single(a => a.Id == 9002).Score);
        Assert.Equal("excused", saved.Single(a => a.Id == 9005).Status);
        Assert.Equal("Canvas/assignments/Problem set 4", sync.Crawl.AssignmentFolder("CS 101", 9002));

        var s = CanvasSettings.Load(dir.Path);
        Assert.Equal("", s.Error);
        Assert.False(s.NeedsLogin);
        Assert.All(sync.Crawl.Sections["CS 101"].Values, state => Assert.Equal("ok", state));
        // The whole term's announcements, from the course's own list: never the 28-day /api/v1/announcements window.
        Assert.DoesNotContain(canvas.Requested, u => u.Contains("/api/v1/announcements", StringComparison.Ordinal));
        Assert.Contains(canvas.Requested, u => u.EndsWith("/discussion_topics?only_announcements=true&per_page=100", StringComparison.Ordinal));
        Assert.All(canvas.Requested, u => Assert.StartsWith(FakeCanvas.Base + "/", u));
        Assert.Empty(sync.Work(force: false).Jobs); // synced just now: nothing until the next hour
    }

    [Theory]
    [InlineData("unauthenticated")]
    [InlineData("bounced to sign in")]
    [InlineData("extension says signed out")]
    public void Unauthenticated_or_a_bounce_to_sign_in_stops_the_sync(string how)
    {
        using var dir = new TempDir();
        var sync = FakeCanvas.Library(dir, () => FakeCanvas.DesignNow);
        var canvas = FakeCanvas.Cs101();
        const string path = "/api/v1/courses/4201/assignments";
        _ = how switch
        {
            "unauthenticated" => canvas.Status(path, 401, """{"status":"unauthenticated","errors":[{"message":"user authorization required"}]}"""),
            // An older extension reports Canvas's sign-in page as a bare 401.
            "bounced to sign in" => canvas.On(path, j => new CanvasResult(j.Id, 401, "", "", "", "", FakeCanvas.Base + "/login/canvas")),
            _ => canvas.On(path, j => new CanvasResult(j.Id, 401, "", "", "", "", FakeCanvas.Base + "/login/saml") { SignedOut = true }),
        };
        Assert.True(canvas.Run(sync));
        Assert.Equal(6, canvas.Requested.Count); // the first batch of per-class asks, then nothing more (the planner's job never goes out)
        Assert.False(sync.Crawl.Active);
        Assert.False(Directory.Exists(Path.Combine(FakeCanvas.CanvasRoot(dir), "assignments")));
        var s = CanvasSettings.Load(dir.Path);
        Assert.True(s.NeedsLogin);
        Assert.Contains("sign in", s.Error, StringComparison.OrdinalIgnoreCase);

        // Signed in again: the next sync reads everything and the warning goes.
        canvas.Json(path, "cs101-assignments.json");
        Assert.True(canvas.Run(sync));
        s = CanvasSettings.Load(dir.Path);
        Assert.False(s.NeedsLogin);
        Assert.Equal("", s.Error);
        Assert.Equal(5, Assignments.Load(dir.Path).Count);
    }

    [Fact]
    public async Task An_ai_reads_canvas_through_the_extension_and_saves_files_only_in_a_class_folder()
    {
        using var dir = new TempDir();
        CanvasSettings.Update(dir.Path, s => { s.Url = "https://canvas.test"; s.Courses["CS 101"] = 42; });
        var sync = new CanvasSync(dir.Path, c => Path.Combine(dir["pool"], c), _ => { });
        Assert.Equal("Only Canvas addresses (or /api/v1/... paths) can be read.", (await sync.FetchAsync("https://evil.test/x", "json"))["error"]!.GetValue<string>());
        Assert.NotNull((await sync.FetchAsync("/files/1", "bytes", "CS 101/../../etc/passwd"))["error"]);

        var asking = sync.FetchAsync("/api/v1/courses/42/pages", "json");
        var work = sync.Work(force: false);
        Assert.True(work.Hot);
        var job = Assert.Single(work.Jobs);
        Assert.Equal("https://canvas.test/api/v1/courses/42/pages", job.Url);
        sync.Results([new CanvasResult(job.Id, 200, "<https://canvas.test/next>; rel=\"next\"", "[1]", "", "", job.Url)]);
        var r = await asking;
        Assert.Equal("[1]", r["json"]!.GetValue<string>());
        Assert.Equal("https://canvas.test/next", r["next_page"]!.GetValue<string>());

        var saving = sync.FetchAsync("/files/9/download", "bytes", "CS 101/Canvas/files/notes.txt");
        job = Assert.Single(sync.Work(false).Jobs);
        sync.Results([new CanvasResult(job.Id, 200, "", "", Convert.ToBase64String("hi"u8.ToArray()), "", job.Url)]);
        Assert.Equal(2, (await saving)["bytes"]!.GetValue<int>());
        Assert.Equal("hi", File.ReadAllText(Path.Combine(dir["pool"], "CS 101", "Canvas", "files", "notes.txt")));
    }

    [Fact]
    public void The_extension_folder_may_reach_only_this_canvas_and_this_library()
    {
        using var dir = new TempDir();
        Extension.Prepare(dir.Path, "http://mini.tail.ts.net:8787", "k3y", "https://canvas.test/");
        var manifest = JsonNode.Parse(File.ReadAllText(dir["manifest.json"]))!;
        Assert.Equal(["https://canvas.test/*", "https://*.inscloudgate.net/*", "http://mini.tail.ts.net:8787/*"],
            manifest["host_permissions"]!.AsArray().Select(h => h!.GetValue<string>()));
        Assert.Contains("\"key\":\"k3y\"", File.ReadAllText(dir["config.js"]));
        Assert.True(File.Exists(dir["background.js"]));
        Assert.Equal(manifest["version"]!.GetValue<string>(), Extension.Version());
    }

    // --- Find my courses: what really went wrong --------------------------------------------------------------

    static CanvasSettings Seen(string when, int protocol = 2) => new() { Url = "https://canvas.test", ExtensionSeen = when, ExtensionProtocol = protocol };

    static string At(DateTimeOffset t) => t.ToString("o", System.Globalization.CultureInfo.InvariantCulture);

    [Fact]
    public void Find_says_found_when_canvas_answered()
    {
        Assert.Equal(FindOutcome.Found, FindOutcome.Of(A("""{"url":"https://canvas.test","available":{}}"""), Seen(At(Now)), Now));
    }

    [Theory]
    [InlineData("Add your school's Canvas address first.", "", "", FindOutcome.NoAddress)] // the library didn't ask Chrome
    [InlineData("Chrome didn't answer. Is Chrome open, with the Study Stash extension on?", "", "", FindOutcome.NoAddress)] // no address, whatever Chrome did
    [InlineData("Chrome isn't signed in to Canvas.", "https://canvas.test", "now", FindOutcome.SignedOut)]
    [InlineData("Chrome didn't answer. Is Chrome open, with the Study Stash extension on?", "https://canvas.test", "", FindOutcome.NoExtension)]
    [InlineData("TypeError: Failed to fetch", "https://canvas.test", "", FindOutcome.NoExtension)]
    [InlineData("Chrome didn't answer. Is Chrome open, with the Study Stash extension on?", "https://canvas.test", "now", FindOutcome.Away)]
    [InlineData("TypeError: Failed to fetch", "https://canvas.test", "an hour ago", FindOutcome.Away)]
    [InlineData("TypeError: Failed to fetch", "https://canvas.test", "now", FindOutcome.Other)]
    [InlineData("refused: not a Canvas URL", "https://canvas.test", "now", FindOutcome.Other)]
    public void Find_says_what_went_wrong(string error, string url, string seen, string outcome)
    {
        var s = new CanvasSettings
        {
            Url = url, ExtensionProtocol = 2,
            ExtensionSeen = seen switch { "now" => At(Now), "an hour ago" => At(Now.AddHours(-1)), _ => "" },
        };
        Assert.Equal(outcome, FindOutcome.Of(new JsonObject { ["error"] = error }, s, Now));
    }

    [Fact]
    public void Each_find_outcome_has_its_own_sentence()
    {
        string[] all = [FindOutcome.Found, FindOutcome.NoAddress, FindOutcome.SignedOut, FindOutcome.NoExtension, FindOutcome.Away, FindOutcome.Other];
        Assert.Equal(all.Length, all.Select(o => FindOutcome.Say(o)).Distinct().Count());
        Assert.Equal("Couldn't find your courses: Canvas answered 503", FindOutcome.Say(FindOutcome.Other, "Canvas answered 503"));
    }

    // --- the extension checking in -----------------------------------------------------------------------------

    [Fact]
    public async Task An_ai_read_that_finds_chrome_signed_out_says_sign_in_until_an_answer_says_otherwise()
    {
        using var dir = new TempDir();
        var sync = FakeCanvas.Library(dir, () => Now);
        CanvasSettings.Update(dir.Path, s => s.Courses.Clear()); // no sync in the way: only the AI's read
        async Task<JsonObject> Read(Func<CanvasJob, CanvasResult> answer)
        {
            var reading = sync.FetchAsync("/api/v1/users/self", "json");
            var job = Assert.Single(sync.Work(false, "1.3", 2).Jobs);
            sync.Results([answer(job)]);
            return await reading;
        }

        var r = await Read(j => new CanvasResult(j.Id, 401, "", """{"status":"unauthenticated","errors":[{"message":"user authorization required"}]}""", "", "", j.Url));
        Assert.Equal("Chrome isn't signed in to Canvas.", r["error"]!.GetValue<string>());
        var st = CanvasSettings.Load(dir.Path);
        Assert.True(st.NeedsLogin);
        Assert.Equal(CanvasSync.SignInError, st.Error);
        Assert.Equal("signed_out", CanvasView.State(sync, Now)["state"]!.GetValue<string>());

        // Signed in again: the next good answer clears it.
        r = await Read(j => new CanvasResult(j.Id, 200, "", """{"id":1}""", "", "", j.Url));
        Assert.Null(r["error"]);
        st = CanvasSettings.Load(dir.Path);
        Assert.False(st.NeedsLogin);
        Assert.Equal("", st.Error);
        Assert.Equal("connected", CanvasView.State(sync, Now)["state"]!.GetValue<string>());
    }

    [Fact]
    public void A_check_in_is_written_down_at_most_every_15_seconds_unless_something_changed()
    {
        using var dir = new TempDir();
        var now = Now;
        var sync = FakeCanvas.Library(dir, () => now);
        CanvasSettings.Update(dir.Path, s => s.Courses.Clear());

        sync.Work(false, "1.4", 3, "http://127.0.0.1:8787");
        var s = CanvasSettings.Load(dir.Path);
        Assert.Equal((At(Now), "1.4", 3, "this_computer"), (s.ExtensionSeen, s.ExtensionVersion, s.ExtensionProtocol, s.ExtensionWhere));
        Assert.True(s.ExtensionConnected(Now));

        var written = File.GetLastWriteTimeUtc(CanvasSettings.PathIn(dir.Path));
        Thread.Sleep(20);
        now = Now.AddSeconds(10);
        sync.Work(false, "1.4", 3, "http://127.0.0.1:8787");
        Assert.Equal(written, File.GetLastWriteTimeUtc(CanvasSettings.PathIn(dir.Path)));
        Assert.Equal(At(Now), CanvasSettings.Load(dir.Path).ExtensionSeen);

        // Another computer's Chrome: written at once.
        sync.Work(false, "1.4", 3, "http://100.64.0.7:8787");
        s = CanvasSettings.Load(dir.Path);
        Assert.Equal((At(now), "another_computer"), (s.ExtensionSeen, s.ExtensionWhere));

        // Quiet but 15 s on: written again.
        now = now.AddSeconds(15);
        sync.Work(false, "1.4", 3, "http://100.64.0.7:8787");
        Assert.Equal(At(now), CanvasSettings.Load(dir.Path).ExtensionSeen);

        // A long-polling extension quiet for 90 s is gone; an older one gets five minutes.
        s = CanvasSettings.Load(dir.Path);
        Assert.False(s.ExtensionConnected(now.AddSeconds(91)));
        s.ExtensionProtocol = 2;
        Assert.True(s.ExtensionConnected(now.AddSeconds(91)));
        Assert.False(s.ExtensionConnected(now.AddMinutes(6)));

        // One from before 1.4 doesn't say where it is.
        sync.Work(false, "1.3", 2);
        Assert.Equal("", CanvasSettings.Load(dir.Path).ExtensionWhere);
    }

    [Fact]
    public void Two_chromes_taking_turns_are_each_written_down_every_15_seconds_and_are_no_update()
    {
        // The library's own Chrome and the laptop's both run the extension, one still on 1.3. Asking in turns, each
        // is written at most every 15 seconds (not on every visit), and 1.3 → 1.4 → 1.3 is never "updated itself".
        using var dir = new TempDir();
        var now = Now;
        var sync = FakeCanvas.Library(dir, () => now);
        CanvasSettings.Update(dir.Path, s => s.Courses.Clear());
        const string Here = "http://127.0.0.1:8787", Laptop = "https://mini.tail.ts.net";

        sync.Work(false, "1.4", 3, Here);
        sync.Work(false, "1.3.9", 3, Laptop);
        var written = File.GetLastWriteTimeUtc(CanvasSettings.PathIn(dir.Path));
        Thread.Sleep(20);
        for (int i = 1; i <= 10; i++)
        {
            now = Now.AddSeconds(i);
            sync.Work(false, "1.4", 3, Here);
            sync.Work(false, "1.3.9", 3, Laptop);
        }
        Assert.Equal(written, File.GetLastWriteTimeUtc(CanvasSettings.PathIn(dir.Path)));
        var s = CanvasSettings.Load(dir.Path);
        Assert.Null(s.ExtensionUpdate);
        Assert.Equal(new ExtensionCopy(At(Now), "1.4", 3), s.ExtensionCopies["this_computer"]);
        Assert.Equal(new ExtensionCopy(At(Now), "1.3.9", 3), s.ExtensionCopies["another_computer"]);

        // The laptop's reloads into 1.4: that one is an update.
        now = Now.AddSeconds(20);
        sync.Work(false, "1.4", 3, Laptop);
        s = CanvasSettings.Load(dir.Path);
        Assert.Equal(new ExtensionUpdate("1.3.9", "1.4", At(now), false), s.ExtensionUpdate);
        Assert.Equal(("1.4", "another_computer"), (s.ExtensionVersion, s.ExtensionWhere));
    }

    [Fact]
    public void A_chrome_from_before_1_4_that_reloads_and_says_where_it_is_is_an_update()
    {
        // 1.3 didn't say where it is; after reloading into 1.4 it does. Same Chrome: "updated itself", and its old
        // unplaced entry goes. A canvas.json from before copies were kept counts its one version as that Chrome's.
        using var dir = new TempDir();
        var sync = FakeCanvas.Library(dir, () => Now);
        CanvasSettings.Update(dir.Path, s => s.Courses.Clear());
        sync.Work(false, "1.3", 2);
        sync.Work(false, "1.4", 3, "http://127.0.0.1:8787");
        var s = CanvasSettings.Load(dir.Path);
        Assert.Equal(new ExtensionUpdate("1.3", "1.4", At(Now), false), s.ExtensionUpdate);
        Assert.Equal(["this_computer"], s.ExtensionCopies.Keys);

        using var old = new TempDir();
        var oldSync = FakeCanvas.Library(old, () => Now);
        CanvasSettings.Update(old.Path, st => { st.Courses.Clear(); st.ExtensionVersion = "1.3"; st.ExtensionSeen = At(Now.AddDays(-1)); });
        oldSync.Work(false, "1.4", 3, "http://127.0.0.1:8787");
        Assert.Equal("1.3", CanvasSettings.Load(old.Path).ExtensionUpdate!.From);
    }

    [Theory]
    [InlineData("http://127.0.0.1:8787", "this_computer")]
    [InlineData("http://localhost:8787", "this_computer")]
    [InlineData("http://[::1]:8787", "this_computer")]
    [InlineData("https://mini.tail.ts.net", "another_computer")]
    [InlineData("http://192.168.1.20:8787", "another_computer")]
    [InlineData("", "")]
    [InlineData(null, "")]
    [InlineData("not an address", "")]
    public void Where_a_chrome_is_comes_from_the_address_it_uses(string? address, string where) =>
        Assert.Equal(where, CanvasSettings.WhereFrom(address));
}
