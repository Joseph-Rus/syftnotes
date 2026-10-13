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
    /// <param name="forEvent">The event the student picked to record; null for whatever is on now.</param>
    RecordingStart CalendarStart(string className, CalendarEvent? forEvent = null)
    {
        try
        {
            var now = forEvent is null ? EventNow() : new EventNow(forEvent, EventClass.For(forEvent.Title, CalendarClasses()));
            return RecordingEvents.For(className, now, [.. Classes().Select(c => c.Name)], RecordingEvents.Names(CalendarSettings.Load(Home)));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            log($"[calendar] {e.Message}");
            return new RecordingStart(className, "", null);
        }
    }

    /// <summary>
    /// The note for an event that hasn't started: opened from Coming up or Up next, it isn't recorded yet. It's named
    /// and filed as the event's recording would be, what's typed in it is kept, and recording starts into it when the
    /// event does (<see cref="DuePlan"/>) or when the student starts it sooner. One already planned for the event is
    /// the one handed back.
    /// </summary>
    public Lecture PlanEvent(CalendarEvent e)
    {
        if (Lectures.All().FirstOrDefault(l => l.State == LectureState.Planned && l.Event?.Id == e.Id) is { } planned) return planned;
        var start = CalendarStart("", e);
        var l = Lectures.Add(new Lecture
        {
            Id = Lecture.NewId(e.Start), Started = e.Start.ToString("yyyy-MM-dd'T'HH:mm:sszzz", System.Globalization.CultureInfo.InvariantCulture),
            ClassName = start.ClassName, Owner = Client().DisplayName, AfterClass = !Settings.LiveTranscript, State = LectureState.Planned,
            Title = start.Title.Length > 0 ? start.Title : e.Title, Event = start.Event ?? RecordedEvent.From(e, ""),
        });
        log($"[calendar] planned a note for \"{e.Title}\" at {e.Start:HH:mm}");
        return l;
    }

    /// <summary>When a planned note's event starts; null when it has none that reads.</summary>
    public static DateTimeOffset? PlanStarts(Lecture l) =>
        l.Event is { } e && DateTimeOffset.TryParse(e.Start, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var at) ? at : null;

    static DateTimeOffset? PlanEnds(Lecture l) =>
        l.Event is { } e && DateTimeOffset.TryParse(e.End, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var at) ? at : null;

    /// <summary>A planned note whose event has started and not ended: it's time to record it. Null when there's none.</summary>
    public Lecture? DuePlan(DateTimeOffset now) => Lectures.All().FirstOrDefault(l => l.State == LectureState.Planned
        && PlanStarts(l) is { } starts && starts <= now && (PlanEnds(l) is not { } ends || now < ends));

    /// <summary>Planned notes whose event ended without being recorded and with nothing typed in them go: there's
    /// nothing in them to keep. One with notes typed stays (Keep recording can still record it).</summary>
    public void ForgetEndedPlans(DateTimeOffset now)
    {
        foreach (var l in Lectures.All().Where(l => l.State == LectureState.Planned && l.Notes.Trim().Length == 0 && PlanEnds(l) is { } ends && ends <= now))
        {
            log($"[calendar] {l.Id}: its event ended without a recording or notes: let go");
            Lectures.Delete(l.Id);
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
