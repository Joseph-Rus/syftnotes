using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using StudyStash.Core;
using StudyStash.Core.Ai;
using StudyStash.Core.PhoneDrive;
using StudyStash.Core.Tests;

namespace StudyStash.App.Tests;

/// <summary>
/// The phone link against real Google Drive, by hand only: STUDYSTASH_DRIVE_CHECK_TOKEN names a test account's token
/// file (client_id, client_secret, refresh_token; read here, never printed), STUDYSTASH_DRIVE_SPACE a prefix of the
/// check's own ("lanelaptop-" if none), and STUDYSTASH_DRIVE_CHECK_OUT where the timings go. A throwaway library with
/// a scripted Claude; the laptop's link with the contract's own timings; the phone's part played here with its own
/// access token, reading by id as the iPhone app does. It times the copy appearing, a streamed Ask's first line and
/// its end, a renamed lecture reaching the copy, and a file each way; then deletes every file with the prefix.
/// Skipped without the token.
/// </summary>
public class PhoneDriveRealCheck
{
    static CancellationToken Test => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Against_real_google_drive()
    {
        string? tokenPath = Environment.GetEnvironmentVariable("STUDYSTASH_DRIVE_CHECK_TOKEN");
        Assert.SkipUnless(tokenPath is { Length: > 0 } && File.Exists(tokenPath), "set STUDYSTASH_DRIVE_CHECK_TOKEN to a test account's token file");
        var token = JsonNode.Parse(File.ReadAllText(tokenPath!))!.AsObject();
        string prefix = Environment.GetEnvironmentVariable("STUDYSTASH_DRIVE_SPACE") is { Length: > 0 } p ? p : "lanelaptop-";
        var report = new StringBuilder();
        void Say(string line)
        {
            report.AppendLine(line);
            if (Environment.GetEnvironmentVariable("STUDYSTASH_DRIVE_CHECK_OUT") is { Length: > 0 } outPath) File.WriteAllText(outPath, report.ToString());
        }

        // Two sides, each with its own access token, as the laptop and the phone each have theirs.
        GoogleDrive Side(string who)
        {
            var google = new GoogleSignIn((string)token["client_secret"]!, clientId: (string)token["client_id"]!);
            return new GoogleDrive(new GoogleTokens(google, (string)token["refresh_token"]!).GetAsync, log: l => Say($"  ({who}) {l}"));
        }
        var laptopDrive = Side("laptop");
        var phoneDrive = Side("phone");
        var names = new DriveNames(prefix);
        async Task Empty()
        {
            foreach (var f in await phoneDrive.ListAsync(prefix, Test)) await phoneDrive.DeleteAsync(f.Id, Test);
        }

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
        var ai = new AiJobs(dir.Path) { Providers = id => id == "claude" ? claude : new ScriptedAi(id), Checks = new FakeChecks().Installed("claude").Build() };
        await using var rig = await LibraryRig.StartAsync(ai);
        for (int i = 0; i < 12; i++)
            rig.AddLecture(new Meeting($"rec-{i:00}")
            {
                Title = $"Lecture {i}", Date = new DateTime(2026, 10, 1 + i, 9, 0, 0).ToString("yyyy-MM-dd'T'HH:mm:ss'-07:00'", CultureInfo.InvariantCulture),
                Transcript = $"[00:00] Lecture {i} is about recursion and stacks, part {i}.",
            }, i % 2 == 0 ? "CS 101" : "BIO 110", $"## Summary\nLecture {i} covers recursion, part {i}.\n\n## Notes\n- a base case\n- a smaller problem");

        await Empty();
        using var stop = new CancellationTokenSource();
        Task? running = null;
        try
        {
            // The phone signed in first: its box is there, open, when the laptop starts.
            string phoneId = "check-" + Guid.NewGuid().ToString("N")[..8];
            var requests = new JsonArray();
            string? box = null;
            async Task WriteBox()
            {
                var json = new JsonObject
                {
                    ["v"] = 1, ["phone"] = new JsonObject { ["id"] = phoneId, ["name"] = "Check’s iPhone", ["model"] = "iPhone" },
                    ["open_until"] = DateTimeOffset.UtcNow.AddSeconds(90).ToString("o"), ["requests"] = requests.DeepClone(),
                };
                byte[] bytes = Encoding.UTF8.GetBytes(json.ToJsonString());
                if (box is null) box = await phoneDrive.CreateAsync(names.Phones + phoneId + ".json", bytes, ct: Test);
                else await phoneDrive.UpdateAsync(box, bytes, ct: Test);
            }
            await WriteBox();

            var link = new DriveLink(laptopDrive, new LibraryCalls(() => (rig.Url, LibraryRig.Password)), () => ("check-laptop", "Check's MacBook Air", Engine.Version),
                names: names, log: l => Say("  (link) " + l));
            var started = Stopwatch.StartNew();
            running = link.RunAsync(stop.Token);

            // The phone finds laptop.json once by name, then reads it by id.
            string? laptopFile = null;
            while (laptopFile is null)
            {
                laptopFile = (await phoneDrive.ListAsync(names.Laptop, Test)).FirstOrDefault(f => f.Name == names.Laptop)?.Id;
                if (laptopFile is null) await Task.Delay(500, Test);
                Assert.True(started.Elapsed < TimeSpan.FromMinutes(2), "laptop.json never came");
            }
            Say($"laptop.json found by the phone: {started.Elapsed.TotalSeconds:0.0} s after the link started");
            JsonObject? laptop = null;
            while (laptop?["copy"]?["GET /library"] is null)
            {
                laptop = JsonNode.Parse((await phoneDrive.ReadBytesAsync(laptopFile, Test))!)!.AsObject();
                if (laptop["copy"]?["GET /library"] is null) await Task.Delay(500, Test);
            }
            var lib = JsonNode.Parse((await phoneDrive.ReadBytesAsync((string)laptop["copy"]!["GET /library"]!["file"]!, Test))!)!;
            Say($"the copy's GET /library read by the phone: {started.Elapsed.TotalSeconds:0.0} s ({lib["classes"]!.AsArray().Count} classes)");
            await link.FirstCopy.Task.WaitAsync(TimeSpan.FromMinutes(5), Test);
            Say($"the first copy complete (12 lectures, {link.Copy.Count} files): {started.Elapsed.TotalSeconds:0.0} s; uploads {link.Uploads.Values.Sum()}");

            async Task<(string Id, string Answer)> Ask(string method, string path, JsonNode? json = null, JsonObject? upload = null)
            {
                string rid = Guid.NewGuid().ToString();
                string answer = await phoneDrive.CreateAsync(names.Answer(rid), "{}"u8.ToArray(), ct: Test);
                var r = new JsonObject { ["id"] = rid, ["at"] = DateTimeOffset.UtcNow.ToString("o"), ["method"] = method, ["path"] = path, ["json"] = json, ["answer"] = answer };
                if (upload is not null) r["upload"] = upload;
                requests.Add(r);
                return (rid, answer);
            }

            // Ask, three times: the spare answer file is made while typing; sending is one write of the box.
            for (int i = 1; i <= 3; i++)
            {
                var (_, answer) = await Ask("POST", "/ai/ask", new JsonObject { ["question"] = "What's recursion?", ["engine"] = "claude", ["stream"] = true });
                var sent = Stopwatch.StartNew();
                await WriteBox();
                double boxWritten = sent.Elapsed.TotalSeconds;
                double? firstLine = null;
                int lines = 0;
                JsonObject? got = null;
                while (got?["done"]?.GetValue<bool>() != true)
                {
                    await Task.Delay(500, Test);
                    got = JsonNode.Parse((await phoneDrive.ReadBytesAsync(answer, Test))!)!.AsObject();
                    int n = got["lines"]?.AsArray().Count ?? 0;
                    if (n > 0 && firstLine is null) firstLine = sent.Elapsed.TotalSeconds;
                    lines = Math.Max(lines, n);
                    Assert.True(sent.Elapsed < TimeSpan.FromMinutes(2), "the Ask never finished");
                }
                string said = string.Concat(got["lines"]!.AsArray().Select(l => JsonNode.Parse((string)l!)!).Where(l => (string?)l["kind"] == "text").Select(l => (string)l["text"]!));
                Say($"Ask {i}: box written {boxWritten:0.0} s, first line on the phone {firstLine:0.0} s, done {sent.Elapsed.TotalSeconds:0.0} s ({got["lines"]!.AsArray().Count} lines: \"{said}\")");
                Assert.StartsWith("Recursion is a function calling itself", said);
            }

            // A rename on the phone, until the phone's next look at laptop.json shows it in the copy.
            {
                var (_, answer) = await Ask("POST", "/lectures/rec-03/title", new JsonObject { ["title"] = "Recursion, renamed on the phone" });
                var sent = Stopwatch.StartNew();
                await WriteBox();
                while (JsonNode.Parse((await phoneDrive.ReadBytesAsync(answer, Test))!)?["done"]?.GetValue<bool>() != true) await Task.Delay(500, Test);
                double answered = sent.Elapsed.TotalSeconds;
                while (true)
                {
                    var index = JsonNode.Parse((await phoneDrive.ReadBytesAsync(laptopFile, Test))!)!["copy"]![DriveLink.LectureKey("rec-03")]!;
                    var body = await phoneDrive.ReadBytesAsync((string)index["file"]!, Test);
                    if (body is not null && DriveNames.Sha(body) == (string)index["sha"]! && Encoding.UTF8.GetString(body).Contains("renamed on the phone")) break;
                    await Task.Delay(500, Test);
                    Assert.True(sent.Elapsed < TimeSpan.FromMinutes(2), "the rename never reached the copy");
                }
                Say($"rename: answered {answered:0.0} s, in the copy (laptop.json agreeing) {sent.Elapsed.TotalSeconds:0.0} s");
            }

            // A file each way, big enough (6 MB) to go up in a resumable upload both times.
            {
                byte[] pdf = new byte[6 * 1024 * 1024];
                RandomNumberGenerator.Fill(pdf);
                "%PDF-1.4\n"u8.CopyTo(pdf);
                var sent = Stopwatch.StartNew();
                string rid = Guid.NewGuid().ToString();
                string upload = await phoneDrive.CreateAsync(names.Uploads + rid, pdf, "application/pdf", Test);
                var (_, answer) = await Ask("POST", "/attachments", null, new JsonObject
                {
                    ["file"] = upload, ["name"] = "Handout.pdf", ["type"] = "application/pdf", ["fields"] = new JsonObject { ["class"] = "CS 101" },
                });
                await WriteBox();
                JsonObject? got = null;
                while (got?["done"]?.GetValue<bool>() != true)
                {
                    await Task.Delay(500, Test);
                    got = JsonNode.Parse((await phoneDrive.ReadBytesAsync(answer, Test))!)!.AsObject();
                }
                Assert.Equal(200, (int)got["status"]!);
                string attachment = (string)got["body"]!["attachments"]![0]!["id"]!;
                Say($"6 MB file phone → laptop: {sent.Elapsed.TotalSeconds:0.0} s (status {got["status"]})");
                sent.Restart();
                var (_, rawAnswer) = await Ask("GET", $"/attachments/{Uri.EscapeDataString(attachment)}/raw");
                await WriteBox();
                got = null;
                while (got?["done"]?.GetValue<bool>() != true)
                {
                    await Task.Delay(500, Test);
                    got = JsonNode.Parse((await phoneDrive.ReadBytesAsync(rawAnswer, Test))!)!.AsObject();
                }
                byte[] back = (await phoneDrive.ReadBytesAsync((string)got["blob"]!, Test))!;
                Assert.Equal(DriveNames.Sha(pdf), DriveNames.Sha(back));
                Say($"6 MB file laptop → phone (blob, read back the same): {sent.Elapsed.TotalSeconds:0.0} s");
                Assert.Null((await phoneDrive.ListAsync(names.Uploads, Test)).FirstOrDefault(f => f.Id == upload)); // used, so deleted
            }
            Say($"phones the laptop lists: {string.Join(", ", link.Phones.Select(ph => ph.Name))}");
            // Drive's "name contains" matches the start of a name: a kind's prefix finds exactly that kind.
            var boxes = await laptopDrive.ListAsync(names.Phones, Test);
            var copies = await laptopDrive.ListAsync(names.Copies, Test);
            Say($"listing by prefix: {boxes.Count} box(es) under {names.Phones}, {copies.Count} copies under {names.Copies} (the index has {link.Copy.Count})");
            Assert.Equal(box, Assert.Single(boxes).Id);
            Assert.Equal(link.Copy.Count, copies.Count);
        }
        finally
        {
            stop.Cancel();
            if (running is not null) await running;
            await Empty();
            Say($"left with the prefix after: {(await phoneDrive.ListAsync(prefix, Test)).Count} files");
        }
    }
}
