using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StudyStash.App.Controls;
using StudyStash.App.Services;
using StudyStash.App.ViewModels;
using StudyStash.App.ViewModels.Kit;
using StudyStash.App.Views;

namespace StudyStash.App.Tests;

/// <summary>The Due calendar's Add and the form on an item of the student's own, in a real window with its dialog layer: Add opens the
/// form and what Save sends is what was typed; a click on the item opens it again; Mark done goes through the marks; Delete steps the
/// form aside to ask, and a no brings the form back as it was while a yes takes the item off the one copy of the list.</summary>
public class DueOwnUiTests
{
    const string Added = """
        {"to_hand_in": 1, "groups": [{"key": "later", "label": "Later", "items": [
          {"class": "", "id": 42, "name": "Chemistry midterm", "kind": "mine", "what": "exam", "all_day": false, "status": "open", "mark": "",
           "due": "2026-11-05T09:00", "due_at": "2026-11-05T14:00:00Z", "url": ""}]}], "hidden": []}
        """;
    const string Empty = """{"to_hand_in": 0, "groups": [], "hidden": []}""";

    /// <summary>A button in the form pressed: its command runs (a pointer doesn't reach into a dialog layer in a headless window).</summary>
    static void Press(OwnDueForm form, string name)
    {
        var button = form.FindControl<Button>(name)!;
        Assert.True(button.IsEffectivelyVisible && button.IsEffectivelyEnabled, $"{name} can be pressed");
        button.Command!.Execute(button.CommandParameter);
    }

    /// <summary>The form on the screen now: a new one, not the one before it (the dialog layer lets go of a closed one a moment late).</summary>
    static async Task<OwnDueForm> FormShowingAsync(Window window, OwnDueForm? before = null)
    {
        OwnDueForm? form = null;
        await Wait.UntilAsync(() => (form = window.GetVisualDescendants().OfType<OwnDueForm>().FirstOrDefault(f => !ReferenceEquals(f, before))) is { IsLoaded: true, IsEffectivelyVisible: true }, "the form to show");
        return form!;
    }

    [AvaloniaFact]
    public async Task Add_sends_what_was_typed_a_click_opens_the_item_and_Delete_asks_before_it_goes()
    {
        var library = new FakeLibrary().Json(HttpMethod.Post, "/api/v2/due/mine", Added).Json(HttpMethod.Delete, "/api/v2/due/mine/42", Empty);
        using var feed = new CanvasFeed(CanvasFixtures.Context(library));
        var marked = new List<string>();
        var zone = TimeZoneInfo.CreateCustomTimeZone("Test", TimeSpan.FromHours(-5), "Test", "Test");
        var sources = new OverviewModel.Sources
        {
            Now = new DateTimeOffset(2026, 11, 4, 10, 0, 0, TimeSpan.FromHours(-5)), Zone = zone,
            Classes = [("Chemistry", Avalonia.Media.Brushes.Gray, 3)],
            Own = new OwnDueEditor((d, ct) => feed.SaveOwnAsync(d, ct), (id, ct) => feed.DeleteOwnAsync(id, ct)),
            Mark = (cls, kind, id, mark, _) =>
            {
                marked.Add($"{cls}|{kind}|{id}|{mark}");
                return Task.FromResult(Outcome.Done);
            },
        };
        var calendar = new DueCalendarModel(sources);
        var form = calendar.Own;
        ((App)Application.Current!).UseSkin(SkinKind.Mac);
        var window = new Window { Width = 1000, Height = 800, RequestedThemeVariant = ThemeVariant.Light };
        window.Content = ConfirmLayer.Wrap(window, new DueCalendar { DataContext = calendar });
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            var host = ConfirmLayer.For(window)!.Host;

            // Add: the form opens on today, can't be saved without a title, and sends what was typed.
            MenuPickTests.Click(window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "AddOwn"));
            var shown = await FormShowingAsync(window);
            Assert.Equal((new DateTime(2026, 11, 4), false), (form.Day, form.CanSave));
            form.Title = "  Chemistry midterm ";
            form.Day = new DateTime(2026, 11, 5);
            form.Time = TimeSpan.FromHours(9);
            form.Chosen = form.Classes.Single(c => c.Name == "Chemistry");
            Press(shown, "Save");
            await Wait.UntilAsync(() => library.Requests.Any(r => r.Method == "POST"), "Save to send");
            await Wait.UntilAsync(() => feed.Due is not null, "the answer to be taken in");
            await Wait.UntilAsync(() => !host.IsOpen, "the form to close");
            Assert.Equal("""{"title":"Chemistry midterm","what":"exam","class":"Chemistry","day":"2026-11-05","time":"09:00"}""",
                Assert.Single(library.Requests, r => r.Method == "POST" && r.Path == "/api/v2/due/mine").Body);
            var item = Assert.Single(feed.Due!.Groups.SelectMany(g => g.Items)); // the library's answer is the one copy now
            Assert.True(item.Own);
            // Home says it the way it says Canvas's: with no Canvas linked, the exam is "tomorrow at 9:00 AM".
            Assert.Equal("Chemistry midterm is tomorrow at 9:00 AM.", OverviewModel.Home(sources with { Due = feed.Due }).Lede);

            // A click on the item opens the same form on it, as it was saved; Mark done goes through the marks, in no class, and closes it.
            form.Edit(item, zone);
            shown = await FormShowingAsync(window, shown);
            Assert.Equal(("Chemistry midterm", "exam", new DateTime(2026, 11, 5), TimeSpan.FromHours(9), true), (form.Title, form.What, form.Day, form.Time, form.Editing));
            Press(shown, "MarkDone");
            await Wait.UntilAsync(() => !host.IsOpen, "the form to close on Mark done");
            Assert.Equal(["|mine|42|done"], marked);

            // Delete steps the form aside for the question. No: the item stays and the form comes back. Yes: the item goes.
            form.Edit(item, zone);
            shown = await FormShowingAsync(window, shown);
            Press(shown, "Delete");
            await Wait.UntilAsync(() => window.GetVisualDescendants().OfType<ConfirmDialog>().FirstOrDefault() is { IsEffectivelyVisible: true }, "the question");
            host.CurrentSession!.Close(false);
            shown = await FormShowingAsync(window, shown);
            Assert.Equal((true, 0), (form.IsOpen, library.Requests.Count(r => r.Method == "DELETE")));
            Press(shown, "Delete");
            await Wait.UntilAsync(() => window.GetVisualDescendants().OfType<ConfirmDialog>().FirstOrDefault() is { IsEffectivelyVisible: true }, "the question again");
            host.CurrentSession!.Close(true);
            await Wait.UntilAsync(() => !host.IsOpen && feed.Due!.Groups.All(g => g.Items.Count == 0), "the item to go");
            Assert.Equal((false, 1), (form.IsOpen, library.Requests.Count(r => r.Method == "DELETE")));
        }
        finally
        {
            window.Close(); // a failure leaves no dialog layer behind for the next test
        }
    }

    /// <summary>Past, their exam is simply behind them, on its day and not struck through; their to-do not ticked off is overdue. Neither
    /// reads "missing", which is for Canvas work that wasn't handed in.</summary>
    [Fact]
    public void A_past_exam_stays_on_its_day_and_a_past_todo_is_overdue_never_missing()
    {
        var zone = TimeZoneInfo.CreateCustomTimeZone("Test", TimeSpan.FromHours(-5), "Test", "Test");
        var exam = new CanvasApi.Item { Class = "", Id = "1", Name = "Statistics final", Kind = "mine", What = "exam", Status = "open", DueAt = new DateTimeOffset(2026, 11, 2, 9, 0, 0, TimeSpan.FromHours(-5)) };
        var todo = new CanvasApi.Item { Class = "", Id = "2", Name = "Return library books", Kind = "mine", What = "task", Status = "overdue", AllDay = true, DueAt = new DateTimeOffset(2026, 11, 3, 23, 59, 0, TimeSpan.FromHours(-5)) };
        var sources = new OverviewModel.Sources
        {
            Now = new DateTimeOffset(2026, 11, 4, 10, 0, 0, TimeSpan.FromHours(-5)), Zone = zone,
            Due = new CanvasApi.DueResponse { ToHandIn = 1, Groups = [new CanvasApi.Group { Key = "overdue", Items = [todo] }], Behind = [exam] },
        };

        Assert.Equal("Return library books is overdue.", OverviewModel.DueSentence(sources));
        // With what's due next and Canvas work missing as well: one "and", before the last.
        var tonight = new CanvasApi.Item { Class = "CS 101", Id = "3", Name = "Reading quiz 6", Kind = "quiz", Status = "open", DueAt = new DateTimeOffset(2026, 11, 4, 23, 59, 0, TimeSpan.FromHours(-5)) };
        var missing = new CanvasApi.Item { Class = "CS 101", Id = "4", Name = "Asynchronous Lecture 5", Kind = "assignment", Status = "missing", Missing = true, DueAt = new DateTimeOffset(2026, 10, 30, 23, 59, 0, TimeSpan.FromHours(-5)) };
        CanvasApi.DueResponse Due(params CanvasApi.Item[] items) => new()
        {
            Groups = [new CanvasApi.Group { Key = "overdue", Items = [.. items.Where(i => i.Status != "open")] }, new CanvasApi.Group { Key = "week", Items = [.. items.Where(i => i.Status == "open")] }],
        };
        Assert.Equal(["Reading quiz 6 is due tonight, Asynchronous Lecture 5 is still missing, and Return library books is overdue.",
                "Reading quiz 6 is due tonight, and Return library books is overdue.",
                "Asynchronous Lecture 5 is still missing, and Return library books is overdue."],
            [OverviewModel.DueSentence(sources with { Due = Due(tonight, missing, todo) }), OverviewModel.DueSentence(sources with { Due = Due(tonight, todo) }),
                OverviewModel.DueSentence(sources with { Due = Due(missing, todo) })]);
        var shown = new DueCalendarModel(sources).Cells.SelectMany(c => c.Shown).ToDictionary(i => i.Title);
        Assert.Equal(("Exam · 9:00 AM", false, false), (shown["Statistics final"].Sub, shown["Statistics final"].Missing, shown["Statistics final"].Done));
        Assert.Equal(("To do · Overdue", true), (shown["Return library books"].Sub, shown["Return library books"].Missing));
        Assert.Equal(["Exam · 9:00 AM"], OverviewModel.WeekOf(sources, _ => Avalonia.Media.Brushes.Gray, new DateTime(2026, 11, 1)).SelectMany(d => d.Deadlines).Where(d => d.Title == "Statistics final").Select(d => d.When));
    }
}
