using Avalonia.Media;
using StudyStash.App.ViewModels;
using StudyStash.Core.Calendar;

namespace StudyStash.App.Tests;

/// <summary>Coming up: its rows, their words, and the list that changes only when they do.</summary>
public class ComingUpTests
{
    static readonly TimeZoneInfo NewYork = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
    static readonly DateTimeOffset Now = new(2026, 9, 28, 9, 0, 0, TimeSpan.FromHours(-4));

    static CalendarEvent E(string id, string title, DateTimeOffset start, int minutes = 60, string? location = null) =>
        new(id, "c", "s", title, start, start.AddMinutes(minutes), false, location, [], null, false);

    [Fact]
    public void When_an_event_is_in_words()
    {
        Assert.Equal("Now, until 9:30 AM", CalendarWords.When(E("a", "x", Now.AddMinutes(-30)), Now, NewYork));
        Assert.Equal("2:00 PM", CalendarWords.When(E("a", "x", Now.AddHours(5)), Now, NewYork));
        Assert.Equal("Tomorrow 10:00 AM", CalendarWords.When(E("a", "x", Now.AddHours(25)), Now, NewYork));
        Assert.Equal("Wed 9:00 AM", CalendarWords.When(E("a", "x", Now.AddDays(2)), Now, NewYork));
        Assert.Equal("1 calendar", CalendarWords.SourceLine(1, null));
        Assert.Equal("3 calendars", CalendarWords.SourceLine(3, null));
        Assert.Equal("Couldn't reach it.", CalendarWords.SourceLine(3, "Couldn't reach it."));
        Assert.Equal("From your calendar: CS 101 Lecture", CalendarWords.FromCalendar("CS 101 Lecture"));
    }

    [Fact]
    public void Rows_have_their_class_and_dot_only_when_the_library_has_the_class()
    {
        List<ClassHint> classes = [new("CS 101", []), new("BIO 110", [])];
        var red = Brushes.Red;
        var rows = ComingUpModel.For([
            E("cs", "CS 101 Lecture", Now.AddMinutes(-10), location: "Hall 204"),
            E("bio", "BIO 110 lab", Now.AddHours(2)),
            E("lunch", "Lunch", Now.AddHours(3)),
        ], Now, NewYork, classes, cls => cls == "CS 101" ? red : null);
        Assert.Equal(["cs", "bio", "lunch"], rows.Select(r => r.Id));
        Assert.Equal(("CS 101", red, true), (rows[0].ClassName, (IBrush)rows[0].Dot, rows[0].Now));
        Assert.Equal("CS 101 · Hall 204", rows[0].Detail);
        // BIO 110 has no dot here (the library doesn't have it): no class shown.
        Assert.Null(rows[1].ClassName);
        Assert.False(rows[1].HasDetail);
        Assert.False(rows[2].HasClass);
    }

    [Fact]
    public void The_list_shows_says_nothing_s_left_or_hides()
    {
        var model = new ComingUpModel();
        Assert.False(model.IsEmpty);
        Assert.False(model.HasRows);
        model.Show(true, []);
        Assert.True(model.IsEmpty);
        var rows = ComingUpModel.For([E("a", "CS 101", Now.AddHours(1))], Now, NewYork, [], _ => null);
        model.Show(true, rows);
        Assert.True(model.HasRows);
        Assert.False(model.IsEmpty);
        int changes = 0;
        model.Rows.CollectionChanged += (_, _) => changes++;
        model.Show(true, ComingUpModel.For([E("a", "CS 101", Now.AddHours(1))], Now, NewYork, [], _ => null));
        Assert.Equal(0, changes);
        model.Show(true, ComingUpModel.For([E("a", "CS 101 (moved)", Now.AddHours(1))], Now, NewYork, [], _ => null));
        Assert.True(changes > 0);
        model.Show(false, []);
        Assert.False(model.IsEmpty);
    }
}
