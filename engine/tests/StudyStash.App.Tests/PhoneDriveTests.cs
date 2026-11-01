using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using StudyStash.App.Services;
using StudyStash.Core;
using StudyStash.Core.Ai;
using StudyStash.Core.Canvas;
using StudyStash.Core.PhoneDrive;
using StudyStash.Core.Tests;

namespace StudyStash.App.Tests;

/// <summary>
/// The phone over Google Drive, the laptop's side (docs/phone-google.md), against a real library (<see cref="LibraryRig"/>)
/// and a Drive folder kept in memory, with the phone's part played as the iPhone app plays it: what could really go
/// wrong for a student. A phone can do nothing outside the iPhone app's own calls; a request is done once; a recording
/// sent twice is one lecture; a changed lecture reaches the copy and an unchanged one isn't uploaded again; a streamed
/// Ask is written a piece at a time and ends done; Disconnect stops everything.
/// </summary>
public class PhoneDriveTests
{
    static readonly DriveTimes Quick = new()
    {
        OpenBox = TimeSpan.FromMilliseconds(100), ClosedBox = TimeSpan.FromMilliseconds(200), NewBoxes = TimeSpan.FromMilliseconds(200), NewBoxesSoon = TimeSpan.FromMilliseconds(200),
        Copy = TimeSpan.FromMilliseconds(300), AfterChange = TimeSpan.FromMilliseconds(50), Seen = TimeSpan.FromSeconds(2),
        Tick = TimeSpan.FromMilliseconds(25),
    };

    static CancellationToken Test => TestContext.Current.CancellationToken;

    static DriveLink Link(FakeDrive drive, LibraryRig rig, string laptopId = "laptop-1") =>
        new(drive, new LibraryCalls(() => (rig.Url, LibraryRig.Password)), () => (laptopId, "Joey's MacBook Air", "0.17.3"))
        {
            Times = Quick,
        };

    static async Task Until(Func<bool> done, string what, double seconds = 20)
    {
        var until = DateTime.UtcNow.AddSeconds(seconds);
        while (!done())
        {
            if (DateTime.UtcNow > until) Assert.Fail($"waited {seconds} s for {what}");
            await Task.Delay(25, Test);
        }
    }

    static Meeting Lecture(string id, string title, string date) => new(id)
    {
        Title = title, Date = date, Transcript = "[00:00] Today we talk about " + title + ".",
    };

    [Fact]
    public async Task A_phone_can_do_only_the_iphone_apps_own_calls()
    {
        await using var rig = await LibraryRig.StartAsync();
        var drive = new FakeDrive();
        var link = Link(drive, rig);
        using var stop = new CancellationTokenSource();
        var running = link.RunAsync(stop.Token);
        var phone = new FakePhone(drive);

        var refused = new[]
        {
            phone.Ask("GET", "/settings"),
            phone.Ask("POST", "/ai", new JsonObject { ["provider"] = "ollama" }),
            phone.Ask("POST", "/settings/password", new JsonObject { ["password"] = "taken over" }),
            phone.Ask("GET", "/devices"),
            phone.Ask("POST", "/devices/code", new JsonObject()),
            phone.Ask("POST", "/ai/engines/claude/key", new JsonObject { ["key"] = "sk-x" }),
            phone.Ask("GET", "/lectures/rec-1/../../settings"),
            phone.Ask("GET", "/lectures/%2e%2e/settings"),
            phone.Ask("GET", "/lectures/a%2F..%2F..%2Fsettings"),
            phone.Ask("GET", "/files/raw?path=/etc/passwd"),
            phone.Ask("PUT", "/lectures/rec-1/title", new JsonObject { ["title"] = "x" }),
            phone.Ask("GET", "//library"),
            phone.Ask("GET", "/library#settings"),
            phone.Ask("GET", "http://127.0.0.1/api/v2/settings"),
        };
        string allowed = phone.Ask("GET", "/library");
        string engine = phone.Ask("GET", "/ai");
        // A chat renamed, or brought back after Delete's undo, from the phone.
        Assert.NotNull(PhoneCalls.Check("POST", "/chats/c-1"));
        Assert.NotNull(PhoneCalls.Check("POST", "/chats/c-1/restore"));

        foreach (string r in refused)
        {
            var answer = await phone.DoneAsync(r);
            Assert.Equal(403, (int)answer["status"]!);
            Assert.Equal(PhoneCalls.Refused, (string)answer["body"]!["detail"]!);
        }
        var library = await phone.DoneAsync(allowed);
        Assert.Equal(200, (int)library["status"]!);
        Assert.NotNull(library["body"]!["classes"]);
        // Which AI answers Ask can be read (the phone shows its mark), but never changed from the phone.
        var ai = await phone.DoneAsync(engine);
        Assert.Equal(200, (int)ai["status"]!);
        Assert.NotNull(ai["body"]!["jobs"]!["ask"]!["provider"]);
        // Nothing reached the library's settings: its password is the one it had.
        Assert.NotNull(await rig.RawAsync("/api/v2/settings"));
        stop.Cancel();
        await running;
    }

    [Fact]
    public async Task The_due_tab_sees_canvas_and_asks_it_to_sync_but_cant_change_where_it_is()
    {
        await using var rig = await LibraryRig.StartAsync();
        CanvasSettings.Update(rig.Home, s => s.Url = "https://school.example.edu");
        var drive = new FakeDrive();
        var link = Link(drive, rig);
        using var stop = new CancellationTokenSource();
        var running = link.RunAsync(stop.Token);
        var phone = new FakePhone(drive);

        var state = await phone.DoneAsync(phone.Ask("GET", "/canvas/state"));
        Assert.Equal(200, (int)state["status"]!);
        var sync = await phone.DoneAsync(phone.Ask("POST", "/canvas", new JsonObject
        {
            ["sync"] = true, ["url"] = "https://elsewhere.example.com", ["courses"] = new JsonObject { ["CS 101"] = 999 },
        }));
        Assert.NotEqual(403, (int)sync["status"]!);
        // Only "sync now" reached the library: Canvas is where it was, and no course was linked.
        var after = CanvasSettings.Load(rig.Home);
        Assert.Equal("https://school.example.edu", after.Url);
        Assert.Empty(after.Courses);
        stop.Cancel();
        await running;
    }

    [Fact]
    public async Task A_request_is_done_once_however_often_the_box_is_read_and_after_a_restart()
    {
        await using var rig = await LibraryRig.StartAsync();
        var drive = new FakeDrive();
        var phone = new FakePhone(drive);
        string memo = phone.Ask("POST", "/voice-memos", upload: phone.Upload("Lecture 4.m4a", "audio/mp4", new byte[4096], new JsonObject { ["class"] = "CS 101" }));
        phone.Repeat(memo); // the same request twice in the box (a phone that sent it again before seeing the answer)

        using (var stop = new CancellationTokenSource())
        {
            var link = Link(drive, rig);
            var running = link.RunAsync(stop.Token);
            Assert.Equal(200, (int)(await phone.DoneAsync(memo))["status"]!);
            // The phone hasn't taken it out of its box yet: read again and again, it isn't done again.
            await Task.Delay(800, Test);
            await Until(() => drive.Json(drive.IdOf(new DriveNames().Laptop)!)?["answered"]?.AsArray().Any(a => (string?)a == memo) == true, "laptop.json to say it's answered");
            stop.Cancel();
            await running;
        }
        using (var stop = new CancellationTokenSource())
        {
            // Syft Notes quits and starts again: laptop.json says what it answered.
            var again = Link(drive, rig);
            var running = again.RunAsync(stop.Token);
            await again.FirstCopy.Task.WaitAsync(TimeSpan.FromSeconds(20), Test);
            await Task.Delay(800, Test);
            stop.Cancel();
            await running;
        }

        Assert.Equal(1, drive.Updates(new DriveNames().Answer(memo)));
        var memos = (await rig.RawAsync("/api/v2/voice-memos"))["memos"]!.AsArray();
        Assert.Single(memos);
        Assert.Equal("Joey's iPhone", (string)memos[0]!["by"]!); // the phone's name, as a header can carry it
        Assert.Null(drive.IdOf(new DriveNames().Uploads + memo)); // used, so gone
    }

    [Fact]
    public async Task A_recording_sent_twice_makes_one_lecture_and_its_the_phones()
    {
        await using var rig = await LibraryRig.StartAsync();
        var drive = new FakeDrive();
        using var stop = new CancellationTokenSource();
        var running = Link(drive, rig).RunAsync(stop.Token);
        var phone = new FakePhone(drive);
        var recording = new JsonObject
        {
            ["id"] = "phone-20261024-091500-a1b2c3", ["date"] = "2026-10-24T09:15:00-07:00", ["class"] = "CS 101", ["title_typed"] = false,
            ["transcript"] = "[00:00] Today: recursion.\n[00:30] A base case and a smaller problem.", ["seconds"] = 61.0, ["language"] = "en",
        };

        string first = phone.Ask("POST", "/lectures", recording);
        Assert.Equal(200, (int)(await phone.DoneAsync(first))["status"]!);
        // The phone didn't hear back in time and sent it again, as a new request.
        string second = phone.Ask("POST", "/lectures", recording.DeepClone());
        Assert.Equal(200, (int)(await phone.DoneAsync(second))["status"]!);

        // One lecture, with the phone's name on it (the library keeps a lecture by its id: the second send is the same one).
        var ours = await rig.RawAsync("/api/v2/lectures/phone-20261024-091500-a1b2c3");
        Assert.Equal("Joey’s iPhone", (string)ours["owner"]!);
        Assert.Equal("[00:00] Today: recursion.\n[00:30] A base case and a smaller problem.", (string)ours["transcript"]!);
        stop.Cancel();
        await running;
    }

    [Fact]
    public async Task A_phones_changes_are_done_in_the_order_it_asked_them()
    {
        await using var rig = await LibraryRig.StartAsync();
        var drive = new FakeDrive();
        using var stop = new CancellationTokenSource();
        var running = Link(drive, rig).RunAsync(stop.Token);
        var phone = new FakePhone(drive);

        // A recording and a notebook page attached to it, in one write of the box: the page needs the lecture there.
        string recording = phone.Ask("POST", "/lectures", new JsonObject
        {
            ["id"] = "phone-20261024-101500-b2c3d4", ["date"] = "2026-10-24T10:15:00-07:00", ["class"] = "CS 101",
            ["transcript"] = "[00:00] Stacks and queues.", ["seconds"] = 30.0, ["language"] = "en",
        }, send: false);
        string page = phone.Ask("POST", "/attachments", upload: phone.Upload("Page 1.pdf", "application/pdf", "%PDF-1.4\n%%EOF"u8.ToArray(),
            new JsonObject { ["lecture"] = "phone-20261024-101500-b2c3d4" }), send: false);
        phone.Send();

        Assert.Equal(200, (int)(await phone.DoneAsync(recording))["status"]!);
        var attached = await phone.DoneAsync(page);
        Assert.Equal(200, (int)attached["status"]!);
        Assert.Equal("phone-20261024-101500-b2c3d4", (string)attached["body"]!["attachments"]![0]!["lecture"]!);
        stop.Cancel();
        await running;
    }

    [Fact]
    public async Task A_changed_lecture_reaches_the_copy_and_an_unchanged_one_isnt_uploaded_again()
    {
        await using var rig = await LibraryRig.StartAsync();
        rig.AddLecture(Lecture("rec-a", "Limits", "2026-10-20T09:00:00-07:00"), "CS 101", "## Summary\nLimits.");
        rig.AddLecture(Lecture("rec-b", "Cells", "2026-10-21T09:00:00-07:00"), "BIO 110", "## Summary\nCells.");
        rig.AddLecture(Lecture("rec-c", "Recursion", "2026-10-22T09:00:00-07:00"), "CS 101", "## Summary\nRecursion.");
        var drive = new FakeDrive();
        var link = Link(drive, rig);
        using var stop = new CancellationTokenSource();
        var running = link.RunAsync(stop.Token);
        await link.FirstCopy.Task.WaitAsync(TimeSpan.FromSeconds(20), Test);

        // What the phone reads: laptop.json's index, then each file by its id.
        var names = new DriveNames();
        // A copy file is rewritten first and laptop.json a moment after: the phone reads one once the other agrees.
        async Task<JsonObject> Copied(string key)
        {
            byte[]? body = null;
            await Until(() => drive.Json(drive.IdOf(names.Laptop)!)!["copy"]![key] is { } index
                && (body = drive.Bytes((string)index["file"]!)) is not null && DriveNames.Sha(body) == (string)index["sha"]!, $"laptop.json to agree with {key}");
            return JsonNode.Parse(body!)!.AsObject();
        }
        await Until(() => drive.Json(drive.IdOf(names.Laptop)!)?["copy"]?[DriveLink.RenderedKey("rec-c")] is not null, "the copy in laptop.json");
        Assert.Equal("Limits", (string)(await Copied(DriveLink.LectureKey("rec-a")))["title"]!);
        Assert.Equal(["rec-c", "rec-b", "rec-a"], JsonNode.Parse(drive.Bytes((string)drive.Json(drive.IdOf(names.Laptop)!)!["copy"]![DriveLink.LecturesKey]!["file"]!)!)!
            .AsArray().Select(l => (string)l!["id"]!));

        var phone = new FakePhone(drive);
        string rename = phone.Ask("POST", "/lectures/rec-a/title", new JsonObject { ["title"] = "Limits and continuity" });
        Assert.Equal(200, (int)(await phone.DoneAsync(rename))["status"]!);
        await Until(() => drive.Json(drive.IdOf(names.Laptop)!) is { } l && l["copy"]?[DriveLink.LectureKey("rec-a")]?["file"] is { } f
            && drive.Json((string)f!)?["title"]?.ToString() == "Limits and continuity", "the renamed lecture in the copy");
        Assert.Equal("Limits and continuity", (string)(await Copied(DriveLink.LectureKey("rec-a")))["title"]!);
        Assert.Contains("Limits and continuity", Encoding.UTF8.GetString(drive.Bytes((string)drive.Json(drive.IdOf(names.Laptop)!)!["copy"]![DriveLink.LecturesKey]!["file"]!)!));

        // A few more rounds (each looks at every lecture in turn): the others never go up again.
        await Task.Delay(Quick.Copy * 6, Test);
        foreach (string id in new[] { "rec-b", "rec-c" })
        {
            Assert.Equal(1, link.Uploads[names.Copy(DriveLink.LectureKey(id))]);
            Assert.Equal(1, link.Uploads[names.Copy(DriveLink.RenderedKey(id))]);
        }
        Assert.Equal(2, link.Uploads[names.Copy(DriveLink.LectureKey("rec-a"))]);
        stop.Cancel();
        await running;

        // A lecture deleted in the library leaves the folder.
        var again = Link(drive, rig);
        using var stop2 = new CancellationTokenSource();
        var running2 = again.RunAsync(stop2.Token);
        await again.FirstCopy.Task.WaitAsync(TimeSpan.FromSeconds(20), Test);
        Assert.DoesNotContain(again.Uploads, u => u.Key != names.Copy("GET /library")); // a restart uploads nothing that didn't change
        var gone = phone.Ask("DELETE", "/lectures/rec-b");
        Assert.Equal(200, (int)(await phone.DoneAsync(gone))["status"]!);
        await Until(() => !again.Copy.ContainsKey(DriveLink.LectureKey("rec-b")) && drive.IdOf(names.Copy(DriveLink.LectureKey("rec-b"))) is null, "the deleted lecture's copy to go");
        stop2.Cancel();
        await running2;
    }

    [Fact]
    public async Task A_streamed_ask_is_written_a_piece_at_a_time_and_ends_done()
    {
        using var dir = new TempDir();
        var claude = new ScriptedAi("claude", "Claude")
        {
            Script =
            [
                new AiEvent("text", "{\"answer\": \"Recursion is"), new AiEvent("text", " a function calling itself,"),
                new AiEvent("text", " with a base case"), new AiEvent("text", " to stop.\", \"sources\": []}"),
            ],
            Pause = TimeSpan.FromMilliseconds(700),
        };
        var ai = new AiJobs(dir.Path) { Providers = id => id == "claude" ? claude : new ScriptedAi(id), Checks = new FakeChecks().Installed("claude").Build() };
        await using var rig = await LibraryRig.StartAsync(ai);
        var drive = new FakeDrive();
        using var stop = new CancellationTokenSource();
        var running = Link(drive, rig).RunAsync(stop.Token);
        var phone = new FakePhone(drive);

        string ask = phone.Ask("POST", "/ai/ask", new JsonObject
        {
            ["question"] = "What's recursion?", ["engine"] = "claude", ["stream"] = true,
            ["live"] = "Recursion is a function calling itself, with a base case to stop.",
        });
        var done = await phone.DoneAsync(ask, 30);

        var lines = done["lines"]!.AsArray().Select(l => JsonNode.Parse((string)l!)!.AsObject()).ToList();
        Assert.Equal(200, (int)done["status"]!);
        Assert.Contains(lines, l => (string?)l["kind"] == "text");
        string said = string.Concat(lines.Where(l => (string?)l["kind"] == "text").Select(l => (string)l["text"]!));
        Assert.StartsWith("Recursion is a function calling itself", said);
        // Written as it came: in pieces that grew, not once at the end, and never more than once a second.
        var writes = drive.History(new DriveNames().Answer(ask)).Select(w => JsonNode.Parse(w)!.AsObject()).ToList();
        var partial = writes.Where(w => w["done"]?.GetValue<bool>() == false).ToList();
        Assert.True(partial.Count >= 2, $"{partial.Count} partial writes");
        Assert.True(partial.Zip(partial.Skip(1)).All(p => p.Second["lines"]!.AsArray().Count > p.First["lines"]!.AsArray().Count));
        var times = drive.Times(new DriveNames().Answer(ask));
        Assert.True(times.Zip(times.Skip(1)).Take(partial.Count - 1).All(t => t.Second - t.First >= TimeSpan.FromMilliseconds(950)),
            string.Join(", ", times.Zip(times.Skip(1)).Select(t => (t.Second - t.First).TotalMilliseconds)));
        Assert.True(writes[^1]["done"]!.GetValue<bool>());
        stop.Cancel();
        await running;
    }

    [Fact]
    public async Task Stop_on_the_phone_stops_the_answer_on_the_laptop()
    {
        using var dir = new TempDir();
        var claude = new ScriptedAi("claude", "Claude")
        {
            Script =
            [
                new AiEvent("text", "{\"answer\": \"Recursion is"), .. Enumerable.Range(0, 14).Select(_ => new AiEvent("text", " a function calling itself,")),
                new AiEvent("text", " to stop.\", \"sources\": []}"),
            ],
            Pause = TimeSpan.FromMilliseconds(700),
        };
        var ai = new AiJobs(dir.Path) { Providers = id => id == "claude" ? claude : new ScriptedAi(id), Checks = new FakeChecks().Installed("claude").Build() };
        await using var rig = await LibraryRig.StartAsync(ai);
        var drive = new FakeDrive();
        var said = new List<string>();
        using var stop = new CancellationTokenSource();
        var link = new DriveLink(drive, new LibraryCalls(() => (rig.Url, LibraryRig.Password)), () => ("laptop-1", "Joey's MacBook Air", "0.18.1"),
            log: l => { lock (said) said.Add(l); }) { Times = Quick };
        var running = link.RunAsync(stop.Token);
        var phone = new FakePhone(drive);

        string ask = phone.Ask("POST", "/ai/ask", new JsonObject
        {
            ["question"] = "What's recursion?", ["engine"] = "claude", ["stream"] = true,
            ["live"] = "Recursion is a function calling itself, with a base case to stop.",
        });
        string answer = new DriveNames().Answer(ask);
        await Until(() => drive.History(answer).Count >= 2, "the answer started");
        phone.Stop(ask);
        await Until(() => { lock (said) return said.Any(l => l.Contains("stopped on the phone")); }, "the laptop stopped");
        int writes = drive.History(answer).Count;
        await Task.Delay(1500, TestContext.Current.CancellationToken);
        // Nothing more is written once stopped, and it never ends as a whole answer.
        Assert.Equal(writes, drive.History(answer).Count);
        Assert.NotEqual(true, JsonNode.Parse(drive.History(answer)[^1])!["done"]?.GetValue<bool>());
        stop.Cancel();
        await running;
    }

    [Fact]
    public async Task Disconnect_stops_everything_empties_the_folder_and_forgets_the_account()
    {
        await using var rig = await LibraryRig.StartAsync();
        rig.AddLecture(Lecture("rec-a", "Limits", "2026-10-20T09:00:00-07:00"), "CS 101", "## Summary\nLimits.");
        using var home = new TempDir();
        new GoogleAccount("joey@example.com", "refresh-1", DateTimeOffset.UtcNow).Save(home.Path);
        var google = new PretendGoogle();
        var drive = new FakeDrive();
        // The link as the library runs it.
        using var phoneDrive = new PhoneLink(home.Path, () => (rig.Url, LibraryRig.Password), () => true, secret: () => "test-secret",
            drive: _ => drive, google: new HttpClient(google), space: "")
        {
            Times = Quick,
        };
        phoneDrive.Start();
        await phoneDrive.Link!.FirstCopy.Task.WaitAsync(TimeSpan.FromSeconds(20), Test);
        var phone = new FakePhone(drive);
        Assert.Equal(200, (int)(await phone.DoneAsync(phone.Ask("GET", "/library")))["status"]!);
        await Until(() => phoneDrive.Phones.Count == 1, "the phone to be listed");
        Assert.Equal("Joey’s iPhone", phoneDrive.Phones[0].Name);

        await phoneDrive.DisconnectAsync();

        Assert.Equal(["refresh-1"], google.Revoked);
        Assert.False(File.Exists(GoogleAccount.PathIn(home.Path)));
        Assert.Null(phoneDrive.Account);
        Assert.Null(phoneDrive.Link);
        var names = new DriveNames();
        Assert.DoesNotContain(drive.Names(), n => n == names.Laptop || n.StartsWith(names.Copies) || n.StartsWith(names.Answers) || n.StartsWith(names.Blobs));
        Assert.Contains(drive.Names(), n => n.StartsWith(names.Phones)); // the phone's box is the phone's
        // Nothing answers any more, and nothing is written.
        int writes = drive.WriteCount;
        string late = phone.Ask("GET", "/library");
        await Task.Delay(1000, Test);
        Assert.Equal("{}", Encoding.UTF8.GetString(drive.Bytes(drive.IdOf(names.Answer(late))!)!));
        Assert.Equal(writes, drive.WriteCount - 2); // only the phone's own two (its answer file and its box)
    }

    [Fact]
    public async Task Connect_keeps_the_account_for_the_owner_only_and_says_why_google_said_no()
    {
        var google = new PretendGoogle();
        var drive = new FakeDrive();
        // The app signs in; the library runs the link.
        await using var rig = await LibraryRig.StartAsync(phone: r => new PhoneLink(r.Home, () => (r.Url, LibraryRig.Password), () => true,
            secret: () => "test-secret", drive: _ => drive, google: new HttpClient(google), space: "")
        {
            Times = Quick,
        });
        var home = new { Path = rig.Home };
        using var phoneDrive = new PhoneDrive(rig.Home, () => (rig.Url, LibraryRig.Password), secret: () => "test-secret",
            google: new HttpClient(google), space: "");
        using var browser = new HttpClient();
        string? page = null;
        // The browser: Google's page, then back to the loopback address with what Google says.
        Action<string> Browser(string answer) => url =>
        {
            page = url;
            var q = Py.ParseQs(new Uri(url).Query.TrimStart('?'));
            _ = browser.GetAsync($"{q["redirect_uri"]}/?{answer}&state={Uri.EscapeDataString(q["state"])}", Test);
        };

        await phoneDrive.ConnectAsync(Browser("code=one-time-code"));

        var asked = Py.ParseQs(new Uri(page!).Query.TrimStart('?'));
        Assert.Equal((GoogleClient.DesktopClientId, GoogleClient.Scopes, "S256", "offline"),
            (asked["client_id"], asked["scope"], asked["code_challenge_method"], asked["access_type"]));
        Assert.StartsWith("http://127.0.0.1:", asked["redirect_uri"]);
        Assert.Equal(asked["redirect_uri"], google.Exchanged!["redirect_uri"]);
        Assert.Equal(("one-time-code", "test-secret"), (google.Exchanged["code"], google.Exchanged["client_secret"]));
        Assert.Equal("sam@example.com", phoneDrive.Account!.Email);
        Assert.Equal("refresh-from-google", GoogleAccount.Load(home.Path)!.RefreshToken);
        if (!OperatingSystem.IsWindows())
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(GoogleAccount.PathIn(home.Path)));
        // The library started the link: laptop.json is in the folder for the phone to find.
        await Until(() => drive.IdOf(new DriveNames().Laptop) is not null, "laptop.json");

        // A school account turned away, and Google Drive unticked: each in words, and nothing kept.
        await phoneDrive.DisconnectAsync();
        var school = await Assert.ThrowsAsync<GoogleException>(() => phoneDrive.ConnectAsync(Browser("error=admin_policy_enforced")));
        Assert.Equal("Your school's Google account doesn't allow Syft Notes. Try a personal Gmail on both.", school.Message);
        google.Scope = "openid email";
        var noDrive = await Assert.ThrowsAsync<GoogleException>(() => phoneDrive.ConnectAsync(Browser("code=another")));
        Assert.Equal(GoogleTrouble.NoDrive, noDrive.Trouble);
        Assert.Null(phoneDrive.Account);
        Assert.False(File.Exists(GoogleAccount.PathIn(home.Path)));
    }

    /// <summary>Google's token and revoke endpoints, pretending: a code becomes a refresh token and an id_token for
    /// sam@example.com, with <see cref="Scope"/>; each revoked token is noted.</summary>
    sealed class PretendGoogle : HttpMessageHandler
    {
        public List<string> Revoked { get; } = [];
        public Dictionary<string, string>? Exchanged { get; private set; }
        public string Scope { get; set; } = GoogleClient.Scopes;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var form = Py.ParseQs(await request.Content!.ReadAsStringAsync(ct));
            if (request.RequestUri!.AbsoluteUri.StartsWith(GoogleClient.RevokeUrl, StringComparison.Ordinal))
            {
                Revoked.Add(form["token"]);
                return new HttpResponseMessage(HttpStatusCode.OK);
            }
            if (request.RequestUri.AbsoluteUri == GoogleClient.TokenUrl && form.GetValueOrDefault("grant_type") == "authorization_code")
            {
                Exchanged = form;
                string claims = Convert.ToBase64String(Encoding.UTF8.GetBytes("{\"email\":\"sam@example.com\",\"email_verified\":true}")).TrimEnd('=').Replace('+', '-').Replace('/', '_');
                var body = new JsonObject
                {
                    ["access_token"] = "access", ["expires_in"] = 3599, ["refresh_token"] = "refresh-from-google", ["scope"] = Scope,
                    ["id_token"] = $"eyJhbGciOiJSUzI1NiJ9.{claims}.signature", ["token_type"] = "Bearer",
                };
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json") };
            }
            return new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = new StringContent("{\"error\":\"invalid_request\"}") };
        }
    }
}

/// <summary>A Google Drive appDataFolder kept in memory: every write's content and time kept, for tests to look at.</summary>
public sealed class FakeDrive : IDrive
{
    readonly Lock gate = new();
    readonly Dictionary<string, (string Name, byte[] Data, DateTimeOffset Modified)> files = [];
    readonly Dictionary<string, List<(DateTimeOffset At, string Text)>> history = [];
    int next;

    /// <summary>Every create and update, by anyone.</summary>
    public int WriteCount { get; private set; }

    public Task<List<DriveFile>> ListAsync(string prefix, CancellationToken ct = default)
    {
        lock (gate)
            return Task.FromResult(files.Where(f => f.Value.Name.StartsWith(prefix, StringComparison.Ordinal))
                .Select(f => new DriveFile(f.Key, f.Value.Name, f.Value.Modified, f.Value.Data.Length)).ToList());
    }

    public async Task<string> CreateAsync(string name, Stream content, string type, CancellationToken ct = default)
    {
        byte[] data = await Read(content, ct);
        lock (gate)
        {
            string id = "f" + ++next;
            files[id] = (name, data, DateTimeOffset.UtcNow);
            WriteCount++;
            return id;
        }
    }

    public async Task<bool> UpdateAsync(string id, Stream content, string type, CancellationToken ct = default)
    {
        byte[] data = await Read(content, ct);
        lock (gate)
        {
            if (!files.TryGetValue(id, out var f)) return false;
            files[id] = (f.Name, data, DateTimeOffset.UtcNow);
            WriteCount++;
            if (!history.TryGetValue(f.Name, out var h)) history[f.Name] = h = [];
            h.Add((DateTimeOffset.UtcNow, Encoding.UTF8.GetString(data)));
            return true;
        }
    }

    public async Task<bool> ReadAsync(string id, Stream into, CancellationToken ct = default)
    {
        byte[]? data;
        lock (gate) data = files.TryGetValue(id, out var f) ? f.Data : null;
        if (data is null) return false;
        await into.WriteAsync(data, ct);
        return true;
    }

    public Task DeleteAsync(string id, CancellationToken ct = default)
    {
        lock (gate) files.Remove(id);
        return Task.CompletedTask;
    }

    static async Task<byte[]> Read(Stream s, CancellationToken ct)
    {
        using var m = new MemoryStream();
        await s.CopyToAsync(m, ct);
        return m.ToArray();
    }

    public string? IdOf(string name)
    {
        lock (gate) return files.FirstOrDefault(f => f.Value.Name == name).Key;
    }

    public byte[]? Bytes(string id)
    {
        lock (gate) return files.TryGetValue(id, out var f) ? f.Data : null;
    }

    public JsonObject? Json(string id) => Bytes(id) is { } b ? JsonNode.Parse(b) as JsonObject : null;

    public List<string> Names()
    {
        lock (gate) return [.. files.Values.Select(f => f.Name)];
    }

    /// <summary>How many times a file was rewritten after it was made.</summary>
    public int Updates(string name)
    {
        lock (gate) return history.TryGetValue(name, out var h) ? h.Count : 0;
    }

    public List<string> History(string name)
    {
        lock (gate) return history.TryGetValue(name, out var h) ? [.. h.Select(x => x.Text)] : [];
    }

    public List<DateTimeOffset> Times(string name)
    {
        lock (gate) return history.TryGetValue(name, out var h) ? [.. h.Select(x => x.At)] : [];
    }

    public string Create(string name, byte[] data)
    {
        lock (gate)
        {
            string id = "f" + ++next;
            files[id] = (name, data, DateTimeOffset.UtcNow);
            WriteCount++;
            return id;
        }
    }

    public void Set(string id, byte[] data)
    {
        lock (gate)
        {
            files[id] = (files[id].Name, data, DateTimeOffset.UtcNow);
            WriteCount++;
        }
    }
}

/// <summary>The phone's part, as the iPhone app plays it (docs/phone-google.md): its box, open; an answer file made
/// before each request is added; uploads as their own files.</summary>
public sealed class FakePhone(FakeDrive drive, string id = "7f3e2a10-phone", string name = "Joey’s iPhone", string prefix = "")
{
    readonly DriveNames names = new(prefix);
    readonly JsonArray requests = [];
    string? box;

    /// <summary>Asks: the answer file first, then the request in the box. The request's id.</summary>
    public string Ask(string method, string path, JsonNode? json = null, JsonObject? upload = null, bool send = true)
    {
        string rid = Guid.NewGuid().ToString();
        string answer = drive.Create(names.Answer(rid), "{}"u8.ToArray());
        var r = new JsonObject
        {
            ["id"] = rid, ["at"] = DateTimeOffset.UtcNow.ToString("o"), ["method"] = method, ["path"] = path, ["json"] = json, ["answer"] = answer,
        };
        if (upload is not null)
        {
            r["upload"] = upload;
            upload["file"] = drive.Create(names.Uploads + rid, Pending.Data);
        }
        requests.Add(r);
        if (send) Write();
        return rid;
    }

    /// <summary>The box written with everything asked so far (several requests in one write).</summary>
    public void Send() => Write();

    /// <summary>Stop: the request taken out of the box, as the app does when the student presses Stop.</summary>
    public void Stop(string rid)
    {
        requests.Remove(requests.First(r => (string?)r!["id"] == rid));
        Write();
    }

    (byte[] Data, string Name) Pending = ([], "");

    /// <summary>A file to send with the next <see cref="Ask"/>.</summary>
    public JsonObject Upload(string fileName, string type, byte[] data, JsonObject? fields = null)
    {
        Pending = (data, fileName);
        return new JsonObject { ["name"] = fileName, ["type"] = type, ["fields"] = fields ?? new JsonObject() };
    }

    /// <summary>The same request put in the box a second time.</summary>
    public void Repeat(string rid)
    {
        requests.Add(requests.First(r => (string?)r!["id"] == rid)!.DeepClone());
        Write();
    }

    void Write()
    {
        var json = new JsonObject
        {
            ["v"] = 1, ["phone"] = new JsonObject { ["id"] = id, ["name"] = name, ["model"] = "iPhone" },
            ["open_until"] = DateTimeOffset.UtcNow.AddSeconds(90).ToString("o"), ["requests"] = requests.DeepClone(),
        };
        byte[] bytes = Encoding.UTF8.GetBytes(json.ToJsonString());
        if (box is null) box = drive.Create(names.Phones + id + ".json", bytes);
        else drive.Set(box, bytes);
    }

    public async Task<JsonObject> DoneAsync(string rid, double seconds = 20)
    {
        string answer = (string)requests.First(r => (string?)r!["id"] == rid)!["answer"]!;
        var until = DateTime.UtcNow.AddSeconds(seconds);
        while (DateTime.UtcNow < until)
        {
            if (drive.Json(answer) is { } a && a["done"]?.GetValue<bool>() == true) return a;
            await Task.Delay(25, TestContext.Current.CancellationToken);
        }
        Assert.Fail($"no answer to {rid} in {seconds} s");
        return null!;
    }
}
