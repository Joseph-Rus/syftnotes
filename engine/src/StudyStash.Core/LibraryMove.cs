using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using StudyStash.Core.Ai;
using StudyStash.Core.Canvas;

namespace StudyStash.Core;

/// <summary>How far bringing a library over has got: which part of it (<see cref="LibraryMove.Lectures"/>,
/// <see cref="LibraryMove.AttachmentsStage"/>, <see cref="LibraryMove.FilesStage"/>), and how many of those are in, of
/// how many the old library has.</summary>
public sealed record MoveProgress(int Done, int Total, string Stage = LibraryMove.Lectures);

/// <summary>What bringing a library over did: lectures filed in the new library, ones it already had (left as they
/// are), and classes it didn't have yet; files (attachments and the library folder's other files) and chats that came
/// too; files the old library listed but no longer had; lectures that came still waiting for their notes (the new
/// library writes them); and whether part of it couldn't come because one of the two runs an older Study Stash.</summary>
public sealed record MoveResult(int Filed, int Skipped, int Classes, int Files = 0, int Missed = 0, int Writing = 0, int Chats = 0,
    bool Partial = false);

/// <summary>
/// Bringing everything from one library to another: when a laptop becomes the library ("Use just this computer"), or
/// a library hands its lectures to another before becoming a laptop (Settings → Connection, docs/one-download.md).
/// It's a copy, done in parts, each a page at a time: the library's classes (their other names and what each covers,
/// in their order, with their Canvas courses), every lecture (its notes as its file has them, its transcript, its
/// class and who chose it; one still waiting for notes is written by the new library), the files attached to lectures
/// and classes, the library folder's other files (captured notes, the Inbox, the Canvas mirror), and the chats. The old
/// library is never changed, and the new one never overwrites anything it has: a lecture, attachment or chat it already
/// has is left alone, and a file whose name is taken by a different one comes in beside it. So doing it again (after a
/// failure part-way, say) brings only what's still missing, never a second copy.
/// </summary>
public static class LibraryMove
{
    public const string Route = "/api/v2/move/lectures";
    public const string LibraryRoute = "/api/v2/move/library";
    public const string AttachmentsRoute = "/api/v2/move/attachments";
    public const string FilesRoute = "/api/v2/move/files";
    public const string ChatsRoute = "/api/v2/move/chats";
    /// <summary>After a route: which of these the new library hasn't got yet.</summary>
    public const string Wanted = "/wanted";
    /// <summary>Lectures per page: transcripts can be long, so a page stays a few megabytes at most.</summary>
    public const int PageSize = 20;

    public const string Lectures = "lectures", AttachmentsStage = "attachments", FilesStage = "files";

    /// <summary>The folder a file on its way in waits in, in the new library's folder (hidden, so it's never listed).</summary>
    const string Incoming = ".move-incoming";

    static readonly JsonSerializerOptions Snake = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    // --- the old library's side: what it has, a page at a time. Nothing is changed. ------------------------------

    /// <summary>The library itself: its name, its classes in order (other names, what each covers, its Canvas
    /// course), its Canvas address and courses, how it writes and sorts notes, and how many lectures and attachments
    /// it has.</summary>
    public static JsonObject ExportLibrary(Store store, Config cfg)
    {
        var canvas = CanvasSettings.Load(cfg.Home);
        var ai = AiSettings.Load(cfg.Home);
        return new JsonObject
        {
            ["version"] = 2,
            ["name"] = cfg.PoolName,
            ["classes"] = ClassesJson(cfg),
            ["canvas"] = new JsonObject
            {
                ["url"] = canvas.Url,
                ["courses"] = JsonSerializer.SerializeToNode(canvas.Courses, Snake),
                ["available"] = JsonSerializer.SerializeToNode(canvas.Available, Snake),
                ["course_info"] = JsonSerializer.SerializeToNode(canvas.CourseInfo, Snake),
                ["chosen"] = JsonSerializer.SerializeToNode(canvas.Chosen, Snake),
                ["poll_minutes"] = canvas.PollMinutes,
            },
            ["notes"] = new JsonObject
            {
                ["summary_enabled"] = cfg.SummaryEnabled, ["summary_model"] = cfg.SummaryModel, ["summary_max_context"] = cfg.SummaryMaxContext,
                ["ollama_enabled"] = cfg.OllamaEnabled, ["ollama_host"] = cfg.OllamaHost, ["ollama_model"] = cfg.OllamaModel,
                ["min_confidence"] = cfg.MinConfidence,
            },
            ["ai"] = new JsonObject
            {
                ["provider"] = ai.Provider, ["by_job"] = JsonSerializer.SerializeToNode(ai.ByJob, Snake),
                ["models"] = JsonSerializer.SerializeToNode(ai.Models, Snake), ["fallback"] = ai.Fallback, ["terminal"] = ai.Terminal,
            },
            ["lectures"] = store.NoteCount(),
            ["attachments"] = store.AttachmentCount(),
        };
    }

    static JsonArray ClassesJson(Config cfg) => new(cfg.Classes.Select(c => (JsonNode?)new JsonObject
    {
        ["name"] = c.Name, ["aliases"] = new JsonArray(c.Aliases.Select(a => (JsonNode?)a).ToArray()), ["description"] = c.Description,
    }).ToArray());

    /// <summary>One page of this library's lectures, in id order, after <paramref name="after"/> (the first page when
    /// null): <c>{classes, lectures, total, next}</c>, where <c>next</c> is the id to ask after, or null on the last
    /// page. <paramref name="lean"/> pages carry each lecture's id only (and no classes), for the new library to say
    /// which it wants (<see cref="ExportOnly"/>); a full one carries every lecture whole, as a library before this
    /// one expects. Only the page's own lectures are read, so a big library's page costs no more than a small one's.</summary>
    public static JsonObject Export(Store store, Config cfg, string? after, int limit = PageSize, bool lean = false)
    {
        limit = Math.Clamp(limit, 1, 100);
        var ids = store.NoteIds(after, limit + 1);
        bool more = ids.Count > limit;
        if (more) ids.RemoveAt(limit);
        var lectures = new JsonArray();
        foreach (string id in ids)
        {
            if (lean) lectures.Add(new JsonObject { ["id"] = id });
            else if (Lecture(store, id) is { } whole) lectures.Add(whole);
        }
        var page = new JsonObject { ["version"] = 2, ["lectures"] = lectures, ["total"] = store.NoteCount(), ["next"] = more ? ids[^1] : null };
        if (!lean) page["classes"] = ClassesJson(cfg);
        return page;
    }

    /// <summary>These lectures, whole (a full page of <see cref="Export"/> without the paging): the ones the new
    /// library said it hasn't got.</summary>
    public static JsonObject ExportOnly(Store store, Config cfg, IEnumerable<string> ids) => new()
    {
        ["version"] = 2,
        ["classes"] = ClassesJson(cfg),
        ["lectures"] = new JsonArray([.. ids.Take(100).Select(id => Lecture(store, id)).OfType<JsonNode>()]),
    };

    /// <summary>A lecture whole: the lecture as it was recorded (transcript included), its class and who chose it, its
    /// study notes and what wrote them, and its note file exactly as it is on disk. Null when it's gone meanwhile.</summary>
    static JsonObject? Lecture(Store store, string id)
    {
        if (store.Get(id) is not { } row) return null;
        var m = store.Meeting(row);
        bool filed = !string.IsNullOrEmpty(row.MdPath) && !string.IsNullOrEmpty(row.ClassName);
        string? noteText = null;
        if (filed && File.Exists(row.MdPath))
        {
            try
            {
                noteText = File.ReadAllText(row.MdPath!, new UTF8Encoding(false));
            }
            catch (IOException)
            {
                // Written again from the lecture on the other side: its notes and transcript are all in the row.
            }
        }
        return new JsonObject
        {
            ["id"] = row.Id,
            ["meeting"] = JsonNode.Parse(Wire.MeetingJson(m)),
            ["class"] = row.ClassName ?? Configs.Unsorted,
            ["confidence"] = row.Confidence ?? 0,
            ["by"] = row.ClassifiedBy ?? "none",
            ["lecture_title"] = row.LectureTitle ?? "",
            ["topics"] = JsonNode.Parse(string.IsNullOrEmpty(row.Topics) ? "[]" : row.Topics) as JsonArray ?? [],
            ["summary"] = row.SummaryMd ?? "",
            ["summary_model"] = row.SummaryModel ?? "",
            ["error"] = row.Error ?? "",
            ["filed"] = filed,
            ["note_text"] = noteText,
        };
    }

    /// <summary>A page of this library's attachments (their words, and whether notes used them, included), in id
    /// order: <c>{attachments, total, next}</c>. The files themselves come from /api/v2/attachments/{id}/raw.</summary>
    public static JsonObject ExportAttachments(Store store, string? after, int limit = PageSize)
    {
        limit = Math.Clamp(limit, 1, 100);
        var list = store.AttachmentsAfter(after, limit + 1);
        bool more = list.Count > limit;
        if (more) list.RemoveAt(limit);
        return new JsonObject
        {
            ["attachments"] = new JsonArray([.. list.Select(a => (JsonNode)new JsonObject
            {
                ["id"] = a.Id, ["name"] = a.Name, ["file"] = a.File, ["class"] = a.ClassName, ["lecture"] = a.NoteId, ["size"] = a.Size,
                ["type"] = a.Type, ["added"] = a.Added, ["text"] = a.Text, ["state"] = a.State, ["used"] = a.Used,
            })]),
            ["total"] = store.AttachmentCount(),
            ["next"] = more ? list[^1].Id : null,
        };
    }

    /// <summary>A page of the library folder's other files: everything that isn't a lecture's note or an attachment
    /// (both come with their own parts) and isn't hidden: notes captured into a class, the Inbox, the Canvas mirror, a
    /// file the student put there. Each with its size and SHA-256, so the new library can tell a copy it already has
    /// from a different file of the same name.</summary>
    public static JsonObject ExportFiles(Store store, Config cfg, string? after, int limit = 50)
    {
        limit = Math.Clamp(limit, 1, 200);
        var managed = new HashSet<string>(PathComparer);
        foreach (string p in store.NoteFiles().Concat(store.AttachmentFiles())) managed.Add(Full(p));
        var all = LibraryFiles(cfg.PoolDir).Where(rel => !managed.Contains(Full(Path.Combine(cfg.PoolDir, rel)))).ToList();
        all.Sort(StringComparer.Ordinal);
        var page = all.Where(rel => after is null || string.CompareOrdinal(rel, after) > 0).Take(limit + 1).ToList();
        bool more = page.Count > limit;
        if (more) page.RemoveAt(limit);
        var files = new JsonArray();
        foreach (string rel in page)
        {
            string path = Path.Combine(cfg.PoolDir, rel);
            try
            {
                files.Add(new JsonObject { ["path"] = rel, ["size"] = new FileInfo(path).Length, ["sha256"] = Sha256(path) });
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // Gone or locked meanwhile: it's missing from this page (and said as such when it's asked for).
            }
        }
        return new JsonObject { ["files"] = files, ["total"] = all.Count, ["next"] = more ? page[^1] : null };
    }

    /// <summary>A file of the library folder <see cref="ExportFiles"/> lists, by its path there; null for anything
    /// else (a hidden file, a path that leaves the folder, one that isn't there).</summary>
    public static string? LibraryFile(Config cfg, string? rel) =>
        InLibrary(cfg.PoolDir, rel) is { } path && File.Exists(path) && !File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint) ? path : null;

    /// <summary>A page of the chats (chats/ beside config.toml), whole, in id order: <c>{chats, total, next}</c>.</summary>
    public static JsonObject ExportChats(string home, string? after, int limit = PageSize)
    {
        limit = Math.Clamp(limit, 1, 100);
        string dir = Path.Combine(home, "chats");
        var ids = Directory.Exists(dir)
            ? Directory.EnumerateFiles(dir, "*.json").Select(Path.GetFileNameWithoutExtension).OfType<string>().Where(Chats.GoodId).Order(StringComparer.Ordinal).ToList()
            : [];
        var page = ids.Where(id => after is null || string.CompareOrdinal(id, after) > 0).Take(limit + 1).ToList();
        bool more = page.Count > limit;
        if (more) page.RemoveAt(limit);
        var chats = new JsonArray();
        foreach (string id in page)
        {
            try
            {
                if (JsonNode.Parse(File.ReadAllText(Path.Combine(dir, id + ".json"))) is JsonObject chat) chats.Add(chat);
            }
            catch (Exception e) when (e is JsonException or IOException)
            {
                // A chat that can't be read stays where it is.
            }
        }
        return new JsonObject { ["chats"] = chats, ["total"] = ids.Count, ["next"] = more ? page[^1] : null };
    }

    // --- the new library's side: takes what came, as it came. What it has already is never touched. --------------

    /// <summary>
    /// The library's own part: classes this library hasn't got are added, in the old library's order (so each keeps
    /// its colour); a class it has already keeps its own name and what it says it covers, and learns the old one's
    /// other names too. A class's Canvas course is linked here when this library has no course for it yet (and Canvas
    /// is the same school's). How notes are written and sorted comes over only when asked for
    /// (<c>settings</c>) and this library's AI was never chosen: a library just made by "Use just this computer".
    /// Answers <c>{classes, courses, settings}</c>: what was added.
    /// </summary>
    public static JsonObject ImportLibrary(Config cfg, JsonObject body)
    {
        int classes = MergeClasses(cfg, body["classes"] as JsonArray);
        int courses = MergeCanvas(cfg, body["canvas"] as JsonObject);
        bool settings = body["settings"] is JsonValue sv && sv.TryGetValue(out bool want) && want && NeverChoseAi(cfg.Home)
            && TakeSettings(cfg, body["notes"] as JsonObject, body["ai"] as JsonObject);
        return new JsonObject { ["classes"] = classes, ["courses"] = courses, ["settings"] = settings };
    }

    /// <summary>Adds the classes this library hasn't got; one it has (by name, whatever the case) learns the other's
    /// other names, and what it covers when it said nothing. How many were added.</summary>
    static int MergeClasses(Config cfg, JsonArray? list)
    {
        int added = 0;
        bool changed = false;
        foreach (var node in list ?? [])
        {
            if (node is not JsonObject c || Text(c["name"]) is not { Length: > 0 and <= 60 } name) continue;
            if (name.Equals(Configs.Unsorted, StringComparison.OrdinalIgnoreCase)) continue;
            var aliases = (c["aliases"] as JsonArray ?? []).Select(Text).OfType<string>().Where(a => a.Length > 0).ToList();
            string about = Text(c["description"]) ?? "";
            var mine = cfg.Classes.FirstOrDefault(k => k.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (mine is null)
            {
                cfg.Classes.Add(new ClassDef(name, aliases, about));
                added++;
                changed = true;
                continue;
            }
            foreach (string a in aliases)
            {
                if (a.Equals(mine.Name, StringComparison.OrdinalIgnoreCase) || mine.Aliases.Contains(a, StringComparer.OrdinalIgnoreCase)) continue;
                mine.Aliases.Add(a);
                changed = true;
            }
            if (mine.Description.Length == 0 && about.Length > 0)
            {
                mine.Description = about;
                changed = true;
            }
        }
        if (changed) Configs.Save(cfg);
        return added;
    }

    /// <summary>Links each class to its Canvas course, where this library hasn't linked it (or that course) yet, and
    /// takes the Canvas address when it has none. A different school's Canvas links nothing: a course id means another
    /// course there. How many classes were linked.</summary>
    static int MergeCanvas(Config cfg, JsonObject? canvas)
    {
        if (canvas is null) return 0;
        string url = Text(canvas["url"]) ?? "";
        var courses = Read<Dictionary<string, long>>(canvas["courses"]) ?? [];
        var available = Read<Dictionary<string, string>>(canvas["available"]) ?? [];
        var info = Read<Dictionary<string, CourseInfo>>(canvas["course_info"]) ?? [];
        var chosen = Read<List<string>>(canvas["chosen"]);
        int poll = canvas["poll_minutes"] is JsonValue pv && pv.TryGetValue(out int p) && p > 0 ? p : 0;
        if (url.Length == 0) return 0;
        int linked = 0;
        var before = CanvasSettings.Load(cfg.Home);
        if (before.Url.Length > 0 && !before.Url.Equals(url, StringComparison.OrdinalIgnoreCase)) return 0;
        CanvasSettings.Update(cfg.Home, s =>
        {
            bool fresh = s.Url.Length == 0 && s.Courses.Count == 0;
            if (s.Url.Length == 0) s.Url = url;
            if (fresh && poll > 0) s.PollMinutes = poll;
            foreach (var kv in available) s.Available.TryAdd(kv.Key, kv.Value);
            foreach (var kv in info) s.CourseInfo.TryAdd(kv.Key, kv.Value);
            if (fresh) s.Chosen = chosen is null ? null : [.. chosen];
            foreach (var (cls, id) in courses)
            {
                string? mine = cfg.Classes.FirstOrDefault(c => c.Name.Equals(cls, StringComparison.OrdinalIgnoreCase))?.Name;
                if (mine is null || s.Courses.Keys.Any(k => k.Equals(mine, StringComparison.OrdinalIgnoreCase)) || s.Courses.ContainsValue(id)) continue;
                string key = id.ToString(CultureInfo.InvariantCulture);
                bool wasChosen = chosen is null || chosen.Contains(key);
                // A library whose every linked course was chosen (no list) gets one, so this course can be left out.
                if (!fresh && !wasChosen && s.Chosen is null) s.Chosen = [.. s.Courses.Values.Select(v => v.ToString(CultureInfo.InvariantCulture))];
                s.Courses[mine] = id;
                if (!fresh && wasChosen && s.Chosen is not null && !s.Chosen.Contains(key)) s.Chosen.Add(key);
                linked++;
            }
        });
        return linked;
    }

    /// <summary>This library's AI was never picked: no ai.json, or one still on the plain default.</summary>
    static bool NeverChoseAi(string home)
    {
        if (!File.Exists(AiSettings.PathIn(home))) return true;
        var ai = AiSettings.Load(home);
        return ai.Provider == "ollama" && ai.ByJob.Count == 0 && ai.Models.Count == 0;
    }

    /// <summary>How the old library wrote and sorted notes, and which AI did it, taken as this library's own.</summary>
    static bool TakeSettings(Config cfg, JsonObject? notes, JsonObject? ai)
    {
        if (notes is null && ai is null) return false;
        if (notes is not null)
        {
            if (Flag(notes["summary_enabled"]) is { } se) cfg.SummaryEnabled = se;
            if (Text(notes["summary_model"]) is { } sm) cfg.SummaryModel = sm;
            if (notes["summary_max_context"] is JsonValue mc && mc.TryGetValue(out int max) && max > 0) cfg.SummaryMaxContext = max;
            if (Flag(notes["ollama_enabled"]) is { } oe) cfg.OllamaEnabled = oe;
            if (Text(notes["ollama_host"]) is { Length: > 0 } oh) cfg.OllamaHost = oh;
            if (Text(notes["ollama_model"]) is { Length: > 0 } om) cfg.OllamaModel = om;
            if (notes["min_confidence"] is JsonValue c && c.TryGetValue(out double conf) && conf is >= 0 and <= 1) cfg.MinConfidence = conf;
            Configs.Save(cfg);
        }
        if (ai is not null)
        {
            var mine = AiSettings.Load(cfg.Home);
            if (Text(ai["provider"]) is { Length: > 0 } provider) mine.Provider = provider;
            mine.ByJob = Read<Dictionary<string, AiChoice>>(ai["by_job"]) ?? [];
            mine.Models = Read<Dictionary<string, string>>(ai["models"]) ?? [];
            if (Flag(ai["fallback"]) is { } fb) mine.Fallback = fb;
            if (Text(ai["terminal"]) is { Length: > 0 } terminal) mine.Terminal = terminal;
            mine.Save(cfg.Home);
        }
        return true;
    }

    /// <summary>Of <paramref name="ids"/>, the lectures this library hasn't got.</summary>
    public static JsonObject WantedLectures(Store store, JsonObject body)
    {
        var known = store.KnownIds();
        return new JsonObject { ["wanted"] = new JsonArray([.. Strings(body["ids"]).Where(id => !known.Contains(id)).Distinct().Select(id => (JsonNode)id)]) };
    }

    /// <summary>Files a page from <see cref="Export"/> here: classes this library hasn't got are added (see
    /// <see cref="ImportLibrary"/>), and each lecture it hasn't got is written as it was, its note file, notes and
    /// class included, under this library's own spelling of the class. A lecture the old library was still writing
    /// notes for waits here for this library to write them (its class, if a person picked it, kept). A lecture already
    /// here is never touched. Throws <see cref="PayloadException"/> for a page that isn't one.</summary>
    public static MoveResult Import(Store store, Config cfg, JsonObject page)
    {
        int classes = MergeClasses(cfg, page["classes"] as JsonArray);
        if (page["lectures"] is not JsonArray lectures) throw new PayloadException("a page needs its lectures");
        int filed = 0, skipped = 0, writing = 0;
        var known = store.KnownIds();
        foreach (var node in lectures)
        {
            if (node is not JsonObject l) throw new PayloadException("a lecture must be an object");
            var m = Wire.MeetingFromJson(l["meeting"]);
            if (!known.Add(m.Id))
            {
                skipped++;
                continue;
            }
            string cls = Text(l["class"]) is { Length: > 0 } named ? named : Configs.Unsorted;
            cls = cls.Equals(Configs.Unsorted, StringComparison.OrdinalIgnoreCase) ? Configs.Unsorted
                : cfg.Classes.FirstOrDefault(k => k.Name.Equals(cls, StringComparison.OrdinalIgnoreCase))?.Name ?? cls;
            double confidence = l["confidence"] is JsonValue v && v.TryGetValue(out double d) ? d : 0;
            var topics = (l["topics"] as JsonArray ?? []).Select(Text).OfType<string>().ToList();
            string by = Text(l["by"]) ?? "none";
            // A page from a library before this one has only filed lectures, and says nothing about it.
            if (Flag(l["filed"]) ?? true)
            {
                string? noteText = l["note_text"] is JsonValue nt && nt.TryGetValue(out string? raw) && raw.Length > 0 ? raw : null;
                store.Import(m, new Classification(cls, confidence, by, Text(l["lecture_title"]) ?? "", topics),
                    Text(l["summary"]) ?? "", Text(l["summary_model"]) ?? "", Text(l["error"]) ?? "", noteText);
                filed++;
            }
            else
            {
                store.Enqueue(m);
                if (by == "human" && cls != Configs.Unsorted) store.SetClass(m.Id, cls);
                writing++;
            }
        }
        return new MoveResult(filed, skipped, classes, Writing: writing);
    }

    /// <summary>Of the attachments named in <c>ids</c>, the ones this library hasn't got.</summary>
    public static JsonObject WantedAttachments(Store store, JsonObject body)
    {
        var known = store.AttachmentIds();
        return new JsonObject { ["wanted"] = new JsonArray([.. Strings(body["ids"]).Where(id => !known.Contains(id)).Distinct().Select(id => (JsonNode)id)]) };
    }

    /// <summary>An attachment from <see cref="ExportAttachments"/>, its file already on this disk at
    /// <paramref name="staged"/>: kept here with its id, words and all, in the class its lecture is in here (or the
    /// class it was in). Null when it's here already (the staged file is then the caller's to remove); otherwise the
    /// attachment as kept.</summary>
    public static Attachment? ImportAttachment(Store store, JsonObject meta, string staged)
    {
        if (Text(meta["id"]) is not { Length: > 0 and <= 64 } id) throw new PayloadException("an attachment needs its id");
        string? lecture = Text(meta["lecture"]) is { Length: > 0 } n ? n : null;
        string cls = lecture is not null && store.Get(lecture) is { ClassName: { Length: > 0 } here } ? here
            : Text(meta["class"]) is { Length: > 0 } c ? c : Configs.Unsorted;
        string name = Attachments.SafeName(Text(meta["name"]) ?? "attachment");
        long size = new FileInfo(staged).Length;
        // Words still being read there are read again here.
        string state = Text(meta["state"]) switch { Attachment.Read => Attachment.Read, Attachment.Unread => Attachment.Unread, _ => Attachment.Reading };
        var a = new Attachment(id, name, Text(meta["file"]) is { Length: > 0 } f ? f : name, cls, lecture, size,
            Text(meta["type"]) ?? "application/octet-stream", Text(meta["added"]) ?? "",
            meta["text"] is JsonValue tv && tv.TryGetValue(out string? text) ? text : "", state, Flag(meta["used"]) ?? false);
        return store.ImportAttachment(a, staged) ? store.GetAttachment(id) : null;
    }

    /// <summary>Of the files listed (path, size, SHA-256), the ones this library hasn't got a copy of: not at that
    /// path, nor beside it under a numbered name. A path that isn't a plain one inside the library is never wanted.</summary>
    public static JsonObject WantedFiles(Config cfg, JsonObject body)
    {
        var wanted = new JsonArray();
        foreach (var node in body["files"] as JsonArray ?? [])
        {
            if (node is not JsonObject f || Text(f["path"]) is not { } rel || InLibrary(cfg.PoolDir, rel) is null) continue;
            long size = f["size"] is JsonValue sv && sv.TryGetValue(out long n) ? n : -1;
            string sha = Text(f["sha256"]) ?? "";
            if (CopyOf(cfg.PoolDir, rel, size, sha) is null) wanted.Add(rel);
        }
        return new JsonObject { ["wanted"] = wanted };
    }

    /// <summary>
    /// A file of the old library's folder, coming in as <paramref name="body"/>: written at its path here, or, when
    /// that's taken by a different file, beside it as "name (2).ext" (never over anything). It has to arrive whole:
    /// its SHA-256 must be <paramref name="sha256"/>. Returns where it went (relative to the library folder), or null
    /// when a copy of it was here already. Throws <see cref="PayloadException"/> for a path that isn't a plain one
    /// inside the library, or a file that didn't arrive whole.
    /// </summary>
    public static async Task<string?> ImportFileAsync(Config cfg, string? rel, string? sha256, Stream body, CancellationToken ct = default)
    {
        if (rel is null || InLibrary(cfg.PoolDir, rel) is null) throw new PayloadException("that isn't a file in the library");
        if (sha256 is not { Length: 64 }) throw new PayloadException("a file needs its SHA-256");
        string dir = Path.Combine(cfg.PoolDir, Incoming);
        Directory.CreateDirectory(dir);
        string staged = Path.Combine(dir, Guid.NewGuid().ToString("N"));
        try
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            long size = 0;
            await using (var file = new FileStream(staged, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                byte[] buffer = new byte[81920];
                int n;
                while ((n = await body.ReadAsync(buffer, ct)) > 0)
                {
                    hash.AppendData(buffer, 0, n);
                    await file.WriteAsync(buffer.AsMemory(0, n), ct);
                    size += n;
                }
            }
            if (!Convert.ToHexStringLower(hash.GetHashAndReset()).Equals(sha256, StringComparison.OrdinalIgnoreCase))
                throw new PayloadException("the file didn't arrive whole: try again");
            if (CopyOf(cfg.PoolDir, rel, size, sha256) is not null) return null;
            foreach (string candidate in Candidates(rel))
            {
                string path = InLibrary(cfg.PoolDir, candidate)!;
                if (File.Exists(path) || Directory.Exists(path)) continue;
                Directory.CreateDirectory(Py.Parent(path));
                try
                {
                    File.Move(staged, path, overwrite: false);
                    return candidate;
                }
                catch (IOException) when (File.Exists(path))
                {
                    // Taken just now: the next name.
                }
            }
            throw new PayloadException("there's no free name for that file");
        }
        finally
        {
            try
            {
                if (File.Exists(staged)) File.Delete(staged);
                if (Directory.Exists(dir) && !Directory.EnumerateFileSystemEntries(dir).Any()) Directory.Delete(dir);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    /// <summary>Keeps the chats this library hasn't got (by id). A chat's AI conversation lived on the old library's
    /// computer, so a follow-up here starts a new one from what was said (its session is cleared).</summary>
    public static MoveResult ImportChats(string home, JsonObject page)
    {
        int filed = 0, skipped = 0;
        string dir = Path.Combine(home, "chats");
        foreach (var node in page["chats"] as JsonArray ?? [])
        {
            if (node is not JsonObject chat || Text(chat["id"]) is not { } id || !Chats.GoodId(id)) continue;
            string path = Path.Combine(dir, id + ".json");
            if (File.Exists(path))
            {
                skipped++;
                continue;
            }
            var copy = (JsonObject)chat.DeepClone();
            copy["session"] = "";
            Directory.CreateDirectory(dir);
            File.WriteAllText(path, copy.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            filed++;
        }
        return new MoveResult(0, skipped, 0, Chats: filed);
    }

    // --- files, paths and hashes ------------------------------------------------------------------------------------

    static StringComparer PathComparer => OperatingSystem.IsLinux() ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase;

    static string Full(string path) => Path.GetFullPath(path);

    /// <summary>Every file in the library folder, relative, with "/" between folders: hidden files and folders (the
    /// AI's history, a file on its way in) and links left out.</summary>
    static List<string> LibraryFiles(string root)
    {
        var found = new List<string>();
        if (!Directory.Exists(root)) return found;
        var dirs = new Stack<string>();
        dirs.Push(root);
        while (dirs.Count > 0)
        {
            string dir = dirs.Pop();
            IEnumerable<string> entries;
            try
            {
                entries = Directory.EnumerateFileSystemEntries(dir).ToList();
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                continue;
            }
            foreach (string entry in entries)
            {
                if (Path.GetFileName(entry).StartsWith('.')) continue;
                FileAttributes attributes;
                try
                {
                    attributes = File.GetAttributes(entry);
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    continue;
                }
                if (attributes.HasFlag(FileAttributes.ReparsePoint)) continue;
                if (attributes.HasFlag(FileAttributes.Directory)) dirs.Push(entry);
                else found.Add(Path.GetRelativePath(root, entry).Replace('\\', '/'));
            }
        }
        return found;
    }

    /// <summary>The full path of <paramref name="rel"/> ("CS 101/Notes/a.md") inside <paramref name="root"/>, or null
    /// when it isn't a plain path there: empty, rooted, with a "..", "." or hidden part, or a character a disk
    /// refuses.</summary>
    static string? InLibrary(string root, string? rel)
    {
        if (string.IsNullOrWhiteSpace(rel) || rel.Length > 1000 || rel.Contains('\\') || rel.StartsWith('/') || Path.IsPathRooted(rel)) return null;
        var bad = Path.GetInvalidFileNameChars();
        foreach (string part in rel.Split('/'))
            if (part.Length == 0 || part.StartsWith('.') || part.IndexOfAny(bad) >= 0 || part.Contains(':') || part.Trim().Length == 0) return null;
        string full = Path.GetFullPath(Path.Combine(root, rel.Replace('/', Path.DirectorySeparatorChar)));
        string top = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return full.StartsWith(top, OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase) ? full : null;
    }

    /// <summary>The names a file may take here, in order: its own, then "name (2).ext", "name (3).ext"…</summary>
    static IEnumerable<string> Candidates(string rel)
    {
        yield return rel;
        int slash = rel.LastIndexOf('/');
        string folder = slash < 0 ? "" : rel[..(slash + 1)], name = rel[(slash + 1)..];
        string ext = Path.GetExtension(name), stem = name[..^ext.Length];
        for (int i = 2; i < 1000; i++) yield return $"{folder}{stem} ({i}){ext}";
    }

    /// <summary>Where a copy of the file (this size and SHA-256) already is: at its path, or beside it under a
    /// numbered name. Null when there's none.</summary>
    static string? CopyOf(string root, string rel, long size, string sha256)
    {
        foreach (string candidate in Candidates(rel))
        {
            string? path = InLibrary(root, candidate);
            if (path is null || !File.Exists(path)) return null;
            try
            {
                if (new FileInfo(path).Length == size && Sha256(path).Equals(sha256, StringComparison.OrdinalIgnoreCase)) return candidate;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
            }
        }
        return null;
    }

    static string Sha256(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 81920);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }

    static string? Text(JsonNode? node) => node is JsonValue v && v.TryGetValue(out string? s) ? s.Trim() : null;

    static bool? Flag(JsonNode? node) => node is JsonValue v && v.TryGetValue(out bool b) ? b : null;

    static IEnumerable<string> Strings(JsonNode? node) => (node as JsonArray ?? []).Select(Text).OfType<string>().Where(s => s.Length > 0);

    static T? Read<T>(JsonNode? node) where T : class
    {
        if (node is null) return null;
        try
        {
            return node.Deserialize<T>(Snake);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // --- the app in between -------------------------------------------------------------------------------------

    /// <summary>
    /// Copies everything from the library <paramref name="from"/> answers at into the one <paramref name="to"/> answers
    /// at (each client's BaseAddress is its library), part by part, a page at a time, saying how far it's got: the
    /// library's classes and Canvas courses (and, with <paramref name="takeSettings"/>, how it writes notes), its
    /// lectures, the attached files, the library folder's other files, and the chats. Only what the new library hasn't
    /// got travels, so doing it again after a failure picks up where it stopped. A library from before this version
    /// hands over (or takes) its lectures only: the rest is left for when it's updated (<see cref="MoveResult.Partial"/>).
    /// Throws <see cref="InvalidOperationException"/> with a sentence for the student when either library says no: the
    /// old one can't be reached, a password is wrong, or it runs a Study Stash too old to hand lectures over. Whatever
    /// came before that stays.
    /// </summary>
    public static async Task<MoveResult> CopyAsync(HttpClient from, string fromKey, HttpClient to, string toKey,
        Action<MoveProgress>? progress = null, CancellationToken ct = default, bool takeSettings = false)
    {
        var old = new Side(from, fromKey, "your old library");
        var here = new Side(to, toKey, "this library");
        int filed = 0, skipped = 0, classes = 0, writing = 0, files = 0, missed = 0, chats = 0;
        bool partial = false;

        // The library itself first, so its classes come in its order, with their Canvas courses.
        var library = await AskAsync(old, HttpMethod.Get, LibraryRoute, null, ct, optional: true);
        if (library is null) partial = true;
        else
        {
            library["settings"] = takeSettings;
            var took = await AskAsync(here, HttpMethod.Post, LibraryRoute, Json(library), ct, optional: true);
            if (took is null) partial = true;
            else classes += Count(took, "classes");
        }

        // The lectures: which ones this page has, which of those the new library wants, then just those, whole.
        string? after = null;
        bool lean = true, choose = true;
        while (true)
        {
            string query = $"{Route}?limit={PageSize}" + (lean ? "&lean=1" : "") + (after is null ? "" : "&after=" + Uri.EscapeDataString(after));
            var page = (await AskAsync(old, HttpMethod.Get, query, null, ct))!;
            var items = page["lectures"] as JsonArray ?? [];
            // A library from before lean pages sends every lecture whole: those go in as they are.
            if (items.Any(i => i is JsonObject o && o.ContainsKey("meeting"))) lean = false;
            JsonObject? full = lean ? null : page;
            if (lean)
            {
                var ids = items.Select(i => Text(i?["id"])).OfType<string>().ToList();
                List<string> wanted = ids;
                if (choose && ids.Count > 0)
                {
                    var answer = await AskAsync(here, HttpMethod.Post, Route + Wanted, Json(new JsonObject { ["ids"] = new JsonArray([.. ids.Select(i => (JsonNode)i)]) }), ct, optional: true);
                    if (answer is null) choose = false; // a library before this one: it's sent everything, and skips what it has
                    else wanted = [.. Strings(answer["wanted"])];
                }
                skipped += ids.Count - wanted.Count;
                if (wanted.Count > 0)
                    full = await AskAsync(old, HttpMethod.Get, Route + "?" + string.Join("&", wanted.Select(id => "id=" + Uri.EscapeDataString(id))), null, ct);
            }
            if (full is not null && (full["lectures"] as JsonArray)?.Count > 0)
            {
                var result = (await AskAsync(here, HttpMethod.Post, Route, Json(full), ct))!;
                filed += Count(result, "filed");
                skipped += Count(result, "skipped");
                classes += Count(result, "classes");
                writing += Count(result, "writing");
            }
            int total = Count(page, "total");
            progress?.Invoke(new MoveProgress(Math.Min(filed + skipped + writing, total), total));
            after = Text(page["next"]);
            if (after is null) break;
        }

        // The files attached to lectures and classes.
        var attached = await CopyAttachmentsAsync(old, here, progress, ct);
        files += attached.Files;
        missed += attached.Missed;
        partial |= attached.Partial;

        // The library folder's other files: captured notes, the Inbox, the Canvas mirror, anything put there by hand.
        var other = await CopyFilesAsync(old, here, progress, ct);
        files += other.Files;
        missed += other.Missed;
        partial |= other.Partial;

        // The chats.
        after = null;
        while (true)
        {
            var page = await AskAsync(old, HttpMethod.Get, $"{ChatsRoute}?limit={PageSize}" + (after is null ? "" : "&after=" + Uri.EscapeDataString(after)), null, ct, optional: true);
            if (page is null)
            {
                partial = true;
                break;
            }
            if ((page["chats"] as JsonArray)?.Count > 0)
            {
                var result = await AskAsync(here, HttpMethod.Post, ChatsRoute, Json(page), ct, optional: true);
                if (result is null)
                {
                    partial = true;
                    break;
                }
                chats += Count(result, "chats");
            }
            after = Text(page["next"]);
            if (after is null) break;
        }

        return new MoveResult(filed, skipped, classes, files, missed, writing, chats, partial);
    }

    sealed record Side(HttpClient Http, string Key, string Who);

    sealed record Part(int Files, int Missed, bool Partial);

    static async Task<Part> CopyAttachmentsAsync(Side old, Side here, Action<MoveProgress>? progress, CancellationToken ct)
    {
        int done = 0, files = 0, missed = 0;
        string? after = null;
        while (true)
        {
            var page = await AskAsync(old, HttpMethod.Get, $"{AttachmentsRoute}?limit={PageSize}" + (after is null ? "" : "&after=" + Uri.EscapeDataString(after)), null, ct, optional: true);
            if (page is null) return new Part(files, missed, true);
            var list = (page["attachments"] as JsonArray ?? []).OfType<JsonObject>().ToList();
            int total = Count(page, "total");
            if (list.Count > 0)
            {
                var answer = await AskAsync(here, HttpMethod.Post, AttachmentsRoute + Wanted,
                    Json(new JsonObject { ["ids"] = new JsonArray([.. list.Select(a => (JsonNode?)Text(a["id"]))]) }), ct, optional: true);
                if (answer is null) return new Part(files, missed, true);
                var wanted = Strings(answer["wanted"]).ToHashSet();
                foreach (var meta in list)
                {
                    done++;
                    if (Text(meta["id"]) is { } id && wanted.Contains(id))
                    {
                        int? kept = await PassOnAsync(old, $"/api/v2/attachments/{Uri.EscapeDataString(id)}/raw", here, HttpMethod.Post,
                            $"{AttachmentsRoute}/{Uri.EscapeDataString(id)}", body =>
                            {
                                var form = new MultipartFormDataContent();
                                form.Add(new StringContent(meta.ToJsonString(), Encoding.UTF8, "application/json"), "meta");
                                form.Add(body, "file", "file");
                                return form;
                            }, ct);
                        if (kept is null) missed++;
                        else files += kept.Value;
                    }
                    progress?.Invoke(new MoveProgress(Math.Min(done, total), total, AttachmentsStage));
                }
            }
            after = Text(page["next"]);
            if (after is null) return new Part(files, missed, false);
        }
    }

    static async Task<Part> CopyFilesAsync(Side old, Side here, Action<MoveProgress>? progress, CancellationToken ct)
    {
        int done = 0, files = 0, missed = 0;
        string? after = null;
        while (true)
        {
            var page = await AskAsync(old, HttpMethod.Get, $"{FilesRoute}?limit=50" + (after is null ? "" : "&after=" + Uri.EscapeDataString(after)), null, ct, optional: true);
            if (page is null) return new Part(files, missed, true);
            var list = (page["files"] as JsonArray ?? []).OfType<JsonObject>().ToList();
            int total = Count(page, "total");
            if (list.Count > 0)
            {
                var answer = await AskAsync(here, HttpMethod.Post, FilesRoute + Wanted, Json(new JsonObject { ["files"] = new JsonArray([.. list.Select(f => f.DeepClone())]) }), ct, optional: true);
                if (answer is null) return new Part(files, missed, true);
                var wanted = Strings(answer["wanted"]).ToHashSet(StringComparer.Ordinal);
                foreach (var f in list)
                {
                    done++;
                    if (Text(f["path"]) is { } rel && wanted.Contains(rel))
                    {
                        int? kept = await PassOnAsync(old, $"{FilesRoute}/raw?path={Uri.EscapeDataString(rel)}", here, HttpMethod.Put,
                            $"{FilesRoute}?path={Uri.EscapeDataString(rel)}&sha256={Uri.EscapeDataString(Text(f["sha256"]) ?? "")}", body => body, ct);
                        if (kept is null) missed++;
                        else files += kept.Value;
                    }
                    progress?.Invoke(new MoveProgress(Math.Min(done, total), total, FilesStage));
                }
            }
            after = Text(page["next"]);
            if (after is null) return new Part(files, missed, false);
        }
    }

    /// <summary>A file from the old library passed straight on to the new one as it arrives (never all in memory):
    /// <paramref name="wrap"/> puts its body in the request. How many files the new library kept (0: it had it), or
    /// null when the old library hasn't got it any more. The old library stopping part-way through says so, whichever
    /// side notices first; the new library drops what it got of it.</summary>
    static async Task<int?> PassOnAsync(Side old, string from, Side here, HttpMethod method, string to, Func<HttpContent, HttpContent> wrap, CancellationToken ct)
    {
        using var file = await OpenAsync(old, from, ct);
        if (file is null) return null;
        var source = new Relay(await file.Content.ReadAsStreamAsync(ct));
        var body = new StreamContent(source);
        body.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        try
        {
            return Count((await AskAsync(here, method, to, wrap(body), ct))!, "files");
        }
        catch (InvalidOperationException e) when (source.Broke)
        {
            throw new InvalidOperationException($"Can't reach {old.Who}. Is its computer on, and is Tailscale connected?", e);
        }
    }

    /// <summary>A file's body on its way through: remembers whether reading it failed (the old library stopped
    /// sending), so that isn't blamed on the new one.</summary>
    sealed class Relay(Stream inner) : Stream
    {
        public bool Broke { get; private set; }

        public override int Read(byte[] buffer, int offset, int count)
        {
            try
            {
                return inner.Read(buffer, offset, count);
            }
            catch (Exception) when (Mark())
            {
                throw;
            }
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            try
            {
                return await inner.ReadAsync(buffer, ct);
            }
            catch (Exception) when (Mark())
            {
                throw;
            }
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct) => ReadAsync(buffer.AsMemory(offset, count), ct).AsTask();

        bool Mark()
        {
            Broke = true;
            return true;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing) inner.Dispose();
            base.Dispose(disposing);
        }
    }

    static StringContent Json(JsonObject body) => new(body.ToJsonString(), Encoding.UTF8, "application/json");

    static int Count(JsonObject o, string key) => o[key] is JsonValue v && v.TryGetValue(out int n) ? n : 0;

    /// <summary>A file from the old library, its body not read yet (it's passed straight on). Null when the old
    /// library hasn't got it any more.</summary>
    static async Task<HttpResponseMessage?> OpenAsync(Side side, string path, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + side.Key);
        HttpResponseMessage r;
        try
        {
            r = await side.Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            throw new InvalidOperationException($"Can't reach {side.Who}. Is its computer on, and is Tailscale connected?", e);
        }
        if (r.StatusCode == HttpStatusCode.NotFound)
        {
            r.Dispose();
            return null;
        }
        if (r.IsSuccessStatusCode) return r;
        var status = r.StatusCode;
        r.Dispose();
        if (status == HttpStatusCode.Unauthorized) throw new InvalidOperationException($"The password for {side.Who} isn't right.");
        throw new InvalidOperationException($"{Capital(side.Who)} didn't hand a file over ({(int)status}). Try again in a moment.");
    }

    /// <summary>Asks one of the libraries. <paramref name="optional"/>: a library from before this route answers null
    /// (it's older) instead of stopping everything.</summary>
    static async Task<JsonObject?> AskAsync(Side side, HttpMethod method, string path, HttpContent? body, CancellationToken ct, bool optional = false)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + side.Key);
        if (body is not null) request.Content = body;
        HttpResponseMessage r;
        try
        {
            r = await side.Http.SendAsync(request, ct);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            throw new InvalidOperationException($"Can't reach {side.Who}. Is its computer on, and is Tailscale connected?", e);
        }
        using (r)
        {
            string text = await r.Content.ReadAsStringAsync(ct);
            if (r.StatusCode == HttpStatusCode.Unauthorized) throw new InvalidOperationException($"The password for {side.Who} isn't right.");
            if (r.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed)
            {
                if (optional) return null;
                throw new InvalidOperationException($"{Capital(side.Who)} runs an older Study Stash. Update it, then bring your lectures over.");
            }
            if (!r.IsSuccessStatusCode)
                throw new InvalidOperationException($"{Capital(side.Who)} didn't take it ({(int)r.StatusCode}). Try again in a moment.");
            try
            {
                return JsonNode.Parse(text) as JsonObject ?? throw new InvalidOperationException($"{Capital(side.Who)} answered oddly. Try again in a moment.");
            }
            catch (JsonException e)
            {
                throw new InvalidOperationException($"{Capital(side.Who)} answered oddly. Try again in a moment.", e);
            }
        }
    }

    static string Capital(string s) => char.ToUpper(s[0], CultureInfo.InvariantCulture) + s[1..];
}
