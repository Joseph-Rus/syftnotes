using System.Text.Json.Nodes;
using StudyStash.App.ViewModels;
using StudyStash.Core;

namespace StudyStash.App.Tests;

/// <summary>The attachments list under a lecture or a class: what it shows, attaching, opening, removing, "Reading your
/// handwriting…" until the library's done, and the offer to rewrite notes written without them.</summary>
public class AttachmentsModelTests
{
    /// <summary>A library that keeps attachments in a list, reads each one's words when told to, and remembers what it was asked.</summary>
    sealed class FakeLibrary : IAttachmentLibrary
    {
        public List<JsonObject> Kept { get; } = [];
        public bool Rewrite { get; set; }
        public bool Older { get; set; }
        public Exception? Fails { get; set; }
        public List<(List<string> Paths, string? Class, string? Lecture)> Sent { get; } = [];
        public List<string> Removed { get; } = [];
        public List<string> Rewritten { get; } = [];
        public int Listed { get; private set; }
        readonly object listing = new();

        /// <summary>Changes what's kept while the model may be listing it from another thread.</summary>
        public void Edit(Action change)
        {
            lock (listing) change();
        }

        public Task<JsonObject?> AttachmentListAsync(string? className, string? lectureId, CancellationToken stop = default)
        {
            lock (listing)
            {
                Listed++;
                if (Fails is not null) throw Fails;
                if (Older) return Task.FromResult<JsonObject?>(null);
                var result = new JsonObject { ["attachments"] = new JsonArray([.. Kept.Select(k => (JsonNode)k.DeepClone())]) };
                if (lectureId is not null) result["rewrite"] = Rewrite;
                return Task.FromResult<JsonObject?>(result);
            }
        }

        public Task<JsonArray> AttachAsync(IEnumerable<string> paths, string? className, string? lectureId, CancellationToken stop = default)
        {
            if (Fails is not null) throw Fails;
            var list = paths.ToList();
            Sent.Add((list, className, lectureId));
            foreach (string p in list) Kept.Add(Item("k" + Kept.Count, Path.GetFileName(p), reading: true));
            return Task.FromResult(new JsonArray());
        }

        public Task<bool> RemoveAttachmentAsync(string id)
        {
            if (Fails is not null) throw Fails;
            Removed.Add(id);
            return Task.FromResult(Kept.RemoveAll(k => k["id"]!.GetValue<string>() == id) > 0);
        }

        public Task DownloadAttachmentAsync(string id, string path, CancellationToken stop = default)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "file " + id);
            return Task.CompletedTask;
        }

        public Task RewriteAsync(string id)
        {
            Rewritten.Add(id);
            return Task.CompletedTask;
        }

        public void ReadAll()
        {
            foreach (var k in Kept)
            {
                k["reading"] = false;
                k["hasText"] = true;
            }
        }
    }

    static JsonObject Item(string id, string name, bool reading = false, bool hasText = true, long size = 2_516_582, string? type = null) => new()
    {
        ["id"] = id, ["name"] = name, ["class"] = "CS 101", ["lecture"] = "l1", ["size"] = size,
        ["type"] = type ?? (name.EndsWith(".png") ? "image/png" : "application/pdf"), ["added"] = "2026-09-23T10:00:00Z", ["hasText"] = hasText, ["reading"] = reading,
    };

    [Fact]
    public async Task A_lectures_files_read_plainly()
    {
        var lib = new FakeLibrary();
        lib.Kept.AddRange([Item("a", "Week 3 slides.pdf"), Item("b", "IMG_2231.png", reading: true), Item("c", "Syllabus.pdf", hasText: false, size: 830_000)]);
        using var model = new AttachmentsModel(lib, "CS 101", "l1") { PollEvery = TimeSpan.FromMilliseconds(5) };
        var loading = model.LoadAsync();
        await Task.Delay(30, TestContext.Current.CancellationToken);
        Assert.True(model.HasItems);
        Assert.Equal(["Week 3 slides.pdf", "IMG_2231.png", "Syllabus.pdf"], model.Items.Select(i => i.Name));
        Assert.Equal("Slides · 2.4 MB", model.Items[0].About);
        Assert.Equal("Your notes · 2.4 MB · Reading your handwriting…", model.Items[1].About);
        Assert.Equal("Handout · 811 KB · No words to read in it", model.Items[2].About);
        Assert.Equal(["slideshow", "image", "description"], model.Items.Select(i => i.Icon));
        Assert.True(model.Reading);

        // It keeps looking until the library has read them.
        int before = lib.Listed;
        lib.ReadAll();
        await loading.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.False(model.Reading);
        Assert.True(lib.Listed > before);
        Assert.Equal("Your notes · 2.4 MB", model.Items[1].About);
    }

    [Fact]
    public async Task A_lecture_with_a_handout_heads_its_list_Attachments_not_Whiteboard_photos()
    {
        var lib = new FakeLibrary();
        lib.Kept.Add(Item("a", "Syllabus.pdf"));
        using var model = new AttachmentsModel(lib, "CS 101", "l1");
        Assert.Equal("Whiteboard photos", model.HeaderText);
        var changed = new List<string?>();
        model.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        await model.LoadAsync();
        // The page shows the new heading only if it hears it changed (it kept "Whiteboard photos" over a PDF before).
        Assert.Contains(nameof(model.HeaderText), changed);
        Assert.Equal(AttachmentWords.Heading, model.HeaderText);
    }

    [Fact]
    public async Task Attaching_sends_the_files_for_the_lecture_and_lists_them()
    {
        using var dir = new TempHome();
        File.WriteAllText(dir["notes.pdf"], "%PDF");
        var lib = new FakeLibrary();
        using var model = new AttachmentsModel(lib, "CS 101", "l1") { PollEvery = TimeSpan.FromMilliseconds(5), PickFiles = () => Task.FromResult<IReadOnlyList<string>>([dir["notes.pdf"]]) };
        await model.LoadAsync();
        Assert.False(model.HasItems);

        var attaching = model.AttachCommand.ExecuteAsync(null);
        await Task.Delay(30, TestContext.Current.CancellationToken);
        Assert.Equal([dir["notes.pdf"]], lib.Sent[0].Paths);
        Assert.Equal(("CS 101", "l1"), (lib.Sent[0].Class, lib.Sent[0].Lecture));
        Assert.True(model.Reading);
        lib.ReadAll();
        await attaching.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.Equal("notes.pdf", Assert.Single(model.Items).Name);
        Assert.False(model.Busy);
    }

    [Fact]
    public async Task Dropping_files_attaches_only_the_ones_that_are_there_and_nothing_when_cancelled()
    {
        using var dir = new TempHome();
        File.WriteAllText(dir["a.png"], "x");
        var lib = new FakeLibrary();
        using var model = new AttachmentsModel(lib, "CS 101", null) { PollEvery = TimeSpan.FromMilliseconds(5), PickFiles = () => Task.FromResult<IReadOnlyList<string>>([]) };
        await model.AttachCommand.ExecuteAsync(null);
        await model.AddAsync([dir["missing.pdf"]]);
        Assert.Empty(lib.Sent);
        lib.Kept.Clear();
        var adding = model.AddAsync([dir["a.png"], dir["missing.pdf"]]);
        lib.ReadAll();
        await adding;
        Assert.Equal([dir["a.png"]], lib.Sent[0].Paths);
        Assert.Equal(("CS 101", (string?)null), (lib.Sent[0].Class, lib.Sent[0].Lecture));
    }

    [Fact]
    public async Task Opening_copies_the_file_from_the_library_then_opens_it()
    {
        using var dir = new TempHome();
        var lib = new FakeLibrary();
        lib.Kept.Add(Item("a", "../slides.pdf"));
        string? opened = null;
        using var model = new AttachmentsModel(lib, "CS 101", "l1") { CacheDir = dir["cache"], OpenFile = p => opened = p };
        await model.LoadAsync();
        await model.OpenCommand.ExecuteAsync(model.Items[0]);
        Assert.Equal(Path.Combine(dir["cache"], "a", "slides.pdf"), opened);
        Assert.Equal("file a", File.ReadAllText(opened!));
    }

    [Fact]
    public async Task Removing_takes_it_off_the_list_and_the_library()
    {
        var lib = new FakeLibrary();
        lib.Kept.AddRange([Item("a", "one.pdf"), Item("b", "two.pdf")]);
        using var model = new AttachmentsModel(lib, "CS 101", "l1");
        await model.LoadAsync();
        await model.RemoveCommand.ExecuteAsync(model.Items[0]);
        Assert.Equal(["a"], lib.Removed);
        Assert.Equal("two.pdf", Assert.Single(model.Items).Name);
    }

    [Fact]
    public async Task Notes_written_without_them_offer_a_rewrite_which_asks_the_library_once()
    {
        var lib = new FakeLibrary { Rewrite = true };
        lib.Kept.Add(Item("a", "my notes.pdf"));
        bool reload = false;
        using var model = new AttachmentsModel(lib, "CS 101", "l1") { Rewriting = () => reload = true };
        await model.LoadAsync();
        Assert.True(model.OfferRewrite);
        await model.RewriteCommand.ExecuteAsync(null);
        Assert.Equal(["l1"], lib.Rewritten);
        Assert.False(model.OfferRewrite);
        Assert.Equal(AttachmentWords.Rewriting, model.Said);
        Assert.True(reload);

        // A class's list never offers one.
        using var cls = new AttachmentsModel(lib, "CS 101", null);
        await cls.LoadAsync();
        Assert.False(cls.OfferRewrite);
        await cls.RewriteCommand.ExecuteAsync(null);
        Assert.Single(lib.Rewritten);
    }

    [Fact]
    public async Task A_library_that_cant_be_reached_or_is_older_says_so()
    {
        var lib = new FakeLibrary { Older = true };
        using var model = new AttachmentsModel(lib, "CS 101", "l1");
        await model.LoadAsync();
        Assert.Equal(AttachmentWords.OlderLibrary, model.Problem);
        lib.Older = false;
        lib.Fails = new HttpRequestException("refused");
        await model.LoadAsync();
        Assert.Equal(AttachmentWords.Unreachable, model.Problem);
        Assert.True(model.HasProblem);
        lib.Fails = new LibraryRefusedException(413, "That's more than 200 MB at once. Attach fewer files, or smaller ones.");
        using var dir = new TempHome();
        File.WriteAllText(dir["big.pdf"], "x");
        await model.AddAsync([dir["big.pdf"]]);
        Assert.False(model.Busy);
    }

    [Fact]
    public async Task A_refusal_says_the_librarys_own_words()
    {
        using var dir = new TempHome();
        File.WriteAllText(dir["big.pdf"], "x");
        var lib = new FakeLibrary();
        var refusing = new Refusing(lib);
        using var model = new AttachmentsModel(refusing, "CS 101", "l1");
        await model.AddAsync([dir["big.pdf"]]);
        Assert.Equal("That's more than 200 MB at once. Attach fewer files, or smaller ones.", model.Problem);
    }

    /// <summary>Lists fine; refuses every upload as too big.</summary>
    sealed class Refusing(FakeLibrary inner) : IAttachmentLibrary
    {
        public Task<JsonObject?> AttachmentListAsync(string? c, string? l, CancellationToken s = default) => inner.AttachmentListAsync(c, l, s);
        public Task<JsonArray> AttachAsync(IEnumerable<string> p, string? c, string? l, CancellationToken s = default) =>
            throw new LibraryRefusedException(413, "That's more than 200 MB at once. Attach fewer files, or smaller ones.");
        public Task<bool> RemoveAttachmentAsync(string id) => inner.RemoveAttachmentAsync(id);
        public Task DownloadAttachmentAsync(string id, string path, CancellationToken s = default) => inner.DownloadAttachmentAsync(id, path, s);
        public Task RewriteAsync(string id) => inner.RewriteAsync(id);
    }

    [Fact]
    public async Task Leaving_the_lecture_stops_it_looking_again()
    {
        var lib = new FakeLibrary();
        lib.Kept.Add(Item("a", "IMG_1.png", reading: true));
        var model = new AttachmentsModel(lib, "CS 101", "l1") { PollEvery = TimeSpan.FromMilliseconds(5) };
        var loading = model.LoadAsync();
        await Task.Delay(20, TestContext.Current.CancellationToken);
        model.Dispose();
        await loading.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        int after = lib.Listed;
        await Task.Delay(30, TestContext.Current.CancellationToken);
        Assert.Equal(after, lib.Listed);
    }
    static JsonObject Slide(int n, string title, double? at) => new() { ["n"] = n, ["title"] = title, ["at"] = at };

    [Fact]
    public async Task A_deck_makes_cards_the_placed_ones_first_in_time_order_and_waits_while_its_pages_are_read()
    {
        using var dir = new TempHome();
        var lib = new FakeLibrary();
        var week5 = Item("a", "Week 5 slides.pdf");
        week5["slides"] = new JsonArray(Slide(1, "Why attention", 600), Slide(2, "Queries and keys", null), Slide(3, "Softmax", 240), Slide(4, "Heads", 600));
        var week6 = Item("b", "Week 6 slides.pdf");
        week6["slides"] = null;
        lib.Kept.AddRange([week5, week6]);
        double? jumpedTo = null;
        IReadOnlyList<SlideItem>? onTranscript = null;
        string? opened = null;
        using var model = new AttachmentsModel(lib, "CS 101", "l1", at => jumpedTo = at, pinned => onTranscript = pinned)
        {
            PollEvery = TimeSpan.FromMilliseconds(5), CacheDir = dir["cache"], OpenFile = p => opened = p,
        };
        var loading = model.LoadAsync();
        await Wait.UntilAsync(() => model.HasPinned, "the slides to show");

        // Placed ones first, by the moment they came up (a tie by number); the rest after, as the deck has them.
        Assert.Equal(["Softmax", "Why attention", "Heads"], model.PinnedSlides.Select(s => s.Title));
        Assert.Equal(["Queries and keys"], model.UnpinnedSlides.Select(s => s.Title));
        Assert.Equal(["04:00", "10:00", "10:00"], model.PinnedSlides.Select(s => s.Moment));
        Assert.Equal(model.PinnedSlides, onTranscript);

        // The other deck's pages are still being read: it says so, and the list keeps looking.
        Assert.True(model.ReadingSlides);
        Assert.False(loading.IsCompleted);
        lib.Edit(() => week6["slides"] = new JsonArray(Slide(1, "Layer norm", null)));
        await loading.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.False(model.ReadingSlides);
        Assert.Equal(["Queries and keys", "Layer norm"], model.UnpinnedSlides.Select(s => s.Title));

        // A placed slide takes the page to its moment; one that couldn't be placed opens its deck.
        await model.OpenSlideCommand.ExecuteAsync(model.PinnedSlides[0]);
        Assert.Equal(240, jumpedTo);
        Assert.Null(opened);
        await model.OpenSlideCommand.ExecuteAsync(model.UnpinnedSlides[1]);
        Assert.Equal(Path.Combine(dir["cache"], "b", "Week 6 slides.pdf"), opened);

        // Taking the deck away takes its cards away, and the transcript's.
        await model.RemoveCommand.ExecuteAsync(model.Items.First(i => i.Id == "a"));
        Assert.False(model.HasPinned);
        Assert.Empty(onTranscript!);
        Assert.Equal(["Layer norm"], model.UnpinnedSlides.Select(s => s.Title));
    }
}
