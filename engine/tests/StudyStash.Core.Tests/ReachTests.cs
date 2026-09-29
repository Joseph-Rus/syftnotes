using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.TestHost;
using StudyStash.Core.Ai;
using StudyStash.Library;

namespace StudyStash.Core.Tests;

/// <summary>Putting the library on the internet for Claude on the web: Funnel through Tailscale (always faked here),
/// its problems in plain words, and the check that the address answers from the internet (DNS and HTTP faked).</summary>
public class ReachTests
{
    const string Ts = "https://mini.tail1234.ts.net";
    const string FunnelLink = "https://login.tailscale.com/f/funnel?node=abc";
    static readonly TailscaleInfo Running = new(true, true, "Running", "mini.tail1234.ts.net.", ["100.64.0.9"], "/usr/local/bin/tailscale");

    /// <summary>A fake Tailscale that prints <paramref name="lines"/> (on either stream: the watcher reads both as one)
    /// and, when <paramref name="exits"/> is false, never exits: the watcher is stopped at the line it waits for or
    /// else runs out of time, just as the real one would be.</summary>
    static StreamRunner Prints(bool exits, int code, params string[] lines) => (_, _, stopAt, _) =>
    {
        var said = new List<string>();
        foreach (string line in lines)
        {
            said.Add(line);
            if (stopAt(line)) return new WatchResult(null, string.Join("\n", said) + "\n", line);
        }
        string output = string.Join("\n", said) + (said.Count > 0 ? "\n" : "");
        return exits ? new WatchResult(code, output) : new WatchResult(null, output, TimedOut: true);
    };

    // --- Turning Funnel on and off -------------------------------------------------------------------------------

    [Fact]
    public void Funnel_not_allowed_yet_hands_back_the_page_that_allows_it_without_waiting()
    {
        // What Tailscale prints on a tailnet without Funnel: to stderr, then it waits for an admin to allow it.
        var reach = new ClaudeReach
        {
            Tailscale = () => Running,
            Watch = Prints(exits: false, 0, "Funnel is not enabled on your tailnet.", "To enable, visit:", "", "         " + FunnelLink),
        };
        var clock = Stopwatch.StartNew();
        var (url, problem) = reach.Set(8001, internet: true, on: true);
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(1));
        Assert.Null(url);
        Assert.Equal(new ReachProblem(ReachKind.NeedsPermission,
            "Your tailnet doesn't allow Funnel yet. Open the page below, allow it for this computer, then turn this on again.", FunnelLink), problem);
    }

    [Fact]
    public void Https_certificates_off_names_that_and_its_page()
    {
        var reach = new ClaudeReach
        {
            Tailscale = () => Running,
            Watch = Prints(exits: false, 0, "Serve is not enabled on your tailnet.", "To enable, visit:", "https://login.tailscale.com/f/serve?node=abc"),
        };
        var problem = reach.Set(8001, true, true).Problem!;
        Assert.Equal(ReachKind.NeedsHttps, problem.Kind);
        Assert.Equal("https://login.tailscale.com/f/serve?node=abc", problem.FixUrl);
        Assert.StartsWith("Your tailnet doesn't have HTTPS certificates turned on yet.", problem.Words);
    }

    [Fact]
    public void Each_other_tailscale_problem_says_what_to_do()
    {
        ReachProblem? With(TailscaleInfo ts, StreamRunner? watch = null) =>
            new ClaudeReach { Tailscale = () => ts, Watch = watch ?? Prints(true, 0) }.Set(8001, true, true).Problem;

        Assert.Equal(new ReachProblem(ReachKind.NotInstalled,
            "Tailscale isn't on the library's computer. Install it from tailscale.com/download and sign in.", "https://tailscale.com/download"),
            With(new TailscaleInfo()));
        Assert.Equal(new ReachProblem(ReachKind.NotRunning, "Tailscale isn't running on the library's computer. Open it and sign in."),
            With(new TailscaleInfo(true, false, "Stopped", Exe: "ts")));
        Assert.Equal(new ReachProblem(ReachKind.NotRunning, "Tailscale is signed out on the library's computer. Open it and sign in."),
            With(new TailscaleInfo(true, false, "NeedsLogin", Exe: "ts")));
        Assert.Equal(new ReachProblem(ReachKind.NotRunning, "Tailscale is still connecting on the library's computer. Try again in a moment."),
            With(new TailscaleInfo(true, false, "Starting", Exe: "ts")));
        Assert.Equal(ReachKind.NoName, With(Running with { Dns = "" })!.Kind);
        Assert.Equal(new ReachProblem(ReachKind.Timeout, "Tailscale didn't answer in time. Check it's running on the library's computer."),
            With(Running, Prints(exits: false, 0, "Waiting for the backend...")));
        Assert.Equal(ReachKind.Other, With(Running, (_, _, _, _) => null)!.Kind);
        var other = With(Running, Prints(true, 1, "error: " + new string('x', 400)))!;
        Assert.Equal(ReachKind.Other, other.Kind);
        Assert.StartsWith("Tailscale said: error: xxx", other.Words);
        Assert.True(other.Words.Length <= "Tailscale said: ".Length + 301);
        Assert.Equal("Tailscale stopped (code 2) without saying why.", With(Running, Prints(true, 2))!.Words);
    }

    [Fact]
    public void Serve_on_another_https_port_leaves_443_to_claude_and_says_the_port()
    {
        var asked = new List<IReadOnlyList<string>>();
        var reach = new ClaudeReach
        {
            Tailscale = () => Running,
            Watch = (_, args, _, _) =>
            {
                asked.Add(args);
                return new WatchResult(0, "");
            },
        };
        Assert.Equal(("https://mini.tail1234.ts.net:8443", (ReachProblem?)null), reach.Set(8000, internet: false, on: true, httpsPort: 8443));
        Assert.Equal(["serve", "--bg", "--https=8443", "http://127.0.0.1:8000"], asked[0]);
        Assert.Equal((Ts, (ReachProblem?)null), reach.Set(8001, internet: false, on: true));
        Assert.Equal(["serve", "--bg", "--https=443", "http://127.0.0.1:8001"], asked[1]);
        reach.Set(8000, internet: false, on: false, httpsPort: 8443);
        Assert.Equal(["serve", "--https=8443", "off"], asked[2]);
    }

    [Fact]
    public void Nothing_here_changes_tailscale_unless_the_service_says_so()
    {
        Assert.Throws<InvalidOperationException>(() => new ClaudeReach { Tailscale = () => Running }.Set(8001, true, true));
        var options = new LibraryWebOptions();
        Assert.Throws<InvalidOperationException>(() => options.Reach.Watch("tailscale", ["funnel"], _ => false, TimeSpan.FromSeconds(1)));
        Assert.Throws<InvalidOperationException>(() => options.Reach.Run("tailscale", ["funnel", "status"], TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public async Task Nothing_here_reaches_the_internet_unless_the_service_says_so()
    {
        var (reachable, words) = await new LibraryWebOptions().WebCheck.RunAsync(Ts);
        Assert.False(reachable);
        Assert.Equal("Couldn't check from here: checking from the internet is off here.", words);
    }

    // --- The real watcher, on a harmless shell command ----------------------------------------------------------

    static (string Exe, string[] Args) Shell(string unix, string windows) =>
        OperatingSystem.IsWindows() ? ("cmd.exe", ["/c", windows]) : ("/bin/sh", ["-c", unix]);

    [Fact]
    public void The_watcher_stops_at_a_link_printed_on_stderr_by_a_command_that_never_exits()
    {
        var (exe, args) = Shell($"echo 'To enable, visit:'; echo '  {FunnelLink}' >&2; sleep 30",
            $"echo To enable, visit: & echo   {FunnelLink} 1>&2 & ping -n 30 127.0.0.1 >nul");
        var clock = Stopwatch.StartNew();
        var said = ClaudeReach.WatchProcess(exe, args, line => line.Contains("login.tailscale.com"), TimeSpan.FromSeconds(20))!;
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(10), $"took {clock.Elapsed}");
        Assert.Null(said.ExitCode);
        Assert.False(said.TimedOut);
        Assert.Contains(FunnelLink, said.StoppedAt);
    }

    [Fact]
    public void The_watcher_reports_a_finished_command_and_gives_up_on_a_stuck_one()
    {
        var (exe, args) = Shell("echo done; exit 3", "echo done & exit 3");
        var done = ClaudeReach.WatchProcess(exe, args, _ => false, TimeSpan.FromSeconds(20))!;
        Assert.Equal(3, done.ExitCode);
        Assert.Equal("done", done.Output.Trim());

        (exe, args) = Shell("sleep 30", "ping -n 30 127.0.0.1 >nul");
        var clock = Stopwatch.StartNew();
        var stuck = ClaudeReach.WatchProcess(exe, args, _ => false, TimeSpan.FromMilliseconds(300))!;
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(10), $"took {clock.Elapsed}");
        Assert.True(stuck.TimedOut);
        Assert.Null(stuck.ExitCode);

        Assert.Null(ClaudeReach.WatchProcess(Path.Combine(Path.GetTempPath(), "no-such-tailscale-" + Guid.NewGuid()), [], _ => false, TimeSpan.FromSeconds(1)));
    }

    // --- Funnel status ------------------------------------------------------------------------------------------

    /// <summary>What `tailscale funnel status --json` prints with 443 proxied to <paramref name="proxy"/>.</summary>
    static string ServeConfig(bool funnel, string proxy = "http://127.0.0.1:8001") => new JsonObject
    {
        ["TCP"] = new JsonObject { ["443"] = new JsonObject { ["HTTPS"] = true } },
        ["Web"] = new JsonObject
        {
            ["mini.tail1234.ts.net:443"] = new JsonObject { ["Handlers"] = new JsonObject { ["/"] = new JsonObject { ["Proxy"] = proxy } } },
        },
        ["AllowFunnel"] = new JsonObject { ["mini.tail1234.ts.net:443"] = funnel },
    }.ToJsonString();

    [Fact]
    public void Funnel_status_says_whether_the_claude_port_is_on_the_internet()
    {
        Assert.True(ClaudeReach.FunnelsTo(ServeConfig(true), 8001));
        Assert.False(ClaudeReach.FunnelsTo(ServeConfig(true), 9001));
        Assert.False(ClaudeReach.FunnelsTo(ServeConfig(false), 8001)); // Serve on the tailnet only
        Assert.False(ClaudeReach.FunnelsTo("{}", 8001));
        Assert.Null(ClaudeReach.FunnelsTo("", 8001));
        Assert.Null(ClaudeReach.FunnelsTo("not json", 8001));

        var asked = new List<string>();
        var reach = new ClaudeReach
        {
            Tailscale = () => Running,
            Run = (_, args, _) =>
            {
                asked.Add(string.Join(" ", args));
                return new ProcResult(0, ServeConfig(true));
            },
        };
        Assert.True(reach.Status(8001));
        Assert.Equal(["funnel status --json"], asked);
        Assert.Null(new ClaudeReach { Tailscale = () => Running }.Status(8001)); // can't ask: can't tell
    }

    // --- The check from the internet ----------------------------------------------------------------------------

    /// <summary>Answers each request from <paramref name="answer"/>, the way a server at the looked-up address would.</summary>
    sealed class Answering(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> answer) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => answer(request, ct);
    }

    static Func<string, CancellationToken, Task<IPAddress[]>> Dns(params string[] ips) => (_, _) => Task.FromResult(ips.Select(IPAddress.Parse).ToArray());

    /// <summary>The Claude port of "Sam's library", with its internet address recorded: the check asks the real door.</summary>
    static async Task<(TestSite Site, TempDir Dir, Store Store)> DoorAsync()
    {
        var dir = new TempDir();
        var cfg = new Config(dir["home"], dir["pool"]) { PoolName = "Sam's library", PoolPassword = "pw", OllamaEnabled = false };
        Directory.CreateDirectory(cfg.Home);
        var store = new Store(cfg.DbPath, cfg.PoolDir);
        var access = new ClaudeAccess(cfg.Home) { PublicUrl = Ts };
        var site = await TestSite.StartAsync(b => ClaudeWeb.Build(b, cfg, new LibraryReader(cfg, store), access));
        return (site, dir, store);
    }

    [Fact]
    public async Task Study_stash_at_a_public_address_answers_from_the_internet()
    {
        var (site, dir, store) = await DoorAsync();
        await using var _site = site;
        using var _dir = dir;
        using var _store = store;
        var connectedTo = new List<IPAddress>();
        var check = new ReachCheck
        {
            Resolve = Dns("198.51.99.7"),
            Handler = ip =>
            {
                connectedTo.Add(ip);
                return site.App.GetTestServer().CreateHandler();
            },
        };
        Assert.Equal((true, "Answers from the internet."), await check.RunAsync(Ts));
        Assert.Equal([IPAddress.Parse("198.51.99.7")], connectedTo);
    }

    [Fact]
    public async Task A_tailnet_only_answer_means_the_internet_cant_find_it_yet()
    {
        var check = new ReachCheck
        {
            Resolve = Dns("100.101.102.103"),
            Handler = _ => throw new InvalidOperationException("never connects"),
        };
        Assert.Equal((false, "The internet can't find mini.tail1234.ts.net yet. Funnel can take a minute to start."), await check.RunAsync(Ts));
        Assert.False((await new ReachCheck { Resolve = Dns(), Handler = check.Handler }.RunAsync(Ts)).Reachable);
    }

    [Fact]
    public async Task Another_app_at_the_address_isnt_study_stash()
    {
        var check = new ReachCheck
        {
            Resolve = Dns("198.51.99.7"),
            Handler = _ => new Answering((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new { resource = "https://other.example/mcp" }),
            })),
        };
        Assert.Equal((false, "mini.tail1234.ts.net answers, but not with Study Stash. Is Funnel pointing at another app?"), await check.RunAsync(Ts));
    }

    [Fact]
    public async Task A_check_that_takes_too_long_says_it_couldnt_check()
    {
        var check = new ReachCheck
        {
            Resolve = Dns("198.51.99.7"), Wait = TimeSpan.FromMilliseconds(200),
            Handler = _ => new Answering(async (_, ct) =>
            {
                await Task.Delay(Timeout.Infinite, ct);
                return new HttpResponseMessage(HttpStatusCode.OK);
            }),
        };
        var clock = Stopwatch.StartNew();
        var (reachable, words) = await check.RunAsync(Ts);
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(5));
        Assert.False(reachable);
        Assert.StartsWith("Couldn't check from here:", words);
    }

    // --- Through the library's API ------------------------------------------------------------------------------

    static async Task<(TestSite Site, TempDir Dir, Store Store, Config Cfg)> LibraryAsync(ClaudeReach reach, ReachCheck check, string password = "pw")
    {
        var dir = new TempDir();
        var cfg = new Config(dir["home"], dir["pool"]) { PoolName = "Sam's library", PoolPassword = password, OllamaEnabled = false };
        Directory.CreateDirectory(cfg.Home);
        var store = new Store(cfg.DbPath, cfg.PoolDir);
        var site = await TestSite.StartAsync(b => LibraryWeb.Build(b, cfg, store, new Pipeline(cfg, store, log: _ => { }), new LibraryWebOptions
        {
            ListModels = _ => Task.FromResult<List<(string, double)>?>(null), Tailscale = () => new TailscaleInfo(),
            Latest = _ => Task.FromResult<Release?>(null), Claude = new ClaudeAccess(cfg.Home), Reach = reach, WebCheck = check,
        }));
        return (site, dir, store, cfg);
    }

    static readonly ReachCheck Answers = new() { Resolve = Dns("198.51.99.7"), Handler = _ => new Answering(Study) };

    /// <summary>What the Claude door says to the three questions the check asks.</summary>
    static Task<HttpResponseMessage> Study(HttpRequestMessage r, CancellationToken ct)
    {
        string path = r.RequestUri!.AbsolutePath;
        var response = path switch
        {
            "/.well-known/oauth-protected-resource/mcp" => new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new { resource = Ts + "/mcp" }) },
            "/.well-known/oauth-authorization-server" => new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new { issuer = Ts }) },
            _ => new HttpResponseMessage(HttpStatusCode.Unauthorized)
            {
                Headers = { WwwAuthenticate = { new("Bearer", $"resource_metadata=\"{Ts}/.well-known/oauth-protected-resource/mcp\"") } },
            },
        };
        return Task.FromResult(response);
    }

    [Fact]
    public async Task Turning_funnel_on_through_the_library_gives_the_address_to_paste_into_claude_and_checks_it()
    {
        var ran = new List<string>();
        int port = 0;
        var reach = new ClaudeReach
        {
            Tailscale = () => Running,
            Watch = (_, args, _, _) =>
            {
                ran.Add(string.Join(" ", args));
                return new WatchResult(0, "");
            },
            Run = (_, _, _) => new ProcResult(0, ServeConfig(true, $"http://127.0.0.1:{port}")),
        };
        var (site, dir, store, cfg) = await LibraryAsync(reach, Answers);
        port = ClaudeWeb.PortFor(cfg);
        await using var _site = site;
        using var _dir = dir;
        using var _store = store;
        var remote = new AiRemote("http://localhost", "pw", site.Client);

        var before = (await remote.AccessAsync())!.Web!;
        Assert.False(before.On);
        Assert.Null(before.McpUrl);
        Assert.Equal("Study Stash", before.Name);

        var on = (await remote.SetWebAsync(true))!.Web!;
        Assert.Equal($"funnel --bg --https=443 http://127.0.0.1:{ClaudeWeb.PortFor(cfg)}", ran[^1]);
        Assert.True(on.On);
        Assert.Equal("https://mini.tail1234.ts.net/mcp", on.McpUrl);
        Assert.Null(on.Problem);
        Assert.True(on.Reachable);
        Assert.Equal("Answers from the internet.", on.Words);
        Assert.NotNull(on.CheckedAt);
        Assert.True(on.HasPassword);
        Assert.Equal(Ts, new ClaudeAccess(cfg.Home).PublicUrl);

        // The same over the wire, snake_case, for any other client.
        var json = JsonNode.Parse(await (await site.Client.SendAsync(Req(HttpMethod.Get, "/api/v2/ai/access"))).Content.ReadAsStringAsync())!["web"]!;
        Assert.Equal("https://mini.tail1234.ts.net/mcp", json["mcp_url"]!.GetValue<string>());
        Assert.True(json["reachable"]!.GetValue<bool>());

        Assert.True((await remote.CheckWebAsync())!.Web!.Reachable);

        var off = (await remote.SetWebAsync(false))!.Web!;
        Assert.Equal("funnel --https=443 off", ran[^1]);
        Assert.False(off.On);
        Assert.Null(off.McpUrl);
        Assert.Null(off.Reachable);
        Assert.Equal("", new ClaudeAccess(cfg.Home).PublicUrl);
    }

    static HttpRequestMessage Req(HttpMethod m, string path, object? body = null)
    {
        var r = new HttpRequestMessage(m, path);
        r.Headers.Authorization = new("Bearer", "pw");
        if (body is not null) r.Content = JsonContent.Create(body);
        return r;
    }

    [Fact]
    public async Task A_tailscale_problem_comes_back_as_words_and_a_page_not_a_refusal()
    {
        var reach = new ClaudeReach { Tailscale = () => Running, Watch = Prints(false, 0, "Funnel is not enabled on your tailnet.", FunnelLink) };
        var (site, dir, store, cfg) = await LibraryAsync(reach, Answers);
        await using var _site = site;
        using var _dir = dir;
        using var _store = store;

        var r = await site.Client.SendAsync(Req(HttpMethod.Post, "/api/v2/ai/access/web", new { on = true }));
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var web = JsonNode.Parse(await r.Content.ReadAsStringAsync())!["web"]!;
        Assert.False(web["on"]!.GetValue<bool>());
        Assert.StartsWith("Your tailnet doesn't allow Funnel yet.", web["problem"]!.GetValue<string>());
        Assert.Equal(FunnelLink, web["fix_url"]!.GetValue<string>());
        Assert.Equal("", new ClaudeAccess(cfg.Home).PublicUrl);
    }

    [Fact]
    public async Task No_library_password_no_funnel()
    {
        var ran = 0;
        var reach = new ClaudeReach { Tailscale = () => Running, Watch = (_, _, _, _) => { ran++; return new WatchResult(0, ""); } };
        var (site, dir, store, _) = await LibraryAsync(reach, Answers, password: "");
        await using var _site = site;
        using var _dir = dir;
        using var _store = store;
        var remote = new AiRemote("http://localhost", "", site.Client);

        var refused = await Assert.ThrowsAsync<LibraryRefusedException>(() => remote.SetWebAsync(true));
        Assert.Equal(400, refused.Status);
        Assert.Equal("Set a library password first, so only you can let Claude in.", refused.Message);
        Assert.Equal(0, ran);
        Assert.False((await remote.AccessAsync())!.Web!.HasPassword);
    }

    [Fact]
    public async Task Funnel_turned_off_outside_the_app_forgets_the_address_when_checked()
    {
        string? status = null;
        var reach = new ClaudeReach
        {
            Tailscale = () => Running,
            Watch = (_, _, _, _) => new WatchResult(0, ""),
            Run = (_, _, _) => new ProcResult(0, status!),
        };
        var (site, dir, store, cfg) = await LibraryAsync(reach, Answers);
        status = ServeConfig(true, $"http://127.0.0.1:{ClaudeWeb.PortFor(cfg)}");
        await using var _site = site;
        using var _dir = dir;
        using var _store = store;
        var remote = new AiRemote("http://localhost", "pw", site.Client);
        Assert.True((await remote.SetWebAsync(true))!.Web!.On);

        status = "{}"; // `tailscale funnel reset` on the library's computer
        var after = (await remote.CheckWebAsync())!.Web!;
        Assert.False(after.On);
        Assert.Null(after.McpUrl);
        Assert.Equal("", new ClaudeAccess(cfg.Home).PublicUrl);
    }

    [Fact]
    public async Task An_older_library_without_the_route_answers_null()
    {
        using var http = new HttpClient(new Answering((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound))));
        var remote = new AiRemote("http://library.test", "pw", http);
        Assert.Null(await remote.SetWebAsync(true));
        Assert.Null(await remote.CheckWebAsync());
    }

    [Fact]
    public void The_Mac_apps_own_program_is_asked_as_the_command_line_and_its_usual_home_is_looked_in()
    {
        // Run bare from an app opened in the Finder, Tailscale.app's program opens its window instead of answering.
        HostInfo.Tailscale(run: (_, _, _) => null, candidates: []);
        Assert.Equal("1", Environment.GetEnvironmentVariable("TAILSCALE_BE_CLI"));
        // launchd's PATH has no /usr/local/bin, where the Mac app installs its command line.
        Assert.Equal("/usr/local/bin/tailscale", HostInfo.TailscalePaths[0]);
    }
}
