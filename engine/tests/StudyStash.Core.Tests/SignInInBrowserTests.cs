using System.Net;
using Microsoft.AspNetCore.Builder;
using StudyStash.Core.Ai;
using StudyStash.Library;

namespace StudyStash.Core.Tests;

/// <summary>
/// Every Sign in button signs in with the AI's own sign-in, hidden, in the student's browser (never a terminal window),
/// through the library's /api/v2/ai/engines/{id}/sign-in on a real loopback server (the route only answers its own
/// computer). The AI's CLI is a stand-in script of the test's own, found only through <see cref="FakeChecks.InstalledAt"/>,
/// so no test here can reach the real Claude Code, Codex, agy, an account or a browser.
/// </summary>
public class SignInInBrowserTests
{
    sealed class Rig : IAsyncDisposable
    {
        readonly TempDir dir = new();
        readonly Store store;
        readonly WebApplication app;
        public string Bin => dir["bin"];
        public Config Cfg { get; }
        public FakeChecks Fake { get; } = new();
        public AiJobs Ai { get; }
        public AiRemote Remote { get; private set; } = null!;

        Rig(string[] ids, Action<string>? prepare)
        {
            Cfg = new Config(dir["home"], dir["pool"]) { PoolPassword = "pw", OllamaEnabled = true, OllamaModel = "qwen3:8b" };
            store = new Store(Cfg.DbPath, Cfg.PoolDir);
            foreach (string id in ids) Fake.InstalledAt(FakeAgents.BinaryOf(id), FakeAgents.WriteCli(Bin, id));
            prepare?.Invoke(Bin);
            // Signed out as far as the library knows (its last try said so): the state a Sign in button is for.
            var settings = AiSettings.Load(Cfg.Home);
            foreach (string id in ids) settings.Tests[id] = "Not logged in · Please run /login";
            settings.ByJob["notes"] = new AiChoice(ids[0]); // its "not signed in" card shows while it writes the notes
            settings.Save(Cfg.Home);
            Ai = new AiJobs(Cfg.Home)
            {
                Checks = Fake.Build(), SignInEvery = TimeSpan.FromMilliseconds(100), SignInPatience = TimeSpan.FromSeconds(60),
            };
            var builder = WebApplication.CreateSlimBuilder();
            Microsoft.AspNetCore.Hosting.WebHostBuilderKestrelExtensions.ConfigureKestrel(builder.WebHost, k => k.Listen(IPAddress.Loopback, 0));
            app = LibraryWeb.Build(builder, Cfg, store, new Pipeline(Cfg, store, log: _ => { }), new LibraryWebOptions
            {
                ListModels = _ => Task.FromResult<List<(string, double)>?>(null), Tailscale = () => new TailscaleInfo(),
                Latest = _ => Task.FromResult<Release?>(null), Ai = Ai,
            });
        }

        public static async Task<Rig> StartAsync(string[] ids, Action<string>? prepare = null)
        {
            var rig = new Rig(ids, prepare);
            await rig.app.StartAsync();
            var http = new HttpClient { BaseAddress = new Uri(rig.app.Urls.Single()), Timeout = TimeSpan.FromSeconds(60) };
            rig.Remote = new AiRemote(http.BaseAddress!.ToString(), "pw", http);
            return rig;
        }

        public async Task<EngineInfo> RowAsync(string id) => (await Remote.EnginesAsync())!.Engines.Single(e => e.Id == id);

        /// <summary>Waits on the real condition (the library's own look at the sign-in), never for a fixed time; a wait
        /// that ends unmet says where the sign-in was.</summary>
        public async Task<EngineInfo> UntilAsync(string id, Func<EngineInfo, bool> condition)
        {
            EngineInfo row = null!;
            if (await Waiting.UntilAsync(async () => condition(row = await RowAsync(id)), 30)) return row;
            throw new TimeoutException($"{id}'s row stayed {row.State}, sign-in \"{row.SignIn}\", {FakeAgents.Logins(Bin)} sign-in(s) started");
        }

        public async ValueTask DisposeAsync()
        {
            Ai.SignIns.Dispose();
            await app.DisposeAsync();
            store.Dispose();
            dir.Dispose();
        }
    }

    [Fact]
    public async Task Sign_in_starts_the_providers_own_hidden_sign_in_once_and_the_row_turns_ready_by_itself()
    {
        await using var rig = await Rig.StartAsync(["claude"], bin => File.WriteAllText(Path.Combine(bin, "login-delay"), "120"));
        Assert.Equal("not_signed_in", (await rig.RowAsync("claude")).State);

        var said = await rig.Remote.SignInAsync("claude");

        // Plain words, no terminal asked for, and the row says it waits.
        Assert.Equal("Finish signing in to Claude in the browser page that just opened.", said!.Said);
        Assert.Equal(0, rig.Fake.SignInCalls);
        var waiting = await rig.RowAsync("claude");
        Assert.Equal(("not_signed_in", SignInPhase.Waiting), (waiting.State, waiting.SignIn));
        Assert.Equal(SignInPhase.Waiting, said.Overview!.Problems.Single(p => p.Kind == "not_signed_in").SignIn);
        await rig.UntilAsync("claude", _ => FakeAgents.Logins(rig.Bin) == 1);

        // A second press doesn't start a second sign-in.
        await rig.Remote.SignInAsync("claude");
        await rig.Remote.SignInAsync("claude");
        Assert.Equal(1, FakeAgents.Logins(rig.Bin));

        // The student finishes in the browser: the AI's own status says so, and nobody presses anything. Only after the
        // library's first look said signed out, as a real one does long before a student is done: a status that already
        // says signed in at that first look waits for the sign-in to end by itself, which this one doesn't for 120 s.
        await rig.UntilAsync("claude", _ => FakeAgents.SignedOutLooks(rig.Bin) >= 1);
        File.WriteAllText(Path.Combine(rig.Bin, "signed-in"), "");
        var ready = await rig.UntilAsync("claude", e => e.State == "ready");
        Assert.Equal("", ready.SignIn);
        Assert.DoesNotContain((await rig.Remote.EnginesAsync())!.Problems, p => p.Kind == "not_signed_in");
        Assert.Equal("works", AiSettings.Load(rig.Cfg.Home).Tests["claude"]);
        Assert.Equal(0, rig.Fake.SignInCalls);
    }

    [Fact]
    public async Task A_status_that_already_says_signed_in_waits_for_the_sign_in_to_end_by_itself()
    {
        // Claude Code says signed in, but its maker no longer takes that sign-in (the last lecture said so).
        await using var rig = await Rig.StartAsync(["claude"], bin =>
        {
            File.WriteAllText(Path.Combine(bin, "signed-in"), "");
            File.WriteAllText(Path.Combine(bin, "login-delay"), "3");
        });

        await rig.Remote.SignInAsync("claude");

        // Ready only once the sign-in it started has finished by itself, not at the first look at the status.
        var ready = await rig.UntilAsync("claude", e => e.State == "ready");
        Assert.True(File.Exists(Path.Combine(rig.Bin, "login-done")), "ready before the sign-in ended");
        Assert.Equal("", ready.SignIn);
        Assert.Equal(1, FakeAgents.Logins(rig.Bin));
    }

    [Fact]
    public async Task A_sign_in_that_stops_at_once_is_stuck_with_no_terminal_and_Try_again_starts_it_afresh()
    {
        await using var rig = await Rig.StartAsync(["claude"], bin => File.WriteAllText(Path.Combine(bin, "login-fails"), ""));

        await rig.Remote.SignInAsync("claude");
        var stuck = await rig.UntilAsync("claude", e => e.SignIn == SignInPhase.Stuck);
        Assert.Equal("not_signed_in", stuck.State);

        // Try again: another hidden sign-in, which waits this time. No terminal at any point.
        File.Delete(Path.Combine(rig.Bin, "login-fails"));
        File.WriteAllText(Path.Combine(rig.Bin, "login-delay"), "120");
        await rig.Remote.SignInAsync("claude");
        await rig.UntilAsync("claude", e => e.SignIn == SignInPhase.Waiting && FakeAgents.Logins(rig.Bin) == 2);
        Assert.Equal(SignInPhase.Waiting, (await rig.RowAsync("claude")).SignIn);
        Assert.Equal(0, rig.Fake.SignInCalls);
    }

    [Fact]
    public async Task A_sign_in_its_maker_turns_away_for_the_country_says_so_and_Try_again_starts_it_afresh()
    {
        await using var rig = await Rig.StartAsync(["gemini"], bin => File.WriteAllText(Path.Combine(bin, "region"), ""));

        await rig.Remote.SignInAsync("gemini");
        var refused = await rig.UntilAsync("gemini", e => e.SignIn == SignInPhase.Region);
        Assert.Equal("not_signed_in", refused.State);
        Assert.Equal(0, rig.Fake.SignInCalls);

        // The country is served after all (or the student is somewhere else): pressing Sign in again starts afresh.
        File.Delete(Path.Combine(rig.Bin, "region"));
        File.WriteAllText(Path.Combine(rig.Bin, "login-delay"), "120");
        await rig.Remote.SignInAsync("gemini");
        await rig.UntilAsync("gemini", e => e.SignIn == SignInPhase.Waiting);
    }

    [Fact]
    public async Task A_sign_in_that_waits_longer_than_the_library_does_gives_up_and_Try_again_goes_on()
    {
        await using var rig = await Rig.StartAsync(["claude"], bin => File.WriteAllText(Path.Combine(bin, "login-delay"), "120"));
        var ai = new AiJobs(rig.Cfg.Home)
        {
            Checks = rig.Fake.Build(), SignInEvery = TimeSpan.FromMilliseconds(50), SignInPatience = TimeSpan.FromMilliseconds(800),
        };
        using var signIns = ai.SignIns;
        signIns.Start("claude");
        Assert.Equal(SignInPhase.Waiting, signIns.State("claude"));
        Assert.True(await Waiting.UntilAsync(() => signIns.State("claude") == SignInPhase.GaveUp, 30), "never gave up");

        // Pressed again, it's a new sign-in that waits.
        signIns.Start("claude");
        Assert.Equal(SignInPhase.Waiting, signIns.State("claude"));
    }

    [Fact]
    public void An_AI_just_installed_and_never_tried_reads_as_signing_in_while_its_sign_in_waits()
    {
        using var dir = new TempDir();
        var fake = new FakeChecks().InstalledAt("claude", FakeAgents.WriteCli(dir["bin"], "claude"));
        File.WriteAllText(dir["bin/login-delay"], "120");
        using var signIns = new AiSignIns(fake.Build(), _ => { });
        var fresh = new AiOverview([new EngineInfo("claude", "Claude", "unchecked") { Installed = true }], "claude", "claude", false, []);
        Assert.Equal("unchecked", signIns.Annotate(fresh).Engines.Single().State);

        signIns.Start("claude");
        var row = signIns.Annotate(fresh).Engines.Single();
        Assert.Equal(("not_signed_in", SignInPhase.Waiting), (row.State, row.SignIn));
    }

    [Fact]
    public async Task An_AI_that_isnt_installed_says_so_and_opens_nothing()
    {
        await using var rig = await Rig.StartAsync(["claude"]);
        var refused = await Assert.ThrowsAsync<LibraryRefusedException>(() => rig.Remote.SignInAsync("codex"));
        Assert.Equal(409, refused.Status);
        Assert.Contains("isn't installed", refused.Message);
        Assert.Equal(0, rig.Fake.SignInCalls);
    }
}
