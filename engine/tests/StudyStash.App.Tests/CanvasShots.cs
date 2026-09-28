using System.Net.Http;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Styling;
using StudyStash.App.Controls;
using StudyStash.App.Services;
using StudyStash.App.ViewModels;
using StudyStash.App.Views;
using StudyStash.App;

namespace StudyStash.App.Tests;

/// <summary>
/// Every Canvas surface (design 06 to 12) drawn the way the design shows it, light and dark, from the same fixtures
/// the client tests read: the ref names exactly (mac-06-canvas-settings, …), plus extras named "&lt;ref&gt;-&lt;what&gt;"
/// that aren't compared. Each shot also walks its control's logical tree and asserts every <see cref="Icon.Glyph"/>
/// is in <see cref="IconPaths.All"/> — a missing icon draws nothing and is easy to miss otherwise.
/// </summary>
public class CanvasShots
{
    static readonly ThemeVariant[] Themes = [ThemeVariant.Light, ThemeVariant.Dark];

    static CanvasShots() => Environment.SetEnvironmentVariable("STUDYSTASH_STILL", "1");

    static void AssertIcons(Control root)
    {
        foreach (var icon in root.GetLogicalDescendants().OfType<Icon>())
        {
            if (icon.Glyph.Length == 0) continue; // e.g. the syncing card, which draws a Spinner instead
            Assert.True(IconPaths.All.ContainsKey(icon.Glyph), $"Icon glyph '{icon.Glyph}' isn't in IconPaths.All");
        }
    }

    static CanvasStatusModel Status(string fixture)
    {
        var m = new CanvasStatusModel(CanvasFixtures.Context());
        m.Show(CanvasFixtures.Load<CanvasApi.State>(fixture));
        return m;
    }

    /// <summary>Design 08's eight cards, in the design's reading order (left column then right, top to bottom).</summary>
    static (string Label, Control Card)[] StateCards(Func<CanvasStatusModel, Control> view) =>
    [
        ("Not set up", view(Status("state-not-set-up"))),
        ("Extension not set up", view(Status("state-no-extension"))),
        ("Chrome not checking in", view(Status("state-chrome-away"))),
        ("Signed out", view(Status("state-signed-out"))),
        ("Syncing", view(Status("state-syncing"))),
        ("Connected", view(Status("state-connected"))),
        ("Extension updated", view(Status("state-updated"))),
        ("Error", view(Status("state-error"))),
    ];

    [AvaloniaFact]
    public void Mac_states()
    {
        foreach (var t in Themes)
        {
            Control? built = null;
            Shot.Take("mac-08-canvas-states", SkinKind.Mac, t, () => built = CanvasFrames.MacStates(StateCards(m => new MacCanvasStatus { DataContext = m })));
            AssertIcons(built!);
        }
    }

    [AvaloniaFact]
    public void Win_states()
    {
        foreach (var t in Themes)
        {
            Control? built = null;
            Shot.Take("win-08-canvas-states", SkinKind.Win, t, () => built = CanvasFrames.WinStates(StateCards(m => new WinCanvasStatus { DataContext = m })));
            AssertIcons(built!);
        }
    }

    /// <summary>The Connected fixtures Settings → Canvas reads for design 06: state, overview, classes, and the
    /// POST routes its buttons hit (never actually called by a shot, but harmless to answer).</summary>
    internal static FakeLibrary ConnectedLibrary(string state = "state-connected") => new FakeLibrary()
        .Json(HttpMethod.Get, "/api/v2/canvas/state", state)
        .Json(HttpMethod.Get, "/api/v2/canvas", "canvas")
        .Json(HttpMethod.Get, "/api/v2/canvas/classes", "classes")
        .Json(HttpMethod.Post, "/api/v2/canvas/courses", "canvas")
        .Json(HttpMethod.Post, "/api/v2/canvas/scout", "canvas")
        .Json(HttpMethod.Post, "/api/v2/canvas", "canvas");

    internal static async Task<CanvasSettingsModel> Settings(string state = "state-connected", string overview = "canvas")
    {
        var model = new CanvasSettingsModel(CanvasFixtures.Context(ConnectedLibrary(state).Json(HttpMethod.Get, "/api/v2/canvas", overview)));
        await model.LoadAsync();
        return model;
    }

    /// <summary>A library whose student chose three of seven courses: this term's in, last term's, a sandbox and chapel
    /// out, each saying why. Made-up courses.</summary>
    internal const string ChosenCourses = """
        {"url": "https://school.instructure.com",
         "courses": {"CS 101": 4201, "BIO 110": 4202, "CALC II": 4203, "HIST 210": 0},
         "chosen": ["4201", "4202", "4203"],
         "available": {"4201": "Intro to Programming", "4202": "Cell and Molecular Biology", "4203": "Calculus II", "4204": "Modern World History",
                       "4206": "Statics", "4207": "Sandbox for Dr. Okafor", "4208": "Chapel and Convocation"},
         "course_info": {
           "4201": {"code": "COMP 101", "name": "Intro to Programming", "term": "Fall 2025", "title": "Intro to Programming", "short_code": "COMP 101", "chosen": true, "suggested": true, "why": "", "class": "CS 101"},
           "4202": {"code": "BIO 110", "name": "Cell and Molecular Biology", "term": "Fall 2025", "title": "Cell and Molecular Biology", "short_code": "BIO 110", "chosen": true, "suggested": true, "why": "", "class": "BIO 110"},
           "4203": {"code": "MATH 142", "name": "Calculus II", "term": "Fall 2025", "title": "Calculus II", "short_code": "MATH 142", "chosen": true, "suggested": true, "why": "", "class": "CALC II"},
           "4204": {"code": "HIST 210", "name": "Modern World History", "term": "Fall 2025", "title": "Modern World History", "short_code": "HIST 210", "chosen": false, "suggested": true, "why": "", "class": null},
           "4206": {"code": "MECH2010.A", "name": "Statics(MECH2010.A)", "term": "Spring 2025", "title": "Statics", "short_code": "MECH 2010", "chosen": false, "suggested": false, "why": "Past term", "class": null},
           "4207": {"code": "SBX", "name": "Sandbox for Dr. Okafor", "term": "Default Term", "title": "Sandbox for Dr. Okafor", "short_code": "", "chosen": false, "suggested": false, "why": "Not a class", "class": null},
           "4208": {"code": "GEN0100.A", "name": "Chapel and Convocation(GEN0100.A)", "term": "Fall 2025", "title": "Chapel and Convocation", "short_code": "GEN 0100", "chosen": false, "suggested": false, "why": "Not a class", "class": null}}}
        """;

    /// <summary>Settings → Canvas with the course picker: as saved, and with a change waiting (one course added, one
    /// dropped: Keep them / Remove them).</summary>
    internal static async Task<(CanvasSettingsModel Saved, CanvasSettingsModel Changed)> PickerSettingsAsync()
    {
        var saved = await Settings(overview: ChosenCourses);
        var changed = await Settings(overview: ChosenCourses);
        changed.Picker.Courses.First(c => c.Id == "4204").Ticked = true;
        changed.Picker.Courses.First(c => c.Id == "4203").Ticked = false;
        return (saved, changed);
    }

    [AvaloniaFact]
    public async Task Mac_course_picker()
    {
        var (saved, changed) = await PickerSettingsAsync();
        foreach (var t in Themes)
        {
            Shot.Take("mac-06-canvas-courses", SkinKind.Mac, t, () => CanvasFrames.MacSettings(new MacCanvasSettings { DataContext = saved }));
            Shot.Take("mac-06-canvas-courses-changed", SkinKind.Mac, t, () => CanvasFrames.MacSettings(new MacCanvasSettings { DataContext = changed }));
        }
        var connect = await CoursesStepAsync();
        foreach (var t in Themes)
            Shot.Take("mac-07-canvas-connect-courses", SkinKind.Mac, t, () => CanvasFrames.MacSetup(new MacCanvasConnect { DataContext = connect }));
    }

    [AvaloniaFact]
    public async Task Win_course_picker()
    {
        var (saved, changed) = await PickerSettingsAsync();
        foreach (var t in Themes)
        {
            Shot.Take("win-06-canvas-courses", SkinKind.Win, t, () => CanvasFrames.WinSettings(new WinCanvasSettings { DataContext = saved }));
            Shot.Take("win-06-canvas-courses-changed", SkinKind.Win, t, () => CanvasFrames.WinSettings(new WinCanvasSettings { DataContext = changed }));
        }
        var connect = await CoursesStepAsync();
        foreach (var t in Themes)
            Shot.Take("win-07-canvas-connect-courses", SkinKind.Win, t, () => CanvasFrames.WinSetup(new WinCanvasConnect { DataContext = connect }));
    }

    [AvaloniaFact]
    public async Task Mac_settings()
    {
        var model = await Settings();
        foreach (var t in Themes)
        {
            Control? built = null;
            Shot.Take("mac-06-canvas-settings", SkinKind.Mac, t, () => built = CanvasFrames.MacSettings(new MacCanvasSettings { DataContext = model }));
            AssertIcons(built!);
        }
        var signedOut = await Settings("state-signed-out");
        foreach (var t in Themes)
            Shot.Take("mac-06-canvas-settings-signed-out", SkinKind.Mac, t, () => CanvasFrames.MacSettings(new MacCanvasSettings { DataContext = signedOut }));
    }

    [AvaloniaFact]
    public async Task Win_settings()
    {
        var model = await Settings();
        foreach (var t in Themes)
        {
            Control? built = null;
            Shot.Take("win-06-canvas-settings", SkinKind.Win, t, () => built = CanvasFrames.WinSettings(new WinCanvasSettings { DataContext = model }));
            AssertIcons(built!);
        }
        var signedOut = await Settings("state-signed-out");
        foreach (var t in Themes)
            Shot.Take("win-06-canvas-settings-signed-out", SkinKind.Win, t, () => CanvasFrames.WinSettings(new WinCanvasSettings { DataContext = signedOut }));
    }

    // ---- design 09: the Due list and an assignment ----

    internal static async Task<CanvasDueModel> DueModel()
    {
        var model = new CanvasDueModel(CanvasFixtures.Context(new FakeLibrary().Json(HttpMethod.Get, "/api/v2/canvas/due", "due")));
        await model.LoadAsync();
        return model;
    }

    internal static AssignmentModel Detail(string fixture)
    {
        var m = new AssignmentModel(CanvasFixtures.Context());
        m.Show(CanvasFixtures.Load<CanvasApi.AssignmentDetail>(fixture));
        return m;
    }

    [AvaloniaFact]
    public async Task Mac_due()
    {
        var due = await DueModel();
        due.SelectItem("CS 101", "9001");
        var lab3 = Detail("assignment-9001");
        foreach (var t in Themes)
        {
            Control? built = null;
            Shot.Take("mac-09-canvas-due", SkinKind.Mac, t,
                () => built = CanvasFrames.MacApp("Due", new MacCanvasDue { DataContext = due }, new MacAssignment { DataContext = lab3 }));
            AssertIcons(built!);
        }

        due.SelectItem("CS 101", "9002");
        var ps4 = Detail("assignment-9002");
        foreach (var t in Themes)
            Shot.Take("mac-09-canvas-due-graded", SkinKind.Mac, t,
                () => CanvasFrames.MacApp("Due", new MacCanvasDue { DataContext = due }, new MacAssignment { DataContext = ps4 }));
    }

    [AvaloniaFact]
    public async Task Win_due()
    {
        var due = await DueModel();
        due.SelectItem("CS 101", "9001");
        var lab3 = Detail("assignment-9001");
        foreach (var t in Themes)
        {
            Control? built = null;
            Shot.Take("win-09-canvas-due", SkinKind.Win, t,
                () => built = CanvasFrames.WinApp("Due", new WinCanvasDue { DataContext = due }, new WinAssignment { DataContext = lab3 }));
            AssertIcons(built!);
        }

        due.SelectItem("CS 101", "9002");
        var ps4 = Detail("assignment-9002");
        foreach (var t in Themes)
            Shot.Take("win-09-canvas-due-graded", SkinKind.Win, t,
                () => CanvasFrames.WinApp("Due", new WinCanvasDue { DataContext = due }, new WinAssignment { DataContext = ps4 }));
    }

    // ---- design 10/11: a Canvas-linked class's own page ----

    /// <summary>The three newest lectures the shell would already have for CS 101, their days worded the way
    /// <see cref="CanvasWords.ShortDay"/> does it.</summary>
    static readonly (string Title, DateTimeOffset At)[] Cs101Lectures =
    [
        ("Recursion and the call stack", new DateTimeOffset(2025, 9, 23, 20, 0, 0, TimeSpan.Zero)),
        ("Stack frames and scope", new DateTimeOffset(2025, 9, 18, 20, 0, 0, TimeSpan.Zero)),
        ("Functions as values", new DateTimeOffset(2025, 9, 16, 20, 0, 0, TimeSpan.Zero)),
    ];

    internal static CanvasClassModel ClassModel()
    {
        var model = new CanvasClassModel(CanvasFixtures.Context());
        var cs101 = CanvasFixtures.Load<List<CanvasApi.ClassRow>>("classes").Single(c => c.Class == "CS 101");
        model.Show(cs101,
            CanvasFixtures.Load<CanvasApi.AssignmentsResponse>("assignments-cs101"),
            CanvasFixtures.Load<CanvasApi.ModulesResponse>("modules-cs101"),
            CanvasFixtures.Load<CanvasApi.FilesResponse>("files-cs101"),
            CanvasFixtures.Load<CanvasApi.AnnouncementsResponse>("announcements-cs101"));
        model.SetLectures(Cs101Lectures.Select(l => new LectureRow(l.Title, CanvasWords.ShortDay(l.At, CanvasFixtures.Zone), () => { })).ToList(), 12);
        return model;
    }

    [AvaloniaFact]
    public void Mac_class_tabs()
    {
        var cls = ClassModel();
        cls.Tab = ClassTab.Assignments;
        cls.Done[0].SelectCommand.Execute(null); // Problem set 4
        var ps4 = Detail("assignment-9002");
        foreach (var t in Themes)
        {
            Control? built = null;
            Shot.Take("mac-10-canvas-class-tabs", SkinKind.Mac, t,
                () => built = CanvasFrames.MacApp("CS 101", new MacCanvasClass { DataContext = cls }, new MacAssignment { DataContext = ps4 }));
            AssertIcons(built!);
        }

        // extras, not compared: the Modules and Announcements tabs
        var modulesCls = ClassModel();
        modulesCls.Tab = ClassTab.Modules;
        foreach (var t in Themes)
            Shot.Take("mac-10-canvas-class-tabs-modules", SkinKind.Mac, t,
                () => CanvasFrames.MacApp("CS 101", new MacCanvasClass { DataContext = modulesCls }, new MacAssignment { DataContext = ps4 }));

        var announceCls = ClassModel();
        announceCls.Tab = ClassTab.Announcements;
        CanvasReaderModel? reader = null;
        announceCls.OnReader = r => reader = r;
        announceCls.Announcements[0].OpenCommand.Execute(null);
        foreach (var t in Themes)
            Shot.Take("mac-10-canvas-class-announcement", SkinKind.Mac, t,
                () => CanvasFrames.MacApp("CS 101", new MacCanvasClass { DataContext = announceCls }, new MacCanvasReader { DataContext = reader }));
    }

    [AvaloniaFact]
    public void Win_class_tabs()
    {
        var cls = ClassModel();
        cls.Tab = ClassTab.Assignments;
        cls.Done[0].SelectCommand.Execute(null);
        var ps4 = Detail("assignment-9002");
        foreach (var t in Themes)
        {
            Control? built = null;
            Shot.Take("win-10-canvas-class-tabs", SkinKind.Win, t,
                () => built = CanvasFrames.WinApp("CS 101", new WinCanvasClass { DataContext = cls }, new WinAssignment { DataContext = ps4 }, listWidth: 360));
            AssertIcons(built!);
        }
    }

    [AvaloniaFact]
    public void Mac_class_sections()
    {
        var cls = ClassModel();
        cls.Layout = ClassLayout.Sections;
        foreach (var t in Themes)
        {
            Control? built = null;
            Shot.Take("mac-11-canvas-class-sections", SkinKind.Mac, t,
                () => built = CanvasFrames.MacApp("CS 101", new MacCanvasClass { DataContext = cls }, width: 588));
            AssertIcons(built!);
        }
    }

    [AvaloniaFact]
    public void Win_class_sections()
    {
        var cls = ClassModel();
        cls.Layout = ClassLayout.Sections;
        foreach (var t in Themes)
        {
            Control? built = null;
            Shot.Take("win-11-canvas-class-sections", SkinKind.Win, t,
                () => built = CanvasFrames.WinApp("CS 101", new WinCanvasClass { DataContext = cls }, width: 640));
            AssertIcons(built!);
        }
    }

    // ---- design 07: connecting Canvas ----

    static CanvasConnectModel Connect(FakeLibrary handler) =>
        new(CanvasFixtures.Context(handler), new CanvasWatch(CanvasFixtures.Context(handler)));

    internal static async Task<CanvasConnectModel> Step2Async()
    {
        var handler = new FakeLibrary()
            .Json(HttpMethod.Get, "/api/v2/canvas/state", "state-no-extension")
            .Json(HttpMethod.Get, "/api/v2/canvas/extension", "extension");
        var m = Connect(handler);
        m.StepLabel = "Step 5 of 5 · Optional";
        m.ShowFooter = true;
        await m.StartAsync(CanvasFixtures.Load<CanvasApi.State>("state-no-extension"), []);
        return m;
    }

    /// <summary>Found my courses, as a library sends them (course_info keyed by id): four of this term's, and a past
    /// term's course and a sandbox the picker leaves unticked.</summary>
    internal const string FoundCourses = """
        {"url": "https://school.instructure.com",
         "available": {"4201": "Intro to Programming", "4202": "Cell and Molecular Biology", "4203": "Calculus II", "4204": "Modern World History",
                       "4206": "Statics", "4207": "Sandbox for Dr. Okafor"},
         "course_info": {"4201": {"code": "CS 101", "name": "Intro to Programming", "term": "Fall 2025", "suggested": true},
                         "4202": {"code": "BIO 110", "name": "Cell and Molecular Biology", "term": "Fall 2025", "suggested": true},
                         "4203": {"code": "CALC II", "name": "Calculus II", "term": "Fall 2025", "suggested": true},
                         "4204": {"code": "HIST 210", "name": "Modern World History", "term": "Fall 2025", "suggested": true},
                         "4206": {"code": "MECH2010.A", "name": "Statics(MECH2010.A)", "term": "Spring 2025", "title": "Statics", "short_code": "MECH 2010", "suggested": false, "why": "Past term"},
                         "4207": {"code": "SBX", "name": "Sandbox for Dr. Okafor", "term": "Default Term", "suggested": false, "why": "Not a class"}}}
        """;

    /// <summary>Settings' connect window on Your courses: the courses found, ticked, and Bring in in the step's row.</summary>
    internal static async Task<CanvasConnectModel> CoursesStepAsync()
    {
        var unmatched = new List<CanvasApi.ClassRow> { new() { Class = "CS 101", Linked = false, Suggested = "4201" } };
        var handler = new FakeLibrary()
            .Json(HttpMethod.Get, "/api/v2/canvas/state", "state-connected")
            .Json(HttpMethod.Post, "/api/v2/canvas/courses", FoundCourses);
        var m = Connect(handler);
        m.ShowFooter = true;
        await m.StartAsync(CanvasFixtures.Load<CanvasApi.State>("state-connected"), unmatched);
        m.Dispose();
        return m;
    }

    /// <summary>Setup's Canvas step: "chrome" (Add to Chrome, not pressed yet), "waiting" (pressed: the folder and
    /// Chrome are open), or "found" (Chrome connected and five courses found).</summary>
    internal static async Task<CanvasConnectModel> SetupStepAsync(string at)
    {
        var handler = new FakeLibrary()
            .Json(HttpMethod.Get, "/api/v2/canvas/state", at == "found" ? "state-connected" : "state-no-extension")
            .Json(HttpMethod.Get, "/api/v2/canvas", """{"url": "https://school.instructure.com", "extension_seen": ""}""")
            .Json(HttpMethod.Get, "/api/v2/canvas/extension", "extension")
            .Json(HttpMethod.Post, "/api/v2/canvas/courses", FoundCourses);
        var context = CanvasFixtures.Context(handler, "the-home");
        var m = new CanvasConnectModel(context, new CanvasWatch(context), forSetup: true);
        await m.StartAsync(CanvasFixtures.Load<CanvasApi.State>(at == "found" ? "state-connected" : "state-no-extension"), []);
        if (at == "waiting") await m.AddToChromeCommand.ExecuteAsync(null);
        m.Dispose(); // a still picture: the watch needn't keep asking
        return m;
    }

    static async Task<CanvasConnectModel> SchoolStepAsync()
    {
        var m = Connect(new FakeLibrary());
        m.ShowFooter = true;
        await m.StartAsync(CanvasFixtures.Load<CanvasApi.State>("state-not-set-up"), []);
        return m;
    }

    static async Task<CanvasConnectModel> SignedOutStepAsync()
    {
        var m = Connect(new FakeLibrary());
        m.ShowFooter = true;
        await m.StartAsync(CanvasFixtures.Load<CanvasApi.State>("state-signed-out"), []);
        return m;
    }

    static async Task<CanvasConnectModel> MatchStepAsync()
    {
        var unmatched = new List<CanvasApi.ClassRow>
        {
            new() { Class = "CS 101", Linked = false, Suggested = "4201" },
            new() { Class = "BIO 110", Linked = false, Suggested = "4202" },
        };
        var handler = new FakeLibrary()
            .Json(HttpMethod.Get, "/api/v2/canvas/state", "state-connected")
            .Json(HttpMethod.Post, "/api/v2/canvas/courses", "canvas")
            .Json(HttpMethod.Post, "/api/v2/canvas/choose", "canvas")
            .Json(HttpMethod.Get, "/api/v2/canvas/classes", JsonSerializer.Serialize(unmatched, CanvasApi.Json));
        var m = Connect(handler);
        m.ShowFooter = true;
        await m.StartAsync(CanvasFixtures.Load<CanvasApi.State>("state-connected"), unmatched); // finds courses on its own
        await m.BringInCommand.ExecuteAsync(null); // then on to matching
        return m;
    }

    static async Task<CanvasConnectModel> SyncingStepAsync()
    {
        var handler = new FakeLibrary()
            .Json(HttpMethod.Get, "/api/v2/canvas/state", "state-syncing")
            .Json(HttpMethod.Post, "/api/v2/canvas", "canvas");
        var m = Connect(handler);
        m.ShowFooter = true;
        await m.StartAsync(CanvasFixtures.Load<CanvasApi.State>("state-connected"), [new() { Class = "CS 101", Linked = true, Canvas = new() { Id = "4201" } }]);
        await m.SyncNowCommand.ExecuteAsync(null);
        m.Syncing = true;
        m.SyncProgress = 0.4;
        return m;
    }

    [AvaloniaFact]
    public async Task Mac_connect()
    {
        foreach (var t in Themes)
        {
            var m = await Step2Async();
            Control? built = null;
            Shot.Take("mac-07-canvas-connect", SkinKind.Mac, t, () => built = CanvasFrames.MacSetup(new MacCanvasConnect { DataContext = m }));
            AssertIcons(built!);
        }
        var school = await SchoolStepAsync();
        var signedOut = await SignedOutStepAsync();
        var match = await MatchStepAsync();
        var syncing = await SyncingStepAsync();
        Shot.Take("mac-07-canvas-connect-school", SkinKind.Mac, ThemeVariant.Light, () => CanvasFrames.MacSetup(new MacCanvasConnect { DataContext = school }));
        Shot.Take("mac-07-canvas-connect-signed-out", SkinKind.Mac, ThemeVariant.Light, () => CanvasFrames.MacSetup(new MacCanvasConnect { DataContext = signedOut }));
        Shot.Take("mac-07-canvas-connect-match", SkinKind.Mac, ThemeVariant.Light, () => CanvasFrames.MacSetup(new MacCanvasConnect { DataContext = match }));
        Shot.Take("mac-07-canvas-connect-syncing", SkinKind.Mac, ThemeVariant.Light, () => CanvasFrames.MacSetup(new MacCanvasConnect { DataContext = syncing }));
    }

    [AvaloniaFact]
    public async Task Mac_connect_setup()
    {
        foreach (var at in new[] { "chrome", "waiting", "found" })
        {
            var m = await SetupStepAsync(at);
            foreach (var t in Themes)
            {
                Control? built = null;
                Shot.Take($"mac-07-canvas-connect-setup-{at}", SkinKind.Mac, t, () => built = CanvasFrames.MacSetup(new MacCanvasConnect { DataContext = m }));
                AssertIcons(built!);
            }
        }
    }

    [AvaloniaFact]
    public async Task Win_connect_setup()
    {
        foreach (var at in new[] { "chrome", "waiting", "found" })
        {
            var m = await SetupStepAsync(at);
            foreach (var t in Themes)
            {
                Control? built = null;
                Shot.Take($"win-07-canvas-connect-setup-{at}", SkinKind.Win, t, () => built = CanvasFrames.WinSetup(new WinCanvasConnect { DataContext = m }));
                AssertIcons(built!);
            }
        }
    }

    [AvaloniaFact]
    public async Task Win_connect()
    {
        foreach (var t in Themes)
        {
            var m = await Step2Async();
            Control? built = null;
            Shot.Take("win-07-canvas-connect", SkinKind.Win, t, () => built = CanvasFrames.WinSetup(new WinCanvasConnect { DataContext = m }));
            AssertIcons(built!);
        }
        var school = await SchoolStepAsync();
        var signedOut = await SignedOutStepAsync();
        var match = await MatchStepAsync();
        var syncing = await SyncingStepAsync();
        Shot.Take("win-07-canvas-connect-school", SkinKind.Win, ThemeVariant.Light, () => CanvasFrames.WinSetup(new WinCanvasConnect { DataContext = school }));
        Shot.Take("win-07-canvas-connect-signed-out", SkinKind.Win, ThemeVariant.Light, () => CanvasFrames.WinSetup(new WinCanvasConnect { DataContext = signedOut }));
        Shot.Take("win-07-canvas-connect-match", SkinKind.Win, ThemeVariant.Light, () => CanvasFrames.WinSetup(new WinCanvasConnect { DataContext = match }));
        Shot.Take("win-07-canvas-connect-syncing", SkinKind.Win, ThemeVariant.Light, () => CanvasFrames.WinSetup(new WinCanvasConnect { DataContext = syncing }));
    }

    // ---- design 12: next due, the quick panel's Canvas rows, and a notification toast ----

    static CanvasApi.NotificationRow Notification(long id, string kind, string title, string text) =>
        new() { Id = id, Kind = kind, Title = title, Text = text, At = CanvasFixtures.Now };

    /// <summary>The design's own three example toasts: the first expanded with its buttons, the rest collapsed —
    /// exactly what the gallery in "Canvas Quick.html" draws, not a live poll's own ordering (see
    /// <see cref="CanvasNotifier"/> for that).</summary>
    internal static CanvasToastModel[] ToastGallery() =>
    [
        new(Notification(1, "new_assignment", "New assignment", "CS 101 · Lab 3 · due Tue 11:59 PM")) { When = "now", Expanded = true },
        new(Notification(2, "due_moved", "Due date moved", "CALC II · Quiz 3 practice · now Fri 9:00 AM")) { When = "now" },
        new(Notification(3, "new_score", "New score", "CS 101 · Problem set 4 · 18/20")) { When = "now" },
    ];

    static readonly string[] DotClasses = ["CS 101", "BIO 110", "CALC II", "HIST 210"];

    static IBrush DotOf(string cls) => Skin.ClassDot(Array.IndexOf(DotClasses, cls) is var i && i >= 0 ? i : 0);

    internal static QuickModel QuickWithDue()
    {
        var due = CanvasFixtures.Load<CanvasApi.DueResponse>("due");
        var state = CanvasFixtures.Load<CanvasApi.State>("state-connected");
        var q = new QuickModel { Query = "due" };
        foreach (var row in CanvasQuick.Rows(due, state, "due", CanvasFixtures.Zone, CanvasFixtures.Now, DotOf, _ => { }, () => { }, () => { }))
            q.Rows.Add(row);
        q.SelectFirst();
        return q;
    }

    [AvaloniaFact]
    public void Mac_dropdown_quick_notify()
    {
        var due = CanvasFixtures.Load<CanvasApi.DueResponse>("due");
        foreach (var t in Themes)
        {
            Control? built = null;
            Shot.Take("mac-12-canvas-dropdown-quick-notify", SkinKind.Mac, t, () =>
            {
                var nextDue = CanvasQuick.NextDue(due, CanvasFixtures.Zone, CanvasFixtures.Now, _ => { })!;
                var toasts = new StackPanel { Spacing = 10, VerticalAlignment = VerticalAlignment.Top };
                foreach (var toast in ToastGallery()) toasts.Children.Add(ToastView.For(toast));
                return built = Shot.Side(
                    CanvasFrames.MacDropdownLine(new MacNextDue { DataContext = nextDue }),
                    new MacQuick { DataContext = QuickWithDue() },
                    toasts);
            });
            AssertIcons(built!);
        }
    }

    [AvaloniaFact]
    public void Win_dropdown_quick_notify()
    {
        var due = CanvasFixtures.Load<CanvasApi.DueResponse>("due");
        foreach (var t in Themes)
        {
            Control? built = null;
            Shot.Take("win-12-canvas-dropdown-quick-notify", SkinKind.Win, t, () =>
            {
                var nextDue = CanvasQuick.NextDue(due, CanvasFixtures.Zone, CanvasFixtures.Now, _ => { })!;
                var toasts = new StackPanel { Spacing = 10, VerticalAlignment = VerticalAlignment.Top };
                foreach (var toast in ToastGallery()) toasts.Children.Add(ToastView.For(toast));
                return built = Shot.Side(
                    CanvasFrames.WinDropdownLine(new WinNextDue { DataContext = nextDue }),
                    new WinQuick { DataContext = QuickWithDue() },
                    toasts);
            });
            AssertIcons(built!);
        }
    }
}
