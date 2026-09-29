using StudyStash.App.ViewModels;
using StudyStash.Core.Ai;

namespace StudyStash.App;

/// <summary>The design's AI engines: Ollama ready with its model downloaded, Claude Code ready and signed in, Codex
/// installed but not signed in, Gemini not installed. What screenshots and previews of the AI screens show.</summary>
public static class AiDemo
{
    static readonly List<EngineInfo> Engines_ =
    [
        new EngineInfo("ollama", "Ollama", "ready") { Installed = true, Model = "qwen3:30b", Models = [new ModelOption("qwen3:30b", "qwen3:30b (19 GB)")] },
        new EngineInfo("claude", "Claude Code", "ready") { Installed = true },
        new EngineInfo("codex", "Codex", "not_signed_in") { Installed = true },
        new EngineInfo("gemini", "Gemini", "not_installed") { Site = "https://ai.google.dev/gemini-api" },
    ];

    public static AiOverview Overview() => new(Engines: Engines_, Notes: "ollama", Ask: "claude", Fallback: true, Problems: [])
    {
        Diagrams = "auto", DiagramsBy = "claude",
    };

    /// <summary>The library setup step's own overview: nobody has picked who answers yet, so it starts as Same as
    /// notes (the design's first choice), not whatever <see cref="Overview"/> settled on for the engines pane.</summary>
    static AiOverview SetupOverview() => Overview() with { Ask = "ollama" };

    /// <summary>The design's connected tools: an MCP client with a token, and Claude signed in from the web.</summary>
    public static ToolAccessInfo Access() => new(
        On: true, Reading: new ReadingScopes(),
        Connections:
        [
            new ToolConnection("tok-1", "Cursor", "token") { Created = 1_726_000_000 },
            new ToolConnection("sam-web", "Claude", "signin") { ClientHost = "claude.ai", Created = 1_726_000_000, LastUsed = 1_726_600_000 },
        ])
    {
        PublicUrl = "https://sams-mini.tailnet.ts.net", HasPassword = true,
        Web = new WebReach(true, "Study Stash", "https://sams-mini.tailnet.ts.net/mcp", null, null, true, Core.ReachCheck.Answers, 1_726_600_000, true),
    };

    /// <summary>Answers one fixed <see cref="AiOverview"/> and nothing else: enough to draw the panes, never a real
    /// library. <paramref name="rewrite"/> answers every rewrite call, for the notes screen's three states.</summary>
    sealed class Library(AiOverview overview, Func<string, RewriteInfo?>? rewrite = null) : IAiLibrary
    {
        public Task<AiOverview?> EnginesAsync() => Task.FromResult<AiOverview?>(overview);
        public Task<AiOverview?> DefaultsAsync(string? notes = null, string? ask = null, bool? fallback = null, string? diagrams = null) => Task.FromResult<AiOverview?>(overview);
        public Task<AiSaid?> StartAsync(string engine) => Task.FromResult<AiSaid?>(null);
        public Task<AiSaid?> DownloadAsync(string engine) => Task.FromResult<AiSaid?>(null);
        public Task<AiSaid?> SignInAsync(string engine) => Task.FromResult<AiSaid?>(null);
        public Task<AiSaid?> CheckAsync(string engine, string model = "") => Task.FromResult<AiSaid?>(null);
        public Task<AiOverview?> ModelAsync(string engine, string model) => Task.FromResult<AiOverview?>(overview);
        public Task<AiOverview?> DismissAsync(string problemId) => Task.FromResult<AiOverview?>(overview);
        public Task<AskReply?> AskAsync(AskRequest request) => Task.FromResult<AskReply?>(null);
        public Task<RewriteInfo?> RewriteAsync(string lecture) => Task.FromResult(rewrite?.Invoke(lecture));
        public Task<RewriteInfo?> RewriteStartAsync(string lecture, string engine) => Task.FromResult(rewrite?.Invoke(lecture));
        public Task<RewriteInfo?> RewriteCancelAsync(string lecture) => Task.FromResult(rewrite?.Invoke(lecture));
        public Task<RewriteInfo?> RewriteKeepAsync(string lecture) => Task.FromResult(rewrite?.Invoke(lecture));
        public Task<RewriteInfo?> RewriteUseAsync(string lecture) => Task.FromResult(rewrite?.Invoke(lecture));
        public Task<ToolAccessInfo?> AccessAsync() => Task.FromResult<ToolAccessInfo?>(Access());
        public Task<ToolAccessInfo?> SetAccessAsync(bool? on = null, ReadingScopes? reading = null) => Task.FromResult<ToolAccessInfo?>(Access());
        public Task<ToolAccessInfo?> SetWebAsync(bool on) => Task.FromResult<ToolAccessInfo?>(Access());
        public Task<ToolAccessInfo?> CheckWebAsync() => Task.FromResult<ToolAccessInfo?>(Access());
    }

    /// <summary>A library's AI that answers the design's overview (and no rewrite running): what the settings window
    /// and the full app read in their pictures.</summary>
    public static IAiLibrary DemoLibrary() => new Library(Overview(), id => new RewriteInfo(id, "none"));

    /// <summary>A lecture's notes as the full app shows them before any rewrite: written by Ollama.</summary>
    public static AiNotesModel LectureNotes(string markdown)
    {
        var m = new AiNotesModel(DemoLibrary());
        m.Load(LectureId, markdown, "Ollama", Now).GetAwaiter().GetResult();
        return m;
    }

    /// <summary>The ask bar under a lecture, nothing asked yet: questions go to Claude Code.</summary>
    public static AiAskModel AskIdle()
    {
        var m = new AiAskModel(DemoLibrary()) { LectureId = LectureId, ClassName = "CS 101" };
        m.Load().GetAwaiter().GetResult();
        return m;
    }

    /// <summary>The AI engines pane, loaded: notes on Ollama, questions on Claude Code, the fallback on.</summary>
    public static AiEnginesModel Engines()
    {
        var m = new AiEnginesModel(new Library(Overview()));
        m.Load().GetAwaiter().GetResult();
        return m;
    }

    /// <summary>The library setup step, loaded: Ollama picked (Recommended), Codex offering to sign in, questions
    /// still Same as notes.</summary>
    public static AiSetupModel Setup(bool windows = false, bool oneComputer = false)
    {
        var m = new AiSetupModel(new Library(SetupOverview()))
        {
            Windows = windows,
            Lede = oneComputer ? $"They run on this {(windows ? "PC" : "Mac")}, as part of your library. You can change this later in Settings."
                : "This computer is your library, so the engines run here. You can change this later from any of your computers.",
            WriteNotes = oneComputer ? _ => Task.FromResult(true) : null,
        };
        m.Load().GetAwaiter().GetResult();
        return m;
    }

    /// <summary>Just this computer's notes step with one engine in <paramref name="state"/> (Claude Code not installed,
    /// say): Ollama picked, "No AI for now" offered.</summary>
    public static AiSetupModel SetupWith(bool windows, string engine, string state)
    {
        var o = SetupOverview();
        o = o with { Engines = [.. o.Engines.Select(e => e.Id == engine ? e with { State = state, Installed = state != "not_installed" } : e)] };
        var m = new AiSetupModel(new Library(o))
        {
            Windows = windows,
            Lede = $"They run on this {(windows ? "PC" : "Mac")}, as part of your library. You can change this later in Settings.",
            WriteNotes = _ => Task.FromResult(true),
        };
        m.Load().GetAwaiter().GetResult();
        return m;
    }

    /// <summary>The ask bar under a lecture's notes, its "Answer with" menu open: the ask default is Claude Code
    /// (checked), Ollama is picked for this question (tinted).</summary>
    public static AiAskModel Ask()
    {
        var m = new AiAskModel(new Library(Overview()));
        m.Load().GetAwaiter().GetResult();
        m.Engine = "ollama";
        return m;
    }

    /// <summary>The recorder's compact chat, one turn answered: the design's sample question, Ollama's answer with
    /// the moments it drew on.</summary>
    public static AiAskModel Chat()
    {
        var m = new AiAskModel(new Library(Overview()));
        m.Load().GetAwaiter().GetResult();
        m.Engine = "ollama";
        m.Turns.Add(new AiTurn("What did she say is on the midterm?", "Ollama")
        {
            Answer = "Recursion traces and call-stack diagrams. Big-O proofs won't be on it.",
            Byline = AiWords.AskByline("Ollama", [new AskSource("lec-recursion", "Recursion", null, null, 18 * 60 + 5, ""), new AskSource("lec-recursion", "Recursion", null, null, 18 * 60 + 40, "")]),
        });
        return m;
    }

    // -----------------------------------------------------------------------------------------------------------
    // 17 · Rewrite the notes: the design's own lecture, "Recursion and the call stack".
    // -----------------------------------------------------------------------------------------------------------

    const string LectureId = "lec-recursion";
    static readonly string Now = DateTime.Now.ToString("o");

    static readonly NotesVersion CurrentNotes = new(
        "# Summary\n\nA recursive function solves a problem by calling itself on a smaller version of it. Each call gets its own frame on the call stack, which holds that call's arguments and local variables.",
        "Ollama", Now);

    static readonly NotesVersion DraftNotes = new(
        "# Summary\n\nA recursive function calls itself on a smaller input until it reaches a base case it can answer directly. Every call gets a frame on the call stack holding its own arguments and locals.",
        "Claude Code", Now);

    static AiNotesModel LoadedNotes(RewriteInfo info)
    {
        var m = new AiNotesModel(new Library(Overview(), _ => info));
        m.Load(LectureId, CurrentNotes.Markdown, "Ollama", CurrentNotes.At).GetAwaiter().GetResult();
        return m;
    }

    /// <summary>The notes as they sit before any rewrite, "Rewrite notes with" open on Claude Code (Ollama, the
    /// writer, shows checked further down; Codex is signed out).</summary>
    public static AiNotesModel NotesIdle()
    {
        var m = LoadedNotes(new RewriteInfo(LectureId, "none") { Current = CurrentNotes });
        m.Engine = "claude";
        m.MenuOpen = true;
        return m;
    }

    /// <summary>Rewriting with Claude Code: the current notes are exactly as before.</summary>
    public static AiNotesModel NotesRewriting() =>
        LoadedNotes(new RewriteInfo(LectureId, "working") { Engine = "claude", EngineName = "Claude Code", Current = CurrentNotes });

    /// <summary>Claude Code's draft is ready to keep, compare or use.</summary>
    public static AiNotesModel NotesReady() =>
        LoadedNotes(new RewriteInfo(LectureId, "ready") { Engine = "claude", EngineName = "Claude Code", Current = CurrentNotes, Draft = DraftNotes });

    /// <summary>The ready draft, "Compare" already pressed.</summary>
    public static AiNotesModel NotesComparing()
    {
        var m = NotesReady();
        m.CompareCommand.Execute(null);
        return m;
    }

    /// <summary>Claude Code's rewrite failed: the current notes are unchanged.</summary>
    public static AiNotesModel NotesFailed() => LoadedNotes(new RewriteInfo(LectureId, "failed")
    {
        Engine = "claude", EngineName = "Claude Code", Error = "Claude Code hit its usage limit.", Current = CurrentNotes,
    });

    // -----------------------------------------------------------------------------------------------------------
    // 18 · AI problems: the design's six, plus the app's own library-offline and rewrite-failed and a compare view.
    // -----------------------------------------------------------------------------------------------------------

    /// <summary>The design's six problems, in its own order (row-major into the sheet's two columns).</summary>
    public static AiProblemsModel Problems()
    {
        var overview = Overview() with
        {
            Problems =
            [
                new AiProblemInfo("engine-offline", "engine_offline", "ollama", "Ollama"),
                new AiProblemInfo("not-signed-in", "not_signed_in", "codex", "Codex") { FallbackTo = "claude" },
                new AiProblemInfo("model-missing", "model_missing", "ollama", "Ollama") { SizeGb = 40 },
                new AiProblemInfo("usage-limit", "usage_limit", "claude", "Claude Code") { Until = DateTime.Today.AddHours(15).ToString("o"), FallbackTo = "ollama" },
            ],
        };
        var m = new AiProblemsModel(new Library(overview));
        m.Load().GetAwaiter().GetResult();
        m.AddFellBack("fell-back", "Ollama", "Claude Code didn't respond in time.");
        m.AddAccessRequest("access-1", "Codex", "Eli's MacBook", () => Task.CompletedTask, () => Task.CompletedTask);
        return m;
    }

    /// <summary>The extras the design's six don't show: the library not answering at all, and a rewrite that failed
    /// (the same words <see cref="NotesFailed"/>'s inline bar uses).</summary>
    public static AiProblemsModel ProblemsMore()
    {
        var m = new AiProblemsModel(new Library(Overview()));
        m.Load().GetAwaiter().GetResult();
        m.AddLibraryOffline(() => Task.CompletedTask);
        m.AddRewriteFailed("rewrite-1", "Claude Code", "Claude Code hit its usage limit.", () => Task.CompletedTask);
        return m;
    }
}
