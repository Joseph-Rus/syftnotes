using System.Globalization;
using StudyStash.Core;
using StudyStash.Core.Ai;

namespace StudyStash.App.ViewModels;

/// <summary>
/// Every piece of copy the AI screens derive rather than take literally from the design: an engine's state as a
/// word, which colour its dot is, what its row's button says, the icon for each engine, and the times the design
/// writes as "Tue 11:52", "Used 10:40" or "3:00 PM". Pure functions, so every AI view model reads from here instead
/// of repeating a switch.
/// </summary>
public static partial class AiWords
{
    /// <summary>An engine's icon (memory/terminal/code/auto_awesome), by id.</summary>
    public static string EngineIcon(string id) => id switch
    {
        "ollama" => "memory",
        "claude" => "terminal",
        "codex" => "code",
        "gemini" => "auto_awesome",
        _ => "memory",
    };

    /// <summary>The word a state shows next to its dot: ready Ready · unchecked Installed · not_signed_in Not
    /// signed in · not_running Not running · model_missing Needs its model · limited Limit reached ·
    /// not_installed Not installed · failed Didn't work.</summary>
    public static string EngineStateWords(string state) => state switch
    {
        "ready" => "Ready",
        "unchecked" => "Installed",
        "not_signed_in" => "Not signed in",
        "not_running" => "Not running",
        "model_missing" => "Needs its model",
        "limited" => "Limit reached",
        "not_installed" => "Not installed",
        "failed" => "Didn't work",
        _ => state,
    };

    public static bool StateIsGood(string state) => state == "ready";
    public static bool StateIsWarn(string state) => state is "not_signed_in" or "not_running" or "model_missing" or "limited" or "failed";
    public static bool StateIsMuted(string state) => state is "unchecked" or "not_installed";

    /// <summary>The row's button: ready → Options · unchecked → Check · not_signed_in → Sign in (tinted) ·
    /// not_running → Start · model_missing → Download · not_installed → Get it · limited/failed → Options.</summary>
    public static string RowActionWords(string state) => state switch
    {
        "unchecked" => "Check",
        "not_signed_in" => "Sign in",
        "not_running" => "Start",
        "model_missing" => "Download",
        "not_installed" => "Get it",
        _ => "Options",
    };

    /// <summary>Only "Sign in" is the tinted, accent-coloured button; every other row action is a plain pill.</summary>
    public static bool RowActionPrimary(string state) => state == "not_signed_in";

    /// <summary>Whether the row's button opens the Options menu (its models, and Check it works) instead of acting
    /// straight away.</summary>
    public static bool NeedsOptions(string state) => state is "ready" or "limited" or "failed";

    /// <summary>The AI engines pane's row subtitle: Ollama always says nothing leaves it; a ready CLI says it's
    /// signed in; every other CLI just says where it runs.</summary>
    public static string EngineAbout(string id, string state) =>
        id == "ollama" ? "Runs on your library. Nothing leaves it."
        : state == "ready" ? "Runs on your library. Signed in."
        : "Runs on your library.";

    /// <summary>A diagrams pick as its menu says it: Automatic · Same as notes · an engine · Off.</summary>
    public static string DiagramsChoiceName(string id) => id switch
    {
        DiagramEngines.Auto => "Automatic",
        DiagramEngines.SameAsNotes => "Same as notes",
        DiagramEngines.Off => "Off",
        _ => Engines.Name(id),
    };

    /// <summary>The diagrams row's line, saying where each transcript goes to be drawn from: off draws none; same as
    /// notes, the notes engine as it writes; otherwise the engine the pick comes to on the library right now.</summary>
    public static string DiagramsAbout(string choice, string by) => choice switch
    {
        DiagramEngines.Off => "New notes have no diagrams",
        DiagramEngines.SameAsNotes => "The notes engine draws them as it writes",
        _ when by == "ollama" => "Ollama reads each transcript, on your library",
        _ when by.Length > 0 => $"{Engines.Name(by)} reads each transcript",
        _ => "From each transcript, after the notes",
    };

    /// <summary>The library setup step's row subtitle: phrased for the computer you're sitting at, since in setup
    /// the library is this computer.</summary>
    public static string SetupAbout(string id, string state, string device = "computer") => id == "ollama"
        ? state switch
        {
            "not_installed" => $"Free and private, but not on this {device} yet.",
            "not_running" => "Installed. Start it to write notes.",
            "model_missing" => "Installed. It needs the model it writes notes with.",
            _ => "Private. Runs here, nothing leaves this computer.",
        }
        : state switch
        {
            "ready" => "Signed in on this computer.",
            "not_signed_in" => "Installed. Sign in to use it.",
            "unchecked" => "Installed on this computer.",
            "not_installed" => $"Not on this {device} yet.",
            "limited" => "Hit its usage limit for now.",
            _ => "",
        };

    /// <summary>"Written by Ollama · Tue 11:52": the notes' byline (engine name, then the design's "ddd H:mm").</summary>
    public static string Byline(string engineName, DateTime at) => $"{engineName} · {at:ddd H:mm}";

    /// <summary>A connected tool's last-used words: "Used 10:40" today, "Used Tue" this week, else "Used 12 Sep".</summary>
    public static string UsedWords(DateTime at, DateTime now)
    {
        if (at.Date == now.Date) return $"Used {at:H:mm}";
        if (now - at < TimeSpan.FromDays(7)) return $"Used {at:ddd}";
        return $"Used {at:d MMM}";
    }

    /// <summary>A usage limit's "3:00 PM", the design's h:mm tt.</summary>
    public static string UntilClock(DateTime until) => until.ToString("h:mm tt", CultureInfo.InvariantCulture);

    /// <summary>Parses the ISO-ish time `AiOverview`/`AiProblemInfo` carry ("Until"), or null when it's empty or
    /// unreadable.</summary>
    public static DateTime? ParseUntil(string iso) =>
        DateTime.TryParse(iso, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var t) ? t : null;

    public const string OlderLibraryWords = "Your library runs an older Study Stash: update it to pick engines here.";

    /// <summary>Whether an engine is worth offering to ask or write with: ready, or installed but never checked. A
    /// down/limited/unsignedin/missing-model engine is skipped (not_installed already never reaches the ask menu).</summary>
    public static bool EngineUsable(string state) => state is "ready" or "unchecked";

    /// <summary>The "Answer with" menu's row subtitle: the ask default says so; Ollama says it's private; a signed-
    /// out row says so; everything else (a ready, non-default CLI) says nothing extra.</summary>
    public static string AskEngineSubtitle(string id, string state, bool isDefault) =>
        isDefault ? "Default for questions"
        : id == "ollama" ? "Private, on your library"
        : state == "not_signed_in" ? "Not signed in"
        : "";

    /// <summary>The ask field's placeholder for a scope: This lecture/class/all your classes.</summary>
    public static string AskPlaceholder(string scope) => scope switch
    {
        "class" => "Ask about this class",
        "all" => "Ask about all your classes",
        _ => "Ask about this lecture",
    };

    /// <summary>While waiting: "Asking Claude Code…".</summary>
    public static string Thinking(string engineName) => $"Asking {engineName}…";

    /// <summary>An answer's byline: the engine, then the moments in the lecture it drew on, earliest first ("Ollama ·
    /// from 18:05 and 18:40"), or just the engine when nothing carries a moment.</summary>
    public static string AskByline(string engineName, IEnumerable<AskSource> sources)
    {
        // The moments the answer drew on, in the order they were said (an engine lists them by relevance), each once.
        var times = sources.Where(s => s.At is not null).Select(s => s.At!.Value).Order().Select(TimedText.Clock).Distinct().ToList();
        return times.Count switch
        {
            0 => engineName,
            1 => $"{engineName} · from {times[0]}",
            _ => $"{engineName} · from {string.Join(", ", times[..^1])} and {times[^1]}",
        };
    }

    /// <summary>"This answer came from Ollama. Claude Code didn't respond in time." — why already reads as a full
    /// sentence about the engine that was asked.</summary>
    public static string FellBackNote(string engineName, string why) => $"This answer came from {engineName}. {why}";

    /// <summary>Under an answer the student stopped: what's there is all there'll be.</summary>
    public const string Stopped = "Stopped.";

    /// <summary>When the library goes quiet with the answer half written (what came is kept above it).</summary>
    public const string CutOff = "Your library stopped answering partway through. Check it's on and connected.";

    /// <summary>When the library can't be reached at all.</summary>
    public const string NotAnswering = "Your library isn't answering. Check it's on and connected.";

    // -----------------------------------------------------------------------------------------------------------
    // 17 · Rewrite the notes.
    // -----------------------------------------------------------------------------------------------------------

    /// <summary>The current notes' byline: "Written by Ollama · Tue 11:52".</summary>
    public static string WrittenByline(string engineName, string updatedAtIso) =>
        engineName.Length == 0 ? "" // an older lecture that doesn't say who wrote its notes
        : ParseUntil(updatedAtIso) is { } at ? $"Written by {engineName} · {at:ddd H:mm}" : $"Written by {engineName}";

    /// <summary>A ready draft's byline: "Claude Code · just now" (or "· 5 min ago", "· 2 h ago", a date).</summary>
    public static string DraftByline(string engineName, string atIso, DateTime now) => $"{engineName} · {History.Ago(atIso, now)}";

    /// <summary>Drops a leading "Summary" heading from a lecture's notes: the header row above already says
    /// "Summary", so the notes themselves start straight at the body once this is applied. Only a *leading*
    /// heading is ever touched.</summary>
    public static string DropLeadingSummary(string markdown)
    {
        string trimmed = markdown.TrimStart();
        if (trimmed.Length == 0 || trimmed[0] != '#') return markdown;
        int nl = trimmed.IndexOf('\n');
        string firstLine = nl < 0 ? trimmed : trimmed[..nl];
        return HeadingSummary().IsMatch(firstLine) ? (nl < 0 ? "" : trimmed[(nl + 1)..].TrimStart('\n')) : markdown;
    }

    [System.Text.RegularExpressions.GeneratedRegex(@"^#{1,6}\s*Summary\s*$", System.Text.RegularExpressions.RegexOptions.IgnoreCase)]
    private static partial System.Text.RegularExpressions.Regex HeadingSummary();

    /// <summary>The "Rewrite notes with" menu's row subtitle: the writer says so; a signed-out row says so; every
    /// other usable row says nothing extra (unlike "Answer with", nobody here is "the default").</summary>
    public static string RewriteEngineSubtitle(string id, string state, bool isWriter) =>
        isWriter ? "Wrote the current notes"
        : state == "not_signed_in" ? "Not signed in"
        : id == "ollama" ? "Private, on your library"
        : "";

    // -----------------------------------------------------------------------------------------------------------
    // 18 · AI problems.
    // -----------------------------------------------------------------------------------------------------------

    /// <summary>The small caption above a problem card: Engine offline · Not signed in · Model missing · Usage
    /// limit · Fell back · Access request · Library offline · Rewrite failed.</summary>
    public static string ProblemCaption(string kind) => kind switch
    {
        "engine_offline" => "Engine offline",
        "not_signed_in" => "Not signed in",
        "model_missing" => "Model missing",
        "usage_limit" => "Usage limit",
        "fell_back" => "Fell back",
        "access_request" => "Access request",
        "library_offline" => "Library offline",
        "rewrite_failed" => "Rewrite failed",
        _ => kind,
    };

    /// <summary>The Mac card's icon: power_off · person_off · download · hourglass_top · swap_horiz · key ·
    /// cloud_off · error.</summary>
    public static string ProblemIcon(string kind) => kind switch
    {
        "engine_offline" => "power_off",
        "not_signed_in" => "person_off",
        "model_missing" => "download",
        "usage_limit" => "hourglass_top",
        "fell_back" => "swap_horiz",
        "access_request" => "key",
        "library_offline" => "cloud_off",
        "rewrite_failed" => "error",
        _ => "info",
    };

    /// <summary>The Mac icon's colour token: Accent for an offline engine, Warn for the other actionable problems,
    /// Fg2 for the merely informational ones.</summary>
    public static string ProblemColorKey(string kind) => kind switch
    {
        "engine_offline" => "Accent",
        "not_signed_in" or "model_missing" or "library_offline" or "rewrite_failed" => "Warn",
        _ => "Fg2",
    };

    /// <summary>The Windows InfoBar's severity: err · warn · info.</summary>
    public static string ProblemSeverity(string kind) => kind switch
    {
        "engine_offline" or "library_offline" or "rewrite_failed" => "err",
        "not_signed_in" or "model_missing" => "warn",
        _ => "info",
    };

    public static string ProblemTitle(string kind, string engineName, string fallbackName) => kind switch
    {
        "engine_offline" => $"{engineName} isn't running on your library",
        "not_signed_in" => $"Sign in to {engineName} on your library",
        "model_missing" => $"{engineName} needs its notes model",
        "usage_limit" => $"{engineName} hit its usage limit",
        "fell_back" => $"This answer came from {engineName}",
        "access_request" => $"{engineName} wants to read your library",
        "library_offline" => "Your library isn't answering",
        "rewrite_failed" => $"{engineName} couldn't rewrite the notes",
        _ => engineName,
    };

    public static string ProblemMessage(string kind, string engineName, string fallbackName, string until, double sizeGb, string detail) => kind switch
    {
        "engine_offline" => "New lectures wait and get their notes when it's back.",
        "not_signed_in" => $"Until then, questions go to {fallbackName}.",
        "model_missing" => sizeGb > 0 ? $"About {sizeGb:0} GB, downloaded once on your library." : "Downloaded once on your library.",
        "usage_limit" => ParseUntil(until) is { } t ? $"Questions go to {fallbackName} until {UntilClock(t)}." : $"Questions go to {fallbackName}.",
        "fell_back" => detail,
        "access_request" => $"From {detail}. It can read, not change.",
        "library_offline" => "Engines run on your library. Check it's on and connected.",
        "rewrite_failed" => $"{detail} Your current notes are unchanged.",
        _ => detail,
    };

    /// <summary>The card's primary action: Start Ollama · Sign in · Download · Dismiss · (none for Fell back) ·
    /// Deny/Allow · Try again · Try again/Dismiss.</summary>
    public static string ProblemAction(string kind) => kind switch
    {
        "engine_offline" => "Start Ollama",
        "not_signed_in" => "Sign in",
        "model_missing" => "Download",
        "usage_limit" => "Dismiss",
        "access_request" => "Allow",
        "library_offline" => "Try again",
        "rewrite_failed" => "Try again",
        _ => "",
    };
}
