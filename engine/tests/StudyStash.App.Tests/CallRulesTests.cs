using StudyStash.App.Platform;
using StudyStash.App.Services;

namespace StudyStash.App.Tests;

/// <summary>When a call is asked about, and when it's over (<see cref="CallRules"/>), fed what holds the microphone every
/// 4 seconds, as <see cref="CallWatch"/> does.</summary>
public class CallRulesTests
{
    static readonly DateTimeOffset T0 = new(2026, 11, 9, 10, 0, 0, TimeSpan.Zero);
    static readonly MicUser Zoom = new("Zoom", false), Chrome = new("Chrome", true);

    /// <summary>Feeds the same reading every 4 seconds from <paramref name="from"/> to before <paramref name="to"/>, and
    /// says when each change came.</summary>
    static List<(double At, CallChange Change)> Feed(CallRules rules, double from, double to, MicUser[] holding, bool recording = false)
    {
        var seen = new List<(double, CallChange)>();
        for (double t = from; t < to; t += 4)
            if (rules.See(holding, recording, T0.AddSeconds(t)) is var c and not CallChange.None) seen.Add((t, c));
        return seen;
    }

    [Fact]
    public void A_call_is_asked_about_once_after_15_seconds_and_ends_20_seconds_after_it_lets_go()
    {
        var rules = new CallRules();
        // Zoom's microphone test: 8 seconds, then let go. Nothing, and the call that follows counts from its own start.
        Assert.Empty(Feed(rules, 0, 8, [Zoom]));
        Assert.Empty(Feed(rules, 8, 12, []));
        Assert.Equal([(28d, CallChange.Ask)], Feed(rules, 12, 40, [Zoom]));
        // Muted, or a breakout room: let go for 12 seconds and back. Still the same call: not asked again, not ended.
        Assert.Empty(Feed(rules, 40, 52, []));
        Assert.Empty(Feed(rules, 52, 200, [Zoom]));
        Assert.Equal(Zoom, rules.Call);
        // Hung up: over once it has let go for 20 seconds.
        Assert.Equal([(216d, CallChange.Ended)], Feed(rules, 200, 240, []));
        Assert.Null(rules.Call);
    }

    [Fact]
    public void A_browser_waits_a_minute_and_nothing_is_asked_while_recording()
    {
        var rules = new CallRules();
        Assert.Equal([(60d, CallChange.Ask)], Feed(rules, 0, 64, [Chrome]));
        Assert.Equal([(80d, CallChange.Ended)], Feed(rules, 64, 88, []));
        // A call that starts while Syft Notes records isn't asked about, even once the recording stops.
        Assert.Empty(Feed(rules, 100, 120, [Zoom], recording: true));
        Assert.Empty(Feed(rules, 120, 300, [Zoom]));
    }
}
