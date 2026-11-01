using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using StudyStash.Core;
using StudyStash.Core.Ai;

namespace StudyStash.App.ViewModels;

/// <summary>One AI problem card (design 18): a caption above it, an icon and colour (Mac) or a severity (Windows'
/// InfoBar), a title and message already in the design's words, and up to two actions plus a close. Built once by
/// <see cref="AiProblemsModel"/> — the view just shows it.</summary>
public sealed class AiProblem
{
    public required string Id { get; init; }
    public required string Kind { get; init; }
    public required string Caption { get; init; }
    public required string Title { get; init; }
    public required string Message { get; init; }
    public required string Icon { get; init; }
    /// <summary>The Mac icon's colour token (Accent/Warn/Fg2); <see cref="IsAccent"/>/<see cref="IsWarn"/> are the
    /// same thing as the style classes a Mac card's icon actually binds.</summary>
    public required string ColorKey { get; init; }
    public bool IsAccent => ColorKey == "Accent";
    public bool IsWarn => ColorKey == "Warn";
    /// <summary>The Windows InfoBar's severity (err/warn/info) — also the style class its card and glyph bind.</summary>
    public required string Severity { get; init; }
    public bool Err => Severity == "err";
    public bool Warn => Severity == "warn";
    public bool Info => Severity == "info";
    /// <summary>The Windows InfoBar glyph's letter: ✕ err, ! warn, i info.</summary>
    public string Glyph => Severity switch { "err" => "✕", "warn" => "!", _ => "i" };
    /// <summary>Usage limit's "Dismiss" is a plain, no-fill button, unlike every other kind's single action
    /// (Start Ollama, Sign in, Download, Try again), which is a plain Raised pill.</summary>
    public bool PrimaryIsPlain => Kind == "usage_limit";
    /// <summary>Only an access request's "Allow" is the tinted, primary pill.</summary>
    public bool PrimaryIsAccent => Kind == "access_request";
    /// <summary>Windows draws no separate button for "Dismiss" (usage limit) or the button-less kinds (fell back) —
    /// its close × already covers those.</summary>
    public bool HasWinPrimaryButton => PrimaryWords.Length > 0 && !PrimaryIsPlain;
    public string PrimaryWords { get; init; } = "";
    public IAsyncRelayCommand? PrimaryCommand { get; internal set; }
    public string SecondaryWords { get; init; } = "";
    public IAsyncRelayCommand? SecondaryCommand { get; internal set; }
    /// <summary>Every card can be closed (Windows' InfoBar × ); Mac only ever draws that as its own primary action
    /// (Usage limit's "Dismiss"), so the Mac view never needs this directly.</summary>
    public bool CanClose { get; init; } = true;
    public IAsyncRelayCommand? CloseCommand { get; internal set; }
}

/// <summary>
/// Every AI problem banner (design 18): the library's own (engine offline, not signed in, model missing, a usage
/// limit) plus the app's (a fell-back answer, the library not answering, a rewrite that failed, an access request —
/// this last one has no server source yet: nothing asks for approval today, but the shape is here for when
/// something does). Reads and drives one library's AI (<see cref="IAiLibrary"/>); the view is just this list.
/// </summary>
public sealed class AiProblemsModel
{
    readonly IAiLibrary library;
    readonly SignInWatch watch;
    string shown = "";

    /// <summary>Opens a page in the browser (Get help); null in tests.</summary>
    public Action<string>? OpenUrl { get; set; }

    public AiProblemsModel(IAiLibrary library)
    {
        this.library = library;
        watch = new SignInWatch(library, SignInMovedAsync);
    }

    /// <summary>Said whenever these cards learn the AIs' state from the library (a sign-in's progress among it), so the
    /// rows on the same page show it too; <see cref="Adopt"/> is the other direction.</summary>
    public event Action<AiOverview>? Heard;

    /// <summary>Takes the state the AI rows learned, so a sign-in started from a row shows on its card at once.</summary>
    public void Adopt(AiOverview overview)
    {
        if (SignInWatch.Signature(overview) == shown) return;
        shown = SignInWatch.Signature(overview);
        Apply(overview.Problems);
        watch.Begin(overview);
    }

    /// <summary>How often a sign-in that's going is looked at; a test sets it small.</summary>
    public TimeSpan SignInEvery
    {
        get => watch.Every;
        set => watch.Every = value;
    }

    /// <summary>Whether the library is on this computer (one computer does everything): its cards say "this computer"
    /// rather than "your library".</summary>
    public bool Here { get; init; }

    public ObservableCollection<AiProblem> Problems { get; } = [];

    /// <summary>A sign-in going from a card: its card turns into "Finish signing in…", and goes by itself once the AI
    /// says it's signed in (nothing to press).</summary>
    Task SignInMovedAsync(AiOverview now)
    {
        if (SignInWatch.Signature(now) == shown) return Task.CompletedTask;
        shown = SignInWatch.Signature(now);
        Apply(now.Problems);
        Heard?.Invoke(now);
        return Task.CompletedTask;
    }

    public async Task Load()
    {
        AiOverview? overview;
        try
        {
            overview = await library.EnginesAsync();
        }
        catch
        {
            return;
        }
        if (overview is null) return;
        shown = SignInWatch.Signature(overview);
        Apply(overview.Problems);
        watch.Begin(overview);
        Heard?.Invoke(overview);
    }

    void Apply(List<AiProblemInfo> infos)
    {
        var appOnly = Problems.Where(p => p.Kind is "fell_back" or "library_offline" or "rewrite_failed" or "access_request").ToList();
        Problems.Clear();
        foreach (var p in infos) Problems.Add(Build(p));
        foreach (var p in appOnly) Problems.Add(p);
    }

    AiProblem Build(AiProblemInfo p)
    {
        string fallbackName = Engines.Name(p.FallbackTo.Length > 0 ? p.FallbackTo : "ollama");
        // A sign-in going from this card says where it is, in the card's own words.
        string signIn = p.Kind == "not_signed_in" ? p.SignIn : "";
        var problem = new AiProblem
        {
            Id = p.Id,
            Kind = p.Kind,
            Caption = AiWords.ProblemCaption(p.Kind),
            Title = signIn.Length > 0 ? AiWords.SignInTitle(p.EngineName, signIn) : AiWords.ProblemTitle(p.Kind, p.EngineName, fallbackName, Here),
            // A sign-in with no stand-in says so, rather than naming Ollama, which isn't used.
            Message = signIn.Length > 0 ? AiWords.SignInAbout(p.EngineName, signIn)
                : AiWords.ProblemMessage(p.Kind, p.EngineName, p.Kind == "not_signed_in" && p.FallbackTo.Length == 0 ? "" : fallbackName,
                    p.Until, p.SizeGb, "", Here),
            Icon = AiWords.ProblemIcon(p.Kind),
            ColorKey = AiWords.ProblemColorKey(p.Kind),
            Severity = AiWords.ProblemSeverity(p.Kind),
            PrimaryWords = signIn.Length > 0 ? AiWords.SignInButton(signIn)
                : p.Kind == "not_signed_in" && signingIn.Contains(p.Engine) ? "Check" : AiWords.ProblemAction(p.Kind),
            SecondaryWords = AiWords.SignInLink(signIn),
        };
        problem.PrimaryCommand = new AsyncRelayCommand(() => Act(p));
        if (signIn.Length > 0) problem.SecondaryCommand = new AsyncRelayCommand(() => ActOnLink(p, signIn));
        problem.CloseCommand = new AsyncRelayCommand(() => CloseServerAsync(p.Id));
        return problem;
    }

    /// <summary>The small link on a sign-in: the page again while it waits, or Syft Notes' help page once it couldn't
    /// start.</summary>
    async Task ActOnLink(AiProblemInfo p, string signIn)
    {
        if (signIn == SignInPhase.Stuck)
        {
            OpenUrl?.Invoke(AiWords.HelpPage);
            return;
        }
        try
        {
            var said = await library.SignInAgainAsync(p.Engine);
            if (said?.Overview is not null) Take(said.Overview);
        }
        catch
        {
            // The card stays: its link can be pressed again.
        }
    }

    /// <summary>The overview an action answered: its cards, and the watch for a sign-in it started.</summary>
    void Take(AiOverview overview)
    {
        shown = SignInWatch.Signature(overview);
        Apply(overview.Problems);
        watch.Begin(overview);
        Heard?.Invoke(overview);
    }

    async Task Act(AiProblemInfo p)
    {
        try
        {
            AiSaid? said = p.Kind switch
            {
                "engine_offline" => await library.StartAsync(p.Engine),
                // Signed in since Sign in was pressed: Check, and the card goes (Sign in comes back if it's still out).
                "not_signed_in" when signingIn.Remove(p.Engine) => await library.CheckAsync(p.Engine),
                "not_signed_in" => await SignInAsync(p.Engine),
                "model_missing" => await library.DownloadAsync(p.Engine),
                "usage_limit" => await Dismissed(p.Id),
                _ => null,
            };
            if (said?.Overview is not null) Take(said.Overview);
        }
        catch (LibraryRefusedException)
        {
            // The row stays; its own action can be tried again (Check, once Sign in has said where to sign in).
            if (signingIn.Contains(p.Engine)) await Load();
        }
        catch
        {
            // Offline: nothing to update here — the pane that owns "Offline" words shows that.
        }
    }

    /// <summary>AIs whose Sign in was pressed on their card: the card's button is Check until it's pressed.</summary>
    readonly HashSet<string> signingIn = [];

    async Task<AiSaid?> SignInAsync(string engine)
    {
        signingIn.Add(engine);
        var said = await library.SignInAsync(engine);
        // A library that signs in hidden says so on the card; one that doesn't leaves the student to press Check.
        if (said?.Overview?.Problems.FirstOrDefault(x => x.Engine == engine && x.Kind == "not_signed_in") is { SignIn.Length: > 0 }
            || said?.Overview?.Engines.FirstOrDefault(x => x.Id == engine) is { SignIn.Length: > 0 }) signingIn.Remove(engine);
        return said;
    }

    async Task<AiSaid?> Dismissed(string id)
    {
        var overview = await library.DismissAsync(id);
        return overview is null ? null : new AiSaid("", overview);
    }

    async Task CloseServerAsync(string id)
    {
        try
        {
            var overview = await library.DismissAsync(id);
            if (overview is not null) { Apply(overview.Problems); return; }
        }
        catch
        {
            // Offline or an older library: still drop it from view — Load() will bring it back if it's still true.
        }
        if (Problems.FirstOrDefault(p => p.Id == id) is { } p) Problems.Remove(p);
    }

    AiProblem? Existing(string id) => Problems.FirstOrDefault(p => p.Id == id);

    void AddLocal(AiProblem problem)
    {
        if (Existing(problem.Id) is not null) return;
        Problems.Add(problem);
    }

    /// <summary>An answer that fell back to Ollama: no action, just says why and who really answered.</summary>
    public void AddFellBack(string id, string fellBackToName, string why) => AddLocal(new AiProblem
    {
        Id = id, Kind = "fell_back", Caption = AiWords.ProblemCaption("fell_back"),
        Title = AiWords.ProblemTitle("fell_back", fellBackToName, ""), Message = why,
        Icon = AiWords.ProblemIcon("fell_back"), ColorKey = AiWords.ProblemColorKey("fell_back"), Severity = AiWords.ProblemSeverity("fell_back"),
    });

    /// <summary>The library isn't answering at all: "Try again" re-runs whatever the host passes in.</summary>
    public void AddLibraryOffline(Func<Task> retry, string id = "library-offline") => AddLocal(new AiProblem
    {
        Id = id, Kind = "library_offline", Caption = AiWords.ProblemCaption("library_offline"),
        Title = AiWords.ProblemTitle("library_offline", "", ""), Message = AiWords.ProblemMessage("library_offline", "", "", "", 0, "", Here),
        Icon = AiWords.ProblemIcon("library_offline"), ColorKey = AiWords.ProblemColorKey("library_offline"), Severity = AiWords.ProblemSeverity("library_offline"),
        PrimaryWords = AiWords.ProblemAction("library_offline"),
    }.Also(p => p.PrimaryCommand = new AsyncRelayCommand(retry)));

    /// <summary>A rewrite that failed, so it's visible even after leaving that lecture's page: "Try again" re-runs
    /// it with the same engine.</summary>
    public void AddRewriteFailed(string id, string engineName, string why, Func<Task> retry) => AddLocal(new AiProblem
    {
        Id = id, Kind = "rewrite_failed", Caption = AiWords.ProblemCaption("rewrite_failed"),
        Title = AiWords.ProblemTitle("rewrite_failed", engineName, ""), Message = AiWords.ProblemMessage("rewrite_failed", engineName, "", "", 0, why),
        Icon = AiWords.ProblemIcon("rewrite_failed"), ColorKey = AiWords.ProblemColorKey("rewrite_failed"), Severity = AiWords.ProblemSeverity("rewrite_failed"),
        PrimaryWords = AiWords.ProblemAction("rewrite_failed"),
    }.Also(p => p.PrimaryCommand = new AsyncRelayCommand(retry)));

    /// <summary>A tool asking to read the library from another computer. Nothing calls this yet — no server sends
    /// one today — but the card is ready for when something does: Allow/Deny call the hooks the host supplies.</summary>
    public void AddAccessRequest(string id, string engineName, string from, Func<Task> allow, Func<Task> deny) => AddLocal(new AiProblem
    {
        Id = id, Kind = "access_request", Caption = AiWords.ProblemCaption("access_request"),
        Title = AiWords.ProblemTitle("access_request", engineName, ""), Message = AiWords.ProblemMessage("access_request", engineName, "", "", 0, from),
        Icon = AiWords.ProblemIcon("access_request"), ColorKey = AiWords.ProblemColorKey("access_request"), Severity = AiWords.ProblemSeverity("access_request"),
        PrimaryWords = "Allow", SecondaryWords = "Deny",
    }.Also(p => { p.PrimaryCommand = new AsyncRelayCommand(allow); p.SecondaryCommand = new AsyncRelayCommand(deny); }));

    public void Remove(string id)
    {
        if (Existing(id) is { } p) Problems.Remove(p);
    }
}

static class AiProblemExtensions
{
    /// <summary>A small fluent helper so an <see cref="AiProblem"/> literal can wire its commands inline.</summary>
    public static AiProblem Also(this AiProblem self, Action<AiProblem> then)
    {
        then(self);
        return self;
    }
}
