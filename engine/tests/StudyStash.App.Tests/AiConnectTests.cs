using System.Text.Json.Nodes;
using Avalonia.Headless.XUnit;
using StudyStash.App.Services;
using StudyStash.App.Services.Kit;
using StudyStash.App.Services.Stores;
using StudyStash.App.ViewModels;
using StudyStash.Core;
using StudyStash.Core.Ai;

namespace StudyStash.App.Tests;

/// <summary>
/// One Connect per AI: it installs what isn't here, adds Syft Notes to every app of that AI in the file each one reads,
/// signs in hidden, makes it the notes AI when none is chosen, and says the one thing left (an app to open again); pressed
/// again it changes nothing that's right. Over a scripted library and a made-up home: never a real app, CLI or account.
/// </summary>
public class AiConnectTests
{
    const string Program = "/Applications/Syft Notes.app/Contents/MacOS/StudyStash";

    static AiOverview Fresh() => new(
        Engines:
        [
            new EngineInfo("claude", "Claude Code", "not_installed"),
            new EngineInfo("codex", "Codex", "not_installed"),
            new EngineInfo("gemini", "Gemini", "not_installed"),
            new EngineInfo("ollama", "Ollama", "not_installed"),
        ],
        Notes: "", Ask: "", Fallback: false, Problems: []);

    /// <summary>A Mac with the Claude app (open, say) and nothing else of Claude's yet, and the server check stood in for.</summary>
    static ClaudeSetup Apps(TempHome home, List<string> probed, bool running = false, bool windows = false, Func<string, string?>? find = null,
        Func<bool>? open = null, Func<string, Task<bool>>? quit = null, Func<string, Task<bool>>? reopen = null) => new()
    {
        Program = Program, Home = home["syft"], UserHome = home["user"], Windows = windows, Mac = !windows,
        Find = find ?? (_ => null), AppInstalled = name => name == "Claude", Running = id => id == "claude-desktop" && (open?.Invoke() ?? running),
        QuitApp = quit, OpenApp = reopen,
        DesktopConfig = home["user/Claude/claude_desktop_config.json"], ClaudeCodeConfig = home["user/.claude.json"],
        CodexConfig = home["user/.codex/config.toml"], GeminiConfig = home["user/.gemini/settings.json"],
        AntigravityConfig = home["user/.gemini/config/mcp_config.json"],
        Probe = (command, args) =>
        {
            probed.Add(command + " " + string.Join(' ', args));
            return Task.FromResult(new McpProbeResult(true, ["search_notes", "get_lecture"]));
        },
    };

    /// <summary>An installer that "installs" Claude Code: the library sees it installed and signed out from then on.</summary>
    static Func<string, Action<string>, Task<InstallOutcome>> Installer(FakeAiLibrary lib, List<string> installed, Action? also = null) => (id, phase) =>
    {
        installed.Add(id);
        phase("Downloading Claude Code…");
        lib.Overview = lib.Overview! with { Engines = [.. lib.Overview.Engines.Select(e => e.Id == id ? e with { Installed = true, State = "not_signed_in" } : e)] };
        also?.Invoke();
        return Task.FromResult(new InstallOutcome(true, InstallFailure.None, "Claude Code 2.1.300 is installed.", AgentFound.None));
    };

    [Fact]
    public async Task One_connect_installs_connects_every_claude_app_signs_in_and_writes_the_notes_and_pressed_again_changes_nothing()
    {
        using var home = new TempHome();
        var lib = new FakeAiLibrary { Overview = Fresh() };
        var probed = new List<string>();
        bool cliHere = false;
        var apps = Apps(home, probed, find: c => cliHere && c == "claude" ? "/u/.local/bin/claude" : null);
        var installed = new List<string>();
        var connect = new AiConnect(lib) { Apps = apps, Install = Installer(lib, installed, () => cliHere = true), Every = TimeSpan.FromMilliseconds(1) };
        var steps = new List<string>();

        var done = await connect.ConnectAsync("claude", p => steps.Add(p.Words), TestContext.Current.CancellationToken);

        Assert.True(done.Ok, done.Words);
        Assert.Equal(["claude"], installed);
        Assert.Equal(["Installing Claude…", "Connecting Claude app…", "Connecting Claude Code…", "Checking Claude app and Claude Code…",
            "Signing in to Claude… finish in your browser.", "Done"], steps);
        Assert.Equal(["Claude app", "Claude Code"], done.Apps);
        Assert.Equal("", done.Next); // Claude isn't open: it reads Syft Notes when it next starts
        // Both apps, in their own files, under the new name, started the way the check started it.
        var desktop = JsonNode.Parse(File.ReadAllText(apps.DesktopConfig))!["mcpServers"]!["syft-notes"]!;
        Assert.Equal(Program, desktop["command"]!.GetValue<string>());
        Assert.NotNull(JsonNode.Parse(File.ReadAllText(apps.ClaudeCodeConfig))!["mcpServers"]!["syft-notes"]);
        Assert.Equal([$"{Program} --home {home["syft"]} mcp --client claude-desktop"], probed);
        // Nobody wrote the notes: Claude does now, and answers questions.
        Assert.Contains(("claude", "claude", (bool?)null, (string?)null), lib.DefaultsCalls);
        Assert.Contains("sign-in:claude", lib.Calls);

        // Again: nothing to install, nothing to sign in to, no file written (no backup made), no new choice.
        lib.Calls.Clear();
        string before = File.ReadAllText(apps.DesktopConfig);
        var again = await connect.ConnectAsync("claude", ct: TestContext.Current.CancellationToken);
        Assert.True(again.Ok);
        Assert.Equal(["claude"], installed);
        Assert.DoesNotContain(lib.Calls, c => c.StartsWith("sign-in", StringComparison.Ordinal) || c == "defaults");
        Assert.Equal(before, File.ReadAllText(apps.DesktopConfig));
        Assert.False(File.Exists(apps.DesktopConfig + ".syft-notes-backup"));
    }

    [Theory]
    [InlineData(false, "Quit Claude fully and open it again.")]
    [InlineData(true, "Quit Claude fully and open it again. Quit it from the tray icon by the clock.")]
    public async Task An_open_claude_app_is_the_one_thing_left_said_once_and_on_windows_by_the_tray(bool windows, string next)
    {
        using var home = new TempHome();
        var lib = new FakeAiLibrary { Overview = Fresh() with { Engines = [new EngineInfo("claude", "Claude Code", "ready") { Installed = true }] } };
        var connect = new AiConnect(lib) { Apps = Apps(home, [], running: true, windows: windows), Windows = windows };

        var done = await connect.ConnectAsync("claude", ct: TestContext.Current.CancellationToken);

        Assert.True(done.Ok, done.Words);
        Assert.Equal(next, done.Next);
        Assert.DoesNotContain(lib.Calls, c => c.StartsWith("sign-in", StringComparison.Ordinal)); // already signed in
    }

    [Fact]
    public async Task An_open_claude_app_is_closed_before_anything_is_written_then_connected_and_opened_again()
    {
        // Claude keeps its settings file in memory and writes it back whole: an entry added while it's open can be lost.
        using var home = new TempHome();
        var lib = new FakeAiLibrary { Overview = Fresh() with { Engines = [new EngineInfo("claude", "Claude Code", "ready") { Installed = true }] } };
        bool open = true;
        var did = new List<string>();
        ClaudeSetup apps = null!;
        apps = Apps(home, [], open: () => open,
            quit: id =>
            {
                did.Add($"close {id}, written: {File.Exists(apps.DesktopConfig)}");
                open = false;
                return Task.FromResult(true);
            },
            reopen: id =>
            {
                did.Add($"open {id}, written: {apps.State(id).Good}");
                open = true;
                return Task.FromResult(true);
            });
        var connect = new AiConnect(lib) { Apps = apps };

        // Pressed while it's open: nothing is written; the card asks to close it.
        var first = await connect.ConnectAsync("claude", ct: TestContext.Current.CancellationToken);
        Assert.Equal((false, "Close Claude first", "Claude"), (first.Ok, first.Words, first.CloseFirst));
        Assert.False(File.Exists(apps.DesktopConfig));
        Assert.Empty(did);

        // Close Claude: closed, connected, opened again, and nothing left for the student to do.
        var steps = new List<string>();
        var done = await connect.ConnectAsync("claude", p => steps.Add(p.Words), TestContext.Current.CancellationToken, close: true);
        Assert.True(done.Ok, done.Words);
        Assert.Equal(["close claude-desktop, written: False", "open claude-desktop, written: True"], did);
        Assert.Equal(["Closing Claude…", "Connecting Claude app…", "Checking Claude app…", "Opening Claude…", "Done"], steps);
        Assert.Equal("", done.Next); // no "quit and open it again": Syft Notes just did
        Assert.False(apps.State("claude-desktop").NeedsReopen);

        // Not open: Connect is as it always was, with nothing closed.
        open = false;
        did.Clear();
        Assert.True((await connect.ConnectAsync("claude", ct: TestContext.Current.CancellationToken)).Ok);
        Assert.Empty(did);
    }

    [Theory]
    [InlineData(false, "Couldn't close Claude. Quit it from its menu, then press Connect.")]
    [InlineData(true, "Couldn't close Claude. Quit it from the tray icon by the clock, then press Connect.")]
    public async Task An_app_that_wont_close_says_how_to_quit_it_and_nothing_is_written(bool windows, string words)
    {
        using var home = new TempHome();
        var lib = new FakeAiLibrary { Overview = Fresh() with { Engines = [new EngineInfo("claude", "Claude Code", "ready") { Installed = true }] } };
        bool opened = false;
        var apps = Apps(home, [], running: true, windows: windows, quit: _ => Task.FromResult(false), reopen: _ => Task.FromResult(opened = true));
        var connect = new AiConnect(lib) { Apps = apps, Windows = windows };

        var done = await connect.ConnectAsync("claude", ct: TestContext.Current.CancellationToken, close: true);

        Assert.Equal((false, words, true), (done.Ok, done.Words, done.CouldntClose));
        Assert.False(File.Exists(apps.DesktopConfig));
        Assert.False(opened);
    }

    [Fact]
    public async Task A_sign_in_that_gives_up_says_so_and_the_apps_are_connected_all_the_same()
    {
        using var home = new TempHome();
        var lib = new FakeAiLibrary { Overview = Fresh() with { Engines = [new EngineInfo("claude", "Claude Code", "not_signed_in") { Installed = true }] } };
        lib.OnSignIn = id =>
        {
            lib.Overview = lib.Overview! with { Engines = [new EngineInfo("claude", "Claude Code", "not_signed_in") { Installed = true, SignIn = SignInPhase.Waiting }] };
            return new AiSaid("Finish signing in to Claude.", lib.Overview);
        };
        int asked = 0;
        lib.OnEngines = () =>
        {
            // The student never finishes: the third look finds it gave up.
            if (++asked >= 3) lib.Overview = lib.Overview! with { Engines = [new EngineInfo("claude", "Claude Code", "not_signed_in") { Installed = true, SignIn = SignInPhase.GaveUp }] };
            return lib.Overview;
        };
        var apps = Apps(home, []);
        var connect = new AiConnect(lib) { Apps = apps, Every = TimeSpan.FromMilliseconds(1) };

        var done = await connect.ConnectAsync("claude", ct: TestContext.Current.CancellationToken);

        Assert.False(done.Ok);
        Assert.Equal(SignInPhase.GaveUp, done.SignIn);
        Assert.Equal("Signing in to Claude stopped before you'd finished. Try again.", done.Words);
        Assert.Equal(["Claude app"], done.Apps);
        Assert.True(apps.State("claude-desktop").Good);
        Assert.Empty(lib.DefaultsCalls);
    }

    [Fact]
    public async Task An_install_that_stops_says_why_and_goes_no_further()
    {
        using var home = new TempHome();
        var lib = new FakeAiLibrary { Overview = Fresh() };
        var apps = Apps(home, []);
        var connect = new AiConnect(lib)
        {
            Apps = apps,
            Install = (_, _) => Task.FromResult(new InstallOutcome(false, InstallFailure.Offline, "Syft Notes couldn't reach claude.ai. Check your internet connection, then try again.", AgentFound.None)),
        };

        var done = await connect.ConnectAsync("claude", ct: TestContext.Current.CancellationToken);

        Assert.False(done.Ok);
        Assert.Equal(InstallFailure.Offline, done.Install);
        Assert.StartsWith("Syft Notes couldn't reach claude.ai", done.Words);
        Assert.DoesNotContain(lib.Calls, c => c.StartsWith("sign-in", StringComparison.Ordinal));
        Assert.False(File.Exists(apps.DesktopConfig));
    }

    [AvaloniaTheory]
    [InlineData("gemini", "not_signed_in")] // the library still has an earlier sign-in that ran out
    [InlineData("claude", "unchecked")]
    [InlineData("codex", "unchecked")]
    public async Task An_AI_signed_in_during_setup_is_connected_in_settings_with_no_second_connect(string id, string before)
    {
        // Setup signs in with the AI's own sign-in, which the library never sees, then runs Settings' Connect through the
        // app's one AI store, in the background: the library checks it (no second sign-in), and Settings → Your AI, open
        // all along, follows.
        using var home = new TempHome();
        var lib = new FakeAiLibrary { Overview = Fresh() with { Engines = [new EngineInfo(id, AiConnect.Brand(id), before) { Installed = true }] } };
        using var store = new AiStatusStore(lib);
        using var settings = new AiEnginesModel(store) { Here = true };
        await settings.Load();
        Assert.NotEqual("Connected", settings.Engines.Single(r => r.Id == id).StateWords);
        var connect = new AiConnect(lib) { Apps = Apps(home, []), WaitForSignIn = false, SignedInHere = true };

        ConnectOutcome? done = null;
        UiTasks.Run(async () => done = await store.ConnectAsync(connect, id), "setup: connect");

        Assert.True(await Poll.UntilAsync(() => done is not null, TimeSpan.FromMilliseconds(5), TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
        Assert.True(done!.Ok, done.Words);
        Assert.Equal(AiEngineState.Ready, store.For(id)!.State);
        Assert.Contains($"check:{id}", lib.Calls);
        Assert.DoesNotContain($"sign-in:{id}", lib.Calls);
        Assert.Equal("Connected", settings.Engines.Single(r => r.Id == id).StateWords); // no Connect pressed there

        // The library still can't use it after its check: said, and no browser page opened behind the student's back.
        var signedOut = lib.Overview! with { Engines = [new EngineInfo(id, AiConnect.Brand(id), "not_signed_in") { Installed = true }] };
        lib.Overview = signedOut;
        lib.OnCheck = (_, _) => new AiSaid("Not signed in.", signedOut);
        Assert.False((await store.ConnectAsync(connect, id, ct: TestContext.Current.CancellationToken)).Ok);
        Assert.DoesNotContain($"sign-in:{id}", lib.Calls);
    }

    [AvaloniaFact]
    public async Task Your_ais_cards_go_from_not_set_up_to_connected_naming_the_apps_and_a_wrong_copy_needs_fixing()
    {
        using var home = new TempHome();
        var lib = new FakeAiLibrary { Overview = Fresh() };
        bool cliHere = false;
        var apps = Apps(home, [], find: c => cliHere && c == "claude" ? "/u/.local/bin/claude" : null);
        var model = new AiEnginesModel(lib) { Here = true, AiApps = apps, SignInEvery = TimeSpan.FromHours(1), Install = Installer(lib, [], () => cliHere = true) };
        await model.Load();
        AiEngineRow Card(string id) => model.Engines.Single(r => r.Id == id);

        Assert.Equal(["claude", "codex", "gemini", "ollama"], model.Engines.Select(r => r.Id));
        Assert.Equal(("Not set up", "Connect", true), (Card("claude").StateWords, Card("claude").ActionWords, Card("claude").IsPrimary));
        Assert.Equal("For a Claude Pro or Max plan. Connect sets it up and signs you in.", Card("claude").About);

        await Card("claude").ActCommand.ExecuteAsync(null);

        var claude = Card("claude");
        Assert.Equal(("Connected", true, "Options"), (claude.StateWords, claude.Good, claude.ActionWords));
        Assert.Equal("Writes your notes · Claude app · Claude Code", claude.About);
        Assert.Equal("Claude is connected.", model.Say);

        // An earlier Syft Notes' entry under its old name, in the Claude app: the card says what's wrong, and Fix puts it right.
        File.WriteAllText(apps.DesktopConfig, new JsonObject { ["mcpServers"] = new JsonObject { ["study-stash"] = new JsonObject { ["command"] = Program, ["args"] = new JsonArray("mcp") } } }.ToJsonString());
        await model.Load();
        Assert.Equal(("Needs attention", "Fix", "Claude app isn't reading your lectures yet. Fix connects it."), (Card("claude").StateWords, Card("claude").ActionWords, Card("claude").About));

        await Card("claude").ActCommand.ExecuteAsync(null);

        Assert.Equal("Connected", Card("claude").StateWords);
        Assert.DoesNotContain("study-stash", File.ReadAllText(apps.DesktopConfig));
    }
}
