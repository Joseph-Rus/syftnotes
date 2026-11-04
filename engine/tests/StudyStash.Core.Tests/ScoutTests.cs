using StudyStash.Core.Canvas;

namespace StudyStash.Core.Tests;

/// <summary>After a sync, the scout explores the classes it hasn't, and tries again, at most once a day, where it failed.</summary>
public class ScoutTests
{
    [Fact]
    public void A_sync_explores_new_classes_and_retries_failed_ones_once_a_day_but_not_explored_ones()
    {
        var now = new DateTimeOffset(2026, 11, 4, 9, 0, 0, TimeSpan.Zero);
        var reports = new Dictionary<string, ScoutReport>
        {
            ["Chemistry"] = new(true, "Found the syllabus page.", "2026-11-01T10:00:00Z", 3),
            ["History"] = new(false, "The network is down.", "2026-11-02T08:00:00Z", 0),
            ["Music"] = new(false, "failed to construct executor: plan model not specified", "2026-11-04T08:00:00Z", 0),
            ["Biology"] = new(false, "The network is down.", "2026-11-02T08:00:00Z", 0),
        };
        // Music failed an hour ago: it waits for tomorrow. Biology was explored once (it has a recipe); only its later try failed.
        var picked = Scout.ToExplore(["Chemistry", "History", "Music", "Biology", "Physics"], reports, c => c == "Biology", now);
        Assert.Equal(["History", "Physics"], picked);
    }
}
