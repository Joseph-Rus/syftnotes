using System.Text.Json.Nodes;

namespace StudyStash.Core.Ai;

/// <summary>
/// The library's work, done by the AI picked for it: the functions the pipeline, sorting and Ask call. ai.json is
/// read on every call, so a change in Settings applies to the next lecture. Work that stays on Ollama goes through
/// the engine's own Ollama calls (their context sizing and loop guards).
/// </summary>
public sealed class AiJobs(string home, Func<string>? ollamaHost = null)
{
    AiSettings Settings => AiSettings.Load(home);

    /// <summary>Every engine by id, real by default; a test sets this so <em>every</em> job — Ollama included —
    /// goes through a fake, never a real account or <c>localhost:11434</c>.</summary>
    public Func<string, AiProvider>? Providers { get; init; }

    /// <summary>How the library checks whether each engine is ready, without ever contacting an account.</summary>
    public EngineChecks Checks { get; init; } = EngineChecks.Machine with { HasKey = id => ApiKeys.Has(home, id) };

    /// <summary>How long <see cref="AskAsync"/> waits for the picked engine before falling back to Ollama: for the
    /// whole answer, or, when it's shown as it's written, for the engine to start writing it.</summary>
    public TimeSpan AskTimeout { get; init; } = TimeSpan.FromSeconds(90);

    /// <summary>A model Ollama is pulling right now, for <see cref="AiOverview.Pulling"/>. Null once it's done.</summary>
    public PullInfo? Pulling { get; private set; }

    AiProvider Provider(string id)
    {
        if (Providers?.Invoke(id) is { } fake) return fake;
        var p = AiProviders.Get(id, ollamaHost);
        p.ApiKey = ApiKeys.Get(home, p.Id);
        return p;
    }

    /// <summary>An empty folder for answers that need no files: nothing there to read or change.</summary>
    string Scratch()
    {
        string dir = Path.Combine(home, "ai-work");
        Directory.CreateDirectory(dir);
        return dir;
    }

    async Task<string> AnswerAsync(string job, string prompt, CancellationToken ct = default)
    {
        var settings = Settings;
        var choice = settings.For(job);
        var provider = Provider(choice.Provider);
        // Only the notes take the speed Settings sets for Claude Code: sorting and answers stay as set up.
        var how = job == "notes" ? AiSpeed.ForNotes(choice.Provider, choice.Model, settings.Speed) : new AiSpeed.How(choice.Model, "", false);
        var result = await provider.CompleteAsync(new AiRequest(prompt, Scratch())
        {
            Model = how.Model, Effort = how.Effort, Fast = how.Fast, Timeout = TimeSpan.FromMinutes(15),
        }, ct);
        if (!result.Ok) throw new InvalidOperationException($"{provider.Name}: {result.Text}");
        return result.Text;
    }

    /// <summary>The same prompt, told to answer with JSON only; then the JSON object in what came back.</summary>
    async Task<string> JsonAnswerAsync(string job, string prompt, JsonObject schema)
    {
        string text = await AnswerAsync(job, prompt + "\n\nAnswer with only a JSON object that fits this JSON schema, and nothing else:\n"
            + schema.ToJsonString());
        return FirstObject(text) ?? throw new InvalidDataException("the answer wasn't JSON");
    }

    /// <summary>Answering with a specific engine, not the one ai.json picks for a job — the "Answer with" menu's
    /// choice, and this class's own fallback to Ollama in <see cref="AskAsync"/>.</summary>
    async Task<string> AnswerWithAsync(string engine, string model, string prompt, CancellationToken ct, Action<string>? soFar = null,
        string effort = "", bool fast = false)
    {
        var provider = Provider(engine);
        var result = await provider.CompleteAsync(new AiRequest(prompt, Scratch())
        {
            Model = model, Effort = effort, Fast = fast, Timeout = TimeSpan.FromMinutes(15),
        }, ct, soFar);
        if (!result.Ok) throw new InvalidOperationException(result.Text);
        return result.Text;
    }

    async Task<string> JsonAnswerWithAsync(string engine, string model, string prompt, JsonObject schema, CancellationToken ct,
        Action<string>? soFar = null)
    {
        string text = await AnswerWithAsync(engine, model, prompt + "\n\nAnswer with only a JSON object that fits this JSON schema, and nothing else:\n"
            + schema.ToJsonString(), ct, soFar);
        return FirstObject(text) ?? throw new InvalidDataException("the answer wasn't JSON");
    }

    /// <summary>Ask a question with one named engine, ignoring ai.json's own "ask" pick. <paramref name="soFar"/>,
    /// when given, hears the engine's reply as it's written (all of it so far, each time more arrives).</summary>
    public LibraryReader.AskChatFn AskWith(string engine, string model = "", CancellationToken ct = default, Action<string>? soFar = null) =>
        (prompt, schema) => JsonAnswerWithAsync(engine, model, prompt, schema, ct, soFar);

    /// <summary>A JSON answer from the AI picked for <paramref name="job"/>, for work that returns a plan.</summary>
    public Task<string> PlanAsync(string job, string prompt, JsonObject schema) => JsonAnswerAsync(job, prompt, schema);

    /// <summary>The first whole {...} in a text (models like to wrap JSON in a code fence or a sentence).</summary>
    public static string? FirstObject(string text)
    {
        int start = text.IndexOf('{');
        while (start >= 0)
        {
            int depth = 0;
            bool quoted = false, escaped = false;
            for (int i = start; i < text.Length; i++)
            {
                char c = text[i];
                if (quoted)
                {
                    if (escaped) escaped = false;
                    else if (c == '\\') escaped = true;
                    else if (c == '"') quoted = false;
                    continue;
                }
                if (c == '"') quoted = true;
                else if (c == '{') depth++;
                else if (c == '}' && --depth == 0)
                {
                    string candidate = text[start..(i + 1)];
                    // An answer's LaTeX with single backslashes (\sqrt) isn't JSON until they're doubled.
                    foreach (string attempt in new[] { candidate, Rich.MathText.DoubleLoneBackslashes(candidate) })
                    {
                        try
                        {
                            if (JsonNode.Parse(attempt) is JsonObject) return attempt;
                        }
                        catch (System.Text.Json.JsonException)
                        {
                        }
                    }
                    break;
                }
            }
            start = text.IndexOf('{', start + 1);
        }
        return null;
    }

    /// <summary>Whether "notes" should really write with Ollama instead of ai.json's pick: either that's the pick
    /// already, or the pick is known unusable and Fallback is on. <see cref="SummarizeAsync"/> and
    /// <see cref="Describe"/> both ask this, so a note never claims a CLI wrote what Ollama actually wrote — a
    /// pre-flight check only, before the run starts; a job already under way never changes engines mid-run (the
    /// pipeline's own retry does that, not this).</summary>
    bool NotesOnOllama(AiSettings settings) =>
        settings.Local("notes") || (settings.Fallback && Engines.KnownUnusableWhy(settings.For("notes").Provider, settings, Checks) is not null);

    /// <summary>What an engine's notes may draw: Ollama's small models keep to flowcharts; the CLI engines, whose
    /// SVG holds together, may draw what's spatial too.</summary>
    public static Drawings DrawingsFor(string engine) => engine == "ollama" ? Drawings.Flowcharts : Drawings.FlowchartsAndSvg;

    /// <summary>Writing study notes, and nothing more: when a designer adds their diagrams, that happens after the lecture
    /// is filed (<see cref="DiagramJobs"/>), never before, and <see cref="TakeDiagramsFollow"/> says so.</summary>
    public async Task<string> SummarizeAsync(Meeting m, Config cfg)
    {
        var settings = Settings;
        bool onOllama = NotesOnOllama(settings);
        string engine = onOllama ? "ollama" : settings.For("notes").Provider;
        var (drawings, designer) = await DiagramPlanAsync(m, cfg, settings, engine);
        string notes;
        if (onOllama)
            notes = Providers is null
                ? await Core.Summarize.SummarizeTranscriptAsync(m, cfg, drawings: drawings)
                // A test's fake Ollama: the same prompts and sizing as the real one's, through the fake.
                : await Core.Summarize.SummarizeTranscriptAsync(m, cfg,
                    chat: (_, _, prompt, _) => AnswerWithAsync("ollama", "", prompt, CancellationToken.None),
                    show: (_, _) => Task.FromResult<int?>(null), drawings: drawings);
        else
            // Other models read a whole lecture at once: tell the splitter their context is large.
            notes = await Core.Summarize.SummarizeTranscriptAsync(m, cfg,
                chat: (_, _, prompt, _) => AnswerAsync("notes", prompt),
                show: (_, _) => Task.FromResult<int?>(200_000),
                drawings: drawings);
        if (designer is null) follow.TryRemove(m.Id, out _);
        else follow[m.Id] = engine;
        return notes;
    }

    readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> follow = new();

    /// <summary>Whether a designer adds diagrams to the notes <see cref="SummarizeAsync"/> just wrote for a lecture
    /// (they were written without any of their own, for it to add): the engine that wrote them, for the designer's
    /// pick, or null when nothing follows. Asked once, when the lecture is filed.</summary>
    public string? TakeDiagramsFollow(string lectureId) => follow.TryRemove(lectureId, out string? engine) ? engine : null;

    /// <summary>How long the notes' diagrams may take, all told (a test makes it short).</summary>
    public TimeSpan DiagramTimeout { get; init; } = DiagramDesign.Timeout;

    /// <summary>
    /// Who designs the diagrams for notes <paramref name="notesEngine"/> wrote, by the student's pick as it is now
    /// (<see cref="DiagramEngines.PickAsync"/>: automatic keeps to engines that already read the lectures, Ollama draws
    /// flowcharts only): null when nobody does now (diagrams turned off since, or no engine to be had).
    /// </summary>
    public Task<DiagramPick?> DesignerAsync(Config cfg, string notesEngine) => DiagramEngines.PickAsync(Settings, cfg, Checks, notesEngine);

    /// <summary>The kinds of rich notes switched on now (none while rich notes are off): what designed diagrams may still
    /// put into the notes.</summary>
    public RichKinds KindsNow() => Settings.Kinds();

    /// <summary>
    /// The diagram pass over a filed lecture's <paramref name="notes"/>, by <paramref name="pick"/>: what it designed, or
    /// null when it couldn't (said in the log, with why). The notes themselves are never touched here: the caller puts
    /// the diagrams in (<see cref="Store.AddDiagrams"/>). Only <paramref name="ct"/> cancelling throws.
    /// </summary>
    public Task<DesignResult?> DesignDiagramsAsync(Meeting m, Config cfg, string notes, DiagramPick pick, CancellationToken ct) =>
        DiagramsAsync(m, cfg, notes, pick, ct);

    /// <summary>Where the library's own log goes (a lecture's diagram pass says what it did there); nowhere when unset.</summary>
    public Action<string>? Log { get; init; }

    /// <summary>
    /// What notes written by <paramref name="notesEngine"/> are told about diagrams, and who designs them after:
    /// rich notes off (or every kind switched off), or a lecture too short for a diagram, draws none and asks nothing
    /// more; "same as notes" (or no designer to be had) leaves the notes engine drawing its own, as before there was a
    /// diagram pass; otherwise the notes draw none and the designer (<see cref="DiagramEngines.PickAsync"/>) adds them.
    /// </summary>
    async Task<(Drawings Notes, DiagramPick? Designer)> DiagramPlanAsync(Meeting m, Config cfg, AiSettings settings, string notesEngine)
    {
        var kinds = settings.Kinds();
        if (kinds == RichKinds.None) return (Drawings.None, null);
        Drawings own = NotesDrawings(notesEngine, kinds);
        if (DiagramEngines.Normal(settings.Diagrams) == DiagramEngines.SameAsNotes) return (own, null);
        if (DiagramDesign.Cap(m.Transcript) == 0) return (Drawings.None, null);
        var pick = await DiagramEngines.PickAsync(settings, cfg, Checks, notesEngine);
        return pick is null ? (own, null) : (Drawings.None, pick);
    }

    /// <summary>What notes that draw as they write may draw (<see cref="DrawingsFor"/>), less what's switched off: a
    /// flowchart is a diagram, so with diagrams off (or with only plots on, which notes never draw) they draw none, and
    /// with drawings off they keep to flowcharts.</summary>
    public static Drawings NotesDrawings(string engine, RichKinds kinds) =>
        !kinds.HasFlag(RichKinds.Diagrams) ? Drawings.None
        : !kinds.HasFlag(RichKinds.Drawings) ? Drawings.Flowcharts
        : DrawingsFor(engine);

    /// <summary>
    /// The diagram pass over filed notes, with the designer <paramref name="pick"/> names: at most
    /// <see cref="DiagramTimeout"/> (longer for an illustration), and never a failure of the notes — an engine that
    /// can't answer, a reply that can't be used or a pass that runs out of time is null, and the notes stay exactly as
    /// filed. Says in the log what it drew or why it drew nothing. Only the caller's own cancelling throws.
    /// </summary>
    async Task<DesignResult?> DiagramsAsync(Meeting m, Config cfg, string notes, DiagramPick pick, CancellationToken ct)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        string who = pick.Engine == "ollama" ? pick.Model
            : Provider(pick.Engine).Name + (pick.Model.Length > 0 ? " " + pick.Model : "") + (pick.Fast ? " in fast mode" : "");
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(DiagramTimeout);
        try
        {
            bool local = pick.Engine == "ollama" && Providers is null;
            int ctx = local ? await Core.Summarize.ContextSizeAsync(cfg, pick.Model) : 200_000;
            var result = await DiagramDesign.DesignAsync(m, notes, pick.Drawings, Designer(pick, cfg, ctx, cts.Token), Core.Summarize.TranscriptBudget(ctx),
                    DiagramTimeout - watch.Elapsed, Drawer(pick, cfg, cts.Token),
                    longer: total => cts.CancelAfter(total > watch.Elapsed ? total - watch.Elapsed : TimeSpan.Zero),
                    compose: Composer(pick, cfg, cts.Token), scenes: Scenes, kinds: pick.Kinds)
                .WaitAsync(cts.Token);
            if (pick.Engine != "ollama") Record(pick.Engine, true, "");
            string left = result.Dropped.Count > 0 ? $"; left out {string.Join("; ", result.Dropped)}" : "";
            string revised = result.Revised > 0 ? $", {result.Revised} redesigned after a look at how {(result.Revised == 1 ? "it" : "they")} laid out" : "";
            if (result.Composed > 0) revised += $", {result.Composed} composed from the parts library";
            string looks = result.Problems.Count > 0 ? $"; still looks off: {string.Join("; ", result.Problems)}" : "";
            Log?.Invoke(result.Malformed
                ? $"[diagrams] '{m.Title}': {who}'s answer couldn't be used ({string.Join("; ", result.Dropped)}); the notes stay as filed"
                : result.Drawn.Count > 0
                    ? $"[diagrams] '{m.Title}': {who} drew {result.Drawn.Count} ({string.Join(", ", result.Drawn.Select(d => d.Title))}) in {watch.Elapsed.TotalSeconds:0}s{revised}{left}{looks}"
                    : $"[diagrams] '{m.Title}': {who} drew none: {result.Reason}{left}");
            return result;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception e)
        {
            string why = e is OperationCanceledException or TimeoutException && cts.IsCancellationRequested
                ? $"it took longer than {(DiagramTimeout.TotalMinutes >= 1 ? $"{DiagramTimeout.TotalMinutes:0} minutes" : $"{DiagramTimeout.TotalSeconds:0} seconds")}"
                : e.Message;
            if (pick.Engine != "ollama") Record(pick.Engine, false, why);
            Log?.Invoke($"[diagrams] '{m.Title}': {who} couldn't design diagrams ({why}); the notes stay as filed");
            return null;
        }
        finally
        {
            // A revision given up on for running late may still be running: it's stopped with the pass.
            cts.Cancel();
        }
    }

    /// <summary>The illustrator: the designer's engine and model at the effort a drawing gets
    /// (<see cref="DiagramEngines.DrawEffort"/>); none for an engine that doesn't draw SVG.</summary>
    Func<string, Task<string>>? Drawer(DiagramPick pick, Config cfg, CancellationToken ct)
    {
        if (pick.Drawings != Drawings.FlowchartsAndSvg || !pick.Kinds.HasFlag(RichKinds.Drawings)) return null;
        var illustrator = Designer(pick with { Effort = DiagramEngines.DrawEffort(pick.Engine) }, cfg, 200_000, ct, IllustrationDesign.Timeout);
        return prompt => illustrator(prompt, false);
    }

    /// <summary>The composer: the designer's engine with a fast model at low effort, for an illustration made of
    /// ready-made parts (<see cref="IllustrationDesign.ComposeAsync"/>); none for an engine that doesn't draw SVG.</summary>
    Func<string, Task<string>>? Composer(DiagramPick pick, Config cfg, CancellationToken ct)
    {
        if (pick.Drawings != Drawings.FlowchartsAndSvg || !pick.Kinds.HasFlag(RichKinds.Drawings)) return null;
        // Sonnet has no fast mode: composing stays as it is, quick already.
        var composer = Designer(pick with { Model = DiagramEngines.ComposeModel(pick.Engine), Effort = DiagramEngines.ComposeEffort(pick.Engine), Fast = false },
            cfg, 200_000, ct, IllustrationDesign.ComposeTimeout);
        return prompt => composer(prompt, false);
    }

    /// <summary>Illustrations composed before, by plan, so a class that comes back to a subject gets its figure at once.</summary>
    ComposedScenes Scenes => sceneCache ??= new ComposedScenes(Path.Combine(home, "illustration-scenes"));
    ComposedScenes? sceneCache;

    /// <summary>
    /// The designer, one prompt at a time (its design, then any repair): a local model straight through Ollama, held
    /// to JSON where the answer must be; a CLI engine as the pick says (by default its strongest model and high effort,
    /// in Claude Code's fast mode or on a smaller model when Settings says so). A CLI that turns those
    /// down (an older one without --effort, a plan without that model) is asked again as the student set it up, and
    /// so from then on; a usage limit or a sign-in problem isn't something asking again fixes.
    /// </summary>
    Func<string, bool, Task<string>> Designer(DiagramPick pick, Config cfg, int ctx, CancellationToken ct, TimeSpan? timeout = null)
    {
        if (pick.Engine == "ollama" && Providers is null)
            return (prompt, json) => Core.Summarize.OllamaGenerateAsync(cfg, pick.Model, prompt, ctx, timeout: DiagramTimeout, json: json, ct: ct);
        var provider = Provider(pick.Engine);
        var strong = new AiRequest("", Scratch()) { Model = pick.Model, Effort = pick.Effort, Fast = pick.Fast, Timeout = timeout ?? DiagramTimeout };
        var plain = strong with { Model = pick.Engine == "ollama" ? "" : Settings.Models.GetValueOrDefault(pick.Engine, ""), Effort = "", Fast = false };
        bool asSetUp = strong == plain;
        return async (prompt, _) =>
        {
            var r = await provider.CompleteAsync((asSetUp ? plain : strong) with { Prompt = prompt }, ct);
            if (!r.Ok && !asSetUp && !ct.IsCancellationRequested && !Engines.LooksLikeLimit(r.Text) && !Engines.LooksLikeAuth(r.Text))
            {
                asSetUp = true;
                r = await provider.CompleteAsync(plain with { Prompt = prompt }, ct);
            }
            if (!r.Ok) throw new InvalidOperationException($"{provider.Name}: {r.Text}");
            return r.Text;
        };
    }

    /// <summary>Rewrite a lecture's notes with a chosen engine — the "Rewrite notes with" menu's own choice, not
    /// ai.json's "notes" pick. Real Ollama with no test hooks keeps <see cref="SummarizeAsync"/>'s own direct path;
    /// every other engine (and a faked Ollama, in tests) goes through the same chat wrapper, but with
    /// <paramref name="engine"/> itself, and <paramref name="ct"/> in the closure since a chat call takes none of
    /// its own. <paramref name="progress"/> hears a running count as each chat call finishes, against a first
    /// estimate of how many parts the transcript needs (long lectures may need a few more, to merge them). Cancelling
    /// a real Ollama run is best-effort only — Ollama's own call has no way to stop mid-generation — so the caller
    /// marks that job cancelled itself and drops whatever this returns.</summary>
    public async Task<string> WriteNotesAsync(Meeting m, Config cfg, string engine, Action<int, int>? progress, CancellationToken ct) =>
        (await WriteNotesPlannedAsync(m, cfg, engine, progress, ct)).Notes;

    /// <summary>
    /// <see cref="WriteNotesAsync"/>, saying too whether a designer adds the new notes' diagrams once they're the
    /// lecture's (<c>DiagramsBy</c>: the engine that wrote them, for the designer's pick; null when nothing follows).
    /// The notes come without them, as the pipeline's do: the draft is ready as soon as its words are.
    /// </summary>
    public async Task<(string Notes, string? DiagramsBy)> WriteNotesPlannedAsync(Meeting m, Config cfg, string engine, Action<int, int>? progress,
        CancellationToken ct)
    {
        // The diagrams come after the notes are used, from the student's diagrams pick, this rewrite's engine being the
        // one that reads the lecture.
        var (drawings, designer) = await DiagramPlanAsync(m, cfg, Settings, engine);
        string notes;
        int done = 0, parts;
        if (engine == "ollama" && Providers is null)
        {
            parts = 1;
            notes = await Core.Summarize.SummarizeTranscriptAsync(m, cfg, drawings: drawings);
            progress?.Invoke(++done, parts);
        }
        else
        {
            var how = AiSpeed.ForNotes(engine, Settings.Models.GetValueOrDefault(engine, ""), Settings.Speed);
            const int ctx = 200_000; // other models read a whole lecture at once: tell the splitter their context is large
            string text = Py.Strip(TimedText.Plain(m.Transcript));
            int budget = Core.Summarize.TranscriptBudget(ctx);
            parts = text.Length <= budget ? 1 : Core.Summarize.SplitTranscript(text, budget).Count;
            async Task<string> ChatAsync(Config c, string mdl, string prompt, int numCtx)
            {
                string result = await AnswerWithAsync(engine, how.Model, prompt, ct, effort: how.Effort, fast: how.Fast);
                progress?.Invoke(++done, Math.Max(done, parts));
                return result;
            }
            notes = await Core.Summarize.SummarizeTranscriptAsync(m, cfg, chat: ChatAsync, show: (_, _) => Task.FromResult<int?>(ctx),
                drawings: drawings);
        }
        return (notes, designer is null ? null : engine);
    }

    /// <summary>The name of what wrote something with a specific engine, not ai.json's own pick for a job — the
    /// rewrite job's choice, kept as the note's <c>summary_model</c> so <see cref="Engines.WhoWrote"/> maps it back
    /// to a display name later, the same way it does for the notes ai.json actually picked.</summary>
    public string DescribeChoice(string engine, Config cfg)
    {
        if (engine == "ollama") return cfg.EffectiveSummaryModel;
        var settings = Settings;
        string model = AiSpeed.ForNotes(engine, settings.Models.GetValueOrDefault(engine, ""), settings.Speed).Model;
        return Provider(engine).Name + (model.Length > 0 ? " " + model : "");
    }

    /// <summary>
    /// The engine that sorts instead of Ollama, or null when sorting stays as it is. Sorting follows the library's main
    /// AI, which is Ollama unless it's changed; a student who picked Claude Code (or Codex, or Gemini) to write the notes
    /// and has no Ollama model to sort with would otherwise see every lecture filed Unsorted. So while Ollama isn't
    /// answering with the sorting model, a sort that follows the main AI goes to the notes engine, when that one looks
    /// usable. A sorting engine the student picked in Settings, and a library whose Ollama works, are never changed.
    /// </summary>
    public static async Task<string?> SortFollowsNotesAsync(AiSettings settings, Config cfg, EngineChecks checks)
    {
        if (!settings.Local("sort") || (settings.ByJob.TryGetValue("sort", out var own) && own.Provider.Length > 0)) return null;
        string notes = settings.For("notes").Provider;
        if (notes == "ollama" || Engines.KnownUnusableWhy(notes, settings, checks) is not null) return null;
        bool ollamaSorts = await checks.OllamaModels(cfg.OllamaHost) is { } models
            && Ollama.HasModel(models.Select(m => m.Name).ToList(), cfg.OllamaModel);
        return ollamaSorts ? null : notes;
    }

    /// <summary>Sorting into classes.</summary>
    public async Task<string> SortAsync(Config cfg, string prompt, JsonObject schema)
    {
        var settings = Settings;
        if (await SortFollowsNotesAsync(settings, cfg, Checks) is { } follows)
        {
            string text = await AnswerWithAsync(follows, settings.For("notes").Model, prompt
                + "\n\nAnswer with only a JSON object that fits this JSON schema, and nothing else:\n" + schema.ToJsonString(), CancellationToken.None);
            return FirstObject(text) ?? throw new InvalidDataException("the answer wasn't JSON");
        }
        if (settings.Local("sort") && Providers is null) return await Classify.OllamaChatAsync(cfg, prompt, schema);
        return await JsonAnswerAsync("sort", prompt, schema); // another engine's pick, or a test's fake Ollama
    }

    /// <summary>Asking your notes.</summary>
    public LibraryReader.AskChatFn Ask(Func<Config> cfg) => (prompt, schema) => Settings.Local("ask")
        ? LibraryReader.OllamaAskAsync(cfg(), prompt, schema)
        : JsonAnswerAsync("ask", prompt, schema);

    /// <summary>Whether the notes are being written in Claude Code's fast mode right now (it has to be what writes them, on
    /// Opus, with the speed set to fast), for the log to say so.</summary>
    public bool NotesInFastMode()
    {
        var settings = Settings;
        if (NotesOnOllama(settings)) return false;
        var c = settings.For("notes");
        return AiSpeed.ForNotes(c.Provider, c.Model, settings.Speed).Fast;
    }

    /// <summary>The name of what does a job, for the log ("Claude sonnet", "qwen3:8b").</summary>
    public string Describe(string job, Config cfg)
    {
        var settings = Settings;
        var c = settings.For(job);
        if (c.Provider == "ollama" || (job == "notes" && NotesOnOllama(settings)))
            return job == "notes" ? cfg.EffectiveSummaryModel : cfg.OllamaModel;
        string model = job == "notes" ? AiSpeed.ForNotes(c.Provider, c.Model, settings.Speed).Model : c.Model;
        return Provider(c.Provider).Name + (model.Length > 0 ? " " + model : "");
    }

    /// <summary>The command that starts this engine's MCP server (the Canvas and library tools), for agents.</summary>
    public IReadOnlyList<string> McpCommand { get; init; } =
        Environment.ProcessPath is { } exe && !Path.GetFileNameWithoutExtension(exe).Equals("dotnet", StringComparison.OrdinalIgnoreCase)
            ? [exe, "--home", home, "mcp"] : [];

    /// <summary>
    /// Agent work (the course scout, a chat that may change notes): the AI picked for <c>agent</c>, working in
    /// <paramref name="cwd"/> with the library's tools. <paramref name="write"/> lets it change files there.
    /// </summary>
    public IAsyncEnumerable<AiEvent> AgentAsync(string prompt, string cwd, bool write, string system = "", string session = "",
        IReadOnlyList<string>? readDirs = null, string job = "agent", CancellationToken ct = default)
    {
        var choice = Settings.For(job);
        var provider = Provider(choice.Provider);
        return provider.RunAsync(new AiRequest(prompt, cwd)
        {
            Model = choice.Model, Write = write, Tools = McpCommand.Count > 0, McpCommand = McpCommand, System = system,
            Session = session, ReadDirs = readDirs ?? [],
        }, ct: ct);
    }

    /// <summary>The name of the AI that does agent work ("Claude").</summary>
    public string AgentName => Provider(Settings.For("agent").Provider).Name;

    /// <summary>Whether agent work can run: its AI is installed (a local model needs Codex to act as an agent).</summary>
    public (bool Ok, string Why) AgentReady()
    {
        var c = Settings.For("agent");
        var p = Provider(c.Provider);
        if (!p.Available()) return (false, $"{p.Name} isn't installed on the library's computer.");
        if (p is OllamaProvider && !OllamaProvider.Agentic()) return (false, "A local model needs the Codex CLI to explore on its own (brew install codex).");
        return (true, "");
    }

    /// <summary>Have a provider say one word, so a person knows it's signed in before relying on it. Kept in ai.json.</summary>
    public async Task<(bool Ok, string Why)> TestAsync(string providerId, string model = "")
    {
        var provider = Provider(providerId);
        var r = await provider.CompleteAsync(new AiRequest("Reply with the single word: ready", Scratch())
        {
            Model = model, Timeout = TimeSpan.FromMinutes(3),
        });
        bool ok = r.Ok && r.Text.Contains("ready", StringComparison.OrdinalIgnoreCase);
        string why = ok ? "" : r.Ok ? $"it answered \"{Py.Head(r.Text, 80)}\"" : r.Text;
        var s = Settings;
        s.Tests[provider.Id] = ok ? "works" : why;
        s.Save(home);
        return (ok, why);
    }

    /// <summary>What an engine's run just proved: success clears any usage limit and marks it working; an
    /// auth-looking error is kept so the engines pane shows "Not signed in"; a limit-looking error is kept as an
    /// until time. Anything else (a one-off hiccup) isn't recorded, so it doesn't outlive the moment.</summary>
    public void Record(string id, bool ok, string error)
    {
        var s = Settings;
        if (ok)
        {
            s.Tests[id] = "works";
            s.Limits.Remove(id);
        }
        else if (Engines.LooksLikeLimit(error))
        {
            s.Limits[id] = Engines.UntilFrom(error, Checks.Now()).ToString("o");
        }
        else if (Engines.LooksLikeAuth(error))
        {
            s.Tests[id] = error;
        }
        else return; // a plain failure: not lasting enough to change the engine's remembered state
        s.Save(home);
    }

    /// <summary>Download <paramref name="model"/> through Ollama, in the background, reporting progress through
    /// <see cref="Pulling"/> until it's done (then null) or it fails (then <see cref="PullInfo.Why"/> says why).</summary>
    public Task DownloadAsync(string model, string host, CancellationToken ct = default)
    {
        if (Pulling is { Why.Length: 0 } already && already.Model == model) return Task.CompletedTask;
        Pulling = new PullInfo(model, 0, "");
        return Task.Run(async () =>
        {
            try
            {
                var (ok, why) = await Checks.PullModel(model, host,
                    (done, total) => Pulling = new PullInfo(model, total > 0 ? (double)done / total : 0, ""), ct);
                Pulling = ok ? null : new PullInfo(model, 0, why);
            }
            catch (Exception e) when (e is HttpRequestException or OperationCanceledException)
            {
                Pulling = new PullInfo(model, 0, e.Message);
            }
        }, ct);
    }

    /// <summary>
    /// Gets the free AI ready on this computer in one go, in the background, whatever it still needs: the Ollama app
    /// (downloaded from its maker and installed), starting it, and the model it writes notes with. <see cref="Pulling"/>
    /// says which step it's at and how far along, until it's done (then null) or a step fails (then
    /// <see cref="PullInfo.Why"/> says why, in words for the student). Asked again while it runs, it carries on.
    /// </summary>
    public Task SetUpOllamaAsync(string model, string host, CancellationToken ct = default)
    {
        if (Pulling is { Why.Length: 0 }) return Task.CompletedTask;
        // No room for it: said before anything is downloaded, not by a download that stops near its end.
        double need = Ollama.DownloadGb(model);
        if (need > 0 && Checks.DiskFreeGb() is { } free && free < need + 2)
        {
            Pulling = new PullInfo(model, 0, $"This computer doesn't have room for the free AI: it needs about {Math.Ceiling(need + 2):0} GB free, and has {Math.Floor(free):0}.");
            return Task.CompletedTask;
        }
        bool installed = Checks.OllamaInstalled();
        Pulling = new PullInfo(model, 0, "") { Step = installed ? "start" : "app" };
        return Task.Run(async () =>
        {
            try
            {
                if (!installed)
                {
                    bool got = await Checks.InstallOllama((done, total) => Pulling = new PullInfo(model, total > 0 ? (double)done / total : 0, "") { Step = "app" });
                    if (!got)
                    {
                        Pulling = new PullInfo(model, 0, "The free AI's app couldn't be downloaded and installed. Check the internet connection and try again.") { Step = "app" };
                        return;
                    }
                }
                var models = await Checks.OllamaModels(host);
                if (models is null)
                {
                    Pulling = new PullInfo(model, 0, "") { Step = "start" };
                    if (!await Checks.StartOllama(host))
                    {
                        Pulling = new PullInfo(model, 0, "The free AI is installed but didn't start. Open the Ollama app, then try again.") { Step = "start" };
                        return;
                    }
                    models = await Checks.OllamaModels(host);
                }
                if (models is not null && Ollama.HasModel(models.Select(m => m.Name).ToList(), model))
                {
                    Pulling = null;
                    return;
                }
                Pulling = null; // the download says where it is itself
                await DownloadAsync(model, host, ct);
            }
            catch (Exception e) when (e is HttpRequestException or OperationCanceledException or IOException)
            {
                Pulling = new PullInfo(model, 0, e.Message);
            }
        }, ct);
    }

    DateTime warmedAt = DateTime.MinValue;
    readonly Lock warming = new();

    /// <summary>How often <see cref="Warm"/> asks Ollama to load its model at most: loaded, it stays loaded for
    /// <see cref="OllamaProvider.KeepLoaded"/> after each question anyway.</summary>
    public static readonly TimeSpan WarmEvery = TimeSpan.FromMinutes(1);

    /// <summary>
    /// A student has started typing a question for <paramref name="engine"/> (or the "ask" pick): when Ollama will
    /// answer it, its model starts loading now, in the background, so the answer isn't held up by the load. True
    /// when that started; a CLI engine has nothing to get ready.
    /// </summary>
    public bool Warm(string? engine, Config cfg)
    {
        var settings = Settings;
        string who = engine is { Length: > 0 } e ? e : settings.For("ask").Provider;
        if (who != "ollama" && settings.Fallback && Engines.KnownUnusableWhy(who, settings, Checks) is not null) who = "ollama";
        if (who != "ollama" || (!cfg.OllamaEnabled && Providers is null)) return false;
        lock (warming)
        {
            if (DateTime.UtcNow - warmedAt < WarmEvery) return false;
            warmedAt = DateTime.UtcNow;
        }
        _ = Provider(who).WarmAsync(AskModel(who, settings, cfg));
        return true;
    }

    /// <summary>The model an engine answers questions with: for Ollama the library's own model (the one Settings
    /// picks and the engines pane checks is there), not just the biggest one installed, which may be far slower.</summary>
    static string AskModel(string engine, AiSettings settings, Config cfg) =>
        engine == "ollama" ? cfg.EffectiveSummaryModel : settings.Models.GetValueOrDefault(engine, "");

    /// <summary>
    /// Answer a question with a chosen engine (the request's, or ai.json's "ask" pick): when it's known unusable
    /// (not installed, not signed in, hit its limit) and <see cref="AiSettings.Fallback"/> is on, Ollama answers up
    /// front; otherwise it's tried for real, under <see cref="AskTimeout"/>, and only falls back on a timeout or an
    /// error (never mid-run: that's the pipeline's own job). Every engine that's actually run has its result
    /// recorded (<see cref="Record"/>).
    /// <para><paramref name="answerSoFar"/>, when given, hears the answer as it's written (all of it so far, each
    /// time more arrives), and the timeout only covers the wait for the engine to start. Once the student is reading
    /// an answer it's never thrown away for another engine's: a failure then says what stopped it.</para>
    /// </summary>
    public async Task<AskReply> AskAsync(AskRequest request, LibraryReader reader, Config cfg, CancellationToken ct = default,
        Action<string>? answerSoFar = null)
    {
        var settings = Settings;
        string asked = request.Engine is { Length: > 0 } e ? e : settings.For("ask").Provider;
        string engine = asked;
        bool fellBack = false;
        string why = "";

        if (settings.Fallback && Engines.KnownUnusableWhy(asked, settings, Checks) is { } preflight)
        {
            engine = "ollama";
            fellBack = true;
            why = preflight;
        }
        if (engine == "ollama" && !cfg.OllamaEnabled && Providers is null)
            throw new InvalidOperationException("Asking needs an engine: turn one on in AI engines.");

        string shown = "";
        async Task<JsonObject> RunAsync(string who, CancellationTokenSource timer)
        {
            string model = AskModel(who, settings, cfg);
            Action<string>? written = answerSoFar is null ? null : raw =>
            {
                timer.CancelAfter(Timeout.InfiniteTimeSpan); // it's answering: from here it has as long as it needs
                if (AskAnswer.SoFar(raw) is { Length: > 0 } now && now != shown)
                {
                    shown = now;
                    answerSoFar(now);
                }
            };
            var answer = await reader.AskAsync(request.Question, request.Lecture, request.Class, request.Live,
                request.LiveTitle is { Length: > 0 } t ? t : "This lecture", AskWith(who, model, timer.Token, written));
            Record(who, true, "");
            return answer;
        }

        JsonObject result;
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(AskTimeout);
        try
        {
            result = await RunAsync(engine, cts);
        }
        catch (InvalidDataException) when (shown.Length > 0)
        {
            // Its answer came through; only the list of sources after it didn't.
            Record(engine, true, "");
            result = new JsonObject { ["answer"] = Py.Strip(shown), ["sources"] = new JsonArray() };
        }
        catch (Exception ex) when (ex is InvalidOperationException or HttpRequestException or TimeoutException && shown.Length > 0)
        {
            Record(engine, false, ex.Message);
            throw new InvalidOperationException($"{Engines.Name(engine)} stopped partway through: {ex.Message}", ex);
        }
        catch (Exception ex) when (ex is InvalidOperationException or InvalidDataException or HttpRequestException or TimeoutException or OperationCanceledException)
        {
            if (ex is OperationCanceledException && ct.IsCancellationRequested) throw; // the caller gave up; not ours to paper over
            bool timedOut = ex is OperationCanceledException;
            Record(engine, false, timedOut ? "timed out" : ex.Message);
            if (engine == "ollama" || !settings.Fallback) throw;
            why = timedOut ? $"{Engines.Name(engine)} didn't respond in time." : $"{Engines.Name(engine)} didn't answer: {ex.Message}";
            fellBack = true;
            engine = "ollama";
            using var cts2 = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts2.CancelAfter(AskTimeout);
            result = await RunAsync(engine, cts2);
        }

        var sources = (result["sources"] as JsonArray ?? []).Select(s => new AskSource(
            Py.AsString(s?["id"]), Py.AsString(s?["title"]) ?? "", Py.AsString(s?["class"]), Py.AsString(s?["date"]),
            s?["at"] is JsonValue av && av.TryGetValue(out double atv) ? atv : null, Py.AsString(s?["section"]) ?? "")).ToList();
        return new AskReply(Py.AsString(result["answer"]) ?? "", sources, engine, Engines.Name(engine))
        {
            Asked = asked, AskedName = Engines.Name(asked), FellBack = fellBack, Why = why,
        };
    }
}
