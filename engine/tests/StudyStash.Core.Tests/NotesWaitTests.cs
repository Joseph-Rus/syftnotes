using StudyStash.Core.Ai;

namespace StudyStash.Core.Tests;

/// <summary>Gemini's free allowance used up while it writes the notes: the lecture keeps its transcript and says until
/// when, and once the allowance is back (the library's timer, or Gemini answering again) its notes are written by
/// themselves. And a first AI chosen on a lecture's page queues every lecture filed without notes.</summary>
public class NotesWaitTests
{
    static readonly string Transcript = string.Concat(Enumerable.Repeat(
        "Today we look at recursion: a function that calls itself, with a base case that stops it and a call stack that grows with every call. ", 30));

    const string Sorted = """{"class_name": "CS 101", "confidence": 0.9, "lecture_title": "Recursion", "topics": []}""";

    [Fact]
    public async Task Notes_Gemini_couldnt_write_over_its_allowance_say_until_when_and_are_written_once_it_is_back()
    {
        using var dir = new TempDir();
        var cfg = new Config(dir["home"], dir["pool"]) { OllamaEnabled = true, OllamaModel = "qwen3:8b", Classes = [new ClassDef("CS 101")] };
        Directory.CreateDirectory(cfg.Home);
        var settings = new AiSettings { Fallback = false };
        settings.ByJob["notes"] = new AiChoice("gemini");
        settings.Save(cfg.Home);
        using var store = new Store(cfg.DbPath, cfg.PoolDir);
        var gemini = new ScriptedAi("gemini", "Gemini") { Script = [new("error", "RESOURCE_EXHAUSTED: You have exhausted your capacity on this model.")] };
        // agy's own count says when it's back: Tuesday at 9 in the morning, this computer's time.
        var back = new DateTime(2099, 11, 3, 9, 0, 0, DateTimeKind.Local);
        var ai = new AiJobs(cfg.Home) { Providers = _ => gemini, Checks = new FakeChecks().Installed(Antigravity.Binary).Build(), GeminiBack = () => back.ToUniversalTime() };
        var pipeline = new Pipeline(cfg, store, (_, _, _) => Task.FromResult(Sorted), ai.SummarizeAsync, _ => { },
            notesModel: () => ai.Describe("notes", cfg), limitUntil: ai.LimitUntil);
        var limited = new List<(string, DateTime)>();
        var over = new List<string>();
        ai.Limited += (id, until) => limited.Add((id, until));
        ai.LimitOver += id =>
        {
            over.Add(id);
            if (store.RequeueLimited(Engines.Name(id)) > 0) pipeline.Wake();
        };

        store.Enqueue(new Meeting("lec-1") { Title = "Recursion", Date = "2026-10-14T09:00:00", Folder = "CS 101", Transcript = Transcript });
        Assert.Equal(1, await pipeline.RunPendingAsync());

        // Filed with its transcript and no notes, saying until when, in the student's time.
        var row = store.Get("lec-1")!;
        Assert.Equal("", row.SummaryMd ?? "");
        Assert.Equal("Your notes aren't written yet because Gemini's allowance is used up until Tue 3 Nov, 9:00 am. They're written by themselves once it's back.", row.Error);
        Assert.Equal("Gemini", Engines.WaitsForLimit(row.Error!));
        Assert.Equal("Tue 3 Nov, 9:00 am", Engines.LimitUntil(row.Error!));
        Assert.Equal([("gemini", back)], limited);
        Assert.Equal(back, ai.LimitUntil("Gemini"));

        // Another lecture while it's used up, with no free AI on this computer to fall back to: filed at once with its
        // transcript and the same reason (Gemini isn't asked, and nothing waits in the queue for an Ollama that isn't here).
        gemini.Throws = new InvalidOperationException("Gemini was asked while its allowance was used up");
        store.Enqueue(new Meeting("lec-2") { Title = "Stacks", Date = "2026-10-16T09:00:00", Folder = "CS 101", Transcript = Transcript });
        Assert.Equal(1, await pipeline.RunPendingAsync());
        Assert.Equal((Store.Done, ""), (store.Get("lec-2")!.Status, store.Get("lec-2")!.SummaryMd ?? ""));
        Assert.Equal("Gemini", Engines.WaitsForLimit(store.Get("lec-2")!.Error!));
        gemini.Throws = null;

        // The library's timer, once the allowance is back: queued again, and Gemini writes them.
        Assert.Equal(2, store.RequeueLimited("Gemini"));
        Assert.Equal(0, store.RequeueLimited("Claude"));
        // Over by now, as the timer says (agy's count, moved to the past here).
        var past = AiSettings.Load(cfg.Home);
        past.Limits["gemini"] = DateTime.Now.AddMinutes(-1).ToString("o");
        past.Save(cfg.Home);
        gemini.Script = [new("final", "## Recursion\n- A function that calls itself, until its base case.")];
        Assert.Equal(2, await pipeline.RunPendingAsync());
        row = store.Get("lec-1")!;
        Assert.Contains("A function that calls itself", row.SummaryMd);
        Assert.Equal("", row.Error ?? "");
        // Gemini answering again said the limit's over (nothing was waiting by then).
        Assert.Equal(["gemini"], over);
        Assert.Null(ai.LimitUntil("Gemini"));
    }

    [Fact]
    public void A_limit_with_no_time_known_says_for_now()
    {
        string why = Engines.NotesWaitForLimit("Claude", null);
        Assert.Equal("Your notes aren't written yet because Claude hit its usage limit for now. They're written by themselves once it's back.", why);
        Assert.Equal("Claude", Engines.WaitsForLimit(why));
        Assert.Equal("", Engines.LimitUntil(why));
        Assert.Null(Engines.WaitsForLimit("Your notes weren't written because Claude needs you to sign in again."));
    }

    [Theory]
    [InlineData("RESOURCE_EXHAUSTED", true)]
    [InlineData("You have exhausted your capacity on this model", true)]
    [InlineData("This week's Gemini allowance is used up", true)]
    [InlineData("Quota exceeded for quota metric", true)]
    [InlineData("Please sign in", false)]
    public void What_counts_as_an_allowance_used_up(string error, bool limit) => Assert.Equal(limit, Engines.LooksLikeLimit(error));

    [Fact]
    public void Agys_usage_says_when_a_used_up_allowance_is_back()
    {
        const string usedUp = """{"command":{"data":{"groups":[{"name":"Gemini models","buckets":[{"remaining_fraction":0,"window":"weekly","reset_time":"2099-11-03T17:00:00Z"}]}]}}}""";
        const string some = """{"command":{"data":{"groups":[{"name":"Gemini models","buckets":[{"remaining_fraction":0.4,"window":"weekly","reset_time":"2099-11-03T17:00:00Z"}]}]}}}""";
        Assert.Equal(new DateTime(2099, 11, 3, 17, 0, 0, DateTimeKind.Utc), Antigravity.BackOf(usedUp));
        Assert.Null(Antigravity.BackOf(some));
        Assert.Null(Antigravity.BackOf("not json"));
    }

    [Fact]
    public async Task Choosing_a_first_AI_queues_every_lecture_filed_without_notes()
    {
        using var dir = new TempDir();
        // No AI here yet: the library writes no notes, and files each lecture with its transcript.
        var cfg = new Config(dir["home"], dir["pool"]) { SummaryEnabled = false, OllamaEnabled = false, Classes = [new ClassDef("CS 101")] };
        Directory.CreateDirectory(cfg.Home);
        using var store = new Store(cfg.DbPath, cfg.PoolDir);
        var pipeline = new Pipeline(cfg, store, (_, _, _) => Task.FromResult(Sorted), (_, _) => Task.FromResult("never asked"), _ => { });
        store.Enqueue(new Meeting("lec-1") { Title = "Recursion", Date = "2026-10-14T09:00:00", Folder = "CS 101", Transcript = Transcript });
        store.Enqueue(new Meeting("lec-2") { Title = "Stacks", Date = "2026-10-16T09:00:00", Folder = "CS 101", Transcript = Transcript });
        Assert.Equal(2, await pipeline.RunPendingAsync());
        Assert.All(new[] { "lec-1", "lec-2" }, id => Assert.Equal(("", Store.Done), (store.Get(id)!.SummaryMd ?? "", store.Get(id)!.Status)));

        // Continue with Google on one of their pages: both are queued for notes.
        Assert.Equal(2, store.RequeueMissing());
        Assert.All(new[] { "lec-1", "lec-2" }, id => Assert.Equal(Store.Queued, store.Get(id)!.Status));
    }
}
