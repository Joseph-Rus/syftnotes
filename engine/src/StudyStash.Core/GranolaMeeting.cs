using System.Globalization;
using System.Net;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace StudyStash.Core;

/// <summary>Reading a lecture as Granola hands it over into a Meeting.</summary>
public static partial class Granola
{
    public static readonly string[] IdKeys = ["id", "document_id", "meeting_id", "note_id", "doc_id"];
    public static readonly string[] TitleKeys = ["title", "name", "subject"];
    public static readonly string[] DateKeys = ["date", "meeting_date", "start_time", "started_at", "created_at", "start", "datetime"];
    public static readonly string[] NotesKeys = ["notes_markdown", "summary_markdown", "notes", "summary", "content", "markdown", "text", "body", "enhanced_notes"];
    public static readonly string[] PrivateKeys = ["private_notes", "private_notes_markdown", "my_notes", "user_notes"];
    public static readonly string[] FolderKeys = ["folder", "folder_name", "folders", "folder_id"];
    public static readonly string[] AttendeeKeys = ["attendees", "participants", "people"];
    public static readonly string[] ParticipantTextKeys = ["known_participants", "known_attendees"];
    public static readonly string[] OwnerKeys = ["owner", "creator", "author"];

    /// <summary>The first of these keys with a value: not missing, null, "", or [].</summary>
    public static JsonNode? FirstKey(JsonObject d, IEnumerable<string> keys)
    {
        foreach (string k in keys)
        {
            if (!d.TryGetPropertyValue(k, out var v) || v is null) continue;
            if (Py.AsString(v) == "" || v is JsonArray { Count: 0 }) continue;
            return v;
        }
        return null;
    }

    public static string AsText(JsonNode? v) => v switch
    {
        null => "",
        JsonObject o => Or(FirstKey(o, ["markdown", "text", "content", "name", "email"]), o),
        JsonArray a => string.Join("\n", a.Select(AsText)),
        _ => Py.Str(v),
    };

    /// <summary>str(value or json.dumps(whole)).</summary>
    static string Or(JsonNode? value, JsonObject whole) => Py.Truthy(value) ? Py.Str(value) : PyJson.Dumps(whole);

    public static List<string> AsNameList(JsonNode? v)
    {
        if (!Py.Truthy(v)) return [];
        if (Py.AsString(v) is string s)
            return Regex.Split(s, "[,;\n]").Select(Py.Strip).Where(x => x.Length > 0).ToList();
        IEnumerable<JsonNode?> items = v switch
        {
            JsonArray a => a,
            JsonObject o => o.Select(kv => (JsonNode?)JsonValue.Create(kv.Key)), // iterating a dict gives its keys
            _ => throw new PayloadException($"not a list of names: {Py.Repr(v)}"),
        };
        return items.Select(item => item is JsonObject o ? Or(FirstKey(o, ["name", "email", "display_name"]), o) : Py.Str(item))
            .ToList();
    }

    // --- dates ---------------------------------------------------------------------------------------

    static readonly string[] MonthAbbr = ["jan", "feb", "mar", "apr", "may", "jun", "jul", "aug", "sep", "oct", "nov", "dec"];
    static readonly string[] MonthFull = ["january", "february", "march", "april", "may", "june", "july", "august",
        "september", "october", "november", "december"];

    // strptime's own patterns for %d %I %H %M %Y %p (ASCII digits only); a space matches any run of spaces.
    const string D = @"(?<d>3[01]|[12][0-9]|0[1-9]|[1-9]| [1-9])";
    const string I = @"(?<I>1[0-2]|0[1-9]|[1-9]| [1-9])";
    const string H = @"(?<H>2[0-3]|[0-1][0-9]|[0-9])";
    const string M = @"(?<M>[0-5][0-9]|[0-9])";
    const string Y = @"(?<Y>[0-9][0-9][0-9][0-9])";
    const string P = @"(?<p>am|pm)";
    static readonly string Abbr = "(?<b>" + string.Join("|", MonthAbbr) + ")";
    static readonly string Full = "(?<B>" + string.Join("|", MonthFull) + ")";

    // The formats parse_granola_date tries, in order: (pattern, has a time).
    static readonly (Regex Pattern, bool WithTime)[] DateFormats =
    [
        (Fmt($@"{Abbr}\s+{D},\s+{Y}\s+{I}:{M}\s+{P}"), true),
        (Fmt($@"{Full}\s+{D},\s+{Y}\s+{I}:{M}\s+{P}"), true),
        (Fmt($@"{Abbr}\s+{D},\s+{Y}\s+{H}:{M}"), true),
        (Fmt($@"{Full}\s+{D},\s+{Y}\s+{H}:{M}"), true),
        (Fmt($@"{Abbr}\s+{D},\s+{Y}"), false),
        (Fmt($@"{Full}\s+{D},\s+{Y}"), false),
        (Fmt($@"{D}\s+{Abbr}\s+{Y}\s+{H}:{M}"), true),
        (Fmt($@"{D}\s+{Abbr}\s+{Y}"), false),
    ];

    static Regex Fmt(string pattern) => new(@"\A" + pattern + @"\z", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    [GeneratedRegex(@"\s+\(?(?!(?:AM|PM)\)?$)[A-Z]{2,5}\)?$")]
    private static partial Regex TzSuffix();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();

    /// <summary>'Feb 4, 2026 7:30 PM' → '2026-02-04T19:30:00'; 'Feb 4, 2026' → '2026-02-04'. Anything else is kept as given.</summary>
    public static string ParseGranolaDate(string s)
    {
        string text = Py.Strip(s).Replace('\u202f', ' ').Replace('\u00a0', ' ');
        if (text.Length == 0) return s;
        // Granola stamps the user's zone on listings ("Sep 15, 2026 2:20 PM PDT"): keep the wall-clock time and
        // drop the zone. AM/PM is never a zone, so it must survive, or 7:30 PM would turn into 07:30.
        string squashed = TzSuffix().Replace(Spaces().Replace(text, " "), "");
        foreach (var (pattern, withTime) in DateFormats)
        {
            var m = pattern.Match(squashed);
            if (!m.Success) continue;
            int month = m.Groups["b"].Success ? Array.IndexOf(MonthAbbr, m.Groups["b"].Value.ToLowerInvariant()) + 1
                : Array.IndexOf(MonthFull, m.Groups["B"].Value.ToLowerInvariant()) + 1;
            int year = int.Parse(m.Groups["Y"].Value, CultureInfo.InvariantCulture);
            int day = int.Parse(m.Groups["d"].Value, CultureInfo.InvariantCulture);
            if (year < 1 || day > DateTime.DaysInMonth(year, month)) continue;
            if (!withTime) return $"{year:0000}-{month:00}-{day:00}";
            int hour = m.Groups["H"].Success ? int.Parse(m.Groups["H"].Value, CultureInfo.InvariantCulture)
                : int.Parse(m.Groups["I"].Value, CultureInfo.InvariantCulture) % 12
                  + (m.Groups["p"].Value.Equals("pm", StringComparison.OrdinalIgnoreCase) ? 12 : 0);
            int minute = int.Parse(m.Groups["M"].Value, CultureInfo.InvariantCulture);
            return $"{year:0000}-{month:00}-{day:00}T{hour:00}:{minute:00}:00";
        }
        return s;
    }

    // --- participants ----------------------------------------------------------------------------------

    [GeneratedRegex(@"\(\s*(?:note\s+)?(?:creator|owner|organi[sz]er)\s*\)", RegexOptions.IgnoreCase)]
    private static partial Regex CreatorMark();

    [GeneratedRegex("<([^<>\\s\"']+@[^<>\\s\"']+)>")]
    internal static partial Regex EmailToken();

    [GeneratedRegex("<[^<>]*>")]
    private static partial Regex AngleToken();

    [GeneratedRegex(@"\s+from\s+.+$", RegexOptions.IgnoreCase)]
    private static partial Regex FromOrg();

    /// <summary>'John Doe (note creator) from Acme &lt;john@acme.com&gt;' → ("John Doe", true, "john@acme.com").</summary>
    public static (string Name, bool IsCreator, string Email) ParseParticipant(string? line)
    {
        string text = Py.Strip(WebUtility.HtmlDecode(line ?? ""));
        bool isCreator = CreatorMark().IsMatch(text);
        var em = EmailToken().Match(text);
        string email = em.Success ? Py.Strip(em.Groups[1].Value) : "";
        text = CreatorMark().Replace(text, " ");
        text = AngleToken().Replace(text, " ");
        text = FromOrg().Replace(Py.Strip(text), "");
        text = Spaces().Replace(text, " ").Trim(' ', ',', ';', '-', '\u2013', '\u2014');
        if (text.Length == 0 && email.Length > 0) text = email;
        return (text, isCreator, email);
    }

    /// <summary>Attendee names from a known_participants block, and the note creator's name.</summary>
    public static (List<string> Names, string Creator) ParseParticipants(JsonNode? value)
    {
        if (!Py.Truthy(value)) return ([], "");
        if (value is JsonObject o)
        {
            value = FirstKey(o, ["participant", "text", "name"]);
            if (!Py.Truthy(value)) return ([], "");
        }
        IEnumerable<string> lines = Py.AsString(value) is string s ? Py.SplitLines(s)
            : value is JsonArray a ? a.Select(AsText)
            : value is JsonObject keys ? keys.Select(kv => kv.Key)
            : throw new PayloadException($"not a list of participants: {Py.Repr(value)}");
        var names = new List<string>();
        string creator = "";
        foreach (string line in lines)
        {
            var (name, isCreator, _) = ParseParticipant(line);
            if (name.Length == 0) continue;
            names.Add(name);
            if (isCreator && creator.Length == 0) creator = name;
        }
        return (names, creator);
    }

    public static Meeting NormalizeMeeting(JsonObject raw)
    {
        var mid = FirstKey(raw, IdKeys)
            ?? throw new PayloadException($"meeting has no id: {Py.Repr(new JsonArray(raw.Take(10).Select(kv => (JsonNode?)JsonValue.Create(kv.Key)).ToArray()))}");
        var folderNode = FirstKey(raw, FolderKeys);
        string folder = folderNode switch
        {
            JsonArray => string.Join(", ", AsNameList(folderNode)),
            JsonObject f => Py.Truthy(FirstKey(f, ["name", "title", "id"])) ? Py.Str(FirstKey(f, ["name", "title", "id"])) : "",
            _ => Py.Truthy(folderNode) ? Py.Str(folderNode) : "",
        };
        var attendees = AsNameList(FirstKey(raw, AttendeeKeys));
        string creator = "";
        var participants = FirstKey(raw, ParticipantTextKeys);
        if (participants is not null)
        {
            (var names, creator) = ParseParticipants(participants);
            if (attendees.Count == 0) attendees = names;
        }
        string owner = AsText(FirstKey(raw, OwnerKeys));
        var title = FirstKey(raw, TitleKeys);
        var date = FirstKey(raw, DateKeys);
        raw.TryGetPropertyValue("transcript", out var transcript);
        return new Meeting(Py.Str(mid))
        {
            Title = Py.Truthy(title) ? Py.Str(title) : "Untitled",
            Date = ParseGranolaDate(Py.Truthy(date) ? Py.Str(date) : ""),
            Owner = owner.Length > 0 ? owner : creator,
            Attendees = attendees,
            Folder = folder,
            NotesMarkdown = AsText(FirstKey(raw, NotesKeys)),
            PrivateNotes = AsText(FirstKey(raw, PrivateKeys)),
            Transcript = AsText(transcript),
            Raw = raw,
        };
    }

}
