using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using StudyStash.App.Services;
using StudyStash.App.ViewModels;

namespace StudyStash.App.Tests;

/// <summary>Submit &amp; Organize's screens (test builds only): what Settings and the checklist decide. The library is a
/// stand-in; no folder on this computer is read or moved.</summary>
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

    static CanvasApi.HandInCheck Check(string mode, string found) => JsonSerializer.Deserialize<CanvasApi.HandInCheck>($$"""
        {"class": "CS 101", "key": "3:1:2026-10-01T10:00:00Z", "assignment": "Lab 3", "mode": "{{mode}}", "folders": ["/Users/sam/Downloads"],
         "file": {"name": "lab3.pdf", "path": {{(found == "none" ? "null" : "\"/Users/sam/Downloads/lab3.pdf\"")}}, "found": "{{found}}"},
         "materials": [{"path": "/Users/sam/Downloads/lab3-handout.pdf", "why": "Your professor attached a file of this name to the assignment", "sure": "high"},
                       {"path": "/Users/sam/Downloads/recursion notes.md", "why": "Its name shares words with the assignment", "sure": "medium"},
                       {"path": "/Users/sam/Downloads/todo.txt", "why": "You changed it while working on the assignment", "sure": "low"}]}
        """, CanvasApi.Json)!;

    static HandInModel Checklist(FakeLibrary library, List<ToastRequested> toasts, string? picks = null) =>
        new(CanvasFixtures.Context(library), new CanvasApi.HandInWaiting { Class = "CS 101", Key = "3:1:2026-10-01T10:00:00Z", Assignment = "Lab 3" },
            new HandInServices((_, _) => Task.FromResult(picks), toasts.Add));

    [Fact]
    public async Task Only_sure_files_are_ticked_the_button_says_the_mode_and_nothing_moves_before_the_press()
    {
        var library = new FakeLibrary().Json(HttpMethod.Post, "/api/v2/canvas/filing/file",
            """{"run": "r1", "class": "CS 101", "assignment": "Lab 3", "filed": 2, "left": []}""");
        var toasts = new List<ToastRequested>();
        var model = Checklist(library, toasts);
        model.Show(Check("organize", "unsure"));

        Assert.Equal("You handed in Lab 3", model.Title);
        Assert.Equal([true, false, false], model.Materials.Select(m => m.Ticked));
        Assert.Equal("File it", model.FileLabel);
        Assert.True(model.IsUnsure);
        Assert.Empty(library.Requests);

        // Unsure and not confirmed: the file handed in stays where it is; the ticked file is filed.
        await model.FileItCommand.ExecuteAsync(null);
        var body = JsonNode.Parse(library.Requests.Single().Body!)!;
        Assert.Null(body["file"]);
        Assert.Equal(["/Users/sam/Downloads/lab3-handout.pdf"], body["materials"]!.AsArray().Select(m => (string)m!));
        Assert.False((bool)body["clean"]!);
        Assert.True(model.Over);
        Assert.Equal("Filed in CS 101 / Lab 3", toasts.Single().Title);
        Assert.Equal("Undo", toasts.Single().Action);

        // Clean: the button says so, and Yes makes the file found the one filed.
        var clean = Checklist(library, toasts);
        clean.Show(Check("clean", "unsure"));
        Assert.Equal("File it and clear the rest", clean.FileLabel);
        clean.ItIsCommand.Execute(null);
        await clean.FileItCommand.ExecuteAsync(null);
        var cleaned = JsonNode.Parse(library.Requests[^1].Body!)!;
        Assert.Equal("/Users/sam/Downloads/lab3.pdf", (string)cleaned["file"]!);
        Assert.True((bool)cleaned["clean"]!);
    }

    [Fact]
    public async Task A_file_chosen_outside_the_folders_to_look_in_isnt_taken()
    {
        var model = Checklist(new FakeLibrary(), [], picks: "/Users/sam/Secret/lab3.pdf");
        model.Show(Check("organize", "none"));
        await model.ChooseCommand.ExecuteAsync(null);
        Assert.True(model.IsNone);
        Assert.Equal("Choose a file in one of your folders to look in: Downloads.", model.PickSay);

        var inside = Checklist(new FakeLibrary(), [], picks: "/Users/sam/Downloads/Lab 3/final.pdf");
        inside.Show(Check("organize", "none"));
        await inside.ChooseCommand.ExecuteAsync(null);
        Assert.True(inside.IsExact);
        Assert.Equal("final.pdf", inside.FileName);
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
