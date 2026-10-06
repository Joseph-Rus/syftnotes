using System.Globalization;
using System.Text.Json.Nodes;

namespace StudyStash.Core.Canvas;

/// <summary>Where the course scout stands on one class, for <see cref="CanvasView.Classes"/>.</summary>
public readonly record struct ScoutState(string State, int Files, string When, string Report);

/// <summary>
/// Builds every JSON answer the app's Canvas screens and Claude's tools read, from Settings, each class's saved
/// index, the crawl's live state and the course scout. Nothing here talks to Canvas; <c>Library/LibraryWeb.Canvas.cs</c>
/// maps these onto routes and nothing more, so <c>LocalLibrary</c> (Claude's tools) can call the same builders.
/// </summary>
public static class CanvasView
{
    /// <summary>A date for the JSON: the saved ISO text, or null when there isn't one. Never "": no date reader takes
    /// it, and one blank date used to break the app's whole Canvas screen (a library that never synced has several).</summary>
    public static JsonNode? When(string? iso) => string.IsNullOrWhiteSpace(iso) ? null : iso;

    // --- state ---------------------------------------------------------------------------------------------------

    /// <summary>How things stand with Canvas, in priority order: not set up, no extension has ever checked in with
    /// this library's current key (one with an old key, or from before keys were written down, doesn't count), Chrome
    /// isn't signed in, Chrome isn't checking in now (<see cref="CanvasSettings.ExtensionConnected"/>), a sync is
    /// running, the last sync had an error, or all is well.</summary>
    public static string StateOf(CanvasSettings s, bool syncing, DateTimeOffset now)
    {
        if (!s.On) return "not_set_up";
        if (s.SeenWithKey.Length == 0) return "no_extension";
        if (s.NeedsLogin) return "signed_out";
        if (!s.ExtensionConnected(now)) return "chrome_away";
        if (syncing) return "syncing";
        if (s.Error.Length > 0) return "error";
        return "connected";
    }

    /// <summary>Which classes this sync hasn't finished yet (still <c>reading</c> some listing, or not started at
    /// all), from the crawl's live section states.</summary>
    static List<string> Left(IReadOnlyDictionary<string, long> courses, IReadOnlyDictionary<string, Dictionary<string, string>> sections)
    {
        bool Done(string cls) => sections.TryGetValue(cls, out var mine) && Crawl.Listings.All(l => mine.TryGetValue(l, out var state) && state is "ok" or "hidden" or "failed");
        return courses.Keys.Where(cls => !Done(cls)).ToList();
    }

    /// <summary>The state, for the app's Canvas screens. <c>extension.browser</c> is the browser that checked in last
    /// ("Chrome", "Firefox"; "" before 1.6); <c>extension.connected</c> is a Chrome checking in now with
    /// this library's current key; <c>seen</c> is when one last did (null: never); <c>key_matches</c> says whether the
    /// Chrome that checked in last used that key (false: it has an old one, so it needs connecting again); and
    /// <c>refused_at</c> is when a Chrome with another key last knocked (<paramref name="refusedAt"/>).</summary>
    public static JsonObject State(CanvasSync sync, DateTimeOffset now, string? refusedAt = null)
    {
        var s = sync.Settings;
        bool active = sync.Crawl.Active;
        var left = active ? Left(s.Synced, sync.Crawl.Sections) : [];
        string? host = Uri.TryCreate(s.Url, UriKind.Absolute, out var u) ? u.Host : null;
        var warnings = new JsonArray();
        if (s.ExtensionOutdated) warnings.Add($"The extension in {s.BrowserName()} is older than this library's; open its extensions page and reload it.");
        return new JsonObject
        {
            ["state"] = StateOf(s, active, now),
            ["school"] = host,
            ["url"] = s.Url,
            ["extension"] = new JsonObject
            {
                ["seen"] = When(s.SeenWithKey), ["version"] = s.ExtensionVersion, ["latest"] = Extension.Version(), ["outdated"] = s.ExtensionOutdated,
                ["connected"] = s.ExtensionConnected(now), ["key_matches"] = s.LastKeyMatches, ["last_seen"] = When(s.ExtensionSeen),
                ["browser"] = s.ExtensionBrowser,
                ["refused_at"] = When(refusedAt),
                ["updated"] = s.ExtensionUpdate is { Dismissed: false } up ? new JsonObject { ["from"] = up.From, ["to"] = up.To, ["at"] = When(up.At) } : null,
            },
            ["last_sync"] = When(s.LastDone),
            ["revision"] = Revision(s),
            ["next_sync"] = s.LastDone.Length > 0 && DateTimeOffset.TryParse(s.LastDone, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var done)
                ? done.AddMinutes(s.PollMinutes).ToString("o", CultureInfo.InvariantCulture) : null,
            ["poll_minutes"] = s.PollMinutes,
            ["syncing"] = active ? new JsonObject { ["left"] = left.Count, ["total"] = s.Synced.Count, ["classes"] = new JsonArray(left.Select(c => (JsonNode)c).ToArray()) } : null,
            ["paused_until"] = sync.Crawl.PausedUntil?.ToString("o", CultureInfo.InvariantCulture),
            ["error"] = s.Error.Length > 0 ? new JsonObject { ["text"] = s.Error, ["at"] = When(s.ErrorAt) } : null,
            ["warnings"] = warnings,
        };
    }

    /// <summary>Changes whenever what the app shows from Canvas may have: a sync finished, or a course was linked,
    /// unlinked, chosen or dropped. The app reloads its Due list and class pages when it changes.</summary>
    public static string Revision(CanvasSettings s)
    {
        string all = string.Join("\n", [s.LastDone, .. s.Courses.OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => $"{kv.Key}={kv.Value}"),
            "chosen:" + (s.Chosen is null ? "all" : string.Join(",", s.Chosen.Order(StringComparer.Ordinal)))]);
        return Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(all)))[..16];
    }

    // --- classes ---------------------------------------------------------------------------------------------------

    public static JsonObject ClassRow(string cls, CanvasSettings s, CourseIndex? index, ScoutState scout, DateTimeOffset now)
    {
        long? id = s.Courses.TryGetValue(cls, out long cid) ? cid : null;
        var suggested = id is null ? CourseMatch.Suggest(cls, s.Available, s.CourseInfo) : null;
        var titles = CourseNames.Of(s);
        string idText = id?.ToString(CultureInfo.InvariantCulture) ?? "";
        var info = s.CourseInfo.GetValueOrDefault(idText);
        string title = titles.GetValueOrDefault(idText) ?? (index is { Name.Length: > 0 } ? CourseNames.Title(index.Name, index.Code) : "");
        string shortCode = CourseNames.ShortCode(index?.Code is { Length: > 0 } c ? c : info?.Code ?? "", index?.Name ?? info?.Name ?? "");
        var done_ = index?.Assignments.Select(a => Assignments.From(cls, a, now).Done).ToList() ?? [];
        int toHandIn = done_.Count(d => !d), done = done_.Count(d => d);
        int newAnnouncements = index?.Announcements.Count(a => !a.ReadOnCanvas) ?? 0;
        return new JsonObject
        {
            ["class"] = cls, ["linked"] = id is not null,
            ["canvas"] = id is null ? null : new JsonObject
            {
                ["id"] = id, ["code"] = index?.Code ?? "", ["name"] = index?.Name ?? "", ["term"] = index?.Term ?? "", ["url"] = index?.HtmlUrl ?? "",
                ["title"] = title, ["short_code"] = shortCode,
            },
            ["suggested"] = suggested is { } sug ? new JsonObject { ["id"] = sug.Id, ["name"] = titles.GetValueOrDefault(sug.Id) ?? sug.Name } : null,
            ["last_sync"] = When(index?.SyncedAt),
            ["counts"] = new JsonObject
            {
                ["to_hand_in"] = toHandIn, ["done"] = done, ["modules"] = index?.Modules.Count ?? 0, ["files"] = index?.Files.Count ?? 0,
                ["announcements"] = index?.Announcements.Count ?? 0, ["announcements_new"] = newAnnouncements,
            },
            ["files_hidden"] = index?.FilesHidden ?? false,
            ["scout"] = new JsonObject { ["state"] = scout.State, ["files"] = scout.Files, ["when"] = When(scout.When), ["report"] = scout.Report },
        };
    }

    public static JsonArray Classes(IReadOnlyList<string> classNames, CanvasSettings s, string home, Func<string, ScoutState> scoutOf, DateTimeOffset now) =>
        new(classNames.Select(cls => (JsonNode)ClassRow(cls, s, CourseIndex.Load(home, cls), scoutOf(cls), now)).ToArray());

    // --- assignments and the Due list ---------------------------------------------------------------------------

    /// <summary>One assignment (or quiz/discussion folded into one), as the app's lists show it.</summary>
    public static JsonObject ItemJson(Assignment a, string? folder) => new()
    {
        ["class"] = a.ClassName, ["id"] = a.Id, ["name"] = a.Name, ["kind"] = a.Kind, ["due"] = When(a.Due), ["due_at"] = When(a.DueAt),
        ["points"] = a.Points, ["status"] = a.Status, ["label"] = Assignments.Label(a.Status, a.Late), ["score"] = a.Score,
        ["grade"] = a.Grade, ["score_text"] = Assignments.ScoreText(a), ["late"] = a.Late, ["missing"] = a.Missing,
        ["excused"] = a.Excused, ["submitted"] = When(a.Submitted), ["graded_at"] = When(a.GradedAt),
        // When the student ticked it off in Canvas's planner (the flag is the status "marked_done"); null otherwise.
        ["marked_done"] = a.MarkedDone ? When(a.MarkedDoneAt) : null,
        ["url"] = a.Url, ["folder"] = folder,
    };

    static DateTime? DueOf(Assignment a) => a.Due.Length > 0 ? DateTime.Parse(a.Due, CultureInfo.InvariantCulture) : null;

    /// <summary>When the student last did something about it (submitted, or it was graded), local time; null for
    /// work that's never been touched.</summary>
    static DateTime? RecentOf(Assignment a, TimeZoneInfo zone)
    {
        DateTime? submitted = a.Submitted.Length > 0 ? DateTime.Parse(a.Submitted, CultureInfo.InvariantCulture) : null;
        DateTime? graded = a.GradedAt.Length > 0 && DateTimeOffset.TryParse(a.GradedAt, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var g)
            ? TimeZoneInfo.ConvertTime(g, zone).DateTime : null;
        DateTime? markedDone = a.MarkedDoneAt.Length > 0 && DateTimeOffset.TryParse(a.MarkedDoneAt, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var m)
            ? TimeZoneInfo.ConvertTime(m, zone).DateTime : null;
        DateTime? best = submitted is null ? graded : graded is null ? submitted : submitted > graded ? submitted : graded;
        return best is null ? markedDone : markedDone is null ? best : markedDone > best ? markedDone : best;
    }

    /// <summary>The Due list across every class: overdue, due this week, later, with no due date, and handed in in
    /// the last week (newest first). <paramref name="folderOf"/> looks up where an assignment's files are.</summary>
    public static JsonObject Due(IReadOnlyList<Assignment> all, string synced, DateTimeOffset nowOffset, TimeZoneInfo zone, Func<string, long, string?> folderOf)
    {
        var now = TimeZoneInfo.ConvertTime(nowOffset, zone).DateTime;
        bool Overdue(Assignment a) => !a.Done && DueOf(a) is { } d && d < now;
        bool Week(Assignment a) => !a.Done && DueOf(a) is { } d && d >= now && d < now.Date.AddDays(8);
        bool Later(Assignment a) => !a.Done && DueOf(a) is { } d && d >= now.Date.AddDays(8);
        bool Undated(Assignment a) => !a.Done && DueOf(a) is null;
        bool HandedIn(Assignment a) => a.Done && RecentOf(a, zone) is { } r && r >= now.AddDays(-7);

        JsonNode Row(Assignment a) => ItemJson(a, folderOf(a.ClassName, a.Id));
        var groupDefs = new (string Key, string Label, Func<Assignment, bool> In)[]
        {
            ("overdue", "Overdue", Overdue), ("week", "This week", Week), ("later", "Later", Later),
            ("undated", "No due date", Undated), ("handed_in", "Handed in", HandedIn),
        };
        var groups = new JsonArray();
        var toHandIn = new List<Assignment>();
        foreach (var (key, label, matches) in groupDefs)
        {
            var items = all.Where(matches).ToList();
            items = key == "handed_in"
                ? items.OrderByDescending(a => RecentOf(a, zone)).ToList()
                : items.OrderBy(a => DueOf(a) ?? DateTime.MaxValue).ThenBy(a => a.Name, StringComparer.Ordinal).ToList();
            if (key != "handed_in") toHandIn.AddRange(items);
            groups.Add(new JsonObject { ["key"] = key, ["label"] = label, ["items"] = new JsonArray(items.Select(Row).ToArray()) });
        }
        var next = toHandIn.Where(a => DueOf(a) is not { } d || d >= now).OrderBy(a => DueOf(a) ?? DateTime.MaxValue).FirstOrDefault();
        return new JsonObject { ["synced"] = When(synced), ["to_hand_in"] = toHandIn.Count, ["next"] = next is null ? null : Row(next), ["groups"] = groups };
    }

    /// <summary>One class's assignments: still to hand in (soonest first), and done (most recently due first).</summary>
    public static JsonObject ForClass(string cls, IReadOnlyList<Assignment> mine, Func<long, string?> folderOf)
    {
        var toHandIn = mine.Where(a => !a.Done).OrderBy(a => DueOf(a) ?? DateTime.MaxValue).ThenBy(a => a.Name, StringComparer.Ordinal).ToList();
        var done = mine.Where(a => a.Done).OrderByDescending(a => DueOf(a) ?? DateTime.MinValue).ToList();
        JsonNode Row(Assignment a) => ItemJson(a, folderOf(a.Id));
        return new JsonObject
        {
            ["class"] = cls, ["to_hand_in"] = new JsonArray(toHandIn.Select(Row).ToArray()), ["done"] = new JsonArray(done.Select(Row).ToArray()),
        };
    }

    // --- one assignment's detail --------------------------------------------------------------------------------

    /// <summary>A rough shape for a file, from its content type or name, when nothing better is known yet.</summary>
    public static string FormatOf(string contentType, string name) => contentType switch
    {
        _ when contentType.Contains("pdf", StringComparison.OrdinalIgnoreCase) => "PDF",
        _ when contentType.Contains("word", StringComparison.OrdinalIgnoreCase) || contentType.Contains("wordprocessingml", StringComparison.OrdinalIgnoreCase) => "Word",
        _ when contentType.Contains("presentation", StringComparison.OrdinalIgnoreCase) || contentType.Contains("powerpoint", StringComparison.OrdinalIgnoreCase) => "PowerPoint",
        _ when contentType.Contains("sheet", StringComparison.OrdinalIgnoreCase) || contentType.Contains("excel", StringComparison.OrdinalIgnoreCase) => "Excel",
        _ when contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase) => "Image",
        _ when contentType.Contains("zip", StringComparison.OrdinalIgnoreCase) => "ZIP",
        _ when contentType.StartsWith("text/", StringComparison.OrdinalIgnoreCase) => "Text",
        _ => Path.GetExtension(name).TrimStart('.') is { Length: > 0 } ext ? ext.ToUpperInvariant() : "File",
    };

    static JsonObject FileJson(FileRef f) => new()
    {
        ["id"] = f.Id, ["name"] = f.Name, ["size"] = f.Size, ["content_type"] = f.ContentType, ["format"] = FormatOf(f.ContentType, f.Name),
        ["local"] = f.Local, ["skipped"] = f.Skipped, ["url"] = f.Url,
    };

    static JsonObject RubricRow(RubricCriterion c, SubmissionInfo? submission)
    {
        var mark = submission?.Marks.GetValueOrDefault(c.Id);
        return new JsonObject
        {
            ["id"] = c.Id, ["criterion"] = c.Description, ["description"] = c.LongDescription, ["points"] = c.Points,
            ["ratings"] = new JsonArray(c.Ratings.Select(r => (JsonNode)new JsonObject { ["label"] = r.Description, ["points"] = r.Points }).ToArray()),
            ["mark"] = mark is null ? null : new JsonObject
            {
                ["points"] = mark.Points, ["rating"] = c.Ratings.FirstOrDefault(r => r.Id == mark.RatingId)?.Description, ["comment"] = mark.Comment,
            },
        };
    }

    static JsonObject SubmissionJson(SubmissionInfo s) => new()
    {
        ["state"] = s.State, ["attempt"] = s.Attempt, ["submitted_at"] = When(s.SubmittedAt), ["graded_at"] = When(s.GradedAt), ["score"] = s.Score,
        ["grade"] = s.Grade, ["late"] = s.Late, ["points_deducted"] = s.PointsDeducted, ["body"] = s.Body,
        ["files"] = new JsonArray(s.Files.Select(f => (JsonNode)FileJson(f)).ToArray()),
        ["attempts"] = new JsonArray(s.Attempts.Select(t => (JsonNode)new JsonObject
        {
            ["attempt"] = t.Attempt, ["submitted_at"] = When(t.SubmittedAt), ["late"] = t.Late, ["files"] = new JsonArray(t.Files.Select(f => (JsonNode)FileJson(f)).ToArray()),
        }).ToArray()),
    };

    /// <summary>One assignment, everything the design's assignment sheet shows: instructions, rubric with marks,
    /// submission, comments and where its spec and feedback are saved.</summary>
    public static JsonObject? Assignment(CourseIndex? index, long id, DateTimeOffset now, string? folder)
    {
        if (index?.Assignments.FirstOrDefault(a => a.Id == id) is not { } a) return null;
        var row = ItemJson(Assignments.From(index.Class, a, now), folder ?? a.Folder);
        row["instructions"] = a.Instructions;
        row["unlock_at"] = When(a.UnlockAt);
        row["lock_at"] = When(a.LockAt);
        row["submission_types"] = new JsonArray(a.SubmissionTypes.Select(t => (JsonNode)t).ToArray());
        row["allowed_attempts"] = a.AllowedAttempts;
        row["grading_type"] = a.GradingType;
        row["files"] = new JsonArray(a.InstructionFiles.Select(f => (JsonNode)FileJson(f)).ToArray());
        row["rubric"] = new JsonArray(a.Rubric.Select(c => (JsonNode)RubricRow(c, a.Submission)).ToArray());
        row["submission"] = a.Submission is { } s ? SubmissionJson(s) : null;
        row["comments"] = new JsonArray((a.Submission?.Comments ?? []).Select(c => (JsonNode)new JsonObject
        {
            ["author"] = c.Author, ["at"] = When(c.At), ["text"] = c.Text, ["files"] = new JsonArray(c.Files.Select(f => (JsonNode)FileJson(f)).ToArray()), ["media_url"] = c.MediaUrl,
        }).ToArray());
        row["quiz"] = a.QuizId is { } qid && index.Quizzes.FirstOrDefault(q => q.Id == qid) is { } quiz ? new JsonObject
        {
            ["id"] = quiz.Id, ["quiz_type"] = quiz.QuizType, ["time_limit"] = quiz.TimeLimit, ["allowed_attempts"] = quiz.AllowedAttempts,
            ["question_count"] = quiz.QuestionCount,
        } : null;
        row["spec"] = a.Folder.Length > 0 ? a.Folder + "/spec.md" : null;
        row["feedback"] = a.Folder.Length > 0 && CanvasMarkdown.HasFeedback(a) ? a.Folder + "/feedback.md" : null;
        return row;
    }

    // --- modules, files, announcements, pages -------------------------------------------------------------------

    static JsonObject ModuleItemJson(ModuleItemInfo it) => new()
    {
        ["id"] = it.Id, ["type"] = it.Type, ["kind"] = it.Kind, ["title"] = it.Title, ["indent"] = it.Indent, ["format"] = it.Format,
        ["size"] = it.Size, ["local"] = it.Local, ["saved"] = it.Saved, ["source"] = it.Source, ["url"] = it.Url, ["external_url"] = it.ExternalUrl,
        ["assignment_id"] = it.AssignmentId, ["locked"] = it.Locked, ["skipped"] = it.Skipped,
    };

    public static JsonObject Modules(CourseIndex? index) => new()
    {
        ["count"] = index?.Modules.Count ?? 0,
        ["modules"] = new JsonArray((index?.Modules ?? []).Select(m => (JsonNode)new JsonObject
        {
            ["id"] = m.Id, ["name"] = m.Name, ["position"] = m.Position, ["state"] = m.State, ["unlock_at"] = When(m.UnlockAt),
            ["items_count"] = m.ItemsCount, ["items"] = new JsonArray(m.Items.Select(it => (JsonNode)ModuleItemJson(it)).ToArray()),
        }).ToArray()),
    };

    public static JsonObject Files(CourseIndex? index) => new()
    {
        ["allowed"] = index is null || !index.FilesHidden, ["count"] = index?.Files.Count ?? 0,
        ["files"] = new JsonArray((index?.Files ?? []).Select(f => (JsonNode)new JsonObject
        {
            ["id"] = f.Id, ["folder"] = f.Folder, ["name"] = f.Name, ["size"] = f.Size, ["content_type"] = f.ContentType,
            ["format"] = f.Format ?? FormatOf(f.ContentType, f.Name), ["updated_at"] = When(f.UpdatedAt), ["local"] = f.Local, ["skipped"] = f.Skipped,
        }).ToArray()),
    };

    public static JsonObject Announcements(CourseIndex? index, HashSet<long> seenHere) => new()
    {
        ["count"] = index?.Announcements.Count ?? 0,
        ["new"] = index?.Announcements.Count(a => !a.ReadOnCanvas && !seenHere.Contains(a.Id)) ?? 0,
        ["items"] = new JsonArray((index?.Announcements ?? []).OrderByDescending(a => a.PostedAt, StringComparer.Ordinal).Select(a => (JsonNode)new JsonObject
        {
            ["id"] = a.Id, ["title"] = a.Title, ["posted_at"] = When(a.PostedAt), ["author"] = a.Author,
            ["new"] = !a.ReadOnCanvas && !seenHere.Contains(a.Id), ["read_on_canvas"] = a.ReadOnCanvas, ["body"] = a.Body,
            ["files"] = new JsonArray(a.Files.Select(f => (JsonNode)FileJson(f)).ToArray()), ["url"] = a.HtmlUrl,
        }).ToArray()),
    };

    static JsonObject PageJson(PageInfo p) => new()
    {
        ["title"] = p.Title, ["url"] = p.Url, ["updated_at"] = When(p.UpdatedAt), ["local"] = p.Local, ["in_module"] = p.InModule,
    };

    public static JsonObject Pages(CourseIndex? index) => new()
    {
        ["syllabus"] = index?.Syllabus,
        ["front_page"] = index?.FrontPage is { } fp ? PageJson(fp) : null,
        ["pages"] = new JsonArray((index?.Pages ?? []).Select(p => (JsonNode)PageJson(p)).ToArray()),
        ["quizzes"] = new JsonArray((index?.Quizzes ?? []).Select(q => (JsonNode)new JsonObject
        {
            ["id"] = q.Id, ["title"] = q.Title, ["due_at"] = When(q.DueAt), ["points"] = q.Points, ["locked"] = q.Locked, ["local"] = q.Local, ["url"] = q.HtmlUrl,
        }).ToArray()),
        ["discussions"] = new JsonArray((index?.Discussions ?? []).Select(d => (JsonNode)new JsonObject
        {
            ["id"] = d.Id, ["title"] = d.Title, ["locked"] = d.Locked, ["local"] = d.Local, ["url"] = d.HtmlUrl,
        }).ToArray()),
    };

    // --- notifications -------------------------------------------------------------------------------------------

    public static JsonObject Notifications(long last, IReadOnlyList<CanvasNotification> items) => new()
    {
        ["last"] = last,
        ["items"] = new JsonArray(items.Select(n => (JsonNode)new JsonObject
        {
            ["id"] = n.Id, ["kind"] = n.Kind, ["title"] = n.Title, ["text"] = n.Text, ["class"] = n.Class, ["assignment_id"] = n.AssignmentId,
            ["announcement_id"] = n.AnnouncementId, ["at"] = When(n.At), ["seen"] = n.Seen,
        }).ToArray()),
    };
}
