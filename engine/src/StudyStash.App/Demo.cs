using Avalonia.Media;
using StudyStash.App.Services;
using StudyStash.App.ViewModels;
using StudyStash.Audio;

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
            string rec = Skin.Current == SkinKind.Mac ? "⌥⇧R" : "Ctrl+Alt+R";
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
    public static LibraryModel Library(bool due = false, bool answered = false, bool deleting = false, bool deleted = false)
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
            m.Ask.Turns.Add(new AiTurn("What should I practise before the midterm?", "Claude Code")
            {
                Answer = "Practise tracing recursion by hand. She said the midterm asks you to draw the call stack for a small "
                    + "recursive function at its deepest point, so work through factorial(4) and fib(4) frame by frame, writing "
                    + "each call's argument and what it returns. Then check what happens when the base case is wrong: calling "
                    + "factorial(0) with a base case of n == 1 never stops, and the stack overflows. She also said Big-O proofs "
                    + "won't be on it, but you should be able to say why each call waits for the one it made, and why the most "
                    + "recent call finishes first. Last, reread the definitions of call stack, stack frame and base case: two "
                    + "of the short questions come straight from them.",
                Byline = "Claude Code · 12:40, 31:05, 58:20",
            });
        if (deleting) m.Deleting = new LectureDeletion("lec-recursion", "Recursion and the call stack", "CS 101");
        if (deleted) m.Deleted = new LectureDeletion("lec-frames", "Stack frames and scope", "CS 101");
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
    /// no graphics card Whisper can use (the compact turbo, 62% down); <paramref name="choosing"/> opens the list.</summary>
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
        m.ModelLeft = skin == SkinKind.Mac ? "About 4 minutes left" : "About a minute left";
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
