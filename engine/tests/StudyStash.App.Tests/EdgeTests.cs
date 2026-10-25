using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StudyStash.App.Controls;
using StudyStash.App.Services;
using StudyStash.App.Views;

namespace StudyStash.App.Tests;

/// <summary>
/// A control's hairline drawn whole: as even along its bottom as along its top. Two things broke it, and pictures at
/// 1x barely showed either. A button cuts off whatever it draws outside its own box, so a ring round a Settings pill
/// showed only on its round ends; and Avalonia paints a list of shadows in order, so a soft shadow listed after a ring
/// was painted over the ring's lower half (the ask bar, lit from above in dark).
/// </summary>
public class EdgeTests
{
    /// <summary>The design's hairline as a shadow (<c>0 0 0 1px</c>): no blur, no offset, only spread.</summary>
    static bool IsRing(BoxShadow s) => !s.IsInset && s.Blur == 0 && s.OffsetX == 0 && s.OffsetY == 0 && s.Spread > 0 && s.Color.A > 0;

    /// <summary>Every hairline drawn as a ring under <paramref name="root"/> that something cuts off (a clipping
    /// ancestor whose box holds the control but not its ring) or paints over (a shadow after it in the list).</summary>
    static IEnumerable<string> Faults(Visual root)
    {
        foreach (var v in root.GetVisualDescendants().OfType<Control>().Where(c => c.IsEffectivelyVisible && c.Bounds.Width > 0 && c.Bounds.Height > 0))
        {
            // The window's frame drawn for a screenshot only (a real window's is the system's).
            if (v.Name == "Chrome") continue;
            BoxShadows shadows = v switch { Border b => b.BoxShadow, Glass g => g.BoxShadow, _ => default };
            double spread = 0;
            for (int i = 0; i < shadows.Count; i++)
            {
                if (!IsRing(shadows[i])) continue;
                spread = Math.Max(spread, shadows[i].Spread);
                if (i < shadows.Count - 1) yield return $"{Who(v)}: its hairline is painted over by the shadow after it";
            }
            if (spread == 0) continue;
            foreach (var a in v.GetVisualAncestors().Where(a => a.ClipToBounds))
            {
                if (v.TranslatePoint(default, a) is not { } at) continue;
                var box = new Rect(at, v.Bounds.Size);
                var room = new Rect(a.Bounds.Size);
                // A control partly scrolled out of sight is cut off as a whole, not just its edge.
                if (room.Contains(box) && !room.Contains(box.Inflate(spread - 0.01)))
                    yield return $"{Who(v)}: its hairline is cut off by {Who(a)}";
            }
        }
    }

    static string Who(Visual v)
    {
        static string Name(StyledElement e) => e.GetType().Name + string.Concat(e.Classes.Where(c => !c.StartsWith(':')).Select(c => "." + c)) + (e.Name is { Length: > 0 } n ? "#" + n : "");
        return v is StyledElement e ? (e.TemplatedParent is StyledElement owner ? $"{Name(owner)} > {Name(e)}" : Name(e)) : v.GetType().Name;
    }

    /// <summary>Shows what <paramref name="build"/> makes (in the look it's for: a view picks its parts by the look
    /// when it's made) 32 points in from a window's edge, so the window cuts off nothing of its own, drawn at
    /// <paramref name="scale"/>.</summary>
    static Window Show(SkinKind skin, ThemeVariant variant, Func<Control> build, double width, double height, double scale = 1)
    {
        Skin.UseTheme(ColourThemes.Default);
        ((App)Application.Current!).UseSkin(skin);
        var window = new Window { Width = width + 64, Height = height + 64, RequestedThemeVariant = variant, Content = new Border { Padding = new Thickness(32), Child = build() } };
        Look.Apply(window);
        if (scale != 1) window.SetRenderScaling(scale);
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    static IEnumerable<(string Name, SkinKind Skin, Func<Control> Build)> Views()
    {
        foreach (var skin in new[] { SkinKind.Mac, SkinKind.Win })
        {
            Control Library(Func<ViewModels.LibraryModel> model) => skin == SkinKind.Mac
                ? new MacLibrary { DataContext = model(), Width = 1280, Height = 860 }
                : new WinLibrary { DataContext = model(), Width = 1280, Height = 860 };
            foreach (var page in Enum.GetValues<Demo.Page>())
                yield return ($"library {page}", skin, () => Library(() => Demo.Semester(page)));
            yield return ("library assignment", skin, () => Library(AppShots.DueApp));
            yield return ("library class on Canvas", skin, () => Library(() => AppShots.ClassApp(false)));
            yield return ("library lecture with AI", skin, () => Library(AppShots.LectureApp));
            yield return ("setup AI", skin, () => skin == SkinKind.Mac ? new MacSetup { DataContext = AppShots.SetupAi(skin) } : new WinSetup { DataContext = AppShots.SetupAi(skin) });
            yield return ("guided setup", skin, () => GuidedSetupShots.View(skin, GuidedSetupShots.Guided(skin)));
            yield return ("panel", skin, () => skin == SkinKind.Mac ? new MacPanel { DataContext = Demo.Panel(recording: false) } : new WinPanel { DataContext = Demo.Panel(recording: false) });
        }
    }

    [AvaloniaFact]
    public void No_hairline_is_cut_off_or_painted_over()
    {
        var faults = new List<string>();
        void Audit(string name, SkinKind skin, Func<Control> build)
        {
            var window = Show(skin, ThemeVariant.Dark, build, 1700, 1100);
            faults.AddRange(Faults(window).Distinct().Select(f => $"{skin} {name}: {f}"));
            window.Close();
        }
        foreach (var (name, skin, build) in Views())
            Audit(name, skin, build);
        foreach (var skin in new[] { SkinKind.Mac, SkinKind.Win })
            foreach (var section in new[] { "General", "Appearance", "Recording", "Classes", "Notes", "Folders", "One-computer-adding" })
            {
                var (model, host, home) = SurfaceShots.MakeSettings(section, skin);
                try { Audit($"settings {section}", skin, () => new SettingsView { DataContext = model }); }
                finally
                {
                    model.Dispose();
                    host.Dispose();
                    if (Directory.Exists(home)) Directory.Delete(home, recursive: true);
                }
            }
        Assert.True(faults.Count == 0, string.Join("\n", faults));
    }

    /// <summary>
    /// The pill it was first seen on (Settings → Library's Add a laptop…), the segmented control, the sidebar's chosen row,
    /// Record, All notes and the ask bar, drawn at 2x light and dark: straight down through each, the hairline is there
    /// at full strength along its top and along its bottom.
    /// </summary>
    [AvaloniaFact]
    public void Each_hairline_is_as_strong_along_the_bottom_as_the_top_at_2x()
    {
        var uneven = new List<string>();
        foreach (var t in new[] { ThemeVariant.Light, ThemeVariant.Dark })
        {
            var line = (t == ThemeVariant.Dark ? Skin.Dark : Skin.Light).Line;
            void Even(Window window, Control c, string what)
            {
                if (Uneven(window, c, line) is { } why) uneven.Add($"{what} {t}: {why}");
            }
            foreach (var (section, find) in new (string, Func<Visual, Control>)[]
            {
                ("One-computer", v => Holding(v, "Add a laptop…")),
                ("Appearance", v => v.GetVisualDescendants().OfType<Button>().Single(b => b.Classes.Contains("seg-item") && b.Classes.Contains("on") && b.IsEffectivelyVisible)),
            })
            {
                var (model, host, home) = SurfaceShots.MakeSettings(section, SkinKind.Mac);
                try
                {
                    var window = Show(SkinKind.Mac, t, () => new SettingsView { DataContext = model }, 1060, 760, scale: 2);
                    Even(window, find(window), section);
                    window.Close();
                }
                finally
                {
                    model.Dispose();
                    host.Dispose();
                    if (Directory.Exists(home)) Directory.Delete(home, recursive: true);
                }
            }
            var app = Show(SkinKind.Mac, t, () => new MacLibrary { DataContext = Demo.Semester(), Width = 1280, Height = 860 }, 1280, 860, scale: 2);
            var all = app.GetVisualDescendants().OfType<Control>().Where(c => c.IsEffectivelyVisible).ToList();
            Even(app, all.OfType<Button>().First(b => b.Classes.Contains("nav-item") && b.Classes.Contains("selected")), "the sidebar's chosen row");
            Even(app, all.OfType<Button>().Single(b => b.Classes.Contains("record")), "Record");
            Even(app, all.OfType<Button>().First(b => b.Classes.Contains("picker")), "All notes");
            Even(app, all.OfType<Border>().Single(b => b.Classes.Contains("ask")), "the ask bar");
            app.Close();
        }
        Assert.True(uneven.Count == 0, string.Join("\n", uneven));
    }

    static Button Holding(Visual root, string words) =>
        root.GetVisualDescendants().OfType<TextBlock>().Single(x => x.Text == words && x.IsEffectivelyVisible).FindAncestorOfType<Button>()!;

    /// <summary>
    /// Down the middle of <paramref name="c"/>, from three pixels outside its box to three inside, at its top and at
    /// its bottom: each has a pixel of the hairline's own colour (a ring sits just outside the box, a border just
    /// inside; one cut off or shaded over has none). Null when it has; otherwise how far off it is.
    /// </summary>
    static string? Uneven(Window window, Control c, Color line)
    {
        var frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("nothing rendered");
        double scale = window.RenderScaling;
        var at = c.TranslatePoint(default, window) ?? throw new InvalidOperationException("not in the window");
        int x = (int)Math.Round((at.X + c.Bounds.Width / 2) * scale);
        int top = (int)Math.Round(at.Y * scale), bottom = (int)Math.Round((at.Y + c.Bounds.Height) * scale);
        int Nearest(int y) => Enumerable.Range(0, 3).Min(dx => Distance(GlassTests.Pixel(frame, x + dx, y), line));
        int nearTop = Enumerable.Range(top - 3, 6).Min(Nearest);
        int nearBottom = Enumerable.Range(bottom - 3, 6).Min(Nearest);
        return nearTop <= 3 && nearBottom <= 3 ? null : $"the hairline is {nearTop} off its colour at the top and {nearBottom} at the bottom";
    }

    static int Distance(Color a, Color b) => Math.Max(Math.Abs(a.R - b.R), Math.Max(Math.Abs(a.G - b.G), Math.Abs(a.B - b.B)));
}
