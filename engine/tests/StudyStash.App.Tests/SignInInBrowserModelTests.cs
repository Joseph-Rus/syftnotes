using Avalonia.Headless.XUnit;
using StudyStash.App.ViewModels;
using StudyStash.Core.Ai;

namespace StudyStash.App.Tests;

/// <summary>
/// What a Sign in button's row and card do once the library signs in hidden (<see cref="SignInPhase"/>): they say they
/// wait for the browser, turn ready by themselves when the library says so, say plainly when it gave up, and only offer a
/// terminal when the hidden sign-in couldn't start. The library is scripted (<see cref="FakeAiLibrary"/>): no CLI, account or
/// browser is ever reached.
/// </summary>
public class SignInInBrowserModelTests
{
    static AiOverview Claude(string state, string signIn = "", bool problem = true) => AiTestData.MixedOverview() with
    {
        Engines = [.. AiTestData.MixedOverview().Engines.Select(e => e.Id == "claude" ? e with { State = state, SignIn = signIn } : e)],
        Problems = problem && state == "not_signed_in" ? [new AiProblemInfo("not_signed_in:claude", "not_signed_in", "claude", "Claude") { SignIn = signIn }] : [],
    };

    static async Task Until(Func<bool> done)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        while (!done()) await Task.Delay(20, cts.Token);
    }

    [AvaloniaFact]
    public async Task The_row_waits_for_the_browser_and_turns_ready_by_itself_with_nothing_to_press()
    {
        var lib = new FakeAiLibrary { Overview = Claude("not_signed_in") };
        lib.OnSignIn = _ => new AiSaid("Finish signing in to Claude in the browser page that just opened.", lib.Overview = Claude("not_signed_in", SignInPhase.Waiting));
        var model = new AiEnginesModel(lib) { SignInEvery = TimeSpan.FromMilliseconds(20) };
        await model.Load();
        AiEngineRow Row() => model.Engines.Single(r => r.Id == "claude");
        Assert.Equal("Sign in", Row().ActionWords);

        await Row().ActCommand.ExecuteAsync(null);

        // Waiting: its words, a small link to open the page again, and no button (and no Check to press).
        Assert.Equal(AiWords.SignInWaiting, Row().About);
        Assert.Equal("Open the page again", Row().LinkWords);
        Assert.False(Row().ShowPill);
        Assert.Equal("Finish signing in to Claude in the browser page that just opened.", model.Say);

        // The library's own look at the sign-in says it's done: the row is ready, by itself.
        lib.Overview = Claude("ready");
        await Until(() => Row().State == "ready");
        Assert.Equal("Claude is signed in.", model.Say);
        Assert.False(Row().HasLink);
        Assert.DoesNotContain(lib.Calls, c => c.StartsWith("check", StringComparison.Ordinal));
    }

    [AvaloniaFact]
    public async Task Leaving_the_page_and_coming_back_to_a_sign_in_still_waiting_carries_on()
    {
        var lib = new FakeAiLibrary { Overview = Claude("not_signed_in", SignInPhase.Waiting) };

        var back = new AiEnginesModel(lib) { SignInEvery = TimeSpan.FromMilliseconds(20) }; // a page opened afresh
        await back.Load();
        Assert.Equal("Open the page again", back.Engines.Single(r => r.Id == "claude").LinkWords);

        lib.Overview = Claude("ready");
        await Until(() => back.Engines.Single(r => r.Id == "claude").State == "ready");
    }

    [AvaloniaFact]
    public async Task Open_the_page_again_asks_the_library_and_never_a_terminal()
    {
        var lib = new FakeAiLibrary { Overview = Claude("not_signed_in", SignInPhase.Waiting) };
        lib.OnSignInAgain = _ => new AiSaid("Finish signing in to Claude in the browser page that just opened.", lib.Overview!);
        var model = new AiEnginesModel(lib) { SignInEvery = TimeSpan.FromHours(1) };
        await model.Load();

        await model.Engines.Single(r => r.Id == "claude").LinkCommand.ExecuteAsync(null);

        Assert.Contains("sign-in-again:claude", lib.Calls);
        Assert.DoesNotContain("sign-in-terminal:claude", lib.Calls);
    }

    [AvaloniaFact]
    public async Task A_sign_in_that_gave_up_says_so_with_Try_again_and_a_country_refusal_has_no_button()
    {
        var gaveUp = new AiEnginesModel(new FakeAiLibrary { Overview = Claude("not_signed_in", SignInPhase.GaveUp) });
        await gaveUp.Load();
        var row = gaveUp.Engines.Single(r => r.Id == "claude");
        Assert.Equal("It stopped waiting before you'd finished. Try again.", row.About);
        Assert.Equal("Try again", row.ActionWords);
        Assert.False(row.HasLink); // no terminal: the hidden sign-in worked, it just wasn't finished

        var region = new AiEnginesModel(new FakeAiLibrary { Overview = Claude("not_signed_in", SignInPhase.Region) });
        await region.Load();
        row = region.Engines.Single(r => r.Id == "claude");
        Assert.Equal("Claude isn't offered in your country yet.", row.About);
        Assert.False(row.ShowPill);
    }

    [AvaloniaFact]
    public async Task The_terminal_is_a_small_link_only_when_the_hidden_sign_in_couldnt_start()
    {
        var lib = new FakeAiLibrary { Overview = Claude("not_signed_in", SignInPhase.Stuck) };
        lib.OnSignInTerminal = _ => new AiSaid("Opened Terminal to sign in to Claude.", lib.Overview!);
        var model = new AiEnginesModel(lib) { SignInEvery = TimeSpan.FromHours(1) };
        await model.Load();
        var row = model.Engines.Single(r => r.Id == "claude");
        Assert.Equal("Try again", row.ActionWords);
        Assert.Equal("Sign in in Terminal instead", row.LinkWords);

        await row.LinkCommand.ExecuteAsync(null);

        Assert.Contains("sign-in-terminal:claude", lib.Calls);
        // While it waits there's no terminal on offer anywhere.
        var waiting = new AiEnginesModel(new FakeAiLibrary { Overview = Claude("not_signed_in", SignInPhase.Waiting) }) { SignInEvery = TimeSpan.FromHours(1) };
        await waiting.Load();
        Assert.DoesNotContain("Terminal", waiting.Engines.Single(r => r.Id == "claude").LinkWords, StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public async Task The_problem_card_follows_the_sign_in_and_goes_by_itself_when_it_works()
    {
        var lib = new FakeAiLibrary { Overview = Claude("not_signed_in") };
        lib.OnSignIn = _ => new AiSaid("Finish signing in to Claude in the browser page that just opened.", lib.Overview = Claude("not_signed_in", SignInPhase.Waiting));
        var card = new AiProblemsModel(lib) { SignInEvery = TimeSpan.FromMilliseconds(20) };
        await card.Load();
        Assert.Equal("Sign in", card.Problems.Single().PrimaryWords);

        await card.Problems.Single().PrimaryCommand!.ExecuteAsync(null);

        var waiting = card.Problems.Single();
        Assert.Equal("Finish signing in to Claude", waiting.Title);
        Assert.Equal(AiWords.SignInWaiting, waiting.Message);
        Assert.Equal("", waiting.PrimaryWords);
        Assert.Equal("Open the page again", waiting.SecondaryWords);

        lib.Overview = Claude("ready");
        await Until(() => card.Problems.Count == 0);
        Assert.DoesNotContain(lib.Calls, c => c.StartsWith("check", StringComparison.Ordinal));
    }

    [AvaloniaFact]
    public async Task The_problem_cards_gave_up_and_stuck_words()
    {
        var gaveUp = new AiProblemsModel(new FakeAiLibrary { Overview = Claude("not_signed_in", SignInPhase.GaveUp) });
        await gaveUp.Load();
        Assert.Equal(("Signing in to Claude stopped", "Try again", ""),
            (gaveUp.Problems.Single().Title, gaveUp.Problems.Single().PrimaryWords, gaveUp.Problems.Single().SecondaryWords));

        var stuck = new AiProblemsModel(new FakeAiLibrary { Overview = Claude("not_signed_in", SignInPhase.Stuck) }) { SignInEvery = TimeSpan.FromHours(1) };
        await stuck.Load();
        Assert.Equal(("Try again", "Sign in in Terminal instead"), (stuck.Problems.Single().PrimaryWords, stuck.Problems.Single().SecondaryWords));
    }

    [AvaloniaFact]
    public async Task A_sign_in_started_from_the_card_shows_on_the_row_below_and_the_other_way_round()
    {
        // Settings shows the problem cards and the AI rows on one page: whichever started the sign-in, both follow it.
        var lib = new FakeAiLibrary { Overview = Claude("not_signed_in") };
        lib.OnSignIn = _ => new AiSaid("Finish signing in to Claude in the browser page that just opened.", lib.Overview = Claude("not_signed_in", SignInPhase.Waiting));
        var rows = new AiEnginesModel(lib) { SignInEvery = TimeSpan.FromMilliseconds(20) };
        var card = new AiProblemsModel(lib) { SignInEvery = TimeSpan.FromMilliseconds(20) };
        rows.ShareWith(card);
        await rows.Load();
        await card.Load();
        AiEngineRow Row() => rows.Engines.Single(r => r.Id == "claude");

        // From the card: the row says it waits too, then turns ready with the card gone, nothing pressed.
        await card.Problems.Single().PrimaryCommand!.ExecuteAsync(null);
        Assert.Equal(AiWords.SignInWaiting, Row().About);
        lib.Overview = Claude("ready");
        await Until(() => card.Problems.Count == 0 && Row().State == "ready");

        // From the row: the card follows.
        lib.Overview = Claude("not_signed_in");
        await rows.Load();
        await card.Load();
        await Row().ActCommand.ExecuteAsync(null);
        Assert.Equal("Finish signing in to Claude", card.Problems.Single().Title);
        lib.Overview = Claude("ready");
        await Until(() => card.Problems.Count == 0 && Row().State == "ready");
    }

    [AvaloniaFact]
    public async Task The_line_under_the_list_matches_the_row_once_a_sign_in_stopped()
    {
        // Pressing Sign in answers "Finish signing in…"; when the sign-in then couldn't start, or gave up, the line says so.
        foreach (var (phase, said) in new[]
        {
            (SignInPhase.Stuck, "Syft Notes couldn't open the sign-in page for Claude. Try again."),
            (SignInPhase.GaveUp, "Signing in to Claude stopped before you'd finished. Try again."),
            (SignInPhase.Region, "Claude isn't offered in your country yet."),
        })
        {
            var lib = new FakeAiLibrary { Overview = Claude("not_signed_in") };
            lib.OnSignIn = _ => new AiSaid("Finish signing in to Claude in the browser page that just opened.", lib.Overview = Claude("not_signed_in", phase));
            var model = new AiEnginesModel(lib) { SignInEvery = TimeSpan.FromHours(1) };
            await model.Load();
            await model.Engines.Single(r => r.Id == "claude").ActCommand.ExecuteAsync(null);
            Assert.Equal(said, model.Say);
        }

        // Still waiting: the press's own words stand.
        var waiting = new FakeAiLibrary { Overview = Claude("not_signed_in") };
        waiting.OnSignIn = _ => new AiSaid("Finish signing in to Claude in the browser page that just opened.", waiting.Overview = Claude("not_signed_in", SignInPhase.Waiting));
        var m = new AiEnginesModel(waiting) { SignInEvery = TimeSpan.FromHours(1) };
        await m.Load();
        await m.Engines.Single(r => r.Id == "claude").ActCommand.ExecuteAsync(null);
        Assert.Equal("Finish signing in to Claude in the browser page that just opened.", m.Say);
    }
}
