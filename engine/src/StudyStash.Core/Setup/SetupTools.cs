using System.ComponentModel;
using System.Text;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using StudyStash.Core.Ai;

namespace StudyStash.Core.Setup;

/// <summary>Where a checklist item has got to. Only Study Stash sets these, from what's really so.</summary>
public enum ChecklistState
{
    Todo,
    /// <summary>Under way (the model downloading) or showing now.</summary>
    Now,
    Done,
    /// <summary>An optional step the student left for later.</summary>
    Skipped,
    Problem,
}

/// <summary>One line of guided setup's checklist: "Microphone", done or not, with a second line of detail
/// ("Downloading · 42%").</summary>
public sealed record ChecklistItem(string Id, string Title, ChecklistState State, string Detail = "", bool Optional = false, bool Recommended = false)
{
    public bool Done => State == ChecklistState.Done;
}

/// <summary>What's true of setup right now, as the setup tools tell the AI: this computer, what it's for, the
/// checklist, and whether it's ready to finish. Never a password or a token.</summary>
public sealed record SetupStatus
{
    public bool Windows { get; init; }
    /// <summary>Setup run again from Settings: what this computer is for stays as it is.</summary>
    public bool Again { get; init; }
    /// <summary>"" (not chosen yet), "one" (just this computer), "laptop" or "library".</summary>
    public string Role { get; init; } = "";
    /// <summary>"library" when the student downloaded the library installer.</summary>
    public string Suggests { get; init; } = "";
    public IReadOnlyList<ChecklistItem> Items { get; init; } = [];
    public bool ReadyToFinish { get; init; }
    /// <summary>A library's addresses a laptop connects to.</summary>
    public IReadOnlyList<string> Addresses { get; init; } = [];
    /// <summary>Who writes the notes, as the student would say it ("Claude"), or "".</summary>
    public string NotesWriter { get; init; } = "";
    /// <summary>What the card on screen is in the middle of ("connecting to the library"), or null.</summary>
    public string? Busy { get; init; }

    public string Device => Windows ? "PC" : "Mac";
    /// <summary>This computer records lectures (just this computer, or a laptop).</summary>
    public bool Records => Role is "one" or "laptop";
    public IEnumerable<ChecklistItem> Left => Items.Where(i => !i.Optional && !i.Done && !(i.Id == "model" && i.State == ChecklistState.Now));
}

/// <summary>A transcription model the student could download: its id, name, size, whether it's here, and whether
/// it's the one marked for this computer (and why).</summary>
public sealed record SetupModelOption(string Id, string Name, string Size, bool Downloaded, bool Marked, string Why = "");

/// <summary>The courses the student's browser found on Canvas, or why there are none yet.</summary>
public sealed record SetupCourses(bool Connected, IReadOnlyList<(string Name, string Code)> Found, string Why = "");

/// <summary>A card the AI asks the setup window to show: the student acts in it. <see cref="Arg"/> is the one thing
/// it's given (a model id, a school's Canvas address, a suggested choice).</summary>
public sealed record SetupCard(string Kind, string Arg = "");

/// <summary>
/// What the setup tools reach: guided setup's window. Reading what's so, showing a question or a card, and the few
/// direct changes to the library's own data (a class, who writes the notes, a step left for later), each reversible
/// in Settings. Nothing here changes the computer: a card does that when the student presses its button.
/// </summary>
public interface ISetupDriver
{
    Task<SetupStatus> StatusAsync();
    Task<IReadOnlyList<SetupModelOption>> ModelsAsync();
    Task<SetupCourses> CoursesAsync();
    /// <summary>Shows a question with buttons to tap.</summary>
    Task AskAsync(string question, IReadOnlyList<string> choices);
    /// <summary>Shows a card. Null when it's showing; else why not, in words.</summary>
    Task<string?> OfferAsync(SetupCard card);
    /// <summary>Adds a class to the library. Null when it's in; else why not.</summary>
    Task<string?> AddClassAsync(string name, string about);
    /// <summary>Who writes the notes: "claude", "codex", "ollama" or "none". Null when set; else why not.</summary>
    Task<string?> SetNotesWriterAsync(string engine);
    /// <summary>Leaves an optional step for later. Null when done; else why not.</summary>
    Task<string?> SkipAsync(string step);
    /// <summary>Hands over to setup by hand, at <paramref name="step"/> (a checklist id) or the first not done.</summary>
    Task OpenManualAsync(string step);
}

/// <summary>
/// The setup tools (MCP server <c>study_stash_setup</c>) guided setup's AI works through, over an
/// <see cref="ISetupDriver"/>. Each answers in short plain text the AI reads, and says no in words, never by failing:
/// arguments too long, too many calls in one turn, a card while another is mid-action, a step that doesn't apply.
/// A tool never changes the computer: <c>offer_…</c> only shows a card.
/// </summary>
public sealed class SetupTools(ISetupDriver driver)
{
    public const int MaxCallsPerTurn = 12;
    public const int MaxClasses = 30;
    int calls, classes;

    /// <summary>A new turn of the chat: its calls are counted afresh.</summary>
    public void NewTurn() => Interlocked.Exchange(ref calls, 0);

    /// <summary>What each tool does: "read" (tells), "card" (shows the student something; changes nothing), or
    /// "direct" (changes the library's own data, or where setup is). A test fails when a tool isn't named here.</summary>
    public static string? KindOf(string tool) => tool switch
    {
        "get_setup_status" or "list_transcription_models" or "get_canvas_courses" => "read",
        "ask_student" or "offer_computer_setup" or "offer_library_password" or "offer_library_connection" or "offer_microphone_check"
            or "offer_model_download" or "offer_chrome_helper" or "offer_course_picker" or "offer_start_at_login" or "offer_taskbar_tip"
            or "offer_finish" => "card",
        "add_class" or "set_notes_writer" or "skip_step" or "open_manual_setup" => "direct",
        _ => null,
    };

    public static readonly string[] Roles = ["one_computer", "laptop", "library"];
    public static readonly string[] Writers = ["claude", "codex", "ollama", "none"];
    public static readonly string[] Skippable = ["canvas", "classes", "start_at_login", "taskbar"];

    static CallToolResult Say(string text) => new() { Content = [new TextContentBlock { Text = text }] };
    static CallToolResult No(string text) => new() { IsError = true, Content = [new TextContentBlock { Text = text }] };

    const string Wait = "Stop here and wait for the student: a [Study Stash] message will say what they did.";

    static McpServerToolCreateOptions Named(string name, string title, string description) =>
        new() { Name = name, Title = title, Description = description, ReadOnly = KindOf(name) != "direct", Destructive = false, Idempotent = KindOf(name) != "direct", OpenWorld = false };

    /// <summary>Every tool, counted: past <see cref="MaxCallsPerTurn"/> in one turn each says so instead.</summary>
    public List<McpServerTool> Tools() => [.. Made().Select(t => (McpServerTool)new Counted(t, this))];

    sealed class Counted(McpServerTool inner, SetupTools owner) : DelegatingMcpServerTool(inner)
    {
        public override async ValueTask<CallToolResult> InvokeAsync(RequestContext<CallToolRequestParams> request, CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref owner.calls) > MaxCallsPerTurn) return No("Too many steps at once: ask the student first.");
            try
            {
                return await base.InvokeAsync(request, cancellationToken);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                return No($"Study Stash couldn't do that just now ({e.Message.TrimEnd('.')}). Tell the student, and offer to finish by hand.");
            }
        }
    }

    List<McpServerTool> Made() =>
    [
        McpServerTool.Create(StatusAsync, Named("get_setup_status", "Check setup",
            "What's true of setup right now: this computer, what it's for, each checklist item and its state, whether it's ready to "
            + "finish and what's left. It's the truth: call it first, and whenever you're unsure what's done.")),
        McpServerTool.Create(ModelsAsync, Named("list_transcription_models", "List transcription models",
            "The models that turn speech into text on this computer: id, name, size, whether it's downloaded, and the one marked for this "
            + "computer and why. offer_model_download takes the id.")),
        McpServerTool.Create(CoursesAsync, Named("get_canvas_courses", "List Canvas courses",
            "The courses the student's browser found on Canvas (name and code), or why there are none yet.")),
        McpServerTool.Create(
            ([Description("The question, in one or two short sentences (up to 300 characters).")] string question,
             [Description("2 to 4 short answers to tap (each up to 40 characters).")] string[] choices) => AskAsync(question, choices),
            Named("ask_student", "Ask the student",
                "Shows your question with buttons to tap (the student may still type). Use it when a question has a few obvious answers. "
                + "End your turn after it.")),
        McpServerTool.Create(
            ([Description("The choice to start on: one_computer, laptop or library. Leave out for one_computer.")] string? suggested = null) =>
                OfferComputerAsync(suggested),
            Named("offer_computer_setup", "Offer: how you'll use it",
                "Shows the card where the student picks how they'll use Study Stash: just this computer, a laptop (library elsewhere), or the "
                + "library (a computer that stays on). They press Set up; just this computer also makes the library here.")),
        McpServerTool.Create(() => OfferAsync("library_password"), Named("offer_library_password", "Offer: library password",
            "A library: shows the card where the student names the library and gives it a password, then creates it. The password never "
            + "reaches you.")),
        McpServerTool.Create(
            ([Description("The library's address, if the student said it (like mac-mini:8787). Leave out otherwise.")] string? address = null) =>
                OfferAsync("library_connection", address ?? ""),
            Named("offer_library_connection", "Offer: connect to your library",
                "A laptop: shows the card where the student finds or types the library's address and its password, and connects. The password "
                + "never reaches you.")),
        McpServerTool.Create(() => OfferAsync("microphone_check"), Named("offer_microphone_check", "Offer: microphone check",
            "A computer that records: shows the microphone card (the computer asks to allow it; live bars show Study Stash hears the student).")),
        McpServerTool.Create(
            ([Description("The model's id, from list_transcription_models.")] string model_id) => OfferModelAsync(model_id),
            Named("offer_model_download", "Offer: download a model",
                "A computer that records: shows the card to download that model (its name, size and why). The student presses Download; it "
                + "downloads in the background.")),
        McpServerTool.Create(
            ([Description("The address the student opens Canvas at, like school.instructure.com.")] string school_address) =>
                OfferCanvasAsync(school_address),
            Named("offer_chrome_helper", "Offer: connect Canvas through the browser",
                "Checks the school's Canvas address, then shows the card that adds Study Stash's helper to the student's browser (the card names "
                + "the browser and shows each step). Study Stash says when the browser is connected.")),
        McpServerTool.Create(() => OfferCoursesAsync(), Named("offer_course_picker", "Offer: pick your courses",
            "Once the student's browser is connected: shows the ticked list of courses Canvas found; \"Add these classes\" makes them classes.")),
        McpServerTool.Create(() => OfferAsync("start_at_login"), Named("offer_start_at_login", "Offer: start at login",
            "Just this computer or a library: asks whether Study Stash starts when the student logs in (recommended, so notes get written).")),
        McpServerTool.Create(() => OfferAsync("taskbar_tip"), Named("offer_taskbar_tip", "Offer: taskbar tip",
            "Windows, a computer that records: shows how to keep Study Stash's icon on the taskbar.")),
        McpServerTool.Create(() => OfferAsync("finish"), Named("offer_finish", "Offer: finish",
            "When get_setup_status says ready_to_finish: shows a short recap and the Open Study Stash button.")),
        McpServerTool.Create(
            ([Description("The class's name, like BIO 110 (up to 80 characters).")] string name,
             [Description("What it covers, in the student's words (up to 300 characters). It helps file each lecture.")] string? about = null) =>
                AddClassAsync(name, about ?? ""),
            Named("add_class", "Add a class",
                "Adds a class to the student's library, with what it covers. The student sees it at once and can change it in Settings → Classes.")),
        McpServerTool.Create(
            ([Description("claude, codex, ollama, or none (transcripts only).")] string engine) => WriterAsync(engine),
            Named("set_notes_writer", "Choose who writes the notes",
                "Who writes the study notes and answers questions: claude, codex, ollama, or none. Refused for one that isn't ready here.")),
        McpServerTool.Create(
            ([Description("canvas, classes, start_at_login or taskbar.")] string step) => SkipAsync(step),
            Named("skip_step", "Leave a step for later", "Marks an optional step as left for later, when the student says so.")),
        McpServerTool.Create(
            ([Description("The checklist item to start at (from get_setup_status). Leave out for the first one not done.")] string? step = null) =>
                ManualAsync(step ?? ""),
            Named("open_manual_setup", "Set up by hand",
                "Hands over to Study Stash's own setup pages, for a student who'd rather do it by hand. The chat stays; they can come back.")),
    ];

    // --- reading ------------------------------------------------------------------------------------------------------

    async Task<CallToolResult> StatusAsync() => Say(Describe(await driver.StatusAsync()));

    /// <summary>The status as the AI reads it.</summary>
    public static string Describe(SetupStatus s)
    {
        var b = new StringBuilder();
        b.Append("Computer: ").AppendLine(s.Device);
        b.AppendLine(s.Again ? "Setup: run again from Settings. What this computer is for stays as it is (Settings → Connection changes it)." : "Setup: first run.");
        b.Append("How it's used: ").AppendLine(s.Role switch
        {
            "one" => $"just this {s.Device}",
            "laptop" => "a laptop that records (the library is on another computer)",
            "library" => "the library (a computer that stays on; it doesn't record)",
            _ => "not chosen yet",
        });
        if (s.Suggests == "library" && s.Role.Length == 0) b.AppendLine("installer_suggests: library (the student downloaded the library installer)");
        b.AppendLine("Checklist:");
        foreach (var i in s.Items)
        {
            b.Append("- ").Append(i.Id).Append(" · ").Append(i.Title).Append(": ").Append(i.State switch
            {
                ChecklistState.Done => "done",
                ChecklistState.Now => "under way",
                ChecklistState.Skipped => "left for later",
                ChecklistState.Problem => "problem",
                _ => "to do",
            });
            if (i.Optional) b.Append(" (optional)");
            else if (i.Recommended) b.Append(" (recommended)");
            if (i.Detail.Length > 0) b.Append(" · ").Append(i.Detail);
            b.AppendLine();
        }
        if (s.NotesWriter.Length > 0) b.Append("Notes are written by: ").AppendLine(s.NotesWriter);
        if (s.Addresses.Count > 0) b.Append("A laptop connects to this library at: ").AppendLine(string.Join(" or ", s.Addresses));
        if (s.Busy is { } busy) b.Append("The card on screen is busy: ").AppendLine(busy);
        b.Append("ready_to_finish: ").AppendLine(s.ReadyToFinish ? "yes" : "no");
        var left = s.Left.Select(i => i.Title).ToList();
        if (left.Count > 0) b.Append("Still to do: ").AppendLine(string.Join(", ", left));
        return b.ToString().TrimEnd();
    }

    async Task<CallToolResult> ModelsAsync()
    {
        var models = await driver.ModelsAsync();
        if (models.Count == 0) return Say("No models to offer here.");
        var b = new StringBuilder();
        foreach (var m in models)
        {
            b.Append("- ").Append(m.Id).Append(": ").Append(m.Name).Append(", ").Append(m.Size).Append(m.Downloaded ? ", downloaded" : ", not downloaded");
            if (m.Marked) b.Append(". Marked for this computer").Append(m.Why.Length > 0 ? ": " + m.Why.TrimEnd('.') : "");
            b.AppendLine(".");
        }
        return Say(b.ToString().TrimEnd());
    }

    async Task<CallToolResult> CoursesAsync()
    {
        var c = await driver.CoursesAsync();
        if (c.Found.Count == 0) return Say(c.Why.Length > 0 ? c.Why : c.Connected ? "The student's browser is connected, but Canvas hasn't shown any courses yet." : "The student's browser isn't connected yet.");
        return Say($"Canvas found {c.Found.Count} course{(c.Found.Count == 1 ? "" : "s")}:\n"
            + string.Join('\n', c.Found.Select(f => "- " + f.Name + (f.Code.Length > 0 && !f.Name.Contains(f.Code, StringComparison.OrdinalIgnoreCase) ? $" ({f.Code})" : ""))));
    }

    // --- cards -----------------------------------------------------------------------------------------------------------

    async Task<CallToolResult> AskAsync(string question, string[]? choices)
    {
        question = (question ?? "").Trim();
        var list = (choices ?? []).Select(c => (c ?? "").Trim()).Where(c => c.Length > 0).ToList();
        if (question.Length == 0) return No("Say the question.");
        if (question.Length > 300) return No("That question is too long: keep it under 300 characters.");
        if (list.Count is < 2 or > 4) return No("Give 2 to 4 answers to tap.");
        if (list.Any(c => c.Length > 40)) return No("Keep each answer under 40 characters.");
        await driver.AskAsync(question, list);
        return Say("The question is showing with its buttons. End your turn now and wait for the student's answer.");
    }

    /// <summary>What stops any card now: another mid-action, or a step that isn't this computer's.</summary>
    static string? Unready(SetupStatus s, string card) => s.Busy is { } busy
        ? $"Not now: the card on screen is busy ({busy}). Wait for the [Study Stash] message about it."
        : card switch
        {
            "library_password" when s.Role != "library" => "That card is only for a library. Check get_setup_status: " + RoleWords(s),
            "library_connection" when s.Role != "laptop" => "That card is only for a laptop connecting to its library. " + RoleWords(s),
            "microphone_check" or "model_download" when !s.Records =>
                s.Role.Length == 0 ? FirstRole : "A library doesn't record, so it doesn't need that.",
            "start_at_login" when s.Role is not ("one" or "library") =>
                s.Role.Length == 0 ? FirstRole : "A laptop doesn't need to start at login.",
            "taskbar_tip" when !s.Windows || !s.Records => "The taskbar tip is only for a Windows PC that records.",
            "finish" when !s.ReadyToFinish => "Not yet. Still to do: " + string.Join(", ", s.Left.Select(i => i.Title)) + ".",
            _ => null,
        };

    const string FirstRole = "First ask how they'll use Study Stash (offer_computer_setup).";

    static string RoleWords(SetupStatus s) => s.Role.Length == 0 ? FirstRole : $"this computer is set up as {(s.Role == "one" ? "just this " + s.Device : "a " + s.Role)}.";

    static string Shown(string what) => $"Showing the {what} card. {Wait}";

    static string CardWords(string kind) => kind switch
    {
        "computer_setup" => "how-you'll-use-it",
        "library_password" => "library password",
        "library_connection" => "connect-to-your-library",
        "microphone_check" => "microphone",
        "model_download" => "model download",
        "chrome_helper" => "browser helper",
        "course_picker" => "course picker",
        "start_at_login" => "start at login",
        "taskbar_tip" => "taskbar",
        _ => "finish",
    };

    async Task<CallToolResult> OfferAsync(string kind, string arg = "")
    {
        if (arg.Length > 200) return No("That's too long: keep it under 200 characters.");
        var s = await driver.StatusAsync();
        if (Unready(s, kind) is { } why) return No(why);
        return await driver.OfferAsync(new SetupCard(kind, arg.Trim())) is { } refused ? No(refused) : Say(Shown(CardWords(kind)));
    }

    async Task<CallToolResult> OfferComputerAsync(string? suggested)
    {
        string choice = (suggested ?? "").Trim();
        if (choice.Length > 0 && !Roles.Contains(choice)) return No("suggested is one_computer, laptop or library.");
        var s = await driver.StatusAsync();
        if (s.Again) return No("To change what this computer is for, use Settings → Connection. Setup run again keeps it.");
        return await OfferAsync("computer_setup", choice);
    }

    async Task<CallToolResult> OfferModelAsync(string modelId)
    {
        string id = (modelId ?? "").Trim();
        if (id.Length is 0 or > 80) return No("Give a model's id from list_transcription_models.");
        var s = await driver.StatusAsync();
        if (s.Role == "library") return No("A library doesn't record, so it doesn't need a model.");
        var models = await driver.ModelsAsync();
        if (models.All(m => m.Id != id))
            return No($"There's no model called \"{id}\". list_transcription_models gives the ids: {string.Join(", ", models.Select(m => m.Id))}.");
        return await OfferAsync("model_download", id);
    }

    async Task<CallToolResult> OfferCanvasAsync(string address)
    {
        string a = (address ?? "").Trim();
        if (a.Length == 0) return No("Ask the student for the address they open Canvas at (like school.instructure.com) first.");
        if (a.Length > 200 || a.Any(char.IsWhiteSpace)) return No("That doesn't look like an address. Ask for the one they open Canvas at, like school.instructure.com.");
        return await OfferAsync("chrome_helper", a);
    }

    async Task<CallToolResult> OfferCoursesAsync()
    {
        var c = await driver.CoursesAsync();
        if (c.Found.Count == 0) return No(c.Why.Length > 0 ? c.Why : "The student's browser isn't connected yet: offer_chrome_helper first, then wait for Study Stash to say it's connected.");
        return await OfferAsync("course_picker");
    }

    // --- direct -----------------------------------------------------------------------------------------------------------

    async Task<CallToolResult> AddClassAsync(string name, string about)
    {
        name = (name ?? "").Trim();
        about = about.Trim();
        if (name.Length == 0) return No("Give the class's name.");
        if (name.Length > 80) return No("That class name is too long: keep it under 80 characters.");
        if (about.Length > 300) return No("Keep what it covers under 300 characters.");
        if (Volatile.Read(ref classes) >= MaxClasses) return No($"That's {MaxClasses} classes in this setup: the student can add more in Settings → Classes.");
        if (await driver.AddClassAsync(name, about) is { } refused) return No(refused);
        Interlocked.Increment(ref classes);
        return Say($"Added {name}. The student sees it in the checklist now.");
    }

    async Task<CallToolResult> WriterAsync(string engine)
    {
        engine = (engine ?? "").Trim().ToLowerInvariant();
        if (!Writers.Contains(engine)) return No("engine is claude, codex, ollama or none.");
        if (await driver.SetNotesWriterAsync(engine) is { } refused) return No(refused);
        return Say(engine == "none" ? "Notes are off for now: lectures keep their transcripts." : $"{(engine == "ollama" ? "Ollama" : AgentCli.Get(engine).Brand)} writes the notes now.");
    }

    async Task<CallToolResult> SkipAsync(string step)
    {
        step = (step ?? "").Trim();
        if (!Skippable.Contains(step)) return No("step is canvas, classes, start_at_login or taskbar: only optional steps can be left for later.");
        if (await driver.SkipAsync(step) is { } refused) return No(refused);
        return Say("Left for later. The student can do it any time in Settings.");
    }

    async Task<CallToolResult> ManualAsync(string step)
    {
        step = step.Trim();
        if (step.Length > 40) return No("Give a checklist item's id, or leave it out.");
        await driver.OpenManualAsync(step);
        return Say("Setup by hand is showing. The student can come back to this chat; Study Stash will tell you what they did.");
    }
}
