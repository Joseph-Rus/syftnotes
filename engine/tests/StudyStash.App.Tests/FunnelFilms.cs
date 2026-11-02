using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using StudyStash.App.Platform;
using StudyStash.App.Services;
using StudyStash.App.ViewModels;
using StudyStash.App.Views;
using StudyStash.Core;
using StudyStash.Core.Ai;
using StudyStash.Core.Canvas;
using StudyStash.Core.Tests;

namespace StudyStash.App.Tests;

/// <summary>
/// The add-on in the student's browser, standing in (Syft Notes for Canvas 1.8, as extension/ has it): once "added",
/// it asks the library for its key the way the store copy does (canvas/hello, as the store copy's own page, which only
/// works while the app's Add has the door open), checks in, says which Canvas it found, then reads Canvas for the
/// library from a pretend Canvas (fixtures), a little at a time so the sync can be watched. It talks only to the film's
/// own library, at the address the film gives it: never the address the library says (a real one listens on 8787).
/// </summary>
sealed class AddOnStandIn(string library, FakeCanvas canvas) : IDisposable
{
    readonly HttpClient http = new() { Timeout = TimeSpan.FromSeconds(60) };
    readonly CancellationTokenSource stop = new();
    readonly Lock answering = new();
    string key = "";

    /// <summary>The wait between two visits that had work: the sync moves at a pace the film can show.</summary>
    public TimeSpan Pace { get; set; } = TimeSpan.FromMilliseconds(1600);

    /// <summary>Added in the browser: it finds the library (as the store copy) and checks in.</summary>
    public async Task AddedAsync()
    {
        using var ask = new HttpRequestMessage(HttpMethod.Get, library + "/api/v2/canvas/hello");
        ask.Headers.Add("Origin", "chrome-extension://" + Extension.ChromeStoreId);
        using var r = await http.SendAsync(ask, stop.Token);
        r.EnsureSuccessStatusCode();
        key = JsonNode.Parse(await r.Content.ReadAsStringAsync(stop.Token))!["key"]!.GetValue<string>();
        await VisitAsync();
    }

    /// <summary>The student is signed in to Canvas: the add-on says which Canvas.</summary>
    public async Task FoundAsync(string url)
    {
        using var post = new HttpRequestMessage(HttpMethod.Post, library + "/api/v2/canvas/found")
        {
            Content = new StringContent(new JsonObject { ["url"] = url }.ToJsonString(), Encoding.UTF8, "application/json"),
        };
        post.Headers.Add("X-Study-Stash-Key", key);
        using var r = await http.SendAsync(post, stop.Token);
        r.EnsureSuccessStatusCode();
    }

    /// <summary>Canvas changes (it's back up, say), never while the add-on is reading it.</summary>
    public void Change(Action<FakeCanvas> change)
    {
        lock (answering) change(canvas);
    }

    /// <summary>From now on it reads Canvas whenever the library has work, until the film ends.</summary>
    public void Run() => _ = Task.Run(async () =>
    {
        while (!stop.IsCancellationRequested)
        {
            try
            {
                int n = await VisitAsync();
                await Task.Delay(n > 0 ? Pace : TimeSpan.FromMilliseconds(150), stop.Token);
            }
            catch (Exception e) when (e is OperationCanceledException or HttpRequestException or ObjectDisposedException)
            {
                if (stop.IsCancellationRequested) return;
            }
        }
    });

    /// <summary>One visit: ask for work (as 1.8, from Chrome), answer it from the pretend Canvas, hand the answers back.</summary>
    async Task<int> VisitAsync()
    {
        string query = string.Create(CultureInfo.InvariantCulture, $"?v=1.8&p={Extension.Protocol}&a={Uri.EscapeDataString(library)}&wait=0&b=Chrome");
        using var ask = new HttpRequestMessage(HttpMethod.Get, library + "/api/v2/canvas/work" + query);
        ask.Headers.Add("X-Study-Stash-Key", key);
        using var r = await http.SendAsync(ask, stop.Token);
        r.EnsureSuccessStatusCode();
        var jobs = (JsonNode.Parse(await r.Content.ReadAsStringAsync(stop.Token))!["jobs"] as JsonArray ?? []).OfType<JsonObject>().ToList();
        if (jobs.Count == 0) return 0;
        var results = new JsonArray();
        lock (answering)
            foreach (var j in jobs)
            {
                var a = canvas.Answer(new CanvasJob(j["id"]!.ToString(), (string)j["url"]!, (string)j["kind"]!));
                results.Add(new JsonObject
                {
                    ["id"] = a.Id, ["status"] = a.Status, ["link"] = a.Link, ["text"] = a.Text, ["b64"] = a.B64, ["error"] = a.Error,
                    ["final"] = a.Final, ["signed_out"] = a.SignedOut, ["rate"] = a.Rate, ["retry_after"] = a.RetryAfter, ["type"] = a.Type,
                });
            }
        using var post = new HttpRequestMessage(HttpMethod.Post, library + "/api/v2/canvas/results")
        {
            Content = new StringContent(new JsonObject { ["results"] = results }.ToJsonString(), Encoding.UTF8, "application/json"),
        };
        post.Headers.Add("X-Study-Stash-Key", key);
        using var answered = await http.SendAsync(post, stop.Token);
        answered.EnsureSuccessStatusCode();
        return jobs.Count;
    }

    public void Dispose()
    {
        stop.Cancel();
        http.Dispose();
    }
}

/// <summary>
/// Films of the first run's funnel, drawn off screen (nothing opens on the computer's screen) into
/// STUDYSTASH_SHOTS/&lt;film&gt;/ with frames.tsv for the GIF maker: the app's own funnel view and models, wired as
/// Shell.ShowWelcome wires them, over a real library run in this process in a throwaway home, with the add-on in the
/// browser and Canvas standing in (<see cref="AddOnStandIn"/>, fixtures) and stand-in Claude Code, Codex and agy for the
/// AI (<see cref="StandIns"/>). Only with STUDYSTASH_FILM=1, on a Mac; never a real Canvas, account, browser or terminal.
/// <para>To film: HOME set to an empty folder of its own, STUDYSTASH_FILM=1 STUDYSTASH_STILL=1 STUDYSTASH_SHOTS=&lt;folder&gt;,
/// then <c>dotnet test tests/StudyStash.App.Tests --no-build --filter FunnelFilms</c>, and
/// qa-bench/offscreen-film/make-gif.py on each film's folder.</para>
/// </summary>
public sealed class FunnelFilms
{
    const string Skip = "A film, not a test: STUDYSTASH_FILM=1, on a Mac.";
    const string School = "https://westbrook.instructure.com";
    const string Note = "Canvas, the browser and its Syft Notes add-on are stand-ins (fixtures): no browser opens, no real Canvas or account.";

    static string TermStart => DateTimeOffset.UtcNow.AddDays(-30).ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
    static string TermEnd => DateTimeOffset.UtcNow.AddDays(90).ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    /// <summary>A fixture as the student's Canvas would send it: at their school's address.</summary>
    static string At(string fixture) => FakeCanvas.Fixture(fixture).Replace(FakeCanvas.Base, School, StringComparison.Ordinal);

    /// <summary>The student's Canvas: three courses this term (COMP 101 in full, as the design draws it; BIO 110 and
    /// MATH 142 with their assignments), every assignment listing failing when <paramref name="down"/>.</summary>
    static FakeCanvas Canvas(bool down = false)
    {
        string courses = $$$"""
            [{"id": 4201, "name": "COMP 101 · Intro to Programming", "course_code": "COMP 101", "term": {"name": "Fall", "start_at": "{{{TermStart}}}", "end_at": "{{{TermEnd}}}"}},
             {"id": 4202, "name": "BIO 110 · Cell and Molecular Biology", "course_code": "BIO 110", "term": {"name": "Fall", "start_at": "{{{TermStart}}}", "end_at": "{{{TermEnd}}}"}},
             {"id": 4203, "name": "MATH 142 · Calculus II", "course_code": "MATH 142", "term": {"name": "Fall", "start_at": "{{{TermStart}}}", "end_at": "{{{TermEnd}}}"}}]
            """;
        var c = new FakeCanvas()
            .Json("/api/v1/courses", courses)
            .Json("/api/v1/courses/4201/assignments", At("cs101-assignments.json"))
            .Json("/api/v1/courses/4201/students/submissions", At("cs101-submissions.json"))
            .Json("/api/v1/courses/4201/modules", At("cs101-modules.json"))
            .Json("/api/v1/courses/4201/discussion_topics", At("cs101-announcements.json"))
            .Json(FakeCanvas.DiscussionsRoute("/api/v1/courses/4201/discussion_topics"), At("cs101-discussions.json"))
            .Json("/api/v1/courses/4201/quizzes", At("cs101-quizzes.json"))
            .Json("/api/v1/planner/items", At("cs101-planner.json"))
            .Json("/api/v1/courses/4201/files/555", At("cs101-file-555.json"))
            .Json("/api/v1/courses/4201/pages/lab-3-instructions", At("cs101-page-lab-3-instructions.json"))
            .Json("/api/v1/courses/4201/folders", "[]").Json("/api/v1/courses/4201/files", "[]")
            .Bytes("/files/555/download", Encoding.UTF8.GetBytes("%PDF-1.4 recursion slides"))
            .Json("/api/v1/courses/4202/assignments", At("bio110-assignments.json"))
            .Json("/api/v1/courses/4203/assignments", At("calc2-assignments.json"));
        foreach (long id in new[] { 4202L, 4203L })
            c.Json($"/api/v1/courses/{id}/students/submissions", "[]").Json($"/api/v1/courses/{id}/modules", "[]")
                .Json($"/api/v1/courses/{id}/discussion_topics", "[]").Json(FakeCanvas.DiscussionsRoute($"/api/v1/courses/{id}/discussion_topics"), "[]")
                .Json($"/api/v1/courses/{id}/quizzes", "[]").Json($"/api/v1/courses/{id}/folders", "[]").Json($"/api/v1/courses/{id}/files", "[]");
        if (down) Down(c, true);
        return c;
    }

    /// <summary>Canvas answering every assignment listing with a server error (or not, again).</summary>
    static void Down(FakeCanvas c, bool down)
    {
        foreach (var (id, fixture) in new[] { (4201L, "cs101-assignments.json"), (4202L, "bio110-assignments.json"), (4203L, "calc2-assignments.json") })
            if (down) c.Json($"/api/v1/courses/{id}/assignments", """{"errors":[{"message":"Internal server error"}]}""", status: 503);
            else c.Json($"/api/v1/courses/{id}/assignments", At(fixture));
    }

    static void Look(SkinKind skin)
    {
        Skin.UseTheme(ColourThemes.Default);
        ((App)Application.Current!).UseSkin(skin);
    }

    /// <summary>A stand-in's turns: the plan check's one word, then the notes (SignInFilms' own).</summary>
    static void Answers(StandIns pc, string id)
    {
        if (id == "gemini")
        {
            pc.Replay(id, [FakeAgents.Agy.Init(), FakeAgents.Agy.Words(1, "ready"), FakeAgents.Agy.Result("ready")], 1);
            return;
        }
        pc.Replay(id, [GuidedSetupTests.Init(), GuidedSetupTests.Says("ready"), GuidedSetupTests.Done("ready")], 1);
    }

    /// <summary>
    /// One first run, as Shell.ShowWelcome wires it: the setup Get started makes (here, the throwaway library in this
    /// process, the app pointed at it), the funnel over it with the add-on and Canvas standing in, guided setup's
    /// screens for the AI over the stand-in CLIs, and Home once it's done.
    /// </summary>
    sealed class Run : IAsyncDisposable
    {
        public required StandIns Pc { get; init; }
        public required AppHost Host { get; init; }
        public required Desk Desk { get; init; }
        public required FakeCanvas Canvas { get; init; }
        public SkinKind Skin { get; init; } = SkinKind.Mac;
        public IReadOnlyList<Browser> Browsers { get; init; } = [Services.Browsers.Chrome];
        /// <summary>The add-on finds Canvas this long after the student's Allow (they sign in meanwhile).</summary>
        public TimeSpan SignInTakes { get; init; } = TimeSpan.FromSeconds(3);

        public FilmLibrary? Lib;
        public AddOnStandIn? AddOn;
        public CanvasContext? Context;
        public CanvasWatch? Watch;
        public FunnelModel Funnel = null!;
        public SetupModel Setup = null!;
        public Control View = null!;
        public FunnelEnd? End;
        public readonly List<string> Opened = [];

        public void Start()
        {
            var setup = Setup = Services.Setup.Make(Host, AppRole.Both, here: new LibraryHere { Command = ["/usr/bin/false"], Folder = Pc["notes"], FirstPort = 9611 });
            setup.OneButton = true;
            setup.OnConnect = async () =>
            {
                setup.Connecting = true;
                Lib = await FilmLibrary.StartAsync(Pc);
                FilmParts.Connect(Host, Lib);
                var client = new CanvasClient(Lib.Url, FilmLibrary.Password);
                var actions = new CanvasActions(
                    OpenUrl: Opened.Add, OpenInBrowser: url => Opened.Add("Canvas " + url), OpenBrowser: () => Opened.Add("browser"),
                    Browsers: () => Browsers, RememberBrowser: _ => { },
                    OpenExtensions: b => { Opened.Add(b.Name + " extensions"); return null; },
                    OpenAddOn: b =>
                    {
                        Opened.Add(b.Name + " store");
                        _ = AddInBrowserAsync();
                        return null;
                    },
                    RevealFolder: _ => { }, OpenFile: _ => { }, PrepareExtension: (_, _) => "", Copy: _ => { });
                Context = new CanvasContext(client, new CanvasClock(() => DateTimeOffset.Now, TimeZoneInfo.Local), c => new SolidColorBrush(Colors.Gray), actions, Lib.Cfg.Home);
                Watch = new CanvasWatch(Context);
                setup.LibraryOk = true;
                setup.LibraryResult = "Sam's library is ready on this Mac.";
                setup.Connecting = false;
            };
            setup.OnGetStarted = () => FirstRun.AfterLibraryAsync(setup, Host, Lib!.Remote, () => FirstRun.Detect(c => AgentInstall.Find(c, Pc.Which), FirstRun.Status), Lib.WriteNotesAsync);
            Funnel = new FunnelModel(setup, new FunnelServices
            {
                MakeAddOn = () => new AddOnModel(Context!, Watch!),
                MakeCanvas = async () =>
                {
                    var c = new CanvasConnectModel(Context!, Watch!, forSetup: true);
                    await Watch!.RefreshAsync();
                    // As Shell does: started, not waited for, so the sync screen shows the courses being found.
                    _ = c.StartAsync(Watch.State ?? new CanvasApi.State(), await Context!.Client!.ClassesAsync() ?? []);
                    return c;
                },
                MakeSync = c => new FirstSyncModel(c, Context!, Watch!),
                MakeReady = () =>
                {
                    var services = new GuidedServices
                    {
                        Home = Host.Home, Windows = false, Find = c => AgentInstall.Find(c, Pc.Which),
                        Installer = c => Pc.Installer(c, Path.Combine(Host.Home, "logs", "setup-install.log")),
                        SignIn = (c, exe) => new AgentSignIn(c, exe), OpenUrl = Pc.Pages.Add, AiApps = () => [],
                    };
                    return new GuidedSetupModel(SetupModel.For(Skin, AppRole.Both), services, () => Host.Settings, Host.Save);
                },
                UseAi = async id =>
                {
                    await Lib!.Remote.DefaultsAsync(notes: id, ask: id);
                    await Lib.WriteNotesAsync(true);
                    Host.Save(s => s.SetupAi = id);
                    return true;
                },
                AlreadyHere = () => Host.Settings.SetupAi is "claude" or "codex" or "gemini" ? Host.Settings.SetupAi : "",
            })
            {
                OnFinish = e => End = e,
            };
            View = Skin == SkinKind.Mac ? new MacFunnel { DataContext = Funnel, DrawChrome = true } : new WinFunnel { DataContext = Funnel, DrawChrome = true };
            Desk.Show(View);
        }

        /// <summary>The student adds Syft Notes on the store's page and clicks Allow: the add-on finds the library and
        /// checks in, then, once they're signed in to Canvas, finds it and starts reading it.</summary>
        async Task AddInBrowserAsync()
        {
            await Task.Delay(TimeSpan.FromSeconds(2.5));
            AddOn = new AddOnStandIn(Lib!.Url, Canvas);
            await AddOn.AddedAsync();
            await Task.Delay(SignInTakes);
            await AddOn.FoundAsync(School);
            AddOn.Run();
        }

        /// <summary>Home, as the funnel leaves it: the classes Canvas brought in, "Ready when class starts", and what's
        /// still to do (an AI, classes by hand).</summary>
        public async Task<Control> HomeAsync()
        {
            var page = FilmParts.Library();
            page.Status = Skin == SkinKind.Mac ? "Library on this Mac" : "Library on this PC";
            var classes = Context?.Client is { } client ? await client.ClassesAsync() ?? [] : [];
            int i = 0;
            foreach (var c in classes) page.Classes.Add(new ClassItem { Name = c.Class, Dot = StudyStash.App.Skin.ClassDot(i++) });
            if (Context?.Client is { } cc && classes.Any(c => c.Linked) && await cc.DueAsync() is { } due)
                page.Classes.Insert(0, new ClassItem { Name = "Due", IsDue = true, Count = due.ToHandIn });
            page.Home.Selected = true;
            var home = OverviewModel.Home(new OverviewModel.Sources { Now = DateTimeOffset.Now, Zone = TimeZoneInfo.Local, NotesOff = End?.Ai.Length == 0 });
            home.OnRecord = () => { };
            page.Overview = home;
            var card = new WelcomeCardModel(Skin == SkinKind.Mac)
            {
                RecordKeys = Keybindings.Show(KeyAction.Record, Skin == SkinKind.Mac), NeedsAi = End?.Ai.Length == 0, AddClasses = End?.Canvas == false,
            };
            card.ShowModel(ready: false, fraction: 0.18, problem: null, device: Skin == SkinKind.Mac ? "Mac" : "PC");
            page.Welcome = card;
            return FilmParts.LibraryView(page, Skin);
        }

        public async ValueTask DisposeAsync()
        {
            AddOn?.Dispose();
            Funnel?.Dispose();
            if (Lib is not null) await Lib.DisposeAsync();
        }
    }

    /// <summary>The welcome: Get started, until "Are you a student?".</summary>
    static async Task WelcomeAsync(Run run)
    {
        run.Desk.Hold(2.2);
        run.Desk.Press(run.View.Part<Button>("GetStarted"));
        await run.Desk.UntilAsync(() => run.Funnel.OnStudent, "Are you a student?");
        run.Desk.Hold(1.6);
    }

    /// <summary>Connect your classes with nothing typed, then the first sync to its end.</summary>
    static async Task ClassesAndSyncAsync(Run run, bool captions = true)
    {
        run.Desk.Press(run.View.Part<Button>("IAmAStudent"));
        await run.Desk.UntilAsync(() => run.Funnel.AddOn is not null, "Connect your classes");
        if (captions) run.Desk.Caption("Connect your classes: nothing to type");
        run.Desk.Hold(2.0);
        run.Desk.Press(run.View.Part<Button>("AddToBrowser"));
        if (captions) run.Desk.Caption("Chrome opens Syft Notes' store page (a stand-in): Add to Chrome, then Allow");
        run.Desk.NoPointer();
        await run.Desk.UntilAsync(() => run.Funnel.AddOn!.Connected, "the add-on checks in", TimeSpan.FromSeconds(40));
        if (captions) run.Desk.Caption("Added. It finds your Canvas from your own sign-in (a stand-in)");
        await run.Desk.UntilAsync(() => run.Funnel.OnSync, "Canvas found, on to the sync", TimeSpan.FromSeconds(40));
        if (captions) run.Desk.Caption("The sync, a line per stage, from the library's own count");
        await run.Desk.UntilAsync(() => run.Funnel.Sync is { Finished: true } || run.Funnel.Sync?.HasProblem == true, "the sync", TimeSpan.FromSeconds(120),
            () => string.Join(" · ", run.Funnel.Sync?.Stages.Select(s => $"{s.Title} {s.State} {s.Detail}") ?? []));
        if (captions) run.Desk.Caption("");
        run.Desk.Pointer();
        run.Desk.Hold(2.2);
    }

    /// <summary>Getting the AI ready on guided setup's own screens, then Home.</summary>
    static async Task ConnectAsync(Run run, string button, string caption)
    {
        run.Desk.Press(run.View.Part<Button>(button));
        await run.Desk.UntilAsync(() => run.Funnel.Ready?.Screen == GuidedScreen.Install, "Get it ready");
        run.Desk.Hold(1.8);
        var ready = (Control)run.View.Part<Control>("GetReady");
        run.Desk.Press(ready.Part<Button>("Install"));
        await run.Desk.UntilAsync(() => run.Funnel.Ready!.Screen == GuidedScreen.SignIn && run.Funnel.Ready.ShowOpenSignIn, "installed, on to signing in", TimeSpan.FromSeconds(40));
        run.Desk.Hold(1.6);
        run.Desk.Press(ready.Part<Button>("OpenSignIn"));
        await run.Desk.UntilAsync(() => run.Funnel.Ready!.WaitingSignIn, "the sign-in waiting");
        run.Desk.Caption(caption);
        run.Desk.NoPointer();
        await run.Desk.UntilAsync(() => run.End is not null, "signed in, setup done", TimeSpan.FromSeconds(60));
        run.Desk.Caption("");
        run.Desk.Pointer();
    }

    static async Task HomeAsync(Run run, string caption)
    {
        run.Desk.Show(await run.HomeAsync());
        run.Desk.Caption(caption);
        run.Desk.Hold(3.6);
    }

    // --- 1 · the whole funnel on a Mac: Canvas, then Gemini ------------------------------------------------------------

    [AvaloniaFact]
    public async Task Funnel_with_canvas_and_gemini()
    {
        Assert.SkipUnless(FilmParts.On, Skip);
        Look(SkinKind.Mac);
        using var pc = new StandIns();
        Answers(pc, "gemini");
        using var host = FilmParts.Host(pc["app"]);
        using var desk = new Desk("01-funnel-canvas-gemini", pc, ThemeVariant.Light, "first run on a Mac · stand-in add-on, Canvas and Gemini (agy) · throwaway library · waits sped up", Note);
        await using var run = new Run { Pc = pc, Host = host, Desk = desk, Canvas = Canvas() };
        run.Start();
        await WelcomeAsync(run);
        await ClassesAndSyncAsync(run);
        run.Desk.Press(run.View.Part<Button>("SyncNext"));
        await run.Desk.UntilAsync(() => run.Funnel.OnAi, "Connect your AI");
        run.Desk.Caption("No plan? Continue with Google: Gemini, free · or Claude, ChatGPT, Gemini");
        run.Desk.Hold(3.2);
        run.Desk.Caption("");
        await ConnectAsync(run, "ContinueWithGoogle", "Google's own sign-in opens your browser · no terminal (a stand-in signs in after 5 s)");
        Assert.Equal(new FunnelEnd(true, true, "gemini"), run.End);
        await HomeAsync(run, "Home: your classes from Canvas, and Gemini writes the notes");
        Assert.Equal(0, pc.TerminalDoors + pc.RefusedTries);
        Assert.Contains("Chrome store", run.Opened);
    }

    // --- 2 · the whole funnel with "I pay for Claude" --------------------------------------------------------------------

    [AvaloniaFact]
    public async Task Funnel_with_claude()
    {
        Assert.SkipUnless(FilmParts.On, Skip);
        Look(SkinKind.Mac);
        using var pc = new StandIns();
        Answers(pc, "claude");
        using var host = FilmParts.Host(pc["app"]);
        using var desk = new Desk("02-funnel-claude", pc, ThemeVariant.Light, "first run on a Mac · stand-in add-on, Canvas and Claude Code · throwaway library · waits sped up", Note);
        await using var run = new Run { Pc = pc, Host = host, Desk = desk, Canvas = Canvas(), SignInTakes = TimeSpan.FromSeconds(1.5) };
        run.Start();
        await WelcomeAsync(run);
        await ClassesAndSyncAsync(run, captions: false);
        run.Desk.Press(run.View.Part<Button>("SyncNext"));
        await run.Desk.UntilAsync(() => run.Funnel.OnAi, "Connect your AI");
        run.Desk.Caption("I pay for Claude: Claude");
        run.Desk.Hold(2.0);
        run.Desk.Caption("");
        await ConnectAsync(run, "UseClaude", "Claude's own sign-in opens your browser · no terminal (a stand-in signs in after 5 s)");
        Assert.Equal(new FunnelEnd(true, true, "claude"), run.End);
        await HomeAsync(run, "Home: your classes from Canvas, and Claude writes the notes");
        Assert.Equal(1, pc.Logins("claude"));
        Assert.Equal(0, pc.TerminalDoors + pc.RefusedTries);
    }

    // --- 3 · not a student: no Canvas, no AI yet, and Home says what's next ----------------------------------------------

    [AvaloniaFact]
    public async Task Not_a_student()
    {
        Assert.SkipUnless(FilmParts.On, Skip);
        Look(SkinKind.Mac);
        using var pc = new StandIns();
        using var host = FilmParts.Host(pc["app"]);
        using var desk = new Desk("03-not-a-student", pc, ThemeVariant.Light, "first run on a Mac · not a student · throwaway library", Note);
        await using var run = new Run { Pc = pc, Host = host, Desk = desk, Canvas = Canvas() };
        run.Start();
        await WelcomeAsync(run);
        run.Desk.Press(run.View.Part<Button>("NotAStudent"));
        await run.Desk.UntilAsync(() => run.Funnel.OnAi, "Connect your AI");
        run.Desk.Hold(2.6);
        run.Desk.Caption("Skip: never straight out with no AI");
        run.Desk.Press(run.View.Part<Button>("SkipAi"));
        run.Desk.Hold(2.6);
        run.Desk.Caption("Later, quietly: Home keeps Continue with Google");
        run.Desk.Press(run.View.Part<Button>("Later"));
        await run.Desk.UntilAsync(() => run.End is not null, "Home");
        Assert.Equal(new FunnelEnd(false, false, ""), run.End);
        await HomeAsync(run, "Home: Notes need an AI (Continue with Google), and how to add classes");
        Assert.Empty(run.Opened);
    }

    // --- 4 · the sync stopped: said plainly, and Try again ---------------------------------------------------------------

    [AvaloniaFact]
    public async Task Sync_error_and_try_again()
    {
        Assert.SkipUnless(FilmParts.On, Skip);
        Look(SkinKind.Mac);
        using var pc = new StandIns();
        using var host = FilmParts.Host(pc["app"]);
        var canvas = Canvas(down: true);
        using var desk = new Desk("04-sync-error-try-again", pc, ThemeVariant.Light, "first run on a Mac · stand-in Canvas answering errors, then fine · waits sped up", Note);
        await using var run = new Run { Pc = pc, Host = host, Desk = desk, Canvas = canvas, SignInTakes = TimeSpan.FromSeconds(1.5) };
        run.Start();
        await WelcomeAsync(run);
        await ClassesAndSyncAsync(run, captions: false);
        Assert.True(run.Funnel.Sync!.HasProblem);
        run.Desk.Caption("Canvas sent no assignments (a stand-in answering 503): said plainly, with Try again");
        run.Desk.Hold(3.0);
        run.AddOn!.Change(c => Down(c, false));
        run.Desk.Caption("");
        run.Desk.Press(run.View.Part<Button>("TryAgain"));
        await run.Desk.UntilAsync(() => run.Funnel.Sync.Finished, "the sync again", TimeSpan.FromSeconds(120),
            () => string.Join(" · ", run.Funnel.Sync.Stages.Select(s => $"{s.Title} {s.State} {s.Detail}")));
        run.Desk.Caption("Tried again: done");
        run.Desk.Hold(2.6);
        run.Desk.Press(run.View.Part<Button>("SyncNext"));
        await run.Desk.UntilAsync(() => run.Funnel.OnAi, "Connect your AI");
        run.Desk.Hold(1.6);
    }

    // --- the contact sheet: a frame from each moment of the five films ---------------------------------------------------

    /// <summary>One picture of the films' moments, three across, each named (run after the films).</summary>
    [AvaloniaFact]
    public void Contact_sheet()
    {
        Assert.SkipUnless(FilmParts.On && File.Exists(Path.Combine(Shot.Dir, "01-funnel-canvas-gemini", "f-0020.png")), "The films first: STUDYSTASH_FILM=1.");
        (string Film, int Frame, string Label)[] tiles = Tiles;
        const double W = 600;
        var grid = new Avalonia.Controls.Primitives.UniformGrid { Columns = 3, Background = Brushes.White };
        foreach (var (film, frame, label) in tiles)
        {
            string path = Path.Combine(Shot.Dir, film, $"f-{frame:0000}.png");
            if (!File.Exists(path)) continue;
            var bitmap = new Avalonia.Media.Imaging.Bitmap(path);
            grid.Children.Add(new StackPanel
            {
                Margin = new Thickness(8), Spacing = 6,
                Children =
                {
                    new Image { Source = bitmap, Width = W, Height = W * bitmap.Size.Height / bitmap.Size.Width },
                    new TextBlock { Text = label, FontSize = 17, FontWeight = FontWeight.SemiBold, Foreground = Brushes.Black },
                },
            });
        }
        int rows = (grid.Children.Count + 2) / 3;
        double tile = W * 944 / 1340 + 6 + 24 + 16;
        var window = new Window { Width = 3 * (W + 16), Height = rows * tile, Content = grid, RequestedThemeVariant = ThemeVariant.Light };
        window.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        var shot = Avalonia.Headless.HeadlessWindowExtensions.CaptureRenderedFrame(window) ?? throw new InvalidOperationException("nothing rendered");
        using (var file = File.Create(Path.Combine(Shot.Dir, "contact-sheet.png"))) shot.Save(file, Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
        window.Close();
    }

    static readonly (string Film, int Frame, string Label)[] Tiles =
    [
        ("01-funnel-canvas-gemini", 15, "1 · Welcome: one button"),
        ("01-funnel-canvas-gemini", 55, "1 · Are you a student?"),
        ("01-funnel-canvas-gemini", 100, "1 · Connect your classes: nothing to type"),
        ("01-funnel-canvas-gemini", 128, "1 · The store's page is open: waiting for Chrome"),
        ("01-funnel-canvas-gemini", 140, "1 · Added; your Canvas found by itself next"),
        ("01-funnel-canvas-gemini", 175, "1 · The sync: a line per stage, Continue open"),
        ("01-funnel-canvas-gemini", 220, "1 · Done. Your classes are in."),
        ("01-funnel-canvas-gemini", 270, "1 · Connect your AI: Continue with Google ringed"),
        ("01-funnel-canvas-gemini", 405, "1 · Google's own sign-in, hidden: waiting"),
        ("01-funnel-canvas-gemini", 440, "1 · Home: the classes from Canvas"),
        ("02-funnel-claude", 290, "2 · I pay for Claude: get it ready, same window"),
        ("02-funnel-claude", 410, "2 · Home: Claude writes the notes"),
        ("03-not-a-student", 100, "3 · Not a student: straight to the AI"),
        ("03-not-a-student", 145, "3 · Skip: Notes need an AI, Gemini once more"),
        ("03-not-a-student", 210, "3 · Later: Home keeps Continue with Google"),
        ("04-sync-error-try-again", 230, "4 · Canvas sent nothing: said plainly, Try again"),
        ("04-sync-error-try-again", 330, "4 · Tried again: done"),
        ("05-windows-dark", 100, "5 · Windows, dark: Add Syft Notes to Edge"),
        ("05-windows-dark", 180, "5 · Windows, dark: the sync"),
        ("05-windows-dark", 250, "5 · Windows, dark: Connect your AI"),
        ("05-windows-dark", 420, "5 · Windows, dark: Home"),
    ];

    // --- 5 · Windows, dark ---------------------------------------------------------------------------------------------

    [AvaloniaFact]
    public async Task Windows_dark()
    {
        Assert.SkipUnless(FilmParts.On, Skip);
        Look(SkinKind.Win);
        try
        {
            using var pc = new StandIns();
            Answers(pc, "gemini");
            using var host = FilmParts.Host(pc["app"]);
            using var desk = new Desk("05-windows-dark", pc, ThemeVariant.Dark, "first run, Windows look, dark · stand-in add-on (Edge), Canvas and Gemini · waits sped up", Note);
            await using var run = new Run
            {
                Pc = pc, Host = host, Desk = desk, Canvas = Canvas(), Skin = SkinKind.Win, Browsers = [Services.Browsers.Edge, Services.Browsers.Chrome],
                SignInTakes = TimeSpan.FromSeconds(1.5),
            };
            run.Start();
            await WelcomeAsync(run);
            await ClassesAndSyncAsync(run, captions: false);
            run.Desk.Press(run.View.Part<Button>("SyncNext"));
            await run.Desk.UntilAsync(() => run.Funnel.OnAi, "Connect your AI");
            run.Desk.Hold(2.4);
            await ConnectAsync(run, "ContinueWithGoogle", "Google's own sign-in · no terminal (a stand-in signs in after 5 s)");
            await HomeAsync(run, "Home: your classes, and Gemini writes the notes");
            Assert.Contains("Edge store", run.Opened);
        }
        finally
        {
            Look(SkinKind.Mac);
        }
    }
}
