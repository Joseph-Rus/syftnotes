using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StudyStash.App.Services;
using StudyStash.App.ViewModels;
using StudyStash.App.Views;

namespace StudyStash.App.Tests;

/// <summary>
/// Settings' and setup's choices (the colour theme swatches, the transcription models, the AI engines, Windows'
/// light/dark rows, what AI tools can read) never show a grey square box when hovered, pressed or focused: a swatch
/// or a radio shows a ring round itself, a card its own rounded outline, a row a wash kept inside its rounded group.
/// </summary>
public class SettingsHighlightTests
{
    static readonly string[] Sections = ["General", "Appearance", "Recording", "Library", "Classes", "Notes", "AI", "Access", "Canvas", "Folders"];

    static void Set(Control c, string state) => ((IPseudoClasses)c.Classes).Add(state);
    static void Clear(Control c, string state) => ((IPseudoClasses)c.Classes).Remove(state);

    static bool Shows(IBrush? brush) => brush is ISolidColorBrush { Color.A: > 0 } s ? s.Opacity > 0 : brush is not null and not ISolidColorBrush;

    /// <summary>Settings over a temp home, with the design's example library, AI engines and Canvas.</summary>
    static async Task<(SettingsModel Model, AppHost Host, TempHome Home)> SettingsAsync()
    {
        var home = new TempHome();
        var host = new AppHost(home.Path);
        var library = new FakeLibrarySettings();
        var model = SettingsModel.Make(host, ai: AiDemo.DemoLibrary(), canvas: CanvasFixtures.Context(Scenes.ConnectedLibrary()), library: () => library.Call);
        await model.Engines.Load();
        await model.Access.Load();
        await model.Canvas.LoadAsync();
        return (model, host, home);
    }

    /// <summary>Every button on show whose hover (or press, or keyboard focus) draws a square box round something
    /// round — a swatch, a radio, a tick, a rounded card — or a square wash poking out of the rounded group its row is
    /// in. A plain text link's wash is left alone.</summary>
    static List<string> SquareBoxes(Control root, string where)
    {
        var found = new List<string>();
        foreach (var b in root.GetVisualDescendants().OfType<Button>().Where(b => b.IsEffectivelyVisible && b.Bounds.Width > 0).ToList())
        {
            var wash = b.GetVisualDescendants().OfType<Border>().FirstOrDefault(x => x.Name == "PART_Wash" && x.TemplatedParent == b);
            if (wash is null) continue;
            bool roundInside = b.GetVisualDescendants().OfType<Control>().Any(d => d.IsEffectivelyVisible && d.Bounds.Width >= 12
                && d is Border { CornerRadius.TopLeft: > 0 } or Ellipse);
            // A row filling a rounded group that clips it: its wash follows the group's corners.
            bool clipped = b.GetVisualAncestors().OfType<Border>().FirstOrDefault(g => g.CornerRadius != default) is { ClipToBounds: true };
            if (!roundInside || clipped || wash.CornerRadius != default) continue;
            foreach (string state in new[] { ":pointerover", ":pressed", ":focus-visible" })
            {
                Set(b, state);
                bool drawn = Shows(wash.Background) || wash.BorderThickness != default && Shows(wash.BorderBrush);
                Clear(b, state);
                if (drawn) found.Add($"{where}: {state} on a {b.DataContext?.GetType().Name} button ({string.Join(".", b.Classes)}, {b.Bounds.Size})");
            }
        }
        return found;
    }

    [AvaloniaTheory]
    [InlineData(SkinKind.Mac)]
    [InlineData(SkinKind.Win)]
    public async Task No_choice_in_settings_or_setup_shows_a_square_box(SkinKind skin)
    {
        try
        {
            ((App)Application.Current!).UseSkin(skin);
            var found = new List<string>();
            var (model, host, home) = await SettingsAsync();
            using (home)
            using (host)
            using (model)
            {
                var view = new SettingsView { DataContext = model };
                var window = new Window { Width = 900, Height = 1400, Content = view };
                window.Show();
                foreach (string section in Sections)
                {
                    model.Section = section;
                    Dispatcher.UIThread.RunJobs();
                    window.UpdateLayout();
                    found.AddRange(SquareBoxes(view, $"Settings, {section}"));
                }
                window.Close();
            }
            foreach (var role in new[] { AppRole.Both, AppRole.Library, AppRole.Laptop })
            {
                var m = SetupModel.For(skin, role);
                m.Ai = AiDemo.Setup();
                m.Canvas = await Scenes.SetupStepAsync("found");
                var view = skin == SkinKind.Mac ? (Control)new MacSetup { DataContext = m } : new WinSetup { DataContext = m };
                var window = new Window { Width = 900, Height = 900, Content = view };
                window.Show();
                foreach (var step in m.Steps.Select(s => s.Step).ToList())
                {
                    m.Go(step);
                    Dispatcher.UIThread.RunJobs();
                    window.UpdateLayout();
                    found.AddRange(SquareBoxes(view, $"Setup ({role}), {step}"));
                }
                window.Close();
            }
            Assert.True(found.Count == 0, string.Join("\n", found.Distinct()));
        }
        finally
        {
            ((App)Application.Current!).UseSkin(SkinKind.Mac);
        }
    }

    /// <summary>Marks the first button <paramref name="which"/> picks as hovered (or focused) once the view is laid
    /// out, for a picture of that state.</summary>
    static Control With(Control view, Func<Button, bool> which, string state)
    {
        EventHandler? once = null;
        once = (_, _) =>
        {
            var target = view.GetVisualDescendants().OfType<Button>().FirstOrDefault(b => b.IsEffectivelyVisible && which(b));
            if (target is null) return;
            view.LayoutUpdated -= once;
            Set(target, state);
        };
        view.LayoutUpdated += once;
        return view;
    }

}
