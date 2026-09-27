using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using StudyStash.App.Platform;
using StudyStash.App.Services;
using StudyStash.App.ViewModels;
using StudyStash.Audio;
using StudyStash.Core;

namespace StudyStash.App.Tests;

/// <summary>Setup: what this computer is for, and the microphone check on its first step.</summary>
public sealed class SetupTests
{
    static AppHost Host(TempHome home, Func<IAudioSource>? mic = null, ILoginItems? login = null) =>
        new(home.Path, mic, log: _ => { }, loginItems: login ?? new CountingLoginItems(), models: ModelSetting.None); // not CI's tiny model

    /// <summary>A minimal server on a free port that always answers /api/health with one status: enough to check
    /// what Setup says about it, with no real library behind it.</summary>
    static async Task<(string Url, WebApplication App)> FakeServerAsync(HttpStatusCode status)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        WebHostBuilderKestrelExtensions.ConfigureKestrel(builder.WebHost, k => k.Listen(IPAddress.Loopback, 0));
        var app = builder.Build();
        app.MapGet("/api/health", () => Results.StatusCode((int)status));
        await app.StartAsync();
        string url = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
        return (url, app);
    }

    static string[] Titles(SetupModel m) => [.. m.Steps.Select(s => s.Title)];

    [Fact]
    public void The_laptop_and_the_library_each_have_their_own_steps()
    {
        var laptop = SetupModel.For(SkinKind.Mac, AppRole.Laptop);
        Assert.Equal(["Welcome", "Your library", "Microphone", "Transcription model", "Canvas", "Classes", "Done"], Titles(laptop));
        Assert.Equal([1, 2, 3, 4, 5, 6, 7], laptop.Steps.Select(s => s.Number));

        var library = SetupModel.For(SkinKind.Mac, AppRole.Library);
        Assert.Equal(["Welcome", "Password", "AI engines", "Canvas", "Classes", "Start at login", "Connect your laptop"], Titles(library));

        // A library that also records: the library's flow with the microphone and model after the AI engines.
        library.SetRole(AppRole.Both);
        Assert.Equal(["Welcome", "Password", "AI engines", "Microphone", "Transcription model", "Canvas", "Classes", "Start at login", "Connect your laptop"],
            Titles(library));
    }

    [Fact]
    public void Windows_adds_the_taskbar_only_where_Study_Stash_records()
    {
        Assert.Equal(["Welcome", "Your library", "Microphone", "Transcription model", "Canvas", "Classes", "Taskbar", "Done"],
            Titles(SetupModel.For(SkinKind.Win, AppRole.Laptop)));
        Assert.DoesNotContain(SetupStep.Taskbar, SetupModel.StepsFor(AppRole.Library, SkinKind.Win));
        Assert.Contains(SetupStep.Taskbar, SetupModel.StepsFor(AppRole.Both, SkinKind.Win));
        Assert.DoesNotContain(SetupStep.Taskbar, SetupModel.StepsFor(AppRole.Laptop, SkinKind.Mac));
    }

    [Fact]
    public void The_two_setups_read_differently()
    {
        var laptop = SetupModel.For(SkinKind.Mac, AppRole.Laptop);
        var library = SetupModel.For(SkinKind.Mac, AppRole.Library);
        Assert.Equal("Set up Study Stash on this Mac", laptop.HeaderTitle);
        Assert.Equal("Set up your library", library.HeaderTitle);
        Assert.Equal("Laptop setup", laptop.FlowName);
        Assert.Equal("Library setup", library.FlowName);
        Assert.NotEqual(laptop.FlowIcon, library.FlowIcon);
        Assert.Equal("Record lectures on this Mac", laptop.WelcomeTitle);
        Assert.Equal("Your library lives here", library.WelcomeTitle);
        Assert.Equal("Laptop setup", laptop.StepLabel);
        Assert.Equal("Set up Study Stash on this PC", SetupModel.For(SkinKind.Win, AppRole.Laptop).HeaderTitle);
    }

    [Fact]
    public void Canvas_comes_before_classes_and_its_page_says_continue()
    {
        var mac = SetupModel.For(SkinKind.Mac);
        mac.Go(SetupStep.Canvas);
        Assert.True(mac.Steps.Single(s => s.Step == SetupStep.Canvas).Optional); // the sidebar still says so
        Assert.Equal("Step 5 of 7", mac.StepLabel);
        Assert.Equal("Continue", mac.ContinueLabel);
        Assert.True(mac.Wide);
        Assert.Equal(SetupStep.Classes, mac.Steps[mac.Index].Step);

        var library = SetupModel.For(SkinKind.Mac, AppRole.Library);
        library.Go(SetupStep.Canvas);
        Assert.Equal(SetupStep.Classes, library.Steps[library.Index].Step);
    }

    [Fact]
    public void The_library_calls_its_AI_step_library_setup_and_its_last_button_opens_Study_Stash()
    {
        var m = SetupModel.For(SkinKind.Mac, AppRole.Library);
        m.Go(SetupStep.Ai);
        Assert.Equal("Library setup · step 3 of 7", m.StepLabel);
        Assert.False(m.OnPlainStep);
        m.Go(SetupStep.Done);
        Assert.True(m.IsLast);
        Assert.Equal("Open Study Stash", m.ContinueLabel);
        Assert.True(m.OnLibraryDone);
        Assert.False(m.OnLaptopDone);
    }

    [Fact]
    public void An_installer_opens_its_own_flow_and_the_switch_keeps_what_was_typed()
    {
        var m = SetupModel.For(SkinKind.Mac, AppRole.Library);
        Assert.False(m.Asking);
        Assert.True(m.ShowSwitch);
        Assert.Equal("Setting up your laptop instead?", m.SwitchText);
        m.LibraryName = "Ada's library";
        m.Password = "correct-horse";
        m.Address = "http://mac-mini:8787";

        m.SwitchFlowCommand.Execute(null);

        Assert.Equal(AppRole.Laptop, m.Role);
        Assert.Equal(SetupStep.Welcome, m.Step);
        Assert.Equal(SetupStep.Library, m.Steps[1].Step);
        Assert.Equal("Setting up your library instead?", m.SwitchText);
        Assert.Equal(("Ada's library", "correct-horse", "http://mac-mini:8787"), (m.LibraryName, m.Password, m.Address));

        m.SwitchFlowCommand.Execute(null);
        Assert.Equal(AppRole.Library, m.Role);
        Assert.Equal("correct-horse", m.Password);
    }

    [Fact]
    public void With_no_installer_the_welcome_asks_with_two_cards()
    {
        var m = SetupModel.For(SkinKind.Mac);
        Assert.True(m.Asking);
        Assert.False(m.ShowSwitch);
        Assert.Equal("Welcome", m.StepLabel);
        Assert.True(m.IsLaptop);

        m.ChooseLibraryCommand.Execute(null);
        Assert.Equal(AppRole.Library, m.Role);
        Assert.Equal("Set up your library", m.HeaderTitle);

        // "I'll also record lectures on this Mac": the library that records, and back.
        m.AlsoRecord = true;
        Assert.Equal(AppRole.Both, m.Role);
        m.ChooseLibraryCommand.Execute(null); // already the library: stays one that records
        Assert.Equal(AppRole.Both, m.Role);
        m.AlsoRecord = false;
        Assert.Equal(AppRole.Library, m.Role);

        m.ChooseLaptopCommand.Execute(null);
        Assert.Equal(AppRole.Laptop, m.Role);
        m.AlsoRecord = true; // no checkbox on the laptop's welcome
        Assert.Equal(AppRole.Laptop, m.Role);
    }

    [Fact]
    public async Task Skip_moves_on_and_a_step_that_saves_first_can_hold_Continue()
    {
        var m = SetupModel.For(SkinKind.Mac);
        var entered = new List<SetupStep>();
        m.OnEnter = entered.Add;
        m.Go(SetupStep.Canvas);
        m.SkipCommand.Execute(null);
        Assert.Equal(SetupStep.Classes, m.Step);
        m.Go(SetupStep.Done);
        bool finished = false;
        m.OnFinish = () => finished = true;
        m.SkipCommand.Execute(null);
        Assert.True(finished);
        Assert.Equal([SetupStep.Canvas, SetupStep.Classes, SetupStep.Done], entered);

        m.SetRole(AppRole.Both);
        m.Go(SetupStep.Ai);
        m.LeaveAsync = step => Task.FromResult(step != SetupStep.Ai);
        await m.NextCommand.ExecuteAsync(null);
        Assert.Equal(SetupStep.Ai, m.Step);
        m.LeaveAsync = _ => Task.FromResult(true);
        await m.NextCommand.ExecuteAsync(null);
        Assert.Equal(SetupStep.Microphone, m.Step);
    }

    [Fact]
    public void Back_and_next_keep_the_step_checks_right()
    {
        var m = SetupModel.For(SkinKind.Mac); // Welcome
        m.NextCommand.Execute(null); // -> Your library
        Assert.True(m.Steps[0].Done);
        Assert.False(m.Steps[1].Done);

        m.NextCommand.Execute(null); // -> Microphone
        m.NextCommand.Execute(null); // -> Model
        m.NextCommand.Execute(null); // -> Canvas
        Assert.True(m.Steps[0].Done);
        Assert.True(m.Steps[1].Done);
        Assert.True(m.Steps[2].Done);
        Assert.True(m.Steps[3].Done);

        m.BackCommand.Execute(null); // -> Model: no longer done, even though it was
        Assert.True(m.Steps[2].Done);
        Assert.False(m.Steps[3].Done);
    }

    [Fact]
    public async Task Continue_connects_on_the_library_step_and_moves_on_only_when_it_worked()
    {
        var m = SetupModel.For(SkinKind.Mac);
        m.Go(SetupStep.Library);
        Assert.Equal("Connect", m.ContinueLabel);
        int tries = 0;
        m.OnConnect = () =>
        {
            tries++;
            m.LibraryOk = tries > 1;
            m.LibraryResult = m.LibraryOk ? "Connected to Ada's library." : "That password isn't right.";
            return Task.CompletedTask;
        };

        await m.NextCommand.ExecuteAsync(null);
        Assert.Equal(SetupStep.Library, m.Step);
        Assert.Equal("That password isn't right.", m.LibraryResult);

        await m.NextCommand.ExecuteAsync(null);
        Assert.Equal(SetupStep.Microphone, m.Step);

        // Back, and a new address: Continue connects again rather than carrying on with the old one.
        m.BackCommand.Execute(null);
        Assert.Equal("Continue", m.ContinueLabel);
        m.Address = "http://another:8787";
        Assert.False(m.LibraryOk);
        Assert.Equal("Connect", m.ContinueLabel);
        await m.NextCommand.ExecuteAsync(null);
        Assert.Equal(3, tries);
    }

    [Fact]
    public async Task The_library_flow_cant_leave_its_password_page_without_a_library()
    {
        using var home = new TempHome();
        using var host = Host(home);
        var m = Setup.Make(host, AppRole.Library);
        Assert.True(m.IsLibrary);
        m.NextCommand.Execute(null); // Welcome -> Password
        Assert.Equal(SetupStep.Password, m.Step);
        Assert.Equal("Create library", m.ContinueLabel);

        m.Password = "ab";
        await m.NextCommand.ExecuteAsync(null);

        Assert.Equal(SetupStep.Password, m.Step);
        Assert.False(m.LibraryOk);
        Assert.Equal("Use a password of at least 4 characters.", m.LibraryResult);
        Assert.False(File.Exists(Configs.Load(home.Path).ConfigPath)); // nothing was made
    }

    [Fact]
    public async Task Library_only_starts_no_model_download()
    {
        using var home = new TempHome();
        using var host = Host(home);
        var m = Setup.Make(host, AppRole.Library, tailscale: () => new TailscaleInfo(), hostName: () => "mac-mini.local");

        foreach (var step in m.Steps.Select(s => s.Step).ToList()) m.Go(step); // every page it has

        Assert.DoesNotContain(m.Steps, s => s.Step is SetupStep.Microphone or SetupStep.Model);
        Assert.False(host.ModelReady);
        Assert.Null(host.Downloading);
        await Task.Delay(50, TestContext.Current.CancellationToken); // nothing was ever scheduled to start
        Assert.Null(host.Downloading);
    }

    [Fact]
    public async Task The_librarys_last_page_shows_where_to_reach_it_and_the_password()
    {
        using var home = new TempHome();
        using var host = Host(home);
        var copied = new List<string>();
        var m = Setup.Make(host, AppRole.Library, tailscale: () => new TailscaleInfo(Installed: true, Running: true, Dns: "mac-mini.example.ts.net"),
            hostName: () => "mac-mini.local");
        m.OnCopy = copied.Add;
        m.Password = "correct-horse";

        m.Go(SetupStep.Done);
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (m.Addresses.Count < 2 && DateTime.UtcNow < deadline) await Task.Delay(10, TestContext.Current.CancellationToken);

        Assert.Equal([new SetupAddress("At home", "http://mac-mini.local:8787"), new SetupAddress("With Tailscale", "http://mac-mini.example.ts.net:8787")],
            m.Addresses);
        Assert.DoesNotContain("correct-horse", m.PasswordText); // hidden until Show
        m.ToggleShowPasswordCommand.Execute(null);
        Assert.Equal("correct-horse", m.PasswordText);
        Assert.Equal("Hide", m.ShowPasswordText);
        m.CopyPasswordCommand.Execute(null);
        m.CopyCommand.Execute(m.Addresses[0].Url);
        Assert.Equal(["correct-horse", "http://mac-mini.local:8787"], copied);
    }

    [Fact]
    public async Task Without_Tailscale_the_last_page_gives_the_home_address_only()
    {
        using var home = new TempHome();
        using var host = Host(home);
        var m = Setup.Make(host, AppRole.Library, tailscale: () => new TailscaleInfo(), hostName: () => "mac-mini.local");
        m.Go(SetupStep.Done);
        await Task.Delay(100, TestContext.Current.CancellationToken);
        Assert.Equal([new SetupAddress("At home", "http://mac-mini.local:8787")], m.Addresses);
    }

    [Fact]
    public void Finish_saves_role_and_setup_done_and_only_toggles_login_when_ticked()
    {
        using var home = new TempHome();
        var login = new CountingLoginItems();
        using var host = Host(home, login: login);
        var m = Setup.Make(host, AppRole.Library);
        m.AlsoRecord = true;

        m.StartAtLogin = false;
        Setup.Finish(m, host);
        Assert.True(AppSettings.Load(home.Path).SetupDone);
        Assert.Equal(AppRole.Both, AppSettings.Load(home.Path).Role);
        Assert.Empty(login.Calls);

        m.StartAtLogin = true;
        Setup.Finish(m, host);
        Assert.Equal([true], login.Calls);
    }

    [Fact]
    public void Setup_run_again_on_the_library_keeps_its_name_password_and_notes_and_starts_it_at_login()
    {
        using var home = new TempHome();
        string notes = home["Lecture notes"];
        Configs.Save(new Config(home.Path, notes) { PoolName = "Sam's library", PoolPassword = "kept-password" });
        var login = new CountingLoginItems();
        using var host = Host(home, login: login);
        var m = Setup.Make(host, AppRole.Library);
        Assert.True(m.ExistingLibrary);
        Assert.Equal("Sam's library", m.LibraryName);
        Assert.Equal("kept-password", m.Password);
        Assert.Equal(Path.GetFullPath(notes), Path.GetFullPath(m.NotesFolder));
        Assert.StartsWith("Its notes stay in", m.NotesFolderWords, StringComparison.Ordinal);
        Assert.Equal("Your library is already here", m.PasswordTitle);
        m.Go(SetupStep.Password);
        m.LibraryOk = false;
        Assert.Equal("Start library", m.ContinueLabel);
        // The login item is written afresh when setup finishes: one pointing at an older copy of the app works again.
        Assert.True(m.StartAtLogin);
        Setup.Finish(m, host);
        Assert.Equal([true], login.Calls);
    }

    [Fact]
    public void A_new_library_starts_at_login_only_when_asked_unless_it_already_did()
    {
        using var home = new TempHome();
        using (var host = Host(home))
        {
            var m = Setup.Make(host, AppRole.Library);
            Assert.False(m.ExistingLibrary);
            Assert.False(m.StartAtLogin);
            Assert.Equal(LibraryHere.DefaultFolder, m.NotesFolder);
            Assert.Equal("Give your library a password", m.PasswordTitle);
        }
        using (var host = Host(home, login: new CountingLoginItems(starts: true)))
            Assert.True(Setup.Make(host, AppRole.Library).StartAtLogin);
    }

    [Fact]
    public void A_laptops_setup_never_changes_its_login_items()
    {
        using var home = new TempHome();
        var login = new CountingLoginItems(starts: true);
        using var host = Host(home, login: login);
        var m = Setup.Make(host, AppRole.Laptop);
        Assert.True(m.StartAtLogin);
        Setup.Finish(m, host);
        Assert.Empty(login.Calls);
    }

    [Fact]
    public void Installers_name_their_role_and_a_build_that_doesnt_asks()
    {
        Assert.Equal(AppRole.Library, Setup.Preset("library"));
        Assert.Equal(AppRole.Laptop, Setup.Preset("laptop"));
        Assert.Null(Setup.Preset(null));
    }

    [Fact]
    public async Task Connecting_elsewhere_with_the_wrong_password_says_so()
    {
        var (url, app) = await FakeServerAsync(HttpStatusCode.Unauthorized);
        try
        {
            using var home = new TempHome();
            using var host = Host(home);
            var m = Setup.Make(host, AppRole.Laptop);
            m.Address = url;
            m.Password = "whatever";

            await m.ConnectCommand.ExecuteAsync(null);

            Assert.False(m.LibraryOk);
            Assert.Equal("That password isn't right.", m.LibraryResult);
        }
        finally
        {
            await app.DisposeAsync();
        }
    }

    [Fact]
    public async Task Adding_an_already_shown_class_sets_its_times_instead_of_posting_a_duplicate()
    {
        int posts = 0;
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        WebHostBuilderKestrelExtensions.ConfigureKestrel(builder.WebHost, k => k.Listen(IPAddress.Loopback, 0));
        var app = builder.Build();
        app.MapPost("/classes", () => { posts++; return Results.Ok(new { }); });
        await app.StartAsync(TestContext.Current.CancellationToken);
        try
        {
            string url = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
            using var home = new TempHome();
            using var host = Host(home);
            var cc = host.Client();
            cc.ServerUrl = url;
            Configs.SaveClient(cc);
            var t = host.Timetable;
            t.Classes.Add(new TimetableClass("CS 101", []));
            host.SaveTimetable(t);
            var m = Setup.Make(host, AppRole.Laptop);

            m.NewClass = "CS 101";
            m.NewWhen = "Tue Thu 10:00-11:15";
            await m.AddClassCommand.ExecuteAsync(null);

            Assert.Equal(0, posts);
            Assert.Single(m.Classes, c => c.Name == "CS 101");
            Assert.Contains("Tue", m.Classes.Single(c => c.Name == "CS 101").When);
            Assert.Single(host.Timetable.Classes, c => c.Name == "CS 101");
        }
        finally
        {
            await app.DisposeAsync();
        }
    }

    // ---- Classes from Canvas courses ----

    static CanvasConnectModel FoundCourses(FakeLibrary? handler = null)
    {
        handler ??= new FakeLibrary().Json(HttpMethod.Post, "/api/v2/canvas/courses", CanvasShots.FoundCourses);
        var context = CanvasFixtures.Context(handler);
        return new CanvasConnectModel(context, new CanvasWatch(context), forSetup: true);
    }

    [Fact]
    public async Task Courses_found_on_the_Canvas_step_become_ticked_classes_and_skipping_Canvas_leaves_Classes_manual()
    {
        var m = SetupModel.For(SkinKind.Mac);
        m.Go(SetupStep.Canvas);
        var canvas = FoundCourses();
        m.Canvas = canvas;

        await canvas.StartAsync(CanvasFixtures.Load<CanvasApi.State>("state-connected"), [], TestContext.Current.CancellationToken);

        Assert.True(m.HasCourses);
        Assert.Equal(["Calculus II", "Cell and Molecular Biology", "Intro to Programming", "Modern World History", "Study Skills"], m.Courses.Select(c => c.Name));
        Assert.All(m.Courses, c => Assert.True(c.Ticked));
        Assert.Equal("CS 101", m.Courses.Single(c => c.Name == "Intro to Programming").Code);
        Assert.False(m.Courses.Single(c => c.Name == "Study Skills").HasCode);
        Assert.Contains("Canvas courses", m.ClassesLede);

        m.SkipCommand.Execute(null);

        Assert.Equal(SetupStep.Classes, m.Step);
        Assert.False(m.HasCourses);
        Assert.StartsWith("With your timetable", m.ClassesLede);
    }

    /// <summary>A pretend library on a free port that takes classes (POST /classes) and Canvas saves (POST
    /// /api/v2/canvas), and keeps what it was sent.</summary>
    static async Task<(string Url, WebApplication App, List<string> Classes, List<string> CanvasBodies)> CoursesServerAsync()
    {
        var classes = new List<string>();
        var bodies = new List<string>();
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        WebHostBuilderKestrelExtensions.ConfigureKestrel(builder.WebHost, k => k.Listen(IPAddress.Loopback, 0));
        var app = builder.Build();
        app.MapPost("/api/v2/classes", async (HttpRequest r) =>
        {
            var body = await System.Text.Json.JsonDocument.ParseAsync(r.Body);
            lock (classes) classes.Add(body.RootElement.GetProperty("name").GetString()!);
            return Results.Ok(new { });
        });
        app.MapPost("/api/v2/canvas", async (HttpRequest r) =>
        {
            using var reader = new StreamReader(r.Body);
            string text = await reader.ReadToEndAsync();
            lock (bodies) bodies.Add(text);
            return Results.Text(CanvasFixtures.Text("canvas"), "application/json");
        });
        await app.StartAsync();
        string url = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
        return (url, app, classes, bodies);
    }

    [Fact]
    public async Task Continue_on_Classes_adds_the_ticked_courses_links_each_to_its_course_and_asks_for_a_sync()
    {
        var (url, app, posted, bodies) = await CoursesServerAsync();
        try
        {
            using var home = new TempHome();
            using var host = Host(home);
            var cc = host.Client();
            cc.ServerUrl = url;
            Configs.SaveClient(cc);
            var m = Setup.Make(host, AppRole.Laptop);
            m.TakeCourses([new FoundCourse("4201", "CS 101", "Intro to Programming"), new FoundCourse("4202", "BIO 110", "Cell Biology"),
                new FoundCourse("4205", "", "Study Skills")]);
            m.Courses[0].When = "Tue Thu 10:00-11:15";
            m.Courses[2].Ticked = false;

            Assert.True(await Setup.AddCoursesAsync(m, host));

            Assert.Equal(["Cell Biology", "Intro to Programming"], host.Timetable.Classes.Select(c => c.Name).Order());
            Assert.Contains(host.Timetable.Classes.Single(c => c.Name == "Intro to Programming").Times, t => t.Describe().Contains("Tue", StringComparison.Ordinal));
            Assert.Empty(host.Timetable.Classes.Single(c => c.Name == "Cell Biology").Times);
            Assert.Equal(["Cell Biology", "Intro to Programming"], posted.Order());
            for (int i = 0; i < 50 && bodies.Count < 2; i++) await Task.Delay(20, TestContext.Current.CancellationToken); // the sync is asked for without waiting
            Assert.Equal("{\"courses\":{\"Intro to Programming\":4201,\"Cell Biology\":4202}}", bodies[0]);
            Assert.Equal("{\"sync\":true}", bodies[1]);
            Assert.False(m.AddingCourses);
        }
        finally
        {
            await app.DisposeAsync();
        }
    }

    [Fact]
    public async Task A_when_that_cant_be_read_keeps_Classes_open_and_adds_nothing()
    {
        using var home = new TempHome();
        using var host = Host(home);
        var m = Setup.Make(host, AppRole.Laptop);
        m.TakeCourses([new FoundCourse("4201", "CS 101", "Intro to Programming")]);
        m.Courses[0].When = "whenever";
        m.Go(SetupStep.Classes);

        await m.NextCommand.ExecuteAsync(null);

        Assert.Equal(SetupStep.Classes, m.Step);
        Assert.Contains("Intro to Programming", m.ClassProblem);
        Assert.Empty(host.Timetable.Classes);
    }

    [Fact]
    public async Task Mic_check_hears_speech_within_two_seconds()
    {
        string wav = Path.Combine(AppContext.BaseDirectory, "Fixtures", "speech.wav");
        using var check = new MicCheck();
        check.Open(() => new FileMicrophone(wav, speed: 4));
        try
        {
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(2);
            while (!check.Heard && DateTime.UtcNow < deadline) await Task.Delay(20, TestContext.Current.CancellationToken);
            Assert.True(check.Heard);
            Assert.Equal(MicCheck.Bars, check.Levels().Length);
        }
        finally
        {
            check.Close();
        }
    }
}
