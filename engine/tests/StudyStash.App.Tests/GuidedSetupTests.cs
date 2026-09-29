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
    public async Task A_first_run_picks_installs_signs_in_and_the_ai_takes_it_from_there()
    {
        await using var rig = await new Rig().OpenAsync();
        var g = rig.Guided;
        FakeAgents.Replay(rig.Bin, ["""{"type":"system","subtype":"init","session_id":"p-1","tools":[],"mcp_servers":[]}""", Says("ready"), Done("ready", "p-1")], 1);
        FakeAgents.Replay(rig.Bin, [Init(), Says("Hi! This takes about 5 minutes. Just this Mac, or two computers?"), Done("Hi!")], 2);

        // Pick your AI: nothing picked, nothing found, nothing downloaded.
        Assert.Equal(GuidedScreen.PickAi, g.Screen);
        Assert.False(g.ClaudeHere);
        Assert.False(g.CanContinue);
        g.PickCommand.Execute("claude");
        Assert.True(g.CanContinue);
        await g.ContinueCommand.ExecuteAsync(null);
        Assert.Equal("claude", AppSettings.Load(rig.Home.Path).SetupAi);

        // Install: only on the press.
        Assert.Equal(GuidedScreen.Install, g.Screen);
        Assert.Equal("Install Claude Code", g.InstallTitle);
        Assert.False(File.Exists(FakeAgents.ExePath(rig.Bin, "claude")));
        await g.InstallCommand.ExecuteAsync(null);
        Assert.True(g.Installed, g.InstallProblem);
        Assert.Equal("Claude Code 2.1.260 is installed.", g.InstalledWords);
        await g.ContinueCommand.ExecuteAsync(null);

        // Sign in: the CLI's own sign-in, noticed when its status says so, then the plan check.
        Assert.Equal(GuidedScreen.SignIn, g.Screen);
        Assert.False(g.SignedIn);
        await g.OpenSignInCommand.ExecuteAsync(null);
        Assert.True(g.SignedIn);
        Assert.True(g.AiReady, $"{g.PlanProblem}: {g.PlanProblemTitle} {g.PlanProblemText} {g.SignInProblem}");
        Assert.Contains("--tools", FakeAgents.Argv(rig.Bin, 1));
        Assert.DoesNotContain("--mcp-config", FakeAgents.Argv(rig.Bin, 1));
        await g.ContinueCommand.ExecuteAsync(null);

        // The chat: its first turn, with the setup tools and their token in its environment only.
        Assert.Equal(GuidedScreen.Chat, g.Screen);
        await Until(() => !g.Busy && g.Thread.OfType<AiEntry>().Any(), "the first turn");
        Assert.Equal("Hi! This takes about 5 minutes. Just this Mac, or two computers?", g.Thread.OfType<AiEntry>().First().Text);
        Assert.Contains("[Study Stash] Setup was opened.", FakeAgents.Input(rig.Bin, 2));
        Assert.Contains("--mcp-config", FakeAgents.Argv(rig.Bin, 2));
        Assert.Contains(SetupChat.TokenVar, FakeAgents.EnvNames(rig.Bin, 2));
        Assert.Equal(new SetupChatSaved("claude", "s-1"), AppSettings.Load(rig.Home.Path).SetupChat);
        Assert.Equal(ChecklistState.Done, rig.Item("ai").State);
        Assert.Equal("How you'll use it", rig.Item("computer").Title);

        // The AI shows the computer card: nothing happens until the student presses Set up.
        var (error, said) = await rig.CallAsync("offer_computer_setup");
        Assert.False(error, said);
        var card = Assert.IsType<CardEntry>(g.Thread[^1]);
        Assert.True(card.IsComputer);
        Assert.Equal(ChecklistState.Now, rig.Item("computer").State);
        Assert.Null(LibraryHere.Existing(rig.Home.Path));
        Assert.Equal(LibraryState.NotSetUp, rig.Host.Library);

        int turns = FakeAgents.Turns(rig.Bin);
        await card.PressCommand.ExecuteAsync("setup");
        Assert.True(rig.Setup.LibraryOk, rig.Setup.LibraryResult);
        Assert.False(card.Open);
        Assert.Equal(ChecklistState.Done, rig.Item("computer").State);
        Assert.Equal($"Just this {rig.Setup.DeviceWord}", rig.Item("computer").Title);
        Assert.Equal(["claude"], rig.Writers);
        Assert.Equal(ChecklistState.Done, rig.Item("notes").State);
        Assert.Contains(g.Thread.OfType<NoteEntry>(), n => n.Text.StartsWith("Your library is ready", StringComparison.Ordinal));
        await Until(() => FakeAgents.Turns(rig.Bin) > turns && !g.Busy, "the note's turn");
        Assert.Contains("[Study Stash] The student chose just this", FakeAgents.Input(rig.Bin, turns + 1));
        Assert.Contains("--resume", FakeAgents.Argv(rig.Bin, turns + 1));

        // A class the student named goes straight into the library, and the checklist shows it.
        (error, said) = await rig.CallAsync("add_class", new() { ["name"] = "BIO 110", ["about"] = "Cells, genetics and evolution" });
        Assert.False(error, said);
        Assert.Contains("BIO 110", rig.Host.Classes().Select(c => c.Name).Concat(rig.Setup.Classes.Select(c => c.Name)));
        Assert.Equal("1 class", rig.Item("classes").Detail);

        // The status the AI reads is the checklist's.
        (_, said) = await rig.CallAsync("get_setup_status");
        Assert.Contains("- computer · Just this", said);
        Assert.Contains("- classes · Classes: done (optional) · 1 class", said);
        Assert.Contains("ready_to_finish: no", said);
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
        var login = (CardEntry)g.Thread[^1];
        Assert.False((await rig.CallAsync("offer_microphone_check")).Error);
        var mic = (CardEntry)g.Thread[^1];
        Assert.False(login.Open);
        Assert.True(rig.Setup.MicCheckOpen);
        Assert.False((await rig.CallAsync("offer_model_download", new() { ["model_id"] = "large-v3-turbo-q5" })).Error);
        var model = (CardEntry)g.Thread[^1];
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
        mic = (CardEntry)g.Thread[^1];
        Assert.True(rig.Setup.MicCheckOpen);
        rig.Setup.MicAllowed = true;
        rig.Setup.MicHeard = true;
        Assert.Equal(ChecklistState.Done, rig.Item("microphone").State);
        Assert.Contains(g.Thread.OfType<NoteEntry>(), n => n.Text == "Microphone allowed · Study Stash hears you");
        Assert.Equal("Study Stash hears you", mic.Outcome);
        Assert.False(rig.Setup.MicCheckOpen);
    }

    [AvaloniaFact]
    public async Task Notes_and_words_typed_during_a_turn_go_together_with_the_next()
    {
        await using var rig = new Rig(installed: true, signedIn: true, saved: s => s.SetupAi = "claude");
        File.WriteAllText(Path.Combine(rig.Bin, "sleep"), "2");
        await rig.OpenAsync();
        var g = rig.Guided;
        await Until(() => g.Busy && FakeAgents.Turns(rig.Bin) == 1, "the first turn to start");

        g.Draft = "Just this Mac, please";
        await g.SendCommand.ExecuteAsync(null);
        g.Note("Microphone allowed · Study Stash hears you", "The microphone is allowed and Study Stash hears the student.");
        var typed = g.Thread.OfType<StudentEntry>().Single();
        Assert.True(typed.Queued);
        Assert.False(g.CanType);
        Assert.Equal(1, FakeAgents.Turns(rig.Bin));

        File.Delete(Path.Combine(rig.Bin, "sleep"));
        await Until(() => FakeAgents.Turns(rig.Bin) == 2 && !g.Busy, "the next turn");
        Assert.False(typed.Queued);
        string next = FakeAgents.Input(rig.Bin, 2);
        Assert.Contains("Just this Mac, please", next);
        Assert.Contains("[Study Stash] The microphone is allowed and Study Stash hears the student.", next);
        Assert.Equal(2, FakeAgents.Turns(rig.Bin));
    }

    [AvaloniaFact]
    public async Task Quick_replies_are_sent_as_the_students_answer()
    {
        await using var rig = await new Rig(installed: true, signedIn: true, saved: s => s.SetupAi = "claude").OpenAsync();
        var g = rig.Guided;
        await Until(() => g.Screen == GuidedScreen.Chat && !g.Busy, "the chat");
        Assert.False((await rig.CallAsync("ask_student", new() { ["question"] = "Does your school use Canvas?", ["choices"] = new[] { "Yes", "No" } })).Error);
        Assert.Equal(["Yes", "No"], g.QuickReplies);
        int turns = FakeAgents.Turns(rig.Bin);
        await g.ReplyCommand.ExecuteAsync("Yes");
        Assert.Empty(g.QuickReplies);
        Assert.Equal("Yes", g.Thread.OfType<StudentEntry>().Last().Text);
        Assert.Equal("Yes", FakeAgents.Input(rig.Bin, turns + 1).Trim());

        // A checklist line clicked asks the AI to go there.
        await g.ClickItemCommand.ExecuteAsync(rig.Item("canvas"));
        Assert.Contains("[Study Stash] The student clicked Canvas in the checklist.", FakeAgents.Input(rig.Bin, turns + 2));
    }

    [AvaloniaFact]
    public async Task Run_again_goes_straight_to_the_chat_and_keeps_what_the_computer_is_for()
    {
        await using var rig = await new Rig(installed: true, signedIn: true, role: AppRole.Laptop, again: true,
            saved: s =>
            {
                s.SetupDone = true;
                s.Role = AppRole.Laptop;
                s.SetupAi = "claude";
            }).OpenAsync();
        var g = rig.Guided;
        await Until(() => g.Screen == GuidedScreen.Chat && !g.Busy, "the chat");
        Assert.Equal("Set up Study Stash again", g.HeaderTitle);
        Assert.Contains("[Study Stash] Setup was opened again from Settings.", FakeAgents.Input(rig.Bin, 1));
        Assert.Contains("This is setup run again.", File.ReadAllText(Path.Combine(SetupChat.Folder(rig.Home.Path), "brief.md")));
        Assert.Equal("This is my laptop", rig.Item("computer").Title);

        var (error, said) = await rig.CallAsync("offer_computer_setup", new() { ["suggested"] = "library" });
        Assert.True(error);
        Assert.Contains("Settings → Connection", said);
        Assert.True(rig.Setup.IsLaptop);
    }

    [AvaloniaFact]
    public async Task No_subscription_opens_setup_by_hand_with_the_free_model_for_notes()
    {
        await using var rig = await new Rig().OpenAsync();
        var g = rig.Guided;
        g.UseOllamaCommand.Execute(null);
        Assert.Equal(GuidedScreen.Manual, g.Screen);
        Assert.True(g.PreferOllama);
        Assert.Equal("ollama", AppSettings.Load(rig.Home.Path).SetupAi);
        Assert.Equal(SetupStep.Welcome, rig.Setup.Step);
        Assert.Equal(0, FakeAgents.Turns(rig.Bin));
        Assert.False(File.Exists(FakeAgents.ExePath(rig.Bin, "claude")));
    }

    [AvaloniaFact]
    public async Task Set_up_by_hand_from_the_chat_opens_the_first_step_not_done_and_comes_back()
    {
        await using var rig = await new Rig(installed: true, signedIn: true, role: AppRole.Laptop, saved: s => s.SetupAi = "claude").OpenAsync();
        var g = rig.Guided;
        await Until(() => g.Screen == GuidedScreen.Chat && !g.Busy, "the chat");
        g.RoleChosen = true;
        Assert.False((await rig.CallAsync("open_manual_setup", new() { ["step"] = "microphone" })).Error);
        Assert.Equal(GuidedScreen.Manual, g.Screen);
        Assert.Equal(SetupStep.Microphone, rig.Setup.Step);

        int turns = FakeAgents.Turns(rig.Bin);
        g.BackToChatCommand.Execute(null);
        Assert.Equal(GuidedScreen.Chat, g.Screen);
        await Until(() => FakeAgents.Turns(rig.Bin) > turns && !g.Busy, "the chat to hear");
        Assert.Contains("[Study Stash] The student did some steps by hand.", FakeAgents.Input(rig.Bin, turns + 1));
    }

    [AvaloniaFact]
    public async Task Setup_by_hand_from_settings_can_still_turn_to_an_ai()
    {
        await using var rig = await new Rig(installed: true).OpenAsync(open: false);
        var g = rig.Guided;
        g.Screen = GuidedScreen.Manual;
        g.BackToChatCommand.Execute(null);
        await Until(() => g.ClaudeHere, "the CLIs to be looked for");
        Assert.Equal(GuidedScreen.PickAi, g.Screen);
        Assert.Equal(0, FakeAgents.Turns(rig.Bin));
    }

    [AvaloniaFact]
    public async Task A_window_closed_part_way_picks_the_conversation_up_again()
    {
        await using var rig = await new Rig(installed: true, signedIn: true,
            saved: s =>
            {
                s.SetupAi = "claude";
                s.SetupChat = new SetupChatSaved("claude", "s-earlier");
            }).OpenAsync();
        var g = rig.Guided;
        await Until(() => g.Screen == GuidedScreen.Chat && !g.Busy && FakeAgents.Turns(rig.Bin) == 1, "the chat");
        string[] argv = FakeAgents.Argv(rig.Bin, 1);
        Assert.Equal("s-earlier", argv[Array.IndexOf(argv, "--resume") + 1]);
        Assert.Contains("[Study Stash] Setup was reopened.", FakeAgents.Input(rig.Bin, 1));
        // Nothing was checked again that didn't need to be: no install, no plan check.
        Assert.Equal(1, FakeAgents.Turns(rig.Bin));
    }

    [AvaloniaFact]
    public async Task Already_installed_and_signed_in_skips_to_the_plan_check()
    {
        await using var rig = await new Rig(installed: true, signedIn: true).OpenAsync();
        var g = rig.Guided;
        Assert.True(g.ClaudeHere);
        // The device is the look's word: "Mac" in the Mac look, "PC" in Windows'.
        Assert.Equal($"Already on this {rig.Setup.DeviceWord}", g.AlreadyHere);
        g.PickCommand.Execute("claude");
        FakeAgents.Replay(rig.Bin, [Done("ready", "p-1")], 1);
        await g.ContinueCommand.ExecuteAsync(null);
        Assert.Equal(GuidedScreen.SignIn, g.Screen);
        Assert.True(g.AiReady, $"{g.PlanProblem}: {g.PlanProblemTitle} {g.PlanProblemText} {g.SignInProblem}");
        Assert.False(File.Exists(rig.Home["logs/setup-install.log"]));
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
        Assert.Equal("Your ChatGPT plan doesn't include Codex in the app", g.PlanProblemTitle);
        Assert.Equal("It needs Plus or higher.", g.PlanProblemText);
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
    public async Task Signed_out_mid_chat_goes_back_to_sign_in()
    {
        await using var rig = new Rig(installed: true, signedIn: true, saved: s => s.SetupAi = "claude");
        FakeAgents.Replay(rig.Bin, [Init(), Done("Invalid API key · Please run /login", error: true)]);
        await rig.OpenAsync();
        var g = rig.Guided;
        await Until(() => g.Screen == GuidedScreen.SignIn, "sign in again");
        Assert.Equal("That sign-in didn't work. Try again.", g.SignInProblem);
    }

    [AvaloniaFact]
    public async Task A_usage_limit_says_until_when_and_what_is_done_stays()
    {
        await using var rig = new Rig(installed: true, signedIn: true, saved: s => s.SetupAi = "claude");
        FakeAgents.Replay(rig.Bin, [Init(), Done("Claude AI usage limit reached. Your limit resets at 3pm.", error: true)]);
        await rig.OpenAsync();
        var g = rig.Guided;
        await Until(() => g.HasChatProblem, "the limit");
        Assert.Equal("Claude is at its usage limit until 3 pm. You can finish by hand; what's done stays done.", g.ChatProblemText);
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
        Assert.Equal("How you'll use it", Of(items, "computer").Title);
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
        Assert.Equal(("This is my laptop", "Not connected yet"), (Of(items, "computer").Title, Of(items, "computer").Detail));
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
