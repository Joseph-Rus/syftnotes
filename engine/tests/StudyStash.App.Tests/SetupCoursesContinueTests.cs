using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StudyStash.App.Services;
using StudyStash.App.ViewModels;
using StudyStash.App.Views;
using StudyStash.Core;

namespace StudyStash.App.Tests;

/// <summary>
/// Setup's Canvas step with a whole term of courses found (25, some with long names): the list scrolls inside the
/// page, the footer stays put under it, and one click on Continue moves on — in the setup window's own size and in
/// the smaller one a small laptop screen leaves it.
/// </summary>
public class SetupCoursesContinueTests
{
    /// <summary>Find my courses for a busy student: 20 this term, 5 from past terms. Made-up courses.</summary>
    internal static string ATerm()
    {
        string[] names =
        [
            "Intro to Programming", "Cell and Molecular Biology", "Calculus II", "Modern World History",
            "Introduction to Engineering Design and Professional Practice Laboratory", "Statics and Strength of Materials",
            "Organic Chemistry I with Laboratory Section and Recitation", "Academic Writing", "Linear Algebra",
            "Principles of Microeconomics", "Physics for Scientists and Engineers II", "Data Structures and Algorithms",
            "Human Anatomy and Physiology", "Public Speaking", "Discrete Mathematics", "Music Appreciation",
            "Environmental Science and Sustainable Systems Seminar", "Spanish II", "Technical Communication for Engineers",
            "Probability and Statistics", "Heat Transfer", "Fluid Mechanics", "Thermodynamics", "Bridge Design", "Chapel",
        ];
        var available = new JsonObject();
        var info = new JsonObject();
        for (int i = 0; i < names.Length; i++)
        {
            string id = (5000 + i).ToString(System.Globalization.CultureInfo.InvariantCulture);
            bool now = i < 20;
            available[id] = names[i];
            info[id] = new JsonObject
            {
                ["code"] = $"ENGR {1000 + i * 10}", ["name"] = names[i], ["term"] = now ? "Fall 2025" : "Spring 2025",
                ["suggested"] = now, ["why"] = now ? "" : "Past term",
            };
        }
        return new JsonObject { ["url"] = "https://school.instructure.com", ["available"] = available, ["course_info"] = info }.ToJsonString();
    }

    static async Task<SetupModel> OnCanvasWithATermAsync(SkinKind skin)
    {
        var m = SetupModel.For(skin, AppRole.Laptop);
        m.Go(SetupStep.Canvas);
        var handler = new FakeLibrary().Json(HttpMethod.Post, "/api/v2/canvas/courses", ATerm());
        var context = CanvasFixtures.Context(handler);
        var canvas = new CanvasConnectModel(context, new CanvasWatch(context), forSetup: true);
        m.Canvas = canvas;
        await canvas.StartAsync(CanvasFixtures.Load<CanvasApi.State>("state-connected"), []);
        canvas.Dispose();
        return m;
    }

    static Control View(SkinKind skin, SetupModel m) =>
        skin == SkinKind.Mac ? new MacSetup { DataContext = m, DrawChrome = false } : new WinSetup { DataContext = m, DrawChrome = false };

    static Rect In(Visual v, Visual root) => new(v.TranslatePoint(new Point(0, 0), root)!.Value, v.Bounds.Size);

    /// <summary>The setup window as the app opens it: the view's own size, or less where the screen has less room
    /// (the app caps the view at the room there is, as Shell.FollowView does).</summary>
    static (Window Window, Control View) Open(SkinKind skin, SetupModel m, double? roomHeight)
    {
        var view = View(skin, m);
        if (roomHeight is { } h) view.MaxHeight = h;
        var window = new Window { Width = view.Width, Height = Math.Min(view.Height, roomHeight ?? double.MaxValue), Content = view };
        window.Show();
        window.UpdateLayout();
        window.CaptureRenderedFrame(); // hit testing reads the drawn scene
        return (window, view);
    }

    static void Click(Window window, Visual target)
    {
        var at = In(target, window).Center;
        window.MouseMove(at);
        window.MouseDown(at, MouseButton.Left);
        window.MouseUp(at, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }

    static string Chain(Visual? v) => v is null ? "nothing" : string.Join(" < ", v.GetSelfAndVisualAncestors().Take(8).Select(a => a.GetType().Name + (a is Control { Name: { } n } ? "#" + n : "")));

    static Button Continue(Control view) =>
        view.GetVisualDescendants().OfType<Button>().Single(b => b.IsEffectivelyVisible && b.Command is { } c && c == ((SetupModel)view.DataContext!).NextCommand);

    [AvaloniaTheory]
    [InlineData(SkinKind.Mac, null)]
    [InlineData(SkinKind.Mac, 560.0)]
    [InlineData(SkinKind.Win, null)]
    [InlineData(SkinKind.Win, 560.0)]
    [InlineData(SkinKind.Win, 480.0)]
    public async Task With_a_whole_term_listed_Continue_shows_and_one_click_moves_on(SkinKind skin, double? room)
    {
        try
        {
            ((App)Application.Current!).UseSkin(skin);
            var m = await OnCanvasWithATermAsync(skin);
            Assert.True(m.Canvas!.ShowPicker);
            Assert.Equal(25, m.Canvas.Picker.Courses.Count);
            var (window, view) = Open(skin, m, room);

            var button = Continue(view);
            var at = In(button, window);
            Assert.True(at.Bottom <= window.ClientSize.Height + 0.5 && at.Top >= 0, $"{skin} {room}: Continue ({at}) is outside the window ({window.ClientSize})");
            Assert.True(m.CanContinue);
            // Nothing is drawn over it: what's under its middle is Continue (or inside it).
            var centre = at.Center;
            var hit = window.InputHitTest(centre) as Visual;
            Assert.True(hit is not null && (hit == button || hit.GetVisualAncestors().Contains(button)), $"{skin} {room}: {Chain(hit)} is over Continue ({at})");

            window.MouseDown(centre, MouseButton.Left);
            window.MouseUp(centre, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(SetupStep.Classes, m.Step);
            window.Close();
        }
        finally
        {
            ((App)Application.Current!).UseSkin(SkinKind.Mac);
        }
    }

    /// <summary>The connect window (from Settings) with a whole term found: its steps scroll above a footer that
    /// stays put, so Finish shows and takes one click, in its own size and on a small screen.</summary>
    [AvaloniaTheory]
    [InlineData(SkinKind.Mac, 680.0)]
    [InlineData(SkinKind.Mac, 480.0)]
    [InlineData(SkinKind.Win, 680.0)]
    [InlineData(SkinKind.Win, 480.0)]
    public async Task In_the_connect_window_Finish_shows_and_one_click_finishes(SkinKind skin, double height)
    {
        try
        {
            ((App)Application.Current!).UseSkin(skin);
            var handler = new FakeLibrary().Json(HttpMethod.Post, "/api/v2/canvas/courses", ATerm());
            var context = CanvasFixtures.Context(handler);
            using var m = new CanvasConnectModel(context, new CanvasWatch(context)) { ShowFooter = true, FinishLabel = "Finish", StepLabel = "" };
            await m.StartAsync(CanvasFixtures.Load<CanvasApi.State>("state-connected"), []);
            Assert.True(m.ShowPicker);
            bool finished = false;
            m.OnFinish = () => finished = true;
            // As Shell.ShowCanvasConnect lays it out: a title bar, then the steps in a padded body.
            Control view = skin == SkinKind.Mac ? new MacCanvasConnect { DataContext = m } : new WinCanvasConnect { DataContext = m };
            PickerRoom.Follow(view.FindControl<ScrollViewer>("Page")!);
            var header = new Controls.WindowHeader { Title = "Connect Canvas" };
            DockPanel.SetDock(header, Dock.Top);
            var window = new Window { Width = 640, Height = height, Content = new DockPanel { Children = { header, new Border { Padding = new Thickness(32, 12, 32, 24), Child = view } } } };
            window.Show();
            window.UpdateLayout();
            window.CaptureRenderedFrame();

            var finish = view.GetVisualDescendants().OfType<Button>().Single(b => b.IsEffectivelyVisible && b.Command == m.FinishCommand);
            var at = In(finish, window);
            Assert.True(at.Top >= 0 && at.Bottom <= window.ClientSize.Height + 0.5, $"{skin} {height}: Finish ({at}) is outside the window");
            var hit = window.InputHitTest(at.Center) as Visual;
            Assert.True(hit is not null && (hit == finish || hit.GetVisualAncestors().Contains(finish)), $"{skin} {height}: {Chain(hit)} is over Finish");
            Click(window, finish);
            Assert.True(finished);
            window.Close();
        }
        finally
        {
            ((App)Application.Current!).UseSkin(SkinKind.Mac);
        }
    }

    /// <summary>A dropdown left open on a setup page (who answers your questions, on the AI engines step): the click
    /// on Continue both closes it and moves on, rather than only closing it.</summary>
    [AvaloniaTheory]
    [InlineData(SkinKind.Mac)]
    [InlineData(SkinKind.Win)]
    public void With_a_dropdown_open_one_click_on_Continue_moves_on(SkinKind skin)
    {
        try
        {
            ((App)Application.Current!).UseSkin(skin);
            var m = SetupModel.For(skin, AppRole.Library);
            m.Ai = AiDemo.Setup();
            m.Go(SetupStep.Ai);
            var (window, view) = Open(skin, m, null);
            var select = view.GetVisualDescendants().OfType<Button>().First(b => b.Flyout is MenuFlyout && b.IsEffectivelyVisible);
            var flyout = (MenuFlyout)select.Flyout!;
            // Its own button still opens and closes it.
            Click(window, select);
            Assert.True(flyout.IsOpen);
            Click(window, select);
            Assert.False(flyout.IsOpen);
            Click(window, select);
            Assert.True(flyout.IsOpen);

            Click(window, Continue(view));
            Assert.False(flyout.IsOpen);
            Assert.NotEqual(SetupStep.Ai, m.Step);
            window.Close();
        }
        finally
        {
            ((App)Application.Current!).UseSkin(SkinKind.Mac);
        }
    }

    /// <summary>The Canvas step with the term listed, in the window's own size and a small screen's.</summary>
    [AvaloniaFact]
    public async Task Shots()
    {
        foreach (var skin in new[] { SkinKind.Mac, SkinKind.Win })
            foreach (double? room in new double?[] { null, 560 })
                foreach (var t in new[] { ThemeVariant.Light, ThemeVariant.Dark })
                {
                    var m = await OnCanvasWithATermAsync(skin);
                    string name = $"{(skin == SkinKind.Mac ? "mac" : "win")}-05-setup-canvas-term{(room is null ? "" : "-small")}";
                    Shot.Take(name, skin, t, () =>
                    {
                        var view = View(skin, m);
                        if (view is MacSetup mac) mac.DrawChrome = true;
                        if (view is WinSetup win) win.DrawChrome = true;
                        if (room is { } h) view.Height = h;
                        return view;
                    }, size: new Size(1100, 800));
                }
        // The connect window from Settings, the term found, at its own size.
        foreach (var skin in new[] { SkinKind.Mac, SkinKind.Win })
            foreach (var t in new[] { ThemeVariant.Light, ThemeVariant.Dark })
            {
                var handler = new FakeLibrary().Json(HttpMethod.Post, "/api/v2/canvas/courses", ATerm());
                var context = CanvasFixtures.Context(handler);
                var m = new CanvasConnectModel(context, new CanvasWatch(context)) { ShowFooter = true, FinishLabel = "Finish", StepLabel = "" };
                await m.StartAsync(CanvasFixtures.Load<CanvasApi.State>("state-connected"), []);
                m.Dispose();
                Shot.Take($"{(skin == SkinKind.Mac ? "mac" : "win")}-07-canvas-connect-term", skin, t, () =>
                {
                    Control view = skin == SkinKind.Mac ? new MacCanvasConnect { DataContext = m } : new WinCanvasConnect { DataContext = m };
                    PickerRoom.Follow(view.FindControl<ScrollViewer>("Page")!);
                    var frame = new Border { Width = 640, Height = 680, Padding = new Thickness(32, 24, 32, 24), Child = view, CornerRadius = new CornerRadius(12) };
                    frame.Bind(Border.BackgroundProperty, frame.GetResourceObservable(skin == SkinKind.Mac ? "Win" : "Layer"));
                    return frame;
                }, size: new Size(800, 800));
            }
    }
}
