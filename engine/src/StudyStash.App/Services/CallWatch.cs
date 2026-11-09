using CommunityToolkit.Mvvm.Messaging;
using StudyStash.App.Platform;
using StudyStash.App.Services.Kit;

namespace StudyStash.App.Services;

public enum CallChange
{
    None,
    /// <summary>A call has gone on long enough to ask about taking notes.</summary>
    Ask,
    /// <summary>The call asked about is over.</summary>
    Ended,
}

/// <summary>
/// When a call is worth asking about, from what holds the microphone every few seconds. A call app has to hold it for
/// <see cref="AskAfter"/> (a browser, which could be anything, for <see cref="BrowserAskAfter"/>) before it's asked
/// about, so a microphone test or a voice message doesn't set it off. Each call is asked about once, never while Syft
/// Notes records, and it's over once its app has let go of the microphone for <see cref="EndedAfter"/>: muting, new
/// headphones or a breakout room isn't the end of it.
/// </summary>
public sealed class CallRules
{
    public static readonly TimeSpan AskAfter = TimeSpan.FromSeconds(15), BrowserAskAfter = TimeSpan.FromMinutes(1), EndedAfter = TimeSpan.FromSeconds(20);

    DateTimeOffset since, held;
    bool asked;

    /// <summary>The call on now (counted from when its app took the microphone), or null.</summary>
    public MicUser? Call { get; private set; }

    /// <summary>What <paramref name="holding"/> the microphone at <paramref name="now"/> means. <paramref name="quiet"/>:
    /// Syft Notes is recording (or isn't to ask), so a call that would be asked about now never is.</summary>
    public CallChange See(IReadOnlyList<MicUser> holding, bool quiet, DateTimeOffset now)
    {
        if (Call is { } call)
        {
            if (holding.Contains(call)) held = now;
            // Let go before it was asked about: it starts again from nothing if it comes back.
            else if (!asked) Call = null;
            else if (now - held >= EndedAfter)
            {
                Call = null;
                return CallChange.Ended;
            }
        }
        if (Call is null && holding.Count > 0)
        {
            // A call app before a browser.
            Call = holding.OrderBy(u => u.Browser).First();
            since = held = now;
            asked = false;
        }
        if (Call is not { } on || asked || now - since < (on.Browser ? BrowserAskAfter : AskAfter)) return CallChange.None;
        asked = true;
        return quiet ? CallChange.None : CallChange.Ask;
    }
}

/// <summary>
/// Notices a call on this computer (Zoom, Teams, Webex, or a browser holding the microphone), looking every
/// <see cref="Every"/> through <see cref="CallApps"/>, and says so on the app's messenger (<see cref="CallStarted"/>,
/// <see cref="CallEnded"/>) as <see cref="CallRules"/> decide. Whether Syft Notes is recording is the recorder's to say,
/// read at each look.
/// </summary>
public sealed class CallWatch(AppHost host, IMessenger messages, TimeProvider clock, AppStart start) : IDisposable
{
    public static readonly TimeSpan Every = TimeSpan.FromSeconds(4);

    readonly CallRules rules = new();
    Poller? poll;

    /// <summary>The call on now, or null.</summary>
    public MicUser? Call => rules.Call;

    /// <summary>Starts looking (once; never where the computer can't tell, or in a self-test).</summary>
    public void Start()
    {
        if (poll is not null || !CallApps.Supported || Desktop.SystemChangesOff) return;
        poll = Poll.Every(Every, async _ =>
        {
            if (Asks) Look(await Task.Run(CallApps.Holding));
        }, clock: clock);
    }

    /// <summary>Settings → Recording's switch is on, on a computer that records and is set up: nothing is read otherwise.</summary>
    bool Asks => host.Settings is { AskAtCalls: true, SetupDone: true, Role: not AppRole.Library };

    void Look(IReadOnlyList<MicUser> holding)
    {
        var call = rules.Call;
        switch (rules.See(holding, quiet: host.Recorder.Current is not null, clock.GetUtcNow()))
        {
            case CallChange.Ask when rules.Call is { } on:
                start.Log($"[calls] a call in {on.App}: asking to take notes");
                messages.Send(new CallStarted(on));
                break;
            case CallChange.Ended when call is { } was:
                start.Log($"[calls] the call in {was.App} ended");
                messages.Send(new CallEnded(was));
                break;
        }
    }

    public void Dispose() => poll?.Dispose();
}
