using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using Avalonia.Styling;
using Avalonia.Threading;
using SkiaSharp;
using StudyStash.App.Controls;
using StudyStash.App.Services;
using StudyStash.App.ViewModels;
using StudyStash.App.Views;
using StudyStash.Core;

using Avalonia.Input;
using Avalonia.VisualTree;
using Avalonia.Controls.Primitives;
using StudyStash.App.Controls.Rich;
using StudyStash.App.Platform;
using StudyStash.Core.Rich;
using StudyStash.Core.Setup;
using System.Net.Http;

namespace StudyStash.App.Tests;

/// <summary>Demo screens and models that real tests (and the pictures) build from: kept here when the picture tests that
/// first made them went away.</summary>
public static partial class Scenes
{
    const string CanvasSynced = "Library connected · Canvas synced 10:24";
    static readonly string Home = Path.Combine(Path.GetTempPath(), "studystash-app-tests", "guided-" + Environment.ProcessId);

    /// <summary>The Connected fixtures Settings → Canvas reads for design 06: state, overview, classes, and the
    /// POST routes its buttons hit (never actually called by a shot, but harmless to answer).</summary>
    internal static FakeLibrary ConnectedLibrary(string state = "state-connected") => new FakeLibrary()
        .Json(HttpMethod.Get, "/api/v2/canvas/state", state)
        .Json(HttpMethod.Get, "/api/v2/canvas", "canvas")
        .Json(HttpMethod.Get, "/api/v2/canvas/classes", "classes")
        .Json(HttpMethod.Post, "/api/v2/canvas/courses", "canvas")
        .Json(HttpMethod.Post, "/api/v2/canvas/scout", "canvas")
        .Json(HttpMethod.Post, "/api/v2/canvas", "canvas");

    /// <summary>Setup's Canvas step: "chrome" (Add to Chrome, not pressed yet: the Chrome Web Store's three steps and
    /// the code), "waiting" (pressed: the store's page is open in Chrome), "folder" (the store wouldn't do, so the
    /// student chose the folder: its three pictures), "folder-waiting" (Add to Chrome pressed for the folder: it and
    /// Chrome's extensions page are open; on a <paramref name="mac"/>, the folder Chrome's picker shows there), or
    /// "found" (Chrome connected and five courses found). On a computer with other browsers: "edge" (Edge is the one
    /// to use, with a quiet way to another; its store step says Get extension), "edge-waiting" (Add to Edge pressed),
    /// "opera" (Opera loads the folder), "firefox" (Firefox's three steps and its code) and "firefox-waiting" (Add to
    /// Firefox pressed). For a student who uses Safari: "safari" (Chrome is here, and is used instead) and "no-browser"
    /// (nothing here will do: get Chrome). For one who uses Firefox before Syft Notes knows where its add-on is:
    /// "firefox-not-yet" (Chrome is used instead, or they say they have the add-on already) and "firefox-have-it"
    /// (they said so: its code, and nothing to add).</summary>
    internal static async Task<CanvasConnectModel> SetupStepAsync(string at, bool mac = false)
    {
        var handler = new FakeLibrary()
            .Json(HttpMethod.Get, "/api/v2/canvas/state", at == "found" ? "state-connected" : "state-no-extension")
            .Json(HttpMethod.Get, "/api/v2/canvas", """{"url": "https://school.instructure.com", "extension_seen": ""}""")
            // A key as long as a real one, so the code Firefox pastes is as long as a student's.
            .Json(HttpMethod.Get, "/api/v2/canvas/extension", """{"key": "q1W2e3R4t5Y6u7I8o9P0a1S2d3F4g5H6", "canvas": "https://school.instructure.com", "version": "1.6"}""")
            .Json(HttpMethod.Post, "/api/v2/canvas/courses", FoundCourses);
        IReadOnlyList<Browser>? browsers = at switch
        {
            "edge" or "edge-waiting" => [Browsers.Edge, Browsers.Chrome, Browsers.Firefox],
            "opera" => [Browsers.Opera],
            "firefox" or "firefox-waiting" => [Browsers.Firefox, Browsers.Chrome],
            "safari" or "firefox-not-yet" or "firefox-have-it" => [Browsers.Chrome, Browsers.Edge],
            _ => null,
        };
        var advice = at is "safari" or "no-browser" ? new BrowserAdvice(Browsers.Safari, NoneHere: at == "no-browser")
            : at is "firefox-not-yet" or "firefox-have-it" ? new BrowserAdvice(Browsers.FirefoxDeveloper, NoneHere: false) : null;
        var context = CanvasFixtures.Context(handler, "the-home", browsers: browsers, advice: advice);
        var m = new CanvasConnectModel(context, new CanvasWatch(context), forSetup: true);
        await m.StartAsync(CanvasFixtures.Load<CanvasApi.State>(at == "found" ? "state-connected" : "state-no-extension"), []);
        if (at is "folder" or "folder-waiting") m.OtherWayCommand.Execute(null);
        if (at is "waiting" or "edge-waiting" or "firefox-waiting" or "folder-waiting") await m.AddToBrowserCommand.ExecuteAsync(null);
        if (at == "firefox-have-it") await m.HaveItCommand.ExecuteAsync(null);
        if (at == "folder-waiting" && mac) m.ExtensionFolder = Path.Combine("~", "Syft Notes", "Chrome extension");
        for (int i = 0; i < 100 && m.FromStore && m.ConnectionCode.Length == 0; i++) await Task.Delay(10); // the code is asked for as the step opens
        m.Dispose(); // a still picture: the watch needn't keep asking
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

    /// <summary>The design's own three example toasts: the first expanded with its buttons, the rest collapsed —
    /// exactly what the gallery in "Canvas Quick.html" draws, not a live poll's own ordering (see
    /// <see cref="CanvasNotifier"/> for that).</summary>
    internal static CanvasToastModel[] ToastGallery() =>
    [
        new(Notification(1, "new_assignment", "New assignment", "CS 101 · Lab 3 · due Tue 11:59 PM")) { When = "now", Expanded = true },
        new(Notification(2, "due_moved", "Due date moved", "CALC II · Quiz 3 practice · now Fri 9:00 AM")) { When = "now" },
        new(Notification(3, "new_score", "New score", "CS 101 · Problem set 4 · 18/20")) { When = "now" },
    ];

    static CanvasApi.NotificationRow Notification(long id, string kind, string title, string text) =>
        new() { Id = id, Kind = kind, Title = title, Text = text, At = CanvasFixtures.Now };

    /// <summary>The dropdown's class picker as Shell builds it, CS 101 picked as in the dropdown beside it (drawn in
    /// place here: a real one is a popup window).</summary>
    internal static ContextMenu ClassMenu()
    {
        var menu = ClassPicker.Build([("CS 101", 0), ("BIO 110", 1), ("CALC II", 2), ("HIST 210", 3)], "CS 101", _ => { });
        menu.VerticalAlignment = VerticalAlignment.Top;
        return menu;
    }

    /// <summary>A class's lists on Canvas: the list on the left, the assignment picked from it beside it.</summary>
    internal static LibraryModel ClassApp(bool narrow, ClassTab tab = ClassTab.Assignments)
    {
        var m = Sidebar("CS 101");
        var cls = ClassModel();
        cls.Tab = tab;
        cls.Done[0].SelectCommand.Execute(null); // Problem set 4
        m.CanvasOf = "CS 101";
        m.CanvasDot = Skin.ClassDot(0);
        m.CanvasClass = cls;
        m.Narrow = narrow;
        if (!narrow) m.Assignment = Detail("assignment-9002");
        return m;
    }

    /// <summary>An assignment on a page of its own, as Due's calendar opens it.</summary>
    internal static LibraryModel DueApp()
    {
        var m = Sidebar("CS 101");
        m.Assignment = Detail("assignment-9001");
        return m;
    }

    /// <summary>Design 04's lecture, its notes now under "Summary · Written by Ollama" with "Rewrite notes", and the
    /// ask bar picking its engine.</summary>
    internal static LibraryModel LectureApp()
    {
        var m = Demo.Library();
        m.Notes = AiDemo.LectureNotes(Demo.Notes);
        m.Ask = AiDemo.AskIdle();
        return m;
    }

    /// <summary>Library setup at the AI engines (design 15).</summary>
    internal static SetupModel SetupAi(SkinKind skin)
    {
        var m = SetupModel.For(skin, AppRole.Library);
        m.Ai = AiDemo.Setup();
        m.Go(SetupStep.Ai);
        return m;
    }

    /// <summary>A note on the library's page, in the look's notes column.</summary>
    internal static Control NotePage(SkinKind skin, string markdown, Action<NoteView>? after = null)
    {
        bool mac = skin == SkinKind.Mac;
        var note = new NoteView { Markdown = markdown, Width = mac ? 620 : 640, HorizontalAlignment = HorizontalAlignment.Left };
        after?.Invoke(note);
        var page = new Border { Padding = mac ? new Thickness(64, 28) : new Thickness(56, 24), Child = note };
        page.Bind(Border.BackgroundProperty, page.GetResourceObservable(mac ? "Win" : "Layer"));
        if (mac) return page;
        var mica = new Border { Child = page };
        mica.Bind(Border.BackgroundProperty, mica.GetResourceObservable("Mica"));
        return mica;
    }

    /// <summary>The sidebar and the cardiac-cycle lecture's page.</summary>
    internal static LibraryModel CardiacLibrary()
    {
        var m = new LibraryModel { Status = "Library connected", DrawChrome = true, UnsortedCount = 1 };
        m.Classes.Add(new ClassItem { Name = "CS 101", Dot = Skin.ClassDot(0), Count = 12 });
        m.Classes.Add(new ClassItem { Name = RichDemo.LectureClassName, Dot = BioDot, Count = 9, Selected = true });
        m.Classes.Add(new ClassItem { Name = "CALC II", Dot = Skin.ClassDot(2), Count = 11 });
        m.Note = new NoteModel { ClassName = RichDemo.LectureClassName, Dot = BioDot, When = RichDemo.LectureWhen, Title = RichDemo.LectureTitle, Markdown = RichDemo.CardiacLecture }.Titled();
        var ask = AiDemo.AskIdle();
        ask.ClassName = RichDemo.LectureClassName;
        m.Ask = ask;
        return m;
    }

    /// <summary>Scrolls the notes column so a diagram shows a little below the top, the way someone reading the
    /// lecture would have scrolled to it.</summary>
    internal static void ScrollToDiagrams(Window window)
    {
        DiagramsReady.Wait(window);
        var scroller = window.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault(s => s.GetVisualDescendants().OfType<NoteView>().Any());
        var diagram = window.GetVisualDescendants().FirstOrDefault(v => v is DiagramView or SvgView) as Visual;
        if (scroller is null || diagram is null) return;
        var top = diagram.TranslatePoint(new Point(0, 0), scroller)?.Y ?? 0;
        scroller.Offset = new Vector(0, Math.Max(0, scroller.Offset.Y + top - 40));
        Dispatcher.UIThread.RunJobs();
    }

    static IBrush BioDot => Skin.ClassDot(1);

    /// <summary>A machine learning lecture's notes with the plots the diagram pass would add.</summary>
    internal static string MlLecture => "## Summary\n\nLogistic regression squashes a linear score into a probability with the sigmoid, and is fitted by gradient descent.\n\n"
        + "## Key points\n\n- The sigmoid turns a score into a probability:\n\n$$\\sigma(z) = \\frac{1}{1 + e^{-kz}}$$\n\n"
        + "**The sigmoid squashes a score into a probability**\n\n```plot\n%% Syft Notes diagram, from 12:40\n" + string.Join("\n", PlotDesign.Sigmoid.Split('\n').Skip(1)) + "\ntangent σ at 0 \"slope {slope}\"\n```\n\n"
        + "*σ goes to 0 for very negative scores and 1 for very positive ones, crossing one half at 0. Raise k and the S becomes a step.* (from 12:40)\n\n"
        + "## Gradient descent\n\n- Each step moves the weights against the gradient, scaled by the learning rate η.\n\n"
        + "**Gradient descent on a long, narrow bowl**\n\n```plot\n%% Syft Notes diagram, from 31:05\n" + string.Join("\n", PlotDesign.Surface.Split('\n').Skip(1)) + "\n```\n\n"
        + "*Too large a learning rate zigzags across the narrow direction; past 0.2 it diverges.* (from 31:05)";


    internal static SetupModel Welcome(SkinKind skin, Action<SetupModel>? state = null)
    {
        var m = SetupModel.For(skin);
        m.OneButton = true;
        state?.Invoke(m);
        return m;
    }

    /// <summary>A first lecture's page with no AI writing notes yet, the card in <paramref name="state"/>.</summary>
    internal static LibraryModel FirstLecture(Action<FreeAiModel> state)
    {
        var m = EmptyLibrary(Demo.Page.Note);
        m.Notes = null;
        m.Note = new NoteModel
        {
            Id = "n1", ClassName = "", Noun = Nouns.Lecture, Title = "Lecture, Friday 30 October at 9:30 AM",
            When = "Friday 30 October at 9:30 AM, 50 min", Pending = NoteModel.NotesAreOff(Nouns.Lecture, transcript: true),
        }.Titled();
        var free = new FreeAiModel(new FakeAiLibrary(), "n1") { Offer = true, Choose = true };
        state(free);
        m.FreeAi = free;
        return m;
    }

    /// <summary>A guided setup that reaches nothing: its state is set by hand for each picture.</summary>
    internal static GuidedSetupModel Guided(SkinKind skin, AppRole role = AppRole.Both)
    {
        var services = new GuidedServices { Home = Home, Windows = skin == SkinKind.Win, Find = _ => AgentFound.None, OpenUrl = _ => { }, Post = a => a() };
        return new GuidedSetupModel(SetupModel.For(skin, role), services, () => new AppSettings(), _ => { });
    }

    /// <summary>Settings → Canvas over a library that answers with the connected fixtures.</summary>
    internal static async Task<CanvasSettingsModel> Settings(string state = "state-connected", string overview = "canvas")
    {
        var model = new CanvasSettingsModel(CanvasFixtures.Context(ConnectedLibrary(state).Json(HttpMethod.Get, "/api/v2/canvas", overview)));
        await model.LoadAsync();
        return model;
    }

    /// <summary>The design's sidebar with Due (3 to hand in) and the four classes; <paramref name="selected"/> lit.</summary>
    internal static LibraryModel Sidebar(string selected)
    {
        var m = Demo.Library(due: true);
        m.Status = CanvasSynced;
        foreach (var c in m.Classes) c.Selected = c.Name == selected;
        m.Note = null;
        m.Ask = null;
        return m;
    }

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

    internal static AssignmentModel Detail(string fixture)
    {
        var m = new AssignmentModel(CanvasFixtures.Context());
        m.Show(CanvasFixtures.Load<CanvasApi.AssignmentDetail>(fixture));
        return m;
    }

    /// <summary>The library window, as a first run leaves it: no classes, no lectures yet.</summary>
    static LibraryModel EmptyLibrary(Demo.Page page)
    {
        var m = Demo.Semester(page);
        m.DrawChrome = true;
        m.Classes.Clear();
        return m;
    }

    internal static Control View(SkinKind skin, GuidedSetupModel g) =>
        skin == SkinKind.Mac ? new MacGuidedSetup { DataContext = g, DrawChrome = true } : new WinGuidedSetup { DataContext = g, DrawChrome = true };


    /// <summary>The three newest lectures the shell would already have for CS 101, their days worded the way
    /// <see cref="CanvasWords.ShortDay"/> does it.</summary>
    static readonly (string Title, DateTimeOffset At)[] Cs101Lectures =
    [
        ("Recursion and the call stack", new DateTimeOffset(2025, 9, 23, 20, 0, 0, TimeSpan.Zero)),
        ("Stack frames and scope", new DateTimeOffset(2025, 9, 18, 20, 0, 0, TimeSpan.Zero)),
        ("Functions as values", new DateTimeOffset(2025, 9, 16, 20, 0, 0, TimeSpan.Zero)),
    ];

    /// <summary>A small fluent helper so a control literal can be tweaked (bind a resource, set a flag) inline.</summary>
    public static T Also<T>(this T self, Action<T> then)
    {
        then(self);
        return self;
    }
}
