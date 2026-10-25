using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using StudyStash.Core;
using StudyStash.Core.Ai;
using StudyStash.Core.PhoneDrive;
using StudyStash.Core.Tests;

namespace StudyStash.App.Tests;

/// <summary>
/// By hand only, for checking the iPhone app against the laptop's real link: a throwaway library (12 lectures, a
/// scripted Claude) and the laptop's link on real Google Drive under STUDYSTASH_DRIVE_SPACE, held open for
/// STUDYSTASH_DRIVE_HOLD_SECONDS while a simulator runs the app with the same token and prefix. The link's lines go to
/// STUDYSTASH_DRIVE_CHECK_OUT. Every file with the prefix is deleted at the end. Skipped without the token.
/// </summary>
public class PhoneDriveHold
{
    static CancellationToken Test => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Hold_the_laptop_side_open()
    {
        string? tokenPath = Environment.GetEnvironmentVariable("STUDYSTASH_DRIVE_CHECK_TOKEN");
        Assert.SkipUnless(tokenPath is { Length: > 0 } && File.Exists(tokenPath) && Environment.GetEnvironmentVariable("STUDYSTASH_DRIVE_HOLD_SECONDS") is { Length: > 0 },
            "set STUDYSTASH_DRIVE_CHECK_TOKEN and STUDYSTASH_DRIVE_HOLD_SECONDS");
        var token = JsonNode.Parse(File.ReadAllText(tokenPath!))!.AsObject();
        string prefix = Environment.GetEnvironmentVariable("STUDYSTASH_DRIVE_SPACE")!;
        int hold = int.Parse(Environment.GetEnvironmentVariable("STUDYSTASH_DRIVE_HOLD_SECONDS")!, CultureInfo.InvariantCulture);
        var report = new StringBuilder();
        void Say(string line)
        {
            lock (report)
            {
                report.AppendLine($"{DateTime.Now:HH:mm:ss.f} {line}");
                if (Environment.GetEnvironmentVariable("STUDYSTASH_DRIVE_CHECK_OUT") is { Length: > 0 } outPath) File.WriteAllText(outPath, report.ToString());
            }
        }

        var google = new GoogleSignIn((string)token["client_secret"]!, clientId: (string)token["client_id"]!);
        var drive = new GoogleDrive(new GoogleTokens(google, (string)token["refresh_token"]!).GetAsync, log: l => Say("  (drive) " + l));
        var names = new DriveNames(prefix);

        using var dir = new TempDir();
        var claude = new ScriptedAi("claude", "Claude")
        {
            Script =
            [
                new AiEvent("text", "{\"answer\": \"Recursion is"), new AiEvent("text", " a function calling itself,"),
                new AiEvent("text", " with a base case"), new AiEvent("text", " that stops it"), new AiEvent("text", " and a smaller problem"),
                new AiEvent("text", " each time.\", \"sources\": []}"),
            ],
            Pause = TimeSpan.FromMilliseconds(400),
        };
        var ai = new AiJobs(dir.Path) { Providers = _ => claude, Checks = new FakeChecks().Installed("claude").Build() };
        await using var rig = await LibraryRig.StartAsync(ai);
        for (int i = 0; i < 12; i++)
            rig.AddLecture(new Meeting($"rec-{i:00}")
            {
                Title = $"Lecture {i}", Date = new DateTime(2026, 10, 1 + i, 9, 0, 0).ToString("yyyy-MM-dd'T'HH:mm:ss'-07:00'", CultureInfo.InvariantCulture),
                Transcript = $"[00:00] Lecture {i} is about recursion and stacks, part {i}.",
            }, i % 2 == 0 ? "CS 101" : "BIO 110", $"## Summary\nLecture {i} covers recursion, part {i}.\n\n## Notes\n- a base case\n- a smaller problem");

        using var stop = new CancellationTokenSource();
        var link = new DriveLink(drive, new LibraryCalls(() => (rig.Url, LibraryRig.Password)), () => ("hold-laptop", "Test MacBook Air", Engine.Version),
            names: names, log: l => Say("(link) " + l));
        var running = link.RunAsync(stop.Token);
        try
        {
            await link.FirstCopy.Task.WaitAsync(TimeSpan.FromMinutes(5), Test);
            Say($"first copy complete: {link.Copy.Count} files; holding {hold} s");
            await Task.Delay(TimeSpan.FromSeconds(hold), Test);
            // What the phone did, as the library now has it.
            using var http = new HttpClient();
            http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", LibraryRig.Password);
            Say("library lectures after: " + await http.GetStringAsync(rig.Url + "/api/v2/lectures?limit=1000", Test));
        }
        finally
        {
            stop.Cancel();
            try { await running; } catch (OperationCanceledException) { }
            foreach (var f in await drive.ListAsync(prefix, CancellationToken.None)) await drive.DeleteAsync(f.Id, CancellationToken.None);
            Say("cleaned up " + prefix);
        }
    }
}
