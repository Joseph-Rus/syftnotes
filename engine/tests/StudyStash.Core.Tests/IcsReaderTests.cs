using StudyStash.Core.Calendar.Ics;

namespace StudyStash.Core.Tests;

/// <summary>Reading iCalendar's lines: folding, parameters, escapes, and what's broken skipped.</summary>
public class IcsReaderTests
{
    [Fact]
    public void Folded_lines_join_whatever_the_line_endings()
    {
        string text = "﻿BEGIN:VCALENDAR\r\nX-WR-CALNAME:Long\r\n  name\r\n\tagain\nSUMMARY:x\rEND:VCALENDAR\r\n";
        Assert.Equal(["BEGIN:VCALENDAR", "X-WR-CALNAME:Long nameagain", "SUMMARY:x", "END:VCALENDAR"], IcsReader.Unfold(text));
    }

    [Fact]
    public void A_fold_can_land_in_the_middle_of_a_word()
    {
        // Outlook folds at 75 octets, which can land mid-word; the fold's one space goes, nothing else does.
        var cal = IcsReader.Parse("BEGIN:VCALENDAR\nBEGIN:VEVENT\nSUMMARY:Intro to Algor\n ithms · Lecture\nEND:VEVENT\nEND:VCALENDAR");
        Assert.Equal("Intro to Algorithms · Lecture", cal[0].Blocks("VEVENT").Single().Text("SUMMARY"));
    }

    [Fact]
    public void Parameters_quoted_or_not_and_the_value_after_the_first_unquoted_colon()
    {
        var p = IcsReader.ParseLine("ATTENDEE;CN=\"Doe, Jane: TA\";ROLE=REQ-PARTICIPANT;RSVP=TRUE:mailto:jane@uni.edu")!;
        Assert.Equal("ATTENDEE", p.Name);
        Assert.Equal("Doe, Jane: TA", p.Param("CN"));
        Assert.Equal("REQ-PARTICIPANT", p.Param("role"));
        Assert.Equal("mailto:jane@uni.edu", p.Value);
        var d = IcsReader.ParseLine("dtstart;tzid=America/New_York:20260915T090000")!;
        Assert.Equal("DTSTART", d.Name);
        Assert.Equal("America/New_York", d.Param("TZID"));
        Assert.Equal("20260915T090000", d.Value);
        Assert.Null(IcsReader.ParseLine("no colon here"));
        Assert.Null(IcsReader.ParseLine(":no name"));
        Assert.Equal("", IcsReader.ParseLine("DESCRIPTION:")!.Value);
    }

    [Fact]
    public void Text_escapes_are_undone()
    {
        Assert.Equal("Room 101, Hall; bring laptop\nand notes\\", IcsText.Unescape(@"Room 101\, Hall\; bring laptop\nand notes\\"));
        Assert.Equal("Line\nTwo", IcsText.Unescape(@"Line\NTwo"));
        Assert.Equal(["20260901T090000", "20260908T090000"], IcsText.Split("20260901T090000, 20260908T090000,"));
        Assert.Equal([@"a\,b", "c"], IcsText.Split(@"a\,b,c"));
    }

    [Fact]
    public void Blocks_nest_and_a_stray_end_or_broken_line_is_skipped()
    {
        string text = """
            BEGIN:VCALENDAR
            X-WR-CALNAME:Classes
            BEGIN:VTIMEZONE
            TZID:Europe/London
            BEGIN:STANDARD
            TZOFFSETTO:+0000
            END:STANDARD
            END:VTIMEZONE
            END:VALARM
            BEGIN:VEVENT
            UID:1
            garbage line
            BEGIN:VALARM
            ACTION:DISPLAY
            END:VALARM
            END:VEVENT
            BEGIN:VEVENT
            UID:2
            END:VCALENDAR
            """;
        var cals = IcsReader.Parse(text);
        var cal = Assert.Single(cals);
        Assert.Equal("Classes", cal.Text("X-WR-CALNAME"));
        Assert.Equal("+0000", cal.Blocks("VTIMEZONE").Single().Blocks("STANDARD").Single().First("TZOFFSETTO")!.Value);
        var events = cal.Blocks("VEVENT").ToList();
        Assert.Equal(["1", "2"], events.Select(e => e.Text("UID")));
        Assert.Single(events[0].Children);
        Assert.Null(events[0].First("GARBAGE LINE"));
    }

    [Fact]
    public void Nothing_that_isn_t_a_calendar_reads_as_one()
    {
        Assert.Empty(IcsReader.Parse(""));
        Assert.Empty(IcsReader.Parse("<html><body>Sign in</body></html>"));
    }
}
