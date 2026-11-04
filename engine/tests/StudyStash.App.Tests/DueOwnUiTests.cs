using StudyStash.App.Services;
using StudyStash.App.ViewModels;

namespace StudyStash.App.Tests;

/// <summary>The Due calendar's Add and the form on an item of the student's own: what Save sends, the one copy of the list taking the
/// library's answer in, "Mark done" and "Delete" (which asks first and, said no, leaves the item and the form as they were).</summary>
public class DueOwnUiTests
{
    const string Added = """
        {"to_hand_in": 1, "groups": [{"key": "later", "label": "Later", "items": [
          {"class": "", "id": 42, "name": "Chemistry midterm", "kind": "mine", "what": "exam", "all_day": false, "status": "open", "mark": "",
           "due": "2026-11-05T09:00", "due_at": "2026-11-05T14:00:00Z", "url": ""}]}], "hidden": []}
        """;
    const string Empty = """{"to_hand_in": 0, "groups": [], "hidden": []}""";

    [Fact]
    public async Task Add_sends_the_day_and_time_as_typed_and_a_row_clicked_opens_the_form_to_mark_done_or_delete_after_asking()
    {
        var library = new FakeLibrary().Json(HttpMethod.Post, "/api/v2/due/mine", Added).Json(HttpMethod.Delete, "/api/v2/due/mine/42", Empty);
        using var feed = new CanvasFeed(CanvasFixtures.Context(library));
        var marked = new List<string>();
        var confirm = new FakeConfirm(false);
        var sources = new OverviewModel.Sources
        {
            Now = new DateTimeOffset(2026, 11, 2, 10, 0, 0, TimeSpan.FromHours(-5)), Zone = TimeZoneInfo.CreateCustomTimeZone("Test", TimeSpan.FromHours(-5), "Test", "Test"),
            Classes = [("Chemistry", Avalonia.Media.Brushes.Gray, 3)],
            Own = new OwnDueEditor((d, ct) => feed.SaveOwnAsync(d, ct), (id, ct) => feed.DeleteOwnAsync(id, ct)),
            Mark = (cls, kind, id, mark, _) =>
            {
                marked.Add($"{cls}|{kind}|{id}|{mark}");
                return Task.FromResult(Outcome.Done);
            },
        };
        var calendar = new DueCalendarModel(sources, confirm);
        var form = calendar.Own;

        // Add: the form opens on today, can't be saved without a title, and sends what was typed.
        calendar.AddOwnCommand.Execute(null);
        Assert.Equal((true, new DateTime(2026, 11, 2), false), (form.IsOpen, form.Day, form.CanSave));
        form.Title = "  Chemistry midterm ";
        form.SetWhatCommand.Execute("exam");
        form.Day = new DateTime(2026, 11, 5);
        form.Time = TimeSpan.FromHours(9);
        form.Chosen = form.Classes.Single(c => c.Name == "Chemistry");
        await form.SaveCommand.ExecuteAsync(null);

        Assert.Equal("""{"title":"Chemistry midterm","what":"exam","class":"Chemistry","day":"2026-11-05","time":"09:00"}""",
            Assert.Single(library.Requests, r => r.Method == "POST" && r.Path == "/api/v2/due/mine").Body);
        Assert.False(form.IsOpen);
        var item = Assert.Single(feed.Due!.Groups.SelectMany(g => g.Items)); // the library's answer is the one copy now
        Assert.True(item.Own);

        // A click on the row opens the same form on it, with its fields as they were saved. Mark done goes through the marks.
        form.Edit(item, sources.Zone);
        Assert.Equal((true, "Chemistry midterm", "exam", new DateTime(2026, 11, 5), TimeSpan.FromHours(9), true, true),
            (form.IsOpen, form.Title, form.What, form.Day, form.Time, form.Editing, form.Marks.CanMark));
        await form.Marks.MarkDoneCommand.ExecuteAsync(null);
        Assert.Equal(["|mine|42|done"], marked); // an item of the student's own is in no class for its mark
        Assert.False(form.IsOpen);

        // Delete asks first. No: the item stays and the form comes back as it was. Yes: it goes.
        form.Edit(item, sources.Zone);
        await form.DeleteCommand.ExecuteAsync(null);
        Assert.Equal(("Delete", true, 0), (Assert.Single(confirm.Asked).Yes, form.IsOpen, library.Requests.Count(r => r.Method == "DELETE")));
        confirm.Answer = true;
        await form.DeleteCommand.ExecuteAsync(null);
        Assert.Equal((false, 1, 0), (form.IsOpen, library.Requests.Count(r => r.Method == "DELETE"), feed.Due!.Groups.SelectMany(g => g.Items).Count()));
    }
}
