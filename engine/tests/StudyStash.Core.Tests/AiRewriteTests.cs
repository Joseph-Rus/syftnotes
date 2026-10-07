using StudyStash.Core.Ai;
using StudyStash.Library;

namespace StudyStash.Core.Tests;

/// <summary>/api/v2/ai/rewrite/* end to end, through TestSite and AiRemote: a rewrite runs with the engine asked
/// for, the current notes stay until the student chooses, and it survives a restart. No test here ever touches a
/// real AI account, a real terminal, or Ollama: every engine is a <see cref="ScriptedAi"/>.</summary>
public class AiRewriteTests
{
    // Longer than Summarize.MinTranscriptChars, so Start doesn't refuse it as "too short to write from".
    const string Sentence = "Today we covered the cell membrane and how osmosis moves water across it, and worked "
        + "through the sodium-potassium pump and why it needs energy to run against the gradient. ";
    const string Transcript = Sentence + Sentence + Sentence + Sentence + Sentence + Sentence + Sentence + Sentence
        + Sentence + Sentence + Sentence + Sentence;

    static Config Cfg(TempDir dir) => new(dir["home"], dir["pool"]) { PoolPassword = "pw", OllamaEnabled = true, OllamaModel = "qwen3:8b" };

    static async Task<TestSite> Site(Config cfg, Store store, AiJobs ai) => await TestSite.StartAsync(b => LibraryWeb.Build(b, cfg, store,
        new Pipeline(cfg, store, log: _ => { }), new LibraryWebOptions
        {
            ListModels = _ => Task.FromResult<List<(string, double)>?>(null), Tailscale = () => new TailscaleInfo(),
            Latest = _ => Task.FromResult<Release?>(null), Ai = ai,
        }));

    static void Seed(Store store, string id = "lec-1", string transcript = Transcript) => store.Save(
        new Meeting(id) { Title = "Cell membranes", Date = "2026-09-01T09:00:00", Owner = "Sam", Transcript = transcript },
        new Classification("Bio 110", 0.9, "ollama", "Cell membranes", ["cells"]),
        summaryMd: "Old notes about cells.", summaryModel: "qwen3:8b");

    /// <summary>Waits (no more than 5s, polling) for the rewrite to reach a state the test is looking for, instead
    /// of a fixed sleep — the job runs on its own background Task.</summary>
    static async Task<RewriteInfo> UntilAsync(AiRemote remote, string id, Func<RewriteInfo, bool> matches)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (true)
        {
            var info = await remote.RewriteAsync(id) ?? throw new InvalidOperationException("the library doesn't know /rewrite");
            if (matches(info)) return info;
            if (DateTime.UtcNow > deadline) throw new TimeoutException($"never got there — last state was '{info.State}'");
            await Task.Delay(20);
        }
    }

    [Fact]
    public async Task A_rewrite_runs_then_the_old_notes_stay_until_you_use_the_new_ones()
    {
        using var dir = new TempDir();
        var cfg = Cfg(dir);
        using var store = new Store(cfg.DbPath, cfg.PoolDir);
        Seed(store);
        var gate = new TaskCompletionSource<bool>();
        var claude = new ScriptedAi("claude", "Claude") { Gate = gate, Script = [new AiEvent("final", "New, better notes.")] };
        var ai = new AiJobs(cfg.Home) { Providers = _ => claude, Checks = new FakeChecks().Installed("claude").Build() };
        await using var site = await Site(cfg, store, ai);
        var remote = new AiRemote("http://localhost", "pw", site.Client);

        var started = await remote.RewriteStartAsync("lec-1", "claude");
        Assert.NotNull(started);
        Assert.Equal("working", started!.State);
        Assert.Equal("claude", started.Engine);

        var working = await UntilAsync(remote, "lec-1", i => i.State != "none");
        Assert.Equal("working", working.State);
        Assert.Equal("Old notes about cells.", working.Current!.Markdown); // untouched while the job runs

        gate.SetResult(true);
        var ready = await UntilAsync(remote, "lec-1", i => i.State != "working");
        Assert.Equal("ready", ready.State);
        Assert.Equal("New, better notes.", ready.Draft!.Markdown);
        Assert.Equal("Claude Code", ready.Draft.By);
        Assert.Equal("Old notes about cells.", ready.Current!.Markdown); // still the old notes: nobody chose yet

        var used = await remote.RewriteUseAsync("lec-1");
        Assert.NotNull(used);
        Assert.Equal("none", used!.State); // decided: nothing left pending
        Assert.Equal("New, better notes.", used.Current!.Markdown);
        Assert.Equal("Claude Code", used.Current.By); // notes_model, read back through WhoWrote

        var row = store.Get("lec-1")!;
        Assert.Equal("Claude Code", Engines.WhoWrote(row.SummaryModel ?? ""));
        Assert.Contains("New, better notes.", File.ReadAllText(row.MdPath!)); // the .md file itself was rewritten
    }

    [Fact]
    public async Task A_rewrite_is_written_with_the_lectures_attachments_and_using_it_counts_them_used()
    {
        using var dir = new TempDir();
        var cfg = Cfg(dir);
        using var store = new Store(cfg.DbPath, cfg.PoolDir);
        Seed(store);
        File.WriteAllText(Path.Combine(store.AttachmentsDir("Bio 110"), "my notes.pdf"), "x");
        store.AddAttachment(new Attachment("a1", "my notes.pdf", "my notes.pdf", "Bio 110", "lec-1", 1, "application/pdf", "2026-09-01T00:00:00Z"));
        store.SetAttachmentText("a1", "Sodium-potassium pump: 3 Na out, 2 K in.");
        Assert.True(store.AttachmentsUnused("lec-1"));
        var claude = new ScriptedAi("claude", "Claude") { Script = [new AiEvent("final", "Notes with the pump.")] };
        var ai = new AiJobs(cfg.Home) { Providers = _ => claude, Checks = new FakeChecks().Installed("claude").Build() };
        await using var site = await Site(cfg, store, ai);
        var remote = new AiRemote("http://localhost", "pw", site.Client);

        await remote.RewriteStartAsync("lec-1", "claude");
        await UntilAsync(remote, "lec-1", i => i.State == "ready");
        Assert.Contains(claude.Prompts, p => p.Contains("The student's own notes (my notes.pdf):\nSodium-potassium pump: 3 Na out, 2 K in."));
        Assert.True(store.AttachmentsUnused("lec-1")); // a draft isn't the notes yet
        await remote.RewriteUseAsync("lec-1");
        Assert.False(store.AttachmentsUnused("lec-1"));
    }

    [Fact]
    public async Task Keep_old_drops_the_draft_and_leaves_the_notes_exactly_as_they_were()
    {
        using var dir = new TempDir();
        var cfg = Cfg(dir);
        using var store = new Store(cfg.DbPath, cfg.PoolDir);
        Seed(store);
        var claude = new ScriptedAi("claude", "Claude") { Script = [new AiEvent("final", "New draft nobody wants.")] };
        var ai = new AiJobs(cfg.Home) { Providers = _ => claude, Checks = new FakeChecks().Installed("claude").Build() };
        await using var site = await Site(cfg, store, ai);
        var remote = new AiRemote("http://localhost", "pw", site.Client);

        await remote.RewriteStartAsync("lec-1", "claude");
        await UntilAsync(remote, "lec-1", i => i.State == "ready");

        var kept = await remote.RewriteKeepAsync("lec-1");
        Assert.NotNull(kept);
        Assert.Equal("none", kept!.State);
        Assert.Equal("Old notes about cells.", kept.Current!.Markdown);

        var row = store.Get("lec-1")!;
        Assert.Equal("Old notes about cells.", row.SummaryMd); // never touched
    }

    [Fact]
    public async Task Cancel_stops_the_engine_and_the_state_becomes_cancelled()
    {
        using var dir = new TempDir();
        var cfg = Cfg(dir);
        using var store = new Store(cfg.DbPath, cfg.PoolDir);
        Seed(store);
        var claude = new ScriptedAi("claude", "Claude") { Hang = true }; // waits on the run's own token, then throws
        var ai = new AiJobs(cfg.Home) { Providers = _ => claude, Checks = new FakeChecks().Installed("claude").Build() };
        await using var site = await Site(cfg, store, ai);
        var remote = new AiRemote("http://localhost", "pw", site.Client);

        await remote.RewriteStartAsync("lec-1", "claude");
        await UntilAsync(remote, "lec-1", i => i.State == "working");

        var cancelled = await remote.RewriteCancelAsync("lec-1");
        Assert.NotNull(cancelled);
        Assert.Equal("cancelled", cancelled!.State);
        // Stays cancelled: the hung engine's own throw (once its token fires) must never flip this back to "failed".
        await Task.Delay(200);
        Assert.Equal("cancelled", (await remote.RewriteAsync("lec-1"))!.State);
        Assert.Equal("Old notes about cells.", (await remote.RewriteAsync("lec-1"))!.Current!.Markdown);
    }

    [Fact]
    public async Task An_engine_error_ends_the_job_as_failed_and_the_notes_are_unchanged()
    {
        using var dir = new TempDir();
        var cfg = Cfg(dir);
        using var store = new Store(cfg.DbPath, cfg.PoolDir);
        Seed(store);
        var claude = new ScriptedAi("claude", "Claude") { Throws = new InvalidOperationException("Claude: usage limit reached") };
        var ai = new AiJobs(cfg.Home) { Providers = _ => claude, Checks = new FakeChecks().Installed("claude").Build() };
        await using var site = await Site(cfg, store, ai);
        var remote = new AiRemote("http://localhost", "pw", site.Client);

        await remote.RewriteStartAsync("lec-1", "claude");
        var failed = await UntilAsync(remote, "lec-1", i => i.State == "failed");
        Assert.Contains("usage limit", failed.Error);
        Assert.Equal("Old notes about cells.", failed.Current!.Markdown);

        var refused = await Assert.ThrowsAsync<LibraryRefusedException>(() => remote.RewriteUseAsync("lec-1"));
        Assert.Equal(409, refused.Status);
    }

    [Fact]
    public async Task A_second_start_while_one_is_already_running_is_refused()
    {
        using var dir = new TempDir();
        var cfg = Cfg(dir);
        using var store = new Store(cfg.DbPath, cfg.PoolDir);
        Seed(store);
        var claude = new ScriptedAi("claude", "Claude") { Gate = new TaskCompletionSource<bool>() };
        var ai = new AiJobs(cfg.Home) { Providers = _ => claude, Checks = new FakeChecks().Installed("claude").Build() };
        await using var site = await Site(cfg, store, ai);
        var remote = new AiRemote("http://localhost", "pw", site.Client);

        await remote.RewriteStartAsync("lec-1", "claude");
        await UntilAsync(remote, "lec-1", i => i.State == "working");

        var refused = await Assert.ThrowsAsync<LibraryRefusedException>(() => remote.RewriteStartAsync("lec-1", "claude"));
        Assert.Equal(409, refused.Status);
    }

    [Fact]
    public async Task An_unknown_lecture_is_404()
    {
        using var dir = new TempDir();
        var cfg = Cfg(dir);
        using var store = new Store(cfg.DbPath, cfg.PoolDir);
        var ai = new AiJobs(cfg.Home) { Checks = new FakeChecks().Installed("claude").Build() };
        await using var site = await Site(cfg, store, ai);
        var remote = new AiRemote("http://localhost", "pw", site.Client);

        var refused = await Assert.ThrowsAsync<LibraryRefusedException>(() => remote.RewriteStartAsync("nope", "claude"));
        Assert.Equal(404, refused.Status);
        var refusedGet = await Assert.ThrowsAsync<LibraryRefusedException>(() => remote.RewriteAsync("nope"));
        Assert.Equal(404, refusedGet.Status);
    }

    [Fact]
    public async Task A_lecture_with_no_transcript_or_an_uninstalled_engine_is_refused()
    {
        using var dir = new TempDir();
        var cfg = Cfg(dir);
        using var store = new Store(cfg.DbPath, cfg.PoolDir);
        Seed(store, "short", transcript: "too short to write notes from");
        Seed(store, "lec-1");
        var ai = new AiJobs(cfg.Home) { Checks = new FakeChecks().Installed("claude").Build() };
        await using var site = await Site(cfg, store, ai);
        var remote = new AiRemote("http://localhost", "pw", site.Client);

        var noTranscript = await Assert.ThrowsAsync<LibraryRefusedException>(() => remote.RewriteStartAsync("short", "claude"));
        Assert.Equal(409, noTranscript.Status);

        var notInstalled = await Assert.ThrowsAsync<LibraryRefusedException>(() => remote.RewriteStartAsync("lec-1", "codex"));
        Assert.Equal(409, notInstalled.Status);
        Assert.Contains("Codex", notInstalled.Message);
    }

    [Fact]
    public async Task A_ready_draft_survives_a_restart()
    {
        using var dir = new TempDir();
        var cfg = Cfg(dir);
        using var store = new Store(cfg.DbPath, cfg.PoolDir);
        Seed(store);
        var claude = new ScriptedAi("claude", "Claude") { Script = [new AiEvent("final", "Draft that must survive.")] };
        var checks = new FakeChecks().Installed("claude").Build();
        var rewrites1 = new Rewrites(cfg, store, new AiJobs(cfg.Home) { Providers = _ => claude, Checks = checks });

        rewrites1.Start("lec-1", "claude");
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (rewrites1.Get("lec-1").State != "ready")
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException("the draft never became ready");
            await Task.Delay(20);
        }

        // A fresh Rewrites over the same home — as if the library had just restarted — reads the same job back.
        var rewrites2 = new Rewrites(cfg, store, new AiJobs(cfg.Home) { Checks = checks });
        var info = rewrites2.Get("lec-1");
        Assert.Equal("ready", info.State);
        Assert.Equal("Draft that must survive.", info.Draft!.Markdown);
        Assert.Equal("claude", info.Engine);
    }

    [Fact]
    public async Task A_job_still_working_when_the_library_stopped_comes_back_failed()
    {
        using var dir = new TempDir();
        var cfg = Cfg(dir);
        using var store = new Store(cfg.DbPath, cfg.PoolDir);
        Seed(store);
        var claude = new ScriptedAi("claude", "Claude") { Gate = new TaskCompletionSource<bool>() }; // never lets go
        var checks = new FakeChecks().Installed("claude").Build();
        var rewrites1 = new Rewrites(cfg, store, new AiJobs(cfg.Home) { Providers = _ => claude, Checks = checks });

        rewrites1.Start("lec-1", "claude");
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (rewrites1.Get("lec-1").State != "working")
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException("never reached working");
            await Task.Delay(20);
        }

        var rewrites2 = new Rewrites(cfg, store, new AiJobs(cfg.Home) { Checks = checks });
        var info = rewrites2.Get("lec-1");
        Assert.Equal("failed", info.State);
        Assert.Contains("restarted", info.Error);
    }

    [Fact]
    public async Task An_edit_by_hand_becomes_the_notes_everywhere_and_never_saves_over_ones_it_hasnt_seen()
    {
        using var dir = new TempDir();
        var cfg = Cfg(dir);
        using var store = new Store(cfg.DbPath, cfg.PoolDir);
        Seed(store);
        var ai = new AiJobs(cfg.Home) { Providers = _ => new ScriptedAi("claude", "Claude"), Checks = new FakeChecks().Build() };
        await using var site = await Site(cfg, store, ai);
        var remote = new AiRemote("http://localhost", "pw", site.Client);
        string was = Notes.Fingerprint("Old notes about cells.");

        var saved = await remote.EditNotesAsync("lec-1", "Old notes about cells, and the mitochondria.\r\n", was);

        Assert.Equal("Old notes about cells, and the mitochondria.", saved!.Current!.Markdown);
        Assert.Equal("none", saved.State);
        var row = store.Get("lec-1")!;
        Assert.Equal("qwen3:8b", row.SummaryModel); // still its writer's
        Assert.Equal(Store.Done, row.Status);
        Assert.Contains("and the mitochondria.", File.ReadAllText(row.MdPath!));
        Assert.Contains(store.SearchPassages("mitochondria"), hit => hit.Note.Id == "lec-1");

        // Another edit that started from the notes as they were before that one: refused, and nothing changes.
        var stale = await Assert.ThrowsAsync<LibraryRefusedException>(() => remote.EditNotesAsync("lec-1", "Something else.", was));
        Assert.Equal(412, stale.Status);
        Assert.Equal("Old notes about cells, and the mitochondria.", store.Get("lec-1")!.SummaryMd);
        await Assert.ThrowsAsync<LibraryRefusedException>(() => remote.EditNotesAsync("lec-1", "  \n", null)); // empty notes aren't notes
    }

    [Fact]
    public void An_edit_by_hand_keeps_what_was_written_in_the_note_file_in_another_app()
    {
        using var dir = new TempDir();
        var cfg = Cfg(dir);
        using var store = new Store(cfg.DbPath, cfg.PoolDir);
        Seed(store);
        string path = store.Get("lec-1")!.MdPath!;
        string Read() => File.ReadAllText(path).Replace("\r\n", "\n");
        string Seen() => Notes.Fingerprint(store.Get("lec-1")!.SummaryMd!);

        // Lines of the student's own in the file, outside its summary (written in Obsidian, say). An edit in Study
        // Stash used to write the whole file afresh, and they were gone; now only the summary is replaced.
        File.WriteAllText(path, Read().Replace("## Transcript", "## My questions\n\nAsk about the Krebs cycle.\n\n## Transcript"));
        Assert.Equal(NotesEdited.Saved, store.EditNotes("lec-1", "## Cells\n\nOld notes about cells, and the mitochondria.", Seen()));
        Assert.Contains("\n## Summary\n\n### Cells\n\nOld notes about cells, and the mitochondria.\n\n_Written by qwen3:8b", Read());
        Assert.Contains("\n## My questions\n\nAsk about the Krebs cycle.\n\n## Transcript\n", Read());
        // The file's summary is the library's notes, a heading level down: that's no edit of its own.
        Assert.Equal(NotesEdited.Saved, store.EditNotes("lec-1", "## Cells\n\nNotes about cells, and the mitochondria.", Seen()));

        // The summary itself changed in the file. The app never showed that (it shows the library's notes), so an edit
        // that started from the ones before isn't saved over it: it becomes the library's notes, to be seen first.
        File.WriteAllText(path, Read().Replace("and the mitochondria.", "and the mitochondria (the powerhouse)."));
        Assert.Equal(NotesEdited.Changed, store.EditNotes("lec-1", "Something typed in the app.", Seen()));
        Assert.Equal("## Cells\n\nNotes about cells, and the mitochondria (the powerhouse).", store.Get("lec-1")!.SummaryMd);
        Assert.Contains("the powerhouse", Read());
        Assert.Contains(store.SearchPassages("powerhouse"), hit => hit.Note.Id == "lec-1");

        // Seen, then saved again: the student's choice. The file's own lines are still there.
        Assert.Equal(NotesEdited.Saved, store.EditNotes("lec-1", "Something typed in the app.", Seen()));
        Assert.Contains("\n## Summary\n\nSomething typed in the app.\n\n_Written by qwen3:8b", Read());
        Assert.DoesNotContain("powerhouse", Read());
        Assert.Contains("Ask about the Krebs cycle.", Read());

        // A file with no summary left in it is kept exactly as it is; the app, search and Ask still get the edit.
        File.WriteAllText(path, "Just my own page now.\n");
        Assert.Equal(NotesEdited.Saved, store.EditNotes("lec-1", "Typed in the app again.", Seen()));
        Assert.Equal("Just my own page now.\n", Read());
        Assert.Equal("Typed in the app again.", store.Get("lec-1")!.SummaryMd);
    }
}
