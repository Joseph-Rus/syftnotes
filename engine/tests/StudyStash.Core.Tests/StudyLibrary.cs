using StudyStash.Core.Ai;
using StudyStash.Core.Study;
using StudyStash.Library;

namespace StudyStash.Core.Tests;

/// <summary>A library on a scratch folder with a few lectures in two classes, served for real, and a scripted Claude
/// that answers whatever it's given (<see cref="Answers"/>, in turn, the last one again once they run out): what the
/// tests of Recipes, the exam flags, Review, exam prep and listening build on.</summary>
public sealed class StudyLibrary : IAsyncDisposable
{
    public required TempDir Dir { get; init; }
    public required Config Cfg { get; init; }
    public required Store Store { get; init; }
    public required ScriptedAi Claude { get; init; }
    public TestSite Site { get; private set; } = null!;
    public AiRemote Remote => new("http://localhost", "pw", Site.Client);
    public AiJobs Ai { get; private set; } = null!;

    /// <summary>The library's clock: a fixed morning, moved on by hand.</summary>
    public DateTimeOffset Now { get; set; } = new(2026, 10, 20, 9, 0, 0, TimeSpan.Zero);

    public StudyRemote Study => new("http://localhost", "pw", Site.Client);
    /// <summary>The voices Listen speaks with; null for this computer's own.</summary>
    public ISpeech? Speech { get; set; }

    /// <summary>Waits while a lecture's listen is being made, and says what it came to.</summary>
    public async Task<ListenInfo> ListenedAsync(string lecture)
    {
        for (int i = 0; i < 500; i++)
        {
            if (await Study.ListenAsync(lecture) is { State: not ListenInfo.Making } info) return info;
            await Task.Delay(20);
        }
        throw new TimeoutException($"{lecture}'s listen was still being made");
    }

    /// <summary>What the stand-in Claude says, one a call.</summary>
    public List<string> Answers { get; } = [];

    public const string BloodTranscript =
        "[00:00:05] Today we're doing blood vessels.\n" +
        "[00:01:10] Arteries carry blood away from the heart.\n" +
        "[00:02:30] This will definitely be on the exam, so make sure you know the three layers of an artery wall.\n" +
        "[00:04:00] Capillaries are one cell thick.\n" +
        "[00:05:15] The history of who discovered capillaries won't be on the exam.\n" +
        "[00:06:40] Veins carry blood back, and they have valves. Write this down.\n";

    public static async Task<StudyLibrary> StartAsync(Action<StudyLibrary>? more = null)
    {
        var dir = new TempDir();
        var cfg = new Config(dir["home"], dir["pool"])
        {
            PoolPassword = "pw", OllamaEnabled = true, Classes = [new ClassDef("BIO 110"), new ClassDef("CHEM 121")],
        };
        var store = new Store(cfg.DbPath, cfg.PoolDir);
        store.Save(new Meeting("v1") { Title = "Blood vessels", Date = "2026-10-08", Transcript = BloodTranscript },
            new Classification("BIO 110", 1, "folder"),
            "## Three kinds of vessel\n- Arteries carry blood away.\n- Capillaries are one cell thick.\n- Veins carry it back.", "Claude");
        store.Save(new Meeting("m1") { Title = "Muscles", Date = "2026-10-01", Transcript = "[00:00:10] Actin slides over myosin. You need to know the sliding filament model." },
            new Classification("BIO 110", 1, "folder"), "## Summary\nActin slides over myosin.", "Claude");
        store.Save(new Meeting("c1") { Title = "Moles", Date = "2026-10-07", Transcript = "A mole is 6.02e23 things." },
            new Classification("CHEM 121", 1, "folder"), "## Summary\nA mole is 6.02e23 things.", "Claude");
        var lib = new StudyLibrary
        {
            Dir = dir, Cfg = cfg, Store = store, Claude = new ScriptedAi("claude", "Claude"),
        };
        lib.Claude.Respond = _ => [new AiEvent("final", lib.Next())];
        more?.Invoke(lib);
        await lib.RestartAsync();
        return lib;
    }

    int answered;

    string Next()
    {
        if (Answers.Count == 0) return """{"answer":"Arteries, capillaries and veins.","sources":[1]}""";
        return Answers[Math.Min(answered++, Answers.Count - 1)];
    }

    public async Task RestartAsync()
    {
        if (Site is not null) await Site.DisposeAsync();
        new AiSettings { Provider = "claude", Fallback = false }.Save(Cfg.Home);
        Ai = new AiJobs(Cfg.Home)
        {
            Providers = id => id == "claude" ? Claude : new ScriptedAi(id) { Respond = _ => [new AiEvent("final", Next())] },
            Checks = new FakeChecks().Installed("claude").Build(),
        };
        Site = await TestSite.StartAsync(b => LibraryWeb.Build(b, Cfg, Store, new Pipeline(Cfg, Store, log: _ => { }), new LibraryWebOptions
        {
            ListModels = _ => Task.FromResult<List<(string, double)>?>(null), Tailscale = () => new TailscaleInfo(),
            Latest = _ => Task.FromResult<Release?>(null), Ai = Ai, Now = () => Now, Speech = Speech,
        }));
    }

    public Task<AskReply?> AskAsync(string question, string? lecture = null, string? className = null) =>
        Remote.AskAsync(new AskRequest(question) { Engine = "claude", Lecture = lecture, Class = className });

    public async ValueTask DisposeAsync()
    {
        await Site.DisposeAsync();
        Store.Dispose();
        Dir.Dispose();
    }
}
