namespace StudyStash.Core.Ai;

/// <summary>Who designs a lecture's diagrams (<see cref="DiagramDesign"/>): an engine, the model and reasoning effort
/// it's run with ("" for its own default), and what it may draw.</summary>
public sealed record DiagramPick(string Engine, string Model, string Effort, Drawings Drawings);

/// <summary>
/// Settings → AI engines → Draws diagrams, kept in ai.json as <see cref="AiSettings.Diagrams"/>: <c>auto</c> (the
/// default), <c>notes</c> (the notes engine draws them as it writes, the way it did before there was a diagram pass),
/// <c>off</c> (no diagrams), or an engine's id. Which engine that comes to, on the library's computer, right now.
/// </summary>
public static class DiagramEngines
{
    public const string Auto = "auto", SameAsNotes = "notes", Off = "off";

    /// <summary>Every choice there is: automatic, same as notes, each engine, off.</summary>
    public static readonly string[] Choices = [Auto, SameAsNotes, .. Engines.Order, Off];

    /// <summary>A stored choice as it's read: anything unknown is automatic.</summary>
    public static string Normal(string? choice) => choice is { } c && Choices.Contains(c) ? c : Auto;

    /// <summary>The strongest model each engine offers, for the one call per lecture the design is: Claude Code's Opus,
    /// Gemini 3.1 Pro, Codex's own default (with high reasoning effort instead).</summary>
    public static string TopModel(string engine) => engine switch
    {
        "claude" => "opus",
        "gemini" => "gemini-3.1-pro-high",
        _ => "",
    };

    /// <summary>High reasoning effort, where the engine's command takes one (Claude Code's --effort, Codex's
    /// model_reasoning_effort).</summary>
    public static string TopEffort(string engine) => engine is "claude" or "codex" ? "high" : "";

    /// <summary>The reasoning effort an illustration is drawn with: the plan is already made, and a detailed drawing is
    /// long to write out, so less thinking keeps it within the pass's time without making it worse.</summary>
    public static string DrawEffort(string engine) => engine is "claude" or "codex" ? "medium" : "";

    /// <summary>
    /// Who designs the diagrams for notes written by <paramref name="notesEngine"/>, or null when nobody does (diagrams
    /// off, or the notes engine draws them itself). A picked engine is used while it looks usable; when it doesn't
    /// (not installed, not signed in, over its limit), the fallback to Ollama applies as it does to the notes.
    /// Automatic is the strongest engine that already reads this library's lectures — the notes engine, or the one
    /// that answers questions — in the order Claude Code, Codex, Gemini, so a lecture never goes anywhere it doesn't
    /// already go; else the biggest model Ollama runs on this computer (never an Ollama cloud model); else the notes
    /// engine itself.
    /// </summary>
    public static async Task<DiagramPick?> PickAsync(AiSettings settings, Config cfg, EngineChecks checks, string notesEngine)
    {
        string choice = Normal(settings.Diagrams);
        if (choice is Off or SameAsNotes) return null;
        if (choice != Auto)
        {
            if (choice == "ollama") return await LocalAsync(cfg, checks);
            if (Engines.KnownUnusableWhy(choice, settings, checks) is null) return Cloud(choice);
            return settings.Fallback ? await LocalAsync(cfg, checks) : null;
        }
        string[] reads = [notesEngine, settings.For("ask").Provider];
        foreach (string id in new[] { "claude", "codex", "gemini" })
            if (reads.Contains(id) && Engines.KnownUnusableWhy(id, settings, checks) is null) return Cloud(id);
        return await LocalAsync(cfg, checks)
            ?? (notesEngine == "ollama" ? new DiagramPick("ollama", cfg.EffectiveSummaryModel, "", Drawings.Flowcharts) : null);
    }

    static DiagramPick Cloud(string id) => new(id, TopModel(id), TopEffort(id), Drawings.FlowchartsAndSvg);

    /// <summary>The biggest model Ollama has on this computer: an Ollama cloud model (no size, or "cloud" in its name)
    /// would send the lecture off it, so it never counts. Null when Ollama isn't there, running, or has none.</summary>
    static async Task<DiagramPick?> LocalAsync(Config cfg, EngineChecks checks)
    {
        if (!checks.OllamaInstalled()) return null;
        var models = await checks.OllamaModels(cfg.OllamaHost);
        var local = (models ?? []).Where(m => m.SizeGb > 0 && !m.Name.Contains("cloud", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(m => m.SizeGb).Select(m => m.Name).FirstOrDefault();
        return local is null ? null : new DiagramPick("ollama", local, "", Drawings.Flowcharts);
    }
}
