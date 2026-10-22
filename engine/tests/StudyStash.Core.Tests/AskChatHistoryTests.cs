using Microsoft.AspNetCore.Builder;
using StudyStash.Core.Ai;
using StudyStash.Library;

namespace StudyStash.Core.Tests;

/// <summary>Ask conversations kept on the library: each turn saved in its chat, listed where it was asked (a lecture,
/// a folder with its lectures, Home), continued after a restart, renamed, deleted with Undo, gone with a lecture deleted
/// for good, and answered one turn at a time. Every AI is a stand-in.</summary>
public class AskChatHistoryTests
{
    const string Answer = """{"answer":"Arteries, capillaries and veins.","sources":[1]}""";

    sealed class Library : IAsyncDisposable
    {
        public required TempDir Dir { get; init; }
        public required Config Cfg { get; init; }
        public required Store Store { get; init; }
        public required ScriptedAi Claude { get; init; }
        public TestSite Site { get; private set; } = null!;
        public AiRemote Remote => new("http://localhost", "pw", Site.Client);

        public static async Task<Library> StartAsync()
        {
            var dir = new TempDir();
            var cfg = new Config(dir["home"], dir["pool"])
            {
                PoolPassword = "pw", OllamaEnabled = true, Classes = [new ClassDef("BIO 110"), new ClassDef("CHEM 121")],
            };
            var store = new Store(cfg.DbPath, cfg.PoolDir);
            store.Save(new Meeting("v1") { Title = "Blood vessels", Date = "2026-10-08", Transcript = "Arteries, capillaries and veins." },
                new Classification("BIO 110", 1, "folder"), "## Three kinds of vessel\n- Arteries carry blood away.\n- Capillaries are one cell thick.\n- Veins carry it back.", "Claude");
            store.Save(new Meeting("m1") { Title = "Muscles", Date = "2026-10-01", Transcript = "Actin slides over myosin." },
                new Classification("BIO 110", 1, "folder"), "## Summary\nActin slides over myosin.", "Claude");
            store.Save(new Meeting("c1") { Title = "Moles", Date = "2026-10-07", Transcript = "A mole is 6.02e23 things." },
                new Classification("CHEM 121", 1, "folder"), "## Summary\nA mole is 6.02e23 things.", "Claude");
            var lib = new Library { Dir = dir, Cfg = cfg, Store = store, Claude = new ScriptedAi("claude", "Claude") { Script = [new AiEvent("final", Answer)] } };
            await lib.RestartAsync();
            return lib;
        }

        /// <summary>The library started again, as after a restart: a new server over the same home.</summary>
        public async Task RestartAsync()
        {
            if (Site is not null) await Site.DisposeAsync();
            var ai = new AiJobs(Cfg.Home) { Providers = id => id == "claude" ? Claude : new ScriptedAi(id) { Script = [new AiEvent("final", Answer)] }, Checks = new FakeChecks().Installed("claude").Build() };
            Site = await TestSite.StartAsync(b => LibraryWeb.Build(b, Cfg, Store, new Pipeline(Cfg, Store, log: _ => { }), new LibraryWebOptions
            {
                ListModels = _ => Task.FromResult<List<(string, double)>?>(null), Tailscale = () => new TailscaleInfo(),
                Latest = _ => Task.FromResult<Release?>(null), Ai = ai,
            }));
        }

        public Task<AskReply?> AskAsync(string question, string chat, AskPlace? place = null, string? lecture = null, string? live = null, string engine = "claude") =>
            Remote.AskAsync(new AskRequest(question) { Engine = engine, Chat = chat, Place = place, Lecture = lecture, Live = live }, _ => { }, CancellationToken.None);

        public async ValueTask DisposeAsync()
        {
            await Site.DisposeAsync();
            Store.Dispose();
            Dir.Dispose();
        }
    }

    [Fact]
    public async Task A_chat_is_kept_and_continues_after_the_library_restarts()
    {
        await using var lib = await Library.StartAsync();
        var first = await lib.AskAsync("What are the three kinds of blood vessel?", "new", new AskPlace("lecture", "v1", "BIO 110"), lecture: "v1");
        string id = first!.Chat!;

        await lib.RestartAsync();
        var next = await lib.AskAsync("And the second one?", id, lecture: "v1");

        Assert.Equal(id, next!.Chat);
        Assert.Equal(1, next.Remembered);
        Assert.Contains("Student: What are the three kinds of blood vessel?\nYou: Arteries, capillaries and veins.", lib.Claude.Prompts[^1]);
        var chat = (await lib.Remote.ChatAsync(id))!;
        Assert.Equal(("What are the three kinds of blood vessel?", "lecture", "v1", "BIO 110", 2), (chat.Title, chat.Scope, chat.Lecture, chat.Class, chat.Turns));
        Assert.Equal(["user", "assistant", "user", "assistant"], chat.Messages!.Select(m => m.Role));
        Assert.Equal(("claude", "Claude"), (chat.Messages![3].Engine, chat.Messages[3].EngineName));
        Assert.Equal("Three kinds of vessel", Assert.Single(chat.Messages[1].Sources).Section);
    }

    [Fact]
    public async Task The_recordings_chat_is_on_its_lecture_after_it_stops()
    {
        await using var lib = await Library.StartAsync();
        // Asked while it records: the library hasn't got the lecture yet, only what's been said so far.
        var reply = await lib.AskAsync("What has she said about veins?", "new", new AskPlace("lecture", "rec-live", "BIO 110"), lecture: "rec-live",
            live: "[00:10] Veins carry blood back to the heart, with valves.");
        // Stopped, it's filed under the same id.
        lib.Store.Save(new Meeting("rec-live") { Title = "BIO 110, Wed 21 Oct", Date = "2026-10-21", Transcript = "Veins carry blood back." },
            new Classification("BIO 110", 1, "folder"), "## Summary\nVeins.", "Claude");

        var chat = Assert.Single(await lib.Remote.ChatsAsync("rec-live", null));
        Assert.Equal((reply!.Chat, "BIO 110, Wed 21 Oct"), (chat.Id, chat.LectureTitle));
    }

    [Fact]
    public async Task Chats_are_listed_per_lecture_folder_and_Home_and_renamed_deleted_and_brought_back()
    {
        await using var lib = await Library.StartAsync();
        string onVessels = (await lib.AskAsync("Vessels?", "new", new AskPlace("lecture", "v1", "BIO 110"), lecture: "v1"))!.Chat!;
        await lib.AskAsync("Muscles?", "new", new AskPlace("lecture", "m1", "BIO 110"), lecture: "m1");
        await lib.AskAsync("This class?", "new", new AskPlace("class", null, "BIO 110"));
        await lib.AskAsync("Moles?", "new", new AskPlace("lecture", "c1", "CHEM 121"), lecture: "c1");
        await lib.AskAsync("Everything?", "new", new AskPlace("home"));

        Assert.Equal(["Vessels?"], (await lib.Remote.ChatsAsync("v1", null)).Select(c => c.Title));
        Assert.Equal(["This class?", "Muscles?", "Vessels?"], (await lib.Remote.ChatsAsync(null, "BIO 110")).Select(c => c.Title));
        Assert.Equal(5, (await lib.Remote.ChatsAsync(null, null)).Count);
        Assert.Equal("Blood vessels", (await lib.Remote.ChatsAsync(null, "BIO 110"))[^1].LectureTitle);

        Assert.Equal("Vessels, again", (await lib.Remote.RenameChatAsync(onVessels, "Vessels, again"))!.Title);
        Assert.True(await lib.Remote.DeleteChatAsync(onVessels));
        Assert.Empty(await lib.Remote.ChatsAsync("v1", null));
        Assert.Null(await lib.Remote.ChatAsync(onVessels));
        Assert.True(await lib.Remote.RestoreChatAsync(onVessels)); // Undo
        Assert.Equal(["Vessels, again"], (await lib.Remote.ChatsAsync("v1", null)).Select(c => c.Title));
        Assert.False(await lib.Remote.RestoreChatAsync(onVessels));
    }

    [Fact]
    public async Task A_lectures_chats_are_on_the_page_of_the_folder_it_is_in_now()
    {
        await using var lib = await Library.StartAsync();
        string onVessels = (await lib.AskAsync("Vessels?", "new", new AskPlace("lecture", "v1", "BIO 110"), lecture: "v1"))!.Chat!;
        // A recording in no folder: its chat has none to start with.
        string onRecording = (await lib.AskAsync("What has she said?", "new", new AskPlace("lecture", "rec-live"), lecture: "rec-live", live: "[00:05] Moles, today."))!.Chat!;

        lib.Store.SetClass("v1", "CHEM 121"); // moved
        lib.Store.Save(new Meeting("rec-live") { Title = "CHEM 121, Wed 21 Oct", Date = "2026-10-21", Transcript = "Moles." },
            new Classification("CHEM 121", 1, "folder"), "## Summary\nMoles.", "Claude"); // filed when it stopped

        Assert.Empty(await lib.Remote.ChatsAsync(null, "BIO 110"));
        var chem = await lib.Remote.ChatsAsync(null, "CHEM 121");
        Assert.Equal([onRecording, onVessels], chem.Select(c => c.Id));
        Assert.All(chem, c => Assert.Equal("CHEM 121", c.Class));
    }

    [Fact]
    public async Task A_lecture_deleted_for_good_takes_its_chats_with_it()
    {
        await using var lib = await Library.StartAsync();
        await lib.AskAsync("Vessels?", "new", new AskPlace("lecture", "v1", "BIO 110"), lecture: "v1");
        await lib.AskAsync("Muscles?", "new", new AskPlace("lecture", "m1", "BIO 110"), lecture: "m1");

        lib.Store.Trash("v1");
        Assert.Single(await lib.Remote.ChatsAsync("v1", null)); // in the trash, it can still come back with them
        await lib.RestartAsync(); // the trash is emptied as the library starts: gone for good

        Assert.Empty(await lib.Remote.ChatsAsync("v1", null));
        Assert.Equal(["Muscles?"], (await lib.Remote.ChatsAsync(null, null)).Select(c => c.Title));
    }

    [Fact]
    public async Task A_chat_answers_one_question_at_a_time()
    {
        await using var lib = await Library.StartAsync();
        string id = (await lib.AskAsync("Vessels?", "new", new AskPlace("lecture", "v1", "BIO 110"), lecture: "v1"))!.Chat!;
        lib.Claude.Gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var answering = lib.AskAsync("And the second one?", id, lecture: "v1");
        while (lib.Claude.Prompts.Count < 2) await Task.Delay(10);

        // Asked in the same chat on another device meanwhile: told so, and nothing is kept of it.
        var refused = await Assert.ThrowsAsync<LibraryRefusedException>(() => lib.AskAsync("And the third?", id, lecture: "v1"));
        Assert.Equal(409, refused.Status);
        Assert.Contains("still answering a question asked on another device", refused.Message);
        // Another chat isn't held up (asked of another AI here: the stand-in Claude is held for the first).
        Assert.NotNull((await lib.AskAsync("Moles?", "new", new AskPlace("lecture", "c1", "CHEM 121"), lecture: "c1", engine: "ollama"))!.Chat);

        lib.Claude.Gate.SetResult(true);
        await answering;
        Assert.NotNull(await lib.AskAsync("And the third?", id, lecture: "v1"));
        Assert.Equal(3, (await lib.Remote.ChatAsync(id))!.Turns);
    }

    [Fact]
    public async Task A_chat_deleted_while_it_answers_stays_deleted_and_Undo_brings_it_back_as_it_was()
    {
        await using var lib = await Library.StartAsync();
        string id = (await lib.AskAsync("Vessels?", "new", new AskPlace("lecture", "v1", "BIO 110"), lecture: "v1"))!.Chat!;
        lib.Claude.Gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var answering = lib.AskAsync("And the second one?", id, lecture: "v1"); // on another device, say
        while (lib.Claude.Prompts.Count < 2) await Task.Delay(10);

        Assert.True(await lib.Remote.DeleteChatAsync(id));
        lib.Claude.Gate.SetResult(true);
        await answering; // its answer comes, and isn't kept

        Assert.Null(await lib.Remote.ChatAsync(id));
        Assert.Empty(await lib.Remote.ChatsAsync("v1", null));
        Assert.True(await lib.Remote.RestoreChatAsync(id)); // Undo still works
        Assert.Equal(1, (await lib.Remote.ChatAsync(id))!.Turns);
    }

    [Fact]
    public async Task A_library_from_before_lists_no_chats_and_says_none_was_kept()
    {
        // One whose chats are the browser's own (no kind) and whose answers don't say which chat they're in.
        await using var site = await TestSite.StartAsync(b =>
        {
            var app = b.Build();
            app.MapGet("/api/v2/chats", () => Microsoft.AspNetCore.Http.Results.Json(new[] { new { id = "250101-1", title = "Study guide", updated = "2026-01-01" } }));
            return app;
        });
        var old = new AiRemote("http://localhost", "pw", site.Client);
        Assert.Empty(await old.ChatsAsync(null, null));
        // And one without the route at all.
        await using var older = await TestSite.StartAsync(b => b.Build());
        Assert.Empty(await new AiRemote("http://localhost", "pw", older.Client).ChatsAsync("v1", null));
    }
}
