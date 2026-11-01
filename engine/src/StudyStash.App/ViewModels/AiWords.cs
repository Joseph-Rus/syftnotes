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
    /// <summary>An engine's icon, by id: the product's own mark, so a student sees the one they know (Claude's,
    /// ChatGPT's for the engine the app calls ChatGPT, Gemini's, Ollama's). Anything else is a plain chip.</summary>
    public static string EngineIcon(string id) => id switch
    {
        "ollama" => "brand:ollama",
        "claude" => "brand:claude",
        "codex" => "brand:chatgpt",
        "gemini" => "brand:gemini",
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
    /// not_running → Start · model_missing → Download · not_installed → Set up · limited/failed → Options.</summary>
    public static string RowActionWords(string state) => state switch
    {
        "unchecked" => "Check",
        "not_signed_in" => "Sign in",
        "not_running" => "Start",
        "model_missing" => "Download",
        "not_installed" => "Set up",
        _ => "Options",
    };

    /// <summary>The line on Settings' row for an AI not on this computer yet: what it's for, plainly, and that Set up gets
    /// it ready (Syft Notes installs it and signs in; nothing to type).</summary>
    public static string NotSetUpAbout(string id) => id switch
    {
        "claude" => "For a Claude Pro or Max plan. Set up gets it ready on this computer.",
        "codex" => "For a ChatGPT Plus plan or higher. Set up gets it ready on this computer.",
        "gemini" => "Free with a Google account. Set up gets it ready on this computer.",
        _ => "Set up gets it ready on this computer.",
    };

    /// <summary>Only "Sign in" is the tinted, accent-coloured button; every other row action is a plain pill.</summary>
    public static bool RowActionPrimary(string state) => state == "not_signed_in";

    /// <summary>Whether the row's button opens the Options menu (its models, and Check it works) instead of acting
    /// straight away.</summary>
    public static bool NeedsOptions(string state) => state is "ready" or "limited" or "failed";

    /// <summary>The AI engines pane's row subtitle: Ollama always says nothing leaves it; a ready CLI says it's
    /// signed in; every other CLI just says where it runs.</summary>
    public static string EngineAbout(string id, string state, bool here = false) =>
        id == "ollama" ? $"Runs on {Where(here)}. Nothing leaves it."
        : Through(id) + (state == "ready" ? $"Runs on {Where(here)}. Signed in." : $"Runs on {Where(here)}.");

    /// <summary>Where the AI runs, in the student's words: "this computer" when the library is on it (one computer
    /// does everything), "your library" when the library is on another.</summary>
    public static string Where(bool here) => here ? "this computer" : "your library";

    /// <summary>The tool Syft Notes reaches an AI through, said once on its row: "Through Claude Code. ".</summary>
    static string Through(string id) => id switch
    {
        "claude" => "Through Claude Code. ",
        "codex" => "Through Codex. ",
        _ => "",
    };

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
        DiagramEngines.SameAsNotes => "The AI that writes your notes draws them too",
        _ when by == "ollama" => "Ollama reads each transcript, on your library",
        _ when by.Length > 0 => $"{Engines.Name(by)} reads each transcript",
        _ => "From each transcript, after the notes",
    };

    /// <summary>The Rich notes row's line: what's added after the notes, or that nothing more is asked of the AI.</summary>
    public static string RichAbout(bool on) => on
        ? "Diagrams, formula plots and labelled drawings, added after each lecture's notes"
        : "Plain notes only. Nothing extra is asked of your AI";

    /// <summary>A speed as its menu says it: Standard · Fast mode · Quicker model.</summary>
    public static string SpeedChoiceName(string id) => id switch
    {
        AiSpeed.Fast => "Fast mode",
        AiSpeed.Quick => "Quicker model",
        _ => "Standard",
    };

    /// <summary>What a speed trades, plainly: pace, depth and what it uses up.</summary>
    public static string SpeedAbout(string id) => id switch
    {
        AiSpeed.Fast => "The same Opus, up to 2.5 times faster for notes and diagrams, but billed at a higher rate. "
            + "It needs usage credits on your Claude account, and without them Claude runs at its normal speed",
        AiSpeed.Quick => "Sonnet at low effort for the notes and diagrams: quicker and lighter on your plan, with shallower notes and diagrams",
        _ => "Claude as it's set up, at its usual pace",
    };

    /// <summary>The library setup step's row subtitle: phrased for the computer you're sitting at, since in setup
    /// the library is this computer.</summary>
    /// <summary>An AI's name where a student chooses one in Settings ("Your AI"): what they'd call it. Claude Code and
    /// Codex are how Syft Notes reaches Claude and ChatGPT, and the free one is Ollama; a student picks the AI, not
    /// the tool, and the tool's name is in the row's own line.</summary>
    public static string PlainName(string id, string name) => id switch
    {
        "ollama" => "Ollama (free, advanced)",
        // By its id too: a library on an older Syft Notes still sends the tools' names.
        "claude" => "Claude",
        "codex" => "ChatGPT",
        _ => name,
    };

    /// <summary>The AIs in the order a student meets them everywhere they're listed: Claude, ChatGPT, Gemini, then the
    /// free one on this computer (Ollama, for the comfortable with it).</summary>
    public static readonly string[] ShownOrder = ["claude", "codex", "gemini", "ollama"];

    /// <summary><paramref name="engines"/> in <see cref="ShownOrder"/> (an AI it doesn't know goes last).</summary>
    public static IEnumerable<EngineInfo> InShownOrder(IEnumerable<EngineInfo> engines) => InShownOrder(engines, e => e.Id);

    /// <summary>The same, for anything that says which AI it is.</summary>
    public static IEnumerable<T> InShownOrder<T>(IEnumerable<T> items, Func<T, string> id) =>
        items.OrderBy(x => Array.IndexOf(ShownOrder, id(x)) is var i and >= 0 ? i : ShownOrder.Length);

    static string Simpler(bool small, string device) => small ? $" A small one fits this {device}'s memory, so its notes are simpler than Claude's or ChatGPT's." : "";

    /// <summary>An engine's name in setup's "who writes your notes" list, in a student's terms: what they'd say they
    /// have (Claude, ChatGPT), or what it is (a free AI on this computer). The tools' own names (Claude Code, Codex,
    /// Ollama) are in the line under it, so they're recognised in Settings later.</summary>
    public static string SetupName(string id, string name, string device = "computer") => id switch
    {
        "ollama" => $"Free AI on this {device}",
        "claude" => "Claude",
        "codex" => "ChatGPT",
        "gemini" => "Gemini",
        _ => name,
    };

    /// <summary>What "Set it up" on the free AI's row is doing right now, under the row.</summary>
    public static string FreeAiProgress(PullInfo at) => at.Step switch
    {
        "app" => $"Downloading the free AI… {Math.Round(Math.Clamp(at.Fraction, 0, 1) * 100):0}%",
        "start" => "Starting it…",
        _ => $"Downloading what it writes notes with ({HowBig(Core.Ollama.DownloadGb(at.Model))}, once)… {Math.Round(Math.Clamp(at.Fraction, 0, 1) * 100):0}%",
    };

    /// <summary>"about 10 GB", "about 1.4 GB", or "a few gigabytes" for a size that isn't known.</summary>
    public static string HowBig(double gb) => gb <= 0 ? "a few gigabytes" : $"about {gb.ToString(gb < 10 ? "0.#" : "0", System.Globalization.CultureInfo.InvariantCulture)} GB";

    /// <summary>A row's line in setup's "who writes your notes" list. For the free AI that still needs setting up:
    /// how much there is to download for this computer (<paramref name="gb"/>), and, where only the small one fits
    /// (<paramref name="small"/>), that its notes are simpler: said before the download, not found out after.</summary>
    public static string SetupAbout(string id, string state, string device = "computer", double gb = 0, bool small = false, bool hasKey = false) => id == "ollama"
        ? state switch
        {
            "not_installed" => $"Free and private (Ollama). Syft Notes downloads it for you: {HowBig(gb)}, once.{Simpler(small, device)}",
            "not_running" => "Free and private (Ollama). It's here, and needs starting.",
            "model_missing" => $"Free and private (Ollama). It's here, and still needs what it writes notes with: {HowBig(gb)}, once.{Simpler(small, device)}",
            _ => $"Free and private (Ollama). It runs on this {device}: nothing you record leaves it.",
        }
        // An API key the student gave it writes the notes, whatever else is installed.
        : hasKey ? state switch
        {
            "ready" => "With your API key. It works.",
            "limited" => "With your API key. Hit its usage limit for now.",
            "failed" or "not_signed_in" => "With your API key, which didn't work last time.",
            _ => "With your API key.",
        }
        : id is "claude" or "codex" or "gemini" ? (id switch
        {
            "claude" => "With your paid Claude plan, through Claude Code. ",
            "codex" => "With your paid ChatGPT plan, through Codex. ",
            // Gemini runs on an API key from Google AI Studio, or through Google's Antigravity, signed in.
            _ => state == "not_installed" ? "With an API key, or Google's Antigravity app. " : "Through Google's Antigravity. ",
        }) + state switch
        {
            "ready" => "Signed in on this computer.",
            "not_signed_in" => "Installed. Sign in to use it.",
            "unchecked" => "Installed on this computer.",
            "not_installed" => $"Not on this {device} yet.",
            "limited" => "Hit its usage limit for now.",
            _ => "",
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

    public const string OlderLibraryWords = "Your library runs an older Syft Notes: update it to choose your AI here.";

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

    /// <summary>The ask field's placeholder for a scope: this lecture (or meeting, or recording), its folder by name,
    /// or everything.</summary>
    public static string AskPlaceholder(string scope, string noun = Nouns.Lecture, string? folder = null) => scope switch
    {
        "class" => $"Ask about {folder ?? "this folder"}",
        "all" => "Ask anything about your notes",
        _ => $"Ask about this {noun}",
    };

    /// <summary>The ask field while a lecture records: there's no lecture yet, only what's been heard.</summary>
    public const string AskLive = "Ask about what's been said so far";

    /// <summary>While waiting: "Asking Claude Code…".</summary>
    public static string Thinking(string engineName) => $"Asking {engineName}…";

    /// <summary>An answer's byline: the engine, then the moments in the lecture it drew on, earliest first ("Ollama ·
    /// from 18:05 and 18:40"), or just the engine when nothing carries a moment.</summary>
    public static string AskByline(string engineName, IEnumerable<AskSource> sources, bool readChat = false) =>
        string.Concat(AskBylineParts(engineName, [.. sources], null, readChat).Select(p => p.Text));

    /// <summary>The byline in the pieces it's shown in. With <paramref name="open"/> (the page can show a source),
    /// each moment is a link of its own that opens its source, so every one the answer drew on can be gone to; a
    /// byline that names no moment opens the answer's first source, as one press. <paramref name="readChat"/>: the
    /// conversation above went to this AI with the question, where another AI (the free one, on the student's own
    /// computer) answered it, and the byline says so (<see cref="ReadChat"/>).</summary>
    public static List<BylinePart> AskBylineParts(string engineName, IReadOnlyList<AskSource> sources, Action<AskSource>? open, bool readChat = false)
    {
        // The moments the answer drew on, in the order they were said (an engine lists them by relevance), each once.
        var moments = sources.Where(s => s.At is not null).OrderBy(s => s.At!.Value).DistinctBy(s => TimedText.Clock(s.At!.Value)).ToList();
        var parts = new List<BylinePart>();
        if (moments.Count == 0) parts.Add(new BylinePart(engineName, open is not null && sources.Count > 0 ? () => open(sources[0]) : null));
        else parts.Add(new BylinePart($"{engineName} · from "));
        for (int i = 0; i < moments.Count; i++)
        {
            if (i > 0) parts.Add(new BylinePart(i == moments.Count - 1 ? " and " : ", "));
            var source = moments[i];
            parts.Add(new BylinePart(TimedText.Clock(source.At!.Value), open is null ? null : () => open(source), moment: true));
        }
        if (readChat) parts.Add(new BylinePart(ReadChat));
        return parts;
    }

    /// <summary>After an answer's byline, when the chat above went to an AI that hadn't had it (Claude asked after the
    /// free AI answered, which kept it on the computer): " · also read the chat above".</summary>
    public const string ReadChat = " · also read the chat above";

    /// <summary>Under the first answer from a library too old to keep chats.</summary>
    public const string NoHistoryOnOlderLibrary = "Your library runs an older Syft Notes, so this chat isn't kept and follow-ups start fresh. Update it to keep your chats.";

    /// <summary>What a moment in a byline does: "Read the transcript at 01:52".</summary>
    public static string MomentTip(string time) => $"Read the transcript at {time}";

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

    /// <summary>The line over the notes right after a change to them, by what made it: "Notes replaced by Claude",
    /// "Notes pasted from a chat", "Your edit saved", "Earlier notes put back", "Undone".</summary>
    public static string ChangeWords(string kind, string by) => kind switch
    {
        NotesChangeKind.App => $"Notes replaced by {Who(by)}",
        NotesChangeKind.Paste => "Notes pasted from a chat",
        NotesChangeKind.Edit => "Your edit saved",
        NotesChangeKind.Restore => "Earlier notes put back",
        NotesChangeKind.Undo => "Undone",
        NotesChangeKind.Redo => "Redone",
        NotesChangeKind.Rewrite => $"Notes rewritten by {Who(by)}",
        NotesChangeKind.Diagrams => "Diagrams added",
        NotesChangeKind.File => "Changed in the note's file",
        _ => $"New notes from {Who(by)}",
    };

    /// <summary>Undo or Redo pressed after the notes changed on another device or in another app.</summary>
    public const string ChangedElsewhere = "These notes changed on another device since this page showed them, so nothing was undone. Here they are now.";

    /// <summary>A version in History, by what made it: "Written by Claude", "Your edit", "Pasted from a chat", "Put
    /// back".</summary>
    public static string VersionWords(string kind, string by) => kind switch
    {
        NotesChangeKind.Paste => "Pasted from a chat",
        NotesChangeKind.Edit => "Your edit",
        NotesChangeKind.Restore => "Put back",
        NotesChangeKind.Undo => "Undo",
        NotesChangeKind.Redo => "Redo",
        NotesChangeKind.Rewrite => $"Rewritten by {Who(by)}",
        NotesChangeKind.Diagrams => "Diagrams added",
        NotesChangeKind.File => "Changed in the note's file",
        _ => by.Length > 0 ? $"Written by {by}" : "Your notes",
    };

    static string Who(string by) => by.Length > 0 ? by : Engines.YourAi;

    /// <summary>When a version was made: "Today 3:10 PM", "Yesterday 9:30 AM", "21 Oct 3:10 PM".</summary>
    public static string VersionWhen(string atIso, DateTime now) => ParseUntil(atIso) is { } at
        ? (at.Date == now.Date ? "Today " : at.Date == now.Date.AddDays(-1) ? "Yesterday " : at.ToString(at.Year == now.Year ? "d MMM " : "d MMM yyyy ", CultureInfo.InvariantCulture))
          + at.ToString("h:mm tt", CultureInfo.InvariantCulture)
        : "";

    /// <summary>What putting a version back would change, in a line.</summary>
    public static string DiffSummary(int added, int removed) => (added, removed) switch
    {
        (0, 0) => "The same words as the notes now.",
        _ => "Against the notes now: " + string.Join(", ", new[]
            {
                added > 0 ? $"{added} line{(added == 1 ? "" : "s")} it brings back" : "",
                removed > 0 ? $"{removed} line{(removed == 1 ? "" : "s")} it takes away" : "",
            }.Where(x => x.Length > 0)) + ".",
    };

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

    /// <summary>Save found other notes on the library than the ones the edit started from: nothing was saved, and Save
    /// again is the student's choice.</summary>
    public const string EditNotesChanged = "These notes changed while you were editing: their diagrams arrived, or they were changed on another device or in another app. "
        + "Save again to replace them with yours, or Cancel to see them.";

    /// <summary>Save couldn't reach the library.</summary>
    public const string EditOffline = "Your library isn't answering, so nothing was saved. What you typed is still here.";

    /// <summary>The library is too old to take an edit.</summary>
    public const string EditNeedsNewerLibrary = "Your library runs an older Syft Notes. Update it to edit notes here.";

    /// <summary>Why the library wouldn't save an edit, as the editor says it (what was typed stays).</summary>
    public static string EditRefused(int status, string message) => status switch
    {
        409 => "Your library is writing this lecture's notes right now. Save again when they're done.",
        503 => "Your library couldn't save this note's file: another app may have it open. Close it there, then save again.",
        _ when message.Length > 0 => char.ToUpperInvariant(message[0]) + message[1..],
        _ => "Your library couldn't save these notes.",
    };

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

    public static string ProblemTitle(string kind, string engineName, string fallbackName, bool here = false) => kind switch
    {
        "engine_offline" => $"{engineName} isn't running on {Where(here)}",
        "not_signed_in" => here ? $"Sign in to {engineName}" : $"Sign in to {engineName} on your library",
        "model_missing" => $"{engineName} needs its notes model",
        "usage_limit" => $"{engineName} hit its usage limit",
        "fell_back" => $"This answer came from {engineName}",
        "access_request" => $"{engineName} wants to read your library",
        "library_offline" => "Your library isn't answering",
        "rewrite_failed" => $"{engineName} couldn't rewrite the notes",
        _ => engineName,
    };

    public static string ProblemMessage(string kind, string engineName, string fallbackName, string until, double sizeGb, string detail, bool here = false) => kind switch
    {
        "engine_offline" => "New lectures wait and get their notes when it's back.",
        // No stand-in (fallbackName empty): nothing writes the notes until it's signed in. The library writes the ones it
        // missed once it is.
        "not_signed_in" => fallbackName.Length > 0 ? $"Until then, questions go to {fallbackName}."
            : "Until then, lectures are filed without notes. They're written once you've signed in.",
        "model_missing" => sizeGb > 0 ? $"About {sizeGb:0} GB, downloaded once on {Where(here)}." : $"Downloaded once on {Where(here)}.",
        "usage_limit" => ParseUntil(until) is { } t ? $"Questions go to {fallbackName} until {UntilClock(t)}." : $"Questions go to {fallbackName}.",
        "fell_back" => detail,
        "access_request" => $"From {detail}. It can read, not change.",
        "library_offline" => $"Your AI runs on {Where(here)}. Check it's on and connected.",
        "rewrite_failed" => $"{detail} Your current notes are unchanged.",
        _ => detail,
    };

    // --- signing in from Syft Notes: the AI's own sign-in runs hidden and opens the browser itself --------------------

    /// <summary>What a row says while the student finishes signing in, in their browser.</summary>
    public const string SignInWaiting = "Waiting for you to finish in your browser…";

    /// <summary>A row's line (or a card's message) for each place a sign-in can be at (<see cref="SignInPhase"/>); ""
    /// when none is going.</summary>
    public static string SignInAbout(string engineName, string phase) => phase switch
    {
        SignInPhase.Waiting => SignInWaiting,
        SignInPhase.GaveUp => "It stopped waiting before you'd finished. Try again.",
        SignInPhase.Region => $"{engineName} isn't offered in your country yet.",
        SignInPhase.Stuck => SignInStuckWords(engineName),
        _ => "",
    };

    /// <summary>Syft Notes' own help page: where Get help goes when it couldn't start an AI's sign-in, or install it.</summary>
    public const string HelpPage = "https://syftnotes.com/support/";

    /// <summary>A sign-in Syft Notes couldn't start (or that stopped at once): said plainly, with Try again and Get help
    /// beside it, and never a terminal to do it in instead.</summary>
    public static string SignInStuckWords(string engineName) => $"Syft Notes couldn't start {engineName}'s sign-in.";

    /// <summary>The line under the list of AIs once a sign-in has stopped, so it says what the row says rather than the
    /// "Finish signing in…" the press answered with.</summary>
    public static string SignInSaid(string engineName, string phase) => phase switch
    {
        SignInPhase.GaveUp => $"Signing in to {engineName} stopped before you'd finished. Try again.",
        SignInPhase.Region => $"{engineName} isn't offered in your country yet.",
        SignInPhase.Stuck => SignInStuckWords(engineName),
        _ => "",
    };

    /// <summary>A card's title for each place a sign-in can be at; "" when none is going.</summary>
    public static string SignInTitle(string engineName, string phase) => phase switch
    {
        SignInPhase.Waiting => $"Finish signing in to {engineName}",
        SignInPhase.GaveUp => $"Signing in to {engineName} stopped",
        SignInPhase.Region => $"{engineName} isn't offered in your country",
        SignInPhase.Stuck => $"{engineName}'s sign-in didn't start",
        _ => "",
    };

    /// <summary>The small link beside a sign-in: the page again while it waits, Syft Notes' help page when it couldn't
    /// start.</summary>
    public static string SignInLink(string phase) => phase switch
    {
        SignInPhase.Waiting => "Open the page again",
        SignInPhase.Stuck => "Get help",
        _ => "",
    };

    /// <summary>The button for a sign-in that stopped (try once more); none while one waits, and none for a country that
    /// isn't served.</summary>
    public static string SignInButton(string phase) => phase switch
    {
        SignInPhase.GaveUp or SignInPhase.Stuck => "Try again",
        _ => "",
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
