using StudyStash.Core.Calendar;

namespace StudyStash.Core.Tests;

/// <summary>A recording started during a calendar event: its title, its class, and the event in what's sent.</summary>
public class RecordingEventTests
{
    static readonly DateTimeOffset At = new(2026, 9, 29, 10, 0, 0, TimeSpan.FromHours(-4));
    static readonly string[] Classes = ["CS 101", "BIO 110"];

    static EventNow Now(string title, string? cls) => new(new CalendarEvent("uid/20260929T140000Z", "cal1", "ics:abc", title, At, At.AddMinutes(75), false,
        "Engineering Hall 204", ["Sam Lee", "alex@uni.edu"], "https://meet.google.com/abc", false), cls);

    static string Name(string id) => id == "cal1" ? "Fall 2026 classes" : "";

    [Fact]
    public void Nothing_on_leaves_the_recording_as_it_was()
    {
        Assert.Equal(new RecordingStart("", "", null), RecordingEvents.For("", null, Classes, Name));
        Assert.Equal(new RecordingStart("BIO 110", "", null), RecordingEvents.For("BIO 110", null, Classes, Name));
    }

    [Fact]
    public void The_event_names_the_lecture_and_picks_its_class()
    {
        var start = RecordingEvents.For("", Now("CS 101 Lecture", "CS 101"), Classes, Name);
        Assert.Equal("CS 101", start.ClassName);
        Assert.Equal("CS 101 Lecture", start.Title);
        Assert.Equal("Fall 2026 classes", start.Event!.Calendar);
        Assert.Equal("2026-09-29T10:00:00-04:00", start.Event.Start);
        Assert.Equal("2026-09-29T11:15:00-04:00", start.Event.End);
        Assert.Equal(["Sam Lee", "alex@uni.edu"], start.Event.Attendees);
    }

    [Fact]
    public void The_student_s_pick_wins_and_a_clash_leaves_the_event_out()
    {
        // Picked the same class, or an event with no class: the event still names it.
        Assert.Equal("CS 101 Lecture", RecordingEvents.For("CS 101", Now("CS 101 Lecture", "CS 101"), Classes, Name).Title);
        var office = RecordingEvents.For("BIO 110", Now("Office hours", null), Classes, Name);
        Assert.Equal(("BIO 110", "Office hours"), (office.ClassName, office.Title));
        // Picked another class than the event's: they're somewhere else; the event isn't this lecture.
        Assert.Equal(new RecordingStart("BIO 110", "", null), RecordingEvents.For("BIO 110", Now("CS 101 Lecture", "CS 101"), Classes, Name));
    }

    [Fact]
    public void A_matched_class_the_library_doesn_t_have_isn_t_used()
    {
        var start = RecordingEvents.For("", Now("MATH 200 Lecture", "MATH 200"), Classes, Name);
        Assert.Equal("", start.ClassName);
        Assert.Equal("MATH 200 Lecture", start.Title);
    }

    [Fact]
    public void The_lecture_keeps_the_event_and_sends_it_in_its_raw_payload()
    {
        using var dir = new TempDir();
        var store = new LectureStore(dir.Path);
        var ev = RecordingEvents.For("", Now("CS 101 Lecture", "CS 101"), Classes, Name).Event!;
        store.Add(new Lecture { Id = "rec-1", Started = "2026-09-29T10:01:00-04:00", ClassName = "CS 101", Title = "CS 101 Lecture", Event = ev });
        var back = new LectureStore(dir.Path).Get("rec-1")!;
        Assert.Equal(ev.Title, back.Event!.Title);
        var payload = back.Payload();
        Assert.Equal("CS 101 Lecture", payload["title"]!.GetValue<string>());
        var raw = payload["raw"]!["event"]!;
        Assert.Equal("CS 101 Lecture", raw["title"]!.GetValue<string>());
        Assert.Equal("Engineering Hall 204", raw["location"]!.GetValue<string>());
        Assert.Equal("Fall 2026 classes", raw["calendar"]!.GetValue<string>());
        Assert.Equal(2, raw["attendees"]!.AsArray().Count);
        Assert.Equal("recorder", payload["raw"]!["source"]!.GetValue<string>());
        // Without an event, the payload is as it always was.
        Assert.Null(new Lecture { Id = "rec-2", Started = "2026-09-29T10:01:00-04:00" }.Payload()["raw"]!["event"]);
    }

    [Fact]
    public void Calendar_names_come_from_the_settings()
    {
        var s = new CalendarSettings { Calendars = [new("c1", "s", "Classes", null, true)] };
        Assert.Equal("Classes", RecordingEvents.Names(s)("c1"));
        Assert.Equal("", RecordingEvents.Names(s)("nope"));
    }
}
