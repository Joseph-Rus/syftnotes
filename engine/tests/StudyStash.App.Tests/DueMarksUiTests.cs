using StudyStash.App.Services;
using StudyStash.App.ViewModels;

namespace StudyStash.App.Tests;

/// <summary>"Mark done" and "Hide" on a Due item (kept in Syft Notes only), and the Due tab's "Show hidden": the buttons send
/// the mark, the one copy of the Due list takes the library's answer in at once, and a library that can't be reached is said.</summary>
public class DueMarksUiTests
{
    const string Hidden = """
        {"to_hand_in": 0, "groups": [], "hidden": [{"class": "CS 101", "id": 9001, "name": "Lab 3: recursion traces", "kind": "assignment", "status": "open", "mark": "hidden"}]}
        """;

    [Fact]
    public async Task Hiding_an_assignment_updates_the_one_copy_of_the_list_and_the_page_offers_Undo()
    {
        var library = new FakeLibrary().Json(HttpMethod.Post, "/api/v2/canvas/mark", Hidden);
        var context = CanvasFixtures.Context(library);
        using var feed = new CanvasFeed(context);
        int updated = 0;
        feed.Updated += () => updated++;
        var page = new AssignmentModel(context, (cls, kind, id, mark, stop) => feed.MarkAsync(cls, kind, id, mark, stop));
        page.Show(CanvasFixtures.Load<CanvasApi.AssignmentDetail>("assignment-9001")); // to do: it offers both
        Assert.Equal((true, false), (page.Marks.CanMark, page.Marks.CanUndo));

        await page.Marks.HideItCommand.ExecuteAsync(null);

        var sent = Assert.Single(library.Requests, r => r.Path == "/api/v2/canvas/mark");
        Assert.Equal("""{"class":"CS 101","kind":"assignment","id":9001,"mark":"hidden"}""", sent.Body);
        Assert.Equal(("hidden", false, true, 1), (page.Marks.Mark, page.Marks.CanMark, page.Marks.CanUndo, updated));
        Assert.Equal(["Lab 3: recursion traces"], feed.Due!.Hidden.Select(i => i.Name)); // the list every screen reads has it now

        // An assignment already handed in has nothing to tick off or hide.
        page.Show(CanvasFixtures.Load<CanvasApi.AssignmentDetail>("assignment-9002"));
        Assert.False(page.Marks.Showing);

        // A library that can't answer: the page says so in words, and nothing changed.
        using var gone = new CanvasFeed(CanvasFixtures.Context(new FakeLibrary()));
        var offline = new AssignmentModel(CanvasFixtures.Context(new FakeLibrary()), (cls, kind, id, mark, stop) => gone.MarkAsync(cls, kind, id, mark, stop));
        offline.Show(CanvasFixtures.Load<CanvasApi.AssignmentDetail>("assignment-9001"));
        await offline.Marks.MarkDoneCommand.ExecuteAsync(null);
        Assert.Equal(("", true), (offline.Marks.Mark, offline.Marks.Action.Say is { Length: > 0 }));
    }

    [Fact]
    public async Task The_Due_tab_shows_Show_hidden_only_when_something_is_hidden_and_Undo_brings_it_back()
    {
        var asked = new List<string>();
        DueMarker marker = (cls, kind, id, mark, _) =>
        {
            asked.Add($"{cls} {kind} {id} '{mark}'");
            return Task.FromResult(Outcome.Done);
        };
        var now = new DateTimeOffset(2026, 10, 12, 13, 34, 0, TimeSpan.FromHours(-4));
        OverviewModel.Sources Sources(CanvasApi.DueResponse due) => new() { Now = now, Zone = TimeZoneInfo.Utc, Due = due, Mark = marker };
        var item = new CanvasApi.Item
        {
            Class = "CS 101", Id = "9101", Name = "Asynchronous Lecture 5", Kind = "assignment", Status = "missing", Mark = "hidden", DueAt = now.AddDays(-7),
        };

        Assert.False(new DueCalendarModel(Sources(new CanvasApi.DueResponse())).HasHidden);

        var calendar = new DueCalendarModel(Sources(new CanvasApi.DueResponse { Hidden = [item] }));
        Assert.Equal(("Show hidden (1)", false), (calendar.HiddenToggle, calendar.HiddenOpen));
        calendar.ToggleHiddenCommand.Execute(null);
        Assert.Equal(("Hide the hidden list", true, "Asynchronous Lecture 5"), (calendar.HiddenToggle, calendar.HiddenOpen, calendar.Hidden.Single().Title));

        await calendar.UnhideCommand.ExecuteAsync(calendar.Hidden[0]);
        Assert.Equal(["CS 101 assignment 9101 ''"], asked);
    }
}
