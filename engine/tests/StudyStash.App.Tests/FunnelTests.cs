using System.Text.Json;
using Avalonia.Headless.XUnit;
using StudyStash.App.Services;
using StudyStash.App.ViewModels;
using StudyStash.Core.Ai;
using StudyStash.Core.Canvas;

namespace StudyStash.App.Tests;

/// <summary>
/// The first run's funnel, every way through it a student can take: a student with Canvas (the add-on finding it by
/// itself, or the school typed while the stores still have the old add-on), one whose school has none, someone who
/// isn't a student; the first sync done, done with some of Canvas missing, stopped and tried again, waiting for a
/// sign-in, and saying what it waits for when it's slow; and the AI step: each AI, Continue with Google as the default,
/// the AI found here already, Google turning a school account down, and the one more push before Later.
/// </summary>
public class FunnelTests
{
    /// <summary>The funnel over a setup whose Get started makes a pretend library at once, with fakes for everything it
    /// reaches. <paramref name="used"/> collects the AIs the library was told to write with.</summary>
    static (FunnelModel Funnel, SetupModel Setup) Make(FunnelServices? services = null)
    {
        var setup = SetupModel.For(SkinKind.Mac);
        setup.OneButton = true;
        setup.OnConnect = () =>
        {
            setup.LibraryOk = true;
            return Task.CompletedTask;
        };
        setup.OnGetStarted = () => Task.CompletedTask;
        return (new FunnelModel(setup, services ?? new FunnelServices()), setup);
    }

    static GuidedSetupModel Ready(AppSettings? settings = null)
    {
        settings ??= new AppSettings();
        var services = new GuidedServices { Home = Path.GetTempPath(), Find = _ => AgentFound.None, OpenUrl = _ => { }, Post = a => a() };
        return new GuidedSetupModel(SetupModel.For(SkinKind.Mac), services, () => settings, change => change(settings));
    }

    /// <summary>The library's state while (or after) a sync of <paramref name="classes"/> classes, with each listing's count.</summary>
    static string Syncing(string state, int classes, int due, int files, int failed = 0, string lastSync = "2025-09-25T16:24:00Z", int dueFailed = 0)
    {
        var listings = new System.Text.Json.Nodes.JsonObject();
        foreach (string l in new[] { "assignments", "submissions" }) listings[l] = new System.Text.Json.Nodes.JsonObject { ["done"] = due, ["failed"] = l == "assignments" ? dueFailed : 0 };
        foreach (string l in new[] { "modules", "announcements", "files", "quizzes", "discussions", "planner" })
            listings[l] = new System.Text.Json.Nodes.JsonObject { ["done"] = files, ["failed"] = l == "files" ? failed : 0 };
        return new System.Text.Json.Nodes.JsonObject
        {
            ["state"] = state, ["url"] = "https://school.instructure.com",
            ["extension"] = new System.Text.Json.Nodes.JsonObject { ["connected"] = true, ["browser"] = "Chrome" },
            ["last_sync"] = lastSync,
            ["syncing"] = state == "syncing" ? new System.Text.Json.Nodes.JsonObject { ["left"] = classes - due, ["total"] = classes } : null,
            ["progress"] = new System.Text.Json.Nodes.JsonObject { ["classes"] = classes, ["listings"] = listings },
            ["error"] = state == "error" ? new System.Text.Json.Nodes.JsonObject { ["text"] = "Couldn't read CS 101 files from Canvas (Canvas answered 500), so what you had is kept.", ["at"] = lastSync } : null,
        }.ToJsonString();
    }

    // --- the way through -----------------------------------------------------------------------------------------------

    [AvaloniaFact]
    public async Task Get_started_makes_the_library_then_asks_if_they_are_a_student()
    {
        var (f, setup) = Make();
        Assert.True(f.OnWelcome);
        Assert.Equal("Get started", f.WelcomeLabel);
        await f.GetStartedCommand.ExecuteAsync(null);
        Assert.True(setup.LibraryOk);
        Assert.True(f.OnStudent);
        Assert.Equal("Step 1 of 4", f.StepLabel);
        Assert.Equal(4, f.Indicator.Count);
        Assert.True(f.Indicator[0].Current);

        // Back to the welcome: its button just moves on now (the library is made).
        f.BackCommand.Execute(null);
        Assert.True(f.OnWelcome);
        Assert.Equal("Continue", f.WelcomeLabel);
        await f.GetStartedCommand.ExecuteAsync(null);
        Assert.True(f.OnStudent);
    }

    [AvaloniaFact]
    public async Task Not_a_student_goes_straight_to_the_AI_and_Home_offers_classes_by_hand()
    {
        FunnelEnd? end = null;
        var (f, _) = Make(new FunnelServices { MakeReady = () => Ready() });
        f.OnFinish = e => end = e;
        await f.GetStartedCommand.ExecuteAsync(null);
        f.NotAStudentCommand.Execute(null);
        Assert.True(f.OnAi);
        Assert.Equal("Step 2 of 2", f.StepLabel);
        Assert.Equal([FunnelStep.Student, FunnelStep.Ai], f.Indicator.Select(d => d.Step));
        f.BackCommand.Execute(null);
        Assert.True(f.OnStudent);

        // A student whose school has no Canvas: the same, as a student.
        f.NoCanvasCommand.Execute(null);
        Assert.True(f.OnAi);
        f.SkipCommand.Execute(null);
        f.LaterCommand.Execute(null);
        Assert.Equal(new FunnelEnd(Student: true, Canvas: false, Ai: ""), end);
    }

    [AvaloniaFact]
    public async Task A_student_s_classes_with_nothing_typed_the_add_on_finds_canvas_then_the_sync_follows()
    {
        var log = new List<(string What, string Arg)>();
        var handler = new FakeLibrary()
            .Json(HttpMethod.Post, "/api/v2/canvas/pair", """{"until":"2025-09-25T17:44:00Z"}""")
            .Json(HttpMethod.Get, "/api/v2/canvas/state", """{"state": "not_set_up", "url": "", "extension": {"connected": false}}""");
        var context = CanvasFixtures.Context(handler, log: log);
        var watch = new CanvasWatch(context);
        var uses = new List<string>();
        AddOnModel? addOn = null;
        var (f, _) = Make(new FunnelServices
        {
            MakeAddOn = () => addOn = new AddOnModel(context, watch),
            SetUses = u =>
            {
                uses.AddRange(u);
                return Task.CompletedTask;
            },
        });
        await f.GetStartedCommand.ExecuteAsync(null);
        await f.IAmAStudentCommand.ExecuteAsync(null);
        Assert.True(f.OnClasses);
        Assert.True(f.FindsItself);
        Assert.False(f.AsksSchool);
        Assert.Equal(["student"], uses);
        Assert.Equal("Add Syft Notes to Chrome", addOn!.AddLabel);

        // Add: the store's page, and the library's door open (on entering the step, and again on Add). No address asked.
        await addOn.AddCommand.ExecuteAsync(null);
        Assert.Contains(("OpenAddOn", "Chrome"), log);
        Assert.Equal(2, handler.Requests.Count(r => r.Path == "/api/v2/canvas/pair"));
        Assert.True(addOn.Waiting);
        Assert.Equal("Waiting for Syft Notes to be added to Chrome", addOn.AddedLine);

        // The add-on asks to connect: the step says so with Allow, and nothing is handed over until it's pressed.
        handler.Json(HttpMethod.Get, "/api/v2/canvas/state", """{"state": "not_set_up", "url": "", "extension": {"connected": false, "asking": "chrome"}}""")
            .Json(HttpMethod.Post, "/api/v2/canvas/pair/answer", """{"ok":true}""");
        await watch.RefreshAsync();
        Assert.True(addOn.IsAsking);
        Assert.Equal("Chrome is asking to connect to Syft Notes.", addOn.AskingLine);
        Assert.False(addOn.ShowStoreSteps);
        await addOn.AllowCommand.ExecuteAsync(null);
        Assert.Contains(handler.Requests, r => r.Path == "/api/v2/canvas/pair/answer" && r.Body!.Contains("true"));
        Assert.False(addOn.IsAsking);

        // The add-on checks in, then finds the student's Canvas from their sign-in: the sync screen follows by itself.
        handler.Json(HttpMethod.Get, "/api/v2/canvas/state", """{"state": "not_set_up", "url": "", "extension": {"connected": true, "browser": "Chrome"}}""");
        await watch.RefreshAsync();
        Assert.True(addOn.LookingForCanvas);
        Assert.True(f.OnClasses);
        handler.Json(HttpMethod.Get, "/api/v2/canvas/state", """{"state": "connected", "url": "https://school.instructure.com", "extension": {"connected": true}}""");
        await watch.RefreshAsync();
        Assert.Equal("Found your Canvas: school.instructure.com", addOn.CanvasLine);
        Assert.True(f.OnSync);
        Assert.DoesNotContain(handler.Requests, r => r.Path == "/api/v2/canvas" && r.Method == "POST"); // no school typed, ever
        f.Dispose();
    }

    /// <summary>What the stores serve, as <see cref="StoreVersions"/> asks it: Google's update check and Mozilla's API.</summary>
    sealed class FakeStores(string chrome, string firefox) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            string body = request.RequestUri!.Host.Contains("google", StringComparison.Ordinal)
                ? "<gupdate><app status=\"ok\"><updatecheck status=\"ok\" version=\"" + chrome + "\"/></app></gupdate>"
                : "{\"current_version\":{\"version\":\"" + firefox + "\"}}";
            return Task.FromResult(new HttpResponseMessage { Content = new StringContent(body) });
        }
    }

    /// <summary>The funnel's classes step for a student whose browser is <paramref name="browser"/>, with the stores
    /// serving these versions: true when it finds Canvas with nothing typed, false when it asks for the school.</summary>
    static async Task<bool> NothingTypedAsync(Browser browser, string chrome, string firefox)
    {
        var context = CanvasFixtures.Context(new FakeLibrary(), browsers: [browser]);
        var stores = new StoreVersions(new HttpClient(new FakeStores(chrome, firefox)), Path.Combine(Directory.CreateTempSubdirectory("ss-stores-").FullName, "s.json"));
        var (f, _) = Make(new FunnelServices
        {
            AddOnFindsCanvas = () => AddOnModel.FindsCanvasAsync(context, stores),
            MakeAddOn = () => new AddOnModel(context, new CanvasWatch(context)),
            MakeCanvas = () => Task.FromResult<CanvasConnectModel?>(new CanvasConnectModel(context, new CanvasWatch(context), forSetup: true)),
        });
        await f.GetStartedCommand.ExecuteAsync(null);
        await f.IAmAStudentCommand.ExecuteAsync(null);
        bool finds = f.FindsItself;
        Assert.NotEqual(finds, f.AsksSchool);
        f.Dispose();
        return finds;
    }

    [AvaloniaFact]
    public async Task A_firefox_student_finds_canvas_with_nothing_typed_when_the_add_ons_site_serves_1_8_and_a_chrome_student_types_it_while_the_web_store_serves_1_6()
    {
        Assert.True(await NothingTypedAsync(Browsers.Firefox, chrome: "1.6", firefox: "1.8"));
        Assert.False(await NothingTypedAsync(Browsers.Chrome, chrome: "1.6", firefox: "1.8"));
        Assert.True(await NothingTypedAsync(Browsers.Chrome, chrome: "1.8", firefox: "1.8")); // the day Google approves it, no app release
        Assert.False(await NothingTypedAsync(Browsers.Firefox, chrome: "1.8", firefox: "1.7"));
    }

    [AvaloniaFact]
    public async Task An_older_add_on_heard_from_sends_the_student_to_the_address_step_at_once()
    {
        var handler = new FakeLibrary()
            .Json(HttpMethod.Post, "/api/v2/canvas/pair", """{"until":"2025-09-25T17:44:00Z"}""")
            .Json(HttpMethod.Get, "/api/v2/canvas/state", """{"state": "not_set_up", "url": "", "extension": {"connected": false}}""");
        var context = CanvasFixtures.Context(handler);
        var watch = new CanvasWatch(context);
        var (f, _) = Make(new FunnelServices
        {
            MakeAddOn = () => new AddOnModel(context, watch),
            MakeCanvas = () => Task.FromResult<CanvasConnectModel?>(new CanvasConnectModel(context, watch, forSetup: true)),
        });
        await f.GetStartedCommand.ExecuteAsync(null);
        await f.IAmAStudentCommand.ExecuteAsync(null);
        Assert.True(f.FindsItself);
        var addOn = f.AddOn!;
        await addOn.AddCommand.ExecuteAsync(null);

        // The store gave 1.6 after all: it checks in saying so, and the step becomes the one that asks for the school.
        handler.Json(HttpMethod.Get, "/api/v2/canvas/state", """{"state": "not_set_up", "url": "", "extension": {"connected": true, "version": "1.6", "browser": "Chrome", "last_seen": "2999-01-01T00:00:00Z"}}""");
        await watch.RefreshAsync();
        Assert.False(f.FindsItself);
        f.Dispose();
    }

    [AvaloniaFact]
    public async Task An_add_on_from_before_with_an_old_key_gets_this_library_s_code_to_paste_once_it_knocks_after_Add()
    {
        using var home = new TempHome();
        var now = DateTimeOffset.Parse("2026-11-02T09:00:00Z");
        var log = new List<(string What, string Arg)>();
        var handler = new FakeLibrary()
            .Json(HttpMethod.Post, "/api/v2/canvas/pair", """{"until":"2026-11-02T09:05:00Z"}""")
            .Json(HttpMethod.Get, "/api/v2/canvas/extension", "extension")
            .Json(HttpMethod.Get, "/api/v2/canvas/state", """{"state": "no_extension", "url": "https://school.instructure.com", "extension": {"connected": false, "refused_at": "2026-11-02T08:59:40Z"}}""");
        var context = CanvasFixtures.Context(handler, log: log, browsers: [Browsers.Firefox]);
        var watch = new CanvasWatch(context) { Extension = new ExtensionKeeper(home.Path, () => context.Client) };
        using var addOn = new AddOnModel(context, watch, () => now);
        await addOn.AddCommand.ExecuteAsync(null);
        await watch.RefreshAsync();
        Assert.True(addOn.ShowStoreSteps); // a knock from before Add says nothing about the add-on being added now
        Assert.False(addOn.ShowPaste);

        // Firefox's add-on from an earlier install knocks again with its old key: this library's code, to paste in its button.
        now += TimeSpan.FromSeconds(30);
        handler.Json(HttpMethod.Get, "/api/v2/canvas/state", """{"state": "no_extension", "url": "https://school.instructure.com", "extension": {"connected": false, "refused_at": "2026-11-02T09:00:20Z"}}""");
        await watch.RefreshAsync();
        Assert.True(addOn.ShowPaste);
        Assert.False(addOn.ShowStoreSteps);
        Assert.Equal("Connect Firefox again", addOn.AddedLine);
        Assert.Equal("Firefox already has Syft Notes from before. Click the Syft Notes button in Firefox's toolbar, choose Paste a new code, and paste this:", addOn.PasteLine);
        Assert.Equal(new ExtensionConnection("https://library.test", "test-key-abc123", "https://school.instructure.com"), Extension.ReadConnectionCode(addOn.ConnectionCode));
        addOn.CopyCodeCommand.Execute(null);
        Assert.Contains(("Copy", addOn.ConnectionCode), log);

        // Pasted: it checks in with this library's key, and the step is done as always.
        handler.Json(HttpMethod.Get, "/api/v2/canvas/state", """{"state": "connected", "url": "https://school.instructure.com", "extension": {"connected": true, "browser": "Firefox"}}""");
        await watch.RefreshAsync();
        Assert.True(addOn.IsDone);
        Assert.False(addOn.ShowPaste);

        // A library with no Canvas address yet (a fresh install) has no code to give: the step that asks for the school's,
        // then its code for the add-on's own button, with nothing to add.
        var fresh = new FakeLibrary()
            .Json(HttpMethod.Post, "/api/v2/canvas/pair", """{"until":"2026-11-02T09:05:00Z"}""")
            .Json(HttpMethod.Post, "/api/v2/canvas", "canvas")
            .Json(HttpMethod.Get, "/api/v2/canvas/classes", "[]")
            .Json(HttpMethod.Get, "/api/v2/canvas/extension", "extension")
            .Json(HttpMethod.Get, "/api/v2/canvas/state", """{"state": "not_set_up", "url": "", "extension": {"connected": false, "refused_at": "2026-11-02T09:00:20Z"}}""");
        var freshLog = new List<(string What, string Arg)>();
        var freshContext = CanvasFixtures.Context(fresh, log: freshLog, browsers: [Browsers.Firefox]);
        var freshWatch = new CanvasWatch(freshContext);
        var (f, _) = Make(new FunnelServices
        {
            MakeAddOn = () => new AddOnModel(freshContext, freshWatch, () => DateTimeOffset.Parse("2026-11-02T09:00:00Z")),
            MakeCanvas = async () =>
            {
                var c = new CanvasConnectModel(freshContext, freshWatch, forSetup: true);
                await c.StartAsync(CanvasFixtures.Load<CanvasApi.State>("state-not-set-up"), []);
                return c;
            },
        });
        await f.GetStartedCommand.ExecuteAsync(null);
        await f.IAmAStudentCommand.ExecuteAsync(null);
        await f.AddOn!.AddCommand.ExecuteAsync(null);
        await freshWatch.RefreshAsync();
        await Wait.UntilAsync(() => f.AsksSchool, "the step that asks for the school's address");
        f.Canvas!.SchoolField = "school.instructure.com";
        fresh.Json(HttpMethod.Get, "/api/v2/canvas/state", """{"state": "no_extension", "url": "https://school.instructure.com", "extension": {"connected": false, "refused_at": "2026-11-02T09:00:50Z"}}""");
        await f.Canvas.ContinueCommand.ExecuteAsync(null);
        await Wait.UntilAsync(() => f.Canvas.WaitingForBrowser, "the add-on from before's code");
        Assert.Equal("In Firefox, click the Syft Notes button (it may be under the puzzle-piece Extensions button).", f.Canvas.StoreStep1);
        Assert.Equal("Choose Paste a new code, paste this code and click Connect.", f.Canvas.StoreStep2);
        Assert.True(f.Canvas.ShowCode);
        Assert.False(f.Canvas.ShowAddOnLink); // nothing to add
        Assert.Single(freshLog, l => l.What == "OpenAddOn"); // the store's page only from the first Add: nothing opened since
        f.Dispose();
    }

    [AvaloniaFact]
    public async Task A_version_the_library_heard_yesterday_changes_nothing_until_a_fresh_check_in_says_it_is_older()
    {
        var now = DateTimeOffset.Parse("2026-11-02T09:00:00Z");
        var handler = new FakeLibrary()
            .Json(HttpMethod.Post, "/api/v2/canvas/pair", """{"until":"2026-11-02T09:05:00Z"}""")
            .Json(HttpMethod.Get, "/api/v2/canvas/state", """{"state": "not_set_up", "url": "", "extension": {"connected": false, "version": "1.6", "last_seen": "2026-11-01T09:00:00Z"}}""");
        var context = CanvasFixtures.Context(handler, browsers: [Browsers.Firefox]);
        var watch = new CanvasWatch(context);
        var stores = new StoreVersions(new HttpClient(new FakeStores("1.6", "1.8")), Path.Combine(Directory.CreateTempSubdirectory("ss-stores-").FullName, "s.json"));
        var (f, _) = Make(new FunnelServices
        {
            AddOnFindsCanvas = () => AddOnModel.FindsCanvasAsync(context, stores),
            MakeAddOn = () => new AddOnModel(context, watch, () => now),
            MakeCanvas = () => Task.FromResult<CanvasConnectModel?>(new CanvasConnectModel(context, watch, forSetup: true)),
        });
        await f.GetStartedCommand.ExecuteAsync(null);
        await f.IAmAStudentCommand.ExecuteAsync(null);
        Assert.True(f.FindsItself);
        await f.AddOn!.StartAsync();
        await f.AddOn.AddCommand.ExecuteAsync(null);
        await watch.RefreshAsync();
        Assert.True(f.FindsItself); // yesterday's 1.6 is ignored

        now += TimeSpan.FromSeconds(30);
        handler.Json(HttpMethod.Get, "/api/v2/canvas/state", """{"state": "not_set_up", "url": "", "extension": {"connected": true, "version": "1.6", "last_seen": "2026-11-02T09:00:30Z"}}""");
        await watch.RefreshAsync();
        Assert.True(f.AsksSchool); // a check-in since Add says 1.6
        f.Dispose();
    }

    [AvaloniaFact]
    public async Task With_nothing_heard_a_minute_and_a_half_after_the_store_opened_the_typing_way_is_offered_in_the_same_step()
    {
        var handler = new FakeLibrary()
            .Json(HttpMethod.Post, "/api/v2/canvas/pair", """{"until":"2025-09-25T17:44:00Z"}""")
            .Json(HttpMethod.Get, "/api/v2/canvas/state", """{"state": "not_set_up", "url": "", "extension": {"connected": false}}""");
        var context = CanvasFixtures.Context(handler);
        var watch = new CanvasWatch(context);
        var now = DateTimeOffset.Parse("2026-11-02T09:00:00Z");
        using var addOn = new AddOnModel(context, watch, () => now);
        bool typing = false;
        addOn.OnTypeInstead = () => typing = true;
        await addOn.AddCommand.ExecuteAsync(null);
        await watch.RefreshAsync();
        Assert.False(addOn.OfferTyping);
        now += AddOnModel.SlowAfter;
        await watch.RefreshAsync();
        Assert.True(addOn.OfferTyping);
        Assert.False(typing); // offered, not forced
        addOn.TypeInsteadCommand.Execute(null);
        Assert.True(typing);
    }

    [AvaloniaFact]
    public async Task That_wasn_t_me_tells_the_library_no_and_puts_Add_back()
    {
        var handler = new FakeLibrary()
            .Json(HttpMethod.Post, "/api/v2/canvas/pair", """{"until":"2025-09-25T17:44:00Z"}""")
            .Json(HttpMethod.Post, "/api/v2/canvas/pair/answer", """{"ok":true}""")
            .Json(HttpMethod.Get, "/api/v2/canvas/state", """{"state": "not_set_up", "url": "", "extension": {"connected": false, "asking": "firefox"}}""");
        var context = CanvasFixtures.Context(handler);
        var watch = new CanvasWatch(context);
        using var addOn = new AddOnModel(context, watch);
        await addOn.AddCommand.ExecuteAsync(null);
        await watch.RefreshAsync();
        Assert.True(addOn.IsAsking);
        Assert.Equal("Firefox is asking to connect to Syft Notes.", addOn.AskingLine); // not the Chrome they chose
        await addOn.NotMeCommand.ExecuteAsync(null);
        Assert.Contains(handler.Requests, r => r.Path == "/api/v2/canvas/pair/answer" && r.Body!.Contains("false"));
        Assert.True(addOn.ShowAdd);
        Assert.False(addOn.IsAsking);
    }

    [AvaloniaFact]
    public async Task While_the_stores_have_the_old_add_on_the_school_is_asked_once_then_its_code()
    {
        var handler = new FakeLibrary().Json(HttpMethod.Get, "/api/v2/canvas/extension", "extension");
        var context = CanvasFixtures.Context(handler);
        var (f, _) = Make(new FunnelServices
        {
            MakeCanvas = async () =>
            {
                var c = new CanvasConnectModel(context, new CanvasWatch(context), forSetup: true);
                await c.StartAsync(CanvasFixtures.Load<CanvasApi.State>("state-not-set-up"), []);
                return c;
            },
        });
        await f.GetStartedCommand.ExecuteAsync(null);
        await f.IAmAStudentCommand.ExecuteAsync(null);
        Assert.True(f.AsksSchool);
        Assert.Equal(1, f.Canvas!.Current);
        f.Dispose();
    }

    // --- the first sync --------------------------------------------------------------------------------------------------

    /// <summary>A sync screen over Find my courses' answer (canvas.json: four courses, three of them ticked).</summary>
    sealed record SyncRig(FirstSyncModel Sync, FakeLibrary Handler, CanvasWatch Watch)
    {
        /// <summary>The library says this about Canvas now, and the watch hears it (as it does every three seconds).</summary>
        public async Task Says(string state)
        {
            Handler.Json(HttpMethod.Get, "/api/v2/canvas/state", state);
            await Watch.RefreshAsync();
        }
    }

    static async Task<SyncRig> SyncAsync(Func<DateTimeOffset>? now = null)
    {
        var handler = new FakeLibrary()
            .Json(HttpMethod.Post, "/api/v2/canvas/courses", "canvas")
            .Json(HttpMethod.Post, "/api/v2/canvas/choose", "canvas")
            .Json(HttpMethod.Post, "/api/v2/canvas", "canvas")
            .Json(HttpMethod.Get, "/api/v2/canvas/state", Syncing("connected", 3, 0, 0, lastSync: "2025-09-24T10:00:00Z"));
        var context = CanvasFixtures.Context(handler);
        var watch = new CanvasWatch(context);
        await watch.RefreshAsync();
        var connect = new CanvasConnectModel(context, watch, forSetup: true);
        var sync = new FirstSyncModel(connect, context, watch) { Now = now ?? (() => CanvasFixtures.Now) };
        await connect.StartAsync(CanvasFixtures.Load<CanvasApi.State>("state-connected"), []);
        await sync.StartAsync();
        return new SyncRig(sync, handler, watch);
    }

    [AvaloniaFact]
    public async Task The_sync_counts_each_stage_from_the_library_and_continue_opens_once_the_due_dates_are_in()
    {
        var rig = await SyncAsync();
        var (sync, handler) = (rig.Sync, rig.Handler);
        Assert.Equal(StageState.Done, sync.Find.State);
        // Canvas lists four courses and one isn't brought in: the first line counts the three that are, the same three
        // the sync counts ("1 of 3" below), never "4 found" and then "3 of 3".
        Assert.Equal("3 found", sync.Find.Detail);
        // The ticked courses became classes, and the sync was asked for.
        var chose = Assert.Single(handler.Requests, r => r.Path == "/api/v2/canvas/choose");
        Assert.Contains("4201", chose.Body);
        Assert.DoesNotContain("4204", chose.Body);
        Assert.Contains(handler.Requests, r => r.Path == "/api/v2/canvas" && r.Body == """{"sync":true}""");
        Assert.Equal(StageState.Working, sync.Due.State);
        Assert.Equal("Waiting for Chrome to start", sync.Due.Detail);
        Assert.False(sync.CanContinue);
        Assert.True(sync.CanSkip);

        await rig.Says(Syncing("syncing", 3, 1, 0));
        Assert.Equal("1 of 3", sync.Due.Detail);
        Assert.Equal(1 / 3.0, sync.Due.Fraction);
        Assert.True(sync.Due.ShowBar);
        Assert.Equal(StageState.ToDo, sync.Files.State);

        var heard = new List<string?>();
        sync.PropertyChanged += (_, e) => heard.Add(e.PropertyName);
        await rig.Says(Syncing("syncing", 3, 3, 2));
        Assert.Contains(nameof(FirstSyncModel.CanContinue), heard); // the button hears it, not only the end
        Assert.Equal(StageState.Done, sync.Due.State);
        Assert.Equal("3 of 3", sync.Due.Detail);
        Assert.Equal("2 of 3", sync.Files.Detail);
        Assert.True(sync.CanContinue); // the files carry on in the background

        await rig.Says(Syncing("connected", 3, 3, 3, lastSync: "2025-09-25T17:30:00Z"));
        Assert.True(sync.Finished);
        Assert.Equal(StageState.Done, sync.Files.State);
        Assert.Equal("Done. Your classes are in.", sync.DoneLine);
        sync.Dispose();
    }

    [AvaloniaFact]
    public async Task A_sync_that_finished_with_some_of_canvas_missing_says_so()
    {
        var rig = await SyncAsync();
        var sync = rig.Sync;
        await rig.Says(Syncing("syncing", 3, 3, 1));
        // The library calls a sync that ended with something unread an error; the screen calls it done, mostly.
        await rig.Says(Syncing("error", 3, 3, 3, failed: 1, lastSync: "2025-09-25T17:30:00Z"));
        Assert.True(sync.Finished);
        Assert.False(sync.HasProblem);
        Assert.Equal(StageState.Done, sync.Due.State);
        Assert.Equal(StageState.Partial, sync.Files.State);
        Assert.StartsWith("Done. Some of Canvas didn't come through", sync.DoneLine);
        sync.Dispose();
    }

    [AvaloniaFact]
    public async Task A_sync_canvas_sent_nothing_for_says_so_and_try_again_asks_again()
    {
        var rig = await SyncAsync();
        var (sync, handler) = (rig.Sync, rig.Handler);
        await rig.Says(Syncing("syncing", 3, 1, 0));
        await rig.Says(Syncing("error", 3, 3, 3, dueFailed: 3, lastSync: "2025-09-25T17:30:00Z"));
        Assert.Equal(StageState.Failed, sync.Due.State);
        Assert.Equal("Canvas didn't send your assignments and due dates. It may be busy: try again in a moment.", sync.Problem);
        Assert.False(sync.Finished);
        Assert.True(sync.CanSkip);

        int asked = handler.Requests.Count(r => r.Body == """{"sync":true}""");
        await sync.TryAgainCommand.ExecuteAsync(null);
        Assert.Equal(asked + 1, handler.Requests.Count(r => r.Body == """{"sync":true}"""));
        // Until the new sync starts the library still says the old one's error: the screen waits for the browser.
        Assert.Null(sync.Problem);
        Assert.Equal(StageState.Working, sync.Due.State);
        Assert.Equal("Waiting for Chrome to start", sync.Due.Detail);
        await rig.Says(Syncing("syncing", 3, 2, 0));
        Assert.Equal("2 of 3", sync.Due.Detail);
        await rig.Says(Syncing("connected", 3, 3, 3, lastSync: "2025-09-25T17:40:00Z"));
        Assert.True(sync.Finished);
        Assert.Equal("Done. Your classes are in.", sync.DoneLine);
        sync.Dispose();
    }

    [AvaloniaFact]
    public async Task Signed_out_of_canvas_it_waits_for_the_sign_in_and_says_what_it_waits_for_when_slow()
    {
        var now = CanvasFixtures.Now;
        var rig = await SyncAsync(() => now);
        var sync = rig.Sync;
        await rig.Says("""{"state": "signed_out", "url": "https://school.instructure.com", "extension": {"connected": true, "browser": "Chrome"}}""");
        Assert.True(sync.SignInNeeded);
        Assert.Equal(StageState.Waiting, sync.Due.State);
        Assert.Equal("Sign in to Canvas in Chrome", sync.Due.Detail);
        Assert.False(sync.HasProblem); // a wait, not a failure
        Assert.False(sync.HasSlowLine);

        // Nothing moves for a while: the screen says what it's waiting for.
        now += FirstSyncModel.Patience + TimeSpan.FromSeconds(1);
        await rig.Says("""{"state": "signed_out", "url": "https://school.instructure.com", "extension": {"connected": true, "browser": "Chrome"}}""");
        Assert.Equal("Syft Notes carries on by itself once you're signed in to Canvas in Chrome.", sync.SlowLine);

        await rig.Says(Syncing("syncing", 3, 1, 0));
        Assert.False(sync.SignInNeeded);
        Assert.False(sync.HasSlowLine);
        Assert.Equal("1 of 3", sync.Due.Detail);
        sync.Dispose();
    }

    // --- connect your AI -------------------------------------------------------------------------------------------------

    static async Task<(FunnelModel Funnel, List<string> Used, Func<FunnelEnd?> End)> AtAiAsync(string alreadyHere = "", bool libraryTakes = true)
    {
        var used = new List<string>();
        FunnelEnd? end = null;
        var (f, _) = Make(new FunnelServices
        {
            MakeReady = () => Ready(),
            UseAi = id =>
            {
                used.Add(id);
                return Task.FromResult(libraryTakes);
            },
            AlreadyHere = () => alreadyHere,
        });
        f.OnFinish = e => end = e;
        await f.GetStartedCommand.ExecuteAsync(null);
        f.NotAStudentCommand.Execute(null);
        return (f, used, () => end);
    }

    [AvaloniaTheory]
    [InlineData("UseClaude", "claude")]
    [InlineData("UseChatGpt", "codex")]
    [InlineData("UseGemini", "gemini")]
    [InlineData("ContinueWithGoogle", "gemini")]
    public async Task Each_AI_is_connected_through_one_method_then_setup_ends_on_home(string button, string id)
    {
        var (f, used, end) = await AtAiAsync();
        Assert.True(f.ShowAiChoices);
        var command = (CommunityToolkit.Mvvm.Input.IAsyncRelayCommand)typeof(FunnelModel).GetProperty(button + "Command")!.GetValue(f)!;
        await command.ExecuteAsync(null);
        // Guided setup's own screens, in this window: here, its helper to install first.
        Assert.True(f.ConnectingAi);
        Assert.False(f.ShowAiChoices);
        Assert.Equal(id, f.Ready!.Picked);
        Assert.Equal(GuidedScreen.Install, f.Ready.Screen);

        // Signed in: the library writes with it, and Home.
        f.Ready.ReadyForNotes!(id);
        Assert.Equal([id], used);
        Assert.Equal(new FunnelEnd(false, false, id), end());
    }

    [AvaloniaFact]
    public async Task Back_from_getting_an_AI_ready_is_the_choices_again_with_nothing_changed()
    {
        var (f, used, end) = await AtAiAsync();
        await f.ContinueWithGoogleCommand.ExecuteAsync(null);
        f.BackCommand.Execute(null);
        Assert.False(f.ConnectingAi);
        Assert.True(f.ShowAiChoices);
        Assert.Empty(used);
        Assert.Null(end());
    }

    [AvaloniaFact]
    public async Task Going_on_without_an_AI_pushes_to_gemini_once_more_and_later_leaves_it_for_home()
    {
        var (f, used, end) = await AtAiAsync();
        f.SkipCommand.Execute(null);
        Assert.True(f.AskedAgain);
        Assert.False(f.ShowAiChoices);
        Assert.Null(end());
        // Back from the push: the choices.
        f.BackCommand.Execute(null);
        Assert.True(f.ShowAiChoices);
        // From the push, Continue with Google is still one press.
        f.SkipCommand.Execute(null);
        await f.ContinueWithGoogleCommand.ExecuteAsync(null);
        Assert.Equal("gemini", f.Ready!.Picked);
        f.BackCommand.Execute(null);
        f.SkipCommand.Execute(null);
        f.LaterCommand.Execute(null);
        Assert.Equal(new FunnelEnd(false, false, ""), end());
        Assert.Empty(used);
    }

    [AvaloniaFact]
    public async Task An_AI_already_signed_in_here_is_offered_first_and_needs_nothing_more()
    {
        var (f, used, end) = await AtAiAsync(alreadyHere: "claude");
        Assert.True(f.HasAlreadyHere);
        Assert.Equal("Claude is signed in on this Mac, so it writes your notes.", f.AlreadyHereLine);
        f.UseAlreadyHereCommand.Execute(null);
        Assert.Equal("claude", end()!.Ai);
        Assert.Empty(used); // Get started already told the library
    }

    [AvaloniaFact]
    public async Task Google_turning_a_school_account_down_is_said_over_the_choices()
    {
        var (f, used, _) = await AtAiAsync();
        await f.ContinueWithGoogleCommand.ExecuteAsync(null);
        f.Ready!.PlanProblem = ChatProblem.Plan;
        Assert.False(f.ConnectingAi);
        Assert.True(f.ShowAiChoices);
        Assert.StartsWith("Google doesn't let school and work accounts use Gemini", f.AiProblem);
        Assert.Empty(used);
    }

    [AvaloniaFact]
    public async Task A_library_that_doesnt_take_the_AI_says_so_and_setup_stays()
    {
        var (f, used, end) = await AtAiAsync(libraryTakes: false);
        await f.UseClaudeCommand.ExecuteAsync(null);
        f.Ready!.ReadyForNotes!("claude");
        Assert.Equal(["claude"], used);
        Assert.Null(end());
        Assert.False(f.ConnectingAi);
        Assert.Equal("Your library didn't answer. Try again in a moment.", f.AiProblem);
    }
}
