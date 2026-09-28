using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using StudyStash.Core.Canvas;
using StudyStash.Library;

namespace StudyStash.Core.Tests;

/// <summary>Choosing which Canvas courses to bring in: this term's courses start ticked and the rest (last term,
/// sandboxes, chapel, no term) don't; only chosen courses become classes and sync; a course dropped later stops
/// syncing, its class and files kept or removed as asked, and a class with lectures always stays.</summary>
public class CourseChoicesTests
{
    static readonly DateTimeOffset Now = new(2026, 9, 27, 17, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("Intro to Programming", "CS 101", "Fall 2026", "2026-08-20T00:00:00Z", "2026-12-15T00:00:00Z", true, "")]
    [InlineData("Intro to Programming", "CS 101", "Spring 2026", "2026-01-10T00:00:00Z", "2026-05-10T00:00:00Z", false, CourseChoices.PastTerm)]
    [InlineData("Organic Chemistry", "CHEM 210", "Spring 2027", "2027-01-10T00:00:00Z", "2027-05-10T00:00:00Z", false, CourseChoices.LaterTerm)]
    [InlineData("Cell Biology", "BIO 110", "Fall 2026", "", "", true, "")]
    [InlineData("Cell Biology", "BIO 110", "2026 Fall", "", "", true, "")]
    [InlineData("Cell Biology", "BIO 110", "Spring 2026", "", "", false, CourseChoices.PastTerm)]
    [InlineData("Cell Biology", "BIO 110", "Summer 2027", "", "", false, CourseChoices.LaterTerm)]
    [InlineData("Cell Biology", "BIO 110", "Term A", "", "", true, "")]
    [InlineData("Sandbox for Dr. Okafor", "SBX-OKAFOR", "Fall 2026", "", "", false, CourseChoices.NotAClass)]
    [InlineData("Chapel and Convocation", "GEN0100.A", "Fall 2026", "", "", false, CourseChoices.NotAClass)]
    [InlineData("New Student Orientation", "ORIENT", "Fall 2026", "", "", false, CourseChoices.NotAClass)]
    [InlineData("Writing Center Resources", "WRITE", "Default Term", "", "", false, CourseChoices.NoTerm)]
    [InlineData("Statics", "MECH 2010", "", "", "", false, CourseChoices.NoTerm)]
    public void This_term_s_courses_start_ticked_and_the_rest_say_why_not(string name, string code, string term, string start, string end, bool ticked, string why)
    {
        var (t, w) = CourseChoices.Suggest(new CourseInfo(code, name, term, start, end), Now);
        Assert.Equal(ticked, t);
        Assert.Equal(why, w);
    }

    [Fact]
    public void A_course_s_own_dates_count_when_its_term_has_none()
    {
        Assert.Equal((false, CourseChoices.PastTerm), CourseChoices.Suggest(new CourseInfo("CS 101", "Intro", "Default Term", CourseEnd: "2025-12-01T00:00:00Z"), Now));
        Assert.Equal((true, ""), CourseChoices.Suggest(new CourseInfo("CS 101", "Intro", "Default Term", CourseStart: "2026-08-20T00:00:00Z"), Now));
    }

    static (Config Cfg, Store Store) Library(TempDir dir)
    {
        var cfg = new Config(dir["home"], dir["pool"]) { PoolPassword = Password, OllamaEnabled = false, Classes = [new ClassDef("Lab Notes")] };
        Directory.CreateDirectory(cfg.Home);
        Configs.Save(cfg);
        CanvasSettings.Update(cfg.Home, s =>
        {
            s.Url = "https://school.instructure.com";
            s.Available = new() { ["11"] = "Bridge Design(MECH4120.G)", ["12"] = "Cell Biology", ["13"] = "Sandbox for Dr. Okafor", ["14"] = "Heat Transfer(MECH2710.A)" };
            s.CourseInfo = new()
            {
                ["11"] = new CourseInfo("MECH4120.G", "Bridge Design(MECH4120.G)", "Fall 2026"),
                ["12"] = new CourseInfo("BIO 110", "Cell Biology", "Fall 2026"),
                ["13"] = new CourseInfo("SBX", "Sandbox for Dr. Okafor", "Default Term"),
                ["14"] = new CourseInfo("MECH2710.A", "Heat Transfer(MECH2710.A)", "Spring 2026"),
            };
        });
        return (cfg, new Store(cfg.DbPath, cfg.PoolDir));
    }

    [Fact]
    public void Only_the_chosen_courses_become_classes_and_are_linked()
    {
        using var dir = new TempDir();
        var (cfg, store) = Library(dir);
        using var _ = store;
        var outcome = CourseChoices.Apply(cfg, store, ["11", "12"], keep: true);

        Assert.Equal(["Bridge Design", "Cell Biology"], outcome.Added.Order());
        Assert.Equal(["Bridge Design", "Cell Biology", "Lab Notes"], Configs.Load(cfg.Home).ClassNames().Order());
        var s = CanvasSettings.Load(cfg.Home);
        Assert.Equal(11, s.Courses["Bridge Design"]);
        Assert.Equal(12, s.Courses["Cell Biology"]);
        Assert.Equal(["11", "12"], s.Chosen!.Order());
        Assert.True(s.SyncNow);
        Assert.Equal(["Bridge Design", "Cell Biology"], s.Synced.Keys.Order());
    }

    [Fact]
    public void A_course_dropped_later_stops_syncing_and_its_class_stays_or_goes_as_asked()
    {
        using var dir = new TempDir();
        var (cfg, store) = Library(dir);
        using var _ = store;
        CourseChoices.Apply(cfg, store, ["11", "12"], keep: true);
        // Canvas left files and rows for both; Cell Biology has a lecture.
        foreach (string cls in new[] { "Bridge Design", "Cell Biology" })
        {
            Directory.CreateDirectory(Path.Combine(store.ClassFolder(cls), "Canvas"));
            File.WriteAllText(Path.Combine(store.ClassFolder(cls), "Canvas", "syllabus.md"), "# syllabus");
            new CourseIndex { Class = cls, CourseId = cls == "Bridge Design" ? 11 : 12, Name = cls }.Save(cfg.Home);
        }
        Assignments.Save(cfg.Home, [new Assignment("Bridge Design", 1, "Truss report", "", 10, "open", null, "", ""),
            new Assignment("Cell Biology", 2, "Lab 1", "", 10, "open", null, "", "")]);
        store.Save(new Meeting("m1") { Title = "Membranes", Date = "2026-09-02", Transcript = "Membranes." }, new Classification("Cell Biology", 0.95, "folder"));

        // Dropping Bridge Design and keeping its class: it only stops syncing.
        var kept = CourseChoices.Apply(cfg, store, ["12"], keep: true);
        Assert.Equal(["Bridge Design"], kept.Stopped);
        Assert.Empty(kept.Removed);
        var s = CanvasSettings.Load(cfg.Home);
        Assert.False(s.Courses.ContainsKey("Bridge Design"));
        Assert.Contains("Bridge Design", Configs.Load(cfg.Home).ClassNames());
        Assert.True(File.Exists(Path.Combine(store.ClassFolder("Bridge Design"), "Canvas", "syllabus.md")));

        // Then everything, removing: Cell Biology's Canvas files and rows go, its class and lecture stay.
        cfg = Configs.Load(cfg.Home);
        var removed = CourseChoices.Apply(cfg, store, [], keep: false);
        Assert.Equal(["Cell Biology"], removed.Stopped);
        Assert.Equal(["Cell Biology"], removed.KeptForLectures);
        Assert.Empty(removed.Removed);
        Assert.Contains("Cell Biology", Configs.Load(cfg.Home).ClassNames());
        Assert.False(Directory.Exists(Path.Combine(store.ClassFolder("Cell Biology"), "Canvas")));
        Assert.Null(CourseIndex.Load(cfg.Home, "Cell Biology"));
        Assert.DoesNotContain(Assignments.Load(cfg.Home), a => a.ClassName == "Cell Biology");
        Assert.Single(store.ListNotes("Cell Biology"));
        Assert.Empty(CanvasSettings.Load(cfg.Home).Chosen!);

        // A class with no lectures goes with its files when it's removed.
        cfg = Configs.Load(cfg.Home);
        CourseChoices.Apply(cfg, store, ["11"], keep: true);
        cfg = Configs.Load(cfg.Home);
        var gone = CourseChoices.Apply(cfg, store, [], keep: false);
        Assert.Equal(["Bridge Design"], gone.Removed);
        Assert.DoesNotContain("Bridge Design", Configs.Load(cfg.Home).ClassNames());
        Assert.False(Directory.Exists(Path.Combine(store.ClassFolder("Bridge Design"), "Canvas")));
    }

    [Fact]
    public void A_chosen_course_named_like_a_class_without_a_course_is_linked_to_that_class()
    {
        using var dir = new TempDir();
        var (cfg, store) = Library(dir);
        using var _ = store;
        cfg.Classes.Add(new ClassDef("Cell Biology"));
        Configs.Save(cfg);
        var outcome = CourseChoices.Apply(cfg, store, ["12"], keep: true);
        Assert.Equal(["Cell Biology"], outcome.Added);
        Assert.Equal(["Cell Biology", "Lab Notes"], Configs.Load(cfg.Home).ClassNames().Order());
        Assert.Equal(12, CanvasSettings.Load(cfg.Home).Courses["Cell Biology"]);
    }

    [Fact]
    public void A_library_from_before_the_choice_syncs_every_linked_course_and_one_with_a_choice_only_the_chosen()
    {
        var s = new CanvasSettings { Url = "https://school.instructure.com", Courses = new() { ["CS 101"] = 4201, ["BIO 110"] = 4202 } };
        Assert.Equal(["BIO 110", "CS 101"], s.Synced.Keys.Order());
        Assert.True(s.Due(Now));
        s.Chosen = ["4201"];
        Assert.Equal(["CS 101"], s.Synced.Keys);
        s.Chosen = [];
        Assert.Empty(s.Synced);
        Assert.False(s.Due(Now));
    }

    [Fact]
    public void A_sync_reads_only_the_chosen_courses()
    {
        using var dir = new TempDir();
        var now = FakeCanvas.DesignNow;
        var sync = FakeCanvas.Library(dir, () => now, ("CS 101", 4201L), ("BIO 110", 4202L));
        CanvasSettings.Update(dir.Path, s => s.Chosen = ["4201"]);
        Assert.True(FakeCanvas.Cs101().Run(sync));
        Assert.NotNull(CourseIndex.Load(dir.Path, "CS 101"));
        Assert.Null(CourseIndex.Load(dir.Path, "BIO 110"));
        Assert.DoesNotContain(Assignments.Load(dir.Path), a => a.ClassName == "BIO 110");
    }

    const string Password = "maple otter";

    [Fact]
    public async Task The_library_says_which_courses_are_chosen_and_suggested_and_takes_a_new_choice()
    {
        using var dir = new TempDir();
        var (cfg, store) = Library(dir);
        using var _ = store;
        var sync = new CanvasSync(cfg.Home, c => store.ClassDir(c), _ => { }) { Clock = () => Now, Zone = FakeCanvas.Zone };
        var options = new LibraryWebOptions
        {
            Canvas = sync, ListModels = _ => Task.FromResult<List<(string, double)>?>(null), Tailscale = () => new TailscaleInfo(),
            Latest = _ => Task.FromResult<Release?>(null), RamGb = () => 16, HostName = () => "library-pc", Nonce = () => "NONCE",
        };
        await using var site = await TestSite.StartAsync(b => LibraryWeb.Build(b, cfg, store, new Pipeline(cfg, store, log: _ => { }), options));

        async Task<JsonObject> Send(HttpMethod method, string path, string? body = null)
        {
            var ask = new HttpRequestMessage(method, path) { Headers = { Authorization = new AuthenticationHeaderValue("Bearer", Password) } };
            if (body is not null) ask.Content = new StringContent(body, Encoding.UTF8, "application/json");
            var r = await site.Client.SendAsync(ask);
            string text = await r.Content.ReadAsStringAsync();
            Assert.True(r.IsSuccessStatusCode, $"{path}: {(int)r.StatusCode} {text}");
            return JsonNode.Parse(text)!.AsObject();
        }

        var before = await Send(HttpMethod.Get, "/api/v2/canvas");
        Assert.Null(before["chosen"]);
        var info = before["course_info"]!.AsObject();
        Assert.True(info["11"]!["suggested"]!.GetValue<bool>());
        Assert.Equal("Bridge Design", info["11"]!["title"]!.GetValue<string>());
        Assert.Equal("MECH 4120", info["11"]!["short_code"]!.GetValue<string>());
        Assert.False(info["13"]!["suggested"]!.GetValue<bool>());
        Assert.Equal(CourseChoices.NotAClass, info["13"]!["why"]!.GetValue<string>());
        Assert.Equal(CourseChoices.PastTerm, info["14"]!["why"]!.GetValue<string>());
        Assert.False(info["11"]!["chosen"]!.GetValue<bool>());
        string revision = (await Send(HttpMethod.Get, "/api/v2/canvas/state"))["revision"]!.GetValue<string>();

        var after = await Send(HttpMethod.Post, "/api/v2/canvas/choose", """{"courses": ["11", "12"]}""");
        Assert.Equal(["Bridge Design", "Cell Biology"], after["outcome"]!["added"]!.AsArray().Select(n => n!.GetValue<string>()).Order());
        Assert.True(after["course_info"]!["11"]!["chosen"]!.GetValue<bool>());
        Assert.Equal("Bridge Design", after["course_info"]!["11"]!["class"]!.GetValue<string>());
        Assert.False(after["course_info"]!["13"]!["chosen"]!.GetValue<bool>());
        Assert.Equal(["11", "12"], after["chosen"]!.AsArray().Select(n => n!.GetValue<string>()).Order());
        Assert.NotEqual(revision, (await Send(HttpMethod.Get, "/api/v2/canvas/state"))["revision"]!.GetValue<string>());
        var classes = await Send(HttpMethod.Get, "/api/v2/library");
        Assert.Contains(classes["classes"]!.AsArray(), c => c!["name"]!.GetValue<string>() == "Cell Biology");

        var dropped = await Send(HttpMethod.Post, "/api/v2/canvas/choose", """{"courses": ["12"], "keep": false}""");
        Assert.Equal(["Bridge Design"], dropped["outcome"]!["removed"]!.AsArray().Select(n => n!.GetValue<string>()));
        Assert.Null(dropped["courses"]!["Bridge Design"]);
    }
}
