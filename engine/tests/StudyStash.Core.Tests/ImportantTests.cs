using StudyStash.Core.Study;

namespace StudyStash.Core.Tests;

/// <summary>Important to remember: the AI reads each flag with what was said round it and names the idea, the flags
/// that weren't about anything are dropped, and what it named is kept until the flags change.</summary>
public class ImportantTests
{
    static StudyLecture Blood(string transcript = StudyLibrary.BloodTranscript) =>
        new("v1", "Blood vessels", "BIO 110", "2026-10-08", "## Vessels\n- Arteries carry blood away.", transcript, ExamHints.Find(transcript));

    const string Answer = """
        {"items":[
          {"flag":1,"keep":true,"concept":"Three layers of an artery wall.","detail":"Intima, media and adventitia."},
          {"flag":2,"keep":true,"concept":"Who discovered capillaries","detail":"Not needed."},
          {"flag":3,"keep":false,"concept":"Writing it down","detail":""},
          {"flag":9,"keep":true,"concept":"Not a flag","detail":""}]}
        """;

    [Fact]
    public void Each_flag_goes_to_the_AI_with_the_transcript_round_it()
    {
        string prompt = Important.Prompt(Blood());
        Assert.Contains("Flag 1 at 02:30:", prompt);
        Assert.Contains("Flag 2 (SKIP) at 05:15:", prompt);
        Assert.Contains("[01:10] Arteries carry blood away from the heart.", prompt); // the minute before flag 1
        Assert.Contains("Arteries carry blood away.", prompt); // the notes
    }

    [Fact]
    public void The_ideas_are_named_and_the_ones_about_nothing_dropped()
    {
        var named = Important.Read(Answer, Blood().Flags);
        Assert.Equal(["Three layers of an artery wall", "Who discovered capillaries"], named.Select(n => n.Concept));
        Assert.Equal(("02:30", "Intima, media and adventitia.", false), (named[0].Moment, named[0].Detail, named[0].Not));
        Assert.True(named[1].Not);
        Assert.StartsWith("This will definitely be on the exam", named[0].Said);
    }

    [Fact]
    public async Task What_was_named_is_kept_until_the_flags_change()
    {
        using var dir = new TempDir();
        var important = new Important(dir["home"]);
        int asked = 0;
        Task<string> Plan(string p, System.Text.Json.Nodes.JsonObject s) { asked++; return Task.FromResult(Answer); }

        Assert.Equal(2, (await important.MakeAsync(Blood(), Plan)).Count);
        Assert.Equal(2, (await important.MakeAsync(Blood(), Plan)).Count);
        Assert.Equal(1, asked);

        await important.MakeAsync(Blood(StudyLibrary.BloodTranscript + "[00:08:00] Make sure you know the portal system.\n"), Plan);
        Assert.Equal(2, asked);
        Assert.Empty(await important.MakeAsync(Blood("[00:01] Nothing to see."), Plan));
        Assert.Equal(2, asked);
    }

    [Fact]
    public async Task A_meeting_outside_the_classes_has_what_s_important_too()
    {
        await using var lib = await StudyLibrary.StartAsync(l =>
        {
            l.Cfg.Classes.Add(new ClassDef("Robotics Club", group: "Clubs"));
            l.Store.Save(new Meeting("club") { Title = "Build night", Date = "2026-10-09", Transcript = StudyLibrary.BloodTranscript },
                new Classification("Robotics Club", 1, "folder"), "## Summary\nWe built the arm.", "Claude");
        });
        lib.Answers.Add(Answer);

        var named = (await lib.Study.ImportantAsync("club"))!;
        Assert.Equal(["Three layers of an artery wall", "Who discovered capillaries"], named.Select(n => n.Concept));
        Assert.Empty((await lib.Study.ExamFlagsAsync("club"))!); // what Review, exam prep and Listen are told stays the classes'
    }
}
