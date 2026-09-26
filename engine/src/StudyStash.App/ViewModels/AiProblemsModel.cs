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
    /// <summary>The Mac icon's colour token (Accent/Warn/Fg2).</summary>
    public required string ColorKey { get; init; }
    /// <summary>The Windows InfoBar's severity (err/warn/info).</summary>
    public required string Severity { get; init; }
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
public sealed class AiProblemsModel(IAiLibrary library)
{
    public ObservableCollection<AiProblem> Problems { get; } = [];

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
        if (overview is not null) Apply(overview.Problems);
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
        var problem = new AiProblem
        {
            Id = p.Id,
            Kind = p.Kind,
            Caption = AiWords.ProblemCaption(p.Kind),
            Title = AiWords.ProblemTitle(p.Kind, p.EngineName, fallbackName),
            Message = AiWords.ProblemMessage(p.Kind, p.EngineName, fallbackName, p.Until, p.SizeGb, ""),
            Icon = AiWords.ProblemIcon(p.Kind),
            ColorKey = AiWords.ProblemColorKey(p.Kind),
            Severity = AiWords.ProblemSeverity(p.Kind),
            PrimaryWords = AiWords.ProblemAction(p.Kind),
        };
        problem.PrimaryCommand = new AsyncRelayCommand(() => Act(p));
        problem.CloseCommand = new AsyncRelayCommand(() => CloseServerAsync(p.Id));
        return problem;
    }

    async Task Act(AiProblemInfo p)
    {
        try
        {
            AiSaid? said = p.Kind switch
            {
                "engine_offline" => await library.StartAsync(p.Engine),
                "not_signed_in" => await library.SignInAsync(p.Engine),
                "model_missing" => await library.DownloadAsync(p.Engine),
                "usage_limit" => await Dismissed(p.Id),
                _ => null,
            };
            if (said?.Overview is not null) Apply(said.Overview.Problems);
        }
        catch (LibraryRefusedException)
        {
            // The row stays; its own action can be tried again.
        }
        catch
        {
            // Offline: nothing to update here — the pane that owns "Offline" words shows that.
        }
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
        Title = AiWords.ProblemTitle("library_offline", "", ""), Message = AiWords.ProblemMessage("library_offline", "", "", "", 0, ""),
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
