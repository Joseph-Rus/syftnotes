using StudyStash.Core.Calendar.Ics;

namespace StudyStash.Core.Tests;

/// <summary>Time zones by name (IANA, Windows, prefixed) and by the feed's own VTIMEZONE rules.</summary>
public class IcsZoneTests
{
    static readonly Dictionary<string, IcsComponent> None = [];

    /// <summary>Outlook's own zone for US Eastern, under a name no computer knows.</summary>
    const string OutlookEastern = """
        BEGIN:VCALENDAR
        BEGIN:VTIMEZONE
        TZID:Customized Time Zone
        BEGIN:STANDARD
        DTSTART:16010101T020000
        TZOFFSETFROM:-0400
        TZOFFSETTO:-0500
        RRULE:FREQ=YEARLY;INTERVAL=1;BYDAY=1SU;BYMONTH=11
        END:STANDARD
        BEGIN:DAYLIGHT
        DTSTART:16010101T020000
        TZOFFSETFROM:-0500
        TZOFFSETTO:-0400
        RRULE:FREQ=YEARLY;INTERVAL=1;BYDAY=2SU;BYMONTH=3
        END:DAYLIGHT
        END:VTIMEZONE
        END:VCALENDAR
        """;

    static Dictionary<string, IcsComponent> Blocks(string text) =>
        IcsReader.Parse(text)[0].Blocks("VTIMEZONE").ToDictionary(b => b.Text("TZID")!);

    [Theory]
    [InlineData("America/New_York")]
    [InlineData("Eastern Standard Time")]
    [InlineData("\"America/New_York\"")]
    [InlineData("/mozilla.org/20050126_1/America/New_York")]
    public void Every_way_of_naming_new_york_is_new_york(string tzid)
    {
        var zone = IcsZones.Find(tzid, None)!;
        Assert.Equal(TimeSpan.FromHours(-4), zone.OffsetAt(new DateTime(2026, 9, 15, 9, 0, 0)));
        Assert.Equal(TimeSpan.FromHours(-5), zone.OffsetAt(new DateTime(2026, 12, 15, 9, 0, 0)));
    }

    [Fact]
    public void An_unknown_name_without_rules_is_no_zone()
    {
        Assert.Null(IcsZones.Find("Somewhere/Nowhere", None));
        Assert.Null(IcsZones.Find("Customized Time Zone", None));
    }

    [Fact]
    public void Other_spellings_of_well_known_zones_are_found()
    {
        Assert.NotNull(IcsZones.Find("Asia/Kolkata", None));
        Assert.NotNull(IcsZones.Find("W. Europe Standard Time", None));
        Assert.Equal(TimeSpan.Zero, IcsZones.Find("UTC", None)!.OffsetAt(new DateTime(2026, 7, 1)));
    }

    [Fact]
    public void A_feed_s_own_rules_give_the_offset_through_the_year()
    {
        var zone = IcsZones.Find("Customized Time Zone", Blocks(OutlookEastern))!;
        Assert.IsType<IcsRulesZone>(zone);
        Assert.Equal(TimeSpan.FromHours(-5), zone.OffsetAt(new DateTime(2026, 1, 20, 9, 0, 0)));
        Assert.Equal(TimeSpan.FromHours(-4), zone.OffsetAt(new DateTime(2026, 9, 15, 9, 0, 0)));
        Assert.Equal(TimeSpan.FromHours(-5), zone.OffsetAt(new DateTime(2026, 12, 15, 9, 0, 0)));
        // The day the clocks go forward (8 March 2026): before 2:00 still standard, after 3:00 daylight.
        Assert.Equal(TimeSpan.FromHours(-5), zone.OffsetAt(new DateTime(2026, 3, 8, 1, 59, 0)));
        Assert.Equal(TimeSpan.FromHours(-4), zone.OffsetAt(new DateTime(2026, 3, 8, 3, 0, 0)));
        // The day they go back (1 November 2026): 1:30 happens twice, the first (daylight) is kept; 2:30 is standard.
        Assert.Equal(TimeSpan.FromHours(-4), zone.OffsetAt(new DateTime(2026, 11, 1, 1, 30, 0)));
        Assert.Equal(TimeSpan.FromHours(-5), zone.OffsetAt(new DateTime(2026, 11, 1, 2, 30, 0)));
    }

    [Fact]
    public void Rules_and_the_system_agree_on_skipped_and_repeated_hours()
    {
        var system = IcsZones.Find("America/New_York", None)!;
        var rules = IcsZones.Find("Customized Time Zone", Blocks(OutlookEastern))!;
        foreach (var local in new[] { new DateTime(2026, 3, 8, 2, 30, 0), new DateTime(2026, 11, 1, 1, 30, 0), new DateTime(2026, 11, 1, 2, 30, 0), new DateTime(2026, 6, 1, 12, 0, 0) })
            Assert.Equal(system.At(local), rules.At(local));
        // 2:30 on the night the clocks spring forward doesn't exist: it lands at 3:30, just after the jump.
        Assert.Equal(new DateTimeOffset(2026, 3, 8, 7, 30, 0, TimeSpan.Zero), system.At(new DateTime(2026, 3, 8, 2, 30, 0)));
    }

    [Fact]
    public void Wall_clock_back_from_a_moment()
    {
        var at = new DateTimeOffset(2026, 9, 15, 13, 0, 0, TimeSpan.Zero);
        Assert.Equal(new DateTime(2026, 9, 15, 9, 0, 0), IcsZones.Find("America/New_York", None)!.Local(at));
        Assert.Equal(new DateTime(2026, 9, 15, 9, 0, 0), IcsZones.Find("Customized Time Zone", Blocks(OutlookEastern))!.Local(at));
        Assert.Equal(new DateTime(2026, 9, 15, 13, 0, 0), IcsZone.Utc.Local(at));
    }

    [Fact]
    public void A_zone_with_one_fixed_offset_and_no_rules()
    {
        var zone = IcsZones.Find("Asia/Custom", Blocks("""
            BEGIN:VCALENDAR
            BEGIN:VTIMEZONE
            TZID:Asia/Custom
            BEGIN:STANDARD
            DTSTART:19700101T000000
            TZOFFSETFROM:+0530
            TZOFFSETTO:+0530
            END:STANDARD
            END:VTIMEZONE
            END:VCALENDAR
            """))!;
        Assert.Equal(new TimeSpan(5, 30, 0), zone.OffsetAt(new DateTime(2026, 9, 15)));
        Assert.Equal(new TimeSpan(5, 30, 0), IcsRulesZone.Offset("+0530"));
        Assert.Equal(new TimeSpan(-3, -30, 0), IcsRulesZone.Offset("-0330"));
        Assert.Null(IcsRulesZone.Offset("0530"));
    }

    [Fact]
    public void Old_rules_that_ended_don_t_come_back()
    {
        // US rules before 2007 (first Sunday of April) ended with UNTIL; only the 2007 ones apply in 2026.
        var zone = IcsZones.Find("US-Eastern-Old", Blocks("""
            BEGIN:VCALENDAR
            BEGIN:VTIMEZONE
            TZID:US-Eastern-Old
            BEGIN:DAYLIGHT
            DTSTART:19870405T020000
            TZOFFSETFROM:-0500
            TZOFFSETTO:-0400
            RRULE:FREQ=YEARLY;BYMONTH=4;BYDAY=1SU;UNTIL=20060402T070000Z
            END:DAYLIGHT
            BEGIN:DAYLIGHT
            DTSTART:20070311T020000
            TZOFFSETFROM:-0500
            TZOFFSETTO:-0400
            RRULE:FREQ=YEARLY;BYMONTH=3;BYDAY=2SU
            END:DAYLIGHT
            BEGIN:STANDARD
            DTSTART:20071104T020000
            TZOFFSETFROM:-0400
            TZOFFSETTO:-0500
            RRULE:FREQ=YEARLY;BYMONTH=11;BYDAY=1SU
            END:STANDARD
            END:VTIMEZONE
            END:VCALENDAR
            """))!;
        Assert.Equal(TimeSpan.FromHours(-4), zone.OffsetAt(new DateTime(2026, 3, 20, 9, 0, 0)));
        Assert.Equal(TimeSpan.FromHours(-5), zone.OffsetAt(new DateTime(2026, 3, 1, 9, 0, 0)));
    }
}
