using StudyStash.App.Services.Stores;
using StudyStash.App.ViewModels;
using StudyStash.Core.Ai;

namespace StudyStash.App.Tests;

/// <summary>
/// The one store of where the AIs stand (<see cref="AiStatusStore"/>), as Settings uses it: its AI rows and the problem
/// cards above them read the same store. The library is scripted (<see cref="FakeAiLibrary"/>) and time is moved on by
/// hand (<see cref="ManualClock"/>): no CLI, account or browser is reached, and nothing waits for real.
/// </summary>
public class AiStatusStoreTests
{
    static AiOverview Claude(string state, string signIn = "") => AiTestData.MixedOverview() with
    {
        Engines = [.. AiTestData.MixedOverview().Engines.Select(e => e.Id == "claude" ? e with { State = state, SignIn = signIn } : e)],
        Problems = state == "not_signed_in" ? [new AiProblemInfo("not_signed_in:claude", "not_signed_in", "claude", "Claude") { SignIn = signIn }] : [],
    };

    [Fact]
    public Task A_sign_in_from_the_card_turns_the_row_and_the_card_ready_after_one_look_by_one_loop() => Task.Run(async () =>
    {
        // Off the test's own synchronization context, so the store's loop runs as the clock fires it.
        var lib = new FakeAiLibrary { Overview = Claude("not_signed_in") };
        lib.OnSignIn = _ => new AiSaid("Finish signing in to Claude in the browser page that just opened.", lib.Overview = Claude("not_signed_in", SignInPhase.Waiting));
        var clock = new ManualClock();
        using var store = new AiStatusStore(lib, clock);
        using var rows = new AiEnginesModel(store) { Here = true };
        using var cards = new AiProblemsModel(store) { Here = true };
        await rows.Load(); // Settings opens Your AI: one look, and the cards hear it too
        AiEngineRow Row() => rows.Engines.Single(r => r.Id == "claude");
        Assert.Equal("Sign in", cards.Problems.Single().PrimaryWords);
        Assert.Equal("Needs attention", Row().StateWords);

        await cards.Problems.Single().PrimaryCommand!.ExecuteAsync(null);

        // Both wait for the browser, and one loop watches the sign-in.
        Assert.Equal("Finish signing in to Claude", cards.Problems.Single().Title);
        Assert.Equal(AiWords.SignInWaiting, Row().About);
        Assert.Equal((1, true), (store.Loops, store.Running));

        // The student finishes in the browser; the store looks once.
        lib.Overview = Claude("ready");
        int asked = lib.Calls.Count(c => c == "engines");
        clock.Advance(store.Every);

        Assert.Equal(asked + 1, lib.Calls.Count(c => c == "engines"));
        Assert.Empty(cards.Problems);
        Assert.Equal(("ready", "Connected"), (Row().State, Row().StateWords));
        Assert.Equal(AiEngineState.Ready, store.For("claude")!.State);
        // Nothing left to watch: the one loop has ended by itself, and nothing else asks.
        Assert.Equal((1, false), (store.Loops, store.Running));
        Assert.Equal(0, clock.Waiting);
        Assert.DoesNotContain(lib.Calls, c => c.StartsWith("check", StringComparison.Ordinal));
    }, TestContext.Current.CancellationToken);

    /// <summary>
    /// Gemini's sign-in waits for the code Google's page ends on, and the box for it was closed: pressing Sign in (or Open
    /// the page again) changes nothing on the library, so the store asks for the box again. Pressed for an AI that isn't
    /// waiting on a code, it doesn't.
    /// </summary>
    [Fact]
    public Task Sign_in_pressed_while_geminis_sign_in_waits_for_its_code_brings_the_box_back_and_not_for_another_AI() => Task.Run(async () =>
    {
        var waiting = AiTestData.MixedOverview() with
        {
            Engines = [.. AiTestData.MixedOverview().Engines.Select(e => e.Id == "gemini"
                ? e with { State = "not_signed_in", Installed = true, SignIn = SignInPhase.Waiting, PasteCode = true } : e)],
        };
        // Its sign-in is already going: the library answers with the same overview.
        var lib = new FakeAiLibrary { Overview = waiting };
        lib.OnSignIn = _ => new AiSaid(AiSignIns.StartedWords("gemini"), waiting);
        lib.OnSignInAgain = _ => new AiSaid(AiSignIns.StartedWords("gemini"), waiting);
        using var store = new AiStatusStore(lib, new ManualClock());
        var asked = new List<string?>();
        store.WantsCodeChanged += asked.Add;

        await store.RefreshAsync();
        Assert.Equal(["gemini"], asked); // the box opens
        Assert.Equal("gemini", store.WantsCode);

        // The box was closed; Sign in, then Open the page again: each asks for it again.
        await store.ActAsync("gemini", AiAction.SignIn);
        await store.ActAsync("gemini", AiAction.SignInAgain);
        Assert.Equal(["gemini", "gemini", "gemini"], asked);

        // Codex isn't waiting on a code: its Sign in doesn't bring Gemini's box up.
        await store.ActAsync("codex", AiAction.SignIn);
        Assert.Equal(3, asked.Count);
    }, TestContext.Current.CancellationToken);
}
