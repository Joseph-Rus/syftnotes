using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StudyStash.App.Services;
using StudyStash.App.ViewModels;
using StudyStash.App.Views;

namespace StudyStash.App.Tests;

/// <summary>
/// A library-only computer's dropdown (the Mac mini at home): no Record, no transcription model, no lectures of its
/// own, but whether the library is running, what it holds, when Canvas last synced and which laptops reach it. A
/// computer that's both keeps Record.
/// </summary>
public class LibraryPanelTests
{
    static readonly DateTimeOffset Now = new(2026, 9, 21, 15, 0, 0, TimeSpan.Zero);

    static JsonObject Overview(params (string Name, DateTimeOffset Seen)[] laptops) => new()
    {
        ["name"] = "Sam's library",
        ["classes"] = new JsonArray(new JsonObject { ["name"] = "CS 101", ["lectures"] = 12 }, new JsonObject { ["name"] = "BIO 110", ["lectures"] = 7 }),
        ["unsorted"] = 2,
        ["writing"] = 0,
        ["laptops"] = new JsonArray(laptops.Select(l => (JsonNode?)new JsonObject { ["name"] = l.Name, ["seen"] = l.Seen.ToString("o") }).ToArray()),
    };

    [Fact]
    public void The_library_lines_read_plainly()
    {
        var lines = LibraryPanelWords.From(true, false, "Mac", Overview(("Sam's MacBook Air", Now.AddSeconds(-20)), ("Sam's iPad", Now.AddMinutes(-2))),
            new CanvasApi.State { Status = "connected", LastSync = Now.AddMinutes(-10) }, Now, TimeZoneInfo.Utc);
        Assert.Equal("Library running on this Mac", lines.Running);
        Assert.True(lines.RunningGood);
        Assert.Equal("21 lectures in 2 classes", lines.Lectures);
        Assert.Equal("Canvas synced 10 min ago", lines.Canvas);
        Assert.Equal("2 laptops connected", lines.Laptops);

        var stopped = LibraryPanelWords.From(false, false, "PC", null, null, Now, TimeZoneInfo.Utc);
        Assert.Equal("Your library isn't running", stopped.Running);
        Assert.Equal("Start it in Settings → Your library", stopped.Lectures);
        Assert.Null(stopped.Canvas);
        Assert.Null(stopped.Laptops);
        Assert.Equal("Starting your library…", LibraryPanelWords.From(false, true, "PC", null, null, Now, TimeZoneInfo.Utc).Running);
    }

    [Theory]
    [InlineData(0, 0, 0, "No lectures yet")]
    [InlineData(1, 0, 0, "1 lecture in 1 class")]
    [InlineData(5, 3, 2, "8 lectures in 1 class · writing 2")]
    public void Lectures_are_counted_with_unsorted_and_writing(int inClass, int unsorted, int writing, string expected)
    {
        var o = new JsonObject { ["classes"] = new JsonArray(new JsonObject { ["name"] = "CS 101", ["lectures"] = inClass }), ["unsorted"] = unsorted, ["writing"] = writing };
        Assert.Equal(expected, LibraryPanelWords.Lectures(o));
    }

    [Fact]
    public void Laptops_that_went_quiet_say_when_they_were_last_seen()
    {
        var zone = TimeZoneInfo.Utc;
        Assert.Equal(("1 laptop connected", true), LibraryPanelWords.Laptops(Overview(("A", Now.AddMinutes(-4)))["laptops"] as JsonArray, Now, zone));
        Assert.Equal(("No laptop connected · last seen 3 h ago", false), LibraryPanelWords.Laptops(Overview(("A", Now.AddHours(-3)))["laptops"] as JsonArray, Now, zone));
        Assert.Equal(("No laptop has connected yet", false), LibraryPanelWords.Laptops([], Now, zone));
        Assert.Equal((null, true), LibraryPanelWords.Laptops(null, Now, zone)); // an older library doesn't count them
    }

    [Theory]
    [InlineData("not_set_up", "Canvas isn't connected", true)]
    [InlineData("no_extension", "Canvas is waiting for Chrome", false)]
    [InlineData("syncing", "Canvas syncing…", true)]
    [InlineData("error", "Canvas couldn't sync", false)]
    public void Canvas_says_what_it_is_doing(string status, string words, bool good) =>
        Assert.Equal((words, good), LibraryPanelWords.Canvas(new CanvasApi.State { Status = status }, Now, TimeZoneInfo.Utc));

    static PanelModel LibraryOnly() => new()
    {
        LibraryOnly = true,
        Status = "Library running on this Mac",
        Library = LibraryPanelWords.From(true, false, "Mac", Overview(("Sam's MacBook Air", Now.AddSeconds(-20))),
            new CanvasApi.State { Status = "connected", LastSync = Now.AddMinutes(-10) }, Now, TimeZoneInfo.Utc),
    };

    [AvaloniaTheory]
    [InlineData(SkinKind.Mac)]
    [InlineData(SkinKind.Win)]
    public void A_library_only_dropdown_has_no_Record_and_shows_the_library(SkinKind skin)
    {
        ((App)Application.Current!).UseSkin(skin);
        var model = LibraryOnly();
        model.Recent.Add(new LectureItem { Title = "Recursion and the call stack", Detail = "Filed in CS 101" });
        Control view = skin == SkinKind.Mac ? new MacPanel { DataContext = model } : new WinPanel { DataContext = model };
        var w = new Window { Width = 600, Height = 700, RequestedThemeVariant = ThemeVariant.Light, Content = view };
        w.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            var shown = view.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible).Select(t => t.Text).ToList();
            Assert.DoesNotContain(view.GetVisualDescendants().OfType<Button>(), b => b.Name is "Record" or "SwitchClass" && b.IsEffectivelyVisible);
            Assert.DoesNotContain(shown, t => t?.Contains("transcription", StringComparison.OrdinalIgnoreCase) == true);
            Assert.DoesNotContain("Recent", shown);
            Assert.Contains("Library running on this Mac", shown);
            Assert.Contains("21 lectures in 2 classes", shown);
            Assert.Contains("Canvas synced 10 min ago", shown);
            Assert.Contains("1 laptop connected", shown);
            Assert.Single(shown, t => t == "Library running on this Mac"); // said once, not again in the status line
            // Search, Open Study Stash and the gear stay.
            foreach (var name in new[] { "Search", "OpenApp", "Settings" })
                Assert.Contains(view.GetVisualDescendants().OfType<Button>(), b => b.Name == name && b.IsEffectivelyVisible);
            // The library's lines: markers in one column, words starting at one edge.
            var card = view.FindControl<Border>("LibraryCard")!;
            var lefts = card.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible).Select(t => t.TranslatePoint(default, card)!.Value.X).Distinct().ToList();
            Assert.Single(lefts);
        }
        finally
        {
            w.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData(SkinKind.Mac)]
    [InlineData(SkinKind.Win)]
    public void A_computer_that_records_keeps_Record(SkinKind skin)
    {
        ((App)Application.Current!).UseSkin(skin);
        var model = Demo.Panel(recording: false);
        Control view = skin == SkinKind.Mac ? new MacPanel { DataContext = model } : new WinPanel { DataContext = model };
        var w = new Window { Width = 600, Height = 700, Content = view };
        w.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            Assert.Contains(view.GetVisualDescendants().OfType<Button>(), b => b.Name == "Record" && b.IsEffectivelyVisible);
            Assert.False(view.FindControl<Border>("LibraryCard")!.IsEffectivelyVisible);
        }
        finally
        {
            w.Close();
        }
    }
}
