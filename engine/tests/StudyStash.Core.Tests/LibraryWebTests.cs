using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using StudyStash.Core.Calendar;
using StudyStash.Library;

namespace StudyStash.Core.Tests;

/// <summary>The library's web pages and the laptop's ingest API, plus every page compared with the Python engine's own text.</summary>
public class LibraryWebTests
{
    static readonly List<(string, double)> Models = [("qwen3:1.7b", 1.4), ("gemma4:e4b", 9.6)];
    static readonly TailscaleInfo Tailnet = new(true, true, "Running", "mini.tail.ts.net", ["100.64.0.9"]);
    static readonly Action<string> Quiet = _ => { };

    static LibraryWebOptions Options(Func<double, Task<Release?>>? latest = null, bool ollama = true, TailscaleInfo? ts = null,
        Func<Release, string, Task>? apply = null, PasswordTries? tries = null) => new()
    {
        Tries = tries,
        ListModels = _ => Task.FromResult(ollama ? Models : null),
        Tailscale = () => ts ?? Tailnet,
        Latest = latest ?? (_ => Task.FromResult<Release?>(null)),
        Apply = apply,
        RamGb = () => 16,
        HostName = () => "library-pc",
        Nonce = () => "NONCE",
    };

    static async Task<TestSite> Site(Config cfg, Store store, Pipeline? pipeline = null, LibraryWebOptions? options = null) =>
        await TestSite.StartAsync(b => LibraryWeb.Build(b, cfg, store, pipeline ?? new Pipeline(cfg, store, log: Quiet), options ?? Options()));

    static Config MakeCfg(TempDir dir, string password = "", string admin = "") => new(dir["home"], dir["pool"])
    {
        PoolPassword = password, AdminPassword = admin, OllamaEnabled = false,
        Classes = [new ClassDef("CS 101", ["cs101"]), new ClassDef("Bio 110", ["biology"])],
    };

    static void SeedCells(Store store) => store.Save(new Meeting("n1")
    {
        Title = "Cells", Date = "2026-09-01", Owner = "Sam", NotesMarkdown = "# Cells\n\nmembranes",
        Transcript = "today we talk about the cell membrane and osmosis",
        Raw = new JsonObject { ["id"] = "n1", ["title"] = "Cells", ["date"] = "2026-09-01", ["notes"] = "# Cells\n\nmembranes" },
    }, new Classification(Configs.Unsorted, 0.3, "ollama", "Cells", ["cells"]));

    static void Sql(Config cfg, string sql)
    {
        using var conn = new SqliteConnection($"Data Source={cfg.DbPath};Pooling=False");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    // --- every page, as the Python engine serves it ------------------------------------------------------------

    /// <summary>The Python engine's own fixed library, lecture for lecture.</summary>
    static void SeedLibrary(Config cfg, Store store)
    {
        void Save(string id, string title, string date, string cls, string by, double conf, List<string> topics, string notes = "", string transcript = "") =>
            store.Save(new Meeting(id) { Title = title, Date = date, Owner = "Sam", NotesMarkdown = notes, Transcript = transcript },
                new Classification(cls, conf, by, "", topics));
        Save("n1", "Cells", "2026-09-01T09:00:00", "Bio 110", "folder", 0.95, ["cells", "membranes"], "membranes",
            "today we talk about the cell membrane and osmosis");
        Save("n2", "Loops & <b>ranges</b>", "2026-09-03", "CS 101", "ollama", 0.875, ["loops"], "for loops");
        Save("n3", "Lunch", "2026-09-02T12:30:00", "Unsorted", "none", 0.0, []);
        Save("n4", "Old class lecture", "2026-08-30", "History 9", "human", 1.0, [], "x");
        store.Enqueue(new Meeting("q1") { Title = "Waiting lecture", Date = "2026-09-04" });
        store.Enqueue(new Meeting("f1") { Title = "Broken lecture", Date = "2026-09-05" });
        Sql(cfg, $"UPDATE notes SET status='failed', error='Summary with big:35b failed: {new string('x', 200)}' WHERE id='f1'");
        // Queued a moment apart, as they were for Python: Windows' clock can give both the same time.
        Sql(cfg, "UPDATE notes SET updated_at='2100-01-01T00:00:00+00:00' WHERE id='q1'");
    }

    [Fact]
    public async Task Every_page_matches_the_python_engine_byte_for_byte()
    {
        using var dir = new TempDir();
        var cfg = new Config(dir["home"], dir["pool"])
        {
            PoolName = "Fall \"26\" & co", PoolPassword = "pw", OllamaModel = "qwen3:1.7b", SummaryModel = "gemma4:e4b", MinConfidence = 0.7,
            Classes = [new ClassDef("CS 101", ["cs101"], "Intro to programming"), new ClassDef("Bio 110"), new ClassDef("Calc II", ["math"], "Series.")],
        };
        using var store = new Store(cfg.DbPath, cfg.PoolDir);
        SeedLibrary(cfg, store);
        var newer = new Release("v9.9.9", [9, 9, 9], "https://x/v9.9.9.tar.gz", "https://x/releases/v9.9.9");
        var pages = Golden.PageCases()["library"]!.AsObject();
        await using var site = await Site(cfg, store);
        await using var noOllama = await Site(cfg, store, options: Options(_ => Task.FromResult<Release?>(newer), ollama: false,
            ts: new TailscaleInfo(), apply: (_, _) => Task.CompletedTask));
        foreach (var s in new[] { site, noOllama })
            await s.PostForm("/login", ("password", "pw"), ("next", "/"));
        foreach (var (key, want) in pages)
        {
            if (key.StartsWith("open:", StringComparison.Ordinal)) continue;
            // Settings has the AI picker (Claude, ChatGPT, Gemini), which only this engine has: it's checked on its own below.
            if (key.Contains("/settings", StringComparison.Ordinal)) continue;
            var (s, path) = key.StartsWith("no-ollama:", StringComparison.Ordinal) ? (noOllama, key[10..]) : (site, key);
            var r = await s.Get(path);
            Assert.True((int)r.StatusCode == want!["status"]!.GetValue<int>(), $"{key}: {(int)r.StatusCode}");
            Assert.Equal(want["csp"]?.S(), r.Headers.TryGetValues("Content-Security-Policy", out var csp) ? csp.Single() : null);
            string html = await r.Content.ReadAsStringAsync();
            if (html != want["html"].S()) Assert.Fail($"{key} differs from Python's:\n{FirstDifference(want["html"].S(), html)}");
        }
        cfg.PoolPassword = ""; // no password: the library is open, with no Log out
        Assert.Equal(pages["open:/"]!["html"].S(), await (await site.Stranger().GetAsync("/")).Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Every_page_opens_and_a_lecture_says_it_was_recorded()
    {
        using var dir = new TempDir();
        var cfg = new Config(dir["home"], dir["pool"])
        {
            PoolPassword = "pw", SummaryModel = "gemma4:e4b",
            Classes = [new ClassDef("CS 101", ["cs101"]), new ClassDef("Bio 110"), new ClassDef("Calc II")],
        };
        using var store = new Store(cfg.DbPath, cfg.PoolDir);
        SeedLibrary(cfg, store);
        // A lecture the laptop recorded: filed under the class it was recorded for, a transcript, no notes of its own,
        // and notes that failed to write.
        const string failed = "Summary with gemma4:e4b failed: the model is not installed";
        store.Save(new Meeting("r1") { Title = "Recursion", Date = "2026-09-11T10:00:00", Folder = "CS 101",
                Transcript = "today we trace a recursive call down to its base case" },
            new Classification("CS 101", 1.0, "folder", "Recursion and the call stack", ["recursion"]), error: failed);
        // One that came with notes typed in class, whose study notes also failed.
        store.Save(new Meeting("r2") { Title = "Osmosis", Date = "2026-09-12T09:00:00", Folder = "Bio 110",
                NotesMarkdown = "water follows salt", Transcript = "water crosses the membrane toward more salt" },
            new Classification("Bio 110", 1.0, "folder", "", []), error: failed);
        await using var site = await Site(cfg, store);
        var login = await site.Stranger().GetAsync("/login");
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var pages = new Dictionary<string, string> { ["/login"] = await login.Content.ReadAsStringAsync() };
        await site.PostForm("/login", ("password", "pw"), ("next", "/"));

        string[] paths = ["/", "/unsorted", "/search?q=cells", "/settings",
            .. new[] { "CS 101", "Bio 110", "Calc II", "History 9" }.Select(Ui.ClassUrl),
            .. new[] { "n1", "n2", "n3", "n4", "q1", "f1", "r1", "r2" }.Select(id => $"/note/{id}")];
        foreach (string path in paths)
        {
            var r = await site.Get(path);
            Assert.True(r.StatusCode == HttpStatusCode.OK, $"{path}: {(int)r.StatusCode}");
            pages[path] = await r.Content.ReadAsStringAsync();
        }
        // Each lecture says where its notes came from, in a recording's terms.
        string recorded = pages["/note/r1"];
        Assert.Contains("<dt>Summary</dt><dd>No study notes yet</dd>", recorded);
        Assert.Contains("Sorted by the class it was recorded for", recorded);
        Assert.Contains(failed + ".", recorded);
        Assert.DoesNotContain("Showing the notes it came with", recorded); // there are none to show instead
        Assert.Contains("<dt>Summary</dt><dd>The notes it came with</dd>", pages["/note/r2"]);
        Assert.Contains(failed + ". Showing the notes it came with.", pages["/note/r2"]);
        Assert.Contains("<section id=\"notes\" role=\"tabpanel\">", pages["/note/r2"]);
        Assert.Contains("<dt>Summary</dt><dd>No transcript, so no study notes</dd>", pages["/note/n3"]);
        Assert.Contains("No notes for this lecture yet.", pages["/note/n3"]);
    }

    [Fact]
    public async Task Settings_picks_the_ai_for_everything_and_for_each_kind_of_work()
    {
        using var dir = new TempDir();
        var cfg = new Config(dir["home"], dir["pool"]) { PoolPassword = "pw", Classes = [new ClassDef("CS 101")] };
        using var store = new Store(cfg.DbPath, cfg.PoolDir);
        await using var site = await Site(cfg, store);
        await site.PostForm("/login", ("password", "pw"), ("next", "/"));
        string page = await site.Text("/settings");
        Assert.Contains("Does the work", page);
        Assert.Contains("<option value=\"claude\"", page);
        var r = await site.PostForm("/settings", ("ai_provider", "claude"), ("ai_job_sort", "ollama"), ("ai_job_notes", ""),
            ("class_name_0", "CS 101"), ("summary_enabled", "1"), ("ollama_enabled", "1"));
        Assert.Equal(HttpStatusCode.SeeOther, r.StatusCode);
        var ai = StudyStash.Core.Ai.AiSettings.Load(cfg.Home);
        Assert.Equal("claude", ai.For("notes").Provider);
        Assert.True(ai.Local("sort"));
        Assert.Contains("Writes study notes", await site.Text("/settings"));
    }

    static string FirstDifference(string want, string got)
    {
        int i = 0;
        while (i < want.Length && i < got.Length && want[i] == got[i]) i++;
        int from = Math.Max(0, i - 80);
        return $"python: …{want[from..Math.Min(want.Length, i + 80)]}\nc#:     …{got[from..Math.Min(got.Length, i + 80)]}";
    }

    // --- the library's own web pages ----------------------------------------------------------------------------

    [Fact]
    public async Task Login_browse_move_download()
    {
        using var dir = new TempDir();
        var cfg = MakeCfg(dir, "pw", "boss");
        using var store = new Store(cfg.DbPath, cfg.PoolDir);
        SeedCells(store);
        await using var c = await Site(cfg, store);
        var r = await c.Get("/note/n1");
        Assert.Equal(HttpStatusCode.SeeOther, r.StatusCode);
        Assert.StartsWith("/login?next=", r.Headers.Location!.OriginalString);
        var bad = await c.PostForm("/login", ("password", "nope"), ("next", "/"));
        Assert.Equal(HttpStatusCode.SeeOther, bad.StatusCode);
        Assert.Contains("bad=1", bad.Headers.Location!.OriginalString);
        r = await c.PostForm("/login", ("password", "pw"), ("next", "/note/n1"));
        Assert.Equal(("/note/n1", true), (r.Headers.Location!.OriginalString, c.Cookie("pool") is not null));

        string home = await c.Text("/");
        Assert.Contains("Recent lectures", home);
        Assert.Contains("Cells", home);
        Assert.Contains("Unsorted", home);
        Assert.True((await c.Get("/")).Headers.Contains("Content-Security-Policy"));
        Assert.Contains("Cells", await c.Text("/unsorted"));
        string page = await c.Text("/note/n1");
        foreach (string want in new[] { "membranes", "<select", "Transcript", "osmosis", "Rewrite summary" }) Assert.Contains(want, page);
        Assert.DoesNotContain("from Sam", page); // one person: everything, no "from" labels
        r = await c.PostForm("/note/n1/class", ("class_name", "Bio 110"));
        Assert.Equal(HttpStatusCode.SeeOther, r.StatusCode);
        Assert.Equal("Bio 110", store.Get("n1")!.ClassName);
        // moving keeps the transcript in the file (0.1 dropped it)
        var dl = await c.Get("/note/n1/download");
        string md = await dl.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, dl.StatusCode);
        Assert.Contains("membranes", md);
        Assert.Contains("osmosis", md);
        Assert.StartsWith("text/markdown", dl.Content.Headers.ContentType!.ToString());
        var z = await c.Get("/class/Bio%20110/zip");
        Assert.Equal("application/zip", z.Content.Headers.ContentType!.MediaType);
        using (var zip = new ZipArchive(await z.Content.ReadAsStreamAsync()))
            Assert.Equal(["Bio 110/2026-09-01 Cells.md"], zip.Entries.Select(e => e.FullName));
        var api = await c.Client.GetFromJsonAsync<JsonArray>("/api/notes?class_name=Bio%20110");
        Assert.Equal(("n1", "[\"cells\"]"), (api![0]!["id"].S(), api[0]!["topics"]!.ToJsonString()));
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostForm("/note/n1/class", ("class_name", "Nope"))).StatusCode);

        string hits = await c.Text("/search?q=osmosis");
        Assert.Contains("1 lecture mention", hits);
        Assert.Contains("<mark>osmosis</mark>", hits);
        Assert.Contains("No matches", await c.Text("/search?q=quantum"));

        Assert.Equal(HttpStatusCode.OK, (await c.Get("/settings")).StatusCode); // the one password opens Settings too
        var stranger = c.Stranger();
        Assert.Equal(HttpStatusCode.SeeOther, (await stranger.PostAsync("/note/n1/delete", null)).StatusCode);
        Assert.NotNull(store.Get("n1")); // not deleted without a login
    }

    [Fact]
    public async Task Settings_models_classes_and_invite()
    {
        using var dir = new TempDir();
        var cfg = MakeCfg(dir, "pw", "boss");
        using var store = new Store(cfg.DbPath, cfg.PoolDir);
        SeedCells(store);
        var pipeline = new Pipeline(cfg, store, log: Quiet);
        await using var c = await Site(cfg, store, pipeline);
        await c.PostForm("/login", ("password", "boss"), ("next", "/")); // the 0.2 admin password still opens it
        string s = await c.Text("/settings");
        Assert.Contains("qwen3:1.7b", s);
        Assert.Contains("Connect your laptop", s);
        Assert.Contains("<span class=\"value\">http://mini.tail.ts.net:8787</span>", s);
        Assert.Contains("<span class=\"value\">pw</span>", s);
        Assert.Contains("/Syft-Notes.dmg\">Mac</a>", s);
        Assert.Contains("/Syft-Notes-Setup.exe\">Windows</a>", s);
        Assert.Contains("Rewrite summary", await c.Text("/note/n1"));

        var r = await c.PostForm("/settings",
            ("summary_model", "qwen3:1.7b"), ("ollama_model", "qwen3:1.7b"), ("summary_enabled", "1"), ("ollama_enabled", "1"),
            ("min_confidence", "0.7"),
            ("class_name_0", "CS 101"), ("class_aliases_0", "cs101, intro"), ("class_desc_0", "Programming"),
            ("class_name_1", "Bio 110"), ("class_aliases_1", ""), ("class_desc_1", ""), ("class_remove_1", "1"),
            ("class_name_2", "Chem 1A"), ("class_aliases_2", "chem"), ("class_desc_2", ""),
            ("class_name_3", ""), ("class_aliases_3", ""), ("class_desc_3", ""));
        Assert.Equal((HttpStatusCode.SeeOther, "/settings?saved=1"), (r.StatusCode, r.Headers.Location!.OriginalString));
        var back = Configs.Load(cfg.Home);
        Assert.Equal(("qwen3:1.7b", 0.7), (back.SummaryModel, back.MinConfidence));
        Assert.Equal(["CS 101", "Chem 1A"], back.ClassNames());
        Assert.Equal(["cs101", "intro"], back.Classes[0].Aliases);
        Assert.Equal(["CS 101", "Chem 1A"], cfg.ClassNames()); // the running app sees it right away

        r = await c.PostForm("/settings/resummarize-all");
        Assert.Equal("/settings?queued=1", r.Headers.Location!.OriginalString);
        Assert.Equal("queued", store.Get("n1")!.Status);
        Assert.Contains("Cells", await c.Text("/unsorted")); // still readable while it waits
        Assert.Equal(HttpStatusCode.SeeOther, (await c.PostForm("/note/n1/delete")).StatusCode);
        Assert.Null(store.Get("n1"));
    }

    [Fact]
    public async Task A_rewrite_that_didnt_happen_leaves_the_notes_on_the_page_and_says_why()
    {
        using var dir = new TempDir();
        var cfg = MakeCfg(dir); // AI off: nothing can write new notes
        using var store = new Store(cfg.DbPath, cfg.PoolDir);
        store.Save(new Meeting("n1") { Title = "Cells", Date = "2026-09-01", Transcript = "today we talk about the cell membrane and osmosis" },
            new Classification("Bio 110", 0.9, "ollama", "Cells and membranes", ["cells"]), "## Summary\nWater follows salt across the membrane.", "Claude");
        var pipeline = new Pipeline(cfg, store, log: Quiet);
        await using var c = await Site(cfg, store, pipeline);

        Assert.Equal(HttpStatusCode.SeeOther, (await c.PostForm("/note/n1/resummarize")).StatusCode); // "Rewrite summary"
        Assert.Contains("A new summary is being written", await c.Text("/note/n1"));
        await pipeline.RunPendingAsync();

        string page = await c.Text("/note/n1");
        Assert.Contains("<h1>Cells and membranes</h1>", page);
        Assert.Contains("<dt>Summary</dt><dd>Notes by Claude, from the transcript</dd>", page);
        Assert.Contains("Water follows salt across the membrane.", page);
        Assert.Contains("The notes weren&#x27;t rewritten: AI is switched off in your library&#x27;s Settings. Your current notes are unchanged.<form", page);
        Assert.DoesNotContain("No study notes yet", page);
    }

    [Fact]
    public async Task Settings_offers_an_update_when_a_newer_release_is_out()
    {
        using var dir = new TempDir();
        var cfg = MakeCfg(dir, admin: "boss");
        using var store = new Store(cfg.DbPath, cfg.PoolDir);
        var rel = new Release("v9.9.9", [9, 9, 9], "https://x/v9.9.9.tar.gz", "https://x/releases/v9.9.9");
        Release? applied = null;
        await using var c = await Site(cfg, store, options: Options(_ => Task.FromResult<Release?>(rel), apply: (r, _) =>
        {
            applied = r;
            return Task.CompletedTask;
        }));
        string settings = await c.Text("/settings");
        Assert.Contains("v9.9.9 is out", settings);
        Assert.Contains("Update now", settings);
        Assert.Contains("Updating", await (await c.PostForm("/settings/update")).Content.ReadAsStringAsync());
        for (int i = 0; i < 50 && applied is null; i++) await Task.Delay(20);
        Assert.Same(rel, applied);

        // An engine that can't install updates yet links to the release instead of offering a button.
        await using var plain = await Site(cfg, store, options: Options(_ => Task.FromResult<Release?>(rel)));
        string linked = await plain.Text("/settings");
        Assert.Contains("v9.9.9 is out", linked);
        Assert.DoesNotContain("Update now", linked);
    }

    [Fact]
    public async Task Note_html_is_neutralized()
    {
        using var dir = new TempDir();
        var cfg = MakeCfg(dir);
        using var store = new Store(cfg.DbPath, cfg.PoolDir);
        store.Save(new Meeting("x") { Title = "<b>Lec</b>", NotesMarkdown = "hi <script>alert(1)</script> [a](javascript:alert(2)) $x_1^2$ [b](JaVaScRiPt:x) <javascript:y>" },
            new Classification("CS 101", 1, "human"));
        await using var c = await Site(cfg, store);
        string page = await c.Text("/note/x");
        Assert.DoesNotContain("<script>alert(1)", page);
        Assert.Contains("&lt;script&gt;", page);
        Assert.DoesNotContain("javascript:alert", page);
        Assert.DoesNotContain("href=\"JaVaScRiPt", page, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("&lt;b&gt;Lec", page);
        Assert.Contains("$x_1^2$", page); // math survives Markdown for KaTeX
    }

    [Fact]
    public async Task No_password_means_open()
    {
        using var dir = new TempDir();
        var cfg = MakeCfg(dir);
        using var store = new Store(cfg.DbPath, cfg.PoolDir);
        await using var c = await Site(cfg, store);
        Assert.Equal(HttpStatusCode.OK, (await c.Get("/")).StatusCode);
        Assert.Contains("No lectures yet", await c.Text("/"));
    }

    [Fact]
    public void Login_never_redirects_off_site()
    {
        Assert.Equal("/note/n1?x=1", LibraryWeb.SafeNext("/note/n1?x=1"));
        foreach (string bad in new[] { "https://evil.com", "//evil.com", "/\\evil.com", "/\t/evil.com", "/\n/evil.com", "evil.com", "" })
            Assert.Equal("/", LibraryWeb.SafeNext(bad));
        foreach (var c in (JsonArray)Golden.Library("safe_next")!)
            Assert.Equal(c![1].S(), LibraryWeb.SafeNext(c[0].S()));
        // A class's page is a place to come back to whatever its name holds, written the way an address carries it.
        Assert.Equal("/class/MATH%20221", LibraryWeb.SafeNext("/class/MATH 221"));
        Assert.Equal("/class/MATH%20221?said=1#top", LibraryWeb.SafeNext("/class/MATH%20221?said=1#top"));
        Assert.Equal("/class/Espa%C3%B1ol%20(honors)", LibraryWeb.SafeNext("/class/Español (honors)"));
        foreach (string bad in new[] { "/\u007f/evil.com", "/\u0085/evil.com", "/ok\r\nSet-Cookie: x=1", "http://evil.com", "http:evil.com", "/ \\evil.com", " //evil.com" })
            Assert.Equal("/", LibraryWeb.SafeNext(bad));
    }

    [Fact]
    public async Task Signing_in_goes_back_to_a_class_whose_name_has_a_space()
    {
        using var dir = new TempDir();
        var cfg = MakeCfg(dir, "pw");
        cfg.Classes.Add(new ClassDef("C# Lab"));
        using var store = new Store(cfg.DbPath, cfg.PoolDir);
        await using var c = await Site(cfg, store);
        foreach (var (asked, back) in new[] { ("/class/Bio%20110", "/class/Bio%20110"), ("/class/C%23%20Lab?said=hi%20there", "/class/C%23%20Lab?said=hi%20there") })
        {
            c.ClearCookies();
            var away = await c.Get(asked);
            Assert.Equal(HttpStatusCode.SeeOther, away.StatusCode);
            string next = System.Web.HttpUtility.ParseQueryString(new Uri(new Uri("http://localhost"), away.Headers.Location!.OriginalString).Query)["next"]!;
            var r = await c.PostForm("/login", ("password", "pw"), ("next", next));
            Assert.Equal(back, r.Headers.Location!.OriginalString);
            Assert.Equal(HttpStatusCode.OK, (await c.Get(back)).StatusCode);
        }
        // A sign-in page kept from before, with the space as it was: the class still, not the home page.
        Assert.Equal("/class/Bio%20110", (await c.PostForm("/login", ("password", "pw"), ("next", "/class/Bio 110"))).Headers.Location!.OriginalString);
        Assert.Equal("/", (await c.PostForm("/login", ("password", "pw"), ("next", "//example.com"))).Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task Log_out_ends_the_sign_in_wherever_its_cookie_is()
    {
        using var dir = new TempDir();
        var cfg = MakeCfg(dir, "pw");
        Directory.CreateDirectory(cfg.Home);
        File.WriteAllText(Path.Combine(cfg.Home, "web_secret"), "00112233445566778899aabbccddeeff00112233445566778899aabbccddeeff");
        using var store = new Store(cfg.DbPath, cfg.PoolDir);
        await using var c = await Site(cfg, store);
        // The cookie every sign-in got before each had its own (one value for everyone, for good) opens nothing now:
        // hmac.new(bytes.fromhex(that), b"admin", hashlib.sha256).hexdigest()
        c.SetCookie("pool", "274fba6ac9d9127073375a2d3c345f69729c751b36420b304f67219b21269d82");
        Assert.Equal(HttpStatusCode.SeeOther, (await c.Get("/settings")).StatusCode);

        await c.PostForm("/login", ("password", "pw"), ("next", "/"));
        string phone = c.Cookie("pool")!;
        await c.PostForm("/login", ("password", "pw"), ("next", "/"));
        string laptop = c.Cookie("pool")!;
        Assert.NotEqual(phone, laptop);

        // Log out on the laptop: a copy of its cookie put back opens nothing, and the phone is still signed in.
        Assert.Equal(HttpStatusCode.SeeOther, (await c.Get("/logout")).StatusCode);
        Assert.Null(c.Cookie("pool"));
        c.SetCookie("pool", laptop);
        Assert.Equal(HttpStatusCode.SeeOther, (await c.Get("/settings")).StatusCode);
        c.SetCookie("pool", phone);
        Assert.Equal(HttpStatusCode.OK, (await c.Get("/settings")).StatusCode);

        // The library starts again (it updates itself): the phone is still signed in, the laptop still out.
        await using var again = await Site(cfg, store);
        again.SetCookie("pool", laptop);
        Assert.Equal(HttpStatusCode.SeeOther, (await again.Get("/settings")).StatusCode);
        again.SetCookie("pool", phone);
        Assert.Equal(HttpStatusCode.OK, (await again.Get("/settings")).StatusCode);

        // The file that keeps sign-ins holds no cookie and is for this account alone; a sign-in runs out after 180 days.
        string file = Path.Combine(cfg.Home, "sessions.json");
        Assert.DoesNotContain(phone, File.ReadAllText(file));
        if (!OperatingSystem.IsWindows()) Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(file));
        var now = DateTimeOffset.UtcNow;
        var kept = new Sessions(cfg.Home, () => now);
        string token = kept.Start("pw");
        now += Sessions.Life - TimeSpan.FromMinutes(1);
        Assert.True(kept.Check(token, "pw"));
        now += TimeSpan.FromMinutes(2);
        Assert.False(kept.Check(token, "pw"));
    }

    [Fact]
    public async Task A_new_password_signs_out_every_browser()
    {
        using var dir = new TempDir();
        var cfg = MakeCfg(dir, "first-one");
        using var store = new Store(cfg.DbPath, cfg.PoolDir);
        await using var c = await Site(cfg, store);
        await c.PostForm("/login", ("password", "first-one"), ("next", "/"));
        Assert.Equal(HttpStatusCode.OK, (await c.Get("/settings")).StatusCode);

        // The app's Settings changes the password: the browser signed in with the old one is out, and stays out when
        // the old password comes back.
        Assert.Equal(HttpStatusCode.OK, (await c.Client.SendAsync(Req(HttpMethod.Post, "/api/v2/settings/password", "first-one", new { password = "a-new-one" }))).StatusCode);
        Assert.Equal(HttpStatusCode.SeeOther, (await c.Get("/settings")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await c.Client.SendAsync(Req(HttpMethod.Post, "/api/v2/settings/password", "a-new-one", new { password = "first-one" }))).StatusCode);
        Assert.Equal(HttpStatusCode.SeeOther, (await c.Get("/settings")).StatusCode);
        await c.PostForm("/login", ("password", "first-one"), ("next", "/"));
        Assert.Equal(HttpStatusCode.OK, (await c.Get("/settings")).StatusCode);

        // Changed while the library was stopped (the app starts it again with the new one, or config.toml by hand).
        string signedIn = c.Cookie("pool")!;
        cfg.PoolPassword = "changed-while-stopped";
        await using var again = await Site(cfg, store);
        cfg.PoolPassword = "first-one";
        again.SetCookie("pool", signedIn);
        Assert.Equal(HttpStatusCode.SeeOther, (await again.Get("/settings")).StatusCode);
    }

    /// <summary>A request from an address: straight to the library (<paramref name="address"/> is where the
    /// connection comes from), or, from 127.0.0.1, passed on by Tailscale Serve for <paramref name="forwardedFor"/>.
    /// With <paramref name="password"/> it's the sign-in form; with <paramref name="key"/>, the laptop's key.</summary>
    static async Task<(int Status, string Location, string Body, string RetryAfter)> From(TestSite site, string address, string path,
        string? password = null, string? key = null, string? forwardedFor = null)
    {
        var ctx = await site.App.GetTestServer().SendAsync(c =>
        {
            c.Connection.RemoteIpAddress = IPAddress.Parse(address);
            c.Request.Method = password is null ? "GET" : "POST";
            c.Request.Path = path;
            if (password is not null)
            {
                c.Request.ContentType = "application/x-www-form-urlencoded";
                c.Request.Body = new FormUrlEncodedContent([KeyValuePair.Create("password", password), KeyValuePair.Create("next", "/")]).ReadAsStream();
            }
            if (key is not null) c.Request.Headers.Authorization = "Bearer " + key;
            if (forwardedFor is not null) c.Request.Headers["X-Forwarded-For"] = forwardedFor;
        });
        var r = ctx.Response;
        return (r.StatusCode, r.Headers.Location.ToString(), await new StreamReader(r.Body).ReadToEndAsync(), r.Headers.RetryAfter.ToString());
    }

    [Fact]
    public async Task Wrong_passwords_are_counted_by_address_and_shut_only_that_address_out_for_a_while()
    {
        using var dir = new TempDir();
        var cfg = MakeCfg(dir, "pw");
        using var store = new Store(cfg.DbPath, cfg.PoolDir);
        var now = DateTimeOffset.Parse("2026-10-13T09:00:00Z", System.Globalization.CultureInfo.InvariantCulture);
        var held = new List<double>();
        var tries = new PasswordTries(() => now, (hold, _) => { held.Add(hold.TotalSeconds); return Task.CompletedTask; });
        await using var c = await Site(cfg, store, options: Options(tries: tries));
        const string Guesser = "203.0.113.5", Student = "198.51.100.9", Here = "127.0.0.1";
        const string WrongKey = "{\"detail\":\"wrong password\"}", Wait = "Too many wrong passwords. Try again in 10 minutes.";
        // The sign-in page: where it sends the browser. A laptop's key: the status and what it's told.
        async Task<string> SignIn(string address, string password, string? forwardedFor = null) => (await From(c, address, "/login", password: password, forwardedFor: forwardedFor)).Location;
        async Task<(int, string)> Key(string address, string key, string? forwardedFor = null) =>
            (await From(c, address, "/api/health", key: key, forwardedFor: forwardedFor)) is var r && r.Status == 200 ? (200, "") : (r.Status, r.Body);

        // Ten wrong passwords on the sign-in page from one address, each claiming to be someone else: the claim gets
        // it no fresh count, and after the first three each answer is held back a second longer, up to five.
        for (int i = 0; i < PasswordTries.Most; i++) Assert.Equal("/login?bad=1&next=%2F", await SignIn(Guesser, "guess" + i, forwardedFor: "10.0.0." + i));
        Assert.Equal([1, 2, 3, 4, 5, 5, 5], held);
        // Now nothing from there is looked at, the right password included, at the page or with a laptop's key: the
        // page says how long to wait, and the key's answer is still the 401 every laptop reads as a wrong password.
        Assert.Equal("/login?wait=1&next=%2F", await SignIn(Guesser, "pw"));
        Assert.Contains("<p class=\"bad\">Too many wrong passwords.<br>Try again in 10 minutes.</p>", (await From(c, Guesser, "/login")).Body);
        Assert.Equal((401, $"{{\"detail\":\"{Wait}\"}}"), await Key(Guesser, "pw"));
        Assert.Equal("600", (await From(c, Guesser, "/api/health", key: "pw")).RetryAfter);
        for (int i = 0; i < PasswordTries.Most; i++) await c.Client.SendAsync(Req(HttpMethod.Get, "/api/health", "guess" + i));
        Assert.Equal("wrong password", (await Assert.ThrowsAsync<InvalidOperationException>(() => LibraryApi.CheckServerAsync("http://localhost", "pw", c.Client))).Message);

        // The student, anywhere else, is let in as ever; so is a laptop there, however often it asks.
        Assert.Equal("/", await SignIn(Student, "pw"));
        Assert.DoesNotContain("Too many", (await From(c, Student, "/login")).Body);
        for (int i = 0; i < 30; i++) Assert.Equal((200, ""), await Key(Student, "pw"));

        // This computer is never counted: its pages need no password, and its own apps' wrong keys are only wrong.
        for (int i = 0; i < 30; i++) Assert.Equal((401, WrongKey), await Key(Here, "guess" + i));
        Assert.Equal((200, ""), await Key(Here, "pw"));
        Assert.Equal(200, (await From(c, Here, "/settings")).Status);

        // Through Tailscale Serve everyone arrives from this computer, named by the header Serve writes. A laptop
        // that still has the old password asks with it on a timer: one wrong password, however often, is one try,
        // so it's told "wrong password" as it always was and the new one works the moment it's typed.
        for (int i = 0; i < 30; i++) Assert.Equal((401, WrongKey), await Key(Here, "last-terms", forwardedFor: "100.64.0.7"));
        Assert.Equal((200, ""), await Key(Here, "pw", forwardedFor: "100.64.0.7"));
        // Someone guessing through Serve is shut out by the address Serve names, whatever they put before it.
        for (int i = 0; i < PasswordTries.Most; i++) await Key(Here, "guess" + i, forwardedFor: "100.64.0.7, 100.64.0.8");
        Assert.Equal(401, (await Key(Here, "pw", forwardedFor: "100.64.0.8")).Item1);
        Assert.Equal((200, ""), await Key(Here, "pw", forwardedFor: "100.64.0.7"));

        // An IPv6 sender has a whole network of addresses to send from: they're counted as one.
        for (int i = 0; i < PasswordTries.Most; i++) await Key("2001:db8:85a3:7::" + (i + 1), "guess" + i);
        Assert.Equal(401, (await Key("2001:db8:85a3:7:ffff::9", "pw")).Item1);
        Assert.Equal((200, ""), await Key("2001:db8:85a3:8::1", "pw"));

        // After the wait the right password works there again.
        now += PasswordTries.Window - TimeSpan.FromSeconds(1);
        Assert.Contains("Try again in 1 minute.", (await From(c, Guesser, "/login")).Body);
        now += TimeSpan.FromSeconds(1);
        Assert.Equal("/", await SignIn(Guesser, "pw"));
    }

    // --- the API the laptop talks to -----------------------------------------------------------------------------

    static HttpRequestMessage Req(HttpMethod method, string path, string? key = null, object? json = null)
    {
        var r = new HttpRequestMessage(method, path);
        if (key is not null) r.Headers.TryAddWithoutValidation("Authorization", "Bearer " + key);
        if (json is not null) r.Content = JsonContent.Create(json);
        return r;
    }

    [Fact]
    public async Task Health_needs_the_password_and_reports_the_library()
    {
        using var dir = new TempDir();
        var cfg = MakeCfg(dir, "pw");
        cfg.PoolName = "Fall pool";
        using var store = new Store(cfg.DbPath, cfg.PoolDir);
        await using var c = await Site(cfg, store);
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.Get("/api/health")).StatusCode);
        var nope = await c.Client.SendAsync(Req(HttpMethod.Get, "/api/health", "nope"));
        Assert.Equal(HttpStatusCode.Unauthorized, nope.StatusCode);
        Assert.Equal("{\"detail\":\"wrong password\"}", await nope.Content.ReadAsStringAsync());
        var r = await c.Client.SendAsync(Req(HttpMethod.Get, "/api/health", "pw"));
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse($$"""{"ok": true, "pool_name": "Fall pool", "classes": ["CS 101", "Bio 110"], "notes": 0, "version": "{{Engine.Version}}", "background": false}"""),
            JsonNode.Parse(await r.Content.ReadAsStringAsync())));
        Assert.Equal(Engine.Version, r.Headers.GetValues("X-Study-Stash").Single());
    }

    [Fact]
    public async Task Ingest_queues_then_the_pipeline_files()
    {
        using var dir = new TempDir();
        var cfg = MakeCfg(dir, "pw");
        using var store = new Store(cfg.DbPath, cfg.PoolDir);
        var pipeline = new Pipeline(cfg, store, log: Quiet);
        await using var c = await Site(cfg, store, pipeline);
        var payload = new
        {
            id = "not_abc", title = "CS101 lecture 2", date = "2026-09-14T10:00:00Z", owner = "Sam", attendees = new[] { "Sam" },
            folder = "", notes_markdown = "# Loops\nfor and while", private_notes = "", transcript = "", raw = new { id = "not_abc" },
        };
        var r = await c.Client.SendAsync(Req(HttpMethod.Post, "/api/ingest", "pw", payload));
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse("""{"id": "not_abc", "status": "queued", "class_name": "CS 101", "has_transcript": false}"""),
            JsonNode.Parse(await r.Content.ReadAsStringAsync())));
        Assert.Equal("queued", store.Get("not_abc")!.Status);
        Assert.Empty(store.ListNotes());

        Assert.Equal(1, await pipeline.RunPendingAsync());
        var row = store.Get("not_abc")!;
        Assert.Equal(("done", "CS 101", "Sam"), (row.Status, row.ClassName, row.Owner));
        Assert.Single(store.ListNotes());
        var status = await c.Client.SendAsync(Req(HttpMethod.Get, "/api/notes/not_abc/status", "pw"));
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse("""{"id": "not_abc", "status": "done", "class_name": "CS 101", "summary_model": null, "has_transcript": false, "path": "/note/not_abc", "lecture_title": "CS101 lecture 2"}"""),
            JsonNode.Parse(await status.Content.ReadAsStringAsync())));
        Assert.Equal(HttpStatusCode.NotFound, (await c.Client.SendAsync(Req(HttpMethod.Get, "/api/notes/nope/status", "pw"))).StatusCode);

        // sharing again queues the same lecture again instead of making a second one
        await c.Client.SendAsync(Req(HttpMethod.Post, "/api/ingest", "pw", payload));
        await pipeline.RunPendingAsync();
        Assert.Single(store.ListNotes());
        Assert.Single(Directory.GetFiles(Path.Combine(cfg.PoolDir, "CS 101")));

        // one no rule matches goes to Unsorted (AI off)
        var r3 = await c.Client.SendAsync(Req(HttpMethod.Post, "/api/ingest", "pw", new { id = "x2", title = "Lunch" }));
        Assert.Null(JsonNode.Parse(await r3.Content.ReadAsStringAsync())!["class_name"]);
        await pipeline.RunPendingAsync();
        Assert.Equal(Configs.Unsorted, store.Get("x2")!.ClassName);
    }

    [Fact]
    public async Task Ingest_refuses_a_wrong_password_and_a_lecture_without_an_id()
    {
        using var dir = new TempDir();
        var cfg = MakeCfg(dir, "pw");
        using var store = new Store(cfg.DbPath, cfg.PoolDir);
        await using var c = await Site(cfg, store);
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.Client.SendAsync(Req(HttpMethod.Post, "/api/ingest", json: new { id = "a" }))).StatusCode);
        var noId = await c.Client.SendAsync(Req(HttpMethod.Post, "/api/ingest", "pw", new { title = "no id" }));
        Assert.Equal((HttpStatusCode.BadRequest, "{\"detail\":\"payload needs a non-empty 'id'\"}"), (noId.StatusCode, await noId.Content.ReadAsStringAsync()));
        var notObject = new HttpRequestMessage(HttpMethod.Post, "/api/ingest") { Content = new StringContent("[1]", System.Text.Encoding.UTF8, "application/json") };
        notObject.Headers.TryAddWithoutValidation("Authorization", "Bearer pw");
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await c.Client.SendAsync(notObject)).StatusCode);
    }

    [Fact]
    public async Task An_open_library_needs_no_password()
    {
        using var dir = new TempDir();
        var cfg = MakeCfg(dir);
        using var store = new Store(cfg.DbPath, cfg.PoolDir);
        await using var c = await Site(cfg, store);
        Assert.Equal(HttpStatusCode.OK, (await c.Get("/api/health")).StatusCode);
    }

    [Fact]
    public async Task Icons_and_unknown_addresses()
    {
        using var dir = new TempDir();
        var cfg = MakeCfg(dir, "pw");
        using var store = new Store(cfg.DbPath, cfg.PoolDir);
        await using var c = await Site(cfg, store);
        var icon = await c.Get("/favicon.ico"); // no sign-in needed
        Assert.Equal(("image/x-icon", "public, max-age=86400"), (icon.Content.Headers.ContentType!.MediaType, icon.Headers.CacheControl!.ToString()));
        Assert.Equal(File.ReadAllBytes(Path.Combine(RepoAssets(), "study-stash.ico")), await icon.Content.ReadAsByteArrayAsync());
        Assert.Equal("image/png", (await c.Get("/apple-touch-icon.png")).Content.Headers.ContentType!.MediaType);
        var missing = await c.Get("/nowhere");
        Assert.Equal((HttpStatusCode.NotFound, "{\"detail\":\"Not Found\"}"), (missing.StatusCode, await missing.Content.ReadAsStringAsync()));
    }

    /// <summary>The repo's assets/ folder. It looks for the icon file, not the folder: on a case-insensitive disk
    /// StudyStash.App/Assets would pass for it.</summary>
    static string RepoAssets()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, "assets", "study-stash.ico"))) return Path.Combine(d.FullName, "assets");
        throw new FileNotFoundException("assets/study-stash.ico");
    }

    [Fact]
    public async Task A_class_with_a_slash_in_its_name_still_opens()
    {
        using var dir = new TempDir();
        var cfg = MakeCfg(dir);
        cfg.Classes.Add(new ClassDef("Lab / A"));
        using var store = new Store(cfg.DbPath, cfg.PoolDir);
        store.Save(new Meeting("l1") { Title = "Titration", Date = "2026-09-01" }, new Classification("Lab / A", 1, "human"));
        await using var c = await Site(cfg, store);
        var r = await c.Get(Ui.ClassUrl("Lab / A"));
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Contains("Titration", await r.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task The_home_page_shows_coming_up_with_its_class_once_a_laptop_has_sent_some()
    {
        using var dir = new TempDir();
        var cfg = MakeCfg(dir, password: "pw");
        using var store = new Store(cfg.DbPath, cfg.PoolDir);
        var now = DateTimeOffset.Now;
        LibraryCalendar.Save(cfg.Home, new System.Text.Json.Nodes.JsonObject
        {
            ["events"] = new System.Text.Json.Nodes.JsonArray(new System.Text.Json.Nodes.JsonObject
            {
                ["id"] = "1", ["title"] = "cs101 lecture", ["start"] = now.AddMinutes(10).ToString("o"), ["end"] = now.AddHours(1).ToString("o"),
            }),
        }, now);
        await using var site = await Site(cfg, store);
        await site.PostForm("/login", ("password", "pw"), ("next", "/"));

        string home = await site.Text("/");

        Assert.Contains("Coming up", home);
        Assert.Contains("cs101 lecture", home);
        Assert.Contains("CS 101", home); // matched from the event's title via the class's other name
    }

    [Fact]
    public async Task The_home_page_says_nothing_about_calendars_with_none_sent()
    {
        using var dir = new TempDir();
        var cfg = MakeCfg(dir, password: "pw");
        using var store = new Store(cfg.DbPath, cfg.PoolDir);
        await using var site = await Site(cfg, store);
        await site.PostForm("/login", ("password", "pw"), ("next", "/"));

        Assert.DoesNotContain("Coming up", await site.Text("/"));
    }
}
