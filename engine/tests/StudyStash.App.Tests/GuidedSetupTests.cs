using System.Diagnostics;
using System.Text.Json.Nodes;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using StudyStash.App.Services;
using StudyStash.App.ViewModels;
using StudyStash.Audio;
using StudyStash.Core;
using StudyStash.Core.Ai;
using StudyStash.Core.Setup;
using StudyStash.Core.Tests;
using StudyStash.Library;

namespace StudyStash.App.Tests;

/// <summary>
/// Guided setup from start to finish with fakes only: a fake installer that drops a fake Claude Code or Codex into a
/// test's own folder, fake sign-in, fake chat turns replayed by the fake CLI, and the real setup tools behind their real
/// door, called by the test the way the CLI would call them. Never a real installer, account, microphone or download.
/// </summary>
public sealed class GuidedSetupTests
{
    static readonly TimeSpan Soon = TimeSpan.FromSeconds(30);

    static string BuiltEngine()
    {
        var bin = new DirectoryInfo(AppContext.BaseDirectory);
        string config = bin.Parent!.Name, framework = bin.Name;
        string engineDir = Path.GetFullPath(Path.Combine(bin.FullName, "..", "..", "..", "..", "..", "src", "StudyStash.Engine", "bin", config, framework));
        return Path.Combine(engineDir, OperatingSystem.IsWindows() ? "studystash.exe" : "studystash");
    }

    internal static async Task Until(Func<bool> done, string what)
    {
        var took = Stopwatch.StartNew();
        while (!done() && took.Elapsed < Soon) await Task.Delay(25, TestContext.Current.CancellationToken);
        Assert.True(done(), $"waited {Soon.TotalSeconds:0} s for {what}");
    }

    internal static string Init(string session = "s-1") => new JsonObject
    {
        ["type"] = "system", ["subtype"] = "init", ["session_id"] = session,
        ["tools"] = new JsonArray("mcp__study_stash_setup__get_setup_status"),
        ["mcp_servers"] = new JsonArray(new JsonObject { ["name"] = "study_stash_setup", ["status"] = "connected" }),
    }.ToJsonString();

    internal static string Says(string text) => new JsonObject
    {
        ["type"] = "assistant", ["message"] = new JsonObject { ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = text }) },
    }.ToJsonString();

    internal static string Done(string text, string session = "s-1", bool error = false) =>
        new JsonObject { ["type"] = "result", ["is_error"] = error, ["result"] = text, ["session_id"] = session }.ToJsonString();

    /// <summary>One computer's guided setup: its app, its setup, the guided model and driver, the setup tools'
    /// door, and a folder the fake installer installs the fake CLI into.</summary>
    internal sealed class Rig : IAsyncDisposable
    {
        public TempHome Home { get; }
        /// <summary>The window's microphone check, ticked the way the app's own tick does.</summary>
        public MicCheck Mic { get; } = new();
        public AppHost Host { get; }
        public SetupModel Setup { get; }
        public GuidedSetupModel Guided { get; }
        public GuidedSetup Driver { get; }
        public SetupTools Tools { get; }
        public SetupMcpHost Door { get; private set; } = null!;
        public string Bin => Home["bin"];
        public CountingLoginItems Login { get; } = new();
        public Mirror Models { get; } = new();
        public List<string> Opened { get; } = [];
        public List<string> Terminals { get; } = [];
        public List<string> Writers { get; } = [];
        public string InstallMode { get; set; } = "ok";
        /// <summary>The ChatGPT and Claude desktop apps this computer has, and the ones a card connected.</summary>
        public List<AiAppState> AiApps { get; } = [];
        public List<string> Connected { get; } = [];

        public Rig(string cli = "claude", bool installed = false, bool signedIn = false, AppRole? role = null, bool again = false, Action<AppSettings>? saved = null,
            Func<IAudioSource>? mic = null, TempHome? home = null)
        {
            Home = home ?? new TempHome();
            if (saved is not null)
            {
                var s = new AppSettings();
                saved(s);
                s.Save(Home.Path);
            }
            FakeAgents.WriteCli(Home["staged"], cli);
            if (installed) FakeAgents.WriteCli(Bin, cli);
            if (signedIn) File.WriteAllText(Path.Combine(Bin, "signed-in"), "");
            // Every turn says a word and ends, unless a test scripts its own.
            Directory.CreateDirectory(Bin);
            FakeAgents.Replay(Bin, [Init(), Says("OK."), Done("OK.")]);
            File.WriteAllText(Path.Combine(Bin, "login-delay"), "1");
            foreach (var m in WhisperModels.All) Models.Hold.Add(m.File);
            Host = new AppHost(Home.Path, mic ?? (() => throw new InvalidOperationException("no microphone in this test")), log: _ => { }, loginItems: Login,
                models: new ModelSetting(null, Mirror: "https://mirror.example/models"), http: new HttpClient(Models));
            Setup = Services.Setup.Make(Host, role, here: new LibraryHere { Command = [BuiltEngine()], Folder = Home["Study Stash"] });
            Setup.Again = again;
            var services = new GuidedServices
            {
                Home = Home.Path,
                Find = c => AgentInstall.Find(c, FakeAgents.Which(Bin)),
                Installer = c => new AgentInstall(c, OperatingSystem.IsWindows(), Home["logs/setup-install.log"],
                    FakeAgents.Installer(Home["installer"], c.Id, InstallMode, Home["staged"], Bin), FakeAgents.Which(Bin)) { Elevated = () => false },
                SignIn = (c, exe) => new AgentSignIn(c, exe) { Every = TimeSpan.FromMilliseconds(200) },
                OpenUrl = Opened.Add,
                OpenTerminal = (exe, args) => Terminals.Add(string.Join(' ', [Path.GetFileNameWithoutExtension(exe), .. args])),
                Downloading = () => Host.Downloading?.Fraction,
                AiApps = () => [.. AiApps],
                ConnectAiApp = id =>
                {
                    Connected.Add(id);
                    int at = AiApps.FindIndex(a => a.Id == id);
                    AiApps[at] = AiApps[at] with { Added = true };
                    return new AiAppChange(true, "");
                },
            };
            Guided = new GuidedSetupModel(Setup, services, () => Host.Settings, Host.Save);
            Driver = new GuidedSetup(Guided, Host)
            {
                Installer = null,
                WriteNotes = engine =>
                {
                    Writers.Add(engine);
                    return Task.FromResult<string?>(null);
                },
            };
            Tools = new SetupTools(Driver);
        }

        public async Task<Rig> OpenAsync(bool open = true)
        {
            Door = await SetupMcpHost.StartAsync(Tools);
            Guided.Door = new SetupDoor(Door.Url, Door.Token, Tools.NewTurn);
            if (open) await Guided.OpenAsync();
            return this;
        }

        /// <summary>The setup tools, called the way the CLI calls them.</summary>
        public async Task<(bool Error, string Text)> CallAsync(string tool, Dictionary<string, object?>? args = null)
        {
            await using var mcp = await McpClient.CreateAsync(new HttpClientTransport(new HttpClientTransportOptions
            {
                Endpoint = new Uri(Door.Url), TransportMode = HttpTransportMode.StreamableHttp,
                AdditionalHeaders = new Dictionary<string, string> { ["Authorization"] = "Bearer " + Door.Token },
            }), cancellationToken: TestContext.Current.CancellationToken);
            var r = await mcp.CallToolAsync(tool, args ?? [], cancellationToken: TestContext.Current.CancellationToken);
            return (r.IsError == true, string.Concat(r.Content.OfType<TextContentBlock>().Select(c => c.Text)));
        }

        public ChecklistRow Item(string id) => Guided.Checklist.Single(r => r.Id == id);

        public async ValueTask DisposeAsync()
        {
            Mic.Close();
            Mic.Dispose();
            Guided.Dispose();
            if (Door is not null) await Door.DisposeAsync();
            Host.StopDownload();
            if (Host.LocalLibrary is { } svc) await svc.StopAsync();
            Host.Dispose();
            Home.Dispose();
        }
    }

    [AvaloniaFact]
    public async Task An_ai_thats_here_and_signed_in_starts_picked_and_one_continue_reaches_the_chat()
    {
        // The ChatGPT app is on this computer (its own Codex, signed in with it): no install step, no sign-in step.
        await using var rig = await new Rig(cli: "codex", installed: true, signedIn: true).OpenAsync();
        var g = rig.Guided;
        Assert.Equal(GuidedScreen.PickAi, g.Screen);
        Assert.Equal("codex", g.Picked); // the only one here, so it's picked already

        await g.ContinueCommand.ExecuteAsync(null);

        await Until(() => g.Screen == GuidedScreen.Chat, "the chat, with no stop at the sign-in screen");
        Assert.True(g.AiReady);
        Assert.Empty(rig.Opened); // no sign-in page was opened
    }

    [AvaloniaFact]
    public async Task Setup_offers_to_let_the_chatgpt_app_read_the_library_and_one_press_connects_it()
    {
        var rig0 = new Rig(installed: true, signedIn: true, role: AppRole.Both, saved: s => s.SetupAi = "claude");
        rig0.AiApps.Add(new AiAppState("codex", "ChatGPT", true, false, false, null, null, ""));
        await using var rig = await rig0.OpenAsync();
        var g = rig.Guided;
        await Until(() => g.Screen == GuidedScreen.Chat && !g.Busy, "the chat");
        g.RoleChosen = true;
        Assert.Equal(("AI app", "ChatGPT"), (rig.Item("ai_app").Title, rig.Item("ai_app").Detail));
        Assert.Equal(ChecklistState.Todo, rig.Item("ai_app").State);

        Assert.False((await rig.CallAsync("offer_ai_app")).Error);
        var card = g.Thread.OfType<CardEntry>().Last();
        Assert.Equal("Let ChatGPT read your lectures?", card.Title);
        Assert.Empty(rig.Connected); // showing the card changed nothing

        await card.PressCommand.ExecuteAsync("connect");

        Assert.Equal(["codex"], rig.Connected);
        Assert.Equal(ChecklistState.Done, rig.Item("ai_app").State);
        Assert.Equal("ChatGPT connected", rig.Item("ai_app").Detail);
        Assert.False(card.Open);
        Assert.Contains(g.Thread.OfType<NoteEntry>(), n => n.Text == "ChatGPT can read your lectures");
        Assert.Contains("already connected", (await rig.CallAsync("offer_ai_app")).Text);
    }

    [AvaloniaFact]
    public async Task Cards_change_nothing_until_their_button_is_pressed()
    {
        await using var rig = await new Rig(installed: true, signedIn: true, role: AppRole.Both,
            saved: s => s.SetupAi = "claude").OpenAsync();
        var g = rig.Guided;
        await Until(() => g.Screen == GuidedScreen.Chat && !g.Busy, "the chat");
        g.RoleChosen = true;

        Assert.False((await rig.CallAsync("offer_start_at_login")).Error);
        var login = g.Thread.OfType<CardEntry>().Last();
        Assert.False((await rig.CallAsync("offer_microphone_check")).Error);
        var mic = g.Thread.OfType<CardEntry>().Last();
        Assert.False(login.Open);
        Assert.True(rig.Setup.MicCheckOpen);
        Assert.False((await rig.CallAsync("offer_model_download", new() { ["model_id"] = "large-v3-turbo-q5" })).Error);
        var model = g.Thread.OfType<CardEntry>().Last();
        Assert.False(rig.Setup.MicCheckOpen);

        // Three cards shown, and nothing on the computer changed.
        Assert.Empty(rig.Login.Calls);
        Assert.Equal(0, rig.Models.Asked);
        Assert.Null(rig.Host.Downloading);
        Assert.Equal(ChecklistState.Todo, rig.Item("model").State);
        Assert.Equal(ChecklistState.Todo, rig.Item("start_at_login").State);
        Assert.Equal("Whisper large-v3 turbo (compact)", model.Model?.Name);
        Assert.Equal("574 MB", model.Model?.Size);

        await model.PressCommand.ExecuteAsync("download");
        await Until(() => rig.Models.Asked > 0, "the download to start");
        Assert.Equal("large-v3-turbo-q5", AppSettings.Load(rig.Home.Path).Model);
        Assert.Equal(ChecklistState.Now, rig.Item("model").State);
        Assert.StartsWith("Downloading", rig.Item("model").Detail);
        Assert.Contains(g.Thread.OfType<NoteEntry>(), n => n.Text.StartsWith("Downloading Whisper large-v3 turbo (compact)", StringComparison.Ordinal));

        await login.PressCommand.ExecuteAsync("on");
        Assert.Equal([true], rig.Login.Calls);
        Assert.Equal(ChecklistState.Done, rig.Item("start_at_login").State);

        // The microphone's check is real state too: only the app's own tick (the microphone open for its card) says it
        // heard the student.
        Assert.Equal(ChecklistState.Todo, rig.Item("microphone").State);
        Assert.True(mic.Folded);
        Assert.False((await rig.CallAsync("offer_microphone_check")).Error);
        mic = g.Thread.OfType<CardEntry>().Last();
        Assert.True(rig.Setup.MicCheckOpen);
        rig.Setup.MicAllowed = true;
        rig.Setup.MicHeard = true;
        Assert.Equal(ChecklistState.Done, rig.Item("microphone").State);
        Assert.Contains(g.Thread.OfType<NoteEntry>(), n => n.Text == "Microphone allowed · Study Stash hears you");
        Assert.Equal("Study Stash hears you", mic.Outcome);
        Assert.False(rig.Setup.MicCheckOpen);
    }

    [AvaloniaFact]
    public async Task No_paid_plan_and_gemini_are_answers_like_claude_and_chatgpt_and_open_setup_by_hand()
    {
        await using var rig = await new Rig().OpenAsync();
        var g = rig.Guided;
        // The first screen asks what the student has, in their terms. The tools underneath aren't named on it.
        string words = GuidedSetupModel.PickLede + GuidedSetupModel.ClaudeLine + GuidedSetupModel.CodexLine + GuidedSetupModel.GeminiLine + g.FreeLine;
        foreach (string tool in new[] { "Claude Code", "Codex", "Antigravity" }) Assert.DoesNotContain(tool, words);
        // The free AI that runs on the computer is named once, as what it is: Ollama, and advanced; so is an API key.
        // Without a plan the plain way is no AI at all, and Study Stash still records and transcribes.
        Assert.DoesNotContain("Ollama", words.Replace(g.FreeLine, ""));
        Assert.Equal($"That's fine: Study Stash still records and transcribes. Advanced: a free AI on this {g.Device} (Ollama), or an API key.", g.FreeLine);
        g.Picked = "";
        Assert.False(g.CanContinue);
        // Nothing picked yet, the steps name no AI.
        Assert.Equal(["Pick your AI", "Get your AI ready", "Sign in", "Set up Study Stash"], g.Steps.Select(s => s.Title));
        g.PickCommand.Execute("claude");
        Assert.Equal(["Pick your AI", "Get Claude ready", "Sign in to Claude", "Set up Study Stash"], g.Steps.Select(s => s.Title));

        // Gemini and "I don't have a paid plan" are answers like the other two: nothing to install or sign in to here,
        // and Continue goes on, to setup by hand.
        foreach (string answer in new[] { "gemini", "free" })
        {
            g.PickCommand.Execute(answer);
            Assert.True(g.PickedByHand);
            Assert.True(g.CanContinue);
            Assert.Equal(["Pick your AI", "Set up Study Stash"], g.Steps.Select(s => s.Title));
        }
        await g.ContinueCommand.ExecuteAsync(null);
        Assert.Equal(GuidedScreen.Manual, g.Screen);
        Assert.True(g.PreferOllama);
        Assert.Equal("ollama", AppSettings.Load(rig.Home.Path).SetupAi);
        Assert.Equal("ollama", g.NotesPick);
        Assert.Equal(SetupStep.Intro, rig.Setup.Step); // set up by hand opens on How it works
        Assert.Equal(0, FakeAgents.Turns(rig.Bin));
        Assert.False(File.Exists(FakeAgents.ExePath(rig.Bin, "claude")));
    }

    /// <summary>
    /// Each AI the first screen offers, picked and carried on with, ends with that AI writing the notes. Claude and
    /// ChatGPT through the chat (here a fake CLI): once the library's made, the AI picked writes the notes. Gemini and the
    /// free AI through setup by hand: its notes step opens on that AI (Gemini's key box showing, since it isn't on this
    /// computer yet), and Continue saves it.
    /// </summary>
    [AvaloniaTheory]
    [InlineData("claude")]
    [InlineData("codex")]
    [InlineData("gemini")]
    [InlineData("free")]
    public async Task Each_ai_picked_on_the_first_screen_ends_up_writing_the_notes(string answer)
    {
        bool chat = answer is "claude" or "codex";
        await using var rig = await new Rig(cli: chat ? answer : "claude", installed: chat, signedIn: chat, role: AppRole.Both).OpenAsync();
        var g = rig.Guided;
        g.PickCommand.Execute(answer);
        await g.ContinueCommand.ExecuteAsync(null);

        if (chat)
        {
            await Until(() => g.Screen == GuidedScreen.Chat && !g.Busy, "the chat");
            // The brief tells the AI it's the one the student picked, and so writes the notes.
            Assert.Contains($"call set_notes_writer with \"{answer}\"", File.ReadAllText(Path.Combine(SetupChat.Folder(rig.Home.Path), "brief.md")));
            // The computer card's Set up on a computer whose library is already made: the AI picked writes the notes.
            rig.Setup.LibraryOk = true;
            Assert.False((await rig.CallAsync("offer_computer_setup")).Error);
            await g.Thread.OfType<CardEntry>().Last().PressCommand.ExecuteAsync("setup");
            Assert.Equal([answer], rig.Writers);
            Assert.Equal((ChecklistState.Done, $"Written by {Engines.Name(answer)}"), (rig.Item("notes").State, rig.Item("notes").Detail));
            Assert.Equal(answer, g.NotesPick);
            return;
        }

        Assert.Equal(GuidedScreen.Manual, g.Screen);
        string engine = answer == "free" ? "ollama" : "gemini";
        Assert.Equal(engine, g.NotesPick);
        // The notes step, as setup's window makes it (Shell.EnterSetupStep), over a library where the free AI is
        // ready and Gemini isn't on this computer.
        var lib = new FakeAiLibrary { Overview = AiTestData.MixedOverview() with { Notes = "ollama" } };
        var opened = new List<string>();
        var notes = new AiSetupModel(lib) { Windows = false, Prefer = g.NotesPick, TakesKeys = true, WriteNotes = _ => Task.FromResult(true), OpenUrl = opened.Add };
        await notes.Load();
        if (engine == "gemini")
        {
            Assert.Equal("gemini", notes.KeyFor); // its key box shows, the rest of the list as it was
            Assert.Equal("Your Gemini API key", notes.KeyTitle);
            Assert.NotEqual("gemini", notes.SelectedNotes);
            notes.GetKeyCommand.Execute(null);
            Assert.Equal(["https://aistudio.google.com/apikey"], opened);
            notes.KeyDraft = "AIza-test-key";
            await notes.SaveKeyCommand.ExecuteAsync(null);
            Assert.Equal("", notes.KeyFor);
            Assert.Equal("", notes.KeyDraft);
            Assert.Contains("key:gemini", lib.Calls);
        }
        Assert.Equal(engine, notes.SelectedNotes);
        Assert.True(await notes.SaveAsync());
        Assert.Equal(engine, lib.DefaultsCalls[^1].Notes);
    }

    [AvaloniaFact]
    public async Task Setup_by_hand_gets_claude_ready_with_buttons_and_comes_back_to_the_step_that_asked()
    {
        // Setup by hand's notes step used to give Claude's two commands to paste into Terminal. It borrows guided
        // setup's two screens instead (download and set up, sign in), and then it's back on the step with Claude ready.
        await using var rig = await new Rig(cli: "claude").OpenAsync();
        var g = rig.Guided;
        g.ByHandCommand.Execute(null);
        rig.Setup.Go(rig.Setup.Steps[2].Step); // a few steps in
        var step = rig.Setup.Step;
        string? ready = null;
        g.ReadyForNotes = id => ready = id;

        await g.GetReadyForNotesAsync("claude");
        Assert.Equal(GuidedScreen.Install, g.Screen); // not on this computer yet
        Assert.True(g.ForNotes);
        Assert.Equal("Back", g.ByHandLabel);

        // Back, having done nothing: the step they left, and nothing picked.
        g.ByHandCommand.Execute(null);
        Assert.Equal((GuidedScreen.Manual, step, (string?)null), (g.Screen, rig.Setup.Step, ready));
        Assert.Equal("Set up by hand", g.ByHandLabel);

        // This time through: one button installs it, its own page signs in, and Continue is back on the step.
        await g.GetReadyForNotesAsync("claude");
        await g.InstallCommand.ExecuteAsync(null);
        Assert.True(g.Installed);
        await g.ContinueCommand.ExecuteAsync(null);
        Assert.Equal(GuidedScreen.SignIn, g.Screen);
        File.WriteAllText(Path.Combine(rig.Bin, "signed-in"), "");
        await g.OpenSignInCommand.ExecuteAsync(null);
        await Until(() => g.AiReady, "Claude signed in and its plan checked");
        await g.ContinueCommand.ExecuteAsync(null);
        Assert.Equal((GuidedScreen.Manual, step, "claude"), (g.Screen, rig.Setup.Step, ready));
        Assert.False(g.ForNotes);
        Assert.Empty(rig.Terminals); // nothing was opened to paste a command into
        Assert.Empty(g.Thread); // and no chat was started: this is setup by hand

        // Already here and signed in (the Claude app's own, say): straight back, with nothing to press.
        ready = null;
        await g.GetReadyForNotesAsync("claude");
        Assert.Equal((GuidedScreen.Manual, "claude"), (g.Screen, ready));
    }

    [AvaloniaFact]
    public async Task An_install_offline_or_a_plan_without_the_cli_says_so_with_a_way_on()
    {
        await using var rig = await new Rig(cli: "codex").OpenAsync();
        var g = rig.Guided;
        g.PickCommand.Execute("codex");
        await g.ContinueCommand.ExecuteAsync(null);
        rig.InstallMode = "offline";
        await g.InstallCommand.ExecuteAsync(null);
        Assert.Equal(InstallFailure.Offline, g.InstallFailure);
        Assert.Equal("Study Stash couldn't reach chatgpt.com. Check your internet connection, then try again.", g.InstallProblem);
        Assert.False(g.CanContinue);

        rig.InstallMode = "ok";
        await g.TryAgainCommand.ExecuteAsync(null);
        Assert.True(g.Installed);
        await g.ContinueCommand.ExecuteAsync(null);
        File.WriteAllText(Path.Combine(rig.Bin, "signed-in"), "");
        FakeAgents.Replay(rig.Bin,
            ["""{"type":"thread.started","thread_id":"t-1"}""", """{"type":"turn.failed","error":{"message":"To use Codex with your ChatGPT plan, upgrade to Plus."}}"""], 1);
        await g.OpenSignInCommand.ExecuteAsync(null);
        Assert.Equal(ChatProblem.Plan, g.PlanProblem);
        Assert.Equal("This ChatGPT account is on a plan that other apps can't use", g.PlanProblemTitle);
        Assert.Equal("Study Stash needs ChatGPT Plus or higher. Without one, it still records and transcribes your lectures. Have an API key? Carry on, and add it on the notes step.", g.PlanProblemText);
        Assert.False(g.AiReady);
        g.OpenPlansCommand.Execute("");
        Assert.Equal(["https://chatgpt.com/pricing"], rig.Opened);
        Assert.Equal("Use Claude instead", g.UseOtherLabel);
    }

    [AvaloniaFact]
    public async Task A_turn_that_reaches_for_another_tool_is_stopped_and_says_so()
    {
        await using var rig = new Rig(installed: true, signedIn: true, saved: s => s.SetupAi = "claude");
        FakeAgents.Replay(rig.Bin, [new JsonObject
        {
            ["type"] = "system", ["subtype"] = "init", ["session_id"] = "s-1", ["tools"] = new JsonArray("Bash", "mcp__study_stash_setup__get_setup_status"),
            ["mcp_servers"] = new JsonArray(new JsonObject { ["name"] = "study_stash_setup", ["status"] = "connected" }),
        }.ToJsonString(), Done("I ran it")]);
        await rig.OpenAsync();
        var g = rig.Guided;
        await Until(() => g.HasChatProblem, "the guard");
        Assert.Equal(ChatProblem.Guard, g.ChatProblemKind);
        Assert.Equal("Study Stash stopped Claude: it tried to use a tool it isn't allowed here.", g.ChatProblemText);
    }

    [AvaloniaFact]
    public async Task Not_now_to_start_at_login_leaves_it_off_though_setup_has_its_box_ticked_because_it_already_did()
    {
        await using var rig = new Rig(role: AppRole.Both);
        rig.Setup.StartAtLogin = true; // what setup does when Study Stash already starts at login
        rig.Guided.Skip("start_at_login");
        Assert.False(rig.Setup.StartAtLogin);
        Assert.Equal(ChecklistState.Skipped, rig.Item("start_at_login").State);
    }

    [AvaloniaFact]
    public async Task A_chat_with_no_door_says_so_and_offers_the_way_out_instead_of_sitting_silent()
    {
        await using var rig = new Rig(installed: true, signedIn: true, saved: s => s.SetupAi = "claude");
        await rig.Guided.OpenAsync();
        Assert.Equal(GuidedScreen.Chat, rig.Guided.Screen);
        Assert.Equal(ChatProblem.Tools, rig.Guided.ChatProblemKind);
        Assert.False(rig.Guided.Busy);
    }

    /// <summary>What the AI says shows once, as plain words: an ask_student question it already put its own way isn't
    /// added under it again (Claude and ChatGPT both paraphrase), and ChatGPT's Markdown marks don't show.</summary>
    [Fact]
    public void A_question_the_ai_asked_in_its_own_words_isnt_shown_twice_and_markdown_marks_dont_show()
    {
        const string q = "Does your school use Canvas?";
        Assert.Null(GuidedSetupModel.QuestionToAdd("The download is running. Next, your classes. Does your school use Canvas?", q));
        Assert.Null(GuidedSetupModel.QuestionToAdd("While that downloads, let's add your classes. Is Canvas what your school uses?", q));
        Assert.Equal(q, GuidedSetupModel.QuestionToAdd("", q));
        Assert.Equal(q, GuidedSetupModel.QuestionToAdd("The download is running in the background.", q));
        Assert.Equal("Press Open Study Stash when you're ready.\nYour library: ready",
            GuidedSetupModel.PlainWords("Press **Open Study Stash** when you're ready.\n## Your library: `ready`"));
    }

    // --- the checklist, on its own ------------------------------------------------------------------------------------

    static IReadOnlyList<ChecklistItem> Items(SetupModel m, ChecklistFacts f) => SetupChecklist.From(m, f);

    static ChecklistItem Of(IReadOnlyList<ChecklistItem> items, string id) => items.Single(i => i.Id == id);

    [Fact]
    public void The_checklist_is_this_computers_and_moves_only_on_real_state()
    {
        var m = SetupModel.For(SkinKind.Mac, AppRole.Both);
        var f = new ChecklistFacts { AiReady = true };
        var items = Items(m, f);
        Assert.Equal(["ai", "computer", "microphone", "model", "notes", "classes", "canvas", "start_at_login"], items.Select(i => i.Id));
        Assert.Equal("Where lectures are kept", Of(items, "computer").Title);
        Assert.False(SetupChecklist.ReadyToFinish(items));

        m.LibraryOk = true;
        m.MicAllowed = true;
        items = Items(m, f with { RoleChosen = true, OpenCard = "microphone_check" });
        Assert.Equal(("Just this Mac", ChecklistState.Done), (Of(items, "computer").Title, Of(items, "computer").State));
        Assert.Equal((ChecklistState.Now, "Allowed · say something"), (Of(items, "microphone").State, Of(items, "microphone").Detail));
        m.MicHeard = true;
        items = Items(m, f with { RoleChosen = true, Downloading = 0.42, NotesWriter = "claude", StartsAtLogin = true });
        Assert.Equal(ChecklistState.Done, Of(items, "microphone").State);
        Assert.Equal((ChecklistState.Now, "Downloading · 42%"), (Of(items, "model").State, Of(items, "model").Detail));
        Assert.Equal("Written by Claude", Of(items, "notes").Detail);
        // Downloading counts for finishing; the optional ones don't hold it up.
        Assert.True(SetupChecklist.ReadyToFinish(items));
        Assert.False(SetupChecklist.ReadyToFinish(Items(m, f with { RoleChosen = true, NotesWriter = "claude", StartsAtLogin = true })));
        // Start at login's "Not now" is an answer too.
        Assert.True(SetupChecklist.ReadyToFinish(Items(m, f with { RoleChosen = true, Downloading = 0.1, NotesWriter = "claude", Skipped = new HashSet<string> { "start_at_login" } })));
        Assert.Equal(ChecklistState.Skipped, Of(Items(m, f with { Skipped = new HashSet<string> { "canvas" } }), "canvas").State);

        m.Classes.Add(new SetupClass { Name = "CS 101" });
        m.Classes.Add(new SetupClass { Name = "BIO 110" });
        Assert.Equal((ChecklistState.Done, "2 classes"), (Of(Items(m, f), "classes").State, Of(Items(m, f), "classes").Detail));
    }

    [Fact]
    public void A_laptop_a_library_and_a_windows_pc_each_have_their_own_list()
    {
        var laptop = SetupModel.For(SkinKind.Win, AppRole.Laptop);
        var f = new ChecklistFacts { Ai = "codex", AiReady = true, Windows = true, RoleChosen = true };
        var items = Items(laptop, f);
        Assert.Equal(["ai", "computer", "microphone", "model", "classes", "canvas", "taskbar"], items.Select(i => i.Id));
        Assert.Equal("ChatGPT is ready", Of(items, "ai").Title);
        Assert.Equal(("My laptop for class", "Not connected yet"), (Of(items, "computer").Title, Of(items, "computer").Detail));
        Assert.True(Of(items, "taskbar").Optional);

        var library = SetupModel.For(SkinKind.Mac, AppRole.Library);
        items = Items(library, f with { Windows = false });
        Assert.Equal(["ai", "computer", "library_password", "notes", "classes", "canvas", "start_at_login"], items.Select(i => i.Id));
        Assert.Equal(ChecklistState.Done, Of(items, "computer").State);
        Assert.Equal(ChecklistState.Todo, Of(items, "library_password").State);

        // Run again: what the computer is for is done, and stays.
        var again = SetupModel.For(SkinKind.Mac, AppRole.Laptop);
        again.Again = true;
        again.LibraryOk = true;
        again.LibraryResult = "Connected to Ada's library.";
        Assert.Equal((ChecklistState.Done, "Connected to Ada's library"), (Of(Items(again, new ChecklistFacts()), "computer").State, Of(Items(again, new ChecklistFacts()), "computer").Detail));
    }

    [Fact]
    public void Set_up_by_hand_opens_at_the_first_step_not_done_or_the_one_asked_for()
    {
        var m = SetupModel.For(SkinKind.Mac, AppRole.Both);
        m.LibraryOk = true;
        var items = Items(m, new ChecklistFacts { AiReady = true, RoleChosen = true });
        Assert.Equal(SetupStep.Microphone, SetupChecklist.StepFor(m, items));
        Assert.Equal(SetupStep.Canvas, SetupChecklist.StepFor(m, items, "canvas"));
        Assert.Equal(SetupStep.Welcome, SetupChecklist.StepFor(m, items, "computer"));
    }
}
