using System.Text.Json;
using System.Text.Json.Serialization;

namespace StudyStash.Core.Calendar;

/// <summary>
/// One calendar source the student added, as calendars.json keeps it: its id and kind, the name Settings shows, the
/// feed's address for an iCalendar feed, and anything else its kind needs to make it again (<see cref="Data"/>: an
/// account id, say; never a password or a token, which a kind keeps in the system's keychain).
/// </summary>
public sealed class CalendarSourceSettings
{
    public string Id { get; set; } = "";
    public string Kind { get; set; } = "";
    public string Name { get; set; } = "";
    /// <summary>An iCalendar feed's address as pasted (webcal:// or https://); "" for other kinds.</summary>
    public string Url { get; set; } = "";
    public Dictionary<string, string> Data { get; set; } = [];
    /// <summary>When it was added (ISO).</summary>
    public string Added { get; set; } = "";
}

/// <summary>
/// Calendars, as this computer knows them (calendars.json beside client.toml): the sources the student added, the
/// calendars each one has (as last listed, so Settings shows them at once), which of those the student turned off,
/// and the classes to record by themselves when their event starts (filled in by auto-record). Calendars run on the
/// computer that records; the library only hears what's coming up.
/// </summary>
public sealed class CalendarSettings
{
    public List<CalendarSourceSettings> Sources { get; set; } = [];
    /// <summary>Every calendar the sources had when last listed.</summary>
    public List<CalendarInfo> Calendars { get; set; } = [];
    /// <summary>Calendar id → shown or not, for the calendars the student switched in Settings. A calendar not here
    /// follows its source's suggestion (<see cref="CalendarInfo.Enabled"/>).</summary>
    public Dictionary<string, bool> Enabled { get; set; } = [];
    /// <summary>The classes whose lectures start recording by themselves when their calendar event does. Kept here
    /// for auto-record; nothing in calendars reads it.</summary>
    public List<string> AutoRecordClasses { get; set; } = [];
    /// <summary>What went wrong the last time each source was read (source id → words for Settings); a source that
    /// read fine isn't here.</summary>
    public Dictionary<string, string> Problems { get; set; } = [];

    /// <summary>Whether Study Stash shows this calendar's events: the student's choice, else its source's suggestion.</summary>
    public bool IsEnabled(CalendarInfo c) => Enabled.TryGetValue(c.Id, out bool on) ? on : c.Enabled;

    /// <summary>The calendars shown, by source.</summary>
    public List<CalendarInfo> EnabledCalendars(string sourceId) => [.. Calendars.Where(c => c.SourceId == sourceId && IsEnabled(c))];

    public CalendarSourceSettings? Source(string id) => Sources.FirstOrDefault(s => s.Id == id);

    public static string PathIn(string home) => Path.Combine(home, "calendars.json");

    static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase, DictionaryKeyPolicy = null,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };
    static readonly Lock Gate = new();

    /// <summary>calendars.json, or no calendars when there's none (or it can't be read).</summary>
    public static CalendarSettings Load(string home)
    {
        lock (Gate)
        {
            if (SharedFile.Read(PathIn(home)) is not { } text) return new CalendarSettings();
            try
            {
                return JsonSerializer.Deserialize<CalendarSettings>(text, Options) ?? new CalendarSettings();
            }
            catch (JsonException)
            {
                return new CalendarSettings();
            }
        }
    }

    /// <summary>Written for this person only: a feed's address is often a secret one (Google's "secret address").</summary>
    public void Save(string home)
    {
        lock (Gate)
        {
            Directory.CreateDirectory(home);
            string p = PathIn(home);
            SharedFile.Write(p, JsonSerializer.Serialize(this, Options) + "\n");
            Py.OwnerOnly(p);
        }
    }

    /// <summary>Load, change and save in one step, so Settings and the poll don't lose each other's changes.</summary>
    public static CalendarSettings Update(string home, Action<CalendarSettings> change)
    {
        lock (Gate)
        {
            var s = Load(home);
            change(s);
            s.Save(home);
            return s;
        }
    }

    /// <summary>Take a source away, with its calendars, choices and problem.</summary>
    public void Remove(string sourceId)
    {
        var gone = Calendars.Where(c => c.SourceId == sourceId).Select(c => c.Id).ToHashSet();
        Sources.RemoveAll(s => s.Id == sourceId);
        Calendars.RemoveAll(c => c.SourceId == sourceId);
        foreach (string id in gone) Enabled.Remove(id);
        Problems.Remove(sourceId);
    }

    /// <summary>A source listed its calendars afresh: they replace what it had, and a calendar it no longer has loses
    /// the student's choice about it too.</summary>
    public void Listed(string sourceId, IEnumerable<CalendarInfo> calendars)
    {
        var fresh = calendars.Where(c => c.SourceId == sourceId).ToList();
        var ids = fresh.Select(c => c.Id).ToHashSet();
        foreach (var old in Calendars.Where(c => c.SourceId == sourceId && !ids.Contains(c.Id))) Enabled.Remove(old.Id);
        int at = Calendars.FindIndex(c => c.SourceId == sourceId);
        Calendars.RemoveAll(c => c.SourceId == sourceId);
        Calendars.InsertRange(at < 0 ? Calendars.Count : Math.Min(at, Calendars.Count), fresh);
    }
}
