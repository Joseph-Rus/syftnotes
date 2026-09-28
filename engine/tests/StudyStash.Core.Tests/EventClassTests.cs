using System.Text.Json.Nodes;
using StudyStash.Core.Calendar;

namespace StudyStash.Core.Tests;

/// <summary>Matching a calendar event's title to a class.</summary>
public class EventClassTests
{
    static readonly List<ClassHint> Classes =
    [
        new("CS 101", ["cs101"]),
        new("Biology 110", []),
        new("Software Engineering", [], "CSCI 321"),
        new("Intro to Programming", ["Intro Prog"], "COMP 101"),
        new("Calculus II", ["Calc II"]),
        new("Art", []),
    ];

    [Theory]
    [InlineData("CS 101 Lecture", "CS 101")]
    [InlineData("cs101 lecture", "CS 101")]
    [InlineData("CS-101: Recursion", "CS 101")]
    [InlineData("Lab 3: Recursion [CS 101-02 Fall 2026]", "CS 101")]
    [InlineData("Software Engineering · Sprint review", "Software Engineering")]
    [InlineData("CSCI 321 lecture", "Software Engineering")]
    [InlineData("CSCI321 lecture", "Software Engineering")]
    [InlineData("COMP 101 lecture", "Intro to Programming")]
    [InlineData("Intro Prog lab", "Intro to Programming")]
    [InlineData("BIO 110 study group", "Biology 110")]
    [InlineData("Calc II (MATH 2410) with Okonkwo", "Calculus II")]
    [InlineData("Art", "Art")]
    public void A_title_that_names_a_class_is_that_class(string title, string expected) =>
        Assert.Equal(expected, EventClass.For(title, Classes));

    [Theory]
    [InlineData("CS 1010 Lecture")]
    [InlineData("Dentist")]
    [InlineData("Room 101 booking")]
    [InlineData("Artificial intelligence club")]
    [InlineData("")]
    [InlineData("   ")]
    public void A_title_that_doesn_t_is_no_class(string title) => Assert.Null(EventClass.For(title, Classes));

    [Fact]
    public void The_longest_name_wins_and_a_tie_between_classes_is_no_match()
    {
        List<ClassHint> classes = [new("CS 101", []), new("CS 101 Lab", [])];
        Assert.Equal("CS 101 Lab", EventClass.For("CS 101 Lab: pointers", classes));
        Assert.Equal("CS 101", EventClass.For("CS 101 Lecture", classes));
        List<ClassHint> twins = [new("Physics", []), new("Physiology", ["Physics"])];
        Assert.Null(EventClass.For("Physics review", twins));
        Assert.Null(EventClass.For("CS 101", []));
    }

    [Fact]
    public void A_loose_match_two_classes_share_is_no_match()
    {
        List<ClassHint> classes = [new("Biology 110", []), new("Biochemistry 110", [])];
        Assert.Null(EventClass.For("BIO 110 review", classes));
    }

    [Fact]
    public void Classes_from_the_library_s_answer_and_its_config()
    {
        var overview = JsonNode.Parse("""{"classes":[{"name":"CS 101","aliases":["cs101"],"code":"COMP 101"},{"name":"Art"},{"name":""}]}""")!.AsObject();
        var hints = EventClass.From(overview);
        Assert.Equal(2, hints.Count);
        Assert.Equal(["cs101"], hints[0].Aliases);
        Assert.Equal("COMP 101", hints[0].Code);
        Assert.Empty(EventClass.From(null));

        using var dir = new TempDir();
        var cfg = new Config(dir["home"], dir["pool"]) { Classes = [new ClassDef("Software Engineering", ["SE"])] };
        Canvas.CanvasSettings.Update(cfg.Home, c =>
        {
            c.Courses["Software Engineering"] = 4242;
            c.CourseInfo["4242"] = new Canvas.CourseInfo("202710.TS.CSCI321.A", "Software Engineering", "Fall 2026");
        });
        var fromConfig = Assert.Single(EventClass.Of(cfg));
        Assert.Equal("CSCI 321", fromConfig.Code);
        Assert.Equal("Software Engineering", EventClass.For("CSCI 321 standup", EventClass.Of(cfg)));
    }
}
