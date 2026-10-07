using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace StudyStash.Core.Canvas;

/// <summary>
/// Canvas, as this library knows it (canvas.json beside config.toml): the school's Canvas address, which Canvas
/// course each class is, and how the last sync went. Canvas is read through a small Chrome extension, with the
/// person's own sign-in, because many schools turn off Canvas's access tokens. Nothing here writes to Canvas.
/// </summary>
public sealed class CanvasSettings
{
    /// <summary>The school's Canvas, like https://school.instructure.com. Empty: Canvas isn't set up.</summary>
    public string Url { get; set; } = "";
    /// <summary>Class name → Canvas course id.</summary>
    public Dictionary<string, long> Courses { get; set; } = [];
    /// <summary>The person's Canvas courses (id → name), as last looked up, for picking which class is which.</summary>
    public Dictionary<string, string> Available { get; set; } = [];
    /// <summary>The same courses' code and term (id → info), for <see cref="CourseMatch"/> and the classes screen.</summary>
    public Dictionary<string, CourseInfo> CourseInfo { get; set; } = [];
    /// <summary>The Canvas courses (ids) the student chose to bring in; only these sync, and only these are explored.
    /// Null in a library from before the choice: then every linked course is chosen.</summary>
    public List<string>? Chosen { get; set; }
    /// <summary>How often the extension reads Canvas again.</summary>
    public int PollMinutes { get; set; } = 60;
    /// <summary>When the last sync started (ISO), and when the extension last asked for work.</summary>
    public string LastSync { get; set; } = "";
    /// <summary>When the last sync finished (ISO); "" before any sync has finished. <see cref="LastSync"/> is when it
    /// started, which is what scheduling (<see cref="Due"/>) goes by.</summary>
    public string LastDone { get; set; } = "";
    /// <summary>When the current <see cref="Error"/> started (ISO); "" while there's no error.</summary>
    public string ErrorAt { get; set; } = "";
    /// <summary>When the extension last asked for work (ISO); "" when it never has. Written at most every
    /// <see cref="SeenEvery"/> while nothing else about it changes.</summary>
    public string ExtensionSeen { get; set; } = "";
    /// <summary>The version of the extension Chrome last ran ("1.3").</summary>
    public string ExtensionVersion { get; set; } = "";
    /// <summary>The protocol the extension that last checked in speaks (1 for one from before protocol 2).</summary>
    public int ExtensionProtocol { get; set; }
    /// <summary>Which key the Chrome that last checked in used (<see cref="KeyId"/>): the extension's key as it was
    /// then, or "password" for one that came with the library password instead. "" in a canvas.json from before keys
    /// were written down.</summary>
    public string ExtensionKeyId { get; set; } = "";
    /// <summary>Which Chrome last checked in, from the library address the extension says it uses:
    /// "this_computer" (a loopback address), "another_computer", or "" when it didn't say.</summary>
    public string ExtensionWhere { get; set; } = "";
    /// <summary>Which browser the extension that last checked in runs in, as it names itself ("Chrome", "Edge",
    /// "Firefox"); "" for an extension from before 1.6, which didn't say.</summary>
    public string ExtensionBrowser { get; set; } = "";
    /// <summary>Every Chrome that checks in, by where it is ("this_computer", "another_computer", or "" for one from
    /// before 1.4 that doesn't say): when it last asked, and its version and protocol. The library's Chrome and the
    /// laptop's can both run the extension; the fields above are the one that asked last.</summary>
    public Dictionary<string, ExtensionCopy> ExtensionCopies { get; set; } = [];
    /// <summary>Every browser that has said its name while checking in, by where it is and then by name, and when it
    /// last did (ISO): how the library knows that two browsers in one place both run the extension.</summary>
    public Dictionary<string, Dictionary<string, string>> BrowsersSeen { get; set; } = [];
    /// <summary>The browser the student picked for a place where two run the extension: that one reads Canvas while
    /// it's running, and the other is left alone.</summary>
    public Dictionary<string, string> BrowserChoice { get; set; } = [];
    /// <summary>Chrome's extension updated itself (it reloads from a folder Study Stash keeps up to date): Settings
    /// says so ("The Chrome extension updated itself. Now version 1.3.") until it's dismissed.</summary>
    public ExtensionUpdate? ExtensionUpdate { get; set; }
    /// <summary>What went wrong last, for Settings; empty when all is well.</summary>
    public string Error { get; set; } = "";
    /// <summary>Chrome isn't signed in to Canvas: syncing waits until it is.</summary>
    public bool NeedsLogin { get; set; }
    /// <summary>Sync on the extension's next visit instead of waiting for <see cref="PollMinutes"/>.</summary>
    public bool SyncNow { get; set; }
    /// <summary>What changed in the last sync that changed anything ("New: CS 101 · Lab 3 · due Tue 11:59 PM").</summary>
    public List<string> Changes { get; set; } = [];
    /// <summary>The same changes with what each is about (kind, class, assignment), for the app's list and notifications.</summary>
    public List<CanvasChange> LastChanges { get; set; } = [];
    /// <summary>What the course scout said last, per class.</summary>
    public Dictionary<string, ScoutReport> Scouts { get; set; } = [];

    [JsonIgnore] public bool On => Url.Length > 0;

    /// <summary>The <see cref="KeyId"/> of the key this library gives its extension now (canvas_key), filled in by
    /// <see cref="Load"/>; "" while there's no key yet.</summary>
    [JsonIgnore] public string CurrentKeyId { get; set; } = "";

    /// <summary>The Chromes that checked in with the key this library gives its extension now. A registration from
    /// before the key changed, or from before keys were written down, isn't one of them: it can't hand Canvas to this
    /// library, so it never counts as connected.</summary>
    IEnumerable<ExtensionCopy> WithCurrentKey() => ExtensionCopies.Count > 0
        ? ExtensionCopies.Values.Where(c => c.Key == CurrentKeyId)
        : ExtensionKeyId == CurrentKeyId && ExtensionSeen.Length > 0 ? [new ExtensionCopy(ExtensionSeen, ExtensionVersion, ExtensionProtocol, ExtensionKeyId)] : [];

    /// <summary>When a Chrome last checked in with the current key (ISO); "" when none ever has.</summary>
    [JsonIgnore] public string SeenWithKey => WithCurrentKey().Select(c => c.Seen).DefaultIfEmpty("").Max(StringComparer.Ordinal)!;

    /// <summary>The Chrome that checked in last used the current key.</summary>
    [JsonIgnore] public bool LastKeyMatches => ExtensionSeen.Length > 0 && ExtensionKeyId == CurrentKeyId;

    /// <summary>The extension Chrome runs is older than this library's: its folder wasn't brought up to date (one the
    /// laptop app made, say), so it can't reload into the new version by itself.</summary>
    [JsonIgnore] public bool ExtensionOutdated => Extension.IsOlder(ExtensionVersion, Extension.Version());

    /// <summary>The browser the extension runs in, for a sentence: its name once an extension has said it
    /// (<see cref="ExtensionBrowser"/>), "your browser" until then ("Your browser" to <paramref name="start"/> one).</summary>
    public string BrowserName(bool start = false) => BrowserName(ExtensionBrowser, start);

    /// <summary><see cref="BrowserName(bool)"/> for any browser's name ("" when it isn't known).</summary>
    public static string BrowserName(string? browser, bool start = false) =>
        browser is { Length: > 0 } ? browser : start ? "Your browser" : "your browser";

    /// <summary>A browser's name as an extension gave it (its <c>b</c>), safe to show and to keep: letters, digits and
    /// spaces, 24 characters at most; "" for anything else.</summary>
    public static string CleanBrowser(string? name)
    {
        string trimmed = (name ?? "").Trim();
        return trimmed.Length is > 0 and <= 24 && trimmed.All(c => char.IsAsciiLetterOrDigit(c) || c == ' ') ? trimmed : "";
    }

    /// <summary>How often a check-in that changes nothing else is written down.</summary>
    public static readonly TimeSpan SeenEvery = TimeSpan.FromSeconds(15);

    /// <summary>How long after a browser's last check-in it still counts as running the extension now. An idle
    /// extension asks about once a minute, so five minutes of quiet means its browser is closed.</summary>
    public static readonly TimeSpan BrowserHere = TimeSpan.FromMinutes(5);

    /// <summary>Whether something that last checked in at <paramref name="seen"/> (ISO) is still here now.</summary>
    public static bool Here(string? seen, DateTimeOffset now) =>
        DateTimeOffset.TryParse(seen, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal, out var at)
        && now - at <= BrowserHere && now >= at - BrowserHere;

    /// <summary>The browsers running the extension in one place right now, by name, in the order of the alphabet.</summary>
    public IReadOnlyList<string> BrowsersHere(string where, DateTimeOffset now) =>
        [.. BrowsersSeen.GetValueOrDefault(where, []).Where(b => Here(b.Value, now)).Select(b => b.Key).Order(StringComparer.Ordinal)];

    /// <summary>A place where two browsers (or more) run the extension now: there's a choice between them, made or
    /// not (Settings offers to change it). Null when there's only one, or none.</summary>
    public (string Where, IReadOnlyList<string> Browsers)? BrowsersToChoose(DateTimeOffset now)
    {
        foreach (string where in BrowsersSeen.Keys.Order(StringComparer.Ordinal))
            if (BrowsersHere(where, now) is { Count: > 1 } here) return (where, here);
        return null;
    }

    /// <summary>That choice while the student hasn't made it (their pick isn't one of the browsers here): the
    /// question the app asks by itself. Null when there's nothing to ask.</summary>
    public (string Where, IReadOnlyList<string> Browsers)? BrowserQuestion(DateTimeOffset now) =>
        BrowsersToChoose(now) is { } choice && !choice.Browsers.Contains(BrowserChoice.GetValueOrDefault(choice.Where, "")) ? choice : null;

    /// <summary>An extension with the current key is checking in: lately enough that it's running now. One that
    /// long-polls (protocol 3 and later) asks all the time, so 90 seconds of quiet means it's gone; an older one only
    /// asks every minute or so while idle, and gets five. A check-in with an old key never counts.</summary>
    public bool ExtensionConnected(DateTimeOffset now) => WithCurrentKey().Any(c => Connected(c.Seen, c.Protocol, now));

    /// <summary>Whether an extension that last asked at <paramref name="seen"/> (ISO) speaking <paramref name="protocol"/>
    /// is running now (<see cref="ExtensionConnected"/>).</summary>
    public static bool Connected(string seen, int protocol, DateTimeOffset now) =>
        DateTimeOffset.TryParse(seen, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal, out var at)
        && now - at <= (protocol >= 3 ? TimeSpan.FromSeconds(90) : TimeSpan.FromMinutes(5));

    /// <summary>"this_computer" when the library address the extension uses is this computer's own (loopback),
    /// "another_computer" for any other address, "" when it didn't say (<paramref name="address"/> is the
    /// extension's <c>a</c>).</summary>
    public static string WhereFrom(string? address)
    {
        if (string.IsNullOrWhiteSpace(address)) return "";
        if (!Uri.TryCreate(address.Trim(), UriKind.Absolute, out var u)) return "";
        return u.IsLoopback || u.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ? "this_computer" : "another_computer";
    }

    public static string PathIn(string home) => Path.Combine(home, "canvas.json");

    static readonly JsonSerializerOptions Options = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };
    static readonly Lock Gate = new();

    public static CanvasSettings Load(string home)
    {
        lock (Gate)
        {
            string p = PathIn(home);
            if (SharedFile.Read(p) is not { } text) return new CanvasSettings { CurrentKeyId = KeyIdIn(home) };
            try
            {
                var s = JsonSerializer.Deserialize<CanvasSettings>(text, Options) ?? new CanvasSettings();
                s.CurrentKeyId = KeyIdIn(home);
                return s;
            }
            catch (JsonException)
            {
                return new CanvasSettings { CurrentKeyId = KeyIdIn(home) };
            }
        }
    }

    public void Save(string home)
    {
        lock (Gate)
        {
            Directory.CreateDirectory(home);
            SharedFile.Write(PathIn(home), JsonSerializer.Serialize(this, Options) + "\n");
        }
    }

    /// <summary>Load, change and save in one step, so the web pages and the sync don't lose each other's changes.</summary>
    public static CanvasSettings Update(string home, Action<CanvasSettings> change)
    {
        lock (Gate)
        {
            var s = Load(home);
            change(s);
            s.Save(home);
            return s;
        }
    }

    /// <summary>The course is one the student brings in: chosen, or (in a library from before the choice) linked.</summary>
    public bool IsChosen(long id) => Chosen?.Contains(id.ToString(System.Globalization.CultureInfo.InvariantCulture)) ?? Courses.ContainsValue(id);

    /// <summary>The classes that sync (class → course id): each linked class whose course is chosen.</summary>
    [JsonIgnore] public Dictionary<string, long> Synced => Courses.Where(kv => IsChosen(kv.Value)).ToDictionary(kv => kv.Key, kv => kv.Value);

    /// <summary>A course was linked by hand: it's chosen now too.</summary>
    public void Choose(long id)
    {
        string key = id.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (Chosen is null || Chosen.Contains(key)) return;
        Chosen.Add(key);
    }

    /// <summary>Time for the extension to read Canvas again.</summary>
    public bool Due(DateTimeOffset now) =>
        On && Synced.Count > 0 && (SyncNow || !DateTimeOffset.TryParse(LastSync, out var last) || last.AddMinutes(PollMinutes) <= now);

    /// <summary>"https://school.instructure.com" from whatever was pasted (a course page, no scheme, a trailing slash).
    /// Null when it isn't a web address.</summary>
    public static string? CleanUrl(string typed)
    {
        string t = Py.Strip(typed);
        if (t.Length == 0) return null;
        if (!t.Contains("://", StringComparison.Ordinal)) t = "https://" + t;
        return Uri.TryCreate(t, UriKind.Absolute, out var u) && u.Scheme is "https" or "http" && u.Host.Contains('.')
            ? $"{u.Scheme}://{u.Authority}" : null;
    }

    /// <summary>The extension's own key (canvas_key, made once): it may only hand Canvas pages to this library.</summary>
    public static string ExtensionKey(string home)
    {
        string p = Path.Combine(home, "canvas_key");
        if (!File.Exists(p))
        {
            Directory.CreateDirectory(home);
            Py.WriteText(p, Convert.ToBase64String(RandomNumberGenerator.GetBytes(24)).TrimEnd('=').Replace('+', '-').Replace('/', '_'));
            Py.OwnerOnly(p);
        }
        return Py.Strip(File.ReadAllText(p));
    }

    /// <summary>A short fingerprint of an extension key, written down with each check-in so the key itself never is.</summary>
    public static string KeyId(string key) =>
        key.Length == 0 ? "" : Convert.ToHexStringLower(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(key)))[..16];

    /// <summary>What a check-in that came with the library password (not the extension's key) is written down as.</summary>
    public const string PasswordKeyId = "password";

    /// <summary>The <see cref="KeyId"/> of this library's extension key, without making one; "" when there's none.</summary>
    public static string KeyIdIn(string home)
    {
        string p = Path.Combine(home, "canvas_key");
        try
        {
            return File.Exists(p) ? KeyId(Py.Strip(File.ReadAllText(p))) : "";
        }
        catch (IOException)
        {
            return "";
        }
    }

    public static bool KeyMatches(string home, string? given) =>
        !string.IsNullOrEmpty(given) && File.Exists(Path.Combine(home, "canvas_key"))
        && CryptographicOperations.FixedTimeEquals(System.Text.Encoding.UTF8.GetBytes(given), System.Text.Encoding.UTF8.GetBytes(ExtensionKey(home)));
}

/// <summary>What the course scout found last time: whether it finished, its summary, and how many files it saved.</summary>
public sealed record ScoutReport(bool Ok, string Report, string When, int Files);

/// <summary>A Canvas course as Canvas names it, for matching it to a class and showing it in Settings, with when its
/// term (and the course itself) starts and ends (ISO; "" where Canvas didn't say), for ticking this term's courses.</summary>
public sealed record CourseInfo(string Code, string Name, string Term, string TermStart = "", string TermEnd = "", string CourseStart = "", string CourseEnd = "");

/// <summary>Chrome's extension went from one version to a newer one at <paramref name="At"/> (ISO); dismissed once the
/// student has seen it.</summary>
public sealed record ExtensionUpdate(string From, string To, string At, bool Dismissed);

/// <summary>One browser's extension as it last checked in: when (ISO), its version, its protocol, which key it came
/// with (<see cref="CanvasSettings.KeyId"/>; "" from before keys were written down), and which browser it is ("Chrome",
/// "Firefox"; "" from before 1.6).</summary>
public sealed record ExtensionCopy(string Seen, string Version, int Protocol, string Key = "", string Browser = "");
