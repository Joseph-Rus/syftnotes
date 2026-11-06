using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia.Media;
using StudyStash.App.Services;
using StudyStash.App.ViewModels;

namespace StudyStash.App.Tests;

/// <summary>Submit &amp; Organize's screens (test builds only): what Settings, Ready to tidy and the tidy card decide. The
/// library is a stand-in; no folder on this computer is read or moved.</summary>
public class SubmitOrganizeTests
{
    const string Filing = """
        {"on": true, "folders": ["/Users/sam/Downloads"], "suggested": [{"name": "Desktop", "path": "/Users/sam/Desktop"}],
         "when": "ask", "mode": "organize", "waiting": [],
         "runs": [{"run": "a1", "at": "2026-10-01T10:00:00Z", "class": "CS 101", "assignment": "Lab 3", "files": 3, "undone": 0},
                  {"run": "b2", "at": "2026-10-03T09:00:00Z", "class": "BIO 110", "assignment": "Essay 2", "files": 1, "undone": 0},
                  {"run": "c3", "at": "2026-09-20T15:00:00Z", "class": "CS 101", "assignment": "Lab 2", "files": 2, "undone": 2}]}
        """;

    [Fact]
    public async Task A_refused_folder_is_said_and_not_added_and_runs_list_newest_first()
    {
        const string why = "That's your whole home folder. Choose a folder in it, like Downloads or Documents.";
        var library = new FakeLibrary()
            .Json(HttpMethod.Get, "/api/v2/canvas/filing", Filing)
            .Status(HttpMethod.Post, "/api/v2/canvas/filing", HttpStatusCode.BadRequest, new JsonObject { ["detail"] = why }.ToJsonString());
        var model = new SubmitOrganizeModel(CanvasFixtures.Context(library));
        await model.LoadAsync();

        Assert.Equal(["Essay 2", "Lab 3", "Lab 2"], model.Runs.Select(r => r.Title));
        Assert.Equal([true, true, false], model.Runs.Select(r => r.CanUndo));

        await model.AddAsync("/Users/sam");
        Assert.Equal(why, model.Saving.Say);
        Assert.Equal(["/Users/sam/Downloads"], model.Folders.Select(f => f.Path));
        Assert.Contains(library.Requests, r => r.Method == "POST" && r.Body!.Contains("\"add\":\"/Users/sam\"", StringComparison.Ordinal));
    }

    static readonly CanvasApi.HandInWaiting Lab3 = new() { Class = "CS 101", Key = "3:1:2026-10-01T10:00:00Z", Assignment = "Lab 3", Id = "3" };

    static string CheckJson(string mode, string found) => $$"""
        {"class": "CS 101", "key": "3:1:2026-10-01T10:00:00Z", "assignment": "Lab 3", "mode": "{{mode}}", "folders": ["/Users/sam/Downloads"],
         "file": {"name": "lab3.pdf", "path": {{(found == "none" ? "null" : "\"/Users/sam/Downloads/lab3.pdf\"")}}, "found": "{{found}}"},
         "materials": [{"path": "/Users/sam/Downloads/lab3-handout.pdf", "why": "Your professor attached a file of this name to the assignment", "sure": "high"},
                       {"path": "/Users/sam/Downloads/recursion notes.md", "why": "Its name shares words with the assignment", "sure": "medium"},
                       {"path": "/Users/sam/Downloads/todo.txt", "why": "You changed it while working on the assignment", "sure": "low"}]}
        """;

    [Fact]
    public async Task Ready_to_tidy_appears_tells_once_ever_and_goes_when_dismissed()
    {
        using var home = new TempHome();
        var library = new FakeLibrary()
            .Json(HttpMethod.Get, "/api/v2/canvas/filing/check", CheckJson("organize", "exact"))
            .Json(HttpMethod.Post, "/api/v2/canvas/filing", Filing);
        var list = new TidyList(CanvasFixtures.Context(library, home: home.Path));
        var told = new List<TidyItem>();
        list.Arrived += told.Add;

        await list.FollowAsync([Lab3]);
        var item = Assert.Single(list.Items);
        Assert.Equal(2, item.Files); // the file handed in, and the one file Syft Notes is sure of
        Assert.Equal(("Lab 3 handed in", "2 files to tidy"), (FilingWords.HandedIn(item.Waiting.Assignment), FilingWords.ToTidy(item.Files)));
        await list.FollowAsync([Lab3]);
        Assert.Single(told);

        // Home's part shows while something waits, and hides when nothing does.
        var page = new OverviewModel { IsHome = true };
        page.ShowTidy([new TidyRow(item.Id, Brushes.Gray, "Lab 3", FilingWords.RowLine(item.Files, ""), () => { }, () => { })]);
        Assert.True(page.HasTidy);

        // After a restart it's still on the list, but isn't told of again.
        var again = new TidyList(CanvasFixtures.Context(library, home: home.Path));
        again.Arrived += told.Add;
        await again.FollowAsync([Lab3]);
        Assert.Single(again.Items);
        Assert.Single(told);

        Assert.True((await again.DismissAsync(again.Items[0])).Ok);
        Assert.Empty(again.Items);
        Assert.Contains(library.Requests, r => r.Method == "POST" && r.Body!.Contains("\"dismiss\"", StringComparison.Ordinal));
        page.ShowTidy([]);
        Assert.False(page.HasTidy);

        // Gone from the library's list (tidied elsewhere): gone from here.
        await list.FollowAsync([]);
        Assert.Empty(list.Items);
    }

    static TidyCardModel Card(FakeLibrary library, string mode, string found, List<TidyItem>? tidied = null, string? picks = null) =>
        new(CanvasFixtures.Context(library), new TidyItem(Lab3, JsonSerializer.Deserialize<CanvasApi.HandInCheck>(CheckJson(mode, found), CanvasApi.Json)!),
            Brushes.Gray, new TidyServices((_, _) => Task.FromResult(picks), i => tidied?.Add(i)));

    [Fact]
    public async Task Sure_files_start_on_a_tap_toggles_the_button_says_the_mode_and_nothing_moves_before_tidy_up()
    {
        var library = new FakeLibrary().Json(HttpMethod.Post, "/api/v2/canvas/filing/file",
            """{"run": "r1", "class": "CS 101", "assignment": "Lab 3", "filed": 2, "left": []}""");
        var tidied = new List<TidyItem>();
        var card = Card(library, "organize", "unsure", tidied);

        Assert.Equal([true, false, false], card.Materials.Select(m => m.On));
        card.Materials[2].ToggleCommand.Execute(null);
        card.Materials[0].ToggleCommand.Execute(null);
        Assert.Equal([false, false, true], card.Materials.Select(m => m.On));
        Assert.Equal("Tidy up", card.TidyLabel);
        Assert.True(card.Unsure);
        Assert.Empty(library.Requests);

        // Still unsure: the file found stays where it is; the files that are on move.
        await card.TidyUpCommand.ExecuteAsync(null);
        var body = JsonNode.Parse(library.Requests.Single().Body!)!;
        Assert.Null(body["file"]);
        Assert.Equal(["/Users/sam/Downloads/todo.txt"], body["materials"]!.AsArray().Select(m => (string)m!));
        Assert.False((bool)body["clean"]!);
        Assert.True(card.Filed && card.Moving);
        Assert.Single(tidied);

        // Clean says so on its button, and Yes makes the file found the one that moves.
        var clean = Card(library, "clean", "unsure");
        Assert.Equal("Tidy up and clear the rest", clean.TidyLabel);
        clean.ItIsCommand.Execute(null);
        await clean.TidyUpCommand.ExecuteAsync(null);
        var cleaned = JsonNode.Parse(library.Requests[^1].Body!)!;
        Assert.Equal("/Users/sam/Downloads/lab3.pdf", (string)cleaned["file"]!);
        Assert.True((bool)cleaned["clean"]!);
    }

    [Fact]
    public async Task A_file_chosen_outside_the_folders_to_look_in_isnt_taken()
    {
        var card = Card(new FakeLibrary(), "organize", "none", picks: "/Users/sam/Secret/lab3.pdf");
        Assert.True(card.NoHandedIn);
        await card.ChooseCommand.ExecuteAsync(null);
        Assert.True(card.NoHandedIn);
        Assert.Equal("Choose a file in one of your folders to look in: Downloads.", card.PickSay);

        var inside = Card(new FakeLibrary(), "organize", "none", picks: "/Users/sam/Downloads/Lab 3/final.pdf");
        await inside.ChooseCommand.ExecuteAsync(null);
        Assert.Equal("final.pdf", inside.HandedIn!.Name);
    }

    [Fact]
    public void Undo_says_each_outcome()
    {
        Assert.Equal("2 files put back where they were.", FilingWords.Undone(["back", "back"]));
        Assert.Equal("1 file put back beside a file now in its place. 1 couldn't be put back (it may be open). Try Undo again.",
            FilingWords.Undone(["beside", "failed"]));
        string trash = OperatingSystem.IsWindows() ? "Recycle Bin" : "Trash";
        Assert.Equal($"1 is in the {trash}: restore it from the {trash}.", FilingWords.Undone(["in_trash"]));
    }
}
