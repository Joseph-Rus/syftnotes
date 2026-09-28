using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using StudyStash.Library;

namespace StudyStash.Core.Tests;

/// <summary>Deleting a lecture from the app: it leaves the library at once (notes, transcript, search), waits in the
/// trash a few minutes so Undo brings it back exactly, and is then gone for good.</summary>
public class LectureTrashTests
{
    const string Notes = "## Summary\nA recursive function calls itself on a smaller input until it reaches a base case.";
    const string Transcript = "[00:05] Okay, let's start.\n[18:05] The midterm covers recursion traces and stack diagrams.";

    static (Config Cfg, Store Store) Library(TempDir dir)
    {
        var cfg = new Config(dir["home"], dir["pool"])
        {
            PoolName = "Sam's library", PoolPassword = "pw", OllamaEnabled = false,
            Classes = [new ClassDef("CS 101", []), new ClassDef("BIO 110", [])],
        };
        Directory.CreateDirectory(cfg.Home);
        var store = new Store(cfg.DbPath, cfg.PoolDir);
        store.Save(new Meeting("rec-1")
        {
            Title = "CS 101 lecture, Tue 23 Sep", Date = "2026-09-23T10:02:12-07:00", Owner = "Sam", Folder = "CS 101", Transcript = Transcript,
            Raw = new JsonObject { ["source"] = "recorder", ["seconds"] = 4320.0 },
        }, new Classification("CS 101", 0.95, "folder", "Recursion and the call stack", ["recursion"]), summaryMd: Notes, summaryModel: "qwen3:8b");
        store.Save(new Meeting("rec-2")
        {
            Title = "BIO lecture", Date = "2026-09-22T09:00:00-07:00", Owner = "Sam", Transcript = "[00:01] Membranes let water through by osmosis.",
        }, new Classification("BIO 110", 0.9, "ai", "Membranes and osmosis", ["osmosis"]), summaryMd: "## Summary\nWater crosses membranes by osmosis.");
        return (cfg, store);
    }

    static Task<TestSite> Site(Config cfg, Store store) =>
        TestSite.StartAsync(b => LibraryWeb.Build(b, cfg, store, new Pipeline(cfg, store, log: _ => { }), new LibraryWebOptions
        {
            ListModels = _ => Task.FromResult<List<(string, double)>?>(null), Tailscale = () => new TailscaleInfo(), Latest = _ => Task.FromResult<Release?>(null),
        }));

    static HttpRequestMessage Req(HttpMethod m, string path, string key = "pw")
    {
        var r = new HttpRequestMessage(m, path);
        r.Headers.Authorization = new("Bearer", key);
        if (m == HttpMethod.Post) r.Content = JsonContent.Create(new { });
        return r;
    }

    [Fact]
    public void A_trashed_lecture_leaves_every_list_and_search_and_comes_back_exactly()
    {
        using var dir = new TempDir();
        var (_, store) = Library(dir);
        using var _s = store;
        var before = store.Get("rec-1")!;
        string file = before.MdPath!;
        string text = File.ReadAllText(file);
        Assert.NotEmpty(store.SearchPassages("midterm"));

        Assert.True(store.Trash("rec-1"));
        Assert.Null(store.Get("rec-1"));
        Assert.False(File.Exists(file));
        Assert.Empty(store.SearchPassages("midterm"));
        Assert.DoesNotContain(store.ClassesSummary(), c => c.ClassName == "CS 101");
        Assert.Equal(["rec-1"], store.Trashed());
        Assert.False(store.Trash("nope"));

        Assert.True(store.Restore("rec-1"));
        var back = store.Get("rec-1")!;
        Assert.Equal((before.ClassName, before.ClassifiedBy, before.LectureTitle, before.SummaryMd, before.Status, before.UpdatedAt, before.FirstSeen),
            (back.ClassName, back.ClassifiedBy, back.LectureTitle, back.SummaryMd, back.Status, back.UpdatedAt, back.FirstSeen));
        Assert.Equal(before.Confidence, back.Confidence);
        Assert.Equal(file, back.MdPath);
        Assert.Equal(text, File.ReadAllText(file));
        Assert.NotEmpty(store.SearchPassages("midterm"));
        Assert.Empty(store.Trashed());
        Assert.False(store.Restore("rec-1")); // it isn't in the trash any more
    }

    [Fact]
    public void Emptying_the_trash_makes_it_final_and_remembers_the_lecture_is_gone()
    {
        using var dir = new TempDir();
        var (_, store) = Library(dir);
        using var _s = store;
        var t0 = DateTimeOffset.UtcNow;
        store.Trash("rec-1", t0.AddMinutes(-20));
        store.Trash("rec-2", t0);
        Assert.Equal(1, store.EmptyTrash(t0.AddMinutes(-10)));
        Assert.Equal(["rec-1"], store.Gone());
        Assert.False(store.Restore("rec-1"));
        Assert.True(store.Restore("rec-2"));

        // The library starting again empties it all.
        store.Trash("rec-2", t0);
        Assert.Equal(1, store.EmptyTrash());
        Assert.Equal(["rec-1", "rec-2"], store.Gone().Order());
    }

    [Fact]
    public void A_lecture_deleted_while_its_notes_were_being_written_is_written_again_when_restored()
    {
        using var dir = new TempDir();
        var (_, store) = Library(dir);
        using var _s = store;
        store.Enqueue(new Meeting("rec-3") { Title = "New one", Date = "2026-09-24T10:00:00-07:00", Transcript = "[00:01] Hello." });
        var claimed = store.ClaimNext()!;
        Assert.Equal("rec-3", claimed.Id);
        Assert.True(store.Trash("rec-3"));
        // The pipeline's result for it is dropped: it was deleted meanwhile.
        Assert.Null(store.Finish(claimed, store.Meeting(claimed), new Classification("CS 101", 1, "folder")));
        Assert.True(store.Restore("rec-3"));
        Assert.Equal(Store.Queued, store.Get("rec-3")!.Status);
    }

    [Fact]
    public async Task The_app_deletes_a_lecture_with_the_library_password_and_undo_brings_it_back()
    {
        using var dir = new TempDir();
        var (cfg, store) = Library(dir);
        using var _s = store;
        await using var site = await Site(cfg, store);
        var c = site.Client;

        Assert.Equal(HttpStatusCode.Unauthorized, (await c.SendAsync(Req(HttpMethod.Delete, "/api/v2/lectures/rec-1", key: "wrong"))).StatusCode);
        Assert.NotNull(store.Get("rec-1"));

        var lib = new RemoteLibrary("http://localhost", "pw", c);
        Assert.True(await lib.DeleteAsync("rec-1"));
        Assert.Null(await lib.LectureAsync("rec-1"));
        Assert.DoesNotContain((await lib.LecturesAsync(null, 50, null)).OfType<JsonObject>(), l => l["id"]!.GetValue<string>() == "rec-1");
        Assert.Empty((await lib.SearchAsync("midterm", null, 8))["passages"]!.AsArray());
        Assert.False(await lib.DeleteAsync("rec-1")); // already gone from the list
        Assert.False(await lib.DeleteAsync("nope"));

        var back = await lib.RestoreAsync("rec-1");
        Assert.Equal("CS 101", back!["class"]!.GetValue<string>());
        Assert.Equal("Recursion and the call stack", (await lib.LectureAsync("rec-1"))!["title"]!.GetValue<string>());
        Assert.NotEmpty((await lib.SearchAsync("midterm", null, 8))["passages"]!.AsArray());
        Assert.Null(await lib.RestoreAsync("rec-1")); // nothing left in the trash to bring back

        Assert.Equal(HttpStatusCode.Unauthorized, (await c.SendAsync(Req(HttpMethod.Post, "/api/v2/lectures/rec-1/restore", key: "wrong"))).StatusCode);
    }

    [Fact]
    public async Task A_lecture_past_its_time_in_the_trash_is_gone_and_the_overview_says_so()
    {
        using var dir = new TempDir();
        var (cfg, store) = Library(dir);
        using var _s = store;
        await using var site = await Site(cfg, store);
        var lib = new RemoteLibrary("http://localhost", "pw", site.Client);

        store.Trash("rec-2", DateTimeOffset.UtcNow - LibraryWeb.TrashKeeps - TimeSpan.FromMinutes(1));
        var overview = await lib.OverviewAsync();
        Assert.Equal(["rec-2"], overview["gone"]!.AsArray().Select(n => n!.GetValue<string>()));
        Assert.Null(await lib.RestoreAsync("rec-2"));
    }
}
