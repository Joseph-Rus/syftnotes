using System.Net;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Avalonia.Headless.XUnit;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using StudyStash.App.Services;
using StudyStash.Core;
using StudyStash.Core.Canvas;
using StudyStash.Library;

namespace StudyStash.App.Tests;

/// <summary>
/// "Use just this computer" (Settings → Connection → This computer, docs/one-download.md): a laptop that sent to a
/// library on another computer becomes the library itself, and everything in the old library comes over: every class
/// as it was set up, every lecture under its class with its notes and transcript, attached files, captured notes,
/// chats. The old library is left exactly as it was, and a switch that stops part-way picks up where it stopped.
/// </summary>
public sealed class UseJustThisComputerTests
{
    const string OldAddress = "http://mac-mini:8787";

    static string BuiltEngine()
    {
        var bin = new DirectoryInfo(AppContext.BaseDirectory);
        string config = bin.Parent!.Name, framework = bin.Name;
        string engineDir = Path.GetFullPath(Path.Combine(bin.FullName, "..", "..", "..", "..", "..", "src", "StudyStash.Engine", "bin", config, framework));
        return Path.Combine(engineDir, OperatingSystem.IsWindows() ? "studystash.exe" : "studystash");
    }

    sealed record Lib(string Url, WebApplication App, Config Cfg, Store Store) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            await App.DisposeAsync();
            Store.Dispose();
        }
    }

    /// <summary>A real library (its web app and API) on a free loopback port, with no AI to reach.</summary>
    static async Task<Lib> LibraryAsync(TempHome home, string name, string password, string tag, params ClassDef[] classes)
    {
        var cfg = new Config(home[tag + "-home"], home[tag + "-pool"])
        {
            PoolName = name, PoolPassword = password, OllamaEnabled = false, OllamaHost = "http://127.0.0.1:9", Classes = [.. classes],
        };
        Directory.CreateDirectory(cfg.Home);
        Configs.Save(cfg);
        var store = new Store(cfg.DbPath, cfg.PoolDir);
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(k => k.Listen(IPAddress.Loopback, 0));
        var app = LibraryWeb.Build(builder, cfg, store, new Pipeline(cfg, store, log: _ => { }), new LibraryWebOptions
        {
            ListModels = _ => Task.FromResult<List<(string, double)>?>(null), Tailscale = () => new TailscaleInfo(false, false, "", "", []),
            Latest = _ => Task.FromResult<Release?>(null), RamGb = () => 16, HostName = () => "mac-mini",
            ReadDocument = (_, _) => Task.FromResult<string?>(null),
        });
        await app.StartAsync(TestContext.Current.CancellationToken);
        string url = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
        return new Lib(url, app, cfg, store);
    }

    /// <summary>Reaching "mac-mini" (the old library, on another computer as far as the app can tell) is really
    /// reaching <paramref name="old"/> here; <paramref name="handler"/> can stand in the way.</summary>
    static Func<string, HttpClient> Network(Lib old, Func<HttpMessageHandler, HttpMessageHandler>? handler = null) => url =>
    {
        bool isOld = url.TrimEnd('/') == OldAddress;
        HttpMessageHandler inner = new SocketsHttpHandler();
        if (isOld && handler is not null) inner = handler(inner);
        return new HttpClient(inner) { BaseAddress = new Uri((isOld ? old.Url : url).TrimEnd('/') + "/"), Timeout = TimeSpan.FromMinutes(2) };
    };

    static void LaptopOf(string home, string url, string password, string name, AppRole role = AppRole.Laptop)
    {
        new AppSettings { SetupDone = true, Role = role }.Save(home);
        var cc = Configs.LoadClient(home);
        cc.ServerUrl = url;
        cc.PoolKey = password;
        cc.PoolName = name;
        cc.DisplayName = "Ada";
        Configs.SaveClient(cc);
    }

    static void FileLecture(Store store, string id, string cls, string title, string summary, string by = "ollama", int day = 1) => store.Save(new Meeting(id)
    {
        Title = title, Date = $"2026-09-{day:D2}T10:00:00Z", Owner = "Sam", Folder = cls == Configs.Unsorted ? "" : cls,
        NotesMarkdown = $"Typed in class: {title}.", PrivateNotes = $"Ask about {title} in office hours.",
        Transcript = $"Today in {cls} we cover {title}. " + string.Concat(Enumerable.Repeat($"More about {title}, said slowly. ", 40)),
    }, new Classification(cls, by == "human" ? 1 : 0.85, by, title, ["topic of " + id]), summary, summary.Length > 0 ? "qwen3:8b" : "");

    static void Put(string root, string rel, string text)
    {
        string path = Path.Combine(root, rel.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    static Attachment Attach(Store store, string id, string cls, string? lecture, string name, int size, string text)
    {
        byte[] bytes = new byte[size];
        new Random(size).NextBytes(bytes);
        "%PDF-1.4"u8.CopyTo(bytes);
        File.WriteAllBytes(Path.Combine(store.AttachmentsDir(cls), name), bytes);
        var a = new Attachment(id, name, name, cls, lecture, size, "application/pdf", "2026-09-01T10:00:00Z", text, Attachment.Read, lecture is not null);
        store.AddAttachment(a);
        return a;
    }

    /// <summary>Every file under the old library's home and notes folder, by path, with its SHA-256: what "left
    /// exactly as it was" means.</summary>
    static Dictionary<string, string> Snapshot(params string[] roots)
    {
        var all = new Dictionary<string, string>();
        foreach (string root in roots)
            foreach (string file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
                all[file] = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file)));
        return all;
    }

    [AvaloniaFact]
    public async Task Using_just_this_computer_brings_every_class_lecture_note_and_file_and_leaves_the_old_library_as_it_was()
    {
        string exe = BuiltEngine();
        Assert.True(File.Exists(exe), $"build the solution first: no {exe}");
        using var home = new TempHome();
        ClassDef[] classes =
        [
            new("CS 101", ["cs101", "Comp Sci"], "Recursion, stacks and queues"),
            new("Bio 110", ["biology"], "Cells and genes"),
            new("History 200", [], ""),
        ];
        await using var old = await LibraryAsync(home, "Sam's library", "old-pw", "old", classes);
        var store = old.Store;

        // Lectures: notes written by the AI, one sorted by a person, one Unsorted, one whose note file was edited by
        // hand afterwards, enough to take more than a page, and one whose notes were never written (the AI was off).
        FileLecture(store, "lec-recursion", "CS 101", "Recursion", "## Recursion\n\nA function that calls itself.\n\n- base case\n- step");
        FileLecture(store, "lec-cells", "Bio 110", "Cells", "## Cells\n\nThe unit of life.", by: "human", day: 2);
        FileLecture(store, "lec-rome", "History 200", "Rome", "## Rome\n\nFounded, then fell.", day: 3);
        FileLecture(store, "lec-loose", Configs.Unsorted, "Guest talk", "", by: "none", day: 4);
        for (int i = 0; i < 22; i++) FileLecture(store, $"lec-more-{i:D2}", i % 2 == 0 ? "CS 101" : "Bio 110", $"Week {i + 2}", $"## Week {i + 2}\n\nNotes {i}.", day: i % 28 + 1);
        File.AppendAllText(store.Get("lec-recursion")!.MdPath!, "\nMy own line: the base case stops it.\n");
        store.Enqueue(new Meeting("lec-waiting") { Title = "Genes", Date = "2026-09-20T10:00:00Z", Folder = "Bio 110", Transcript = "Genes are made of DNA. " + new string('x', 400) });
        store.ClaimNext();
        store.MarkFailed("lec-waiting", "The AI engine was off.");

        // Files attached to a lecture and to a class; notes captured into a class and waiting in the Inbox; the Canvas
        // mirror; the AI's history (hidden, never brought).
        var slides = Attach(store, "att-slides", "CS 101", "lec-recursion", "week 1 slides.pdf", 60_000, "Slide 1: recursion");
        var syllabus = Attach(store, "att-syllabus", "Bio 110", null, "syllabus.pdf", 5_000, "Week 1: cells");
        string[] others = ["CS 101/Notes/2026-09-02 1015 Office hours.md", "Inbox/2026-09-03 0900 Study group.md", "Bio 110/Canvas/assignments/Lab 1/spec.md"];
        foreach (string rel in others) Put(old.Cfg.PoolDir, rel, $"# {Path.GetFileNameWithoutExtension(rel)}\n\nWritten on the old library.\n");
        Put(old.Cfg.PoolDir, ".git/HEAD", "ref: refs/heads/main\n");

        // Canvas: the school, and which course each class is. How notes are written. A chat.
        CanvasSettings.Update(old.Cfg.Home, s =>
        {
            s.Url = "https://school.instructure.com";
            s.Courses["CS 101"] = 111;
            s.Courses["Bio 110"] = 222;
            s.Available["111"] = "Intro to Computer Science";
            s.CourseInfo["111"] = new CourseInfo("CS101", "Intro to Computer Science", "Fall 2026");
            s.Chosen = ["111", "222"];
        });
        new StudyStash.Core.Ai.AiSettings { Provider = "ollama", Models = { ["ollama"] = "qwen3:8b" } }.Save(old.Cfg.Home);
        Put(old.Cfg.Home, "chats/chat-1.json", """
            {"id": "chat-1", "title": "Exam prep", "class_name": "CS 101", "lecture": "", "provider": "claude", "session": "sess-on-mac-mini",
             "updated": "2026-09-05T10:00:00Z", "messages": [{"role": "user", "text": "What's a base case?", "when": "2026-09-05T10:00:00Z"}]}
            """);

        var before = Snapshot(old.Cfg.Home, old.Cfg.PoolDir);
        var oldIds = store.KnownIds();

        string laptop = home["laptop"];
        LaptopOf(laptop, OldAddress, "old-pw", "Sam's library");
        using var host = new AppHost(laptop, log: _ => { });
        using var model = SettingsModel.Make(host)
            .WithLibraryHere(() => new LibraryHere { Command = [exe, "--no-ollama"], Folder = home["new-pool"], FirstPort = 28787 })
            .WithLibraryHttp(Network(old));
        model.DisplayName = "Ada";
        try
        {
            // The student sees what will happen before anything does.
            Assert.Contains("Stop using Sam's library on mac-mini", model.JustThisComputerLine, StringComparison.Ordinal);
            model.AskBecomeLibraryCommand.Execute(null);
            Assert.True(model.ConfirmingBecomeLibrary);
            Assert.StartsWith("Sam's library on mac-mini keeps its own copy. Classes and notes come here", model.BecomeLibraryQuestion, StringComparison.Ordinal);

            await model.ConfirmBecomeLibraryCommand.ExecuteAsync(null);

            Assert.Equal(AppRole.Both, host.Settings.Role);
            Assert.False(model.ConfirmingBecomeLibrary);
            Assert.False(model.HasPendingBring);
            Assert.Null(RoleSwitch.Pending(laptop));
            Assert.Contains("26 lectures and 3 classes came over from Sam's library, with 5 files and 1 chat.", model.SwitchSay, StringComparison.Ordinal);
            Assert.Contains("1 of them had no notes yet", model.SwitchSay, StringComparison.Ordinal);
            Assert.EndsWith("It keeps its own copy.", model.SwitchSay, StringComparison.Ordinal);
            // Just this computer: its library listens to this computer alone, and this computer keeps recording.
            Assert.True(LibraryHere.OnlyHere(Configs.Load(laptop)));
            Assert.True(Setup.IsThisComputer(host.Client().ServerUrl));
        }
        finally
        {
            if (host.LocalLibrary is { } svc) await svc.StopAsync();
        }

        // The classes, in their order (so their colours), each with its other names and what it covers.
        var cfg = Configs.Load(laptop);
        Assert.Equal(["CS 101", "Bio 110", "History 200"], cfg.ClassNames());
        foreach (var c in classes)
        {
            var mine = cfg.Classes.Single(k => k.Name == c.Name);
            Assert.Equal(c.Aliases, mine.Aliases);
            Assert.Equal(c.Description, mine.Description);
        }
        Assert.Equal("Sam's library", cfg.PoolName);
        Assert.Equal("old-pw", cfg.PoolPassword);
        // Canvas: the same school, each class its course.
        var canvas = CanvasSettings.Load(laptop);
        Assert.Equal("https://school.instructure.com", canvas.Url);
        Assert.Equal(new Dictionary<string, long> { ["CS 101"] = 111, ["Bio 110"] = 222 }, canvas.Courses);
        Assert.Equal(["111", "222"], canvas.Chosen);
        Assert.Equal("qwen3:8b", StudyStash.Core.Ai.AiSettings.Load(laptop).Models["ollama"]);

        // Every lecture, under the same class, its notes and transcript, its note file as it was (edits included).
        using var mine2 = new Store(cfg.DbPath, cfg.PoolDir);
        Assert.Equal(oldIds.Count, mine2.NoteCount());
        foreach (string id in oldIds)
        {
            var was = store.Get(id)!;
            var now = mine2.Get(id);
            Assert.NotNull(now);
            var (wm, nm) = (store.Meeting(was), mine2.Meeting(now));
            Assert.Equal(wm.Transcript, nm.Transcript);
            Assert.Equal(wm.NotesMarkdown, nm.NotesMarkdown);
            Assert.Equal(wm.PrivateNotes, nm.PrivateNotes);
            Assert.Equal(wm.Folder, nm.Folder);
            if (id == "lec-waiting") continue; // this library writes its notes itself
            Assert.Equal(was.ClassName, now.ClassName);
            Assert.Equal(was.ClassifiedBy, now.ClassifiedBy);
            Assert.Equal(was.SummaryMd, now.SummaryMd);
            Assert.Equal(was.LectureTitle, now.LectureTitle);
            Assert.Equal(Path.GetRelativePath(old.Cfg.PoolDir, was.MdPath!), Path.GetRelativePath(cfg.PoolDir, now.MdPath!));
            Assert.Equal(File.ReadAllText(was.MdPath!), File.ReadAllText(now.MdPath!));
        }
        Assert.Contains("My own line: the base case stops it.", File.ReadAllText(mine2.Get("lec-recursion")!.MdPath!), StringComparison.Ordinal);

        // The attached files, each with its words, where it was; the other notes and files; not the hidden history.
        foreach (var a in new[] { slides, syllabus })
        {
            var b = mine2.GetAttachment(a.Id);
            Assert.NotNull(b);
            Assert.Equal((a.Name, a.ClassName, a.NoteId, a.Text, a.Used), (b.Name, b.ClassName, b.NoteId, b.Text, b.Used));
            Assert.Equal(File.ReadAllBytes(store.AttachmentPath(a)), File.ReadAllBytes(mine2.AttachmentPath(b)));
        }
        foreach (string rel in others)
            Assert.Equal(File.ReadAllText(Path.Combine(old.Cfg.PoolDir, rel)), File.ReadAllText(Path.Combine(cfg.PoolDir, rel)));
        Assert.False(Directory.Exists(Path.Combine(cfg.PoolDir, ".git")));
        // The chat, its conversation to be started afresh here.
        var chat = JsonNode.Parse(File.ReadAllText(Path.Combine(laptop, "chats", "chat-1.json")))!;
        Assert.Equal("", chat["session"]!.GetValue<string>());
        Assert.Equal("What's a base case?", chat["messages"]![0]!["text"]!.GetValue<string>());

        // The old library: not one file changed, nothing gone.
        Assert.Equal(before, Snapshot(old.Cfg.Home, old.Cfg.PoolDir));
        Assert.Equal(oldIds, store.KnownIds());
    }

    /// <summary>Stands between the app and the old library: the nth request it's asked to break fails, either before
    /// anything is said (the Wi-Fi dropped) or part-way through a file (the old library went to sleep mid-send).</summary>
    sealed class Breaks(Func<HttpRequestMessage, bool> which, int nth, bool midFile) : DelegatingHandler
    {
        int seen;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (!which(request) || ++seen != nth) return await base.SendAsync(request, ct);
            if (!midFile) throw new HttpRequestException("the network went away");
            var response = await base.SendAsync(request, ct);
            byte[] start = new byte[1000];
            await using (var body = await response.Content.ReadAsStreamAsync(ct)) await body.ReadExactlyAsync(start, ct);
            response.Content = new StreamContent(new Cut(start));
            return response;
        }

        sealed class Cut(byte[] start) : MemoryStream(start)
        {
            public override int Read(byte[] buffer, int offset, int count) =>
                Position < Length ? base.Read(buffer, offset, count) : throw new IOException("the old library stopped sending");

            public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default) =>
                Position < Length ? base.ReadAsync(buffer, ct) : throw new IOException("the old library stopped sending");
        }
    }

    [AvaloniaFact]
    public async Task Stopping_part_way_keeps_what_came_says_so_and_try_again_brings_only_what_is_missing()
    {
        using var home = new TempHome();
        await using var old = await LibraryAsync(home, "Sam's library", "old-pw", "old", new ClassDef("CS 101", ["cs101"], "Recursion"));
        for (int i = 0; i < 30; i++) FileLecture(old.Store, $"lec-{i:D2}", "CS 101", $"Week {i + 1}", $"## Week {i + 1}\n\nNotes {i}.", day: i % 28 + 1);
        Attach(old.Store, "att-a", "CS 101", "lec-00", "slides a.pdf", 40_000, "Slides A");
        Attach(old.Store, "att-b", "CS 101", "lec-01", "slides b.pdf", 40_000, "Slides B");
        Put(old.Cfg.PoolDir, "CS 101/Notes/a thought.md", "Remember the base case.\n");
        var before = Snapshot(old.Cfg.Home, old.Cfg.PoolDir);

        // This computer is the library already (the switch happened); the old library is still to come over.
        await using var here = await LibraryAsync(home, "Sam's library", "old-pw", "here");
        string laptop = home["laptop"];
        LaptopOf(laptop, here.Url, "old-pw", "Sam's library", AppRole.Both);
        using var host = new AppHost(laptop, log: _ => { });
        var from = new OldLibrary(OldAddress, "old-pw", "Sam's library");

        // 1. The network goes away on the second page of lectures: the first page stays, and it's remembered.
        var e = await Assert.ThrowsAsync<InvalidOperationException>(() => RoleSwitch.BringLecturesAsync(host, from,
            http: Network(old, inner => new Breaks(r => r.RequestUri!.Query.Contains("lean=1") && r.RequestUri.Query.Contains("after="), 1, midFile: false) { InnerHandler = inner }),
            ct: TestContext.Current.CancellationToken));
        Assert.Equal("Can't reach your old library. Is its computer on, and is Tailscale connected?", e.Message);
        Assert.Equal(LibraryMove.PageSize, here.Store.NoteCount());
        Assert.True(RoleSwitch.Pending(laptop) is { Tried: true, Url: OldAddress });

        // 2. Settings says so, with Try again; this time the old library stops part-way through the second file.
        using (var model = SettingsModel.Make(host).WithLibraryHttp(Network(old,
                   inner => new Breaks(r => r.RequestUri!.AbsolutePath.StartsWith("/api/v2/attachments/", StringComparison.Ordinal), 2, midFile: true) { InnerHandler = inner })))
        {
            Assert.True(model.HasPendingBring);
            Assert.Equal("Try again", model.PendingBringAction);
            Assert.Equal("Not everything from Sam's library has come over yet", model.PendingBringTitle);
            await model.BringAgainCommand.ExecuteAsync(null);
            Assert.StartsWith("Not everything from Sam's library has come over yet. Can't reach your old library.", model.SwitchSay, StringComparison.Ordinal);
            Assert.True(model.HasPendingBring);
        }
        Assert.Equal(30, here.Store.NoteCount());
        Assert.NotNull(here.Store.GetAttachment("att-a"));
        Assert.Null(here.Store.GetAttachment("att-b")); // cut short: nothing of it kept

        // 3. Try again with the old library awake: only what's missing comes, and the row goes.
        using (var model = SettingsModel.Make(host).WithLibraryHttp(Network(old)))
        {
            await model.BringAgainCommand.ExecuteAsync(null);
            Assert.Equal("Every lecture from Sam's library is already here. 2 files came over too. It keeps its own copy.", model.SwitchSay);
            Assert.False(model.HasPendingBring);
        }
        Assert.Null(RoleSwitch.Pending(laptop));

        // 4. Once more changes nothing: never a second copy of anything.
        string again = await RoleSwitch.BringLecturesAsync(host, from, http: Network(old), ct: TestContext.Current.CancellationToken);
        Assert.Equal("Every lecture from Sam's library is already here. It keeps its own copy.", again);
        Assert.Equal(30, here.Store.NoteCount());
        Assert.Equal(["att-a", "att-b"], here.Store.ListAttachments().Select(a => a.Id).Order());
        foreach (var a in old.Store.ListAttachments())
            Assert.Equal(File.ReadAllBytes(old.Store.AttachmentPath(a)), File.ReadAllBytes(here.Store.AttachmentPath(here.Store.GetAttachment(a.Id)!)));
        string cs = Path.Combine(here.Cfg.PoolDir, "CS 101");
        Assert.Equal(30, Directory.GetFiles(cs, "*.md").Length);
        Assert.Equal(["slides a.pdf", "slides b.pdf"], Directory.GetFiles(Path.Combine(cs, Store.AttachmentsFolder)).Select(Path.GetFileName).Order());
        Assert.Equal(["a thought.md"], Directory.GetFiles(Path.Combine(cs, "Notes")).Select(Path.GetFileName));
        Assert.Equal(["CS 101"], here.Cfg.ClassNames());
        Assert.Equal(before, Snapshot(old.Cfg.Home, old.Cfg.PoolDir));
    }
}
