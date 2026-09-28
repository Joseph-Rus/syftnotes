using System.Globalization;

namespace StudyStash.Core.Calendar.Ics;

/// <summary>
/// A repeat rule (RRULE): how often ("FREQ=WEEKLY"), every how many ("INTERVAL=2"), until when or how many times
/// ("UNTIL", "COUNT"), and on which days ("BYDAY=MO,WE,FR", "BYDAY=2SU", "BYMONTHDAY=-1", "BYMONTH=3",
/// "BYSETPOS=-1"). What calendars actually write for classes, holidays and time zones; the rarer parts (by hour, by
/// week number, by day of the year) aren't read, and a rule that uses them repeats as if they weren't there.
/// </summary>
public sealed record IcsRule(string Freq, int Interval, int? Count, IcsTime? Until,
    IReadOnlyList<(int Nth, DayOfWeek Day)> ByDay, IReadOnlyList<int> ByMonthDay, IReadOnlyList<int> ByMonth,
    IReadOnlyList<int> BySetPos, DayOfWeek WeekStart)
{
    /// <summary>The most occurrences one rule is ever expanded to, and the furthest ahead: a rule with neither an end
    /// nor a count stops at the window anyway, this is only against a broken one.</summary>
    const int MaxSteps = 100_000;

    static readonly Dictionary<string, DayOfWeek> Days = new(StringComparer.OrdinalIgnoreCase)
    {
        ["MO"] = DayOfWeek.Monday, ["TU"] = DayOfWeek.Tuesday, ["WE"] = DayOfWeek.Wednesday, ["TH"] = DayOfWeek.Thursday,
        ["FR"] = DayOfWeek.Friday, ["SA"] = DayOfWeek.Saturday, ["SU"] = DayOfWeek.Sunday,
    };

    /// <summary>"FREQ=WEEKLY;BYDAY=TU,TH;UNTIL=20261211T235959Z" → the rule; null without a FREQ this reads.</summary>
    public static IcsRule? Parse(string value)
    {
        var parts = value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(p => p.Split('=', 2)).Where(p => p.Length == 2)
            .GroupBy(p => p[0].ToUpperInvariant()).ToDictionary(g => g.Key, g => g.First()[1]);
        if (!parts.TryGetValue("FREQ", out string? freq)) return null;
        freq = freq.ToUpperInvariant();
        if (freq is not ("DAILY" or "WEEKLY" or "MONTHLY" or "YEARLY")) return null;
        int interval = parts.TryGetValue("INTERVAL", out string? i) && int.TryParse(i, NumberStyles.None, CultureInfo.InvariantCulture, out int n) && n > 0 ? n : 1;
        int? count = parts.TryGetValue("COUNT", out string? c) && int.TryParse(c, NumberStyles.None, CultureInfo.InvariantCulture, out int k) ? k : null;
        IcsTime? until = parts.TryGetValue("UNTIL", out string? u) ? IcsTime.Parse(u) : null;
        var byDay = new List<(int, DayOfWeek)>();
        foreach (string d in List(parts, "BYDAY"))
        {
            if (d.Length < 2 || !Days.TryGetValue(d[^2..], out var day)) continue;
            string nth = d[..^2];
            byDay.Add((nth.Length == 0 ? 0 : int.TryParse(nth, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int x) ? x : 0, day));
        }
        var weekStart = parts.TryGetValue("WKST", out string? w) && Days.TryGetValue(w, out var ws) ? ws : DayOfWeek.Monday;
        return new IcsRule(freq, interval, count, until, byDay, Numbers(parts, "BYMONTHDAY"), Numbers(parts, "BYMONTH").Where(m => m is >= 1 and <= 12).ToList(),
            Numbers(parts, "BYSETPOS"), weekStart);
    }

    static IEnumerable<string> List(Dictionary<string, string> parts, string key) =>
        parts.TryGetValue(key, out string? v) ? v.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) : [];

    static List<int> Numbers(Dictionary<string, string> parts, string key) =>
        [.. List(parts, key).Select(s => int.TryParse(s, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int x) ? x : 0).Where(x => x != 0)];

    /// <summary>
    /// The wall-clock start of every occurrence, in order, from <paramref name="start"/> (always the first, as the
    /// standard says) until the rule ends or an occurrence would start after <paramref name="through"/>.
    /// <paramref name="afterUntil"/> says whether an occurrence is past the rule's UNTIL (which may be UTC, so only the
    /// event's zone can tell).
    /// </summary>
    public static IEnumerable<DateTime> Expand(DateTime start, IcsRule rule, DateTime through, Func<DateTime, bool>? afterUntil = null)
    {
        afterUntil ??= t => rule.Until is { } u && t > (u.IsDate ? u.Value.Date.AddDays(1).AddTicks(-1) : u.Value);
        int produced = 0;
        if (start > through) yield break;
        if (afterUntil(start)) yield break;
        yield return start;
        produced++;
        if (rule.Count is { } max && produced >= max) yield break;
        var time = start.TimeOfDay;
        for (int step = 0; step < MaxSteps; step++)
        {
            var period = PeriodStart(start, rule, step);
            if (period > through) yield break;
            foreach (var day in Candidates(period, rule, start))
            {
                var t = day.Date + time;
                if (t <= start) continue;
                if (t > through || afterUntil(t)) yield break;
                yield return t;
                produced++;
                if (rule.Count is { } m && produced >= m) yield break;
            }
        }
    }

    /// <summary>The first day of the step-th period (day, week, month or year) counted from the one start is in.</summary>
    static DateTime PeriodStart(DateTime start, IcsRule rule, int step)
    {
        long n = (long)step * rule.Interval;
        var d = start.Date;
        return rule.Freq switch
        {
            "DAILY" => n > 3_000_000 ? DateTime.MaxValue.Date : d.AddDays(n),
            "WEEKLY" => n > 400_000 ? DateTime.MaxValue.Date : d.AddDays(-(((int)d.DayOfWeek - (int)rule.WeekStart + 7) % 7)).AddDays(7 * n),
            "MONTHLY" => n > 90_000 ? DateTime.MaxValue.Date : new DateTime(d.Year, d.Month, 1).AddMonths((int)n),
            _ => n > 7_000 || d.Year + n > 9998 ? DateTime.MaxValue.Date : new DateTime(d.Year + (int)n, 1, 1),
        };
    }

    /// <summary>The days in one period the rule picks, in order.</summary>
    static List<DateTime> Candidates(DateTime period, IcsRule rule, DateTime start)
    {
        var days = new List<DateTime>();
        switch (rule.Freq)
        {
            case "DAILY":
                days.Add(period);
                break;
            case "WEEKLY":
                var weekdays = rule.ByDay.Count > 0 ? rule.ByDay.Select(b => b.Day).ToHashSet() : [start.DayOfWeek];
                for (int i = 0; i < 7; i++)
                    if (weekdays.Contains(period.AddDays(i).DayOfWeek)) days.Add(period.AddDays(i));
                break;
            case "MONTHLY":
                days.AddRange(InMonth(period.Year, period.Month, rule, start));
                break;
            default:
                if (rule.ByMonth.Count == 0 && rule.ByMonthDay.Count == 0 && rule.ByDay.Count > 0)
                {
                    // "Every year on the 20th Monday": the ordinal counts through the year.
                    var all = Enumerable.Range(0, DateTime.IsLeapYear(period.Year) ? 366 : 365).Select(i => period.AddDays(i)).ToList();
                    days.AddRange(ByDayIn(all, rule.ByDay));
                    break;
                }
                var months = rule.ByMonth.Count > 0 ? rule.ByMonth.Order() : (IEnumerable<int>)[start.Month];
                foreach (int m in months) days.AddRange(InMonth(period.Year, m, rule, start));
                break;
        }
        // Filters a finer rule leaves to its parts: a daily rule on weekdays only, a weekly one in some months.
        if (rule.ByMonth.Count > 0) days.RemoveAll(d => !rule.ByMonth.Contains(d.Month));
        if (rule.Freq == "DAILY")
        {
            if (rule.ByDay.Count > 0) days.RemoveAll(d => rule.ByDay.All(b => b.Day != d.DayOfWeek));
            if (rule.ByMonthDay.Count > 0) days.RemoveAll(d => !MonthDayMatches(d, rule.ByMonthDay));
        }
        days.Sort();
        return SetPos(days, rule.BySetPos);
    }

    /// <summary>The days of one month a monthly (or yearly-by-month) rule picks.</summary>
    static List<DateTime> InMonth(int year, int month, IcsRule rule, DateTime start)
    {
        int length = DateTime.DaysInMonth(year, month);
        var all = Enumerable.Range(1, length).Select(d => new DateTime(year, month, d)).ToList();
        if (rule.ByMonthDay.Count == 0 && rule.ByDay.Count == 0)
            return start.Day <= length ? [new DateTime(year, month, start.Day)] : [];
        var picked = rule.ByMonthDay.Count > 0 ? all.Where(d => MonthDayMatches(d, rule.ByMonthDay)).ToList() : all;
        return rule.ByDay.Count > 0 ? ByDayIn(picked, rule.ByDay, all) : picked;
    }

    static bool MonthDayMatches(DateTime d, IReadOnlyList<int> monthDays)
    {
        int length = DateTime.DaysInMonth(d.Year, d.Month);
        return monthDays.Any(n => n > 0 ? d.Day == n : d.Day == length + n + 1);
    }

    /// <summary>The days among <paramref name="days"/> that BYDAY picks: any Tuesday, or the 2nd Sunday / the last
    /// Friday counted through <paramref name="span"/> (the month or year).</summary>
    static List<DateTime> ByDayIn(List<DateTime> days, IReadOnlyList<(int Nth, DayOfWeek Day)> byDay, List<DateTime>? span = null)
    {
        span ??= days;
        var result = new HashSet<DateTime>();
        foreach (var (nth, day) in byDay)
        {
            var matching = span.Where(d => d.DayOfWeek == day).ToList();
            if (nth == 0) result.UnionWith(matching.Where(days.Contains));
            else if (nth > 0 && nth <= matching.Count && days.Contains(matching[nth - 1])) result.Add(matching[nth - 1]);
            else if (nth < 0 && -nth <= matching.Count && days.Contains(matching[^-nth])) result.Add(matching[^-nth]);
        }
        return [.. result.Order()];
    }

    /// <summary>BYSETPOS: of the period's days, only the nth (from the end when negative).</summary>
    static List<DateTime> SetPos(List<DateTime> days, IReadOnlyList<int> setPos)
    {
        if (setPos.Count == 0) return days;
        var result = new SortedSet<DateTime>();
        foreach (int p in setPos)
        {
            if (p > 0 && p <= days.Count) result.Add(days[p - 1]);
            else if (p < 0 && -p <= days.Count) result.Add(days[^-p]);
        }
        return [.. result];
    }
}
