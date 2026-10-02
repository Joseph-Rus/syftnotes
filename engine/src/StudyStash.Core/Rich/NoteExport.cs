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
    /// <summary>The name a lecture's file is saved under unless the student picks another: its day and title,
    /// "2026-09-23 The cardiac cycle.md".</summary>
    public static string FileName(JsonObject lecture) => SafeName($"{Notes.DatePrefix(S(lecture["date"]))} {Notes.Slugify(Title(lecture))}") + ".md";

    /// <summary>The name the Save dialog suggests for one lecture: its title, as a file any computer can keep, ending
    /// in <paramref name="extension"/> (".md", or ".pdf" for the notes on paper).</summary>
    public static string SaveName(string title, string extension = ".md") => SafeName(Notes.Slugify(title)) + extension;

    /// <summary>
    /// A name (already free of the characters no file name may hold: <see cref="Notes.Slugify"/>) that Windows saves
    /// exactly as written: no dots or spaces at its end (Windows quietly drops them, so a class "Chem." would land in a
    /// folder "Chem" and a second download couldn't find it), and not one of the names Windows keeps for its devices
    /// (CON, PRN, AUX, NUL, COM1 to COM9, LPT1 to LPT9, with an extension or without), which gets an underscore.
    /// </summary>
    public static string SafeName(string name)
    {
        string t = name.TrimEnd('.', ' ');
        if (t.Length == 0) return "Untitled";
        int dot = t.IndexOf('.');
        string stem = (dot < 0 ? t : t[..dot]).TrimEnd(' ');
        return Reserved.Contains(stem) ? stem + "_" + t[stem.Length..] : t;
    }

    static readonly HashSet<string> Reserved = new(
        new[] { "CON", "PRN", "AUX", "NUL" }.Concat(new[] { "COM", "LPT" }.SelectMany(p => "123456789¹²³".Select(d => p + d))),
        StringComparer.OrdinalIgnoreCase);

    /// <summary>The lecture's Markdown file. <paramref name="mermaidSvg"/> draws a Mermaid flowchart it already knows
    /// parses (its source in, a standalone SVG out, or null when it couldn't be drawn) — the caller measures text in
    /// its own font, so the picture matches what the app would show. <paramref name="fileName"/> is the name it will
    /// really be saved under (the one the Save dialog was given, or a class download's numbered one); its diagrams go
    /// in a folder named after it, so two lectures saved side by side never share one. Without it, the file keeps
    /// <see cref="FileName(JsonObject)"/>.</summary>
    public static ExportFile Lecture(JsonObject lecture, bool transcript, Func<string, string?> mermaidSvg, string? fileName = null)
    {
        string title = Title(lecture);
        string className = S(lecture["class"]);
        string dateRaw = S(lecture["date"]);
        double seconds = lecture["seconds"] is JsonValue v && v.TryGetValue(out double s) ? s : 0;
        string notes = S(lecture["notes"]);
        string rawTranscript = S(lecture["transcript"]);
        var topics = (lecture["topics"] as JsonArray)?.Select(S).Where(x => x.Length > 0).ToList() ?? [];

        string name = fileName is { Length: > 0 } ? System.IO.Path.GetFileName(fileName) : FileName(lecture);
        string stem = System.IO.Path.GetFileNameWithoutExtension(name) is { Length: > 0 } st ? st : name;
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

        // One kind of line ending throughout, whatever the library's computer wrote the notes or transcript with.
        return new ExportFile(name, md.ToString().ReplaceLineEndings("\n"), assets);
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

    static string Title(JsonObject lecture) => S(lecture["title"]) is { Length: > 0 } t ? t : "Untitled";

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
                    // An illustration's parts, each with what the drawing says of it, so the file still teaches them.
                    if (cleaned.Parts.Count > 0)
                    {
                        output.Add("");
                        // Plain words only: a name that looks like markup is shown, never run, by whatever opens the file.
                        static string Plain(string s) => s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("*", "\\*");
                        foreach (var part in cleaned.Parts)
                            output.Add($"{indent}- **{Plain(part.Name)}**" + (part.Note.Length > 0 ? $": {Plain(part.Note)}" : ""));
                    }
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

    /// <summary>A relative asset path as a Markdown link reads it: each piece (a title with a space or a
    /// parenthesis) percent-encoded, the "/" between them kept plain.</summary>
    public static string EncodePath(string path) => string.Join('/', path.Split('/').Select(Uri.EscapeDataString));
}
