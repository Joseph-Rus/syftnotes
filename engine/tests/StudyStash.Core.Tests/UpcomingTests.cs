using StudyStash.Core.Calendar;
using StudyStash.Core.Calendar.Ics;

namespace StudyStash.Core.Tests;

/// <summary>A pretend calendar source: its calendars and events as a test sets them, or a failure.</summary>
public sealed class FakeCalendarSource(string id, string kind = "fake") : ICalendarSource
{
    public string Id => id;
    public string Kind => kind;
    public string Name => id;
    public List<CalendarInfo> Calendars { get; } = [];
    public List<CalendarEvent> Items { get; } = [];
    public Exception? Fails { get; set; }
    public List<IReadOnlyCollection<string>> AskedFor { get; } = [];

    public Task<IReadOnlyList<CalendarInfo>> ListCalendarsAsync(CancellationToken ct) =>
        Fails is { } e ? Task.FromException<IReadOnlyList<CalendarInfo>>(e) : Task.FromResult<IReadOnlyList<CalendarInfo>>(Calendars);

    public Task<IReadOnlyList<CalendarEvent>> EventsAsync(IReadOnlyCollection<string> calendarIds, DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        AskedFor.Add(calendarIds);
        return Task.FromResult<IReadOnlyList<CalendarEvent>>([.. Items.Where(e => calendarIds.Contains(e.CalendarId) && e.Start < to && e.End > from)]);
    }

    public static CalendarEvent Event(string id, string calendar, string source, string title, DateTimeOffset start, int minutes = 60, bool cancelled = false, bool allDay = false) =>
        new(id, calendar, source, title, start, start.AddMinutes(minutes), allDay, null, [], null, cancelled);
}

/// <summary>Reading every calendar, putting them together, and what's on now.</summary>
public class UpcomingTests
{
    static readonly DateTimeOffset Now = new(2026, 9, 28, 9, 0, 0, TimeSpan.FromHours(-4));
    static readonly TimeZoneInfo NewYork = IcsZones.SystemZone("America/New_York")!;

    static (Upcoming Upcoming, Dictionary<string, FakeCalendarSource> Sources) Make(TempDir dir, params string[] ids)
    {
        var sources = ids.ToDictionary(id => id, id => new FakeCalendarSource(id));
        var kinds = new CalendarKinds();
        kinds.Register(new CalendarKind("fake", "Fake", s => sources[s.Id]));
        CalendarSettings.Update(dir.Path, s =>
        {
            foreach (string id in ids) s.Sources.Add(new CalendarSourceSettings { Id = id, Kind = "fake", Name = id });
        });
        return (new Upcoming(dir.Path, kinds, () => Now), sources);
    }

    [Fact]
    public async Task Every_shown_calendar_is_read_merged_in_order_and_saved_for_next_time()
    {
        using var dir = new TempDir();
        var (upcoming, sources) = Make(dir, "a", "b");
        sources["a"].Calendars.AddRange([new("a1", "a", "Classes", "#FF0000", true), new("a2", "a", "Birthdays", null, false)]);
        sources["a"].Items.AddRange([
            FakeCalendarSource.Event("e2", "a1", "a", "BIO 110", Now.AddHours(3)),
            FakeCalendarSource.Event("e1", "a1", "a", "CS 101 Lecture", Now.AddHours(1)),
            FakeCalendarSource.Event("x", "a1", "a", "Called off", Now.AddHours(2), cancelled: true),
            FakeCalendarSource.Event("bd", "a2", "a", "Mum's birthday", Now.AddHours(1)),
        ]);
        sources["b"].Calendars.Add(new("b1", "b", "School feed", null, true));
        // The same class on the school's feed: shown once.
        sources["b"].Items.Add(FakeCalendarSource.Event("other-id", "b1", "b", "CS 101 lecture", Now.AddHours(1)));
        int changed = 0;
        upcoming.Changed += () => changed++;
        await upcoming.RefreshAsync();
        Assert.Equal(["e1", "e2"], upcoming.Events.Select(e => e.Id));
        Assert.Equal("a", upcoming.Events[0].SourceId);
        Assert.Equal(Now, upcoming.Updated);
        Assert.Equal(1, changed);
        // Only the shown calendar was asked for.
        Assert.Equal(["a1"], sources["a"].AskedFor.Single());
        var settings = CalendarSettings.Load(dir.Path);
        Assert.Equal(["a1", "a2", "b1"], settings.Calendars.Select(c => c.Id));
        // The menu opens on the saved copy, before anything is read.
        var again = new Upcoming(dir.Path, new CalendarKinds(), () => Now);
        Assert.Equal(["e1", "e2"], again.Events.Select(e => e.Id));
        Assert.Equal(Now, again.Updated);
    }

    [Fact]
    public async Task A_calendar_switched_off_or_on_is_followed()
    {
        using var dir = new TempDir();
        var (upcoming, sources) = Make(dir, "a");
        sources["a"].Calendars.AddRange([new("a1", "a", "Classes", null, true), new("a2", "a", "Birthdays", null, false)]);
        sources["a"].Items.AddRange([FakeCalendarSource.Event("e1", "a1", "a", "CS 101", Now.AddHours(1)), FakeCalendarSource.Event("bd", "a2", "a", "Birthday", Now.AddHours(2))]);
        CalendarSettings.Update(dir.Path, s =>
        {
            s.Enabled["a1"] = false;
            s.Enabled["a2"] = true;
        });
        await upcoming.RefreshAsync();
        Assert.Equal(["bd"], upcoming.Events.Select(e => e.Id));
    }

    [Fact]
    public async Task A_source_that_fails_keeps_what_it_had_and_settings_says_why()
    {
        using var dir = new TempDir();
        var (upcoming, sources) = Make(dir, "a", "b");
        sources["a"].Calendars.Add(new("a1", "a", "Feed", null, true));
        sources["a"].Items.Add(FakeCalendarSource.Event("e1", "a1", "a", "CS 101", Now.AddHours(1)));
        sources["b"].Calendars.Add(new("b1", "b", "Other", null, true));
        sources["b"].Items.Add(FakeCalendarSource.Event("f1", "b1", "b", "Art", Now.AddHours(2)));
        await upcoming.RefreshAsync();
        sources["a"].Fails = new CalendarFeedException("That calendar isn't public.");
        sources["b"].Fails = new InvalidOperationException("boom.");
        await upcoming.RefreshAsync();
        Assert.Equal(["e1", "f1"], upcoming.Events.Select(e => e.Id));
        var problems = CalendarSettings.Load(dir.Path).Problems;
        Assert.Equal("That calendar isn't public.", problems["a"]);
        Assert.StartsWith("Couldn't read it (boom)", problems["b"]);
        sources["a"].Fails = sources["b"].Fails = null;
        await upcoming.RefreshAsync();
        Assert.Empty(CalendarSettings.Load(dir.Path).Problems);
    }

    [Fact]
    public async Task A_removed_source_s_events_go_and_an_unknown_kind_is_left_alone()
    {
        using var dir = new TempDir();
        var (upcoming, sources) = Make(dir, "a");
        sources["a"].Calendars.Add(new("a1", "a", "Feed", null, true));
        sources["a"].Items.Add(FakeCalendarSource.Event("e1", "a1", "a", "CS 101", Now.AddHours(1)));
        await upcoming.RefreshAsync();
        CalendarSettings.Update(dir.Path, s =>
        {
            s.Remove("a");
            s.Sources.Add(new CalendarSourceSettings { Id = "apple", Kind = "apple", Name = "Apple Calendar" });
        });
        await upcoming.RefreshAsync();
        Assert.Empty(upcoming.Events);
        Assert.Equal("apple", CalendarSettings.Load(dir.Path).Sources.Single().Id);
    }

    [Fact]
    public async Task A_real_feed_end_to_end()
    {
        using var dir = new TempDir();
        var server = new FeedServer().Serve("https://calendar.google.com/basic.ics", FeedServer.Fixture("google.ics"));
        var kinds = CalendarKinds.WithFeeds(new HttpClient(server), NewYork);
        var feed = IcsSource.New("webcal://calendar.google.com/basic.ics", null, Now);
        CalendarSettings.Update(dir.Path, s => s.Sources.Add(feed));
        var upcoming = new Upcoming(dir.Path, kinds, () => Now);
        await upcoming.RefreshAsync();
        Assert.Equal("Fall 2026 classes", CalendarSettings.Load(dir.Path).Calendars.Single().Name);
        // A week from Monday 28 Sep: Tue, Thu (1 Oct moved to 2 PM), the study group; Tue 6 Oct is cancelled.
        Assert.Equal(["CS 101 Lecture", "BIO 110 study group", "CS 101 Lecture (moved)"], upcoming.Events.Select(e => e.Title));
    }

    [Fact]
    public void Merge_drops_cancelled_and_the_same_event_twice_and_prefers_the_first_source()
    {
        var a = FakeCalendarSource.Event("1", "c", "late", "CS 101", Now);
        var b = FakeCalendarSource.Event("2", "c", "early", "cs 101", Now);
        var c = FakeCalendarSource.Event("1", "c", "early", "Again", Now.AddHours(1));
        var d = FakeCalendarSource.Event("1", "c", "early", "Again", Now.AddHours(1));
        var gone = FakeCalendarSource.Event("3", "c", "early", "Gone", Now, cancelled: true);
        var merged = Upcoming.Merge([a, b, c, d, gone], new Dictionary<string, int> { ["early"] = 0, ["late"] = 1 });
        Assert.Equal([b, c], merged);
    }

    [Fact]
    public void Coming_up_is_timed_events_today_and_tomorrow_not_yet_over()
    {
        var events = new[]
        {
            FakeCalendarSource.Event("over", "c", "s", "Over", Now.AddHours(-2)),
            FakeCalendarSource.Event("on", "c", "s", "On now", Now.AddMinutes(-30)),
            FakeCalendarSource.Event("day", "c", "s", "Fall break", Now.AddHours(1), minutes: 1440, allDay: true),
            FakeCalendarSource.Event("due", "c", "s", "Lab due", Now.AddHours(1), minutes: 0),
            FakeCalendarSource.Event("tomorrow", "c", "s", "Tomorrow", Now.AddDays(1)),
            FakeCalendarSource.Event("later", "c", "s", "Wednesday", Now.AddDays(2)),
        };
        Assert.Equal(["on", "tomorrow"], Upcoming.Coming(events, Now, NewYork).Select(e => e.Id));
        Assert.Equal(["on"], Upcoming.Coming(events, Now, NewYork, 1).Select(e => e.Id));
    }

    [Fact]
    public void The_event_a_recording_is_for_on_now_or_starting_within_ten_minutes()
    {
        List<ClassHint> classes = [new("CS 101", []), new("BIO 110", [])];
        var cs = FakeCalendarSource.Event("cs", "c", "s", "CS 101 Lecture", Now.AddMinutes(8));
        var lunch = FakeCalendarSource.Event("lunch", "c", "s", "Lunch", Now.AddMinutes(-20));
        Assert.Equal(new EventNow(cs, "CS 101"), Upcoming.During([cs, lunch], Now, classes));
        Assert.Equal(new EventNow(lunch, null), Upcoming.During([lunch], Now, classes));
        Assert.Null(Upcoming.During([FakeCalendarSource.Event("x", "c", "s", "CS 101", Now.AddMinutes(11))], Now, classes));
        // Two on at once: the one that started last.
        var bio = FakeCalendarSource.Event("bio", "c", "s", "BIO 110", Now.AddMinutes(-5));
        var early = FakeCalendarSource.Event("early", "c", "s", "CS 101 Lecture", Now.AddMinutes(-50));
        Assert.Equal("bio", Upcoming.During([early, bio], Now, classes)!.Event.Id);
        // On now beats starting soon; a due date, an all-day event or a cancelled one is never it.
        Assert.Equal("bio", Upcoming.During([cs, bio], Now, classes)!.Event.Id);
        Assert.Null(Upcoming.During([
            FakeCalendarSource.Event("due", "c", "s", "CS 101 due", Now.AddMinutes(5), minutes: 0),
            FakeCalendarSource.Event("day", "c", "s", "CS 101 day", Now.AddHours(-1), minutes: 1440, allDay: true),
            FakeCalendarSource.Event("off", "c", "s", "CS 101", Now, cancelled: true),
        ], Now, classes));
    }

    [Fact]
    public async Task Running_reads_at_once_and_again_when_woken()
    {
        using var dir = new TempDir();
        var (upcoming, sources) = Make(dir, "a");
        sources["a"].Calendars.Add(new("a1", "a", "Feed", null, true));
        var reads = new SemaphoreSlim(0);
        upcoming.Changed += () => reads.Release();
        using var stop = new CancellationTokenSource();
        var run = upcoming.RunAsync(stop.Token);
        Assert.True(await reads.WaitAsync(TimeSpan.FromSeconds(10)));
        sources["a"].Items.Add(FakeCalendarSource.Event("e1", "a1", "a", "CS 101", Now.AddHours(1)));
        upcoming.Wake();
        Assert.True(await reads.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.Single(upcoming.Events);
        stop.Cancel();
        await run.WaitAsync(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public void A_broken_saved_copy_is_nothing_coming_up()
    {
        using var dir = new TempDir();
        File.WriteAllText(Upcoming.PathIn(dir.Path), "{broken");
        var upcoming = new Upcoming(dir.Path, new CalendarKinds(), () => Now);
        Assert.Empty(upcoming.Events);
        Assert.Null(upcoming.Updated);
    }
}
