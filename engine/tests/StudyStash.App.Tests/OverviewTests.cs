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
using StudyStash.App.ViewModels;
using StudyStash.App.Views;

namespace StudyStash.App.Tests;

/// <summary>Home, Due's home and each class's home: what they say, what a click on them opens, and how they sit in the
/// library window in both looks, wide and narrow.</summary>
public class OverviewTests
{
    static readonly TimeZoneInfo Zone = TimeZoneInfo.CreateCustomTimeZone("Test", TimeSpan.FromHours(-7), "Test", "Test");
    /// <summary>A Wednesday, two in the afternoon: the week is Monday 28 September to Sunday 4 October.</summary>
    static readonly DateTimeOffset Now = new(2026, 9, 30, 14, 0, 0, TimeSpan.FromHours(-7));

    static CanvasApi.Item Item(string cls, string id, string name, double inDays, string status = "open", bool missing = false) => new()
    {
        Class = cls, Id = id, Name = name, Kind = "assignment", DueAt = Now.AddDays(inDays), Status = status, Missing = missing,
    };

    static OverviewModel.LectureFacts Lecture(string id, string cls, int daysAgo, bool writing = false, string? summary = null) =>
        new(id, $"Lecture {id}", cls, Now.AddDays(-daysAgo), 3600, summary ?? $"About {id}.", writing);

    static OverviewModel.Sources Sources(List<string>? opened = null, bool canvas = true, bool calendars = true, CanvasApi.Item[]? week = null) => new()
    {
        Now = Now, Zone = Zone,
        Classes = [("CS 101", Skin.ClassDot(0), 3), ("BIO 110", Skin.ClassDot(1), 1), ("Chess club", Skin.ClassDot(2), 0)],
        Lectures = [Lecture("a", "CS 101", 0), Lecture("b", "BIO 110", 2, writing: true), Lecture("c", "CS 101", 9)],
        Due = canvas ? new CanvasApi.DueResponse
        {
            Groups =
            [
                new CanvasApi.Group { Key = "overdue", Items = [Item("BIO 110", "1", "Lab report", -2, "missing", missing: true)] },
                new CanvasApi.Group { Key = "week", Items = week ?? [Item("CS 101", "2", "Problem set", 1)] },
                new CanvasApi.Group { Key = "handed_in", Items = [Item("CS 101", "3", "Old quiz", -1, "submitted")] },
            ],
        } : null,
        Canvas = canvas ? [new CanvasApi.ClassRow { Class = "CS 101", Linked = true }] : [],
        // CS 101 met this morning (the lecture "a" was recorded in it) and BIO 110 meets tomorrow.
        Meetings = calendars ? [new(Now.AddHours(-1), Now.AddHours(1), "CS 101"), new(Now.AddDays(1), Now.AddDays(1).AddHours(1), "BIO 110")] : null,
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
    public void Home_is_the_date_one_sentence_the_week_and_the_newest_lectures()
    {
        var opened = new List<string>();
        var m = OverviewModel.Home(Sources(opened));
        Assert.True(m.IsHome);
        Assert.Equal("Wednesday, September 30", m.Title);
        Assert.Equal("Problem set is due tomorrow at 2:00 PM, and Lab report is late.", m.Lede);

        // Late first (only because something is), then Monday to Sunday; today is lifted out.
        Assert.Equal(["Late", "Mon", "Tue", "Today", "Thu", "Fri", "Sat", "Sun"], m.Week.Select(d => d.Name));
        Assert.Equal(["", "28", "29", "30", "1", "2", "3", "4"], m.Week.Select(d => d.Number));
        var late = m.Week[0];
        Assert.True(late.IsLate);
        Assert.Equal("Lab report", Assert.Single(late.Deadlines).Title);
        Assert.Equal("Was due Mon 28", late.Deadlines[0].When);
        Assert.True(late.Deadlines[0].Late);

        // What's due sits on its day with only its time; what was handed in stays on its day, struck through.
        var thursday = m.Week.Single(d => d.Name == "Thu");
        Assert.Equal(("Problem set", "2:00 PM", false), (thursday.Deadlines[0].Title, thursday.Deadlines[0].When, thursday.Deadlines[0].Done));
        var tuesday = m.Week.Single(d => d.Name == "Tue");
        Assert.Equal(("Old quiz", "Handed in", true), (tuesday.Deadlines[0].Title, tuesday.Deadlines[0].When, tuesday.Deadlines[0].Done));

        // Class meetings come from the calendars; the one a lecture was recorded in says so.
        var today = m.Week.Single(d => d.IsToday);
        Assert.Equal(new WeekMeeting("1:00", "CS 101", Skin.ClassDot(0), Recorded: true), Assert.Single(today.Meetings));
        Assert.False(Assert.Single(thursday.Meetings).Recorded);
        Assert.True(thursday.Divided);

        // The four newest lectures: when, the class (marked), a sentence, how long.
        Assert.Equal(["Lecture a", "Lecture b", "Lecture c"], m.Lectures.Select(l => l.Title));
        Assert.Equal(("Today", "1 h 00 min", "CS 101"), (m.Lectures[0].When, m.Lectures[0].Length, m.Lectures[0].ClassName));
        Assert.Equal(("Writing summary…", true), (m.Lectures[1].Summary, m.Lectures[1].Pending));
        Assert.Equal("Mon 21 Sep", m.Lectures[2].When);

        m.OpenLectureCommand.Execute(m.Lectures[0]);
        m.OpenDueCommand.Execute(thursday.Deadlines[0]);
        Assert.Equal(["lecture:CS 101/a", "assignment:CS 101/2"], opened);
    }

    [Fact]
    public void A_day_with_more_than_four_due_shows_three_and_how_many_more()
    {
        var opened = new List<string>();
        var busy = Enumerable.Range(0, 6).Select(n => Item("CS 101", $"w{n}", $"Task {n}", 1 + n / 100.0)).ToArray();
        var m = OverviewModel.Home(Sources(opened, week: busy));
        var thursday = m.Week.Single(d => d.Name == "Thu");
        Assert.Equal(3, thursday.Deadlines.Count);
        Assert.Equal("+3 more", thursday.More);
        m.OpenMoreCommand.Execute(thursday);
        Assert.Equal(["due"], opened);
        Assert.Equal("Six assignments due tomorrow, and Lab report is late.", m.Lede);
    }

    [Fact]
    public void With_nothing_due_the_week_says_so_and_without_Canvas_or_calendars_there_is_no_week()
    {
        var m = OverviewModel.Home(Sources() with { Due = new CanvasApi.DueResponse { Groups = [new CanvasApi.Group { Key = "later", Items = [Item("CS 101", "9", "Essay", 12)] }] } });
        Assert.Equal("Nothing due this week.", m.Lede);
        Assert.Equal("Nothing due this week.", m.WeekEmpty);
        Assert.Equal("Next up is Essay for CS 101, due Mon 12 Oct.", m.WeekNext);
        Assert.DoesNotContain(m.Week, d => d.IsLate);
        Assert.Equal(7, m.Week.Count);

        // No Canvas: nothing to hand in to tell of, so the sentence counts the library; the week has only meetings.
        var noCanvas = OverviewModel.Home(Sources(canvas: false));
        Assert.Equal("Six lectures in three classes, and notes being written for one.", noCanvas.Lede);
        Assert.True(noCanvas.ShowWeek);
        Assert.All(noCanvas.Week, d => Assert.Empty(d.Deadlines));

        // Neither: no week at all (no class meetings are made up).
        var bare = OverviewModel.Home(Sources(canvas: false, calendars: false));
        Assert.False(bare.ShowWeek);
    }

    [Fact]
    public void A_class_home_is_that_class_alone_with_the_ways_further_in()
    {
        var opened = new List<string>();
        const string name = "Theory of Computation and Compilers(CSCI4150.A)";
        var s = Sources(opened) with
        {
            Classes = [(name, Skin.ClassDot(0), 3)],
            Lectures = [Lecture("a", name, 0, summary: "An objective $c^T x$ to maximize, with $x_i \\ge 0$.")],
            Due = new CanvasApi.DueResponse { Groups = [new CanvasApi.Group { Key = "week", Items = [Item(name, "2", "CCHW2", 1), Item("BIO 110", "8", "Lab", 1)] }] },
            Meetings = [new(Now.AddDays(2), Now.AddDays(2).AddHours(1), name)],
        };
        var facts = new ClassHomeFacts(name, Skin.ClassDot(0), 3, null, null, "Automata, grammars and what can't be computed.",
            new CanvasApi.Counts { ToHandIn = 2, Done = 5, Modules = 6, Announcements = 4, AnnouncementsNew = 1 });
        var m = OverviewModel.ForClass(facts, s);
        // The registrar's code is off the title; it's in the line under it, with when the class next meets.
        Assert.Equal("Theory of Computation and Compilers", m.Title);
        Assert.Equal(name, m.ClassName);
        Assert.Equal("CSCI 4150 · 3 lectures · next meets Fri 2 Oct at 2:00", m.Lede);
        // Its own deadline only, saying the day (there's no column to say it); no class named (it's this one's page).
        var due = Assert.Single(m.Due);
        Assert.Equal(("CCHW2", "Tomorrow, 2:00 PM", ""), (due.Title, due.When, due.ClassLabel));
        // A summary's maths reads as characters, never as LaTeX with its dollars.
        Assert.Equal("An objective cᵀx to maximize, with xᵢ ≥ 0.", Assert.Single(m.Lectures).Summary);
        Assert.Equal("", m.Lectures[0].ClassName);
        Assert.Equal("All 3 lectures", m.AllLecturesWords);

        m.AddLinks(facts, () => { }, tab => opened.Add("tab:" + tab));
        Assert.Equal(["Assignments", "Modules", "Announcements"], m.Links.Select(l => l.Label));
        Assert.Equal("4 · 1 new", m.Links[2].Detail);
        m.OpenLinkCommand.Execute(m.Links[0]);
        m.AllLecturesCommand.Execute(null);
        Assert.Equal(["tab:Assignments", "lectures:" + name], opened);
    }

    [Fact]
    public void Dues_home_is_day_by_day_with_what_is_late_first()
    {
        var m = OverviewModel.ForDue(Sources(week: [Item("CS 101", "2", "Problem set", 1), Item("CS 101", "4", "Reading", 1.1), Item("BIO 110", "5", "Quiz", 9)]));
        Assert.Equal("Four to hand in, one of them late.", m.Lede);
        Assert.Equal(["Late", "Tomorrow", "Fri 9 Oct"], m.DueGroups.Select(g => g.Heading));
        Assert.True(m.DueGroups[0].Late);
        Assert.Equal(("Lab report", "Was due Mon 28", "BIO 110"), (m.DueGroups[0].Items[0].Title, m.DueGroups[0].Items[0].When, m.DueGroups[0].Items[0].ClassLabel));
        Assert.Equal(["Problem set", "Reading"], m.DueGroups[1].Items.Select(i => i.Title));
        Assert.Equal("2:00 PM", m.DueGroups[1].Items[0].When);
    }

    [Theory]
    [InlineData("Theory of Computation and Compilers(CSCI4150.A)", "Theory of Computation and Compilers", "CSCI 4150", "Theory")]
    [InlineData("Machine and Deep Learning", "Machine and Deep Learning", "", "Machine")]
    [InlineData("Web Application Development", "Web Application Development", "", "Web Application")]
    [InlineData("Data Mining", "Data Mining", "", "Data Mining")]
    [InlineData("Introduction to Organic Chemistry(CHEM2310.A)", "Introduction to Organic Chemistry", "CHEM 2310", "Organic Chemistry")]
    [InlineData("CS 101", "CS 101", "", "CS 101")]
    public void A_class_is_shown_by_its_name_without_the_registrars_code(string name, string display, string code, string brief)
    {
        Assert.Equal(display, ClassNames.Display(name));
        Assert.Equal(code, ClassNames.Code(name));
        Assert.Equal(brief, ClassNames.Short(name));
    }

    [Theory]
    [InlineData("A coupled system $b = Ax + s$ with objective $c^T x$.", "A coupled system b = Ax + s with objective cᵀx.")]
    [InlineData("Runs in $O(n^2)$ time, $O(\\log n)$ space.", "Runs in O(n²) time, O(log n) space.")]
    [InlineData("Minimise \\(\\sum_i x_i^2\\) over $\\mathbb{R}^n$.", "Minimise ∑ᵢ xᵢ² over ℝⁿ.")]
    [InlineData("Costs $5 each, no maths here.", "Costs $5 each, no maths here.")]
    public void A_summary_never_shows_raw_LaTeX(string summary, string shown) => Assert.Equal(shown, PlainMath.Inline(summary));

    [Fact]
    public void The_record_button_follows_the_recording()
    {
        var m = OverviewModel.Home(Sources());
        m.OnRecord = () => { };
        Assert.Equal(("Record", true, false, false), (m.RecordWords, m.Idle, m.Running, m.Paused));
        var recorder = new RecorderModel { ClassName = "Theory of Computation and Compilers(CSCI4150.A)", Elapsed = "12:04" };
        m.Recorder = recorder;
        m.Recording = true;
        // The class by its short name, the time beside it; a screen reader hears the time too.
        Assert.Equal(("Theory", "12:04", true), (m.RecordWords, m.Elapsed, m.Running));
        Assert.Equal("Recording 12:04: stop", m.RecordTip);
        recorder.IsPaused = true;
        Assert.Equal(("Paused", true, false), (m.RecordWords, m.Paused, m.Running));
        m.Recorder = null;
        m.Recording = false;
        Assert.Equal("Record", m.RecordWords);
    }

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
        Assert.Equal((0, 1), (m.Overview.ActionsColumn, m.Overview.ActionsRow));
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
    public void Home_sits_beside_the_sidebar_as_one_page_with_the_week_and_the_lectures(SkinKind skin, double width, bool narrow)
    {
        var m = Demo.Semester(narrow: narrow);
        Control view = skin == SkinKind.Mac ? new MacLibrary { DataContext = m } : new WinLibrary { DataContext = m };
        var w = Host(view, skin, width, 860);
        try
        {
            var home = view.GetVisualDescendants().OfType<Button>().First(b => b.Name == "HomeRow");
            Assert.True(home.IsEffectivelyVisible);
            Assert.Contains("selected", home.Classes);
            var sidebar = view.GetVisualDescendants().OfType<Control>().First(c => c.Name == "Sidebar");
            var page = view.GetVisualDescendants().OfType<UserControl>().First(u => u is MacOverview or WinOverview);
            Assert.True(page.IsEffectivelyVisible);
            Assert.False(view.GetVisualDescendants().OfType<Control>().First(c => c.Name == "LectureList").IsEffectivelyVisible);
            // The page starts past the sidebar and stops at the window's own margin.
            Assert.True(In(page, view).Left >= In(sidebar, view).Right - 0.5, $"{skin}: the overview starts under the sidebar");
            Assert.True(In(page, view).Right >= width - 10, $"{skin}: the overview stops short of the window's edge");

            // No stat tiles, no greeting, no class cards: the date, the week, then the lectures under it.
            var words = page.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible).Select(t => t.Text).ToList();
            Assert.Contains("Monday, October 12", words);
            Assert.Contains("Two assignments due Wednesday, and Asynchronous Lecture 5 is late.", words);
            Assert.DoesNotContain("Good morning", words);
            Assert.DoesNotContain("To hand in", words);
            var week = page.GetVisualDescendants().OfType<Border>().First(b => b.Name == "Week");
            var recent = page.GetVisualDescendants().OfType<StackPanel>().First(p => p.Name == "Recent");
            Assert.True(In(recent, view).Top >= In(week, view).Bottom, $"{skin}: the lectures start over the week");
            // Late and seven days, the same height; today's column stands a little proud of the grid.
            var days = week.GetVisualDescendants().OfType<Border>().Where(b => b.Classes.Contains("day")).ToList();
            Assert.Equal(8, days.Count);
            var today = days.Single(d => d.Classes.Contains("today"));
            Assert.True(In(today, view).Top < In(week, view).Top && In(today, view).Bottom > In(week, view).Bottom, $"{skin}: today isn't lifted out of the week");
            // Each class's name is without its registrar's code, in the sidebar and under a lecture.
            Assert.DoesNotContain(view.GetVisualDescendants().OfType<TextBlock>(), t => t.IsEffectivelyVisible && t.Text?.Contains("CSCI4150") == true);
            Assert.Contains(page.GetVisualDescendants().OfType<Highlight>(), h => h.Text == "Asynchronous Lecture 5" && h.IsEffectivelyVisible);
            // Ask is docked at the page's foot and never past its sides.
            var ask = page.GetVisualDescendants().OfType<Border>().First(b => b.Classes.Contains("ask"));
            Assert.True(In(ask, view).Left >= In(page, view).Left && In(ask, view).Right <= In(page, view).Right, $"{skin}: Ask runs off the page");
            Assert.True(In(ask, view).Bottom <= In(page, view).Bottom, $"{skin}: Ask hangs under the page");
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
            Assert.Contains("Upcoming deadlines", words);
            Assert.Contains("On Canvas", words);
            Assert.Contains("Assignments", words);
            Assert.Contains("Lectures", words);
            Assert.DoesNotContain("This week", words);
            Assert.True(page.GetVisualDescendants().OfType<Button>().First(b => b.Name == "AllLectures").IsEffectivelyVisible); // the class's lectures by week
        }
        finally
        {
            w.Close();
            ((App)Application.Current!).UseSkin(SkinKind.Mac);
        }
    }

    /// <summary>A long class name wraps beside the page's buttons, never under them (Record once repeated the name, and
    /// covered it).</summary>
    [AvaloniaTheory]
    [InlineData(SkinKind.Mac)]
    [InlineData(SkinKind.Win)]
    public void A_long_class_name_wraps_beside_the_buttons_not_under_them(SkinKind skin)
    {
        var m = Demo.Overview("Differential Equations and Linear Algebra for Engineers(MATH2250.B)");
        m.Overview!.OnRecord = () => { };
        Control view = skin == SkinKind.Mac ? new MacLibrary { DataContext = m } : new WinLibrary { DataContext = m };
        var w = Host(view, skin, 1000, 800);
        try
        {
            var page = view.GetVisualDescendants().OfType<UserControl>().First(u => u is MacOverview or WinOverview);
            var title = page.GetVisualDescendants().OfType<TextBlock>().First(t => t.Name == "Title");
            var record = page.GetVisualDescendants().OfType<Button>().First(b => b.Name == "Record");
            Assert.True(record.IsEffectivelyVisible);
            Assert.True(In(title, view).Right <= In(record, view).Left, $"{skin}: the title runs under Record");
            Assert.True(In(record, view).Right <= In(page, view).Right, $"{skin}: Record runs off the page");
        }
        finally
        {
            w.Close();
            ((App)Application.Current!).UseSkin(SkinKind.Mac);
        }
    }

    /// <summary>The window alone, edge to edge, the size of the design's own screenshots (1280 × 860 at 1x; theirs
    /// are at 2x): to lay one over the other. No drawn traffic lights or window corners, as the design's has none.</summary>
    static void Plain(string name, ThemeVariant variant, Func<LibraryModel> model, double width = 1280, double height = 860)
    {
        Skin.UseTheme(ColourThemes.Default);
        ((App)Application.Current!).UseSkin(SkinKind.Mac);
        var m = model();
        m.DrawChrome = false;
        var window = new Window { Width = width, Height = height, RequestedThemeVariant = variant, Content = new MacLibrary { DataContext = m } };
        Look.Apply(window);
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("nothing rendered");
        using (var file = File.Create(Path.Combine(Shot.Dir, $"{name}-{(variant == ThemeVariant.Dark ? "dark" : "light")}.png")))
            frame.Save(file, Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
        window.Close();
    }

    [AvaloniaFact]
    public void Design_shots()
    {
        foreach (var t in new[] { ThemeVariant.Light, ThemeVariant.Dark })
        {
            Plain("semester-home", t, () => Demo.Semester());
            Plain("semester-home-recording", t, () => Demo.Semester(recording: true));
        }
    }

    /// <summary>The window 64 px in on the design's ground, as every shot of it is.</summary>
    static readonly Size Wide = new(1408, 988);

    [AvaloniaFact]
    public void Overview_shots()
    {
        foreach (var t in new[] { ThemeVariant.Light, ThemeVariant.Dark })
        {
            // The design's own Home, at the size of its screenshots (1280 × 860).
            Shot.Take("mac-40-home", SkinKind.Mac, t, () => new MacLibrary { DataContext = Demo.Semester(), Width = 1280, Height = 860 }, size: Wide);
            Shot.Take("win-40-home", SkinKind.Win, t, () => new WinLibrary { DataContext = Demo.Semester(), Width = 1280, Height = 860 }, size: Wide);
            Shot.Take("mac-40-home-recording", SkinKind.Mac, t, () => new MacLibrary { DataContext = Demo.Semester(recording: true), Width = 1280, Height = 860 }, size: Wide);
            Shot.Take("mac-41-class-home", SkinKind.Mac, t, () => new MacLibrary { DataContext = Demo.Overview("CS 101"), Width = 1280, Height = 860 }, size: Wide);
            Shot.Take("win-41-class-home", SkinKind.Win, t, () => new WinLibrary { DataContext = Demo.Overview("CS 101"), Width = 1280, Height = 860 }, size: Wide);
            Shot.Take("mac-42-home-narrow", SkinKind.Mac, t, () => new MacLibrary { DataContext = Demo.Semester(narrow: true), Width = 860, Height = 860 }, size: new Size(988, 988));
            Shot.Take("mac-43-club-home", SkinKind.Mac, t, () => new MacLibrary { DataContext = Demo.Overview("CS 101", canvas: false, calendars: false), Width = 1280, Height = 860 }, size: Wide);
            Shot.Take("mac-44-due-home", SkinKind.Mac, t, () => new MacLibrary { DataContext = Demo.Overview("\u0001due"), Width = 1280, Height = 860 }, size: Wide);
        }
    }
}
