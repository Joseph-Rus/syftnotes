using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using StudyStash.Core.Rich;

namespace StudyStash.Core;

/// <summary>
/// Composing an illustration from ready-made parts instead of drawing it: Study Stash shortlists the library's parts
/// that fit the plan (by its words; no model asked), a fast model answers with a small scene (which parts, where, a few
/// simple shapes where no part exists, which part each label points at), and Study Stash draws it (<see cref="PartsScene"/>).
/// Seconds where a drawing takes minutes. When the library has nothing for the subject, or the composer says it can't,
/// the illustration is drawn as before.
/// </summary>
public static partial class IllustrationDesign
{
    /// <summary>The most parts the composer is shown, and how well the best must match for it to be asked at all.</summary>
    public const int Catalogue = 40;

    /// <summary>The fewest of the plan's parts the library must seem to have (by their words) for composing to be
    /// tried: below it, the subject is drawn.</summary>
    public const double MinCoverage = 0.4;

    /// <summary>The fewest of the plan's parts a composed illustration must label to be kept.</summary>
    public const double MinLabelled = 0.7;

    /// <summary>How long a composer may take before the drawing is asked for instead.</summary>
    public static readonly TimeSpan ComposeTimeout = TimeSpan.FromMinutes(3);

    /// <summary>The library composing uses (a test may give its own).</summary>
    public static PartsLibrary Library { get; set; } = PartsLibrary.Default;

    /// <summary>The parts an illustration could be composed of, best first, and how much of the plan they seem to
    /// cover: the share of its parts whose name's words are in a shortlisted part's name, regions or tags.</summary>
    public static (IReadOnlyList<LibraryPart> Parts, double Coverage) Shortlist(IllustrationPlan plan, PartsLibrary library)
    {
        var words = new StringBuilder();
        words.Append(plan.Title).Append(' ').Append(plan.Subject).Append(' ').Append(plan.Subject).Append(' ').Append(plan.Layout).Append(' ');
        foreach (var p in plan.Parts) words.Append(p.Name).Append(' ').Append(p.Name).Append(' ').Append(p.Note).Append(' ');
        var scored = library.Shortlist(words.ToString(), Catalogue * 2);
        if (scored.Count == 0) return ([], 0);
        // Only parts in the same league as the best: a passing word doesn't earn a place.
        double best = scored[0].Score;
        var parts = scored.Where(s => s.Score >= best * 0.12).Take(Catalogue).Select(s => s.Part).ToList();
        var vocabulary = new HashSet<string>(parts.SelectMany(p => PartsLibrary.Terms(string.Join(' ', new[] { p.Name }.Concat(p.Tags).Concat(p.Regions.Select(r => r.Name + " " + r.Id.Replace('-', ' ') + " " + string.Join(' ', r.Tags)))))));
        int covered = plan.Parts.Count(p => PartsLibrary.Terms(p.Name + " " + p.Id.Replace('-', ' ')).Any(vocabulary.Contains));
        return (parts, plan.Parts.Count == 0 ? 0 : (double)covered / plan.Parts.Count);
    }

    /// <summary>What the composer is asked: the plan, the catalogue (each part's name, view, proportions, real size,
    /// regions and ports) and the scene to answer with.</summary>
    public static string ComposePrompt(Meeting m, IllustrationPlan plan, IReadOnlyList<LibraryPart> catalogue)
    {
        var sb = new StringBuilder();
        sb.Append("You compose a labelled illustration for a university student's study notes from ready-made parts, the way a textbook figure is assembled: you pick parts from the catalogue, place and size them, add a few simple shapes only for what no part shows, and say which part or region each label points at. You don't draw: Study Stash draws the parts and the labels from your plan.\n\n");
        sb.Append("<lecture>\n").Append(Summarize.Header(m)).Append("\n</lecture>\n\n");
        sb.Append("## The figure\n\nTitle: ").Append(plan.Title).Append("\nWhat it shows: ").Append(plan.Subject).Append("\nLayout: ").Append(plan.Layout).Append('\n');
        sb.Append("Labels, one for each of these parts (id: name, what the lecture said of it):\n");
        foreach (var p in plan.Parts) sb.Append("- ").Append(p.Id).Append(": ").Append(p.Name).Append(p.Note.Length > 0 ? " — " + p.Note : "").Append('\n');
        sb.Append("\n## Catalogue (use only these)\n\nEach: id — name [the view it's drawn from] width×height in its own proportions, its real size; regions (named pieces of it a label can point at); ports (points on it, as fractions of its width and height from its top left).\n");
        foreach (var p in catalogue)
        {
            sb.Append("- ").Append(p.Id).Append(" — ").Append(p.Name);
            if (p.View.Length > 0) sb.Append(" [").Append(p.View).Append(']');
            sb.Append(F($" {p.Box.W / Math.Max(p.Box.W, p.Box.H) * 100:0}×{p.Box.H / Math.Max(p.Box.W, p.Box.H) * 100:0}"));
            if (p.Size.Length > 0) sb.Append(", ").Append(p.Size);
            if (p.Regions.Count > 0) sb.Append("; regions: ").Append(string.Join(", ", p.Regions.Select(r => r.Id)));
            if (p.Ports.Count > 0) sb.Append("; ports: ").Append(string.Join(", ", p.Ports.Select(q => F($"{q.Id} ({(q.X - p.Box.X) / p.Box.W:0.##},{(q.Y - p.Box.Y) / p.Box.H:0.##})"))));
            sb.Append('\n');
        }
        sb.Append($$"""

            ## Your answer

            JSON only, in this shape:
            {"compose": true,
             "parts": [{"as": "stand", "use": "burette-on-stand", "x": 500, "y": 400, "w": 300},
                       {"as": "flask", "use": "conical-flask", "w": 90, "on": "base", "at": "stand.base-top"}],
             "shapes": [{"as": "tile", "kind": "rect", "x": 640, "y": 700, "w": 120, "h": 8, "material": "light-metal", "under": true}],
             "labels": [{"part": "burette", "to": "stand.burette"}, {"part": "conical-flask", "to": "flask", "fact": "holds the analyte"}]}

            - "parts": what to draw, back to front. "as" is your short name for it (lower case); "use" a catalogue id. Place it by its middle ("x", "y") in a canvas about 1000 wide, y going down, and give its width "w" (its height follows its proportions); or put one of its ports ("on") on a port or region of a part placed before it ("at": "name.port") or on a point [x, y]. "turn" (degrees, clockwise) and "flip": true (mirrored) when the view needs them. Size parts to each other as the real things are; parts drawn to scale (their size says so) take the scale of one placed before them with "like": "name" instead of a width.
            - One part may be the whole figure: when the catalogue has the thing itself (a heart, a board, a drone, a cell), use it, and point the labels at its regions. Compose from several parts when the figure is a set-up (apparatus on a bench, components wired together, organelles in a cell).
            - "shapes", only for what no part shows, at most {{PartsScene.MaxShapes}}: "ellipse" or "rect" (middle "x", "y", size "w", "h"), "blob" (a closed smooth outline through "points"), "band" (a smooth strip of "width" through "points": a tendon, a wire, a tube, a vessel), "arrow" (through "points", for a movement or a flow). A point is [x, y] or "name.port": route a band along a part (a tendon up a finger, a wire to a pin) through that part's ports, in order. "material" one of: {{string.Join(", ", PartsScene.MaterialNames)}}. "under": true draws it behind the parts.
            - "labels": one for each part listed above, by its id ("part"), pointing ("to") at a placed part ("name"), one of its regions ("name.region"), one of its ports ("name.port": a small ring marks the spot) or a shape ("name"); never two labels at the same thing. A region is only ever the label for what it's named: when a part's region has the label's name, point at it; when nothing shows a labelled thing, add a shape for it, or point at the port nearest where it is. "fact" (optional, a few words, at most 40 characters) is a number or direction the lecture stressed, on a few labels at most. "at": [x, y] (optional) is where on its target the leader should end, when that matters (inside a cell's cytoplasm clear of the organelles, on a membrane's edge).
            - The figure must be true to the lecture and to the real thing: the view, where each part sits, how big it is next to the others.
            - If these parts can't make a figure a student could learn this subject from (the thing isn't in the catalogue, or the view is wrong), answer {"compose": false, "why": "..."} and it will be drawn instead.
            """);
        return sb.ToString().ReplaceLineEndings("\n");
    }

    /// <summary>What composing came to: the illustration (null when it couldn't be composed), why not or what was
    /// left out, what still looks off, and whether it came from the cache.</summary>
    public sealed record Composed(DesignedDiagram? Drawn, IReadOnlyList<string> Dropped, IReadOnlyList<string> Problems, bool Cached);

    /// <summary>
    /// Composes <paramref name="plan"/> from the library: shortlists, asks <paramref name="compose"/> once (unless the
    /// same plan was composed before: then it's drawn again from the scene kept in <paramref name="cache"/>, no model
    /// asked), reads the scene strictly, draws it, and keeps it only when it's safe, true to the lecture
    /// (<paramref name="grounds"/>) and labels most of the plan. Never throws but for cancelling.
    /// </summary>
    public static async Task<Composed> ComposeAsync(Meeting m, IllustrationPlan plan, string grounds, Func<string, Task<string>> compose,
        SceneCache? cache = null, PartsLibrary? library = null)
    {
        library ??= Library;
        var dropped = new List<string>();
        try
        {
            var (catalogue, coverage) = Shortlist(plan, library);
            if (catalogue.Count == 0 || coverage < MinCoverage)
                return new Composed(null, [$"the parts library has little for this ({coverage:P0} of its parts)"], [], false);
            string key = SceneCache.Key(plan, library);
            bool cached = false;
            string? answer = cache?.Get(key);
            if (answer is not null) cached = true;
            else answer = await compose(ComposePrompt(m, plan, catalogue)).WaitAsync(ComposeTimeout);
            var allowed = catalogue.ToDictionary(p => p.Id, StringComparer.Ordinal);
            if (PartsScene.Read(answer, allowed, plan.Parts, dropped) is not { } scene) return new Composed(null, dropped, [], cached);
            if (PartsScene.Draw(scene, plan, library) is not { } drawn) return new Composed(null, [.. dropped, "nothing in it could be drawn"], [], cached);
            if (drawn.Labelled.Count < Math.Max(MinParts, Math.Ceiling(plan.Parts.Count * MinLabelled)))
                return new Composed(null, [.. dropped, $"it labels {drawn.Labelled.Count} of the {plan.Parts.Count} parts"], [], cached);
            if (SafeSvg.Clean(drawn.Svg) is { Problem: { } problem }) return new Composed(null, [.. dropped, problem.TrimEnd('.')], [], cached);
            if (Unfaithful(drawn.Svg, grounds) is { } untrue) return new Composed(null, [.. dropped, untrue], [], cached);
            var labelled = plan.Parts.Where(p => drawn.Labelled.Contains(p.Id)).ToList();
            var unlabelled = plan.Parts.Where(p => !drawn.Labelled.Contains(p.Id)).Select(p => $"“{p.Name}” isn't in the parts").ToList();
            // A composed figure is made of whole, finished parts: only the callouts and the parts' places are checked.
            var problems = SvgLint.Problems(drawn.Svg, labelled).Where(p => !p.StartsWith("It's drawn too simply", StringComparison.Ordinal)).ToList();
            if (!cached) cache?.Put(key, answer);
            string caption = drawn.Credits.Count == 0 ? plan.Caption : $"{plan.Caption.TrimEnd()} Parts: {string.Join("; ", drawn.Credits)}.";
            return new Composed(new DesignedDiagram(plan.Title, plan.After, plan.At, NoteBlockKind.Svg, drawn.Svg, caption), [.. dropped, .. unlabelled], problems, cached);
        }
        catch (TimeoutException)
        {
            return new Composed(null, [.. dropped, "composing took too long"], [], false);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            return new Composed(null, [.. dropped, e.Message], [], false);
        }
    }

    static string F(FormattableString s) => s.ToString(CultureInfo.InvariantCulture);
}

/// <summary>
/// Scenes composed before, by plan: the same subject planned the same way (a class that comes back to the drone) is
/// drawn again from its scene at once, no model asked. Kept in memory, and in a folder when given one (one small JSON
/// file a scene); a scene is only ever drawn through <see cref="PartsScene.Read"/>, so a file that's been tampered
/// with can't put anything unsafe on the page.
/// </summary>
public sealed class SceneCache(string? folder = null)
{
    readonly ConcurrentDictionary<string, string> memory = new(StringComparer.Ordinal);

    /// <summary>A plan's key: its subject, layout and parts (ids and names), and the library's version.</summary>
    public static string Key(IllustrationPlan plan, PartsLibrary library)
    {
        var text = new StringBuilder(library.Version).Append('\n').Append(plan.Subject.Trim().ToLowerInvariant()).Append('\n').Append(plan.Layout.Trim().ToLowerInvariant());
        foreach (var p in plan.Parts.OrderBy(p => p.Id, StringComparer.Ordinal)) text.Append('\n').Append(p.Id).Append('=').Append(p.Name.ToLowerInvariant());
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())))[..24];
    }

    public string? Get(string key)
    {
        if (memory.TryGetValue(key, out var scene)) return scene;
        if (folder is null) return null;
        try
        {
            string file = Path.Combine(folder, key + ".json");
            if (!File.Exists(file) || new FileInfo(file).Length > 64 * 1024) return null;
            return memory[key] = File.ReadAllText(file);
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    public void Put(string key, string scene)
    {
        memory[key] = scene;
        if (folder is null) return;
        try
        {
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, key + ".json"), scene);
        }
        catch (IOException)
        {
            // A scene that can't be kept is composed again next time.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
