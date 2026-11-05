using System.Net.Http.Json;
using System.Text.Json.Nodes;
using StudyStash.Core.Ai;
using StudyStash.Library;

namespace StudyStash.Core.Tests;

/// <summary>A student who picks Claude Code (or Codex, or Gemini) to write notes and has no Ollama model to sort with
/// gets lectures filed in their class, not in Unsorted: sorting follows the notes engine while Ollama can't sort, and
/// stays on Ollama whenever it can, or the student picked it.</summary>
public class SortingFollowsNotesTests
{
    const string Answer = """{"class_name": "CS 101", "confidence": 0.9, "lecture_title": "Recursion", "topics": ["recursion"]}""";

    static Config Cfg(TempDir dir) => new(dir["home"], dir["pool"])
    {
        PoolPassword = "pw", OllamaEnabled = true, OllamaModel = "qwen3:8b", Classes = [new ClassDef("CS 101", [], "Recursion and the call stack")],
    };

    static ScriptedAi Says(string id, string json) => new(id) { Script = [new("final", json)] };

    static AiJobs Jobs(Config cfg, ScriptedAi claude, ScriptedAi ollama, EngineChecks checks) =>
        new(cfg.Home) { Providers = id => id == "claude" ? claude : ollama, Checks = checks };

    static Task<Classification> Sort(AiJobs ai, Config cfg) =>
        Classify.ClassifyAsync(new Meeting("1") { Title = "Lecture", NotesMarkdown = "Today: a function that calls itself." }, cfg, ai.SortAsync);

    static void NotesBy(Config cfg, string engine, string? sortPick = null)
    {
        var s = new AiSettings();
        s.ByJob["notes"] = new AiChoice(engine);
        if (sortPick is not null) s.ByJob["sort"] = new AiChoice(sortPick);
        s.Save(cfg.Home);
    }

    [Fact]
    public async Task Picking_Claude_Code_for_notes_in_setup_files_lectures_in_their_class_though_Ollama_has_no_model()
    {
        using var dir = new TempDir();
        var cfg = Cfg(dir);
        Directory.CreateDirectory(cfg.Home);
        using var store = new Store(cfg.DbPath, cfg.PoolDir);
        var claude = Says("claude", Answer);
        var ollama = Says("ollama", """{"class_name": "Unsorted", "confidence": 0, "lecture_title": "x", "topics": []}""");
        // Ollama is running with something else: the sorting model isn't there.
        var ai = Jobs(cfg, claude, ollama, new FakeChecks().Installed("claude").Ollama(models: [("llama3.2:1b", 1.3)]).Build());
        await using var site = await TestSite.StartAsync(b => LibraryWeb.Build(b, cfg, store, new Pipeline(cfg, store, log: _ => { }), new LibraryWebOptions
        {
            ListModels = _ => Task.FromResult<List<(string, double)>?>([("llama3.2:1b", 1.3)]), Tailscale = () => new TailscaleInfo(),
            Latest = _ => Task.FromResult<Release?>(null), Ai = ai,
        }));

        // Exactly what the setup does: the notes (and answers) go to Claude Code. Nothing is said about sorting.
        await new AiRemote("http://localhost", "pw", site.Client).DefaultsAsync(notes: "claude", ask: "claude");
        Assert.False(AiSettings.Load(cfg.Home).ByJob.ContainsKey("sort"));

        var c = await Sort(ai, cfg);
        Assert.Equal(("CS 101", "ollama"), (c.ClassName, c.By)); // By: the AI sorted it (whoever it was)
        Assert.Single(claude.Prompts);
        Assert.Empty(ollama.Prompts);

        // Settings says so: sorting is with Claude Code, not an Ollama that can't sort.
        using var settings = new HttpRequestMessage(HttpMethod.Get, "/api/v2/settings");
        settings.Headers.Authorization = new("Bearer", "pw");
        var json = JsonNode.Parse(await (await site.Client.SendAsync(settings)).Content.ReadAsStringAsync())!;
        Assert.Contains("Claude", json["sorting"]!["default"]!.GetValue<string>());
        Assert.Equal("", json["sorting"]!["engine"]!.GetValue<string>()); // still "the library's main AI", nothing picked
    }

    [Fact]
    public async Task A_library_on_Gemini_alone_never_chose_Ollama_and_files_its_lectures_as_sorted_by_Gemini()
    {
        using var dir = new TempDir();
        var cfg = Cfg(dir);
        cfg.OllamaModel = Config.DefaultOllamaModel; // nobody chose a free model on this library
        new AiSettings { Provider = "gemini" }.Save(cfg.Home);
        using var store = new Store(cfg.DbPath, cfg.PoolDir);
        var gemini = Says("gemini", Answer);
        var ollama = Says("ollama", "not json");
        var ai = new AiJobs(cfg.Home)
        {
            Providers = id => id == "gemini" ? gemini : ollama,
            Checks = new FakeChecks().Installed("agy").Ollama(installed: false).Build() with { RamGb = () => 8 },
        };
        var pipeline = new Pipeline(cfg, store, ai.SortAsync, (_, _) => Task.FromResult("## Summary\nRecursion."), _ => { },
            sortedWith: () => ai.SortedWith);
        store.Enqueue(new Meeting("l1") { Title = "Lecture", Transcript = string.Concat(Enumerable.Repeat("Recursion is a function calling itself.\n", 60)) });
        store.Enqueue(new Meeting("l2") { Title = "Lecture 2", Transcript = "Too short for notes." });

        await pipeline.RunPendingAsync();

        // Sorting went to Gemini (it never waited on an Ollama that isn't there), and the lecture says so.
        Assert.Equal(("CS 101", "gemini"), (store.Get("l1")!.ClassName, store.Get("l1")!.ClassifiedBy));
        Assert.Contains("classified_by: gemini (0.90)", Py.ReadText(store.Get("l1")!.MdPath!));
        Assert.Empty(ollama.Prompts);

        // Settings: nothing to say about Ollama until it is chosen for something; the free model nobody chose is the one
        // this 8 GB computer can run (not the built-in 24 GB one), and one the student chose stays; "Rewrite all" counts
        // the one lecture that has a summary, not both.
        await using var site = await TestSite.StartAsync(b => LibraryWeb.Build(b, cfg, store, pipeline, new LibraryWebOptions
        {
            ListModels = _ => Task.FromResult<List<(string, double)>?>(null), Tailscale = () => new TailscaleInfo(),
            Latest = _ => Task.FromResult<Release?>(null), Ai = ai,
        }));
        async Task<JsonNode> Settings()
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, "/api/v2/settings");
            req.Headers.Authorization = new("Bearer", "pw");
            return JsonNode.Parse(await (await site.Client.SendAsync(req)).Content.ReadAsStringAsync())!;
        }
        var now = await Settings();
        Assert.Equal((false, "qwen3:1.7b", 1), (now["ollama"]!["chosen"]!.GetValue<bool>(), now["ollama"]!["sort_model"]!.GetValue<string>(), now["lectures"]!.GetValue<int>()));
        cfg.OllamaModel = "qwen3:8b";
        new AiSettings { Provider = "gemini", ByJob = { ["ask"] = new AiChoice("ollama") } }.Save(cfg.Home);
        now = await Settings();
        Assert.Equal((true, "qwen3:8b"), (now["ollama"]!["chosen"]!.GetValue<bool>(), now["ollama"]!["sort_model"]!.GetValue<string>()));

        // "Rewrite all" queues the lectures it counts: l1 has a summary, l2 (filed with none) is not rewritten.
        using var rewrite = new HttpRequestMessage(HttpMethod.Post, "/api/v2/settings/rewrite-all") { Content = JsonContent.Create(new { }) };
        rewrite.Headers.Authorization = new("Bearer", "pw");
        var queued = JsonNode.Parse(await (await site.Client.SendAsync(rewrite)).Content.ReadAsStringAsync())!["queued"]!.GetValue<int>();
        Assert.Equal((1, "queued", "done"), (queued, store.Get("l1")!.Status, store.Get("l2")!.Status));
    }

    [Theory]
    [InlineData("/api/v2/settings", """{"sort_engine": "ollama"}""")]
    [InlineData("/api/v2/ai/defaults", """{"ask": "ollama"}""")]
    public async Task Picking_Ollama_for_sorting_or_Ask_makes_the_model_on_this_computer_the_librarys_own(string route, string body)
    {
        using var dir = new TempDir();
        var cfg = Cfg(dir);
        cfg.OllamaModel = Config.DefaultOllamaModel; // nobody chose a free model: the built-in one is 24 GB and isn't here
        Directory.CreateDirectory(cfg.Home);
        using var store = new Store(cfg.DbPath, cfg.PoolDir);
        var ai = new AiJobs(cfg.Home) { Checks = new FakeChecks().Ollama(models: [("qwen3:1.7b", 1.4)]).Build() with { RamGb = () => 8 } };
        await using var site = await TestSite.StartAsync(b => LibraryWeb.Build(b, cfg, store, new Pipeline(cfg, store, log: _ => { }), new LibraryWebOptions
        {
            ListModels = _ => Task.FromResult<List<(string, double)>?>([("qwen3:1.7b", 1.4)]), Tailscale = () => new TailscaleInfo(),
            Latest = _ => Task.FromResult<Release?>(null), Ai = ai,
        }));

        using var post = new HttpRequestMessage(HttpMethod.Post, route) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") };
        post.Headers.Authorization = new("Bearer", "pw");
        Assert.True((await site.Client.SendAsync(post)).IsSuccessStatusCode);

        // What sorting (cfg.OllamaModel) and Ask (the notes model) ask Ollama for is the model Settings names.
        using var get = new HttpRequestMessage(HttpMethod.Get, "/api/v2/settings");
        get.Headers.Authorization = new("Bearer", "pw");
        var shown = JsonNode.Parse(await (await site.Client.SendAsync(get)).Content.ReadAsStringAsync())!["ollama"]!["sort_model"]!.GetValue<string>();
        Assert.Equal(("qwen3:1.7b", "qwen3:1.7b", "qwen3:1.7b"), (cfg.OllamaModel, cfg.EffectiveSummaryModel, shown));
        Assert.Equal("qwen3:1.7b", Configs.Load(cfg.Home).OllamaModel); // and it is saved, not only held while running
    }

    [Fact]
    public async Task Sorting_stays_on_Ollama_when_it_has_the_model()
    {
        using var dir = new TempDir();
        var cfg = Cfg(dir);
        NotesBy(cfg, "claude");
        var claude = Says("claude", Answer);
        var ollama = Says("ollama", Answer);
        var ai = Jobs(cfg, claude, ollama, new FakeChecks().Installed("claude").Ollama(models: [("qwen3:8b", 5.2)]).Build());

        Assert.Equal("CS 101", (await Sort(ai, cfg)).ClassName);
        Assert.Single(ollama.Prompts);
        Assert.Empty(claude.Prompts);
    }

    [Fact]
    public async Task A_sorting_engine_the_student_picked_is_kept_even_when_it_cannot_sort()
    {
        using var dir = new TempDir();
        var cfg = Cfg(dir);
        NotesBy(cfg, "claude", sortPick: "ollama");
        var claude = Says("claude", Answer);
        var ollama = Says("ollama", "not json");
        var ai = Jobs(cfg, claude, ollama, new FakeChecks().Installed("claude").Ollama(models: []).Build());

        Assert.Equal(Configs.Unsorted, (await Sort(ai, cfg)).ClassName);
        Assert.Single(ollama.Prompts);
        Assert.Empty(claude.Prompts);
    }

    [Fact]
    public async Task Sorting_waits_with_Ollama_when_the_notes_engine_cannot_be_used_either()
    {
        using var dir = new TempDir();
        var cfg = Cfg(dir);
        NotesBy(cfg, "claude");
        var claude = Says("claude", Answer);
        var ollama = Says("ollama", "not json");
        var ai = Jobs(cfg, claude, ollama, new FakeChecks().Ollama(models: null).Build()); // Claude Code isn't installed

        Assert.Equal(Configs.Unsorted, (await Sort(ai, cfg)).ClassName);
        Assert.Empty(claude.Prompts);
    }

    [Fact]
    public async Task A_library_whose_notes_are_Ollamas_is_unchanged()
    {
        using var dir = new TempDir();
        var cfg = Cfg(dir);
        var claude = Says("claude", Answer);
        var ollama = Says("ollama", "not json");
        var ai = Jobs(cfg, claude, ollama, new FakeChecks().Installed("claude").Ollama(models: []).Build());

        Assert.Equal(Configs.Unsorted, (await Sort(ai, cfg)).ClassName); // no ai.json at all
        Assert.Empty(claude.Prompts);
    }

    [Theory]
    [InlineData("claude", "fast", "", true)]
    [InlineData("claude", "fast", "opus", true)]
    [InlineData("claude", "fast", "sonnet", false)] // fast mode is Opus's
    [InlineData("claude", "standard", "", false)]
    [InlineData("codex", "fast", "", false)] // only Claude Code has it
    [InlineData("ollama", "fast", "", false)]
    public void Fast_mode_is_only_claimed_when_Claude_Code_really_writes_the_notes_with_it(string engine, string speed, string model, bool fast)
    {
        using var dir = new TempDir();
        var s = new AiSettings { Speed = speed };
        s.ByJob["notes"] = new AiChoice(engine, model);
        s.Save(dir.Path);
        var ai = new AiJobs(dir.Path) { Checks = new FakeChecks().Installed("claude", "codex").Build() };
        Assert.Equal(fast, ai.NotesInFastMode());
    }
}
