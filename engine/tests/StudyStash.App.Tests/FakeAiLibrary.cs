using StudyStash.Core;
using StudyStash.Core.Ai;

namespace StudyStash.App.Tests;

/// <summary>
/// A library's AI, scripted: a view model test sets <see cref="Overview"/> (and the other fields below) instead of
/// running a real library, and reads <see cref="Calls"/> to check which endpoint a command hit. Every action, by
/// default, applies its obvious effect to <see cref="Overview"/> and hands it straight back — set the matching
/// <c>On...</c> hook to script something else (a timeout, a refusal, an older library's null).
/// </summary>
public sealed class FakeAiLibrary : IAiLibrary
{
    public AiOverview? Overview { get; set; }
    public ToolAccessInfo? Access { get; set; }
    public List<string> Calls { get; } = [];
    public List<(string? Notes, string? Ask, bool? Fallback, string? Diagrams)> DefaultsCalls { get; } = [];
    public List<AskRequest> AskRequests { get; } = [];

    public Func<string, AiSaid?>? OnStart { get; set; }
    public Func<string, AiSaid?>? OnDownload { get; set; }
    public Func<string, AiSaid?>? OnSignIn { get; set; }
    public Func<string, string, AiSaid?>? OnCheck { get; set; }
    public Func<AiOverview?>? OnEngines { get; set; }
    public Func<string?, string?, bool?, AiOverview?>? OnDefaults { get; set; }
    public Func<AskRequest, AskReply?>? OnAsk { get; set; }
    public Func<string, RewriteInfo?>? OnRewrite { get; set; }
    public Func<string, string, RewriteInfo?>? OnRewriteStart { get; set; }
    public Func<string, RewriteInfo?>? OnRewriteCancel { get; set; }
    public Func<string, RewriteInfo?>? OnRewriteKeep { get; set; }
    public Func<string, RewriteInfo?>? OnRewriteUse { get; set; }
    public Func<ToolAccessInfo?>? OnAccess { get; set; }
    public Func<bool?, ReadingScopes?, ToolAccessInfo?>? OnSetAccess { get; set; }
    public Func<bool, ToolAccessInfo?>? OnSetWeb { get; set; }
    public Func<ToolAccessInfo?>? OnCheckWeb { get; set; }

    static EngineInfo Row(AiOverview o, string id) => o.Engines.First(e => e.Id == id);

    public Task<AiOverview?> EnginesAsync()
    {
        Calls.Add("engines");
        return Task.FromResult(OnEngines is not null ? OnEngines() : Overview);
    }

    public Task<AiOverview?> DefaultsAsync(string? notes = null, string? ask = null, bool? fallback = null, string? diagrams = null)
    {
        Calls.Add("defaults");
        DefaultsCalls.Add((notes, ask, fallback, diagrams));
        if (OnDefaults is not null) return Task.FromResult(OnDefaults(notes, ask, fallback));
        if (Overview is null) return Task.FromResult<AiOverview?>(null);
        Overview = Overview with
        {
            Notes = notes ?? Overview.Notes, Ask = ask ?? Overview.Ask, Fallback = fallback ?? Overview.Fallback,
            Diagrams = diagrams ?? Overview.Diagrams,
        };
        return Task.FromResult<AiOverview?>(Overview);
    }

    public Task<AiSaid?> StartAsync(string engine)
    {
        Calls.Add($"start:{engine}");
        if (OnStart is not null) return Task.FromResult(OnStart(engine));
        if (Overview is null) return Task.FromResult<AiSaid?>(null);
        Overview = Overview with { Engines = [.. Overview.Engines.Select(e => e.Id == engine ? e with { State = "ready" } : e)] };
        return Task.FromResult<AiSaid?>(new AiSaid($"Started {Row(Overview, engine).Name}.", Overview));
    }

    public Task<AiSaid?> DownloadAsync(string engine)
    {
        Calls.Add($"download:{engine}");
        if (OnDownload is not null) return Task.FromResult(OnDownload(engine));
        if (Overview is null) return Task.FromResult<AiSaid?>(null);
        Overview = Overview with { Engines = [.. Overview.Engines.Select(e => e.Id == engine ? e with { State = "ready" } : e)] };
        return Task.FromResult<AiSaid?>(new AiSaid($"Downloading {Row(Overview, engine).Name}'s model.", Overview));
    }

    public Task<AiSaid?> SignInAsync(string engine)
    {
        Calls.Add($"sign-in:{engine}");
        if (OnSignIn is not null) return Task.FromResult(OnSignIn(engine));
        if (Overview is null) return Task.FromResult<AiSaid?>(null);
        Overview = Overview with { Engines = [.. Overview.Engines.Select(e => e.Id == engine ? e with { State = "ready" } : e)] };
        return Task.FromResult<AiSaid?>(new AiSaid($"Opened a terminal to sign in to {Row(Overview, engine).Name}.", Overview));
    }

    public Task<AiSaid?> CheckAsync(string engine, string model = "")
    {
        Calls.Add($"check:{engine}");
        if (OnCheck is not null) return Task.FromResult(OnCheck(engine, model));
        if (Overview is null) return Task.FromResult<AiSaid?>(null);
        Overview = Overview with { Engines = [.. Overview.Engines.Select(e => e.Id == engine ? e with { State = "ready" } : e)] };
        return Task.FromResult<AiSaid?>(new AiSaid("Works.", Overview));
    }

    public Task<AiOverview?> ModelAsync(string engine, string model)
    {
        Calls.Add($"model:{engine}:{model}");
        if (Overview is null) return Task.FromResult<AiOverview?>(null);
        Overview = Overview with { Engines = [.. Overview.Engines.Select(e => e.Id == engine ? e with { Model = model } : e)] };
        return Task.FromResult<AiOverview?>(Overview);
    }

    public Task<AiOverview?> DismissAsync(string problemId)
    {
        Calls.Add($"dismiss:{problemId}");
        if (Overview is null) return Task.FromResult<AiOverview?>(null);
        Overview = Overview with { Problems = [.. Overview.Problems.Where(p => p.Id != problemId)] };
        return Task.FromResult<AiOverview?>(Overview);
    }

    public Task<AskReply?> AskAsync(AskRequest request)
    {
        Calls.Add("ask");
        AskRequests.Add(request);
        return Task.FromResult(OnAsk?.Invoke(request));
    }

    /// <summary>An answer written a piece at a time: the test hands the pieces to the answer-so-far callback when it
    /// likes (and honours the stop token); unset, asking answers at once, through <see cref="OnAsk"/>.</summary>
    public Func<AskRequest, Action<string>, CancellationToken, Task<AskReply?>>? OnAskStream { get; set; }
    /// <summary>The engines <see cref="WarmAsync"/> was told a question is being typed for ("" for the library's pick).</summary>
    public List<string> Warmed { get; } = [];

    public Task<AskReply?> AskAsync(AskRequest request, Action<string> answerSoFar, CancellationToken stop)
    {
        if (OnAskStream is null) return AskAsync(request);
        Calls.Add("ask");
        AskRequests.Add(request);
        return OnAskStream(request, answerSoFar, stop);
    }

    public Task WarmAsync(string? engine)
    {
        Warmed.Add(engine ?? "");
        return Task.CompletedTask;
    }

    public Task<RewriteInfo?> RewriteAsync(string lecture)
    {
        Calls.Add($"rewrite:{lecture}");
        return Task.FromResult(OnRewrite?.Invoke(lecture));
    }

    public Task<RewriteInfo?> RewriteStartAsync(string lecture, string engine)
    {
        Calls.Add($"rewrite-start:{lecture}:{engine}");
        return Task.FromResult(OnRewriteStart is not null ? OnRewriteStart(lecture, engine) : new RewriteInfo(lecture, "working") { Engine = engine });
    }

    public Task<RewriteInfo?> RewriteCancelAsync(string lecture)
    {
        Calls.Add($"rewrite-cancel:{lecture}");
        return Task.FromResult(OnRewriteCancel is not null ? OnRewriteCancel(lecture) : new RewriteInfo(lecture, "cancelled"));
    }

    public Task<RewriteInfo?> RewriteKeepAsync(string lecture)
    {
        Calls.Add($"rewrite-keep:{lecture}");
        return Task.FromResult(OnRewriteKeep is not null ? OnRewriteKeep(lecture) : new RewriteInfo(lecture, "none"));
    }

    public Task<RewriteInfo?> RewriteUseAsync(string lecture)
    {
        Calls.Add($"rewrite-use:{lecture}");
        return Task.FromResult(OnRewriteUse is not null ? OnRewriteUse(lecture) : new RewriteInfo(lecture, "none"));
    }

    public Task<ToolAccessInfo?> AccessAsync()
    {
        Calls.Add("access");
        return Task.FromResult(OnAccess is not null ? OnAccess() : Access);
    }

    public Task<ToolAccessInfo?> SetAccessAsync(bool? on = null, ReadingScopes? reading = null)
    {
        Calls.Add("set-access");
        if (OnSetAccess is not null) return Task.FromResult(OnSetAccess(on, reading));
        if (Access is null) return Task.FromResult<ToolAccessInfo?>(null);
        Access = Access with { On = on ?? Access.On, Reading = reading ?? Access.Reading };
        return Task.FromResult<ToolAccessInfo?>(Access);
    }

    /// <summary>Unscripted, Funnel goes on at the design's address and answers from the internet, or goes off.</summary>
    public Task<ToolAccessInfo?> SetWebAsync(bool on)
    {
        Calls.Add($"set-web:{(on ? "on" : "off")}");
        if (OnSetWeb is not null) return Task.FromResult(OnSetWeb(on));
        if (Access is null) return Task.FromResult<ToolAccessInfo?>(null);
        const string Url = "https://mini.tail1234.ts.net";
        Access = Access with
        {
            PublicUrl = on ? Url : null,
            Web = new WebReach(on, "Study Stash", on ? Url + "/mcp" : null, null, null, on ? true : null, on ? ReachCheck.Answers : null,
                on ? 1_790_000_000 : null, Access.HasPassword),
        };
        return Task.FromResult<ToolAccessInfo?>(Access);
    }

    public Task<ToolAccessInfo?> CheckWebAsync()
    {
        Calls.Add("check-web");
        return Task.FromResult(OnCheckWeb is not null ? OnCheckWeb() : Access);
    }
}

/// <summary>Sample AI engine data for view model tests: Ollama and Claude Code ready, Codex not signed in, Gemini
/// not installed — the same mix the AI screens' shots draw.</summary>
public static class AiTestData
{
    public static AiOverview MixedOverview() => new(
        Engines:
        [
            new EngineInfo("ollama", "Ollama", "ready") { Installed = true, Model = "qwen3:30b" },
            new EngineInfo("claude", "Claude Code", "ready") { Installed = true },
            new EngineInfo("codex", "Codex", "not_signed_in") { Installed = true },
            new EngineInfo("gemini", "Gemini", "not_installed") { Site = "https://gemini.google.com" },
        ],
        Notes: "ollama", Ask: "claude", Fallback: true, Problems: []);
}
