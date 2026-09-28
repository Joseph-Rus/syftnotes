using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace StudyStash.Core.Calendar.Ics;

/// <summary>
/// One iCalendar feed, read: its name and colour, and its events turned into the occurrences in a stretch of time.
/// A repeating event is expanded in its own zone's wall-clock time (a 10:00 class stays at 10:00 when the clocks
/// change), with the dates it skips (EXDATE) left out and the ones changed on their own (a RECURRENCE-ID: moved,
/// renamed, cancelled) put in their place. A time with no zone reads in the feed's zone (X-WR-TIMEZONE) when it names
/// one, else this computer's; an all-day event starts at this computer's midnight.
/// </summary>
public sealed partial class IcsFeed
{
    readonly List<IcsComponent> events;
    readonly Dictionary<string, IcsComponent> vtimezones;
    readonly TimeZoneInfo local;
    readonly IcsZone floating;
    readonly Dictionary<string, IcsZone?> zones = [];

    /// <summary>X-WR-CALNAME (or NAME): what the calendar calls itself; null when it doesn't say.</summary>
    public string? Name { get; }
    /// <summary>X-APPLE-CALENDAR-COLOR (or COLOR) as "#RRGGBB"; null when it doesn't say, or says it by a name.</summary>
    public string? Color { get; }
    /// <summary>The feed had a VCALENDAR in it at all (a sign-in page or an error page doesn't).</summary>
    public bool IsCalendar { get; }

    IcsFeed(List<IcsComponent> calendars, TimeZoneInfo local)
    {
        this.local = local;
        IsCalendar = calendars.Count > 0;
        events = [.. calendars.SelectMany(c => c.Blocks("VEVENT"))];
        vtimezones = calendars.SelectMany(c => c.Blocks("VTIMEZONE"))
            .Where(z => z.Text("TZID") is { Length: > 0 })
            .GroupBy(z => z.Text("TZID")!.Trim()).ToDictionary(g => g.Key, g => g.First());
        var cal = calendars.FirstOrDefault();
        Name = Clean(cal?.Text("X-WR-CALNAME")) ?? Clean(cal?.Text("NAME"));
        Color = HexColor(cal?.Text("X-APPLE-CALENDAR-COLOR")) ?? HexColor(cal?.Text("COLOR"));
        floating = cal?.Text("X-WR-TIMEZONE") is { Length: > 0 } tz && IcsZones.Find(tz, vtimezones) is { } z ? z : IcsZone.Of(local);
    }

    /// <summary>Read a feed's text. <paramref name="local"/> is this computer's zone (a test gives its own).</summary>
    public static IcsFeed Parse(string text, TimeZoneInfo? local = null) => new(IcsReader.Parse(text), local ?? TimeZoneInfo.Local);

    static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    [GeneratedRegex("^#?([0-9A-Fa-f]{6})([0-9A-Fa-f]{2})?$")]
    private static partial Regex Hex();

    /// <summary>"#FF2968FF" or "ff2968" → "#FF2968"; null for anything else.</summary>
    public static string? HexColor(string? value) =>
        value is not null && Hex().Match(value.Trim()) is { Success: true } m ? "#" + m.Groups[1].Value.ToUpperInvariant() : null;

    [GeneratedRegex(@"https://[^\s""<>]*(?:zoom\.us|meet\.google\.com|teams\.microsoft\.com|teams\.live\.com|webex\.com|whereby\.com)[^\s""<>]*", RegexOptions.IgnoreCase)]
    private static partial Regex MeetingLink();

    /// <summary>The zone a DTSTART-like time is in: UTC, its TZID's, or the floating one.</summary>
    IcsZone ZoneOf(IcsTime t)
    {
        if (t.IsUtc) return IcsZone.Utc;
        if (t.IsDate || t.Tzid is null) return floating;
        if (!zones.TryGetValue(t.Tzid, out var z)) zones[t.Tzid] = z = IcsZones.Find(t.Tzid, vtimezones);
        return z ?? floating;
    }

    /// <summary>The moment a time is: in its zone, or a whole day at this computer's midnight.</summary>
    DateTimeOffset Moment(IcsTime t) => t.IsDate ? IcsZone.Of(local).At(t.Value.Date) : ZoneOf(t).At(t.Value);

    /// <summary>How one occurrence is known among its series: the moment it was first due (a whole day by its date).</summary>
    static string Key(DateTimeOffset at) => at.UtcDateTime.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
    static string DayKey(DateTime day) => day.ToString("yyyyMMdd", CultureInfo.InvariantCulture);

    /// <summary>
    /// Every occurrence that overlaps [<paramref name="from"/>, <paramref name="to"/>), in start order, with the ids
    /// of the calendar and source it's shown under. Cancelled ones are included, marked.
    /// </summary>
    public List<CalendarEvent> Events(string calendarId, string sourceId, DateTimeOffset from, DateTimeOffset to)
    {
        var result = new List<CalendarEvent>();
        foreach (var series in events.GroupBy(UidOf))
        {
            var masters = series.Where(e => e.First("RECURRENCE-ID") is null).ToList();
            var overrides = series.Where(e => e.First("RECURRENCE-ID") is not null).ToList();
            // Two copies of one event (a feed that repeats itself, or kept an old one): the latest SEQUENCE wins.
            var master = masters.OrderByDescending(Sequence).FirstOrDefault();
            var changed = new Dictionary<string, IcsComponent>();
            foreach (var o in overrides.OrderBy(Sequence))
                if (IcsTime.Of(o.First("RECURRENCE-ID")) is { } rid)
                    changed[rid.IsDate ? DayKey(rid.Value) : Key(Moment(rid))] = o;
            var used = new HashSet<string>();
            if (master is not null) Expand(master, series.Key, changed, used, calendarId, sourceId, from, to, result);
            // Changed occurrences whose first date wasn't among the ones expanded (moved in from outside the window, or
            // with no series in the feed at all) stand on their own.
            foreach (var (key, o) in changed)
                if (!used.Contains(key) && Occurrence(o, master, $"{series.Key}/{key}", null, calendarId, sourceId) is { } e && Overlaps(e, from, to))
                    result.Add(e);
        }
        result.Sort((a, b) => a.Start != b.Start ? a.Start.CompareTo(b.Start) : string.CompareOrdinal(a.Id, b.Id));
        return result;
    }

    static bool Overlaps(CalendarEvent e, DateTimeOffset from, DateTimeOffset to) =>
        e.Start < to && (e.End > from || (e.End == e.Start && e.Start >= from));

    static int Sequence(IcsComponent e) => int.TryParse(e.Text("SEQUENCE"), NumberStyles.None, CultureInfo.InvariantCulture, out int n) ? n : 0;

    /// <summary>An event's UID; one without (hand-written feeds) gets one from its title and start.</summary>
    static string UidOf(IcsComponent e) =>
        e.Text("UID") is { Length: > 0 } uid ? uid.Trim()
            : "nouid-" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes($"{e.Text("SUMMARY")}|{e.First("DTSTART")?.Value}")))[..16];

    void Expand(IcsComponent master, string uid, Dictionary<string, IcsComponent> changed, HashSet<string> used,
        string calendarId, string sourceId, DateTimeOffset from, DateTimeOffset to, List<CalendarEvent> result)
    {
        if (IcsTime.Of(master.First("DTSTART")) is not { } start) return;
        var rule = master.All("RRULE").Select(r => IcsRule.Parse(r.Value)).FirstOrDefault(r => r is not null);
        var extra = master.All("RDATE").SelectMany(IcsTime.List).ToList();
        bool repeats = rule is not null || extra.Count > 0;
        if (!repeats)
        {
            string key = start.IsDate ? DayKey(start.Value) : Key(Moment(start));
            if (changed.TryGetValue(key, out var only))
            {
                used.Add(key);
                if (Occurrence(only, master, uid, null, calendarId, sourceId) is { } c && Overlaps(c, from, to)) result.Add(c);
            }
            else if (Occurrence(master, null, uid, null, calendarId, sourceId) is { } e && Overlaps(e, from, to))
            {
                result.Add(e);
            }
            return;
        }
        var zone = ZoneOf(start);
        // Expanded in the event's own wall-clock time, a day past the window (an event that started before it may
        // still be on), and back far enough to catch a long one that started before the window and runs into it.
        var through = (start.IsDate ? IcsZone.Of(local) : zone).Local(to).AddDays(1);
        var span = Length(master, start);
        var skipped = master.All("EXDATE").SelectMany(IcsTime.List).ToList();
        var skippedDays = skipped.Where(t => t.IsDate).Select(t => t.Value.Date).ToHashSet();
        var skippedAt = skipped.Where(t => !t.IsDate).Select(t => Moment(t)).ToHashSet();
        Func<DateTime, bool>? afterUntil = rule?.Until is { IsUtc: true } u && !start.IsDate
            ? t => zone.At(t) > new DateTimeOffset(u.Value, TimeSpan.Zero)
            : null;
        var starts = rule is not null ? IcsRule.Expand(start.Value, rule, through, afterUntil) : [start.Value];
        var all = starts.Concat(extra.Select(t => t.IsDate || t.IsUtc || t.Tzid is not null ? (start.IsDate ? t.Value.Date : zone.Local(Moment(t))) : t.Value))
            .Distinct().Order();
        foreach (var at in all)
        {
            if (at > through) break;
            var moment = start.IsDate ? IcsZone.Of(local).At(at.Date) : zone.At(at);
            string key = start.IsDate ? DayKey(at) : Key(moment);
            if (skippedDays.Contains(at.Date) || skippedAt.Contains(moment)) continue;
            if (changed.TryGetValue(key, out var o))
            {
                used.Add(key);
                if (Occurrence(o, master, $"{uid}/{key}", null, calendarId, sourceId) is { } c && Overlaps(c, from, to)) result.Add(c);
                continue;
            }
            // Cheap check before building it: this one ends before the window.
            var end = start.IsDate ? IcsZone.Of(local).At(at.Date + span.Length) : span.Local ? zone.At(at + span.Length) : moment + span.Length;
            if (end <= from && !(end == moment && moment >= from)) continue;
            if (Occurrence(master, null, $"{uid}/{key}", (moment, end), calendarId, sourceId) is { } e && Overlaps(e, from, to)) result.Add(e);
        }
    }

    /// <summary>How long an event is: in its zone's wall-clock time when it ends in the same zone it starts in (so a
    /// class stays an hour across a change of clocks), else as elapsed time. A whole-day event is a number of days.</summary>
    (TimeSpan Length, bool Local) Length(IcsComponent e, IcsTime start)
    {
        if (IcsTime.Of(e.First("DTEND")) is { } end)
        {
            if (start.IsDate) return (TimeSpan.FromDays(Math.Max(1, (end.Value.Date - start.Value.Date).Days)), true);
            if (end.IsUtc == start.IsUtc && end.Tzid == start.Tzid && !end.IsDate) return (Max0(end.Value - start.Value), true);
            return (Max0(Moment(end) - Moment(start)), false);
        }
        if (e.Text("DURATION") is { } d && IcsDuration.Parse(d) is { } length)
            return start.IsDate ? (TimeSpan.FromDays(Math.Max(1, length.Days)), true) : (Max0(length), length.Days > 0);
        // No end: a whole-day event is that day; a timed one is a moment (RFC 5545).
        return (start.IsDate ? TimeSpan.FromDays(1) : TimeSpan.Zero, true);
    }

    static TimeSpan Max0(TimeSpan t) => t < TimeSpan.Zero ? TimeSpan.Zero : t;

    /// <summary>
    /// One occurrence as a <see cref="CalendarEvent"/>: from <paramref name="e"/> (the series, or one changed occurrence
    /// of it, which falls back to <paramref name="master"/> for what it leaves out), at <paramref name="when"/> or at
    /// its own DTSTART.
    /// </summary>
    CalendarEvent? Occurrence(IcsComponent e, IcsComponent? master, string id, (DateTimeOffset Start, DateTimeOffset End)? when,
        string calendarId, string sourceId)
    {
        string? Text(string name) => Clean(e.Text(name)) ?? (master is null ? null : Clean(master.Text(name)));
        DateTimeOffset start, end;
        bool allDay;
        if (when is { } w)
        {
            (start, end) = w;
            allDay = IcsTime.Of(e.First("DTSTART"))?.IsDate ?? false;
        }
        else
        {
            if ((IcsTime.Of(e.First("DTSTART")) ?? IcsTime.Of(e.First("RECURRENCE-ID"))) is not { } s) return null;
            allDay = s.IsDate;
            start = Moment(s);
            var (length, local_) = Length(e.First("DTEND") is null && e.First("DURATION") is null && master is not null ? master : e, s);
            end = s.IsDate ? IcsZone.Of(local).At(s.Value.Date + length) : local_ ? ZoneOf(s).At(s.Value + length) : start + length;
        }
        var attendees = (e.All("ATTENDEE").Any() ? e.All("ATTENDEE") : master?.All("ATTENDEE") ?? [])
            .Select(a => Clean(a.Param("CN")) ?? Clean(Regex.Replace(a.Value, "^mailto:", "", RegexOptions.IgnoreCase)))
            .OfType<string>().Distinct().ToList();
        string? location = Text("LOCATION");
        string? url = Text("URL") ?? (MeetingLink().Match($"{location} {Text("DESCRIPTION")}") is { Success: true } m ? m.Value : null);
        bool cancelled = string.Equals(Text("STATUS"), "CANCELLED", StringComparison.OrdinalIgnoreCase);
        return new CalendarEvent(id, calendarId, sourceId, Text("SUMMARY") ?? "(No title)", start, end, allDay, location, attendees, url, cancelled);
    }
}
