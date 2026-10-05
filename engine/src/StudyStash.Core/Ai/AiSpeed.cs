namespace StudyStash.Core.Ai;

/// <summary>
/// How fast Claude Code writes the notes and designs the rich notes (ai.json <see cref="AiSettings.Speed"/>), for the
/// two things that take a while:
/// <list type="bullet">
/// <item><c>standard</c>: as it's set up (the notes with the model picked, or Claude Code's own default; the designer
/// on Opus at high effort);</item>
/// <item><c>fast</c>: Claude Code's fast mode, the same Opus with up to 2.5 times faster output. It draws on the Claude
/// account's usage credits at a higher rate than the plan's own usage, so a Claude account without usage credits runs at
/// normal speed. Opus only: a notes model picked as Sonnet or Haiku stays as picked;</item>
/// <item><c>quick</c>: a smaller model at low effort (Sonnet; the designer a little higher, since it decides what to
/// draw): quicker and lighter on the plan, with shallower notes and diagrams.</item>
/// </list>
/// Only Claude Code has these; every other engine runs as it's set up. The sorting and the answers never change.
/// </summary>
public static class AiSpeed
{
    public const string Standard = "standard", Fast = "fast", Quick = "quick";

    public static readonly string[] Choices = [Standard, Fast, Quick];

    /// <summary>A stored choice as it's read: anything unknown is standard.</summary>
    public static string Normal(string? choice) => choice is { } c && Choices.Contains(c) ? c : Standard;

    /// <summary>The model, reasoning effort and fast mode a run is asked with ("" and false: as set up).</summary>
    public readonly record struct How(string Model, string Effort, bool Fast);

    /// <summary>How the notes are written by <paramref name="engine"/> with <paramref name="picked"/> as its model.</summary>
    public static How ForNotes(string engine, string picked, string? speed)
    {
        if (engine != "claude") return new How(picked, "", false);
        return Normal(speed) switch
        {
            // Fast mode is Opus's: say Opus rather than lean on the default (an account's may be Sonnet), unless a smaller
            // model was picked on purpose.
            Fast when picked is "" or "opus" => new How("opus", "", true),
            Quick => new How(picked == "haiku" ? picked : "sonnet", "low", false),
            _ => new How(picked, "", false),
        };
    }

    /// <summary>How the diagram designer (and what it draws) runs on <paramref name="engine"/>: its strongest model at
    /// high effort, as <see cref="DiagramEngines.TopModel"/> and <see cref="DiagramEngines.TopEffort"/> say, in fast mode
    /// when asked, or a smaller model at lower effort.</summary>
    public static How ForDesign(string engine, string? speed)
    {
        var top = new How(DiagramEngines.TopModel(engine), DiagramEngines.TopEffort(engine), false);
        if (engine != "claude") return top;
        return Normal(speed) switch
        {
            Fast => top with { Fast = true },
            Quick => new How("sonnet", "medium", false),
            _ => top,
        };
    }
}
