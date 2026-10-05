using System.Net;
using System.Net.Sockets;

namespace StudyStash.Core.Tests;

/// <summary>The library's pipeline while its notes engine is away: lectures wait in the queue, and get their notes
/// when it's back.</summary>
public class PipelineTests
{
    const string Line = "Recursion is a function calling itself on a smaller piece of the problem.\n";

    static string Lines(int n) => string.Concat(Enumerable.Repeat(Line, n));

    static Config CfgFor(TempDir dir) => new(dir["home"], dir["pool"])
    {
        OllamaModel = "small:1b", SummaryModel = "big:35b", Classes = [new ClassDef("CS 101"), new ClassDef("BIO 110")],
    };

    static readonly SortChatFn Sort = (_, _, _) =>
        Task.FromResult("""{"class_name": "CS 101", "confidence": 0.9, "lecture_title": "Recursion", "topics": ["recursion"]}""");

    static readonly Action<string> Quiet = _ => { };

    static HttpRequestException Refused() =>
        new("Connection refused (127.0.0.1:11434)", new SocketException((int)SocketError.ConnectionRefused));

    [Fact]
    public async Task An_engine_that_isnt_answering_leaves_the_lecture_queued_and_says_so()
    {
        using var dir = new TempDir();
        var cfg = CfgFor(dir);
        using var store = new Store(cfg.DbPath, cfg.PoolDir);
        bool up = false;
        int asked = 0;
        var p = new Pipeline(cfg, store, Sort, (_, _) =>
        {
            asked++;
            return up ? Task.FromResult("## Summary\nRecursion, and the midterm.") : throw Refused();
        }, Quiet);
        store.Enqueue(new Meeting("l1") { Title = "CS 101 lecture", Transcript = Lines(40) });
        store.Enqueue(new Meeting("l2") { Title = "CS 101 lecture", Transcript = Lines(40) });

        Assert.Equal(0, await p.RunPendingAsync());
        // The pass ends at the first one: the second isn't tried against an engine that's away.
        Assert.Equal(1, asked);
        Assert.Equal("queued", store.Get("l1")!.Status);
        Assert.Equal("queued", store.Get("l2")!.Status);
        Assert.Null(store.Get("l1")!.MdPath);
        Assert.Equal("Ollama isn't answering on your library. New lectures wait and get their notes when it's back.", p.EngineProblem);
        Assert.Equal(TimeSpan.FromSeconds(60), p.Pause);

        up = true;
        Assert.Equal(2, await p.RunPendingAsync());
        var row = store.Get("l1")!;
        Assert.Equal(("done", "CS 101"), (row.Status, row.ClassName));
        Assert.StartsWith("## Summary", row.SummaryMd);
        Assert.Contains("Recursion, and the midterm.", Py.ReadText(row.MdPath!));
        Assert.Null(p.EngineProblem);
        Assert.Equal(TimeSpan.FromSeconds(30), p.Pause);
    }

    [Fact]
    public async Task The_log_says_when_the_notes_were_written_in_fast_mode()
    {
        using var dir = new TempDir();
        var cfg = CfgFor(dir);
        using var store = new Store(cfg.DbPath, cfg.PoolDir);
        var log = new List<string>();
        bool fast = true;
        var p = new Pipeline(cfg, store, Sort, (_, _) => Task.FromResult("## Summary\nRecursion."), log.Add,
            notesModel: () => "Claude opus", notesFast: () => fast);
        store.Enqueue(new Meeting("l1") { Title = "First", Transcript = Lines(40) });
        store.Enqueue(new Meeting("l2") { Title = "Second", Transcript = Lines(40) });
        await p.RunPendingAsync();
        fast = false;
        store.Enqueue(new Meeting("l3") { Title = "Third", Transcript = Lines(40) });
        await p.RunPendingAsync();

        var said = log.Where(l => l.Contains("summarized", StringComparison.Ordinal)).ToList();
        Assert.Equal(3, said.Count);
        Assert.Contains("with Claude opus in fast mode in ", said[0]);
        Assert.DoesNotContain("fast mode", said[2]);
        Assert.Contains("with Claude opus in ", said[2]);
    }

    [Fact]
    public async Task No_answer_in_time_waits_too_and_the_engine_is_named()
    {
        using var dir = new TempDir();
        var cfg = CfgFor(dir);
        using var store = new Store(cfg.DbPath, cfg.PoolDir);
        var p = new Pipeline(cfg, store, Sort, (_, _) => throw new TimeoutException("Ollama did not answer within 600 s"), Quiet,
            notesEngine: () => "Ollama on the Mac mini");
        store.Enqueue(new Meeting("l1") { Title = "Lecture", Transcript = Lines(40) });
        await p.RunPendingAsync();
        Assert.Equal("queued", store.Get("l1")!.Status);
        Assert.StartsWith("Ollama on the Mac mini isn't answering", p.EngineProblem);
    }

    /// <summary>Clocks a test moves by hand: the wall clock, and the one that stops while the computer sleeps.</summary>
    sealed class Clocks
    {
        public DateTime Wall = new(2026, 9, 21, 10, 0, 0, DateTimeKind.Utc);
        public TimeSpan Awake = TimeSpan.FromHours(3);
        public SleepClock Clock => new(() => Wall, () => Awake);

        /// <summary>The lid closes for <paramref name="asleep"/>, after <paramref name="working"/> of work.</summary>
        public void Sleep(TimeSpan asleep, TimeSpan working)
        {
            Wall += asleep + working;
            Awake += working;
        }
    }

    [Fact]
    public void Sleep_is_when_the_wall_clock_ran_on_without_the_awake_one()
    {
        var c = new Clocks();
        var slept = c.Clock.Start();
        Assert.False(slept());
        c.Sleep(TimeSpan.Zero, TimeSpan.FromHours(2)); // two busy hours, awake all along
        Assert.False(slept());
        c.Sleep(TimeSpan.FromSeconds(30), TimeSpan.Zero); // a blink: not worth calling sleep
        Assert.False(slept());
        c.Sleep(TimeSpan.FromHours(7), TimeSpan.Zero); // the night
        Assert.True(slept());
        Assert.False(c.Clock.Start()()); // a new watch starts from now
    }

    [Fact]
    public async Task Notes_cut_off_by_sleep_are_written_again_after_waking_not_filed_without_them()
    {
        using var dir = new TempDir();
        var cfg = CfgFor(dir);
        using var store = new Store(cfg.DbPath, cfg.PoolDir);
        var clocks = new Clocks();
        int asked = 0;
        var p = new Pipeline(cfg, store, Sort, (_, _) =>
        {
            asked++;
            if (asked > 1) return Task.FromResult("## Summary\nRecursion, and the midterm.");
            // Halfway through, the lid closes for the night; on waking the engine's connection has gone.
            clocks.Sleep(TimeSpan.FromHours(8), TimeSpan.FromMinutes(2));
            throw new InvalidOperationException("Claude Code: API Error: Connection error.");
        }, Quiet, sleep: clocks.Clock);
        store.Enqueue(new Meeting("l1") { Title = "CS 101 lecture", Transcript = Lines(40) });

        Assert.Equal(0, await p.RunPendingAsync());
        var waiting = store.Get("l1")!;
        Assert.Equal("queued", waiting.Status);
        Assert.True(string.IsNullOrEmpty(waiting.Error));
        Assert.Null(waiting.MdPath);
        Assert.Null(p.EngineProblem); // the engine is fine: the computer was asleep

        Assert.Equal(1, await p.RunPendingAsync());
        var row = store.Get("l1")!;
        Assert.Equal(("done", "CS 101"), (row.Status, row.ClassName));
        Assert.StartsWith("## Summary", row.SummaryMd);
        Assert.Equal(2, asked);
    }

    [Fact]
    public async Task A_failure_while_awake_still_files_the_lecture_without_notes()
    {
        using var dir = new TempDir();
        var cfg = CfgFor(dir);
        using var store = new Store(cfg.DbPath, cfg.PoolDir);
        var clocks = new Clocks();
        var p = new Pipeline(cfg, store, Sort, (_, _) =>
        {
            clocks.Sleep(TimeSpan.Zero, TimeSpan.FromMinutes(15));
            throw new InvalidOperationException("claude didn't finish within 15 minutes.");
        }, Quiet, sleep: clocks.Clock);
        store.Enqueue(new Meeting("l1") { Title = "CS 101 lecture", Transcript = Lines(40) });

        Assert.Equal(1, await p.RunPendingAsync());
        var row = store.Get("l1")!;
        Assert.Equal("done", row.Status);
        Assert.Contains("didn't finish", row.Error);
    }

    [Fact]
    public async Task An_engine_that_answers_no_still_files_the_lecture_without_notes()
    {
        using var dir = new TempDir();
        var cfg = CfgFor(dir);
        using var store = new Store(cfg.DbPath, cfg.PoolDir);
        // A refusal with a status is an answer: the model isn't there, so the lecture is filed as before.
        var p = new Pipeline(cfg, store, Sort, (_, _) => throw new HttpRequestException("model 'big:35b' not found", null, HttpStatusCode.NotFound), Quiet);
        store.Enqueue(new Meeting("l1") { Title = "Lecture", Transcript = Lines(40) });
        Assert.Equal(1, await p.RunPendingAsync());
        var row = store.Get("l1")!;
        Assert.Equal("done", row.Status);
        Assert.Contains("not found", row.Error);
        Assert.Null(p.EngineProblem);
    }

    [Fact]
    public void What_counts_as_no_answer()
    {
        Assert.True(Pipeline.IsOffline(Refused()));
        Assert.True(Pipeline.IsOffline(new HttpRequestException("The response ended prematurely.")));
        Assert.True(Pipeline.IsOffline(new TimeoutException()));
        Assert.False(Pipeline.IsOffline(new HttpRequestException("Bad gateway", null, HttpStatusCode.BadGateway)));
        Assert.False(Pipeline.IsOffline(new InvalidOperationException("Claude Code: not signed in")));
    }

    [Fact]
    public async Task Running_on_its_own_it_waits_for_the_engine_and_a_wake_tries_again()
    {
        using var dir = new TempDir();
        var cfg = CfgFor(dir);
        using var store = new Store(cfg.DbPath, cfg.PoolDir);
        bool up = false;
        var p = new Pipeline(cfg, store, Sort, (_, _) => up ? Task.FromResult("## Summary\nNotes.") : throw Refused(), Quiet);
        store.Enqueue(new Meeting("l1") { Title = "Lecture", Transcript = Lines(40) });
        using var stop = new CancellationTokenSource();
        var running = p.Start(stop.Token);
        try
        {
            for (int i = 0; i < 100 && p.EngineProblem is null; i++) await Task.Delay(50);
            Assert.NotNull(p.EngineProblem);
            Assert.Equal("queued", store.Get("l1")!.Status);
            up = true;
            p.Wake();
            for (int i = 0; i < 100 && store.Get("l1")!.Status != "done"; i++) await Task.Delay(50);
            Assert.Equal("done", store.Get("l1")!.Status);
            Assert.Null(p.EngineProblem);
        }
        finally
        {
            await stop.CancelAsync();
            await running;
        }
    }
}
