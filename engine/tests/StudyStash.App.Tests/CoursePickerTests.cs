using System.Text.Json;
using StudyStash.App.Services;
using StudyStash.App.ViewModels;

namespace StudyStash.App.Tests;

/// <summary>The course picker (setup's Canvas step, the connect window, Settings → Canvas): this term's courses
/// ticked the first time, the library's choice after that; a change in Settings waits for Save, and one that drops
/// courses asks whether to keep their classes and files.</summary>
public class CoursePickerTests
{
    /// <summary>What a library says after Find my courses: a class name, short code and term for each course, whether
    /// it's brought in, and whether the picker would tick it. Made-up courses.</summary>
    static string Overview(bool chosen) => """
        {"url": "https://school.instructure.com",
         "courses": {@LINKS@},
         "chosen": @CHOSEN@,
         "available": {"11": "Bridge Design", "12": "Cell Biology", "13": "Sandbox for Dr. Okafor", "14": "Heat Transfer"},
         "course_info": {
           "11": {"code": "MECH4120.G", "name": "Bridge Design(MECH4120.G)", "term": "Fall 2026", "title": "Bridge Design", "short_code": "MECH 4120",
                  "chosen": @IN@, "suggested": true, "why": "", "class": @BRIDGE@},
           "12": {"code": "BIO 110", "name": "Cell Biology", "term": "Fall 2026", "title": "Cell Biology", "short_code": "BIO 110",
                  "chosen": @IN@, "suggested": true, "why": "", "class": @CELLS@},
           "13": {"code": "SBX", "name": "Sandbox for Dr. Okafor", "term": "Default Term", "title": "Sandbox for Dr. Okafor", "short_code": "",
                  "chosen": false, "suggested": false, "why": "Not a class", "class": null},
           "14": {"code": "MECH2710.A", "name": "Heat Transfer(MECH2710.A)", "term": "Spring 2026", "title": "Heat Transfer", "short_code": "MECH 2710",
                  "chosen": false, "suggested": false, "why": "Past term", "class": null}}}
        """.Replace("@LINKS@", chosen ? "\"Bridge Design\": 11, \"Cell Biology\": 12" : "").Replace("@CHOSEN@", chosen ? "[\"11\", \"12\"]" : "null")
        .Replace("@IN@", chosen ? "true" : "false").Replace("@BRIDGE@", chosen ? "\"Bridge Design\"" : "null").Replace("@CELLS@", chosen ? "\"Cell Biology\"" : "null");

    static CanvasApi.Overview Read(string json) => JsonSerializer.Deserialize<CanvasApi.Overview>(json, CanvasApi.Json)!;

    [Fact]
    public void The_first_time_this_term_s_courses_are_ticked_and_the_rest_say_why_not()
    {
        var m = new CoursePickerModel(CanvasFixtures.Context());
        m.Fill(Read(Overview(chosen: false)));

        Assert.True(m.Fresh);
        Assert.Equal(["Bridge Design", "Cell Biology", "Heat Transfer", "Sandbox for Dr. Okafor"], m.Courses.Select(c => c.Title));
        Assert.Equal([true, true, false, false], m.Courses.Select(c => c.Ticked));
        Assert.Equal("MECH 4120 · Fall 2026", m.Courses[0].Detail);
        Assert.Equal("Past term", m.Courses[2].Right);
        Assert.Equal("Not a class", m.Courses[3].Right);
        Assert.Equal("", m.Courses[0].Right);
        m.Courses[3].Ticked = true;
        Assert.Equal("", m.Courses[3].Right); // ticked: no reason to show
    }

    [Fact]
    public async Task In_settings_a_change_waits_for_save_and_dropping_asks_to_keep_or_remove()
    {
        var handler = new FakeLibrary().Json(HttpMethod.Post, "/api/v2/canvas/choose", Overview(chosen: true));
        var m = new CoursePickerModel(CanvasFixtures.Context(handler)) { SavesItself = true };
        m.Fill(Read(Overview(chosen: true)));
        Assert.False(m.Fresh);
        Assert.False(m.ShowSaveBar);

        // Adding one only: Save.
        m.Courses.First(c => c.Title == "Heat Transfer").Ticked = true;
        Assert.True(m.ShowSaveBar);
        Assert.True(m.OnlyAdds);
        Assert.Equal("Bring in 1 course.", m.ChangeLine);

        // Dropping one too: Keep them / Remove them.
        m.Courses.First(c => c.Title == "Cell Biology").Ticked = false;
        Assert.True(m.Drops);
        Assert.False(m.OnlyAdds);
        Assert.Equal("Bring in 1 course. Stop syncing 1 course. Keep its class and files?", m.ChangeLine);

        await m.RemoveCommand.ExecuteAsync(null);
        var sent = Assert.Single(handler.Requests, r => r.Path == "/api/v2/canvas/choose");
        using var body = JsonDocument.Parse(sent.Body!);
        Assert.Equal(["11", "14"], body.RootElement.GetProperty("courses").EnumerateArray().Select(e => e.GetString()!).Order());
        Assert.False(body.RootElement.GetProperty("keep").GetBoolean());

        // Cancel puts the ticks back as the library has them.
        m.Courses.First(c => c.Title == "Bridge Design").Ticked = false;
        m.CancelCommand.Execute(null);
        Assert.False(m.HasChanges);
    }

    [Fact]
    public void What_saving_did_is_said_in_a_line()
    {
        Assert.Equal("Added Bridge Design and Cell Biology. Syncing them now.",
            CanvasWords.ChoiceSaid(new CanvasApi.ChoiceOutcome { Added = ["Bridge Design", "Cell Biology"] }));
        Assert.Equal("Stopped syncing Heat Transfer. Kept Cell Biology for its lectures; its Canvas files are gone.",
            CanvasWords.ChoiceSaid(new CanvasApi.ChoiceOutcome { Stopped = ["Heat Transfer", "Cell Biology"], KeptForLectures = ["Cell Biology"] }));
        Assert.Equal("Removed Bridge Design.", CanvasWords.ChoiceSaid(new CanvasApi.ChoiceOutcome { Stopped = ["Bridge Design"], Removed = ["Bridge Design"] }));
        Assert.Null(CanvasWords.ChoiceSaid(new CanvasApi.ChoiceOutcome()));
    }

    [Fact]
    public async Task In_setup_only_the_ticked_courses_go_on_to_become_classes()
    {
        var handler = new FakeLibrary().Json(HttpMethod.Post, "/api/v2/canvas/courses", Overview(chosen: false));
        var context = CanvasFixtures.Context(handler);
        using var m = new CanvasConnectModel(context, new CanvasWatch(context), forSetup: true);
        await m.StartAsync(CanvasFixtures.Load<CanvasApi.State>("state-connected"), [], TestContext.Current.CancellationToken);

        Assert.True(m.AllDone);
        Assert.True(m.ShowPicker);
        Assert.Equal(["Bridge Design", "Cell Biology"], m.Found.Select(f => f.ClassName));
        m.Picker.Courses.First(c => c.Title == "Heat Transfer").Ticked = true;
        m.Picker.Courses.First(c => c.Title == "Cell Biology").Ticked = false;
        Assert.Equal(["Bridge Design", "Heat Transfer"], m.Found.Select(f => f.ClassName));
        Assert.Equal("MECH 2710", m.Found[1].Code);
    }
}
