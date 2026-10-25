using StudyStash.Core.Ai;

namespace StudyStash.Core.Tests;

/// <summary>Claude signed out (its sign-in ran out) while it writes the notes: the lecture says why there are none and
/// what to do, Settings offers Sign in, and once Claude answers again the library writes the notes it missed.</summary>
public class SignInAgainTests
{
    static readonly string Transcript = string.Concat(Enumerable.Repeat(
        "Today we look at recursion: a function that calls itself, with a base case that stops it and a call stack that grows with every call. ", 30));

    [Fact]
    public async Task Notes_Claude_couldnt_write_while_signed_out_say_why_and_are_written_once_it_is_signed_in_again()
    {
        using var dir = new TempDir();
        var cfg = new Config(dir["home"], dir["pool"]) { OllamaEnabled = true, OllamaModel = "qwen3:8b", Classes = [new ClassDef("CS 101")] };
        Directory.CreateDirectory(cfg.Home);
        var settings = new AiSettings { Fallback = false };
        settings.ByJob["notes"] = new AiChoice("claude");
        settings.Save(cfg.Home);
        using var store = new Store(cfg.DbPath, cfg.PoolDir);
        var claude = new ScriptedAi("claude", "Claude") { Script = [new("error", "Not logged in · Please run /login")] };
        var checks = new FakeChecks().Installed("claude").Build();
        var ai = new AiJobs(cfg.Home) { Providers = id => claude, Checks = checks };
        var pipeline = new Pipeline(cfg, store, (_, _, _) => Task.FromResult("""{"class_name": "CS 101", "confidence": 0.9, "lecture_title": "Recursion", "topics": []}"""),
            ai.SummarizeAsync, _ => { }, notesModel: () => ai.Describe("notes", cfg));
        // As the library wires it (Cli): signed in again, the lectures it missed are written.
        ai.SignedInAgain += id =>
        {
            if (store.RequeueSignedIn(Engines.Name(id)) > 0) pipeline.Wake();
        };

        store.Enqueue(new Meeting("lec-1") { Title = "Recursion", Date = "2026-10-14T09:00:00", Folder = "CS 101", Transcript = Transcript });
        Assert.Equal(1, await pipeline.RunPendingAsync());

        // Filed, with no notes, and the reason in the student's words where the notes would be (the app, the phone and
        // the library's pages all show this).
        var row = store.Get("lec-1")!;
        Assert.Equal(("CS 101", ""), (row.ClassName, row.SummaryMd ?? ""));
        Assert.Equal("Your notes weren't written because Claude needs you to sign in again. Sign in from Settings → Your AI, and they're written then.", row.Error);
        Assert.Equal("Claude", Engines.NeedsSignIn(row.Error!));
        // Settings says Claude isn't signed in and offers Sign in.
        var overview = await Engines.StatusAsync(AiSettings.Load(cfg.Home), cfg, checks);
        Assert.Equal("not_signed_in", overview.Engines.Single(e => e.Id == "claude").State);
        Assert.Contains(overview.Problems, p => p is { Kind: "not_signed_in", Engine: "claude" });

        // The student signs in and Settings checks Claude: the lecture is queued again, and gets its notes.
        claude.Script = [new("final", "ready")];
        Assert.True((await ai.TestAsync("claude")).Ok);
        Assert.Equal(Store.Queued, store.Get("lec-1")!.Status);
        claude.Script = [new("final", "## Recursion\n- A function that calls itself, until its base case.")];
        Assert.Equal(1, await pipeline.RunPendingAsync());
        row = store.Get("lec-1")!;
        Assert.Contains("A function that calls itself", row.SummaryMd);
        Assert.Equal("", row.Error ?? "");

        // Checking again (signed in all along) queues nothing more.
        claude.Script = [new("final", "ready")];
        Assert.True((await ai.TestAsync("claude")).Ok);
        Assert.Equal(Store.Done, store.Get("lec-1")!.Status);
    }

    [Theory]
    [InlineData("Not logged in · Please run /login", true)]
    [InlineData("Invalid API key · Please run /login", true)]
    [InlineData("OAuth token has expired. Please obtain a new token or refresh your existing token.", true)]
    [InlineData("API Error: 401 {\"type\":\"error\",\"error\":{\"type\":\"authentication_error\",\"message\":\"OAuth token has expired.\"}}", true)]
    // Claude Code 2.1.287 on, in -p (Anthropic's docs, errors: "OAuth token revoked or expired", "Login expired").
    [InlineData("Failed to authenticate: OAuth token revoked. Please log in again or contact your administrator.", true)]
    [InlineData("Failed to authenticate. API Error: 401 OAuth token has expired", true)]
    [InlineData("Failed to authenticate: OAuth session expired and could not be refreshed", true)]
    [InlineData("Invalid API key · Fix external API key", false)] // a key turned down: signing in doesn't fix it
    [InlineData("Claude AI usage limit reached, resets at 3pm", false)]
    [InlineData("Request timed out", false)]
    public void What_counts_as_needing_to_sign_in_again(string error, bool signedOut) =>
        Assert.Equal(signedOut, Engines.LooksSignedOut(error));

    const string RefreshBusy = "Failed to refresh OAuth token: another Claude Code process is refreshing it or exited mid-refresh. "
        + "This is usually transient; retry in a minute, and if it persists close other Claude Code processes or sign in again";

    static string Failed(string why) => new System.Text.Json.Nodes.JsonObject
    {
        ["type"] = "result", ["is_error"] = true, ["result"] = why, ["session_id"] = "s1",
    }.ToJsonString();

    /// <summary>Another Claude Code renewing the sign-in isn't Claude signed out: the run is tried again a little later
    /// and the notes are written, where the lecture used to say "Claude needs you to sign in again". Only when it keeps
    /// saying so for every try do its words stand (and read as signed out, as Anthropic's docs then say it is).</summary>
    [Fact]
    public async Task Claude_busy_renewing_its_sign_in_elsewhere_is_tried_again_not_called_signed_out()
    {
        using var dir = new TempDir();
        string exe = FakeAgents.WriteCli(dir["claude"], "claude");
        FakeAgents.Replay(dir["claude"], [Failed(RefreshBusy)], 1);
        FakeAgents.Replay(dir["claude"], [Answer("## Recursion")], 2);
        var r = await Claude(exe).CompleteAsync(new AiRequest("notes", dir.Path));
        Assert.True(r.Ok, r.Text);
        Assert.Equal("## Recursion", r.Text);
        Assert.Equal(2, FakeAgents.Turns(dir["claude"]));

        // Busy every time: three tries, then its own words.
        using var again = new TempDir();
        exe = FakeAgents.WriteCli(again["claude"], "claude");
        FakeAgents.Replay(again["claude"], [Failed(RefreshBusy)]);
        r = await Claude(exe).CompleteAsync(new AiRequest("notes", again.Path));
        Assert.False(r.Ok);
        Assert.Contains("another Claude Code process", r.Text);
        Assert.Equal(ClaudeProvider.RefreshTries, FakeAgents.Turns(again["claude"]));
    }

    /// <summary>A sign-in that "couldn't be refreshed" is tried once more as a fresh claude, alone: another Claude Code
    /// may just have renewed it. Only when that fails too is Claude signed out, and the lecture says so.</summary>
    [Fact]
    public async Task A_sign_in_that_couldnt_be_renewed_is_tried_once_more_alone_before_it_counts_as_signed_out()
    {
        const string Expired = "Failed to authenticate: OAuth session expired and could not be refreshed";
        using var dir = new TempDir();
        string exe = FakeAgents.WriteCli(dir["claude"], "claude");
        FakeAgents.Replay(dir["claude"], [Failed(Expired)], 1);
        FakeAgents.Replay(dir["claude"], [Answer("## Recursion")], 2);
        var gate = new SignInGate();
        var r = await Claude(exe, gate).CompleteAsync(new AiRequest("notes", dir.Path));
        Assert.True(r.Ok, r.Text);
        Assert.Equal(2, FakeAgents.Turns(dir["claude"]));
        Assert.True(gate.Warm); // it worked: the next runs start at once

        // Signed out for real: the fresh run fails too, and then the words stand, as signed out.
        using var gone = new TempDir();
        exe = FakeAgents.WriteCli(gone["claude"], "claude");
        FakeAgents.Replay(gone["claude"], [Failed(Expired)]);
        gate = new SignInGate();
        r = await Claude(exe, gate).CompleteAsync(new AiRequest("notes", gone.Path));
        Assert.False(r.Ok);
        Assert.True(Engines.LooksSignedOut(r.Text));
        Assert.Equal(2, FakeAgents.Turns(gone["claude"]));
        Assert.False(gate.Warm); // the next run signs in alone again
    }

    /// <summary>After a quiet spell the first Claude run signs in alone: a second started at the same moment waits
    /// until the first has begun to answer (its sign-in renewed), then runs. The one leading streams, so the wait ends
    /// as soon as Claude begins, not when it's done; the one that waited doesn't stream, as it didn't ask to.</summary>
    [Fact]
    public async Task After_a_quiet_spell_one_claude_signs_in_alone_and_the_next_waits_for_it()
    {
        using var dir = new TempDir();
        string bin = dir["claude"], exe = FakeAgents.WriteCli(bin, "claude");
        // Each run prints its first line, waits two seconds (as if renewing the sign-in), then begins to answer.
        FakeAgents.Replay(bin, [
            """{"type":"system","subtype":"init","session_id":"s1"}""",
            """{"type":"stream_event","event":{"type":"message_start","message":{}},"parent_tool_use_id":null}""",
            Answer("ready"),
        ]);
        File.WriteAllText(Path.Combine(bin, "sleep"), "2");
        var claude = Claude(exe, new SignInGate());

        var first = claude.CompleteAsync(new AiRequest("notes", dir.Path));
        await Until(() => FakeAgents.Turns(bin) == 1);
        var second = claude.CompleteAsync(new AiRequest("a question", dir.Path));
        await Task.Delay(700);
        Assert.Equal(1, FakeAgents.Turns(bin)); // the first is still signing in: the second hasn't started
        Assert.True((await first).Ok);
        Assert.True((await second).Ok);
        Assert.Equal(2, FakeAgents.Turns(bin));
        Assert.Contains("--include-partial-messages", FakeAgents.Argv(bin, 1));
        Assert.DoesNotContain("--include-partial-messages", FakeAgents.Argv(bin, 2));
    }

    /// <summary>The gate on its own: who leads, who waits, and when it goes cold again.</summary>
    [Fact]
    public async Task The_sign_in_gate_lets_one_lead_after_a_quiet_spell_or_a_turned_away_run()
    {
        var now = new DateTime(2026, 10, 24, 9, 0, 0, DateTimeKind.Utc);
        var gate = new SignInGate { Now = () => now };
        var lead = await gate.EnterAsync();
        Assert.True(lead.Leads);
        var second = gate.EnterAsync();
        var third = gate.EnterAsync();
        await Task.Delay(100);
        Assert.False(second.IsCompleted || third.IsCompleted); // waiting for the one signing in

        // It finished without getting past sign-in (turned away): one of the others leads, the last still waits.
        lead.Dispose();
        var winner = await Task.WhenAny(second, third);
        var next = await winner;
        Assert.True(next.Leads);
        var last = winner == second ? third : second;
        await Task.Delay(100);
        Assert.False(last.IsCompleted);

        // This one gets past sign-in: the last goes at once, without leading, and so does the next for five minutes.
        next.Passed();
        Assert.False((await last).Leads);
        Assert.False((await gate.EnterAsync()).Leads);
        now = now.AddMinutes(6);
        using (var after = await gate.EnterAsync()) Assert.True(after.Leads); // a quiet spell: lead again
        (await gate.EnterAsync()).Passed();
        Assert.True(gate.Warm);
        gate.Cold(); // a run was turned away
        using var once = await gate.EnterAsync();
        Assert.True(once.Leads);
    }

    static ClaudeProvider Claude(string exe, SignInGate? gate = null) =>
        new() { At = exe, RefreshWait = TimeSpan.Zero, SignedOutWait = TimeSpan.Zero, Gate = gate ?? new SignInGate() };

    static string Answer(string text) => new System.Text.Json.Nodes.JsonObject
    {
        ["type"] = "result", ["is_error"] = false, ["result"] = text, ["session_id"] = "s2",
    }.ToJsonString();

    static async Task Until(Func<bool> done)
    {
        var waited = System.Diagnostics.Stopwatch.StartNew();
        while (!done())
        {
            if (waited.Elapsed > TimeSpan.FromSeconds(20)) throw new TimeoutException("the fake claude didn't start");
            await Task.Delay(25);
        }
    }
}
