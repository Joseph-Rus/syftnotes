using System.Globalization;
using System.Text;
using System.Text.Json;

namespace StudyStash.Core.Canvas;

/// <summary>
/// The Markdown the sync writes from a class's index when it finishes: each assignment's spec.md (what to do) and
/// feedback.md (where you stand). Pure functions of the index, the time zone and the time, so the same Canvas always
/// writes the same bytes and links point at where files really landed.
/// </summary>
public static class CanvasMarkdown
{
    /// <summary>"Tue 30 Sep 2025, 11:59 PM" in <paramref name="zone"/>; "" when there's no time.</summary>
    public static string When(string? iso, TimeZoneInfo zone) =>
        !string.IsNullOrEmpty(iso) && DateTimeOffset.TryParse(iso, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var t)
            ? TimeZoneInfo.ConvertTime(t, zone).ToString("ddd d MMM yyyy, h:mm tt", CultureInfo.InvariantCulture) : "";

    /// <summary>A file size the way the design writes it: "4 KB", "1.2 MB".</summary>
    public static string Size(long? bytes) => bytes switch
    {
        null => "",
        < 1024 => $"{bytes} bytes",
        < 1024 * 1024 => $"{Math.Max(1, Math.Round(bytes.Value / 1024.0)).ToString("0", CultureInfo.InvariantCulture)} KB",
        < 1024L * 1024 * 1024 => $"{(bytes.Value / (1024.0 * 1024)).ToString("0.#", CultureInfo.InvariantCulture)} MB",
        _ => $"{(bytes.Value / (1024.0 * 1024 * 1024)).ToString("0.#", CultureInfo.InvariantCulture)} GB",
    };

    static string Num(double? d) => (d ?? 0).ToString("0.##", CultureInfo.InvariantCulture);

    // Inside a table cell: one line, and no bar to end the cell early.
    static string Cell(string s) => s.ReplaceLineEndings(" ").Replace("|", "\\|", StringComparison.Ordinal).Trim();

    /// <summary>How an assignment is marked, when it isn't points: "Complete/incomplete", "Letter grade".</summary>
    static string? Grading(string type) => type switch
    {
        "pass_fail" => "Complete/incomplete",
        "letter_grade" => "Letter grade",
        "gpa_scale" => "GPA scale",
        "percent" => "Percentage",
        "not_graded" => "Not graded",
        _ => null,
    };

    /// <summary>A quiz's own facts, the way spec.md states them: "Quiz · 10 questions · 30 minutes · 2 attempts".</summary>
    static string QuizFacts(QuizInfo q)
    {
        var facts = new List<string> { "Quiz" };
        if (q.QuestionCount is int qc) facts.Add(qc == 1 ? "1 question" : $"{qc} questions");
        if (q.TimeLimit is int tl) facts.Add($"{tl} minutes");
        if (q.AllowedAttempts is int aa) facts.Add(aa < 0 ? "unlimited attempts" : aa == 1 ? "1 attempt" : $"{aa} attempts");
        return string.Join(" · ", facts);
    }

    /// <summary>spec.md: what the assignment asks, by when, how it's marked. <paramref name="quiz"/> and
    /// <paramref name="discussion"/> are the quiz or discussion this assignment folds in (T6), when it's one.</summary>
    public static string Spec(string cls, AssignmentInfo a, TimeZoneInfo zone, QuizInfo? quiz = null, DiscussionInfo? discussion = null)
    {
        var sb = new StringBuilder();
        sb.Append("---\n")
            .Append("title: ").Append(JsonSerializer.Serialize(a.Name)).Append('\n')
            .Append("class: ").Append(cls).Append('\n')
            .Append("canvas_id: ").Append(a.Id.ToString(CultureInfo.InvariantCulture)).Append('\n')
            .Append("due: ").Append(When(a.DueAt, zone) is { Length: > 0 } due ? due : "none").Append('\n')
            .Append("points: ").Append(Num(a.Points)).Append('\n')
            .Append("submission_types: ").Append(string.Join(", ", a.SubmissionTypes)).Append('\n')
            .Append("url: ").Append(a.HtmlUrl).Append('\n')
            .Append(Crawl.Generated).Append(" (from Canvas; rewritten when Canvas changes)\n---\n\n")
            .Append("# ").Append(a.Name).Append("\n\n");
        var facts = new List<string> { When(a.DueAt, zone) is { Length: > 0 } d ? $"**Due** {d}" : "**No due date**" };
        if (a.GradingType != "not_graded") facts.Add($"**{Num(a.Points)} points**");
        if (Grading(a.GradingType) is { } how) facts.Add(how);
        // A quiz's own attempts fact says more than the assignment's (and would only repeat it).
        if (quiz is null && a.AllowedAttempts is int n and > 0) facts.Add(n == 1 ? "1 attempt" : $"{n} attempts");
        if (quiz is not null) facts.Add(QuizFacts(quiz));
        sb.Append(string.Join(" · ", facts)).Append("\n\n");
        var window = new List<string>();
        if (When(a.UnlockAt, zone) is { Length: > 0 } from) window.Add($"Available from {from}");
        if (When(a.LockAt, zone) is { Length: > 0 } until) window.Add($"Closes {until}");
        if (window.Count > 0) sb.Append(string.Join(" · ", window)).Append("\n\n");
        // A graded discussion's own prompt fills in for instructions Canvas never gave the assignment.
        string body = a.Instructions.Length > 0 ? a.Instructions : discussion?.Prompt is { Length: > 0 } prompt ? prompt : "_No instructions on Canvas._";
        sb.Append(body).Append('\n');
        if (a.Rubric.Count > 0)
        {
            sb.Append("\n## Rubric\n\n| Criterion | Points | Levels |\n|---|---|---|\n");
            foreach (var r in a.Rubric)
            {
                string levels = string.Join("; ", r.Ratings.Select(x => $"{x.Description} ({Num(x.Points)})"));
                string desc = r.Description + (r.LongDescription.Length > 0 ? $" ({r.LongDescription})" : "");
                sb.Append("| ").Append(Cell(desc)).Append(" | ").Append(Num(r.Points)).Append(" | ").Append(Cell(levels)).Append(" |\n");
            }
        }
        return sb.ToString();
    }

    /// <summary>There's something to say about the student's work: handed in, marked, or commented on.</summary>
    public static bool HasFeedback(AssignmentInfo a) => a.Submission is { } s
        && (!string.IsNullOrEmpty(s.SubmittedAt) || s.Score is not null || !string.IsNullOrEmpty(s.Grade) || s.Comments.Count > 0 || s.Marks.Count > 0);

    /// <summary>feedback.md: the mark, status, files handed in, rubric marks, comments and earlier attempts.</summary>
    public static string Feedback(string cls, AssignmentInfo a, TimeZoneInfo zone, DateTimeOffset now)
    {
        var s = a.Submission ?? new SubmissionInfo();
        var sb = new StringBuilder();
        sb.Append("---\ncanvas_id: ").Append(a.Id.ToString(CultureInfo.InvariantCulture)).Append('\n')
            .Append("class: ").Append(cls).Append('\n')
            .Append("url: ").Append(a.HtmlUrl).Append('\n')
            .Append(Crawl.Generated).Append(" (from Canvas)\n---\n\n")
            .Append("# ").Append(a.Name).Append(": my submission\n\n");
        string score = Assignments.ScoreText(s.Score, s.Grade, a.GradingType, a.Points);
        // A letter or pass/fail grade says the points too, when Canvas gave them.
        if (score.Length > 0 && a.GradingType is not ("points" or "") && s.Score is double pts && a.Points is > 0) score += $" ({Num(pts)}/{Num(a.Points)})";
        sb.Append("- **Score:** ").Append(score.Length > 0 ? score : s.Excused ? "excused" : "not graded yet").Append('\n');
        string status = Assignments.StatusOf(a, now);
        sb.Append("- **Status:** ").Append(Assignments.Label(status, s.Late)).Append('\n');
        if (When(s.SubmittedAt, zone) is { Length: > 0 } submitted) sb.Append("- **Submitted:** ").Append(submitted).Append('\n');
        if (When(s.GradedAt, zone) is { Length: > 0 } graded && status == "graded") sb.Append("- **Graded:** ").Append(graded).Append('\n');
        if (s.Attempt is int attempt)
            sb.Append("- **Attempt:** ").Append(attempt).Append(a.AllowedAttempts is int most and > 0 ? $" of {most}" : "").Append('\n');
        if (s.PointsDeducted is double off && off > 0) sb.Append("- **Late penalty:** −").Append(Num(off)).Append(off == 1 ? " point\n" : " points\n");
        if (s.Files.Count > 0) sb.Append("- **Files:** ").Append(Files(s.Files, a.Folder)).Append('\n');
        if (s.Body.Length > 0) sb.Append("\n## What I submitted\n\n").Append(s.Body).Append('\n');
        if (s.Marks.Count > 0)
        {
            sb.Append("\n## Rubric marks\n\n| Criterion | Mark | Rating | Comment |\n|---|---|---|---|\n");
            var criteria = a.Rubric.ToDictionary(r => r.Id);
            // In the rubric's order; a mark for a criterion the rubric no longer has comes last, by its id.
            var order = a.Rubric.Select(r => r.Id).Where(s.Marks.ContainsKey).Concat(s.Marks.Keys.Where(k => !criteria.ContainsKey(k)).Order(StringComparer.Ordinal));
            foreach (string id in order)
            {
                var mark = s.Marks[id];
                var c = criteria.GetValueOrDefault(id);
                string points = mark.Points is double p ? Num(p) + (c?.Points is double of ? $" / {Num(of)}" : "") : "–";
                string rating = c?.Ratings.FirstOrDefault(r => r.Id == mark.RatingId)?.Description ?? "";
                sb.Append("| ").Append(Cell(c?.Description ?? id)).Append(" | ").Append(points).Append(" | ").Append(Cell(rating)).Append(" | ")
                    .Append(Cell(mark.Comment)).Append(" |\n");
            }
        }
        if (s.Comments.Count > 0)
        {
            sb.Append("\n## Comments\n\n");
            foreach (var c in s.Comments)
            {
                sb.Append("- **").Append(c.Author.Length > 0 ? c.Author : "Someone").Append("**");
                if (When(c.At, zone) is { Length: > 0 } at) sb.Append(" (").Append(at).Append(')');
                sb.Append(": ").Append(Py.Strip(c.Text).ReplaceLineEndings(" ")).Append('\n');
                if (c.Files.Count > 0) sb.Append("  - Attached: ").Append(Files(c.Files, a.Folder)).Append('\n');
                if (c.MediaUrl is { Length: > 0 } media) sb.Append("  - [Audio or video comment, on Canvas](").Append(media).Append(")\n");
            }
        }
        if (s.Attempts.Count > 1)
        {
            sb.Append("\n## Attempts\n\n");
            foreach (var t in s.Attempts.OrderByDescending(t => t.Attempt))
            {
                sb.Append("- Attempt ").Append(t.Attempt);
                if (t.Attempt == s.Attempt) sb.Append(" (latest)");
                if (When(t.SubmittedAt, zone) is { Length: > 0 } at) sb.Append(", submitted ").Append(at);
                if (t.Late) sb.Append(", late");
                if (t.Files.Count > 0) sb.Append(": ").Append(Files(t.Files, a.Folder));
                sb.Append('\n');
            }
        }
        return sb.ToString();
    }

    /// <summary>"[ps4.py](submission/ps4.py) (4 KB)", or the Canvas link and why it wasn't saved.</summary>
    static string Files(IEnumerable<FileRef> files, string folder) => string.Join(", ", files.Select(f =>
    {
        string size = Size(f.Size);
        if (f.Local is { Length: > 0 } local) return $"[{f.Name}]({RelativeLink(folder, local)})" + (size.Length > 0 ? $" ({size})" : "");
        string why = f.Skipped is { Length: > 0 } skipped ? $"not saved: {skipped}" : "not saved";
        return $"[{f.Name}]({f.Url}) ({(size.Length > 0 ? size + ", " : "")}{why}, on Canvas)";
    }));

    /// <summary>A link from a file in <paramref name="fromFolder"/> to <paramref name="path"/> (both inside the class's
    /// folder, with /), each part escaped for Markdown: "submission/attempt%201/lab2.pdf", "../Lab%202/spec.md".</summary>
    public static string RelativeLink(string fromFolder, string path)
    {
        string[] from = fromFolder.Split('/', StringSplitOptions.RemoveEmptyEntries), to = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        int same = 0;
        while (same < from.Length && same < to.Length - 1 && from[same] == to[same]) same++;
        return string.Join('/', Enumerable.Repeat("..", from.Length - same).Concat(to.Skip(same).Select(Uri.EscapeDataString)));
    }

    /// <summary>How a module item links a service it isn't Canvas: "Box", "Drive", "OneDrive", "YouTube".</summary>
    static string? SourceName(string? source) => source switch
    {
        "box" => "Box", "drive" => "Drive", "onedrive" => "OneDrive", "youtube" => "YouTube", _ => null,
    };

    /// <summary>A local path (relative to the class folder, "Canvas/modules/…") as modules.md links it: relative to
    /// the class's Canvas folder (where modules.md itself lives), spaces the only thing escaped (Canvas's own titles
    /// may hold characters a strict URL escape would mangle for no reader's benefit).</summary>
    static string ModuleLink(string classRelPath) =>
        (classRelPath.StartsWith("Canvas/", StringComparison.Ordinal) ? classRelPath["Canvas/".Length..] : classRelPath).Replace(" ", "%20");

    /// <summary>One module item's line in the outline: what it is, and where it really is (a local copy, a linked
    /// assignment's spec.md, or Canvas itself).</summary>
    static string ItemLine(ModuleItemInfo it, CourseIndex index)
    {
        string suffix = it.Locked ? " (locked)" : "";
        switch (it.Kind)
        {
            case "header":
                return $"**{it.Title}**";
            case "file":
                if (it.Local is { Length: > 0 } floc)
                {
                    var bits = new List<string>();
                    if (it.Format is { Length: > 0 } f) bits.Add(f);
                    if (Size(it.Size) is { Length: > 0 } sz) bits.Add(sz);
                    string tag = bits.Count > 0 ? $" ({string.Join(", ", bits)})" : "";
                    return $"[{it.Title}]({ModuleLink(floc)}){tag}{suffix}";
                }
                return $"{it.Title} (not saved: {it.Skipped ?? "unavailable"}, on Canvas: {it.Url}){suffix}";
            case "page":
                return it.Local is { Length: > 0 } ploc ? $"[{it.Title}]({ModuleLink(ploc)}){suffix}" : $"{it.Title} (on Canvas: {it.Url}){suffix}";
            case "assignment" or "quiz" or "discussion":
                var linked = it.Kind switch
                {
                    "quiz" => index.Assignments.FirstOrDefault(a => a.QuizId == it.AssignmentId),
                    "discussion" => index.Assignments.FirstOrDefault(a => a.DiscussionTopicId == it.AssignmentId),
                    _ => index.Assignments.FirstOrDefault(a => a.Id == it.AssignmentId),
                };
                return linked is { Folder.Length: > 0 } a
                    ? $"[{it.Title}]({ModuleLink(a.Folder + "/spec.md")}){suffix}"
                    : $"{it.Title} (on Canvas: {it.Url}){suffix}";
            case "link":
                string dest = it.ExternalUrl is { Length: > 0 } eu ? eu : it.Url;
                string? source = SourceName(it.Source);
                string linkTag = source is null ? " (link)" : it.Saved ? $" (saved from {source})" : " (link)";
                return $"[{it.Title}]({dest}){linkTag}{suffix}";
            case "tool":
                return $"[{it.Title}]({(it.ExternalUrl is { Length: > 0 } tu ? tu : it.Url)}) (tool){suffix}";
            default:
                return $"{it.Title} (on Canvas: {it.Url}){suffix}";
        }
    }

    /// <summary>modules.md: every module, in Canvas's order, each item linked to where it really landed (a local
    /// copy, a linked assignment's spec.md) or to Canvas when there's nothing local to point at.</summary>
    public static string Modules(string cls, CourseIndex index)
    {
        var sb = new StringBuilder($"# {cls}: Canvas modules\n\n_From Canvas; the local copies are linked. Rewritten on every sync._\n\n");
        foreach (var m in index.Modules.OrderBy(m => m.Position))
        {
            sb.Append("## ").Append(m.Name).Append("\n\n");
            foreach (var it in m.Items)
                sb.Append(new string(' ', 2 * it.Indent)).Append("- ").Append(ItemLine(it, index)).Append('\n');
            sb.Append('\n');
        }
        return sb.ToString().TrimEnd() + "\n";
    }

    /// <summary>announcements.md: every announcement, newest first, its attachments linked to where they landed.</summary>
    public static string Announcements(string cls, CourseIndex index, TimeZoneInfo zone)
    {
        var sb = new StringBuilder($"# {cls}: announcements\n\n_From Canvas, newest first._\n\n");
        foreach (var a in index.Announcements)
        {
            sb.Append("## ").Append(a.Title).Append('\n')
                .Append('_').Append(When(a.PostedAt, zone)).Append(" · ").Append(a.Author).Append("_\n\n")
                .Append(a.Body.Length > 0 ? a.Body : "_No text._").Append('\n');
            if (a.Files.Count > 0) sb.Append('\n').Append("**Attached:** ").Append(Files(a.Files, "Canvas")).Append('\n');
            sb.Append('\n');
        }
        return sb.ToString().TrimEnd() + "\n";
    }

    /// <summary>An ungraded (practice or survey) quiz's own file, standing alone (a graded one folds into its
    /// assignment's spec.md instead): its facts and description, never its questions.</summary>
    public static string Quiz(QuizInfo q)
    {
        var sb = new StringBuilder().Append("# ").Append(q.Title).Append("\n\n").Append(QuizFacts(q)).Append("\n\n")
            .Append(q.Description.Length > 0 ? q.Description : "_No description on Canvas._").Append('\n');
        return sb.ToString();
    }

    /// <summary>An ungraded discussion's own file, standing alone (a graded one folds into its assignment's spec.md
    /// instead): its prompt, never anyone's replies.</summary>
    public static string Discussion(DiscussionInfo d) =>
        $"# {d.Title}\n\n{(d.Prompt.Length > 0 ? d.Prompt : "_No prompt on Canvas._")}\n";
}
