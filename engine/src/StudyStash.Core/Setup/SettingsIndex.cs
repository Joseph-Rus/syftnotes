using System.Text;

namespace StudyStash.Core.Setup;

/// <summary>
/// One thing in the Settings window: the section it's in (the sidebar's id), its title as Settings shows it (the
/// window finds the row by it), a line on what it does, other words a student might call it by, and, for one the
/// settings helper may change itself, its <see cref="Key"/> and what it takes. <see cref="Only"/> keeps it to the
/// computers that show it: "records" (just this computer, or a laptop), "laptop", "library" (a computer that keeps
/// the library), or "" for every one. A page's own entry (<see cref="IsPage"/>) has the section's name as its title.
/// </summary>
public sealed record SettingEntry
{
    public required string Section { get; init; }
    public required string Title { get; init; }
    public string Detail { get; init; } = "";
    public string Words { get; init; } = "";
    public string Key { get; init; } = "";
    /// <summary>What change_setting takes for it, as the AI reads it ("on or off").</summary>
    public string Takes { get; init; } = "";
    public string Only { get; init; } = "";
    /// <summary>A shorter name, for a chip under the helper's words, when the title is long.</summary>
    public string Short { get => shortName ?? Title; init => shortName = value; }
    readonly string? shortName;
    public bool IsPage { get; init; }
    public bool Changeable => Key.Length > 0;
}

/// <summary>
/// Everything in Settings that search finds, and that the settings helper finds, opens and (some of it) changes: each
/// page and the rows on it, in the sidebar's order, with the words Settings shows. A test checks every title is on
/// its page.
/// </summary>
public static class SettingsIndex
{
    /// <summary>The sidebar's sections: id and name, in its order (Recording, Calendars and Granola only on a computer
    /// that records).</summary>
    public static readonly IReadOnlyList<(string Id, string Name)> Sections =
    [
        ("General", "General"), ("Appearance", "Appearance"), ("Recording", "Recording"), ("Calendars", "Calendars"), ("Phone", "Phone"),
        ("Classes", "Classes"), ("Notes", "Notes"), ("AI", "Your AI"), ("Canvas", "Canvas"),
        ("Access", "AI apps"), ("Granola", "Granola"), ("Folders", "Your files"),
        ("Library", "Library"), ("Connection", "Connection"),
    ];

    /// <summary>A section's name as the sidebar shows it ("Notes and sorting"), or the id when there's none.</summary>
    public static string SectionName(string id) => Sections.FirstOrDefault(s => s.Id == id).Name ?? id;

    /// <summary>The section an AI (or a student) means: its id or its name, in any case; null when there's none.</summary>
    public static string? SectionOf(string idOrName)
    {
        string s = (idOrName ?? "").Trim();
        return Sections.FirstOrDefault(x => x.Id.Equals(s, StringComparison.OrdinalIgnoreCase) || x.Name.Equals(s, StringComparison.OrdinalIgnoreCase)).Id;
    }

    const string OnOff = "on or off";

    static SettingEntry Page(string id, string detail, string words = "", string only = "") =>
        new() { Section = id, Title = SectionName(id), Detail = detail, Words = words, Only = only, IsPage = true };

    public static readonly IReadOnlyList<SettingEntry> All =
    [
        Page("General", "How Study Stash starts, setup, and its version.", "startup about version quit"),
        new() { Section = "General", Title = "Open when you log in", Detail = "Study Stash starts in the menu bar or tray when you log in.",
            Words = "start startup login boot launch automatically background", Key = "start_at_login", Short = "Open at login", Takes = OnOff },
        new() { Section = "General", Title = "Update automatically", Detail = "This laptop installs each new version by itself.",
            Words = "updates upgrade new version auto", Key = "auto_update", Short = "Updates", Takes = OnOff, Only = "laptop" },
        new() { Section = "General", Title = "Count this copy", Detail = "Once a day, one anonymous count of this copy.",
            Words = "count anonymous users usage ping daily privacy analytics tracking statistics" },
        new() { Section = "General", Title = "Run setup again", Detail = "The first-run steps, with what's set up already kept.",
            Words = "setup wizard onboarding guided start over" },
        new() { Section = "General", Title = "Quit Study Stash", Words = "exit close stop app" },
        new() { Section = "General", Title = "Credits", Detail = "The speech models and illustration parts Study Stash uses, and their licences.",
            Words = "licenses licences credits about open source" },

        Page("Appearance", "Pick how Study Stash looks.", "look theme"),
        new() { Section = "Appearance", Title = "Mode", Detail = "Match system, light or dark.",
            Words = "dark light mode night theme appearance black white", Key = "appearance", Takes = "system, light or dark" },
        new() { Section = "Appearance", Title = "Colour theme", Detail = "The accent for buttons, the selection and highlights.",
            Words = "color colour accent theme highlight tint", Key = "colour_theme" },

        new() { Section = "General", Title = "Search and Record work from any app", Detail = "The shortcuts work while you're in another app.",
            Words = "global anywhere hotkeys keyboard shortcuts", Key = "shortcuts_anywhere", Short = "Shortcuts from any app", Takes = OnOff },
        new() { Section = "General", Title = "Search your lectures", Detail = "Its keyboard shortcut.", Words = "hotkey keys search panel quick" },
        new() { Section = "General", Title = "Start or stop recording", Detail = "Its keyboard shortcut.", Words = "hotkey keys record", Only = "records" },
        new() { Section = "General", Title = "Ask about what you searched", Detail = "Its keyboard shortcut.", Words = "hotkey keys ask question" },
        new() { Section = "General", Title = "Open Settings", Detail = "Its keyboard shortcut.", Words = "hotkey keys preferences" },

        Page("Recording", "How each lecture is written down on this computer.", "record recorder microphone mic audio transcript", "records"),
        new() { Section = "Recording", Title = "Transcription model", Detail = "The model that turns speech into text on this computer.",
            Words = "whisper parakeet whistle speech text accuracy accurate faster download model", Key = "transcription_model", Only = "records" },
        new() { Section = "Recording", Title = "When it's written down", Detail = "As you record (live transcript), or after class (saves battery).",
            Words = "live transcript battery after class real time", Key = "live_transcript", Short = "When it's written down", Takes = "as_you_record or after_class", Only = "records" },
        new() { Section = "Recording", Title = "Language", Detail = "The language lectures are in, or found for each lecture.",
            Words = "english spanish french german chinese language transcription", Key = "language",
            Takes = "a language code like en, es or fr, or auto to find it for each lecture", Only = "records" },
        new() { Section = "Recording", Title = "Also record what this computer plays (a lecture on Zoom or Teams)",
            Detail = "Records the computer's own sound as well as the microphone.",
            Words = "zoom teams meet online system audio computer sound video call", Key = "computer_audio", Short = "Recording the computer's sound", Takes = OnOff, Only = "records" },
        new() { Section = "Recording", Title = "Tell speakers apart in the transcript", Detail = "Marks where a voice other than the lecturer's speaks.",
            Words = "speakers diarization voices who talking questions students", Key = "speakers", Short = "Telling speakers apart", Takes = OnOff, Only = "records" },
        new() { Section = "Recording", Title = "Keep recordings", Detail = "Days the audio stays after a lecture is filed (0 keeps it).",
            Words = "audio delete storage space disk days recordings", Key = "keep_recordings_days", Short = "Keep recordings", Takes = "a number of days, 0 to keep them", Only = "records" },

        Page("Calendars", "Study Stash reads your calendars to show what's coming up, and names a lecture you record during a class after it.",
            "calendar schedule timetable events", "records"),
        new() { Section = "Calendars", Title = "Add a calendar feed", Detail = "Paste a calendar's address (ics).",
            Words = "ics ical google outlook icloud schedule timetable feed", Only = "records" },

        Page("Granola", "Bring lectures you recorded in Granola over to Study Stash, which files them and writes their notes.",
            "granola import transfer bring move lectures other app recorder", "records"),

        Page("Connection", "Which library this computer uses, and what this computer is for.", "library server network"),
        new() { Section = "Connection", Title = "Connect to a library", Detail = "The library's address and password.",
            Words = "library address password server connect", Only = "laptop" },
        new() { Section = "Connection", Title = "Your name in the library", Detail = "The name your lectures show from this computer.",
            Words = "name display who", Key = "display_name", Takes = "a name, up to 60 characters" },
        new() { Section = "Connection", Title = "This computer", Detail = "What this computer is for: just this computer, a laptop, or the library.",
            Words = "role laptop library just this computer switch become" },

        Page("Library", "Your library: its name, password, laptops and updates.", "server"),
        new() { Section = "Library", Title = "Name", Detail = "The library's name.", Words = "library name rename" },
        new() { Section = "Library", Title = "Password", Detail = "The library's password.", Words = "library password change" },
        new() { Section = "Library", Title = "Laptops", Detail = "Let a laptop connect to this library.", Words = "laptop add another computer connect" },
        new() { Section = "Library", Title = "How your laptop reaches it", Detail = "The addresses a laptop connects to.",
            Words = "address tailscale network reach ip" },
        new() { Section = "Library", Title = "Notes folder", Detail = "Where the library keeps its notes.",
            Words = "folder where files stored location obsidian markdown" },
        new() { Section = "Library", Title = "Update automatically", Detail = "The library installs each new version by itself.",
            Words = "updates upgrade new version auto library", Key = "library_auto_update", Short = "Library updates", Takes = OnOff, Only = "laptop" },
        // The library is this computer: its updates are the app's, in General.
        new() { Section = "General", Title = "Update automatically", Detail = "Study Stash installs each new version by itself.",
            Words = "updates upgrade new version auto library", Key = "library_auto_update", Short = "Updates", Takes = OnOff, Only = "library" },

        Page("Classes", "The classes your library files lectures into.", "course courses subject subjects add rename remove aliases"),
        new() { Section = "Classes", Title = "Use Canvas course names", Detail = "Renames your classes to their Canvas courses' names.",
            Words = "canvas course names rename" },

        Page("Notes", "How your library writes the notes for each lecture and sorts it into a class.", "summary summaries sort"),
        new() { Section = "Notes", Title = "Write study notes from each lecture", Detail = "Notes are written after each lecture.",
            Words = "notes summaries summary write", Key = "write_notes", Short = "Writing notes", Takes = OnOff },
        new() { Section = "Notes", Title = "Sort lectures into classes with AI", Detail = "The AI reads a lecture to pick its class.",
            Words = "sort file classify unsorted automatic", Key = "sort_with_ai", Short = "Sorting with AI", Takes = OnOff },
        new() { Section = "Notes", Title = "Sorts them with", Detail = "The AI that reads a lecture to pick its class.", Words = "sorting engine model" },
        new() { Section = "Notes", Title = "Files a lecture when the AI is at least", Detail = "Less sure, and the lecture waits in Unsorted.",
            Words = "confidence sure threshold unsorted", Key = "sort_confidence", Short = "Sorting confidence", Takes = "40, 50, 60, 70, 80 or 90 (percent sure)" },
        new() { Section = "Notes", Title = "Writes the notes", Detail = "The local model, when Ollama writes them.", Words = "ollama local model offline" },
        new() { Section = "Notes", Title = "Opens a class with Claude Code, Codex or Gemini in", Detail = "The terminal a class opens in.",
            Words = "terminal iterm warp ghostty claude code codex gemini" },
        new() { Section = "Notes", Title = "Rewrite every summary", Detail = "Writes every lecture's notes again.", Words = "rewrite regenerate redo all notes summaries" },

        Page("AI", "Which AI writes your notes and answers your questions.", "claude chatgpt codex gemini ollama engine provider model"),
        new() { Section = "AI", Title = "Writes your notes", Detail = "The AI that writes the notes after each lecture.",
            Words = "claude chatgpt codex gemini ollama engine model provider who writes" },
        new() { Section = "AI", Title = "Answers your questions", Detail = "The AI behind the ask bar, the recorder's chat and the quick panel.",
            Words = "ask chat questions answers engine" },
        new() { Section = "AI", Title = "If your AI isn’t available", Detail = "What answers when your AI can't.", Words = "fallback backup ollama offline limit" },
        new() { Section = "AI", Title = "API keys", Detail = "Keys for an AI used through its API.", Words = "api key openai anthropic gemini token" },
        new() { Section = "AI", Title = "Rich notes", Detail = "Diagrams, drawings and formula plots in the notes.",
            Words = "diagrams drawings plots pictures figures illustrations mermaid" },

        Page("Access", "ChatGPT, Claude and other AI apps can read your lectures and notes to answer your questions, and save notes you ask them to write.",
            "mcp connector claude desktop chatgpt app permissions"),
        new() { Section = "Access", Title = "AI apps on this computer", Detail = "Connect the ChatGPT or Claude app to your library.",
            Words = "claude desktop chatgpt app mcp connect connector" },
        new() { Section = "Access", Title = "What they can read", Detail = "What AI apps may read: lectures, notes, recordings, Canvas.",
            Words = "privacy permissions read access" },
        new() { Section = "Access", Title = Ai.ToolAccess.SaveToggle, Detail = "Let AI apps save the notes you ask them to write for a lecture.",
            Words = "save write notes free claude app no paid plan subscription" },
        new() { Section = "Access", Title = "Let Claude and ChatGPT reach your library from the internet", Detail = "For claude.ai, chatgpt.com and their phone apps.",
            Words = "web phone claude.ai chatgpt.com tailscale funnel remote internet" },

        Page("Canvas", "Bring your Canvas courses in as classes. Study Stash only reads from Canvas.", "lms instructure school courses assignments"),
        new() { Section = "Canvas", Title = "School", Detail = "Your school's Canvas address.", Words = "canvas school instructure address" },
        new() { Section = "Canvas", Title = "Courses to bring in", Detail = "Which Canvas courses become classes and sync.", Words = "courses sync classes" },
        new() { Section = "Canvas", Title = "Change browser", Detail = "The browser Study Stash's helper is in.", Words = "browser chrome edge extension helper" },

        Page("Folders", "Folders on the library's computer that search, and the AI when you chat, may read.",
            "files documents search private reads slides pdfs"),

        Page("Phone", "Read your notes and lectures, search and ask, and add files from your phone.",
            "iphone android mobile qr code home screen app google drive gmail"),
        new() { Section = "Phone", Title = "Add a phone", Detail = "Scan the code in the Study Stash app on your iPhone or iPad.", Words = "iphone android mobile qr code" },
    ];

    /// <summary>The entry change_setting knows by <paramref name="key"/>, or null.</summary>
    /// <summary>The setting with this key, as this computer shows it (one setting can sit on a different page on a
    /// laptop than where the library is: the library's updates are in General when it's this computer).</summary>
    public static SettingEntry? ByKey(string key, Func<SettingEntry, bool>? shown = null) =>
        All.FirstOrDefault(e => e.Changeable && e.Key == key && (shown?.Invoke(e) ?? true))
        ?? All.FirstOrDefault(e => e.Changeable && e.Key == key);

    /// <summary>Words that say nothing about which setting: left out of a search (unless they're all there is).</summary>
    static readonly HashSet<string> Filler =
    [
        "a", "an", "the", "to", "my", "i", "me", "it", "its", "is", "be", "do", "how", "can", "where", "what", "which", "of", "in", "on", "off",
        "for", "and", "or", "with", "from", "this", "that", "want", "please", "change", "set", "turn", "make", "setting", "settings", "find",
        "go", "show", "open", "study", "stash", "you", "your", "would", "like", "get", "use", "using",
    ];

    /// <summary>Lower case, curly quotes made straight, and anything that isn't a letter or digit a space.</summary>
    static string[] Tokens(string s)
    {
        var b = new StringBuilder(s.Length);
        foreach (char c in s.ToLowerInvariant().Replace('’', '\'')) b.Append(char.IsLetterOrDigit(c) ? c : ' ');
        return b.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries);
    }

    static bool Hits(string q, string[] field) =>
        field.Any(w => w.StartsWith(q, StringComparison.Ordinal) || q.Length >= 4 && w.Contains(q, StringComparison.Ordinal));

    /// <summary>
    /// The entries that best match <paramref name="query"/>, most likely first, among those <paramref name="shown"/>
    /// lets through: every word found (in its title, its section's name, what it does, or the other words it goes by)
    /// before any that match only some; a word in the title counts most, and a title that starts with the whole query
    /// most of all. Each word matches the start of a word there (or, from four letters, any part of one).
    /// </summary>
    public static IReadOnlyList<SettingEntry> Search(string query, Func<SettingEntry, bool>? shown = null, int max = 8)
    {
        var all = Tokens(query ?? "");
        var words = all.Where(w => !Filler.Contains(w)).ToArray();
        if (words.Length == 0) words = all;
        if (words.Length == 0) return [];
        string whole = string.Join(' ', all);
        var scored = new List<(SettingEntry Entry, int Found, int Score, int Order)>();
        for (int i = 0; i < All.Count; i++)
        {
            var e = All[i];
            if (shown is not null && !shown(e)) continue;
            string[] title = Tokens(e.Title), section = Tokens(SectionName(e.Section)), other = Tokens(e.Detail + " " + e.Words);
            int found = 0, score = 0;
            foreach (string w in words)
            {
                int best = Hits(w, title) ? 6 : Hits(w, section) ? 3 : Hits(w, other) ? 2 : 0;
                if (best > 0) found++;
                score += best;
            }
            if (found == 0) continue;
            string t = string.Join(' ', title);
            if (t.StartsWith(whole, StringComparison.Ordinal)) score += 10;
            else if (whole.Length >= 3 && t.Contains(whole, StringComparison.Ordinal)) score += 5;
            scored.Add((e, found, score, i));
        }
        // Every word found comes first; some of them only when nothing has them all, and then at least half.
        int most = scored.Count == 0 ? 0 : scored.Max(s => s.Found);
        int need = most == words.Length ? most : Math.Max(1, (words.Length + 1) / 2);
        return [.. scored.Where(s => s.Found >= need).OrderByDescending(s => s.Found).ThenByDescending(s => s.Score).ThenBy(s => s.Order).Take(max).Select(s => s.Entry)];
    }

    /// <summary>Whether <paramref name="e"/> shows on a computer that records (or not), as a laptop (or not).</summary>
    public static bool ShownOn(SettingEntry e, bool records, bool laptop) => e.Only switch
    {
        "records" => records,
        "laptop" => laptop,
        "library" => !laptop,
        _ => true,
    };
}
