using System.Text.Json;
using StudyStash.App.Services;
using StudyStash.Audio;
using StudyStash.Core;
using StudyStash.Core.Calendar;

namespace StudyStash.App.Tests;

/// <summary>The app's calendars: a recording started during an event takes its title and keeps the event.</summary>
[Collection(nameof(EnvironmentTests))]
public class AppCalendarTests
{
    static AppHost Host(TempHome home)
    {
        string wav = home["speech.wav"];
        using (var w = new WavWriter(wav)) w.Write(new float[Sound.Rate / 2]);
        return new AppHost(home.Path, () => new FileMicrophone(wav), () => throw new InvalidOperationException("no model here"), log: _ => { },
            loginItems: new CountingLoginItems(), models: new ModelSetting(File: home["model.bin"]));
    }

    /// <summary>What's coming up, saved as the last read left it: one event, on from 10 minutes ago.</summary>
    static void OnNow(TempHome home, string title)
    {
        var now = DateTimeOffset.Now;
        var e = new CalendarEvent("uid/1", "cal1", "ics:abc", title, now.AddMinutes(-10), now.AddMinutes(65), false, "Hall 204", ["Sam Lee"], null, false);
        File.WriteAllText(Upcoming.PathIn(home.Path), JsonSerializer.Serialize(new UpcomingCache(now, [e]), new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
        CalendarSettings.Update(home.Path, s => s.Calendars.Add(new CalendarInfo("cal1", "ics:abc", "Fall 2026 classes", null, true)));
    }

    [Fact]
    public async Task Recording_during_an_event_is_named_by_it_and_keeps_it()
    {
        using var home = new TempHome();
        OnNow(home, "CS 101 Lecture");
        using var host = Host(home);
        Assert.Equal("CS 101 Lecture", host.EventNow()!.Event.Title);
        var start = await host.RecordAsync("");
        // Read while it records: a recording this short is dropped when it stops.
        var kept = host.Lectures.Get(start.Lecture!.Id)!;
        host.StopRecording();
        Assert.Equal("CS 101 Lecture", kept.Title);
        Assert.Equal("Fall 2026 classes", kept.Event!.Calendar);
        Assert.Equal("Hall 204", kept.Payload()["raw"]!["event"]!["location"]!.GetValue<string>());
    }

    /// <summary>An event that hasn't started, clicked: its note is planned, not recorded. It records when the event
    /// starts (or sooner, from its page), with what was typed in it; one that ends unrecorded and empty goes.</summary>
    [Fact]
    public async Task An_event_clicked_before_it_starts_is_planned_and_records_when_it_starts()
    {
        using var home = new TempHome();
        var now = DateTimeOffset.Now;
        var e = new CalendarEvent("uid/2", "cal1", "ics:abc", "Data Mining", now.AddMinutes(30), now.AddMinutes(120), false, "TEGR 301", [], null, false);
        File.WriteAllText(Upcoming.PathIn(home.Path), JsonSerializer.Serialize(new UpcomingCache(now, [e]), new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
        CalendarSettings.Update(home.Path, s => s.Calendars.Add(new CalendarInfo("cal1", "ics:abc", "School", null, true)));
        using var host = Host(home);

        var plan = host.PlanEvent(e);
        Assert.Equal(LectureState.Planned, plan.State);
        Assert.Equal("Data Mining", plan.Title);
        Assert.Null(host.Recorder.Current); // nothing records yet
        Assert.Same(plan, host.PlanEvent(e)); // the one note for the event
        Assert.Null(host.DuePlan(now.AddMinutes(31))); // only opened, nothing typed: it doesn't record on its own

        host.KeepTypedNotes(plan.Id, "- bring the slides");
        Assert.Null(host.DuePlan(now));
        Assert.Equal(plan.Id, host.DuePlan(now.AddMinutes(31))?.Id);
        Assert.Null(host.DuePlan(now.AddMinutes(121))); // over: too late to start on its own
        var start = await host.ContinueAsync(plan.Id);
        Assert.Equal(plan.Id, start.Lecture!.Id);
        Assert.Equal(LectureState.Recording, start.Lecture.State);
        Assert.Equal("- bring the slides", start.Lecture.Notes);
        Assert.True(start.Lecture.StartedAt <= DateTimeOffset.Now.AddSeconds(1) && start.Lecture.StartedAt > now.AddMinutes(-1)); // started now, not at 30 min
        host.StopRecording(); // a moment only: it's planned again
        Assert.Equal(LectureState.Planned, host.Lectures.Get(plan.Id)!.State);

        // Over, never recorded: with notes typed it stays; empty, it goes.
        host.ForgetEndedPlans(now.AddMinutes(121));
        Assert.NotNull(host.Lectures.Get(plan.Id));
        host.KeepTypedNotes(plan.Id, "");
        host.ForgetEndedPlans(now.AddMinutes(121));
        Assert.Null(host.Lectures.Get(plan.Id));
    }

    /// <summary>An event's note opened and left empty leaves nothing: it goes once it's not the one open. One with
    /// something typed in it stays to record.</summary>
    [Fact]
    public void A_planned_note_left_empty_goes()
    {
        using var home = new TempHome();
        var now = DateTimeOffset.Now;
        var a = new CalendarEvent("uid/3", "cal1", "ics:abc", "Compilers", now.AddHours(18), now.AddHours(19), false, "TEGR 302", [], null, false);
        var b = new CalendarEvent("uid/4", "cal1", "ics:abc", "Data Mining", now.AddHours(20), now.AddHours(21), false, "TEGR 301", [], null, false);
        File.WriteAllText(Upcoming.PathIn(home.Path), JsonSerializer.Serialize(new UpcomingCache(now, [a, b]), new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
        CalendarSettings.Update(home.Path, s => s.Calendars.Add(new CalendarInfo("cal1", "ics:abc", "School", null, true)));
        using var host = Host(home);

        var empty = host.PlanEvent(a);
        var typed = host.PlanEvent(b);
        host.KeepTypedNotes(typed.Id, "- read chapter 4");
        host.ForgetEmptyPlans(keep: empty.Id); // still open
        Assert.NotNull(host.Lectures.Get(empty.Id));
        host.ForgetEmptyPlans(keep: null); // left
        Assert.Null(host.Lectures.Get(empty.Id));
        Assert.NotNull(host.Lectures.Get(typed.Id));
    }

    [Fact]
    public async Task Recording_with_nothing_on_is_as_before()
    {
        using var home = new TempHome();
        using var host = Host(home);
        Assert.Null(host.EventNow());
        var l = host.Lectures.Get((await host.RecordAsync("")).Lecture!.Id)!;
        host.StopRecording();
        Assert.Equal("", l.Title);
        Assert.Null(l.Event);
    }
}
