using StudyStash.Core.Canvas;

namespace StudyStash.Core.Tests;

/// <summary>After a sync, the scout explores the classes it hasn't, and tries again where it failed while offline.</summary>
public class ScoutTests
{
    [Fact]
    public void A_sync_explores_new_classes_and_retries_failed_ones_but_not_explored_ones()
    {
        var reports = new Dictionary<string, ScoutReport>
        {
            ["Chemistry"] = new(true, "Found the syllabus page.", "2026-11-01T10:00:00Z", 3),
            ["History"] = new(false, "failed to construct executor: plan model not specified", "2026-11-04T08:00:00Z", 0),
            ["Biology"] = new(false, "The network is down.", "2026-11-04T08:00:00Z", 0),
        };
        // Biology was explored once (it has a recipe); only its later try failed.
        var picked = Scout.ToExplore(["Chemistry", "History", "Biology", "Physics"], reports, c => c == "Biology");
        Assert.Equal(["History", "Physics"], picked);
    }
}
