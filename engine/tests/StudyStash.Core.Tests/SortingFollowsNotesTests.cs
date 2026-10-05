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
