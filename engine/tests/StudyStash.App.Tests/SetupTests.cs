using Avalonia.Headless.XUnit;
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
using StudyStash.App.Views;
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
    public void Each_of_the_three_setups_has_its_own_steps()
    {
        // Just this computer: no password, no library to find; who writes the notes comes after the model.
        var one = SetupModel.For(SkinKind.Mac);
        Assert.Equal(AppRole.Both, one.Role);
        Assert.Equal(["Welcome", "Microphone", "Transcription model", "Notes", "Canvas", "Classes", "Start at login", "Done"], Titles(one));
        Assert.Equal([1, 2, 3, 4, 5, 6, 7, 8], one.Steps.Select(s => s.Number));

        var laptop = SetupModel.For(SkinKind.Mac, AppRole.Laptop);
        Assert.Equal(["Welcome", "Your library", "Microphone", "Transcription model", "Canvas", "Classes", "Done"], Titles(laptop));

        var library = SetupModel.For(SkinKind.Mac, AppRole.Library);
        Assert.Equal(["Welcome", "Password", "AI engines", "Canvas", "Classes", "Start at login", "Connect your laptop"], Titles(library));
    }

    [Fact]
    public void Windows_adds_the_taskbar_only_where_Study_Stash_records()
    {
        Assert.Equal(["Welcome", "Your library", "Microphone", "Transcription model", "Canvas", "Classes", "Taskbar", "Done"],
            Titles(SetupModel.For(SkinKind.Win, AppRole.Laptop)));
        Assert.Equal(["Welcome", "Microphone", "Transcription model", "Notes", "Canvas", "Classes", "Start at login", "Taskbar", "Done"],
            Titles(SetupModel.For(SkinKind.Win)));
        Assert.DoesNotContain(SetupStep.Taskbar, SetupModel.StepsFor(AppRole.Library, SkinKind.Win));
        Assert.DoesNotContain(SetupStep.Taskbar, SetupModel.StepsFor(AppRole.Laptop, SkinKind.Mac));
        Assert.DoesNotContain(SetupStep.Taskbar, SetupModel.StepsFor(AppRole.Both, SkinKind.Mac));
    }

    [Fact]
    public void The_three_setups_read_differently()
    {
        var one = SetupModel.For(SkinKind.Mac);
        var laptop = SetupModel.For(SkinKind.Mac, AppRole.Laptop);
        var library = SetupModel.For(SkinKind.Mac, AppRole.Library);
        Assert.Equal("Set up Study Stash on this Mac", one.HeaderTitle);
        Assert.Equal("Set up Study Stash on this Mac", laptop.HeaderTitle);
        Assert.Equal("Set up your library", library.HeaderTitle);
        Assert.Equal(["One-computer setup", "Laptop setup", "Library setup"], new[] { one.FlowName, laptop.FlowName, library.FlowName });
        Assert.Equal(3, new[] { one.FlowIcon, laptop.FlowIcon, library.FlowIcon }.Distinct().Count());
        Assert.All(new[] { one, laptop, library }, m => Assert.Equal("Welcome", m.StepLabel));
        Assert.Equal("Set up Study Stash on this PC", SetupModel.For(SkinKind.Win).HeaderTitle);
        Assert.Contains("all on this Mac", one.OneComputerAbout, StringComparison.Ordinal);
        Assert.Contains("No server needed", one.OneComputerAbout, StringComparison.Ordinal);
        Assert.Contains("Mac mini", library.LibraryAbout, StringComparison.Ordinal);
        Assert.DoesNotContain("Mac", SetupModel.For(SkinKind.Win).LibraryAbout, StringComparison.Ordinal);
    }

    [Fact]
    public void The_welcome_starts_on_just_this_computer_and_each_choice_brings_its_own_steps()
    {
        var m = SetupModel.For(SkinKind.Mac);
        Assert.True(m.IsOneComputer);
        Assert.False(m.IsLaptop || m.IsLibrary);
        Assert.Equal("Welcome to Study Stash", m.WelcomeTitle);
        m.LibraryName = "Ada's library";
        m.Password = "correct-horse";
        m.Address = "http://mac-mini:8787";

        m.ChooseLaptopCommand.Execute(null);
        Assert.Equal(AppRole.Laptop, m.Role);
        Assert.Equal(SetupStep.Welcome, m.Step);
        Assert.Equal(SetupStep.Library, m.Steps[1].Step);

        m.ChooseLibraryCommand.Execute(null);
        Assert.Equal(AppRole.Library, m.Role);
        Assert.Equal(SetupStep.Password, m.Steps[1].Step);
        Assert.Equal("Set up your library", m.HeaderTitle);

        m.ChooseOneComputerCommand.Execute(null);
        Assert.Equal(AppRole.Both, m.Role);
        Assert.Equal(SetupStep.Microphone, m.Steps[1].Step);
        // A library made for one choice is made again for another (each keeps it its own way).
        m.LibraryOk = true;
        m.ChooseLibraryCommand.Execute(null);
        Assert.False(m.LibraryOk);
        m.ChooseOneComputerCommand.Execute(null);
        // What was typed stays, whichever card was tried.
        Assert.Equal(("Ada's library", "correct-horse", "http://mac-mini:8787"), (m.LibraryName, m.Password, m.Address));
    }

    [Fact]
    public void Installers_suggest_a_choice_and_a_run_that_stopped_part_way_keeps_its_own()
    {
        // The library download suggests the library; the laptop download, or a build that doesn't say, just this computer.
        Assert.Equal(AppRole.Library, Setup.StartingRole(AppRole.Library, AppRole.Laptop, ""));
        Assert.Equal(AppRole.Both, Setup.StartingRole(AppRole.Laptop, AppRole.Laptop, ""));
        Assert.Equal(AppRole.Both, Setup.StartingRole(null, AppRole.Laptop, ""));
        // A library already made here keeps its flow, whatever the download.
        Assert.Equal(AppRole.Both, Setup.StartingRole(AppRole.Library, AppRole.Both, "http://127.0.0.1:8787"));
        Assert.Equal(AppRole.Library, Setup.StartingRole(AppRole.Laptop, AppRole.Library, "http://127.0.0.1:8787"));
        // Already connected to a library on another computer: a laptop.
        Assert.Equal(AppRole.Laptop, Setup.StartingRole(AppRole.Laptop, AppRole.Laptop, "http://mac-mini:8787"));
        Assert.Equal(AppRole.Both, Setup.StartingRole(null, AppRole.Laptop, "http://127.0.0.1:8787"));
        Assert.Equal(AppRole.Both, Setup.StartingRole(null, AppRole.Laptop, "http://localhost:8787"));
    }

    [Fact]
    public async Task Continue_on_just_this_computers_welcome_makes_the_library_first_and_stays_if_it_cant()
    {
        var m = SetupModel.For(SkinKind.Mac);
        int tries = 0;
        m.OnConnect = () =>
        {
            tries++;
            m.LibraryOk = tries > 1;
            m.LibraryResult = m.LibraryOk ? "Ada's library is ready on this Mac." : "The library didn't start.";
            return Task.CompletedTask;
        };
        Assert.Equal("Continue", m.ContinueLabel);

        await m.NextCommand.ExecuteAsync(null);
        Assert.Equal(SetupStep.Welcome, m.Step);
        Assert.True(m.HasWelcomeProblem);

        await m.NextCommand.ExecuteAsync(null);
        Assert.Equal(SetupStep.Microphone, m.Step);
        Assert.False(m.HasWelcomeProblem);
        Assert.Equal("Ready on this Mac", m.LibrarySummary);

        // Back to the welcome and on again: the library is already made.
        m.BackCommand.Execute(null);
        await m.NextCommand.ExecuteAsync(null);
        Assert.Equal(2, tries);

        // The laptop's welcome makes nothing: it connects on its own step.
        var laptop = SetupModel.For(SkinKind.Mac, AppRole.Laptop);
        laptop.OnConnect = () => throw new InvalidOperationException("the laptop's welcome shouldn't connect");
        await laptop.NextCommand.ExecuteAsync(null);
        Assert.Equal(SetupStep.Library, laptop.Step);
    }

    [Fact]
    public void Just_this_computers_last_steps_say_how_to_record_and_how_to_add_a_laptop_later()
    {
        var m = SetupModel.For(SkinKind.Mac);
        m.Go(SetupStep.StartAtLogin);
        Assert.Equal("Keep Study Stash running", m.StartAtLoginTitle);
        Assert.Contains("whenever this Mac is on", m.StartAtLoginLede, StringComparison.Ordinal);
        Assert.DoesNotContain("laptop", m.StartAtLoginLede, StringComparison.OrdinalIgnoreCase);
        m.Go(SetupStep.Done);
        Assert.True(m.OnLaptopDone);
        Assert.True(m.OnOneComputerDone);
        Assert.False(m.OnLibraryDone);
        Assert.Equal("Done", m.Steps[^1].Title);
        Assert.Contains("Settings → Your library", m.AddLaptopLater, StringComparison.Ordinal);
        Assert.False(m.ShowNotesSummary);
        m.NotesSummary = "Ollama";
        Assert.True(m.ShowNotesSummary);

        var library = SetupModel.For(SkinKind.Mac, AppRole.Library);
        library.Go(SetupStep.StartAtLogin);
        Assert.Equal("Keep your library running", library.StartAtLoginTitle);
        library.Go(SetupStep.Done);
        Assert.False(library.OnOneComputerDone);
        Assert.False(library.ShowNotesSummary);
    }

    [AvaloniaFact]
    public async Task The_setup_window_grows_while_an_engines_setup_steps_show()
    {
        var m = SetupModel.For(SkinKind.Mac);
        m.Go(SetupStep.Ai);
        var o = AiDemo.Overview();
        m.Ai = new AiSetupModel(new FakeAiLibrary { Overview = o with { Engines = [.. o.Engines.Select(e => e.Id == "claude" ? e with { State = "not_installed", Installed = false } : e)] } });
        await m.Ai.Load();
        var view = new MacSetup { DataContext = m };
        Assert.Equal(640, view.Height);
        Assert.False(m.AiHelpOpen);

        m.Ai.Engines.Single(r => r.Id == "claude").ToggleHelpCommand.Execute(null);

        Assert.True(m.AiHelpOpen);
        Assert.Equal(800, view.Height);
        m.Go(SetupStep.Canvas);
        Assert.False(m.AiHelpOpen);
    }

    [Fact]
    public void Canvas_comes_before_classes_and_its_page_says_continue()
    {
        var mac = SetupModel.For(SkinKind.Mac, AppRole.Laptop);
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

        var one = SetupModel.For(SkinKind.Mac);
        one.Go(SetupStep.Ai);
        Assert.Equal("Step 4 of 8", one.StepLabel);
    }

    [Fact]
    public async Task Skip_moves_on_and_a_step_that_saves_first_can_hold_Continue()
    {
        var m = SetupModel.For(SkinKind.Mac, AppRole.Laptop);
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
        Assert.Equal(SetupStep.Canvas, m.Step);
    }

    [Fact]
    public void Back_and_next_keep_the_step_checks_right()
    {
        var m = SetupModel.For(SkinKind.Mac, AppRole.Laptop); // Welcome
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
        var m = SetupModel.For(SkinKind.Mac, AppRole.Laptop);
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
        var m = Setup.Make(host, AppRole.Both);

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
    public void Installers_name_their_role_and_a_build_that_doesnt_says_nothing()
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
    public async Task Classes_lists_the_librarys_classes_and_adds_one_with_what_it_covers()
    {
        await using var rig = await LibraryRig.StartAsync();
        using var home = new TempHome();
        using var host = Host(home);
        var cc = host.Client();
        cc.ServerUrl = rig.Url;
        cc.PoolKey = LibraryRig.Password;
        Configs.SaveClient(cc);
        await host.CheckLibraryAsync();
        var m = Setup.Make(host, AppRole.Laptop);
        Assert.Equal(["CS 101", "BIO 110"], m.Classes.Select(c => c.Name));

        m.NewClass = "HIST 210";
        m.NewAbout = "Modern European history, 1789 to 1914";
        await m.AddClassCommand.ExecuteAsync(null);
        Assert.Null(m.ClassProblem);
        Assert.Equal("Modern European history, 1789 to 1914", m.Classes.Single(c => c.Name == "HIST 210").About);
        Assert.Equal(("", ""), (m.NewClass, m.NewAbout));
        // The library has it, and what it covers, for its AI to sort by.
        Assert.Equal("Modern European history, 1789 to 1914", Configs.Load(rig.Home).Classes.Single(c => c.Name == "HIST 210").Description);

        // One it already has isn't added twice.
        m.NewClass = "CS 101";
        await m.AddClassCommand.ExecuteAsync(null);
        Assert.Single(m.Classes, c => c.Name == "CS 101");
        Assert.Single(Configs.Load(rig.Home).Classes, c => c.Name == "CS 101");

        // A name the library won't take: said, and nothing added.
        m.NewClass = "Unsorted";
        await m.AddClassCommand.ExecuteAsync(null);
        Assert.StartsWith("The library didn't take the class", m.ClassProblem);
        Assert.DoesNotContain(m.Classes, c => c.Name == "Unsorted");
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
        var m = SetupModel.For(SkinKind.Mac, AppRole.Laptop);
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
        Assert.StartsWith("The library reads each lecture and files it under the class it's about", m.ClassesLede);
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
            string about = body.RootElement.TryGetProperty("description", out var d) && d.ValueKind == System.Text.Json.JsonValueKind.String ? d.GetString()! : "";
            lock (classes) classes.Add(about.Length > 0 ? $"{body.RootElement.GetProperty("name").GetString()} · {about}" : body.RootElement.GetProperty("name").GetString()!);
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
            m.Courses[2].Ticked = false;

            Assert.True(await Setup.AddCoursesAsync(m, host));

            // Each ticked course is a class named as on Canvas, which says what it covers (the library's AI sorts by it).
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
