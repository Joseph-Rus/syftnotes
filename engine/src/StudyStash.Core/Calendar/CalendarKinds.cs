using StudyStash.Core.Calendar.Ics;

namespace StudyStash.Core.Calendar;

/// <summary>
/// One kind of calendar source Study Stash knows: its kind ("ics", "apple", "google", "microsoft"), what Settings calls
/// it, how to make a source from what calendars.json keeps (<see cref="Create"/>), and, for a kind the student
/// connects with a button ("Connect Google"), what that button says and does (<see cref="Connect"/>: sign in or ask
/// the system, then hand back the new source's settings, or null when the student backed out). An iCalendar feed has
/// no button: it's added by pasting its address.
/// </summary>
public sealed record CalendarKind(string Kind, string Label, Func<CalendarSourceSettings, ICalendarSource> Create)
{
    /// <summary>The Settings button's words ("Connect Google"); null for a kind without one.</summary>
    public string? ConnectLabel { get; init; }
    /// <summary>The button's icon (a glyph in IconPaths).</summary>
    public string Glyph { get; init; } = "event";
    /// <summary>Connect a new source; null for a kind without a button.</summary>
    public Func<CancellationToken, Task<CalendarSourceSettings?>>? Connect { get; init; }
    /// <summary>At most one source of this kind (Apple Calendar is the computer's own).</summary>
    public bool OnlyOne { get; init; }
}

/// <summary>
/// The calendar kinds this app knows, which every part of calendars asks: <see cref="Upcoming"/> to make each source
/// from its settings, Settings for its Connect buttons. <see cref="Default"/> knows iCalendar feeds; Apple, Google and
/// Microsoft <see cref="Register"/> themselves as the app starts (where the platform has them), and Settings shows
/// their buttons from then on.
/// </summary>
public sealed class CalendarKinds
{
    readonly Lock gate = new();
    readonly List<CalendarKind> kinds = [];

    /// <summary>The app's own: iCalendar feeds, and whatever registers itself.</summary>
    public static CalendarKinds Default { get; } = WithFeeds(new HttpClient());

    /// <summary>A kind was added or replaced: Settings shows its button.</summary>
    public event Action? Changed;

    /// <summary>Kinds that know only iCalendar feeds, downloaded with <paramref name="http"/> (a test's pretend server).</summary>
    public static CalendarKinds WithFeeds(HttpClient http, TimeZoneInfo? local = null)
    {
        var k = new CalendarKinds();
        k.Register(new CalendarKind(IcsSource.KindName, "Calendar feed", s => new IcsSource(s, http, local)) { Glyph = "link" });
        return k;
    }

    /// <summary>Add a kind, or replace the one of the same <see cref="CalendarKind.Kind"/>.</summary>
    public void Register(CalendarKind kind)
    {
        lock (gate)
        {
            kinds.RemoveAll(k => k.Kind == kind.Kind);
            kinds.Add(kind);
        }
        Changed?.Invoke();
    }

    public CalendarKind? Find(string kind)
    {
        lock (gate) return kinds.FirstOrDefault(k => k.Kind == kind);
    }

    /// <summary>Every kind, in the order they registered.</summary>
    public IReadOnlyList<CalendarKind> All
    {
        get
        {
            lock (gate) return [.. kinds];
        }
    }

    /// <summary>The kinds with a Connect button, for Settings; empty (and the buttons hidden) until one registers.</summary>
    public IReadOnlyList<CalendarKind> Connectable => [.. All.Where(k => k.Connect is not null)];

    /// <summary>The source these settings describe, or null when its kind isn't known here (Apple Calendar's settings
    /// read on Windows, or a kind from a newer Study Stash).</summary>
    public ICalendarSource? Create(CalendarSourceSettings s) => Find(s.Kind)?.Create(s);
}
