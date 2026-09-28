using System.Net;
using System.Text.Json;
using StudyStash.App.Services;
using StudyStash.App.ViewModels;
using StudyStash.Core;

namespace StudyStash.App.Tests;

public class CanvasConnectTests
{
    static CanvasConnectModel Model(FakeLibrary handler, string? home = null, List<(string What, string Arg)>? log = null, bool forSetup = false)
    {
        var context = CanvasFixtures.Context(handler, home, log);
        return new CanvasConnectModel(context, new CanvasWatch(context), forSetup);
    }

    /// <summary>GET canvas from an older library whose extension hasn't checked in yet.</summary>
    const string NoExtensionYet = """{"url": "https://school.instructure.com", "extension_seen": "", "extension_version": ""}""";

    [Theory]
    [InlineData("state-not-set-up", 1)]
    [InlineData("state-no-extension", 2)]
    [InlineData("state-signed-out", 3)]
    public async Task StartAsync_opens_at_the_step_the_state_still_needs(string fixture, int step)
    {
        var handler = new FakeLibrary().Json(HttpMethod.Get, "/api/v2/canvas/extension", "extension");
        var m = Model(handler);
        await m.StartAsync(CanvasFixtures.Load<CanvasApi.State>(fixture), [], TestContext.Current.CancellationToken);
        Assert.Equal(step, m.Current);
    }

    [Theory]
    [InlineData("""{"state": "chrome_away", "url": "https://canvas.test", "extension": {"seen": "2025-09-20T10:00:00Z", "connected": false, "key_matches": true}}""")]
    [InlineData("""{"state": "no_extension", "url": "https://canvas.test", "extension": {"seen": null, "connected": false, "key_matches": false, "last_seen": "2025-09-25T17:23:00Z"}}""")]
    [InlineData("""{"state": "connected", "url": "https://canvas.test", "extension": {"seen": "2025-09-25T17:23:00Z", "connected": false}}""")]
    public async Task A_chrome_that_is_not_checking_in_now_with_the_current_key_opens_at_the_extension_step(string json)
    {
        var handler = new FakeLibrary().Json(HttpMethod.Get, "/api/v2/canvas/extension", "extension");
        var m = Model(handler);
        var linked = new List<CanvasApi.ClassRow> { new() { Class = "CS 101", Linked = true } };
        await m.StartAsync(JsonSerializer.Deserialize<CanvasApi.State>(json, CanvasApi.Json)!, linked, TestContext.Current.CancellationToken);
        Assert.Equal(2, m.Current);
    }

    [Fact]
    public async Task With_no_class_linked_it_finds_courses_itself_and_lands_on_match()
    {
        var classes = new List<CanvasApi.ClassRow> { new() { Class = "CS 101", Linked = false, Suggested = "4201" } };
        var handler = new FakeLibrary()
            .Json(HttpMethod.Post, "/api/v2/canvas/courses", "canvas")
            .Json(HttpMethod.Get, "/api/v2/canvas/classes", JsonSerializer.Serialize(classes, CanvasApi.Json));
        var m = Model(handler);

        await m.StartAsync(CanvasFixtures.Load<CanvasApi.State>("state-connected"), classes, TestContext.Current.CancellationToken);

        Assert.Equal(4, m.Current);
        Assert.Equal("Found 4 courses.", m.CoursesSay);
        var row = Assert.Single(m.Courses);
        Assert.Equal("4201", row.Selected?.Id); // the suggested course, preselected
    }

    [Fact]
    public async Task With_every_class_already_linked_it_goes_straight_to_sync_and_shows_the_last_one()
    {
        var classes = CanvasFixtures.Load<List<CanvasApi.ClassRow>>("classes"); // three of four linked
        var m = Model(new FakeLibrary());

        await m.StartAsync(CanvasFixtures.Load<CanvasApi.State>("state-connected"), classes, TestContext.Current.CancellationToken);

        Assert.Equal(5, m.Current);
        Assert.Equal("Canvas synced 10:24.", m.SyncedLine);
    }

    [Fact]
    public async Task Continue_saves_the_school_and_moves_to_the_extension()
    {
        var log = new List<(string What, string Arg)>();
        var handler = new FakeLibrary()
            .Json(HttpMethod.Post, "/api/v2/canvas", "canvas")
            .Json(HttpMethod.Get, "/api/v2/canvas/state", "state-no-extension")
            .Json(HttpMethod.Get, "/api/v2/canvas/classes", "[]")
            .Json(HttpMethod.Get, "/api/v2/canvas/extension", "extension");
        var m = Model(handler, log: log);
        await m.StartAsync(CanvasFixtures.Load<CanvasApi.State>("state-not-set-up"), [], TestContext.Current.CancellationToken);

        m.SchoolField = "school.instructure.com";
        await m.ContinueCommand.ExecuteAsync(null);

        Assert.Equal(2, m.Current);
        var sent = Assert.Single(handler.Requests, r => r.Method == "POST" && r.Path == "/api/v2/canvas");
        Assert.Equal("{\"url\":\"school.instructure.com\"}", sent.Body);
        Assert.Empty(log); // Chrome opens only when Add to Chrome is pressed
        m.Dispose();
    }

    [Fact]
    public async Task Continue_shows_the_librarys_detail_on_a_bad_address()
    {
        var handler = new FakeLibrary().Status(HttpMethod.Post, "/api/v2/canvas", HttpStatusCode.BadRequest, "{\"detail\":\"That isn't a web address.\"}");
        var m = Model(handler);
        await m.StartAsync(CanvasFixtures.Load<CanvasApi.State>("state-not-set-up"), [], TestContext.Current.CancellationToken);

        m.SchoolField = "not a web address";
        await m.ContinueCommand.ExecuteAsync(null);

        Assert.Equal("That isn't a web address.", m.SchoolError);
        Assert.Equal(1, m.Current); // stays put — nothing to move on to yet
    }

    [Fact]
    public async Task Add_to_Chrome_changes_nothing_until_pressed_but_hurries_the_watch()
    {
        var log = new List<(string What, string Arg)>();
        var handler = new FakeLibrary().Json(HttpMethod.Get, "/api/v2/canvas/state", "state-no-extension").Json(HttpMethod.Get, "/api/v2/canvas", NoExtensionYet);
        var context = CanvasFixtures.Context(handler, "the-home", log);
        var watch = new CanvasWatch(context);
        var m = new CanvasConnectModel(context, watch);

        await m.StartAsync(CanvasFixtures.Load<CanvasApi.State>("state-no-extension"), [], TestContext.Current.CancellationToken);

        Assert.Equal(2, m.Current);
        Assert.Equal("Chrome extension", m.Step2.Title);
        Assert.True(m.ShowAddToChrome);
        Assert.False(m.ShowChromeStatus);
        Assert.Empty(log);
        Assert.Equal(TimeSpan.FromSeconds(3), watch.NextDelay);

        m.Dispose(); // leaving lets the watch go back to its own pace
        Assert.NotEqual(TimeSpan.FromSeconds(3), watch.NextDelay);
        Assert.False(watch.Running);
    }

    [Fact]
    public async Task Add_to_Chrome_shows_the_folder_then_opens_chromes_extensions_page_once_each()
    {
        var log = new List<(string What, string Arg)>();
        var handler = new FakeLibrary()
            .Json(HttpMethod.Get, "/api/v2/canvas/state", "state-no-extension")
            .Json(HttpMethod.Get, "/api/v2/canvas", NoExtensionYet)
            .Json(HttpMethod.Get, "/api/v2/canvas/extension", "extension");
        var m = Model(handler, home: "the-home", log: log);
        await m.StartAsync(CanvasFixtures.Load<CanvasApi.State>("state-no-extension"), [], TestContext.Current.CancellationToken);

        await m.AddToChromeCommand.ExecuteAsync(null);

        string folder = Path.Combine("the-home", "chrome-extension");
        Assert.Equal(folder, m.ExtensionFolder);
        Assert.Equal([("PrepareExtension", "test-key-abc123 https://school.instructure.com"), ("RevealFolder", folder), ("OpenChromeExtensions", "")], log);
        Assert.True(m.AddedToChrome);
        Assert.False(m.ShowAddToChrome); // the button gives way to the quiet links
        Assert.True(m.WaitingForChrome);
        Assert.Equal(2, m.Current);

        log.Clear();
        m.ShowFolderCommand.Execute(null);
        Assert.Equal([("RevealFolder", folder)], log);
        m.Dispose();
    }

    [Fact]
    public async Task Add_to_Chrome_says_so_when_the_library_has_no_folder_for_it()
    {
        var log = new List<(string What, string Arg)>();
        var handler = new FakeLibrary().Json(HttpMethod.Get, "/api/v2/canvas", NoExtensionYet); // no canvas/extension: nothing to make
        var m = Model(handler, home: "the-home", log: log);
        await m.StartAsync(CanvasFixtures.Load<CanvasApi.State>("state-no-extension"), [], TestContext.Current.CancellationToken);

        await m.AddToChromeCommand.ExecuteAsync(null);

        Assert.NotNull(m.ChromeError);
        Assert.False(m.AddedToChrome);
        Assert.Empty(log);
        m.Dispose();
    }

    [Fact]
    public async Task The_extension_turning_up_says_connected_and_moves_on_to_finding_courses()
    {
        var handler = new FakeLibrary()
            .Json(HttpMethod.Get, "/api/v2/canvas", NoExtensionYet)
            .Json(HttpMethod.Get, "/api/v2/canvas/extension", "extension")
            .Json(HttpMethod.Get, "/api/v2/canvas/state", "state-no-extension")
            .Json(HttpMethod.Post, "/api/v2/canvas/courses", "canvas")
            .Json(HttpMethod.Get, "/api/v2/canvas/classes", "classes");
        var context = CanvasFixtures.Context(handler, "the-home");
        var watch = new CanvasWatch(context);
        var m = new CanvasConnectModel(context, watch);
        await m.StartAsync(CanvasFixtures.Load<CanvasApi.State>("state-no-extension"), [], TestContext.Current.CancellationToken);
        await m.AddToChromeCommand.ExecuteAsync(null);
        await Task.Delay(40, TestContext.Current.CancellationToken); // the hurried watch asks at once: still waiting
        Assert.True(m.WaitingForChrome);

        handler.Json(HttpMethod.Get, "/api/v2/canvas/state", "state-connected"); // Chrome loads the extension
        await watch.RefreshAsync(TestContext.Current.CancellationToken); // the library now says "connected"
        await Task.Delay(40, TestContext.Current.CancellationToken); // Changed fires the advance without awaiting it

        Assert.True(m.ChromeConnected);
        Assert.False(m.WaitingForChrome);
        Assert.Equal("Connected", m.Step2.Summary);
        Assert.Equal("Found 4 courses.", m.CoursesSay); // straight on to finding courses, then matching them
        Assert.Equal(4, m.Current);
        m.Dispose();
    }

    [Fact]
    public async Task A_library_that_says_a_chrome_is_connected_is_believed_before_its_state_catches_up()
    {
        var handler = new FakeLibrary()
            .Json(HttpMethod.Get, "/api/v2/canvas/state", "state-no-extension")
            .Json(HttpMethod.Get, "/api/v2/canvas", """{"url": "https://school.instructure.com", "extension": {"connected": true, "folder": "", "version": "1.4"}}""");
        var context = CanvasFixtures.Context(handler);
        var watch = new CanvasWatch(context);
        var m = new CanvasConnectModel(context, watch, forSetup: true);
        await m.StartAsync(CanvasFixtures.Load<CanvasApi.State>("state-no-extension"), [], TestContext.Current.CancellationToken);

        await watch.RefreshAsync(TestContext.Current.CancellationToken);
        await Task.Delay(40, TestContext.Current.CancellationToken);

        Assert.True(m.ChromeConnected);
        Assert.Equal(3, m.Current);
        m.Dispose();
    }

    [Fact]
    public async Task In_setup_it_is_three_parts_and_finding_courses_finishes_it_with_each_courses_name_and_code()
    {
        var handler = new FakeLibrary().Json(HttpMethod.Post, "/api/v2/canvas/courses", """
            {"url": "https://school.instructure.com",
             "available": {"4201": "Intro to Programming", "4202": "BIO 110 · Cell Biology", "4205": "Study Skills", "4206": "202710.TS.CSCI321.A - Software Engineering"},
             "course_info": {"4201": {"code": "COMP 101", "name": "Intro to Programming", "term": "Fall 2025"},
                             "4206": {"code": "202710.TS.CSCI321.A", "name": "202710.TS.CSCI321.A - Software Engineering", "term": "Fall 2026"}}}
            """);
        var m = Model(handler, forSetup: true);

        await m.StartAsync(CanvasFixtures.Load<CanvasApi.State>("state-connected"), [], TestContext.Current.CancellationToken);

        Assert.Equal(["School address", "Chrome extension", "Your courses"], m.Steps.Select(s => s.Title));
        Assert.False(m.ShowMatchAndSync);
        Assert.True(m.AllDone);
        Assert.All(m.Steps, s => Assert.True(s.IsDone));
        Assert.Equal("Found 4 courses", m.Step3.Summary);
        Assert.Equal([("Cell Biology", "BIO 110", "4202"), ("Intro to Programming", "COMP 101", "4201"), ("Software Engineering", "CSCI 321", "4206"), ("Study Skills", "", "4205")],
            m.Found.Select(f => (f.ClassName, f.Code, f.Id)));
        Assert.DoesNotContain(handler.Requests, r => r.Path == "/api/v2/canvas/classes"); // no matching in setup
    }

    [Fact]
    public async Task Signed_out_shows_the_sign_in_line_and_opens_canvas_in_chrome()
    {
        var log = new List<(string What, string Arg)>();
        var m = Model(new FakeLibrary(), log: log);

        await m.StartAsync(CanvasFixtures.Load<CanvasApi.State>("state-signed-out"), [], TestContext.Current.CancellationToken);
        Assert.True(m.SignedOut);

        m.OpenCanvasCommand.Execute(null);
        Assert.Contains(log, l => l.What == "OpenInChrome" && l.Arg == "https://school.instructure.com");
    }

    [Fact]
    public async Task Signing_in_retries_finding_courses_on_its_own()
    {
        var classes = new List<CanvasApi.ClassRow> { new() { Class = "CS 101", Linked = false, Suggested = "4201" } };
        var handler = new FakeLibrary()
            .Json(HttpMethod.Get, "/api/v2/canvas/state", "state-connected")
            .Json(HttpMethod.Post, "/api/v2/canvas/courses", "canvas")
            .Json(HttpMethod.Get, "/api/v2/canvas/classes", JsonSerializer.Serialize(classes, CanvasApi.Json));
        var context = CanvasFixtures.Context(handler);
        var watch = new CanvasWatch(context);
        var m = new CanvasConnectModel(context, watch);
        await m.StartAsync(CanvasFixtures.Load<CanvasApi.State>("state-signed-out"), classes, TestContext.Current.CancellationToken);
        Assert.Equal(3, m.Current);
        Assert.True(m.SignedOut);

        await watch.RefreshAsync(TestContext.Current.CancellationToken); // now "connected" — signed in
        await Task.Delay(20, TestContext.Current.CancellationToken);

        Assert.False(m.SignedOut);
        Assert.Equal(4, m.Current);
    }

    [Fact]
    public async Task Find_courses_builds_rows_with_the_suggested_course_preselected()
    {
        var classes = new List<CanvasApi.ClassRow>
        {
            new() { Class = "CS 101", Linked = false, Suggested = "4201" },
            new() { Class = "HIST 210", Linked = false, Suggested = null },
        };
        var handler = new FakeLibrary()
            .Json(HttpMethod.Post, "/api/v2/canvas/courses", "canvas")
            .Json(HttpMethod.Get, "/api/v2/canvas/classes", JsonSerializer.Serialize(classes, CanvasApi.Json));
        var m = Model(handler);

        await m.StartAsync(CanvasFixtures.Load<CanvasApi.State>("state-connected"), classes, TestContext.Current.CancellationToken);

        Assert.Equal(4, m.Current);
        Assert.Equal(2, m.Courses.Count);
        Assert.Equal("4201", m.Courses[0].Selected?.Id);
        Assert.Equal("Choose a course…", m.Courses[1].Selected?.Label);
        Assert.Contains(m.Courses[0].Choices, c => c.Id == "4204"); // every found course offered, not just the suggested one
    }

    [Fact]
    public async Task Link_these_posts_every_rows_choice_including_the_ones_left_unmatched()
    {
        var classes = new List<CanvasApi.ClassRow>
        {
            new() { Class = "CS 101", Linked = false, Suggested = "4201" },
            new() { Class = "HIST 210", Linked = false, Suggested = null },
        };
        var handler = new FakeLibrary()
            .Json(HttpMethod.Post, "/api/v2/canvas/courses", "canvas")
            .Json(HttpMethod.Get, "/api/v2/canvas/classes", JsonSerializer.Serialize(classes, CanvasApi.Json))
            .Json(HttpMethod.Post, "/api/v2/canvas", "canvas");
        var m = Model(handler);
        await m.StartAsync(CanvasFixtures.Load<CanvasApi.State>("state-connected"), classes, TestContext.Current.CancellationToken);

        await m.LinkTheseCommand.ExecuteAsync(null);

        Assert.Equal(5, m.Current);
        var sent = handler.Requests.Where(r => r.Method == "POST" && r.Path == "/api/v2/canvas").ToList();
        var courses = Assert.Single(sent, r => r.Body!.Contains("courses"));
        Assert.Equal("{\"courses\":{\"CS 101\":4201,\"HIST 210\":0}}", courses.Body);
    }

    [Fact]
    public async Task Sync_now_posts_sync_true()
    {
        var handler = new FakeLibrary()
            .Json(HttpMethod.Post, "/api/v2/canvas", "canvas");
        var classes = CanvasFixtures.Load<List<CanvasApi.ClassRow>>("classes");
        var m = Model(handler);
        await m.StartAsync(CanvasFixtures.Load<CanvasApi.State>("state-connected"), classes, TestContext.Current.CancellationToken);
        Assert.Equal(5, m.Current);

        await m.SyncNowCommand.ExecuteAsync(null);

        Assert.True(m.Syncing);
        var sent = Assert.Single(handler.Requests, r => r.Method == "POST" && r.Path == "/api/v2/canvas");
        Assert.Equal("{\"sync\":true}", sent.Body);
    }

    [Fact]
    public async Task Change_reopens_the_school_step_from_anywhere()
    {
        var handler = new FakeLibrary().Json(HttpMethod.Get, "/api/v2/canvas/extension", "extension");
        var m = Model(handler);
        await m.StartAsync(CanvasFixtures.Load<CanvasApi.State>("state-no-extension"), [], TestContext.Current.CancellationToken);
        Assert.Equal(2, m.Current);

        m.ChangeSchoolCommand.Execute(null);

        Assert.Equal(1, m.Current);
        Assert.Equal("school.instructure.com", m.SchoolField);
    }

    [Fact]
    public async Task CanFinish_is_false_while_any_step_is_busy()
    {
        var m = Model(new FakeLibrary());
        await m.StartAsync(CanvasFixtures.Load<CanvasApi.State>("state-not-set-up"), [], TestContext.Current.CancellationToken);
        Assert.True(m.CanFinish);

        m.Syncing = true;
        Assert.False(m.CanFinish);
        m.Syncing = false;
        Assert.True(m.CanFinish);
    }

    [Fact]
    public async Task Skip_back_and_finish_call_the_host_back()
    {
        var m = Model(new FakeLibrary());
        await m.StartAsync(CanvasFixtures.Load<CanvasApi.State>("state-not-set-up"), [], TestContext.Current.CancellationToken);
        bool skipped = false, back = false, finished = false;
        m.OnSkip = () => skipped = true;
        m.OnBack = () => back = true;
        m.OnFinish = () => finished = true;

        m.SkipCommand.Execute(null);
        m.BackCommand.Execute(null);
        m.FinishCommand.Execute(null);

        Assert.True(skipped);
        Assert.True(back);
        Assert.True(finished);
    }

    // ---- Chrome.cs: never launches anything real in a test ----

    [Fact]
    public void Chrome_reports_when_nothing_could_be_started()
    {
        Assert.Equal(Chrome.NotInstalled, Chrome.Open(null, (_, _, _) => null));
    }

    [Fact]
    public void Chrome_asks_for_the_url_it_was_given()
    {
        if (!OperatingSystem.IsMacOS() && !OperatingSystem.IsWindows()) return; // the app opens Chrome on a Mac or Windows
        string? exe = null;
        IReadOnlyList<string>? args = null;
        string? result = Chrome.Open("https://school.instructure.com", (e, a, _) =>
        {
            exe = e;
            args = a;
            return new ProcResult(0, "");
        });

        Assert.Null(result);
        Assert.NotNull(exe);
        Assert.Contains("https://school.instructure.com", args!);
    }

    [Fact]
    public void Chrome_extensions_opens_the_extensions_page()
    {
        if (!OperatingSystem.IsMacOS() && !OperatingSystem.IsWindows()) return; // the app opens Chrome on a Mac or Windows
        IReadOnlyList<string>? args = null;
        Chrome.OpenExtensions((_, a, _) =>
        {
            args = a;
            return new ProcResult(0, "");
        });

        Assert.Contains("chrome://extensions", args!);
    }
}
