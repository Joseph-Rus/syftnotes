using System.Text.Json;
using StudyStash.Core.Calendar.Ics;

namespace StudyStash.Core.Calendar;

/// <summary>What's on the student's calendars soon, as saved (calendar_upcoming.json): the events, and when they were read.</summary>
public sealed record UpcomingCache(DateTimeOffset? Updated, List<CalendarEvent> Events);

/// <summary>An event that's on now, or starts soon, and the class it's for (null when none clearly is).</summary>
public sealed record EventNow(CalendarEvent Event, string? Class);

/// <summary>
/// What's coming up on the student's calendars: every shown calendar of every source, read every five minutes (and
/// whenever Settings changes something), put together in time order without the cancelled ones or the same event
/// twice (one class on both a Google calendar and the school's feed), and saved, so the menu shows it the moment it
/// opens, before anything is read again. A source that can't be read keeps what it last had, and Settings says why.
/// </summary>
public sealed class Upcoming
{
    /// <summary>How often calendars are read again.</summary>
    public static readonly TimeSpan Every = TimeSpan.FromMinutes(5);
    /// <summary>How far ahead events are read (and sent to the library).</summary>
    public static readonly TimeSpan Ahead = TimeSpan.FromDays(7);
    /// <summary>A recording started this long before an event is for that event.</summary>
    public static readonly TimeSpan StartsSoon = TimeSpan.FromMinutes(10);

    static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    readonly string home;
    readonly CalendarKinds kinds;
    readonly Func<DateTimeOffset> clock;
    readonly Action<string> log;
    readonly SemaphoreSlim reading = new(1, 1);
    readonly SemaphoreSlim wake = new(0);
    // Made once per source and kept, so a source's own caching (a feed read once a minute) lasts between reads.
    readonly Dictionary<string, (string Settings, ICalendarSource Source)> made = [];
    UpcomingCache cache;

    public Upcoming(string home, CalendarKinds? kinds = null, Func<DateTimeOffset>? clock = null, Action<string>? log = null)
    {
        this.home = home;
        this.kinds = kinds ?? CalendarKinds.Default;
        this.clock = clock ?? (() => DateTimeOffset.UtcNow);
        this.log = log ?? (_ => { });
        cache = Load(home);
    }

    /// <summary>The events, soonest first, as last read.</summary>
    public IReadOnlyList<CalendarEvent> Events => cache.Events;
    /// <summary>When calendars were last read; null before they ever were.</summary>
    public DateTimeOffset? Updated => cache.Updated;

    /// <summary>A read finished (on a worker thread): the events or Settings' problems may have changed.</summary>
    public event Action? Changed;

    public static string PathIn(string home) => Path.Combine(home, "calendar_upcoming.json");

    static UpcomingCache Load(string home)
    {
        try
        {
            if (SharedFile.Read(PathIn(home)) is { } text && JsonSerializer.Deserialize<UpcomingCache>(text, Json) is { } c)
                return c with { Events = c.Events ?? [] };
        }
        catch (JsonException)
        {
        }
        return new UpcomingCache(null, []);
    }

    /// <summary>Read now instead of at the next five minutes (Settings added a source or switched a calendar).</summary>
    public void Wake() => wake.Release();

    /// <summary>Read every calendar until <paramref name="stop"/>: at once, then every <see cref="Every"/> or when woken.</summary>
    public async Task RunAsync(CancellationToken stop)
    {
        while (!stop.IsCancellationRequested)
        {
            try
            {
                await RefreshAsync(stop);
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested)
            {
                break;
            }
            catch (Exception e)
            {
                log($"[calendar] {e.Message}");
            }
            try
            {
                await wake.WaitAsync(Every, stop);
                while (wake.CurrentCount > 0) await wake.WaitAsync(stop);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    ICalendarSource? SourceFor(CalendarSourceSettings s)
    {
        string key = JsonSerializer.Serialize(s);
        if (made.TryGetValue(s.Id, out var m) && m.Settings == key) return m.Source;
        var source = kinds.Create(s);
        if (source is null) made.Remove(s.Id);
        else made[s.Id] = (key, source);
        return source;
    }

    /// <summary>Read every source once: their calendars into calendars.json, their events into the cache.</summary>
    public async Task RefreshAsync(CancellationToken ct = default)
    {
        await reading.WaitAsync(ct);
        try
        {
            var settings = CalendarSettings.Load(home);
            var now = clock();
            DateTimeOffset from = now.AddHours(-12), to = now + Ahead;
            var listed = new Dictionary<string, IReadOnlyList<CalendarInfo>>();
            var problems = new Dictionary<string, string>();
            var events = new List<CalendarEvent>();
            foreach (var s in settings.Sources)
            {
                if (SourceFor(s) is not { } source) continue;
                try
                {
                    var calendars = await source.ListCalendarsAsync(ct);
                    listed[s.Id] = calendars;
                    var shown = new CalendarSettings { Calendars = [.. calendars], Enabled = settings.Enabled }.EnabledCalendars(s.Id).Select(c => c.Id).ToList();
                    if (shown.Count > 0) events.AddRange(await source.EventsAsync(shown, from, to, ct));
                }
                catch (Exception e) when (e is not OperationCanceledException || !ct.IsCancellationRequested)
                {
                    problems[s.Id] = e is CalendarFeedException ? e.Message : $"Couldn't read it ({e.Message.TrimEnd('.')}). Study Stash tries again soon.";
                    log($"[calendar] {s.Name}: {e.Message}");
                    // What it had last time stays until it can be read again.
                    events.AddRange(cache.Events.Where(x => x.SourceId == s.Id));
                }
            }
            var saved = CalendarSettings.Update(home, c =>
            {
                foreach (var (id, calendars) in listed)
                    if (c.Source(id) is not null) c.Listed(id, calendars);
                c.Problems = problems.Where(p => c.Source(p.Key) is not null).ToDictionary();
            });
            var order = saved.Sources.Select((s, i) => (s.Id, i)).ToDictionary(x => x.Id, x => x.i);
            var shownNow = saved.Calendars.Where(saved.IsEnabled).Select(c => c.Id).ToHashSet();
            cache = new UpcomingCache(now, Merge(events.Where(e => order.ContainsKey(e.SourceId) && shownNow.Contains(e.CalendarId)), order));
            try
            {
                Directory.CreateDirectory(home);
                SharedFile.Write(PathIn(home), JsonSerializer.Serialize(cache, Json) + "\n");
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                log($"[calendar] couldn't save what's coming up: {e.Message}");
            }
        }
        finally
        {
            reading.Release();
        }
        Changed?.Invoke();
    }

    /// <summary>The events in time order: cancelled ones dropped, and the same event from two places kept once (the
    /// one from the source added first).</summary>
    public static List<CalendarEvent> Merge(IEnumerable<CalendarEvent> events, IReadOnlyDictionary<string, int>? sourceOrder = null)
    {
        int Rank(CalendarEvent e) => sourceOrder is not null && sourceOrder.TryGetValue(e.SourceId, out int i) ? i : int.MaxValue;
        var seen = new HashSet<string>();
        var result = new List<CalendarEvent>();
        foreach (var e in events.Where(e => !e.Cancelled).OrderBy(Rank))
        {
            if (!seen.Add($"{e.SourceId}\n{e.Id}")) continue;
            if (!seen.Add($"{Classify.Norm(e.Title)}\n{e.Start.UtcTicks}\n{e.End.UtcTicks}\n{e.AllDay}")) continue;
            result.Add(e);
        }
        result.Sort((a, b) => a.Start != b.Start ? a.Start.CompareTo(b.Start) : a.End.CompareTo(b.End));
        return result;
    }

    /// <summary>What's coming up today and tomorrow (in <paramref name="zone"/>), not yet over: timed events only, since
    /// a day off or a due date isn't something to go to, at most <paramref name="count"/>.</summary>
    public static List<CalendarEvent> Coming(IEnumerable<CalendarEvent> events, DateTimeOffset now, TimeZoneInfo zone, int count = 4)
    {
        var localNow = TimeZoneInfo.ConvertTime(now, zone);
        var endOfTomorrow = new DateTimeOffset(localNow.Date.AddDays(2), zone.GetUtcOffset(localNow.Date.AddDays(2)));
        return [.. events.Where(e => !e.AllDay && !e.Cancelled && e.End > e.Start && e.End > now && e.Start < endOfTomorrow)
            .OrderBy(e => e.Start).Take(count)];
    }

    /// <summary>
    /// The event a recording started now is for: one that's on (the latest to have started), else the next one to
    /// start within <see cref="StartsSoon"/>; one with a class before one without. Null when there's none.
    /// </summary>
    public static EventNow? During(IEnumerable<CalendarEvent> events, DateTimeOffset now, IReadOnlyList<ClassHint> classes)
    {
        var candidates = events.Where(e => !e.AllDay && !e.Cancelled && e.End > e.Start && e.End > now && e.Start <= now + StartsSoon)
            .Select(e => new EventNow(e, EventClass.For(e.Title, classes)))
            .OrderBy(x => x.Class is null)
            .ThenBy(x => x.Event.Start <= now ? 0 : 1)
            .ThenBy(x => x.Event.Start <= now ? -x.Event.Start.UtcTicks : x.Event.Start.UtcTicks)
            .ToList();
        return candidates.FirstOrDefault();
    }

    public List<CalendarEvent> Coming(TimeZoneInfo zone, int count = 4) => Coming(Events, clock(), zone, count);

    public EventNow? During(IReadOnlyList<ClassHint> classes) => During(Events, clock(), classes);
}
