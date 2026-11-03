using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StudyStash.App.Controls;
using StudyStash.App.Services;
using StudyStash.App.ViewModels;
using StudyStash.App.Views;

namespace StudyStash.App.Tests;

/// <summary>
/// Text that is cut off with an ellipsis, or cut at its last line, must have a way to be read in full: its own tooltip
/// (<see cref="Tips.WhenCutProperty"/> makes it show only while the text really is cut), or one on something around it.
/// Home, a class's page, a lecture's notes and Settings, in both looks, at the default size and a small window.
/// </summary>
public class CutTextTests
{
    /// <summary>
    /// Cut text in another lane's files that can't pass yet. Each entry is "View: the words" and says why it waits.
    /// Empty for now: add an entry only with the file that has to change.
    /// </summary>
    static readonly HashSet<string> Waiting = [];

    static bool Trims(TextBlock t) => t.TextTrimming != TextTrimming.None || t.MaxLines > 0;

    /// <summary>Whether the pointer resting on <paramref name="t"/>, or on something around it, shows a tooltip.</summary>
    static bool CanBeRead(TextBlock t) =>
        ToolTip.GetTip(t) is not null || t.GetVisualAncestors().OfType<Control>().Any(c => ToolTip.GetTip(c) is not null);

    /// <summary>Every shown TextBlock under <paramref name="root"/> whose words are cut and which has no way to read the rest.</summary>
    static List<string> CutWithoutAWay(Visual root, string page) =>
    [
        .. root.GetVisualDescendants().OfType<TextBlock>()
            .Where(t => t.IsEffectivelyVisible && t.Bounds.Width > 0 && !string.IsNullOrEmpty(t.Text) && Trims(t) && Tips.IsCut(t) && !CanBeRead(t))
            .Select(t => $"{page} / {t.FindAncestorOfType<UserControl>()?.GetType().Name ?? t.GetType().Name}: \"{t.Text}\"")
            .Where(line => !Waiting.Any(w => line.Contains(w))),
    ];

    static List<string> Show(SkinKind skin, Func<Control> build, double width, double height, string page)
    {
        ((App)Application.Current!).UseSkin(skin);
        var window = new Window { Width = width, Height = height, RequestedThemeVariant = ThemeVariant.Light, Content = build() };
        try
        {
            Views.Look.Apply(window);
            window.Show();
            DiagramsReady.Wait(window);
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            return CutWithoutAWay(window, $"{skin} {width}x{height} {page}");
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task No_text_on_the_main_pages_is_cut_off_without_a_way_to_read_the_rest()
    {
        var found = new List<string>();
        var home = new TempHome();
        var host = new AppHost(home.Path);
        var library = new FakeLibrarySettings();
        var settings = SettingsModel.Make(host, ai: AiDemo.DemoLibrary(), canvas: CanvasFixtures.Context(CanvasShots.ConnectedLibrary()), library: () => library.Call);
        try
        {
            await settings.Engines.Load();
            await settings.Access.Load();
            await settings.Canvas.LoadAsync();
            foreach (var skin in new[] { SkinKind.Mac, SkinKind.Win })
            foreach (var (width, height) in new[] { (1280d, 800d), (992d, 696d) })
            {
                Control Library(LibraryModel m) => skin == SkinKind.Mac ? new MacLibrary { DataContext = m } : new WinLibrary { DataContext = m };
                found.AddRange(Show(skin, () => Library(Demo.Overview()), width, height, "Home"));
                found.AddRange(Show(skin, () => Library(Demo.Overview("CS 101")), width, height, "a class"));
                found.AddRange(Show(skin, () => Library(Demo.Library()), width, height, "a lecture's notes"));
                foreach (string section in new[] { "General", "Library", "Canvas" })
                {
                    settings.Section = section;
                    found.AddRange(Show(skin, () => new SettingsView { DataContext = settings }, width, height, $"Settings {section}"));
                }
            }
        }
        finally
        {
            ((App)Application.Current!).UseSkin(SkinKind.Mac);
            settings.Dispose();
            host.Dispose();
            home.Dispose();
        }
        var distinct = found.Distinct().ToList();
        Assert.True(distinct.Count == 0, $"{distinct.Count} cut-off texts have no tooltip to read them in full (put c:Tips.WhenCut=\"True\" and ToolTip.Tip on them):\n  " + string.Join("\n  ", distinct));
    }
}
