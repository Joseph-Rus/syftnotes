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
/// courses, bringing them in and a sync, and every Canvas screen's read afterwards. Skipped unless STUDYSTASH_E2E_CHROME is set
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
            OpenUrl: _ => { }, OpenInBrowser: _ => { }, OpenBrowser: () => { }, Browsers: () => [Browsers.Chrome], RememberBrowser: _ => { },
            OpenExtensions: _ => null, OpenAddOn: _ => null, RevealFolder: _ => { },
            OpenFile: _ => { },
            PrepareExtension: (key, canvasUrl) => Extension.Ensure(Extension.Folder(laptopHome), libraryAsTheLaptopSeesIt, key, canvasUrl).Path,
            Copy: _ => { });
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
        // The laptop's own folder, made the way Add to Chrome makes it for a library on another computer. (The rig's
        // library answers on 127.0.0.1, so the button itself would take the library's own folder, which points at a
        // library on this computer's usual port, not at the rig.)
        var made = (await client.ExtensionAsync(stop))!;
        string folder = actions.PrepareExtension(made.Key, made.Canvas);
        Assert.True(Extension.Ready(folder), "the laptop app didn't make the extension's folder");
        Assert.Equal(libraryAsTheLaptopSeesIt, Extension.Connection(folder)?.App);

        using var chrome = ChromeRunner.Start(folder, rig.Scratch("chrome-profile"), canvas.Url + "/login/e2e",
            CanvasServer.Host, CanvasServer.OtherHost, LibraryHost);

        // Chrome checks in with the library's key: the extension step moves on by itself and Find my courses runs,
        // offering both courses (this term's, so both ticked).
        await Until(async () =>
        {
            await watch.RefreshAsync(stop);
            return m.Current == 3 && m.Picker.HasCourses;
        }, "the connect steps to find the courses", 90, chrome);
        Assert.Equal("Found 2 courses. Tick the ones to bring in.", m.CoursesSay);
        Assert.Equal(["4201", "4202"], m.Picker.Courses.Where(c => c.Ticked).Select(c => c.Id).Order());
        var extension = (await client.ExtensionAsync(stop))!;
        Assert.True(extension.Connected);
        Assert.True(extension.KeyMatches);
        Assert.Equal("another_computer", extension.SeenWhere);
        // The extension says which browser it's in, and the app reads it from the library (Chrome for Testing calls itself Chromium).
        Assert.Equal("Chromium", (await client.StateAsync(stop))!.Extension?.Browser);
        TestContext.Current.SendDiagnosticMessage($"[e2e] courses found {sw.Elapsed.TotalSeconds:0.0} s after the school's address");

        // Bringing them in links each to the class this library already has for it, so it's straight on to the sync.
        await m.BringInCommand.ExecuteAsync(null);
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
