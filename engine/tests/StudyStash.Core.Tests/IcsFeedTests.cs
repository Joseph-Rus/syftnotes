using StudyStash.Core.Calendar;
using StudyStash.Core.Calendar.Ics;

namespace StudyStash.Core.Tests;

/// <summary>Real-world-shaped feeds (Google, iCloud, Canvas, Outlook) turned into the occurrences in a window.</summary>
public class IcsFeedTests
{
    static readonly TimeZoneInfo NewYork = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
    static readonly TimeZoneInfo LosAngeles = TimeZoneInfo.FindSystemTimeZoneById("America/Los_Angeles");

    static string Fixture(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "calendar", name));

    static DateTimeOffset Ny(int m, int d, int h = 0, int min = 0) => new(new DateTime(2026, m, d, h, min, 0), NewYork.GetUtcOffset(new DateTime(2026, m, d, h, min, 0)));

    static List<CalendarEvent> Events(string fixture, DateTimeOffset from, DateTimeOffset to, TimeZoneInfo? local = null) =>
        IcsFeed.Parse(Fixture(fixture), local ?? NewYork).Events("cal", "src", from, to);

    [Fact]
    public void Google_a_weekly_class_its_name_and_the_days_it_skips()
    {
        var feed = IcsFeed.Parse(Fixture("google.ics"), NewYork);
        Assert.True(feed.IsCalendar);
        Assert.Equal("Fall 2026 classes", feed.Name);
        Assert.Null(feed.Color);
        var cs = feed.Events("cal", "src", Ny(9, 1), Ny(12, 31)).Where(e => e.Title.StartsWith("CS 101")).ToList();
        // Tuesdays and Thursdays from 1 Sep to 10 Dec: 30, less Thanksgiving week (2); the cancelled 6 Oct stays in, marked.
        Assert.Equal(30 - 2, cs.Count);
        Assert.Single(cs, e => e.Cancelled);
        Assert.DoesNotContain(cs, e => e.Start.Date == new DateTime(2026, 11, 24) || e.Start.Date == new DateTime(2026, 11, 26));
        var first = cs[0];
        Assert.Equal(Ny(9, 1, 10), first.Start);
        Assert.Equal(Ny(9, 1, 11, 15), first.End);
        Assert.Equal("Engineering Hall 204, Main Campus", first.Location);
        Assert.Equal("cal", first.CalendarId);
        Assert.Equal("src", first.SourceId);
        Assert.False(first.AllDay);
        // After the clocks go back (1 Nov) the class is still at 10:00 in New York.
        var november = cs.First(e => e.Start.Month == 11);
        Assert.Equal(new DateTimeOffset(2026, 11, 3, 10, 0, 0, TimeSpan.FromHours(-5)), november.Start);
        Assert.Equal(Ny(12, 10, 10), cs[^1].Start);
        // Each occurrence has an id of its own, the same every time the feed is read.
        Assert.Equal(cs.Count, cs.Select(e => e.Id).Distinct().Count());
        Assert.Equal(first.Id, IcsFeed.Parse(Fixture("google.ics"), NewYork).Events("c", "s", Ny(9, 1), Ny(9, 2)).Single().Id);
    }

    [Fact]
    public void Google_a_moved_occurrence_takes_its_new_time_title_and_place()
    {
        var oct1 = Events("google.ics", Ny(10, 1), Ny(10, 2));
        var moved = Assert.Single(oct1);
        Assert.Equal("CS 101 Lecture (moved)", moved.Title);
        Assert.Equal(Ny(10, 1, 14), moved.Start);
        Assert.Equal(Ny(10, 1, 15, 15), moved.End);
        Assert.Equal("Library Room B", moved.Location);
        // Moved out of a window that held its first time, it's gone from that window.
        Assert.Empty(Events("google.ics", Ny(10, 1, 9), Ny(10, 1, 12)));
    }

    [Fact]
    public void Google_all_day_attendees_and_a_meeting_link()
    {
        var events = Events("google.ics", Ny(9, 30), Ny(10, 15));
        var fallBreak = events.Single(e => e.Title == "Fall break");
        Assert.True(fallBreak.AllDay);
        Assert.Equal(Ny(10, 12), fallBreak.Start);
        Assert.Equal(Ny(10, 14), fallBreak.End);
        var group = events.Single(e => e.Title == "BIO 110 study group");
        Assert.Equal(new DateTimeOffset(2026, 9, 30, 18, 0, 0, TimeSpan.Zero), group.Start);
        Assert.Equal(["Sam Lee", "alex@uni.edu"], group.Attendees);
        Assert.Equal("https://meet.google.com/abc-defg-hij", group.Url);
    }

    [Fact]
    public void ICloud_colour_folded_title_and_a_count()
    {
        var feed = IcsFeed.Parse(Fixture("icloud.ics"), LosAngeles);
        Assert.Equal("School", feed.Name);
        Assert.Equal("#FF2968", feed.Color);
        var all = feed.Events("c", "s", new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero), new DateTimeOffset(2027, 6, 1, 0, 0, 0, TimeSpan.Zero));
        var calc = all.Where(e => e.Title.StartsWith("Calculus")).ToList();
        Assert.Equal(40, calc.Count);
        Assert.Equal("Calculus II (MATH 2410) – lecture with Professor Okonkwo in the big hall", calc[0].Title);
        Assert.Equal(new DateTimeOffset(2026, 9, 28, 9, 0, 0, TimeSpan.FromHours(-7)), calc[0].Start);
        Assert.Equal(TimeSpan.FromMinutes(50), calc[0].End - calc[0].Start);
    }

    [Fact]
    public void A_class_keeps_its_wall_clock_time_and_length_across_the_change_of_clocks()
    {
        // 30 Oct to 3 Nov at 9:00 in Los Angeles, over the night of 1 November.
        var lab = Events("icloud.ics", new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 12, 1, 0, 0, 0, TimeSpan.Zero), LosAngeles)
            .Where(e => e.Title == "Morning lab").ToList();
        Assert.Equal(5, lab.Count);
        Assert.All(lab, e => Assert.Equal(9, TimeZoneInfo.ConvertTime(e.Start, LosAngeles).Hour));
        Assert.All(lab, e => Assert.Equal(TimeSpan.FromMinutes(50), e.End - e.Start));
        Assert.Equal(TimeSpan.FromHours(-7), lab[0].Start.Offset);
        Assert.Equal(TimeSpan.FromHours(-8), lab[^1].Start.Offset);
    }

    [Fact]
    public void Canvas_due_dates_and_its_links()
    {
        var feed = IcsFeed.Parse(Fixture("canvas.ics"), NewYork);
        Assert.Equal("Sam Lee Calendar (Canvas)", feed.Name);
        var events = feed.Events("c", "s", Ny(9, 28), Ny(10, 5));
        var lab = events.Single(e => e.Title.StartsWith("Lab 3"));
        // A due date is a moment: it starts and ends at once, and still shows in a window it's in.
        Assert.Equal(lab.Start, lab.End);
        Assert.Equal("https://school.instructure.com/courses/4242/assignments/123456", lab.Url);
        Assert.True(events.Single(e => e.Title.StartsWith("Quiz 2")).AllDay);
    }

    [Fact]
    public void Outlook_windows_zone_names_its_own_zones_quoted_names_and_teams()
    {
        var events = Events("outlook.ics", Ny(9, 1), Ny(12, 31));
        var statics = events.Where(e => e.Title == "ENGR 2010 Statics recitation").ToList();
        // Wednesdays 2 Sep to 9 Dec.
        Assert.Equal(15, statics.Count);
        Assert.Equal(Ny(9, 2, 10), statics[0].Start);
        Assert.Equal(new DateTimeOffset(2026, 12, 9, 10, 0, 0, TimeSpan.FromHours(-5)), statics[^1].Start);
        Assert.Equal(["Okafor, Dana"], statics[0].Attendees);
        Assert.StartsWith("https://teams.microsoft.com/l/meetup-join/", statics[0].Url);
        Assert.Equal(Ny(10, 15, 13, 30), events.Single(e => e.Title == "Advising").Start);
        Assert.True(events.Single(e => e.Title.StartsWith("Canceled")).Cancelled);
    }

    [Fact]
    public void Floating_times_read_in_the_feed_s_zone_or_this_computer_s()
    {
        const string text = "BEGIN:VCALENDAR\nBEGIN:VEVENT\nUID:f\nSUMMARY:Floating\nDTSTART:20261001T090000\nDTEND:20261001T100000\nEND:VEVENT\nEND:VCALENDAR";
        var here = IcsFeed.Parse(text, LosAngeles).Events("c", "s", Ny(9, 1), Ny(11, 1)).Single();
        Assert.Equal(TimeSpan.FromHours(-7), here.Start.Offset);
        var inFeedZone = IcsFeed.Parse(text.Replace("BEGIN:VEVENT", "X-WR-TIMEZONE:America/New_York\nBEGIN:VEVENT"), LosAngeles)
            .Events("c", "s", Ny(9, 1), Ny(11, 1)).Single();
        Assert.Equal(Ny(10, 1, 9), inFeedZone.Start);
    }

    [Fact]
    public void Odd_events_no_uid_no_title_no_end_a_duration_and_no_start()
    {
        const string text = """
            BEGIN:VCALENDAR
            BEGIN:VEVENT
            DTSTART:20261001T130000Z
            DURATION:PT1H30M
            END:VEVENT
            BEGIN:VEVENT
            SUMMARY:No start
            END:VEVENT
            BEGIN:VEVENT
            SUMMARY:A day
            DTSTART;VALUE=DATE:20261002
            END:VEVENT
            END:VCALENDAR
            """;
        var events = IcsFeed.Parse(text, NewYork).Events("c", "s", Ny(9, 1), Ny(11, 1));
        Assert.Equal(2, events.Count);
        Assert.Equal("(No title)", events[0].Title);
        Assert.StartsWith("nouid-", events[0].Id);
        Assert.Equal(TimeSpan.FromMinutes(90), events[0].End - events[0].Start);
        Assert.Equal(TimeSpan.FromDays(1), events[1].End - events[1].Start);
    }

    [Fact]
    public void A_changed_occurrence_with_no_series_in_the_feed_still_shows()
    {
        const string text = """
            BEGIN:VCALENDAR
            BEGIN:VEVENT
            UID:lonely
            RECURRENCE-ID:20261001T130000Z
            DTSTART:20261001T150000Z
            DTEND:20261001T160000Z
            SUMMARY:Just this once
            END:VEVENT
            END:VCALENDAR
            """;
        var e = Assert.Single(IcsFeed.Parse(text, NewYork).Events("c", "s", Ny(9, 1), Ny(11, 1)));
        Assert.Equal("Just this once", e.Title);
        Assert.Equal("lonely/20261001T130000Z", e.Id);
    }

    [Fact]
    public void A_newer_copy_of_an_event_wins_and_rdate_adds_a_day()
    {
        const string text = """
            BEGIN:VCALENDAR
            BEGIN:VEVENT
            UID:x
            SEQUENCE:0
            SUMMARY:Old
            DTSTART:20261001T130000Z
            END:VEVENT
            BEGIN:VEVENT
            UID:x
            SEQUENCE:2
            SUMMARY:Review session
            DTSTART:20261001T130000Z
            DTEND:20261001T140000Z
            RDATE:20261008T130000Z
            END:VEVENT
            END:VCALENDAR
            """;
        var events = IcsFeed.Parse(text, NewYork).Events("c", "s", Ny(9, 1), Ny(11, 1));
        Assert.Equal(2, events.Count);
        Assert.All(events, e => Assert.Equal("Review session", e.Title));
        Assert.Equal(new DateTimeOffset(2026, 10, 8, 13, 0, 0, TimeSpan.Zero), events[1].Start);
    }

    [Fact]
    public void An_event_that_started_before_the_window_and_is_still_on_is_in_it()
    {
        var on = Events("google.ics", Ny(9, 1, 10, 30), Ny(9, 1, 10, 45));
        Assert.Equal("CS 101 Lecture", Assert.Single(on).Title);
        Assert.Empty(Events("google.ics", Ny(9, 1, 11, 15), Ny(9, 1, 12)));
    }

    [Fact]
    public void Colours_and_pages_that_aren_t_calendars()
    {
        Assert.Equal("#00AAFF", IcsFeed.HexColor("#00aaff"));
        Assert.Equal("#00AAFF", IcsFeed.HexColor("00AAFFCC"));
        Assert.Null(IcsFeed.HexColor("turquoise"));
        Assert.Null(IcsFeed.HexColor(null));
        Assert.False(IcsFeed.Parse("<html>Sign in</html>").IsCalendar);
    }
}
