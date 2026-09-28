using System.Net;
using System.Text.Json.Nodes;
using StudyStash.Library;

namespace StudyStash.Core.Tests;

/// <summary>Bringing lectures from an old library to a new one (LibraryMove): two real libraries on in-memory servers,
/// the notes and classes arriving as they were, nothing on the old one changed, and a second go changing nothing.</summary>
public class LibraryMoveTests
{
    static readonly Action<string> Quiet = _ => { };

    sealed class Lib : IAsyncDisposable
    {
        public required Config Cfg { get; init; }
        public required Store Store { get; init; }
        public required TestSite Site { get; init; }

        public HttpClient Client => Site.Stranger();

        public async ValueTask DisposeAsync()
        {
            await Site.DisposeAsync();
            Store.Dispose();
        }
    }

    static async Task<Lib> Library(TempDir dir, string name, string password, params ClassDef[] classes)
    {
        var cfg = new Config(dir[name + "-home"], dir[name + "-pool"]) { PoolPassword = password, OllamaEnabled = false, Classes = [.. classes] };
        var store = new Store(cfg.DbPath, cfg.PoolDir);
        var site = await TestSite.StartAsync(b => LibraryWeb.Build(b, cfg, store, new Pipeline(cfg, store, log: Quiet), new LibraryWebOptions
        {
            ListModels = _ => Task.FromResult<List<(string, double)>?>(null),
            Latest = _ => Task.FromResult<Release?>(null),
        }));
        return new Lib { Cfg = cfg, Store = store, Site = site };
    }

    static void File(Store store, string id, string cls, string summary = "", string date = "2026-09-01") => store.Save(new Meeting(id)
    {
        Title = "Lecture " + id, Date = date, Owner = "Sam", NotesMarkdown = "my own notes " + id, Transcript = "today we talk about " + id,
    }, new Classification(cls, 0.8, "ollama", "Lecture " + id, ["topic " + id]), summary, summary.Length > 0 ? "qwen3:1.7b" : "");

    [Fact]
    public async Task Every_lecture_comes_over_with_its_notes_and_class_and_the_old_library_keeps_them()
    {
        using var dir = new TempDir();
        await using var old = await Library(dir, "old", "old-pw", new ClassDef("CS 101", ["cs101"], "Recursion and stacks"), new ClassDef("Bio 110"));
        await using var fresh = await Library(dir, "new", "new-pw", new ClassDef("bio 110", [], "Cells"));
        File(old.Store, "a1", "CS 101", "## Recursion\n\nA function that calls itself.");
        File(old.Store, "a2", "Bio 110");
        File(old.Store, "a3", Configs.Unsorted);

        var seen = new List<MoveProgress>();
        var result = await LibraryMove.CopyAsync(old.Client, "old-pw", fresh.Client, "new-pw", seen.Add);

        Assert.Equal(new MoveResult(3, 0, 1), result);
        Assert.Equal(new MoveProgress(3, 3), seen[^1]);
        var a1 = fresh.Store.Get("a1")!;
        Assert.Equal("CS 101", a1.ClassName);
        Assert.Equal("## Recursion\n\nA function that calls itself.", a1.SummaryMd);
        Assert.Equal("qwen3:1.7b", a1.SummaryModel);
        Assert.Equal("ollama", a1.ClassifiedBy);
        Assert.True(System.IO.File.Exists(a1.MdPath));
        Assert.StartsWith(fresh.Cfg.PoolDir, a1.MdPath);
        Assert.Contains("today we talk about a1", fresh.Store.Meeting(a1).Transcript);
        Assert.Equal(Configs.Unsorted, fresh.Store.Get("a3")!.ClassName);
        // The class it didn't have came with its other names and what it covers; the one it had (another spelling) stays its own.
        var cs = fresh.Cfg.Classes.Single(c => c.Name == "CS 101");
        Assert.Equal(["cs101"], cs.Aliases);
        Assert.Equal("Recursion and stacks", cs.Description);
        Assert.Equal(["bio 110", "CS 101"], fresh.Cfg.Classes.Select(c => c.Name));
        Assert.Contains("CS 101", Configs.Load(fresh.Cfg.Home).ClassNames());
        // A copy: the old library still has every lecture and file.
        Assert.Equal(3, old.Store.ListNotes().Count);
        Assert.True(System.IO.File.Exists(old.Store.Get("a1")!.MdPath));
    }

    [Fact]
    public async Task Doing_it_again_changes_nothing_and_a_lecture_already_here_is_left_alone()
    {
        using var dir = new TempDir();
        await using var old = await Library(dir, "old", "old-pw");
        await using var fresh = await Library(dir, "new", "new-pw");
        File(old.Store, "a1", "CS 101", "old notes");
        File(fresh.Store, "a1", "Mine", "notes written here since");

        var first = await LibraryMove.CopyAsync(old.Client, "old-pw", fresh.Client, "new-pw");
        Assert.Equal(new MoveResult(0, 1, 0), first);
        Assert.Equal("notes written here since", fresh.Store.Get("a1")!.SummaryMd);
        Assert.Equal("Mine", fresh.Store.Get("a1")!.ClassName);
    }

    [Fact]
    public async Task Many_lectures_come_a_page_at_a_time()
    {
        using var dir = new TempDir();
        await using var old = await Library(dir, "old", "old-pw");
        await using var fresh = await Library(dir, "new", "new-pw");
        int n = LibraryMove.PageSize * 2 + 3;
        for (int i = 0; i < n; i++) File(old.Store, $"n{i:D3}", "CS 101", date: $"2026-09-{i % 28 + 1:D2}");

        var seen = new List<MoveProgress>();
        var result = await LibraryMove.CopyAsync(old.Client, "old-pw", fresh.Client, "new-pw", seen.Add);
        Assert.Equal(n, result.Filed);
        Assert.Equal(3, seen.Count);
        Assert.Equal([LibraryMove.PageSize, LibraryMove.PageSize * 2, n], seen.Select(p => p.Done));
        Assert.All(seen, p => Assert.Equal(n, p.Total));
        Assert.Equal(n, fresh.Store.ListNotes().Count);
        Assert.Equal(new MoveResult(0, n, 0), await LibraryMove.CopyAsync(old.Client, "old-pw", fresh.Client, "new-pw"));
    }

    [Fact]
    public async Task An_empty_library_brings_nothing_and_says_so()
    {
        using var dir = new TempDir();
        await using var old = await Library(dir, "old", "old-pw");
        await using var fresh = await Library(dir, "new", "new-pw");
        var seen = new List<MoveProgress>();
        Assert.Equal(new MoveResult(0, 0, 0), await LibraryMove.CopyAsync(old.Client, "old-pw", fresh.Client, "new-pw", seen.Add));
        Assert.Equal(new MoveProgress(0, 0), seen.Single());
    }

    [Fact]
    public async Task A_wrong_password_on_either_side_says_which_and_files_nothing()
    {
        using var dir = new TempDir();
        await using var old = await Library(dir, "old", "old-pw");
        await using var fresh = await Library(dir, "new", "new-pw");
        File(old.Store, "a1", "CS 101");
        var e = await Assert.ThrowsAsync<InvalidOperationException>(() => LibraryMove.CopyAsync(old.Client, "nope", fresh.Client, "new-pw"));
        Assert.Equal("The password for your old library isn't right.", e.Message);
        e = await Assert.ThrowsAsync<InvalidOperationException>(() => LibraryMove.CopyAsync(old.Client, "old-pw", fresh.Client, "nope"));
        Assert.Equal("The password for this library isn't right.", e.Message);
        Assert.Empty(fresh.Store.ListNotes());
    }

    [Fact]
    public async Task An_old_library_too_old_to_hand_lectures_over_says_to_update_it()
    {
        using var dir = new TempDir();
        await using var fresh = await Library(dir, "new", "new-pw");
        // What a 0.8 library answers for a route it hasn't got.
        using var older = new HttpClient(new Answer(HttpStatusCode.NotFound, "{\"detail\":\"Not Found\"}")) { BaseAddress = new Uri("http://old") };
        var e = await Assert.ThrowsAsync<InvalidOperationException>(() => LibraryMove.CopyAsync(older, "pw", fresh.Client, "new-pw"));
        Assert.Equal("Your old library runs an older Study Stash. Update it, then bring your lectures over.", e.Message);
        using var gone = new HttpClient(new Answer(null, "")) { BaseAddress = new Uri("http://old") };
        e = await Assert.ThrowsAsync<InvalidOperationException>(() => LibraryMove.CopyAsync(gone, "pw", fresh.Client, "new-pw"));
        Assert.Equal("Can't reach your old library. Is its computer on, and is Tailscale connected?", e.Message);
    }

    [Fact]
    public async Task A_page_that_isnt_one_is_refused()
    {
        using var dir = new TempDir();
        await using var fresh = await Library(dir, "new", "new-pw");
        foreach (string body in new[] { "[]", "{}", "{\"lectures\":[{\"meeting\":{}}]}", "{\"lectures\":[1]}" })
        {
            using var post = new HttpRequestMessage(HttpMethod.Post, LibraryMove.Route) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") };
            post.Headers.Add("Authorization", "Bearer new-pw");
            Assert.Equal(HttpStatusCode.BadRequest, (await fresh.Client.SendAsync(post)).StatusCode);
        }
        Assert.Empty(fresh.Store.ListNotes());
        // Without the password, neither side answers.
        Assert.Equal(HttpStatusCode.Unauthorized, (await fresh.Client.GetAsync(LibraryMove.Route)).StatusCode);
    }

    [Fact]
    public void Classes_with_no_name_unsorted_or_too_long_are_not_added()
    {
        using var dir = new TempDir();
        var cfg = new Config(dir["home"], dir["pool"]) { Classes = [] };
        using var store = new Store(cfg.DbPath, cfg.PoolDir);
        var page = JsonNode.Parse($$"""
            {"classes": [{"name": ""}, {"name": "Unsorted"}, {"name": "{{new string('x', 61)}}"}, {"name": 5}, "CS 101", {"name": " Art "}],
             "lectures": []}
            """)!.AsObject();
        Assert.Equal(new MoveResult(0, 0, 1), LibraryMove.Import(store, cfg, page));
        Assert.Equal(["Art"], cfg.ClassNames());
    }

    sealed class Answer(HttpStatusCode? status, string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            status is { } s ? Task.FromResult(new HttpResponseMessage(s) { Content = new StringContent(body) })
                : throw new HttpRequestException("connection refused");
    }
}
