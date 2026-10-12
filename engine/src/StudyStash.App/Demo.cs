using Avalonia.Media;
using StudyStash.App.Services;
using StudyStash.App.ViewModels;
using StudyStash.Audio;
using StudyStash.App.Platform;

namespace StudyStash.App;

/// <summary>The design's sample lectures (CS 101, BIO 110, CALC II, HIST 210): what screenshots and `--demo` show.</summary>
public static class Demo
{
    /// <summary>The design's waveform, bar by bar (heights out of 28).</summary>
    public static readonly double[] Wave = [.. new double[] { 6, 10, 16, 22, 14, 8, 12, 20, 26, 18, 10, 6, 9, 15, 24, 20, 12, 8, 14, 6 }.Select(h => h / 28)];

    /// <summary>The recorder pill's five bars (heights out of 20).</summary>
    public static readonly double[] PillWave = [.. new double[] { 8, 16, 11, 18, 7 }.Select(h => h / 20)];

    static IBrush Cs => Skin.ClassDot(0);
    static IBrush Bio => Skin.ClassDot(1);
    static IBrush Calc => Skin.ClassDot(2);
    static IBrush Hist => Skin.ClassDot(3);

    public static RecorderModel Recorder(bool paused = false, bool expanded = false)
    {
        var r = new RecorderModel { ClassName = "CS 101", ClassDot = Cs, Elapsed = "24:18", IsPaused = paused, Levels = PillWave, Expanded = expanded };
        r.Lines.Add(new HeardLine { Time = "18:05", Text = "Okay, the midterm. Recursion traces will be on it, the stack diagrams from last week." });
        r.Lines.Add(new HeardLine { Time = "19:30", Text = "Think of each call as a plate on a stack. You can only take the top one off." });
        r.Lines.Add(new HeardLine { Time = "21:52", Text = "When factorial of three calls factorial of two, the first call is paused, waiting." });
        r.Lines.Add(new HeardLine { Time = "24:12", Text = "And when we hit the base case, the frames come off one by one.", Latest = true });
        r.Ask = AiDemo.Chat();
        return r;
    }

    public static QuickModel Quick(bool answer)
    {
        var q = new QuickModel();
        if (!answer)
        {
            q.Query = "call stack";
            string rec = Keybindings.Show(KeyAction.Record);
            q.Rows.Add(new QuickRow { Kind = QuickKind.Header, Title = "Lectures", First = true });
            q.Rows.Add(new QuickRow { Kind = QuickKind.Lecture, Title = "Recursion and the call stack", Meta = "CS 101 · Tue 23 Sep", Dot = Cs, Selected = true });
            q.Rows.Add(new QuickRow { Kind = QuickKind.Lecture, Title = "Stack frames and scope", Meta = "CS 101 · Thu 18 Sep", Dot = Cs });
            q.Rows.Add(new QuickRow { Kind = QuickKind.Header, Title = "Passages from notes" });
            q.Rows.Add(new QuickRow { Kind = QuickKind.Passage, Title = "…each frame on the call stack keeps its own copy of n, so nothing gets overwritten…", Sub = "Recursion and the call stack · Key points", Dot = Cs });
            q.Rows.Add(new QuickRow { Kind = QuickKind.Passage, Title = "…once the base case returns, the call stack unwinds and each paused call finishes…", Sub = "Recursion and the call stack · Summary", Dot = Cs });
            q.Rows.Add(new QuickRow { Kind = QuickKind.Header, Title = "Classes" });
            q.Rows.Add(new QuickRow { Kind = QuickKind.Class, Title = "CS 101", Meta = "12 lectures", Dot = Cs });
            q.Rows.Add(new QuickRow { Kind = QuickKind.Header, Title = "Actions" });
            q.Rows.Add(new QuickRow { Kind = QuickKind.Action, Title = "Record CS 101", Meta = rec, Glyph = "mic" });
            q.Rows.Add(new QuickRow { Kind = QuickKind.Action, Title = "Open library", Glyph = "book_2" });
        }
        else
        {
            q.Query = "what's on the cs 101 midterm?";
            q.Answering = true;
            q.Answer = "Recursion traces and call-stack diagrams, from Tuesday's lecture. On Thursday she added that scope rules are fair game, but Big-O proofs aren't.";
            q.Rows.Add(new QuickRow { Kind = QuickKind.Header, Title = "Sources", First = true });
            q.Rows.Add(new QuickRow { Kind = QuickKind.Source, Title = "Recursion and the call stack", Meta = "18:05", Selected = true });
            q.Rows.Add(new QuickRow { Kind = QuickKind.Source, Title = "Stack frames and scope", Meta = "41:20" });
        }
        return q;
    }

    public const string Notes = """
        ## Summary
        A recursive function solves a problem by calling itself on a smaller version of it. Each call gets its own frame on the call stack, which holds that call's arguments and local variables. The calls pause in order until a base case returns, then the stack unwinds and each paused call finishes its work.

        ## Key points
        - Every recursive function needs a base case that returns without calling itself.
        - Each call waits for the one it made, so the most recent call finishes first.
        - Without a base case the stack keeps growing until it overflows.
        - Tracing factorial(3) frame by frame is the skill the midterm tests.

        ## Definitions
        - **Call stack**: the memory a program uses to track calls that haven't finished yet.
        - **Stack frame**: one entry on the stack: a call's arguments, locals and where to return.
        - **Base case**: the input a recursive function answers directly.

        ## Questions to review
        1. Draw the stack for factorial(4) at its deepest point.
        2. What happens if the base case is n == 1 and you call factorial(0)?
        """;

    /// <summary>The sample library; with <paramref name="due"/> a "Due" item heads the sidebar (nothing selected), to
    /// eyeball it against the Canvas Due screen; with <paramref name="answered"/> a long answer sits above the ask bar;
    /// with <paramref name="deleting"/> the window asks before deleting the open lecture, and with
    /// <paramref name="deleted"/> "Deleted · Undo" shows under the list.</summary>
    public static LibraryModel Library(bool due = false, bool answered = false, bool deleting = false, bool deleted = false, bool askingSupport = false)
    {
        var m = new LibraryModel { ClassTitle = "CS 101", ClassCount = "12 lectures", Status = "Library connected", DrawChrome = true };
        if (due) m.Classes.Add(new ClassItem { Name = "Due", IsDue = true, Count = 3 });
        m.Classes.Add(new ClassItem { Name = "CS 101", Dot = Cs, Count = 12, Selected = true });
        m.Classes.Add(new ClassItem { Name = "BIO 110", Dot = Bio, Count = 9 });
        m.Classes.Add(new ClassItem { Name = "CALC II", Dot = Calc, Count = 11 });
        m.Classes.Add(new ClassItem { Name = "HIST 210", Dot = Hist, Count = 7 });
        m.Unsorted.Count = 2;
        var week = new LectureGroup { Label = "This week", First = true };
        week.Items.Add(new LectureCard { Title = "Recursion and the call stack", Meta = "Tue 23 Sep · 1 h 12 min", Summary = "A recursive function solves a problem by calling itself on a smaller version of it.", Selected = true });
        week.Items.Add(new LectureCard { Title = "Stack frames and scope", Meta = "Thu 18 Sep · 1 h 14 min", Summary = "Where a variable lives decides who can see it, and for how long." });
        week.Items.Add(new LectureCard { Title = "Functions as values", Meta = "Tue 16 Sep · 1 h 10 min", Summary = "Passing a function to another function, and why map and filter work." });
        var last = new LectureGroup { Label = "Last week" };
        last.Items.Add(new LectureCard { Title = "Loops and invariants", Meta = "Thu 11 Sep · 1 h 13 min", Summary = "An invariant is a statement that stays true on every pass through a loop." });
        last.Items.Add(new LectureCard { Title = "Tracing small programs", Meta = "Tue 9 Sep · 1 h 08 min", Summary = "Reading code line by line and writing down every value as it changes.", Last = true });
        m.Groups.Add(week);
        m.Groups.Add(last);
        m.Note = new NoteModel
        {
            ClassName = "CS 101", Dot = Cs, Meta = "CS 101 · Tuesday 23 September · 1 h 12 min", Title = "Recursion and the call stack", Markdown = Notes,
        };
        m.Ask = AiDemo.AskIdle();
        if (answered)
            m.Ask.Turns.Add(new AiTurn("What should I practise before the midterm?", "Claude")
            {
                Answer = "Practise tracing recursion by hand. She said the midterm asks you to draw the call stack for a small "
                    + "recursive function at its deepest point, so work through factorial(4) and fib(4) frame by frame, writing "
                    + "each call's argument and what it returns. Then check what happens when the base case is wrong: calling "
                    + "factorial(0) with a base case of n == 1 never stops, and the stack overflows. She also said Big-O proofs "
                    + "won't be on it, but you should be able to say why each call waits for the one it made, and why the most "
                    + "recent call finishes first. Last, reread the definitions of call stack, stack frame and base case: two "
                    + "of the short questions come straight from them.",
                Byline = "Claude · from 12:40, 31:05 and 58:20",
            });
        if (deleting) m.Deleting = new LectureDeletion("lec-recursion", "Recursion and the call stack", "CS 101");
        if (deleted) m.Deleted = new LectureDeletion("lec-frames", "Stack frames and scope", "CS 101");
        if (askingSupport) m.AskForSupport(last: false);
        return m;
    }

    /// <summary>The sample library on Home (<paramref name="cls"/>: on that class's home instead), with a week of
    /// lectures, four things to hand in (one late), and the classes that meet today and tomorrow on the calendar.</summary>
    public static LibraryModel Overview(string? cls = null, bool narrow = false, bool canvas = true, bool calendars = true)
    {
        var m = Library(due: canvas);
        m.Note = null;
        m.Ask = null;
        foreach (var c in m.Classes) c.Selected = cls is not null && c.Name == cls && !c.IsDue;
        m.Home.Selected = cls is null;
        var now = new DateTimeOffset(2026, 9, 30, 10, 20, 0, TimeSpan.FromHours(-7));
        var zone = TimeZoneInfo.CreateCustomTimeZone("Demo", TimeSpan.FromHours(-7), "Demo", "Demo");
        OverviewModel.LectureFacts L(string id, string title, string c, int daysAgo, double min, string summary) =>
            new(id, title, c, now.AddDays(-daysAgo).AddHours(-1), min * 60, summary, false);
        var lectures = new List<OverviewModel.LectureFacts>
        {
            L("l1", "Recursion and the call stack", "CS 101", 1, 72, "A recursive function solves a problem by calling itself on a smaller version of it."),
            L("l2", "Cell membranes and transport", "BIO 110", 1, 50, "How the lipid bilayer lets some molecules through and keeps others out."),
            L("l3", "Integration by parts", "CALC II", 2, 55, "Turning the integral of a product into one that's easier, and choosing u well."),
            L("l4", "Stack frames and scope", "CS 101", 6, 74, "Where a variable lives decides who can see it, and for how long."),
            L("l5", "The Treaty of Versailles", "HIST 210", 7, 48, "What the treaty asked of Germany, and why so many thought it would not hold."),
            L("l6", "Functions as values", "CS 101", 8, 70, "Passing a function to another function, and why map and filter work."),
        };
        CanvasApi.Item Item(string c, string id, string name, double inDays, string status = "open", bool missing = false) => new()
        {
            Class = c, Id = id, Name = name, Kind = "assignment", DueAt = now.AddDays(inDays), Status = status, Missing = missing, Points = 10,
        };
        var due = new CanvasApi.DueResponse
        {
            ToHandIn = 4,
            Groups =
            [
                new CanvasApi.Group { Key = "overdue", Label = "Overdue", Items = [Item("BIO 110", "a1", "Lab 3 write-up", -1, "missing", missing: true)] },
                new CanvasApi.Group { Key = "week", Label = "This week", Items = [Item("CS 101", "a2", "Problem set 4: recursion", 1), Item("CALC II", "a3", "Quiz 5", 2), Item("CS 101", "a4", "Reading: SICP 1.2", 5)] },
            ],
        };
        var today = new DateTimeOffset(now.Date, now.Offset);
        OverviewModel.Meeting Meets(string c, int day, double hour, double hours = 1.25) => new(today.AddDays(day).AddHours(hour), today.AddDays(day).AddHours(hour + hours), c);
        var meetings = new List<OverviewModel.Meeting>
        {
            Meets("CS 101", 0, 10), Meets("BIO 110", 0, 13.5), Meets("CALC II", 1, 9), Meets("CS 101", 2, 10), Meets("HIST 210", 2, 14),
        };
        var sources = new OverviewModel.Sources
        {
            Now = now, Zone = zone,
            Classes = [("CS 101", Cs, 12), ("BIO 110", Bio, 9), ("CALC II", Calc, 11), ("HIST 210", Hist, 7)],
            Lectures = lectures,
            Due = canvas ? due : null,
            Canvas = canvas ? [new CanvasApi.ClassRow { Class = "CS 101", Linked = true }] : [],
            Meetings = calendars ? meetings : null,
            Unsorted = 2,
        };
        if (cls == "\u0001due")
        {
            foreach (var c in m.Classes) c.Selected = c.IsDue;
            m.Home.Selected = false;
            m.Overview = OverviewModel.ForDue(sources);
        }
        else if (cls is null)
        {
            // As on a computer that records: Record beside the date, "Buy us a coffee" at the page's foot.
            var home = OverviewModel.Home(sources);
            home.OnRecord = () => { };
            home.OnSupport = () => { };
            m.Overview = home;
        }
        else
        {
            var facts = new ClassHomeFacts(cls, Cs, 12, canvas ? "CS 101" : null, canvas ? "Introduction to Computer Science" : null,
                "Programming from the ground up: recursion, data structures and how programs run.",
                canvas ? new CanvasApi.Counts { ToHandIn = 2, Done = 6, Modules = 6, Files = 9, Announcements = 4, AnnouncementsNew = 1 } : null);
            var page = OverviewModel.ForClass(facts, sources);
            page.AddLinks(facts, () => { }, canvas ? _ => { } : null);
            m.Overview = page;
        }
        m.Narrow = narrow;
        return m;
    }

    /// <summary>
    /// The design's own Home (the handoff's screenshots): Monday 12 October, eight classes each with its highlighter,
    /// one thing late, three due this week, the week's class meetings (the first of them recorded), and the four
    /// newest of twelve lectures. For laying a picture of the app over the design's.
    /// </summary>
    public static LibraryModel Semester(bool recording = false, bool narrow = false)
    {
        (string Name, string Hue, int Lectures)[] classes =
        [
            ("Chapel Convocation", "Blue", 0), ("Web Application Development", "Green", 0),
            ("Theory of Computation and Compilers(CSCI4150.A)", "Yellow", 4), ("Machine and Deep Learning", "Orange", 2), ("Data Mining", "Pink", 1),
            ("Capstone Design", "Teal", 2), ("Internship Report and Presentation", "Violet", 0), ("Personal Projects", "Lime", 2),
        ];
        IBrush Dot(string hue) => Skin.ClassDot(Array.IndexOf(Skin.ClassHues, hue));
        const string theory = "Theory of Computation and Compilers(CSCI4150.A)";
        var m = new LibraryModel { Status = "Library on this Mac · Canvas synced at 10:18", DrawChrome = true, OnAddClass = () => { } };
        m.Classes.Add(new ClassItem { Name = "Due", IsDue = true, Count = 16 });
        foreach (var (name, hue, lectures) in classes) m.Classes.Add(new ClassItem { Name = name, Dot = Dot(hue), Count = lectures });
        m.Unsorted.Count = 1;
        m.Home.Selected = true;

        var zone = TimeZoneInfo.CreateCustomTimeZone("Demo", TimeSpan.FromHours(-4), "Demo", "Demo");
        var monday = new DateTimeOffset(2026, 10, 12, 0, 0, 0, TimeSpan.FromHours(-4));
        var now = monday.AddHours(10.3);
        OverviewModel.Meeting Meets(string c, int day, double hour) => new(monday.AddDays(day).AddHours(hour), monday.AddDays(day).AddHours(hour + 1.25), c);
        CanvasApi.Item Item(string c, string id, string name, int day, string status = "open", bool missing = false) => new()
        {
            Class = c, Id = id, Name = name, Kind = "assignment", DueAt = monday.AddDays(day).AddHours(23).AddMinutes(59), Status = status, Missing = missing,
        };
        OverviewModel.LectureFacts L(string id, string title, string c, DateTimeOffset at, double min, string summary) => new(id, title, c, at, min * 60, summary, false);
        var sources = new OverviewModel.Sources
        {
            Now = now, Zone = zone,
            Classes = [.. classes.Select(c => (c.Name, Dot(c.Hue), c.Lectures))],
            Unsorted = 1,
            Lectures =
            [
                L("s1", "Linear vs Integer Linear Programming and NP-Hardness", theory, monday.AddHours(9.2), 13,
                    "Built up from independent linear equations to a coupled system $b = Ax + s$, with an objective $c^T x$ to maximize or minimize."),
                L("s2", "DFA Design Problems and Product Construction", theory, monday.AddDays(-5).AddHours(9), 53,
                    "Worked solutions to a DFA design problem (parts a–d), each built from accept and reject strings and then checked state by state."),
                L("s3", "Convolutional Neural Networks: Kernels, Stride, Dilation", "Machine and Deep Learning", monday.AddDays(-7).AddHours(13.5), 28,
                    "Why CNNs generalize beyond their training data instead of memorizing it, then kernels, stride and dilation on MNIST."),
                L("s4", "Drone Interception: Vision Training and Target Dataset", "Capstone Design", monday.AddDays(-12).AddHours(13), 23,
                    "A team working session on the drone-interception project, not a formal lecture."),
            ],
            Canvas = [new CanvasApi.ClassRow { Class = theory, Linked = true }],
            Due = new CanvasApi.DueResponse
            {
                ToHandIn = 16,
                Groups =
                [
                    new CanvasApi.Group { Key = "overdue", Label = "Overdue", Items = [Item(theory, "d1", "Asynchronous Lecture 5", -7, "missing", missing: true)] },
                    new CanvasApi.Group
                    {
                        Key = "week", Label = "This week",
                        Items = [Item(theory, "d2", "CCHW2", 2), Item("Web Application Development", "d3", "HW3", 2), Item("Machine and Deep Learning", "d4", "CA.02: First Contact", 6)],
                    },
                ],
            },
            Meetings =
            [
                Meets(theory, 0, 9), Meets("Machine and Deep Learning", 0, 13.5), Meets("Web Application Development", 1, 10.5), Meets("Data Mining", 1, 14),
                Meets(theory, 2, 9), Meets("Chapel Convocation", 2, 11), Meets("Machine and Deep Learning", 2, 13.5),
                Meets("Web Application Development", 3, 10.5), Meets("Data Mining", 3, 14), Meets("Capstone Design", 4, 13),
            ],
        };
        var home = OverviewModel.Home(sources);
        home.OnRecord = () => { };
        home.OnSupport = () => { };
        if (recording)
        {
            home.Recorder = new RecorderModel { ClassName = theory, Elapsed = "12:04", Levels = PillWave };
            home.Recording = true;
        }
        home.Ask = AiDemo.AskIdle();
        home.Ask.OnlyScopes("all");
        m.Overview = home;
        m.Narrow = narrow;
        return m;
    }

    /// <summary>A term with many classes, their names as a school's catalogue writes them (long, with the section on
    /// the end), none of them with a lecture yet: the sidebar scrolls, names end in "…", and the open class's name
    /// is too long for the list's header.</summary>
    public static LibraryModel Crowded()
    {
        string[] names =
        [
            "Introduction to Organic Chemistry(CHEM2310.A)", "Differential Equations and Linear Algebra(MATH2250.B)",
            "Principles of Macroeconomics(ECON2020.C)", "Studio Art: Drawing Fundamentals(ART1100.A)", "World Religions(REL1300.D)",
            "Fluid Mechanics(ME3310.A)", "Data Structures and Algorithms(CS2420.B)", "Human Anatomy and Physiology Lab(BIO2320L.A)",
            "Technical Writing for Scientists(ENG3050.C)", "Probability and Statistics for Engineers(STAT3110.A)",
            "Music Theory II(MUS1220.A)", "Introduction to Psychology(PSY1010.E)", "Senior Project Seminar(ME4900.A)",
        ];
        const int open = 9;
        var m = new LibraryModel { ClassTitle = names[open], ClassCount = "0 lectures", Status = "Library running on this PC", DrawChrome = true };
        for (int i = 0; i < names.Length; i++) m.Classes.Add(new ClassItem { Name = names[i], Dot = Skin.ClassDot(i), Count = i == 6 ? 12 : 0, Selected = i == open });
        m.Empty = $"No lectures in {names[open]} yet. Record one and it lands here.";
        return m;
    }

    public static SetupModel Setup(SkinKind skin)
    {
        var m = SetupModel.For(skin, AppRole.Laptop);
        if (skin == SkinKind.Mac)
        {
            m.Go(SetupStep.Model);
            ModelStep(m, skin);
        }
        else
        {
            m.Go(SetupStep.Taskbar);
        }
        return m;
    }

    /// <summary>Setup's model step on a made-up computer: a Mac with Apple silicon (large-v3, 62% down) or a PC with
    /// no graphics card Whisper can use (Parakeet, 62% down); <paramref name="choosing"/> opens the list.</summary>
    public static void ModelStep(SetupModel m, SkinKind skin, bool choosing = false)
    {
        var hw = skin == SkinKind.Mac
            ? new HardwareProfile(HostOs.Mac, System.Runtime.InteropServices.Architecture.Arm64, 8, true, 16)
            : new HardwareProfile(HostOs.Windows, System.Runtime.InteropServices.Architecture.X64, 8, true, 16,
                new GraphicsCard("Intel(R) UHD Graphics 620", 0.125, true), Vulkan: true);
        var advice = WhisperModels.Advise(hw);
        m.Models.Clear();
        foreach (var c in ModelChoice.For(advice.Model, advice, Path.Combine(Path.GetTempPath(), "studystash-demo-no-models"))) m.Models.Add(c);
        m.ChosenModel = m.Models.First(c => c.Chosen);
        m.ChoosingModel = choosing;
        m.ModelName = advice.Model.Name;
        m.ModelSize = Services.Setup.About(advice.Model.Bytes);
        m.ModelProgress = 0.62;
        var done = new DownloadProgress((long)(advice.Model.Bytes * 0.62), advice.Model.Bytes, 0);
        m.ModelDone = done.Amount;
        m.ModelLeft = skin == SkinKind.Mac ? "About 4 minutes left" : "About 2 minutes left";
    }

    /// <summary>A library-only computer's dropdown (the Mac mini at home): running, 21 lectures, Canvas synced ten
    /// minutes ago, one laptop connected; no Record.</summary>
    public static PanelModel LibraryPanel()
    {
        var now = new DateTimeOffset(2026, 9, 21, 15, 0, 0, TimeSpan.Zero);
        var overview = new System.Text.Json.Nodes.JsonObject
        {
            ["classes"] = new System.Text.Json.Nodes.JsonArray(new System.Text.Json.Nodes.JsonObject { ["name"] = "CS 101", ["lectures"] = 12 },
                new System.Text.Json.Nodes.JsonObject { ["name"] = "BIO 110", ["lectures"] = 7 }),
            ["unsorted"] = 2,
            ["laptops"] = new System.Text.Json.Nodes.JsonArray(new System.Text.Json.Nodes.JsonObject { ["name"] = "Sam's MacBook Air", ["seen"] = now.AddSeconds(-20).ToString("o") }),
        };
        string device = Skin.Current == SkinKind.Mac ? "Mac" : "PC";
        return new PanelModel
        {
            LibraryOnly = true,
            Status = $"Library running on this {device}",
            Library = LibraryPanelWords.From(true, false, device, overview, new Services.CanvasApi.State { Status = "connected", LastSync = now.AddMinutes(-10) }, now, TimeZoneInfo.Utc),
        };
    }

    public static PanelModel Panel(bool recording)
    {
        var p = new PanelModel
        {
            ClassName = "CS 101", ClassDot = Cs, Hint = "Picked by you", Status = "Library connected · Model ready",
            IsRecording = recording, Elapsed = "24:18", Levels = Wave, LastLine = "“…and when we hit the base case, the frames come off one by one.”",
        };
        if (!recording)
        {
            p.Recent.Add(new LectureItem { Title = "Membranes and osmosis", Detail = "Transcribing 42%", Time = "11:40", Dot = Bio, Progress = 0.42 });
            p.Recent.Add(new LectureItem { Title = "Series convergence tests", Detail = "Writing notes…", Time = "10:50", Dot = Calc, Busy = true });
            p.Recent.Add(new LectureItem { Title = "Recursion and the call stack", Detail = "Filed in CS 101", Time = "9:02", Dot = Cs, Selected = true });
            p.Recent.Add(new LectureItem { Title = "The Treaty of Versailles", Detail = "Filed in HIST 210", Time = "Mon", Dot = Hist });
        }
        else
        {
            p.Recent.Add(new LectureItem { Title = "The Treaty of Versailles", Detail = "Filed in HIST 210", Time = "Mon", Dot = Hist });
            p.Recent.Add(new LectureItem { Title = "Cell transport, part 1", Detail = "Filed in BIO 110", Time = "Fri", Dot = Bio });
        }
        return p;
    }
}
