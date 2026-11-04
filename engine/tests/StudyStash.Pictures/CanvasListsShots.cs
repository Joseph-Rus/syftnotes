using System.Net;
using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using StudyStash.App.Services;
using StudyStash.App.ViewModels;
using StudyStash.App.Views;

namespace StudyStash.App.Pictures;

/// <summary>A class's lists on Canvas while they load, when one of them can't be read, and read again after the
/// student opened a module. Drawn off screen into STUDYSTASH_SHOTS; only with STUDYSTASH_FILM=1 (fixture data only).</summary>
public class CanvasListsShots
{
    static bool Wanted => Environment.GetEnvironmentVariable("STUDYSTASH_FILM") == "1";

    static CanvasApi.ClassRow Cs101() => CanvasFixtures.Load<List<CanvasApi.ClassRow>>("classes").Single(c => c.Class == "CS 101");

    static FakeLibrary Lists(bool announcements = true)
    {
        var handler = new FakeLibrary()
            .Json(HttpMethod.Get, "/api/v2/canvas/assignments", "assignments-cs101")
            .Json(HttpMethod.Get, "/api/v2/canvas/modules", "modules-cs101")
            .Json(HttpMethod.Get, "/api/v2/canvas/files", "files-cs101");
        if (announcements) handler.Json(HttpMethod.Get, "/api/v2/canvas/announcements", "announcements-cs101");
        else handler.Status(HttpMethod.Get, "/api/v2/canvas/announcements", HttpStatusCode.ServiceUnavailable);
        return handler;
    }

    static LibraryModel Window(CanvasClassModel cls)
    {
        var m = Scenes.Sidebar("CS 101");
        m.CanvasOf = "CS 101";
        m.CanvasDot = Skin.ClassDot(0);
        m.CanvasClass = cls;
        return m;
    }

    static void Take(string name, Func<LibraryModel> model)
    {
        foreach (var skin in new[] { SkinKind.Mac, SkinKind.Win })
            Shot.Take($"{(skin == SkinKind.Mac ? "mac" : "win")}-{name}", skin, ThemeVariant.Light, () =>
            {
                var m = model();
                return skin == SkinKind.Mac ? new MacLibrary { DataContext = m, Width = 1280, Height = 860 } : new WinLibrary { DataContext = m, Width = 1280, Height = 860 };
            }, size: new Size(1408, 988));
    }

    /// <summary>The lists asked for and not answered yet: the page Shell shows while the class's lists load.</summary>
    [AvaloniaFact]
    public void While_the_lists_load()
    {
        if (!Wanted) return;
        // As LoadAsync leaves it until the library answers.
        var cls = new CanvasClassModel(CanvasFixtures.Context()) { Tab = ClassTab.Assignments, Loading = true };
        Take("canvas-lists-loading", () => Window(cls));
    }

    /// <summary>Announcements couldn't be read; assignments, modules and files could.</summary>
    [AvaloniaFact]
    public async Task When_one_list_cant_be_read()
    {
        if (!Wanted) return;
        var cls = new CanvasClassModel(CanvasFixtures.Context(Lists(announcements: false)));
        cls.Tab = ClassTab.Assignments;
        await cls.LoadAsync(Cs101(), TestContext.Current.CancellationToken);
        Take("canvas-lists-one-failed", () => Window(cls));
    }

    /// <summary>The student opened an older module and picked an assignment; then a sync read the lists again.</summary>
    [AvaloniaFact]
    public async Task Read_again_after_a_module_was_opened()
    {
        if (!Wanted) return;
        var cls = new CanvasClassModel(CanvasFixtures.Context(Lists()));
        await cls.LoadAsync(Cs101(), TestContext.Current.CancellationToken);
        cls.Tab = ClassTab.Modules;
        foreach (var m in cls.Modules) m.Expanded = false;
        cls.Modules[1].Expanded = true;
        await cls.LoadAsync(Cs101(), TestContext.Current.CancellationToken);
        Take("canvas-lists-read-again", () => Window(cls));
    }
}
