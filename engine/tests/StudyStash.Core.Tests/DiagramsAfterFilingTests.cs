using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using StudyStash.Core.Ai;
using StudyStash.Core.Rich;

namespace StudyStash.Core.Tests;

/// <summary>
/// Notes first, diagrams after: a lecture is filed with its notes the moment they're written, the designer's diagrams
/// go into them later in their place, a student's edit (or newer notes) meanwhile is never clobbered or doubled, a pass
/// cut off by a restart runs again a bounded number of times, notes that get no diagram stay byte for byte as filed,
/// and "Rewrite notes with" goes the same way. Every engine is a fake.
/// </summary>
public class DiagramsAfterFilingTests
{
    const string Said = "Low blood pressure makes the JG cells release renin, and renin cleaves angiotensinogen into angiotensin I. "
        + "ACE in the lungs converts angiotensin I into angiotensin II, blood pressure rises, and that stops renin.";

    /// <summary>25 minutes of a lecture, 25 seconds a line: long enough for two diagrams.</summary>
    static readonly string Transcript = string.Join("\n", Enumerable.Range(0, 60).Select(i => $"[{TimedText.Clock(i * 25)}] {Said}"));

    const string Notes = "## Summary\nHow the kidney raises blood pressure.\n\n## Key points\n- Renin starts it.\n- Angiotensin II does the work.\n\n"
        + "## Details and examples\n### Drugs\n- ACE inhibitors block it.\n\n## Questions to review\n1. What releases renin?";

    const string Chain = "flowchart LR\n  Low[\"Low blood pressure\"] --> JG[\"JG cells\"]\n  JG -->|renin| A1[\"Angiotensin I\"]\n"
        + "  A1 -->|ACE in lungs| A2[\"Angiotensin II\"]\n  A2 --> BP[\"Blood pressure rises\"]\n  BP -.->|stops renin| JG";

    /// <summary>A second diagram, of something else: where the drugs act.</summary>
    const string Drugs = "flowchart LR\n  A1[\"Angiotensin I\"] -->|ACE in lungs| A2[\"Angiotensin II\"]\n  INH[\"ACE inhibitors\"] -.->|block| A1\n"
        + "  A2 --> BP[\"Blood pressure rises\"]";

    const string Caption = "Low pressure releases renin, which leads to angiotensin II.";

    static string SourceFor(string after) => after == "Drugs" ? Drugs : Chain;

    static DesignedDiagram Designed(string after, string title = "The renin chain") => new(title, after, 25, NoteBlockKind.Mermaid, SourceFor(after), Caption);

    static string Reply(params (string After, string Title)[] diagrams) => new JsonObject
    {
        ["reason"] = "The chain is the lecture's point.",
        ["diagrams"] = new JsonArray([.. diagrams.Select(d => (JsonNode)new JsonObject
        {
            ["title"] = d.Title, ["after"] = d.After, ["at"] = "00:25", ["kind"] = "mermaid", ["source"] = SourceFor(d.After), ["caption"] = Caption,
        })]),
    }.ToJsonString();

    /// <summary>A diagram as it sits in the notes once it's in: what follows the line it goes after.</summary>
    static string Unit(DesignedDiagram d) => DiagramDesign.Insert("x", [d])[1..];

    static bool Designing(string prompt) => prompt.StartsWith("You design the diagrams", StringComparison.Ordinal);

    /// <summary>Claude, faked: the notes are <see cref="NotesText"/>; a design is <see cref="Design"/>'s answer.</summary>
    sealed class Engine : AiProvider
    {
        public override string Id => "claude";
        public override string Name => "Claude";
        public override string Binary => "claude";
        public override string Site => "https://example.test/claude";
        public override bool Available() => true;
        public override List<string> Command(AiRequest req, bool stream) => [];
        public override IEnumerable<AiEvent> Parse(string line) => [];
        public string NotesText = Notes;
        public Func<string, CancellationToken, Task<string>> Design = (_, _) => Task.FromResult(Reply(("Key points", "The renin chain")));
        public int Designs;
        public readonly SemaphoreSlim Asked = new(0);

        public override async IAsyncEnumerable<AiEvent> RunAsync(AiRequest req, bool stream = true, [EnumeratorCancellation] CancellationToken ct = default)
        {
            string answer;
            if (!Designing(req.Prompt)) answer = NotesText;
            else
            {
                Interlocked.Increment(ref Designs);
                Asked.Release();
                answer = await Design(req.Prompt, ct);
            }
            yield return new AiEvent("final", answer);
        }
    }

    sealed class Rig : IDisposable
    {
        public readonly TempDir Dir = new();
        public readonly Config Cfg;
        public readonly Store Store;
        public readonly Engine Claude = new();
        public readonly AiJobs Ai;
        public readonly List<string> Log = [];
        public DiagramJobs Jobs;
        public readonly Pipeline Pipeline;

        public Rig()
        {
            Cfg = new Config(Dir["home"], Dir["pool"]) { OllamaEnabled = true, OllamaModel = "qwen3:8b", Classes = [new ClassDef("BIO 210")] };
            new AiSettings { Provider = "claude", Fallback = false }.Save(Cfg.Home); // diagrams: automatic, so Claude designs them
            Store = new Store(Cfg.DbPath, Cfg.PoolDir);
            Ai = new AiJobs(Cfg.Home) { Providers = _ => Claude, Checks = new FakeChecks().Installed("claude").Build(), Log = Say, DiagramTimeout = TimeSpan.FromSeconds(20) };
            Jobs = Restart();
            Pipeline = new Pipeline(Cfg, Store, (_, _, _) => Task.FromResult("""{"class_name": "BIO 210", "confidence": 0.9, "lecture_title": "Blood pressure", "topics": []}"""),
                Ai.SummarizeAsync, Say, filed: id => Jobs.AfterFiled(id, Ai.TakeDiagramsFollow(id)));
        }

        public void Say(string line)
        {
            lock (Log) Log.Add(line);
        }

        public string Logged()
        {
            lock (Log) return string.Join("\n", Log);
        }

        /// <summary>The library starting again: a new queue reading what the last one left.</summary>
        public DiagramJobs Restart() => Jobs = new DiagramJobs(Cfg, Store, Ai, Say);

        public NoteRow Row(string id = "lec-1") => Store.Get(id)!;

        public string File(string id = "lec-1") => Py.ReadText(Row(id).MdPath!);

        public async Task<NoteRow> FileAsync(string id = "lec-1")
        {
            Store.Enqueue(new Meeting(id) { Title = "Blood pressure", Date = "2026-09-29T09:00:00", Folder = "BIO 210", Transcript = Transcript });
            Assert.Equal(1, await Pipeline.RunPendingAsync());
            return Row(id);
        }

        /// <summary>Waits until the designer has been asked (once more).</summary>
        public async Task AskedAsync() => Assert.True(await Claude.Asked.WaitAsync(TimeSpan.FromSeconds(10)), "the designer was never asked");

        public void Dispose()
        {
            Store.Dispose();
            Dir.Dispose();
        }
    }

    [Fact]
    public async Task A_lecture_is_filed_with_its_notes_at_once_and_its_diagrams_go_in_where_they_belong_later()
    {
        using var rig = new Rig();
        var answer = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        rig.Claude.Design = (_, _) => answer.Task;

        var row = await rig.FileAsync();
        // Filed, readable everywhere, with no diagram yet and none being waited for.
        Assert.Equal((Store.Done, Notes), (row.Status, row.SummaryMd));
        Assert.Equal(0, rig.Claude.Designs);
        Assert.True(rig.Jobs.Adding("lec-1"));
        Assert.Equal(Notes, new LibraryReader(rig.Cfg, rig.Store).Lecture("lec-1")!["notes"]!.GetValue<string>());
        string filed = rig.File();
        Assert.Contains("How the kidney raises blood pressure.", filed);
        Assert.DoesNotContain("```mermaid", filed);
        Assert.Contains("its notes are filed; diagrams follow", rig.Logged());

        var running = rig.Jobs.RunPendingAsync();
        await rig.AskedAsync();
        answer.SetResult(Reply(("Key points", "The renin chain")));
        Assert.Equal(1, await running);

        // In their place, once, in the notes the app, the phone and Ask read, and in the note file.
        string expected = DiagramDesign.Insert(Notes, [Designed("Key points")]);
        Assert.Equal(expected, rig.Row().SummaryMd);
        Assert.Equal(expected, new LibraryReader(rig.Cfg, rig.Store).Lecture("lec-1")!["notes"]!.GetValue<string>());
        Assert.Equal(filed.Replace("- Angiotensin II does the work.", "- Angiotensin II does the work." + Unit(Designed("Key points"))), rig.File());
        Assert.False(rig.Jobs.Adding("lec-1"));
        Assert.Contains("added 1 diagram to the notes", rig.Logged());
        Assert.Contains(rig.Store.PassagesOf("lec-1"), p => p.Text.Contains("Low pressure releases renin", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Notes_that_get_no_diagram_stay_byte_for_byte_as_filed()
    {
        using var rig = new Rig();
        rig.Claude.Design = (_, _) => Task.FromResult("""{"reason": "A discussion, nothing to draw.", "diagrams": []}""");
        var row = await rig.FileAsync();
        byte[] before = File.ReadAllBytes(row.MdPath!);

        Assert.Equal(1, await rig.Jobs.RunPendingAsync());
        Assert.Equal(before, File.ReadAllBytes(row.MdPath!));
        Assert.Equal((Notes, row.UpdatedAt), (rig.Row().SummaryMd, rig.Row().UpdatedAt));

        // A designer that fails, or never answers in time, leaves them so too.
        rig.Jobs.Queue("lec-1", "claude");
        rig.Claude.Design = (_, _) => throw new InvalidOperationException("Claude is having a bad day");
        Assert.Equal(1, await rig.Jobs.RunPendingAsync());
        rig.Jobs = new DiagramJobs(rig.Cfg, rig.Store, rig.Ai) { HardStop = TimeSpan.FromMilliseconds(300) };
        rig.Jobs.Queue("lec-1", "claude");
        rig.Claude.Design = (_, ct) => Task.Delay(Timeout.Infinite, ct).ContinueWith(_ => "", TaskScheduler.Default);
        Assert.Equal(1, await rig.Jobs.RunPendingAsync());
        Assert.Equal(before, File.ReadAllBytes(row.MdPath!));
        Assert.Equal((Notes, row.UpdatedAt), (rig.Row().SummaryMd, rig.Row().UpdatedAt));
        Assert.False(rig.Jobs.Adding("lec-1"));
    }

    [Fact]
    public async Task A_students_edit_while_the_diagrams_are_designed_is_kept_and_nothing_is_doubled()
    {
        using var rig = new Rig();
        var answer = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        rig.Claude.Design = (_, _) => answer.Task;
        var row = await rig.FileAsync();

        // The student edits the note file by hand while the designer works.
        var running = rig.Jobs.RunPendingAsync();
        await rig.AskedAsync();
        string edited = rig.File().Replace("- Angiotensin II does the work.", "- Angiotensin II does the work.\n- My own: ACE sits in the lungs.") + "\nMy own last line.\n";
        File.WriteAllText(row.MdPath!, edited);
        answer.SetResult(Reply(("Key points", "The renin chain")));
        Assert.Equal(1, await running);
        string now = rig.File();
        string unit = Unit(Designed("Key points"));
        Assert.Equal(edited, now.Replace(unit, "")); // every edit kept, in its place; only the diagram added
        Assert.Contains("- My own: ACE sits in the lungs." + unit, now);
        Assert.Single(NoteBlocks.Find(now), b => b.IsDiagram);
        Assert.Contains("the note file had edits of its own", rig.Logged());

        // The notes themselves rewritten meanwhile (not through a rewrite, which would replace the job): the diagram
        // whose heading is still there goes under it, the one whose heading is gone is left out, and the diagram an
        // earlier pass added is replaced, not doubled.
        string rewritten = "## Overview\nThe kidney and blood pressure, again.\n\n## Details and examples\n### Drugs\n- ACE inhibitors block it.\n- So do ARBs.";
        rig.Jobs.Queue("lec-1", "claude");
        answer = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        running = rig.Jobs.RunPendingAsync();
        await rig.AskedAsync();
        var m = rig.Store.Meeting(rig.Row());
        rig.Store.Save(m, new Classification("BIO 210", 1, "human", "Blood pressure"), rewritten + Unit(Designed("Drugs", "An old one")), "Claude");
        answer.SetResult(Reply(("Key points", "The renin chain"), ("Drugs", "Where the drugs act")));
        Assert.Equal(1, await running);
        Assert.Equal(DiagramDesign.Insert(rewritten, [Designed("Drugs", "Where the drugs act")]), rig.Row().SummaryMd);
        Assert.Single(NoteBlocks.Find(rig.File()), b => b.IsDiagram);
        Assert.Contains("1 diagram went under its heading, and “The renin chain” was left out", rig.Logged());

        // None of their headings left: nothing changes at all.
        string bare = "Just a paragraph, no headings.";
        rig.Store.Save(m, new Classification("BIO 210", 1, "human", "Blood pressure"), bare, "Claude");
        string seen = StudyStash.Core.Notes.Fingerprint(rig.Row().SummaryMd!);
        rig.Store.Save(m, new Classification("BIO 210", 1, "human", "Blood pressure"), "## Another\n" + bare, "Claude");
        byte[] before = File.ReadAllBytes(rig.Row().MdPath!);
        var outcome = rig.Store.AddDiagrams("lec-1", seen, [Designed("Key points")]);
        Assert.Equal(DiagramsLanded.Skipped, outcome.How);
        Assert.Equal("## Another\n" + bare, rig.Row().SummaryMd);
        Assert.Equal(before, File.ReadAllBytes(rig.Row().MdPath!));
    }

    [Fact]
    public async Task A_pass_cut_off_by_a_restart_runs_again_a_few_times_at_most_and_never_leaves_the_lecture_waiting()
    {
        using var rig = new Rig();
        rig.Claude.Design = (_, ct) => Task.Delay(Timeout.Infinite, ct).ContinueWith(_ => "", TaskScheduler.Default);
        await rig.FileAsync();

        // The library dies mid-pass (its queue is simply abandoned) and starts again: the job is still there, and runs.
        _ = rig.Jobs.RunPendingAsync();
        await rig.AskedAsync();
        rig.Restart();
        Assert.True(rig.Jobs.Adding("lec-1"));
        rig.Claude.Design = (_, _) => Task.FromResult(Reply(("Key points", "The renin chain")));
        Assert.Equal(1, await rig.Jobs.RunPendingAsync());
        Assert.Equal(DiagramDesign.Insert(Notes, [Designed("Key points")]), rig.Row().SummaryMd);

        // A lecture whose pass is cut off every time is given up on after three tries, its notes as filed.
        byte[] filed = File.ReadAllBytes((await rig.FileAsync("lec-2")).MdPath!);
        rig.Claude.Design = (_, ct) => Task.Delay(Timeout.Infinite, ct).ContinueWith(_ => "", TaskScheduler.Default);
        for (int i = 0; i < DiagramJobs.MaxTries; i++)
        {
            _ = rig.Jobs.RunPendingAsync();
            await rig.AskedAsync();
            rig.Restart();
        }
        Assert.True(rig.Jobs.Adding("lec-2"));
        Assert.Equal(1, await rig.Jobs.RunPendingAsync());
        Assert.False(rig.Jobs.Adding("lec-2"));
        Assert.Contains("given up on after 3 tries", rig.Logged());
        Assert.Equal(filed, File.ReadAllBytes(rig.Row("lec-2").MdPath!));
        Assert.Equal(Notes, rig.Row("lec-2").SummaryMd);

        // A library stopping on purpose mid-pass doesn't spend a try.
        await rig.FileAsync("lec-3");
        using var stop = new CancellationTokenSource();
        var stopping = rig.Jobs.RunPendingAsync(stop.Token);
        await rig.AskedAsync();
        await stop.CancelAsync();
        await stopping;
        rig.Restart();
        rig.Claude.Design = (_, _) => Task.FromResult(Reply(("Key points", "The renin chain")));
        Assert.Equal(1, await rig.Jobs.RunPendingAsync());
        Assert.Contains("```mermaid", rig.Row("lec-3").SummaryMd);
    }

    [Fact]
    public async Task A_pass_on_the_librarys_own_model_steps_aside_while_notes_are_written()
    {
        using var rig = new Rig();
        await rig.FileAsync();
        new AiSettings { Provider = "claude", Fallback = false, Diagrams = "ollama" }.Save(rig.Cfg.Home);
        var local = new AiJobs(rig.Cfg.Home)
        {
            Providers = _ => rig.Claude, Checks = new FakeChecks().Installed("claude").Ollama(models: [("qwen3:30b", 18.6)]).Build(), Log = rig.Say,
            DiagramTimeout = TimeSpan.FromSeconds(20),
        };
        bool busy = false;
        rig.Jobs = new DiagramJobs(rig.Cfg, rig.Store, local, rig.Say) { NotesBusy = () => busy, YieldCheck = TimeSpan.FromMilliseconds(20) };
        rig.Claude.Design = (_, ct) => Task.Delay(Timeout.Infinite, ct).ContinueWith(_ => "", TaskScheduler.Default);

        var running = rig.Jobs.RunPendingAsync();
        await rig.AskedAsync();
        busy = true; // a lecture comes in to be written
        Assert.Equal(0, await running);
        Assert.Contains("stepped aside while notes are written", rig.Logged());
        Assert.Equal(0, await rig.Jobs.RunPendingAsync()); // nothing starts while notes are written
        Assert.True(rig.Jobs.Adding("lec-1"));

        busy = false;
        rig.Claude.Design = (_, _) => Task.FromResult(Reply(("Key points", "The renin chain")));
        Assert.Equal(1, await rig.Jobs.RunPendingAsync());
        Assert.Equal(DiagramDesign.Insert(Notes, [Designed("Key points")]), rig.Row().SummaryMd);
        Assert.Contains("qwen3:30b drew 1", rig.Logged());
    }

    [Fact]
    public async Task Diagrams_designed_before_a_restart_just_go_in_without_designing_them_again()
    {
        if (OperatingSystem.IsWindows()) return; // a folder that can't be written to, the way a Mac or Linux has it
        using var rig = new Rig();
        var row = await rig.FileAsync();
        string folder = Path.GetDirectoryName(row.MdPath!)!;
        File.SetUnixFileMode(folder, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        try
        {
            Assert.Equal(0, await rig.Jobs.RunPendingAsync()); // designed, but the file can't be written yet
            Assert.Contains("couldn't write the note file", rig.Logged());
            Assert.Equal(Notes, rig.Row().SummaryMd);
            Assert.True(rig.Jobs.Adding("lec-1"));
        }
        finally
        {
            File.SetUnixFileMode(folder, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
        int designs = rig.Claude.Designs;
        rig.Jobs = new DiagramJobs(rig.Cfg, rig.Store, rig.Ai) { RetryWait = TimeSpan.Zero };
        Assert.Equal(1, await rig.Jobs.RunPendingAsync());
        Assert.Equal(designs, rig.Claude.Designs);
        Assert.Equal(DiagramDesign.Insert(Notes, [Designed("Key points")]), rig.Row().SummaryMd);
        Assert.Contains("```mermaid", rig.File());
    }

    [Fact]
    public async Task Rewritten_notes_show_at_once_and_their_diagrams_follow_once_theyre_used()
    {
        using var rig = new Rig();
        var oldPass = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        rig.Claude.Design = (_, ct) => oldPass.Task.WaitAsync(ct);
        await rig.FileAsync();
        var rewrites = new Rewrites(rig.Cfg, rig.Store, rig.Ai, _ => { }, rig.Jobs);
        // The first notes' diagrams are still being designed when the student uses rewritten ones.
        var running = rig.Jobs.RunPendingAsync();
        await rig.AskedAsync();
        Assert.Equal("adding", rewrites.Get("lec-1").Diagrams);

        const string Better = "## Summary\nRenin, angiotensin and blood pressure.\n\n## Key points\n- Renin starts it.\n- ACE in the lungs.\n- Angiotensin II raises pressure.";
        rig.Claude.NotesText = Better;
        int designs = rig.Claude.Designs;
        rewrites.Start("lec-1", "claude");
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (rewrites.Get("lec-1").State == "working" && DateTime.UtcNow < deadline) await Task.Delay(20);
        var ready = rewrites.Get("lec-1");
        Assert.Equal("ready", ready.State);
        Assert.Equal(Better, ready.Draft!.Markdown); // the draft never waits for a diagram
        Assert.Equal(designs, rig.Claude.Designs);

        var newPass = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        rig.Claude.Design = (_, _) => newPass.Task;
        var used = rewrites.Use("lec-1");
        Assert.Equal(Better, used.Current!.Markdown);
        Assert.Equal("adding", used.Diagrams);
        await rig.AskedAsync();
        newPass.SetResult(Reply(("Key points", "The renin chain")));
        // The old notes' pass stops, and what it would have drawn never lands; the new notes' pass runs after it.
        Assert.Equal(2, await running);
        Assert.Equal(DiagramDesign.Insert(Better, [Designed("Key points")]), rig.Row().SummaryMd);
        Assert.Single(NoteBlocks.Find(rig.File()), b => b.IsDiagram);
        Assert.Equal("", rewrites.Get("lec-1").Diagrams);
        oldPass.SetResult(Reply(("Key points", "The old notes' chain")));
    }
}
