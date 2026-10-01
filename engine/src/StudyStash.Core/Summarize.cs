using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using StudyStash.Core.Rich;

namespace StudyStash.Core;

/// <summary>(cfg, model, prompt, num_ctx) → the model's answer.</summary>
public delegate Task<string> ChatFn(Config cfg, string model, string prompt, int numCtx);

/// <summary>(cfg, model) → the model's own context length, or null when Ollama doesn't say.</summary>
public delegate Task<int?> ShowFn(Config cfg, string model);

/// <summary>The model didn't finish properly: it ran to the length cap, or repeated itself.</summary>
public sealed class RunawayOutputException(string message) : Exception(message);

/// <summary>What the notes may draw: Mermaid flowcharts only (every engine, and all a small local model does
/// well), or SVG drawings too, for what's spatial (the CLI engines, which write SVG that holds together), or
/// nothing at all: <see cref="None"/> is for notes whose diagrams a separate pass designs (<see cref="DiagramDesign"/>),
/// or whose student turned diagrams off.</summary>
public enum Drawings { Flowcharts, FlowchartsAndSvg, None }

/// <summary>
/// Write study notes from a lecture's transcript with the model you pick (a local Ollama model here). Transcripts
/// longer than the model's context are summarized in parts, then merged.
/// </summary>
public static partial class Summarize
{
    public const double CharsPerToken = 3.5; // rough, for English speech
    // Below this (a couple of minutes of speech) there's too little to fill the notes: small models then pad and
    // loop. Such lectures keep their transcript without study notes.
    public const int MinTranscriptChars = 1500;
    // Good notes are well under 2,000 tokens. Without a cap, a small model that starts repeating itself never
    // stops: Ollama keeps shifting its context and generating (39,000 tokens seen, from llama3.2:3b).
    public const int MaxNotesTokens = 4096;

    // .ReplaceLineEndings: on Windows, git checks this file out with "\r\n", which would change the prompts.
    public static readonly string Structure = """
        Use exactly this structure, and skip any section the lecture has nothing for:

        ## Summary
        Two to four sentences: what the lecture covered and how it fits the course.

        ## Key points
        Bullets: the ideas to remember, each in one or two sentences, the way the lecturer explained them.

        ## Definitions
        Bullets: the bold term, a colon, then what it means in one sentence.

        ## Details and examples
        Worked examples, derivations, formulas and calculations, code, and demonstrations, in the order they were taught.

        ## Announcements
        Deadlines, exams, assignments, and readings, with dates exactly as said.

        ## Questions to review
        Three to five numbered questions a student should be able to answer after this lecture.
        """.ReplaceLineEndings("\n");

    public static readonly string Rules = """
        Rules:
        - Use only what is in the transcript. Never invent facts, dates, or examples.
        - The transcript comes from speech recognition: fix obvious mis-hearings of technical terms, and skip filler, small talk, and audio problems.
        - Write every formula in LaTeX ($...$ or $$...$$).
        - Keep any formula and any ```mermaid or ```svg block you are given exactly as it is.
        - Write in the language of the lecture. Output Markdown only, with no preamble.
        """.ReplaceLineEndings("\n");

    /// <summary>The dose calculation the notes are shown, as the form a worked calculation takes.</summary>
    public const string DoseExample =
        @"$$\text{Volume} = \frac{\text{Desired}}{\text{Have}} \times \text{Quantity} = \frac{500\ \text{mg}}{250\ \text{mg}} \times 5\ \text{mL} = 10\ \text{mL}$$";

    /// <summary>The flowchart the notes are shown: blood flow through the heart, a cycle, coloured by what the colour
    /// means (oxygen-poor blue, oxygen-rich red), its valves on the arrows. It parses, and draws as a ring.</summary>
    public static readonly string MermaidExample = """
        flowchart LR
        RA["Right atrium"]:::blue -->|tricuspid valve| RV["Right ventricle"]:::blue -->|pulmonary valve| Lungs(("Lungs"))
        Lungs --> LA["Left atrium"]:::red -->|mitral valve| LV["Left ventricle"]:::red -->|aortic valve| Body(("Body"))
        Body --> RA
        """.ReplaceLineEndings("\n");

    /// <summary>The drawing the CLI engines are shown: the forces on a block on a slope, in the palette, every part
    /// labelled, its arrows ending in markers. It passes <see cref="SafeSvg"/> untouched but for its colours.</summary>
    public static readonly string SvgExample = """
        <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 640 280" font-size="14" fill="#1D1D1F">
        <title>Forces on a block on a slope</title>
        <defs>
        <marker id="red" viewBox="0 0 10 10" refX="9" refY="5" markerWidth="4" markerHeight="4" orient="auto"><path d="M0 0L10 5L0 10z" fill="#D93025"/></marker>
        <marker id="green" viewBox="0 0 10 10" refX="9" refY="5" markerWidth="4" markerHeight="4" orient="auto"><path d="M0 0L10 5L0 10z" fill="#188038"/></marker>
        <marker id="amber" viewBox="0 0 10 10" refX="9" refY="5" markerWidth="4" markerHeight="4" orient="auto"><path d="M0 0L10 5L0 10z" fill="#E37400"/></marker>
        </defs>
        <polygon points="60,260 580,260 580,40" fill="none" stroke="#1D1D1F" stroke-width="2"/>
        <path d="M124 260A64 64 0 0 0 119 235" fill="none" stroke="#6E6E73" stroke-width="1.5"/><text x="134" y="252" fill="#6E6E73">θ</text>
        <rect x="-48" y="-60" width="96" height="60" rx="4" transform="translate(294 161) rotate(-22.9)" fill="#E8F0FE" stroke="#1A73E8" stroke-width="2"/>
        <line x1="282" y1="133" x2="282" y2="233" stroke="#D93025" stroke-width="2.5" marker-end="url(#red)"/><text x="292" y="228" fill="#D93025">weight mg</text>
        <line x1="282" y1="133" x2="243" y2="41" stroke="#188038" stroke-width="2.5" marker-end="url(#green)"/><text x="234" y="32" text-anchor="end" fill="#188038">normal force N</text>
        <line x1="282" y1="133" x2="374" y2="94" stroke="#E37400" stroke-width="2.5" marker-end="url(#amber)"/><text x="384" y="92" fill="#E37400">friction f</text>
        </svg>
        """.ReplaceLineEndings("\n");

    /// <summary>How every formula is written, up to the worked dose that shows the form.</summary>
    const string FormulasBrief = @"Formulas: write every formula, equation, unit conversion and calculation in LaTeX: $...$ inside a sentence, and $$...$$ on a line of its own for one worth seeing alone. Use \frac, \times, \cdot, ^ and _, \text{...} for words and units, and \mathrm{H_2O} for chemistry. Work calculations step by step, for example a dose:";

    /// <summary>What an SVG drawing may be: its size, its words, its arrows and the palette the app recolours. The
    /// notes' own brief and the diagram designer's (<see cref="DiagramDesign"/>) both say it.</summary>
    public static readonly string SvgRules = """viewBox="0 0 640 H" with H at most 480, no width or height, text 13 to 15 units and at least 16 from the edges, every part labelled, arrows with a <marker>. Colours only from: #1D1D1F (lines and text), #6E6E73 (secondary text), #C7C7CC (light lines), #FFFFFF (paper), and the stroke/fill pairs red #D93025/#FCE8E6, blue #1A73E8/#E8F0FE, green #188038/#E6F4EA, amber #E37400/#FEF7E0, purple #8E24AA/#F3E8FD; the app recolours them for dark mode. No scripts, images, links, fonts, CSS or foreignObject.""";

    /// <summary>
    /// What the notes are told about formulas and diagrams: LaTeX for every formula, with a worked dose as the form;
    /// a diagram only where a picture makes the lecture clearer, said again in words, drawn as a Mermaid flowchart
    /// (and, for <see cref="Drawings.FlowchartsAndSvg"/>, as SVG for what's spatial), each with its example. With
    /// <see cref="Drawings.None"/>, no diagram at all: the process is told in words instead.
    /// </summary>
    public static string Drawing(Drawings drawings)
    {
        if (drawings == Drawings.None)
            return (FormulasBrief + "\n" + DoseExample + "\n\n"
                + "Diagrams: draw none: no Mermaid, no SVG and no pictures made of text. Diagrams are designed separately from the transcript and added to these notes afterwards, "
                + "so explain every process, cycle, pathway and structure in words, step by step and in the lecturer's order.\n\n"
                + "The example shows the form only: write only what this lecture teaches.").ReplaceLineEndings("\n");
        bool svg = drawings == Drawings.FlowchartsAndSvg;
        string what = svg ? "a process, cycle, pathway, decision rule, hierarchy or structure" : "a process, cycle, pathway, decision rule or hierarchy";
        string examples = "the cardiac cycle, blood flow through the heart, the nursing process, a care or assessment pathway, "
            + "how a drug moves from dose to effect, the cell cycle, " + (svg ? "a call stack, a binary tree, the forces on a block" : "a binary tree");
        string text = $$"""
            {{FormulasBrief}}
            {{DoseExample}}

            Diagrams: when the lecture explains {{what}} that a picture makes clearer ({{examples}}), draw it: at most {{(svg ? "three" : "two")}}, and none for a lecture of facts or discussion. Put each diagram on its own, right after the text it illustrates, never inside a bullet, and follow it with one sentence that says the same in words.
            Draw steps, cycles, pathways, decisions and hierarchies as a Mermaid flowchart:
            ```mermaid
            {{MermaidExample}}
            ```
            A few words per box, in quotes. LR for a sequence, TD for a hierarchy or a decision. A cycle ends with an arrow from its last step back to its first. A decision is a {"Question?"} box with |yes| and |no| on its arrows. Group boxes with subgraph "Title" ... end. Colour a box only when colour means something: :::red, :::blue, :::green, :::amber, :::purple or :::accent (above: oxygen-rich red, oxygen-poor blue). No style, classDef, click or HTML.
            Other Mermaid kinds are drawn too, when they fit better: stateDiagram-v2 for states and the events between them (an automaton: [*] --> q0, q0 --> q1 : a, class q2 accept), sequenceDiagram for who sends what to whom in order (A->>B: request, B-->>A: reply), timeline for events in order (1857 : event), and mindmap for a topic's themes by indentation.
            """;
        if (svg)
            text += $$"""


                Draw something spatial (a labelled structure, a physics setup with its forces, a circuit, the graph of a function, a data structure in memory) as SVG instead, in a ```svg block: {{SvgRules}} For example:
                ```svg
                {{SvgExample}}
                ```
                """;
        text += "\n\nThe examples show the form only: write and draw only what this lecture teaches.";
        return text.ReplaceLineEndings("\n");
    }

    /// <summary>One answer from a local model, its context sized and its output capped. <paramref name="json"/> holds
    /// it to one JSON object (the diagram designer's reply); <paramref name="timeout"/> is how long it may take.</summary>
    public static async Task<string> OllamaGenerateAsync(Config cfg, string model, string prompt, int numCtx,
        HttpClient? http = null, TimeSpan? timeout = null, bool json = false, CancellationToken ct = default)
    {
        int numPredict = Math.Min(MaxNotesTokens, numCtx / 2);
        var body = new JsonObject
        {
            ["model"] = model,
            ["messages"] = new JsonArray(new JsonObject { ["role"] = "user", ["content"] = prompt }),
            ["stream"] = false,
            ["think"] = false,
            // A mild repeat penalty: stronger ones mangle Markdown, whose bullets legitimately repeat.
            ["options"] = new JsonObject
            {
                ["temperature"] = 0.2, ["num_ctx"] = numCtx, ["num_predict"] = numPredict,
                ["repeat_penalty"] = 1.1, ["repeat_last_n"] = 256,
            },
        };
        if (json) body["format"] = "json";
        // A long lecture on a big model, or a slow computer, takes minutes; fine, this runs in the background.
        // Output is capped, so this only has to cover reading the transcript plus 4,096 tokens.
        var data = await Ollama.PostAsync(cfg.OllamaHost, "/api/chat", body, timeout ?? TimeSpan.FromSeconds(900), http, ct);
        if (Py.AsString(data?["done_reason"]) == "length")
            throw new RunawayOutputException($"{model} kept writing past {numPredict} tokens without finishing "
                + "(small models sometimes loop), so no notes were written from it");
        return Py.AsString(data?["message"]?["content"]) ?? throw new InvalidDataException("Ollama sent no message");
    }

    /// <summary>The same line over and over: a model stuck in a loop, not notes. Only the prose counts: a finished
    /// code block, formula or diagram repeats its lines on purpose (a flowchart's <c>end</c>s, an SVG's shapes).</summary>
    public static bool Repetitive(string text, int times = 5)
    {
        var all = Py.SplitLines(text);
        var inBlock = new bool[all.Count];
        foreach (var b in NoteBlocks.Find(all).Where(b => b.Closed))
            for (int k = b.First; k <= b.Last; k++) inBlock[k] = true;
        var lines = all.Where((_, k) => !inBlock[k]).Select(Py.Strip).Where(l => l.Length >= 12).ToList();
        if (lines.Count == 0) return false;
        var counts = lines.GroupBy(l => l, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Count());
        return counts.Values.Max() >= times || (lines.Count >= 20 && counts.Count < lines.Count / 2.0);
    }

    /// <summary>The model's own context length, from Ollama's /api/show.</summary>
    public static async Task<int?> ModelContextAsync(Config cfg, string model)
    {
        try
        {
            var data = await Ollama.PostAsync(cfg.OllamaHost, "/api/show", new JsonObject { ["model"] = model },
                TimeSpan.FromSeconds(10));
            if (data?["model_info"] is JsonObject info)
                foreach (var (key, value) in info)
                    if (key.EndsWith(".context_length", StringComparison.Ordinal) && value is JsonValue v)
                        return v.GetValueKind() == JsonValueKind.Number ? (int)v.GetValue<double>() : int.Parse(Py.Str(v));
        }
        catch (Exception)
        {
            // Ollama not running, an old version, or a model it doesn't know: use our own cap.
        }
        return null;
    }

    public static async Task<int> ContextSizeAsync(Config cfg, string model, ShowFn? show = null)
    {
        int cap = Math.Max(4096, cfg.SummaryMaxContext);
        int? native = await (show ?? ModelContextAsync)(cfg, model);
        return native is int n && n != 0 ? Math.Min(cap, n) : cap;
    }

    /// <summary>Characters of transcript that fit in one call, leaving room for instructions and the answer.</summary>
    public static int TranscriptBudget(int ctx)
    {
        int reserved = Math.Min(4096, ctx / 3);
        return (int)((ctx - reserved) * CharsPerToken);
    }

    public static bool WantsSummary(Meeting m, Config cfg) =>
        cfg.OllamaEnabled && cfg.SummaryEnabled && Py.Strip(m.Transcript).Length >= MinTranscriptChars;

    [GeneratedRegex("<think>.*?</think>", RegexOptions.Singleline)]
    internal static partial Regex Thinking();

    [GeneratedRegex(@"\A```(markdown|md)?\s*\n(.*)\n```\z", RegexOptions.Singleline)]
    private static partial Regex Fenced();

    [GeneratedRegex(@"(?<=[.!?])\s+")]
    private static partial Regex SentenceEnd();

    public static string CleanOutput(string? text)
    {
        string t = Py.Strip(Unwrapped(Py.Strip(Thinking().Replace(text ?? "", ""))));
        if (Repetitive(t))
            throw new RunawayOutputException("the model repeated itself instead of writing notes, so no notes were written from it");
        return t;
    }

    /// <summary>
    /// Notes the model wrapped whole in a fence: a ```markdown one always comes off. A bare one comes off only when it
    /// really wraps the whole note (counting the fences inside it, which open with a word and close bare) and what it
    /// holds isn't a diagram — so a note that merely starts with a diagram or a code block is kept as it is.
    /// </summary>
    static string Unwrapped(string t)
    {
        var fence = Fenced().Match(t);
        if (!fence.Success) return t;
        string inner = fence.Groups[2].Value;
        if (fence.Groups[1].Success) return inner;
        if (NoteBlocks.StartsAsDiagram(inner)) return t;
        var lines = NoteBlocks.Lines(t);
        int depth = 0;
        for (int k = 0; k < lines.Length; k++)
        {
            if (NoteBlocks.Fence(lines[k]) is not { Char: '`' } f) continue;
            depth += k == 0 || f.Info.Length > 0 ? 1 : -1;
            if (depth == 0) return k == lines.Length - 1 ? inner : t;
        }
        return t;
    }

    /// <summary>Break one over-long line at sentence ends, then at spaces, then anywhere.</summary>
    internal static List<string> Pieces(string line, int maxChars)
    {
        if (line.Length <= maxChars) return [line];
        var output = new List<string>();
        string cur = "";
        foreach (string s in SentenceEnd().Split(line))
        {
            string sentence = s;
            while (sentence.Length > maxChars)
            {
                int cut = sentence.LastIndexOf(' ', maxChars - 1);
                cut = cut > maxChars / 2 ? cut : maxChars;
                string head = sentence[..cut];
                sentence = Py.LStrip(sentence[cut..]);
                if (cur.Length > 0)
                {
                    output.Add(cur);
                    cur = "";
                }
                output.Add(head);
            }
            if (cur.Length > 0 && cur.Length + 1 + sentence.Length > maxChars)
            {
                output.Add(cur);
                cur = "";
            }
            cur = cur.Length > 0 ? $"{cur} {sentence}" : sentence;
        }
        if (cur.Length > 0) output.Add(cur);
        return output;
    }

    /// <summary>Split on line breaks into parts of at most maxChars.</summary>
    public static List<string> SplitTranscript(string text, int maxChars)
    {
        var parts = new List<string>();
        var cur = new List<string>();
        int size = 0;
        foreach (string raw in Py.SplitLines(text))
        {
            foreach (string line in Pieces(raw, maxChars))
            {
                if (cur.Count > 0 && size + line.Length + 1 > maxChars)
                {
                    parts.Add(string.Join("\n", cur));
                    cur = [];
                    size = 0;
                }
                cur.Add(line);
                size += line.Length + 1;
            }
        }
        if (cur.Count > 0) parts.Add(string.Join("\n", cur));
        return parts.Where(p => Py.Strip(p).Length > 0).ToList();
    }

    internal static string Header(Meeting m)
    {
        var lines = new List<string> { $"Lecture: {m.Title}", $"Date: {Py.Head(m.Date, 10)}" };
        if (m.Folder.Length > 0) lines.Add($"Recorded for: {m.Folder}");
        return string.Join("\n", lines);
    }

    public static string WholePrompt(Meeting m, string transcript, Drawings drawings = Drawings.Flowcharts, string attached = "") =>
        "You are an expert note-taker for university lectures. Write study notes for this lecture "
        + $"from its transcript.\n\n{Structure}\n\n{Drawing(drawings)}\n\n{Rules}\n\n{Header(m)}{AttachedPart(attached)}\n\nTranscript:\n{transcript}";

    /// <summary>What the student attached, under its own heading, for the notes to draw on; nothing when there's none.</summary>
    static string AttachedPart(string attached) => attached.Length == 0 ? ""
        : "\n\nThe student attached these to the lecture. Use them to get names, terms, formulas and slide titles right, and to "
          + "cover what the student wrote down, but write the notes from the lecture itself:\n\n" + attached;

    /// <summary>What the student attached gets at most a third of a call's room; the transcript keeps the rest.</summary>
    const int AttachedShare = 3;

    public static string PartPrompt(Meeting m, string part, int i, int n) =>
        $"You are taking notes on part {i} of {n} of a lecture transcript. Write detailed Markdown bullet "
        + "notes for this part only: every concept, definition, example, formula, and announcement, in order. "
        + "They will be merged with the other parts later, so write no introduction or conclusion.\n\n"
        + $"{Rules}\n\n{Header(m)}\n\nTranscript part {i} of {n}:\n{part}";

    public static string CondensePrompt(Meeting m, IReadOnlyList<string> notes)
    {
        string joined = string.Join("\n\n", notes.Select((n, i) => $"### Notes {i + 1}\n{n}"));
        return "Combine these notes on consecutive parts of one lecture into one set of detailed Markdown bullet "
            + $"notes. Remove repetition but keep every distinct fact.\n\n{Rules}\n\n{Header(m)}\n\n{joined}";
    }

    public static string MergePrompt(Meeting m, IReadOnlyList<string> notes, Drawings drawings = Drawings.Flowcharts, string attached = "")
    {
        string joined = string.Join("\n\n", notes.Select((n, i) => $"### Part {i + 1}\n{n}"));
        return "You are an expert note-taker for university lectures. Below are notes on consecutive parts of one "
            + "lecture. Merge them into one set of study notes, removing repetition and keeping every distinct "
            + $"fact.\n\n{Structure}\n\n{Drawing(drawings)}\n\n{Rules}\n\n{Header(m)}{AttachedPart(attached)}\n\n{joined}";
    }

    /// <summary>Our own study notes for a lecture, written from its transcript, drawing what <paramref name="drawings"/>
    /// allows; a diagram that came out broken is sent back once to be fixed (<see cref="RepairDiagramsAsync"/>).</summary>
    public static async Task<string> SummarizeTranscriptAsync(Meeting m, Config cfg, ChatFn? chat = null, ShowFn? show = null,
        Drawings drawings = Drawings.Flowcharts)
    {
        chat ??= (c, model, prompt, ctx) => OllamaGenerateAsync(c, model, prompt, ctx);
        string model = cfg.EffectiveSummaryModel;
        int ctx = await ContextSizeAsync(cfg, model, show);
        int budget = TranscriptBudget(ctx);
        // What the student attached takes part of the room (never more than a third), and the transcript the rest.
        string attached = Attachments.Context(m.Attached, Math.Min(Attachments.MaxContextChars, budget / AttachedShare));
        budget -= attached.Length;
        string text = Py.Strip(TimedText.Plain(m.Transcript)); // a recording's times would only distract the model
        // A transcript that says who's speaking says what that means, once, before the words (and in each part of a long one).
        string hint = Speakers.HasLabels(text) ? Speakers.NotesHint + "\n\n" : "";
        text = hint + text;
        Task<string> Repaired(string notes) => RepairDiagramsAsync(notes, prompt => chat(cfg, model, prompt, ctx));
        if (text.Length <= budget) return await Repaired(CleanOutput(await chat(cfg, model, WholePrompt(m, text, drawings, attached), ctx)));
        var parts = SplitTranscript(text[hint.Length..], budget).Select(part => hint + part).ToList();
        var notes = new List<string>();
        for (int i = 0; i < parts.Count; i++)
            notes.Add(CleanOutput(await chat(cfg, model, PartPrompt(m, parts[i], i + 1, parts.Count), ctx)));
        // Very long lectures: fold pairs of part-notes together until the merge fits in one call.
        while (notes.Count > 1 && notes.Sum(n => n.Length) > budget)
        {
            var folded = new List<string>();
            for (int i = 0; i < notes.Count; i += 2)
                folded.Add(i + 1 < notes.Count ? CleanOutput(await chat(cfg, model, CondensePrompt(m, notes[i..(i + 2)]), ctx)) : notes[i]);
            notes = folded;
        }
        return await Repaired(CleanOutput(await chat(cfg, model, MergePrompt(m, notes, drawings, attached), ctx)));
    }

    /// <summary>How many broken diagrams one note sends back to be fixed.</summary>
    public const int MaxDiagramRepairs = 2;

    /// <summary>
    /// One repair round for the diagrams a note drew: each ```mermaid flowchart that doesn't parse and each ```svg
    /// drawing that can't be made safe (at most <see cref="MaxDiagramRepairs"/>) goes back to the same engine once,
    /// with what's wrong, and is replaced only by a reply that parses or cleans. Never throws (but for cancelling),
    /// never fails the notes: a diagram still broken after this shows its source in the note.
    /// </summary>
    public static async Task<string> RepairDiagramsAsync(string notes, Func<string, Task<string>> ask)
    {
        var lines = NoteBlocks.Lines(notes).ToList();
        // A fence never closed may have swallowed the rest of the note: replacing it could lose that, so it stays.
        var broken = NoteBlocks.Find(lines).Where(b => b.IsDiagram && b.Closed).Select(b => (Block: b, Why: DiagramProblem(b.Kind, b.Text)))
            .Where(x => x.Why is not null).Take(MaxDiagramRepairs).ToList();
        if (broken.Count == 0) return notes;
        var fixes = new List<(NoteBlock Block, string Text)>();
        foreach (var (block, why) in broken)
        {
            bool svg = block.Kind == NoteBlockKind.Svg;
            string what = svg ? "SVG drawing" : NoteBlocks.StartsAsFlowchart(block.Text) ? "Mermaid flowchart" : "Mermaid diagram";
            string prompt = $"This {what} has a problem: {why!.TrimEnd('.')}. "
                + $"Reply with only the corrected block.\n\n```{(svg ? "svg" : "mermaid")}\n{block.Text}\n```";
            try
            {
                if (Corrected(await ask(prompt), block.Kind) is { } text) fixes.Add((block, text));
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                // The engine couldn't answer: the note keeps its diagram as written, and shows its source.
            }
        }
        foreach (var (block, text) in fixes.OrderByDescending(f => f.Block.First))
        {
            string first = lines[block.First], indent = first[..(first.Length - first.TrimStart().Length)];
            var fresh = new List<string> { indent + "```" + (block.Kind == NoteBlockKind.Svg ? "svg" : "mermaid") };
            fresh.AddRange(text.Split('\n').Select(l => l.Length == 0 ? l : indent + l));
            fresh.Add(indent + "```");
            lines.RemoveRange(block.First, block.Last - block.First + 1);
            lines.InsertRange(block.First, fresh);
        }
        return string.Join("\n", lines);
    }

    /// <summary>Why a diagram can't be drawn, in plain English, or null when it can.</summary>
    public static string? DiagramProblem(NoteBlockKind kind, string text)
    {
        if (kind == NoteBlockKind.Svg) return SafeSvg.Clean(text).Problem;
        try
        {
            Flowchart.Parse(text);
            return null;
        }
        catch (MermaidException e)
        {
            return e.Message;
        }
    }

    /// <summary>The diagram in a repair's reply (fenced or not), when it's now one that can be drawn.</summary>
    static string? Corrected(string reply, NoteBlockKind kind)
    {
        string t = Py.Strip(Thinking().Replace(reply ?? "", ""));
        string candidate = NoteBlocks.Find(t).FirstOrDefault(b => b.Kind == kind)?.Text
            ?? (NoteBlocks.StartsAsDiagram(t) ? t : "");
        candidate = candidate.Trim('\n', '\r');
        return candidate.Trim().Length > 0 && DiagramProblem(kind, candidate) is null ? candidate : null;
    }
}
