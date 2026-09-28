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
