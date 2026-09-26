using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using StudyStash.App.Services;
using StudyStash.App.ViewModels;
using StudyStash.App.Views;

namespace StudyStash.App.Tests;

/// <summary>
/// The Canvas and AI screens where the app really shows them: the full app window (MacLibrary/WinLibrary) with Due, a
/// linked class's page, an assignment, a lecture's rewritable notes and the ask bar; the real settings window's
/// Canvas, AI engines and AI tool access; and the dropdown with its next due. Named after the design section each
/// matches ("mac-09-canvas-due-app"), to compare side by side with its ref.
/// </summary>
public class AppShots
{
    static readonly ThemeVariant[] Themes = [ThemeVariant.Light, ThemeVariant.Dark];

    static AppShots() => Environment.SetEnvironmentVariable("STUDYSTASH_STILL", "1");

    const string CanvasSynced = "Library connected · Canvas synced 10:24";

    /// <summary>The design's sidebar with Due (3 to hand in) and the four classes; <paramref name="selected"/> lit.</summary>
    static LibraryModel Sidebar(string selected)
    {
        var m = Demo.Library(due: true);
        m.Status = CanvasSynced;
        foreach (var c in m.Classes) c.Selected = c.Name == selected;
        m.Groups.Clear();
        return m;
    }

    static async Task<LibraryModel> DueApp()
    {
        var m = Sidebar("Due");
        m.ClassTitle = "Due";
        m.DueList = await CanvasShots.DueModel();
        m.DueList.SelectItem("CS 101", "9001");
        m.List = LibraryList.Due;
        m.Note = null;
        m.Assignment = CanvasShots.Detail("assignment-9001");
        return m;
    }

    static LibraryModel ClassApp(bool narrow)
    {
        var m = Sidebar("CS 101");
        m.ClassTitle = "CS 101";
        var cls = CanvasShots.ClassModel();
        cls.Tab = ClassTab.Assignments;
        cls.Done[0].SelectCommand.Execute(null); // Problem set 4
        m.CanvasClass = cls;
        m.List = LibraryList.CanvasClass;
        m.Narrow = narrow;
        if (!narrow) m.Assignment = CanvasShots.Detail("assignment-9002");
        return m;
    }

    /// <summary>Design 04's lecture, its notes now under "Summary · Written by Ollama" with "Rewrite notes", and the
    /// ask bar picking its engine.</summary>
    static LibraryModel LectureApp()
    {
        var m = Demo.Library();
        m.Notes = AiDemo.LectureNotes(Demo.Notes);
        m.Ask = AiDemo.AskIdle();
        return m;
    }

    [AvaloniaFact]
    public async Task Mac_app_canvas()
    {
        var due = await DueApp();
        foreach (var t in Themes)
            Shot.Take("mac-09-canvas-due-app", SkinKind.Mac, t, () => new MacLibrary { DataContext = due, Width = 1280, Height = 800 });
        var cls = ClassApp(narrow: false);
        foreach (var t in Themes)
            Shot.Take("mac-10-canvas-class-tabs-app", SkinKind.Mac, t, () => new MacLibrary { DataContext = cls, Width = 1280, Height = 800 });
        var narrow = ClassApp(narrow: true);
        foreach (var t in Themes)
            Shot.Take("mac-11-canvas-class-sections-app", SkinKind.Mac, t, () => new MacLibrary { DataContext = narrow, Width = 588, Height = 800 });
    }

    [AvaloniaFact]
    public async Task Win_app_canvas()
    {
        var due = await DueApp();
        foreach (var t in Themes)
            Shot.Take("win-09-canvas-due-app", SkinKind.Win, t, () => new WinLibrary { DataContext = due, Width = 1280, Height = 800 });
        var cls = ClassApp(narrow: false);
        foreach (var t in Themes)
            Shot.Take("win-10-canvas-class-tabs-app", SkinKind.Win, t, () => new WinLibrary { DataContext = cls, Width = 1280, Height = 800 });
        var narrow = ClassApp(narrow: true);
        foreach (var t in Themes)
            Shot.Take("win-11-canvas-class-sections-app", SkinKind.Win, t, () => new WinLibrary { DataContext = narrow, Width = 640, Height = 800 });
    }

    [AvaloniaFact]
    public void Mac_app_ai()
    {
        foreach (var t in Themes)
            Shot.Take("mac-04-full-app-ai", SkinKind.Mac, t, () => new MacLibrary { DataContext = LectureApp(), Width = 1280, Height = 800 });
    }

    [AvaloniaFact]
    public void Win_app_ai()
    {
        foreach (var t in Themes)
            Shot.Take("win-04-full-app-ai", SkinKind.Win, t, () => new WinLibrary { DataContext = LectureApp(), Width = 1280, Height = 800 });
    }

    /// <summary>The real settings window at one of the new sections, over a temp home (never a real one).</summary>
    static async Task SettingsShot(string name, SkinKind skin, string section)
    {
        string home = Path.Combine(Path.GetTempPath(), "studystash-settings-" + Guid.NewGuid().ToString("N"));
        var host = new AppHost(home);
        var canvas = CanvasFixtures.Context(CanvasShots.ConnectedLibrary());
        var model = SettingsModel.Make(host, ai: AiDemo.DemoLibrary(), canvas: canvas);
        try
        {
            model.Section = section;
            // The section loads itself when it opens; wait for it the way the window would.
            await model.Engines.Load();
            await model.Access.Load();
            await model.Canvas.LoadAsync();
            var size = new Size(1700, skin == SkinKind.Mac ? 908 : 988);
            foreach (var t in Themes)
                Shot.Take(name, skin, t, () => new SettingsView { DataContext = model, DrawChrome = true }, size: size);
        }
        finally
        {
            model.Dispose();
            host.Dispose();
            if (Directory.Exists(home)) Directory.Delete(home, recursive: true);
        }
    }

    [AvaloniaFact]
    public async Task Mac_settings_canvas_ai()
    {
        await SettingsShot("mac-06-canvas-settings-app", SkinKind.Mac, "Canvas");
        await SettingsShot("mac-13-ai-engines-app", SkinKind.Mac, "AI");
        await SettingsShot("mac-14-ai-tool-access-app", SkinKind.Mac, "Access");
    }

    [AvaloniaFact]
    public async Task Win_settings_canvas_ai()
    {
        await SettingsShot("win-06-canvas-settings-app", SkinKind.Win, "Canvas");
        await SettingsShot("win-13-ai-engines-app", SkinKind.Win, "AI");
        await SettingsShot("win-14-ai-tool-access-app", SkinKind.Win, "Access");
    }

    /// <summary>Setup at Canvas (design 07): the extension step open, as the lane's own shot has it.</summary>
    static async Task<SetupModel> SetupCanvas(SkinKind skin)
    {
        var m = SetupModel.For(skin);
        m.Canvas = await CanvasShots.Step2Async();
        m.Canvas.StepLabel = "";
        m.Canvas.ShowFooter = false;
        m.Go(SetupStep.Canvas);
        return m;
    }

    /// <summary>Library setup at the AI engines (design 15).</summary>
    static SetupModel SetupAi(SkinKind skin)
    {
        var m = SetupModel.For(skin);
        m.SetRole(AppRole.Library, skin);
        m.Ai = AiDemo.Setup();
        m.Go(SetupStep.Ai);
        return m;
    }

    [AvaloniaFact]
    public async Task Mac_setup_canvas_ai()
    {
        var canvas = await SetupCanvas(SkinKind.Mac);
        foreach (var t in Themes)
            Shot.Take("mac-07-canvas-connect-app", SkinKind.Mac, t, () => new MacSetup { DataContext = canvas, DrawChrome = true });
        var ai = SetupAi(SkinKind.Mac);
        foreach (var t in Themes)
            Shot.Take("mac-15-ai-library-setup-app", SkinKind.Mac, t, () => new MacSetup { DataContext = ai, DrawChrome = true });
    }

    [AvaloniaFact]
    public async Task Win_setup_canvas_ai()
    {
        var canvas = await SetupCanvas(SkinKind.Win);
        foreach (var t in Themes)
            Shot.Take("win-07-canvas-connect-app", SkinKind.Win, t, () => new WinSetup { DataContext = canvas, DrawChrome = true });
        var ai = SetupAi(SkinKind.Win);
        foreach (var t in Themes)
            Shot.Take("win-15-ai-library-setup-app", SkinKind.Win, t, () => new WinSetup { DataContext = ai, DrawChrome = true });
    }

    /// <summary>Design 12's dropdown: the timetable's hint, then the next thing due.</summary>
    static PanelModel PanelWithDue()
    {
        var p = Demo.Panel(recording: false);
        p.Hint = "From your timetable · 10:00–11:15";
        p.NextDue = CanvasQuick.NextDue(CanvasFixtures.Load<CanvasApi.DueResponse>("due"), CanvasFixtures.Zone, CanvasFixtures.Now, _ => { });
        return p;
    }

    [AvaloniaFact]
    public void Mac_dropdown_due()
    {
        foreach (var t in Themes)
            Shot.Take("mac-12-canvas-dropdown-app", SkinKind.Mac, t, () => Shot.Side(
                new MacPanel { DataContext = PanelWithDue() },
                new MacQuick { DataContext = CanvasShots.QuickWithDue() }));
    }

    [AvaloniaFact]
    public void Win_dropdown_due()
    {
        foreach (var t in Themes)
            Shot.Take("win-12-canvas-dropdown-app", SkinKind.Win, t, () => Shot.Side(
                new WinPanel { DataContext = PanelWithDue() },
                new WinQuick { DataContext = CanvasShots.QuickWithDue() }));
    }
}
