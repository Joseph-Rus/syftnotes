using StudyStash.Library;

namespace StudyStash.Core.Tests;

/// <summary>Claude's list_attachments and read_attachment, on the library itself and over its API from a laptop, and
/// the laptop's side of attaching: sending files, fetching one back, removing one.</summary>
public class AttachmentToolsTests
{
    static (Config Cfg, Store Store) Library(TempDir dir)
    {
        var cfg = new Config(dir["home"], dir["pool"]) { PoolPassword = "pw", OllamaEnabled = false, Classes = [new ClassDef("Bio 110")] };
        Directory.CreateDirectory(cfg.Home);
        Configs.Save(cfg);
        var store = new Store(cfg.DbPath, cfg.PoolDir);
        store.Save(new Meeting("n1") { Title = "Cells", Date = "2026-09-01", Transcript = "osmosis" }, new Classification("Bio 110", 1, "folder"));
        return (cfg, store);
    }

    static void Attach(Store store, string id, string? noteId, string name, string? words, long size = 2_516_582)
    {
        File.WriteAllText(Path.Combine(store.AttachmentsDir("Bio 110"), name), "x");
        store.AddAttachment(new Attachment(id, name, name, "Bio 110", noteId, size, name.EndsWith(".png") ? "image/png" : "application/pdf", "2026-09-01T00:00:00Z"));
        if (words is not null) store.SetAttachmentText(id, words);
    }

    static async Task Check(ILibrarySource lib)
    {
        string all = await ClaudeTools.ListAttachmentsAsync(lib, "Bio 110", null);
        Assert.Contains("- [own] my notes.pdf — Bio 110, lecture n1, the student's own notes, 2.4 MB; read_attachment reads it", all);
        Assert.Contains("- [deck] Week 1 slides.pdf — Bio 110, slides, 2.4 MB; its words are still being read", all);
        Assert.Contains("- [blank] IMG_2.png — Bio 110, lecture n1, the student's own notes, 2.4 MB; no words could be read from it", all);
        Assert.DoesNotContain("deck", await ClaudeTools.ListAttachmentsAsync(lib, null, "n1"));
        Assert.Equal("Nothing is attached to that lecture.", await ClaudeTools.ListAttachmentsAsync(lib, null, "n9"));

        Assert.Equal("Mitochondria make ATP.", await ClaudeTools.ReadAttachmentAsync(lib, "own", 0));
        Assert.Equal("ATP.", await ClaudeTools.ReadAttachmentAsync(lib, "own", 18));
        Assert.Equal("Week 1 slides.pdf's words are still being read. Try again in a minute.", await ClaudeTools.ReadAttachmentAsync(lib, "deck", 0));
        Assert.Equal("No words could be read from IMG_2.png.", await ClaudeTools.ReadAttachmentAsync(lib, "blank", 0));
        Assert.Equal("There's no attachment nope. list_attachments gives their ids.", await ClaudeTools.ReadAttachmentAsync(lib, "nope", 0));
    }

    static void Seed(Store store)
    {
        Attach(store, "own", "n1", "my notes.pdf", "Mitochondria make ATP.");
        Attach(store, "deck", null, "Week 1 slides.pdf", null);
        Attach(store, "blank", "n1", "IMG_2.png", "");
    }

    [Fact]
    public async Task Claude_lists_and_reads_attachments_on_the_library_itself()
    {
        using var dir = new TempDir();
        var (cfg, store) = Library(dir);
        using var owned = store;
        Seed(store);
        await Check(new LocalLibrary(new LibraryReader(cfg, store)));
    }

    [Fact]
    public async Task Claude_lists_and_reads_attachments_from_a_laptop()
    {
        using var dir = new TempDir();
        var (cfg, store) = Library(dir);
        using var owned = store;
        Seed(store);
        await using var site = await TestSite.StartAsync(b => LibraryWeb.Build(b, cfg, store, new Pipeline(cfg, store, log: _ => { }), new LibraryWebOptions
        {
            ListModels = _ => Task.FromResult<List<(string, double)>?>([]), Latest = _ => Task.FromResult<Release?>(null),
            // The one still being read stays that way.
            ReadDocument = (_, ct) => Task.Delay(Timeout.Infinite, ct).ContinueWith(_ => (string?)null),
        }));
        await Check(new RemoteLibrary("http://localhost", "pw", site.Client));
    }

    [Fact]
    public async Task A_laptop_attaches_files_fetches_one_back_and_removes_it()
    {
        using var dir = new TempDir();
        var (cfg, store) = Library(dir);
        using var owned = store;
        await using var site = await TestSite.StartAsync(b => LibraryWeb.Build(b, cfg, store, new Pipeline(cfg, store, log: _ => { }), new LibraryWebOptions
        {
            ListModels = _ => Task.FromResult<List<(string, double)>?>([]), Latest = _ => Task.FromResult<Release?>(null),
            ReadDocument = (_, _) => Task.FromResult<string?>("words"),
        }));
        var lib = new RemoteLibrary("http://localhost", "pw", site.Client);
        File.WriteAllText(dir["Week 2.pdf"], "%PDF-1.7 two");
        File.WriteAllBytes(dir["page.png"], [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1]);

        var kept = await lib.AttachAsync([dir["Week 2.pdf"], dir["page.png"]], null, "n1");
        Assert.Equal(["Week 2.pdf", "page.png"], kept.Select(a => a!["name"]!.GetValue<string>()));
        var list = await lib.AttachmentListAsync(null, "n1");
        Assert.Equal(2, list!["attachments"]!.AsArray().Count);
        string id = kept[0]!["id"]!.GetValue<string>();
        await lib.DownloadAttachmentAsync(id, dir["back/Week 2.pdf"]);
        Assert.Equal("%PDF-1.7 two", File.ReadAllText(dir["back/Week 2.pdf"]));
        Assert.True(await lib.RemoveAttachmentAsync(id));
        Assert.False(await lib.RemoveAttachmentAsync(id));
        await Assert.ThrowsAsync<LibraryRefusedException>(() => lib.DownloadAttachmentAsync(id, dir["again.pdf"]));

        var toClass = await lib.AttachAsync([dir["Week 2.pdf"]], "Bio 110", null);
        Assert.Null(toClass[0]!["lecture"]);
        var refused = await Assert.ThrowsAsync<LibraryRefusedException>(() => lib.AttachAsync([dir["Week 2.pdf"]], null, "nope"));
        Assert.Contains("no such lecture", refused.Message);
        await Assert.ThrowsAsync<LibraryRefusedException>(() => new RemoteLibrary("http://localhost", "wrong", site.Client).AttachAsync([dir["Week 2.pdf"]], "Bio 110", null));
    }
}
