using System.Text.Json.Nodes;
using StudyStash.Core.Calendar;

namespace StudyStash.Core.Tests;

/// <summary>calendars.json: the sources, their calendars, which are shown, and auto-record's classes.</summary>
public class CalendarSettingsTests
{
    [Fact]
    public void No_file_means_no_calendars_and_a_broken_one_too()
    {
        using var dir = new TempDir();
        var s = CalendarSettings.Load(dir.Path);
        Assert.Empty(s.Sources);
        Assert.Empty(s.AutoRecordClasses);
        File.WriteAllText(CalendarSettings.PathIn(dir.Path), "{ not json");
        Assert.Empty(CalendarSettings.Load(dir.Path).Sources);
    }

    [Fact]
    public void Saves_and_loads_in_the_shape_the_brief_names()
    {
        using var dir = new TempDir();
        CalendarSettings.Update(dir.Path, s =>
        {
            s.Sources.Add(new CalendarSourceSettings { Id = "ics:abc", Kind = "ics", Name = "School", Url = "webcal://example.edu/cal.ics" });
            s.Calendars.Add(new CalendarInfo("ics:abc", "ics:abc", "Timetable", "#3366FF", true));
            s.Enabled["ics:abc"] = false;
        });
        var json = JsonNode.Parse(File.ReadAllText(CalendarSettings.PathIn(dir.Path)))!.AsObject();
        Assert.NotNull(json["autoRecordClasses"]);
        Assert.Equal("webcal://example.edu/cal.ics", json["sources"]![0]!["url"]!.GetValue<string>());
        Assert.False(json["enabled"]!["ics:abc"]!.GetValue<bool>());
        var back = CalendarSettings.Load(dir.Path);
        Assert.Equal("School", back.Sources.Single().Name);
        Assert.Equal("#3366FF", back.Calendars.Single().Color);
        Assert.False(back.IsEnabled(back.Calendars[0]));
        if (!OperatingSystem.IsWindows())
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(CalendarSettings.PathIn(dir.Path)));
    }

    [Fact]
    public void A_calendar_follows_its_source_until_the_student_chooses()
    {
        var s = new CalendarSettings();
        var on = new CalendarInfo("a", "src", "A", null, true);
        var off = new CalendarInfo("b", "src", "B", null, false);
        s.Calendars.AddRange([on, off]);
        Assert.True(s.IsEnabled(on));
        Assert.False(s.IsEnabled(off));
        s.Enabled["b"] = true;
        Assert.Equal(["a", "b"], s.EnabledCalendars("src").Select(c => c.Id));
        Assert.Empty(s.EnabledCalendars("other"));
    }

    [Fact]
    public void Listing_again_replaces_a_source_s_calendars_in_place_and_forgets_choices_about_gone_ones()
    {
        var s = new CalendarSettings();
        s.Calendars.AddRange([new("x1", "x", "X", null, true), new("a1", "a", "A1", null, true), new("a2", "a", "A2", null, true), new("y1", "y", "Y", null, true)]);
        s.Enabled["a2"] = false;
        s.Enabled["a1"] = false;
        s.Listed("a", [new CalendarInfo("a1", "a", "A1 renamed", "#FF0000", true), new CalendarInfo("a3", "a", "A3", null, true)]);
        Assert.Equal(["x1", "a1", "a3", "y1"], s.Calendars.Select(c => c.Id));
        Assert.Equal("A1 renamed", s.Calendars[1].Name);
        Assert.False(s.Enabled["a1"]);
        Assert.False(s.Enabled.ContainsKey("a2"));
        // A source listed for the first time goes at the end.
        s.Listed("new", [new CalendarInfo("n1", "new", "N", null, true)]);
        Assert.Equal("n1", s.Calendars[^1].Id);
    }

    [Fact]
    public void Removing_a_source_takes_its_calendars_choices_and_problem_with_it()
    {
        var s = new CalendarSettings();
        s.Sources.AddRange([new() { Id = "a" }, new() { Id = "b" }]);
        s.Calendars.AddRange([new("a1", "a", "A", null, true), new("b1", "b", "B", null, true)]);
        s.Enabled["a1"] = false;
        s.Enabled["b1"] = false;
        s.Problems["a"] = "Couldn't read it";
        s.Remove("a");
        Assert.Equal(["b"], s.Sources.Select(x => x.Id));
        Assert.Equal(["b1"], s.Calendars.Select(c => c.Id));
        Assert.Equal(["b1"], s.Enabled.Keys);
        Assert.Empty(s.Problems);
        Assert.Null(s.Source("a"));
        Assert.NotNull(s.Source("b"));
    }
}
