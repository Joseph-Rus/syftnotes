using Microsoft.Extensions.Time.Testing;
using StudyStash.Core.Ai;
using StudyStash.Core.Canvas;

namespace StudyStash.Core.Tests;

/// <summary>A class's "What it covers", written for the student (ClassTopics): from Canvas's words on day one, never
/// over what they typed, and written again only once a few more lectures are filed, at most once a day.</summary>
public class ClassTopicsTests
{
    const string Thermo = "Thermodynamics", Heat = "Heat Transfer";

    static ModuleInfo Module(int at, string name) => new() { Position = at, Name = name };

    /// <summary>Two close made-up classes, each with Canvas modules laid out the ways instructors do.</summary>
    static Config TwoCloseClasses(TempDir dir)
    {
        var cfg = new Config(dir["home"], dir["pool"]) { Classes = [new ClassDef(Thermo), new ClassDef(Heat)] };
        Configs.Save(cfg);
        new CourseIndex
        {
            Class = Thermo, CourseId = 1, Code = "ME 301", Name = "Thermodynamics I",
            Modules = [Module(1, "Start Here"), Module(2, "Week 1: Systems, Properties and State (Aug 26 - Aug 30)"), Module(3, "Week 2 - The First Law"),
                Module(4, "Week 3: Entropy and the Second Law"), Module(5, "Module 4 – Carnot Cycles and Heat Engines"), Module(6, "Midterm Exam")],
        }.Save(cfg.Home);
        new CourseIndex
        {
            Class = Heat, CourseId = 2, Code = "ME 342", Name = "Heat Transfer",
            // Modules that only say when: the pages say what.
            Modules = [Module(1, "Week 1"), Module(2, "Week 2"), Module(3, "Week 3")],
            Pages = [new PageInfo { Title = "Syllabus" }, new PageInfo { Title = "1. Conduction and Fourier's Law" }, new PageInfo { Title = "2. Convection (Sep 9)" }],
            Assignments = [new AssignmentInfo { Name = "HW 3: Radiation and View Factors" }, new AssignmentInfo { Name = "Homework 4" }],
        }.Save(cfg.Home);
        return cfg;
    }

    [Fact]
    public async Task Close_classes_get_canvas_words_with_no_ai_and_the_sorting_ai_reads_them()
    {
        using var dir = new TempDir();
        var cfg = TwoCloseClasses(dir);
        using var store = new Store(cfg.DbPath, cfg.PoolDir);
        await new ClassTopics(cfg, store, chat: null, log: _ => { }).RunAsync();

        var saved = Configs.Load(cfg.Home).Classes;
        Assert.Equal("Systems, Properties and State, The First Law, Entropy and the Second Law, Carnot Cycles and Heat Engines", saved[0].Description);
        Assert.Equal("Conduction and Fourier's Law, Convection, Radiation and View Factors", saved[1].Description);
        Assert.All(saved, c => Assert.Equal(ClassTopics.ByCanvas, c.DescriptionBy));

        // What the AI is given when it sorts a lecture recorded with no class: each class with what it covers.
        var lecture = new Meeting("m1") { Title = "Lecture", Transcript = "Today: entropy never decreases in an isolated system, the second law." };
        string prompt = Classify.BuildPrompt(lecture, Classify.WithCanvas(saved, cfg.Home));
        Assert.Contains($"- {Thermo} — Systems, Properties and State, The First Law, Entropy and the Second Law, Carnot Cycles and Heat Engines (on Canvas: Thermodynamics I · ME 301)", prompt);
        Assert.Contains($"- {Heat} — Conduction and Fourier's Law, Convection, Radiation and View Factors (on Canvas: ME 342)", prompt);
    }

    [Fact]
    public async Task What_the_student_typed_is_never_written_over()
    {
        using var dir = new TempDir();
        var cfg = TwoCloseClasses(dir);
        cfg.Classes[1].Description = "Prof. Okafor's class, Tuesdays";
        using var store = new Store(cfg.DbPath, cfg.PoolDir);
        var topics = new ClassTopics(cfg, store, chat: null, log: _ => { });
        await topics.RunAsync();
        Assert.Equal("Prof. Okafor's class, Tuesdays", cfg.Classes[1].Description);
        Assert.Equal("", cfg.Classes[1].DescriptionBy);

        // Settings sends the written text back as it was, or with the box left empty: it stays written, and marked.
        var written = cfg.Classes[0];
        Assert.Equal((written.Description, ClassTopics.ByCanvas), ClassTopics.Kept(written, written.Description));
        Assert.Equal((written.Description, ClassTopics.ByCanvas), ClassTopics.Kept(written, ""));
        Assert.Equal((written.Description, ClassTopics.ByCanvas), ClassTopics.Kept(written, "an older copy", ClassTopics.ByCanvas));
        // The student changes it: it's theirs, and nothing writes it again.
        var (text, by) = ClassTopics.Kept(written, "Entropy, the second law and exergy");
        Assert.Equal(("Entropy, the second law and exergy", ""), (text, by));
        written.Description = text;
        written.DescriptionBy = by;
        for (int i = 0; i < 5; i++) store.Save(new Meeting($"m{i}") { Title = $"Lecture {i}", Transcript = "Entropy." }, new Classification(Thermo, 0.9, "ollama", "", ["entropy"]));
        topics.Due();
        await topics.RunAsync();
        Assert.Equal("Entropy, the second law and exergy", cfg.Classes[0].Description);
        Assert.Equal("", cfg.Classes[0].DescriptionBy);
    }

    [Fact]
    public async Task The_ai_writes_them_again_once_three_more_lectures_are_filed_and_a_day_has_passed()
    {
        using var dir = new TempDir();
        var cfg = TwoCloseClasses(dir);
        cfg.Classes.RemoveAt(1);
        new AiSettings { Provider = "claude" }.Save(cfg.Home);
        using var store = new Store(cfg.DbPath, cfg.PoolDir);
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 9, 1, 12, 0, 0, TimeSpan.Zero));
        var asked = new List<string>();
        // The AI's answer is a stand-in: what's checked is when it's asked, what it's given, and where its words go.
        SortChatFn ai = (_, prompt, _) =>
        {
            asked.Add(prompt);
            return Task.FromResult($$"""{"topics": ["entropy", "the second law", "answer {{asked.Count}}"]}""");
        };
        var topics = new ClassTopics(cfg, store, ai, log: _ => { }, time: clock);
        await topics.RunAsync();
        Assert.Equal(("entropy, the second law, answer 1", ClassTopics.ByAi), (cfg.Classes[0].Description, cfg.Classes[0].DescriptionBy));
        Assert.Contains("- Week 3: Entropy and the Second Law", asked[0]);

        void File(int i) => store.Save(new Meeting($"m{i}") { Title = $"Lecture {i}", Date = $"2026-09-{i + 2:00}", Transcript = "Heat engines." },
            new Classification(Thermo, 0.9, "ollama", "", ["Carnot efficiency", "isentropic processes"]));
        File(1);
        File(2);
        File(3);
        topics.Due();
        await topics.RunAsync();
        Assert.Single(asked); // three more, but the same day

        clock.Advance(TimeSpan.FromDays(1));
        topics.Due();
        await topics.RunAsync();
        Assert.Equal(2, asked.Count);
        Assert.Contains("- Carnot efficiency", asked[1]); // what the lectures covered
        Assert.Equal("entropy, the second law, answer 2", cfg.Classes[0].Description);

        clock.Advance(TimeSpan.FromDays(3));
        File(4);
        topics.Due();
        await topics.RunAsync();
        Assert.Equal(2, asked.Count); // one more isn't enough
    }
}
