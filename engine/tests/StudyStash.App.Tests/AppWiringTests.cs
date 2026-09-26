using Avalonia.Headless.XUnit;
using StudyStash.App.Services;
using StudyStash.App.ViewModels;

namespace StudyStash.App.Tests;

/// <summary>The Canvas and AI screens as the app itself hosts them: the full app's columns, Settings' new sections,
/// the dropdown's next due and the quick panel's missing work.</summary>
public class AppWiringTests
{
    [Fact]
    public void The_list_column_is_the_designs_width_for_what_it_lists()
    {
        var m = new LibraryModel();
        Assert.Equal(Skin.Current == SkinKind.Mac ? 312 : 320, m.ListWidth);

        m.List = LibraryList.Due;
        Assert.Equal(340, m.ListWidth);
        Assert.True(m.ShowDueList);
        Assert.False(m.ShowLectures);

        m.List = LibraryList.CanvasClass;
        Assert.Equal(Skin.Current == SkinKind.Mac ? 340 : 360, m.ListWidth);
    }

    [Fact]
    public void A_narrow_window_shows_what_was_opened_in_the_lists_place_until_back()
    {
        var m = new LibraryModel { CanvasClass = new CanvasClassModel(CanvasFixtures.Context()) };
        m.Opened(); // wide: nothing moves
        Assert.False(m.NarrowDetail);
        Assert.True(m.ShowListColumn);
        Assert.True(m.ShowDetailColumn);
        Assert.Equal(ClassLayout.Tabs, m.CanvasClass.Layout);

        m.Narrow = true;
        Assert.Equal(ClassLayout.Sections, m.CanvasClass.Layout);
        Assert.True(double.IsNaN(m.ListWidth));
        Assert.True(m.ShowListColumn);
        Assert.False(m.ShowDetailColumn);

        m.Opened();
        Assert.False(m.ShowListColumn);
        Assert.True(m.ShowDetailColumn);
        Assert.Equal(1, m.DetailColumn);

        m.BackCommand.Execute(null);
        Assert.True(m.ShowListColumn);

        m.Opened();
        m.Narrow = false; // widening shows both again
        Assert.False(m.NarrowDetail);
        Assert.Equal(ClassLayout.Tabs, m.CanvasClass.Layout);
    }

    [Fact]
    public void An_assignment_or_a_canvas_page_takes_the_lectures_place_and_its_ask_bar_goes()
    {
        var m = new LibraryModel { Note = new NoteModel { Id = "l1", Title = "Recursion" }, Ask = AiDemo.AskIdle() };
        Assert.True(m.ShowLecturePage);
        Assert.True(m.HasAsk);

        m.Assignment = new AssignmentModel(CanvasFixtures.Context());
        Assert.False(m.ShowLecturePage);
        Assert.True(m.ShowAssignment);
        Assert.False(m.HasAsk);

        m.Assignment = null;
        m.Reader = new CanvasReaderModel(CanvasFixtures.Context());
        Assert.True(m.ShowReader);
        Assert.False(m.ShowLecturePage);

        m.Reader = null;
        m.Note = null;
        Assert.False(m.HasAsk); // nothing open to ask about
    }

    [AvaloniaFact]
    public async Task Settings_has_the_AI_and_Canvas_sections_and_each_reads_the_library_when_opened()
    {
        using var home = new TempHome();
        using var host = new AppHost(home.Path, log: _ => { });
        var ai = new FakeAiLibrary { Overview = AiDemo.Overview(), Access = AiDemo.Access() };
        using var model = SettingsModel.Make(host, ai: ai, canvas: CanvasFixtures.Context());

        Assert.Equal(["General", "Appearance", "Recording", "Library", "Classes", "AI engines", "AI tool access", "Canvas"],
            model.NavItems.Select(n => n.Label));
        Assert.Empty(ai.Calls); // nothing asked before a section opens

        model.Section = "AI";
        Assert.False(model.OnPlainPage);
        await Until(() => model.Engines.Engines.Count > 0);
        Assert.Contains("engines", ai.Calls);

        model.Section = "Access";
        await Until(() => ai.Calls.Contains("access"));
        Assert.True(model.OnAccess);

        model.Section = "Canvas";
        Assert.True(model.OnPlainPage);
        Assert.True(model.OnCanvas);
    }

    static async Task Until(Func<bool> done)
    {
        for (int i = 0; i < 100 && !done(); i++) await Task.Delay(10, TestContext.Current.CancellationToken);
        Assert.True(done());
    }

    [Fact]
    public void The_dropdown_shows_next_due_only_when_something_is()
    {
        var p = new PanelModel();
        Assert.False(p.HasNextDue);
        p.NextDue = CanvasQuick.NextDue(CanvasFixtures.Load<CanvasApi.DueResponse>("due"), CanvasFixtures.Zone, CanvasFixtures.Now, _ => { });
        Assert.True(p.HasNextDue);
        p.NextDue = null;
        Assert.False(p.HasNextDue);
    }

    [Fact]
    public void The_quick_panel_marks_missing_work()
    {
        var due = CanvasFixtures.Load<CanvasApi.DueResponse>("due");
        var rows = CanvasQuick.Rows(due, null, "due", CanvasFixtures.Zone, CanvasFixtures.Now, _ => Avalonia.Media.Brushes.Gray, _ => { }, () => { }, () => { });
        var missing = rows.Single(r => r.Title == "Reading response: Treaty of Versailles");
        Assert.True(missing.Strong);
        Assert.All(rows.Where(r => r != missing), r => Assert.False(r.Strong));
    }
}
