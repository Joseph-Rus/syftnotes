namespace StudyStash.Core.Tests;

/// <summary>A library not for school writes notes for meetings and talks: decisions and action items, not study notes.</summary>
public class MeetingNotesTests
{
    static async Task<string> PromptFor(params string[] uses)
    {
        using var dir = new TempDir();
        var cfg = new Config(dir["home"], dir["pool"]) { Uses = [.. uses] };
        var m = new Meeting("m1") { Title = "Weekly sync", Transcript = string.Concat(Enumerable.Repeat("We agreed to ship on Friday. Sam will write the release notes. ", 40)) };
        string asked = "";
        await Summarize.SummarizeTranscriptAsync(m, cfg, chat: (_, _, prompt, _) => { asked = prompt; return Task.FromResult("## Summary\nok"); },
            show: (_, _) => Task.FromResult<int?>(32768));
        return asked;
    }

    [Fact]
    public async Task Work_gets_decisions_and_action_items()
    {
        string p = await PromptFor("work");
        Assert.Contains("## Action items", p);
        Assert.Contains("## Decisions", p);
        Assert.DoesNotContain("## Questions to review", p);
        Assert.Contains("meetings, talks and conversations", p);
    }

    [Fact]
    public async Task School_and_not_asked_get_study_notes()
    {
        foreach (var p in new[] { await PromptFor(), await PromptFor("student", "work") })
        {
            Assert.Contains("## Questions to review", p);
            Assert.DoesNotContain("## Action items", p);
        }
    }
}
