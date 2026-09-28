using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using StudyStash.Library;

namespace StudyStash.Core.Tests;

/// <summary>/api/v2/attachments: adding files to a lecture or class, listing them, fetching and removing one, and the
/// words read from each, all with the library's password; and nothing sent can reach outside the class's folder.</summary>
public class AttachmentsApiTests
{
    static (Config Cfg, Store Store) Library(TempDir dir, string password = "pw")
    {
        var cfg = new Config(dir["home"], dir["pool"])
        {
            PoolName = "Sam's library", PoolPassword = password, OllamaEnabled = false,
            Classes = [new ClassDef("CS 101"), new ClassDef("BIO 110")],
        };
        Directory.CreateDirectory(cfg.Home);
        Configs.Save(cfg);
        var store = new Store(cfg.DbPath, cfg.PoolDir);
        store.Save(new Meeting("n1") { Title = "Cells", Date = "2026-09-01", Transcript = new string('t', 2000) },
            new Classification("BIO 110", 1, "folder"), "## Summary\nCells.", "qwen3");
        return (cfg, store);
    }

    /// <summary>Reads a document the way the test says: its words are its bytes as text, unless it's told otherwise.</summary>
    static LibraryWebOptions Options(Func<string, CancellationToken, Task<string?>>? read = null) => new()
    {
        ListModels = _ => Task.FromResult<List<(string, double)>?>([]),
        Latest = _ => Task.FromResult<Release?>(null),
        ReadDocument = read ?? ((path, _) => Task.FromResult<string?>(File.ReadAllText(path) is var t && t.StartsWith("%PDF", StringComparison.Ordinal) ? t[8..] : null)),
    };

    static Task<TestSite> Site(Config cfg, Store store, LibraryWebOptions? options = null) =>
        TestSite.StartAsync(b => LibraryWeb.Build(b, cfg, store, new Pipeline(cfg, store, log: _ => { }), options ?? Options()));

    static HttpRequestMessage Req(HttpMethod m, string path, HttpContent? content = null, string key = "pw")
    {
        var r = new HttpRequestMessage(m, path) { Content = content };
        if (key.Length > 0) r.Headers.Authorization = new("Bearer", key);
        return r;
    }

    static MultipartFormDataContent Form(IEnumerable<(string Name, byte[] Bytes)> files, string? cls = null, string? lecture = null)
    {
        var form = new MultipartFormDataContent();
        if (cls is not null) form.Add(new StringContent(cls), "class");
        if (lecture is not null) form.Add(new StringContent(lecture), "lecture");
        foreach (var (name, bytes) in files) form.Add(new ByteArrayContent(bytes), "file", name);
        return form;
    }

    static byte[] Pdf(string words) => Encoding.UTF8.GetBytes("%PDF-1.7" + words);

    static async Task<JsonObject> Json(HttpResponseMessage r) => (JsonObject)JsonNode.Parse(await r.Content.ReadAsStringAsync())!;

    static async Task<JsonObject> Upload(TestSite site, MultipartFormDataContent form, HttpStatusCode expect = HttpStatusCode.OK)
    {
        var r = await site.Client.SendAsync(Req(HttpMethod.Post, "/api/v2/attachments", form));
        Assert.True(expect == r.StatusCode, $"{r.StatusCode}: {await r.Content.ReadAsStringAsync()}");
        return await Json(r);
    }

    static async Task Read(Store store, string id)
    {
        for (int i = 0; i < 200 && store.GetAttachment(id)?.State == Attachment.Reading; i++) await Task.Delay(20);
    }

    [Fact]
    public async Task A_file_attached_to_a_lecture_is_kept_in_its_class_folder_and_listed()
    {
        using var dir = new TempDir();
        var (cfg, store) = Library(dir);
        using var owned = store;
        await using var site = await Site(cfg, store);

        var body = await Upload(site, Form([("Handwritten notes.pdf", Pdf("Osmosis moves water."))], lecture: "n1"));
        var a = (JsonObject)Assert.Single((JsonArray)body["attachments"]!)!;
        Assert.Equal("Handwritten notes.pdf", a["name"]!.GetValue<string>());
        Assert.Equal("BIO 110", a["class"]!.GetValue<string>());
        Assert.Equal("n1", a["lecture"]!.GetValue<string>());
        Assert.Equal(Pdf("Osmosis moves water.").Length, a["size"]!.GetValue<long>());
        Assert.Equal("application/pdf", a["type"]!.GetValue<string>());
        Assert.True(DateTimeOffset.TryParse(a["added"]!.GetValue<string>(), out _));
        Assert.False(a["hasText"]!.GetValue<bool>());
        string id = a["id"]!.GetValue<string>();
        Assert.True(File.Exists(Path.Combine(cfg.PoolDir, "BIO 110", "Attachments", "Handwritten notes.pdf")));

        await Read(store, id);
        var list = await Json(await site.Client.SendAsync(Req(HttpMethod.Get, "/api/v2/attachments?lecture=n1")));
        var listed = (JsonObject)Assert.Single((JsonArray)list["attachments"]!)!;
        Assert.True(listed["hasText"]!.GetValue<bool>());
        Assert.False(listed["reading"]!.GetValue<bool>());
        Assert.True(list["rewrite"]!.GetValue<bool>()); // its notes were written before it came
        var byClass = await Json(await site.Client.SendAsync(Req(HttpMethod.Get, "/api/v2/attachments?class=BIO%20110")));
        Assert.Single((JsonArray)byClass["attachments"]!);
        var other = await Json(await site.Client.SendAsync(Req(HttpMethod.Get, "/api/v2/attachments?class=CS%20101")));
        Assert.Empty((JsonArray)other["attachments"]!);
        Assert.Null(other["rewrite"]);

        var text = await Json(await site.Client.SendAsync(Req(HttpMethod.Get, $"/api/v2/attachments/{id}/text")));
        Assert.Equal("Osmosis moves water.", text["text"]!.GetValue<string>());
    }

    [Fact]
    public async Task Several_files_go_in_one_request_and_a_taken_name_gets_a_number()
    {
        using var dir = new TempDir();
        var (cfg, store) = Library(dir);
        using var owned = store;
        await using var site = await Site(cfg, store);
        var body = await Upload(site, Form([("slides.pdf", Pdf("one")), ("slides.pdf", Pdf("two")), ("photo.jpg", [0xFF, 0xD8, 0xFF, 0xE0, 1, 2])], cls: "CS 101"));
        var names = ((JsonArray)body["attachments"]!).Select(a => a!["name"]!.GetValue<string>()).ToList();
        Assert.Equal(["slides.pdf", "slides.pdf", "photo.jpg"], names);
        Assert.True(File.Exists(Path.Combine(cfg.PoolDir, "CS 101", "Attachments", "slides (2).pdf")));
        Assert.Equal("image/jpeg", body["attachments"]![2]!["type"]!.GetValue<string>());
        Assert.All((JsonArray)body["attachments"]!, a => Assert.Null(a!["lecture"]));
    }

    [Fact]
    public async Task Where_a_file_goes_is_checked()
    {
        using var dir = new TempDir();
        var (cfg, store) = Library(dir);
        using var owned = store;
        await using var site = await Site(cfg, store);
        var none = await Upload(site, Form([("loose.pdf", Pdf("x"))]));
        Assert.Equal(Configs.Unsorted, none["attachments"]![0]!["class"]!.GetValue<string>());
        await Upload(site, Form([("x.pdf", Pdf("x"))], cls: "Underwater Basket Weaving"), HttpStatusCode.BadRequest);
        await Upload(site, Form([("x.pdf", Pdf("x"))], lecture: "no-such-lecture"), HttpStatusCode.NotFound);
        await Upload(site, Form([], cls: "CS 101"), HttpStatusCode.BadRequest);
        var empty = await Upload(site, Form([("empty.pdf", [])], cls: "CS 101"), HttpStatusCode.BadRequest);
        Assert.Contains("empty.pdf", empty["detail"]!.GetValue<string>());
        var notAForm = await site.Client.SendAsync(Req(HttpMethod.Post, "/api/v2/attachments", new StringContent("{}", Encoding.UTF8, "application/json")));
        Assert.Equal(HttpStatusCode.BadRequest, notAForm.StatusCode);
        Assert.Single(store.ListAttachments());
        // Nothing half-sent is left behind.
        Assert.Empty(Directory.EnumerateFiles(Path.Combine(cfg.PoolDir, ".attachments-incoming"), "*", SearchOption.AllDirectories));
    }

    [Theory]
    [InlineData("../../../outside.pdf")]
    [InlineData("..\\..\\..\\outside.pdf")]
    [InlineData("/etc/outside.pdf")]
    [InlineData("C:\\Windows\\outside.pdf")]
    public async Task A_name_with_a_path_stays_inside_the_class_folder(string name)
    {
        using var dir = new TempDir();
        var (cfg, store) = Library(dir);
        using var owned = store;
        await using var site = await Site(cfg, store);
        var body = await Upload(site, Form([(name, Pdf("x"))], cls: "CS 101"));
        Assert.Equal("outside.pdf", body["attachments"]![0]!["name"]!.GetValue<string>());
        Assert.True(File.Exists(Path.Combine(cfg.PoolDir, "CS 101", "Attachments", "outside.pdf")));
        Assert.Single(Directory.EnumerateFiles(dir.Path, "outside.pdf", SearchOption.AllDirectories));
    }

    [Theory]
    [InlineData("/api/v2/attachments/..%2F..%2Fstate.db/raw")]
    [InlineData("/api/v2/attachments/%2E%2E/raw")]
    [InlineData("/api/v2/attachments/nope/raw")]
    public async Task An_id_that_isnt_one_finds_nothing(string path)
    {
        using var dir = new TempDir();
        var (cfg, store) = Library(dir);
        using var owned = store;
        await using var site = await Site(cfg, store);
        var r = await site.Client.SendAsync(Req(HttpMethod.Get, path));
        Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await site.Client.SendAsync(Req(HttpMethod.Delete, "/api/v2/attachments/..%2F..%2Fstate.db"))).StatusCode);
        Assert.True(File.Exists(cfg.DbPath));
    }

    [Fact]
    public async Task A_row_pointing_outside_the_library_is_never_served()
    {
        using var dir = new TempDir();
        var (cfg, store) = Library(dir);
        using var owned = store;
        File.WriteAllText(dir["secret.txt"], "secret");
        store.AddAttachment(new Attachment("bad", "secret.txt", "../../../secret.txt", "CS 101", null, 6, "text/plain", "2026-09-01T00:00:00Z", State: Attachment.Read));
        await using var site = await Site(cfg, store);
        Assert.Equal(HttpStatusCode.NotFound, (await site.Client.SendAsync(Req(HttpMethod.Get, "/api/v2/attachments/bad/raw"))).StatusCode);
    }

    [Fact]
    public async Task The_file_comes_back_as_what_its_bytes_say_and_a_page_is_only_ever_downloaded()
    {
        using var dir = new TempDir();
        var (cfg, store) = Library(dir);
        using var owned = store;
        await using var site = await Site(cfg, store);
        var body = await Upload(site, Form([("notes.pdf", Pdf("words")), ("trick.pdf", Encoding.UTF8.GetBytes("<html><script>alert(1)</script>"))], cls: "CS 101"));
        string pdf = body["attachments"]![0]!["id"]!.GetValue<string>(), html = body["attachments"]![1]!["id"]!.GetValue<string>();

        var r = await site.Client.SendAsync(Req(HttpMethod.Get, $"/api/v2/attachments/{pdf}/raw"));
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal("application/pdf", r.Content.Headers.ContentType!.MediaType);
        Assert.Equal("inline", r.Content.Headers.ContentDisposition!.DispositionType);
        Assert.Equal(Pdf("words"), await r.Content.ReadAsByteArrayAsync());
        Assert.Equal("nosniff", r.Headers.GetValues("X-Content-Type-Options").Single());

        var trick = await site.Client.SendAsync(Req(HttpMethod.Get, $"/api/v2/attachments/{html}/raw"));
        Assert.Equal("application/octet-stream", trick.Content.Headers.ContentType!.MediaType);
        Assert.Equal("attachment", trick.Content.Headers.ContentDisposition!.DispositionType);
        Assert.Equal("application/octet-stream", body["attachments"]![1]!["type"]!.GetValue<string>());
    }

    [Fact]
    public async Task Removing_one_takes_its_file_away()
    {
        using var dir = new TempDir();
        var (cfg, store) = Library(dir);
        using var owned = store;
        await using var site = await Site(cfg, store);
        var body = await Upload(site, Form([("slides.pdf", Pdf("x"))], cls: "CS 101"));
        string id = body["attachments"]![0]!["id"]!.GetValue<string>();
        var r = await site.Client.SendAsync(Req(HttpMethod.Delete, $"/api/v2/attachments/{id}"));
        Assert.Equal(id, (await Json(r))["deleted"]!.GetValue<string>());
        Assert.False(File.Exists(Path.Combine(cfg.PoolDir, "CS 101", "Attachments", "slides.pdf")));
        Assert.Equal(HttpStatusCode.NotFound, (await site.Client.SendAsync(Req(HttpMethod.Get, $"/api/v2/attachments/{id}/raw"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await site.Client.SendAsync(Req(HttpMethod.Delete, $"/api/v2/attachments/{id}"))).StatusCode);
    }

    /// <summary>Zeros, as many as asked, made as they're read: a big upload without holding it in memory.</summary>
    sealed class Zeros(long length) : Stream
    {
        long left = length;
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count)
        {
            int n = (int)Math.Min(count, left);
            Array.Clear(buffer, offset, n);
            left -= n;
            return n;
        }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    [Fact]
    public async Task More_than_200_MB_at_once_is_refused_and_nothing_is_kept()
    {
        using var dir = new TempDir();
        var (cfg, store) = Library(dir);
        using var owned = store;
        await using var site = await Site(cfg, store);

        // Said up front: refused before a byte is read.
        var said = new ByteArrayContent([1]);
        var saidForm = new MultipartFormDataContent { { said, "file", "big.pdf" } };
        var request = Req(HttpMethod.Post, "/api/v2/attachments", saidForm);
        await saidForm.LoadIntoBufferAsync();
        request.Content!.Headers.ContentLength = Attachments.MaxRequestBytes + 2 * 1024 * 1024;
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, (await site.Client.SendAsync(request)).StatusCode);

        // Not said (sent in chunks): stopped once the files pass 200 MB, across two parts.
        var form = new MultipartFormDataContent
        {
            { new StreamContent(new Zeros(150L * 1024 * 1024)), "file", "first.pdf" },
            { new StreamContent(new Zeros(51L * 1024 * 1024)), "file", "second.pdf" },
        };
        var chunked = Req(HttpMethod.Post, "/api/v2/attachments", form);
        chunked.Headers.TransferEncodingChunked = true;
        var r = await site.Client.SendAsync(chunked);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, r.StatusCode);
        Assert.Contains("200 MB", (await Json(r))["detail"]!.GetValue<string>());
        Assert.Empty(store.ListAttachments());
        Assert.DoesNotContain(Directory.EnumerateFiles(cfg.PoolDir, "*", SearchOption.AllDirectories), f => !f.EndsWith(".md", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Just_under_the_limit_is_kept()
    {
        using var dir = new TempDir();
        var (cfg, store) = Library(dir);
        using var owned = store;
        await using var site = await Site(cfg, store, Options((_, _) => Task.FromResult<string?>(null)));
        var form = new MultipartFormDataContent { { new StreamContent(new Zeros(Attachments.MaxRequestBytes)), "file", "big scan.pdf" } };
        var body = await Upload(site, form);
        Assert.Equal(Attachments.MaxRequestBytes, body["attachments"]![0]!["size"]!.GetValue<long>());
    }

    /// <summary>The real server (Kestrel) refuses bodies over 30 MB unless a route says otherwise: this one does.</summary>
    [Fact]
    public async Task The_real_server_takes_more_than_its_usual_30_MB_here_and_nowhere_else()
    {
        using var dir = new TempDir();
        var (cfg, store) = Library(dir);
        using var owned = store;
        var builder = Microsoft.AspNetCore.Builder.WebApplication.CreateSlimBuilder();
        Microsoft.AspNetCore.Hosting.WebHostBuilderKestrelExtensions.ConfigureKestrel(builder.WebHost, k => k.Listen(IPAddress.Loopback, 0));
        await using var app = LibraryWeb.Build(builder, cfg, store, new Pipeline(cfg, store, log: _ => { }), Options((_, _) => Task.FromResult<string?>(null)));
        await app.StartAsync();
        using var http = new HttpClient { BaseAddress = new Uri(app.Urls.Single()), Timeout = TimeSpan.FromMinutes(2) };

        var form = new MultipartFormDataContent { { new StreamContent(new Zeros(40L * 1024 * 1024)), "file", "scan.pdf" } };
        var r = await http.SendAsync(Req(HttpMethod.Post, "/api/v2/attachments", form));
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal(40L * 1024 * 1024, (await Json(r))["attachments"]![0]!["size"]!.GetValue<long>());

        // Another route keeps the usual limit.
        var big = new ByteArrayContent(new byte[31 * 1024 * 1024]);
        big.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        try
        {
            var other = await http.SendAsync(Req(HttpMethod.Post, "/api/v2/capture", big));
            Assert.NotEqual(HttpStatusCode.OK, other.StatusCode);
        }
        catch (HttpRequestException)
        {
            // Refused partway through sending: the usual limit, as it should be.
        }
        await app.StopAsync();
    }

    [Fact]
    public async Task Everything_needs_the_library_password()
    {
        using var dir = new TempDir();
        var (cfg, store) = Library(dir);
        using var owned = store;
        await using var site = await Site(cfg, store);
        var body = await Upload(site, Form([("slides.pdf", Pdf("x"))], cls: "CS 101"));
        string id = body["attachments"]![0]!["id"]!.GetValue<string>();
        foreach (string key in new[] { "", "wrong" })
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await site.Client.SendAsync(Req(HttpMethod.Post, "/api/v2/attachments", Form([("x.pdf", Pdf("x"))], cls: "CS 101"), key))).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await site.Client.SendAsync(Req(HttpMethod.Get, "/api/v2/attachments", key: key))).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await site.Client.SendAsync(Req(HttpMethod.Get, $"/api/v2/attachments/{id}/raw", key: key))).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await site.Client.SendAsync(Req(HttpMethod.Get, $"/api/v2/attachments/{id}/text", key: key))).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await site.Client.SendAsync(Req(HttpMethod.Delete, $"/api/v2/attachments/{id}", key: key))).StatusCode);
        }
        Assert.Single(store.ListAttachments());
        // The web page's link asks a browser to sign in first.
        var page = await site.Get($"/attachments/{id}");
        Assert.Equal(HttpStatusCode.SeeOther, page.StatusCode);
        Assert.StartsWith("/login", page.Headers.Location!.OriginalString);
        await site.PostForm("/login", ("password", "pw"), ("next", "/"));
        Assert.Equal(HttpStatusCode.OK, (await site.Get($"/attachments/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await site.Get("/attachments/nope")).StatusCode);
    }

    [Fact]
    public async Task While_its_words_are_read_it_says_so_and_a_document_with_none_says_that()
    {
        using var dir = new TempDir();
        var (cfg, store) = Library(dir);
        using var owned = store;
        var reading = new TaskCompletionSource<string?>();
        await using var site = await Site(cfg, store, Options((_, _) => reading.Task));
        var body = await Upload(site, Form([("IMG_1.png", [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0])], lecture: "n1"));
        string id = body["attachments"]![0]!["id"]!.GetValue<string>();
        Assert.True(body["attachments"]![0]!["reading"]!.GetValue<bool>());
        var list = await Json(await site.Client.SendAsync(Req(HttpMethod.Get, "/api/v2/attachments?lecture=n1")));
        Assert.True(list["attachments"]![0]!["reading"]!.GetValue<bool>());
        Assert.False(list["rewrite"]!.GetValue<bool>()); // nothing to rewrite with yet

        reading.SetResult(null);
        await Read(store, id);
        list = await Json(await site.Client.SendAsync(Req(HttpMethod.Get, "/api/v2/attachments?lecture=n1")));
        Assert.False(list["attachments"]![0]!["reading"]!.GetValue<bool>());
        Assert.False(list["attachments"]![0]!["hasText"]!.GetValue<bool>());
        Assert.False(list["rewrite"]!.GetValue<bool>());
    }

    [Fact]
    public async Task A_scanned_photo_attachment_gets_its_words_from_the_real_reader()
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var dir = new TempDir();
        var (cfg, store) = Library(dir);
        using var owned = store;
        // No ReadDocument override here: this goes through DocumentText.ExtractAsync itself, Vision included.
        await using var site = await Site(cfg, store, new LibraryWebOptions
        {
            ListModels = _ => Task.FromResult<List<(string, double)>?>([]),
            Latest = _ => Task.FromResult<Release?>(null),
        });
        byte[] photo = File.ReadAllBytes(DocumentOcrTests.Fixture("handwriting.png"));
        var body = await Upload(site, Form([("notes.png", photo)], lecture: "n1"));
        string id = body["attachments"]![0]!["id"]!.GetValue<string>();
        await Read(store, id);
        var list = await Json(await site.Client.SendAsync(Req(HttpMethod.Get, "/api/v2/attachments?lecture=n1")));
        Assert.True(list["attachments"]![0]!["hasText"]!.GetValue<bool>());
    }

    [Fact]
    public async Task A_reader_that_fails_leaves_the_attachment_without_words_not_stuck()
    {
        using var dir = new TempDir();
        var (cfg, store) = Library(dir);
        using var owned = store;
        await using var site = await Site(cfg, store, Options((_, _) => throw new InvalidOperationException("tesseract crashed")));
        var body = await Upload(site, Form([("notes.pdf", Pdf("x"))], lecture: "n1"));
        string id = body["attachments"]![0]!["id"]!.GetValue<string>();
        await Read(store, id);
        Assert.Equal(Attachment.Unread, store.GetAttachment(id)!.State);
    }

    [Fact]
    public async Task The_lecture_and_class_pages_show_attachments_and_offer_a_rewrite_only_when_there_are_some()
    {
        using var dir = new TempDir();
        var (cfg, store) = Library(dir);
        using var owned = store;
        var reading = new TaskCompletionSource<string?>();
        await using var site = await Site(cfg, store, Options((path, _) => path.EndsWith(".png") ? reading.Task : Task.FromResult<string?>("words")));
        await site.PostForm("/login", ("password", "pw"), ("next", "/"));
        string before = await site.Text("/note/n1"), classBefore = await site.Text("/class/BIO%20110");
        Assert.DoesNotContain("Attachments", before);
        Assert.DoesNotContain("Attachments", classBefore);

        var body = await Upload(site, Form([("My notes.pdf", Pdf("words"))], lecture: "n1"));
        await Upload(site, Form([("IMG_1.png", [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0])], cls: "BIO 110"));
        await Read(store, body["attachments"]![0]!["id"]!.GetValue<string>());

        string page = await site.Text("/note/n1");
        Assert.Contains("<h2>Attachments</h2>", page);
        Assert.Contains($"href=\"/attachments/{body["attachments"]![0]!["id"]}\"", page);
        Assert.Contains("My notes.pdf", page);
        Assert.Contains("The student&#x27;s own notes · 13 bytes", page);
        Assert.Contains("Rewrite notes with your attachments", page);
        Assert.DoesNotContain("IMG_1.png", page); // the class's own, not this lecture's
        // After the notes, not in their way.
        Assert.True(page.IndexOf("<h2>Attachments</h2>", StringComparison.Ordinal) > page.IndexOf("<div class=\"sheet\">", StringComparison.Ordinal));

        string cls = await site.Text("/class/BIO%20110");
        Assert.Contains("My notes.pdf", cls);
        Assert.Contains("Cells · The student&#x27;s own notes", cls);
        Assert.Contains("IMG_1.png", cls);
        Assert.Contains(LibraryWeb.ReadingWords, cls);
        Assert.Contains("data-refresh", cls);
        Assert.DoesNotContain("Rewrite notes with your attachments", cls);

        // Rewriting from the page queues the lecture; once it's written with them, the offer goes.
        await site.PostForm("/note/n1/resummarize");
        Assert.Equal(Store.Queued, store.Get("n1")!.Status);
        Assert.DoesNotContain("Rewrite notes with your attachments", await site.Text("/note/n1"));
        reading.SetResult(null);
    }

    [Fact]
    public async Task Attachments_a_restart_left_unread_are_read_when_the_library_starts()
    {
        using var dir = new TempDir();
        var (cfg, store) = Library(dir);
        using var owned = store;
        string folder = store.AttachmentsDir("BIO 110");
        File.WriteAllBytes(Path.Combine(folder, "notes.pdf"), Pdf("Mitosis."));
        store.AddAttachment(new Attachment("a1", "notes.pdf", "notes.pdf", "BIO 110", "n1", 16, "application/pdf", "2026-09-01T00:00:00Z"));
        await using var site = await Site(cfg, store);
        await Read(store, "a1");
        Assert.Equal("Mitosis.", store.GetAttachment("a1")!.Text);
    }
}
