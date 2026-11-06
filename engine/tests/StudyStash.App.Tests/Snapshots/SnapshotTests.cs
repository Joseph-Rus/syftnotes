using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using StudyStash.App.Platform;
using StudyStash.App.Services;
using StudyStash.App.ViewModels;
using StudyStash.App.Views;
using StudyStash.Core.Ai;

namespace StudyStash.App.Tests.Snapshots;

/// <summary>
/// About ten screens of the Syft look, each drawn off screen from demo data and compared with its verified picture
/// (see <see cref="SnapshotSetup"/>): Home (light and dark), Study (dark), a class page, a lecture's notes with a formula and a
/// diagram, Due, Settings → Your AI, the first-run funnel, the recorder, the menu-bar dropdown, a notification, and Ask
/// beside the notes. A change you meant: run with <c>STUDYSTASH_SNAPSHOTS=1</c>, look at the
/// <c>*.received.png</c> beside the test, and rename it over the <c>*.verified.png</c>.
/// </summary>
public class SnapshotTests
{
    public SnapshotTests() => SnapshotSetup.Require();

    static Control Library(LibraryModel m) => new MacLibrary { DataContext = m };

    /// <summary>A page on the pane's own fill, as the library window shows it.</summary>
    static Control OnPane(Control page)
    {
        var pane = new Border { Child = page };
        pane.Bind(Border.BackgroundProperty, pane.GetResourceObservable("Surface"));
        return pane;
    }

    [AvaloniaFact]
    public Task Home_light() => SnapshotSetup.Check(Stage.Library(() => Library(Demo.Semester()), ThemeVariant.Light));

    [AvaloniaFact]
    public Task Home_dark() => SnapshotSetup.Check(Stage.Library(() => Library(Demo.Semester()), ThemeVariant.Dark));

    [AvaloniaFact]
    public Task Class_page_light() => SnapshotSetup.Check(Stage.Library(() => Library(Demo.Semester(Demo.Page.FolderClass)), ThemeVariant.Light));

    [AvaloniaFact]
    public Task Due_light() => SnapshotSetup.Check(Stage.Library(() => Library(Demo.Semester(Demo.Page.Due)), ThemeVariant.Light, height: 900));

    /// <summary>The cardiac lecture's notes scrolled to a formula and a diagram.</summary>
    [AvaloniaFact]
    public Task Notes_with_a_formula_and_a_diagram_light()
    {
        var window = Stage.Library(() => Library(Scenes.CardiacLibrary()), ThemeVariant.Light, height: 800);
        Scenes.ScrollToDiagrams(window);
        return SnapshotSetup.Check(window);
    }

    [AvaloniaFact]
    public async Task Settings_your_AI_light()
    {
        using var home = new TempHome();
        using var host = new AppHost(home.Path, log: _ => { });
        var library = new FakeLibrarySettings();
        using var model = SettingsModel.Make(host, ai: AiDemo.DemoLibrary(), canvas: CanvasFixtures.Context(Scenes.ConnectedLibrary()), library: () => library.Call);
        await model.Engines.Load();
        await model.Access.Load();
        await model.Canvas.LoadAsync();
        await model.Lib.Load();
        model.Section = "AI";
        await SnapshotSetup.Check(Stage.Surface(() => new SettingsView { DataContext = model, DrawChrome = true }, ThemeVariant.Light, new Size(1000, 940), new Thickness(32), 936, 876));
    }

    /// <summary>The first screen after the welcome: are you a student.</summary>
    [AvaloniaFact]
    public Task First_run_funnel_light()
    {
        var setup = SetupModel.For(SkinKind.Mac);
        setup.OneButton = true;
        var funnel = new FunnelModel(setup, new FunnelServices()) { WithCanvas = true, Step = FunnelStep.Student };
        return SnapshotSetup.Check(Stage.Surface(() => new MacFunnel { DataContext = funnel, DrawChrome = true }, ThemeVariant.Light, new Size(684, 724)));
    }

    /// <summary>The recorder open on a lecture, its pill and the live notes under it.</summary>
    [AvaloniaFact]
    public Task Recorder_light() =>
        SnapshotSetup.Check(Stage.Surface(() => new MacRecorder { DataContext = Demo.Recorder(expanded: true) }, ThemeVariant.Light, new Size(520, 640)));

    [AvaloniaFact]
    public Task Menu_bar_dropdown_light() =>
        SnapshotSetup.Check(Stage.Surface(() => new MacPanel { DataContext = Demo.Panel(recording: false) }, ThemeVariant.Light, new Size(420, 520)));

    /// <summary>Two notifications as the app words them: a lecture filed (with its button), and a Canvas one expanded.</summary>
    [AvaloniaFact]
    public Task Notification_light() =>
        SnapshotSetup.Check(Stage.Surface(() => new StackPanel
        {
            Spacing = 10,
            Children =
            {
                new ToastView { Title = "Filed in CS 101", Text = "Recursion and the call stack", ActionLabel = "Open note" },
                ToastView.For(Scenes.ToastGallery()[0]),
            },
        }, ThemeVariant.Light, new Size(520, 360)));

    /// <summary>Study in dark: twelve cards due by class and Start, the exams coming up, and a prep for another exam
    /// (its classes as pills, a name to type, Write the prep not ready yet).</summary>
    [AvaloniaFact]
    public async Task Study_dark()
    {
        var model = await StudyScene.Page();
        await SnapshotSetup.Check(Stage.Surface(() => OnPane(new StudyPage { DataContext = model }), ThemeVariant.Dark, new Size(1036, 1056), new Thickness(28), 980, 1000));
    }

    /// <summary>A lecture's page after an answer: Ask in its own panel beside the notes, the words of the notes it
    /// quoted highlighted, and the "From your notes" chips under the answer.</summary>
    [AvaloniaFact]
    public async Task Ask_beside_the_notes_light()
    {
        var model = await AskScene.Page();
        await SnapshotSetup.Check(Stage.Library(() => Library(model), ThemeVariant.Light, 1180, 840));
    }
}
