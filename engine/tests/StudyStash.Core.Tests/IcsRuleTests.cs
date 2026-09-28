using StudyStash.Core.Calendar.Ics;

namespace StudyStash.Core.Tests;

/// <summary>Dates, durations and repeat rules as feeds write them.</summary>
public class IcsRuleTests
{
    static DateTime D(int y, int m, int d, int h = 0, int min = 0) => new(y, m, d, h, min, 0);

    static List<DateTime> Expand(string start, string rule, DateTime through) =>
        [.. IcsRule.Expand(IcsTime.Parse(start)!.Value.Value, IcsRule.Parse(rule)!, through)];

    [Fact]
    public void Dates_and_times_in_every_form_a_feed_writes()
    {
        var utc = IcsTime.Parse("20260915T130000Z")!.Value;
        Assert.Equal((D(2026, 9, 15, 13), true, false), (utc.Value, utc.IsUtc, utc.IsDate));
        Assert.Null(utc.Tzid);
        var zoned = IcsTime.Parse("20260915T090000", "\"America/New_York\"")!.Value;
        Assert.Equal("America/New_York", zoned.Tzid);
        Assert.False(zoned.IsUtc);
        var day = IcsTime.Parse("20260915")!.Value;
        Assert.True(day.IsDate);
        Assert.Equal(D(2026, 9, 15), day.Value);
        Assert.Equal(D(2026, 9, 15, 9, 30), IcsTime.Parse("20260915T0930")!.Value.Value);
        Assert.Equal(D(2026, 9, 16), IcsTime.Parse("20260915T240000")!.Value.Value);
        Assert.Null(IcsTime.Parse("2026-09-15"));
        Assert.Null(IcsTime.Parse("20261315"));
        Assert.Null(IcsTime.Parse("20260915T256000"));
        Assert.Null(IcsTime.Parse(""));
    }

    [Fact]
    public void Value_date_makes_a_whole_day_and_lists_keep_their_zone()
    {
        var p = IcsReader.ParseLine("DTSTART;VALUE=DATE:20260915")!;
        Assert.True(IcsTime.Of(p)!.Value.IsDate);
        var ex = IcsReader.ParseLine("EXDATE;TZID=Europe/London:20260901T090000,20260908T090000")!;
        var list = IcsTime.List(ex).ToList();
        Assert.Equal(2, list.Count);
        Assert.All(list, t => Assert.Equal("Europe/London", t.Tzid));
        var rdate = IcsReader.ParseLine("RDATE;VALUE=PERIOD:20260901T090000Z/PT1H")!;
        Assert.Equal(D(2026, 9, 1, 9), IcsTime.List(rdate).Single().Value);
        Assert.Null(IcsTime.Of(null));
    }

    [Fact]
    public void Durations()
    {
        Assert.Equal(TimeSpan.FromMinutes(50), IcsDuration.Parse("PT50M"));
        Assert.Equal(TimeSpan.FromDays(1), IcsDuration.Parse("P1D"));
        Assert.Equal(TimeSpan.FromDays(14), IcsDuration.Parse("P2W"));
        Assert.Equal(new TimeSpan(1, 2, 30, 5), IcsDuration.Parse("P1DT2H30M5S"));
        Assert.Equal(TimeSpan.FromMinutes(-15), IcsDuration.Parse("-PT15M"));
        Assert.Null(IcsDuration.Parse("P"));
        Assert.Null(IcsDuration.Parse("PT"));
        Assert.Null(IcsDuration.Parse("1H"));
    }

    [Fact]
    public void Rules_parse_with_their_parts_and_nonsense_is_refused()
    {
        var r = IcsRule.Parse("FREQ=WEEKLY;INTERVAL=2;BYDAY=MO,-1FR,2SU;WKST=SU;UNTIL=20261211T235959Z;BYSETPOS=-1")!;
        Assert.Equal("WEEKLY", r.Freq);
        Assert.Equal(2, r.Interval);
        Assert.Equal([(0, DayOfWeek.Monday), (-1, DayOfWeek.Friday), (2, DayOfWeek.Sunday)], r.ByDay);
        Assert.Equal(DayOfWeek.Sunday, r.WeekStart);
        Assert.True(r.Until!.Value.IsUtc);
        Assert.Equal([-1], r.BySetPos);
        Assert.Null(IcsRule.Parse("INTERVAL=2"));
        Assert.Null(IcsRule.Parse("FREQ=SECONDLY"));
        Assert.Equal(1, IcsRule.Parse("FREQ=DAILY;INTERVAL=0")!.Interval);
    }

    [Fact]
    public void A_class_on_tuesdays_and_thursdays_until_the_end_of_term()
    {
        var all = Expand("20260901T100000", "FREQ=WEEKLY;BYDAY=TU,TH;UNTIL=20260917T235959", D(2027, 1, 1));
        Assert.Equal([D(2026, 9, 1, 10), D(2026, 9, 3, 10), D(2026, 9, 8, 10), D(2026, 9, 10, 10), D(2026, 9, 15, 10), D(2026, 9, 17, 10)], all);
    }

    [Fact]
    public void Every_other_week_counted_from_the_week_it_starts()
    {
        // Starts Wednesday 2 Sep; weeks start Monday, so the next Mondays are the 14th and the 28th.
        var all = Expand("20260902T140000", "FREQ=WEEKLY;INTERVAL=2;BYDAY=MO,WE;COUNT=5", D(2027, 1, 1));
        Assert.Equal([D(2026, 9, 2, 14), D(2026, 9, 14, 14), D(2026, 9, 16, 14), D(2026, 9, 28, 14), D(2026, 9, 30, 14)], all);
    }

    [Fact]
    public void Week_start_changes_which_days_are_in_an_every_other_week()
    {
        // RFC 5545's own example: the same rule gives different days with WKST=MO and WKST=SU.
        Assert.Equal([D(1997, 8, 5, 9), D(1997, 8, 10, 9), D(1997, 8, 19, 9), D(1997, 8, 24, 9)],
            Expand("19970805T090000", "FREQ=WEEKLY;INTERVAL=2;COUNT=4;BYDAY=TU,SU;WKST=MO", D(1998, 1, 1)));
        Assert.Equal([D(1997, 8, 5, 9), D(1997, 8, 17, 9), D(1997, 8, 19, 9), D(1997, 8, 31, 9)],
            Expand("19970805T090000", "FREQ=WEEKLY;INTERVAL=2;COUNT=4;BYDAY=TU,SU;WKST=SU", D(1998, 1, 1)));
    }

    [Fact]
    public void Daily_with_count_interval_and_weekdays_only()
    {
        Assert.Equal([D(2026, 9, 1, 8), D(2026, 9, 4, 8), D(2026, 9, 7, 8)], Expand("20260901T080000", "FREQ=DAILY;INTERVAL=3;COUNT=3", D(2027, 1, 1)));
        var weekdays = Expand("20260904T080000", "FREQ=DAILY;BYDAY=MO,TU,WE,TH,FR;COUNT=4", D(2027, 1, 1));
        Assert.Equal([D(2026, 9, 4, 8), D(2026, 9, 7, 8), D(2026, 9, 8, 8), D(2026, 9, 9, 8)], weekdays);
    }

    [Fact]
    public void Monthly_by_day_of_month_by_nth_weekday_and_the_last_weekday()
    {
        // The 31st: months without one are skipped, not moved.
        Assert.Equal([D(2026, 1, 31), D(2026, 3, 31), D(2026, 5, 31)], Expand("20260131", "FREQ=MONTHLY;COUNT=3", D(2027, 1, 1)));
        Assert.Equal([D(2026, 9, 30), D(2026, 10, 31), D(2026, 11, 30)], Expand("20260930", "FREQ=MONTHLY;BYMONTHDAY=-1;COUNT=3", D(2027, 1, 1)));
        Assert.Equal([D(2026, 9, 14, 18), D(2026, 10, 12, 18), D(2026, 11, 9, 18)], Expand("20260914T180000", "FREQ=MONTHLY;BYDAY=2MO;COUNT=3", D(2027, 1, 1)));
        Assert.Equal([D(2026, 9, 25), D(2026, 10, 30), D(2026, 11, 27)], Expand("20260925", "FREQ=MONTHLY;BYDAY=-1FR;COUNT=3", D(2027, 1, 1)));
        // The last weekday of the month, by BYSETPOS.
        Assert.Equal([D(2026, 9, 30), D(2026, 10, 30), D(2026, 11, 30)], Expand("20260930", "FREQ=MONTHLY;BYDAY=MO,TU,WE,TH,FR;BYSETPOS=-1;COUNT=3", D(2027, 1, 1)));
        // Friday the 13th.
        Assert.Equal([D(2026, 11, 13), D(2027, 8, 13)], Expand("20261113", "FREQ=MONTHLY;BYDAY=FR;BYMONTHDAY=13;COUNT=2", D(2028, 1, 1)));
    }

    [Fact]
    public void Yearly_by_month_and_weekday_as_time_zones_and_holidays_write_it()
    {
        // The US's clocks: the second Sunday of March, the first of November.
        Assert.Equal([D(2026, 3, 8, 2), D(2027, 3, 14, 2)], Expand("20070311T020000", "FREQ=YEARLY;BYMONTH=3;BYDAY=2SU", D(2027, 12, 31)).Where(t => t.Year >= 2026));
        Assert.Equal([D(2026, 11, 1, 2)], Expand("20071104T020000", "FREQ=YEARLY;BYMONTH=11;BYDAY=1SU", D(2026, 12, 31)).Where(t => t.Year == 2026));
        // Europe's: the last Sunday of March.
        Assert.Contains(D(2026, 3, 29, 1), Expand("19810329T010000", "FREQ=YEARLY;BYMONTH=3;BYDAY=-1SU", D(2026, 12, 31)));
        // A birthday on 29 February comes only in leap years.
        Assert.Equal([D(2024, 2, 29), D(2028, 2, 29)], Expand("20240229", "FREQ=YEARLY;COUNT=2", D(2030, 1, 1)));
        // "The 20th Monday of the year".
        Assert.Equal([D(1997, 5, 19, 9), D(1998, 5, 18, 9)], Expand("19970519T090000", "FREQ=YEARLY;BYDAY=20MO;COUNT=2", D(1999, 1, 1)));
    }

    [Fact]
    public void Expansion_stops_at_the_window_the_count_and_until_and_never_runs_away()
    {
        Assert.Equal(3, Expand("20260901T090000", "FREQ=DAILY", D(2026, 9, 3, 9)).Count);
        Assert.Empty(Expand("20260901T090000", "FREQ=DAILY", D(2026, 8, 1)));
        Assert.Equal([D(2026, 9, 1, 9)], Expand("20260901T090000", "FREQ=DAILY;COUNT=1", D(2027, 1, 1)));
        // An UNTIL on a date runs through that whole day.
        Assert.Equal(3, Expand("20260901T090000", "FREQ=DAILY;UNTIL=20260903", D(2027, 1, 1)).Count);
        // An UNTIL before the start: nothing at all.
        Assert.Empty(Expand("20260901T090000", "FREQ=DAILY;UNTIL=20250101", D(2027, 1, 1)));
        // A weekly rule on no day that exists in the months it's limited to ends by the window, not forever.
        Assert.Single(Expand("20260901T090000", "FREQ=YEARLY;BYMONTH=2;BYMONTHDAY=30", D(2400, 1, 1)));
        // Decades of a daily class since 2000 are fine.
        Assert.Equal(new DateTime(2026, 9, 28, 9, 0, 0), Expand("20000101T090000", "FREQ=DAILY", D(2026, 9, 28, 12))[^1]);
    }
}
