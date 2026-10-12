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

    /// <summary>The sample library with a lecture's page open (CS 101's "Recursion and the call stack"); with
    /// <paramref name="due"/> a "Due" item is in the sidebar; with <paramref name="answered"/> a long answer sits above
    /// the ask bar; with <paramref name="deleting"/> the window asks before deleting the open lecture, and with
    /// <paramref name="deleted"/> "Deleted · Undo" shows at the page's foot; <paramref name="askingSupport"/> is Home
    /// with the ask for a tip on it.</summary>
    public static LibraryModel Library(bool due = false, bool answered = false, bool deleting = false, bool deleted = false, bool askingSupport = false)
    {
        var m = new LibraryModel { Status = "Library connected", DrawChrome = true, UnsortedCount = 2 };
        if (due) m.Classes.Add(new ClassItem { Name = "Due", IsDue = true, Count = 3 });
        m.Classes.Add(new ClassItem { Name = "CS 101", Dot = Cs, Count = 12, Selected = true });
        m.Classes.Add(new ClassItem { Name = "BIO 110", Dot = Bio, Count = 9 });
        m.Classes.Add(new ClassItem { Name = "CALC II", Dot = Calc, Count = 11 });
        m.Classes.Add(new ClassItem { Name = "HIST 210", Dot = Hist, Count = 7 });
        m.Note = new NoteModel
        {
            Id = "lec-recursion", ClassName = "CS 101", Dot = Cs, When = "Tuesday 23 September at 10:02 AM, 1 h 12 min", Title = "Recursion and the call stack", Markdown = Notes,
            OnRename = _ => Task.FromResult(true),
        }.Titled();
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
        if (askingSupport)
        {
            m = Overview();
            m.AskForSupport(last: false);
        }
        return m;
    }

    /// <summary>The sample library on Home (<paramref name="cls"/>: on that folder's page instead, or "\u0001due" for
    /// Due), with a week of lectures, four things to hand in (one missing), and the classes that meet today and
    /// tomorrow on the calendar.</summary>
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
            L("l0", "Recording, Sun 27 Sep at 4:12 PM", "Unsorted", 3, 18, "A planning conversation about the study group's schedule before the midterm."),
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
                new CanvasApi.Group { Key = "handed_in", Label = "Handed in", Items = [Item("CS 101", "a0", "Problem set 3: loops", -6, "submitted")] },
            ],
        };
        var today = new DateTimeOffset(now.Date, now.Offset);
        int n = 0;
        OverviewModel.Event Meets(string c, int day, double hour, double hours = 1.25) => new($"e{n++}", c, today.AddDays(day).AddHours(hour), today.AddDays(day).AddHours(hour + hours), c);
        var events = new List<OverviewModel.Event>
        {
            Meets("CS 101", -2, 10), Meets("CS 101", 0, 10), Meets("BIO 110", 0, 13.5), Meets("CALC II", 1, 9), Meets("CS 101", 2, 10), Meets("HIST 210", 2, 14),
            new("e-study", "Study group", today.AddHours(16), today.AddHours(17), null),
        };
        var sources = new OverviewModel.Sources
        {
            Now = now, Zone = zone,
            Classes = [("CS 101", Cs, 12), ("BIO 110", Bio, 9), ("CALC II", Calc, 11), ("HIST 210", Hist, 7)],
            Lectures = lectures,
            Due = canvas ? due : null,
            Canvas = canvas ? [new CanvasApi.ClassRow { Class = "CS 101", Linked = true }] : [],
            Events = calendars ? events : null,
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
            page.AddLinks(facts, canvas ? _ => { } : null);
            page.OnRecord = () => { };
            m.Overview = page;
        }
        m.Narrow = narrow;
        return m;
    }

    /// <summary>What a student typed in Machine Learning's lecture, as they typed it: the design's own sample.</summary>
    public const string Typed = """
        # History
        - Attention Is All You Need, 2017
        - ChatGPT, Nov 2022

        # Chatbots and people
        - news story about AI-driven delusion

        # Why not fully connected nets
        - fixed input size

        # Self-attention
        - q · k → softmax → weights on v
        """;

    /// <summary>That lecture's notes once they're written: the student's lines kept (marked), what the recording added
    /// round them.</summary>
    public const string Merged = """
        ### ==History==
        - ==Attention Is All You Need, 2017== The Google paper that introduced the transformer, built on attention instead of recurrence.
        - ==ChatGPT, Nov 2022== The release that brought large language models to a general audience.
        - The 2022 claim that Google’s LaMDA was sentient, and why the class treated it with skepticism.

        ### ==Chatbots and people==
        - ==news story about AI-driven delusion== How a chatbot that keeps agreeing with its user can reinforce false beliefs.

        ### ==Why not fully connected nets==
        - ==fixed input size== A fully connected network expects a fixed-length input, but sentences vary in length and word order carries meaning.

        ### ==Self-attention==
        - ==q · k → softmax → weights on v== Each token scores every other token with a dot product of its query and their keys; softmax turns the scores into weights over the value vectors.
        - ==position encodings, on the exam?== Attention alone ignores order, so a position signal is added to each token’s embedding. The professor said to know both sinusoidal and learned versions.

        ### Tokenizers and decoder-only models
        - Text is split into subword tokens before it is embedded.
        - ==GPT = decoder only, predicts next token== Generation is autoregressive: each predicted token is fed back in as input for the next one.

        ### ==To do==
        - ==read ch. 12 before Wed==
        """;

    /// <summary>The pages of the design's own sample (the handoff's reference pages and screenshots).</summary>
    public enum Page { Home, HomeUnsorted, Due, DueWeek, FolderClass, FolderClub, Note, NoteOnlyMine, Recording, CanvasLists }

    /// <summary>
    /// The design's own sample (the handoff's screenshots): Monday 12 October, 1:34 PM. Five classes, a club and two
    /// work folders, each with its highlighter; Capstone's team sync eleven minutes off; a quiz due tonight and one
    /// thing missing. <paramref name="page"/> picks the screen. For laying a picture of the app over the design's.
    /// </summary>
    public static LibraryModel Semester(Page page = Page.Home, bool recording = false, bool narrow = false)
    {
        (string Name, string Hue, int Notes, string Group)[] folders =
        [
            ("Machine Learning", "Blue", 14, ""), ("Capstone", "Green", 4, ""), ("Computer Architecture", "Violet", 8, ""), ("Data Mining", "Orange", 9, ""),
            ("Theory of Computation", "Pink", 15, ""), ("ACM", "Teal", 5, "Clubs"), ("Intellievent", "Yellow", 2, "Work"), ("NSWC", "Lime", 1, "Work"),
        ];
        IBrush Dot(string hue) => Skin.ClassDot(Array.IndexOf(Skin.ClassHues, hue));
        IBrush DotOf(string folder) => folders.FirstOrDefault(f => f.Name == folder) is { Name: not null } f ? Dot(f.Hue) : Brushes.Gray;
        string GroupOf(string folder) => folders.FirstOrDefault(f => f.Name == folder).Group ?? "";
        const string ml = "Machine Learning";
        var m = new LibraryModel { Status = "Library on this Mac · Canvas synced at 12:58", DrawChrome = true, OnAddClass = () => { }, UnsortedCount = 1 };
        m.Classes.Add(new ClassItem { Name = "Due", IsDue = true, Count = 24, Selected = page is Page.Due or Page.DueWeek });
        string group = "";
        foreach (var (name, hue, notes, g) in folders)
        {
            m.Classes.Add(new ClassItem { Name = name, Dot = Dot(hue), Count = notes, Group = g, Section = g != group ? g : "" });
            group = g;
        }
        m.Home.Selected = page is Page.Home or Page.HomeUnsorted;

        var zone = TimeZoneInfo.CreateCustomTimeZone("Demo", TimeSpan.FromHours(-4), "Demo", "Demo");
        var monday = new DateTimeOffset(2026, 10, 12, 0, 0, 0, TimeSpan.FromHours(-4));
        var now = monday.AddHours(13).AddMinutes(34);
        int n = 0;
        OverviewModel.Event At(string title, string? folder, int day, double hour, double hours = 0.85) =>
            new($"ev{n++}", title, monday.AddDays(day).AddHours(hour), monday.AddDays(day).AddHours(hour + hours), folder);
        var events = new List<OverviewModel.Event>
        {
            At(ml, ml, 0, 9.5), At("Capstone team sync", "Capstone", 0, 13.75), At("Officer meeting", "ACM", 0, 18), At("Data Mining", "Data Mining", 1, 8.75),
            At("Computer Architecture", "Computer Architecture", 1, 10.5), At(ml, ml, 2, 9.5), At("Theory of Computation", "Theory of Computation", 2, 13),
            At("Data Mining", "Data Mining", 3, 8.75), At("Computer Architecture", "Computer Architecture", 3, 10.5), At("ACM", "ACM", 3, 18), At(ml, ml, 4, 9.5),
        };
        m.ComingUp.Show(true, [.. events.Where(e => e.End > now).Take(2).Select(e =>
        {
            bool soon = e.Start - now <= OverviewModel.Soon;
            var at = TimeZoneInfo.ConvertTime(e.Start, zone);
            string brief = soon ? $"In {(int)Math.Ceiling((e.Start - now).TotalMinutes)} minutes" : $"{(at.Date == monday.Date ? "Today" : "Tomorrow")} {at:h:mm tt}";
            return new ComingUpRow(e.Id, brief, e.Title, null, e.Class, DotOf(e.Class ?? ""), false, brief, soon);
        })]);

        CanvasApi.Item Item(string c, string id, string name, int day, string status = "open", bool missing = false) => new()
        {
            Class = c, Id = id, Name = name, Kind = "assignment", DueAt = new DateTimeOffset(2026, 10, 1, 23, 59, 0, TimeSpan.FromHours(-4)).AddDays(day - 1), Status = status, Missing = missing,
        };
        CanvasApi.Item Done(string c, string id, string name, int day) => Item(c, id, name, day, "submitted");
        const string toc = "Theory of Computation", arch = "Computer Architecture", mining = "Data Mining", cap = "Capstone";
        var due = new CanvasApi.DueResponse
        {
            ToHandIn = 24,
            Groups =
            [
                new CanvasApi.Group { Key = "overdue", Label = "Overdue", Items = [Item(toc, "d1", "Asynchronous Lecture 5", 5, "missing", missing: true)] },
                new CanvasApi.Group
                {
                    Key = "week", Label = "This week",
                    Items = [Item(mining, "d2", "Reading quiz 6", 12), Item(toc, "d3", "CCHW2", 14), Item(arch, "d4", "HW3", 14), Item(mining, "d5", "Project proposal", 16), Item(ml, "d6", "CA.02: First Contact", 18)],
                },
                new CanvasApi.Group
                {
                    Key = "later", Label = "Later",
                    Items =
                    [
                        Item(toc, "d7", "CCHW3", 19), Item(arch, "d8", "Lab 4: Pipelining", 21), Item(mining, "d9", "Quiz 4", 22), Item(cap, "d10", "Sprint 3 report", 23),
                        Item(mining, "d11", "Reading quiz 7", 23), Item(toc, "d12", "Proof portfolio draft", 23), Item(ml, "d13", "Midterm Project: 3-Minute Demo Video", 25),
                        Item(toc, "d14", "CCHW4", 26), Item(arch, "d15", "HW4", 28), Item(cap, "d16", "Design review", 30), Item(ml, "d17", "Research Paper Presentation", 72),
                    ],
                },
                new CanvasApi.Group
                {
                    Key = "handed_in", Label = "Handed in",
                    Items = [Done(ml, "h1", "Paper summary", 2), Done(toc, "h2", "CCHW1", 5), Done(arch, "h3", "HW2", 8), Done(cap, "h4", "Sprint 2 report", 9)],
                },
            ],
        };
        OverviewModel.LectureFacts L(string id, string title, string c, int day, double hour, double min, string summary) =>
            new(id, title, c, monday.AddDays(day).AddHours(hour), min * 60, summary, false, monday.AddDays(day).AddHours(hour).ToString("o"));
        var everything = new List<OverviewModel.LectureFacts>
        {
            L("n1", "Transformers, Self-Attention, and LLM Fundamentals", ml, 0, 9.5, 52,
                "Opened the course's block on modern AI: the 2017 transformer paper, why fixed-size networks fall short for language, and dot-product self-attention."),
            L("n2", "Recording, Sat 10 Oct at 4:12 PM", "Unsorted", -2, 16.2, 18, "A planning conversation about the hackathon sponsor list and booking a room for the kickoff."),
            L("n3", "General meeting: Hackathon planning", "ACM", -4, 18, 41, "Officers split up sponsor outreach, picked a November date and set up a sign-up form for teams."),
            L("n4", "CNNs: Pooling, Kernels, AlexNet, and Sketch Classification", ml, -5, 9.5, 50,
                "Continued the unit on convolutional neural networks with pooling, kernel sizes, the AlexNet architecture and a sketch classification demo."),
            L("n5", "Weekly check-in", "NSWC", -6, 14, 24, "Status on the signal-processing pipeline and what the test harness needs before the next review."),
            L("n6", "Intro to CNNs: AlexNet History, Invariance, and 1D Convolution", ml, -7, 9.5, 47,
                "Started the course's unit on convolutional neural networks, from AlexNet's history to translation invariance and 1D convolution."),
        };
        var mlNotes = new List<OverviewModel.LectureFacts>
        {
            L("n1", "Transformers, Self-Attention, and LLM Fundamentals", ml, 0, 9.5, 52, "Opened the course's block on modern AI, starting with transformers."),
            L("n4", "CNNs: Pooling, Kernels, AlexNet, and Sketch Classification", ml, -5, 9.5, 50, "Continued the unit on convolutional neural networks (CNNs)."),
            L("n6", "Intro to CNNs: AlexNet History, Invariance, and 1D Convolution", ml, -7, 9.5, 47, "Started the course's unit on convolutional neural networks (CNNs)."),
            L("n7", "Project Stand-up and Paper Talks: RF-CNN, AI in Music Production, Multimodal Unlearning", ml, -10, 9.5, 49,
                "A Friday stand-up in two parts: project updates, then student paper talks."),
            L("n8", "Backpropagation and Training a Network to Reproduce a Cat Image", ml, -12, 9.5, 51, "Explained backpropagation, the algorithm neural networks use to learn."),
            L("n9", "Deep Learning Fundamentals: Network Depth, Expressivity, and Hyperparameters", ml, -14, 9.5, 50,
                "How adding more layers increases a network’s expressivity and complexity."),
            L("n10", "Research Papers: Epistemic Honesty in LLMs and LoRA Fine-Tuning", ml, -17, 9.5, 46,
                "Two paper talks: epistemic honesty in large language models, and LoRA fine-tuning."),
        };
        var acmNotes = new List<OverviewModel.LectureFacts>
        {
            L("n3", "General meeting: Hackathon planning", "ACM", -4, 18, 41, "Officers split up sponsor outreach, picked a November date and set up a sign-up form for teams."),
            L("n11", "Officer meeting", "ACM", -11, 18, 33, "Budget for the semester, the speaker series shortlist and who runs the next general meeting."),
            L("n12", "General meeting: Welcome back", "ACM", -18, 18, 52, "Introductions, this year’s events and a demo of last spring’s hackathon winner."),
        };
        OverviewModel.Sources Sources(IReadOnlyList<OverviewModel.LectureFacts> notes) => new()
        {
            Now = now, Zone = zone,
            Classes = [.. folders.Select(f => (f.Name, Dot(f.Hue), f.Notes))],
            GroupOf = GroupOf,
            Unsorted = 1,
            Lectures = notes,
            Canvas = [.. new[] { ml, toc, arch, mining, cap }.Select(c => new CanvasApi.ClassRow { Class = c, Linked = true })],
            Due = due,
            Events = events,
        };
        var recorder = new RecorderModel { ClassName = ml, ClassDot = Dot("Blue"), Elapsed = "12:04", Levels = PillWave };
        AiAskModel Ask(string noun, string? folder, params string[] scopes)
        {
            var ask = AiDemo.AskIdle();
            ask.OnlyScopes(scopes);
            ask.NameScopes(noun, folder);
            return ask;
        }
        void Records(OverviewModel o)
        {
            o.OnRecord = () => { };
            if (!recording) return;
            o.Recorder = recorder;
            o.Recording = true;
        }
        if (recording) m.Recording = recorder;
        switch (page)
        {
            case Page.Home or Page.HomeUnsorted:
            {
                bool unsorted = page == Page.HomeUnsorted;
                var home = OverviewModel.Home(Sources(unsorted ? [.. everything.Where(l => l.Class == "Unsorted")] : everything), unsorted);
                Records(home);
                home.OnSupport = () => { };
                home.PickerFor = row => SamplePicker(row.Folder);
                home.Ask = Ask(Nouns.Lecture, null, "all");
                m.Overview = home;
                break;
            }
            case Page.Due or Page.DueWeek:
            {
                var page1 = OverviewModel.ForDue(Sources([]));
                if (page == Page.DueWeek) page1.Calendar!.WeekView = true;
                m.Overview = page1;
                break;
            }
            case Page.FolderClass or Page.FolderClub:
            {
                bool club = page == Page.FolderClub;
                string name = club ? "ACM" : ml;
                foreach (var c in m.Classes) c.Selected = c.Name == name;
                var facts = new ClassHomeFacts(name, DotOf(name), club ? 5 : 14, club ? null : "CSCI 4220", club ? null : "Machine and Deep Learning", null,
                    club ? null : new CanvasApi.Counts { ToHandIn = 3, Done = 5, Modules = 8, Files = 12, Announcements = 6, AnnouncementsNew = 1 }, GroupOf(name));
                var folder = OverviewModel.ForClass(facts, Sources(club ? acmNotes : mlNotes));
                folder.AddLinks(facts, club ? null : _ => { });
                Records(folder);
                folder.Files = new AttachmentsModel(new NoFiles(), name, null);
                folder.Ask = Ask(folder.Noun, name, "class", "all");
                m.Overview = folder;
                break;
            }
            case Page.Note or Page.NoteOnlyMine:
            {
                foreach (var c in m.Classes) c.Selected = c.Name == ml;
                m.Note = new NoteModel
                {
                    Id = "n1", ClassName = ml, Dot = Dot("Blue"), Noun = Nouns.Lecture, When = "Monday 12 October at 9:30 AM, 52 min",
                    Title = "Transformers, Self-Attention, and LLM Fundamentals", Markdown = Merged, OnRename = _ => Task.FromResult(true),
                }.Titled();
                m.Notes = AiDemo.LectureNotes(Merged, "Claude");
                m.Notes.OnlyMine = page == Page.NoteOnlyMine;
                m.LectureFiles = new AttachmentsModel(new NoFiles(), ml, "n1");
                m.Ask = Ask(Nouns.Lecture, ml, "lecture", "class", "all");
                break;
            }
            case Page.Recording:
            {
                foreach (var c in m.Classes) c.Selected = c.Name == ml;
                m.Recording = recorder;
                recorder.Lines.Add(new HeardLine { Time = "10:48", Text = "So the query for this token is compared against the key of every other token.", Start = 648 });
                recorder.Lines.Add(new HeardLine { Time = "11:30", Text = "You take the dot product, and softmax turns those scores into weights.", Start = 690 });
                recorder.Lines.Add(new HeardLine { Time = "12:01", Text = "And those weights are applied to the value vectors.", Start = 721, Latest = true });
                m.Note = new NoteModel
                {
                    Id = "rec-live", ClassName = ml, Dot = Dot("Blue"), Noun = Nouns.Lecture, When = "Monday 12 October at 9:30 AM", Title = "Machine Learning, Mon 12 Oct",
                    Live = true, Recorder = recorder, Typed = Typed.ReplaceLineEndings("\n"), OnRename = _ => Task.FromResult(true),
                }.Titled().Following();
                var ask = AiDemo.AskIdle();
                ask.Live = () => "";
                ask.OnlyScopes("lecture");
                ask.NameScopes(Nouns.Lecture, null);
                m.Ask = ask;
                break;
            }
            case Page.CanvasLists:
            {
                foreach (var c in m.Classes) c.Selected = c.Name == ml;
                m.CanvasOf = ml;
                m.CanvasDot = Dot("Blue");
                break;
            }
        }
        m.Narrow = narrow;
        return m;

        FolderPickerModel SamplePicker(string current) =>
            new(folders.Select(f => (f.Name, f.Group, Dot(f.Hue), f.Notes)), current);
    }

    /// <summary>The sample's folder picker, open on a note in <paramref name="current"/> ("" for Unsorted).</summary>
    public static FolderPickerModel Picker(string current = "Machine Learning")
    {
        (string Name, string Hue, int Notes, string Group)[] folders =
        [
            ("Machine Learning", "Blue", 14, ""), ("Capstone", "Green", 4, ""), ("Computer Architecture", "Violet", 8, ""), ("Data Mining", "Orange", 9, ""),
            ("Theory of Computation", "Pink", 15, ""), ("ACM", "Teal", 5, "Clubs"), ("Intellievent", "Yellow", 2, "Work"), ("NSWC", "Lime", 1, "Work"),
        ];
        return new FolderPickerModel(folders.Select(f => (f.Name, f.Group, (IBrush)Skin.ClassDot(Array.IndexOf(Skin.ClassHues, f.Hue)), f.Notes)), current);
    }

    /// <summary>A library that holds no files and takes none: what a sample page's Attach hangs off.</summary>
    sealed class NoFiles : Core.IAttachmentLibrary
    {
        public Task<System.Text.Json.Nodes.JsonObject?> AttachmentListAsync(string? className, string? lectureId, CancellationToken stop = default) =>
            Task.FromResult<System.Text.Json.Nodes.JsonObject?>(null);
        public Task<System.Text.Json.Nodes.JsonArray> AttachAsync(IEnumerable<string> paths, string? className, string? lectureId, CancellationToken stop = default) =>
            Task.FromResult(new System.Text.Json.Nodes.JsonArray());
        public Task<bool> RemoveAttachmentAsync(string id) => Task.FromResult(false);
        public Task DownloadAttachmentAsync(string id, string path, CancellationToken stop = default) => Task.CompletedTask;
        public Task RewriteAsync(string id) => Task.CompletedTask;
    }

    /// <summary>A term with many classes, their names as a school's catalogue writes them (long, with the section on
    /// the end), none of them with a lecture yet: the sidebar scrolls, names end in "…", and the open folder's name
    /// is too long for one line of its page's title.</summary>
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
        var m = new LibraryModel { Status = "Library running on this PC", DrawChrome = true };
        for (int i = 0; i < names.Length; i++) m.Classes.Add(new ClassItem { Name = names[i], Dot = Skin.ClassDot(i), Count = i == 6 ? 12 : 0, Selected = i == open });
        var now = new DateTimeOffset(2026, 9, 30, 10, 20, 0, TimeSpan.FromHours(-7));
        var sources = new OverviewModel.Sources
        {
            Now = now, Zone = TimeZoneInfo.CreateCustomTimeZone("Demo", TimeSpan.FromHours(-7), "Demo", "Demo"),
            Classes = [.. names.Select((name, i) => (name, (IBrush)Skin.ClassDot(i), i == 6 ? 12 : 0))],
        };
        var page = OverviewModel.ForClass(new ClassHomeFacts(names[open], Skin.ClassDot(open), 0, null, null, null, null), sources);
        page.OnRecord = () => { };
        m.Overview = page;
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
