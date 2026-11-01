using StudyStash.App.Services;
using StudyStash.App.ViewModels;
using StudyStash.Audio;
using StudyStash.Core;
using StudyStash.Core.Ai;

namespace StudyStash.App.Tests;

/// <summary>
/// A first run as every student has it now: one page, one button. What could really break for one: Get started not
/// getting them to Home (or getting there with no library), a login item written though they unticked it, an AI picked
/// that bills per lecture, Record refusing before the speech model has downloaded, an update showing any of it again,
/// and the lecture page's card not offering the free way, or not saying why it can't.
/// </summary>
public sealed class FirstRunTests
{
    static AppHost Host(TempHome home, CountingLoginItems? login = null) =>
        new(home.Path, log: _ => { }, loginItems: login ?? new CountingLoginItems(), models: ModelSetting.None);

    // --- the welcome ---------------------------------------------------------------------------------------------

    [Fact]
    public void A_first_run_is_one_page_with_one_button_and_open_at_login_ticked()
    {
        var mac = SetupModel.For(SkinKind.Mac);
        mac.OneButton = true;
        Assert.Equal([SetupStep.Start], mac.Steps.Select(s => s.Step));
        Assert.True(mac.OnStart);
        Assert.True(mac.IsLast);
        Assert.Equal(AppRole.Both, mac.Role);
        Assert.Equal("Get started", mac.ContinueLabel);
        Assert.Equal("Record your lectures. Get the notes.", SetupModel.StartTitle);
        Assert.Equal("Press Record when class starts. Syft Notes writes down what's said, here on this Mac, and turns it into study notes.", mac.StartLede);
        // The Mac's microphone question comes next, so the page says so; Windows asks nothing.
        Assert.Equal("Your Mac will ask next to let Syft Notes use the microphone. Recordings stay on this Mac.", mac.StartHint);
        Assert.True(mac.StartAtLogin);

        var win = SetupModel.For(SkinKind.Win);
        win.OneButton = true;
        Assert.Equal([SetupStep.Start], win.Steps.Select(s => s.Step));
        Assert.Equal("Recordings stay on this PC.", win.StartHint);
        Assert.Contains("here on this PC", win.StartLede, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Get_started_makes_the_library_then_does_the_rest_then_finishes()
    {
        var m = SetupModel.For(SkinKind.Mac);
        m.OneButton = true;
        var did = new List<string>();
        m.OnConnect = () =>
        {
            did.Add("library");
            Assert.True(m.Starting);
            Assert.Equal("Getting ready…", m.ContinueLabel);
            m.LibraryOk = true;
            return Task.CompletedTask;
        };
        m.OnGetStarted = () =>
        {
            did.Add("rest");
            return Task.CompletedTask;
        };
        m.OnFinish = () => did.Add("finish");
        await m.NextCommand.ExecuteAsync(null);
        Assert.Equal(["library", "rest", "finish"], did);
        Assert.False(m.Starting);
    }

    [Fact]
    public async Task A_library_that_cant_be_made_says_why_and_the_button_says_try_again()
    {
        var m = SetupModel.For(SkinKind.Mac);
        m.OneButton = true;
        int tries = 0;
        bool rest = false, finished = false;
        m.OnConnect = () =>
        {
            tries++;
            m.LibraryResult = "Port 8787 is taken by another app.";
            return Task.CompletedTask;
        };
        m.OnGetStarted = () =>
        {
            rest = true;
            return Task.CompletedTask;
        };
        m.OnFinish = () => finished = true;
        await m.NextCommand.ExecuteAsync(null);
        Assert.True(m.HasStartProblem);
        Assert.Equal("Try again", m.ContinueLabel);
        Assert.False(rest);
        Assert.False(finished);
        // Try again makes it again.
        await m.NextCommand.ExecuteAsync(null);
        Assert.Equal(2, tries);
    }

    [Fact]
    public void Finishing_a_first_run_turns_on_Homes_card_and_writes_the_login_item_only_while_ticked()
    {
        using var home = new TempHome();
        var login = new CountingLoginItems();
        using var host = Host(home, login);
        var m = Setup.Make(host, AppRole.Both);
        m.OneButton = true;
        m.StartAtLogin = false;
        Setup.Finish(m, host);
        var saved = AppSettings.Load(home.Path);
        Assert.True(saved.SetupDone);
        Assert.Equal(AppRole.Both, saved.Role);
        Assert.True(saved.WelcomeCard);
        Assert.Equal(OperatingSystem.IsMacOS(), saved.AskNotificationsAtStop);
        Assert.Empty(login.Calls);

        m.StartAtLogin = true;
        Setup.Finish(m, host);
        Assert.Equal([true], login.Calls);
    }

    [Fact]
    public void Only_a_first_run_on_just_this_computer_gets_the_welcome()
    {
        using var home = new TempHome();
        using (var host = Host(home)) Assert.True(Setup.OneButtonFits(host));
        // A laptop already connected to a library on another computer keeps setup by hand (frozen as it is).
        using (var host = Host(home))
        {
            var cc = host.Client();
            cc.ServerUrl = "http://mac-mini.local:8787";
            host.SaveClient(cc);
            Assert.False(Setup.OneButtonFits(host));
        }
        // An update (set up already) sees nothing of it.
        File.Delete(Path.Combine(home.Path, "client.toml"));
        var settings = AppSettings.Load(home.Path);
        settings.SetupDone = true;
        settings.Role = AppRole.Both;
        File.WriteAllText(AppSettings.PathIn(home.Path), System.Text.Json.JsonSerializer.Serialize(settings,
            new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.SnakeCaseLower }));
        using (var host = Host(home))
        {
            Assert.True(host.Settings.SetupDone);
            Assert.False(Setup.OneButtonFits(host));
            Assert.False(host.Settings.WelcomeCard);
            Assert.False(host.Settings.AskNotificationsAtStop);
        }
    }

    // --- an AI already here --------------------------------------------------------------------------------------

    [Theory]
    [InlineData("claude", """{"loggedIn":true,"authMethod":"claude.ai","subscriptionType":"pro"}""", 0, true)]
    [InlineData("claude", """{"loggedIn":true,"authMethod":"api_key"}""", 0, false)] // paid for as it's used: never picked unasked
    [InlineData("claude", """{"loggedIn":false}""", 1, false)]
    [InlineData("codex", "Logged in using ChatGPT", 0, true)]
    [InlineData("codex", "Logged in using an API key - sk-proj-***abcd", 0, false)]
    [InlineData("codex", "Not logged in", 1, false)]
    [InlineData("gemini", "gemini-3-flash", 0, true)]
    [InlineData("gemini", "Please sign in", 1, false)]
    public void Only_an_AI_signed_in_on_a_plan_writes_the_notes_unasked(string id, string said, int exit, bool onPlan) =>
        Assert.Equal(onPlan, FirstRun.OnPlan(id, new ProcResult(exit, said)));

    [Fact]
    public void The_AI_found_is_the_first_of_Claude_ChatGPT_and_Gemini_signed_in_on_a_plan()
    {
        var here = new Dictionary<string, ProcResult>
        {
            ["claude"] = new(0, """{"loggedIn":true,"authMethod":"api_key"}"""),
            ["codex"] = new(0, "Logged in using ChatGPT"),
            ["gemini"] = new(0, "gemini-3-flash"),
        };
        AgentFound Found(AgentCliInfo cli) => here.ContainsKey(cli.Id) ? new AgentFound("/bin/" + cli.Binary, new Version(1, 0, 0), "1.0.0") : AgentFound.None;
        Assert.Equal("codex", FirstRun.Detect(Found, (cli, _) => here[cli.Id]));
        here.Remove("codex");
        Assert.Equal("gemini", FirstRun.Detect(Found, (cli, _) => here[cli.Id]));
        here.Clear();
        Assert.Equal("", FirstRun.Detect(Found, (cli, _) => here[cli.Id]));
    }

    [Fact]
    public async Task With_no_AI_here_the_notes_wait_for_the_first_lectures_page()
    {
        using var home = new TempHome();
        using var host = Host(home);
        var m = Setup.Make(host, AppRole.Both);
        var ai = new FakeAiLibrary { Overview = new AiOverview([], Notes: "ollama", Ask: "ollama", Fallback: false, Problems: []) };
        var notes = new List<bool>();
        await FirstRun.AfterLibraryAsync(m, host, ai, () => "", on =>
        {
            notes.Add(on);
            return Task.FromResult(true);
        });
        Assert.Equal([false], notes);
        Assert.Empty(ai.DefaultsCalls);

        // Claude signed in here on a plan: it writes the notes and answers questions, with no question asked.
        notes.Clear();
        await FirstRun.AfterLibraryAsync(m, host, ai, () => "claude", on =>
        {
            notes.Add(on);
            return Task.FromResult(true);
        });
        Assert.Equal([true], notes);
        Assert.Equal(("claude", "claude"), (ai.DefaultsCalls[^1].Notes, ai.DefaultsCalls[^1].Ask));
        Assert.Equal("claude", host.Settings.SetupAi);
    }

    // --- Record before the model -----------------------------------------------------------------------------------

    [Fact]
    public async Task Record_works_before_the_model_has_downloaded_and_the_lecture_waits_for_it_quietly()
    {
        using var home = new TempHome();
        string wav = home["speech.wav"];
        // A second of a tone, not silence (silence is passed over without the model).
        using (var w = new WavWriter(wav)) w.Write([.. Enumerable.Range(0, Sound.Rate).Select(i => 0.3f * MathF.Sin(i * 2 * MathF.PI * 440 / Sound.Rate))]);
        var mirror = new Mirror();
        mirror.Hold.Add(WhisperModels.Tiny.File);
        using var host = new AppHost(home.Path, () => new FileMicrophone(wav), log: _ => { }, loginItems: new CountingLoginItems(),
            models: new ModelSetting(WhisperModels.Tiny), http: new HttpClient(mirror));
        Assert.False(host.ModelReady);

        var l = host.StartRecording("");
        Assert.True(l.WaitsForModel);
        Assert.True(l.AfterClass);
        // The download is going (the mirror holds it, as a slow network would).
        await mirror.Held.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        Assert.NotNull(host.Downloading);
        host.StopRecording(out _);

        // A lecture waiting for its model: the worker tries nothing and says no problem until the model is here.
        host.Lectures.Add(new Lecture { Id = "rec-wait", Started = "2026-10-30T10:00:00-07:00", State = LectureState.Transcribing, Seconds = 1, WaitsForModel = true });
        File.Copy(wav, host.Lectures.AudioPath("rec-wait"));
        Assert.False(await host.Whisper.StepAsync(TestContext.Current.CancellationToken));
        Assert.Null(host.WhisperProblem);
        Assert.Equal(LectureState.Transcribing, host.Lectures.Get("rec-wait")!.State);
    }

    // --- Home's card -------------------------------------------------------------------------------------------------

    [Fact]
    public void Homes_card_says_how_the_models_download_is_getting_on()
    {
        var card = new WelcomeCardModel(mac: true) { RecordKeys = "⌃⌥R" };
        Assert.Equal("Press Record, or ⌃⌥R, when the lecture begins. Type your own notes as it records if you like.", card.Lede);
        card.ShowModel(ready: false, fraction: 0.42, problem: null, device: "Mac");
        Assert.Equal("Getting speech-to-text ready on this Mac · 42%", card.ModelLine);
        Assert.False(card.ModelFailed);
        card.ShowModel(ready: false, fraction: null, problem: "No route to host.", device: "Mac");
        Assert.Equal("Speech-to-text couldn't download: No route to host.", card.ModelLine);
        Assert.True(card.ModelFailed);
        card.ShowModel(ready: true, fraction: null, problem: null, device: "Mac");
        Assert.Equal("Speech-to-text is ready.", card.ModelLine);
        card.IconHidden = true;
        Assert.True(card.CanShowIt);
        Assert.Equal("Your menu bar is full, so the Syft Notes icon is hidden behind other icons.", card.WhereLine);
        Assert.False(new WelcomeCardModel(mac: false) { IconHidden = true }.CanShowIt);
    }

    // --- the lecture page's card -----------------------------------------------------------------------------------

    [Fact]
    public void With_no_AI_the_lecture_page_offers_Google_as_Gemini_first_then_all_four_AIs_and_copy_and_paste_quietly()
    {
        var free = new FreeAiModel(new FakeAiLibrary(), "lec-1") { Offer = true, Choose = true };
        var asked = new List<string>();
        free.OnGetReady = asked.Add;
        Assert.True(free.ShowChoose);
        Assert.False(free.ShowOffer); // the old offer gives way to the card
        Assert.False(free.ShowCopyWay); // the other free way is folded until asked for
        Assert.Equal("Get study notes from this lecture", free.ChooseTitle);
        // The Google button says it's Gemini, beside it.
        Assert.Equal("Continue with Google", FreeAiModel.GoogleButton);
        Assert.Equal("Gemini, free with your Google account", FreeAiModel.GoogleBeside);
        Assert.Contains("Google's free plan may use what's sent to improve its products", FreeAiModel.GoogleLine, StringComparison.Ordinal);
        Assert.Contains("Ollama is free and runs on this computer (advanced)", FreeAiModel.PaidLine, StringComparison.Ordinal);
        free.ContinueWithGoogleCommand.Execute(null);
        free.UseClaudeCommand.Execute(null);
        free.UseChatGptCommand.Execute(null);
        free.UseGeminiCommand.Execute(null);
        free.UseOllamaCommand.Execute(null);
        Assert.Equal(["gemini", "claude", "codex", "gemini", "ollama"], asked);
        free.ToggleCopyWayCommand.Execute(null);
        Assert.True(free.ShowCopyWay);
    }

    [Fact]
    public void A_school_account_or_a_used_up_allowance_is_said_plainly_with_copy_and_paste_meanwhile()
    {
        var free = new FreeAiModel(new FakeAiLibrary(), "lec-1") { Offer = true, Choose = true, Fallback = NotesFallback.GoogleAccount };
        Assert.False(free.ShowChoose);
        Assert.True(free.ShowFallback);
        Assert.True(free.ShowCopyWay);
        Assert.True(free.ShowGoogleAgain);
        Assert.Equal("Use a personal Google account", free.FallbackTitle);
        Assert.Contains("personal Google account (like Gmail)", free.FallbackText, StringComparison.Ordinal);
        Assert.Contains("copy and paste", free.FallbackText, StringComparison.Ordinal);

        free.Fallback = NotesFallback.Limit;
        free.LimitedAi = "Gemini";
        free.LimitedUntil = "Tue 3 Nov, 9:00 am";
        Assert.Equal("Google's free allowance is used up for now", free.FallbackTitle);
        Assert.Equal("This lecture keeps its transcript, and its notes are written by themselves once Google's allowance is back (Tue 3 Nov, 9:00 am). "
            + "Meanwhile, you can copy and paste it into any free AI chat.", free.FallbackText);
        Assert.False(free.ShowGoogleAgain);
        Assert.True(free.ShowCopyWay);
    }
}
