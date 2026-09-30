using StudyStash.App.Services;
using StudyStash.App.ViewModels;

namespace StudyStash.App.Tests;

/// <summary>CS 101's own page (design 10 tabs, design 11 sections): the header, the to-hand-in/done rows, the
/// modules tree, and opening a new announcement.</summary>
public class CanvasClassTests
{
    static CanvasApi.ClassRow Cs101() => CanvasFixtures.Load<List<CanvasApi.ClassRow>>("classes").Single(c => c.Class == "CS 101");

    static CanvasClassModel Model(out List<(string What, string Arg)> log, FakeLibrary? handler = null)
    {
        var l = new List<(string, string)>();
        log = l;
        return new CanvasClassModel(CanvasFixtures.Context(handler, home: null, log: l));
    }

    static CanvasClassModel Shown()
    {
        var model = Model(out _);
        model.Show(Cs101(),
            CanvasFixtures.Load<CanvasApi.AssignmentsResponse>("assignments-cs101"),
            CanvasFixtures.Load<CanvasApi.ModulesResponse>("modules-cs101"),
            CanvasFixtures.Load<CanvasApi.FilesResponse>("files-cs101"),
            CanvasFixtures.Load<CanvasApi.AnnouncementsResponse>("announcements-cs101"));
        return model;
    }

    // ---- header and Scout line ----

    [Fact]
    public void Header_reads_the_class_and_its_course()
    {
        var model = Shown();
        model.SetLectures([], 12);
        Assert.Equal("CS 101", model.Title);
        Assert.Equal("12 lectures · COMP 101 on Canvas", model.HeaderLine);
        Assert.Equal("Scout explored this course · 14 files saved · 20 Sep", model.ScoutLine);
        Assert.True(model.HasScoutLine);
    }

    [Fact]
    public void One_lecture_reads_singular()
    {
        var model = Shown();
        model.SetLectures([], 1);
        Assert.Equal("1 lecture · COMP 101 on Canvas", model.HeaderLine);
        Assert.Equal("All 1 lecture", model.AllLecturesText);
    }

    // ---- to-hand-in and done rows, exactly as design 10 draws them ----

    [Fact]
    public void To_hand_in_reads_the_design_s_words()
    {
        var row = Assert.Single(Shown().ToHandIn);
        Assert.Equal("Lab 3: recursion traces", row.Title);
        Assert.Equal("In 5 days", row.Right);
        Assert.False(row.Strong);
        Assert.Equal("20 pts · Tue 30 Sep, 11:59 PM", row.Sub);
    }

    [Fact]
    public void Done_rows_read_the_design_s_words()
    {
        var done = Shown().Done;
        Assert.Equal(4, done.Count);
        Assert.Equal(("Problem set 4", "18/20", "Graded Mon 22 Sep"), (done[0].Title, done[0].Right, done[0].Sub));
        Assert.Equal(("Problem set 3", "20/20", "Graded Mon 15 Sep"), (done[1].Title, done[1].Right, done[1].Sub));
        Assert.Equal(("Lab 2: tracing loops", "17/20", "Graded Sun 14 Sep · late"), (done[2].Title, done[2].Right, done[2].Sub));
        Assert.Equal(("Syllabus quiz", "Excused", "Excused · due Fri 5 Sep"), (done[3].Title, done[3].Right, done[3].Sub));
    }

    [Fact]
    public void Picking_an_assignment_selects_it_and_tells_the_host()
    {
        var model = Shown();
        (string Class, string Id)? picked = null;
        model.OnAssignment = (c, id) => picked = (c, id);
        var row = model.Done[0];
        row.SelectCommand.Execute(null);
        Assert.True(row.Selected);
        Assert.Equal(("CS 101", "9002"), picked);

        var other = model.Done[1];
        other.SelectCommand.Execute(null);
        Assert.False(row.Selected);
        Assert.True(other.Selected);
    }

    // ---- the modules tree, exactly as design 11 draws it ----

    [Fact]
    public void The_started_module_opens_on_its_own()
    {
        var modules = Shown().Modules;
        Assert.Equal(8, modules.Count);
        var week4 = modules[3];
        Assert.Equal("Week 4 · Recursion", week4.Name);
        Assert.True(week4.Expanded);
        var week3 = modules[2];
        Assert.Equal("Week 3 · Scope", week3.Name);
        Assert.False(week3.Expanded);
        Assert.Equal("5 items", week3.ItemsCountText);
    }

    [Fact]
    public void Week_4_s_items_read_the_design_s_glyphs_and_words()
    {
        var items = Shown().Modules[3].Items;
        Assert.Equal(4, items.Count);
        Assert.Equal(("picture_as_pdf", "recursion-slides.pdf", "PDF"), (items[0].Glyph, items[0].Title, items[0].Right));
        Assert.Equal(("description", "Lab 3 instructions", "Page"), (items[1].Glyph, items[1].Title, items[1].Right));
        // A quirk of the design's own mock: a saved Drive link gets its file's icon, a saved Box link keeps the
        // plain link glyph, even though both are equally "saved" in the data.
        Assert.Equal(("link", "Tracing worksheet", "Saved from Box"), (items[2].Glyph, items[2].Title, items[2].Right));
        Assert.Equal(("picture_as_pdf", "Stack diagrams handout", "Saved from Drive"), (items[3].Glyph, items[3].Title, items[3].Right));
    }

    [Fact]
    public void The_sections_preview_shows_only_the_current_module_and_the_one_before_it()
    {
        var model = Shown();
        var recent = model.RecentModules;
        Assert.Equal(2, recent.Count);
        Assert.Equal("Week 4 · Recursion", recent[0].Name);
        Assert.True(recent[0].Expanded);
        Assert.Equal("Week 3 · Scope", recent[1].Name);
        Assert.False(recent[1].Expanded);
        // it's the very same node the full Modules list holds, so toggling one moves the other too
        Assert.Same(model.Modules[3], recent[0]);
        Assert.Same(model.Modules[2], recent[1]);
    }

    [Fact]
    public void A_collapsed_module_opens_when_toggled()
    {
        var week3 = Shown().Modules[2];
        week3.ToggleCommand.Execute(null);
        Assert.True(week3.Expanded);
        week3.ToggleCommand.Execute(null);
        Assert.False(week3.Expanded);
    }

    // ---- Files and Announcements sections, collapsed by default ----

    [Fact]
    public void Files_and_announcements_start_collapsed_and_toggle()
    {
        var model = Shown();
        Assert.Equal(23, model.FilesCount);
        Assert.False(model.FilesExpanded);
        model.ToggleFilesCommand.Execute(null);
        Assert.True(model.FilesExpanded);

        Assert.Equal(4, model.AnnouncementsCount);
        Assert.Equal(1, model.AnnouncementsNew);
        Assert.Equal("4 · 1 new", model.AnnouncementsCountText);
        Assert.True(model.HasAnnouncementsNew);
        Assert.False(model.AnnouncementsExpanded);
        model.ToggleAnnouncementsCommand.Execute(null);
        Assert.True(model.AnnouncementsExpanded);
    }

    [Fact]
    public void Files_group_under_their_own_folder_label()
    {
        var files = Shown().Files;
        Assert.Equal(3, files.Count);
        Assert.Equal("Slides", files[0].FolderHeader);
        Assert.Null(files[1].FolderHeader); // same folder as the row before it — no repeated label
        Assert.Equal("Handouts", files[2].FolderHeader);
        Assert.Equal("500 KB", files[0].SizeText);
    }

    [Fact]
    public async Task Opening_the_new_announcement_marks_it_seen_and_the_count_drops()
    {
        var handler = new FakeLibrary().Json(HttpMethod.Post, "/api/v2/canvas/announcements/seen", "{}");
        var model = Model(out _, handler);
        model.Show(Cs101(), new CanvasApi.AssignmentsResponse(), new CanvasApi.ModulesResponse(), new CanvasApi.FilesResponse(),
            CanvasFixtures.Load<CanvasApi.AnnouncementsResponse>("announcements-cs101"));

        CanvasReaderModel? reader = null;
        model.OnReader = r => reader = r;
        var newRow = model.Announcements[0];
        Assert.True(newRow.New);
        newRow.OpenCommand.Execute(null);
        await Task.Delay(20, TestContext.Current.CancellationToken); // the seen POST fires and forgets

        Assert.NotNull(reader);
        Assert.Equal("Office hours moved this week", reader!.Title);
        Assert.Contains("Announcement", reader.Meta);
        Assert.Contains("Dr. Okafor", reader.Meta);
        Assert.False(newRow.New);
        Assert.Equal(0, model.AnnouncementsNew);
        Assert.Equal("4", model.AnnouncementsCountText);

        var sent = Assert.Single(handler.Requests, r => r.Path == "/api/v2/canvas/announcements/seen");
        Assert.Contains("\"an1\"", sent.Body);
    }

    [Fact]
    public void A_notifications_announcement_opens_in_its_reader_on_the_announcements_tab()
    {
        var model = Shown();
        CanvasReaderModel? reader = null;
        model.OnReader = r => reader = r;
        Assert.Equal(ClassTab.Lectures, model.Tab);

        Assert.True(model.OpenAnnouncement("an2"));
        Assert.Equal(ClassTab.Announcements, model.Tab);
        Assert.Equal("Lab 3 posted", reader!.Title);

        // One this class hasn't got (any more): nothing opens.
        reader = null;
        Assert.False(model.OpenAnnouncement("an99"));
        Assert.Null(reader);
    }

    [Fact]
    public void Reopening_an_already_read_announcement_posts_nothing_again()
    {
        var model = Shown();
        var read = model.Announcements[1]; // "Lab 3 posted", already read
        Assert.False(read.New);
        read.OpenCommand.Execute(null);
        Assert.Equal(1, model.AnnouncementsNew); // untouched
    }

    // ---- LoadAsync fetches all four endpoints and shapes them ----

    [Fact]
    public async Task LoadAsync_reads_all_four_endpoints_for_the_class()
    {
        var handler = new FakeLibrary()
            .Json(HttpMethod.Get, "/api/v2/canvas/assignments", "assignments-cs101")
            .Json(HttpMethod.Get, "/api/v2/canvas/modules", "modules-cs101")
            .Json(HttpMethod.Get, "/api/v2/canvas/files", "files-cs101")
            .Json(HttpMethod.Get, "/api/v2/canvas/announcements", "announcements-cs101");
        var model = Model(out _, handler);
        await model.LoadAsync(Cs101(), TestContext.Current.CancellationToken);
        Assert.Single(model.ToHandIn);
        Assert.Equal(8, model.ModulesCount);
        Assert.Equal(23, model.FilesCount);
        Assert.Equal(4, model.AnnouncementsCount);
    }
}
