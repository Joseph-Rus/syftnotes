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
    [InlineData("Invalid API key · Fix external API key", false)] // a key turned down: signing in doesn't fix it
    [InlineData("Claude AI usage limit reached, resets at 3pm", false)]
    [InlineData("Request timed out", false)]
    public void What_counts_as_needing_to_sign_in_again(string error, bool signedOut) =>
        Assert.Equal(signedOut, Engines.LooksSignedOut(error));
}
