using System.Globalization;
using System.Text.RegularExpressions;

namespace StudyStash.Core.Calendar.Ics;

/// <summary>
/// A date or date-time as a feed writes it: the wall-clock value, whether it's a whole day ("20260915", an all-day
/// event), whether it's UTC ("…Z"), and the zone it's in (TZID) when it names one. A time that's none of those is
/// "floating": the same wall-clock time wherever you are.
/// </summary>
public readonly record struct IcsTime(DateTime Value, bool IsDate, bool IsUtc, string? Tzid)
{
    /// <summary>"20260915", "20260915T090000", "20260915T090000Z" (and "…T0900" without seconds, which some school
    /// systems write); null for anything else.</summary>
    public static IcsTime? Parse(string value, string? tzid = null)
    {
        string v = value.Trim();
        bool utc = v.EndsWith('Z') || v.EndsWith('z');
        if (utc) v = v[..^1];
        if (v.Length == 8 && Digits(v, 0, 8))
            return TryDate(v, out var d) ? new IcsTime(d, true, false, null) : null;
        if (v.Length is 15 or 13 && v[8] is 'T' or 't' && Digits(v, 0, 8) && Digits(v, 9, v.Length - 9)
            && TryDate(v[..8], out var day))
        {
            int h = Num(v, 9), m = Num(v, 11), s = v.Length == 15 ? Num(v, 13) : 0;
            // 24:00:00 and a leap second (60) are rare but legal enough in the wild: roll them over.
            if (h > 24 || m > 59 || s > 60) return null;
            var t = day.AddHours(h).AddMinutes(m).AddSeconds(s);
            return new IcsTime(DateTime.SpecifyKind(t, DateTimeKind.Unspecified), false, utc, utc ? null : Clean(tzid));
        }
        return null;
    }

    /// <summary>A DTSTART-like line's value: its TZID, and VALUE=DATE where it's said.</summary>
    public static IcsTime? Of(IcsProperty? p)
    {
        if (p is null) return null;
        var t = Parse(p.Value, p.Param("TZID"));
        if (t is { } x && string.Equals(p.Param("VALUE"), "DATE", StringComparison.OrdinalIgnoreCase) && !x.IsDate)
            return x with { Value = x.Value.Date, IsDate = true, IsUtc = false, Tzid = null };
        return t;
    }

    /// <summary>A list line's values (EXDATE, RDATE: "a,b,c"), each with the line's TZID.</summary>
    public static IEnumerable<IcsTime> List(IcsProperty p)
    {
        bool dateOnly = string.Equals(p.Param("VALUE"), "DATE", StringComparison.OrdinalIgnoreCase);
        foreach (string item in IcsText.Split(p.Value))
        {
            // RDATE may be a PERIOD ("start/end"): its start is the date.
            string one = item.Split('/')[0];
            if (Parse(one, p.Param("TZID")) is not { } t) continue;
            yield return dateOnly && !t.IsDate ? t with { Value = t.Value.Date, IsDate = true, IsUtc = false, Tzid = null } : t;
        }
    }

    static string? Clean(string? tzid) => string.IsNullOrWhiteSpace(tzid) ? null : tzid.Trim().Trim('"');

    static bool Digits(string s, int from, int count)
    {
        for (int i = from; i < from + count; i++)
            if (s[i] is < '0' or > '9') return false;
        return count > 0;
    }

    static int Num(string s, int at) => (s[at] - '0') * 10 + (s[at + 1] - '0');

    static bool TryDate(string yyyymmdd, out DateTime d) =>
        DateTime.TryParseExact(yyyymmdd, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out d);
}

/// <summary>iCalendar's durations: "PT50M", "P1D", "P1W", "-PT15M", "P1DT2H30M".</summary>
public static partial class IcsDuration
{
    [GeneratedRegex(@"^([+-])?P(?:(\d+)W)?(?:(\d+)D)?(?:T(?:(\d+)H)?(?:(\d+)M)?(?:(\d+)S)?)?$", RegexOptions.IgnoreCase)]
    private static partial Regex Pattern();

    /// <summary>The duration, or null when it isn't one.</summary>
    public static TimeSpan? Parse(string value)
    {
        var m = Pattern().Match(value.Trim());
        if (!m.Success || value.Trim().Length <= 1 || value.Trim().EndsWith('T')) return null;
        int G(int i) => m.Groups[i].Success ? int.Parse(m.Groups[i].Value, CultureInfo.InvariantCulture) : 0;
        var span = new TimeSpan(G(2) * 7 + G(3), G(4), G(5), G(6));
        return m.Groups[1].Value == "-" ? -span : span;
    }
}
