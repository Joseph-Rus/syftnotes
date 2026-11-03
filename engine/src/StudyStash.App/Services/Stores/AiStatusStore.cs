using StudyStash.App.Services.Kit;
using StudyStash.App.ViewModels;
using StudyStash.Core.Ai;

namespace StudyStash.App.Services.Stores;

/// <summary>Where an AI on the library stands, as the library says it (its <see cref="EngineInfo.State"/>).</summary>
public enum AiEngineState
{
    /// <summary>A state this app doesn't know yet (a newer library's): shown in its own words, nothing to press.</summary>
    Unknown,
    NotInstalled,
    /// <summary>Here, but nobody knows yet whether it's signed in.</summary>
    Unchecked,
    NotSignedIn,
    NotRunning,
    ModelMissing,
    Ready,
    /// <summary>A usage limit was reached.</summary>
    Limited,
    /// <summary>It didn't work last time.</summary>
    Failed,
}

/// <summary>What can be asked of the library for one AI.</summary>
public enum AiAction
{
    None,
    /// <summary>Gets it ready in one go (the free AI: installed, started, its model downloaded).</summary>
    SetUp,
    /// <summary>The AI's own sign-in, hidden: it opens the student's browser, and the AI turns ready by itself.</summary>
    SignIn,
    /// <summary>"Open the page again" while a sign-in waits.</summary>
    SignInAgain,
    Check,
    Start,
    Download,
}

/// <summary>Whether the library could be asked, as of the last time it was.</summary>
public enum Reach
{
    /// <summary>Not asked yet.</summary>
    Unknown,
    Ok,
    Offline,
    OlderLibrary,
}

/// <summary>The kinds of AI problem card the library sends (<see cref="AiProblemInfo.Kind"/>), and the app's own.</summary>
public static class AiProblemKind
{
    public const string EngineOffline = "engine_offline";
    public const string NotSignedIn = "not_signed_in";
    public const string ModelMissing = "model_missing";
    public const string UsageLimit = "usage_limit";
    public const string FellBack = "fell_back";
    public const string LibraryOffline = "library_offline";
    public const string RewriteFailed = "rewrite_failed";
    public const string AccessRequest = "access_request";
}

/// <summary>One AI as the store sees it: its name as students know it, where it stands, where its sign-in is, the one
/// thing the library can be asked to move it on, and its state in a student's words.</summary>
public sealed record AiEngineStatus(string Id, string Brand, AiEngineState State, string SignIn, AiAction Action, string Words)
{
    /// <summary>What the library said of it, whole.</summary>
    public required EngineInfo Engine { get; init; }
    /// <summary>A sign-in is going, waiting for the student in their browser.</summary>
    public bool SigningIn => State == AiEngineState.NotSignedIn && SignIn == SignInPhase.Waiting;
    /// <summary>That sign-in's page ends on a code the student pastes into the app (Gemini's Google page does).</summary>
    public bool WantsCode => SigningIn && Engine.PasteCode;
}

/// <summary>
/// The library's AI, as one store the whole app reads (<c>host.AiStatus</c>): Settings' AI rows and the problem cards above
/// them read the same <see cref="Current"/>, hear the same <see cref="Changed"/>, and press through the same
/// <see cref="ActAsync"/>, so a sign-in started from either shows on both. Built on <see cref="CanvasWatch"/>'s pattern:
/// one loop, which asks the library every <see cref="Every"/> while a sign-in waits for the student (or while something
/// holds a <see cref="Hurry"/>), and ends by itself when none does. Its waits go through <see cref="Poll"/>.
/// <para>The library's engine-state strings are read here and nowhere else (<see cref="StateOf(string)"/>).</para>
/// <para>Used from the UI thread: the loop runs where it was started, so <see cref="Changed"/> is raised there.</para>
/// </summary>
public sealed class AiStatusStore : IDisposable
{
    readonly Func<IAiLibrary> library;
    readonly TimeProvider clock;
    /// <summary>AIs whose Sign in was pressed on a library that doesn't sign in hidden (one from before that opens a
    /// terminal, or says where to sign in): nothing learns when the student is done, so their next action is Check.</summary>
    readonly HashSet<string> checkNext = [];
    Poller? poller;
    int hurrying;
    bool lost;

    public AiStatusStore(IAiLibrary library, TimeProvider? clock = null) : this(() => library, clock)
    {
    }

    /// <param name="library">The library's AI as it is now (its address can change while the app runs).</param>
    public AiStatusStore(Func<IAiLibrary> library, TimeProvider? clock = null)
    {
        this.library = library;
        this.clock = clock ?? TimeProvider.System;
    }

    /// <summary>The library's AI, for what isn't about where the AIs stand (the defaults, keys, Connect).</summary>
    public IAiLibrary Library => library();

    /// <summary>How often the library is asked while a sign-in waits; a test sets it small (or moves the clock).</summary>
    public TimeSpan Every { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>What the library last said of its AIs; null before it was first asked (or while it never answered).</summary>
    public AiOverview? Current { get; private set; }

    public Reach Reach { get; private set; }

    /// <summary>What a view shows of the AIs, to tell whether an answer changed anything worth drawing again.</summary>
    public string Signature { get; private set; } = "";

    /// <summary>Raised whenever <see cref="Current"/> is set from an answer (a refresh, an action's, the loop's), whether
    /// or not anything in it changed: a view that only redraws on a real change compares <see cref="Signature"/>.</summary>
    public event Action? Changed;

    /// <summary>How many times the loop has started (a test checks only one ran).</summary>
    public int Loops { get; private set; }

    /// <summary>The loop is asking the library now.</summary>
    public bool Running => poller is { Running: true };

    // --- reading ----------------------------------------------------------------------------------------------------

    /// <summary>The engine-state string as an <see cref="AiEngineState"/>: the only place the library's words are read.</summary>
    public static AiEngineState StateOf(string state) => state switch
    {
        "not_installed" => AiEngineState.NotInstalled,
        "unchecked" => AiEngineState.Unchecked,
        "not_signed_in" => AiEngineState.NotSignedIn,
        "not_running" => AiEngineState.NotRunning,
        "model_missing" => AiEngineState.ModelMissing,
        "ready" => AiEngineState.Ready,
        "limited" => AiEngineState.Limited,
        "failed" => AiEngineState.Failed,
        _ => AiEngineState.Unknown,
    };

    public static AiEngineState StateOf(EngineInfo engine) => StateOf(engine.State);

    /// <summary>The library's own string for a state (for an <see cref="EngineInfo"/> made here, as Connect makes one
    /// just installed read signed out).</summary>
    public static string Raw(AiEngineState state) => state switch
    {
        AiEngineState.NotInstalled => "not_installed",
        AiEngineState.Unchecked => "unchecked",
        AiEngineState.NotSignedIn => "not_signed_in",
        AiEngineState.NotRunning => "not_running",
        AiEngineState.ModelMissing => "model_missing",
        AiEngineState.Ready => "ready",
        AiEngineState.Limited => "limited",
        AiEngineState.Failed => "failed",
        _ => "",
    };

    /// <summary>Where a sign-in is on this AI: only while it's signed out ("" otherwise).</summary>
    public static string SignInOf(EngineInfo engine) => StateOf(engine) == AiEngineState.NotSignedIn ? engine.SignIn : "";

    /// <summary>Whether a sign-in the student can still finish is going on some AI: waiting for the browser. (One that
    /// stopped waits for Try again.)</summary>
    public static bool SignInGoing(AiOverview overview) =>
        overview.Engines.Any(e => StateOf(e) == AiEngineState.NotSignedIn && e.SignIn == SignInPhase.Waiting);

    /// <summary>What a view shows of the AIs, to tell whether an answer changed anything worth drawing again.</summary>
    public static string SignatureOf(AiOverview overview) =>
        string.Join('|', overview.Engines.Select(e => $"{e.Id}:{e.State}:{e.SignIn}:{e.PasteCode}")) + "#" + string.Join('|', overview.Problems.Select(p => $"{p.Id}:{p.SignIn}"));

    /// <summary>The AI whose sign-in waits for the code its page ends on, pasted into the app; null when none does.</summary>
    public static string? CodeWantedBy(AiOverview overview) =>
        overview.Engines.FirstOrDefault(e => StateOf(e) == AiEngineState.NotSignedIn && e.SignIn == SignInPhase.Waiting && e.PasteCode)?.Id;

    /// <summary>The AI whose sign-in waits for a pasted code now (<see cref="CodeWantedBy"/>); null when none does.</summary>
    public string? WantsCode { get; private set; }

    /// <summary>Raised when <see cref="WantsCode"/> changes: a sign-in started waiting for its code (the app shows the box
    /// for it, whichever button started it), or it isn't waiting any more.</summary>
    public event Action<string?>? WantsCodeChanged;

    /// <summary>One AI as the library last said it; null when it didn't mention it (or hasn't been asked).</summary>
    public AiEngineStatus? For(string id) => Current?.Engines.FirstOrDefault(e => e.Id == id) is { } e ? StatusOf(e) : null;

    public AiEngineStatus StatusOf(EngineInfo e)
    {
        var state = StateOf(e);
        return new AiEngineStatus(e.Id, AiWords.PlainName(e.Id, e.Name), state, SignInOf(e), NextAction(e.Id, state), AiWords.EngineStateWords(e.State))
        {
            Engine = e,
        };
    }

    /// <summary>The library action that moves an AI on from <paramref name="state"/>: Sign in (Check once a Sign in that
    /// isn't hidden was pressed), Start, Download, Set up, Check; none once it's ready.</summary>
    public AiAction NextAction(string id, AiEngineState state) => state switch
    {
        AiEngineState.NotInstalled => AiAction.SetUp,
        AiEngineState.NotSignedIn => checkNext.Contains(id) ? AiAction.Check : AiAction.SignIn,
        AiEngineState.NotRunning => AiAction.Start,
        AiEngineState.ModelMissing => AiAction.Download,
        AiEngineState.Unchecked => AiAction.Check,
        _ => AiAction.None,
    };

    /// <summary>Sign in was pressed for this AI where the library doesn't sign in hidden: Check is what's next.</summary>
    public bool ChecksNext(string id) => checkNext.Contains(id);

    // --- asking -----------------------------------------------------------------------------------------------------

    /// <summary>Asks the library once. An answer becomes <see cref="Current"/> (and <see cref="Changed"/>); a failure only
    /// moves <see cref="Reach"/>, and what's known stays.</summary>
    public async Task<Outcome<AiOverview>> RefreshAsync(CancellationToken ct = default)
    {
        var asked = await LibraryCall.Run(() => Library.EnginesAsync(), ct);
        if (asked.Value is { } overview) Take(overview);
        else Reach = asked.Failure!.Kind == FailureKind.OlderLibrary ? Reach.OlderLibrary : Reach.Offline;
        return asked;
    }

    /// <summary>An overview the library answered something else with (an action, the defaults, a key): it's what's known
    /// now. A sign-in going in it is watched until it ends.</summary>
    public void Take(AiOverview overview)
    {
        Current = overview;
        Reach = Reach.Ok;
        Signature = SignatureOf(overview);
        Changed?.Invoke();
        if (CodeWantedBy(overview) is var wanted && wanted != WantsCode)
        {
            WantsCode = wanted;
            WantsCodeChanged?.Invoke(wanted);
        }
        if (SignInGoing(overview)) Watch();
    }

    /// <summary>The code the student pasted from <paramref name="id"/>'s sign-in page, handed to its sign-in on the
    /// library, which then finishes by itself (the store follows it to ready). The code goes nowhere else.</summary>
    public async Task<Outcome<AiSaid>> SendCodeAsync(string id, string code, CancellationToken ct = default)
    {
        var ai = Library;
        var said = await LibraryCall.Run(() => ai.SendSignInCodeAsync(id, code), ct);
        if (said.Value?.Overview is { } now) Take(now);
        Watch();
        return said;
    }

    /// <summary>Asks the library to move one AI on. Its answer's overview is taken (so every view follows), and a sign-in
    /// it started is watched until the AI turns ready by itself.</summary>
    public async Task<Outcome<AiSaid>> ActAsync(string id, AiAction action, CancellationToken ct = default)
    {
        if (action == AiAction.SignIn) checkNext.Add(id);
        else if (action == AiAction.Check) checkNext.Remove(id);
        var ai = Library;
        var said = await LibraryCall.Run(() => action switch
        {
            AiAction.SetUp => ai.SetUpAsync(id),
            AiAction.SignIn => ai.SignInAsync(id),
            AiAction.SignInAgain => ai.SignInAgainAsync(id),
            AiAction.Check => ai.CheckAsync(id),
            AiAction.Start => ai.StartAsync(id),
            AiAction.Download => ai.DownloadAsync(id),
            _ => Task.FromResult<AiSaid?>(null),
        }, ct);
        // A library that signs in hidden says where the sign-in is: the AI turns ready by itself, no Check to press.
        if (action == AiAction.SignIn && said.Value?.Overview is { } o
            && (o.Engines.FirstOrDefault(e => e.Id == id) is { SignIn.Length: > 0 }
                || o.Problems.FirstOrDefault(p => p.Engine == id && p.Kind == AiProblemKind.NotSignedIn) is { SignIn.Length: > 0 }))
            checkNext.Remove(id);
        if (said.Value?.Overview is { } now) Take(now);
        return said;
    }

    /// <summary>Closes one of the library's problem cards (a usage limit's Dismiss, a card's ×).</summary>
    public async Task<Outcome<AiOverview>> DismissAsync(string problemId, CancellationToken ct = default)
    {
        var said = await LibraryCall.Run(() => Library.DismissAsync(problemId), ct);
        if (said.Value is { } overview) Take(overview);
        return said;
    }

    /// <summary>Connect on Claude's, ChatGPT's or Gemini's card (<see cref="AiConnect"/>): what it ends on is taken, so the
    /// problem cards follow the card, and a sign-in it left waiting is watched.</summary>
    public async Task<ConnectOutcome> ConnectAsync(AiConnect connector, string id, Action<ConnectProgress>? progress = null, CancellationToken ct = default)
    {
        checkNext.Remove(id);
        // Asked meanwhile, so a sign-in Connect starts that waits for a pasted code (Gemini's) gets its box.
        using var watching = Hurry();
        var outcome = await connector.ConnectAsync(id, progress, ct);
        if (outcome.Overview is { } overview) Take(overview);
        return outcome;
    }

    // --- the one loop -----------------------------------------------------------------------------------------------

    /// <summary>
    /// Something waits for the AIs to move (a screen waiting on a sign-in the library may not show yet): ask now and every
    /// <see cref="Every"/> until the returned handle is disposed, as well as while a sign-in is going.
    /// </summary>
    public IDisposable Hurry()
    {
        hurrying++;
        Watch();
        poller?.Wake();
        return new Hurried(this);
    }

    sealed class Hurried(AiStatusStore store) : IDisposable
    {
        bool done;

        public void Dispose()
        {
            if (done) return;
            done = true;
            store.hurrying--;
        }
    }

    /// <summary>Starts the loop, unless it's running.</summary>
    void Watch()
    {
        if (Running) return;
        Loops++;
        poller = Poll.Every(Every, TickAsync, KeepGoing, clock);
    }

    async Task TickAsync(CancellationToken ct)
    {
        var asked = await RefreshAsync(ct);
        // The library isn't answering (or is too old to say): the page says so itself, and the loop stops.
        lost = !asked.Ok;
    }

    bool KeepGoing() => hurrying > 0 || !lost && Current is { } now && SignInGoing(now);

    public void Dispose()
    {
        poller?.Dispose();
        poller = null;
    }
}
