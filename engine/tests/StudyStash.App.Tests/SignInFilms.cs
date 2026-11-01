using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.Input;
using StudyStash.App.Platform;
using StudyStash.App.Services;
using StudyStash.App.ViewModels;
using StudyStash.App.Views;
using StudyStash.Core;
using StudyStash.Core.Ai;
using StudyStash.Core.Tests;

namespace StudyStash.App.Tests;

/// <summary>
/// Films of a student's way from the first launch to an AI writing their notes, and of every Sign in, drawn off screen
/// (nothing opens on the computer's screen) into STUDYSTASH_SHOTS/&lt;film&gt;/ with frames.tsv for the GIF maker. The
/// app's own views and view models, wired as the app wires them (the welcome as Shell.ShowWelcome, the lecture page's
/// "Get study notes" and its small window as Shell.GetNotesWith, Settings → Your AI as Shell.ShowSettings), over a real
/// library run in this process in a throwaway home, with stand-in Claude Code, Codex and agy that start signed out and
/// sign in by themselves a few seconds after their own sign-in starts (where the real ones wait for the browser page).
/// Beside the window, the test's own record of every process Syft Notes started and how. Only with STUDYSTASH_FILM=1,
/// on a Mac (the stand-ins are shell scripts); never a real sign-in, account, browser or terminal.
/// <para>To film: HOME set to an empty folder of its own (so nothing can reach ~/.claude, ~/.codex, ~/.gemini or
/// ~/.study-stash), STUDYSTASH_FILM=1 STUDYSTASH_STILL=1 STUDYSTASH_SHOTS=&lt;folder&gt;, then
/// <c>dotnet test tests/StudyStash.App.Tests --no-build --filter SignInFilms</c> (Contact_sheet after the others), and
/// qa-bench/offscreen-film/make-gif.py on each film's folder.</para>
/// </summary>
public sealed class SignInFilms
{
    const string Skip = "A film, not a test: STUDYSTASH_FILM=1, on a Mac.";
    const string Lecture = "rec-20261030-093000-a41c07";
    const string FirstTitle = "Lecture, Friday 30 October at 9:30 AM";
    const string Date = "2026-10-30T09:30:00-07:00";

    /// <summary>Where the lecture is, for a film that stopped.</summary>
    static string Said(FilmLibrary lib) => lib.Store.Get(Lecture) is { } r ? $"status {r.Status}, error {r.Error}, notes {r.SummaryMd?.Length}" : "no lecture";

    static void Look(SkinKind skin)
    {
        Skin.UseTheme(ColourThemes.Default);
        ((App)Application.Current!).UseSkin(skin);
    }

    /// <summary>A stand-in's turns: the plan check's one word first, then the notes.</summary>
    static void Answers(StandIns pc, string id)
    {
        if (id == "gemini")
        {
            pc.Replay(id, [FakeAgents.Agy.Init(), FakeAgents.Agy.Words(1, "ready"), FakeAgents.Agy.Result("ready")], 1);
            pc.Replay(id, [FakeAgents.Agy.Init(Antigravity.AnswerAgent), FakeAgents.Agy.Words(1, FilmLibrary.Notes), FakeAgents.Agy.Result(FilmLibrary.Notes)]);
            return;
        }
        pc.Replay(id, [GuidedSetupTests.Init(), GuidedSetupTests.Says("ready"), GuidedSetupTests.Done("ready")], 1);
        pc.Replay(id, [GuidedSetupTests.Done(FilmLibrary.Notes)]);
    }

    /// <summary>
    /// The lecture page's "Get study notes" buttons, as Shell.GetNotesWith: guided setup's install and sign-in screens in a
    /// small window of their own (here over the library, as it is on the screen), the stand-in's own installer and
    /// sign-in behind them; once the AI's ready the window closes and the library writes with it (Shell.UseForNotesAsync).
    /// </summary>
    sealed class GetReady(Desk desk, StandIns pc, AppHost host, FilmLibrary lib, LibraryModel page)
    {
        public GuidedSetupModel? Guided;
        public Control? View;
        public string Ready = "";
        public bool Writing;

        public void Open(string engine)
        {
            var services = new GuidedServices
            {
                Home = host.Home, Windows = false,
                Find = c => AgentInstall.Find(c, pc.Which),
                Installer = c => pc.Installer(c, Path.Combine(host.Home, "logs", "setup-install.log")),
                SignIn = (c, exe) => new AgentSignIn(c, exe),
                OpenUrl = pc.Pages.Add,
                Downloading = () => host.Downloading?.Fraction,
                AiApps = () => [],
            };
            var guided = Guided = new GuidedSetupModel(SetupModel.For(Skin.Current, AppRole.Both), services, () => host.Settings, host.Save);
            View = Skin.Current == SkinKind.Mac ? new MacGetReady { DataContext = guided, DrawChrome = true } : new WinGetReady { DataContext = guided, DrawChrome = true };
            guided.ReadyForNotes = ready =>
            {
                Ready = ready;
                desk.Over(null);
                _ = UseForNotesAsync(ready);
            };
            desk.Over(View);
            _ = guided.GetReadyForNotesAsync(engine);
        }

        async Task UseForNotesAsync(string engine)
        {
            await lib.Remote.DefaultsAsync(notes: engine, ask: engine);
            await lib.WriteNotesAsync(true);
            host.Save(s => s.SetupAi = engine);
            await lib.Lib.SettingsAsync(HttpMethod.Post, "/write-missing");
            await FilmParts.ShowLectureAsync(page, lib, Lecture);
            Writing = true;
        }
    }

    // --- 1 · the first launch, to Claude writing the first lecture's notes ----------------------------------------------

    [AvaloniaFact]
    public async Task Onboarding_with_Claude()
    {
        Assert.SkipUnless(FilmParts.On, Skip);
        Look(SkinKind.Mac);
        using var pc = new StandIns();
        Answers(pc, "claude");
        using var host = FilmParts.Host(pc["app"]);
        FilmLibrary? lib = null;
        using var desk = new Desk("01-onboarding-claude", pc, ThemeVariant.Light, "first launch on a Mac · stand-in Claude · throwaway library · waits sped up");
        try
        {
            // The welcome, as Shell.ShowWelcome makes it. Get started makes the library: here the throwaway one in this
            // process, made as LibraryHere makes one (its own port, this computer only) and the app pointed at it.
            var setup = Services.Setup.Make(host, AppRole.Both, here: new LibraryHere { Command = ["/usr/bin/false"], Folder = pc["notes"], FirstPort = 9611 });
            setup.OneButton = true;
            setup.OnConnect = async () =>
            {
                setup.Connecting = true;
                lib = await FilmLibrary.StartAsync(pc);
                FilmParts.Connect(host, lib);
                setup.LibraryOk = true;
                setup.LibraryResult = "Sam's library is ready on this Mac.";
                setup.Connecting = false;
            };
            setup.OnGetStarted = () => FirstRun.AfterLibraryAsync(setup, host, lib!.Remote, () => FirstRun.Detect(c => AgentInstall.Find(c, pc.Which), FirstRun.Status), lib.WriteNotesAsync);
            bool finished = false;
            setup.OnFinish = () =>
            {
                Services.Setup.Finish(setup, host);
                finished = true;
            };
            var welcome = new MacWelcome { DataContext = setup, DrawChrome = true };
            desk.Show(welcome);
            desk.Hold(2.6);
            desk.Press(welcome.Part<Button>("GetStarted"));
            await desk.UntilAsync(() => finished, "Get started");

            // Home, as the first run leaves it: "Ready when class starts". No AI was found here, so none is asked for yet.
            var page = FilmParts.Library();
            page.Home.Selected = true;
            var home = OverviewModel.Home(new OverviewModel.Sources { Now = DateTimeOffset.Now, Zone = TimeZoneInfo.Local, NotesOff = true });
            home.OnRecord = () => { };
            page.Overview = home;
            var card = new WelcomeCardModel(true) { RecordKeys = Keybindings.Show(KeyAction.Record, true) };
            card.ShowModel(ready: false, fraction: host.Downloading?.Fraction ?? 0, problem: null, device: "Mac");
            page.Welcome = card;
            var view = FilmParts.LibraryView(page, SkinKind.Mac);
            desk.Show(view);
            desk.Hold(3.2);

            // The first lecture: filed with its transcript, and its page asks which AI writes the notes.
            desk.Caption("After the first lecture");
            lib!.Record(Lecture, FirstTitle, Date);
            await desk.UntilAsync(() => lib.Store.Get(Lecture)?.Status == Store.Done, "the lecture filed");
            var ready = new GetReady(desk, pc, host, lib, page);
            await FilmParts.ShowLectureAsync(page, lib, Lecture, ready.Open);
            desk.Caption("Which AI: Continue with Google is Gemini, free · or Claude, ChatGPT, Gemini, Ollama");
            desk.Hold(3.8);
            desk.Caption("");
            desk.Press(view.Part<Button>("UseClaude"));
            await desk.UntilAsync(() => ready.Guided?.Screen == GuidedScreen.Install, "Get Claude ready");
            desk.Hold(2.8);
            desk.Press(ready.View!.Part<Button>("Install"));
            await desk.UntilAsync(() => ready.Guided!.Installing, "installing");
            await desk.UntilAsync(() => ready.Guided!.Screen == GuidedScreen.SignIn && ready.Guided.ShowOpenSignIn, "installed, on to signing in");
            desk.Hold(2.8);
            desk.Press(ready.View!.Part<Button>("OpenSignIn"));
            await desk.UntilAsync(() => ready.Guided!.WaitingSignIn, "the sign-in waiting");
            desk.Caption("Claude's own sign-in opens your browser · no terminal (a stand-in signs in after 5 s)");
            desk.NoPointer();
            desk.Hold(2.6);
            await desk.UntilAsync(() => ready.Guided!.CheckingPlan || ready.Ready.Length > 0, "signed in", TimeSpan.FromSeconds(40));
            desk.Caption("");
            await desk.UntilAsync(() => ready.Writing, "back to the lecture", TimeSpan.FromSeconds(40));
            desk.Hold(1.2);
            await desk.UntilAsync(() => lib.Store.Get(Lecture) is { Status: Store.Done, SummaryMd.Length: > 0 }, "the notes written", TimeSpan.FromSeconds(40), () => Said(lib));
            await FilmParts.ShowLectureAsync(page, lib, Lecture);
            desk.Caption("Claude writes the notes · after every class from now on");
            desk.Hold(2.4);
            desk.Scroll(420, 2.4);
            desk.Hold(2.0);
            Assert.Equal(1, pc.Logins("claude"));
            Assert.Equal(0, pc.TerminalDoors + pc.RefusedTries);
        }
        finally
        {
            if (lib is not null) await lib.DisposeAsync();
        }
    }

    // --- 2 · Continue with Google, from the first lecture's page -----------------------------------------------------------

    [AvaloniaFact]
    public async Task Onboarding_with_Google()
    {
        Assert.SkipUnless(FilmParts.On, Skip);
        Look(SkinKind.Mac);
        using var pc = new StandIns();
        Answers(pc, "gemini");
        using var host = FilmParts.Host(pc["app"]);
        await using var lib = await FilmLibrary.StartAsync(pc);
        FilmParts.Connect(host, lib);
        await lib.WriteNotesAsync(false); // as Get started leaves it when no AI is signed in here
        lib.Record(Lecture, FirstTitle, Date);
        using var desk = new Desk("02-onboarding-google", pc, ThemeVariant.Light, "the first lecture's page · stand-in Gemini (agy) · throwaway library · waits sped up");
        var page = FilmParts.Library();
        var view = FilmParts.LibraryView(page, SkinKind.Mac);
        desk.Show(view);
        await desk.UntilAsync(() => lib.Store.Get(Lecture)?.Status == Store.Done, "the lecture filed");
        var ready = new GetReady(desk, pc, host, lib, page);
        await FilmParts.ShowLectureAsync(page, lib, Lecture, ready.Open);
        desk.Caption("Continue with Google: Gemini, free with your Google account");
        desk.Hold(3.0);
        desk.Caption("");
        desk.Press(view.Part<Button>("ContinueWithGoogle"));
        await desk.UntilAsync(() => ready.Guided?.Screen == GuidedScreen.Install, "Get Gemini ready");
        desk.Hold(1.6);
        desk.Press(ready.View!.Part<Button>("Install"));
        await desk.UntilAsync(() => ready.Guided!.Screen == GuidedScreen.SignIn && ready.Guided.ShowOpenSignIn, "installed, on to signing in");
        desk.Hold(1.8);
        desk.Press(ready.View!.Part<Button>("OpenSignIn"));
        await desk.UntilAsync(() => ready.Guided!.WaitingSignIn, "the sign-in waiting");
        desk.Caption("Google's own sign-in opens your browser · no terminal (a stand-in signs in after 5 s)");
        desk.NoPointer();
        desk.Hold(2.6);
        await desk.UntilAsync(() => ready.Writing, "Gemini ready, back to the lecture", TimeSpan.FromSeconds(60));
        desk.Caption("");
        desk.Hold(1.0);
        await desk.UntilAsync(() => lib.Store.Get(Lecture) is { Status: Store.Done, SummaryMd.Length: > 0 }, "the notes written", TimeSpan.FromSeconds(40), () => Said(lib));
        await FilmParts.ShowLectureAsync(page, lib, Lecture);
        desk.Caption("Gemini writes the notes · after every class from now on");
        desk.Hold(2.4);
        desk.Scroll(420, 2.0);
        desk.Hold(1.6);
        Assert.Equal(0, pc.TerminalDoors + pc.RefusedTries);
    }

    // --- 3 · Settings → Your AI: Claude, ChatGPT and Gemini, each signed in in turn -------------------------------------

    /// <summary>Settings, as Shell.ShowSettings opens it at Your AI, over the film's library.</summary>
    static async Task<(SettingsModel Model, Control View)> SettingsAsync(AppHost host, FilmLibrary lib)
    {
        var model = SettingsModel.Make(host, ai: lib.Remote);
        model.Section = "AI";
        await model.Engines.Load();
        await model.AiProblems.Load();
        return (model, new SettingsView { DataContext = model, DrawChrome = true });
    }

    /// <summary>The Sign in on <paramref name="id"/>'s row, pressed; then its waiting, until it's ready by itself.</summary>
    static async Task SignInRowAsync(Desk desk, SettingsModel model, Control view, string id, string brand)
    {
        var row = model.Engines.Engines.Single(r => r.Id == id);
        desk.Pointer();
        desk.Press(Desk.ButtonIn(view, b => b.DataContext == row && b.Command == row.ActCommand));
        await desk.UntilAsync(() => model.Engines.Engines.Single(r => r.Id == id).SignIn == SignInPhase.Waiting, $"{brand} waiting");
        desk.NoPointer();
        desk.Caption($"{brand}'s own sign-in opens your browser · no terminal (a stand-in signs in after 5 s)");
        desk.Hold(1.6);
        await desk.UntilAsync(() => model.Engines.Engines.Single(r => r.Id == id).State == "ready", $"{brand} ready", TimeSpan.FromSeconds(40));
        desk.Caption("");
        desk.Hold(1.4);
    }

    /// <summary>Set up on <paramref name="id"/>'s row, pressed: the stand-in's installer (as AgentInstall runs the maker's),
    /// then its own sign-in, hidden, until it's ready by itself.</summary>
    static async Task SetUpRowAsync(Desk desk, SettingsModel model, Control view, StandIns pc, AppHost host, string id, string brand)
    {
        // The stand-in's installer takes no time: it starts once the film has shown the row setting up.
        var go = new TaskCompletionSource();
        model.Engines.Install = async (engine, phase) =>
        {
            var install = pc.Installer(AgentCli.Get(engine), Path.Combine(host.Home, "logs", "setup-install.log"));
            install.Phase += phase;
            await go.Task;
            return await install.RunAsync();
        };
        var row = model.Engines.Engines.Single(r => r.Id == id);
        desk.Pointer();
        desk.Press(Desk.ButtonIn(view, b => b.DataContext == row && b.Command == row.ActCommand));
        await desk.UntilAsync(() => model.Engines.Engines.Single(r => r.Id == id).StateWords == "Setting up", $"{brand} setting up");
        desk.NoPointer();
        desk.Caption($"Set up: Syft Notes installs {brand} for you (the stand-in's installer) · nothing to type");
        desk.Hold(2.2);
        go.SetResult();
        await desk.UntilAsync(() => model.Engines.Engines.Single(r => r.Id == id).SignIn == SignInPhase.Waiting, $"{brand} waiting");
        desk.Caption($"Then {brand}'s own sign-in opens your browser · no terminal (a stand-in signs in after 5 s)");
        desk.Hold(1.6);
        await desk.UntilAsync(() => model.Engines.Engines.Single(r => r.Id == id).State == "ready", $"{brand} ready", TimeSpan.FromSeconds(40));
        desk.Caption("");
        desk.Hold(1.4);
    }

    [AvaloniaFact]
    public async Task Signing_in_from_Settings()
    {
        Assert.SkipUnless(FilmParts.On, Skip);
        Look(SkinKind.Mac);
        using var pc = new StandIns();
        foreach (string id in new[] { "claude", "codex" }) pc.Install(id);
        using var host = FilmParts.Host(pc["app"]);
        await using var lib = await FilmLibrary.StartAsync(pc, s =>
        {
            foreach (string id in new[] { "claude", "codex", "gemini" }) s.Tests[id] = "Not logged in · Please run /login";
        });
        FilmParts.Connect(host, lib);
        using var desk = new Desk("03-settings-all-four", pc, ThemeVariant.Light, "Settings → Your AI · stand-in Claude, ChatGPT and Gemini · throwaway library · waits sped up");
        var (model, view) = await SettingsAsync(host, lib);
        desk.Show(view);
        desk.Hold(1.6);
        desk.ScrollTo(Desk.Words(view, "AIs on this computer"));
        desk.Caption("All four, always: Claude, ChatGPT, Gemini, Ollama (advanced)");
        desk.Hold(3.0);
        desk.Caption("");
        await SignInRowAsync(desk, model, view, "claude", "Claude");
        await SignInRowAsync(desk, model, view, "codex", "ChatGPT");
        await SetUpRowAsync(desk, model, view, pc, host, "gemini", "Gemini");
        desk.NoPointer();
        desk.Caption("Claude, ChatGPT and Gemini ready · nothing to type, no terminal");
        desk.Hold(3.0);
        Assert.Equal((1, 1, true), (pc.Logins("claude"), pc.Logins("codex"), pc.SignedIn("gemini")));
        Assert.Equal(0, pc.TerminalDoors + pc.RefusedTries);
        model.Dispose();
    }

    // --- 4 · Claude's login ran out: the lecture says so, Sign in, and the missed notes are written ----------------------

    [AvaloniaFact]
    public async Task Claudes_login_ran_out()
    {
        Assert.SkipUnless(FilmParts.On, Skip);
        Look(SkinKind.Mac);
        using var pc = new StandIns();
        pc.Install("claude");
        pc.Replay("claude", [GuidedSetupTests.Done(FilmLibrary.Notes)]);
        using var host = FilmParts.Host(pc["app"]);
        // Claude wrote the notes until its sign-in ran out (it isn't signed in now).
        await using var lib = await FilmLibrary.StartAsync(pc, s =>
        {
            s.ByJob["notes"] = new AiChoice("claude");
            s.ByJob["ask"] = new AiChoice("claude");
            s.Tests["claude"] = "works";
        });
        FilmParts.Connect(host, lib);
        await lib.WriteNotesAsync(true);
        lib.Record(Lecture, FirstTitle, Date);
        using var desk = new Desk("04-claude-login-ran-out", pc, ThemeVariant.Light, "Claude's sign-in ran out · stand-in Claude · throwaway library · waits sped up");
        await desk.UntilAsync(() => lib.Store.Get(Lecture) is { Status: Store.Done, Error.Length: > 0 }, "the lecture filed without notes", TimeSpan.FromSeconds(40));
        var page = FilmParts.Library();
        var library = FilmParts.LibraryView(page, SkinKind.Mac);
        await FilmParts.ShowLectureAsync(page, lib, Lecture);
        desk.Show(library);
        desk.NoPointer();
        desk.Hold(1.2);
        // The notification the app shows when a lecture is filed without notes for that reason (NoticeWords.Filed).
        var row = lib.Store.Get(Lecture)!;
        var (title, text, action) = NoticeWords.Filed("", page.Note!.Title, row.Error!);
        SettingsModel? settings = null;
        Control? settingsView = null;
        var toast = new ToastView { Title = title, Text = text, ActionLabel = action, Width = 380 };
        toast.ActionCommand = new AsyncRelayCommand(async () =>
        {
            desk.Toast(null);
            (settings, settingsView) = await SettingsAsync(host, lib);
            desk.Show(settingsView);
        });
        desk.Toast(toast);
        desk.Pointer();
        desk.Hold(2.4);
        desk.Press(toast.GetVisualDescendants().OfType<Button>().First(b => b.Name == "ActionButton"));
        await desk.UntilAsync(() => settings?.AiProblems.Problems.Any(p => p.Kind == "not_signed_in") == true, "Settings → Your AI with the card");
        desk.Hold(2.0);
        var problem = settings!.AiProblems.Problems.First(p => p.Kind == "not_signed_in");
        desk.Press(Desk.ButtonIn(settingsView!, b => b.DataContext == problem && b.Command == problem.PrimaryCommand));
        await desk.UntilAsync(() => settings.AiProblems.Problems.Any(p => p.Kind == "not_signed_in" && p.Title.StartsWith("Finish", StringComparison.Ordinal)), "the card waiting");
        desk.Caption("Claude's own sign-in opens your browser · no terminal (a stand-in signs in after 5 s)");
        desk.NoPointer();
        await desk.UntilAsync(() => settings.AiProblems.Problems.All(p => p.Kind != "not_signed_in"), "the card gone by itself", TimeSpan.FromSeconds(40));
        desk.Caption("");
        desk.Hold(1.2);
        // The card's gone, and Your AI's own Claude row below it was told by the card's sign-in: it says ready too.
        desk.ScrollTo(Desk.Words(settingsView!, "AIs on this computer"));
        desk.Caption("Claude's own row below says it is signed in too, with no need to reopen Your AI");
        desk.Hold(3.6);
        desk.Caption("");
        await desk.UntilAsync(() => lib.Store.Get(Lecture) is { SummaryMd.Length: > 0 }, "the missed notes written", TimeSpan.FromSeconds(40));
        desk.Caption("Back on the lecture: the notes Claude missed were written by themselves");
        await FilmParts.ShowLectureAsync(page, lib, Lecture);
        desk.Show(library);
        desk.Hold(3.4);
        Assert.Equal(1, pc.Logins("claude"));
        Assert.Equal(0, pc.TerminalDoors + pc.RefusedTries);
        settings.Dispose();
    }

    // --- 5 · the fallbacks: it gave up, Try again; it couldn't start, Get help and Try again ------------------------------------

    [AvaloniaFact]
    public async Task The_fallbacks()
    {
        Assert.SkipUnless(FilmParts.On, Skip);
        Look(SkinKind.Mac);
        using var pc = new StandIns();
        pc.Install("claude");
        pc.Install("codex");
        pc.Delay("claude", 120); // the student doesn't finish in the browser this time
        pc.LoginFails("codex", true); // Codex's hidden sign-in stops at once (it couldn't open the browser)
        using var host = FilmParts.Host(pc["app"]);
        await using var lib = await FilmLibrary.StartAsync(pc, s =>
        {
            foreach (string id in new[] { "claude", "codex" }) s.Tests[id] = "Not logged in · Please run /login";
        }, patience: TimeSpan.FromSeconds(8));
        FilmParts.Connect(host, lib);
        using var desk = new Desk("05-fallbacks-gave-up-and-stuck", pc, ThemeVariant.Light, "Settings → Your AI · the library waits 8 s here (10 minutes really) · stand-ins · waits sped up");
        var (model, view) = await SettingsAsync(host, lib);
        model.Engines.OpenUrl = pc.Pages.Add; // Get help, written down: no browser opens
        desk.Show(view);
        desk.Hold(1.2);
        desk.ScrollTo(Desk.Words(view, "AIs on this computer"));
        desk.Hold(1.2);
        AiEngineRow Row(string id) => model.Engines.Engines.Single(r => r.Id == id);
        var claude = Row("claude");
        desk.Press(Desk.ButtonIn(view, b => b.DataContext == claude && b.Command == claude.ActCommand));
        await desk.UntilAsync(() => Row("claude").SignIn == SignInPhase.Waiting, "Claude waiting");
        desk.NoPointer();
        desk.Caption("Not finished in the browser…");
        await desk.UntilAsync(() => Row("claude").SignIn == SignInPhase.GaveUp, "Claude gave up", TimeSpan.FromSeconds(40));
        desk.Caption("It stopped waiting: Try again");
        desk.Hold(2.4);
        pc.Delay("claude", 4); // this time the student finishes
        var gaveUp = Row("claude");
        desk.Pointer();
        desk.Press(Desk.ButtonIn(view, b => b.DataContext == gaveUp && b.Command == gaveUp.ActCommand));
        await desk.UntilAsync(() => Row("claude").SignIn == SignInPhase.Waiting, "Claude waiting again");
        desk.NoPointer();
        desk.Caption("");
        await desk.UntilAsync(() => Row("claude").State == "ready", "Claude ready", TimeSpan.FromSeconds(40));
        desk.Hold(1.6);

        var codex = Row("codex");
        desk.Pointer();
        desk.Press(Desk.ButtonIn(view, b => b.DataContext == codex && b.Command == codex.ActCommand));
        await desk.UntilAsync(() => Row("codex").SignIn == SignInPhase.Stuck, "ChatGPT stuck", TimeSpan.FromSeconds(40));
        var stuck = Row("codex");
        desk.Caption("It couldn't start: Try again, or Get help · never a terminal");
        desk.Hold(2.4);
        desk.Press(Desk.ButtonIn(view, b => b.DataContext == stuck && b.Name == "SignInLink"));
        desk.Caption("Get help opens syftnotes.com/support (written down here: no browser opens)");
        desk.Hold(2.6);
        pc.LoginFails("codex", false);
        pc.Delay("codex", 4);
        var again = Row("codex");
        desk.Caption("");
        desk.Press(Desk.ButtonIn(view, b => b.DataContext == again && b.Command == again.ActCommand));
        await desk.UntilAsync(() => Row("codex").SignIn == SignInPhase.Waiting, "ChatGPT waiting");
        desk.NoPointer();
        await desk.UntilAsync(() => Row("codex").State == "ready", "ChatGPT ready", TimeSpan.FromSeconds(40));
        desk.Caption("Try again worked");
        desk.Hold(2.4);
        Assert.Equal([AiWords.HelpPage], pc.Pages);
        Assert.Equal(0, pc.TerminalDoors + pc.RefusedTries);
        model.Dispose();
    }

    // --- the contact sheet: key frames of the films above, labelled ---------------------------------------------------------

    /// <summary>Key frames of the films (run them first, into the same STUDYSTASH_SHOTS), three across with a label under
    /// each, into contact-sheet.png there.</summary>
    [AvaloniaFact]
    public void Contact_sheet()
    {
        Assert.SkipUnless(FilmParts.On && File.Exists(Path.Combine(Shot.Dir, "01-onboarding-claude", "f-0020.png")), "The films first: STUDYSTASH_FILM=1.");
        (string Film, int Frame, string Label)[] tiles =
        [
            ("01-onboarding-claude", 20, "1 · First launch: one button"),
            ("01-onboarding-claude", 70, "1 · Home after Get started"),
            ("01-onboarding-claude", 120, "1 · The first lecture: Gemini named, all four AIs"),
            ("01-onboarding-claude", 165, "1 · Get Claude ready (the stand-in's installer)"),
            ("01-onboarding-claude", 275, "1 · Waiting: hidden terminal, no window"),
            ("01-onboarding-claude", 390, "1 · Claude's notes, written by themselves"),
            ("02-onboarding-google", 30, "2 · Continue with Google: Gemini, free"),
            ("02-onboarding-google", 150, "2 · Sign in to Gemini: waiting"),
            ("02-onboarding-google", 250, "2 · Gemini's notes"),
            ("03-settings-all-four", 60, "3 · Settings → Your AI: all four, always"),
            ("03-settings-all-four", 240, "3 · Set up Gemini: installs, then signs in"),
            ("03-settings-all-four", 330, "3 · Claude, ChatGPT, Gemini ready"),
            ("04-claude-login-ran-out", 115, "4 · Login ran out: the card, waiting"),
            ("04-claude-login-ran-out", 230, "4 · The missed notes, written"),
            ("05-fallbacks-gave-up-and-stuck", 90, "5 · It gave up: Try again"),
            ("05-fallbacks-gave-up-and-stuck", 215, "5 · Couldn't start: Try again, Get help, no terminal"),
            ("06-windows-dark-all-four", 60, "6 · Windows look, dark: all four"),
            ("06-windows-dark-all-four", 190, "6 · Windows look, dark: Gemini set up, waiting"),
        ];
        const double W = 600;
        var grid = new Avalonia.Controls.Primitives.UniformGrid { Columns = 3, Background = Avalonia.Media.Brushes.White };
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
                    new TextBlock { Text = label, FontSize = 17, FontWeight = Avalonia.Media.FontWeight.SemiBold, Foreground = Avalonia.Media.Brushes.Black },
                },
            });
        }
        // A headless window doesn't size itself to what it holds: it's given the sheet's size.
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

    // --- 6 · Windows, dark: all four AIs in Settings → Your AI, Claude signed in and Gemini set up -------------------------------

    [AvaloniaFact]
    public async Task Signing_in_on_Windows_in_dark()
    {
        Assert.SkipUnless(FilmParts.On, Skip);
        Look(SkinKind.Win);
        using var pc = new StandIns();
        pc.Install("claude");
        pc.Install("codex");
        File.WriteAllText(Path.Combine(pc.State("codex"), "signed-in"), "");
        using var host = FilmParts.Host(pc["app"]);
        await using var lib = await FilmLibrary.StartAsync(pc, s =>
        {
            s.Tests["claude"] = "Not logged in · Please run /login";
            s.Tests["codex"] = "works";
        });
        FilmParts.Connect(host, lib);
        using var desk = new Desk("06-windows-dark-all-four", pc, ThemeVariant.Dark, "the Windows look, dark, drawn on a Mac · stand-ins · throwaway library · waits sped up",
            note: "The Windows look, drawn on a Mac: these are the Mac's processes. CI's Windows runner checks the real CLIs for windows of their own.");
        var (model, view) = await SettingsAsync(host, lib);
        desk.Show(view);
        desk.Hold(1.4);
        desk.ScrollTo(Desk.Words(view, "AIs on this computer"));
        desk.Caption("All four, always: Claude, ChatGPT, Gemini, Ollama (advanced)");
        desk.Hold(2.6);
        desk.Caption("");
        await SignInRowAsync(desk, model, view, "claude", "Claude");
        await SetUpRowAsync(desk, model, view, pc, host, "gemini", "Gemini");
        desk.NoPointer();
        desk.Hold(2.4);
        Assert.Equal(0, pc.TerminalDoors + pc.RefusedTries);
        model.Dispose();
        Look(SkinKind.Mac);
    }
}
