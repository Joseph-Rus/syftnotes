using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using StudyStash.Core.Ai;
using StudyStash.Library;

namespace StudyStash.Core.Tests;

/// <summary>
/// Notes from an AI the library doesn't run, end to end through a real library's API: the Claude app (or any app
/// connected to Study Stash) saving notes with save_notes, and the student pasting what a free chat wrote. Nothing is
/// ever lost: a lecture's own notes stay unless replacing them is asked for, the ones replaced come back, and what the
/// student typed during the lecture is in whatever is saved. No real AI here: the notes are what an AI might send.
/// </summary>
public class FreeAiNotesTests
{
    const string Sentence = "Today we covered the cell membrane and how osmosis moves water across it, and worked "
        + "through the sodium-potassium pump and why it needs energy to run against the gradient. ";
    static readonly string Transcript = string.Concat(Enumerable.Repeat(Sentence, 12));

    static Config Cfg(TempDir dir) => new(dir["home"], dir["pool"]) { PoolPassword = "pw", OllamaEnabled = false };

    static async Task<TestSite> Site(Config cfg, Store store) => await TestSite.StartAsync(b => LibraryWeb.Build(b, cfg, store,
        new Pipeline(cfg, store, log: _ => { }), new LibraryWebOptions
        {
            ListModels = _ => Task.FromResult<List<(string, double)>?>(null), Tailscale = () => new TailscaleInfo(),
            Latest = _ => Task.FromResult<Release?>(null), Ai = new AiJobs(cfg.Home) { Checks = new FakeChecks().Build() },
        }));

    static void Seed(Store store, string id = "lec-1", string notes = "", string model = "", string typed = "", string transcript = "") => store.Save(
        new Meeting(id) { Title = "Cell membranes", Date = "2026-09-01T09:00:00", Owner = "Sam", Transcript = transcript.Length > 0 ? transcript : Transcript, PrivateNotes = typed },
        new Classification("Bio 110", 0.9, "ollama", "Cell membranes", ["cells"]), summaryMd: notes, summaryModel: model);

    [Fact]
    public async Task An_AI_app_never_saves_over_a_lectures_notes_unless_told_and_what_it_replaced_comes_back()
    {
        using var dir = new TempDir();
        var cfg = Cfg(dir);
        using var store = new Store(cfg.DbPath, cfg.PoolDir);
        Seed(store, notes: "## Summary\nOld notes about cells.", model: "qwen3:8b");
        await using var site = await Site(cfg, store);
        var app = new RemoteLibrary("http://localhost", "pw", site.Client); // what `Study Stash mcp` reads through
        const string Writer = "Claude (in the Claude app)";
        const string Written = "## Summary\nThe membrane lets water through by osmosis.\n\n## Key points\n- The pump needs energy.";

        string refused = await ClaudeTools.SaveNotesAsync(app, "lec-1", Written, replace: false, Writer);
        Assert.StartsWith("Nothing was saved: this lecture already has notes", refused);
        Assert.Contains("replace set to true", refused);
        Assert.Equal("## Summary\nOld notes about cells.", store.Get("lec-1")!.SummaryMd);

        string saved = await ClaudeTools.SaveNotesAsync(app, "lec-1", Written, replace: true, Writer);
        Assert.StartsWith("Saved.", saved);
        Assert.Contains("written by Claude", saved);
        Assert.Contains("put them back", saved);
        var row = store.Get("lec-1")!;
        Assert.Equal(Written, row.SummaryMd);
        Assert.Equal(Writer, row.SummaryModel);
        Assert.Contains($"_Written by {Writer} from the transcript._", File.ReadAllText(row.MdPath!)); // the note file says so too

        var remote = new AiRemote("http://localhost", "pw", site.Client);
        var info = (await remote.RewriteAsync("lec-1"))!;
        Assert.Equal("Claude", info.Current!.By);
        Assert.Equal((NotesChangeKind.App, "Claude", true), (info.Change!.Kind, info.Change.By, info.Change.CanUndo)); // what it replaced can come back

        var back = (await remote.UndoNotesAsync("lec-1"))!;
        Assert.Equal("## Summary\nOld notes about cells.", back.Current!.Markdown);
        Assert.Equal("Ollama", back.Current.By);
        Assert.True(back.Change!.CanRedo); // and Redo takes it back again
        Assert.Equal("qwen3:8b", store.Get("lec-1")!.SummaryModel);
    }

    [Fact]
    public async Task Notes_edited_in_their_own_file_are_kept_when_an_AI_app_saves_over_them_or_a_version_is_put_back()
    {
        using var dir = new TempDir();
        var cfg = Cfg(dir);
        using var store = new Store(cfg.DbPath, cfg.PoolDir);
        Seed(store, notes: "## Summary\nThe library's own notes.", model: "qwen3:8b");
        await using var site = await Site(cfg, store);
        var app = new RemoteLibrary("http://localhost", "pw", site.Client);
        var remote = new AiRemote("http://localhost", "pw", site.Client);
        string path = store.Get("lec-1")!.MdPath!;
        void EditFile(string from, string to) => File.WriteAllText(path, File.ReadAllText(path).Replace(from, to));

        // Fixed in the note file itself (another app, or an AI working in the folder), then an AI app saves over them.
        EditFile("The library's own notes.", "Fixed in Obsidian.");
        Assert.StartsWith("Saved.", await ClaudeTools.SaveNotesAsync(app, "lec-1", "## Summary\nClaude's notes.", replace: true, "Claude (in the Claude app)"));
        Assert.Equal([("## Summary\nClaude's notes.", NotesChangeKind.App), ("## Summary\nFixed in Obsidian.", NotesChangeKind.File), ("## Summary\nThe library's own notes.", NotesChangeKind.Written)],
            store.NotesVersions("lec-1").Select(v => (v.Markdown, v.Kind)));
        Assert.Equal("## Summary\nClaude's notes.", store.Get("lec-1")!.SummaryMd);

        // Fixed in the file again, then the version before is put back: that fix is kept too.
        EditFile("Claude's notes.", "Claude's notes, fixed in the file.");
        var back = (await remote.RestoreNotesAsync("lec-1"))!;
        Assert.Equal("## Summary\nFixed in Obsidian.", back.Current!.Markdown);
        Assert.Equal([NotesChangeKind.Restore, NotesChangeKind.File, NotesChangeKind.App], store.NotesVersions("lec-1").Take(3).Select(v => v.Kind));
        Assert.Equal("## Summary\nClaude's notes, fixed in the file.", store.NotesVersions("lec-1")[1].Markdown);
        Assert.Contains("Fixed in Obsidian.", File.ReadAllText(path));
    }

    [Fact]
    public async Task What_the_student_typed_is_in_notes_pasted_from_a_chat_and_the_chats_words_are_not()
    {
        using var dir = new TempDir();
        var cfg = Cfg(dir);
        using var store = new Store(cfg.DbPath, cfg.PoolDir);
        Seed(store, typed: "**Osmosis** moves water\n- [ ] read chapter 3");
        await using var site = await Site(cfg, store);
        var remote = new AiRemote("http://localhost", "pw", site.Client);
        const string Pasted = "Sure! Here are your study notes:\n\n```markdown\n# Cell membranes\n\n## Summary\nThe membrane and the pump.\n\n"
            + "## Key points\n- Osmosis moves water across the membrane.\n- The pump needs ATP: \\(3\\,\\mathrm{Na^+}\\) out.\n```\n\nLet me know if you'd like flashcards!";

        var info = (await remote.SaveNotesAsync("lec-1", Pasted, replace: false, basedOn: null))!;

        string notes = info.Current!.Markdown;
        Assert.StartsWith("## Summary\nThe membrane and the pump.", notes); // no "Sure!", no fence, no title over it
        Assert.Contains("- ==**Osmosis** moves water== across the membrane.", notes); // their line, marked as theirs, as they typed it
        Assert.Contains("[ ] ==read chapter 3==", notes); // left out by the chat: kept anyway
        Assert.Contains("$3\\,\\mathrm{Na^+}$", notes); // the chat's maths, as the library writes it
        Assert.DoesNotContain("Let me know", notes);
        Assert.Equal("your AI", info.Current.By);
        Assert.Equal((NotesChangeKind.Paste, false), (info.Change!.Kind, info.Change.CanUndo)); // there were no notes to replace
        Assert.Equal(Engines.PastedWriter, store.Get("lec-1")!.SummaryModel);
    }

    [Fact]
    public async Task Switched_off_in_Settings_an_AI_app_saves_nothing()
    {
        using var dir = new TempDir();
        var cfg = Cfg(dir);
        using var store = new Store(cfg.DbPath, cfg.PoolDir);
        Seed(store);
        await using var site = await Site(cfg, store);
        var remote = new AiRemote("http://localhost", "pw", site.Client);
        var app = new RemoteLibrary("http://localhost", "pw", site.Client);
        var access = await remote.SetAccessAsync(reading: new ReadingScopes(SaveNotes: false));
        Assert.False(access!.Reading.SaveNotes);

        // The library itself refuses, whatever the app's server last heard...
        string said = await ClaudeTools.SaveNotesAsync(app, "lec-1", "## Summary\nNotes.", replace: false, "Claude (in the Claude app)");
        Assert.StartsWith("Nothing was saved: Study Stash's settings don't let AI apps save notes", said);
        Assert.Null(store.Get("lec-1")!.SummaryMd);
        // ...and so does the tool, before it asks the library.
        var server = McpServer.Create(new StdioServerTransport("test"), new McpServerOptions { ServerInfo = new Implementation { Name = "test", Version = "1" } });
        var save = ToolAccess.Guard(ClaudeTools.Tools(app), () => Task.FromResult((true, access.Reading))).Single(t => t.ProtocolTool.Name == "save_notes");
        var result = await save.InvokeAsync(new RequestContext<CallToolRequestParams>(server, new JsonRpcRequest { Method = "tools/call" },
            new CallToolRequestParams { Name = "save_notes" }), CancellationToken.None);
        Assert.True(result.IsError);
        Assert.Equal(ToolAccess.Refused("save"), ((TextContentBlock)result.Content[0]).Text);

        // Pasting from a chat is the student's own doing: the switch is about AI apps.
        Assert.NotNull(await remote.SaveNotesAsync("lec-1", "## Summary\nPasted notes.", replace: false, basedOn: null));
    }

    [Fact]
    public async Task Copy_for_your_AI_is_the_librarys_own_brief_and_the_transcript_in_parts_when_it_is_long()
    {
        using var dir = new TempDir();
        var cfg = Cfg(dir);
        using var store = new Store(cfg.DbPath, cfg.PoolDir);
        Seed(store, typed: "**Osmosis** moves water");
        string longer = string.Join("\n", Enumerable.Range(0, 900).Select(i => $"[{i / 60:00}:{i % 60:00}] Line {i}: {Sentence}"));
        Seed(store, id: "lec-2", transcript: longer);
        await using var site = await Site(cfg, store);
        var remote = new AiRemote("http://localhost", "pw", site.Client);

        var one = (await remote.NotesBriefAsync("lec-1"))!;
        string copy = Assert.Single(one.Parts);
        Assert.StartsWith(one.Brief, copy);
        Assert.Contains("You are an expert note-taker for university lectures.", copy);
        Assert.Contains("**Osmosis** moves water", copy); // the student's line, to be kept and marked
        Assert.Contains("Transcript:\n" + Sentence.TrimEnd(), copy);

        var two = (await remote.NotesBriefAsync("lec-2"))!;
        Assert.True(two.Parts.Count > 1);
        Assert.All(two.Parts, p => Assert.True(p.Length <= ChatNotes.OneMessage));
        Assert.Contains($"in {two.Parts.Count} parts", two.Parts[0]);
        Assert.Contains(two.Brief, two.Parts[0]);
        Assert.Contains("That's the whole transcript", two.Parts[^1]);
        string all = string.Join("\n", two.Parts);
        Assert.Contains("Line 0: ", all);
        Assert.Contains("Line 899: ", all);
        Assert.DoesNotContain("[00:00]", all); // without its times, as the library's own AI reads it
    }

    [Theory]
    [InlineData("Here’s a set of study notes based on the transcript:\n\n---\n\n### Summary\nCells.\n\n### Key points\n• Osmosis\n• The pump\n\n---\n\nWould you like me to make a quiz?",
        "## Summary\nCells.\n\n## Key points\n- Osmosis\n- The pump")]
    [InlineData("**Summary**\nCells divide.\n\n**Key points:**\n- Mitosis \\[ 2n \\to 2n \\]【4:0†source】",
        "## Summary\nCells divide.\n\n## Key points\n- Mitosis $$2n \\to 2n$$")]
    [InlineData("## Summary\nCode stays as it was:\n\n```python\n# not a heading\nprint(\"\\(x\\)\")\n```",
        "## Summary\nCode stays as it was:\n\n```python\n# not a heading\nprint(\"\\(x\\)\")\n```")]
    [InlineData("Just my own words, nothing a chat added.", "Just my own words, nothing a chat added.")]
    public void What_chats_put_round_notes_is_tidied_away(string pasted, string notes) => Assert.Equal(notes, ChatNotes.Clean(pasted));
}
