using System.Text.Json;
using System.Text.Json.Nodes;

namespace StudyStash.Core.Ai;

/// <summary>Which AI does one kind of work, and with which of its models ("" is its default).</summary>
public sealed record AiChoice(string Provider, string Model = "");

/// <summary>
/// Which AI does what, kept in ai.json beside config.toml (so config.toml stays as the Python engine writes it).
/// One AI is picked for everything, and each kind of work may use another:
/// <list type="bullet">
/// <item><c>notes</c>: writing study notes from a transcript;</item>
/// <item><c>sort</c>: filing a lecture under its class;</item>
/// <item><c>ask</c>: answering questions from your notes;</item>
/// <item><c>agent</c>: work that reads around and uses tools (Canvas, the chat that can change notes).</item>
/// </list>
/// Rich notes (diagrams, formula plots and drawings) have their own switches (<see cref="RichNotes"/> and one for each kind)
/// and a pick of who designs them (<see cref="Diagrams"/>): a separate pass after the notes, automatic by default.
/// <see cref="Speed"/> says how fast Claude Code writes the notes and designs them.
/// With no ai.json nobody has been chosen yet: <see cref="Provider"/> is empty, nothing writes the notes (they wait for
/// the student to pick an AI), and sorting and Ask keep to the library's own Ollama when it's there, as they always did.
/// </summary>
public sealed class AiSettings
{
    public static readonly string[] Jobs = ["notes", "sort", "ask", "agent"];

    /// <summary>The AI picked for everything, or "" while nobody has been chosen yet (a fresh library).</summary>
    public string Provider { get; set; } = "";
    /// <summary>Which shape of ai.json this is. Older files have none (0): their "ollama" may only be what a fresh library
    /// used to say, which <see cref="Migrated"/> sorts out once; every file saved from now on is stamped, so a pick the
    /// student made is never taken for it.</summary>
    public int Version { get; set; }
    public const int CurrentVersion = 1;
    /// <summary>Per kind of work: its own provider and model, when it isn't the one above.</summary>
    public Dictionary<string, AiChoice> ByJob { get; set; } = [];
    /// <summary>Per provider: the model to use when a job doesn't name one.</summary>
    public Dictionary<string, string> Models { get; set; } = [];
    /// <summary>Per provider: "works", or what went wrong the last time it was tried.</summary>
    public Dictionary<string, string> Tests { get; set; } = [];
    /// <summary>The terminal "Open in Claude Code" uses (ghostty, terminal, iterm, warp).</summary>
    public string Terminal { get; set; } = "ghostty";
    /// <summary>Whether a question goes to Ollama when the engine asked (or picked as a default) can't answer.</summary>
    public bool Fallback { get; set; } = true;
    /// <summary>Per provider: until when its usage limit lasts (ISO 8601), while still in the future.</summary>
    public Dictionary<string, string> Limits { get; set; } = [];
    /// <summary>Problem ids the student has closed, hidden until the problem changes (a new usage limit, say).</summary>
    public List<string> Dismissed { get; set; } = [];
    /// <summary>Who draws the diagrams in the notes: <c>auto</c>, <c>notes</c> (the notes engine, as it writes),
    /// <c>off</c>, or an engine's id (<see cref="DiagramEngines"/>).</summary>
    public string Diagrams { get; set; } = DiagramEngines.Auto;
    /// <summary>Rich notes: diagrams, formula plots and drawings added after the notes. Off: plain notes, and nothing extra
    /// is asked of the AI (a stored <see cref="Diagrams"/> of <c>off</c>, from before this switch, means the same).</summary>
    public bool RichNotes { get; set; } = true;
    /// <summary>Which kinds of rich notes (<see cref="Kinds"/>), while they're on.</summary>
    public bool RichDiagrams { get; set; } = true;
    public bool RichPlots { get; set; } = true;
    public bool RichDrawings { get; set; } = true;
    /// <summary>How fast Claude Code writes the notes and designs the rich notes: <see cref="AiSpeed"/>.</summary>
    public string Speed { get; set; } = AiSpeed.Standard;

    public static string PathIn(string home) => System.IO.Path.Combine(home, "ai.json");

    /// <summary>What does this kind of work.</summary>
    public AiChoice For(string job)
    {
        if (ByJob.TryGetValue(job, out var c) && c.Provider.Length > 0)
            return c.Model.Length > 0 ? c : c with { Model = Models.GetValueOrDefault(c.Provider, "") };
        // Nobody chosen: the notes have no writer ("" here), while sorting and Ask keep to the library's own Ollama.
        if (Provider.Length == 0) return job == "notes" ? new AiChoice("") : new AiChoice("ollama", Models.GetValueOrDefault("ollama", ""));
        return new AiChoice(Provider, Models.GetValueOrDefault(Provider, ""));
    }

    /// <summary>Whether the student (or setup) has chosen an AI for this kind of work: its own, or the one for everything.</summary>
    public bool Picked(string job) => (ByJob.TryGetValue(job, out var c) && c.Provider.Length > 0) || Provider.Length > 0;

    /// <summary>Whether any AI is chosen to write the notes. False on a fresh library: no AI writes the notes yet.</summary>
    public bool NotesChosen => Picked("notes");

    /// <summary>
    /// A library from before "nobody chosen yet" existed, whose ai.json says Ollama only because that was what a fresh
    /// library said, becomes one where nobody is chosen: nothing was picked (no per-job pick, no model), Ollama isn't on
    /// this computer, and no note was ever written by it. Anything else is kept as the student has it, an ai.json that
    /// is already stamped, a pick of another AI, and an Ollama that is here or has written notes (<paramref name="wroteNotes"/>).
    /// Returns whether it changed; the stamp is set either way, so this never runs twice.
    /// </summary>
    public bool Migrated(bool ollamaInstalled, bool ollamaWroteNotes)
    {
        if (Version >= CurrentVersion) return false;
        Version = CurrentVersion;
        if (Provider != "ollama" || ByJob.Count > 0 || Models.Count > 0 || ollamaInstalled || ollamaWroteNotes) return false;
        Provider = "";
        return true;
    }

    /// <summary>The kinds of rich notes to make now: none when rich notes are off (or diagrams were turned off the way
    /// they were before the switch), else those switched on.</summary>
    public RichKinds Kinds() => !RichNotes || DiagramEngines.Normal(Diagrams) == DiagramEngines.Off ? RichKinds.None
        : (RichDiagrams ? RichKinds.Diagrams : 0) | (RichPlots ? RichKinds.Plots : 0) | (RichDrawings ? RichKinds.Drawings : 0);

    /// <summary>
    /// Changes the rich notes switches as a student's click would: whatever is given. Switching them on from diagrams
    /// that were turned off the old way (<see cref="Diagrams"/> = off) goes back to automatic, and rich notes with no
    /// kind left are no rich notes: switching on with none on puts every kind on, and switching off the last kind
    /// switches rich notes off (every kind back on, for the next time).
    /// </summary>
    public void SetRich(bool? on = null, bool? diagrams = null, bool? plots = null, bool? drawings = null)
    {
        if (diagrams is { } d) RichDiagrams = d;
        if (plots is { } p) RichPlots = p;
        if (drawings is { } w) RichDrawings = w;
        if (on is { } o)
        {
            RichNotes = o;
            if (o && DiagramEngines.Normal(Diagrams) == DiagramEngines.Off) Diagrams = DiagramEngines.Auto;
        }
        if (RichDiagrams || RichPlots || RichDrawings) return;
        if (on == true) RichDiagrams = RichPlots = RichDrawings = true;
        else
        {
            RichNotes = false;
            RichDiagrams = RichPlots = RichDrawings = true;
        }
    }

    /// <summary>True when a job runs on the library's own Ollama, the way the engine always has.</summary>
    public bool Local(string job) => For(job).Provider == "ollama";

    static readonly JsonSerializerOptions Options = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    public static AiSettings Load(string home)
    {
        string path = PathIn(home);
        if (!File.Exists(path)) return new AiSettings();
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                return JsonSerializer.Deserialize<AiSettings>(File.ReadAllText(path), Options) ?? new AiSettings();
            }
            catch (JsonException)
            {
                return new AiSettings();
            }
            catch (IOException) when (attempt < 5)
            {
                // Windows refuses a read while the file is being written (an engine's run is recorded in it after every
                // call): a moment later it reads.
                Thread.Sleep(25 * attempt);
            }
        }
    }

    public void Save(string home)
    {
        Directory.CreateDirectory(home);
        Version = CurrentVersion; // the library runs Migrated at start, before anything else saves, so this never hides an unsorted old file
        string text = JsonSerializer.Serialize(this, Options) + "\n";
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                Py.WriteText(PathIn(home), text);
                return;
            }
            catch (IOException) when (attempt < 5)
            {
                // Windows refuses a write while the file is being read (the diagram pass checks the switches): try again.
                Thread.Sleep(25 * attempt);
            }
        }
    }

    /// <summary>As the API shows it: the choice, each job's, and every provider with whether it's here.</summary>
    public JsonObject ToJson()
    {
        var jobs = new JsonObject();
        foreach (string j in Jobs)
        {
            var c = For(j);
            jobs[j] = new JsonObject { ["provider"] = c.Provider, ["model"] = c.Model, ["own"] = ByJob.ContainsKey(j) };
        }
        var providers = new JsonArray();
        foreach (var p in AiProviders.All())
            providers.Add(new JsonObject
            {
                ["id"] = p.Id, ["name"] = p.Name, ["installed"] = p.Available(), ["site"] = p.Site,
                ["test"] = Tests.GetValueOrDefault(p.Id, ""),
                ["models"] = new JsonArray(p.Models.Select(m => (JsonNode)new JsonObject { ["id"] = m.Id, ["label"] = m.Label }).ToArray()),
            });
        return new JsonObject { ["provider"] = Provider, ["jobs"] = jobs, ["providers"] = providers };
    }
}
