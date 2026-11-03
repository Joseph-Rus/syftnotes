using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using StudyStash.App.Platform;
using StudyStash.App.Services;
using StudyStash.App.Views;
using StudyStash.Core;
using StudyStash.Core.Ai;

namespace StudyStash.App.Pictures;

/// <summary>
/// Films of Settings → Your AI's one Connect, drawn off screen into STUDYSTASH_SHOTS/&lt;film&gt;/ the way SignInFilms are:
/// Claude not set up, the Claude app already on the computer (and open), and one press that installs Claude (the stand-in's
/// installer), adds Syft Notes to the Claude app and Claude Code in the files they read (a made-up home: on the Windows
/// look, the Store build's layout after it moved its data, with an earlier Syft Notes' entry in the old folder), checks
/// the server they'll start answers (the app as the tests built it, run as their settings say), signs in hidden (a
/// stand-in that signs in by itself after 5 s), and makes Claude the notes AI; then the one thing left, said once: quit
/// Claude and open it again. Only with STUDYSTASH_FILM=1, on a Mac; never a real app, sign-in, account or browser.
/// </summary>
public sealed class ConnectFilms
{
    const string Skip = "A film, not a test: STUDYSTASH_FILM=1, on a Mac.";

    static void Look(SkinKind skin)
    {
        Skin.UseTheme(ColourThemes.Default);
        ((App)Application.Current!).UseSkin(skin);
    }

    /// <summary>The AI apps in the film's own home: the Claude app is there and open; on Windows its settings are where the
    /// Store build keeps them now, with an earlier Syft Notes' entry left in its old folder.</summary>
    sealed class Apps
    {
        public bool ClaudeOpen = true;
        public readonly ClaudeSetup Setup;
        public readonly string Home;

        public Apps(StandIns pc, AppHost host, bool windows)
        {
            Home = host.Home;
            string user = pc["user"], local = Path.Combine(user, "AppData", "Local");
            if (windows)
            {
                string old = Path.Combine(local, "Packages", "Claude_pzs8sxrjxfjjc", "LocalCache", "Roaming", "Claude");
                Directory.CreateDirectory(old);
                File.WriteAllText(Path.Combine(old, ".user-data-left.claude_pzs8sxrjxfjjc"), "");
                File.WriteAllText(Path.Combine(old, "claude_desktop_config.json"),
                    new JsonObject { ["mcpServers"] = new JsonObject { ["study-stash"] = new JsonObject { ["command"] = "StudyStash.exe", ["args"] = new JsonArray("mcp") } } }.ToJsonString());
                string moved = Path.Combine(local, "Claude-Data", "claude_pzs8sxrjxfjjc");
                Directory.CreateDirectory(moved);
                File.WriteAllText(Path.Combine(moved, "claude_desktop_config.json"), """{"preferences": {"menuBarEnabled": true}}""");
            }
            else
            {
                string support = Path.Combine(user, "Library", "Application Support", "Claude");
                Directory.CreateDirectory(support);
                File.WriteAllText(Path.Combine(support, "claude_desktop_config.json"), """{"preferences": {"menuBarEnabled": true}}""");
            }
            Setup = new ClaudeSetup
            {
                Program = Path.Combine(AppContext.BaseDirectory, "StudyStash"), Home = host.Home, UserHome = user, Windows = windows, Mac = !windows,
                LocalAppData = local, AppData = Path.Combine(user, "AppData", "Roaming"),
                Find = pc.Which, AppInstalled = name => name == "Claude", Running = id => id == "claude-desktop" && ClaudeOpen,
                // A Mac quits the Claude app and opens it again for the student: it loads Syft Notes as it starts.
                ReopenApp = windows ? null : _ =>
                {
                    Loads();
                    return Task.FromResult(true);
                },
            };
        }

        /// <summary>The Claude app starts Syft Notes' server, as it does when it opens with it in its settings.</summary>
        public void Loads() => McpClients.Touch(McpClients.StartedFile(Home, "claude-desktop"));
    }

    static async Task ConnectClaudeAsync(Desk desk, SettingsModel model, Control view, StandIns pc, AppHost host, bool windows, Apps apps)
    {
        var go = new TaskCompletionSource();
        model.Engines.Install = async (engine, phase) =>
        {
            var install = pc.Installer(AgentCli.Get(engine), Path.Combine(host.Home, "logs", "setup-install.log"));
            install.Phase += phase;
            await go.Task;
            return await install.RunAsync();
        };
        model.Engines.AppsEvery = TimeSpan.FromMilliseconds(300);
        string StateOf() => model.Engines.Engines.Single(r => r.Id == "claude").StateWords;
        string AboutOf() => model.Engines.Engines.Single(r => r.Id == "claude").About;
        var card = model.Engines.Engines.Single(r => r.Id == "claude");
        desk.ScrollTo(Desk.Words(view, "AIs on this computer"));
        desk.Caption("One card per AI · Claude isn't set up, and the Claude app is already on this computer");
        desk.Hold(2.6);
        desk.Pointer();
        desk.Press(Desk.ButtonIn(view, b => b.DataContext == card && b.Command == card.ActCommand));
        await desk.UntilAsync(() => AboutOf() == "Installing Claude…", "installing");
        desk.NoPointer();
        desk.Caption("One Connect: Syft Notes installs Claude (the stand-in's installer) · nothing to type");
        desk.Hold(2.0);
        go.SetResult();
        await desk.UntilAsync(() => StateOf() is "Signing in" or "Almost done" or "Connected", "signing in", TimeSpan.FromSeconds(60), () => $"{StateOf()} · {AboutOf()}");
        desk.Caption("The Claude app and Claude Code got Syft Notes in the files they read · now Claude's own sign-in, in your browser");
        desk.Hold(2.0);
        await desk.UntilAsync(() => StateOf() is "Almost done" or "Connected", "connected", TimeSpan.FromSeconds(60), () => $"{StateOf()} · {AboutOf()}");
        desk.Caption(windows ? "The one thing left, said once: quit Claude from the tray by the clock" : "The one thing left, said once: quit Claude and open it again (Reopen does it)");
        desk.Hold(3.2);
        if (!windows)
        {
            desk.Pointer();
            desk.Press(Desk.ButtonIn(view, b => b.DataContext is ViewModels.AiEngineRow { Id: "claude", CanReopen: true } now && b.Command == now.ReopenCommand));
        }
        else
        {
            // The student quits Claude from the tray and opens it again: it loads Syft Notes.
            apps.Loads();
        }
        await desk.UntilAsync(() => StateOf() == "Connected", "loaded", TimeSpan.FromSeconds(30), () => $"{StateOf()} · {AboutOf()}");
        desk.NoPointer();
        desk.Caption("Connected · writes your notes · in the Claude app and Claude Code");
        desk.Hold(3.0);
        // The power options, folded away under the cards.
        desk.Pointer();
        desk.Press(Desk.ButtonIn(view, b => b.Command == model.Engines.ToggleAdvancedCommand));
        desk.NoPointer();
        desk.Caption("Everything else is under Advanced settings: API keys, what AI apps may read, the web address");
        desk.Hold(1.0);
        desk.ScrollTo(Desk.Words(view, "Hide advanced settings"), seconds: 1.6);
        desk.Hold(2.2);
        desk.ScrollTo(Desk.Words(view, "AI apps and your library"), seconds: 1.6);
        desk.Hold(2.2);
    }

    static async Task FilmAsync(string name, SkinKind skin, ThemeVariant variant, string what, string? note)
    {
        Look(skin);
        bool windows = skin == SkinKind.Win;
        using var pc = new StandIns();
        using var host = FilmParts.Host(pc["app"]);
        await using var lib = await FilmLibrary.StartAsync(pc, s => s.Tests["claude"] = "Not logged in · Please run /login");
        FilmParts.Connect(host, lib);
        var apps = new Apps(pc, host, windows);
        using var desk = new Desk(name, pc, variant, what, note);
        var model = SettingsModel.Make(host, ai: lib.Remote).WithAiApps(apps.Setup);
        model.Section = "AI";
        await model.Engines.Load();
        await model.AiProblems.Load();
        var view = new SettingsView { DataContext = model, DrawChrome = true };
        desk.Show(view);
        desk.Hold(1.4);
        await ConnectClaudeAsync(desk, model, view, pc, host, windows, apps);

        // What Connect did, checked: both apps have Syft Notes in the file each reads, under its new name, and Claude
        // writes the notes; on the Windows look the old folder's entry is gone.
        Assert.All(apps.Setup.StatesOf("claude"), a => Assert.True(a.Good, a.Id));
        Assert.Equal("claude", model.Engines.SelectedNotes);
        if (windows) Assert.Null(JsonNode.Parse(File.ReadAllText(apps.Setup.Desktop.Others.First(o => o.Contains("LocalCache", StringComparison.Ordinal))))!["mcpServers"]);
        Assert.Equal(0, pc.TerminalDoors + pc.RefusedTries);
        model.Dispose();
        Look(SkinKind.Mac);
    }

    [AvaloniaFact]
    public async Task Connect_does_everything_for_claude()
    {
        Assert.SkipUnless(FilmParts.On, Skip);
        await FilmAsync("07-connect-everything", SkinKind.Mac, ThemeVariant.Light, "Settings → Your AI · one Connect · stand-in Claude · throwaway home and library · waits sped up", null);
    }

    [AvaloniaFact]
    public async Task Connect_on_windows_in_dark()
    {
        Assert.SkipUnless(FilmParts.On, Skip);
        await FilmAsync("08-windows-dark-connect", SkinKind.Win, ThemeVariant.Dark, "the Windows look, dark, drawn on a Mac · one Connect · stand-ins · waits sped up",
            "The Windows look, drawn on a Mac. The Claude app's settings are laid out as the Microsoft Store build keeps them now, with an earlier Syft Notes' entry left in the old folder.");
    }
}
