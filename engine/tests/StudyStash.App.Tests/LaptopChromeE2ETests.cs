using System.Diagnostics;
using StudyStash.App.Services;
using StudyStash.App.ViewModels;
using StudyStash.Core.Canvas;
using StudyStash.Core.Tests;
using StudyStash.Core.Tests.E2E;

namespace StudyStash.App.Tests;

/// <summary>
/// The laptop's whole Canvas setup in a real Chrome, against a library that has never synced: the app's own connect
/// steps (its <see cref="CanvasClient"/> over HTTP with the library password), the extension folder the laptop app
/// makes pointing at the library as another computer, Chrome for Testing signed in to a pretend Canvas, then Find my
/// courses, matching and a sync, and every Canvas screen's read afterwards. Skipped unless STUDYSTASH_E2E_CHROME is set
/// (engine/tests/extension-e2e.sh runs it).
/// </summary>
[Collection("LaptopChromeE2E")]
public sealed class LaptopChromeE2ETests
{
    /// <summary>The library's name as the laptop's Chrome knows it: another computer (Chrome maps it to this one).</summary>
    const string LibraryHost = "library.test";

    static async Task Until(Func<Task<bool>> done, string what, int seconds, ChromeRunner chrome)
    {
        var sw = Stopwatch.StartNew();
        while (!await done())
        {
            if (sw.Elapsed > TimeSpan.FromSeconds(seconds) || chrome.Exited)
                Assert.Fail($"Waited {sw.Elapsed.TotalSeconds:0} s for {what}.\nChrome{(chrome.Exited ? " (exited)" : "")}:\n{chrome.Tail(30)}");
            await Task.Delay(250);
        }
    }

    [Fact]
    public async Task A_laptop_connects_a_fresh_library_to_canvas_through_a_real_chrome()
    {
        Assert.SkipWhen(ChromeRunner.Binary.Length == 0, "Set STUDYSTASH_E2E_CHROME to a Chrome for Testing binary (engine/tests/extension-e2e.sh).");
        var stop = TestContext.Current.CancellationToken;
        await using var rig = await LibraryRig.StartAsync();
        await using var canvas = await CanvasServer.StartAsync(rig.Canvas);
        string laptopHome = rig.Scratch("laptop");
        string libraryAsTheLaptopSeesIt = $"http://{LibraryHost}:{rig.Port}";

        // The laptop app: its client reads the library over HTTP; its extension folder is made the way
        // CanvasContext.For makes it, pointing Chrome at the library by the laptop's address for it.
        var actions = new CanvasActions(
            OpenUrl: _ => { }, OpenInChrome: _ => { }, OpenChrome: () => { }, OpenChromeExtensions: () => { }, RevealFolder: _ => { },
            OpenFile: _ => { },
            PrepareExtension: (key, canvasUrl) => Extension.Ensure(Extension.Folder(laptopHome), libraryAsTheLaptopSeesIt, key, canvasUrl).Path);
        var context = rig.Context() with { Actions = actions, Home = laptopHome };
        var client = context.Client!;
        var watch = new CanvasWatch(context);
        using var m = new CanvasConnectModel(context, watch);

        var sw = Stopwatch.StartNew();
        var fresh = (await client.StateAsync(stop))!;
        Assert.Null(fresh.LastSync);
        await m.StartAsync(fresh, (await client.ClassesAsync(stop))!, stop);
        Assert.Equal(1, m.Current);
        m.SchoolField = canvas.Url;
        await m.ContinueCommand.ExecuteAsync(null);
        Assert.Null(m.SchoolError);
        Assert.Equal(2, m.Current);
        Assert.True(Extension.Ready(m.ExtensionFolder!), "the laptop app didn't make the extension's folder");

        using var chrome = ChromeRunner.Start(m.ExtensionFolder!, rig.Scratch("chrome-profile"), canvas.Url + "/login/e2e",
            CanvasServer.Host, CanvasServer.OtherHost, LibraryHost);

        // Chrome checks in with the library's key: the extension step moves on by itself, Find my courses runs, and the
        // steps land on matching each class to a course.
        await Until(async () =>
        {
            await watch.RefreshAsync(stop);
            return m.Current == 4;
        }, "the connect steps to reach matching", 90, chrome);
        Assert.Equal("Found 2 courses.", m.CoursesSay);
        Assert.Equal(["4201", "4202"], m.Courses.Select(r => r.Selected?.Id));
        var extension = (await client.ExtensionAsync(stop))!;
        Assert.True(extension.Connected);
        Assert.True(extension.KeyMatches);
        Assert.Equal("another_computer", extension.SeenWhere);
        TestContext.Current.SendDiagnosticMessage($"[e2e] matching {sw.Elapsed.TotalSeconds:0.0} s after the school's address");

        await m.LinkTheseCommand.ExecuteAsync(null);
        Assert.Null(m.LinkError);
        Assert.Equal(5, m.Current);
        await m.SyncNowCommand.ExecuteAsync(null);
        await Until(() => Task.FromResult(CanvasSettings.Load(rig.Home).LastDone.Length > 0), "the sync to finish", 120, chrome);
        TestContext.Current.SendDiagnosticMessage($"[e2e] synced {sw.Elapsed.TotalSeconds:0.0} s after the school's address");

        var state = (await client.StateAsync(stop))!;
        Assert.Equal("connected", state.Status);
        Assert.Equal("", CanvasSettings.Load(rig.Home).Error);
        Assert.NotNull(state.LastSync);
        var cs101 = (await client.AssignmentsAsync("CS 101", stop))!;
        var all = cs101.ToHandIn.Concat(cs101.Done).ToList();
        Assert.Equal(5, all.Count);
        foreach (var item in all) Assert.NotNull(await client.AssignmentAsync("CS 101", item.Id, stop));
        Assert.NotNull((await client.DueAsync(stop))!.Synced);
        Assert.NotEmpty((await client.ModulesAsync("CS 101", stop))!.Modules);
        Assert.NotEmpty((await client.AnnouncementsAsync("CS 101", stop))!.Items);
        // A file's bytes came through Chrome and reach the laptop through the library.
        string saved = Path.Combine(laptopHome, "ps4-answers.pdf");
        Assert.True(await client.DownloadAsync("CS 101", "Canvas/assignments/Problem set 4/submission/ps4-answers.pdf", saved, stop));
        Assert.Equal("%PDF-1.4 ps4 answers", File.ReadAllText(saved));
        Assert.All(canvas.Hits.Where(h => !h.PathAndQuery.StartsWith("/login", StringComparison.Ordinal)),
            h => Assert.True(h.SignedIn, $"{h.PathAndQuery} came without Chrome's Canvas session"));
    }
}

[CollectionDefinition("LaptopChromeE2E", DisableParallelization = true)]
public sealed class LaptopChromeE2ECollection;
