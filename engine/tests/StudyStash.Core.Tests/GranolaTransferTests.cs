using System.Text.Json.Nodes;

namespace StudyStash.Core.Tests;

/// <summary>Bringing lectures over from Granola: which of the lectures there are offered, what one looks like when it
/// arrives, and the notes the library writes for one that came with Granola's notes and no transcript.</summary>
public class GranolaTransferTests
{
    /// <summary>Granola with these lectures in it; a transcript only for the ids given one (a paid plan).</summary>
    sealed class FakeGranola(params Meeting[] lectures) : ILectureSource
    {
        public Dictionary<string, string> Transcripts { get; } = [];

        sealed class Session : IGranolaSession
        {
            public Task<IReadOnlyList<ToolInfo>> ListToolsAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<ToolInfo>>([]);
            public Task<ToolResult> CallToolAsync(string name, JsonObject args, CancellationToken ct = default) => Task.FromResult(ToolResult.Text(""));
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }

        public Task<IGranolaSession> SessionAsync(CancellationToken ct = default) => Task.FromResult<IGranolaSession>(new Session());

        // The list names the lectures; only get_meetings gives their notes.
        public Task<List<Meeting>> ListMeetingsAsync(IGranolaSession session, DateOnly? since = null, DateOnly? until = null, int limit = 50,
            int maxPages = 20, CancellationToken ct = default) =>
            Task.FromResult(lectures.Select(m => new Meeting(m.Id) { Title = m.Title, Date = m.Date }).ToList());

        public Task<List<Meeting>> GetMeetingsAsync(IGranolaSession session, IReadOnlyList<string> ids, CancellationToken ct = default) =>
            Task.FromResult(lectures.Where(m => ids.Contains(m.Id)).ToList());

        public Task<string> GetTranscriptAsync(IGranolaSession session, string meetingId, CancellationToken ct = default) =>
            Task.FromResult(Transcripts.GetValueOrDefault(meetingId, ""));
    }

    static readonly string Summary = string.Join("\n", Enumerable.Repeat("- The cell membrane is a lipid bilayer that lets small nonpolar molecules through.", 8));

    /// <summary>Long after every lecture here: none could still be recording.</summary>
    static readonly DateTimeOffset Later = new(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);

    static Meeting Lecture(string id, string title, string date, string notes = "") =>
        new(id) { Title = title, Date = date, NotesMarkdown = notes, Folder = "School" };

    [Fact]
    public async Task Only_lectures_study_stash_doesnt_have_and_wasnt_told_to_leave_are_offered_newest_first()
    {
        using var dir = new TempDir();
        var granola = new FakeGranola(Lecture("a", "Membranes", "2026-10-05T09:00:00"), Lecture("b", "Osmosis", "2026-10-07T09:00:00"),
            Lecture("c", "Left where it is", "2026-10-08T09:00:00"), Lecture("d", "Brought over on the other laptop", "2026-10-09T09:00:00"),
            Lecture("e", "Already brought over", "2026-10-10T09:00:00"));
        var ledger = new GranolaLedger(dir["granola.json"]);
        ledger.Mark("c", GranolaLedger.Left);
        ledger.Mark("e", GranolaLedger.Transferred);

        var waiting = await GranolaTransfer.WaitingAsync(granola, ledger, id => Task.FromResult(id == "granola-d"), Later);

        Assert.Equal(["b", "a"], waiting.Select(w => w.GranolaId));
        Assert.Equal("Osmosis", waiting[0].Title);
        // The one the library has already is remembered, here and after the app starts again.
        Assert.Equal(GranolaLedger.Transferred, new GranolaLedger(dir["granola.json"]).Of("d"));

        // A library that can't be asked right now: its lectures are still offered, and nothing is marked.
        var offline = await GranolaTransfer.WaitingAsync(granola, new GranolaLedger(dir["other.json"]), _ => throw new HttpRequestException("no route"), Later);
        Assert.Equal(5, offline.Count);
    }

    [Fact]
    public async Task A_lecture_still_being_recorded_waits_until_granola_has_written_its_notes()
    {
        using var dir = new TempDir();
        var now = new DateTimeOffset(2026, 10, 12, 10, 30, 0, TimeZoneInfo.Local.GetUtcOffset(new DateTime(2026, 10, 12, 10, 30, 0)));
        var recording = Lecture("live", "Enzymes", "2026-10-12T10:00:00"); // started half an hour ago, nothing written
        var done = Lecture("done", "Membranes", "2026-10-12T09:00:00", Summary); // an hour and a half ago, written up
        var old = Lecture("old", "No notes, long over", "2026-10-11T09:00:00");
        var ledger = new GranolaLedger(dir["granola.json"]);
        Task<bool> No(string _) => Task.FromResult(false);

        var waiting = await GranolaTransfer.WaitingAsync(new FakeGranola(recording, done, old), ledger, No, now);
        Assert.Equal(["done", "old"], waiting.Select(w => w.GranolaId));

        // Its notes written, it's offered; and once offered, a lecture stays in the list whatever Granola says of it.
        recording.NotesMarkdown = Summary;
        ledger.Mark("old", GranolaLedger.Offered);
        waiting = await GranolaTransfer.WaitingAsync(new FakeGranola(recording, done, old), ledger, No, now);
        Assert.Equal(["live", "done", "old"], waiting.Select(w => w.GranolaId));
    }

    [Fact]
    public async Task A_lecture_comes_over_with_granolas_notes_under_an_id_of_its_own()
    {
        var granola = new FakeGranola(Lecture("a", "Membranes", "2026-10-05T09:00:00", Summary), Lecture("empty", "Still recording", "2026-10-12T09:00:00"));

        var m = (await GranolaTransfer.FetchAsync(granola, "a"))!;
        Assert.Equal("granola-a", m.Id);
        Assert.True(GranolaTransfer.IsFromGranola(m.Id));
        Assert.Equal(("Membranes", "2026-10-05T09:00:00", Summary, ""), (m.Title, m.Date, m.NotesMarkdown, m.Transcript));
        Assert.Equal("", m.Folder); // Granola's folder isn't a class: the library sorts it
        // What the library is sent says where it came from, and reads back the same.
        var back = Wire.MeetingFromJson(JsonNode.Parse(Wire.MeetingJson(m)));
        Assert.Equal(("granola-a", Summary, "granola"), (back.Id, back.NotesMarkdown, (string?)back.Raw["source"]));

        // On a plan that gives it, the transcript comes too.
        granola.Transcripts["a"] = "[00:00] Today, membranes.";
        Assert.Equal("[00:00] Today, membranes.", (await GranolaTransfer.FetchAsync(granola, "a"))!.Transcript);

        // Nothing written for it yet: nothing to bring, and nothing for one Granola doesn't have.
        Assert.Null(await GranolaTransfer.FetchAsync(granola, "empty"));
        Assert.Null(await GranolaTransfer.FetchAsync(granola, "gone"));
    }

    [Fact]
    public async Task With_no_transcript_the_notes_are_written_from_the_ones_it_came_with_and_the_file_keeps_both()
    {
        using var dir = new TempDir();
        var cfg = new Config(dir["home"], dir["pool"]) { OllamaModel = "small:1b", SummaryModel = "big:35b" };
        var m = new Meeting("granola-a") { Title = "Membranes", Date = "2026-10-05T09:00:00", NotesMarkdown = Summary, PrivateNotes = "ask about aquaporins" };
        Assert.True(Summarize.FromNotes(m));
        Assert.True(Summarize.WantsSummary(m, cfg));

        var prompts = new List<string>();
        string notes = await Summarize.SummarizeTranscriptAsync(m, cfg, (_, _, prompt, _) =>
        {
            prompts.Add(prompt);
            return Task.FromResult("## Summary\nMembranes are lipid bilayers.");
        }, (_, _) => Task.FromResult<int?>(32768));
        string p = Assert.Single(prompts);
        Assert.Contains("There is no transcript of this lecture", p);
        Assert.Contains("The notes it came with:\n- The cell membrane is a lipid bilayer", p);
        Assert.Contains("What the student typed during it:\nask about aquaporins", p);
        Assert.Contains("Use only what is in these notes", p);
        Assert.DoesNotContain("Transcript:", p);

        string file = Notes.Render(m, new Classification("BIO 110", 1, "rules"), notes, "Claude");
        Assert.Contains("_Written by Claude from the notes it came with._", file);
        Assert.Contains("## Notes\n\n- The cell membrane is a lipid bilayer", file); // Granola's own, kept

        // Too little to write from stays as it came; a lecture with its transcript is written from that, as ever.
        Assert.False(Summarize.WantsSummary(new Meeting("granola-b") { NotesMarkdown = "Short." }, cfg));
        var recorded = new Meeting("rec-1") { Title = "Membranes", Transcript = string.Join("\n", Enumerable.Repeat("The membrane is a lipid bilayer and water crosses it by osmosis.", 40)) };
        Assert.False(Summarize.FromNotes(recorded));
        Assert.Contains("_Written by Claude from the transcript._", Notes.Render(recorded, new Classification("BIO 110", 1, "rules"), notes, "Claude"));
    }

    [Fact]
    public async Task A_new_lecture_is_announced_once_and_transfer_sends_it_to_the_library()
    {
        using var dir = new TempDir();
        File.WriteAllText(dir["tokens.json"], "{\"access_token\": \"t\"}"); // signed in
        var granola = new FakeGranola(Lecture("a", "Membranes", "2026-10-05T09:00:00", Summary), Lecture("b", "Osmosis", "2026-10-07T09:00:00", Summary));
        var sent = new List<(string Url, JsonObject Body, string Key)>();
        var net = new LaptopHost
        {
            Post = (url, body, key) =>
            {
                sent.Add((url, (JsonObject)JsonNode.Parse(body)!, key));
                return Task.FromResult(new JsonObject { ["status"] = "queued" });
            },
            Get = (_, _) => Task.FromResult<JsonObject?>(null), // the library has neither
        };
        var cc = new ClientConfig(dir["home"]) { ServerUrl = "http://library:8787/", PoolKey = "secret", DisplayName = "Joey" };
        GranolaImport Import() => new(dir["tokens.json"], dir["client.json"], new GranolaLedger(dir["granola.json"]), () => cc, net, _ => granola);
        var import = Import();
        var found = new List<string>();
        import.Found += lectures => found.AddRange(lectures.Select(l => l.Title));

        Assert.True(import.SignedIn);
        await import.CheckAsync();
        Assert.Equal(["Osmosis", "Membranes"], found);
        Assert.Equal(["b", "a"], import.Waiting.Select(w => w.GranolaId));
        // Looked at again, even after the app starts again: still waiting, not announced a second time.
        await import.CheckAsync();
        import = Import();
        import.Found += lectures => found.AddRange(lectures.Select(l => l.Title));
        await import.CheckAsync();
        Assert.Equal(2, found.Count);
        Assert.Equal(2, import.Waiting.Count);

        Assert.True(await import.TransferAsync("a"));
        var (url, body, key) = Assert.Single(sent);
        Assert.Equal(("http://library:8787/api/ingest", "secret"), (url, key));
        Assert.Equal(("granola-a", "Membranes", Summary, "Joey"), ((string?)body["id"], (string?)body["title"], (string?)body["notes_markdown"], (string?)body["owner"]));
        Assert.Equal(["b"], import.Waiting.Select(w => w.GranolaId));

        import.Leave("b");
        Assert.Empty(import.Waiting);
        await Import().CheckAsync();
        Assert.Single(sent); // neither is offered or sent again
        var after = Import();
        await after.CheckAsync();
        Assert.Empty(after.Waiting);
    }
}
