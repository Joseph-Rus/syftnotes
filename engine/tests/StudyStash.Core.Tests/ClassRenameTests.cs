using System.Text.Json.Nodes;
using StudyStash.Core.Canvas;

namespace StudyStash.Core.Tests;

/// <summary>"Use Canvas course names": classes named from course codes take their course's names, and nothing is lost.</summary>
public class ClassRenameTests
{
    const string Cs = "202710.TS.CSCI321.A", Engr = "202710.TS.ENGR401.A";

    static void Write(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    /// <summary>A library like one set up before names: two classes named from codes and linked, one typed by hand,
    /// lectures (one still being written), Canvas files, an index, assignments and a chat.</summary>
    static (Config Cfg, Store Store) Library(TempDir dir)
    {
        var cfg = new Config(dir["home"], dir["pool"])
        {
            Classes = [new ClassDef(Cs, ["se"], "Design and testing"), new ClassDef(Engr), new ClassDef("BIO 110")],
        };
        Directory.CreateDirectory(cfg.Home);
        Configs.Save(cfg);
        var store = new Store(cfg.DbPath, cfg.PoolDir);
        store.Save(new Meeting("m1") { Title = "Requirements", Date = "2026-09-01", Transcript = "Today: requirements." }, new Classification(Cs, 0.95, "folder"));
        store.Save(new Meeting("m2") { Title = "Testing", Date = "2026-09-03", Transcript = "Unit tests." }, new Classification(Cs, 0.95, "folder"));
        store.Save(new Meeting("m3") { Title = "Capstone kickoff", Date = "2026-09-02", Transcript = "Teams." }, new Classification(Engr, 0.95, "folder"));
        store.Save(new Meeting("m4") { Title = "Cells", Date = "2026-09-02", Transcript = "Membranes." }, new Classification("BIO 110", 0.95, "folder"));
        store.Enqueue(new Meeting("m5") { Title = "Design patterns", Date = "2026-09-05", Transcript = "Observer." });
        store.SetClass("m5", Cs);
        Write(Path.Combine(store.ClassDir(Cs), "Canvas", "syllabus.md"), "# syllabus");

        CanvasSettings.Update(cfg.Home, s =>
        {
            s.Url = "https://school.instructure.com";
            s.Courses = new() { [Cs] = 4206, [Engr] = 4207 };
            s.Available = new() { ["4206"] = "202710.TS.CSCI321.A - Software Engineering", ["4207"] = "Senior Design", ["4208"] = "Cell Biology" };
            s.CourseInfo = new()
            {
                ["4206"] = new CourseInfo(Cs, "202710.TS.CSCI321.A - Software Engineering", "Fall 2026"),
                ["4207"] = new CourseInfo(Engr, "Senior Design", "Fall 2026"),
            };
            s.Scouts = new() { [Cs] = new ScoutReport(true, "Found 3 files", "2026-09-04T00:00:00Z", 3) };
        });
        new CourseIndex { Class = Cs, CourseId = 4206, Code = Cs, Name = "Software Engineering" }.Save(cfg.Home);
        Assignments.Save(cfg.Home, [new Assignment(Cs, 9001, "Lab 1", "2026-09-30T23:59", 10, "open", null, "", ""),
            new Assignment("BIO 110", 9101, "Lab A", "", null, "open", null, "", "")]);
        CanvasSeen.Mark(cfg.Home, Cs, [77]);
        File.WriteAllText(Path.Combine(cfg.Home, "crawl.json"),
            new JsonObject
            {
                ["manifest"] = new JsonObject
                {
                    [$"asgdir:{Cs}:9001"] = "Canvas/assignments/Lab 1", [$"fileloc:{Cs}:55"] = "Canvas/files/a.pdf",
                    ["file:55:Canvas/files/a.pdf"] = "v1", ["asgdir:BIO 110:9101"] = "x",
                },
            }.ToJsonString());
        Directory.CreateDirectory(Path.Combine(cfg.Home, "chats"));
        File.WriteAllText(Path.Combine(cfg.Home, "chats", "c1.json"), $$"""{"id": "c1", "title": "Q", "class_name": "{{Cs}}", "messages": []}""");
        return (cfg, store);
    }

    [Fact]
    public void The_preview_lists_each_class_named_from_a_code_with_its_course_s_name()
    {
        using var dir = new TempDir();
        var (cfg, store) = Library(dir);
        using var _ = store;

        var plan = ClassRename.Plan(cfg, CanvasSettings.Load(cfg.Home));

        Assert.Equal([(Cs, "Software Engineering", "CSCI 321"), (Engr, "Senior Design", "ENGR 401")], plan.Select(p => (p.From, p.To, p.Code)));
    }

    /// <summary>Classes a 0.7.0 setup named with the section code glued on ("Bridge Design(MECH4120.G)") are offered
    /// their plain course names; two sections of one course keep apart by section.</summary>
    [Fact]
    public void Classes_named_with_a_code_in_brackets_are_offered_the_plain_name()
    {
        using var dir = new TempDir();
        const string Bridge = "Bridge Design(MECH4120.G)", HeatA = "Heat Transfer(MECH2710.A)", HeatB = "Heat Transfer(MECH2710.B)";
        var cfg = new Config(dir["home"], dir["pool"]) { Classes = [new ClassDef(Bridge), new ClassDef(HeatA), new ClassDef(HeatB)] };
        Directory.CreateDirectory(cfg.Home);
        Configs.Save(cfg);
        CanvasSettings.Update(cfg.Home, s =>
        {
            s.Url = "https://school.instructure.com";
            s.Courses = new() { [Bridge] = 51, [HeatA] = 52, [HeatB] = 53 };
            s.Available = new() { ["51"] = Bridge, ["52"] = HeatA, ["53"] = HeatB };
            s.CourseInfo = new()
            {
                ["51"] = new CourseInfo("MECH4120.G", Bridge, "Fall 2026"),
                ["52"] = new CourseInfo("MECH2710.A", HeatA, "Fall 2026"),
                ["53"] = new CourseInfo("MECH2710.B", HeatB, "Fall 2026"),
            };
        });
        var plan = ClassRename.Plan(cfg, CanvasSettings.Load(cfg.Home)).ToDictionary(st => st.From, st => st.To);
        Assert.Equal("Bridge Design", plan[Bridge]);
        Assert.Equal("Heat Transfer (section A)", plan[HeatA]);
        Assert.Equal("Heat Transfer (section B)", plan[HeatB]);
    }

    [Fact]
    public void Renaming_moves_the_folder_and_every_reference_and_loses_no_lecture()
    {
        using var dir = new TempDir();
        var (cfg, store) = Library(dir);
        using var _ = store;
        int before = store.ListNotes().Count;
        string oldFolder = store.ClassFolder(Cs);

        var outcome = ClassRename.Apply(cfg, store, ClassRename.Plan(cfg, CanvasSettings.Load(cfg.Home)));

        Assert.Null(outcome.Problem);
        Assert.Equal(2, outcome.Renamed.Count);
        Assert.Equal(4, outcome.Lectures); // three filed or queued in CS, one in ENGR
        // The config: new names, the old name and the short code kept as other names, and saved.
        Assert.Equal(["Software Engineering", "Senior Design", "BIO 110"], Configs.Load(cfg.Home).ClassNames());
        var se = Configs.Load(cfg.Home).Classes[0];
        Assert.Equal(["se", Cs, "CSCI 321"], se.Aliases);
        Assert.Equal("Design and testing", se.Description);
        // The lectures: every one still there, filed under the new name, its file in the moved folder saying so.
        Assert.Equal(before, store.ListNotes().Count);
        Assert.False(Directory.Exists(oldFolder));
        string newFolder = store.ClassFolder("Software Engineering");
        Assert.True(File.Exists(Path.Combine(newFolder, "Canvas", "syllabus.md")));
        foreach (string id in new[] { "m1", "m2" })
        {
            var row = store.Get(id)!;
            Assert.Equal("Software Engineering", row.ClassName);
            Assert.StartsWith(newFolder, row.MdPath!, StringComparison.Ordinal);
            string text = File.ReadAllText(row.MdPath!);
            Assert.Contains("class: \"Software Engineering\"", text, StringComparison.Ordinal);
            Assert.Contains("class: **Software Engineering**", text, StringComparison.Ordinal);
            Assert.DoesNotContain(Cs, text, StringComparison.Ordinal);
        }
        Assert.Equal(("Software Engineering", Store.Queued), (store.Get("m5")!.ClassName, store.Get("m5")!.Status));
        Assert.Equal("BIO 110", store.Get("m4")!.ClassName);
        Assert.Equal("Senior Design", store.Get("m3")!.ClassName);
        // Canvas: the link, the scout, the index, the assignments, what was opened, and the crawl's memory.
        var s = CanvasSettings.Load(cfg.Home);
        Assert.Equal(4206, s.Courses["Software Engineering"]);
        Assert.False(s.Courses.ContainsKey(Cs));
        Assert.True(s.Scouts.ContainsKey("Software Engineering"));
        Assert.Null(CourseIndex.Load(cfg.Home, Cs));
        Assert.Equal("Software Engineering", CourseIndex.Load(cfg.Home, "Software Engineering")!.Class);
        Assert.Equal(["Software Engineering", "BIO 110"], Assignments.Load(cfg.Home).Select(a => a.ClassName));
        Assert.Equal([77L], CanvasSeen.For(cfg.Home, "Software Engineering"));
        var manifest = JsonNode.Parse(File.ReadAllText(Path.Combine(cfg.Home, "crawl.json")))!["manifest"]!.AsObject();
        Assert.Equal(["asgdir:BIO 110:9101", "asgdir:Software Engineering:9001", "file:55:Canvas/files/a.pdf", "fileloc:Software Engineering:55"],
            manifest.Select(kv => kv.Key).Order(StringComparer.Ordinal));
        // A chat about the class, and a lecture a laptop still sends under the old name.
        Assert.Equal("Software Engineering", JsonNode.Parse(File.ReadAllText(Path.Combine(cfg.Home, "chats", "c1.json")))!["class_name"]!.GetValue<string>());
        Assert.Equal("Software Engineering", Classify.ByRules(new Meeting("late") { Title = "Lecture", Folder = Cs }, cfg.Classes)!.ClassName);
        // Done: the preview is empty now.
        Assert.Empty(ClassRename.Plan(cfg, s));
    }

    [Fact]
    public void Nothing_is_renamed_while_Canvas_is_syncing()
    {
        using var dir = new TempDir();
        var (cfg, store) = Library(dir);
        using var _ = store;
        var crawl = new Crawl(cfg.Home, c => store.ClassFolder(c), () => DateTimeOffset.UtcNow, () => TimeZoneInfo.Utc);
        Assert.True(crawl.Start("https://school.instructure.com", CanvasSettings.Load(cfg.Home).Courses));

        var outcome = ClassRename.Apply(cfg, store, ClassRename.Plan(cfg, CanvasSettings.Load(cfg.Home)), crawl);

        Assert.Empty(outcome.Renamed);
        Assert.Contains("syncing", outcome.Problem, StringComparison.Ordinal);
        Assert.Equal(Cs, store.Get("m1")!.ClassName);
        Assert.True(File.Exists(store.Get("m1")!.MdPath));
    }

    [Fact]
    public void A_folder_already_full_under_the_new_name_leaves_that_class_as_it_is()
    {
        using var dir = new TempDir();
        var (cfg, store) = Library(dir);
        using var _ = store;
        Write(Path.Combine(cfg.PoolDir, "Software Engineering", "mine.md"), "a note of my own");

        var plan = ClassRename.Plan(cfg, CanvasSettings.Load(cfg.Home));
        Assert.DoesNotContain(plan, p => p.From == Cs);

        // Even asked directly, the folder stops it before anything changes.
        var outcome = ClassRename.Apply(cfg, store, [new ClassRenameStep(Cs, "Software Engineering", 4206, "CSCI 321")]);
        Assert.Empty(outcome.Renamed);
        Assert.NotNull(outcome.Problem);
        Assert.Equal(Cs, store.Get("m1")!.ClassName);
        Assert.True(File.Exists(store.Get("m1")!.MdPath));
        Assert.Equal("a note of my own", File.ReadAllText(Path.Combine(cfg.PoolDir, "Software Engineering", "mine.md")));
        Assert.Contains(Cs, Configs.Load(cfg.Home).ClassNames());
    }

    [Fact]
    public void A_change_of_case_only_renames_in_place()
    {
        using var dir = new TempDir();
        var cfg = new Config(dir["home"], dir["pool"]) { Classes = [new ClassDef("software engineering")] };
        Directory.CreateDirectory(cfg.Home);
        using var store = new Store(cfg.DbPath, cfg.PoolDir);
        store.Save(new Meeting("m1") { Title = "Intro", Date = "2026-09-01", Transcript = "Hi." }, new Classification("software engineering", 0.9, "folder"));
        CanvasSettings.Update(cfg.Home, s =>
        {
            s.Courses = new() { ["software engineering"] = 1 };
            s.Available = new() { ["1"] = "Software Engineering" };
        });

        var outcome = ClassRename.Apply(cfg, store, ClassRename.Plan(cfg, CanvasSettings.Load(cfg.Home)));

        Assert.Single(outcome.Renamed);
        Assert.Equal("Software Engineering", store.Get("m1")!.ClassName);
        Assert.True(File.Exists(store.Get("m1")!.MdPath));
        Assert.Equal(["Software Engineering"], Directory.GetDirectories(cfg.PoolDir).Select(Path.GetFileName));
    }
}
