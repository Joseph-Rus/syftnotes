using System.Globalization;
using StudyStash.App.Services;

namespace StudyStash.App.ViewModels;

/// <summary>
/// Every word the Canvas screens show, computed from the API's data at a given moment: pure functions, no
/// <see cref="DateTime.Now"/>, no culture but <see cref="CultureInfo.InvariantCulture"/>. Every time comes in UTC
/// and is converted to the reader's zone before it's formatted, the way the design does it.
/// </summary>
public static class CanvasWords
{
    // ---- dates and clocks ----

    static DateTime Local(DateTimeOffset at, TimeZoneInfo zone) => TimeZoneInfo.ConvertTime(at, zone).DateTime;

    /// <summary>"Tue 30 Sep, 11:59 PM"; another year adds " yyyy" after the month.</summary>
    public static string Full(DateTimeOffset at, TimeZoneInfo zone, DateTimeOffset now)
    {
        var local = Local(at, zone);
        string fmt = local.Year == Local(now, zone).Year ? "ddd d MMM, h:mm tt" : "ddd d MMM yyyy, h:mm tt";
        return local.ToString(fmt, CultureInfo.InvariantCulture);
    }

    /// <summary>"Mon 22 Sep".</summary>
    public static string Day(DateTimeOffset at, TimeZoneInfo zone) => Local(at, zone).ToString("ddd d MMM", CultureInfo.InvariantCulture);

    /// <summary>"Tue 23" — a lecture row's day, with no month.</summary>
    public static string ShortDay(DateTimeOffset at, TimeZoneInfo zone) => Local(at, zone).ToString("ddd d", CultureInfo.InvariantCulture);

    /// <summary>"20 Sep" — Scout's day, with no weekday.</summary>
    public static string ScoutDay(DateTimeOffset at, TimeZoneInfo zone) => Local(at, zone).ToString("d MMM", CultureInfo.InvariantCulture);

    /// <summary>"10:24" — no AM/PM, the design's own clock.</summary>
    public static string Clock(DateTimeOffset at, TimeZoneInfo zone) => Local(at, zone).ToString("h:mm", CultureInfo.InvariantCulture);

    /// <summary>"Today, 9:00 AM" / "Tomorrow, 9:00 AM" / the Full date.</summary>
    public static string When(DateTimeOffset at, TimeZoneInfo zone, DateTimeOffset now)
    {
        var local = Local(at, zone);
        var today = Local(now, zone).Date;
        string clock = local.ToString("h:mm tt", CultureInfo.InvariantCulture);
        if (local.Date == today) return $"Today, {clock}";
        if (local.Date == today.AddDays(1)) return $"Tomorrow, {clock}";
        return Full(at, zone, now);
    }

    /// <summary>"just now" / "n min ago" / "n h ago" / "on {Day}".</summary>
    public static string Ago(DateTimeOffset at, TimeZoneInfo zone, DateTimeOffset now)
    {
        var span = now - at;
        if (span < TimeSpan.FromMinutes(1)) return "just now";
        if (span < TimeSpan.FromHours(1)) return $"{(int)span.TotalMinutes} min ago";
        if (span < TimeSpan.FromHours(24)) return $"{(int)span.TotalHours} h ago";
        return $"on {Day(at, zone)}";
    }

    /// <summary>A notification's own age: "now" / "n min" / "n h" / its weekday.</summary>
    public static string NotificationAge(DateTimeOffset at, TimeZoneInfo zone, DateTimeOffset now)
    {
        var span = now - at;
        if (span < TimeSpan.FromMinutes(1)) return "now";
        if (span < TimeSpan.FromHours(1)) return $"{(int)span.TotalMinutes} min";
        if (span < TimeSpan.FromHours(24)) return $"{(int)span.TotalHours} h";
        return Local(at, zone).ToString("ddd", CultureInfo.InvariantCulture);
    }

    /// <summary>Calendar days between now and a due date, in the reader's zone: "Due today", "Due tomorrow", "Due in
    /// n days", "Was due yesterday", "Was due n days ago".</summary>
    public static string DueRelative(DateTimeOffset dueAt, TimeZoneInfo zone, DateTimeOffset now)
    {
        int days = (Local(dueAt, zone).Date - Local(now, zone).Date).Days;
        return days switch
        {
            0 => "Due today",
            1 => "Due tomorrow",
            > 1 => $"Due in {days} days",
            -1 => "Was due yesterday",
            _ => $"Was due {-days} days ago",
        };
    }

    /// <summary>The quick panel's day for an overdue item: its weekday within 6 days, else "d MMM".</summary>
    public static string QuickOverdueDay(DateTimeOffset at, TimeZoneInfo zone, DateTimeOffset now)
    {
        var local = Local(at, zone);
        int daysAgo = (Local(now, zone).Date - local.Date).Days;
        return daysAgo is >= 0 and <= 6 ? local.ToString("ddd", CultureInfo.InvariantCulture) : local.ToString("d MMM", CultureInfo.InvariantCulture);
    }

    // ---- an item (Due list, a class's assignment tabs) ----

    /// <summary>What the right of a Due-list row shows: its score when it's graded, else its plain label.</summary>
    public static string RightLabel(CanvasApi.Item item) =>
        // A score out of nothing ("0/0", Canvas's way of counting a syllabus quiz or attendance) says nothing: its label does.
        !string.IsNullOrEmpty(item.ScoreText) && !(item.Points is null or 0 && item.ScoreText.EndsWith("/0", StringComparison.Ordinal)) ? item.ScoreText : item.Label;

    /// <summary>Still to hand in (not yet submitted, and Canvas still takes it).</summary>
    public static bool ToHandIn(CanvasApi.Item item) => item.Status is "open" or "to_hand_in" or "past due" or "missing" or "overdue";

    /// <summary>A row's right-hand word in a list: for work still to hand in, how soon it's due ("Today", "Tomorrow",
    /// "In 3 days", "In 2 weeks") or that it's late; for the rest, the score or the plain label.</summary>
    public static string RightLabel(CanvasApi.Item item, TimeZoneInfo zone, DateTimeOffset now)
    {
        if (!ToHandIn(item) || !string.IsNullOrEmpty(item.ScoreText)) return RightLabel(item);
        if (item.Missing || item.Status is "missing") return "Missing";
        if (item.DueAt is not { } due) return "";
        int days = (Local(due, zone).Date - Local(now, zone).Date).Days;
        return days switch
        {
            < 0 => "Late",
            0 => "Today",
            1 => "Tomorrow",
            < 14 => $"In {days} days",
            // The date itself is on the line under the title; this only says how far off it is.
            _ => $"In {days / 7} weeks",
        };
    }

    /// <summary>"Was due … · {class}" / "{When} · …" / "Graded {Day} · …" / "Submitted {Day} · …" / "Marked done {Day} · …".</summary>
    public static string DueSub(CanvasApi.Item item, TimeZoneInfo zone, DateTimeOffset now)
    {
        string body = item.Status switch
        {
            "missing" or "overdue" => item.DueAt is { } da ? $"Was due {Full(da, zone, now)}" : "Overdue",
            "graded" => item.GradedAt is { } g ? $"Graded {Day(g, zone)}" : RightLabel(item),
            "submitted" or "late" => item.Submitted is { } sub ? $"Submitted {Day(sub, zone)}" : RightLabel(item),
            "past due" => item.DueAt is { } pd ? $"Was due {Full(pd, zone, now)}" : "Past due",
            "closed" => "Closed",
            "marked_done" => item.MarkedDone is { } md ? $"Marked done {Day(md, zone)}" : RightLabel(item),
            _ => item.DueAt is { } due ? When(due, zone, now) : "No due date",
        };
        // The date first: a long class name is what gets cut short, not when it's due (the dot says the class).
        return $"{body} · {item.Class}";
    }

    /// <summary>A class's tab row: to-hand-in items lead with their points, done ones with their plain label.</summary>
    public static string ClassTabRow(CanvasApi.Item item, TimeZoneInfo zone, DateTimeOffset now)
    {
        string points = item.Points is > 0 ? $"{PointsText(item.Points)} pts · " : "";
        if (ToHandIn(item)) return item.DueAt is { } due ? $"{points}{When(due, zone, now)}" : $"{points}No due date";
        // Done: when it went in, or was graded, rather than the label the row already shows on its right.
        string late = item.Late ? " · late" : "";
        if (item.GradedAt is { } graded) return $"Graded {Day(graded, zone)}{late}";
        if (item.Submitted is { } sub) return $"Submitted {Day(sub, zone)}{late}";
        // The label is already on the row's right ("Excused", "Nothing to hand in"): here, only when it was due.
        return item.DueAt is { } was ? $"Due {Day(was, zone)}" : "No due date";
    }

    /// <summary>The Due list's header: how many are left to hand in, and when it last synced.</summary>
    public static string DueHeader(int toHandIn, DateTimeOffset? synced, TimeZoneInfo zone) =>
        synced is not { } s ? "Not synced yet" :
        toHandIn == 0 ? $"Nothing to hand in · synced {Clock(s, zone)}" : $"{toHandIn} to hand in · synced {Clock(s, zone)}";

    /// <summary>Assignment/Quiz/Discussion/To-do, from the API's lowercase kind.</summary>
    public static string KindWord(string kind) => kind switch
    {
        "assignment" => "Assignment",
        "quiz" => "Quiz",
        "discussion" => "Discussion",
        "to-do" or "todo" => "To-do",
        "" => "",
        _ => char.ToUpperInvariant(kind[0]) + kind[1..],
    };

    /// <summary>A points value with no trailing ".0" (Canvas's points are doubles even when they're whole numbers).</summary>
    public static string PointsText(double? points) =>
        points is not { } p ? "" : p == Math.Floor(p) ? ((long)p).ToString(CultureInfo.InvariantCulture) : p.ToString("0.##", CultureInfo.InvariantCulture);

    /// <summary>"18 / 20", or the letter/pass-fail grade when that's how the assignment is graded.</summary>
    public static string ScoreOrGradeText(double? score, double? points, string? grade, string? gradingType)
    {
        if (gradingType is "letter" or "pass_fail" && !string.IsNullOrEmpty(grade)) return grade;
        return score is null || points is null ? "" : $"{PointsText(score)} / {PointsText(points)}";
    }

    /// <summary>"CS 101 · Assignment", the detail screen's header.</summary>
    public static string DetailHeader(string cls, string kind) => $"{cls} · {KindWord(kind)}";

    // ---- a rubric ----

    /// <summary>"8 / 10" when it's been marked, else "10 pts".</summary>
    public static string RubricPoints(CanvasApi.RubricRow row) =>
        row.Mark is { } m ? $"{PointsText(m.Points)} / {PointsText(row.Points)}" : $"{PointsText(row.Points)} pts";

    /// <summary>A rubric comment, in curly quotes the way the design sets them.</summary>
    public static string Quote(string text) => $"“{text}”";

    // ---- a submission ----

    public sealed record SubmissionCopy(string Status, string Detail);

    public const string HandItInLink = "Hand it in on Canvas ↗";

    /// <summary>The assignment detail's submission card: its status line and, under it, when it was submitted or
    /// how long until it's due.</summary>
    public static SubmissionCopy SubmissionText(CanvasApi.SubmissionInfo? submission, double? score, double? points, string? grade,
        string? gradingType, bool excused, DateTimeOffset? dueAt, TimeZoneInfo zone, DateTimeOffset now)
    {
        if (excused) return new SubmissionCopy("Excused", "");
        if (submission is null) return new SubmissionCopy("Nothing handed in yet", dueAt is { } due ? DueRelative(due, zone, now) : "");
        if (submission.GradedAt is not null)
            return new SubmissionCopy($"Graded · {ScoreOrGradeText(score, points, grade, gradingType)}",
                submission.SubmittedAt is { } handed ? $"Submitted {Full(handed, zone, now)}" : "");
        // "Submitted" already says what happened; the line beside it only says when.
        return new SubmissionCopy(submission.Late ? "Submitted late" : "Submitted", submission.SubmittedAt is { } sa ? Full(sa, zone, now) : "");
    }

    /// <summary>The first few lines of a text-entry submission, as plain text (Canvas stores it as HTML).</summary>
    public static string BodyPreview(string? body, int max = 280)
    {
        if (string.IsNullOrWhiteSpace(body)) return "";
        string text = System.Net.WebUtility.HtmlDecode(System.Text.RegularExpressions.Regex.Replace(body, "<[^>]+>", " "));
        text = System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ").Trim();
        if (text.Length <= max) return text;
        int cut = text.LastIndexOf(' ', max);
        return text[..(cut > max - 60 ? cut : max)].TrimEnd() + "…";
    }

    // ---- sizes ----

    /// <summary>"512 B" / "4 KB" / "1.2 MB" (1024-based; MB drops a trailing ".0").</summary>
    public static string Size(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        double kb = bytes / 1024.0;
        if (kb < 1024) return $"{(long)Math.Round(kb, MidpointRounding.AwayFromZero)} KB";
        double mb = kb / 1024.0;
        return $"{mb.ToString("0.#", CultureInfo.InvariantCulture)} MB";
    }

    // ---- a class header ----

    public static string LectureCountText(int n) => n == 1 ? "1 lecture" : $"{n} lectures";
    public static string FileCountText(int n) => n == 1 ? "1 file" : $"{n} files";

    /// <summary>"COMP 101 on Canvas", or the course's plain name when it has no code.</summary>
    public static string ClassCanvasLine(string? code, string name) => $"{(string.IsNullOrEmpty(code) ? name : code)} on Canvas";

    /// <summary>Scout's line under a class's header. One that didn't finish says nothing here: there's nothing to do
    /// about it from the class page, and Settings → Canvas has its Try again.</summary>
    public static string ScoutHeaderLine(CanvasApi.Scout scout, TimeZoneInfo zone) => scout.State switch
    {
        "done" => $"Scout explored this course · {FileCountText(scout.Files)} saved · {(scout.When is { } w ? ScoutDay(w, zone) : "")}",
        "exploring" => "Scout exploring…",
        _ => "",
    };

    // ---- Settings -> Canvas ----

    /// <summary>A class row in Settings' scout line: the same states, shorter words.</summary>
    public static string ScoutSettingsLine(CanvasApi.Scout scout, TimeZoneInfo zone) => scout.State switch
    {
        "done" => $"Scout done · {FileCountText(scout.Files)} saved · {(scout.When is { } w ? ScoutDay(w, zone) : "")}",
        "exploring" => "Scout exploring…",
        "failed" => "Scout didn’t finish",
        _ => "",
    };

    public const string NotMatched = "Not matched, so nothing syncs";
    public const string TryAgain = "Try again";

    /// <summary>"Every 15 minutes" … "Once a day", the sync interval choices.</summary>
    public static string PollIntervalText(int minutes) => minutes switch
    {
        15 => "Every 15 minutes",
        30 => "Every 30 minutes",
        60 => "Every hour",
        180 => "Every 3 hours",
        1440 => "Once a day",
        _ => $"Every {minutes} minutes",
    };

    /// <summary>"Checked in 2 min ago · version 1.4" — the extension's own status line.</summary>
    public static string ExtensionCheckedInLine(DateTimeOffset seen, string version, TimeZoneInfo zone, DateTimeOffset now) =>
        $"Checked in {Ago(seen, zone, now)} · version {version}";

    /// <summary>"Last sync · 10:24".</summary>
    public static string LastSyncLine(DateTimeOffset? synced, TimeZoneInfo zone) =>
        synced is { } s ? $"Last sync · {Clock(s, zone)}" : "Not synced yet";

    /// <summary>"Last sync 10:24" — the quick panel's own line for Sync Canvas (no dot, unlike Settings'
    /// <see cref="LastSyncLine"/>).</summary>
    public static string QuickLastSyncLine(DateTimeOffset? synced, TimeZoneInfo zone) =>
        synced is { } s ? $"Last sync {Clock(s, zone)}" : "Not synced yet";

    /// <summary>"Connected · last sync 10:24" — Settings' one-line summary.</summary>
    public static string ConnectedSummary(DateTimeOffset? lastSync, TimeZoneInfo zone) =>
        lastSync is { } s ? $"Connected · last sync {Clock(s, zone)}" : "Connected";

    // ---- modules, files, announcements ----

    /// <summary>A module item's word: its format ("PDF"), "Page", where it was saved from, or why it wasn't.</summary>
    public static string ModuleItemWord(CanvasApi.ModuleItem item)
    {
        if (item.Skipped) return item.Format == "video" ? "Video" : "Too big";
        if (item.Locked) return "Locked";
        if (item.Kind == "link" && !item.Saved) return "Link";
        if (item.Source == "box") return "Saved from Box";
        if (item.Source == "drive") return "Saved from Drive";
        if (item.Kind == "page") return "Page";
        return item.Format is { Length: > 0 } f ? f.ToUpperInvariant() : KindWord(item.Kind ?? item.Type);
    }

    public static string ItemsCountText(int n) => n == 1 ? "1 item" : $"{n} items";

    /// <summary>"Modules 8" / "Files 23".</summary>
    public static string CountLine(string label, int n) => $"{label} {n}";

    /// <summary>"4 · 1 new", or just the count when nothing's new.</summary>
    public static string AnnouncementsCountText(int count, int newCount) => newCount > 0 ? $"{count} · {newCount} new" : $"{count}";

    // ---- the quick panel and the dropdown ----

    /// <summary>An overdue item leads with its label; anything else is just its due time.</summary>
    public static string QuickDueLine(CanvasApi.Item item, TimeZoneInfo zone, DateTimeOffset now) =>
        item.Missing && item.DueAt is { } d ? $"{RightLabel(item)} · was due {QuickOverdueDay(d, zone, now)}" :
        item.DueAt is { } due ? When(due, zone, now) : "";

    /// <summary>"Next due: Quiz 3 practice · Tomorrow, 9:00 AM" — the dropdown's line for the soonest item that
    /// isn't overdue.</summary>
    public static string DropdownNextDue(CanvasApi.Item next, TimeZoneInfo zone, DateTimeOffset now) =>
        next.DueAt is { } due ? $"Next due: {next.Name} · {When(due, zone, now)}" : $"Next due: {next.Name}";

    /// <summary>Joins a list the way a sentence does: "A", "A and B", "A, B and C".</summary>
    public static string JoinAnd(IReadOnlyList<string> items) => items.Count switch
    {
        0 => "",
        1 => items[0],
        2 => $"{items[0]} and {items[1]}",
        _ => string.Join(", ", items.Take(items.Count - 1)) + $" and {items[^1]}",
    };

    // ---- design 08: Canvas's states ----

    public sealed record StateCopy(string Title, string Text);

    /// <summary>The states screen's title and text for wherever Canvas is right now. "updated" isn't a state the
    /// library sends — it's connected with an extension update still to show.</summary>
    public static StateCopy Describe(CanvasApi.State s, TimeZoneInfo zone)
    {
        string status = s.Status == "connected" && s.Extension?.Updated is not null ? "updated"
            // A Chrome from an earlier setup, with a key this library no longer takes: it needs adding again.
            : s.Status == "no_extension" && (s.Extension?.RefusedAt is not null || s.Extension is { KeyMatches: false, LastSeen: not null }) ? "old_key"
            : s.Status;
        string C(DateTimeOffset? at) => at is { } a ? Clock(a, zone) : "";
        return status switch
        {
            "not_set_up" => new StateCopy("Connect Canvas", "Bring in assignments, due dates and course files next to your lectures."),
            "no_extension" => new StateCopy("Finish setting up the Chrome extension", "It takes three clicks in Chrome."),
            "old_key" => new StateCopy("Connect Chrome again", "Chrome has the extension from an earlier setup. Add it again from this library so Canvas can sync."),
            "chrome_away" => new StateCopy("Is Chrome open?", $"Chrome last checked in at {C(s.Extension?.Seen)}. Canvas syncs only while Chrome is open."),
            "signed_out" => new StateCopy("Sign in to Canvas in Chrome", "Syncing waits until you do."),
            "syncing" => new StateCopy($"Syncing… {s.Syncing?.Left ?? 0} left", $"{JoinAnd(s.Syncing?.Classes ?? [])}."),
            "updated" => new StateCopy("The Chrome extension updated itself", $"Now version {s.Extension?.Updated?.To}. Nothing to do."),
            "error" => new StateCopy("Canvas didn’t answer", $"{s.School} didn’t respond at {C(s.Error?.At)}. Study Stash will try again at {C(s.NextSync)}."),
            _ => new StateCopy("Connected", $"Last sync {C(s.LastSync)}. Next at {C(s.NextSync)}."),
        };
    }

    /// <summary>What saving the course choice did, in a line: "Added Bridge Design and Cell Biology. Syncing them
    /// now. Stopped syncing Heat Transfer." A class kept for its lectures says so. Null when nothing changed.</summary>
    public static string? ChoiceSaid(CanvasApi.ChoiceOutcome? o)
    {
        if (o is null) return null;
        static string List(IReadOnlyList<string> names) =>
            names.Count <= 1 ? string.Join("", names) : string.Join(", ", names.Take(names.Count - 1)) + " and " + names[^1];
        var parts = new List<string>();
        if (o.Added.Count > 0) parts.Add($"Added {List(o.Added)}. Syncing {(o.Added.Count == 1 ? "it" : "them")} now.");
        var stoppedOnly = o.Stopped.Except(o.Removed).Except(o.KeptForLectures).ToList();
        if (stoppedOnly.Count > 0) parts.Add($"Stopped syncing {List(stoppedOnly)}.");
        if (o.Removed.Count > 0) parts.Add($"Removed {List(o.Removed)}.");
        if (o.KeptForLectures.Count > 0)
            parts.Add($"Kept {List(o.KeptForLectures)} for {(o.KeptForLectures.Count == 1 ? "its" : "their")} lectures; {(o.KeptForLectures.Count == 1 ? "its" : "their")} Canvas files are gone.");
        return parts.Count == 0 ? null : string.Join(" ", parts);
    }
}
