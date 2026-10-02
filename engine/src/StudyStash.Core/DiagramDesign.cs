using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using StudyStash.Core.Rich;

namespace StudyStash.Core;

/// <summary>A diagram the designer drew, as checked: its title, the heading of the section it goes at the end of,
/// the moment of the lecture it comes from (seconds, when the transcript has times), its kind and source, and the
/// sentence under it that says the same in words.</summary>
public sealed record DesignedDiagram(string Title, string After, double? At, NoteBlockKind Kind, string Source, string Caption);

/// <summary>The designer's reply, read strictly: its reason, the diagrams whose every field checked out (in the
/// order it gave them), and a word on each one that didn't. <see cref="Malformed"/>: nothing in it can be used.</summary>
public sealed record DesignReply(string Reason, IReadOnlyList<DesignedDiagram> Diagrams, IReadOnlyList<string> Dropped)
{
    public bool Malformed { get; init; }
    public static DesignReply Unusable(string why) => new("", [], [why]) { Malformed = true };

    /// <summary>The illustrations it planned (<see cref="IllustrationDesign"/>), each checked like a diagram.</summary>
    public IReadOnlyList<IllustrationPlan> Illustrations { get; init; } = [];
}

/// <summary>What a diagram pass did: the notes (the very string it was given when it added nothing), the diagrams
/// it added, the designer's reason, and why any it designed were left out. <see cref="Revised"/>: how many it
/// redesigned after seeing how they'd look; <see cref="Problems"/>, what still looks wrong with the ones drawn.</summary>
public sealed record DesignResult(string Notes, IReadOnlyList<DesignedDiagram> Drawn, string Reason, IReadOnlyList<string> Dropped)
{
    public bool Malformed { get; init; }
    public int Revised { get; init; }
    public IReadOnlyList<string> Problems { get; init; } = [];
}

/// <summary>
/// The diagram pass: after a lecture's notes are written, a stronger model reads its timed transcript and the notes,
/// decides whether the lecture teaches anything a picture makes clearer (a process, cycle, pathway, decision rule,
/// hierarchy, structure, states and transitions, an exchange between parties, a timeline, a topic's themes, a
/// comparison, or something spatial), and designs at most a few, each placed at the end of the section it
/// illustrates: a flowchart (a big topic in groups, its boxes carrying the lecture's specifics in smaller words), a
/// state or sequence diagram, a timeline or a mind map, or SVG. Its JSON reply is read strictly; a diagram that
/// doesn't parse goes back once for repair (<see cref="Summarize.RepairDiagramsAsync"/>) and is otherwise left out,
/// as is one with too many boxes or labels the lecture never said. Each one that's kept is laid out at the notes'
/// width and looked over (<see cref="DiagramLint"/>); one that would look wrong (too wide to read, crossing arrows,
/// sentences in boxes, a big chart with no groups) goes back once, with what's wrong, while there's time, and its
/// redesign is used only when it's better. Nothing here ever fails the notes: a pass that adds nothing hands back the
/// notes it was given, byte for byte. Diagrams it added carry a mark, so a later pass replaces them, never stacks.
/// </summary>
public static partial class DiagramDesign
{
    /// <summary>Below this (about three minutes of speech) a lecture is too short for a diagram to matter.</summary>
    public const int MinTranscriptChars = 3000;
    /// <summary>The most diagrams one lecture gets (a long one; shorter ones get fewer, see <see cref="Cap"/>).</summary>
    public const int MaxDiagrams = 4;
    /// <summary>The boxes the brief allows a focused diagram and a big one (in groups), and the most a diagram may
    /// come back with before it's left out.</summary>
    public const int MaxBoxes = 12, MaxGrouped = 36, MaxNodes = 40, MinNodes = 3;
    public const int MaxTitle = 80, MaxCaption = 420;
    /// <summary>How much of the pass's time must be left for a diagram that would look wrong to go back once.</summary>
    public static readonly TimeSpan RevisionRoom = TimeSpan.FromSeconds(150);
    /// <summary>How much of a diagram's wording must be found in the transcript or the notes.</summary>
    public const double MinGrounded = 0.6;
    /// <summary>How long the whole pass may take, repairs included, before the notes go on without it.</summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromMinutes(8);

    /// <summary>What marks a diagram this pass added: the first line inside its block.</summary>
    const string Mark = "Study Stash diagram";

    /// <summary>How many diagrams a lecture may get: none under <see cref="MinTranscriptChars"/>, one for a short
    /// lecture (under about ten minutes), two under about twenty, three under about forty, four for a long one.</summary>
    public static int Cap(string transcript)
    {
        int n = Py.Strip(TimedText.Plain(transcript ?? "")).Length;
        return n < MinTranscriptChars ? 0 : n < 10_000 ? 1 : n < 22_000 ? 2 : n < 40_000 ? 3 : MaxDiagrams;
    }

    // --- the examples the brief shows: each one parses, and lays out well at the notes' width ----------------------

    /// <summary>A big topic as a flowchart in groups: cellular respiration by where it happens, each step's yield in its
    /// smaller words, coloured by what the colour means. Its groups fold into an overview of their own.</summary>
    public static readonly string GroupedExample = """
        flowchart LR
          subgraph CYTO ["Cytoplasm"]
            GLU["Glucose"] --> GLY["Glycolysis<br><small>2 ATP, 2 NADH</small>"] --> PYR["2 pyruvate"]
          end
          subgraph MATRIX ["Mitochondrial matrix"]
            OX["Pyruvate oxidation<br><small>CO2 released</small>"] --> ACO["Acetyl-CoA"] --> KREBS["Krebs cycle<br><small>2 ATP, NADH, FADH2</small>"]
          end
          subgraph MEMBRANE ["Inner membrane"]
            ETC["Electron transport chain"]:::amber --> GRAD["H+ gradient"] --> SYN["ATP synthase<br><small>about 34 ATP</small>"]:::green
            O2(["O2 accepts electrons"]):::blue --> H2O["Water"]
          end
          PYR -->|"with oxygen"| OX
          KREBS -->|"NADH, FADH2"| ETC
          ETC --> O2
          PYR -.->|"no oxygen"| FER["Fermentation<br><small>lactate, 2 ATP total</small>"]:::red
        """.ReplaceLineEndings("\n");

    /// <summary>An automaton as a state diagram: binary strings ending in 01, its accepting state double.</summary>
    public static readonly string StateExample = """
        stateDiagram-v2
          direction LR
          [*] --> q0
          q0 --> q0 : 1
          q0 --> q1 : 0
          q1 --> q1 : 0
          q1 --> q2 : 1
          q2 --> q1 : 0
          q2 --> q0 : 1
          class q2 accept
        """.ReplaceLineEndings("\n");

    /// <summary>An exchange as a sequence diagram: logging in, with the two ways it can go.</summary>
    public static readonly string SequenceExample = """
        sequenceDiagram
          participant B as Browser
          participant S as Server
          participant D as Database
          B->>S: POST /login (email, password)
          S->>D: look up the user
          D-->>S: password hash
          alt hash matches
            S-->>B: 200 OK + session cookie
          else no match
            S-->>B: 401 Unauthorized
          end
          Note over B,S: later requests send the cookie
        """.ReplaceLineEndings("\n");

    /// <summary>Events in order as a timeline.</summary>
    public static readonly string TimelineExample = """
        timeline
          title Germ theory
          1847 : Semmelweis has doctors wash their hands
          1857 : Pasteur shows microbes cause fermentation
          1867 : Lister sterilises wounds with carbolic acid
          1882 : Koch identifies the TB bacterium
        """.ReplaceLineEndings("\n");

    /// <summary>A topic broken into its themes as a mind map.</summary>
    public static readonly string MindmapExample = """
        mindmap
          root((Tissue types))
            Epithelial
              Covers surfaces
              Squamous, cuboidal, columnar
            Connective
              Supports and binds
              Bone, blood, cartilage
            Muscle
              Contracts
              Skeletal, cardiac, smooth
            Nervous
              Signals
              Neurons and glia
        """.ReplaceLineEndings("\n");

    /// <summary>Every example the brief shows, for the checks that each one parses and lays out well.</summary>
    public static IReadOnlyList<string> Examples => [Summarize.MermaidExample, GroupedExample, StateExample, SequenceExample, TimelineExample, MindmapExample];

    // --- the brief -------------------------------------------------------------------------------------------------

    /// <summary>
    /// What the designer is asked: the lecture, its transcript (with times, when it has them) and its notes first,
    /// then when a diagram helps and when it doesn't, which kind of diagram fits which idea (each with an example that
    /// itself parses and lays out well), how to put the lecture's detail in and stay true to it, how big a diagram may
    /// be and how a big one is grouped, the style, the caption, a check to run before answering, the headings it may
    /// go under, and the JSON to answer with. A prompt longer than <paramref name="maxChars"/> (a local model's
    /// context) keeps the notes whole and cuts the transcript's end.
    /// </summary>
    public static string Prompt(Meeting m, string notes, Drawings drawings, int cap, int maxChars = int.MaxValue)
    {
        bool svg = drawings == Drawings.FlowchartsAndSvg;
        bool timed = TimedText.HasTimes(m.Transcript);
        string transcript = timed ? string.Join("\n", NoteBlocks.Lines(m.Transcript).Select(l => l.Trim()).Where(l => l.Length > 0)) : Py.Strip(m.Transcript);
        if (Speakers.HasLabels(transcript)) transcript = Speakers.NotesHint + "\n\n" + transcript;
        var heads = Headings(NoteBlocks.Lines(notes));
        string headings = heads.Count == 0
            ? "The notes have no headings: leave \"after\" empty and the diagram goes at their end."
            : "Headings in the notes, for \"after\":\n" + string.Join("\n", heads.Select(h => "- " + h.Text));
        string many = cap == 1 ? "one diagram" : $"{cap} diagrams";
        string kinds = svg ? "\"mermaid\" (every kind above), \"plot\" or \"svg\"" : "\"mermaid\" (every kind above) or \"plot\"";

        string Build(string t) => ($$"""
            You design the diagrams in a university student's study notes. Below are a lecture's transcript and the notes already written from it. Decide whether this lecture teaches anything a diagram would make clearer, and if it does, design the best study diagram you can: one a student could learn the topic from, and redraw from memory in an exam. A great diagram shows in a few seconds what took the lecturer minutes to say: the steps in order, what causes what, what is part of what, who sends what to whom, what changes when. A diagram that only repeats a list, or shows something the lecture didn't explain, is worse than none.

            <lecture>
            {{Summarize.Header(m)}}
            </lecture>

            <transcript>
            {{t}}
            </transcript>

            <notes>
            {{notes}}
            </notes>

            ## Decide

            Draw a diagram only for something the lecturer explained at some length, whose shape is the point:
            - a process, pathway or algorithm: steps in order, decisions, loops (the nursing process, a signalling cascade, how a scanner reads a token)
            - a cycle (the cardiac cycle, the Krebs cycle)
            - a structure whose parts connect (the layers of a network, the parts of a compiler, a data warehouse's tiers)
            - a hierarchy or classification (the types of tissue, a class hierarchy)
            - states and the events or inputs that move between them (an automaton, a process's states, a protocol)
            - an exchange between parties, in order (a client and a server, a handshake, cells signalling to each other)
            - a timeline the lecturer put in order
            - a topic broken into its themes, when the lecture's point is how they fit together
            - a comparison, when two or three things share a structure and part ways at clear points
            - a formula, function or distribution the lecturer gave whose shape is the point (an activation or loss function, a distribution and its parameters, growth rates, gradient descent, a matrix's effect): a plot, drawn exactly from the formula{{(svg ? "\n- something spatial (the forces on an object, a circuit, a labelled structure, the graph of a function, memory during a function call)" : "")}}

            Draw nothing for:
            - discussion, opinion, stories, a case told as a story, or a review of unrelated questions
            - course business: the syllabus, grading, deadlines, logistics
            - definitions, or facts with no order or links between them
            - a topic only mentioned in passing, whose steps or parts were never explained
            - a short list the notes already make clear (three boxes in a row add nothing)
            - anything the notes already show as a diagram or a plot
            - a formula whose shape the lecture never talked about (a plot is for when how it looks is the point)
            When in doubt, draw nothing. No diagram is a normal, correct answer. This lecture may have at most {{many}}, each of a different idea; draw fewer unless each one earns its place.

            ## Choose the kind

            Pick the kind whose shape matches the idea. Every one is Mermaid, written in a ```mermaid block's syntax.

            A flowchart (flowchart LR or TD) for processes, pathways, decisions, cycles, structures and hierarchies, and for a concept map: the ideas as boxes, joined by arrows labelled with how they relate (causes, is a, requires, produces). A focused idea, small; blood flow through the heart, a cycle coloured by what the colour means:
            ```mermaid
            {{Summarize.MermaidExample}}
            ```
            A big topic, in groups; cellular respiration by where each step happens, each step's yield in its smaller words:
            ```mermaid
            {{GroupedExample}}
            ```

            A state diagram (stateDiagram-v2) for states and what moves between them: each transition labelled with its event or input, [*] where it starts (and ends). An automaton names its states as the lecturer did (q0, q1…) and marks each accepting state with class name accept; binary strings ending in 01:
            ```mermaid
            {{StateExample}}
            ```

            A sequence diagram (sequenceDiagram) for who sends what to whom, in order: participants left to right in the order they first act, one message a line (->> a request or call, -->> a reply), alt/else, opt and loop blocks closed with end, a Note over two participants for what holds throughout; logging in:
            ```mermaid
            {{SequenceExample}}
            ```

            A timeline (timeline) for events the lecturer put in order: one period a line, then its events after colons, section lines to group eras:
            ```mermaid
            {{TimelineExample}}
            ```

            A mind map (mindmap) for a topic broken into its themes and their key points, by indentation: the root as root((Topic)), two or three levels below it:
            ```mermaid
            {{MindmapExample}}
            ```
            """ + (svg ? $$"""


            Something spatial is SVG instead: {{Summarize.SvgRules}} For example, the forces on a block on a slope:
            ```svg
            {{Summarize.SvgExample}}
            ```
            """ : "") + $$"""


            {{PlotDesign.Brief}}
            The examples show the form only: draw only what this lecture teaches.

            ## Put the lecture's detail in, and keep to it

            - Every box, arrow and label comes from what the lecturer said. Never add a step, part, name, number or example the lecture didn't give, even one you know is true. If the lecturer skipped a step, so does the diagram.
            - Put the lecturer's specifics in: the numbers, conditions, units, names and examples they gave belong on the boxes and arrows, not left vague. A box's name is one to five words; what it does, where, or how much goes in a smaller line under it: G["Glycolysis<br><small>cytoplasm, 2 ATP</small>"] (flowcharts; in a state diagram, a line q0 : what it means).
            - Label an arrow with what it carries, the condition that takes it, or the event that fires it, whenever that says more than "then": one to four words.
            - The transcript comes from speech recognition: fix obvious mis-hearings of technical terms (the notes usually have them right), and write in the language of the lecture.
            - {{(timed ? "\"at\" is the time of the transcript line where the lecturer starts explaining what the diagram shows, as written there (mm:ss, or h:mm:ss past the first hour)." : "This transcript has no times: leave \"at\" empty.")}}

            ## Size and shape

            - A focused idea: 4 to {{MaxBoxes}} boxes.
            - A big topic the lecturer built up over a long stretch (a whole system, a whole process with its phases): up to {{MaxGrouped}} boxes, in groups: 3 to 7 subgraphs of 3 to 7 boxes each, named for the phases, places or parts the lecturer named, one level of groups (two at most). Most arrows stay inside a group; a few arrows between groups carry the main thread, each from the box where one group's part ends to the box where the next one's starts (arrows join boxes, not groups). Read alone, the groups must make a diagram of their own: the app can fold each group into one box.
            - A flowchart runs LR for a sequence, pathway or cycle (a grouped LR chart shows its groups as columns, each read downwards) and TD for a hierarchy or a decision tree. A cycle ends with an arrow from its last step back to its first. A decision is a {"Question?"} box with |yes| and |no| (or the answers) on its arrows.
            - Shapes mean something: {"Question?"} a decision, [/"Input"/] an input or output, [("Store")] stored data, ([...]) where a process starts or ends, [["..."]] a sub-procedure, ((...)) a hub, (((...))) an accepting state.
            - Colour only when the colour means something the lecture said, the same meaning everywhere in the diagram, and say in the caption what each colour means: :::red, :::blue, :::green, :::amber, :::purple or :::accent (in a state diagram, class name red). Never colour to decorate. Quote every flowchart label: A["..."]. No style, classDef, linkStyle, click, and no HTML but <br> and <small>.
            - Fit the notes' column (about 620 px): no more than four or five boxes side by side, five participants in a sequence diagram, labels short enough not to wrap more than twice.

            ## Title and caption

            - A short title that names what the diagram shows.
            - A caption of one or two sentences (50 words at most) that teaches: what the diagram shows and what to notice in it (the turning point, the loop back, where two paths part, what each colour means), so the notes still teach it without the picture.
            - "after" is the heading of the section it illustrates, copied exactly from the list below: the most specific one that fits, never Announcements or Questions to review.

            {{headings}}
            {{(svg ? "\n" + IllustrationDesign.Brief(IllustrationDesign.Cap(m.Transcript)) : "")}}
            ## Check before you answer

            For each diagram: every label is something the lecture said (and a plot's formula is the lecturer's own); a student would see the main idea in five seconds; each box is one to five words with its detail in <small>; a big one is in groups that read as an overview; every colour means one thing, said in the caption; it is the kind that fits the idea; and its source is valid Mermaid (quoted labels, matched brackets, one statement a line, every block closed).

            ## Answer

            Answer with only this JSON object, nothing before or after it:
            {"reason": "...", "diagrams": [{"title": "...", "after": "...", "at": "{{(timed ? "mm:ss" : "")}}", "kind": "mermaid", "source": "flowchart LR\n  A[\"...\"] --> B[\"...\"]", "caption": "..."}]{{(svg ? ", \"illustrations\": []" : "")}}}
            - "reason": one sentence: what the diagrams show and why they help, or why this lecture needs none.
            - "diagrams": the most helpful first, at most {{many}}; an empty list [] when nothing is worth drawing.
            - "kind": {{kinds}}. "source": the diagram's code (or the plot's lines) alone, with no ``` fence, as a JSON string (\n between lines, \" for quotes).
            - Every diagram needs all six fields; one missing any of them is left out.{{(svg ? "\n- " + IllustrationDesign.AnswerField + "." : "")}}
            """).ReplaceLineEndings("\n");

        string whole = Build(transcript);
        if (whole.Length <= maxChars) return whole;
        int room = Math.Max(0, maxChars - Build("").Length - Cut.Length);
        return Build(Fit(transcript, room));
    }

    const string Cut = "\n[... the rest of the transcript didn't fit here; the notes cover it ...]";

    /// <summary>The transcript's start, at most <paramref name="room"/> characters, cut at a line's end.</summary>
    static string Fit(string transcript, int room)
    {
        if (transcript.Length <= room) return transcript;
        int end = transcript.LastIndexOf('\n', Math.Max(0, room - 1));
        return (end > 0 ? transcript[..end] : transcript[..room]) + Cut;
    }

    // --- reading the reply ----------------------------------------------------------------------------------------

    [GeneratedRegex(@"^\[?(?:(\d+):)?(\d{1,2}):(\d{2})\]?$")]
    private static partial Regex Clock();

    [GeneratedRegex(@"^(#{1,6})\s+(.+?)\s*#*\s*$")]
    private static partial Regex Heading();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();

    [GeneratedRegex(@"[^\p{L}\p{N}]+")]
    private static partial Regex NotWord();

    /// <summary>
    /// The designer's reply, read strictly: one JSON object with a non-empty <c>reason</c> and a <c>diagrams</c> list,
    /// or nothing is used. Each diagram needs a kind this engine may draw, a source, a title, a caption, a heading of
    /// these notes to go under (when they have headings) and, for a timed transcript, a moment inside the lecture or
    /// none; one that misses any of them is left out, with a word on why. Whether its source draws is checked later,
    /// where a broken one can be sent back once.
    /// </summary>
    public static DesignReply Read(string reply, string notes, string transcript, Drawings drawings)
    {
        string text = Py.Strip(Summarize.Thinking().Replace(reply ?? "", ""));
        JsonObject? o = null;
        try
        {
            if (Ai.AiJobs.FirstObject(text) is { } json) o = JsonNode.Parse(json) as JsonObject;
        }
        catch (JsonException)
        {
        }
        if (o is null) return DesignReply.Unusable("the reply wasn't a JSON object");
        if (Str(o["reason"]) is not { } reasonRaw || OneLine(reasonRaw) is not { Length: > 0 } reason) return DesignReply.Unusable("the reply gave no reason");
        if (o["diagrams"] is not JsonArray list) return DesignReply.Unusable("the reply had no list of diagrams");

        var heads = Headings(NoteBlocks.Lines(notes));
        double? end = TimedText.HasTimes(transcript) ? TimedText.Parse(transcript).LastOrDefault()?.End ?? 0 : null;
        var ok = new List<DesignedDiagram>();
        var dropped = new List<string>();
        for (int i = 0; i < list.Count; i++)
        {
            var (d, why) = Check(list[i] as JsonObject, heads, end, drawings);
            if (d is not null) ok.Add(d);
            else dropped.Add($"diagram {i + 1}: {why}");
        }
        if (drawings != Drawings.FlowchartsAndSvg) return new DesignReply(reason, ok, dropped);
        var (plans, unplanned) = IllustrationDesign.ReadPlans(o, heads.Select(h => h.Text).ToList(), end);
        return new DesignReply(reason, ok, [.. dropped, .. unplanned]) { Illustrations = plans };
    }

    static (DesignedDiagram? D, string Why) Check(JsonObject? o, List<(int Line, int Level, string Text)> heads, double? end, Drawings drawings)
    {
        if (o is null) return (null, "not an object");
        NoteBlockKind kind;
        switch (Str(o["kind"])?.Trim().ToLowerInvariant().Replace(" ", "").Replace("-", ""))
        {
            // Mermaid by name, or by any of the kinds it draws (what's in the source decides).
            case "mermaid" or "flowchart" or "statediagram" or "state" or "statediagramv2" or "sequencediagram" or "sequence" or "timeline" or "mindmap":
                kind = NoteBlockKind.Mermaid;
                break;
            case "svg" when drawings == Drawings.FlowchartsAndSvg:
                kind = NoteBlockKind.Svg;
                break;
            case "plot":
                kind = NoteBlockKind.Plot;
                break;
            // A name that could be either: what's in the source decides (graph TD is a flowchart).
            case "graph" or "function" or "chart":
                kind = NoteBlocks.StartsAsDiagram(Str(o["source"]) ?? "") ? NoteBlockKind.Mermaid : NoteBlockKind.Plot;
                break;
            case "svg":
                return (null, "an SVG drawing, which this engine doesn't draw");
            default:
                return (null, "no kind it can be drawn as");
        }
        string source = Unfenced(Str(o["source"]) ?? "", kind);
        if (source.Trim().Length == 0) return (null, "no source");
        if (kind == NoteBlockKind.Svg && !NoteBlocks.IsSvg(source)) return (null, "its source isn't an SVG drawing");
        if (Clean(Str(o["title"]) ?? "", MaxTitle) is not { Length: > 0 } title) return (null, "no title");
        if (Clean(Str(o["caption"]) ?? "", MaxCaption) is not { Length: > 0 } caption) return (null, $"“{title}” has no caption");
        string after = OneLine(Str(o["after"]) ?? "");
        if (heads.Count > 0 && !heads.Any(h => Norm(h.Text) == Norm(after)))
            return (null, $"“{title}” goes after “{after}”, which isn't a heading in the notes");
        double? at = null;
        if (end is double last && OneLine(Str(o["at"]) ?? "") is { Length: > 0 } said)
        {
            if (Seconds(said) is not double s) return (null, $"“{title}” comes from “{said}”, which isn't a time");
            if (s > last + 120) return (null, $"“{title}” comes from {said}, after the lecture ends");
            at = s;
        }
        return (new DesignedDiagram(title, after, at, kind, source, caption), "");
    }

    /// <summary>A source with any fence the model wrapped it in taken off.</summary>
    static string Unfenced(string source, NoteBlockKind kind)
    {
        string t = source.ReplaceLineEndings("\n");
        if (t.Contains("```", StringComparison.Ordinal) && NoteBlocks.Find(t).FirstOrDefault(b => b.Kind == kind && b.Closed) is { } block) t = block.Text;
        return string.Join("\n", t.Split('\n').Select(l => l.TrimEnd())).Trim('\n');
    }

    static string? Str(JsonNode? n) => n is JsonValue v && v.GetValueKind() == JsonValueKind.String ? v.GetValue<string>() : null;

    static string OneLine(string s) => Spaces().Replace(s, " ").Trim();

    /// <summary>A title or caption as it can sit in the notes: one line, no emphasis marks of its own, at most
    /// <paramref name="max"/> characters: its sentences that fit, else cut at a word with an ellipsis.</summary>
    static string Clean(string s, int max)
    {
        string t = OneLine(s.Replace("*", "").Replace("`", "")).TrimStart('#', ' ');
        if (t.Length <= max) return t;
        int end = t.LastIndexOfAny(['.', '!', '?'], max - 1);
        if (end >= max / 3) return t[..(end + 1)];
        int cut = t.LastIndexOf(' ', max - 1);
        return t[..(cut > max / 2 ? cut : max - 1)].TrimEnd(',', ';', ':', ' ') + "…";
    }

    static double? Seconds(string clock)
    {
        var m = Clock().Match(clock);
        if (!m.Success) return null;
        double h = m.Groups[1].Success ? double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) : 0;
        return h * 3600 + double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture) * 60 + double.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture);
    }

    /// <summary>A heading as it's compared: no #, emphasis or trailing colon, one space, any case.</summary>
    static string Norm(string heading) =>
        OneLine(heading.TrimStart('#').Replace("*", "").Replace("`", "").Replace("_", " ")).TrimEnd(':', ' ').ToLowerInvariant();

    /// <summary>The note's headings (never a line inside a code block or a diagram), with their line and level.</summary>
    static List<(int Line, int Level, string Text)> Headings(IReadOnlyList<string> lines)
    {
        var inBlock = new bool[lines.Count];
        foreach (var b in NoteBlocks.Find(lines))
            for (int k = b.First; k <= b.Last && k < lines.Count; k++) inBlock[k] = true;
        var heads = new List<(int, int, string)>();
        for (int k = 0; k < lines.Count; k++)
            if (!inBlock[k] && Heading().Match(lines[k]) is { Success: true } m)
                heads.Add((k, m.Groups[1].Value.Length, m.Groups[2].Value.Trim()));
        return heads;
    }

    // --- the pass ---------------------------------------------------------------------------------------------------

    /// <summary>
    /// One diagram pass over freshly written <paramref name="notes"/>: any diagrams an earlier pass added come out
    /// first, the designer is asked once (<paramref name="ask"/>, told the answer must be JSON), and what it designed
    /// is checked: a broken one sent back once (a plain answer, this time) or left out, one untrue to the lecture left
    /// out. Then each one that's kept is laid out as the notes will show it; while <paramref name="budget"/> leaves
    /// <see cref="RevisionRoom"/>, those that would look wrong go back together once, with what's wrong, and each
    /// redesign that draws, stays true and looks better takes its draft's place. A reply that can't be used, or a
    /// lecture too short, leaves <paramref name="notes"/> exactly as given. Only <paramref name="ask"/>'s own failures
    /// in the first design (and cancelling) throw; a revision that fails or runs late keeps the drafts. An illustration
    /// the designer plans is drawn with <paramref name="draw"/> (<paramref name="ask"/> when there's none), in
    /// <see cref="IllustrationDesign.Timeout"/> from the start, the caller told so through <paramref name="longer"/>.
    /// </summary>
    public static async Task<DesignResult> DesignAsync(Meeting m, string notes, Drawings drawings, Func<string, bool, Task<string>> ask,
        int maxPromptChars = int.MaxValue, TimeSpan? budget = null, Func<string, Task<string>>? draw = null, Action<TimeSpan>? longer = null)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        int cap = Cap(m.Transcript);
        if (cap == 0) return new DesignResult(notes, [], "the lecture is too short for a diagram", []);
        string clean = Strip(notes);
        var reply = Read(await ask(Prompt(m, clean, drawings, cap, maxPromptChars), true), clean, m.Transcript, drawings);
        if (reply.Malformed) return new DesignResult(notes, [], "", reply.Dropped) { Malformed = true };
        var drawn = new List<DesignedDiagram>();
        var dropped = reply.Dropped.ToList();
        string grounds = (TimedText.Plain(m.Transcript) + "\n" + clean).ToLowerInvariant();
        // Illustrations are drawn while the diagrams are checked, in a time of their own: a detailed drawing takes several
        // minutes, so a pass that draws one may run longer (the caller is told how long, all told).
        if (reply.Illustrations.Count > 0) longer?.Invoke(IllustrationDesign.Timeout);
        var illustrating = IllustrationDesign.DrawAllAsync(m, clean, reply.Illustrations, grounds, draw ?? (prompt => ask(prompt, false)),
            () => (budget is { } b && longer is null ? b : IllustrationDesign.Timeout) - clock.Elapsed, maxPromptChars);
        foreach (var d in reply.Diagrams)
        {
            if (drawn.Count >= cap)
            {
                dropped.Add($"“{d.Title}”: this lecture has room for {cap}");
                continue;
            }
            if (await Drawable(d, prompt => ask(prompt, false)) is not { } ready)
            {
                dropped.Add($"“{d.Title}”: {Summarize.DiagramProblem(d.Kind, d.Source)?.TrimEnd('.')}, even after a repair");
                continue;
            }
            if (Unfaithful(ready, grounds) is { } why)
            {
                dropped.Add($"“{d.Title}”: {why}");
                continue;
            }
            if (drawn.Any(x => x.Source == ready.Source)) continue;
            drawn.Add(ready);
        }

        // How each would look in the notes; those that would look wrong go back once, while there's time.
        var problems = drawn.Select(Looks).ToList();
        int revised = 0;
        var left = (budget ?? Timeout) - clock.Elapsed;
        var wrong = Enumerable.Range(0, drawn.Count).Where(i => problems[i].Count > 0).ToList();
        if (wrong.Count > 0 && left > RevisionRoom)
        {
            try
            {
                string answer = await ask(RevisePrompt(m, clean, wrong.Select(i => (drawn[i], problems[i])).ToList(), drawings), true)
                    .WaitAsync(left - TimeSpan.FromSeconds(20));
                var again = Read(answer, clean, m.Transcript, drawings);
                for (int k = 0; k < wrong.Count && k < again.Diagrams.Count; k++)
                {
                    int i = wrong[k];
                    var fresh = again.Diagrams[k] with { After = drawn[i].After, At = again.Diagrams[k].At ?? drawn[i].At };
                    if (fresh.Kind != drawn[i].Kind || Summarize.DiagramProblem(fresh.Kind, fresh.Source) is not null || Unfaithful(fresh, grounds) is not null) continue;
                    var looks = Looks(fresh);
                    if (looks.Count >= problems[i].Count) continue;
                    drawn[i] = fresh;
                    problems[i] = looks;
                    revised++;
                }
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                // A revision that fails or runs late changes nothing: the drafts stand.
            }
        }
        var still = drawn.Select((d, i) => (d, i)).Where(x => problems[x.i].Count > 0).Select(x => $"“{x.d.Title}”: {string.Join("; ", problems[x.i])}").ToList();
        var art = await illustrating;
        drawn.AddRange(art.Drawn);
        dropped.AddRange(art.Dropped);
        still.AddRange(art.Problems);
        return new DesignResult(drawn.Count == 0 ? clean : Insert(clean, drawn), drawn, reply.Reason, dropped) { Revised = revised + art.Revised, Problems = still };
    }

    /// <summary>What would look wrong with a diagram in the notes (<see cref="DiagramLint"/>); nothing for an SVG
    /// drawing, which draws as written.</summary>
    static IReadOnlyList<string> Looks(DesignedDiagram d)
    {
        if (d.Kind == NoteBlockKind.Plot)
        {
            try
            {
                return PlotLint.Problems(Plot.Parse(d.Source));
            }
            catch (PlotException)
            {
                return [];
            }
        }
        if (d.Kind != NoteBlockKind.Mermaid) return [];
        try
        {
            return DiagramLint.Problems(Flowchart.Parse(d.Source));
        }
        catch (MermaidException)
        {
            return [];
        }
    }

    /// <summary>
    /// The one revision: the diagrams that would look wrong, each with its title, caption and source and what was
    /// found wrong with it laid out at the notes' width, the notes to keep it true, the essentials of the brief, and
    /// the same JSON to answer with, a diagram for each in the same order.
    /// </summary>
    public static string RevisePrompt(Meeting m, string notes, IReadOnlyList<(DesignedDiagram Diagram, IReadOnlyList<string> Problems)> wrong, Drawings drawings)
    {
        var sb = new StringBuilder();
        sb.Append($"You designed {(wrong.Count == 1 ? "this diagram" : "these diagrams")} for a university student's study notes on a lecture. ")
            .Append("Study Stash laid each one out at the width of the notes (about 620 px) and found the problems listed under it. ")
            .Append("Redesign each one so every problem is fixed, keeping what was good about it and keeping it true to the lecture: every label must still be something the lecture said (the notes below are written from it). ")
            .Append("Keep the same kind of diagram unless another kind fixes the problem better.\n\n");
        sb.Append("<lecture>\n").Append(Summarize.Header(m)).Append("\n</lecture>\n\n<notes>\n").Append(notes).Append("\n</notes>\n");
        for (int i = 0; i < wrong.Count; i++)
        {
            var (d, problems) = wrong[i];
            sb.Append($"\n## Diagram {i + 1}: {d.Title}\n\n\"after\": \"{d.After}\", \"at\": \"{(d.At is double at ? TimedText.Clock(at) : "")}\"\n\n```{Summarize.Fence(d.Kind)}\n{d.Source}\n```\n\nCaption: {d.Caption}\n\nProblems:\n");
            foreach (string p in problems) sb.Append("- ").Append(p).Append('\n');
        }
        sb.Append($$"""

            ## How

            - A box is one to five words; its detail goes in a smaller line under it: A["Name<br><small>what, where, how much</small>"].
            - A big topic goes in 3 to 7 subgraphs of 3 to 7 boxes each ("subgraph ID ["Title"]" ... "end"), most arrows inside a group, a few between groups, each joining the box where one group's part ends to the box where the next starts; flowchart LR shows the groups as columns.
            - Narrow enough for the notes: no more than four or five boxes side by side, five participants in a sequence diagram.
            - Arrows that cross: put the boxes in the order the arrows run, and drop arrows that repeat what the order shows.
            - Keep its colours (:::red and the rest) and what they mean, said in the caption. Quote every flowchart label. No style, classDef, linkStyle or click.
            - Don't split a decision or an algorithm into groups just to have groups: groups are for a big topic's phases, places or parts.
            - A plot ("kind": "plot"): keep the lecturer's formula exactly; fix it with its ranges (x, y), its sliders' ranges and starting values, and its labels.{{(wrong.Any(w => w.Diagram.Kind == NoteBlockKind.Plot) ? "\n\n" + PlotDesign.Reference : "")}}

            ## Answer

            Answer with only this JSON object, nothing before or after it, with one diagram for each above, in the same order:
            {"reason": "what you changed", "diagrams": [{"title": "...", "after": "", "at": "", "kind": "mermaid or plot, as it was", "source": "...", "caption": "..."}]}
            - "source": the diagram's code alone, with no ``` fence, as a JSON string (\n between lines, \" for quotes). "caption": one or two sentences, 50 words at most, saying what it shows and what to notice.
            - "after" and "at": each diagram's own, copied exactly as given above it.
            """);
        return sb.ToString().ReplaceLineEndings("\n");
    }

    /// <summary>The diagram as it can be drawn: as designed, or as its one repair came back; null when neither draws.</summary>
    static async Task<DesignedDiagram?> Drawable(DesignedDiagram d, Func<string, Task<string>> ask)
    {
        if (Summarize.DiagramProblem(d.Kind, d.Source) is null) return d;
        string fence = Summarize.Fence(d.Kind);
        string repaired = await Summarize.RepairDiagramsAsync($"```{fence}\n{d.Source}\n```", ask);
        var block = NoteBlocks.Find(repaired).FirstOrDefault(b => b.Kind == d.Kind && b.Closed);
        return block is not null && Summarize.DiagramProblem(d.Kind, block.Text) is null ? d with { Source = block.Text } : null;
    }

    static readonly HashSet<string> Common =
    [
        "with", "from", "that", "this", "then", "into", "onto", "over", "each", "when", "what", "where", "which", "their", "there",
        "they", "them", "more", "less", "than", "back", "also", "step", "steps", "start", "until", "after", "before", "next",
    ];

    /// <summary>Why a drawable diagram still isn't one for these notes: too few or too many boxes, boxes that don't
    /// join up, or wording the transcript and notes don't have (a diagram about something the lecture never said).
    /// Null when it's fine.</summary>
    static string? Unfaithful(DesignedDiagram d, string grounds)
    {
        IReadOnlyList<string> words;
        if (d.Kind == NoteBlockKind.Plot)
        {
            // A plot's formula is drawn exactly: what's checked is that it's about what the lecture said (its title and
            // labels), and that it isn't a wall of curves.
            var plot = Plot.Parse(d.Source);
            if (plot.Items.Count(PlotLayout.Legendable) > PlotLint.MaxSeries + 2) return "too many curves to be one plot";
            words = [d.Title, .. PlotDesign.Labels(plot)];
        }
        else if (d.Kind == NoteBlockKind.Mermaid)
        {
            var chart = Flowchart.Parse(d.Source);
            int boxes = chart.Nodes.Count(n => n.Role is not (NodeRole.Start or NodeRole.End or NodeRole.Note or NodeRole.Bar));
            if (boxes > MaxNodes) return $"{boxes} boxes, more than a diagram in the notes should have";
            if (boxes < MinNodes && chart.Form != ChartForm.Sequence) return "too few boxes to be worth a diagram";
            if (chart.Form == ChartForm.Sequence && chart.Edges.Count < 2) return "too few messages to be worth a diagram";
            // A flowchart or state diagram is one idea (two, side by side, for a comparison); a sequence diagram,
            // a timeline and a mind map hold together by their kind.
            if (chart.Form is ChartForm.Flowchart or ChartForm.State && Pieces(chart) > Math.Max(2, chart.Groups.Count(g => g.Parent is null))) return "its boxes don't join up into one picture";
            words = chart.Labels();
        }
        else words = SafeSvg.Clean(d.Source).Texts;
        var distinct = words.SelectMany(w => NotWord().Split(w.ToLowerInvariant())).Where(w => w.Length >= 4 && !Common.Contains(w)).Distinct().ToList();
        if (distinct.Count < 3) return null;
        int found = distinct.Count(w => grounds.Contains(w[..Math.Max(4, w.Length - 2)], StringComparison.Ordinal));
        return found < distinct.Count * MinGrounded ? "its labels aren't what the lecture said" : null;
    }

    /// <summary>How many separate pieces a flowchart's boxes fall into, counting any arrow as joining two boxes: one
    /// idea is one piece (two, for things compared side by side); a scatter of pairs is no diagram at all.</summary>
    static int Pieces(Flowchart chart)
    {
        var root = chart.Nodes.Where(n => n.Role != NodeRole.Note).ToDictionary(n => n.Id, n => n.Id);
        string Find(string id)
        {
            while (root[id] != id) id = root[id] = root[root[id]];
            return id;
        }
        foreach (var e in chart.Edges)
            if (root.ContainsKey(e.From) && root.ContainsKey(e.To)) root[Find(e.From)] = Find(e.To);
        return root.Keys.Select(Find).Distinct().Count();
    }

    // --- in and out of the notes -----------------------------------------------------------------------------------

    /// <summary>
    /// The notes with each diagram at the end of the section its heading starts (before the next heading of the same
    /// or a higher level; at the end of the notes when they have no such heading): a blank line, its title in bold, the
    /// marked block, and its caption in italics with the moment it comes from. Diagrams for the same section keep their
    /// order. Nothing to add: the notes, untouched.
    /// </summary>
    public static string Insert(string notes, IReadOnlyList<DesignedDiagram> diagrams)
    {
        if (diagrams.Count == 0) return notes;
        var lines = NoteBlocks.Lines(notes).ToList();
        var heads = Headings(lines);
        var at = new SortedDictionary<int, List<string>>();
        foreach (var d in diagrams)
        {
            int anchor = Anchor(lines, heads, d.After);
            if (!at.TryGetValue(anchor, out var unit)) at[anchor] = unit = [];
            unit.AddRange(Unit(d));
        }
        foreach (var (anchor, unit) in at.Reverse()) lines.InsertRange(anchor + 1, unit);
        return string.Join("\n", lines);
    }

    /// <summary>The last line with anything on it in the section <paramref name="after"/> heads (or in the notes).</summary>
    static int Anchor(List<string> lines, List<(int Line, int Level, string Text)> heads, string after)
    {
        int from = 0, to = lines.Count;
        int h = heads.FindIndex(x => Norm(x.Text) == Norm(after));
        if (h >= 0)
        {
            from = heads[h].Line;
            var next = heads.Skip(h + 1).FirstOrDefault(x => x.Level <= heads[h].Level);
            if (next.Text is not null) to = next.Line;
        }
        int k = to - 1;
        while (k > from && lines[k].Trim().Length == 0) k--;
        return lines.Count == 0 ? -1 : k;
    }

    static IEnumerable<string> Unit(DesignedDiagram d)
    {
        string from = d.At is double s ? ", from " + TimedText.Clock(s) : "";
        bool svg = d.Kind == NoteBlockKind.Svg;
        yield return "";
        yield return $"**{d.Title}**";
        yield return "";
        yield return "```" + Summarize.Fence(d.Kind);
        yield return svg ? $"<!-- {Mark}{from} -->" : $"%% {Mark}{from}";
        foreach (string line in d.Source.Split('\n')) yield return line;
        yield return "```";
        yield return "";
        yield return $"*{d.Caption}*" + (d.At is double t ? $" (from {TimedText.Clock(t)})" : "");
    }

    static bool Marked(string blockText)
    {
        string first = blockText.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0) ?? "";
        return first.StartsWith("%% " + Mark, StringComparison.Ordinal) || first.StartsWith("<!-- " + Mark, StringComparison.Ordinal);
    }

    /// <summary>
    /// The notes without the diagrams a diagram pass added (each with its title, caption and the blank line before
    /// it), so rewriting a lecture's diagrams replaces them instead of stacking more. Notes with none: untouched.
    /// </summary>
    public static string Strip(string notes)
    {
        var lines = NoteBlocks.Lines(notes).ToList();
        var ours = NoteBlocks.Find(lines).Where(b => b.IsDiagram && b.Closed && Marked(b.Text)).ToList();
        if (ours.Count == 0) return notes;
        static bool Blank(string l) => l.Trim().Length == 0;
        static bool Title(string l) => l.Trim() is { Length: > 4 } t && t.StartsWith("**", StringComparison.Ordinal) && t.EndsWith("**", StringComparison.Ordinal);
        static bool Caption(string l) => l.Trim() is { Length: > 2 } t && t[0] == '*' && t[1] != '*';
        foreach (var b in ours.OrderByDescending(b => b.First))
        {
            int start = b.First, end = b.Last;
            if (start >= 2 && Blank(lines[start - 1]) && Title(lines[start - 2])) start -= 2;
            if (start >= 1 && Blank(lines[start - 1])) start--;
            if (end + 2 < lines.Count && Blank(lines[end + 1]) && Caption(lines[end + 2])) end += 2;
            lines.RemoveRange(start, end - start + 1);
        }
        return string.Join("\n", lines);
    }
}
