using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using StudyStash.App.Services;
using StudyStash.App.Services.Stores;
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
/// something does). Reads the library's AI from the one <see cref="AiStatusStore"/> (Settings' AI rows read the same
/// one, so a sign-in started from either shows on both) and keeps only its cards; the view is just this list.
/// </summary>
public sealed class AiProblemsModel : IDisposable
{
    readonly AiStatusStore store;
    /// <summary>Made here for a model given only a library (a test, a picture): disposed with it.</summary>
    readonly bool ownsStore;
    string shown = "";
    /// <summary>Above zero while this model's own call to the store is going: it draws the answer itself.</summary>
    int own;

    /// <summary>Opens a page in the browser (Get help); null in tests.</summary>
    public Action<string>? OpenUrl { get; set; }

    public AiProblemsModel(AiStatusStore store)
    {
        this.store = store;
        store.Changed += OnStoreChanged;
    }

    /// <summary>Over a store of its own (a test, a picture, a page with nothing else on it reading the AI).</summary>
    public AiProblemsModel(IAiLibrary library) : this(new AiStatusStore(library)) => ownsStore = true;

    /// <summary>How often a sign-in that's going is looked at; a test sets it small.</summary>
    public TimeSpan SignInEvery
    {
        get => store.Every;
        set => store.Every = value;
    }

    /// <summary>Whether the library is on this computer (one computer does everything): its cards say "this computer"
    /// rather than "your library".</summary>
    public bool Here { get; init; }

    public ObservableCollection<AiProblem> Problems { get; } = [];

    /// <summary>The store learned something (a sign-in's progress, or Settings' rows did something): the cards follow,
    /// a card for a sign-in that worked going by itself (nothing to press).</summary>
    void OnStoreChanged()
    {
        if (own > 0 || store.Current is not { } now || store.Signature == shown) return;
        Show(now);
    }

    void Show(AiOverview overview)
    {
        shown = AiStatusStore.SignatureOf(overview);
        Apply(overview.Problems);
    }

    async Task<T> Own<T>(Func<Task<T>> call)
    {
        own++;
        try
        {
            return await call();
        }
        finally
        {
            own--;
        }
    }

    public async Task Load()
    {
        var asked = await Own(() => store.RefreshAsync());
        if (asked.Value is { } overview) Show(overview);
    }

    void Apply(List<AiProblemInfo> infos)
    {
        var appOnly = Problems.Where(p => p.Kind is AiProblemKind.FellBack or AiProblemKind.LibraryOffline or AiProblemKind.RewriteFailed or AiProblemKind.AccessRequest).ToList();
        Problems.Clear();
        foreach (var p in infos) Problems.Add(Build(p));
        foreach (var p in appOnly) Problems.Add(p);
    }

    AiProblem Build(AiProblemInfo p)
    {
        string fallbackName = Engines.Name(p.FallbackTo.Length > 0 ? p.FallbackTo : "ollama");
        bool signedOut = p.Kind == AiProblemKind.NotSignedIn;
        // A sign-in going from this card says where it is, in the card's own words.
        string signIn = signedOut ? p.SignIn : "";
        var problem = new AiProblem
        {
            Id = p.Id,
            Kind = p.Kind,
            Caption = AiWords.ProblemCaption(p.Kind),
            Title = signIn.Length > 0 ? AiWords.SignInTitle(p.EngineName, signIn) : AiWords.ProblemTitle(p.Kind, p.EngineName, fallbackName, Here),
            // A sign-in with no stand-in says so, rather than naming Ollama, which isn't used.
            Message = signIn.Length > 0 ? AiWords.SignInAbout(p.EngineName, signIn)
                : AiWords.ProblemMessage(p.Kind, p.EngineName, signedOut && p.FallbackTo.Length == 0 ? "" : fallbackName,
                    p.Until, p.SizeGb, "", Here),
            Icon = AiWords.ProblemIcon(p.Kind),
            ColorKey = AiWords.ProblemColorKey(p.Kind),
            Severity = AiWords.ProblemSeverity(p.Kind),
            PrimaryWords = signIn.Length > 0 ? AiWords.SignInButton(signIn)
                : signedOut && store.ChecksNext(p.Engine) ? "Check" : AiWords.ProblemAction(p.Kind),
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
        // Not answering, or refused: the card stays, and its link can be pressed again.
        var said = await Own(() => store.ActAsync(p.Engine, AiAction.SignInAgain));
        if (said.Value?.Overview is { } overview) Show(overview);
    }

    async Task Act(AiProblemInfo p)
    {
        // Signed in since Sign in was pressed (on a library that doesn't sign in hidden): Check, and the card goes (Sign in
        // comes back if it's still out).
        var said = p.Kind switch
        {
            AiProblemKind.EngineOffline => await Own(() => store.ActAsync(p.Engine, AiAction.Start)),
            AiProblemKind.NotSignedIn => await Own(() => store.ActAsync(p.Engine, store.ChecksNext(p.Engine) ? AiAction.Check : AiAction.SignIn)),
            AiProblemKind.ModelMissing => await Own(() => store.ActAsync(p.Engine, AiAction.Download)),
            AiProblemKind.UsageLimit => await Dismissed(p.Id),
            _ => null,
        };
        if (said?.Value?.Overview is { } overview) Show(overview);
        // Refused: the card stays; its own action can be tried again (Check, once Sign in has said where to sign in).
        // Offline: nothing to update here — the pane that owns "Offline" words shows that.
        else if (said?.Failure?.Kind == FailureKind.Refused && store.ChecksNext(p.Engine)) await Load();
    }

    async Task<Outcome<AiSaid>> Dismissed(string id)
    {
        var dismissed = await Own(() => store.DismissAsync(id));
        return dismissed.Value is { } overview ? Outcome<AiSaid>.Answered(new AiSaid("", overview)) : Outcome<AiSaid>.Failed(dismissed.Failure!);
    }

    async Task CloseServerAsync(string id)
    {
        var dismissed = await Own(() => store.DismissAsync(id));
        if (dismissed.Value is { } overview)
        {
            Apply(overview.Problems);
            return;
        }
        // Offline or an older library: still drop it from view — Load() will bring it back if it's still true.
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

    public void Dispose()
    {
        store.Changed -= OnStoreChanged;
        if (ownsStore) store.Dispose();
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
