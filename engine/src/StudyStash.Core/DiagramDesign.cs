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
}

/// <summary>What a diagram pass did: the notes (the very string it was given when it added nothing), the diagrams
/// it added, the designer's reason, and why any it designed were left out.</summary>
public sealed record DesignResult(string Notes, IReadOnlyList<DesignedDiagram> Drawn, string Reason, IReadOnlyList<string> Dropped)
{
    public bool Malformed { get; init; }
}

/// <summary>
/// The diagram pass: after a lecture's notes are written, a stronger model reads its timed transcript and the notes,
/// decides whether the lecture teaches anything a picture makes clearer (a process, cycle, pathway, decision rule,
/// hierarchy, structure, timeline, comparison, or something spatial), and designs at most a few, each placed at the
/// end of the section it illustrates. Its JSON reply is read strictly; a diagram that doesn't parse goes back once
/// for repair (<see cref="Summarize.RepairDiagramsAsync"/>) and is otherwise left out, as is one with too many boxes
/// or labels the lecture never said. Nothing here ever fails the notes: a pass that adds nothing hands back the
/// notes it was given, byte for byte. Diagrams it added carry a mark, so a later pass replaces them, never stacks.
/// </summary>
public static partial class DiagramDesign
{
    /// <summary>Below this (about three minutes of speech) a lecture is too short for a diagram to matter.</summary>
    public const int MinTranscriptChars = 3000;
    /// <summary>The most diagrams one lecture gets (a long one; shorter ones get fewer, see <see cref="Cap"/>).</summary>
    public const int MaxDiagrams = 3;
    /// <summary>The boxes the brief allows, and the most a flowchart may come back with before it's left out.</summary>
    public const int MaxBoxes = 12, MaxNodes = 14, MinNodes = 3;
    public const int MaxTitle = 80, MaxCaption = 300;
    /// <summary>How much of a diagram's wording must be found in the transcript or the notes.</summary>
    public const double MinGrounded = 0.6;
    /// <summary>How long the whole pass may take, repairs included, before the notes go on without it.</summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromMinutes(8);

    /// <summary>What marks a diagram this pass added: the first line inside its block.</summary>
    const string Mark = "Study Stash diagram";

    /// <summary>How many diagrams a lecture may get: none under <see cref="MinTranscriptChars"/>, one for a short
    /// lecture (under about 13 minutes), two under about half an hour, three for a full one.</summary>
    public static int Cap(string transcript)
    {
        int n = Py.Strip(TimedText.Plain(transcript ?? "")).Length;
        return n < MinTranscriptChars ? 0 : n < 12_000 ? 1 : n < 30_000 ? 2 : MaxDiagrams;
    }

    // --- the brief -------------------------------------------------------------------------------------------------

    /// <summary>
    /// What the designer is asked: the lecture, its transcript (with times, when it has them) and its notes first,
    /// then when a diagram helps and when it doesn't, how to stay true to the lecture, how to lay one out, the form
    /// (with the notes' own examples), the headings it may go under, and the JSON to answer with. A prompt longer than
    /// <paramref name="maxChars"/> (a local model's context) keeps the notes whole and cuts the transcript's end.
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
        string kinds = svg ? "\"mermaid\" or \"svg\"" : "\"mermaid\"";

        string Build(string t) => ($$"""
            You design the diagrams in a university student's study notes. Below are a lecture's transcript and the notes already written from it. Decide whether this lecture teaches anything a diagram would make clearer, and if it does, design that diagram well. A good diagram lets a student see in a few seconds what took the lecturer minutes to say. A diagram that only repeats a list, or shows something the lecture didn't explain, is worse than none.

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
            - a process or sequence of steps (the nursing process, how a bill becomes law, the phases of mitosis)
            - a cycle (the cardiac cycle, the Krebs cycle, the water cycle)
            - a pathway or flow (blood through the heart, a signalling cascade, a drug from dose to effect)
            - a decision rule or algorithm (when to escalate care, a diagnostic rule, what a loop does on each pass)
            - a hierarchy or classification (the types of tissue, a taxonomy, a class hierarchy)
            - a structure whose parts connect (the parts of a neuron, the layers of a network, a linked list)
            - a timeline the lecturer put in order
            - a comparison, when two or three things share a structure and part ways at clear points (aerobic and anaerobic respiration after glycolysis){{(svg ? "\n- something spatial (the forces on an object, a circuit, a labelled structure, the graph of a function, memory during a function call)" : "")}}

            Draw nothing for:
            - discussion, opinion, stories, a case told as a story, or a review of unrelated questions
            - course business: the syllabus, grading, deadlines, logistics
            - definitions, or facts with no order or links between them
            - a topic only mentioned in passing, whose steps or parts were never explained
            - a short list the notes already make clear (three boxes in a row add nothing)
            - anything the notes already show as a diagram
            When in doubt, draw nothing. No diagram is a normal, correct answer, and most lectures need one at most. This lecture may have at most {{many}}.

            ## Stay true to the lecture

            - Every box, arrow and label comes from what the lecturer said. Never add a step, part, name, number or example the lecture didn't give, even one you know is true. If the lecturer skipped a step, so does the diagram.
            - The transcript comes from speech recognition: fix obvious mis-hearings of technical terms (the notes usually have them right), and write in the language of the lecture.
            - {{(timed ? "\"at\" is the time of the transcript line where the lecturer starts explaining what the diagram shows, as written there (mm:ss, or h:mm:ss past the first hour)." : "This transcript has no times: leave \"at\" empty.")}}

            ## Design it

            - One idea per diagram, drawn as a Mermaid flowchart{{(svg ? ", or as SVG when the idea is spatial" : "")}}.
            - At most {{MaxBoxes}} boxes; four to nine usually reads best. For a bigger topic, draw its main path and leave the detail to the notes.
            - Box labels: one to five words, in quotes, in the lecturer's terms. Arrow labels: one to three words, only where the arrow means more than "then" (a valve, a condition, an enzyme, yes or no).
            - Direction: LR for a sequence, pathway or cycle; TD for a hierarchy, a decision or a timeline. A cycle ends with an arrow from its last step back to its first. A decision is a {"Question?"} box with |yes| and |no| on its arrows.
            - Group boxes with subgraph "Title" ... end only where the lecturer grouped them (phases, organs, layers), at most two levels deep.
            - Colour a box only when its colour means something the lecture said (oxygen-rich red and oxygen-poor blue; stimulating green and inhibiting red), with :::red, :::blue, :::green, :::amber, :::purple or :::accent. Never colour to decorate. No style, classDef, linkStyle, click or HTML.
            - Give it a short title that names what it shows, and a caption: one short sentence (25 words at most) that says in words what the diagram shows, so the notes still teach it without the picture.
            - "after" is the heading of the section it illustrates, copied exactly from the list below: the most specific one that fits, never Announcements or Questions to review.

            For example, blood flow through the heart as one lecture explained it, a cycle coloured by what the colour means:
            ```mermaid
            {{Summarize.MermaidExample}}
            ```
            """ + (svg ? $$"""


            Something spatial is SVG instead: {{Summarize.SvgRules}} For example, the forces on a block on a slope:
            ```svg
            {{Summarize.SvgExample}}
            ```
            """ : "") + $$"""


            The examples show the form only: draw only what this lecture teaches.

            {{headings}}

            ## Answer

            Answer with only this JSON object, nothing before or after it:
            {"reason": "...", "diagrams": [{"title": "...", "after": "...", "at": "{{(timed ? "mm:ss" : "")}}", "kind": "mermaid", "source": "flowchart LR\n  A[\"...\"] --> B[\"...\"]", "caption": "..."}]}
            - "reason": one sentence: what the diagrams show and why they help, or why this lecture needs none.
            - "diagrams": the most helpful first, at most {{many}}; an empty list [] when nothing is worth drawing.
            - "kind": {{kinds}}. "source": the diagram's code alone, with no ``` fence, as a JSON string (\n between lines, \" for quotes).
            - Every diagram needs all six fields; one missing any of them is left out.
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
        return new DesignReply(reason, ok, dropped);
    }

    static (DesignedDiagram? D, string Why) Check(JsonObject? o, List<(int Line, int Level, string Text)> heads, double? end, Drawings drawings)
    {
        if (o is null) return (null, "not an object");
        NoteBlockKind kind;
        switch (Str(o["kind"])?.Trim().ToLowerInvariant())
        {
            case "mermaid" or "flowchart":
                kind = NoteBlockKind.Mermaid;
                break;
            case "svg" when drawings == Drawings.FlowchartsAndSvg:
                kind = NoteBlockKind.Svg;
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
    /// goes in, each checked, a broken one sent back once (a plain answer, this time) or left out. A reply that can't
    /// be used, or a lecture too short, leaves <paramref name="notes"/> exactly as given. Only
    /// <paramref name="ask"/>'s own failures (and cancelling) throw.
    /// </summary>
    public static async Task<DesignResult> DesignAsync(Meeting m, string notes, Drawings drawings, Func<string, bool, Task<string>> ask,
        int maxPromptChars = int.MaxValue)
    {
        int cap = Cap(m.Transcript);
        if (cap == 0) return new DesignResult(notes, [], "the lecture is too short for a diagram", []);
        string clean = Strip(notes);
        var reply = Read(await ask(Prompt(m, clean, drawings, cap, maxPromptChars), true), clean, m.Transcript, drawings);
        if (reply.Malformed) return new DesignResult(notes, [], "", reply.Dropped) { Malformed = true };
        var drawn = new List<DesignedDiagram>();
        var dropped = reply.Dropped.ToList();
        string grounds = (TimedText.Plain(m.Transcript) + "\n" + clean).ToLowerInvariant();
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
        return new DesignResult(drawn.Count == 0 ? clean : Insert(clean, drawn), drawn, reply.Reason, dropped);
    }

    /// <summary>The diagram as it can be drawn: as designed, or as its one repair came back; null when neither draws.</summary>
    static async Task<DesignedDiagram?> Drawable(DesignedDiagram d, Func<string, Task<string>> ask)
    {
        if (Summarize.DiagramProblem(d.Kind, d.Source) is null) return d;
        string fence = d.Kind == NoteBlockKind.Svg ? "svg" : "mermaid";
        string repaired = await Summarize.RepairDiagramsAsync($"```{fence}\n{d.Source}\n```", ask);
        var block = NoteBlocks.Find(repaired).FirstOrDefault(b => b.Kind == d.Kind && b.Closed);
        return block is not null && Summarize.DiagramProblem(d.Kind, block.Text) is null ? d with { Source = block.Text } : null;
    }

    static readonly HashSet<string> Common =
    [
        "with", "from", "that", "this", "then", "into", "onto", "over", "each", "when", "what", "where", "which", "their", "there",
        "they", "them", "more", "less", "than", "back", "also", "step", "steps", "start", "until", "after", "before", "next",
    ];

    /// <summary>Why a drawable diagram still isn't one for these notes: too few or too many boxes, or wording the
    /// transcript and notes don't have (a diagram about something the lecture never said). Null when it's fine.</summary>
    static string? Unfaithful(DesignedDiagram d, string grounds)
    {
        IReadOnlyList<string> words;
        if (d.Kind == NoteBlockKind.Mermaid)
        {
            var chart = Flowchart.Parse(d.Source);
            if (chart.Nodes.Count > MaxNodes) return $"{chart.Nodes.Count} boxes, more than a diagram in the notes should have";
            if (chart.Nodes.Count < MinNodes) return "too few boxes to be worth a diagram";
            if (Pieces(chart) > 2) return "its boxes don't join up into one picture";
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
        var root = chart.Nodes.ToDictionary(n => n.Id, n => n.Id);
        string Find(string id)
        {
            while (root[id] != id) id = root[id] = root[root[id]];
            return id;
        }
        foreach (var e in chart.Edges)
            if (root.ContainsKey(e.From) && root.ContainsKey(e.To)) root[Find(e.From)] = Find(e.To);
        return chart.Nodes.Select(n => Find(n.Id)).Distinct().Count();
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
        yield return svg ? "```svg" : "```mermaid";
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
