using StudyStash.Core.Canvas;

namespace StudyStash.Core.Filing;

/// <summary>
/// Work the student handed in on Canvas, as a sync saw it: the class, the assignment, the newest attempt (its number
/// and when it went in) and that attempt's files. Submit &amp; Organize files these into the class's folder. Canvas sync
/// already reads all of it (the submissions listing, with every attempt); this only picks it out. Test builds only for
/// now (<see cref="TestSwitches.On"/>).
/// </summary>
public sealed record HandedIn(string Class, AssignmentInfo Assignment, int Attempt, string SubmittedAt, IReadOnlyList<FileRef> Files)
{
    /// <summary>What makes a hand-in new: the assignment, the attempt and when it went in.</summary>
    public string Key => $"{Assignment.Id}:{Attempt}:{SubmittedAt}";

    /// <summary>For each assignment in <paramref name="index"/> handed in with files, its newest attempt.</summary>
    public static List<HandedIn> In(CourseIndex index)
    {
        var list = new List<HandedIn>();
        foreach (var a in index.Assignments)
        {
            if (a.Submission is not { } s || s.State is not ("submitted" or "pending_review" or "graded")) continue;
            var newest = s.Attempts.Where(t => t.SubmittedAt is not null).MaxBy(t => t.Attempt);
            // The history may lag the submission itself: the submission's own attempt is the newest then.
            if (newest is null || s.Attempt is int n && n > newest.Attempt)
                newest = new AttemptInfo { Attempt = s.Attempt ?? 0, SubmittedAt = s.SubmittedAt, Files = s.Files };
            if (newest.SubmittedAt is not { } at || newest.Files.Count == 0) continue;
            list.Add(new HandedIn(index.Class, a, newest.Attempt, at, newest.Files));
        }
        return list;
    }

    /// <summary>What was handed in, or handed in again, since the sync before: the hand-ins in <paramref name="after"/>
    /// whose assignment, attempt and time <paramref name="before"/> didn't have. Nothing on a class's first sync, so
    /// linking Canvas doesn't file a whole term's old work at once.</summary>
    public static List<HandedIn> Since(CourseIndex? before, CourseIndex after)
    {
        if (before is null) return [];
        var had = In(before).Select(h => h.Key).ToHashSet(StringComparer.Ordinal);
        return [.. In(after).Where(h => !had.Contains(h.Key))];
    }
}
