using StudyStash.Core.Ai;
using StudyStash.Library;

namespace StudyStash.Core.Tests;

/// <summary>/api/v2/ai/* end to end, through TestSite and AiRemote — exactly how the app reads it. No test here
/// ever touches a real AI account, a real terminal, or Ollama: every probe and every engine is a fake.</summary>
public class AiApiTests
{
    static Config Cfg(TempDir dir) => new(dir["home"], dir["pool"]) { PoolPassword = "pw", OllamaEnabled = true, OllamaModel = "qwen3:8b" };

    static async Task<TestSite> Site(Config cfg, Store store, AiJobs ai) => await TestSite.StartAsync(b => LibraryWeb.Build(b, cfg, store,
        new Pipeline(cfg, store, log: _ => { }), new LibraryWebOptions
        {
            ListModels = _ => Task.FromResult<List<(string, double)>?>(null), Tailscale = () => new TailscaleInfo(),
            Latest = _ => Task.FromResult<Release?>(null), Ai = ai,
        }));

    [Fact]
    public async Task Engines_reports_a_mixed_machine_through_AiRemote()
    {
        using var dir = new TempDir();
        var cfg = Cfg(dir);
        using var store = new Store(cfg.DbPath, cfg.PoolDir);
        var ai = new AiJobs(cfg.Home) { Checks = new FakeChecks().Installed("codex").Ollama(models: [("qwen3:8b", 4.7)]).Build() };
        await using var site = await Site(cfg, store, ai);
        var remote = new AiRemote("http://localhost", "pw", site.Client);

        var overview = await remote.EnginesAsync();
        Assert.NotNull(overview);
        Assert.Equal(["ollama", "claude", "codex", "gemini"], overview!.Engines.Select(e => e.Id));
        Assert.Equal("ready", overview.Engines.Single(e => e.Id == "ollama").State);
        Assert.Equal("not_signed_in", overview.Engines.Single(e => e.Id == "codex").State); // installed, no hint, never tested
        Assert.Equal("not_installed", overview.Engines.Single(e => e.Id == "claude").State);
        Assert.True(overview.Fallback);
    }

    [Fact]
    public async Task Defaults_pick_who_writes_notes_and_who_answers_and_refuse_a_bad_pick()
    {
        using var dir = new TempDir();
        var cfg = Cfg(dir);
        using var store = new Store(cfg.DbPath, cfg.PoolDir);
        var ai = new AiJobs(cfg.Home) { Checks = new FakeChecks().Installed("claude").Build() };
        await using var site = await Site(cfg, store, ai);
        var remote = new AiRemote("http://localhost", "pw", site.Client);

        var overview = await remote.DefaultsAsync(notes: "claude", ask: "claude", fallback: false);
        Assert.NotNull(overview);
        Assert.Equal(("claude", "claude", false), (overview!.Notes, overview.Ask, overview.Fallback));

        var unknown = await Assert.ThrowsAsync<LibraryRefusedException>(() => remote.DefaultsAsync(notes: "nope"));
        Assert.Equal(400, unknown.Status);
        var notInstalled = await Assert.ThrowsAsync<LibraryRefusedException>(() => remote.DefaultsAsync(notes: "codex"));
        Assert.Equal(409, notInstalled.Status);
        Assert.Contains("ChatGPT isn't set up on your library's computer.", notInstalled.Message);

        // Who draws the diagrams: automatic comes to the notes engine here; off, and an engine that isn't here, refused.
        Assert.Equal(("auto", "claude"), (overview.Diagrams, overview.DiagramsBy));
        Assert.Equal(("off", ""), ((await remote.DefaultsAsync(diagrams: "off"))!.Diagrams, (await remote.EnginesAsync())!.DiagramsBy));
        Assert.Equal(409, (await Assert.ThrowsAsync<LibraryRefusedException>(() => remote.DefaultsAsync(diagrams: "codex"))).Status);
        Assert.Equal(400, (await Assert.ThrowsAsync<LibraryRefusedException>(() => remote.DefaultsAsync(diagrams: "nope"))).Status);
    }

    [Fact]
    public async Task Rich_notes_switches_and_Claude_Codes_speed_round_trip_through_the_librarys_ai_json()
    {
        using var dir = new TempDir();
        var cfg = Cfg(dir);
        using var store = new Store(cfg.DbPath, cfg.PoolDir);
        var ai = new AiJobs(cfg.Home) { Checks = new FakeChecks().Installed("claude").Build() };
        await using var site = await Site(cfg, store, ai);
        var remote = new AiRemote("http://localhost", "pw", site.Client);

        // As it was: rich notes on, every kind, Claude Code as set up.
        var start = (await remote.EnginesAsync())!;
        Assert.Equal((new RichNotesInfo(true, true, true, true), "standard"), (start.Rich, start.Speed));

        // The laptop switches rich notes off, then on with one kind less, and picks fast mode.
        Assert.Equal(new RichNotesInfo(false, true, true, true), (await remote.RichAsync(on: false))!.Rich);
        var changed = (await remote.RichAsync(on: true, plots: false, speed: "fast"))!;
        Assert.Equal((new RichNotesInfo(true, true, false, true), "fast"), (changed.Rich, changed.Speed));
        var saved = AiSettings.Load(cfg.Home); // the library's own ai.json holds it, and the designer reads it from there
        Assert.Equal((RichKinds.Diagrams | RichKinds.Drawings, AiSpeed.Fast), (saved.Kinds(), saved.Speed));
        Assert.Equal(("claude", RichKinds.Diagrams | RichKinds.Drawings, true), await ai.DesignerAsync(cfg, "claude") is { } pick ? (pick.Engine, pick.Kinds, pick.Fast) : default);

        // Nothing left to draw means rich notes off; and the old "off" pick of diagrams reads as the switch being off.
        Assert.False((await remote.RichAsync(diagrams: false, drawings: false))!.Rich!.On);
        Assert.False((await remote.DefaultsAsync(diagrams: "off"))!.Rich!.On);
        Assert.True((await remote.RichAsync(on: true))!.Rich!.On);
        Assert.Equal("auto", AiSettings.Load(cfg.Home).Diagrams);
        Assert.Equal(400, (await Assert.ThrowsAsync<LibraryRefusedException>(() => remote.RichAsync(speed: "warp"))).Status);
    }

    [Fact]
    public async Task Ask_answers_with_the_engine_you_pick()
    {
        using var dir = new TempDir();
        var cfg = Cfg(dir);
        using var store = new Store(cfg.DbPath, cfg.PoolDir);
        var claude = new ScriptedAi("claude", "Claude") { Script = [new AiEvent("final", """{"answer":"Cells have membranes.","sources":[1]}""")] };
        var ai = new AiJobs(cfg.Home) { Providers = id => id == "claude" ? claude : new ScriptedAi(id), Checks = new FakeChecks().Installed("claude").Build() };
        await using var site = await Site(cfg, store, ai);
        var remote = new AiRemote("http://localhost", "pw", site.Client);

        var reply = await remote.AskAsync(new AskRequest("What did we study today?")
        {
            Engine = "claude", Live = "Today we studied the cell membrane and osmosis in class.",
        });
        Assert.NotNull(reply);
        Assert.Equal(("Cells have membranes.", "claude", "Claude", false), (reply!.Answer, reply.Engine, reply.EngineName, reply.FellBack));
    }

    [Fact]
    public async Task Ask_falls_back_to_ollama_when_the_picked_engine_times_out()
    {
        using var dir = new TempDir();
        var cfg = Cfg(dir);
        using var store = new Store(cfg.DbPath, cfg.PoolDir);
        var codex = new ScriptedAi("codex", "Codex") { Hang = true };
        var ollama = new ScriptedAi("ollama") { Script = [new AiEvent("final", """{"answer":"From Ollama.","sources":[]}""")] };
        var ai = new AiJobs(cfg.Home)
        {
            Providers = id => id switch { "codex" => codex, "ollama" => ollama, _ => new ScriptedAi(id) },
            // installed, with a credential hint: "unchecked", not "not_signed_in" — so it's actually tried, not skipped up front
            Checks = new FakeChecks().Installed("codex").HasEnv("OPENAI_API_KEY", "sk-1").Build(),
            AskTimeout = TimeSpan.FromMilliseconds(200),
        };
        await using var site = await Site(cfg, store, ai);
        var remote = new AiRemote("http://localhost", "pw", site.Client);

        var reply = await remote.AskAsync(new AskRequest("What happened in class?") { Engine = "codex", Live = "Something happened today in class." });
        Assert.NotNull(reply);
        Assert.True(reply!.FellBack);
        Assert.Equal(("ollama", "codex", "From Ollama."), (reply.Engine, reply.Asked, reply.Answer));
        Assert.Contains("didn't respond in time", reply.Why);
    }

    [Fact]
    public async Task Ask_falls_back_on_a_usage_limit_error_and_records_it_until_it_lifts()
    {
        using var dir = new TempDir();
        var cfg = Cfg(dir);
        using var store = new Store(cfg.DbPath, cfg.PoolDir);
        var claude = new ScriptedAi("claude", "Claude") { Script = [AiEvent.Error("rate limit exceeded, resets in 2 hours")] };
        var ollama = new ScriptedAi("ollama") { Script = [new AiEvent("final", """{"answer":"Ollama's own answer.","sources":[]}""")] };
        var now = new DateTime(2026, 1, 1, 9, 0, 0);
        var ai = new AiJobs(cfg.Home)
        {
            Providers = id => id switch { "claude" => claude, "ollama" => ollama, _ => new ScriptedAi(id) },
            Checks = new FakeChecks().Installed("claude").At(now).Build(),
        };
        await using var site = await Site(cfg, store, ai);
        var remote = new AiRemote("http://localhost", "pw", site.Client);

        var reply = await remote.AskAsync(new AskRequest("What happened?") { Engine = "claude", Live = "Something happened today." });
        Assert.NotNull(reply);
        Assert.True(reply!.FellBack);
        Assert.Equal("Ollama's own answer.", reply.Answer);
        Assert.Contains("didn't answer", reply.Why);

        var overview = await remote.EnginesAsync();
        var row = overview!.Engines.Single(e => e.Id == "claude");
        Assert.Equal("limited", row.State);
        Assert.Equal(now.AddHours(2).ToString("o"), row.Until);
    }

    [Fact]
    public async Task Turning_fallback_off_lets_the_picked_engines_own_error_come_back()
    {
        using var dir = new TempDir();
        var cfg = Cfg(dir);
        using var store = new Store(cfg.DbPath, cfg.PoolDir);
        new AiSettings { Fallback = false }.Save(cfg.Home);
        var claude = new ScriptedAi("claude") { Script = [AiEvent.Error("the model refused to answer")] };
        var ai = new AiJobs(cfg.Home) { Providers = id => id == "claude" ? claude : new ScriptedAi(id), Checks = new FakeChecks().Installed("claude").Build() };
        await using var site = await Site(cfg, store, ai);
        var remote = new AiRemote("http://localhost", "pw", site.Client);

        var refused = await Assert.ThrowsAsync<LibraryRefusedException>(() =>
            remote.AskAsync(new AskRequest("What happened?") { Engine = "claude", Live = "Something happened." }));
        Assert.Equal(503, refused.Status);
        Assert.Contains("the model refused to answer", refused.Message);
    }

    [Fact]
    public async Task Signing_in_is_refused_away_from_the_librarys_own_computer()
    {
        using var dir = new TempDir();
        var cfg = Cfg(dir);
        using var store = new Store(cfg.DbPath, cfg.PoolDir);
        var ai = new AiJobs(cfg.Home) { Checks = new FakeChecks().Installed("codex").Build() };
        await using var site = await Site(cfg, store, ai);
        var remote = new AiRemote("http://localhost", "pw", site.Client);

        // TestSite has no real socket, so it never looks local — the same as a laptop reaching the library remotely.
        var refused = await Assert.ThrowsAsync<LibraryRefusedException>(() => remote.SignInAsync("codex"));
        Assert.Equal(409, refused.Status);
        Assert.Contains("codex login", refused.Message);
    }

    [Fact]
    public async Task Start_and_download_reach_only_the_fake_checks()
    {
        using var dir = new TempDir();
        var cfg = Cfg(dir);
        using var store = new Store(cfg.DbPath, cfg.PoolDir);
        var checks = new FakeChecks().Ollama().StartsOllama(true).Pulls(true);
        var ai = new AiJobs(cfg.Home) { Checks = checks.Build() };
        await using var site = await Site(cfg, store, ai);
        var remote = new AiRemote("http://localhost", "pw", site.Client);

        var started = await remote.StartAsync("ollama");
        Assert.NotNull(started);
        Assert.Equal(1, checks.StartCalls);

        // The fake pull can finish before the answer is written, so the answer needn't still say "pulling".
        var downloaded = await remote.DownloadAsync("ollama");
        Assert.NotNull(downloaded?.Overview);

        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline && (await remote.EnginesAsync())!.Pulling is not null) await Task.Delay(20);
        Assert.Equal(1, checks.PullCalls);
        Assert.Null((await remote.EnginesAsync())!.Pulling);
    }

    /// <summary>The free AI a library sets up is one its computer can run. A library made by the app for "Just this
    /// computer" was never given a model, so it stood on the built-in one: 24 GB to download, for a computer with 40 GB
    /// of memory, on a student's laptop.</summary>
    [Theory]
    [InlineData(8.0, 100.0, "", "qwen3:1.7b", 1.4, true)]   // a small laptop: the small one, said to be simpler
    [InlineData(16.0, 100.0, "", "gemma4:e4b", 10.0, false)]
    [InlineData(64.0, 100.0, "", "qwen3.6:35b-a3b", 24.0, false)]
    [InlineData(8.0, 100.0, "llama3.2", "llama3.2", 0.0, false)] // one the student chose stays, whatever the memory
    public async Task The_free_AI_set_up_on_a_computer_is_one_its_memory_can_run(double ram, double disk, string chosen, string model, double gb, bool small)
    {
        using var dir = new TempDir();
        var cfg = new Config(dir["home"], dir["pool"]) { PoolPassword = "pw", OllamaEnabled = true, SummaryModel = chosen };
        Configs.Save(cfg);
        using var store = new Store(cfg.DbPath, cfg.PoolDir);
        var models = new List<(string Name, double SizeGb)>();
        var pulled = new List<string>();
        var checks = new FakeChecks().Build() with
        {
            OllamaInstalled = () => true,
            OllamaModels = _ => Task.FromResult<List<(string Name, double SizeGb)>?>(models.ToList()),
            RamGb = () => ram,
            DiskFreeGb = () => disk,
            PullModel = (name, _, _, _) =>
            {
                pulled.Add(name);
                models.Add((name, gb));
                return Task.FromResult((true, ""));
            },
        };
        var jobs = new AiJobs(cfg.Home) { Checks = checks };
        await using var site = await Site(cfg, store, jobs);
        var remote = new AiRemote("http://localhost", "pw", site.Client);

        // Before anything is downloaded, the row can say how much there is to get, and that it's the small one.
        var free = (await remote.EnginesAsync())!.Engines.Single(e => e.Id == "ollama");
        Assert.Equal(("model_missing", gb, small), (free.State, free.SetUpGb, free.Small));

        await remote.SetUpAsync("ollama");
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline && (await remote.EnginesAsync())!.Pulling is { Why.Length: 0 }) await Task.Delay(20);

        Assert.Equal([model], pulled);
        Assert.Equal("ready", (await remote.EnginesAsync())!.Engines.Single(e => e.Id == "ollama").State);
        Assert.Equal(model, Configs.Load(cfg.Home).EffectiveSummaryModel); // kept: the library writes notes with it from now on
    }

    [Fact]
    public async Task A_computer_without_room_for_the_free_AI_is_told_before_anything_is_downloaded()
    {
        using var dir = new TempDir();
        var cfg = new Config(dir["home"], dir["pool"]) { PoolPassword = "pw", OllamaEnabled = true };
        using var store = new Store(cfg.DbPath, cfg.PoolDir);
        var did = new List<string>();
        var checks = new FakeChecks().Build() with
        {
            OllamaInstalled = () => false,
            RamGb = () => 16,
            DiskFreeGb = () => 5.7,
            InstallOllama = _ =>
            {
                did.Add("app");
                return Task.FromResult(true);
            },
        };
        await using var site = await Site(cfg, store, new AiJobs(cfg.Home) { Checks = checks });
        var remote = new AiRemote("http://localhost", "pw", site.Client);

        var said = await remote.SetUpAsync("ollama");

        Assert.Equal("This computer doesn't have room for the free AI: it needs about 12 GB free, and has 5.", said!.Overview!.Pulling!.Why);
        Assert.Empty(did);
    }

    /// <summary>A student with no paid plan presses one button, and the library gets the free AI ready whatever it
    /// still needs: its app from its maker, starting it, the model it writes notes with. Nothing here is a real
    /// download or a real Ollama.</summary>
    [Fact]
    public async Task The_free_AI_is_got_ready_in_one_go_its_app_then_starting_it_then_its_model()
    {
        using var dir = new TempDir();
        var cfg = Cfg(dir);
        using var store = new Store(cfg.DbPath, cfg.PoolDir);
        bool installed = false, running = false, appComes = false;
        var models = new List<(string Name, double SizeGb)>();
        var did = new List<string>();
        var seen = new List<string>();
        AiJobs? jobs = null;
        var checks = new FakeChecks().Build() with
        {
            OllamaInstalled = () => installed,
            InstallOllama = progress =>
            {
                did.Add("app");
                progress?.Invoke(50, 100);
                seen.Add($"{jobs!.Pulling?.Step} {jobs.Pulling?.Fraction}");
                installed = appComes;
                return Task.FromResult(appComes);
            },
            OllamaModels = _ => Task.FromResult(running ? models.ToList() : null),
            StartOllama = _ =>
            {
                did.Add("start");
                seen.Add($"{jobs!.Pulling?.Step}");
                running = true;
                return Task.FromResult(true);
            },
            PullModel = (model, _, progress, _) =>
            {
                did.Add("model " + model);
                progress?.Invoke(1, 4);
                seen.Add($"{jobs!.Pulling?.Step} {jobs.Pulling?.Fraction}");
                models.Add((model, 5.2));
                return Task.FromResult((true, ""));
            },
        };
        jobs = new AiJobs(cfg.Home) { Checks = checks };
        await using var site = await Site(cfg, store, jobs);
        var remote = new AiRemote("http://localhost", "pw", site.Client);
        async Task<PullInfo?> Settled()
        {
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (DateTime.UtcNow < deadline && (await remote.EnginesAsync())!.Pulling is { Why.Length: 0 }) await Task.Delay(20);
            return (await remote.EnginesAsync())!.Pulling;
        }

        // The app can't be had (no internet): said in words for the student, and nothing more is tried.
        Assert.NotNull(await remote.SetUpAsync("ollama"));
        var stopped = await Settled();
        Assert.Equal(("app", "The free AI's app couldn't be downloaded and installed. Check the internet connection and try again."), (stopped!.Step, stopped.Why));
        Assert.Equal(["app"], did);

        // Tried again with the internet back: its app, then starting it, then its model, each saying where it is.
        did.Clear();
        seen.Clear();
        appComes = true;
        await remote.SetUpAsync("ollama");
        Assert.Null(await Settled());
        Assert.Equal(["app", "start", "model " + cfg.EffectiveSummaryModel], did);
        Assert.Equal(["app 0.5", "start", "model 0.25"], seen);
        Assert.Equal("ready", (await remote.EnginesAsync())!.Engines.Single(e => e.Id == "ollama").State);

        // All there: pressed again, there's nothing to do. And only the free AI is set up this way.
        did.Clear();
        await remote.SetUpAsync("ollama");
        Assert.Null(await Settled());
        Assert.Empty(did);
        await Assert.ThrowsAsync<LibraryRefusedException>(() => remote.SetUpAsync("claude"));
    }

    [Fact]
    public async Task Checking_an_engine_records_whether_it_answered()
    {
        using var dir = new TempDir();
        var cfg = Cfg(dir);
        using var store = new Store(cfg.DbPath, cfg.PoolDir);
        var codex = new ScriptedAi("codex", "Codex") { Script = [new AiEvent("final", "ready")] };
        var ai = new AiJobs(cfg.Home) { Providers = id => id == "codex" ? codex : new ScriptedAi(id), Checks = new FakeChecks().Installed("codex").Build() };
        await using var site = await Site(cfg, store, ai);
        var remote = new AiRemote("http://localhost", "pw", site.Client);

        var said = await remote.CheckAsync("codex");
        Assert.NotNull(said);
        Assert.Equal("ready", said!.Overview!.Engines.Single(e => e.Id == "codex").State);
    }

    [Fact]
    public async Task Picking_a_model_updates_ollamas_notes_model_and_a_clis_own_model()
    {
        using var dir = new TempDir();
        var cfg = Cfg(dir);
        using var store = new Store(cfg.DbPath, cfg.PoolDir);
        var ai = new AiJobs(cfg.Home) { Checks = new FakeChecks().Installed("claude").Ollama(models: [("qwen3:8b", 4.7), ("llama3", 4.7)]).Build() };
        await using var site = await Site(cfg, store, ai);
        var remote = new AiRemote("http://localhost", "pw", site.Client);

        var afterOllama = await remote.ModelAsync("ollama", "llama3");
        Assert.NotNull(afterOllama);
        Assert.Equal("llama3", afterOllama!.Engines.Single(e => e.Id == "ollama").Model);
        Assert.Equal("llama3", Configs.Load(cfg.Home).SummaryModel);

        var afterClaude = await remote.ModelAsync("claude", "opus");
        Assert.Equal("opus", afterClaude!.Engines.Single(e => e.Id == "claude").Model);
    }

    [Fact]
    public async Task Dismissing_a_problem_hides_it_from_the_overview()
    {
        using var dir = new TempDir();
        var cfg = Cfg(dir);
        using var store = new Store(cfg.DbPath, cfg.PoolDir);
        var ai = new AiJobs(cfg.Home) { Checks = new FakeChecks().Ollama().Build() }; // not running, and it's the default: a problem
        await using var site = await Site(cfg, store, ai);
        var remote = new AiRemote("http://localhost", "pw", site.Client);

        var before = await remote.EnginesAsync();
        var problem = Assert.Single(before!.Problems);
        var after = await remote.DismissAsync(problem.Id);
        Assert.Empty(after!.Problems);
    }

    [Fact]
    public async Task An_older_library_with_no_ai_routes_gives_null_from_AiRemote()
    {
        await using var site = await TestSite.StartAsync(b => b.Build()); // nothing mapped: a plain 404, not one of ours
        var remote = new AiRemote("http://localhost", "pw", site.Client);
        Assert.Null(await remote.EnginesAsync());
        Assert.Null(await remote.DefaultsAsync(fallback: true));
        Assert.Null(await remote.StartAsync("ollama"));
    }

    [Fact]
    public async Task A_wrong_key_is_401()
    {
        using var dir = new TempDir();
        var cfg = Cfg(dir);
        using var store = new Store(cfg.DbPath, cfg.PoolDir);
        var ai = new AiJobs(cfg.Home) { Checks = new FakeChecks().Ollama(models: [("qwen3:8b", 4)]).Build() };
        await using var site = await Site(cfg, store, ai);
        var remote = new AiRemote("http://localhost", "wrong-password", site.Client);

        var refused = await Assert.ThrowsAsync<LibraryRefusedException>(() => remote.EnginesAsync());
        Assert.Equal(401, refused.Status);
    }
}
