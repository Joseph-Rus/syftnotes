using System.Text.RegularExpressions;

namespace StudyStash.Core.Calendar.Ics;

/// <summary>
/// A time zone an event's wall-clock times are in: the computer's own zone database when it knows the name, else the
/// rules the feed itself sends (VTIMEZONE). A wall-clock time the clocks skip (2:30 on the night they spring forward)
/// is read with the offset before the jump, so it lands just after it; one that happens twice (the hour they fall
/// back) is the first of the two.
/// </summary>
public abstract class IcsZone
{
    /// <summary>The zone's offset from UTC at this wall-clock time.</summary>
    public abstract TimeSpan OffsetAt(DateTime local);

    /// <summary>The moment this wall-clock time is.</summary>
    public DateTimeOffset At(DateTime local)
    {
        var l = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        return new DateTimeOffset(l, OffsetAt(l));
    }

    /// <summary>The wall-clock time here at a moment.</summary>
    public abstract DateTime Local(DateTimeOffset at);

    public static IcsZone Utc { get; } = new SystemZone(TimeZoneInfo.Utc);

    public static IcsZone Of(TimeZoneInfo zone) => new SystemZone(zone);

    sealed class SystemZone(TimeZoneInfo zone) : IcsZone
    {
        public override TimeSpan OffsetAt(DateTime local)
        {
            if (zone.IsAmbiguousTime(local)) return zone.GetAmbiguousTimeOffsets(local).Max();
            if (zone.IsInvalidTime(local)) return zone.GetUtcOffset(local.AddHours(-3));
            return zone.GetUtcOffset(local);
        }

        public override DateTime Local(DateTimeOffset at) => DateTime.SpecifyKind(TimeZoneInfo.ConvertTime(at, zone).DateTime, DateTimeKind.Unspecified);
    }
}

/// <summary>
/// A zone made from a feed's own VTIMEZONE: its STANDARD and DAYLIGHT parts, each starting on a date and (usually)
/// again every year by a rule ("the second Sunday of March"). Used when the computer doesn't know the zone's name:
/// Outlook's "Customized Time Zone", an old Windows name, a made-up id.
/// </summary>
public sealed class IcsRulesZone : IcsZone
{
    sealed record Part(DateTime Start, TimeSpan From, TimeSpan To, IcsRule? Rule, List<DateTime> Dates);

    readonly List<Part> parts;

    IcsRulesZone(List<Part> parts) => this.parts = parts;

    /// <summary>The zone a VTIMEZONE block describes, or null when it has no usable parts.</summary>
    public static IcsRulesZone? From(IcsComponent vtimezone)
    {
        var parts = new List<Part>();
        foreach (var c in vtimezone.Children.Where(c => c.Name is "STANDARD" or "DAYLIGHT"))
        {
            if (IcsTime.Of(c.First("DTSTART")) is not { } start || Offset(c.First("TZOFFSETTO")?.Value) is not { } to) continue;
            var from = Offset(c.First("TZOFFSETFROM")?.Value) ?? to;
            var rule = c.First("RRULE") is { } r ? IcsRule.Parse(r.Value) : null;
            var dates = c.All("RDATE").SelectMany(IcsTime.List).Select(t => t.Value).ToList();
            parts.Add(new Part(start.Value, from, to, rule, dates));
        }
        return parts.Count == 0 ? null : new IcsRulesZone(parts);
    }

    /// <summary>"+0530", "-0800", "+010000" → the offset.</summary>
    public static TimeSpan? Offset(string? value)
    {
        if (value is null) return null;
        var m = Regex.Match(value.Trim(), @"^([+-])(\d{2})(\d{2})(\d{2})?$");
        if (!m.Success) return null;
        var span = new TimeSpan(int.Parse(m.Groups[2].Value), int.Parse(m.Groups[3].Value), m.Groups[4].Success ? int.Parse(m.Groups[4].Value) : 0);
        return m.Groups[1].Value == "-" ? -span : span;
    }

    /// <summary>Every change of offset around a year, as (the wall-clock time it happens, in the offset before it;
    /// the offset before; the offset after), in order.</summary>
    List<(DateTime At, TimeSpan From, TimeSpan To)> Changes(int year)
    {
        var until = new DateTime(year + 1, 12, 31);
        var list = new List<(DateTime, TimeSpan, TimeSpan)>();
        foreach (var p in parts)
        {
            IEnumerable<DateTime> onsets = p.Rule is { } rule
                ? IcsRule.Expand(p.Start, rule, until).Where(t => t.Year >= year - 1)
                : [p.Start];
            foreach (var t in onsets.Concat(p.Dates)) list.Add((t, p.From, p.To));
        }
        list.Sort((a, b) => a.Item1.CompareTo(b.Item1));
        return list;
    }

    public override TimeSpan OffsetAt(DateTime local)
    {
        var changes = Changes(local.Year);
        TimeSpan offset = changes.Count > 0 ? changes[0].From : parts[0].To;
        foreach (var (at, from, to) in changes)
        {
            // A change is written in the wall-clock time before it: once past it, the new offset holds. The hour the
            // clocks fall back repeats before the change, so its first (earlier) reading is the one kept, as with the
            // system's zones; a time the clocks skip keeps the offset before the jump.
            if (local < at) break;
            if (to > from && local < at + (to - from)) break;
            offset = to;
        }
        return offset;
    }

    public override DateTime Local(DateTimeOffset at)
    {
        // Guess with the offset at the UTC wall time, then settle on the one that holds there.
        var utc = at.UtcDateTime;
        var guess = utc + OffsetAt(DateTime.SpecifyKind(utc, DateTimeKind.Unspecified));
        var local = utc + OffsetAt(DateTime.SpecifyKind(guess, DateTimeKind.Unspecified));
        return DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
    }
}

/// <summary>
/// Finds the zone a TZID names. Google and iCloud write IANA names ("America/New_York"), Outlook writes Windows names
/// ("Eastern Standard Time") or its own ("Customized Time Zone"), some old exports a prefixed path
/// ("/mozilla.org/20050126_1/America/New_York"). The computer's zone database is asked first (under either kind of
/// name, through a small table of the common ones, since Study Stash runs without the ICU data that converts them);
/// then the feed's own VTIMEZONE; then nothing, and the time reads as floating.
/// </summary>
public static class IcsZones
{
    /// <summary>The zone for <paramref name="tzid"/>, from the system or the feed's VTIMEZONE blocks; null when neither knows it.</summary>
    public static IcsZone? Find(string tzid, IReadOnlyDictionary<string, IcsComponent> vtimezones)
    {
        string id = tzid.Trim().Trim('"');
        foreach (string name in Names(id))
            if (TimeZoneInfo.TryFindSystemTimeZoneById(name, out var tz))
                return IcsZone.Of(tz);
        if (vtimezones.TryGetValue(id, out var block) && IcsRulesZone.From(block) is { } rules) return rules;
        return null;
    }

    /// <summary>The names to try for a TZID: itself, the Area/City at the end of a prefixed path, and its twin in the
    /// other naming (IANA ↔ Windows).</summary>
    static IEnumerable<string> Names(string id)
    {
        var tried = new List<string> { id };
        var tail = Regex.Match(id, @"([A-Za-z_]+/[A-Za-z_+\-0-9]+(?:/[A-Za-z_]+)?)$");
        if (id.StartsWith('/') && tail.Success) tried.Add(tail.Groups[1].Value);
        foreach (string n in tried.ToList())
        {
            if (WindowsToIana.TryGetValue(n, out string? iana)) tried.Add(iana);
            if (IanaToWindows.TryGetValue(n, out string? windows)) tried.Add(windows);
            foreach (var kv in WindowsToIana.Where(kv => kv.Value == n)) tried.Add(kv.Key);
        }
        return tried.Distinct();
    }

    /// <summary>The Windows zone names Outlook and Exchange write, with the IANA zone each is (CLDR's primary one).</summary>
    static readonly Dictionary<string, string> WindowsToIana = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Dateline Standard Time"] = "Etc/GMT+12",
        ["Hawaiian Standard Time"] = "Pacific/Honolulu",
        ["Alaskan Standard Time"] = "America/Anchorage",
        ["Pacific Standard Time"] = "America/Los_Angeles",
        ["US Mountain Standard Time"] = "America/Phoenix",
        ["Mountain Standard Time"] = "America/Denver",
        ["Central Standard Time"] = "America/Chicago",
        ["Central America Standard Time"] = "America/Guatemala",
        ["Canada Central Standard Time"] = "America/Regina",
        ["Central Standard Time (Mexico)"] = "America/Mexico_City",
        ["Eastern Standard Time"] = "America/New_York",
        ["US Eastern Standard Time"] = "America/Indiana/Indianapolis",
        ["Atlantic Standard Time"] = "America/Halifax",
        ["Newfoundland Standard Time"] = "America/St_Johns",
        ["SA Pacific Standard Time"] = "America/Bogota",
        ["E. South America Standard Time"] = "America/Sao_Paulo",
        ["Argentina Standard Time"] = "America/Buenos_Aires",
        ["Pacific SA Standard Time"] = "America/Santiago",
        ["UTC"] = "Etc/UTC",
        ["GMT Standard Time"] = "Europe/London",
        ["Greenwich Standard Time"] = "Atlantic/Reykjavik",
        ["W. Europe Standard Time"] = "Europe/Berlin",
        ["Central Europe Standard Time"] = "Europe/Budapest",
        ["Romance Standard Time"] = "Europe/Paris",
        ["Central European Standard Time"] = "Europe/Warsaw",
        ["GTB Standard Time"] = "Europe/Bucharest",
        ["FLE Standard Time"] = "Europe/Kiev",
        ["E. Europe Standard Time"] = "Europe/Chisinau",
        ["Israel Standard Time"] = "Asia/Jerusalem",
        ["Egypt Standard Time"] = "Africa/Cairo",
        ["South Africa Standard Time"] = "Africa/Johannesburg",
        ["Turkey Standard Time"] = "Europe/Istanbul",
        ["Russian Standard Time"] = "Europe/Moscow",
        ["Arab Standard Time"] = "Asia/Riyadh",
        ["Arabian Standard Time"] = "Asia/Dubai",
        ["Iran Standard Time"] = "Asia/Tehran",
        ["Pakistan Standard Time"] = "Asia/Karachi",
        ["India Standard Time"] = "Asia/Calcutta",
        ["Nepal Standard Time"] = "Asia/Katmandu",
        ["Bangladesh Standard Time"] = "Asia/Dhaka",
        ["SE Asia Standard Time"] = "Asia/Bangkok",
        ["China Standard Time"] = "Asia/Shanghai",
        ["Singapore Standard Time"] = "Asia/Singapore",
        ["Taipei Standard Time"] = "Asia/Taipei",
        ["W. Australia Standard Time"] = "Australia/Perth",
        ["Korea Standard Time"] = "Asia/Seoul",
        ["Tokyo Standard Time"] = "Asia/Tokyo",
        ["Cen. Australia Standard Time"] = "Australia/Adelaide",
        ["AUS Central Standard Time"] = "Australia/Darwin",
        ["E. Australia Standard Time"] = "Australia/Brisbane",
        ["AUS Eastern Standard Time"] = "Australia/Sydney",
        ["Tasmania Standard Time"] = "Australia/Hobart",
        ["New Zealand Standard Time"] = "Pacific/Auckland",
    };

    /// <summary>IANA names that are another spelling of one in the table above (the newer or older of a pair).</summary>
    static readonly Dictionary<string, string> IanaToWindows = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Asia/Kolkata"] = "India Standard Time",
        ["Asia/Kathmandu"] = "Nepal Standard Time",
        ["Europe/Kyiv"] = "FLE Standard Time",
        ["America/Argentina/Buenos_Aires"] = "Argentina Standard Time",
        ["America/Indianapolis"] = "US Eastern Standard Time",
        ["Etc/GMT"] = "UTC",
        ["GMT"] = "UTC",
        ["Z"] = "UTC",
    };
}
