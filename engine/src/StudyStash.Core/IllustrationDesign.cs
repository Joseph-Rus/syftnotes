using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using StudyStash.Core.Rich;

namespace StudyStash.Core;

/// <summary>An illustration the designer planned: what it shows and from where, where things go, and the parts it
/// names (each from the lecture), with its title, caption, the section it goes after and the moment it comes from.</summary>
public sealed record IllustrationPlan(string Title, string After, double? At, string Caption, string Subject, string Layout, IReadOnlyList<Illustration.Planned> Parts);

/// <summary>
/// Illustrations: when a lecture describes a physical thing at length (a drone, the bones of the hand, the heart cut
/// open, a circuit board, a robot arm), its notes get a detailed, labelled drawing of it, like a textbook's figure,
/// whose parts a student can point at. The diagram designer decides and plans one in the same reply as its diagrams
/// (<see cref="Brief"/>, read by <see cref="ReadPlans"/>): the subject and view, the layout, and the parts the lecture
/// named, each with a line from what was said. Then an illustrator draws it from the plan in a house style, in layers
/// (<see cref="DrawPrompt"/>); the drawing's parts are named from the plan (<see cref="Illustration.Name"/>), checked
/// against the lecture, and looked over (<see cref="SvgLint"/>): its callouts are spaced and kept on the canvas
/// (<see cref="Callouts.Tidy"/>), and one still wrong goes back once while there's time. Only engines that draw well
/// (the cloud engines) are asked; a weak local model never is.
/// </summary>
public static partial class IllustrationDesign
{
    /// <summary>The most parts an illustration names, and the fewest worth naming.</summary>
    public const int MaxParts = 18, MinParts = 3;

    /// <summary>The drawing's width, in its own units; its height is the illustrator's, up to <see cref="MaxHeight"/>.</summary>
    public const int Width = 720, MaxHeight = 760;

    /// <summary>How much time must be left for an illustration that would look wrong to go back once (only what changes
    /// comes back, so a fix is quicker than the drawing).</summary>
    public static readonly TimeSpan RevisionRoom = TimeSpan.FromSeconds(200);

    /// <summary>How long a diagram pass that draws an illustration may take, all told: the designer's few minutes and
    /// the drawing's (8 to 14 minutes for a detailed one, written out shape by shape).</summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromMinutes(18);

    /// <summary>How many illustrations a lecture may get: none when it's too short for a diagram, else one (two for a
    /// long lecture).</summary>
    public static int Cap(string transcript)
    {
        int diagrams = DiagramDesign.Cap(transcript);
        return diagrams == 0 ? 0 : diagrams >= DiagramDesign.MaxDiagrams ? 2 : 1;
    }

    // --- the designer's brief ------------------------------------------------------------------------------------------

    /// <summary>What the designer is told about illustrations: when one earns its place, and how to plan it.</summary>
    public static string Brief(int cap) => $$"""
        ## Illustrations

        Besides diagrams, a lecture that describes a physical thing at length may get {{(cap == 1 ? "one detailed illustration" : $"up to {cap} detailed illustrations")}}: a labelled drawing of the thing itself, like a figure in a good textbook, which an illustrator draws from your plan. Plan one only when all of these hold:
        - the lecturer described a real, physical thing: what it looks like, its parts and where each one sits (a quadcopter's frame, motors and battery; the bones and tendons of the hand; the heart cut open; a circuit board; a robot arm; a cell; an engine; a plant; a piece of lab equipment)
        - they spent some time on it, naming several parts and saying where they are, so seeing the thing is how a student learns it
        - it can be shown from one view (from above, from the side, from the front, cut open)
        Plan none for a process or a sequence (that's a flowchart), an idea, software, data, or a thing only mentioned. When in doubt, plan none: no illustration is a normal, correct answer. An illustration isn't instead of a diagram: a lecture may have both, of different ideas.

        Plan it in "illustrations":
        {"title": "...", "after": "...", "at": "...", "subject": "...", "layout": "...", "parts": [{"id": "front-left-motor", "name": "Front-left motor", "note": "..."}], "caption": "..."}
        - "subject": what it is and the view, in one sentence (a quadcopter seen from directly above, front at the top).
        - "layout": where the main parts sit in the picture, in one or two sentences, as the lecturer described them.
        - "parts": {{MinParts}} to {{MaxParts}} of the parts the lecturer named, the most important first. "id": lower case with hyphens; "name": one to four words, as the lecturer said it; "note": one line (15 words at most) from what the lecturer said about it: what it does, where it is, its numbers.
        - "title", "after", "at" and "caption" as for a diagram; the caption says what the picture shows and what to notice in it.
        - Every part, name and note comes from what the lecturer said, as for diagrams.
        """.ReplaceLineEndings("\n");

    /// <summary>The field the designer's JSON answer adds for illustrations, and what it's for.</summary>
    public const string AnswerField = "\"illustrations\": [] (or the illustrations you plan, as above)";

    // --- reading the plan -------------------------------------------------------------------------------------------

    [GeneratedRegex(@"[^a-z0-9]+")]
    private static partial Regex NotId();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();

    /// <summary>
    /// The illustrations the designer planned, read strictly: each needs a title, a caption, a subject, a heading of
    /// these notes to go under, a moment inside the lecture (or none) and at least <see cref="MinParts"/> parts with a
    /// name; ids are made plain and unique. One that misses any of it is left out, with a word on why.
    /// </summary>
    public static (IReadOnlyList<IllustrationPlan> Plans, IReadOnlyList<string> Dropped) ReadPlans(JsonObject reply, IReadOnlyList<string> headings, double? end)
    {
        if (reply["illustrations"] is not JsonArray list) return ([], []);
        var plans = new List<IllustrationPlan>();
        var dropped = new List<string>();
        for (int i = 0; i < list.Count; i++)
        {
            var (plan, why) = Check(list[i] as JsonObject, headings, end);
            if (plan is not null) plans.Add(plan);
            else dropped.Add($"illustration {i + 1}: {why}");
        }
        return (plans, dropped);
    }

    static (IllustrationPlan?, string) Check(JsonObject? o, IReadOnlyList<string> headings, double? end)
    {
        if (o is null) return (null, "not an object");
        string title = Clip(Str(o["title"]), DiagramDesign.MaxTitle);
        if (title.Length == 0) return (null, "no title");
        string caption = Clip(Str(o["caption"]), DiagramDesign.MaxCaption);
        if (caption.Length == 0) return (null, $"“{title}” has no caption");
        string subject = Clip(Str(o["subject"]), 300);
        if (subject.Length == 0) return (null, $"“{title}” doesn't say what it shows");
        string after = OneLine(Str(o["after"]));
        if (headings.Count > 0 && !headings.Any(h => Norm(h) == Norm(after))) return (null, $"“{title}” goes after “{after}”, which isn't a heading in the notes");
        double? at = null;
        if (end is double last && OneLine(Str(o["at"])) is { Length: > 0 } said)
        {
            if (Seconds(said) is not double s) return (null, $"“{title}” comes from “{said}”, which isn't a time");
            if (s > last + 120) return (null, $"“{title}” comes from {said}, after the lecture ends");
            at = s;
        }
        var parts = new List<Illustration.Planned>();
        if (o["parts"] is JsonArray ps)
            foreach (var p in ps.OfType<JsonObject>())
            {
                string name = Clip(Str(p["name"]), 60);
                if (name.Length == 0) continue;
                string id = NotId().Replace((Str(p["id"]) is { Length: > 0 } given ? given : name).ToLowerInvariant(), "-").Trim('-');
                if (id.Length == 0 || !char.IsAsciiLetter(id[0])) id = "part-" + id;
                if (id is "art" or "labels" || id.StartsWith("label-", StringComparison.Ordinal)) id = "part-" + id;
                string unique = id;
                for (int n = 2; parts.Any(x => x.Id == unique); n++) unique = $"{id}-{n}";
                parts.Add(new Illustration.Planned(unique, name, Clip(Str(p["note"]), 160)));
                if (parts.Count == MaxParts) break;
            }
        if (parts.Count < MinParts) return (null, $"“{title}” names {parts.Count} parts, too few to be worth drawing");
        return (new IllustrationPlan(title, after, at, caption, subject, Clip(Str(o["layout"]), 600), parts), "");
    }

    [GeneratedRegex(@"^\[?(?:(\d+):)?(\d{1,2}):(\d{2})\]?$")]
    private static partial Regex Clock();

    static double? Seconds(string clock)
    {
        var m = Clock().Match(clock);
        if (!m.Success) return null;
        double h = m.Groups[1].Success ? double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) : 0;
        return h * 3600 + double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture) * 60 + double.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture);
    }

    static string Str(JsonNode? n) => n is JsonValue v && v.GetValueKind() == JsonValueKind.String ? v.GetValue<string>() : "";

    static string OneLine(string s) => Spaces().Replace(s, " ").Trim();

    static string Clip(string s, int max)
    {
        string t = OneLine(s.Replace("*", "").Replace("`", ""));
        if (t.Length <= max) return t;
        int cut = t.LastIndexOf(' ', max - 1);
        return t[..(cut > max / 2 ? cut : max - 1)].TrimEnd(',', ';', ':', ' ') + "…";
    }

    static string Norm(string heading) =>
        OneLine(heading.TrimStart('#').Replace("*", "").Replace("`", "").Replace("_", " ")).TrimEnd(':', ' ').ToLowerInvariant();

    // --- the illustrator's brief ------------------------------------------------------------------------------------

    /// <summary>The house palette, by material: a light tint, the base colour, a shade, and the outline, darkest.</summary>
    public static readonly (string Material, string Light, string Base, string Shade, string Line)[] Palette =
    [
        ("skin", "#FBE3D2", "#F2C6A6", "#DCA07C", "#A86F52"),
        ("bone, cartilage", "#FCF6E8", "#F2E4C4", "#DCC59A", "#A88C5C"),
        ("tendon, ligament", "#F7F4EE", "#E8E1D3", "#CBC1AC", "#8F8670"),
        ("muscle", "#F7B5AC", "#E5867A", "#C65E53", "#8E3A32"),
        ("artery, oxygenated blood", "#F7A39B", "#E2574E", "#BB3B34", "#82231E"),
        ("vein, deoxygenated blood", "#B9CDF3", "#7C9BDB", "#5273B5", "#2F4A85"),
        ("nerve, fat", "#FDF0BF", "#F6D872", "#D9B23C", "#9C7C1E"),
        ("light metal, aluminium", "#F3F5F7", "#D7DCE1", "#AEB6BE", "#6B747D"),
        ("dark metal, carbon, black plastic", "#7D8389", "#555B61", "#3B4045", "#24272B"),
        ("copper, brass, gold", "#F7D2AE", "#E6A874", "#C27F49", "#8A5225"),
        ("green circuit board, leaves", "#A8DDB5", "#4FAE73", "#2F8452", "#1B5634"),
        ("blue circuit board, blue plastic", "#A9D3F0", "#4A9AD1", "#2C74A8", "#174C75"),
        ("red plastic, warning", "#F9B4A8", "#EE6F5C", "#C9483A", "#8C2A20"),
        ("yellow, orange", "#FFE6A8", "#FFC85E", "#E89E2C", "#A86A12"),
        ("glass, water, membrane", "#E6F3FB", "#C3E2F5", "#8FC6E8", "#4F92BF"),
        ("purple (a part the lecture singles out)", "#E7D7F5", "#C39BE3", "#9B6BC4", "#6A4291"),
    ];

    /// <summary>
    /// What the illustrator is asked: the lecture (its notes, and its transcript to the length allowed), the plan, the
    /// look (a textbook figure in soft flat colour with gentle shading, true to the real thing), the order to draw it
    /// in, the callouts, the structure Study Stash reads its parts by, and the few rules that keep it safe to show.
    /// </summary>
    public static string DrawPrompt(Meeting m, string notes, IllustrationPlan plan, int maxChars = int.MaxValue)
    {
        string transcript = TimedText.HasTimes(m.Transcript)
            ? string.Join("\n", NoteBlocks.Lines(m.Transcript).Select(l => l.Trim()).Where(l => l.Length > 0))
            : Py.Strip(m.Transcript);
        var parts = new StringBuilder();
        foreach (var p in plan.Parts) parts.Append($"- id \"{p.Id}\": {p.Name}{(p.Note.Length > 0 ? " — " + p.Note : "")}\n");
        var palette = new StringBuilder();
        foreach (var (material, light, b, shade, line) in Palette) palette.Append($"- {material}: {light} light, {b} base, {shade} shade, {line} outline\n");
        string first = plan.Parts[0].Id, firstName = plan.Parts[0].Name;

        string Build(string t) => ($$"""
            You are a scientific illustrator. Draw one figure, in SVG, for a university student's study notes on the lecture below: a detailed, labelled illustration of the thing the lecturer described, as good as the figures in the best textbooks, that a student could learn its parts from and redraw in an exam.

            <lecture>
            {{Summarize.Header(m)}}
            </lecture>

            <transcript>
            {{t}}
            </transcript>

            <notes>
            {{notes}}
            </notes>

            ## The figure

            Title: {{plan.Title}}
            What it shows: {{plan.Subject}}
            Layout: {{plan.Layout}}
            Its parts, each drawn in a group with this exact id and labelled with this exact name:
            {{parts}}
            Draw it as the lecturer described it: the parts where they said they are, their shapes, numbers and directions as said (fix only obvious mis-hearings). Where the lecture doesn't say what something looks like, draw the real thing as it really looks. Label only the parts above.

            ## How it looks

            A clean textbook illustration in soft, flat colour with gentle shading: true to the real thing's shape and proportions, detailed, calm and easy to read. Not a diagram of boxes and circles: the thing itself.
            - Shape: each part has its real outline and proportions from this view. Organic shapes (bones, organs, a hand, a leaf) are smooth curves (path C, S, Q and A commands), never rectangles; machines have their real outlines: rounded corners, bevels, tapers, holes, fillets.
            - Detail: every part has the features that make it recognisable (on a motor, its bell, the gap round the stator and its screws; on a bone, its rounded head, its shaft and the joint surface; on a board, the pins of each chip and the traces), and small things the lecture mentioned are drawn too. About 200 to 500 shapes in all; most of the detail on the parts the lecture dwelt on. Repeat identical parts with a <symbol> and <use>.
            - Work in layers, in this order in the file: (1) each part's base shape and fill; (2) the shapes of its pieces; (3) fine detail (edges, seams, screws, creases, texture) in thinner lines; (4) shading: each surface filled with a linearGradient from its light tint (the light comes from the top left) to its shade, a few soft highlights (the light tint at fill-opacity 0.6), and the far side of round things a little darker; (5) the callouts, last, on top.
            - Colour: soft and natural for each material, from this palette (light, base and shade for fills and gradients, the outline colour for the edges):
            {{palette}}  Outline every shape in the outline colour of its own material (never black): 1.5 units for a part's outline, 0.75 to 1 for detail lines, stroke-linejoin="round". No background: the figure sits on the page, so nothing fills the whole canvas, and no white shapes (use the light tints). #1D1D1F and #6E6E73 are only for the callouts' words and lines.
            - Callouts: every part above gets one: a thin leader line from a point on the part (a small dot there) out to its name, in the margin. Labels go in columns along the left and right of the figure (or above and below a wide one), never on top of the drawing, in the order their parts sit so no two leader lines cross, at least 22 units apart. A name is 14 units, #1D1D1F; a key fact the lecture stressed (a direction, a number) may go on a second line under it at 12 units in #6E6E73, on a few labels at most. The canvas is wide enough for every label: at least 10 units between any word and the edge.

            ## Structure (Study Stash reads the parts by it: keep to it exactly)

            ```svg
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 {{Width}} H">
              <title>{{plan.Title}}</title>
              <defs>(the gradients, one per material and part, and any symbols)</defs>
              <g id="art">
                <g id="{{first}}">(everything that draws {{firstName}})</g>
                (one group like it for each part, with its id, behind first; detail that belongs to no part sits in "art" outside them)
              </g>
              <g id="labels" font-size="14">
                <g id="label-{{first}}">
                  <polyline points="(on the part) (the bend, if any) (by the words)" fill="none" stroke="#6E6E73" stroke-width="1"/>
                  <circle cx="(on the part)" cy="..." r="2.5" fill="#6E6E73"/>
                  <text x="..." y="..." fill="#1D1D1F" text-anchor="end">{{firstName}}</text>
                </g>
                (one callout like it for each part, its id "label-" and the part's id)
              </g>
            </svg>
            ```
            - viewBox "0 0 {{Width}} H", with H from 360 to {{MaxHeight}} to suit the subject's shape; no width or height.
            - A part that holds another (a board and a chip on it) holds its group inside its own.
            - Only these elements: svg, g, defs, title, desc, symbol, use, path, rect, circle, ellipse, line, polyline, polygon, text, tspan, linearGradient, radialGradient, stop, clipPath; style with attributes only (no style element or attribute, class, CSS, filter, mask, pattern, image, script, link or font).
            - Plain letters and digits in the words (no arrows or symbols: write "to", "clockwise").
            - At most 150 KB.

            Before you answer, check: each part above is drawn, recognisable and in its group with its exact id; each has its callout, its leader starting on the part; no two labels overlap and none runs off the canvas; no leader lines cross; nothing is drawn as a plain box where the real thing isn't one.

            Answer with only the SVG in a ```svg block, nothing before or after it.
            """).ReplaceLineEndings("\n");

        string whole = Build(transcript);
        if (whole.Length <= maxChars) return whole;
        int room = Math.Max(0, maxChars - Build("").Length - 80);
        return Build(transcript.Length <= room ? transcript : transcript[..Math.Max(0, transcript.LastIndexOf('\n', Math.Max(0, room - 1)))] + "\n[... the rest of the transcript didn't fit here ...]");
    }

    /// <summary>The one revision: the drawing as it came, what was found wrong with it, and the same rules, for just the
    /// groups that need to change (a part, a callout), redrawn with every problem fixed (<see cref="Patch"/> puts them in).</summary>
    public static string RevisePrompt(IllustrationPlan plan, string svg, IReadOnlyList<string> problems)
    {
        var sb = new StringBuilder();
        sb.Append("You drew this illustration for a university student's study notes. Study Stash measured it and found the problems listed under it. ")
            .Append("Fix every problem, keeping everything that was right: the same parts with the same ids, the same look, the same callouts' words. ")
            .Append("Send back only what changes: each part's group (<g id=\"...\">, whole, with everything in it) and each callout's group (<g id=\"label-...\">) that you redraw or add, ")
            .Append("and a <defs> with any new gradients; Study Stash puts each one in place of the group with its id. If the whole drawing must change, send all of it.\n\n");
        sb.Append($"Title: {plan.Title}\nWhat it shows: {plan.Subject}\nIts parts:\n");
        foreach (var p in plan.Parts) sb.Append($"- id \"{p.Id}\": {p.Name}\n");
        sb.Append("\n```svg\n").Append(svg.Trim()).Append("\n```\n\nProblems:\n");
        foreach (string p in problems) sb.Append("- ").Append(p).Append('\n');
        sb.Append($$"""

            Keep to the structure: each part a group with its id inside <g id="art">, each callout a group "label-" and the part's id inside <g id="labels"> (a leader polyline starting on the part, a small dot there, and the name). Attributes only, no style, class, filter, mask, pattern or image.

            Answer with only the changed groups in one ```svg block, wrapped in <svg xmlns="http://www.w3.org/2000/svg">...</svg>, nothing before or after it.
            """);
        return sb.ToString().ReplaceLineEndings("\n");
    }

    // --- drawing it ----------------------------------------------------------------------------------------------------

    /// <summary>What drawing the planned illustrations came to: those drawn (as diagrams of the notes, SVG), why any
    /// were left out, what still looks wrong with those drawn, and how many were redrawn after a look.</summary>
    public sealed record Outcome(IReadOnlyList<DesignedDiagram> Drawn, IReadOnlyList<string> Dropped, IReadOnlyList<string> Problems, int Revised)
    {
        public static readonly Outcome None = new([], [], [], 0);

        /// <summary>How many were composed from the parts library rather than drawn.</summary>
        public int Composed { get; init; }
    }

    /// <summary>
    /// Draws each plan (at most <see cref="Cap"/>, all at once): asks the illustrator (<paramref name="ask"/>, a plain
    /// answer), names the parts in what comes back, and keeps it if it's safe to show and true to the lecture
    /// (<paramref name="grounds"/>: the transcript and notes, lower case). Its callouts are tidied, then it's looked
    /// over; one that still looks wrong goes back once while <paramref name="left"/> leaves <see cref="RevisionRoom"/>,
    /// its redrawing used only when it's better. One not done by the time <paramref name="left"/> runs out is left
    /// out. Never throws but for cancelling.
    /// </summary>
    public static async Task<Outcome> DrawAllAsync(Meeting m, string notes, IReadOnlyList<IllustrationPlan> plans, string grounds,
        Func<string, Task<string>> ask, Func<TimeSpan> left, int maxPromptChars = int.MaxValue,
        Func<string, Task<string>>? compose = null, ComposedScenes? scenes = null)
    {
        if (plans.Count == 0) return Outcome.None;
        var cap = Cap(m.Transcript);
        var dropped = plans.Skip(cap).Select(p => $"“{p.Title}”: this lecture has room for {cap} illustration{(cap == 1 ? "" : "s")}").ToList();
        var runs = plans.Take(cap).Select(p => DrawAsync(m, notes, p, grounds, ask, left, maxPromptChars, compose, scenes)).ToList();
        var drawn = new List<DesignedDiagram>();
        var problems = new List<string>();
        int revised = 0, composed = 0;
        foreach (var run in runs)
        {
            var one = await run;
            if (one.Drawn is { } d) drawn.Add(d);
            if (one.Dropped is { } why) dropped.Add(why);
            if (one.Problems.Count > 0 && one.Drawn is { } still) problems.Add($"“{still.Title}”: {string.Join("; ", one.Problems)}");
            if (one.Revised) revised++;
            if (one.Composed) composed++;
        }
        return new Outcome(drawn, dropped, problems, revised) { Composed = composed };
    }

    sealed record One(DesignedDiagram? Drawn, string? Dropped, IReadOnlyList<string> Problems, bool Revised)
    {
        public bool Composed { get; init; }
    }

    /// <summary>One plan: composed from the parts library when <paramref name="compose"/> is given and the library has
    /// what it needs (seconds), else drawn (minutes).</summary>
    static async Task<One> DrawAsync(Meeting m, string notes, IllustrationPlan plan, string grounds, Func<string, Task<string>> ask,
        Func<TimeSpan> left, int maxPromptChars, Func<string, Task<string>>? compose, ComposedScenes? scenes)
    {
        string title = "“" + plan.Title + "”";
        if (compose is not null)
        {
            var c = await ComposeAsync(m, plan, grounds, compose, scenes);
            if (c.Drawn is { } made) return new One(made, null, c.Problems, false) { Composed = true };
        }
        try
        {
            var room = left() - TimeSpan.FromSeconds(20);
            if (room <= TimeSpan.Zero) return new One(null, $"{title}: no time left to draw it", [], false);
            string? svg = Drawing(await ask(DrawPrompt(m, notes, plan, maxPromptChars)).WaitAsync(room));
            if (svg is null) return new One(null, $"{title}: the illustrator's answer held no drawing", [], false);
            string named = Callouts.Tidy(Illustration.Name(svg, plan.Parts));
            if (SafeSvg.Clean(named) is { Problem: { } problem }) return new One(null, $"{title}: {problem.TrimEnd('.')}", [], false);
            if (Unfaithful(named, grounds) is { } untrue) return new One(null, $"{title}: {untrue}", [], false);
            var problems = SvgLint.Problems(named, plan.Parts);
            bool revised = false;
            if (problems.Count > 0 && left() > RevisionRoom)
            {
                try
                {
                    string? again = Drawing(await ask(RevisePrompt(plan, svg, problems)).WaitAsync(left() - TimeSpan.FromSeconds(20)));
                    if (again is not null && Patch(svg, again) is { } patched)
                    {
                        string fresh = Callouts.Tidy(Illustration.Name(patched, plan.Parts));
                        var after = SvgLint.Problems(fresh, plan.Parts);
                        if (SafeSvg.Clean(fresh).Problem is null && Unfaithful(fresh, grounds) is null && after.Count < problems.Count)
                        {
                            named = fresh;
                            problems = after;
                            revised = true;
                        }
                    }
                }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    // A redrawing that fails or runs late changes nothing: the first drawing stands.
                }
            }
            return new One(new DesignedDiagram(plan.Title, plan.After, plan.At, NoteBlockKind.Svg, named, plan.Caption), null, problems, revised);
        }
        catch (TimeoutException)
        {
            return new One(null, $"{title}: it took too long to draw", [], false);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            return new One(null, $"{title}: {e.Message}", [], false);
        }
    }

    /// <summary>
    /// A drawing with the groups a revision sent in place of those with the same ids (a new part's group goes at the end
    /// of the art, a new callout's at the end of the labels), and the gradients it sent added; a revision that sent a
    /// whole drawing (with its art and its labels) is the drawing. Null when either can't be read.
    /// </summary>
    public static string? Patch(string svg, string revision)
    {
        static System.Xml.Linq.XDocument? Read(string text)
        {
            try
            {
                var settings = new System.Xml.XmlReaderSettings { DtdProcessing = System.Xml.DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 800_000 };
                using var reader = System.Xml.XmlReader.Create(new StringReader(text), settings);
                return System.Xml.Linq.XDocument.Load(reader, System.Xml.Linq.LoadOptions.PreserveWhitespace);
            }
            catch (System.Xml.XmlException)
            {
                return null;
            }
        }
        if (Read(svg) is not { Root: { } root } doc || Read(revision) is not { Root: { } patch }) return null;
        static string? Id(System.Xml.Linq.XElement e) => (string?)e.Attribute("id");
        if (patch.Descendants().Any(e => Id(e) == "art") && patch.Descendants().Any(e => Id(e) == "labels") && patch.Attribute("viewBox") is not null) return revision;
        var ns = root.Name.Namespace;
        System.Xml.Linq.XElement Into(System.Xml.Linq.XElement e) => new(ns + e.Name.LocalName, e.Attributes(), e.Nodes().Select(n => n is System.Xml.Linq.XElement c ? Into(c) : n));
        var byId = root.Descendants().Where(e => Id(e) is { Length: > 0 }).GroupBy(e => Id(e)!).ToDictionary(g => g.Key, g => g.First());
        var defs = root.Elements().FirstOrDefault(e => e.Name.LocalName == "defs");
        if (defs is null) root.AddFirst(defs = new System.Xml.Linq.XElement(ns + "defs"));
        foreach (var d in patch.Descendants().Where(e => e.Name.LocalName == "defs").SelectMany(d => d.Elements()))
            if (Id(d) is not { } id || !byId.ContainsKey(id)) defs.Add(Into(d));
        // The outermost groups sent, each in place of its namesake.
        foreach (var g in patch.Descendants().Where(e => e.Name.LocalName == "g" && Id(e) is { Length: > 0 } && !e.Ancestors().Any(a => a.Name.LocalName == "g" && Id(a) is { Length: > 0 } && a != patch)).ToList())
        {
            string id = Id(g)!;
            if (id is "art" or "labels") continue;
            if (byId.TryGetValue(id, out var old)) old.ReplaceWith(Into(g));
            else (byId.GetValueOrDefault(id.StartsWith("label-", StringComparison.Ordinal) ? "labels" : "art") ?? root).Add(Into(g));
        }
        return root.ToString(System.Xml.Linq.SaveOptions.DisableFormatting);
    }

    [GeneratedRegex(@"<svg[\s\S]*</svg>", RegexOptions.IgnoreCase)]
    private static partial Regex SvgSpan();

    /// <summary>The drawing in an illustrator's answer: its ```svg block, or else the SVG in it; null when there's none.</summary>
    public static string? Drawing(string reply)
    {
        string text = Summarize.Thinking().Replace(reply ?? "", "").ReplaceLineEndings("\n");
        if (NoteBlocks.Find(text).FirstOrDefault(b => b.Kind == NoteBlockKind.Svg && b.Closed) is { } block && NoteBlocks.IsSvg(block.Text.Trim())) return block.Text.Trim();
        return SvgSpan().Match(text) is { Success: true } span ? span.Value : null;
    }

    static readonly HashSet<string> Common =
    [
        "with", "from", "that", "this", "then", "into", "onto", "over", "each", "when", "what", "where", "which", "their", "there",
        "they", "them", "more", "less", "than", "back", "also", "left", "right", "front", "rear", "side", "top", "bottom", "under",
    ];

    [GeneratedRegex(@"[^\p{L}\p{N}]+")]
    private static partial Regex NotWord();

    /// <summary>Why an illustration isn't one of this lecture: its words (callouts, part names and lines) mostly
    /// aren't the lecture's. Null when they are.</summary>
    static string? Unfaithful(string svg, string grounds)
    {
        var drawing = SafeSvg.Clean(svg);
        var words = drawing.Texts.Concat(drawing.Parts.SelectMany(p => new[] { p.Name, p.Note }));
        var distinct = words.SelectMany(w => NotWord().Split(w.ToLowerInvariant())).Where(w => w.Length >= 4 && !Common.Contains(w)).Distinct().ToList();
        if (distinct.Count < 3) return null;
        int found = distinct.Count(w => grounds.Contains(w[..Math.Max(4, w.Length - 2)], StringComparison.Ordinal));
        return found < distinct.Count * DiagramDesign.MinGrounded ? "its labels aren't what the lecture said" : null;
    }
}
