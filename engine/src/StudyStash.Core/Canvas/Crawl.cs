using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace StudyStash.Core.Canvas;

/// <summary>A Canvas address for the extension to read, as JSON (the API) or as a file's bytes.</summary>
public sealed record CanvasJob(string Id, string Url, string Kind);

/// <summary>What a finished sync did: the files it changed (class → paths inside the class's folder), what it couldn't
/// read, how each class's listings went, and each class's index before the sync and after it.</summary>
public sealed record CrawlFinished(Dictionary<string, List<string>> Changed, List<string> Errors,
    Dictionary<string, Dictionary<string, string>> Sections, Dictionary<string, CourseIndex?> Before, Dictionary<string, CourseIndex> Indexes);

/// <summary>What the extension read: the HTTP status, Canvas's paging header, and the text or bytes (base64). Status
/// 401 with no text, or <see cref="SignedOut"/>, means Chrome isn't signed in to Canvas; 401 with Canvas's
/// "unauthorized" JSON only means the student can't see that part of the course.</summary>
public sealed record CanvasResult(string Id, int Status, string Link, string Text, string B64, string Error, string Final)
{
    /// <summary>Canvas sent the extension to its sign-in page (newer extensions say so; older ones answer 401).</summary>
    public bool SignedOut { get; init; }
    /// <summary>Canvas's X-Rate-Limit-Remaining: how much of its allowance is left. Null when it didn't say.</summary>
    public double? Rate { get; init; }
    /// <summary>Seconds Canvas asked to wait (Retry-After), when it asked.</summary>
    public double? RetryAfter { get; init; }
    /// <summary>The answer's content type.</summary>
    public string Type { get; init; } = "";

    public static CanvasResult From(JsonObject o)
    {
        static string S(JsonNode? v) => v is JsonValue j && j.GetValueKind() == JsonValueKind.String ? j.GetValue<string>() : "";
        // Header values arrive as text from the extension ("699.8"); numbers are fine too.
        static double? N(JsonNode? v) => v is JsonValue j && j.GetValueKind() == JsonValueKind.Number ? j.GetValue<double>()
            : double.TryParse(S(v), NumberStyles.Float, CultureInfo.InvariantCulture, out double d) ? d : null;
        int status = o["status"] is JsonValue sv && sv.GetValueKind() == JsonValueKind.Number ? (int)sv.GetValue<double>() : 0;
        return new CanvasResult(o["id"] is JsonValue iv && iv.GetValueKind() == JsonValueKind.Number ? iv.ToString() : S(o["id"]),
            status, S(o["link"]), S(o["text"]), S(o["b64"]), S(o["error"]), S(o["final"]))
        {
            SignedOut = o["signed_out"] is JsonValue so && so.GetValueKind() == JsonValueKind.True,
            Rate = N(o["rate"]), RetryAfter = N(o["retry_after"]), Type = S(o["type"]),
        };
    }
}

/// <summary>What one of Canvas's answers means for the sync.</summary>
public enum CanvasAnswer
{
    /// <summary>The data: file it.</summary>
    Ok,
    /// <summary>Chrome isn't signed in to Canvas: the sync stops until it is.</summary>
    SignedOut,
    /// <summary>The student can't see this (a hidden tab, a locked page, something removed): skipped quietly.</summary>
    Hidden,
    /// <summary>Canvas asked to slow down: the sync pauses and asks again.</summary>
    RateLimited,
    /// <summary>Canvas or the network hiccuped: asked again, a few times.</summary>
    Transient,
    /// <summary>It didn't work and won't by asking again.</summary>
    Failed,
}

/// <summary>
/// Mirrors Canvas into each class's folder, one fetch at a time, through the Chrome extension. The library hands the
/// extension jobs; the extension fetches them with the person's own Canvas session and hands the answers back;
/// <see cref="Handle"/> files them and queues what follows (next pages, file bodies). In each class's folder:
/// <code>
/// Canvas/assignments/&lt;name&gt;/spec.md      instructions, due date, points, rubric
/// Canvas/assignments/&lt;name&gt;/feedback.md  your submission: status, score, rubric marks, comments
/// Canvas/assignments/&lt;name&gt;/submission/  the files you turned in
/// Canvas/modules/&lt;NN Module&gt;/             module files, and pages as Markdown
/// Canvas/modules.md                      the module outline, linked to the copies
/// Canvas/announcements.md                announcements, newest first
/// </code>
/// It keeps its queue in crawl.json, so a restart carries on where it was. Each class's listings (assignments,
/// submissions, modules, announcements) are sections: read completely (<c>ok</c>), not shown to the student
/// (<c>hidden</c>) or <c>failed</c>, so a sync that couldn't read something never passes that off as "gone".
/// </summary>
public sealed partial class Crawl
{
    public const long MaxBytes = 40L * 1024 * 1024;
    const double InflightSeconds = 600;
    /// <summary>Asked this many times, a server error or a dropped connection counts as failed.</summary>
    public const int MaxTries = 3;
    /// <summary>The first pause when Canvas rate-limits, doubling each time it happens again, up to <see cref="MaxBackoff"/>.</summary>
    public static readonly TimeSpan FirstBackoff = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan MaxBackoff = TimeSpan.FromMinutes(10);

    /// <summary>The listings each class is read through; each is a section of the sync. <c>planner</c> is one job for
    /// every linked class together (Canvas's planner takes them all at once), but still marked per class.</summary>
    public static readonly IReadOnlyList<string> Listings =
        ["assignments", "submissions", "modules", "announcements", "files", "quizzes", "discussions", "planner"];
    // Listings whose pages only make sense together (an outline, a newest-first list): filed once the last page is in.
    static readonly HashSet<string> Whole = ["modules", "announcements"];

    readonly string home;
    readonly Func<string, string> classDir;
    readonly Func<DateTimeOffset> clock;
    readonly Func<TimeZoneInfo> zone;
    readonly Lock gate = new();
    readonly JsonObject data;
    // Each class's index as this sync has read it so far (home/canvas/<class>.sync.json), and the ones to save.
    readonly Dictionary<string, CourseIndex> staging = [];
    readonly HashSet<string> dirty = [];
    // Pages, the front page and the syllabus are rendered when the sync finishes (like spec.md/feedback.md), so a
    // file they link can point at where it really landed: their Markdown (file links not yet resolved) waits here.
    // This sync's memory only; lost on a restart, but the next sync reads the same pages again.
    readonly Dictionary<string, Dictionary<string, PendingPage>> pendingPages = [];
    readonly Dictionary<string, PendingPage> pendingFrontPage = [];
    readonly Dictionary<string, PendingPage> pendingSyllabus = [];
    // A module's Page items (by slug), so the pages listing outside modules doesn't save them a second time.
    readonly Dictionary<string, HashSet<string>> modulePageSlugs = [];
    // An assignment the planner says is marked done this sync, and when (class -> assignment id -> Canvas's UTC ISO,
    // or null when it didn't say). This sync's memory only; ApplyPlanner bakes it into the index at Finish.
    readonly Dictionary<string, Dictionary<long, string?>> markedDone = [];

    /// <summary>A page's Markdown before its file links are resolved, and where it belongs.</summary>
    sealed record PendingPage(string Title, string Body, string Dir, string HtmlUrl, string? UpdatedAt);

    /// <param name="classDir">A class's folder in the library (created if missing).</param>
    public Crawl(string home, Func<string, string> classDir) : this(home, classDir, () => DateTimeOffset.Now)
    {
    }

    /// <param name="clock">What time it is (tests set it).</param>
    public Crawl(string home, Func<string, string> classDir, Func<DateTimeOffset> clock) : this(home, classDir, clock, () => TimeZoneInfo.Local)
    {
    }

    /// <param name="zone">The time zone the Markdown gives times in (tests set it).</param>
    public Crawl(string home, Func<string, string> classDir, Func<DateTimeOffset> clock, Func<TimeZoneInfo> zone)
    {
        this.home = home;
        this.classDir = classDir;
        this.clock = clock;
        this.zone = zone;
        data = Read() ?? [];
        data.Remove("assignments"); // a crawl.json from before the index kept every assignment's JSON here
        if (data["jobs"] is not JsonArray) data["jobs"] = new JsonArray();
        foreach (string key in new[] { "inflight", "changed", "manifest", "sections", "pages", "courses" })
            if (data[key] is not JsonObject) data[key] = new JsonObject();
        if (data["errors"] is not JsonArray) data["errors"] = new JsonArray();
    }

    string StatePath => Path.Combine(home, "crawl.json");

    JsonObject? Read()
    {
        try
        {
            return File.Exists(StatePath) ? JsonNode.Parse(File.ReadAllText(StatePath)) as JsonObject : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    void Save()
    {
        string tmp = StatePath + ".tmp";
        File.WriteAllText(tmp, data.ToJsonString());
        File.Move(tmp, StatePath, overwrite: true);
    }

    JsonArray Jobs => (JsonArray)data["jobs"]!;
    JsonObject Inflight => (JsonObject)data["inflight"]!;
    JsonObject Manifest => (JsonObject)data["manifest"]!;
    JsonObject Changed => (JsonObject)data["changed"]!;
    JsonArray Errors => (JsonArray)data["errors"]!;
    JsonObject SectionStates => (JsonObject)data["sections"]!;
    JsonObject PagesSoFar => (JsonObject)data["pages"]!;
    // Canvas course id (as a string) -> class name, so the one cross-class planner job can tell whose to-do is whose.
    JsonObject Courses => (JsonObject)data["courses"]!;

    static bool Flag(JsonNode? v) => v is JsonValue j && j.GetValueKind() == JsonValueKind.True;
    static string S(JsonNode? v) => v is JsonValue j && j.GetValueKind() == JsonValueKind.String ? j.GetValue<string>() : "";
    // Numbers parsed from JSON and numbers set here (ints) read the same way.
    static double? D(JsonNode? v) => v is JsonValue j && j.GetValueKind() == JsonValueKind.Number
        ? double.Parse(j.ToJsonString(), System.Globalization.CultureInfo.InvariantCulture) : null;

    /// <summary>Now, in seconds since 1970 (how crawl.json keeps times).</summary>
    double Seconds() => clock().ToUnixTimeMilliseconds() / 1000.0;

    /// <summary>A sync is running.</summary>
    public bool Active { get { lock (gate) return Flag(data["active"]); } }
    /// <summary>A sync finished and its results are waiting for <see cref="TakeFinished"/>.</summary>
    public bool Ready { get { lock (gate) return Flag(data["ready"]); } }
    /// <summary>Jobs still to do, and being done.</summary>
    public (int Waiting, int Inflight) Left { get { lock (gate) return (Jobs.Count, Inflight.Count); } }

    /// <summary>Canvas asked to slow down: nothing is handed out before this. Null when the sync isn't paused.</summary>
    public DateTimeOffset? PausedUntil
    {
        get
        {
            lock (gate)
                return D(data["pause_until"]) is double until && until > Seconds()
                    ? DateTimeOffset.FromUnixTimeMilliseconds((long)(until * 1000)) : null;
        }
    }

    /// <summary>How each class's listings went in this sync (or the last one): class → listing → reading | ok | hidden | failed.</summary>
    public Dictionary<string, Dictionary<string, string>> Sections { get { lock (gate) return SectionsNow(); } }

    Dictionary<string, Dictionary<string, string>> SectionsNow() =>
        SectionStates.ToDictionary(kv => kv.Key, kv => (kv.Value as JsonObject ?? []).ToDictionary(x => x.Key, x => S(x.Value)));

    /// <summary>The extension was sent to Canvas's sign-in page: the sync stopped. Reading clears it.</summary>
    public bool TakeSignedOut()
    {
        lock (gate)
        {
            if (!Flag(data["signed_out"])) return false;
            data["signed_out"] = false;
            Save();
            return true;
        }
    }

    /// <summary>A class's index as this sync has read it so far.</summary>
    CourseIndex Staged(string cls)
    {
        if (!staging.TryGetValue(cls, out var index))
            staging[cls] = index = CourseIndex.LoadStaged(home, cls) ?? new CourseIndex { Class = cls, Staged = new SyncParts() };
        index.Staged ??= new SyncParts();
        dirty.Add(cls);
        return index;
    }

    void Add(string url, string kind, JsonObject tag)
    {
        int seq = (int)(D(data["seq"]) ?? 0) + 1;
        data["seq"] = seq;
        Jobs.Add(new JsonObject { ["id"] = seq.ToString(CultureInfo.InvariantCulture), ["url"] = url, ["kind"] = kind, ["tag"] = tag });
    }

    static JsonObject Tag(string type, string cls, params (string Key, string Value)[] more)
    {
        var t = new JsonObject { ["type"] = type, ["class"] = cls };
        foreach (var (k, v) in more) t[k] = v;
        return t;
    }

    /// <summary>A listing moves on from <c>reading</c> once: to ok, hidden or failed. What it became first stays.
    /// Once modules ends (however it went), the syllabus, the pages outside modules and the front page are asked
    /// for too: modules must be read first so a module's own page isn't saved again outside it.</summary>
    void Section(string cls, string listing, string state)
    {
        if (!Listings.Contains(listing)) return;
        if (SectionStates[cls] is not JsonObject mine) SectionStates[cls] = mine = [];
        bool wasReading = S(mine[listing]) is "" or "reading";
        if (wasReading) mine[listing] = state;
        if (state != "ok" && PagesSoFar[cls] is JsonObject kept) kept.Remove(listing);
        if (wasReading && listing == "modules" && state != "reading") AfterModules(cls);
    }

    /// <summary>Like <see cref="Section"/>, but for an answer whose job isn't one class's: the planner reads every
    /// linked class in a single request, so its answer marks that listing for all of them at once.</summary>
    void MarkSection(string cls, string listing, string state)
    {
        if (listing != "planner") { Section(cls, listing, state); return; }
        foreach (string c in SectionStates.Select(kv => kv.Key).ToList()) Section(c, listing, state);
    }

    /// <summary>Ask for what only makes sense once modules are known: the syllabus and course info, the pages
    /// outside modules, and the front page.</summary>
    void AfterModules(string cls)
    {
        string canvasUrl = S(data["base"]);
        long id = Staged(cls).CourseId;
        string api = $"{canvasUrl}/api/v1/courses/{id}";
        Add($"{api}?include[]=syllabus_body&include[]=term", "json", Tag("course", cls));
        Add($"{api}/pages?per_page=100&sort=title", "json", Tag("pages", cls));
        Add($"{api}/front_page", "json", Tag("front_page", cls));
        // The Files area: folders first (so each file's path is known), then the files themselves (FilesStage).
        Add($"{api}/folders?per_page=100", "json", Tag("files", cls, ("stage", "folders")));
    }

    /// <summary>Start a sync of these classes (class → Canvas course id). False if one is already running.</summary>
    public bool Start(string canvasUrl, IReadOnlyDictionary<string, long> courses)
    {
        lock (gate)
        {
            if (Flag(data["active"])) return false;
            data["jobs"] = new JsonArray();
            data["inflight"] = new JsonObject();
            data["changed"] = new JsonObject();
            data["errors"] = new JsonArray();
            data["sections"] = new JsonObject();
            data["pages"] = new JsonObject();
            data["courses"] = new JsonObject();
            data["active"] = true;
            data["ready"] = false;
            data["signed_out"] = false;
            data["started"] = clock().ToString("o", CultureInfo.InvariantCulture);
            data["base"] = canvasUrl;
            CourseIndex.DropStaged(home);
            staging.Clear();
            dirty.Clear();
            pendingPages.Clear();
            pendingFrontPage.Clear();
            pendingSyllabus.Clear();
            modulePageSlugs.Clear();
            markedDone.Clear();
            foreach (var (cls, id) in courses)
            {
                staging[cls] = new CourseIndex { Class = cls, CourseId = id, Staged = new SyncParts() };
                staging[cls].SaveStaged(home);
                Courses[id.ToString(CultureInfo.InvariantCulture)] = cls;
                string api = $"{canvasUrl}/api/v1/courses/{id}";
                Add($"{api}/assignments?include[]=submission&per_page=100&order_by=due_at", "json", Tag("assignments", cls));
                // Only the student's own: every attempt (submission_history), the grader's comments and rubric marks.
                Add($"{api}/students/submissions?student_ids[]=self&include[]=submission_comments&include[]=rubric_assessment&include[]=assignment"
                    + "&include[]=submission_history&per_page=100", "json", Tag("submissions", cls));
                Add($"{api}/modules?include[]=items&include[]=content_details&per_page=100", "json", Tag("modules", cls));
                // Not /api/v1/announcements: without an end_date it stops 28 days after its start_date. The course's
                // own list has the whole term, and whether the student has read each one.
                Add($"{api}/discussion_topics?only_announcements=true&per_page=100", "json", Tag("announcements", cls));
                // Real (non-announcement) discussion topics: never their /entries, /view or /entry_list.
                Add($"{api}/discussion_topics?per_page=100", "json", Tag("discussions", cls));
                // Never a quiz's /questions, /submissions or /quiz_submissions.
                Add($"{api}/quizzes?per_page=100", "json", Tag("quizzes", cls));
                foreach (string listing in Listings) Section(cls, listing, "reading");
            }
            // One request for every linked class's to-dos (Canvas's planner takes them all at once); Planner() sorts
            // each item to its own class by course id, and marks "planner" for the whole class list once it's read.
            if (courses.Count > 0)
            {
                var today = clock().UtcDateTime.Date;
                string codes = string.Join("&", courses.Values.Select(id => $"context_codes[]=course_{id}"));
                Add($"{canvasUrl}/api/v1/planner/items?start_date={today.AddDays(-28):yyyy-MM-dd}&end_date={today.AddDays(120):yyyy-MM-dd}&{codes}&per_page=100",
                    "json", Tag("planner", ""));
            }
            Save();
            return true;
        }
    }

    /// <summary>Up to <paramref name="n"/> jobs for the extension; none while Canvas has asked to slow down. Jobs it
    /// took and never answered (Chrome closed) go back in the queue after ten minutes.</summary>
    public List<CanvasJob> Next(int n = 6)
    {
        lock (gate)
        {
            double t = Seconds();
            bool changed = false;
            foreach (var (id, rec) in Inflight.ToList())
                if (t - (D(rec?["t"]) ?? 0) > InflightSeconds)
                {
                    Jobs.Add(rec!["job"]!.DeepClone());
                    Inflight.Remove(id);
                    changed = true;
                }
            var take = D(data["pause_until"]) is double until && until > t ? [] : Jobs.Take(n).Select(j => j!.AsObject()).ToList();
            foreach (var j in take)
            {
                Jobs.Remove(j);
                Inflight[S(j["id"])] = new JsonObject { ["job"] = j, ["t"] = t };
            }
            if (take.Count > 0 || changed) Save();
            return take.Select(j => new CanvasJob(S(j["id"]), S(j["url"]), S(j["kind"]))).ToList();
        }
    }

    /// <summary>The extension started afresh (installed, reloaded, or asked to sync): whatever it had taken is lost with
    /// its old copy, so it goes back in the queue now, first out (ahead of anything nobody's tried yet), instead of
    /// waiting ten minutes.</summary>
    public void Requeue()
    {
        lock (gate)
        {
            if (Inflight.Count == 0) return;
            var lost = Inflight.Select(kv => kv.Value!["job"]!.DeepClone()).ToList(); // Inflight keeps the order Next() handed them out in
            foreach (string id in Inflight.Select(kv => kv.Key).ToList()) Inflight.Remove(id);
            for (int i = lost.Count - 1; i >= 0; i--) Jobs.Insert(0, lost[i]);
            Save();
        }
    }

    [GeneratedRegex("\"status\"\\s*:\\s*\"unauthorized\"|not authorized", RegexOptions.IgnoreCase)]
    private static partial Regex NotAuthorized();

    /// <summary>
    /// What an answer means. Canvas says 401 for two different things: "unauthenticated" (nobody is signed in, and
    /// the extension also reports a bounce to the sign-in page as 401) and "unauthorized" (signed in, but this
    /// student can't see that tab). Only the first stops the sync. "403 Forbidden (Rate Limit Exceeded)" asks the
    /// sync to slow down; any other 403 or 404 is something the student can't see.
    /// </summary>
    public static CanvasAnswer Classify(CanvasResult r)
    {
        if (r.SignedOut) return CanvasAnswer.SignedOut;
        string body = BodyText(r);
        if (r.Status == 429 || r.Status == 403 && (body.Contains("Rate Limit Exceeded", StringComparison.OrdinalIgnoreCase) || r.Rate is <= 0))
            return CanvasAnswer.RateLimited;
        if (r.Status == 401) return NotAuthorized().IsMatch(body) ? CanvasAnswer.Hidden : CanvasAnswer.SignedOut;
        if (r.Status is 403 or 404) return CanvasAnswer.Hidden;
        if (r.Status >= 500) return CanvasAnswer.Transient;
        if (r.Status == 0) return r.Error.StartsWith("refused", StringComparison.Ordinal) || r.Error == "too big" ? CanvasAnswer.Failed : CanvasAnswer.Transient;
        return r.Error.Length > 0 || r.Status >= 400 ? CanvasAnswer.Failed : CanvasAnswer.Ok;
    }

    /// <summary>An answer's text: for a file, its bytes when they're short enough to be an error message.</summary>
    static string BodyText(CanvasResult r)
    {
        if (r.Text.Length > 0 || r.B64.Length is 0 or > 64 * 1024) return r.Text;
        try
        {
            return Encoding.UTF8.GetString(Convert.FromBase64String(r.B64));
        }
        catch (FormatException)
        {
            return "";
        }
    }

    static string Why(CanvasResult r) => r.Error.Length > 0 ? r.Error : r.Status > 0 ? $"Canvas answered {r.Status}" : "no answer";

    /// <summary>
    /// Canvas asked to slow down: nothing goes out for a while, twice as long each time a job sent after the last
    /// pause is refused again. The rest of the burst that caused a pause (sent before it) doesn't lengthen it.
    /// </summary>
    void Pause(double handedOut, double? retryAfter)
    {
        double until = D(data["pause_until"]) ?? 0, now = Seconds();
        if (handedOut >= until)
        {
            double backoff = D(data["backoff"]) is double b && b > 0 ? Math.Min(b * 2, MaxBackoff.TotalSeconds) : FirstBackoff.TotalSeconds;
            data["backoff"] = backoff;
            until = now + backoff;
        }
        data["pause_until"] = Math.Max(until, now + (retryAfter ?? 0));
    }

    /// <summary>An answer came back fine: Canvas is calm again once a job sent after the last pause gets through.</summary>
    void Calm(double handedOut, double? rate)
    {
        double until = D(data["pause_until"]) ?? 0;
        // Canvas says its allowance is used up: this answer is good, the next ones wouldn't be.
        if (rate is <= 0) data["pause_until"] = Math.Max(until, Seconds() + FirstBackoff.TotalSeconds);
        else if (handedOut >= until) data["backoff"] = 0;
    }

    /// <summary>A file's bytes are wanted no more (its want was signed out, hidden, or failed for good): the
    /// "someone already asked" marker <see cref="WantFile"/> left comes off, so a later sync asks again instead of
    /// believing forever that the file is on its way.</summary>
    void Unqueue(JsonObject tag)
    {
        if (S(tag["type"]) == "file_bytes" && S(tag["key"]) is { Length: > 0 } key && S(Manifest[key]).StartsWith("queued:", StringComparison.Ordinal))
            Manifest.Remove(key);
    }

    /// <summary>File one answer. False when it isn't one of this crawl's jobs.</summary>
    public bool Handle(CanvasResult r)
    {
        lock (gate)
        {
            if (Inflight[r.Id] is not JsonObject rec) return false;
            Inflight.Remove(r.Id);
            var job = rec["job"]!.AsObject();
            var tag = job["tag"]!.AsObject();
            string cls = S(tag["class"]), type = S(tag["type"]);
            try
            {
                switch (Classify(r))
                {
                    case CanvasAnswer.SignedOut:
                        Unqueue(tag);
                        foreach (var (_, inflight) in Inflight) Unqueue(inflight!["job"]!["tag"]!.AsObject());
                        foreach (var waiting in Jobs) Unqueue(waiting!["tag"]!.AsObject());
                        data["signed_out"] = true;
                        data["jobs"] = new JsonArray();
                        data["inflight"] = new JsonObject();
                        break;
                    case CanvasAnswer.Hidden:
                        Unqueue(tag);
                        MarkSection(cls, type, "hidden");
                        break;
                    case CanvasAnswer.RateLimited:
                        Jobs.Insert(0, job.DeepClone()); // first out when the pause is over: nothing is lost
                        Pause(D(rec["t"]) ?? 0, r.RetryAfter);
                        break;
                    case CanvasAnswer.Transient when (D(tag["tries"]) ?? 0) + 1 < MaxTries:
                        var again = job.DeepClone().AsObject();
                        again["tag"]!["tries"] = (int)(D(tag["tries"]) ?? 0) + 1;
                        Jobs.Add(again);
                        break;
                    case CanvasAnswer.Transient or CanvasAnswer.Failed:
                        Unqueue(tag);
                        Errors.Add($"{(cls.Length > 0 ? cls + " " : "")}{type}: {Why(r)}");
                        MarkSection(cls, type, "failed");
                        break;
                    default:
                        Calm(D(rec["t"]) ?? 0, r.Rate);
                        Dispatch(job, tag, r);
                        break;
                }
            }
            catch (Exception e) when (e is JsonException or IOException or InvalidOperationException or FormatException or UnauthorizedAccessException)
            {
                Unqueue(tag);
                Errors.Add($"{(cls.Length > 0 ? cls + " " : "")}{type}: {e.Message}"); // one odd item never stops the sync
                MarkSection(cls, type, "failed");
            }
            if (Flag(data["active"]) && Jobs.Count == 0 && Inflight.Count == 0)
            {
                data["active"] = false;
                data["ready"] = !Flag(data["signed_out"]);
            }
            SaveStaged();
            Save();
            return true;
        }
    }

    /// <summary>Keep what this sync has read of each class that changed, so a restart carries on.</summary>
    void SaveStaged()
    {
        foreach (string cls in dirty)
            if (staging.TryGetValue(cls, out var index)) index.SaveStaged(home);
        dirty.Clear();
    }

    /// <summary>
    /// A finished sync's results, once. Each class's index is promoted (<see cref="CourseIndex.Promoted"/>) and its
    /// Markdown written from it; then: the files that changed (class → paths inside the class folder), what couldn't
    /// be read, how each class's listings went, and each class's index before and after. Null while it's still running.
    /// </summary>
    public CrawlFinished? TakeFinished()
    {
        lock (gate)
        {
            if (!Flag(data["ready"])) return null;
            var sections = SectionsNow();
            var before = new Dictionary<string, CourseIndex?>();
            var after = new Dictionary<string, CourseIndex>();
            string at = clock().ToString("o", CultureInfo.InvariantCulture);
            foreach (var (cls, states) in sections)
            {
                try
                {
                    before[cls] = CourseIndex.Load(home, cls);
                    after[cls] = CourseIndex.Promote(home, cls, states, at, index => { Forget(cls, index); FinishPages(cls, index); ApplyPlanner(cls, index, before[cls]); });
                    Render(cls, after[cls]);
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    Errors.Add($"{cls}: {e.Message}");
                }
            }
            staging.Clear();
            dirty.Clear();
            var changed = Changed.ToDictionary(kv => kv.Key, kv => (kv.Value as JsonArray ?? []).Select(S).Distinct().ToList());
            var errors = Errors.Select(S).ToList();
            data["ready"] = false;
            data["changed"] = new JsonObject();
            data["pages"] = new JsonObject();
            Save();
            return new CrawlFinished(changed, errors, sections, before, after);
        }
    }

    /// <summary>A file the index says was saved but never arrived (Canvas refused it) isn't there: the next sync asks again.</summary>
    void Forget(string cls, CourseIndex index)
    {
        string root = classDir(cls);
        void Check(FileRef f)
        {
            if (f.Local is { } local && !File.Exists(Path.Combine(root, local))) f.Local = null;
        }
        foreach (var a in index.Assignments)
        {
            a.InstructionFiles.ForEach(Check);
            if (a.Submission is not { } s) continue;
            s.Files.ForEach(Check);
            foreach (var t in s.Attempts) t.Files.ForEach(Check);
            foreach (var c in s.Comments) c.Files.ForEach(Check);
        }
    }

    /// <summary>Each assignment's spec.md and feedback.md, from the class's index (a spec written by hand is left
    /// alone), and modules.md once the course is known to have any module (never written from nothing known, so a
    /// class whose first sync couldn't read its modules gets no outline instead of an empty one).</summary>
    void Render(string cls, CourseIndex index)
    {
        var tz = zone();
        var now = clock();
        foreach (var a in index.Assignments)
        {
            if (a.Folder.Length == 0) continue;
            string dir = Path.Combine(classDir(cls), a.Folder);
            var quiz = a.QuizId is long qid ? index.Quizzes.FirstOrDefault(q => q.Id == qid) : null;
            var discussion = a.DiscussionTopicId is long did ? index.Discussions.FirstOrDefault(d => d.Id == did) : null;
            if (SpecHead(dir) is not { } head || head.Contains(Generated, StringComparison.Ordinal))
                Write(cls, Path.Combine(dir, "spec.md"), CanvasMarkdown.Spec(cls, a, tz, quiz, discussion));
            if (CanvasMarkdown.HasFeedback(a)) Write(cls, Path.Combine(dir, "feedback.md"), CanvasMarkdown.Feedback(cls, a, tz, now));
        }
        if (index.Modules.Count > 0) Write(cls, Path.Combine(CanvasDir(cls), "modules.md"), CanvasMarkdown.Modules(cls, index));
        if (index.Announcements.Count > 0) Write(cls, Path.Combine(CanvasDir(cls), "announcements.md"), CanvasMarkdown.Announcements(cls, index, tz));
        // A quiz or discussion folded into its own assignment's spec.md isn't written again on its own.
        var foldedQuizzes = index.Assignments.Where(a => a.QuizId is not null).Select(a => a.QuizId!.Value).ToHashSet();
        foreach (var q in index.Quizzes.Where(q => !foldedQuizzes.Contains(q.Id)))
            Write(cls, Path.Combine(CanvasDir(cls), "quizzes", SafeName(q.Title) + ".md"), CanvasMarkdown.Quiz(q));
        var foldedDiscussions = index.Assignments.Where(a => a.DiscussionTopicId is not null).Select(a => a.DiscussionTopicId!.Value).ToHashSet();
        foreach (var d in index.Discussions.Where(d => !foldedDiscussions.Contains(d.Id)))
            Write(cls, Path.Combine(CanvasDir(cls), "discussions", SafeName(d.Title) + ".md"), CanvasMarkdown.Discussion(d));
    }

    // --- what each answer becomes ---------------------------------------------------------------------------------

    [GeneratedRegex("<([^>]+)>;\\s*rel=\"next\"")]
    private static partial Regex NextLink();

    void Dispatch(JsonObject job, JsonObject tag, CanvasResult r)
    {
        string type = S(tag["type"]);
        if (S(job["kind"]) == "bytes")
        {
            FileBytes(tag, Convert.FromBase64String(r.B64));
            return;
        }
        var body = JsonNode.Parse(r.Text.Length > 0 ? r.Text : "null");
        bool more = false;
        if (body is JsonArray && NextLink().Match(r.Link) is { Success: true } m)
        {
            var next = (JsonObject)tag.DeepClone();
            next.Remove("tries"); // a new page gets its own tries
            Add(m.Groups[1].Value, "json", next);
            more = true;
        }
        string cls = S(tag["class"]);
        if (type == "files") { FilesStage(cls, tag, body as JsonArray ?? [], more); return; }
        switch (type)
        {
            case "assignments": AssignmentsPage(cls, body as JsonArray ?? []); break;
            case "submissions": Submissions(cls, body as JsonArray ?? []); break;
            case "quizzes": QuizzesPage(cls, body as JsonArray ?? []); break;
            case "discussions": DiscussionsPage(cls, body as JsonArray ?? []); break;
            case "planner": Planner(body as JsonArray ?? []); break;
            case var whole when Whole.Contains(whole):
                if (S(SectionStates[cls]?[type]) is "failed" or "hidden") break; // an earlier page went wrong: never file part of it
                if (PagesSoFar[cls] is not JsonObject mine) PagesSoFar[cls] = mine = [];
                if (mine[type] is not JsonArray all) mine[type] = all = [];
                foreach (var item in body as JsonArray ?? []) all.Add(item?.DeepClone());
                if (more) break;
                mine.Remove(type);
                if (type == "modules") Modules(cls, all);
                else Announcements(cls, all);
                break;
            case var mi when mi.StartsWith("module_items:", StringComparison.Ordinal):
                if (PagesSoFar[cls] is not JsonObject mpage) PagesSoFar[cls] = mpage = [];
                if (mpage[mi] is not JsonArray macc) mpage[mi] = macc = [];
                foreach (var item in body as JsonArray ?? []) macc.Add(item?.DeepClone());
                if (more) break;
                mpage.Remove(mi);
                long modId = long.Parse(mi.Split(':')[1], CultureInfo.InvariantCulture);
                if (Staged(cls).Modules.FirstOrDefault(m => m.Id == modId) is { } modInfo)
                    modInfo.Items = BuildItems(cls, modId, ModuleFolderAbs(cls, modId), macc);
                break;
            case "file_meta": if (body is JsonObject meta) Attach(cls, tag, WantFile(cls, meta, S(tag["dir"]))); break;
            case "page": if (body is JsonObject page) Page(cls, page, tag); break;
            case "course": if (body is JsonObject course) Course(cls, course); break;
            case "pages": PagesListing(cls, body as JsonArray ?? []); break;
            case "front_page": if (body is JsonObject front) FrontPage(cls, front); break;
            case "outside_page": if (body is JsonObject op) OutsidePage(cls, op, S(tag["slug"])); break;
        }
        if (!more) MarkSection(cls, type, "ok");
    }

    string ModuleFolderAbs(string cls, long moduleId) =>
        S(Manifest[$"moddir:{cls}:{moduleId}"]) is { Length: > 0 } rel
            ? Path.Combine(classDir(cls), rel.Replace('/', Path.DirectorySeparatorChar))
            : Path.Combine(CanvasDir(cls), "modules");

    string CanvasDir(string cls) => Path.Combine(classDir(cls), "Canvas");

    /// <summary>Write a file if it's new or different, and note it as changed.</summary>
    void Write(string cls, string path, byte[] content)
    {
        Directory.CreateDirectory(Py.Parent(path));
        if (File.Exists(path) && File.ReadAllBytes(path).AsSpan().SequenceEqual(content)) return;
        File.WriteAllBytes(path, content);
        if (Changed[cls] is not JsonArray list) Changed[cls] = list = [];
        list.Add(Path.GetRelativePath(classDir(cls), path).Replace('\\', '/'));
    }

    void Write(string cls, string path, string text) => Write(cls, path, new UTF8Encoding(false).GetBytes(text));

    [GeneratedRegex("[\\\\/:*?\"<>|\\x00-\\x1f]+")]
    private static partial Regex Unsafe();

    /// <summary>A name that works as a file name everywhere.</summary>
    public static string SafeName(string name, int limit = 120)
    {
        string n = Unsafe().Replace(Py.Strip(name), "-").Trim(' ', '.', '-');
        if (n.Length == 0) n = "untitled";
        return n[..Math.Min(n.Length, limit)].TrimEnd(' ', '.', '-');
    }

    /// <summary>Where an assignment's spec and feedback are, inside its class's folder ("Canvas/assignments/Lab 1"), once a
    /// sync has written them.</summary>
    public string? AssignmentFolder(string cls, long id)
    {
        lock (gate) return S(Manifest[$"asgdir:{cls}:{id}"]) is { Length: > 0 } rel ? rel.Replace('\\', '/') : null;
    }

    /// <summary>The start of a spec.md, or null when there isn't one.</summary>
    static string? SpecHead(string dir)
    {
        string spec = Path.Combine(dir, "spec.md");
        if (!File.Exists(spec)) return null;
        using var reader = new StreamReader(spec);
        var buf = new char[1500];
        return new string(buf, 0, reader.ReadBlock(buf, 0, buf.Length));
    }

    /// <summary>A spec's front matter, from its opening "---" to the closing one; "" when it has none.</summary>
    static string FrontMatter(string head)
    {
        if (!head.StartsWith("---", StringComparison.Ordinal)) return "";
        int end = head.IndexOf("\n---", 3, StringComparison.Ordinal);
        return end < 0 ? head : head[..end];
    }

    /// <summary>An assignment's folder: the one already made for its Canvas id, one whose spec.md names it, else a new
    /// one named after it. A spec the sync wrote names its assignment only by the canvas_id in its front matter; one
    /// written by hand may name it by its Canvas id or its Canvas address anywhere.</summary>
    string AssignmentDir(string cls, JsonObject a)
    {
        string key = $"asgdir:{cls}:{D(a["id"]):0}";
        if (S(Manifest[key]) is { Length: > 0 } known) return Path.Combine(classDir(cls), known);
        string root = Path.Combine(CanvasDir(cls), "assignments"), id = Num(D(a["id"])), url = S(a["html_url"]);
        var byId = new Regex($"(?m)^canvas_id: {id}\\r?$");
        // ".../assignments/900" must not claim the spec of ".../assignments/9001".
        var byHand = new Regex($"canvas_id: {id}(?![0-9])" + (url.Length > 0 ? $"|{Regex.Escape(url)}(?![0-9])" : ""));
        // Instructions the sync copied into a spec link other assignments by their full Canvas address, and a link
        // must not hand the linking assignment's folder to the one it links.
        bool Names(string head) => head.Contains(Generated, StringComparison.Ordinal) ? byId.IsMatch(FrontMatter(head)) : byHand.IsMatch(head);
        string? dir = Directory.Exists(root)
            ? Directory.EnumerateDirectories(root).Order(StringComparer.Ordinal).FirstOrDefault(d => SpecHead(d) is { } head && Names(head))
            : null;
        if (dir is null)
        {
            // Two assignments with the same name (or one renamed on Canvas) get their own folders. Specs are written when
            // the sync finishes, so a folder another assignment already has is taken too.
            string mine = $"asgdir:{cls}:";
            var taken = Manifest.Where(kv => kv.Key.StartsWith(mine, StringComparison.Ordinal)).Select(kv => S(kv.Value)).ToHashSet();
            bool Taken(string d) => File.Exists(Path.Combine(d, "spec.md")) || taken.Contains(Path.GetRelativePath(classDir(cls), d));
            dir = Path.Combine(root, SafeName(S(a["name"]), 80));
            for (int i = 2; Taken(dir); i++) dir = Path.Combine(root, SafeName(S(a["name"]), 76) + $" {i}");
        }
        Manifest[key] = Path.GetRelativePath(classDir(cls), dir);
        return dir;
    }

    /// <summary>The marker in every spec.md the sync writes; one without it was written by hand and is left alone.</summary>
    public const string Generated = "generated_by: study-stash";

    /// <summary>A path inside the class's folder, with / ("Canvas/assignments/Lab 1/spec.md").</summary>
    string Rel(string cls, string path) => Path.GetRelativePath(classDir(cls), path).Replace('\\', '/');

    void AssignmentsPage(string cls, JsonArray list)
    {
        var index = Staged(cls);
        foreach (var a in list.OfType<JsonObject>().Where(Assignments.Published))
        {
            string dir = AssignmentDir(cls, a);
            var (info, files) = AssignmentInfo.From(a, Rel(cls, dir), BuildContext(cls, index.CourseId, Rel(cls, dir)));
            int at = index.Assignments.FindIndex(x => x.Id == info.Id);
            if (at >= 0) index.Assignments[at] = info;
            else index.Assignments.Add(info);
            QueueFileLinks(cls, files, Path.Combine(dir, "files"), "assignment", Num(info.Id));
        }
    }

    // --- Canvas file links inside HTML (instructions, pages, the syllabus): AngleSharp/ReverseMarkdown does the
    // conversion (HtmlText); Crawl asks for the files it finds and remembers where each landed. --------------------

    /// <summary>Everything <see cref="HtmlText.Convert"/> needs for one document: absolute links, and any Canvas file
    /// already wanted rewritten to its local copy, relative to <paramref name="fromFolder"/> (the document's own
    /// folder, inside the class, with /).</summary>
    HtmlContext BuildContext(string cls, long courseId, string fromFolder) =>
        new(S(data["base"]), courseId, id => LocalOf(cls, id) is { } p ? CanvasMarkdown.RelativeLink(fromFolder, p) : null);

    /// <summary>Where a Canvas file id has already landed, inside the class's folder ("Canvas/assignments/Lab 1/files/x.pdf"), or null.</summary>
    string? LocalOf(string cls, long fileId) => S(Manifest[$"fileloc:{cls}:{fileId}"]) is { Length: > 0 } p ? p : null;

    /// <summary>Ask for the metadata of any linked file this class doesn't already have a copy of.</summary>
    void QueueFileLinks(string cls, IEnumerable<CanvasFileLink> links, string destDir, string owner = "", string ownerId = "")
    {
        foreach (var link in links)
            if (LocalOf(cls, link.FileId) is null)
                Add(link.ApiUrl, "json", owner.Length > 0 ? Tag("file_meta", cls, ("dir", destDir), ("owner", owner), ("ownerId", ownerId)) : Tag("file_meta", cls, ("dir", destDir)));
    }

    /// <summary>A file wanted for an assignment's instructions is remembered on that assignment too (so
    /// <see cref="Forget"/> notices if it never arrives).</summary>
    void Attach(string cls, JsonObject tag, FileRef f)
    {
        string owner = S(tag["owner"]);
        if (owner == "assignment" && long.TryParse(S(tag["ownerId"]), out long id))
        {
            var index = Staged(cls);
            (index.Assignments.FirstOrDefault(x => x.Id == id) ?? index.Staged?.Assignments.GetValueOrDefault(id))?.InstructionFiles.Add(f);
        }
        else if (owner == "module_item" && long.TryParse(S(tag["moduleId"]), out long modId) && long.TryParse(S(tag["itemId"]), out long itemId)
                 && Staged(cls).Modules.FirstOrDefault(m => m.Id == modId)?.Items.FirstOrDefault(i => i.Id == itemId) is { } item)
        {
            item.Local = f.Local;
            item.Size = f.Size;
            item.Skipped = f.Skipped;
            item.Format = CanvasView.FormatOf(f.ContentType, f.Name);
        }
    }

    static string Num(double? d) => (d ?? 0).ToString("0.##", CultureInfo.InvariantCulture);

    void Submissions(string cls, JsonArray list)
    {
        var index = Staged(cls);
        foreach (var s in list.OfType<JsonObject>())
        {
            var a = s["assignment"] as JsonObject;
            long id = (long)(D(s["assignment_id"]) ?? D(a?["id"]) ?? 0);
            if (id == 0) continue;
            string? dir = a is not null ? AssignmentDir(cls, a)
                : S(Manifest[$"asgdir:{cls}:{id}"]) is { Length: > 0 } known ? Path.Combine(classDir(cls), known) : null;
            index.Staged!.Submissions[id] = Submission(cls, s, dir);
            if (a is not null && dir is not null && Assignments.Published(a))
            {
                var (info, files) = AssignmentInfo.From(a, Rel(cls, dir), BuildContext(cls, index.CourseId, Rel(cls, dir)));
                index.Staged.Assignments[id] = info;
                QueueFileLinks(cls, files, Path.Combine(dir, "files"), "assignment", Num(info.Id));
            }
        }
    }

    [GeneratedRegex("/users/\\d+/")]
    private static partial Regex UserPath();

    /// <summary>
    /// A submission as the index keeps it, with its files queued: the latest attempt's into submission/, older
    /// attempts' into submission/attempt N/, files attached to comments into feedback/. Audio and video comments stay
    /// links. People are kept by display name only (never ids, avatars or emails); a comment is the student's own
    /// when its author is the submission's owner.
    /// </summary>
    SubmissionInfo Submission(string cls, JsonObject s, string? dir)
    {
        var info = SubmissionInfo.Summary(s);
        var had = new Dictionary<long, FileRef>(); // a file handed in again with the next attempt is saved once
        List<FileRef> Files(JsonNode? list, string into) => (list as JsonArray ?? []).OfType<JsonObject>().Select(meta =>
        {
            long fid = (long)(D(meta["id"]) ?? 0);
            if (fid != 0 && had.TryGetValue(fid, out var known)) return known;
            var f = dir is null ? Describe(meta) : WantFile(cls, meta, Path.Combine(dir, into));
            if (fid != 0) had[fid] = f;
            return f;
        }).ToList();

        info.Files = Files(s["attachments"], "submission");
        info.Body = HtmlText.ToMarkdown(S(s["body"])) is { Length: > 0 } body ? body : S(s["url"]) is { Length: > 0 } link ? $"<{link}>" : "";
        var history = (s["submission_history"] as JsonArray ?? []).OfType<JsonObject>().Where(h => D(h["attempt"]) is not null)
            .GroupBy(h => (int)D(h["attempt"])!.Value).Select(g => g.Last()).OrderBy(h => D(h["attempt"]));
        foreach (var h in history)
        {
            int n = (int)D(h["attempt"])!.Value;
            info.Attempts.Add(new AttemptInfo
            {
                Attempt = n, SubmittedAt = S(h["submitted_at"]) is { Length: > 0 } at ? at : null, Late = Flag(h["late"]),
                Files = n == info.Attempt ? info.Files : Files(h["attachments"], Path.Combine("submission", $"attempt {n}")),
            });
        }
        if (info.Attempts.Count == 0 && info.Attempt is int only && info.SubmittedAt is not null)
            info.Attempts.Add(new AttemptInfo { Attempt = only, SubmittedAt = info.SubmittedAt, Late = info.Late, Files = info.Files });

        if (s["rubric_assessment"] is JsonObject marks)
            foreach (var (criterion, m) in marks)
                if (m is JsonObject mark)
                    info.Marks[criterion] = new RubricMark { Points = D(mark["points"]), RatingId = S(mark["rating_id"]) is { Length: > 0 } r ? r : null, Comment = Py.Strip(S(mark["comments"])) };

        double? owner = D(s["user_id"]);
        foreach (var c in (s["submission_comments"] as JsonArray ?? []).OfType<JsonObject>())
            info.Comments.Add(new CommentInfo
            {
                Author = Py.Strip(S(c["author_name"])),
                At = S(c["created_at"]) is { Length: > 0 } at ? at : null,
                Text = Py.Strip(S(c["comment"])),
                Files = Files(c["attachments"], "feedback"),
                // Canvas's link names the student by id; "self" opens the same recording.
                MediaUrl = c["media_comment"] is JsonObject media && S(media["url"]) is { Length: > 0 } url ? UserPath().Replace(url, "/users/self/") : null,
                Mine = owner is not null && D(c["author_id"]) == owner,
            });
        return info;
    }

    /// <summary>A Canvas file as the index keeps it (its link without the one-time verifier), and why it won't be
    /// saved, if it won't: "locked", "too big", or "video" (audio and video).</summary>
    static FileRef Describe(JsonObject meta)
    {
        string type = S(meta["content-type"]), url = S(meta["url"]);
        return new FileRef
        {
            Id = (long)(D(meta["id"]) ?? 0),
            Name = S(meta["display_name"]) is { Length: > 0 } dn ? dn : S(meta["filename"]) is { Length: > 0 } fn ? fn : Num(D(meta["id"])),
            Size = D(meta["size"]) is double n ? (long)n : null,
            ContentType = type,
            UpdatedAt = S(meta["updated_at"]) is { Length: > 0 } u ? u : null,
            Url = url.Split('?')[0],
            // A video is never saved, whatever its size: "video" says more than "too big".
            Skipped = Flag(meta["locked_for_user"]) || url.Length == 0 ? "locked"
                : type.StartsWith("video/", StringComparison.Ordinal) || type.StartsWith("audio/", StringComparison.Ordinal) ? "video"
                : (D(meta["size"]) ?? 0) > MaxBytes ? "too big"
                : null,
        };
    }

    /// <summary>One copy per Canvas file id: the first place a file was wanted is remembered
    /// (<c>fileloc:{class}:{fileId}</c>, stable across syncs), so a later want for the same id (a module, the Files
    /// area, a link in a page) points at that copy instead of downloading it again. A different file wanting a path
    /// already taken gets "name (2).ext".</summary>
    string ClaimedPath(string cls, long fileId, string destDir, string name)
    {
        string locKey = $"fileloc:{cls}:{fileId}";
        if (S(Manifest[locKey]) is { Length: > 0 } known) return Path.Combine(classDir(cls), known.Replace('/', Path.DirectorySeparatorChar));
        var taken = Manifest.Where(kv => kv.Key.StartsWith("fileloc:" + cls + ":", StringComparison.Ordinal)).Select(kv => S(kv.Value)).ToHashSet(StringComparer.Ordinal);
        string ext = Path.GetExtension(name), stem = Path.GetFileNameWithoutExtension(name), path = Path.Combine(destDir, name);
        for (int i = 2; taken.Contains(Rel(cls, path)); i++) path = Path.Combine(destDir, $"{stem} ({i}){ext}");
        Manifest[locKey] = Rel(cls, path);
        return path;
    }

    /// <summary>Queue a file's bytes into <paramref name="destDir"/> (or wherever it already landed, for the same
    /// Canvas file id), unless this version is already there or already asked for, and say where it goes (or why it
    /// won't).</summary>
    FileRef WantFile(string cls, JsonObject meta, string destDir, string? name = null)
    {
        var f = Describe(meta);
        if (f.Skipped is not null) return f;
        string path = ClaimedPath(cls, f.Id, destDir, SafeName(name ?? f.Name));
        f.Local = Rel(cls, path);
        string key = $"file:{f.Id}:{Rel(cls, path)}", updated = S(meta["updated_at"]);
        string have = S(Manifest[key]);
        if (have == updated || have == "queued:" + updated) return f; // already have it, or someone else already asked
        Manifest[key] = "queued:" + updated;
        Add(S(meta["url"]), "bytes", Tag("file_bytes", cls, ("path", path), ("key", key), ("updated", updated)));
        return f;
    }

    void FileBytes(JsonObject tag, byte[] body)
    {
        Write(S(tag["class"]), S(tag["path"]), body);
        Manifest[S(tag["key"])] = S(tag["updated"]);
    }

    /// <summary>Each module, its folder (by Canvas id, renamed in place rather than duplicated), and its items: File
    /// and Page items queue their own fetch; a module with no inline items (<c>items_url</c>) is fetched separately
    /// (<see cref="BuildItems"/> runs again once that finishes, from <c>module_items:&lt;id&gt;</c>).</summary>
    void Modules(string cls, JsonArray list)
    {
        var modules = new List<ModuleInfo>();
        foreach (var mod in list.OfType<JsonObject>().OrderBy(m => D(m["position"]) ?? 0))
        {
            long id = (long)(D(mod["id"]) ?? 0);
            int position = (int)(D(mod["position"]) ?? 0);
            string name = S(mod["name"]);
            string folder = ModuleDir(cls, id, position, name);
            var info = new ModuleInfo
            {
                Id = id, Name = name, Position = position,
                State = S(mod["state"]) is { Length: > 0 } st ? st : null,
                UnlockAt = S(mod["unlock_at"]) is { Length: > 0 } u ? u : null,
                ItemsCount = (int)(D(mod["items_count"]) ?? 0),
            };
            modules.Add(info);
            if (mod["items"] is JsonArray items) info.Items = BuildItems(cls, id, folder, items);
            else if (S(mod["items_url"]) is { Length: > 0 } itemsUrl)
                Add($"{itemsUrl}{(itemsUrl.Contains('?') ? "&" : "?")}include[]=content_details&per_page=100", "json", Tag($"module_items:{id}", cls));
        }
        Staged(cls).Modules = modules;
    }

    /// <summary>The folder for a module, by its Canvas id (<c>moddir:{class}:{id}</c>): a renamed or reordered module
    /// moves its existing folder (when nothing already sits where it's moving to) instead of making a second one.</summary>
    string ModuleDir(string cls, long moduleId, int position, string name)
    {
        string key = $"moddir:{cls}:{moduleId}";
        string desiredRel = Rel(cls, Path.Combine(CanvasDir(cls), "modules", $"{position:00} {SafeName(name, 80)}"));
        string desiredAbs = Path.Combine(classDir(cls), desiredRel.Replace('/', Path.DirectorySeparatorChar));
        if (S(Manifest[key]) is { Length: > 0 } known && !string.Equals(known, desiredRel, StringComparison.Ordinal))
        {
            string knownAbs = Path.Combine(classDir(cls), known.Replace('/', Path.DirectorySeparatorChar));
            if (Directory.Exists(knownAbs) && !Directory.Exists(desiredAbs))
            {
                Directory.CreateDirectory(Py.Parent(desiredAbs));
                Directory.Move(knownAbs, desiredAbs);
                RewriteManifestPrefix(cls, known, desiredRel);
            }
            else if (Directory.Exists(knownAbs))
            {
                desiredAbs = knownAbs;
                desiredRel = known;
            }
        }
        Manifest[key] = desiredRel;
        return desiredAbs;
    }

    /// <summary>A moved folder's saved files still answer to their old manifest keys (the version markers, and the
    /// one-copy-per-file-id map): rewrite whichever of those point inside <paramref name="oldRel"/> to
    /// <paramref name="newRel"/> instead.</summary>
    void RewriteManifestPrefix(string cls, string oldRel, string newRel)
    {
        string oldPre = oldRel.Replace('\\', '/').TrimEnd('/') + "/", newPre = newRel.Replace('\\', '/').TrimEnd('/') + "/";
        foreach (var (key, value) in Manifest.ToList())
        {
            if (key.StartsWith($"fileloc:{cls}:", StringComparison.Ordinal) && S(value).StartsWith(oldPre, StringComparison.Ordinal))
                Manifest[key] = newPre + S(value)[oldPre.Length..];
            else if (key.StartsWith("file:", StringComparison.Ordinal) && key.IndexOf(':', 5) is int second and >= 0
                     && key[(second + 1)..].StartsWith(oldPre, StringComparison.Ordinal))
            {
                string newKey = key[..(second + 1)] + newPre + key[(second + 1 + oldPre.Length)..];
                Manifest.Remove(key);
                Manifest[newKey] = value?.DeepClone();
            }
        }
    }

    /// <summary>box.com, drive/docs.google.com, onedrive.live.com/1drv.ms/*.sharepoint.com, youtube.com/youtu.be: the
    /// services a module's external links most often point at (the scout may have saved a copy beside them).</summary>
    static string? SourceOf(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var u)) return null;
        string host = u.Host.ToLowerInvariant();
        bool Is(string domain) => host == domain || host.EndsWith("." + domain, StringComparison.Ordinal);
        if (Is("box.com")) return "box";
        if (Is("drive.google.com") || Is("docs.google.com")) return "drive";
        if (Is("onedrive.live.com") || Is("1drv.ms") || Is("sharepoint.com")) return "onedrive";
        if (Is("youtube.com") || Is("youtu.be")) return "youtube";
        return null;
    }

    /// <summary>A file the scout saved beside an external link, named after the item (any extension).</summary>
    static string? ScoutSaved(string folderAbs, string title)
    {
        if (!Directory.Exists(folderAbs)) return null;
        string stem = SafeName(title);
        return Directory.EnumerateFiles(folderAbs).FirstOrDefault(f => Path.GetFileNameWithoutExtension(f) == stem);
    }

    /// <summary>One module's items, in Canvas's order: a file or page queues its own fetch (into the module's own
    /// folder); an assignment, quiz or discussion is resolved to its spec.md when the outline is rendered, once every
    /// listing is in.</summary>
    List<ModuleItemInfo> BuildItems(string cls, long moduleId, string folderAbs, JsonArray items)
    {
        string folderRel = Rel(cls, folderAbs);
        var result = new List<ModuleItemInfo>();
        foreach (var it in items.OfType<JsonObject>())
        {
            string type = S(it["type"]);
            var info = new ModuleItemInfo
            {
                Id = (long)(D(it["id"]) ?? 0), Type = type,
                Title = S(it["title"]) is { Length: > 0 } t ? t : "untitled",
                Indent = (int)(D(it["indent"]) ?? 0), Url = S(it["html_url"]),
                Locked = Flag(it["content_details"]?["locked_for_user"]),
            };
            long contentId = (long)(D(it["content_id"]) ?? 0);
            switch (type)
            {
                case "File":
                    info.Kind = "file";
                    if (info.Locked) info.Skipped = "locked";
                    else if (S(it["url"]) is { Length: > 0 } fileUrl)
                        Add(fileUrl, "json", Tag("file_meta", cls, ("dir", folderAbs), ("owner", "module_item"),
                            ("moduleId", moduleId.ToString(CultureInfo.InvariantCulture)), ("itemId", info.Id.ToString(CultureInfo.InvariantCulture))));
                    break;
                case "Page":
                    info.Kind = "page";
                    info.PageUrl = S(it["page_url"]) is { Length: > 0 } slug ? slug : null;
                    if (info.Locked) info.Skipped = "locked";
                    else if (S(it["url"]) is { Length: > 0 } pageUrl)
                    {
                        Add(pageUrl, "json", Tag("page", cls, ("dir", folderAbs), ("owner", "module_item"),
                            ("moduleId", moduleId.ToString(CultureInfo.InvariantCulture)), ("itemId", info.Id.ToString(CultureInfo.InvariantCulture))));
                        if (info.PageUrl is { Length: > 0 } known)
                            (modulePageSlugs.TryGetValue(cls, out var slugs) ? slugs : modulePageSlugs[cls] = []).Add(known);
                    }
                    break;
                case "Assignment": info.Kind = "assignment"; info.AssignmentId = contentId; break;
                case "Quiz": info.Kind = "quiz"; info.AssignmentId = contentId; break;
                case "Discussion": info.Kind = "discussion"; info.AssignmentId = contentId; break;
                case "ExternalUrl":
                    info.Kind = "link";
                    info.ExternalUrl = S(it["external_url"]) is { Length: > 0 } eu ? eu : S(it["html_url"]);
                    info.Source = SourceOf(info.ExternalUrl);
                    if (ScoutSaved(folderAbs, info.Title) is { } found) { info.Saved = true; info.Local = Rel(cls, found); }
                    break;
                case "ExternalTool":
                    info.Kind = "tool";
                    info.ExternalUrl = S(it["external_url"]) is { Length: > 0 } tu ? tu : S(it["url"]);
                    break;
                case "SubHeader": info.Kind = "header"; break;
                default: info.Kind = type.Length > 0 ? type.ToLowerInvariant() : "item"; break;
            }
            result.Add(info);
        }
        return result;
    }

    /// <summary>A course's Files area: folders first (so each file's path is known), then the files (paged, both).
    /// Skipped when Canvas hides it (401/403/404, handled generically as <c>files</c> is hidden or failed).</summary>
    void FilesStage(string cls, JsonObject tag, JsonArray page, bool more)
    {
        string stage = S(tag["stage"]), bufKey = "files_" + stage;
        if (PagesSoFar[cls] is not JsonObject mine) PagesSoFar[cls] = mine = [];
        if (mine[bufKey] is not JsonArray all) mine[bufKey] = all = [];
        foreach (var item in page) all.Add(item?.DeepClone());
        if (more) return;
        mine.Remove(bufKey);
        if (stage == "folders")
        {
            var folders = new JsonObject();
            foreach (var f in all.OfType<JsonObject>()) folders[Num(D(f["id"]))] = FolderPath(S(f["full_name"]));
            mine["files_foldermap"] = folders;
            string canvasUrl = S(data["base"]);
            long courseId = Staged(cls).CourseId;
            Add($"{canvasUrl}/api/v1/courses/{courseId}/files?per_page=100", "json", Tag("files", cls, ("stage", "course_files")));
            return;
        }
        var folderMap = mine["files_foldermap"] as JsonObject ?? [];
        mine.Remove("files_foldermap");
        var list = new List<CourseFileInfo>();
        foreach (var f in all.OfType<JsonObject>())
        {
            if (Flag(f["hidden_for_user"]) || Flag(f["locked_for_user"])) continue;
            string folder = S(folderMap[Num(D(f["folder_id"]))]);
            var saved = WantFile(cls, f, Path.Combine(CanvasDir(cls), "files", folder.Replace('/', Path.DirectorySeparatorChar)));
            list.Add(new CourseFileInfo
            {
                Id = saved.Id, Folder = folder, Name = saved.Name, Size = saved.Size, ContentType = saved.ContentType,
                UpdatedAt = S(f["updated_at"]) is { Length: > 0 } u ? u : null, Local = saved.Local, Skipped = saved.Skipped,
            });
        }
        Staged(cls).Files = list;
        Section(cls, "files", "ok");
    }

    /// <summary>A folder's path under the Files area, without Canvas's leading "course files".</summary>
    static string FolderPath(string fullName) =>
        (fullName.StartsWith("course files", StringComparison.OrdinalIgnoreCase) ? fullName["course files".Length..] : fullName).Trim('/');

    void Page(string cls, JsonObject page, JsonObject tag)
    {
        string dir = S(tag["dir"]);
        if (Flag(page["locked_for_user"]))
        {
            AttachModuleItem(cls, tag, local: null, skipped: "locked");
            return;
        }
        // A file a module page links to is wanted into that module's own folder, not pages/files/.
        var (body, links) = HtmlText.Convert(S(page["body"]), BuildContext(cls, Staged(cls).CourseId, Rel(cls, dir)));
        QueueFileLinks(cls, links, Path.Combine(dir, "files"));
        string text = $"# {S(page["title"])}\n\n_From Canvas ({S(page["html_url"])}), updated {CanvasMarkdown.When(S(page["updated_at"]), zone())}._\n\n{body}\n";
        string path = Path.Combine(dir, SafeName(S(page["title"]) is { Length: > 0 } t ? t : "page") + ".md");
        Write(cls, path, text);
        AttachModuleItem(cls, tag, Rel(cls, path), skipped: null);
    }

    /// <summary>A module Page item learns where its Markdown landed (or that it was locked), once it's fetched.</summary>
    void AttachModuleItem(string cls, JsonObject tag, string? local, string? skipped)
    {
        if (S(tag["owner"]) != "module_item" || !long.TryParse(S(tag["moduleId"]), out long modId) || !long.TryParse(S(tag["itemId"]), out long itemId)) return;
        if (Staged(cls).Modules.FirstOrDefault(m => m.Id == modId)?.Items.FirstOrDefault(i => i.Id == itemId) is not { } item) return;
        item.Local = local;
        item.Skipped = skipped;
    }

    /// <summary>Every announcement, newest first, with its attachments queued into <c>announcements/files/</c> and
    /// its body's own file links resolved the same way as a page's. <c>announcements.md</c> itself is rendered at
    /// <see cref="Render"/>, from this, so its links point at where a file really landed.</summary>
    void Announcements(string cls, JsonArray list)
    {
        var index = Staged(cls);
        string destDir = Path.Combine(CanvasDir(cls), "announcements", "files");
        var infos = new List<AnnouncementInfo>();
        foreach (var a in list.OfType<JsonObject>().OrderByDescending(x => S(x["posted_at"]), StringComparer.Ordinal))
        {
            var (body, links) = HtmlText.Convert(S(a["message"]), BuildContext(cls, index.CourseId, Rel(cls, Path.Combine(CanvasDir(cls), "announcements"))));
            QueueFileLinks(cls, links, destDir);
            var files = (a["attachments"] as JsonArray ?? []).OfType<JsonObject>().Select(meta => WantFile(cls, meta, destDir)).ToList();
            infos.Add(new AnnouncementInfo
            {
                Id = (long)(D(a["id"]) ?? 0), Title = Py.Strip(S(a["title"])), PostedAt = Opt(a["posted_at"]),
                Author = Py.Strip(S(a["author"]?["display_name"])), ReadOnCanvas = S(a["read_state"]) == "read",
                Body = body, Files = files, HtmlUrl = S(a["html_url"]),
            });
        }
        index.Announcements = infos;
    }

    /// <summary>Every quiz's facts (never its questions, answers or submissions), merged in by id as pages arrive.</summary>
    void QuizzesPage(string cls, JsonArray list)
    {
        var index = Staged(cls);
        string destDir = Path.Combine(CanvasDir(cls), "quizzes", "files");
        foreach (var q in list.OfType<JsonObject>())
        {
            long id = (long)(D(q["id"]) ?? 0);
            if (id == 0) continue;
            var (description, links) = HtmlText.Convert(S(q["description"]), BuildContext(cls, index.CourseId, Rel(cls, Path.Combine(CanvasDir(cls), "quizzes"))));
            QueueFileLinks(cls, links, destDir);
            var info = new QuizInfo
            {
                Id = id, Title = Py.Strip(S(q["title"])), QuizType = S(q["quiz_type"]), DueAt = Opt(q["due_at"]),
                Points = D(q["points_possible"]), TimeLimit = D(q["time_limit"]) is double tl ? (int)tl : null,
                AllowedAttempts = D(q["allowed_attempts"]) is double aa ? (int)aa : null,
                QuestionCount = D(q["question_count"]) is double qc ? (int)qc : null, Description = description,
                AssignmentId = D(q["assignment_id"]) is double aid ? (long)aid : null,
                Locked = Flag(q["locked_for_user"]), HtmlUrl = S(q["html_url"]),
            };
            int at = index.Quizzes.FindIndex(x => x.Id == id);
            if (at >= 0) index.Quizzes[at] = info; else index.Quizzes.Add(info);
        }
    }

    /// <summary>Real (ungraded, or graded-but-folded-into-an-assignment) discussion topics: never their entries or
    /// views, only the prompt. An item marked <c>is_announcement</c> is one Canvas answered here by mistake (the
    /// two listings share a Canvas path): skipped, since the announcements listing already has it.</summary>
    void DiscussionsPage(string cls, JsonArray list)
    {
        var index = Staged(cls);
        string destDir = Path.Combine(CanvasDir(cls), "discussions", "files");
        foreach (var t in list.OfType<JsonObject>())
        {
            long id = (long)(D(t["id"]) ?? 0);
            if (id == 0 || Flag(t["is_announcement"])) continue;
            var (prompt, links) = HtmlText.Convert(S(t["message"]), BuildContext(cls, index.CourseId, Rel(cls, Path.Combine(CanvasDir(cls), "discussions"))));
            QueueFileLinks(cls, links, destDir);
            var info = new DiscussionInfo
            {
                Id = id, Title = Py.Strip(S(t["title"])), Prompt = prompt,
                AssignmentId = D(t["assignment_id"]) is double aid ? (long)aid : null,
                TodoDate = Opt(t["todo_date"]), Locked = Flag(t["locked_for_user"]), HtmlUrl = S(t["html_url"]),
            };
            int at = index.Discussions.FindIndex(x => x.Id == id);
            if (at >= 0) index.Discussions[at] = info; else index.Discussions.Add(info);
        }
    }

    /// <summary>Canvas's planner, for every linked class at once: an assignment marked done there is noted (applied
    /// to the index at <see cref="ApplyPlanner"/>, once the assignments listing's own copy is final); anything else
    /// with a date becomes a to-do. Calendar events aren't coursework, so they're dropped.</summary>
    void Planner(JsonArray list)
    {
        foreach (var item in list.OfType<JsonObject>())
        {
            string cls = S(Courses[Num(D(item["course_id"]))]);
            if (cls.Length == 0) continue; // a course this student takes but Study Stash doesn't sync
            string type = S(item["plannable_type"]);
            var plannable = item["plannable"] as JsonObject ?? [];
            long id = (long)(D(item["plannable_id"]) ?? D(plannable["id"]) ?? 0);
            if (id == 0 || type == "calendar_event") continue;
            var over = item["planner_override"] as JsonObject;
            bool done = Flag(over?["marked_complete"]);
            if (type == "assignment")
            {
                if (!done) continue;
                (markedDone.TryGetValue(cls, out var have) ? have : markedDone[cls] = [])[id] = Opt(over?["updated_at"]);
                continue;
            }
            string title = Py.Strip(S(plannable["title"])) is { Length: > 0 } t ? t : "untitled";
            string? todoAt = Opt(item["plannable_date"]) ?? Opt(plannable["todo_date"]);
            var todo = new TodoInfo { Id = id, Kind = type, Title = title, TodoAt = todoAt, HtmlUrl = S(item["html_url"]), MarkedDone = done };
            var list2 = Staged(cls).Todos;
            int at = list2.FindIndex(x => x.Id == id);
            if (at >= 0) list2[at] = todo; else list2.Add(todo);
        }
    }

    /// <summary>Bakes this sync's planner reads into the promoted index: an assignment the planner said (this sync)
    /// is marked done gets it; one it didn't read (hidden, failed, or a class the planner job never covered) keeps
    /// what the previous index already knew, since "planner" not being <c>ok</c> means "unknown", not "false".</summary>
    void ApplyPlanner(string cls, CourseIndex index, CourseIndex? prev)
    {
        bool read = index.Sections.GetValueOrDefault("planner") == "ok";
        var done = markedDone.GetValueOrDefault(cls) ?? [];
        var before = (prev?.Assignments ?? []).ToDictionary(a => a.Id);
        foreach (var a in index.Assignments)
        {
            if (read)
            {
                a.MarkedDone = done.TryGetValue(a.Id, out var at);
                a.MarkedDoneAt = a.MarkedDone ? at : null;
            }
            else if (before.TryGetValue(a.Id, out var old))
            {
                a.MarkedDone = old.MarkedDone;
                a.MarkedDoneAt = old.MarkedDoneAt;
            }
        }
    }

    // --- the syllabus, pages outside modules, and the front page: rendered when the sync finishes, from what's
    // staged here, so their file links can point at where a file really landed (FinishPages). -----------------------

    static string? Opt(JsonNode? v) => S(v) is { Length: > 0 } s ? s : null;

    void Course(string cls, JsonObject c)
    {
        var index = Staged(cls);
        index.Code = S(c["course_code"]);
        index.Name = S(c["name"]);
        index.Term = S(c["term"]?["name"]);
        index.HtmlUrl = S(c["html_url"]);
        if (S(c["syllabus_body"]) is not { Length: > 0 } syllabus) return;
        string root = Rel(cls, CanvasDir(cls));
        var (md, links) = HtmlText.Convert(syllabus, BuildContext(cls, index.CourseId, root));
        QueueFileLinks(cls, links, Path.Combine(CanvasDir(cls), "pages", "files"));
        pendingSyllabus[cls] = new PendingPage("", md, CanvasDir(cls), S(c["html_url"]), null);
    }

    void PagesListing(string cls, JsonArray list)
    {
        string canvasUrl = S(data["base"]);
        long id = Staged(cls).CourseId;
        var known = modulePageSlugs.GetValueOrDefault(cls) ?? [];
        foreach (var p in list.OfType<JsonObject>())
        {
            string slug = S(p["url"]);
            if (slug.Length == 0 || known.Contains(slug) || Flag(p["front_page"])) continue; // a module's own page, or handled by front_page
            Add($"{canvasUrl}/api/v1/courses/{id}/pages/{slug}", "json", Tag("outside_page", cls, ("slug", slug)));
        }
    }

    static void Upsert(List<PageInfo> list, string slug, PageInfo info)
    {
        int at = list.FindIndex(x => x.Url == slug);
        if (at >= 0) list[at] = info;
        else list.Add(info);
    }

    void OutsidePage(string cls, JsonObject page, string slug)
    {
        string title = Py.Strip(S(page["title"])) is { Length: > 0 } t ? t : "untitled";
        string htmlUrl = S(page["html_url"]);
        string? updated = Opt(page["updated_at"]);
        if (Flag(page["locked_for_user"]))
        {
            Upsert(Staged(cls).Pages, slug, new PageInfo { Title = title, Url = slug, HtmlUrl = htmlUrl, UpdatedAt = updated, Locked = true });
            return;
        }
        string dir = Path.Combine(CanvasDir(cls), "pages");
        var (md, links) = HtmlText.Convert(S(page["body"]), BuildContext(cls, Staged(cls).CourseId, Rel(cls, dir)));
        QueueFileLinks(cls, links, Path.Combine(dir, "files"));
        (pendingPages.TryGetValue(cls, out var mine) ? mine : pendingPages[cls] = [])[slug] = new PendingPage(title, md, dir, htmlUrl, updated);
        Upsert(Staged(cls).Pages, slug, new PageInfo { Title = title, Url = slug, HtmlUrl = htmlUrl, UpdatedAt = updated });
    }

    void FrontPage(string cls, JsonObject page)
    {
        string slug = S(page["url"]), title = Py.Strip(S(page["title"])) is { Length: > 0 } t ? t : "Front page";
        string htmlUrl = S(page["html_url"]);
        string? updated = Opt(page["updated_at"]);
        if (Flag(page["locked_for_user"]))
        {
            Staged(cls).FrontPage = new PageInfo { Title = title, Url = slug, HtmlUrl = htmlUrl, UpdatedAt = updated, Locked = true, FrontPage = true };
            return;
        }
        string dir = Path.Combine(CanvasDir(cls), "pages");
        var (md, links) = HtmlText.Convert(S(page["body"]), BuildContext(cls, Staged(cls).CourseId, Rel(cls, dir)));
        QueueFileLinks(cls, links, Path.Combine(dir, "files"));
        pendingFrontPage[cls] = new PendingPage(title, md, dir, htmlUrl, updated);
        Staged(cls).FrontPage = new PageInfo { Title = title, Url = slug, HtmlUrl = htmlUrl, UpdatedAt = updated, FrontPage = true };
    }

    string WritePageFile(string cls, PendingPage p, Func<long, string?> local)
    {
        string body = HtmlText.ResolveLinks(p.Body, local);
        string text = $"# {p.Title}\n\n_From Canvas ({p.HtmlUrl}), updated {CanvasMarkdown.When(p.UpdatedAt, zone())}._\n\n{body}\n";
        string path = Path.Combine(p.Dir, SafeName(p.Title) + ".md");
        Write(cls, path, text);
        return path;
    }

    /// <summary>Write the syllabus, the pages outside modules and the front page (whatever this sync staged for the
    /// class), now that every file it wanted has either landed or failed to. Called before a class's index is saved,
    /// so <see cref="CourseIndex.Syllabus"/>, <see cref="CourseIndex.Pages"/> and <see cref="CourseIndex.FrontPage"/>
    /// hold where each one really went. Also fixes up any Canvas-file link an assignment's instructions still point
    /// at Canvas for, now that the file it names may have finished downloading.</summary>
    void FinishPages(string cls, CourseIndex index)
    {
        string pagesRoot = Rel(cls, Path.Combine(CanvasDir(cls), "pages")), classRoot = Rel(cls, CanvasDir(cls));
        Func<long, string?> LocalIn(string fromFolder) => id => LocalOf(cls, id) is { } p ? CanvasMarkdown.RelativeLink(fromFolder, p) : null;

        if (pendingSyllabus.Remove(cls, out var syllabus))
        {
            string body = HtmlText.ResolveLinks(syllabus.Body, LocalIn(classRoot));
            Write(cls, Path.Combine(CanvasDir(cls), "syllabus.md"), $"# {cls}: syllabus\n\n_From Canvas ({syllabus.HtmlUrl})._\n\n{body}\n");
            index.Syllabus = Rel(cls, Path.Combine(CanvasDir(cls), "syllabus.md"));
        }
        if (pendingFrontPage.Remove(cls, out var front) && index.FrontPage is not null)
            index.FrontPage.Local = Rel(cls, WritePageFile(cls, front, LocalIn(pagesRoot)));
        if (pendingPages.Remove(cls, out var pages))
            foreach (var (slug, p) in pages)
                if (index.Pages.FirstOrDefault(x => x.Url == slug) is { } info)
                    info.Local = Rel(cls, WritePageFile(cls, p, LocalIn(pagesRoot)));

        foreach (var a in index.Assignments)
            if (a.Instructions.Length > 0)
                a.Instructions = HtmlText.ResolveLinks(a.Instructions, LocalIn(a.Folder));
    }
}
