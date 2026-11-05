using System.Text.Json.Nodes;
using StudyStash.App.Services;
using StudyStash.App.ViewModels;
using StudyStash.Core.Canvas;

namespace StudyStash.App.Tests;

/// <summary>The old extension folder inside the home still kept up to date for a Chrome that loaded it, without a second
/// folder made where Chrome's picker shows it; and the Chrome step saying which folder to pick, or to drag it.</summary>
public class ExtensionFolderMoveTests
{
    const string Library = """
        {"key": "key-after", "canvas": "https://school.instructure.com", "version": "1.4", "protocol": 3,
         "folder": "/elsewhere/chrome-extension", "folder_ready": true, "connected": true, "seen_where": "this_computer"}
        """;

    static string Key(string folder) => JsonNode.Parse(File.ReadAllText(Path.Combine(folder, "config.json")))!["key"]!.GetValue<string>();

    [Fact]
    public async Task A_connected_Chrome_s_old_folder_follows_the_new_key_and_no_second_folder_is_made()
    {
        using var home = new TempHome();
        using var visible = new TempHome();
        string old = Extension.InHome(home.Path);
        Extension.Ensure(old, "https://mini.tail.ts.net", "key-before", "https://school.instructure.com");
        var fake = new FakeLibrary().Json(HttpMethod.Get, "/api/v2/canvas/extension", Library);
        var keeper = new ExtensionKeeper(home.Path, () => new CanvasClient("https://mini.tail.ts.net", "test-key", fake.Client()))
        {
            LocalFolder = Path.Combine(visible.Path, "Syft Notes", "Chrome extension"),
        };

        Assert.True(await keeper.KeepAsync(TestContext.Current.CancellationToken));
        Assert.Equal("key-after", Key(old));
        Assert.False(Directory.Exists(keeper.LocalFolder)); // only a Chrome that loads a folder gets one
        Assert.False(keeper.FolderReady);
    }

    [Fact]
    public void The_Chrome_step_names_the_folder_and_says_it_can_be_dragged()
    {
        var context = CanvasFixtures.Context();
        var model = new CanvasConnectModel(context, new CanvasWatch(context));
        Assert.Equal("Pick the Chrome extension folder, or drag it onto the page", model.PickCaption);
        model.ExtensionFolder = Path.Combine("home", "Syft Notes", "Chrome extension");
        Assert.Equal("Chrome extension", model.FolderName);
        model.ExtensionFolder = Path.Combine("home", ".study-stash", "chrome-extension");
        Assert.Equal("Pick the chrome-extension folder, or drag it onto the page", model.PickCaption);
    }
}
