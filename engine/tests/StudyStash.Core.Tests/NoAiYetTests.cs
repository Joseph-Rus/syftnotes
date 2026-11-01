using StudyStash.Core.Ai;

namespace StudyStash.Core.Tests;

/// <summary>A fresh library has no AI chosen to write the notes (it used to say Ollama, installed or not): its lectures
/// wait for a choice, the first AI signed in becomes the writer, and an old ai.json that only said Ollama by default is
/// moved to "nobody chosen" once, while every real pick is kept.</summary>
public class NoAiYetTests
{
    static readonly string Transcript = string.Concat(Enumerable.Repeat(
        "Today we look at recursion: a function that calls itself, with a base case that stops it and a call stack that grows with every call. ", 30));

    const string Sorted = """{"class_name": "CS 101", "confidence": 0.9, "lecture_title": "Recursion", "topics": []}""";

    static Config Library(TempDir dir)
    {
        var cfg = new Config(dir["home"], dir["pool"]) { OllamaEnabled = true, OllamaModel = "qwen3:8b", Classes = [new ClassDef("CS 101")] };
        Directory.CreateDirectory(cfg.Home);
        return cfg;
    }

    /// <summary>An ai.json as an older Syft Notes saved it: no version, and "ollama" whether it was chosen or not.</summary>
    static void OldAiJson(Config cfg, string provider, string extra = "") =>
        File.WriteAllText(AiSettings.PathIn(cfg.Home), $$"""{ "provider": "{{provider}}", "fallback": true{{extra}} }""");

    static bool Migrate(Config cfg, bool installed, bool wrote) =>
        new AiJobs(cfg.Home) { Checks = (installed ? new FakeChecks().Ollama() : new FakeChecks()).Build() }.MigrateAiSettings(() => wrote);

    [Fact]
    public void An_old_ai_json_that_only_said_ollama_with_none_here_and_none_written_becomes_nobody_chosen()
    {
        using var dir = new TempDir();
        var cfg = Library(dir);
        OldAiJson(cfg, "ollama");
        Assert.True(Migrate(cfg, installed: false, wrote: false));
        var s = AiSettings.Load(cfg.Home);
        Assert.Equal(("", false, AiSettings.CurrentVersion), (s.Provider, s.NotesChosen, s.Version));
        Assert.False(Migrate(cfg, installed: false, wrote: false)); // once
    }

    [Fact]
    public void An_ollama_that_is_installed_or_wrote_notes_or_has_picks_beside_it_stays()
    {
        foreach (var (installed, wrote, extra) in new[] { (true, false, ""), (false, true, ""), (false, false, ", \"models\": {\"ollama\": \"qwen3:8b\"}"),
                     (false, false, ", \"by_job\": {\"ask\": {\"provider\": \"claude\", \"model\": \"\"}}") })
        {
            using var dir = new TempDir();
            var cfg = Library(dir);
            OldAiJson(cfg, "ollama", extra);
            Assert.False(Migrate(cfg, installed, wrote));
            Assert.Equal("ollama", AiSettings.Load(cfg.Home).Provider);
        }
    }

    [Fact]
    public void A_pick_of_another_ai_is_kept_and_a_deliberate_ollama_saved_by_this_version_is_never_taken_for_the_default()
    {
        using var dir = new TempDir();
        var cfg = Library(dir);
        OldAiJson(cfg, "claude");
        Assert.False(Migrate(cfg, installed: false, wrote: false));
        Assert.Equal("claude", AiSettings.Load(cfg.Home).Provider);

        // The student picks Ollama today (saved, so stamped), and restarts the library with Ollama gone: still their pick.
        new AiSettings { Provider = "ollama" }.Save(cfg.Home);
        Assert.False(Migrate(cfg, installed: false, wrote: false));
        Assert.Equal("ollama", AiSettings.Load(cfg.Home).Provider);
    }

    [Fact]
    public void A_library_with_no_ai_json_has_nothing_to_migrate_and_nobody_chosen()
    {
        using var dir = new TempDir();
        var cfg = Library(dir);
        Assert.False(Migrate(cfg, installed: false, wrote: false));
        Assert.False(File.Exists(AiSettings.PathIn(cfg.Home)));
        Assert.False(AiSettings.Load(cfg.Home).NotesChosen);
    }

    [Fact]
    public async Task Only_notes_Ollama_or_an_unknown_writer_wrote_count_against_migrating()
    {
        using var dir = new TempDir();
        var cfg = Library(dir);
        using var store = new Store(cfg.DbPath, cfg.PoolDir);
        Assert.False(store.HasNotesFromOllamaOrUnknown());
        string writer = "Claude sonnet";
        var pipeline = new Pipeline(cfg, store, (_, _, _) => Task.FromResult(Sorted), (_, _) => Task.FromResult("## Summary\nRecursion."), _ => { }, notesModel: () => writer);
        store.Enqueue(new Meeting("lec-1") { Title = "Recursion", Date = "2026-10-14T09:00:00", Folder = "CS 101", Transcript = Transcript });
        await pipeline.RunPendingAsync();
        Assert.False(store.HasNotesFromOllamaOrUnknown()); // Claude wrote them: Ollama never did

        writer = "qwen3:8b";
        store.Enqueue(new Meeting("lec-2") { Title = "Stacks", Date = "2026-10-16T09:00:00", Folder = "CS 101", Transcript = Transcript });
        await pipeline.RunPendingAsync();
        Assert.True(store.HasNotesFromOllamaOrUnknown());
    }

    [Fact]
    public async Task With_nobody_chosen_a_lecture_is_filed_with_its_transcript_and_says_why_and_is_written_once_an_AI_is_chosen()
    {
        using var dir = new TempDir();
        var cfg = Library(dir);
        using var store = new Store(cfg.DbPath, cfg.PoolDir);
        var claude = new ScriptedAi("claude", "Claude") { Script = [new("final", "## Summary\nRecursion calls itself.")] };
        var ai = new AiJobs(cfg.Home) { Providers = _ => claude, Checks = new FakeChecks().Installed("claude").Build() };
        var log = new List<string>();
        var pipeline = new Pipeline(cfg, store, (_, _, _) => Task.FromResult(Sorted), ai.SummarizeAsync, log.Add, notesModel: () => ai.Describe("notes", cfg));
        store.Enqueue(new Meeting("lec-1") { Title = "Recursion", Date = "2026-10-14T09:00:00", Folder = "CS 101", Transcript = Transcript });
        Assert.Equal(1, await pipeline.RunPendingAsync());

        var row = store.Get("lec-1")!;
        Assert.Equal((Store.Done, "", Engines.NotesNeedAnAi), (row.Status, row.SummaryMd ?? "", row.Error));
        Assert.DoesNotContain(log, l => l.Contains("failed"));
        Assert.Empty(claude.Prompts); // nothing was asked of any AI
        Assert.Equal("", ai.Describe("notes", cfg));

        // The student chooses Claude: that lecture, and only lectures waiting for a choice, are queued.
        new AiSettings { Provider = "claude" }.Save(cfg.Home);
        Assert.Equal(1, store.RequeueNoAi());
        Assert.Equal(Store.Queued, store.Get("lec-1")!.Status);
        await pipeline.RunPendingAsync();
        Assert.Contains("Recursion calls itself", store.Get("lec-1")!.SummaryMd);
        Assert.Equal(0, store.RequeueNoAi());
    }

    [Fact]
    public void The_first_AI_signed_in_becomes_the_writer_when_none_was_chosen_and_a_later_one_changes_nothing()
    {
        using var dir = new TempDir();
        var cfg = Library(dir);
        var ai = new AiJobs(cfg.Home);
        int chosen = 0;
        ai.NotesAiChosen += () => chosen++;
        ai.MarkSignedIn("gemini");
        Assert.Equal(("gemini", true, 1), (AiSettings.Load(cfg.Home).Provider, AiSettings.Load(cfg.Home).NotesChosen, chosen));
        ai.MarkSignedIn("claude");
        Assert.Equal(("gemini", 1), (AiSettings.Load(cfg.Home).Provider, chosen));
    }

    [Fact]
    public async Task A_fresh_library_overview_names_nobody_and_reports_no_problem_for_an_ollama_that_is_not_here()
    {
        var cfg = new Config("/lib", "/pool");
        var overview = await Engines.StatusAsync(new AiSettings(), cfg, new FakeChecks().Build());
        Assert.Equal(("", ""), (overview.Notes, overview.Ask));
        Assert.Empty(overview.Problems);
        Assert.Equal("", overview.DiagramsBy);
    }

    [Fact]
    public void Moving_a_library_brings_its_AI_over_only_when_this_one_never_chose_one()
    {
        var body = System.Text.Json.Nodes.JsonNode.Parse("""{ "settings": true, "ai": { "provider": "claude" } }""")!.AsObject();
        bool Takes(Action<Config> setup)
        {
            using var dir = new TempDir();
            var cfg = Library(dir);
            setup(cfg);
            var said = LibraryMove.ImportLibrary(cfg, (System.Text.Json.Nodes.JsonObject)body.DeepClone());
            return said["settings"]!.GetValue<bool>();
        }
        Assert.True(Takes(_ => { }));                                    // no ai.json
        Assert.True(Takes(c => new AiSettings().Save(c.Home)));            // saved with nobody chosen
        Assert.True(Takes(c => OldAiJson(c, "ollama")));                   // an old file on the plain default
        Assert.False(Takes(c => new AiSettings { Provider = "ollama" }.Save(c.Home))); // a deliberate Ollama
        Assert.False(Takes(c => OldAiJson(c, "gemini")));
    }
}
