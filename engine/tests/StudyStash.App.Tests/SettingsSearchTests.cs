using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StudyStash.App.Services;
using StudyStash.App.ViewModels;
using StudyStash.App.Views;
using StudyStash.Core;
using StudyStash.Core.Ai;
using StudyStash.Core.Setup;

namespace StudyStash.App.Tests;

/// <summary>Settings' search (every setting it knows is where it says, and it goes there), and its helper, "Ask your
/// AI" (it changes settings the way the student's own clicks would, and talks through the student's own AI).</summary>
public class SettingsSearchTests
{
    static async Task<(SettingsModel Model, AppHost Host, TempHome Home)> SettingsAsync(AppRole role = AppRole.Both)
    {
        var home = new TempHome();
        new AppSettings { SetupDone = true, Role = role }.Save(home.Path);
        var host = new AppHost(home.Path, log: _ => { });
        var library = new FakeLibrarySettings();
        var model = SettingsModel.Make(host, ai: AiDemo.DemoLibrary(), canvas: CanvasFixtures.Context(CanvasShots.ConnectedLibrary()), library: () => library.Call);
        await model.Engines.Load();
        await model.Access.Load();
        await model.Canvas.LoadAsync();
        await model.Lib.Load();
        return (model, host, home);
    }

    /// <summary>Rows that show only when there's something for them (Canvas course names to use, a browser helper set
    /// up, the library's phone page loaded, a library letting laptops in), so a test library may not have them.</summary>
    static readonly string[] Sometimes = ["Use Canvas course names", "Change browser", "Add a phone", "Laptops"];

    [AvaloniaTheory]
    [InlineData(SkinKind.Mac, AppRole.Both)]
    [InlineData(SkinKind.Win, AppRole.Both)]
    [InlineData(SkinKind.Mac, AppRole.Laptop)]
    public async Task Every_setting_search_knows_is_on_its_page(SkinKind skin, AppRole role)
    {
        try
        {
            ((App)Application.Current!).UseSkin(skin);
            var (model, host, home) = await SettingsAsync(role);
            using (home)
            using (host)
            using (model)
            {
                var view = new SettingsView { DataContext = model };
                var window = new Window { Width = 900, Height = 1600, Content = view };
                window.Show();
                var missing = new List<string>();
                foreach (var e in SettingsIndex.All.Where(e => !e.IsPage && model.Shows(e) && !Sometimes.Contains(e.Title)))
                {
                    model.Section = e.Section;
                    Dispatcher.UIThread.RunJobs();
                    window.UpdateLayout();
                    if (view.Find(e.Title) is null) missing.Add($"{e.Section} → {e.Title}");
                }
                window.Close();
                Assert.True(missing.Count == 0, "Not on its page: " + string.Join("; ", missing));
            }
        }
        finally
        {
            ((App)Application.Current!).UseSkin(SkinKind.Mac);
        }
    }

    [AvaloniaFact]
    public async Task Searching_lists_what_matches_and_picking_one_goes_there_and_lights_it_up()
    {
        var (model, host, home) = await SettingsAsync();
        using (home)
        using (host)
        using (model)
        {
            var view = new SettingsView { DataContext = model };
            var window = new Window { Width = 900, Height = 900, Content = view };
            window.Show();
            Assert.False(model.Searching);

            model.SearchText = "keep recordings";
            Assert.True(model.Searching);
            Assert.Equal("Keep recordings", model.SearchHits[0].Title);
            Assert.Equal("Recording", model.SearchHits[0].Where);
            Assert.Contains("“keep recordings”", model.AskLine);

            model.OpenHitCommand.Execute(model.SearchHits[0]);
            Assert.Equal("Recording", model.Section);
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            var line = view.Find("Keep recordings");
            Assert.NotNull(line);
            Assert.Contains(line!.GetSelfAndVisualAncestors().OfType<Control>(), c => c.Classes.Contains("spot"));

            model.SearchText = "qqqqzz";
            Assert.True(model.NoHits);
            model.ClearSearchCommand.Execute(null);
            Assert.False(model.Searching);
            Assert.Empty(model.SearchHits);
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task A_library_only_computer_doesnt_find_recording_settings()
    {
        var (model, host, home) = await SettingsAsync(AppRole.Library);
        using (home)
        using (host)
        using (model)
        {
            model.SearchText = "zoom";
            Assert.DoesNotContain(model.SearchHits, h => h.Entry.Section == "Recording");
        }
    }

    [AvaloniaFact]
    public async Task Search_finds_recording_the_computers_sound_only_where_it_can()
    {
        var (model, host, home) = await SettingsAsync();
        using (home)
        using (host)
        using (model)
        {
            Assert.Equal(model.CanRecordComputerAudio, model.Shows(SettingsIndex.ByKey("computer_audio")!));
            model.SearchText = "zoom";
            Assert.Equal(model.CanRecordComputerAudio, model.SearchHits.Any(h => h.Entry.Key == "computer_audio"));
        }
    }

    [AvaloniaFact]
    public async Task The_helper_changes_settings_as_the_students_own_clicks_would()
    {
        var (model, host, home) = await SettingsAsync();
        using (home)
        using (host)
        using (model)
        {
            var driver = model.Driver();
            Assert.Null(await driver.ChangeAsync(SettingsIndex.ByKey("appearance")!, "dark"));
            Assert.Equal(AppAppearance.Dark, model.Appearance);
            Assert.Equal("Appearance", model.Section);
            Assert.Null(await driver.ChangeAsync(SettingsIndex.ByKey("keep_recordings_days")!, "7"));
            if (model.CanRecordComputerAudio) Assert.Null(await driver.ChangeAsync(SettingsIndex.ByKey("computer_audio")!, "on"));
            Assert.NotNull(await driver.ChangeAsync(SettingsIndex.ByKey("keep_recordings_days")!, "forever"));
            Assert.NotNull(await driver.ChangeAsync(SettingsIndex.ByKey("colour_theme")!, "no such theme"));
            Assert.Null(await driver.ChangeAsync(SettingsIndex.ByKey("colour_theme")!, ColourThemes.All[1].Name.ToUpperInvariant()));

            var saved = AppSettings.Load(home.Path);
            Assert.Equal(AppAppearance.Dark, saved.Appearance);
            Assert.Equal(7, saved.KeepAudioDays);
            Assert.Equal(ColourThemes.All[1].Name, saved.Theme);

            // The library's own, through its settings routes.
            Assert.Null(await driver.ChangeAsync(SettingsIndex.ByKey("write_notes")!, "off"));
            Assert.False(model.Lib.WriteNotes);
            var values = await driver.ValuesAsync();
            Assert.Contains(values, v => v.Entry.Key == "appearance" && v.Value == "dark");
            Assert.Contains(values, v => v.Entry.Key == "write_notes" && v.Value == "off");
            Assert.Null(driver.LibraryProblem);
        }
    }

    sealed class FakeTurns
    {
        public List<SetupTurn> Turns { get; } = [];

        public async IAsyncEnumerable<SetupChatEvent> Run(SetupTurn t, [EnumeratorCancellation] CancellationToken ct)
        {
            Turns.Add(t);
            await Task.Yield();
            yield return new SetupChatEvent("Session", "s-1");
            yield return new SetupChatEvent("Tool") { Tool = "change_setting", Chip = "Mode: dark" };
            yield return new SetupChatEvent("Text", "Done — Study Stash is dark now.");
            yield return new SetupChatEvent("Final", "Done — Study Stash is dark now.");
        }
    }

    static SettingsChatServices Services(TempHome home, FakeTurns turns, bool found = true) => new()
    {
        Home = home.Path,
        Find = cli => found && cli.Id == "claude" ? new AgentFound("/usr/local/bin/claude", new Version(2, 1, 300), "2.1.300") : AgentFound.None,
        Turn = turns.Run,
        Preferred = () => "",
        Computer = () => "This Mac records lectures and is your library.",
        OpenDoor = () => Task.FromResult<SetupDoor?>(new SetupDoor("http://127.0.0.1:1/setup-mcp", "token", () => { })),
    };

    [AvaloniaFact]
    public async Task Asking_the_helper_runs_a_turn_of_the_students_own_AI_and_shows_what_it_did()
    {
        using var home = new TempHome();
        var turns = new FakeTurns();
        using var chat = new SettingsChatModel(Services(home, turns));
        await chat.OpenAsync();
        Assert.True(chat.IsReady);
        Assert.Equal("Ask Claude", chat.Title);
        Assert.True(chat.ShowWelcome);

        chat.Draft = "make it dark";
        await chat.SendCommand.ExecuteAsync(null);

        var t = Assert.Single(turns.Turns);
        Assert.Equal("claude", t.Provider);
        Assert.Equal("make it dark", t.Prompt);
        Assert.Equal(SettingsChat.Folder(home.Path), t.WorkDir);
        Assert.Contains("settings helper", File.ReadAllText(t.BriefFile));
        Assert.Equal("", chat.Draft);
        Assert.False(chat.ShowWelcome);
        Assert.IsType<StudentEntry>(chat.Thread[0]);
        var ai = Assert.IsType<AiEntry>(chat.Thread[1]);
        Assert.Equal("Done — Study Stash is dark now.", ai.Text);
        Assert.Equal(["Mode: dark"], ai.Chips);

        // The next turn carries the conversation on; New chat starts afresh.
        await chat.SendAsync("thanks");
        Assert.Equal("s-1", turns.Turns[1].Session);
        chat.NewChatCommand.Execute(null);
        Assert.Empty(chat.Thread);
        await chat.SendAsync("again");
        Assert.Equal("", turns.Turns[2].Session);
    }

    [AvaloniaFact]
    public async Task With_no_AI_on_this_computer_the_helper_offers_setup()
    {
        using var home = new TempHome();
        var turns = new FakeTurns();
        int setups = 0;
        var services = Services(home, turns, found: false);
        services.RunSetup = () => setups++;
        using var chat = new SettingsChatModel(services);
        await chat.SendAsync("make it dark");
        Assert.True(chat.IsNoAi);
        Assert.Empty(turns.Turns);
        Assert.Empty(chat.Thread);
        chat.SetUpCommand.Execute(null);
        Assert.Equal(1, setups);
    }

    [AvaloniaFact]
    public async Task Asking_from_search_opens_the_helper_with_the_words()
    {
        var (model, host, home) = await SettingsAsync();
        using (home)
        using (host)
        using (model)
        {
            Assert.Equal("Ask your AI", model.NavItems.First().Label);
            model.SearchText = "something settings don't have";
            model.AskAboutSearchCommand.Execute(null);
            Assert.Equal("Assistant", model.Section);
            Assert.True(model.OnAssistant);
            Assert.False(model.OnPlainPage);
            Assert.Equal("", model.SearchText);
        }
    }
}
