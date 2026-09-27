using System.Diagnostics;
using System.Text.Json.Nodes;
using StudyStash.App.Services;
using StudyStash.App.ViewModels;
using StudyStash.Core.Canvas;
using StudyStash.Core.Tests;

namespace StudyStash.App.Tests;

/// <summary>The app's Canvas client and the connect steps against a real library (<see cref="LibraryRig"/>), not
/// hand-written JSON: a library that has never synced reads cleanly, and connecting goes all the way from the
/// school's address through Find my courses, matching and a sync.</summary>
public class LibraryCanvasTests
{
    /// <summary>Every key the Canvas API uses for a date.</summary>
    static readonly HashSet<string> DateKeys =
    [
        "last_sync", "next_sync", "paused_until", "seen", "at", "when", "synced", "due", "due_at", "submitted", "graded_at", "marked_done",
        "unlock_at", "lock_at", "submitted_at", "posted_at", "updated_at", "extension_seen",
    ];

    /// <summary>Where in <paramref name="node"/> a date is "" (the library sends null for a date it doesn't have).</summary>
    static IEnumerable<string> BlankDates(JsonNode? node, string where = "$")
    {
        switch (node)
        {
            case JsonObject o:
                foreach (var (k, v) in o)
                {
                    if (DateKeys.Contains(k) && v is JsonValue s && s.TryGetValue(out string? text) && text.Trim().Length == 0) yield return $"{where}.{k}";
                    foreach (var inner in BlankDates(v, $"{where}.{k}")) yield return inner;
                }
                break;
            case JsonArray a:
                for (int i = 0; i < a.Count; i++)
                    foreach (var inner in BlankDates(a[i], $"{where}[{i}]")) yield return inner;
                break;
        }
    }

    static async Task NoBlankDatesAsync(LibraryRig rig, params string[] paths)
    {
        foreach (string path in paths) Assert.Empty(BlankDates(await rig.RawAsync(path), path));
    }

    static readonly string[] EveryRead =
    [
        "/api/v2/canvas", "/api/v2/canvas/state", "/api/v2/canvas/extension", "/api/v2/canvas/classes", "/api/v2/canvas/due",
        "/api/v2/canvas/notifications", "/api/v2/canvas/assignments?class=CS%20101", "/api/v2/canvas/modules?class=CS%20101",
        "/api/v2/canvas/files?class=CS%20101", "/api/v2/canvas/announcements?class=CS%20101", "/api/v2/canvas/pages?class=CS%20101",
    ];

    static async Task Until(Func<Task<bool>> done, string what, int seconds = 30)
    {
        var sw = Stopwatch.StartNew();
        while (!await done())
        {
            Assert.True(sw.Elapsed < TimeSpan.FromSeconds(seconds), $"waited {seconds} s for {what}");
            await Task.Delay(50);
        }
    }

    [Fact]
    public async Task A_library_that_has_never_synced_sends_null_for_every_date_it_does_not_have()
    {
        await using var rig = await LibraryRig.StartAsync();
        await NoBlankDatesAsync(rig, EveryRead);
        var state = await rig.RawAsync("/api/v2/canvas/state");
        Assert.Null(state["last_sync"]);
        Assert.Null(state["next_sync"]);
        Assert.Null(state["extension"]!["seen"]);
        Assert.Null((await rig.RawAsync("/api/v2/canvas"))["last_sync"]);
    }

    [Fact]
    public async Task The_app_reads_every_answer_of_a_library_that_has_never_synced()
    {
        await using var rig = await LibraryRig.StartAsync();
        var client = rig.Client();
        var stop = TestContext.Current.CancellationToken;

        var state = await client.StateAsync(stop);
        Assert.Equal("not_set_up", state!.Status);
        Assert.Null(state.LastSync);
        Assert.Null(state.NextSync);
        Assert.Null(state.Extension?.Seen);
        var overview = await client.OverviewAsync(stop);
        Assert.Null(overview!.LastSync);
        Assert.Equal("", overview.ExtensionSeen);
        var extension = await client.ExtensionAsync(stop);
        Assert.Equal(rig.ExtensionKey, extension!.Key);
        Assert.Equal("", extension.Seen);
        Assert.False(extension.Connected);
        var classes = (await client.ClassesAsync(stop))!;
        Assert.Equal(["CS 101", "BIO 110"], classes.Select(c => c.Class));
        Assert.All(classes, c => Assert.Null(c.LastSync));
        Assert.All(classes, c => Assert.Null(c.Scout?.When));
        var due = await client.DueAsync(stop);
        Assert.Null(due!.Synced);
        Assert.Equal(0, due.ToHandIn);
        Assert.Empty((await client.NotificationsAsync(stop: stop))!.Items);
        Assert.Empty((await client.AssignmentsAsync("CS 101", stop))!.ToHandIn);
        Assert.Empty((await client.ModulesAsync("CS 101", stop))!.Modules);
        Assert.Empty((await client.FilesAsync("CS 101", stop))!.Files);
        Assert.Empty((await client.AnnouncementsAsync("CS 101", stop))!.Items);
    }

    /// <summary>The exact case that broke: a fresh library, no sync yet, and the connect steps from the school's
    /// address to Find my courses (which read a blank last-sync date and stopped), matching, and a sync.</summary>
    [Fact]
    public async Task A_fresh_library_connects_finds_courses_links_them_and_syncs_through_the_connect_steps()
    {
        await using var rig = await LibraryRig.StartAsync();
        var stop = TestContext.Current.CancellationToken;
        var context = rig.Context();
        var client = context.Client!;
        var watch = new CanvasWatch(context);
        using var m = new CanvasConnectModel(context, watch);

        await m.StartAsync((await client.StateAsync(stop))!, (await client.ClassesAsync(stop))!, stop);
        Assert.Equal(1, m.Current);
        m.SchoolField = FakeCanvas.Base;
        await m.ContinueCommand.ExecuteAsync(null);
        Assert.Null(m.SchoolError);
        Assert.Equal(2, m.Current); // no Chrome has checked in yet

        using var chrome = CancellationTokenSource.CreateLinkedTokenSource(stop);
        var extension = rig.RunExtensionAsync(chrome.Token);
        try
        {
            // Chrome checks in: the step moves on by itself, finds the courses and lands on matching them.
            await Until(async () =>
            {
                await watch.RefreshAsync(stop);
                return m.Current == 4;
            }, "the connect steps to reach matching");
            Assert.Equal("Found 2 courses.", m.CoursesSay);
            Assert.Equal(["CS 101", "BIO 110"], m.Courses.Select(r => r.Class));
            Assert.Equal("4201", m.Courses[0].Selected?.Id); // suggested from the course code, preselected
            Assert.Equal("4202", m.Courses[1].Selected?.Id);

            await m.LinkTheseCommand.ExecuteAsync(null);
            Assert.Null(m.LinkError);
            Assert.Equal(5, m.Current);
            await m.SyncNowCommand.ExecuteAsync(null);
            await Until(() => Task.FromResult(CanvasSettings.Load(rig.Home).LastDone.Length > 0), "the sync to finish", 60);
        }
        finally
        {
            chrome.Cancel();
            await extension;
        }

        var state = await client.StateAsync(stop);
        Assert.Equal("connected", state!.Status);
        Assert.NotNull(state.LastSync);
        Assert.NotNull(state.NextSync);
        var due = await client.DueAsync(stop);
        Assert.NotNull(due!.Synced);
        var cs101 = await client.AssignmentsAsync("CS 101", stop);
        var all = cs101!.ToHandIn.Concat(cs101.Done).ToList();
        Assert.Equal(5, all.Count);
        foreach (var item in all) Assert.NotNull(await client.AssignmentAsync("CS 101", item.Id, stop));
        Assert.NotEmpty((await client.ModulesAsync("CS 101", stop))!.Modules);
        Assert.NotEmpty((await client.AnnouncementsAsync("CS 101", stop))!.Items);
        Assert.NotNull(await client.FilesAsync("CS 101", stop));
        Assert.NotNull(await client.NotificationsAsync(stop: stop));
        var classes = await client.ClassesAsync(stop);
        Assert.All(classes!, c => Assert.True(c.Linked));
        Assert.NotNull(classes!.Single(c => c.Class == "CS 101").LastSync);
        await NoBlankDatesAsync(rig, EveryRead);
        await NoBlankDatesAsync(rig, all.Select(a => $"/api/v2/canvas/assignment?class=CS%20101&id={a.Id}").ToArray());
    }
}
