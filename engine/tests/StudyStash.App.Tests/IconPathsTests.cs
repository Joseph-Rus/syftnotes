using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.Styling;
using Avalonia.Threading;
using System.Text.RegularExpressions;
using StudyStash.App.Controls;
using StudyStash.App.ViewModels;
using StudyStash.App.Views;

namespace StudyStash.App.Tests;

/// <summary>Every icon the app draws is one IconPaths has: an icon whose name isn't there draws nothing. Looked at two
/// ways: every glyph written into a view or a control, and every glyph on the Canvas screens as they're built (some
/// come from the models).</summary>
public class IconPathsTests
{
    static string App()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (Path.Combine(dir.FullName, "engine", "src", "StudyStash.App") is { } found && Directory.Exists(found)) return found;
        throw new DirectoryNotFoundException("the app's source folder isn't above the tests");
    }

    [Fact]
    public void Every_glyph_written_in_a_view_or_a_control_is_in_IconPaths()
    {
        var literal = new Regex("Glyph\\s*=\\s*\"([^\"{}]+)\"");
        var missing = new List<string>();
        int seen = 0;
        foreach (var file in Directory.EnumerateFiles(App(), "*.*", SearchOption.AllDirectories)
                     .Where(f => (f.EndsWith(".axaml") || f.EndsWith(".cs")) && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")))
            foreach (Match m in literal.Matches(File.ReadAllText(file)))
            {
                seen++;
                // "brand:claude" and the like are a product's own mark, drawn by Icon itself.
                if (!m.Groups[1].Value.StartsWith("brand:", StringComparison.Ordinal) && !IconPaths.All.ContainsKey(m.Groups[1].Value)) missing.Add($"{Path.GetFileName(file)}: {m.Groups[1].Value}");
            }
        Assert.True(seen > 50, $"only {seen} glyphs found: is the pattern or the folder wrong?");
        Assert.Empty(missing);
    }

    static void AllKnown(Func<Control> build, SkinKind skin)
    {
        Skin.UseTheme(ColourThemes.Default);
        ((StudyStash.App.App)Application.Current!).UseSkin(skin);
        var window = new Window { Width = 1100, Height = 900, RequestedThemeVariant = ThemeVariant.Light, Content = build() };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            foreach (var icon in window.GetLogicalDescendants().OfType<Icon>())
            {
                if (icon.Glyph.Length == 0 || icon.Glyph.StartsWith("brand:", StringComparison.Ordinal)) continue; // e.g. the syncing card, which draws a loader instead
                Assert.True(IconPaths.All.ContainsKey(icon.Glyph), $"Icon glyph '{icon.Glyph}' isn't in IconPaths.All");
            }
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData(SkinKind.Mac)]
    [InlineData(SkinKind.Win)]
    public async Task Every_glyph_on_the_Canvas_screens_is_in_IconPaths(SkinKind skin)
    {
        bool mac = skin == SkinKind.Mac;
        foreach (string state in new[] { "state-not-set-up", "state-no-extension", "state-chrome-away", "state-signed-out", "state-syncing", "state-connected", "state-updated", "state-error" })
            AllKnown(() => mac ? new MacCanvasStatus { DataContext = Scenes.Status(state) } : new WinCanvasStatus { DataContext = Scenes.Status(state) }, skin);

        var settings = await Scenes.Settings();
        AllKnown(() => mac ? new MacCanvasSettings { DataContext = settings } : new WinCanvasSettings { DataContext = settings }, skin);

        foreach (string step in new[] { "chrome", "waiting", "folder", "found", "edge", "firefox", "safari", "no-browser" })
        {
            var connect = await Scenes.SetupStepAsync(step, mac);
            AllKnown(() => mac ? new MacCanvasConnect { DataContext = connect } : new WinCanvasConnect { DataContext = connect }, skin);
        }

        var cls = Scenes.ClassModel();
        cls.Tab = ClassTab.Assignments;
        cls.Done[0].SelectCommand.Execute(null);
        var detail = Scenes.Detail("assignment-9002");
        AllKnown(() => mac
            ? new Grid { Children = { new MacCanvasClass { DataContext = cls }, new MacAssignment { DataContext = detail } } }
            : new Grid { Children = { new WinCanvasClass { DataContext = cls }, new WinAssignment { DataContext = detail } } }, skin);
    }
}
