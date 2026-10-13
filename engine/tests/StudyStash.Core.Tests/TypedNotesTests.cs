using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using StudyStash.Library;

namespace StudyStash.Core.Tests;

/// <summary>What a student types while a lecture records: it goes to the library with the recording, the notes are
/// written round it with every line of theirs kept and marked, and a title they typed is the lecture's from then on.</summary>
public class TypedNotesTests
{
    const string Line = "Attention lets every token look at every other token in the sentence.\n";
    const string Typed = "# History\n- Attention Is All You Need, 2017\n- ChatGPT, Nov 2022\n\n# Self-attention\n- q · k → softmax → weights on v";

    static string Transcript => string.Concat(Enumerable.Repeat(Line, 40));

    [Fact]
    public void A_recording_sends_what_was_typed_and_says_a_typed_title_is_the_students()
    {
        var l = new Lecture { Id = "rec-1", Started = "2026-10-12T09:30:00-07:00", ClassName = "Machine Learning", Notes = Typed, Title = "Transformers", TitleTyped = true };
        var payload = l.Payload();
        Assert.Equal(Typed, (string?)payload["private_notes"]);
        Assert.Equal("Transformers", (string?)payload["title"]);
        var m = Wire.MeetingFromJson(payload);
        Assert.Equal(Typed, m.PrivateNotes);
        Assert.True(m.TitledByHand);

        // Nothing typed: nothing sent, and a title from the calendar isn't the student's.
        var plain = new Lecture { Id = "rec-2", Started = "2026-10-12T09:30:00-07:00", Title = "Machine Learning", Notes = "  \n" }.Payload();
        Assert.False(plain.ContainsKey("private_notes"));
        Assert.False(Wire.MeetingFromJson(plain).TitledByHand);
    }

    [Fact]
    public void The_notes_are_asked_for_round_what_was_typed_and_as_ever_when_nothing_was()
    {
        var typed = new Meeting("1") { Title = "Transformers", PrivateNotes = Typed };
        string prompt = Summarize.WholePrompt(typed, "the transcript");
        Assert.Contains("Keep every line the student typed, word for word", prompt);
        Assert.Contains("==exactly as they typed it==", prompt);
        Assert.Contains("- q · k → softmax → weights on v", prompt);
        Assert.DoesNotContain("## Key points", prompt);
        Assert.Contains("- ChatGPT, Nov 2022", Summarize.MergePrompt(typed, ["part one", "part two"]));

        string usual = Summarize.WholePrompt(new Meeting("2") { Title = "Transformers" }, "the transcript");
        Assert.Contains("## Key points", usual);
        Assert.DoesNotContain("student typed", usual);
    }

    [Fact]
    public void Every_typed_line_is_in_the_notes_and_marked_whatever_the_model_wrote()
    {
        // The model marked one line, wrote another unmarked, and left two out.
        string notes = "## Summary\nA lecture on transformers.\n\n## ==History==\n- Attention Is All You Need, 2017 The paper that introduced the transformer.\n";
        string kept = Summarize.KeepTyped(notes, Typed);
        Assert.Contains("## ==History==", kept);
        Assert.Contains("- ==Attention Is All You Need, 2017== The paper", kept);
        Assert.Contains("## Also in your notes", kept);
        Assert.Contains("- ==ChatGPT, Nov 2022==", kept);
        Assert.Contains("- ==Self-attention==", kept);
        Assert.Contains("- ==q · k → softmax → weights on v==", kept);
        // Already whole: nothing is added, and doing it again changes nothing.
        Assert.Equal(kept, Summarize.KeepTyped(kept, Typed));
        Assert.Equal(notes, Summarize.KeepTyped(notes, ""));
    }

    [Fact]
    public async Task The_pipeline_writes_round_typed_notes_keeps_them_apart_and_keeps_a_typed_title()
    {
        using var dir = new TempDir();
        var cfg = new Config(dir["home"], dir["pool"]) { OllamaModel = "small:1b", SummaryModel = "big:35b", Classes = [new ClassDef("Machine Learning")] };
        using var store = new Store(cfg.DbPath, cfg.PoolDir);
        string? asked = null;
        var p = new Pipeline(cfg, store,
            (_, _, _) => Task.FromResult("""{"class_name": "Machine Learning", "confidence": 0.9, "lecture_title": "A title the AI made up", "topics": []}"""),
            (m, c) => Summarize.SummarizeTranscriptAsync(m, c, chat: (_, _, prompt, _) =>
            {
                asked = prompt;
                return Task.FromResult("## Summary\nTransformers, from the 2017 paper on.\n\n## ==History==\n- ==Attention Is All You Need, 2017== The paper.\n");
            }, show: (_, _) => Task.FromResult<int?>(8192)), _ => { });
        var l = new Lecture { Id = "rec-1", Started = "2026-10-12T09:30:00-07:00", Notes = Typed, Title = "Transformers, my title", TitleTyped = true };
        l.Segments.Add(new Spoken(0, 5, Transcript));
        store.Enqueue(Wire.MeetingFromJson(l.Payload()));
        Assert.Equal(1, await p.RunPendingAsync());

        Assert.Contains("ChatGPT, Nov 2022", asked);
        var row = store.Get("rec-1")!;
        Assert.Equal("Transformers, my title", row.LectureTitle);
        Assert.Contains("## ==History==", row.SummaryMd);
        Assert.Contains("- ==ChatGPT, Nov 2022==", row.SummaryMd); // a line the model left out
        var reader = new LibraryReader(cfg, store);
        var lecture = reader.Lecture("rec-1")!;
        Assert.Equal(Typed, (string?)lecture["typed"]);
        Assert.Equal("Transformers, my title", (string?)lecture["title"]);
        Assert.Equal("Transformers, from the 2017 paper on.", (string?)lecture["summary"]);
    }

    /// <summary>Keep recording sends the lecture to the library again, longer, under the title the laptop knows it by:
    /// the name it has in the library stays, above all one the student typed.</summary>
    [Fact]
    public async Task A_lecture_kept_recording_comes_in_again_and_keeps_its_name()
    {
        using var dir = new TempDir();
        var cfg = new Config(dir["home"], dir["pool"]) { OllamaModel = "small:1b", SummaryEnabled = false, Classes = [new ClassDef("Machine Learning")] };
        using var store = new Store(cfg.DbPath, cfg.PoolDir);
        string ai = "Attention and transformers";
        var p = new Pipeline(cfg, store,
            (_, _, _) => Task.FromResult($$"""{"class_name": "Machine Learning", "confidence": 0.9, "lecture_title": "{{ai}}", "topics": []}"""), log: _ => { });
        var l = new Lecture { Id = "rec-1", Started = "2026-10-12T09:30:00-07:00" };
        l.Segments.Add(new Spoken(0, 5, Transcript));
        async Task<NoteRow> SentAsync()
        {
            store.Enqueue(Wire.MeetingFromJson(l.Payload()));
            Assert.Equal(1, await p.RunPendingAsync());
            return store.Get("rec-1")!;
        }
        Assert.Equal("Attention and transformers", (await SentAsync()).LectureTitle);

        // Kept recording: the AI would call the longer lecture something else, and doesn't get to.
        ai = "Transformers, continued";
        Assert.Equal("Attention and transformers", (await SentAsync()).LectureTitle);

        // Renamed by the student in the library, then kept recording: the laptop doesn't know the new title, and it stays theirs.
        Assert.Equal(Store.Renamed.Done, store.SetTitle("rec-1", "My transformers notes"));
        var row = await SentAsync();
        Assert.Equal("My transformers notes", row.LectureTitle);
        Assert.True(store.Meeting(row).TitledByHand);
        Assert.Contains("My transformers notes", File.ReadAllText(row.MdPath!));

        // A title typed on the recording's page while it's kept recording is the newest: the lecture's from then on.
        l.Title = "Transformers, the exam one";
        l.TitleTyped = true;
        Assert.Equal("Transformers, the exam one", (await SentAsync()).LectureTitle);
    }

    static Task<TestSite> Site(Config cfg, Store store) =>
        TestSite.StartAsync(b => LibraryWeb.Build(b, cfg, store, new Pipeline(cfg, store, log: _ => { }), new LibraryWebOptions
        {
            ListModels = _ => Task.FromResult<List<(string, double)>?>(null), Tailscale = () => new TailscaleInfo(), Latest = _ => Task.FromResult<Release?>(null),
        }));

    [Fact]
    public async Task A_lecture_can_be_renamed_and_keeps_the_name_when_its_notes_are_written_again()
    {
        using var dir = new TempDir();
        var cfg = new Config(dir["home"], dir["pool"]) { PoolName = "Sam's library", PoolPassword = "pw", OllamaEnabled = false, Classes = [new ClassDef("CS 101", [])] };
        Directory.CreateDirectory(cfg.Home);
        using var store = new Store(cfg.DbPath, cfg.PoolDir);
        store.Save(new Meeting("rec-1") { Title = "CS 101 lecture, Tue 23 Sep", Date = "2026-09-23T10:02:12-07:00", Folder = "CS 101", Transcript = "[00:05] Okay." },
            new Classification("CS 101", 0.95, "folder", "Recursion and the call stack", ["recursion"]), summaryMd: "## Summary\nRecursion.");
        string before = store.Get("rec-1")!.MdPath!;
        await using var site = await Site(cfg, store);

        HttpRequestMessage Rename(string title)
        {
            var r = new HttpRequestMessage(HttpMethod.Post, "/api/v2/lectures/rec-1/title") { Content = JsonContent.Create(new { title }) };
            r.Headers.Authorization = new("Bearer", "pw");
            return r;
        }
        Assert.Equal(HttpStatusCode.BadRequest, (await site.Client.SendAsync(Rename("   "))).StatusCode);
        var answer = await site.Client.SendAsync(Rename("  Recursion, the midterm one "));
        Assert.Equal(HttpStatusCode.OK, answer.StatusCode);
        Assert.Equal("Recursion, the midterm one", (string?)(await answer.Content.ReadFromJsonAsync<JsonObject>())!["title"]);

        var row = store.Get("rec-1")!;
        Assert.Equal(("Recursion, the midterm one", "CS 101", "folder", "## Summary\nRecursion."), (row.LectureTitle, row.ClassName, row.ClassifiedBy, row.SummaryMd));
        Assert.NotEqual(before, row.MdPath);
        Assert.False(File.Exists(before));
        Assert.Contains("Recursion, the midterm one", File.ReadAllText(row.MdPath!));
        Assert.True(store.Meeting(row).TitledByHand);

        // Still being written: not now (the library would write its own title over it as it finished).
        Assert.True(store.Requeue("rec-1"));
        Assert.Equal(HttpStatusCode.Conflict, (await site.Client.SendAsync(Rename("Another"))).StatusCode);
    }
}
