using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;

namespace StudyStash.Core.Rich;

/// <summary>One file an export saves beside its Markdown file: a diagram's picture, at a path relative to it.</summary>
public sealed record ExportAsset(string RelativePath, string Text);

/// <summary>A lecture as a Markdown file: its name, its content, and the diagram pictures it points at.</summary>
public sealed record ExportFile(string FileName, string Markdown, IReadOnlyList<ExportAsset> Assets);

/// <summary>
/// A lecture written out as Markdown that opens well in Obsidian, Typora and VS Code: front matter, its notes with
/// their formulas as plain LaTeX and their diagrams drawn as a picture beside the file, and (asked for) its
/// transcript. Pure: nothing here touches a disk or a library, so the app and the library computer share it.
/// </summary>
public static class NoteExport
{
    /// <summary>The lecture's Markdown file. <paramref name="mermaidSvg"/> draws a Mermaid flowchart it already knows
    /// parses (its source in, a standalone SVG out, or null when it couldn't be drawn) — the caller measures text in
    /// its own font, so the picture matches what the app would show.</summary>
    public static ExportFile Lecture(JsonObject lecture, bool transcript, Func<string, string?> mermaidSvg)
    {
        string title = S(lecture["title"]) is { Length: > 0 } t ? t : "Untitled";
        string className = S(lecture["class"]);
        string dateRaw = S(lecture["date"]);
        double seconds = lecture["seconds"] is JsonValue v && v.TryGetValue(out double s) ? s : 0;
        string notes = S(lecture["notes"]);
        string rawTranscript = S(lecture["transcript"]);
        var topics = (lecture["topics"] as JsonArray)?.Select(S).Where(x => x.Length > 0).ToList() ?? [];

        string stem = $"{Notes.DatePrefix(dateRaw)} {Notes.Slugify(title)}";
        var assets = new List<ExportAsset>();
        int diagramIndex = 0;
        string body = notes.Length == 0 ? "_No notes yet._" : DrawDiagrams(MathText.DollarDelimiters(notes), stem, mermaidSvg, assets, ref diagramIndex);

        var date = ParseDate(dateRaw);
        string duration = TimedText.Length(seconds);
        string meta = string.Join(" · ", new[] { className, date?.ToString("dddd d MMMM yyyy", CultureInfo.InvariantCulture) ?? "", duration }
            .Where(x => x.Length > 0));

        var md = new StringBuilder("---\n");
        md.Append("title: ").Append(PyJson.Dumps(title)).Append('\n');
        md.Append("class: ").Append(PyJson.Dumps(className)).Append('\n');
        if (date is { } d) md.Append("date: ").Append(d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Append('\n');
        md.Append("duration: ").Append(PyJson.Dumps(duration)).Append('\n');
        if (topics.Count > 0) md.Append("topics: ").Append(PyJson.Dumps(topics)).Append('\n');
        md.Append("source: Study Stash\n---\n\n");
        md.Append("# ").Append(title).Append("\n\n");
        if (meta.Length > 0) md.Append('*').Append(meta).Append("*\n\n");
        md.Append(body.TrimEnd('\n')).Append('\n');
        if (transcript && rawTranscript.Length > 0)
            md.Append("\n## Transcript\n\n").Append(TranscriptText(rawTranscript)).Append('\n');

        return new ExportFile(stem + ".md", md.ToString(), assets);
    }

    /// <summary>A name not already in <paramref name="taken"/>: as given, or with " (2)", " (3)"… before its
    /// extension, added to <paramref name="taken"/> either way.</summary>
    public static string UniqueName(string name, ISet<string> taken)
    {
        if (taken.Add(name)) return name;
        string ext = System.IO.Path.GetExtension(name), stem = name[..^ext.Length];
        for (int n = 2; ; n++)
        {
            string candidate = $"{stem} ({n}){ext}";
            if (taken.Add(candidate)) return candidate;
        }
    }

    static string TranscriptText(string transcript) => TimedText.HasTimes(transcript)
        ? string.Join("\n", TimedText.Parse(transcript).Select(seg => $"[{TimedText.Clock(seg.Start)}] {seg.Text}"))
        : transcript;

    static DateTimeOffset? ParseDate(string s) =>
        DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var d) ? d : null;

    static string S(JsonNode? n) => n is JsonValue v && v.TryGetValue(out string? s) ? s ?? "" : "";

    /// <summary>Rewrites a note's fenced diagrams for a saved file: a Mermaid flowchart that parses becomes canonical
    /// Mermaid (what mermaid.js reads back) with its picture saved beside the file, but no link to it (Obsidian
    /// already draws the fence — a link would show it twice); one that doesn't parse is kept exactly as written. A
    /// safe SVG becomes an image link to its cleaned, saved picture; an unsafe one becomes its reason, in italics.
    /// Everything else (code, formulas, prose) is untouched.</summary>
    static string DrawDiagrams(string notes, string stem, Func<string, string?> mermaidSvg, List<ExportAsset> assets, ref int diagramIndex)
    {
        string[] lines = NoteBlocks.Lines(notes);
        var output = new List<string>();
        int at = 0;
        foreach (var block in NoteBlocks.Find(lines))
        {
            for (; at < block.First; at++) output.Add(lines[at]);
            string indent = lines[block.First][..(lines[block.First].Length - lines[block.First].TrimStart().Length)];
            if (block.Kind == NoteBlockKind.Mermaid && block.Closed && TryParse(block.Text) is { } chart)
            {
                output.Add(indent + "```mermaid");
                foreach (string line in chart.ToSource().TrimEnd('\n').Split('\n')) output.Add(indent + line);
                output.Add(indent + "```");
                if (mermaidSvg(block.Text) is { Length: > 0 } svg)
                    assets.Add(new ExportAsset($"{stem}.assets/diagram-{++diagramIndex}.svg", svg));
            }
            else if (block.Kind == NoteBlockKind.Svg && block.Closed)
            {
                var cleaned = SafeSvg.Clean(block.Text);
                if (cleaned.Svg is { } svg)
                {
                    string path = $"{stem}.assets/diagram-{++diagramIndex}.svg";
                    assets.Add(new ExportAsset(path, svg));
                    output.Add($"{indent}![Diagram: {(cleaned.Title.Length > 0 ? cleaned.Title : "Diagram")}]({EncodePath(path)})");
                }
                else
                {
                    output.Add($"{indent}_{cleaned.Problem}_");
                }
            }
            else
            {
                for (int k = block.First; k <= block.Last; k++) output.Add(lines[k]);
            }
            at = block.Last + 1;
        }
        for (; at < lines.Length; at++) output.Add(lines[at]);
        return string.Join("\n", output);
    }

    static Flowchart? TryParse(string source)
    {
        try { return Mermaid.Parse(source); }
        catch (MermaidException) { return null; }
    }

    static string EncodePath(string path) => string.Join('/', path.Split('/').Select(Uri.EscapeDataString));
}
