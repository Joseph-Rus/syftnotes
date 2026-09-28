using System.Text.Json.Nodes;
using StudyStash.App.Services;
using StudyStash.App.ViewModels;
using StudyStash.Core.Canvas;

namespace StudyStash.App.Tests;

/// <summary>The laptop's extension folder where Chrome's picker shows it, with the old one inside the home still kept
/// up to date for a Chrome that loaded it; and the Chrome step saying which folder to pick, or to drag it.</summary>
public class ExtensionFolderMoveTests
{
    const string Library = """
        {"key": "key-after", "canvas": "https://school.instructure.com", "version": "1.4", "protocol": 3,
         "folder": "/elsewhere/chrome-extension", "folder_ready": true, "connected": true, "seen_where": "this_computer"}
        """;

    static string Key(string folder) => JsonNode.Parse(File.ReadAllText(Path.Combine(folder, "config.json")))!["key"]!.GetValue<string>();

    [Fact]
    public async Task The_new_folder_is_written_and_a_connected_Chrome_s_old_folder_follows_it()
    {
        using var home = new TempHome();
        using var visible = new TempHome();
        string old = Extension.InHome(home.Path);
        Extension.Ensure(old, "https://mini.tail.ts.net", "key-before", "https://school.instructure.com");
        var fake = new FakeLibrary().Json(HttpMethod.Get, "/api/v2/canvas/extension", Library);
        var keeper = new ExtensionKeeper(home.Path, () => new CanvasClient("https://mini.tail.ts.net", "test-key", fake.Client()))
        {
            LocalFolder = Path.Combine(visible.Path, "Study Stash", "Chrome extension"),
        };

        Assert.True(await keeper.KeepAsync(TestContext.Current.CancellationToken));
        Assert.True(keeper.FolderReady);
        Assert.True(Extension.Ready(keeper.LocalFolder));
        Assert.Equal("key-after", Key(keeper.LocalFolder));
        Assert.Equal("key-after", Key(old));
    }

    [Fact]
    public void The_Chrome_step_names_the_folder_and_says_it_can_be_dragged()
    {
        var context = CanvasFixtures.Context();
        var model = new CanvasConnectModel(context, new CanvasWatch(context));
        Assert.Equal("Pick the Chrome extension folder, or drag it onto the page", model.PickCaption);
        model.ExtensionFolder = Path.Combine("home", "Study Stash", "Chrome extension");
        Assert.Equal("Chrome extension", model.FolderName);
        model.ExtensionFolder = Path.Combine("home", ".study-stash", "chrome-extension");
        Assert.Equal("Pick the chrome-extension folder, or drag it onto the page", model.PickCaption);
    }
}
