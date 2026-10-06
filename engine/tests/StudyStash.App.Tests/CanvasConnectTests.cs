using System.Net;
using System.Text.Json;
using StudyStash.App.Services;
using StudyStash.App.ViewModels;

namespace StudyStash.App.Tests;

public class CanvasConnectTests
{
    static CanvasConnectModel Model(FakeLibrary handler, string? home = null, List<(string What, string Arg)>? log = null, bool forSetup = false,
        IReadOnlyList<Browser>? browsers = null, string? notInstalled = null, BrowserAdvice? advice = null)
    {
        var context = CanvasFixtures.Context(handler, home, log, browsers, notInstalled, advice);
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
    public async Task A_browser_that_is_not_checking_in_now_with_the_current_key_opens_at_the_extension_step(string json)
    {
        var handler = new FakeLibrary().Json(HttpMethod.Get, "/api/v2/canvas/extension", "extension");
        var m = Model(handler);
        var linked = new List<CanvasApi.ClassRow> { new() { Class = "CS 101", Linked = true } };
        await m.StartAsync(JsonSerializer.Deserialize<CanvasApi.State>(json, CanvasApi.Json)!, linked, TestContext.Current.CancellationToken);
        Assert.Equal(2, m.Current);
    }

    [Fact]
    public async Task With_no_class_linked_it_finds_courses_itself_offers_them_to_tick_then_lands_on_match()
    {
        var classes = new List<CanvasApi.ClassRow> { new() { Class = "CS 101", Linked = false, Suggested = "4201" } };
        var handler = new FakeLibrary()
            .Json(HttpMethod.Post, "/api/v2/canvas/courses", "canvas")
            .Json(HttpMethod.Post, "/api/v2/canvas/choose", "canvas")
            .Json(HttpMethod.Get, "/api/v2/canvas/classes", JsonSerializer.Serialize(classes, CanvasApi.Json));
        var m = Model(handler);

        await m.StartAsync(CanvasFixtures.Load<CanvasApi.State>("state-connected"), classes, TestContext.Current.CancellationToken);

        // Your courses stays open with every course to tick, and Bring in in its row.
        Assert.Equal(3, m.Current);
        Assert.Equal("Found 4 courses. Tick the ones to bring in.", m.CoursesSay);
        Assert.True(m.ShowPicker);
        Assert.True(m.ShowBringIn);
        Assert.False(m.ShowFind);
        // A library from before the choice: the courses its classes link are the ones ticked.
        Assert.Equal(4, m.Picker.Courses.Count);
        Assert.Equal(["4201", "4202", "4203"], m.Picker.Courses.Where(c => c.Ticked).Select(c => c.Id).Order());
        Assert.Equal("Bring in 3 courses", m.BringInLabel);
        m.Picker.Courses.First(c => c.Id == "4203").Ticked = false;
        Assert.Equal("Bring in 2 courses", m.BringInLabel);

        await m.BringInCommand.ExecuteAsync(null);

        var chose = Assert.Single(handler.Requests, r => r.Method == "POST" && r.Path == "/api/v2/canvas/choose");
        Assert.Contains("\"match\":true", chose.Body);
        Assert.Contains("\"keep\":true", chose.Body);
        Assert.Equal(4, m.Current);
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
        Assert.Empty(log); // the browser opens only when Add to Chrome is pressed
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
        Assert.Equal("Browser extension", m.Step2.Title);
        Assert.Equal("Add to Chrome", m.AddLabel);
        Assert.True(m.ShowAddToBrowser);
        Assert.False(m.ShowBrowserStatus);
        Assert.False(m.CanSwitchBrowser); // one browser here: nothing to switch to
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

        await m.AddToBrowserCommand.ExecuteAsync(null);

        string folder = Path.Combine("the-home", "chrome-extension");
        Assert.Equal(folder, m.ExtensionFolder);
        Assert.Equal([("PrepareExtension", "test-key-abc123 https://school.instructure.com"), ("RevealFolder", folder), ("OpenExtensions", "Chrome"),
            ("RememberBrowser", "Chrome")], log);
        Assert.True(m.AddedToBrowser);
        Assert.False(m.ShowAddToBrowser); // the button gives way to the quiet links
        Assert.True(m.WaitingForBrowser);
        Assert.Equal("Waiting for Chrome…", m.WaitingLabel);
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

        await m.AddToBrowserCommand.ExecuteAsync(null);

        Assert.NotNull(m.BrowserError);
        Assert.False(m.AddedToBrowser);
        Assert.Empty(log);
        m.Dispose();
    }

    [Fact]
    public async Task A_browser_that_isnt_on_this_computer_is_said_and_the_button_stays()
    {
        var handler = new FakeLibrary()
            .Json(HttpMethod.Get, "/api/v2/canvas", NoExtensionYet)
            .Json(HttpMethod.Get, "/api/v2/canvas/extension", "extension");
        var m = Model(handler, home: "the-home", notInstalled: Browsers.NotInstalled);
        await m.StartAsync(CanvasFixtures.Load<CanvasApi.State>("state-no-extension"), [], TestContext.Current.CancellationToken);

        await m.AddToBrowserCommand.ExecuteAsync(null);

        Assert.Equal(Browsers.NotInstalled, m.BrowserError);
        Assert.Contains("Chrome, Edge, Brave", m.BrowserError); // the browsers to choose from, not only Chrome
        Assert.True(m.ShowAddToBrowser); // "Install one, then try again."
        Assert.False(m.WaitingForBrowser);
        m.Dispose();
    }

    /// <summary>A student who uses Safari is told the extension can't go in it and which browser Study Stash will use
    /// (the one the step offers, also after they pick another); one with nothing that will do gets a way to Chrome.</summary>
    [Fact]
    public async Task A_usual_browser_that_cant_take_the_extension_is_said_with_the_one_to_use_or_where_to_get_one()
    {
        var log = new List<(string What, string Arg)>();
        var handler = new FakeLibrary()
            .Json(HttpMethod.Get, "/api/v2/canvas", NoExtensionYet)
            .Json(HttpMethod.Get, "/api/v2/canvas/extension", "extension");
        var m = Model(handler, home: "the-home", log: log, browsers: [Browsers.Chrome, Browsers.Edge], advice: new BrowserAdvice(Browsers.Safari, NoneHere: false));
        await m.StartAsync(CanvasFixtures.Load<CanvasApi.State>("state-no-extension"), [], TestContext.Current.CancellationToken);
        Assert.True(m.ShowAdvice);
        Assert.Equal("Your usual browser, Safari, can’t run the Study Stash extension. Study Stash will use Chrome instead.", m.AdviceText);
        Assert.False(m.ShowGetBrowser);
        m.BrowserChoices[1].Pick.Execute(null);
        Assert.EndsWith("Study Stash will use Edge instead.", m.AdviceText);
        await m.AddToBrowserCommand.ExecuteAsync(null);
        Assert.False(m.ShowAdvice); // added: the step is about Edge's own page now
        m.Dispose();

        // Nothing here will do: Chrome's download page is one click away, in whatever this computer opens links with.
        log.Clear();
        var bare = Model(handler, home: "the-home", log: log, notInstalled: Browsers.NotInstalled, advice: new BrowserAdvice(Browsers.Safari, NoneHere: true));
        await bare.StartAsync(CanvasFixtures.Load<CanvasApi.State>("state-no-extension"), [], TestContext.Current.CancellationToken);
        Assert.True(bare.ShowGetBrowser);
        Assert.EndsWith("Get Chrome, then press Add to Chrome.", bare.AdviceText);
        bare.GetBrowserCommand.Execute(null);
        Assert.Contains(("OpenUrl", Browsers.GetChrome), log);
        await bare.AddToBrowserCommand.ExecuteAsync(null);
        Assert.True(bare.ShowGetBrowser); // still not there: the way to it stays
        bare.Dispose();

        // The usual browser takes it: nothing to say.
        var fine = Model(handler, home: "the-home");
        Assert.False(fine.ShowAdvice);
        fine.Dispose();
    }

    [Fact]
    public async Task With_two_browsers_here_the_student_can_use_the_other_and_the_step_follows()
    {
        var log = new List<(string What, string Arg)>();
        var handler = new FakeLibrary()
            .Json(HttpMethod.Get, "/api/v2/canvas", NoExtensionYet)
            .Json(HttpMethod.Get, "/api/v2/canvas/extension", "extension");
        var m = Model(handler, home: "the-home", log: log, browsers: [Browsers.Chrome, Browsers.Edge]);
        await m.StartAsync(CanvasFixtures.Load<CanvasApi.State>("state-no-extension"), [], TestContext.Current.CancellationToken);
        Assert.Equal("Add to Chrome", m.AddLabel); // the one to use comes first
        Assert.True(m.CanSwitchBrowser);
        Assert.Equal(["Chrome", "Edge"], m.BrowserChoices.Select(c => c.Name));
        Assert.Empty(log);

        m.BrowserChoices[1].Pick.Execute(null);

        Assert.Equal("Add to Edge", m.AddLabel);
        Assert.Contains("opens Edge’s extensions page", m.BrowserHelp);
        Assert.True(m.IsChromium); // Edge loads the folder like Chrome: the same three pictures
        Assert.Equal([false, true], m.BrowserChoices.Select(c => c.Current));
        Assert.Equal([("RememberBrowser", "Edge")], log); // the one Study Stash opens Canvas in from now on

        log.Clear();
        await m.AddToBrowserCommand.ExecuteAsync(null);
        Assert.Contains(("OpenExtensions", "Edge"), log);
        Assert.Equal("Waiting for Edge…", m.WaitingLabel);
        Assert.Equal("Open Edge extensions", m.OpenExtensionsLabel);
        m.Dispose();
    }

    [Fact]
    public async Task Firefox_shows_the_code_and_opens_the_add_on_and_never_a_folder()
    {
        var log = new List<(string What, string Arg)>();
        var handler = new FakeLibrary()
            .Json(HttpMethod.Get, "/api/v2/canvas", NoExtensionYet)
            .Json(HttpMethod.Get, "/api/v2/canvas/extension", "extension");
        var m = Model(handler, home: "the-home", log: log, browsers: [Browsers.Firefox]);
        await m.StartAsync(CanvasFixtures.Load<CanvasApi.State>("state-no-extension"), [], TestContext.Current.CancellationToken);
        Assert.True(m.IsFirefox);
        Assert.Equal("Add to Firefox", m.AddLabel);
        Assert.Equal("In Firefox, click Add when it asks to add Study Stash for Canvas.", m.FirefoxStep1);

        await m.AddToBrowserCommand.ExecuteAsync(null);

        // The add-on, in Firefox; nothing written or shown on this computer.
        Assert.Equal([("OpenAddOn", "Firefox"), ("RememberBrowser", "Firefox")], log);
        Assert.Null(m.ExtensionFolder);
        Assert.False(m.ShowFolderLinks);
        Assert.True(m.ShowAddOnLink);
        // The code to paste connects to the library as this computer reaches it, with the extension's key and the school.
        Assert.True(m.ShowCode);
        Assert.Equal(new StudyStash.Core.Canvas.ExtensionConnection("https://library.test", "test-key-abc123", "https://school.instructure.com"),
            StudyStash.Core.Canvas.Extension.ReadConnectionCode(m.ConnectionCode));
        Assert.True(m.WaitingForBrowser);
        Assert.Equal("Waiting for Firefox…", m.WaitingLabel);

        log.Clear();
        m.CopyCodeCommand.Execute(null);
        Assert.Equal([("Copy", m.ConnectionCode)], log);
        Assert.Equal("Copied", m.CopyLabel);
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
        await m.AddToBrowserCommand.ExecuteAsync(null);
        await Task.Delay(40, TestContext.Current.CancellationToken); // the hurried watch asks at once: still waiting
        Assert.True(m.WaitingForBrowser);

        handler.Json(HttpMethod.Get, "/api/v2/canvas/state", "state-connected"); // Chrome loads the extension
        await watch.RefreshAsync(TestContext.Current.CancellationToken); // the library now says "connected"
        await Task.Delay(40, TestContext.Current.CancellationToken); // Changed fires the advance without awaiting it

        Assert.True(m.BrowserConnected);
        Assert.False(m.WaitingForBrowser);
        Assert.Equal("Connected", m.Step2.Summary);
        Assert.Equal("Found 4 courses. Tick the ones to bring in.", m.CoursesSay); // straight on to finding courses
        Assert.Equal(3, m.Current);
        Assert.True(m.ShowPicker);
        m.Dispose();
    }

    [Fact]
    public async Task A_library_that_says_a_browser_is_connected_is_believed_before_its_state_catches_up()
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

        Assert.True(m.BrowserConnected);
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

        Assert.Equal(["School address", "Browser extension", "Your courses"], m.Steps.Select(s => s.Title));
        Assert.False(m.ShowMatchAndSync);
        Assert.True(m.AllDone);
        Assert.All(m.Steps, s => Assert.True(s.IsDone));
        Assert.Equal("Found 4 courses", m.Step3.Summary);
        Assert.Equal([("Cell Biology", "BIO 110", "4202"), ("Intro to Programming", "COMP 101", "4201"), ("Software Engineering", "CSCI 321", "4206"), ("Study Skills", "", "4205")],
            m.Found.Select(f => (f.ClassName, f.Code, f.Id)));
        Assert.DoesNotContain(handler.Requests, r => r.Path == "/api/v2/canvas/classes"); // no matching in setup
    }

    [Fact]
    public async Task Signed_out_shows_the_sign_in_line_and_opens_canvas_in_the_browser()
    {
        var log = new List<(string What, string Arg)>();
        var m = Model(new FakeLibrary(), log: log);

        await m.StartAsync(CanvasFixtures.Load<CanvasApi.State>("state-signed-out"), [], TestContext.Current.CancellationToken);
        Assert.True(m.SignedOut);
        Assert.Equal("Sign in to Canvas in Chrome. Study Stash looks again by itself once you have.", m.SignInHelp); // the browser that checked in

        m.OpenCanvasCommand.Execute(null);
        Assert.Contains(log, l => l.What == "OpenInBrowser" && l.Arg == "https://school.instructure.com");
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
        Assert.Equal(3, m.Current);
        Assert.True(m.Picker.HasCourses); // found them on its own, ready to tick
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
            .Json(HttpMethod.Post, "/api/v2/canvas/choose", "canvas")
            .Json(HttpMethod.Get, "/api/v2/canvas/classes", JsonSerializer.Serialize(classes, CanvasApi.Json));
        var m = Model(handler);

        await m.StartAsync(CanvasFixtures.Load<CanvasApi.State>("state-connected"), classes, TestContext.Current.CancellationToken);
        await m.BringInCommand.ExecuteAsync(null);

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
            .Json(HttpMethod.Post, "/api/v2/canvas/choose", "canvas")
            .Json(HttpMethod.Post, "/api/v2/canvas", "canvas");
        var m = Model(handler);
        await m.StartAsync(CanvasFixtures.Load<CanvasApi.State>("state-connected"), classes, TestContext.Current.CancellationToken);
        await m.BringInCommand.ExecuteAsync(null);

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
}
