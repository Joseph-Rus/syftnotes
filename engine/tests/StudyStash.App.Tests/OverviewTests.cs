using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StudyStash.App.Services;
using StudyStash.App.ViewModels;
using StudyStash.App.Views;

namespace StudyStash.App.Tests;

/// <summary>Home and each class's home: what they say, what a click on them opens, and how they sit in the library
/// window in both looks, wide and narrow.</summary>
public class OverviewTests
{
    static readonly TimeZoneInfo Zone = TimeZoneInfo.CreateCustomTimeZone("Test", TimeSpan.FromHours(-7), "Test", "Test");
    static readonly DateTimeOffset Now = new(2026, 9, 30, 14, 0, 0, TimeSpan.FromHours(-7));

    static CanvasApi.Item Item(string cls, string id, string name, double inDays, string status = "open", bool missing = false) => new()
    {
        Class = cls, Id = id, Name = name, Kind = "assignment", DueAt = Now.AddDays(inDays), Status = status, Missing = missing,
    };

    static OverviewModel.LectureFacts Lecture(string id, string cls, int daysAgo, bool writing = false) =>
        new(id, $"Lecture {id}", cls, Now.AddDays(-daysAgo), 3600, $"About {id}.", writing);

    static OverviewModel.Sources Sources(List<string>? opened = null, bool canvas = true, bool calendars = true) => new()
    {
        Now = Now, Zone = Zone,
        Classes = [("CS 101", Brushes.Red, 3), ("BIO 110", Brushes.Green, 1), ("Chess club", Brushes.Blue, 0)],
        Lectures = [Lecture("a", "CS 101", 1), Lecture("b", "BIO 110", 2, writing: true), Lecture("c", "CS 101", 9)],
        Due = canvas ? new CanvasApi.DueResponse
        {
            Groups =
            [
                new CanvasApi.Group { Key = "overdue", Items = [Item("BIO 110", "1", "Lab report", -2, "missing", missing: true)] },
                new CanvasApi.Group { Key = "week", Items = [Item("CS 101", "2", "Problem set", 1)] },
                new CanvasApi.Group { Key = "handed_in", Items = [Item("CS 101", "3", "Old quiz", -5, "submitted")] },
            ],
        } : null,
        Canvas = canvas ? [new CanvasApi.ClassRow { Class = "CS 101", Linked = true }] : [],
        Events = calendars ? [new ComingUpRow("e", "1:30 PM", "CS 101 Lecture", "Hall 2", "CS 101", Brushes.Red, false)] : null,
        Unsorted = 2,
        Writing = 1,
        OpenClass = c => opened?.Add("class:" + c),
        OpenAllLectures = c => opened?.Add("lectures:" + c),
        OpenLecture = (c, id) => opened?.Add($"lecture:{c}/{id}"),
        OpenAssignment = (c, id) => opened?.Add($"assignment:{c}/{id}"),
        OpenDueList = () => opened?.Add("due"),
        OpenUnsorted = () => opened?.Add("unsorted"),
    };

    [Fact]
    public void Home_is_every_class_at_once()
    {
        var opened = new List<string>();
        var m = OverviewModel.Home(Sources(opened));

        Assert.True(m.IsHome);
        Assert.Equal("Good afternoon", m.Title);
        Assert.Equal("Wednesday 30 September", m.Subtitle);
        // Every lecture (Unsorted's too), the classes, what's to hand in and what's late, what's being written, Unsorted.
        Assert.Equal(["6 Lectures", "3 Classes", "2 To hand in", "1 Overdue", "1 Writing notes", "2 Unsorted"], m.Stats.Select(s => $"{s.Value} {s.Label}"));
        Assert.True(m.Stats.Single(s => s.Label == "Overdue").Strong);

        // Only what's still to hand in, soonest first, with the class it's for.
        Assert.Equal(["Lab report", "Problem set"], m.Due.Select(d => d.Title));
        Assert.Equal("Missing", m.Due[0].Right);
        Assert.True(m.Due[0].Strong);
        Assert.EndsWith("· BIO 110", m.Due[0].Sub);
        Assert.Equal("Tomorrow", m.Due[1].Right);

        Assert.Equal(["Lecture a", "Lecture b", "Lecture c"], m.Lectures.Select(l => l.Title));
        Assert.Equal("Writing notes…", m.Lectures[1].Summary);
        Assert.Equal("CS 101 Lecture", Assert.Single(m.Events).Title);

        // A card for every class, a club with no lectures too: its count, its last lecture and what's next.
        Assert.Equal(["CS 101", "BIO 110", "Chess club"], m.Classes.Select(c => c.Name));
        Assert.Equal("3 lectures · last Tue 29 Sep", m.Classes[0].Lectures);
        Assert.Equal("Problem set · Tomorrow", m.Classes[0].Next);
        Assert.True(m.Classes[1].NextStrong);
        Assert.Equal("0 lectures", m.Classes[2].Lectures);
        Assert.False(m.Classes[2].HasNext);
        Assert.False(m.HasLinks);

        // Each opens where it says.
        m.OpenClassCommand.Execute(m.Classes[2]);
        m.OpenLectureCommand.Execute(m.Lectures[1]);
        m.OpenDueCommand.Execute(m.Due[0]);
        m.OpenStatCommand.Execute(m.Stats.Single(s => s.Label == "To hand in"));
        m.OpenStatCommand.Execute(m.Stats.Single(s => s.Label == "Unsorted"));
        m.AllDueCommand.Execute(null);
        Assert.Equal(["class:Chess club", "lecture:BIO 110/b", "assignment:BIO 110/1", "due", "unsorted", "due"], opened);
        Assert.False(m.Stats.Single(s => s.Label == "Lectures").CanOpen);
    }

    [Fact]
    public void Home_without_Canvas_or_calendars_leaves_their_sections_out()
    {
        var m = OverviewModel.Home(Sources(canvas: false, calendars: false));
        Assert.False(m.ShowDue);
        Assert.False(m.ShowEvents);
        Assert.False(m.HasSide);
        Assert.Equal(3, m.LecturesSpan);
        Assert.DoesNotContain(m.Stats, s => s.Label is "To hand in" or "Overdue");
        Assert.All(m.Classes, c => Assert.False(c.HasNext));

        // With calendars but nothing on them, Coming up still shows, and says so.
        var quiet = OverviewModel.Home(Sources(canvas: false) with { Events = [] });
        Assert.True(quiet.ShowEvents);
        Assert.True(quiet.NoEvents);
    }

    [Fact]
    public void A_class_home_is_that_class_alone_with_the_ways_further_in()
    {
        var opened = new List<string>();
        var s = Sources(opened);
        var facts = new ClassHomeFacts("CS 101", Brushes.Red, 3, "CS 101", "Intro to Computer Science", "Programs from the ground up.",
            new CanvasApi.Counts { ToHandIn = 1, Done = 4, Modules = 6, Announcements = 3, AnnouncementsNew = 1 });
        var m = OverviewModel.ForClass(facts, s);
        m.AddLinks(facts, () => opened.Add("all"), tab => opened.Add("tab:" + tab));

        Assert.False(m.IsHome);
        Assert.Equal("CS 101", m.Title);
        Assert.Equal("3 lectures · CS 101 on Canvas", m.Subtitle);
        Assert.Equal("Programs from the ground up.", m.Description);
        Assert.Equal(["3 Lectures", "1 To hand in", "1 New announcement", "Tue 29 Sep Last lecture", "1:30 PM Next on your calendar"], m.Stats.Select(x => $"{x.Value} {x.Label}"));
        Assert.Equal(["Problem set"], m.Due.Select(d => d.Title));
        Assert.Equal(["Lecture a", "Lecture c"], m.Lectures.Select(l => l.Title));
        Assert.Equal("Hall 2", Assert.Single(m.Events).Detail);
        Assert.False(m.HasClasses);
        Assert.Equal("To hand in", m.DueHeading);

        Assert.Equal(["Assignments", "Modules", "Announcements"], m.Links.Select(l => l.Label));
        Assert.Equal("1 to hand in · 4 done", m.Links[0].Detail);
        Assert.Equal("3 · 1 new", m.Links[2].Detail);
        foreach (var link in m.Links) m.OpenLinkCommand.Execute(link);
        m.AllLecturesCommand.Execute(null);
        Assert.Equal(["tab:Assignments", "tab:Modules", "tab:Announcements", "lectures:CS 101"], opened);
    }

    [Fact]
    public void A_club_with_no_Canvas_and_no_lectures_still_has_a_home()
    {
        var facts = new ClassHomeFacts("Chess club", Brushes.Blue, 0, null, null, null, null);
        var m = OverviewModel.ForClass(facts, Sources());
        m.AddLinks(facts, () => { }, null);

        Assert.Equal("0 lectures", m.Subtitle);
        Assert.False(m.ShowDue);
        Assert.True(m.NoLectures);
        Assert.Equal("No lectures in Chess club yet. Record one and it lands here.", m.LecturesEmpty);
        Assert.False(m.CanSeeAllLectures);
        // Its calendar shows (nothing of it this week); with no Canvas there are no Canvas cards.
        Assert.True(m.ShowEvents);
        Assert.Equal("No Chess club on your calendar this week.", m.EventsEmpty);
        Assert.Empty(m.Links);
        Assert.False(m.HasLinks);
        Assert.Equal(["0 Lectures"], m.Stats.Select(x => $"{x.Value} {x.Label}"));
    }

    [Theory]
    [InlineData(4, "Good evening")]
    [InlineData(5, "Good morning")]
    [InlineData(11, "Good morning")]
    [InlineData(12, "Good afternoon")]
    [InlineData(18, "Good evening")]
    public void The_greeting_follows_the_clock(int hour, string said) =>
        Assert.Equal(said, OverviewModel.Greeting(new DateTimeOffset(2026, 9, 30, hour, 0, 0, TimeSpan.FromHours(-7)), Zone));

    [Fact]
    public void An_overview_takes_the_list_and_the_page_beside_it_and_goes_narrow_with_the_window()
    {
        var m = new LibraryModel();
        Assert.True(m.ShowListColumn);
        m.Overview = OverviewModel.Home(Sources());
        Assert.True(m.ShowOverview);
        Assert.False(m.ShowListColumn);
        Assert.False(m.ShowDetailColumn);
        Assert.False(m.ShowLectureTools);

        m.Narrow = true;
        Assert.True(m.Overview.Narrow);
        Assert.Equal(0, m.Overview.SideColumn);
        Assert.Equal(1, m.Overview.SideRow);
        Assert.Equal(2, m.Overview.CardColumns);
        Assert.Equal(3, m.Overview.SideSpan);
        var next = OverviewModel.Home(Sources());
        m.Overview = next;
        Assert.True(next.Narrow);

        m.Overview = null;
        Assert.True(m.ShowListColumn);
    }

    static Window Host(Control content, SkinKind skin, double width, double height)
    {
        ((App)Application.Current!).UseSkin(skin);
        var w = new Window { Width = width, Height = height, RequestedThemeVariant = ThemeVariant.Light, Content = content };
        w.Show();
        Dispatcher.UIThread.RunJobs();
        w.UpdateLayout();
        return w;
    }

    static Rect In(Visual v, Visual root) => new(v.TranslatePoint(default, root)!.Value, v.Bounds.Size);

    [AvaloniaTheory]
    [InlineData(SkinKind.Mac, 1280, false)]
    [InlineData(SkinKind.Mac, 860, true)]
    [InlineData(SkinKind.Win, 1280, false)]
    [InlineData(SkinKind.Win, 860, true)]
    public void Home_sits_beside_the_sidebar_with_its_sections_side_by_side_or_stacked(SkinKind skin, double width, bool narrow)
    {
        var m = Demo.Overview(narrow: narrow);
        Control view = skin == SkinKind.Mac ? new MacLibrary { DataContext = m } : new WinLibrary { DataContext = m };
        var w = Host(view, skin, width, 800);
        try
        {
            var home = view.GetVisualDescendants().OfType<Button>().First(b => b.Name == "HomeRow");
            Assert.True(home.IsEffectivelyVisible);
            Assert.Contains("selected", home.Classes);
            var sidebar = view.GetVisualDescendants().OfType<Control>().First(c => c.Name == "Sidebar");
            var page = view.GetVisualDescendants().OfType<UserControl>().First(u => u is MacOverview or WinOverview);
            Assert.True(page.IsEffectivelyVisible);
            Assert.False(view.GetVisualDescendants().OfType<Control>().First(c => c.Name == "LectureList").IsEffectivelyVisible);
            // The page starts past the sidebar and runs to the window's edge.
            Assert.True(In(page, view).Left >= In(sidebar, view).Right - 0.5, $"{skin}: the overview starts under the sidebar");
            Assert.True(In(page, view).Right >= width - 2, $"{skin}: the overview stops short of the window's edge");

            var recent = page.GetVisualDescendants().OfType<StackPanel>().First(p => p.Name == "Recent");
            var due = page.GetVisualDescendants().OfType<StackPanel>().First(p => p.Name == "DueSoon");
            if (narrow)
            {
                Assert.True(In(due, view).Top >= In(recent, view).Bottom, $"{skin}: narrow, what's due goes under the lectures");
                Assert.True(Math.Abs(In(due, view).Width - In(recent, view).Width) < 1, $"{skin}: narrow, what's due is as wide as the lectures");
            }
            else
            {
                Assert.True(In(due, view).Left >= In(recent, view).Right, $"{skin}: wide, what's due sits beside the lectures");
                Assert.True(Math.Abs(In(due, view).Top - In(recent, view).Top) < 1, $"{skin}: side by side, they start level");
            }

            // Every card of a class is the same width, and fits the page.
            var cards = page.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("ov-card")).ToList();
            Assert.Equal(4, cards.Count);
            Assert.True(cards.Select(c => c.Bounds.Width).Distinct().Count() == 1, $"{skin}: the class cards differ in width");
            Assert.All(cards, c => Assert.True(In(c, view).Right <= In(page, view).Right + 0.5, $"{skin}: a card runs off the page"));
            Assert.Contains(page.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Lab 3 write-up · Missing" && t.IsEffectivelyVisible);
        }
        finally
        {
            w.Close();
            ((App)Application.Current!).UseSkin(SkinKind.Mac);
        }
    }

    [AvaloniaTheory]
    [InlineData(SkinKind.Mac)]
    [InlineData(SkinKind.Win)]
    public void A_class_home_selects_its_class_and_shows_its_ways_in(SkinKind skin)
    {
        var m = Demo.Overview("CS 101");
        Control view = skin == SkinKind.Mac ? new MacLibrary { DataContext = m } : new WinLibrary { DataContext = m };
        var w = Host(view, skin, 1280, 800);
        try
        {
            Assert.DoesNotContain("selected", view.GetVisualDescendants().OfType<Button>().First(b => b.Name == "HomeRow").Classes);
            var page = view.GetVisualDescendants().OfType<UserControl>().First(u => u is MacOverview or WinOverview);
            var words = page.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible).Select(t => t.Text).ToList();
            Assert.Contains("On Canvas", words);
            Assert.Contains("Assignments", words);
            Assert.Contains("To hand in", words);
            Assert.DoesNotContain("Classes", words);
            Assert.Contains("See all", words); // the class's lectures, beside Recent lectures
        }
        finally
        {
            w.Close();
            ((App)Application.Current!).UseSkin(SkinKind.Mac);
        }
    }

    /// <summary>The window 64 px in on the design's ground, as every shot of it is.</summary>
    static readonly Size Wide = new(1408, 928);

    [AvaloniaFact]
    public void Overview_shots()
    {
        foreach (var t in new[] { ThemeVariant.Light, ThemeVariant.Dark })
        {
            Shot.Take("mac-40-home", SkinKind.Mac, t, () => new MacLibrary { DataContext = Demo.Overview(), Width = 1280, Height = 800 }, size: Wide);
            Shot.Take("win-40-home", SkinKind.Win, t, () => new WinLibrary { DataContext = Demo.Overview(), Width = 1280, Height = 800 }, size: Wide);
            Shot.Take("mac-41-class-home", SkinKind.Mac, t, () => new MacLibrary { DataContext = Demo.Overview("CS 101"), Width = 1280, Height = 800 }, size: Wide);
            Shot.Take("win-41-class-home", SkinKind.Win, t, () => new WinLibrary { DataContext = Demo.Overview("CS 101"), Width = 1280, Height = 800 }, size: Wide);
            Shot.Take("mac-42-home-narrow", SkinKind.Mac, t, () => new MacLibrary { DataContext = Demo.Overview(narrow: true), Width = 860, Height = 800 }, size: new Size(988, 928));
            Shot.Take("mac-43-club-home", SkinKind.Mac, t, () => new MacLibrary { DataContext = Demo.Overview("CS 101", canvas: false, calendars: false), Width = 1280, Height = 800 }, size: Wide);
        }
    }
}
