using StudyStash.Core.Canvas;

namespace StudyStash.Core.Tests;

/// <summary>Classes made from Canvas courses take the course's name, not its code.</summary>
public class CourseNamesTests
{
    [Theory]
    [InlineData("Software Engineering", "202710.TS.CSCI321.A", "Software Engineering")]
    [InlineData("202710.TS.CSCI321.A - Software Engineering", "202710.TS.CSCI321.A", "Software Engineering")]
    [InlineData("202710.TS.CSCI321.A: Software Engineering (Fall 2026)", "", "Software Engineering")]
    [InlineData("202710.Software Engineering", "", "Software Engineering")]
    [InlineData("  Intro   to  Programming ", "CS 101", "Intro to Programming")]
    [InlineData("CS 101: Intro to Programming", "CS 101", "Intro to Programming")]
    [InlineData("CS 101 · Intro to Programming", "", "Intro to Programming")]
    [InlineData("Calculus II - Spring 2027", "MATH 2B", "Calculus II")]
    [InlineData("Fall 2026 - Biology of Cells", "BIO 110", "Biology of Cells")]
    [InlineData("Organic Chemistry [202710]", "", "Organic Chemistry")]
    [InlineData("202710.TS.CSCI321.A", "202710.TS.CSCI321.A", "CSCI 321")]
    [InlineData("CS 101", "CS 101", "CS 101")]
    [InlineData("", "BIO 110", "BIO 110")]
    [InlineData("Physics 2", "PHYS 2", "Physics 2")]
    [InlineData("Bridge Design(MECH4120.G)", "MECH4120.G", "Bridge Design")]
    [InlineData("Materials Directed Study(MECH3950.DS)", "", "Materials Directed Study")]
    [InlineData("Signals Lab (EE 2150.B)", "EE2150.B", "Signals Lab")]
    [InlineData("Chemistry of Soils [202710.CHEM101.A]", "", "Chemistry of Soils")]
    [InlineData("Statics - ENGR 2010.A", "", "Statics")]
    [InlineData("202710.TS.MECH4120.G - Bridge Design(MECH4120.G)", "", "Bridge Design")]
    [InlineData("Calculus (Honors)", "MATH 150", "Calculus (Honors)")]
    [InlineData("Physics (Part II)", "", "Physics (Part II)")]
    [InlineData("(MECH4120.G)", "MECH4120.G", "MECH 4120")]
    public void A_course_s_name_is_cleaned_into_a_class_name(string name, string code, string expected) =>
        Assert.Equal(expected, CourseNames.Title(name, code));

    [Theory]
    [InlineData("202710.TS.CSCI321.A", "", "CSCI 321")]
    [InlineData("CS-101", "", "CS 101")]
    [InlineData("BIO110A", "", "BIO 110A")]
    [InlineData("", "ENGR 401: Senior Design", "ENGR 401")]
    [InlineData("FA 2026", "", "")]
    [InlineData("Seminar", "Seminar", "")]
    [InlineData("MECH3950.DS", "", "MECH 3950")]
    [InlineData("", "Bridge Design(MECH4120.G)", "MECH 4120")]
    public void The_short_code_is_a_subject_and_a_number(string code, string name, string expected) =>
        Assert.Equal(expected, CourseNames.ShortCode(code, name));

    [Fact]
    public void A_long_name_is_cut_at_a_word()
    {
        const string Long = "Advanced Topics in the History and Philosophy of Science and Technology Studies";
        string t = CourseNames.Title(Long, "");
        Assert.True(t.Length <= CourseNames.MaxLength);
        Assert.StartsWith(t + " ", Long);
    }

    [Fact]
    public void Two_courses_with_one_name_are_told_apart_by_code_then_number()
    {
        var names = CourseNames.Unique([
            ("1", "Senior Design", "202710.TS.ENGR401.A"),
            ("2", "Senior Design", "202710.TS.ENGR402.A"),
            ("3", "Seminar", ""),
            ("4", "seminar", ""),
            ("5", "Unsorted", "GEN 100"),
        ]);
        Assert.Equal("Senior Design (ENGR 401)", names["1"]);
        Assert.Equal("Senior Design (ENGR 402)", names["2"]);
        Assert.Equal("Seminar", names["3"]);
        Assert.Equal("seminar 2", names["4"]);
        Assert.Equal("Unsorted (GEN 100)", names["5"]);
    }

    [Theory]
    [InlineData("MECH2710.A", "", "A")]
    [InlineData("", "Heat Transfer(MECH2710.C)", "C")]
    [InlineData("202710.TS.CSCI321.02", "", "02")]
    [InlineData("CS 101", "Intro to Programming", "")]
    public void The_section_follows_the_course_code(string code, string name, string expected) =>
        Assert.Equal(expected, CourseNames.Section(code, name));

    [Fact]
    public void Sections_of_one_course_are_told_apart_by_their_section()
    {
        var names = CourseNames.Unique([
            ("1", "Heat Transfer(MECH2710.A)", "MECH2710.A"),
            ("2", "Heat Transfer(MECH2710.B)", "MECH2710.B"),
            ("3", "Bridge Design(MECH4120.G)", "MECH4120.G"),
        ]);
        Assert.Equal("Heat Transfer (section A)", names["1"]);
        Assert.Equal("Heat Transfer (section B)", names["2"]);
        Assert.Equal("Bridge Design", names["3"]);
    }

    [Fact]
    public void A_name_already_taken_by_another_class_gets_its_code()
    {
        var names = CourseNames.Unique([("1", "Software Engineering", "CSCI 321")], ["software engineering"]);
        Assert.Equal("Software Engineering (CSCI 321)", names["1"]);
    }
}
