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

/// <summary>Home, Due and a folder's page, a note's page and a recording's: what they say, what a click on them does,
/// and how they sit in the library window in both looks, wide and narrow.</summary>
public class OverviewTests
{
    static readonly TimeZoneInfo Zone = TimeZoneInfo.CreateCustomTimeZone("Test", TimeSpan.FromHours(-7), "Test", "Test");
    /// <summary>A Wednesday, two in the afternoon: the week is Monday 28 September to Sunday 4 October.</summary>
    static readonly DateTimeOffset Now = new(2026, 9, 30, 14, 0, 0, TimeSpan.FromHours(-7));
    static readonly DateTimeOffset Midnight = new(2026, 9, 30, 0, 0, 0, TimeSpan.FromHours(-7));

    /// <summary>Due at the end of a day, <paramref name="day"/> days from today.</summary>
    static CanvasApi.Item Item(string cls, string id, string name, int day, string status = "open", bool missing = false, double hour = 23 + 59 / 60.0) => new()
    {
        Class = cls, Id = id, Name = name, Kind = "assignment", DueAt = Midnight.AddDays(day).AddHours(hour), Status = status, Missing = missing,
    };

    static OverviewModel.LectureFacts Note(string id, string cls, int daysAgo, bool writing = false, string? summary = null) =>
        new(id, $"Note {id}", cls, Now.AddDays(-daysAgo), 3600, summary ?? $"About {id}.", writing, Now.AddDays(-daysAgo).ToString("o"));

    static OverviewModel.Event Event(string id, string title, string? cls, double inHours, double hours = 1) =>
        new(id, title, Now.AddHours(inHours), Now.AddHours(inHours + hours), cls);

    internal static OverviewModel.Sources Sources(List<string>? opened = null, bool canvas = true, bool calendars = true, CanvasApi.Item[]? week = null) => new()
    {
        Now = Now, Zone = Zone,
        Classes = [("CS 101", Skin.ClassDot(0), 3), ("BIO 110", Skin.ClassDot(1), 1), ("Chess club", Skin.ClassDot(2), 2)],
        GroupOf = c => c == "Chess club" ? "Clubs" : "",
        Lectures = [Note("a", "CS 101", 0), Note("u", "Unsorted", 1), Note("b", "BIO 110", 2, writing: true), Note("k", "Chess club", 6), Note("c", "CS 101", 9)],
        Due = canvas ? new CanvasApi.DueResponse
        {
            Groups =
            [
                new CanvasApi.Group { Key = "overdue", Items = [Item("BIO 110", "1", "Lab report", -2, "missing", missing: true)] },
                new CanvasApi.Group { Key = "week", Items = week ?? [Item("CS 101", "2", "Problem set", 0), Item("CS 101", "4", "Reading", 3, hour: 9)] },
                new CanvasApi.Group { Key = "later", Items = [Item("CS 101", "5", "Project", 40)] },
                new CanvasApi.Group { Key = "handed_in", Items = [Item("CS 101", "3", "Old quiz", -1, "submitted")] },
            ],
        } : null,
        Canvas = canvas ? [new CanvasApi.ClassRow { Class = "CS 101", Linked = true }] : [],
        // The chess club's officers meet in eleven minutes; CS 101 met this morning and meets again on Friday; BIO 110
        // is tomorrow; and a dentist's appointment that's nobody's folder is this evening.
        Events = calendars
            ? [Event("e0", "CS 101", "CS 101", -4), Event("e1", "Officers", "Chess club", 11 / 60.0), Event("e2", "Dentist", null, 4), Event("e3", "BIO 110", "BIO 110", 20),
               Event("e4", "CS 101", "CS 101", 44), Event("e5", "Chess club", "Chess club", 28)]
            : null,
        Unsorted = 1,
        Writing = 1,
        OpenClass = c => opened?.Add("folder:" + c),
        OpenLecture = (c, id) => opened?.Add($"note:{c}/{id}"),
        OpenAssignment = (c, id) => opened?.Add($"assignment:{c}/{id}"),
        OpenDue = c => opened?.Add("due:" + c),
        RecordEvent = id => opened?.Add("record:" + id),
    };

    [Fact]
    public void Home_is_the_date_what_is_pressing_what_is_up_next_and_every_note()
    {
        var opened = new List<string>();
        var m = OverviewModel.Home(Sources(opened));
        Assert.True(m.IsHome);
        Assert.Equal("Wednesday, September 30", m.Title);
        // The most pressing first: what's about to start, then what's due (the end of the day goes unsaid) and what's missing.
        Assert.Equal("Officers starts in 11 minutes. Problem set is due tonight.", m.Lede);
        // With nothing about to start, what's missing is said too.
        Assert.Equal("Problem set is due tonight, and Lab report is still missing.", OverviewModel.HomeLede(Sources(calendars: false), canvas: true));

        // Up next: the rest of today's and tomorrow's events, each a button that records it. No Record beside it.
        Assert.Equal(["Officers", "Dentist", "BIO 110", "Chess club"], m.UpNext.Select(e => e.Title));
        Assert.Equal([("2:11 PM", "In 11 minutes", true), ("6:00 PM", "Today", false), ("10:00 AM", "Tomorrow", false), ("6:00 PM", "Tomorrow", false)],
            m.UpNext.Select(e => (e.Time, e.Day, e.Soon)));
        Assert.Equal("Record Officers as a meeting, today at 2:11 PM", m.UpNext[0].Said);
        Assert.Equal("Record Dentist as a recording, today at 6:00 PM", m.UpNext[1].Said);
        Assert.Equal("Record BIO 110 as a lecture, tomorrow at 10:00 AM", m.UpNext[2].Said);
        // An event that's just its class shows the name once, as the highlight; one with a name of its own shows both.
        Assert.Equal((true, false, false), (m.UpNext[2].TitleIsFolder, m.UpNext[2].ShowTitle, m.UpNext[2].ShowFolder));
        Assert.Equal((false, true, true), (m.UpNext[0].TitleIsFolder, m.UpNext[0].ShowTitle, m.UpNext[0].ShowFolder));
        Assert.False(m.UpNext[1].HasFolder);
        m.RecordEventCommand.Execute(m.UpNext[0]);
        Assert.Equal(["record:e1"], opened);

        // Every note, newest first: each names its folder (a note in none says Unsorted), and what it's called follows.
        Assert.Equal("Notes", m.NotesHeading);
        Assert.Equal(["a", "u", "b", "k", "c"], m.Notes.Select(n => n.Id));
        Assert.Equal(["Today", "Yesterday", "Mon 28 Sep", "Thu 24 Sep", "Mon 21 Sep"], m.Notes.Select(n => n.When));
        Assert.Equal(["CS 101", "Unsorted", "BIO 110", "Chess club", "CS 101"], m.Notes.Select(n => n.FolderShown));
        Assert.Equal([Nouns.Lecture, Nouns.Recording, Nouns.Lecture, Nouns.Meeting, Nouns.Lecture], m.Notes.Select(n => n.Noun));
        Assert.Equal((true, false), (m.Notes[1].ShowUnsorted, m.Notes[1].ShowHighlight));
        Assert.Equal("In Unsorted. Change folder", m.Notes[1].PickerSaid);
        Assert.Equal("In CS 101. Change folder", m.Notes[0].PickerSaid);
        Assert.Equal(("Writing notes…", true), (m.Notes[2].Summary, m.Notes[2].Pending));
        Assert.True(m.Notes[0].First);
        m.OpenNoteCommand.Execute(m.Notes[3]);
        Assert.Equal("note:Chess club/k", opened[^1]);

        // The filter: all of them or the unsorted ones where they are; a folder goes to its page.
        Assert.Equal("All notes", m.FilterName);
        Assert.Equal(["All notes", "Unsorted", "", "Classes", "CS 101", "BIO 110", "Clubs", "Chess club"], m.Filters.Select(f => f.Name));
        Assert.True(m.Filters[0].Checked);
        Assert.Equal((1, true), (m.Filters[1].Count, m.Filters[1].ShowCount));
        bool? filtered = null;
        m.OnFilter = unsorted => filtered = unsorted;
        m.PickFilterCommand.Execute(m.Filters[1]);
        Assert.True(filtered);
        m.PickFilterCommand.Execute(m.Filters[7]);
        Assert.Equal("folder:Chess club", opened[^1]);
        var only = OverviewModel.Home(Sources() with { Lectures = [Note("u", "Unsorted", 1)] }, onlyUnsorted: true);
        Assert.Equal("Unsorted", only.FilterName);
        Assert.True(only.Filters[1].Checked);

        // More of them come a page at a time, under the ones showing; one that's moved or deleted leaves the list.
        m.Add([OverviewModel.Row(Note("z", "BIO 110", 30), Sources(), _ => Brushes.Gray, showFolder: true)], canLoadMore: false);
        Assert.Equal("z", m.Notes[^1].Id);
        Assert.False(m.Notes[^1].First);
        m.Remove("a");
        Assert.Equal("u", m.Notes[0].Id);
        Assert.True(m.Notes[0].First);
    }

    [Fact]
    public void Home_says_what_it_can_with_nothing_due_no_calendar_or_no_Canvas()
    {
        // Nothing left to hand in this week: it says so. No calendars: no Up next at all, rather than an empty one.
        var quiet = OverviewModel.Home(Sources(calendars: false, week: []) with { Due = new CanvasApi.DueResponse { Groups = [] } });
        Assert.Equal("Nothing due this week.", quiet.Lede);
        Assert.False(quiet.ShowUpNext);
        // Calendars with nothing left today or tomorrow: Up next says so.
        var free = OverviewModel.Home(Sources(canvas: false) with { Events = [] });
        Assert.True(free.NoUpNext);
        Assert.Equal("Nothing on your calendar for the rest of today or tomorrow.", free.UpNextEmpty);
        // Neither Canvas nor a calendar: the library is counted instead.
        Assert.Equal("Seven notes in three folders, and notes being written for one.", OverviewModel.Home(Sources(canvas: false, calendars: false)).Lede);
        // A library that writes no notes (setup's "No AI for now") doesn't say any are being written, or will land.
        var off = OverviewModel.Home(Sources(canvas: false, calendars: false) with { NotesOff = true });
        Assert.Equal("Seven notes in three folders, and one being filed.", off.Lede);
        Assert.Equal("Being filed", off.Notes[2].Summary);
        Assert.Equal("No notes yet. Record something and it lands here with its transcript.",
            OverviewModel.HomeLede(Sources(canvas: false, calendars: false) with { NotesOff = true, Classes = [], Unsorted = 0 }, canvas: false));
        // Something on right now is said first; later today, by its time.
        Assert.Equal("CS 101 is on now.", OverviewModel.EventSentence(Sources() with { Events = [Event("x", "CS 101", "CS 101", -0.5)] }));
        Assert.Equal("Dentist is at 6:00 PM.", OverviewModel.EventSentence(Sources() with { Events = [Event("x", "Dentist", null, 4)] }));
        Assert.Equal("", OverviewModel.EventSentence(Sources() with { Events = [Event("x", "BIO 110", "BIO 110", 20)] }));
        // Several due the same day are counted; a time that isn't the end of the day is said.
        Assert.Equal("Two assignments due Friday.", OverviewModel.DueSentence(Sources(week: [Item("CS 101", "2", "A", 2), Item("CS 101", "4", "B", 2)]) with { Events = null })
            .Replace(", and Lab report is still missing", ""));
        Assert.StartsWith("Quiz is due tomorrow at 9:00 AM", OverviewModel.DueSentence(Sources(week: [Item("CS 101", "2", "Quiz", 1, hour: 9)])));
    }

    [Fact]
    public void A_folders_page_is_its_notes_by_week_with_what_is_due_and_when_it_meets_beside_them()
    {
        var opened = new List<string>();
        var s = Sources(opened);
        var facts = new ClassHomeFacts("CS 101", Skin.ClassDot(0), 3, "CS 101", "Intro to CS", null, new CanvasApi.Counts { ToHandIn = 2, Done = 6, Modules = 5, Announcements = 3, AnnouncementsNew = 1 });
        var m = OverviewModel.ForClass(facts, s);
        Assert.True(m.IsFolder);
        Assert.Equal(("CS 101", Nouns.Lecture, "Lectures"), (m.Title, m.Noun, m.NotesHeading));
        // A class that goes by its code needs no code under its name; its days come from the calendar.
        Assert.Equal("On Canvas. Meets Wednesday and Friday at 10:00 AM.", m.Lede);
        Assert.Equal("CSCI 4220 on Canvas. Meets Wednesday and Friday at 10:00 AM.", OverviewModel.ForClass(facts with { Code = "CSCI 4220" }, s).Lede);
        // Its own notes, by week; no folder under each title (the page is the folder).
        Assert.Equal(["This week", "Last week"], m.Groups.Select(g => g.Label));
        Assert.Equal(["Wed 30 Sep", "Mon 21 Sep"], m.Groups.SelectMany(g => g.Notes).Select(n => n.When)); // the day itself, not "Today"
        Assert.Equal("August", OverviewModel.GroupOf(Now.AddDays(-40), s));
        Assert.Equal("December 2025", OverviewModel.GroupOf(Now.AddDays(-300), s));
        Assert.Equal([["a"], ["c"]], m.Groups.Select(g => g.Notes.Select(n => n.Id).ToArray()));
        Assert.All(m.Groups.SelectMany(g => g.Notes), n => Assert.False(n.ShowFolder));
        Assert.All(m.Groups, g => Assert.True(g.Notes[0].First));
        // The rail: what's due next (three at most; within a week is said heavier), and its lists on Canvas.
        Assert.Equal(["Problem set", "Reading", "Project"], m.Due.Select(d => d.Title));
        Assert.Equal(["Today, 11:59 PM", "Sat 3 Oct, in 3 days", "Mon 9 Nov"], m.Due.Select(d => d.When));
        Assert.Equal([true, true, false], m.Due.Select(d => d.Soon));
        m.OpenRailDueCommand.Execute(m.Due[1]);
        m.SeeCalendarCommand.Execute(null);
        Assert.Equal(["assignment:CS 101/4", "due:CS 101"], opened);
        var tabs = new List<ClassTab>();
        m.AddLinks(facts, tabs.Add);
        Assert.Equal(["Assignments", "Modules", "Announcements"], m.Links.Select(l => l.Label));
        foreach (var link in m.Links) m.OpenLinkCommand.Execute(link);
        Assert.Equal([ClassTab.Assignments, ClassTab.Modules, ClassTab.Announcements], tabs);
        Assert.Equal(("Next class", "Friday at 10:00 AM"), (m.NextHeading, m.NextText));
        Assert.True(m.HasRail);

        // A missing assignment leads the rail, in words, not colour alone.
        var bio = OverviewModel.ForClass(new ClassHomeFacts("BIO 110", Skin.ClassDot(1), 1, null, null, null, new CanvasApi.Counts()), s);
        Assert.Equal(("Lab report", "Was due Mon 28 Sep", true), (bio.Due[0].Title, bio.Due[0].When, bio.Due[0].Late));
        Assert.Contains("missing", bio.Due[0].Said);

        // A club: its recordings are meetings, everywhere the page names them, and its next one is named when it has a name.
        var club = OverviewModel.ForClass(new ClassHomeFacts("Chess club", Skin.ClassDot(2), 2, null, null, null, null, "Clubs"), s);
        Assert.Equal((Nouns.Meeting, "Meetings", "Next meeting"), (club.Noun, club.NotesHeading, club.NextHeading));
        Assert.Equal("Club meetings, Thursdays at 6:00 PM.", club.Lede);
        Assert.Equal("Today at 2:11 PM, Officers.", club.NextText);
        Assert.Equal("No meetings yet. Record one here, or attach a recording.", club.NotesEmpty);
        Assert.Empty(club.Due);
        Assert.StartsWith("Record a meeting into Chess club", club.RecordTip);
        // With no calendar to say when it meets, it says how many there are.
        Assert.Equal("Two meetings so far.", OverviewModel.ForClass(new ClassHomeFacts("Chess club", Skin.ClassDot(2), 2, null, null, null, null, "Clubs"), Sources(calendars: false)).Lede);
    }

    [Fact]
    public void Due_is_a_calendar_of_the_month_with_what_is_missing_and_what_was_handed_in()
    {
        var opened = new List<string>();
        var page = OverviewModel.ForDue(Sources(opened, week:
            [Item("CS 101", "2", "Problem set", 0), Item("CS 101", "4", "Reading", 3, hour: 9), Item("BIO 110", "6", "Quiz", 3), Item("CS 101", "7", "Essay", 3)]));
        Assert.Equal(("Due", "Problem set is due tonight, and Lab report is still missing."), (page.Title, page.Lede));
        Assert.False(page.HasAsk);
        var cal = page.Calendar!;
        // September 2026: Monday first, five whole weeks, the days outside the month quieter, the 1st naming its month.
        Assert.Equal("September 2026", cal.Heading);
        Assert.Equal(35, cal.Cells.Count);
        Assert.Equal(("31", true), (cal.Cells[0].Number, cal.Cells[0].IsOut));
        Assert.Equal("1 Sep", cal.Cells[1].Number);
        Assert.Equal("1 Oct", cal.Cells[31].Number);
        CalendarCell Day(int month, int day) => cal.Cells.Single(c => c.Date.Month == month && c.Date.Day == day);
        Assert.True(Day(9, 30).IsToday);
        // Today's says "Tonight"; one past its day and not handed in says Missing; one handed in is struck through.
        var tonight = Assert.Single(Day(9, 30).Shown);
        Assert.Equal(("Problem set", "Tonight 11:59 PM", false, false), (tonight.Title, tonight.Sub, tonight.Missing, tonight.Done));
        var missing = Assert.Single(Day(9, 28).Shown);
        Assert.Equal(("Lab report", "Missing", true), (missing.Title, missing.Sub, missing.Missing));
        Assert.Equal("Lab report, BIO 110, due Mon 28 Sep at 11:59 PM, Missing", missing.Said);
        var done = Assert.Single(Day(9, 29).Shown);
        Assert.Equal(("Old quiz", "", true), (done.Title, done.Sub, done.Done));
        Assert.EndsWith("Handed in", done.Said);
        // A day shows two, the rest behind "1 more"; a time is said only when it isn't the end of the day.
        var full = Day(10, 3);
        Assert.Equal(["Reading", "Quiz"], full.Shown.Select(i => i.Title));
        Assert.Equal(["9:00 AM", ""], full.Shown.Select(i => i.Sub));
        Assert.Equal(("1 more", "Essay"), (full.MoreWords, full.More.Single().Title));
        cal.OpenCommand.Execute(full.More[0]);
        Assert.Equal(["assignment:CS 101/7"], opened);

        // One class's alone; the menu offers every class that has something due.
        Assert.Equal(["All classes", "CS 101", "BIO 110"], cal.Filters.Select(f => f.Name));
        cal.Filters[2].Pick.Execute(null);
        Assert.Equal(("BIO 110", "BIO 110"), (cal.Class, cal.FilterName));
        Assert.Empty(Day(9, 30).Shown);
        Assert.Single(Day(9, 28).Shown);
        cal.Class = null;

        // Next month and back; Today comes home.
        cal.NextCommand.Execute(null);
        Assert.Equal("October 2026", cal.Heading);
        Assert.Equal("Project", OverviewModel.ForDue(Sources()).Calendar is { } far && Step(far, 2) ? far.Cells.SelectMany(c => c.Shown).Single(i => i.Title == "Project").Title : "");
        cal.GoTodayCommand.Execute(null);
        Assert.Equal("September 2026", cal.Heading);

        // The week: the planner, what's missing from before it in a column of its own, then Monday to Sunday.
        cal.ShowWeekCommand.Execute(null);
        Assert.Equal("28 Sep – 4 Oct", cal.Heading);
        Assert.Equal(["Mon", "Tue", "Today", "Thu", "Fri", "Sat", "Sun"], cal.Week.Select(d => d.Name));
        Assert.Equal(["Problem set"], cal.Week[2].Deadlines.Select(d => d.Title));
        Assert.Equal(["10:00", "2:11"], cal.Week[2].Meetings.Select(e => e.Time)); // the class, and the club's officers
        Assert.False(cal.Week[2].Meetings[1].Recorded);
        // Opened for one class (a folder's "See these on the calendar"), it starts on that class.
        Assert.Equal("CS 101", OverviewModel.ForDue(Sources(), "CS 101").Calendar!.Class);

        static bool Step(DueCalendarModel c, int months)
        {
            for (int i = 0; i < months; i++) c.NextCommand.Execute(null);
            return true;
        }
    }

    [Fact]
    public void A_recording_is_called_by_where_it_is_filed()
    {
        Assert.Equal(Nouns.Lecture, Nouns.For("CS 101", ""));
        Assert.Equal(Nouns.Meeting, Nouns.For("Chess club", "Clubs"));
        Assert.Equal(Nouns.Meeting, Nouns.For("Intellievent", "Work"));
        Assert.Equal(Nouns.Recording, Nouns.For("", ""));
        Assert.Equal(Nouns.Recording, Nouns.For("Unsorted", ""));
        Assert.Equal("Meetings", Nouns.Section(Nouns.Meeting));

        // A note's page says what it is over its title, and its ask bar asks about it by that name.
        var meeting = new NoteModel { ClassName = "Chess club", Noun = Nouns.Meeting, When = "Thursday 24 September at 6:00 PM, 41 min" };
        Assert.Equal("Meeting, Thursday 24 September at 6:00 PM, 41 min", meeting.Crumb);
        var loose = new NoteModel { ClassName = "Unsorted", Noun = Nouns.Recording, When = "Saturday 10 October at 4:12 PM, 18 min" };
        Assert.Equal((true, "Unsorted", "Recording, Saturday 10 October at 4:12 PM, 18 min"), (loose.IsUnsorted, loose.ClassShown, loose.Crumb));
        var ask = AiDemo.AskIdle();
        ask.NameScopes(Nouns.Meeting, "Chess club");
        Assert.Equal(("Ask about this meeting", "This meeting"), (ask.Placeholder, ask.ScopeName));
        ask.Scope = "class";
        Assert.Equal(("Ask about Chess club", "Chess club"), (ask.Placeholder, ask.ScopeName));
        ask.Scope = "all";
        Assert.Equal(("Ask anything about your notes", "All notes"), (ask.Placeholder, ask.ScopeName));
        ask.Live = () => "what's been said";
        Assert.Equal("Ask about what's been said so far", ask.Placeholder);
        // Deleting asks by the same name.
        Assert.Equal("Delete this meeting?", new LectureDeletion("k", "Officers", "Chess club", Nouns.Meeting).Ask);
    }

    [Fact]
    public void What_the_student_typed_stays_theirs_in_the_notes()
    {
        const string notes = "### ==History==\n- ==Attention Is All You Need, 2017== The paper that introduced the transformer.\n- A claim the class treated with skepticism.\n\n"
            + "### Tokenizers\n- Text is split into tokens.\n- ==GPT = decoder only== Each token is fed back in.\n\n```\n==not a mark==\n```";
        Assert.True(TypedNotes.HasMarks(notes));
        Assert.False(TypedNotes.HasMarks("### History\n- 2 == 2 is true"));
        // The editor shows the words without the marks.
        Assert.StartsWith("### History\n- Attention Is All You Need, 2017 The paper", TypedNotes.Unmarked(notes));
        // Only my notes: each of the student's lines with its heading or bullet, in order, and nothing added.
        Assert.Equal("### ==History==\n- ==Attention Is All You Need, 2017==\n- ==GPT = decoder only==", TypedNotes.Only(notes));
        // With no notes written yet, what was typed reads as all theirs.
        Assert.Equal("# ==History==\n- ==Attention, 2017==\n\n- ==q · k==", TypedNotes.AllMine("# History\n- Attention, 2017\n\n- q · k"));
        // After an edit: a line left alone keeps its marks, a line changed or added is the student's, code is left alone.
        string edited = TypedNotes.Unmarked(notes).Replace("- Text is split into tokens.", "- Text is split into subword tokens.") + "\n- on the exam";
        string kept = TypedNotes.Remark(edited, notes);
        Assert.Contains("- ==Attention Is All You Need, 2017== The paper that introduced the transformer.", kept);
        Assert.Contains("- ==Text is split into subword tokens.==", kept);
        Assert.Contains("- ==on the exam==", kept);
        Assert.Contains("- A claim the class treated with skepticism.", kept);
        Assert.Contains("```\n==not a mark==\n```", kept);
        // Return in a list carries the list on.
        Assert.Equal("- ", TypedNotes.Bullet("- fixed input size"));
        Assert.Equal("  * ", TypedNotes.Bullet("  * nested"));
        Assert.Equal("3. ", TypedNotes.Bullet("2. second"));
        Assert.Null(TypedNotes.Bullet("# History"));

        // The notes' model: "Only my notes" shows the student's alone, and the line over them says whose are whose.
        var model = AiDemo.LectureNotes(notes);
        Assert.True(model.HasMine);
        Assert.StartsWith("Added by ", model.LegendAi);
        model.ToggleOnlyMineCommand.Execute(null);
        Assert.Equal(TypedNotes.Only(notes), model.ShownMarkdown);
        var plain = AiDemo.LectureNotes(Demo.Notes);
        Assert.False(plain.HasMine);
        Assert.StartsWith("Written by ", plain.LegendAi);
    }

    [Fact]
    public void The_folder_picker_lists_every_folder_by_group_narrows_as_it_is_typed_in_and_makes_new_ones()
    {
        var picked = new List<string>();
        var made = new List<string>();
        int closed = 0;
        FolderPickerModel Picker(string current = "Machine Learning")
        {
            var p = Demo.Picker(current);
            p.OnPick = picked.Add;
            p.OnNew = made.Add;
            p.OnClose = () => closed++;
            return p;
        }
        var m = Picker();
        // Unsorted, then the folders under Classes, Clubs and Work with how many notes each holds; a check on where it is.
        Assert.Equal(["Unsorted", "Classes", "Machine Learning", "Capstone", "Computer Architecture", "Data Mining", "Theory of Computation", "Clubs", "ACM", "Work",
            "Intellievent", "NSWC", "", "New folder"], m.Rows.Select(r => r.Shown));
        var ml = m.Rows.Single(r => r.Name == "Machine Learning");
        Assert.Equal((true, false), (ml.Current, ml.ShowCount));
        Assert.Equal((4, true), (m.Rows.Single(r => r.Name == "Capstone").Count, m.Rows.Single(r => r.Name == "Capstone").ShowCount));
        Assert.Equal("Move to folder", m.Placeholder);

        // Typing narrows it, and puts the keys on the first one found: Return files it there.
        m.Query = "comp";
        Assert.Equal(["Classes", "Computer Architecture", "Theory of Computation", "", "New folder “comp”"], m.Rows.Select(r => r.Shown));
        Assert.True(m.Rows[1].Active);
        m.DownCommand.Execute(null);
        Assert.True(m.Rows[2].Active);
        m.UpCommand.Execute(null);
        m.EnterCommand.Execute(null);
        Assert.Equal(["Computer Architecture"], picked);
        Assert.Equal(1, closed);

        // The folder it's already in closes the menu and moves nothing; Unsorted takes it out of every folder.
        m = Picker();
        m.PickCommand.Execute(m.Rows.Single(r => r.Current));
        Assert.Single(picked);
        m.PickCommand.Execute(m.Rows[0]);
        Assert.Equal("Unsorted", picked[^1]);
        Assert.True(Picker("").Rows[0].Current);

        // New folder: named by what's typed, or (nothing typed) asked for in the same field. Esc backs out of naming first.
        m = Picker();
        m.Query = "Robotics";
        m.PickCommand.Execute(m.Rows[^1]);
        Assert.Equal(["Robotics"], made);
        m = Picker();
        m.PickCommand.Execute(m.Rows[^1]);
        Assert.True(m.Naming);
        Assert.Equal("Name the new folder", m.Placeholder);
        Assert.Empty(m.Rows);
        closed = 0;
        m.EscapeCommand.Execute(null);
        Assert.False(m.Naming);
        Assert.Equal(0, closed);
        m.PickCommand.Execute(m.Rows[^1]);
        m.Query = "Study group";
        m.EnterCommand.Execute(null);
        Assert.Equal(["Robotics", "Study group"], made);
        // A name that's a folder already isn't made twice.
        m = Picker();
        m.PickCommand.Execute(m.Rows[^1]);
        m.Query = "ACM";
        m.EnterCommand.Execute(null);
        Assert.Equal(2, made.Count);
        m.EscapeCommand.Execute(null);
        m.EscapeCommand.Execute(null);
        Assert.True(closed > 0);
    }

    [AvaloniaFact]
    public void The_notes_typed_while_recording_read_as_notes_and_are_kept_as_plain_text()
    {
        var editor = new TypedEditor { Width = 500 };
        var window = new Window { Width = 560, Height = 400, Content = editor };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            void Press(TextBox box, Avalonia.Input.Key key) =>
                box.RaiseEvent(new Avalonia.Input.KeyEventArgs { RoutedEvent = Avalonia.Input.InputElement.KeyDownEvent, Key = key, Source = box });
            void Type(TextBox box, string text)
            {
                box.Text = text;
                box.CaretIndex = text.Length;
            }
            // An empty page: one plain line.
            Assert.Equal("", editor.Text);
            Assert.Single(editor.Fields);

            // "# " makes a heading the moment it's typed; Return goes on to a plain line, where "- " makes a bullet.
            Type(editor.Fields[0], "# History");
            Assert.Equal(("History", "# History"), (editor.Fields[0].Text, editor.Text));
            Assert.Equal(FontWeight.ExtraBold, editor.Fields[0].FontWeight);
            Press(editor.Fields[0], Avalonia.Input.Key.Enter);
            Assert.Equal(2, editor.Fields.Count);
            Type(editor.Fields[1], "- Attention, 2017");
            Assert.Equal("# History\n- Attention, 2017", editor.Text);
            // Return in a list carries the list on; Return on an empty bullet ends it.
            Press(editor.Fields[1], Avalonia.Input.Key.Enter);
            Assert.Equal("# History\n- Attention, 2017\n- ", editor.Text);
            Type(editor.Fields[2], "ChatGPT, Nov 2022");
            Press(editor.Fields[2], Avalonia.Input.Key.Enter);
            Press(editor.Fields[3], Avalonia.Input.Key.Enter);
            Assert.Equal("# History\n- Attention, 2017\n- ChatGPT, Nov 2022\n", editor.Text);
            // Return in the middle of a line splits it there.
            editor.Fields[2].CaretIndex = 8;
            editor.Fields[2].SelectionStart = editor.Fields[2].SelectionEnd = 8;
            Press(editor.Fields[2], Avalonia.Input.Key.Enter);
            Assert.Equal("# History\n- Attention, 2017\n- ChatGPT,\n-  Nov 2022\n", editor.Text);
            // Backspace at a bullet's start makes it a plain line; again, and it joins the line above.
            var second = editor.Fields[3];
            second.CaretIndex = second.SelectionStart = second.SelectionEnd = 0;
            Press(second, Avalonia.Input.Key.Back);
            Assert.Equal("# History\n- Attention, 2017\n- ChatGPT,\n Nov 2022\n", editor.Text);
            second.CaretIndex = second.SelectionStart = second.SelectionEnd = 0;
            Press(second, Avalonia.Input.Key.Back);
            Assert.Equal("# History\n- Attention, 2017\n- ChatGPT, Nov 2022\n", editor.Text);
            // Delete at a line's end pulls the next line up.
            var first = editor.Fields[1];
            first.CaretIndex = first.SelectionStart = first.SelectionEnd = first.Text!.Length;
            Press(first, Avalonia.Input.Key.Delete);
            Assert.Equal("# History\n- Attention, 2017ChatGPT, Nov 2022\n", editor.Text);
            // Several lines pasted land as several lines, each what it's written as.
            editor.Text = "";
            Dispatcher.UIThread.RunJobs();
            editor.Insert("# Self-attention\n- q · k → softmax\n1. position encodings");
            Assert.Equal("# Self-attention\n- q · k → softmax\n1. position encodings", editor.Text);
            Assert.Equal(["Self-attention", "q · k → softmax", "position encodings"], editor.Fields.Select(f => f.Text));
            // A numbered list goes on with the next number.
            var numbered = editor.Fields[2];
            numbered.CaretIndex = numbered.SelectionStart = numbered.SelectionEnd = numbered.Text!.Length;
            Press(numbered, Avalonia.Input.Key.Enter);
            Assert.EndsWith("\n1. position encodings\n2. ", editor.Text);

            // Text set from outside (a recording's page opening on what was typed before) is laid out the same way.
            editor.Text = Demo.Typed.ReplaceLineEndings("\n");
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(Demo.Typed.ReplaceLineEndings("\n"), editor.Text);
            Assert.Equal("History", editor.Fields[0].Text);
        }
        finally
        {
            window.Close();
        }
    }

    [Fact]
    public async Task A_notes_title_is_typed_over_to_rename_it_and_a_refusal_puts_the_old_one_back()
    {
        var renamed = new List<string>();
        bool accept = true;
        var note = new NoteModel
        {
            Title = "Lecture, Wed 30 Sep",
            OnRename = title =>
            {
                renamed.Add(title);
                return Task.FromResult(accept);
            },
        }.Titled();
        Assert.Equal("Lecture, Wed 30 Sep", note.TitleText);
        // Left as it was, or emptied: nothing is renamed, and the name comes back.
        await note.CommitTitleCommand.ExecuteAsync(null);
        note.TitleText = "   ";
        await note.CommitTitleCommand.ExecuteAsync(null);
        Assert.Empty(renamed);
        Assert.Equal("Lecture, Wed 30 Sep", note.TitleText);
        note.TitleText = "  Transformers  ";
        await note.CommitTitleCommand.ExecuteAsync(null);
        Assert.Equal(["Transformers"], renamed);
        Assert.Equal("Transformers", note.TitleText);
        // The library won't take it just now (it's writing the notes): the old name stays.
        accept = false;
        note.TitleText = "Attention";
        await note.CommitTitleCommand.ExecuteAsync(null);
        Assert.Equal("Transformers", note.TitleText);
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
        // The folder by its short name, the time beside it; a screen reader hears the time too.
        Assert.Equal(("Theory", "12:04", true), (m.RecordWords, m.Elapsed, m.Running));
        Assert.Equal("Recording 12:04: open it", m.RecordTip);
        recorder.IsPaused = true;
        Assert.Equal(("Paused", true, false), (m.RecordWords, m.Paused, m.Running));
        m.Recorder = null;
        m.Recording = false;
        Assert.Equal("Record", m.RecordWords);
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

    static Control Library(SkinKind skin, LibraryModel m) => skin == SkinKind.Mac ? new MacLibrary { DataContext = m } : new WinLibrary { DataContext = m };

    static T Named<T>(Control root, string name) where T : Control => root.GetVisualDescendants().OfType<T>().First(c => c.Name == name);

    static List<string> Words(Control root) => [.. root.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible).Select(t => t.Text ?? "")];

    [AvaloniaTheory]
    [InlineData(SkinKind.Mac, 1280, false)]
    [InlineData(SkinKind.Mac, 860, true)]
    [InlineData(SkinKind.Win, 1280, false)]
    [InlineData(SkinKind.Win, 860, true)]
    public void Home_sits_beside_the_sidebar_as_one_page_with_what_is_up_next_and_every_note(SkinKind skin, double width, bool narrow)
    {
        var m = Demo.Semester(narrow: narrow);
        var view = Library(skin, m);
        var w = Host(view, skin, width, 860);
        try
        {
            Assert.Contains("selected", Named<Button>(view, "HomeRow").Classes);
            var sidebar = Named<Control>(view, "Sidebar");
            var page = view.GetVisualDescendants().OfType<Overview>().Single();
            Assert.True(page.IsEffectivelyVisible);
            // One sidebar, one page: the page starts past the sidebar and runs to the window's own margin.
            Assert.False(Named<Control>(view, "CanvasLists").IsEffectivelyVisible);
            Assert.True(In(page, view).Left >= In(sidebar, view).Right - 0.5, $"{skin}: the page starts under the sidebar");
            Assert.True(In(page, view).Right >= width - 10, $"{skin}: the page stops short of the window's edge");

            // No stat tiles, no greeting, no class cards: the date, what's pressing, Up next, then the notes.
            var words = Words(page);
            Assert.Contains("Monday, October 12", words);
            Assert.Contains("Capstone team sync starts in 11 minutes. Reading quiz 6 is due tonight.", words);
            foreach (string gone in new[] { "Good morning", "To hand in", "Lectures", "Last lecture", "Recent lectures" }) Assert.DoesNotContain(gone, words);
            var up = Named<StackPanel>(page, "UpNext");
            var notes = Named<StackPanel>(page, "Notes");
            Assert.True(In(notes, view).Top >= In(up, view).Bottom, $"{skin}: the notes start over Up next");
            // Up next's rows are whole buttons, with no Record button of their own inside them.
            var events = up.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("event")).ToList();
            Assert.Equal(4, events.Count);
            Assert.All(events, e => Assert.Empty(e.GetVisualDescendants().OfType<Button>()));
            Assert.Equal("Click to record", ToolTip.GetTip(events[0]));
            Assert.Contains("soon", events[0].Classes);
            // Each note names its folder as a button of its own; the one in no folder says Unsorted beside an inbox.
            var rows = notes.GetVisualDescendants().OfType<NoteRowView>().ToList();
            Assert.Equal(6, rows.Count);
            var folder = Named<Button>(rows[0], "Folder");
            Assert.True(folder.IsEffectivelyVisible);
            Assert.Equal("In Machine Learning. Change folder", Avalonia.Automation.AutomationProperties.GetName(folder));
            Assert.Contains("Unsorted", Words(rows[1]));
            Assert.Contains(rows[1].GetVisualDescendants().OfType<Icon>(), i => i.Glyph == "inbox" && i.IsEffectivelyVisible);
            Assert.Equal("All notes", Avalonia.Automation.AutomationProperties.GetName(Named<Button>(page, "Filter")));
            // Ask is docked at the page's foot and never past its sides.
            var ask = page.GetVisualDescendants().OfType<Border>().First(b => b.Classes.Contains("ask"));
            Assert.True(In(ask, view).Left >= In(page, view).Left && In(ask, view).Right <= In(page, view).Right, $"{skin}: Ask runs off the page");
            Assert.True(In(ask, view).Bottom <= In(page, view).Bottom, $"{skin}: Ask hangs under the page");
            Assert.Equal("Ask anything about your notes", ask.GetVisualDescendants().OfType<TextBox>().Single().PlaceholderText);
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
    public void A_folder_has_one_page_its_notes_with_a_rail_beside_them_and_no_tabs(SkinKind skin)
    {
        var view = Library(skin, Demo.Semester(Demo.Page.FolderClass));
        var w = Host(view, skin, 1280, 860);
        try
        {
            var page = view.GetVisualDescendants().OfType<Overview>().Single();
            var words = Words(page);
            Assert.Contains("CSCI 4220 on Canvas. Meets Monday, Wednesday and Friday at 9:30 AM.", words);
            // One page: no Lectures | Assignments | Modules | Announcements switcher, no "Class home", no tiles.
            Assert.DoesNotContain(view.GetVisualDescendants().OfType<Button>(), b => b.IsEffectivelyVisible && b.Classes.Contains("seg-item"));
            foreach (string gone in new[] { "Class home", "To hand in", "Recent lectures", "Coming up this week" }) Assert.DoesNotContain(gone, words);
            var title = Named<Highlight>(page, "FolderTitle");
            Assert.True(title.IsEffectivelyVisible && title.Under);
            Assert.Equal("Machine Learning", title.Text);
            // Attach and Record at the head; the notes by week on the left, the rail (272 wide) on the right.
            Assert.True(Named<Button>(page, "Attach").IsEffectivelyVisible);
            Assert.True(Named<Button>(page, "Record").IsEffectivelyVisible);
            foreach (string heading in new[] { "Lectures", "This week", "Last week", "October", "September", "Due", "On Canvas", "Next class" }) Assert.Contains(heading, words);
            var rail = Named<StackPanel>(page, "Rail");
            var rows = page.GetVisualDescendants().OfType<NoteRowView>().ToList();
            Assert.Equal(7, rows.Count);
            Assert.Equal(272, rail.Bounds.Width, 0.5);
            Assert.True(In(rail, view).Left >= In(rows[0], view).Right, "the rail sits beside the notes");
            Assert.All(rows, r => Assert.False(Named<Button>(r, "Folder").IsVisible)); // the page is the folder
            Assert.Contains("Sun 18 Oct, in 6 days", words);
            Assert.Contains("Wednesday at 9:30 AM", words);
            Assert.Equal("Ask about Machine Learning", page.GetVisualDescendants().OfType<TextBox>().First(t => t.Name == "AskInput").PlaceholderText);
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
    public void A_notes_page_fills_the_pane_with_whose_words_are_whose(SkinKind skin)
    {
        var m = Demo.Semester(Demo.Page.Note);
        string? folder = null;
        m.OnFolder = name => folder = name;
        var view = Library(skin, m);
        var w = Host(view, skin, 1280, 860);
        try
        {
            var page = view.GetVisualDescendants().OfType<NotePage>().Single();
            Assert.True(page.IsEffectivelyVisible);
            // No second column: the page runs from the sidebar to the window's margin.
            Assert.False(Named<Control>(view, "CanvasLists").IsEffectivelyVisible);
            Assert.True(In(page, view).Left <= In(Named<Control>(view, "Sidebar"), view).Right + 12 && In(page, view).Right >= 1280 - 10);
            var words = Words(page);
            Assert.Contains("Lecture, Monday 12 October at 9:30 AM, 52 min", words);
            Assert.Contains("What you wrote", words);
            Assert.Contains("Added by Claude from the recording", words);
            // The folder over the title is a link to its page; the title is a field to type over.
            var link = Named<Button>(page, "FolderLink");
            link.Command!.Execute(link.CommandParameter);
            Assert.Equal("Machine Learning", folder);
            Assert.Equal("Transformers, Self-Attention, and LLM Fundamentals", Named<TextBox>(page, "Title").Text);
            Assert.True(Named<Button>(page, "ChangeFolder").IsEffectivelyVisible);
            // The student's words are ink and heavier; what was added is muted.
            var body = page.GetVisualDescendants().OfType<NoteView>().First(n => n.IsEffectivelyVisible);
            Assert.True(body.Provenance);
            // "Only my notes" takes away what was added.
            var only = Named<Button>(page, "OnlyMine");
            Assert.True(only.IsEffectivelyVisible);
            only.Command!.Execute(null);
            Dispatcher.UIThread.RunJobs();
            Assert.True(m.Notes!.OnlyMine);
            Assert.DoesNotContain("built on attention", body.Markdown);
            Assert.Contains("==Attention Is All You Need, 2017==", body.Markdown);
            Assert.Equal("Ask about this lecture", page.GetVisualDescendants().OfType<TextBox>().First(t => t.Name == "AskInput").PlaceholderText);
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
    public void While_it_records_the_page_takes_the_students_notes_and_shows_what_is_heard(SkinKind skin)
    {
        var m = Demo.Semester(Demo.Page.Recording);
        var typed = new List<string>();
        m.Note!.OnTyped = typed.Add;
        int stopped = 0, paused = 0;
        m.Note.Recorder!.OnStop = () => stopped++;
        m.Note.Recorder.OnPause = () => paused++;
        var view = Library(skin, m);
        var w = Host(view, skin, 1280, 860);
        try
        {
            var page = view.GetVisualDescendants().OfType<NotePage>().Single();
            var bar = Named<Border>(page, "RecBar");
            Assert.True(bar.IsEffectivelyVisible);
            Assert.Equal("Recording, 12:04", Avalonia.Automation.AutomationProperties.GetName(bar));
            var words = Words(page);
            Assert.Contains("Machine Learning, Mon 12 Oct", Named<TextBox>(page, "Title").Text);
            Assert.Contains("Write as much or as little as you like. When you stop recording, Claude writes up the lecture around your notes.", words);
            Assert.Equal("Live transcript", Named<Button>(page, "TranscriptTab").Content);
            // Nothing to move, share or delete yet: those are for a note that's in the library.
            Assert.False(Named<Control>(view, "NoteTools").IsEffectivelyVisible);
            Assert.False(Named<Button>(page, "ChangeFolder").IsVisible);
            // What's typed is the student's own, reads as notes (a heading, bullets under it), and is handed on as it's typed.
            var notes = Named<TypedEditor>(page, "Typed");
            Assert.True(notes.IsEffectivelyVisible);
            Assert.Equal("History", notes.Fields[0].Text);
            Assert.Equal(FontWeight.ExtraBold, notes.Fields[0].FontWeight);
            Assert.Equal("Attention Is All You Need, 2017", notes.Fields[1].Text);
            var last = notes.Fields[^1];
            last.Text += ", on the exam?";
            Assert.EndsWith("- q · k → softmax → weights on v, on the exam?", typed[^1]);
            Assert.EndsWith(typed[^1], m.Note.Typed);
            // Pause and Stop are the bar's own.
            Named<Button>(page, "Pause").Command!.Execute(null);
            Named<Button>(page, "Stop").Command!.Execute(null);
            Assert.Equal((1, 1), (paused, stopped));
            // The other tab: what's been heard so far.
            m.Note.TranscriptsCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
            Assert.Contains("And those weights are applied to the value vectors.", Words(page));
            Assert.Equal("Ask about what's been said so far", page.GetVisualDescendants().OfType<TextBox>().First(t => t.Name == "AskInput").PlaceholderText);
            // The sidebar says something is recording, one click from this page.
            Assert.True(Named<Button>(view, "RecordingNow").IsEffectivelyVisible);
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
    public void Due_is_the_calendar_with_today_lifted_and_no_ask_bar(SkinKind skin)
    {
        var view = Library(skin, Demo.Semester(Demo.Page.Due));
        var w = Host(view, skin, 1280, 900);
        try
        {
            var page = view.GetVisualDescendants().OfType<Overview>().Single();
            var words = Words(page);
            Assert.Contains("Reading quiz 6 is due tonight, and Asynchronous Lecture 5 is still missing.", words);
            foreach (string there in new[] { "October 2026", "Mon", "Sun", "1 Oct", "1 Nov", "Today", "Tonight 11:59 PM", "Missing", "1 more", "All classes", "Month", "Week" }) Assert.Contains(there, words);
            var cells = page.GetVisualDescendants().OfType<Border>().Where(b => b.Classes.Contains("cal-cell")).ToList();
            Assert.Equal(35, cells.Count);
            Assert.Single(cells, c => c.Classes.Contains("today"));
            // Handed in: struck through. Missing: the word, in the late red.
            Assert.Contains(page.GetVisualDescendants().OfType<Button>(), b => b.Classes.Contains("cal-item") && b.Classes.Contains("done"));
            var late = (ISolidColorBrush)view.FindResource(ThemeVariant.Light, "Late")!;
            var missing = page.GetVisualDescendants().OfType<TextBlock>().First(t => t.Text == "Missing" && t.IsEffectivelyVisible);
            Assert.Equal(late.Color, ((ISolidColorBrush)missing.Foreground!).Color);
            // No Ask bar on this page, and no tiles.
            Assert.DoesNotContain(page.GetVisualDescendants().OfType<Border>(), b => b.Classes.Contains("ask") && b.IsEffectivelyVisible);
            Assert.DoesNotContain("To hand in", words);
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
    public void A_long_folder_name_wraps_beside_the_buttons_not_under_them(SkinKind skin)
    {
        var m = Demo.Crowded();
        var view = Library(skin, m);
        var w = Host(view, skin, 1100, 800);
        try
        {
            var page = view.GetVisualDescendants().OfType<Overview>().Single();
            var title = Named<Highlight>(page, "FolderTitle");
            var record = Named<Button>(page, "Record");
            Assert.True(record.IsEffectivelyVisible);
            Assert.True(In(title, view).Right <= In(record, view).Left, $"{skin}: the title runs under Record");
            Assert.Equal("Probability and Statistics for Engineers", title.Text);
            Assert.Contains("No lectures yet. Record one here, or attach a recording.", Words(page));
        }
        finally
        {
            w.Close();
            ((App)Application.Current!).UseSkin(SkinKind.Mac);
        }
    }

    /// <summary>The window alone, edge to edge, the size of the design's own screenshots (1280 × 860 at 1x; theirs
    /// are at 2x): to lay one over the other. No drawn traffic lights or window corners, as the design's has none.</summary>
    static void Plain(string name, ThemeVariant variant, Func<Control> content, double width = 1280, double height = 860)
    {
        Skin.UseTheme(ColourThemes.Default);
        ((App)Application.Current!).UseSkin(SkinKind.Mac);
        var window = new Window { Width = width, Height = height, RequestedThemeVariant = variant, Content = content() };
        Look.Apply(window);
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("nothing rendered");
        using (var file = File.Create(Path.Combine(Shot.Dir, $"{name}-{(variant == ThemeVariant.Dark ? "dark" : "light")}.png")))
            frame.Save(file, Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
        window.Close();
    }

    static Control Bare(LibraryModel m)
    {
        m.DrawChrome = false;
        return new MacLibrary { DataContext = m };
    }

    /// <summary>Every screen of the design, at the size of its own screenshots, light and dark.</summary>
    [AvaloniaFact]
    public void Design_shots()
    {
        foreach (var t in new[] { ThemeVariant.Light, ThemeVariant.Dark })
        {
            Plain("semester-home", t, () => Bare(Demo.Semester()));
            Plain("semester-home-recording", t, () => Bare(Demo.Semester(recording: true)));
            Plain("semester-home-unsorted", t, () => Bare(Demo.Semester(Demo.Page.HomeUnsorted)));
            Plain("semester-due", t, () => Bare(Demo.Semester(Demo.Page.Due)), height: 900);
            Plain("semester-due-week", t, () => Bare(Demo.Semester(Demo.Page.DueWeek)), height: 900);
            Plain("semester-folder-class", t, () => Bare(Demo.Semester(Demo.Page.FolderClass)));
            Plain("semester-folder-club", t, () => Bare(Demo.Semester(Demo.Page.FolderClub)));
            Plain("semester-note", t, () => Bare(Demo.Semester(Demo.Page.Note)));
            Plain("semester-note-only-mine", t, () => Bare(Demo.Semester(Demo.Page.NoteOnlyMine)));
            Plain("semester-recording", t, () => Bare(Demo.Semester(Demo.Page.Recording)));
            // The folder picker, as it opens under a note's folder (a menu doesn't draw into a window's picture).
            Plain("semester-folder-picker", t, () => new Border
            {
                Background = Brushes.Transparent, Padding = new Thickness(24), Child = new FolderPicker { DataContext = Demo.Picker(), HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top },
            }, 328, 470);
        }
    }

    /// <summary>The window 64 px in on the design's ground, as every shot of it is.</summary>
    static readonly Size Wide = new(1408, 988);

    [AvaloniaFact]
    public void Overview_shots()
    {
        foreach (var t in new[] { ThemeVariant.Light, ThemeVariant.Dark })
        {
            Shot.Take("mac-40-home", SkinKind.Mac, t, () => new MacLibrary { DataContext = Demo.Semester(), Width = 1280, Height = 860 }, size: Wide);
            Shot.Take("win-40-home", SkinKind.Win, t, () => new WinLibrary { DataContext = Demo.Semester(), Width = 1280, Height = 860 }, size: Wide);
            Shot.Take("mac-41-folder", SkinKind.Mac, t, () => new MacLibrary { DataContext = Demo.Semester(Demo.Page.FolderClass), Width = 1280, Height = 860 }, size: Wide);
            Shot.Take("win-41-folder", SkinKind.Win, t, () => new WinLibrary { DataContext = Demo.Semester(Demo.Page.FolderClass), Width = 1280, Height = 860 }, size: Wide);
            Shot.Take("mac-42-home-narrow", SkinKind.Mac, t, () => new MacLibrary { DataContext = Demo.Semester(narrow: true), Width = 860, Height = 860 }, size: new Size(988, 988));
            Shot.Take("mac-42-folder-narrow", SkinKind.Mac, t, () => new MacLibrary { DataContext = Demo.Semester(Demo.Page.FolderClass, narrow: true), Width = 860, Height = 860 }, size: new Size(988, 988));
            Shot.Take("mac-43-note", SkinKind.Mac, t, () => new MacLibrary { DataContext = Demo.Semester(Demo.Page.Note), Width = 1280, Height = 860 }, size: Wide);
            Shot.Take("win-43-note", SkinKind.Win, t, () => new WinLibrary { DataContext = Demo.Semester(Demo.Page.Note), Width = 1280, Height = 860 }, size: Wide);
            Shot.Take("mac-44-due", SkinKind.Mac, t, () => new MacLibrary { DataContext = Demo.Semester(Demo.Page.Due), Width = 1280, Height = 900 }, size: new Size(1408, 1028));
            Shot.Take("win-44-due", SkinKind.Win, t, () => new WinLibrary { DataContext = Demo.Semester(Demo.Page.Due), Width = 1280, Height = 900 }, size: new Size(1408, 1028));
            Shot.Take("mac-45-recording", SkinKind.Mac, t, () => new MacLibrary { DataContext = Demo.Semester(Demo.Page.Recording), Width = 1280, Height = 860 }, size: Wide);
            Shot.Take("win-45-recording", SkinKind.Win, t, () => new WinLibrary { DataContext = Demo.Semester(Demo.Page.Recording), Width = 1280, Height = 860 }, size: Wide);
        }
    }

    /// <summary>A note's length is shown whole, however long it reads: "1 h 12 min" is wider than "52 min", and most
    /// lectures run over an hour.</summary>
    [AvaloniaTheory]
    [InlineData(SkinKind.Mac)]
    [InlineData(SkinKind.Win)]
    public void A_notes_length_over_an_hour_is_shown_whole(SkinKind skin)
    {
        ((App)Application.Current!).UseSkin(skin);
        var row = new NoteRowView { DataContext = new NoteItem { Id = "l1", Title = "Recursion and the call stack", When = "Yesterday", Length = "1 h 12 min", Summary = "A recursive function solves a problem by calling itself." } };
        var w = new Window { Width = 900, Height = 300, Content = row };
        try
        {
            w.Show();
            Dispatcher.UIThread.RunJobs();
            var length = row.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Classes.Contains("lecture-length"));
            Assert.True(length.TextLayout.WidthIncludingTrailingWhitespace <= length.Bounds.Width + 0.5,
                $"{skin}: \"{length.Text}\" needs {length.TextLayout.WidthIncludingTrailingWhitespace:0.0} and has {length.Bounds.Width:0.0}");
        }
        finally
        {
            w.Close();
            ((App)Application.Current!).UseSkin(SkinKind.Mac);
        }
    }
}
