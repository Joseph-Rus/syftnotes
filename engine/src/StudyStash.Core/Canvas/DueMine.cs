using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;

namespace StudyStash.Core.Canvas;

/// <summary>One thing the student put on their own Due list: an exam, a study session or a to-do. <paramref name="What"/> is
/// exam | study | task; <paramref name="Class"/> is a class's name or "". <paramref name="Due"/> is the student's clock
/// ("2026-11-05T09:00") and <paramref name="DueAt"/> the same moment in UTC, both "" for no date; <paramref name="AllDay"/> when
/// they gave a day and no time (it is then due at the end of the day, like most things on Canvas).</summary>
public sealed record OwnDue(long Id, string Title, string What, string Class, string Due, string DueAt, bool AllDay);

/// <summary>
/// The student's own items on the Due list, in <c>home/due_mine.json</c>: Syft Notes only, no Canvas and no calendar to sync
/// with, so they are there with no Canvas linked. They join the list as <see cref="Assignment"/> rows of kind
/// <see cref="Kind"/> in <see cref="Assignments.Marked"/>, which every reader of the list goes through, and "done" is the
/// same <see cref="DueMarks"/> as for a Canvas item (class "", kind "mine").
/// </summary>
public static class DueMine
{
    public const string Kind = "mine";
    public static readonly string[] Whats = ["exam", "study", "task"];

    static readonly JsonSerializerOptions Options = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };
    static readonly Lock Gate = new();

    public static string PathIn(string home) => Path.Combine(home, "due_mine.json");

    public static List<OwnDue> Load(string home)
    {
        try
        {
            return SharedFile.Read(PathIn(home)) is { } text ? JsonSerializer.Deserialize<List<OwnDue>>(text, Options) ?? [] : [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    /// <summary>What's wrong with what the student typed, in a sentence; null when it's fine. <paramref name="day"/> is
    /// "2026-11-05" or "" (no date), <paramref name="time"/> is "09:00" or "" (none, only with a day).</summary>
    public static string? Problem(string title, string what, string day, string time)
    {
        if (title.Trim().Length == 0) return "Give it a title.";
        if (!Whats.Contains(what)) return "It is an exam, a study session or a to-do.";
        if (day.Length > 0 && !DateTime.TryParseExact(day, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)) return "That isn't a day.";
        if (time.Length > 0 && (day.Length == 0 || !TimeSpan.TryParseExact(time, @"hh\:mm", CultureInfo.InvariantCulture, out _))) return "That isn't a time.";
        return null;
    }

    /// <summary>The item for what the student typed (already <see cref="Problem"/>-free), its moment worked out once in
    /// <paramref name="zone"/>: no day is no date; a day with no time is that day's end (23:59) and all day.</summary>
    public static OwnDue Make(long id, string title, string what, string cls, string day, string time, TimeZoneInfo zone)
    {
        title = title.Trim();
        if (day.Length == 0) return new OwnDue(id, title, what, cls, "", "", false);
        var local = DateTime.ParseExact(day, "yyyy-MM-dd", CultureInfo.InvariantCulture)
            + (time.Length > 0 ? TimeSpan.ParseExact(time, @"hh\:mm", CultureInfo.InvariantCulture) : new TimeSpan(23, 59, 0));
        // A time the clocks skip (spring forward) is the hour after it.
        if (zone.IsInvalidTime(local)) local = local.AddHours(1);
        var utc = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local, zone), TimeSpan.Zero);
        return new OwnDue(id, title, what, cls, local.ToString("yyyy-MM-dd'T'HH:mm", CultureInfo.InvariantCulture),
            utc.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture), time.Length == 0);
    }

    /// <summary>Adds the item, or (with an id the list has) replaces it. An id of 0 gets a new one. Null when it names an
    /// id that isn't there (deleted from another screen meanwhile).</summary>
    public static OwnDue? Upsert(string home, OwnDue item)
    {
        lock (Gate)
        {
            var all = Load(home);
            if (item.Id == 0)
            {
                long id;
                do id = RandomNumberGenerator.GetInt32(1, int.MaxValue);
                while (all.Any(o => o.Id == id));
                all.Add(item = item with { Id = id });
            }
            else if (all.FindIndex(o => o.Id == item.Id) is var at and >= 0) all[at] = item;
            else return null;
            SharedFile.Write(PathIn(home), JsonSerializer.Serialize(all, Options));
            return item;
        }
    }

    /// <summary>Takes the item off the list, and its mark with it. False when it wasn't there.</summary>
    public static bool Delete(string home, long id)
    {
        lock (Gate)
        {
            var all = Load(home);
            if (all.RemoveAll(o => o.Id == id) == 0) return false;
            SharedFile.Write(PathIn(home), JsonSerializer.Serialize(all, Options));
        }
        DueMarks.Set(home, "", Kind, id, "", DateTimeOffset.UtcNow);
        return true;
    }

    /// <summary>A class was renamed: its items move to the new name.</summary>
    public static void Rename(string home, string from, string to)
    {
        lock (Gate)
        {
            var all = Load(home);
            if (!all.Any(o => o.Class == from)) return;
            SharedFile.Write(PathIn(home), JsonSerializer.Serialize(all.Select(o => o.Class == from ? o with { Class = to } : o).ToList(), Options));
        }
    }

    /// <summary>An exam or a study session: it happens at its time and then it is simply behind the student, where a to-do stays
    /// theirs until it is ticked off (<see cref="Assignments.Behind"/>).</summary>
    public static bool IsEvent(Assignment a) => a.Kind == Kind && a.What is "exam" or "study";

    /// <summary>The items as rows of the Due list: always to do (the student ticks them off with a <see cref="DueMarks"/> mark).</summary>
    public static List<Assignment> Rows(string home) =>
        [.. Load(home).Select(o => new Assignment(o.Class, o.Id, o.Title, o.Due, null, "open", null, "", "", Kind: Kind, DueAt: o.DueAt, What: o.What, AllDay: o.AllDay))];
}
