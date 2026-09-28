using StudyStash.Core;
using StudyStash.Core.Calendar;

namespace StudyStash.App.Services;

/// <summary>
/// Calendars, on the computer that records: what's coming up is read every five minutes and sent on to the library,
/// and a recording started during an event (or just before one) takes its title and class from it.
/// </summary>
public sealed partial class AppHost
{
    readonly LaptopHost laptopHost;
    readonly Lock calendarsLock = new();
    Upcoming? calendars;
    UpcomingSender? calendarSender;

    /// <summary>What's coming up on the student's calendars (read from its saved copy until the first read).</summary>
    public Upcoming Calendars
    {
        get
        {
            lock (calendarsLock)
            {
                if (calendars is not null) return calendars;
                calendars = new Upcoming(Home, CalendarKinds.Default, log: log);
                calendarSender = new UpcomingSender(() => calendars.Events, Client, laptopHost, CalendarClasses, log);
                HasCalendars = CalendarSettings.Load(Home).Sources.Count > 0;
                calendars.Changed += () =>
                {
                    HasCalendars = CalendarSettings.Load(Home).Sources.Count > 0;
                    _ = calendarSender.SendAsync();
                    Changed?.Invoke();
                };
                return calendars;
            }
        }
    }

    /// <summary>The student has added a calendar (as of the last read, or Settings' last change).</summary>
    public bool HasCalendars { get; private set; }

    /// <summary>The library's classes as calendar matching sees them: names, other names and Canvas codes.</summary>
    public IReadOnlyList<ClassHint> CalendarClasses() => EventClass.From(Overview);

    /// <summary>The event on now (or starting within ten minutes) and its class; null when there's none.</summary>
    public EventNow? EventNow() => Calendars.During(CalendarClasses());

    /// <summary>A library-only computer records nothing, so it reads no calendars.</summary>
    void StartCalendars()
    {
        if (Settings.Role == AppRole.Library) return;
        var upcoming = Calendars;
        running.Add(Task.Run(() => upcoming.RunAsync(stop.Token)));
    }

    /// <summary>What a recording for <paramref name="className"/> ("" when none was picked) starts as, given what's on.</summary>
    RecordingStart CalendarStart(string className)
    {
        try
        {
            return RecordingEvents.For(className, EventNow(), [.. Classes().Select(c => c.Name)], RecordingEvents.Names(CalendarSettings.Load(Home)));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            log($"[calendar] {e.Message}");
            return new RecordingStart(className, "", null);
        }
    }

    /// <summary>The lecture named by its event, with the event kept, when there was one.</summary>
    Lecture WithEvent(Lecture l, RecordingStart start)
    {
        if (start.Event is null) return l;
        log($"[calendar] recording during \"{start.Title}\"");
        return Lectures.Update(l.Id, x =>
        {
            x.Title = start.Title;
            x.Event = start.Event;
        }) ?? l;
    }
}
