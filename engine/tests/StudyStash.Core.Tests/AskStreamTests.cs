using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
using StudyStash.Core.Ai;
using StudyStash.Library;

namespace StudyStash.Core.Tests;

/// <summary>An answer read as it's written: out of the AI's half-written JSON, through the library's streamed
/// /api/v2/ai/ask, to the app's <see cref="AiRemote"/> — over the library's own test server (a laptop's library) and
/// over a real socket on this computer (just this computer). Every engine is a fake that writes its answer a piece at
/// a time; nothing calls a real AI.</summary>
public class AskStreamTests
{
    [Theory]
    [InlineData("", null)]
    [InlineData("{\"ans", null)]
    [InlineData("{\"answer\": \"", "")]
    [InlineData("{\"answer\": \"Cells have mem", "Cells have mem")]
    [InlineData("{\"answer\":\"Two lines:\\nthe second\", \"sources\": [1]}", "Two lines:\nthe second")]
    [InlineData("{\"answer\": \"She said \\\"osmosis\\\" twice", "She said \"osmosis\" twice")]
    [InlineData("{\"answer\": \"caf\\u00e9 and then", "café and then")]
    public void The_answer_so_far_is_read_out_of_half_written_JSON(string raw, string? expected) =>
        Assert.Equal(expected, AskAnswer.SoFar(raw));

    [Fact]
    public void An_escape_cut_off_at_the_end_waits_for_the_rest_of_it()
    {
        Assert.Equal("A quote: ", AskAnswer.SoFar("{\"answer\": \"A quote: \\"));
        Assert.Equal("caf", AskAnswer.SoFar("{\"answer\": \"caf\\u00e"));
        Assert.Equal("café", AskAnswer.SoFar("{\"answer\": \"caf\\u00e9"));
    }

    [Fact]
    public void Latex_reads_the_same_as_it_will_in_the_finished_answer()
    {
        // Doubled, as the prompt asks; single, as models sometimes write it anyway (\f and \t are JSON escapes too).
        Assert.Equal("So $\\frac{a}{b}$ and $\\sqrt{x}$ and $\\theta$", AskAnswer.SoFar("{\"answer\": \"So $\\\\frac{a}{b}$ and $\\sqrt{x}$ and $\\theta$"));
        Assert.Equal("So $\\frac{a}{b}$ is", AskAnswer.SoFar("{\"answer\": \"So $\\frac{a}{b}$ is"));
    }

    static Config Cfg(TempDir dir) => new(dir["home"], dir["pool"]) { PoolPassword = "pw", OllamaEnabled = true, OllamaModel = "qwen3:8b" };

    static LibraryWebOptions Options(AiJobs ai) => new()
    {
        ListModels = _ => Task.FromResult<List<(string, double)>?>(null), Tailscale = () => new TailscaleInfo(),
        Latest = _ => Task.FromResult<Release?>(null), Ai = ai,
    };

    /// <summary>An answer in pieces, held after <paramref name="before"/> of them until the test lets it go on.</summary>
    static ScriptedAi Writer(string id, TaskCompletionSource<bool> gate, int before, params string[] pieces) => new(id, id)
    {
        Script = [.. pieces.Select(p => new AiEvent("text", p))], Gate = gate, GateAfter = before,
    };

    static readonly string[] Pieces = ["{\"answer\": \"Cells ", "have membranes", " that let water through.\", \"sources\": [1]}"];

    static AskRequest Question(string engine = "claude") => new("What did we study today?")
    {
        Engine = engine, Live = "Today we studied the cell membrane and osmosis in class.",
    };

    /// <summary>Asks through <paramref name="remote"/> and waits until the first words are on their way to the app
    /// while the engine is still held, then lets it finish.</summary>
    static async Task<(AskReply? Reply, List<string> Seen)> FirstWordsBeforeTheEnd(AiRemote remote, ScriptedAi engine, TaskCompletionSource<bool> gate,
        AskRequest? question = null)
    {
        var seen = new List<string>();
        var firstWords = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var asking = remote.AskAsync(question ?? Question(engine.Id), soFar =>
        {
            lock (seen) seen.Add(soFar);
            firstWords.TrySetResult(soFar);
        }, CancellationToken.None);
        string first = await firstWords.Task.WaitAsync(TimeSpan.FromSeconds(20));
        Assert.Equal("Cells", first.TrimEnd());
        Assert.False(engine.Finished); // the engine is still writing: the app has its first words anyway
        gate.SetResult(true);
        var reply = await asking.WaitAsync(TimeSpan.FromSeconds(20));
        return (reply, seen);
    }

    [Fact]
    public async Task The_library_sends_the_first_words_before_the_engine_has_finished()
    {
        using var dir = new TempDir();
        var cfg = Cfg(dir);
        using var store = new Store(cfg.DbPath, cfg.PoolDir);
        var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var claude = Writer("claude", gate, 1, Pieces);
        var ai = new AiJobs(cfg.Home) { Providers = id => id == "claude" ? claude : new ScriptedAi(id), Checks = new FakeChecks().Installed("claude").Build() };
        await using var site = await TestSite.StartAsync(b => LibraryWeb.Build(b, cfg, store, new Pipeline(cfg, store, log: _ => { }), Options(ai)));

        var (reply, seen) = await FirstWordsBeforeTheEnd(new AiRemote("http://localhost", "pw", site.Client), claude, gate);

        Assert.True(claude.Streamed);
        Assert.Equal(["Cells", "Cells have membranes", "Cells have membranes that let water through."], seen.Select(s => s.TrimEnd()));
        Assert.NotNull(reply);
        Assert.Equal(("Cells have membranes that let water through.", "claude", false), (reply!.Answer, reply.Engine, reply.FellBack));
        Assert.Single(reply.Sources);
    }

    [Fact]
    public async Task On_just_this_computer_the_first_words_come_over_a_real_socket_before_the_end()
    {
        using var dir = new TempDir();
        var cfg = Cfg(dir);
        using var store = new Store(cfg.DbPath, cfg.PoolDir);
        var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var ollama = Writer("ollama", gate, 1, Pieces);
        var ai = new AiJobs(cfg.Home) { Providers = id => id == "ollama" ? ollama : new ScriptedAi(id), Checks = new FakeChecks().Build() };
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        await using var app = LibraryWeb.Build(builder, cfg, store, new Pipeline(cfg, store, log: _ => { }), Options(ai));
        await app.StartAsync();
        using var http = new HttpClient();

        var (reply, _) = await FirstWordsBeforeTheEnd(new AiRemote(app.Urls.Single(), "pw", http), ollama, gate, Question("ollama"));

        Assert.Equal("Cells have membranes that let water through.", reply!.Answer);
    }

    [Fact]
    public async Task A_slow_writer_is_not_cut_off_once_it_has_started_answering()
    {
        using var dir = new TempDir();
        var cfg = Cfg(dir);
        using var store = new Store(cfg.DbPath, cfg.PoolDir);
        var claude = new ScriptedAi("claude") { Script = [.. Pieces.Select(p => new AiEvent("text", p))], Pause = TimeSpan.FromMilliseconds(400) };
        var ollama = new ScriptedAi("ollama") { Script = [new AiEvent("final", """{"answer":"From Ollama.","sources":[]}""")] };
        var ai = new AiJobs(cfg.Home)
        {
            Providers = id => id switch { "claude" => claude, "ollama" => ollama, _ => new ScriptedAi(id) },
            Checks = new FakeChecks().Installed("claude").Build(),
            AskTimeout = TimeSpan.FromSeconds(1), // the whole answer takes longer; its first words don't
        };
        await using var site = await TestSite.StartAsync(b => LibraryWeb.Build(b, cfg, store, new Pipeline(cfg, store, log: _ => { }), Options(ai)));
        var remote = new AiRemote("http://localhost", "pw", site.Client);

        var reply = await remote.AskAsync(Question(), _ => { }, CancellationToken.None);

        Assert.Equal(("claude", false, "Cells have membranes that let water through."), (reply!.Engine, reply.FellBack, reply.Answer));
    }

    [Fact]
    public async Task An_engine_that_fails_partway_keeps_what_it_wrote_and_says_what_stopped_it()
    {
        using var dir = new TempDir();
        var cfg = Cfg(dir);
        using var store = new Store(cfg.DbPath, cfg.PoolDir);
        var claude = new ScriptedAi("claude") { Script = [new AiEvent("text", Pieces[0]), new AiEvent("text", Pieces[1]), AiEvent.Error("rate limit exceeded")] };
        var ollama = new ScriptedAi("ollama") { Script = [new AiEvent("final", """{"answer":"From Ollama.","sources":[]}""")] };
        var ai = new AiJobs(cfg.Home)
        {
            Providers = id => id switch { "claude" => claude, "ollama" => ollama, _ => new ScriptedAi(id) },
            Checks = new FakeChecks().Installed("claude").Build(),
        };
        await using var site = await TestSite.StartAsync(b => LibraryWeb.Build(b, cfg, store, new Pipeline(cfg, store, log: _ => { }), Options(ai)));
        var remote = new AiRemote("http://localhost", "pw", site.Client);
        string soFar = "";

        var stopped = await Assert.ThrowsAsync<LibraryRefusedException>(() => remote.AskAsync(Question(), s => soFar = s, CancellationToken.None));

        Assert.Equal("Cells have membranes", soFar); // not thrown away for Ollama's answer from scratch
        Assert.Equal(503, stopped.Status);
        Assert.Equal("Claude Code stopped partway through: rate limit exceeded", stopped.Message);
        Assert.False(ollama.Finished);
    }

    [Fact]
    public async Task An_answer_whose_sources_never_came_is_still_the_answer()
    {
        using var dir = new TempDir();
        var cfg = Cfg(dir);
        using var store = new Store(cfg.DbPath, cfg.PoolDir);
        var claude = new ScriptedAi("claude") { Script = [new AiEvent("text", "{\"answer\": \"Cells have membranes.\", \"sour")] };
        var ai = new AiJobs(cfg.Home) { Providers = id => id == "claude" ? claude : new ScriptedAi(id), Checks = new FakeChecks().Installed("claude").Build() };
        await using var site = await TestSite.StartAsync(b => LibraryWeb.Build(b, cfg, store, new Pipeline(cfg, store, log: _ => { }), Options(ai)));

        var reply = await new AiRemote("http://localhost", "pw", site.Client).AskAsync(Question(), _ => { }, CancellationToken.None);

        Assert.Equal(("Cells have membranes.", false), (reply!.Answer, reply.FellBack));
        Assert.Empty(reply.Sources);
    }

    [Fact]
    public async Task Stopping_closes_the_connection_and_stops_the_engine()
    {
        using var dir = new TempDir();
        var cfg = Cfg(dir);
        using var store = new Store(cfg.DbPath, cfg.PoolDir);
        var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var claude = Writer("claude", gate, 1, Pieces);
        var ai = new AiJobs(cfg.Home) { Providers = id => id == "claude" ? claude : new ScriptedAi(id), Checks = new FakeChecks().Installed("claude").Build() };
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        await using var app = LibraryWeb.Build(builder, cfg, store, new Pipeline(cfg, store, log: _ => { }), Options(ai));
        await app.StartAsync();
        using var http = new HttpClient();
        using var stop = new CancellationTokenSource();

        var asking = new AiRemote(app.Urls.Single(), "pw", http).AskAsync(Question(), _ => stop.Cancel(), stop.Token);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => asking.WaitAsync(TimeSpan.FromSeconds(20)));
        // The library notices the student left and stops the engine where it was held.
        await claude.StoppedWhileHeld.Task.WaitAsync(TimeSpan.FromSeconds(20));
        Assert.False(claude.Finished);
    }

    [Fact]
    public async Task A_library_asked_the_old_way_still_answers_once_at_the_end()
    {
        using var dir = new TempDir();
        var cfg = Cfg(dir);
        using var store = new Store(cfg.DbPath, cfg.PoolDir);
        var claude = new ScriptedAi("claude") { Script = [.. Pieces.Select(p => new AiEvent("text", p))] };
        var ai = new AiJobs(cfg.Home) { Providers = id => id == "claude" ? claude : new ScriptedAi(id), Checks = new FakeChecks().Installed("claude").Build() };
        await using var site = await TestSite.StartAsync(b => LibraryWeb.Build(b, cfg, store, new Pipeline(cfg, store, log: _ => { }), Options(ai)));

        var reply = await new AiRemote("http://localhost", "pw", site.Client).AskAsync(Question());

        Assert.False(claude.Streamed);
        Assert.Equal("Cells have membranes that let water through.", reply!.Answer);
    }

    /// <summary>A library from before answers streamed: it doesn't know "stream" and replies once, as plain JSON.</summary>
    sealed class OlderLibrary : HttpMessageHandler
    {
        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Body = await request.Content!.ReadAsStringAsync(ct);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"answer": "Cells have membranes.", "sources": [], "engine": "claude", "engine_name": "Claude Code"}""",
                    System.Text.Encoding.UTF8, "application/json"),
            };
        }
    }

    [Fact]
    public async Task An_app_asking_a_library_too_old_to_stream_gets_the_answer_once_at_the_end()
    {
        var older = new OlderLibrary();
        using var http = new HttpClient(older);
        var seen = new List<string>();

        var reply = await new AiRemote("http://library.test", "pw", http).AskAsync(Question(), seen.Add, CancellationToken.None);

        Assert.Contains("\"stream\":true", older.Body);
        Assert.Empty(seen); // nothing half-written to show: the whole answer is the reply
        Assert.Equal("Cells have membranes.", reply!.Answer);
    }

    [Fact]
    public async Task Typing_a_question_for_ollama_loads_its_model_once_and_a_cli_has_nothing_to_warm()
    {
        using var dir = new TempDir();
        var cfg = Cfg(dir);
        cfg.SummaryModel = "qwen3:14b";
        using var store = new Store(cfg.DbPath, cfg.PoolDir);
        var ollama = new ScriptedAi("ollama");
        var claude = new ScriptedAi("claude");
        var ai = new AiJobs(cfg.Home)
        {
            Providers = id => id switch { "ollama" => ollama, "claude" => claude, _ => new ScriptedAi(id) },
            Checks = new FakeChecks().Installed("claude").Build(),
        };
        await using var site = await TestSite.StartAsync(b => LibraryWeb.Build(b, cfg, store, new Pipeline(cfg, store, log: _ => { }), Options(ai)));
        var remote = new AiRemote("http://localhost", "pw", site.Client);

        await remote.WarmAsync("claude");
        await remote.WarmAsync("ollama");
        await remote.WarmAsync("ollama"); // a second keystroke a moment later: it's already loading

        Assert.Empty(claude.Warmed);
        Assert.Equal(["qwen3:14b"], ollama.Warmed); // the library's own model, the one it answers with
    }

    [Fact]
    public void A_plain_claude_answer_doesnt_wait_for_the_persons_own_mcp_servers()
    {
        var claude = new ClaudeProvider { PromptOnInput = false };
        Assert.Contains("--strict-mcp-config", claude.Command(new AiRequest("hi", "/lib"), stream: true));
        Assert.Contains("--include-partial-messages", claude.Command(new AiRequest("hi", "/lib"), stream: true));
        Assert.DoesNotContain("--strict-mcp-config", claude.Command(new AiRequest("hi", "/lib") { Tools = true, McpCommand = ["/app/studystash", "mcp"] }, stream: true));
    }
}
